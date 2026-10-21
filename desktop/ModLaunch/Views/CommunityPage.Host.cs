using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Minecraft;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: вкладка «Свой сервер» — сервер Minecraft на этом ПК: создать, принять EULA, запустить и остановить,
/// консоль с командами, игроки онлайн, адрес в домашней сети, моды из сборки Fabric и «Выложить на доску».
/// </summary>
public sealed partial class CommunityPage
{
    static string? _hostId;
    string _hostKey = "";
    string _command = "";
    McHostRun? _consoleRun;
    SelectableTextBlock? _console;
    ScrollViewer? _consoleScroll;
    TextBlock? _hostPlayers;
    volatile bool _consoleDirty;
    DispatcherTimer? _consoleTimer;

    /// <summary>Для снимков: какой свой сервер открыть.</summary>
    internal static void SelectHost(string id) => _hostId = id;

    /// <summary>Что видно на вкладке, кроме числа игроков: поменялось — вкладка перестраивается, иначе обновляется только число.</summary>
    static string HostKey(List<McHosted> list, McHosted h, McHostRun? run) =>
        $"{string.Join(",", list.Select(x => x.Id + ":" + (McHost.Running(x.Id)?.State ?? "")))}|{h.Id}|{run?.State}|{McHost.EulaAccepted(h)}|{h.Board}|{McHost.ModCount(h)}|{h.Installed}";

    void OnHost() => Dispatcher.UIThread.Post(() =>
    {
        if (_tab != "host") return;
        var list = McHost.List();
        var h = list.FirstOrDefault(x => x.Id == _hostId) ?? list.FirstOrDefault();
        if (h is null || HostKey(list, h, McHost.Running(h.Id)) != _hostKey) { Build(); return; }
        if (_hostPlayers is not null) _hostPlayers.Text = PlayersText(McHost.Running(h.Id), h);
    });

    void OnConsoleLine(string _) => _consoleDirty = true;

    void DetachConsole()
    {
        if (_consoleRun is not null) _consoleRun.Line -= OnConsoleLine;
        _consoleRun = null;
        _consoleTimer?.Stop();
    }

    void AttachConsole(McHostRun run)
    {
        DetachConsole();
        _consoleRun = run;
        run.Line += OnConsoleLine;
        if (_consoleTimer is null)
        {
            _consoleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _consoleTimer.Tick += (_, _) =>
            {
                if (!_consoleDirty || _consoleRun is null || _console is null) return;
                _consoleDirty = false;
                _console.Text = ConsoleText(_consoleRun);
                _consoleScroll?.ScrollToEnd();
            };
        }
        _consoleTimer.Start();
    }

    static string ConsoleText(McHostRun run) => string.Join('\n', run.Lines.TakeLast(300));

    static string PlayersText(McHostRun? run, McHosted h) =>
        run is { State: "running" } ? I18n.T("v11.cm.host.players", ("n", run.Players), ("max", run.Max)) : I18n.T("v11.cm.host.playersMax", ("max", h.MaxPlayers));

