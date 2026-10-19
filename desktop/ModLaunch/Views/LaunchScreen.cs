using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 9.9: экран запуска игры «как в кино». Картинка игры во весь экран медленно наезжает,
/// по ней один раз проходит блик; слева снизу — логотип, сведения об игре и дорожка из трёх
/// шагов «моды → копия сохранений → запуск», справа — кольцо прогресса с кнопкой «Играть»
/// в центре. В конце кольцо становится галочкой, экран вспыхивает цветом игры — «Игра запущена!».
/// Игра стартует сразу на третьем шаге — анимация запуск не задерживает.
/// </summary>
public sealed class LaunchScreen : Panel
{
    public enum State { Waiting, Working, Done, Warn, Failed, Skipped }
    public sealed record Result(bool Ok, string? Backup, string? Error);

    sealed class Step
    {
        public readonly Border Mark = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Left };
        public readonly Border Line = new() { Height = 3, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(38, 0, 8, 0) };
        public readonly Border Fill = new() { Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        public readonly TextBlock Title = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
        public readonly TextBlock Detail = new() { FontSize = 12, Foreground = Ui.Hex("#B8BFCC"), TextTrimming = TextTrimming.CharacterEllipsis };
        public State State;
    }

    const double RingSize = 132, RingStroke = 6;

