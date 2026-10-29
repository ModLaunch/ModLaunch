using Avalonia;
using Avalonia.Controls;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 2.0: рамка-«золотой прямоугольник»: высота = ширина / φ (ratio можно задать иначе, например 2,618 = φ² для низких сцен).
/// Широкие картинки плиток (проекты, моды, сборки) — в ней, поэтому у них одна пропорция на любой ширине.
/// </summary>
public sealed class GoldenBox : Decorator
{
    public double Ratio { get; set; } = Phi.Ratio;
    public double MaxHeightLimit { get; set; } = double.PositiveInfinity;

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 233 : available.Width;
        var h = Math.Min(w / Ratio, MaxHeightLimit);
        Child?.Measure(new Size(w, h));
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(final));
        return final;
    }
}
