using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>Снимки 11.0: главная, язык Creator Hub с песочницей, инжекты, часы по сборкам, настройки.</summary>
public static class V11Shots
{
    [DemoShots]
    static void Eleven0Home(Shots s)
    {
        Settings.Data.Remove("widgets");
        s.Window.Navigate(() => new HomePage());
        s.Pump(1400);
        s.Save("v11-0-home");
    }

    [DemoShots]
    static void Eleven2Injects(Shots s)
    {
        Inject.Demo();
        s.Window.Navigate(() => new CreatorPage("injects"));
        s.Pump(1000);
        s.Save("v11-3-injects");
    }

    [DemoShots]
    static void Eleven3Builds(Shots s)
    {
        BuildTime.Demo();
        s.Window.Navigate(() => new ProfilePage("builds"));
        s.Pump(1000);
        s.Save("v11-4-build-hours");
        var dir = Path.Combine(s.OutDir, "v11-5-achievements-card.png");
        ProfilePage.SaveCard(Achievements.All(), dir);
    }

    [DemoShots]
    static void Eleven4Settings(Shots s)
    {
        foreach (var tab in new[] { "general", "look", "launch" })
        {
            s.Window.Navigate(() => new SettingsPage(tab));
            s.Pump(900);
            s.Save("v11-6-settings-" + tab);
        }
    }

    [DemoShots]
    static void Eleven5Offline(Shots s)
    {
        Offline.Demo(true);
        s.Window.Navigate(() => new HomePage());
        s.Pump(900);
        s.Save("v11-7-auto-offline");
        Offline.Demo(false);
    }
}
