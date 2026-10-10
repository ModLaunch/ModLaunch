using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Восемь видов появления страницы игры (9.3).</summary>
public enum GameFx
{
    /// <summary>«Портал» — игра открывается кругом из места нажатия, по краю бежит светящееся кольцо.</summary>
    Portal = 1,
    /// <summary>«Жалюзи» — прежний экран переворачивается полосками слева направо.</summary>
    Blinds = 2,
    /// <summary>«Осколки» — прежний экран разлетается кусками от места нажатия.</summary>
    Shatter = 3,
    /// <summary>«Глитч» — экран «сбоит» полосами и цветными помехами и переключается на игру.</summary>
    Glitch = 4,
    /// <summary>«Гиперпрыжок» — пролёт сквозь прежний экран, лучи скорости от центра.</summary>
    Warp = 5,
    /// <summary>«Обложка» — нажатая обложка растёт в арт игры во весь экран, и из него проступает страница.</summary>
    Expand = 6,
    /// <summary>«Срез» — игра проявляется диагональной полосой со светящейся кромкой.</summary>
    Wipe = 7,
    /// <summary>«Створки» — прежний экран раскрывается двумя створками от середины.</summary>
    Doors = 8,
}

/// <summary>
/// Анимации открытия игры: какая — решает случай, но одна и та же не играет больше двух раз
/// подряд (как у модов и «Скачать»). Работают по снимку прежнего экрана: кадры считаются вручную,
/// двигаются только картинки и прозрачность — раскладка страницы не пересчитывается. Снимок
/// освобождается сразу после анимации (через полсекунды–секунду), страховка — 3 секунды.
/// </summary>
public static class GameOpenFx
{
    public static bool Enabled => Animate.On && Settings.Data.Bool("gameOpenFx", true);
    public static readonly GameFx[] All = Enum.GetValues<GameFx>();

    static readonly Random Rng = new();
    static GameFx? _last;
    static int _streak;
    static GameOpenRun? _active;

    public static GameFx Next()
    {
        var pool = _last is GameFx last && _streak >= 2 ? All.Where(k => k != last).ToArray() : All;
        var kind = pool[Rng.Next(pool.Length)];
        if (kind == _last) _streak++;
        else { _last = kind; _streak = 1; }
        return kind;
    }

    /// <summary>Снимок области страницы в пикселях экрана (до того, как в ней появится новая страница).</summary>
    public static RenderTargetBitmap? Snapshot(Control area, MainWindow w)
    {
        var size = area.Bounds.Size;
        if (size.Width < 120 || size.Height < 120) return null;
        try
        {
            var k = Math.Max(1, w.RenderScaling) * Math.Max(0.5, w.Scale);
            var px = new PixelSize(Math.Max(1, (int)Math.Round(size.Width * k)), Math.Max(1, (int)Math.Round(size.Height * k)));
            var shot = new RenderTargetBitmap(px, new Vector(96 * k, 96 * k));
            shot.Render(area);
            return shot;
        }
        catch { return null; }
    }

    /// <summary>
    /// Сыграть появление страницы игры. shot — снимок прежнего экрана, origin — точка нажатия,
    /// from — нажатая обложка (в координатах области страницы). С force — без таймера (для снимков).
    /// </summary>
    public static GameOpenRun? Play(MainWindow w, Control page, RenderTargetBitmap? shot, Point? origin, Rect? from, Games.GameDef? game, GameFx? force = null)
    {
        if (shot is null || (force is null && !Enabled)) { if (shot is not null) DispatcherTimer.RunOnce(shot.Dispose, TimeSpan.FromSeconds(1)); Animate.PageIn(page); return null; }
        _active?.Finish();
        var run = new GameOpenRun(w, page, shot, force ?? Next(), origin, from, game);
        if (force is null)
        {
            _active = run;
            run.Ended += () => { if (_active == run) _active = null; };
            run.Start();
        }
        return run;
    }