    Control Host()
    {
        var list = McHost.List();
        var lan = McHost.LanIp();
        var create = Ui.Button(I18n.T("v11.cm.host.new"), () => HostDialog(null), "primary", Icons.Plus);
        create.VerticalAlignment = VerticalAlignment.Top;
        var intro = Ui.Col(8,
            Ui.Row(10, Ui.Icon(Icons.Cube, 20, Ui.Res("Brand2")), Ui.Text(I18n.T("v11.cm.host.title"), "h2")),
            Ui.Text(I18n.T("v11.cm.host.lead"), "muted", wrap: true));
        intro.Margin = new Thickness(0, 0, 16, 0);
        var top = new DockPanel();
        DockPanel.SetDock(create, Dock.Right);
        top.Children.Add(create);
        top.Children.Add(intro);
        var col = Ui.Col(16, Ui.Card(top, 20));
        if (list.Count == 0)
        {
            _hostKey = "";
            DetachConsole();
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v11.cm.host.empty.title"), "h3"), Ui.Text(I18n.T("v11.cm.host.empty.text"), "muted", wrap: true)), 22));
            return col;
        }
        var h = list.FirstOrDefault(x => x.Id == _hostId) ?? list[0];
        _hostId = h.Id;
        var run = McHost.Running(h.Id);
        _hostKey = HostKey(list, h, run);

        var side = Ui.Col(8);
        foreach (var x in list)
        {
            var r = McHost.Running(x.Id);
            var on = x.Id == h.Id;
            var dot = Ui.Dot(r?.State switch { "running" => Ui.Res("Good"), "starting" or "stopping" => Ui.Res("Warn"), _ => Ui.Res("Faint") }, 9);
            var item = new DockPanel();
            DockPanel.SetDock(dot, Dock.Right);
            item.Children.Add(dot);
            item.Children.Add(Ui.Col(2, Ui.Text(x.Name, "h3"), Ui.Text(x.Label, "small muted")));
            var tile = new Border
            {
                Classes = { "store-tile" }, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), BorderThickness = new Thickness(1),
                Background = on ? Ui.Res("BrandSoft") : Ui.Res("Surface"), BorderBrush = on ? Ui.Res("Brand") : Ui.Res("Line"), Child = item,
            };
            var id = x.Id;
            StoreKit.OnClick(tile, () => { _hostId = id; Build(); });
            side.Children.Add(tile);
        }

