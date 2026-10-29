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
/// 20.0: слева два пункта — «ModScript X» (внутри «Создать» и «Мои проекты») и «Рынок»; «Команда» — тихой строкой
/// внизу. Рынок только для покупок: кошелёк, доход (студия продавца) и комиссия — в «Аккаунте», не здесь.
/// </summary>
public static class HubNav
{
    /// <summary>Пункт боковой панели: id, ключ названия, значок.</summary>
    sealed record Rail(string Id, string Key, string Icon);

    /// <summary>20.0: что внутри «ModScript X».</summary>
    static readonly Rail[] Main =
    [
        new("create", "v18.hub.create", Icons.Plus),
        new("available", "v20.hub.projects", Icons.Grid),
    ];

    /// <summary>Рынок — второй главный пункт; «Команда» — тихой строкой внизу.</summary>
    static readonly Rail MarketRail = new("shop", "v20.hub.market", Icons.Bag);

    static readonly Rail[] Sections = [new("team", "v13.hub.team", Icons.Users)];

    /// <summary>Вкладки магазина на полосе; остальные — в «Ещё».</summary>
    static readonly (string Id, string Key)[] ShopTabs =
    [
        ("store", "v93.mk.tab.store"), ("browse", "v93.mk.tab.browse"), ("library", "v93.mk.tab.library"), ("wishlist", "v93.mk.tab.wishlist"),
    ];

    /// <summary>
    /// «Ещё» магазина: редкие страницы рынка. 18.0: «Мои инжекты» переехали в «Скрипты», полки «Коды» и «Модели»
    /// повторяли каталог магазина — убраны. 20.0: кошелёк — в «Аккаунте».
    /// </summary>
    static readonly (string Id, string Key, string Icon)[] ShopMore =
    [
        ("workshop", "v93.mk.tab.workshop", Icons.Globe), ("authors", "v93.mk.tab.authors", Icons.Users), ("queue", "v93.mk.tab.queue", Icons.Shuffle),
    ];

    /// <summary>Вкладки, которые живут на странице рынка (остальные — в мастерской).</summary>
    public static bool IsMarketTab(string id) => id is "store" or "browse" or "workshop" or "authors" or "queue" or "library" or "wishlist";

    /// <summary>20.0: деньги автора — в «Аккаунте»: старые вкладки рынка (кошелёк, студия, комиссия) ведут туда.</summary>
    internal static string? AccountTab(string id) => id switch
    {
        "wallet" => "wallet",
        "studio" or "earnings" => "earnings",
        "commission" => "commission",
        _ => null,
    };

    /// <summary>Все разделы и вкладки (для палитры команд): id, ключ названия, значок.</summary>
    public static IEnumerable<(string Id, string Key, string Icon)> Tabs =>
        Main.Append(MarketRail).Concat(Sections).Select(s => (s.Id, s.Key, s.Icon)).Concat(ShopTabs.Select(t => (t.Id, t.Key, Icons.Bag))).Concat(ShopMore);

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

    /// <summary>Вкладка мастерской для раздела или старого id: «Мои проекты» — home, «Создать» — анкета (form), ModScript X — x.</summary>
    internal static string CreatorTab(string id) => id switch
    {
        "projects" or "available" or "lang" or "x" => "home",
        "modscript" or "console" or "commands" or "functions" or "docs" => "x",
        "create" => "form",
        "templates" => "examples",
        _ => id,
    };

    /// <summary>Открыть вкладку или раздел: вкладку рынка — на той же странице (inPage), остальное — в мастерской.</summary>
    public static void Open(string id, Action<string>? inPage = null)
    {
        if (id == "shop") id = ShopTarget;
        if (AccountTab(id) is { } account)
        {
            MainWindow.Current?.Navigate(() => new AccountPage(account));
            return;
        }
        if (IsMarketTab(id))
        {
            if (inPage is not null) inPage(id);
            else MainWindow.Current?.Navigate(() => new MarketPage(id));
            return;
        }
        MainWindow.Current?.Navigate(() => new CreatorPage(CreatorTab(id)));
    }

    /// <summary>
    /// Какой пункт горит: по вкладке мастерской (старые id тоже) или «Магазин» для рынка. 20.0: консоль и справка —
    /// в «ModScript X», защищённые скрипты — вкладка «Моих проектов»; старая среда (inject, mxdev) — тоже в проектах.
    /// </summary>
    public static string RailOf(string tab, bool creator) => !creator ? "shop" : tab switch
    {
        "form" or "create" or "examples" => "create",
        "x" or "modscript" or "console" or "commands" or "functions" or "docs" => "create",
        "team" => "team",
        "codes" or "models" or "hub" => "shop",
        _ => "available",
    };


