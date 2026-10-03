using ModLaunch.Creator;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    [DemoShots]
    static void MarketShots(Shots s)
    {
        Listing L(string id, string title, string kind, long price, long sales, string preview = "") => new(
            id, "demo", "Mira", title, "Готово к использованию: подключение в два шага.", "", kind, "subnautica", price, "personal", "1.2.0",
            preview, [], [], 120_000, "", 1, "item.zip", "active", sales, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddHours(-sales));
        _market =
        [
            L("a", "Low-poly Seamoth", "model", 34900, 12),
            L("b", "Inventory sort (C#)", "code", 9900, 41, "public static void Sort(Inventory inv)\n{\n    inv.Items.Sort((a, b) => a.Name.CompareTo(b.Name));\n}"),
            L("c", "Day/Night tweaks", "script", 0, 230),
            L("d", "Alien UI icons", "asset", 14900, 7),
            L("e", "Survival pack", "pack", 49900, 3),
        ];
        s.Window.Navigate(() => new CreatorPage("market"));
        s.Pump(900);
        s.Save("market-1-store");
        MarketViews.Open(_market[1]);
        s.Pump(700);
        s.Save("market-2-listing");
        s.Window.CloseDialog();
        MarketViews.Editor(null);
        s.Pump(700);
        s.Save("market-3-editor");
        s.Window.CloseDialog();
        s.Window.Navigate(() => new CreatorPage("studio"));
        s.Pump(600);
        s.Save("market-4-studio");
        s.Window.Navigate(() => new AccountPage());
        s.Pump(600);
        s.Save("market-5-account");
    }
}
