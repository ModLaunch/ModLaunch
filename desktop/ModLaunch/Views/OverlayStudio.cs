using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 23.0: «Создать мод» в оверлее игры. Инструменты — правила простыми словами: выбрал, вписал число — правило
/// легло в мод этой игры. «Поставить в игру» ставит мод сразу (как F6 в среде); многие загрузчики берут моды
/// при запуске, поэтому рядом — «Перезапустить игру». Перед первой установкой из игры включается защита
/// прогресса (копия сохранений «до»). В играх с античитом инструменты не работают — так и написано.
/// Полная среда — «Открыть в Creator Hub».
/// </summary>
public static class OverlayStudio
{
    static readonly IBrush Ink = Brushes.White;
    static readonly IBrush Soft = Ui.Hex("#C9CFDB");
    static readonly IBrush Faint = Ui.Hex("#8C93A3");
    static readonly IBrush Plate = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
    static readonly IBrush Rim = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

    /// <summary>Выбранный инструмент и последний результат (на время показа оверлея).</summary>
    static string? _tool;
    static string? _note;
    static bool _noteOk;

    public static Control Panel(GameState g, Action back, Action rebuild)
    {
        var col = Ui.Col(16);
        var head = Ui.Row(10, Ui.Button("", back, "icon ghost", Icons.ChevronLeft, I18n.T("v23.form.back")),
            Ui.Col(2, Ui.Text(I18n.T("v23.ig.title"), "h2", color: Ink), Ui.Text(g.Def.Name, "small", color: Faint)));
        col.Children.Add(head);

        if (g.Path is null) { col.Children.Add(Ui.Text(I18n.T("ovl.noGame"), color: Soft, wrap: true)); return col; }

        // Античит — ничего не ставим: так аккаунт игрока в безопасности, а ModLaunch не помогает читерить.
        if (MX.Engines.FindAntiCheat(g.Path) is { } ac)
        {
            col.Children.Add(Note(Icons.Shield, I18n.T("v23.ig.anticheat", ("ac", ac)), Ui.Res("Bad")));
            return col;
        }
        // 23.0: конструктор внутри самой игры (Unity с BepInEx) — главный путь; правила ниже — быстрые мелочи.
        if (DevMode.Unsupported(g.Path) is null)
        {
            if (DevMode.Active(g.Path)) col.Children.Add(Note(Icons.Cube, I18n.T(DevMode.Key(g.Path, "inside")), Ui.Res("Good")));
            else
            {
                var restartDev = Ui.Button(I18n.T("v23.dev.restart"), () =>
                {
                    Launcher.Stop(g.Def.Id);
                    OverlayWindow.Current?.Hide();
                    DispatcherTimer.RunOnce(() => DevLaunch.Start(g), TimeSpan.FromSeconds(3));
                }, "", Icons.Cube);
                restartDev.Background = HubLook.Amber;
                restartDev.Foreground = HubLook.OnAmber;
                col.Children.Add(new Border
                {
                    Background = Plate, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12),
                    Child = Ui.Col(8, Ui.Text(I18n.T("v23.dev.title"), "h3", color: Ink), Ui.Text(I18n.T("v23.dev.overlay"), "small", color: Soft, wrap: true), restartDev),
                });
            }
        }
        var bridge = CreatorPage.BridgeFor(g.Path, g.Def);
        if (bridge is null)
        {
            col.Children.Add(Ui.Row(10, new ProgressBar { IsIndeterminate = true, Width = 80, VerticalAlignment = VerticalAlignment.Center }, Ui.Text(I18n.T("v16.bridge.scanning"), "small", color: Soft)));
            DispatcherTimer.RunOnce(rebuild, TimeSpan.FromSeconds(1));
            return col;
        }
        if (!bridge.Code)
        {
            col.Children.Add(Note(Icons.Info, I18n.T("v23.ig.files"), Ui.Res("Warn")));
            col.Children.Add(Ui.Button(I18n.T("v23.ig.openHub"), () => OpenHub(null), "primary", Icons.Creator));
            return col;
        }

        var p = InGame.ModFor(g, bridge);
        var rules = InGame.RuleCount(p);
        var modLine = new DockPanel();
        var open = Ui.Button(I18n.T("v23.ig.openHub"), () => OpenHub(p), "ghost", Icons.Creator);
        DockPanel.SetDock(open, Dock.Right);
        modLine.Children.Add(open);
        modLine.Children.Add(Ui.Col(2, Ui.Text(p.Name, "h3", color: Ink), Ui.Text(I18n.T("v23.ig.rules", ("n", rules)), "small", color: Faint)));
        col.Children.Add(new Border { Background = Plate, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = modLine });