    /// <summary>
    /// Весь экран Creator Hub. 2.0: без второй боковой панели (пункты Hub — вложенным списком на главной панели окна,
    /// см. <see cref="SideItems"/>), без сетки чертежа и подписей «// РАЗДЕЛ»: шапка — название страницы (34), строка
    /// пояснения, справа не больше одного действия, под ней — вкладки; ниже — содержимое. Поля 34 / 55 (шкала φ).
    /// current — вкладка (вкладки магазина запоминаются), rail — горящий пункт, local — переходы внутри страницы,
    /// action — действие раздела (null — по разделу: в магазине «Продать работу»), sub — вкладки страницы.
    /// </summary>
    public static Control Frame(string current, string rail, string title, Control body, Action<string>? inPage = null, Action? sold = null,
        Func<string, bool>? local = null, IReadOnlyList<Control>? sub = null, Control? action = null, string? lead = null, Control? top = null)
    {
        Remember(current);
        var column = new StackPanel { Spacing = Phi.S21, Margin = Phi.Page, MaxWidth = Ui.PageMaxWidth };
        if (top is not null) column.Children.Add(top);
        column.Children.Add(Head(current, rail, title, lead, inPage, sold, sub, action));
        column.Children.Add(body);
        var scroll = new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var root = new Panel { Classes = { "hub-space" }, Children = { scroll } };
        HubLook.Scope(root);
        // Пункты Hub на боковой панели окна горят по открытой вкладке.
        MainWindow.Current?.RefreshRail();
        return root;
    }

    /// <summary>Пункт Hub на боковой панели окна: id, подпись, значок, выбран ли, число справа, уровень вложенности, заголовок группы.</summary>
    public sealed record SideItem(string Id, string Label, string Icon, bool On, string? Count, int Level, bool Group = false);

    /// <summary>
    /// 2.0: пункты Creator Hub под «Creator Hub» на боковой панели окна — как вложенные пункты команды в Linear:
    /// «ModScript X» (заголовок группы, ведёт в «Мои проекты») с «Создать» и «Мои проекты», затем «Рынок» и «Команда».
    /// </summary>
    public static IEnumerable<SideItem> SideItems(Page? page)
    {
        var rail = page switch
        {
            CreatorPage cp => RailOf(cp.Tab, creator: true),
            MarketPage or ListingPage or AuthorPage => "shop",
            XStudio => "available",
            _ => "",
        };
        // Число проектов — из списков на диске (имена папок), без чтения кода.
        var count = XProjects.List().Count + Projects.List().Count;
        yield return new("x", "ModScript X", Icons.Code, false, null, 0, Group: true);
        yield return new("create", I18n.T("v18.hub.create"), Icons.Plus, rail == "create", null, 1);
        yield return new("available", I18n.T("v20.hub.projects"), Icons.Grid, rail == "available", count > 0 ? count.ToString() : null, 1);
        yield return new("shop", I18n.T(MarketRail.Key), MarketRail.Icon, rail == "shop", null, 0);
        var team = Social.Teams.Mine.FirstOrDefault();
        yield return new("team", team?.Name ?? I18n.T("v13.hub.team"), Icons.Users, rail == "team", null, 0);
    }

    /// <summary>Пункт Hub с боковой панели: на той же странице, если она это умеет, иначе — новой страницей.</summary>
    public static void SideGo(string id)
    {
        if (id == "x") id = "available";
        var page = MainWindow.Current?.CurrentPage;
        if (page is CreatorPage cp && cp.OpenRail(id)) { MainWindow.Current?.RefreshRail(); return; }
        if (id == "shop")
        {
            if (page is MarketPage mp) mp.Go(ShopTarget);
            else Open(ShopTarget);
            return;
        }
        MainWindow.Current?.Navigate(() => new CreatorPage(CreatorTab(id)));
    }

