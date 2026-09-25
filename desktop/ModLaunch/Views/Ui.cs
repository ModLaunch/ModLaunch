using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using Path = Avalonia.Controls.Shapes.Path;

namespace ModLaunch.Views;

/// <summary>Иконки в стиле Lucide: контуры 24×24, рисуются обводкой.</summary>
public static class Icons
{
    public const string Home = "M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z M9 22V12h6v10";
    public const string Settings = "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z";
    public const string Search = "M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z M21 21l-4.35-4.35";
    public const string Download = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3";
    public const string Play = "M6 3l14 9-14 9z";
    public const string Folder = "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z";
    public const string Back = "M19 12H5 M12 19l-7-7 7-7";
    public const string Forward = "M5 12h14 M12 5l7 7-7 7";
    public const string Minimize = "M5 12h14";
    public const string Maximize = "M5 5h14v14H5z";
    public const string Close = "M18 6L6 18 M6 6l12 12";
    public const string Trash = "M3 6h18 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6 M10 11v6 M14 11v6 M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2";
    public const string Check = "M20 6L9 17l-5-5";
    public const string List = "M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01";
    public const string Bag = "M6 2L3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4z M3 6h18 M16 10a4 4 0 0 1-8 0";
    public const string Refresh = "M23 4v6h-6 M20.49 15a9 9 0 1 1-2.12-9.36L23 10";
    public const string External = "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6 M15 3h6v6 M10 14L21 3";
    public const string FilePlus = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M12 18v-6 M9 15h6";
    public const string Alert = "M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z M12 9v4 M12 17h.01";
    public const string Star = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01z";
    public const string Heart = "M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z";
    public const string Clock = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 6v6l4 2";
    public const string Sort = "M11 5h10 M11 9h7 M11 13h4 M3 17l3 3 3-3 M6 18V4";
    public const string Package = "M16.5 9.4l-9-5.19 M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z M3.27 6.96L12 12.01l8.73-5.05 M12 22.08V12";
    public const string Globe = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M2 12h20 M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z";
    public const string Key = "M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.78 7.78 5.5 5.5 0 0 1 7.78-7.78zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4";
}

/// <summary>Небольшие строительные блоки интерфейса, чтобы экраны читались как разметка.</summary>
public static class Ui
{
    public static IBrush Res(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Magenta;

    public static IBrush Hex(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>Иконка: контур в квадрате 24×24, масштабируется целиком (и прямые линии тоже).</summary>
    public static Control Icon(string data, double size = 18, IBrush? stroke = null, bool fill = false)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        if (fill) path.Fill = stroke ?? Res("Text");
        else if (stroke is not null) path.Stroke = stroke;
        else path.Bind(Shape.StrokeProperty, new Avalonia.Data.Binding("Foreground") { RelativeSource = new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor) { AncestorType = typeof(TemplatedControl) } });
        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Canvas { Width = 24, Height = 24, Children = { path } },
        };
    }

    public static TextBlock Text(string text, string classes = "", double? size = null, IBrush? color = null, bool wrap = false)
    {
        var t = new TextBlock { Text = text, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) t.Classes.Add(c);
        if (size is double s) t.FontSize = s;
        if (color is not null) t.Foreground = color;
        return t;
    }

    public static StackPanel Row(double spacing, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        p.Children.AddRange(children);
        return p;
    }

    public static StackPanel Col(double spacing, params Control[] children)
    {
        var p = new StackPanel { Spacing = spacing };
        p.Children.AddRange(children);
        return p;
    }

    public static Button Button(string text, Action onClick, string classes = "", string? icon = null, string? tip = null)
    {
        object content = icon is null ? text : Row(8, Icon(icon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        if (icon is not null && text == "") content = Icon(icon, 18);
        var b = new Button { Content = content };
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(c);
        b.Click += (_, _) => onClick();
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    public static Border Card(Control child, double padding = 20) => new Border { Child = child, Padding = new Thickness(padding), Classes = { "card" } };

    public static Border Dot(IBrush color, double size = 8) => new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size), Background = color, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Картинка по ссылке: пока грузится — буквы на цветном фоне.</summary>
    public static Control Thumb(string? url, string name, double size, double radius = 12, int decode = 160)
    {
        var initials = string.Concat(name.Split(' ', '-', '_').Where(w => w.Length > 0 && char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        var hue = (int)(name.Aggregate(17u, (h, c) => h * 31 + c) % 360);
        var fallback = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(HslColor(hue, 0.55, 0.42), 0), new GradientStop(HslColor((hue + 50) % 360, 0.6, 0.3), 1) },
            },
            Child = new TextBlock { Text = initials, FontWeight = FontWeight.Bold, FontSize = size * 0.3, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var image = new Image { Stretch = Stretch.UniformToFill };
        var host = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), ClipToBounds = true, Child = new Panel { Children = { fallback, image } } };
        if (!string.IsNullOrEmpty(url))
        {
            _ = Images.FromUrl(url, decode).ContinueWith(t =>
            {
                if (t.Result is Bitmap bmp) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bmp);
            });
        }
        return host;
    }

    public static Color HslColor(double h, double s, double l)
    {
        double C = (1 - Math.Abs(2 * l - 1)) * s, X = C * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - C / 2;
        var (r, g, b) = h switch { < 60 => (C, X, 0d), < 120 => (X, C, 0d), < 180 => (0d, C, X), < 240 => (0d, X, C), < 300 => (X, 0d, C), _ => (C, 0d, X) };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public static string Ago(DateTime? when)
    {
        if (when is not DateTime w) return "";
        var days = (int)(DateTime.UtcNow - w).TotalDays;
        if (days <= 0) return I18n.T("time.today");
        return I18n.T("time.daysAgo." + I18n.Plural(days, "one", "few", "many"), ("n", days));
    }

    public static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
