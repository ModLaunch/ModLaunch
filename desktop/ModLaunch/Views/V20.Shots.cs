using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 20.0 (MODLAUNCH_SHOTS=V20): Creator Hub с «ModScript X» («Создать», «Мои проекты») и «Рынком»,
/// скрипты вкладкой проектов, консоль, рынок без денег автора и «Аккаунт → Заработок».
/// </summary>
public static class V20Shots
{
    [DemoShots]
    static void V20Hub(Shots s)
    {
        XStudio.DemoProject();
        if (Projects.List().Count < 2)
            foreach (var id in new[] { "sdv-seeds", "valheim-builder" })
                Projects.Create(I18n.T($"cr.ex.{id}"), Templates.ById(id)!.Code);
        if (!Features.Inject.All().Any(i => i.Source == "import")) Features.Inject.Demo();
        foreach (var (tab, shot) in new[] { ("home", "v20-1-projects"), ("create", "v20-2-create"), ("scripts", "v20-3-scripts"), ("console", "v20-4-console") })
        {
            s.Window.Navigate(() => new CreatorPage(tab));
            s.Pump(1200);
            s.Save(shot);
        }
        MarketData.Demo(Demo.Listings(), Demo.Hub());
        s.Window.Navigate(() => new MarketPage());
        s.Pump(1400);
        s.Save("v20-5-market");
        s.Window.Navigate(() => new AccountPage("earnings"));
        s.Pump(1200);
        s.Save("v20-6-earnings");
        s.Window.CurrentPage?.Build();
        s.Pump(400);
    }
}
