using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Библиотека 9.2 «Store» — как «Библиотека» в Microsoft Store: заголовок, фильтры-«пилюли»,
/// сортировка и вид (обложки или список). Сначала игры с компьютера, ниже — остальные поддерживаемые.
/// </summary>
public sealed partial class LibraryPage
{
    string _view = Settings.Data.Str("libraryView") == "list" ? "list" : "grid";

    bool Filter(GameState g) => _collection switch
    {
        "all" => !GameCollections.IsHidden(g.Def.Id),
        "found" => !GameCollections.IsHidden(g.Def.Id) && g.Status == Detect.Found,
        "mods" => !GameCollections.IsHidden(g.Def.Id) && g.Status == Detect.Found && g.ModCount > 0,
        "hidden" => GameCollections.IsHidden(g.Def.Id),
        _ => GameCollections.Has(_collection, g.Def.Id),
    };

    void BuildStore()
    {
        var col = StoreKit.Column(spacing: 22, top: 28);
        bool Match(GameState g) => (_query == "" || g.Def.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)) && Filter(g);
        var all = MainWindow.OrderedGames().ToList();
        var installed = Sorted(all.Where(g => g.Status == Detect.Found && Match(g))).ToList();
        var other = Sorted(all.Where(g => g.Status != Detect.Found && Match(g))).ToList();

