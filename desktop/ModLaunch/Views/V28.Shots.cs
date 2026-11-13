using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Снимки 28.0 (MODLAUNCH_SHOTS=V28…) на демо-данных.</summary>
public static class V28Shots
{
    /// <summary>MODLAUNCH_SHOTS=V28Wish: «Желаемое» со скидками, поиск, окно цены-цели, колокольчик, светлая тема.</summary>
    [DemoShots]
    static void V28Wish(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new WishlistPage());
        s.Pump(2500);
        s.Save("v28-01-wishlist");

        if (s.Window.CurrentPage is WishlistPage page)
        {
            page.Search("hollow");
            s.Pump(2500);
            s.Save("v28-02-wishlist-search");
        }

        s.Window.Navigate(() => new WishlistPage());
        s.Pump(1200);
        s.Window.RefreshBell();
        s.Save("v28-03-wishlist-again");

        var was = Settings.Data.Str("theme") ?? "store";
        Look.SetTheme("light");
        try
        {
            s.Window.Navigate(() => new WishlistPage());
            s.Pump(1500);
            s.Save("v28-04-wishlist-light");
        }
        finally { Look.SetTheme(was); }
    }
}
