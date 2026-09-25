using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Страница игры: шапка с запуском и две вкладки — установленные моды и каталог.</summary>
public sealed partial class GamePage : Page
{
    readonly GameState _g;
    string _tab;
    string _section = "all";
    string? _source;
    readonly Dictionary<string, long> _sourceTotals = [];
    string _query;
    SortBy _sort = SortBy.Popular;

    // Каталог грузится отдельно от перерисовки: перерисовка не должна его сбрасывать.
    readonly List<ModInfo> _mods = [];
    long _total;
    bool _hasMore, _loading;
    string? _error;
    int _page = 1;
    int _requestId;
    List<ModInfo>? _picks;

    StackPanel? _listHost;

    public GamePage(string gameId, string tab = "", string query = "")
    {
        // Своя игра могла быть убрана — тогда «назад» ведёт на первую игру.
        _g = AppState.Games.FirstOrDefault(g => g.Def.Id == gameId) ?? AppState.Games[0];
        _query = query;
        _tab = tab != "" ? tab : _g.ModCount > 0 || !_g.Def.HasCatalog ? "installed" : "catalog";
        if (_tab == "catalog" && !_g.Def.HasCatalog) _tab = "installed";
        if (_g.Def.Picks.Length > 0 && _query == "") _section = "picks";
        if (_tab == "catalog") _ = Load(reset: true);
    }

    public override string Title => _tab == "catalog" && _g.Status == Detect.Found ? I18n.T("games.market") : _g.Def.Name;
    public override string? GameId => _g.Def.Id;
    public override string SearchHint => I18n.T("search.game", ("game", _g.Def.Name));

    public override void Search(string text)
    {
        _query = text;
        _tab = "catalog";
        if (_section == "picks" || _section == "packs") _section = "all";
        _ = Load(reset: true);
        Build();
    }

