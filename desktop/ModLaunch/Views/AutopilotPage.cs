using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: «Автопилот» — оценка сборки из 100 и план починки. Слева большое кольцо с оценкой,
/// справа шаги: что автопилот сделает сам (с галочкой) и что лучше решить вам (с кнопкой).
/// «Запустить автопилот» — снимок «как было», затем шаги по очереди с отметками, в конце — новая оценка.
/// Сверху — ваши игры с их оценками, чтобы переключаться между сборками.
/// </summary>
public sealed class AutopilotPage : Page
{
    string? _id;
    bool _running;
    readonly Dictionary<string, string> _state = [];

    public AutopilotPage(string? gameId = null) => _id = gameId;

    public override string Title => I18n.T("v10.ap.title");
    public override string? GameId => null;

    static List<GameState> Games() => MainWindow.OrderedGames()
        .Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft && g.Registry is not null && !GameCollections.IsHidden(g.Def.Id))
        .OrderByDescending(g => PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).ToList();

    public override void Build()
    {
        var games = Games();
        var col = StoreKit.Column(spacing: 18, top: 22);
        col.Children.Add(Ui.Col(6,
            new TextBlock { Text = I18n.T("v10.ap.title"), FontFamily = Look.Display, FontSize = 30, FontWeight = FontWeight.Bold, LetterSpacing = -0.8 },
            Ui.Text(I18n.T("v10.ap.lead"), "muted", wrap: true)));
        if (games.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Text(I18n.T("v10.w.ap.none"), "muted", wrap: true), 24));
            Content = new ScrollViewer { Content = col };
            return;
        }
        var g = games.FirstOrDefault(x => x.Def.Id == _id) ?? games[0];
        _id = g.Def.Id;
        col.Children.Add(Picker(games, g));
        var (score, steps) = Autopilot.Plan(g);
        col.Children.Add(Main(g, score, steps));
        col.Children.Add(Optimize(g));
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Control Picker(List<GameState> games, GameState current)
    {
        var row = new WrapPanel();
        foreach (var x in games)
        {
            var s = Autopilot.Plan(x, quick: true).Score;
            var on = x.Def.Id == current.Def.Id;
            var pic = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = Ui.GameImage(x.Def, 96, art: Images.Art.Cover) };
            var chip = new Border
            {
                Classes = { "store-tile" }, CornerRadius = new CornerRadius(999), Padding = new Thickness(6, 5, 14, 5), Margin = new Thickness(0, 0, 8, 8),
                Background = on ? Ui.Res("BrandSoft") : Ui.Res("Surface"), BorderBrush = on ? Ui.Res("Brand") : Ui.Res("Line"),
                Child = Ui.Row(9, pic, new TextBlock { Text = x.Def.ShortName, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = s.ToString(), FontWeight = FontWeight.Bold, Foreground = HomePage.ScoreBrush(s), VerticalAlignment = VerticalAlignment.Center }),
            };
            var id = x.Def.Id;
            StoreKit.OnClick(chip, () => { if (_running) return; _id = id; _state.Clear(); Build(); });
            row.Children.Add(chip);
        }
        return row;
    }

    static Panel ScoreRing(int score, double size)
    {
        var color = HomePage.ScoreBrush(score);
        var ring = new Panel { Width = size, Height = size };
        ring.Children.Add(new Ellipse { Width = size, Height = size, Stroke = Ui.Res("Surface3"), StrokeThickness = 10 });
        ring.Children.Add(new Arc { Width = size, Height = size, StartAngle = -90, SweepAngle = 360 * Math.Max(0.01, score / 100.0), Stroke = color, StrokeThickness = 10, StrokeLineCap = PenLineCap.Round });
        ring.Children.Add(Ui.Col(0,
            new TextBlock { Text = score.ToString(), FontFamily = Look.Display, FontSize = size * 0.3, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Res("Text") },
            new TextBlock { Text = I18n.T("v10.ap.of100"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Res("Faint") }));
        ((StackPanel)ring.Children[2]).VerticalAlignment = VerticalAlignment.Center;
        return ring;
    }

    Control Main(GameState g, int score, List<PilotStep> steps)
    {
        var ring = ScoreRing(score, 168);
        ring.HorizontalAlignment = HorizontalAlignment.Center;
        var grade = new TextBlock { Text = I18n.T("v10.ap.grade." + Autopilot.Grade(score)), FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        var gameName = new TextBlock { Text = g.Def.Name, FontSize = 13, Foreground = Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center };
        var auto = steps.Where(s => s.Auto).ToList();
        var run = Ui.Button(auto.Count > 0 ? I18n.T("v10.ap.run", ("n", auto.Count)) : I18n.T("v10.ap.recheck"), () => _ = Run(g, auto), "primary", auto.Count > 0 ? Icons.Wand : Icons.Refresh);
        run.HorizontalAlignment = HorizontalAlignment.Stretch;
        run.HorizontalContentAlignment = HorizontalAlignment.Center;
        run.IsEnabled = !_running;
        var undo = Snapshots.List(g.Def.Id).FirstOrDefault(s => s.Reason == "autopilot");
        var left = Ui.Col(10, ring, grade, gameName, new Border { Height = 6 }, run);
        if (undo is not null)
        {
            var back = Ui.Button(I18n.T("v10.ap.undo"), () => Actions.RestoreSnapshot(g, undo), "ghost", Icons.Undo, I18n.T("v10.ap.undo.tip", ("when", Ui.Ago(undo.At))));
            back.HorizontalAlignment = HorizontalAlignment.Center;
            left.Children.Add(back);
        }
        left.Width = 240;

        var right = Ui.Col(8);
        if (steps.Count == 0)
            right.Children.Add(new Border
            {
                Padding = new Thickness(18), CornerRadius = new CornerRadius(14), Background = Ui.Res("Surface2"),
                Child = Ui.Col(6, Ui.Row(8, Ui.Icon(Icons.Check, 18, Ui.Res("Good")), Ui.Text(I18n.T("v10.ap.clean"), "h3")), Ui.Text(I18n.T("v10.ap.clean.text"), "small muted", wrap: true)),
            });
        else
        {
            if (auto.Count > 0) right.Children.Add(Gx.Eyebrow(I18n.T("v10.ap.auto"), Ui.Res("Muted"), 11));
            foreach (var s in auto) right.Children.Add(StepRow(g, s));
            var manual = steps.Where(s => !s.Auto).ToList();
            if (manual.Count > 0) right.Children.Add(new Border { Height = 6 });
            if (manual.Count > 0) right.Children.Add(Gx.Eyebrow(I18n.T("v10.ap.manual"), Ui.Res("Muted"), 11));
            foreach (var s in manual) right.Children.Add(StepRow(g, s));
        }
        var toggle = new ToggleSwitch { IsChecked = Autopilot.BeforePlay, OnContent = "", OffContent = "", MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => { Settings.Data["autopilotBeforePlay"] = toggle.IsChecked == true; Settings.Save(); };
        var before = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(toggle, Dock.Right);
        before.Children.Add(toggle);
        before.Children.Add(Ui.Col(2, Ui.Text(I18n.T("v10.ap.before"), "h3"), Ui.Text(I18n.T("v10.ap.before.hint"), "small muted", wrap: true)));
        right.Children.Add(before);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 34 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return Ui.Card(grid, 26);
    }

    Control StepRow(GameState g, PilotStep s)
    {
        var state = _state.GetValueOrDefault(s.Kind);
        var color = s.Level switch { HealthLevel.Critical => Ui.Res("Bad"), HealthLevel.Warning => Ui.Res("Warn"), _ => Ui.Res("Brand2") };
        Control mark = state switch
        {
            "done" => new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Res("Good"), Child = Ui.Icon(Icons.Check, 14, Ui.Hex("#0F1116")) },
            "work" => new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Refresh, 14, Ui.Res("Brand2")) },
            "fail" => new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Res("Bad"), Child = Ui.Icon(Icons.Close, 14, Brushes.White) },
            _ => new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderBrush = color, BorderThickness = new Thickness(2), Child = Ui.Dot(color, 7) },
        };
        mark.VerticalAlignment = VerticalAlignment.Top;
        var words = Ui.Col(3, Ui.Row(8, Ui.Text(s.Title, "h3"), Gx.Tag("−" + s.Penalty, Ui.Res("Surface3"), Ui.Res("Muted"), 11)));
        if (s.Optimize) ((StackPanel)words.Children[0]).Children.Add(Gx.Tag(I18n.T("v10.ap.opt"), Ui.Res("BrandSoft"), Ui.Res("Brand2"), 11));
        if (s.Text != "") words.Children.Add(new TextBlock { Text = s.Text, FontSize = 12.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(mark);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        if (!s.Auto && Manual(g, s) is { } fix)
        {
            fix.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fix, 2);
            grid.Children.Add(fix);
        }
        return new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface2"), Child = grid };
    }

    Control? Manual(GameState g, PilotStep s)
    {
        var w = MainWindow.Current!;
        var registry = g.Registry;
        return s.Kind switch
        {
            "log" when s.Mods.Count > 0 => Ui.Button(I18n.T("health.fix.disable"), () => { foreach (var id in s.Mods) try { registry?.SetEnabled(id, false); } catch { } AppState.Notify(); Build(); }, "", Icons.Power),
            "log" or "crash" => Ui.Button(I18n.T("crash.log"), () => w.Navigate(() => new GamePage(g.Def.Id, "log")), "ghost", Icons.Alert),
            "updates" => Ui.Button(I18n.T("health.fix.show"), () => w.Navigate(() => new GamePage(g.Def.Id, "installed")), "ghost", Icons.ArrowUp),
            "write" => Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(g.Path), "ghost", Icons.Folder),
            "conflicts" => Ui.Button(I18n.T("health.fix.show"), () => w.Navigate(() => new GamePage(g.Def.Id, "health")), "ghost", Icons.List),
            _ => null,
        };
    }

    /// <summary>Пройти безопасные шаги по очереди: снимок, затем каждый с отметкой «идёт» → «готово».</summary>
    async Task Run(GameState g, List<PilotStep> steps)
    {
        if (_running) return;
        var w = MainWindow.Current!;
        if (steps.Count == 0) { _state.Clear(); Build(); w.Toast(I18n.T("health.checked")); return; }
        if (Launcher.IsRunning(g.Def.Id)) { w.Toast(I18n.T("vanilla.running"), bad: true); return; }
        var before = Autopilot.Score(Autopilot.Plan(g).Steps);
        _running = true;
        Snapshots.Take(g, "autopilot");
        var registry = g.Registry!;
        foreach (var s in steps)
        {
            _state[s.Kind] = "work";
            Build();
            await Task.Delay(Program.Screenshot ? 0 : 260);
            try
            {
                switch (s.Kind)
                {
                    case "loader": Actions.InstallLoader(g); break;
                    case "deps": await Actions.InstallMissing(g); break;
                    case "depsOff": foreach (var id in s.Mods) registry.SetEnabled(id, true); break;
                    case "conflicts" or "unused": foreach (var id in s.Mods) try { registry.SetEnabled(id, false); } catch { } break;
                    case "files":
                        registry.Reconcile();
                        foreach (var id in s.Mods) if (registry.Get(id) is { } mod && mod.Bool("missing")) registry.Remove(id);
                        break;
                    case "unmanaged": Actions.AdoptAll(g, registry.Unmanaged()); break;
                }
                _state[s.Kind] = "done";
            }
            catch { _state[s.Kind] = "fail"; }
        }
        _running = false;
        AppState.Notify();
        var after = Autopilot.Plan(g).Score;
        Build();
        w.Toast(I18n.T("v10.ap.result", ("from", before), ("to", after)));
    }

    /// <summary>Оптимизация: сколько модов включено и выключено, сколько места занимают, есть ли лишнее.</summary>
    static Control Optimize(GameState g)
    {
        var registry = g.Registry!;
        var list = registry.List().Where(m => m.Str("kind") != "preset").ToList();
        var on = list.Count(m => m.Bool("enabled", true));
        var off = list.Count - on;
        long size = 0;
        try { size = Conflicts.FolderSize(registry.ModsDir); } catch { }
        var unused = 0;
        try { unused = DepGraph.Build(registry).Unused().Count; } catch { }
        Control Stat(string value, string label) => Gx.Stat(value, label, null, 24);
        var stats = Ui.Row(34, Stat(on.ToString(), I18n.T("v10.ap.on")), Stat(off.ToString(), I18n.T("v10.ap.off")), Stat(GamePage.Size(size), I18n.T("v10.ap.size")), Stat(unused.ToString(), I18n.T("v10.ap.unused")));
        var tips = Ui.Col(6);
        if (off > 15) tips.Children.Add(Ui.Text(I18n.T("v10.ap.tip.off", ("n", off)), "small muted", wrap: true));
        if (unused > 0) tips.Children.Add(Ui.Text(I18n.T("v10.ap.tip.unused", ("n", unused)), "small muted", wrap: true));
        if (!CleanBoot.On) tips.Children.Add(Ui.Text(I18n.T("v10.ap.tip.clean"), "small muted", wrap: true));
        if (tips.Children.Count == 0) tips.Children.Add(Ui.Text(I18n.T("v10.ap.tip.ok"), "small muted", wrap: true));
        return Ui.Card(Ui.Col(14, Ui.Row(8, Ui.Icon(Icons.Zap, 17, Ui.Res("Brand2")), Ui.Text(I18n.T("v10.ap.optTitle"), "h2")), stats, tips), 24);
    }
}