    [SelfTest]
    static string NeverThreeInARow()
    {
        var (last, streak) = (_last, _streak);
        try
        {
            _last = null; _streak = 0;
            var seen = new HashSet<GameFx>();
            var run = 0;
            GameFx? prev = null;
            for (var i = 0; i < 5000; i++)
            {
                var k = Next();
                seen.Add(k);
                run = k == prev ? run + 1 : 1;
                prev = k;
                if (run > 2) throw new Exception($"{k} played {run} times in a row");
            }
            if (seen.Count != All.Length) throw new Exception("not all kinds were picked");
            return $"{All.Length} game opening animations, never more than 2 of the same in a row";
        }
        finally { (_last, _streak) = (last, streak); }
    }
}

/// <summary>Один показ анимации открытия игры: слои, кадр на момент времени и часы.</summary>
public sealed class GameOpenRun
{
    readonly MainWindow _w;
    readonly Control _page;
    readonly Panel _under, _over;
    readonly Canvas _stage = new() { IsHitTestVisible = false };
    readonly Canvas _back = new() { IsHitTestVisible = false };
    RenderTargetBitmap? _shot;
    readonly Size _size;
    readonly double _k;
    readonly Point _o;
    Action<double> _frame = _ => { };
    readonly List<Action> _restore = [];
    DispatcherTimer? _timer;
    readonly System.Diagnostics.Stopwatch _clock = new();
    bool _done;

    public GameFx Kind { get; }
    public double Length { get; private set; } = 700;
    public event Action? Ended;

    internal GameOpenRun(MainWindow w, Control page, RenderTargetBitmap shot, GameFx kind, Point? origin, Rect? from, Games.GameDef? game)
    {
        _w = w; _page = page; _shot = shot; Kind = kind;
        _under = w.PageUnder;
        _over = w.PageFx;
        _size = w.PageHost.Bounds.Size;
        if (_size.Width < 120 || _size.Height < 120) _size = new Size(shot.Size.Width, shot.Size.Height);
        _k = shot.PixelSize.Width / Math.Max(1, _size.Width);
        _o = origin ?? new Point(_size.Width / 2, _size.Height * 0.4);
        foreach (var c in new[] { _stage, _back }) { c.Width = _size.Width; c.Height = _size.Height; }
        _over.Children.Clear();
        _under.Children.Clear();
        _over.Children.Add(_stage);
        _under.Children.Add(_back);
        _over.IsVisible = _under.IsVisible = true;

        switch (kind)
        {
            case GameFx.Portal: Portal(); break;
            case GameFx.Blinds: Blinds(); break;
            case GameFx.Shatter: Shatter(); break;
            case GameFx.Glitch: Glitch(); break;
            case GameFx.Warp: Warp(); break;
            case GameFx.Expand: Expand(from, game); break;
            case GameFx.Wipe: Wipe(); break;
            default: Doors(); break;
        }
        Seek(0);
    }

    // ---------------------------------------------------------------- общее

    static double Clamp01(double v) => Math.Clamp(v, 0, 1);
    static double Phase(double ms, double start, double length) => Clamp01((ms - start) / length);
    static double CubicOut(double k) => 1 - Math.Pow(1 - k, 3);
    static double CubicInOut(double k) => k < 0.5 ? 4 * k * k * k : 1 - Math.Pow(-2 * k + 2, 3) / 2;
    static double QuadIn(double k) => k * k;
    static double BackOut(double k) { const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(k - 1, 3) + c1 * Math.Pow(k - 1, 2); }

    Color Accent => Gx.Accent;

