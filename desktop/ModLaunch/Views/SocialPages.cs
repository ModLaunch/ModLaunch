using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Setup;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>Друзья: код, добавление, запросы, кто в сети и во что играет.</summary>
public sealed class FriendsPage : Page
{
    string? _code;
    bool _loading;

    public FriendsPage()
    {
        if (Account.SignedIn && !Program.Demo) _ = Load();
    }

    public override string Title => I18n.T("friends.title");

    async Task Load()
    {
        _loading = true;
        try
        {
            _code = await Friends.MyCode();
            await Friends.Refresh(force: true);
        }
        catch (Exception e) { MainWindow.Current?.Toast(Friends.Explain(e), bad: true); }
        _loading = false;
        Build();
    }

    public override void Build()
    {
        var col = new StackPanel { Spacing = 16, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 900 };
        col.Children.Add(Ui.Text(I18n.T("friends.title"), "h1"));
        var view = Friends.View();
        if (!view.Configured) col.Children.Add(Ui.Card(Ui.Text(I18n.T("err.friendsOff"), "muted"), 22));
        else if (!view.SignedIn)
        {
            col.Children.Add(Ui.Card(Ui.Col(12, Ui.Text(I18n.T("friends.signin.text"), "muted", wrap: true),
                Ui.Row(10, Ui.Button(I18n.T("acc.tab.signin"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "primary", Icons.Key))), 22));
        }
        else
        {
            var code = _code ?? view.Code;
            var copy = Ui.Button(I18n.T("friends.copy"), async () =>
            {
                if (code is null) return;
                var clip = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clip is not null) await clip.SetTextAsync(code);
                MainWindow.Current?.Toast(I18n.T("friends.copied"));
            }, "", Icons.Link);
            var box = new TextBox { Watermark = I18n.T("friends.add.placeholder"), Width = 260 };
            async void Add()
            {
                try
                {
                    var (status, name) = await Friends.Add(box.Text ?? "");
                    MainWindow.Current?.Toast(I18n.T("friends.added." + status, ("name", name)));
                    box.Text = "";
                }
                catch (Exception e) { MainWindow.Current?.Toast(Friends.Explain(e), bad: true); }
                Build();
            }
            box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Add(); };
            var mode = new ComboBox { Width = 220 };
            var modes = new[] { "all", "online", "hidden" };
            foreach (var m in modes) mode.Items.Add(I18n.T("friends.status." + m));
            mode.SelectedIndex = Array.IndexOf(modes, view.Mode);
            mode.SelectionChanged += (_, _) => { if (mode.SelectedIndex >= 0) Friends.Mode = modes[mode.SelectedIndex]; };

            col.Children.Add(Ui.Card(Ui.Col(14,
                Ui.Row(12, Ui.Text(I18n.T("friends.myCode"), "muted"), new TextBlock { Text = code ?? "…", FontSize = 22, FontWeight = FontWeight.Bold, FontFamily = new FontFamily("Consolas, monospace"), Foreground = Ui.Res("Brand2") }, copy),
                Ui.Row(10, box, Ui.Button(I18n.T("friends.add"), Add, "primary", Icons.Check)),
                Ui.Row(10, Ui.Text(I18n.T("friends.status"), "muted"), mode)), 22));

            if (view.Incoming.Count > 0)
            {
                var list = Ui.Col(8, Ui.Text(I18n.T("friends.incoming"), "h3"));
                foreach (var r in view.Incoming)
                {
                    var uid = r.Uid;
                    list.Children.Add(Row(r.Name, Ui.Ago(r.At), Ui.Button(I18n.T("friends.accept"), () => Do(() => Friends.Accept(uid)), "primary"), Ui.Button(I18n.T("friends.decline"), () => Do(() => Friends.Remove(uid)), "ghost")));
                }
                col.Children.Add(Ui.Card(list, 18));
            }
            if (view.Outgoing.Count > 0)
            {
                var list = Ui.Col(8, Ui.Text(I18n.T("friends.outgoing"), "h3"));
                foreach (var r in view.Outgoing)
                {
                    var uid = r.Uid;
                    list.Children.Add(Row(r.Name, I18n.T("friends.waiting"), Ui.Button(I18n.T("friends.cancel"), () => Do(() => Friends.Remove(uid)), "ghost")));
                }
                col.Children.Add(Ui.Card(list, 18));
            }

            var online = view.Friends.Count(f => f.State != "offline");
            var friendsList = Ui.Col(8, Ui.Row(10, Ui.Text(I18n.T("friends.list"), "h3"), Ui.Text(I18n.T("friends.onlineN", ("n", online), ("total", view.Friends.Count)), "small muted")));
            if (view.Friends.Count == 0) friendsList.Children.Add(Ui.Text(_loading ? I18n.T("common.loading") : I18n.T("friends.empty"), "muted", wrap: true));
            foreach (var f in view.Friends)
            {
                var uid = f.Uid;
                var name = f.Name;
                var status = f.State switch
                {
                    "playing" => I18n.T("friends.playing", ("game", f.GameName)),
                    "online" => I18n.T("friends.online"),
                    _ => f.Seen is DateTime seen ? I18n.T("friends.offline") + " · " + Ui.Ago(seen) : I18n.T("friends.offline"),
                };
                var actions = new List<Control>();
                if (f.State == "playing" && AppState.Games.FirstOrDefault(g => g.Def.Id == f.Game) is { } game)
                    actions.Add(Ui.Button(I18n.T("friends.toGame"), () => MainWindow.Current?.Navigate(() => new GamePage(game.Def.Id)), "", Icons.Play));
                actions.Add(Ui.Button("", () => ConfirmRemove(uid, name), "icon ghost", Icons.Trash, I18n.T("friends.remove")));
                var dot = Ui.Dot(f.State switch { "playing" => Ui.Res("Brand2"), "online" => Ui.Res("Good"), _ => Ui.Res("Faint") }, 10);
                var row = Row(name, status, actions.ToArray());
                friendsList.Children.Add(Ui.Row(10, dot, row));
            }
            col.Children.Add(Ui.Card(friendsList, 18));
            col.Children.Add(Ui.Button(I18n.T("stats.refresh"), () => _ = Load(), "ghost", Icons.Refresh));
        }
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Row(string name, string hint, params Control[] actions)
    {
        var right = Ui.Row(6, actions);
        right.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinWidth = 600 };
        g.Children.Add(Ui.Col(2, Ui.Text(name, "h3"), Ui.Text(hint, "small muted")));
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    async void Do(Func<Task> action)
    {
        try { await action(); } catch (Exception e) { MainWindow.Current?.Toast(Friends.Explain(e), bad: true); }
        Build();
    }

