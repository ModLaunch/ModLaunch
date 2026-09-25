using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Setup;

namespace ModLaunch.Views;

/// <summary>Окно установки, обновления и удаления (та же программа, режим по имени файла или ключу).</summary>
public sealed class SetupWindow : Window
{
    readonly SetupMode _mode;
    string _target = Installer.SuggestedDir();
    bool _desktop = true, _launch = true, _wipe;
    readonly ContentControl _body = new();
    Installer.Result? _result;

    static string T(string key, params (string, object)[] args) => I18n.T("setup." + key, args);

    public SetupWindow(SetupMode mode)
    {
        _mode = mode;
        Title = mode == SetupMode.Uninstall ? T("window.titleUninstall") : T("window.title");
        Width = 940;
        Height = 580;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
        try { Icon = new WindowIcon(Images.Asset("icon.png")); } catch { }

        var close = new Button { Classes = { "win", "close" }, Content = Ui.Icon(Icons.Close, 14) };
        close.Click += (_, _) => Close();
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 8, 8, 0);

        // Слева — «стена» настоящих обложек игр, медленно плывущая вверх, поверх — логотип и что умеет программа.
        var features = Ui.Col(12);
        foreach (var (i, icon) in new[] { (1, Icons.Download), (2, Icons.Layers), (3, Icons.Sparkles), (4, Icons.Users), (5, Icons.Shield) })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            row.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top, Background = Ui.Hex("#332A2350"), BorderBrush = Ui.Hex("#557C5CFF"), BorderThickness = new Thickness(1), Child = Ui.Icon(icon, 16, Ui.Res("Brand2")) });
            var text = Ui.Col(1, Ui.Text(T($"feat.{i}.title"), "h3", color: Brushes.White), Ui.Text(T($"feat.{i}.text"), "small", color: Ui.Hex("#B9C0CE"), wrap: true));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            features.Children.Add(row);
        }
        var side = new Panel
        {
            Width = 340, ClipToBounds = true,
            Children =
            {
                CoverWall(),
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#F20F1116"), 0), new GradientStop(Color.Parse("#C80F1116"), 0.45), new GradientStop(Color.Parse("#F50F1116"), 1) } } },
                new StackPanel
                {
                    Margin = new Thickness(30, 32),
                    Spacing = 26,
                    Children =
                    {
                        Ui.Row(10, new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = new Image { Source = Images.Asset("icon.png", 96) } },
                            new TextBlock { FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Inlines = { new Avalonia.Controls.Documents.Run("Mod"), new Avalonia.Controls.Documents.Run("Launch") { Foreground = Ui.Res("Brand2") } } },
                            new Border { Background = Ui.Hex("#332A2350"), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = Http.Version, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Brand2") } }),
                        features,
                    },
                },
            },
        };
        side.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        _body.Margin = new Thickness(44, 36, 44, 30);
        _body.PointerPressed += (_, e) => { if (e.Source == _body && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        var right = new DockPanel();
        _steps.Margin = new Thickness(44, 26, 60, 0);
        DockPanel.SetDock(_steps, Dock.Top);
        right.Children.Add(_steps);
        right.Children.Add(_body);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(side);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = new Panel { Children = { grid, close } };

        if (mode == SetupMode.Update) Opened += (_, _) => Run();
        else Show(mode == SetupMode.Uninstall ? UninstallView() : Welcome());
    }

    readonly StackPanel _steps = new() { Orientation = Orientation.Horizontal, Spacing = 10 };

    /// <summary>Сменить экран: с шагами сверху и мягким въездом.</summary>
    void Show(Control c, int step = 1)
    {
        _body.Content = c;
        RenderSteps(step);
        Animate.From(c, "translateX(24px)", 360);
    }

    void RenderSteps(int current)
    {
        _steps.Children.Clear();
        if (_mode != SetupMode.Install) return;
        var names = new[] { T("steps.setup"), T("steps.install"), T("steps.done") };
        for (var i = 0; i < names.Length; i++)
        {
            var n = i + 1;
            var done = n < current;
            var active = n == current;
            var circle = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                Background = done || active ? Ui.Res("Brand") : Ui.Res("Surface3"),
                Child = done ? Ui.Icon(Icons.Check, 12, Brushes.White) : new TextBlock { Text = n.ToString(), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = active ? Brushes.White : Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            _steps.Children.Add(Ui.Row(8, circle, new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal, Foreground = active ? Ui.Res("Text") : Ui.Res("Muted") }));
            if (n < names.Length) _steps.Children.Add(new Border { Width = 30, Height = 2, CornerRadius = new CornerRadius(1), Background = done ? Ui.Res("Brand") : Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Center });
        }
    }

    /// <summary>Три колонки настоящих обложек, которые медленно плывут вверх (бесконечно, по кругу).</summary>
    static Control CoverWall()
    {
        var games = Games.GameCatalog.All.Where(g => g.SteamAppId > 0).ToList();
        var canvas = new Canvas { Width = 340 };
        for (var c = 0; c < 3; c++)
        {
            var column = new StackPanel { Spacing = 10, Width = 104 };
            var order = games.Skip(c * 5).Concat(games.Take(c * 5)).ToList();
            foreach (var g in order.Concat(order)) // дважды — чтобы петля была незаметной
                column.Children.Add(new Border { Width = 104, Height = 156, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.GameImage(g, 256, art: Images.Art.Cover) });
            Canvas.SetLeft(column, 6 + c * 112);
            canvas.Children.Add(column);
            var loop = order.Count * 166.0; // высота одного круга
            var speed = 14 + c * 4.0;       // колонки плывут с разной скоростью — «параллакс»
            var offset = -c * 60.0;
            var transform = new TranslateTransform(0, offset);
            column.RenderTransform = transform;
            if (!Program.Screenshot)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                void Tick(TimeSpan _)
                {
                    transform.Y = offset - clock.Elapsed.TotalSeconds * speed % loop;
                    if (column.IsAttachedToVisualTree()) TopLevel.GetTopLevel(column)?.RequestAnimationFrame(Tick);
                }
                column.AttachedToVisualTree += (_, _) => TopLevel.GetTopLevel(column)?.RequestAnimationFrame(Tick);
            }
        }
        return canvas;
    }

    Control Welcome()
    {
        var (kind, version) = Installer.Inspect(_target);
        var update = kind == Installer.TargetKind.Ours;
        var col = Ui.Col(16,
            Ui.Text(update ? T("welcome.titleUpdate") : T("welcome.title"), "h1"),
            Ui.Text(update ? (version is null ? T("welcome.updateLegacy", ("version", Http.Version)) : T("welcome.update", ("from", version), ("version", Http.Version))) : T("welcome.lead"), "muted", wrap: true));

        var folder = new TextBox { Text = _target, IsReadOnly = true };
        var change = Ui.Button(T("welcome.change"), async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { AllowMultiple = false });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is string dir) { _target = Installer.Normalize(dir); Show(Welcome()); }
        });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(folder);
        Grid.SetColumn(change, 1);
        row.Children.Add(change);
        var space = Ui.Text(FreeSpace(_target), "small muted");
        col.Children.Add(Ui.Col(6, Ui.Text(T("welcome.folder"), "small muted"), row, space));

        var desktop = new CheckBox { Content = T("welcome.desktop"), IsChecked = _desktop };
        desktop.IsCheckedChanged += (_, _) => _desktop = desktop.IsChecked == true;
        var launch = new CheckBox { Content = T("welcome.launch"), IsChecked = _launch };
        launch.IsCheckedChanged += (_, _) => _launch = launch.IsChecked == true;
        col.Children.Add(Ui.Col(2, desktop, launch));

        var go = Ui.Button(update ? T("welcome.updateBtn") : T("welcome.install"), Run, "primary", Icons.Download);
        go.FontSize = 16;
        go.Padding = new Thickness(28, 12);
        var portable = Ui.Button(T("welcome.portable"), () =>
        {
            Program.StartMain([]);
            Close();
        }, "ghost");
        col.Children.Add(Ui.Row(10, go, portable));
        col.Children.Add(Ui.Text(T("welcome.terms", ("button", update ? T("welcome.updateBtn") : T("welcome.install"))) + " " + T("welcome.termsLink").ToLowerInvariant(), "small muted", wrap: true));
        return col;
    }

    static string FreeSpace(string target)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(target));
            if (root is null) return "";
            var free = new DriveInfo(root).AvailableFreeSpace / 1024.0 / 1024 / 1024;
            return T("welcome.space", ("need", 80), ("free", free.ToString("0.#")));
        }
        catch { return ""; }
    }

    async void Run()
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 10, CornerRadius = new CornerRadius(5) };
        var step = Ui.Text(T("step.close"), "muted");
        var percent = new TextBlock { Text = "0%", FontSize = 56, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2") };
        var tip = Ui.Text(T("tip.1"), "muted", wrap: true);
        var tipBox = new Border { Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12),
            Child = Ui.Row(12, Ui.Icon(Icons.Sparkles, 18, Ui.Res("Brand2")), tip) };
        var tipIndex = 1;
        var tips = new DispatcherTimer(TimeSpan.FromSeconds(3.5), DispatcherPriority.Background, (_, _) =>
        {
            tipIndex = tipIndex % 5 + 1;
            tip.Text = T("tip." + tipIndex);
            Animate.From(tip, "translateY(8px)", 300);
        });
        tips.Start();
        Show(Ui.Col(18,
            Ui.Text(_mode == SetupMode.Update && Updater.Latest is null ? T("update.title", ("version", Http.Version)) : T("progress.install"), "h1"),
            _mode == SetupMode.Update ? Ui.Text(T("update.lead"), "muted", wrap: true) : Ui.Text(T("progress.hint"), "muted", wrap: true),
            percent, bar, step, tipBox), 2);
        try
        {
            var progress = new Progress<(string Step, double Ratio)>(p =>
            {
                step.Text = T("step." + p.Step);
                bar.Value = p.Ratio;
                percent.Text = $"{p.Ratio * 100:0}%";
            });
            var desktop = _mode == SetupMode.Update
                ? File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ModLaunch.lnk"))
                : _desktop;
            _result = await Installer.Install(_target, desktop, progress);
            tips.Stop();
            if (_mode == SetupMode.Update) { Installer.Launch(_result.Exe); Close(); return; }
            Show(Done(), 3);
        }
        catch (Exception e)
        {
            tips.Stop();
            var code = e is SetupError s ? s.Code : "generic";
            var text = I18n.Has("setup.error." + code) ? T("error." + code, ("message", e.Message)) : T("error.generic", ("message", e.Message));
            Show(Ui.Col(16, Ui.Text(T("error.title"), "h1"), Ui.Text(text, "muted", wrap: true),
                Ui.Row(10, Ui.Button(T("error.retry"), Run, "primary", Icons.Refresh), Ui.Button(T("error.back"), () => Show(Welcome())))));
        }
    }

    Control Done()
    {
        var launch = Ui.Button(T("done.launch"), () => { Installer.Launch(_result!.Exe); Close(); }, "primary", Icons.Play);
        launch.FontSize = 16;
        launch.Padding = new Thickness(28, 12);
        if (_launch) DispatcherTimer.RunOnce(() => { Installer.Launch(_result!.Exe); Close(); }, TimeSpan.FromSeconds(1.6));
        var check = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(38), Background = Ui.Hex("#1F5BD68F"), BorderBrush = Ui.Res("Good"), BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.Icon(Icons.Check, 36, Ui.Res("Good")) };
        Animate.From(check, "scale(0.3)", 520, 60, new Avalonia.Animation.Easings.BackEaseOut());
        var covers = Ui.Row(8);
        foreach (var g in Games.GameCatalog.All.Where(g => g.SteamAppId > 0).Take(6))
            covers.Children.Add(new Border { Width = 58, Height = 87, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.GameImage(g, 128, art: Images.Art.Cover) });
        return Ui.Col(16,
            check,
            Ui.Text(_result!.Updated ? T("done.titleUpdate", ("version", Http.Version)) : T("done.title"), "h1"),
            Ui.Text(_desktop ? T("done.textDesktop") : T("done.textStart"), "muted", wrap: true),
            Ui.Row(10, launch, Ui.Button(T("done.close"), Close)),
            Ui.Col(8, Ui.Text(T("done.games"), "small muted"), covers));
    }

    Control UninstallView()
    {
        var wipe = new CheckBox { Content = T("uninstall.wipe"), IsChecked = _wipe };
        wipe.IsCheckedChanged += (_, _) => _wipe = wipe.IsChecked == true;
        var go = Ui.Button(T("uninstall.go"), () =>
        {
            try
            {
                Installer.Uninstall(_wipe);
                Show(Ui.Col(16, Ui.Text(T("uninstall.done"), "h1"), Ui.Text(T("uninstall.bye"), "muted", wrap: true), Ui.Button(T("done.close"), Close, "primary")));
            }
            catch (Exception e)
            {
                Show(Ui.Col(16, Ui.Text(T("uninstall.error"), "h1"), Ui.Text(e.Message, "muted", wrap: true), Ui.Button(T("done.close"), Close)));
            }
        }, "primary", Icons.Trash);
        return Ui.Col(16, Ui.Text(T("uninstall.title"), "h1"), Ui.Text(T("uninstall.lead"), "muted", wrap: true), wipe,
            Ui.Row(10, go, Ui.Button(T("uninstall.cancel"), Close)));
    }
}
