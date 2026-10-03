using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Экран запуска игры: картинка игры на всё окно, шаги «моды → копия сохранений →
/// запуск» с галочками и итог «Игра запущена!» с подсказкой про оверлей.
/// Игра стартует сразу на третьем шаге — анимация запуск не задерживает.
/// </summary>
public sealed class LaunchScreen : Panel
{
    public enum State { Waiting, Working, Done, Warn, Failed, Skipped }
    public sealed record Result(bool Ok, string? Backup, string? Error);

    sealed class Step
    {
        public readonly Border Mark = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock Title = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
        public readonly TextBlock Detail = new() { FontSize = 12.5, Foreground = Ui.Hex("#AAB2C2"), TextTrimming = TextTrimming.CharacterEllipsis };
        public State State;
    }

    readonly GameState _g;
    readonly Step[] _steps = [new(), new(), new()];
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, Height = 6, MinHeight = 6 };
    readonly StackPanel _result = new() { Spacing = 14, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Border _art;
    readonly DispatcherTimer _spin;
    Control? _spinning;
    double _angle;
    bool _closed;

    /// <summary>Экран закрыт (кнопкой, Esc или сам после запуска).</summary>
    public event Action? Closed;

    public LaunchScreen(GameState g)
    {
        _g = g;
        Background = Brushes.Black;
        // Крутилка у шага «идёт»: таймер нужен раньше первого SetStep.
        _spin = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            if (_spinning is null) return;
            _angle = (_angle + 7) % 360;
            _spinning.RenderTransform = new RotateTransform(_angle);
        });
        _spin.Stop();

        // Картинка игры: размыта и медленно приближается.
        _art = new Border
        {
            Child = Ui.GameImage(g.Def, 1400, art: Images.Art.Hero),
            Effect = new BlurEffect { Radius = 18 },
            RenderTransform = TransformOperations.Parse("scale(1.06)"),
            Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromSeconds(8), Easing = new SineEaseOut() }],
        };
        var shade = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#B30F1116"), 0), new GradientStop(Color.Parse("#E60F1116"), 0.55), new GradientStop(Color.Parse("#FA0F1116"), 1) },
            },
        };

        // Логотип игры (как в шапке страницы игры) или название.
        var logo = Images.GameAsset(g.Def, Images.Art.Logo, 640);
        Control title = logo is null
            ? new TextBlock { Text = g.Def.Name, FontSize = 38, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
            : new Image { Source = logo, MaxHeight = 120, MaxWidth = 440, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };

        _bar.Foreground = Ui.Res("Brand");
        _bar.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        _bar.Transitions = [new DoubleTransition { Property = RangeBase.ValueProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = new CubicEaseOut() }];

        var titles = new[] { I18n.T("launch.check"), I18n.T("launch.backup"), I18n.T("launch.start") };
        var rows = Ui.Col(16);
        for (var i = 0; i < _steps.Length; i++)
        {
            var s = _steps[i];
            s.Title.Text = titles[i];
            var words = Ui.Col(2, s.Title, s.Detail);
            words.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
            row.Children.Add(s.Mark);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            rows.Children.Add(row);
            SetStep(i, State.Waiting, null);
        }
        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#B3171A21")), CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 20),
            Child = rows,
        };

        var center = new StackPanel
        {
            Spacing = 22, Width = 520, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                title,
                new TextBlock { Text = I18n.T("launch.via"), FontSize = 13, Foreground = Ui.Hex("#AAB2C2"), HorizontalAlignment = HorizontalAlignment.Center },
                _bar,
                card,
                _result,
            },
        };

        var close = Ui.Button("", Close, "icon ghost", Icons.Close);
        close.Foreground = Brushes.White;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 20, 24, 0);

        Children.Add(_art);
        Children.Add(shade);
        Children.Add(center);
        Children.Add(close);

        AttachedToVisualTree += (_, _) =>
        {
            _art.RenderTransform = TransformOperations.Parse("scale(1.16)");
            Animate.From(center, "translateY(24px)", 420);
        };
        DetachedFromVisualTree += (_, _) => _spin.Stop();
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
            State.Working => ((IBrush)new SolidColorBrush(Color.FromArgb(90, 124, 92, 255)), Icons.Refresh, (IBrush)Brushes.White),
            State.Done => (Ui.Res("Good"), Icons.Check, Ui.Hex("#0F1116")),
            State.Warn => (Ui.Res("Warn"), Icons.Alert, Ui.Hex("#0F1116")),
            State.Failed => (Ui.Res("Bad"), Icons.Close, Brushes.White),
            State.Skipped => (new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), Icons.Minimize, Ui.Hex("#AAB2C2")),
            _ => (new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), (string?)null, Brushes.White),
        };
        s.Mark.Background = back;
        s.Mark.Child = icon is null ? Ui.Dot(Ui.Hex("#6B7385"), 6) : Ui.Icon(icon, 15, stroke);
        if (state == State.Working)
        {
            _spinning = s.Mark.Child;
            if (!_spin.IsEnabled) _spin.Start();
        }
        else if (_steps.All(x => x.State != State.Working))
        {
            _spinning = null;
            _spin.Stop();
        }
        if (state is State.Done or State.Warn && Animate.On) Animate.From(s.Mark, "scale(0.6)", 320, easing: new BackEaseOut());
        var done = _steps.Count(x => x.State is State.Done or State.Warn or State.Skipped);
        _bar.Value = Math.Min(100, done * 33.4 + (_steps.Any(x => x.State == State.Working) ? 12 : 0));
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
        await Task.Delay(420);
        if (_closed) return;
        SetStep(0, conflicts > 0 ? State.Warn : State.Done,
            mods == 0 ? I18n.T("launch.check.none")
            : conflicts > 0 ? I18n.T("launch.check.conflicts", ("n", mods), ("c", conflicts))
            : I18n.T("launch.check.ok", ("n", mods)));

        var backups = Backups.OnLaunch;
        SetStep(1, backups ? State.Working : State.Skipped, backups ? null : I18n.T("launch.backup.off"));
        if (backups) await Task.Delay(300);
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
        await Task.Delay(380);
        SetStep(2, State.Done, _g.Def.Name);
        ShowSuccess();
        await Task.Delay(2800);
        Close();
    }

    /// <summary>«Игра запущена!»: большая галочка, подсказка про оверлей, «Свернуть» и «Готово».</summary>
    public void ShowSuccess()
    {
        _bar.Value = 100;
        var check = new Border
        {
            Width = 68, Height = 68, CornerRadius = new CornerRadius(34), Background = Ui.Res("Good"), HorizontalAlignment = HorizontalAlignment.Center,
            Child = Ui.Icon(Icons.Check, 32, Ui.Hex("#0F1116")),
        };
        _result.Children.Clear();
        _result.Children.Add(check);
        _result.Children.Add(new TextBlock { Text = I18n.T("launch.started"), FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        if (Settings.Data.Bool("overlay", true))
            _result.Children.Add(new TextBlock
            {
                Text = I18n.T("launch.overlayHint", ("key", Hotkey.Display(Hotkey.KeyOf(Settings.Data.Str("overlayKey"))))),
                FontSize = 14, Foreground = Ui.Hex("#C9CFDB"), HorizontalAlignment = HorizontalAlignment.Center,
            });
        var minimize = Ui.Button(I18n.T("launch.minimize"), () => { Close(); if (MainWindow.Current is { } w) w.WindowState = WindowState.Minimized; }, "", Icons.Minimize);
        var done = Ui.Button(I18n.T("launch.done"), Close, "primary", Icons.Check);
        var buttons = Ui.Row(10, minimize, done);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        buttons.Margin = new Thickness(0, 6, 0, 0);
        _result.Children.Add(buttons);
        _result.IsVisible = true;
        Animate.From(check, "scale(0.3)", 460, easing: new BackEaseOut());
        Animate.From(_result, "translateY(16px)", 380);
    }

    void ShowFailure(string? error)
    {
        _result.Children.Clear();
        _result.Children.Add(new Border
        {
            Width = 68, Height = 68, CornerRadius = new CornerRadius(34), Background = Ui.Res("Bad"), HorizontalAlignment = HorizontalAlignment.Center,
            Child = Ui.Icon(Icons.Close, 30, Brushes.White),
        });
        _result.Children.Add(new TextBlock { Text = I18n.T("launch.failed"), FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        if (!string.IsNullOrEmpty(error))
            _result.Children.Add(new TextBlock { Text = error, FontSize = 13.5, Foreground = Ui.Hex("#C9CFDB"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 500 });
        var done = Ui.Button(I18n.T("launch.done"), Close, "primary");
        done.HorizontalAlignment = HorizontalAlignment.Center;
        _result.Children.Add(done);
        _result.IsVisible = true;
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _spin.Stop();
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
