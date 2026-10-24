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
/// 20.0 «Платформа»: рамка окна как у Steam и других больших лаунчеров — одна программа, а не набор экранов.
/// Сверху — шапка с четырьмя разделами (Магазин · Библиотека · Сообщество · Создателям), уведомлениями и аккаунтом;
/// под ней — вкладки открытого раздела и поиск. В библиотеке слева — список игр, справа — выбранная игра.
/// Внизу — полоса платформы: «Добавить игру», состояние, загрузки и «Друзья и чат».
/// </summary>
public sealed partial class MainWindow
{
    const double TitleHeight = 52;
    const double SubHeight = 46;
    /// <summary>Ширина списка игр слева в библиотеке.</summary>
    internal const double SidebarWidth = 256;

    Border? _crumbSep;
    /// <summary>Слой над страницей для анимаций открытия игры (9.3): снимок старой страницы, вспышки, рамки.</summary>
    readonly Panel _pageFx = new() { IsHitTestVisible = false, IsVisible = false, ClipToBounds = true };
    /// <summary>Слой под страницей: снимок прежнего экрана, который новая страница «прорезает».</summary>
    readonly Panel _pageUnder = new() { IsHitTestVisible = false, IsVisible = false, ClipToBounds = true };
    internal Panel PageFx => _pageFx;
    internal Panel PageUnder => _pageUnder;
    internal Control PageHost => _page;

    // ---------------------------------------------------------------- разделы платформы

    Section _section = Section.None;
    /// <summary>Открытый раздел (для снимков и проверок).</summary>
    public Section CurrentSection => _section;
    /// <summary>Где в каждом разделе были в последний раз: раздел открывается на том же месте, как в Steam.</summary>
    readonly Dictionary<Section, Func<Page>> _sectionLast = [];
    readonly Dictionary<Section, Button> _ptabs = [];
    readonly StackPanel _subTabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    readonly LibrarySidebar _sidebar = new();
    Border? _sidebarHost;
    TextBlock? _wordmark;
    string _subKey = "";

    /// <summary>Открыть раздел: туда, где в нём были; если уже в нём — на его главную вкладку.</summary>
    public void OpenSection(Section s)
    {
        var tabs = PlatformNav.Tabs(s);
        if (s == _section)
        {
            if (_current is not null && tabs.Count > 0 && tabs[0].Lit(_current)) return;
            Navigate(tabs.Count > 0 ? tabs[0].Open : PlatformNav.Home(s));
            return;
        }
        Navigate(_sectionLast.TryGetValue(s, out var last) ? last : PlatformNav.Home(s));
    }

    /// <summary>Запомнить раздел показанной страницы (страницы без своего раздела — мод, лот — остаются в текущем).</summary>
    void TrackSection(Page page)
    {
        _section = PlatformNav.Of(page) ?? _section;
        if (_index >= 0 && _index < _history.Count) _sectionLast[_section] = _history[_index];
    }

    /// <summary>Вкладка раздела в шапке: крупная надпись, у открытого — подчёркивание цветом акцента.</summary>
    Button PlatformTab(Section s)
    {
        var label = new TextBlock
        {
            Classes = { "ptab-label" }, FontSize = 13.5, FontWeight = FontWeight.Bold, LetterSpacing = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var line = new Border
        {
            Classes = { "ptab-line" }, Height = 3, CornerRadius = new CornerRadius(2), Background = Ui.Res("Brand"),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 0),
        };
        var b = new Button { Classes = { "ptab" }, Height = TitleHeight, Content = new Panel { Children = { label, line } } };
        b.Click += (_, _) => OpenSection(s);
        _ptabs[s] = b;
        return b;
    }

    void RenderPlatformTabs()
    {
        foreach (var (s, b) in _ptabs)
        {
            var on = s == _section;
            b.Classes.Set("active", on);
            if (b.Content is Panel { Children: [TextBlock label, Border line] })
            {
                label.Text = I18n.T(PlatformNav.Key(s)).ToUpper(I18n.Culture);
                line.IsVisible = on;
                line.BoxShadow = on && !Look.Calm ? new BoxShadows(new BoxShadow { Blur = 12, Spread = 0, Color = WithAlpha(Ui.Res("Brand"), 190) }) : default;
            }
        }
    }

