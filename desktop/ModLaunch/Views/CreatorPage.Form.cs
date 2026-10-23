using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 16.0: «Создать проект» — анкета вместо маленького окна. Пять вопросов на одном экране: название и описание,
/// игра (сразу виден мост: движок, загрузчик, язык, уверенность и улики), что делает мод, когда срабатывает,
/// автор, версия и цвет. Справа — живая карточка будущего проекта. «Создать» открывает среду ModScript X
/// (XStudio) с готовым кодом по ответам. Игра без кода (GameMaker, Source…) — честно говорим, что мост
/// через файлы, и открываем классический мастер ModScript с той же игрой и именем.
/// </summary>
public sealed partial class CreatorPage
{
    sealed class FormState
    {
        public string Name = "";
        public string About = "";
        public string? Game;
        public string? Folder;
        public string Kind = "gameplay";
        public HashSet<string> Events = ["start", "tick"];
        public string Author = Templates.DefaultAuthor();
        public string Version = "1.0.0";
        public string Color = "violet";
        /// <summary>Движок вручную — когда игру не выбрали (только код, без игры).</summary>
        public string ManualTarget = "unity";
        public bool Busy;
    }

    static FormState _form = new();
    static readonly Dictionary<string, MX.BridgeReport> Bridges = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Scanning = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Открыть анкету с чистого листа (из шапки Hub, с плитки «Новый проект», из палитры команд).</summary>
    public static void OpenForm(string? game = null) => MainWindow.Current?.Navigate(() => NewForm(game));

