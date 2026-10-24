using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// 9.8: «ИИ-сборки» — первый раздел вместо ленты. Слева — конструктор (игра, запрос словами,
/// стиль, размер), справа — идеи и «Мод недели». Готовая сборка: моды с причиной выбора,
/// галочки, «Установить», «Сохранить», «В файл». Вкладки «Мои сборки» и «Профиль»
/// (уровень, достижения и итоги, как в Steam).
/// </summary>
public sealed class AiBuildsPage : Page
{
    static string _tab = "create";
    static string? _game;
    static string _prompt = "";
    static string _style = "vanilla";
    static int _size = 10;
    static bool _noCheats = true;
    static AiBuild? _result;
    static readonly HashSet<string> _off = [];
    static (string Game, ModInfo Mod)? _weekly;
    static int _weeklyFor = -1;
    /// <summary>16.0: мод сборки, открытый справа в «Просмотре» (нажатие на строку мода).</summary>
    static string? _peek;
    static int _peekImage;
    static readonly Dictionary<string, ModDetails> _details = [];
    static readonly Dictionary<string, List<ModInfo>> _alts = [];
    static string _refine = "";

    bool _busy;
    string? _error;
    CancellationTokenSource? _cts;

    public AiBuildsPage(string tab = "create") { _tab = tab; _ = LoadWeekly(); }

    /// <summary>20.0: открыть «Собрать» сразу с игрой и стилем (из подсказки на главной).</summary>
    public static AiBuildsPage Start(string? game, string? style = null)
    {
        _game = game ?? _game;
        if (style is not null && AiBuilds.Styles.Any(s => s.Id == style)) _style = style;
        _result = null;
        _peek = null;
        _off.Clear();
        return new AiBuildsPage("create");
    }

    /// <summary>20.0: подходит ли игра для ИИ-сборки (свой внешний каталог, не Minecraft).</summary>
    public static bool Supports(GameState g) => g.Status == Detect.Found && !g.Def.IsMinecraft && g.Def.HasExternalCatalog;

    /// <summary>20.0: «Мод недели» для плитки на главной — тот же, что в «ИИ-сборках».</summary>
    public static (GameState Game, ModInfo Mod, int DaysLeft)? WeeklyPick
    {
        get
        {
            if (_weekly is not { } wk || _weeklyFor != Week) return null;
            var g = AppState.Games.FirstOrDefault(x => x.Def.Id == wk.Game);
            if (g is null) return null;
            var days = ((int)DayOfWeek.Monday - (int)DateTime.UtcNow.DayOfWeek + 7) % 7;
            return (g, wk.Mod, days == 0 ? 7 : days);
        }
    }

    /// <summary>20.0: подгрузить «Мод недели» без открытия страницы (главная перерисуется, когда придёт).</summary>
    public static async Task EnsureWeekly(Action done)
    {
        if (_weeklyFor == Week) return;
        var games = Games();
        if (games.Count == 0) return;
        var g = games[Week % games.Count];
        try
        {
            var mods = Program.Demo ? Demo.Catalog(g.Def, new Query()).Mods : (await Catalog.Browse(g.Def, new Query(Sort: SortBy.Rating))).Mods;
            mods = mods.Where(m => !m.Adult && !m.Deprecated).Take(30).ToList();
            if (mods.Count == 0) return;
            _weekly = (g.Def.Id, mods[Week % mods.Count]);
            _weeklyFor = Week;
            done();
        }
        catch { }
    }

    public override string Title => I18n.T("v98.nav.ai");

    /// <summary>Для снимков: показать готовую сборку (и открыть в просмотре мод номер peek).</summary>
    public static void DemoResult(AiBuild b, int peek = -1)
    {
        _result = b; _game = b.Game; _prompt = b.Prompt; _style = b.Style; _tab = "create";
        _peek = peek >= 0 && peek < b.Picks.Count ? b.Picks[peek].Mod.Id : null;
        _peekImage = 0;
    }

