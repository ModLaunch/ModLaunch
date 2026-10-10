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
/// 9.3: Creator Hub — самостоятельный рынок креаторов, устроенный как магазин Steam:
/// своя шапка с разделами (Магазин, Обзор, Мастерская, Очередь открытий, Авторы, Желаемое,
/// Библиотека, Студия), витрина с подборкой «Рекомендуемое», категории, вкладки
/// «Новинки / Лидеры продаж / Бесплатно / До 100 ₽» с предпросмотром при наведении,
/// авторы недели, Мастерская бесплатных модов и «Станьте автором». Лоты, покупки, отзывы
/// и выплаты — прежние (Market), желаемое и подписки на авторов — на этом компьютере.
/// </summary>
public sealed partial class MarketPage : Page
{
    public static readonly string[] Tabs = ["store", "browse", "workshop", "queue", "authors", "wishlist", "library", "studio"];

    string _tab;
    string _query = "";
    // Обзор: фильтры как в поиске Steam.
    string _price = "all";
    readonly HashSet<string> _kinds = [];
    string? _game;
    string? _tag;
    string _sort = "popular";
    string _view = Settings.Data.Str("marketView") == "list" ? "list" : "grid";
    // Мастерская.
    string _wsSort = "trending";
    string? _wsGame;
    // Витрина.
    static int _featuredIndex;
    string _listTab = "new";
    DispatcherTimer? _rotate;

    public MarketPage(string tab = "store", string query = "")
    {
        _tab = Tabs.Contains(tab) ? tab : "store";
        _query = query;
        if (_query != "") _tab = "browse";
        MarketLocal.Changed += OnLocal;
        MarketData.Changed += OnData;
        DetachedFromVisualTree += (_, _) => { MarketLocal.Changed -= OnLocal; MarketData.Changed -= OnData; _rotate?.Stop(); };
        AttachedToVisualTree += (_, _) => { MarketLocal.Changed -= OnLocal; MarketData.Changed -= OnData; MarketLocal.Changed += OnLocal; MarketData.Changed += OnData; };
    }

    void OnLocal() { if (_tab is "wishlist" or "authors") Build(); }
    void OnData() { if (MainWindow.Current?.CurrentPage == this) Build(); }

    public override string Title => "Creator Hub";
    public override string SearchHint => I18n.T("v93.mk.search");
    public override string? Accent => null;

    public override IEnumerable<(string Text, Action? Open)> Crumbs =>
        _tab == "store" ? [("Creator Hub", null)] : [("Creator Hub", () => MainWindow.Current?.Navigate(() => new MarketPage())), (I18n.T("v93.mk.tab." + _tab), null)];

    public override void Search(string text) { _query = text.Trim(); _tab = "browse"; Build(); MainWindow.Current?.RenderCrumbs(); }

    public override bool SameScreenAs(Page? previous) => previous is MarketPage;

    void Go(string tab) { _tab = tab; Build(); MainWindow.Current?.RenderCrumbs(); }

    public override void Build()
    {
        if (MarketData.Listings is null && !MarketData.Loading) _ = MarketData.Load();
        _rotate?.Stop();
        var col = StoreKit.Column(spacing: 22, top: 20);
        col.Children.Add(Header());
        col.Children.Add(NavBar());
        var loading = MarketData.Listings is null;
        col.Children.Add(_tab switch
        {
            "browse" => loading ? Skeleton() : Browse(),
            "workshop" => loading ? Skeleton() : WorkshopView(),
            "queue" => loading ? Skeleton() : Queue(),
            "authors" => loading ? Skeleton() : Authors(),
            "wishlist" => loading ? Skeleton() : Wishlist(),
            "library" => Library(),
            "studio" => Studio(),
            _ => loading ? Skeleton() : Front(),
        });
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Skeleton()
    {
        var col = Ui.Col(StoreKit.Gap);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = StoreKit.Gap, Height = 360 };
        var a = StoreKit.Placeholder(double.NaN, double.NaN);
        var b = StoreKit.Placeholder(double.NaN, double.NaN);
        Grid.SetColumn(b, 1);
        grid.Children.Add(a);
        grid.Children.Add(b);
        col.Children.Add(grid);
        var row = new UniformGrid { Columns = 5, Height = 140 };
        for (var i = 0; i < 5; i++) { var p = StoreKit.Placeholder(double.NaN, double.NaN); p.Margin = new Thickness(0, 0, StoreKit.Gap, 0); row.Children.Add(p); }
        col.Children.Add(row);
        return col;
    }

    // ---------------------------------------------------------------- шапка и разделы

