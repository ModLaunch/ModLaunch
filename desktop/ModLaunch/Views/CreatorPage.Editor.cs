using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    /// <summary>12.0: что видно у открытого проекта — code (редактор) или overview (обзор: сборки, проверки, выпуски).</summary>
    string _pane = "code";
    /// <summary>Панель «Проверка в игре» справа от кода и место кнопки «Собрать и проверить» / «Остановить» в шапке.</summary>
    StackPanel? _testBox;
    ContentControl? _testAction;
    TextBlock? _testClock;
    DispatcherTimer? _testTick;
    bool _testHooked;

    // ---------------------------------------------------------------- редактор

    Control Editor()
    {
        var p = _open!;
        HookTest();
        // 12.1: полоса инструментов маленькой IDE: назад, статус сборки с версией и действия; «Код · Обзор» — вкладками в шапке Hub (PaneTabs).
        var back = Ui.Button("", () => { if (_dirty) Save(quiet: true); _open = null; _pane = "code"; Build(); }, "icon ghost", Icons.Back, I18n.T("cr.back"));
        var status = p.Meta().Status(File.Exists(p.Script) ? File.GetLastWriteTimeUtc(p.Script) : p.Updated);
        var state = Ui.Row(8, Ui.Dot(StatusBrush(status), 8), Ui.Text(I18n.T($"v12.pj.st.{status}.short"), "small"), HubLook.MonoText(p.Version == "" ? "" : "v" + p.Version));
        state.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(state, I18n.T($"v12.pj.st.{status}"));
        _testAction = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        // 12.0: главная кнопка — «Собрать и проверить»; сохранить и просто установить — значками (Ctrl+S и сборка сохраняют и так).
        var actions = Ui.Row(6,
            Ui.Button("", () => Actions.OpenFolder(Directory.CreateDirectory(Path.Combine(p.Dir, "files")).FullName), "icon ghost", Icons.Folder, I18n.T("cr.files")),
            Ui.Button("", Delete, "icon ghost", Icons.Trash, I18n.T("cr.delete")),
            Ui.Button("", () => Save(), "icon ghost", Icons.Save, I18n.T("cr.save") + " (Ctrl+S)"),
            Ui.Button("", () => _ = InstallOpen(), "icon ghost", Icons.Download, I18n.T("cr.install")),
            new Border { Width = 1, Height = 22, Background = HubLook.Edge, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center },
            Ui.Button(I18n.T("cr.export"), () => _ = Export(), "", Icons.Package),
            Ui.Button(I18n.T("cr.publish"), Publish, "", Icons.Upload),
            _testAction, HubLook.Key("F5"));
        actions.VerticalAlignment = VerticalAlignment.Center;
        var strip = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        strip.Children.Add(actions);
        strip.Children.Add(Ui.Row(10, back, new Border { Width = 1, Height = 22, Background = HubLook.Edge, VerticalAlignment = VerticalAlignment.Center }, state));
        var bar = new Border { Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 7, 8, 7), Child = strip };

        Control root;
        if (_pane == "overview")
        {
            _testBox = null;
            RenderTestAction(Playtest.For(p.Id));
            root = Ui.Col(14, bar, Overview(p));
        }
        else root = Ui.Col(14, bar, CodePane());
        // F5 — собрать и проверить, Shift+F5 — остановить (как в IDE). Ctrl+Enter занят окном: «Играть».
        root.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.F5 || _open is null) return;
            if (e.KeyModifiers == KeyModifiers.None) { e.Handled = true; BuildAndTest(); }
            else if (e.KeyModifiers == KeyModifiers.Shift) { e.Handled = true; Playtest.For(_open.Id)?.Stop(); }
        }, RoutingStrategies.Tunnel);
        return root;
    }

    /// <summary>12.0: «Код · Обзор» — у проекта своя сводка (статус, сборки, проверки, Hub); 12.1 — вкладками в шапке Hub.</summary>
    List<Control> PaneTabs()
    {
        var p = _open!;
        var tabs = new List<Control>();
        foreach (var (id, key) in new[] { ("code", "v12.pj.pane.code"), ("overview", "v12.pj.pane.overview") })
        {
            var pane = id;
            // Открыли обзор — цифры Hub и маркета перечитать (сессия могла идти часами).
            var b = Ui.Button(I18n.T(key), () => { if (_pane == pane) return; _pane = pane; if (pane == "overview") ForgetStats(p.Id); Build(); }, "hub-tab");
            if (_pane == id) b.Classes.Add("active");
            tabs.Add(b);
        }
        return tabs;
    }

    Control CodePane()
    {
        // Быстрые вставки: команда с образцом прямо в место курсора.
        var snippets = Ui.Row(6);
        foreach (var (label, text) in new[]
        {
            ("needs", "needs Author-ModName\n"),
            ("config", "config \"BepInEx.cfg\" [Section] Key = value\n"),
            ("edit", "edit \"Data/Objects\" \"472\" Price = 10\n"),
            ("dialogue", "dialogue \"Abigail\" \"Mon\" = \"Привет!\"\n"),
            ("let", "let name = 1\n"),
            ("for", "for x in 1 2 3 {\n  print \"$x\"\n}\n"),
            ("when", "when Season = spring {\n  \n}\n"),
            ("if", "if $x > 1 {\n  \n}\nelse {\n  \n}\n"),
            ("write", "write \"MyMod/readme.txt\" = \"текст\"\n"),
            ("copy", "copy \"file.txt\" to \"plugins/MyMod/file.txt\"\n"),
        })
        {
            var t = text;
            var chip = Ui.Button("+ " + label, () => Insert(t), "chip snippet");
            chip.FontFamily = HubLook.Mono;
            snippets.Children.Add(chip);
        }

        // 12.1: поверхность кода — вкладка файла, быстрые вставки, редактор и строка подсказок по клавишам.
        var file = Ui.Row(8, Ui.Dot(HubLook.Violet, 6), HubLook.MonoText("main.mls", 12, Ui.Res("Text")));
        var fileTab = new Border { Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1, 0, 1, 0), Padding = new Thickness(14, 9), Child = file };
        var lang = HubLook.MonoText(Dotted("ModScript", GameCatalog.ById(_open!.Game)?.ShortName ?? _open.Game), 11);
        lang.Margin = new Thickness(0, 0, 14, 0);
        var tabs = new DockPanel { Background = HubLook.Raised };
        DockPanel.SetDock(lang, Dock.Right);
        tabs.Children.Add(lang);
        tabs.Children.Add(new Border { Child = fileTab, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 0, 0, 0) });
        snippets.Margin = new Thickness(10, 8);
        var inserts = new ScrollViewer { Content = snippets, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var keys = HubLook.MonoText(I18n.T("v12d.ide.keys"), 10.5);
        keys.TextTrimming = TextTrimming.CharacterEllipsis;
        var status = new Border { Background = HubLook.Raised, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 5), Child = keys };
        var surface = new DockPanel();
        foreach (var (part, edge) in new (Control, bool)[] { (tabs, true), (inserts, true) })
        {
            var holder = new Border { BorderBrush = HubLook.Edge, BorderThickness = new Thickness(0, 0, 0, edge ? 1 : 0), Child = part };
            DockPanel.SetDock(holder, Dock.Top);
            surface.Children.Add(holder);
        }
        DockPanel.SetDock(status, Dock.Bottom);
        surface.Children.Add(status);
        surface.Children.Add(new Border { Padding = new Thickness(4, 6, 4, 4), Child = Free(_code) });
        var editor = new Border { Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = surface };
        _testBox = new StackPanel { Spacing = 8 };
        var side = HubLook.Frame(new ScrollViewer { Content = Ui.Col(16, _testBox, new Border { Height = 1, Background = HubLook.Edge }, Free(_check)), MaxHeight = 640 }, new Thickness(18, 16));
        side.VerticalAlignment = VerticalAlignment.Top;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 16 };
        grid.Children.Add(editor);
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        RenderCheck();
        RenderTest();
        return grid;
    }

    /// <summary>Постоянный элемент (редактор, панель проверки) — отцепить от прежней разметки перед новой.</summary>
    static T Free<T>(T c) where T : Control
    {
        switch (c.Parent)
        {
            case Decorator d: d.Child = null; break;
            case ContentControl cc: cc.Content = null; break;
            case Panel panel: panel.Children.Remove(c); break;
        }
        return c;
    }

    void Insert(string text) => _code.Insert(text);

    void GoToLine(int line) => _code.GoToLine(line);

    static string Msg(Diag d) => ModScript.Explain(d, (k, a) => I18n.T(k, a));

    /// <summary>Правая панель: ошибки со ссылкой на строку и что получится после сборки.</summary>
    void RenderCheck()
    {
        _check.Children.Clear();
        var b = ModScript.Compile(_code.Text ?? "");
        var game = GameCatalog.ById(b.Game);
        var errors = b.Diags.Where(d => !d.Warning).ToList();
        _code.MarkErrors(errors.Select(d => d.Line));
        _check.Children.Add(HubLook.Eyebrow(I18n.T("v12d.ide.check"), HubLook.Violet));
        _check.Children.Add(Ui.Row(8,
            Ui.Dot(errors.Count == 0 ? Ui.Res("Good") : Ui.Res("Bad"), 10),
            Ui.Text(errors.Count == 0 ? I18n.T("cr.ok") : I18n.T("cr.errors", ("n", errors.Count)), "h3")));
        foreach (var d in errors.Take(12))
        {
            var line = d.Line;
            var row = Ui.Button($"{I18n.T("cr.line", ("n", line))}: {Msg(d)}", () => GoToLine(line), "ghost");
            row.HorizontalAlignment = HorizontalAlignment.Stretch;
            row.HorizontalContentAlignment = HorizontalAlignment.Left;
            row.Foreground = Ui.Res("Bad");
            if (row.Content is string text) row.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            _check.Children.Add(row);
        }
        if (errors.Count > 0) return;

        var format = game is null ? "?" : game.Loader switch { LoaderKind.Smapi => "Content Patcher", LoaderKind.Bepinex => "Thunderstore (BepInEx)", _ => "zip" };
        _check.Children.Add(new Border { Height = 1, Background = HubLook.Edge, Margin = new Thickness(0, 4) });
        _check.Children.Add(HubLook.Eyebrow(I18n.T("cr.result")));
        void Line(string k, string v) { var r = new DockPanel(); var val = Ui.Text(v, "small"); DockPanel.SetDock(val, Dock.Right); r.Children.Add(val); r.Children.Add(Ui.Text(k, "small muted")); _check.Children.Add(r); }
        Line(I18n.T("cr.r.name"), $"{b.Name} {b.Version}");
        Line(I18n.T("cr.r.game"), game?.Name ?? b.Game);
        Line(I18n.T("cr.r.format"), format);
        if (b.Needs.Count > 0) Line(I18n.T("cr.r.needs"), b.Needs.Count.ToString());
        if (b.Changes.Count > 0) Line(I18n.T("cr.r.changes"), b.Changes.Count.ToString());
        if (b.Configs.Count > 0) Line(I18n.T("cr.r.configs"), b.Configs.Count.ToString());
        if (b.Inis.Count > 0) Line(I18n.T("cr.r.inis"), b.Inis.Count.ToString());
        if (b.Copies.Count > 0) Line(I18n.T("cr.r.files"), b.Copies.Count.ToString());
        if (game is null) _check.Children.Add(Ui.Text(I18n.T("cr.build.noGame", ("game", b.Game)), "small", color: Ui.Res("Warn"), wrap: true));
        foreach (var log in b.Log.Take(8)) _check.Children.Add(new TextBlock { Text = "› " + log, FontFamily = HubLook.Mono, FontSize = 11.5, Foreground = HubLook.Cyan, TextWrapping = TextWrapping.Wrap });
    }

    void Delete()
    {
        var p = _open!;
        var w = MainWindow.Current!;
        if (!Settings.Data.Bool("confirmRemove", true)) { Projects.Delete(p); _open = null; _dirty = false; Build(); return; }
        w.Dialog(I18n.T("cr.delete"), Ui.Text(I18n.T("cr.delete.text", ("name", p.Name)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("cr.delete"), () => { w.CloseDialog(); Projects.Delete(p); _open = null; _dirty = false; Build(); }, "primary", Icons.Trash));
    }

    // ---------------------------------------------------------------- собрать и проверить

    void BuildAndTest()
    {
        if (_open is null) return;
        if (_dirty) Save(quiet: true);
        Playtest.Start(_open, _code.Text ?? "");
    }

    /// <summary>Следить за проверкой, пока страница на экране (событие статическое — отписываемся при уходе).</summary>
    void HookTest()
    {
        if (_testHooked) return;
        _testHooked = true;
        Playtest.Changed += OnTestChanged;
        DetachedFromVisualTree -= UnhookTest;
        DetachedFromVisualTree += UnhookTest;
    }

    void UnhookTest(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Playtest.Changed -= OnTestChanged;
        _testHooked = false;
        _testTick?.Stop();
    }

    void OnTestChanged()
    {
        // Список проектов на экране — точки статуса обновить, но только по итогу (шаги список не меняют).
        if (_open is null) { if (_tab == "mine" && _minePart == "projects" && Playtest.Current is { Active: false }) Build(); return; }
        // Обзор целиком из истории проекта — перестраиваем; в редакторе — только панель проверки (курсор и правка не теряются).
        if (_pane == "overview") Build();
        else RenderTest();
    }

    /// <summary>Кнопка в шапке: «Собрать и проверить» или, пока идёт проверка, «Остановить».</summary>
    void RenderTestAction(Playtest? run)
    {
        if (_testAction is null) return;
        _testAction.Content = run is { Active: true }
            ? Ui.Button(I18n.T("v12.pj.stop"), run.Stop, "primary", Icons.Stop, I18n.T("v12.pj.stop.tip"))
            : Ui.Button(I18n.T("v12.pj.run"), BuildAndTest, "primary", Icons.Play, I18n.T("v12.pj.run.tip"));
    }

    /// <summary>Панель «Проверка в игре»: шаги идущей проверки или итог последней — время, код выхода, строки лога мода.</summary>
    void RenderTest()
    {
        if (_open is null) return;
        var run = Playtest.For(_open.Id);
        RenderTestAction(run);
        if (_testBox is null) return;
        _testBox.Children.Clear();
        _testClock = null;
        _testBox.Children.Add(HubLook.Head(I18n.T("v12d.ide.run"), I18n.T("v12.pj.test")));
        if (run is { Active: true } or { Step: Playtest.Stage.Failed })
        {
            _testBox.Children.Add(TestSteps(run));
            if (run.Step == Playtest.Stage.Running)
            {
                _testClock = Ui.Text("", "small muted");
                _testClock.VerticalAlignment = VerticalAlignment.Center;
                var stop = Ui.Button(I18n.T("v12.pj.stop"), run.Stop, "", Icons.Stop, I18n.T("v12.pj.stop.tip"));
                var line = new DockPanel();
                DockPanel.SetDock(stop, Dock.Right);
                line.Children.Add(stop);
                line.Children.Add(_testClock);
                _testBox.Children.Add(line);
                TickTest();
            }
            if (run.Error is { } error) _testBox.Children.Add(Ui.Text(error, "small", color: Ui.Res("Bad"), wrap: true));
            foreach (var note in run.Notes.Take(4)) _testBox.Children.Add(Ui.Text("⚠ " + note, "small", color: Ui.Res("Warn"), wrap: true));
            if (run.Step == Playtest.Stage.Failed) _testBox.Children.Add(Again());
        }
        else if ((run?.Result ?? _open.Meta().LastTest) is { } last)
        {
            _testBox.Children.Add(TestResult(last, run?.LogPath ?? LogPathFor(_open), compact: true));
            foreach (var note in (run?.Notes ?? []).Take(4)) _testBox.Children.Add(Ui.Text("⚠ " + note, "small", color: Ui.Res("Warn"), wrap: true));
            _testBox.Children.Add(Again());
        }
        else _testBox.Children.Add(Ui.Text(I18n.T("v12.pj.hint"), "small muted", wrap: true));
        if (run?.Step != Playtest.Stage.Running) _testTick?.Stop();
    }

    Control Again()
    {
        var b = Ui.Button(I18n.T("v12.pj.again"), BuildAndTest, "", Icons.Refresh, I18n.T("v12.pj.run.tip"));
        b.HorizontalAlignment = HorizontalAlignment.Left;
        return b;
    }

    /// <summary>Часы «Игра идёт · 2:31» — раз в секунду, без перестройки панели.</summary>
    void TickTest()
    {
        void Show()
        {
            if (_testClock is null || _open is null || Playtest.For(_open.Id) is not { Step: Playtest.Stage.Running, Started: { } at }) { _testTick?.Stop(); return; }
            _testClock.Text = I18n.T("v12.pj.running", ("time", ProjectMeta.Duration((DateTime.UtcNow - at).TotalSeconds)));
        }
        Show();
        if (_testTick is null)
        {
            _testTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _testTick.Tick += (_, _) => Show();
        }
        // Снимки экрана: время на картинке — то, что посчитали, без живых часов.
        if (!Program.Screenshot) _testTick.Start();
    }

    /// <summary>Шаги проверки: сделанные — с галочкой, текущий — акцентом, сорвавшийся — красным.</summary>
    static Control TestSteps(Playtest run)
    {
        var col = Ui.Col(6);
        var now = run.Step == Playtest.Stage.Failed ? run.FailedAt ?? Playtest.Stage.Build : run.Step;
        foreach (var step in Playtest.Steps)
        {
            var done = step < now || run.Step == Playtest.Stage.Done;
            var current = step == now && run.Step != Playtest.Stage.Done;
            var failed = current && run.Step == Playtest.Stage.Failed;
            Control mark = failed ? Ui.Icon(Icons.Alert, 13, Ui.Res("Bad"))
                : done ? Ui.Icon(Icons.Check, 13, Ui.Res("Good"))
                : new Border { Width = 13, Height = 13, Child = Ui.Dot(current ? Ui.Res("Brand") : Ui.Res("Faint"), current ? 9 : 6) };
            var text = Ui.Text(I18n.T("v12.pj.step." + step.ToString().ToLowerInvariant()), "small", color: current ? (failed ? Ui.Res("Bad") : Ui.Res("Text")) : Ui.Res("Muted"));
            if (current) text.FontWeight = FontWeight.SemiBold;
            text.VerticalAlignment = VerticalAlignment.Center;
            col.Children.Add(Ui.Row(10, mark, text));
        }
        return col;
    }

    /// <summary>Строки лога — тем же моноширинным шрифтом, что и код.</summary>
    static readonly FontFamily LogFont = new("Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, monospace");

    /// <summary>Итог проверки: пройдена ли, сколько шла, код выхода и строки лога этого мода.</summary>
    static Control TestResult(TestNote t, string? logPath, bool compact)
    {
        var col = Ui.Col(8);
        var title = t.Ok ? I18n.T("v12.pj.passed") : t.Errors > 0 ? I18n.T("v12.pj.failed.log", ("n", t.Errors)) : I18n.T("v12.pj.failed.exit");
        var head = Ui.Text(title, "h3");
        if (compact) head.FontSize = 14;
        col.Children.Add(Ui.Row(8, Ui.Dot(t.Ok ? Ui.Res("Good") : Ui.Res("Bad"), 10), head));
        var how = t.ByUser ? I18n.T("v12.pj.byUser") : t.Exit is { } code ? I18n.T("v12.pj.exit", ("code", code)) : I18n.T("v12.pj.exit.none");
        col.Children.Add(Ui.Text(string.Join(" · ", new[] { t.Version, I18n.T("v12.pj.ran", ("time", ProjectMeta.Duration(t.Seconds))), how, Since(t.Time) }.Where(s => s != "")), "small muted", wrap: true));
        if (!t.FreshLog) col.Children.Add(Ui.Text(I18n.T("v12.pj.log.stale"), "small", color: Ui.Res("Warn"), wrap: true));
        else if (t.Lines.Count == 0) col.Children.Add(Ui.Text(I18n.T("v12.pj.log.none"), "small muted", wrap: true));
        var max = compact ? 12 : 30;
        if (t.Lines.Count > 0)
        {
            var lines = Ui.Col(2);
            foreach (var l in t.Lines.Take(max))
                lines.Children.Add(new TextBlock
                {
                    Text = l, FontFamily = LogFont, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Foreground = ProjectMeta.IsError(l) ? Ui.Res("Bad") : ProjectMeta.IsWarning(l) ? Ui.Res("Warn") : Ui.Res("Muted"),
                });
            if (t.Lines.Count > max) lines.Children.Add(Ui.Text(I18n.T("v12.pj.log.more", ("n", t.Lines.Count - max)), "small muted"));
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = lines });
        }
        if (logPath is not null && File.Exists(logPath))
        {
            var open = Ui.Button(I18n.T("v12.pj.log.open"), () => Ui.OpenUrl(logPath), "ghost", Icons.External, logPath);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            col.Children.Add(open);
        }
        return col;
    }

    /// <summary>Лог загрузчика игры проекта (для «Открыть лог»), если игра найдена.</summary>
    static string? LogPathFor(Project p)
    {
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == p.Game);
        return g?.Path is null ? null : Logs.PathFor(g.Def, g.Path);
    }

    // ---------------------------------------------------------------- сборка, установка, публикация

    async Task<(ModBuild Build, Packed Packed)?> BuildOpen()
    {
        Save(quiet: true);
        var b = ModScript.Compile(_code.Text ?? "");
        if (!b.Ok)
        {
            ProjectMeta.NoteBuild(_open!.Dir, false, 0, null, b.Version);
            MainWindow.Current?.Toast(I18n.T("cr.build.hasErrors"), bad: true);
            return null;
        }
        try { return (b, await Projects.Pack(b, _open!.Dir)); }
        catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); return null; }
    }

    async Task InstallOpen()
    {
        if (await BuildOpen() is not { } r) return;
        await InstallBuilt(r.Build, r.Packed);
    }

    static async Task InstallBuilt(ModBuild b, Packed packed)
    {
        var w = MainWindow.Current!;
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == b.Game);
        if (g is null || g.Path is null) { w.Toast(I18n.T("cr.install.noGame"), bad: true); return; }
        if (!g.LoaderInstalled) { w.Toast(I18n.T("games.loaderMissing", ("loader", g.Def.LoaderName)), bad: true); return; }
        try
        {
            Projects.Install(g, b, packed);
            AppState.Notify();
            foreach (var w2 in packed.Warnings) w.Toast(w2);
            // Моды из needs: ставим из каталога игры (для Stardew — подсказываем, где взять).
            var missing = Projects.MissingNeeds(g, b);
            var fromCatalog = new List<Sources.ModInfo>();
            foreach (var need in missing)
            {
                try { if (await Sources.Catalog.Get(g.Def, need) is { } mod) fromCatalog.Add(mod); } catch { }
            }
            if (fromCatalog.Count > 0) _ = Actions.InstallQueue(g, b.Name, fromCatalog.Select(m => (m, (Pin?)null)).ToList());
            var notFound = missing.Where(m => fromCatalog.All(c => !c.Id.Equals(m, StringComparison.OrdinalIgnoreCase))).ToList();
            if (notFound.Count > 0) w.Toast(I18n.T("cr.install.needs", ("mods", string.Join(", ", notFound))));
            w.Toast(I18n.T("cr.install.done", ("name", b.Name), ("game", g.Def.Name)));
        }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
    }

    async Task Export()
    {
        if (await BuildOpen() is not { } r) return;
        var w = MainWindow.Current!;
        var game = GameCatalog.ById(r.Build.Game);
        var list = Ui.Col(4, r.Packed.Files.Take(14).Select(f => (Control)Ui.Text("• " + f, "small muted")).ToArray());
        var body = Ui.Col(10,
            Ui.Text(I18n.T("cr.export.text", ("format", r.Packed.Format)), "muted", wrap: true),
            new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = list });
        foreach (var warn in r.Packed.Warnings) body.Children.Add(Ui.Text("⚠ " + warn, "small", color: Ui.Res("Warn"), wrap: true));
        // 12.0: у проекта своя папка сборок (по версиям) — её и открываем.
        var folder = Path.GetDirectoryName(r.Packed.Zip) ?? Projects.OutDir;
        var buttons = new List<Control> { Ui.Button(I18n.T("common.close"), w.CloseDialog), Ui.Button(I18n.T("cr.export.folder"), () => Actions.OpenFolder(folder), "", Icons.Folder) };
        if (game?.ThunderstoreCommunity is not null && game.Loader == LoaderKind.Bepinex)
            buttons.Add(Ui.Button("Thunderstore", () => Ui.OpenUrl($"https://thunderstore.io/c/{game.ThunderstoreCommunity}/create/"), "primary", Icons.External));
        else if (game?.NexusDomain is not null)
            buttons.Add(Ui.Button("Nexus Mods", () => Ui.OpenUrl($"https://www.nexusmods.com/{game.NexusDomain}/mods/add"), "primary", Icons.External));
        w.Dialog(I18n.T("cr.export.title", ("name", Path.GetFileName(r.Packed.Zip))), body, buttons.ToArray());
    }

    void Publish()
    {
        Save(quiet: true);
        var code = _code.Text;
        var b = ModScript.Compile(code);
        if (!b.Ok) { MainWindow.Current?.Toast(I18n.T("cr.build.hasErrors"), bad: true); return; }
        var p = _open!;
        var draft = new HubDraft
        {
            Name = b.Name, Summary = b.About, Game = b.Game, Version = b.Version, Code = code,
            Description = b.About,
        };
        HubPublish.Show(draft, fromProject: true, pack: async () => (await Projects.Pack(b, p.Dir)).Zip, done: () =>
        {
            // 12.0: запомнить, куда выложен проект (id в Hub выводится из автора и имени), и выпуск — в историю.
            if (Social.Account.Uid is { } uid)
                ProjectMeta.Update(p.Dir, m => { m.HubId = Hub.DocId(uid, draft.Name); m.NoteRelease(draft.Version, "hub", DateTime.UtcNow); });
            ForgetStats(p.Id);
            _tab = "mine"; _minePart = "published"; Build();
        });
    }
}