    void ConfirmRemove(string uid, string name)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("friends.remove.title", ("name", name)), Ui.Text(I18n.T("friends.remove.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("friends.remove"), () => { w.CloseDialog(); Do(() => Friends.Remove(uid)); }, "primary", Icons.Trash));
    }
}

/// <summary>Статистика для владельца: скачивания по выпускам и отзывы.</summary>
public sealed class StatsPage : Page
{
    List<(string Version, DateTime? Published, long Setup, long Zip, long Total, string? Page)>? _releases;
    string? _error;

    public StatsPage() { _ = Load(); }

    public override string Title => I18n.T("stats.title");

    async Task Load()
    {
        try
        {
            _releases = Program.Demo ? [("3.1.0", DateTime.UtcNow.AddDays(-20), 18, 2, 20, null), ("3.2.0", DateTime.UtcNow.AddDays(-2), 7, 1, 8, null)] : await Updater.Releases();
            if (!Program.Demo) try { await Reviews.Sync(force: true); } catch { }
        }
        catch (Exception e) { _error = Jobs.Explain(e); }
        Build();
    }

    public override void Build()
    {
        var col = new StackPanel { Spacing = 16, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1000 };
        var head = new DockPanel();
        var refresh = Ui.Button(I18n.T("stats.refresh"), () => _ = Load(), "", Icons.Refresh);
        DockPanel.SetDock(refresh, Dock.Right);
        head.Children.Add(refresh);
        head.Children.Add(Ui.Text(I18n.T("stats.title"), "h1"));
        col.Children.Add(head);
        if (_error is not null) col.Children.Add(Ui.Card(Ui.Text(_error, "muted", wrap: true), 18));

        var reviews = Reviews.List();
        var total = _releases?.Sum(r => r.Total) ?? 0;
        var tiles = new UniformGrid { Columns = 3 };
        tiles.Children.Add(Tile(I18n.T("stats.downloads"), total.ToString("N0"), I18n.T("stats.releasesN", ("n", _releases?.Count ?? 0))));
        tiles.Children.Add(Tile(I18n.T("stats.reviews"), reviews.Count.ToString(), I18n.T("stats.authors", ("n", reviews.Select(r => r.Uid).Distinct().Count()))));
        tiles.Children.Add(Tile(I18n.T("stats.avg"), reviews.Count == 0 ? "—" : $"{reviews.Average(r => r.Stars):0.00} ★", I18n.T("stats.latest", ("version", _releases?.LastOrDefault().Version ?? Http.Version))));
        col.Children.Add(tiles);

        // Скачивания по версиям — простые столбики (одна величина, одна шкала).
        if (_releases is { Count: > 0 })
        {
            var max = Math.Max(1, _releases.Max(r => r.Total));
            var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Height = 180 };
            foreach (var r in _releases.TakeLast(16))
            {
                var h = Math.Max(4, 140.0 * r.Total / max);
                var bar = new Border { Width = 34, Height = h, CornerRadius = new CornerRadius(4, 4, 0, 0), Background = Ui.Res("Brand"), VerticalAlignment = VerticalAlignment.Bottom };
                ToolTip.SetTip(bar, I18n.T("stats.tip", ("version", r.Version), ("n", r.Total), ("setup", r.Setup), ("zip", r.Zip)));
                var column = new DockPanel { Width = 44 };
                var label = Ui.Text(r.Version, "small muted");
                label.HorizontalAlignment = HorizontalAlignment.Center;
                DockPanel.SetDock(label, Dock.Bottom);
                column.Children.Add(label);
                var value = Ui.Text(r.Total.ToString(), "small");
                value.HorizontalAlignment = HorizontalAlignment.Center;
                column.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 4, Children = { value, bar } });
                bars.Children.Add(column);
            }
            col.Children.Add(Ui.Card(Ui.Col(12, Ui.Text(I18n.T("stats.chart"), "h2"), new ScrollViewer { Content = bars, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }), 22));

