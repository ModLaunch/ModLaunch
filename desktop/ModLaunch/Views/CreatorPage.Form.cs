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
/// 23.0: «Новый мод» — анкета по шагам: один вопрос на экране, сверху видно, где вы, справа собирается карточка
/// будущего мода (такой же она будет в «Моих модах»). Первый вопрос — что создаём: мод с кодом, замену файлов
/// (текстуры, звуки, модели, переводы), настройки и баланс или сборку модов. Creator Hub — для всех, кто делает
/// что-то для игр, а не только для программистов. Код — среда ModScript с готовым кодом по ответам; остальное —
/// проект правок файлов с подсказками, который открывается в той же среде.
/// </summary>
public sealed partial class CreatorPage
{
    sealed class FormState
    {
        /// <summary>Что создаём: code, files, config, pack.</summary>
        public string What = "code";
        public int Step;
        public string Name = "";
        public string About = "";
        public string? Game;
        public string? Folder;
        public string Kind = "gameplay";
        public HashSet<string> Events = ["start", "tick"];
        public string Author = Templates.DefaultAuthor();
        public string Version = "1.0.0";
        public string Color = "amber";
        /// <summary>Движок вручную — когда игру не выбрали (только код, без игры).</summary>
        public string ManualTarget = "unity";
        public bool More;
        public bool Busy;
    }

    /// <summary>Что можно создать: id, значок.</summary>
    internal static readonly (string Id, string Icon)[] Whats =
    [
        ("code", Icons.Code), ("world", Icons.Cube), ("files", Icons.Image), ("config", Icons.Settings), ("pack", Icons.Package),
    ];

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

    // ---------------------------------------------------------------- шаги анкеты

    /// <summary>Шаги для выбранного «что»: у мода с кодом — ещё «что делает».</summary>
    static string[] FormSteps(string what) => what switch { "code" => ["what", "game", "does", "name"], "world" => ["what", "game"], _ => ["what", "game", "name"] };

    /// <summary>Что известно об игре и пути мода для текущих ответов.</summary>
    sealed record FormFacts(GameDef? Def, string? Path, MX.BridgeReport? Bridge, MX.Target? Target, bool FilesOnly)
    {
        /// <summary>Можно идти дальше с шага «Игра»: для файлов нужна игра; для кода — движок (найденный или выбранный).</summary>
        public bool GameReady(string what) => what switch
        {
            "code" => Target is not null || FilesOnly,
            // Карта и мир — конструктор внутри игры: нужна Unity-игра с BepInEx.
            "world" => Path is not null && Features.DevMode.Unsupported(Path) is null,
            _ => Def is not null || Path is not null,
        };
    }

    FormFacts Facts()
    {
        var f = _form;
        var def = f.Game is { } gid ? AppState.Games.FirstOrDefault(g => g.Def.Id == gid)?.Def : null;
        var path = FormPath();
        var bridge = BridgeFor(path, def);
        var target = bridge?.Code == true ? MX.Target.Find(bridge.TargetId!) : f.Game is null && f.Folder is null ? MX.Target.Find(f.ManualTarget) : null;
        return new FormFacts(def, path, bridge, target, bridge is not null && !bridge.Code);
    }

