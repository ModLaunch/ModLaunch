using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Три вида появления страницы мода (9.2).</summary>
public enum OpenKind
{
    /// <summary>1: «Из карточки» — страница вырастает из того места, куда нажали.</summary>
    Zoom = 1,
    /// <summary>2: «Шторка» — страница въезжает справа, картинка мода «выпрыгивает» с поворотом.</summary>
    Slide = 2,
    /// <summary>3: «Каскад» — шапка, вкладки и описание раскрываются сверху вниз по очереди.</summary>
    Cascade = 3,
}

/// <summary>
/// Анимации открытия мода. Выбор — случайный, но одна и та же не играет больше двух раз подряд
/// (то же правило, что у анимаций «Скачать»). Кадры считаются вручную по таймеру: двигаются только
/// RenderTransform и Opacity, раскладка не пересчитывается — поэтому не тормозит. Пока анимация
/// идёт, страница мода не перестраивается (догрузившееся описание покажется сразу после неё).
/// </summary>
public static class ModOpenFx
{
    public static bool Enabled => Animate.On && Settings.Data.Bool("modOpenFx", true);

    static readonly Random Rng = new();
    static OpenKind? _last;
    static int _streak;
    static OpenRun? _active;

    /// <summary>Страница, у которой сейчас идёт анимация открытия (её перестройку откладываем).</summary>
    public static Control? Running => _active?.Page;

    /// <summary>Следующий вид: наугад; если один уже сыграл два раза подряд — третий раз точно другой.</summary>
    public static OpenKind Next()
    {
        OpenKind[] all = [OpenKind.Zoom, OpenKind.Slide, OpenKind.Cascade];
        var pool = _last is OpenKind last && _streak >= 2 ? all.Where(k => k != last).ToArray() : all;
        var kind = pool[Rng.Next(pool.Length)];
        if (kind == _last) _streak++;
        else { _last = kind; _streak = 1; }
        return kind;
    }

    /// <summary>
    /// Сыграть появление страницы мода. area — область, где лежит страница, origin — точка нажатия в ней.
    /// С force — указанный вид без таймера (для снимков: кадр выбирается через <see cref="OpenRun.Seek"/>).
    /// </summary>
    public static OpenRun? Play(Control page, Control area, Point? origin, OpenKind? force = null)
    {
        if (force is null && !Enabled) { Animate.PageIn(page); return null; }
        _active?.Finish();
        var run = new OpenRun(page, force ?? Next(), area, origin);
        if (force is null)
        {
            _active = run;
            run.Ended += () => { if (_active == run) _active = null; };
            run.Start();
        }
        return run;
    }

    /// <summary>Правило: «всё на рандоме, одна и та же — не больше двух раз подряд».</summary>
    [SelfTest]
    static string NeverThreeInARow()
    {
        var (last, streak) = (_last, _streak);
        try
        {
            _last = null; _streak = 0;
            var seen = new HashSet<OpenKind>();
            var run = 0;
            OpenKind? prev = null;
            for (var i = 0; i < 3000; i++)
            {
                var k = Next();
                seen.Add(k);
                run = k == prev ? run + 1 : 1;
                prev = k;
                if (run > 2) throw new Exception($"{k} played {run} times in a row");
            }
            if (seen.Count != 3) throw new Exception("not all kinds were picked: " + string.Join(", ", seen));
            return "3 mod opening animations, never more than 2 of the same in a row";
        }
        finally { (_last, _streak) = (last, streak); }
    }
}

/// <summary>Один показ анимации открытия мода: набор «дорожек» (что, откуда, когда) и часы.</summary>
public sealed class OpenRun
{
    /// <summary>Одна дорожка: элемент едет из начального положения в своё.</summary>
    sealed class Track
    {
        public required Control View;
        public double Delay, Duration = 480, Dx, Dy, Scale = 1, ScaleY = 1, Rotate, Opacity;
        public Func<double, double> Ease = OpenRun.CubicOut;
        public RelativePoint Origin = RelativePoint.Center;
        readonly TranslateTransform _t = new();
        readonly ScaleTransform _s = new();
        readonly RotateTransform _r = new();
        ITransform? _was;
        RelativePoint _wasOrigin;
        double _wasOpacity;
        bool _ownOpacity;

        public double End => Delay + Duration;

        public void Init()
        {
            _was = View.RenderTransform;
            _wasOrigin = View.RenderTransformOrigin;
            _ownOpacity = View.IsSet(Visual.OpacityProperty);
            _wasOpacity = View.Opacity;
            View.RenderTransformOrigin = Origin;
            View.RenderTransform = new TransformGroup { Children = { _s, _r, _t } };
            At(0);
        }

