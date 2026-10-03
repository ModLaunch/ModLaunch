using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Mods;

namespace ModLaunch.Views;

/// <summary>
/// Установленные моды «пачкой», как в Vortex и MO2: выбрать несколько и включить, выключить,
/// обновить, пометить или удалить разом; свои названия и метки; выключение и удаление
/// с оглядкой на зависимости (DepGraph).
/// </summary>
public sealed partial class GamePage
{
    bool _select;
    readonly HashSet<string> _picked = [];
    string? _instTag;

    /// <summary>Последнее массовое включение/выключение — для «Вернуть как было».</summary>
    (string Text, Dictionary<string, bool> Before)? _undo;

    /// <summary>Название мода: своё, если его задали.</summary>
    static string ModTitle(JsonObject mod) => mod.Str("displayName") is { Length: > 0 } own ? own : mod.Str("name") ?? mod.Str("id") ?? "";

    static List<string> TagsOf(JsonObject mod) => mod.Arr("tags").Select(t => t?.ToString()?.Trim()).OfType<string>().Where(t => t != "").ToList();

    static string Names(IEnumerable<JsonObject> mods, int max = 6)
    {
        var list = mods.ToList();
        return string.Join(", ", list.Take(max).Select(ModTitle)) + (list.Count > max ? $" +{list.Count - max}" : "");
    }

    // ---------------------------------------------------------------- включить / выключить с зависимостями

    /// <summary>Переключатель у мода: выключение спросит, если мод нужен другим; включение включит и его зависимости.</summary>
    void ToggleMod(ModRegistry registry, JsonObject mod, bool on)
    {
        var id = mod.Str("id")!;
        var graph = DepGraph.Build(registry);
        if (!on && graph.Dependents([id]) is { Count: > 0 } dependents)
        {
            Build(); // переключатель вернётся на место, пока человек не решил
            AskDisable(registry, [id], dependents);
            return;
        }
        var extra = on ? graph.DisabledNeeds(id) : [];
        SetMany(registry, [id, .. extra.Select(m => m.Str("id")!)], on);
        if (registry.Get(id)?.Bool("enabled", true) != on) return; // не вышло — SetMany уже сказал почему
        MainWindow.Current?.Toast(extra.Count > 0 ? I18n.T("deps.on.with", ("list", Names(extra))) : I18n.T(on ? "toast.enabled" : "toast.disabled"));
    }