    Control Header()
    {
        var title = new TextBlock { Text = "Creator Hub", FontFamily = Gx.Display, FontSize = 26, FontWeight = FontWeight.Bold, LetterSpacing = -0.6 };
        var words = Ui.Col(3, title, Ui.Text(I18n.T("v93.mk.sub", ("n", 100 - Market.FeePercent)), "muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        var left = Ui.Row(16, CreatorLogo.Tile(52), words);

        var right = Ui.Row(10);
        right.VerticalAlignment = VerticalAlignment.Center;
        if (Social.Account.SignedIn && MarketData.Wallet is { } w)
        {
            var wallet = new Button
            {
                Classes = { "chip" }, Padding = new Thickness(12, 7),
                Content = Ui.Row(8, Ui.Icon(Icons.Bag, 15, Ui.Res("Brand2")), new TextBlock { Text = Market.Money(w.Balance), FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }),
            };
            ToolTip.SetTip(wallet, I18n.T("v93.mk.wallet"));
            wallet.Click += (_, _) => MainWindow.Current?.Navigate(() => new AccountPage("wallet"));
            right.Children.Add(wallet);
        }
        right.Children.Add(Ui.Button(I18n.T("v93.mk.create"), () => MainWindow.Current?.Navigate(() => new CreatorPage()), "", Icons.Edit));
        right.Children.Add(Ui.Button(I18n.T("mk.sell"), () => MarketViews.Editor(null, MarketData.Reload), "primary", Icons.Plus));
        var head = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(left);
        return head;
    }

    /// <summary>Полоса разделов рынка, как меню магазина Steam: слева — витрина, справа — своё.</summary>
    Control NavBar()
    {
        Button Item(string id, string? icon, string? count = null)
        {
            var label = I18n.T("v93.mk.tab." + id);
            var text = count is null ? label : $"{label}  {count}";
            var b = Ui.Button(text, () => Go(id), "pivot", icon);
            if (_tab == id) b.Classes.Add("active");
            b.Padding = new Thickness(12, 9);
            return b;
        }
        var leftRow = Ui.Row(2, Item("store", Icons.Home), Item("browse", Icons.Search), Item("workshop", Icons.Globe), Item("queue", Icons.Shuffle), Item("authors", Icons.Users));
        var wish = MarketLocal.WishCount;
        var rightRow = Ui.Row(2, Item("wishlist", Icons.Heart, wish > 0 ? wish.ToString() : null), Item("library", Icons.Layers), Item("studio", Icons.Chart));
        var bar = new DockPanel();
        DockPanel.SetDock(rightRow, Dock.Right);
        bar.Children.Add(rightRow);
        bar.Children.Add(leftRow);
        // Полоска акцента под выбранным разделом — как у вкладок страницы игры.
        return new Border { Classes = { "market-nav" }, Padding = new Thickness(4), CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), Child = bar };
    }

    static Control Section(string title, Action? all = null, Control? right = null, string? eyebrow = null)
    {
        var head = StoreKit.Header(title, all, right);
        if (eyebrow is null) return head;
        return Ui.Col(4, Gx.Eyebrow(eyebrow, Ui.Res("Brand2"), 10.5), head);
    }

    IEnumerable<Listing> Active => MarketData.All.Where(l => l.Active);

    static List<string> MyGames() => AppState.Games.Where(g => g.Status == Detect.Found).Select(g => g.Def.Id).ToList();

    // ---------------------------------------------------------------- витрина

    Control Front()
    {
        var col = Ui.Col(30);
        var all = Active.ToList();
        if (all.Count == 0)
        {
            // Сервер рынка ещё не подключён (нет правил в базе) — объясняем и ведём в «Состояние сервисов».
            if (MarketData.Error is { } error) col.Children.Add(Offline(error));
            else col.Children.Add(Empty());
            col.Children.Add(Become());
            if (MarketData.Mods.Any()) col.Children.Add(WorkshopShelf());
            return col;
        }
        var forYou = MarketLocal.ForYou(all, MyGames());
        col.Children.Add(Featured(forYou.Take(6).ToList()));
        col.Children.Add(Categories(all));
        col.Children.Add(Ui.Col(14, Section(I18n.T("v93.mk.forYou"), () => { _sort = "popular"; Go("browse"); }, eyebrow: I18n.T("v93.mk.forYou.eyebrow")),
            StoreKit.Shelf(forYou.Skip(2).Take(12).Select(l => MarketTiles.Card(l)))));
        col.Children.Add(Tabbed(all));
        var authors = MarketLocal.Authors(all, MarketData.Mods);
        if (authors.Count > 0) col.Children.Add(Ui.Col(14, Section(I18n.T("v93.mk.authorsWeek"), () => Go("authors")), AuthorRow(authors.Take(4).ToList())));
        if (MarketData.Mods.Any()) col.Children.Add(WorkshopShelf());
        var mine = MyGames();
        var byGame = all.Where(l => mine.Contains(l.Game)).GroupBy(l => l.Game).OrderByDescending(g => g.Count()).Take(3).ToList();
        foreach (var g in byGame)
        {
            var id = g.Key;
            var def = GameCatalog.ById(id);
            if (def is null) continue;
            col.Children.Add(Ui.Col(14, Section(I18n.T("v93.mk.forGame", ("game", def.Name)), () => { _game = id; _kinds.Clear(); _price = "all"; Go("browse"); }),
                StoreKit.Shelf(g.OrderByDescending(l => l.Sales).Select(l => MarketTiles.Card(l, 248)))));
        }
        col.Children.Add(Become());
        return col;
    }

    static Control Offline(string error)
    {
        var steps = Ui.Col(6,
            Ui.Text(I18n.T("v93.mk.offline.1"), "small", wrap: true),
            Ui.Text(I18n.T("v93.mk.offline.2"), "small", wrap: true),
            Ui.Text(I18n.T("v93.mk.offline.3"), "small", wrap: true));
        var detail = Ui.Text(error, "small muted", wrap: true);
        var buttons = Ui.Row(10,
            Ui.Button(I18n.T("v93.mk.offline.connect"), () => MainWindow.Current?.Navigate(() => new SettingsPage("services")), "primary", Icons.Zap),
            Ui.Button(I18n.T("cr.refresh"), MarketData.Reload, "", Icons.Refresh));
        var words = Ui.Col(12,
            Gx.Eyebrow(I18n.T("v93.mk.offline.eyebrow"), Ui.Res("Warn")),
            Gx.Title(I18n.T("v93.mk.offline"), 22, Ui.Res("Text")),
            Ui.Text(I18n.T("v93.mk.offline.text"), "muted", wrap: true),
            steps, detail, buttons);
        var icon = new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(18), Background = Ui.Res("Surface2"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top, Child = Ui.Icon(Icons.Server, 32, Ui.Res("Warn")),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 22 };
        grid.Children.Add(icon);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return new Border { Classes = { "card" }, Padding = new Thickness(26, 22), Child = grid };
    }

    static Control Empty() => Ui.Card(Ui.Col(10,
        Gx.Title(I18n.T("mk.empty"), 24, Ui.Res("Text")),
        Ui.Text(I18n.T("v93.mk.empty.text"), "muted", wrap: true),
        Ui.Row(10, Ui.Button(I18n.T("mk.sell"), () => MarketViews.Editor(null, MarketData.Reload), "primary", Icons.Plus),
            Ui.Button(I18n.T("cr.refresh"), MarketData.Reload, "", Icons.Refresh))), 28);

    /// <summary>«Рекомендуемое»: большая капсула слева, сведения и покупка справа, листается само.</summary>
    Control Featured(List<Listing> picks)
    {
        if (picks.Count == 0) return new Border();
        _featuredIndex %= picks.Count;
        var host = new Panel();
        var info = new Border { Classes = { "card" }, Padding = new Thickness(22) };
        var pips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        void Show(int i, bool animate)
        {
            _featuredIndex = (i + picks.Count) % picks.Count;
            var l = picks[_featuredIndex];
            var art = MarketTiles.Art(l, 1600);
            var title = Gx.Title(l.Title, 30, Brushes.White);
            var words = Ui.Col(10, Gx.Eyebrow(I18n.T("v93.mk.featured"), Ui.Hex("#E6E7EE")), title,
                Ui.Row(8, StoreKit.Pill(I18n.T("mk.kind." + l.Kind)), StoreKit.Pill(MarketTiles.GameName(l.Game))));
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(28, 0, 28, 26);
            var wish = MarketTiles.WishButton(l);
            wish.HorizontalAlignment = HorizontalAlignment.Right;
            wish.VerticalAlignment = VerticalAlignment.Top;
            wish.Margin = new Thickness(14);
            var slide = new Panel { Children = { art, new Border { Background = Gx.ShadeUp(0.35, 230) }, Gx.Haze(true), words, wish } };
            StoreKit.OnClick(slide, () => MarketTiles.Open(l));
            host.Children.Clear();
            host.Children.Add(slide);
            if (animate) Animate.From(slide, "translateX(30px)", 420, 0, new Avalonia.Animation.Easings.CubicEaseOut());

            var tags = new WrapPanel();
            foreach (var t in l.Tags.Take(5))
            {
                var tag = new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = I18n.T("hub.tag." + t), FontSize = 11.5, Foreground = Ui.Res("Muted") } };
                tags.Children.Add(tag);
            }
            var author = Ui.Row(10, Ui.Thumb(null, l.Author, 34, 17, person: true), Ui.Col(1, Ui.Text(l.Author, "h3"), Ui.Text(I18n.T("v93.mk.author"), "small muted")));
            var authorLink = new Border { Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Child = author };
            StoreKit.OnClick(authorLink, () => MainWindow.Current?.Navigate(() => new AuthorPage(l.Uid, l.Author)));
            var buy = Ui.Button(l.Mine || Market.Owned.Contains(l.Id) ? I18n.T("v93.mk.open") : l.Free ? I18n.T("mk.getFree") : I18n.T("mk.buy", ("price", Market.Money(l.Price))), () => MarketTiles.Open(l), "primary", l.Free ? Icons.Download : Icons.Bag);
            buy.HorizontalAlignment = HorizontalAlignment.Stretch;
            buy.HorizontalContentAlignment = HorizontalAlignment.Center;
            buy.Padding = new Thickness(16, 11);
            var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            var sold = Gx.Stat(I18n.Compact(l.Sales), I18n.T("st.sales"), null, 20);
            var ver = Gx.Stat("v" + l.Version, I18n.T("hub.f.version"), null, 20);
            Grid.SetColumn(ver, 1);
            stats.Children.Add(sold);
            stats.Children.Add(ver);
            var body = new DockPanel();
            var bottom = Ui.Col(14, stats, buy);
            DockPanel.SetDock(bottom, Dock.Bottom);
            body.Children.Add(bottom);
            body.Children.Add(Ui.Col(14, authorLink,
                new TextBlock { Text = l.Summary, TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14, Foreground = Ui.Res("Text"), LineHeight = 20 },
                tags));
            info.Child = body;
            for (var k = 0; k < pips.Children.Count; k++)
                if (pips.Children[k] is Border { Child: Border pip })
                {
                    pip.Width = k == _featuredIndex ? 24 : 8;
                    pip.Background = k == _featuredIndex ? Ui.Res("Brand") : Ui.Res("Surface3");
                }
        }
        for (var k = 0; k < picks.Count; k++)
        {
            var index = k;
            var pip = new Border { Height = 6, Width = 8, CornerRadius = new CornerRadius(3), Background = Ui.Res("Surface3") };
            var hit = new Border { Padding = new Thickness(0, 6), Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Child = pip };
            hit.PointerPressed += (_, _) => Show(index, true);
            pips.Children.Add(hit);
        }
        Show(_featuredIndex, false);

