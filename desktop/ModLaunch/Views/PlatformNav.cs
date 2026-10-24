using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 20.0 «Платформа»: четыре раздела в шапке, как в Steam (Магазин · Библиотека · Сообщество · Создателям).
/// У каждого раздела — свой ряд вкладок под шапкой; любая страница программы принадлежит ровно одному разделу,
/// поэтому шапка всегда показывает, где вы, а «назад» и переходы между разделами работают одинаково везде.
/// </summary>
public enum Section { None, Store, Library, Community, Creator }

/// <summary>Вкладка раздела: ключ названия, значок, какую страницу открыть и какие страницы ею подсвечиваются.</summary>
public sealed record SubTab(string Id, string Key, string Icon, Func<Page> Open, Func<Page, bool> Lit);

public static class PlatformNav
{
    /// <summary>Разделы в шапке — в порядке Steam.</summary>
    public static readonly Section[] Main = [Section.Store, Section.Library, Section.Community, Section.Creator];

    public static string Key(Section s) => s switch
    {
        Section.Store => "v20.nav.store",
        Section.Library => "v20.nav.library",
        Section.Community => "v20.nav.community",
        Section.Creator => "v20.nav.creator",
        _ => "",
    };

    public static string Icon(Section s) => s switch
    {
        Section.Store => Icons.Bag,
        Section.Library => Icons.Gamepad,
        Section.Community => Icons.Users,
        Section.Creator => Icons.Creator,
        _ => Icons.Settings,
    };

    /// <summary>Вкладки рынка, которые относятся к продавцу (раздел «Создателям»), остальные — покупателю («Магазин»).</summary>
    static bool SellerTab(string tab) => tab is "studio" or "commission";

    /// <summary>
    /// Раздел страницы. null — страница не меняет раздел: мод, лот и автор открываются там, откуда пришли
    /// (мод из витрины магазина остаётся в магазине, мод из каталога игры — в библиотеке).
    /// </summary>
    public static Section? Of(Page? page) => page switch
    {
        null => Section.None,
        ModPage or ListingPage or AuthorPage => null,
        StorePage or SearchPage or AiBuildsPage or SteamGamesPage or ModrinthPage => Section.Store,
        MarketPage m => SellerTab(m.Tab) ? Section.Creator : Section.Store,
        HomePage or LibraryPage or GamePage or MinecraftPage or AddGamePage or ModsCenterPage or SteamLibraryPage or AutopilotPage => Section.Library,
        CommunityPage or FriendsPage or ProfilePage or AccountPage => Section.Community,
        // Старые вкладки мастерской с полками рынка (коды, модели, Hub) — это магазин.
        CreatorPage c when HubNav.RailOf(c.Tab, true) == "shop" => Section.Store,
        CreatorPage or XStudio => Section.Creator,
        _ => Section.None,
    };

    /// <summary>Куда ведёт раздел, если в нём ещё не были за этот сеанс.</summary>
    public static Func<Page> Home(Section s) => s switch
    {
        Section.Store => () => new StorePage(),
        Section.Community => () => new CommunityPage(),
        Section.Creator => () => new CreatorPage(),
        _ => () => new HomePage(),
    };

    static string CreatorRail(Page p) => p switch
    {
        CreatorPage c => HubNav.RailOf(c.Tab, true),
        XStudio => "available",
        _ => "",
    };

