using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Фон окна (8.1): «аврора» из трёх мягких пятен в оттенках акцента, которые
/// медленно плывут; тонкая точечная сетка, тающая к низу; виньетка по краям.
/// Стиль выбирается в «Внешний вид → Стиль»: aurora, soft (без движения),
/// grid (сетка и одно пятно), plain (ровный фон). Анимация идёт ~20 кадров
/// в секунду и замирает, когда окно свёрнуто или анимации выключены.
/// </summary>
public sealed class Backdrop : Control
{
    Color _accent = Color.Parse("#7C5CFF");
    Color _bg = Color.Parse("#0F1116");
    bool _light;
    string _style = "aurora";
    double _t = Random.Shared.NextDouble() * 100;
    readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(66) };
    IBrush? _grid;

    public Backdrop()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) =>
        {
            // Окно свёрнуто или не в фокусе (например, идёт игра) — фон замирает и не тратит ни такта.
            if (MainWindow.Current is not { IsActive: true } w || w.WindowState == WindowState.Minimized || !IsEffectivelyVisible) return;
            _t += 0.05;
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        AttachedToVisualTree += (_, _) => Restart();
    }

    /// <summary>Новые цвета или стиль (сменили тему, акцент, страницу с цветом игры).</summary>
    public void Set(Color accent, Color bg, bool light, string style)
    {
        _accent = accent;
        _bg = bg;
        _light = light;
        _style = style;
        _grid = null;
        Restart();
        InvalidateVisual();
    }

    void Restart()
    {
        if (_style == "aurora" && Look.Animations) _timer.Start();
        else _timer.Stop();
    }

    static Color Hue(Color c, double shift, double light = 0)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, h = 0, s = 0, d = max - min;
        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
        }
        return Ui.HslColor((h + shift + 360) % 360, s, Math.Clamp(l + light, 0, 1));
    }

    static Color A(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);

    /// <summary>Плитка точечной сетки: точка 1.4 px раз в 26 px.</summary>
    IBrush Grid()
    {
        if (_grid is not null) return _grid;
        var dot = _light ? Color.FromArgb(40, 20, 24, 34) : Color.FromArgb(34, 255, 255, 255);
        var drawing = new GeometryDrawing
        {
            Brush = new SolidColorBrush(dot),
            Geometry = new EllipseGeometry(new Rect(12.3, 12.3, 1.4, 1.4)),
        };
        var group = new DrawingGroup { Children = { new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(0, 0, 26, 26)) }, drawing } };
        return _grid = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            DestinationRect = new RelativeRect(0, 0, 26, 26, RelativeUnit.Absolute),
        };
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width < 1 || size.Height < 1 || _style == "plain") return;
        var rect = new Rect(size);
        var strength = _light ? 0.55 : 1.0;
        var moving = _style == "aurora";
        var t = moving ? _t : 0;

        // Пятна: акцент, соседний оттенок «теплее» и «холоднее». Двигаются по медленным синусам.
        var blobs = _style == "grid"
            ? new[] { (X: 0.15, Y: 0.0, R: 0.75, C: _accent, A: 0.20) }
            : new[]
            {
                (X: 0.12 + 0.06 * Math.Sin(t * 0.21), Y: 0.02 + 0.05 * Math.Cos(t * 0.17), R: 0.75, C: _accent, A: 0.27),
                (X: 0.86 + 0.07 * Math.Cos(t * 0.15 + 1.3), Y: 0.18 + 0.08 * Math.Sin(t * 0.19 + 0.4), R: 0.60, C: Hue(_accent, 38, 0.04), A: 0.17),
                (X: 0.55 + 0.10 * Math.Sin(t * 0.11 + 2.1), Y: 1.02 + 0.05 * Math.Cos(t * 0.13), R: 0.85, C: Hue(_accent, -42), A: 0.15),
            };
        if (_style == "studio") blobs = [];
        foreach (var b in blobs)
        {
            var brush = new RadialGradientBrush
            {
                Center = new RelativePoint(b.X, b.Y, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(b.X, b.Y, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(b.R, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(b.R * 0.85, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(A(b.C, b.A * strength), 0),
                    new GradientStop(A(b.C, b.A * strength * 0.35), 0.45),
                    new GradientStop(A(b.C, 0), 1),
                },
            };
            ctx.DrawRectangle(brush, null, rect);
        }

        // Сетка, тающая сверху вниз.
        if (_style is "aurora" or "grid")
        {
            using (ctx.PushOpacityMask(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(255, 0, 0, 0), 0), new GradientStop(Color.FromArgb(90, 0, 0, 0), 0.45), new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.9) },
            }, rect))
                ctx.DrawRectangle(Grid(), null, rect);
        }

        // Тонкий блик сверху и виньетка по краям — глубина без лишнего шума.
        ctx.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 0.35, RelativeUnit.Relative),
            GradientStops = { new GradientStop(_light ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(14, 255, 255, 255), 0), new GradientStop(Colors.Transparent, 1) },
        }, null, rect);
        if (_style == "studio") return;
        ctx.DrawRectangle(new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(1.05, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.05, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Transparent, 0.72), new GradientStop(A(_bg, _light ? 0.15 : 0.35), 1) },
        }, null, rect);
    }
}