        var detail = Ui.Col(16, HostCard(h, run, lan), ConsoleCard(h, run));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*"), ColumnSpacing = 16 };
        grid.Children.Add(side);
        Grid.SetColumn(detail, 1);
        grid.Children.Add(detail);
        col.Children.Add(grid);
        return col;
    }

    Control HostCard(McHosted h, McHostRun? run, string? lan)
    {
        var w = MainWindow.Current!;
        var state = run?.State ?? "stopped";
        var dot = state switch { "running" => Ui.Res("Good"), "starting" or "stopping" => Ui.Res("Warn"), _ => Ui.Res("Faint") };
        var facts = new List<string> { h.Label, I18n.T("v11.cm.host.port", ("port", h.Port)), I18n.T("v11.cm.host.at." + h.Difficulty) };
        var mods = McHost.ModCount(h);
        if (h.Fabric) facts.Add(ModsText(mods));
        if (h.WhiteList) facts.Add(I18n.T("v11.cm.host.whitelisted"));
        _hostPlayers = Ui.Text(PlayersText(run, h), "small muted");
        var status = Ui.Col(6, StoreKit.Pill(I18n.T("v11.cm.host.state." + state), dot, onArt: false), _hostPlayers);
        status.VerticalAlignment = VerticalAlignment.Top;
        ((Control)status.Children[0]).HorizontalAlignment = HorizontalAlignment.Right;
        _hostPlayers.HorizontalAlignment = HorizontalAlignment.Right;
        var head = new DockPanel();
        DockPanel.SetDock(status, Dock.Right);
        head.Children.Add(status);
        head.Children.Add(Ui.Col(4, Ui.Text(h.Name, "h2"), Ui.Text(string.Join(" · ", facts), "small muted")));

        // EULA: галочку ставит только человек; принятую — не снимаем (eula.txt уже в папке сервера).
        var accepted = McHost.EulaAccepted(h);
        var eula = new CheckBox { Content = I18n.T("v11.cm.host.eula"), IsChecked = accepted, IsEnabled = !accepted, VerticalAlignment = VerticalAlignment.Center };
        eula.IsCheckedChanged += (_, _) =>
        {
            if (eula.IsChecked != true) return;
            try { McHost.AcceptEula(h); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { w.Toast(Jobs.Explain(e), bad: true); eula.IsChecked = false; }
        };
        var eulaRow = Ui.Col(4, Ui.Row(10, eula, Ui.Button(I18n.T("v11.cm.host.eula.read"), () => Ui.OpenUrl(McHost.EulaUrl), "ghost", Icons.External)));
        if (!accepted) eulaRow.Children.Add(Ui.Text(I18n.T("v11.cm.host.eula.hint"), "small faint", wrap: true));

        var actions = new WrapPanel();
        void Add(Button b, bool enabled = true)
        {
            b.IsEnabled = enabled;
            b.Margin = new Thickness(0, 0, 8, 8);
            actions.Children.Add(b);
        }
        if (run is { Active: true } active)
            Add(Ui.Button(I18n.T("v11.cm.host.stop"), () => _ = active.Stop(), "", Icons.Stop), state != "stopping");
        else
            Add(Ui.Button(I18n.T("v11.cm.host.start"), () => StartHost(h), "primary", Icons.Play), accepted);
        Add(Ui.Button(I18n.T("v11.cm.host.publish"), () => PublishHost(h), "", Icons.Upload, I18n.T("v11.cm.host.publish.tip")), state == "running");
        Add(Ui.Button(I18n.T("v11.cm.host.settings"), () => HostDialog(h), "ghost", Icons.Sliders));
        if (h.Fabric) Add(Ui.Button(I18n.T("v11.cm.host.mods"), () => CopyModsDialog(h), "ghost", Icons.Package));
        Add(Ui.Button(I18n.T("v11.cm.host.folder"), () => Actions.OpenFolder(h.Dir), "ghost", Icons.Folder));
        Add(Ui.Button("", () => ConfirmDeleteHost(h), "icon ghost", Icons.Trash, I18n.T("v11.cm.host.delete")), run is not { Active: true });

        var where = Ui.Col(6);
        if (lan is null) where.Children.Add(Notice(I18n.T("v11.cm.host.noLan"), Icons.WifiOff));
        else
        {
            var address = $"{lan}:{h.Port}";
            var copy = Ui.Button("", () => Copy(address, I18n.T("v11.cm.join.copied", ("address", address))), "icon ghost", Icons.Copy, I18n.T("v11.cm.s.copy"));
            copy.Width = copy.Height = 30;
            var label = Ui.Text(I18n.T("v11.cm.host.lan"), "small muted");
            label.VerticalAlignment = VerticalAlignment.Center;
            where.Children.Add(Ui.Row(8, label,
                new Border
                {
                    Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center,
                    Child = new SelectableTextBlock { Text = address, FontFamily = Mono, FontSize = 13, FontWeight = FontWeight.SemiBold },
                }, copy));
        }
        where.Children.Add(Ui.Text(I18n.T("v11.cm.host.internet", ("port", h.Port)), "small faint", wrap: true));
        if (h.Board != "" && Servers.Cached.FirstOrDefault(s => s.Id == h.Board) is { } posted)
            where.Children.Add(Ui.Row(8, Ui.Icon(Icons.Globe, 14, Ui.Res("Brand2")),
                Ui.Text(I18n.T(Servers.IsLive(posted.Id) ? "v11.cm.host.posted.live" : "v11.cm.host.posted", ("name", posted.Name)), "small muted")));

        return Ui.Card(Ui.Col(14, head, eulaRow, actions, where), 20);
    }

    Control ConsoleCard(McHosted h, McHostRun? run)
    {
        _console = new SelectableTextBlock
        {
            FontFamily = Mono, FontSize = 12, LineHeight = 18, TextWrapping = TextWrapping.Wrap,
            Text = run is null ? I18n.T(h.Installed ? "v11.cm.host.console.idle" : "v11.cm.host.console.first") : ConsoleText(run),
            Foreground = run is null ? Ui.Res("Faint") : Ui.Res("Text"),
        };
        var scroll = new ScrollViewer { Height = 300, Content = _console, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _consoleScroll = scroll;
        var running = run is { State: "running" };
        var cmd = new TextBox { Text = _command, Watermark = I18n.T("v11.cm.host.cmd.hint"), FontFamily = Mono, IsEnabled = running, MaxLength = 256 };
        cmd.TextChanged += (_, _) => _command = cmd.Text ?? "";
        void Send()
        {
            if (McHost.Running(h.Id) is not { State: "running" } r || (cmd.Text ?? "").Trim() == "") return;
            r.Send(cmd.Text!);
            cmd.Text = "";
        }
        cmd.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Send(); };
        var send = Ui.Button(I18n.T("v11.cm.host.send"), Send, "", Icons.Forward);
        send.IsEnabled = running;
        send.Margin = new Thickness(8, 0, 0, 0);
        var line = new DockPanel();
        DockPanel.SetDock(send, Dock.Right);
        line.Children.Add(send);
        line.Children.Add(cmd);
        if (run is not null)
        {
            AttachConsole(run);
            Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Loaded);
        }
        else DetachConsole();
        return Ui.Card(Ui.Col(10,
            Ui.Row(8, Ui.Icon(Icons.Code, 15, Ui.Res("Muted")), Ui.Text(I18n.T("v11.cm.host.console"), "h3")),
            new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = scroll },
            line), 18);
    }

    static async void StartHost(McHosted h)
    {
        var w = MainWindow.Current!;
        if (!McHost.EulaAccepted(h)) { w.Toast(I18n.T("v11.cm.host.err.eula"), bad: true); return; }
        if (McHost.NeedsDownload(h))
        {
            // Файлы сервера и Java качаются задачей — видно в «Загрузках», после неё сервер стартует сам.
            w.Toast(I18n.T("v11.cm.host.preparing", ("name", h.Name)));
            Jobs.Run(h.Name, "Minecraft", async (_, progress, ct) => { await McHost.Start(h, progress, ct); });
            return;
        }
        try { await McHost.Start(h); }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or HttpRequestException)
        {
            w.Toast(Jobs.Explain(e), bad: true);
        }
    }

    /// <summary>Выложить свой сервер на доску: адрес в сети этого ПК или тот, что человек впишет сам.</summary>
    static void PublishHost(McHosted h)
    {
        var existing = Servers.Cached.FirstOrDefault(s => s.Id == h.Board && s.Owner == Servers.Me);
        var players = McHost.Running(h.Id)?.Players ?? 0;
        var preset = new ServerDraft(existing?.Id, existing?.Name ?? h.Name, Mc.Id, existing?.Address ?? McHost.LanAddress(h) ?? "", "", McHost.ModCount(h),
            existing?.Visibility ?? "friends", Math.Min(players, h.MaxPlayers), h.MaxPlayers, h.Label);
        ServerDialog(existing, preset, s => McHost.SetBoard(h, s.Id));
    }

    /// <summary>Новый свой сервер или настройки готового (версию и ядро у готового не меняем — другой мир).</summary>
    void HostDialog(McHosted? edit)
    {
        var w = MainWindow.Current!;
        var loader = edit?.Loader ?? "vanilla";
        List<McVersion> versions = [];
        var name = new TextBox { Text = edit?.Name ?? "", MaxLength = 40, Watermark = I18n.T("v11.cm.host.d.name.hint") };
        var version = new ComboBox { Width = 220, MaxDropDownHeight = 380, PlaceholderText = I18n.T("common.loading") };
        var port = new NumericUpDown { Value = edit?.Port ?? McHost.FreePort(), Minimum = 1024, Maximum = 65535, Increment = 1, FormatString = "0", Width = 150 };
        var max = new NumericUpDown { Value = edit?.MaxPlayers ?? 10, Minimum = 1, Maximum = McHost.MaxPlayers, Increment = 1, FormatString = "0", Width = 150 };
        var difficulty = new ComboBox { Width = 220 };
        foreach (var x in McHost.Difficulties) difficulty.Items.Add(I18n.T("v11.cm.host.diff." + x));
        difficulty.SelectedIndex = Math.Max(0, Array.IndexOf(McHost.Difficulties, edit?.Difficulty ?? "normal"));
        var motd = new TextBox { Text = edit?.Motd ?? "", MaxLength = McHost.MaxMotd, Watermark = I18n.T("v11.cm.host.d.motd.hint") };
        var white = new CheckBox { Content = I18n.T("v11.cm.host.d.white"), IsChecked = edit?.WhiteList == true };
        var maxGb = Math.Max(2, Math.Min(16, Mc.TotalMemoryMb() / 1024 - 2));
        var memory = new Slider
        {
            Minimum = 1, Maximum = maxGb, Value = Math.Clamp((edit?.MemoryMb ?? 2048) / 1024.0, 1, maxGb), TickFrequency = 0.5, IsSnapToTickEnabled = true,
            Width = 280, VerticalAlignment = VerticalAlignment.Center,
        };
        var memText = Ui.Text("", "small");
        memText.VerticalAlignment = VerticalAlignment.Center;
        void ShowMemory() => memText.Text = I18n.T("v11.cm.host.d.gb", ("gb", memory.Value.ToString("0.#", I18n.Culture)));
        memory.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) ShowMemory(); };
        ShowMemory();
        var note = Ui.Text("", "small", color: Ui.Res("Muted"), wrap: true);

        var loaders = Ui.Row(8);
        void RenderLoaders()
        {
            loaders.Children.Clear();
            foreach (var x in new[] { "vanilla", "fabric" })
            {
                var id = x;
                var chip = new Button
                {
                    Classes = { "chip" },
                    Content = Ui.Row(8, MinecraftPage.LoaderBadge(x, 18), new TextBlock { Text = I18n.T("v11.cm.host.d.loader." + x), VerticalAlignment = VerticalAlignment.Center }),
                };
                if (x == loader) chip.Classes.Add("active");
                chip.Click += (_, _) => { loader = id; RenderLoaders(); };
                loaders.Children.Add(chip);
            }
        }
        RenderLoaders();

        var go = Ui.Button(I18n.T(edit is null ? "v11.cm.host.d.create" : "common.save"), () => { }, "primary", edit is null ? Icons.Plus : Icons.Save);
        go.IsEnabled = edit is not null;
        async void LoadVersions()
        {
            try
            {
                versions = await McMeta.GameVersions();
                version.ItemsSource = versions.Select(v => v.Id).ToList();
                version.SelectedIndex = versions.Count > 0 ? 0 : -1;
                go.IsEnabled = versions.Count > 0;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException)
            {
                note.Text = I18n.T("v11.cm.host.d.noVersions", ("reason", Jobs.Explain(e)));
                note.Foreground = Ui.Res("Bad");
            }
        }

        go.Click += (_, _) =>
        {
            var mb = (int)(memory.Value * 1024);
            var diff = McHost.Difficulties[Math.Max(0, difficulty.SelectedIndex)];
            try
            {
                if (edit is null)
                {
                    if (version.SelectedIndex < 0 || version.SelectedIndex >= versions.Count) return;
                    var h = McHost.Create(name.Text ?? "", versions[version.SelectedIndex].Id, loader, (int)(port.Value ?? McHost.DefaultPort), (int)(max.Value ?? 10),
                        motd.Text ?? "", diff, white.IsChecked == true, mb);
                    _hostId = h.Id;
                    w.CloseDialog();
                    w.Toast(I18n.T("v11.cm.host.created", ("name", h.Name)));
                }
                else
                {
                    edit.Name = (name.Text ?? "").Trim() is { Length: > 0 } n ? n : edit.Name;
                    edit.Port = (int)(port.Value ?? edit.Port);
                    edit.MaxPlayers = (int)(max.Value ?? edit.MaxPlayers);
                    edit.Motd = (motd.Text ?? "").Trim();
                    edit.Difficulty = diff;
                    edit.WhiteList = white.IsChecked == true;
                    edit.MemoryMb = mb;
                    McHost.Save(edit);
                    w.CloseDialog();
                    w.Toast(I18n.T(McHost.Running(edit.Id) is { Active: true } ? "v11.cm.host.savedRestart" : "v11.cm.host.saved"));
                }
                Build();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                note.Text = Jobs.Explain(e);
                note.Foreground = Ui.Res("Bad");
            }
        };

        var body = Ui.Col(14, Field(I18n.T("v11.cm.host.d.name"), name));
        if (edit is null)
        {
            body.Children.Add(Field(I18n.T("v11.cm.host.d.version"), version));
            body.Children.Add(Field(I18n.T("v11.cm.host.d.loader"), loaders, I18n.T("v11.cm.host.d.loader.hint")));
            LoadVersions();
        }
        else body.Children.Add(Ui.Text(edit.Label + " · " + I18n.T("v11.cm.host.d.fixed"), "small muted", wrap: true));
        body.Children.Add(Ui.Row(16, Field(I18n.T("v11.cm.host.d.port"), port), Field(I18n.T("v11.cm.host.d.max"), max), Field(I18n.T("v11.cm.host.d.diff"), difficulty)));
        body.Children.Add(Field(I18n.T("v11.cm.host.d.motd"), motd));
        body.Children.Add(white);
        body.Children.Add(Field(I18n.T("v11.cm.host.d.memory"), Ui.Row(12, memory, memText)));
        body.Children.Add(note);
        w.Dialog(I18n.T(edit is null ? "v11.cm.host.d.title" : "v11.cm.host.d.title.edit"), new ScrollViewer { Content = body, MaxHeight = 560 }, 640,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
    }

    /// <summary>Моды из своей сборки Fabric той же версии — с предупреждением про моды только для клиента.</summary>
    static void CopyModsDialog(McHosted h)
    {
        var w = MainWindow.Current!;
        var sources = McHost.ModSources(h);
        if (sources.Count == 0)
        {
            w.Dialog(I18n.T("v11.cm.host.mods.title"), Ui.Text(I18n.T("v11.cm.host.mods.none", ("version", h.GameVersion)), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), w.CloseDialog, "primary"));
            return;
        }
        var pick = new ComboBox { MinWidth = 360 };
        foreach (var i in sources) pick.Items.Add(new ComboBoxItem { Content = $"{i.Name} · {ModsText(McContent.Count(i, "mod"))}", Tag = i.Id });
        pick.SelectedIndex = Math.Max(0, sources.FindIndex(i => i.Id == Mc.Active?.Id));
        var go = Ui.Button(I18n.T("v11.cm.host.mods.go"), () =>
        {
            if (pick.SelectedItem is not ComboBoxItem { Tag: string id } || sources.FirstOrDefault(i => i.Id == id) is not { } from) return;
            w.CloseDialog();
            try { w.Toast(I18n.T("v11.cm.host.mods.done", ("mods", ModsText(McHost.CopyMods(h, from))))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { w.Toast(Jobs.Explain(e), bad: true); }
        }, "primary", Icons.Package);
        w.Dialog(I18n.T("v11.cm.host.mods.title"),
            Ui.Col(12, Ui.Text(I18n.T("v11.cm.host.mods.text"), "muted", wrap: true), pick, Notice(I18n.T("v11.cm.host.mods.warn"), Icons.Alert, Ui.Res("Warn"))),
            560, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
    }

    void ConfirmDeleteHost(McHosted h)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("v11.cm.host.delete.title", ("name", h.Name)), Ui.Text(I18n.T("v11.cm.host.delete.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v11.cm.host.delete"), () =>
            {
                w.CloseDialog();
                try
                {
                    McHost.Delete(h);
                    _hostId = null;
                    w.Toast(I18n.T("v11.cm.host.deleted", ("name", h.Name)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { w.Toast(Jobs.Explain(e), bad: true); }
                Build();
            }, "primary", Icons.Trash));
    }
}
