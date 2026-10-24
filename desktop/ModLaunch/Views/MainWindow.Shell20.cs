using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>
/// 20.0: рамка окна по эскизам. Слева — боковая панель на всю высоту окна: логотип, разделы с подписями
/// (Главная, Каталог модов, ИИ-сборки, Библиотека), список «Мои игры» с обложкой, названием и состоянием,
/// внизу — Creator Hub, Сообщество и Настройки. Панель сворачивается до значков (кнопкой, на узком окне
/// и в рабочих разделах — Creator Hub и редакторе). Сверху над страницей — шапка: назад и вперёд, поиск,
/// точка состояния «Всё работает», уведомления, загрузки, меню «⋯» и профиль. Строки состояния внизу
/// и колонки рекламы справа по умолчанию нет.
/// </summary>
public sealed partial class MainWindow
{
    const double TitleHeight = 60;
    const double SideWide = 248, SideNarrow = 72;

    readonly StackPanel _sideNav = new() { Spacing = 2 };
    readonly StackPanel _sideBottom = new() { Spacing = 2 };
    readonly StackPanel _sideGames = new() { Spacing = 2 };
    Border? _side;
    Grid? _sideHead;
    Control? _sideGamesHead;
    Button? _sideToggle;
    StatusPill? _statusPill;
    bool _sideIsCollapsed;
    string? _sideGamesKey;
    readonly List<(string Id, Button Row)> _sideGameRows = [];
    readonly Button _sideAdd = new() { Classes = { "icon", "ghost" } };

    /// <summary>Свёрнута ли боковая панель: выбрано вручную, рабочий раздел или узкое окно.</summary>
    bool SideCollapsed
    {
        get
        {
            if (Settings.Data.Bool("sideCollapsed") || _current is { CompactRail: true }) return true;
            var w = Bounds.Width / Math.Max(0.5, _appliedScale);
            return w > 0 && w < 1100;
        }
    }

    /// <summary>«Каталог модов»: каталог последней игры (или первой найденной с каталогом).</summary>
    void OpenCatalog()
    {
        var games = AppState.Games.Where(g => g.Status == Detect.Found && g.Def.HasCatalog).ToList();
        var last = Settings.Data.Str("lastGame");
        var pick = games.FirstOrDefault(g => g.Def.Id == last)
            ?? games.OrderByDescending(g => Features.NewGames.Recent(g)).FirstOrDefault();
        if (pick is null) { Navigate(() => new LibraryPage()); return; }
        var id = pick.Def.Id;
        Navigate(() => new GamePage(id, "catalog"));
    }