    static List<GameState> Games() => AppState.Games
        .Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft && g.Def.HasExternalCatalog)
        .OrderByDescending(g => Features.NewGames.Recent(g)).ToList();

    static string IconOf(string name) => typeof(Icons).GetField(name)?.GetValue(null) as string ?? Icons.Star;

    public override void Build()
    {
        var col = StoreKit.Fit(new StackPanel { Spacing = 18, Margin = new Thickness(30, 22, 30, 34) }, 1400);
        col.Children.Add(Hero());
        col.Children.Add(Ui.TabBar(
            Tab("create", I18n.T("v98.tab.create"), Icons.Sparkles),
            Tab("mine", I18n.T("v98.tab.mine", ("n", AiBuilds.All().Count)), Icons.Layers),
            Ui.Button(I18n.T("v98.tab.player"), () => MainWindow.Current?.Navigate(() => new ProfilePage()), "tab", Icons.Trophy)));
        col.Children.Add(_tab switch { "mine" => Mine(), _ => Create() });
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Button Tab(string id, string text, string icon)
    {
        var b = Ui.Button(text, () => { _tab = id; Build(); }, "tab", icon);
        if (_tab == id) b.Classes.Add("active");
        return b;
    }

    // ---------------------------------------------------------------- шапка

    static Control Hero()
    {
        var accent = Gx.Accent;
        var background = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Look.Mix(accent, Colors.Black, 0.1), 0),
                    new GradientStop(Color.Parse("#B23ACF"), 0.55),
                    new GradientStop(Color.Parse("#E83E8C"), 1),
                },
            },
        };
        var engine = AiBuilds.HasClaude ? I18n.T("v98.engine.claude") : I18n.T("v98.engine.local");
        var words = Ui.Col(8,
            Gx.Eyebrow(I18n.T("v98.hero.eyebrow"), Ui.Hex("#FFE3F3")),
            Gx.Title(I18n.T("v98.hero.title"), 34, Brushes.White, 1),
            new TextBlock { Text = I18n.T("v98.hero.text"), FontSize = 14, Foreground = Ui.Hex("#F5ECFF"), TextWrapping = TextWrapping.Wrap, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left },
            Ui.Row(8, Gx.Tag(engine, new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Brushes.White),
                Gx.Tag(I18n.T("v98.hero.real"), new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Brushes.White)));
        words.VerticalAlignment = VerticalAlignment.Center;
        var orb = new Border
        {
            Width = 112, Height = 112, CornerRadius = new CornerRadius(56), VerticalAlignment = VerticalAlignment.Center,
            Background = new RadialGradientBrush { GradientStops = { new GradientStop(Color.FromArgb(150, 255, 255, 255), 0), new GradientStop(Color.FromArgb(20, 255, 255, 255), 1) } },
            BoxShadow = Gx.Glow(140, 40),
            Child = Ui.Icon(Icons.Sparkles, 52, Brushes.White),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24, Margin = new Thickness(30, 26) };
        grid.Children.Add(words);
        Grid.SetColumn(orb, 1);
        grid.Children.Add(orb);
        return new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true, MinHeight = 170, Child = new Panel { Children = { background, Gx.Scanlines(), grid } } };
    }

    // ---------------------------------------------------------------- конструктор

    Control Create()
    {
        var games = Games();
        if (games.Count == 0)
            return Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v98.noGames"), "h3"), Ui.Text(I18n.T("v98.noGames.text"), "muted", wrap: true),
                Ui.Button(I18n.T("add.title"), () => MainWindow.Current?.Navigate(() => new AddGamePage()), "primary", Icons.Plus)), 24);
        if (_game is null || games.All(g => g.Def.Id != _game)) _game = games[0].Def.Id;

        var left = new StackPanel { Spacing = 18 };
        left.Children.Add(Composer(games));
        if (_busy) left.Children.Add(Ui.Card(Ui.Col(10, Ui.Row(10, Ui.Icon(Icons.Sparkles, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("v98.busy"), "h3")),
            new ProgressBar { IsIndeterminate = true, Height = 4 }, Ui.Text(I18n.T("v98.busy.text"), "small muted", wrap: true)), 22));
        if (_error is not null) left.Children.Add(Ui.Card(Ui.Row(10, Ui.Icon(Icons.Alert, 18, Ui.Res("Bad")), Ui.Text(_error, "muted", wrap: true)), 18));
        if (_result is not null && !_busy) left.Children.Add(Result(_result));

        // 16.0: нажали на мод сборки — справа его просмотр вместо идей и «Мода недели».
        var peek = _result is { } r && !_busy ? r.Picks.FirstOrDefault(p => p.Mod.Id == _peek) : null;
        Control right = peek is not null ? Peek(_result!, peek) : Ui.Col(18, Ideas(), Weekly());
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(peek is not null ? "*,460" : "*,320"), ColumnSpacing = 18 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    static Control Step(int n, string text) => Ui.Row(10,
        new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Res("BrandSoft"), Child = new TextBlock { Text = n.ToString(), FontWeight = FontWeight.Bold, Foreground = Ui.Res("Brand2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
        Ui.Text(text, "h3"));

    Control Composer(List<GameState> games)
    {
        var gameRow = new WrapPanel();
        foreach (var g in games.Take(14))
        {
            var id = g.Def.Id;
            var pic = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Ui.GameImage(g.Def, 120) };
            var b = new Button { Classes = { "chip" }, Margin = new Thickness(0, 0, 8, 8), Content = Ui.Row(8, pic, Ui.Text(g.Def.Name)) };
            if (id == _game) b.Classes.Add("active");
            b.Click += (_, _) => { _game = id; Build(); };
            gameRow.Children.Add(b);
        }

        var box = new TextBox
        {
            Text = _prompt, Watermark = I18n.T("v98.prompt.hint"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 92, MaxLength = 600, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.TextChanged += (_, _) => _prompt = box.Text ?? "";

        var styles = new WrapPanel();
        foreach (var (sid, _) in AiBuilds.Styles)
        {
            var icon = sid switch { "beauty" => Icons.Image, "hardcore" => Icons.Flame, "coop" => Icons.Users, "content" => Icons.Package, "speed" => Icons.Zap, _ => Icons.Sparkles };
            var b = Ui.Button(I18n.T("v98.ai.style." + sid), () => { _style = sid; Build(); }, "chip", icon);
            b.Margin = new Thickness(0, 0, 8, 8);
            if (sid == _style) b.Classes.Add("active");
            styles.Children.Add(b);
        }

        var sizes = Ui.Row(8);
        foreach (var n in new[] { 5, 10, 15, 25 })
        {
            var b = Ui.Button(I18n.T("v98.size", ("n", n)), () => { _size = n; Build(); }, "chip");
            if (n == _size) b.Classes.Add("active");
            sizes.Children.Add(b);
        }
        var cheats = new CheckBox { Content = I18n.T("v98.noCheats"), IsChecked = _noCheats, VerticalAlignment = VerticalAlignment.Center };
        cheats.IsCheckedChanged += (_, _) => _noCheats = cheats.IsChecked == true;

        var go = Ui.Button(_busy ? I18n.T("v98.stop") : I18n.T("v98.go"), () => { if (_busy) _cts?.Cancel(); else _ = Generate(); }, _busy ? "" : "primary", _busy ? Icons.Stop : Icons.Sparkles);
        go.MinHeight = 44;
        go.Padding = new Thickness(22, 0);
        var footer = new DockPanel();
        DockPanel.SetDock(go, Dock.Right);
        footer.Children.Add(go);
        footer.Children.Add(Ui.Row(18, sizes, cheats));

        return Ui.Card(Ui.Col(14,
            Step(1, I18n.T("v98.step.game")), gameRow,
            Step(2, I18n.T("v98.step.prompt")), box,
            Step(3, I18n.T("v98.step.style")), styles,
            footer), 24);
    }

    async Task Generate()
    {
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == _game);
        if (g is null) return;
        _busy = true; _error = null; _off.Clear(); _peek = null;
        _cts = new CancellationTokenSource();
        Build();
        try
        {
            _result = await AiBuilds.Generate(g, _prompt, _style, _size, _noCheats, _cts.Token);
            AiBuilds.Save(_result);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { _error = Jobs.Explain(e); }
        _busy = false;
        Build();
    }

    Control Result(AiBuild b)
    {
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game);
        var on = b.Picks.Where(p => !_off.Contains(p.Mod.Id)).ToList();
        var install = Ui.Button(I18n.T("v98.install", ("n", on.Count)), () => Install(b, on), "primary", Icons.Download);
        install.IsEnabled = on.Count > 0 && g?.Registry is not null;
        var buttons = Ui.Row(8,
            Ui.Button(I18n.T("v98.again"), () => _ = Generate(), "ghost", Icons.Shuffle),
            Ui.Button(I18n.T("v98.export"), () => _ = ExportFile(b), "", Icons.Upload),
            install);
        var head = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(Ui.Col(6,
            Ui.Row(8, Gx.Tag(b.Engine == "claude" ? "Claude" : I18n.T("v98.engine.short"), Ui.Res("BrandSoft"), Ui.Res("Brand2")), Ui.Text(g?.Def.Name ?? b.Game, "small muted")),
            Gx.Title(b.Name, 22, null, 2)));

        var list = new StackPanel { Spacing = 6 };
        foreach (var p in b.Picks) list.Children.Add(PickRow(b, p));
        var body = Ui.Col(14, head);
        if (b.Note != "") body.Children.Add(Ui.Text(b.Note, "muted", wrap: true));
        body.Children.Add(Ui.Text(I18n.T("v16.ai.peek.hint"), "small muted", wrap: true));
        body.Children.Add(list);
        body.Children.Add(RefineBox(b));
        if (g?.Registry is null) body.Children.Add(Ui.Text(I18n.T("v98.noLoader"), "small muted", wrap: true));
        var card = Ui.Card(body, 24);
        Animate.From(card, "translateY(12px)", 260, 0, new Avalonia.Animation.Easings.QuinticEaseOut());
        return card;
    }

    Control PickRow(AiBuild b, AiPick p)
    {
        var id = p.Mod.Id;
        var check = new CheckBox { IsChecked = !_off.Contains(id), VerticalAlignment = VerticalAlignment.Center };
        check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) _off.Remove(id); else _off.Add(id); Build(); };
        var installed = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game)?.Registry?.Has(id) == true;
        var text = Ui.Col(3,
            Ui.Row(8, Ui.Text(p.Mod.Name, "h3"), installed ? Gx.Tag(I18n.T("pack.have"), Ui.Res("Surface3"), Ui.Res("Good")) : new Panel()),
            Ui.Row(6, Ui.Icon(Icons.Sparkles, 13, Ui.Res("Brand2")), Ui.Text(p.Why, "small muted")));
        text.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 12 };
        row.Children.Add(check);
        var thumb = Ui.Thumb(p.Mod.Icon, p.Mod.Name, 44);
        Grid.SetColumn(thumb, 1);
        row.Children.Add(thumb);
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        var tail = Ui.Row(10);
        tail.VerticalAlignment = VerticalAlignment.Center;
        if (p.Mod.Author != "" && _peek is null) tail.Children.Add(Ui.Text(p.Mod.Author, "small muted"));
        if (p.Match > 0) tail.Children.Add(MatchPill(p.Match));
        Grid.SetColumn(tail, 3);
        row.Children.Add(tail);
        var eye = Ui.Icon(Icons.ChevronRight, 14, Ui.Res("Faint"));
        eye.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(eye, 4);
        row.Children.Add(eye);
        var on = _peek == id;
        var host = new Border
        {
            Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(12), Background = on ? Ui.Res("BrandSoft") : Ui.Res("Surface2"), Child = row,
            BorderBrush = on ? Ui.Res("Brand") : Brushes.Transparent, BorderThickness = new Thickness(1),
            Opacity = _off.Contains(id) ? 0.5 : 1, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        ToolTip.SetTip(host, I18n.T("v16.ai.peek.tip"));
        // 16.0: нажатие на мод (не на галочку) — просмотр справа; повторное — закрыть.
        host.PointerReleased += (_, e) =>
        {
            if (e.Source is Avalonia.Visual v && (v is CheckBox || v.FindAncestorOfType<CheckBox>() is not null)) return;
            OpenPeek(_peek == id ? null : id);
        };
        return host;
    }

    static async void Install(AiBuild b, List<AiPick> picks)
    {
        var w = MainWindow.Current;
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game);
        if (w is null || g?.Registry is null) return;
        try
        {
            Snapshots.Take(g, "import");
            var ids = picks.Where(p => !g.Registry.Has(p.Mod.Id)).Select(p => p.Mod.Id).ToList();
            if (ids.Count == 0) { w.Toast(I18n.T("v98.allHave")); return; }
            var mods = Program.Demo ? Demo.Many(g.Def, ids) : await Catalog.Many(g.Def, ids);
            await Actions.InstallQueue(g, b.Name, mods.Select(m => (m, (Pin?)null)).ToList());
            w.Toast(I18n.T("v98.queued", ("n", mods.Count)));
        }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
    }

    static async Task ExportFile(AiBuild b)
    {
        var w = MainWindow.Current!;
        var file = await w.PickSaveFile(I18n.T("dialog.packExport"), $"{b.Game}-ai.modhub.json");
        if (file is null) return;
        await File.WriteAllTextAsync(file, AiBuilds.Export(b));
        w.Toast(I18n.T("pack.exported", ("n", b.Picks.Count)));
    }

    static Control MatchPill(int match)
    {
        var color = match >= 80 ? Ui.Res("Good") : match >= 58 ? Ui.Res("Brand2") : Ui.Res("Muted");
        var pill = new Border
        {
            CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), BorderBrush = color, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = match + "%", FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = color },
        };
        ToolTip.SetTip(pill, I18n.T("v16.ai.match.tip"));
        return pill;
    }

    // ---------------------------------------------------------------- 16.0: уточнить сборку

    Control RefineBox(AiBuild b)
    {
        var box = new TextBox { Text = _refine, Watermark = I18n.T("v16.ai.refine.hint"), MaxLength = 300, MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) => _refine = box.Text ?? "";
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && _refine.Trim() != "") { e.Handled = true; _ = Refine(b, _refine); } };
        var go = Ui.Button(I18n.T("v16.ai.refine"), () => { if (_refine.Trim() != "") _ = Refine(b, _refine); }, "", Icons.Wand);
        go.MinHeight = 38;
        var line = new DockPanel();
        DockPanel.SetDock(go, Dock.Right);
        go.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(go);
        line.Children.Add(box);
        var chips = new WrapPanel();
        for (var i = 1; i <= 5; i++)
        {
            var text = I18n.T($"v16.ai.refine.{i}");
            var c = Ui.Button(text, () => _ = Refine(b, text), "chip");
            c.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(c);
        }
        return new Border
        {
            Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(12), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1),
            Child = Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Wand, 15, Ui.Res("Brand2")), Ui.Text(I18n.T("v16.ai.refine.title"), "strong")), line, chips),
        };
    }

    async Task Refine(AiBuild b, string instruction)
    {
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game);
        if (g is null || _busy) return;
        _busy = true; _error = null; _peek = null;
        _cts = new CancellationTokenSource();
        Build();
        try
        {
            _result = await AiBuilds.Refine(g, b, instruction, _size, _noCheats, _cts.Token);
            AiBuilds.Save(_result);
            _prompt = _result.Prompt;
            _refine = "";
            _off.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { _error = Jobs.Explain(e); }
        _busy = false;
        Build();
    }

    // ---------------------------------------------------------------- 16.0: просмотр мода

    void OpenPeek(string? id)
    {
        _peek = id;
        _peekImage = 0;
        Build();
    }

    static string Key(AiBuild b, ModInfo m) => b.Game + "|" + m.Id;

    async Task LoadPeek(AiBuild b, ModInfo mod)
    {
        var key = Key(b, mod);
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game);
        if (g is null) return;
        try
        {
            var d = Program.Demo ? Demo.Details(g.Def, mod) : await Details.Load(g.Def, mod.Id, source: mod.Source);
            _details[key] = d;
        }
        catch { _details[key] = new ModDetails(mod, [], mod.Icon is null ? [] : [mod.Icon], []); }
        try { _alts[key] = await AiBuilds.Alternatives(g, b, mod); } catch { _alts[key] = []; }
        if (_peek == mod.Id && MainWindow.Current?.CurrentPage == this) Build();
    }

    Control Peek(AiBuild b, AiPick p)
    {
        var mod = p.Mod;
        var key = Key(b, mod);
        _details.TryGetValue(key, out var d);
        if (d is null) _ = LoadPeek(b, mod);
        var at = b.Picks.IndexOf(p);

        // Шапка: «Просмотр», листать моды сборки, закрыть.
        var prev = Ui.Button("", () => OpenPeek(b.Picks[(at + b.Picks.Count - 1) % b.Picks.Count].Mod.Id), "icon ghost", Icons.ChevronLeft, I18n.T("v16.ai.peek.prev"));
        var next = Ui.Button("", () => OpenPeek(b.Picks[(at + 1) % b.Picks.Count].Mod.Id), "icon ghost", Icons.ChevronRight, I18n.T("v16.ai.peek.next"));
        var close = Ui.Button("", () => OpenPeek(null), "icon ghost", Icons.Close, I18n.T("v16.ai.peek.close"));
        var tools = Ui.Row(2, prev, Ui.Text($"{at + 1}/{b.Picks.Count}", "small muted"), next, close);
        foreach (var c in tools.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        head.Children.Add(Gx.Eyebrow(I18n.T("v16.ai.peek"), Ui.Res("Brand2")));

        // Галерея: большая картинка и до шести миниатюр.
        var pictures = (d?.Images ?? []).Where(u => !string.IsNullOrEmpty(u)).Distinct().Take(8).ToList();
        if (pictures.Count == 0 && mod.Icon is not null) pictures.Add(mod.Icon);
        _peekImage = Math.Clamp(_peekImage, 0, Math.Max(0, pictures.Count - 1));
        Control big = pictures.Count == 0
            ? new Border { Background = Ui.Res("Surface2"), Child = d is null ? new ProgressBar { IsIndeterminate = true, Width = 120, VerticalAlignment = VerticalAlignment.Center } : Ui.Icon(Icons.Image, 34, Ui.Res("Faint")) }
            : Picture(pictures[_peekImage], 900);
        var gallery = Ui.Col(8, new Border { Height = 230, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = big });
        if (pictures.Count > 1)
        {
            var strip = new WrapPanel();
            for (var i = 0; i < Math.Min(6, pictures.Count); i++)
            {
                var n = i;
                var t = new Border
                {
                    Width = 64, Height = 40, CornerRadius = new CornerRadius(7), ClipToBounds = true, Margin = new Thickness(0, 0, 6, 0),
                    BorderBrush = n == _peekImage ? Ui.Res("Brand") : Brushes.Transparent, BorderThickness = new Thickness(2),
                    Child = Picture(pictures[n], 160), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };
                StoreKit.OnClick(t, () => { _peekImage = n; Build(); });
                strip.Children.Add(t);
            }
            gallery.Children.Add(strip);
        }

        var full = d?.Mod ?? mod;
        var by = string.Join(" · ", new[] { full.Author, full.Version == "" ? null : "v" + full.Version }.Where(x => !string.IsNullOrEmpty(x)));
        var stats = Ui.Row(18);
        if (full.Downloads > 0) stats.Children.Add(Gx.Stat(Short(full.Downloads), I18n.T("v16.ai.peek.downloads"), null, 18));
        if (full.Rating > 0) stats.Children.Add(Gx.Stat(Short(full.Rating), I18n.T("v16.ai.peek.likes"), null, 18));
        if (full.UpdatedAt is { } u) stats.Children.Add(Gx.Stat(Ui.Ago(u), I18n.T("v16.ai.peek.updated"), null, 18));
        var tags = new WrapPanel();
        foreach (var c in full.Categories.Take(6))
        {
            var tag = Gx.Tag(c, Ui.Res("Surface3"), Ui.Res("Muted"));
            tag.Margin = new Thickness(0, 0, 6, 6);
            tags.Children.Add(tag);
        }

        // Почему ИИ взял мод и насколько он подходит.
        var why = new Border
        {
            Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(10), Background = Ui.Res("BrandSoft"),
            Child = Ui.Col(8,
                Ui.Row(8, Ui.Icon(Icons.Sparkles, 14, Ui.Res("Brand2")), Ui.Text(I18n.T("v16.ai.peek.why"), "small strong"), p.Match > 0 ? MatchPill(p.Match) : new Panel()),
                Ui.Text(p.Why == "" ? I18n.T("v98.ai.why.rated") : p.Why, "small", wrap: true)),
        };

        // Описание: первые абзацы; целиком — на странице мода.
        var about = new StackPanel { Spacing = 6 };
        var blocks = d?.Blocks ?? [];
        var shown = 0;
        foreach (var block in blocks)
        {
            if (shown > 900) break;
            var t = block.Kind switch
            {
                "h" => Ui.Text(block.Text, "strong", wrap: true),
                "li" => Ui.Text("•  " + block.Text, "small", wrap: true),
                _ => Ui.Text(block.Text, "small muted", wrap: true),
            };
            about.Children.Add(t);
            shown += block.Text.Length;
        }
        if (about.Children.Count == 0 && full.Description != "") about.Children.Add(Ui.Text(full.Description, "small muted", wrap: true));
        if (d is null && about.Children.Count == 0) about.Children.Add(Ui.Text(I18n.T("v16.ai.peek.loading"), "small muted"));

        var col = Ui.Col(14, head, gallery,
            Ui.Col(4, Gx.Title(full.Name, 22, null, 2), by == "" ? new Panel() : Ui.Text(by, "small muted")));
        if (stats.Children.Count > 0) col.Children.Add(stats);
        if (tags.Children.Count > 0) col.Children.Add(tags);
        col.Children.Add(why);
        col.Children.Add(about);

        // Что ему нужно: зависимости, и есть ли они в сборке.
        if (d?.Requirements is { Count: > 0 } reqs)
        {
            var list = Ui.Col(4, Ui.Text(I18n.T("v16.ai.peek.needs"), "small strong"));
            foreach (var r in reqs.Take(6))
            {
                var inBuild = b.Picks.Any(x => Recommend.Bare(x.Mod.Id) == Recommend.Bare(r.Id));
                list.Children.Add(Ui.Row(8, Ui.Icon(inBuild ? Icons.Check : Icons.Package, 13, inBuild ? Ui.Res("Good") : Ui.Res("Muted")),
                    Ui.Text(r.Name + (inBuild ? "  · " + I18n.T("v16.ai.peek.inBuild") : ""), "small")));
            }
            col.Children.Add(list);
        }

        // Кнопки: страница мода, в сборке или нет, поставить только его.
        var off = _off.Contains(mod.Id);
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game);
        var page = Ui.Button(I18n.T("v16.ai.peek.page"), () => { if (g is not null) MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, full)); }, "primary", Icons.External);
        var toggle = Ui.Button(off ? I18n.T("v16.ai.peek.add") : I18n.T("v16.ai.peek.remove"), () => { if (!_off.Remove(mod.Id)) _off.Add(mod.Id); Build(); }, "", off ? Icons.Plus : Icons.Close);
        var one = Ui.Button("", () => Install(b, [p]), "icon", Icons.Download, I18n.T("v16.ai.peek.installOne"));
        one.IsEnabled = g?.Registry is not null;
        var buttons = new WrapPanel();
        foreach (var x in new Control[] { page, toggle, one }) { x.Margin = new Thickness(0, 0, 8, 6); buttons.Children.Add(x); }
        col.Children.Add(buttons);

        // Похожие: заменить мод одним нажатием.
        if (_alts.TryGetValue(key, out var alts) && alts.Count > 0)
        {
            var list = Ui.Col(6, Ui.Row(8, Ui.Icon(Icons.Shuffle, 14, Ui.Res("Brand2")), Ui.Text(I18n.T("v16.ai.peek.alts"), "small strong")));
            foreach (var alt in alts)
            {
                var a = alt;
                var swap = Ui.Button(I18n.T("v16.ai.peek.swap"), () =>
                {
                    _result = AiBuilds.Replace(b, mod.Id, a);
                    _peek = a.Id;
                    _peekImage = 0;
                    Build();
                }, "chip");
                swap.VerticalAlignment = VerticalAlignment.Center;
                var line = new DockPanel();
                DockPanel.SetDock(swap, Dock.Right);
                line.Children.Add(swap);
                var words = Ui.Col(1, Ui.Text(a.Name, "small strong"), Ui.Text(a.Downloads > 0 ? I18n.T("v98.ai.why.popular", ("n", Short(a.Downloads))) : a.Author, "tiny muted"));
                words.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(Ui.Row(10, Ui.Thumb(a.Icon, a.Name, 34, 8, 80), words));
                list.Children.Add(line);
            }
            col.Children.Add(list);
        }

        var card = Ui.Card(col, 18);
        Animate.From(card, "translateX(14px)", 220, 0, new Avalonia.Animation.Easings.QuinticEaseOut());
        return card;
    }

    static Control Picture(string url, int decode)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var host = new Panel { Children = { new Border { Background = Ui.Res("Surface2") }, image } };
        _ = Images.FromUrl(url, decode).ContinueWith(t =>
        {
            if (t.Result is { } bmp) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bmp);
        });
        return host;
    }

    static string Short(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();

    // ---------------------------------------------------------------- справа

    Control Ideas()
    {
        var col = Ui.Col(8, Ui.Row(8, Ui.Icon(Icons.Wand, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("v98.ideas"), "h3")));
        for (var i = 1; i <= 5; i++)
        {
            var text = I18n.T($"v98.idea.{i}");
            var b = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = Ui.Text(text, "small", wrap: true) };
            b.Click += (_, _) => { _prompt = text; Build(); };
            col.Children.Add(b);
        }
        if (!AiBuilds.HasClaude)
        {
            col.Children.Add(Ui.Text(I18n.T("v98.keyHint"), "small muted", wrap: true));
            col.Children.Add(Ui.Button(I18n.T("v98.key.connect"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "", Icons.Key));
        }
        return Ui.Card(col, 18);
    }

    // «Мод недели» как бесплатная игра недели в Epic: один мод на неделю, меняется по понедельникам.
    static int Week => System.Globalization.ISOWeek.GetWeekOfYear(DateTime.UtcNow) + DateTime.UtcNow.Year * 53;

    async Task LoadWeekly()
    {
        if (_weeklyFor == Week) return;
        var games = Games();
        if (games.Count == 0) return;
        var g = games[Week % games.Count];
        try
        {
            var mods = Program.Demo ? Demo.Catalog(g.Def, new Query()).Mods : (await Catalog.Browse(g.Def, new Query(Sort: SortBy.Rating))).Mods;
            mods = mods.Where(m => !m.Adult && !m.Deprecated).Take(30).ToList();
            if (mods.Count == 0) return;
            _weekly = (g.Def.Id, mods[Week % mods.Count]);
            _weeklyFor = Week;
            if (_tab == "create") Build();
        }
        catch { }
    }

    static Control Weekly()
    {
        if (_weekly is not { } wk) return new Panel();
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == wk.Game);
        if (g is null) return new Panel();
        var days = ((int)DayOfWeek.Monday - (int)DateTime.UtcNow.DayOfWeek + 7) % 7;
        if (days == 0) days = 7;
        var top = new Panel { Height = 130, Children = { Ui.GameImage(g.Def, 480), new Border { Background = Gx.ShadeUp(0.2) } } };
        var badge = Gx.Tag(I18n.T("v98.weekly"), Ui.Hex("#FFD84D"), Ui.Hex("#1A1400"));
        badge.Margin = new Thickness(12);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        badge.VerticalAlignment = VerticalAlignment.Top;
        top.Children.Add(badge);
        var body = Ui.Col(6,
            Ui.Row(10, Ui.Thumb(wk.Mod.Icon, wk.Mod.Name, 40), Ui.Col(2, Ui.Text(wk.Mod.Name, "h3"), Ui.Text(g.Def.Name, "small muted"))),
            Ui.Text(wk.Mod.Description, "small muted", wrap: true),
            Ui.Text(I18n.T("v98.weekly.left", ("n", days)), "small brand"));
        body.Margin = new Thickness(16, 12, 16, 16);
        (body.Children[1] as TextBlock)!.MaxLines = 3;
        var card = new Border { Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true, Child = Ui.Col(0, top, body), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        card.PointerReleased += (_, _) => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, wk.Mod));
        return card;
    }

    // ---------------------------------------------------------------- мои сборки

    Control Mine()
    {
        var all = AiBuilds.All().OrderByDescending(b => b.Likes).ThenByDescending(b => b.CreatedAt).ToList();
        if (all.Count == 0)
            return Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v98.mine.empty"), "h3"), Ui.Text(I18n.T("v98.mine.empty.text"), "muted", wrap: true),
                Ui.Button(I18n.T("v98.go"), () => { _tab = "create"; Build(); }, "primary", Icons.Sparkles)), 24);
        var wrap = new WrapPanel();
        foreach (var b in all) wrap.Children.Add(BuildCard(b));
        return wrap;
    }

    Control BuildCard(AiBuild b)
    {
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game);
        var top = new Panel { Height = 120 };
        if (g is not null) top.Children.Add(Ui.GameImage(g.Def, 480));
        top.Children.Add(new Border { Background = Gx.ShadeUp(0.1) });
        var name = Gx.Title(b.Name, 17, Brushes.White, 2);
        name.Margin = new Thickness(14, 0, 14, 12);
        name.VerticalAlignment = VerticalAlignment.Bottom;
        top.Children.Add(name);
        var thumbs = Ui.Row(-8);
        foreach (var p in b.Picks.Take(6))
        {
            var pick = p;
            var t = new Border { CornerRadius = new CornerRadius(9), BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(2), ClipToBounds = true, Child = Ui.Thumb(p.Mod.Icon, p.Mod.Name, 30, 8, 80), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            ToolTip.SetTip(t, p.Mod.Name);
            // 16.0: значок мода — сразу к сборке с этим модом в просмотре.
            StoreKit.OnClick(t, () => { _result = b; _game = b.Game; _prompt = b.Prompt; _style = b.Style; _off.Clear(); _tab = "create"; _peek = pick.Mod.Id; _peekImage = 0; Build(); });
            thumbs.Children.Add(t);
        }
        var like = Ui.Button(b.Likes > 0 ? "♥" : "♡", () => { AiBuilds.Like(b); Build(); }, "icon ghost", null, I18n.T("v98.like"));
        var open = Ui.Button(I18n.T("v98.open"), () => { _result = b; _game = b.Game; _prompt = b.Prompt; _style = b.Style; _off.Clear(); _tab = "create"; _peek = null; Build(); }, "primary");
        var remove = Ui.Button("", () => { AiBuilds.Remove(b.Id); if (_result?.Id == b.Id) _result = null; Build(); }, "icon ghost", Icons.Trash, I18n.T("v98.remove"));
        var actions = Ui.Row(6, open, Ui.Button("", () => Install(b, b.Picks), "icon", Icons.Download, I18n.T("v98.install", ("n", b.Picks.Count))), like, remove);
        var body = Ui.Col(10,
            Ui.Row(8, Gx.Tag(b.Engine == "claude" ? "Claude" : I18n.T("v98.engine.short"), Ui.Res("BrandSoft"), Ui.Res("Brand2")),
                Ui.Text(I18n.T("v98.mods", ("n", b.Picks.Count)) + " · " + Ui.Ago(b.CreatedAt), "small muted")),
            thumbs, actions);
        body.Margin = new Thickness(14, 12, 14, 14);
        return new Border { Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true, Width = 300, Margin = new Thickness(0, 0, 16, 16), Child = Ui.Col(0, top, body) };
    }
}
