using Avalonia.Controls;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 2.0 (MODLAUNCH_SHOTS=V2): вид «Φ» — главная, библиотека, страница игры, Creator Hub («Создать»,
/// «Мои проекты», консоль, рынок), заработок, сетка золотого сечения, свёрнутая панель и светлая тема.
/// </summary>
public static class V2Shots
{
    static void Shot(Shots s, string name, Func<Page> make, int ms = 1300)
    {
        s.Window.Navigate(make);
        s.Pump(ms);
        s.Save(name);
    }

    static void HubDemo()
    {
        XStudio.DemoProject();
        if (Projects.List().Count < 2)
            foreach (var id in new[] { "sdv-seeds", "valheim-builder" })
                Projects.Create(I18n.T($"cr.ex.{id}"), Templates.ById(id)!.Code);
        if (!Features.Inject.All().Any(i => i.Source == "import")) Features.Inject.Demo();
        MarketData.Demo(Demo.Listings(), Demo.Hub());
    }

    [DemoShots]
    static void V2App(Shots s)
    {
        Shot(s, "v2-1-home", () => new HomePage(), 1700);
        Shot(s, "v2-2-library", () => new LibraryPage(), 1500);
        if (AppState.Games.FirstOrDefault(x => x.Status == Detect.Found) is { } g)
            Shot(s, "v2-3-game", () => new GamePage(g.Def.Id), 1500);
        Shot(s, "v2-4-settings", () => new SettingsPage("look"));
    }

    [DemoShots]
    static void V2Hub(Shots s)
    {
        HubDemo();
        foreach (var (tab, shot) in new[] { ("create", "v2-5-create"), ("home", "v2-6-projects"), ("console", "v2-7-console") })
            Shot(s, shot, () => new CreatorPage(tab));
        Shot(s, "v2-8-market", () => new MarketPage(), 1500);
        Shot(s, "v2-9-earnings", () => new AccountPage("earnings"));

        // Среда ModScript X и фокус (Ctrl+F11).
        var studio = new XStudio(XStudio.DemoProject());
        Shot(s, "v2-e-studio", () => studio, 1500);
        studio.ToggleFocus();
        s.Pump(900);
        s.Save("v2-f-focus");
        studio.ToggleFocus();
        s.Pump(300);

        // «Пока вы играли» — карточка после выхода из игры.
        s.Window.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Window.ShowDigest([I18n.T("v2.demo.d1"), I18n.T("v2.demo.d2"), I18n.T("v2.demo.d3")]);
        s.Pump(700);
        s.Save("v2-g-digest");
    }

    [DemoShots]
    static void V2Extras(Shots s)
    {
        var w = s.Window;
        w.Navigate(() => new HomePage());
        s.Pump(1500);
        w.TogglePhiGrid();
        s.Pump(500);
        s.Save("v2-a-grid-home");
        w.TogglePhiGrid();

        w.ToggleNarrow();
        Shot(s, "v2-b-narrow", () => new HomePage(), 1300);
        w.ToggleNarrow();
        s.Pump(300);

        var was = Settings.Data.Str("theme") ?? "phi";
        Look.SetTheme("phi-light");
        Shot(s, "v2-c-light-home", () => new HomePage(), 1500);
        HubDemo();
        Shot(s, "v2-d-light-create", () => new CreatorPage("create"));
        Look.SetTheme(was);
        s.Pump(300);
    }
}
