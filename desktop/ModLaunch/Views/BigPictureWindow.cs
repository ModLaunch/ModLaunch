using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Режим Big Picture: весь экран, крупные обложки, управление геймпадом
/// (или стрелками и Enter). Выбрать игру, включить/выключить моды, запустить,
/// усыпить или выключить компьютер — не вставая с дивана. Кнопка View
/// переключает «управление ПК»: геймпад становится мышью и клавиатурой.
/// </summary>
public sealed class BigPictureWindow : Window
{
    enum Zone { Games, Actions, Modal }

    readonly Gamepad _pad = new();
    readonly DispatcherTimer _timer;
    readonly List<GameState> _games;
    readonly Panel _heroHost = new() { ClipToBounds = true };
    int _shownGame = -1;
    readonly Panel _heroFallback = new();
    readonly ContentControl _title = new();
    readonly TextBlock _facts = new() { FontSize = 18, Foreground = Ui.Hex("#C9CFDB") };
    readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 14 };
    readonly StackPanel _carousel = new() { Orientation = Orientation.Horizontal, Spacing = 22, Margin = new Thickness(72, 26, 72, 22) };
    readonly ScrollViewer _carouselScroll;
    readonly Panel _modal = new() { IsVisible = false };
    readonly TextBlock _clock = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _padState = new() { FontSize = 14, Foreground = Ui.Hex("#AAB2C2"), VerticalAlignment = VerticalAlignment.Center };
    readonly Border _pcBanner;

    Zone _zone = Zone.Games;
    int _game, _action;
    List<(string Text, string? Sub, Action Run, bool On)> _modalItems = [];
    int _modalIndex;
    string _modalTitle = "";
    bool _pcMode;
    DateTime _lastFrame = DateTime.UtcNow;
    readonly List<Button> _actionButtons = [];

    public static BigPictureWindow? Current { get; private set; }

    public static void Open()
    {
        if (Current is { } open) { open.Activate(); return; }
        Current = new BigPictureWindow();
        Current.Show();
        MainWindow.Current?.Hide();
    }

    public BigPictureWindow(bool windowed = false)
    {
        Title = "ModLaunch — Big Picture";
        Background = Ui.Hex("#08090D");
        SystemDecorations = windowed ? SystemDecorations.Full : SystemDecorations.None;
        if (!windowed) WindowState = WindowState.FullScreen;
        else { Width = 1366; Height = 800; }
        Icon = MainWindow.Current?.Icon;

        _games = AppState.Games.Where(g => g.Status == Detect.Found)
            .OrderByDescending(g => PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).ToList();

        _carouselScroll = new ScrollViewer
        {
            Content = _carousel, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        _pcBanner = new Border
        {
            IsVisible = false, Background = Ui.Hex("#E6101218"), CornerRadius = new CornerRadius(16), Padding = new Thickness(28, 18),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderBrush = Ui.Res("Brand"), BorderThickness = new Thickness(2),
            Child = Ui.Col(8,
                new TextBlock { Text = I18n.T("bp.pc.title"), FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                new TextBlock { Text = I18n.T("bp.pc.help"), FontSize = 16, Foreground = Ui.Hex("#C9CFDB"), TextWrapping = TextWrapping.Wrap, MaxWidth = 720, LineHeight = 26 }),
        };

        Content = BuildLayout();
        BuildCarousel();
        Select(0);
        Classes.Set("juicy", Animate.On);
        // Анимируем содержимое обложки, а не саму кнопку: у кнопки прозрачность задаёт стиль (выбранная ярче).
        for (var i = 0; i < _carousel.Children.Count; i++) if (_carousel.Children[i] is Button { Content: Control inner }) Animate.From(inner, "translateY(80px) scale(0.9)", 520, 120 + Math.Min(i * 45, 500), new Avalonia.Animation.Easings.BackEaseOut());

        _pad.Pressed += OnPad;
        _pad.Released += p => { if (_pcMode) PcControl.Press(p, false); };
        _pad.ConnectionChanged += _ => UpdatePadState();
        UpdatePadState();

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Frame());
        _timer.Start();
        KeyDown += OnKey;
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_pcMode) PcControl.ReleaseAll();
            if (Current == this) Current = null;
            MainWindow.Current?.Show();
            MainWindow.Current?.Activate();
        };
    }

    // ---------------------------------------------------------------- разметка

    Control BuildLayout()
    {
        var top = new DockPanel { Margin = new Thickness(56, 34, 56, 0), VerticalAlignment = VerticalAlignment.Top };
        var right = Ui.Row(22, Ui.Row(8, Ui.Icon(Icons.Gamepad, 18, Ui.Hex("#AAB2C2")), _padState), _clock);
        var power = BigIconButton(Icons.Power, OpenPower, I18n.T("bp.power"));
        right.Children.Add(power);
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(Ui.Row(12,
            new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = new Image { Source = Images.Asset("icon.png", 96) } },
            new TextBlock { Text = "ModLaunch", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
            new Border { Background = Ui.Res("Brand"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "BIG PICTURE", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brushes.White } }));

        var info = Ui.Col(16, _title, _facts, _actions);
        info.Margin = new Thickness(72, 0, 72, 4);
        info.VerticalAlignment = VerticalAlignment.Bottom;
        info.HorizontalAlignment = HorizontalAlignment.Left;

        var hints = Ui.Row(28,
            Hint("A", I18n.T("bp.hint.select")), Hint("B", I18n.T("bp.hint.back")), Hint("X", I18n.T("bp.hint.mods")),
            Hint("☰", I18n.T("bp.hint.power")), Hint("⧉", I18n.T("bp.hint.pc")), Hint("Esc", I18n.T("bp.hint.exit")));
        hints.HorizontalAlignment = HorizontalAlignment.Right;
        hints.Margin = new Thickness(56, 0, 56, 22);

        _clock.Text = DateTime.Now.ToString("HH:mm");
        DispatcherTimer.Run(() => { _clock.Text = DateTime.Now.ToString("HH:mm"); return IsVisible; }, TimeSpan.FromSeconds(10));

        var empty = new TextBlock
        {
            Text = I18n.T("bp.empty"), FontSize = 22, Foreground = Ui.Hex("#C9CFDB"), IsVisible = _games.Count == 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        return new Panel
        {
            Children =
            {
                _heroFallback,
                _heroHost,
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#9008090D"), 0), new GradientStop(Color.Parse("#2008090D"), 0.25), new GradientStop(Color.Parse("#C008090D"), 0.62), new GradientStop(Color.Parse("#FA08090D"), 1) } } },
                new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#B008090D"), 0), new GradientStop(Color.Parse("#0008090D"), 0.6) } } },
                Layout(top, info, hints), empty, _modal, _pcBanner,
            },
        };
    }

    Grid Layout(Control top, Control info, Control hints)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto") };
        grid.Children.Add(top);
        Grid.SetRow(info, 2);
        grid.Children.Add(info);
        Grid.SetRow(_carouselScroll, 3);
        grid.Children.Add(_carouselScroll);
        Grid.SetRow(hints, 4);
        grid.Children.Add(hints);
        return grid;
    }

    static Control Hint(string key, string text) => Ui.Row(8,
        new Border { MinWidth = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Hex("#2A2F3A"), Padding = new Thickness(7, 0),
            Child = new TextBlock { Text = key, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
        new TextBlock { Text = text, FontSize = 14, Foreground = Ui.Hex("#AAB2C2"), VerticalAlignment = VerticalAlignment.Center });

    static Button BigIconButton(string icon, Action run, string tip)
    {
        var b = new Button { Classes = { "bp-icon" }, Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Padding = new Thickness(0), Content = Ui.Icon(icon, 22, Brushes.White), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => run();
        return b;
    }

    void BuildCarousel()
    {
        _carousel.Children.Clear();
        for (var i = 0; i < _games.Count; i++)
        {
            var index = i;
            var g = _games[i];
            var card = new Button
            {
                Classes = { "bp-cover" }, Padding = new Thickness(0), Width = 180, Height = 270,
                Content = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = Ui.GameImage(g.Def, 400, art: Images.Art.Cover) },
            };
            card.Click += (_, _) => { if (_game == index) Activate(0); else { _zone = Zone.Games; Select(index); } };
            _carousel.Children.Add(card);
        }
    }

    // ---------------------------------------------------------------- выбор игры

    GameState? Selected => _games.Count == 0 ? null : _games[Math.Clamp(_game, 0, _games.Count - 1)];

    void Select(int index)
    {
        if (_games.Count == 0) { RenderActions(); return; }
        _game = (index % _games.Count + _games.Count) % _games.Count;
        for (var i = 0; i < _carousel.Children.Count; i++)
            _carousel.Children[i].Classes.Set("selected", i == _game);
        if (_carousel.Children[_game] is Control c) c.BringIntoView();

        var g = _games[_game];
        var changed = _shownGame != _game;
        _shownGame = _game;
        if (changed)
        {
            _heroFallback.Background = new SolidColorBrush(Color.Parse(g.Def.Accent));
            if (Images.GameAsset(g.Def, Images.Art.Hero, 1920) is { } local) SetHero(local);
            else
            {
                var def = g.Def;
                _ = Images.GameAsync(def, Images.Art.Hero, 1920).ContinueWith(t =>
                {
                    if (t.Result is Bitmap b) Dispatcher.UIThread.Post(() => { if (Selected?.Def == def) SetHero(b); });
                });
            }
        }
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 900);
        _title.Content = logo is null
            ? new TextBlock { Text = g.Def.Name, FontSize = 54, FontWeight = FontWeight.Bold, Foreground = Brushes.White }
            : new Image { Source = logo, MaxHeight = 170, MaxWidth = 560, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };

        var played = PlayTime.Get(g.Def.Id);
        var facts = new List<string>();
        if (played.Running) facts.Add(I18n.T("time.running"));
        else if (played.LastPlayed is not null) facts.Add(I18n.T("time.last", ("when", Ui.Ago(played.LastPlayed))));
        if (played.TotalMs > 0) facts.Add(I18n.T("time.total", ("time", PlayTime.Format(played.TotalMs))));
        facts.Add(GameCard.Status(g));
        _facts.Text = string.Join("  ·  ", facts);
        RenderActions();
        if (changed)
        {
            // Логотип, строка фактов и кнопки въезжают слева лесенкой.
            Animate.From(_title, "translateX(-40px)", 420, 0, new Avalonia.Animation.Easings.BackEaseOut());
            Animate.From(_facts, "translateX(-30px)", 380, 60);
            Animate.From(_actions, "translateX(-24px)", 380, 110);
        }
    }

    /// <summary>Новый фон плавно проявляется поверх старого и медленно «отъезжает» (эффект Кена Бёрнса).</summary>
    void SetHero(Bitmap bitmap)
    {
        var image = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
        _heroHost.Children.Add(image);
        if (!Animate.On)
        {
            while (_heroHost.Children.Count > 1) _heroHost.Children.RemoveAt(0);
            return;
        }
        image.Opacity = 0;
        image.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1.1)");
        Dispatcher.UIThread.Post(() =>
        {
            image.Transitions =
            [
                new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(1600), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
            ];
            image.Opacity = 1;
            image.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)");
        }, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(() =>
        {
            // Старые фоны убираем, когда новый уже проявился.
            while (_heroHost.Children.Count > 1 && _heroHost.Children[0] != image) _heroHost.Children.RemoveAt(0);
        }, TimeSpan.FromMilliseconds(600));
    }

    // ---------------------------------------------------------------- кнопки действий

    List<(string Text, string Icon, Action Run, bool Primary)> ActionList()
    {
        var list = new List<(string, string, Action, bool)>();
        var g = Selected;
        if (g is not null)
        {
            var running = Features.Launcher.IsRunning(g.Def.Id);
            if (running) list.Add((I18n.T("v4.stop"), Icons.Stop, () => { Features.Launcher.Stop(g.Def.Id); Select(_game); }, true));
            else if (g.LoaderInstalled || g.Def.Loader == Games.LoaderKind.None) list.Add((I18n.T("games.play"), Icons.Play, () => Play(g), true));
            else list.Add((I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), Icons.Download, () => { Actions.InstallLoader(g); }, true));
            list.Add((I18n.T("bp.mods", ("n", g.ModCount)), Icons.Layers, () => OpenMods(g), false));
            list.Add((I18n.T("bp.openInApp"), Icons.External, () => { var id = g.Def.Id; Close(); MainWindow.Current?.Navigate(() => new GamePage(id)); }, false));
        }
        list.Add((I18n.T("bp.pc"), Icons.Mouse, TogglePc, false));
        return list;
    }

    void RenderActions()
    {
        _actions.Children.Clear();
        _actionButtons.Clear();
        var list = ActionList();
        _action = Math.Clamp(_action, 0, list.Count - 1);
        for (var i = 0; i < list.Count; i++)
        {
            var (text, icon, run, primary) = list[i];
            var b = new Button
            {
                Classes = { "bp-action" }, Padding = new Thickness(primary ? 34 : 24, 16), CornerRadius = new CornerRadius(14),
                Content = Ui.Row(12, Ui.Icon(icon, 22, Brushes.White), new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }),
            };
            if (primary) b.Classes.Add("primary-bp");
            if (_zone == Zone.Actions && i == _action) b.Classes.Add("focused");
            var rr = run;
            b.Click += (_, _) => rr();
            _actionButtons.Add(b);
            _actions.Children.Add(b);
        }
    }

    void Activate(int action)
    {
        var list = ActionList();
        if (action >= 0 && action < list.Count) list[action].Run();
    }

    void Play(GameState g)
    {
        Actions.Play(g);
        Toast(I18n.T("bp.starting", ("game", g.Def.Name)));
        DispatcherTimer.RunOnce(() => Select(_game), TimeSpan.FromSeconds(3));
    }

    // ---------------------------------------------------------------- моды и меню питания

    void OpenMods(GameState g)
    {
        var registry = g.Registry;
        var items = new List<(string, string?, Action, bool)>();
        foreach (var m in registry?.List() ?? [])
        {
            var id = m.Str("id");
            if (id is null || m.Bool("missing")) continue;
            var on = m.Bool("enabled", true);
            items.Add((m.Str("name") ?? id, m.Str("version") is { Length: > 0 } v ? "v" + v : null, () =>
            {
                try
                {
                    registry!.SetEnabled(id, !on);
                    if (m.Str("kind") == "preset") Mods.Installer.SyncPreset(registry, !on ? registry.Get(id) : null);
                }
                catch (Exception e) { Toast(Jobs.Explain(e)); }
                AppState.Notify();
                var keep = _modalIndex;
                OpenMods(g);
                _modalIndex = Math.Min(keep, _modalItems.Count - 1);
                RenderModal();
            }, on));
        }
        ShowModal(I18n.T("bp.mods.title", ("game", g.Def.Name)), items, I18n.T("bp.mods.none"), toggles: true);
    }

    void OpenPower()
    {
        ShowModal(I18n.T("bp.power"),
        [
            (I18n.T("bp.power.exit"), null, Close, false),
            (I18n.T("bp.power.sleep"), null, () => { CloseModal(); PcControl.Sleep(); }, false),
            (I18n.T("bp.power.restart"), null, () => Confirm(I18n.T("bp.power.restart"), PcControl.Restart), false),
            (I18n.T("bp.power.off"), null, () => Confirm(I18n.T("bp.power.off"), PcControl.PowerOff), false),
            (I18n.T("bp.power.quit"), null, () => { Close(); MainWindow.Current?.Quit(); }, false),
        ], "");
    }

    void Confirm(string what, Action run) => ShowModal(I18n.T("bp.confirm", ("what", what)),
        [(what, null, run, false), (I18n.T("common.cancel"), null, OpenPower, false)], "");

    void ShowModal(string title, List<(string, string?, Action, bool)> items, string empty, bool toggles = false)
    {
        _modalToggles = toggles;
        _modalTitle = title;
        _modalItems = items;
        _modalIndex = 0;
        _zone = Zone.Modal;
        _modalEmpty = empty;
        RenderModal();
    }

    string _modalEmpty = "";
    bool _modalToggles;

    void RenderModal()
    {
        var list = new StackPanel { Spacing = 6 };
        for (var i = 0; i < _modalItems.Count; i++)
        {
            var (text, sub, run, on) = _modalItems[i];
            var isToggle = _modalToggles;
            var row = new DockPanel();
            if (isToggle)
            {
                var pill = new Border
                {
                    Width = 52, Height = 28, CornerRadius = new CornerRadius(14), Background = on ? Ui.Res("Brand") : Ui.Hex("#3A404D"),
                    Child = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brushes.White, Margin = new Thickness(3), HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left },
                };
                DockPanel.SetDock(pill, Dock.Right);
                row.Children.Add(pill);
            }
            var label = Ui.Col(2, new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });
            if (sub is not null) label.Children.Add(new TextBlock { Text = sub, FontSize = 14, Foreground = Ui.Hex("#AAB2C2") });
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            var b = new Button { Classes = { "bp-row" }, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(20, 14), CornerRadius = new CornerRadius(12) };
            if (i == _modalIndex) b.Classes.Add("focused");
            var index = i;
            b.Click += (_, _) => { _modalIndex = index; run(); };
            list.Children.Add(b);
        }
        if (_modalItems.Count == 0) list.Children.Add(new TextBlock { Text = _modalEmpty, FontSize = 18, Foreground = Ui.Hex("#AAB2C2"), TextWrapping = TextWrapping.Wrap });
        var scroll = new ScrollViewer { Content = list, MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var box = new Border
        {
            Width = 640, Background = Ui.Hex("#F2151821"), CornerRadius = new CornerRadius(20), Padding = new Thickness(26), BorderBrush = Ui.Hex("#2A2F3A"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Col(18, new TextBlock { Text = _modalTitle, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brushes.White }, scroll,
                new TextBlock { Text = I18n.T("bp.modal.hint"), FontSize = 14, Foreground = Ui.Hex("#8A93A5") }),
        };
        _modal.Children.Clear();
        var shade = new Border { Background = Ui.Hex("#B0000000") };
        shade.PointerPressed += (_, _) => CloseModal();
        _modal.Children.Add(shade);
        _modal.Children.Add(box);
        if (!_modal.IsVisible) { Animate.Pop(box); Animate.From(shade, "none", 200); }
        _modal.IsVisible = true;
        if (list.Children.Count > _modalIndex && _modalIndex >= 0) list.Children[_modalIndex].BringIntoView();
    }

    void CloseModal()
    {
        _modal.IsVisible = false;
        _modal.Children.Clear();
        _zone = Zone.Actions;
        Select(_game);
    }

    // ---------------------------------------------------------------- ввод

    void Frame()
    {
        var now = DateTime.UtcNow;
        var dt = Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        _pad.Tick();
        if (_pcMode && _pad.Connected)
        {
            PcControl.Frame(_pad, dt);
            PcControl.AfterFrame(_pad);
        }
    }

    void OnPad(Pad p)
    {
        if (_pcMode)
        {
            // View (Back) — вернуться в Big Picture; остальное — мышь и клавиши.
            if (p == Pad.Back) { TogglePc(); return; }
            PcControl.Press(p, true);
            return;
        }
        if (!IsActive || Features.Launcher.AnyRunning()) return; // игра на переднем плане — геймпад её, не наш
        switch (p)
        {
            case Pad.Up: Move(0, -1); break;
            case Pad.Down: Move(0, 1); break;
            case Pad.Left: Move(-1, 0); break;
            case Pad.Right: Move(1, 0); break;
            case Pad.LB: if (_zone != Zone.Modal) { _zone = Zone.Games; Select(_game - 1); } break;
            case Pad.RB: if (_zone != Zone.Modal) { _zone = Zone.Games; Select(_game + 1); } break;
            case Pad.A: Confirm(); break;
            case Pad.B: Back(); break;
            case Pad.X: if (_zone != Zone.Modal && Selected is { } g) OpenMods(g); break;
            case Pad.Start: if (_zone == Zone.Modal) CloseModal(); else OpenPower(); break;
            case Pad.Back: TogglePc(); break;
        }
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Move(0, -1); break;
            case Key.Down: Move(0, 1); break;
            case Key.Left: Move(-1, 0); break;
            case Key.Right: Move(1, 0); break;
            case Key.Enter or Key.Space: Confirm(); break;
            case Key.Escape or Key.Back: if (_zone == Zone.Games) Close(); else Back(); break;
            case Key.F11: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    void Move(int dx, int dy)
    {
        switch (_zone)
        {
            case Zone.Modal:
                if (_modalItems.Count == 0) return;
                _modalIndex = Math.Clamp(_modalIndex + dy + dx, 0, _modalItems.Count - 1);
                RenderModal();
                break;
            case Zone.Games:
                if (dy < 0 && _games.Count > 0) { _zone = Zone.Actions; _action = 0; RenderActions(); }
                else if (dx != 0) Select(_game + dx);
                break;
            case Zone.Actions:
                if (dy > 0) { _zone = Zone.Games; RenderActions(); }
                else if (dx != 0) { _action = Math.Clamp(_action + dx, 0, _actionButtons.Count - 1); RenderActions(); }
                break;
        }
    }

    void Confirm()
    {
        switch (_zone)
        {
            case Zone.Modal:
                if (_modalIndex >= 0 && _modalIndex < _modalItems.Count) _modalItems[_modalIndex].Run();
                break;
            case Zone.Games: Activate(0); break; // A на обложке — «Играть», как в Steam
            case Zone.Actions: Activate(_action); break;
        }
    }

    void Back()
    {
        if (_zone == Zone.Modal) CloseModal();
        else if (_zone == Zone.Actions) { _zone = Zone.Games; RenderActions(); }
    }

    void TogglePc()
    {
        if (!_pad.Connected && !_pcMode) { Toast(I18n.T("bp.pc.noPad")); return; }
        _pcMode = !_pcMode;
        if (_pcMode)
        {
            if (_zone == Zone.Modal) CloseModal();
            _pcBanner.IsVisible = true;
            DispatcherTimer.RunOnce(() =>
            {
                _pcBanner.IsVisible = false;
                if (_pcMode) WindowState = WindowState.Minimized;
            }, TimeSpan.FromSeconds(2.5));
        }
        else
        {
            PcControl.ReleaseAll();
            _pcBanner.IsVisible = false;
            WindowState = SystemDecorations == SystemDecorations.None ? WindowState.FullScreen : WindowState.Normal;
            Activate();
            Toast(I18n.T("bp.pc.off"));
        }
        UpdatePadState();
    }

    void UpdatePadState() => _padState.Text = _pcMode ? I18n.T("bp.pc.on") : _pad.Connected ? I18n.T("bp.pad.on") : I18n.T("bp.pad.off");

    // ---------------------------------------------------------------- всплывающие подсказки

    Border? _toast;
    void Toast(string text)
    {
        if (Content is not Panel root) return;
        if (_toast is not null) root.Children.Remove(_toast);
        _toast = new Border
        {
            Background = Ui.Hex("#F0151821"), CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 14), BorderBrush = Ui.Hex("#2A2F3A"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 34, 0, 0),
            Child = new TextBlock { Text = text, FontSize = 18, Foreground = Brushes.White },
        };
        root.Children.Add(_toast);
        var mine = _toast;
        DispatcherTimer.RunOnce(() => { root.Children.Remove(mine); if (_toast == mine) _toast = null; }, TimeSpan.FromSeconds(3));
    }

    /// <summary>Для снимков экрана: показать меню модов первой игры.</summary>
    public void DemoMods() { if (Selected is { } g) OpenMods(g); }
}
