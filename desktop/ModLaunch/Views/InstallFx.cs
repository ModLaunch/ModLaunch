using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Три вида анимации нажатия «Скачать» (9.5: лёгкие, страница не трогается).</summary>
public enum FxKind
{
    /// <summary>1: карточка мода взлетает от кнопки и по дуге улетает в значок «Загрузки».</summary>
    Arc = 1,
    /// <summary>2: светящаяся искра с хвостом летит от кнопки в «Загрузки».</summary>
    Spark = 2,
    /// <summary>3: карточка приподнимается, полоса загрузки пробегает по ней, и она сжимается в «Загрузки».</summary>
    Lift = 3,
}

/// <summary>Что показывает карточка: мод (картинка по ссылке) или игра (обложка).</summary>
public sealed record FxCard(string Title, string Subtitle, string? IconUrl = null, Games.GameDef? Game = null, string Glyph = Icons.Download);

/// <summary>
/// Анимации нажатия «Скачать» (и «Создать» в Creator Hub). 9.5: вместо эффектов на весь экран
/// (экран раскалывался, рамка расходилась по окну) — короткий полёт от нажатой кнопки к значку
/// «Загрузки» в шапке, как у браузеров. Слой анимации пропускает щелчки, страница не замирает,
/// полёт всегда внутри окна. Вид — случайный, один и тот же не больше двух раз подряд.
/// </summary>
public static class InstallFx
{
    public static bool Enabled => Look.Animations && Settings.Data.Bool("installFx", true);

    static readonly Random Rng = new();
    static FxKind? _last;
    static int _streak;
    static FxRun? _active;

    public static bool Active => _active is not null;

    /// <summary>Следующий вид: наугад; если один уже сыграл два раза подряд — третий раз точно другой.</summary>
    public static FxKind Next()
    {
        FxKind[] all = [FxKind.Arc, FxKind.Spark, FxKind.Lift];
        var pool = _last is FxKind last && _streak >= 2 ? all.Where(k => k != last).ToArray() : all;
        var kind = pool[Rng.Next(pool.Length)];
        if (kind == _last) _streak++;
        else { _last = kind; _streak = 1; }
        return kind;
    }

    /// <summary>Сыграть анимацию для мода, который начал ставиться. after — сразу (анимация ничего не ждёт).</summary>
    public static void Play(FxCard card, Job? job, Action? after = null)
    {
        after?.Invoke();
        var w = MainWindow.Current;
        if (w is null || !Enabled || Program.Screenshot || !w.IsVisible || w.WindowState == WindowState.Minimized) return;
        // Новая загрузка, пока летит прошлая, — прошлая сразу садится.
        _active?.Finish();
        FxRun? run = null;
        try { run = FxRun.Create(w, Next(), card); } catch { }
        if (run is null) return;
        _active = run;
        run.Ended += () => { if (_active == run) _active = null; };
        run.Start();
    }

    /// <summary>Esc — закончить сразу.</summary>
    public static void Skip() => _active?.Finish();

    /// <summary>Для снимков экрана: без таймера, кадр выбирается вручную (<see cref="FxRun.Seek"/>).</summary>
    public static FxRun? Demo(MainWindow w, FxKind kind, FxCard card, Point? from = null) => FxRun.Create(w, kind, card, from);

    /// <summary>Правило эскиза: «всё на рандоме, если одна анимация чаще 2 раз подряд — то другая со 100% шансом».</summary>
    [SelfTest]
    static string NeverThreeInARow()
    {
        var (last, streak) = (_last, _streak);
        try
        {
            _last = null; _streak = 0;
            var seen = new HashSet<FxKind>();
            var run = 0;
            FxKind? prev = null;
            for (var i = 0; i < 3000; i++)
            {
                var k = Next();
                seen.Add(k);
                run = k == prev ? run + 1 : 1;
                prev = k;
                if (run > 2) throw new Exception($"{k} played {run} times in a row");
            }
            if (seen.Count != 3) throw new Exception("not all kinds were picked: " + string.Join(", ", seen));
            return "3 download animations, never more than 2 of the same in a row";
        }
        finally { (_last, _streak) = (last, streak); }
    }

    /// <summary>Путь полёта не выходит за края окна, где бы ни нажали.</summary>
    [SelfTest]
    static string FlightStaysInside()
    {
        var size = new Size(1366, 800);
        var target = new Point(1300, 24);
        foreach (var from in new[] { new Point(5, 5), new Point(1360, 790), new Point(20, 780), new Point(683, 400) })
            for (var t = 0.0; t <= 1.0001; t += 0.05)
            {
                var p = FxRun.PathPoint(from, target, t, size, 230, 56);
                if (p.X < 0 || p.Y < 0 || p.X + 230 > size.Width + 0.5 || p.Y + 56 > size.Height + 0.5) throw new Exception($"{from} t={t:0.00}: {p}");
            }
        return "card stays inside 1366×800 from every corner";
    }
}