    static Color WithAlpha(IBrush brush, byte alpha) =>
        brush is ISolidColorBrush s ? Color.FromArgb(alpha, s.Color.R, s.Color.G, s.Color.B) : Color.FromArgb(alpha, 123, 92, 255);

    /// <summary>Вкладки открытого раздела под шапкой. Страницы без раздела (настройки) — путь к ним.</summary>
    void RenderSubTabs()
    {
        var tabs = PlatformNav.Tabs(_section);
        var key = $"{_section}|{I18n.Lang}|{Look.Theme}{Look.Accent}|{_current?.GetType().Name}|" + string.Join(",", tabs.Select(t => _current is not null && t.Lit(_current) ? "1" : "0"));
        _crumbs.IsVisible = tabs.Count == 0;
        if (_crumbSep is not null) _crumbSep.IsVisible = false;
        if (key == _subKey) return;
        _subKey = key;
        _subTabs.Children.Clear();
        foreach (var t in tabs)
        {
            var tab = t;
            var on = _current is not null && tab.Lit(_current);
            var b = new Button
            {
                Classes = { "subtab" },
                Content = Ui.Row(7, Ui.Icon(tab.Icon, 15, on ? Ui.Res("Brand2") : Ui.Res("Faint")), new TextBlock { Text = I18n.T(tab.Key), VerticalAlignment = VerticalAlignment.Center }),
            };
            if (on) b.Classes.Add("active");
            // Щелчок по открытой вкладке тоже ведёт на её начало (из проекта — к списку проектов, со вкладки рынка — на витрину).
            b.Click += (_, _) => Navigate(tab.Open);
            _subTabs.Children.Add(b);
        }
    }

    /// <summary>Список игр слева — только в библиотеке (и если его не спрятали: Ctrl+B).</summary>
    void UpdateSidebar()
    {
        if (_sidebarHost is null) return;
        var show = _section == Section.Library && !Settings.Data.Bool("railHidden");
        _sidebarHost.IsVisible = show;
        if (show) _sidebar.Render(_current);
        if (_sheet is not null)
        {
            _sheet.CornerRadius = show ? new CornerRadius(10, 0, 0, 0) : new CornerRadius(0);
            _sheet.BorderThickness = show ? new Thickness(1, 1, 0, 0) : new Thickness(0, 1, 0, 0);
        }
    }

