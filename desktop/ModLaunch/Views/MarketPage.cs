using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 23.0: рынок Creator Hub — одна понятная страница вместо витрины из всего подряд. Сверху — что ищем
/// (вид товара, игра, только бесплатное, порядок), ниже — «Выбор недели» и одна сетка одинаковых карточек.
/// Три вкладки: «Рынок», «Моды сообщества» (бесплатная Мастерская) и «Мои покупки» (купленное и желаемое).
/// Старые вкладки (обзор, очередь открытий, авторы, желаемое) ведут в свои новые места.
/// </summary>
public sealed partial class MarketPage : Page
{
    public static readonly string[] Tabs = ["store", "workshop", "library"];

    /// <summary>Старые id вкладок рынка → новые: обзор, очередь и авторы — это «Рынок», желаемое — в «Моих покупках».</summary>
    public static string Normalize(string tab) => tab switch
    {
        "workshop" => "workshop",
        "library" or "wishlist" => "library",
        _ => "store",
    };

    const int PageSize = 24;

    string _tab;
    string _query = "";
    string _kind = "all";
    string? _game;
    bool _freeOnly;
    string _sort = "popular";
    int _shown = PageSize;
    // Мои покупки: «bought» или «wish».
    string _lib;
    // Моды сообщества.
    string _wsSort = "trending";
    string? _wsGame;

    public MarketPage(string tab = "store", string query = "")
    {
        _lib = tab == "wishlist" ? "wish" : "bought";
        _tab = Normalize(tab);
        _query = query;
        if (_query != "" && _tab != "workshop") _tab = "store";
        MarketLocal.Changed += OnLocal;
        MarketData.Changed += OnData;
        DetachedFromVisualTree += (_, _) => { MarketLocal.Changed -= OnLocal; MarketData.Changed -= OnData; };
        AttachedToVisualTree += (_, _) => { MarketLocal.Changed -= OnLocal; MarketData.Changed -= OnData; MarketLocal.Changed += OnLocal; MarketData.Changed += OnData; };
    }

    /// <summary>21.0: рынок с частями кода — кусочки для ModScript.</summary>
    public static MarketPage CodePieces() => new("store") { _kind = "code" };

    void OnLocal() { if (_tab == "library" && _lib == "wish") Build(); }
    void OnData() { if (MainWindow.Current?.CurrentPage == this) Build(); }

    public override string Title => "Creator Hub";
    public override string SearchHint => I18n.T("v93.mk.search");
    public override string? Accent => null;

    public override IEnumerable<(string Text, Action? Open)> Crumbs =>
        _tab == "store" ? [("Creator Hub", null)] : [("Creator Hub", () => MainWindow.Current?.Navigate(() => new MarketPage())), (I18n.T("v23.mk.tab." + _tab), null)];

    public override void Search(string text)
    {
        _query = text.Trim();
        if (_tab != "workshop") _tab = "store";
        _shown = PageSize;
        Build();
        MainWindow.Current?.RenderCrumbs();
    }

    public override bool SameScreenAs(Page? previous) => HubNav.IsHub(previous);

    void Go(string tab)
    {
        if (tab == "wishlist") _lib = "wish";
        _tab = Normalize(tab);
        Build();
        MainWindow.Current?.RenderCrumbs();
    }

    public override void Build()
    {
        if (MarketData.Listings is null && !MarketData.Loading) _ = MarketData.Load();
        var loading = MarketData.Listings is null;
        Control body = _tab switch
        {
            "workshop" => loading ? Skeleton() : WorkshopView(),
            "library" => LibraryView(),
            _ => loading ? Skeleton() : Store(),
        };
        Content = HubNav.Frame(_tab, HubNav.RailOf(_tab, creator: false), I18n.T("v23.mk.tab." + _tab), body, Go, MarketData.Reload);
    }

    static Control Skeleton()
    {
        var col = Ui.Col(StoreKit.Gap);
        col.Children.Add(StoreKit.Placeholder(double.NaN, 44));
        var grid = new UniformGrid { Columns = 3 };
        for (var i = 0; i < 6; i++)
        {
            var p = StoreKit.Placeholder(double.NaN, 200);
            p.Margin = new Thickness(0, 0, StoreKit.Gap, StoreKit.Gap);
            grid.Children.Add(p);
        }
        col.Children.Add(grid);
        return col;
    }

