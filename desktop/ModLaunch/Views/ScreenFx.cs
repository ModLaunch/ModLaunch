using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: эффекты «экрана» поверх всего окна — строки развёртки, как у старого монитора, и мягкое
/// затемнение по краям. Включаются конфигом (fx_scanlines 1, fx_vignette 1) или в «Конфигах».
/// Нажатия проходят насквозь.
/// </summary>
public sealed class ScreenFx : Panel
{
    sealed class Lines : Control
    {
        static readonly IBrush Ink = new SolidColorBrush(Color.FromArgb(34, 0, 0, 0));
        public override void Render(DrawingContext ctx)
        {
            var h = Bounds.Height;
            for (double y = 0; y < h; y += 3) ctx.FillRectangle(Ink, new Rect(0, y, Bounds.Width, 1));
        }
    }

    readonly Lines _lines = new() { IsHitTestVisible = false };
    readonly Border _vignette = new()
    {
        IsHitTestVisible = false,
        Background = new RadialGradientBrush
        {
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(120, 0, 0, 0), 1) },
        },
    };

    public ScreenFx()
    {
        IsHitTestVisible = false;
        Children.Add(_vignette);
        Children.Add(_lines);
        Update();
        Look.Changed += Update;
    }

    void Update()
    {
        _lines.IsVisible = Look.Scanlines;
        _vignette.IsVisible = Look.Vignette;
        IsVisible = Look.Scanlines || Look.Vignette;
        _lines.InvalidateVisual();
    }
}