        public void At(double ms)
        {
            var k = Math.Clamp((ms - Delay) / Duration, 0, 1);
            var e = Ease(k);
            var u = 1 - e;
            _t.X = Dx * u;
            _t.Y = Dy * u;
            _r.Angle = Rotate * u;
            _s.ScaleX = Scale + (1 - Scale) * e;
            _s.ScaleY = Scale * ScaleY + (1 - Scale * ScaleY) * e;
            // Прозрачность набирается быстрее движения: элемент виден уже в начале пути.
            var fade = Math.Clamp(k * 1.8, 0, 1);
            View.Opacity = _wasOpacity * (Opacity + (1 - Opacity) * fade);
        }

        public void Done()
        {
            View.RenderTransform = _was;
            View.RenderTransformOrigin = _wasOrigin;
            if (_ownOpacity) View.Opacity = _wasOpacity; else View.ClearValue(Visual.OpacityProperty);
        }
    }

    public Control Page { get; }
    public OpenKind Kind { get; }
    public event Action? Ended;
    readonly List<Track> _tracks = [];
    DispatcherTimer? _timer;
    readonly System.Diagnostics.Stopwatch _clock = new();
    bool _done;

    internal OpenRun(Control page, OpenKind kind, Control area, Point? origin)
    {
        Page = page;
        Kind = kind;
        var blocks = page is UserControl { Content: ScrollViewer { Content: StackPanel col } } ? col.Children.ToList() : [];
        var picture = Picture(page);
        void Add(Control c, Action<Track> setup) { var t = new Track { View = c }; setup(t); _tracks.Add(t); }

        switch (kind)
        {
            case OpenKind.Zoom:
            {
                var w = Math.Max(1, area.Bounds.Width);
                var h = Math.Max(1, area.Bounds.Height);
                var o = origin ?? new Point(w / 2, h / 3);
                Add(page, t => { t.Scale = 0.78; t.Duration = 460; t.Origin = new RelativePoint(Math.Clamp(o.X / w, 0, 1), Math.Clamp(o.Y / h, 0, 1), RelativeUnit.Relative); });
                if (picture is not null) Add(picture, t => { t.Scale = 0.6; t.Delay = 100; t.Duration = 560; t.Ease = BackOut; });
                break;
            }
            case OpenKind.Slide:
            {
                Add(page, t => { t.Dx = 140; t.Duration = 480; });
                if (picture is not null) Add(picture, t => { t.Scale = 0.4; t.Rotate = -14; t.Delay = 140; t.Duration = 620; t.Ease = BackOut; });
                for (var i = 1; i < blocks.Count && i < 4; i++) { var k = i; Add(blocks[i], t => { t.Dx = 70; t.Delay = 80 + k * 70; t.Duration = 520; }); }
                break;
            }
            default:
            {
                // Сама страница стоит, раскрываются её части — каждая «шторкой» сверху вниз.
                for (var i = 0; i < blocks.Count && i < 4; i++)
                {
                    var k = i;
                    Add(blocks[i], t => { t.Dy = -30; t.ScaleY = 0.8; t.Origin = new RelativePoint(0.5, 0, RelativeUnit.Relative); t.Delay = k * 110; t.Duration = 520; t.Ease = BackOut; });
                }
                if (picture is not null) Add(picture, t => { t.Rotate = -90; t.Scale = 0.5; t.Delay = 60; t.Duration = 640; t.Ease = BackOut; });
                break;
            }
        }
        foreach (var t in _tracks) t.Init();
    }

    public double Length => _tracks.Count == 0 ? 0 : _tracks.Max(t => t.End);

    public void Start()
    {
        if (_tracks.Count == 0) { Finish(); return; }
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
        {
            var ms = _clock.Elapsed.TotalMilliseconds;
            Seek(ms);
            if (ms >= Length) Finish();
        });
        // Часы пускаем, когда новая страница уже разложена и нарисована в начальном кадре:
        // иначе на тяжёлой странице первые сотни миллисекунд анимации «съедала» бы раскладка.
        Dispatcher.UIThread.Post(() =>
        {
            if (_done) return;
            _clock.Start();
            _timer.Start();
        }, DispatcherPriority.Background);
        // Страховка: что бы ни случилось с таймером, через 3 с страница стоит на месте.
        DispatcherTimer.RunOnce(Finish, TimeSpan.FromSeconds(3));
    }

    /// <summary>Поставить кадр на момент ms (для снимков экрана).</summary>
    public void Seek(double ms) { foreach (var t in _tracks) t.At(ms); }

    /// <summary>Закончить сразу: всё на своих местах, отложенная перестройка страницы — сейчас.</summary>
    public void Finish()
    {
        if (_done) return;
        _done = true;
        _timer?.Stop();
        foreach (var t in _tracks) t.Done();
        Ended?.Invoke();
        if (Page is ModPage mp) mp.FlushDeferred();
    }

    static Control? Picture(Control page)
    {
        foreach (var v in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(page))
            if (v is Border b && b.Classes.Contains("mod-picture")) return b;
        return null;
    }

    internal static double CubicOut(double k) => 1 - Math.Pow(1 - k, 3);

    internal static double BackOut(double k)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(k - 1, 3) + c1 * Math.Pow(k - 1, 2);
    }
}
