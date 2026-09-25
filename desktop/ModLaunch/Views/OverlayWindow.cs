using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// Оверлей поверх игры: время сеанса, включённые моды, копия сохранений, друзья
/// в сети и заметки. Открывается сочетанием клавиш, закрывается им же или Esc.
/// </summary>
public sealed class OverlayWindow : Window
{
    public static OverlayWindow? Current { get; private set; }
    public static string? GameId { get; set; }
    public static DateTime? StartedAt { get; set; }
    public static bool Preview { get; set; }

    readonly DispatcherTimer _clock;
    TextBlock? _session;

    public OverlayWindow()
    {
        Current = this;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        Deactivated += (_, _) => Hide();
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Tick());
    }

    public static void Toggle()
    {
        var w = Current ?? new OverlayWindow();
        if (w.IsVisible) w.Hide();
        else w.ShowOver();
    }

    void ShowOver()
    {
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is not null)
        {
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width / screen.Scaling;
            Height = screen.Bounds.Height / screen.Scaling;
        }
        Build();
        Show();
        Activate();
        _clock.Start();
        if (Account.SignedIn) _ = Friends.Refresh().ContinueWith(_ => Dispatcher.UIThread.Post(Build));
    }

    public new void Hide()
    {
        _clock.Stop();
        base.Hide();
        Preview = false;
    }

    void Tick()
    {
        if (_session is not null && StartedAt is DateTime s) _session.Text = Clock(DateTime.UtcNow - s);
    }

    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    void Build()
    {
        var g = GameId is null ? null : AppState.Game(GameId);
        var panel = Ui.Col(16);

        // Игра и время.
        if (g is null) panel.Children.Add(Ui.Text(I18n.T("ovl.noGame"), "h2", color: Brushes.White));
        else
        {
            panel.Children.Add(Ui.Text(Preview ? I18n.T("ovl.preview") : I18n.T("ovl.playing"), "small", color: Ui.Hex("#AAB2C2")));
            panel.Children.Add(Ui.Text(g.Def.Name, "h1", color: Brushes.White));
            _session = new TextBlock { Text = StartedAt is DateTime s ? Clock(DateTime.UtcNow - s) : "00:00", FontSize = 44, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2") };
            panel.Children.Add(_session);
            panel.Children.Add(Ui.Text(I18n.T("ovl.total", ("time", PlayTime.Format(PlayTime.Get(g.Def.Id).TotalMs))), "small", color: Ui.Hex("#C9CFDB")));
            var mods = g.Registry?.List().Where(m => m.Bool("enabled", true) && m.Str("kind") != "preset").Select(m => m.Str("name") ?? "").ToList() ?? [];
            panel.Children.Add(Ui.Text(mods.Count == 0 ? I18n.T("ovl.noMods") : I18n.T("ovl.mods." + I18n.Plural(mods.Count, "one", "few", "many"), ("n", mods.Count)) + ": " + string.Join(", ", mods.Take(8)), "small", color: Ui.Hex("#C9CFDB"), wrap: true));

            var last = Backups.List(g.Def.Id).FirstOrDefault();
            var backupHint = Ui.Text(last is null ? "" : I18n.T("ovl.backup.last", ("time", last.At.ToString("t"))), "small", color: Ui.Hex("#AAB2C2"));
            var backup = Ui.Button(I18n.T("ovl.backup"), () =>
            {
                try
                {
                    var made = Backups.Create(g.Def.Id, g.Path is null ? null : Features.Launcher.SavesDir(g.Def, g.Path), "manual")
                               ?? throw new InvalidOperationException(I18n.T("err.BACKUP_NO_SAVES"));
                    backupHint.Text = I18n.T("ovl.backup.done", ("time", made.At.ToString("t")));
                }
                catch (Exception e) { backupHint.Text = e.Message; }
            }, "primary", Icons.Save);
            panel.Children.Add(Ui.Row(10, backup, backupHint));

            // Заметки к игре (settings.notes, как в 3.x).
            var notes = new TextBox { Text = Settings.Data.Obj("notes").Str(g.Def.Id) ?? "", Watermark = I18n.T("ovl.notes.placeholder"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, MaxLength = 4000 };
            var saved = Ui.Text("", "small", color: Ui.Res("Good"));
            notes.LostFocus += (_, _) =>
            {
                Settings.Data.Obj("notes")[g.Def.Id] = notes.Text ?? "";
                Settings.Save();
                saved.Text = I18n.T("ovl.notes.saved");
            };
            panel.Children.Add(Ui.Col(6, Ui.Row(8, Ui.Text(I18n.T("ovl.notes"), "h3", color: Brushes.White), saved), notes));
        }

        // Друзья в сети.
        var friends = Ui.Col(8, Ui.Text(I18n.T("friends.title"), "h3", color: Brushes.White));
        if (!Account.SignedIn) friends.Children.Add(Ui.Text(I18n.T("ovl.friends.signin"), "small", color: Ui.Hex("#AAB2C2"), wrap: true));
        else
        {
            var online = Friends.View().Friends.Where(f => f.State != "offline").ToList();
            if (online.Count == 0) friends.Children.Add(Ui.Text(I18n.T("ovl.friends.none"), "small", color: Ui.Hex("#AAB2C2")));
            foreach (var f in online.Take(10))
                friends.Children.Add(Ui.Row(8, Ui.Dot(f.State == "playing" ? Ui.Res("Brand2") : Ui.Res("Good")), Ui.Text(f.Name, color: Brushes.White),
                    Ui.Text(f.State == "playing" ? I18n.T("friends.playing", ("game", f.GameName)) : I18n.T("friends.online"), "small", color: Ui.Hex("#AAB2C2"))));
        }

        var footer = Ui.Row(10,
            Ui.Button(I18n.T("ovl.open"), () => { Hide(); var w = MainWindow.Current; if (w is not null) { w.WindowState = WindowState.Normal; w.Activate(); } }, "", Icons.Home),
            Ui.Button(I18n.T("ovl.close"), Hide, "ghost", Icons.Close),
            Ui.Text(I18n.T("ovl.hint", ("key", Hotkey.Display(Hotkey.KeyOf(Settings.Data.Str("overlayKey"))))), "small", color: Ui.Hex("#AAB2C2")));
        foreach (var c in footer.Children) c.VerticalAlignment = VerticalAlignment.Center;

        var card = new Border
        {
            Width = 420,
            Margin = new Thickness(28),
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.Parse("#E6171A21")),
            BorderBrush = Ui.Res("Line"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new DockPanel { Children = { Dock(footer), new ScrollViewer { Content = Ui.Col(20, panel, friends) } } },
        };
        var shade = new Border { Background = new SolidColorBrush(Color.Parse("#8C000000")) };
        shade.PointerPressed += (_, _) => Hide();
        Content = new Panel { Children = { shade, card } };

        static Control Dock(Control c) { DockPanel.SetDock(c, Avalonia.Controls.Dock.Bottom); return c; }
    }

    /// <summary>Игра запущена — занять сочетание клавиш; закрыта — освободить.</summary>
    public static void OnGameStarted(string gameId)
    {
        GameId = gameId;
        StartedAt = DateTime.UtcNow;
        if (Settings.Data.Bool("overlay", true)) Hotkey.Arm(Settings.Data.Str("overlayKey") ?? Hotkey.Keys[0], Toggle);
    }

    public static void OnGameExited()
    {
        Hotkey.Disarm();
        Current?.Hide();
        StartedAt = null;
    }
}
