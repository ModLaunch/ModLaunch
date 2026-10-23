using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ModLaunch.Views;

/// <summary>
/// 17.0: иконка одним элементом. Раньше каждая была тремя (Viewbox → Canvas → Path), а цвет искался
/// привязкой к кнопке-предку, и контур заново разбирался из строки. На странице их сотни — профилировщик
/// показал, что иконки и их присоединение к дереву съедали заметную часть перехода между страницами.
/// Теперь контур разбирается один раз на всё приложение, цвет наследуется, как у текста рядом.
/// </summary>
public sealed class IconView : Control
{
    static readonly Dictionary<string, Geometry> Parsed = new();

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<IconView>();
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<IconView, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<IconView, IBrush?>(nameof(Fill));

    static IconView() => AffectsRender<IconView>(ForegroundProperty, StrokeProperty, FillProperty);

    readonly Geometry _geometry;

    public IconView(string data)
    {
        if (!Parsed.TryGetValue(data, out var g)) Parsed[data] = g = Geometry.Parse(data);
        _geometry = g;
    }

    /// <summary>Цвет контура; не задан — цвет текста вокруг (кнопки, пункта меню).</summary>
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    /// <summary>Заливка (звёзды рейтинга, сплошные значки); без неё иконка — только контур.</summary>
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsNaN(Width) ? 18 : Width, double.IsNaN(Height) ? 18 : Height);

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var k = size / 24;
        var pen = Fill is not null && Stroke is null ? null : new Pen(Stroke ?? Foreground ?? Brushes.White, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var offset = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(offset.X, offset.Y)))
            context.DrawGeometry(Fill, pen, _geometry);
    }
}