        // Заголовок страницы и «Добавить игру».
        var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus);
        var head = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        add.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(add);
        head.Children.Add(Ui.Col(2, Ui.Text(I18n.T("v92.lib.title"), "h1"), Ui.Text(I18n.T("v92.lib.sub", ("found", all.Count(g => g.Status == Detect.Found)), ("all", all.Count)), "muted")));
        col.Children.Add(head);

        // Фильтры-«пилюли» слева, сортировка и вид справа.
        var pills = new WrapPanel();
        Button Pill(string id, string text, int? count = null, string? icon = null)
        {
            var label = count is null ? text : $"{text}  {count}";
            var b = Ui.Button(label, () => { _collection = id; Build(); MainWindow.Current?.RenderAside(); }, _collection == id ? "chip active" : "chip", icon);
            b.Margin = new Thickness(0, 0, 8, 8);
            return b;
        }
        var visible = all.Where(g => !GameCollections.IsHidden(g.Def.Id)).ToList();
        pills.Children.Add(Pill("all", I18n.T("lib.all"), visible.Count));
        pills.Children.Add(Pill("found", I18n.T("v92.lib.found"), visible.Count(g => g.Status == Detect.Found)));
        pills.Children.Add(Pill("mods", I18n.T("v92.lib.withMods"), visible.Count(g => g.Status == Detect.Found && g.ModCount > 0)));
        pills.Children.Add(Pill(GameCollections.Favorites, I18n.T("lib.favorites"), GameCollections.Games(GameCollections.Favorites).Count, Icons.Star));
        foreach (var name in GameCollections.Names()) pills.Children.Add(Pill(name, name, GameCollections.Games(name).Count, Icons.Layers));
        if (GameCollections.HiddenCount > 0) pills.Children.Add(Pill("hidden", I18n.T("lib.hidden", ("n", GameCollections.HiddenCount)), null, Icons.EyeOff));
        var newCollection = Ui.Button(I18n.T("lib.newCollection"), NewCollection, "chip", Icons.Plus);
        newCollection.Margin = new Thickness(0, 0, 8, 8);
        pills.Children.Add(newCollection);

        var sorts = new[] { "recent", "name", "time" };
        var sort = new ComboBox { Width = 200 };
        foreach (var s in sorts) sort.Items.Add(I18n.T("lib.sort." + s));
        sort.SelectedIndex = Math.Max(0, Array.IndexOf(sorts, _sort));
        sort.SelectionChanged += (_, _) =>
        {
            if (sort.SelectedIndex < 0 || sorts[sort.SelectedIndex] == _sort) return;
            _sort = sorts[sort.SelectedIndex];
            Settings.Data["librarySort"] = _sort;
            Settings.Save();
            Build();
        };
        Button View(string id, string icon, string tip) => Ui.Button("", () => { _view = id; Settings.Data["libraryView"] = id; Settings.Save(); Build(); }, _view == id ? "icon active" : "icon ghost", icon, tip);
        var views = new Border { Classes = { "card" }, Padding = new Thickness(3), Child = Ui.Row(2, View("grid", Icons.Grid, I18n.T("cat.grid")), View("list", Icons.List, I18n.T("cat.list"))) };
        var tools = Ui.Row(10, sort, views);
        tools.VerticalAlignment = VerticalAlignment.Top;
        var bar = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        bar.Children.Add(tools);
        bar.Children.Add(pills);
        col.Children.Add(bar);

        var n = 0;
        Control Games(List<GameState> list, bool addTile)
        {
            if (_view == "list")
            {
                var rows = new StackPanel { Spacing = 6 };
                foreach (var g in list) rows.Children.Add(Intro(Row(g), n++));
                return rows;
            }
            var wrap = new WrapPanel();
            foreach (var g in list) { var p = StoreKit.Poster(g); p.Margin = new Thickness(0, 0, 14, 16); wrap.Children.Add(Intro(p, n++)); }
            if (addTile) { var a = StoreKit.AddPoster(); a.Margin = new Thickness(0, 0, 14, 16); wrap.Children.Add(Intro(a, n++)); }
            return wrap;
        }

        if (installed.Count > 0 || _collection is "all" or "found")
            col.Children.Add(Ui.Col(14, StoreKit.Header(I18n.T("lib.found"), count: installed.Count.ToString()), Games(installed, _collection is "all" or "found" && _query == "")));
        if (installed.Count == 0 && other.Count == 0)
            col.Children.Add(Ui.Card(Ui.Text(_query != "" ? I18n.T("catalog.nothingFound", ("query", _query)) : I18n.T("lib.emptyCollection"), "muted", wrap: true), 22));

        if (other.Count > 0 && _collection is not ("found" or "mods"))
        {
            var toggle = Ui.Button(_showOther ? I18n.T("lib.hideOther") : I18n.T("lib.showOther"), () =>
            {
                _showOther = !_showOther;
                Settings.Data["libraryShowOther"] = _showOther;
                Settings.Save();
                Build();
            }, "ghost", _showOther ? Icons.EyeOff : Icons.Eye);
            var section = Ui.Col(14, StoreKit.Header(I18n.T("lib.other"), right: toggle, count: other.Count.ToString()), Ui.Text(I18n.T("lib.other.hint"), "small muted", wrap: true));
            if (_showOther) section.Children.Add(Games(other, false));
            col.Children.Add(section);
        }
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Строка игры в виде «список», как в библиотеке Microsoft Store.</summary>
    static Control Row(GameState g)
    {
        var (dot, state) = StoreKit.GameState(g);
        var played = PlayTime.Get(g.Def.Id);
        var cover = new Border { Width = 44, Height = 66, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Ui.GameImage(g.Def, 100, art: Images.Art.Cover) };
        var title = Ui.Col(3, Ui.Text(g.Def.Name, "h3"), Ui.Row(7, Ui.Dot(dot, 7), Ui.Text(state, "small muted")));
        title.VerticalAlignment = VerticalAlignment.Center;
        TextBlock Cell(string text) => new() { Text = text, FontSize = 13, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var mods = Cell(g.Status == Detect.Found ? StoreKit.Mods(g.ModCount) : "—");
        var time = Cell(played.TotalMs > 0 ? I18n.T("time.total", ("time", PlayTime.Format(played.TotalMs))) : "—");
        var last = Cell(played.LastPlayed is null ? "—" : Ui.Ago(played.LastPlayed));
        Control action;
        var gs = g;
        if (g.Status != Detect.Found) action = Ui.Button(I18n.T("games.setPath"), () => Actions.PickGameFolder(gs), "", Icons.Folder);
        else if (Features.Launcher.IsRunning(g.Def.Id)) action = PlayControls.RunningPill(g.Def.Id, big: false, onArt: false);
        else if (g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft) action = Ui.Button(I18n.T("games.play"), () => Actions.Play(gs), "primary", Icons.Play);
        else action = Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => Actions.InstallLoader(gs), "", Icons.Download);
        action.VerticalAlignment = VerticalAlignment.Center;
        var more = Ui.Button("", () => { }, "icon ghost", Icons.More, I18n.T("top.more"));
        more.Flyout = GameCard.Menu(g);
        more.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,2.2*,*,*,*,200,Auto"), ColumnSpacing = 16 };
        action.HorizontalAlignment = HorizontalAlignment.Right;
        var cells = new Control[] { cover, title, mods, time, last, action, more };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        var row = new Border { Classes = { "store-row" }, Padding = new Thickness(10, 8), Child = grid, Opacity = g.Status == Detect.Found ? 1 : 0.7 };
        var id = g.Def.Id;
        StoreKit.OnClick(row, () => MainWindow.Current?.Navigate(() => new GamePage(id)));
        row.ContextFlyout = GameCard.Menu(g);
        return row;
    }
}