    /// <summary>Кусок снимка прежнего экрана как картинка на сцене.</summary>
    Image Slice(Rect r, Canvas on)
    {
        r = r.Intersect(new Rect(_size));
        var max = _shot!.PixelSize;
        var x = Math.Clamp((int)Math.Floor(r.X * _k), 0, max.Width - 1);
        var y = Math.Clamp((int)Math.Floor(r.Y * _k), 0, max.Height - 1);
        var w = Math.Clamp((int)Math.Ceiling(r.Width * _k), 1, max.Width - x);
        var h = Math.Clamp((int)Math.Ceiling(r.Height * _k), 1, max.Height - y);
        var img = new Image { Source = new CroppedBitmap(_shot, new PixelRect(x, y, w, h)), Stretch = Stretch.Fill, Width = r.Width, Height = r.Height, IsHitTestVisible = false };
        Canvas.SetLeft(img, r.X);
        Canvas.SetTop(img, r.Y);
        on.Children.Add(img);
        return img;
    }

    Image Whole(Canvas on)
    {
        var img = new Image { Source = _shot, Stretch = Stretch.Fill, Width = _size.Width, Height = _size.Height, IsHitTestVisible = false };
        on.Children.Add(img);
        return img;
    }

    /// <summary>Новой странице на время анимации — непрозрачный фон (её обрезают, а под ней — снимок).</summary>
    void OpaquePage()
    {
        if (_page is not TemplatedControl tc) return;
        var had = tc.IsSet(TemplatedControl.BackgroundProperty);
        var was = tc.Background;
        tc.Background = Ui.Res("Layer");
        _restore.Add(() => { if (had) tc.Background = was; else tc.ClearValue(TemplatedControl.BackgroundProperty); });
    }

    (ScaleTransform S, TranslateTransform T) PageTransform(RelativePoint? origin = null)
    {
        var s = new ScaleTransform();
        var t = new TranslateTransform();
        var was = _page.RenderTransform;
        var wasOrigin = _page.RenderTransformOrigin;
        _page.RenderTransformOrigin = origin ?? RelativePoint.Center;
        _page.RenderTransform = new TransformGroup { Children = { s, t } };
        _restore.Add(() => { _page.RenderTransform = was; _page.RenderTransformOrigin = wasOrigin; });
        return (s, t);
    }

    void PageOpacity()
    {
        var own = _page.IsSet(Visual.OpacityProperty);
        var was = _page.Opacity;
        _restore.Add(() => { if (own) _page.Opacity = was; else _page.ClearValue(Visual.OpacityProperty); });
    }

    void PageClip()
    {
        var was = _page.Clip;
        _restore.Add(() => _page.Clip = was);
    }

    double Diagonal(Point p) => new[] { new Point(0, 0), new Point(_size.Width, 0), new Point(0, _size.Height), new Point(_size.Width, _size.Height) }
        .Max(c => Math.Sqrt((c.X - p.X) * (c.X - p.X) + (c.Y - p.Y) * (c.Y - p.Y)));

    // ---------------------------------------------------------------- 1. Портал

