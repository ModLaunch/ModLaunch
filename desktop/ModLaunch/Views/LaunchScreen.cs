using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: экран запуска игры — спокойный, как у консоли, без вспышек, бликов и прыгающих значков.
/// Арт игры во весь экран едва заметно приближается; слева снизу — логотип, одна строка о том,
/// что сейчас происходит, и три шага мелким текстом; по нижнему краю — тонкая полоса прогресса
/// цветом игры. Игра стартует сразу на третьем шаге; через пару секунд экран закрывается сам.
/// Выключается в Настройки → Интерфейс (или командой конфига anim_launch off).
/// </summary>
public sealed class LaunchScreen : Panel
{
    public enum State { Waiting, Working, Done, Warn, Failed, Skipped }
    public sealed record Result(bool Ok, string? Backup, string? Error, string? Note = null);

    sealed class Step
    {
        public readonly Border Dot = new() { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock Title = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        public string? Detail;
        public State State;
    }

    readonly GameState _g;
    readonly Color _accent;
    readonly Step[] _steps = [new(), new(), new()];
    readonly TextBlock _status = new() { FontSize = 17, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 720 };
    readonly TextBlock _eyebrow;
    readonly Border _bar;
    readonly Border _art;
    readonly StackPanel _result = new() { Orientation = Orientation.Horizontal, Spacing = 10, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
    double _target, _shown;
    readonly DispatcherTimer _tick;
    bool _closed;

    /// <summary>Экран закрыт (кнопкой, Esc или сам после запуска).</summary>
    public event Action? Closed;

    public LaunchScreen(GameState g)
    {
        _g = g;
        _accent = Color.TryParse(g.Def.Accent, out var c) ? c : Gx.Accent;
        Background = Ui.Hex("#050608");
        ClipToBounds = true;
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());

        _art = new Border
        {
            Child = Ui.GameImage(g.Def, 1600, art: Images.Art.Hero),
            RenderTransform = TransformOperations.Parse("scale(1.0)"),
            Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromSeconds(9), Easing = new SineEaseOut() }],
        };