/// <summary>
/// 12.0: «Собрать и проверить» — сохранить, собрать, поставить в игру, дождаться нужных модов, запустить
/// и, когда игра закроется, записать время, код выхода и строки лога этого мода в project.json.
/// Проверка одна на всю программу (игра запускается одна); страница редактора только смотрит на неё,
/// поэтому итог не теряется, если за время игры уйти со страницы.
/// </summary>
public sealed class Playtest
{
    public enum Stage { Build, Install, Needs, Launch, Running, Done, Failed }

    /// <summary>Шаги, которые видно в панели (Done/Failed — итог, а не шаг).</summary>
    public static readonly Stage[] Steps = [Stage.Build, Stage.Install, Stage.Needs, Stage.Launch, Stage.Running];

    public static Playtest? Current { get; private set; }
    /// <summary>Шаг сменился — всегда в потоке окна.</summary>
    public static event Action? Changed;

    public string ProjectId { get; }
    public string Dir { get; }
    public string Name { get; }
    public string Version { get; }
    public string GameId { get; }
    public Stage Step { get; private set; } = Stage.Build;
    public Stage? FailedAt { get; private set; }
    public string? Error { get; private set; }
    /// <summary>Предупреждения сборки и нужные моды, которых не нашлось.</summary>
    public List<string> Notes { get; } = [];
    public DateTime? Started { get; private set; }
    public TestNote? Result { get; private set; }
    public string? LogPath { get; private set; }
    public bool Active => Step is not (Stage.Done or Stage.Failed);
    readonly ModBuild _build;
    bool _cancel;

