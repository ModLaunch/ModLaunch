using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Setup;

namespace ModLaunch.Views;

/// <summary>
/// Окно установки, обновления и удаления (та же программа, режим по имени файла или ключу).
/// 8.4.1: новый вид — живое свечение фона, карточка папки, переключатели вместо галочек,
/// кольцо прогресса с процентами и праздничный экран «Готово».
/// </summary>
public sealed partial class SetupWindow : Window
{
    readonly SetupMode _mode;
    string _target = Installer.SuggestedDir();
    bool _desktop = true, _launch = true, _wipe, _autostart;
    readonly ContentControl _body = new();
    readonly StackPanel _steps = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    readonly ContentControl _lang = new() { VerticalAlignment = VerticalAlignment.Center };
    Installer.Result? _result;

    static string T(string key, params (string, object)[] args) => I18n.T("setup." + key, args);
    static Color Accent => Color.TryParse(Look.Accent, out var c) ? c : Color.Parse("#F2483A");
    static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public SetupWindow(SetupMode mode)
    {
        _mode = mode;
        Title = mode == SetupMode.Uninstall ? T("window.titleUninstall") : T("window.title");
        Width = 980;
        Height = 620;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
        Background = Ui.Res("Bg");
        try { Icon = new WindowIcon(Images.Asset("icon.png")); } catch { }

        Compose();
        if (mode == SetupMode.Update) Opened += (_, _) => Run();
        else Show(mode == SetupMode.Uninstall ? UninstallView() : Welcome());
    }

