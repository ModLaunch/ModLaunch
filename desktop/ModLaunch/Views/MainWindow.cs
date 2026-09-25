using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>Экран программы: у каждого свой адрес, чтобы работали «назад» и «вперёд».</summary>
public abstract class Page : UserControl
{
    public abstract string Title { get; }
    public virtual string? GameId => null;
    public virtual string SearchHint => I18n.T("search.home");
    public virtual void Search(string text) { }
    /// <summary>Перестроить содержимое (язык сменился, игра нашлась…).</summary>
    public abstract void Build();
}

/// <summary>
/// Окно со своей рамкой: слева — главная, игры и настройки, сверху — навигация,
/// поиск, загрузки и кнопки окна. Страница меняется в середине.
/// </summary>
public sealed class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }

    readonly ContentControl _page = new() { Name = "Page" };
    readonly StackPanel _railGames = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
    readonly TextBox _search = new() { Width = 320, Height = 40 };
    readonly Button _back, _forward, _downloads, _homeButton, _settingsButton, _friendsButton, _statsButton, _donateButton, _creatorButton, _libraryButton;
    readonly LayoutTransformControl _scale = new();
    Control? _railHost;
    Control? _brandWord;
    Panel? _layers;
    readonly Button _updatePill = new() { Classes = { "chip" }, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _friendsBadge = new() { IsVisible = false };
    readonly Panel _overlay = new() { IsVisible = false };
    readonly StackPanel _toasts = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 24, 24) };
    readonly Border _downloadsPanel;
    readonly StackPanel _downloadsList = new() { Spacing = 10 };
    readonly Border _dlBadge = new() { IsVisible = false };
    readonly Button _bell = new() { Classes = { "icon" } };
    readonly Border _bellBadge = new() { IsVisible = false };
    readonly Border _bellPanel;
    readonly StackPanel _bellList = new() { Spacing = 8 };

    readonly List<Func<Page>> _history = [];
    int _index = -1;
    Page? _current;

    public MainWindow()
    {
        Current = this;
        Title = "ModLaunch";
        Width = 1366;
        Height = 800;
        MinWidth = 980;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
        try { Icon = new WindowIcon(Images.Asset("icon.png")); } catch { }

        _back = Ui.Button("", GoBack, "icon ghost", Icons.Back, I18n.T("nav.back"));
        _forward = Ui.Button("", GoForward, "icon ghost", Icons.Forward, I18n.T("nav.forward"));
        _homeButton = RailIcon(Icons.Home, () => Navigate(() => new HomePage()), I18n.T("nav.menu"));
        _settingsButton = RailIcon(Icons.Settings, () => Navigate(() => new SettingsPage()), I18n.T("nav.settings"));
        _friendsButton = RailIcon(Icons.Users, () => Navigate(() => new FriendsPage()), I18n.T("friends.title"));
        _statsButton = RailIcon(Icons.Chart, () => Navigate(() => new StatsPage()), I18n.T("stats.title"));
        _donateButton = RailIcon(Icons.Coffee, () => Navigate(() => new DonatePage()), I18n.T("nav.donate"));
        // Как в Modrinth App: разделы — отдельными пунктами на боковой панели.
        _libraryButton = RailIcon(Icons.Layers, () => Navigate(() => new LibraryPage()), I18n.T("lib.title"));
        _creatorButton = RailIcon(Icons.Code, () => Navigate(() => new CreatorPage()), "Creator Hub");
        _friendsBadge.Width = 10; _friendsBadge.Height = 10; _friendsBadge.CornerRadius = new CornerRadius(5);
        _friendsBadge.Background = Ui.Res("Good"); _friendsBadge.HorizontalAlignment = HorizontalAlignment.Right; _friendsBadge.VerticalAlignment = VerticalAlignment.Top;
        _friendsBadge.Margin = new Thickness(0, 6, 6, 0);
        var friendsIcon = (Control)_friendsButton.Content!;
        _friendsButton.Content = null;
        _friendsButton.Content = new Panel { Children = { friendsIcon, _friendsBadge } };
        _updatePill.Click += (_, _) => ShowUpdate();

        _downloads = new Button
        {
            Classes = { "icon" },
            Content = new Panel { Children = { Ui.Icon(Icons.Download, 18), _dlBadge } },
        };
        _dlBadge.Classes.Add("pulse");
        _dlBadge.RenderTransform = new ScaleTransform();
        _dlBadge.Width = 10; _dlBadge.Height = 10; _dlBadge.CornerRadius = new CornerRadius(5);
        _dlBadge.Background = Ui.Res("Brand2"); _dlBadge.HorizontalAlignment = HorizontalAlignment.Right; _dlBadge.VerticalAlignment = VerticalAlignment.Top;
        _dlBadge.Margin = new Thickness(0, -4, -4, 0);
        _downloads.Click += (_, _) => { _downloadsPanel!.IsVisible = !_downloadsPanel.IsVisible; _bellPanel!.IsVisible = false; };
        ToolTip.SetTip(_downloads, I18n.T("dl.button"));

        _bellBadge.Width = 10; _bellBadge.Height = 10; _bellBadge.CornerRadius = new CornerRadius(5);
        _bellBadge.Background = Ui.Res("Bad"); _bellBadge.HorizontalAlignment = HorizontalAlignment.Right; _bellBadge.VerticalAlignment = VerticalAlignment.Top;
        _bellBadge.Margin = new Thickness(0, -4, -4, 0);
        _bell.Content = new Panel { Children = { Ui.Icon(Icons.Bell, 18), _bellBadge } };
        _bell.Click += (_, _) => { _bellPanel!.IsVisible = !_bellPanel.IsVisible; _downloadsPanel!.IsVisible = false; RenderBell(); };
        ToolTip.SetTip(_bell, I18n.T("nx.notify"));
        _bellPanel = new Border
        {
            Classes = { "card" }, Width = 400, MaxHeight = 540, Padding = new Thickness(16), Margin = new Thickness(0, 64, 64, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsVisible = false,
            BoxShadow = BoxShadows.Parse("0 18 50 0 #80000000"),
            Child = Ui.Col(12, Ui.Text(I18n.T("nx.notify"), "h3"), new ScrollViewer { Content = _bellList, MaxHeight = 460 }),
        };
        Features.Tracking.Changed += () => Dispatcher.UIThread.Post(() => { RenderBell(); });

        _downloadsPanel = new Border
        {
            Classes = { "card" },
            Width = 380,
            MaxHeight = 520,
            Padding = new Thickness(16),
            Margin = new Thickness(0, 64, 16, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false,
            BoxShadow = BoxShadows.Parse("0 18 50 0 #80000000"),
            Child = Ui.Col(12, Ui.Text(I18n.T("dl.title"), "h3"), new ScrollViewer { Content = _downloadsList, MaxHeight = 440 }),
        };

        _scale.Child = BuildLayout();
        Content = _scale;
        ApplyScale();
        Look.Changed += () => { ApplyScale(); RenderRail(); _current?.Build(); };

        I18n.Changed += () => { RebuildChrome(); _current?.Build(); };
        AppState.Changed += OnStateChanged;
        Jobs.Changed += _ => RenderDownloads();
        Jobs.Finished += OnJobFinished;
        KeyDown += OnKey;
        Features.Launcher.Exited += OnGameExit;
        Features.Nxm.Received += OnExternal;

        Navigate(StartPage());
        RenderRail();
        SetupTray();
        RenderBell();
        RenderDownloads();
        if (!Program.Screenshot) _ = StartUp();
        if (Program.Autostarted && Settings.Data.Bool("startMinimized"))
        {
            if (Settings.Data.Bool("closeToTray")) Opened += (_, _) => Dispatcher.UIThread.Post(Hide);
            else WindowState = WindowState.Minimized;
        }
    }

    Control BuildLayout()
    {
        // Боковая панель: логотип, главная, игры, настройки.
        var logo = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(14), ClipToBounds = true,
            Child = new Image { Source = Images.Asset("icon.png", 96), Stretch = Stretch.UniformToFill },
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        logo.PointerPressed += (_, e) => { if (e.GetCurrentPoint(logo).Properties.IsLeftButtonPressed) Navigate(() => new HomePage()); };

        var rail = new DockPanel
        {
            Width = 76,
            Background = Ui.Res("Rail"),
            LastChildFill = true,
        };
        var top = Ui.Col(8, logo, new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(14, 6) }, _homeButton, _libraryButton, _creatorButton,
            new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(14, 6, 14, 0) });
        top.Margin = new Thickness(0, 14, 0, 10);
        top.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(top, Dock.Top);
        var bottom = Ui.Col(8, _friendsButton, _statsButton, _settingsButton);
        bottom.Margin = new Thickness(0, 10, 0, 16);
        bottom.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(bottom, Dock.Bottom);
        rail.Children.Add(top);
        rail.Children.Add(bottom);
        rail.Children.Add(new ScrollViewer { Content = _railGames, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
        var railBorder = new Border { Child = rail, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 1, 0) };
        _railHost = railBorder;
        railBorder.IsVisible = !Settings.Data.Bool("railHidden");

        // Верхняя строка: тянется за неё всё окно.
        _search.Watermark = I18n.T("search.home");
        _search.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Search, 16, Ui.Res("Muted")) };
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) _current?.Search(_search.Text ?? ""); };

        _brandWord = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center, FontSize = 20, FontWeight = FontWeight.Bold,
                Inlines = { new Avalonia.Controls.Documents.Run("Mod"), new Avalonia.Controls.Documents.Run("Launch") { Foreground = Ui.Res("Brand2") } },
            };
        // Минимализм: по умолчанию в шапке только название экрана, логотип — на боковой панели.
        var brand = Ui.Row(10, _brandWord, _title);
        _brandWord.IsVisible = Settings.Data.Bool("showBrand");

        var winButtons = Ui.Row(2,
            WinButton(Icons.Minimize, () => WindowState = WindowState.Minimized, I18n.T("win.minimize")),
            WinButton(Icons.Maximize, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, I18n.T("win.maximize")),
            WinButton(Icons.Close, Close, I18n.T("win.close"), "close"));

        var topbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            Height = 68,
            Background = Brushes.Transparent,
        };
        var left = Ui.Row(6, _back, _forward, new Border { Width = 8 }, brand);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(20, 0, 0, 0);
        topbar.Children.Add(left);
        _search.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_search, 2);
        topbar.Children.Add(_search);
        var dlWrap = new Border { Child = Ui.Row(10, _updatePill, _bell, _downloads), Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(dlWrap, 3);
        topbar.Children.Add(dlWrap);
        winButtons.VerticalAlignment = VerticalAlignment.Center;
        winButtons.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(winButtons, 4);
        topbar.Children.Add(winButtons);
        topbar.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is null && v.FindAncestorOfType<TextBox>(true) is null)
            {
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
            }
        };
        var topBorder = new Border { Child = topbar, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };

        var main = new DockPanel();
        DockPanel.SetDock(topBorder, Dock.Top);
        main.Children.Add(topBorder);
        main.Children.Add(_page);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        root.Children.Add(railBorder);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        var layers = new Panel();
        _layers = layers;
        layers.Classes.Set("anim", Look.Animations);
        layers.Children.Add(root);
        layers.Children.Add(_downloadsPanel);
        layers.Children.Add(_bellPanel);
        layers.Children.Add(_toasts);
        layers.Children.Add(_overlay);
        return layers;
    }

    static Button RailIcon(string icon, Action onClick, string tip)
    {
        var b = Ui.Button("", onClick, "rail", icon, tip);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        b.Foreground = Ui.Res("Muted");
        return b;
    }

    static Button WinButton(string icon, Action onClick, string tip, string extra = "")
    {
        var b = new Button { Classes = { "win" }, Content = Ui.Icon(icon, 14) };
        if (extra != "") b.Classes.Add(extra);
        b.Click += (_, _) => onClick();
        ToolTip.SetTip(b, tip);
        return b;
    }

    void RebuildChrome()
    {
        _search.Watermark = _current?.SearchHint ?? I18n.T("search.home");
        _title.Text = _current?.Title ?? "";
        RenderRail();
        RenderDownloads();
    }

    void OnStateChanged()
    {
        RenderRail();
        _current?.Build();
    }

    void RenderRail()
    {
        _railGames.Children.Clear();
        foreach (var g in RailGames())
        {
            var id = g.Def.Id;
            var b = new Button
            {
                Classes = { "rail" },
                Content = new Border { CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = Ui.GameImage(g.Def, 120) },
            };
            if (_current?.GameId == id) b.Classes.Add("active");
            b.Click += (_, _) => Navigate(() => new GamePage(id));
            ToolTip.SetTip(b, g.Def.Name);
            _railGames.Children.Add(b);
        }
        var plus = RailIcon(Icons.Plus, () => Navigate(() => new AddGamePage()), I18n.T("add.title"));
        plus.Classes.Set("active", _current is AddGamePage);
        _railGames.Children.Add(plus);
        _creatorButton.Classes.Set("active", _current is CreatorPage);
        _libraryButton.Classes.Set("active", _current is LibraryPage);
        _friendsButton.IsVisible = Settings.Data.Bool("railFriends", true);
        _homeButton.Classes.Set("active", _current is HomePage);
        _settingsButton.Classes.Set("active", _current is SettingsPage);
        _friendsButton.Classes.Set("active", _current is FriendsPage);
        _statsButton.Classes.Set("active", _current is StatsPage);
        _donateButton.Classes.Set("active", _current is DonatePage);
        _statsButton.IsVisible = Settings.Data.Bool("ownerStats");
        var view = Social.Friends.View();
        _friendsBadge.IsVisible = view.Incoming.Count > 0 || view.Friends.Any(f => f.State != "offline");
        _friendsBadge.Background = view.Incoming.Count > 0 ? Ui.Res("Warn") : Ui.Res("Good");
        _updatePill.IsVisible = Setup.Updater.Available;
        if (Setup.Updater.Latest is { } latest) _updatePill.Content = Ui.Row(6, Ui.Icon(Icons.Sparkles, 14), new TextBlock { Text = I18n.T("upd.app.pill", ("version", latest.Version)), VerticalAlignment = VerticalAlignment.Center });
    }

    /// <summary>Игры на боковой панели: без скрытых, по желанию — только найденные, свой порядок.</summary>
    /// <summary>
    /// Игры на боковой панели — как недавние сборки в Modrinth App: только те,
    /// что есть на компьютере, недавно запущенные сверху, не больше восьми.
    /// Остальные — в «Библиотеке».
    /// </summary>
    public static IEnumerable<GameState> RailGames()
    {
        var hidden = Settings.Data.Arr("hiddenGames").Select(x => x?.ToString()).ToHashSet();
        return OrderedGames()
            .Where(g => !hidden.Contains(g.Def.Id) && g.Status == Detect.Found)
            .OrderByDescending(g => Features.PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue)
            .Take(8);
    }

    /// <summary>Все игры в порядке, заданном в настройках (остальные — как в программе).</summary>
    public static IEnumerable<GameState> OrderedGames()
    {
        var order = Settings.Data.Arr("gameOrder").Select(x => x?.ToString()).ToList();
        return AppState.Games.Select((g, i) => (g, i))
            .OrderBy(x => order.IndexOf(x.g.Def.Id) is var k && k >= 0 ? k : 10000 + x.i)
            .Select(x => x.g);
    }

    public static void MoveGame(string id, int delta)
    {
        var ids = OrderedGames().Select(g => g.Def.Id).ToList();
        var at = ids.IndexOf(id);
        var to = at + delta;
        if (at < 0 || to < 0 || to >= ids.Count) return;
        (ids[at], ids[to]) = (ids[to], ids[at]);
        Settings.Data["gameOrder"] = new System.Text.Json.Nodes.JsonArray(ids.Select(x => (System.Text.Json.Nodes.JsonNode)x).ToArray());
        Settings.Save();
    }

    /// <summary>Перерисовать рамку окна после смены настроек интерфейса.</summary>
    public void Refresh()
    {
        Settings.Save();
        ApplyScale();
        RenderRail();
    }

    void ApplyScale()
    {
        _layers?.Classes.Set("anim", Look.Animations);
        var k = Look.Scale;
        _scale.LayoutTransform = Math.Abs(k - 1) < 0.001 ? null : new ScaleTransform(k, k);
        if (_brandWord is not null) _brandWord.IsVisible = Settings.Data.Bool("showBrand");
        if (_railHost is not null) _railHost.IsVisible = !Settings.Data.Bool("railHidden");
    }

    /// <summary>С чего начинать: главная, последняя игра, Creator Hub или библиотека.</summary>
    static Func<Page> StartPage()
    {
        switch (Settings.Data.Str("startPage"))
        {
            case "lastGame" when Settings.Data.Str("lastGame") is string id && AppState.Games.Any(g => g.Def.Id == id):
                return () => new GamePage(id);
            case "creator": return () => new CreatorPage();
            case "add": return () => new AddGamePage();
            default: return () => new HomePage();
        }
    }

    /// <summary>Скрыть или показать боковую панель (Ctrl+B) — «дзен-режим».</summary>
    public void ToggleRail()
    {
        Settings.Data["railHidden"] = !Settings.Data.Bool("railHidden");
        Settings.Save();
        ApplyScale();
    }

    // ---------------------------------------------------------------- значок в трее

    TrayIcon? _tray;
    bool _quitting;

    void SetupTray()
    {
        if (Program.Screenshot) return;
        Closing += (_, e) =>
        {
            if (_quitting || !Settings.Data.Bool("closeToTray")) return;
            e.Cancel = true;
            Hide();
        };
        UpdateTray();
    }

    public void UpdateTray()
    {
        if (Program.Screenshot || Application.Current is null) return;
        var want = Settings.Data.Bool("closeToTray");
        if (!want) { if (_tray is not null) { _tray.IsVisible = false; _tray.Dispose(); _tray = null; } return; }
        if (_tray is not null) return;
        var menu = new NativeMenu();
        var open = new NativeMenuItem(I18n.T("tray.open"));
        open.Click += (_, _) => ShowFromTray();
        menu.Items.Add(open);
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found).Take(8))
        {
            var item = new NativeMenuItem(I18n.T("tray.play", ("game", g.Def.Name)));
            var gs = g;
            item.Click += (_, _) => Actions.Play(gs);
            menu.Items.Add(item);
        }
        menu.Items.Add(new NativeMenuItemSeparator());
        var quit = new NativeMenuItem(I18n.T("tray.quit"));
        quit.Click += (_, _) => { _quitting = true; Close(); };
        menu.Items.Add(quit);
        _tray = new TrayIcon { ToolTipText = "ModLaunch", Menu = menu, IsVisible = true };
        try { _tray.Icon = new WindowIcon(Images.Asset("icon.png")); } catch { }
        _tray.Clicked += (_, _) => ShowFromTray();
        TrayIcon.SetIcons(Application.Current, [_tray]);
    }

    void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // ---------------------------------------------------------------- быстрый переход (Ctrl+P)

    public void CommandPalette()
    {
        var box = new TextBox { Watermark = I18n.T("cmd.hint") };
        var list = new StackPanel { Spacing = 2 };
        var commands = new List<(string Title, string Icon, Action Run)>
        {
            (I18n.T("nav.menu"), Icons.Home, () => Navigate(() => new HomePage())),
            ("Creator Hub", Icons.Code, () => Navigate(() => new CreatorPage())),
            (I18n.T("add.title"), Icons.Plus, () => Navigate(() => new AddGamePage())),
            (I18n.T("nav.settings"), Icons.Settings, () => Navigate(() => new SettingsPage())),
            (I18n.T("look.title"), Icons.Palette, () => Navigate(() => new SettingsPage("look"))),
            (I18n.T("friends.title"), Icons.Users, () => Navigate(() => new FriendsPage())),
            (I18n.T("cmd.rail"), Icons.Layers, () => ToggleRail()),
            (I18n.T("cmd.theme"), Icons.Eye, () => Look.SetTheme(Look.Themes[(Array.IndexOf(Look.Themes, Look.Theme) + 1) % Look.Themes.Length])),
            (I18n.T("keys.title"), Icons.Key, () => Shortcuts()),
        };
        foreach (var g in AppState.Games)
        {
            var gs = g;
            commands.Add((gs.Def.Name, Icons.Folder, () => Navigate(() => new GamePage(gs.Def.Id))));
            if (gs.Status == Detect.Found) commands.Add((I18n.T("tray.play", ("game", gs.Def.Name)), Icons.Play, () => Actions.Play(gs)));
        }
        var shown = new List<(string Title, string Icon, Action Run)>();
        var selected = 0;
        void Render()
        {
            var q = (box.Text ?? "").Trim();
            shown = commands.Where(c => q == "" || c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(9).ToList();
            selected = Math.Clamp(selected, 0, Math.Max(0, shown.Count - 1));
            list.Children.Clear();
            for (var i = 0; i < shown.Count; i++)
            {
                var c = shown[i];
                var b = Ui.Button(c.Title, () => { CloseDialog(); c.Run(); }, i == selected ? "tab active" : "tab", c.Icon);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                list.Children.Add(b);
            }
        }
        box.TextChanged += (_, _) => { selected = 0; Render(); };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { selected++; Render(); e.Handled = true; }
            else if (e.Key == Key.Up) { selected--; Render(); e.Handled = true; }
            else if (e.Key == Key.Enter && shown.Count > 0) { var c = shown[selected]; CloseDialog(); c.Run(); e.Handled = true; }
        };
        Render();
        Dialog(I18n.T("cmd.title"), Ui.Col(10, box, list));
        Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Background);
    }

    public void Shortcuts()
    {
        var rows = new StackPanel { Spacing = 8 };
        foreach (var (keys, what) in new[]
        {
            ("Ctrl+P", "keys.palette"), ("Ctrl+K", "keys.search"), ("Ctrl+J", "keys.downloads"), ("Ctrl+B", "keys.rail"),
            ("Ctrl+,", "keys.settings"), ("Ctrl+Shift+C", "keys.creator"), ("Alt+← / Alt+→", "keys.history"), ("F1", "keys.help"), ("Esc", "keys.close"),
        })
        {
            var row = new DockPanel();
            var k = new Border { Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), Child = Ui.Text(keys, "small") };
            DockPanel.SetDock(k, Dock.Right);
            row.Children.Add(k);
            row.Children.Add(Ui.Text(I18n.T(what)));
            rows.Children.Add(row);
        }
        Dialog(I18n.T("keys.title"), rows, Ui.Button(I18n.T("common.close"), CloseDialog, "primary"));
    }

    // ---------------------------------------------------------------- навигация

    public void Navigate(Func<Page> make)
    {
        if (_index < _history.Count - 1) _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        _history.Add(make);
        _index = _history.Count - 1;
        Show(make());
    }

    void GoBack() { if (_index > 0) Show(_history[--_index]()); }
    void GoForward() { if (_index < _history.Count - 1) Show(_history[++_index]()); }

    void Show(Page page)
    {
        _current = page;
        if (page.GameId is string gid) { Settings.Data["lastGame"] = gid; Settings.Save(); }
        page.Build();
        _page.Content = page;
        _title.Text = page.Title;
        _search.Text = "";
        _search.Watermark = page.SearchHint;
        _back.IsEnabled = _index > 0;
        _forward.IsEnabled = _index < _history.Count - 1;
        _downloadsPanel.IsVisible = false;
        _bellPanel.IsVisible = false;
        RenderRail();
    }

    /// <summary>Уведомления: новые версии отслеживаемых модов (всех каталогов и ModLaunch Hub).</summary>
    void RenderBell()
    {
        var updates = Features.Tracking.Updates;
        var hub = Creator.Hub.Updates.Where(h => !updates.Any(u => u.Item.Source == "hub" && u.Item.Id == h.Id)).ToList();
        _bellBadge.IsVisible = updates.Count + hub.Count > 0;
        _bell.IsVisible = _bellBadge.IsVisible || _bellPanel.IsVisible;
        _bellList.Children.Clear();
        if (updates.Count + hub.Count == 0)
        {
            _bellList.Children.Add(Ui.Col(6, Ui.Text(I18n.T("nx.notify.empty"), "h3"), Ui.Text(I18n.T("nx.notify.empty.text"), "muted small", wrap: true)));
            return;
        }
        foreach (var u in updates)
        {
            var uu = u;
            var game = Games.GameCatalog.ById(u.Item.Game);
            var row = new Button
            {
                Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8),
                Content = Ui.Row(10, Ui.Thumb(u.Now.Icon ?? u.Item.Icon, u.Item.Name, 40, 10), Ui.Col(2,
                    Ui.Text(u.Item.Name, "h3"),
                    Ui.Text(I18n.T("nx.notify.update", ("from", u.Item.Version), ("to", u.Now.Version)), "small", color: Ui.Res("Good")),
                    Ui.Text($"{game?.Name} · {Sources.Catalog.Title(u.Item.Source)}", "small muted"))),
            };
            row.Click += (_, _) =>
            {
                Features.Tracking.Seen(uu);
                _bellPanel.IsVisible = false;
                if (game is not null) Navigate(() => new ModPage(game.Id, uu.Now));
            };
            _bellList.Children.Add(row);
        }
        foreach (var h in hub)
        {
            var hh = h;
            var row = new Button
            {
                Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8),
                Content = Ui.Row(10, Ui.Thumb(h.Images.FirstOrDefault(), h.Name, 40, 10), Ui.Col(2,
                    Ui.Text(h.Name, "h3"), Ui.Text(I18n.T("nx.notify.hub", ("version", h.Version)), "small", color: Ui.Res("Good")))),
            };
            row.Click += (_, _) => { Creator.Hub.MarkSeen(hh); _bellPanel.IsVisible = false; CreatorPage.OpenMod(hh); };
            _bellList.Children.Add(row);
        }
        var clear = Ui.Button(I18n.T("nx.notify.clear"), () =>
        {
            foreach (var u in Features.Tracking.Updates.ToList()) Features.Tracking.Seen(u);
            foreach (var h in Creator.Hub.Updates.ToList()) Creator.Hub.MarkSeen(h);
            RenderBell();
        }, "ghost", Icons.Check);
        _bellList.Children.Add(clear);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left) GoBack();
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Right) GoForward();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.K) _search.Focus();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.J) _downloadsPanel.IsVisible = !_downloadsPanel.IsVisible;
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.P) CommandPalette();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.B) ToggleRail();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.OemComma) Navigate(() => new SettingsPage());
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.C) Navigate(() => new CreatorPage());
        else if (e.Key == Key.F1) Shortcuts();
        else if (e.Key == Key.Escape) { if (_overlay.IsVisible) CloseDialog(); else _downloadsPanel.IsVisible = false; }
    }

    async Task StartUp()
    {
        Setup.Updater.Changed += () => Dispatcher.UIThread.Post(RenderRail);
        Social.Account.Changed += () => Dispatcher.UIThread.Post(() => { RenderRail(); _current?.Build(); });
        _ = Task.Run(async () =>
        {
            try { await Setup.Updater.Check(); } catch { }
            try { await Social.Reviews.Sync(); } catch { }
            if (Social.Account.SignedIn)
            {
                try { await Social.Account.RefreshProfile(); } catch { }
                try { await Social.Friends.BeatNow(); await Social.Friends.Refresh(); } catch { }
                Dispatcher.UIThread.Post(RenderRail);
            }
        });
        // «В сети» для друзей — отметка раз в пять минут, пока программа открыта.
        DispatcherTimer.Run(() =>
        {
            if (Social.Account.SignedIn) _ = Task.Run(async () =>
            {
                try { await Social.Friends.BeatNow(); await Social.Friends.Refresh(); } catch { }
                Dispatcher.UIThread.Post(RenderRail);
            });
            return true;
        }, TimeSpan.FromMinutes(5));
        Closing += (_, e) =>
        {
            if (e.Cancel) return;
            _tray?.Dispose();
            Features.Hotkey.Disarm();
            try { Social.Friends.GoOffline().Wait(1500); } catch { }
        };
        await AppState.DetectAll();
        _ = Task.Run(async () =>
        {
            try { await Features.Tracking.Check(); } catch { }
            try { await Creator.Hub.All(); } catch { }
            Dispatcher.UIThread.Post(RenderBell);
        });
        if (Program.StartupLink is string link) Actions.InstallNxm(link);
        if (Features.ModUpdates.OnStart)
        {
            foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && g.ModCount > 0))
            {
                try { await Features.ModUpdates.Check(g); } catch { }
            }
            AppState.Notify();
        }
    }

    void OnGameExit(string gameId, bool counted, long ms)
    {
        OverlayWindow.OnGameExited();
        Social.Friends.SetActivity("online");
        var game = AppState.Game(gameId);
        if (Settings.Data.Str("afterLaunch") == "minimize" && Settings.Data.Bool("restoreAfterGame", true) && WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        if (counted) Toast(I18n.T("time.session", ("game", game.Def.Name), ("time", Features.PlayTime.Format(ms))));
        AppState.Notify();
    }

    /// <summary>Второй запуск программы передал нам ссылку nxm:// (или просто просит показаться).</summary>
    public void OnExternal(string line)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (line.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase)) Actions.InstallNxm(line);
    }

    /// <summary>Окно «Вышел ModLaunch X»: что нового и кнопка обновления.</summary>
    public void ShowUpdate()
    {
        if (Setup.Updater.Latest is not { } latest) return;
        var notes = new SelectableTextBlock { Text = latest.Notes, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted") };
        var body = Ui.Col(10, Ui.Text(I18n.T("upd.app.now", ("version", Http.Version)), "small muted"), new ScrollViewer { Content = notes, MaxHeight = 300 });
        var actions = new List<Control> { Ui.Button(I18n.T("upd.app.later"), CloseDialog) };
        if (latest.Page is string page) actions.Add(Ui.Button(I18n.T("upd.app.page"), () => Ui.OpenUrl(page), "", Icons.External));
        if (Setup.Updater.CanInstall)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false };
            body.Children.Add(bar);
            Button? go = null;
            go = Ui.Button(I18n.T("upd.app.install"), async () =>
            {
                go!.IsEnabled = false;
                go.Content = I18n.T("upd.app.installing");
                bar.IsVisible = true;
                try
                {
                    await Setup.Updater.Fetch(new Progress<double>(r => bar.Value = r));
                    Setup.Updater.Launch();
                    Close();
                }
                catch (Exception e) { Toast(Jobs.Explain(e), bad: true); CloseDialog(); }
            }, "primary", Icons.Download);
            actions.Add(go);
        }
        Dialog(I18n.T("upd.app.title", ("version", latest.Version)), body, actions.ToArray());
    }

    // ---------------------------------------------------------------- загрузки и уведомления

    void RenderDownloads()
    {
        _dlBadge.IsVisible = Jobs.Running > 0;
        _downloadsList.Children.Clear();
        _downloadsList.Children.Add(Ui.Button(I18n.T("nx.history"), ShowHistory, "ghost", Icons.Clock));
        if (Jobs.All.Count == 0)
        {
            _downloadsList.Children.Add(Ui.Col(6, Ui.Text(I18n.T("dl.empty"), "h3"), Ui.Text(I18n.T("dl.empty.text"), "muted small", wrap: true)));
            return;
        }
        foreach (var job in Jobs.All.Take(30))
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = Math.Max(0, job.Ratio), IsIndeterminate = job.Status == JobStatus.Running && job.Ratio < 0 };
            var color = job.Status switch { JobStatus.Done => Ui.Res("Good"), JobStatus.Failed => Ui.Res("Bad"), _ => Ui.Res("Muted") };
            var col = Ui.Col(6,
                Ui.Text(job.Title, "h3"),
                Ui.Text($"{job.GameName} · {job.Step}", "small", color: color));
            if (job.Status == JobStatus.Running) col.Children.Add(bar);
            if (job.Error is not null) col.Children.Add(Ui.Text(job.Error, "small muted", wrap: true));
            _downloadsList.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = col });
        }
    }

    /// <summary>История загрузок за всё время (как «Download history» на Nexus).</summary>
    public void ShowHistory()
    {
        _downloadsPanel.IsVisible = false;
        var list = new StackPanel { Spacing = 6 };
        var items = Features.History.All();
        if (items.Count == 0) list.Children.Add(Ui.Text(I18n.T("nx.history.empty"), "muted"));
        foreach (var h in items.Take(150))
        {
            var row = new DockPanel();
            var when = Ui.Text(h.At.ToLocalTime().ToString("dd.MM HH:mm"), "small muted");
            DockPanel.SetDock(when, Dock.Right);
            row.Children.Add(when);
            row.Children.Add(Ui.Row(8, Ui.Dot(h.Ok ? Ui.Res("Good") : Ui.Res("Bad")), Ui.Text($"{h.Title} · {h.Game}", "small")));
            list.Children.Add(row);
        }
        Dialog(I18n.T("nx.history"), new ScrollViewer { Content = list, MaxHeight = 460 },
            Ui.Button(I18n.T("nx.history.clear"), () => { Features.History.Clear(); CloseDialog(); }, "ghost", Icons.Trash),
            Ui.Button(I18n.T("common.close"), CloseDialog, "primary"));
    }

    void OnJobFinished(Job job)
    {
        try { Features.History.Add(job); } catch { }
        if (job.Status == JobStatus.Done && Settings.Data.Bool("toastOnDone", true)) Toast($"{job.Title} — {I18n.T("dl.done").ToLowerInvariant()}");
        else if (job.Status == JobStatus.Failed) Toast($"{job.Title}: {job.Error}", bad: true);
        foreach (var g in AppState.Games) if (g.Path is not null) g.Refresh();
        AppState.Notify();
    }

    public void Toast(string text, bool bad = false)
    {
        var t = new Border
        {
            Background = Ui.Res("Surface3"),
            BorderBrush = bad ? Ui.Res("Bad") : Ui.Res("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12),
            MaxWidth = 420,
            BoxShadow = BoxShadows.Parse("0 10 30 0 #66000000"),
            Child = Ui.Text(text, wrap: true),
        };
        _toasts.Children.Add(t);
        Animate.ToastIn(t);
        var seconds = Settings.Data.Long("toastSeconds") is var ts && ts > 0 ? ts : 4;
        DispatcherTimer.RunOnce(() => Animate.ToastOut(t, () => _toasts.Children.Remove(t)), TimeSpan.FromSeconds(bad ? Math.Max(8, seconds) : seconds));
    }

    // ---------------------------------------------------------------- окна поверх

    public void Dialog(string title, Control body, params Control[] actions)
    {
        var buttons = Ui.Row(10, actions);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var card = new Border
        {
            Classes = { "card" },
            Width = 520,
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = BoxShadows.Parse("0 24 70 0 #99000000"),
            Child = Ui.Col(16, Ui.Text(title, "h2", wrap: true), body, buttons),
            RenderTransform = Look.Animations ? new ScaleTransform(0.96, 0.96) : null,
            Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(160) } },
        };
        var shade = new Border { Background = new SolidColorBrush(Color.Parse("#99000000")) };
        shade.PointerPressed += (_, _) => CloseDialog();
        _overlay.Children.Clear();
        _overlay.Children.Add(shade);
        _overlay.Children.Add(card);
        _overlay.IsVisible = true;
        Dispatcher.UIThread.Post(() => card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)"), DispatcherPriority.Background);
    }

    public void CloseDialog()
    {
        _overlay.IsVisible = false;
        _overlay.Children.Clear();
    }

    public Task<string?> PickFolder(string title) => Pickers.Folder(this, title);
    public Task<string?> PickFile(string title, bool json = false) => Pickers.File(this, title, json);
    public Task<string?> PickSaveFile(string title, string suggested) => Pickers.Save(this, title, suggested);
    public Task<string?> PickExe(string title) => Pickers.Exe(this, title);
}

static class Pickers
{
    public static async Task<string?> Folder(TopLevel top, string title)
    {
        var result = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> File(TopLevel top, string title, bool json)
    {
        var type = json
            ? new FilePickerFileType(I18n.T("dialog.pack")) { Patterns = ["*.json"] }
            : new FilePickerFileType(I18n.T("dialog.archives")) { Patterns = ["*.zip", "*.7z", "*.rar"] };
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = [type] });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> Exe(TopLevel top, string title)
    {
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(I18n.T("dialog.exe")) { Patterns = ["*.exe"] }],
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> Save(TopLevel top, string title, string suggested)
    {
        var result = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType(I18n.T("dialog.pack")) { Patterns = ["*.json"] }],
        });
        return result?.TryGetLocalPath();
    }
}
