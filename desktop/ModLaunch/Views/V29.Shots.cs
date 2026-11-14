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

    /// <summary>MODLAUNCH_SHOTS=V29Packs: готовые сборки по играм и окно «Поставить и играть».</summary>
    [DemoShots]
    static void V29Packs(Shots s)
    {
        s.Pump(4500);
        s.Window.Navigate(() => new ModpacksPage());
        s.Pump(3000);
        s.Save("v29-04-modpacks");
        if (AppState.Games.FirstOrDefault(g => g.Def.Id == "lethal-company") is { } lc)
        {
            var pack = new Sources.ModInfo { Source = "thunderstore", Id = "Demo-NightShift", Name = "Night Shift Pack", Author = "Lena", Downloads = 412_000,
                Dependencies = ["BepInEx-BepInExPack-5.4.2100", "notnotnotswipez-MoreCompany-1.11.0", "x753-More_Suits-1.4.3", "Sligili-More_Emotes-1.3.3"] };
            ModpacksPage.Ask(lc, pack, 3);
            s.Pump(800);
            s.Save("v29-05-modpack-ask");
            s.Window.CloseDialog();
        }
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
