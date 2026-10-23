using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 16.0: среда ModScript X — своя страница проекта, а не вкладка в Hub. Открывается анкетой «Создать проект»
/// (или плиткой проекта). Сверху — проект, игра и мост, кнопки «Запустить» (F5) и «В игру» (F6); слева — панель
/// действий: файлы проекта со структурой, мост к игре (движок, улики, куда лягут файлы, какие события есть),
/// блоки (клик — вставить), шпаргалка языка и настройки проекта; в середине — вкладки редактора; снизу —
/// «Проблемы», «Вывод» песочницы, «Код движка» и «Журнал моста»; строка состояния — в цвете проекта.
/// Открытые файлы помнятся по проекту: перестройка страницы и уход в Hub не теряют правок и истории.
/// Пришла на смену «Студии» 15.0 (ModStudio): всё, что она умела, есть здесь.
/// </summary>
public sealed partial class XStudio : Page
{
    sealed class Doc
    {
        public string? Path;
        public string Title = "";
        public string Saved = "";
        public ScriptEditor Editor = null!;
        public List<Diag> Diags = [];
        public bool Modx => Path is null || Path.EndsWith(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase);
        public bool Dirty => Editor.Text != Saved;
        /// <summary>18.0: за сколько миллисекунд прошла последняя проверка на лету — в строке состояния.</summary>
        public double CheckMs;
        /// <summary>19.0: находки понимания кода (опечатки, лишние переменные…) — с местом и исправлением.</summary>
        public List<ModxInsight.Issue> Lints = [];
    }

    /// <summary>Что открыто в проекте: живёт дольше страницы.</summary>
    sealed class Session
    {
        public readonly List<Doc> Docs = [];
        public Doc? Active;
        public string Side = "files";
        public string Panel = "problems";
        public bool Watch;
        public string Vars = "gold = 100\nhealth = 80\nspeed = 5\nenemy_damage = 10";
        public int Ticks = 3;
        public MX.Sandbox.Result? Run;
        public Verdict? Verdict;
        public readonly List<(DateTime At, bool Ok, string Text)> Log = [];
        public (int Line, int Col) Caret = (1, 1);
        /// <summary>19.0: запись последнего запуска (отладчик), шаг на экране и файл записи.</summary>
        public MX.Sandbox.Trace? Trace;
        public int Frame;
        public bool Debugging;
        public string? TraceFile;
        /// <summary>19.0: ссылки (Shift+F12) или найденное поиском по проекту.</summary>
        public List<(string? Path, int Line, int Col, int Length, string Preview, bool Write)> Found = [];
        public string FoundTitle = "";
        public string SearchText = "";
    }

    static readonly Dictionary<string, Session> Sessions = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Страница на экране: события редакторов (они живут в сессии) идут ей.</summary>
    static XStudio? _live;
    static readonly FontFamily Mono = HubLook.Mono;

    readonly string _id;
    XProject? _p;
    readonly Session _s;
    bool _busy;
    DispatcherTimer? _analyze;
    readonly Border _top = new(), _activity = new(), _side = new(), _tabs = new(), _editorHost = new(), _panelTabs = new(), _panelBody = new(), _status = new();

    public XStudio(string projectId, string? side = null, string? panel = null)
    {
        _id = projectId;
        _p = XProjects.Get(projectId);
        if (!Sessions.TryGetValue(projectId, out var s)) Sessions[projectId] = s = new Session();
        _s = s;
        if (side is not null) _s.Side = side;
        if (panel is not null) _s.Panel = panel;
        if (_p is not null)
        {
            if (_s.Docs.Count == 0) Open(_p.Main, focus: false);
            Settings.Data["xLast"] = projectId;
        }
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => _live = this;
        DetachedFromVisualTree += (_, _) =>
        {
            if (_live == this) _live = null;
            // Уход со страницы — несохранённое сохраняется (как в редакторе проектов Hub).
            foreach (var d in _s.Docs.Where(d => d.Dirty && d.Path is not null)) Save(d, quiet: true);
        };
    }

    /// <summary>Вход в среду: последний проект, иначе анкета нового.</summary>
    public static Page Entry() =>
        Settings.Data.Str("xLast") is { } id && XProjects.Get(id) is not null ? new XStudio(id) : CreatorPage.NewForm();

    public override string Title => _p?.Name ?? "ModScript X";
    public override bool Workspace => true;
    public override bool SameScreenAs(Page? previous) => HubNav.IsHub(previous);
    public override string? Accent => _p is null ? null : XProjects.Hex(_p.Color);
    public override string SearchHint => I18n.T("v16.x.search");
    public override void Search(string text) => _s.Active?.Editor.Find();

    public override IEnumerable<(string Text, Action? Open)> Crumbs =>
    [
        ("Creator Hub", () => MainWindow.Current?.Navigate(() => new CreatorPage())),
        (I18n.T("v18.hub.projects"), () => MainWindow.Current?.Navigate(() => new CreatorPage())),
        (_p?.Name ?? "ModScript X", null),
    ];

    GameState? Game => _p is { Game: { Length: > 0 } id } ? AppState.Games.FirstOrDefault(g => g.Def.Id == id) : null;

    string? GamePath =>
        _p?.GamePath is { } own && Directory.Exists(own) ? own
        : Game is { Status: Detect.Found, Path: { } p } && Directory.Exists(p) ? p : null;

    MX.BridgeReport? Bridge => CreatorPage.BridgeFor(GamePath, Game?.Def);

    /// <summary>Перевод: движок из файла (engine), иначе из проекта, иначе по мосту, иначе Ren'Py.</summary>
    MX.Target TargetFor(MX.ModxDoc? doc) =>
        (doc?.Engine is { } e ? MX.Target.Find(e) : null)
        ?? (_p?.Target is { } t ? MX.Target.Find(t) : null)
        ?? (Bridge is { } b ? MX.Bridge.TargetOf(b) : null)
        ?? MX.Target.Find("renpy")!;

    IBrush Color => Ui.Hex(XProjects.Hex(_p?.Color ?? "violet"));

    /// <summary>Язык перевода для людей: csharp → C#, gdscript → GDScript.</summary>
    internal static string Lang(string id) => id switch
    {
        "csharp" => "C#", "javascript" => "JavaScript", "gdscript" => "GDScript", "python" => "Python", "lua" => "Lua", "renpy" => "Ren'Py", _ => id,
    };

    // ---------------------------------------------------------------- раскладка