    /// <summary>«Без этого не заработают A и B»: выключить вместе с ними, только это или передумать.</summary>
    void AskDisable(ModRegistry registry, List<string> ids, List<JsonObject> dependents)
    {
        var w = MainWindow.Current!;
        var what = ids.Count == 1 && registry.Get(ids[0]) is { } one ? ModTitle(one) : I18n.T("bulk.theseN", ("n", ids.Count));
        void Apply(IEnumerable<string> off)
        {
            w.CloseDialog();
            var list = off.Distinct().ToList();
            Remember(registry, list, I18n.T("bulk.undo.off", ("n", list.Count)));
            var n = SetMany(registry, list, false);
            w.Toast(I18n.T("health.disabledN", ("n", n)));
        }
        w.Dialog(I18n.T("deps.off.title", ("name", what)),
            Ui.Col(10,
                Ui.Text(I18n.T("deps.off.text", ("list", Names(dependents))), "muted", wrap: true),
                Ui.Row(8, Ui.Icon(Icons.Info, 15, Ui.Res("Muted")), Ui.Text(I18n.T("deps.off.hint"), "small muted", wrap: true))),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("deps.off.only"), () => Apply(ids), "ghost"),
            Ui.Button(I18n.T("deps.off.all", ("n", dependents.Count)), () => Apply(ids.Concat(dependents.Select(m => m.Str("id")!))), "primary", Icons.Power));
    }

    /// <summary>Включить или выключить несколько модов; пресеты ReShade — через SyncPreset.</summary>
    int SetMany(ModRegistry registry, IEnumerable<string> ids, bool on)
    {
        var n = 0;
        JsonObject? preset = null;
        Exception? error = null;
        foreach (var id in ids.Distinct())
        {
            try
            {
                registry.SetEnabled(id, on);
                n++;
                if (registry.Get(id) is { } m && m.Str("kind") == "preset") preset = m;
            }
            catch (Exception e) { error ??= e; } // например, игра запущена и файлы заняты
        }
        if (error is not null) MainWindow.Current?.Toast(Jobs.Explain(error), bad: true);
        if (preset is not null) Installer.SyncPreset(registry, on ? preset : null);
        _missing = null;
        AppState.Notify();
        return n;
    }

    void Remember(ModRegistry registry, IEnumerable<string> ids, string text) =>
        _undo = (text, ids.Where(registry.Has).ToDictionary(id => id, id => registry.Get(id)!.Bool("enabled", true)));

    /// <summary>«Выключено 12 модов · Вернуть как было».</summary>
    Control? UndoNotice()
    {
        if (_undo is not { } undo || _g.Registry is not { } registry) return null;
        var back = Ui.Button(I18n.T("bulk.undo"), () =>
        {
            _undo = null;
            foreach (var group in undo.Before.GroupBy(kv => kv.Value)) SetMany(registry, group.Select(kv => kv.Key), group.Key);
            MainWindow.Current?.Toast(I18n.T("bulk.undone"));
        }, "", Icons.Refresh);
        var close = Ui.Button("", () => { _undo = null; Build(); }, "icon ghost", Icons.Close);
        var buttons = Ui.Row(4, back, close);
        DockPanel.SetDock(buttons, Dock.Right);
        var text = Ui.Row(10, Ui.Icon(Icons.Info, 16, Ui.Res("Muted")), Ui.Text(undo.Text, "", wrap: true));
        text.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Background = Ui.Res("Surface"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 6, 6, 6), Child = new DockPanel { Children = { buttons, text } } };
    }

    // ---------------------------------------------------------------- удаление и ненужные помощники

    /// <summary>Удалить моды (после подтверждения) и предложить убрать зависимости, которые больше никому не нужны.</summary>
    void RemoveMods(ModRegistry registry, List<string> ids)
    {
        var w = MainWindow.Current!;
        var orphans = DepGraph.Build(registry).Orphans(ids).Where(m => !ids.Contains(m.Str("id")!)).ToList();
        Snapshots.Take(_g, "remove");
        var n = 0;
        var preset = false;
        foreach (var id in ids)
        {
            try
            {
                preset |= registry.Get(id)?.Str("kind") == "preset";
                registry.Remove(id);
                n++;
            }
            catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
        }
        if (preset) Installer.SyncPreset(registry, null);
        foreach (var id in ids) _picked.Remove(id);
        _missing = null;
        w.Toast(n == 1 ? I18n.T("toast.removed") : I18n.T("bulk.removedN", ("n", n)));
        AppState.Notify();
        if (orphans.Count > 0) OfferOrphans(registry, orphans);
    }

    /// <summary>«Эти моды ставились как зависимости и теперь никому не нужны — убрать?»</summary>
    void OfferOrphans(ModRegistry registry, List<JsonObject> orphans)
    {
        var w = MainWindow.Current!;
        var n = orphans.Count;
        w.Dialog(I18n.T("deps.orphans.title." + I18n.Plural(n, "one", "few", "many"), ("n", n)),
            Ui.Text(I18n.T("deps.orphans.text", ("list", Names(orphans, 10))), "muted", wrap: true),
            Ui.Button(I18n.T("deps.orphans.keep"), w.CloseDialog),
            Ui.Button(I18n.T("deps.orphans.remove", ("n", n)), () => { w.CloseDialog(); RemoveMods(registry, orphans.Select(m => m.Str("id")!).ToList()); }, "primary", Icons.Trash));
    }

    // ---------------------------------------------------------------- режим выбора

    void ToggleSelect()
    {
        _select = !_select;
        _picked.Clear();
        Build();
    }

    /// <summary>Галочка у строки в режиме выбора.</summary>
    Control PickBox(string id)
    {
        var box = new CheckBox { IsChecked = _picked.Contains(id), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Margin = new Thickness(0, 0, 4, 0) };
        box.IsCheckedChanged += (_, _) =>
        {
            if (box.IsChecked == true) _picked.Add(id); else _picked.Remove(id);
            Build();
        };
        return box;
    }

    /// <summary>Полоса внизу экрана в режиме выбора: что сделать с отмеченными.</summary>
    Control? BulkBar()
    {
        if (!_select || _tab != "installed" || _g.Registry is not { } registry) return null;
        var picked = _picked.Where(registry.Has).ToList();
        var n = picked.Count;
        var updates = ModUpdates.Found.TryGetValue(_g.Def.Id, out var ups) ? ups.Where(u => _picked.Contains(u.RecordId)).ToList() : [];

        Button Act(string text, string icon, Action click, string classes = "")
        {
            var b = Ui.Button(text, click, classes, icon);
            b.IsEnabled = n > 0;
            return b;
        }
        var row = Ui.Row(8,
            Ui.Text(n == 0 ? I18n.T("bulk.none") : I18n.T("bulk.picked", ("n", n)), "h3"),
            new Border { Width = 1, Height = 24, Background = Ui.Res("Line"), Margin = new Thickness(4, 0) },
            Act(I18n.T("bulk.on"), Icons.Power, () => BulkEnable(registry, picked)),
            Act(I18n.T("bulk.off"), Icons.EyeOff, () => BulkDisable(registry, picked)));
        if (updates.Count > 0) row.Children.Add(Act(I18n.T("bulk.update", ("n", updates.Count)), Icons.ArrowUp, () => { _ = Actions.UpdateMods(_g, updates); Build(); }, "primary"));
        row.Children.Add(Act(I18n.T("bulk.tag"), Icons.Flag, () => TagDialog(registry, picked)));
        row.Children.Add(Act(I18n.T("bulk.copy"), Icons.List, () => CopyList(registry, picked)));
        row.Children.Add(Act(I18n.T("bulk.remove"), Icons.Trash, () => ConfirmBulkRemove(registry, picked)));
        row.Children.Add(Ui.Button("", ToggleSelect, "icon ghost", Icons.Close, I18n.T("bulk.done")));
        foreach (var c in row.Children) c.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Classes = { "card" },
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 10, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 22),
            BoxShadow = BoxShadows.Parse("0 14 40 0 #80000000"),
            Child = row,
        };
    }

    void BulkEnable(ModRegistry registry, List<string> ids)
    {
        var graph = DepGraph.Build(registry);
        var extra = ids.SelectMany(graph.DisabledNeeds).Where(m => !ids.Contains(m.Str("id")!)).DistinctBy(m => m.Str("id")).ToList();
        var all = ids.Concat(extra.Select(m => m.Str("id")!)).ToList();
        Remember(registry, all, I18n.T("bulk.undo.on", ("n", all.Count)));
        var n = SetMany(registry, all, true);
        MainWindow.Current?.Toast(extra.Count > 0 ? I18n.T("deps.on.with", ("list", Names(extra))) : I18n.T("health.enabledN", ("n", n)));
    }

    void BulkDisable(ModRegistry registry, List<string> ids)
    {
        var outside = DepGraph.Build(registry).Dependents(ids).Where(m => !ids.Contains(m.Str("id")!)).ToList();
        if (outside.Count > 0) { Build(); AskDisable(registry, ids, outside); return; } // переключатель группы вернётся на место, пока человек не решил
        Remember(registry, ids, I18n.T("bulk.undo.off", ("n", ids.Count)));
        var n = SetMany(registry, ids, false);
        MainWindow.Current?.Toast(I18n.T("health.disabledN", ("n", n)));
    }

    void ConfirmBulkRemove(ModRegistry registry, List<string> ids)
    {
        var w = MainWindow.Current!;
        var mods = ids.Select(registry.Get).OfType<JsonObject>().ToList();
        var outside = DepGraph.Build(registry).Dependents(ids).Where(m => !ids.Contains(m.Str("id")!)).ToList();
        var body = Ui.Col(10, Ui.Text(Names(mods, 10), "muted", wrap: true));
        if (outside.Count > 0)
            body.Children.Add(Ui.Row(8, Ui.Icon(Icons.Alert, 15, Ui.Res("Warn")), Ui.Text(I18n.T("deps.remove.warn", ("list", Names(outside))), "small", wrap: true)));
        w.Dialog(I18n.T("bulk.remove.title." + I18n.Plural(ids.Count, "one", "few", "many"), ("n", ids.Count)), body,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mod.remove"), () => { w.CloseDialog(); RemoveMods(registry, ids); }, "primary", Icons.Trash));
    }

    async void CopyList(ModRegistry registry, List<string> ids)
    {
        var lines = ids.Select(registry.Get).OfType<JsonObject>().OrderBy(ModTitle, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => m.Str("version") is { Length: > 0 } v ? $"{ModTitle(m)} — {v}" : ModTitle(m));
        var clipboard = TopLevel.GetTopLevel(MainWindow.Current)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
        MainWindow.Current?.Toast(I18n.T("bulk.copied", ("n", ids.Count)));
    }

    // ---------------------------------------------------------------- метки

    /// <summary>Все метки, что есть у модов игры (для фильтра и подсказок).</summary>
    static List<string> AllTags(ModRegistry registry) =>
        registry.List().SelectMany(TagsOf).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Поставить метку отмеченным модам или снять её.</summary>
    void TagDialog(ModRegistry registry, List<string> ids)
    {
        var w = MainWindow.Current!;
        var box = new TextBox { Watermark = I18n.T("tags.placeholder"), MaxLength = 30 };
        void Apply(string tag, bool add)
        {
            tag = tag.Trim();
            if (tag == "") return;
            foreach (var id in ids)
                registry.Edit(id, m =>
                {
                    var tags = TagsOf(m).Where(t => !t.Equals(tag, StringComparison.CurrentCultureIgnoreCase)).ToList();
                    if (add) tags.Add(tag);
                    m["tags"] = new JsonArray(tags.Select(t => (JsonNode)t).ToArray());
                });
            w.CloseDialog();
            w.Toast(I18n.T(add ? "tags.added" : "tags.removed", ("tag", tag), ("n", ids.Count)));
            AppState.Notify();
        }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Apply(box.Text ?? "", true); };
        var body = Ui.Col(12, box);
        var known = AllTags(registry);
        if (known.Count > 0)
        {
            var chips = new WrapPanel();
            foreach (var t in known)
            {
                var tag = t;
                var chip = Ui.Button("#" + tag, () => Apply(tag, true), "chip");
                chip.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(chip);
            }
            body.Children.Add(Ui.Col(6, Ui.Text(I18n.T("tags.known"), "small muted"), chips));
            var onPicked = ids.Select(registry.Get).OfType<JsonObject>().SelectMany(TagsOf).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            if (onPicked.Count > 0)
            {
                var remove = new WrapPanel();
                foreach (var t in onPicked)
                {
                    var tag = t;
                    var chip = Ui.Button("#" + tag + "  ✕", () => Apply(tag, false), "chip");
                    chip.Margin = new Thickness(0, 0, 6, 6);
                    remove.Children.Add(chip);
                }
                body.Children.Add(Ui.Col(6, Ui.Text(I18n.T("tags.remove"), "small muted"), remove));
            }
        }
        w.Dialog(I18n.T("tags.title", ("n", ids.Count)), body,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("tags.add"), () => Apply(box.Text ?? "", true), "primary", Icons.Flag));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => box.Focus());
    }

    /// <summary>Метки маленькими плашками в строке мода.</summary>
    static IEnumerable<Control> TagChips(JsonObject mod) =>
        TagsOf(mod).Take(4).Select(t => (Control)ModRow.Tag("#" + t, Ui.Res("Surface3"), Ui.Res("Muted")));

    /// <summary>Своё название и метки — в окне мода.</summary>
    Control PersonalEditor(ModRegistry registry, JsonObject mod, out Action save)
    {
        var id = mod.Str("id")!;
        var name = new TextBox { Text = mod.Str("displayName") ?? "", Watermark = mod.Str("name") ?? id, MaxLength = 80 };
        var tags = TagsOf(mod);
        var chips = new WrapPanel();
        var input = new TextBox { Watermark = I18n.T("tags.placeholder"), MaxLength = 30, Width = 180 };
        void Draw()
        {
            chips.Children.Clear();
            foreach (var t in tags)
            {
                var tag = t;
                var chip = Ui.Button("#" + tag + "  ✕", () => { tags.Remove(tag); Draw(); }, "chip");
                chip.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(chip);
            }
            input.Margin = new Thickness(0, 0, 0, 6);
            chips.Children.Add(input);
        }
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            var tag = (input.Text ?? "").Trim();
            if (tag != "" && !tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase)) tags.Add(tag);
            input.Text = "";
            Draw();
            input.Focus();
        };
        Draw();
        save = () =>
        {
            var pending = (input.Text ?? "").Trim();
            if (pending != "" && !tags.Contains(pending, StringComparer.CurrentCultureIgnoreCase)) tags.Add(pending);
            registry.Edit(id, m =>
            {
                var own = (name.Text ?? "").Trim();
                if (own == "" || own == m.Str("name")) m.Remove("displayName"); else m["displayName"] = own;
                if (tags.Count == 0) m.Remove("tags"); else m["tags"] = new JsonArray(tags.Select(t => (JsonNode)t).ToArray());
            });
        };
        return Ui.Col(10,
            Ui.Col(6, Ui.Text(I18n.T("mod.ownName"), "h3"), name),
            Ui.Col(6, Ui.Text(I18n.T("tags.header"), "h3"), chips));
    }

    /// <summary>Снимки: метки и группы, режим выбора, предупреждение о зависимостях, окно мода, помощники без дела, перетаскивание.</summary>
    [DemoShots]
    static void InstalledShots(Shots s)
    {
        var w = s.Window;
        var sub = AppState.Game("subnautica");
        var registry = sub.Registry!;
        // Ещё несколько модов: Vehicle Framework требует Nautilus, ECC Library стоит ради удалённого мода.
        foreach (var (id, name, by) in new[] { ("859", "Vehicle Framework", (string?)null), ("1112", "Configuration Manager", null), ("1457", "ECC Library 2.0", "nexus:subnautica:9999") })
        {
            Directory.CreateDirectory(Path.Combine(registry.ModsDir, name));
            registry.Add(new JsonObject { ["id"] = "nexus:subnautica:" + id, ["name"] = name, ["folder"] = name, ["version"] = "1.4." + id.Length, ["author"] = "Subnautica Modding", ["source"] = "nexus", ["requestedBy"] = by });
        }
        registry.Edit("nexus:subnautica:859", m => m["requires"] = new JsonArray(new JsonObject { ["id"] = "1262", ["name"] = "Nautilus" }));
        registry.Edit("nexus:subnautica:12", m => m["requires"] = new JsonArray(new JsonObject { ["id"] = "1262", ["name"] = "Nautilus" }));
        void Tag(string id, params string[] tags) => registry.Edit("nexus:subnautica:" + id, m => m["tags"] = new JsonArray(tags.Select(t => (JsonNode)t).ToArray()));
        Tag("1262", "библиотеки");
        Tag("1112", "библиотеки");
        Tag("859", "транспорт");
        Tag("12", "интерфейс", "с друзьями");
        registry.Edit("nexus:subnautica:12", m => m["displayName"] = "Карта с метками");
        Directory.CreateDirectory(Path.Combine(registry.ModsDir, "BetterBioReactor"));
        AppState.Notify();

        var page = new GamePage("subnautica", "installed") { _instSort = "tags" };
        w.Navigate(() => page);
        s.Save("installed-1-tags");

        page._instSort = "name";
        page._select = true;
        page._picked.UnionWith(["nexus:subnautica:12", "nexus:subnautica:859"]);
        page.Build();
        s.Save("installed-2-select");

        page.AskDisable(registry, ["nexus:subnautica:1262"], DepGraph.Build(registry).Dependents(["nexus:subnautica:1262"]));
        s.Save("installed-3-deps-off");
        w.CloseDialog();

        page._select = false;
        page._picked.Clear();
        page.Build();
        page.ModDialog(registry, registry.Get("nexus:subnautica:859")!);
        s.Save("installed-4-mod-dialog");
        w.CloseDialog();

        page.OfferOrphans(registry, DepGraph.Build(registry).Unused());
        s.Save("installed-5-orphans");
        w.CloseDialog();

        page.TagDialog(registry, ["nexus:subnautica:1262", "nexus:subnautica:1112"]);
        s.Save("installed-6-tag-dialog");
        w.CloseDialog();

        w.PreviewDrop(true);
        s.Save("installed-7-drop");
        w.PreviewDrop(false);

        foreach (var id in new[] { "859", "1112", "1457" }) registry.Remove("nexus:subnautica:" + id);
        foreach (var id in new[] { "12", "1262" }) registry.Edit("nexus:subnautica:" + id, m => { m.Remove("tags"); m.Remove("displayName"); m.Remove("requires"); });
        Directory.Delete(Path.Combine(registry.ModsDir, "BetterBioReactor"), true);
        AppState.Notify();
    }

    /// <summary>«Требует» и «Нужен для» — в окне мода.</summary>
    static Control? DepsInfo(ModRegistry registry, string id)
    {
        var graph = DepGraph.Build(registry);
        var needs = graph.Tree(id, 3);
        var by = graph.NeededByOf(id);
        if (needs.Count == 0 && by.Count == 0) return null;
        var col = Ui.Col(6);
        if (needs.Count > 0)
        {
            col.Children.Add(Ui.Text(I18n.T("deps.needs"), "h3"));
            foreach (var (depth, m) in needs.Take(10))
            {
                var line = Ui.Row(6, Ui.Dot(m.Bool("enabled", true) ? Ui.Res("Good") : Ui.Res("Warn")), Ui.Text(ModTitle(m) + (m.Bool("enabled", true) ? "" : " · " + I18n.T("inst.off")), "small"));
                line.Margin = new Thickness((depth - 1) * 16, 0, 0, 0);
                col.Children.Add(line);
            }
        }
        if (by.Count > 0)
            col.Children.Add(Ui.Col(4, Ui.Text(I18n.T("deps.neededBy"), "h3"), Ui.Text(Names(by, 8), "small muted", wrap: true)));
        return col;
    }
}
