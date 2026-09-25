using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModLaunch.Features;

/// <summary>
/// Управление компьютером геймпадом, как в Steam: левый стик — мышь, правый —
/// прокрутка, A — левая кнопка, B — правая, X — экранная клавиатура, Y — меню
/// «Пуск», крестовина — стрелки, Start — Enter, LB/RB — Alt+Tab назад/вперёд.
/// Всё через SendInput — работает в любой программе на рабочем столе.
/// </summary>
public static unsafe partial class PcControl
{
    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public IntPtr Extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }

    [StructLayout(LayoutKind.Explicit)]
    struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }

    [StructLayout(LayoutKind.Sequential)]
    struct Input { public uint Type; public Union U; }

    [LibraryImport("user32.dll")] private static partial uint SendInput(uint count, Input* inputs, int size);
    [LibraryImport("powrprof.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetSuspendState([MarshalAs(UnmanagedType.Bool)] bool hibernate, [MarshalAs(UnmanagedType.Bool)] bool force, [MarshalAs(UnmanagedType.Bool)] bool wakeupEventsDisabled);

    const uint Move = 0x1, LeftDown = 0x2, LeftUp = 0x4, RightDown = 0x8, RightUp = 0x10, Wheel = 0x800, HWheel = 0x1000, KeyUp = 0x2;

    static double _mx, _my, _wheel, _hwheel;
    static bool _alt;

    static void Send(Input input)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { SendInput(1, &input, sizeof(Input)); } catch { }
    }

    static void MouseEvent(uint flags, int dx = 0, int dy = 0, int data = 0) =>
        Send(new Input { Type = 0, U = new Union { Mouse = new MouseInput { Dx = dx, Dy = dy, Data = (uint)data, Flags = flags } } });

    public static void Key(ushort vk, bool down) =>
        Send(new Input { Type = 1, U = new Union { Key = new KeyInput { Vk = vk, Flags = down ? 0 : KeyUp } } });

    public static void Tap(ushort vk) { Key(vk, true); Key(vk, false); }

    static double Curve(short v)
    {
        const double dead = 7849;
        var a = Math.Abs((double)v);
        if (a < dead) return 0;
        var n = (a - dead) / (32767 - dead);
        return Math.Sign(v) * n * n; // квадратичная кривая: точно при малом отклонении, быстро при полном
    }

    /// <summary>Кадр управления: dt — сколько секунд прошло с прошлого кадра.</summary>
    public static void Frame(Gamepad pad, double dt)
    {
        var s = pad.State;
        var speed = pad.IsDown(Pad.RT) ? 2600 : pad.IsDown(Pad.LT) ? 450 : 1500; // RT — быстрее, LT — точнее
        _mx += Curve(s.LX) * speed * dt;
        _my -= Curve(s.LY) * speed * dt;
        int dx = (int)_mx, dy = (int)_my;
        if (dx != 0 || dy != 0) { MouseEvent(Move, dx, dy); _mx -= dx; _my -= dy; }

        _wheel += Curve(s.RY) * 1400 * dt;
        _hwheel += Curve(s.RX) * 1400 * dt;
        if (Math.Abs(_wheel) >= 40) { MouseEvent(Wheel, data: (int)_wheel); _wheel = 0; }
        if (Math.Abs(_hwheel) >= 40) { MouseEvent(HWheel, data: (int)_hwheel); _hwheel = 0; }
    }

    /// <summary>Кнопки геймпада в режиме управления ПК.</summary>
    public static void Press(Pad p, bool down)
    {
        switch (p)
        {
            case Pad.A: MouseEvent(down ? LeftDown : LeftUp); break;
            case Pad.B: MouseEvent(down ? RightDown : RightUp); break;
            case Pad.X when down: Keyboard(); break;
            case Pad.Y when down: Tap(0x5B); break; // Win
            case Pad.Up: Key(0x26, down); break;
            case Pad.Down: Key(0x28, down); break;
            case Pad.Left: Key(0x25, down); break;
            case Pad.Right: Key(0x27, down); break;
            case Pad.Start: Key(0x0D, down); break;
            case Pad.LB or Pad.RB when down:
                if (!_alt) { Key(0x12, true); _alt = true; }
                if (p == Pad.LB) Key(0x10, true);
                Tap(0x09);
                if (p == Pad.LB) Key(0x10, false);
                break;
        }
    }

    /// <summary>Отпустить всё, что могло остаться нажатым (Alt после Alt+Tab, кнопки мыши).</summary>
    public static void ReleaseAll()
    {
        if (_alt) { Key(0x12, false); _alt = false; }
        MouseEvent(LeftUp);
        MouseEvent(RightUp);
    }

    /// <summary>Alt держится, пока крутим Alt+Tab; отпускаем, когда LB/RB отпущены.</summary>
    public static void AfterFrame(Gamepad pad)
    {
        if (_alt && !pad.IsDown(Pad.LB) && !pad.IsDown(Pad.RB)) { Key(0x12, false); _alt = false; }
    }

    public static void Keyboard()
    {
        try { Process.Start(new ProcessStartInfo("osk.exe") { UseShellExecute = true }); } catch { }
    }

    public static void Sleep() { try { SetSuspendState(false, false, false); } catch { } }
    public static void Restart() => Shutdown("/r /t 0");
    public static void PowerOff() => Shutdown("/s /t 0");
    static void Shutdown(string args)
    {
        try { Process.Start(new ProcessStartInfo("shutdown", args) { UseShellExecute = false, CreateNoWindow = true }); } catch { }
    }
}
