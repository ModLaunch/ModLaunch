using System.Runtime.InteropServices;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 30.6: «Быстрый переход» из любой программы — глобальное сочетание (по умолчанию Ctrl+Alt+Пробел), пока открыт
/// ModLaunch (в том числе свёрнутый в трей). Своё окно сообщений в отдельном потоке, независимо от сочетаний
/// оверлея (те занимаются только во время игры). Сочетание занято другой программой — просто не работает,
/// и настройки об этом говорят.
/// </summary>
public static partial class QuickKey
{
    public static readonly string[] Keys = ["Ctrl+Alt+Space", "Ctrl+Alt+G", "Ctrl+Shift+Space"];

    const int WmHotkey = 0x0312, WmQuit = 0x0012, Id = 10;
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

    public static bool On => Settings.Data.Bool("quickKey", true);
    public static string Key => Keys.Contains(Settings.Data.Str("quickKeyCombo")) ? Settings.Data.Str("quickKeyCombo")! : Keys[0];

    /// <summary>Занято ли сейчас сочетание (false — выключено или его держит другая программа).</summary>
    public static bool Armed { get; private set; }

    public static string Display(string key) => key.Replace("Space", I18n.T("v30.qk.space"));

    internal static (uint Mods, uint Vk) Parse(string key) => key switch
    {
        "Ctrl+Shift+Space" => (ModControl | ModShift, 0x20),
        "Ctrl+Alt+G" => (ModControl | ModAlt, 'G'),
        _ => (ModControl | ModAlt, 0x20),
    };

    /// <summary>Какое сочетание занято на самом деле (без выбора вручную могло достаться следующее свободное).</summary>
    public static string ArmedKey { get; private set; } = Keys[0];

    /// <summary>
    /// Занять сочетание по настройкам (повторный вызов — переназначить). onPress — в потоке окна. Если сочетание
    /// не выбирали вручную и первое занято другой программой — берём следующее свободное из списка.
    /// </summary>
    public static void Arm(Action onPress)
    {
        Disarm();
        ArmedKey = Key;
        if (Program.Screenshot) { Armed = On; return; }
        if (!OperatingSystem.IsWindows() || !On) return;
        string[] candidates = Settings.Data.Str("quickKeyCombo") is null ? Keys : [Key];
        var ok = new TaskCompletionSource<bool>();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var registered = false;
            foreach (var key in candidates)
            {
                var (mods, vk) = Parse(key);
                if (!RegisterHotKey(IntPtr.Zero, Id, mods | ModNoRepeat, vk)) continue;
                ArmedKey = key;
                registered = true;
                break;
            }
            ok.TrySetResult(registered);
            if (!registered) return;
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.Message == WmHotkey) Avalonia.Threading.Dispatcher.UIThread.Post(onPress);
            UnregisterHotKey(IntPtr.Zero, Id);
        }) { IsBackground = true, Name = "ModLaunch quick key" };
        _thread.Start();
        Armed = ok.Task.Wait(2000) && ok.Task.Result;
    }

    public static void Disarm()
    {
        if (_thread is null) return;
        PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
        Armed = false;
    }

    [SelfTest]
    static string QuickKeysParse()
    {
        if (Parse("Ctrl+Alt+Space") != (ModControl | ModAlt, 0x20u)) throw new Exception("ctrl+alt+space");
        if (Parse("Ctrl+Shift+Space") != (ModControl | ModShift, 0x20u)) throw new Exception("ctrl+shift+space");
        if (Parse("Ctrl+Alt+G") != (ModControl | ModAlt, (uint)'G')) throw new Exception("ctrl+alt+g");
        if (Parse("nonsense") != Parse(Keys[0])) throw new Exception("fallback");
        return "three quick-jump combos map to RegisterHotKey modifiers and keys";
    }
}
