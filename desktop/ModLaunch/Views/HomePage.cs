using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Главная: все игры плитками и короткий рассказ о том, что умеет программа.</summary>
public sealed class HomePage : Page
{
    public override string Title => I18n.T("nav.menu");

    int _hero;
    IDisposable? _timer;

    public HomePage()
    {
        DetachedFromVisualTree += (_, _) => _timer?.Dispose();
        AttachedToVisualTree += (_, _) =>
        {
            if (Program.Screenshot || !Look.Animations) return;
            _timer = DispatcherTimer.Run(() => { _hero = (_hero + 1) % 3; Build(); return true; }, TimeSpan.FromSeconds(8));
        };
    }

    public override void Search(string text)
    {
        var game = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found);
        if (game is null) { MainWindow.Current?.Toast(I18n.T("search.noGames")); return; }
        MainWindow.Current?.Navigate(() => new GamePage(game.Def.Id, "catalog", text));
    }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 22, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1280 };

        var header = new DockPanel();
        var all = Ui.Button(I18n.T("home.allGames") + "  ›", () => MainWindow.Current?.Navigate(() => new SettingsPage("games")), "ghost");
        all.Foreground = Ui.Res("Brand2");
        DockPanel.SetDock(all, Dock.Right);
        header.Children.Add(all);
        header.Children.Add(Ui.Text(I18n.T("home.yourGames"), "h2"));
        content.Children.Add(header);

        // «Продолжить игру» — недавно запущенные (как «Jump back in» в Modrinth App).
        var recent = AppState.Games
            .Select(g => (Game: g, Played: Features.PlayTime.Get(g.Def.Id)))
            .Where(x => x.Game.Status == Detect.Found && x.Played.LastPlayed is not null)
            .OrderByDescending(x => x.Played.LastPlayed).Take(3).ToList();
        if (recent.Count > 0 && Settings.Data.Bool("homeContinue", true)) content.Children.Add(Continue(recent));

        var grid = new UniformGrid { Columns = 4 };
        var foundOnly = Settings.Data.Bool("homeFoundOnly");
        var hidden = Settings.Data.Arr("hiddenGames").Select(x => x?.ToString()).ToHashSet();
        foreach (var g in MainWindow.OrderedGames().Where(g => !hidden.Contains(g.Def.Id) && (!foundOnly || g.Status == Detect.Found))) grid.Children.Add(Tile(g));
        grid.Children.Add(AddTile());
        content.Children.Add(grid);

        if (Settings.Data.Bool("homeHero", true)) content.Children.Add(Hero());

        var favorites = Favorites.All().Where(f => AppState.Games.Any(g => g.Def.Id == f.GameId && g.Status == Detect.Found)).Take(20).ToList();
        if (favorites.Count > 0 && Settings.Data.Bool("homeFavorites", true)) content.Children.Add(Shelf(I18n.T("home.favorites"), I18n.T("home.favorites.text"), favorites));

        var first = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found && g.LoaderInstalled) ?? AppState.Games.FirstOrDefault(g => g.Status == Detect.Found);
        if (first is not null && Settings.Data.Bool("homePopular", true))
        {
            if (_popularFor != first.Def.Id) { _popularFor = first.Def.Id; _popular = null; _ = LoadPopular(first); }
            if (_popular is { Count: > 0 }) content.Children.Add(Shelf(I18n.T("home.popular", ("game", first.Def.Name)), null, _popular.Select(m => (first.Def.Id, m)).ToList()));
        }

        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Continue(List<(GameState Game, Features.Played Played)> recent)
    {
        var row = new UniformGrid { Columns = 3 };
        foreach (var (g, played) in recent)
        {
            var running = Features.Launcher.IsRunning(g.Def.Id);
            var art = Images.Game(g.Def, 160);
            var thumb = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Ui.Hex(g.Def.Accent), Child = art is null ? null : new Image { Source = art, Stretch = Stretch.UniformToFill } };
            var info = Ui.Col(3, Ui.Text(g.Def.Name, "h3"),
                Ui.Text(running ? I18n.T("time.running") : I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed))), "small", color: running ? Ui.Res("Good") : Ui.Res("Muted")),
                Ui.Text(I18n.T("time.total", ("time", Features.PlayTime.Format(played.TotalMs))), "small muted"));
            info.VerticalAlignment = VerticalAlignment.Center;
            var gs = g;
            var play = running
                ? Ui.Button("", () => Features.Launcher.Stop(gs.Def.Id), "icon", Icons.Stop, I18n.T("v4.stop"))
                : Ui.Button("", () => Actions.Play(gs), "icon primary", Icons.Play, I18n.T("games.play"));
            play.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            grid.Children.Add(thumb);
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);
            Grid.SetColumn(play, 2);
            grid.Children.Add(play);
            var card = new Button { Classes = { "tile" }, Padding = new Thickness(12), Margin = new Thickness(0, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Stretch, Content = grid };
            card.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(gs.Def.Id));
            row.Children.Add(card);
        }
        return Ui.Col(12, Ui.Col(2, Ui.Text(I18n.T("v4.continue"), "h2"), Ui.Text(I18n.T("v4.continue.text"), "small muted")), row);
    }

    string? _popularFor;
    List<Sources.ModInfo>? _popular;

    async Task LoadPopular(GameState g)
    {
        try
        {
            var page = Program.Demo ? Demo.Catalog(g.Def, new Sources.Query()) : await Sources.Catalog.Browse(g.Def, new Sources.Query());
            _popular = page.Mods.Take(12).ToList();
        }
        catch { _popular = []; }
        Build();
    }

    /// <summary>Полка модов: карточки в ряд с прокруткой.</summary>
    static Control Shelf(string title, string? hint, List<(string GameId, Sources.ModInfo Mod)> mods)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var (gameId, mod) in mods)
        {
            var card = new Button
            {
                Classes = { "tile" },
                Width = 180,
                Content = Ui.Col(8,
                    Ui.Thumb(mod.Icon, mod.Name, 156, 12, 320),
                    Ui.Text(mod.Name, "h3"),
                    Ui.Text(mod.Author == "" ? AppState.Game(gameId).Def.ShortName : mod.Author, "small muted")),
                Padding = new Thickness(12),
            };
            var id = gameId;
            var m = mod;
            card.Click += (_, _) => MainWindow.Current?.Navigate(() => new ModPage(id, m));
            row.Children.Add(card);
        }
        var head = Ui.Col(2, Ui.Text(title, "h2"));
        if (hint is not null) head.Children.Add(Ui.Text(hint, "small muted"));
        return Ui.Col(12, head, new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    static string StatusText(GameState g) => g.Status switch
    {
        Detect.Searching => I18n.T("games.searching"),
        Detect.Found when !g.LoaderInstalled => I18n.T("home.loaderNeeded", ("loader", g.Def.LoaderName)),
        Detect.Found when g.ModCount > 0 => I18n.T("aside.mods." + I18n.Plural(g.ModCount, "one", "few", "many"), ("n", g.ModCount)),
        Detect.Found => I18n.T("games.loaderReady", ("loader", g.Def.LoaderName)),
        Detect.NotFound => I18n.T("games.notDetected"),
        _ => I18n.T("games.notSearched"),
    };

    /// <summary>Плитка «+»: найти на компьютере другие игры и добавить их.</summary>
    static Control AddTile()
    {
        var b = new Button
        {
            Classes = { "tile" }, Height = 136, Margin = new Thickness(0, 0, 14, 14), HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent, BorderBrush = Ui.Res("Line"),
            Content = Ui.Col(8,
                new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Plus, 20, Ui.Res("Brand2")), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = I18n.T("add.tile"), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = I18n.T("add.tile.text"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Foreground = Ui.Res("Muted") }),
        };
        if (b.Content is Control c) { c.VerticalAlignment = VerticalAlignment.Center; c.HorizontalAlignment = HorizontalAlignment.Center; }
        b.Click += (_, _) => MainWindow.Current?.Navigate(() => new AddGamePage());
        return b;
    }

    static Control Tile(GameState g)
    {
        var art = Images.Game(g.Def, 520);
        Control face = art is not null
            ? new Image { Source = art, Stretch = Stretch.UniformToFill }
            : new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(g.Def.Accent), 0), new GradientStop(Color.Parse("#2A1B0A"), 1) },
                },
            };

        var shade = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.2, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#000F1116"), 0), new GradientStop(Color.Parse("#E60F1116"), 1) },
            },
        };

        var dot = Ui.Dot(g.Status == Detect.Found ? (g.LoaderInstalled ? Ui.Res("Good") : Ui.Res("Warn")) : Ui.Res("Faint"));
        var info = Ui.Col(4,
            new TextBlock { Text = g.Def.Name, FontSize = 19, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxLines = 2 },
            Ui.Row(8, dot, Ui.Text(StatusText(g), "small", color: Ui.Hex("#C9CFDB"))));
        info.VerticalAlignment = VerticalAlignment.Bottom;
        info.Margin = new Thickness(14, 0, 14, 14);

        var tile = new Button
        {
            Classes = { "tile" },
            Height = 136,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 14, 14),
            Content = new Panel { Children = { face, shade, info } },
        };
        var id = g.Def.Id;
        tile.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(id));
        return tile;
    }

    Control Hero()
    {
        string[] banners = ["banner-hero.jpg", "banner-deps.jpg", "banner-broken.jpg"];
        string[] keys = ["home.hero", "home.hero2", "home.hero3"];
        var i = _hero % 3;

        var text = Ui.Col(12,
            new TextBlock { Text = I18n.T(keys[i] + ".title"), FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = I18n.T(keys[i] + ".text"), FontSize = 15, Foreground = Ui.Hex("#D5DAE5"), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 });
        var dots = Ui.Row(6, Enumerable.Range(0, 3).Select(n =>
        {
            var d = new Border { Width = n == i ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Background = n == i ? Ui.Res("Brand2") : Ui.Hex("#55FFFFFF"), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            var target = n;
            d.PointerPressed += (_, _) => { _hero = target; Build(); };
            return (Control)d;
        }).ToArray());
        text.Children.Add(dots);
        foreach (var c in text.Children) c.HorizontalAlignment = HorizontalAlignment.Left;
        text.Margin = new Thickness(36, 32);
        text.VerticalAlignment = VerticalAlignment.Center;

        return new Border
        {
            Height = 260,
            CornerRadius = new CornerRadius(20),
            ClipToBounds = true,
            BorderBrush = Ui.Res("Line"),
            BorderThickness = new Thickness(1),
            Child = new Panel
            {
                Children =
                {
                    new Image { Source = Images.Asset(banners[i], 1600), Stretch = Stretch.UniformToFill },
                    new Border
                    {
                        Background = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                            GradientStops = { new GradientStop(Color.Parse("#F20F1116"), 0), new GradientStop(Color.Parse("#990F1116"), 0.55), new GradientStop(Color.Parse("#000F1116"), 1) },
                        },
                    },
                    text,
                },
            },
        };
    }
}
