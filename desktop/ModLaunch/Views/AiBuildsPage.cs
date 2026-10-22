using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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

    bool _busy;
    string? _error;
    CancellationTokenSource? _cts;

    public AiBuildsPage(string tab = "create") { _tab = tab; _ = LoadWeekly(); }

    public override string Title => I18n.T("v98.nav.ai");

    /// <summary>Для снимков: показать готовую сборку.</summary>
    public static void DemoResult(AiBuild b) { _result = b; _game = b.Game; _prompt = b.Prompt; _style = b.Style; _tab = "create"; }

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

        var right = Ui.Col(18, Ideas(), Weekly());
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,320"), ColumnSpacing = 18 };
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
        _busy = true; _error = null; _off.Clear();
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
        body.Children.Add(list);
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
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 12 };
        row.Children.Add(check);
        var thumb = Ui.Thumb(p.Mod.Icon, p.Mod.Name, 44);
        Grid.SetColumn(thumb, 1);
        row.Children.Add(thumb);
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        if (p.Mod.Author != "")
        {
            var by = Ui.Text(p.Mod.Author, "small muted");
            by.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(by, 3);
            row.Children.Add(by);
        }
        var host = new Border { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface2"), Child = row, Opacity = _off.Contains(id) ? 0.5 : 1 };
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
        foreach (var p in b.Picks.Take(6)) thumbs.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderBrush = Ui.Res("Surface"), BorderThickness = new Thickness(2), ClipToBounds = true, Child = Ui.Thumb(p.Mod.Icon, p.Mod.Name, 30, 8, 80) });
        var like = Ui.Button(b.Likes > 0 ? "♥" : "♡", () => { AiBuilds.Like(b); Build(); }, "icon ghost", null, I18n.T("v98.like"));
        var open = Ui.Button(I18n.T("v98.open"), () => { _result = b; _game = b.Game; _prompt = b.Prompt; _style = b.Style; _off.Clear(); _tab = "create"; Build(); }, "primary");
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
