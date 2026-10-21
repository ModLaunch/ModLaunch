using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 9.5: одна навигация Creator Hub для рынка (MarketPage) и мастерской (CreatorPage) — раньше это
/// были два разных экрана со своими вкладками. Три раздела, как в магазинах приложений:
/// «Магазин» (витрина, обзор, Мастерская, авторы, очередь), «Моё» (библиотека, желаемое,
/// кошелёк) и «Автору» (проекты, студия продаж, шаблоны, справка). Выбранный раздел — заливкой,
/// его страницы — рядом; справа — баланс и «Продать работу». 12.0: раздел помнит последнюю вкладку,
/// а «Автору» по умолчанию — «Проекты» (они работают и без входа, студия продаж — нет).
/// </summary>
public static class HubNav
{
    public enum Group { Shop, Mine, Author }

    sealed record Item(string Id, string Key, string Icon, Group Group);

    static readonly Item[] Items =
    [
        new("store", "v93.mk.tab.store", Icons.Home, Group.Shop),
        new("browse", "v93.mk.tab.browse", Icons.Search, Group.Shop),
        new("workshop", "v93.mk.tab.workshop", Icons.Globe, Group.Shop),
        new("authors", "v93.mk.tab.authors", Icons.Users, Group.Shop),
        new("queue", "v93.mk.tab.queue", Icons.Shuffle, Group.Shop),
        new("library", "v93.mk.tab.library", Icons.Layers, Group.Mine),
        new("wishlist", "v93.mk.tab.wishlist", Icons.Heart, Group.Mine),
        new("injects", "v11.hub.injects", Icons.Zap, Group.Mine),
        new("wallet", "v93.mk.tab.wallet", Icons.Bag, Group.Mine),
        new("projects", "v95.hub.projects", Icons.Edit, Group.Author),
        new("studio", "v93.mk.tab.studio", Icons.Chart, Group.Author),
        new("lang", "v11.hub.lang", Icons.Code, Group.Author),
        new("templates", "v91.cr.tab.templates", Icons.Wand, Group.Author),
        new("docs", "cr.tab.docs", Icons.Book, Group.Author),
    ];

    /// <summary>Вкладки, которые живут на странице рынка (остальные — в мастерской).</summary>
    public static bool IsMarketTab(string id) => id is "store" or "browse" or "workshop" or "authors" or "queue" or "library" or "wishlist" or "wallet" or "studio";

    static Group GroupOf(string id) => Items.FirstOrDefault(i => i.Id == id)?.Group ?? Group.Shop;

    /// <summary>Все вкладки (для палитры команд): id, ключ названия, значок.</summary>
    public static IEnumerable<(string Id, string Key, string Icon)> Tabs => Items.Select(i => (i.Id, i.Key, i.Icon));

    /// <summary>Страница Creator Hub (рынок или мастерская): между ними — без въезда всей страницы.</summary>
    public static bool IsHub(Page? page) => page is MarketPage or CreatorPage;

    /// <summary>12.0: последняя открытая вкладка каждого раздела (на сеанс).</summary>
    static readonly Dictionary<Group, string> Last = [];

    /// <summary>Куда ведёт кнопка раздела: туда, где ушли, иначе на первую вкладку («Автору» — «Проекты», не стена входа студии).</summary>
    public static string Target(Group g) => Last.TryGetValue(g, out var id) ? id : Items.First(i => i.Group == g).Id;

    /// <summary>Запомнить вкладку её разделу; неизвестные id не трогают память.</summary>
    public static void Remember(string id)
    {
        if (Items.FirstOrDefault(i => i.Id == id) is { } item) Last[item.Group] = id;
    }

    /// <summary>Открыть раздел: вкладку рынка — на той же странице (inPage), остальное — в мастерской.</summary>
    public static void Open(string id, Action<string>? inPage = null)
    {
        if (IsMarketTab(id))
        {
            if (inPage is not null) inPage(id);
            else MainWindow.Current?.Navigate(() => new MarketPage(id));
            return;
        }
        MainWindow.Current?.Navigate(() => id switch
        {
            "templates" => new CreatorPage("examples"),
            "docs" => new CreatorPage("docs"),
            "injects" => new CreatorPage("injects"),
            "lang" => new CreatorPage("inject"),
            _ => new CreatorPage("home"),
        });
    }