    Control BuildLayout()
    {
        // ---- боковая панель
        _sideHead = new Grid { Height = TitleHeight, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Brushes.Transparent };
        _sideHead.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null) return;
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        _sideToggle = new Button { Classes = { "icon", "ghost" }, Width = 30, Height = 30, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
        _sideToggle.Click += (_, _) =>
        {
            Settings.Data["sideCollapsed"] = !Settings.Data.Bool("sideCollapsed");
            Settings.Save();
            RenderRail();
        };
        _sideAdd.Width = _sideAdd.Height = 28;
        _sideAdd.CornerRadius = new CornerRadius(8);
        _sideAdd.Content = Ui.Icon(Icons.Plus, 16, Ui.Res("Muted"));
        _sideAdd.Click += (_, _) => Navigate(() => new AddGamePage());
        ToolTip.SetTip(_sideAdd, I18n.T("add.title"));

        // Полосу прокрутки списка игр не показываем: он прокручивается колесом, а полоса резала обложки.
        var gamesScroll = new ScrollViewer { Content = _sideGames, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        _sideGamesHead = new Border();
        var top = new StackPanel { Spacing = 0, Children = { _sideHead, _sideNav, _sideGamesHead } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_sideBottom, Dock.Bottom);
        var sideDock = new DockPanel { Children = { top, _sideBottom, gamesScroll } };
        _side = new Border
        {
            Background = Ui.Res("Rail"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 1, 0),
            Width = SideCollapsed ? SideNarrow : SideWide, ClipToBounds = true, Child = sideDock,
        };
        if (Animate.On) _side.Transitions = [new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
        _railHost = _side;
        _side.IsVisible = !Settings.Data.Bool("railHidden");

        // ---- шапка над страницей
        _search.Watermark = I18n.T("search.home");
        _search.InnerLeftContent = new Border { Padding = new Thickness(13, 0, 0, 0), Child = Ui.Icon(Icons.Search, 16, Ui.Res("Faint")) };
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
            Margin = new Thickness(0, 0, 8, 0), Background = Ui.Res("Surface2"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Ctrl K", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Muted") },
        };
        _search.Width = double.NaN;
        _search.Height = 40;
        _search.MinHeight = 40;
        _search.Padding = new Thickness(10, 6);
        _search.CornerRadius = new CornerRadius(12);
        _search.Classes.Add("top-search");
        _search.Classes.Add("store-search");
        _search.VerticalAlignment = VerticalAlignment.Center;
        _search.HorizontalAlignment = HorizontalAlignment.Stretch;

        foreach (var b in new[] { _back, _forward })
        {
            b.Width = b.Height = 34;
            b.CornerRadius = new CornerRadius(9);
        }
        var navPair = Ui.Row(4, _back, _forward);
        navPair.VerticalAlignment = VerticalAlignment.Center;
        navPair.Margin = new Thickness(20, 0, 12, 0);

        _more.Content = Ui.Icon(Icons.More, 20);
        ToolTip.SetTip(_more, I18n.T("top.more"));
        _more.Click += (_, _) => MoreMenu().ShowAt(_more, true);
        _asideToggle = new Button { Classes = { "icon" }, Content = Ui.Icon(Icons.Sidebar, 18) };
        _asideToggle.Click += (_, _) => ToggleAside();
        _adWrap = new Border { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        foreach (var b in new[] { _bell, _downloads, _more }) { b.Width = b.Height = 38; b.CornerRadius = new CornerRadius(10); }
        _statusPill = new StatusPill();
        // Профиль — справа в шапке: аватар, имя и уровень (меню аккаунта выпадает вниз).
        _accountButton.Classes.Clear();
        _accountButton.Classes.Add("acct20");
        _accountButton.Height = 42;
        _accountButton.CornerRadius = new CornerRadius(999);
        _accountButton.Padding = new Thickness(5, 0, 14, 0);
        _accountButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        _accountButton.VerticalContentAlignment = VerticalAlignment.Center;
        _accountButton.Margin = new Thickness(6, 0, 0, 0);
        var tools = Ui.Row(4, _updatePill, _statusPill, _adWrap, _bell, _downloads, _more, _accountButton);
        tools.VerticalAlignment = VerticalAlignment.Center;
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        tools.Margin = new Thickness(12, 0, 6, 0);

        // Кнопки окна как у Windows — вплотную к углу.
        _maxButton = WinButton(Icons.Maximize, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, I18n.T("win.maximize"));
        var winButtons = Ui.Row(0,
            WinButton(Icons.Minimize, () => WindowState = WindowState.Minimized, I18n.T("win.minimize")),
            _maxButton,
            WinButton(Icons.Close, Close, I18n.T("win.close"), "close"));
        winButtons.VerticalAlignment = VerticalAlignment.Top;
        _winButtons = winButtons;

        var titlebar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Height = TitleHeight, Background = Brushes.Transparent };
        titlebar.Children.Add(navPair);
        var searchHost = new Border { Child = _search, MaxWidth = 560, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        var searchLane = new Border { Child = searchHost, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        // Поле поиска тянется до 560, но не налезает на кнопки справа.
        titlebar.SizeChanged += (_, _) => searchHost.Width = Math.Clamp(titlebar.Bounds.Width - navPair.Bounds.Width - tools.Bounds.Width - winButtons.Bounds.Width - 60, 220, 560);
        Grid.SetColumn(searchLane, 1);
        titlebar.Children.Add(searchLane);
        var toolsHost = new Border { Child = tools, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(toolsHost, 2);
        titlebar.Children.Add(toolsHost);
        Grid.SetColumn(winButtons, 3);
        titlebar.Children.Add(winButtons);
        titlebar.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is null && v.FindAncestorOfType<TextBox>(true) is null)
            {
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
            }
        };
        var titleBorder = new Border { Child = titlebar, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };

        // ---- страница и правая панель сведений
        var main = new DockPanel();
        _asideHost.BorderBrush = Ui.Res("Line");
        _asideHost.Background = Ui.Res("Layer");
        _asideHost.Child = new ScrollViewer { Content = new Border { Padding = new Thickness(20, 22, 20, 22), Child = _aside }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(_asideHost, Dock.Right);
        main.Children.Add(_asideHost);
        // 20.0: колонки рекламы справа нет — реклама стала плиткой в «Подборках» на главной.
        _adRail.IsVisible = false;
        main.Children.Add(new Panel { ClipToBounds = true, Children = { _glow, _pageUnder, _page, _pageFx } });
        _sheet = new Border { Background = Ui.Res("Layer"), ClipToBounds = true, Child = main };
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
            UpdateAsideVisibility();
            UpdateAdVisibility();
            if (SideCollapsed != _sideIsCollapsed) RenderRail();
        };

        // Сетка окна: панель слева на всю высоту, справа — шапка над страницей; строка состояния — по желанию.
        var root = new Grid { RowDefinitions = new RowDefinitions($"{TitleHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)},*,Auto"), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var status = new StatusBar();
        Grid.SetRow(status, 2);
        Grid.SetColumnSpan(status, 2);
        root.Children.Add(status);
        Grid.SetRowSpan(_side, 2);
        root.Children.Add(_side);
        Grid.SetColumn(titleBorder, 1);
        root.Children.Add(titleBorder);
        Grid.SetRow(_sheet, 1);
        Grid.SetColumn(_sheet, 1);
        root.Children.Add(_sheet);
        _root = root;

        // Меню аккаунта — под профилем в шапке.
        _accountPanel.HorizontalAlignment = HorizontalAlignment.Right;
        _accountPanel.VerticalAlignment = VerticalAlignment.Top;
        _accountPanel.Margin = new Thickness(0, TitleHeight - 2, 142, 0);
        _downloadsPanel.Margin = new Thickness(0, TitleHeight - 2, 250, 0);
        _bellPanel.Margin = new Thickness(0, TitleHeight - 2, 292, 0);

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
        // Друзья и уведомления — над строкой состояния, если она включена.
        void AboveStatus()
        {
            var h = status.IsVisible ? status.Height : 0;
            _toasts.Margin = new Thickness(0, 0, 24, 24 + h);
            dock.Margin = new Thickness(0, 0, 18, 16 + h);
        }
        AboveStatus();
        status.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) AboveStatus(); };
        layers.Children.Add(_fxLayer);
        layers.Children.Add(_launchLayer);
        layers.Children.Add(_toasts);
        layers.Children.Add(_overlay);
        layers.Children.Add(new ScreenFx());
        if (Splash.Enabled)
        {
            _splash = new Splash();
            layers.Children.Add(_splash);
        }
        return layers;
    }

