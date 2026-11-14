using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Снимки 29.0 (MODLAUNCH_SHOTS=V29…) на демо-данных.</summary>
public static class V29Shots
{
    /// <summary>MODLAUNCH_SHOTS=V29Price: «Дешевле в …» в желаемом и окно цен по магазинам.</summary>
    [DemoShots]
    static void V29Price(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new WishlistPage());
        s.Pump(2000);
        s.Save("v29-01-wishlist-cheaper");
        var bg3 = Wishlist.Items.FirstOrDefault(i => i.AppId == 1086940);
        PriceUi.Show(1086940, "Baldur's Gate 3", bg3?.Price);
        s.Pump(1200);
        s.Save("v29-02-price-compare");
        s.Window.CloseDialog();
    }

    /// <summary>MODLAUNCH_SHOTS=V29Recommend: «Для вас» — похожие игры с причиной.</summary>
    [DemoShots]
    static void V29Recommend(Shots s)
    {
        s.Pump(4500);
        Games.AllGames.Refresh();
        s.Pump(1500);
        s.Window.Navigate(() => new RecommendPage());
        s.Pump(2500);
        s.Save("v29-03-for-you");
    }
}
