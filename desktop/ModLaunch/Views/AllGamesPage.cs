using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 27.0: единая библиотека — все игры со всех лаунчеров ПК в одном списке. Слева — поиск, фильтр по магазину,
/// недавние сверху; справа — выбранная игра: «Играть» (через её лаунчер или через ModLaunch с модами),
/// последний запуск, часы, размер, папка и есть ли моды. Раскладка — как у библиотеки Steam (SteamLibraryPage).
/// </summary>
public sealed class AllGamesPage : Page
{
    public override string Title => I18n.T("v27.lib.title");
    public override string SearchHint => I18n.T("v27.lib.search");

    string _query = "";
    string _store = Settings.Data.Str("allGamesStore") ?? "all";
    string _selected = Settings.Data.Str("allGamesSel") ?? "";
    ScrollViewer _list = new();
    ContentControl _detail = new();
    ScrollViewer? _right;

    /// <summary>Снимки экрана: показать низ страницы игры (отзывы).</summary>
    internal void ScrollDetailToEnd() => _right?.ScrollToEnd();

    public AllGamesPage(string? key = null)
    {
        if (key is not null) _selected = key;
        void Changed() => Dispatcher.UIThread.Post(() => { if (MainWindow.Current?.CurrentPage == this) Build(); });
        AttachedToVisualTree += (_, _) => { AllGames.Changed += Changed; SteamAccount.Changed += Refresh; };
        DetachedFromVisualTree += (_, _) => { AllGames.Changed -= Changed; SteamAccount.Changed -= Refresh; };
    }

    static void Refresh() => Dispatcher.UIThread.Post(() => _ = AllGames.Refresh());

    public override void Search(string text) { _query = text.Trim(); RenderList(); }

    IReadOnlyList<LibGame> Games => AllGames.Current ?? [];

    IEnumerable<LibGame> Visible()
    {
        IEnumerable<LibGame> all = Games;
        if (_store == "installed") all = all.Where(g => g.Installed);
        else if (_store != "all") all = all.Where(g => g.Store == _store);
        if (_query != "") all = all.Where(g => g.Name.Contains(_query, StringComparison.CurrentCultureIgnoreCase));
        return all;
    }

