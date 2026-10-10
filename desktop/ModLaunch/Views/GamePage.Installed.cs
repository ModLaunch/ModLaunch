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
    string _instQuery = "", _instFilter = "all", _instSort = "name";

    bool _smapiAsked;

    /// <summary>Stardew: один раз спросить у smapi.io, какие из установленных модов сломаны, и перерисовать список.</summary>
    async void AskSmapi(ModRegistry registry)
    {
        if (_smapiAsked || _g.Def.Loader != Games.LoaderKind.Smapi || Program.Demo) return;
        var ids = registry.List().Select(m => m.Str("uniqueId")).OfType<string>().Where(u => Features.SmapiIndex.Cached(u) is null).ToList();
        if (ids.Count == 0) return;
        _smapiAsked = true;
        try { await Features.SmapiIndex.Lookup(ids, default); Build(); } catch { }
    }

    Control InstalledView()
    {
        var registry = _g.Registry!;
        AskSmapi(registry);
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
        {
            // Моды, положенные руками: взять в список — будут выкл/вкл, профили и обновления.
            var adopt = Ui.Button(I18n.T("local.adopt"), () => Actions.AdoptAll(_g, unmanaged), "", Icons.Plus);
            DockPanel.SetDock(adopt, Dock.Right);
            var text = Ui.Row(10, Ui.Icon(Icons.Package, 16, Ui.Res("Muted")), new TextBlock { Text = I18n.T("inst.unmanaged", ("list", string.Join(", ", unmanaged.Take(6)))), TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxWidth = 820, VerticalAlignment = VerticalAlignment.Center });
            text.VerticalAlignment = VerticalAlignment.Center;
            col.Children.Add(new Border { Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8, 8, 8), Child = new DockPanel { Children = { adopt, text } } });
        }

        var conflicts = Features.Conflicts.Find(registry);
        if (conflicts.Count > 0)
        {
            var list = string.Join("; ", conflicts.Take(4).Select(c => I18n.T("v4.conflict.pair", ("a", c.A), ("b", c.B), ("n", c.Files))));
            var notice = Notice(Icons.Alert, I18n.T("v4.conflicts", ("list", list)), Ui.Res("Warn"));
            ToolTip.SetTip(notice, I18n.T("v4.conflict.hint"));
            col.Children.Add(notice);
        }

        var mods = registry.List();
        if (UndoNotice() is { } undo) col.Children.Add(undo);
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
        if (_instSort == "tags")
        {
            // Группы по первой метке, как разделители в MO2; без меток — в конце.
            foreach (var group in mods.GroupBy(m => TagsOf(m).FirstOrDefault() ?? "").OrderBy(x => x.Key == "").ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                col.Children.Add(GroupHeader(registry, group.Key, group.ToList()));
                foreach (var mod in group) col.Children.Add(InstalledRow(registry, mod, byRecord.GetValueOrDefault(mod.Str("id")!)));
            }
            return col;
        }
        foreach (var mod in mods) col.Children.Add(InstalledRow(registry, mod, byRecord.GetValueOrDefault(mod.Str("id")!)));
        return col;
    }

    /// <summary>Заголовок группы: метка, сколько включено и переключатель для всей группы.</summary>
    Control GroupHeader(ModRegistry registry, string tag, List<JsonObject> mods)
    {
        var on = mods.Count(m => m.Bool("enabled", true));
        var sw = new ToggleSwitch { IsChecked = on == mods.Count, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(sw, I18n.T("tags.group.toggle"));
        sw.IsCheckedChanged += (_, _) =>
        {
            var ids = mods.Select(m => m.Str("id")!).ToList();
            if (sw.IsChecked == true) BulkEnable(registry, ids); else BulkDisable(registry, ids);
        };
        DockPanel.SetDock(sw, Dock.Right);
        var title = Ui.Row(8, Ui.Text(tag == "" ? I18n.T("tags.none") : "#" + tag, "h3"), Ui.Text($"{on}/{mods.Count}", "small muted"));
        title.VerticalAlignment = VerticalAlignment.Center;
        return new DockPanel { Margin = new Thickness(4, 10, 12, 0), Children = { sw, title } };
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
        foreach (var tag in AllTags(_g.Registry!))
        {
            var t = tag;
            var chip = Ui.Button("#" + t, () => { _instTag = _instTag == t ? null : t; Build(); }, "chip");
            if (_instTag == t) chip.Classes.Add("active");
            filters.Children.Add(chip);
        }
        var sorts = new[] { "name", "date", "size", "tags" };
        var sort = new ComboBox { Width = 190, Height = 38 };
        foreach (var x in sorts) sort.Items.Add(x == "tags" ? I18n.T("tags.sort") : I18n.T("v4.sort." + x));
        sort.SelectedIndex = Array.IndexOf(sorts, _instSort);
        sort.SelectionChanged += (_, _) => { if (sort.SelectedIndex >= 0 && sorts[sort.SelectedIndex] != _instSort) { _instSort = sorts[sort.SelectedIndex]; Build(); } };
        var pick = Ui.Button(_select ? I18n.T("bulk.done") : I18n.T("bulk.select"), ToggleSelect, _select ? "primary" : "", _select ? Icons.Check : Icons.List);
        pick.Height = 38;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(search);
        Grid.SetColumn(sort, 1);
        grid.Children.Add(sort);
        Grid.SetColumn(pick, 2);
        grid.Children.Add(pick);
        var rows = Ui.Col(8, grid, filters);
        if (_select)
        {
            // «Выбрать все» — среди тех, что видны с текущим поиском и фильтром.
            var visible = FilterInstalled(_g.Registry!, _g.Registry!.List(), ModUpdates.Found.GetValueOrDefault(_g.Def.Id)).Select(m => m.Str("id")!).ToList();
            var all = new CheckBox { Content = I18n.T("upd.review.all"), IsChecked = visible.Count > 0 && visible.All(_picked.Contains) };
            all.IsCheckedChanged += (_, _) =>
            {
                if (all.IsChecked == true) _picked.UnionWith(visible); else _picked.ExceptWith(visible);
                Build();
            };
            rows.Children.Add(all);
        }
        return rows;
    }

    List<JsonObject> FilterInstalled(ModRegistry registry, IReadOnlyList<JsonObject> mods, List<ModUpdate>? updates)
    {
        var withUpdate = updates?.Select(u => u.RecordId).ToHashSet() ?? [];
        var problemMods = (_missing ?? Deps.Find(registry)).Select(m => m.ModId).ToHashSet();
        IEnumerable<JsonObject> q = mods;
        if (_instQuery.Trim() != "")
        {
            // Ищем и по своему названию, и по меткам (можно с #).
            var text = _instQuery.Trim().TrimStart('#');
            bool Has(string? s) => (s ?? "").Contains(text, StringComparison.CurrentCultureIgnoreCase);
            q = q.Where(m => Has(m.Str("name")) || Has(m.Str("displayName")) || Has(m.Str("author")) || TagsOf(m).Any(Has));
        }
        if (_instTag is { } tag) q = q.Where(m => TagsOf(m).Contains(tag, StringComparer.CurrentCultureIgnoreCase));
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
            "name" or "tags" => q.OrderBy(ModTitle, StringComparer.CurrentCultureIgnoreCase),
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
        var name = ModTitle(mod);
        var note = new TextBox { Text = Notes.Get(_g.Def.Id, id), Watermark = I18n.T("v4.note.placeholder"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, MaxLength = 2000 };
        var body = Ui.Col(14);
        var facts = new List<string>();
        if (mod.Str("version") is { Length: > 0 } v) facts.Add(I18n.T("mod.version", ("version", v)));
        facts.Add(GamePage.Size(Features.Conflicts.FolderSize(registry.FolderFor(mod))));
        facts.Add(Catalog.Title(_g.Def.SourceOf(id, mod.Str("source"))));
        body.Children.Add(Ui.Text(string.Join(" · ", facts), "small muted"));
        body.Children.Add(PersonalEditor(registry, mod, out var savePersonal));
        body.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v4.note"), "h3"), note));
        if (DepsInfo(registry, id) is { } deps) body.Children.Add(deps);
        body.Children.Add(HoldRow(id, mod));

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
            savePersonal();
            w.CloseDialog();
            w.Toast(I18n.T("v4.note.saved"));
            Build();
        }, "primary", Icons.Save));
        w.Dialog(name, body, actions.ToArray());
    }

    /// <summary>«Не обновлять этот мод» — чтобы версия совпадала с друзьями по сети.</summary>
    Control HoldRow(string id, JsonObject mod)
    {
        var sw = new ToggleSwitch { IsChecked = mod.Bool("hold"), OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        sw.IsCheckedChanged += (_, _) =>
        {
            ModUpdates.Hold(_g, id, sw.IsChecked == true);
            AppState.Notify();
        };
        DockPanel.SetDock(sw, Dock.Right);
        var text = Ui.Col(2, Ui.Text(I18n.T("upd.hold"), "h3"), Ui.Text(I18n.T("upd.hold.hint"), "small muted", wrap: true));
        text.Margin = new Thickness(0, 0, 12, 0);
        return new DockPanel { Children = { sw, text } };
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
        var head = new DockPanel();
        // Сначала окно со списком: что обновится, что нового, что оставить как есть.
        var all = Ui.Button(I18n.T("upd.all"), () => UpdateReview.Show(updates.Select(u => (_g, u)).ToList()), "primary", Icons.ArrowUp);
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        var label = Ui.Row(10, Ui.Icon(Icons.ArrowUp, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("upd.found." + I18n.Plural(n, "one", "few", "many"), ("n", n)), "h3"));
        label.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(label);
        return new Border { Background = Ui.Res("BrandSoft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12), Child = head };
    }

    void RunUpdate(ModUpdate u)
    {
        _ = Actions.UpdateMods(_g, [u]);
        Build();
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
        var name = ModTitle(mod);
        var enabled = mod.Bool("enabled", true);
        var missing = mod.Bool("missing");
        var preset = mod.Str("kind") == "preset";

        var sub = new List<string>();
        if (mod.Str("displayName") is { Length: > 0 } && mod.Str("name") is { } original) sub.Add(original);
        if (preset) sub.Add("ReShade");
        if (mod.Str("version") is { Length: > 0 } v) sub.Add(I18n.T("mod.version", ("version", v)));
        if (mod.Str("author") is { Length: > 0 } a) sub.Add(a);
        if (DateTime.TryParse(mod.Str("installedAt"), out var at)) sub.Add(Ui.Ago(at.ToUniversalTime()));

        var title = Ui.Row(8, Ui.Text(name, "h3"));
        if (missing) title.Children.Add(ModRow.Tag(I18n.T("inst.missing"), Ui.Soft("#3A1A1A", "#FF6B6B"), Ui.Ink("#FF6B6B")));
        else if (!enabled) title.Children.Add(ModRow.Tag(I18n.T("inst.off"), Ui.Res("Surface3"), Ui.Res("Muted")));
        if (mod.Str("requestedBy") is not null) title.Children.Add(ModRow.Tag(I18n.T("mod.stat.deps.one").ToLowerInvariant(), Ui.Res("Surface3"), Ui.Res("Muted")));
        if (mod.Bool("hold")) title.Children.Add(ModRow.Tag(I18n.T("upd.held"), Ui.Res("Surface3"), Ui.Res("Muted")));
        if (_g.Def.Loader == Games.LoaderKind.Smapi && Features.SmapiIndex.Cached(mod.Str("uniqueId")) is var (smapi, fix) && Features.Compat.SmapiBadge(smapi) is var (smapiKey, smapiBad))
        {
            var tag = ModRow.Tag(I18n.T(smapiKey), smapiBad ? Ui.Soft("#3A1A1A", "#FF6B6B") : Ui.Soft("#3A2A12", "#F2B84B"), smapiBad ? Ui.Ink("#FF6B6B") : Ui.Ink("#F2B84B"));
            ToolTip.SetTip(tag, I18n.T("smapi.hint"));
            title.Children.Add(tag);
            if (fix is not null) title.Children.Add(Ui.Button(I18n.T("smapi.fix"), () => Ui.OpenUrl(fix), "ghost"));
        }
        title.Children.AddRange(TagChips(mod));
        var middle = Ui.Col(4, title, Ui.Text(string.Join(" · ", sub), "small muted"));
        if (Notes.Get(_g.Def.Id, id) is { Length: > 0 } note) middle.Children.Add(Ui.Text("✎ " + note.Split('\n')[0], "small brand"));
        middle.VerticalAlignment = VerticalAlignment.Center;

        var toggle = new ToggleSwitch { IsChecked = enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center, IsEnabled = !missing, MinWidth = 0 };
        ToolTip.SetTip(toggle, enabled ? I18n.T("mod.disable") : I18n.T("mod.enable"));
        toggle.IsCheckedChanged += (_, _) =>
        {
            var on = toggle.IsChecked == true;
            if (!preset) { ToggleMod(registry, mod, on); return; }
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
            // Nexus без Premium — тоже отсюда: откроем страницу, файл из браузера поставится сам.
            var busy = Actions.IsUpdating(_g, id);
            var b = Ui.Button(busy ? I18n.T("upd.updating") : I18n.T("upd.to", ("version", update.Latest)), () => RunUpdate(update),
                update.Manual ? "" : "primary", update.Manual ? Icons.External : Icons.ArrowUp, update.Manual ? I18n.T("upd.viaBrowser.tip") : null);
            b.IsEnabled = !busy;
            right.Children.Add(b);
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
        Control body = grid;
        if (_select)
        {
            var box = PickBox(id);
            DockPanel.SetDock(box, Dock.Left);
            body = new DockPanel { Children = { box, grid } };
        }
        var card = new Border { Classes = { "card" }, Padding = new Thickness(12), Child = body };
        if (_select && _picked.Contains(id)) { card.BorderBrush = Ui.Res("Brand"); card.BorderThickness = new Thickness(1); }
        if (!enabled || missing) card.Opacity = 0.7;
        Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(enabled ? I18n.T("ctx.disable") : I18n.T("ctx.enable"), enabled ? Icons.EyeOff : Icons.Eye, () => ToggleMod(registry, mod, !enabled), !missing),
            update is not null ? Ctx.Item(I18n.T("upd.to", ("version", update.Latest)), Icons.ArrowUp, () => RunUpdate(update)) : null,
            Ctx.Item(I18n.T("v4.note"), Icons.Edit, () => ModDialog(registry, mod)),
            "-",
            Ctx.Folder(I18n.T("v4.folder"), registry.FolderFor(mod)),
            mod.Str("url") is string link ? Ctx.Link(I18n.T("mod.page"), link) : null,
            Ctx.Copy(I18n.T("ctx.copyName"), name),
            Ctx.Copy(I18n.T("ctx.copyId"), id),
            "-",
            Ctx.Item(_select && _picked.Contains(id) ? I18n.T("ctx.unselect") : I18n.T("ctx.select"), Icons.Check, () => { _select = true; if (!_picked.Remove(id)) _picked.Add(id); Build(); }),
            Ctx.Item(I18n.T("mod.remove"), Icons.Trash, () => ConfirmRemove(registry, mod))));
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
            RemoveMods(registry, [id]);
        }
        var dependents = DepGraph.Build(registry).Dependents([id]);
        // «Спрашивать перед удалением» можно выключить в Настройки → Система (но не когда мод нужен другим).
        if (!Settings.Data.Bool("confirmRemove", true) && dependents.Count == 0) { Remove(); return; }
        var text = Ui.Col(10, Ui.Text(I18n.T("mod.removeConfirm.text"), "muted", wrap: true));
        if (dependents.Count > 0)
            text.Children.Add(Ui.Row(8, Ui.Icon(Icons.Alert, 15, Ui.Res("Warn")), Ui.Text(I18n.T("deps.remove.warn", ("list", Names(dependents))), "small", wrap: true)));
        w.Dialog(I18n.T("mod.removeConfirm", ("name", name)),
            text,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mod.remove"), Remove, "primary", Icons.Trash));
    }
}
