using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ModLaunch.Views;

/// <summary>Логотип Creator Hub (22.0): тёмный кубик с искрой на янтарной плитке — цвет ModScript, без градиента.</summary>
public static class CreatorLogo
{
    public static Control Tile(double size) => new Border
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.27),
        Background = HubLook.Amber,
        Child = Ui.Icon(Icons.Creator, size * 0.55, HubLook.OnAmber),
    };
}
