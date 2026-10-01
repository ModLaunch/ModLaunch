using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Setup;

namespace ModLaunch.Views;

/// <summary>
/// Окно установки, обновления и удаления (та же программа, режим по имени файла или ключу).
/// Задумано так, чтобы ошибиться было нечем: одна большая кнопка «Установить», папка уже выбрана
/// (Programs\ModLaunch), старая версия находится и переезжает сама.
/// </summary>
public sealed class SetupWindow : Window
{
    readonly SetupMode _mode;
    string _target = Installer.SuggestedDir();
    bool _desktop = true, _launch = true, _wipe;
    readonly ContentControl _body = new();
    readonly Panel _root = new();
    Installer.Result? _result;

    // Что лежит в выбранной папке и где осталась старая установка; для снимков экрана подставляется вручную.
    Installer.TargetKind _kind;
    string? _version;
    List<string> _legacy = [];
    bool _preview;

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

        var minimize = new Button { Classes = { "win" }, Content = Ui.Icon(Icons.Minimize, 14) };
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        ToolTip.SetTip(minimize, T("window.min"));
        var close = new Button { Classes = { "win", "close" }, Content = Ui.Icon(Icons.Close, 14) };
        close.Click += (_, _) => Close();
        ToolTip.SetTip(close, T("window.close"));
        var buttons = Ui.Row(0, minimize, close);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.VerticalAlignment = VerticalAlignment.Top;
        buttons.Margin = new Thickness(0, 8, 8, 0);

