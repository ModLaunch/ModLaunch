using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: список игр слева в библиотеке, как в Steam: поиск, фильтр (все, установленные, с модами),
/// «Избранное», ваши игры (недавние сверху) и свёрнутый список поддерживаемых игр, которых нет на компьютере.
/// Строка — обложка, название и состояние (запущена, нужен загрузчик, сколько модов); щелчок — страница игры,
/// двойной щелчок — играть, правый — меню игры. Выбранная игра подсвечена, пока открыта её страница или мод.
/// </summary>
public sealed class LibrarySidebar : Border
{
    readonly TextBox _find = new() { Height = 32, MinHeight = 32 };
    readonly StackPanel _list = new() { Spacing = 1 };
    readonly StackPanel _filters = Ui.Row(4);
    readonly TextBlock _count = new() { FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center };
    readonly Button _sort;
    readonly Border _ad = new();
    readonly TextBlock _title = new() { FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1.1, VerticalAlignment = VerticalAlignment.Center };
    string _query = "";
    string _filter = Settings.Data.Str("sidebarFilter") is "installed" or "mods" ? Settings.Data.Str("sidebarFilter")! : "all";
    string _key = "";
    string? _lit;
    readonly List<(string Id, Button Row)> _rows = [];

    static readonly string[] Sorts = ["recent", "name", "time"];

