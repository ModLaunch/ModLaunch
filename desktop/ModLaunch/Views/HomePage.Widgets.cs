using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: сетка виджетов главной — квадратные клетки, виджеты 1×1, 2×1 и 2×2 встают плотно,
/// как на iPhone. Число столбцов зависит от ширины окна (клетка не уже MinCell).
/// </summary>
public sealed class WidgetGrid : Panel
{
    public List<string> Sizes { get; } = [];
    public double Gap { get; set; } = 14;
    public double MinCell { get; set; } = 172;

    (int Cols, double Cell, double CellH) Metrics(double width)
    {
        if (double.IsInfinity(width) || width <= 0) width = 1200;
        var cols = Math.Clamp((int)Math.Floor((width + Gap) / (MinCell + Gap)), 2, 8);
        // Чётное число столбцов: широкие виджеты не оставляют дыр по краю.
        if (cols % 2 == 1 && cols > 2) cols--;
        var cell = (width - Gap * (cols - 1)) / cols;
        return (cols, cell, Math.Round(cell * 0.78));
    }

    Rect Slot(int i, List<(int X, int Y)> at, (int Cols, double Cell, double CellH) m)
    {
        var (w, h) = Widgets.Cells(i < Sizes.Count ? Sizes[i] : "s");
        w = Math.Min(w, m.Cols);
        var (x, y) = at[i];
        return new Rect(x * (m.Cell + Gap), y * (m.CellH + Gap), w * m.Cell + (w - 1) * Gap, h * m.CellH + (h - 1) * Gap);
    }

    protected override Size MeasureOverride(Size available)
    {
        var m = Metrics(available.Width);
        var at = Widgets.Pack(Sizes.Take(Children.Count).Concat(Enumerable.Repeat("s", Math.Max(0, Children.Count - Sizes.Count))).ToList(), m.Cols);
        double bottom = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var r = Slot(i, at, m);
            Children[i].Measure(r.Size);
            bottom = Math.Max(bottom, r.Bottom);
        }
        return new Size(double.IsInfinity(available.Width) ? m.Cols * (m.Cell + Gap) : available.Width, bottom);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var m = Metrics(final.Width);
        var at = Widgets.Pack(Sizes.Take(Children.Count).Concat(Enumerable.Repeat("s", Math.Max(0, Children.Count - Sizes.Count))).ToList(), m.Cols);
        for (var i = 0; i < Children.Count; i++) Children[i].Arrange(Slot(i, at, m));
        return final;
    }
}

public sealed partial class HomePage
{
    static bool _editWidgets;

    /// <summary>Открыть главную сразу в режиме правки виджетов (из настроек).</summary>
    public static void EditWidgets() => _editWidgets = true;

    /// <summary>Сетка виджетов; в режиме правки у каждого — «убрать», «размер» и стрелки.</summary>
    Control WidgetsArea(List<GameState> mine)
    {
        var layout = Widgets.Layout();
        var grid = new WidgetGrid();
        for (var i = 0; i < layout.Count; i++)
        {
            var (id, size) = layout[i];
            Control w;
            try { w = Widget(id, size, mine); }
            catch { w = Shell(Ui.Text(I18n.T("v10.w.error"), "small muted"), null); }
            grid.Sizes.Add(size);
            grid.Children.Add(_editWidgets ? EditChrome(w, i, layout.Count) : w);
        }
        var col = Ui.Col(14);
        if (_editWidgets) col.Children.Add(Gallery(layout));
        if (layout.Count == 0 && !_editWidgets)
            col.Children.Add(Ui.Card(Empty(Icons.Grid, Ui.Res("Brand2"), I18n.T("v10.w.none"), I18n.T("v10.w.none.text"),
                Ui.Button(I18n.T("v10.w.add"), () => { _editWidgets = true; Build(); }, "primary", Icons.Plus)), 20));
        col.Children.Add(grid);
        return col;
    }