    Control Form()
    {
        var f = _form;
        var steps = FormSteps(f.What);
        f.Step = Math.Clamp(f.Step, 0, steps.Length - 1);
        var step = steps[f.Step];
        var facts = Facts();

        var (title, hint, body) = step switch
        {
            "what" => (I18n.T("v23.form.q.what"), I18n.T("v23.form.q.what.text"), WhatStep()),
            "game" => (I18n.T("v23.form.q.game"), I18n.T(f.What == "code" ? "v23.form.q.game.code" : "v23.form.q.game.text"), GameStep(facts)),
            "does" => (I18n.T("v16.form.q3"), I18n.T("v23.form.q.does.text"), DoesStep(facts)),
            _ => (I18n.T("v23.form.q.name"), I18n.T("v23.form.q.name.text"), NameStep()),
        };
        var question = Ui.Col(6,
            new TextBlock { Text = title, FontFamily = Look.Display, FontSize = 24, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = hint, FontSize = 13.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left });

        // Назад / Дальше (на последнем шаге — «Создать»).
        var last = f.Step == steps.Length - 1;
        var canGo = step != "game" || facts.GameReady(f.What);
        Button next;
        if (last)
        {
            next = f.What == "world"
                ? Ui.Button(I18n.T("v23.dev.play"), () => { if (AppState.Games.FirstOrDefault(x => x.Def.Id == facts.Def?.Id) is { } gs) DevLaunch.Start(gs); }, "primary", Icons.Play)
                : Ui.Button(f.Busy ? I18n.T("v16.form.creating") : I18n.T("v23.form.create"), () => Create(facts), "primary", Icons.Check);
            next.IsEnabled = !f.Busy && facts.GameReady(f.What);
        }
        else
        {
            next = Ui.Button(I18n.T("v23.form.next"), () => { f.Step++; Build(); }, "primary", Icons.ChevronRight);
            next.IsEnabled = canGo;
        }
        next.Padding = new Thickness(20, 10);
        var nav = new DockPanel();
        DockPanel.SetDock(next, Dock.Right);
        nav.Children.Add(next);
        if (f.Step > 0)
        {
            var back = Ui.Button(I18n.T("v23.form.back"), () => { f.Step--; Build(); }, "ghost", Icons.ChevronLeft);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            nav.Children.Add(back);
        }
        else nav.Children.Add(new Border());

        var left = Ui.Col(22, Stepper(steps), question, body, new Border { Height = 1, Background = HubLook.Edge }, nav);
        var right = Preview(facts);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300"), ColumnSpacing = 32 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Где вы в анкете: пройденные шаги — галочкой (можно вернуться), текущий — янтарём, будущие — тихо.</summary>
    Control Stepper(string[] steps)
    {
        var f = _form;
        var row = new WrapPanel();
        for (var i = 0; i < steps.Length; i++)
        {
            var at = i;
            var done = i < f.Step;
            var now = i == f.Step;
            var circle = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                Background = now ? HubLook.Amber : done ? HubLook.Raised : Brushes.Transparent,
                BorderBrush = now ? HubLook.Amber : HubLook.Edge, BorderThickness = new Thickness(done || now ? 0 : 1.5),
                Child = done ? Ui.Icon(Icons.Check, 13, Ui.Res("Text"))
                    : new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = now ? HubLook.OnAmber : Ui.Res("Faint"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var label = new TextBlock
            {
                Text = I18n.T("v23.form.step." + steps[i]), FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = now ? FontWeight.SemiBold : FontWeight.Normal, Foreground = now ? Ui.Res("Text") : done ? Ui.Res("Muted") : Ui.Res("Faint"),
            };
            var item = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 2), Child = Ui.Row(8, circle, label) };
            if (done)
            {
                item.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                StoreKit.OnClick(item, () => { f.Step = at; Build(); });
            }
            row.Children.Add(item);
            if (i < steps.Length - 1)
                row.Children.Add(new Border { Width = 28, Height = 1.5, Background = done ? HubLook.Amber : HubLook.Edge, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        return row;
    }

    /// <summary>Большая карточка выбора: значок, название, пояснение; выбранная — янтарной рамкой.</summary>
    static Button Choice(string icon, string title, string text, bool on, Action pick)
    {
        var mark = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top,
            Background = on ? HubLook.Amber : HubLook.Raised, Child = Ui.Icon(icon, 19, on ? HubLook.OnAmber : Ui.Res("Muted")),
        };
        var words = Ui.Col(3, new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = text, FontSize = 12.5, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, LineHeight = 18 });
        var dock = new DockPanel();
        mark.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(mark, Dock.Left);
        dock.Children.Add(mark);
        dock.Children.Add(words);
        var b = new Button
        {
            Classes = { "tile" }, Padding = new Thickness(16, 14), Margin = new Thickness(0, 0, 12, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Content = dock,
        };
        if (on) { b.BorderBrush = HubLook.Amber; b.BorderThickness = new Thickness(2); }
        b.Click += (_, _) => pick();
        return b;
    }

    Control WhatStep()
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var (id, icon) in Whats)
        {
            var what = id;
            grid.Children.Add(Choice(icon, I18n.T("v23.what." + id), I18n.T($"v23.what.{id}.text"), _form.What == id, () => { _form.What = what; _form.Step = 1; Build(); }));
        }
        return grid;
    }