    Playtest(Project p, ModBuild b)
    {
        ProjectId = p.Id;
        Dir = p.Dir;
        Name = b.Name == "" ? p.Name : b.Name;
        Version = b.Version;
        GameId = b.Game == "" ? p.Game : b.Game;
        _build = b;
    }

    public static Playtest? For(string projectId) => Current?.ProjectId == projectId ? Current : null;

    /// <summary>Настоящий запуск игры: не в снимках, не в демо и не в проверках (у них папка данных временная).</summary>
    static bool MayLaunch => !Program.Screenshot && !Program.Demo && !Paths.DataDir.Contains("modlaunch-self", StringComparison.OrdinalIgnoreCase);

    /// <summary>Начать проверку. Идёт игра прошлой проверки — она перезапускается с новой сборкой.</summary>
    public static void Start(Project p, string code)
    {
        // Сборка и установка уже идут — вторая такая же только помешала бы.
        if (Current is { Active: true, Step: not Stage.Running }) { MainWindow.Current?.Toast(I18n.T("v12.pj.busy")); return; }
        var previous = Current;
        var run = Current = new Playtest(p, ModScript.Compile(code));
        _ = run.Run(previous);
    }

    void Set(Stage s) { Step = s; Changed?.Invoke(); }

    void Fail(string error)
    {
        FailedAt = Step;
        Error = error;
        Step = Stage.Failed;
        Changed?.Invoke();
    }