    /// <summary>Рамка виджета: скругление как у iOS, мягкая поверхность, нажатие — переход.</summary>
    static Border Shell(Control body, Action? open, IBrush? background = null, double pad = 16)
    {
        var b = new Border
        {
            Classes = { "store-tile", "widget" }, CornerRadius = new CornerRadius(Look.Radius == "sharp" ? 4 : Look.Radius == "round" ? 28 : 22),
            Padding = new Thickness(pad), Background = background ?? Ui.Res("Surface"), ClipToBounds = true, Child = body,
        };
        if (open is not null && !_editWidgets) StoreKit.OnClick(b, open);
        else b.Cursor = Avalonia.Input.Cursor.Default;
        return b;
    }

    /// <summary>Подпись виджета: значок цвета акцента и маленькое слово капсом, как в «Погоде» на iPhone.</summary>
    static Control Cap(string icon, string text, IBrush? tint = null) =>
        Ui.Row(6, Ui.Icon(icon, 13, tint ?? Ui.Res("Brand2")), new TextBlock { Text = text.ToUpper(I18n.Culture), FontSize = 10.5, FontWeight = FontWeight.Bold, LetterSpacing = 0.9, Foreground = tint ?? Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center });

    static TextBlock Big(string text, double size = 34, IBrush? color = null) => new()
    {
        Text = text, FontFamily = Look.Display, FontSize = size, FontWeight = FontWeight.Bold, LetterSpacing = -0.8, Foreground = color ?? Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis,
    };

    static TextBlock Small(string text, IBrush? color = null, int lines = 2) => new()
    {
        Text = text, FontSize = 12.5, Foreground = color ?? Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxLines = lines, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Столбик снизу вверх: подпись сверху, остальное прижато к низу — так виджеты одной высоты выглядят ровно.</summary>
    static Control Stack(Control top, params Control[] bottom)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        var low = Ui.Col(4, bottom);
        low.VerticalAlignment = VerticalAlignment.Bottom;
        dock.Children.Add(low);
        return dock;
    }

