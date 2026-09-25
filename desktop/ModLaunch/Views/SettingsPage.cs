using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Настройки: те же разделы и те же ключи settings.json, что в 3.x.</summary>
public sealed class SettingsPage : Page
{
    string _tab;
    string? _keyStatus;
    ExeInfo? _dxvkPicked;
    string _accMode = "signin";
    bool _accBusy;
    int _versionClicks;
    DateTime _versionAt;
    string? _dxvkApi;

    public SettingsPage(string tab = "look") => _tab = tab;

    public override string Title => I18n.T("nav.settings");

    public override void Build()
    {
        var tabs = new StackPanel { Spacing = 4, Width = 230 };
        foreach (var (id, key, icon) in new[]
        {
            ("look", "settings.tab.look", Icons.Globe),
            ("games", "settings.tab.games", Icons.Folder),
            ("launch", "settings.tab.launch", Icons.Play),
            ("backups", "settings.tab.backups", Icons.Shield),
            ("graphics", "settings.tab.graphics", Icons.Sparkles),
            ("updates", "settings.tab.updates", Icons.ArrowUp),
            ("accounts", "settings.tab.accounts", Icons.Key),
            ("about", "settings.tab.about", Icons.Star),
        })
        {
            var b = Ui.Button(I18n.T(key), () => { _tab = id; Build(); }, "tab", icon);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (_tab == id) b.Classes.Add("active");
            tabs.Children.Add(b);
        }

        Control body = _tab switch
        {
            "games" => GamesTab(),
            "launch" => LaunchTab(),
            "backups" => BackupsTab(),
            "graphics" => GraphicsTab(),
            "updates" => UpdatesTab(),
            "accounts" => AccountsTab(),
            "about" => AboutTab(),
            _ => LookTab(),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1100 };
        var side = Ui.Card(tabs, 10);
        side.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(side);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        Content = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Section(string title, string? hint, params Control[] rows)
    {
        var col = Ui.Col(14, Ui.Text(title, "h2"));
        if (hint is not null) col.Children.Add(Ui.Text(hint, "muted", wrap: true));
        col.Children.AddRange(rows);
        return Ui.Card(col, 24);
    }

    /// <summary>Строка «название, пояснение — переключатель».</summary>
    Control Toggle(string title, string hint, bool value, Action<bool> set)
    {
        var sw = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        sw.IsCheckedChanged += (_, _) => { set(sw.IsChecked == true); Settings.Save(); };
        var row = new DockPanel();
        DockPanel.SetDock(sw, Dock.Right);
        row.Children.Add(sw);
        row.Children.Add(Ui.Col(3, Ui.Text(title, "h3"), Ui.Text(hint, "small muted", wrap: true)));
        return row;
    }

    Control LookTab()
    {
        var lang = new ComboBox { Width = 240 };
        lang.Items.Add("Русский");
        lang.Items.Add("English");
        lang.SelectedIndex = I18n.Lang == "en" ? 1 : 0;
        lang.SelectionChanged += (_, _) =>
        {
            var value = lang.SelectedIndex == 1 ? "en" : "ru";
            if (value == I18n.Lang) return;
            Settings.Language = value;
            I18n.Set(value);
        };
        return Section(I18n.T("settings.language"), null, lang);
    }

    Control GamesTab()
    {
        var list = new StackPanel { Spacing = 10 };
        foreach (var g in AppState.Games)
        {
            var status = g.Status switch
            {
                Detect.Found => g.Path!,
                Detect.Searching => g.SearchingWhere is null ? I18n.T("games.searching") : I18n.T("games.searchingWhere", ("where", g.SearchingWhere)),
                Detect.NotFound => I18n.T("games.notDetected"),
                _ => I18n.T("games.notSearched"),
            };
            var info = Ui.Col(4, Ui.Text(g.Def.Name, "h3"), Ui.Text(status, "small muted"));
            info.VerticalAlignment = VerticalAlignment.Center;
            var buttons = Ui.Row(6,
                Ui.Button("", () => Actions.PickGameFolder(g), "icon", Icons.Folder, I18n.T("games.setPath")),
                Ui.Button("", () => _ = AppState.DetectOne(g), "icon", Icons.Refresh, I18n.T("games.detectAgain")));
            if (g.Path is not null)
                buttons.Children.Add(Ui.Button("", () => { AppState.SetPath(g, null); MainWindow.Current?.Toast(I18n.T("toast.pathForgotten")); _ = AppState.DetectOne(g); }, "icon ghost", Icons.Trash, I18n.T("games.forget")));
            buttons.VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            var art = g.Def.Art is null ? null : Images.Asset(g.Def.Art, 120);
            grid.Children.Add(new Border
            {
                Width = 56, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                Background = Ui.Hex(g.Def.Accent),
                Child = art is null ? null : new Image { Source = art, Stretch = Stretch.UniformToFill },
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = grid });
        }
        var guide = Ui.Col(6, Ui.Text(I18n.T("guide.title"), "h3"),
            Ui.Text(I18n.T("guide.steam"), "small muted", wrap: true), Ui.Text(I18n.T("guide.gog"), "small muted", wrap: true),
            Ui.Text(I18n.T("guide.repack"), "small muted", wrap: true), Ui.Text(I18n.T("guide.check"), "small muted", wrap: true));
        return Section(I18n.T("settings.games"), I18n.T("settings.games.hint"), list,
            Ui.Button(I18n.T("games.deep"), () => { foreach (var g in AppState.Games.Where(x => x.Status != Detect.Found)) _ = AppState.DetectOne(g, deep: true); }, "", Icons.Search),
            guide);
    }

    // ---------------------------------------------------------------- запуск и игровое время

    Control LaunchTab()
    {
        var col = new StackPanel { Spacing = 16 };
        var after = new ComboBox { Width = 260 };
        after.Items.Add(I18n.T("launch.after.stay"));
        after.Items.Add(I18n.T("launch.after.minimize"));
        after.SelectedIndex = Settings.Data.Str("afterLaunch") == "minimize" ? 1 : 0;
        after.SelectionChanged += (_, _) => { Settings.Data["afterLaunch"] = after.SelectedIndex == 1 ? "minimize" : "stay"; Settings.Save(); Build(); };
        var afterRow = new DockPanel();
        DockPanel.SetDock(after, Dock.Right);
        afterRow.Children.Add(after);
        afterRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("launch.after"), "h3"), Ui.Text(I18n.T("launch.after.hint"), "small muted")));
        var rows = new List<Control> { afterRow };
        if (Settings.Data.Str("afterLaunch") == "minimize")
            rows.Add(Toggle(I18n.T("launch.restore"), I18n.T("launch.restore.hint"), Settings.Data.Bool("restoreAfterGame", true), v => Settings.Data["restoreAfterGame"] = v));
        rows.Add(Toggle(I18n.T("launch.track"), I18n.T("launch.track.hint"), PlayTime.Track, v => Settings.Data["trackPlaytime"] = v));
        col.Children.Add(Section(I18n.T("settings.tab.launch"), null, rows.ToArray()));

        // Оверлей в игре.
        var key = new ComboBox { Width = 200 };
        foreach (var k in Hotkey.Keys) key.Items.Add(Hotkey.Display(k));
        key.SelectedIndex = Array.IndexOf(Hotkey.Keys, Hotkey.KeyOf(Settings.Data.Str("overlayKey")));
        key.SelectionChanged += (_, _) => { if (key.SelectedIndex >= 0) { Settings.Data["overlayKey"] = Hotkey.Keys[key.SelectedIndex]; Settings.Save(); } };
        var keyRow = new DockPanel();
        DockPanel.SetDock(key, Dock.Right);
        keyRow.Children.Add(key);
        keyRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("ov.key"), "h3"), Ui.Text(I18n.T("ov.key.hint"), "small muted")));
        var preview = new DockPanel();
        var go = Ui.Button(I18n.T("ov.preview.go"), () =>
        {
            OverlayWindow.GameId = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found)?.Def.Id;
            OverlayWindow.StartedAt ??= DateTime.UtcNow;
            OverlayWindow.Preview = true;
            OverlayWindow.Toggle();
        }, "", Icons.Play);
        DockPanel.SetDock(go, Dock.Right);
        preview.Children.Add(go);
        preview.Children.Add(Ui.Col(3, Ui.Text(I18n.T("ov.preview"), "h3"), Ui.Text(I18n.T("ov.preview.hint"), "small muted")));
        col.Children.Add(Section(I18n.T("ov.title"), null,
            Toggle(I18n.T("ov.on"), I18n.T("ov.on.hint"), Settings.Data.Bool("overlay", true), v => Settings.Data["overlay"] = v),
            keyRow, preview));

        // Параметры запуска по играм.
        var found = AppState.Games.Where(g => g.Status == Detect.Found).ToList();
        var args = new StackPanel { Spacing = 10 };
        if (found.Count == 0) args.Children.Add(Ui.Text(I18n.T("launch.noGames"), "muted"));
        foreach (var g in found)
        {
            var box = new TextBox { Text = Launcher.Args(g.Def.Id), Watermark = I18n.T("launch.args.placeholder") };
            var id = g.Def.Id;
            box.LostFocus += (_, _) =>
            {
                if ((box.Text ?? "").Trim() == Launcher.Args(id)) return;
                Launcher.SetArgs(id, box.Text ?? "");
                MainWindow.Current?.Toast(I18n.T("launch.args.saved"));
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), ColumnSpacing = 12 };
            var label = Ui.Text(g.Def.Name, "h3");
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            args.Children.Add(row);
        }
        col.Children.Add(Section(I18n.T("launch.args"), I18n.T("launch.args.hint"), args));

        // Игровое время.
        var times = new StackPanel { Spacing = 8 };
        var any = false;
        foreach (var g in AppState.Games)
        {
            var p = PlayTime.Get(g.Def.Id);
            if (p.TotalMs <= 0 && !p.Running) continue;
            any = true;
            var sessions = I18n.T("time.sessions." + I18n.Plural(p.Sessions, "one", "few", "many"), ("n", p.Sessions));
            var line = $"{PlayTime.Format(p.TotalMs)} · {sessions}" + (p.LastPlayed is { } last ? " · " + I18n.T("time.last", ("when", Ui.Ago(last))) : "");
            var row = new DockPanel();
            var value = Ui.Text(line, "muted");
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(Ui.Text(g.Def.Name, "h3"));
            times.Children.Add(row);
        }
        if (!any) times.Children.Add(Ui.Text(I18n.T("launch.time.none"), "muted", wrap: true));
        else times.Children.Add(Ui.Button(I18n.T("launch.time.reset"), () =>
        {
            var w = MainWindow.Current!;
            w.Dialog(I18n.T("launch.time.reset"), Ui.Text(I18n.T("launch.time.reset.hint"), "muted"),
                Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
                Ui.Button(I18n.T("launch.time.reset.go"), () => { w.CloseDialog(); PlayTime.Reset(null); Build(); }, "primary"));
        }, "ghost", Icons.Trash));
        col.Children.Add(Section(I18n.T("launch.time"), null, times));
        return col;
    }

    // ---------------------------------------------------------------- резервные копии

    Control BackupsTab()
    {
        var col = new StackPanel { Spacing = 16 };
        var keep = new NumericUpDown { Minimum = 1, Maximum = 200, Value = Backups.Keep, Width = 140, FormatString = "0" };
        keep.ValueChanged += (_, _) => { Settings.Data["backupKeep"] = (int)(keep.Value ?? 10); Settings.Save(); };
        var keepRow = new DockPanel();
        DockPanel.SetDock(keep, Dock.Right);
        keepRow.Children.Add(keep);
        keepRow.Children.Add(Ui.Col(3, Ui.Text(I18n.T("bak.keep"), "h3"), Ui.Text(I18n.T("bak.keep.hint"), "small muted")));
        col.Children.Add(Section(I18n.T("settings.tab.backups"), null,
            Toggle(I18n.T("bak.onLaunch"), I18n.T("bak.onLaunch.hint"), Backups.OnLaunch, v => Settings.Data["backupOnLaunch"] = v),
            keepRow,
            Ui.Row(10, Ui.Text(I18n.T("bak.folder") + ": " + Backups.Root, "small muted"), Ui.Button("", () => { Directory.CreateDirectory(Backups.Root); Actions.OpenFolder(Backups.Root); }, "icon ghost", Icons.Folder))));

        var list = new StackPanel { Spacing = 8 };
        foreach (var g in AppState.Games)
        {
            var items = Backups.List(g.Def.Id);
            var text = items.Count == 0 ? I18n.T("bak.summary.none") : I18n.T("bak.summary",
                ("n", I18n.T("bak.copies." + I18n.Plural(items.Count, "one", "few", "many"), ("n", items.Count))),
                ("size", GamePage.Size(items.Sum(b => b.Size))),
                ("when", Ui.Ago(items[0].At.ToUniversalTime())));
            var row = new DockPanel();
            var value = Ui.Text(text, "muted");
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(Ui.Text(g.Def.Name, "h3"));
            list.Children.Add(row);
        }
        col.Children.Add(Section(I18n.T("bak.games"), I18n.T("bak.games.hint"), list));
        return col;
    }

    // ---------------------------------------------------------------- DXVK

    Control GraphicsTab()
    {
        var col = new StackPanel { Spacing = 16 };
        var intro = Section(I18n.T("dxvk.title"), I18n.T("dxvk.lead"),
            Ui.Text("• " + I18n.T("dxvk.note.vulkan"), "small muted", wrap: true),
            Ui.Text("• " + I18n.T("dxvk.note.online"), "small muted", wrap: true),
            Ui.Button(I18n.T("dxvk.add"), PickExe, "primary", Icons.FilePlus));
        col.Children.Add(intro);
        if (_dxvkPicked is not null) col.Children.Add(DxvkPicked(_dxvkPicked));

        var list = new StackPanel { Spacing = 8 };
        var entries = Dxvk.List();
        if (entries.Count == 0) list.Children.Add(Ui.Text(I18n.T("dxvk.empty"), "muted"));
        foreach (var e in entries)
        {
            var status = !e.Exists ? I18n.T("dxvk.missing")
                : e.State.Installed ? I18n.T("dxvk.on", ("version", e.State.Version), ("api", ApiName(e.State.Api)), ("bits", e.State.Arch == "x64" ? "64" : "32"))
                : I18n.T("dxvk.off");
            var info = Ui.Col(3, Ui.Text(e.Name, "h3"), Ui.Text(status, "small", color: e.State.Installed ? Ui.Res("Good") : Ui.Res("Muted")));
            info.VerticalAlignment = VerticalAlignment.Center;
            var exe = e.Exe;
            var buttons = Ui.Row(6);
            if (e.Exists && e.State.Installed)
                buttons.Children.Add(Ui.Button(I18n.T("dxvk.remove"), () =>
                {
                    try { Dxvk.Remove(Path.GetDirectoryName(exe)!); MainWindow.Current?.Toast(I18n.T("dxvk.removed")); } catch (Exception x) { MainWindow.Current?.Toast(Jobs.Explain(x), bad: true); }
                    Build();
                }));
            else if (e.Exists)
                buttons.Children.Add(Ui.Button(I18n.T("dxvk.install"), () => { _dxvkPicked = Dxvk.Inspect(exe); _dxvkApi = _dxvkPicked.Api; Build(); }, "primary"));
            buttons.Children.Add(Ui.Button("", () => Actions.OpenFolder(exe), "icon ghost", Icons.Folder));
            buttons.Children.Add(Ui.Button("", () => { Dxvk.Forget(exe); Build(); }, "icon ghost", Icons.Trash, I18n.T("dxvk.forget")));
            buttons.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(info);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row });
        }
        col.Children.Add(Section(I18n.T("dxvk.games"), null, list));
        return col;
    }

    static string ApiName(string? api) => api switch
    {
        "dx8" => "DirectX 8", "dx9" => "DirectX 9", "dx10" => "DirectX 10", "dx11" => "DirectX 11", "dx12" => "DirectX 12",
        "opengl" => "OpenGL", "vulkan" => "Vulkan", _ => I18n.T("dxvk.api.unknown"),
    };

    async void PickExe()
    {
        var exe = await MainWindow.Current!.PickExe(I18n.T("dialog.pickExe"));
        if (exe is null) return;
        try { _dxvkPicked = Dxvk.Inspect(exe); _dxvkApi = _dxvkPicked.Supported ? _dxvkPicked.Api : null; }
        catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); return; }
        Build();
    }

    Control DxvkPicked(ExeInfo info)
    {
        var state = Dxvk.Status(info.Dir);
        var col = Ui.Col(12,
            Ui.Text(info.Name, "h2"),
            Ui.Text($"{info.Exe} · {(info.Arch == "x64" ? I18n.T("dxvk.bits64") : I18n.T("dxvk.bits32"))} · {ApiName(info.Api)} ({(info.Api is null ? I18n.T("dxvk.notDetected") : I18n.T("dxvk.detected"))})", "small muted", wrap: true));
        if (info.Api is "dx12") col.Children.Add(Ui.Text(I18n.T("dxvk.dx12", ("api", ApiName(info.Api))), "muted", wrap: true));
        else if (info.Api is "vulkan" or "opengl") col.Children.Add(Ui.Text(I18n.T("dxvk.native", ("api", ApiName(info.Api))), "muted", wrap: true));
        if (state.Installed) col.Children.Add(Ui.Text(I18n.T("dxvk.reinstall", ("version", state.Version)), "small brand"));

        col.Children.Add(Ui.Text(info.Supported ? I18n.T("dxvk.choose.change") : I18n.T("dxvk.choose"), "small muted", wrap: true));
        var chips = Ui.Row(8);
        foreach (var api in new[] { "dx8", "dx9", "dx10", "dx11" })
        {
            var a = api;
            var chip = Ui.Button(ApiName(api), () => { _dxvkApi = a; Build(); }, "chip");
            if (_dxvkApi == api) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        col.Children.Add(chips);
        if (!info.Supported) col.Children.Add(Ui.Text(I18n.T("dxvk.choose.hint"), "small muted", wrap: true));

        var busy = Jobs.All.Any(j => j.Status == JobStatus.Running && j.Title == "DXVK");
        var go = Ui.Button(busy ? I18n.T("dxvk.working") : I18n.T("dxvk.install"), () =>
        {
            var exe = info.Exe;
            var api = _dxvkApi;
            var name = info.Name;
            Jobs.Run("DXVK", name, async (_, progress, ct) =>
            {
                var (version, _) = await Dxvk.Install(exe, api, progress, ct);
                Dxvk.Remember(exe, name);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { MainWindow.Current?.Toast(I18n.T("dxvk.done", ("version", version), ("name", name))); _dxvkPicked = null; Build(); });
            });
            Build();
        }, "primary", Icons.Download);
        go.IsEnabled = !busy && _dxvkApi is not null;
        col.Children.Add(Ui.Row(10, Ui.Button(I18n.T("common.cancel"), () => { _dxvkPicked = null; Build(); }), go));
        return Ui.Card(col, 22);
    }

    // ---------------------------------------------------------------- обновления модов

    Control UpdatesTab()
    {
        var col = new StackPanel { Spacing = 16 };
        col.Children.Add(Section(I18n.T("settings.tab.updates"), null,
            Toggle(I18n.T("upd.onStart"), I18n.T("upd.onStart.hint"), ModUpdates.OnStart, v => Settings.Data["checkModUpdates"] = v),
            Ui.Row(10, Ui.Button(I18n.T("upd.checkAll"), CheckAll, "primary", Icons.Refresh)),
            Ui.Text(I18n.T("upd.checkAll.hint"), "small muted", wrap: true)));

        var list = new StackPanel { Spacing = 8 };
        var total = 0;
        foreach (var g in AppState.Games)
        {
            if (!ModUpdates.Found.TryGetValue(g.Def.Id, out var ups) || ups.Count == 0) continue;
            total += ups.Count;
            var id = g.Def.Id;
            var row = new DockPanel();
            var open = Ui.Button(I18n.T("upd.found." + I18n.Plural(ups.Count, "one", "few", "many"), ("n", ups.Count)), () => MainWindow.Current?.Navigate(() => new GamePage(id, "installed")), "", Icons.ArrowUp);
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
            row.Children.Add(Ui.Col(3, Ui.Text(g.Def.Name, "h3"), Ui.Text(string.Join(", ", ups.Select(u => $"{u.Name} {u.Current} → {u.Latest}").Take(4)), "small muted", wrap: true)));
            list.Children.Add(row);
        }
        if (total == 0) list.Children.Add(Ui.Text(ModUpdates.Found.IsEmpty ? I18n.T("upd.never") : I18n.T("upd.none"), "muted"));
        col.Children.Add(Section(I18n.T("upd.list"), null, list));
        return col;
    }

    async void CheckAll()
    {
        MainWindow.Current?.Toast(I18n.T("upd.checking"));
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && g.ModCount > 0))
        {
            try { await ModUpdates.Check(g); } catch { }
        }
        Build();
    }

    // ---------------------------------------------------------------- аккаунты

    Control AccountsTab()
    {
        var box = new TextBox { Text = Settings.NexusApiKey ?? "", PasswordChar = '•', Watermark = "API key", Width = 420 };
        var check = Ui.Button(I18n.T("settings.nexus.check"), async () =>
        {
            var key = box.Text?.Trim() ?? "";
            Settings.Data["nexusApiKey"] = key == "" ? null : key;
            Settings.Data["nexusPremium"] = false;
            Settings.Save();
            if (key == "") { _keyStatus = null; Build(); return; }
            try
            {
                var (name, premium) = await Nexus.ValidateKey(key);
                Settings.Data["nexusPremium"] = premium;
                Settings.Save();
                _keyStatus = I18n.T("settings.nexus.ok", ("name", name)) + (premium ? " · Premium" : "");
            }
            catch (Exception e) { _keyStatus = Jobs.Explain(e); }
            Build();
        }, "primary", Icons.Check);
        var rows = new List<Control>
        {
            Ui.Row(10, box, check),
            Ui.Button("nexusmods.com/users/myaccount?tab=api", () => Ui.OpenUrl("https://www.nexusmods.com/users/myaccount?tab=api"), "ghost", Icons.External),
        };
        if (_keyStatus is not null) rows.Insert(1, Ui.Text(_keyStatus, "small brand"));

        var registered = Nxm.IsRegistered();
        var nxm = new DockPanel();
        if (!registered && OperatingSystem.IsWindows())
        {
            var fix = Ui.Button(I18n.T("common.continue"), () => { try { Nxm.Register(); } catch (Exception e) { MainWindow.Current?.Toast(e.Message, bad: true); } Build(); }, "", Icons.Link);
            DockPanel.SetDock(fix, Dock.Right);
            nxm.Children.Add(fix);
        }
        nxm.Children.Add(Ui.Col(3, Ui.Text(I18n.T("settings.nxm"), "h3"),
            Ui.Text(registered ? I18n.T("settings.nxm.yes") : I18n.T("settings.nxm.no"), "small", color: registered ? Ui.Res("Good") : Ui.Res("Muted"))));
        rows.Add(nxm);
        return Ui.Col(16, AccountSection(), Section(I18n.T("settings.nexus"), I18n.T("settings.nexus.hint"), rows.ToArray()));
    }

    // ---------------------------------------------------------------- аккаунт ModLaunch

    Control AccountSection()
    {
        var p = Social.Account.Get();
        if (!p.Configured) return Section(I18n.T("acc.title"), I18n.T("acc.off"));
        return p.SignedIn ? SignedIn(p) : SignForm();
    }

    async void AccountCall(Func<Task> action, string? ok = null)
    {
        _accBusy = true;
        Build();
        try
        {
            await action();
            if (ok is not null) MainWindow.Current?.Toast(ok);
        }
        catch (Exception e) { MainWindow.Current?.Toast(Social.Account.Explain(e), bad: true); }
        _accBusy = false;
        Build();
    }

    Control SignForm()
    {
        var tabs = Ui.Row(6);
        foreach (var (id, key) in new[] { ("signin", "acc.tab.signin"), ("signup", "acc.tab.signup") })
        {
            var b = Ui.Button(I18n.T(key), () => { _accMode = id; Build(); }, "chip");
            if (_accMode == id) b.Classes.Add("active");
            tabs.Children.Add(b);
        }
        var name = new TextBox { Watermark = I18n.T("acc.name"), MaxLength = 32 };
        var email = new TextBox { Watermark = I18n.T("acc.email") };
        var password = new TextBox { Watermark = I18n.T("acc.password"), PasswordChar = '•', MaxLength = 128 };
        var strength = Ui.Text(I18n.T("acc.pass.0"), "small muted");
        password.TextChanged += (_, _) =>
        {
            var t = password.Text ?? "";
            var score = t.Length < Social.Account.MinPassword ? 0 : (t.Length >= 12 ? 1 : 0) + (t.Any(char.IsDigit) && t.Any(char.IsLetter) ? 1 : 0) + (t.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
            strength.Text = I18n.T($"acc.pass.{Math.Clamp(score, 0, 3)}");
        };

        var col = Ui.Col(12);
        if (_accMode == "reset")
        {
            col.Children.Add(Ui.Text(I18n.T("acc.reset.title"), "h2"));
            col.Children.Add(Ui.Text(I18n.T("acc.reset.lead"), "muted", wrap: true));
            col.Children.Add(email);
            col.Children.Add(Ui.Row(10,
                Ui.Button(_accBusy ? I18n.T("acc.reset.busy") : I18n.T("acc.reset.go"), () => AccountCall(() => Social.Account.ResetPassword(email.Text), I18n.T("acc.reset.sent", ("email", email.Text ?? ""))), "primary"),
                Ui.Button(I18n.T("acc.backToSignin"), () => { _accMode = "signin"; Build(); }, "ghost")));
            return Ui.Card(col, 24);
        }
        col.Children.Add(Ui.Text(I18n.T("acc.out.title"), "h2"));
        col.Children.Add(Ui.Col(4, Ui.Text("✓ " + I18n.T("acc.perk.1"), "small muted"), Ui.Text("✓ " + I18n.T("acc.perk.2"), "small muted"), Ui.Text("✓ " + I18n.T("acc.perk.3"), "small muted")));
        col.Children.Add(tabs);
        if (_accMode == "signup") col.Children.Add(Ui.Col(4, name, Ui.Text(I18n.T("acc.name.hint"), "small muted")));
        col.Children.Add(email);
        col.Children.Add(password);
        if (_accMode == "signup") col.Children.Add(strength);
        var go = _accMode == "signup"
            ? Ui.Button(_accBusy ? I18n.T("acc.signup.busy") : I18n.T("acc.signup.go"), () => AccountCall(async () =>
              {
                  var p = await Social.Account.SignUp(name.Text ?? "", email.Text ?? "", password.Text ?? "");
                  MainWindow.Current?.Toast(I18n.T("acc.welcomeNew", ("name", p.Name ?? "")));
              }), "primary", Icons.User)
            : Ui.Button(_accBusy ? I18n.T("acc.signin.busy") : I18n.T("acc.signin.go"), () => AccountCall(async () =>
              {
                  var p = await Social.Account.SignIn(email.Text ?? "", password.Text ?? "");
                  MainWindow.Current?.Toast(I18n.T("acc.welcome", ("name", p.Name ?? "")));
              }), "primary", Icons.Key);
        go.IsEnabled = !_accBusy;
        var buttons = Ui.Row(10, go);
        if (_accMode == "signin") buttons.Children.Add(Ui.Button(I18n.T("acc.forgot"), () => { _accMode = "reset"; Build(); }, "ghost"));
        col.Children.Add(buttons);
        col.Children.Add(Ui.Text(I18n.T("acc.note"), "small muted", wrap: true));
        return Ui.Card(col, 24);
    }

    Control SignedIn(Social.Profile p)
    {
        var col = Ui.Col(14);
        var who = Ui.Row(12,
            new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Ui.Res("BrandSoft"), Child = new TextBlock { Text = (p.Name ?? "?")[..1].ToUpperInvariant(), FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
            Ui.Col(3, Ui.Text(p.Name ?? "", "h2"), Ui.Text(p.Email ?? "", "small muted"),
                Ui.Text(p.Verified ? "✓ " + I18n.T("acc.verified") : I18n.T("acc.unverified"), "small", color: p.Verified ? Ui.Res("Good") : Ui.Res("Warn"))));
        col.Children.Add(Ui.Text(I18n.T("acc.title"), "h2"));
        col.Children.Add(who);
        if (p.Admin) col.Children.Add(Ui.Col(3, Ui.Text("👑 " + I18n.T("acc.admin.title"), "h3"), Ui.Text(I18n.T("acc.admin.text"), "small muted", wrap: true)));
        else if (p.AdminPending) col.Children.Add(Ui.Text(I18n.T("acc.admin.pending"), "small muted", wrap: true));
        if (!p.Verified)
        {
            col.Children.Add(Ui.Text(I18n.T("acc.verify.text", ("email", p.Email ?? "")), "small muted", wrap: true));
            col.Children.Add(Ui.Row(8,
                Ui.Button(I18n.T("acc.verify.again"), () => AccountCall(Social.Account.SendVerification, I18n.T("acc.verify.sent", ("email", p.Email ?? "")))),
                Ui.Button(I18n.T("acc.verify.check"), () => AccountCall(async () =>
                {
                    var fresh = await Social.Account.RefreshProfile();
                    MainWindow.Current?.Toast(fresh.Verified ? I18n.T("acc.verify.done") : I18n.T("acc.verify.notYet"), bad: !fresh.Verified);
                }), "primary")));
        }
        var rename = new TextBox { Text = p.Name, MaxLength = 32, Width = 260 };
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("acc.rename"), "h3"), Ui.Text(I18n.T("acc.rename.hint"), "small muted"),
            Ui.Row(8, rename, Ui.Button(I18n.T("common.save"), () => AccountCall(() => Social.Account.Rename(rename.Text ?? ""), I18n.T("acc.renamed"))))));
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("acc.password.change"), "h3"), Ui.Text(I18n.T("acc.password.hint", ("email", p.Email ?? "")), "small muted"),
            Ui.Button(I18n.T("acc.password.send"), () => AccountCall(() => Social.Account.ResetPassword(p.Email), I18n.T("acc.password.sent", ("email", p.Email ?? ""))))));
        col.Children.Add(Ui.Row(10,
            Ui.Button(I18n.T("acc.signout"), () => { Social.Account.SignOut(); MainWindow.Current?.Toast(I18n.T("acc.signedOut")); Build(); }, "", Icons.Close),
            Ui.Button(I18n.T("acc.delete.title"), DeleteAccount, "ghost", Icons.Trash)));
        col.Children.Add(Ui.Text(p.Since is DateTime since ? I18n.T("acc.delete.hint", ("since", since.ToString("d"))) : I18n.T("acc.delete.hintPlain"), "small muted"));
        return Ui.Card(col, 24);
    }

    void DeleteAccount()
    {
        var w = MainWindow.Current!;
        var password = new TextBox { Watermark = I18n.T("acc.password"), PasswordChar = '•' };
        w.Dialog(I18n.T("acc.delete.title"), Ui.Col(10, Ui.Text(I18n.T("acc.delete.text"), "muted", wrap: true), password),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("acc.delete.go"), () =>
            {
                var pass = password.Text ?? "";
                w.CloseDialog();
                AccountCall(async () =>
                {
                    try { await Social.Friends.Forget(); } catch { }
                    await Social.Account.Delete(pass);
                }, I18n.T("acc.deleted"));
            }, "primary", Icons.Trash));
    }

    Control AboutTab()
    {
        var logo = new Image { Source = Images.Asset("icon.png", 128), Width = 64, Height = 64 };
        var version = new Button { Classes = { "ghost" }, Padding = new Thickness(0), Content = Ui.Text($"{I18n.T("upd.app.version", ("version", Http.Version))} · Avalonia UI · .NET {Environment.Version.ToString(2)}", "muted small") };
        version.Click += (_, _) =>
        {
            // Пять щелчков по версии — включить или спрятать «Статистику» (для владельца), как в 3.x.
            if (DateTime.UtcNow - _versionAt > TimeSpan.FromSeconds(1.5)) _versionClicks = 0;
            _versionAt = DateTime.UtcNow;
            if (++_versionClicks < 5) return;
            _versionClicks = 0;
            var on = !Settings.Data.Bool("ownerStats");
            Settings.Data["ownerStats"] = on;
            Settings.Save();
            MainWindow.Current?.Toast(I18n.T(on ? "stats.enabled" : "stats.disabled"));
            AppState.Notify();
        };
        var name = Ui.Col(4, Ui.Text("ModLaunch", "h2"), version);
        name.VerticalAlignment = VerticalAlignment.Center;

        var latest = Setup.Updater.Latest;
        var status = !Setup.Updater.Configured ? I18n.T("upd.app.off")
            : latest is null ? I18n.T("upd.never")
            : Setup.Updater.Available ? I18n.T("upd.app.available", ("version", latest.Version))
            : I18n.T("upd.app.latest", ("version", Http.Version));
        var updates = Ui.Col(10, Ui.Text(status, "h3"), Ui.Text(I18n.T("upd.app.hint"), "small muted", wrap: true),
            Ui.Row(10,
                Ui.Button(I18n.T("upd.check"), async () =>
                {
                    try { await Setup.Updater.Check(); } catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
                    Build();
                }, "", Icons.Refresh),
                Setup.Updater.Available ? Ui.Button(I18n.T("upd.app.open"), () => MainWindow.Current?.ShowUpdate(), "primary", Icons.Sparkles) : new Panel()),
            Toggle(I18n.T("upd.app.auto"), I18n.T("upd.app.auto.hint"), Setup.Updater.AutoDownload, v => Settings.Data["autoDownloadUpdates"] = v));

        return Section(I18n.T("settings.tab.about"), null,
            Ui.Row(16, logo, name),
            updates,
            Ui.Row(10,
                Ui.Button("GitHub", () => Ui.OpenUrl("https://github.com/ModLaunch/ModLaunch"), "", Icons.External),
                Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(Paths.DataDir), "", Icons.Folder)));
    }
}