    Control GameStep(FormFacts facts)
    {
        var f = _form;
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var tiles = new WrapPanel();
        foreach (var g in games.Take(24))
        {
            var id = g.Def.Id;
            var on = f.Folder is null && f.Game == id;
            var cover = new Border { Width = 92, Height = 124, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.GameImage(g.Def, 200, art: Images.Art.Cover) };
            var frame = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(2), BorderThickness = new Thickness(2), BorderBrush = on ? HubLook.Amber : Brushes.Transparent, Child = cover };
            var label = new TextBlock
            {
                Text = g.Def.ShortName, FontSize = 12, Width = 96, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0),
                FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, Foreground = on ? Ui.Res("Text") : Ui.Res("Muted"),
            };
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(2), Margin = new Thickness(0, 0, 10, 10), Content = Ui.Col(0, frame, label) };
            ToolTip.SetTip(b, g.Def.Name);
            b.Click += (_, _) => { f.Game = id; f.Folder = null; Build(); };
            tiles.Children.Add(b);
        }
        var col = Ui.Col(14);
        if (games.Count == 0) col.Children.Add(Ui.Text(I18n.T("v23.form.noGames"), "muted", wrap: true));
        else col.Children.Add(tiles);
        var folder = Ui.Button(f.Folder is null ? I18n.T("v16.form.folder") : Path.GetFileName(f.Folder.TrimEnd('\\', '/')), () => _ = PickFolder(), f.Folder is null ? "" : "primary", Icons.Folder);
        var others = Ui.Row(8, folder);
        if (f.What == "code")
        {
            var none = Ui.Button(I18n.T("v16.form.noGame"), () => { f.Game = null; f.Folder = null; Build(); }, f.Game is null && f.Folder is null ? "primary" : "ghost", Icons.Code);
            ToolTip.SetTip(none, I18n.T("v16.form.noGame.tip"));
            others.Children.Add(none);
        }
        col.Children.Add(others);
        if (f.What == "code" && f.Game is null && f.Folder is null)
        {
            var engine = new ComboBox { MinWidth = 260 };
            foreach (var t in MX.Target.All) engine.Items.Add(new ComboBoxItem { Content = $"{t.Title}, {XStudio.Lang(t.Language)}", Tag = t.Id });
            engine.SelectedItem = engine.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == f.ManualTarget);
            engine.SelectionChanged += (_, _) => { if (engine.SelectedItem is ComboBoxItem { Tag: string id }) { f.ManualTarget = id; Build(); } };
            col.Children.Add(Ui.Row(10, Ui.Text(I18n.T("v16.form.engine"), "small muted"), engine));
        }
        else if (Route(facts) is { } route) col.Children.Add(route);
        return col;
    }

    /// <summary>Как мод попадёт в игру — одной-двумя фразами, без улик и процентов (они — в среде, в панели «Мост»).</summary>
    Control? Route(FormFacts facts)
    {
        var f = _form;
        if (facts.Def is null && facts.Path is null) return null;
        string text;
        IBrush dot;
        if (f.What == "world")
        {
            var why = Features.DevMode.Unsupported(facts.Path);
            text = why is null ? I18n.T(Features.DevMode.Key(facts.Path, "route")) : I18n.T(why, ("ac", ""));
            dot = why is null ? Ui.Res("Good") : Ui.Res("Warn");
        }
        else if (f.What != "code") { text = I18n.T("v23.form.route.files"); dot = Ui.Res("Good"); }
        else if (facts.Bridge is null) { text = facts.Path is null && !Program.Demo ? I18n.T("v16.bridge.noPath") : I18n.T("v16.bridge.scanning"); dot = Ui.Res("Faint"); }
        else if (facts.Bridge.AntiCheat is not null) { text = BridgeHint(facts.Bridge); dot = Ui.Res("Bad"); }
        else if (facts.Bridge.Code)
        {
            var lang = XStudio.Lang(MX.Bridge.TargetOf(facts.Bridge)!.Language);
            text = I18n.T("v23.form.route.code", ("lang", lang), ("loader", facts.Bridge.Loader)) + " "
                + I18n.T(facts.Bridge.LoaderReady ? "v16.bridge.loader.ok" : "v16.bridge.loader.no", ("loader", facts.Bridge.Loader));
            dot = facts.Bridge.LoaderReady ? Ui.Res("Good") : Ui.Res("Warn");
        }
        else { text = I18n.T("v23.form.route.nocode"); dot = Ui.Res("Warn"); }
        var d = Ui.Dot(dot);
        d.Margin = new Thickness(0, 6, 10, 0);
        d.VerticalAlignment = VerticalAlignment.Top;
        var dock = new DockPanel();
        DockPanel.SetDock(d, Dock.Left);
        dock.Children.Add(d);
        dock.Children.Add(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
        return new Border { Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12), Child = dock };
    }

    Control DoesStep(FormFacts facts)
    {
        var f = _form;
        var kinds = new UniformGrid { Columns = 3 };
        foreach (var k in XProjects.Kinds)
        {
            var kind = k;
            var icon = k switch { "economy" => Icons.Bag, "messages" => Icons.Bell, "difficulty" => Icons.Flame, "cheats" => Icons.Zap, "empty" => Icons.FilePlus, _ => Icons.Gamepad };
            kinds.Children.Add(Choice(icon, I18n.T($"v16.kind.{k}"), I18n.T($"v16.kind.{k}.text"), f.Kind == k, () => { f.Kind = kind; Build(); }));
        }
        var col = Ui.Col(18, kinds);
        if (facts.FilesOnly) return col;
        // Когда срабатывает — простыми словами, без «hook»; события, которых у движка нет, выключены с подсказкой.
        var events = new WrapPanel();
        foreach (var e in MX.ModxDoc.Events)
        {
            var ev = e;
            var can = facts.Target is null || facts.Target.Events.Contains(e);
            var chip = new ToggleButton
            {
                Classes = { "chip" }, IsChecked = can && f.Events.Contains(e), IsEnabled = can, Margin = new Thickness(0, 0, 8, 8),
                Content = I18n.T($"v16.event.{e}"), Padding = new Thickness(14, 7),
            };
            ToolTip.SetTip(chip, can ? I18n.T($"v16.event.{e}.tip") : I18n.T("v16.event.missing", ("engine", facts.Target?.Title ?? "")));
            chip.IsCheckedChanged += (_, _) => { if (chip.IsChecked == true) f.Events.Add(ev); else f.Events.Remove(ev); };
            events.Children.Add(chip);
        }
        col.Children.Add(Ui.Col(10, new TextBlock { Text = I18n.T("v16.form.q4"), FontSize = 15, FontWeight = FontWeight.SemiBold }, events));
        return col;
    }

    Control NameStep()
    {
        var f = _form;
        var name = new TextBox { Text = f.Name, Watermark = I18n.T("v23.form.name.hint." + f.What), MaxLength = 80, FontSize = 18, MinHeight = 46, VerticalContentAlignment = VerticalAlignment.Center };
        var about = new TextBox { Text = f.About, Watermark = I18n.T("v16.form.about.hint"), MaxLength = 300, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72 };
        name.TextChanged += (_, _) => { f.Name = name.Text ?? ""; NameChanged?.Invoke(); };
        about.TextChanged += (_, _) => f.About = about.Text ?? "";
        name.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => name.Focus());
        var col = Ui.Col(12, name, about);

        // Ещё: автор, версия и цвет ярлыка — свёрнуто, у большинства всё и так верно.
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
            StoreKit.OnClick(dot, () => { f.Color = c; f.More = true; Build(); });
            colors.Children.Add(dot);
        }
        var more = new Expander
        {
            Header = I18n.T("v23.form.more"), IsExpanded = f.More, HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Ui.Col(14, Ui.Row(12, Labeled(I18n.T("v16.form.author"), author), Labeled(I18n.T("v16.form.version"), version)), Labeled(I18n.T("v16.form.color"), colors)),
        };
        more.Expanded += (_, _) => f.More = true;
        more.Collapsed += (_, _) => f.More = false;
        col.Children.Add(more);
        return col;
    }

    /// <summary>Перерисовать название в карточке справа, пока его печатают (без перестройки страницы).</summary>
    event Action? NameChanged;

    static Control Labeled(string label, Control c) => Ui.Col(6, Ui.Text(label, "small muted"), c);

    /// <summary>Карточка будущего мода — такой же она встанет в «Мои моды»: обложка игры и ярлык с названием.</summary>
    Control Preview(FormFacts facts)
    {
        var f = _form;
        var color = Color.Parse(XProjects.Hex(f.Color));
        string Title() => f.Name.Trim() == "" ? I18n.T("v23.form.name.hint." + f.What) : f.Name.Trim();
        var plateText = new TextBlock { Text = Title(), FontSize = 15, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = IsLightColor(color) ? HubLook.OnAmber : Brushes.White };
        NameChanged += () => plateText.Text = Title();
        var plate = new Border
        {
            Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(4), Padding = new Thickness(9, 5), Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Child = plateText,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 6, Color = Color.FromArgb(110, 0, 0, 0) }),
        };
        var whatIcon = Whats.First(w => w.Id == f.What).Icon;
        Control art = facts.Def is not null ? Ui.GameImage(facts.Def, 600) : new Border { Background = HubLook.Raised, Child = Ui.Icon(whatIcon, 34, Ui.Res("Faint")) };
        var scrim = new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.45, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(150, 0, 0, 0), 1) },
            },
        };
        var top = new Border { Height = 170, ClipToBounds = true, Child = new Panel { Children = { art, scrim, plate } } };
        var game = facts.Def?.Name ?? (f.Folder is { } fd ? Path.GetFileName(fd.TrimEnd('\\', '/')) : I18n.T("v16.sum.noGame"));
        var what = I18n.T("v23.what." + f.What);
        if (f.What == "code" && FormSteps(f.What).ToList().IndexOf("does") <= f.Step) what += ", " + I18n.T($"v16.kind.{f.Kind}").ToLowerInvariant();
        var foot = Ui.Col(3,
            new TextBlock { Text = game, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = what, FontSize = 12, Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "v" + (f.Version.Trim() == "" ? "1.0.0" : f.Version.Trim()) + "  " + (f.Author.Trim() == "" ? "" : f.Author.Trim()), FontSize = 12, Foreground = Ui.Res("Faint") });
        foot.Margin = new Thickness(14, 12, 14, 14);
        var card = new Border { Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.Col(0, top, foot) };
        var note = new TextBlock { Text = I18n.T("v23.form.preview"), FontSize = 12, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap };
        var col = Ui.Col(10, card, note);
        col.VerticalAlignment = VerticalAlignment.Top;
        return col;
    }

    async Task PickFolder()
    {
        if (MainWindow.Current is not { } w || await w.PickFolder(I18n.T("v16.form.folder")) is not { } dir) return;
        _form.Folder = dir;
        _form.Game = AppState.Games.FirstOrDefault(g => g.Path is { } p && string.Equals(Path.GetFullPath(p).TrimEnd('\\', '/'), Path.GetFullPath(dir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))?.Def.Id;
        Build();
    }

    void Create(FormFacts facts)
    {
        var f = _form;
        var finalName = f.Name.Trim() == "" ? I18n.T("v16.form.defaultName") : f.Name.Trim();
        // Мод с кодом для игры без моста кода — тоже проект правок файлов: честнее, чем код, который не встанет.
        if (f.What != "code" || facts.FilesOnly)
        {
            f.Busy = true;
            try
            {
                var p = Projects.Create(finalName, FilesProjectCode(f.What == "code" ? "files" : f.What, finalName, f.About, f.Author, f.Version, facts.Def?.Id));
                if (facts.Def is not null) Settings.Data["lastGame"] = facts.Def.Id;
                Settings.Save();
                _form = new FormState();
                OpenInStudio(p);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); }
            finally { f.Busy = false; }
            return;
        }
        if (facts.Target is not { } target) return;
        f.Busy = true;
        try
        {
            var p = XProjects.Create(new XForm(finalName, f.About, facts.Def?.Id ?? "", f.Folder ?? FormPath(), f.Kind, facts.Bridge?.Engine ?? target.Id, target.Id,
                MX.ModxDoc.Events.Where(f.Events.Contains).ToList(), f.Author, f.Version, f.Color));
            if (facts.Def is not null) Settings.Data["lastGame"] = facts.Def.Id;
            Settings.Save();
            _form = new FormState();
            MainWindow.Current?.Navigate(() => new XStudio(p.Id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); }
        finally { f.Busy = false; }
    }

    /// <summary>
    /// Начальный код проекта правок файлов для «что»: заголовок мода и подсказка с примером строки (закомментирован —
    /// пустой проект собирается без ошибок и ничего не ломает).
    /// </summary>
    internal static string FilesProjectCode(string what, string name, string about, string author, string version, string? game)
    {
        static string Q(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        var lines = new List<string>();
        foreach (var l in I18n.T("v23.tpl." + what).Split('\n')) lines.Add("# " + l.Trim());
        lines.Add("");
        lines.Add($"mod \"{Q(name)}\"");
        lines.Add($"version {(version.Trim() == "" ? "1.0.0" : Q(version.Trim()).Replace(" ", ""))}");
        if (author.Trim() != "") lines.Add($"author \"{Q(author.Trim())}\"");
        if (about.Trim() != "") lines.Add($"about \"{Q(about.Trim())}\"");
        if (game is not null) lines.Add($"game {game}");
        lines.Add("");
        lines.Add(what switch
        {
            "config" => "# config \"BepInEx/config/SomeMod.cfg\" [General] Speed = 2",
            "pack" => "# needs Author-ModName Author-OtherMod",
            _ => "# copy \"my-texture.png\" to \"Textures/my-texture.png\"",
        });
        return string.Join("\n", lines) + "\n";
    }

    [SelfTest]
    static string FormMakesEveryKindOfProject()
    {
        foreach (var (id, _) in Whats.Where(w => w.Id is not ("code" or "world")))
        {
            var code = FilesProjectCode(id, "Test \"quoted\" name", "line one\nline two", "Me", "1.2.0", "valheim");
            var r = ModScript.Compile(code);
            if (!r.Ok) throw new Exception($"{id}: {string.Join("; ", r.Diags.Select(d => d.ToString()))}");
            if (r.Name != "Test \"quoted\" name" || r.Game != "valheim" || r.Version != "1.2.0") throw new Exception($"{id}: meta {r.Name} {r.Game} {r.Version}");
        }
        if (string.Join(",", FormSteps("code")) != "what,game,does,name" || string.Join(",", FormSteps("files")) != "what,game,name" || string.Join(",", FormSteps("world")) != "what,game") throw new Exception("steps");
        return $"{Whats.Length} kinds of things to create; file projects compile with their metadata";
    }
}

public sealed partial class CreatorPage
{
    /// <summary>Для снимков: анкета на нужном шаге с ответами.</summary>
    internal static CreatorPage DemoForm(string what, int step, string name = "", string about = "")
    {
        var page = NewForm();
        _form.What = what;
        _form.Step = step;
        _form.Name = name;
        _form.About = about;
        return page;
    }
}
