using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Три вида анимации нажатия «Скачать» (9.1, по эскизу).</summary>
public enum FxKind
{
    /// <summary>1: рамка расходится от мода к краям окна, экран растворяется, боковая панель уезжает влево.</summary>
    Frame = 1,
    /// <summary>2: экран раскалывается на куски вокруг мода, куски разлетаются в разные стороны.</summary>
    Shatter = 2,
    /// <summary>3: волна из левого верхнего угла (от логотипа) уносит экран плитками.</summary>
    Wave = 3,
}

/// <summary>Что показывает карточка в центре: мод (картинка по ссылке) или игра (обложка).</summary>
public sealed record FxCard(string Title, string Subtitle, string? IconUrl = null, Games.GameDef? Game = null, string Glyph = Icons.Download);

/// <summary>
/// Анимации нажатия «Скачать» (и «Создать» в Creator Hub). Экран снимается в картинку,
/// картинка режется на куски, и куски улетают — настоящие страницы при этом не двигаются,
/// поэтому анимация не тормозит. В центре остаётся карточка мода с прогрессом загрузки;
/// потом она улетает в значок загрузок, а экран возвращается.
/// Выбор вида — случайный, но один и тот же вид не играет больше двух раз подряд.
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
        FxKind[] all = [FxKind.Frame, FxKind.Shatter, FxKind.Wave];
        var pool = _last is FxKind last && _streak >= 2 ? all.Where(k => k != last).ToArray() : all;
        var kind = pool[Rng.Next(pool.Length)];
        if (kind == _last) _streak++;
        else { _last = kind; _streak = 1; }
        return kind;
    }

    /// <summary>Сыграть анимацию для мода, который начал ставиться.</summary>
    public static void Play(FxCard card, Job? job, Action? after = null)
    {
        var w = MainWindow.Current;
        if (w is null || !Enabled || Program.Screenshot || _active is not null || !w.IsVisible || w.WindowState == WindowState.Minimized)
        {
            after?.Invoke();
            return;
        }
        FxRun? run = null;
        try { run = FxRun.Create(w, Next(), card, job); } catch { }
        if (run is null) { after?.Invoke(); return; }
        _active = run;
        run.Ended += () => { _active = null; after?.Invoke(); };
        run.Start();
    }

    /// <summary>Пропустить (щелчок или Esc): карточка сразу улетает, экран возвращается.</summary>
    public static void Skip() => _active?.Skip();

    /// <summary>Для снимков экрана: анимация без таймера, кадр выбирается вручную (<see cref="FxRun.Seek"/>).</summary>
    public static FxRun? Demo(MainWindow w, FxKind kind, FxCard card) => FxRun.Create(w, kind, card, null, demo: true);

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
}

/// <summary>Один показ анимации загрузки.</summary>
public sealed class FxRun
{
    /// <summary>Кусок снимка экрана и куда он летит.</summary>
    sealed class Piece
    {
        public required Control View;
        public double Delay, Duration = 500, Dx, Dy, Rotate, Scale = 1, Opacity;
        public Func<double, double> Ease = FxRun.EaseIn;
        readonly TranslateTransform _t = new();
        readonly ScaleTransform _s = new();
        readonly RotateTransform _r = new();

        public void Init(RelativePoint origin)
        {
            View.RenderTransformOrigin = origin;
            View.RenderTransform = new TransformGroup { Children = { _s, _r, _t } };
        }

        public double End => Delay + Duration;

        public void At(double ms)
        {
            var k = Math.Clamp((ms - Delay) / Duration, 0, 1);
            var e = Ease(k);
            _t.X = Dx * e;
            _t.Y = Dy * e;
            _r.Angle = Rotate * e;
            _s.ScaleX = _s.ScaleY = 1 + (Scale - 1) * e;
            // Прозрачность уходит во второй половине пути: сначала видно, куда кусок полетел.
            var fade = Math.Clamp((k - 0.35) / 0.65, 0, 1);
            View.Opacity = 1 + (Opacity - 1) * fade;
        }
    }