    Control BuildLayout()
    {
        // ---- шапка: назад/вперёд и знак, разделы, справа — уведомления, меню, аккаунт и кнопки окна
        _back.Width = _back.Height = _forward.Width = _forward.Height = 32;
        _back.CornerRadius = _forward.CornerRadius = new CornerRadius(6);
        var navPair = Ui.Row(0, _back, _forward);
        navPair.VerticalAlignment = VerticalAlignment.Center;
        var appIcon = new Image { Source = Images.Asset("icon.png", 64), Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        _wordmark = new TextBlock { Text = "ModLaunch", FontSize = 15, FontWeight = FontWeight.Bold, LetterSpacing = -0.2, VerticalAlignment = VerticalAlignment.Center };
        var brand = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0, 10, 0), Child = Ui.Row(9, appIcon, _wordmark),
        };
        brand.PointerPressed += (_, e) => { if (e.GetCurrentPoint(brand).Properties.IsLeftButtonPressed) Navigate(() => new HomePage()); };
        ToolTip.SetTip(brand, I18n.T("v92.nav.home"));
        var left = Ui.Row(8, navPair, brand);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(10, 0, 6, 0);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Stretch };
        foreach (var s in PlatformNav.Main) tabs.Children.Add(PlatformTab(s));

        _more.Content = Ui.Icon(Icons.More, 20);
        ToolTip.SetTip(_more, I18n.T("top.more"));
        _more.Click += (_, _) => MoreMenu().ShowAt(_more, true);
        _adWrap = new Border { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        foreach (var b in new[] { _bell, _more }) { b.Width = b.Height = 36; b.CornerRadius = new CornerRadius(8); }
        _accountButton.Classes.Clear();
        _accountButton.Classes.Add("account-chip");
        _accountButton.Height = 38;
        _accountButton.VerticalAlignment = VerticalAlignment.Center;
        _accountButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        _accountButton.VerticalContentAlignment = VerticalAlignment.Center;
        var tools = Ui.Row(4, _updatePill, _bell, _more, _accountButton);
        tools.VerticalAlignment = VerticalAlignment.Center;
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        tools.Margin = new Thickness(12, 0, 10, 0);

        // 9.5: кнопки окна как у Windows — во всю высоту полосы, вплотную к углу (мышь можно «бросить» в угол).
        _maxButton = WinButton(Icons.Maximize, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, I18n.T("win.maximize"));
        var winButtons = Ui.Row(0,
            WinButton(Icons.Minimize, () => WindowState = WindowState.Minimized, I18n.T("win.minimize")),
            _maxButton,
            WinButton(Icons.Close, Close, I18n.T("win.close"), "close"));
        winButtons.VerticalAlignment = VerticalAlignment.Top;
        _winButtons = winButtons;

        var titlebar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Height = TitleHeight, Background = Brushes.Transparent };
        titlebar.Children.Add(left);
        Grid.SetColumn(tabs, 1);
        titlebar.Children.Add(tabs);
        Grid.SetColumn(tools, 3);
        titlebar.Children.Add(tools);
        Grid.SetColumn(winButtons, 4);
        titlebar.Children.Add(winButtons);
        // Узкое окно: сначала прячется надпись «ModLaunch», потом имя в кнопке аккаунта — разделы видны всегда.
        titlebar.SizeChanged += (_, _) =>
        {
            var w = titlebar.Bounds.Width;
            _wordmark.IsVisible = w >= 1240;
            _accountCompact = w < 1120;
            RenderAccount();
        };
        titlebar.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is null && v.FindAncestorOfType<TextBox>(true) is null)
            {
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
            }
        };

        // ---- вкладки раздела и поиск
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
        _search.Width = 320;
        _search.Height = 34;
        _search.MinHeight = 34;
        _search.Padding = new Thickness(10, 5);
        _search.Classes.Add("top-search");
        _search.Classes.Add("store-search");
        _search.VerticalAlignment = VerticalAlignment.Center;

        _asideToggle = new Button { Classes = { "icon", "ghost" }, Content = Ui.Icon(Icons.Sidebar, 17), Width = 34, Height = 34, CornerRadius = new CornerRadius(8) };
        _asideToggle.Click += (_, _) => ToggleAside();
        ToolTip.SetTip(_asideToggle, I18n.T("top.aside.show") + " (Ctrl+I)");

        var crumbSep = new Border { Width = 1, Height = 16, Background = Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), IsVisible = false };
        _crumbSep = crumbSep;
        _crumbs.Margin = new Thickness(8, 0, 0, 0);
        var subLeft = new Panel { Children = { new ScrollViewer { Content = _subTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, _crumbs } };
        var subRight = Ui.Row(6, _search, _asideToggle);
        subRight.VerticalAlignment = VerticalAlignment.Center;
        var subnav = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = SubHeight, Margin = new Thickness(12, 0, 14, 0) };
        subnav.Children.Add(subLeft);
        Grid.SetColumn(subRight, 1);
        subnav.Children.Add(subRight);
        // Узкое окно: поиск сжимается, вкладки не налезают на него.
        subnav.SizeChanged += (_, _) => _search.Width = Math.Clamp(subnav.Bounds.Width * 0.26, 220, 340);
        var subnavHost = new Border { Child = subnav, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 0, 0) };

        // ---- тело: список игр (в библиотеке) и «лист» со страницей и панелью сведений
        var main = new DockPanel();
        _asideHost.BorderBrush = Ui.Res("Line");
        _asideHost.Background = Ui.Res("Layer");
        _asideHost.Child = new ScrollViewer { Content = new Border { Padding = new Thickness(20, 22, 20, 22), Child = _aside }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(_asideHost, Dock.Right);
        main.Children.Add(_asideHost);
        main.Children.Add(new Panel { ClipToBounds = true, Children = { _glow, _pageUnder, _page, _pageFx } });
        _sheet = new Border
        {
            Background = Ui.Res("Layer"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 1, 0, 0),
            ClipToBounds = true, Child = main,
        };
        _sidebarHost = new Border { Width = SidebarWidth, Child = _sidebar, IsVisible = false };
        _railHost = _sidebarHost;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        body.Children.Add(_sidebarHost);
        Grid.SetColumn(_sheet, 1);
        body.Children.Add(_sheet);

        // Правый клик по пустому месту страницы: навигация и быстрые переходы.
        Ctx.Attach(_page, () => Ctx.Menu(
            Ctx.Item(I18n.T("ctx.back"), Icons.Back, GoBack, _index > 0, "Alt+Left"),
            Ctx.Item(I18n.T("ctx.forward"), Icons.Forward, GoForward, _index < _history.Count - 1, "Alt+Right"),
            Ctx.Item(I18n.T("ctx.reload"), Icons.Refresh, () => _current?.Build(), gesture: "F5"),
            "-",
            Ctx.Item(I18n.T("v20.nav.store"), Icons.Bag, () => OpenSection(Section.Store)),
            Ctx.Item(I18n.T("v20.nav.library"), Icons.Gamepad, () => OpenSection(Section.Library)),
            Ctx.Item(I18n.T("v20.nav.community"), Icons.Users, () => OpenSection(Section.Community)),
            Ctx.Item(I18n.T("v20.nav.creator"), Icons.Creator, () => OpenSection(Section.Creator), gesture: "Ctrl+Shift+C"),
            "-",
            Ctx.Item(I18n.T("cp.title"), Icons.Grid, () => Navigate(() => new SettingsPage("panel")), gesture: "Ctrl+Shift+P"),
            Ctx.Item(I18n.T("look.title"), Icons.Palette, () => Navigate(() => new SettingsPage("look"))),
            Ctx.Item(I18n.T("nav.settings"), Icons.Settings, () => Navigate(() => new SettingsPage()), gesture: "Ctrl+OemComma")));
        SizeChanged += (_, _) =>
        {
            if (Look.AutoScale && Math.Abs(EffectiveScale() - _appliedScale) > 0.001) ApplyScale();
            UpdateAsideVisibility();
        };

        // ---- полоса платформы внизу: «Добавить игру», состояние, загрузки, «Друзья и чат»
        var dock = new FriendsDock();
        _friendsDock = dock;
        var addGame = new Button { Classes = { "foot" }, Content = Ui.Row(7, Ui.Icon(Icons.Plus, 15), _addText) };
        addGame.Click += (_, _) => Navigate(() => new AddGamePage());
        _addGameFoot = addGame;
        _downloads.Classes.Clear();
        _downloads.Classes.Add("foot");
        _downloads.Classes.Add("foot-dl");
        _downloads.Content = DownloadsStrip();
        var status = new StatusBar(addGame, _downloads, dock.TakePill());
        _status = status;

        var root = new Grid { RowDefinitions = new RowDefinitions($"{Px(TitleHeight)},{Px(SubHeight)},*,Auto") };
        root.Children.Add(titlebar);
        Grid.SetRow(subnavHost, 1);
        root.Children.Add(subnavHost);
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        Grid.SetRow(status, 3);
        root.Children.Add(status);
        var chrome = new Border { Background = Ui.Res("Rail"), Child = root };
        _root = chrome;

        // Всплывающие панели — у своих кнопок: уведомления и аккаунт под шапкой справа, загрузки — над полосой внизу.
        _accountPanel.HorizontalAlignment = HorizontalAlignment.Right;
        _accountPanel.VerticalAlignment = VerticalAlignment.Top;
        _accountPanel.Margin = new Thickness(0, TitleHeight + 4, 146, 0);
        _bellPanel.Margin = new Thickness(0, TitleHeight + 4, 190, 0);
        _downloadsPanel.HorizontalAlignment = HorizontalAlignment.Center;
        _downloadsPanel.VerticalAlignment = VerticalAlignment.Bottom;
        _downloadsPanel.Width = 440;

        var layers = new Panel();
        _layers = layers;
        layers.Classes.Set("anim", Look.Animations);
        layers.Children.Add(chrome);
        layers.Children.Add(_downloadsPanel);
        layers.Children.Add(_bellPanel);
        layers.Children.Add(_accountPanel);
        layers.Children.Add(dock);
        void AboveStatus()
        {
            var h = status.Height;
            _toasts.Margin = new Thickness(0, 0, 24, 16 + h);
            dock.Margin = new Thickness(0, 0, 12, 8 + h);
            _downloadsPanel.Margin = new Thickness(0, 0, 0, 8 + h);
        }
        AboveStatus();
        // Анимация «Скачать» — поверх страницы и панелей, но под экраном запуска и окнами.
        layers.Children.Add(_fxLayer);
        layers.Children.Add(_launchLayer);
        layers.Children.Add(_toasts);
        layers.Children.Add(_overlay);
        // 10.0: строки ЭЛТ и виньетка из конфига — поверх всего, нажатия проходят насквозь.
        layers.Children.Add(new ScreenFx());
        if (Splash.Enabled)
        {
            _splash = new Splash();
            layers.Children.Add(_splash);
        }
        return layers;
    }

    static string Px(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    StatusBar? _status;
    Button? _addGameFoot;
    readonly TextBlock _addText = new() { VerticalAlignment = VerticalAlignment.Center };
    bool _accountCompact;

    /// <summary>Закрыть всплывающие панели: загрузки, уведомления, аккаунт и друзей.</summary>
    public void CloseOverlays()
    {
        _downloadsPanel.IsVisible = false;
        _bellPanel.IsVisible = false;
        CloseAccountPanel();
        if (_friendsDock?.IsOpen == true) _friendsDock.Toggle(false);
    }

    // ---------------------------------------------------------------- загрузки в полосе внизу

    readonly TextBlock _dlText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 };
    readonly ProgressBar _dlBar = new() { Minimum = 0, Maximum = 1, Width = 150, Height = 4, MinHeight = 4, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };

    /// <summary>Середина нижней полосы, как «Загрузки» в Steam: что качается, общий прогресс, клик — менеджер загрузок.</summary>
    Control DownloadsStrip()
    {
        _dlRing.Width = _dlRing.Height = 26;
        _dlBadge.Margin = new Thickness(0, -2, -2, 0);
        var icon = new Panel { Width = 26, Height = 26, Children = { _dlRing, Ui.Icon(Icons.Download, 15), _dlBadge } };
        return Ui.Row(9, icon, _dlText, _dlBar);
    }

    /// <summary>Текст и полоса в середине нижней полосы (вызывается вместе с перерисовкой панели загрузок).</summary>
    void RenderFooterDownloads()
    {
        var active = Jobs.All.Where(j => j.Active).ToList();
        if (active.Count == 0)
        {
            var done = Jobs.All.Count(j => j.Status == JobStatus.Done);
            _dlText.Text = done > 0 ? I18n.T("v20.foot.dl.done", ("n", done)) : I18n.T("v20.foot.dl");
            _dlText.Foreground = Ui.Res("Muted");
            _dlBar.IsVisible = false;
            return;
        }
        var running = active.Where(j => j.Status == JobStatus.Running).ToList();
        var first = running.FirstOrDefault() ?? active[0];
        _dlText.Text = active.Count == 1
            ? I18n.T("v20.foot.dl.one", ("name", first.Title))
            : I18n.T("v20.foot.dl.many", ("name", first.Title), ("n", active.Count - 1));
        _dlText.Foreground = Ui.Res("Text");
        double total = running.Sum(j => j.Total > 0 ? j.Total : 0), got = running.Sum(j => j.Total > 0 ? j.Received : 0);
        var ratio = total > 0 ? got / total : running.Count > 0 ? running.Average(j => Math.Clamp(j.Ratio, 0, 1)) : 0;
        _dlBar.IsVisible = true;
        _dlBar.Value = Math.Clamp(ratio, 0, 1);
    }

    // ---------------------------------------------------------------- перерисовка рамки

    /// <summary>
    /// Перерисовать рамку окна: разделы, вкладки раздела, список игр, аккаунт и «вышла новая версия».
    /// (Имя осталось с тех пор, когда слева была узкая панель со значками.)
    /// </summary>
    void RenderRail()
    {
        _addText.Text = I18n.T("v20.foot.add");
        RenderPlatformTabs();
        RenderSubTabs();
        UpdateSidebar();
        RenderAccount();
        _updatePill.IsVisible = Setup.Updater.Available;
        if (Setup.Updater.Latest is { } latest) _updatePill.Content = Ui.Row(6, Ui.Icon(Icons.Sparkles, 14), new TextBlock { Text = I18n.T("upd.app.pill", ("version", latest.Version)), VerticalAlignment = VerticalAlignment.Center });
    }
}
