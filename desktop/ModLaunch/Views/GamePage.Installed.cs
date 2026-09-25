using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Views;

public sealed partial class GamePage
{
    List<Missing>? _missing;
    bool _checkingUpdates, _missingLoading;
    readonly HashSet<string> _updating = [];
    string _instQuery = "", _instFilter = "all", _instSort = "name";

    Control InstalledView()
    {
        var registry = _g.Registry!;
        var col = new StackPanel { Spacing = 10 };

        var head = new DockPanel();
        var actions = Ui.Row(8,
            Ui.Button(_checkingUpdates ? I18n.T("upd.checking") : I18n.T("upd.check"), CheckUpdates, "", Icons.Refresh),
            Ui.Button(I18n.T("inst.fromFile"), () => Actions.InstallFromFile(_g), "", Icons.FilePlus),
            Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(registry.ModsDir), "", Icons.Folder));
        ((Button)actions.Children[0]).IsEnabled = !_checkingUpdates;
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(Ui.Text(I18n.T("inst.title"), "h2"));
        col.Children.Add(head);

        // Обновления.
        if (ModUpdates.Found.TryGetValue(_g.Def.Id, out var updates) && updates.Count > 0) col.Children.Add(UpdatesCard(updates));

        // Зависимости: сразу — по списку, номера в каталоге — в фоне.
        if (_missing is null && !_missingLoading) _ = LoadMissing();
        var missing = _missing ?? Deps.Find(registry);
        if (missing.Count > 0)
        {
            var text = I18n.T("inst.problems", ("list", string.Join(", ", missing.Select(m => m.Name).Distinct().Take(8))));
            var row = new DockPanel();
            if (missing.Any(m => m.ResolveId is not null))
            {
                var fix = Ui.Button(I18n.T("deps.install"), () => _ = Actions.InstallMissing(_g), "primary", Icons.Download);
                DockPanel.SetDock(fix, Dock.Right);
                row.Children.Add(fix);
            }
            row.Children.Add(Ui.Row(10, Ui.Icon(Icons.Alert, 16, Ui.Res("Warn")), new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 720 }));
            col.Children.Add(new Border { Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Warn"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row });
        }

        var unmanaged = registry.Unmanaged();
        if (unmanaged.Count > 0)
            col.Children.Add(Notice(Icons.Package, I18n.T("inst.unmanaged", ("list", string.Join(", ", unmanaged.Take(6)))), Ui.Res("Muted")));

        var conflicts = Features.Conflicts.Find(registry);
        if (conflicts.Count > 0)
        {
            var list = string.Join("; ", conflicts.Take(4).Select(c => I18n.T("v4.conflict.pair", ("a", c.A), ("b", c.B), ("n", c.Files))));
            var notice = Notice(Icons.Alert, I18n.T("v4.conflicts", ("list", list)), Ui.Res("Warn"));
            ToolTip.SetTip(notice, I18n.T("v4.conflict.hint"));
            col.Children.Add(notice);
        }

        var mods = registry.List();
        if (mods.Count > 0) col.Children.Add(InstalledToolbar());
        mods = FilterInstalled(registry, mods, updates);
        if (mods.Count == 0 && registry.List().Count > 0) { col.Children.Add(Ui.Card(Ui.Text(I18n.T("catalog.nothingFound", ("query", _instQuery)), "muted"), 18)); return col; }
        if (mods.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Text(I18n.T("inst.empty"), "h3"),
                Ui.Text(I18n.T("dl.empty.text"), "muted", wrap: true),
                Ui.Button(I18n.T("games.market"), () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "catalog")), "primary", Icons.Bag)), 24));
            return col;
        }

        var byRecord = updates?.ToDictionary(u => u.RecordId) ?? [];
        foreach (var mod in mods) col.Children.Add(InstalledRow(registry, mod, byRecord.GetValueOrDefault(mod.Str("id")!)));
        return col;
    }

    /// <summary>Поиск, фильтр и сортировка установленных (как в Vortex и Modrinth App).</summary>
    Control InstalledToolbar()
    {
        var search = new TextBox { Text = _instQuery, Watermark = I18n.T("v4.search.installed"), Height = 38 };
        search.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Search, 15, Ui.Res("Muted")) };
        // Поиск по Enter: перерисовка на каждую букву сбивала бы курсор.
        search.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { _instQuery = search.Text ?? ""; Build(); } };
        var filters = Ui.Row(6);
        foreach (var f in new[] { "all", "enabled", "disabled", "updates", "problems" })
        {
            var id = f;
            var b = Ui.Button(I18n.T("v4.filter." + f), () => { _instFilter = id; Build(); }, "chip");
            if (_instFilter == f) b.Classes.Add("active");
            filters.Children.Add(b);
        }
        var sorts = new[] { "name", "date", "size" };
        var sort = new ComboBox { Width = 190, Height = 38 };
        foreach (var x in sorts) sort.Items.Add(I18n.T("v4.sort." + x));
        sort.SelectedIndex = Array.IndexOf(sorts, _instSort);
        sort.SelectionChanged += (_, _) => { if (sort.SelectedIndex >= 0 && sorts[sort.SelectedIndex] != _instSort) { _instSort = sorts[sort.SelectedIndex]; Build(); } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(search);
        Grid.SetColumn(sort, 1);
        grid.Children.Add(sort);
        return Ui.Col(8, grid, filters);
    }

    List<JsonObject> FilterInstalled(ModRegistry registry, IReadOnlyList<JsonObject> mods, List<ModUpdate>? updates)
    {
        var withUpdate = updates?.Select(u => u.RecordId).ToHashSet() ?? [];
        var problemMods = (_missing ?? Deps.Find(registry)).Select(m => m.ModId).ToHashSet();
        IEnumerable<JsonObject> q = mods;
        if (_instQuery.Trim() != "")
            q = q.Where(m => (m.Str("name") ?? "").Contains(_instQuery.Trim(), StringComparison.CurrentCultureIgnoreCase) || (m.Str("author") ?? "").Contains(_instQuery.Trim(), StringComparison.CurrentCultureIgnoreCase));
        q = _instFilter switch
        {
            "enabled" => q.Where(m => m.Bool("enabled", true)),
            "disabled" => q.Where(m => !m.Bool("enabled", true)),
            "updates" => q.Where(m => withUpdate.Contains(m.Str("id") ?? "")),
            "problems" => q.Where(m => m.Bool("missing") || problemMods.Contains(m.Str("id") ?? "")),
            _ => q,
        };
        q = _instSort switch
        {
            "date" => q.OrderByDescending(m => m.Str("installedAt")),
            "size" => q.OrderByDescending(m => Features.Conflicts.FolderSize(registry.FolderFor(m))),
            _ => q,
        };
        return q.ToList();
    }

    /// <summary>Окно мода: заметка, папка, версии из архива, одобрение на Nexus.</summary>
    void ModDialog(ModRegistry registry, JsonObject mod)
    {
        var w = MainWindow.Current!;
        var id = mod.Str("id")!;
        var name = mod.Str("name") ?? id;
        var note = new TextBox { Text = Notes.Get(_g.Def.Id, id), Watermark = I18n.T("v4.note.placeholder"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, MaxLength = 2000 };
        var body = Ui.Col(14);
        var facts = new List<string>();
        if (mod.Str("version") is { Length: > 0 } v) facts.Add(I18n.T("mod.version", ("version", v)));
        facts.Add(GamePage.Size(Features.Conflicts.FolderSize(registry.FolderFor(mod))));
        facts.Add(Catalog.Title(_g.Def.SourceOf(id, mod.Str("source"))));
        body.Children.Add(Ui.Text(string.Join(" · ", facts), "small muted"));
        body.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v4.note"), "h3"), note));

        var archive = DownloadArchive.For(_g.Def.Id, id);
        var versions = Ui.Col(6, Ui.Text(I18n.T("v4.archive.versions"), "h3"));
        if (archive.Count == 0) versions.Children.Add(Ui.Text(I18n.T("v4.archive.empty"), "small muted"));
        foreach (var file in archive.Take(6))
        {
            var row = new DockPanel();
            var f = file;
            var go = Ui.Button(I18n.T("v4.archive.reinstall"), () => { w.CloseDialog(); Actions.ReinstallFromArchive(_g, mod, f); }, "", Icons.Refresh);
            DockPanel.SetDock(go, Dock.Right);
            row.Children.Add(go);
            row.Children.Add(Ui.Text($"{(f.Version == "" ? "—" : f.Version)} · {Ui.Ago(f.At)} · {GamePage.Size(f.Size)}", "small"));
            versions.Children.Add(row);
        }
        body.Children.Add(versions);

        var actions = new List<Control>
        {
            Ui.Button(I18n.T("v4.folder"), () => Actions.OpenFolder(registry.FolderFor(mod)), "", Icons.Folder),
        };
        if (_g.Def.SourceOf(id, mod.Str("source")) == "nexus" && _g.Def.CatalogId(id) is string nexusId)
        {
            var endorsed = Actions.IsEndorsed(_g, nexusId);
            var endorse = Ui.Button(endorsed ? "✓ " + I18n.T("v4.endorsed") : I18n.T("v4.endorse"), async () => { await Actions.Endorse(_g, nexusId, mod.Str("version") ?? ""); w.CloseDialog(); }, "", Icons.Heart);
            endorse.IsEnabled = !endorsed;
            actions.Add(endorse);
        }
        actions.Add(Ui.Button(I18n.T("common.save"), () =>
        {
            Notes.Set(_g.Def.Id, id, note.Text ?? "");
            w.CloseDialog();
            w.Toast(I18n.T("v4.note.saved"));
            Build();
        }, "primary", Icons.Save));
        w.Dialog(name, body, actions.ToArray());
    }

    async Task LoadMissing()
    {
        _missingLoading = true;
        try { _missing = Program.Demo ? Deps.Find(_g.Registry!) : await Deps.FindResolved(_g.Registry!); }
        catch { _missing = Deps.Find(_g.Registry!); }
        _missingLoading = false;
        if (_tab == "installed") Build();
    }

    async void CheckUpdates()
    {
        _checkingUpdates = true;
        Build();
        try
        {
            var list = await ModUpdates.Check(_g);
            var n = list.Count;
            MainWindow.Current?.Toast(n == 0 ? I18n.T("upd.none") : I18n.T("upd.found." + I18n.Plural(n, "one", "few", "many"), ("n", n)));
        }
        catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
        _checkingUpdates = false;
        Build();
    }

    Control UpdatesCard(List<ModUpdate> updates)
    {
        var n = updates.Count;
        var auto = updates.Where(u => !u.Manual).ToList();
        var head = new DockPanel();
        if (auto.Count > 0)
        {
            var all = Ui.Button(I18n.T("upd.all"), () => { foreach (var u in auto) RunUpdate(u); }, "primary", Icons.ArrowUp);
            DockPanel.SetDock(all, Dock.Right);
            head.Children.Add(all);
        }
        head.Children.Add(Ui.Row(10, Ui.Icon(Icons.ArrowUp, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("upd.found." + I18n.Plural(n, "one", "few", "many"), ("n", n)), "h3")));
        return new Border { Background = Ui.Res("BrandSoft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12), Child = head };
    }

    void RunUpdate(ModUpdate u)
    {
        if (!_updating.Add(u.RecordId)) return;
        Build();
        Jobs.Run(u.Name, _g.Def.Name, async (_, progress, ct) =>
        {
            try { await ModUpdates.Update(_g, u, progress, ct); }
            finally { _updating.Remove(u.RecordId); AppState.Notify(); }
        });
    }

    static Control Notice(string icon, string text, IBrush color) => new Border
    {
        Background = Ui.Res("Surface"),
        BorderBrush = color,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14, 10),
        Child = Ui.Row(10, Ui.Icon(icon, 16, color), new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxWidth = 900 }),
    };

    Control InstalledRow(ModRegistry registry, JsonObject mod, ModUpdate? update)
    {
        var id = mod.Str("id")!;
        var name = mod.Str("name") ?? id;
        var enabled = mod.Bool("enabled", true);
        var missing = mod.Bool("missing");
        var preset = mod.Str("kind") == "preset";

        var sub = new List<string>();
        if (preset) sub.Add("ReShade");
        if (mod.Str("version") is { Length: > 0 } v) sub.Add(I18n.T("mod.version", ("version", v)));
        if (mod.Str("author") is { Length: > 0 } a) sub.Add(a);
        if (DateTime.TryParse(mod.Str("installedAt"), out var at)) sub.Add(Ui.Ago(at.ToUniversalTime()));

        var title = Ui.Row(8, Ui.Text(name, "h3"));
        if (missing) title.Children.Add(ModRow.Tag(I18n.T("inst.missing"), Ui.Hex("#3A1A1A"), Ui.Res("Bad")));
        else if (!enabled) title.Children.Add(ModRow.Tag(I18n.T("inst.off"), Ui.Res("Surface3"), Ui.Res("Muted")));
        if (mod.Str("requestedBy") is not null) title.Children.Add(ModRow.Tag(I18n.T("mod.stat.deps.one").ToLowerInvariant(), Ui.Res("Surface3"), Ui.Res("Muted")));
        var middle = Ui.Col(4, title, Ui.Text(string.Join(" · ", sub), "small muted"));
        if (Notes.Get(_g.Def.Id, id) is { Length: > 0 } note) middle.Children.Add(Ui.Text("✎ " + note.Split('\n')[0], "small brand"));
        middle.VerticalAlignment = VerticalAlignment.Center;

        var toggle = new ToggleSwitch { IsChecked = enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center, IsEnabled = !missing, MinWidth = 0 };
        ToolTip.SetTip(toggle, enabled ? I18n.T("mod.disable") : I18n.T("mod.enable"));
        toggle.IsCheckedChanged += (_, _) =>
        {
            var on = toggle.IsChecked == true;
            try
            {
                registry.SetEnabled(id, on);
                if (preset) Installer.SyncPreset(registry, on ? registry.Get(id) : null);
                MainWindow.Current?.Toast(I18n.T(on ? "toast.enabled" : "toast.disabled"));
            }
            catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
            _missing = null;
            Build();
        };

        var right = Ui.Row(6);
        if (update is not null)
        {
            if (update.Manual) right.Children.Add(Ui.Button(I18n.T("upd.to", ("version", update.Latest)), () => Ui.OpenUrl(mod.Str("url") ?? ""), "", Icons.External, I18n.T("upd.manual")));
            else
            {
                var busy = _updating.Contains(id);
                var b = Ui.Button(busy ? I18n.T("upd.updating") : I18n.T("upd.to", ("version", update.Latest)), () => RunUpdate(update), "primary", Icons.ArrowUp);
                b.IsEnabled = !busy;
                right.Children.Add(b);
            }
        }
        if (mod.Str("url") is string url) right.Children.Add(Ui.Button("", () => Ui.OpenUrl(url), "icon ghost", Icons.External, I18n.T("mod.page")));
        right.Children.Add(toggle);
        right.Children.Add(Ui.Button("", () => ModDialog(registry, mod), "icon ghost", Icons.Edit, I18n.T("v4.note")));
        right.Children.Add(Ui.Button("", () => ConfirmRemove(registry, mod), "icon ghost", Icons.Trash, I18n.T("mod.remove")));
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

    void ConfirmRemove(ModRegistry registry, JsonObject mod)
    {
        var w = MainWindow.Current!;
        var id = mod.Str("id")!;
        var name = mod.Str("name") ?? id;
        void Remove()
        {
            w.CloseDialog();
            try
            {
                registry.Remove(id);
                if (mod.Str("kind") == "preset") Installer.SyncPreset(registry, null);
                w.Toast(I18n.T("toast.removed"));
            }
            catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
            _missing = null;
            AppState.Notify();
        }
        // «Спрашивать перед удалением» можно выключить в Настройки → Система.
        if (!Settings.Data.Bool("confirmRemove", true)) { Remove(); return; }
        w.Dialog(I18n.T("mod.removeConfirm", ("name", name)),
            Ui.Text(I18n.T("mod.removeConfirm.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mod.remove"), Remove, "primary", Icons.Trash));
    }
}
