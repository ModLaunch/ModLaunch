using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 12.1: свой облик Creator Hub — «мастерская автора», а не витрина лаунчера. Только внутри Hub
/// (рынок и мастерская): глубокие чернила с тонкой сеткой чертежа (на светлых темах — бумага и синие
/// чернила), одна фирменная полоса фиолетовый → бирюзовый, моноширинные подписи «// РАЗДЕЛ»,
/// волосяные рамки с уголками вместо мягких залитых карточек. Цвета текста — из темы программы,
/// поэтому фон Hub держится той же светлоты, что и тема (Look.IsLight).
/// </summary>
public static class HubLook
{
    static bool Light => Look.IsLight;

    /// <summary>Моноширинный шрифт — тот же, что у редактора кода.</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace");

    static SolidColorBrush B(string hex) => new(Color.Parse(hex));

    /// <summary>Фон Hub: чернила или бумага.</summary>
    public static IBrush Ink => B(Light ? "#F3F1EA" : "#0A0C12");
    /// <summary>Листы и рамки.</summary>
    public static IBrush Panel => B(Light ? "#FBFAF6" : "#10131B");
    /// <summary>Поднятое: наведение, выбранное, полоса редактора.</summary>
    public static IBrush Raised => B(Light ? "#ECE9DF" : "#171B27");
    /// <summary>Волосяная линия.</summary>
    public static IBrush Edge => B(Light ? "#D9D4C6" : "#232839");
    /// <summary>Линия под курсором.</summary>
    public static IBrush EdgeHi => B(Light ? "#9AA6C8" : "#454E72");
    /// <summary>Акцент для текста и значков: фиолетовый и бирюзовый, читаемые на своём фоне.</summary>
    public static IBrush Violet => B(Light ? "#5B3FD0" : "#A48BFF");
    public static IBrush Cyan => B(Light ? "#0E7490" : "#45D3EE");
    /// <summary>Уголки рамок.</summary>
    static IBrush Tick => B(Light ? "#7A86B8" : "#6D62B8");

