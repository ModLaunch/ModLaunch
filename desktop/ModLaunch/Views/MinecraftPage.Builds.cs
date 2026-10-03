using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>Вкладка «Сборки»: свои сборки, модпаки, импорт .mrpack и сборки из других лаунчеров.</summary>
public sealed partial class MinecraftPage
{
    static List<McFound>? _found;
    static bool _scanning;

    Control BuildsView()
    {
        var col = new StackPanel { Spacing = 16 };
        col.Children.Add(Ui.Row(10,
            Ui.Button(I18n.T("mine.build.new"), CreateDialog, "primary", Icons.Plus),
            Ui.Button(I18n.T("mine.import.mrpack"), () => ImportMrpack(), "", Icons.FilePlus),
            Ui.Button(I18n.T("mine.tab.catalog") + " · " + I18n.T("mine.kind.modpack.many"), () => { _type = "modpack"; _category = ""; Go("catalog"); }, "ghost", Icons.Bag)));

        var builds = Mc.Instances();
        var active = Mc.Active;
        if (builds.Count == 0)
            col.Children.Add(Ui.Text(I18n.T("mine.builds.none"), "muted"));
        else
        {
            var grid = new UniformGrid { Columns = 3 };
            foreach (var i in builds) grid.Children.Add(BuildCard(i, i.Id == active?.Id));
            col.Children.Add(grid);
        }

        // Сборки других лаунчеров: подключаем на месте, ничего не копируя.
        if (_found is null && !_scanning) _ = Scan();
        var others = Ui.Col(10, Ui.Row(10, Ui.Text(I18n.T("mine.import.title"), "h3"),
            Ui.Button("", () => _ = Scan(), "icon ghost", Icons.Refresh, I18n.T("cr.refresh"))));
        others.Children.Add(Ui.Text(I18n.T("mine.import.text"), "small muted", wrap: true));
        if (_scanning && _found is null) others.Children.Add(Ui.Text(I18n.T("games.searching"), "small muted"));
        else if (_found is { Count: 0 }) others.Children.Add(Ui.Text(I18n.T("mine.import.none"), "small muted"));
        foreach (var f in _found ?? []) others.Children.Add(FoundRow(f));
        col.Children.Add(others);
        return col;
    }

    async Task Scan()
    {
        _scanning = true;
        if (Shown) Build();
        List<McFound> list;
        try { list = Program.Demo ? DemoFound() : await Task.Run(McImport.Scan); }
        catch { list = []; }
        _found = list;
        _scanning = false;
        if (MainWindow.Current?.CurrentPage == this) Build();
    }

    static List<McFound> DemoFound() =>
    [
        new("Better MC [FABRIC] - BMC2", @"C:\Users\Player\AppData\Roaming\ModrinthApp\profiles\Better MC", "modrinth-app", "1.20.1", "fabric", "0.16.10", 214),
        new("All the Mods 10", @"C:\Users\Player\curseforge\minecraft\Instances\All the Mods 10", "curseforge", "1.21.1", "neoforge", "21.1.200", 431),
    ];

