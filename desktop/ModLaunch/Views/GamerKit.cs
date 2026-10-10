using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 9.3: «игровые» детали оформления — подписи в духе HUD (капсом, с полоской акцента),
/// скошенные ярлыки цены и типа, крупные цифры счётчиков, свечение цветом акцента.
/// Используются лентой и рынком креаторов; работают во всех трёх оформлениях.
/// </summary>
public static class Gx
{
    /// <summary>Заголовочный шрифт ленты и рынка — широкий Unbounded (встроен в программу).</summary>
    public static FontFamily Display => Look.Unbounded;

    public static Color Accent => Ui.Res("Brand") is ISolidColorBrush b ? b.Color : Color.Parse(Look.Accent);

    public static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>«▍ПОДОБРАНО ДЛЯ ВАС» — подпись капсом с полоской акцента слева.</summary>
    public static Control Eyebrow(string text, IBrush? color = null, double size = 11)
    {
        var bar = new Border { Width = 3, Height = size + 2, CornerRadius = new CornerRadius(1), Background = Ui.Res("Brand"), VerticalAlignment = VerticalAlignment.Center };
        var words = new TextBlock
        {
            Text = text.ToUpper(I18n.Culture), FontSize = size, FontWeight = FontWeight.Bold, LetterSpacing = 1.3,
            Foreground = color ?? Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { bar, words } };
    }

    /// <summary>Скошенный ярлык (цена, «Бесплатно», тип лота) — как ценники в играх.</summary>
    public static Control Tag(string text, IBrush? background = null, IBrush? foreground = null, double size = 11.5)
    {
        var words = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = FontWeight.Bold, Foreground = foreground ?? Brushes.White,
            RenderTransform = new SkewTransform(12, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border
        {
            Background = background ?? new SolidColorBrush(Color.FromArgb(200, 10, 10, 14)), Padding = new Thickness(9, 3), CornerRadius = new CornerRadius(3),
            RenderTransform = new SkewTransform(-12, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = words,
        };
    }

    /// <summary>Ярлык цены: бесплатно — зелёный, платно — цвет акцента.</summary>
    public static Control Price(string text, bool free) =>
        Tag(text, free ? Ui.Res("Good") : Ui.Res("Brand"), free ? new SolidColorBrush(Color.Parse("#06140C")) : Ui.Res("OnBrand"), 12.5);

    /// <summary>Крупная цифра счётчика с подписью под ней (профиль игрока, статистика студии).</summary>
    public static Control Stat(string value, string label, IBrush? valueColor = null, double size = 26, IBrush? labelColor = null)
    {
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = value, FontFamily = Display, FontSize = size, FontWeight = FontWeight.Bold, Foreground = valueColor ?? Ui.Res("Text") },
                new TextBlock { Text = label.ToUpper(I18n.Culture), FontSize = 10.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.1, Foreground = labelColor ?? Ui.Res("Muted") },
            },
        };
    }

    /// <summary>Мягкое свечение цветом акцента вокруг элемента.</summary>
    public static BoxShadows Glow(byte alpha = 120, double blur = 24, double spread = 0) =>
        new(new BoxShadow { Blur = blur, Spread = spread, Color = Alpha(Accent, alpha) });

    /// <summary>Затемнение снизу вверх — чтобы белый текст читался на любой картинке.</summary>
    public static IBrush ShadeUp(double from = 0.35, byte alpha = 235) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 6, 6, 9), from),
            new GradientStop(Color.FromArgb((byte)(alpha * 0.8), 6, 6, 9), from + (1 - from) * 0.6),
            new GradientStop(Color.FromArgb(alpha, 6, 6, 9), 1),
        },
    };

    /// <summary>Затемнение слева направо — под текст в левой части широкого баннера.</summary>
    public static IBrush ShadeLeft(double to = 0.7, byte alpha = 235) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(alpha, 6, 6, 9), 0),
            new GradientStop(Color.FromArgb((byte)(alpha * 0.65), 6, 6, 9), to * 0.5),
            new GradientStop(Color.FromArgb(0, 6, 6, 9), to),
        },
    };

    /// <summary>Акцентная дымка по краю картинки — «неоновый» отсвет.</summary>
    public static Control Haze(bool right = false) => new Border
    {
        IsHitTestVisible = false,
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(right ? 1 : 0, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(right ? 0.4 : 0.6, 0.3, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Alpha(Accent, 90), 0), new GradientStop(Alpha(Accent, 0), 1) },
        },
    };

    /// <summary>Сетка «как в HUD» — тонкие линии поверх фона промо-плиток.</summary>
    public static Control Scanlines() => new Border
    {
        IsHitTestVisible = false, Opacity = 0.16,
        Background = new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Repeat,
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(0, 4, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(90, 255, 255, 255), 1) },
        },
    };

    /// <summary>Градиент из цвета игры (или акцента) в почти чёрный — фон, когда картинки нет.</summary>
    public static Control Gradient(string? hex, double angle = 1)
    {
        var a = hex is null ? Accent : Color.Parse(hex);
        return new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(angle, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Look.Mix(a, Colors.White, 0.08), 0), new GradientStop(Look.Mix(a, Color.Parse("#0B0B10"), 0.72), 0.7), new GradientStop(Color.Parse("#0B0B10"), 1) },
            },
        };
    }

    /// <summary>Заголовок крупным «игровым» шрифтом.</summary>
    public static TextBlock Title(string text, double size, IBrush? color = null, int lines = 2) => new()
    {
        Text = text, FontFamily = Display, FontSize = size, FontWeight = FontWeight.Bold, LetterSpacing = -0.4,
        Foreground = color ?? Brushes.White, TextWrapping = TextWrapping.Wrap, MaxLines = lines, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Полоса прогресса уровня (профиль игрока).</summary>
    public static Control Bar(double value, double height = 6)
    {
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = Ui.Res("Brand"), HorizontalAlignment = HorizontalAlignment.Left, BoxShadow = Glow(160, 10) };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Child = fill };
        track.SizeChanged += (_, e) => fill.Width = Math.Max(height, e.NewSize.Width * Math.Clamp(value, 0, 1));
        return track;
    }
}
