using Avalonia;
using Avalonia.Controls;

namespace ModLaunch.Views;

/// <summary>
/// Раскладка «кирпичиками»: карточки идут в две колонки (на широком окне), каждая
/// следующая — в ту, что короче. На узком окне — одна колонка, как раньше.
/// </summary>
public sealed class Masonry : Panel
{
    public double Gap { get; set; } = 16;
    public double MinColumn { get; set; } = 460;

    int Columns(double width) => double.IsInfinity(width) ? 1 : Math.Max(1, Math.Min(2, (int)((width + Gap) / (MinColumn + Gap))));

    protected override Size MeasureOverride(Size available)
    {
        var cols = Columns(available.Width);
        var colWidth = cols == 1 ? available.Width : (available.Width - Gap * (cols - 1)) / cols;
        var heights = new double[cols];
        foreach (var child in Children)
        {
            child.Measure(new Size(colWidth, double.PositiveInfinity));
            var i = Array.IndexOf(heights, heights.Min());
            heights[i] += child.DesiredSize.Height + Gap;
        }
        return new Size(double.IsInfinity(available.Width) ? Children.Select(c => c.DesiredSize.Width).DefaultIfEmpty(0).Max() : available.Width, Math.Max(0, heights.Max() - Gap));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cols = Columns(final.Width);
        var colWidth = (final.Width - Gap * (cols - 1)) / cols;
        var heights = new double[cols];
        foreach (var child in Children)
        {
            var i = Array.IndexOf(heights, heights.Min());
            child.Arrange(new Rect(i * (colWidth + Gap), heights[i], colWidth, child.DesiredSize.Height));
            heights[i] += child.DesiredSize.Height + Gap;
        }
        return final;
    }
}
