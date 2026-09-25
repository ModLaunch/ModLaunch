using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// «Центр модов» — всё личное, что на Nexus разбросано по меню профиля:
/// обновления, отслеживаемые, избранное, недавно просмотренные, история
/// загрузок и скрытые моды/авторы. Одна страница, вкладки сверху.
/// </summary>
public sealed class ModsCenterPage : Page
{
    string _tab;
    string _query = "";

    public ModsCenterPage(string tab = "updates") => _tab = tab;

    public override string Title => I18n.T("mc.title");
    public override string SearchHint => I18n.T("mc.search");
    public override void Search(string text) { _query = text.Trim(); Build(); }

    bool Match(params string?[] fields) => _query == "" || fields.Any(f => f?.Contains(_query, StringComparison.OrdinalIgnoreCase) == true);

    public override void Build()
    {
        var content = new StackPanel { Spacing = 18, Margin = new Thickness(32, 26, 32, 32), MaxWidth = 1400 };
        content.Children.Add(Ui.Col(4, Ui.Text(I18n.T("mc.title"), "h1"), Ui.Text(I18n.T("mc.text"), "muted", wrap: true)));

        var installedUpdates = ModUpdates.Found.Sum(kv => kv.Value.Count);
        Button Tab(string id, string text, string icon, int count)
        {
            var c = Ui.Row(7, Ui.Icon(icon, 15), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (count > 0) c.Children.Add(new Border { Background = id == "updates" ? Ui.Res("BrandSoft") : Ui.Res("Surface3"), CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 1), Child = Ui.Text(count.ToString(), "small") });
            var b = new Button { Classes = { "tab" }, Content = c };
            if (_tab == id) b.Classes.Add("active");
            b.Click += (_, _) => { _tab = id; Build(); };
            return b;
        }
        var bar = new WrapPanel
        {
            Children =
            {
                Tab("updates", I18n.T("mc.updates"), Icons.Refresh, installedUpdates + Tracking.Updates.Count),
                Tab("tracked", I18n.T("mc.tracked"), Icons.Bell, Tracking.All().Count),
                Tab("favorites", I18n.T("mc.favorites"), Icons.Heart, Favorites.All().Count),
                Tab("recent", I18n.T("mc.recent"), Icons.Eye, Recent.All().Count),
                Tab("history", I18n.T("mc.history"), Icons.Download, History.All().Count),
                Tab("hidden", I18n.T("mc.hidden"), Icons.EyeOff, Blocklist.All().Count),
            },
        };
        content.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(6), CornerRadius = new CornerRadius(16), Child = bar, HorizontalAlignment = HorizontalAlignment.Left });
        content.Children.Add(_tab switch
        {
            "tracked" => Tracked(),
            "favorites" => FavoritesList(),
            "recent" => RecentList(),
            "history" => HistoryList(),
            "hidden" => Hidden(),
            _ => Updates(),
        });
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // ---------------------------------------------------------------- общие строки

    static Control Row(string? icon, string title, string subtitle, params Control[] actions)
    {
        var right = Ui.Row(8, actions);
        right.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(right, Dock.Right);
        var left = Ui.Row(12, Ui.Thumb(icon, title, 44, 10), Ui.Col(2, Ui.Text(title, "h3"), Ui.Text(subtitle, "small muted")));
        left.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel { Children = { right, left } };
        return new Border { Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10), Child = dock };
    }

    static Control ModRowFor(string game, ModInfo mod, string subtitle, params Control[] extra)
    {
        var def = Games.GameCatalog.ById(game);
        var open = Ui.Button(I18n.T("common.open"), () => MainWindow.Current?.Navigate(() => new ModPage(game, mod)), "ghost");
        var row = Row(mod.Icon, mod.Name, (def?.ShortName ?? game) + (subtitle == "" ? "" : " · " + subtitle), [.. extra, open]);
        return row;
    }

    static Control List(IEnumerable<Control> rows, string empty)
    {
        var col = Ui.Col(8);
        foreach (var r in rows) col.Children.Add(r);
        if (col.Children.Count == 0) col.Children.Add(Ui.Card(Ui.Text(empty, "muted", wrap: true), 22));
        return col;
    }

    // ---------------------------------------------------------------- вкладки

    Control Updates()
    {
        var rows = new List<Control>();
        foreach (var (gameId, list) in ModUpdates.Found)
        {
            var g = AppState.Game(gameId);
            foreach (var u in list.Where(u => Match(u.Name, g.Def.Name)))
                rows.Add(Row(u.Icon, u.Name, $"{g.Def.ShortName} · {u.Current} → {u.Latest}",
                    Ui.Button(I18n.T("mc.toGame"), () => MainWindow.Current?.Navigate(() => new GamePage(gameId, "installed")), "primary", Icons.Refresh)));
        }
        foreach (var u in Tracking.Updates.Where(u => Match(u.Item.Name)))
        {
            var uu = u;
            rows.Add(ModRowFor(u.Item.Game, u.Now, $"{u.Item.Version} → {u.Now.Version} · {I18n.T("mc.trackedTag")}",
                Ui.Button(I18n.T("mc.seen"), () => { Tracking.Seen(uu); Build(); }, "ghost", Icons.Check)));
        }
        var check = Ui.Button(I18n.T("mc.check"), async () =>
        {
            MainWindow.Current?.Toast(I18n.T("mc.checking"));
            try { await Tracking.Check(); } catch { }
            foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found)) { try { await ModUpdates.Check(g); } catch { } }
            Build();
        }, "", Icons.Refresh);
        check.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.Col(12, check, List(rows, I18n.T("mc.updates.none")));
    }

    Control Tracked() => List(Tracking.All().Where(t => Match(t.Name)).OrderByDescending(t => t.Since).Select(t =>
    {
        var mod = new ModInfo { Source = t.Source, Id = t.Id, Name = t.Name, Icon = t.Icon, Version = t.Version };
        return ModRowFor(t.Game, mod, t.Version == "" ? Ui.Ago(t.Since) : $"v{t.Version} · {I18n.T("mc.since", ("when", Ui.Ago(t.Since)))}",
            Ui.Button("", () => { Tracking.Toggle(t.Game, mod); Build(); }, "icon", Icons.Close, I18n.T("nx.untrack")));
    }), I18n.T("mc.tracked.none"));

    Control FavoritesList() => List(Favorites.All().Where(f => Match(f.Mod.Name, f.Mod.Author)).Select(f =>
        ModRowFor(f.GameId, f.Mod, f.Mod.Author)), I18n.T("mc.favorites.none"));

    Control RecentList()
    {
        var items = Recent.All().Where(r => Match(r.Mod.Name, r.Mod.Author)).ToList();
        var clear = Ui.Button(I18n.T("mc.clear"), () => { Recent.Clear(); Build(); }, "ghost", Icons.Trash);
        clear.IsVisible = items.Count > 0;
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.Col(12, clear, List(items.Select(r => ModRowFor(r.Game, r.Mod, Ui.Ago(r.At))), I18n.T("mc.recent.none")));
    }

    Control HistoryList()
    {
        var items = History.All().Where(h => Match(h.Title, h.Game)).Take(150).ToList();
        var clear = Ui.Button(I18n.T("mc.clear"), () => { History.Clear(); Build(); }, "ghost", Icons.Trash);
        clear.IsVisible = items.Count > 0;
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        var rows = items.Select(h =>
        {
            var state = Ui.Text(h.Ok ? I18n.T("mc.ok") : I18n.T("mc.failed"), "small", color: h.Ok ? Ui.Res("Good") : Ui.Res("Bad"));
            if (h.Error is not null) ToolTip.SetTip(state, h.Error);
            return Row(null, h.Title, $"{h.Game} · {h.At.ToLocalTime():dd.MM.yyyy HH:mm}", state);
        });
        return Ui.Col(12, clear, List(rows, I18n.T("mc.history.none")));
    }

    Control Hidden() => List(Blocklist.All().Where(b => Match(b.Title)).Select(b =>
        Row(null, b.Title, b.Author ? I18n.T("mc.hidden.author") : I18n.T("mc.hidden.mod"),
            Ui.Button(I18n.T("mc.unhide"), () => { Blocklist.Unhide(b.Key, b.Author); Build(); }, "ghost", Icons.Eye))), I18n.T("mc.hidden.none"));
}
