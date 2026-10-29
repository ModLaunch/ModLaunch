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

    // 2.0 «без шума»: у Creator Hub больше нет своих чернил, сетки чертежа, неона и стали — те же поверхности,
    // линии и один акцент, что у всего приложения. Имена прежние, чтобы все экраны Hub перекрасились разом.

    /// <summary>Фон Hub — лист страницы.</summary>
    public static IBrush Ink => Ui.Res("Layer");
    /// <summary>Листы и рамки.</summary>
    public static IBrush Panel => Ui.Res("Surface");
    /// <summary>Поднятое: наведение, выбранное, полоса редактора.</summary>
    public static IBrush Raised => Ui.Res("Surface2");
    /// <summary>Волосяная линия.</summary>
    public static IBrush Edge => Ui.Res("Line");
    /// <summary>Линия под курсором.</summary>
    public static IBrush EdgeHi => Ui.Res("Surface3");
    /// <summary>Акцент для текста и значков — цвет акцента приложения, читаемый на фоне.</summary>
    public static IBrush Violet => Ui.Res("Brand2");
    /// <summary>Второстепенный цвет подписей (бывший бирюзовый) — приглушённый текст.</summary>
    public static IBrush Cyan => Ui.Res("Muted");

    /// <summary>Фирменная заливка — сплошной цвет акцента (полоски, подчёркивания, главная кнопка).</summary>
    public static IBrush Signature(byte alpha = 255) => Ui.Res("Brand") is ISolidColorBrush b
        ? new SolidColorBrush(Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B)) : Ui.Res("Brand");

    /// <summary>
    /// Знак ModScript X: монограмма — цвет акцента с лёгким переходом в золото логотипа (единственный «премиальный» штрих);
    /// рамки (alpha меньше 255) — обычная линия, без цветной обводки.
    /// </summary>
    public static IBrush Premium(byte alpha = 255)
    {
        if (alpha < 255) return Edge;
        var a = Ui.Res("Brand") is ISolidColorBrush b ? b.Color : Color.Parse("#6D5EF0");
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Look.Mix(a, Colors.White, 0.12), 0), new GradientStop(a, 0.6), new GradientStop(Look.Mix(a, Color.Parse("#F2B544"), 0.35), 1) },
        };
    }

    /// <summary>
    /// Подпись раздела. 2.0: без «// », капса и моноширинного шрифта — обычный мелкий полужирный текст
    /// приглушённого цвета (цвет из вызова больше не красит подписи: акцент — только у выбранного).
    /// </summary>
    public static TextBlock Eyebrow(string text, IBrush? color = null) => new()
    {
        Text = text, FontSize = Phi.Small, FontWeight = FontWeight.SemiBold,
        Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Мелочь: версии, счётчики, даты. 2.0: шрифтом интерфейса (моноширинный — только у кода, см. <see cref="Code"/>).</summary>
    public static TextBlock MonoText(string text, double size = 11.5, IBrush? color = null) => new()
    {
        Text = text, FontSize = Math.Max(size, Phi.Caption), Foreground = color is null || ReferenceEquals(color, Cyan) || ReferenceEquals(color, Violet) ? Ui.Res("Faint") : color,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Моноширинный текст — для кода, позиций в файле и клавиш в среде разработки.</summary>
    public static TextBlock Code(string text, double size = 11.5, IBrush? color = null) => new()
    {
        Text = text, FontFamily = Mono, FontSize = size, Foreground = color ?? Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Клавиша-подсказка: «F5» в тонкой рамке.</summary>
    public static Control Key(string key) => new Border
    {
        BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0),
        VerticalAlignment = VerticalAlignment.Center, Child = Code(key, 10.5),
    };

    /// <summary>Рамка мастерской. 2.0: обычный лист — волосяная линия и угол 13, без уголков-засечек чертежа.</summary>
    public static Control Frame(Control child, Thickness padding, bool ticks = true) =>
        new Border { Background = Panel, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Phi.R13), Padding = padding, Child = child };

    /// <summary>Заголовок блока: название и справа — действия (2.0: без подписи над ним — она повторяла раздел).</summary>
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
        var name = new TextBlock { Text = title, FontSize = Phi.H3, FontWeight = FontWeight.SemiBold, LetterSpacing = -0.15, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(name);
        return head;
    }

    /// <summary>
    /// Правила Hub для всего, что внутри root. 2.0: те же углы и поверхности, что у остального приложения (8 у кнопок,
    /// 13 у листов), главная кнопка — акцентом, строки и пункты — тихие, вкладки — подчёркивание цветом акцента.
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
        var r8 = new CornerRadius(Phi.R8);
        var r13 = new CornerRadius(Phi.R13);
        Add(x => x.OfType<Border>().Class("card"),
            (Border.BackgroundProperty, Panel), (Border.BorderBrushProperty, Edge), (Border.BorderThicknessProperty, new Thickness(1)),
            (Border.CornerRadiusProperty, r13), (Border.BoxShadowProperty, none));
        Add(x => x.OfType<Border>().Class("store-tile").Class(":pointerover"), (Border.BoxShadowProperty, none), (Border.BorderBrushProperty, EdgeHi));
        Add(x => x.OfType<Button>(), (TemplatedControl.CornerRadiusProperty, r8));
        Add(x => x.OfType<TextBox>(), (TemplatedControl.CornerRadiusProperty, r8));
        Add(x => x.OfType<ComboBox>(), (TemplatedControl.CornerRadiusProperty, r8));
        Add(x => x.OfType<Button>().Class("chip"), (TemplatedControl.CornerRadiusProperty, new CornerRadius(999)));
        foreach (var cls in new[] { "tile", "card-btn" })
        {
            Add(x => x.OfType<Button>().Class(cls),
                (TemplatedControl.BackgroundProperty, Panel), (TemplatedControl.BorderBrushProperty, Edge),
                (TemplatedControl.BorderThicknessProperty, new Thickness(1)), (TemplatedControl.CornerRadiusProperty, r13));
            Add(x => x.OfType<Button>().Class(cls).Class(":pointerover").Template().OfType<ContentPresenter>(),
                (ContentPresenter.BackgroundProperty, Raised), (ContentPresenter.BorderBrushProperty, EdgeHi), (ContentPresenter.BoxShadowProperty, none));
        }
        // Строки списков (недавние проекты, путь автора): без фона, под курсором — поднятые.
        Add(x => x.OfType<Button>().Class("hub-row"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.PaddingProperty, new Thickness(Phi.S8, 7)),
            (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch), (ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            (TemplatedControl.FontWeightProperty, FontWeight.Normal), (TemplatedControl.CornerRadiusProperty, r8));
        Add(x => x.OfType<Button>().Class("hub-row").Class(":pointerover").Template().OfType<ContentPresenter>(), (ContentPresenter.BackgroundProperty, Raised));
        // Пункты внутри страниц (бывшая вторая панель): тихие строки, выбранная — подложкой.
        Add(x => x.OfType<Button>().Class("hub-rail"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.ForegroundProperty, Ui.Res("Muted")),
            (TemplatedControl.PaddingProperty, new Thickness(Phi.S8, 0)), (Layoutable.HeightProperty, Phi.S34),
            (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch), (ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            (ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center), (TemplatedControl.FontWeightProperty, FontWeight.Normal),
            (TemplatedControl.CornerRadiusProperty, r8));
        Add(x => x.OfType<Button>().Class("hub-rail").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Raised), (ContentPresenter.ForegroundProperty, Ui.Res("Text")));
        Add(x => x.OfType<Button>().Class("hub-rail").Class("active"),
            (TemplatedControl.BackgroundProperty, Raised), (TemplatedControl.ForegroundProperty, Ui.Res("Text")), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        // Вкладки раздела: подчёркивание цветом акцента.
        Add(x => x.OfType<Button>().Class("hub-tab"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent), (TemplatedControl.ForegroundProperty, Ui.Res("Muted")),
            (TemplatedControl.PaddingProperty, new Thickness(2, 10)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
            (TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 2)), (TemplatedControl.BorderBrushProperty, Brushes.Transparent),
            (TemplatedControl.FontWeightProperty, FontWeight.Medium));
        Add(x => x.OfType<Button>().Class("hub-tab").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BackgroundProperty, Brushes.Transparent), (ContentPresenter.ForegroundProperty, Ui.Res("Text")), (ContentPresenter.BorderBrushProperty, Edge));
        Add(x => x.OfType<Button>().Class("hub-tab").Class("active"),
            (TemplatedControl.ForegroundProperty, Ui.Res("Text")), (TemplatedControl.BorderBrushProperty, Signature()), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        Add(x => x.OfType<Button>().Class("hub-tab").Class("active").Class(":pointerover").Template().OfType<ContentPresenter>(),
            (ContentPresenter.BorderBrushProperty, Signature()));
    }

    /// <summary>Фон Hub. 2.0: ровный лист страницы — без сетки чертежа и цветного света (они и были «шумом»).</summary>
    public sealed class Blueprint : Control
    {
        public Blueprint() { IsHitTestVisible = false; }

        public override void Render(DrawingContext ctx) => ctx.FillRectangle(Ink, new Rect(Bounds.Size));
    }
}
