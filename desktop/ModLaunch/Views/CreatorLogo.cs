using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ModLaunch.Views;

/// <summary>Логотип Creator Hub: белый кубик с искрой на фиолетово-розовой плитке.</summary>
public static class CreatorLogo
{
    public static Control Tile(double size) => new Border
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.27),
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#9D85FF"), 0), new GradientStop(Color.Parse("#EC4899"), 1) },
        },
        Child = Ui.Icon(Icons.Creator, size * 0.55, Brushes.White),
    };
}
