using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 9.3: обложка мода — его собственная картинка из каталога, а не арт игры. Широкие картинки
/// (Nexus) занимают всю плитку; квадратные значки (Thunderstore) стоят в рамке на размытом фоне
/// из той же картинки, как обложки в магазинах приложений. Пока картинка грузится или если её
/// нет — затемнённый арт игры с буквами названия мода.
/// </summary>
public static class ModCover
{
    /// <summary>Крупная версия картинки: у Nexus вместо миниатюры — полноразмерная.</summary>
    public static string? HiRes(string? url) => url is not null && url.Contains("/thumbnails/", StringComparison.Ordinal) ? url.Replace("/thumbnails/", "/") : url;

    public static Control Create(GameState g, ModInfo m, int decode, bool big = false,
        HorizontalAlignment iconH = HorizontalAlignment.Center, VerticalAlignment iconV = VerticalAlignment.Center, double iconShare = 0.62)
    {
        var fallback = Fallback(g, m, decode, iconH, iconV);
        var back = new Image { Stretch = Stretch.UniformToFill, Effect = new BlurEffect { Radius = 34 }, IsVisible = false, IsHitTestVisible = false };
        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(110, 6, 6, 10)), IsVisible = false, IsHitTestVisible = false };
        var wide = new Image { Classes = { "zoom" }, Stretch = Stretch.UniformToFill, IsVisible = false, IsHitTestVisible = false };
        // Квадратный значок — в рамке со скруглением и тенью; размер — доля меньшей стороны плитки.
        var squareImage = new Image { Stretch = Stretch.UniformToFill };
        var square = new Border
        {
            ClipToBounds = true, CornerRadius = new CornerRadius(18), IsVisible = false, IsHitTestVisible = false,
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = squareImage, HorizontalAlignment = iconH, VerticalAlignment = iconV,
        };
        var frame = new Border { Child = square, BoxShadow = BoxShadows.Parse("0 16 40 0 #B0000000"), CornerRadius = new CornerRadius(18), HorizontalAlignment = iconH, VerticalAlignment = iconV, IsVisible = false };
        var host = new Panel { ClipToBounds = true, Children = { fallback, back, dim, wide, frame } };
        host.SizeChanged += (_, e) =>
        {
            var side = Math.Max(48, Math.Min(Math.Min(e.NewSize.Width, e.NewSize.Height) * iconShare, 220));
            square.Width = square.Height = side;
            square.CornerRadius = frame.CornerRadius = new CornerRadius(side * 0.18);
            var pad = Math.Min(e.NewSize.Width, e.NewSize.Height) * 0.12;
            frame.Margin = new Thickness(iconH == HorizontalAlignment.Left ? pad : 0, iconV == VerticalAlignment.Top ? pad : 0, iconH == HorizontalAlignment.Right ? pad * 1.6 : 0, iconV == VerticalAlignment.Bottom ? pad : 0);
        };

        void Apply(Bitmap bmp)
        {
            fallback.IsVisible = false;
            var ratio = bmp.PixelSize.Width / (double)Math.Max(1, bmp.PixelSize.Height);
            if (ratio > 1.25)
            {
                wide.Source = bmp;
                wide.IsVisible = true;
            }
            else
            {
                back.Source = bmp;
                squareImage.Source = bmp;
                back.IsVisible = dim.IsVisible = frame.IsVisible = square.IsVisible = true;
            }
        }

        var url = big ? HiRes(m.Icon) : m.Icon;
        if (!string.IsNullOrEmpty(url))
            _ = Images.FromUrl(url, decode).ContinueWith(async t =>
            {
                var bmp = t.Result;
                if (bmp is null && url != m.Icon && !string.IsNullOrEmpty(m.Icon)) bmp = await Images.FromUrl(m.Icon, decode);
                if (bmp is not null) Dispatcher.UIThread.Post(() => Apply(bmp));
            });
        return host;
    }

    /// <summary>Пока нет картинки: размытый тёмный арт игры и буквы названия мода в цветной плашке.</summary>
    static Control Fallback(GameState g, ModInfo m, int decode, HorizontalAlignment h, VerticalAlignment v)
    {
        var art = Ui.GameImage(g.Def, Math.Min(decode, 768), art: Images.Art.Hero);
        art.Effect = new BlurEffect { Radius = 16 };
        var initials = string.Concat(m.Name.Split(' ', '-', '_', '.').Where(w => w.Length > 0 && char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        if (initials == "") initials = "M";
        var hue = (int)(m.Name.Aggregate(23u, (h, c) => h * 31 + c) % 360);
        var badge = new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(16), HorizontalAlignment = h, VerticalAlignment = v,
            Margin = new Thickness(h == HorizontalAlignment.Left ? 28 : 0, v == VerticalAlignment.Top ? 28 : 0, h == HorizontalAlignment.Right ? 56 : 0, v == VerticalAlignment.Bottom ? 28 : 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Ui.HslColor(hue, 0.6, 0.5), 0), new GradientStop(Ui.HslColor((hue + 50) % 360, 0.65, 0.3), 1) },
            },
            BoxShadow = BoxShadows.Parse("0 12 30 0 #A0000000"),
            Child = new TextBlock { Text = initials, FontFamily = Gx.Display, FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        return new Panel { Children = { art, new Border { Background = new SolidColorBrush(Color.FromArgb(150, 6, 6, 10)) }, badge } };
    }
}
