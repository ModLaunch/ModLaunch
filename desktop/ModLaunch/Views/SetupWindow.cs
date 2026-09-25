using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
        Width = 760;
        Height = 480;
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

        // Слева — картинка и возможности, справа — шаги установки.
        var features = Ui.Col(14);
        for (var i = 1; i <= 5; i++)
            features.Children.Add(Ui.Col(2, Ui.Text(T($"feat.{i}.title"), "h3", color: Brushes.White), Ui.Text(T($"feat.{i}.text"), "small", color: Ui.Hex("#C9CFDB"), wrap: true)));
        var side = new Panel
        {
            Width = 280,
            Children =
            {
                new Image { Source = Images.Asset("banner-hero.jpg", 800), Stretch = Stretch.UniformToFill },
                new Border { Background = new SolidColorBrush(Color.Parse("#D90F1116")) },
                new StackPanel
                {
                    Margin = new Thickness(28, 30),
                    Spacing = 24,
                    Children =
                    {
                        Ui.Row(10, new Image { Source = Images.Asset("icon.png", 64), Width = 32, Height = 32 },
                            new TextBlock { FontSize = 22, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Inlines = { new Avalonia.Controls.Documents.Run("Mod"), new Avalonia.Controls.Documents.Run("Launch") { Foreground = Ui.Res("Brand2") } } }),
                        features,
                    },
                },
            },
        };
        side.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        _body.Margin = new Thickness(36, 40, 36, 30);
        _body.PointerPressed += (_, e) => { if (e.Source == _body && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(side);
        Grid.SetColumn(_body, 1);
        grid.Children.Add(_body);
        Content = new Panel { Children = { grid, close } };

        if (mode == SetupMode.Update) Opened += (_, _) => Run();
        else Show(mode == SetupMode.Uninstall ? UninstallView() : Welcome());
    }

    void Show(Control c) => _body.Content = c;

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
        col.Children.Add(Ui.Col(6, Ui.Text(T("welcome.folder"), "small muted"), row));

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

    async void Run()
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8 };
        var step = Ui.Text(T("step.close"), "muted");
        Show(Ui.Col(16,
            Ui.Text(_mode == SetupMode.Update && Updater.Latest is null ? T("update.title", ("version", Http.Version)) : T("progress.install"), "h1"),
            _mode == SetupMode.Update ? Ui.Text(T("update.lead"), "muted", wrap: true) : Ui.Text(T("progress.hint"), "muted", wrap: true),
            bar, step));
        try
        {
            var progress = new Progress<(string Step, double Ratio)>(p =>
            {
                step.Text = T("step." + p.Step);
                bar.Value = p.Ratio;
            });
            var desktop = _mode == SetupMode.Update
                ? File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ModLaunch.lnk"))
                : _desktop;
            _result = await Installer.Install(_target, desktop, progress);
            if (_mode == SetupMode.Update) { Installer.Launch(_result.Exe); Close(); return; }
            Show(Done());
        }
        catch (Exception e)
        {
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
        if (_launch) Dispatcher.UIThread.Post(() => { Installer.Launch(_result!.Exe); Close(); }, DispatcherPriority.Background);
        return Ui.Col(16,
            Ui.Icon(Icons.Check, 48, Ui.Res("Good")),
            Ui.Text(_result!.Updated ? T("done.titleUpdate", ("version", Http.Version)) : T("done.title"), "h1"),
            Ui.Text(_desktop ? T("done.textDesktop") : T("done.textStart"), "muted", wrap: true),
            Ui.Row(10, launch, Ui.Button(T("done.close"), Close)));
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