        // Инструменты — плитками; выбранный раскрывается полями ниже.
        col.Children.Add(Ui.Text(I18n.T("v23.ig.tools"), "h3", color: Ink));
        var grid = new UniformGrid { Columns = 2 };
        foreach (var (id, icon, _, _, _) in InGame.Tools)
        {
            var tool = id;
            var on = _tool == id;
            var b = new Button
            {
                Classes = { "ghost" }, Padding = new Thickness(12, 10), Margin = new Thickness(0, 0, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = on ? new SolidColorBrush(Color.FromArgb(46, 0xF0, 0xB4, 0x29)) : Plate,
                BorderBrush = on ? HubLook.Amber : Rim, BorderThickness = new Thickness(on ? 1.5 : 1), CornerRadius = new CornerRadius(10),
                Content = Ui.Col(3, Ui.Row(8, Ui.Icon(icon, 15, on ? HubLook.Amber : Soft), Ui.Text(I18n.T("v23.ig.tool." + id), "strong", color: Ink)),
                    Ui.Text(I18n.T($"v23.ig.tool.{id}.text"), "small", color: Faint, wrap: true)),
            };
            b.Click += (_, _) => { _tool = _tool == tool ? null : tool; _note = null; rebuild(); };
            grid.Children.Add(b);
        }
        col.Children.Add(grid);
        if (_tool is { } t) col.Children.Add(ToolForm(p, t, rebuild));

        // Поставить в игру и перезапустить.
        var apply = Ui.Button(I18n.T("v23.ig.apply"), () =>
        {
            ProgressGuard.Begin(g, force: true);
            if (p.Kind == "cheats") ProgressGuard.NoteCheats(g.Def.Id);
            InGame.ApplyResult r;
            try { r = InGame.Apply(p, g.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { r = new(false, false, e.Message); }
            _note = r.Message;
            _noteOk = r.Ok;
            rebuild();
        }, "primary", Icons.Zap);
        apply.IsEnabled = rules > 0;
        apply.Background = HubLook.Amber;
        apply.Foreground = HubLook.OnAmber;
        var restart = Ui.Button(I18n.T("v23.ig.restart"), () =>
        {
            Launcher.Stop(g.Def.Id);
            OverlayWindow.Current?.Hide();
            DispatcherTimer.RunOnce(() => Actions.Play(g, animate: false, skipUpdate: true), TimeSpan.FromSeconds(3));
        }, "", Icons.Refresh);
        ToolTip.SetTip(restart, I18n.T("v23.ig.restart.tip"));
        col.Children.Add(Ui.Row(8, apply, restart));
        if (_note is { } note) col.Children.Add(Note(_noteOk ? Icons.Check : Icons.Alert, note + (_noteOk ? "\n" + I18n.T("v23.ig.nextLaunch") : ""), _noteOk ? Ui.Res("Good") : Ui.Res("Bad")));
        if (ProgressGuard.Current(g.Def.Id) is { } s)
            col.Children.Add(Note(Icons.Save, I18n.T("v23.ig.guard", ("time", s.At.ToLocalTime().ToString("t"))), Ui.Res("Good")));
        return col;
    }

    /// <summary>Поля инструмента: переменная игры, число или текст — и «Добавить в мод».</summary>
    static Control ToolForm(XProject p, string tool, Action rebuild)
    {
        var (_, _, needVar, needNumber, needText) = InGame.Tools.First(x => x.Id == tool);
        var row = new WrapPanel();
        // Переменная игры — чипами, а не выпадающим списком: всплывающее окно списка закрыло бы оверлей.
        string? chosen = null;
        WrapPanel? vars = null;
        if (needVar)
        {
            chosen = tool == "floor" ? "health" : "gold";
            vars = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            void Chips()
            {
                vars!.Children.Clear();
                foreach (var v in InGame.Vars)
                {
                    var id = v;
                    var chip = Ui.Button(I18n.T("v23.ig.var." + v), () => { chosen = id; Chips(); }, chosen == v ? "chip active" : "chip");
                    chip.Margin = new Thickness(0, 0, 6, 6);
                    vars.Children.Add(chip);
                }
            }
            Chips();
        }
        NumericUpDown? number = null;
        if (needNumber)
        {
            number = new NumericUpDown { Value = tool == "floor" ? 10 : 500, Minimum = -1_000_000, Maximum = 1_000_000, Increment = tool == "floor" ? 5 : 100, Width = 150, FormatString = "0", Margin = new Thickness(0, 0, 8, 8) };
            row.Children.Add(number);
        }
        TextBox? text = null;
        if (needText)
        {
            text = new TextBox { Watermark = I18n.T("v23.ig.say.hint"), MaxLength = 140, Width = 300, Margin = new Thickness(0, 0, 8, 8) };
            row.Children.Add(text);
        }
        var add = Ui.Button(I18n.T("v23.ig.add"), () =>
        {
            var v = chosen;
            if (needText && string.IsNullOrWhiteSpace(text?.Text)) return;
            try
            {
                InGame.AddRule(p, tool, v, (double)(number?.Value ?? 0), text?.Text);
                _note = I18n.T("v23.ig.added");
                _noteOk = true;
                _tool = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _note = e.Message; _noteOk = false; }
            rebuild();
        }, "primary", Icons.Plus);
        row.Children.Add(add);
        return new Border { Background = Plate, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 6, 4), Child = vars is null ? row : Ui.Col(6, vars, row) };
    }

    static Control Note(string icon, string text, IBrush color)
    {
        var i = Ui.Icon(icon, 16, color);
        i.VerticalAlignment = VerticalAlignment.Top;
        i.Margin = new Thickness(0, 2, 10, 0);
        var dock = new DockPanel();
        DockPanel.SetDock(i, Dock.Left);
        dock.Children.Add(i);
        dock.Children.Add(Ui.Text(text, "small", color: Soft, wrap: true));
        return new Border { Background = Plate, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10), Child = dock };
    }

    static void OpenHub(XProject? p)
    {
        OverlayWindow.Current?.Hide();
        var w = MainWindow.Current;
        if (w is null) return;
        w.WindowState = WindowState.Normal;
        w.Activate();
        w.Navigate(() => p is null ? CreatorPage.NewForm() : new XStudio(p.Id));
    }

    /// <summary>Забыть выбранный инструмент и результат, когда оверлей закрыт.</summary>
    public static void Reset() { _tool = null; _note = null; }
}
