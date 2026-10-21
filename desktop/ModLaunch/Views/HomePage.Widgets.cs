using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

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
        var layout = Widgets.Entries();
        var grid = new WidgetGrid();
        for (var i = 0; i < layout.Count; i++)
        {
            var e = layout[i];
            Control w;
            try { w = Widget(e, mine, i); }
            catch { w = Shell(Ui.Text(I18n.T("v10.w.error"), "small muted"), null); }
            grid.Sizes.Add(e.Size);
            // 12.0: правая кнопка по виджету — изменить, размер, игра, сдвиг, убрать (свои меню внутри виджета важнее).
            if (!_editWidgets && w.ContextFlyout is null) w.ContextFlyout = WidgetMenu(i, e, layout.Count);
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
            Classes = { "store-tile", "widget" }, CornerRadius = new CornerRadius(Look.Radius == "sharp" ? 4 : Look.Radius == "round" ? 28 : Look.Calm ? 14 : 22),
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

    /// <summary>Цвет из настройки виджета; null — как тема.</summary>
    static IBrush? TintOf(string tint) => tint switch
    {
        "blue" => Ui.Hex("#4C8DFF"), "violet" => Ui.Hex("#9B7BFF"), "green" => Ui.Hex("#3FCB7E"),
        "orange" => Ui.Hex("#FF9640"), "pink" => Ui.Hex("#FF6FB1"), "mono" => Ui.Res("Text"), _ => null,
    };

    /// <summary>Фон цветного виджета: цвет сверху слева мягко уходит в обычную поверхность, как у виджетов iPhone.</summary>
    static IBrush? Wash(IBrush? tint)
    {
        if (tint is not ISolidColorBrush c) return null;
        var surface = (Ui.Res("Surface") as ISolidColorBrush)?.Color ?? Colors.Black;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(70, c.Color.R, c.Color.G, c.Color.B), 0), new GradientStop(surface, 0.85) },
        };
    }

    /// <summary>Игра из настройки: своя (id), из Steam (steam:номер) или «авто» — тогда fallback.</summary>
    static (GameState? Own, SteamApp? Steam) PickGame(string value, GameState? fallback)
    {
        if (value.StartsWith("steam:", StringComparison.Ordinal) && int.TryParse(value[6..], out var app) && app > 0)
            return SteamLibrary.Managed(app) is { } managed ? (managed, null) : (null, SteamLibrary.ById(app) ?? new SteamApp(app, "Steam · " + app, "", ""));
        if (value != "" && AppState.Games.FirstOrDefault(g => g.Def.Id == value) is { } own) return (own, null);
        return (fallback, null);
    }

    Control Widget(Widgets.Entry e, List<GameState> mine, int index = -1)
    {
        var id = e.Id;
        var size = e.Size;
        var wide = size != "s";
        var large = size == "l";
        var tint = TintOf(e.Get("tint"));
        var wash = Wash(tint);
        void Go(Func<Page> make) => MainWindow.Current?.Navigate(make);
        switch (id)
        {
            case "clock":
            {
                var now = DateTime.Now;
                var format = e.Get("h12") == "1" ? "h:mm" : "HH:mm";
                var time = Big(now.ToString(format), wide ? 46 : 38, tint);
                var lines = new List<Control> { time };
                if (e.Get("date") != "0") lines.Add(Small(now.ToString("dddd, d MMMM", I18n.Culture), lines: 1));
                var today = mine.Where(g => PlayTime.Get(g.Def.Id).LastPlayed?.Date == DateTime.Today).Select(g => g.Def.ShortName).Take(2).ToList();
                if (wide) lines.Add(Small(today.Count > 0 ? I18n.T("v10.w.clock.today", ("list", string.Join(", ", today))) : Greeting(), Ui.Res("Faint"), 1));
                var clock = Stack(Cap(Icons.Clock, I18n.T("v10.w.clock"), tint), lines.ToArray());
                // Время обновляется раз в 20 секунд, пока виджет на экране. Таймер создаётся остановленным.
                var tick = new Avalonia.Threading.DispatcherTimer(Avalonia.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
                tick.Tick += (_, _) => time.Text = DateTime.Now.ToString(format);
                clock.AttachedToVisualTree += (_, _) => { if (!Program.Screenshot) tick.Start(); };
                clock.DetachedFromVisualTree += (_, _) => tick.Stop();
                return Shell(clock, null, wash);
            }
            case "level":
            {
                var all = Achievements.All();
                var (level, _, next) = Achievements.Level(all);
                var ring = Ring(1 - next / 300.0, 66, 6, tint ?? Ui.Res("Brand"));
                ring.Children.Add(new TextBlock { Text = level.ToString(), FontFamily = Look.Display, FontSize = 22, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Res("Text") });
                var words = Ui.Col(2, Small(I18n.T("v10.w.level.next", ("n", next)), lines: 1));
                if (wide && all.Where(a => !a.Done && a.Reward is not null).OrderByDescending(a => a.Progress).FirstOrDefault() is { } a)
                    words.Children.Add(Small("★ " + a.Title + " · " + (int)(a.Progress * 100) + "%", Ui.Res("Brand2"), 1));
                var body = wide
                    ? (Control)Stack(Cap(Icons.Trophy, I18n.T("v10.w.level"), tint), Ui.Row(14, ring, new Border { VerticalAlignment = VerticalAlignment.Center, Child = Ui.Col(4, Big(I18n.T("v10.w.level.n", ("n", level)), 22), words) }))
                    : Stack(Cap(Icons.Trophy, I18n.T("v10.w.level"), tint), ring, words);
                return Shell(body, () => Go(() => new ProfilePage()), wash);
            }
            case "autopilot":
            {
                var g = PickGame(e.Get("game"), mine.FirstOrDefault(x => !x.Def.IsMinecraft && x.Registry is not null)).Own;
                if (g?.Registry is null || g.Def.IsMinecraft) return Shell(Stack(Cap(Icons.Wand, I18n.T("v10.ap.short")), Small(I18n.T("v10.w.ap.none"))), () => Go(() => new AutopilotPage()));
                var (score, steps) = Autopilot.Plan(g, quick: true);
                var color = ScoreBrush(score);
                var lines = new List<Control> { Ui.Row(6, Big(score.ToString(), wide ? 40 : 36, color), new TextBlock { Text = "/100", FontSize = 13, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) }) };
                lines.Add(Small(I18n.T("v10.ap.grade." + Autopilot.Grade(score)) + " · " + g.Def.ShortName, lines: 1));
                if (wide) lines.Add(Small(steps.Count == 0 ? I18n.T("v10.w.ap.clean") : steps[0].Title, Ui.Res("Faint"), 1));
                return Shell(Stack(Cap(Icons.Wand, I18n.T("v10.ap.short")), lines.ToArray()), () => Go(() => new AutopilotPage(g.Def.Id)));
            }
            case "continue":
            {
                var g = PickGame(e.Get("game"), _stage is { } s && mine.FirstOrDefault(x => x.Def.Id == s) is { } picked ? picked : mine.FirstOrDefault()).Own;
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
                return Shell(Stack(top, col), () => Go(() => v.SignedIn ? new CommunityPage() : new SettingsPage("accounts")));
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
                var only = e.Get("game") is { Length: > 0 } pick ? PickGame(pick, null).Own : null;
                var rows = (only is null ? mine : [only]).Select(g => (g, ms: PlayTime.Get(g.Def.Id).TotalMs)).Where(x => x.ms > 0).OrderByDescending(x => x.ms).ToList();
                var hours = rows.Sum(r => r.ms) / 3_600_000;
                var list = new List<Control> { Ui.Row(6, Big(hours.ToString(), 36, tint), new TextBlock { Text = I18n.T("v10.w.hours"), FontSize = 13, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7) }) };
                if (only is not null) list.Add(Small(only.Def.ShortName, lines: 1));
                else if (wide)
                {
                    var max = Math.Max(1, rows.FirstOrDefault().ms);
                    foreach (var (g, ms) in rows.Take(2))
                        list.Add(Ui.Row(8, new Border { Width = 70, Child = new TextBlock { Text = g.Def.ShortName, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis } },
                            new Border { Height = 6, CornerRadius = new CornerRadius(3), Width = Math.Max(6, 150.0 * ms / max), Background = Ui.Hex(g.Def.Accent), VerticalAlignment = VerticalAlignment.Center }));
                }
                else list.Add(Small(rows.Count > 0 ? I18n.T("v10.w.hours.top", ("game", rows[0].g.Def.ShortName)) : I18n.T("v98.replay.empty"), lines: 1));
                return Shell(Stack(Cap(Icons.Chart, I18n.T("v10.w.playtime"), tint), list.ToArray()), () => Go(() => new ProfilePage("replay")), wash);
            }
            case "builds":
            {
                // 11.0: часы по сборкам — самая обжитая сборка; широкий — две сборки полосками.
                var builds = BuildTime.All();
                var list = new List<Control>();
                if (builds.Count == 0) list.Add(Small(I18n.T("v11.bt.empty.short")));
                else
                {
                    var top = builds[0];
                    list.Add(Ui.Row(6, Big(((long)top.Hours).ToString(), 36, tint), new TextBlock { Text = I18n.T("v10.w.hours"), FontSize = 13, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7) }));
                    if (wide)
                    {
                        var max = Math.Max(1, top.TotalMs);
                        foreach (var b in builds.Take(2))
                            list.Add(Ui.Row(8, new Border { Width = 96, Child = new TextBlock { Text = b.Name, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis } },
                                new Border { Height = 6, CornerRadius = new CornerRadius(3), Width = Math.Max(6, 150.0 * b.TotalMs / max), Background = Ui.Res("Brand"), VerticalAlignment = VerticalAlignment.Center }));
                    }
                    else list.Add(Small(top.Name, lines: 1));
                }
                return Shell(Stack(Cap(Icons.Hourglass, I18n.T("v11.w.builds"), tint), list.ToArray()), () => Go(() => new ProfilePage("builds")), wash);
            }
            case "downloads":
                return new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true, Child = new DownloadsTile() };
            case "library":
            {
                var wrap = new Avalonia.Controls.Primitives.UniformGrid { Columns = large ? 4 : 4, Rows = large ? 2 : 1 };
                var sorted = e.Get("sort") switch
                {
                    "name" => mine.OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                    "hours" => mine.OrderByDescending(g => PlayTime.Get(g.Def.Id).TotalMs).ToList(),
                    _ => mine,
                };
                foreach (var g in sorted.Take(large ? 8 : 4))
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
                var state = bad > 0 ? Ui.Res("Bad") : notices.Count > 0 ? Ui.Res("Warn") : Ui.Res("Good");
                var first = notices.FirstOrDefault();
                var lines = new List<Control> { Big(notices.Count == 0 ? "✓" : notices.Count.ToString(), 36, state), Small(first?.Title ?? I18n.T("v10.w.calm"), lines: wide ? 2 : 1) };
                return Shell(Stack(Cap(Icons.Bell, I18n.T("v10.w.attention"), state), lines.ToArray()), first is null ? null : () => (first.Open ?? first.Run)?.Invoke());
            }
            case "streak":
            {
                var n = PlayStreak.Current();
                var flame = tint ?? Ui.Hex("#FF8A1F");
                return Shell(Stack(Cap(Icons.Flame, I18n.T("v10.w.streak"), flame), Big(n.ToString(), 40, flame), Small(I18n.T("v10.w.streak.days." + I18n.Plural(n, "one", "few", "many"), ("n", n)), lines: 1)), null, wash);
            }
            case "random":
            {
                var from = e.Get("from");
                var steamPool = from is "steam" or "all" ? SteamLibrary.Installed.Where(a => SteamLibrary.Managed(a.AppId) is null).ToList() : [];
                var pool = from == "steam" ? [] : mine.Where(g => !g.Def.IsMinecraft).ToList();
                var total = pool.Count + steamPool.Count;
                if (total == 0) return Shell(Stack(Cap(Icons.Shuffle, I18n.T("v10.w.random")), Small(I18n.T("home.noGames"))), null);
                var at = (DateTime.Today.DayOfYear + DateTime.Now.Hour) % total;
                if (at >= pool.Count) return SteamTile(steamPool[at - pool.Count], size, Cap(Icons.Shuffle, I18n.T("v10.w.random"), Brushes.White));
                var g = pool[at];
                var words = Ui.Col(4, Cap(Icons.Shuffle, I18n.T("v10.w.random"), Brushes.White), new TextBlock { Text = g.Def.Name, FontWeight = FontWeight.Bold, FontSize = wide ? 18 : 14, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
                words.VerticalAlignment = VerticalAlignment.Bottom;
                words.Margin = new Thickness(14);
                return Shell(new Panel { Children = { Ui.GameImage(g.Def, 600, art: wide ? Images.Art.Hero : Images.Art.Cover), new Border { Background = Gx.ShadeUp(0.3, 230) }, words } }, () => Go(() => new GamePage(g.Def.Id)), pad: 0);
            }
            case "game":
            {
                var (own, steam) = PickGame(e.Get("game"), null);
                if (own is not null) return GameTile(own, size);
                if (steam is not null) return SteamTile(steam, size, null);
                var pick = Ui.Button(I18n.T("v12w.pick"), () => OpenStudio(edit: index), "primary", Icons.Sliders);
                pick.HorizontalAlignment = HorizontalAlignment.Left;
                return Shell(Stack(Cap(Icons.Gamepad, I18n.T("v10.w.name.game")), Small(I18n.T("v12w.game.empty")), pick), null);
            }
            case "note":
            {
                var text = e.Get("text");
                var body = text == ""
                    ? Small(I18n.T("v12w.note.empty"), Ui.Res("Faint"), 4)
                    : new TextBlock { Text = text, FontSize = large ? 20 : wide ? 17 : 15, FontWeight = FontWeight.SemiBold, Foreground = Ui.Res("Text"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = large ? 9 : wide ? 3 : 4, LineHeight = large ? 27 : 22 };
                var note = new DockPanel();
                var cap = Cap(Icons.Edit, I18n.T("v10.w.name.note"), tint ?? Ui.Hex("#F5C542"));
                DockPanel.SetDock(cap, Dock.Top);
                cap.Margin = new Thickness(0, 0, 0, 8);
                note.Children.Add(cap);
                note.Children.Add(body);
                return Shell(note, () => OpenStudio(edit: index), wash ?? Wash(Ui.Hex("#F5C542")));
            }
            case "steam":
            {
                var apps = SteamLibrary.Sorted(e.Get("sort")).ToList();
                var top = new DockPanel();
                var count = new TextBlock { Text = apps.Count.ToString(), FontFamily = Look.Display, FontWeight = FontWeight.Bold, FontSize = 15, Foreground = Ui.Res("Brand2") };
                DockPanel.SetDock(count, Dock.Right);
                top.Children.Add(count);
                top.Children.Add(Cap(Icons.Layers, I18n.T("v10.w.name.steam")));
                if (apps.Count == 0) return Shell(Stack(top, Small(I18n.T("v12w.steam.none"))), () => Go(() => new SteamGamesPage()));
                var wrap = new Avalonia.Controls.Primitives.UniformGrid { Columns = large ? 4 : 5, Rows = large ? 2 : 1 };
                foreach (var a in apps.Take(large ? 8 : 5))
                {
                    var app = a;
                    var cover = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(3), Child = WebArt(SteamLibrary.Cover(a.AppId), GameDef.SteamArt(a.AppId), a.Name, 300), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                    ToolTip.SetTip(cover, a.Name);
                    if (!_editWidgets) StoreKit.OnClick(cover, () => PlaySteam(app));
                    wrap.Children.Add(cover);
                }
                return Shell(Stack(top, wrap), () => Go(() => new SteamGamesPage()), pad: 14);
            }
        }
        return Shell(Small(id), null);
    }

    /// <summary>Игра Steam: своя в ModLaunch — страница игры (с модами), иначе — запуск через Steam.</summary>
    static void PlaySteam(SteamApp a)
    {
        if (SteamLibrary.Managed(a.AppId) is { } g) MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id));
        else SteamLibrary.Play(a.AppId);
    }

    /// <summary>Картинка по ссылке на весь виджет; не загрузилась — запасная, потом мягкий цветной фон с буквой.</summary>
    internal static Control WebArt(string url, string? fallback, string name, int decode)
    {
        var hue = (int)(name.Aggregate(17u, (h, c) => h * 31 + c) % 360);
        var back = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Ui.HslColor(hue, 0.45, 0.32), 0), new GradientStop(Ui.HslColor((hue + 40) % 360, 0.5, 0.18), 1) },
            },
            Child = new TextBlock { Text = name.Length > 0 ? name[..1].ToUpper(I18n.Culture) : "?", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var image = new Image { Stretch = Stretch.UniformToFill };
        _ = Images.FromUrl(url, decode).ContinueWith(async t =>
        {
            var bmp = t.Result ?? (fallback is null ? null : await Images.FromUrl(fallback, decode));
            if (bmp is not null) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bmp);
        });
        return new Panel { Children = { back, image } };
    }

    /// <summary>Виджет «Игра» для игры ModLaunch: обложка или герой, имя и «Играть».</summary>
    Control GameTile(GameState g, string size)
    {
        var large = size == "l";
        var play = Launcher.IsRunning(g.Def.Id) ? PlayControls.RunningPill(g.Def.Id, big: false) : Ui.Button(I18n.T("games.play"), () => Actions.Play(g), "primary", Icons.Play);
        play.HorizontalAlignment = HorizontalAlignment.Left;
        var hours = PlayTime.Get(g.Def.Id).TotalMs / 3_600_000;
        var words = size == "s"
            ? Ui.Col(6, new TextBlock { Text = g.Def.ShortName, FontWeight = FontWeight.Bold, FontSize = 14, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis }, play)
            : Ui.Col(8, Gx.Eyebrow(hours > 0 ? I18n.T("v12w.game.hours", ("n", hours)) : I18n.T("v93.home.notPlayed"), Ui.Hex("#E6E7EE"), 10), Gx.Title(g.Def.Name, large ? 26 : 20, Brushes.White, 2), play);
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(size == "s" ? 12 : 18, 12, 12, size == "s" ? 12 : 16);
        var art = size == "s" ? Images.Art.Cover : Images.Art.Hero;
        var tile = Shell(new Panel { Children = { Ui.GameImage(g.Def, size == "s" ? 400 : 1024, art: art), new Border { Background = Gx.ShadeUp(0.25, 230) }, words } }, () => MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id)), pad: 0);
        tile.ContextFlyout = GameCard.Menu(g);
        return tile;
    }

    /// <summary>Виджет игры Steam без модов: картинка Steam, имя, «Играть» через Steam (не установлена — «Установить»).</summary>
    static Control SteamTile(SteamApp a, string size, Control? cap)
    {
        var installed = SteamLibrary.ById(a.AppId) is not null;
        var play = installed
            ? Ui.Button(I18n.T("games.play"), () => SteamLibrary.Play(a.AppId), "primary", Icons.Play)
            : Ui.Button(I18n.T("v12w.steam.install"), () => SteamLibrary.Install(a.AppId), "", Icons.Download);
        play.HorizontalAlignment = HorizontalAlignment.Left;
        var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 10, 12, 20)), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = "STEAM", FontSize = 9.5, FontWeight = FontWeight.Bold, LetterSpacing = 1, Foreground = Ui.Hex("#C7D5E0") } };
        var words = Ui.Col(size == "s" ? 6 : 8);
        if (cap is not null) words.Children.Add(cap);
        else words.Children.Add(badge);
        words.Children.Add(new TextBlock { Text = a.Name, FontWeight = FontWeight.Bold, FontSize = size == "s" ? 14 : size == "l" ? 24 : 19, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxLines = 2 });
        if (cap is null) words.Children.Add(play);
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(size == "s" ? 12 : 18, 12, 12, size == "s" ? 12 : 16);
        var art = size == "s" ? WebArt(SteamLibrary.Cover(a.AppId), GameDef.SteamArt(a.AppId), a.Name, 400) : WebArt(SteamLibrary.Hero(a.AppId), GameDef.SteamArt(a.AppId), a.Name, 1024);
        return Shell(new Panel { Children = { art, new Border { Background = Gx.ShadeUp(0.25, 230) }, words } }, () => PlaySteam(a), pad: 0);
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

    /// <summary>«Добавить виджет»: все виды.</summary>
    Control Gallery(List<Widgets.Entry> layout)
    {
        var row = new WrapPanel();
        foreach (var k in Widgets.Catalog)
        {
            var icon = typeof(Icons).GetField(k.Icon)?.GetValue(null) as string ?? Icons.Grid;
            var kind = k.Id;
            var b = Ui.Button(I18n.T("v10.w.name." + k.Id), () => OpenStudio(kind: kind), "", icon, I18n.T("v10.w.add.one"));
            b.Margin = new Thickness(0, 0, 8, 8);
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
