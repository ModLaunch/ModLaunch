using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 12.0: все игры Steam. Сверху — все установленные (и без модов): играть через Steam, а у кого моды есть —
/// добавить в ModLaunch. Ниже — поиск по всему магазину Steam: поставить, открыть страницу, а если для игры
/// есть моды — сразу видно где (Thunderstore, Nexus).
/// </summary>
public sealed class SteamGamesPage : Page
{
    public override string Title => I18n.T("v12w.steam.title");
    public override string SearchHint => I18n.T("v12w.steam.search");

    string _sort = "recent";
    string _query = "";
    List<SteamLibrary.StoreHit>? _hits;
    bool _searching;
    CancellationTokenSource? _cts;
    readonly ScrollViewer _scroll = new();

    public SteamGamesPage(string query = "")
    {
        _query = query.Trim();
        if (_query != "") _ = Find(_query);
    }

    public override void Search(string text)
    {
        _query = text.Trim();
        _ = Find(_query);
    }

    async Task Find(string query)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _searching = query != "";
        _hits = null;
        Dispatcher.UIThread.Post(Build);
        if (query == "") return;
        var hits = await SteamLibrary.Search(query, cts.Token);
        if (cts.IsCancellationRequested) return;
        _hits = hits;
        _searching = false;
        Dispatcher.UIThread.Post(Build);
    }

    public override void Build()
    {
        var content = StoreKit.Fit(new StackPanel { Spacing = 16, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1100 }, 1700);
        var apps = SteamLibrary.Sorted(_sort).ToList();

        var sort = Ui.Row(6);
        foreach (var s in new[] { "recent", "name", "size" })
        {
            var one = s;
            sort.Children.Add(Ui.Button(I18n.T("v12w.opt.sort." + one), () => { _sort = one; Build(); }, one == _sort ? "primary" : "ghost"));
        }
        sort.VerticalAlignment = VerticalAlignment.Top;
        var head = new DockPanel();
        DockPanel.SetDock(sort, Dock.Right);
        head.Children.Add(sort);
        head.Children.Add(Ui.Col(6,
            Ui.Row(12, Ui.Text(I18n.T("v12w.steam.title"), "h2"), Ui.Text(apps.Count.ToString("N0", I18n.Culture), "h2", color: Ui.Res("Faint"))),
            Ui.Text(I18n.T("v12w.steam.text"), "muted small", wrap: true)));
        content.Children.Add(head);

        // Поиск по магазину — первым, когда он есть: человек ищет игру, а не листает свою библиотеку.
        if (_query != "")
        {
            content.Children.Add(StoreKit.Header(I18n.T("v12w.steam.store", ("q", _query)), count: _hits?.Count.ToString()));
            if (_searching) content.Children.Add(Ui.Card(Ui.Row(12, new ProgressBar { IsIndeterminate = true, Width = 120 }, Ui.Text(I18n.T("v12w.steam.searching"), "muted"))));
            else if (_hits is { Count: 0 }) content.Children.Add(Ui.Card(Ui.Text(I18n.T("v12w.steam.nothing"), "muted small", wrap: true), 14));
            foreach (var h in _hits ?? []) content.Children.Add(Ui.Card(StoreRow(h), 12));
        }

        if (apps.Count == 0)
            content.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("v12w.steam.none"), "h3"), Ui.Text(I18n.T("v12w.steam.none.text"), "muted small", wrap: true)), 18));
        else
        {
            content.Children.Add(StoreKit.Header(I18n.T("v12w.steam.installed"), count: apps.Count.ToString()));
            var grid = new WrapPanel();
            foreach (var a in apps) grid.Children.Add(Tile(a));
            content.Children.Add(grid);
        }
        _scroll.Content = content;
        if (Content != _scroll) Content = _scroll;
    }

    /// <summary>Плитка установленной игры: картинка Steam, имя, «Играть» и, если моды есть, «Моды».</summary>
    static Control Tile(SteamApp a)
    {
        var managed = SteamLibrary.Managed(a.AppId);
        var dir = managed is null ? GameDirectory.BySteam(a.AppId) ?? GameDirectory.ByName(a.Name) : null;
        var art = new Border { Height = 110, CornerRadius = new CornerRadius(10, 10, 0, 0), ClipToBounds = true, Child = HomePage.WebArt(GameDef.SteamArt(a.AppId), null, a.Name, 480) };
        var actions = Ui.Row(6, Ui.Button(I18n.T("games.play"), () => SteamLibrary.Play(a.AppId), "primary", Icons.Play));
        if (managed is not null) actions.Children.Add(Ui.Button(I18n.T("v12w.steam.open"), () => MainWindow.Current?.Navigate(() => new GamePage(managed.Def.Id)), "", Icons.Layers));
        else if (dir is not null) actions.Children.Add(Ui.Button(I18n.T("v12w.steam.mods"), () => MainWindow.Current?.Navigate(() => new AddGamePage(a.Name)), "", Icons.Plus));
        var tags = Ui.Row(6);
        if (managed is not null) tags.Children.Add(ModRow.Tag(I18n.T("v12g.badge.inLibrary"), Ui.Soft("#13301F", "#3FCB7E"), Ui.Ink("#3FCB7E")));
        else if (dir is not null) tags.Children.Add(ModRow.Tag(I18n.T("v12w.steam.hasMods"), Ui.Soft("#1E1A3A", "#A99BFF"), Ui.Ink("#A99BFF")));
        if (a.SizeOnDisk > 0) tags.Children.Add(Ui.Text(SizeText(a.SizeOnDisk), "small muted"));
        var body = Ui.Col(8, new TextBlock { Text = a.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis }, tags, actions);
        body.Margin = new Thickness(12, 10, 12, 12);
        return new Border
        {
            Classes = { "card" }, Width = 236, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0), ClipToBounds = true,
            Child = new DockPanel { Children = { Dock(art), body } },
        };

        static Control Dock(Control c) { DockPanel.SetDock(c, Avalonia.Controls.Dock.Top); return c; }
    }

    /// <summary>Игры весят гигабайты — их и показываем в гигабайтах.</summary>
    static string SizeText(long bytes) => bytes >= 1L << 30
        ? I18n.T("v12w.size.gb", ("n", (bytes / (double)(1L << 30)).ToString("0.#", I18n.Culture)))
        : GamePage.Size(bytes);

    /// <summary>Строка магазина: картинка, имя, цена, есть ли моды; поставить, играть или открыть страницу.</summary>
    static Control StoreRow(SteamLibrary.StoreHit h)
    {
        var installed = SteamLibrary.ById(h.AppId) is not null;
        var dir = GameDirectory.BySteam(h.AppId) ?? GameDirectory.ByName(h.Name);
        var actions = Ui.Row(6);
        actions.Children.Add(installed
            ? Ui.Button(I18n.T("games.play"), () => SteamLibrary.Play(h.AppId), "primary", Icons.Play)
            : Ui.Button(I18n.T("v12w.steam.install"), () => SteamLibrary.Install(h.AppId), "primary", Icons.Download));
        actions.Children.Add(Ui.Button("", () => SteamLibrary.StorePage(h.AppId), "icon ghost", Icons.External, I18n.T("v12w.steam.page")));
        actions.VerticalAlignment = VerticalAlignment.Center;
        var tags = Ui.Row(6);
        if (installed) tags.Children.Add(ModRow.Tag(I18n.T("v12w.steam.installedTag"), Ui.Soft("#13301F", "#3FCB7E"), Ui.Ink("#3FCB7E")));
        if (dir?.Community is not null) tags.Children.Add(ModRow.Tag("Thunderstore", Ui.Soft("#0E2A33", "#4FD1E8"), Ui.Ink("#4FD1E8")));
        if (dir?.NexusDomain is not null) tags.Children.Add(ModRow.Tag("Nexus Mods", Ui.Soft("#33240E", "#F0A23B"), Ui.Ink("#F0A23B")));
        if (h.Price is not null) tags.Children.Add(Ui.Text(h.Price, "small muted"));
        var row = new DockPanel();
        DockPanel.SetDock(actions, Avalonia.Controls.Dock.Right);
        row.Children.Add(actions);
        row.Children.Add(Ui.Row(14, new Border { Width = 120, Height = 45, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = HomePage.WebArt(GameDef.SteamArt(h.AppId), null, h.Name, 240) },
            Ui.Col(4, new TextBlock { Text = h.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis }, tags)));
        return row;
    }
}
