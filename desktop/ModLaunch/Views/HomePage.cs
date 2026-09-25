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

    public override void Search(string text)
    {
        var game = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found);
        if (game is null) { MainWindow.Current?.Toast(I18n.T("search.noGames")); return; }
        MainWindow.Current?.Navigate(() => new GamePage(game.Def.Id, "catalog", text));
    }

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
        var mine = MainWindow.OrderedGames().Where(g => g.Status == Detect.Found).ToList();
        var searching = AppState.Games.Any(g => g.Status == Detect.Searching);
        var games = new WrapPanel();
        foreach (var g in mine) games.Children.Add(GameCard.Create(g));
        games.Children.Add(GameCard.Add());
        var section = Ui.Col(12, Header(I18n.T("home.yourGames"), I18n.T("lib.open"), () => MainWindow.Current?.Navigate(() => new LibraryPage())));
        if (mine.Count == 0)
            section.Children.Add(Ui.Text(searching ? I18n.T("games.searching") : I18n.T("home.noGames"), "muted", wrap: true));
        section.Children.Add(games);
        content.Children.Add(section);

        var first = mine.FirstOrDefault(g => g.LoaderInstalled) ?? mine.FirstOrDefault();
        if (first is not null && Settings.Data.Bool("homePopular", true))
        {
            if (_popularFor != first.Def.Id) { _popularFor = first.Def.Id; _popular = null; _ = LoadPopular(first); }
            if (_popular is { Count: > 0 })
                content.Children.Add(Shelf(I18n.T("home.popular", ("game", first.Def.Name)), _popular.Select(m => (first.Def.Id, m)).ToList(),
                    () => MainWindow.Current?.Navigate(() => new GamePage(first.Def.Id, "catalog"))));
        }

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
            var cover = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = Ui.GameImage(g.Def, 160) };
            var info = Ui.Col(2, Ui.Text(g.Def.Name, "h3"),
                Ui.Text(running ? I18n.T("time.running") : I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed))), "small", color: running ? Ui.Res("Good") : Ui.Res("Muted")));
            info.VerticalAlignment = VerticalAlignment.Center;
            var gs = g;
            var play = running
                ? Ui.Button(I18n.T("v4.stop"), () => Features.Launcher.Stop(gs.Def.Id), "", Icons.Stop)
                : Ui.Button(I18n.T("games.play"), () => Actions.Play(gs), "primary", Icons.Play);
            play.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            grid.Children.Add(cover);
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);
            Grid.SetColumn(play, 2);
            grid.Children.Add(play);
            var card = new Button { Classes = { "card-btn" }, Width = 380, Padding = new Thickness(10), Margin = new Thickness(0, 0, 12, 12), Content = grid };
            card.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(gs.Def.Id));
            row.Children.Add(card);
        }
        return Ui.Col(12, Header(I18n.T("v4.continue"), null, null), row);
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
