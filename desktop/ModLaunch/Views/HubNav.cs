using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>
/// 13.0: Creator Hub заново, по плану, а не кнопкой на каждую мысль. 18.0: главный элемент слева — «Мои проекты»
/// с двумя подразделами: «Создать» (анкета и среда ModScript X) и «Доступные» (свои проекты и приобретённые
/// решения; при наведении на плитку — «Запуск» и «Настройка»). Среда ModScript X открывается только отсюда.
/// Ниже — «Скрипты» (купленные и импортированные скрипты под защитой, консоль и справка ModScript),
/// «Команда» и «Магазин». В шапке — название раздела и не больше одной главной кнопки.
/// </summary>
public static class HubNav
{
    /// <summary>Пункт боковой панели: id, ключ названия, значок.</summary>
    sealed record Rail(string Id, string Key, string Icon);

    /// <summary>Подразделы «Моих проектов».</summary>
    static readonly Rail[] ProjectRails =
    [
        new("create", "v18.hub.create", Icons.Plus),
        new("available", "v18.hub.available", Icons.Grid),
    ];

    /// <summary>Остальные разделы.</summary>
    static readonly Rail[] Sections =
    [
        new("scripts", "v18.hub.scripts", Icons.Shield),
        new("team", "v13.hub.team", Icons.Users),
        new("shop", "v13.hub.shop", Icons.Bag),
    ];

    /// <summary>Вкладки магазина на полосе; остальные — в «Ещё».</summary>
    static readonly (string Id, string Key)[] ShopTabs =
    [
        ("store", "v93.mk.tab.store"), ("browse", "v93.mk.tab.browse"), ("library", "v93.mk.tab.library"), ("studio", "v93.mk.tab.studio"),
    ];

    /// <summary>
    /// «Ещё» магазина: редкие страницы рынка. 18.0: «Мои инжекты» переехали в «Скрипты», полки «Коды» и «Модели»
    /// повторяли каталог магазина — убраны.
    /// </summary>
    static readonly (string Id, string Key, string Icon)[] ShopMore =
    [
        ("workshop", "v93.mk.tab.workshop", Icons.Globe), ("authors", "v93.mk.tab.authors", Icons.Users), ("queue", "v93.mk.tab.queue", Icons.Shuffle),
        ("wishlist", "v93.mk.tab.wishlist", Icons.Heart), ("wallet", "v93.mk.tab.wallet", Icons.Bag),
    ];

    /// <summary>Вкладки, которые живут на странице рынка (остальные — в мастерской).</summary>
    public static bool IsMarketTab(string id) => id is "store" or "browse" or "workshop" or "authors" or "queue" or "library" or "wishlist" or "wallet" or "studio" or "commission";

    /// <summary>Все разделы и вкладки (для палитры команд): id, ключ названия, значок.</summary>
    public static IEnumerable<(string Id, string Key, string Icon)> Tabs =>
        ProjectRails.Concat(Sections).Select(s => (s.Id, s.Key, s.Icon)).Concat(ShopTabs.Select(t => (t.Id, t.Key, Icons.Bag))).Concat(ShopMore);

    /// <summary>Страница Creator Hub (рынок, мастерская или среда ModScript X): между ними — без въезда всей страницы.</summary>
    public static bool IsHub(Page? page) => page is MarketPage or CreatorPage or XStudio;

    /// <summary>Последняя вкладка магазина (на сеанс): «Магазин» слева ведёт туда, где ушли.</summary>
    static string? _lastShop;

    public static string ShopTarget => _lastShop ?? "store";

    /// <summary>Запомнить вкладку магазина; вкладки других разделов память не трогают.</summary>
    public static void Remember(string id)
    {
        if (IsMarketTab(id)) _lastShop = id;
    }

    /// <summary>Вкладка мастерской для раздела или старого id: «Доступные» — home, «Создать» — анкета (form).</summary>
    internal static string CreatorTab(string id) => id switch
    {
        "projects" or "available" or "lang" => "home",
        "create" => "form",
        "templates" => "examples",
        _ => id,
    };

    /// <summary>Открыть вкладку или раздел: вкладку рынка — на той же странице (inPage), остальное — в мастерской.</summary>
    public static void Open(string id, Action<string>? inPage = null)
    {
        if (id == "shop") id = ShopTarget;
        if (IsMarketTab(id))
        {
            if (inPage is not null) inPage(id);
            else MainWindow.Current?.Navigate(() => new MarketPage(id));
            return;
        }
        MainWindow.Current?.Navigate(() => new CreatorPage(CreatorTab(id)));
    }