    // ---------------------------------------------------------------- боковая панель

    /// <summary>Содержимое пункта: значок (с точкой-меткой) и подпись; справа — необязательная приписка.</summary>
    static Control SideContent(string icon, string label, bool active, bool collapsed, Control? badge = null, string? trailing = null)
    {
        Control glyph = Ui.Icon(icon, 19, active ? Ui.Res("Brand2") : Ui.Res("Muted"));
        if (badge is not null)
        {
            if (badge.Parent is Panel old) old.Children.Remove(badge);
            badge.Margin = new Thickness(0, -3, -3, 0);
            glyph = new Panel { Width = 20, Height = 20, Children = { glyph, badge } };
        }
        if (collapsed) return glyph;
        var text = new TextBlock
        {
            Text = label, FontSize = 14, FontWeight = active ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = active ? Ui.Res("Text") : Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var dock = new DockPanel { LastChildFill = true };
        if (trailing is not null)
        {
            var t = new TextBlock { Text = trailing, FontSize = 12, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(t, Dock.Right);
            dock.Children.Add(t);
        }
        var row = Ui.Row(12, glyph, text);
        row.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(row);
        return dock;
    }

    void SideItem(Button b, bool active, bool collapsed, Control? badge = null, string? trailing = null)
    {
        if (!_nav.TryGetValue(b, out var n)) return;
        var label = Label(n.Label);
        b.Classes.Set("active", active);
        b.Background = active ? Ui.Res("BrandSoft") : Brushes.Transparent;
        b.CornerRadius = new CornerRadius(10);
        if (collapsed)
        {
            b.Width = 48;
            b.Height = 44;
            b.Padding = new Thickness(0);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        else
        {
            b.Width = double.NaN;
            b.Height = 42;
            b.Padding = new Thickness(12, 0);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
        b.VerticalContentAlignment = VerticalAlignment.Center;
        b.Content = SideContent(n.Icon, label, active, collapsed, badge, trailing);
        ToolTip.SetTip(b, collapsed ? label : null);
        ToolTip.SetPlacement(b, PlacementMode.Right);
    }

    /// <summary>Подпись под состоянием игры: запущена, нужен загрузчик, сколько модов или «готова к модам».</summary>
    static (string Text, IBrush Color) GameLine(GameState g)
    {
        if (Features.Launcher.IsRunning(g.Def.Id)) return (I18n.T("v20.game.running"), Kit20.Readable("Good"));
        var ready = g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft;
        if (!ready) return (I18n.T("v20.game.needs", ("loader", g.Def.LoaderName)), Kit20.Readable("Warn"));
        if (Features.NewGames.IsNew(g)) return (I18n.T("v20.game.new"), Kit20.Readable("Brand2"));
        if (g.ModCount > 0) return (StoreKit.Mods(g.ModCount), Ui.Res("Faint"));
        return (I18n.T("v20.game.ready"), Ui.Res("Faint"));
    }

    /// <summary>Игры на панели: все найденные (без скрытых), недавние сверху.</summary>
    static List<GameState> SideGames()
    {
        var hidden = Settings.Data.Arr("hiddenGames").Select(x => x?.ToString()).ToHashSet();
        return OrderedGames()
            .Where(g => !hidden.Contains(g.Def.Id) && !Features.GameCollections.IsHidden(g.Def.Id) && g.Status == Detect.Found)
            .OrderByDescending(g => Features.NewGames.Recent(g))
            .ToList();
    }

    Button SideGameRow(GameState g, bool collapsed)
    {
        var id = g.Def.Id;
        var running = Features.Launcher.IsRunning(id);
        var ready = g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft;
        var fresh = Features.NewGames.IsNew(g);
        var (w, h) = collapsed ? (34.0, 46.0) : (30.0, 40.0);
        var art = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(collapsed ? 7 : 6), ClipToBounds = true, Child = Ui.GameImage(g.Def, 120, art: Images.Art.Cover) };
        var dot = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = running ? Ui.Res("Good") : !ready ? Ui.Res("Warn") : Ui.Res("Brand"),
            BorderBrush = Ui.Res("Rail"), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, -3, -3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsVisible = collapsed && (running || !ready || fresh),
        };
        if (running) dot.Classes.Add("pulse");
        var cover = new Panel { Width = w, Height = h, Children = { art, dot } };
        var b = new Button { Classes = { "nav20", "side-game" }, CornerRadius = new CornerRadius(10), VerticalContentAlignment = VerticalAlignment.Center };
        if (collapsed)
        {
            b.Width = 48;
            b.Height = 54;
            b.Padding = new Thickness(0);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.Content = cover;
            ToolTip.SetTip(b, g.Def.Name);
            ToolTip.SetPlacement(b, PlacementMode.Right);
        }
        else
        {
            var (line, color) = GameLine(g);
            b.Height = 52;
            b.Padding = new Thickness(8, 0, 10, 0);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var name = new TextBlock { Text = g.Def.Name, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
            var sub = new TextBlock { Text = line, FontSize = 12, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis };
            var words = Ui.Col(2, name, sub);
            words.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            grid.Children.Add(cover);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            b.Content = grid;
            if (running || fresh) ToolTip.SetTip(b, $"{g.Def.Name} · {line}");
        }
        b.Click += (_, _) => Navigate(() => new GamePage(id));
        // Двойной щелчок — сразу играть, как в библиотеке Steam.
        b.DoubleTapped += (_, _) => Guard.Later(() => Actions.Play(g));
        b.ContextFlyout = GameCard.Menu(g);
        return b;
    }

    void RenderRail()
    {
        if (_side is null) return;
        var collapsed = SideCollapsed;
        var changed = collapsed != _sideIsCollapsed;
        _sideIsCollapsed = collapsed;
        _side.Width = collapsed ? SideNarrow : SideWide;

        // Логотип и название; у развёрнутой панели — кнопка «свернуть».
        if (_sideHead is not null)
        {
            _sideHead.Children.Clear();
            var logo = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = new Image { Source = Images.Asset("icon.png", 96), Width = 32, Height = 32 } };
            var brand = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
            brand.PointerPressed += (_, e) => { if (e.GetCurrentPoint(brand).Properties.IsLeftButtonPressed) { e.Handled = true; Navigate(() => new HomePage()); } };
            ToolTip.SetTip(brand, I18n.T("v20.nav.home"));
            if (collapsed)
            {
                brand.Child = logo;
                brand.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumnSpan(brand, 2);
                _sideHead.Children.Add(brand);
            }
            else
            {
                var name = new TextBlock { Text = "ModLaunch", FontFamily = Look.Display, FontSize = Look.WideHeadings ? 15.5 : 16.5, FontWeight = FontWeight.SemiBold, LetterSpacing = Look.WideHeadings ? -0.2 : -0.3, VerticalAlignment = VerticalAlignment.Center };
                brand.Child = Ui.Row(10, logo, name);
                brand.Margin = new Thickness(18, 0, 0, 0);
                brand.HorizontalAlignment = HorizontalAlignment.Left;
                _sideHead.Children.Add(brand);
                if (_sideToggle is not null)
                {
                    if (_sideToggle.Parent is Panel p) p.Children.Remove(_sideToggle);
                    _sideToggle.Content = Ui.Icon(Icons.ChevronLeft, 16, Ui.Res("Faint"));
                    _sideToggle.Margin = new Thickness(0, 0, 10, 0);
                    ToolTip.SetTip(_sideToggle, I18n.T("v20.side.collapse"));
                    Grid.SetColumn(_sideToggle, 1);
                    _sideHead.Children.Add(_sideToggle);
                }
            }
        }

        // Разделы.
        _sideNav.Margin = collapsed ? new Thickness(0, 2, 0, 0) : new Thickness(12, 2, 12, 0);
        _sideNav.Children.Clear();
        // Каталог модов открывается со страницы игры и с главной, а на его месте — Creator Hub.
        if (_creatorButton.Parent is Panel cp) cp.Children.Remove(_creatorButton);
        foreach (var b in new[] { _homeButton, _creatorButton, _feedButton, _libraryButton }) _sideNav.Children.Add(b);
        SideItem(_homeButton, _current is HomePage or SearchPage, collapsed);
        SideItem(_creatorButton, _current is MarketPage or CreatorPage or XStudio or ListingPage or AuthorPage, collapsed);
        SideItem(_feedButton, _current is AiBuildsPage, collapsed);
        SideItem(_libraryButton, _current is LibraryPage or AddGamePage or SteamLibraryPage, collapsed);

        // «Мои игры»: подпись с «+» и строки игр (перестраиваются, только когда что-то поменялось).
        var games = SideGames();
        if (_sideGamesHead is Border gh)
        {
            if (_sideAdd.Parent is Panel ap) ap.Children.Remove(_sideAdd);
            var rule = new Border { Height = 1, Background = Ui.Res("Line"), Margin = collapsed ? new Thickness(16, 12, 16, 8) : new Thickness(16, 14, 16, 10) };
            if (collapsed) gh.Child = rule;
            else
            {
                var caption = new TextBlock { Text = I18n.T("v20.side.games").ToUpper(I18n.Culture), FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.9, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center };
                var row = new DockPanel { Margin = new Thickness(24, 0, 14, 4), Height = 28 };
                DockPanel.SetDock(_sideAdd, Dock.Right);
                row.Children.Add(_sideAdd);
                row.Children.Add(caption);
                gh.Child = Ui.Col(0, rule, row);
            }
            _sideAdd.Classes.Set("active", _current is AddGamePage);
        }
        var key = string.Join("|", games.Select(g => $"{g.Def.Id}:{g.Def.Name}:{Features.Launcher.IsRunning(g.Def.Id)}:{g.LoaderInstalled}:{g.ModCount}:{Features.NewGames.IsNew(g)}"))
            + $"#{collapsed}#{Look.Theme}{Look.Accent}{I18n.Lang}";
        if (key != _sideGamesKey)
        {
            _sideGamesKey = key;
            _sideGameRows.Clear();
            _sideGames.Children.Clear();
            _sideGames.Margin = collapsed ? new Thickness(0, 0, 0, 8) : new Thickness(12, 0, 12, 8);
            foreach (var g in games)
            {
                var row = SideGameRow(g, collapsed);
                _sideGames.Children.Add(row);
                _sideGameRows.Add((g.Def.Id, row));
            }
            if (games.Count == 0 && !collapsed)
                _sideGames.Children.Add(new TextBlock { Text = I18n.T("v20.side.none"), FontSize = 12.5, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4, 12, 0) });
            if (collapsed)
            {
                var plus = new Button { Classes = { "nav20" }, Width = 48, Height = 40, CornerRadius = new CornerRadius(10), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Content = Ui.Icon(Icons.Plus, 17, Ui.Res("Muted")) };
                plus.Click += (_, _) => Navigate(() => new AddGamePage());
                ToolTip.SetTip(plus, I18n.T("add.title"));
                ToolTip.SetPlacement(plus, PlacementMode.Right);
                _sideGames.Children.Add(plus);
            }
        }
        foreach (var (id, row) in _sideGameRows)
        {
            var on = _current?.GameId == id;
            row.Classes.Set("active", on);
            row.Background = on ? Ui.Res("BrandSoft") : Brushes.Transparent;
        }

        // Низ панели: Сообщество (сколько друзей в сети), статистика владельца, Настройки.
        _sideBottom.Margin = collapsed ? new Thickness(0, 6, 0, 12) : new Thickness(12, 6, 12, 14);
        _sideBottom.Children.Clear();
        _sideBottom.Children.Add(new Border { Height = 1, Background = Ui.Res("Line"), Margin = collapsed ? new Thickness(16, 0, 16, 8) : new Thickness(4, 0, 4, 8) });
        var view = Social.Friends.View();
        var online = view.Friends.Count(f => f.State != "offline");
        _friendsBadge.IsVisible = view.Incoming.Count > 0 || online > 0;
        _friendsBadge.Background = view.Incoming.Count > 0 ? Ui.Res("Warn") : Ui.Res("Good");
        _friendsButton.IsVisible = Settings.Data.Bool("railFriends", true);
        if (_friendsButton.IsVisible)
        {
            _sideBottom.Children.Add(_friendsButton);
            SideItem(_friendsButton, _current is FriendsPage or CommunityPage, collapsed, collapsed ? _friendsBadge : null, I18n.T("v20.side.online", ("n", online)));
        }
        if (Settings.Data.Bool("ownerStats"))
        {
            _sideBottom.Children.Add(_statsButton);
            SideItem(_statsButton, _current is StatsPage, collapsed);
        }
        _sideBottom.Children.Add(_settingsButton);
        SideItem(_settingsButton, _current is SettingsPage, collapsed);
        // У свёрнутой панели — кнопка «развернуть» внизу (если свернули сами, а не узкое окно или рабочий раздел).
        if (collapsed && _sideToggle is not null && Settings.Data.Bool("sideCollapsed"))
        {
            if (_sideToggle.Parent is Panel p) p.Children.Remove(_sideToggle);
            _sideToggle.Content = Ui.Icon(Icons.ChevronRight, 16, Ui.Res("Faint"));
            _sideToggle.Margin = new Thickness(0, 4, 0, 0);
            _sideToggle.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(_sideToggle, I18n.T("v20.side.expand"));
            _sideBottom.Children.Add(_sideToggle);
        }
        else if (_sideToggle is not null) _sideToggle.HorizontalAlignment = HorizontalAlignment.Right;

        RenderAccount();
        _statusPill?.Render();
        _updatePill.IsVisible = Setup.Updater.Available;
        if (Setup.Updater.Latest is { } latest) _updatePill.Content = Ui.Row(6, Ui.Icon(Icons.Sparkles, 14), new TextBlock { Text = I18n.T("upd.app.pill", ("version", latest.Version)), VerticalAlignment = VerticalAlignment.Center });
        if (changed) _friendsDock?.Render();
    }
}
