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
/// Рамка окна 9.2 в духе Microsoft Store: сверху — шапка (назад, название, поиск по центру,
/// загрузки), слева — подписанные разделы, недавние игры и (9.3) аккаунт внизу, а страница
/// лежит на отдельном «листе» со скруглённым левым верхним углом.
/// </summary>
public sealed partial class MainWindow
{
    const double RailWidth = 76;
    const double TitleHeight = 50;
    /// <summary>Значок первого пункта — логотип ModLaunch (а не домик): «Лента» — лицо программы.</summary>
    const string LogoIcon = "@logo";

    /// <summary>Значок и подпись каждого раздела — чтобы перерисовать выбранный цветом акцента.</summary>
    readonly Dictionary<Button, (string Icon, string Label)> _nav = [];
    Border? _crumbSep;
    /// <summary>Невысокое окно: подписи под значками прячутся, пункты становятся ниже — всё помещается.</summary>
    bool _railCompact;
    /// <summary>Слой над страницей для анимаций открытия игры (9.3): снимок старой страницы, вспышки, рамки.</summary>
    readonly Panel _pageFx = new() { IsHitTestVisible = false, IsVisible = false, ClipToBounds = true };
    /// <summary>Слой под страницей: снимок прежнего экрана, который новая страница «прорезает».</summary>
    readonly Panel _pageUnder = new() { IsHitTestVisible = false, IsVisible = false, ClipToBounds = true };
    internal Panel PageFx => _pageFx;
    internal Panel PageUnder => _pageUnder;
    internal Control PageHost => _page;

    Button NavButton(string icon, string labelKey, Action onClick)
    {
        var b = new Button { Classes = { "nav" }, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center };
        b.Click += (_, _) => onClick();
        _nav[b] = (icon, labelKey);
        b.Content = NavContent(icon, Label(labelKey), false, null, false);
        return b;
    }

    static string Label(string key) => key.Contains('.') ? I18n.T(key) : key;

