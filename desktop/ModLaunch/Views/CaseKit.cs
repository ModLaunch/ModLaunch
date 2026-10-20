using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: кейс ModLaunch в «3D» — тёмный корпус с тремя гранями (перед, крышка, бок), кромки и
/// полоса замка цветом редкости, на замке — логотип ModLaunch. Крышка — отдельный слой (Lid),
/// чтобы её можно было откинуть при открытии; Glow — свет изнутри.
/// </summary>
public sealed class CaseBox : Panel
{
    public readonly Canvas Lid = new();
    public readonly Canvas Body = new();
    public readonly Border Glow;
    public readonly Border Beam;
    public readonly Color Tint;

    public static Color TierColor(string tier) => Color.Parse(tier switch { "legend" => "#FFB547", "rare" => "#3BA7FF", _ => "#8B6BFF" });
    public static Color TierColor2(string tier) => Color.Parse(tier switch { "legend" => "#E8408C", "rare" => "#2B4FE0", _ => "#5A36E6" });

    public CaseBox(double width, string tier)
    {
        Tint = TierColor(tier);
        var tint2 = TierColor2(tier);
        var w = width;
        var h = width * 0.6;
        var d = width * 0.22;          // глубина: сдвиг задней кромки вправо-вверх
        var lidH = h * 0.34;            // высота крышки на передней грани
        Width = w + d;
        Height = h + d;
        ClipToBounds = false;

        IBrush Face(string a, string b) => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) },
        };
        var rim = new SolidColorBrush(Look.Mix(Tint, Colors.White, 0.25));
        var thin = Math.Max(1, width / 160);
        Polygon Poly(Canvas on, IBrush fill, params Point[] pts)
        {
            var p = new Polygon { Points = pts.ToList(), Fill = fill, Stroke = rim, StrokeThickness = thin, StrokeJoin = PenLineJoin.Round };
            on.Children.Add(p);
            return p;
        }

        // Свет изнутри — виден, когда крышка поднята.
        Glow = new Border
        {
            Width = w * 1.6, Height = w * 1.0, Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-w * 0.3, -w * 0.45, -w * 0.3, 0),
            Background = new RadialGradientBrush { GradientStops = { new GradientStop(Gx.Alpha(Look.Mix(Tint, Colors.White, 0.5), 230), 0), new GradientStop(Gx.Alpha(Tint, 120), 0.35), new GradientStop(Gx.Alpha(Tint, 0), 1) } },
        };
        Beam = new Border
        {
            Width = w * 0.62, Height = w * 1.4, Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(w * 0.19 + d * 0.5, 0, 0, h * 0.62),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Gx.Alpha(Colors.White, 200), 0), new GradientStop(Gx.Alpha(Tint, 110), 0.4), new GradientStop(Gx.Alpha(Tint, 0), 1) },
            },
            // Края луча мягкие, без резкой кромки.
            OpacityMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.3), new GradientStop(Colors.Black, 0.7), new GradientStop(Colors.Transparent, 1) },
            },
        };

        foreach (var c in new[] { Body, Lid }) { c.Width = Width; c.Height = Height; }
        // Проём внутри корпуса — виден, когда крышка поднята.
        var hollow = new Polygon
        {
            Points = [new(0, d + lidH), new(d, lidH), new(w + d, lidH), new(w, d + lidH)],
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#07060C"), 0), new GradientStop(Look.Mix(Color.Parse("#07060C"), Tint, 0.45), 1) },
            },
            Stroke = rim, StrokeThickness = thin,
        };
        Body.Children.Add(hollow);
        // Корпус: бок и перед ниже крышки.
        Poly(Body, Face("#1C1730", "#120F1F"), new(w, d + lidH), new(w + d, lidH), new(w + d, h), new(w, h + d));
        Poly(Body, Face("#2A2344", "#171327"), new(0, d + lidH), new(w, d + lidH), new(w, h + d), new(0, h + d));
        // Рёбра жёсткости на передней грани.
        foreach (var x in new[] { w * 0.12, w * 0.88 })
            Body.Children.Add(new Line { StartPoint = new Point(x, d + lidH + h * 0.08), EndPoint = new Point(x, h + d - h * 0.08), Stroke = new SolidColorBrush(Gx.Alpha(Colors.White, 26)), StrokeThickness = thin * 2 });

        // Крышка: верхняя грань, бок крышки и передняя полоса с замком.
        Poly(Lid, Face("#3A3060", "#2A2347"), new(0, d), new(d, 0), new(w + d, 0), new(w, d));
        Poly(Lid, Face("#221C38", "#1A1530"), new(w, d), new(w + d, 0), new(w + d, lidH), new(w, d + lidH));
        Poly(Lid, Face("#332A55", "#241E3D"), new(0, d), new(w, d), new(w, d + lidH), new(0, d + lidH));
        // Полоса цветом редкости через всю крышку.
        var band = new Border
        {
            Width = w, Height = Math.Max(3, lidH * 0.16),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Tint, 0), new GradientStop(tint2, 1) },
            },
            BoxShadow = new BoxShadows(new BoxShadow { Blur = width / 12, Color = Gx.Alpha(Tint, 200) }),
        };
        Canvas.SetLeft(band, 0);
        Canvas.SetTop(band, d + lidH - band.Height);
        Lid.Children.Add(band);
        // Замок с логотипом ModLaunch — на стыке крышки и корпуса.
        var lockSize = h * 0.36;
        var lockBox = new Border
        {
            Width = lockSize, Height = lockSize, CornerRadius = new CornerRadius(lockSize * 0.28),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Look.Mix(Tint, Colors.White, 0.35), 0), new GradientStop(tint2, 1) },
            },
            BorderBrush = new SolidColorBrush(Gx.Alpha(Colors.White, 140)), BorderThickness = new Thickness(thin),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = width / 9, Color = Gx.Alpha(Tint, 170) }),
            Child = new Image { Source = Images.Asset("icon.png", 128), Width = lockSize * 0.62, Height = lockSize * 0.62 },
        };
        Canvas.SetLeft(lockBox, (w - lockSize) / 2);
        Canvas.SetTop(lockBox, d + lidH - lockSize * 0.45);
        Lid.Children.Add(lockBox);

        Lid.RenderTransformOrigin = new RelativePoint(0.5, d / Height, RelativeUnit.Relative);
        Children.Add(Glow);
        Children.Add(Body);
        Children.Add(Beam);
        Children.Add(Lid);
    }
}

/// <summary>Пьедестал-тень под кейсом и плавное покачивание (настоящий поворот в 3D).</summary>
public static class CaseKit
{
    /// <summary>Кейс на тени, для виджета и списков.</summary>
    public static Control Small(double width, string tier)
    {
        var box = new CaseBox(width, tier) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var shadow = new Border
        {
            Width = width * 1.1, Height = width * 0.16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Background = new RadialGradientBrush { GradientStops = { new GradientStop(Gx.Alpha(box.Tint, 110), 0), new GradientStop(Gx.Alpha(box.Tint, 0), 1) } },
            Margin = new Thickness(0, 0, 0, -width * 0.06),
        };
        return new Panel { Children = { shadow, box } };
    }

    public static string TierName(string tier) => I18n.T("v10.case.tier." + tier);
}