    /// <summary>Фирменная полоса: фиолетовый → бирюзовый (белый текст на ней читается).</summary>
    public static IBrush Signature(byte alpha = 255) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(alpha, 0x70, 0x48, 0xE8), 0), new GradientStop(Color.FromArgb(alpha, 0x0B, 0x8F, 0xB3), 1) },
    };

    /// <summary>Подпись «// РАЗДЕЛ» моноширинным шрифтом.</summary>
    public static TextBlock Eyebrow(string text, IBrush? color = null) => new()
    {
        Text = "// " + text.ToUpper(I18n.Culture), FontFamily = Mono, FontSize = 10.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.1,
        Foreground = color ?? Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Моноширинная мелочь: версии, счётчики, клавиши.</summary>
    public static TextBlock MonoText(string text, double size = 11.5, IBrush? color = null) => new()
    {
        Text = text, FontFamily = Mono, FontSize = size, Foreground = color ?? Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Клавиша-подсказка: «F5» в тонкой рамке.</summary>
    public static Control Key(string key) => new Border
    {
        BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0),
        VerticalAlignment = VerticalAlignment.Center, Child = MonoText(key, 10.5),
    };

    /// <summary>Рамка мастерской: лист с волосяной линией и уголками-засечками, как на чертеже.</summary>
    public static Control Frame(Control child, Thickness padding, bool ticks = true)
    {
        var box = new Border { Background = Panel, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = padding, Child = child };
        if (!ticks) return box;
        var panel = new Avalonia.Controls.Panel { Children = { box } };
        foreach (var (h, v, t) in new[]
        {
            (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(1, 1, 0, 0)), (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 1, 1, 0)),
            (HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(1, 0, 0, 1)), (HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 1, 1)),
        })
            panel.Children.Add(new Border { Width = 8, Height = 8, BorderBrush = Tick, BorderThickness = t, HorizontalAlignment = h, VerticalAlignment = v, IsHitTestVisible = false });
        return panel;
    }

    /// <summary>Заголовок блока: «// ПОДПИСЬ», название и справа — действия.</summary>
    public static Control Head(string eyebrow, string title, params Control[] actions)
    {
        var head = new DockPanel { LastChildFill = true };
        if (actions.Length > 0)
        {
            var right = Ui.Row(6, actions);
            right.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }
        var name = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.Bold, LetterSpacing = -0.2, TextTrimming = TextTrimming.CharacterEllipsis };
        head.Children.Add(Ui.Col(3, Eyebrow(eyebrow, Violet), name));
        return head;
    }

    /// <summary>
    /// Правила Hub для всего, что внутри root: карточки и плитки — листы с волосяной линией,
    /// кнопки прямее, главная кнопка — фирменной полосой, пункты боковой панели (hub-rail).
    /// Стили ближе к элементу сильнее стилей программы, а остальные страницы их не видят.
    /// </summary>
    public static void Scope(Control root)
    {
        void Add(Func<Selector?, Selector> sel, params (AvaloniaProperty P, object V)[] setters)
        {
            var style = new Style(sel);
            foreach (var (p, v) in setters) style.Setters.Add(new Setter(p, v));
            root.Styles.Add(style);
        }
        var none = new BoxShadows();
        Add(x => x.OfType<Border>().Class("card"),
            (Border.BackgroundProperty, Panel), (Border.BorderBrushProperty, Edge), (Border.BorderThicknessProperty, new Thickness(1)),
            (Border.CornerRadiusProperty, new CornerRadius(4)), (Border.BoxShadowProperty, none));
        Add(x => x.OfType<Border>().Class("store-tile").Class(":pointerover"), (Border.BoxShadowProperty, none), (Border.BorderBrushProperty, EdgeHi));
        Add(x => x.OfType<Button>(), (TemplatedControl.CornerRadiusProperty, new CornerRadius(4)));
        Add(x => x.OfType<TextBox>(), (TemplatedControl.CornerRadiusProperty, new CornerRadius(4)));
        Add(x => x.OfType<ComboBox>(), (TemplatedControl.CornerRadiusProperty, new CornerRadius(4)));
        Add(x => x.OfType<Button>().Class("chip"), (TemplatedControl.CornerRadiusProperty, new CornerRadius(3)));
        foreach (var cls in new[] { "tile", "card-btn" })
        {
            Add(x => x.OfType<Button>().Class(cls),
                (TemplatedControl.BackgroundProperty, Panel), (TemplatedControl.BorderBrushProperty, Edge),
                (TemplatedControl.BorderThicknessProperty, new Thickness(1)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(4)));
            Add(x => x.OfType<Button>().Class(cls).Class(":pointerover").Template().OfType<ContentPresenter>(),
                (ContentPresenter.BackgroundProperty, Raised), (ContentPresenter.BorderBrushProperty, EdgeHi), (ContentPresenter.BoxShadowProperty, none));
        }
        // Главная кнопка Hub — фирменная полоса, без свечения.
        Add(x => x.OfType<Button>().Class("primary"), (TemplatedControl.BackgroundProperty, Signature()), (TemplatedControl.ForegroundProperty, Brushes.White));
        Add(x => x.OfType<Button>().Class("primary").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Signature(225)), (ContentPresenter.ForegroundProperty, Brushes.White), (ContentPresenter.BoxShadowProperty, none));
        // Боковая панель: тихие строки, выбранная — поднятым фоном и полоской слева.
        Add(x => x.OfType<Button>().Class("hub-rail"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.ForegroundProperty, Ui.Res("Muted")),
            (TemplatedControl.PaddingProperty, new Thickness(0, 0, 10, 0)), (Layoutable.HeightProperty, 34.0),
            (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch), (ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            (ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center), (TemplatedControl.FontWeightProperty, FontWeight.Normal),
            (TemplatedControl.CornerRadiusProperty, new CornerRadius(3)));
        Add(x => x.OfType<Button>().Class("hub-rail").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Raised), (ContentPresenter.ForegroundProperty, Ui.Res("Text")));
        Add(x => x.OfType<Button>().Class("hub-rail").Class("active"),
            (TemplatedControl.BackgroundProperty, Raised), (TemplatedControl.ForegroundProperty, Ui.Res("Text")), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        // Строки списков (недавние проекты, путь автора): без фона, под курсором — поднятые.
        Add(x => x.OfType<Button>().Class("hub-row"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.PaddingProperty, new Thickness(8, 7)),
            (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch), (ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            (TemplatedControl.FontWeightProperty, FontWeight.Normal), (TemplatedControl.CornerRadiusProperty, new CornerRadius(3)));
        Add(x => x.OfType<Button>().Class("hub-row").Class(":pointerover").Template().OfType<ContentPresenter>(), (ContentPresenter.BackgroundProperty, Raised));
        // Вкладки раздела: подчёркивание фирменным цветом.
        Add(x => x.OfType<Button>().Class("hub-tab"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.ForegroundProperty, Ui.Res("Muted")),
            (TemplatedControl.PaddingProperty, new Thickness(2, 8)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
            (TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 2)), (TemplatedControl.BorderBrushProperty, Brushes.Transparent),
            (TemplatedControl.FontWeightProperty, FontWeight.Medium));
        Add(x => x.OfType<Button>().Class("hub-tab").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Brushes.Transparent), (ContentPresenter.ForegroundProperty, Ui.Res("Text")), (ContentPresenter.BorderBrushProperty, Edge));
        Add(x => x.OfType<Button>().Class("hub-tab").Class("active"),
            (TemplatedControl.ForegroundProperty, Ui.Res("Text")), (TemplatedControl.BorderBrushProperty, Signature()), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        Add(x => x.OfType<Button>().Class("hub-tab").Class("active").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BorderBrushProperty, Signature()));
    }

    /// <summary>Фон Hub: чернила, сетка чертежа (крупная клетка через пять мелких) и слабый свет фирменного цвета сверху.</summary>
    public sealed class Blueprint : Control
    {
        const double Cell = 24;

        public Blueprint() { IsHitTestVisible = false; ClipToBounds = true; }

        public override void Render(DrawingContext ctx)
        {
            var light = Light;
            var w = Bounds.Width;
            var h = Bounds.Height;
            ctx.FillRectangle(Ink, new Rect(0, 0, w, h));
            var glow = new RadialGradientBrush
            {
                Center = new RelativePoint(0.18, 0, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.18, 0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb((byte)(light ? 22 : 34), 0x70, 0x48, 0xE8), 0), new GradientStop(Color.FromArgb(0, 0x0B, 0x8F, 0xB3), 1) },
            };
            ctx.FillRectangle(glow, new Rect(0, 0, w, h));
            var line = light ? Color.FromRgb(0x2B, 0x4C, 0x9B) : Color.FromRgb(0x8C, 0x9A, 0xFF);
            var minor = new Pen(new SolidColorBrush(Color.FromArgb((byte)(light ? 16 : 13), line.R, line.G, line.B)), 1);
            var major = new Pen(new SolidColorBrush(Color.FromArgb((byte)(light ? 30 : 25), line.R, line.G, line.B)), 1);
            for (var i = 1; i * Cell < w; i++) ctx.DrawLine(i % 5 == 0 ? major : minor, new Point(i * Cell + 0.5, 0), new Point(i * Cell + 0.5, h));
            for (var i = 1; i * Cell < h; i++) ctx.DrawLine(i % 5 == 0 ? major : minor, new Point(0, i * Cell + 0.5), new Point(w, i * Cell + 0.5));
        }
    }
}