    /// <summary>Вкладки раздела под шапкой.</summary>
    public static IReadOnlyList<SubTab> Tabs(Section s) => s switch
    {
        Section.Store =>
        [
            new("front", "v20.store.front", Icons.Sparkles, () => new StorePage(), p => p is StorePage),
            new("search", "v20.store.search", Icons.Search, () => new SearchPage(), p => p is SearchPage),
            new("ai", "v98.nav.ai", Icons.Wand, () => new AiBuildsPage(), p => p is AiBuildsPage),
            new("market", "v20.store.market", Icons.Bag, () => new MarketPage("store"), p => p is MarketPage m && m.Tab != "workshop" && !SellerTab(m.Tab) || p is CreatorPage { Tab: "codes" or "models" }),
            new("hub", "v20.store.hub", Icons.Globe, () => new MarketPage("workshop"), p => p is MarketPage { Tab: "workshop" } or CreatorPage { Tab: "hub" }),
            new("steam", "v20.store.steam", Icons.Shelf, () => new SteamGamesPage(), p => p is SteamGamesPage),
        ],
        Section.Library =>
        [
            new("home", "v20.lib.home", Icons.Home, () => new HomePage(), p => p is HomePage),
            new("all", "v20.lib.all", Icons.Grid, () => new LibraryPage(), p => p is LibraryPage),
            new("mods", "v20.lib.mods", Icons.Package, () => new ModsCenterPage(), p => p is ModsCenterPage),
            new("steam", "v20.lib.steam", Icons.Shelf, () => new SteamLibraryPage(), p => p is SteamLibraryPage),
            new("autopilot", "v20.lib.autopilot", Icons.Activity, () => new AutopilotPage(), p => p is AutopilotPage),
            new("add", "v20.lib.add", Icons.Plus, () => new AddGamePage(), p => p is AddGamePage),
        ],
        Section.Community =>
        [
            new("friends", "v20.cm.friends", Icons.Users, () => new CommunityPage("friends"), p => p is CommunityPage { Tab: "friends" } or FriendsPage),
            new("servers", "v20.cm.servers", Icons.Server, () => new CommunityPage("servers"), p => p is CommunityPage { Tab: "servers" }),
            new("host", "v20.cm.host", Icons.Cube, () => new CommunityPage("host"), p => p is CommunityPage { Tab: "host" }),
            new("profile", "v20.cm.profile", Icons.Trophy, () => new ProfilePage(), p => p is ProfilePage or AccountPage),
        ],
        Section.Creator =>
        [
            new("projects", "v18.hub.projects", Icons.Grid, () => new CreatorPage(), p => CreatorRail(p) == "available"),
            new("create", "v18.hub.create", Icons.Plus, () => new CreatorPage("form"), p => CreatorRail(p) == "create"),
            new("scripts", "v18.hub.scripts", Icons.Shield, () => new CreatorPage("scripts"), p => CreatorRail(p) == "scripts"),
            new("team", "v13.hub.team", Icons.Users, () => new CreatorPage("team"), p => CreatorRail(p) == "team"),
            new("sales", "v20.cr.sales", Icons.Chart, () => new MarketPage("studio"), p => p is MarketPage m && SellerTab(m.Tab)),
        ],
        _ => [],
    };

    [SelfTest]
    static string PlatformSectionsCoverPages()
    {
        // У каждого раздела есть вкладки, у каждой вкладки — название. Тексты читаются из ресурсов Avalonia:
        // в --selfcheck (без интерфейса) их нет — там проверяем только сами вкладки.
        var texts = Avalonia.Application.Current is not null;
        foreach (var s in Main)
        {
            var tabs = Tabs(s);
            if (tabs.Count == 0) throw new Exception($"{s}: no tabs");
            if (tabs.Select(t => t.Id).Distinct().Count() != tabs.Count) throw new Exception($"{s}: duplicate tab ids");
            foreach (var t in tabs)
                if (texts && I18n.T(t.Key) == t.Key) throw new Exception($"{s}/{t.Id}: no text for {t.Key}");
        }
        if (Of(null) != Section.None) throw new Exception("no page — no section");
        foreach (var (tab, want) in new[] { ("store", Section.Store), ("workshop", Section.Store), ("wishlist", Section.Store), ("studio", Section.Creator), ("commission", Section.Creator) })
            if (SellerTab(tab) != (want == Section.Creator)) throw new Exception($"market tab {tab} → wrong section");
        if (Main.Distinct().Count() != 4 || Main.Any(s => Key(s) == "")) throw new Exception("four sections with names");
        return "four sections, every tab has a name; market splits into buyer (Store) and seller (Creators) tabs";
    }
}
