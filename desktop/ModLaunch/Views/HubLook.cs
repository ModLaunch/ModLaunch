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
/// 22.0: облик Creator Hub — «верстак». Фон и поверхности берутся из темы программы: хаб — часть ModLaunch,
/// а не отдельный «чертёж». Один свой цвет — янтарь ModScript (лампа над верстаком): главная кнопка, выбранная
/// вкладка, отметки. Без сеток, засечек, градиентов и подписей капсом; моноширинный шрифт — только для кода
/// и клавиш. Единственная яркая деталь хаба — ярлыки модов на обложках игр (CreatorPage.Mine.cs).
/// Имена членов прежние (Violet, Cyan, Eyebrow, Frame…), чтобы все экраны хаба сменили облик разом.
/// </summary>
public static class HubLook
{
    static bool Light => Look.IsLight;

    /// <summary>Моноширинный шрифт — тот же, что у редактора кода. Только для кода и клавиш.</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace");

    static SolidColorBrush B(string hex) => new(Color.Parse(hex));

    /// <summary>Фон хаба — фон программы.</summary>
    public static IBrush Ink => Ui.Res("Bg");
    /// <summary>Листы и рамки.</summary>
    public static IBrush Panel => Ui.Res("Surface");
    /// <summary>Поднятое: наведение, выбранное.</summary>
    public static IBrush Raised => Ui.Res("Surface2");
    /// <summary>Линия.</summary>
    public static IBrush Edge => Ui.Res("Line");
    /// <summary>Линия под курсором.</summary>
    public static IBrush EdgeHi => Ui.Res("Faint");

    /// <summary>Янтарь ModScript — для заливки (главная кнопка, отметка выбранного). Текст на нём — тёмный.</summary>
    public static readonly Color AmberColor = Color.Parse("#F0B429");
    public static IBrush Amber => new SolidColorBrush(AmberColor);
    /// <summary>Тёмный текст на янтаре: читается на нём в обеих темах.</summary>
    public static IBrush OnAmber => B("#1B1406");

    /// <summary>Янтарь для текста и значков: на тёмном фоне — светлый, на светлом — тёмный, чтобы читался.</summary>
    public static IBrush Violet => B(Light ? "#8A5A00" : "#F0B429");
    /// <summary>Тихий второй цвет (пути, подписи): приглушённый текст темы.</summary>
    public static IBrush Cyan => Ui.Res("Muted");

    /// <summary>Заливка своего цвета ModScript (раньше — градиент). alpha — для наведения.</summary>
    public static IBrush Signature(byte alpha = 255) => new SolidColorBrush(Color.FromArgb(alpha, AmberColor.R, AmberColor.G, AmberColor.B));
    public static IBrush Premium(byte alpha = 255) => Signature(alpha);

    /// <summary>Заголовок группы: обычный регистр, полужирный, цвет текста. (Раньше — «// ПОДПИСЬ» моноширинным капсом.)</summary>
    public static TextBlock Eyebrow(string text, IBrush? color = null) => new()
    {
        Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"),
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Мелкая подпись: версии, счётчики, даты. Шрифт программы — моноширинный оставлен коду.</summary>
    public static TextBlock MonoText(string text, double size = 11.5, IBrush? color = null) => new()
    {
        Text = text, FontSize = size + 1, Foreground = color ?? Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center,
    };


    /// <summary>22.0: подпись над блоком на страницах хаба — обычным регистром (общий Gx.Eyebrow пишет капсом с полоской).</summary>
    public static Control Label(string text, IBrush? color = null, double size = 11) => new TextBlock
    {
        Text = text, FontSize = size + 1.5, FontWeight = FontWeight.SemiBold, Foreground = color ?? Ui.Res("Muted"),
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>22.0: число с подписью на страницах хаба — подпись обычным регистром (общий Gx.Stat пишет её капсом).</summary>
    public static Control Stat(string value, string label, IBrush? valueColor = null, double size = 26, IBrush? labelColor = null) => new StackPanel
    {
        Spacing = 1,
        Children =
        {
            new TextBlock { Text = value, FontFamily = Look.Display, FontSize = size, FontWeight = FontWeight.Bold, Foreground = valueColor ?? Ui.Res("Text") },
            new TextBlock { Text = label, FontSize = 12, Foreground = labelColor ?? Ui.Res("Muted") },
        },
    };
    /// <summary>Клавиша-подсказка: «F5» в рамке клавиши.</summary>
    public static Control Key(string key) => new Border
    {
        BorderBrush = Edge, BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0), HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = key, FontFamily = Mono, FontSize = 10.5, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center },
    };

