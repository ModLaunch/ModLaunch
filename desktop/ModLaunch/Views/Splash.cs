using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Заставка при запуске (8.0): поверх окна, пока ModLaunch проверяет игры и
/// готовит обложки. Наклонная стена настоящих обложек медленно плывёт,
/// логотип «дышит», под ним — что сейчас происходит и полоска хода.
/// Держится не меньше секунды с небольшим (чтобы не мигать) и не дольше
/// шести, потом растворяется. Отключается в «Настройки → Внешний вид».
/// </summary>
public sealed class Splash : Panel
{
    const double MinSeconds = 1.3, MaxSeconds = 6;

    readonly TextBlock _status = new() { FontSize = 14, Foreground = Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center };
    readonly Border _bar = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    readonly ScaleTransform _logoScale = new(1, 1);
    readonly Control _center;
    readonly List<(TranslateTransform Transform, double Speed, double Loop, double Offset)> _columns = [];
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    const double BarWidth = 240;
    double _target, _shown;
    bool _finishing, _gone;

    public static bool Enabled => Settings.Data.Bool("splash", true);

    public Splash()
    {
        Background = Ui.Res("Bg");
        ZIndex = 100;

        Children.Add(Wall());
        // Центр чистый, к краям стена проступает — логотип не тонет в обложках.
        Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                GradientStops = { new GradientStop(WithAlpha("Bg", 250), 0.25), new GradientStop(WithAlpha("Bg", 170), 0.75), new GradientStop(WithAlpha("Bg", 120), 1) },
            },
        });

        var logo = new Border
        {
            Width = 96, Height = 96, CornerRadius = new CornerRadius(26), ClipToBounds = true,
            BoxShadow = BoxShadows.Parse("0 18 40 -8 #A0000000"),
            Child = new Image { Source = Images.Asset("icon.png", 192), Stretch = Stretch.UniformToFill },
            RenderTransform = _logoScale,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var word = new TextBlock
        {
            FontSize = 34, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, HorizontalAlignment = HorizontalAlignment.Center,
            Inlines = { new Avalonia.Controls.Documents.Run("Mod"), new Avalonia.Controls.Documents.Run("Launch") { Foreground = Ui.Res("Brand2") } },
        };
        var track = new Border { Width = BarWidth, Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Res("Surface3"), Child = _bar, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = I18n.T("splash.start");
        var center = Ui.Col(14, logo, word, track, _status);
        center.HorizontalAlignment = HorizontalAlignment.Center;
        center.VerticalAlignment = VerticalAlignment.Center;
        center.RenderTransform = new ScaleTransform(1, 1);
        center.RenderTransformOrigin = RelativePoint.Center;
        _center = center;
        Children.Add(center);

        // Совет внизу — каждый раз другой.
        var tips = Enumerable.Range(1, 8).Select(i => "splash.tip" + i).Where(I18n.Has).ToList();
        if (tips.Count > 0)
        {
            var tip = new TextBlock
            {
                Text = I18n.T(tips[Random.Shared.Next(tips.Count)]), FontSize = 13, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 0, 24, 40),
            };
            Children.Add(tip);
        }
        Children.Add(new TextBlock
        {
            Text = "v" + Http.Version, FontSize = 12, Foreground = Ui.Res("Faint"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 22, 18),
        });

        Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = new Avalonia.Animation.Easings.CubicEaseIn() }];
        if (!Program.Screenshot)
        {
            AttachedToVisualTree += (_, _) => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Tick);
            // Что бы ни случилось с запуском — заставка не висит дольше MaxSeconds.
            Avalonia.Threading.DispatcherTimer.RunOnce(Finish, TimeSpan.FromSeconds(MaxSeconds));
        }
    }

    static Color WithAlpha(string key, byte alpha)
    {
        var c = Ui.Res(key) is ISolidColorBrush b ? b.Color : Colors.Black;
        return Color.FromArgb(alpha, c.R, c.G, c.B);
    }

    /// <summary>Шаг загрузки: подпись и до какой доли дойти полоске.</summary>
    public void Step(string text, double ratio)
    {
        _status.Text = text;
        _target = Math.Max(_target, Math.Clamp(ratio, 0, 1));
        if (Program.Screenshot) { _shown = _target; _bar.Width = BarWidth * _shown; }
    }

    /// <summary>Готово: дождаться минимального времени и раствориться.</summary>
    public void Finish()
    {
        if (_finishing) return;
        _finishing = true;
        _target = 1;
        var wait = Math.Max(0, MinSeconds - _clock.Elapsed.TotalSeconds);
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            IsHitTestVisible = false;
            Opacity = 0;
            if (_center.RenderTransform is ScaleTransform s) { s.ScaleX = s.ScaleY = 1.04; }
            Avalonia.Threading.DispatcherTimer.RunOnce(() => { _gone = true; (Parent as Panel)?.Children.Remove(this); }, TimeSpan.FromMilliseconds(460));
        }, TimeSpan.FromSeconds(wait));
    }

    void Tick(TimeSpan _)
    {
        if (_gone || !this.IsAttachedToVisualTree()) return;
        var t = _clock.Elapsed.TotalSeconds;
        // Логотип «дышит»: два процента туда-обратно, раз в две с половиной секунды.
        var k = 1 + 0.025 * Math.Sin(t * Math.PI * 2 / 2.5);
        _logoScale.ScaleX = _logoScale.ScaleY = k;
        // Полоска догоняет цель плавно, а не скачками.
        _shown += (_target - _shown) * 0.12;
        _bar.Width = BarWidth * _shown;
        foreach (var (transform, speed, loop, offset) in _columns) transform.Y = offset - t * speed % loop;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Tick);
    }

    /// <summary>Стена обложек под наклоном: колонки плывут вверх с разной скоростью.</summary>
    Control Wall()
    {
        var games = Games.GameCatalog.All.Where(g => g.SteamAppId > 0).ToList();
        const double w = 150, h = 225, gap = 16;
        var canvas = new Canvas { Width = 8 * (w + gap), Height = 1800, Opacity = 0.5 };
        for (var c = 0; c < 8 && games.Count > 0; c++)
        {
            var column = new StackPanel { Spacing = gap, Width = w };
            // По восемь обложек в колонке, каждая колонка начинает с другой игры; дважды подряд — чтобы круг был незаметен.
            var order = Enumerable.Range(0, 8).Select(i => games[(c * 3 + i) % games.Count]).ToList();
            foreach (var g in order.Concat(order))
                column.Children.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = Ui.GameImage(g, 300, art: Images.Art.Cover) });
            Canvas.SetLeft(column, c * (w + gap));
            canvas.Children.Add(column);
            var offset = -(c % 3) * 90.0;
            var transform = new TranslateTransform(0, offset);
            column.RenderTransform = transform;
            _columns.Add((transform, 10 + c % 4 * 3.5, order.Count * (h + gap), offset));
        }
        return new Border
        {
            ClipToBounds = true,
            Child = new LayoutTransformControl
            {
                LayoutTransform = new RotateTransform(-12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = canvas,
            },
        };
    }
}
