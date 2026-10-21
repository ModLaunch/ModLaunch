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
/// 12.1: вместо двух рядов переключателей — боковая панель инструментов внутри Hub (Frame).
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

    /// <summary>Пункт боковой панели Hub: id, ключ названия, значок.</summary>
    sealed record Rail(string Id, string Key, string Icon);

    /// <summary>12.1: «Автору» — работа над своими модами; ниже черты — Hub: магазин, своё и списки сообщества.</summary>
    static readonly Rail[] MakeRail =
    [
        new("home", "v12d.rail.overview", Icons.Grid), new("mine", "v12d.rail.projects", Icons.Folder), new("studio", "v93.mk.tab.studio", Icons.Chart),
        new("lang", "v11.hub.lang", Icons.Code), new("templates", "v91.cr.tab.templates", Icons.Wand), new("docs", "cr.tab.docs", Icons.Book),
    ];

    static readonly Rail[] HubRail =
    [
        new("shop", "v95.hub.group.shop", Icons.Bag), new("my", "v95.hub.group.mine", Icons.Layers),
        new("codes", "v91.cr.tab.codes", Icons.List), new("models", "v91.cr.tab.models", Icons.Cube),
    ];

    /// <summary>Какой пункт боковой панели горит: у мастерской — по её вкладке, у рынка — студия или раздел.</summary>
    public static string RailOf(string tab, bool creator) => creator
        ? tab switch
        {
            "home" or "mine" or "codes" or "models" or "docs" => tab,
            "inject" or "mxdev" => "lang",
            "examples" => "templates",
            "injects" => "my",
            "hub" => "shop",
            _ => "mine",
        }
        : tab == "studio" ? "studio" : GroupOf(tab) == Group.Mine ? "my" : "shop";

    /// <summary>Перейти по пункту панели; local — страница может открыть его у себя, без новой страницы.</summary>
    static void Go(string rail, Action<string>? inPage, Func<string, bool>? local)
    {
        if (local?.Invoke(rail) == true) return;
        switch (rail)
        {
            case "studio": Open("studio", inPage); return;
            case "shop": Open(Target(Group.Shop), inPage); return;
            case "my": Open(Target(Group.Mine), inPage); return;
        }
        var tab = rail switch { "lang" => "inject", "templates" => "examples", _ => rail };
        MainWindow.Current?.Navigate(() => new CreatorPage(tab));
    }

    /// <summary>
    /// 12.1: весь экран Creator Hub — своё пространство: слева боковая панель инструментов (не прокручивается),
    /// справа шапка (раздел, цифры автора, «Новый проект») и тело страницы на сетке чертежа.
    /// current — вкладка HubNav (её раздел запоминается), rail — горящий пункт панели, local — переходы внутри страницы.
    /// </summary>
    public static Control Frame(string current, string rail, string title, Control body, Action<string>? inPage = null, Action? sold = null,
        Func<string, bool>? local = null, IReadOnlyList<Control>? sub = null)
    {
        Remember(current);
        var column = new StackPanel { Spacing = 20, Margin = new Thickness(32, 22, 32, 40), MaxWidth = 1640 };
        column.Children.Add(Head(current, title, inPage, sold, sub));
        column.Children.Add(body);
        var scroll = new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(RailPanel(rail, inPage, local));
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        var root = new Panel { Classes = { "hub-space" }, Children = { new HubLook.Blueprint(), grid } };
        HubLook.Scope(root);
        return root;
    }

    /// <summary>Боковая панель: знак и название, «Автору», черта, Hub; внизу — баланс и «Продать работу».</summary>
    static Control RailPanel(string current, Action<string>? inPage, Func<string, bool>? local)
    {
        var brand = Ui.Row(10, CreatorLogo.Tile(30), Ui.Col(1,
            new TextBlock { Text = "Creator Hub", FontSize = 15, FontWeight = FontWeight.Bold, LetterSpacing = -0.2 },
            HubLook.MonoText(I18n.T("v12d.rail.studio"), 10)));
        ToolTip.SetTip(brand, I18n.T("v93.mk.sub", ("n", 100 - Market.FeePercent)));
        brand.Margin = new Thickness(6, 2, 0, 18);

        var items = new StackPanel { Spacing = 2 };
        void Section(string key, Rail[] list)
        {
            var label = HubLook.Eyebrow(I18n.T(key));
            label.Margin = new Thickness(10, items.Children.Count == 0 ? 0 : 6, 0, 6);
            items.Children.Add(label);
            foreach (var r in list)
            {
                var id = r.Id;
                var on = id == current;
                var bar = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Background = on ? HubLook.Signature() : Brushes.Transparent, Margin = new Thickness(0, 0, 9, 0) };
                var text = new TextBlock { Text = I18n.T(r.Key), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var row = new DockPanel { Children = { bar, Ui.Row(10, Ui.Icon(r.Icon, 16, on ? HubLook.Violet : null), text) } };
                DockPanel.SetDock(bar, Dock.Left);
                var b = new Button { Classes = { "hub-rail" }, Content = row };
                if (on) b.Classes.Add("active");
                if (id == "my" && MarketLocal.WishCount > 0) ToolTip.SetTip(b, I18n.T("v93.mk.tab.wishlist") + ": " + MarketLocal.WishCount);
                b.Click += (_, _) => Go(id, inPage, local);
                items.Children.Add(b);
            }
        }
        Section("v95.hub.group.author", MakeRail);
        items.Children.Add(new Border { Height = 1, Background = HubLook.Edge, Margin = new Thickness(10, 12, 10, 8) });
        Section("v12d.rail.hub", HubRail);

        var foot = Ui.Col(8);
        if (Social.Account.SignedIn && MarketData.Wallet is { } w)
        {
            var wallet = new Button
            {
                Classes = { "hub-rail" }, Content = new DockPanel
                {
                    Margin = new Thickness(12, 0, 0, 0),
                    Children = { Ui.Row(10, Ui.Icon(Icons.Bag, 15), new TextBlock { Text = Market.Money(w.Balance), FontFamily = HubLook.Mono, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }) },
                },
            };
            if (current == "my" && Last.GetValueOrDefault(Group.Mine) == "wallet") wallet.Classes.Add("active");
            ToolTip.SetTip(wallet, I18n.T("v93.mk.wallet"));
            wallet.Click += (_, _) => Open("wallet", inPage);
            foot.Children.Add(wallet);
        }
        var dock = new DockPanel { Margin = new Thickness(12, 18, 12, 14) };
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(brand);
        dock.Children.Add(foot);
        dock.Children.Add(new ScrollViewer { Content = items, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        return new Border
        {
            Width = 216, Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, 0, 1, 0), Child = dock,
        };
    }

    /// <summary>Шапка раздела: «// CREATOR HUB · РАЗДЕЛ», название, цифры автора и главные кнопки; под ней — вкладки раздела.</summary>
    static Control Head(string current, string title, Action<string>? inPage, Action? sold, IReadOnlyList<Control>? sub)
    {
        var group = GroupOf(current);
        var groupKey = group switch { Group.Shop => "v95.hub.group.shop", Group.Mine => "v95.hub.group.mine", _ => "v95.hub.group.author" };
        var words = Ui.Col(4,
            HubLook.Eyebrow("Creator Hub · " + I18n.T(groupKey), HubLook.Violet),
            new TextBlock { Text = title, FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, LetterSpacing = -0.6, TextTrimming = TextTrimming.CharacterEllipsis });
        words.VerticalAlignment = VerticalAlignment.Bottom;

        // Цифры автора — тихо, моноширинно: проекты, в Hub, загрузки.
        var (projects, published, downloads) = CreatorPage.AuthorStats();
        var stats = Ui.Row(18);
        stats.VerticalAlignment = VerticalAlignment.Center;
        stats.Margin = new Thickness(0, 0, 10, 0);
        foreach (var (value, key) in new[] { (projects.ToString(), "v99.cr.projects"), (published?.ToString() ?? "—", "v99.cr.published"), (downloads is { } d ? I18n.Compact(d) : "—", "v99.cr.downloads") })
            stats.Children.Add(Ui.Col(0,
                new TextBlock { Text = value, FontFamily = HubLook.Mono, FontSize = 15, FontWeight = FontWeight.SemiBold },
                HubLook.MonoText(I18n.T(key), 10)));
        var sell = Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, sold ?? MarketData.Reload), "", Icons.Upload);
        var create = Ui.Button(I18n.T("v95.hub.newProject"), CreatorPage.CreateNew, "primary", Icons.Plus);
        var right = Ui.Row(8, stats, new Border { Width = 1, Height = 28, Background = HubLook.Edge, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }, sell, create);
        right.VerticalAlignment = VerticalAlignment.Bottom;

        var head = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(words);
        var col = Ui.Col(12, head);
        if (Bar(current, inPage, sub) is { } bar) col.Children.Add(bar);
        else col.Children.Add(new Border { Height = 1, Background = HubLook.Edge });
        return col;
    }

    /// <summary>Вкладки раздела «Магазин» или «Моё» (и вкладки страницы после черты) — тонкой линией с фирменным подчёркиванием.</summary>
    public static Control? Bar(string current, Action<string>? inPage = null, IReadOnlyList<Control>? sub = null)
    {
        var group = GroupOf(current);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22 };
        if (group != Group.Author)
            foreach (var item in Items.Where(i => i.Group == group))
            {
                var label = I18n.T(item.Key);
                if (item.Id == "wishlist" && MarketLocal.WishCount > 0) label += "  " + MarketLocal.WishCount;
                var id = item.Id;
                var b = Ui.Button(label, () => Open(id, inPage), "hub-tab");
                if (current == item.Id) b.Classes.Add("active");
                tabs.Children.Add(b);
            }
        if (sub is { Count: > 0 })
        {
            if (tabs.Children.Count > 0) tabs.Children.Add(new Border { Width = 1, Height = 16, Background = HubLook.Edge, VerticalAlignment = VerticalAlignment.Center });
            foreach (var c in sub) tabs.Children.Add(c);
        }
        if (tabs.Children.Count == 0) return null;
        return new Border
        {
            BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
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
            // 12.1: боковая панель горит на своём пункте и у мастерской, и у рынка.
            foreach (var (tab, creator, rail) in new[] { ("home", true, "home"), ("inject", true, "lang"), ("mxdev", true, "lang"), ("examples", true, "templates"), ("injects", true, "my"),
                ("hub", true, "shop"), ("studio", false, "studio"), ("wallet", false, "my"), ("workshop", false, "shop") })
                if (RailOf(tab, creator) != rail) throw new Exception($"rail {tab} → {RailOf(tab, creator)}");
            return "Author opens Projects; each group remembers its last tab";
        }
        finally
        {
            Last.Clear();
            foreach (var (k, v) in saved) Last[k] = v;
        }
    }
}
