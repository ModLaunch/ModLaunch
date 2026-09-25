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
public sealed class GamePage : Page
{
    readonly GameState _g;
    string _tab;
    string _section = "all";
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
        _g = AppState.Game(gameId);
        _query = query;
        _tab = tab != "" ? tab : _g.ModCount > 0 ? "installed" : "catalog";
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
            content.Children.Add(_tab == "catalog" ? CatalogView() : InstalledView());
        }
        else content.Children.Add(NotFoundView());
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // ---------------------------------------------------------------- шапка

    Control Header()
    {
        var art = _g.Def.Art is null ? null : Images.Asset(_g.Def.Art, 900);
        var status = _g.Status switch
        {
            Detect.Found when _g.LoaderInstalled => (Ui.Res("Good"), I18n.T("games.loaderReady", ("loader", _g.Def.LoaderName))),
            Detect.Found => (Ui.Res("Warn"), I18n.T("games.loaderMissing", ("loader", _g.Def.LoaderName))),
            Detect.Searching => (Ui.Res("Muted"), _g.SearchingWhere is null ? I18n.T("games.searching") : I18n.T("games.searchingWhere", ("where", _g.SearchingWhere))),
            _ => (Ui.Res("Faint"), I18n.T("games.notDetected")),
        };

        var info = Ui.Col(8,
            new TextBlock { Text = _g.Def.Name, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
            Ui.Row(8, Ui.Dot(status.Item1), Ui.Text(status.Item2, "small", color: Ui.Hex("#D5DAE5"))));
        if (_g.Path is not null)
            info.Children.Add(Ui.Row(8, Ui.Icon(Icons.Folder, 13, Ui.Hex("#AAB2C2")), Ui.Text(ShortPath(_g.Path), "small", color: Ui.Hex("#AAB2C2"))));
        info.VerticalAlignment = VerticalAlignment.Center;

        var buttons = Ui.Row(10);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        if (_g.Status == Detect.Found)
        {
            buttons.Children.Add(Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(_g.Path), "", Icons.Folder));
            if (_g.LoaderInstalled)
            {
                var play = Ui.Button(I18n.T("games.play"), () => Actions.Play(_g), "primary", Icons.Play);
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

        var bar = Ui.Row(4,
            Tab("installed", I18n.T("games.downloads"), Icons.List, _g.ModCount.ToString()),
            Tab("catalog", I18n.T("games.market"), Icons.Bag, _total > 0 ? I18n.Compact(_total) : null));
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

    // ---------------------------------------------------------------- установленные

    Control InstalledView()
    {
        var registry = _g.Registry!;
        var col = new StackPanel { Spacing = 10 };

        var head = new DockPanel();
        var actions = Ui.Row(8,
            Ui.Button(I18n.T("inst.fromFile"), () => Actions.InstallFromFile(_g), "", Icons.FilePlus),
            Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(registry.ModsDir), "", Icons.Folder));
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(Ui.Text(I18n.T("inst.title"), "h2"));
        col.Children.Add(head);

        var problems = Mods.Installer.CheckDependencies(registry);
        if (problems.Count > 0)
            col.Children.Add(Notice(Icons.Alert, I18n.T("inst.problems", ("list", string.Join(", ", problems.Select(p => p.Missing).Distinct()))), Ui.Res("Warn")));
        var unmanaged = registry.Unmanaged();
        if (unmanaged.Count > 0)
            col.Children.Add(Notice(Icons.Package, I18n.T("inst.unmanaged", ("list", string.Join(", ", unmanaged.Take(6)))), Ui.Res("Muted")));

        var mods = registry.List();
        if (mods.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Text(I18n.T("inst.empty"), "h3"),
                Ui.Text(I18n.T("dl.empty.text"), "muted", wrap: true),
                Ui.Button(I18n.T("games.market"), () => { _tab = "catalog"; _ = Load(reset: true); Build(); }, "primary", Icons.Bag)), 24));
            return col;
        }

        foreach (var mod in mods) col.Children.Add(InstalledRow(registry, mod));
        return col;
    }

    static Control Notice(string icon, string text, IBrush color) => new Border
    {
        Background = Ui.Res("Surface"),
        BorderBrush = color,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14, 10),
        Child = Ui.Row(10, Ui.Icon(icon, 16, color), Ui.Text(text, "small", wrap: true)),
    };

    Control InstalledRow(Mods.ModRegistry registry, JsonObject mod)
    {
        var id = mod.Str("id")!;
        var name = mod.Str("name") ?? id;
        var enabled = mod.Bool("enabled", true);
        var missing = mod.Bool("missing");

        var sub = new List<string>();
        if (mod.Str("version") is { Length: > 0 } v) sub.Add(I18n.T("mod.version", ("version", v)));
        if (mod.Str("author") is { Length: > 0 } a) sub.Add(a);
        if (DateTime.TryParse(mod.Str("installedAt"), out var at)) sub.Add(Ui.Ago(at.ToUniversalTime()));

        var title = Ui.Row(8, Ui.Text(name, "h3"));
        if (missing) title.Children.Add(ModRow.Tag(I18n.T("inst.missing"), Ui.Hex("#3A1A1A"), Ui.Res("Bad")));
        else if (!enabled) title.Children.Add(ModRow.Tag(I18n.T("inst.off"), Ui.Res("Surface3"), Ui.Res("Muted")));
        var middle = Ui.Col(4, title, Ui.Text(string.Join(" · ", sub), "small muted"));
        middle.VerticalAlignment = VerticalAlignment.Center;

        var toggle = new ToggleSwitch { IsChecked = enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center, IsEnabled = !missing };
        ToolTip.SetTip(toggle, enabled ? I18n.T("mod.disable") : I18n.T("mod.enable"));
        toggle.IsCheckedChanged += (_, _) =>
        {
            try
            {
                registry.SetEnabled(id, toggle.IsChecked == true);
                MainWindow.Current?.Toast(I18n.T(toggle.IsChecked == true ? "toast.enabled" : "toast.disabled"));
            }
            catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
            Build();
        };

        var remove = Ui.Button("", () => ConfirmRemove(registry, id, name), "icon ghost", Icons.Trash, I18n.T("mod.remove"));
        var right = Ui.Row(6, toggle, remove);
        if (mod.Str("url") is string url) right.Children.Insert(0, Ui.Button("", () => Ui.OpenUrl(url), "icon ghost", Icons.External, I18n.T("mod.page")));
        right.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(Ui.Thumb(mod.Str("icon"), name, 48, 12, 96));
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        var card = new Border { Classes = { "card" }, Padding = new Thickness(12), Child = grid };
        if (!enabled || missing) card.Opacity = 0.7;
        return card;
    }

    void ConfirmRemove(Mods.ModRegistry registry, string id, string name)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("mod.removeConfirm", ("name", name)),
            Ui.Text(I18n.T("mod.removeConfirm.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mod.remove"), () =>
            {
                w.CloseDialog();
                try { registry.Remove(id); w.Toast(I18n.T("toast.removed")); }
                catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
                AppState.Notify();
            }, "primary", Icons.Trash));
    }

    // ---------------------------------------------------------------- каталог

    Control CatalogView()
    {
        var col = new StackPanel { Spacing = 14 };

        var chips = new WrapPanel();
        foreach (var s in _g.Def.Sections)
        {
            if (s.Id == "packs" && _g.Def.Kits.Length == 0) continue;
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

    void SelectSection(string id)
    {
        _section = id;
        if (id is not ("picks" or "packs")) _ = Load(reset: true);
        else _ = LoadPicks();
        Build();
    }

    bool IsInstalled(ModInfo mod) => _g.Registry?.Get(mod.Id) is { } r && !r.Bool("missing");
    bool IsInstalling(ModInfo mod) => Actions.Installing.Contains($"{_g.Def.Id}/{mod.Id}");

    void RenderList()
    {
        if (_listHost is null) return;
        _listHost.Children.Clear();

        if (_section == "picks") { RenderPicks(); return; }
        if (_section == "packs") { RenderKits(); return; }

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
            _listHost.Children.Add(ModRow.Build(_g.Def, mod, IsInstalled(mod), IsInstalling(mod), picks.Contains(mod.Id), () => Actions.Install(_g, mod)));

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
                : await Catalog.Browse(_g.Def, new Query(_query, _page, _sort, _section));
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
                _listHost!.Children.Add(ModRow.Build(_g.Def, mod, IsInstalled(mod), IsInstalling(mod), true, () => Actions.Install(_g, mod)));
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
        w.Dialog(I18n.T("kit.confirm", ("name", I18n.T($"kit.{kit.Id}.title"))),
            Ui.Text(I18n.T("kit.confirm.text", ("n", missing.Count)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mod.install"), () =>
            {
                w.CloseDialog();
                foreach (var mod in missing) Actions.Install(_g, mod);
            }, "primary", Icons.Download));
    }
}
