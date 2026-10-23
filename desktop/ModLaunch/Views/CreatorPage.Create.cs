using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 18.0: «Мои проекты → Создать». Сверху — знак среды ModScript X: что она умеет и чем отличается от обычного
/// редактора (цифры живые: сколько движков знает мост, на сколько языков переводит, за сколько миллисекунд
/// проверяет мод на этом компьютере). Ниже — анкета нового проекта (16.0), под ней — другие способы начать:
/// шаблон или импорт/классический проект. Раньше то же было разбросано: баннер «Мод за три шага» над списком,
/// «Новый проект» в шапке, пунктирная плитка, «Импорт» и «Отмена» под анкетой.
/// </summary>
public sealed partial class CreatorPage
{
    Control CreateView() => Ui.Col(20, XBanner(), Form(), OtherWays());

    /// <summary>За сколько среда разбирает и проверяет типичный мод (Arena) на этом компьютере — среднее пяти прогонов.</summary>
    static double? _checkMs;

    internal static double CheckMs()
    {
        if (_checkMs is { } known) return known;
        var text = ArenaExample();
        MX.ModxDoc.Parse(text);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) MX.ModxDoc.Parse(text);
        return (_checkMs = sw.Elapsed.TotalMilliseconds / 5).Value;
    }

    /// <summary>Знак ModScript X: монограмма, название, чем хороша, живые цифры и сравнение с обычным редактором.</summary>
    static Control XBanner()
    {
        var engines = MX.Bridge.Known.Count();
        var langs = MX.Target.All.Select(t => t.Language).Distinct().Count();
        var mono = XMark(56);
        var title = Ui.Col(2,
            new TextBlock { Text = "ModScript X", FontFamily = Look.Display, FontSize = 30, FontWeight = FontWeight.Black, LetterSpacing = -0.8, Foreground = Ui.Res("Text") },
            HubLook.MonoText(I18n.T("v18.x.sub").ToUpper(I18n.Culture), 10.5, HubLook.Cyan));
        title.VerticalAlignment = VerticalAlignment.Center;
        var chips = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (icon, text) in new[]
        {
            (Icons.Activity, I18n.T("v18.x.chip.check", ("ms", CheckMs().ToString(CheckMs() < 10 ? "0.0" : "0"))) ),
            (Icons.Layers, I18n.T("v18.x.chip.engines", ("n", engines))),
            (Icons.Code, I18n.T("v18.x.chip.langs", ("n", langs))),
            (Icons.Play, I18n.T("v18.x.chip.sandbox")),
            (Icons.Zap, I18n.T("v18.x.chip.inject")),
        })
            chips.Children.Add(new Border
            {
                BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 8, 8),
                Background = HubLook.Raised, Child = Ui.Row(6, Ui.Icon(icon, 13, HubLook.Cyan), HubLook.MonoText(text, 11, Ui.Res("Text"))),
            });
        var left = Ui.Col(14, Ui.Row(16, mono, title), Ui.Text(I18n.T("v18.x.tagline"), "muted", wrap: true), chips);
        left.MaxWidth = 640;
        left.HorizontalAlignment = HorizontalAlignment.Left;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 28 };
        grid.Children.Add(left);
        var compare = Compare();
        Grid.SetColumn(compare, 1);
        grid.Children.Add(compare);

        // Рамка — фирменной полосой, а не волосяной линией: среда выделяется на сетке Hub.
        var frame = new Border
        {
            Background = HubLook.Panel, BorderBrush = HubLook.Premium(170), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(26, 22), ClipToBounds = true, Child = new Panel { Children = { Glow(), grid } },
        };
        return frame;
    }

    /// <summary>Монограмма X на фирменной полосе — знак среды (в «Создать», в панели и в самой среде).</summary>
    internal static Control XMark(double size) => new Border
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.22), Background = HubLook.Premium(), VerticalAlignment = VerticalAlignment.Center,
        BoxShadow = new BoxShadows(new BoxShadow { Blur = size * 0.5, Color = Look.Mature ? Color.FromArgb(70, 0x6C, 0x83, 0xB0) : Color.FromArgb(90, 0x70, 0x48, 0xE8) }),
        Child = new TextBlock
        {
            Text = "X", FontFamily = Look.Display, FontWeight = FontWeight.Black, FontSize = size * 0.54, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>Мягкий свет фирменного цвета в правом верхнем углу знака.</summary>
    static Control Glow() => new Border
    {
        IsHitTestVisible = false, Margin = new Thickness(-26, -22),
        Background = Look.Mature ? Brushes.Transparent : new RadialGradientBrush
        {
            Center = new RelativePoint(0.85, 0, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.85, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.2, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(40, 0x0B, 0x8F, 0xB3), 0), new GradientStop(Color.FromArgb(0, 0x70, 0x48, 0xE8), 1) },
        },
    };

    /// <summary>ModScript X против обычного редактора кода: восемь вещей, которых у редактора нет.</summary>
    static Control Compare()
    {
        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,72,72"), RowSpacing = 0 };
        void Cell(Control c, int row, int col)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            table.Children.Add(c);
        }
        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var x = HubLook.MonoText("MODSCRIPT X", 9.5, HubLook.Violet);
        var other = HubLook.MonoText(I18n.T("v18.cmp.other").ToUpper(I18n.Culture), 9.5, Ui.Res("Faint"));
        x.HorizontalAlignment = other.HorizontalAlignment = HorizontalAlignment.Center;
        x.TextWrapping = other.TextWrapping = TextWrapping.Wrap;
        x.TextAlignment = other.TextAlignment = TextAlignment.Center;
        x.Margin = other.Margin = new Thickness(0, 0, 0, 8);
        Cell(x, 0, 1);
        Cell(other, 0, 2);
        // 19.0: ещё две строки — отладчик с шагами назад и проверка до запуска.
        string[] rows = ["v18.cmp.1", "v18.cmp.2", "v18.cmp.3", "v19.cmp.debug", "v19.cmp.lint", "v18.cmp.4", "v18.cmp.5", "v18.cmp.6"];
        for (var i = 1; i <= rows.Length; i++)
        {
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var line = new Border { BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6), Child = Ui.Text(I18n.T(rows[i - 1]), "small") };
            Cell(line, i, 0);
            var yes = new Border { BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Child = Ui.Icon(Icons.Check, 15, HubLook.Cyan) };
            var no = new Border { BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Child = HubLook.MonoText("—", 12, Ui.Res("Faint")) };
            ((Control)no.Child!).HorizontalAlignment = HorizontalAlignment.Center;
            Cell(yes, i, 1);
            Cell(no, i, 2);
        }
        var col = Ui.Col(8, HubLook.Eyebrow(I18n.T("v18.cmp.title"), HubLook.Violet), table);
        col.VerticalAlignment = VerticalAlignment.Center;
        col.MinWidth = 380;
        return col;
    }

    /// <summary>Другие способы начать: шаблон или импорт папки/архива (классический мастер ModScript).</summary>
    Control OtherWays()
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var (icon, key, run) in new (string, string, Action)[]
        {
            (Icons.Wand, "v18.other.template", () => Go("examples")),
            (Icons.Folder, "v18.other.import", NewProjectDialog),
        })
        {
            var b = new Button
            {
                Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 0), Padding = new Thickness(16, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = Ui.Row(12, new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(4), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), Child = Ui.Icon(icon, 16, HubLook.Violet) },
                    Ui.Col(2, Ui.Text(I18n.T(key), "strong"), Ui.Text(I18n.T(key + ".text"), "small muted", wrap: true))),
            };
            b.Click += (_, _) => run();
            grid.Children.Add(b);
        }
        return Ui.Col(10, Section(I18n.T("v18.other.title")), grid);
    }
}
