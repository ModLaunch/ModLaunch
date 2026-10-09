using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Заставка при каждом запуске (9.1, по эскизу «ModLaunch app» с полоской): логотип,
/// название и полоска загрузки, пока программа читает настройки и ищет игры.
/// Минимум ~1,2 с (чтобы полоска успела дойти), максимум 5 с — дальше окно открывается в любом случае.
/// </summary>
public sealed class Splash : Panel
{
    public static bool Enabled => Settings.Data.Bool("splash", true) && !Program.Screenshot;

    const double TrackWidth = 320;
    readonly Border _fill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _shine = new() { Width = 80, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    readonly TextBlock _status = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Border _logo;
    readonly Border _glow = new() { Width = 900, Height = 900, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly ScaleTransform _logoScale = new();
    readonly ScaleTransform _zoom = new();
    readonly DispatcherTimer? _timer;
    readonly DateTime _t0 = DateTime.UtcNow;
    double _shown, _target = 0.18;
    bool _finish;
    double _fadeFrom = -1;
    Action? _closed;

    public Splash(bool demo = false)
    {
        Background = Ui.Res("Bg");
        var accent = Color.Parse(Look.Accent);
        _glow.Background = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(70, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(18, accent.R, accent.G, accent.B), 0.5),
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
            },
        };

        _logo = new Border
        {
            Width = 112, Height = 112, CornerRadius = new CornerRadius(30), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center,
            BoxShadow = BoxShadows.Parse($"0 24 60 0 #66{accent.R:X2}{accent.G:X2}{accent.B:X2}"),
            Child = new Image { Source = Images.Asset("icon.png", 224), Stretch = Stretch.UniformToFill },
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = _logoScale,
        };
        // Тень у Border с ClipToBounds обрезается — обёртка держит тень снаружи.
        var logoHost = new Border
        {
            Width = 112, Height = 112, CornerRadius = new CornerRadius(30), HorizontalAlignment = HorizontalAlignment.Center,
            BoxShadow = BoxShadows.Parse($"0 24 70 0 #70{accent.R:X2}{accent.G:X2}{accent.B:X2}"),
            Child = _logo,
        };

        var word = new TextBlock
        {
            FontFamily = Look.Unbounded, FontSize = 40, FontWeight = FontWeight.Bold, LetterSpacing = -1.2, HorizontalAlignment = HorizontalAlignment.Center,
            Inlines = { new Run("Mod") { Foreground = Ui.Res("Text") }, new Run("Launch") { Foreground = Ui.Res("Brand2") } },
        };
        var tag = new Border
        {
            Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "APP · " + Http.Version, FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.5, Foreground = Ui.Res("Faint") },
        };

        _fill.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(accent, 0), new GradientStop(Look.Mix(accent, Colors.White, 0.35), 1) },
        };
        _shine.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
        };
        var track = new Border
        {
            Width = TrackWidth, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Res("Surface3"), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Panel { Children = { _fill, _shine } },
        };
        _status.Foreground = Ui.Res("Muted");
        _status.Text = I18n.T("v91.splash.settings");

        var center = Ui.Col(0, logoHost, Gap(26), word, Gap(10), tag, Gap(34), track, Gap(14), _status);
        center.HorizontalAlignment = HorizontalAlignment.Center;
        center.VerticalAlignment = VerticalAlignment.Center;
        center.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        center.RenderTransform = _zoom;

        Children.Add(_glow);
        Children.Add(center);
        ZIndex = 1000;
        Render();

        if (!demo)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
            _timer.Start();
        }
    }

    static Control Gap(double h) => new Border { Height = h };

    /// <summary>Шаг загрузки: подпись и докуда дойти полоске (0…1).</summary>
    public void Step(string text, double target)
    {
        _status.Text = text;
        _target = Math.Max(_target, Math.Clamp(target, 0, 1));
    }

    /// <summary>Всё готово: полоска добегает до конца, заставка растворяется.</summary>
    public void Finish(Action? closed = null)
    {
        _closed = closed;
        _finish = true;
        _target = 1;
        _status.Text = I18n.T("v91.splash.ready");
    }

    /// <summary>Для снимков: заставка в заданной точке.</summary>
    public void DemoAt(double progress, string text)
    {
        _shown = progress;
        _status.Text = text;
        Render(0.6);
    }

    void Tick()
    {
        var elapsed = (DateTime.UtcNow - _t0).TotalMilliseconds;
        // Сама ползёт к цели, а без новостей — медленно подбирается, чтобы не казалось, что зависла.
        if (!_finish && elapsed > 5000) Finish(_closed);
        var creep = _finish ? 0.02 : 0.0012;
        var goal = _finish ? 1 : Math.Min(0.92, _target + elapsed / 9000);
        _shown = Math.Min(goal, _shown + Math.Max(creep, (goal - _shown) * 0.09));
        Render(elapsed / 1000);

        if (_finish && _shown >= 0.999 && elapsed >= 1150)
        {
            if (_fadeFrom < 0) _fadeFrom = elapsed;
            var t = Math.Clamp((elapsed - _fadeFrom) / 380, 0, 1);
            var e = 1 - Math.Pow(1 - t, 3);
            Opacity = 1 - e;
            _zoom.ScaleX = _zoom.ScaleY = 1 + 0.06 * e;
            if (t >= 1)
            {
                _timer?.Stop();
                IsVisible = false;
                (Parent as Panel)?.Children.Remove(this);
                _closed?.Invoke();
            }
        }
    }

    void Render(double seconds = 0)
    {
        _fill.Width = Math.Max(6, TrackWidth * _shown);
        // Блик бежит по заполненной части.
        var run = (seconds * 0.9) % 1.0;
        _shine.Margin = new Thickness(-80 + (TrackWidth * _shown + 80) * run, 0, 0, 0);
        _shine.IsVisible = _shown < 0.999;
        // Логотип «дышит».
        var breath = 1 + 0.025 * Math.Sin(seconds * 3.2);
        _logoScale.ScaleX = _logoScale.ScaleY = breath;
        _glow.Opacity = 0.75 + 0.25 * Math.Sin(seconds * 1.7);
    }
}