    readonly MainWindow _w;
    readonly FxKind _kind;
    readonly FxCard _card;
    readonly Job? _job;
    readonly bool _demo;
    readonly Panel _layer;
    readonly Control _root;
    readonly Canvas _stage = new();
    readonly List<Piece> _pieces = [];
    readonly List<(Border Frame, double Delay)> _frames = [];
    readonly Border _glow = new() { IsHitTestVisible = false };
    readonly Border _cardView = new();
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = 6, MinHeight = 6 };
    readonly TextBlock _step = new() { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _skip = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TranslateTransform _cardMove = new();
    readonly ScaleTransform _cardScale = new();
    RenderTargetBitmap? _shot;
    DispatcherTimer? _timer;
    DateTime _t0;
    double _exit;
    double _returnAt = double.NaN;
    double _now;
    bool _done, _failed, _ended;
    Rect _all, _main, _rail, _cardRect;
    Point _target;

    public event Action? Ended;
    public FxKind Kind => _kind;

    const double CardW = 440, CardH = 132, ReturnMs = 560;

    FxRun(MainWindow w, FxKind kind, FxCard card, Job? job, bool demo)
    {
        _w = w; _kind = kind; _card = card; _job = job; _demo = demo;
        _layer = w.FxLayer;
        _root = w.FxRoot;
    }

    public static FxRun? Create(MainWindow w, FxKind kind, FxCard card, Job? job, bool demo = false)
    {
        var run = new FxRun(w, kind, card, job, demo);
        return run.Prepare() ? run : null;
    }

    // ---------------------------------------------------------------- подготовка: снимок и куски

    bool Prepare()
    {
        var size = _root.Bounds.Size;
        if (size.Width < 200 || size.Height < 200) return false;
        var k = Math.Max(1, _w.RenderScaling) * Math.Max(0.5, _w.Scale);
        var px = new PixelSize(Math.Max(1, (int)Math.Round(size.Width * k)), Math.Max(1, (int)Math.Round(size.Height * k)));
        _shot = new RenderTargetBitmap(px, new Vector(96 * k, 96 * k));
        _shot.Render(_root);

        _all = new Rect(size);
        _rail = _w.FxRail is { IsVisible: true } rail && rail.TranslatePoint(new Point(0, 0), _root) is Point at
            ? new Rect(at, rail.Bounds.Size) : default;
        _main = _rail.Width > 0
            ? (_rail.X < size.Width / 2 ? new Rect(_rail.Right, 0, size.Width - _rail.Right, size.Height) : new Rect(0, 0, _rail.X, size.Height))
            : _all;
        var c = _main.Center;
        _cardRect = new Rect(c.X - CardW / 2, c.Y - CardH / 2, CardW, CardH);
        _target = _w.FxDownloads?.TranslatePoint(new Point(_w.FxDownloads.Bounds.Width / 2, _w.FxDownloads.Bounds.Height / 2), _layer) ?? new Point(size.Width - 120, 32);

        _stage.Width = size.Width;
        _stage.Height = size.Height;
        _stage.ClipToBounds = true;
        BuildGlow();
        switch (_kind)
        {
            case FxKind.Frame: BuildFrame(k); break;
            case FxKind.Shatter: BuildShatter(k); break;
            default: BuildWave(k); break;
        }
        _exit = _pieces.Count == 0 ? 600 : _pieces.Max(p => p.End);
        if (_frames.Count > 0) _exit = Math.Max(_exit, _frames.Max(f => f.Delay) + 620);
        BuildCard();

        _layer.Children.Clear();
        _layer.Background = Ui.Res("Bg");
        _layer.Children.Add(_stage);
        _layer.Children.Add(_cardHost);
        _layer.Opacity = 1;
        _layer.IsVisible = true;
        _layer.Cursor = new Cursor(StandardCursorType.Hand);
        if (!_demo) _layer.PointerPressed += OnPressed;
        _root.Opacity = 0;
        Seek(0);
        return true;
    }

    Control Slice(Rect r, double k, out Rect clipped)
    {
        clipped = r.Intersect(_all);
        var max = _shot!.PixelSize;
        var x = Math.Clamp((int)Math.Floor(clipped.X * k), 0, max.Width - 1);
        var y = Math.Clamp((int)Math.Floor(clipped.Y * k), 0, max.Height - 1);
        var w = Math.Clamp((int)Math.Ceiling(clipped.Width * k), 1, max.Width - x);
        var h = Math.Clamp((int)Math.Ceiling(clipped.Height * k), 1, max.Height - y);
        var pr = new PixelRect(x, y, w, h);
        var img = new Image { Source = new CroppedBitmap(_shot, pr), Stretch = Stretch.Fill, Width = clipped.Width, Height = clipped.Height, IsHitTestVisible = false };
        Canvas.SetLeft(img, clipped.X);
        Canvas.SetTop(img, clipped.Y);
        _stage.Children.Add(img);
        return img;
    }

    Piece Add(Rect r, double k, Action<Piece> set, RelativePoint? origin = null)
    {
        var view = Slice(r, k, out _);
        var p = new Piece { View = view };
        set(p);
        p.Init(origin ?? new RelativePoint(0.5, 0.5, RelativeUnit.Relative));
        _pieces.Add(p);
        return p;
    }

    /// <summary>Боковая панель полосками уезжает в сторону — сверху вниз, друг за другом.</summary>
    void RailBands(double k, double start, double step, int bands = 8)
    {
        if (_rail.Width <= 0) return;
        var left = _rail.X < _all.Width / 2;
        var h = _rail.Height / bands;
        for (var i = 0; i < bands; i++)
        {
            var band = new Rect(_rail.X, _rail.Y + i * h, _rail.Width, Math.Ceiling(h + 0.5));
            var n = i;
            Add(band, k, p =>
            {
                p.Delay = start + n * step;
                p.Duration = 420;
                p.Dx = (left ? -1 : 1) * (_rail.Width + 60);
                p.Rotate = (left ? -1 : 1) * (n % 2 == 0 ? 4 : -3);
                p.Opacity = 0;
            });
        }
    }

    void BuildGlow()
    {
        var accent = Ui.Res("Brand") is SolidColorBrush b ? b.Color : Color.Parse(Look.Accent);
        _glow.Width = 980;
        _glow.Height = 680;
        _glow.Background = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(110, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(36, accent.R, accent.G, accent.B), 0.45),
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
            },
        };
        Canvas.SetLeft(_glow, _cardRect.Center.X - _glow.Width / 2);
        Canvas.SetTop(_glow, _cardRect.Center.Y - _glow.Height / 2);
        _glow.Opacity = 0;
        _stage.Children.Add(_glow);
    }

    /// <summary>Вид 1: экран растворяется наружу от мода, две рамки расходятся к краям, панель уезжает.</summary>
    void BuildFrame(double k)
    {
        var origin = new RelativePoint((_cardRect.Center.X - _main.X) / _main.Width, (_cardRect.Center.Y - _main.Y) / _main.Height, RelativeUnit.Relative);
        Add(_main, k, p => { p.Delay = 40; p.Duration = 640; p.Scale = 1.5; p.Opacity = 0; p.Ease = EaseInQuad; }, origin);
        RailBands(k, 60, 34);
        var accent = Ui.Res("Brand");
        foreach (var delay in new[] { 0.0, 170.0 })
        {
            var f = new Border { BorderBrush = accent, BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(20), IsHitTestVisible = false };
            _frames.Add((f, delay));
            _stage.Children.Add(f);
        }
    }

    /// <summary>Вид 2: экран трескается вокруг мода, куски I–VI разлетаются в свои стороны.</summary>
    void BuildShatter(double k)
    {
        var m = _cardRect.Inflate(14);
        var (x0, x1, y0, y1) = (_main.X, _main.Right, _main.Y, _main.Bottom);
        // I — верх, II — низ, III и VI — слева сверху и снизу, IV — справа, V — боковая панель.
        Add(new Rect(x0, y0, x1 - x0, m.Top - y0), k, p => { p.Delay = 0; p.Duration = 620; p.Dy = -(m.Top - y0) - 90; p.Dx = -20; p.Rotate = -5; p.Opacity = 0.1; p.Ease = Crack; });
        Add(new Rect(x0, m.Bottom, x1 - x0, y1 - m.Bottom), k, p => { p.Delay = 50; p.Duration = 620; p.Dy = (y1 - m.Bottom) + 90; p.Dx = 30; p.Rotate = 4; p.Opacity = 0.1; p.Ease = Crack; });
        Add(new Rect(x0, m.Top, m.Left - x0, m.Height / 2), k, p => { p.Delay = 90; p.Duration = 600; p.Dx = -(m.Left - x0) - 80; p.Dy = -60; p.Rotate = -9; p.Opacity = 0.1; p.Ease = Crack; });
        Add(new Rect(x0, m.Center.Y, m.Left - x0, m.Height / 2), k, p => { p.Delay = 120; p.Duration = 600; p.Dx = -(m.Left - x0) - 80; p.Dy = 60; p.Rotate = 8; p.Opacity = 0.1; p.Ease = Crack; });
        Add(new Rect(m.Right, m.Top, x1 - m.Right, m.Height), k, p => { p.Delay = 70; p.Duration = 600; p.Dx = (x1 - m.Right) + 90; p.Rotate = 6; p.Opacity = 0.1; p.Ease = Crack; });
        // Середина за карточкой — просто тает.
        Add(m, k, p => { p.Delay = 0; p.Duration = 320; p.Scale = 0.85; p.Opacity = 0; p.Ease = EaseOut; });
        RailBands(k, 110, 30);
    }

    /// <summary>Вид 3: волна из левого верхнего угла — плитки переворачиваются и тают по диагонали.</summary>
    void BuildWave(double k)
    {
        const int cols = 14, rows = 9;
        var w = _all.Width / cols;
        var h = _all.Height / rows;
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var cell = new Rect(c * w, r * h, Math.Ceiling(w + 0.5), Math.Ceiling(h + 0.5));
                var wave = (c / (double)(cols - 1) + r / (double)(rows - 1)) / 2;
                var (cc, rr) = (c, r);
                Add(cell, k, p =>
                {
                    p.Delay = wave * 620;
                    p.Duration = 420;
                    p.Dx = 26 + cc * 1.5;
                    p.Dy = 34 + rr * 1.5;
                    p.Rotate = 18 + (cc + rr) % 3 * 6;
                    p.Scale = 0.15;
                    p.Opacity = 0;
                });
            }
    }

    // ---------------------------------------------------------------- карточка мода в центре

    readonly Panel _cardHost = new();

    void BuildCard()
    {
        Control art = _card.Game is { } game && string.IsNullOrEmpty(_card.IconUrl)
            ? new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = Ui.GameImage(game, 160, art: Images.Art.Cover) }
            : Ui.Thumb(_card.IconUrl, _card.Title, 76, 16, 200);
        var badge = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Res("Brand"),
            BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -8, -8),
            Child = Ui.Icon(_card.Glyph, 13, Brushes.White),
        };
        var pic = new Panel { Width = 76, Height = 76, VerticalAlignment = VerticalAlignment.Center, Children = { art, badge } };

        var title = new TextBlock { Text = _card.Title, FontSize = 19, FontWeight = FontWeight.Bold, FontFamily = Look.Display, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Text") };
        var sub = new TextBlock { Text = _card.Subtitle, FontSize = 13, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis };
        _bar.Foreground = Ui.Res("Brand");
        _bar.Background = Ui.Res("Surface3");
        _bar.CornerRadius = new CornerRadius(3);
        _step.Foreground = Ui.Res("Muted");
        var words = Ui.Col(6, Ui.Col(2, title, sub), _bar, _step);
        words.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 20 };
        grid.Children.Add(pic);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        var accent = Ui.Res("Brand") is SolidColorBrush b ? b.Color : Color.Parse(Look.Accent);
        _cardView.Width = CardW;
        _cardView.Height = CardH;
        _cardView.Padding = new Thickness(22, 18);
        _cardView.CornerRadius = new CornerRadius(22);
        _cardView.Background = Ui.Res("Surface");
        _cardView.BorderBrush = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B));
        _cardView.BorderThickness = new Thickness(1.5);
        _cardView.BoxShadow = BoxShadows.Parse($"0 30 80 0 #B0000000, 0 0 60 0 #55{accent.R:X2}{accent.G:X2}{accent.B:X2}");
        _cardView.Child = grid;
        _cardView.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _cardView.RenderTransform = new TransformGroup { Children = { _cardScale, _cardMove } };
        Canvas.SetLeft(_cardView, _cardRect.X);
        Canvas.SetTop(_cardView, _cardRect.Y);

        _skip.Text = I18n.T("v91.fx.skip");
        _skip.Foreground = Ui.Res("Faint");
        _skip.Width = CardW;
        _skip.TextAlignment = TextAlignment.Center;
        Canvas.SetLeft(_skip, _cardRect.X);
        Canvas.SetTop(_skip, _cardRect.Bottom + 22);
        _skip.Opacity = 0;

        var canvas = new Canvas { Width = _all.Width, Height = _all.Height, Children = { _cardView, _skip } };
        _cardHost.Children.Add(canvas);
        _cardHost.IsHitTestVisible = false;

        if (_job is null)
        {
            _bar.IsIndeterminate = true;
            _step.Text = _card.Glyph == Icons.Download ? I18n.T("v91.fx.preparing") : I18n.T("v91.fx.opening");
        }
        else
        {
            RenderJob();
            Jobs.Changed += OnJob;
            Jobs.Finished += OnJobFinished;
        }
    }

    void OnJob(Job j) { if (j == _job) Dispatcher.UIThread.Post(RenderJob); }

    void OnJobFinished(Job j)
    {
        if (j != _job) return;
        Dispatcher.UIThread.Post(() =>
        {
            _done = j.Status == JobStatus.Done;
            _failed = j.Status is JobStatus.Failed or JobStatus.Canceled;
            RenderJob();
            // Готово — даём полсекунды полюбоваться галочкой и возвращаем экран.
            if (double.IsNaN(_returnAt)) _returnAt = Math.Max(_now + 520, _exit + 380);
        });
    }

    void RenderJob()
    {
        if (_job is null) return;
        if (_done || _job.Status == JobStatus.Done)
        {
            _bar.IsIndeterminate = false;
            _bar.Value = 1;
            _bar.Foreground = Ui.Res("Good");
            _step.Text = "✓ " + I18n.T("v91.fx.done");
            _step.Foreground = Ui.Res("Good");
            return;
        }
        if (_failed || _job.Status is JobStatus.Failed or JobStatus.Canceled)
        {
            _bar.IsIndeterminate = false;
            _bar.Foreground = Ui.Res("Bad");
            _step.Text = I18n.T("v91.fx.failed") + (string.IsNullOrEmpty(_job.Error) ? "" : ": " + _job.Error);
            _step.Foreground = Ui.Res("Bad");
            return;
        }
        _bar.IsIndeterminate = _job.Ratio < 0;
        if (_job.Ratio >= 0) _bar.Value = _job.Ratio;
        _step.Text = _job.Status == JobStatus.Queued ? I18n.T("v91.fx.queued") : _job.Step;
    }

    // ---------------------------------------------------------------- ход времени

    public void Start()
    {
        _t0 = DateTime.UtcNow;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Seek((DateTime.UtcNow - _t0).TotalMilliseconds));
        _timer.Start();
    }

    public void Skip()
    {
        if (_ended) return;
        if (double.IsNaN(_returnAt) || _returnAt > _now) _returnAt = _now;
    }

    void OnPressed(object? sender, PointerPressedEventArgs e) { e.Handled = true; Skip(); }

    /// <summary>Показать кадр на момент ms от начала (таймер, или вручную — для снимков).</summary>
    public void Seek(double ms)
    {
        if (_ended) return;
        _now = ms;
        foreach (var p in _pieces) p.At(ms);
        SeekFrames(ms);

        // Свечение за карточкой и сама карточка — «выпрыгивает» чуть позже начала.
        _glow.Opacity = Math.Clamp(ms / 500, 0, 1);
        var pop = Math.Clamp((ms - 140) / 460, 0, 1);
        var back = EaseOutBack(pop);
        _cardScale.ScaleX = _cardScale.ScaleY = 0.72 + 0.28 * back;
        _cardView.Opacity = Math.Clamp(pop * 1.6, 0, 1);
        _skip.Opacity = Math.Clamp((ms - _exit - 300) / 400, 0, 0.9);

        // Когда возвращать экран: без задачи — чуть погодя, с задачей — когда готово, но не дольше 1,7 с.
        if (double.IsNaN(_returnAt) && !_demo)
        {
            if (_job is null && ms >= _exit + 650) _returnAt = ms;
            else if (ms >= _exit + 1700) _returnAt = ms;
        }
        if (!double.IsNaN(_returnAt) && ms >= _returnAt) SeekReturn((ms - _returnAt) / ReturnMs);
    }

    void SeekFrames(double ms)
    {
        foreach (var (frame, delay) in _frames)
        {
            var k = Math.Clamp((ms - delay) / 620, 0, 1);
            var e = EaseOut(k);
            var outer = _main.Inflate(30);
            var x = _cardRect.X + (outer.X - _cardRect.X) * e;
            var y = _cardRect.Y + (outer.Y - _cardRect.Y) * e;
            var w = _cardRect.Width + (outer.Width - _cardRect.Width) * e;
            var h = _cardRect.Height + (outer.Height - _cardRect.Height) * e;
            Canvas.SetLeft(frame, x);
            Canvas.SetTop(frame, y);
            frame.Width = w;
            frame.Height = h;
            frame.Opacity = k <= 0 ? 0 : 1 - Math.Clamp((k - 0.45) / 0.55, 0, 1);
        }
    }

    /// <summary>Возврат: карточка улетает в значок загрузок, экран проявляется обратно.</summary>
    public void SeekReturn(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var e = EaseIn(t);
        var from = _cardRect.Center;
        _cardMove.X = (_target.X - from.X) * e;
        _cardMove.Y = (_target.Y - from.Y) * e;
        _cardScale.ScaleX = _cardScale.ScaleY = 1 - 0.88 * e;
        _cardView.Opacity = 1 - Math.Clamp((t - 0.55) / 0.45, 0, 1);
        _skip.Opacity = 0;
        _stage.Opacity = 1 - EaseOut(t);
        _layer.Background = t > 0 ? null : _layer.Background;
        _root.Opacity = EaseOut(t);
        if (t >= 1 && !_demo) Finish();
    }

    void Finish()
    {
        if (_ended) return;
        _ended = true;
        _timer?.Stop();
        Jobs.Changed -= OnJob;
        Jobs.Finished -= OnJobFinished;
        _layer.PointerPressed -= OnPressed;
        _layer.IsVisible = false;
        _layer.Children.Clear();
        _layer.Background = null;
        _root.Opacity = 1;
        // Снимок освобождаем не сразу: отрисовщик ещё кадр-другой может держать куски, вырезанные из него,
        // и рисовать из уже освобождённой памяти — это роняло бы программу.
        foreach (var p in _pieces) if (p.View is Image img) img.Source = null;
        _pieces.Clear();
        if (_shot is { } shot) DispatcherTimer.RunOnce(shot.Dispose, TimeSpan.FromSeconds(3));
        _shot = null;
        Ended?.Invoke();
    }

    /// <summary>Для снимков: убрать слой анимации и вернуть экран.</summary>
    public void Close() => Finish();

    // ---------------------------------------------------------------- плавность

    static double EaseIn(double k) => k * k * k;
    static double EaseInQuad(double k) => k * k;
    static double EaseOut(double k) => 1 - Math.Pow(1 - k, 3);
    static double EaseOutBack(double k)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return k <= 0 ? 0 : 1 + c3 * Math.Pow(k - 1, 3) + c1 * Math.Pow(k - 1, 2);
    }
    /// <summary>«Трещина»: сначала куски чуть расходятся, потом разгоняются и улетают.</summary>
    static double Crack(double k) => k < 0.18 ? 0.035 * EaseOut(k / 0.18) : 0.035 + 0.965 * EaseIn((k - 0.18) / 0.82);
}