    IEnumerable<Listing> Active => MarketData.All.Where(l => l.Active);

    static List<string> MyGames() => AppState.Games.Where(g => g.Status == Detect.Found).Select(g => g.Def.Id).ToList();

    /// <summary>Кнопка-переключатель: «pivot» — вид товара, «chip» — игра.</summary>
    static Button Toggle(object content, bool on, Action click, string kind = "pivot")
    {
        var b = new Button { Content = content, Classes = { kind }, VerticalAlignment = VerticalAlignment.Center };
        if (on) b.Classes.Add("active");
        b.Click += (_, _) => click();
        return b;
    }

    // ---------------------------------------------------------------- рынок

    bool Filtered => _kind != "all" || _game is not null || _freeOnly || _query != "";

    bool Match(Listing l) =>
        (_query == "" || l.Title.Contains(_query, StringComparison.OrdinalIgnoreCase) || l.Author.Contains(_query, StringComparison.OrdinalIgnoreCase)
            || l.Summary.Contains(_query, StringComparison.OrdinalIgnoreCase) || l.Tags.Any(t => I18n.T("hub.tag." + t).Contains(_query, StringComparison.OrdinalIgnoreCase)))
        && (_kind == "all" || l.Kind == _kind)
        && (_game is null || l.Game == _game)
        && (!_freeOnly || l.Free);