    public override void Build()
    {
        if (_p is null)
        {
            Content = new Border
            {
                Padding = new Thickness(48), Child = Ui.Col(14, Ui.Text(I18n.T("v16.x.missing"), "h2"), Ui.Text(I18n.T("v16.x.missing.text"), "muted", wrap: true),
                    Ui.Row(10, Ui.Button(I18n.T("v13.hub.projects"), () => MainWindow.Current?.Navigate(() => new CreatorPage()), "primary", Icons.Folder),
                        Ui.Button(I18n.T("v16.form.title"), () => CreatorPage.OpenForm(), "", Icons.Plus))),
            };
            return;
        }
        if (Program.Screenshot && _s.Panel == "output" && _s.Run is null) Run();
        // Области живут дольше раскладки: перестройка (смена языка, настройки, мост догрузился) снимает их со старой.
        foreach (var region in new Control[] { _top, _activity, _side, _tabs, _editorHost, _panelTabs, _panelBody, _status, _overlay }) Detach(region);
        _top.Background = HubLook.Panel;
        _top.BorderBrush = HubLook.Edge;
        _top.BorderThickness = new Thickness(0, 0, 0, 1);
        _top.Padding = new Thickness(10, 8, 12, 8);
        _activity.Background = HubLook.Panel;
        _activity.BorderBrush = HubLook.Edge;
        _activity.BorderThickness = new Thickness(0, 0, 1, 0);
        _activity.Width = 52;
        _side.Background = HubLook.Panel;
        _side.BorderBrush = HubLook.Edge;
        _side.BorderThickness = new Thickness(0, 0, 1, 0);
        _side.Width = 290;

        // 18.0: редактор обведён фирменной полосой — среда ModScript X сразу отличается от обычного редактора.
        var editor = new Border
        {
            Background = HubLook.Panel, BorderBrush = HubLook.Premium(140), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true,
            Child = new DockPanel { Children = { Docked(_tabs, Dock.Top), _editorHost } },
        };
        var panel = new Border
        {
            Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true,
            Child = new DockPanel { Children = { Docked(_panelTabs, Dock.Top), _panelBody } },
        };
        var work = new Grid { RowDefinitions = new RowDefinitions("*,6,230"), Margin = new Thickness(10, 10, 10, 8) };
        work.Children.Add(editor);
        var split = new GridSplitter { Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(split, 1);
        work.Children.Add(split);
        Grid.SetRow(panel, 2);
        work.Children.Add(panel);

        var center = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        center.Children.Add(_activity);
        Grid.SetColumn(_side, 1);
        center.Children.Add(_side);
        Grid.SetColumn(work, 2);
        center.Children.Add(work);

        var line = new Border { Height = 2, Background = HubLook.Premium(), IsHitTestVisible = false };
        var main = new DockPanel { Children = { Docked(_top, Dock.Top), Docked(line, Dock.Top), Docked(_status, Dock.Bottom), center } };
        var root = new Panel { Classes = { "hub-space" }, Children = { new HubLook.Blueprint(), main, _overlay } };
        HubLook.Scope(root);
        Content = root;
        RenderAll();
    }

    static Control Docked(Control c, Dock where) { DockPanel.SetDock(c, where); return c; }

    static void Detach(Control c)
    {
        switch (c.Parent)
        {
            case Panel p: p.Children.Remove(c); break;
            case Decorator d: d.Child = null; break;
            case ContentControl cc: cc.Content = null; break;
        }
    }

    void RenderAll()
    {
        RenderTop();
        RenderActivity();
        RenderSide();
        RenderTabs();
        RenderEditor();
        RenderPanel();
        RenderStatus();
    }

    // ---------------------------------------------------------------- шапка

    void RenderTop()
    {
        var p = _p!;
        // 18.0: среда живёт внутри «Моих проектов» — слева путь назад туда, знак X и проект (цвет проекта — точкой).
        var back = Ui.Button(I18n.T("v18.hub.projects"), () => MainWindow.Current?.Navigate(() => new CreatorPage()), "ghost", Icons.Back, I18n.T("v18.x.back"));
        var tile = CreatorPage.XMark(34);
        var name = Ui.Col(1,
            new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 },
            Ui.Row(6, Ui.Dot(Color, 6), HubLook.MonoText($"v{p.Version}  ·  MODSCRIPT X", 10.5, HubLook.Violet)));
        var t = TargetFor(ActiveDoc());
        var b = Bridge;
        Control art = Game?.Def is { } def
            ? new Border { Width = 24, Height = 32, CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = Ui.GameImage(def, 96, art: Images.Art.Cover) }
            : new Border { Width = 24, Height = 32, CornerRadius = new CornerRadius(3), Background = HubLook.Raised, Child = Ui.Icon(Icons.Folder, 13, Ui.Res("Faint")) };
        var gameName = Game?.Def.Name ?? (p.GamePath is { } gp ? System.IO.Path.GetFileName(gp.TrimEnd('\\', '/')) : I18n.T("v16.sum.noGame"));
        var route = b is { Code: false } ? I18n.T("v16.sum.files") : $"{b?.Title ?? t.Title} → {Lang(t.Language)}";
        var dot = Ui.Dot(b?.AntiCheat is not null ? Ui.Res("Bad") : b is null ? Ui.Res("Faint") : b.Code && b.LoaderReady ? Ui.Res("Good") : Ui.Res("Warn"), 7);
        var chip = new Button
        {
            Classes = { "ghost" }, Padding = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center,
            Content = Ui.Row(10, art, Ui.Col(1, new TextBlock { Text = gameName, FontSize = 12.5, FontWeight = FontWeight.SemiBold, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis },
                Ui.Row(6, dot, HubLook.MonoText(route, 10.5)))),
        };
        ToolTip.SetTip(chip, I18n.T("v16.x.bridge.tip"));
        chip.Click += (_, _) => Side("bridge");
        var left = Ui.Row(10, back, tile, name, new Border { Width = 1, Height = 30, Background = HubLook.Edge, Margin = new Thickness(6, 0) }, chip);
        foreach (var c in left.Children) c.VerticalAlignment = VerticalAlignment.Center;

        var run = Ui.Button(I18n.T("v15.ms.run"), Run, "", Icons.Play, I18n.T("v15.ms.run.tip") + "  (F5)");
        var inject = Ui.Button(_busy ? I18n.T("v15.ms.injecting") : I18n.T("v16.x.inject"), () => _ = InjectNow(), "primary", Icons.Zap, I18n.T("v15.ms.inject.tip") + "  (F6)");
        inject.IsEnabled = !_busy;
        // 18.0: на виду — две главные кнопки (F5, F6); снять мод, «Следить», «Играть» и «Сохранить» — в «⋯» и на клавишах.
        var more = Ui.Button("", () => { }, "icon ghost", Icons.More, I18n.T("top.more"));
        more.Flyout = Ctx.Menu(
            Ctx.Item(I18n.T("v15.ms.play"), Icons.Gamepad, () => _ = PlayWithInject()),
            Ctx.Item(I18n.T("v15.ms.eject.tip"), Icons.Undo, () => _ = EjectNow(), gesture: "Shift+F6"),
            Ctx.Item((_s.Watch ? "✓  " : "") + I18n.T("v15.ms.watch"), Icons.Eye, () => { _s.Watch = !_s.Watch; RenderStatus(); RenderTop(); }),
            Ctx.Item(I18n.T("cr.save"), Icons.Save, () => { if (_s.Active is { } d) Save(d); }, gesture: "Ctrl+S"),
            "-",
            Ctx.Item(I18n.T("v15.ms.new"), Icons.FilePlus, NewFile, gesture: "Ctrl+N"),
            Ctx.Item(I18n.T("v15.ms.open"), Icons.Folder, () => _ = OpenFile(), gesture: "Ctrl+O"),
            Ctx.Item(I18n.T("v15.ms.find"), Icons.Search, () => _s.Active?.Editor.Find(), gesture: "Ctrl+F"),
            Ctx.Item(I18n.T("v15.ms.replace"), Icons.Edit, () => _s.Active?.Editor.Find(true), gesture: "Ctrl+H"),
            "-",
            Ctx.Item(I18n.T("v11.lx.check"), Icons.Shield, Check),
            Ctx.Item(I18n.T("v15.ms.attach"), Icons.Layers, () => Attach()),
            Ctx.Item(I18n.T("v11.lx.sell"), Icons.Bag, Sell),
            "-",
            Ctx.Item(I18n.T("v16.x.side.settings"), Icons.Settings, () => Side("settings")),
            Ctx.Item(I18n.T("v18.hub.scripts"), Icons.Shield, () => MainWindow.Current?.Navigate(() => new CreatorPage("scripts"))),
            Ctx.Folder(I18n.T("v15.ms.reveal"), p.Dir));
        var right = Ui.Row(8, run, inject, more);
        foreach (var c in right.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(left);
        _top.Child = bar;
    }

    // ---------------------------------------------------------------- панель действий слева

    static readonly (string Id, string Icon, string Key)[] Sides =
    [
        ("files", Icons.Folder, "v16.x.side.files"), ("bridge", Icons.Layers, "v16.x.side.bridge"), ("blocks", Icons.Wand, "v16.x.side.blocks"),
        ("docs", Icons.Book, "v16.x.side.docs"), ("history", Icons.Clock, "v19.side.history"), ("settings", Icons.Settings, "v16.x.side.settings"),
    ];

    void Side(string id)
    {
        // Повторный клик по горящему — спрятать панель (больше места коду).
        _s.Side = _s.Side == id && id != "bridge" ? "" : id;
        RenderActivity();
        RenderSide();
    }

    void RenderActivity()
    {
        var col = new StackPanel { Spacing = 4, Margin = new Thickness(0, 10) };
        foreach (var (id, icon, key) in Sides)
        {
            var side = id;
            var on = _s.Side == id;
            var b = new Button
            {
                Classes = { "ghost" }, Width = 40, Height = 40, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center,
                Background = on ? HubLook.Raised : Brushes.Transparent, Content = Ui.Icon(icon, 19, on ? Color : Ui.Res("Muted")),
            };
            ToolTip.SetTip(b, I18n.T(key));
            ToolTip.SetPlacement(b, PlacementMode.Right);
            b.Click += (_, _) => Side(side);
            var mark = new Border { Width = 3, Height = 22, CornerRadius = new CornerRadius(2), Background = on ? Color : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(new Panel { Children = { b, mark } });
        }
        var console = new Button { Classes = { "ghost" }, Width = 40, Height = 40, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, Content = Ui.Icon(Icons.Code, 18, Ui.Res("Faint")) };
        ToolTip.SetTip(console, I18n.T("v16.x.console"));
        ToolTip.SetPlacement(console, PlacementMode.Right);
        console.Click += (_, _) => MainWindow.Current?.Navigate(() => new CreatorPage("console"));
        console.Margin = new Thickness(0, 0, 0, 10);
        var dock = new DockPanel { Children = { Docked(console, Dock.Bottom), col } };
        _activity.Child = dock;
    }

    void RenderSide()
    {
        _side.IsVisible = _s.Side != "";
        if (!_side.IsVisible) return;
        var body = _s.Side switch
        {
            "bridge" => BridgeSide(),
            "blocks" => BlocksSide(),
            "docs" => DocsSide(),
            "history" => HistorySide(),
            "settings" => SettingsSide(),
            _ => FilesSide(),
        };
        var title = Sides.FirstOrDefault(x => x.Id == _s.Side).Key is { } key ? I18n.T(key) : "";
        var head = new Border { Padding = new Thickness(16, 14, 12, 10), Child = HubLook.Eyebrow(title, Color) };
        _side.Child = new DockPanel { Children = { Docked(head, Dock.Top), new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } };
    }

    static Control Caption(string text) => new Border { Padding = new Thickness(8, 14, 8, 6), Child = HubLook.MonoText(text.ToUpper(I18n.Culture), 10, Ui.Res("Faint")) };

    static Button Row(string icon, string text, bool active, Action open, IBrush? mark = null, string? right = null, IBrush? iconColor = null)
    {
        var line = new DockPanel();
        if (right is not null)
        {
            var r = HubLook.MonoText(right, 10.5, Ui.Res("Faint"));
            DockPanel.SetDock(r, Dock.Right);
            line.Children.Add(r);
        }
        if (mark is not null)
        {
            var dot = Ui.Dot(mark, 7);
            dot.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(dot, Dock.Right);
            line.Children.Add(dot);
        }
        line.Children.Add(Ui.Row(8, Ui.Icon(icon, 14, iconColor ?? Ui.Res("Muted")),
            new TextBlock { Text = text, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
        var b = new Button
        {
            Classes = { "ghost" }, Padding = new Thickness(8, 5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = active ? HubLook.Raised : Brushes.Transparent, Content = line,
        };
        b.Click += (_, _) => open();
        return b;
    }

    Control FilesSide()
    {
        var p = _p!;
        var col = new StackPanel { Spacing = 1, Margin = new Thickness(8, 0, 8, 12) };
        var add = Ui.Button(I18n.T("v15.ms.new"), NewFile, "ghost", Icons.FilePlus);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        List<string> files = [];
        try
        {
            files = Directory.EnumerateFiles(p.Dir)
                .Where(f => f.EndsWith(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => System.IO.Path.GetFileName(f) == System.IO.Path.GetFileName(p.Main) ? 0 : f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? 2 : 1).ThenBy(f => f).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        col.Children.Add(Caption(I18n.T("v16.x.files.project")));
        foreach (var f in files)
        {
            var path = f;
            var doc = _s.Docs.FirstOrDefault(d => Same(d.Path, f));
            var mark = doc is null ? null : doc.Dirty ? Ui.Res("Warn") : doc.Diags.Any(x => !x.Warning) ? Ui.Res("Bad") : null;
            var main = Same(f, p.Main);
            col.Children.Add(Row(f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? Icons.Book : Icons.Code, System.IO.Path.GetFileName(f), Same(_s.Active?.Path, f), () => Open(path), mark,
                main ? I18n.T("v16.x.files.main") : null, main ? Color : null));
        }
        col.Children.Add(add);

        if (_s.Active is { Modx: true } a)
        {
            var outline = Outline(a.Editor.Text ?? "");
            if (outline.Count > 0)
            {
                col.Children.Add(Caption(I18n.T("v15.ms.outline")));
                foreach (var (line, kind, name) in outline)
                {
                    var at = line;
                    var icon = kind switch { "hook" => Icons.Zap, "fn" => Icons.Code, "native" => Icons.Layers, _ => Icons.Package };
                    col.Children.Add(Row(icon, (kind == "mod" ? "" : kind + " ") + name, false, () => a.Editor.GoToLine(at), right: at.ToString(),
                        iconColor: kind == "hook" ? HubLook.Cyan : kind == "fn" ? HubLook.Violet : null));
                }
            }
        }

        List<string> loose = [];
        try { loose = Directory.EnumerateFiles(CreatorPage.ScriptsDir(), "*" + MX.ModxDoc.Extension).OrderByDescending(File.GetLastWriteTimeUtc).Take(10).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (loose.Count > 0)
        {
            col.Children.Add(Caption(I18n.T("v16.x.files.other")));
            foreach (var f in loose)
            {
                var path = f;
                col.Children.Add(Row(Icons.Code, System.IO.Path.GetFileName(f), Same(_s.Active?.Path, f), () => Open(path)));
            }
        }
        return col;
    }

    static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    Control BridgeSide()
    {
        var p = _p!;
        var col = new StackPanel { Spacing = 12, Margin = new Thickness(14, 0, 14, 14) };
        var path = GamePath;
        var b = Bridge;
        col.Children.Add(Ui.Text(I18n.T("v16.x.bridge.lead"), "small muted", wrap: true));

        // Игра: из найденных или своя папка.
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var pick = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = I18n.T("v11.lx.pickGame") };
        foreach (var g in games) pick.Items.Add(new ComboBoxItem { Content = g.Def.Name, Tag = g.Def.Id });
        pick.SelectedItem = pick.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == p.Game);
        pick.SelectionChanged += (_, _) =>
        {
            if (pick.SelectedItem is not ComboBoxItem { Tag: string id } || id == p.Game) return;
            p.Game = id;
            p.GamePath = null;
            Retarget();
        };
        var folder = Ui.Button(I18n.T("v16.form.folder"), () => _ = PickFolder(), "ghost", Icons.Folder);
        col.Children.Add(Ui.Col(8, pick, folder));
        if (path is not null) col.Children.Add(new TextBlock { Text = path, FontSize = 11, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap });

        col.Children.Add(CreatorPage.BridgeCard(b, path is null && !Program.Demo));

        // Куда лягут файлы и какие события есть у движка.
        var t = TargetFor(ActiveDoc());
        if (b is not { Code: false })
        {
            var where = Ui.Col(4, HubLook.MonoText(I18n.T("v16.x.bridge.files").ToUpper(I18n.Culture), 10, Ui.Res("Faint")));
            try
            {
                if (ActiveDoc() is { Ok: true } doc)
                    foreach (var f in t.Emit(doc, path).Take(6))
                        where.Children.Add(new TextBlock { Text = f.Path.Replace('\\', '/'), FontFamily = Mono, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Text") });
                else where.Children.Add(Ui.Text(I18n.T("v11.lx.fix"), "small muted", wrap: true));
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException) { where.Children.Add(Ui.Text(e.Message, "small muted", wrap: true)); }
            col.Children.Add(where);

            var events = new WrapPanel();
            foreach (var e in MX.ModxDoc.Events)
            {
                var has = t.Events.Contains(e);
                var tag = new Border
                {
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 3), Margin = new Thickness(0, 0, 6, 6), BorderBrush = has ? HubLook.Cyan : HubLook.Edge, BorderThickness = new Thickness(1),
                    Child = Ui.Row(5, Ui.Dot(has ? Ui.Res("Good") : Ui.Res("Faint"), 6), HubLook.MonoText(e, 11, has ? Ui.Res("Text") : Ui.Res("Faint"))),
                };
                ToolTip.SetTip(tag, has ? I18n.T($"v16.event.{e}.tip") : I18n.T("v16.event.missing", ("engine", t.Title)));
                events.Children.Add(tag);
            }
            col.Children.Add(Ui.Col(6, HubLook.MonoText(I18n.T("v16.x.bridge.events", ("engine", t.Title)).ToUpper(I18n.Culture), 10, Ui.Res("Faint")), events));
        }
        else
        {
            var classic = Ui.Button(I18n.T("v16.form.create.files"), () => CreatorPage.ShowNewProject(new CreatorPage.NewProjectState { Name = p.Name, Game = Game?.Def.Id }), "", Icons.FilePlus);
            col.Children.Add(classic);
        }
        if (path is not null)
            col.Children.Add(Ui.Button(I18n.T("v16.x.bridge.rescan"), () => { CreatorPage.Rescan(path); Build(); }, "ghost", Icons.Refresh));
        return col;
    }

    /// <summary>Игра или папка сменилась: мост, перевод и строка engine в коде — по новой игре.</summary>
    void Retarget()
    {
        var p = _p!;
        var b = CreatorPage.BridgeFor(GamePath, Game?.Def);
        if (b is not null)
        {
            p.Engine = b.Engine;
            if (MX.Bridge.TargetOf(b) is { } t)
            {
                p.Target = t.Id;
                foreach (var d in _s.Docs.Where(d => d.Modx)) d.Editor.Text = SetHeader(d.Editor.Text ?? "", "engine", t.Id);
            }
        }
        p.Save();
        Build();
    }

    async Task PickFolder()
    {
        if (MainWindow.Current is not { } w || await w.PickFolder(I18n.T("v16.form.folder")) is not { } dir) return;
        _p!.GamePath = dir;
        _p.Game = AppState.Games.FirstOrDefault(g => Same(g.Path, dir))?.Def.Id ?? "";
        Retarget();
    }

    /// <summary>Блоки: готовые куски кода, клик — вставить там, где курсор.</summary>
    Control BlocksSide()
    {
        var col = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0, 8, 14) };
        col.Children.Add(new Border { Padding = new Thickness(8, 0, 8, 4), Child = Ui.Text(I18n.T("v16.x.blocks.lead"), "small muted", wrap: true) });
        var t = TargetFor(ActiveDoc());
        foreach (var (group, items) in Blocks())
        {
            col.Children.Add(Caption(I18n.T("v16.x.blocks." + group)));
            foreach (var (key, code, ev) in items)
            {
                var text = code;
                var can = ev is null || t.Events.Contains(ev);
                var b = new Button
                {
                    Classes = { "ghost" }, Padding = new Thickness(8, 6), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = can,
                    Content = Ui.Col(2, Ui.Text(I18n.T(key), "small strong"),
                        new TextBlock { Text = code.Split('\n')[0], FontFamily = Mono, FontSize = 11, Foreground = HubLook.Cyan, TextTrimming = TextTrimming.CharacterEllipsis }),
                };
                ToolTip.SetTip(b, can ? code : I18n.T("v16.event.missing", ("engine", t.Title)));
                b.Click += (_, _) => InsertBlock(text);
                col.Children.Add(b);
            }
        }
        return col;
    }

    internal static IEnumerable<(string Group, (string Key, string Code, string? Event)[] Items)> Blocks() =>
    [
        ("events", [
            ("v16.event.ready", "hook ready {\n    log \"mod loaded\"\n}\n", "ready"),
            ("v16.event.start", "hook start {\n    say \"Hello from ModScript X\"\n}\n", "start"),
            ("v16.event.load", "hook load {\n    log \"save loaded\"\n}\n", "load"),
            ("v16.event.tick", "hook tick {\n    add ticks 1\n}\n", "tick"),
        ]),
        ("data", [
            ("v16.x.b.let", "let bonus = 100\n", null),
            ("v16.x.b.set", "set gold = gold + bonus\n", null),
            ("v16.x.b.add", "add score 1\n", null),
        ]),
        ("logic", [
            ("v16.x.b.if", "if health < 20 {\n    say \"Low health!\"\n} else {\n    log \"ok\"\n}\n", null),
            ("v16.x.b.for", "for i in 1..3 {\n    add gold 10\n}\n", null),
            ("v16.x.b.while", "while gold < 100 {\n    add gold 10\n}\n", null),
            ("v16.x.b.fn", "fn double(value) {\n    return value * 2\n}\n", null),
        ]),
        ("talk", [
            ("v16.x.b.say", "say \"Gold: {gold}\"\n", null),
            ("v16.x.b.log", "log \"debug: {gold}\"\n", null),
        ]),
        ("tricks", [
            ("v16.x.b.timer", "add timer 1\nif timer >= 600 {\n    set timer = 0\n    say \"Another 10 seconds\"\n}\n", null),
            ("v16.x.b.chance", "if chance(10) {\n    add gold rand(1, 50)\n    say \"Lucky!\"\n}\n", null),
            ("v16.x.b.clamp", "set health = clamp(health, 0, 100)\n", null),
        ]),
    ];

    void InsertBlock(string code)
    {
        if (_s.Active is not { Modx: true } d) { NewFile(); d = _s.Active!; }
        d.Editor.Insert(code);
        d.Editor.FocusText();
    }

    Control DocsSide()
    {
        var col = new StackPanel { Spacing = 10, Margin = new Thickness(14, 0, 14, 14) };
        col.Children.Add(Ui.Text(I18n.T("v16.x.docs.lead"), "small muted", wrap: true));
        foreach (var (key, code) in new[]
        {
            ("v16.x.docs.head", "mod \"My mod\"\nversion 1.0.0\nauthor \"Me\"\nengine unity"),
            ("v16.x.docs.hooks", "hook start {\n    say \"Hi\"\n}"),
            ("v16.x.docs.vars", "let gold = 100\nset gold = gold * 2\nadd gold 5\ngold += 1"),
            ("v16.x.docs.logic", "if gold > 10 { … } else { … }\nfor i in 1..5 { … }\nwhile alive { … }"),
            ("v16.x.docs.fn", "fn reward(wave) {\n    return wave * 50\n}"),
            ("v16.x.docs.text", "say \"Gold: {gold}\"\nlog \"x = {x}\""),
            ("v16.x.docs.native", "native csharp {\n    // C# / lua / python / js / gdscript\n}"),
        })
        {
            var box = new Border
            {
                Background = HubLook.Raised, CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8),
                Child = new SelectableTextBlock { Text = code, FontFamily = Mono, FontSize = 11.5, LineHeight = 17, TextWrapping = TextWrapping.Wrap },
            };
            col.Children.Add(Ui.Col(6, Ui.Text(I18n.T(key), "small strong"), box));
        }
        var builtins = new WrapPanel();
        foreach (var f in MX.ModxDoc.Builtins)
            builtins.Children.Add(new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), Margin = new Thickness(0, 0, 5, 5), Background = HubLook.Raised, Child = HubLook.MonoText(f + "()", 10.5, Ui.Res("Text")) });
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v16.x.docs.builtins"), "small strong"), builtins));
        // 19.0: клавиши среды — вместо кнопки на старую страницу языка (её больше нет).
        var keys = Ui.Col(3);
        foreach (var (k, key) in new[]
        {
            ("Ctrl+Space", "v15.ms.key.suggest"), ("F12 · Ctrl+клик", "v19.cmd.definition"), ("Shift+F12", "v19.cmd.references"), ("F2", "v19.cmd.rename"),
            ("Shift+Alt+F", "v19.cmd.format"), ("F9", "v19.cmd.breakpoint"), ("F10 · Shift+F10", "v19.docs.step"), ("Ctrl+P", "v19.key.palette"), ("Ctrl+Shift+F", "v19.cmd.search"),
        })
            keys.Children.Add(Ui.Row(8, new TextBlock { Text = k, FontFamily = Mono, FontSize = 11, Width = 104, Foreground = HubLook.Cyan }, Ui.Text(I18n.T(key), "tiny muted", wrap: true)));
        col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v19.docs.keys"), "small strong"), keys));
        return col;
    }

    Control SettingsSide()
    {
        var p = _p!;
        var col = new StackPanel { Spacing = 12, Margin = new Thickness(14, 0, 14, 14) };
        var name = new TextBox { Text = p.Name, MaxLength = 80 };
        var about = new TextBox { Text = p.About, MaxLength = 300, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        var version = new TextBox { Text = p.Version, MaxLength = 20 };
        var author = new TextBox { Text = p.Author, MaxLength = 60 };
        var color = p.Color;
        var colors = new WrapPanel();
        var dots = new List<(string Id, Border Dot)>();
        foreach (var (cid, hex) in XProjects.Colors)
        {
            var c = cid;
            var dot = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 6), Background = Ui.Hex(hex), Cursor = new Cursor(StandardCursorType.Hand),
                BorderBrush = cid == color ? Ui.Res("Text") : Brushes.Transparent, BorderThickness = new Thickness(2),
            };
            dots.Add((cid, dot));
            StoreKit.OnClick(dot, () => { color = c; foreach (var (id, d) in dots) d.BorderBrush = id == c ? Ui.Res("Text") : Brushes.Transparent; });
            colors.Children.Add(dot);
        }
        col.Children.Add(Field(I18n.T("v16.form.q1"), name));
        col.Children.Add(Field(I18n.T("v16.form.about"), about));
        col.Children.Add(Field(I18n.T("v16.form.version"), version));
        col.Children.Add(Field(I18n.T("v16.form.author"), author));
        col.Children.Add(Field(I18n.T("v16.form.color"), colors));
        var save = Ui.Button(I18n.T("cr.save"), () =>
        {
            p.Name = (name.Text ?? "").Trim() is { Length: > 0 } n ? n : p.Name;
            p.About = (about.Text ?? "").Trim();
            p.Version = (version.Text ?? "").Trim() is { Length: > 0 } v ? v : p.Version;
            p.Author = (author.Text ?? "").Trim();
            p.Color = color;
            try { p.Save(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
            // Шапка мода в main.modx — по настройкам (открыт — правим в редакторе, иначе на диске).
            var main = _s.Docs.FirstOrDefault(d => Same(d.Path, p.Main));
            var text = main?.Editor.Text ?? (File.Exists(p.Main) ? File.ReadAllText(p.Main) : "");
            text = SetHeader(SetHeader(SetHeader(SetHeader(text, "mod", Q(p.Name)), "version", p.Version), "author", p.Author == "" ? null : Q(p.Author)), "about", p.About == "" ? null : Q(p.About));
            if (main is not null) { main.Editor.Text = text; Save(main, quiet: true); }
            else File.WriteAllText(p.Main, text);
            MainWindow.Current?.Toast(I18n.T("cr.saved"));
            MainWindow.Current?.RenderCrumbs();
            Build();
        }, "primary", Icons.Save);
        col.Children.Add(save);
        col.Children.Add(new Border { Height = 1, Background = HubLook.Edge, Margin = new Thickness(0, 6) });
        col.Children.Add(Ui.Button(I18n.T("v15.ms.reveal"), () => Actions.OpenFolder(p.Dir), "ghost", Icons.Folder));
        col.Children.Add(Ui.Button(I18n.T("v16.x.delete"), DeleteProject, "danger", Icons.Trash));
        return col;
    }

    static Control Field(string label, Control c) => Ui.Col(6, Ui.Text(label, "small muted"), c);

    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "'").Replace("\n", " ") + "\"";

    /// <summary>Строка шапки мода (mod, version, author, about, engine): заменить, дописать после mod или убрать (value null).</summary>
    internal static string SetHeader(string text, string key, string? value)
    {
        var re = new Regex(@"^[ \t]*" + key + @"[ \t]+[^\r\n]*$", RegexOptions.Multiline);
        if (re.IsMatch(text)) return value is null ? Regex.Replace(re.Replace(text, "\u0001", 1), "\u0001\r?\n?", "") : re.Replace(text, key + " " + value, 1);
        if (value is null) return text;
        var mod = new Regex(@"^[ \t]*mod[ \t]+[^\r\n]*$", RegexOptions.Multiline).Match(text);
        if (key == "mod" || !mod.Success) return key + " " + value + "\n" + text;
        // Порядок шапки: mod, version, author, about, engine — новая строка встаёт после последней из предыдущих.
        var order = new[] { "mod", "version", "author", "about", "engine" };
        var at = mod.Index + mod.Length;
        foreach (var k in order.TakeWhile(k => k != key))
            if (new Regex(@"^[ \t]*" + k + @"[ \t]+[^\r\n]*$", RegexOptions.Multiline).Match(text) is { Success: true } m && m.Index + m.Length > at) at = m.Index + m.Length;
        return text[..at] + "\n" + key + " " + value + text[at..];
    }

    void DeleteProject()
    {
        var w = MainWindow.Current;
        if (w is null || _p is null) return;
        var p = _p;
        w.Dialog(I18n.T("v16.x.delete.title", ("name", p.Name)), Ui.Text(I18n.T("v16.x.delete.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("ctx.delete"), () =>
            {
                w.CloseDialog();
                foreach (var d in _s.Docs) d.Saved = d.Editor.Text ?? "";
                Sessions.Remove(_id);
                XProjects.Delete(p);
                w.Navigate(() => new CreatorPage());
            }, "danger", Icons.Trash));
    }

    // ---------------------------------------------------------------- файлы и вкладки

    MX.ModxDoc? ActiveDoc() => _s.Active is { Modx: true } d ? MX.ModxDoc.Parse(d.Editor.Text ?? "") : null;

    Doc Add(string? path, string title, string text, string saved)
    {
        var d = new Doc { Path = path, Title = title, Saved = saved };
        d.Editor = new ScriptEditor { Modx = d.Modx };
        d.Editor.Text = text;
        d.Editor.TextChanged += () => { if (_live is { } live && d == live._s.Active) live.Schedule(); };
        d.Editor.SaveRequested += () => _live?.Save(d);
        d.Editor.CaretMoved += (line, col) => { if (_live is { } live && d == live._s.Active) { live._s.Caret = (line, col); live.RenderStatus(); } };
        // 19.0: понимание кода и отладчик — командам нужна страница на экране, значения берутся из сессии проекта.
        d.Editor.IdeCommand += cmd => _live?.Ide(cmd);
        d.Editor.DefinitionRequested += at => _live?.Definition(at);
        d.Editor.BreakpointsChanged += () => { if (_live is { _s.Panel: "debug" } live) live.RenderPanel(); };
        d.Editor.DebugValue = DebugValueOf;
        _s.Docs.Add(d);
        Analyze(d);
        return d;
    }

    void Open(string path, bool focus = true)
    {
        var full = System.IO.Path.GetFullPath(path);
        var doc = _s.Docs.FirstOrDefault(d => Same(d.Path, full));
        if (doc is null)
        {
            string text;
            try { text = File.ReadAllText(full); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
            doc = Add(full, System.IO.Path.GetFileName(full), text, text);
        }
        _s.Active = doc;
        if (focus) Activate(doc);
    }

    void Activate(Doc doc)
    {
        _s.Active = doc;
        if (!_s.Debugging) _s.Run = null;
        _s.Verdict = null;
        if (Content is null) return;
        RenderAll();
        Dispatcher.UIThread.Post(doc.Editor.FocusText, DispatcherPriority.Background);
    }

    void NewFile()
    {
        if (_p is null) return;
        var path = CreatorPage.FreeModxPath(_p.Dir, "script");
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        try { File.WriteAllText(path, $"mod {Q(_p.Name + " · " + name)}\nversion {_p.Version}\nengine {TargetFor(null).Id}\n\nhook start {{\n    log \"{name} ready\"\n}}\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
        Open(path);
    }

    async Task OpenFile()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var type = new FilePickerFileType("ModScript X (.modx)") { Patterns = ["*" + MX.ModxDoc.Extension] };
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, FileTypeFilter = [type] });
        foreach (var f in files) if (f.TryGetLocalPath() is { } p) Open(p);
    }

    bool Save(Doc d, bool quiet = false)
    {
        if (d.Path is null) return false;
        try
        {
            // 19.0: история файла — до первого сохранения кладём и то, что было на диске.
            if (d.Saved != "" && XHistory.List(d.Path).Count == 0) XHistory.Snap(d.Path, d.Saved, File.Exists(d.Path) ? File.GetLastWriteTimeUtc(d.Path) : null);
            File.WriteAllText(d.Path, d.Editor.Text ?? "");
            d.Saved = d.Editor.Text ?? "";
            XHistory.Snap(d.Path, d.Saved);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MainWindow.Current?.Toast(e.Message, bad: true);
            return false;
        }
        if (quiet || _live != this) return true;
        RenderSide();
        RenderTabs();
        RenderStatus();
        // «Следить»: каждое сохранение сразу ставит мод в игру заново.
        if (_s.Watch && d == _s.Active && d.Modx && GamePath is not null) _ = InjectNow(quiet: true);
        return true;
    }

    void Close(Doc d)
    {
        if (d.Dirty)
        {
            var w = MainWindow.Current;
            if (w is null) return;
            w.Dialog(I18n.T("v15.ms.close.title", ("file", d.Title)), Ui.Text(I18n.T("v15.ms.close.text"), "muted", wrap: true),
                Ui.Button(I18n.T("v15.ms.close.discard"), () => { w.CloseDialog(); d.Saved = d.Editor.Text ?? ""; Close(d); }, "ghost"),
                Ui.Button(I18n.T("cr.save"), () => { w.CloseDialog(); if (Save(d)) Close(d); }, "primary", Icons.Save));
            return;
        }
        var at = _s.Docs.IndexOf(d);
        _s.Docs.Remove(d);
        if (_s.Active == d) _s.Active = _s.Docs.Count == 0 ? null : _s.Docs[Math.Clamp(at, 0, _s.Docs.Count - 1)];
        RenderAll();
    }

    void RenderTabs()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var d in _s.Docs)
        {
            var doc = d;
            var active = d == _s.Active;
            var errors = d.Diags.Count(x => !x.Warning);
            var name = new TextBlock { Text = d.Title, FontFamily = Mono, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = active ? Ui.Res("Text") : Ui.Res("Muted") };
            var mark = d.Dirty ? Ui.Dot(Ui.Res("Warn"), 7) : errors > 0 ? Ui.Dot(Ui.Res("Bad"), 7) : Ui.Dot(Brushes.Transparent, 7);
            var close = new Button { Classes = { "icon", "ghost" }, Width = 22, Height = 22, Padding = new Thickness(0), Content = Ui.Icon(Icons.Close, 11, Ui.Res("Faint")), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(close, I18n.T("v15.ms.close") + "  (Ctrl+W)");
            close.Click += (_, e) => { e.Handled = true; Close(doc); };
            var tab = new Border
            {
                Padding = new Thickness(12, 8, 6, 8), Cursor = new Cursor(StandardCursorType.Hand),
                Background = active ? HubLook.Raised : Brushes.Transparent,
                BorderBrush = active ? Color : Brushes.Transparent, BorderThickness = new Thickness(0, 2, 0, 0),
                Child = Ui.Row(8, Ui.Icon(d.Modx ? Icons.Code : Icons.Book, 13, active ? Color : Ui.Res("Faint")), name, mark, close),
            };
            ToolTip.SetTip(tab, d.Path ?? I18n.T("v15.ms.unsaved"));
            tab.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(tab).Properties.IsMiddleButtonPressed) { Close(doc); return; }
                if (doc != _s.Active) Activate(doc);
            };
            row.Children.Add(tab);
        }
        _tabs.Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = row };
        _tabs.BorderBrush = HubLook.Edge;
        _tabs.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    void RenderEditor()
    {
        if (_s.Active is { } d)
        {
            if (d.Editor.Parent is Decorator old && old != _editorHost) old.Child = null;
            _editorHost.Child = d.Editor;
            return;
        }
        var keys = Ui.Col(6);
        foreach (var (k, key) in new[] { ("F5", "v15.ms.key.run"), ("F6", "v15.ms.key.inject"), ("F9 / F10 / F11", "v19.key.debug"), ("F12 / Shift+F12", "v19.key.nav"), ("F2", "v19.cmd.rename"), ("Ctrl+P", "v19.key.palette"), ("Ctrl+Space", "v15.ms.key.suggest"), ("F8", "v15.ms.key.next") })
            keys.Children.Add(Ui.Row(12, new TextBlock { Text = k, FontFamily = Mono, FontSize = 12, Width = 130, Foreground = HubLook.Cyan }, Ui.Text(I18n.T(key), "small muted")));
        _editorHost.Child = new Border
        {
            Padding = new Thickness(40, 36),
            Child = Ui.Col(16, Ui.Text(I18n.T("v15.ms.empty"), "h2"),
                Ui.Row(10, Ui.Button(I18n.T("v16.x.openMain"), () => Open(_p!.Main), "primary", Icons.Code), Ui.Button(I18n.T("v15.ms.new"), NewFile, "", Icons.FilePlus)),
                keys),
        };
    }

    /// <summary>Структура файла: имя мода, хуки, функции и native-блоки со строками.</summary>
    internal static List<(int Line, string Kind, string Name)> Outline(string text)
    {
        var list = new List<(int, string, string)>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], @"^\s*(hook|fn|native)\s+([\p{L}\w]+)(\s*\([^)]*\))?");
            if (m.Success) list.Add((i + 1, m.Groups[1].Value, m.Groups[2].Value + m.Groups[3].Value.Trim()));
            else if (Regex.Match(lines[i], @"^\s*mod\s+""([^""]*)""") is { Success: true } h) list.Add((i + 1, "mod", h.Groups[1].Value));
        }
        return list;
    }

    // ---------------------------------------------------------------- проверка на лету

    void Schedule()
    {
        _analyze?.Stop();
        _analyze = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _analyze.Tick += (_, _) =>
        {
            _analyze!.Stop();
            if (_s.Active is not { } d) return;
            Analyze(d);
            RenderTabs();
            if (_s.Side == "files") RenderSide();
            if (_s.Panel is "problems" or "code") RenderPanel();
            else RenderPanelTabs();
            RenderStatus();
        };
        _analyze.Start();
    }

    static void Analyze(Doc d)
    {
        if (!d.Modx) { d.Diags = []; return; }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var doc = MX.ModxDoc.Parse(d.Editor.Text ?? "");
        // 19.0: и понимание кода — опечатки, лишние переменные, аргументы, код после return.
        d.Lints = ModxInsight.Lint(d.Editor.Model);
        d.CheckMs = clock.Elapsed.TotalMilliseconds;
        d.Diags = [.. doc.Diags.OrderBy(x => x.Warning).ThenBy(x => x.Line)];
        d.Editor.MarkErrors(d.Diags.Where(x => !x.Warning && x.Line > 0).Select(x => x.Line));
        d.Editor.SetIssues(d.Diags.Where(x => x.Line > 0).Select(x => new ModxInsight.Issue(x.Line, 0, 0, CreatorPage.LxMsg(x), x.Warning)).Concat(d.Lints));
    }

    void NextProblem()
    {
        if (_s.Active is not { } d || d.Diags.Count + d.Lints.Count == 0) return;
        var lines = d.Diags.Select(x => x.Line).Concat(d.Lints.Select(x => x.Line)).Where(x => x > 0).Order().ToList();
        if (lines.Count == 0) return;
        var next = lines.Where(x => x > d.Editor.CaretLine).DefaultIfEmpty(lines[0]).First();
        d.Editor.GoToLine(Math.Max(1, next));
        _s.Panel = "problems";
        RenderPanel();
    }

    // ---------------------------------------------------------------- нижняя панель

    static readonly (string Id, string Key, string Icon)[] PanelTabs =
    [
        ("problems", "v15.ms.p.problems", Icons.Alert), ("output", "v15.ms.p.output", Icons.Play), ("debug", "v19.p.debug", Icons.Bug), ("refs", "v19.p.refs", Icons.Link),
        ("search", "v19.p.search", Icons.Search), ("code", "v15.ms.p.code", Icons.Code), ("inject", "v16.x.p.bridge", Icons.Layers),
    ];

    void RenderPanelTabs()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 4) };
        var problems = (_s.Active?.Diags.Count ?? 0) + (_s.Active?.Lints.Count ?? 0);
        foreach (var (id, key, icon) in PanelTabs)
        {
            var tab = id;
            var content = Ui.Row(6, Ui.Icon(icon, 13, _s.Panel == id ? Color : Ui.Res("Muted")), new TextBlock { Text = I18n.T(key), VerticalAlignment = VerticalAlignment.Center });
            if (id == "problems" && problems > 0) content.Children.Add(new Border { Classes = { "tab-badge" }, Child = Ui.Text(problems.ToString(), "tiny") });
            if (id == "debug" && _s.Debugging) content.Children.Add(Ui.Dot(new SolidColorBrush(Avalonia.Media.Color.Parse("#F5B83D")), 7));
            var b = new Button { Classes = { "tab" }, Content = content };
            if (_s.Panel == id) b.Classes.Add("active");
            b.Click += (_, _) => { _s.Panel = tab; RenderPanel(); };
            row.Children.Add(b);
        }
        _panelTabs.Child = row;
        _panelTabs.BorderBrush = HubLook.Edge;
        _panelTabs.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    void RenderPanel()
    {
        RenderPanelTabs();
        _panelBody.Child = _s.Panel switch
        {
            "output" => OutputPane(),
            "debug" => DebugPane(),
            "refs" => FoundPane(search: false),
            "search" => FoundPane(search: true),
            "code" => CodePane(),
            "inject" => InjectPane(),
            _ => ProblemsPane(),
        };
    }

    Control OutputPane()
    {
        var vars = new TextBox { Text = _s.Vars, AcceptsReturn = true, FontFamily = Mono, FontSize = 12, Height = 92, TextWrapping = TextWrapping.NoWrap, VerticalContentAlignment = VerticalAlignment.Top };
        vars.TextChanged += (_, _) => _s.Vars = vars.Text ?? "";
        var ticks = new NumericUpDown { Value = _s.Ticks, Minimum = 0, Maximum = 100, Increment = 1, FormatString = "0", Width = 110 };
        ticks.ValueChanged += (_, _) => _s.Ticks = (int)(ticks.Value ?? 3);
        var ticksRow = Ui.Row(8, Ui.Text(I18n.T("v11.lx.ticks"), "tiny muted"), ticks, Ui.Button(I18n.T("v15.ms.run"), Run, "primary", Icons.Play, "F5"));
        foreach (var c in ticksRow.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var left = Ui.Col(6, ticksRow, new TextBlock { Text = I18n.T("v11.lx.vars"), FontSize = 11, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis }, vars);
        left.Width = 370;

        var right = new StackPanel { Spacing = 4 };
        if (_s.Run is not { } run) right.Children.Add(Ui.Text(I18n.T("v15.ms.output.none"), "small muted", wrap: true));
        else
        {
            right.Children.Add(Ui.Row(8, Ui.Icon(run.Ok ? Icons.Check : Icons.Alert, 13, run.Ok ? Ui.Res("Good") : Ui.Res("Bad")),
                Ui.Text(I18n.T(run.Ok ? "v11.lx.ran" : "v11.lx.failed", ("steps", run.Steps)), "small", color: run.Ok ? Ui.Res("Good") : Ui.Res("Bad"))));
            var text = new StringBuilder();
            foreach (var o in run.Output) text.Append(o).Append('\n');
            foreach (var e in run.Errors) text.Append(e.Line > 0 ? $"! {I18n.T("v15.ms.line", ("n", e.Line))}: " : "! ").Append(CreatorPage.LxMsg(e)).Append('\n');
            right.Children.Add(new SelectableTextBlock { Text = text.ToString().TrimEnd(), FontFamily = Mono, FontSize = 12, LineHeight = 18, TextWrapping = TextWrapping.Wrap });
            if (run.Vars.Count > 0)
                right.Children.Add(new SelectableTextBlock
                {
                    Text = string.Join("   ", run.Vars.Select(kv => $"{kv.Key} = {MX.Sandbox.Show(kv.Value)}")), FontFamily = Mono, FontSize = 12,
                    Foreground = Ui.Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                });
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 18, Margin = new Thickness(12, 10) };
        grid.Children.Add(left);
        var scroll = new ScrollViewer { Content = right };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        return grid;
    }

    Control CodePane()
    {
        if (ActiveDoc() is not { } doc) return new Border { Padding = new Thickness(12, 10), Child = Ui.Text(I18n.T("v16.x.code.none"), "small muted") };
        var t = TargetFor(doc);
        var text = doc.Ok ? EngineCode(doc, t, null) : I18n.T("v11.lx.fix");
        var head = Ui.Row(8, Ui.Text($"{t.Title} · {Lang(t.Language)}", "small strong"), Ui.Text(I18n.T("v11.lx.code.hint"), "tiny muted"));
        var copy = Ui.Button("", () => _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text), "icon ghost", Icons.Copy, I18n.T("ctx.copy"));
        var bar = new DockPanel { Margin = new Thickness(12, 8, 8, 4) };
        DockPanel.SetDock(copy, Dock.Right);
        bar.Children.Add(copy);
        bar.Children.Add(head);
        var code = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 4, 8),
            Content = new SelectableTextBlock { Text = text, FontFamily = Mono, FontSize = 12, LineHeight = 18 },
        };
        return new DockPanel { Children = { Docked(bar, Dock.Top), code } };
    }

    /// <summary>Во что переведётся мод: файлы движка подряд (у Godot — скрипт из архива мода).</summary>
    internal static string EngineCode(MX.ModxDoc doc, MX.Target t, string? gameDir)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var f in t.Emit(doc, gameDir))
            {
                sb.Append("// ").Append(f.Path).Append('\n');
                if (f.Bytes is null) { sb.Append(f.Text).Append('\n'); continue; }
                using var zip = new ZipArchive(new MemoryStream(f.Bytes), ZipArchiveMode.Read);
                foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".gd")))
                {
                    using var r = new StreamReader(e.Open());
                    sb.Append(r.ReadToEnd()).Append('\n');
                }
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or InvalidDataException) { return e.Message; }
    }

    /// <summary>Журнал моста: что ставилось в игру, что снято, проверка модерации.</summary>
    Control InjectPane()
    {
        var g = Game;
        var path = GamePath;
        var left = Ui.Col(8);
        if (path is null) left.Children.Add(Ui.Text(g is null ? I18n.T("v15.ms.inj.pick") : I18n.T("v15.ms.inj.notFound", ("game", g.Def.Name)), "small", color: Ui.Res("Warn"), wrap: true));
        else
        {
            left.Children.Add(Ui.Text(g?.Def.Name ?? System.IO.Path.GetFileName(path.TrimEnd('\\', '/')), "h3"));
            left.Children.Add(new TextBlock { Text = path, FontSize = 12, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis });
            var buttons = new WrapPanel();
            foreach (var b in new[]
            {
                Ui.Button(_busy ? I18n.T("v15.ms.injecting") : I18n.T("v16.x.inject"), () => _ = InjectNow(), "primary", Icons.Zap, "F6"),
                Ui.Button(I18n.T("v15.ms.eject"), () => _ = EjectNow(), "", Icons.Undo, "Shift+F6"),
                Ui.Button(I18n.T("v15.ms.attach"), () => Attach(), "ghost", Icons.Layers, I18n.T("v15.ms.attach.tip")),
            })
            {
                b.Margin = new Thickness(0, 2, 8, 4);
                buttons.Children.Add(b);
            }
            left.Children.Add(buttons);
            var installed = new List<string>();
            try { installed = MX.Injector.Installed(path).ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            left.Children.Add(Ui.Text(installed.Count == 0 ? I18n.T("v15.ms.inj.nothing") : I18n.T("v15.ms.inj.installed", ("list", string.Join(", ", installed))), "small muted", wrap: true));
        }
        if (_s.Verdict is { } v) left.Children.Add(CreatorPage.VerdictView(v));
        left.Width = 430;

        var log = new StackPanel { Spacing = 3 };
        if (_s.Log.Count == 0) log.Children.Add(Ui.Text(I18n.T("v15.ms.inj.log.empty"), "small muted", wrap: true));
        foreach (var (at, ok, text) in _s.Log.Skip(Math.Max(0, _s.Log.Count - 60)))
            log.Children.Add(new SelectableTextBlock
            {
                Text = $"{at.ToLocalTime():HH:mm:ss}  {text}", FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = ok ? Ui.Res("Text") : Ui.Res("Bad"),
            });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 18, Margin = new Thickness(12, 10) };
        grid.Children.Add(new ScrollViewer { Content = left });
        var scroll = new ScrollViewer { Content = log };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        return grid;
    }

    // ---------------------------------------------------------------- действия

    void Run()
    {
        if (ActiveDoc() is not { } doc) return;
        _s.Panel = "output";
        if (!doc.Ok) { _s.Panel = "problems"; RenderPanel(); MainWindow.Current?.Toast(I18n.T("v11.lx.fix"), bad: true); return; }
        var errors = new List<Diag>();
        var vars = MX.Sandbox.ParseVars(_s.Vars, errors);
        // 19.0: каждый запуск пишет ход для отладчика; есть точка останова на пути — встаём на ней.
        var trace = new MX.Sandbox.Trace();
        _s.Run = MX.Sandbox.Run(doc, vars, _s.Ticks, trace: trace);
        foreach (var e in errors) _s.Run.Errors.Add(new Diag(e.Line, I18n.T("v11.lx.varsBad") + ": " + CreatorPage.LxMsg(e), true));
        AfterRun(trace);
        if (Content is not null) { RenderPanel(); RenderStatus(); }
    }

    void Log(bool ok, string text)
    {
        _s.Log.Add((DateTime.UtcNow, ok, text));
        if (_s.Log.Count > 200) _s.Log.RemoveRange(0, _s.Log.Count - 200);
    }

    /// <summary>F6: перевести и поставить мод в папку игры сейчас (в фоне: сборка C# для Unity занимает секунды).</summary>
    async Task InjectNow(bool quiet = false)
    {
        if (_s.Active is not { Modx: true } d || _busy) return;
        var w = MainWindow.Current;
        if (GamePath is not { } path) { _s.Panel = "inject"; RenderPanel(); Side("bridge"); w?.Toast(I18n.T("v15.ms.inj.pick"), bad: true); return; }
        if (Bridge is { Code: false } files) { _s.Panel = "inject"; Log(false, CreatorPage.BridgeHint(files)); RenderPanel(); return; }
        var gameName = Game?.Def.Name ?? System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        var source = d.Editor.Text ?? "";
        _busy = true;
        _s.Panel = "inject";
        RenderTop();
        RenderPanel();
        try
        {
            if (d.Dirty) Save(d, quiet: true);
            var v = Moderation.CheckModx(source);
            _s.Verdict = v.Ok && v.Clean ? null : v;
            if (!v.Ok) { Log(false, I18n.T("v15.ms.inj.blocked")); return; }
            var engine = MX.ModxDoc.Parse(source).Engine is null ? _p?.Target : null;
            var r = await Task.Run(() => MX.Injector.Inject(source, path, engine));
            if (!r.Doc.Ok || r.Target is null)
            {
                foreach (var x in r.Doc.Diags.Where(x => !x.Warning)) Log(false, (x.Line > 0 ? I18n.T("v15.ms.line", ("n", x.Line)) + ": " : "") + CreatorPage.LxMsg(x));
                if (!quiet) w?.Toast(I18n.T("v15.ms.inj.failed"), bad: true);
                return;
            }
            var built = r.Target.Id != "unity" || Features.Inject.Built(path, r.Doc.Id);
            Log(built, I18n.T(built ? "v15.ms.inj.done" : "v15.ms.inj.notBuilt", ("mod", r.Doc.Name), ("game", gameName), ("engine", r.Target.Title), ("n", r.Files.Count)));
            foreach (var f in r.Files.Take(8)) Log(true, "  + " + f);
            if (r.Note is { } note) foreach (var line in note.Split('\n').Where(l => l.Trim() != "").Take(20)) Log(built, "  " + line.Trim());
            if (!quiet) w?.Toast(built ? I18n.T("v15.ms.inj.toast", ("game", gameName)) : I18n.T("v15.ms.inj.failed"), bad: !built);
        }
        catch (Exception e)
        {
            Log(false, e.Message);
            if (!quiet) w?.Toast(e.Message, bad: true);
        }
        finally
        {
            _busy = false;
            if (_live == this) { RenderTop(); RenderPanel(); }
        }
    }

    async Task EjectNow()
    {
        if (ActiveDoc() is not { } doc || GamePath is not { } path) { _s.Panel = "inject"; RenderPanel(); return; }
        try
        {
            var removed = await Task.Run(() => MX.Injector.Eject(doc.Id, path));
            Log(true, I18n.T("v15.ms.inj.ejected", ("n", removed.Count)));
            MainWindow.Current?.Toast(I18n.T("v15.ms.inj.ejected", ("n", removed.Count)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log(false, e.Message); }
        _s.Panel = "inject";
        RenderPanel();
    }

    /// <summary>В библиотеку инжектов игры: ставится на каждом «Играть» и снимается после игры.</summary>
    Features.InjectItem? Attach(bool quiet = false)
    {
        if (_s.Active is not { Modx: true } d) return null;
        if (Game is not { } g) { _s.Panel = "inject"; RenderPanel(); MainWindow.Current?.Toast(I18n.T("v16.x.needGame"), bad: true); return null; }
        if (!Save(d, quiet: true)) return null;
        var (item, v) = Features.Inject.AddLocal(d.Path!, g.Def.Id);
        _s.Verdict = v;
        if (item is not null)
        {
            Log(true, I18n.T("v11.lx.attached", ("title", item.Title), ("game", g.Def.Name)));
            if (!quiet) MainWindow.Current?.Toast(I18n.T("v11.lx.attached", ("title", item.Title), ("game", g.Def.Name)));
        }
        else Log(false, I18n.T("v15.ms.inj.blocked"));
        _s.Panel = "inject";
        RenderPanel();
        return item;
    }

    async Task PlayWithInject()
    {
        if (Attach(quiet: true) is null || Game is not { } g) return;
        await Task.Yield();
        Actions.Play(g);
    }

    void Check()
    {
        if (_s.Active is not { Modx: true } d) return;
        _s.Verdict = Moderation.CheckModx(d.Editor.Text ?? "");
        _s.Panel = "inject";
        RenderPanel();
    }

    void Sell()
    {
        if (_s.Active is not { Modx: true } d) return;
        var v = Moderation.CheckModx(d.Editor.Text ?? "");
        _s.Verdict = v;
        if (!v.Ok || !Save(d, quiet: true)) { _s.Panel = "inject"; RenderPanel(); return; }
        ListingWizard.Show(null, MarketData.Reload, d.Path, "script");
    }

    // ---------------------------------------------------------------- строка состояния и клавиши

    void RenderStatus()
    {
        var parts = new List<string> { "ModScript X" };
        if (_s.Active is { } d)
        {
            if (d.Modx)
            {
                var t = TargetFor(ActiveDoc());
                parts.Add($"{t.Title} → {Lang(t.Language)}");
                var errors = d.Diags.Count(x => !x.Warning) + d.Lints.Count(x => !x.Warning);
                var warnings = d.Diags.Count(x => x.Warning) + d.Lints.Count(x => x.Warning);
                parts.Add(errors == 0 ? I18n.T("v15.ms.noProblems") : I18n.T("v15.ms.errors", ("n", errors)));
                if (warnings > 0) parts.Add(I18n.T("v19.status.warnings", ("n", warnings)));
                if (d.CheckMs > 0) parts.Add(I18n.T("v18.x.checked", ("ms", d.CheckMs.ToString(d.CheckMs < 10 ? "0.0" : "0"))));
            }
            parts.Add(d.Dirty ? I18n.T("v15.ms.modified") : I18n.T("v15.ms.saved"));
        }
        if (_s.Watch) parts.Add(I18n.T("v15.ms.watching"));
        if (_s is { Debugging: true, Trace: { Frames.Count: > 0 } trace }) parts.Add(I18n.T("v19.status.debug", ("i", _s.Frame + 1), ("n", trace.Frames.Count)));
        var line = new TextBlock { Text = string.Join("   ·   ", parts), FontSize = 11.5, Foreground = Brushes.White, FontFamily = Mono, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var caret = new TextBlock { Text = I18n.T("v15.ms.caret", ("line", _s.Caret.Line), ("col", _s.Caret.Col)) + "   ·   F5 · F9 · F12 · Ctrl+P", FontSize = 11.5, Foreground = Brushes.White, FontFamily = Mono, VerticalAlignment = VerticalAlignment.Center };
        var bar = new DockPanel();
        DockPanel.SetDock(caret, Dock.Right);
        bar.Children.Add(caret);
        bar.Children.Add(line);
        _status.Child = bar;
        _status.Background = Color;
        _status.Padding = new Thickness(14, 4);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (IdeKey(e)) { e.Handled = true; return; }
        switch (e.Key)
        {
            case Key.F6 when shift: _ = EjectNow(); break;
            case Key.F6: _ = InjectNow(); break;
            case Key.F8: NextProblem(); break;
            case Key.S when ctrl: if (_s.Active is { } d) Save(d); break;
            case Key.N when ctrl: NewFile(); break;
            case Key.O when ctrl: _ = OpenFile(); break;
            case Key.W when ctrl: if (_s.Active is { } c) Close(c); break;
            case Key.F when ctrl: _s.Active?.Editor.Find(); break;
            case Key.H when ctrl: _s.Active?.Editor.Find(true); break;
            case Key.Tab when ctrl && _s.Docs.Count > 1:
                var i = _s.Active is null ? 0 : _s.Docs.IndexOf(_s.Active);
                Activate(_s.Docs[(i + (shift ? _s.Docs.Count - 1 : 1)) % _s.Docs.Count]);
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- снимки и самотест

    /// <summary>Для снимков: проект «Arena Rewards» по анкете (игра с BepInEx), журнал моста.</summary>
    internal static string DemoProject()
    {
        var game = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found && g.Def.Loader == LoaderKind.Bepinex) ?? AppState.Games.FirstOrDefault(g => g.Status == Detect.Found && !g.Def.IsMinecraft);
        var p = XProjects.List().FirstOrDefault(x => x.Name == "Arena Rewards")
            ?? XProjects.Create(new XForm("Arena Rewards", "Gold for every wave you survive, a bonus on load and a toast when a wave starts.", game?.Def.Id ?? "", null, "economy", "unity-mono", "unity",
                ["start", "load", "tick"], "Demo Studio", "1.2.0", "violet"));
        if (!Sessions.TryGetValue(p.Id, out var s)) Sessions[p.Id] = s = new Session();
        if (s.Log.Count == 0)
        {
            s.Log.Add((DateTime.UtcNow, true, I18n.T("v15.ms.inj.done", ("mod", p.Name), ("game", game?.Def.Name ?? "Game"), ("engine", "Unity (BepInEx 5)"), ("n", 2))));
            s.Log.Add((DateTime.UtcNow, true, "  + BepInEx/modscript/Arena_Rewards/ModScript_Arena_Rewards.cs"));
            s.Log.Add((DateTime.UtcNow, true, "  built into BepInEx/plugins/ModScript_Arena_Rewards"));
        }
        s.Run = null;
        return p.Id;
    }

    [SelfTest]
    static string StudioOutlineAndCode()
    {
        var text = "mod \"Probe\"\nengine renpy\nlet bonus = 5\nfn pay(w) {\n  return w * bonus\n}\nhook start {\n  gold += pay(2)\n}\n";
        var outline = Outline(text);
        if (outline.Count != 3 || outline[0] != (1, "mod", "Probe") || outline[1] != (4, "fn", "pay(w)") || outline[2] != (7, "hook", "start"))
            throw new Exception("outline: " + string.Join(", ", outline));
        var doc = MX.ModxDoc.Parse(text);
        var code = EngineCode(doc, MX.Target.Find("renpy")!, null);
        if (!code.Contains("pay") || !code.StartsWith("// ")) throw new Exception("engine code: " + code[..Math.Min(80, code.Length)]);
        var gd = EngineCode(doc, MX.Target.Find("godot")!, null);
        if (!gd.Contains("func")) throw new Exception("godot code from the archive");
        return "outline (mod, fn, hook) and engine code (Ren'Py, Godot from zip)";
    }

    [SelfTest]
    static string StudioHeaderAndBlocks()
    {
        var text = "# My mod\nmod \"A\"\nversion 1.0.0\nengine unity\n\nhook start {\n    say \"hi\"\n}\n";
        var t = SetHeader(SetHeader(SetHeader(text, "mod", "\"B\""), "version", "2.0.0"), "author", "\"Me\"");
        var doc = MX.ModxDoc.Parse(t);
        if (doc.Name != "B" || doc.Version != "2.0.0" || doc.Author != "Me" || doc.Engine != "unity" || !doc.Ok) throw new Exception("header: " + t);
        if (!t.Contains("version 2.0.0\nauthor \"Me\"\nengine")) throw new Exception("author goes after version: " + t);
        var back = SetHeader(t, "author", null);
        if (back.Contains("author") || MX.ModxDoc.Parse(back).Version != "2.0.0") throw new Exception("remove author: " + back);
        // Каждый блок вставляется в хук и не ломает разбор.
        var n = 0;
        foreach (var (_, items) in Blocks())
            foreach (var (key, code, ev) in items)
            {
                var src = ev is not null || code.StartsWith("fn ") ? "mod \"T\"\n" + code : "mod \"T\"\nlet gold = 1\nlet health = 1\nlet score = 0\nlet timer = 0\nlet ticks = 0\nlet bonus = 1\nhook start {\n" + string.Join("\n", code.TrimEnd().Split('\n').Select(l => "    " + l)) + "\n}\n";
                var d = MX.ModxDoc.Parse(src);
                if (!d.Ok) throw new Exception(key + ": " + string.Join("; ", d.Diags.Where(x => !x.Warning).Select(x => x.Message)));
                n++;
            }
        return $"header lines kept in order, {n} blocks parse";
    }
}