    /// <summary>
    /// Шапка Creator Hub. 10.0 — тише: одна строка (знак, название, три раздела, баланс и кнопки)
    /// и под ней одна линия вкладок без значков. Вкладки внутри страницы (sub, например у «Проектов»)
    /// встают в ту же линию после черты, а не третьим рядом; action — главная кнопка страницы.
    /// </summary>
    public static Control Header(string current, Action<string>? inPage = null, Action? sold = null, IReadOnlyList<Control>? sub = null, Control? action = null)
    {
        Remember(current);
        var title = new TextBlock { Text = "Creator Hub", FontFamily = Gx.Display, FontSize = 20, FontWeight = FontWeight.Bold, LetterSpacing = -0.4, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(title, I18n.T("v93.mk.sub", ("n", 100 - Market.FeePercent)));
        var left = Ui.Row(12, CreatorLogo.Tile(36), title, Groups(current, inPage));
        left.Children[2].Margin = new Thickness(14, 0, 0, 0);

        var right = Ui.Row(8);
        right.VerticalAlignment = VerticalAlignment.Center;
        if (Social.Account.SignedIn && MarketData.Wallet is { } w)
        {
            var wallet = new Button
            {
                Classes = { "chip" }, Padding = new Thickness(12, 7),
                Content = Ui.Row(8, Ui.Icon(Icons.Bag, 15, Ui.Res("Muted")), new TextBlock { Text = Market.Money(w.Balance), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }),
            };
            if (current == "wallet") wallet.Classes.Add("active");
            ToolTip.SetTip(wallet, I18n.T("v93.mk.wallet"));
            wallet.Click += (_, _) => Open("wallet", inPage);
            right.Children.Add(wallet);
        }
        // Главная кнопка одна: на странице со своим действием «Продать работу» уходит во вторые.
        right.Children.Add(Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, sold ?? MarketData.Reload), action is null ? "primary" : "", Icons.Plus));
        if (action is not null) right.Children.Add(action);
        var head = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(left);
        return Ui.Col(10, head, Bar(current, inPage, sub));
    }

    /// <summary>Три раздела — маленьким переключателем рядом с названием.</summary>
    static Control Groups(string current, Action<string>? inPage)
    {
        var group = GroupOf(current);
        var groups = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (g, key) in new[] { (Group.Shop, "v95.hub.group.shop"), (Group.Mine, "v95.hub.group.mine"), (Group.Author, "v95.hub.group.author") })
        {
            var target = Target(g);
            var b = Ui.Button(I18n.T(key), () => { if (g != group) Open(target, inPage); }, "hub-group");
            b.Padding = new Thickness(12, 6);
            if (g == group) b.Classes.Add("active");
            groups.Children.Add(b);
        }
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(9), Padding = new Thickness(3), Child = groups, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Линия вкладок выбранного раздела (и вкладки страницы после черты).</summary>
    public static Control Bar(string current, Action<string>? inPage = null, IReadOnlyList<Control>? sub = null)
    {
        var group = GroupOf(current);
        var tabs = new List<Control>();
        foreach (var item in Items.Where(i => i.Group == group))
        {
            var label = I18n.T(item.Key);
            if (item.Id == "wishlist" && MarketLocal.WishCount > 0) label += "  " + MarketLocal.WishCount;
            var id = item.Id;
            var b = Ui.Button(label, () => Open(id, inPage), "tab");
            if (current == item.Id) b.Classes.Add("active");
            tabs.Add(b);
        }
        if (sub is { Count: > 0 })
        {
            tabs.Add(new Border { Width = 1, Height = 18, Background = Ui.Res("Line"), Margin = new Thickness(4, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center });
            tabs.AddRange(sub);
        }
        return Ui.TabBar(tabs.ToArray());
    }

    [SelfTest]
    static string HubNavRemembersTabs()
    {
        var saved = new Dictionary<Group, string>(Last);
        try
        {
            Last.Clear();
            if (Target(Group.Author) != "projects" || Target(Group.Shop) != "store" || Target(Group.Mine) != "library")
                throw new Exception($"defaults {Target(Group.Shop)}/{Target(Group.Mine)}/{Target(Group.Author)}");
            Remember("templates");
            Remember("wishlist");
            Remember("no-such-tab");
            if (Target(Group.Author) != "templates" || Target(Group.Mine) != "wishlist" || Target(Group.Shop) != "store") throw new Exception("not remembered");
            Remember("docs");
            if (Target(Group.Author) != "docs") throw new Exception("last tab must win");
            // Вкладки мастерской попадают в свой раздел шапки (старые id тоже).
            foreach (var (tab, hub) in new[] { ("home", "projects"), ("mine", "projects"), ("codes", "projects"), ("examples", "templates"), ("inject", "lang"), ("mxdev", "lang"), ("hub", "workshop") })
                if (CreatorPage.HubTabOf(tab) != hub) throw new Exception($"{tab} → {CreatorPage.HubTabOf(tab)}");
            // Поиск: со своим списком — фильтр на месте, с Обзора, справки и из редактора — к проектам.
            foreach (var (tab, editing, q, to) in new[] { ("home", false, "x", "mine"), ("home", false, "", "home"), ("mine", true, "x", "mine"), ("docs", false, "x", "mine"),
                ("inject", false, "x", "mine"), ("codes", false, "x", "codes"), ("examples", false, "x", "examples"), ("hub", false, "x", "hub") })
                if (CreatorPage.SearchTab(tab, editing, q) != to) throw new Exception($"search on {tab} → {CreatorPage.SearchTab(tab, editing, q)}");
            return "Author opens Projects; each group remembers its last tab";
        }
        finally
        {
            Last.Clear();
            foreach (var (k, v) in saved) Last[k] = v;
        }
    }
}
