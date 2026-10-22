using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

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
        head.Children.Add(Ui.Col(2, Ui.Text(I18n.T("v92.lib.title"), "h1"), Ui.Text(I18n.T("v151.lib.sub", ("found", all.Count(g => g.Status == Detect.Found)), ("all", Supported().ToString("N0", I18n.Culture))), "muted")));
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
        if (_collection == "all") col.Children.Add(CatalogShelf(all, ref n));
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Сколько игр поддерживается: справочник с модами и все игры Steam (каталог грузится в фоне).</summary>
    static int Supported() => Math.Max(GameDirectory.Count, CustomGames.SupportedCount);

    /// <summary>
    /// 15.1: весь каталог (141 тыс. игр) — прямо в библиотеке, а не только «7 из 14». Без поиска — игры с модами,
    /// которых ещё нет в библиотеке; с поиском — совпадения из каталога. Клик — «Добавить игру» с этой игрой.
    /// </summary>
    Control CatalogShelf(List<GameState> mine, ref int n)
    {
        var have = mine.Where(g => g.Def.SteamAppId > 0).Select(g => g.Def.SteamAppId).ToHashSet();
        var names = mine.Select(g => g.Def.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Fresh(DirGame d) => !(d.SteamAppId > 0 && have.Contains(d.SteamAppId)) && !names.Contains(d.Name);
        const int Take = 12;
        List<DirGame> rows;
        if (_query == "") rows = AddGamePage.PopularDirectory().Where(Fresh).Take(Take).ToList();
        else
        {
            rows = GameDirectory.Search(_query, Take * 2).Where(Fresh).Take(Take).ToList();
            if (rows.Count < Take && SteamCatalog.Ready)
            {
                var shown = rows.Where(r => r.SteamAppId > 0).Select(r => r.SteamAppId).Concat(have).ToHashSet();
                rows.AddRange(SteamCatalog.Search(_query, Take - rows.Count, shown).Select(g => GameDirectory.BySteam(g.SteamAppId) ?? g).Where(Fresh));
            }
        }
        var query = _query;
        var open = Ui.Button(query == "" ? I18n.T("v151.lib.catalog.open") : I18n.T("v151.lib.catalog.find", ("query", query)),
            () => MainWindow.Current?.Navigate(() => new AddGamePage(query)), "ghost", Icons.ChevronRight);
        var section = Ui.Col(14,
            StoreKit.Header(I18n.T("v151.lib.catalog"), right: open, count: Supported().ToString("N0", I18n.Culture)),
            Ui.Text(I18n.T(query == "" ? "v151.lib.catalog.hint" : "v151.lib.catalog.hint.query"), "small muted", wrap: true));
        if (rows.Count == 0)
        {
            section.Children.Add(Ui.Text(I18n.T("v12g.all.none", ("query", query)), "muted", wrap: true));
            return section;
        }
        var wrap = new WrapPanel();
        foreach (var d in rows) { var t = CatalogTile(d); t.Margin = new Thickness(0, 0, 14, 16); wrap.Children.Add(Intro(t, n++)); }
        section.Children.Add(wrap);
        return section;
    }

    /// <summary>Игра каталога: картинка Steam, название, сколько модов. Клик — добавить её (найти на диске или поставить).</summary>
    static Control CatalogTile(DirGame d)
    {
        const double w = 230;
        // Пока картинка грузится (или её нет) — первая буква на градиенте своего цвета, а не пустой серый прямоугольник.
        var hue = (int)(d.Name.Aggregate(17u, (h, c) => h * 31 + c) % 360);
        var fallback = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Ui.HslColor(hue, 0.45, 0.34), 0), new GradientStop(Ui.HslColor((hue + 40) % 360, 0.5, 0.18), 1) },
            },
            Child = new TextBlock
            {
                Text = d.Name.FirstOrDefault(char.IsLetterOrDigit).ToString().ToUpperInvariant(), FontSize = 34, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var image = new Image { Classes = { "zoom" }, Stretch = Stretch.UniformToFill };
        if (d.Art is { } url)
            _ = Images.FromUrl(url, 460).ContinueWith(t => { if (t.Result is Avalonia.Media.Imaging.Bitmap b) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = b); });
        var art = new Border { Height = Math.Round(w * 215 / 460), ClipToBounds = true, Child = new Panel { Children = { fallback, image } } };
        var name = new TextBlock { Text = d.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis };
        var mods = d.Mods > 0
            ? I18n.T("v12g.mods." + I18n.Plural(d.Mods, "one", "few", "many"), ("n", I18n.Compact(d.Mods)))
            : I18n.T("v151.lib.catalog.nomods");
        var sub = new TextBlock { Text = mods, FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis };
        var tile = new Border
        {
            Classes = { "store-tile" }, Width = w, ClipToBounds = true,
            Child = new StackPanel { Children = { art, new StackPanel { Spacing = 3, Margin = new Thickness(12, 10, 12, 12), Children = { name, sub } } } },
        };
        ToolTip.SetTip(tile, d.Name);
        var title = d.Name;
        StoreKit.OnClick(tile, () => MainWindow.Current?.Navigate(() => new AddGamePage(title)));
        return tile;
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
