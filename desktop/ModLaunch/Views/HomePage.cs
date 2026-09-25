using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Главная в духе Modrinth App: продолжить игру, ваши (найденные) игры,
/// популярные моды и избранное. Все остальные игры — в «Библиотеке».
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

    Control Intro(Control c, int index) { if (!Shown) Animate.Rise(c, index); return c; }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 30, Margin = new Thickness(32, 26, 32, 32), MaxWidth = 1240 };

        // Продолжить игру — последние запущенные.
        var recent = AppState.Games
            .Select(g => (Game: g, Played: Features.PlayTime.Get(g.Def.Id)))
            .Where(x => x.Game.Status == Detect.Found && x.Played.LastPlayed is not null)
            .OrderByDescending(x => x.Played.LastPlayed).Take(3).ToList();
        if (recent.Count > 0 && Settings.Data.Bool("homeContinue", true)) content.Children.Add(Continue(recent));

        // Ваши игры — только те, что есть на компьютере.
        var mine = MainWindow.OrderedGames().Where(g => g.Status == Detect.Found && !Features.GameCollections.IsHidden(g.Def.Id)).ToList();
        var searching = AppState.Games.Any(g => g.Status == Detect.Searching);
        var games = new WrapPanel();
        var n = 0;
        foreach (var g in mine) games.Children.Add(Intro(GameCard.Cover(g, 132), n++));
        games.Children.Add(Intro(GameCard.AddCover(132), n++));
        var section = Ui.Col(12, Header(I18n.T("home.yourGames"), I18n.T("lib.open"), () => MainWindow.Current?.Navigate(() => new LibraryPage())));
        if (mine.Count == 0)
            section.Children.Add(Ui.Text(searching ? I18n.T("games.searching") : I18n.T("home.noGames"), "muted", wrap: true));
        section.Children.Add(games);
        content.Children.Add(section);

        // «Выбор ModLaunch» — карусель лучших модов для ваших игр.
        if (_featured is null) { if (!_featuredLoading) { _featuredLoading = true; Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadFeatured(mine)); } }
        else if (_featured.Count > 0 && Settings.Data.Bool("homePopular", true))
        {
            _featuredView ??= new Featured(_featured);
            if (_featuredView.Parent is Panel old) old.Children.Remove(_featuredView);
            content.Children.Add(_featuredView);
        }

        // Топ модов: все ваши игры вместе или одна.
        if (mine.Count > 0 && Settings.Data.Bool("homePopular", true)) content.Children.Add(TopMods(mine));

        var favorites = Favorites.All().Where(f => AppState.Games.Any(g => g.Def.Id == f.GameId && g.Status == Detect.Found)).Take(20).ToList();
        if (favorites.Count > 0 && Settings.Data.Bool("homeFavorites", true)) content.Children.Add(Shelf(I18n.T("home.favorites"), favorites, null));

        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Header(string title, string? link, Action? open)
    {
        var row = new DockPanel();
        if (link is not null && open is not null)
        {
            var more = Ui.Button(link + "  ›", open, "ghost");
            more.Foreground = Ui.Res("Muted");
            more.Padding = new Thickness(8, 4);
            DockPanel.SetDock(more, Dock.Right);
            row.Children.Add(more);
        }
        var t = Ui.Text(title, "h2");
        t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        return row;
    }

    static Control Continue(List<(GameState Game, Features.Played Played)> recent)
    {
        var row = new WrapPanel();
        foreach (var (g, played) in recent)
        {
            var running = Features.Launcher.IsRunning(g.Def.Id);
            row.Children.Add(GameCard.Row(g, running ? I18n.T("time.running") : I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed)))));
        }
        return Ui.Col(12, Header(I18n.T("v4.continue"), null, null), row);
    }

    // Карусель и её данные живут весь сеанс: перерисовка главной не сбрасывает прокрутку.
    static List<(GameState Game, Sources.ModInfo Mod)>? _featured;
    Featured? _featuredView;
    static bool _featuredLoading;
    string _topGame = "all";

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
        for (var i = 0; result.Count < 10 && lists.Any(l => l.Count > i); i++)
            foreach (var l in lists) if (l.Count > i && result.Count < 10) result.Add(l[i]);
        _featured = result;
        foreach (var g in mine) await Views.Aside.PopularAsync(g);
        Build();
    }

    Control TopMods(List<GameState> mine)
    {
        var chips = new WrapPanel();
        Button Chip(string id, string text, Games.GameDef? def)
        {
            Control content = def is null ? new TextBlock { Text = text } : Ui.Row(6, new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = Ui.GameImage(def, 40, art: Images.Art.Cover) }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Classes = { "chip" }, Content = content, Margin = new Thickness(0, 0, 8, 8) };
            if (_topGame == id) b.Classes.Add("active");
            b.Click += (_, _) => { _topGame = id; Build(); };
            return b;
        }
        chips.Children.Add(Chip("all", I18n.T("top.all"), null));
        foreach (var g in mine.Where(g => g.Def.HasCatalog)) chips.Children.Add(Chip(g.Def.Id, g.Def.ShortName, g.Def));

        var pool = mine.Where(g => _topGame == "all" || g.Def.Id == _topGame)
            .SelectMany(g => (Views.Aside.Popular(g) ?? []).Select(m => (Game: g, Mod: m)))
            .OrderByDescending(x => x.Mod.Downloads).Take(8).ToList();
        var list = Ui.Col(10);
        foreach (var (g, m) in pool)
        {
            var gg = g; var mm = m;
            list.Children.Add(ModRow.Build(g.Def, m, Actions.IsInstalled(g, m.Id), Actions.IsBusy(g, m.Id), false,
                () => _ = Actions.Install(gg, mm), () => MainWindow.Current?.Navigate(() => new ModPage(gg.Def.Id, mm))));
        }
        if (pool.Count == 0) list.Children.Add(Ui.Text(I18n.T("common.loading"), "muted"));
        var head = new DockPanel();
        var title = Ui.Row(10, Ui.Icon(Icons.Trophy, 20, Ui.Hex("#F2C25C")), Ui.Text(I18n.T("top.title"), "h2"));
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        return Ui.Col(12, head, chips, list);
    }

    /// <summary>Полка модов: небольшие карточки в ряд с прокруткой.</summary>
    static Control Shelf(string title, List<(string GameId, Sources.ModInfo Mod)> mods, Action? more)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var (gameId, mod) in mods)
        {
            var card = new Button
            {
                Classes = { "card-btn" },
                Width = 168,
                Padding = new Thickness(10),
                Content = Ui.Col(8,
                    Ui.Thumb(mod.Icon, mod.Name, 148, 10, 300),
                    Ui.Text(mod.Name, "h3"),
                    Ui.Text(mod.Author == "" ? AppState.Game(gameId).Def.ShortName : mod.Author, "small muted")),
            };
            var id = gameId;
            var m = mod;
            card.Click += (_, _) => MainWindow.Current?.Navigate(() => new ModPage(id, m));
            row.Children.Add(card);
        }
        return Ui.Col(12, Header(title, more is null ? null : I18n.T("home.all"), more),
            new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 10) });
    }
}
