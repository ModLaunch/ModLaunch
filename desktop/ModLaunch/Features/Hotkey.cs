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

    // 30.0: снимок экрана в игре (Features/GameShots.cs) — второе сочетание того же потока.
    public static readonly string[] ShotKeys = ["CommandOrControl+F12", "Alt+F12", "F12"];
    public static string? ArmedShot { get; private set; }
    public static string ShotKeyOf(string? value) => ShotKeys.Contains(value) ? value! : ShotKeys[0];

    /// <summary>F12 в игре Steam — снимок самого Steam, его не перехватываем: там берём Ctrl+F12.</summary>
    public static string EffectiveShot(string? chosen, bool steamGame) => ShotKeyOf(chosen) is "F12" && steamGame ? ShotKeys[0] : ShotKeyOf(chosen);

    public static string KeyOf(string? value) => Keys.Contains(value) ? value! : Keys[0];

    /// <summary>Какое сочетание занять в этой игре: Shift+Tab в игре Steam отдали бы у оверлея Steam — берём Shift+F1.</summary>
    public static string Effective(string? chosen, bool steamGame) => KeyOf(chosen) is "Shift+Tab" && steamGame ? "Shift+F1" : KeyOf(chosen);

    [SelfTest]
    static string ShiftTabFallsBackInSteamGames()
    {
        if (Parse("Shift+Tab") != (ModShift, 0x09u)) throw new Exception("Shift+Tab parse");
        if (Effective("Shift+Tab", steamGame: true) != "Shift+F1" || Effective("Shift+Tab", steamGame: false) != "Shift+Tab") throw new Exception("fallback");
        if (Effective("Alt+`", steamGame: true) != "Alt+`" || Effective("nonsense", false) != Keys[0]) throw new Exception("other keys");
        if (Parse("F12") != (0u, 0x7Bu) || Parse("CommandOrControl+F12") != (ModControl, 0x7Bu) || Parse("Alt+F12") != (ModAlt, 0x7Bu)) throw new Exception("F12 parse");
        if (EffectiveShot("F12", steamGame: true) != "CommandOrControl+F12" || EffectiveShot("F12", steamGame: false) != "F12" || EffectiveShot(null, false) != ShotKeys[0]) throw new Exception("shot keys");
        return "Shift+Tab on its own; in Steam games falls back to Shift+F1; screenshot F12 falls back to Ctrl+F12 there";
    }

    static (uint Mods, uint Vk) Parse(string key) => key switch
    {
        "Alt+`" => (ModAlt, 0xC0),
        "Shift+F1" => (ModShift, 0x70),
        "Shift+Tab" => (ModShift, 0x09),
        "F12" => (0, 0x7B),
        "CommandOrControl+F12" => (ModControl, 0x7B),
        "Alt+F12" => (ModAlt, 0x7B),
        _ => (ModControl | ModShift, 'M'),
    };

    /// <summary>
    /// Занять сочетания: key — оверлей (null — не занимать), shotKey — снимок экрана. По нажатию — onPress или onShot
    /// (в потоке интерфейса). Возвращает, занялось ли сочетание оверлея (или снимка, если оверлея нет).
    /// </summary>
    public static bool Arm(string? key, Action? onPress, string? shotKey = null, Action? onShot = null)
    {
        Disarm();
        if (!OperatingSystem.IsWindows()) return false;
        if (key is not null) key = KeyOf(key);
        if (shotKey is not null) shotKey = ShotKeyOf(shotKey);
        var shotOk = false;
        var mainOk = false;
        var ok = new TaskCompletionSource<bool>();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            if (key is not null && onPress is not null)
            {
                var (mods, vk) = Parse(key);
                mainOk = RegisterHotKey(IntPtr.Zero, 1, mods | ModNoRepeat, vk);
            }
            if (shotKey is not null && onShot is not null)
            {
                var (mods, vk) = Parse(shotKey);
                shotOk = RegisterHotKey(IntPtr.Zero, 2, mods | ModNoRepeat, vk);
            }
            if (!mainOk && !shotOk) { ok.TrySetResult(false); return; }
            ok.TrySetResult(true);
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.Message == WmHotkey) Avalonia.Threading.Dispatcher.UIThread.Post(msg.WParam == 2 ? onShot! : onPress!);
            if (mainOk) UnregisterHotKey(IntPtr.Zero, 1);
            if (shotOk) UnregisterHotKey(IntPtr.Zero, 2);
        }) { IsBackground = true, Name = "ModLaunch hotkey" };
        _thread.Start();
        var armed = ok.Task.Wait(2000) && ok.Task.Result;
        // Оверлей считается занятым, только если его сочетание действительно зарегистрировано (снимок мог занять его один).
        Armed = armed && mainOk ? key : null;
        ArmedShot = armed && shotOk ? shotKey : null;
        return armed;
    }

    public static void Disarm()
    {
        if (_thread is null) return;
        PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
        Armed = null;
        ArmedShot = null;
    }
}