    /// <summary>
    /// Какой пункт горит: по вкладке мастерской (старые id тоже) или «Магазин» для рынка. Старая среда (x, inject, mxdev)
    /// ведёт в «Доступные»: ModScript X теперь только внутри «Моих проектов».
    /// </summary>
    public static string RailOf(string tab, bool creator) => !creator ? "shop" : tab switch
    {
        "form" or "create" or "examples" => "create",
        "scripts" or "modscript" or "console" or "commands" or "functions" or "docs" or "injects" or "library" => "scripts",
        "team" => "team",
        "codes" or "models" or "hub" => "shop",
        _ => "available",
    };

    /// <summary>Горящий пункт — подраздел «Моих проектов»?</summary>
    static bool InProjects(string rail) => ProjectRails.Any(p => p.Id == rail);

    /// <summary>
    /// Весь экран Creator Hub — своё пространство: слева «Мои проекты» и разделы (не прокручиваются), справа шапка и тело на сетке чертежа.
    /// current — вкладка (вкладки магазина запоминаются), rail — горящий пункт, local — переходы внутри страницы,
    /// action — главная кнопка раздела (null — по разделу: в магазине «Продать работу», в остальных нет), sub — вкладки страницы.
    /// </summary>
    public static Control Frame(string current, string rail, string title, Control body, Action<string>? inPage = null, Action? sold = null,
        Func<string, bool>? local = null, IReadOnlyList<Control>? sub = null, Control? action = null, string? lead = null, Control? top = null)
    {
        Remember(current);
        var column = new StackPanel { Spacing = 20, Margin = new Thickness(32, 22, 32, 40), MaxWidth = 1640 };
        if (top is not null) column.Children.Add(top);
        column.Children.Add(Head(current, rail, title, lead, inPage, sold, sub, action));
        column.Children.Add(body);
        var scroll = new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        // 20.0: разделы Creator Hub — вкладками раздела «Создателям» под шапкой окна (одна навигация на всю программу),
        // поэтому своей боковой панели у мастерской больше нет: странице достаётся вся ширина.
        var root = new Panel { Classes = { "hub-space" }, Children = { new HubLook.Blueprint(), scroll } };
        HubLook.Scope(root);
        return root;
    }

    /// <summary>Перейти в раздел; local — страница может открыть его у себя, без новой страницы.</summary>
    static void Go(string rail, Action<string>? inPage, Func<string, bool>? local)
    {
        if (local?.Invoke(rail) == true) return;
        if (rail == "shop") { Open(ShopTarget, inPage); return; }
        MainWindow.Current?.Navigate(() => new CreatorPage(CreatorTab(rail)));
    }

