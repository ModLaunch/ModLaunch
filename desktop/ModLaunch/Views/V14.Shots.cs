namespace ModLaunch.Views;

/// <summary>Снимки 14.0 (MODLAUNCH_SHOTS=V14): взрослая главная с рекламой справа, библиотека Steam, комиссия рынка, Creator Hub.</summary>
public static class V14Shots
{
    [DemoShots]
    static void V14Home(Shots s)
    {
        s.Window.Navigate(() => new HomePage());
        s.Pump(1400);
        s.Save("v14-1-home");
    }

    [DemoShots]
    static void V14Steam(Shots s)
    {
        s.Window.Navigate(() => new SteamLibraryPage(1245620));
        s.Pump(1400);
        s.Save("v14-2-steam");
    }

    [DemoShots]
    static void V14SteamNotInstalled(Shots s)
    {
        s.Window.Navigate(() => new SteamLibraryPage(292030));
        s.Pump(1200);
        s.Save("v14-3-steam-owned");
    }

    [DemoShots]
    static void V14Commission(Shots s)
    {
        CommissionPanel.Reset();
        s.Window.Navigate(() => new MarketPage("commission"));
        s.Pump(1600);
        s.Save("v14-4-commission");
    }

    [DemoShots]
    static void V14Store(Shots s)
    {
        s.Window.Navigate(() => new MarketPage("store"));
        s.Pump(1400);
        s.Save("v14-5-store");
    }

    [DemoShots]
    static void V14Hub(Shots s)
    {
        s.Window.Navigate(() => new CreatorPage("home"));
        s.Pump(1000);
        s.Save("v14-6-hub");
    }
}