        Button Arrow(string icon, int dir)
        {
            var b = new Button
            {
                Classes = { "icon", "shelf-arrow" }, Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Content = Ui.Icon(icon, 18),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = dir < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, Margin = new Thickness(12),
            };
            b.Click += (_, _) => Show(_featuredIndex + dir, true);
            return b;
        }
        var stage = new Border { Classes = { "store-tile", "hero" }, Child = new Panel { Children = { host, Arrow(Icons.ChevronLeft, -1), Arrow(Icons.ChevronRight, 1) } } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = StoreKit.Gap, Height = 380 };
        grid.Children.Add(stage);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        if (picks.Count > 1 && !Program.Screenshot)
        {
            var hover = false;
            grid.PointerEntered += (_, _) => hover = true;
            grid.PointerExited += (_, _) => hover = false;
            _rotate = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _rotate.Tick += (_, _) => { if (!hover) Show(_featuredIndex + 1, true); };
            var timer = _rotate;
            grid.AttachedToVisualTree += (_, _) => timer.Start();
            grid.DetachedFromVisualTree += (_, _) => timer.Stop();
        }
        return Ui.Col(10, Section(I18n.T("v93.mk.featured.title"), eyebrow: I18n.T("v93.mk.featured.eyebrow")), grid, pips);
    }

    static readonly (string Kind, string Hex)[] KindColors =
    [
        ("model", "#7B5CFF"), ("code", "#2F8CFF"), ("script", "#FF8A3D"), ("asset", "#E0498F"), ("pack", "#2BB673"),
    ];

    /// <summary>Категории: плитка на каждый тип лота и «Бесплатно» — с числом лотов.</summary>
    Control Categories(List<Listing> all)
    {
        var grid = new Grid { ColumnSpacing = StoreKit.Gap, Height = 132 };
        var items = KindColors.Select(k => (Id: k.Kind, Hex: k.Hex, Title: I18n.T("mk.kind." + k.Kind), Icon: MarketTiles.KindIcon(k.Kind), Count: all.Count(l => l.Kind == k.Kind))).ToList();
        items.Add(("free", "#16924C", I18n.T("mk.free"), Icons.Download, all.Count(l => l.Free)));
        for (var i = 0; i < items.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var (id, hex, title, icon, count) = items[i];
            var glyph = Ui.Icon(icon, 54, new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)));
            glyph.HorizontalAlignment = HorizontalAlignment.Right;
            glyph.VerticalAlignment = VerticalAlignment.Bottom;
            glyph.Margin = new Thickness(0, 0, -6, -8);
            var words = Ui.Col(2, Gx.Title(title, 16, Brushes.White, 1), new TextBlock { Text = I18n.T("v93.mk.items", ("n", count)), FontSize = 12, Foreground = Ui.Hex("#E2E4EA") });
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(14, 0, 14, 12);
            var tile = new Border { Classes = { "store-tile", "feed-tile" }, Child = new Panel { Children = { Gx.Gradient(hex, 0.8), Gx.Scanlines(), glyph, words } } };
            var kind = id;
            StoreKit.OnClick(tile, () =>
            {
                _kinds.Clear(); _game = null; _tag = null;
                if (kind == "free") _price = "free"; else { _price = "all"; _kinds.Add(kind); }
                Go("browse");
            });
            Grid.SetColumn(tile, i);
            grid.Children.Add(tile);
        }
        return Ui.Col(14, Section(I18n.T("v93.mk.categories")), grid);
    }

    /// <summary>Вкладки «Новинки / Лидеры продаж / Бесплатно / До 100 ₽»: список слева, предпросмотр справа (как в Steam).</summary>
    Control Tabbed(List<Listing> all)
    {
        var cheap = 10000;
        List<Listing> Of(string id) => id switch
        {
            "top" => all.OrderByDescending(l => l.Sales).ToList(),
            "free" => all.Where(l => l.Free).OrderByDescending(l => l.Sales).ToList(),
            "cheap" => all.Where(l => !l.Free && l.Price <= cheap).OrderByDescending(l => l.Sales).ToList(),
            _ => all.OrderByDescending(l => l.Updated).ToList(),
        };
        var tabs = Ui.Row(2);
        foreach (var (id, key) in new[] { ("new", "v93.mk.list.new"), ("top", "v93.mk.list.top"), ("free", "v93.mk.list.free"), ("cheap", "v93.mk.list.cheap") })
        {
            var t = id;
            var label = id == "cheap" ? I18n.T(key, ("price", Market.Money(cheap))) : I18n.T(key);
            var b = Ui.Button(label, () => { _listTab = t; Build(); }, _listTab == id ? "pivot active" : "pivot");
            tabs.Children.Add(b);
        }
        var list = Of(_listTab).Take(8).ToList();
        var preview = new Border { Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true };
        void Preview(Listing l)
        {
            var art = new Border { Height = 190, ClipToBounds = true, Child = MarketTiles.Art(l, 768) };
            var tags = new WrapPanel();
            foreach (var t in l.Tags.Take(4))
                tags.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = I18n.T("hub.tag." + t), FontSize = 11.5, Foreground = Ui.Res("Muted") } });
            var body = Ui.Col(10, Gx.Title(l.Title, 18, Ui.Res("Text")),
                Ui.Text($"{I18n.T("mk.kind." + l.Kind)} · {MarketTiles.GameName(l.Game)} · {l.Author}", "small muted"),
                new TextBlock { Text = l.Summary, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Res("Muted"), MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis },
                tags,
                Ui.Row(10, Gx.Price(MarketViews.PriceText(l), l.Free), Ui.Text(I18n.T("mk.sold", ("n", l.Sales)), "small muted")));
            body.Margin = new Thickness(18, 16, 18, 18);
            preview.Child = Ui.Col(0, art, body);
        }
        var rows = Ui.Col(4);
        if (list.Count == 0) rows.Children.Add(Ui.Text(I18n.T("hub.nothing"), "muted"));
        for (var i = 0; i < list.Count; i++)
        {
            var l = list[i];
            var row = MarketTiles.Row(l, _listTab == "top" ? i + 1 : null);
            row.PointerEntered += (_, _) => Preview(l);
            rows.Children.Add(row);
        }
        if (list.Count > 0) Preview(list[0]);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 18 };
        grid.Children.Add(rows);
        Grid.SetColumn(preview, 1);
        preview.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(preview);
        var more = Ui.Button(I18n.T("home.all") + "  ›", () => { _sort = _listTab == "new" ? "new" : "popular"; _price = _listTab is "free" ? "free" : _listTab == "cheap" ? "cheap" : "all"; Go("browse"); }, "ghost");
        more.Foreground = Ui.Res("Muted");
        var head = new DockPanel();
        DockPanel.SetDock(more, Dock.Right);
        head.Children.Add(more);
        head.Children.Add(tabs);
        return Ui.Col(12, head, new Border { Height = 1, Background = Ui.Res("Line") }, grid);
    }

    static Control AuthorRow(List<MarketLocal.Author> authors)
    {
        var grid = new Grid { ColumnSpacing = StoreKit.Gap };
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < authors.Count; i++)
        {
            var c = AuthorCard(authors[i]);
            Grid.SetColumn(c, i);
            grid.Children.Add(c);
        }
        return grid;
    }

    /// <summary>Карточка автора: аватар, имя, лоты и продажи, кнопка «Подписаться».</summary>
    public static Control AuthorCard(MarketLocal.Author a)
    {
        var hue = (int)(a.Name.Aggregate(17u, (h, c) => h * 31 + c) % 360);
        var banner = new Border
        {
            Height = 64,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Ui.HslColor(hue, 0.55, 0.42), 0), new GradientStop(Ui.HslColor((hue + 60) % 360, 0.6, 0.22), 1) },
            },
        };
        var avatar = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), BorderThickness = new Thickness(3), BorderBrush = Ui.Res("Surface"),
            Child = Ui.Thumb(null, a.Name, 58, 29, person: true), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, -32, 0, 0),
        };
        var follow = FollowButton(a.Uid, a.Name);
        var stats = Ui.Row(18, Gx.Stat(a.Items.ToString(), I18n.T("v93.mk.items.short"), null, 17), Gx.Stat(I18n.Compact(a.Sales), I18n.T("st.sales"), null, 17));
        if (a.Workshop > 0) stats.Children.Add(Gx.Stat(I18n.Compact(a.Downloads), I18n.T("hub.stat.downloads"), null, 17));
        var body = Ui.Col(10,
            Ui.Col(2, new TextBlock { Text = a.Name, FontWeight = FontWeight.Bold, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis },
                Ui.Text(a.Games.Count > 0 ? string.Join(", ", a.Games.Take(3).Select(MarketTiles.GameName)) : I18n.T("mk.anyGame"), "small muted")),
            stats, follow);
        body.Margin = new Thickness(16, 8, 16, 16);
        var card = new Border { Classes = { "store-tile", "feed-tile" }, Child = Ui.Col(0, banner, avatar, body) };
        StoreKit.OnClick(card, () => MainWindow.Current?.Navigate(() => new AuthorPage(a.Uid, a.Name)));
        return card;
    }

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

    Control WorkshopShelf()
    {
        var mods = Hub.Sort(MarketData.Mods, "trending").Take(12).ToList();
        return Ui.Col(14, Section(I18n.T("v93.mk.workshop.shelf"), () => Go("workshop"), eyebrow: I18n.T("v93.mk.workshop.eyebrow")),
            StoreKit.Shelf(mods.Select(m => MarketTiles.WorkshopCard(m))));
    }

    /// <summary>«Станьте автором»: сколько получает автор, три шага и кнопка.</summary>
    static Control Become()
    {
        var steps = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 14 };
        var items = new[] { (Icons.Edit, "v93.mk.become.1"), (Icons.Upload, "v93.mk.become.2"), (Icons.Bag, "v93.mk.become.3") };
        for (var i = 0; i < items.Length; i++)
        {
            var (icon, key) = items[i];
            var num = new TextBlock { Text = (i + 1).ToString(), FontFamily = Gx.Display, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)) };
            var step = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
                Child = Ui.Row(12, num, Ui.Col(4, Ui.Icon(icon, 18, Brushes.White), new TextBlock { Text = I18n.T(key), Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Left })),
            };
            Grid.SetColumn(step, i);
            steps.Children.Add(step);
        }
        var buttons = Ui.Row(10,
            Ui.Button(I18n.T("mk.sell"), () => MarketViews.Editor(null, MarketData.Reload), "hero-light", Icons.Plus),
            Ui.Button(I18n.T("v93.mk.create"), () => MainWindow.Current?.Navigate(() => new CreatorPage()), "hero-ghost", Icons.Edit));
        var words = Ui.Col(14,
            Gx.Eyebrow(I18n.T("v93.mk.become.eyebrow"), Ui.Hex("#F0F0F5")),
            Gx.Title(I18n.T("v93.mk.become", ("n", 100 - Market.FeePercent)), 26, Brushes.White),
            new TextBlock { Text = I18n.T("v93.mk.become.text", ("fee", Market.FeePercent)), Foreground = Ui.Hex("#E2E4EA"), FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left },
            steps, buttons);
        words.Margin = new Thickness(30, 26);
        return new Border { Classes = { "store-tile" }, Cursor = null, Child = new Panel { Children = { Gx.Gradient(Look.Accent, 0.35), Gx.Scanlines(), Gx.Haze(true), words } } };
    }

    // ---------------------------------------------------------------- обзор (поиск с фильтрами)

    Control Browse()
    {
        var all = Active.ToList();
        bool Match(Listing l) =>
            (_query == "" || l.Title.Contains(_query, StringComparison.OrdinalIgnoreCase) || l.Author.Contains(_query, StringComparison.OrdinalIgnoreCase) || l.Summary.Contains(_query, StringComparison.OrdinalIgnoreCase) || l.Tags.Any(t => I18n.T("hub.tag." + t).Contains(_query, StringComparison.OrdinalIgnoreCase)))
            && (_kinds.Count == 0 || _kinds.Contains(l.Kind))
            && (_game is null || l.Game == _game)
            && (_tag is null || l.Tags.Contains(_tag))
            && _price switch { "free" => l.Free, "paid" => !l.Free, "cheap" => !l.Free && l.Price <= 10000, "mid" => !l.Free && l.Price <= 50000, _ => true };
        var found = Market.Sort(all.Where(Match), _sort switch { "new" => "new", "cheap" => "cheap", "expensive" => "expensive", _ => "popular" }).ToList();

        // Слева — фильтры.
        Control Group(string title, IEnumerable<Control> items) => Ui.Col(6, Gx.Eyebrow(title, null, 10.5), Ui.Col(2, items.ToArray()));
        Button Opt(string text, bool on, Action click, string? count = null, string? icon = null)
        {
            var b = Ui.Button(count is null ? text : $"{text}  ·  {count}", () => { click(); Build(); }, on ? "chip side active" : "chip side", icon);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            return b;
        }
        var side = Ui.Col(18);
        side.Children.Add(Group(I18n.T("v93.mk.f.price"), new[]
        {
            Opt(I18n.T("mk.kind.all"), _price == "all", () => _price = "all"),
            Opt(I18n.T("mk.free"), _price == "free", () => _price = "free", all.Count(l => l.Free).ToString()),
            Opt(I18n.T("v93.mk.f.paid"), _price == "paid", () => _price = "paid"),
            Opt(I18n.T("v93.mk.f.upTo", ("price", Market.Money(10000))), _price == "cheap", () => _price = "cheap"),
            Opt(I18n.T("v93.mk.f.upTo", ("price", Market.Money(50000))), _price == "mid", () => _price = "mid"),
        }));
        side.Children.Add(Group(I18n.T("v93.mk.f.kind"), Market.Kinds.Select(k => (Control)Opt(I18n.T("mk.kind." + k), _kinds.Contains(k), () => { if (!_kinds.Remove(k)) _kinds.Add(k); }, all.Count(l => l.Kind == k).ToString(), MarketTiles.KindIcon(k)))));
        var games = all.Where(l => l.Game != "").GroupBy(l => l.Game).OrderByDescending(g => g.Count()).Take(10).ToList();
        if (games.Count > 0)
            side.Children.Add(Group(I18n.T("v93.mk.f.game"), new[] { (Control)Opt(I18n.T("hub.allGames"), _game is null, () => _game = null) }
                .Concat(games.Select(g => (Control)Opt(MarketTiles.GameName(g.Key), _game == g.Key, () => _game = _game == g.Key ? null : g.Key, g.Count().ToString())))));
        var tags = new WrapPanel();
        foreach (var t in all.SelectMany(l => l.Tags).GroupBy(t => t).OrderByDescending(g => g.Count()).Take(14).Select(g => g.Key))
        {
            var tag = t;
            var chip = Ui.Button("#" + I18n.T("hub.tag." + t), () => { _tag = _tag == tag ? null : tag; Build(); }, _tag == t ? "chip active" : "chip");
            chip.Padding = new Thickness(10, 4);
            chip.FontSize = 12;
            chip.Margin = new Thickness(0, 0, 6, 6);
            tags.Children.Add(chip);
        }
        if (tags.Children.Count > 0) side.Children.Add(Ui.Col(8, Gx.Eyebrow(I18n.T("v93.mk.f.tags"), null, 10.5), tags));
        if (_kinds.Count > 0 || _game is not null || _tag is not null || _price != "all" || _query != "")
            side.Children.Add(Ui.Button(I18n.T("v93.mk.f.reset"), () => { _kinds.Clear(); _game = null; _tag = null; _price = "all"; _query = ""; Build(); }, "ghost", Icons.Undo));

        // Справа — итог, сортировка, вид и результаты.
        var sorts = new[] { "popular", "new", "cheap", "expensive" };
        var sort = new ComboBox { Width = 190, ItemsSource = sorts.Select(s => I18n.T("mk.sort." + s)).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(sorts, _sort)) };
        sort.SelectionChanged += (_, _) => { var v = sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _sort) { _sort = v; Build(); } };
        Button View(string id, string icon) => Ui.Button("", () => { _view = id; Settings.Data["marketView"] = id; Settings.Save(); Build(); }, _view == id ? "icon active" : "icon ghost", icon);
        var tools = Ui.Row(10, sort, new Border { Classes = { "card" }, Padding = new Thickness(3), Child = Ui.Row(2, View("grid", Icons.Grid), View("list", Icons.List)) });
        var count = Ui.Text(_query != "" ? I18n.T("v93.mk.found.q", ("n", found.Count), ("q", _query)) : I18n.T("v93.mk.found", ("n", found.Count)), "h3");
        count.VerticalAlignment = VerticalAlignment.Center;
        var bar = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        bar.Children.Add(tools);
        bar.Children.Add(count);

        Control results;
        if (found.Count == 0)
            results = Ui.Card(Ui.Col(8, Ui.Text(I18n.T("hub.nothing"), "h3"), Ui.Text(I18n.T("v93.mk.nothing.text"), "muted", wrap: true)), 24);
        else if (_view == "list")
        {
            var rows = Ui.Col(4);
            foreach (var l in found) rows.Children.Add(MarketTiles.Row(l));
            results = rows;
        }
        else
        {
            results = StoreKit.Tiles(found.Select(l => MarketTiles.Listing(l, 1)), 250);
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), ColumnSpacing = 26 };
        grid.Children.Add(side);
        var right = Ui.Col(16, bar, results);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    // ---------------------------------------------------------------- Мастерская (бесплатные моды сообщества)

    Control WorkshopView()
    {
        var all = MarketData.Mods.ToList();
        var col = Ui.Col(18);
        var sorts = Ui.Row(6);
        foreach (var (id, key) in new[] { ("trending", "hub.sort.trending"), ("new", "hub.sort.new"), ("downloads", "hub.sort.downloads"), ("likes", "hub.sort.likes") })
        {
            var s = id;
            sorts.Children.Add(Ui.Button(I18n.T(key), () => { _wsSort = s; Build(); }, _wsSort == id ? "chip active" : "chip"));
        }
        var game = new ComboBox { Width = 210 };
        var games = new List<string?> { null };
        game.Items.Add(I18n.T("hub.allGames"));
        foreach (var g in all.Select(m => m.Game).Distinct()) { games.Add(g); game.Items.Add(MarketTiles.GameName(g)); }
        game.SelectedIndex = Math.Max(0, games.IndexOf(_wsGame));
        game.SelectionChanged += (_, _) => { var v = games[Math.Max(0, game.SelectedIndex)]; if (v != _wsGame) { _wsGame = v; Build(); } };
        var right = Ui.Row(8, game, Ui.Button(I18n.T("hub.upload"), CreatorPage.PublishFromAnywhere, "primary", Icons.Upload));
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        sorts.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(sorts);

        // Итоги Мастерской — крупными цифрами.
        var stats = Ui.Row(40,
            Gx.Stat(all.Count.ToString("N0", I18n.Culture), I18n.T("hub.stat.mods")),
            Gx.Stat(all.Select(m => m.Uid).Distinct().Count().ToString("N0", I18n.Culture), I18n.T("hub.stat.authors")),
            Gx.Stat(I18n.Compact(all.Sum(m => m.Downloads)), I18n.T("hub.stat.downloads")),
            Gx.Stat(I18n.Compact(all.Sum(m => m.Likes)), I18n.T("hub.stat.likes")));
        var intro = Ui.Col(6, Gx.Title(I18n.T("v93.mk.workshop"), 22, Ui.Res("Text")), Ui.Text(I18n.T("v93.mk.workshop.text"), "muted", wrap: true));
        var head = new DockPanel();
        DockPanel.SetDock(stats, Dock.Right);
        head.Children.Add(stats);
        head.Children.Add(intro);
        col.Children.Add(Ui.Card(head, 22));
        col.Children.Add(bar);
        var list = Hub.Sort(all.Where(m => (_wsGame is null || m.Game == _wsGame) && (_query == "" || m.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) || m.Author.Contains(_query, StringComparison.OrdinalIgnoreCase))), _wsSort).ToList();
        if (list.Count == 0) col.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("hub.nothing"), "h3"), Ui.Text(I18n.T("hub.nothing.text"), "small muted", wrap: true)), 22));
        if (list.Count > 0) col.Children.Add(StoreKit.Tiles(list.Select(m => MarketTiles.Workshop(m, 1)), 260));
        return col;
    }

    // ---------------------------------------------------------------- очередь открытий

    static List<string>? _queue;
    static int _queueAt;

    /// <summary>
    /// «Очередь открытий», как в Steam: лоты по одному, крупно. «В желаемое», «Не интересно»
    /// (больше не покажем) или «Дальше». Очередь — из рекомендаций «для вас».
    /// </summary>
    Control Queue()
    {
        var all = Active.ToList();
        if (_queue is null || _queue.Count == 0 || _queue.Any(id => all.All(l => l.Id != id)))
        {
            _queue = MarketLocal.ForYou(all.Where(l => !MarketLocal.Ignored(l.Id) && !l.Mine && !Market.Owned.Contains(l.Id)), MyGames()).Take(10).Select(l => l.Id).ToList();
            _queueAt = 0;
        }
        var col = Ui.Col(18);
        if (_queue.Count == 0 || _queueAt >= _queue.Count)
        {
            var again = Ui.Button(I18n.T("v93.mk.queue.again"), () => { _queue = null; Build(); }, "primary", Icons.Shuffle);
            col.Children.Add(Ui.Card(Ui.Col(12, Ui.Icon(Icons.Trophy, 34, Ui.Res("Brand2")), Gx.Title(I18n.T("v93.mk.queue.done"), 24, Ui.Res("Text")), Ui.Text(I18n.T("v93.mk.queue.done.text"), "muted", wrap: true), again), 32));
            return col;
        }
        var l = all.First(x => x.Id == _queue[_queueAt]);
        // Полоска прогресса очереди: сегмент на каждый лот.
        var segs = new Grid { ColumnSpacing = 4, Height = 5 };
        for (var i = 0; i < _queue.Count; i++)
        {
            segs.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var s = new Border { CornerRadius = new CornerRadius(3), Background = i <= _queueAt ? Ui.Res("Brand") : Ui.Res("Surface3") };
            if (i == _queueAt) s.BoxShadow = Gx.Glow(160, 10);
            Grid.SetColumn(s, i);
            segs.Children.Add(s);
        }
        var counter = Ui.Text(I18n.T("v93.mk.queue.n", ("n", _queueAt + 1), ("of", _queue.Count)), "small muted");
        col.Children.Add(Ui.Col(8, Ui.Row(10, Gx.Eyebrow(I18n.T("v93.mk.tab.queue"), Ui.Res("Brand2")), counter), segs));

        var art = MarketTiles.Art(l, 1600);
        var title = Gx.Title(l.Title, 34, Brushes.White);
        var why = MyGames().Contains(l.Game) ? I18n.T("v93.mk.queue.why.game", ("game", MarketTiles.GameName(l.Game)))
            : MarketLocal.Follows(l.Uid) ? I18n.T("v93.mk.queue.why.author", ("name", l.Author))
            : I18n.T("v93.mk.queue.why.popular");
        var words = Ui.Col(12, StoreKit.Pill(why), title,
            new TextBlock { Text = l.Summary, FontSize = 15, Foreground = Ui.Hex("#D9DBE3"), TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left },
            Ui.Row(10, Gx.Price(MarketViews.PriceText(l), l.Free), StoreKit.Pill(I18n.T("mk.kind." + l.Kind)), StoreKit.Pill(I18n.T("mod.by", ("author", l.Author)))));
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(36, 0, 36, 32);
        var stage = new Border { Classes = { "store-tile", "hero" }, Height = 440, Child = new Panel { Children = { art, new Border { Background = Gx.ShadeUp(0.25, 240) }, new Border { Background = Gx.ShadeLeft(0.7, 160) }, Gx.Haze(true), words } } };
        StoreKit.OnClick(stage, () => MarketTiles.Open(l));
        col.Children.Add(stage);
        if (Animate.On) Animate.From(stage, "translateX(60px) scale(0.97)", 420, 0, new Avalonia.Animation.Easings.BackEaseOut());

        void Next() { _queueAt++; Build(); }
        var wished = MarketLocal.Wished(l.Id);
        var buttons = Ui.Row(10,
            Ui.Button(I18n.T(wished ? "v93.mk.wish.remove" : "v93.mk.wish.add"), () => { MarketLocal.ToggleWish(l.Id); Next(); }, wished ? "" : "primary", Icons.Heart),
            Ui.Button(I18n.T("v93.mk.queue.skip"), () => { MarketLocal.Ignore(l.Id); Next(); }, "", Icons.EyeOff),
            Ui.Button(I18n.T("v93.mk.open"), () => MarketTiles.Open(l), "", Icons.Eye));
        var next = Ui.Button(I18n.T("v93.mk.queue.next"), Next, "primary", Icons.ChevronRight);
        next.Padding = new Thickness(22, 10);
        var bar = new DockPanel();
        DockPanel.SetDock(next, Dock.Right);
        bar.Children.Add(next);
        bar.Children.Add(buttons);
        col.Children.Add(bar);
        return col;
    }

    // ---------------------------------------------------------------- авторы

    bool _onlyFollowed;

    Control Authors()
    {
        var all = MarketLocal.Authors(Active, MarketData.Mods);
        var list = all.Where(a => !_onlyFollowed || MarketLocal.Follows(a.Uid))
            .Where(a => _query == "" || a.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)).ToList();
        var chips = Ui.Row(6,
            Ui.Button(I18n.T("v93.mk.authors.all", ("n", all.Count)), () => { _onlyFollowed = false; Build(); }, _onlyFollowed ? "chip" : "chip active"),
            Ui.Button(I18n.T("v93.mk.authors.followed", ("n", all.Count(a => MarketLocal.Follows(a.Uid)))), () => { _onlyFollowed = true; Build(); }, _onlyFollowed ? "chip active" : "chip", Icons.Heart));
        var col = Ui.Col(18, chips);
        if (list.Count == 0) { col.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("v93.mk.authors.none"), "h3"), Ui.Text(I18n.T("v93.mk.authors.none.text"), "muted", wrap: true)), 24)); return col; }
        var grid = new UniformGrid { Columns = 4 };
        foreach (var a in list) { var c = AuthorCard(a); c.Margin = new Thickness(0, 0, StoreKit.Gap, StoreKit.Gap); grid.Children.Add(c); }
        col.Children.Add(grid);
        return col;
    }

    // ---------------------------------------------------------------- желаемое

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
        var col = Ui.Col(16);
        var total = items.Where(x => !Market.Owned.Contains(x.Item.Id)).Sum(x => x.Item.Price);
        var sorts = Ui.Row(6);
        foreach (var (id, key) in new[] { ("added", "v93.mk.wish.sort.added"), ("price", "v93.mk.wish.sort.price"), ("name", "v93.mk.wish.sort.name") })
        {
            var s = id;
            sorts.Children.Add(Ui.Button(I18n.T(key), () => { _wishSort = s; Build(); }, _wishSort == id ? "chip active" : "chip"));
        }
        var summary = Ui.Row(28, Gx.Stat(items.Count.ToString(), I18n.T("v93.mk.wish.count"), null, 22), Gx.Stat(Market.Money(total), I18n.T("v93.mk.wish.total"), Ui.Res("Brand2"), 22));
        var head = new DockPanel();
        DockPanel.SetDock(summary, Dock.Right);
        head.Children.Add(summary);
        sorts.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(sorts);
        col.Children.Add(head);
        if (items.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Icon(Icons.Heart, 30, Ui.Res("Bad")), Ui.Text(I18n.T("v93.mk.wish.empty"), "h3"), Ui.Text(I18n.T("v93.mk.wish.empty.text"), "muted", wrap: true),
                Ui.Row(10, Ui.Button(I18n.T("v93.mk.tab.queue"), () => Go("queue"), "primary", Icons.Shuffle), Ui.Button(I18n.T("v93.mk.tab.store"), () => Go("store"), "", Icons.Home))), 28));
            return col;
        }
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

    // ---------------------------------------------------------------- библиотека покупок

    static List<Purchase>? _purchases;
    static string? _purchasesError;
    static bool _purchasesLoading;

    /// <summary>Для снимков экрана: покупки без сети.</summary>
    public static void DemoPurchases(List<Purchase> list) => _purchases = list;

    Control Library()
    {
        var col = Ui.Col(16);
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
        var spent = _purchases.Sum(p => p.Price);
        col.Children.Add(Ui.Row(36, Gx.Stat(_purchases.Count.ToString(), I18n.T("v93.mk.library.items")), Gx.Stat(Market.Money(spent), I18n.T("v93.mk.library.spent"))));
        if (_purchases.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v93.mk.library.empty"), "h3"), Ui.Text(I18n.T("v93.mk.library.empty.text"), "muted", wrap: true),
                Ui.Button(I18n.T("v93.mk.tab.store"), () => Go("store"), "primary", Icons.Home)), 24));
            return col;
        }
        foreach (var p in _purchases)
        {
            var listing = MarketData.All.FirstOrDefault(l => l.Id == p.ListingId);
            Control art = listing is not null ? MarketTiles.Art(listing, 384, hero: false) : Gx.Gradient(null);
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
            var words = Ui.Col(3, Ui.Text(p.Title, "h3"),
                Ui.Text($"{I18n.T("mk.kind." + p.Kind)} · {p.Seller} · {(p.Price == 0 ? I18n.T("mk.free") : Market.Money(p.Price))} · {Ui.Ago(p.Created)}", "small muted"));
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
