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
    /// <summary>Страница уже показана: вступительные анимации больше не играют.</summary>
    public bool Shown { get; set; }
    public abstract string Title { get; }
    public virtual string? GameId => null;
    public virtual string SearchHint => I18n.T("search.home");
    public virtual void Search(string text) { }
    /// <summary>Перестроить содержимое (язык сменился, игра нашлась…).</summary>
    public abstract void Build();
    /// <summary>«Хлебные крошки» в шапке: Subnautica › Каталог модов.</summary>
    public virtual IEnumerable<(string Text, Action? Open)> Crumbs => [(Title, null)];
    /// <summary>Правая панель со сведениями о том, что на экране (как в ModLaunch 3). null — панели нет.</summary>
    public virtual Control? Aside() => null;
    /// <summary>Тот же экран, что и предыдущий (другая вкладка той же игры): без въезда всей страницы.</summary>
    public virtual bool SameScreenAs(Page? previous) => false;
    /// <summary>Цвет свечения фона: у страниц игры — цвет игры.</summary>
    public virtual string? Accent => GameId is string id ? AppState.Game(id).Def.Accent : null;
}

/// <summary>
/// Окно со своей рамкой: слева — главная, игры и настройки, сверху — навигация,
/// поиск, загрузки и кнопки окна. Страница меняется в середине.
/// </summary>
public sealed partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }

    readonly ContentControl _page = new() { Name = "Page" };
    readonly StackPanel _railGames = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
    readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    readonly ContentControl _aside = new();
    readonly Border _asideHost = new() { Width = 304, BorderThickness = new Thickness(1, 0, 0, 0) };
    readonly Border _glow = new() { IsHitTestVisible = false };
    readonly Backdrop _backdrop = new();
    Button? _asideToggle;
    FriendsDock? _friendsDock;
    readonly TextBox _search = new() { Width = 340, Height = 40 };
    readonly Button _more = new() { Classes = { "icon", "ghost" } };
    readonly Avalonia.Controls.Shapes.Arc _dlRing = new()
    {
        Width = 34, Height = 34, StartAngle = -90, SweepAngle = 0, StrokeThickness = 2.5, IsVisible = false,
        StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    Border? _adWrap;
    readonly Button _back, _forward, _downloads, _settingsButton, _friendsButton, _statsButton, _donateButton, _creatorButton, _libraryButton, _modsButton, _panelButton, _homeButton;
    readonly LayoutTransformControl _scale = new();
    Control? _railHost;
    Panel? _layers;
    Border? _sheet;
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

    // 9.1: слой анимаций «Скачать», выдвижная полоска Creator Hub у левого края, заставка при запуске.
    readonly Panel _fxLayer = new() { IsVisible = false };
    Control? _root;
    CreatorDrawer? _drawer;
    Splash? _splash;
    /// <summary>Что снимает и прячет анимация загрузки: боковая панель и страница с шапкой.</summary>
    internal Control FxRoot => _root!;
    internal Control? FxRail => _railHost;
    internal Panel FxLayer => _fxLayer;
    internal Control? FxDownloads => _downloads;

    Point? _lastPress;

    /// <summary>Последнее нажатие в координатах области страницы (null — нажатия не было или оно вне страницы).</summary>
    Point? ClickInPage()
    {
        if (_lastPress is not Point p || this.TranslatePoint(p, _page) is not Point q) return null;
        return q.X >= 0 && q.Y >= 0 && q.X <= _page.Bounds.Width && q.Y <= _page.Bounds.Height ? q : null;
    }

    readonly List<Func<Page>> _history = [];
    int _index = -1;
    Page? _current;
    /// <summary>Страница на экране.</summary>
    public Page? CurrentPage => _current;

    public MainWindow()
    {
        Classes.Set("juicy", Animate.On);
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
        // 9.2 Store: разделы — подписанными пунктами на боковой панели, как в Microsoft Store.
        _homeButton = NavButton(Icons.Home, "v92.nav.home", () => Navigate(() => new HomePage()));
        _libraryButton = NavButton(Icons.Gamepad, "v92.nav.games", () => Navigate(() => new LibraryPage()));
        _modsButton = NavButton(Icons.Package, "v92.nav.mods", () => Navigate(() => new ModsCenterPage()));
        _panelButton = NavButton(Icons.Grid, "v92.nav.panel", () => Navigate(() => new ControlPanelPage()));
        _settingsButton = NavButton(Icons.Settings, "v92.nav.settings", () => Navigate(() => new SettingsPage()));
        _friendsButton = NavButton(Icons.Users, "v92.nav.friends", () => Navigate(() => new FriendsPage()));
        _statsButton = NavButton(Icons.Chart, "v92.nav.stats", () => Navigate(() => new StatsPage()));
        _donateButton = NavButton(Icons.Coffee, "v92.nav.donate", () => Navigate(() => new DonatePage()));
        // Creator Hub — в выдвижной полоске у левого края (эскиз 9.1), отдельного пункта нет.
        _creatorButton = NavButton(Icons.Creator, "Creator Hub", () => Navigate(() => new CreatorPage()));
        _friendsBadge.Width = 9; _friendsBadge.Height = 9; _friendsBadge.CornerRadius = new CornerRadius(5);
        _friendsBadge.Background = Ui.Res("Good"); _friendsBadge.HorizontalAlignment = HorizontalAlignment.Right; _friendsBadge.VerticalAlignment = VerticalAlignment.Top;
        _updatePill.Click += (_, _) => ShowUpdate();

        _downloads = new Button
        {
            Classes = { "icon", "ghost" },
            // Кольцо вокруг значка — общий прогресс всех загрузок (как в браузере).
            Content = new Panel { Children = { _dlRing, Ui.Icon(Icons.Download, 18), _dlBadge } },
        };
        _dlRing.Stroke = Ui.Res("Brand");
        _dlBadge.Classes.Add("pulse");
        _dlBadge.RenderTransform = new ScaleTransform();
        _dlBadge.Width = 10; _dlBadge.Height = 10; _dlBadge.CornerRadius = new CornerRadius(5);
        _dlBadge.Background = Ui.Res("Brand2"); _dlBadge.HorizontalAlignment = HorizontalAlignment.Right; _dlBadge.VerticalAlignment = VerticalAlignment.Top;
        _dlBadge.Margin = new Thickness(0, -4, -4, 0);
        _downloads.Click += (_, _) => { TogglePanel(_downloadsPanel!); _bellPanel!.IsVisible = false; };
        ToolTip.SetTip(_downloads, I18n.T("dl.button"));

        _bellBadge.Width = 10; _bellBadge.Height = 10; _bellBadge.CornerRadius = new CornerRadius(5);
        _bellBadge.Background = Ui.Res("Bad"); _bellBadge.HorizontalAlignment = HorizontalAlignment.Right; _bellBadge.VerticalAlignment = VerticalAlignment.Top;
        _bellBadge.Margin = new Thickness(0, -4, -4, 0);
        _bell.Content = new Panel { Children = { Ui.Icon(Icons.Bell, 18), _bellBadge } };
        _bell.Classes.Add("ghost");
        _bell.Click += (_, _) => { TogglePanel(_bellPanel!); _downloadsPanel!.IsVisible = false; RenderBell(); };
        ToolTip.SetTip(_bell, I18n.T("nx.notify"));
        _bellPanel = new Border
        {
            Classes = { "card" }, Width = 400, MaxHeight = 540, Padding = new Thickness(16), Margin = new Thickness(0, 50, 170, 0),
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
            Margin = new Thickness(0, 50, 130, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false,
            BoxShadow = BoxShadows.Parse("0 18 50 0 #80000000"),
            Child = DownloadsContent(),
        };

        InitPlay();
        InitDrop();
        _scale.Child = BuildLayout();
        Content = _scale;
        ApplyScale();
        UpdateAdVisibility();
        Look.Changed += () => { ApplyScale(); RenderRail(); _current?.Build(); RenderAside(); RenderGlow(); };
        Look.GlowChanged += () => _glow.Opacity = Look.Glow;
        _glow.Opacity = Look.Glow;

        I18n.Changed += () => { FlowDirection = I18n.IsRtl ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight; RebuildChrome(); _current?.Build(); };
        AppState.Changed += OnStateChanged;
        Jobs.Changed += _ => RenderDownloads();
        Jobs.Finished += OnJobFinished;
        KeyDown += OnKey;
        Features.Launcher.Exited += OnGameExit;
        Features.Nxm.Received += OnExternal;

        SmoothScroll.Attach(this);
        // Где нажали в последний раз — отсюда «вырастает» страница мода.
        AddHandler(PointerPressedEvent, (_, e) => _lastPress = e.GetPosition(this), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Images.Prewarm(AppState.Games.Select(g => g.Def));
        Navigate(StartPage());
        WhatsNew.MaybeShow();
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

    static Button RailIcon(string icon, Action onClick, string tip)
    {
        var b = Ui.Button("", onClick, "rail", icon, tip);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        b.Foreground = Ui.Res("Muted");
        return b;
    }

    /// <summary>Черта на боковой панели: тонкая, к краям растворяется в фон.</summary>
    static Control RailRule(Thickness margin) => new Border
    {
        Height = 1, Margin = margin, Width = 44, HorizontalAlignment = HorizontalAlignment.Center,
        // Цвет — из темы (меняется вместе с ней), края гасит маска.
        Background = Ui.Res("Line"),
        OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.25), new GradientStop(Colors.Black, 0.75), new GradientStop(Colors.Transparent, 1) },
        },
    };

    readonly List<(Panel Slot, Button Button)> _railSlots = [];

    /// <summary>Пункт боковой панели с «пилюлей» слева: растёт при наведении и у выбранного.</summary>
    Control Slot(Button b)
    {
        var slot = new Panel { Classes = { "rail-item" }, Width = 76, Children = { new Border { Classes = { "rail-pip" } }, b } };
        b.HorizontalAlignment = HorizontalAlignment.Center;
        b.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) slot.IsVisible = b.IsVisible; };
        slot.IsVisible = b.IsVisible;
        _railSlots.Add((slot, b));
        return slot;
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
        RenderCrumbs();
        RenderAside();
        RenderRail();
        RenderDownloads();
    }

    void OnStateChanged()
    {
        RenderRail();
        _current?.Build();
        RenderAside();
    }

    // ---------------------------------------------------------------- шапка, правая панель, свечение

    public void RenderCrumbs()
    {
        _crumbs.Children.Clear();
        var items = _current?.Crumbs.ToList() ?? [];
        // 9.2: название программы в шапке ведёт на главную — путь начинается сразу с раздела.
        for (var i = 0; i < items.Count; i++)
        {
            var (text, open) = items[i];
            var last = i == items.Count - 1;
            if (i > 0) _crumbs.Children.Add(Ui.Icon(Icons.Forward, 12, Ui.Res("Faint")));
            var t = new TextBlock
            {
                Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = last ? FontWeight.SemiBold : FontWeight.Medium, Foreground = last ? Ui.Res("Text") : Ui.Res("Muted"),
            };
            if (open is not null && !last)
            {
                t.Cursor = new Cursor(StandardCursorType.Hand);
                var go = open;
                t.PointerPressed += (_, _) => go();
            }
            _crumbs.Children.Add(t);
        }
        // Store: как в Microsoft Store — в шапке только название программы, путь показывают сами страницы.
        _crumbs.IsVisible = !Look.Store;
        if (_crumbSep is not null) _crumbSep.IsVisible = _crumbs.IsVisible && _crumbs.Children.Count > 0;
    }

    /// <summary>Перестроить правую панель (страница вызывает, когда догрузила данные).</summary>
    public void RenderAside()
    {
        Control? content = null;
        try { content = _current?.Aside(); } catch { }
        _aside.Content = content;
        _asideToggle?.Classes.Set("active", Settings.Data.Bool("asideOpen", false));
        if (_asideToggle is not null) _asideToggle.IsVisible = content is not null;
        UpdateAsideVisibility();
    }

    void UpdateAsideVisibility() =>
        _asideHost.IsVisible = _aside.Content is not null && Settings.Data.Bool("asideOpen", false) && Bounds.Width / Math.Max(0.5, _appliedScale) >= 1180;

    void RenderGlow()
    {
        var accent = Color.Parse(_current?.Accent ?? Look.Accent);
        var bg = Ui.Res("Bg") is SolidColorBrush b ? b.Color : Colors.Black;
        if (_glow.Child is not Backdrop) _glow.Child = _backdrop;
        _backdrop.Set(accent, bg, Look.IsLight, Look.Studio ? "studio" : Look.Backdrop);
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
        Classes.Set("juicy", Animate.On);
        Settings.Save();
        ApplyScale();
        RenderRail();
    }

    double _appliedScale = 1;
    public double Scale => _appliedScale;

    /// <summary>Масштаб: выбранный вручную или «авто» — от ширины окна (1500 пикселей и больше — крупнее, до 140%).</summary>
    double EffectiveScale()
    {
        if (!Look.AutoScale) return Look.Scale;
        var w = ClientSize.Width;
        if (w <= 0) return 1;
        return Math.Round(Math.Clamp(w / 1500.0, 1.0, 1.4) * 20) / 20; // шаг 5%, чтобы не пересчитывать раскладку на каждый пиксель
    }

    void ApplyScale()
    {
        _layers?.Classes.Set("anim", Look.Animations);
        var k = EffectiveScale();
        _appliedScale = k;
        _scale.LayoutTransform = Math.Abs(k - 1) < 0.001 ? null : new ScaleTransform(k, k);
        if (_railHost is not null) _railHost.IsVisible = !Settings.Data.Bool("railHidden");
        // Без боковой панели «лист» с содержимым — без скруглённого угла.
        if (_sheet is not null)
        {
            var rail = _railHost?.IsVisible == true;
            _sheet.CornerRadius = rail ? new CornerRadius(8, 0, 0, 0) : new CornerRadius(0);
            _sheet.BorderThickness = rail ? new Thickness(1, 1, 0, 0) : new Thickness(0, 1, 0, 0);
        }
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
            item.Click += (_, _) => Guard.Later(() => Actions.Play(gs));
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
            ("Creator Hub", Icons.Creator, () => Navigate(() => new CreatorPage())),
            (I18n.T("add.title"), Icons.Plus, () => Navigate(() => new AddGamePage())),
            (I18n.T("nav.settings"), Icons.Settings, () => Navigate(() => new SettingsPage())),
            (I18n.T("look.title"), Icons.Palette, () => Navigate(() => new SettingsPage("look"))),
            (I18n.T("friends.title"), Icons.Users, () => Navigate(() => new FriendsPage())),
            (I18n.T("acc.page"), Icons.User, () => Navigate(() => new AccountPage())),
            (I18n.T("mk.tab"), Icons.Bag, () => Navigate(() => new CreatorPage("market"))),
            ("Minecraft", Icons.Cube, () => Navigate(() => new MinecraftPage())),
            ("Minecraft · " + I18n.T("mine.tab.catalog"), Icons.Bag, () => Navigate(() => new MinecraftPage("catalog"))),
            ("Minecraft · " + I18n.T("mine.build.new"), Icons.Plus, () => { Navigate(() => new MinecraftPage("builds")); MinecraftPage.CreateDialog(); }),
            (I18n.T("cp.title"), Icons.Grid, () => Navigate(() => new ControlPanelPage())),
            (I18n.T("new.title"), Icons.Sparkles, WhatsNew.Show),
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
            ("Ctrl+,", "keys.settings"), ("Ctrl+Shift+P", "cp.title"), ("Ctrl+L", "lib.title"), ("Ctrl+U", "mc.title"), ("Ctrl+Shift+M", "keys.minecraft"),
            ("Ctrl+Shift+C", "keys.creator"), ("Ctrl+I", "keys.aside"), ("F11", "bp.open"), ("Alt+← / Alt+→", "keys.history"), ("F1", "keys.help"), ("Esc", "keys.close"),
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
        CloseAccountPanel();
        _drawer?.Close();
        if (_index < _history.Count - 1) _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        _history.Add(make);
        _index = _history.Count - 1;
        Show(make());
    }

    void GoBack() { if (_index > 0) Show(_history[--_index]()); }
    void GoForward() { if (_index < _history.Count - 1) Show(_history[++_index]()); }

    void Show(Page page)
    {
        // У Minecraft своя страница (сборки, версии, загрузчики) — любые ссылки на «игру» ведут туда.
        if (page is GamePage gp && gp.GameId == Minecraft.Mc.Id) page = new MinecraftPage(gp.Tab, gp.Query);
        var same = page.SameScreenAs(_current);
        if (same) page.Classes.Add("quiet");
        _current = page;
        if (page.GameId is string gid) { Settings.Data["lastGame"] = gid; Settings.Save(); }
        page.Build();
        page.Shown = true;
        _page.Content = page;
        RenderCrumbs();
        RenderAside();
        RenderGlow();
        // 9.2: страница мода появляется одной из трёх анимаций (из места нажатия, шторкой или каскадом).
        if (!same && page is ModPage && _current is not null) ModOpenFx.Play(page, _page, ClickInPage());
        else if (!same) Animate.PageIn(page);
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
        var center = Ui.Button(I18n.T("mc.open"), () => { _bellPanel.IsVisible = false; Navigate(() => new ModsCenterPage()); }, "ghost", Icons.Package);
        _bellList.Children.Add(Ui.Row(6, clear, center));
    }

    public void RefreshFriendsDock() => _friendsDock?.Render();

    /// <summary>Выйти из программы совсем (не в трей).</summary>
    public void Quit() { _quitting = true; Close(); }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left) GoBack();
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Right) GoForward();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.K) { _search.Focus(); _search.SelectAll(); }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.J) TogglePanel(_downloadsPanel);
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.P) CommandPalette();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.B) ToggleRail();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.I && _aside.Content is not null) ToggleAside();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.OemComma) Navigate(() => new SettingsPage());
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.L) Navigate(() => new LibraryPage());
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.U) Navigate(() => new ModsCenterPage());
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.C) Navigate(() => new CreatorPage());
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.P) Navigate(() => new ControlPanelPage());
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.M) Navigate(() => new MinecraftPage());
        else if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None) _current?.Build();
        else if (e.Key == Key.F1) Shortcuts();
        else if (e.Key == Key.F11) BigPictureWindow.Open();
        else if (e.Key == Key.Escape)
        {
            if (InstallFx.Active) InstallFx.Skip();
            else if (_drawer?.IsOpen == true) _drawer.Close();
            else if (_overlay.IsVisible) CloseDialog();
            else if (_launchLayer.IsVisible) (_launchLayer.Children.FirstOrDefault() as LaunchScreen)?.Close();
            else { _downloadsPanel.IsVisible = false; CloseAccountPanel(); }
        }
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
        // Программа закрылась, пока шла игра «без модов», — возвращаем загрузчик на место.
        try { Features.Vanilla.RestoreLeftovers(); } catch { }
        _splash?.Step(I18n.T("v91.splash.games"), 0.45);
        try { await AppState.DetectAll(); }
        finally { _splash?.Finish(() => _splash = null); }
        _ = Task.Run(async () =>
        {
            try { await Features.Tracking.Check(); } catch { }
            try { await Creator.Hub.All(); } catch { }
            Dispatcher.UIThread.Post(RenderBell);
        });
        if (Program.StartupLink is string link) OnExternal(link);
        if (Features.ModUpdates.OnStart)
        {
            foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found && g.ModCount > 0))
            {
                try { await Features.ModUpdates.Check(g); } catch { }
            }
            AppState.Notify();
            Actions.AutoUpdate();
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
        // Закрылась сама и быстро — возможно, вылет: смотрим лог.
        if (Features.Launcher.LastExit(gameId) is { ByUser: false } exit && exit.Ms < 90_000 && Settings.Data.Bool("crashHelper", true))
            _ = CheckCrash(game, exit);
    }

    /// <summary>Второй запуск программы передал нам ссылку nxm:// (или просто просит показаться).</summary>
    public void OnExternal(string line)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (line.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase)) Actions.InstallNxm(line);
        else if (Features.ModLink.IsAppLink(line)) OpenLink(line);
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
            var when = Ui.Text(h.At.ToLocalTime().ToString("g", I18n.Culture), "small muted");
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

    public void Dialog(string title, Control body, params Control[] actions) => Dialog(title, body, 520, actions);

    /// <summary>Окно поверх страницы нужной ширины (для списков, которым тесно в обычном).</summary>
    public void Dialog(string title, Control body, double width, params Control[] actions)
    {
        var buttons = Ui.Row(10, actions);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var card = new Border
        {
            Classes = { "card" },
            Width = width,
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = BoxShadows.Parse("0 24 70 0 #99000000"),
            // Диалог — всегда непрозрачный, даже при стеклянных карточках.
            Background = Ui.Res("Surface"),
            Child = Ui.Col(16, Ui.Text(title, "h2", wrap: true), body, buttons),
        };
        var shade = new Border { Background = new SolidColorBrush(Color.Parse("#99000000")) };
        shade.PointerPressed += (_, _) => CloseDialog();
        _overlay.Children.Clear();
        _overlay.Children.Add(shade);
        _overlay.Children.Add(card);
        _overlay.IsVisible = true;
        Animate.Pop(card);
        Animate.From(shade, "none", 220);
    }

    public void CloseDialog()
    {
        _overlay.IsVisible = false;
        _overlay.Children.Clear();
    }

    /// <summary>Для снимков: показать что-то поверх всего окна (например, заставку). Убирается <see cref="CloseDialog"/>.</summary>
    internal void Cover(Control c)
    {
        _overlay.Children.Clear();
        _overlay.Children.Add(c);
        _overlay.IsVisible = true;
    }

    /// <summary>Выдвижная полоска Creator Hub у левого края (открыть или убрать из кода — для снимков и горячих клавиш).</summary>
    internal void ShowCreatorDrawer(bool open) { if (open) _drawer?.Open(); else _drawer?.Close(); }

    public Task<string?> PickFolder(string title) => Pickers.Folder(this, title);
    public Task<string?> PickFile(string title, bool json = false) => Pickers.File(this, title, json);
    public Task<List<string>> PickModFiles(string title) => Pickers.ModFiles(this, title);
    public Task<string?> PickProfileFile(string title) => Pickers.ProfileFile(this, title);
    public Task<string?> PickSaveFile(string title, string suggested) => Pickers.Save(this, title, suggested);
    public Task<string?> PickExe(string title) => Pickers.Exe(this, title);
    public Task<string?> PickSaveAny(string title, string suggested, string ext, string label) => Pickers.SaveAny(this, title, suggested, ext, label);
    public Task<string?> PickAny(string title, string[] patterns, string label) => Pickers.Typed(this, title, patterns, label);
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

    /// <summary>Любой файл (товар маркета: модель, архив, исходник).</summary>
    public static async Task<string?> AnyFile(TopLevel top, string title)
    {
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>Файл сборки r2modman / Gale (.r2z).</summary>
    public static async Task<string?> ProfileFile(TopLevel top, string title)
    {
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("r2modman / Gale") { Patterns = ["*.r2z", "*.zip"] }],
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>Несколько модов сразу: архивы и .dll.</summary>
    public static async Task<List<string>> ModFiles(TopLevel top, string title)
    {
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(I18n.T("dialog.archives")) { Patterns = ["*.zip", "*.7z", "*.rar", "*.dll"] }],
        });
        return result.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
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

    /// <summary>Файл своего типа (.mlcfg и т.п.).</summary>
    public static async Task<string?> Typed(TopLevel top, string title, string[] patterns, string label)
    {
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = [new FilePickerFileType(label) { Patterns = patterns }] });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> SaveAny(TopLevel top, string title, string suggested, string ext, string label)
    {
        var result = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggested, DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType(label) { Patterns = ["*." + ext] }],
        });
        return result?.TryGetLocalPath();
    }

    public static async Task<string?> Save(TopLevel top, string title, string suggested)
    {
        // .r2z (сборка r2modman) — со своим фильтром, иначе Windows допишет «.json»: «Сборка.r2z.json».
        var r2z = suggested.EndsWith(".r2z", StringComparison.OrdinalIgnoreCase);
        var result = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            DefaultExtension = r2z ? "r2z" : "json",
            FileTypeChoices = [r2z ? new FilePickerFileType("r2modman / Gale") { Patterns = ["*.r2z"] } : new FilePickerFileType(I18n.T("dialog.pack")) { Patterns = ["*.json"] }],
        });
        return result?.TryGetLocalPath();
    }
}