    Control Widget(string id, string size, List<GameState> mine)
    {
        var wide = size != "s";
        var large = size == "l";
        void Go(Func<Page> make) => MainWindow.Current?.Navigate(make);
        switch (id)
        {
            case "clock":
            {
                var now = DateTime.Now;
                var time = Big(now.ToString("HH:mm"), wide ? 46 : 38);
                var date = Small(now.ToString("dddd, d MMMM", I18n.Culture), lines: 1);
                var today = mine.Where(g => PlayTime.Get(g.Def.Id).LastPlayed?.Date == DateTime.Today).Select(g => g.Def.ShortName).Take(2).ToList();
                var extra = wide ? Small(today.Count > 0 ? I18n.T("v10.w.clock.today", ("list", string.Join(", ", today))) : Greeting(), Ui.Res("Faint"), 1) : null;
                var clock = Stack(Cap(Icons.Clock, I18n.T("v10.w.clock")), extra is null ? [time, date] : [time, date, extra]);
                // Время обновляется раз в 20 секунд, пока виджет на экране.
                var tick = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(20), Avalonia.Threading.DispatcherPriority.Background, (_, _) => time.Text = DateTime.Now.ToString("HH:mm"));
                clock.AttachedToVisualTree += (_, _) => { if (!Program.Screenshot) tick.Start(); };
                clock.DetachedFromVisualTree += (_, _) => tick.Stop();
                return Shell(clock, null);
            }
            case "level":
            {
                var all = Achievements.All();
                var (level, _, next) = Achievements.Level(all);
                var ring = Ring(1 - next / 300.0, 66, 6, Ui.Res("Brand"));
                ring.Children.Add(new TextBlock { Text = level.ToString(), FontFamily = Look.Display, FontSize = 22, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Res("Text") });
                var words = Ui.Col(2, Small(I18n.T("v10.w.level.next", ("n", next)), lines: 1));
                if (wide && all.Where(a => !a.Done && a.Reward is not null).OrderByDescending(a => a.Progress).FirstOrDefault() is { } a)
                    words.Children.Add(Small("★ " + a.Title + " · " + (int)(a.Progress * 100) + "%", Ui.Res("Brand2"), 1));
                var body = wide
                    ? (Control)Stack(Cap(Icons.Trophy, I18n.T("v10.w.level")), Ui.Row(14, ring, new Border { VerticalAlignment = VerticalAlignment.Center, Child = Ui.Col(4, Big(I18n.T("v10.w.level.n", ("n", level)), 22), words) }))
                    : Stack(Cap(Icons.Trophy, I18n.T("v10.w.level")), ring, words);
                return Shell(body, () => Go(() => new ProfilePage()));
            }
            case "autopilot":
            {
                var g = mine.FirstOrDefault(x => !x.Def.IsMinecraft && x.Registry is not null);
                if (g is null) return Shell(Stack(Cap(Icons.Wand, I18n.T("v10.ap.short")), Small(I18n.T("v10.w.ap.none"))), () => Go(() => new AutopilotPage()));
                var (score, steps) = Autopilot.Plan(g, quick: true);
                var color = ScoreBrush(score);
                var lines = new List<Control> { Ui.Row(6, Big(score.ToString(), wide ? 40 : 36, color), new TextBlock { Text = "/100", FontSize = 13, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) }) };
                lines.Add(Small(I18n.T("v10.ap.grade." + Autopilot.Grade(score)) + " · " + g.Def.ShortName, lines: 1));
                if (wide) lines.Add(Small(steps.Count == 0 ? I18n.T("v10.w.ap.clean") : steps[0].Title, Ui.Res("Faint"), 1));
                return Shell(Stack(Cap(Icons.Wand, I18n.T("v10.ap.short")), lines.ToArray()), () => Go(() => new AutopilotPage(g.Def.Id)));
            }
            case "cases":
            {
                var ready = Cases.Ready();
                var tier = ready.FirstOrDefault()?.Tier ?? "common";
                var box = CaseKit.Small(wide ? 96 : 74, tier);
                box.Opacity = ready.Count > 0 ? 1 : 0.45;
                var text = ready.Count > 0 ? I18n.T("v10.w.cases.ready." + I18n.Plural(ready.Count, "one", "few", "many"), ("n", ready.Count)) : I18n.T("v10.w.cases.none");
                Control body = wide
                    ? Stack(Cap(Icons.Cube, I18n.T("v10.case.title")), Ui.Row(16, box, new Border { VerticalAlignment = VerticalAlignment.Center, Child = Ui.Col(4, Big(ready.Count.ToString(), 30), Small(text, lines: 2)) }))
                    : Stack(Cap(Icons.Cube, I18n.T("v10.case.title")), new Border { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 6), Child = box }, Small(text, lines: 1));
                return Shell(body, () => Go(() => new CasesPage()));
            }
            case "continue":
            {
                var g = _stage is { } s && mine.FirstOrDefault(x => x.Def.Id == s) is { } picked ? picked : mine.FirstOrDefault();
                if (g is null) return Shell(Stack(Cap(Icons.Play, I18n.T("v4.continue")), Small(I18n.T("h95.continue.empty")), Ui.Button(I18n.T("add.title"), () => Go(() => new AddGamePage()), "primary", Icons.Plus)), null);
                var played = PlayTime.Get(g.Def.Id);
                var logo = Images.GameAsset(g.Def, Images.Art.Logo, 512);
                Control title = logo is null ? Gx.Title(g.Def.Name, large ? 26 : 20, Brushes.White, 2) : new Image { Source = logo, MaxHeight = large ? 80 : 52, MaxWidth = large ? 300 : 210, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                var when = Launcher.IsRunning(g.Def.Id) ? I18n.T("time.running") : played.LastPlayed is not null ? Ui.Ago(played.LastPlayed) : I18n.T("v93.home.notPlayed");
                Control play = Launcher.IsRunning(g.Def.Id) ? PlayControls.RunningPill(g.Def.Id, big: false)
                    : Ui.Button(I18n.T("games.play"), () => Actions.Play(g), "primary", Icons.Play);
                play.HorizontalAlignment = HorizontalAlignment.Left;
                var words = Ui.Col(8, Gx.Eyebrow(I18n.T("v4.continue") + " · " + when, Ui.Hex("#E6E7EE"), 10), title, play);
                words.VerticalAlignment = VerticalAlignment.Bottom;
                words.Margin = new Thickness(18, 14, 18, 16);
                var layers = new Panel { Children = { Ui.GameImage(g.Def, 1024, art: Images.Art.Hero), new Border { Background = Gx.ShadeLeft(0.9, 225) }, new Border { Background = Gx.ShadeUp(0.35, 210) }, words } };
                var tile = Shell(layers, () => Go(() => new GamePage(g.Def.Id)), pad: 0);
                tile.ContextFlyout = GameCard.Menu(g);
                return tile;
            }
            case "friends":
            {
                var v = Social.Friends.View();
                var col = Ui.Col(6);
                if (!v.SignedIn) col.Children.Add(Small(I18n.T("h95.friends.signinText"), lines: 3));
                else
                {
                    var online = v.Friends.Where(f => f.State != "offline").OrderBy(f => f.State == "playing" ? 0 : 1).ToList();
                    if (online.Count == 0) col.Children.Add(Small(v.Friends.Count == 0 ? I18n.T("h95.friends.emptyText") : I18n.T("h95.friends.noneText"), lines: 3));
                    foreach (var f in online.Take(large ? 6 : 2))
                        col.Children.Add(Ui.Row(10, Ui.Thumb(null, f.Name, 28, 14, person: true),
                            Ui.Col(0, new TextBlock { Text = f.Name, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                                new TextBlock { Text = f.State == "playing" && f.GameName != "" ? f.GameName : I18n.T("aside.online"), FontSize = 11.5, Foreground = f.State == "playing" ? Ui.Res("Good") : Ui.Res("Muted") })));
                }
                var top = new DockPanel();
                var count = new TextBlock { Text = v.SignedIn ? v.Friends.Count(f => f.State != "offline").ToString() : "", FontFamily = Look.Display, FontWeight = FontWeight.Bold, FontSize = 15, Foreground = Ui.Res("Good") };
                DockPanel.SetDock(count, Dock.Right);
                top.Children.Add(count);
                top.Children.Add(Cap(Icons.Users, I18n.T("h95.friends")));
                return Shell(Stack(top, col), () => Go(() => v.SignedIn ? new FriendsPage() : new SettingsPage("accounts")));
            }
            case "tip":
            {
                if (_tip < 0) _tip = DateTime.Today.DayOfYear % Tips.Length;
                var text = Small(I18n.T(Tips[_tip]), Ui.Res("Text"), 3);
                text.FontSize = 14;
                var more = Ui.Button("", () => { _tip = (_tip + 1) % Tips.Length; text.Text = I18n.T(Tips[_tip]); }, "icon ghost", Icons.Shuffle, I18n.T("v93.home.tipMore"));
                var top = new DockPanel();
                DockPanel.SetDock(more, Dock.Right);
                more.Margin = new Thickness(0, -6, -6, 0);
                top.Children.Add(more);
                top.Children.Add(Cap(Icons.Sparkles, I18n.T("v93.home.tip"), Yellow));
                return Shell(Stack(top, text), null);
            }
            case "playtime":
            {
                var rows = mine.Select(g => (g, ms: PlayTime.Get(g.Def.Id).TotalMs)).Where(x => x.ms > 0).OrderByDescending(x => x.ms).ToList();
                var hours = rows.Sum(r => r.ms) / 3_600_000;
                var list = new List<Control> { Ui.Row(6, Big(hours.ToString(), 36), new TextBlock { Text = I18n.T("v10.w.hours"), FontSize = 13, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7) }) };
                if (wide)
                {
                    var max = Math.Max(1, rows.FirstOrDefault().ms);
                    foreach (var (g, ms) in rows.Take(2))
                        list.Add(Ui.Row(8, new Border { Width = 70, Child = new TextBlock { Text = g.Def.ShortName, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis } },
                            new Border { Height = 6, CornerRadius = new CornerRadius(3), Width = Math.Max(6, 150.0 * ms / max), Background = Ui.Hex(g.Def.Accent), VerticalAlignment = VerticalAlignment.Center }));
                }
                else list.Add(Small(rows.Count > 0 ? I18n.T("v10.w.hours.top", ("game", rows[0].g.Def.ShortName)) : I18n.T("v98.replay.empty"), lines: 1));
                return Shell(Stack(Cap(Icons.Chart, I18n.T("v10.w.playtime")), list.ToArray()), () => Go(() => new ProfilePage("replay")));
            }
            case "downloads":
                return new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true, Child = new DownloadsTile() };
            case "library":
            {
                var wrap = new Avalonia.Controls.Primitives.UniformGrid { Columns = large ? 4 : 4, Rows = large ? 2 : 1 };
                foreach (var g in mine.Take(large ? 8 : 4))
                {
                    var gs = g;
                    var cover = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(3), Child = Ui.GameImage(g.Def, 240, art: Images.Art.Cover), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                    ToolTip.SetTip(cover, g.Def.Name);
                    if (!_editWidgets) StoreKit.OnClick(cover, () => Go(() => new GamePage(gs.Def.Id)));
                    wrap.Children.Add(cover);
                }
                return Shell(Stack(Cap(Icons.Grid, I18n.T("v10.w.library")), wrap), () => Go(() => new LibraryPage()), pad: 14);
            }
            case "attention":
            {
                var notices = Notices(mine).Where(n => n.Kind is "failed" or "health" or "app" or "mods" or "mc" or "pack").ToList();
                var bad = notices.Count(n => n.Kind is "failed" or "health");
                var tint = bad > 0 ? Ui.Res("Bad") : notices.Count > 0 ? Ui.Res("Warn") : Ui.Res("Good");
                var first = notices.FirstOrDefault();
                var lines = new List<Control> { Big(notices.Count == 0 ? "✓" : notices.Count.ToString(), 36, tint), Small(first?.Title ?? I18n.T("v10.w.calm"), lines: wide ? 2 : 1) };
                return Shell(Stack(Cap(Icons.Bell, I18n.T("v10.w.attention"), tint), lines.ToArray()), first is null ? null : () => (first.Open ?? first.Run)?.Invoke());
            }
            case "streak":
            {
                var n = Cases.Streak();
                return Shell(Stack(Cap(Icons.Flame, I18n.T("v10.w.streak"), Ui.Hex("#FF8A1F")), Big(n.ToString(), 40, Ui.Hex("#FF8A1F")), Small(I18n.T("v10.w.streak.days." + I18n.Plural(n, "one", "few", "many"), ("n", n)), lines: 1)), null);
            }
            case "random":
            {
                var pool = mine.Where(g => !g.Def.IsMinecraft).ToList();
                if (pool.Count == 0) return Shell(Stack(Cap(Icons.Shuffle, I18n.T("v10.w.random")), Small(I18n.T("home.noGames"))), null);
                var g = pool[(DateTime.Today.DayOfYear + DateTime.Now.Hour) % pool.Count];
                var words = Ui.Col(4, Cap(Icons.Shuffle, I18n.T("v10.w.random"), Brushes.White), new TextBlock { Text = g.Def.Name, FontWeight = FontWeight.Bold, FontSize = wide ? 18 : 14, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
                words.VerticalAlignment = VerticalAlignment.Bottom;
                words.Margin = new Thickness(14);
                return Shell(new Panel { Children = { Ui.GameImage(g.Def, 600, art: wide ? Images.Art.Hero : Images.Art.Cover), new Border { Background = Gx.ShadeUp(0.3, 230) }, words } }, () => Go(() => new GamePage(g.Def.Id)), pad: 0);
            }
        }
        return Shell(Small(id), null);
    }

    /// <summary>Кольцо прогресса (0…1) с местом под текст в середине.</summary>
    static Panel Ring(double value, double size, double stroke, IBrush color)
    {
        var track = new Ellipse { Width = size, Height = size, Stroke = Ui.Res("Surface3"), StrokeThickness = stroke };
        var arc = new Arc { Width = size, Height = size, StartAngle = -90, SweepAngle = 360 * Math.Clamp(value, 0.02, 1), Stroke = color, StrokeThickness = stroke, StrokeLineCap = PenLineCap.Round };
        return new Panel { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, Children = { track, arc } };
    }

    public static IBrush ScoreBrush(int score) => score >= 90 ? Ui.Res("Good") : score >= 70 ? Ui.Res("Brand2") : score >= 40 ? Ui.Res("Warn") : Ui.Res("Bad");

    // ---------------------------------------------------------------- правка

    Control EditChrome(Control w, int index, int count)
    {
        Button Mini(string icon, Action run, string tip)
        {
            var b = Ui.Button("", run, "icon", icon, tip);
            b.Width = b.Height = 28;
            b.Padding = new Thickness(0);
            b.CornerRadius = new CornerRadius(14);
            b.Background = Ui.Res("Surface3");
            return b;
        }
        var remove = Mini(Icons.Minimize, () => Widgets.Remove(index), I18n.T("v10.w.remove"));
        remove.Background = Ui.Res("Bad");
        remove.Foreground = Brushes.White;
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        remove.VerticalAlignment = VerticalAlignment.Top;
        remove.Margin = new Thickness(-6, -6, 0, 0);
        var tools = Ui.Row(4);
        if (index > 0) tools.Children.Add(Mini(Icons.ChevronLeft, () => Widgets.Move(index, -1), I18n.T("v10.w.left")));
        if (index < count - 1) tools.Children.Add(Mini(Icons.ChevronRight, () => Widgets.Move(index, 1), I18n.T("v10.w.right")));
        if (Widgets.Find(Widgets.Layout()[index].Id) is { Sizes.Length: > 1 }) tools.Children.Add(Mini(Icons.Maximize, () => Widgets.Resize(index), I18n.T("v10.w.size")));
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        tools.VerticalAlignment = VerticalAlignment.Bottom;
        tools.Margin = new Thickness(0, 0, 8, 8);
        var outline = new Border { BorderBrush = Ui.Res("Brand"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(24), IsHitTestVisible = false, Margin = new Thickness(-3) };
        w.IsHitTestVisible = false;
        w.Opacity = 0.92;
        return new Panel { Children = { w, outline, tools, remove } };
    }

    /// <summary>«Добавить виджет»: все виды; закрытые из кейса — с замком.</summary>
    Control Gallery(List<(string Id, string Size)> layout)
    {
        var row = new WrapPanel();
        foreach (var k in Widgets.Catalog)
        {
            var locked = Widgets.IsLocked(k);
            var icon = typeof(Icons).GetField(k.Icon)?.GetValue(null) as string ?? Icons.Grid;
            var b = Ui.Button(I18n.T("v10.w.name." + k.Id), () => { if (!locked) Widgets.Add(k.Id); }, locked ? "ghost" : "", locked ? Icons.Lock : icon,
                locked ? I18n.T("v10.case.inCase") : I18n.T("v10.w.add.one"));
            b.Margin = new Thickness(0, 0, 8, 8);
            if (locked) b.Opacity = 0.5;
            row.Children.Add(b);
        }
        var done = Ui.Button(I18n.T("v10.w.done"), () => { _editWidgets = false; Build(); }, "primary", Icons.Check);
        var reset = Ui.Button(I18n.T("v10.w.reset"), Widgets.Reset, "ghost", Icons.Undo);
        var head = new DockPanel();
        var right = Ui.Row(8, reset, done);
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(Ui.Col(2, Ui.Text(I18n.T("v10.w.add"), "h3"), Ui.Text(I18n.T("v10.w.add.text"), "small muted", wrap: true)));
        return Ui.Card(Ui.Col(12, head, row), 18);
    }
}