    void Portal()
    {
        Length = 720;
        Whole(_back);
        OpaquePage();
        PageClip();
        var (s, _) = PageTransform(new RelativePoint(_o.X / _size.Width, _o.Y / _size.Height, RelativeUnit.Relative));
        var reach = Diagonal(_o) + 30;
        var ring = new Border { BorderThickness = new Thickness(3), BorderBrush = new SolidColorBrush(Accent), BoxShadow = new BoxShadows(new BoxShadow { Blur = 26, Spread = 2, Color = Gx.Alpha(Accent, 220) }) };
        var ring2 = new Border { BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)) };
        _stage.Children.Add(ring);
        _stage.Children.Add(ring2);
        var flash = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White, BoxShadow = new BoxShadows(new BoxShadow { Blur = 40, Spread = 12, Color = Gx.Alpha(Accent, 255) }) };
        Canvas.SetLeft(flash, _o.X - 8);
        Canvas.SetTop(flash, _o.Y - 8);
        _stage.Children.Add(flash);
        _frame = ms =>
        {
            var k = CubicInOut(Phase(ms, 60, 560));
            var r = Math.Max(0.5, reach * k);
            _page.Clip = new EllipseGeometry(new Rect(_o.X - r, _o.Y - r, r * 2, r * 2));
            s.ScaleX = s.ScaleY = 1.06 - 0.06 * k;
            void Place(Border b, double rr)
            {
                b.Width = b.Height = rr * 2;
                b.CornerRadius = new CornerRadius(rr);
                Canvas.SetLeft(b, _o.X - rr);
                Canvas.SetTop(b, _o.Y - rr);
            }
            Place(ring, r + 2);
            Place(ring2, Math.Max(1, r - 10));
            ring.Opacity = k < 0.85 ? 1 : (1 - k) / 0.15;
            ring2.Opacity = ring.Opacity * 0.7;
            var f = Phase(ms, 0, 200);
            flash.Opacity = f < 0.5 ? f * 2 : 2 - f * 2;
            flash.RenderTransform = new ScaleTransform(1 + f * 2, 1 + f * 2);
        };
    }

    // ---------------------------------------------------------------- 2. Жалюзи

    void Blinds()
    {
        Length = 760;
        const int n = 12;
        var w = _size.Width / n;
        var strips = new List<(Image View, ScaleTransform S, double Delay)>();
        var (ps, _) = PageTransform();
        var edge = new List<Border>();
        for (var i = 0; i < n; i++)
        {
            var img = Slice(new Rect(i * w, 0, w + 1, _size.Height), _stage);
            var st = new ScaleTransform();
            img.RenderTransform = st;
            img.RenderTransformOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative);
            strips.Add((img, st, i * 34));
            var line = new Border { Width = 2, Height = _size.Height, Background = new SolidColorBrush(Accent), BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Gx.Alpha(Accent, 200) }), Opacity = 0 };
            Canvas.SetLeft(line, (i + 1) * w - 1);
            _stage.Children.Add(line);
            edge.Add(line);
        }
        _frame = ms =>
        {
            for (var i = 0; i < strips.Count; i++)
            {
                var (view, st, delay) = strips[i];
                var k = CubicInOut(Phase(ms, delay, 360));
                st.ScaleX = 1 - k;
                view.Opacity = 1 - k * 0.5;
                edge[i].Opacity = k > 0 && k < 1 ? Math.Sin(k * Math.PI) : 0;
                Canvas.SetLeft(edge[i], (i + 1) * w - 1 - k * w);
            }
            var p = CubicOut(Phase(ms, 80, 620));
            ps.ScaleX = ps.ScaleY = 0.96 + 0.04 * p;
        };
    }

    // ---------------------------------------------------------------- 3. Осколки

    void Shatter()
    {
        Length = 820;
        var cols = 9;
        var rows = 6;
        var cw = _size.Width / cols;
        var ch = _size.Height / rows;
        var rng = new Random(7);
        var reach = Diagonal(_o);
        var (ps, _) = PageTransform(new RelativePoint(_o.X / _size.Width, _o.Y / _size.Height, RelativeUnit.Relative));
        var pieces = new List<(Image View, TranslateTransform T, RotateTransform R, ScaleTransform S, double Dx, double Dy, double Rot, double Delay)>();
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < cols; x++)
            {
                var r = new Rect(x * cw, y * ch, cw + 1, ch + 1);
                var img = Slice(r, _stage);
                var t = new TranslateTransform();
                var ro = new RotateTransform();
                var sc = new ScaleTransform();
                img.RenderTransform = new TransformGroup { Children = { sc, ro, t } };
                img.RenderTransformOrigin = RelativePoint.Center;
                var c = r.Center;
                var dx = c.X - _o.X;
                var dy = c.Y - _o.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var len = Math.Max(1, dist);
                var push = 260 + rng.NextDouble() * 260;
                pieces.Add((img, t, ro, sc, dx / len * push, dy / len * push + 120 + rng.NextDouble() * 160, (rng.NextDouble() - 0.5) * 140, dist / reach * 260));
            }
        _frame = ms =>
        {
            foreach (var (view, t, ro, sc, dx, dy, rot, delay) in pieces)
            {
                var k = Phase(ms, delay, 480);
                var e = QuadIn(k) * 0.6 + CubicOut(k) * 0.4;
                t.X = dx * e;
                t.Y = dy * e;
                ro.Angle = rot * e;
                sc.ScaleX = sc.ScaleY = 1 - 0.45 * e;
                view.Opacity = 1 - k;
            }
            var p = CubicOut(Phase(ms, 120, 640));
            ps.ScaleX = ps.ScaleY = 1.05 - 0.05 * p;
        };
    }

    // ---------------------------------------------------------------- 4. Глитч

    void Glitch()
    {
        Length = 640;
        const int bands = 16;
        var bh = _size.Height / bands;
        var views = new List<(Image View, int Index)>();
        for (var i = 0; i < bands; i++) views.Add((Slice(new Rect(0, i * bh, _size.Width, bh + 1), _stage), i));
        // Цветные помехи: полосы цвета акцента и бирюзовые — мелькают поверх.
        var noise = new List<Border>();
        for (var i = 0; i < 10; i++)
        {
            var b = new Border { Height = 2 + i % 4 * 3, Width = _size.Width, Background = new SolidColorBrush(i % 2 == 0 ? Gx.Alpha(Accent, 200) : Color.FromArgb(190, 40, 230, 230)), Opacity = 0 };
            _stage.Children.Add(b);
            noise.Add(b);
        }
        var (_, pt) = PageTransform();
        static double Hash(int a, int b) { var h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) / (double)uint.MaxValue; }
        _frame = ms =>
        {
            var tick = (int)(ms / 45);
            var strength = ms < 300 ? 0.4 + ms / 300 * 0.6 : 1;
            var fade = Phase(ms, 300, 220);
            foreach (var (view, i) in views)
            {
                var j = Hash(i, tick);
                var shift = j > 0.55 ? (Hash(tick, i) - 0.5) * 90 * strength : 0;
                Canvas.SetLeft(view, ms <= 0 ? 0 : shift);
                view.Opacity = (1 - fade) * (j > 0.93 ? 0.4 : 1);
            }
            for (var i = 0; i < noise.Count; i++)
            {
                var b = noise[i];
                var on = ms > 0 && Hash(i + 50, tick) > 0.45 && ms < 520;
                b.Opacity = on ? 0.85 * (1 - fade * 0.6) : 0;
                Canvas.SetTop(b, Hash(i, tick + 3) * _size.Height);
                Canvas.SetLeft(b, (Hash(tick, i + 9) - 0.5) * 60);
            }
            // Новая страница «встаёт на место» коротким дрожанием.
            var settle = Phase(ms, 300, 300);
            pt.X = settle is > 0 and < 1 ? Math.Sin(settle * 30) * 10 * (1 - settle) : 0;
        };
    }

    // ---------------------------------------------------------------- 5. Гиперпрыжок

    void Warp()
    {
        Length = 760;
        var old = Whole(_stage);
        var os = new ScaleTransform();
        old.RenderTransform = os;
        old.RenderTransformOrigin = new RelativePoint(_o.X / _size.Width, _o.Y / _size.Height, RelativeUnit.Relative);
        PageOpacity();
        var (ps, _) = PageTransform(new RelativePoint(_o.X / _size.Width, _o.Y / _size.Height, RelativeUnit.Relative));
        var rng = new Random(11);
        var streaks = new List<(Border View, double Angle, double Start, double Speed)>();
        for (var i = 0; i < 34; i++)
        {
            var angle = rng.NextDouble() * Math.PI * 2;
            var b = new Border { Height = 2, Width = 10, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(i % 3 == 0 ? Gx.Alpha(Accent, 255) : Color.FromArgb(230, 255, 255, 255)), RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative) };
            _stage.Children.Add(b);
            streaks.Add((b, angle, 30 + rng.NextDouble() * 80, 0.7 + rng.NextDouble() * 0.8));
        }
        var reach = Diagonal(_o);
        _frame = ms =>
        {
            var k = Phase(ms, 0, 460);
            os.ScaleX = os.ScaleY = 1 + QuadIn(k) * 0.9;
            old.Opacity = 1 - CubicOut(k);
            var p = CubicOut(Phase(ms, 160, 560));
            ps.ScaleX = ps.ScaleY = 0.82 + 0.18 * p;
            _page.Opacity = p;
            foreach (var (view, angle, start, speed) in streaks)
            {
                var t = Phase(ms, 0, 520);
                var d = start + reach * speed * QuadIn(t);
                var len = 20 + 260 * t * speed;
                view.Width = len;
                view.Opacity = t is > 0 and < 1 ? Math.Sin(t * Math.PI) : 0;
                view.RenderTransform = new RotateTransform(angle * 180 / Math.PI);
                Canvas.SetLeft(view, _o.X + Math.Cos(angle) * d);
                Canvas.SetTop(view, _o.Y + Math.Sin(angle) * d - 1);
            }
        };
    }

    // ---------------------------------------------------------------- 6. Обложка

    void Expand(Rect? from, Games.GameDef? game)
    {
        Length = 780;
        Whole(_back);
        PageOpacity();
        var (_, pt) = PageTransform();
        var start = from is Rect r && r.Width > 20 && r.Height > 20 ? r : new Rect(_o.X - 80, _o.Y - 120, 160, 240);
        var end = new Rect(_size);
        Control art = game is null ? Gx.Gradient(null) : Ui.GameImage(game, 1600, art: Images.Art.Hero);
        var card = new Border
        {
            ClipToBounds = true, Child = new Panel { Children = { art, new Border { Background = Gx.ShadeUp(0.5, 180) } } },
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 40, OffsetY = 18, Color = Color.FromArgb(150, 0, 0, 0) }),
            BorderBrush = new SolidColorBrush(Gx.Alpha(Accent, 220)), BorderThickness = new Thickness(2),
        };
        _stage.Children.Add(card);
        var shine = new Border { Width = 120, Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) } }, RenderTransform = new SkewTransform(-20, 0) };
        _stage.Children.Add(shine);
        _frame = ms =>
        {
            var k = CubicInOut(Phase(ms, 0, 460));
            var x = start.X + (end.X - start.X) * k;
            var y = start.Y + (end.Y - start.Y) * k;
            var w = start.Width + (end.Width - start.Width) * k;
            var h = start.Height + (end.Height - start.Height) * k;
            card.Width = Math.Max(1, w);
            card.Height = Math.Max(1, h);
            card.CornerRadius = new CornerRadius(12 * (1 - k));
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            var f = Phase(ms, 420, 320);
            card.Opacity = 1 - CubicOut(f);
            var sh = Phase(ms, 140, 420);
            shine.Height = h;
            Canvas.SetTop(shine, y);
            Canvas.SetLeft(shine, x - 140 + (w + 280) * sh);
            shine.Opacity = sh is > 0 and < 1 ? card.Opacity : 0;
            var p = CubicOut(Phase(ms, 360, 380));
            _page.Opacity = p;
            pt.Y = 24 * (1 - p);
        };
    }

    // ---------------------------------------------------------------- 7. Срез

    void Wipe()
    {
        Length = 700;
        Whole(_back);
        OpaquePage();
        PageClip();
        var tilt = _size.Height * 0.38;
        var band = new Border
        {
            Width = 6, Height = Math.Sqrt(_size.Height * _size.Height + tilt * tilt) + 40, Background = new SolidColorBrush(Accent),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Spread = 4, Color = Gx.Alpha(Accent, 230) }),
            RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative), RenderTransform = new RotateTransform(Math.Atan2(tilt, _size.Height) * 180 / Math.PI),
        };
        var glow = new Border { Width = 2, Height = band.Height, Background = Brushes.White, RenderTransformOrigin = band.RenderTransformOrigin, RenderTransform = new RotateTransform(Math.Atan2(tilt, _size.Height) * 180 / Math.PI) };
        _stage.Children.Add(band);
        _stage.Children.Add(glow);
        var (ps, _) = PageTransform();
        _frame = ms =>
        {
            var k = CubicInOut(Phase(ms, 40, 560));
            // Кромка идёт слева направо; наверху она впереди на tilt.
            var x = -tilt + (_size.Width + tilt * 2) * k;
            var poly = new PolylineGeometry(new Points { new Point(0, 0), new Point(x + tilt, 0), new Point(x, _size.Height), new Point(0, _size.Height) }, true);
            _page.Clip = poly;
            Canvas.SetLeft(band, x + tilt - 3);
            Canvas.SetTop(band, -20);
            Canvas.SetLeft(glow, x + tilt - 1);
            Canvas.SetTop(glow, -20);
            band.Opacity = glow.Opacity = k is > 0 and < 1 ? 1 : 0;
            ps.ScaleX = ps.ScaleY = 1.03 - 0.03 * k;
        };
    }

    // ---------------------------------------------------------------- 8. Створки

    void Doors()
    {
        Length = 760;
        var half = _size.Width / 2;
        var left = Slice(new Rect(0, 0, half + 1, _size.Height), _stage);
        var right = Slice(new Rect(half, 0, half, _size.Height), _stage);
        var lt = new TranslateTransform();
        var rt = new TranslateTransform();
        var ls = new ScaleTransform();
        var rs = new ScaleTransform();
        left.RenderTransform = new TransformGroup { Children = { ls, lt } };
        right.RenderTransform = new TransformGroup { Children = { rs, rt } };
        left.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        right.RenderTransformOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative);
        var seam = new Border { Width = 4, Height = _size.Height, Background = Brushes.White, BoxShadow = new BoxShadows(new BoxShadow { Blur = 40, Spread = 8, Color = Gx.Alpha(Accent, 255) }) };
        Canvas.SetLeft(seam, half - 2);
        _stage.Children.Add(seam);
        var (ps, _) = PageTransform();
        _frame = ms =>
        {
            var s = Phase(ms, 0, 160);
            seam.Opacity = s < 1 ? s : 1 - Phase(ms, 160, 260);
            var k = CubicInOut(Phase(ms, 120, 520));
            lt.X = -half * 1.05 * k;
            rt.X = half * 1.05 * k;
            ls.ScaleY = rs.ScaleY = 1 - 0.06 * k;
            ls.ScaleX = rs.ScaleX = 1 - 0.25 * k;
            left.Opacity = right.Opacity = 1 - k * 0.6;
            var p = BackOut(Phase(ms, 160, 600));
            ps.ScaleX = ps.ScaleY = 0.92 + 0.08 * p;
        };
    }

    // ---------------------------------------------------------------- часы

    public void Start()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
        {
            var ms = _clock.Elapsed.TotalMilliseconds;
            Seek(ms);
            if (ms >= Length) Finish();
        });
        // Часы — когда новая страница уже разложена: иначе первые кадры «съест» раскладка.
        Dispatcher.UIThread.Post(() =>
        {
            if (_done) return;
            _clock.Start();
            _timer.Start();
        }, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(Finish, TimeSpan.FromSeconds(3));
    }

    public void Seek(double ms) { if (!_done) _frame(ms); }

    public void Finish()
    {
        if (_done) return;
        _done = true;
        _timer?.Stop();
        foreach (var r in _restore) r();
        _stage.Children.Clear();
        _back.Children.Clear();
        _over.Children.Clear();
        _under.Children.Clear();
        _over.IsVisible = _under.IsVisible = false;
        // Снимок большой — отпускаем, как только последний кадр с ним точно нарисован.
        if (_shot is { } shot) DispatcherTimer.RunOnce(shot.Dispose, TimeSpan.FromSeconds(2));
        _shot = null;
        Ended?.Invoke();
    }
}