    public override void Build()
    {
        var content = new StackPanel { Spacing = 18, Margin = new Thickness(34, 22, 34, 34), MaxWidth = 1180 };
        content.Children.Add(Header());
        if (_g.Status == Detect.Found)
        {
            content.Children.Add(Tabs());
            content.Children.Add(_tab switch
            {
                "catalog" => CatalogView(),
                "profiles" => ProfilesView(),
                "saves" => SavesView(),
                "log" => LogView(),
                "tools" => ToolsView(),
                _ => InstalledView(),
            });
        }
        else content.Children.Add(NotFoundView());
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // ---------------------------------------------------------------- шапка

    Control Header()
    {
        var art = Images.Game(_g.Def, 900);
        var status = _g.Status switch
        {
            Detect.Found when _g.Def.Loader == LoaderKind.None => (Ui.Res("Good"), I18n.T("add.noLoader", ("folder", _g.Def.ModsFolder))),
            Detect.Found when _g.LoaderInstalled => (Ui.Res("Good"), I18n.T("games.loaderReady", ("loader", _g.Def.LoaderName))),
            Detect.Found => (Ui.Res("Warn"), I18n.T("games.loaderMissing", ("loader", _g.Def.LoaderName))),
            Detect.Searching => (Ui.Res("Muted"), _g.SearchingWhere is null ? I18n.T("games.searching") : I18n.T("games.searchingWhere", ("where", _g.SearchingWhere))),
            _ => (Ui.Res("Faint"), I18n.T("games.notDetected")),
        };

        var info = Ui.Col(8,
            new TextBlock { Text = _g.Def.Name, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
            Ui.Row(8, Ui.Dot(status.Item1), Ui.Text(status.Item2, "small", color: Ui.Hex("#D5DAE5"))));
        if (_g.Path is not null)
        {
            var line = Ui.Row(16, Ui.Row(8, Ui.Icon(Icons.Folder, 13, Ui.Hex("#AAB2C2")), Ui.Text(ShortPath(_g.Path), "small", color: Ui.Hex("#AAB2C2"))));
            var played = Features.PlayTime.Get(_g.Def.Id);
            if (played.Running) line.Children.Add(Ui.Row(8, Ui.Icon(Icons.Clock, 13, Ui.Res("Good")), Ui.Text(I18n.T("time.running"), "small", color: Ui.Res("Good"))));
            else if (played.TotalMs > 0) line.Children.Add(Ui.Row(8, Ui.Icon(Icons.Clock, 13, Ui.Hex("#AAB2C2")), Ui.Text(I18n.T("time.total", ("time", Features.PlayTime.Format(played.TotalMs))), "small", color: Ui.Hex("#AAB2C2"))));
            info.Children.Add(line);
        }
        info.VerticalAlignment = VerticalAlignment.Center;

        var buttons = Ui.Row(10);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        if (_g.Status == Detect.Found)
        {
            buttons.Children.Add(Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(_g.Path), "", Icons.Folder));
            if (_g.LoaderInstalled)
            {
                var running = Features.Launcher.IsRunning(_g.Def.Id);
                var play = running
                    ? Ui.Button(I18n.T("v4.stop"), () => { Features.Launcher.Stop(_g.Def.Id); MainWindow.Current?.Toast(I18n.T("v4.stopped")); }, "primary", Icons.Stop)
                    : Ui.Button(I18n.T("games.play"), () => Actions.Play(_g), "primary", Icons.Play);
                if (running) play.Background = Ui.Hex("#E5484D");
                play.FontSize = 17;
                play.Padding = new Thickness(30, 13);
                buttons.Children.Add(play);
            }
            else
            {
                var busy = Jobs.All.Any(j => j.Status == JobStatus.Running && j.Title == _g.Def.LoaderName && j.GameName == _g.Def.Name);
                var install = Ui.Button(busy ? I18n.T("aside.installing") : I18n.T("games.installLoader", ("loader", _g.Def.LoaderName)), () => Actions.InstallLoader(_g), "primary", Icons.Download);
                install.IsEnabled = !busy;
                install.Padding = new Thickness(24, 13);
                buttons.Children.Add(install);
            }
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(26, 22) };
        grid.Children.Add(info);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return new Border
        {
            CornerRadius = new CornerRadius(20),
            ClipToBounds = true,
            BorderBrush = Ui.Res("Line"),
            BorderThickness = new Thickness(1),
            Height = 132,
            Child = new Panel
            {
                Children =
                {
                    art is null
                        ? new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(_g.Def.Accent), 0), new GradientStop(Color.Parse("#1A1206"), 1) } } }
                        : new Image { Source = art, Stretch = Stretch.UniformToFill },
                    new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#EB0F1116"), 0), new GradientStop(Color.Parse("#990F1116"), 0.6), new GradientStop(Color.Parse("#400F1116"), 1) } } },
                    grid,
                },
            },
        };
    }

    static string ShortPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? path : "…" + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, parts[^2..]);
    }

    Control Tabs()
    {
        Button Tab(string id, string text, string icon, string? badge)
        {
            var content = Ui.Row(8, Ui.Icon(icon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (badge is not null)
                content.Children.Add(new Border { Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), Child = Ui.Text(badge, "small") });
            var b = new Button { Classes = { "tab" }, Content = content };
            if (_tab == id) b.Classes.Add("active");
            b.Click += (_, _) => { if (_tab != id) MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, id)); };
            return b;
        }

        var updates = Features.ModUpdates.Found.TryGetValue(_g.Def.Id, out var found) && found.Count > 0 ? $" · ↑{found.Count}" : "";
        var profiles = Features.Profiles.List(_g.Def.Id).Count;
        var saves = Features.Backups.List(_g.Def.Id).Count;
        var bar = Ui.Row(4,
            Tab("installed", I18n.T("games.downloads"), Icons.List, _g.ModCount + updates),
            _g.Def.HasCatalog ? Tab("catalog", I18n.T("games.market"), Icons.Bag, _total > 0 ? I18n.Compact(_total) : null) : new Control { IsVisible = false },
            Tab("profiles", I18n.T("games.profiles"), Icons.Layers, profiles > 0 ? profiles.ToString() : null),
            Tab("saves", I18n.T("games.saves"), Icons.Shield, saves > 0 ? saves.ToString() : null),
            Tab("tools", I18n.T("v4.tools"), Icons.Settings, Features.Tools.For(_g.Def.Id).Count is > 0 and var t ? t.ToString() : null),
            Tab("log", I18n.T("games.log"), Icons.Alert, null));
        return new Border { Classes = { "card" }, Padding = new Thickness(6), CornerRadius = new CornerRadius(16), Child = bar, HorizontalAlignment = HorizontalAlignment.Left };
    }

    // ---------------------------------------------------------------- игра не найдена

    Control NotFoundView()
    {
        var searching = _g.Status == Detect.Searching;
        var col = Ui.Col(14,
            Ui.Text(I18n.T("games.notDetected"), "h2"),
            Ui.Text(I18n.T("games.notDetected.text", ("game", _g.Def.Name)), "muted", wrap: true));
        var buttons = Ui.Row(10,
            Ui.Button(I18n.T("games.setPath"), () => Actions.PickGameFolder(_g), "primary", Icons.Folder),
            Ui.Button(searching ? I18n.T("games.searching") : I18n.T("games.detectAgain"), () => _ = AppState.DetectOne(_g), "", Icons.Refresh),
            Ui.Button(I18n.T("games.deep"), () => _ = AppState.DetectOne(_g, deep: true), "ghost", Icons.Search));
        foreach (var b in buttons.Children.Skip(1)) b.IsEnabled = !searching;
        col.Children.Add(buttons);
        return Ui.Card(col, 26);
    }

    // ---------------------------------------------------------------- каталог

    Control CatalogView()
    {
        var col = new StackPanel { Spacing = 14 };

        // Источники: основной каталог и дополнительные (как в Vortex — моды с разных сайтов).
        var source = _source ?? _g.Def.PrimarySource;
        if (_g.Def.Sources.Length > 1)
        {
            if (_sourceTotals.Count < _g.Def.Sources.Length && !Program.Demo) _ = LoadSourceTotals();
            var sources = Ui.Row(8, Ui.Text(I18n.T("v4.source"), "small muted"));
            sources.Children[0].VerticalAlignment = VerticalAlignment.Center;
            foreach (var src in _g.Def.Sources)
            {
                var label = Catalog.Title(src) + (_sourceTotals.TryGetValue(src, out var n) ? $" · {n:N0}" : "");
                var b = Ui.Button(label, () => { _source = src; _section = "all"; _ = Load(reset: true); Build(); }, "chip");
                if (src == source) b.Classes.Add("active");
                sources.Children.Add(b);
            }
            col.Children.Add(sources);
        }

        var chips = new WrapPanel();
        var primarySource = source == _g.Def.PrimarySource;
        foreach (var s in _g.Def.Sections)
        {
            if (!primarySource && s.Id is not ("all" or "best")) continue;
            if (s.Id == "packs" && _g.Def.Kits.Length == 0 && _g.Def.Catalog != CatalogKind.Nexus) continue;
            var b = Ui.Button(I18n.T("sec." + s.Id), () => SelectSection(s.Id), "chip");
            if (_section == s.Id) b.Classes.Add("active");
            b.Margin = new Thickness(0, 0, 8, 8);
            chips.Children.Add(b);
        }
        col.Children.Add(chips);

        if (_section is not ("picks" or "packs"))
        {
            var search = new TextBox { Text = _query, Watermark = I18n.T("search.game", ("game", _g.Def.Name)), Height = 42 };
            search.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Search, 16, Ui.Res("Muted")) };
            if (_total > 0) search.InnerRightContent = new Border { Margin = new Thickness(0, 0, 10, 0), Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text(_total.ToString("N0"), "small muted") };
            search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _query = search.Text ?? ""; _ = Load(reset: true); } };

            var sort = new ComboBox { Width = 200, Height = 42 };
            var sorts = new[] { SortBy.Popular, SortBy.Rating, SortBy.Updated, SortBy.New };
            foreach (var s in sorts) sort.Items.Add(I18n.T("sort." + s.ToString().ToLowerInvariant()));
            sort.SelectedIndex = Array.IndexOf(sorts, _sort);
            sort.SelectionChanged += (_, _) =>
            {
                if (sort.SelectedIndex < 0 || sorts[sort.SelectedIndex] == _sort) return;
                _sort = sorts[sort.SelectedIndex];
                _ = Load(reset: true);
            };

            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            bar.Children.Add(search);
            Grid.SetColumn(sort, 1);
            bar.Children.Add(sort);
            col.Children.Add(bar);
        }

        _listHost = new StackPanel { Spacing = 10 };
        RenderList();
        col.Children.Add(_listHost);
        return col;
    }

    /// <summary>Открыть раздел каталога (для снимков экрана).</summary>
    public void ShowSection(string id) => SelectSection(id);

    void SelectSection(string id)
    {
        _section = id;
        if (id is not ("picks" or "packs")) _ = Load(reset: true);
        else _ = LoadPicks();
        Build();
    }

    bool IsInstalled(ModInfo mod) => Actions.IsInstalled(_g, mod.Id);
    bool IsInstalling(ModInfo mod) => Actions.IsBusy(_g, mod.Id);

    void RenderList()
    {
        if (_listHost is null) return;
        _listHost.Children.Clear();

        if (_section == "picks") { RenderPicks(); return; }
        if (_section == "packs") { RenderKits(); _listHost.Children.Add(CollectionsBlock()); return; }
        if (_section == "modpacks" && _g.Def.Catalog == CatalogKind.Thunderstore) _listHost.Children.Add(Ui.Card(Ui.Text(I18n.T("packs.intro"), "muted", wrap: true), 16));
        if (_section == "visuals") _listHost.Children.Add(ReShadeCard());

        if (_error is not null && _mods.Count == 0)
        {
            _listHost.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("catalog.error"), "h3"), Ui.Text(_error, "muted small", wrap: true),
                Ui.Button(I18n.T("common.retry"), () => _ = Load(reset: true), "primary", Icons.Refresh)), 22));
            return;
        }
        if (_loading && _mods.Count == 0)
        {
            for (var i = 0; i < 5; i++) _listHost.Children.Add(Skeleton());
            return;
        }
        if (!_loading && _mods.Count == 0)
        {
            _listHost.Children.Add(Ui.Card(Ui.Text(_query == "" ? I18n.T("inst.empty") : I18n.T("catalog.nothingFound", ("query", _query)), "muted"), 22));
            return;
        }

        var picks = _g.Def.Picks.ToHashSet();
        foreach (var mod in _mods)
            _listHost.Children.Add(ModRow.Build(_g.Def, mod, IsInstalled(mod), IsInstalling(mod), picks.Contains(mod.Id), () => _ = Actions.Install(_g, mod), () => MainWindow.Current?.Navigate(() => new ModPage(_g.Def.Id, mod))));

        if (_hasMore)
        {
            var more = Ui.Button(_loading ? I18n.T("catalog.loading") : I18n.T("catalog.more"), () => _ = Load(reset: false), "", Icons.Refresh);
            more.IsEnabled = !_loading;
            more.HorizontalAlignment = HorizontalAlignment.Center;
            _listHost.Children.Add(more);
        }
    }

    static Control Skeleton() => new Border
    {
        Classes = { "card" },
        Height = 118,
        Child = new Border { Width = 88, Height = 88, Margin = new Thickness(14), CornerRadius = new CornerRadius(14), Background = Ui.Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Left },
    };

    /// <summary>Сколько модов в каждом каталоге игры — для подписей у переключателя.</summary>
    async Task LoadSourceTotals()
    {
        foreach (var src in _g.Def.Sources)
        {
            if (_sourceTotals.ContainsKey(src)) continue;
            _sourceTotals[src] = 0;
            try { _sourceTotals[src] = (await Catalog.Browse(_g.Def, new Query(), source: src)).Total; } catch { }
        }
        if (_tab == "catalog") Build();
    }

    async Task Load(bool reset)
    {
        var id = ++_requestId;
        if (reset) { _mods.Clear(); _page = 1; _hasMore = false; }
        else _page++;
        _loading = true;
        _error = null;
        RenderList();
        try
        {
            var page = Program.Demo
                ? Demo.Catalog(_g.Def, new Query(_query, _page, _sort, _section))
                : await Catalog.Browse(_g.Def, new Query(_query, _page, _sort, _section), source: _source);
            if (id != _requestId) return;
            _mods.AddRange(page.Mods.Where(m => _mods.All(x => x.Id != m.Id)));
            _total = page.Total;
            _hasMore = page.HasMore;
        }
        catch (Exception e)
        {
            if (id != _requestId) return;
            _error = Jobs.Explain(e);
        }
        _loading = false;
        Build();
    }

    async Task LoadPicks()
    {
        if (_picks is not null) { RenderList(); return; }
        try
        {
            var ids = _g.Def.Picks.Concat(_g.Def.Kits.SelectMany(k => k.Mods));
            _picks = Program.Demo ? Demo.Many(_g.Def, ids) : await Catalog.Many(_g.Def, ids);
        }
        catch (Exception e) { _error = Jobs.Explain(e); _picks = []; }
        Build();
    }

    void RenderPicks()
    {
        if (_picks is null)
        {
            _ = LoadPicks();
            for (var i = 0; i < 4; i++) _listHost!.Children.Add(Skeleton());
            return;
        }
        var byId = _picks.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in _g.Def.Picks)
            if (byId.TryGetValue(id, out var mod))
                _listHost!.Children.Add(ModRow.Build(_g.Def, mod, IsInstalled(mod), IsInstalling(mod), true, () => _ = Actions.Install(_g, mod), () => MainWindow.Current?.Navigate(() => new ModPage(_g.Def.Id, mod))));
        if (_listHost!.Children.Count == 0)
            _listHost.Children.Add(Ui.Card(Ui.Text(_error ?? I18n.T("catalog.error"), "muted", wrap: true), 22));
    }

    void RenderKits()
    {
        if (_picks is null)
        {
            _ = LoadPicks();
            for (var i = 0; i < 3; i++) _listHost!.Children.Add(Skeleton());
            return;
        }
        var byId = _picks.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var kit in _g.Def.Kits)
        {
            var mods = kit.Mods.Select(id => byId.GetValueOrDefault(id)).OfType<ModInfo>().ToList();
            var missing = mods.Where(m => !IsInstalled(m)).ToList();
            var icons = Ui.Row(-10, mods.Take(7).Select(m => (Control)new Border { BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Child = Ui.Thumb(m.Icon, m.Name, 40, 10, 80) }).ToArray());
            var n = kit.Mods.Length;
            var info = Ui.Col(6,
                Ui.Text(I18n.T($"kit.{kit.Id}.title"), "h3"),
                Ui.Text(I18n.T($"kit.{kit.Id}.text"), "muted", wrap: true),
                Ui.Text(I18n.T("kit.mods." + I18n.Plural(n, "one", "few", "many"), ("n", n)), "small brand"),
                icons);
            var button = missing.Count == 0
                ? Ui.Button(I18n.T("kit.have"), () => { }, "", Icons.Check)
                : Ui.Button(I18n.T("mod.install"), () => InstallKit(kit, missing), "primary", Icons.Download);
            button.IsEnabled = missing.Count > 0;
            button.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
            grid.Children.Add(info);
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            _listHost!.Children.Add(Ui.Card(grid, 20));
        }
    }

    void InstallKit(Kit kit, List<ModInfo> missing)
    {
        var w = MainWindow.Current!;
        var title = I18n.T($"kit.{kit.Id}.title");
        w.Dialog(I18n.T("kit.confirm", ("name", title)),
            Ui.Text(I18n.T("kit.confirm.text", ("n", missing.Count)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mod.install"), () =>
            {
                w.CloseDialog();
                _ = Actions.InstallQueue(_g, title, missing.Select(m => (m, (Pin?)null)).ToList());
            }, "primary", Icons.Download));
    }
}