    public LibrarySidebar()
    {
        Background = Ui.Res("Rail");
        _find.Watermark = I18n.T("v20.side.search");
        _find.Classes.Add("side-search");
        _find.InnerLeftContent = new Border { Padding = new Thickness(10, 0, 0, 0), Child = Ui.Icon(Icons.Search, 13, Ui.Res("Faint")) };
        _find.TextChanged += (_, _) => { _query = (_find.Text ?? "").Trim(); _key = ""; Render(MainWindow.Current?.CurrentPage); };
        _find.KeyDown += (_, e) =>
        {
            // Enter — открыть первую найденную игру.
            if (e.Key != Key.Enter || _rows.Count == 0) return;
            var id = _rows[0].Id;
            MainWindow.Current?.Navigate(() => new GamePage(id));
        };

        _sort = new Button { Classes = { "icon", "ghost" }, Width = 26, Height = 26, CornerRadius = new CornerRadius(6), Content = Ui.Icon(Icons.Sort, 13, Ui.Res("Muted")) };
        _sort.Click += (_, _) =>
        {
            var at = Array.IndexOf(Sorts, Sort);
            Settings.Data["librarySort"] = Sorts[(at + 1) % Sorts.Length];
            Settings.Save();
            _key = "";
            Render(MainWindow.Current?.CurrentPage);
        };

        var head = new DockPanel { Margin = new Thickness(14, 14, 10, 8) };
        DockPanel.SetDock(_sort, Dock.Right);
        head.Children.Add(_sort);
        _title.Foreground = Ui.Res("Muted");
        head.Children.Add(Ui.Row(8, _title, _count));
        var top = Ui.Col(8, new Border { Margin = new Thickness(10, 0, 10, 0), Child = _find }, new Border { Margin = new Thickness(10, 0, 10, 4), Child = _filters });
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(top, Dock.Top);
        _ad.Margin = new Thickness(10, 6, 10, 10);
        DockPanel.SetDock(_ad, Dock.Bottom);
        var scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(6, 4, 6, 10), Child = _list }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Child = new DockPanel { Children = { head, top, _ad, scroll } };
        I18n.Changed += () => { _find.Watermark = I18n.T("v20.side.search"); _key = ""; };
    }

    static string Sort => Sorts.Contains(Settings.Data.Str("librarySort")) ? Settings.Data.Str("librarySort")! : "recent";

    static IEnumerable<GameState> Sorted(IEnumerable<GameState> games) => Sort switch
    {
        "name" => games.OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase),
        "time" => games.OrderByDescending(g => PlayTime.Get(g.Def.Id).TotalMs).ThenBy(g => g.Def.Name),
        _ => games.OrderByDescending(g => NewGames.Recent(g)).ThenBy(g => g.Def.Name),
    };

    bool Passes(GameState g) => (_query == "" || g.Def.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)) && _filter switch
    {
        "installed" => g.Status == Detect.Found,
        "mods" => g.Status == Detect.Found && g.ModCount > 0,
        _ => true,
    };

    /// <summary>Свёрнутые группы списка (запоминаются).</summary>
    static bool Collapsed(string group) => Settings.Data.Arr("sidebarCollapsed").Any(x => x?.ToString() == group) != (group == "other");

    static void ToggleGroup(string group)
    {
        var list = Settings.Data.Arr("sidebarCollapsed").Select(x => x?.ToString()).OfType<string>().ToList();
        if (!list.Remove(group)) list.Add(group);
        Settings.Data["sidebarCollapsed"] = new System.Text.Json.Nodes.JsonArray(list.Select(x => (System.Text.Json.Nodes.JsonNode)x).ToArray());
        Settings.Save();
    }

    /// <summary>
    /// Перестроить список, если поменялись игры, их состояние, фильтр или язык; иначе — только подсветить игру открытой страницы.
    /// </summary>
    public void Render(Page? current)
    {
        _lit = current?.GameId;
        var games = MainWindow.OrderedGames().Where(g => !GameCollections.IsHidden(g.Def.Id)).ToList();
        var key = string.Join("|", games.Select(g => $"{g.Def.Id}:{g.Def.Name}:{g.Status}:{g.ModCount}:{g.LoaderInstalled}:{Launcher.IsRunning(g.Def.Id)}:{GameCollections.IsFavorite(g.Def.Id)}:{NewGames.IsNew(g)}"))
            + $"#{_query}#{_filter}#{Sort}#{I18n.Lang}#{Look.Theme}{Look.Accent}#{string.Join(",", Settings.Data.Arr("sidebarCollapsed"))}";
        if (key != _key) { _key = key; Rebuild(games); }
        foreach (var (id, row) in _rows) row.Classes.Set("active", id == _lit);
    }

    void Rebuild(List<GameState> games)
    {
        _rows.Clear();
        _list.Children.Clear();
        RenderFilters();
        _title.Text = I18n.T("v20.side.title").ToUpper(I18n.Culture);
        ToolTip.SetTip(_sort, I18n.T("v20.side.sort", ("sort", I18n.T("lib.sort." + Sort))));
        var found = games.Where(g => g.Status == Detect.Found).ToList();
        _count.Text = found.Count.ToString();

        var favorites = Sorted(found.Where(g => GameCollections.IsFavorite(g.Def.Id) && Passes(g))).ToList();
        var mine = Sorted(found.Where(g => !GameCollections.IsFavorite(g.Def.Id) && Passes(g))).ToList();
        var other = _filter == "all" ? games.Where(g => g.Status != Detect.Found && Passes(g)).OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase).ToList() : [];

        Group("favorites", I18n.T("v20.side.favorites"), favorites);
        Group("games", I18n.T("v20.side.games"), mine);
        Group("other", I18n.T("v20.side.other"), other);
        if (favorites.Count + mine.Count + other.Count == 0)
        {
            var empty = Ui.Col(10, Ui.Text(_query == "" ? I18n.T("v20.side.empty") : I18n.T("v20.side.nothing", ("query", _query)), "small muted", wrap: true));
            var q = _query;
            empty.Children.Add(Ui.Button(I18n.T(_query == "" ? "add.title" : "v20.side.find"), () => MainWindow.Current?.Navigate(() => new AddGamePage(q)), "", Icons.Plus));
            empty.Margin = new Thickness(10, 8, 10, 0);
            _list.Children.Add(empty);
        }
        RenderAd();
    }

    void RenderFilters()
    {
        _filters.Children.Clear();
        foreach (var (id, key) in new[] { ("all", "v20.side.all"), ("installed", "v20.side.installed"), ("mods", "v20.side.withMods") })
        {
            var f = id;
            var b = new Button { Classes = { "side-filter" }, Content = I18n.T(key) };
            if (_filter == id) b.Classes.Add("active");
            b.Click += (_, _) =>
            {
                _filter = f;
                Settings.Data["sidebarFilter"] = f;
                Settings.Save();
                _key = "";
                Render(MainWindow.Current?.CurrentPage);
            };
            _filters.Children.Add(b);
        }
    }

    void Group(string id, string title, List<GameState> games)
    {
        if (games.Count == 0) return;
        // Поиск раскрывает свёрнутые группы — иначе найденное пряталось бы.
        var collapsed = Collapsed(id) && _query == "";
        var chevron = Ui.Icon(collapsed ? Icons.ChevronRight : Icons.ChevronDown, 12, Ui.Res("Faint"));
        var head = new Button
        {
            Classes = { "side-group" },
            Content = Ui.Row(6, chevron,
                new TextBlock { Text = title.ToUpper(I18n.Culture), FontSize = 10.5, FontWeight = FontWeight.Bold, LetterSpacing = 0.9, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = $"({games.Count})", FontSize = 10.5, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center }),
        };
        head.Click += (_, _) => { ToggleGroup(id); _key = ""; Render(MainWindow.Current?.CurrentPage); };
        _list.Children.Add(head);
        if (collapsed) return;
        foreach (var g in games)
        {
            var row = Row(g);
            _rows.Add((g.Def.Id, row));
            _list.Children.Add(row);
        }
    }

    /// <summary>Строка игры: обложка 2:3, название и состояние; при наведении — «Играть».</summary>
    static Button Row(GameState g)
    {
        var id = g.Def.Id;
        var found = g.Status == Detect.Found;
        var running = Launcher.IsRunning(id);
        var ready = g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft;
        var fresh = NewGames.IsNew(g);
        var cover = new Border
        {
            Width = 28, Height = 38, CornerRadius = new CornerRadius(5), ClipToBounds = true, Opacity = found ? 1 : 0.55,
            Child = Ui.GameImage(g.Def, 96, art: Images.Art.Cover),
        };
        var name = new TextBlock { Text = g.Def.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = found ? Ui.Res("Text") : Ui.Res("Muted") };
        var (lineText, lineColor) = running ? (I18n.T("run.running"), Ui.Res("Good"))
            : fresh ? (I18n.T("v151.new"), Ui.Res("Brand2"))
            : found && !ready ? (GameCard.Status(g), Ui.Res("Warn"))
            : (GameCard.Status(g), Ui.Res("Faint"));
        var line = new TextBlock { Text = lineText, FontSize = 11, Foreground = lineColor, TextTrimming = TextTrimming.CharacterEllipsis };
        var words = Ui.Col(1, name, line);
        words.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(cover);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        if (found && ready)
        {
            var gs = g;
            var play = running
                ? Ui.Button("", () => Launcher.Stop(gs.Def.Id), "icon", Icons.Stop, I18n.T("v4.stop"))
                : Ui.Button("", () => Actions.Play(gs), "icon primary", Icons.Play, I18n.T("games.play"));
            play.Classes.Add("side-play");
            play.Width = play.Height = 28;
            play.CornerRadius = new CornerRadius(7);
            play.VerticalAlignment = VerticalAlignment.Center;
            if (running) play.Classes.Add("on");
            Grid.SetColumn(play, 2);
            grid.Children.Add(play);
        }
        var b = new Button { Classes = { "librow" }, Content = grid };
        b.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(id));
        if (found && ready) b.DoubleTapped += (_, _) => Actions.Play(g);
        b.ContextFlyout = GameCard.Menu(g);
        ToolTip.SetTip(b, g.Def.Name);
        ToolTip.SetPlacement(b, PlacementMode.Right);
        return b;
    }

    /// <summary>Реклама — тихой строкой внизу списка (вместо колонки справа на каждой странице).</summary>
    void RenderAd()
    {
        _ad.IsVisible = Ads.Enabled && !AdSlot.SideClosed;
        if (_ad.IsVisible && _ad.Child is null) _ad.Child = AdSlot.Compact(() => { AdSlot.SideClosed = true; _ad.IsVisible = false; });
    }
}
