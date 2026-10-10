using Avalonia;
using Avalonia.Controls;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>Снимки 9.3: лента, восемь анимаций открытия игры, рынок креаторов, страница лота и автора.</summary>
public static class V93Shots
{
    static ScrollViewer? Scroller(Control? page) => page is UserControl { Content: ScrollViewer sv } ? sv : null;

    [DemoShots]
    static void Feed93Shots(Shots s)
    {
        var w = s.Window;
        var listings = Demo.Listings();
        var hub = Demo.Hub();
        MarketData.Demo(listings, hub);
        HomePage.DemoFeed(listings, hub);
        w.Navigate(() => new HomePage());
        s.Pump(1500);
        w.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Save("v93-1-feed");
        foreach (var y in new[] { 900, 1900, 2900, 3900 })
        {
            if (Scroller(w.CurrentPage) is { } sv) sv.Offset = new Vector(0, y);
            s.Pump(700);
            s.Save($"v93-1-feed-{y}");
        }
        foreach (var style in Look.Styles)
        {
            Look.SetStyle(style.Id);
            w.Navigate(() => new HomePage());
            s.Pump(1000);
            s.Save($"v93-2-{style.Id}-feed");
            if (Scroller(w.CurrentPage) is { } sv) sv.Offset = new Vector(0, 1400);
            s.Pump(700);
            s.Save($"v93-2-{style.Id}-feed-down");
        }
        Look.SetStyle("dark");
    }

    [DemoShots]
    static void GameFx93Shots(Shots s)
    {
        var w = s.Window;
        foreach (var kind in GameOpenFx.All)
        {
            w.Navigate(() => new LibraryPage());
            s.Pump(900);
            var run = w.NavigateWithFx(() => new GamePage("subnautica"), kind, new Point(250, 300));
            s.Pump(250);
            if (run is null) { s.Save($"v93-3-gamefx-{kind.ToString().ToLowerInvariant()}-none"); continue; }
            foreach (var ms in new[] { 140, 330 })
            {
                run.Seek(ms);
                s.Pump(80);
                s.Save($"v93-3-gamefx-{kind.ToString().ToLowerInvariant()}-{ms}");
            }
            run.Finish();
            s.Pump(150);
        }
    }

    [DemoShots]
    static void Market93Shots(Shots s)
    {
        var w = s.Window;
        var listings = Demo.Listings();
        var hub = Demo.Hub();
        MarketData.Demo(listings, hub);
        MarketLocal.ToggleWish("seamoth");
        MarketLocal.ToggleWish("monster");
        MarketLocal.ToggleWish("vikinghud");
        MarketLocal.ToggleFollow("demoMira", "Mira");
        foreach (var tab in MarketPage.Tabs)
        {
            if (tab == "library")
                MarketPage.DemoPurchases(
                [
                    new("daynight", "Day/Night tweaks", "script", "Nox", 0, DateTime.UtcNow.AddDays(-3)),
                    new("sort", "Сортировка инвентаря", "code", "Kira", 9900, DateTime.UtcNow.AddDays(-12)),
                ]);
            if (tab == "studio")
            {
                var mine = listings.Where(l => l.Author == "Mira").ToList();
                var sales = Enumerable.Range(0, 26).Select(i => new Sale(mine[i % mine.Count].Id, "b" + i, new[] { "Kira", "Vega", "Nox", "Echo", "Alpin" }[i % 5], "demoMira",
                    mine[i % mine.Count].Title, mine[i % mine.Count].Price == 0 ? 0 : mine[i % mine.Count].Price, mine[i % mine.Count].Price / 10, DateTime.UtcNow.AddDays(-(i * 7 % 29)).AddHours(-i))).Where(x => x.Price > 0).ToList();
                MarketPage.DemoStudio(mine, sales, new Wallet(412_300, 1_284_500, 0, 800_000));
            }
            w.Navigate(() => new MarketPage(tab));
            s.Pump(1100);
            s.Save($"v93-4-market-{tab}");
        }
        w.Navigate(() => new MarketPage("store"));
        s.Pump(900);
        if (Scroller(w.CurrentPage) is { } sv) sv.Offset = new Vector(0, 900);
        s.Pump(600);
        s.Save("v93-4-market-store-down");
        if (Scroller(w.CurrentPage) is { } sv2) sv2.Offset = new Vector(0, 1900);
        s.Pump(600);
        s.Save("v93-4-market-store-down2");

        w.Navigate(() => new ListingPage(listings[1]));
        s.Pump(1100);
        s.Save("v93-5-listing");
        if (Scroller(w.CurrentPage) is { } sv3) sv3.Offset = new Vector(0, 700);
        s.Pump(600);
        s.Save("v93-5-listing-down");
        w.Navigate(() => new AuthorPage("demoMira", "Mira"));
        s.Pump(1000);
        s.Save("v93-6-author");

        foreach (var style in Look.Styles)
        {
            Look.SetStyle(style.Id);
            w.Navigate(() => new MarketPage());
            s.Pump(1000);
            s.Save($"v93-7-{style.Id}-market");
            w.Navigate(() => new ListingPage(listings[0]));
            s.Pump(1000);
            s.Save($"v93-7-{style.Id}-listing");
        }
        Look.SetStyle("dark");
    }
}