    Control BuildCard(McInstance i, bool active)
    {
        var mods = McContent.Count(i, "mod");
        Control icon = i.Icon is { Length: > 0 } url ? Ui.Thumb(url, i.Name, 52, 14, 104) : LoaderBadge(i.Loader, 52);
        var head = Ui.Col(3, new TextBlock { Text = i.Name, Classes = { "h3" }, TextTrimming = TextTrimming.CharacterEllipsis }, Ui.Text(i.Label, "small brand"));
        head.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        top.Children.Add(icon);
        Grid.SetColumn(head, 1);
        top.Children.Add(head);

        var facts = new List<string>();
        if (i.Modded) facts.Add(I18n.T("mine.mods." + I18n.Plural(mods, "one", "few", "many"), ("n", mods)));
        facts.Add(i.LastPlayed is null ? I18n.T("mine.aside.never") : I18n.T("mine.aside.played", ("ago", Ui.Ago(i.LastPlayed))));
        if (i.Linked) facts.Add(McImport.SourceTitle(i.Source ?? ""));

        var play = Ui.Button(I18n.T("games.play"), () => Play(i), "primary", Icons.Play);
        var pick = Ui.Button(active ? I18n.T("mine.build.active") : I18n.T("mine.build.pick"), () => { Mc.Active = i; Mc.Notify(); }, "ghost", active ? Icons.Check : Icons.Layers);
        pick.IsEnabled = !active;
        var more = Ui.Button("", () => { }, "icon ghost", Icons.More, I18n.T("tab.more"));
        more.Click += (_, _) => { var m = BuildMenu(i); m.Placement = PlacementMode.BottomEdgeAlignedRight; m.ShowAt(more); };
        var buttons = Ui.Row(8, play, pick);
        var foot = new DockPanel();
        DockPanel.SetDock(more, Dock.Right);
        foot.Children.Add(more);
        foot.Children.Add(buttons);

        var body = Ui.Col(12, top, Ui.Text(string.Join(" · ", facts), "small muted"), foot);
        var card = new Border { Classes = { "card" }, Padding = new Thickness(16), Margin = new Thickness(0, 0, 14, 14), Child = body };
        if (active) { card.BorderBrush = Ui.Hex("#3FB950"); card.BorderThickness = new Thickness(1.5); }
        return Ctx.Attach(card, () => BuildMenu(i));
    }

    MenuFlyout BuildMenu(McInstance i) => Ctx.Menu(
        Ctx.Item(I18n.T("games.play"), Icons.Play, () => Play(i)),
        Ctx.Item(I18n.T("mine.build.pick"), Icons.Check, () => { Mc.Active = i; Mc.Notify(); }),
        Ctx.Item(I18n.T("mine.tab.settings"), Icons.Sliders, () => { Mc.Active = i; Go("settings"); }),
        "-",
        Ctx.Item(I18n.T("mine.build.rename"), Icons.Edit, () => RenameDialog(i)),
        Ctx.Item(I18n.T("mine.build.duplicate"), Icons.Copy, () => DuplicateDialog(i)),
        Ctx.Folder(I18n.T("games.openFolder"), i.Dir),
        "-",
        Ctx.Item(i.Linked ? I18n.T("mine.build.unlink") : I18n.T("mine.build.delete"), Icons.Trash, () => DeleteDialog(i)));

    Control FoundRow(McFound f)
    {
        var label = f.GameVersion == "" ? I18n.T("mine.import.unknown") : f.Loader == "vanilla" ? $"Minecraft {f.GameVersion}" : $"{Mc.LoaderTitle(f.Loader)} {f.GameVersion}";
        var words = Ui.Col(2, Ui.Text(f.Name, "h3"), Ui.Text($"{McImport.SourceTitle(f.Source)} · {label} · {I18n.T("mine.mods." + I18n.Plural(f.Mods, "one", "few", "many"), ("n", f.Mods))}", "small muted"));
        words.VerticalAlignment = VerticalAlignment.Center;
        var link = Ui.Button(I18n.T("mine.import.link"), () => LinkDialog(f), "", Icons.Link);
        link.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(link, Dock.Right);
        dock.Children.Add(link);
        dock.Children.Add(Ui.Row(12, LoaderBadge(f.Loader, 40), words));
        return Ctx.Attach(Ui.Card(dock, 12), () => Ctx.Menu(
            Ctx.Item(I18n.T("mine.import.link"), Icons.Link, () => LinkDialog(f)),
            Ctx.Folder(I18n.T("games.openFolder"), f.Dir)));
    }

    // ---------------------------------------------------------------- новая сборка

    public static void CreateDialog() => BuildDialog(null);

    static void LinkDialog(McFound f) => BuildDialog(f);