        _eyebrow = new TextBlock { Text = I18n.T("launch.via").ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 2.4, Foreground = Ui.Hex("#9AA1AE") };
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 720);
        Control title = logo is null
            ? new TextBlock { Text = g.Def.Name, FontFamily = Look.Display, FontSize = 44, FontWeight = FontWeight.Bold, LetterSpacing = -1, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 }
            : new Image { Source = logo, MaxHeight = 120, MaxWidth = 440, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 22, OffsetX = 0, OffsetY = 0, Opacity = 0.35 } };

        var titles = new[] { I18n.T("launch.check"), I18n.T("launch.backup"), I18n.T("launch.start") };
        var steps = Ui.Row(18);
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Title.Text = titles[i];
            steps.Children.Add(Ui.Row(7, _steps[i].Dot, _steps[i].Title));
            SetStep(i, State.Waiting, null);
        }

        var played = PlayTime.Get(g.Def.Id);
        var meta = new TextBlock
        {
            Text = string.Join("  ·  ", new[] { g.ModCount > 0 ? I18n.T("v99.launch.mods", ("n", g.ModCount)) : null, played.TotalMs >= 60_000 ? PlayTime.Format(played.TotalMs) : null }.OfType<string>()),
            FontSize = 12.5, Foreground = Ui.Hex("#8E95A3"),
        };

        var left = new StackPanel
        {
            Spacing = 14, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(72, 0, 72, 64),
            Children = { _eyebrow, title, _status, steps, meta, _result },
        };

        // Полоса прогресса по нижнему краю.
        var track = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) };
        _bar = new Border { Height = 2, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(_accent) };

        var close = Ui.Button("", Close, "icon ghost", Icons.Close);
        close.Foreground = Ui.Hex("#C9CED8");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 20, 24, 0);

        Children.Add(_art);
        Children.Add(new Border { Background = Gx.ShadeLeft(0.8, 245) });
        Children.Add(new Border { Background = Gx.ShadeUp(0.3, 250) });
        Children.Add(left);
        Children.Add(track);
        Children.Add(_bar);
        Children.Add(close);

        AttachedToVisualTree += (_, _) =>
        {
            _art.RenderTransform = TransformOperations.Parse("scale(1.04)");
            if (Animate.On)
            {
                Opacity = 0;
                Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new CubicEaseOut() }];
                Dispatcher.UIThread.Post(() => Opacity = 1, DispatcherPriority.Background);
                Animate.From(left, "translateY(12px)", 420, 60, new CubicEaseOut());
            }
            _tick.Start();
        };
        DetachedFromVisualTree += (_, _) => _tick.Stop();
    }

    void Tick()
    {
        if (Math.Abs(_shown - _target) < 0.05) return;
        _shown += (_target - _shown) * (Animate.On ? 0.1 : 1);
        _bar.Width = Bounds.Width * _shown / 100;
    }

    /// <summary>Отметить шаг. Строка состояния показывает тот шаг, что идёт сейчас.</summary>
    public void SetStep(int index, State state, string? detail)
    {
        var s = _steps[index];
        s.State = state;
        s.Detail = detail;
        var (dot, text) = state switch
        {
            State.Working => ((IBrush)new SolidColorBrush(_accent), (IBrush)Brushes.White),
            State.Done => (Ui.Res("Good"), Ui.Hex("#C9CED8")),
            State.Warn => (Ui.Res("Warn"), Ui.Hex("#C9CED8")),
            State.Failed => (Ui.Res("Bad"), Brushes.White),
            State.Skipped => (new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Ui.Hex("#7D8492")),
            _ => (new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), Ui.Hex("#6E7583")),
        };
        s.Dot.Background = dot;
        s.Title.Foreground = text;
        if (state == State.Working || (state is State.Done or State.Warn && detail is not null))
            _status.Text = detail ?? s.Title.Text + "…";
        var done = _steps.Count(x => x.State is State.Done or State.Warn or State.Skipped);
        _target = Math.Min(100, done * 33.4 + (_steps.Any(x => x.State == State.Working) ? 14 : 0));
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
        await Task.Delay(300);
        if (_closed) return;
        SetStep(0, conflicts > 0 ? State.Warn : State.Done,
            mods == 0 ? I18n.T("launch.check.none")
            : conflicts > 0 ? I18n.T("launch.check.conflicts", ("n", mods), ("c", conflicts))
            : I18n.T("launch.check.ok", ("n", mods)));

        var backups = Backups.OnLaunch;
        SetStep(1, backups ? State.Working : State.Skipped, backups ? null : I18n.T("launch.backup.off"));
        await Task.Delay(backups ? 260 : 120);
        if (_closed) return; // закрыли до запуска — значит, передумали
        SetStep(2, State.Working, I18n.T("v10.launch.starting", ("game", _g.Def.ShortName)));
        var r = launch();
        if (backups) SetStep(1, r.Ok ? State.Done : State.Skipped, null);
        if (!r.Ok)
        {
            SetStep(2, State.Failed, null);
            ShowFailure(r.Error);
            return;
        }
        await Task.Delay(320);
        SetStep(2, State.Done, null);
        ShowSuccess(r.Note);
        await Task.Delay(2200);
        Close();
    }

    /// <summary>«Игра запущена»: полоса до конца, строка состояния и две тихие кнопки.</summary>
    public void ShowSuccess(string? note = null)
    {
        _target = 100;
        _eyebrow.Text = I18n.T("v99.launch.ready").ToUpperInvariant();
        _status.Text = I18n.T("launch.started") + (note is null ? "" : "  ·  " + note);
        _result.Children.Clear();
        var minimize = Ui.Button(I18n.T("launch.minimize"), () => { Close(); if (MainWindow.Current is { } w) w.WindowState = WindowState.Minimized; }, "hero-ghost", Icons.Minimize);
        var done = Ui.Button(I18n.T("launch.done"), Close, "hero-ghost", Icons.Check);
        _result.Children.Add(done);
        _result.Children.Add(minimize);
        if (Settings.Data.Bool("overlay", true))
            _result.Children.Add(new TextBlock
            {
                Text = I18n.T("launch.overlayHint", ("key", Hotkey.Display(Hotkey.KeyOf(Settings.Data.Str("overlayKey"))))),
                FontSize = 12.5, Foreground = Ui.Hex("#8E95A3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            });
        _result.IsVisible = true;
        if (Animate.On) Animate.From(_result, "translateY(8px)", 320, 0, new CubicEaseOut());
        else Tick();
    }

    void ShowFailure(string? error)
    {
        _bar.Background = Ui.Res("Bad");
        _status.Text = I18n.T("launch.failed") + (string.IsNullOrEmpty(error) ? "" : ": " + error);
        _status.TextWrapping = TextWrapping.Wrap;
        _result.Children.Clear();
        _result.Children.Add(Ui.Button(I18n.T("launch.done"), Close, "primary"));
        _result.IsVisible = true;
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
        SetStep(1, State.Done, null);
        SetStep(2, State.Working, I18n.T("v10.launch.starting", ("game", _g.Def.ShortName)));
    }

    public void DemoDone()
    {
        SetStep(2, State.Done, null);
        ShowSuccess(I18n.T("v10.clean.note", ("n", 4)));
    }
}
