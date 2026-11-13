using System.Runtime.InteropServices;

namespace ModLaunch.Features;

/// <summary>
/// Глобальное сочетание клавиш для оверлея (RegisterHotKey). Работает в своём
/// потоке с очередью сообщений Windows, поэтому не зависит от окон программы.
/// Занято только пока идёт игра, запущенная из ModLaunch.
/// </summary>
public static partial class Hotkey
{
    // 28.0: и Shift+Tab, как в Steam. В играх Steam его занимает оверлей Steam — там берём Shift+F1 (Effective).
    public static readonly string[] Keys = ["CommandOrControl+Shift+M", "Alt+`", "Shift+F1", "Shift+Tab"];
    public static string Display(string key) => key.Replace("CommandOrControl", "Ctrl");

    const int WmHotkey = 0x0312, WmQuit = 0x0012;
    const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModNoRepeat = 0x4000;

    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(out Msg msg, IntPtr hWnd, uint min, uint max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostThreadMessageW(uint thread, uint msg, IntPtr w, IntPtr l);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam; public IntPtr LParam; public uint Time; public int X; public int Y; }

    static Thread? _thread;
    static uint _threadId;
    public static string? Armed { get; private set; }

    public static string KeyOf(string? value) => Keys.Contains(value) ? value! : Keys[0];

    /// <summary>Какое сочетание занять в этой игре: Shift+Tab в игре Steam отдали бы у оверлея Steam — берём Shift+F1.</summary>
    public static string Effective(string? chosen, bool steamGame) => KeyOf(chosen) is "Shift+Tab" && steamGame ? "Shift+F1" : KeyOf(chosen);

    [SelfTest]
    static string ShiftTabFallsBackInSteamGames()
    {
        if (Parse("Shift+Tab") != (ModShift, 0x09u)) throw new Exception("Shift+Tab parse");
        if (Effective("Shift+Tab", steamGame: true) != "Shift+F1" || Effective("Shift+Tab", steamGame: false) != "Shift+Tab") throw new Exception("fallback");
        if (Effective("Alt+`", steamGame: true) != "Alt+`" || Effective("nonsense", false) != Keys[0]) throw new Exception("other keys");
        return "Shift+Tab on its own; in Steam games falls back to Shift+F1";
    }

    static (uint Mods, uint Vk) Parse(string key) => key switch
    {
        "Alt+`" => (ModAlt, 0xC0),
        "Shift+F1" => (ModShift, 0x70),
        "Shift+Tab" => (ModShift, 0x09),
        _ => (ModControl | ModShift, 'M'),
    };

    /// <summary>Занять сочетание; по нажатию вызывается onPress (в потоке интерфейса).</summary>
    public static bool Arm(string key, Action onPress)
    {
        Disarm();
        if (!OperatingSystem.IsWindows()) return false;
        key = KeyOf(key);
        var ok = new TaskCompletionSource<bool>();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var (mods, vk) = Parse(key);
            if (!RegisterHotKey(IntPtr.Zero, 1, mods | ModNoRepeat, vk)) { ok.TrySetResult(false); return; }
            ok.TrySetResult(true);
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.Message == WmHotkey) Avalonia.Threading.Dispatcher.UIThread.Post(onPress);
            UnregisterHotKey(IntPtr.Zero, 1);
        }) { IsBackground = true, Name = "ModLaunch hotkey" };
        _thread.Start();
        var armed = ok.Task.Wait(2000) && ok.Task.Result;
        Armed = armed ? key : null;
        return armed;
    }

    public static void Disarm()
    {
        if (_thread is null) return;
        PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
        Armed = null;
    }
}
