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
    readonly Button _back, _forward, _downloads, _homeButton, _settingsButton;
    readonly Panel _overlay = new() { IsVisible = false };
    readonly StackPanel _toasts = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 24, 24) };
    readonly Border _downloadsPanel;
    readonly StackPanel _downloadsList = new() { Spacing = 10 };
    readonly Border _dlBadge = new() { IsVisible = false };

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

        _downloads = new Button
        {
            Classes = { "icon" },
            Content = new Panel { Children = { Ui.Icon(Icons.Download, 18), _dlBadge } },
        };
        _dlBadge.Width = 10; _dlBadge.Height = 10; _dlBadge.CornerRadius = new CornerRadius(5);
        _dlBadge.Background = Ui.Res("Brand2"); _dlBadge.HorizontalAlignment = HorizontalAlignment.Right; _dlBadge.VerticalAlignment = VerticalAlignment.Top;
        _dlBadge.Margin = new Thickness(0, -4, -4, 0);
        _downloads.Click += (_, _) => _downloadsPanel!.IsVisible = !_downloadsPanel.IsVisible;
        ToolTip.SetTip(_downloads, I18n.T("dl.button"));

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

        Content = BuildLayout();

        I18n.Changed += () => { RebuildChrome(); _current?.Build(); };
        AppState.Changed += OnStateChanged;
        Jobs.Changed += _ => RenderDownloads();
        Jobs.Finished += OnJobFinished;
        KeyDown += OnKey;
        Features.Launcher.Exited += OnGameExit;
        Features.Nxm.Received += OnExternal;

        Navigate(() => new HomePage());
        RenderRail();
        RenderDownloads();
        if (!Program.Screenshot) _ = StartUp();
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
        var top = Ui.Col(14, logo, new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(14, 0) }, _homeButton);
        top.Margin = new Thickness(0, 14, 0, 10);
        top.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(top, Dock.Top);
        var bottom = Ui.Col(10, _settingsButton);
        bottom.Margin = new Thickness(0, 10, 0, 16);
        bottom.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(bottom, Dock.Bottom);
        rail.Children.Add(top);
        rail.Children.Add(bottom);
        rail.Children.Add(new ScrollViewer { Content = _railGames, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
        var railBorder = new Border { Child = rail, BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(0, 0, 1, 0) };

        // Верхняя строка: тянется за неё всё окно.
        _search.Watermark = I18n.T("search.home");
        _search.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Search, 16, Ui.Res("Muted")) };
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) _current?.Search(_search.Text ?? ""); };

        var brand = Ui.Row(10,
            new Image { Source = Images.Asset("icon.png", 64), Width = 28, Height = 28 },
            new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center, FontSize = 20, FontWeight = FontWeight.Bold,
                Inlines = { new Avalonia.Controls.Documents.Run("Mod"), new Avalonia.Controls.Documents.Run("Launch") { Foreground = Ui.Res("Brand2") } },
            },
            new Border { Width = 1, Height = 24, Background = Ui.Res("Line"), Margin = new Thickness(6, 0) },
            _title);

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
        var dlWrap = new Border { Child = _downloads, Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
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
        layers.Children.Add(root);
        layers.Children.Add(_downloadsPanel);
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
        foreach (var g in AppState.Games)
        {
            var art = g.Def.Art is null ? null : Images.Asset(g.Def.Art, 120);
            Control face = art is not null
                ? new Image { Source = art, Stretch = Stretch.UniformToFill }
                : new Border { Background = Ui.Hex(g.Def.Accent), Child = new TextBlock { Text = g.Def.ShortName[..1], FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Black } };
            var status = Ui.Dot(g.Status == Detect.Found ? Ui.Res("Good") : Ui.Res("Faint"), 9);
            status.HorizontalAlignment = HorizontalAlignment.Right;
            status.VerticalAlignment = VerticalAlignment.Bottom;
            status.Margin = new Thickness(0, 0, 2, 2);
            var id = g.Def.Id;
            var b = new Button
            {
                Classes = { "rail" },
                Content = new Panel { Children = { new Border { CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = face }, status } },
            };
            if (_current?.GameId == id) b.Classes.Add("active");
            b.Click += (_, _) => Navigate(() => new GamePage(id));
            ToolTip.SetTip(b, g.Def.Name);
            _railGames.Children.Add(b);
        }
        _homeButton.Classes.Set("active", _current is HomePage);
        _settingsButton.Classes.Set("active", _current is SettingsPage);
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
        page.Build();
        _page.Content = page;
        _title.Text = page.Title;
        _search.Text = "";
        _search.Watermark = page.SearchHint;
        _back.IsEnabled = _index > 0;
        _forward.IsEnabled = _index < _history.Count - 1;
        _downloadsPanel.IsVisible = false;
        RenderRail();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left) GoBack();
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Right) GoForward();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.K) _search.Focus();
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.J) _downloadsPanel.IsVisible = !_downloadsPanel.IsVisible;
        else if (e.Key == Key.Escape) { if (_overlay.IsVisible) CloseDialog(); else _downloadsPanel.IsVisible = false; }
    }

    async Task StartUp()
    {
        await AppState.DetectAll();
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

    // ---------------------------------------------------------------- загрузки и уведомления

    void RenderDownloads()
    {
        _dlBadge.IsVisible = Jobs.Running > 0;
        _downloadsList.Children.Clear();
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

    void OnJobFinished(Job job)
    {
        if (job.Status == JobStatus.Done) Toast($"{job.Title} — {I18n.T("dl.done").ToLowerInvariant()}");
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
        DispatcherTimer.RunOnce(() => _toasts.Children.Remove(t), TimeSpan.FromSeconds(bad ? 8 : 4));
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
            RenderTransform = new ScaleTransform(0.96, 0.96),
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