    /// <summary>Остановить: до запуска — прервать между шагами, игру — закрыть (итог всё равно запишется).</summary>
    public void Stop()
    {
        if (Step is Stage.Running or Stage.Launch && Launcher.IsRunning(GameId)) { Launcher.Stop(GameId); return; }
        _cancel = true;
        if (Step == Stage.Needs) Actions.StopQueue();
    }

    async Task Run(Playtest? previous)
    {
        try { await RunSteps(previous); }
        // По сути это async void: необработанная ошибка уронила бы окно — показываем её в панели.
        catch (Exception e) { Launcher.Exited -= OnExit; if (Active) Fail(Jobs.Explain(e)); }
    }

    async Task RunSteps(Playtest? previous)
    {
        Set(Stage.Build);
        if (!_build.Ok)
        {
            ProjectMeta.NoteBuild(Dir, false, 0, null, Version);
            Fail(I18n.T("cr.build.hasErrors"));
            return;
        }
        Packed packed;
        try { packed = await Projects.Pack(_build, Dir); }
        catch (Exception e) { Fail(Jobs.Explain(e)); return; }
        Notes.AddRange(packed.Warnings);
        if (_cancel) { Fail(I18n.T("v12.pj.cancelled")); return; }

        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == GameId);
        if (g?.Path is null || g.Registry is null) { Fail(I18n.T("cr.install.noGame")); return; }
        if (!g.LoaderInstalled) { Fail(I18n.T("games.loaderMissing", ("loader", g.Def.LoaderName))); return; }
        // Игра уже идёт: свою прошлую проверку перезапускаем (файлы мода заняты игрой), чужой сеанс не трогаем.
        if (Launcher.IsRunning(GameId))
        {
            if (previous is not { Step: Stage.Running } || previous.GameId != GameId) { Fail(I18n.T("v12.pj.err.running", ("game", g.Def.Name))); return; }
            Launcher.Stop(GameId);
            for (var i = 0; i < 60 && Launcher.IsRunning(GameId); i++) await Task.Delay(250);
            if (Launcher.IsRunning(GameId)) { Fail(I18n.T("v12.pj.err.running", ("game", g.Def.Name))); return; }
        }

