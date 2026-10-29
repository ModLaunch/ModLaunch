using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// Рамка окна. 2.0 (по образцу Linear и приложений Apple): боковая панель во всю высоту окна — знак ModLaunch,
/// подписанные разделы, внутри Creator Hub — его пункты вложенным списком (второй панели больше нет), ниже —
/// ваши игры строками с названием, внизу — сообщество, настройки и аккаунт. Ширина 233 (число Фибоначчи),
/// свёрнутая — 68, значками (Ctrl+B, и сама на узком окне). Шапка — только над содержимым: назад, путь, поиск,
/// загрузки, кнопки окна. Страница лежит на отдельном «листе» со скруглёнными углами и отступом от краёв.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>2.0: ширина боковой панели — 233 (число Фибоначчи) или 68 значками.</summary>
    const double SideWide = 233, SideNarrow = 68;
    const double TitleHeight = 50;
    /// <summary>Значок первого пункта — логотип ModLaunch (а не домик).</summary>
    const string LogoIcon = "@logo";

    /// <summary>Панель свёрнута до значков: выбрали сами (Ctrl+B) или окно узкое.</summary>
    bool _sideNarrow;
    double RailWidth => _sideNarrow ? SideNarrow : SideWide;
    static bool NarrowChosen => Settings.Data.Bool("railNarrow");

    /// <summary>Значок и подпись каждого раздела — чтобы перерисовать выбранный.</summary>
    readonly Dictionary<Button, (string Icon, string Label)> _nav = [];
    Border? _crumbSep;
    Border? _railGamesHost;
    Border? _sideHost;
    Control? _brandBar;
    readonly StackPanel _navList = new() { Spacing = 2 };
    readonly StackPanel _hubList = new() { Spacing = 1 };
    readonly StackPanel _sideFoot = new() { Spacing = 8 };
    TextBlock? _gamesCaption;
    Control? _gamesHead;

    /// <summary>
    /// Сколько игр показать на панели: столько, сколько влезает по высоте целиком. Остальные — в библиотеке;
    /// панель не превращается в обрезанную ленту.
    /// </summary>
    void FitRailGames(double height)
    {
        if (height <= 0) return;
        double item = _sideNarrow ? 52 : 34, gap = _sideNarrow ? 7 : 2, head = _sideNarrow ? 0 : 34;
        var slots = _railGameSlots.Select(s => (Control)s.Slot).ToList();
        var free = height - head - (_sideNarrow ? 42 + gap : 0) - 4;
        var fit = Math.Max(0, (int)Math.Floor((free + gap) / (item + gap)));
        for (var i = 0; i < slots.Count; i++) slots[i].IsVisible = i < fit;
    }
    /// <summary>Второстепенные пункты (сообщество, статистика, настройки) — значками в нижнем ряду.</summary>
    readonly HashSet<Button> _secondary = [];
    /// <summary>Слой над страницей для анимаций открытия игры (9.3): снимок старой страницы, вспышки, рамки.</summary>
    readonly Panel _pageFx = new() { IsHitTestVisible = false, IsVisible = false, ClipToBounds = true };
    /// <summary>Слой под страницей: снимок прежнего экрана, который новая страница «прорезает».</summary>
    readonly Panel _pageUnder = new() { IsHitTestVisible = false, IsVisible = false, ClipToBounds = true };
    internal Panel PageFx => _pageFx;
    internal Panel PageUnder => _pageUnder;
    internal Control PageHost => _page;

    Button NavButton(string icon, string labelKey, Action onClick)
    {
        var b = new Button { Classes = { "sidenav" } };
        b.Click += (_, _) => onClick();
        _nav[b] = (icon, labelKey);
        b.Content = NavContent(icon, Label(labelKey), false, null, false);
        return b;
    }

    static string Label(string key) => key.Contains('.') ? I18n.T(key) : key;

    /// <summary>Значок раздела: логотип ModLaunch или контурный значок; у выбранного — цветом акцента.</summary>
    static Control Glyph(string icon, bool active, double size)
    {
        if (icon != LogoIcon) return Ui.Icon(icon, size, active ? Ui.Res("Brand2") : Ui.Res("Muted"));
        return new Image { Source = Images.Asset("icon.png", 64), Width = size + 2, Height = size + 2, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>
    /// Пункт панели: широкая — значок 18 и подпись 14 в строке высотой 34; свёрнутая — один значок по центру.
    /// Выбранный — подложкой и значком акцента (стиль sidenav в Look.AddPhi), без свечения.
    /// </summary>
    static Control NavContent(string icon, string label, bool active, Control? badge, bool narrow)
    {
        var glyph = Glyph(icon, active, 18);
        Control iconHost = glyph;
        if (badge is not null)
        {
            if (badge.Parent is Panel old) old.Children.Remove(badge);
            badge.Margin = new Thickness(0, -3, -4, 0);
            iconHost = new Panel { Width = 20, Height = 20, Children = { glyph, badge } };
        }
        if (narrow) { iconHost.HorizontalAlignment = HorizontalAlignment.Center; return iconHost; }
        var text = new TextBlock
        {
            Text = label, FontSize = Phi.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = active ? Ui.Res("Text") : Ui.Res("Muted"), FontWeight = active ? FontWeight.SemiBold : FontWeight.Medium,
        };
        return Ui.Row(Phi.S13, iconHost, text);
    }

    void SetNav(Button b, bool active, Control? badge = null)
    {
        if (!_nav.TryGetValue(b, out var n)) return;
        var secondary = _secondary.Contains(b);
        var narrow = _sideNarrow || secondary;
        b.Classes.Set("active", active);
        b.Height = Phi.S34;
        b.Width = secondary ? Phi.S34 : double.NaN;
        b.Padding = narrow ? new Thickness(0) : new Thickness(Phi.S8, 0);
        b.HorizontalAlignment = secondary ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        b.Content = NavContent(n.Icon, Label(n.Label), active, badge, narrow);
        ToolTip.SetTip(b, narrow ? Label(n.Label) : null);
        ToolTip.SetPlacement(b, secondary && !_sideNarrow ? PlacementMode.Top : PlacementMode.Right);
    }

    /// <summary>Свернуть или развернуть боковую панель (Ctrl+B). Выбор запоминается.</summary>
    internal void ToggleNarrow()
    {
        Settings.Data["railNarrow"] = !_sideNarrow;
        Settings.Save();
        ApplySideWidth(force: true);
    }

    /// <summary>Ширина панели: свёрнута сама на узком окне (меньше 1180 точек), иначе — как выбрали.</summary>
    void ApplySideWidth(bool force = false)
    {
        var logical = Bounds.Width / Math.Max(0.5, _appliedScale);
        var narrow = NarrowChosen || logical > 0 && logical < 1180;
        if (narrow == _sideNarrow && !force) return;
        _sideNarrow = narrow;
        if (_sideHost is not null) _sideHost.Width = RailWidth;
        _railKey = null;
        RenderRail();
        _accountPanel.Margin = new Thickness(RailWidth + 8, 0, 0, _accountPanel.Margin.Bottom);
    }

    Control BuildLayout()
    {
        // ---- боковая панель 2.0: знак, разделы (и пункты Creator Hub), игры, внизу — значки и аккаунт.
        _sideNarrow = NarrowChosen;
        var brandIcon = new Image { Source = Images.Asset("icon.png", 64), Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        var brandName = new TextBlock { Text = "ModLaunch", FontSize = 15, FontWeight = FontWeight.Bold, LetterSpacing = -0.2, VerticalAlignment = VerticalAlignment.Center };
        var brandBadge = new Border
        {
            CornerRadius = new CornerRadius(Phi.R5), Padding = new Thickness(5, 1), VerticalAlignment = VerticalAlignment.Center, Background = Ui.Res("Surface2"),
            Child = new TextBlock { Text = Http.Display, FontSize = Phi.Caption, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Gold") },
        };
        var brandRow = Ui.Row(Phi.S8 + 2, brandIcon, brandName, brandBadge);
        brandRow.VerticalAlignment = VerticalAlignment.Center;
        var brand = new Border { Height = TitleHeight, Background = Brushes.Transparent, Padding = new Thickness(Phi.S21, 0, 0, 0), Child = brandRow, Cursor = new Cursor(StandardCursorType.Hand) };
        // Знак ведёт на главную; за пустое место панели, как за шапку, окно можно тянуть.
        brand.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(brand).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; return; }
            var start = e.GetPosition(this);
            void Moved(object? s, PointerEventArgs m)
            {
                if (Point.Distance(m.GetPosition(this), start) < 4) return;
                brand.PointerMoved -= Moved;
                BeginMoveDrag(e);
            }
            brand.PointerMoved += Moved;
            brand.PointerReleased += (_, _) => { if (brand.IsPointerOver) brand.PointerMoved -= Moved; };
        };
        brand.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && brand.IsPointerOver) Navigate(() => new HomePage());
        };
        ToolTip.SetTip(brand, I18n.T("v92.nav.home"));
        _brandBar = brand;

        foreach (var b in new[] { _homeButton, _feedButton, _libraryButton, _creatorButton }) _navList.Children.Add(b);
        _navList.Children.Add(_hubList);
        var top = Ui.Col(0, brand, new Border { Padding = new Thickness(Phi.S13, Phi.S8, Phi.S13, 0), Child = _navList });
        DockPanel.SetDock(top, Dock.Top);

        // Низ: значки сообщества, статистики и настроек в ряд и строка аккаунта.
        foreach (var b in new[] { _friendsButton, _statsButton, _settingsButton }) _secondary.Add(b);
        _accountButton.Classes.Clear();
        _accountButton.Classes.Add("sidenav");
        _accountButton.Classes.Add("account");
        _accountButton.Height = 46;
        ToolTip.SetPlacement(_accountButton, PlacementMode.Right);
        var foot = new Border { Padding = new Thickness(Phi.S13, Phi.S8, Phi.S13, Phi.S13), Child = _sideFoot };
        DockPanel.SetDock(foot, Dock.Bottom);

        // Игры: подпись «Игры» и «+», под ней — строки с обложкой и названием.
        _railGames.VerticalAlignment = VerticalAlignment.Top;
        _railGames.Spacing = 2;
        _railGamesHost = new Border { ClipToBounds = true, Child = _railGames, Padding = new Thickness(Phi.S13, Phi.S13, Phi.S13, 0) };
        _railGamesHost.SizeChanged += (_, e) => FitRailGames(e.NewSize.Height);

        var rail = new DockPanel { LastChildFill = true };
        rail.Children.Add(top);
        rail.Children.Add(foot);
        rail.Children.Add(_railGamesHost);
        var railBorder = new Border { Child = rail, Background = Ui.Res("Rail"), Width = RailWidth, ClipToBounds = true };
        _sideHost = railBorder;
        _railHost = railBorder;
        railBorder.IsVisible = !Settings.Data.Bool("railHidden");

        // ---- шапка над содержимым: назад/вперёд и путь слева, поиск по центру, инструменты справа
        _search.Watermark = I18n.T("search.home");
        _search.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Search, 15, Ui.Res("Muted")) };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            // Вставили ссылку на мод — сразу его страница.
            if (Features.ModLink.Parse(_search.Text ?? "") is not null) { OpenLink(_search.Text!); return; }
            _current?.Search(_search.Text ?? "");
        };
        _search.TextChanged += (_, _) => OnSearchTyping();
        _search.InnerRightContent = new Border
        {
            Margin = new Thickness(0, 0, 8, 0), Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(Phi.R5), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Ctrl K", FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Muted") },
        };
        _search.Width = double.NaN;
        _search.MaxWidth = 560;
        _search.MinWidth = 220;
        _search.Height = Phi.S34;
        _search.MinHeight = Phi.S34;
        _search.Padding = new Thickness(10, 5);
        _search.Classes.Add("top-search");
        _search.Classes.Add("store-search");
        _search.VerticalAlignment = VerticalAlignment.Center;
        _search.HorizontalAlignment = HorizontalAlignment.Stretch;

        _back.Width = _back.Height = _forward.Width = _forward.Height = 32;
        _back.CornerRadius = _forward.CornerRadius = new CornerRadius(Phi.R8);
        var navPair = Ui.Row(0, _back, _forward);
        navPair.VerticalAlignment = VerticalAlignment.Center;
        // Знак в шапке — только когда боковая панель спрятана (иначе он на панели).
        var appIcon = new Image { Source = Images.Asset("icon.png", 48), Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var topBrand = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Row(9, appIcon, new TextBlock { Text = "ModLaunch", FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }),
            IsVisible = Settings.Data.Bool("railHidden"),
        };
        topBrand.PointerPressed += (_, e) => { if (e.GetCurrentPoint(topBrand).Properties.IsLeftButtonPressed) Navigate(() => new HomePage()); };
        _topBrand = topBrand;
        var crumbSep = new Border { Width = 1, Height = 16, Background = Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), IsVisible = false };
        _crumbSep = crumbSep;
        var left = Ui.Row(10, navPair, topBrand, crumbSep, _crumbs);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(Phi.S8, 0, 12, 0);
        var leftHost = new Border { Child = left, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };

        _more.Content = Ui.Icon(Icons.More, 20);
        ToolTip.SetTip(_more, I18n.T("top.more"));
        _more.Click += (_, _) => MoreMenu().ShowAt(_more, true);
        _asideToggle = new Button { Classes = { "icon" }, Content = Ui.Icon(Icons.Sidebar, 18) };
        _asideToggle.Click += (_, _) => ToggleAside();
        // 14.0: в шапке рекламы нет — только колонка справа (_adRail).
        _adWrap = new Border { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        foreach (var b in new[] { _bell, _downloads, _more }) { b.Width = b.Height = Phi.S34; b.CornerRadius = new CornerRadius(Phi.R8); }
        var tools = Ui.Row(4, _updatePill, _adWrap, _bell, _downloads, _more);
        tools.VerticalAlignment = VerticalAlignment.Center;
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        tools.Margin = new Thickness(12, 0, 8, 0);

        // 9.5: кнопки окна как у Windows — во всю высоту полосы, вплотную к углу (мышь можно «бросить» в угол).
        _maxButton = WinButton(Icons.Maximize, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, I18n.T("win.maximize"));
        var winButtons = Ui.Row(0,
            WinButton(Icons.Minimize, () => WindowState = WindowState.Minimized, I18n.T("win.minimize")),
            _maxButton,
            WinButton(Icons.Close, Close, I18n.T("win.close"), "close"));
        winButtons.VerticalAlignment = VerticalAlignment.Top;
        _winButtons = winButtons;

        // Поиск — по центру между левой и правой частью.
        var titlebar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto"), Height = TitleHeight, Background = Brushes.Transparent };
        titlebar.Children.Add(leftHost);
        var searchHost = new Border { Width = 480, Child = _search, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(searchHost, 1);
        titlebar.Children.Add(searchHost);
        var toolsHost = new Border { Child = tools, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(toolsHost, 2);
        titlebar.Children.Add(toolsHost);
        Grid.SetColumn(winButtons, 3);
        titlebar.Children.Add(winButtons);
        // Узкое окно: поиск сжимается, но не меньше 220.
        titlebar.SizeChanged += (_, _) => searchHost.Width = Math.Clamp(titlebar.Bounds.Width - 2 * 300, 220, 480);
        titlebar.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is null && v.FindAncestorOfType<TextBox>(true) is null)
            {
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
            }
        };

        // ---- «лист» со страницей и правой панелью сведений: отдельный слой с углами 13 и отступом 8 от краёв окна
        var main = new DockPanel();
        _asideHost.BorderBrush = Ui.Res("Line");
        _asideHost.Background = Ui.Res("Layer");
        _asideHost.Child = new ScrollViewer { Content = new Border { Padding = new Thickness(20, 22, 20, 22), Child = _aside }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(_asideHost, Dock.Right);
        main.Children.Add(_asideHost);
        // 14.0: место под рекламу — узкая колонка у правого края, левее панели сведений.
        _adRail.Child = AdSlot.Side(() => { AdSlot.SideClosed = true; UpdateAdVisibility(); });
        _adRail.Padding = new Thickness(0, 18, 16, 18);
        DockPanel.SetDock(_adRail, Dock.Right);
        main.Children.Add(_adRail);
        main.Children.Add(new Panel { ClipToBounds = true, Children = { _glow, _pageUnder, _page, _pageFx } });
        _sheet = new Border
        {
            Background = Ui.Res("Layer"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Phi.R13), ClipToBounds = true, Child = main, Margin = new Thickness(0, 0, Phi.S8, Phi.S8),
        };
        // Правый клик по пустому месту страницы: навигация и быстрые переходы.
        Ctx.Attach(_page, () => Ctx.Menu(
            Ctx.Item(I18n.T("ctx.back"), Icons.Back, GoBack, _index > 0, "Alt+Left"),
            Ctx.Item(I18n.T("ctx.forward"), Icons.Forward, GoForward, _index < _history.Count - 1, "Alt+Right"),
            Ctx.Item(I18n.T("ctx.reload"), Icons.Refresh, () => _current?.Build(), gesture: "F5"),
            "-",
            Ctx.Item(I18n.T("cp.title"), Icons.Grid, () => Navigate(() => new SettingsPage("panel")), gesture: "Ctrl+Shift+P"),
            Ctx.Item(I18n.T("nav.menu"), Icons.Home, () => Navigate(() => new HomePage())),
            Ctx.Item("Creator Hub", Icons.Creator, () => Navigate(() => new CreatorPage()), gesture: "Ctrl+Shift+C"),
            Ctx.Item("Minecraft", Icons.Cube, () => Navigate(() => new MinecraftPage()), gesture: "Ctrl+Shift+M"),
            Ctx.Item(I18n.T("acc.page"), Icons.User, () => Navigate(() => new AccountPage())),
            "-",
            Ctx.Item(I18n.T("look.title"), Icons.Palette, () => Navigate(() => new SettingsPage("look"))),
            Ctx.Item(I18n.T("nav.settings"), Icons.Settings, () => Navigate(() => new SettingsPage()), gesture: "Ctrl+OemComma")));
        SizeChanged += (_, _) =>
        {
            if (Look.AutoScale && Math.Abs(EffectiveScale() - _appliedScale) > 0.001) ApplyScale();
            ApplySideWidth();
            UpdateAsideVisibility();
            UpdateAdVisibility();
        };

        var root = new Grid { RowDefinitions = new RowDefinitions($"{TitleHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)},*,Auto"), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        // 11.0: строка состояния — сеть, инжектор, бэкап.
        var status = _statusBar = new StatusBar();
        Grid.SetRow(status, 2);
        Grid.SetColumnSpan(status, 2);
        root.Children.Add(status);
        // Панель — во всю высоту над строкой состояния; шапка — только над листом.
        Grid.SetRowSpan(railBorder, 2);
        root.Children.Add(railBorder);
        Grid.SetColumn(titlebar, 1);
        root.Children.Add(titlebar);
        Grid.SetRow(_sheet, 1);
        Grid.SetColumn(_sheet, 1);
        root.Children.Add(_sheet);
        _root = root;

        // Меню аккаунта — рядом со строкой аккаунта внизу боковой панели.
        _accountPanel.HorizontalAlignment = HorizontalAlignment.Left;
        _accountPanel.VerticalAlignment = VerticalAlignment.Bottom;

        var layers = new Panel();
        _layers = layers;
        layers.Classes.Set("anim", Look.Animations);
        layers.Children.Add(root);
        layers.Children.Add(_downloadsPanel);
        layers.Children.Add(_bellPanel);
        layers.Children.Add(_accountPanel);
        var dock = new FriendsDock();
        _friendsDock = dock;
        layers.Children.Add(dock);
        // 11.0: низ окна занимает строка состояния — друзья, уведомления и меню аккаунта встают над ней.
        void AboveStatus()
        {
            var h = status.IsVisible ? status.Height : 0;
            _accountPanel.Margin = new Thickness(RailWidth + 8, 0, 0, 12 + h);
            _toasts.Margin = new Thickness(0, 0, 24, 24 + h);
            dock.Margin = new Thickness(0, 0, 18, 16 + h);
        }
        AboveStatus();
        status.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) AboveStatus(); };
        // Анимация «Скачать» — поверх страницы и панелей, но под экраном запуска и окнами.
        layers.Children.Add(_fxLayer);
        layers.Children.Add(_launchLayer);
        layers.Children.Add(_toasts);
        layers.Children.Add(_overlay);
        // 2.0: сетка золотого сечения поверх окна (Ctrl+Shift+G) — проверить пропорции.
        layers.Children.Add(_phiGrid);
        // 10.0: строки ЭЛТ и виньетка из конфига — поверх всего, нажатия проходят насквозь.
        layers.Children.Add(new ScreenFx());
        if (Splash.Enabled)
        {
            _splash = new Splash();
            layers.Children.Add(_splash);
        }
        return layers;
    }

    Control? _topBrand;
    string? _railKey;
    readonly List<(string Id, Control Slot, Button Button)> _railGameSlots = [];
    Button? _railPlus;

    /// <summary>Строка игры на широкой панели: обложка 22×30, название, справа — точка состояния.</summary>
    Button GameRow(GameState g, bool running, bool ready, bool fresh)
    {
        var art = new Border { Width = 22, Height = 30, CornerRadius = new CornerRadius(Phi.R5), ClipToBounds = true, Child = Ui.GameImage(g.Def, 90, art: Images.Art.Cover) };
        var name = new TextBlock { Text = g.Def.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new DockPanel { LastChildFill = true };
        if (running || !ready || fresh)
        {
            var dot = Ui.Dot(running ? Ui.Res("Good") : !ready ? Ui.Res("Warn") : Ui.Res("Brand"), 7);
            if (running) dot.Classes.Add("pulse");
            dot.Margin = new Thickness(6, 0, 2, 0);
            DockPanel.SetDock(dot, Dock.Right);
            row.Children.Add(dot);
        }
        art.Margin = new Thickness(0, 0, Phi.S13 - 2, 0);
        DockPanel.SetDock(art, Dock.Left);
        row.Children.Add(art);
        row.Children.Add(name);
        var b = new Button { Classes = { "sidenav", "side-game" }, Height = Phi.S34, Padding = new Thickness(6, 0, Phi.S8, 0), Content = row };
        return b;
    }

    /// <summary>Обложка игры на свёрнутой панели (как прежде): 34×46 с точкой состояния в углу.</summary>
    Button GameCover(GameState g, bool running, bool ready, bool fresh)
    {
        var art = new Border { Width = 34, Height = 46, CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = Ui.GameImage(g.Def, 120, art: Images.Art.Cover) };
        var dot = new Border
        {
            Width = running ? 12 : 10, Height = running ? 12 : 10, CornerRadius = new CornerRadius(6), Background = running ? Ui.Res("Good") : !ready ? Ui.Res("Warn") : Ui.Res("Brand"),
            BorderBrush = Ui.Res("Rail"), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, -3, -3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsVisible = running || !ready || fresh,
        };
        if (running) dot.Classes.Add("pulse");
        var b = new Button { Classes = { "rail", "rail-game" }, Width = 40, Height = 52, CornerRadius = new CornerRadius(9), Content = new Panel { Children = { art, dot } } };
        b.ClipToBounds = false;
        b.HorizontalAlignment = HorizontalAlignment.Center;
        return b;
    }

    void RenderRail()
    {
        var games = RailGames().ToList();
        // 9.5: игры на панели перестраиваются, только когда изменился их список или состояние
        // (запущена, нужен загрузчик), тема, язык или ширина панели. При переходе между страницами — только подсветка.
        var key = string.Join("|", games.Select(g => $"{g.Def.Id}:{g.Def.Name}:{Features.Launcher.IsRunning(g.Def.Id)}:{g.LoaderInstalled}:{Features.NewGames.IsNew(g)}")) + $"#{Look.Theme}{Look.Accent}{I18n.Lang}{_sideNarrow}";
        if (key == _railKey && _railPlus is not null)
        {
            foreach (var (id, _, button) in _railGameSlots)
                button.Classes.Set("active", _current?.GameId == id || _sideNarrow && Features.Launcher.IsRunning(id));
            _railPlus.Classes.Set("active", _current is AddGamePage);
        }
        else
        {
            _railKey = key;
            var before = _railGameSlots.Count > 0 ? _railGameSlots.Select(x => x.Id).ToHashSet() : null;
            _railGameSlots.Clear();
            _railGames.Children.Clear();
            _railGames.Spacing = _sideNarrow ? 7 : 2;
            var plus = new Button { Classes = { _sideNarrow ? "rail" : "sidenav" }, Content = Ui.Icon(Icons.Plus, _sideNarrow ? 18 : 15), Padding = new Thickness(0) };
            plus.Click += (_, _) => Navigate(() => new AddGamePage());
            ToolTip.SetTip(plus, I18n.T("add.title"));
            ToolTip.SetPlacement(plus, PlacementMode.Right);
            plus.Classes.Set("active", _current is AddGamePage);
            plus.HorizontalContentAlignment = HorizontalAlignment.Center;
            plus.VerticalContentAlignment = VerticalAlignment.Center;
            if (!_sideNarrow)
            {
                // Подпись «Игры» и «+» справа — одной строкой.
                plus.Width = plus.Height = 26;
                plus.Foreground = Ui.Res("Muted");
                _gamesCaption = new TextBlock { Text = I18n.T("v93.rail.games"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center };
                var head = new DockPanel { Height = 32, Margin = new Thickness(Phi.S8, 0, 2, 0) };
                DockPanel.SetDock(plus, Dock.Right);
                head.Children.Add(plus);
                head.Children.Add(_gamesCaption);
                _gamesHead = head;
                _railGames.Children.Add(head);
            }
            foreach (var g in games)
            {
                var id = g.Def.Id;
                var running = Features.Launcher.IsRunning(id);
                // 9.3: точка — только когда есть что сказать: игра запущена (зелёная, мигает) или нужен загрузчик (жёлтая).
                var ready = g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft;
                // 15.1: новая игра — точка цвета акцента, пока в неё не сыграли.
                var fresh = Features.NewGames.IsNew(g);
                var b = _sideNarrow ? GameCover(g, running, ready, fresh) : GameRow(g, running, ready, fresh);
                if (_current?.GameId == id || _sideNarrow && running) b.Classes.Add("active");
                b.Click += (_, _) => Navigate(() => new GamePage(id));
                b.ContextFlyout = GameCard.Menu(g);
                ToolTip.SetTip(b, running ? $"{g.Def.Name} · {I18n.T("run.running")}" : fresh ? $"{g.Def.Name} · {I18n.T("v151.new")}" : _sideNarrow ? g.Def.Name : null);
                ToolTip.SetPlacement(b, PlacementMode.Right);
                _railGames.Children.Add(b);
                _railGameSlots.Add((id, b, b));
                // Игра только что появилась на панели — вырастает на своём месте.
                if (before is not null && !before.Contains(id)) Animate.From(b, _sideNarrow ? "scale(0.4)" : "translateX(-12px)", 460, 80, new Avalonia.Animation.Easings.QuinticEaseOut());
            }
            if (_sideNarrow)
            {
                plus.Width = plus.Height = 34;
                plus.CornerRadius = new CornerRadius(9);
                plus.HorizontalAlignment = HorizontalAlignment.Center;
                plus.Margin = new Thickness(0, 2, 0, 6);
                plus.Foreground = Ui.Res("Muted");
                _railGames.Children.Add(plus);
            }
            _railPlus = plus;
            if (_railGamesHost is { } host)
            {
                host.Padding = _sideNarrow ? new Thickness(0, Phi.S13, 0, 0) : new Thickness(Phi.S13, Phi.S8, Phi.S13, 0);
                FitRailGames(host.Bounds.Height);
            }
            RenderSideChrome();
        }

        // Разделы: выбранный — подложкой и значком акцента.
        SetNav(_homeButton, _current is HomePage or SearchPage);
        SetNav(_feedButton, _current is AiBuildsPage);
        _libraryButton.IsVisible = true;
        SetNav(_libraryButton, _current is LibraryPage or AddGamePage or GamePage or MinecraftPage or ModsCenterPage || _current is ModPage);
        var hub = _current is MarketPage or CreatorPage or XStudio or ListingPage or AuthorPage;
        SetNav(_creatorButton, hub);
        RenderHubItems(hub);

        var view = Social.Friends.View();
        _friendsBadge.IsVisible = view.Incoming.Count > 0 || view.Friends.Any(f => f.State != "offline");
        _friendsBadge.Background = view.Incoming.Count > 0 ? Ui.Res("Warn") : Ui.Res("Good");
        SetNav(_friendsButton, _current is FriendsPage or CommunityPage, _friendsBadge);
        SetNav(_statsButton, _current is StatsPage);
        SetNav(_donateButton, _current is DonatePage);
        SetNav(_settingsButton, _current is SettingsPage);
        _friendsButton.IsVisible = Settings.Data.Bool("railFriends", true);
        _statsButton.IsVisible = Settings.Data.Bool("ownerStats");
        RenderAccount();
        _updatePill.IsVisible = Setup.Updater.Available;
        if (Setup.Updater.Latest is { } latest) _updatePill.Content = Ui.Row(6, Ui.Icon(Icons.Sparkles, 14), new TextBlock { Text = I18n.T("upd.app.pill", ("version", Http.DisplayOf(latest.Version))), VerticalAlignment = VerticalAlignment.Center });
    }

    /// <summary>
    /// Пункты Creator Hub — вложенным списком под «Creator Hub», пока открыт любой его экран (вторая панель
    /// внутри Hub убрана): «ModScript X» с «Создать» и «Мои проекты», затем «Рынок» и «Команда».
    /// </summary>
    void RenderHubItems(bool hub)
    {
        _hubList.Children.Clear();
        _hubList.IsVisible = hub;
        if (!hub) return;
        _hubList.Margin = new Thickness(0, 2, 0, 4);
        foreach (var item in HubNav.SideItems(_current))
        {
            var id = item.Id;
            Control content;
            if (_sideNarrow) content = Ui.Icon(item.Icon, 15, item.On ? Ui.Res("Brand2") : Ui.Res("Faint"));
            else
            {
                var text = new TextBlock
                {
                    Text = item.Label, FontSize = item.Group ? 13 : 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = item.On ? Ui.Res("Text") : Ui.Res("Muted"), FontWeight = item.On || item.Group ? FontWeight.SemiBold : FontWeight.Normal,
                };
                Control mark = item.Group ? CreatorPage.XMark(16) : Ui.Icon(item.Icon, 15, item.On ? Ui.Res("Brand2") : Ui.Res("Faint"));
                var row = new DockPanel { LastChildFill = true };
                if (item.Count is { } n)
                {
                    var count = new TextBlock { Text = n, FontSize = 11.5, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(count, Dock.Right);
                    row.Children.Add(count);
                }
                mark.Margin = new Thickness(0, 0, Phi.S13 - 3, 0);
                DockPanel.SetDock(mark, Dock.Left);
                row.Children.Add(mark);
                row.Children.Add(text);
                content = row;
            }
            var b = new Button
            {
                Classes = { "sidenav", "side-sub" }, Content = content, Height = item.Group ? 32 : 30,
                Padding = _sideNarrow ? new Thickness(0) : new Thickness(Phi.S8 + Phi.S13 + item.Level * Phi.S21, 0, Phi.S8, 0),
                HorizontalContentAlignment = _sideNarrow ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            };
            if (_sideNarrow) { ToolTip.SetTip(b, item.Label); ToolTip.SetPlacement(b, PlacementMode.Right); }
            if (item.On) b.Classes.Add("active");
            b.Click += (_, _) => HubNav.SideGo(id);
            _hubList.Children.Add(b);
        }
    }

    /// <summary>Низ панели: значки в ряд (широкая) или столбиком (свёрнутая), под ними — аккаунт; знак — по ширине.</summary>
    void RenderSideChrome()
    {
        _sideFoot.Children.Clear();
        foreach (var b in _secondary) if (b.Parent is Panel p) p.Children.Remove(b);
        if (_accountButton.Parent is Panel ap) ap.Children.Remove(_accountButton);
        var collapse = new Button { Classes = { "sidenav" }, Width = Phi.S34, Height = Phi.S34, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Content = Ui.Icon(Icons.Sidebar, 17, Ui.Res("Muted")) };
        collapse.Click += (_, _) => ToggleNarrow();
        ToolTip.SetTip(collapse, I18n.T(_sideNarrow ? "v2.side.expand" : "v2.side.collapse") + " · Ctrl+B");
        ToolTip.SetPlacement(collapse, _sideNarrow ? PlacementMode.Right : PlacementMode.Top);
        if (_sideNarrow)
        {
            var col = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var b in _secondary) col.Children.Add(b);
            col.Children.Add(collapse);
            _sideFoot.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(Phi.S8, 0) });
            _sideFoot.Children.Add(col);
            _accountButton.Width = 44;
            _accountButton.HorizontalAlignment = HorizontalAlignment.Center;
            _accountButton.Padding = new Thickness(0);
            _accountButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        else
        {
            var icons = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(collapse, Dock.Right);
            icons.Children.Add(collapse);
            var row = Ui.Row(2);
            foreach (var b in _secondary) row.Children.Add(b);
            icons.Children.Add(row);
            _sideFoot.Children.Add(icons);
            _accountButton.Width = double.NaN;
            _accountButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            _accountButton.Padding = new Thickness(6, 0, Phi.S8, 0);
            _accountButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
        _sideFoot.Children.Add(_accountButton);
        if (_brandBar is Border brand && brand.Child is StackPanel brandRow)
        {
            foreach (var c in brandRow.Children.Skip(1)) c.IsVisible = !_sideNarrow;
            brand.Padding = _sideNarrow ? new Thickness(0) : new Thickness(Phi.S21, 0, 0, 0);
            brandRow.HorizontalAlignment = _sideNarrow ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }
        _navList.Parent?.SetValue(Border.PaddingProperty, _sideNarrow ? new Thickness(Phi.S13 - 1, Phi.S8, Phi.S13 - 1, 0) : new Thickness(Phi.S13, Phi.S8, Phi.S13, 0));
    }
}