/// <summary>Один полёт «в загрузки».</summary>
public sealed class FxRun
{
    readonly MainWindow _w;
    readonly FxKind _kind;
    readonly Canvas _canvas = new() { IsHitTestVisible = false };
    readonly Border _card;
    readonly Border _bar;
    readonly List<Border> _trail = [];
    readonly Border _spark;
    readonly Border _ring;
    readonly Point _from;
    readonly Point _to;
    readonly Size _area;
    DispatcherTimer? _timer;
    DateTime _start;
    bool _done;

    public event Action? Ended;
    public FxKind Kind => _kind;

    const double CardW = 230, CardH = 56;
    public double Duration => _kind switch { FxKind.Spark => 620, FxKind.Lift => 900, _ => 820 };

    FxRun(MainWindow w, FxKind kind, FxCard card, Point from, Point to, Size area)
    {
        _w = w;
        _kind = kind;
        _from = from;
        _to = to;
        _area = area;
        var accent = Ui.Res("Brand") as ISolidColorBrush;
        var glow = accent?.Color ?? Color.Parse("#7B5CFF");

        Control icon = card.IconUrl is { Length: > 0 } url ? Ui.Thumb(url, card.Title, 36, 10, 96)
            : card.Game is { } g ? new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.GameImage(g, 96, art: Images.Art.Cover) }
            : new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(card.Glyph, 18, Ui.Res("Brand2")) };
        _bar = new Border { Height = 3, Width = 0, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"), HorizontalAlignment = HorizontalAlignment.Left };
        var words = Ui.Col(2,
            new TextBlock { Text = card.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 },
            new TextBlock { Text = card.Subtitle, FontSize = 11, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 });
        words.VerticalAlignment = VerticalAlignment.Center;
        _card = new Border
        {
            Width = CardW, Height = CardH, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 0, 12, 0),
            Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 30, Color = Color.FromArgb(150, 0, 0, 0) }, [new BoxShadow { Blur = 22, Color = Color.FromArgb(90, glow.R, glow.G, glow.B) }]),
            Child = new Grid { Children = { Ui.Row(10, icon, words), new Border { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-10, 0, -12, 0), ClipToBounds = true, CornerRadius = new CornerRadius(0, 0, 14, 14), Child = _bar } } },
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            IsVisible = kind != FxKind.Spark,
        };
        _spark = new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Spread = 3, Color = Color.FromArgb(230, glow.R, glow.G, glow.B) }),
            IsVisible = kind == FxKind.Spark,
        };
        if (kind == FxKind.Spark)
            for (var i = 0; i < 10; i++)
            {
                var d = new Border { Width = 9 - i * 0.6, Height = 9 - i * 0.6, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(glow), Opacity = 0 };
                _trail.Add(d);
                _canvas.Children.Add(d);
            }
        _ring = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(2.5), BorderBrush = new SolidColorBrush(glow),
            Opacity = 0, RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        };
        Canvas.SetLeft(_ring, to.X - 20);
        Canvas.SetTop(_ring, to.Y - 20);
        _canvas.Children.Add(_card);
        _canvas.Children.Add(_spark);
        _canvas.Children.Add(_ring);
    }

    /// <summary>Подготовить полёт: откуда (последнее нажатие), куда (значок «Загрузки»).</summary>
    public static FxRun? Create(MainWindow w, FxKind kind, FxCard card, Point? from = null)
    {
        var layer = w.FxLayer;
        var area = layer.Bounds.Size;
        if (area.Width < 200 || area.Height < 200) area = new Size(w.Bounds.Width, w.Bounds.Height);
        Point to;
        if (w.FxDownloads is { } dl && dl.IsVisible && dl.TranslatePoint(new Point(dl.Bounds.Width / 2, dl.Bounds.Height / 2), layer) is { } t) to = t;
        else to = new Point(area.Width - 120, 24);
        var start = from ?? (w.LastPressInWindow is { } p && w.TranslatePoint(p, layer) is { } q ? q : new Point(area.Width / 2, area.Height / 2));
        var run = new FxRun(w, kind, card, start, to, area);
        layer.Children.Clear();
        layer.Children.Add(run._canvas);
        layer.IsHitTestVisible = false;
        layer.IsVisible = true;
        run.Apply(0);
        return run;
    }

    public void Start()
    {
        _start = DateTime.UtcNow;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += (_, _) =>
        {
            var ms = (DateTime.UtcNow - _start).TotalMilliseconds;
            if (ms >= Duration + 420) { Finish(); return; }
            Apply(ms);
        };
        _timer.Start();
    }

    public void Seek(double ms) => Apply(ms);

    /// <summary>Убрать анимацию (и вернуть значок «Загрузки» в обычный вид).</summary>
    public void Finish()
    {
        if (_done) return;
        _done = true;
        _timer?.Stop();
        if (_w.FxDownloads is { } dl) dl.RenderTransform = null;
        _w.FxLayer.Children.Remove(_canvas);
        if (_w.FxLayer.Children.Count == 0) _w.FxLayer.IsVisible = false;
        Ended?.Invoke();
    }

    public void Close() => Finish();

    static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>
    /// Левый верхний угол карточки на дуге от точки нажатия к цели: дуга идёт вверх, но карточка
    /// всегда целиком внутри окна (у края кнопки — сдвигается внутрь).
    /// </summary>
    public static Point PathPoint(Point from, Point to, double t, Size area, double w, double h)
    {
        var lift = Math.Min(140, Math.Max(40, Math.Abs(from.Y - to.Y) * 0.35));
        var control = new Point((from.X + to.X) / 2, Math.Min(from.Y, to.Y) - lift);
        var u = 1 - t;
        var x = u * u * from.X + 2 * u * t * control.X + t * t * to.X;
        var y = u * u * from.Y + 2 * u * t * control.Y + t * t * to.Y;
        return new Point(Math.Clamp(x - w / 2, 0, Math.Max(0, area.Width - w)), Math.Clamp(y - h / 2, 0, Math.Max(0, area.Height - h)));
    }

    void Apply(double ms)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string Tf(double scale, double dy = 0) => $"translateY({dy.ToString(inv)}px) scale({scale.ToString(inv)})";
        var total = Duration;
        switch (_kind)
        {
            case FxKind.Arc:
            {
                // 0–180 мс: появляется у кнопки; дальше — дуга в «Загрузки», уменьшаясь.
                var appear = Math.Clamp(ms / 180, 0, 1);
                var t = EaseInOut(Math.Clamp((ms - 180) / (total - 180), 0, 1));
                var p = PathPoint(_from, _to, t, _area, CardW, CardH);
                Canvas.SetLeft(_card, p.X);
                Canvas.SetTop(_card, p.Y);
                var scale = (0.86 + 0.14 * EaseOut(appear)) * (1 - 0.82 * t);
                _card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(Tf(scale, -10 * EaseOut(appear) * (1 - t)));
                _card.Opacity = Math.Min(EaseOut(appear), 1 - 0.75 * Math.Pow(t, 3));
                _card.IsVisible = ms < total;
                break;
            }
            case FxKind.Spark:
            {
                var t = EaseInOut(Math.Clamp(ms / total, 0, 1));
                var p = PathPoint(_from, _to, t, _area, 14, 14);
                Canvas.SetLeft(_spark, p.X);
                Canvas.SetTop(_spark, p.Y);
                _spark.Opacity = ms < total ? Math.Min(1, ms / 80) : 0;
                for (var i = 0; i < _trail.Count; i++)
                {
                    var tt = EaseInOut(Math.Clamp((ms - (i + 1) * 22) / total, 0, 1));
                    var q = PathPoint(_from, _to, tt, _area, _trail[i].Width, _trail[i].Height);
                    Canvas.SetLeft(_trail[i], q.X + (14 - _trail[i].Width) / 2);
                    Canvas.SetTop(_trail[i], q.Y + (14 - _trail[i].Height) / 2);
                    _trail[i].Opacity = ms < total + i * 22 && ms > (i + 1) * 22 ? 0.55 * (1 - i / (double)_trail.Count) : 0;
                }
                break;
            }
            case FxKind.Lift:
            {
                // 0–300 мс: карточка приподнимается и по ней пробегает полоса; дальше — сжимается в «Загрузки».
                var rise = EaseOut(Math.Clamp(ms / 300, 0, 1));
                var t = EaseInOut(Math.Clamp((ms - 340) / (total - 340), 0, 1));
                var start = PathPoint(_from, _from, 0, _area, CardW, CardH);
                var risen = new Point(start.X, Math.Max(0, start.Y - 34 * rise));
                var end = PathPoint(_from, _to, 1, _area, CardW, CardH);
                var x = risen.X + (end.X - risen.X) * t;
                var y = risen.Y + (end.Y - risen.Y) * t - Math.Sin(t * Math.PI) * 30;
                Canvas.SetLeft(_card, Math.Clamp(x, 0, Math.Max(0, _area.Width - CardW)));
                Canvas.SetTop(_card, Math.Clamp(y, 0, Math.Max(0, _area.Height - CardH)));
                _bar.Width = CardW * Math.Clamp(ms / 420, 0, 1);
                _card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(Tf((0.92 + 0.1 * rise) * (1 - 0.84 * t)));
                _card.Opacity = Math.Min(rise * 1.4, 1 - 0.7 * Math.Pow(t, 3));
                _card.IsVisible = ms < total;
                break;
            }
        }
        // Посадка: кольцо расходится от значка «Загрузки», значок чуть подпрыгивает.
        var land = Math.Clamp((ms - total + 60) / 420, 0, 1);
        _ring.Opacity = land is > 0 and < 1 ? 0.8 * (1 - land) : 0;
        _ring.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"scale({(0.6 + 1.1 * EaseOut(land)).ToString(inv)})");
        if (_w.FxDownloads is { } dl)
        {
            var bump = land is > 0 and < 1 ? 1 + 0.16 * Math.Sin(land * Math.PI) : 1;
            dl.RenderTransform = Math.Abs(bump - 1) < 0.001 ? null : Avalonia.Media.Transformation.TransformOperations.Parse($"scale({bump.ToString(inv)})");
        }
    }
}