        Set(Stage.Install);
        try { Projects.Install(g, _build, packed); }
        catch (Exception e) { Fail(Jobs.Explain(e)); return; }
        AppState.Notify();

        // Нужные моды (needs, Content Patcher) — ставим и ждём: без них проверка бессмысленна.
        Set(Stage.Needs);
        var missing = Projects.MissingNeeds(g, _build);
        if (missing.Count > 0)
        {
            var found = new List<Sources.ModInfo>();
            foreach (var need in missing)
            {
                if (_cancel) break;
                try { if (await Sources.Catalog.Get(g.Def, need) is { } mod) found.Add(mod); } catch { }
            }
            if (found.Count > 0 && !_cancel) await Actions.InstallQueue(g, Name, found.Select(m => (m, (Pin?)null)).ToList());
            if (Projects.MissingNeeds(g, _build) is { Count: > 0 } still) Notes.Add(I18n.T("cr.install.needs", ("mods", string.Join(", ", still))));
        }
        if (_cancel) { Fail(I18n.T("v12.pj.cancelled")); return; }

        Set(Stage.Launch);
        if (!MayLaunch) { Fail(I18n.T("v12.pj.err.notStarted")); return; }
        Launcher.Exited += OnExit;
        var since = DateTime.UtcNow;
        // Без заставки запуска: проверка — рабочий цикл автора, ждать анимацию незачем. Проверку перед игрой и
        // обновления модов пропускаем — проверяем ровно то, что собрали.
        Actions.Play(g, animate: false, skipCheck: true, skipUpdate: true);
        // Minecraft стартует через свой лаунчер — ему даём минуту, остальным хватает пары секунд.
        for (var i = 0; i < (g.Def.IsMinecraft ? 240 : 12) && Step == Stage.Launch && !Launcher.IsRunning(GameId) && !_cancel; i++) await Task.Delay(250);
        if (Step != Stage.Launch) return; // игра успела закрыться — итог уже записан (OnExit)
        if (!Launcher.IsRunning(GameId))
        {
            Launcher.Exited -= OnExit;
            Fail(I18n.T(_cancel ? "v12.pj.cancelled" : "v12.pj.err.notStarted"));
            return;
        }
        Started = Launcher.StartedAt(GameId) ?? since;
        Set(Stage.Running);
    }

    void OnExit(string gameId, bool counted, long ms)
    {
        if (gameId != GameId || Step is not (Stage.Launch or Stage.Running)) return;
        Launcher.Exited -= OnExit;
        _ = Finish();
    }

    async Task Finish()
    {
        try
        {
            var exit = Launcher.LastExit(GameId);
            var since = exit?.Started ?? Started ?? DateTime.UtcNow;
            Started ??= since;
            var g = AppState.Game(GameId);
            // Загрузчик дописывает лог в последние мгновения — даём ему закрыть файл.
            await Task.Delay(400);
            var gamePath = g.Path;
            var (lines, errors, fresh, path) = gamePath is null
                ? ([], 0, false, null)
                : await Task.Run(() => ProjectMeta.ModLog(g.Def, gamePath, _build, since));
            LogPath = path;
            var seconds = (exit?.Ms ?? (long)(DateTime.UtcNow - since).TotalMilliseconds) / 1000.0;
            var byUser = exit?.ByUser ?? false;
            var note = new TestNote(DateTime.UtcNow, ProjectMeta.Passed(exit?.Code, byUser, errors), exit?.Code, seconds, byUser, Version, lines, fresh, errors);
            Result = note;
            ProjectMeta.Update(Dir, m => m.LastTest = note);
            Step = Stage.Done;
            Changed?.Invoke();
            MainWindow.Current?.Toast(I18n.T(note.Ok ? "v12.pj.toast.ok" : "v12.pj.toast.bad", ("name", Name)), bad: !note.Ok);
        }
        catch (Exception e) { if (Active) Fail(Jobs.Explain(e)); }
    }

    /// <summary>Снимки экрана: проверка на шаге step (по умолчанию — игра идёт полторы минуты); null — проверки нет.</summary>
    internal static void Demo(Project? p, Stage step = Stage.Running, TimeSpan? ago = null)
    {
        if (p is null) { Current = null; return; }
        Current = new Playtest(p, ModScript.Compile(File.ReadAllText(p.Script)))
        {
            Step = step, Started = DateTime.UtcNow - (ago ?? TimeSpan.FromSeconds(83)),
        };
    }
}