    /// <summary>Строка боковой панели: полоска слева, значок, название и (если есть) число справа моноширинно.</summary>
    static Button RailButton(Rail r, bool on, double indent, Action click, string? count = null)
    {
        var bar = new Border { Width = 3, Height = 20, CornerRadius = new CornerRadius(2), Background = on ? HubLook.Signature() : Brushes.Transparent, Margin = new Thickness(0, 0, 10 + indent, 0) };
        var text = new TextBlock { Text = I18n.T(r.Key), FontSize = 14, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new DockPanel { Children = { bar } };
        DockPanel.SetDock(bar, Dock.Left);
        if (count is not null)
        {
            var n = HubLook.MonoText(count, 10.5);
            DockPanel.SetDock(n, Dock.Right);
            row.Children.Add(n);
        }
        row.Children.Add(Ui.Row(12, Ui.Icon(r.Icon, indent > 0 ? 16 : 18, on ? HubLook.Violet : null), text));
        var b = new Button { Classes = { "hub-rail" }, Content = row, MinHeight = indent > 0 ? 36 : 42 };
        if (on) b.Classes.Add("active");
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>
    /// «Мои проекты» — главный блок панели: знак ModScript X, название и два подраздела под ним.
    /// Сам заголовок ведёт в «Доступные».
    /// </summary>
    static Control ProjectsGroup(string current, Action<string>? inPage, Func<string, bool>? local)
    {
        var lit = InProjects(current);
        var mark = CreatorPage.XMark(28);
        var words = Ui.Col(0,
            new TextBlock { Text = I18n.T("v18.hub.projects"), FontSize = 15, FontWeight = FontWeight.Bold, LetterSpacing = -0.2 },
            HubLook.MonoText("MODSCRIPT X", 9.5, HubLook.Cyan));
        var head = new Button { Classes = { "hub-rail" }, Content = Ui.Row(10, mark, words), MinHeight = 48, Padding = new Thickness(8, 0, 10, 0) };
        head.Click += (_, _) => Go("available", inPage, local);
        ToolTip.SetTip(head, I18n.T("v18.hub.projects.tip"));
        var items = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 2) };
        foreach (var r in ProjectRails)
        {
            var id = r.Id;
            items.Children.Add(RailButton(r, id == current, 14, () => Go(id, inPage, local)));
        }
        return new Border
        {
            Background = lit ? HubLook.Raised : Brushes.Transparent, BorderBrush = lit ? HubLook.EdgeHi : HubLook.Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 14),
            Child = Ui.Col(0, head, new Border { Height = 1, Background = HubLook.Edge, Margin = new Thickness(8, 2, 8, 0) }, items),
        };
    }

    /// <summary>Боковая панель: знак и название, «Мои проекты» с подразделами, остальные разделы; внизу — команда и баланс, если они есть.</summary>
    static Control RailPanel(string current, Action<string>? inPage, Func<string, bool>? local)
    {
        var brand = Ui.Row(10, CreatorLogo.Tile(30), Ui.Col(1,
            new TextBlock { Text = "Creator Hub", FontSize = 15, FontWeight = FontWeight.Bold, LetterSpacing = -0.2 },
            HubLook.MonoText(I18n.T("v12d.rail.studio"), 10)));
        ToolTip.SetTip(brand, I18n.T("v93.mk.sub", ("n", 100 - Market.FeePercent)));
        brand.Margin = new Thickness(6, 2, 0, 22);

        var items = new StackPanel { Spacing = 4 };
        items.Children.Add(ProjectsGroup(current, inPage, local));
        foreach (var r in Sections)
        {
            var id = r.Id;
            // Число защищённых скриптов — из библиотеки (один небольшой JSON), без чтения самих файлов.
            var count = id == "scripts" && Features.Inject.All().Count(i => i.Sealed) is > 0 and var n ? n.ToString() : null;
            items.Children.Add(RailButton(r, id == current, 0, () => Go(id, inPage, local), count));
        }

        var foot = Ui.Col(8);
        // Своя команда — тихой строкой внизу: тег и название (клик — в раздел «Команда»).
        if (Social.Teams.Mine.FirstOrDefault() is { } team)
        {
            var chip = new Border { Background = TeamLook.Brush(team.Color), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1), Child = HubLook.MonoText(team.Tag, 10, Brushes.White) };
            var t = new Button { Classes = { "hub-rail" }, Content = Ui.Row(8, chip, new TextBlock { Text = team.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }) };
            t.Click += (_, _) => Go("team", inPage, local);
            foot.Children.Add(t);
        }
        if (Social.Account.SignedIn && MarketData.Wallet is { } w)
        {
            var wallet = new Button
            {
                Classes = { "hub-rail" }, Content = Ui.Row(10, Ui.Icon(Icons.Bag, 15), new TextBlock { Text = Market.Money(w.Balance), FontFamily = HubLook.Mono, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }),
            };
            ToolTip.SetTip(wallet, I18n.T("v93.mk.wallet"));
            wallet.Click += (_, _) => Open("wallet", inPage);
            foot.Children.Add(wallet);
        }
        var dock = new DockPanel { Margin = new Thickness(12, 18, 12, 14) };
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(brand);
        dock.Children.Add(foot);
        dock.Children.Add(items);
        return new Border
        {
            Width = 216, Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, 0, 1, 0), Child = dock,
        };
    }

    /// <summary>Шапка раздела: «// CREATOR HUB · РАЗДЕЛ», название (и строка пояснения), справа — одна главная кнопка; под ней — вкладки.</summary>
    static Control Head(string current, string rail, string title, string? lead, Action<string>? inPage, Action? sold, IReadOnlyList<Control>? sub, Control? action)
    {
        var eyebrow = InProjects(rail) ? I18n.T("v18.hub.projects") : I18n.T(Sections.First(s => s.Id == rail).Key);
        var words = Ui.Col(4,
            HubLook.Eyebrow("Creator Hub · " + eyebrow, HubLook.Violet),
            new TextBlock { Text = title, FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, LetterSpacing = -0.6, TextTrimming = TextTrimming.CharacterEllipsis });
        if (lead is not null) words.Children.Add(new TextBlock { Text = lead, FontSize = 13, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left });
        words.VerticalAlignment = VerticalAlignment.Bottom;

        // 18.0: «Новый проект» в шапке повторял пункт «Создать» слева — убран; главная кнопка осталась у магазина
        // и у тех страниц, что передают свою (импорт в «Скриптах», команда).
        action ??= rail switch
        {
            "shop" => Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, sold ?? MarketData.Reload), "primary", Icons.Upload),
            _ => null,
        };
        var head = new DockPanel();
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(action, Dock.Right);
            head.Children.Add(action);
        }
        head.Children.Add(words);
        var col = Ui.Col(12, head);
        if (Bar(current, rail, inPage, sub) is { } bar) col.Children.Add(bar);
        else col.Children.Add(new Border { Height = 1, Background = HubLook.Edge });
        return col;
    }

    /// <summary>Вкладки раздела тонкой линией с фирменным подчёркиванием: у магазина — четыре и «Ещё», у остальных — вкладки страницы.</summary>
    public static Control? Bar(string current, string rail, Action<string>? inPage = null, IReadOnlyList<Control>? sub = null)
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22 };
        if (rail == "shop")
        {
            foreach (var (id, key) in ShopTabs)
            {
                var tab = id;
                var b = Ui.Button(I18n.T(key), () => Open(tab, inPage), "hub-tab");
                if (current == id) b.Classes.Add("active");
                tabs.Children.Add(b);
            }
            // 14.0: комиссия рынка — вкладка владельца, остальные её не видят.
            if (Social.Account.Owner)
            {
                var fee = Ui.Button(I18n.T("v14.mk.tab.commission"), () => Open("commission", inPage), "hub-tab");
                if (current == "commission") fee.Classes.Add("active");
                tabs.Children.Add(fee);
            }
            var inMore = ShopMore.FirstOrDefault(m => m.Id == current);
            var label = inMore.Id is null ? I18n.T("v13.hub.more") : I18n.T(inMore.Key);
            if (current == "wishlist" && MarketLocal.WishCount > 0) label += "  " + MarketLocal.WishCount;
            var more = Ui.Button(label + "  ▾", () => { }, "hub-tab");
            if (inMore.Id is not null) more.Classes.Add("active");
            var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            foreach (var (id, key, icon) in ShopMore)
            {
                var tab = id;
                var text = I18n.T(key);
                if (id == "wishlist" && MarketLocal.WishCount > 0) text += "  " + MarketLocal.WishCount;
                var item = new MenuItem { Header = text, Icon = Ui.Icon(icon, 14) };
                item.Click += (_, _) => Open(tab, inPage);
                menu.Items.Add(item);
            }
            more.Flyout = menu;
            tabs.Children.Add(more);
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
    static string HubNavProjectsFirst()
    {
        var saved = _lastShop;
        try
        {
            _lastShop = null;
            if (ShopTarget != "store") throw new Exception("shop default " + ShopTarget);
            Remember("wishlist");
            Remember("scripts");
            Remember("no-such-tab");
            if (ShopTarget != "wishlist") throw new Exception("shop tab not remembered");
            // Старые и новые вкладки мастерской горят в своём пункте; старая среда ModScript X — в «Доступных»; рынок — всегда «Магазин».
            foreach (var (tab, creator, rail) in new[] { ("home", true, "available"), ("mine", true, "available"), ("form", true, "create"), ("examples", true, "create"),
                ("x", true, "available"), ("inject", true, "available"), ("mxdev", true, "available"), ("modscript", true, "scripts"), ("docs", true, "scripts"),
                ("console", true, "scripts"), ("injects", true, "scripts"), ("team", true, "team"), ("codes", true, "shop"), ("hub", true, "shop"),
                ("studio", false, "shop"), ("wallet", false, "shop") })
                if (RailOf(tab, creator) != rail) throw new Exception($"rail {tab} → {RailOf(tab, creator)}");
            foreach (var (id, tab) in new[] { ("available", "home"), ("projects", "home"), ("create", "form"), ("templates", "examples"), ("scripts", "scripts"), ("lang", "home") })
                if (CreatorTab(id) != tab) throw new Exception($"open {id} → {CreatorTab(id)}");
            // Поиск: со своим списком — фильтр на месте, с разделов без списка — к проектам.
            foreach (var (tab, editing, q, to) in new[] { ("home", false, "x", "home"), ("home", false, "", "home"), ("mine", true, "x", "home"), ("scripts", false, "x", "scripts"),
                ("team", false, "x", "home"), ("codes", false, "x", "codes"), ("examples", false, "x", "examples"), ("hub", false, "x", "hub") })
                if (CreatorPage.SearchTab(tab, editing, q) != to) throw new Exception($"search on {tab} → {CreatorPage.SearchTab(tab, editing, q)}");
            if (ProjectRails.Length != 2 || Sections.Length != 3) throw new Exception("Creator Hub: «Мои проекты» (create, available) + scripts, team, shop");
            if (ShopMore.Any(m => m.Id is "injects" or "codes" or "models")) throw new Exception("old duplicate shop pages are back");
            return "«Мои проекты» first (create, available), then scripts, team, shop; old tab ids land in their place";
        }
        finally { _lastShop = saved; }
    }
}