    /// <summary>Лист: поверхность темы и тонкая линия. ticks оставлен для совместимости — засечек больше нет.</summary>
    public static Control Frame(Control child, Thickness padding, bool ticks = true) =>
        new Border { Background = Panel, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = padding, Child = child };

    /// <summary>Заголовок блока: название и справа — действия. Подпись над названием больше не показывается.</summary>
    public static Control Head(string eyebrow, string title, params Control[] actions)
    {
        var head = new DockPanel { LastChildFill = true };
        if (actions.Length > 0)
        {
            var right = Ui.Row(6, actions);
            right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }
        var name = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(name);
        return head;
    }

    /// <summary>
    /// Правила хаба для всего, что внутри root: листы со скруглением 10, кнопки 8, главная кнопка — янтарная
    /// с тёмным текстом, пункты боковой панели (hub-rail), строки (hub-row) и вкладки (hub-tab).
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
            (Border.CornerRadiusProperty, new CornerRadius(10)), (Border.BoxShadowProperty, none));
        Add(x => x.OfType<Border>().Class("store-tile").Class(":pointerover"), (Border.BoxShadowProperty, none), (Border.BorderBrushProperty, EdgeHi));
        Add(x => x.OfType<Button>(), (TemplatedControl.CornerRadiusProperty, new CornerRadius(8)));
        Add(x => x.OfType<TextBox>(), (TemplatedControl.CornerRadiusProperty, new CornerRadius(8)));
        Add(x => x.OfType<ComboBox>(), (TemplatedControl.CornerRadiusProperty, new CornerRadius(8)));
        Add(x => x.OfType<Button>().Class("chip"), (TemplatedControl.CornerRadiusProperty, new CornerRadius(16)));
        // 23.0: переключатель вида (рынок, покупки): выбранный — поднятой плашкой, остальные — тихим текстом.
        Add(x => x.OfType<Button>().Class("pivot"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.ForegroundProperty, Ui.Res("Muted")), (TemplatedControl.PaddingProperty, new Thickness(12, 7)));
        Add(x => x.OfType<Button>().Class("pivot").Class("active"),
            (TemplatedControl.BackgroundProperty, Raised), (TemplatedControl.ForegroundProperty, Ui.Res("Text")), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        // Выбранный чип (игра в фильтре) — янтарной рамкой на поднятом фоне, а не синим цветом темы.
        var amberSoft = new SolidColorBrush(Color.FromArgb(38, 0xF0, 0xB4, 0x29));
        Add(x => x.OfType<Button>().Class("chip").Class("active"),
            (TemplatedControl.BackgroundProperty, amberSoft), (TemplatedControl.BorderBrushProperty, Amber), (TemplatedControl.ForegroundProperty, Ui.Res("Text")));
        Add(x => x.OfType<Button>().Class("chip").Class("active").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, amberSoft), (ContentPresenter.BorderBrushProperty, Amber), (ContentPresenter.ForegroundProperty, Ui.Res("Text")));
        // Включённый чип-переключатель (события в анкете) — янтарём, а не цветом темы.
        Add(x => x.OfType<ToggleButton>().Class("chip").Class(":checked").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Amber), (ContentPresenter.ForegroundProperty, OnAmber), (ContentPresenter.BorderBrushProperty, Amber));
        Add(x => x.OfType<ToggleButton>().Class("chip").Class(":checked"), (TemplatedControl.ForegroundProperty, OnAmber));
        foreach (var cls in new[] { "tile", "card-btn" })
        {
            Add(x => x.OfType<Button>().Class(cls),
                (TemplatedControl.BackgroundProperty, Panel), (TemplatedControl.BorderBrushProperty, Edge),
                (TemplatedControl.BorderThicknessProperty, new Thickness(1)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(10)));
            Add(x => x.OfType<Button>().Class(cls).Class(":pointerover").Template().OfType<ContentPresenter>(),
                (ContentPresenter.BackgroundProperty, Raised), (ContentPresenter.BorderBrushProperty, EdgeHi), (ContentPresenter.BoxShadowProperty, none));
        }
        // Главная кнопка хаба — янтарь с тёмным текстом; под курсором чуть светлее.
        Add(x => x.OfType<Button>().Class("primary"), (TemplatedControl.BackgroundProperty, Amber), (TemplatedControl.ForegroundProperty, OnAmber));
        Add(x => x.OfType<Button>().Class("primary").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, B("#F6C451")), (ContentPresenter.ForegroundProperty, OnAmber), (ContentPresenter.BoxShadowProperty, none));
        Add(x => x.OfType<Button>().Class("primary").Class(":disabled").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Raised), (ContentPresenter.ForegroundProperty, Ui.Res("Faint")));
        // Боковая панель: тихие строки, выбранная — поднятым фоном и полужирным.
        Add(x => x.OfType<Button>().Class("hub-rail"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.ForegroundProperty, Ui.Res("Muted")),
            (TemplatedControl.PaddingProperty, new Thickness(0, 0, 10, 0)), (Layoutable.HeightProperty, 36.0),
            (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch), (ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            (ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center), (TemplatedControl.FontWeightProperty, FontWeight.Normal),
            (TemplatedControl.CornerRadiusProperty, new CornerRadius(8)));
        Add(x => x.OfType<Button>().Class("hub-rail").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Raised), (ContentPresenter.ForegroundProperty, Ui.Res("Text")));
        Add(x => x.OfType<Button>().Class("hub-rail").Class("active"),
            (TemplatedControl.BackgroundProperty, Raised), (TemplatedControl.ForegroundProperty, Ui.Res("Text")), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        // Строки списков: без фона, под курсором — поднятые.
        Add(x => x.OfType<Button>().Class("hub-row"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.PaddingProperty, new Thickness(8, 7)),
            (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch), (ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            (TemplatedControl.FontWeightProperty, FontWeight.Normal), (TemplatedControl.CornerRadiusProperty, new CornerRadius(8)));
        Add(x => x.OfType<Button>().Class("hub-row").Class(":pointerover").Template().OfType<ContentPresenter>(), (ContentPresenter.BackgroundProperty, Raised));
        // Вкладки раздела: выбранная — янтарной чертой снизу.
        Add(x => x.OfType<Button>().Class("hub-tab"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.ForegroundProperty, Ui.Res("Muted")),
            (TemplatedControl.PaddingProperty, new Thickness(2, 8)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
            (TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 2)), (TemplatedControl.BorderBrushProperty, Brushes.Transparent),
            (TemplatedControl.FontWeightProperty, FontWeight.Medium));
        Add(x => x.OfType<Button>().Class("hub-tab").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Brushes.Transparent), (ContentPresenter.ForegroundProperty, Ui.Res("Text")), (ContentPresenter.BorderBrushProperty, Edge));
        Add(x => x.OfType<Button>().Class("hub-tab").Class("active"),
            (TemplatedControl.ForegroundProperty, Ui.Res("Text")), (TemplatedControl.BorderBrushProperty, Amber), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        Add(x => x.OfType<Button>().Class("hub-tab").Class("active").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BorderBrushProperty, Amber));
    }

    /// <summary>Фон хаба: ровный фон программы (раньше — сетка чертежа со свечением).</summary>
    public sealed class Blueprint : Control
    {
        public Blueprint() { IsHitTestVisible = false; ClipToBounds = true; }

        public override void Render(DrawingContext ctx) => ctx.FillRectangle(Ink, new Rect(0, 0, Bounds.Width, Bounds.Height));
    }
}
