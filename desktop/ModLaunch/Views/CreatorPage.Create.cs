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
    /// <summary>
    /// 20.0: «ModScript X → Создать»: знак среды, анкета нового проекта и другие способы начать — шаблон, импорт,
    /// а также консоль и справка ModScript (своего пункта слева у них больше нет).
    /// </summary>
    /// <remarks>
    /// 2.0 «без шума»: знак среды, таблица «лучше редактора» и пять плашек над анкетой убраны — анкета сразу под
    /// заголовком, слева (61,8 % ширины); справа (38,2 %) — будущий проект, «Создать» и четыре тихие строки о том,
    /// чем хороша среда. Другие способы начать — строкой ссылок внизу.
    /// </remarks>
    Control CreateView() => Ui.Col(Phi.S34, Form(), OtherWays());

    /// <summary>2.0: чем хороша ModScript X — знак, название и четыре строки с галочкой, без таблицы и плашек.</summary>
    static Control XFacts()
    {
        var head = Ui.Row(Phi.S13 - 3, XMark(26), Ui.Col(0,
            new TextBlock { Text = "ModScript X", FontSize = 14, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = I18n.T("v18.x.sub"), FontSize = Phi.Caption, Foreground = Ui.Res("Faint") }));
        var col = Ui.Col(Phi.S8, head);
        foreach (var key in new[] { "v18.cmp.1", "v19.cmp.debug", "v19.cmp.lint", "v18.cmp.4" })
            col.Children.Add(Ui.Row(Phi.S8, Ui.Icon(Icons.Check, 14, Ui.Res("Brand2")), Ui.Text(I18n.T(key), "small muted")));
        col.Children.Add(Ui.Text(I18n.T("v18.x.chip.check", ("ms", CheckMs().ToString(CheckMs() < 10 ? "0.0" : "0"))), "caption"));
        col.Margin = new Thickness(Phi.S5, Phi.S8, 0, 0);
        return col;
    }

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

    /// <summary>Монограмма X на фирменной полосе — знак среды (в «Создать», в панели и в самой среде).</summary>
    internal static Control XMark(double size) => new Border
    {
        // 2.0: без свечения вокруг — знак держится цветом, а не тенью.
        Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = HubLook.Premium(), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = "X", FontWeight = FontWeight.Black, FontSize = size * 0.54, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>Другие способы начать — строкой тихих ссылок (2.0: вместо четырёх плиток с пояснениями).</summary>
    Control OtherWays()
    {
        var row = new WrapPanel();
        foreach (var (icon, key, run) in new (string, string, Action)[]
        {
            (Icons.Wand, "v18.other.template", () => Go("examples")),
            (Icons.Folder, "v18.other.import", NewProjectDialog),
            (Icons.Activity, "v20.x.way.console", () => { _ms = "console"; Go("x"); }),
            (Icons.Book, "v20.x.way.docs", () => { _ms = "commands"; Go("x"); }),
        })
        {
            var b = Ui.Button(I18n.T(key), run, "ghost", icon, I18n.T(key + ".text"));
            b.Foreground = Ui.Res("Muted");
            b.Margin = new Thickness(0, 0, Phi.S8, 0);
            row.Children.Add(b);
        }
        row.Margin = new Thickness(-Phi.S13, 0, 0, 0);
        return Ui.Col(Phi.S8, Ui.Text(I18n.T("v18.other.title"), "caption"), row);
    }
}