        // Слева — «стена» настоящих обложек игр, медленно плывущая вверх, поверх — логотип и что умеет программа.
        var features = Ui.Col(12);
        foreach (var (i, icon) in new[] { (1, Icons.Download), (2, Icons.Layers), (3, Icons.Gamepad), (6, Icons.Code), (4, Icons.Users), (5, Icons.Shield) })
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
                    GradientStops = { new GradientStop(Color.Parse("#F20F1116"), 0), new GradientStop(Color.Parse("#DC0F1116"), 0.62), new GradientStop(Color.Parse("#A00F1116"), 1) } } },
                new Border { IsHitTestVisible = false, Background = new RadialGradientBrush { Center = new RelativePoint(0, 0, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
                    RadiusX = new RelativeScalar(1, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#407C5CFF"), 0), new GradientStop(Color.Parse("#007C5CFF"), 1) } } },
                new StackPanel
                {
                    Margin = new Thickness(30, 32),
                    Spacing = 28,
                    Children =
                    {
                        Ui.Row(10, new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = new Image { Source = Images.Asset("icon.png", 96) } },
                            new TextBlock { FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Inlines = { new Avalonia.Controls.Documents.Run("Mod"), new Avalonia.Controls.Documents.Run("Launch") { Foreground = Ui.Res("Brand2") } } },
                            new Border { Background = Ui.Hex("#332A2350"), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = Http.Version, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Brand2") } }),
                        features,
                    },
                },
            },
        };
        side.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        _body.Margin = new Thickness(44, 30, 44, 30);
        _body.PointerPressed += (_, e) => { if (e.Source == _body && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        var right = new DockPanel();
        _steps.Margin = new Thickness(44, 26, 90, 0);
        DockPanel.SetDock(_steps, Dock.Top);
        right.Children.Add(_steps);
        right.Children.Add(_body);
        // Мягкое фиолетовое свечение в правом верхнем углу — на фоне окна.
        var glow = new Border { IsHitTestVisible = false, Background = new RadialGradientBrush { Center = new RelativePoint(1, 0, RelativeUnit.Relative), GradientOrigin = new RelativePoint(1, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#287C5CFF"), 0), new GradientStop(Color.Parse("#007C5CFF"), 1) } } };
        var rightPane = new Panel { Children = { glow, right } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(side);
        Grid.SetColumn(rightPane, 1);
        grid.Children.Add(rightPane);
        _root.Children.Add(grid);
        _root.Children.Add(buttons);
        Content = _root;

        if (mode == SetupMode.Update) Opened += (_, _) => Run();
        else Show(mode == SetupMode.Uninstall ? UninstallView() : Welcome());
    }

    /// <summary>Для снимков экрана: показать нужный экран, ничего не устанавливая.</summary>
    public void Preview(string screen)
    {
        _preview = true;
        _target = Installer.DefaultDir;
        var old = Path.Combine(Path.GetDirectoryName(_target)!, "modhub");
        _result = new Installer.Result(_target, Path.Combine(_target, Installer.ExeName), false, null);
        switch (screen)
        {
            case "welcome-update": _kind = Installer.TargetKind.Ours; _version = "7.0.0"; Show(Welcome()); break;
            case "welcome-legacy": _kind = Installer.TargetKind.Missing; _legacy = [old]; Show(Welcome()); break;
            case "progress": RunView(0.47, "copy"); break;
            case "done": _result = _result with { Moved = old }; _launch = false; Show(Done(), 3); break;
            case "error": ShowError(new SetupError("SETUP_BUSY")); break;
            case "license": Show(Welcome()); ShowLicense(); break;
            case "uninstall": Show(UninstallView()); break;
            case "uninstalled": Show(Ui.Col(16, Ui.Text(T("uninstall.done"), "h1"), Ui.Text(T("uninstall.bye"), "muted", wrap: true), Ui.Button(T("done.close"), Close, "primary"))); break;
            default: _kind = Installer.TargetKind.Missing; Show(Welcome()); break;
        }
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

    /// <summary>Что сейчас в выбранной папке и какая старая установка переедет.</summary>
    void Inspect()
    {
        if (_preview) return;
        (_kind, _version) = Installer.Inspect(_target);
        _legacy = Installer.FindLegacy(_target);
    }

    static Border IconBox(string icon, string bg, string border, IBrush color, double size = 38) =>
        new() { Width = size, Height = size, CornerRadius = new CornerRadius(11), Background = Ui.Hex(bg), BorderBrush = Ui.Hex(border), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Child = Ui.Icon(icon, 18, color) };

    Control Welcome()
    {
        Inspect();
        var update = _kind == Installer.TargetKind.Ours;
        var col = Ui.Col(14,
            Ui.Text(update ? T("welcome.titleUpdate") : T("welcome.title"), "h1"),
            Ui.Text(update ? (_version is null ? T("welcome.updateLegacy", ("version", Http.Version)) : T("welcome.update", ("from", _version), ("version", Http.Version))) : T("welcome.lead"), "muted", wrap: true));

        // Папка установки одной карточкой: куда встанет, сколько места, кнопка «Изменить».
        var change = Ui.Button(T("welcome.change"), async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is string dir) { _target = Installer.Normalize(dir); Show(Welcome()); }
        });
        change.VerticalAlignment = VerticalAlignment.Center;
        var where = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        where.Children.Add(IconBox(Icons.Folder, "#332A2350", "#557C5CFF", Ui.Res("Brand2")));
        var path = Ui.Col(2, Ui.Text(T("welcome.where"), "small muted"), Ui.Text(_target, "h3"), Ui.Text(FreeSpace(_target), "small muted"));
        path.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(path, 1);
        where.Children.Add(path);
        Grid.SetColumn(change, 2);
        where.Children.Add(change);
        col.Children.Add(new Border { Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12), Child = where });

        // Нашлась старая установка (например, Programs\modhub) — говорим, что с ней будет.
        if (_legacy.Count > 0)
        {
            var note = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            note.Children.Add(IconBox(Icons.Info, "#2A2350", "#557C5CFF", Ui.Res("Brand2"), 30));
            var text = Ui.Col(2, Ui.Text(T("welcome.legacyTitle"), "h3"), Ui.Text(T("welcome.legacy", ("path", _legacy[0])), "small muted", wrap: true));
            Grid.SetColumn(text, 1);
            note.Children.Add(text);
            col.Children.Add(new Border { Background = Ui.Hex("#142A2350"), BorderBrush = Ui.Hex("#447C5CFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10), Child = note });
        }

        var desktop = new CheckBox { Content = T("welcome.desktop"), IsChecked = _desktop };
        desktop.IsCheckedChanged += (_, _) => _desktop = desktop.IsChecked == true;
        var launch = new CheckBox { Content = T("welcome.launch"), IsChecked = _launch };
        launch.IsCheckedChanged += (_, _) => _launch = launch.IsChecked == true;
        col.Children.Add(Ui.Row(22, desktop, launch));

        var go = Ui.Button(update ? T("welcome.updateBtn") : T("welcome.install"), Run, "primary", Icons.Download);
        go.FontSize = 16;
        go.Padding = new Thickness(30, 13);
        var portable = Ui.Button(T("welcome.portable"), () =>
        {
            Program.StartMain([]);
            Close();
        }, "ghost");
        col.Children.Add(Ui.Row(10, go, portable));

        var link = Ui.Text(T("welcome.termsLink").ToLowerInvariant(), "small", color: Ui.Res("Brand2"));
        link.TextDecorations = TextDecorations.Underline;
        link.Cursor = new Cursor(StandardCursorType.Hand);
        link.PointerPressed += (_, _) => ShowLicense();
        col.Children.Add(Ui.Row(4, Ui.Text(T("welcome.terms", ("button", update ? T("welcome.updateBtn") : T("welcome.install"))), "small muted"), link));
        return col;
    }

    /// <summary>Условия использования поверх окна: текст лежит внутри программы.</summary>
    void ShowLicense()
    {
        string text;
        try
        {
            using var reader = new StreamReader(AssetLoader.Open(new Uri("avares://ModLaunch/Assets/license.txt")));
            text = reader.ReadToEnd().TrimStart('﻿');
        }
        catch { text = T("license.title"); }
        Control? overlay = null;
        void Dismiss() { if (overlay is not null) _root.Children.Remove(overlay); }
        var card = new Border
        {
            Width = 620, Height = 440, Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(26, 22),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    Docked(Ui.Text(T("license.title"), "h2"), Dock.Top, new Thickness(0, 0, 0, 12)),
                    Docked(Ui.Button(T("license.close"), Dismiss, "primary"), Dock.Bottom, new Thickness(0, 14, 0, 0)),
                    new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Res("Muted"), Margin = new Thickness(0, 0, 14, 0) } },
                },
            },
        };
        overlay = new Panel { Background = Ui.Hex("#CC07080B"), Children = { card } };
        overlay.PointerPressed += (_, e) => { if (e.Source == overlay) Dismiss(); };
        _root.Children.Add(overlay);
        Animate.From(card, "scale(0.96)", 260);
    }

    static Control Docked(Control c, Dock dock, Thickness margin)
    {
        DockPanel.SetDock(c, dock);
        c.Margin = margin;
        if (c is Button) c.HorizontalAlignment = HorizontalAlignment.Left;
        return c;
    }

    static string FreeSpace(string target)
    {
        try
        {
            if (Installer.FreeBytes(target) is not long bytes) return "";
            return T("welcome.space", ("need", Math.Max(1, Installer.SourceSize / 1048576)), ("free", (bytes / 1024.0 / 1024 / 1024).ToString("0.#")));
        }
        catch { return ""; }
    }

    /// <summary>Экран установки: проценты, полоса, что делается сейчас и подсказки.</summary>
    (ProgressBar Bar, TextBlock Step, TextBlock Percent, TextBlock Size, DispatcherTimer Tips) RunView()
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 12, CornerRadius = new CornerRadius(6) };
        var step = Ui.Text(T("step.close"), "muted");
        var size = Ui.Text("", "small muted");
        var percent = new TextBlock { Text = "0%", FontSize = 60, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2") };
        var tip = Ui.Text(T("tip.1"), "muted", wrap: true);
        var tipBox = new Border { Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12),
            Child = Ui.Row(12, Ui.Icon(Icons.Sparkles, 18, Ui.Res("Brand2")), tip) };
        var tipIndex = 1;
        var tips = new DispatcherTimer(TimeSpan.FromSeconds(3.5), DispatcherPriority.Background, (_, _) =>
        {
            tipIndex = tipIndex % 7 + 1;
            tip.Text = T("tip." + tipIndex);
            Animate.From(tip, "translateY(8px)", 300);
        });
        if (!_preview) tips.Start();
        var lead = _mode == SetupMode.Update ? T("update.lead") : T("progress.hint") + (_legacy.Count > 0 ? " " + T("progress.moveHint") : "");
        Show(Ui.Col(16,
            Ui.Text(_mode == SetupMode.Update && Updater.Latest is null ? T("update.title", ("version", Http.Version)) : T("progress.install"), "h1"),
            Ui.Text(lead, "muted", wrap: true),
            percent, bar, Ui.Col(2, step, size), tipBox), 2);
        return (bar, step, percent, size, tips);
    }

    void RunView(double ratio, string stepName) => Progress(RunView(), (stepName, ratio));

    static void Progress((ProgressBar Bar, TextBlock Step, TextBlock Percent, TextBlock Size, DispatcherTimer Tips) v, (string Step, double Ratio) p)
    {
        v.Step.Text = T("step." + p.Step);
        v.Bar.Value = p.Ratio;
        v.Percent.Text = $"{p.Ratio * 100:0}%";
        if (p.Step == "copy" && Installer.SourceSize > 0)
        {
            var total = Installer.SourceSize / 1048576.0;
            var done = Math.Clamp((p.Ratio - 0.04) / 0.8, 0, 1) * total;
            v.Size.Text = T("progress.bytes", ("done", done.ToString("0")), ("total", total.ToString("0")));
        }
        else v.Size.Text = "";
    }

    async void Run()
    {
        var view = RunView();
        try
        {
            var progress = new Progress<(string Step, double Ratio)>(p => Progress(view, p));
            var desktop = _mode == SetupMode.Update
                ? File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ModLaunch.lnk"))
                : _desktop;
            _result = await Installer.Install(_target, desktop, progress);
            view.Tips.Stop();
            if (_mode == SetupMode.Update) { Installer.Launch(_result.Exe); Close(); return; }
            Show(Done(), 3);
        }
        catch (Exception e)
        {
            view.Tips.Stop();
            ShowError(e);
        }
    }

    void ShowError(Exception e)
    {
        var code = e is SetupError s ? s.Code : "generic";
        var text = I18n.Has("setup.error." + code) ? T("error." + code, ("message", e.Message)) : T("error.generic", ("message", e.Message));
        var icon = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = Ui.Hex("#1FE5484D"), BorderBrush = Ui.Hex("#E5484D"), BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.Icon(Icons.Alert, 28, Ui.Hex("#E5484D")) };
        Show(Ui.Col(16, icon, Ui.Text(T("error.title"), "h1"), Ui.Text(text, "muted", wrap: true),
            Ui.Row(10, Ui.Button(T("error.retry"), Run, "primary", Icons.Refresh), Ui.Button(T("error.back"), () => Show(Welcome())))));
    }

    Control Done()
    {
        var exe = _result!.Exe;
        var launch = Ui.Button(T("done.launch"), () => { Installer.Launch(exe); Close(); }, "primary", Icons.Play);
        launch.FontSize = 16;
        launch.Padding = new Thickness(30, 13);
        if (_launch && !_preview) DispatcherTimer.RunOnce(() => { Installer.Launch(exe); Close(); }, TimeSpan.FromSeconds(2.2));
        var check = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(38), Background = Ui.Hex("#1F5BD68F"), BorderBrush = Ui.Res("Good"), BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.Icon(Icons.Check, 36, Ui.Res("Good")) };
        Animate.From(check, "scale(0.3)", 520, 60, new Avalonia.Animation.Easings.BackEaseOut());
        var covers = Ui.Row(8);
        foreach (var g in Games.GameCatalog.All.Where(g => g.SteamAppId > 0).Take(6))
            covers.Children.Add(new Border { Width = 58, Height = 87, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.GameImage(g, 128, art: Images.Art.Cover) });

        var open = Ui.Button(T("done.open"), () => Installer.OpenFolder(_result.Target), "ghost");
        open.Padding = new Thickness(10, 3);
        open.FontSize = 12;
        var col = Ui.Col(14,
            check,
            Ui.Text(_result.Updated ? T("done.titleUpdate", ("version", Http.Version)) : T("done.title"), "h1"),
            Ui.Text(_desktop ? T("done.textDesktop") : T("done.textStart"), "muted", wrap: true),
            Note(Icons.Folder, Ui.Res("Muted"), T("done.where", ("path", _result.Target)), open));
        if (_result.Moved is not null) col.Children.Add(Note(Icons.Check, Ui.Res("Good"), T("done.moved", ("path", _result.Moved))));
        col.Children.Add(Ui.Row(10, launch, Ui.Button(T("done.close"), Close)));
        col.Children.Add(Ui.Col(8, Ui.Text(T("done.games"), "small muted"), covers));
        return col;
    }

    /// <summary>Строка с маленькой иконкой слева; длинный текст переносится, а не уходит за край окна.</summary>
    static Control Note(string icon, IBrush color, string text, Control? extra = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        var i = Ui.Icon(icon, 15, color);
        i.VerticalAlignment = VerticalAlignment.Top;
        i.Margin = new Thickness(0, 1, 0, 0);
        row.Children.Add(i);
        var t = Ui.Text(text, "small muted", wrap: true);
        Grid.SetColumn(t, 1);
        row.Children.Add(t);
        if (extra is not null)
        {
            extra.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(extra, 2);
            row.Children.Add(extra);
        }
        return row;
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
        var folder = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? _target;
        return Ui.Col(16, Ui.Text(T("uninstall.title"), "h1"), Ui.Text(T("uninstall.lead"), "muted", wrap: true),
            Note(Icons.Folder, Ui.Res("Muted"), T("uninstall.where", ("path", _preview ? _target : folder))),
            wipe, Ui.Row(10, go, Ui.Button(T("uninstall.cancel"), Close)));
    }
}
