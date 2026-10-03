using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Библиотека Modrinth из 8.0 (установка прямо в .minecraft). С 8.5 её заменила страница Minecraft
/// (сборки, загрузчики, каталог под сборку) — сюда больше не ведёт ни одна ссылка; оставлена для совместимости.
/// </summary>
public sealed class ModrinthPage : Page
{
    public override string Title => "Modrinth";
    public override string SearchHint => I18n.T("mr.search");
    public override string? Accent => "#1BD96A";

    string _tab = "browse";
    string _type = "mod";
    string _sort = "relevance";
    string _text = "";
    readonly List<MrProject> _hits = [];
    long _total;
    bool _loading;
    string? _error;
    List<string>? _versions;
    readonly HashSet<string> _busy = [];
    List<(MrInstalled Item, MrVersion Latest)>? _updates;
    CancellationTokenSource? _cts;

    public ModrinthPage(string tab = "browse") { _tab = tab; _ = Load(); _ = LoadVersions(); }

    public override void Search(string text) { _text = text.Trim(); _tab = "browse"; _ = Load(); }

    async Task LoadVersions()
    {
        try { _versions = await Modrinth.GameVersions(); } catch { _versions = []; }
        Build();
    }

    async Task Load(bool more = false)
    {
        if (Program.Screenshot) { Build(); return; }
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _loading = true;
        _error = null;
        if (!more) _hits.Clear();
        Build();
        try
        {
            var (hits, total) = await Modrinth.Search(_text, _type, _sort, more ? _hits.Count : 0, cts.Token);
            if (cts.IsCancellationRequested) return;
            _hits.AddRange(hits);
            _total = total;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { _error = Jobs.Explain(e); }
        _loading = false;
        Build();
    }

    /// <summary>Для скриншотов: готовая выдача без сети.</summary>
    public void Demo(List<MrProject> hits) { _hits.Clear(); _hits.AddRange(hits); _total = hits.Count; _loading = false; Build(); }

    public override void Build()
    {
        var col = new StackPanel { Spacing = 18, Margin = new Thickness(40, 26, 40, 40), MaxWidth = 1640 };

        var logo = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(16), Background = Ui.Hex("#1BD96A"), Child = Ui.Icon(Icons.Package, 28, Brushes.Black) };
        var words = Ui.Col(3, Ui.Text("Modrinth", "h1"), Ui.Text(I18n.T("mr.subtitle"), "muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        col.Children.Add(Ui.Row(16, logo, words));

        var tabs = new List<Control>();
        foreach (var (id, key, icon) in new[] { ("browse", "mr.tab.browse", Icons.Globe), ("installed", "mr.tab.installed", Icons.Check), ("settings", "mr.tab.settings", Icons.Settings) })
        {
            var t = id;
            var b = Ui.Button(I18n.T(key, ("n", Modrinth.Installed().Count)), () => { _tab = t; Build(); }, "tab", icon);
            if (_tab == id) b.Classes.Add("active");
            tabs.Add(b);
        }
        col.Children.Add(Ui.TabBar(tabs.ToArray()));

        col.Children.Add(_tab switch
        {
            "installed" => InstalledView(),
            "settings" => SettingsView(),
            _ => Browse(),
        });
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // ---------------------------------------------------------------- каталог

    Control Filters()
    {
        var types = Ui.Row(6);
        foreach (var type in Modrinth.Types)
        {
            var t = type;
            var chip = Ui.Button(I18n.T("mr.type." + type), () => { _type = t; _ = Load(); }, "chip");
            if (_type == type) chip.Classes.Add("active");
            types.Children.Add(chip);
        }
        var loader = new ComboBox { Width = 130, ItemsSource = Modrinth.Loaders.Select(l => char.ToUpperInvariant(l[0]) + l[1..]).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(Modrinth.Loaders, Modrinth.Loader)) };
        loader.IsEnabled = _type is "mod" or "modpack";
        loader.SelectionChanged += (_, _) => { var v = Modrinth.Loaders[Math.Max(0, loader.SelectedIndex)]; if (v != Modrinth.Loader) { Modrinth.Loader = v; _ = Load(); } };
        var versions = new List<string> { "" };
        versions.AddRange(_versions ?? []);
        if (Modrinth.GameVersion != "" && !versions.Contains(Modrinth.GameVersion)) versions.Add(Modrinth.GameVersion);
        var version = new ComboBox { Width = 170, ItemsSource = versions.Select(v => v == "" ? I18n.T("mr.anyVersion") : v).ToList(), SelectedIndex = Math.Max(0, versions.IndexOf(Modrinth.GameVersion)), MaxDropDownHeight = 420 };
        version.SelectionChanged += (_, _) => { var v = versions[Math.Max(0, version.SelectedIndex)]; if (v != Modrinth.GameVersion) { Modrinth.GameVersion = v; _ = Load(); } };
        var sort = new ComboBox { Width = 170, ItemsSource = Modrinth.Sorts.Select(s => I18n.T("mr.sort." + s)).ToList(), SelectedIndex = Array.IndexOf(Modrinth.Sorts, _sort) };
        sort.SelectionChanged += (_, _) => { var v = Modrinth.Sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _sort) { _sort = v; _ = Load(); } };
        var right = Ui.Row(8, loader, version, sort, Ui.Button("", () => _ = Load(), "icon ghost", Icons.Refresh, I18n.T("cr.refresh")));
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        types.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(types);
        return bar;
    }

    Control Browse()
    {
        var col = new StackPanel { Spacing = 16 };
        col.Children.Add(Filters());
        if (_error is not null) col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("mr.error"), "h3"), Ui.Text(_error, "muted", wrap: true),
            Ui.Button(I18n.T("cr.refresh"), () => _ = Load(), "primary", Icons.Refresh)), 22));
        var summary = _text == "" ? I18n.T("mr.total", ("n", I18n.Compact(_total))) : I18n.T("mr.found", ("n", I18n.Compact(_total)), ("q", _text));
        if (_hits.Count > 0 || !_loading) col.Children.Add(Ui.Text(summary, "small muted"));

        var grid = new UniformGrid { Columns = 3 };
        foreach (var p in _hits) grid.Children.Add(Card(p));
        if (_loading) for (var i = 0; i < (_hits.Count == 0 ? 9 : 3); i++) grid.Children.Add(new Border { Classes = { "card", "skeleton" }, Height = 150, Margin = new Thickness(0, 0, 14, 14) });
        col.Children.Add(grid);
        if (!_loading && _hits.Count == 0 && _error is null) col.Children.Add(Ui.Text(I18n.T("mr.empty"), "muted"));
        if (!_loading && _hits.Count < _total)
        {
            var more = Ui.Button(I18n.T("mr.more"), () => _ = Load(more: true), "", Icons.ChevronDown);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(more);
        }
        return col;
    }

    Control Card(MrProject p)
    {
        var installed = Modrinth.InstalledOf(p.Id);
        var icon = Ui.Thumb(p.Icon, p.Title, 64, 14, 128);
        var install = Ui.Button(_busy.Contains(p.Id) ? I18n.T("mr.installing") : installed is not null ? I18n.T("mr.installed") : I18n.T("mr.install"),
            () => Install(p), installed is not null ? "ghost" : "primary", installed is not null ? Icons.Check : Icons.Download);
        install.IsEnabled = !_busy.Contains(p.Id) && installed is null;
        var cats = new WrapPanel();
        foreach (var c in p.Categories.Take(4))
            cats.Children.Add(new Border { Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 5, 5), Child = new TextBlock { Text = c, FontSize = 11, Foreground = Ui.Res("Muted") } });
        var head = Ui.Col(3, Ui.Text(p.Title, "h3"), Ui.Text(I18n.T("mod.by", ("author", p.Author)), "small brand"));
        head.VerticalAlignment = VerticalAlignment.Center;
        var body = Ui.Col(8,
            Ui.Row(12, icon, head),
            new TextBlock { Text = p.Description, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Muted"), FontSize = 13, Height = 36 },
            cats);
        var foot = new DockPanel();
        DockPanel.SetDock(install, Dock.Right);
        foot.Children.Add(install);
        foot.Children.Add(Ui.Row(12,
            Ui.Row(4, Ui.Icon(Icons.Download, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(p.Downloads), "small muted")),
            Ui.Row(4, Ui.Icon(Icons.Heart, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(p.Follows), "small muted")),
            Ui.Text(Ui.Ago(p.Updated), "small muted")));
        foot.Children[^1].VerticalAlignment = VerticalAlignment.Center;
        body.Children.Add(foot);
        var card = new Button
        {
            Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = body,
        };
        card.Click += (_, _) => Details(p);
        return Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(I18n.T("ctx.open"), Icons.Eye, () => Details(p)),
            installed is null ? Ctx.Item(I18n.T("mr.install"), Icons.Download, () => Install(p), !_busy.Contains(p.Id)) : Ctx.Item(I18n.T("mr.remove"), Icons.Trash, () => Remove(installed)),
            Ctx.Item(I18n.T("mr.versions"), Icons.Layers, () => Details(p, versions: true)),
            "-",
            Ctx.Link(I18n.T("ctx.openSite"), p.Url),
            Ctx.Copy(I18n.T("ctx.copyLink"), p.Url),
            Ctx.Copy(I18n.T("ctx.copyName"), p.Title),
            Ctx.Copy(I18n.T("ctx.copyId"), p.Id),
            "-",
            Ctx.Folder(I18n.T("mr.openFolder"), Directory.CreateDirectory(Path.Combine(Modrinth.Folder, Modrinth.SubFolder(p.Type))).FullName)));
    }

    async void Install(MrProject p, MrVersion? v = null)
    {
        var w = MainWindow.Current!;
        _busy.Add(p.Id);
        Build();
        try
        {
            var done = await Modrinth.Install(p, v);
            w.Toast(done.Count > 1 ? I18n.T("mr.installedMany", ("title", p.Title), ("n", done.Count - 1)) : I18n.T("mr.installedOne", ("title", p.Title)));
        }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
        _busy.Remove(p.Id);
        Build();
    }

    void Remove(MrInstalled i)
    {
        Modrinth.Remove(i);
        MainWindow.Current?.Toast(I18n.T("mr.removed", ("title", i.Title)));
        _updates?.RemoveAll(u => u.Item.ProjectId == i.ProjectId);
        Build();
    }

    async void Details(MrProject p, bool versions = false)
    {
        var w = MainWindow.Current!;
        var body = Ui.Col(12, Ui.Text(p.Description, "muted", wrap: true), Ui.Text(I18n.T("common.loading"), "small muted"));
        var list = Ui.Col(6);
        w.Dialog(p.Title, new ScrollViewer { MaxHeight = 560, Content = Ui.Col(14, body, list) }, 760,
            Ui.Button(I18n.T("ctx.openSite"), () => Ui.OpenUrl(p.Url), "ghost", Icons.External),
            Ui.Button(I18n.T("common.close"), w.CloseDialog),
            Ui.Button(I18n.T("mr.install"), () => { w.CloseDialog(); Install(p); }, "primary", Icons.Download));
        try
        {
            var (_, text, gallery) = await Modrinth.Project(p.Id);
            body.Children.RemoveAt(1);
            if (gallery.Count > 0)
            {
                var shots = Ui.Row(8);
                foreach (var g in gallery.Take(4)) shots.Children.Add(new Border { Width = 170, Height = 96, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.Thumb(g, p.Title, 170, 10, 340) });
                body.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = shots });
            }
            if (!versions)
            {
                var plain = System.Text.RegularExpressions.Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)|<[^>]+>", "").Trim();
                body.Children.Add(new SelectableTextBlock { Text = plain.Length > 4000 ? plain[..4000] + "…" : plain, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
            }
            var all = await Modrinth.Versions(p.Id, p.Type);
            list.Children.Add(Ui.Text(all.Count == 0 ? I18n.T("mr.noMatching") : I18n.T("mr.versionsFor", ("loader", Modrinth.Loader), ("version", Modrinth.GameVersion == "" ? "*" : Modrinth.GameVersion)), "h3"));
            foreach (var v in all.Take(25))
            {
                var vv = v;
                var line = new DockPanel();
                var go = Ui.Button(I18n.T("mr.install"), () => { w.CloseDialog(); Install(p, vv); }, "ghost", Icons.Download);
                DockPanel.SetDock(go, Dock.Right);
                line.Children.Add(go);
                line.Children.Add(Ui.Col(2, Ui.Text($"{v.Number}  ·  {v.Name}", ""),
                    Ui.Text($"{v.Type} · {string.Join(", ", v.Loaders)} · {string.Join(", ", v.GameVersions.TakeLast(4))} · {Ui.Ago(v.Published)}", "small muted")));
                list.Children.Add(Ui.Card(line, 10));
            }
        }
        catch (Exception e) { body.Children.Add(Ui.Text(Jobs.Explain(e), "small", color: Ui.Res("Bad"), wrap: true)); }
    }

    // ---------------------------------------------------------------- установленные

    Control InstalledView()
    {
        var col = new StackPanel { Spacing = 12 };
        var items = Modrinth.Installed();
        var check = Ui.Button(I18n.T("mr.checkUpdates"), async () =>
        {
            _updates = null;
            Build();
            _updates = await Modrinth.Updates();
            MainWindow.Current?.Toast(_updates.Count == 0 ? I18n.T("mr.upToDate") : I18n.T("mr.updatesFound", ("n", _updates.Count)));
            Build();
        }, "", Icons.Refresh);
        var all = Ui.Button(I18n.T("mr.updateAll"), async () =>
        {
            foreach (var (item, latest) in _updates ?? [])
            {
                try { await Modrinth.Install(new MrProject(item.ProjectId, "", item.Title, "", "", item.Icon, 0, 0, item.Type, [], [], [], null, null), latest); }
                catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
            }
            _updates = [];
            MainWindow.Current?.Toast(I18n.T("mr.updated"));
            Build();
        }, "primary", Icons.ArrowUp);
        all.IsVisible = _updates is { Count: > 0 };
        col.Children.Add(Ui.Row(10, check, all, Ui.Button(I18n.T("mr.openFolder"), () => Actions.OpenFolder(Directory.CreateDirectory(Modrinth.Folder).FullName), "ghost", Icons.Folder)));
        if (items.Count == 0) { col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("mr.noneInstalled"), "h3"), Ui.Text(I18n.T("mr.noneInstalled.text"), "muted", wrap: true)), 24)); return col; }
        foreach (var group in items.GroupBy(i => i.Type))
        {
            col.Children.Add(Ui.Text(I18n.T("mr.type." + group.Key) + $" · {group.Count()}", "h3"));
            foreach (var i in group)
            {
                var item = i;
                var update = _updates?.FirstOrDefault(u => u.Item.ProjectId == i.ProjectId);
                var right = Ui.Row(6);
                if (update is { Latest: { } latest })
                    right.Children.Add(Ui.Button(I18n.T("upd.to", ("version", latest.Number)), async () =>
                    {
                        try { await Modrinth.Install(new MrProject(item.ProjectId, "", item.Title, "", "", item.Icon, 0, 0, item.Type, [], [], [], null, null), latest); _updates?.RemoveAll(u => u.Item.ProjectId == item.ProjectId); }
                        catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
                        Build();
                    }, "primary", Icons.ArrowUp));
                right.Children.Add(Ui.Button("", () => Remove(item), "icon ghost", Icons.Trash, I18n.T("mr.remove")));
                var line = new DockPanel();
                DockPanel.SetDock(right, Dock.Right);
                line.Children.Add(right);
                var who = Ui.Col(2, Ui.Text(i.Title, "h3"), Ui.Text($"{i.Version} · {i.File} · {Ui.Ago(i.Installed)}", "small muted"));
                who.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(Ui.Row(12, Ui.Thumb(i.Icon, i.Title, 40, 10, 80), who));
                var card = Ui.Card(line, 12);
                col.Children.Add(Ctx.Attach(card, () => Ctx.Menu(
                    Ctx.Item(I18n.T("mr.remove"), Icons.Trash, () => Remove(item)),
                    Ctx.Folder(I18n.T("mr.openFolder"), Directory.CreateDirectory(Path.Combine(Modrinth.Folder, Modrinth.SubFolder(item.Type))).FullName),
                    Ctx.Copy(I18n.T("ctx.copyName"), item.Title),
                    Ctx.Copy(I18n.T("ctx.copyId"), item.ProjectId))));
            }
        }
        foreach (var type in Modrinth.Types)
            if (Modrinth.LooseFiles(type) is > 0 and var n) col.Children.Add(Ui.Text(I18n.T("mr.loose", ("n", n), ("folder", Modrinth.SubFolder(type))), "small muted"));
        return col;
    }

    // ---------------------------------------------------------------- настройки

    Control SettingsView()
    {
        var path = new TextBox { Text = Modrinth.Folder, Width = 520, IsReadOnly = true };
        var pick = Ui.Button(I18n.T("mr.pickFolder"), async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || await Pickers.Folder(top, I18n.T("mr.pickFolder")) is not { } dir) return;
            Modrinth.Folder = dir;
            Build();
        }, "primary", Icons.Folder);
        var reset = Ui.Button(I18n.T("mr.defaultFolder"), () => { Modrinth.Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"); Build(); }, "ghost", Icons.Refresh);
        var exists = Directory.Exists(Modrinth.Folder);
        return Ui.Card(Ui.Col(14,
            Ui.Text(I18n.T("mr.folder"), "h2"),
            Ui.Text(I18n.T("mr.folder.text"), "muted", wrap: true),
            Ui.Row(8, path, pick, reset),
            Ui.Text(exists ? "✓ " + I18n.T("mr.folder.ok") : I18n.T("mr.folder.missing"), "small", color: exists ? Ui.Res("Good") : Ui.Res("Warn")),
            Ui.Text(I18n.T("mr.folder.subs"), "small muted", wrap: true)), 24);
    }
}
