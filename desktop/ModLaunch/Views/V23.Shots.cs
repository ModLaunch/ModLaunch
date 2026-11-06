using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>Снимки 23.0 (MODLAUNCH_SHOTS=V23): рынок одной страницей, моды сообщества, мои покупки.</summary>
public static class V23Shots
{
    /// <summary>Прокрутить самую большую прокрутку страницы на долю высоты (0…1).</summary>
    internal static void Scroll(Shots s, double part)
    {
        if (s.Window.CurrentPage is not Control page) return;
        var sv = page.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.Extent.Height > v.Viewport.Height + 1).OrderByDescending(v => v.Extent.Height).FirstOrDefault();
        if (sv is null) return;
        sv.Offset = new Vector(sv.Offset.X, Math.Max(0, (sv.Extent.Height - sv.Viewport.Height) * part));
        s.Pump(500);
    }

    [DemoShots]
    static void V23Market(Shots s)
    {
        MarketData.Demo(Demo.Listings(), Demo.Hub());
        s.Window.Navigate(() => new MarketPage());
        s.Pump(1500);
        s.Save("v23-m1-market");
        Scroll(s, 0.6);
        s.Save("v23-m2-market-grid");
        s.Window.Navigate(() => MarketPage.CodePieces());
        s.Pump(1200);
        s.Save("v23-m3-market-code");
        s.Window.Navigate(() => new MarketPage("workshop"));
        s.Pump(1200);
        s.Save("v23-m4-workshop");
        MarketPage.DemoPurchases([new Purchase("x1", "Arena Rewards", "script", "Alpin", 9900, DateTime.UtcNow.AddDays(-3))]);
        s.Window.Navigate(() => new MarketPage("wishlist"));
        s.Pump(1200);
        s.Save("v23-m5-wishlist");
    }

    [DemoShots]
    static void V23Form(Shots s)
    {
        foreach (var (what, step, name, shot) in new[] { ("code", 0, "", "v23-f1-what"), ("code", 1, "", "v23-f2-game"), ("code", 2, "", "v23-f3-does"), ("code", 3, "Золото за волны", "v23-f4-name"), ("files", 1, "", "v23-f5-files-game"), ("pack", 2, "Вечер с друзьями", "v23-f6-pack-name"), ("world", 1, "", "v23-f7-world-game") })
        {
            s.Window.Navigate(() => CreatorPage.DemoForm(what, step, name));
            s.Pump(1300);
            s.Save(shot);
        }
    }

    [DemoShots]
    static void V23Catalog(Shots s)
    {
        Settings.Data["catalogView"] = "grid";
        s.Window.Navigate(() => new GamePage("lethal-company", "catalog"));
        s.Pump(9000);
        Scroll(s, 0.15);
        s.Pump(9000);
        s.Save("v23-c1-catalog");
        Scroll(s, 0.4);
        s.Pump(9000);
        s.Save("v23-c2-catalog");
    }

    /// <summary>Страница для снимка: показывает готовый элемент (оверлей рисуется отдельным окном, его так не снять).</summary>
    sealed class Stage(Func<Control> make) : Page
    {
        public override string Title => "Overlay";
        public override void Build() => Content = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A3140")), Child = make() };
    }

    [DemoShots]
    static void V23Overlay(Shots s)
    {
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == "lethal-company") ?? AppState.Games.First();
        Control Card(Control body) => new Border
        {
            Width = 540, Margin = new Thickness(28), Padding = new Thickness(24), CornerRadius = new CornerRadius(20),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E6171A21")), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Child = new ScrollViewer { Content = body },
        };
        Control Studio() => Card(OverlayStudio.Panel(g, () => { }, () => s.Window.CurrentPage?.Build()));
        s.Window.Navigate(() => new Stage(Studio));
        s.Pump(1500);
        s.Save("v23-o1-overlay-studio");
    }
}

/// <summary>Снимок 23.3 (MODLAUNCH_SHOTS=V23Rail): девять игр на компьютере — все на боковой панели.</summary>
public static class V23RailShots
{
    [DemoShots]
    static void V23Rail(Shots s)
    {
        var fake = AppState.Games.Where(g => g.Status != Detect.Found).Take(9).ToList();
        var saved = fake.Select(g => (Game: g, g.Status, g.Path)).ToList();
        try
        {
            foreach (var g in fake) { g.Status = Detect.Found; g.Path ??= System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rail-" + g.Def.Id); }
            s.Window.RefreshRail();
            s.Pump(1500);
            s.Save("v23-rail-many-games");
            if (s.Window.RailScroll is { } rail) { rail.Offset = new Avalonia.Vector(0, rail.Extent.Height); s.Pump(600); s.Save("v23-rail-scrolled"); }
        }
        finally
        {
            foreach (var (g, status, path) in saved) { g.Status = status; g.Path = path; }
            s.Window.RefreshRail();
        }
    }
}

/// <summary>Снимок 23.6 (MODLAUNCH_SHOTS=V23DevGames): «Конструктор в играх» — 300+ игр с режимом разработчика.</summary>
public static class V23DevGamesShots
{
    [DemoShots]
    static void V23DevGames(Shots s)
    {
        s.Window.Navigate(() => new CreatorPage("ingame"));
        s.Pump(9000);
        s.Save("v23-devgames");
        V23Shots.Scroll(s, 0.5);
        s.Pump(2000);
        s.Save("v23-devgames-more");
    }
}
