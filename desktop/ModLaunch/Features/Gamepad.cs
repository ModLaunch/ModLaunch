using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModLaunch.Features;

public enum Pad { Up, Down, Left, Right, A, B, X, Y, LB, RB, Start, Back, LT, RT }

/// <summary>
/// Геймпад через XInput (Xbox и всё, что им притворяется, — DualSense через
/// Steam/DS4Windows тоже). Опрос раз в кадр: нажатия с автоповтором для
/// стрелок и сырые значения стиков — для управления мышью.
/// </summary>
public sealed partial class Gamepad
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XState
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }

    [LibraryImport("xinput1_4.dll")] private static partial uint XInputGetState(uint index, out XState state);

    const ushort DUp = 0x1, DDown = 0x2, DLeft = 0x4, DRight = 0x8, BStart = 0x10, BBack = 0x20, BLB = 0x100, BRB = 0x200, BA = 0x1000, BB = 0x2000, BX = 0x4000, BY = 0x8000;

    public event Action<Pad>? Pressed;
    public event Action<Pad>? Released;
    public XState State { get; private set; }
    public bool Connected { get; private set; }

    int _index = -1;
    long _nextScan;
    bool _broken;
    readonly HashSet<Pad> _down = [];
    readonly Dictionary<Pad, long> _repeatAt = [];
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>Опросить геймпад (вызывать из потока интерфейса ~60 раз в секунду).</summary>
    public void Tick()
    {
        if (_broken || !OperatingSystem.IsWindows()) return;
        var now = Clock.ElapsedMilliseconds;
        XState s = default;
        try
        {
            if (_index < 0)
            {
                // Пустые слоты XInput опрашиваются медленно — ищем геймпад не чаще раза в 2 секунды.
                if (now < _nextScan) { SetConnected(false); return; }
                _nextScan = now + 2000;
                for (uint i = 0; i < 4 && _index < 0; i++) if (XInputGetState(i, out s) == 0) _index = (int)i;
                if (_index < 0) { SetConnected(false); return; }
            }
            else if (XInputGetState((uint)_index, out s) != 0) { _index = -1; SetConnected(false); return; }
        }
        catch { _broken = true; return; }
        SetConnected(true);
        State = s;

        var now_ = new HashSet<Pad>();
        void Map(bool on, Pad p) { if (on) now_.Add(p); }
        const int Stick = 16000;
        Map((s.Buttons & DUp) != 0 || s.LY > Stick, Pad.Up);
        Map((s.Buttons & DDown) != 0 || s.LY < -Stick, Pad.Down);
        Map((s.Buttons & DLeft) != 0 || s.LX < -Stick, Pad.Left);
        Map((s.Buttons & DRight) != 0 || s.LX > Stick, Pad.Right);
        Map((s.Buttons & BA) != 0, Pad.A);
        Map((s.Buttons & BB) != 0, Pad.B);
        Map((s.Buttons & BX) != 0, Pad.X);
        Map((s.Buttons & BY) != 0, Pad.Y);
        Map((s.Buttons & BLB) != 0, Pad.LB);
        Map((s.Buttons & BRB) != 0, Pad.RB);
        Map((s.Buttons & BStart) != 0, Pad.Start);
        Map((s.Buttons & BBack) != 0, Pad.Back);
        Map(s.LeftTrigger > 120, Pad.LT);
        Map(s.RightTrigger > 120, Pad.RT);

        foreach (var p in now_)
        {
            if (_down.Add(p)) { _repeatAt[p] = now + 380; Pressed?.Invoke(p); }
            else if (p is Pad.Up or Pad.Down or Pad.Left or Pad.Right or Pad.LB or Pad.RB && now >= _repeatAt[p])
            {
                _repeatAt[p] = now + 110;
                Pressed?.Invoke(p);
            }
        }
        foreach (var p in _down.Where(p => !now_.Contains(p)).ToList()) { _down.Remove(p); Released?.Invoke(p); }
    }

    public bool IsDown(Pad p) => _down.Contains(p);

    public event Action<bool>? ConnectionChanged;
    void SetConnected(bool on)
    {
        if (Connected == on) return;
        Connected = on;
        if (!on) { State = default; _down.Clear(); }
        ConnectionChanged?.Invoke(on);
    }
}
