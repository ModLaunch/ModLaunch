using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: «Витрина» — главная страница магазина, как в Steam. Сверху — карусель «Выбор ModLaunch»
/// (популярные моды для ваших игр: большая карточка и превью следующих), под ней — разделы магазина плитками,
/// полки «Популярное в …» по каждой вашей игре, ИИ-сборки, маркет создателей, моды ModLaunch Hub
/// и игры с поддержкой модов, которых у вас ещё нет. Каталоги грузятся в фоне; пока грузятся — заготовки.
/// </summary>
public sealed class StorePage : Page
{
    /// <summary>Популярные моды каждой игры — общие на сеанс (витрину открывают часто, каталоги не дёргаем каждый раз).</summary>
    static readonly Dictionary<string, List<ModInfo>> Popular = [];
    static DateTime _loadedAt = DateTime.MinValue;
    static Task? _loading;

    int _hero;
    readonly DispatcherTimer _rotate = new() { Interval = TimeSpan.FromSeconds(8) };
    Panel? _heroHost;

    public override string Title => I18n.T("v20.store.front");
    public override string SearchHint => I18n.T("v20.store.searchHint");
    public override void Search(string text) { if (text.Trim() != "") MainWindow.Current?.Navigate(() => new SearchPage(text.Trim())); }

    public StorePage()
    {
        _rotate.Tick += (_, _) => { if (_heroHost?.IsPointerOver != true) ShowHero(_hero + 1); };
        AttachedToVisualTree += (_, _) =>
        {
            MarketData.Changed += OnMarket;
            if (!Program.Screenshot) _rotate.Start();
        };
        DetachedFromVisualTree += (_, _) => { _rotate.Stop(); MarketData.Changed -= OnMarket; };
        _ = Load();
        _ = MarketData.Load();
    }

    void OnMarket() => Dispatcher.UIThread.Post(Build);

    /// <summary>Игры, для которых есть каталог (Minecraft — со своим каталогом на своей странице).</summary>
    static List<GameState> Shelved() => AppState.Games
        .Where(g => g.Status == Detect.Found && g.Def.HasExternalCatalog && !g.Def.IsMinecraft && !Features.GameCollections.IsHidden(g.Def.Id))
        .OrderByDescending(g => Features.NewGames.Recent(g))
        .Take(6).ToList();

    async Task Load()
    {
        if (DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(20) && Shelved().All(g => Popular.ContainsKey(g.Def.Id))) return;
        _loading ??= Fetch();
        try { await _loading; } finally { _loading = null; }
        Dispatcher.UIThread.Post(Build);
    }

    static async Task Fetch()
    {
        var tasks = Shelved().Select(async g =>
        {
            try
            {
                var page = Program.Demo ? Demo.Catalog(g.Def, new Query()) : await Catalog.Browse(g.Def, new Query());
                lock (Popular) Popular[g.Def.Id] = page.Mods.Where(m => !Catalog.IsHidden(g.Def, m)).Take(12).ToList();
            }
            catch { }
        }).ToList();
        await Task.WhenAll(tasks);
        _loadedAt = DateTime.UtcNow;
    }

    /// <summary>«Выбор ModLaunch»: по два самых популярных мода каждой игры, игры чередуются.</summary>
    static List<(GameState Game, ModInfo Mod)> Featured()
    {
        var list = new List<(GameState, ModInfo)>();
        var games = Shelved();
        for (var i = 0; i < 2; i++)
            foreach (var g in games)
                if (Popular.TryGetValue(g.Def.Id, out var mods) && mods.Count > i) list.Add((g, mods[i]));
        return list.Take(8).ToList();
    }

