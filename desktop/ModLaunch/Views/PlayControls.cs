using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Кнопка «Играть» со свечением и выбором профиля, и плашка «Запущено» с таймером сеанса.</summary>
public static class PlayControls
{
    public static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>«● Запущено 12:34» — пока игра идёт; время тикает раз в секунду, пока плашка на экране.</summary>
    public static Control RunningPill(string gameId, bool big = true)
    {
        var size = big ? 16 : 13;
        var dot = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Ui.Res("Good"), Classes = { "pulse" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock { Text = I18n.T("run.running"), FontSize = size, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Good"), VerticalAlignment = VerticalAlignment.Center };
        var time = new TextBlock { FontSize = size, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        void Tick() => time.Text = Launcher.StartedAt(gameId) is DateTime s ? Clock(DateTime.UtcNow - s) : "";
        Tick();
        var pill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(46, 61, 214, 140)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 61, 214, 140)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = big ? new Thickness(20, 12) : new Thickness(12, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Row(10, dot, label, time),
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Tick();
        pill.AttachedToVisualTree += (_, _) => timer.Start();
        pill.DetachedFromVisualTree += (_, _) => timer.Stop();
        return pill;
    }

    /// <summary>
    /// Большая кнопка «Играть»: светится цветом акцента, а если у игры есть профили —
    /// рядом стрелка выбора профиля перед запуском (как в Modrinth App).
    /// </summary>
    public static Control PlayButton(GameState g)
    {
        var play = Ui.Button(I18n.T("games.play"), () => Actions.Play(g), "primary", Icons.Play);
        play.FontSize = 17;
        play.Padding = new Thickness(30, 13);
        Control body = play;

        var profiles = g.Registry is null ? [] : Profiles.List(g.Def.Id);
        var vanilla = g.Path is not null && Vanilla.Supported(g.Def, g.Path);
        if (profiles.Count > 0 || vanilla)
        {
            play.CornerRadius = new CornerRadius(12, 0, 0, 12);
            var chevron = new Button
            {
                Classes = { "primary" }, Padding = new Thickness(12, 0), CornerRadius = new CornerRadius(0, 12, 12, 0),
                VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
                Content = Ui.Icon(Icons.ChevronDown, 16),
            };
            ToolTip.SetTip(chevron, I18n.T(profiles.Count > 0 ? "profile.play" : "vanilla.menu"));
            var menu = new MenuFlyout();
            foreach (var p in profiles)
            {
                var name = p.Name;
                var item = new MenuItem { Header = p.Name, Icon = p.Active ? Ui.Icon(Icons.Check, 14) : null };
                item.Click += (_, _) => Guard.Later(() =>
                {
                    try
                    {
                        var (on, off, _) = Profiles.Apply(g.Def.Id, name, g.Registry!);
                        MainWindow.Current?.Toast(I18n.T("profile.applied", ("name", name), ("on", on), ("off", off)));
                        AppState.Notify();
                    }
                    catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
                });
                menu.Items.Add(item);
            }
            // Как «Play Vanilla» в CurseForge и Purge в Vortex: без модов — на один раз или пока не вернёшь.
            if (vanilla)
            {
                if (profiles.Count > 0) menu.Items.Add(new Separator());
                var purged = Vanilla.IsPurged(g.Def.Id);
                if (!purged)
                {
                    var plain = new MenuItem { Header = I18n.T("vanilla.play"), Icon = Ui.Icon(Icons.Play, 14) };
                    plain.Click += (_, _) => Guard.Later(() => Actions.Play(g, vanilla: true));
                    menu.Items.Add(plain);
                }
                var clean = new MenuItem { Header = I18n.T(purged ? "vanilla.restore" : "vanilla.purge"), Icon = Ui.Icon(purged ? Icons.Refresh : Icons.EyeOff, 14) };
                clean.Click += (_, _) => Guard.Later(() => { if (purged) GamePage.RestorePurge(g); else GamePage.ConfirmPurge(g); });
                menu.Items.Add(clean);
            }
            chevron.Flyout = menu;
            body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Children = { play, chevron } };
        }

        var accent = Color.Parse(Look.Accent);
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 10, Blur = 30, Spread = -6, Color = Color.FromArgb(0xB3, accent.R, accent.G, accent.B) }),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = body,
        };
    }
}