    /// <summary>
    /// Новая сборка (или подключение чужой): название, версия игры (со снимками по желанию),
    /// загрузчик и его версия, память. Списки версий — из манифеста Mojang и мета загрузчиков.
    /// </summary>
    static void BuildDialog(McFound? link)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var loader = link?.Loader is { } l && Mc.Loaders.Contains(l) ? l : "fabric";
        List<McVersion> versions = [];
        List<(string Version, bool Stable)> loaderVersions = [];
        var ticket = 0;

        var name = new TextBox { Text = link?.Name ?? "", Watermark = I18n.T("mine.create.name.hint") };
        var snapshots = new CheckBox { Content = I18n.T("mine.create.snapshots"), VerticalAlignment = VerticalAlignment.Center };
        var version = new ComboBox { Width = 240, MaxDropDownHeight = 420, PlaceholderText = I18n.T("common.loading") };
        var loaderVersion = new ComboBox { Width = 240, MaxDropDownHeight = 360, PlaceholderText = I18n.T("common.loading") };
        var note = Ui.Text("", "small muted", wrap: true);
        var loaders = Ui.Row(8);

        var maxGb = Math.Max(2, Math.Min(32, Mc.TotalMemoryMb() / 1024 - 2));
        var memory = new Slider { Minimum = 1, Maximum = maxGb, Value = Math.Min(maxGb, Mc.DefaultMemory() / 1024.0), TickFrequency = 0.5, IsSnapToTickEnabled = true, Width = 300, VerticalAlignment = VerticalAlignment.Center };
        var memText = Ui.Text("", "small");
        memText.VerticalAlignment = VerticalAlignment.Center;
        void ShowMemory() => memText.Text = I18n.T("mine.memory.value", ("gb", memory.Value.ToString("0.#", I18n.Culture)));
        memory.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) ShowMemory(); };
        ShowMemory();

        var go = Ui.Button(link is null ? I18n.T("mine.create.go") : I18n.T("mine.import.link"), () => { }, "primary", link is null ? Icons.Plus : Icons.Link);
        go.IsEnabled = false;

        string? Game() => version.SelectedIndex >= 0 && version.SelectedIndex < versions.Count ? versions[version.SelectedIndex].Id : null;

        void RenderLoaders()
        {
            loaders.Children.Clear();
            foreach (var x in Mc.Loaders)
            {
                var id = x;
                var chip = new Button { Classes = { "chip" }, Content = Ui.Row(8, LoaderBadge(x, 18), new TextBlock { Text = x == "vanilla" ? I18n.T("mine.loader.vanilla") : Mc.LoaderTitle(x), VerticalAlignment = VerticalAlignment.Center }) };
                if (x == loader) chip.Classes.Add("active");
                chip.Click += (_, _) => { loader = id; RenderLoaders(); _ = LoadLoaders(); };
                loaders.Children.Add(chip);
            }
        }

        async Task LoadVersions()
        {
            var my = ++ticket;
            version.ItemsSource = null;
            go.IsEnabled = false;
            try
            {
                var list = await McMeta.GameVersions(snapshots.IsChecked == true);
                if (my != ticket) return;
                versions = list;
                version.ItemsSource = list.Select(v => v.Release ? v.Id : $"{v.Id}  ·  {I18n.T("mine.create.snapshot")}").ToList();
                var want = link?.GameVersion is { Length: > 0 } gv ? list.FindIndex(v => v.Id == gv) : -1;
                version.SelectedIndex = want >= 0 ? want : Math.Max(0, list.FindIndex(v => v.Release));
                note.Text = "";
            }
            catch (Exception e)
            {
                if (my != ticket) return;
                note.Text = I18n.T("mine.err.offline", ("reason", Jobs.Explain(e)));
                note.Foreground = Ui.Res("Bad");
            }
            await LoadLoaders();
        }

        async Task LoadLoaders()
        {
            var my = ++ticket;
            loaderVersion.ItemsSource = null;
            go.IsEnabled = false;
            note.Text = "";
            note.Foreground = Ui.Res("Muted");
            if (Game() is not { } gv) return;
            if (loader == "vanilla")
            {
                loaderVersion.ItemsSource = new[] { I18n.T("mine.create.noLoader") };
                loaderVersion.SelectedIndex = 0;
                loaderVersion.IsEnabled = false;
                go.IsEnabled = true;
                note.Text = I18n.T("mine.create.vanillaNote");
                return;
            }
            loaderVersion.IsEnabled = true;
            try
            {
                var list = await McMeta.LoaderVersions(loader, gv);
                if (my != ticket) return;
                loaderVersions = list;
                if (list.Count == 0)
                {
                    loaderVersion.ItemsSource = new[] { "—" };
                    loaderVersion.SelectedIndex = 0;
                    loaderVersion.IsEnabled = false;
                    note.Text = I18n.T("mine.err.noLoader", ("loader", Mc.LoaderTitle(loader)), ("version", gv));
                    note.Foreground = Ui.Res("Warn");
                    return;
                }
                loaderVersion.ItemsSource = list.Select(x => x.Stable ? x.Version : $"{x.Version}  ·  beta").ToList();
                var want = link?.LoaderVersion is { Length: > 0 } lv ? list.FindIndex(x => x.Version == lv) : -1;
                loaderVersion.SelectedIndex = want >= 0 ? want : Math.Max(0, list.FindIndex(x => x.Stable));
                note.Text = loader is "forge" or "neoforge" ? I18n.T("mine.create.forgeNote", ("loader", Mc.LoaderTitle(loader))) : "";
                go.IsEnabled = true;
            }
            catch (Exception e)
            {
                if (my != ticket) return;
                note.Text = I18n.T("mine.err.offline", ("reason", Jobs.Explain(e)));
                note.Foreground = Ui.Res("Bad");
            }
        }

        version.SelectionChanged += (_, _) => { if (version.SelectedIndex >= 0) _ = LoadLoaders(); };
        snapshots.IsCheckedChanged += (_, _) => _ = LoadVersions();

        go.Click += (_, _) =>
        {
            if (Game() is not { } gv) return;
            var lv = loader == "vanilla" || loaderVersion.SelectedIndex < 0 || loaderVersion.SelectedIndex >= loaderVersions.Count ? "" : loaderVersions[loaderVersion.SelectedIndex].Version;
            var title = name.Text?.Trim() is { Length: > 0 } t ? t : loader == "vanilla" ? $"Minecraft {gv}" : $"{Mc.LoaderTitle(loader)} {gv}";
            var mb = (int)(memory.Value * 1024);
            w.CloseDialog();
            try
            {
                McInstance i;
                if (link is null) i = Mc.Create(title, gv, loader, lv, mb);
                else
                {
                    i = Mc.Link(title, link.Dir, gv, loader, lv, link.Source);
                    i.MemoryMb = mb;
                    Mc.Save(i);
                    _found?.Remove(link);
                    if (!Program.Demo) _ = Task.Run(async () => { try { await McContent.Identify(i); await McContent.CheckUpdates(i); } catch { } Mc.Notify(); });
                }
                try { McProfiles.Sync(i); } catch { }
                w.Toast(I18n.T(link is null ? "mine.create.done" : "mine.import.done", ("build", i.Name)));
                // Загрузчик ставим сразу — к первому «Играть» всё будет готово.
                if (i.Modded && !Program.Demo && !Mc.VersionReady(i))
                    Jobs.Run($"{i.LoaderTitle} {i.GameVersion}", "Minecraft", async (_, progress, ct) => { await McInstall.EnsureVersion(i, progress, ct); McProfiles.Sync(i); });
                w.Navigate(() => new MinecraftPage("mods"));
                Mc.Notify();
            }
            catch (Exception e) { Guard.Log(e); w.Toast(Jobs.Explain(e), bad: true); }
        };

        RenderLoaders();
        var body = Ui.Col(14,
            Field(I18n.T("mine.create.name"), name),
            Field(I18n.T("mine.create.version"), Ui.Row(14, version, snapshots)),
            Field(I18n.T("mine.create.loader"), loaders),
            Field(I18n.T("mine.create.loaderVersion"), loaderVersion),
            Field(I18n.T("mine.memory"), Ui.Row(14, memory, memText)),
            note);
        if (link is not null) body.Children.Insert(0, Ui.Text(I18n.T("mine.import.linkText", ("source", McImport.SourceTitle(link.Source)), ("dir", Ui.ShortPath(link.Dir))), "small muted", wrap: true));
        w.Dialog(link is null ? I18n.T("mine.build.new") : I18n.T("mine.import.link") + " · " + link.Name, body, 620,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
        _ = LoadVersions();
    }

    static Control Field(string label, Control input) => Ui.Col(6, Ui.Text(label, "small muted"), input);

    // ---------------------------------------------------------------- .mrpack

    public static async void ImportMrpack(string? file = null)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        file ??= await Pickers.Typed(w, I18n.T("mine.import.mrpack"), ["*.mrpack", "*.zip"], "Modrinth (.mrpack)");
        if (file is null) return;
        string title;
        try { title = McModrinth.ReadIndex(file).Name; }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); return; }
        w.Toast(I18n.T("mine.pack.started", ("title", title)));
        Jobs.Run(title, "Minecraft", async (_, progress, ct) =>
        {
            var i = await McModrinth.ImportPack(file, null, progress, ct);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Mc.Active = i;
                w.Toast(I18n.T("mine.pack.done", ("title", i.Name)));
                Mc.Notify();
            });
        });
    }

    // ---------------------------------------------------------------- переименовать, копия, удалить

    void RenameDialog(McInstance i)
    {
        var box = new TextBox { Text = i.Name };
        void Save() { W.CloseDialog(); Mc.Rename(i, box.Text ?? ""); }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Save(); };
        W.Dialog(I18n.T("mine.build.rename"), box, Ui.Button(I18n.T("common.cancel"), W.CloseDialog), Ui.Button(I18n.T("common.save"), Save, "primary", Icons.Save));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); }, Avalonia.Threading.DispatcherPriority.Background);
    }

    void DuplicateDialog(McInstance i)
    {
        var worlds = new CheckBox { Content = I18n.T("mine.build.duplicate.worlds") };
        W.Dialog(I18n.T("mine.build.duplicate"), Ui.Col(10, Ui.Text(I18n.T("mine.build.duplicate.text"), "muted", wrap: true), worlds),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("mine.build.duplicate"), () =>
            {
                var withWorlds = worlds.IsChecked == true;
                W.CloseDialog();
                W.Toast(I18n.T("mine.build.copying"));
                _ = Task.Run(() =>
                {
                    try
                    {
                        var copy = Mc.Duplicate(i, withWorlds);
                        McProfiles.Sync(copy);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => { W.Toast(I18n.T("mine.build.copied", ("build", copy.Name))); Mc.Notify(); });
                    }
                    catch (Exception e) { Avalonia.Threading.Dispatcher.UIThread.Post(() => W.Toast(Jobs.Explain(e), bad: true)); }
                });
            }, "primary", Icons.Copy));
    }

    void DeleteDialog(McInstance i)
    {
        var text = i.Linked ? I18n.T("mine.build.unlink.text", ("source", McImport.SourceTitle(i.Source ?? ""))) : I18n.T("mine.build.delete.text");
        W.Dialog(i.Linked ? I18n.T("mine.build.unlink") : I18n.T("mine.build.delete") + " · " + i.Name,
            Ui.Text(text, "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(i.Linked ? I18n.T("mine.build.unlink") : I18n.T("mine.build.delete"), () =>
            {
                W.CloseDialog();
                try { Mc.Delete(i, files: !i.Linked); W.Toast(I18n.T("mine.build.deleted", ("build", i.Name))); }
                catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
                _found = null;
                Build();
            }, "primary", Icons.Trash));
    }
}
