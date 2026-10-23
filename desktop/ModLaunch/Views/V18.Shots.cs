using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 18.0 (MODLAUNCH_SHOTS=V18): «Мои проекты» слева («Создать» и «Доступные»), плитка под курсором
/// («Запуск» и «Настройка»), «Создать» со знаком ModScript X, «Скрипты» с защитой, настройка скрипта и среда.
/// </summary>
public static class V18Shots
{
    static void Seed()
    {
        XStudio.DemoProject();
        if (Projects.List().Count < 2)
            foreach (var id in new[] { "sdv-seeds", "valheim-builder" })
                Projects.Create(I18n.T($"cr.ex.{id}"), Templates.ById(id)!.Code);
        if (!Features.Inject.All().Any(i => i.Source == "import")) Features.Inject.Demo();
    }

    /// <summary>Навести курсор на первый элемент нужного вида (плитка, строка) — как живой пользователь.</summary>
    static void HoverFirst<T>(Shots s, Func<T, bool> pick) where T : Control
    {
        var target = s.Window.GetVisualDescendants().OfType<T>().FirstOrDefault(pick);
        if (target?.TranslatePoint(new Point(target.Bounds.Width / 2, 30), s.Window) is not { } p) return;
        s.Window.MouseMove(p);
        s.Pump(500);
    }

    [DemoShots]
    static void V18Hub(Shots s)
    {
        Seed();
        s.Window.Navigate(() => new CreatorPage());
        s.Pump(1200);
        s.Save("v18-1-available");
        HoverFirst<Button>(s, b => b.Classes.Contains("tile"));
        s.Save("v18-2-hover");
        s.Window.MouseMove(new Point(5, 5));
        s.Pump(300);

        s.Window.Navigate(() => CreatorPage.NewForm());
        s.Pump(1400);
        s.Save("v18-3-create");

        s.Window.Navigate(() => new CreatorPage("scripts"));
        s.Pump(1200);
        s.Save("v18-4-scripts");
        HoverFirst<Border>(s, b => b.Child is DockPanel && b.Padding == new Thickness(12, 10));
        s.Save("v18-5-scripts-hover");
        s.Window.MouseMove(new Point(5, 5));
        s.Pump(300);
        if (s.Window.CurrentPage is CreatorPage page && Features.Inject.All().FirstOrDefault(i => i.Source == "import") is { } item)
        {
            page.DemoSettings(item.Id);
            s.Pump(700);
            s.Save("v18-6-script-settings");
            s.Window.CloseDialog();
        }

        var id = XStudio.DemoProject();
        s.Window.Navigate(() => new XStudio(id, "files", "problems"));
        s.Pump(1500);
        s.Save("v18-7-studio");
        s.Window.Navigate(() => new XStudio(id, "settings", "problems"));
        s.Pump(1200);
        s.Save("v18-8-studio-settings");
        // Перестройка той же страницы (смена языка) не должна падать.
        s.Window.CurrentPage?.Build();
        s.Pump(500);
    }
}
