using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// Панель управления: всё главное на одном экране — сводка, быстрые
/// действия (их можно выбирать и переставлять), игры с кнопкой «Играть»,
/// переключатели часто нужных настроек и место на диске.
/// </summary>
public sealed class ControlPanelPage : Page
{
    public override string Title => I18n.T("cp.title");

    bool _editing;
    static List<Storage.Part>? _parts;
    static DateTime _measuredAt;
    static bool _measuring;

    void MeasureLater()
    {
        if (_measuring) return;
        _measuring = true;
        _measuredAt = DateTime.UtcNow;
        Task.Run(Storage.Measure).ContinueWith(t =>
        {
            _measuring = false;
            if (t.IsCompletedSuccessfully) _parts = t.Result;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (IsAttachedToVisualTree()) Build(); });
        });
    }

    bool IsAttachedToVisualTree() => Avalonia.VisualTree.VisualExtensions.GetVisualRoot(this) is not null;
    int _updates = -1;
    bool _checking;

    sealed record Tile(string Id, string Icon, string Key, Action Run);

    static MainWindow W => MainWindow.Current!;

    static List<Tile> AllTiles() =>
    [
        new("play", Icons.Play, "cp.t.play", PlayLast),
        new("updates", Icons.ArrowUp, "cp.t.updates", () => W.Navigate(() => new ModsCenterPage())),
        new("library", Icons.Layers, "lib.title", () => W.Navigate(() => new LibraryPage())),
        new("creator", Icons.Creator, "cp.t.creator", () => W.Navigate(() => new CreatorPage())),
        new("market", Icons.Bag, "mk.tab", () => W.Navigate(() => new MarketPage())),
        new("studio", Icons.Chart, "st.tab", () => W.Navigate(() => new MarketPage("studio"))),
        new("modrinth", Icons.Cube, "cp.t.minecraft", () => W.Navigate(() => new MinecraftPage())),
        new("newmod", Icons.Plus, "cp.t.newmod", () => W.Navigate(() => new CreatorPage("mine"))),
        new("account", Icons.User, "acc.page", () => W.Navigate(() => new AccountPage())),
        new("wallet", Icons.Bag, "acc.s.wallet", () => W.Navigate(() => new AccountPage("wallet"))),
        new("friends", Icons.Users, "friends.title", () => W.Navigate(() => new FriendsPage())),
        new("look", Icons.Palette, "look.title", () => W.Navigate(() => new SettingsPage("look"))),
        new("settings", Icons.Settings, "nav.settings", () => W.Navigate(() => new SettingsPage())),
        new("addgame", Icons.Plus, "add.title", () => W.Navigate(() => new AddGamePage())),
        new("data", Icons.Folder, "cp.t.data", () => Actions.OpenFolder(Paths.DataDir)),
        new("clean", Icons.Trash, "cp.t.clean", CleanCache),
        new("rescan", Icons.Refresh, "cp.t.rescan", () => { _ = AppState.DetectAll(force: true); W.Toast(I18n.T("cp.rescanning")); }),
        new("downloads", Icons.Download, "cp.t.downloads", () => W.Navigate(() => new SettingsPage("downloads"))),
    ];

    static readonly string[] DefaultTiles = ["play", "updates", "creator", "market", "modrinth", "account", "look", "clean"];

    static JsonObject Store => Settings.Data.Obj("controlPanel");

    static List<string> Pinned
    {
        get => Store["tiles"] is JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x != "").ToList() : [.. DefaultTiles];
        set { Store["tiles"] = new JsonArray(value.Select(x => (JsonNode)x).ToArray()); Settings.Save(); }
    }

    static GameState? LastGame() => AppState.Games
        .Where(g => g.Status == Detect.Found)
        .OrderByDescending(g => PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).FirstOrDefault();

    static void PlayLast()
    {
        if (LastGame() is { } g && g.LoaderInstalled) Actions.Play(g);
        else if (LastGame() is { } g2) W.Navigate(() => new GamePage(g2.Def.Id));
        else W.Toast(I18n.T("cp.noGames"), bad: true);
    }

    static void CleanCache()
    {
        long freed = 0;
        foreach (var p in Storage.Measure().Where(p => p.Clearable)) freed += Storage.Clear(p.Dir);
        W.Toast(I18n.T("storage.cleared", ("size", GamePage.Size(freed))));
    }

    async void CheckUpdates()
    {
        _checking = true;
        Build();
        var total = 0;
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && g.Registry is not null))
        {
            try { total += (await ModUpdates.Check(g)).Count; } catch { }
        }
        if (Minecraft.Mc.Active is { } mc) total += Minecraft.McContent.UpdateCount(mc);
        _updates = total;
        _checking = false;
        Build();
    }

    public override void Build()
    {
        var col = new StackPanel { Spacing = 22, Margin = new Thickness(40, 26, 40, 40), MaxWidth = 1640 };

        // Приветствие и сводка.
        var hour = DateTime.Now.Hour;
        var hello = I18n.T(hour < 6 ? "cp.hi.night" : hour < 12 ? "cp.hi.morning" : hour < 18 ? "cp.hi.day" : "cp.hi.evening",
            ("name", Social.Account.Get().Name ?? "")).TrimEnd(' ', ',');
        var found = AppState.Games.Where(g => g.Status == Detect.Found).ToList();
        var mods = found.Sum(g => g.Registry?.List().Count ?? 0);
        var played = AppState.Games.Sum(g => PlayTime.Get(g.Def.Id).TotalMs);
        var head = new DockPanel();
        var edit = Ui.Button(_editing ? I18n.T("cp.done") : I18n.T("cp.customize"), () => { _editing = !_editing; Build(); }, _editing ? "primary" : "ghost", _editing ? Icons.Check : Icons.Edit);
        edit.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(edit, Dock.Right);
        head.Children.Add(edit);
        head.Children.Add(Ui.Col(4, Ui.Text(hello, "h1"), Ui.Text(DateTime.Now.ToString("D", I18n.Culture), "muted")));
        col.Children.Add(head);

        var stats = new UniformGrid { Columns = 5 };
        stats.Children.Add(Stat(Icons.Gamepad, I18n.T("cp.s.games"), found.Count.ToString(I18n.Culture), () => W.Navigate(() => new LibraryPage())));
        stats.Children.Add(Stat(Icons.Package, I18n.T("cp.s.mods"), mods.ToString(I18n.Culture), () => W.Navigate(() => new ModsCenterPage())));
        stats.Children.Add(Stat(Icons.ArrowUp, I18n.T("cp.s.updates"), _checking ? "…" : _updates < 0 ? "?" : _updates.ToString(I18n.Culture), CheckUpdates,
            _updates > 0 ? Ui.Res("Warn") : null));
        stats.Children.Add(Stat(Icons.Clock, I18n.T("cp.s.played"), PlayTime.Format(played), null));
        stats.Children.Add(Stat(Icons.Cube, I18n.T("cp.s.minecraft"), Minecraft.Mc.Instances().Count.ToString(I18n.Culture), () => W.Navigate(() => new MinecraftPage("builds"))));
        col.Children.Add(stats);

        // Быстрые действия.
        col.Children.Add(Ui.Text(I18n.T("cp.actions"), "h2"));
        var all = AllTiles();
        var pinned = Pinned;
        var tiles = new WrapPanel();
        foreach (var t in _editing ? all : pinned.Select(id => all.FirstOrDefault(x => x.Id == id)).OfType<Tile>())
            tiles.Children.Add(TileView(t, pinned.Contains(t.Id)));
        col.Children.Add(tiles);
        if (_editing) col.Children.Add(Ui.Text(I18n.T("cp.customize.hint"), "small muted", wrap: true));

        // Игры.
        if (found.Count > 0)
        {
            col.Children.Add(Ui.Text(I18n.T("cp.games"), "h2"));
            var games = Ui.Col(8);
            foreach (var g in found.OrderByDescending(g => PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).Take(8))
            {
                var game = g;
                var p = PlayTime.Get(g.Def.Id);
                var line = new DockPanel();
                var buttons = Ui.Row(6,
                    Ui.Button("", () => W.Navigate(() => new GamePage(game.Def.Id, "installed")), "icon ghost", Icons.Package, I18n.T("cp.mods")),
                    Ui.Button("", () => Actions.OpenFolder(game.Path!), "icon ghost", Icons.Folder, I18n.T("games.openFolder")),
                    Ui.Button(p.Running ? I18n.T("cp.running") : I18n.T("games.play"), () => Actions.Play(game), "primary", Icons.Play));
                buttons.Children[^1].IsEnabled = g.LoaderInstalled && !p.Running;
                DockPanel.SetDock(buttons, Dock.Right);
                line.Children.Add(buttons);
                var info = Ui.Col(2, Ui.Text(g.Def.Name, "h3"),
                    Ui.Text(I18n.T("cp.gameLine", ("mods", g.Registry?.List().Count ?? 0), ("time", PlayTime.Format(p.TotalMs)), ("last", p.LastPlayed is null ? "—" : Ui.Ago(p.LastPlayed))), "small muted"));
                info.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(Ui.Row(12, new Border { Width = 92, Height = 43, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.GameImage(g.Def, 200) }, info));
                var card = Ui.Card(line, 10);
                card.ContextFlyout = GameCard.Menu(g);
                games.Children.Add(card);
            }
            col.Children.Add(games);
        }

        // Переключатели и место на диске — в две колонки.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 18 };
        var switches = Ui.Col(10, Ui.Text(I18n.T("cp.switches"), "h2"),
            Switch(I18n.T("look.anim"), Look.Animations, v => { Settings.Data["animations"] = v; W.Refresh(); }),
            Switch(I18n.T("look.smooth"), SmoothScroll.On, v => Settings.Data["smoothScroll"] = v),
            Switch(I18n.T("look.compact"), Settings.Data.Bool("compactLists"), v => Settings.Data["compactLists"] = v),
            Switch(I18n.T("cp.sw.checkUpdates"), ModUpdates.OnStart, v => Settings.Data["checkModUpdates"] = v),
            Switch(I18n.T("cp.sw.autoUpdate"), ModUpdates.Auto, v => Settings.Data["autoUpdateMods"] = v),
            Switch(I18n.T("cp.sw.confirmRemove"), Settings.Data.Bool("confirmRemove", true), v => Settings.Data["confirmRemove"] = v),
            Switch(I18n.T("cp.sw.playtime"), PlayTime.Track, v => Settings.Data["trackPlaytime"] = v),
            Switch(I18n.T("cp.sw.autostart"), Autostart.Enabled, v => { try { Autostart.Set(v); } catch (Exception e) { W.Toast(e.Message, bad: true); } }));
        // Размер папок считается в фоне и кэшируется на минуту: на больших архивах это секунды.
        if (_parts is null || DateTime.UtcNow - _measuredAt > TimeSpan.FromMinutes(1)) MeasureLater();
        var parts = _parts ?? [];
        var disk = Ui.Col(10, Ui.Text(I18n.T("cp.disk"), "h2"));
        var max = Math.Max(1, parts.Count == 0 ? 1 : parts.Max(p => p.Bytes));
        if (parts.Count == 0) disk.Children.Add(Ui.Text(I18n.T("common.loading"), "small muted"));
        foreach (var p in parts)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = max, Value = p.Bytes, Height = 6, MinHeight = 6 };
            var row = new DockPanel();
            var size = Ui.Text(GamePage.Size(p.Bytes), "small muted");
            DockPanel.SetDock(size, Dock.Right);
            row.Children.Add(size);
            row.Children.Add(Ui.Text(I18n.Has("storage." + p.Key) ? I18n.T("storage." + p.Key) : p.Key, "small"));
            disk.Children.Add(Ctx.Attach(Ui.Col(4, row, bar), () => Ctx.Menu(
                Ctx.Folder(I18n.T("games.openFolder"), Directory.CreateDirectory(p.Dir).FullName),
                p.Clearable ? Ctx.Item(I18n.T("storage.clear"), Icons.Trash, () => { W.Toast(I18n.T("storage.cleared", ("size", GamePage.Size(Storage.Clear(p.Dir))))); _parts = null; Build(); }) : null)));
        }
        disk.Children.Add(Ui.Row(8,
            Ui.Button(I18n.T("cp.t.clean"), () => { CleanCache(); _parts = null; Build(); }, "", Icons.Trash),
            Ui.Button(I18n.T("cp.t.data"), () => Actions.OpenFolder(Paths.DataDir), "ghost", Icons.Folder)));
        disk.Children.Add(Ui.Text($"ModLaunch {Http.Version} · .NET {Environment.Version.ToString(2)} · {Environment.OSVersion.VersionString}", "small muted"));
        var left = Ui.Card(switches, 22);
        var right = Ui.Card(disk, 22);
        right.VerticalAlignment = left.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        col.Children.Add(grid);

        col.Children.Add(Ui.Text(I18n.T("cp.keys"), "small muted", wrap: true));
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Stat(string icon, string label, string value, Action? click, IBrush? color = null)
    {
        var body = Ui.Col(6, Ui.Row(8, Ui.Icon(icon, 16, Ui.Res("Brand2")), Ui.Text(label, "small muted")), Ui.Text(value, "h2", color: color));
        var b = new Button { Classes = { "card-btn" }, Padding = new Thickness(18), Margin = new Thickness(0, 0, 12, 12), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = body };
        b.IsHitTestVisible = click is not null;
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    Control TileView(Tile t, bool pinned)
    {
        var circle = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(t.Icon, 20, Ui.Res("Brand2")) };
        var label = new TextBlock { Text = I18n.T(t.Key), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontWeight = FontWeight.SemiBold, FontSize = 13, MaxWidth = 120 };
        var content = Ui.Col(10, circle, label);
        circle.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Button
        {
            Classes = { "card-btn" }, Width = 148, Height = 118, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(10),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Content = content,
        };
        if (_editing)
        {
            b.Opacity = pinned ? 1 : 0.45;
            if (pinned) { b.BorderBrush = Ui.Res("Brand"); b.BorderThickness = new Thickness(2); }
            b.Click += (_, _) => { var list = Pinned; if (!list.Remove(t.Id)) list.Add(t.Id); Pinned = list; Build(); };
        }
        else b.Click += (_, _) => t.Run();
        return Ctx.Attach(b, () =>
        {
            var list = Pinned;
            var i = list.IndexOf(t.Id);
            return Ctx.Menu(
                Ctx.Item(I18n.T("ctx.open"), t.Icon, t.Run),
                "-",
                i > 0 ? Ctx.Item(I18n.T("cp.moveLeft"), Icons.Back, () => { (list[i - 1], list[i]) = (list[i], list[i - 1]); Pinned = list; Build(); }) : null,
                i >= 0 && i < list.Count - 1 ? Ctx.Item(I18n.T("cp.moveRight"), Icons.Forward, () => { (list[i + 1], list[i]) = (list[i], list[i + 1]); Pinned = list; Build(); }) : null,
                i >= 0 ? Ctx.Item(I18n.T("cp.unpin"), Icons.EyeOff, () => { list.Remove(t.Id); Pinned = list; Build(); }) : Ctx.Item(I18n.T("cp.pin"), Icons.Star, () => { list.Add(t.Id); Pinned = list; Build(); }),
                Ctx.Item(I18n.T("cp.reset"), Icons.Refresh, () => { Pinned = [.. DefaultTiles]; Build(); }));
        });
    }

    static Control Switch(string text, bool value, Action<bool> set)
    {
        var sw = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        sw.IsCheckedChanged += (_, _) => { set(sw.IsChecked == true); Settings.Save(); };
        var row = new DockPanel();
        DockPanel.SetDock(sw, Dock.Right);
        row.Children.Add(sw);
        var t = Ui.Text(text, "", wrap: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        return row;
    }
}
