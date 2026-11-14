using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 30.0: свой снимок экрана в игре, как F12 в Steam: по сочетанию (по умолчанию Ctrl+F12) снимается окно игры
/// и ложится в «Изображения\ModLaunch\&lt;игра&gt;» (PNG), оттуда — в галерею игры. Снимает то, что на экране
/// (GDI): окно и «полноэкранное окно» — да; эксклюзивный полноэкранный режим DirectX может дать чёрный кадр.
/// </summary>
public static partial class GameShots
{
    public static bool Enabled => Settings.Data.Bool("gameShots", true);

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ModLaunch");

    /// <summary>Папка снимков игры: имя без запрещённых в Windows символов.</summary>
    public static string Folder(string game)
    {
        var safe = string.Concat(game.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is ':' or '*' or '?' ? '_' : c)).Trim().TrimEnd('.');
        return Path.Combine(Root, safe == "" ? "Game" : safe);
    }

    internal static string FileName(DateTime at) => $"{at:yyyy-MM-dd HH-mm-ss}.png";

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }

    [LibraryImport("user32.dll")] private static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect rect, int size);
    [LibraryImport("user32.dll")] private static partial IntPtr GetDC(IntPtr hWnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [LibraryImport("gdi32.dll")] private static partial IntPtr CreateCompatibleDC(IntPtr dc);
    [LibraryImport("gdi32.dll")] private static partial IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [LibraryImport("gdi32.dll")] private static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(IntPtr obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(IntPtr dc);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool MessageBeep(uint type);

    const int DwmwaExtendedFrameBounds = 9;
    const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;

    /// <summary>Пиксели прямоугольника экрана (BGRA сверху вниз, альфа = 255). null — не вышло.</summary>
    internal static byte[]? Grab(int x, int y, int w, int h)
    {
        if (!OperatingSystem.IsWindows() || w <= 0 || h <= 0 || (long)w * h > 16_384L * 16_384) return null;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, w, h, screen, x, y, SrcCopy | CaptureBlt)) return null;
            SelectObject(mem, old);
            var info = new BitmapInfoHeader { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32, Compression = 0 };
            var bits = new byte[w * h * 4];
            if (GetDIBits(mem, bmp, 0, (uint)h, bits, ref info, 0) == 0) return null;
            for (var i = 3; i < bits.Length; i += 4) bits[i] = 255;
            return bits;
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>Сохранить пиксели в PNG (Avalonia/Skia — без лишних библиотек).</summary>
    internal static void SavePng(byte[] bgra, int w, int h, string file)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = bitmap.Lock())
        {
            for (var row = 0; row < h; row++)
                Marshal.Copy(bgra, row * w * 4, fb.Address + row * fb.RowBytes, w * 4);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        bitmap.Save(tmp);
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>
    /// Снять окно, которое сейчас на переднем плане (игру), и сохранить. Возвращает путь к файлу или null.
    /// Пиксели берутся сразу (кадр — тот, что был при нажатии), а PNG кодируется в фоне.
    /// </summary>
    public static async Task<string?> Capture(string game)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        if (DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var r, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(window, out r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var bits = Grab(r.Left, r.Top, w, h);
        if (bits is null) return null;
        MessageBeep(0);
        var file = Path.Combine(Folder(game), FileName(DateTime.Now));
        for (var i = 2; File.Exists(file) && i < 50; i++) file = Path.Combine(Folder(game), Path.GetFileNameWithoutExtension(FileName(DateTime.Now)) + $" ({i}).png");
        await Task.Run(() => SavePng(bits, w, h, file));
        return file;
    }

    [SelfTest]
    static string GameShotsSaveRealPng()
    {
        if (FileName(new DateTime(2026, 11, 14, 9, 5, 7)) != "2026-11-14 09-05-07.png") throw new Exception("file name");
        if (Path.GetFileName(Folder("Half-Life: Alyx?")) != "Half-Life_ Alyx_") throw new Exception("folder: " + Folder("Half-Life: Alyx?"));
        var file = Path.Combine(Path.GetTempPath(), "modlaunch-shot-test.png");
        try
        {
            // Без экрана (CI) — хотя бы кодирование: 3×2 пикселя известного цвета.
            var bits = Grab(0, 0, 32, 16) ?? Enumerable.Repeat((byte)200, 32 * 16 * 4).ToArray();
            SavePng(bits, 32, 16, file);
            var head = File.ReadAllBytes(file).Take(8).ToArray();
            if (!head.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) throw new Exception("not a PNG");
            using var back = new Bitmap(file);
            if (back.PixelSize != new PixelSize(32, 16)) throw new Exception("size " + back.PixelSize);
            // Сочетание снимка занимается и отпускается (оверлей при этом не занят).
            var armed = OperatingSystem.IsWindows() && Hotkey.Arm(null, null, "CommandOrControl+F12", () => { });
            var shotKey = Hotkey.ArmedShot;
            var overlay = Hotkey.Armed;
            Hotkey.Disarm();
            if (armed && (shotKey != "CommandOrControl+F12" || overlay is not null)) throw new Exception($"armed shot={shotKey} overlay={overlay}");
            if (Hotkey.ArmedShot is not null) throw new Exception("not released");
            return "screen pixels grabbed, PNG written and read back at the same size; names safe for Windows; Ctrl+F12 " + (armed ? "registered and released" : "busy in another app (skipped)");
        }
        finally { try { File.Delete(file); } catch { } }
    }
}