    readonly GameState _g;
    readonly Color _accent;
    readonly Step[] _steps = [new(), new(), new()];
    readonly Arc _arc;
    readonly Border _core;
    readonly Border _halo;
    readonly TextBlock _percent = new() { FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Ui.Hex("#D9DDE6"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
    readonly StackPanel _result = new() { Spacing = 12, IsVisible = false };
    readonly TextBlock _eyebrow;
    readonly Border _art, _flash, _sweep;
    readonly DispatcherTimer _tick;
    Control? _spinning;
    double _angle, _pulse, _shown;
    double _target;
    bool _closed;

    /// <summary>Экран закрыт (кнопкой, Esc или сам после запуска).</summary>
    public event Action? Closed;

    public LaunchScreen(GameState g)
    {
        _g = g;
        _accent = Color.TryParse(g.Def.Accent, out var c) ? c : Gx.Accent;
        Background = Ui.Hex("#07080C");
        ClipToBounds = true;

        // Один таймер на всё: крутилка у шага «идёт», дыхание кольца и плавный бег процентов.
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());

        // Картинка игры: чёткая, медленно приближается (Ken Burns).
        _art = new Border
        {
            Child = Ui.GameImage(g.Def, 1600, art: Images.Art.Hero),
            RenderTransform = TransformOperations.Parse("scale(1.02)"),
            Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromSeconds(12), Easing = new SineEaseOut() }],
        };
        var shadeLeft = new Border { Background = Gx.ShadeLeft(0.75, 245) };
        var shadeUp = new Border { Background = Gx.ShadeUp(0.35, 250) };
        var glow = new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.1, 1.05, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.1, 1.05, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Gx.Alpha(_accent, 120), 0), new GradientStop(Gx.Alpha(_accent, 0), 1) },
            },
        };
        // Блик: светлая полоса наискосок один раз проходит через экран.
        _sweep = new Border
        {
            Width = 260, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(34, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
            },
            RenderTransform = TransformOperations.Parse("translateX(-400px) skewX(-18deg)"),
            Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(1400), Easing = new CubicEaseInOut() }],
        };
        // Вспышка цветом игры в момент «Игра запущена».
        _flash = new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.82, 0.78, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.82, 0.78, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Gx.Alpha(_accent, 170), 0), new GradientStop(Gx.Alpha(_accent, 0), 1) },
            },
            Opacity = 0, IsHitTestVisible = false,
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(700), Easing = new CubicEaseOut() }],
        };

        // Слева снизу: «Запуск», логотип, сведения об игре, дорожка шагов и итог.
        _eyebrow = new TextBlock { Text = I18n.T("launch.via").ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.Bold, LetterSpacing = 2, Foreground = new SolidColorBrush(Look.Mix(_accent, Colors.White, 0.45)) };
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 720);
        Control title = logo is null
            ? new TextBlock { Text = g.Def.Name, FontFamily = Gx.Display, FontSize = 46, FontWeight = FontWeight.Bold, LetterSpacing = -1, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 }
            : new Image { Source = logo, MaxHeight = 140, MaxWidth = 480, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };

        var played = PlayTime.Get(g.Def.Id);
        var meta = Ui.Row(8);
        void Chip(string icon, string text) => meta.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4),
            Child = Ui.Row(6, Ui.Icon(icon, 13, Brushes.White), new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }),
        });
        if (g.ModCount > 0) Chip(Icons.Package, I18n.T("v99.launch.mods", ("n", g.ModCount)));
        if (played.TotalMs >= 60_000) Chip(Icons.Clock, PlayTime.Format(played.TotalMs));
        if (played.LastPlayed is not null) Chip(Icons.Play, Ui.Ago(played.LastPlayed));

        var titles = new[] { I18n.T("launch.check"), I18n.T("launch.backup"), I18n.T("launch.start") };
        var track = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Width = 660, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < _steps.Length; i++)
        {
            var s = _steps[i];
            s.Title.Text = titles[i];
            s.Line.Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
            s.Line.IsVisible = i < _steps.Length - 1;
            s.Fill.Background = new SolidColorBrush(_accent);
            s.Fill.Transitions = [new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new CubicEaseInOut() }];
            s.Line.Child = s.Fill;
            var top = new Panel { Height = 30, Children = { s.Line, s.Mark } };
            var cell = Ui.Col(8, top, Ui.Col(2, s.Title, s.Detail));
            Grid.SetColumn(cell, i);
            track.Children.Add(cell);
            SetStep(i, State.Waiting, null);
        }

        var left = new StackPanel
        {
            Spacing = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(72, 0, 0, 64),
            Children = { _eyebrow, title, meta, new Border { Height = 8 }, track, _result },
        };

        // Справа снизу — кольцо прогресса с «Играть» в центре.
        var trackRing = new Ellipse { Width = RingSize, Height = RingSize, Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = RingStroke };
        _arc = new Arc
        {
            Width = RingSize, Height = RingSize, StartAngle = -90, SweepAngle = 0, Stroke = new SolidColorBrush(_accent), StrokeThickness = RingStroke,
            StrokeLineCap = PenLineCap.Round,
        };
        _core = new Border
        {
            Width = RingSize - 34, Height = RingSize - 34, CornerRadius = new CornerRadius((RingSize - 34) / 2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Look.Mix(_accent, Colors.White, 0.2), 0), new GradientStop(Look.Mix(_accent, Colors.Black, 0.25), 1) },
            },
            Child = Ui.Icon(Icons.Play, 34, Brushes.White),
            RenderTransformOrigin = RelativePoint.Center,
        };
        _halo = new Border
        {
            Width = RingSize + 60, Height = RingSize + 60, CornerRadius = new CornerRadius((RingSize + 60) / 2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = new RadialGradientBrush { GradientStops = { new GradientStop(Gx.Alpha(_accent, 110), 0), new GradientStop(Gx.Alpha(_accent, 0), 1) } },
            RenderTransformOrigin = RelativePoint.Center,
        };
        var ring = new Panel { Width = RingSize + 60, Height = RingSize + 60, Children = { _halo, new Panel { Width = RingSize, Height = RingSize, Children = { trackRing, _arc, _core } } } };
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 60, 40), Children = { ring, _percent } };

        var close = Ui.Button("", Close, "icon ghost", Icons.Close);
        close.Foreground = Brushes.White;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 20, 24, 0);
        var brand = Ui.Row(8, new Image { Source = Images.Asset("icon.png", 64), Width = 20, Height = 20 }, new TextBlock { Text = "ModLaunch", FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = Ui.Hex("#D9DDE6") });
        brand.Margin = new Thickness(28, 24, 0, 0);
        brand.HorizontalAlignment = HorizontalAlignment.Left;
        brand.VerticalAlignment = VerticalAlignment.Top;

        Children.Add(_art);
        Children.Add(shadeLeft);
        Children.Add(shadeUp);
        Children.Add(glow);
        Children.Add(_sweep);
        Children.Add(_flash);
        Children.Add(left);
        Children.Add(right);
        Children.Add(brand);
        Children.Add(close);

        AttachedToVisualTree += (_, _) =>
        {
            _art.RenderTransform = TransformOperations.Parse("scale(1.12)");
            _sweep.RenderTransform = TransformOperations.Parse("translateX(2200px) skewX(-18deg)");
            Animate.From(left, "translateX(-40px)", 520, 0, new QuinticEaseOut());
            Animate.From(right, "scale(0.7)", 560, 120, new BackEaseOut());
            _tick.Start();
        };
        DetachedFromVisualTree += (_, _) => _tick.Stop();
    }

    void Tick()
    {
        // Шаги размечаются в конструкторе раньше, чем создано кольцо.
        if (_arc is null || _halo is null) return;
        if (_spinning is not null)
        {
            _angle = (_angle + 7) % 360;
            _spinning.RenderTransform = new RotateTransform(_angle);
        }
        // «Дыхание» кольца, пока идёт запуск.
        _pulse += 0.06;
        var k = _steps.Any(s => s.State == State.Working) ? 1 + Math.Sin(_pulse) * 0.06 : 1;
        _halo.RenderTransform = new ScaleTransform(k * 1.05, k * 1.05);
        _halo.Opacity = 0.65 + Math.Sin(_pulse) * 0.25;
        // Проценты и дуга догоняют цель плавно.
        if (Math.Abs(_shown - _target) > 0.1)
        {
            _shown += (_target - _shown) * (Animate.On ? 0.12 : 1);
            _arc.SweepAngle = 360 * _shown / 100;
            _percent.Text = $"{Math.Round(_shown)}%";
        }
    }

    /// <summary>Отметить шаг: ждёт, идёт (крутится), готов, предупреждение, ошибка или пропущен.</summary>
    public void SetStep(int index, State state, string? detail)
    {
        var s = _steps[index];
        s.State = state;
        s.Detail.Text = detail ?? "";
        s.Detail.IsVisible = !string.IsNullOrEmpty(detail);
        s.Title.Opacity = state == State.Waiting ? 0.5 : 1;
        var (back, icon, stroke) = state switch
        {
            State.Working => ((IBrush)new SolidColorBrush(Gx.Alpha(_accent, 110)), Icons.Refresh, (IBrush)Brushes.White),
            State.Done => (Ui.Res("Good"), Icons.Check, Ui.Hex("#0F1116")),
            State.Warn => (Ui.Res("Warn"), Icons.Alert, Ui.Hex("#0F1116")),
            State.Failed => (Ui.Res("Bad"), Icons.Close, Brushes.White),
            State.Skipped => (new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), Icons.Minimize, Ui.Hex("#AAB2C2")),
            _ => (new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), (string?)null, Brushes.White),
        };
        s.Mark.Background = back;
        s.Mark.BoxShadow = state == State.Working ? BoxShadows.Parse($"0 0 16 2 {Gx.Alpha(_accent, 160)}") : default;
        s.Mark.Child = icon is null ? Ui.Dot(Ui.Hex("#6B7385"), 6) : Ui.Icon(icon, 15, stroke);
        if (state == State.Working) _spinning = s.Mark.Child;
        else if (_steps.All(x => x.State != State.Working)) _spinning = null;
        if (state is State.Done or State.Warn or State.Skipped)
        {
            // Линия к следующему шагу заполняется цветом игры.
            s.Fill.Width = Math.Max(0, (660 / 3.0) - 46);
            if (Animate.On) Animate.From(s.Mark, "scale(0.6)", 320, easing: new BackEaseOut());
        }
        else s.Fill.Width = 0;
        var done = _steps.Count(x => x.State is State.Done or State.Warn or State.Skipped);
        _target = Math.Min(100, done * 33.4 + (_steps.Any(x => x.State == State.Working) ? 12 : 0));
        if (!Animate.On) Tick();
    }

    /// <summary>Пройти шаги по-настоящему: проверка модов, копия сохранений, запуск.</summary>
    public async Task RunAsync(Func<Result> launch)
    {
        var registry = _g.Registry;
        SetStep(0, State.Working, null);
        var (mods, conflicts) = await Task.Run(() =>
        {
            try
            {
                var list = registry?.List().Where(m => m.Bool("enabled", true) && !m.Bool("missing") && m.Str("kind") != "preset").ToList() ?? [];
                return (list.Count, registry is null || list.Count == 0 ? 0 : Conflicts.Find(registry).Count);
            }
            catch { return (0, 0); }
        });
        await Task.Delay(520);
        if (_closed) return;
        SetStep(0, conflicts > 0 ? State.Warn : State.Done,
            mods == 0 ? I18n.T("launch.check.none")
            : conflicts > 0 ? I18n.T("launch.check.conflicts", ("n", mods), ("c", conflicts))
            : I18n.T("launch.check.ok", ("n", mods)));

        var backups = Backups.OnLaunch;
        SetStep(1, backups ? State.Working : State.Skipped, backups ? null : I18n.T("launch.backup.off"));
        await Task.Delay(backups ? 420 : 240);
        if (_closed) return; // закрыли до запуска — значит, передумали
        SetStep(2, State.Working, _g.Def.Name);
        var r = launch();
        if (backups) SetStep(1, r.Ok ? State.Done : State.Skipped, r.Ok ? (r.Backup is null ? I18n.T("launch.backup.none") : I18n.T("launch.backup.done")) : null);
        if (!r.Ok)
        {
            SetStep(2, State.Failed, null);
            ShowFailure(r.Error);
            return;
        }
        await Task.Delay(480);
        SetStep(2, State.Done, _g.Def.Name);
        ShowSuccess();
        await Task.Delay(3200);
        Close();
    }

    /// <summary>«Игра запущена!»: кольцо — галочка, вспышка цветом игры, подсказка про оверлей, «Свернуть» и «Готово».</summary>
    public void ShowSuccess()
    {
        _target = 100;
        _arc.Stroke = Ui.Res("Good");
        _core.Background = Ui.Res("Good");
        _core.Child = Ui.Icon(Icons.Check, 40, Ui.Hex("#0F1116"));
        _flash.Opacity = 1;
        DispatcherTimer.RunOnce(() => _flash.Opacity = 0.35, TimeSpan.FromMilliseconds(Animate.On ? 650 : 0));
        _eyebrow.Text = I18n.T("v99.launch.ready").ToUpperInvariant();
        _result.Children.Clear();
        _result.Children.Add(new TextBlock { Text = I18n.T("launch.started"), FontFamily = Gx.Display, FontSize = 34, FontWeight = FontWeight.Bold, LetterSpacing = -0.6, Foreground = Brushes.White });
        if (Settings.Data.Bool("overlay", true))
            _result.Children.Add(new TextBlock
            {
                Text = I18n.T("launch.overlayHint", ("key", Hotkey.Display(Hotkey.KeyOf(Settings.Data.Str("overlayKey"))))),
                FontSize = 14, Foreground = Ui.Hex("#C9CFDB"),
            });
        var minimize = Ui.Button(I18n.T("launch.minimize"), () => { Close(); if (MainWindow.Current is { } w) w.WindowState = WindowState.Minimized; }, "hero-ghost", Icons.Minimize);
        var done = Ui.Button(I18n.T("launch.done"), Close, "primary", Icons.Check);
        var buttons = Ui.Row(10, done, minimize);
        buttons.Margin = new Thickness(0, 4, 0, 0);
        _result.Children.Add(buttons);
        _result.Margin = new Thickness(0, 12, 0, 0);
        _result.IsVisible = true;
        Animate.From(_core, "scale(0.4)", 520, easing: new BackEaseOut());
        Animate.From(_result, "translateY(18px)", 420);
        if (!Animate.On) Tick();
    }

    void ShowFailure(string? error)
    {
        _arc.Stroke = Ui.Res("Bad");
        _core.Background = Ui.Res("Bad");
        _core.Child = Ui.Icon(Icons.Close, 34, Brushes.White);
        _result.Children.Clear();
        _result.Children.Add(new TextBlock { Text = I18n.T("launch.failed"), FontFamily = Gx.Display, FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
        if (!string.IsNullOrEmpty(error))
            _result.Children.Add(new TextBlock { Text = error, FontSize = 13.5, Foreground = Ui.Hex("#C9CFDB"), TextWrapping = TextWrapping.Wrap, MaxWidth = 620 });
        _result.Children.Add(Ui.Button(I18n.T("launch.done"), Close, "primary"));
        _result.Margin = new Thickness(0, 12, 0, 0);
        _result.IsVisible = true;
        Animate.From(_result, "translateY(18px)", 420);
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _tick.Stop();
        Closed?.Invoke();
    }

    // ---------------------------------------------------------------- снимки экранов

    public void DemoProgress()
    {
        SetStep(0, State.Done, I18n.T("launch.check.ok", ("n", 3)));
        SetStep(1, State.Done, I18n.T("launch.backup.done"));
        SetStep(2, State.Working, _g.Def.Name);
    }

    public void DemoDone()
    {
        SetStep(2, State.Done, _g.Def.Name);
        ShowSuccess();
    }
}
