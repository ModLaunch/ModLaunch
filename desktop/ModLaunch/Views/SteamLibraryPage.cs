using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 14.0: библиотека Steam как в самом клиенте. Слева — список всех игр аккаунта (поиск, «Установленные»,
/// недавние сверху), справа — выбранная игра: большая картинка, «Играть» или «Установить», последний запуск,
/// наигранные часы, размер и есть ли для неё моды. Поиск по магазину Steam — прежняя страница SteamGamesPage.
/// </summary>
public sealed class SteamLibraryPage : Page
{
    public override string Title => I18n.T("v14.steam.title");
    public override string SearchHint => I18n.T("v14.steam.search");

    string _query = "";
    string _filter = Settings.Data.Str("steamLibFilter") is "installed" or "notInstalled" ? Settings.Data.Str("steamLibFilter")! : "all";
    int _selected = (int)Settings.Data.Long("steamLibSel");
    ScrollViewer _list = new();
    ContentControl _detail = new();

    public SteamLibraryPage(int appId = 0)
    {
        if (appId > 0) _selected = appId;
        void Changed() => Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
        AttachedToVisualTree += (_, _) => SteamAccount.Changed += Changed;
        DetachedFromVisualTree += (_, _) => SteamAccount.Changed -= Changed;
    }

    public override void Search(string text) { _query = text.Trim(); RenderList(); }

    IEnumerable<SteamGame> Visible()
    {
        IEnumerable<SteamGame> all = SteamAccount.All;
        if (_filter == "installed") all = all.Where(g => g.IsInstalled);
        else if (_filter == "notInstalled") all = all.Where(g => !g.IsInstalled);
        if (_query != "") all = all.Where(g => g.Name.Contains(_query, StringComparison.CurrentCultureIgnoreCase) || g.AppId.ToString() == _query);
        return all;
    }

    public override void Build()
    {
        var games = SteamAccount.All;
        if (games.Count == 0)
        {
            var empty = Ui.Card(Ui.Col(10,
                Ui.Text(I18n.T("v14.steam.none"), "h2"),
                Ui.Text(I18n.T("v14.steam.none.text"), "muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("v14.steam.store"), () => MainWindow.Current?.Navigate(() => new SteamGamesPage()), "primary", Icons.Globe))), 28);
            empty.Margin = new Thickness(34, 28);
            empty.MaxWidth = 720;
            empty.HorizontalAlignment = HorizontalAlignment.Left;
            empty.VerticalAlignment = VerticalAlignment.Top;
            Content = empty;
            return;
        }
        if (SteamAccount.ById(_selected) is null) _selected = games[0].AppId;
        // Каждая постройка — новые контейнеры: старые ещё висят в прошлом дереве.
        _list = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _detail = new ContentControl();

        var search = new TextBox { Watermark = I18n.T("v14.steam.filter"), Text = _query, Classes = { "search" } };
        search.TextChanged += (_, _) => { _query = search.Text?.Trim() ?? ""; RenderList(); };
        var chips = new WrapPanel();
        foreach (var f in new[] { "all", "installed", "notInstalled" })
        {
            var one = f;
            var count = one switch { "installed" => games.Count(g => g.IsInstalled), "notInstalled" => games.Count(g => !g.IsInstalled), _ => games.Count };
            chips.Children.Add(Ui.Button(I18n.T("v14.steam.f." + one) + " " + count.ToString("N0", I18n.Culture), () =>
            {
                _filter = one;
                Settings.Data["steamLibFilter"] = one;
                Settings.Save();
                Build();
            }, one == _filter ? "chip active" : "chip"));
            ((Control)chips.Children[^1]).Margin = new Thickness(0, 0, 4, 4);
        }
        var hours = games.Sum(g => g.Minutes) / 60;
        var head = Ui.Col(10,
            Ui.Row(10, Ui.Icon(Icons.Shelf, 16, Ui.Res("Muted")), Ui.Text(I18n.T("v14.steam.title"), "h3")),
            Ui.Text(I18n.T("v14.steam.total", ("n", games.Count.ToString("N0", I18n.Culture)), ("h", hours.ToString("N0", I18n.Culture))), "tiny muted"),
            search, chips);
        head.Margin = new Thickness(16, 18, 16, 10);

        var left = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        var store = Ui.Button(I18n.T("v14.steam.store"), () => MainWindow.Current?.Navigate(() => new SteamGamesPage(_query)), "ghost", Icons.Globe);
        store.HorizontalAlignment = HorizontalAlignment.Stretch;
        store.Margin = new Thickness(10, 6, 10, 12);
        DockPanel.SetDock(store, Dock.Bottom);
        left.Children.Add(store);
        left.Children.Add(_list);

        var side = new Border { Width = 300, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 1, 0), Background = Ui.Res("Layer"), Child = left };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(side);
        var right = new ScrollViewer { Content = _detail, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;
        RenderList();
        RenderDetail();
    }

    void RenderList()
    {
        var col = new StackPanel { Spacing = 1, Margin = new Thickness(8, 0, 8, 8) };
        var visible = Visible().ToList();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14).ToUnixTimeSeconds();
        var recent = _query == "" ? visible.Where(g => g.LastPlayed >= cutoff).Take(8).ToList() : [];
        if (recent.Count > 0)
        {
            col.Children.Add(Group(I18n.T("v14.steam.recent")));
            foreach (var g in recent) col.Children.Add(Row(g));
        }
        col.Children.Add(Group(I18n.T("v14.steam.allGames", ("n", visible.Count.ToString("N0", I18n.Culture)))));
        foreach (var g in visible.OrderBy(g => g.Name == "" ? "￿" : g.Name, StringComparer.CurrentCultureIgnoreCase)) col.Children.Add(Row(g));
        if (visible.Count == 0) col.Children.Add(new Border { Padding = new Thickness(10, 8), Child = Ui.Text(I18n.T("v14.steam.nothing"), "small muted", wrap: true) });
        _list.Content = col;
    }

    static Control Group(string text) => new TextBlock
    {
        Text = text.ToUpper(I18n.Culture), FontSize = 10, FontWeight = FontWeight.SemiBold, LetterSpacing = 1, Foreground = Ui.Res("Faint"), Margin = new Thickness(10, 12, 10, 6),
    };

    Control Row(SteamGame g)
    {
        var art = new Border { Width = 46, Height = 22, CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = HomePage.WebArt(GameDef.SteamArt(g.AppId), null, g.Name, 120), Opacity = g.IsInstalled ? 1 : 0.55 };
        var name = new TextBlock
        {
            Text = g.Name == "" ? "Steam · " + g.AppId : g.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = g.IsInstalled ? Ui.Res("Text") : Ui.Res("Muted"),
        };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(art, Dock.Left);
        art.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(art);
        if (Features.Launcher.IsRunning(SteamLibrary.Managed(g.AppId)?.Def.Id ?? ""))
        {
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Ui.Res("Good"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Right);
            row.Children.Add(dot);
        }
        row.Children.Add(name);
        var b = new Button { Classes = { "ghost", "steam-row" }, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5) };
        if (g.AppId == _selected) { b.Classes.Add("active"); b.Background = Ui.Res("Surface2"); }
        b.Click += (_, _) => { _selected = g.AppId; Settings.Data["steamLibSel"] = g.AppId; Settings.Save(); RenderList(); RenderDetail(); };
        b.DoubleTapped += (_, _) => { if (g.IsInstalled) SteamLibrary.Play(g.AppId); };
        ToolTip.SetTip(b, g.Name);
        return b;
    }

    void RenderDetail()
    {
        if (SteamAccount.ById(_selected) is not { } g) { _detail.Content = null; return; }
        var managed = SteamLibrary.Managed(g.AppId);
        var dir = managed is null ? GameDirectory.BySteam(g.AppId) ?? (g.Name != "" ? GameDirectory.ByName(g.Name) : null) : null;

        // Шапка: широкая картинка Steam, снизу плавно уходит в фон страницы.
        var hero = new Panel { Height = 320, ClipToBounds = true };
        hero.Children.Add(HomePage.WebArt(SteamLibrary.Hero(g.AppId), GameDef.SteamArt(g.AppId), g.Name, 1600));
        var bg = (Ui.Res("Layer") as ISolidColorBrush)?.Color ?? Colors.Black;
        hero.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0.35), new GradientStop(bg, 1) },
            },
        });
        var title = new TextBlock { Text = g.Name == "" ? "Steam · " + g.AppId : g.Name, FontSize = 34, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"), TextWrapping = TextWrapping.Wrap, MaxWidth = 900 };
        hero.Children.Add(new Border { Padding = new Thickness(34, 0, 34, 22), VerticalAlignment = VerticalAlignment.Bottom, Child = title });

        // Полоса действий, как у Steam: большая кнопка и три цифры.
        var main = g.IsInstalled
            ? Ui.Button(I18n.T("games.play"), () => SteamLibrary.Play(g.AppId), "primary big", Icons.Play)
            : Ui.Button(I18n.T("v12w.steam.install"), () => SteamLibrary.Install(g.AppId), "primary big", Icons.Download);
        main.MinWidth = 160;
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(main);
        bar.Children.Add(Fact(I18n.T("v14.steam.last"), g.LastPlayed > 0 ? When(g.LastPlayed) : I18n.T("v14.steam.never")));
        bar.Children.Add(Fact(I18n.T("v14.steam.played"), Hours(g.Minutes)));
        if (g.Size > 0) bar.Children.Add(Fact(I18n.T("v14.steam.size"), SizeText(g.Size)));
        var tools = Ui.Row(4,
            Ui.Button("", () => SteamLibrary.StorePage(g.AppId), "icon ghost", Icons.External, I18n.T("v12w.steam.page")));
        if (g.Installed is { } inst && Directory.Exists(inst.Path)) tools.Children.Add(Ui.Button("", () => Ui.OpenUrl(inst.Path), "icon ghost", Icons.Folder, I18n.T("v14.steam.folder")));
        tools.VerticalAlignment = VerticalAlignment.Center;
        tools.Margin = new Thickness(12, 0, 0, 0);
        bar.Children.Add(tools);
        var barCard = new Border { Classes = { "card" }, Padding = new Thickness(18, 14), Child = bar };

        // Моды: игра уже в ModLaunch, их можно добавить или для неё модов нет.
        Control mods;
        if (managed is not null)
            mods = Line(Icons.Layers, I18n.T("v14.steam.inModLaunch"), I18n.T("v14.steam.inModLaunch.text"),
                Ui.Button(I18n.T("v12w.steam.open"), () => MainWindow.Current?.Navigate(() => new GamePage(managed.Def.Id)), "", Icons.Forward));
        else if (dir is not null)
        {
            var where = string.Join(" · ", new[] { dir.Community is not null ? "Thunderstore" : null, dir.NexusDomain is not null ? "Nexus Mods" : null }.OfType<string>());
            mods = Line(Icons.Package, I18n.T("v14.steam.hasMods"), where == "" ? I18n.T("v14.steam.hasMods.text") : where,
                Ui.Button(I18n.T("v12w.steam.mods"), () => MainWindow.Current?.Navigate(() => new AddGamePage(g.Name)), "", Icons.Plus));
        }
        else mods = Line(Icons.Package, I18n.T("v14.steam.noMods"), I18n.T("v14.steam.noMods.text"), null);

        var body = Ui.Col(16, barCard, Ui.Card(mods, 18));
        body.Margin = new Thickness(34, -6, 34, 34);
        body.MaxWidth = 1100;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        _detail.Content = new StackPanel { Children = { hero, body } };
    }

    static Control Fact(string label, string value) => new Border
    {
        Margin = new Thickness(26, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = Ui.Col(2,
            new TextBlock { Text = label.ToUpper(I18n.Culture), FontSize = 9.5, LetterSpacing = 1, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Faint") },
            new TextBlock { Text = value, FontSize = 14, Foreground = Ui.Res("Text") }),
    };

    static Control Line(string icon, string title, string text, Control? action)
    {
        var row = new DockPanel();
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(action, Dock.Right);
            row.Children.Add(action);
        }
        row.Children.Add(Ui.Row(14, Ui.Icon(icon, 20, Ui.Res("Muted")), Ui.Col(3, Ui.Text(title, "h3"), Ui.Text(text, "small muted", wrap: true))));
        return row;
    }

    static string Hours(long minutes) => minutes <= 0
        ? I18n.T("v14.steam.zero")
        : minutes < 60 ? I18n.T("v14.steam.min", ("n", minutes)) : I18n.T("v14.steam.h", ("n", (minutes / 60.0).ToString(minutes < 600 ? "0.#" : "N0", I18n.Culture)));

    static string When(long unix)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        var days = (DateTime.Today - at.Date).Days;
        return days <= 0 ? I18n.T("v14.steam.today") : days == 1 ? I18n.T("v14.steam.yesterday") : at.ToString(at.Year == DateTime.Today.Year ? "d MMMM" : "d MMMM yyyy", I18n.Culture);
    }

    static string SizeText(long bytes) => bytes >= 1L << 30
        ? I18n.T("v12w.size.gb", ("n", (bytes / (double)(1L << 30)).ToString("0.#", I18n.Culture)))
        : GamePage.Size(bytes);
}
