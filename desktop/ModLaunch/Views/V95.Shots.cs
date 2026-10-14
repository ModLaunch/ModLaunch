using Avalonia;
using Avalonia.Controls;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using ModLaunch.Minecraft;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>Снимки 9.5: главная «Сводка важного», советы по вашим модам в ленте.</summary>
public static class V95Shots
{
    static ScrollViewer? Scroller(Control? page) => page is UserControl { Content: ScrollViewer sv } ? sv : null;

    static void SeedFriends() => Friends.DemoView = new FriendsView(true, true, "ML-4K2Q-9", "online",
    [
        new("u1", "Lena", "playing", "lethal-company", "Lethal Company", DateTime.UtcNow.AddMinutes(-40), DateTime.UtcNow),
        new("u2", "Max_K", "online", "", "", null, DateTime.UtcNow),
        new("u3", "Dima", "playing", "minecraft", "Minecraft", DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow),
        new("u4", "Sasha", "offline", "", "", null, DateTime.UtcNow.AddHours(-6)),
        new("u5", "Kirill", "offline", "", "", null, DateTime.UtcNow.AddDays(-2)),
    ],
    [new("u9", "NightOwl", DateTime.UtcNow.AddMinutes(-12))], [], null);

    [DemoShots]
    static void Home95Shots(Shots s)
    {
        var w = s.Window;
        MarketData.Demo(Demo.Listings(), Demo.Hub());
        HomePage.DemoFeed(Demo.Listings(), Demo.Hub());

        // Спокойная главная: нет обновлений, ошибок и друзей.
        w.Navigate(() => new HomePage());
        s.Pump(1500);
        w.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Save("v95-0-home-calm");

        // Есть что делать: обновления модов и сборки, ошибка загрузки, заявка в друзья, друзья в сети.
        SeedFriends();
        Features.ModUpdates.Found["subnautica"] =
        [
            new("nexus:subnautica:12", "12", "Map Mod", "2.1.23", "2.3.1", null, true, "nexus"),
            new("nexus:subnautica:1262", "1262", "Nautilus", "2.1.24", "2.2.0", null, false, "nexus"),
        ];
        if (Mc.Active is { } build) McContent.Demo.Seed(build);
        var failed = new Job { Title = "Vehicle Framework", GameName = "Subnautica", Status = JobStatus.Failed, Step = I18n.T("dl.failed"), Error = "timeout" };
        var running = new Job { Title = "Sodium", GameName = "Minecraft", Status = JobStatus.Running, Step = I18n.T("dl.download"), Ratio = 0.46 }.WithDemoBytes(4_100_000, 8_900_000, 1_700_000);
        foreach (var (title, game) in new[] { ("Nautilus", "Subnautica"), ("Fabric API", "Minecraft"), ("More Suits", "Lethal Company") })
            History.Add(new Job { Title = title, GameName = game, Status = JobStatus.Done });
        Jobs.All.Insert(0, failed);
        Jobs.All.Insert(0, running);
        AppState.Notify();
        w.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Save("v95-1-home");
        if (Scroller(w.CurrentPage) is { } sv) sv.Offset = new Vector(0, 520);
        s.Pump(600);
        s.Save("v95-1-home-down");

        foreach (var style in new[] { "redwhite", "blackgreen" })
        {
            if (Look.Styles.All(x => x.Id != style)) continue;
            Look.SetStyle(style);
            w.Navigate(() => new HomePage());
            s.Pump(1000);
            s.Save($"v95-2-{style}-home");
        }
        Look.SetStyle("dark");

        var (wasW, wasH) = (w.Width, w.Height);
        w.Width = 2560;
        w.Height = 1440;
        s.Pump(600);
        w.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Save("v95-3-home-wide");
        w.Width = 1100;
        w.Height = 760;
        s.Pump(600);
        w.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Save("v95-3-home-narrow");
        w.Width = wasW;
        w.Height = wasH;
        s.Pump(600);

        // Лента: блок «Для вас» и плитки с причиной.
        HomePage.DemoLayouts(false, "bigL4", "chips", "towersL", "bannerChips");
        w.Navigate(() => new HomePage(feed: true));
        s.Pump(1500);
        s.Save("v95-4-feed");
        foreach (var y in new[] { 900, 1800 })
        {
            if (Scroller(w.CurrentPage) is { } fv) fv.Offset = new Vector(0, y);
            s.Pump(700);
            s.Save($"v95-4-feed-{y}");
        }
        HomePage.DemoLayouts(false);

        Jobs.All.Remove(failed);
        Jobs.All.Remove(running);
        Friends.DemoView = null;
    }
}