    public override void Build()
    {
        var col = StoreKit.Column(34, 22);
        col.Children.Add(Hero());
        col.Children.Add(Sections());

        var games = Shelved();
        if (games.Count == 0) col.Children.Add(NoGames());
        foreach (var g in games.Take(4))
        {
            var id = g.Def.Id;
            col.Children.Add(Ui.Col(14,
                StoreKit.Header(I18n.T("v20.store.popularIn", ("game", g.Def.Name)), () => MainWindow.Current?.Navigate(() => new GamePage(id, "catalog"))),
                Popular.TryGetValue(id, out var mods)
                    ? mods.Count > 0 ? StoreKit.Shelf(mods.Take(10).Select(m => StoreKit.ModCard(g, m, 268))) : Ui.Text(I18n.T("v20.store.emptyShelf"), "muted small")
                    : StoreKit.Shelf(Enumerable.Range(0, 5).Select(_ => StoreKit.Placeholder(268, 214)))));
        }

        col.Children.Add(AiBanner());

        // Маркет создателей и моды ModLaunch Hub — те же данные, что на страницах рынка.
        var listings = MarketData.All.Where(l => l.Status == "active").OrderByDescending(l => l.Sales).Take(10).ToList();
        if (listings.Count > 0)
            col.Children.Add(Ui.Col(14,
                StoreKit.Header(I18n.T("v20.store.market"), () => MainWindow.Current?.Navigate(() => new MarketPage("store"))),
                StoreKit.Shelf(listings.Select(l => MarketTiles.Card(l, 252)))));
        var hub = MarketData.Mods.OrderByDescending(h => h.Downloads).Take(10).ToList();
        if (hub.Count > 0)
            col.Children.Add(Ui.Col(14,
                StoreKit.Header(I18n.T("v20.store.hub"), () => MainWindow.Current?.Navigate(() => new MarketPage("workshop"))),
                StoreKit.Shelf(hub.Select(h => MarketTiles.WorkshopCard(h, 252)))));

        if (Ads.Enabled)
            col.Children.Add(Ui.Col(10,
                new TextBlock { Text = I18n.T("v20.store.sponsored").ToUpper(I18n.Culture), FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1, Foreground = Ui.Res("Faint") },
                AdSlot.WideBanner()));

        var missing = AppState.Games.Where(g => g.Status != Detect.Found && !g.Def.Custom && !Features.GameCollections.IsHidden(g.Def.Id)).Take(12).ToList();
        if (missing.Count > 0)
            col.Children.Add(Ui.Col(14,
                StoreKit.Header(I18n.T("v20.store.moddable"), () => MainWindow.Current?.Navigate(() => new AddGamePage()),
                    count: Games.CustomGames.MoreSupported > 0 ? "+" + I18n.Compact(Games.CustomGames.MoreSupported) : null),
                StoreKit.Shelf(missing.Select(g => StoreKit.Poster(g, 150)))));

        var offset = Content is ScrollViewer old ? old.Offset : default;
        var scroll = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Content = scroll;
        if (offset != default) Dispatcher.UIThread.Post(() => scroll.Offset = offset, DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- карусель «Выбор ModLaunch»

    Control Hero()
    {
        _heroHost = new Panel();
        var host = StoreKit.Scaled(new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = _heroHost }, 360, 1200, 1.35);
        ShowHero(_hero, animate: false);
        return host;
    }

    void ShowHero(int index, bool animate = true)
    {
        if (_heroHost is null) return;
        var items = Featured();
        _heroHost.Children.Clear();
        if (items.Count == 0)
        {
            _heroHost.Children.Add(HeroWelcome());
            return;
        }
        _hero = (index % items.Count + items.Count) % items.Count;
        var (g, m) = items[_hero];

        var art = Ui.GameImage(g.Def, 1600, art: Images.Art.Hero);
        var shade = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(242, 8, 9, 12), 0), new GradientStop(Color.FromArgb(200, 8, 9, 12), 0.45), new GradientStop(Color.FromArgb(40, 8, 9, 12), 1) },
            },
        };
        var bottom = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 8, 9, 12), 0.55), new GradientStop(Color.FromArgb(170, 8, 9, 12), 1) },
            },
        };

        var icon = new Border
        {
            Width = 76, Height = 76, CornerRadius = new CornerRadius(16), ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 14 34 0 #A0000000"), Child = Ui.Thumb(m.Icon, m.Name, 76, 0, 200),
        };
        var eyebrow = Ui.Row(8,
            new Border { Background = Ui.Res("Brand"), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3), Child = new TextBlock { Text = I18n.T("v20.store.pick").ToUpper(I18n.Culture), FontSize = 10.5, FontWeight = FontWeight.Bold, LetterSpacing = 0.8, Foreground = Brushes.White } },
            new TextBlock { Text = g.Def.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Ui.Hex("#D5D8E2"), VerticalAlignment = VerticalAlignment.Center });
        var title = new TextBlock
        {
            Text = m.Name, FontFamily = Look.Display, FontSize = 34, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var text = new TextBlock
        {
            Text = m.Description, FontSize = 14, Foreground = Ui.Hex("#C9CCD6"), TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var facts = new List<string>();
        if (m.Author != "") facts.Add(I18n.T("v20.store.by", ("author", m.Author)));
        if (m.Downloads > 0) facts.Add("↓ " + I18n.Compact(m.Downloads));
        if (m.UpdatedAt is { } updated) facts.Add(I18n.T("v20.store.updated", ("ago", Ui.Ago(updated).Trim())));
        var meta = new TextBlock { Text = string.Join("  ·  ", facts), FontSize = 12.5, Foreground = Ui.Hex("#A9ADBA") };

        var gs = g; var mm = m; var id = g.Def.Id;
        var installed = Actions.IsInstalled(g, m.Id);
        var install = installed
            ? Ui.Button(I18n.T("mod.installed"), () => MainWindow.Current?.Navigate(() => new GamePage(id, "installed")), "hero-ghost", Icons.Check)
            : Ui.Button(I18n.T("mod.install"), () => _ = Actions.Install(gs, mm), "primary", Icons.Download);
        install.Padding = new Thickness(22, 11);
        install.FontSize = 14;
        install.FontWeight = FontWeight.SemiBold;
        var more = Ui.Button(I18n.T("v20.store.details"), () => MainWindow.Current?.Navigate(() => new ModPage(id, mm)), "hero-ghost", Icons.Info);
        more.Padding = new Thickness(18, 11);
        more.FontSize = 14;

        var words = Ui.Col(12, eyebrow, Ui.Row(16, icon, Ui.Col(6, title, meta)), text, Ui.Row(10, install, more));
        ((StackPanel)words.Children[1]).Children[1].VerticalAlignment = VerticalAlignment.Center;
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(36, 0, 36, 34);

        // Справа — следующие карточки карусели (как скриншоты у «Рекомендуемого» в Steam).
        var thumbs = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 22, 0) };
        for (var k = 1; k <= Math.Min(4, items.Count - 1); k++)
        {
            var at = (_hero + k) % items.Count;
            var (tg, tm) = items[at];
            var thumb = new Border
            {
                Classes = { "hero-thumb" }, Width = 210, Height = 64, Background = new SolidColorBrush(Color.FromArgb(150, 12, 13, 18)),
                Child = Ui.Row(10, new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.Thumb(tm.Icon, tm.Name, 46, 0, 120) },
                    Ui.Col(1,
                        new TextBlock { Text = tm.Name, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 136 },
                        new TextBlock { Text = tg.Def.ShortName, FontSize = 11, Foreground = Ui.Hex("#A9ADBA"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 136 })),
                Padding = new Thickness(9),
            };
            ((StackPanel)thumb.Child).Children[1].VerticalAlignment = VerticalAlignment.Center;
            var jump = at;
            StoreKit.OnClick(thumb, () => ShowHero(jump));
            thumbs.Children.Add(thumb);
        }
        thumbs.IsVisible = Bounds.Width <= 0 || Bounds.Width > 1000;

        var dots = Ui.Row(6);
        for (var k = 0; k < items.Count; k++)
        {
            var jump = k;
            var dot = new Border { Width = k == _hero ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Background = k == _hero ? Brushes.White : new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            StoreKit.OnClick(dot, () => ShowHero(jump));
            dots.Children.Add(dot);
        }
        dots.HorizontalAlignment = HorizontalAlignment.Right;
        dots.VerticalAlignment = VerticalAlignment.Bottom;
        dots.Margin = new Thickness(0, 0, 26, 22);

        var prev = Arrow(Icons.ChevronLeft, () => ShowHero(_hero - 1));
        prev.HorizontalAlignment = HorizontalAlignment.Left;
        prev.Margin = new Thickness(10, 0, 0, 0);

        var layers = new Panel { Children = { art, shade, bottom, words, thumbs, dots, prev } };
        _heroHost.Children.Add(layers);
        if (animate) Animate.From(words, "translateX(-18px)", 420);
    }

    static Button Arrow(string icon, Action go)
    {
        var b = new Button
        {
            Classes = { "icon", "hero-ghost" }, Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Content = Ui.Icon(icon, 16, Brushes.White),
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => go();
        return b;
    }

    /// <summary>Пока каталоги грузятся (или игр с каталогом нет) — приветствие магазина на фирменном градиенте.</summary>
    static Control HeroWelcome()
    {
        var brand = Ui.Res("Brand") is ISolidColorBrush b ? b.Color : Color.Parse("#7C5CFF");
        var bg = StoreKit.Gradient(Color.FromArgb(255, (byte)(brand.R / 3), (byte)(brand.G / 3), (byte)(brand.B / 2.2)), Color.Parse("#0B0C12"));
        var words = Ui.Col(12,
            new TextBlock { Text = I18n.T("v20.store.welcome"), FontFamily = Look.Display, FontSize = 34, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left },
            new TextBlock { Text = I18n.T("v20.store.welcome.text"), FontSize = 14.5, Foreground = Ui.Hex("#C9CCD6"), TextWrapping = TextWrapping.Wrap, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left },
            Ui.Row(10,
                Ui.Button(I18n.T("v20.store.search"), () => MainWindow.Current?.Navigate(() => new SearchPage()), "primary", Icons.Search),
                Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "hero-ghost", Icons.Plus)));
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(40, 0, 40, 0);
        var glyph = new Border { Opacity = 0.18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 50, 0), Child = Ui.Icon(Icons.Bag, 200, Brushes.White) };
        return new Panel { Children = { bg, glyph, words } };
    }

    // ---------------------------------------------------------------- разделы магазина плитками

    Control Sections()
    {
        Border Cat(string title, string text, string icon, string a, string b, Action open)
        {
            var words = Ui.Col(2,
                new TextBlock { Text = title, FontSize = 15.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = text, FontSize = 12, Foreground = Ui.Hex("#E2E4EC"), Opacity = 0.85, TextTrimming = TextTrimming.CharacterEllipsis });
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(16, 0, 16, 14);
            var glyph = new Border { Opacity = 0.3, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 14, 0), Child = Ui.Icon(icon, 40, Brushes.White) };
            var tile = new Border { Classes = { "store-cat" }, Height = 112, Child = new Panel { Children = { StoreKit.Gradient(Color.Parse(a), Color.Parse(b)), glyph, words } } };
            StoreKit.OnClick(tile, open);
            return tile;
        }
        var w = MainWindow.Current;
        var tiles = new[]
        {
            Cat(I18n.T("v20.store.search"), I18n.T("v20.cat.search"), Icons.Search, "#5B3FE0", "#24195E", () => w?.Navigate(() => new SearchPage())),
            Cat(I18n.T("v98.nav.ai"), I18n.T("v20.cat.ai"), Icons.Sparkles, "#C2389A", "#4A1640", () => w?.Navigate(() => new AiBuildsPage())),
            Cat(I18n.T("v20.store.market"), I18n.T("v20.cat.market"), Icons.Bag, "#E07A2E", "#5A2A10", () => w?.Navigate(() => new MarketPage("store"))),
            Cat(I18n.T("v20.store.hub"), I18n.T("v20.cat.hub"), Icons.Globe, "#1E9E74", "#0D3A2C", () => w?.Navigate(() => new MarketPage("workshop"))),
            Cat("Minecraft", I18n.T("v20.cat.minecraft"), Icons.Cube, "#3F8F3A", "#173416", () => w?.Navigate(() => new MinecraftPage("catalog"))),
            Cat(I18n.T("v20.store.steam"), I18n.T("v20.cat.steam"), Icons.Shelf, "#2F6FD6", "#11264D", () => w?.Navigate(() => new SteamGamesPage())),
        };
        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, -StoreKit.Gap, 0) };
        foreach (var t in tiles) { t.Margin = new Thickness(0, 0, StoreKit.Gap, StoreKit.Gap); grid.Children.Add(t); }
        grid.SizeChanged += (_, e) =>
        {
            var cols = e.NewSize.Width >= 1100 ? 6 : e.NewSize.Width >= 640 ? 3 : 2;
            if (grid.Columns != cols) grid.Columns = cols;
        };
        return grid;
    }

    // ---------------------------------------------------------------- баннеры

    static Control AiBanner()
    {
        var bg = StoreKit.Gradient(Color.Parse("#3A1F8F"), Color.Parse("#B0306E"));
        var words = Ui.Col(8,
            new TextBlock { Text = I18n.T("v20.store.ai.eyebrow").ToUpper(I18n.Culture), FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1, Foreground = Ui.Hex("#F3D9FF") },
            new TextBlock { Text = I18n.T("v20.store.ai.title"), FontFamily = Look.Display, FontSize = 24, FontWeight = FontWeight.Bold, LetterSpacing = -0.5, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = I18n.T("v20.store.ai.text"), FontSize = 13.5, Foreground = Ui.Hex("#EADCF5"), TextWrapping = TextWrapping.Wrap, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left });
        words.VerticalAlignment = VerticalAlignment.Center;
        var go = Ui.Button(I18n.T("v20.store.ai.go"), () => MainWindow.Current?.Navigate(() => new AiBuildsPage()), "hero-light", Icons.Sparkles);
        go.Padding = new Thickness(20, 11);
        go.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(32, 24, 32, 24) };
        grid.Children.Add(words);
        Grid.SetColumn(go, 1);
        grid.Children.Add(go);
        var glyph = new Border { Opacity = 0.14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 210, 0), Child = Ui.Icon(Icons.Sparkles, 150, Brushes.White) };
        var tile = new Border { Classes = { "store-cat" }, Child = new Panel { Children = { bg, glyph, grid } } };
        StoreKit.OnClick(tile, () => MainWindow.Current?.Navigate(() => new AiBuildsPage()));
        return tile;
    }

    static Control NoGames()
    {
        var icon = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Gamepad, 24, Ui.Res("Brand2")), VerticalAlignment = VerticalAlignment.Top };
        var words = Ui.Col(6, Ui.Text(I18n.T("v20.store.nogames"), "h3"), Ui.Text(I18n.T("v20.store.nogames.text"), "muted", wrap: true),
            Ui.Row(10, Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus)));
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        icon.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(icon);
        row.Children.Add(words);
        return Ui.Card(row, 22);
    }
}