            var table = Ui.Col(6, TableRow(I18n.T("stats.col.version"), I18n.T("stats.col.date"), I18n.T("stats.col.setup"), I18n.T("stats.col.total"), true));
            foreach (var r in Enumerable.Reverse(_releases))
                table.Children.Add(TableRow(r.Version, r.Published?.ToString("d", System.Globalization.CultureInfo.GetCultureInfo(I18n.Lang == "en" ? "en-US" : "ru-RU")) ?? "", r.Setup.ToString(), r.Total.ToString(), false));
            col.Children.Add(Ui.Card(Ui.Col(12, Ui.Text(I18n.T("stats.table"), "h2"), table), 22));
        }
        else if (_releases is not null) col.Children.Add(Ui.Card(Ui.Text(I18n.T("stats.noReleases"), "muted"), 18));

        if (reviews.Count == 0) col.Children.Add(Ui.Card(Ui.Text(I18n.T("stats.noReviews"), "muted"), 18));
        else
        {
            var top = reviews.GroupBy(r => $"{r.Game}|{r.Mod}").Select(g => (Name: g.First().ModName, Game: g.First().Game, Count: g.Count(), Avg: g.Average(r => r.Stars)))
                .OrderByDescending(x => x.Count).ThenByDescending(x => x.Avg).Take(8);
            var list = Ui.Col(6, Ui.Text(I18n.T("stats.top"), "h2"));
            foreach (var t in top) list.Children.Add(TableRow(t.Name, t.Game, $"{t.Avg:0.0} ★", t.Count.ToString(), false));
            col.Children.Add(Ui.Card(list, 22));
            var fresh = Ui.Col(8, Ui.Text(I18n.T("stats.fresh"), "h2"));
            foreach (var r in reviews.Take(8))
                fresh.Children.Add(Ui.Col(2, Ui.Text($"{new string('★', r.Stars)} {r.ModName} — {r.Name}", "h3"), Ui.Text(r.Text, "small muted", wrap: true)));
            col.Children.Add(Ui.Card(fresh, 22));
        }
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Tile(string title, string value, string hint) => new Border
    {
        Classes = { "card" },
        Padding = new Thickness(20),
        Margin = new Thickness(0, 0, 12, 0),
        Child = Ui.Col(6, Ui.Text(title, "small muted"), new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeight.Bold }, Ui.Text(hint, "small muted")),
    };

    static Control TableRow(string a, string b, string c, string d, bool head)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,*") };
        string cls = head ? "small muted" : "";
        var cells = new[] { a, b, c, d };
        for (var i = 0; i < 4; i++)
        {
            var t = Ui.Text(cells[i], cls);
            if (i > 0) t.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return g;
    }
}

/// <summary>«Деньги на чай».</summary>
public sealed class DonatePage : Page
{
    public override string Title => I18n.T("nav.donate");

    public override void Build()
    {
        var col = Ui.Col(14,
            new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(20), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Heart, 30, Ui.Res("Brand2")), HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = I18n.T("donate.title"), FontSize = 28, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = I18n.T("donate.text"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Ui.Res("Muted") },
            new TextBlock { Text = I18n.T("donate.soon"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Ui.Res("Faint"), FontSize = 12 });
        Content = new Border { Classes = { "card" }, Padding = new Thickness(40), Margin = new Thickness(34, 40), MaxWidth = 560, VerticalAlignment = VerticalAlignment.Top, Child = col };
    }
}