    Control Store()
    {
        var all = Active.ToList();
        var col = Ui.Col(24);
        if (all.Count == 0)
        {
            // Сервер рынка недоступен или лотов ещё нет — объясняем, что делать.
            col.Children.Add(MarketData.Error is { } error ? Offline(error) : Empty());
            return col;
        }
        col.Children.Add(Filters(all));
        var found = Market.Sort(all.Where(Match), _sort).ToList();
        if (!Filtered)
        {
            var pick = MarketLocal.ForYou(all, MyGames()).FirstOrDefault();
            if (pick is not null)
            {
                col.Children.Add(Pick(pick));
                found.Remove(pick);
            }
        }
        // Над сеткой: сколько нашлось, «только бесплатное» и порядок.
        var count = Ui.Text(_query != "" ? I18n.T("v93.mk.found.q", ("n", found.Count), ("q", _query)) : I18n.T("v93.mk.found", ("n", found.Count)), "h3");
        count.VerticalAlignment = VerticalAlignment.Center;
        var free = new CheckBox { Content = I18n.T("v23.mk.freeOnly"), IsChecked = _freeOnly, VerticalAlignment = VerticalAlignment.Center };
        free.IsCheckedChanged += (_, _) => { _freeOnly = free.IsChecked == true; _shown = PageSize; Build(); };
        var sorts = new[] { "popular", "new", "cheap", "expensive" };
        var sort = new ComboBox { Width = 180, ItemsSource = sorts.Select(s => I18n.T("mk.sort." + s)).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(sorts, _sort)), VerticalAlignment = VerticalAlignment.Center };
        sort.SelectionChanged += (_, _) => { var v = sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _sort) { _sort = v; Build(); } };
        var tools = Ui.Row(16, free, sort);
        var head = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        head.Children.Add(count);
        if (found.Count == 0)
        {
            col.Children.Add(head);
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("hub.nothing"), "h3"), Ui.Text(I18n.T("v93.mk.nothing.text"), "muted", wrap: true),
                Ui.Button(I18n.T("v93.mk.f.reset"), Reset, "", Icons.Undo)), 24));
            return col;
        }
        var results = Ui.Col(14, head, StoreKit.Tiles(found.Take(_shown).Select(l => MarketTiles.Listing(l, 1)), 250));
        if (found.Count > _shown)
        {
            var more = Ui.Button(I18n.T("v23.mk.more", ("n", found.Count - _shown)), () => { _shown += PageSize; Build(); }, "", Icons.ChevronDown);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            results.Children.Add(more);
        }
        col.Children.Add(results);
        return col;
    }

    void Reset()
    {
        _kind = "all"; _game = null; _freeOnly = false; _query = ""; _shown = PageSize;
        Build();
    }

    /// <summary>Что ищем: вид товара и порядок — одной строкой, игры — чипами ниже (свои установленные — первыми).</summary>
    Control Filters(List<Listing> all)
    {
        var kinds = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in Market.Kinds.Prepend("all"))
        {
            var id = k;
            if (k != "all" && all.All(l => l.Kind != k) && _kind != k) continue;
            var b = Toggle(I18n.T("mk.kind." + k), _kind == k, () => { _kind = id; _shown = PageSize; Build(); });
            b.Margin = new Thickness(0, 0, 4, 4);
            kinds.Children.Add(b);
        }
        var bar = kinds;

        var mine = MyGames();
        var games = all.Where(l => l.Game != "").GroupBy(l => l.Game)
            .Select(g => (Id: g.Key, N: g.Count(), Mine: mine.Contains(g.Key)))
            .OrderByDescending(g => g.Mine).ThenByDescending(g => g.N).ToList();
        var chips = new WrapPanel();
        void Add(Control c) { c.Margin = new Thickness(0, 0, 6, 6); chips.Children.Add(c); }
        Add(Toggle(I18n.T("hub.allGames"), _game is null, () => { _game = null; _shown = PageSize; Build(); }, "chip"));
        const int shown = 7;
        foreach (var g in games.Take(shown))
        {
            var id = g.Id;
            Add(Toggle(GameChip(id), _game == id, () => { _game = _game == id ? null : id; _shown = PageSize; Build(); }, "chip"));
        }
        if (games.Count > shown)
        {
            var rest = games.Skip(shown).ToList();
            var other = new ComboBox { Width = 190, PlaceholderText = I18n.T("v23.mk.otherGame"), ItemsSource = rest.Select(g => MarketTiles.GameName(g.Id)).ToList() };
            var at = rest.FindIndex(g => g.Id == _game);
            if (at >= 0) other.SelectedIndex = at;
            other.SelectionChanged += (_, _) => { if (other.SelectedIndex >= 0 && rest[other.SelectedIndex].Id != _game) { _game = rest[other.SelectedIndex].Id; _shown = PageSize; Build(); } };
            Add(other);
        }
        if (games.Count == 0) return bar;
        return Ui.Col(10, bar, chips);
    }

    /// <summary>Чип игры: маленькая обложка и короткое название.</summary>
    static Control GameChip(string id)
    {
        var name = new TextBlock { Text = MarketTiles.GameName(id), VerticalAlignment = VerticalAlignment.Center };
        if (GameCatalog.ById(id) is not { } def) return name;
        var cover = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = Ui.GameImage(def, 64, art: Images.Art.Cover) };
        return Ui.Row(8, cover, name);
    }

    /// <summary>«Выбор недели»: один товар крупно — картинка слева, о нём и покупка справа. Без карусели.</summary>
    Control Pick(Listing l)
    {
        var art = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = MarketTiles.Art(l, 1280) };
        StoreKit.OnClick(art, () => MarketTiles.Open(l));
        var label = new TextBlock { Text = I18n.T("v23.mk.pick"), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = HubLook.Violet };
        var title = new TextBlock { Text = l.Title, FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        var what = new TextBlock { Text = I18n.T("mk.kind." + l.Kind) + ", " + MarketTiles.GameName(l.Game), FontSize = 13, Foreground = Ui.Res("Muted") };
        var summary = new TextBlock { Text = l.Summary, TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14, LineHeight = 21 };
        var author = Ui.Row(10, Ui.Thumb(null, l.Author, 28, 14, person: true), new TextBlock { Text = l.Author, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var authorLink = new Border { Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Child = author, HorizontalAlignment = HorizontalAlignment.Left };
        StoreKit.OnClick(authorLink, () => MainWindow.Current?.Navigate(() => new AuthorPage(l.Uid, l.Author)));
        var owned = l.Mine || Market.Owned.Contains(l.Id);
        var buy = Ui.Button(owned ? I18n.T("v93.mk.open") : l.Free ? I18n.T("mk.getFree") : I18n.T("mk.buy", ("price", Market.Money(l.Price))), () => MarketTiles.Open(l), "primary", owned ? Icons.Eye : l.Free ? Icons.Download : Icons.Bag);
        buy.Padding = new Thickness(18, 10);
        var wish = MarketTiles.WishButton(l, onArt: false);
        wish.Width = wish.Height = 40;
        wish.CornerRadius = new CornerRadius(8);
        var actions = Ui.Row(8, buy, wish);
        var info = new DockPanel { Margin = new Thickness(4, 2, 4, 2) };
        DockPanel.SetDock(actions, Dock.Bottom);
        info.Children.Add(actions);
        info.Children.Add(Ui.Col(10, label, title, what, summary, authorLink));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 24 };
        grid.Children.Add(art);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        return new Border { Classes = { "card" }, Padding = new Thickness(14), Child = StoreKit.Scaled(grid, 300, maxScale: 1.25) };
    }

    static Control Offline(string error)
    {
        // 10.0: как подключить сервер, видит только владелец; остальным — просто «временно недоступен».
        if (!Social.Account.Owner)
            return Ui.Card(Ui.Col(12,
                Gx.Title(I18n.T("v10.mk.down"), 22, Ui.Res("Text")),
                Ui.Text(I18n.T("v10.mk.down.text"), "muted", wrap: true),
                Ui.Row(10, Ui.Button(I18n.T("cr.refresh"), MarketData.Reload, "", Icons.Refresh))), 28);
        var steps = Ui.Col(6,
            Ui.Text(I18n.T("v93.mk.offline.1"), "small", wrap: true),
            Ui.Text(I18n.T("v93.mk.offline.2"), "small", wrap: true),
            Ui.Text(I18n.T("v93.mk.offline.3"), "small", wrap: true));
        var buttons = Ui.Row(10,
            Ui.Button(I18n.T("v93.mk.offline.connect"), () => MainWindow.Current?.Navigate(() => new SettingsPage("services")), "primary", Icons.Zap),
            Ui.Button(I18n.T("cr.refresh"), MarketData.Reload, "", Icons.Refresh));
        return Ui.Card(Ui.Col(12,
            Gx.Title(I18n.T("v93.mk.offline"), 22, Ui.Res("Text")),
            Ui.Text(I18n.T("v93.mk.offline.text"), "muted", wrap: true),
            steps, Ui.Text(error, "small muted", wrap: true), buttons), 26);
    }

    static Control Empty() => Ui.Card(Ui.Col(10,
        Gx.Title(I18n.T("mk.empty"), 24, Ui.Res("Text")),
        Ui.Text(I18n.T("v93.mk.empty.text"), "muted", wrap: true),
        Ui.Row(10, Ui.Button(I18n.T("mk.sell"), () => MarketViews.Editor(null, MarketData.Reload), "primary", Icons.Plus),
            Ui.Button(I18n.T("cr.refresh"), MarketData.Reload, "", Icons.Refresh))), 28);

    public static Button FollowButton(string uid, string name)
    {
        var on = MarketLocal.Follows(uid);
        Button? b = null;
        b = Ui.Button(I18n.T(on ? "v93.mk.following" : "v93.mk.follow"), () =>
        {
            var now = MarketLocal.ToggleFollow(uid, name);
            b!.Content = I18n.T(now ? "v93.mk.following" : "v93.mk.follow");
            b.Classes.Set("primary", !now);
            MainWindow.Current?.Toast(I18n.T(now ? "v93.mk.followed" : "v93.mk.unfollowed", ("name", name)));
        }, on ? "" : "primary", on ? Icons.Check : Icons.Plus);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        return b;
    }

    // ---------------------------------------------------------------- моды сообщества (бесплатная Мастерская)

    Control WorkshopView()
    {
        var all = MarketData.Mods.ToList();
        var col = Ui.Col(18);
        var sorts = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (id, key) in new[] { ("trending", "hub.sort.trending"), ("new", "hub.sort.new"), ("downloads", "hub.sort.downloads"), ("likes", "hub.sort.likes") })
        {
            var s = id;
            var b = Toggle(I18n.T(key), _wsSort == id, () => { _wsSort = s; Build(); });
            b.Margin = new Thickness(0, 0, 4, 4);
            sorts.Children.Add(b);
        }
        var game = new ComboBox { Width = 210, VerticalAlignment = VerticalAlignment.Center };
        var games = new List<string?> { null };
        game.Items.Add(I18n.T("hub.allGames"));
        foreach (var g in all.Select(m => m.Game).Distinct()) { games.Add(g); game.Items.Add(MarketTiles.GameName(g)); }
        game.SelectedIndex = Math.Max(0, games.IndexOf(_wsGame));
        game.SelectionChanged += (_, _) => { var v = games[Math.Max(0, game.SelectedIndex)]; if (v != _wsGame) { _wsGame = v; Build(); } };
        var right = Ui.Row(8, game, Ui.Button(I18n.T("hub.upload"), CreatorPage.PublishFromAnywhere, "", Icons.Upload));
        right.VerticalAlignment = VerticalAlignment.Top;
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(sorts);
        col.Children.Add(Ui.Text(I18n.T("v93.mk.workshop.text"), "muted", wrap: true));
        col.Children.Add(bar);
        var list = Hub.Sort(all.Where(m => (_wsGame is null || m.Game == _wsGame) && (_query == "" || m.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) || m.Author.Contains(_query, StringComparison.OrdinalIgnoreCase))), _wsSort).ToList();
        if (list.Count == 0) col.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("hub.nothing"), "h3"), Ui.Text(I18n.T("hub.nothing.text"), "small muted", wrap: true)), 22));
        else col.Children.Add(StoreKit.Tiles(list.Select(m => MarketTiles.Workshop(m, 1)), 250));
        return col;
    }

    // ---------------------------------------------------------------- мои покупки: купленное и желаемое

    Control LibraryView()
    {
        var wishCount = MarketLocal.WishCount;
        var bought = _purchases?.Count;
        var tabs = Ui.Row(4,
            Toggle(bought is int n ? $"{I18n.T("v23.mk.lib.bought")}  {n}" : I18n.T("v23.mk.lib.bought"), _lib == "bought", () => { _lib = "bought"; Build(); }),
            Toggle(wishCount > 0 ? $"{I18n.T("v93.mk.tab.wishlist")}  {wishCount}" : I18n.T("v93.mk.tab.wishlist"), _lib == "wish", () => { _lib = "wish"; Build(); }));
        return Ui.Col(18, tabs, _lib == "wish" ? Wishlist() : Library());
    }

    string _wishSort = "added";

    Control Wishlist()
    {
        var wish = MarketLocal.Wishlist();
        var byId = MarketData.All.ToDictionary(l => l.Id);
        var items = wish.Where(w => byId.ContainsKey(w.Id)).Select(w => (Item: byId[w.Id], w.At)).ToList();
        items = _wishSort switch
        {
            "price" => items.OrderBy(x => x.Item.Price).ToList(),
            "name" => items.OrderBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => items,
        };
        var col = Ui.Col(14);
        if (items.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v93.mk.wish.empty"), "h3"), Ui.Text(I18n.T("v23.mk.wish.empty.text"), "muted", wrap: true),
                Ui.Button(I18n.T("v23.mk.tab.store"), () => Go("store"), "primary", Icons.Bag)), 24));
            return col;
        }
        var total = items.Where(x => !Market.Owned.Contains(x.Item.Id)).Sum(x => x.Item.Price);
        var sorts = Ui.Row(4);
        foreach (var (id, key) in new[] { ("added", "v93.mk.wish.sort.added"), ("price", "v93.mk.wish.sort.price"), ("name", "v93.mk.wish.sort.name") })
        {
            var s = id;
            sorts.Children.Add(Toggle(I18n.T(key), _wishSort == id, () => { _wishSort = s; Build(); }, "chip"));
        }
        var sum = Ui.Text(I18n.T("v23.mk.wish.total", ("money", Market.Money(total))), "small muted");
        sum.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel();
        DockPanel.SetDock(sum, Dock.Right);
        head.Children.Add(sum);
        head.Children.Add(sorts);
        col.Children.Add(head);
        foreach (var (l, at) in items)
        {
            var item = l;
            var buy = Ui.Button(l.Mine || Market.Owned.Contains(l.Id) ? I18n.T("v93.mk.open") : l.Free ? I18n.T("mk.getFree") : Market.Money(l.Price), () => MarketTiles.Open(item), "primary", l.Free ? Icons.Download : Icons.Bag);
            var remove = Ui.Button("", () => MarketLocal.ToggleWish(item.Id), "icon ghost", Icons.Trash, I18n.T("v93.mk.wish.remove"));
            var right = Ui.Row(8, Ui.Text(I18n.T("v93.mk.wish.added.at", ("when", Ui.Ago(at))), "small muted"), buy, remove);
            right.VerticalAlignment = VerticalAlignment.Center;
            col.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(6), Child = MarketTiles.Row(l, null, right) });
        }
        return col;
    }

    static List<Purchase>? _purchases;
    static string? _purchasesError;
    static bool _purchasesLoading;

    /// <summary>Для снимков экрана: покупки без сети.</summary>
    public static void DemoPurchases(List<Purchase> list) => _purchases = list;

    Control Library()
    {
        var col = Ui.Col(12);
        if (!Social.Account.SignedIn && _purchases is null)
        {
            col.Children.Add(SignIn(I18n.T("v93.mk.library.signin")));
            return col;
        }
        if (_purchases is null && !_purchasesLoading && !Program.Screenshot)
        {
            _purchasesLoading = true;
            _ = Task.Run(async () =>
            {
                try { var p = await Market.Purchases(); _purchases = p; _purchasesError = null; }
                catch (Exception e) { _purchasesError = Market.Explain(e); _purchases = []; }
                _purchasesLoading = false;
                Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
            });
        }
        if (_purchases is null) { col.Children.Add(Skeleton()); return col; }
        if (_purchasesError is not null) col.Children.Add(Ui.Card(Ui.Text(_purchasesError, "muted", wrap: true), 18));
        if (_purchases.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v93.mk.library.empty"), "h3"), Ui.Text(I18n.T("v93.mk.library.empty.text"), "muted", wrap: true),
                Ui.Button(I18n.T("v23.mk.tab.store"), () => Go("store"), "primary", Icons.Bag)), 24));
            return col;
        }
        foreach (var p in _purchases)
        {
            var listing = MarketData.All.FirstOrDefault(l => l.Id == p.ListingId);
            Control art = listing is not null ? MarketTiles.Art(listing, 384, hero: false) : new Border { Background = HubLook.Raised, Child = Ui.Icon(MarketTiles.KindIcon(p.Kind), 18, Ui.Res("Muted")) };
            var download = Ui.Button(I18n.T("mk.download"), async () =>
            {
                var l = listing ?? await Market.Get(p.ListingId);
                if (l is null) { MainWindow.Current?.Toast(I18n.T("v93.mk.library.gone"), bad: true); return; }
                MarketViews.Download(l);
            }, "primary", Icons.Download);
            var open = Ui.Button("", () => { if (listing is not null) MarketTiles.Open(listing); }, "icon ghost", Icons.Eye, I18n.T("v93.mk.open"));
            open.IsEnabled = listing is not null;
            var right = Ui.Row(8, open, download);
            right.VerticalAlignment = VerticalAlignment.Center;
            var price = p.Price == 0 ? I18n.T("mk.free") : Market.Money(p.Price);
            var words = Ui.Col(3, Ui.Text(p.Title, "h3"),
                Ui.Text(I18n.T("v23.mk.bought.line", ("kind", I18n.T("mk.kind." + p.Kind)), ("seller", p.Seller), ("price", price), ("when", Ui.Ago(p.Created))), "small muted"));
            words.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            grid.Children.Add(new Border { Width = 120, Height = 68, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = art });
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            col.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(10), Child = grid });
        }
        return col;
    }

    static Control SignIn(string text) => Ui.Card(Ui.Col(10,
        Ui.Icon(Icons.User, 30, Ui.Res("Brand2")), Ui.Text(I18n.T("st.signin"), "h3"), Ui.Text(text, "muted", wrap: true),
        Ui.Button(I18n.T("acc.menu.signin"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "primary", Icons.User)), 28);
}
