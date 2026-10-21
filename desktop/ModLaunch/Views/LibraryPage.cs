using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Библиотека, как в Steam: обложки, коллекции («Избранное», свои, скрытые),
/// сортировка (недавние, по названию, по времени в игре). Сначала игры с
/// компьютера, ниже — остальные поддерживаемые.
/// </summary>
public sealed partial class LibraryPage : Page
{
    string _query = "";
    string _collection = "all"; // all | ★ | hidden | имя коллекции
    string _sort = Settings.Data.Str("librarySort") ?? "recent";
    bool _showOther = Settings.Data.Bool("libraryShowOther", true);

    public override string Title => I18n.T("lib.title");
    public override string SearchHint => I18n.T("lib.search");
    public override void Search(string text) { _query = text.Trim(); Build(); }
    public override Control? Aside() => CollectionsAside();

    Control Intro(Control c, int index) { if (!Shown) Animate.Rise(c, index); return c; }

    IEnumerable<GameState> Sorted(IEnumerable<GameState> games) => _sort switch
    {
        "name" => games.OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase),
        "time" => games.OrderByDescending(g => PlayTime.Get(g.Def.Id).TotalMs).ThenBy(g => g.Def.Name),
        _ => games.OrderByDescending(g => PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).ThenBy(g => g.Def.Name),
    };

    bool InCollection(GameState g) => _collection switch
    {
        "all" => !GameCollections.IsHidden(g.Def.Id),
        "hidden" => GameCollections.IsHidden(g.Def.Id),
        _ => GameCollections.Has(_collection, g.Def.Id),
    };

    public override void Build()
    {
        // 9.2: в дизайне Store — библиотека как в Microsoft Store (LibraryPage.Store.cs).
        if (Look.Store) { BuildStore(); AddSupportedLink(); return; }
        var content = new StackPanel { Spacing = 20, Margin = new Thickness(32, 26, 32, 32), MaxWidth = 1760 };
        bool Match(GameState g) => (_query == "" || g.Def.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)) && InCollection(g);
        var all = MainWindow.OrderedGames().ToList();
        var installed = Sorted(all.Where(g => g.Status == Detect.Found && Match(g))).ToList();
        var other = Sorted(all.Where(g => g.Status != Detect.Found && Match(g))).ToList();

        // Шапка: коллекции чипами и сортировка — как полка библиотеки Steam.
        var chips = new WrapPanel();
        Button Chip(string id, string text, string? icon = null)
        {
            var b = Ui.Button(text, () => { _collection = id; Build(); MainWindow.Current?.RenderAside(); }, _collection == id ? "chip active" : "chip", icon);
            b.Margin = new Thickness(0, 0, 8, 8);
            return b;
        }
        chips.Children.Add(Chip("all", I18n.T("lib.all")));
        chips.Children.Add(Chip(GameCollections.Favorites, I18n.T("lib.favorites"), Icons.Star));
        foreach (var name in GameCollections.Names()) chips.Children.Add(Chip(name, name, Icons.Layers));
        if (GameCollections.HiddenCount > 0) chips.Children.Add(Chip("hidden", I18n.T("lib.hidden", ("n", GameCollections.HiddenCount)), Icons.EyeOff));
        var newCollection = Ui.Button(I18n.T("lib.newCollection"), NewCollection, "chip", Icons.Plus);
        newCollection.Margin = new Thickness(0, 0, 8, 8);
        chips.Children.Add(newCollection);

        var sorts = new[] { "recent", "name", "time" };
        var sort = new ComboBox { Width = 190 };
        foreach (var s in sorts) sort.Items.Add(I18n.T("lib.sort." + s));
        sort.SelectedIndex = Math.Max(0, Array.IndexOf(sorts, _sort));
        sort.SelectionChanged += (_, _) =>
        {
            if (sort.SelectedIndex < 0) return;
            _sort = sorts[sort.SelectedIndex];
            Settings.Data["librarySort"] = _sort;
            Settings.Save();
            Build();
        };
        var add = Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "", Icons.Plus);
        var tools = Ui.Row(10, sort, add);
        tools.VerticalAlignment = VerticalAlignment.Top;
        var head = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        head.Children.Add(chips);
        content.Children.Add(head);

        content.Children.Add(Ui.Row(10, Ui.Text(_collection == "all" ? I18n.T("lib.found") : ChipTitle(), "h2"), Ui.Text(installed.Count.ToString(), "h2", color: Ui.Res("Faint"))));
        var grid = new WrapPanel();
        var n = 0;
        foreach (var g in installed) grid.Children.Add(Intro(GameCard.Cover(g, 136), n++));
        if (_collection == "all") grid.Children.Add(Intro(GameCard.AddCover(136), n++));
        if (installed.Count == 0 && _collection != "all") content.Children.Add(Ui.Text(I18n.T("lib.emptyCollection"), "muted", wrap: true));
        content.Children.Add(grid);
        if (_collection == "all" && SupportedLink() is { } link) content.Children.Add(link);

        if (other.Count > 0)
        {
            var toggle = Ui.Button(_showOther ? I18n.T("lib.hideOther") : I18n.T("lib.showOther"), () =>
            {
                _showOther = !_showOther;
                Settings.Data["libraryShowOther"] = _showOther;
                Settings.Save();
                Build();
            }, "ghost");
            var otherHead = new DockPanel();
            DockPanel.SetDock(toggle, Dock.Right);
            otherHead.Children.Add(toggle);
            otherHead.Children.Add(Ui.Col(2,
                Ui.Row(10, Ui.Text(I18n.T("lib.other"), "h2"), Ui.Text(other.Count.ToString(), "h2", color: Ui.Res("Faint"))),
                Ui.Text(I18n.T("lib.other.hint"), "small muted")));
            content.Children.Add(otherHead);
            if (_showOther)
            {
                var more = new WrapPanel();
                foreach (var g in other) more.Children.Add(Intro(GameCard.Cover(g, 116), n++));
                content.Children.Add(more);
            }
        }
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>
    /// 12.0: «+ N поддерживаемых игр» — ссылка на справочник в «Добавить игру». С набранным поиском —
    /// туда же с тем же текстом: игры нет в библиотеке, но она может быть в справочнике.
    /// </summary>
    Control? SupportedLink()
    {
        var query = _query;
        var row = new WrapPanel();
        var more = Games.CustomGames.MoreSupported;
        if (more > 0)
        {
            var title = query != ""
                ? I18n.T("v12g.lib.find", ("query", query))
                : I18n.T("v12g.lib.more." + I18n.Plural(more, "one", "few", "many"), ("n", more.ToString("N0", I18n.Culture)));
            row.Children.Add(Link(query != "" ? Icons.Search : Icons.Plus, title, I18n.T("v12g.lib.more.hint"), () => MainWindow.Current?.Navigate(() => new AddGamePage(query))));
        }
        // Все игры Steam — и без модов: играть через Steam, искать по всему магазину.
        var steam = Games.SteamLibrary.Installed.Count;
        if (steam > 0)
            row.Children.Add(Link(Icons.Layers, I18n.T("v12w.steam.link") + " · " + steam.ToString("N0", I18n.Culture), I18n.T("v12w.steam.link.hint"), () => MainWindow.Current?.Navigate(() => new SteamGamesPage(query))));
        return row.Children.Count == 0 ? null : row;
    }

    static Button Link(string glyph, string title, string hint, Action open)
    {
        var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Ui.Res("Surface3"), Child = Ui.Icon(glyph, 16, Ui.Res("Brand2")) };
        var button = new Button
        {
            // Отступ влево — значок встаёт по краю обложек, а подсветка кнопки остаётся вокруг него.
            Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 8, 16, 8), Margin = new Thickness(-10, 0, 18, 0),
            Content = Ui.Row(12, icon, Ui.Col(1, Ui.Text(title, "h3"), Ui.Text(hint, "small muted")), Ui.Icon(Icons.ChevronRight, 16, Ui.Res("Muted"))),
        };
        button.Click += (_, _) => open();
        return button;
    }

    /// <summary>
    /// В дизайне Store ссылка встаёт под играми с компьютера: разметку строит LibraryPage.Store.cs,
    /// здесь — только вставка в готовую колонку (шапка, фильтры, найденные игры — первые три строки).
    /// </summary>
    void AddSupportedLink()
    {
        if (_collection is not ("all" or "found") || Content is not ScrollViewer { Content: Panel col } || SupportedLink() is not { } link) return;
        col.Children.Insert(Math.Min(3, col.Children.Count), link);
    }

    string ChipTitle() => _collection switch
    {
        GameCollections.Favorites => I18n.T("lib.favorites"),
        "hidden" => I18n.T("lib.hidden", ("n", GameCollections.HiddenCount)),
        _ => _collection,
    };

    void NewCollection()
    {
        var w = MainWindow.Current!;
        var box = new TextBox { Watermark = I18n.T("lib.newCollection.hint"), MaxLength = 40 };
        void Create()
        {
            var name = (box.Text ?? "").Trim();
            if (name == "") return;
            GameCollections.Create(name);
            _collection = name;
            w.CloseDialog();
            Build();
            w.RenderAside();
        }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Create(); };
        w.Dialog(I18n.T("lib.newCollection"), Ui.Col(10, Ui.Text(I18n.T("lib.newCollection.text"), "muted", wrap: true), box),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"), Ui.Button(I18n.T("lib.create"), Create, "primary", Icons.Plus));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => box.Focus());
    }

    Control CollectionsAside()
    {
        var list = Ui.Col(4);
        void Row(string id, string title, string icon, int count)
        {
            var b = new Button
            {
                Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6),
                Content = new DockPanel { Children = { Right(Ui.Text(count.ToString(), "small muted")), Ui.Row(10, Ui.Icon(icon, 15, _collection == id ? Ui.Res("Brand2") : Ui.Res("Muted")), Ui.Text(title, _collection == id ? "h3" : "")) } },
            };
            b.Click += (_, _) => { _collection = id; Build(); MainWindow.Current?.RenderAside(); };
            list.Children.Add(b);
        }
        static Control Right(Control c) { DockPanel.SetDock(c, Dock.Right); c.VerticalAlignment = VerticalAlignment.Center; return c; }
        Row("all", I18n.T("lib.all"), Icons.Grid, AppState.Games.Count(g => !GameCollections.IsHidden(g.Def.Id)));
        Row(GameCollections.Favorites, I18n.T("lib.favorites"), Icons.Star, GameCollections.Games(GameCollections.Favorites).Count);
        foreach (var name in GameCollections.Names()) Row(name, name, Icons.Layers, GameCollections.Games(name).Count);
        if (GameCollections.HiddenCount > 0) Row("hidden", I18n.T("lib.hidden", ("n", GameCollections.HiddenCount)), Icons.EyeOff, GameCollections.HiddenCount);
        var col = Ui.Col(16, Views.Aside.Section(I18n.T("lib.collections"), Icons.Layers, list, Ui.Text(I18n.T("lib.collections.hint"), "small muted", wrap: true)));
        if (_collection is not ("all" or GameCollections.Favorites or "hidden"))
        {
            var name = _collection;
            col.Children.Add(Ui.Button(I18n.T("lib.deleteCollection"), () => { GameCollections.Delete(name); _collection = "all"; Build(); MainWindow.Current?.RenderAside(); }, "ghost", Icons.Trash));
        }
        return col;
    }
}
