using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Главная 8.0 — общее у Microsoft Store, App Store, Modrinth и Roblox, но не копия:
///   • дата и приветствие, как «Сегодня» в App Store;
///   • бенто: большая карточка «Выбор редакции», «Продолжить» и две малые плитки (Microsoft Store);
///   • ваши игры обложками (библиотека Steam);
///   • топ-10 с крупными цифрами и полки «Популярное в …» плитками со стрелками (Roblox, Microsoft Store).
/// </summary>
public sealed class HomePage : Page
{
    public override string Title => I18n.T("nav.menu");
    public override Control? Aside() => Views.Aside.Home();

    public override void Search(string text)
    {
        var game = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found);
        if (game is null) { MainWindow.Current?.Toast(I18n.T("search.noGames")); return; }
        MainWindow.Current?.Navigate(() => new GamePage(game.Def.Id, "catalog", text));
    }

    // Карусель, полки и их данные живут весь сеанс: перерисовка главной не сбрасывает страницу полки.
    static List<(GameState Game, Sources.ModInfo Mod)>? _featured;
    Featured? _featuredView;
    static bool _featuredLoading;
    readonly Dictionary<string, Shelf> _shelves = [];

    public override void Build()
    {
        var content = new StackPanel { Spacing = 34, Margin = new Thickness(32, 24, 32, 40), MaxWidth = 1680 };
        var mine = MainWindow.OrderedGames().Where(g => g.Status == Detect.Found && !Features.GameCollections.IsHidden(g.Def.Id)).ToList();

        content.Children.Add(Greeting(mine));

        if (_featured is null) { if (!_featuredLoading) { _featuredLoading = true; Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); } }
        content.Children.Add(Bento(mine));

        // Ваши игры — только те, что есть на компьютере: обложки тянутся по ширине, лишние — за стрелками.
        var searching = AppState.Games.Any(g => g.Status == Detect.Searching);
        var covers = mine.Select(g => (Func<Control>)(() => GameCard.Cover(g, 0))).Append(() => GameCard.AddCover(0)).ToList();
        var games = ShelfFor("games", I18n.T("home.yourGames"), null, covers, () => MainWindow.Current?.Navigate(() => new LibraryPage()), 138, inset: 0);
        if (mine.Count == 0)
            content.Children.Add(Ui.Col(12, games, Ui.Text(searching ? I18n.T("games.searching") : I18n.T("home.noGames"), "muted", wrap: true)));
        else content.Children.Add(games);

        if (mine.Count > 0 && Settings.Data.Bool("homePopular", true))
        {
            // Топ-10 по загрузкам среди всех ваших игр — с цифрами, как в чартах.
            var top = mine.SelectMany(g => (Views.Aside.Popular(g) ?? []).Select(m => (Game: g, Mod: m)))
                .OrderByDescending(x => x.Mod.Downloads).Take(10).ToList();
            if (top.Count > 0)
                content.Children.Add(ShelfFor("top", I18n.T("home.top10"), I18n.T("home.top10.eyebrow"),
                    top.Select((x, i) => (Func<Control>)(() => Tiles.Ranked(i + 1, ModTile(x.Game, x.Mod, showGame: true)))).ToList(), null, 280));

            // «Популярное в …» — по полке на игру.
            foreach (var g in mine.Where(g => g.Def.HasCatalog).Take(4))
            {
                var popular = Views.Aside.Popular(g);
                if (popular is { Count: 0 }) continue;
                var gs = g;
                var items = popular is null
                    ? Enumerable.Range(0, 6).Select(_ => (Func<Control>)Tiles.Skeleton).ToList()
                    : popular.Select(m => (Func<Control>)(() => ModTile(gs, m))).ToList();
                var logo = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Bottom, Child = Ui.GameImage(g.Def, 80, art: Images.Art.Cover) };
                content.Children.Add(ShelfFor("game:" + g.Def.Id, I18n.T("home.popularIn", ("game", g.Def.ShortName)), null, items,
                    () => MainWindow.Current?.Navigate(() => new GamePage(gs.Def.Id, "catalog")), 220, logo));
            }
        }

        var favorites = Favorites.All().Where(f => AppState.Games.Any(g => g.Def.Id == f.GameId && g.Status == Detect.Found)).Take(20).ToList();
        if (favorites.Count > 0 && Settings.Data.Bool("homeFavorites", true))
            content.Children.Add(ShelfFor("favorites", I18n.T("home.favorites"), null,
                favorites.Select(f => (Func<Control>)(() => ModTile(AppState.Game(f.GameId), f.Mod))).ToList(), null, 220));

        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Control ModTile(GameState g, Sources.ModInfo m, bool showGame = false) =>
        Tiles.Mod(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id),
            () => _ = Actions.Install(g, m), () => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, m)), showGame: showGame);

    Shelf ShelfFor(string key, string title, string? eyebrow, List<Func<Control>> items, Action? more, double minWidth, Control? leading = null, double inset = 8)
    {
        if (_shelves.TryGetValue(key, out var shelf))
        {
            if (shelf.Parent is Panel old) old.Children.Remove(shelf);
            shelf.SetItems(items);
            return shelf;
        }
        return _shelves[key] = new Shelf(title, eyebrow, items, more, minWidth, leading, inset);
    }

    static Control Header(string title, string? eyebrow, Action? open)
    {
        var col = Ui.Col(2);
        if (eyebrow is not null) col.Children.Add(Ui.Text(eyebrow.ToUpperInvariant(), "eyebrow"));
        var t = Ui.Text(title, "h2");
        if (open is null) { col.Children.Add(t); return col; }
        var link = new Button { Classes = { "link" }, Padding = new Thickness(0), Foreground = Ui.Res("Text"), Content = Ui.Row(6, t, Ui.Icon(Icons.Forward, 16, Ui.Res("Muted"))) };
        link.Click += (_, _) => open();
        col.Children.Add(link);
        return col;
    }

    // ---------------------------------------------------------------- приветствие

    /// <summary>«ЧЕТВЕРГ, 1 ОКТЯБРЯ / Добрый вечер, Максим» и строка о том, что сейчас важно.</summary>
    static Control Greeting(List<GameState> mine)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(I18n.Lang == "en" ? "en-US" : "ru-RU");
        var now = DateTime.Now;
        var date = now.ToString(I18n.Lang == "en" ? "dddd, MMMM d" : "dddd, d MMMM", culture).ToUpper(culture);
        var part = now.Hour switch { >= 5 and < 12 => "morning", >= 12 and < 17 => "day", >= 17 and < 23 => "evening", _ => "night" };
        var name = Social.Account.SignedIn ? Social.Account.Get().Name : null;
        var hello = I18n.T("home.greet." + part) + (string.IsNullOrWhiteSpace(name) ? "" : ", " + name);

        var updates = Features.ModUpdates.Found.Values.Sum(v => v.Count);
        var last = mine.Select(g => (Game: g, Played: Features.PlayTime.Get(g.Def.Id))).Where(x => x.Played.LastPlayed is not null).OrderByDescending(x => x.Played.LastPlayed).FirstOrDefault();
        var line = updates > 0 ? I18n.T("home.sub.updates", ("n", updates))
            : last.Game is not null ? I18n.T("home.sub.last", ("game", last.Game.Def.Name), ("when", Ui.Ago(last.Played.LastPlayed)))
            : mine.Count > 0 ? I18n.T("home.sub.ready", ("n", mine.Count))
            : I18n.T("home.sub.empty");

        return Ui.Col(4,
            Ui.Text(date, "eyebrow"),
            Ui.Text(hello, "display"),
            Ui.Text(line, "muted"));
    }

    // ---------------------------------------------------------------- бенто

    Control Bento(List<GameState> mine)
    {
        const double height = 380;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.75*,1*"), ColumnSpacing = 14, Height = height };

        // Слева — «Выбор редакции».
        Control left;
        if (_featured is { Count: > 0 } && Settings.Data.Bool("homePopular", true))
        {
            _featuredView ??= new Featured(_featured);
            if (_featuredView.Parent is Panel old) old.Children.Remove(_featuredView);
            left = _featuredView;
        }
        else left = new Border { CornerRadius = new CornerRadius(22), Background = Ui.Res("Surface"), Child = FeaturedPlaceholder(mine) };
        grid.Children.Add(left);

        // Справа — «Продолжить» и две малые плитки.
        var right = new Grid { RowDefinitions = new RowDefinitions("1.25*,1*"), RowSpacing = 14 };
        right.Children.Add(ContinueTile(mine));
        var small = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14 };
        var installed = AppState.Games.Sum(g => g.Status == Detect.Found ? g.ModCount : 0);
        var updates = Features.ModUpdates.Found.Values.Sum(v => v.Count);
        small.Children.Add(SmallTile(Icons.Package, "#3478F6", I18n.T("mc.title"),
            updates > 0 ? I18n.T("home.tile.mods.updates", ("n", updates)) : I18n.T("home.tile.mods", ("n", installed)),
            () => MainWindow.Current?.Navigate(() => new ModsCenterPage())));
        var creator = SmallTile(Icons.Tools, "#F2994A", "Creator Hub", I18n.T("home.tile.creator"), () => MainWindow.Current?.Navigate(() => new CreatorPage()));
        Grid.SetColumn(creator, 1);
        small.Children.Add(creator);
        Grid.SetRow(small, 1);
        right.Children.Add(small);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Пока подборка грузится (или выключена) — спокойная карточка с обложками ваших игр.</summary>
    static Control FeaturedPlaceholder(List<GameState> mine)
    {
        var covers = Ui.Row(10);
        foreach (var g in mine.Take(5))
            covers.Children.Add(new Border { Width = 92, Height = 138, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.GameImage(g.Def, 200, art: Images.Art.Cover) });
        covers.HorizontalAlignment = HorizontalAlignment.Right;
        covers.VerticalAlignment = VerticalAlignment.Center;
        covers.Margin = new Thickness(0, 0, 28, 0);
        covers.Opacity = 0.9;
        var text = Ui.Col(8, Ui.Text(I18n.T("feat.eyebrow").ToUpperInvariant(), "eyebrow"), Ui.Text(I18n.T("home.feat.loading"), "h1"), Ui.Text(I18n.T("home.feat.loading.text"), "muted", wrap: true));
        text.Margin = new Thickness(32);
        text.MaxWidth = 380;
        text.HorizontalAlignment = HorizontalAlignment.Left;
        text.VerticalAlignment = VerticalAlignment.Bottom;
        return new Panel { Children = { covers, text } };
    }

    /// <summary>«Продолжить»: последняя игра на своём арте и кнопка «Играть».</summary>
    static Control ContinueTile(List<GameState> mine)
    {
        if (!Settings.Data.Bool("homeContinue", true))
            return SmallTile(Icons.Layers, "#30D158", I18n.T("lib.title"), I18n.T("home.tile.library", ("n", mine.Count)), () => MainWindow.Current?.Navigate(() => new LibraryPage()));
        var recent = mine.Select(g => (Game: g, Played: Features.PlayTime.Get(g.Def.Id)))
            .OrderByDescending(x => x.Played.LastPlayed ?? DateTime.MinValue).FirstOrDefault();
        if (recent.Game is null)
        {
            var add = SmallTile(Icons.Plus, "#22C55E", I18n.T("add.tile"), I18n.T("home.noGames.short"), () => MainWindow.Current?.Navigate(() => new AddGamePage()));
            return add;
        }
        var g = recent.Game;
        var running = Features.Launcher.IsRunning(g.Def.Id);
        var line = running ? I18n.T("time.running")
            : recent.Played.LastPlayed is null ? GameCard.Status(g)
            : string.Join(" · ", new[] { recent.Played.TotalMs > 0 ? Features.PlayTime.Format(recent.Played.TotalMs) : null, Ui.Ago(recent.Played.LastPlayed) }.Where(x => x is not null));

        Button play;
        if (running) play = Ui.Button(I18n.T("v4.stop"), () => Features.Launcher.Stop(g.Def.Id), "", Icons.Stop);
        else if (g.LoaderInstalled || g.Def.Loader == Games.LoaderKind.None) play = Ui.Button(I18n.T("games.play"), () => Actions.Play(g), "", Icons.Play);
        else play = Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(g), "", Icons.Download);
        play.Background = Brushes.White;
        play.Foreground = Ui.Hex("#111113");
        play.BorderThickness = new Thickness(0);
        play.CornerRadius = new CornerRadius(999);
        play.Padding = new Thickness(18, 9);
        play.HorizontalAlignment = HorizontalAlignment.Left;

        var text = Ui.Col(6,
            new TextBlock { Text = I18n.T(recent.Played.LastPlayed is null ? "home.yourGame" : "home.continue").ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.Bold, LetterSpacing = 1.2, Foreground = Ui.Hex("#CCFFFFFF") },
            new TextBlock { Text = g.Def.Name, FontSize = 24, FontWeight = FontWeight.Bold, LetterSpacing = -0.4, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = line, FontSize = 13, Foreground = running ? Ui.Res("Good") : Ui.Hex("#D0FFFFFF"), TextTrimming = TextTrimming.CharacterEllipsis },
            play);
        play.Margin = new Thickness(0, 6, 0, 0);
        text.Margin = new Thickness(22, 0, 22, 20);
        text.VerticalAlignment = VerticalAlignment.Bottom;

        var tile = new Button
        {
            Classes = { "bento" },
            Content = new Panel
            {
                Children =
                {
                    Ui.GameImage(g.Def, 900, art: Images.Art.Hero),
                    new Border { Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.Parse("#100B0B0D"), 0), new GradientStop(Color.Parse("#E60B0B0D"), 1) },
                    } },
                    text,
                },
            },
        };
        tile.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id));
        return tile;
    }

    /// <summary>
    /// Малая плитка: крупный значок, повёрнутый и наполовину спрятанный за край, —
    /// как наклейка на обложке тетради, а не очередная иконка в квадрате.
    /// </summary>
    static Button SmallTile(string icon, string color, string title, string text, Action open)
    {
        var tint = Color.Parse(color);
        var big = Ui.Icon(icon, 120, new SolidColorBrush(Color.FromArgb(70, tint.R, tint.G, tint.B)));
        big.HorizontalAlignment = HorizontalAlignment.Right;
        big.VerticalAlignment = VerticalAlignment.Top;
        big.Margin = new Thickness(0, -18, -26, 0);
        big.RenderTransform = new RotateTransform(-12);
        var badge = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(tint),
            Child = Ui.Icon(icon, 18, Brushes.White), HorizontalAlignment = HorizontalAlignment.Left,
        };
        var col = Ui.Col(4, badge, new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) },
            new TextBlock { Text = text, FontSize = 12.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        col.Margin = new Thickness(18);
        col.VerticalAlignment = VerticalAlignment.Bottom;
        var tile = new Button
        {
            Classes = { "bento" },
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(46, tint.R, tint.G, tint.B), 0), new GradientStop(Color.FromArgb(10, tint.R, tint.G, tint.B), 1) },
            },
            BorderBrush = Ui.Res("Line"),
            BorderThickness = new Thickness(1),
            Content = new Panel { ClipToBounds = true, Children = { big, col } },
        };
        tile.Click += (_, _) => open();
        return tile;
    }

    async Task LoadFeatured(List<GameState> mine)
    {
        var lists = new List<List<(GameState, Sources.ModInfo)>>();
        foreach (var g in mine.Where(g => g.Def.Picks.Length > 0).Take(6))
        {
            try
            {
                var ids = g.Def.Picks.Take(4).ToList();
                var mods = Program.Demo ? Demo.Many(g.Def, ids) : await Sources.Catalog.Many(g.Def, ids);
                lists.Add(mods.Where(m => !Actions.IsInstalled(g, m.Id)).Concat(mods.Where(m => Actions.IsInstalled(g, m.Id))).Select(m => (g, m)).ToList());
            }
            catch { }
        }
        // Чередуем игры: мод из первой, из второй… — чтобы карусель не была про одну игру.
        var result = new List<(GameState, Sources.ModInfo)>();
        for (var i = 0; result.Count < 8 && lists.Any(l => l.Count > i); i++)
            foreach (var l in lists) if (l.Count > i && result.Count < 8) result.Add(l[i]);
        _featured = result;
        foreach (var g in mine) await Views.Aside.PopularAsync(g);
        Build();
    }
}
