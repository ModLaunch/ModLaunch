using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 2.0: сетка золотого сечения поверх окна (Ctrl+Shift+G, меню «…»). Линии 38,2 % и 61,8 % по ширине и высоте
/// «листа» со страницей и золотая спираль в самом большом прямоугольнике φ : 1, что в него помещается, —
/// видно, где на экране сходятся пропорции. Нажатия проходят насквозь.
/// </summary>
public sealed class PhiGrid : Control
{
    /// <summary>Что размечать (лист со страницей); null — всё окно.</summary>
    public Visual? Target { get; set; }

    public PhiGrid()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    static Color Gold => Ui.Res("Gold") is ISolidColorBrush b ? b.Color : Color.Parse("#F2B544");

    public override void Render(DrawingContext ctx)
    {
        var area = Target is { } t && t.TranslatePoint(new Point(0, 0), this) is { } at ? new Rect(at, t.Bounds.Size) : new Rect(Bounds.Size);
        if (area.Width < 50 || area.Height < 50) return;
        var gold = Gold;
        var line = new Pen(new SolidColorBrush(Color.FromArgb(170, gold.R, gold.G, gold.B)), 1, new DashStyle([4, 4], 0));
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(60, gold.R, gold.G, gold.B)), 1);
        var curve = new Pen(new SolidColorBrush(Color.FromArgb(200, gold.R, gold.G, gold.B)), 1.5);

        foreach (var f in new[] { 1 - 1 / Phi.Ratio, 1 / Phi.Ratio })
        {
            var x = Math.Round(area.X + area.Width * f) + 0.5;
            var y = Math.Round(area.Y + area.Height * f) + 0.5;
            ctx.DrawLine(line, new Point(x, area.Y), new Point(x, area.Bottom));
            ctx.DrawLine(line, new Point(area.X, y), new Point(area.Right, y));
            Label(ctx, f < 0.5 ? "38,2 %" : "61,8 %", new Point(x + 5, area.Y + 6), gold);
        }

        // Самый большой прямоугольник φ : 1 по центру листа — и спираль из четвертей круга в его квадратах.
        var w = Math.Min(area.Width, area.Height * Phi.Ratio);
        var h = w / Phi.Ratio;
        var r = new Rect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        ctx.DrawRectangle(null, faint, r);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(r.X, r.Bottom), false);
            for (var i = 0; i < 12 && r.Width > 2 && r.Height > 2; i++)
            {
                Rect square;
                Point end;
                switch (i % 4)
                {
                    case 0: square = new Rect(r.X, r.Y, r.Height, r.Height); end = square.TopRight; r = new Rect(r.X + r.Height, r.Y, r.Width - r.Height, r.Height); break;
                    case 1: square = new Rect(r.X, r.Y, r.Width, r.Width); end = square.BottomRight; r = new Rect(r.X, r.Y + r.Width, r.Width, r.Height - r.Width); break;
                    case 2: square = new Rect(r.Right - r.Height, r.Y, r.Height, r.Height); end = square.BottomLeft; r = new Rect(r.X, r.Y, r.Width - r.Height, r.Height); break;
                    default: square = new Rect(r.X, r.Bottom - r.Width, r.Width, r.Width); end = square.TopLeft; r = new Rect(r.X, r.Y, r.Width, r.Height - r.Width); break;
                }
                ctx.DrawRectangle(null, faint, square);
                g.ArcTo(end, new Size(square.Width, square.Height), 0, false, SweepDirection.Clockwise);
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, curve, geometry);
        Label(ctx, "φ = 1,618 · Ctrl+Shift+G", new Point(area.X + 10, area.Bottom - 24), gold);
    }

    static void Label(DrawingContext ctx, string text, Point at, Color gold)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Look.UiFont, FontStyle.Normal, FontWeight.SemiBold), 11,
            new SolidColorBrush(gold));
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 12, 12, 14)), null, new Rect(at.X - 4, at.Y - 2, ft.Width + 8, ft.Height + 4), 4, 4);
        ctx.DrawText(ft, at);
    }
}
