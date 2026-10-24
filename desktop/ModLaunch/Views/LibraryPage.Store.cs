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

        // Заголовок страницы, «Найти игры заново» и «Добавить игру».
        var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus);
        var rescan = Ui.Button(I18n.T("v20.lib.rescan"), () => { _ = AppState.DetectAll(force: true); MainWindow.Current?.Toast(I18n.T("cp.rescanning")); }, "", Icons.Refresh);
        var headButtons = Ui.Row(10, rescan, add);
        headButtons.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel();
        DockPanel.SetDock(headButtons, Dock.Right);
        head.Children.Add(headButtons);
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
            // 20.0: обложки ровной сеткой во всю ширину, название и состояние — под обложкой.
            var items = new List<Control>();
            foreach (var g in list) items.Add(Intro(Poster20(g, small: !addTile), n++));
            if (addTile) items.Add(Intro(AddPoster20(), n++));
            return Kit20.Grid(items, addTile ? 150 : 118, addTile ? 8 : 10, addTile ? 20 : 16);
        }

        if (installed.Count > 0 || _collection is "all" or "found")
            col.Children.Add(Ui.Col(16, Count20(I18n.T("v20.lib.here"), installed.Count), Games(installed, _collection is "all" or "found" && _query == "")));
        if (installed.Count == 0 && other.Count == 0)
            col.Children.Add(Ui.Card(Ui.Text(_query != "" ? I18n.T("catalog.nothingFound", ("query", _query)) : I18n.T("lib.emptyCollection"), "muted", wrap: true), 22));

        if (other.Count > 0 && _collection is not ("found" or "mods"))
        {
            var toggle = Kit20.Link(_showOther ? I18n.T("v20.lib.collapse") : I18n.T("lib.showOther"), () =>
            {
                _showOther = !_showOther;
                Settings.Data["libraryShowOther"] = _showOther;
                Settings.Save();
                Build();
            }, 13.5);
            var manual = Kit20.Link(I18n.T("v20.lib.manual"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), 13.5);
            var links = Ui.Row(8, manual, toggle);
            var otherHead = new DockPanel();
            DockPanel.SetDock(links, Dock.Right);
            links.VerticalAlignment = VerticalAlignment.Center;
            otherHead.Children.Add(links);
            otherHead.Children.Add(Count20(I18n.T("v20.lib.other"), other.Count));
            var section = Ui.Col(14, otherHead);
            if (_showOther) section.Children.Add(Games(other, false));
            col.Children.Add(section);
        }
        if (_collection == "all") col.Children.Add(CatalogShelf(all, ref n));
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Заголовок раздела библиотеки: «На этом компьютере · 7».</summary>
    static Control Count20(string title, int count) => Ui.Row(8,
        new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
        new TextBlock { Text = "· " + count.ToString("N0", I18n.Culture), FontSize = 17, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center });

    /// <summary>
    /// 20.0: обложка 2:3 и под ней название и состояние. При наведении — затемнение, круглая «Играть» и «Моды · N».
    /// Маленькая (не найденные игры) — бледнее, без кнопок.
    /// </summary>
    static Control Poster20(GameState g, bool small)
    {
        var found = g.Status == Detect.Found;
        var (dot, state) = StoreKit.GameState(g);
        var art = new Border { CornerRadius = new CornerRadius(small ? 12 : 14), ClipToBounds = true, Child = Ui.GameImage(g.Def, small ? 240 : 320, art: Images.Art.Cover) };
        var frame = new Panel { Children = { art } };
        // Высота по ширине: обложки Steam — 2:3.
        frame.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width * 1.5 - frame.Height) > 1) frame.Height = Math.Round(e.NewSize.Width * 1.5); };
        frame.Height = small ? 177 : 225;
        var ready = found && (g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft);
        if (found && !small)
        {
            var id = g.Def.Id;
            var running = Features.Launcher.IsRunning(id);
            var hover = new Panel { Opacity = 0, IsHitTestVisible = true };
            hover.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(150, 8, 8, 12)) });
            var stack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (ready && !running)
            {
                var gs = g;
                var play = new Button { Classes = { "primary" }, Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Padding = new Thickness(4, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Content = Ui.Icon(Icons.Play, 22, null, fill: true) };
                play.Click += (_, _) => Actions.Play(gs);
                ToolTip.SetTip(play, I18n.T("games.play"));
                stack.Children.Add(play);
                stack.Children.Add(new TextBlock { Text = I18n.T("games.play"), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            }
            var modsButton = new Button
            {
                Classes = { "chip20" }, Padding = new Thickness(12, 5), CornerRadius = new CornerRadius(999), HorizontalAlignment = HorizontalAlignment.Center,
                Content = new TextBlock { Text = g.ModCount > 0 ? I18n.T("v20.lib.mods", ("n", g.ModCount)) : I18n.T("tab.mods"), FontSize = 12.5, FontWeight = FontWeight.SemiBold },
            };
            modsButton.Click += (_, _) => MainWindow.Current?.Navigate(() => new GamePage(id, "installed"));
            stack.Children.Add(modsButton);
            hover.Children.Add(stack);
            if (Animate.On) hover.Transitions = [new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) }];
            frame.Children.Add(hover);
            frame.PointerEntered += (_, _) => hover.Opacity = 1;
            frame.PointerExited += (_, _) => hover.Opacity = 0;
        }
        var fresh = Features.NewGames.IsNew(g);
        if (g.Def.Custom || fresh)
            frame.Children.Add(new Border
            {
                Background = fresh ? Ui.Res("Brand") : new SolidColorBrush(Color.FromArgb(190, 10, 10, 12)), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2),
                Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Child = new TextBlock { Text = fresh ? I18n.T("v151.new") : I18n.T("lib.custom"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = fresh ? Ui.Res("OnBrand") : Brushes.White },
            });
        if (GameCollections.IsFavorite(g.Def.Id))
            frame.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(8), Background = new SolidColorBrush(Color.FromArgb(190, 10, 10, 12)), IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = Ui.Icon(Icons.Star, 12, Ui.Hex("#F2C25C"), fill: true),
            });
        var name = new TextBlock { Text = g.Def.Name, FontSize = small ? 13 : 14.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = found ? Ui.Res("Text") : Ui.Res("Muted") };
        var col = new StackPanel { Spacing = small ? 6 : 8, Children = { frame, name } };
        if (!small) col.Children.Add(Ui.Row(6, Ui.Dot(dot, 7), new TextBlock { Text = state, FontSize = 12.5, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
        // Бледность — у содержимого, а не у плитки: появление страницы доводит прозрачность плитки до 1.
        col.Opacity = found ? 1 : 0.6;
        var tile = new Border { Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Child = col };
        ToolTip.SetTip(tile, found ? $"{g.Def.Name} · {state}" : g.Def.Name);
        var gid = g.Def.Id;
        StoreKit.OnClick(tile, () => MainWindow.Current?.Navigate(() => new GamePage(gid)));
        tile.ContextFlyout = GameCard.Menu(g);
        return tile;
    }

    /// <summary>«Добавить игру» — штриховая плитка в размер обложки.</summary>
    static Control AddPoster20()
    {
        var plus = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Ui.Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Center, Child = Ui.Icon(Icons.Plus, 20, Ui.Res("Text")) };
        var words = Ui.Col(8, plus,
            new TextBlock { Text = I18n.T("add.title"), FontWeight = FontWeight.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = I18n.T("v92.add.hint"), FontSize = 12.5, Foreground = Ui.Res("Faint"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        words.VerticalAlignment = VerticalAlignment.Center;
        var box = new Panel { Children = { Kit20.Dashed(words, 14, new Thickness(12)) } };
        box.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width * 1.5 - box.Height) > 1) box.Height = Math.Round(e.NewSize.Width * 1.5); };
        box.Height = 225;
        var tile = new Border { Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Child = box, VerticalAlignment = VerticalAlignment.Top };
        StoreKit.OnClick(tile, () => MainWindow.Current?.Navigate(() => new AddGamePage()));
        return tile;
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