    public override void Build()
    {
        if (AllGames.Stale) _ = AllGames.Refresh();
        var games = Games;
        if (AllGames.Current is null)
        {
            Content = Center(Ui.Col(12, Ui.Text(I18n.T("v27.lib.scanning"), "h2"), Ui.Text(I18n.T("v27.lib.scanning.text"), "muted", wrap: true)));
            return;
        }
        if (games.Count == 0)
        {
            Content = Center(Ui.Col(10,
                Ui.Text(I18n.T("v27.lib.none"), "h2"),
                Ui.Text(I18n.T("v27.lib.none.text"), "muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus))));
            return;
        }
        if (_store is not ("all" or "installed") && !games.Any(g => g.Store == _store)) _store = "all";
        if (games.All(g => g.Key != _selected)) _selected = (games.Where(g => g.Installed).OrderByDescending(g => AllGames.Played(g).Last).FirstOrDefault() ?? games[0]).Key;
        _list = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _detail = new ContentControl();

        var search = new TextBox { Watermark = I18n.T("v27.lib.filter"), Text = _query, Classes = { "search" } };
        search.TextChanged += (_, _) => { _query = search.Text?.Trim() ?? ""; RenderList(); };
        var chips = new WrapPanel();
        void Chip(string id, string text, int count)
        {
            var b = Ui.Button(text + " " + count.ToString("N0", I18n.Culture), () =>
            {
                _store = id;
                Settings.Data["allGamesStore"] = id;
                Settings.Save();
                Build();
            }, id == _store ? "chip active" : "chip");
            b.Margin = new Thickness(0, 0, 4, 4);
            chips.Children.Add(b);
        }
        Chip("all", I18n.T("v27.lib.f.all"), games.Count);
        Chip("installed", I18n.T("v27.lib.f.installed"), games.Count(g => g.Installed));
        foreach (var store in Stores.Order)
            if (games.Count(g => g.Store == store) is var n and > 0) Chip(store, Stores.Title(store), n);

        var hours = games.Sum(g => AllGames.Played(g).Minutes) / 60;
        var refresh = Ui.Button("", () => { _ = AllGames.Refresh(); }, "icon ghost", Icons.Refresh, I18n.T("v27.lib.refresh"));
        // 28.0: «Желаемое» — рядом с библиотекой, как в Steam.
        var wish = Ui.Button("", () => MainWindow.Current?.Navigate(() => new WishlistPage()), "icon ghost", Icons.Heart, I18n.T("v28.wish.title") + " · Ctrl+Shift+W");
        var titleRow = new DockPanel();
        DockPanel.SetDock(refresh, Dock.Right);
        titleRow.Children.Add(refresh);
        DockPanel.SetDock(wish, Dock.Right);
        titleRow.Children.Add(wish);
        // 29.0: «Для вас» — похожие игры.
        var forYou = Ui.Button("", () => MainWindow.Current?.Navigate(() => new RecommendPage()), "icon ghost", Icons.Sparkles, I18n.T("v29.rec.title"));
        DockPanel.SetDock(forYou, Dock.Right);
        titleRow.Children.Add(forYou);
        titleRow.Children.Add(Ui.Row(10, Ui.Icon(Icons.Grid, 16, Ui.Res("Muted")), Ui.Text(I18n.T("v27.lib.title"), "h3")));
        var stores = games.Select(g => g.Store).Distinct().Count();
        var head = Ui.Col(10,
            titleRow,
            Ui.Text(I18n.T("v27.lib.total", ("n", games.Count.ToString("N0", I18n.Culture)), ("stores", stores), ("h", hours.ToString("N0", I18n.Culture))), "tiny muted"),
            search, chips);
        head.Margin = new Thickness(16, 18, 16, 10);

        var left = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        var add = Ui.Button(I18n.T("v27.lib.addOwn"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "ghost", Icons.Plus);
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.Margin = new Thickness(10, 6, 10, 12);
        DockPanel.SetDock(add, Dock.Bottom);
        left.Children.Add(add);
        left.Children.Add(_list);

        var side = new Border { Width = 300, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 1, 0), Background = Ui.Res("Layer"), Child = left };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(side);
        var right = _right = new ScrollViewer { Content = _detail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;
        RenderList();
        RenderDetail();
    }

    static Control Center(Control inner)
    {
        var card = Ui.Card(inner, 28);
        card.Margin = new Thickness(34, 28);
        card.MaxWidth = 720;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    void RenderList()
    {
        var col = new StackPanel { Spacing = 1, Margin = new Thickness(8, 0, 8, 8) };
        var visible = Visible().ToList();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14).ToUnixTimeSeconds();
        var recent = _query == "" ? visible.Select(g => (Game: g, Last: AllGames.Played(g).Last)).Where(x => x.Last >= cutoff).OrderByDescending(x => x.Last).Take(8).Select(x => x.Game).ToList() : [];
        if (recent.Count > 0)
        {
            col.Children.Add(Group(I18n.T("v27.lib.recent")));
            foreach (var g in recent) col.Children.Add(Row(g));
        }
        col.Children.Add(Group(I18n.T("v27.lib.allGames", ("n", visible.Count.ToString("N0", I18n.Culture)))));
        foreach (var g in visible.OrderBy(g => g.Name == "" ? "￿" : g.Name, StringComparer.CurrentCultureIgnoreCase)) col.Children.Add(Row(g));
        if (visible.Count == 0) col.Children.Add(new Border { Padding = new Thickness(10, 8), Child = Ui.Text(I18n.T("v27.lib.nothing"), "small muted", wrap: true) });
        _list.Content = col;
    }

    static Control Group(string text) => new TextBlock
    {
        Text = text.ToUpper(I18n.Culture), FontSize = 10, FontWeight = FontWeight.SemiBold, LetterSpacing = 1, Foreground = Ui.Res("Faint"), Margin = new Thickness(10, 12, 10, 6),
    };

    static string NameOf(LibGame g) => g.Name == "" ? "Steam · " + g.Key[(g.Key.IndexOf(':') + 1)..] : g.Name;

    /// <summary>Картинка игры: Steam (свой номер или найденный по имени), логотип пакета Xbox или буква на цветном фоне.</summary>
    static Control Art(LibGame g, int decode, bool hero = false)
    {
        if (g.SteamAppId > 0)
            return hero ? HomePage.WebArt(SteamLibrary.Hero(g.SteamAppId), GameDef.SteamArt(g.SteamAppId), g.Name, decode) : HomePage.WebArt(GameDef.SteamArt(g.SteamAppId), null, g.Name, decode);
        var art = HomePage.WebArt("", null, g.Name, decode);
        if (g.Source?.Logo is { } logo && art is Panel panel)
        {
            var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(hero ? 60 : 3) };
            panel.Children.Add(image);
            _ = Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(logo)) return;
                    using var s = File.OpenRead(logo);
                    var bmp = Bitmap.DecodeToWidth(s, Math.Min(decode, 480));
                    Dispatcher.UIThread.Post(() => image.Source = bmp);
                }
                catch { }
            });
        }
        return art;
    }

    Control Row(LibGame g)
    {
        var art = new Border { Width = 46, Height = 22, CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = Art(g, 120), Opacity = g.Installed ? 1 : 0.55 };
        var name = new TextBlock
        {
            Text = NameOf(g), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = g.Installed ? Ui.Res("Text") : Ui.Res("Muted"),
        };
        var store = new TextBlock { Text = Stores.Title(g.Store), FontSize = 10.5, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(art, Dock.Left);
        art.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(art);
        if (AllGames.IsRunning(g))
        {
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Ui.Res("Good"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Right);
            row.Children.Add(dot);
        }
        if (_store == "all" || _store == "installed")
        {
            DockPanel.SetDock(store, Dock.Right);
            row.Children.Add(store);
        }
        row.Children.Add(name);
        var b = new Button { Classes = { "ghost", "steam-row" }, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5) };
        if (g.Key == _selected) { b.Classes.Add("active"); b.Background = Ui.Res("Surface2"); }
        b.Click += (_, _) => { _selected = g.Key; Settings.Data["allGamesSel"] = g.Key; Settings.Save(); RenderList(); RenderDetail(); };
        b.DoubleTapped += (_, _) => { if (g.Installed) Play(g); };
        ToolTip.SetTip(b, NameOf(g) + " · " + Stores.Title(g.Store));
        return b;
    }

    static GameState? Managed(LibGame g) => g.ManagedId is { } id ? AppState.Games.FirstOrDefault(x => x.Def.Id == id && x.Path is not null) : null;

    internal static void Play(LibGame g)
    {
        if (Managed(g) is { } managed) { Actions.Play(managed); return; }
        if (!g.Installed) { SteamLibrary.Install(g.SteamAppId); return; }
        if (AllGames.Play(g) is { } error) MainWindow.Current?.Toast(error, bad: true);
        else MainWindow.Current?.Toast(I18n.T("v27.lib.starting", ("name", NameOf(g)), ("store", Stores.Title(g.Store))));
    }

    void RenderDetail()
    {
        if (Games.FirstOrDefault(x => x.Key == _selected) is not { } g) { _detail.Content = null; return; }
        var managed = Managed(g);
        var dir = managed is null ? (g.SteamAppId > 0 ? GameDirectory.BySteam(g.SteamAppId) : null) ?? GameDirectory.ByName(g.Name) : null;
        var (last, minutes) = AllGames.Played(g);

        var hero = new Panel { Height = 320, ClipToBounds = true };
        hero.Children.Add(Art(g, 1600, hero: true));
        var bg = (Ui.Res("Layer") as ISolidColorBrush)?.Color ?? Colors.Black;
        hero.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0.35), new GradientStop(bg, 1) },
            },
        });
        var badge = new Border
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), Background = Ui.Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = Stores.Title(g.Store), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text") },
        };
        var title = new TextBlock { Text = NameOf(g), FontSize = 34, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"), TextWrapping = TextWrapping.Wrap, MaxWidth = 900 };
        hero.Children.Add(new Border { Padding = new Thickness(34, 0, 34, 22), VerticalAlignment = VerticalAlignment.Bottom, Child = Ui.Col(10, badge, title) });

        Button main;
        if (AllGames.IsRunning(g))
        {
            main = Ui.Button(I18n.T("v27.lib.running"), () => { }, "primary big", Icons.Play);
            main.IsEnabled = false;
        }
        else if (!g.Installed) main = Ui.Button(I18n.T("v12w.steam.install"), () => Play(g), "primary big", Icons.Download);
        else main = Ui.Button(managed is not null ? I18n.T("v27.lib.playMods") : I18n.T("games.play"), () => Play(g), "primary big", Icons.Play);
        main.MinWidth = 160;
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(main);
        bar.Children.Add(Fact(I18n.T("v14.steam.last"), last > 0 ? When(last) : I18n.T("v14.steam.never")));
        var played = Fact(I18n.T("v14.steam.played"), Hours(minutes));
        if (g.Store != "steam") ToolTip.SetTip(played, I18n.T("v27.lib.ownTime"));
        bar.Children.Add(played);
        if (g.Size > 0) bar.Children.Add(Fact(I18n.T("v14.steam.size"), SizeText(g.Size)));

        var tools = Ui.Row(4);
        if (AllGames.LauncherUri(g) is { } uri)
            tools.Children.Add(Ui.Button("", () => OpenUri(uri, g), "icon ghost", Icons.External, I18n.T("v27.lib.inLauncher", ("store", Stores.Title(g.Store)))));
        if (g.SteamAppId > 0) tools.Children.Add(Ui.Button("", () => SteamLibrary.StorePage(g.SteamAppId), "icon ghost", Icons.Globe, I18n.T("v12w.steam.page")));
        var folder = g.Source?.AltDir ?? g.Dir;
        if (folder is not null && Directory.Exists(folder)) tools.Children.Add(Ui.Button("", () => Ui.OpenUrl(folder), "icon ghost", Icons.Folder, I18n.T("v14.steam.folder")));
        tools.VerticalAlignment = VerticalAlignment.Center;
        tools.Margin = new Thickness(12, 0, 0, 0);
        bar.Children.Add(tools);
        var barCard = new Border { Classes = { "card" }, Padding = new Thickness(18, 14), Child = bar };

        Control mods;
        if (managed is not null)
            mods = Line(Icons.Layers, I18n.T("v14.steam.inModLaunch"), I18n.T("v27.lib.inModLaunch.text"),
                Ui.Button(I18n.T("v12w.steam.open"), () => MainWindow.Current?.Navigate(() => new GamePage(managed.Def.Id)), "", Icons.Forward));
        else if (dir is not null && g.Installed)
        {
            var where = string.Join(" · ", new[] { dir.Community is not null ? "Thunderstore" : null, dir.NexusDomain is not null ? "Nexus Mods" : null }.OfType<string>());
            mods = Line(Icons.Package, I18n.T("v14.steam.hasMods"), where == "" ? I18n.T("v14.steam.hasMods.text") : where,
                Ui.Button(I18n.T("v12w.steam.mods"), () => MainWindow.Current?.Navigate(() => new AddGamePage(g.Name)), "", Icons.Plus));
        }
        else if (dir is not null)
            mods = Line(Icons.Package, I18n.T("v14.steam.hasMods"), I18n.T("v27.lib.modsAfterInstall"), null);
        else mods = Line(Icons.Package, I18n.T("v14.steam.noMods"), I18n.T("v27.lib.noMods.text"), null);

        // 28.0: отзывы игроков — оценить может только тот, кто наиграл час и больше.
        var body = Ui.Col(16, barCard);
        // 30.0: достижения Steam — из файлов Steam на этом ПК.
        if (SteamAchievementsCard.For(g) is { } achievements) body.Children.Add(achievements);
        body.Children.Add(Ui.Card(mods, 18));
        body.Children.Add(new GameReviewsCard(g));
        if (folder is not null) body.Children.Add(Ui.Text(folder, "tiny muted", wrap: true));
        body.Margin = new Thickness(34, -6, 34, 34);
        body.MaxWidth = 1100;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        _detail.Content = new StackPanel { Children = { hero, body } };
    }

    static void OpenUri(string uri, LibGame g)
    {
        if (Program.Screenshot) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { MainWindow.Current?.Toast(I18n.T("v27.lib.noLauncher", ("store", Stores.Title(g.Store))), bad: true); }
    }

    static Control Fact(string label, string value) => new Border
    {
        Margin = new Thickness(26, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
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

    internal static string Hours(long minutes) => minutes <= 0
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