    /// <summary>Страница анкеты с чистыми ответами: игра — последняя, с которой работали, иначе первая найденная.</summary>
    public static CreatorPage NewForm(string? game = null)
    {
        _form = new FormState();
        var found = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).ToList();
        _form.Game = game ?? found.Select(g => g.Def.Id).FirstOrDefault(id => id == Settings.Data.Str("lastGame")) ?? found.Select(g => g.Def.Id).FirstOrDefault();
        return new CreatorPage("form");
    }

    /// <summary>Папка игры анкеты: выбранная вручную или папка игры ModLaunch.</summary>
    static string? FormPath() =>
        _form.Folder ?? (_form.Game is { } id && AppState.Games.FirstOrDefault(g => g.Def.Id == id) is { Status: Detect.Found, Path: { } p } ? p : null);

    /// <summary>
    /// Мост для папки: из кэша или сканом в фоне (страница перестроится, когда он готов).
    /// В демо и на снимках папок нет — мост по загрузчику игры из каталога.
    /// </summary>
    internal static MX.BridgeReport? BridgeFor(string? path, GameDef? def)
    {
        if (Program.Demo && def is not null) return DemoBridge(def);
        if (path is null) return null;
        if (Bridges.TryGetValue(path, out var hit)) return hit;
        if (Scanning.Add(path))
            _ = Task.Run(() =>
            {
                MX.BridgeReport r;
                try { r = MX.Bridge.Scan(path); }
                catch (Exception e) { r = new MX.BridgeReport("generic", "Unknown engine", 0, [e.Message], null, "files", false, "files", null, null); }
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Bridges[path] = r;
                    Scanning.Remove(path);
                    if (MainWindow.Current?.CurrentPage is CreatorPage { Tab: "form" } page) page.Build();
                    else if (MainWindow.Current?.CurrentPage is XStudio studio) studio.Build();
                });
            });
        return null;
    }

    internal static void Rescan(string path) { Bridges.Remove(path); Scanning.Remove(path); }

    static MX.BridgeReport DemoBridge(GameDef def) => def.Loader switch
    {
        LoaderKind.Bepinex or LoaderKind.HkApi => new("unity-mono", "Unity (Mono)", 97, ["*_Data/Managed/Assembly-CSharp.dll", "UnityPlayer.dll", "*_Data/globalgamemanagers"], "unity", "BepInEx 5", def.Loader == LoaderKind.Bepinex, "code", null,
            def.Loader == LoaderKind.Bepinex ? null : "install BepInEx 5 (x64) into the game folder and run the game once"),
        LoaderKind.Smapi => new("xna", "XNA / FNA / MonoGame", 91, ["MonoGame.Framework.dll", "Content/"], null, "Content/", false, "files", null, "no code bridge for this engine: ModScript works through files — configs, ini and replaced assets"),
        _ => new("generic", "Unknown engine", 0, ["no engine fingerprints found"], null, "files", false, "files", null, "no known engine: ModScript works through files — configs, ini and replaced assets"),
    };

    // ---------------------------------------------------------------- анкета

    Control Form()
    {
        var f = _form;
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var def = f.Game is { } gid ? AppState.Games.FirstOrDefault(g => g.Def.Id == gid)?.Def : null;
        var path = FormPath();
        var bridge = BridgeFor(path, def);
        var target = bridge?.Code == true ? MX.Target.Find(bridge.TargetId!) : f.Game is null && f.Folder is null ? MX.Target.Find(f.ManualTarget) : null;
        var filesOnly = bridge is not null && !bridge.Code;

        var left = new StackPanel { Spacing = 16 };

        // 1. Название и описание
        var name = new TextBox { Text = f.Name, Watermark = I18n.T("v16.form.name.hint"), MaxLength = 80, FontSize = 16, MinHeight = 42, VerticalContentAlignment = VerticalAlignment.Center };
        var about = new TextBox { Text = f.About, Watermark = I18n.T("v16.form.about.hint"), MaxLength = 300, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64 };
        left.Children.Add(Question(1, I18n.T("v16.form.q1"), I18n.T("v16.form.q1.text"), Ui.Col(10, name, about)));

        // 2. Игра и мост
        var tiles = new WrapPanel();
        foreach (var g in games.Take(18))
        {
            var id = g.Def.Id;
            var on = f.Folder is null && f.Game == id;
            var cover = new Border { Width = 68, Height = 92, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Ui.GameImage(g.Def, 160, art: Images.Art.Cover) };
            var label = new TextBlock { Text = g.Def.ShortName, FontSize = 11.5, Width = 76, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) };
            var b = new Button
            {
                Classes = { "ghost" }, Padding = new Thickness(5), Margin = new Thickness(0, 0, 8, 8), Content = Ui.Col(0, cover, label),
                BorderBrush = on ? HubLook.Violet : Brushes.Transparent, BorderThickness = new Thickness(2),
            };
            ToolTip.SetTip(b, g.Def.Name);
            b.Click += (_, _) => { Keep(name, about); f.Game = id; f.Folder = null; Build(); };
            tiles.Children.Add(b);
        }
        var folder = Ui.Button(f.Folder is null ? I18n.T("v16.form.folder") : Path.GetFileName(f.Folder.TrimEnd('\\', '/')), () => _ = PickFolder(name, about), f.Folder is null ? "" : "primary", Icons.Folder);
        var none = Ui.Button(I18n.T("v16.form.noGame"), () => { Keep(name, about); f.Game = null; f.Folder = null; Build(); }, f.Game is null && f.Folder is null ? "primary" : "ghost", Icons.Code);
        ToolTip.SetTip(none, I18n.T("v16.form.noGame.tip"));
        var gameCol = Ui.Col(12, tiles, Ui.Row(8, folder, none));
        if (f.Game is null && f.Folder is null)
        {
            var engine = new ComboBox { MinWidth = 260 };
            foreach (var t in MX.Target.All) engine.Items.Add(new ComboBoxItem { Content = $"{t.Title} · {XStudio.Lang(t.Language)}", Tag = t.Id });
            engine.SelectedItem = engine.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == f.ManualTarget);
            engine.SelectionChanged += (_, _) => { if (engine.SelectedItem is ComboBoxItem { Tag: string id }) { Keep(name, about); f.ManualTarget = id; Build(); } };
            gameCol.Children.Add(Ui.Row(10, Ui.Text(I18n.T("v16.form.engine"), "small muted"), engine));
        }
        else gameCol.Children.Add(BridgeCard(bridge, path is null && !Program.Demo));
        left.Children.Add(Question(2, I18n.T("v16.form.q2"), I18n.T("v16.form.q2.text"), gameCol));

        // 3. Что делает мод
        var kinds = new UniformGrid { Columns = 3 };
        foreach (var k in XProjects.Kinds)
        {
            var kind = k;
            var on = f.Kind == k;
            var icon = k switch { "economy" => Icons.Bag, "messages" => Icons.Bell, "difficulty" => Icons.Flame, "cheats" => Icons.Zap, "empty" => Icons.FilePlus, _ => Icons.Gamepad };
            var card = new Button
            {
                Classes = { "ghost" }, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 10), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderBrush = on ? HubLook.Violet : HubLook.Edge, BorderThickness = new Thickness(on ? 2 : 1), Background = on ? Ui.Res("BrandSoft") : Brushes.Transparent,
                Content = Ui.Col(4, Ui.Row(8, Ui.Icon(icon, 16, on ? HubLook.Violet : Ui.Res("Muted")), Ui.Text(I18n.T($"v16.kind.{k}"), "strong")),
                    Ui.Text(I18n.T($"v16.kind.{k}.text"), "small muted", wrap: true)),
            };
            card.Click += (_, _) => { Keep(name, about); f.Kind = kind; Build(); };
            kinds.Children.Add(card);
        }
        left.Children.Add(Question(3, I18n.T("v16.form.q3"), I18n.T("v16.form.q3.text"), kinds));

        // 4. Когда срабатывает
        var events = new WrapPanel();
        foreach (var e in MX.ModxDoc.Events)
        {
            var ev = e;
            var can = target is null || target.Events.Contains(e);
            var chip = new ToggleButton
            {
                Classes = { "chip" }, IsChecked = can && f.Events.Contains(e), IsEnabled = can && !filesOnly, Margin = new Thickness(0, 0, 8, 8),
                Content = Ui.Col(2, Ui.Text(I18n.T($"v16.event.{e}"), "strong"), Ui.Text("hook " + e, "tiny muted")),
                Padding = new Thickness(12, 8),
            };
            ToolTip.SetTip(chip, can ? I18n.T($"v16.event.{e}.tip") : I18n.T("v16.event.missing", ("engine", target?.Title ?? "")));
            chip.IsCheckedChanged += (_, _) => { if (chip.IsChecked == true) f.Events.Add(ev); else f.Events.Remove(ev); };
            events.Children.Add(chip);
        }
        left.Children.Add(Question(4, I18n.T("v16.form.q4"), filesOnly ? I18n.T("v16.form.q4.files") : I18n.T("v16.form.q4.text"), events));

        // 5. Автор, версия, цвет
        var author = new TextBox { Text = f.Author, Watermark = I18n.T("v16.form.author"), MaxLength = 60, Width = 240 };
        var version = new TextBox { Text = f.Version, Watermark = "1.0.0", MaxLength = 20, Width = 110 };
        author.TextChanged += (_, _) => f.Author = author.Text ?? "";
        version.TextChanged += (_, _) => f.Version = version.Text ?? "";
        var colors = Ui.Row(8);
        foreach (var (cid, hex) in XProjects.Colors)
        {
            var c = cid;
            var dot = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Hex(hex), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                BorderBrush = f.Color == cid ? Ui.Res("Text") : Brushes.Transparent, BorderThickness = new Thickness(2),
            };
            StoreKit.OnClick(dot, () => { Keep(name, about); f.Color = c; Build(); });
            colors.Children.Add(dot);
        }
        left.Children.Add(Question(5, I18n.T("v16.form.q5"), I18n.T("v16.form.q5.text"),
            Ui.Col(12, Ui.Row(10, Labeled(I18n.T("v16.form.author"), author), Labeled(I18n.T("v16.form.version"), version)), Labeled(I18n.T("v16.form.color"), colors))));

        // Справа — будущий проект и кнопка.
        var right = Summary(def, bridge, target, filesOnly, name, about);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,360"), ColumnSpacing = 22 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        name.TextChanged += (_, _) => { f.Name = name.Text ?? ""; };
        about.TextChanged += (_, _) => { f.About = about.Text ?? ""; };
        return grid;
    }

    static void Keep(TextBox name, TextBox about) { _form.Name = name.Text ?? ""; _form.About = about.Text ?? ""; }

    async Task PickFolder(TextBox name, TextBox about)
    {
        Keep(name, about);
        if (MainWindow.Current is not { } w || await w.PickFolder(I18n.T("v16.form.folder")) is not { } dir) return;
        _form.Folder = dir;
        _form.Game = AppState.Games.FirstOrDefault(g => g.Path is { } p && string.Equals(Path.GetFullPath(p).TrimEnd('\\', '/'), Path.GetFullPath(dir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))?.Def.Id;
        Build();
    }

    static Control Labeled(string label, Control c) => Ui.Col(6, Ui.Text(label, "small muted"), c);

    /// <summary>Вопрос анкеты: номер в рамке, вопрос, пояснение и поле.</summary>
    static Control Question(int n, string title, string text, Control field)
    {
        var num = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(4), BorderBrush = HubLook.Violet, BorderThickness = new Thickness(1.5),
            Child = new TextBlock { Text = n.ToString(), FontFamily = HubLook.Mono, FontWeight = FontWeight.Bold, Foreground = HubLook.Violet, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var head = Ui.Row(12, num, Ui.Col(2, Ui.Text(title, "h3"), Ui.Text(text, "small muted", wrap: true)));
        return HubLook.Frame(Ui.Col(14, head, field), new Thickness(20, 18));
    }

    /// <summary>Карточка моста: движок, уверенность, улики, загрузчик и путь (код или файлы).</summary>
    internal static Control BridgeCard(MX.BridgeReport? r, bool noPath)
    {
        if (r is null)
            return new Border
            {
                Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(4), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1),
                Child = noPath ? Ui.Text(I18n.T("v16.bridge.noPath"), "small muted", wrap: true)
                    : Ui.Row(10, new ProgressBar { IsIndeterminate = true, Width = 80, VerticalAlignment = VerticalAlignment.Center }, Ui.Text(I18n.T("v16.bridge.scanning"), "small muted")),
            };
        var target = MX.Bridge.TargetOf(r);
        var color = r.AntiCheat is not null ? Ui.Res("Bad") : r.Code ? Ui.Res("Good") : Ui.Res("Warn");
        var route = r.Code
            ? I18n.T("v16.bridge.code", ("lang", XStudio.Lang(target!.Language)), ("loader", r.Loader))
            : I18n.T("v16.bridge.files");
        var head = new WrapPanel();
        foreach (var part in new Control[] { Ui.Icon(Icons.Layers, 16, HubLook.Cyan), Ui.Text(I18n.T("v16.bridge.title"), "strong"),
            Gx.Tag(r.Title, Ui.Res("Surface3")), Gx.Tag(route, color is ISolidColorBrush c ? new SolidColorBrush(Color.FromArgb(40, c.Color.R, c.Color.G, c.Color.B)) : null, color) })
        {
            part.Margin = new Thickness(0, 0, 8, 4);
            part.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(part);
        }
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = r.Confidence, Height = 4, MinHeight = 4, MinWidth = 0, Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = HubLook.Cyan };
        var sure = Ui.Row(10, Ui.Text(I18n.T("v16.bridge.sure", ("n", r.Confidence)), "small muted"), bar);
        var clues = new WrapPanel();
        foreach (var e in r.Evidence.Take(6))
        {
            var t = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), Margin = new Thickness(0, 0, 6, 6), Background = Ui.Res("Surface2"), Child = HubLook.MonoText(e, 10.5) };
            clues.Children.Add(t);
        }
        var dot = Ui.Dot(r.LoaderReady ? Ui.Res("Good") : Ui.Res("Warn"));
        dot.Margin = new Thickness(0, 0, 8, 0);
        var loader = new DockPanel { Children = { Dockd(dot),
            Ui.Text(r.LoaderReady ? I18n.T("v16.bridge.loader.ok", ("loader", r.Loader)) : I18n.T("v16.bridge.loader.no", ("loader", r.Loader)), "small", wrap: true) } };
        var col = Ui.Col(10, head, sure, clues);
        if (r.Code) col.Children.Add(loader);
        if (r.Hint is { } hint) col.Children.Add(Ui.Text(BridgeHint(r), "small muted", wrap: true));
        return new Border { Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(4), BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), Background = HubLook.Raised, Child = col };
    }

    /// <summary>Подсказка моста по-русски (у ModScript X консольного — по-английски, здесь — из строк программы).</summary>
    internal static string BridgeHint(MX.BridgeReport r)
    {
        var key = r.AntiCheat is not null ? "v16.bridge.hint.anticheat" : $"v16.bridge.hint.{r.Engine}{(r.LoaderReady ? ".ok" : "")}";
        if (I18n.Has(key)) return I18n.T(key, ("ac", r.AntiCheat ?? ""));
        if (I18n.Has($"v16.bridge.hint.{r.Engine}")) return I18n.T($"v16.bridge.hint.{r.Engine}");
        return r.Hint ?? "";
    }

    Control Summary(GameDef? def, MX.BridgeReport? bridge, MX.Target? target, bool filesOnly, TextBox name, TextBox about)
    {
        var f = _form;
        var hex = XProjects.Hex(f.Color);
        var title = new TextBlock { Text = f.Name.Trim() == "" ? I18n.T("v16.form.name.hint") : f.Name.Trim(), FontFamily = Look.Display, FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        name.TextChanged += (_, _) => title.Text = (name.Text ?? "").Trim() == "" ? I18n.T("v16.form.name.hint") : name.Text!.Trim();
        Control art = def is not null ? Ui.GameImage(def, 480) : new Border { Background = HubLook.Raised, Child = Ui.Icon(Icons.Code, 34, Ui.Res("Faint")) };
        var stripe = new Border { Height = 4, Background = Ui.Hex(hex) };
        var top = new Border { Height = 120, ClipToBounds = true, CornerRadius = new CornerRadius(4, 4, 0, 0), Child = new Panel { Children = { art, new Border { Background = Gx.ShadeUp(0.1, 200) } } } };
        var rows = Ui.Col(6);
        void Row(string k, string v) => rows.Children.Add(new DockPanel { Children = { Dockd(HubLook.MonoText(k, 10.5)), new TextBlock { Text = v, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis } } });
        Row(I18n.T("v16.sum.game"), def?.Name ?? (f.Folder is { } fd ? Path.GetFileName(fd.TrimEnd('\\', '/')) : I18n.T("v16.sum.noGame")));
        Row(I18n.T("v16.sum.engine"), bridge?.Title ?? target?.Title ?? "—");
        Row(I18n.T("v16.sum.lang"), filesOnly ? I18n.T("v16.sum.files") : target is not null ? $"ModScript X → {XStudio.Lang(target.Language)}" : "—");
        Row(I18n.T("v16.sum.kind"), I18n.T($"v16.kind.{f.Kind}"));
        Row(I18n.T("v16.sum.version"), f.Version.Trim() == "" ? "1.0.0" : f.Version.Trim());
        var files = Ui.Col(3, HubLook.MonoText(filesOnly ? "main.mls  ·  files/" : "main.modx  ·  modscript.json  ·  README.md", 10.5));
        var body = Ui.Col(12, HubLook.Eyebrow(I18n.T("v16.sum.title"), HubLook.Violet), title, rows, new Border { Height = 1, Background = HubLook.Edge }, files);
        body.Margin = new Thickness(16, 14);

        var go = Ui.Button(f.Busy ? I18n.T("v16.form.creating") : filesOnly ? I18n.T("v16.form.create.files") : I18n.T("v16.form.create"), () => Create(name, about, def, bridge, target, filesOnly), "primary", filesOnly ? Icons.FilePlus : Icons.Code);
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        go.HorizontalContentAlignment = HorizontalAlignment.Center;
        go.MinHeight = 46;
        go.IsEnabled = !f.Busy && (target is not null || filesOnly);
        // 18.0: «Отмена» и «Импорт» убраны — уйти можно пунктом слева, импорт — в «Других способах» под анкетой.
        var steps = Ui.Col(6);
        foreach (var (n, key) in new[] { (1, "v16.form.flow.1"), (2, "v16.form.flow.2"), (3, "v16.form.flow.3") })
            steps.Children.Add(Ui.Row(8, HubLook.MonoText($"0{n}", 10.5, HubLook.Cyan), Ui.Text(I18n.T(key), "small muted")));

        var card = new Border { Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = Ui.Col(0, stripe, top, body) };
        var col = Ui.Col(12, card, go, steps);
        col.VerticalAlignment = VerticalAlignment.Top;
        return col;
    }

    static Control Dockd(Control c) { DockPanel.SetDock(c, Dock.Left); return c; }

    void Create(TextBox name, TextBox about, GameDef? def, MX.BridgeReport? bridge, MX.Target? target, bool filesOnly)
    {
        Keep(name, about);
        var f = _form;
        var finalName = f.Name.Trim() == "" ? I18n.T("v16.form.defaultName") : f.Name.Trim();
        if (filesOnly)
        {
            // Нет кода для движка — классический ModScript (файлы, конфиги, ini) в знакомом мастере.
            ShowNewProject(new NewProjectState { Name = finalName, Game = def?.Id });
            return;
        }
        if (target is null) return;
        f.Busy = true;
        try
        {
            var p = XProjects.Create(new XForm(finalName, f.About, def?.Id ?? "", f.Folder ?? FormPath(), f.Kind, bridge?.Engine ?? target.Id, target.Id,
                MX.ModxDoc.Events.Where(f.Events.Contains).ToList(), f.Author, f.Version, f.Color));
            if (def is not null) Settings.Data["lastGame"] = def.Id;
            Settings.Save();
            _form = new FormState();
            MainWindow.Current?.Navigate(() => new XStudio(p.Id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MainWindow.Current?.Toast(e.Message, bad: true);
        }
        finally { f.Busy = false; }
    }
}
