using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 20.0 «Платформа» (MODLAUNCH_SHOTS=V20): шапка с разделами, вкладки раздела, список игр в библиотеке,
/// нижняя полоса с загрузками и друзьями, витрина магазина, сообщество, «Создателям», узкое окно и английский.
/// </summary>
public static class V20Shots
{
    [DemoShots]
    static void V20Platform(Shots s)
    {
        var w = s.Window;
        MarketData.Demo(Demo.Listings(), Demo.Hub());

        w.OpenSection(Section.Store);
        s.Pump(1500);
        s.Save("v20-01-store");

        w.OpenSection(Section.Library);
        s.Pump(900);
        s.Save("v20-02-library-home");

        w.Navigate(() => new GamePage("subnautica"));
        s.Pump(1100);
        s.Save("v20-03-game");

        w.Navigate(() => new GamePage("lethal-company", "catalog"));
        s.Pump(1100);
        s.Save("v20-04-game-catalog");

        w.Navigate(() => new LibraryPage());
        s.Pump(900);
        s.Save("v20-05-library-all");

        w.OpenSection(Section.Community);
        s.Pump(900);
        s.Save("v20-06-community");

        w.Navigate(() => new ProfilePage());
        s.Pump(900);
        s.Save("v20-07-profile");

        w.OpenSection(Section.Creator);
        s.Pump(1100);
        s.Save("v20-08-creator");

        w.Navigate(() => new MarketPage("store"));
        s.Pump(1100);
        s.Save("v20-09-market");

        w.Navigate(() => new SettingsPage());
        s.Pump(900);
        s.Save("v20-10-settings");

        // Раздел помнит, где в нём были: «Библиотека» возвращает к последней игре.
        w.OpenSection(Section.Library);
        s.Pump(700);
        s.Save("v20-11-library-remembers");

        w.OpenDownloads();
        s.Pump(600);
        s.Save("v20-12-downloads");
        w.CloseOverlays();

        w.OpenAccountPanel();
        s.Pump(600);
        s.Save("v20-13-account");
        w.CloseAccountPanel();

        w.Width = 1024;
        w.Height = 700;
        w.OpenSection(Section.Library);
        w.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Save("v20-14-narrow");
        w.Width = 1366;
        w.Height = 800;

        I18n.Set("en");
        w.OpenSection(Section.Store);
        s.Pump(1200);
        s.Save("v20-15-store-en");
        w.Navigate(() => new GamePage("valheim"));
        s.Pump(1000);
        s.Save("v20-16-game-en");
        I18n.Set("ru");

        // Светлое и «чёрно-зелёное» оформления: рамка платформы берёт цвета темы.
        foreach (var style in new[] { "redwhite", "blackgreen" })
        {
            Look.SetStyle(style);
            w.Navigate(() => new StorePage());
            s.Pump(1200);
            s.Save($"v20-17-{style}-store");
            w.Navigate(() => new GamePage("subnautica"));
            s.Pump(1100);
            s.Save($"v20-18-{style}-game");
        }
        Look.SetStyle("dark");
    }
}