    /// <summary>
    /// Значок над подписью; у выбранного — значок цветом акцента, светящаяся полоска слева
    /// (9.3: «игровая» подсветка), у логотипа — мягкое свечение вокруг.
    /// </summary>
    static Control NavContent(string icon, string label, bool active, Control? badge, bool compact)
    {
        Control glyph;
        if (icon == LogoIcon)
        {
            var size = compact ? 24 : 27;
            var logo = new Image { Source = Images.Asset("icon.png", 96), Width = size, Height = size };
            glyph = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2.0), Child = logo,
                BoxShadow = active ? new BoxShadows(new BoxShadow { Blur = 16, Spread = 1, Color = WithAlpha(Ui.Res("Brand"), 150) }) : default,
            };
        }
        else glyph = Ui.Icon(icon, 20, active ? Ui.Res("Brand2") : Ui.Res("Muted"));
        Control iconHost = glyph;
        if (badge is not null)
        {
            if (badge.Parent is Panel old) old.Children.Remove(badge);
            iconHost = new Panel { Width = 28, Height = 22, Children = { glyph, badge } };
        }
        var col = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { iconHost } };
        if (!compact)
            col.Children.Add(new TextBlock
            {
                // Длинная подпись («Creator Hub») — чуть мельче, чтобы влезла целиком.
                Text = label, FontSize = label.Length > 9 ? 10 : 11, FontWeight = active ? FontWeight.SemiBold : FontWeight.Medium,
                Foreground = active ? Ui.Res("Text") : Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 64,
            });
        var pip = new Border
        {
            Width = 3, Height = active ? 20 : 0, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = active ? new BoxShadows(new BoxShadow { Blur = 10, Spread = 1, Color = WithAlpha(Ui.Res("Brand"), 200) }) : default,
        };
        return new Panel { Children = { col, pip } };
    }

    static Color WithAlpha(IBrush brush, byte alpha) =>
        brush is ISolidColorBrush s ? Color.FromArgb(alpha, s.Color.R, s.Color.G, s.Color.B) : Color.FromArgb(alpha, 123, 92, 255);

    void SetNav(Button b, bool active, Control? badge = null)
    {
        if (!_nav.TryGetValue(b, out var n)) return;
        b.Classes.Set("active", active);
        b.Height = _railCompact ? 46 : 58;
        b.Content = NavContent(n.Icon, Label(n.Label), active, badge, _railCompact);
        ToolTip.SetTip(b, _railCompact ? Label(n.Label) : null);
        ToolTip.SetPlacement(b, PlacementMode.Right);
    }

    static Control RailLine(Thickness margin) => new Border { Height = 1, Width = 40, Margin = margin, Background = Ui.Res("Line"), HorizontalAlignment = HorizontalAlignment.Center };

    Control BuildLayout()
    {
        // ---- боковая панель (9.3): «Лента» с логотипом, игры, моды, Creator Hub и панель сверху,
        // недавние игры посередине, друзья, настройки и аккаунт — внизу.
        var rail = new DockPanel { Width = RailWidth, LastChildFill = true };
        var top = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 2), HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var b in new[] { _homeButton, _libraryButton, _modsButton, _creatorButton, _panelButton }) top.Children.Add(b);
        top.Children.Add(RailLine(new Thickness(0, 8, 0, 6)));
        DockPanel.SetDock(top, Dock.Top);
        var bottom = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Center };
        bottom.Children.Add(RailLine(new Thickness(0, 4, 0, 6)));
        foreach (var b in new[] { _friendsButton, _statsButton, _donateButton, _settingsButton }) bottom.Children.Add(b);
        // Аккаунт — самым нижним пунктом: аватар с точкой «в сети».
        _accountButton.Classes.Clear();
        _accountButton.Classes.Add("nav");
        _accountButton.Classes.Add("account");
        _accountButton.Height = 54;
        _accountButton.Padding = new Thickness(0);
        _accountButton.CornerRadius = new CornerRadius(8);
        _accountButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _accountButton.VerticalContentAlignment = VerticalAlignment.Center;
        _accountButton.Margin = new Thickness(0, 4, 0, 0);
        ToolTip.SetPlacement(_accountButton, PlacementMode.Right);
        bottom.Children.Add(_accountButton);
        DockPanel.SetDock(bottom, Dock.Bottom);
        rail.Children.Add(top);
        rail.Children.Add(bottom);
        rail.Children.Add(new ScrollViewer { Content = _railGames, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
        var railBorder = new Border { Child = rail, Background = Ui.Res("Rail") };
        _railHost = railBorder;
        railBorder.IsVisible = !Settings.Data.Bool("railHidden");
        railBorder.SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Height < 720;
            if (compact == _railCompact) return;
            _railCompact = compact;
            RenderRail();
        };

        // ---- шапка: назад/вперёд и название слева, поиск по центру, инструменты справа
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
            Margin = new Thickness(0, 0, 8, 0), Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Ctrl K", FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Muted") },
        };
        _search.Width = double.NaN;
        _search.MaxWidth = 560;
        _search.MinWidth = 260;
        _search.Height = 34;
        _search.MinHeight = 34;
        _search.Padding = new Thickness(10, 5);
        _search.Classes.Add("top-search");
        _search.Classes.Add("store-search");
        _search.VerticalAlignment = VerticalAlignment.Center;
        _search.HorizontalAlignment = HorizontalAlignment.Stretch;

        _back.Width = _back.Height = _forward.Width = _forward.Height = 32;
        _back.CornerRadius = _forward.CornerRadius = new CornerRadius(6);
        var navPair = Ui.Row(0, _back, _forward);
        navPair.VerticalAlignment = VerticalAlignment.Center;
        var appIcon = new Image { Source = Images.Asset("icon.png", 48), Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var appName = new TextBlock { Text = "ModLaunch", FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var brand = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Row(9, appIcon, appName),
        };
        brand.PointerPressed += (_, e) => { if (e.GetCurrentPoint(brand).Properties.IsLeftButtonPressed) Navigate(() => new HomePage()); };
        ToolTip.SetTip(brand, I18n.T("v92.nav.home"));
        var crumbSep = new Border { Width = 1, Height = 16, Background = Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        _crumbSep = crumbSep;
        var left = Ui.Row(10, navPair, brand, crumbSep, _crumbs);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(10, 0, 12, 0);
        var leftHost = new Border { Child = left, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };

        _more.Content = Ui.Icon(Icons.More, 20);
        ToolTip.SetTip(_more, I18n.T("top.more"));
        _more.Click += (_, _) => MoreMenu().ShowAt(_more, true);
        _asideToggle = new Button { Classes = { "icon" }, Content = Ui.Icon(Icons.Sidebar, 18) };
        _asideToggle.Click += (_, _) => ToggleAside();
        _adWrap = new Border { Child = AdSlot.Pill(), VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new[] { _bell, _downloads, _more }) { b.Width = b.Height = 36; b.CornerRadius = new CornerRadius(6); }
        var tools = Ui.Row(4, _updatePill, _adWrap, _bell, _downloads, _more);
        tools.VerticalAlignment = VerticalAlignment.Center;
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        tools.Margin = new Thickness(12, 0, 8, 0);

        var winButtons = Ui.Row(0,
            WinButton(Icons.Minimize, () => WindowState = WindowState.Minimized, I18n.T("win.minimize")),
            WinButton(Icons.Maximize, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, I18n.T("win.maximize")),
            WinButton(Icons.Close, Close, I18n.T("win.close"), "close"));
        winButtons.VerticalAlignment = VerticalAlignment.Top;

        // Поиск — ровно по центру между левой и правой частью (как в Microsoft Store).
        var titlebar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto"), Height = TitleHeight, Background = Brushes.Transparent };
        titlebar.Children.Add(leftHost);
        var searchHost = new Border { Width = 540, Child = _search, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(searchHost, 1);
        titlebar.Children.Add(searchHost);
        var toolsHost = new Border { Child = tools, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(toolsHost, 2);
        titlebar.Children.Add(toolsHost);
        Grid.SetColumn(winButtons, 3);
        titlebar.Children.Add(winButtons);
        // Узкое окно: поиск сжимается, но не меньше 260.
        titlebar.SizeChanged += (_, _) => searchHost.Width = Math.Clamp(titlebar.Bounds.Width - 2 * 420, 260, 540);
        titlebar.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is null && v.FindAncestorOfType<TextBox>(true) is null)
            {
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
            }
        };

        // ---- «лист» со страницей и правой панелью сведений
        var main = new DockPanel();
        _asideHost.BorderBrush = Ui.Res("Line");
        _asideHost.Background = Ui.Res("Layer");
        _asideHost.Child = new ScrollViewer { Content = new Border { Padding = new Thickness(20, 22, 20, 22), Child = _aside }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(_asideHost, Dock.Right);
        main.Children.Add(_asideHost);
        main.Children.Add(new Panel { ClipToBounds = true, Children = { _glow, _pageUnder, _page, _pageFx } });
        _sheet = new Border
        {
            Background = Ui.Res("Layer"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1, 1, 0, 0),
            CornerRadius = new CornerRadius(8, 0, 0, 0), ClipToBounds = true, Child = main,
        };
        // Правый клик по пустому месту страницы: навигация и быстрые переходы.
        Ctx.Attach(_page, () => Ctx.Menu(
            Ctx.Item(I18n.T("ctx.back"), Icons.Back, GoBack, _index > 0, "Alt+Left"),
            Ctx.Item(I18n.T("ctx.forward"), Icons.Forward, GoForward, _index < _history.Count - 1, "Alt+Right"),
            Ctx.Item(I18n.T("ctx.reload"), Icons.Refresh, () => _current?.Build(), gesture: "F5"),
            "-",
            Ctx.Item(I18n.T("cp.title"), Icons.Grid, () => Navigate(() => new ControlPanelPage()), gesture: "Ctrl+Shift+P"),
            Ctx.Item(I18n.T("nav.menu"), Icons.Home, () => Navigate(() => new HomePage())),
            Ctx.Item("Creator Hub", Icons.Creator, () => Navigate(() => new MarketPage()), gesture: "Ctrl+Shift+C"),
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
        };

        var root = new Grid { RowDefinitions = new RowDefinitions($"{TitleHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)},*"), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumnSpan(titlebar, 2);
        root.Children.Add(titlebar);
        Grid.SetRow(railBorder, 1);
        root.Children.Add(railBorder);
        Grid.SetRow(_sheet, 1);
        Grid.SetColumn(_sheet, 1);
        root.Children.Add(_sheet);
        _root = root;

        // Меню аккаунта — рядом с аватаром внизу боковой панели.
        _accountPanel.HorizontalAlignment = HorizontalAlignment.Left;
        _accountPanel.VerticalAlignment = VerticalAlignment.Bottom;
        _accountPanel.Margin = new Thickness(RailWidth + 8, 0, 0, 12);

        var layers = new Panel();
        _layers = layers;
        layers.Classes.Set("anim", Look.Animations);
        layers.Children.Add(root);
        layers.Children.Add(_downloadsPanel);
        layers.Children.Add(_bellPanel);
        layers.Children.Add(_accountPanel);
        _friendsDock = new FriendsDock();
        layers.Children.Add(_friendsDock);
        // Анимация «Скачать» — поверх страницы и панелей, но под экраном запуска и окнами.
        layers.Children.Add(_fxLayer);
        layers.Children.Add(_launchLayer);
        layers.Children.Add(_toasts);
        layers.Children.Add(_overlay);
        if (Splash.Enabled)
        {
            _splash = new Splash();
            layers.Children.Add(_splash);
        }
        return layers;
    }

    void RenderRail()
    {
        _railGames.Children.Clear();
        foreach (var g in RailGames())
        {
            var id = g.Def.Id;
            var art = new Border { Width = 40, Height = 54, CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = Ui.GameImage(g.Def, 120, art: Images.Art.Cover) };
            var running = Features.Launcher.IsRunning(id);
            // Точка состояния: зелёная — можно играть с модами, жёлтая — сначала поставить загрузчик, мигает — игра запущена.
            var ready = g.LoaderInstalled || g.Def.Loader is Games.LoaderKind.None or Games.LoaderKind.Minecraft;
            var dot = new Border
            {
                Width = running ? 13 : 11, Height = running ? 13 : 11, CornerRadius = new CornerRadius(7), Background = running || ready ? Ui.Res("Good") : Ui.Res("Warn"),
                BorderBrush = Ui.Res("Rail"), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, -3, -3),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            };
            if (running) dot.Classes.Add("pulse");
            var b = new Button
            {
                Classes = { "rail" }, Width = 46, Height = 60, CornerRadius = new CornerRadius(9),
                Content = new Panel { Children = { art, dot } },
            };
            b.ClipToBounds = false;
            if (_current?.GameId == id) b.Classes.Add("active");
            b.Click += (_, _) => Navigate(() => new GamePage(id));
            b.ContextFlyout = GameCard.Menu(g);
            ToolTip.SetTip(b, running ? $"{g.Def.Name} · {I18n.T("run.running")}" : g.Def.Name);
            ToolTip.SetPlacement(b, PlacementMode.Right);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            var slot = new Panel { Classes = { "rail-item" }, Width = RailWidth, Children = { new Border { Classes = { "rail-pip" } }, b } };
            slot.Classes.Set("active", _current?.GameId == id);
            _railGames.Children.Add(slot);
        }
        var plus = RailIcon(Icons.Plus, () => Navigate(() => new AddGamePage()), I18n.T("add.title"));
        plus.Width = plus.Height = 40;
        plus.CornerRadius = new CornerRadius(9);
        plus.Classes.Set("active", _current is AddGamePage);
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetPlacement(plus, PlacementMode.Right);
        _railGames.Children.Add(new Panel { Classes = { "rail-item" }, Width = RailWidth, Margin = new Thickness(0, 2, 0, 6), Children = { new Border { Classes = { "rail-pip" } }, plus } });

        // Разделы: выбранный — значком цвета акцента и полоской слева.
        SetNav(_homeButton, _current is HomePage or SearchPage);
        // 9.3: «Игры» и «Моды» на панели — по желанию (Настройки → Внешний вид → Рамка окна);
        // без них их место занимает «Панель», и она подсвечена на страницах библиотеки и модов.
        var sections = Settings.Data.Bool("railGamesMods");
        _libraryButton.IsVisible = _modsButton.IsVisible = sections;
        SetNav(_libraryButton, _current is LibraryPage or AddGamePage or GamePage or MinecraftPage || _current is ModPage);
        SetNav(_creatorButton, _current is MarketPage or CreatorPage or ListingPage or AuthorPage);
        SetNav(_modsButton, _current is ModsCenterPage);
        SetNav(_panelButton, _current is ControlPanelPage || !sections && _current is LibraryPage or ModsCenterPage);
        var view = Social.Friends.View();
        _friendsBadge.IsVisible = view.Incoming.Count > 0 || view.Friends.Any(f => f.State != "offline");
        _friendsBadge.Background = view.Incoming.Count > 0 ? Ui.Res("Warn") : Ui.Res("Good");
        SetNav(_friendsButton, _current is FriendsPage, _friendsBadge);
        SetNav(_statsButton, _current is StatsPage);
        SetNav(_donateButton, _current is DonatePage);
        SetNav(_settingsButton, _current is SettingsPage);
        _friendsButton.IsVisible = Settings.Data.Bool("railFriends", true);
        _statsButton.IsVisible = Settings.Data.Bool("ownerStats");
        RenderAccount();
        _updatePill.IsVisible = Setup.Updater.Available;
        if (Setup.Updater.Latest is { } latest) _updatePill.Content = Ui.Row(6, Ui.Icon(Icons.Sparkles, 14), new TextBlock { Text = I18n.T("upd.app.pill", ("version", latest.Version)), VerticalAlignment = VerticalAlignment.Center });
    }
}