    /// <summary>Собрать окно целиком (и заново — после смены языка).</summary>
    void Compose()
    {
        var right = new DockPanel();
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(44, 22, 14, 0), Height = 40 };
        _steps.Children.Clear();
        if (_steps.Parent is Panel oldParent) oldParent.Children.Remove(_steps);
        top.Children.Add(_steps);
        var minimize = new Button { Classes = { "win" }, Content = Ui.Icon(Icons.Minimize, 14) };
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        var close = new Button { Classes = { "win", "close" }, Content = Ui.Icon(Icons.Close, 14) };
        close.Click += (_, _) => Close();
        _lang.Content = _mode == SetupMode.Uninstall ? null : LangPicker();
        if (_lang.Parent is Panel langParent) langParent.Children.Remove(_lang);
        var tools = Ui.Row(4, minimize, close);
        tools.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tools, 1);
        top.Children.Add(tools);
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);
        if (_body.Parent is Panel bodyParent) bodyParent.Children.Remove(_body);
        _body.Margin = new Thickness(44, 14, 44, 26);
        right.Children.Add(_body);

        var stage = new Panel { ClipToBounds = true, Background = Brushes.Transparent, Children = { Aurora(), right } };
        // Окно тянется за пустое место (не за кнопки и строки с переключателями).
        stage.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (ReferenceEquals(e.Source, stage) || ReferenceEquals(e.Source, right) || ReferenceEquals(e.Source, top) || ReferenceEquals(e.Source, _body) || e.Source is Canvas) BeginMoveDrag(e);
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(Side());
        Grid.SetColumn(stage, 1);
        grid.Children.Add(stage);
        Content = grid;
    }

    // ---------------------------------------------------------------- левая часть

    /// <summary>Слева: «стена» обложек, плывущая вверх, логотип и что умеет программа.</summary>
    Control Side()
    {
        var features = Ui.Col(14);
        var n = 0;
        foreach (var (i, icon) in new[] { (1, Icons.Download), (2, Icons.Layers), (3, Icons.Sparkles), (4, Icons.Users), (5, Icons.Shield) })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            row.Children.Add(new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Alpha(Accent, 0x2E)), BorderBrush = new SolidColorBrush(Alpha(Accent, 0x66)), BorderThickness = new Thickness(1),
                Child = Ui.Icon(icon, 16, new SolidColorBrush(Accent)),
            });
            var text = Ui.Col(1, Ui.Text(T($"feat.{i}.title"), "h3", color: Brushes.White), Ui.Text(T($"feat.{i}.text"), "small", color: Ui.Hex("#B9C0CE"), wrap: true));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            features.Children.Add(row);
            Animate.From(row, "translateX(-18px)", 460, 120 + n++ * 70, new CubicEaseOut());
        }

        var logo = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(12), ClipToBounds = true,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, Spread = 2, Color = Alpha(Accent, 0x88) }),
            Child = new Image { Source = Images.Asset("icon.png", 96) },
        };
        var brand = Ui.Row(12, logo,
            new TextBlock { FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Inlines = { new Avalonia.Controls.Documents.Run("Mod"), new Avalonia.Controls.Documents.Run("Launch") { Foreground = new SolidColorBrush(Accent) } } });
        Animate.From(logo, "scale(0.6) rotate(-12deg)", 620, 60, new BackEaseOut());

        var side = new Panel
        {
            Width = 360, ClipToBounds = true,
            Children =
            {
                CoverWall(),
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#F50C0D12"), 0), new GradientStop(Color.Parse("#C40C0D12"), 0.45), new GradientStop(Color.Parse("#FA0C0D12"), 1) } } },
                new Border { Background = new RadialGradientBrush { Center = new RelativePoint(0.15, 0.05, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.15, 0.05, RelativeUnit.Relative), RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Alpha(Accent, 0x40), 0), new GradientStop(Alpha(Accent, 0), 1) } }, IsHitTestVisible = false },
                new DockPanel
                {
                    Margin = new Thickness(30, 30, 30, 26),
                    Children =
                    {
                        Docked(brand, Dock.Top),
                        Docked(Ui.Row(12, _lang, new TextBlock { Text = "v" + Http.Version, FontSize = 11, Foreground = Ui.Hex("#7D8596"), VerticalAlignment = VerticalAlignment.Center }), Dock.Bottom),
                        new Border { Margin = new Thickness(0, 30, 0, 0), Child = features },
                    },
                },
            },
        };
        side.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        return side;
    }

    static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    /// <summary>Три колонки настоящих обложек, которые медленно плывут вверх (бесконечно, по кругу).</summary>
    static Control CoverWall()
    {
        var games = Games.GameCatalog.All.Where(g => g.SteamAppId > 0).ToList();
        var canvas = new Canvas { Width = 360 };
        for (var c = 0; c < 3; c++)
        {
            var column = new StackPanel { Spacing = 10, Width = 110 };
            var order = games.Skip(c * 5).Concat(games.Take(c * 5)).ToList();
            foreach (var g in order.Concat(order)) // дважды — чтобы петля была незаметной
                column.Children.Add(new Border { Width = 110, Height = 165, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = Ui.GameImage(g, 256, art: Images.Art.Cover) });
            Canvas.SetLeft(column, 6 + c * 118);
            canvas.Children.Add(column);
            var loop = order.Count * 175.0; // высота одного круга
            var speed = 12 + c * 4.0;       // колонки плывут с разной скоростью — «параллакс»
            var offset = -c * 70.0;
            var transform = new TranslateTransform(0, offset);
            column.RenderTransform = transform;
            Frames(column, t => transform.Y = offset - t * speed % loop);
        }
        return canvas;
    }

    /// <summary>Каждый кадр, пока элемент на экране (t — секунды с появления).</summary>
    static void Frames(Control c, Action<double> tick)
    {
        if (Program.Screenshot) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Tick(TimeSpan _)
        {
            tick(clock.Elapsed.TotalSeconds);
            if (c.IsAttachedToVisualTree()) TopLevel.GetTopLevel(c)?.RequestAnimationFrame(Tick);
        }
        c.AttachedToVisualTree += (_, _) => TopLevel.GetTopLevel(c)?.RequestAnimationFrame(Tick);
    }

    /// <summary>Мягкое свечение цвета акцента за содержимым: два пятна медленно плывут.</summary>
    static Control Aurora()
    {
        Border Blob(double size, byte alpha) => new()
        {
            Width = size, Height = size, IsHitTestVisible = false,
            Background = new RadialGradientBrush { GradientStops = { new GradientStop(Alpha(Accent, alpha), 0), new GradientStop(Alpha(Accent, 0), 1) } },
        };
        var a = Blob(560, 0x30);
        var b = Blob(420, 0x22);
        var canvas = new Canvas { IsHitTestVisible = false, Children = { a, b } };
        Canvas.SetLeft(a, 300); Canvas.SetTop(a, -260);
        Canvas.SetLeft(b, -140); Canvas.SetTop(b, 330);
        var ta = new TranslateTransform();
        var tb = new TranslateTransform();
        a.RenderTransform = ta;
        b.RenderTransform = tb;
        Frames(canvas, t =>
        {
            ta.X = Math.Sin(t * 0.25) * 60; ta.Y = Math.Cos(t * 0.21) * 40;
            tb.X = Math.Cos(t * 0.18) * 70; tb.Y = Math.Sin(t * 0.23) * 30;
        });
        return canvas;
    }

    // ---------------------------------------------------------------- шаги и смена экранов

    /// <summary>Сменить экран: с шагами сверху и мягким въездом.</summary>
    void Show(Control c, int step = 1)
    {
        _body.Content = c;
        RenderSteps(step);
        Animate.From(c, "translateX(28px)", 420, 0, new CubicEaseOut());
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
            var dot = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                Background = done || active ? new SolidColorBrush(Accent) : Ui.Res("Surface3"),
                BoxShadow = active ? new BoxShadows(new BoxShadow { Blur = 14, Color = Alpha(Accent, 0x99) }) : default,
                Child = done ? Ui.Icon(Icons.Check, 12, Brushes.White) : new TextBlock { Text = n.ToString(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = active ? Brushes.White : Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            if (active) Animate.Pop(dot);
            _steps.Children.Add(Ui.Row(8, dot, new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal, Foreground = active || done ? Ui.Res("Text") : Ui.Res("Muted") }));
            if (n < names.Length) _steps.Children.Add(new Border { Width = 28, Height = 2, CornerRadius = new CornerRadius(1), Background = done ? new SolidColorBrush(Accent) : Ui.Res("Line"), VerticalAlignment = VerticalAlignment.Center });
        }
    }

    // ---------------------------------------------------------------- экран 1: настройка

    Control Welcome()
    {
        var (kind, version) = Installer.Inspect(_target);
        var update = kind == Installer.TargetKind.Ours;

        var kicker = new Border
        {
            Background = new SolidColorBrush(Alpha(Accent, 0x22)), BorderBrush = new SolidColorBrush(Alpha(Accent, 0x55)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 4), HorizontalAlignment = HorizontalAlignment.Left,
            Child = Ui.Row(6, Ui.Icon(Icons.Sparkles, 13, new SolidColorBrush(Accent)), new TextBlock { Text = T("kicker") + " · v" + Http.Version, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text") }),
        };
        var title = new TextBlock
        {
            Text = update ? T("welcome.titleUpdate") : T("welcome.title"), FontSize = 38, FontWeight = FontWeight.ExtraBold, LetterSpacing = -1, TextWrapping = TextWrapping.Wrap,
            Foreground = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(((ISolidColorBrush)Ui.Res("Text")).Color, 0.35), new GradientStop(Accent, 1) },
            },
        };
        var lead = Ui.Text(update ? (version is null ? T("welcome.updateLegacy", ("version", Http.Version)) : T("welcome.update", ("from", version), ("version", Http.Version))) : T("welcome.lead"), "muted", wrap: true);
        lead.MaxWidth = 500;
        lead.HorizontalAlignment = HorizontalAlignment.Left;

        // Папка установки — карточкой: значок, путь, свободное место и «Изменить».
        var change = Ui.Button(T("welcome.change"), async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is string dir) { _target = Installer.Normalize(dir); Show(Welcome()); }
        }, "", Icons.Folder);
        change.VerticalAlignment = VerticalAlignment.Center;
        var path = new TextBlock { Text = _target, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(path, _target);
        var where = Ui.Col(2, Ui.Text(T("welcome.folder"), "tiny muted"), path, Ui.Text(FreeSpace(_target), "tiny muted"));
        where.VerticalAlignment = VerticalAlignment.Center;
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        folderRow.Children.Add(Tile(Icons.Folder));
        Grid.SetColumn(where, 1);
        folderRow.Children.Add(where);
        Grid.SetColumn(change, 2);
        folderRow.Children.Add(change);

        // Параметры — переключателями в той же карточке.
        var options = Ui.Col(2,
            Option(Icons.Tv, T("welcome.desktop"), _desktop, v => _desktop = v),
            Option(Icons.Zap, T("welcome.autostart"), _autostart, v => _autostart = v),
            Option(Icons.Play, T("welcome.launch"), _launch, v => _launch = v));
        var card = new Border
        {
            Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 14),
            Child = Ui.Col(12, folderRow, new Border { Height = 1, Background = Ui.Res("Line") }, options),
        };

        var go = Ui.Button(update ? T("welcome.updateBtn") : T("welcome.install"), Run, "primary", Icons.Download);
        go.FontSize = 16;
        go.Padding = new Thickness(30, 13);
        var glow = new Border { CornerRadius = new CornerRadius(12), BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 10, Blur = 30, Spread = -6, Color = Alpha(Accent, 0xB0) }), Child = go };
        var portable = Ui.Button(T("welcome.portable"), () =>
        {
            Program.StartMain([]);
            Close();
        }, "ghost");
        var actions = Ui.Row(12, glow, portable);
        var terms = Ui.Text(T("welcome.terms", ("button", update ? T("welcome.updateBtn") : T("welcome.install"))) + " " + T("welcome.termsLink").ToLowerInvariant(), "tiny muted", wrap: true);

        var col = Ui.Col(14, kicker, title, lead, card, actions, terms);
        for (var i = 0; i < col.Children.Count; i++) Animate.From(col.Children[i], "translateY(14px)", 460, 60 + i * 55, new CubicEaseOut());
        return col;
    }

    Border Tile(string icon, double size = 40) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        Background = new SolidColorBrush(Alpha(Accent, 0x26)), Child = Ui.Icon(icon, size * 0.45, new SolidColorBrush(Accent)),
    };

    static Control Option(string icon, string text, bool value, Action<bool> set)
    {
        var sw = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        sw.IsCheckedChanged += (_, _) => set(sw.IsChecked == true);
        var label = Ui.Row(10, Ui.Icon(icon, 16, Ui.Res("Muted")), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        label.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 34, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        row.Children.Add(label);
        Grid.SetColumn(sw, 1);
        row.Children.Add(sw);
        // Вся строка кликабельна, не только переключатель.
        row.PointerReleased += (_, e) => { if (e.Source is not ToggleSwitch && !(e.Source as Visual)!.GetVisualAncestors().OfType<ToggleSwitch>().Any()) sw.IsChecked = sw.IsChecked != true; };
        return row;
    }

    /// <summary>Выбор языка установщика (и самой программы): остаётся после установки.</summary>
    Control LangPicker()
    {
        var languages = I18n.Available();
        var box = new ComboBox { MinWidth = 150 };
        foreach (var l in languages) box.Items.Add(l.Coverage < 100 ? $"{l.Native} · {l.Coverage}%" : l.Native);
        box.SelectedIndex = Math.Max(0, languages.FindIndex(l => l.Code.Equals(I18n.Lang, StringComparison.OrdinalIgnoreCase)));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0 || languages[box.SelectedIndex].Code == I18n.Lang) return;
            Settings.Language = languages[box.SelectedIndex].Code;
            I18n.Set(Settings.Language);
            Title = _mode == SetupMode.Uninstall ? T("window.titleUninstall") : T("window.title");
            // Пересобираем после обработчика: сам список языков сейчас меняется.
            Dispatcher.UIThread.Post(() => { Compose(); Show(Welcome()); }, DispatcherPriority.Background);
        };
        return Ui.Row(8, Ui.Icon(Icons.Globe, 15, Ui.Res("Muted")), box);
    }

    static string FreeSpace(string target)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(target));
            if (root is null) return "";
            var free = new DriveInfo(root).AvailableFreeSpace / 1024.0 / 1024 / 1024;
            var need = Math.Ceiling((new FileInfo(Environment.ProcessPath ?? "").Length * 2 + 50.0 * 1024 * 1024) / 1024 / 1024);
            return T("welcome.space", ("need", need), ("free", free.ToString("0.#")));
        }
        catch { return ""; }
    }

    // ---------------------------------------------------------------- экран 2: установка

    /// <summary>Кольцо прогресса: дорожка, дуга с процентами и бегущий блик, пока идёт работа.</summary>
    sealed class Ring
    {
        public readonly Panel View;
        readonly Arc _arc;
        readonly TextBlock _percent = new() { FontSize = 40, FontWeight = FontWeight.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center, Text = "0%" };

        public Ring(double size = 190)
        {
            var thick = 12.0;
            var track = new Ellipse { Width = size, Height = size, Stroke = Ui.Res("Surface3"), StrokeThickness = thick };
            _arc = new Arc
            {
                Width = size, Height = size, StartAngle = -90, SweepAngle = 0.01, StrokeThickness = thick, StrokeLineCap = PenLineCap.Round,
                Stroke = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Accent, 0), new GradientStop(Color.Parse("#FFB36B"), 1) } },
            };
            if (Animate.On) _arc.Transitions = [new DoubleTransition { Property = Arc.SweepAngleProperty, Duration = TimeSpan.FromMilliseconds(380), Easing = new CubicEaseOut() }];
            var spinner = new Arc { Width = size + 26, Height = size + 26, StartAngle = 0, SweepAngle = 70, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, Stroke = new SolidColorBrush(Alpha(Accent, 0x99)) };
            var spin = new RotateTransform();
            spinner.RenderTransform = spin;
            Frames(spinner, t => spin.Angle = t * 140 % 360);
            var glow = new Border { Width = size * 0.72, Height = size * 0.72, CornerRadius = new CornerRadius(size), BoxShadow = new BoxShadows(new BoxShadow { Blur = 60, Spread = 6, Color = Alpha(Accent, 0x55) }) };
            View = new Panel
            {
                Width = size + 30, Height = size + 30,
                Children = { glow, spinner, track, _arc, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _percent } } },
            };
            foreach (var c in View.Children) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Center; }
        }

        public void Set(double ratio)
        {
            ratio = Math.Clamp(ratio, 0, 1);
            _arc.SweepAngle = Math.Max(0.01, ratio * 359.9);
            _percent.Text = $"{ratio * 100:0}%";
        }
    }

    /// <summary>Экран установки: кольцо, заголовок, текущий шаг и сменяющиеся подсказки.</summary>
    (Control View, Ring Ring, TextBlock Step, DispatcherTimer Tips) ProgressView()
    {
        var ring = new Ring();
        var step = Ui.Text(T("step.close"), "muted");
        step.HorizontalAlignment = HorizontalAlignment.Center;
        var tip = Ui.Text(T("tip.1"), "", wrap: true);
        tip.VerticalAlignment = VerticalAlignment.Center;
        var tipGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        tipGrid.Children.Add(Tile(Icons.Sparkles, 34));
        Grid.SetColumn(tip, 1);
        tipGrid.Children.Add(tip);
        var tipBox = new Border { Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12), MaxWidth = 460, Child = tipGrid };
        var tipIndex = 1;
        var tips = new DispatcherTimer(TimeSpan.FromSeconds(3.5), DispatcherPriority.Background, (_, _) =>
        {
            tipIndex = tipIndex % 5 + 1;
            tip.Text = T("tip." + tipIndex);
            Animate.From(tip, "translateY(8px)", 300);
        });
        tips.Start();
        var title = Ui.Text(_mode == SetupMode.Update && Updater.Latest is null ? T("update.title", ("version", Http.Version)) : T("progress.install"), "h1");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var hint = Ui.Text(_mode == SetupMode.Update ? T("update.lead") : T("progress.hint"), "muted small", wrap: true);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.TextAlignment = TextAlignment.Center;
        hint.MaxWidth = 440;
        var view = Ui.Col(14, ring.View, title, step, hint, tipBox);
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
        return (view, ring, step, tips);
    }

    async void Run()
    {
        var (view, ring, step, tips) = ProgressView();
        Show(view, 2);
        try
        {
            var progress = new Progress<(string Step, double Ratio)>(p =>
            {
                step.Text = T("step." + p.Step);
                ring.Set(p.Ratio);
            });
            var desktop = _mode == SetupMode.Update
                ? File.Exists(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ModLaunch.lnk"))
                : _desktop;
            _result = await Installer.Install(_target, desktop, progress, _autostart);
            tips.Stop();
            ring.Set(1);
            if (_mode == SetupMode.Update) { Installer.Launch(_result.Exe); Close(); return; }
            await Task.Delay(Animate.On ? 450 : 0);
            Show(Done(), 3);
        }
        catch (Exception e)
        {
            tips.Stop();
            Guard.Log(e);
            var code = e is SetupError s ? s.Code : "generic";
            var text = I18n.Has("setup.error." + code) ? T("error." + code, ("message", e.Message)) : T("error.generic", ("message", e.Message));
            var badge = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = new SolidColorBrush(Alpha(Color.Parse("#E5484D"), 0x26)), HorizontalAlignment = HorizontalAlignment.Left,
                Child = Ui.Icon(Icons.Alert, 28, Ui.Res("Bad")) };
            Animate.Pop(badge);
            Show(Ui.Col(16, badge, Ui.Text(T("error.title"), "h1"), Ui.Text(text, "muted", wrap: true), Ui.Text(T("error.log", ("path", Installer.LogPath)), "small muted", wrap: true),
                Ui.Row(10, Ui.Button(T("error.retry"), Run, "primary", Icons.Refresh), Ui.Button(T("error.back"), () => Show(Welcome())))));
        }
    }

    // ---------------------------------------------------------------- экран 3: готово

    Control Done()
    {
        var launch = Ui.Button(T("done.launch"), () => { Installer.Launch(_result!.Exe); Close(); }, "primary", Icons.Play);
        launch.FontSize = 16;
        launch.Padding = new Thickness(30, 13);
        var glow = new Border { CornerRadius = new CornerRadius(12), BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 10, Blur = 30, Spread = -6, Color = Alpha(Accent, 0xB0) }), Child = launch };
        if (_launch && !Program.Screenshot) DispatcherTimer.RunOnce(() => { Installer.Launch(_result!.Exe); Close(); }, TimeSpan.FromSeconds(2.4));

        // Значок «готово» с «конфетти» вокруг: точки разлетаются из центра.
        var good = ((ISolidColorBrush)Ui.Res("Good")).Color;
        var burst = new Canvas { Width = 150, Height = 150, IsHitTestVisible = false };
        var colors = new[] { Accent, good, Color.Parse("#FFB36B"), Color.Parse("#8B7CFF") };
        for (var i = 0; i < 14; i++)
        {
            var angle = i * Math.PI * 2 / 14 + (i % 2) * 0.2;
            var radius = 62 + (i % 3) * 8;
            var size = 5 + i % 3 * 2.0;
            var dot = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size), Background = new SolidColorBrush(colors[i % colors.Length]) };
            var dx = Math.Cos(angle) * radius;
            var dy = Math.Sin(angle) * radius;
            Canvas.SetLeft(dot, 75 + dx - size / 2);
            Canvas.SetTop(dot, 75 + dy - size / 2);
            burst.Children.Add(dot);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Animate.From(dot, $"translate({(-dx).ToString("0", inv)}px, {(-dy).ToString("0", inv)}px) scale(0.2)", 700, 180 + i * 12, new CubicEaseOut());
        }
        var check = new Border
        {
            Width = 92, Height = 92, CornerRadius = new CornerRadius(46), Background = new SolidColorBrush(Alpha(good, 0x2A)), BorderBrush = new SolidColorBrush(good), BorderThickness = new Thickness(2),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 40, Color = Alpha(good, 0x66) }),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Ui.Icon(Icons.Check, 42, new SolidColorBrush(good)),
        };
        Animate.From(check, "scale(0.3)", 560, 60, new BackEaseOut());
        var badge = new Panel { Width = 150, Height = 150, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-28, -20, 0, -10), Children = { burst, check } };

        var covers = Ui.Row(10);
        var n = 0;
        foreach (var g in Games.GameCatalog.All.Where(g => g.SteamAppId > 0).Take(7))
        {
            var cover = new Border { Width = 62, Height = 93, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.GameImage(g, 128, art: Images.Art.Cover) };
            Animate.From(cover, "translateY(16px)", 420, 380 + n++ * 50, new CubicEaseOut());
            covers.Children.Add(cover);
        }
        var title = new TextBlock { Text = _result!.Updated ? T("done.titleUpdate", ("version", Http.Version)) : T("done.title"), FontSize = 34, FontWeight = FontWeight.ExtraBold, LetterSpacing = -0.8, TextWrapping = TextWrapping.Wrap };
        return Ui.Col(16,
            badge,
            title,
            Ui.Text(_desktop ? T("done.textDesktop") : T("done.textStart"), "muted", wrap: true),
            Ui.Row(12, glow, Ui.Button(T("done.close"), Close, "ghost")),
            Ui.Col(8, Ui.Text(T("done.games"), "small muted"), covers));
    }

    // ---------------------------------------------------------------- удаление

    Control UninstallView()
    {
        var wipe = new CheckBox { Content = T("uninstall.wipe"), IsChecked = _wipe };
        wipe.IsCheckedChanged += (_, _) => _wipe = wipe.IsChecked == true;
        var go = Ui.Button(T("uninstall.go"), () =>
        {
            try
            {
                Installer.Uninstall(_wipe);
                Show(Ui.Col(16, Tile(Icons.Check, 64), Ui.Text(T("uninstall.done"), "h1"), Ui.Text(T("uninstall.bye"), "muted", wrap: true), Ui.Button(T("done.close"), Close, "primary")));
            }
            catch (Exception e)
            {
                Guard.Log(e);
                Show(Ui.Col(16, Ui.Text(T("uninstall.error"), "h1"), Ui.Text(e.Message, "muted", wrap: true), Ui.Button(T("done.close"), Close)));
            }
        }, "primary", Icons.Trash);
        var card = new Border { Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 14), Child = wipe };
        return Ui.Col(16, Tile(Icons.Trash, 64), Ui.Text(T("uninstall.title"), "h1"), Ui.Text(T("uninstall.lead"), "muted", wrap: true), card,
            Ui.Row(10, go, Ui.Button(T("uninstall.cancel"), Close, "ghost")));
    }

    /// <summary>Для снимков: экран установки на нужном проценте и экран «Готово».</summary>
    public void DemoProgress(double ratio)
    {
        var (view, ring, step, tips) = ProgressView();
        tips.Stop();
        step.Text = T("step.copy");
        ring.Set(ratio);
        Show(view, 2);
    }

    public void DemoDone()
    {
        _launch = false;
        _result = new Installer.Result(_target, "ModLaunch.exe", false, null);
        Show(Done(), 3);
    }
}
