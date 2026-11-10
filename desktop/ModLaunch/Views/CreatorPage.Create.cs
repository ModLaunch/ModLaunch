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
/// 22.0: «Новый мод». Три способа начать, по порядку того, кому что нужно: идея для первого мода (блоки),
/// анкета (пять вопросов — и среда ModScript с кодом под игру) и шаблон или импорт папки. Рекламный блок среды
/// с таблицей «чем лучше редактора» и счётчиками убран: страница помогает начать, а не продаёт.
/// Консоль и справка — пункт «Справочник ModScript» слева.
/// </summary>
public sealed partial class CreatorPage
{
    /// <summary>
    /// 26.0: «Другие способы начать» — внутри колонки анкеты (Form). Тому, у кого ещё нет ни одного мода, над анкетой —
    /// мод из блоков одной кнопкой (KidStart): первый мод за минуту, без вопросов.
    /// </summary>
    Control CreateView() => FirstTime() && _form.Step == 0 ? Ui.Col(28, KidStart(), Form()) : Form();

    /// <summary>Своих модов ещё нет (ни скриптов, ни проектов правок). Для снимков — DemoFirstTime.</summary>
    internal static bool? DemoFirstTime;

    static bool FirstTime() => DemoFirstTime ?? XProjects.List().Count + Projects.List().Count == 0;

    /// <summary>Значок слева, название и пояснение справа (пояснение переносится по ширине плитки).</summary>
    static Control WayContent(string icon, string key)
    {
        var mark = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = HubLook.Raised, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top, Child = Ui.Icon(icon, 17, HubLook.Violet) };
        var dock = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        dock.Children.Add(mark);
        dock.Children.Add(Ui.Col(2, Ui.Text(I18n.T(key), "strong"), Ui.Text(I18n.T(key + ".text"), "small muted", wrap: true)));
        return dock;
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

    /// <summary>Знак ModScript: янтарная плитка с тёмными фигурными скобками (в среде и на ярлыках).</summary>
    internal static Control XMark(double size) => new Border
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Background = HubLook.Amber, VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = "{ }", FontFamily = HubLook.Mono, FontWeight = FontWeight.Bold, FontSize = size * 0.4, Foreground = HubLook.OnAmber,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>Другие способы начать: шаблон или импорт папки / архива / старого проекта.</summary>
    Control OtherWays()
    {
        // 26.0: ровно в ширину колонки анкеты (была MaxWidth 900 — край не совпадал ни с анкетой, ни с карточкой справа).
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -12, 0) };
        foreach (var (icon, key, run) in new (string, string, Action)[]
        {
            (Icons.Wand, "v18.other.template", () => Go("examples")),
            (Icons.Folder, "v18.other.import", NewProjectDialog),
        })
        {
            var b = new Button
            {
                Classes = { "tile" }, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(16, 14),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Content = WayContent(icon, key),
            };
            b.Click += (_, _) => run();
            grid.Children.Add(b);
        }
        return Ui.Col(12, Section(I18n.T("v18.other.title")), grid);
    }
}