    /// <summary>Шапка раздела: название (34), строка пояснения, справа — одно действие; под ней — вкладки тонкой линией.</summary>
    static Control Head(string current, string rail, string title, string? lead, Action<string>? inPage, Action? sold, IReadOnlyList<Control>? sub, Control? action)
    {
        var words = Ui.Col(Phi.S8, new TextBlock { Text = title, FontSize = Phi.H1, FontWeight = FontWeight.Bold, LetterSpacing = -1, TextTrimming = TextTrimming.CharacterEllipsis });
        if (lead is not null) words.Children.Add(new TextBlock { Text = lead, FontSize = Phi.Body, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left });
        words.VerticalAlignment = VerticalAlignment.Bottom;

        // 18.0: «Новый проект» в шапке повторял пункт «Создать» слева — убран; у магазина — «Продать работу».
        action ??= rail switch
        {
            "shop" => Ui.Button(I18n.T("mk.sell"), () => ListingWizard.Show(null, sold ?? MarketData.Reload), "soft", Icons.Upload),
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
        var col = Ui.Col(Phi.S21, head);
        if (Bar(current, rail, inPage, sub) is { } bar) col.Children.Add(bar);
        return col;
    }

    /// <summary>Вкладки раздела тонкой линией с фирменным подчёркиванием: у магазина — четыре и «Ещё», у остальных — вкладки страницы.</summary>
    public static Control? Bar(string current, string rail, Action<string>? inPage = null, IReadOnlyList<Control>? sub = null)
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Phi.S21 + 3 };
        if (rail == "shop")
        {
            foreach (var (id, key) in ShopTabs)
            {
                var tab = id;
                var b = Ui.Button(I18n.T(key), () => Open(tab, inPage), "hub-tab");
                if (current == id) b.Classes.Add("active");
                tabs.Children.Add(b);
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
            Remember("authors");
            Remember("scripts");
            Remember("no-such-tab");
            if (ShopTarget != "authors") throw new Exception("shop tab not remembered");
            // Старые и новые вкладки мастерской горят в своём пункте; старая среда ModScript X — в «Доступных»; рынок — всегда «Магазин».
            foreach (var (tab, creator, rail) in new[] { ("home", true, "available"), ("mine", true, "available"), ("form", true, "create"), ("examples", true, "create"),
                ("x", true, "create"), ("inject", true, "available"), ("mxdev", true, "available"), ("modscript", true, "create"), ("docs", true, "create"),
                ("console", true, "create"), ("injects", true, "available"), ("scripts", true, "available"), ("team", true, "team"), ("codes", true, "shop"), ("hub", true, "shop"),
                ("browse", false, "shop"), ("library", false, "shop") })
                if (RailOf(tab, creator) != rail) throw new Exception($"rail {tab} → {RailOf(tab, creator)}");
            foreach (var (id, tab) in new[] { ("available", "home"), ("projects", "home"), ("create", "form"), ("templates", "examples"), ("scripts", "scripts"), ("lang", "home"), ("x", "home"), ("console", "x") })
                if (CreatorTab(id) != tab) throw new Exception($"open {id} → {CreatorTab(id)}");
            // Поиск: со своим списком — фильтр на месте, с разделов без списка — к проектам.
            foreach (var (tab, editing, q, to) in new[] { ("home", false, "x", "home"), ("home", false, "", "home"), ("mine", true, "x", "home"), ("scripts", false, "x", "scripts"),
                ("team", false, "x", "home"), ("x", false, "x", "x"), ("codes", false, "x", "codes"), ("examples", false, "x", "examples"), ("hub", false, "x", "hub") })
                if (CreatorPage.SearchTab(tab, editing, q) != to) throw new Exception($"search on {tab} → {CreatorPage.SearchTab(tab, editing, q)}");
            // 20.0: «ModScript X» (create, available) и «Рынок»; деньги — не на рынке, а в аккаунте.
            if (string.Join(",", Main.Select(m => m.Id)) != "create,available" || MarketRail.Id != "shop") throw new Exception("Creator Hub: ModScript X (create, available) + market");
            if (ShopTabs.Any(t => AccountTab(t.Id) is not null) || ShopMore.Any(t => AccountTab(t.Id) is not null)) throw new Exception("money tabs are back on the market");
            foreach (var (id, to) in new[] { ("wallet", "wallet"), ("studio", "earnings"), ("commission", "commission") })
                if (AccountTab(id) != to || IsMarketTab(id)) throw new Exception($"{id} → account {AccountTab(id)}");
            if (ShopMore.Any(m => m.Id is "injects" or "codes" or "models")) throw new Exception("old duplicate shop pages are back");
            return "ModScript X (create, available) and the market; money tabs open the account; old tab ids land in their place";
        }
        finally { _lastShop = saved; }
    }
}
