using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    /// <summary>26.0: высота переключателя, списка и поля поиска в строке фильтров — одна на всех, строка ровная.</summary>
    const double ToolHeight = 36;

    string _tab;
    string _query = "";
    string _kind = "all";
    string? _game;
    bool _freeOnly;
    string _sort = "popular";
    int _shown = PageSize;
    // Мои покупки: «bought» или «wish».
    string _lib;
    // Моды сообщества. 26.0: свои поиск, вид, метка и «Показать ещё» — как у рынка.
    string _wsSort = "trending";
    string? _wsGame;
    string _wsKind = "all";
    string? _wsTag;
    string _wsQuery = "";
    int _wsShown = PageSize;

    public MarketPage(string tab = "store", string query = "")
    {
        _lib = tab == "wishlist" ? "wish" : "bought";
        _tab = Normalize(tab);
        if (_tab == "workshop") _wsQuery = query.Trim();
        else _query = query;
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
    // 26.0: рынок — страница хаба: без колонки рекламы справа (на других пунктах хаба её нет, и сетка «прыгала» на столбец).
    public override bool Workspace => true;

    public override IEnumerable<(string Text, Action? Open)> Crumbs =>
        _tab == "store" ? [("Creator Hub", null)] : [("Creator Hub", () => MainWindow.Current?.Navigate(() => new MarketPage())), (I18n.T("v23.mk.tab." + _tab), null)];

    public override void Search(string text)
    {
        // На вкладке модов сообщества строка поиска ищет моды сообщества; с остальных вкладок — товары рынка.
        if (_tab == "workshop")
        {
            _wsQuery = text.Trim();
            _wsShown = PageSize;
        }
        else
        {
            _query = text.Trim();
            _tab = "store";
            _shown = PageSize;
        }
        // Новый поиск — с начала страницы, а не с того места, где остановились.
        _builtTab = null;
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

    // Вкладка, которая сейчас нарисована: перестройка той же вкладки («Показать ещё», фильтр) не прокручивает наверх.
    string? _builtTab;

    public override void Build()
    {
        if (MarketData.Listings is null && !MarketData.Loading) _ = MarketData.Load();
        var loading = MarketData.Listings is null;
        _wsResults = null;
        Control body = _tab switch
        {
            "workshop" => loading ? Skeleton() : WorkshopView(),
            "library" => LibraryView(),
            _ => loading ? Skeleton() : Store(),
        };
        var keep = _builtTab == _tab ? HubScroller(Content as Control)?.Offset : null;
        _builtTab = _tab;
        // 26.0: заголовок — «Рынок» на всех трёх вкладках (какая открыта, видно по подчёркнутой вкладке под ним).
        // У модов сообщества главная кнопка — «Опубликовать мод», а не «Продать работу», и пояснение — под заголовком.
        var workshop = _tab == "workshop";
        Content = HubNav.Frame(_tab, HubNav.RailOf(_tab, creator: false), I18n.T("v20.hub.market"), body, Go, MarketData.Reload,
            action: workshop ? Ui.Button(I18n.T("hub.upload"), CreatorPage.PublishFromAnywhere, "primary", Icons.Upload) : null,
            lead: workshop ? I18n.T("v93.mk.workshop.text") : null);
        if (keep is { Y: > 0 } offset)
            Dispatcher.UIThread.Post(() => { if (HubScroller(Content as Control) is { } sv) sv.Offset = offset; }, DispatcherPriority.Loaded);
    }

    /// <summary>26.0: главная прокрутка страницы хаба (самая длинная) — чтобы перестройка сохраняла место на странице.</summary>
    internal static ScrollViewer? HubScroller(Control? root) =>
        root?.GetVisualDescendants().OfType<ScrollViewer>().OrderByDescending(v => v.Extent.Height).FirstOrDefault();

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
        var found = Market.Sort(all.Where(Match), _sort).ToList();
        col.Children.Add(Filters(all, found.Count));
        if (!Filtered)
        {
            var pick = MarketLocal.ForYou(all, MyGames()).FirstOrDefault();
            if (pick is not null)
            {
                col.Children.Add(Pick(pick));
                found.Remove(pick);
            }
        }
        if (found.Count == 0)
        {
            col.Children.Add(HubLook.Empty(Icons.Search, I18n.T("hub.nothing"), I18n.T("v93.mk.nothing.text"), Ui.Button(I18n.T("v93.mk.f.reset"), Reset, "", Icons.Undo)));
            return col;
        }
        var results = Ui.Col(14, StoreKit.Tiles(found.Take(_shown).Select(l => MarketTiles.Listing(l, 1)), 250));
        if (found.Count > _shown)
            results.Children.Add(More(found.Count - _shown, () => { _shown += PageSize; Build(); }));
        col.Children.Add(results);
        return col;
    }

    /// <summary>«Показать ещё (N)» под сеткой.</summary>
    static Button More(int rest, Action click)
    {
        var more = Ui.Button(I18n.T("v23.mk.more", ("n", rest)), click, "", Icons.ChevronDown);
        more.HorizontalAlignment = HorizontalAlignment.Center;
        return more;
    }

    void Reset()
    {
        _kind = "all"; _game = null; _freeOnly = false; _query = ""; _shown = PageSize;
        Build();
    }

    /// <summary>Сколько нашлось — тихой подписью рядом с переключателем вида (26.0: раньше — отдельной строкой-заголовком над сеткой).</summary>
    static TextBlock FoundText(int n, string query) => new()
    {
        Text = query != "" ? I18n.T("v93.mk.found.q", ("n", n), ("q", query)) : I18n.T("v93.mk.found", ("n", n)),
        FontSize = 12.5, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Строка фильтров: слева — переключатель и «Найдено», справа — инструменты.</summary>
    static DockPanel FilterBar(Control left, Control right)
    {
        right.VerticalAlignment = VerticalAlignment.Center;
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(left);
        return bar;
    }

    /// <summary>
    /// Что ищем. 26.0: два ряда вместо четырёх — вид товара, «Найдено» и справа «Только бесплатное» с порядком одной
    /// строкой, игры — одной строкой чипов ниже.
    /// </summary>
    Control Filters(List<Listing> all, int count)
    {
        var kinds = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in Market.Kinds.Prepend("all"))
        {
            var id = k;
            if (k != "all" && all.All(l => l.Kind != k) && _kind != k) continue;
            var b = Toggle(I18n.T("mk.kind." + k), _kind == k, () => { _kind = id; _shown = PageSize; Build(); });
            b.Margin = new Thickness(0, 0, 4, 0);
            kinds.Children.Add(b);
        }
        kinds.Children.Add(FoundText(count, _query));
        // «Только бесплатное» — чипом-переключателем хаба (раньше — обычная галочка темы).
        var free = new ToggleButton { Classes = { "chip" }, Content = I18n.T("v23.mk.freeOnly"), IsChecked = _freeOnly, Padding = new Thickness(14, 0), Height = ToolHeight, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        free.IsCheckedChanged += (_, _) => { _freeOnly = free.IsChecked == true; _shown = PageSize; Build(); };
        var sorts = new[] { "popular", "new", "cheap", "expensive" };
        var sort = new ComboBox { Width = 170, Height = ToolHeight, ItemsSource = sorts.Select(s => I18n.T("mk.sort." + s)).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(sorts, _sort)), VerticalAlignment = VerticalAlignment.Center };
        sort.SelectionChanged += (_, _) => { var v = sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _sort) { _sort = v; Build(); } };
        var head = FilterBar(kinds, Ui.Row(10, free, sort));
        var games = GameChips(all.Select(l => l.Game), _game, g => { _game = g; _shown = PageSize; Build(); });
        return games is null ? head : Ui.Col(12, head, games);
    }

    /// <summary>
    /// Игры чипами одной строкой: «Все игры», до пяти игр (свои установленные — первыми, дальше — где больше всего)
    /// и «Другая игра» списком. Общие для рынка и модов сообщества.
    /// </summary>
    static Control? GameChips(IEnumerable<string> gameIds, string? current, Action<string?> pick)
    {
        var mine = MyGames();
        var games = gameIds.Where(g => g != "").GroupBy(g => g)
            .Select(g => (Id: g.Key, N: g.Count(), Mine: mine.Contains(g.Key)))
            .OrderByDescending(g => g.Mine).ThenByDescending(g => g.N).ToList();
        if (games.Count == 0) return null;
        var chips = new WrapPanel();
        void Add(Control c) { c.Margin = new Thickness(0, 0, 6, 6); chips.Children.Add(c); }
        Add(Toggle(I18n.T("hub.allGames"), current is null, () => pick(null), "chip"));
        const int shown = 5;
        foreach (var g in games.Take(shown))
        {
            var id = g.Id;
            Add(Toggle(GameChip(id), current == id, () => pick(current == id ? null : id), "chip"));
        }
        if (games.Count > shown)
        {
            var rest = games.Skip(shown).ToList();
            var other = new ComboBox { Width = 180, Height = 32, PlaceholderText = I18n.T("v23.mk.otherGame"), ItemsSource = rest.Select(g => MarketTiles.GameName(g.Id)).ToList(), VerticalAlignment = VerticalAlignment.Center };
            var at = rest.FindIndex(g => g.Id == current);
            if (at >= 0) other.SelectedIndex = at;
            other.SelectionChanged += (_, _) => { if (other.SelectedIndex >= 0 && rest[other.SelectedIndex].Id != current) pick(rest[other.SelectedIndex].Id); };
            Add(other);
        }
        return chips;
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
        // 26.0: высота картинки — от её ширины (невысокая: под ней на экране должен помещаться ряд товаров), а сведения справа стоят по центру. Раньше
        // карточка была фиксированной высоты, кнопки прижимались к низу, и между автором и «Купить» зияла пустота.
        art.SizeChanged += (_, e) =>
        {
            var h = Math.Clamp(Math.Round(e.NewSize.Width * 0.44), 200, 270);
            if (Math.Abs(art.MinHeight - h) > 1) art.MinHeight = h;
        };
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
        actions.Margin = new Thickness(0, 8, 0, 0);
        var info = Ui.Col(10, label, title, what, summary, authorLink, actions);
        info.Margin = new Thickness(4, 6, 4, 6);
        info.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 24 };
        grid.Children.Add(art);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        return new Border { Classes = { "card" }, Padding = new Thickness(14), Child = grid };
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

    // 26.0: пустой рынок — общим пустым состоянием хаба (круг со значком по центру), как «Мои моды» и «Желаемое».
    static Control Empty() => HubLook.Empty(Icons.Bag, I18n.T("mk.empty"), I18n.T("v93.mk.empty.text"),
        Ui.Button(I18n.T("mk.sell"), () => MarketViews.Editor(null, MarketData.Reload), "primary", Icons.Plus),
        Ui.Button(I18n.T("cr.refresh"), MarketData.Reload, "", Icons.Refresh));

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

    // 26.0: поиск на вкладке — живой: при наборе перерисовываются только «Найдено» и сетка, а не вся страница
    // (иначе поле теряло бы фокус на каждой букве).
    ContentControl? _wsResults;
    TextBlock? _wsCount;
    Control? _wsSortBox;
    DispatcherTimer? _wsTyping;
    string _wsPending = "";

    /// <summary>
    /// 26.0: «Моды сообщества» с поиском как у каталогов: обновления отслеживаемых модов, вид (готовые моды или скрипты
    /// ModScript), игры и метки чипами, умный поиск по названию, описанию, меткам и автору и «Показать ещё».
    /// </summary>
    Control WorkshopView()
    {
        var all = MarketData.Mods.ToList();
        var col = Ui.Col(20);
        if (all.Count == 0)
        {
            col.Children.Add(Hub.LastError is { } error
                ? HubLook.Empty(Icons.WifiOff, I18n.T("hub.error"), error, Ui.Button(I18n.T("hub.retry"), MarketData.Reload, "primary", Icons.Refresh))
                : HubLook.Empty(Icons.Globe, I18n.T("hub.empty"), I18n.T("v26.market.ws.empty"), Ui.Button(I18n.T("hub.upload"), CreatorPage.PublishFromAnywhere, "primary", Icons.Upload)));
            return col;
        }
        var updates = TrackedUpdates(all);
        if (updates.Count > 0) col.Children.Add(UpdatesCard(updates));
        col.Children.Add(WorkshopFilters(all));
        _wsResults = new ContentControl();
        col.Children.Add(_wsResults);
        FillWorkshop();
        return col;
    }

    /// <summary>Вид, «Найдено», порядок и поиск — строкой; игры и метки — чипами ниже.</summary>
    Control WorkshopFilters(List<HubMod> all)
    {
        var kinds = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (id, key) in new[] { ("all", "hub.kind.all"), ("package", "v26.market.ws.package"), ("script", "v26.market.ws.script") })
        {
            var k = id;
            if (id != "all" && all.All(m => m.Kind != id) && _wsKind != id) continue;
            var b = Toggle(I18n.T(key), _wsKind == id, () => { _wsKind = k; _wsShown = PageSize; Build(); });
            b.Margin = new Thickness(0, 0, 4, 0);
            kinds.Children.Add(b);
        }
        _wsCount = FoundText(0, _wsQuery);
        kinds.Children.Add(_wsCount);

        var sorts = new[] { "trending", "new", "updated", "downloads", "likes" };
        var sort = new ComboBox { Width = 160, Height = ToolHeight, ItemsSource = sorts.Select(s => I18n.T("hub.sort." + s)).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(sorts, _wsSort)), VerticalAlignment = VerticalAlignment.Center };
        sort.SelectionChanged += (_, _) => { var v = sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _wsSort) { _wsSort = v; _wsShown = PageSize; FillWorkshop(); } };
        _wsSortBox = sort;
        var search = new TextBox
        {
            Classes = { "clearButton" }, Width = 340, Height = ToolHeight, VerticalContentAlignment = VerticalAlignment.Center, Text = _wsQuery, Watermark = I18n.T("v26.market.ws.search"), VerticalAlignment = VerticalAlignment.Center,
            InnerLeftContent = new Border { Padding = new Thickness(10, 0, 0, 0), Child = Ui.Icon(Icons.Search, 14, Ui.Res("Muted")) },
        };
        search.TextChanged += (_, _) => Typed(search.Text ?? "");
        // Порядок — слева от поиска: при поиске он прячется (найденное идёт по совпадению), а поле не сдвигается.
        var col = Ui.Col(12, FilterBar(kinds, Ui.Row(10, sort, search)));
        if (GameChips(all.Select(m => m.Game), _wsGame, g => { _wsGame = g; _wsShown = PageSize; Build(); }) is { } games) col.Children.Add(games);
        if (TagChips(all) is { } tags) col.Children.Add(tags);
        return col;
    }

    const int TagsShown = 9;
    // Все метки раскрыты (иначе — самые частые одной строкой и «+N»).
    bool _wsAllTags;

    /// <summary>
    /// Метки чипами — только те, что есть у загруженных модов (и выбранная), самые частые — первыми. Больше девяти —
    /// одна строка и чип «+N», который раскрывает остальные.
    /// </summary>
    Control? TagChips(List<HubMod> all)
    {
        var present = Hub.Tags.Select(t => (Tag: t, N: all.Count(m => m.Tags.Contains(t)))).Where(x => x.N > 0 || x.Tag == _wsTag)
            .OrderByDescending(x => x.N).Select(x => x.Tag).ToList();
        if (present.Count == 0) return null;
        var folds = present.Count > TagsShown + 1;
        var shown = !folds || _wsAllTags ? present : present.Take(TagsShown).ToList();
        if (_wsTag is not null && !shown.Contains(_wsTag)) shown.Add(_wsTag);
        var wrap = new WrapPanel();
        Button Chip(object content, bool on, Action click)
        {
            var chip = Toggle(content, on, click, "chip");
            chip.Padding = new Thickness(11, 4);
            chip.FontSize = 12;
            chip.Margin = new Thickness(0, 0, 6, 6);
            wrap.Children.Add(chip);
            return chip;
        }
        foreach (var tag in shown)
        {
            var t = tag;
            Chip("#" + I18n.T("hub.tag." + tag), _wsTag == tag, () => { _wsTag = _wsTag == t ? null : t; _wsShown = PageSize; Build(); });
        }
        if (folds)
        {
            var chevron = Ui.Icon(Icons.ChevronDown, 12);
            if (_wsAllTags) chevron.RenderTransform = new RotateTransform(180);
            var more = Chip(_wsAllTags ? chevron : Ui.Row(4, new TextBlock { Text = "+" + (present.Count - shown.Count), VerticalAlignment = VerticalAlignment.Center }, chevron), false,
                () => { _wsAllTags = !_wsAllTags; Build(); });
            ToolTip.SetTip(more, I18n.T(_wsAllTags ? "v26.market.tags.less" : "v26.market.tags.all"));
        }
        return wrap;
    }

    void Typed(string text)
    {
        _wsPending = text.Trim();
        if (_wsTyping is null)
        {
            _wsTyping = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _wsTyping.Tick += (_, _) =>
            {
                _wsTyping!.Stop();
                if (_wsPending == _wsQuery) return;
                _wsQuery = _wsPending;
                _wsShown = PageSize;
                FillWorkshop();
            };
        }
        _wsTyping.Stop();
        _wsTyping.Start();
    }

    /// <summary>Что показать: фильтры, затем поиск (если есть) или выбранный порядок.</summary>
    List<HubMod> WorkshopFound(IEnumerable<HubMod> all)
    {
        var pool = all.Where(m => (_wsGame is null || m.Game == _wsGame) && (_wsTag is null || m.Tags.Contains(_wsTag)) && (_wsKind == "all" || m.Kind == _wsKind));
        return _wsQuery == "" ? Hub.Sort(pool, _wsSort).ToList() : SmartFind(pool, _wsQuery);
    }

    void FillWorkshop()
    {
        if (_wsResults is null) return;
        var found = WorkshopFound(MarketData.Mods);
        if (_wsCount is not null) _wsCount.Text = FoundText(found.Count, _wsQuery).Text;
        if (_wsSortBox is not null) _wsSortBox.IsVisible = _wsQuery == "";
        if (found.Count == 0)
        {
            _wsResults.Content = HubLook.Empty(Icons.Search, I18n.T("hub.nothing"), I18n.T("hub.nothing.text"), Ui.Button(I18n.T("v93.mk.f.reset"), ResetWorkshop, "", Icons.Undo));
            return;
        }
        var results = Ui.Col(14, StoreKit.Tiles(found.Take(_wsShown).Select(m => MarketTiles.Workshop(m, 1)), 250, MarketTiles.WorkshopFoot));
        if (found.Count > _wsShown)
            results.Children.Add(More(found.Count - _wsShown, () => { _wsShown += PageSize; FillWorkshop(); }));
        _wsResults.Content = results;
    }

    void ResetWorkshop()
    {
        _wsKind = "all"; _wsGame = null; _wsTag = null; _wsQuery = ""; _wsPending = ""; _wsShown = PageSize;
        Build();
    }

    /// <summary>
    /// 26.0: умный поиск модов сообщества — тот же, что у каталогов (SearchWords): русские слова находят английские
    /// названия, формы слова не важны. Ищет в названии, описании, метках (и их названиях на языке программы) и авторе.
    /// Остаются моды, где совпало больше всего слов; выше — совпадения в названии, затем — популярные.
    /// </summary>
    internal static List<HubMod> SmartFind(IEnumerable<HubMod> mods, string query)
    {
        var plan = ModLaunch.Sources.SearchWords.Make(query);
        var scored = mods.Select(m =>
        {
            var info = Searchable(m);
            return (Mod: m, Score: ModLaunch.Sources.SearchWords.Score(info, plan), Name: ModLaunch.Sources.SearchWords.NameScore(info, plan));
        }).Where(x => x.Score > 0).ToList();
        if (scored.Count == 0) return [];
        var best = scored.Max(x => x.Score);
        return scored.Where(x => x.Score == best).OrderByDescending(x => x.Name).ThenByDescending(x => x.Mod.Downloads).Select(x => x.Mod).ToList();
    }

    /// <summary>Мод для поиска: как в каталоге игры (Hub.ToModInfo), плюс названия меток на языке программы и автор.</summary>
    static ModLaunch.Sources.ModInfo Searchable(HubMod h)
    {
        var m = Hub.ToModInfo(h);
        return new ModLaunch.Sources.ModInfo
        {
            Source = m.Source, Id = m.Id, Name = m.Name, Author = m.Author, Description = m.Description, Downloads = m.Downloads,
            Categories = [.. m.Categories, .. h.Tags.Select(t => I18n.T("hub.tag." + t)), h.Author],
        };
    }

    // ---------------------------------------------------------------- обновления отслеживаемых модов

    /// <summary>
    /// Отслеживаемые моды сообщества с новой версией: из «Следить» на странице мода (Features.Tracking, источник hub)
    /// и из старого списка подписок Hub (Hub.Updates).
    /// </summary>
    static List<(HubMod Mod, Features.Tracked? Track)> TrackedUpdates(List<HubMod> all)
    {
        var list = Hub.Updates.Select(m => (Mod: m, Track: (Features.Tracked?)null)).ToList();
        foreach (var t in Features.Tracking.All().Where(t => t.Source == "hub" && t.Version != ""))
        {
            if (all.FirstOrDefault(m => m.Id == t.Id) is not { } mod || mod.Version == t.Version) continue;
            var at = list.FindIndex(x => x.Mod.Id == mod.Id);
            if (at >= 0) list[at] = (list[at].Mod, t);
            else list.Add((mod, t));
        }
        return list;
    }

    /// <summary>Обновление просмотрено: запомнить новую версию в обоих списках (и колокольчик уведомлений его забудет).</summary>
    static void SeenUpdate(HubMod m, Features.Tracked? t)
    {
        Hub.MarkSeen(m);
        if (t is null) return;
        var u = Features.Tracking.Updates.FirstOrDefault(x => x.Item.Source == "hub" && x.Item.Id == m.Id) ?? new Features.TrackedUpdate(t, Hub.ToModInfo(m));
        Features.Tracking.Seen(u);
    }

    const int UpdatesShown = 6;

    Control UpdatesCard(List<(HubMod Mod, Features.Tracked? Track)> updates)
    {
        var clear = Ui.Button(I18n.T("nx.notify.clear"), () => { foreach (var (m, t) in updates) SeenUpdate(m, t); Build(); }, "ghost", Icons.Check);
        var title = Ui.Row(10, Ui.Icon(Icons.Bell, 16, HubLook.Violet),
            new TextBlock { Text = I18n.T("hub.followUpdates", ("n", updates.Count)), FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        title.VerticalAlignment = VerticalAlignment.Center;
        var rows = new UniformGrid { Columns = updates.Count > 1 ? 2 : 1 };
        foreach (var (m, t) in updates.Take(UpdatesShown)) rows.Children.Add(UpdateRow(m, t));
        var col = Ui.Col(10, FilterBar(title, clear), rows);
        if (updates.Count > UpdatesShown) col.Children.Add(Ui.Text(I18n.T("v26.market.updates.more", ("n", updates.Count - UpdatesShown)), "small muted"));
        return HubLook.Frame(col, new Thickness(16, 14));
    }

    /// <summary>Строка обновления: обложка, название, «было → стало» и игра; щелчок — страница мода, обновление прочитано.</summary>
    static Control UpdateRow(HubMod m, Features.Tracked? t)
    {
        var art = new Border { Width = 88, Height = 50, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = MarketTiles.Art(m, 384) };
        var version = t is { Version.Length: > 0 } ? $"{t.Version} → {m.Version}" : m.Version;
        var meta = Ui.Row(8, HubLook.Badge(I18n.T("v26.market.updates.version", ("version", version)), "accent", Icons.Download),
            new TextBlock { Text = MarketTiles.GameName(m.Game), FontSize = 12, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var words = Ui.Col(4, new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis }, meta);
        words.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(art);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        var row = new Border { Classes = { "store-row" }, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(8), Child = grid };
        StoreKit.OnClick(row, () => { SeenUpdate(m, t); CreatorPage.OpenMod(m); });
        return row;
    }

    [SelfTest]
    static string CommunitySmartSearch()
    {
        var mods = Demo.Hub();
        var fail = new List<string>();
        void Expect(string query, string first, int count)
        {
            var got = SmartFind(mods, query);
            if (got.Count != count || got.FirstOrDefault()?.Id != first) fail.Add($"«{query}» → {string.Join(",", got.Select(m => m.Id))}, ждали {first} ×{count}");
        }
        Expect("vega", "demo_hud", 1);             // автор
        Expect("семена", "demo_seeds", 1);         // русское слово → seed в номере и описании
        Expect("multiplayer", "demo_night", 2);    // метка; равные — по скачиваниям
        Expect("звук воды", "demo_audio", 1);      // два слова: метка audio и слово из описания
        if (SmartFind(mods, "абракадабра").Count != 0) fail.Add("nonsense query found something");
        if (fail.Count > 0) throw new Exception(string.Join("; ", fail));
        return "community search: author, Russian word, tag and two-word queries find the right mods; best match first";
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
            // 26.0: общее пустое состояние хаба; кнопка говорит, что сделает («Смотреть рынок»), а не название вкладки.
            col.Children.Add(HubLook.Empty(Icons.Heart, I18n.T("v93.mk.wish.empty"), I18n.T("v23.mk.wish.empty.text"),
                Ui.Button(I18n.T("v26.market.wish.browse"), () => Go("store"), "primary", Icons.Bag)));
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
            col.Children.Add(HubLook.Empty(Icons.Bag, I18n.T("v93.mk.library.empty"), I18n.T("v93.mk.library.empty.text"),
                Ui.Button(I18n.T("v26.market.wish.browse"), () => Go("store"), "primary", Icons.Bag)));
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

    static Control SignIn(string text) => HubLook.Empty(Icons.User, I18n.T("st.signin"), text,
        Ui.Button(I18n.T("acc.menu.signin"), () => MainWindow.Current?.Navigate(() => new AccountPage()), "primary", Icons.User));
}
