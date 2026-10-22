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
/// 15.0: ModScript Studio — среда разработки модов ModScript X (раздел ModScript → «Студия»).
/// Слева — файлы папки «Документы/ModScript X», примеры и структура открытого файла; в середине — вкладки
/// редактора (подсветка, подсказки, поиск и замена); снизу — «Проблемы», «Вывод» песочницы, «Код движка»
/// и «Инжект». Инжект — главная кнопка: F6 ставит мод в выбранную игру сразу (C# для Unity собирает
/// встроенный компилятор), «Следить» — заново на каждое сохранение, «При запуске» — в библиотеку инжектов
/// (ставится на «Играть» и снимается после игры). Студия одна на всю программу: перестройка страницы
/// не теряет открытые файлы, курсор и историю правок.
/// </summary>
public sealed class ModStudio : UserControl
{
    static ModStudio? _shared;

    /// <summary>Студия для страницы: тот же экземпляр, отцепленный от прошлого родителя.</summary>
    public static ModStudio Host()
    {
        var s = _shared ??= new ModStudio();
        switch (s.Parent)
        {
            case Panel p: p.Children.Remove(s); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
        s.Fit(MainWindow.Current);
        s.RenderAll();
        return s;
    }

    sealed class Doc
    {
        public string? Path;
        public string Title = "";
        public string Saved = "";
        public readonly ScriptEditor Editor = new() { Modx = true };
        public List<Diag> Diags = [];
        public bool Dirty => Editor.Text != Saved;
    }

    static readonly FontFamily Mono = HubLook.Mono;

    readonly List<Doc> _docs = [];
    Doc? _active;
    string _panel = "problems";
    string _vars = "gold = 100\nstreak = 0";
    int _ticks = 3;
    MX.Sandbox.Result? _run;
    Verdict? _verdict;
    bool _watch;
    bool _busy;
    readonly List<(DateTime At, bool Ok, string Text)> _log = [];
    DispatcherTimer? _analyze;
    (int Line, int Col) _caret = (1, 1);

    readonly Border _toolbar = new(), _explorer = new(), _tabs = new(), _editorHost = new(), _status = new(), _panelTabs = new(), _panelBody = new();

    TopLevel? _top;

    /// <summary>Шапка Hub, вкладки ModScript и строка состояния окна — около 265 px: студия встаёт ровно в окно.</summary>
    void Fit(TopLevel? top) => Height = Math.Max(480, (top?.ClientSize.Height ?? 860) - 265);

    void OnTopChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TopLevel.ClientSizeProperty) Fit(_top);
    }

    ModStudio()
    {
        AttachedToVisualTree += (_, _) =>
        {
            _top = TopLevel.GetTopLevel(this);
            if (_top is null) return;
            _top.PropertyChanged += OnTopChanged;
            Fit(_top);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_top is not null) _top.PropertyChanged -= OnTopChanged;
            _top = null;
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Content = Layout();
        Restore();
    }

    static string Workspace => CreatorPage.ScriptsDir();

    string? GameId
    {
        get => Settings.Data.Str("lxGame") is { } id && AppState.Games.Any(g => g.Def.Id == id) ? id : null;
        set { Settings.Data["lxGame"] = value; Settings.Save(); }
    }

    static string Engine
    {
        get => Settings.Data.Str("msEngine") ?? "auto";
        set { Settings.Data["msEngine"] = value; Settings.Save(); }
    }

    GameState? Game => GameId is { } id ? AppState.Game(id) : null;
    string? GamePath => Game is { Status: Detect.Found, Path: { } p } && Directory.Exists(p) ? p : null;

    // ---------------------------------------------------------------- раскладка

    Control Layout()
    {
        var left = new Border { Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true, Child = _explorer };
        var editorCard = new Border
        {
            Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true,
            Child = new DockPanel { Children = { Dock(_tabs, Avalonia.Controls.Dock.Top), _editorHost } },
        };
        var bottom = new Border
        {
            Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true,
            Child = new DockPanel { Children = { Dock(_panelTabs, Avalonia.Controls.Dock.Top), _panelBody } },
        };
        var work = new Grid { RowDefinitions = new RowDefinitions("*,8,200"), MinHeight = 0 };
        work.Children.Add(editorCard);
        var hsplit = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Transparent, Height = 8 };
        Grid.SetRow(hsplit, 1);
        work.Children.Add(hsplit);
        Grid.SetRow(bottom, 2);
        work.Children.Add(bottom);

        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("230,8,*") };
        main.Children.Add(left);
        var vsplit = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent, Width = 8 };
        Grid.SetColumn(vsplit, 1);
        main.Children.Add(vsplit);
        Grid.SetColumn(work, 2);
        main.Children.Add(work);

        _toolbar.Margin = new Thickness(0, 0, 0, 10);
        _status.Margin = new Thickness(0, 8, 0, 0);
        return new DockPanel { Children = { Dock(_toolbar, Avalonia.Controls.Dock.Top), Dock(_status, Avalonia.Controls.Dock.Bottom), main } };
    }

    static Control Dock(Control c, Dock where)
    {
        DockPanel.SetDock(c, where);
        return c;
    }

    void RenderAll()
    {
        RenderToolbar();
        RenderExplorer();
        RenderTabs();
        RenderEditor();
        RenderPanel();
        RenderStatus();
    }

    // ---------------------------------------------------------------- файлы и вкладки

    /// <summary>Открытые в прошлый раз файлы; нет ни одного — пример «Арена», чтобы сразу было что запустить.</summary>
    void Restore()
    {
        foreach (var p in (Settings.Data.Str("msOpen") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(p)) Open(p, remember: false);
        if (Settings.Data.Str("msActive") is { } a && _docs.FirstOrDefault(d => d.Path == a) is { } doc) _active = doc;
        if (_docs.Count == 0) OpenText(CreatorPage.ArenaExample(), "arena.modx");
    }

    void Remember()
    {
        Settings.Data["msOpen"] = string.Join("|", _docs.Select(d => d.Path).OfType<string>());
        Settings.Data["msActive"] = _active?.Path;
        Settings.Save();
    }

    Doc Add(string? path, string title, string text, string saved)
    {
        var d = new Doc { Path = path, Title = title, Saved = saved };
        d.Editor.Text = text;
        d.Editor.TextChanged += () => { if (d == _active) Schedule(); };
        d.Editor.SaveRequested += () => Save(d);
        d.Editor.CaretMoved += (line, col) => { if (d == _active) { _caret = (line, col); RenderStatus(); } };
        _docs.Add(d);
        Analyze(d);
        return d;
    }

    void Open(string path, bool remember = true)
    {
        var full = System.IO.Path.GetFullPath(path);
        var doc = _docs.FirstOrDefault(d => d.Path is { } p && string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        if (doc is null)
        {
            string text;
            try { text = File.ReadAllText(full); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
            doc = Add(full, System.IO.Path.GetFileName(full), text, text);
        }
        Activate(doc, remember);
    }

    /// <summary>Не сохранённый файл (пример или новый): при сохранении получит свободное имя в папке студии.</summary>
    void OpenText(string text, string title) => Activate(Add(null, title, text, ""), remember: false);

    void Activate(Doc doc, bool remember = true)
    {
        _active = doc;
        _run = null;
        _verdict = null;
        if (remember) Remember();
        RenderExplorer();
        RenderTabs();
        RenderEditor();
        RenderPanel();
        RenderStatus();
        Dispatcher.UIThread.Post(doc.Editor.FocusText, DispatcherPriority.Background);
    }

    void NewFile()
    {
        var text = "mod \"" + I18n.T("v15.ms.newName") + "\"\nversion 1.0.0\nauthor \"" + (Social.Account.Get().Name ?? "") + "\"\n\nhook start {\n  say \"Hello from ModScript X\"\n}\n";
        OpenText(text, I18n.T("v15.ms.untitled"));
    }

    async Task OpenFile()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var type = new FilePickerFileType("ModScript X (.modx)") { Patterns = ["*" + MX.ModxDoc.Extension] };
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, FileTypeFilter = [type] });
        foreach (var f in files) if (f.TryGetLocalPath() is { } p) Open(p);
    }

    bool Save(Doc d)
    {
        try
        {
            if (d.Path is null)
            {
                var id = MX.ModxDoc.Parse(d.Editor.Text).Id.ToLowerInvariant();
                d.Path = CreatorPage.FreeModxPath(Workspace, id);
                d.Title = System.IO.Path.GetFileName(d.Path);
            }
            File.WriteAllText(d.Path, d.Editor.Text);
            d.Saved = d.Editor.Text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MainWindow.Current?.Toast(e.Message, bad: true);
            return false;
        }
        Remember();
        RenderExplorer();
        RenderTabs();
        RenderStatus();
        // «Следить»: каждое сохранение сразу ставит мод в игру заново.
        if (_watch && d == _active && GamePath is not null) _ = InjectNow(quiet: true);
        return true;
    }

    void Close(Doc d)
    {
        if (d.Dirty)
        {
            var w = MainWindow.Current;
            if (w is null) return;
            w.Dialog(I18n.T("v15.ms.close.title", ("file", d.Title)), Ui.Text(I18n.T("v15.ms.close.text"), "muted", wrap: true),
                Ui.Button(I18n.T("v15.ms.close.discard"), () => { w.CloseDialog(); d.Saved = d.Editor.Text; Close(d); }, "ghost"),
                Ui.Button(I18n.T("cr.save"), () => { w.CloseDialog(); if (Save(d)) Close(d); }, "primary", Icons.Save));
            return;
        }
        var at = _docs.IndexOf(d);
        _docs.Remove(d);
        if (_active == d) _active = _docs.Count == 0 ? null : _docs[Math.Clamp(at, 0, _docs.Count - 1)];
        Remember();
        RenderAll();
    }

    void RenderTabs()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var d in _docs)
        {
            var doc = d;
            var active = d == _active;
            var errors = d.Diags.Count(x => !x.Warning);
            var name = new TextBlock { Text = d.Title, FontFamily = Mono, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = active ? Ui.Res("Text") : Ui.Res("Muted") };
            var mark = d.Dirty ? Ui.Dot(Ui.Res("Warn"), 7) : errors > 0 ? Ui.Dot(Ui.Res("Bad"), 7) : Ui.Dot(Brushes.Transparent, 7);
            var close = new Button { Classes = { "icon", "ghost" }, Width = 22, Height = 22, Padding = new Thickness(0), Content = Ui.Icon(Icons.Close, 11, Ui.Res("Faint")), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(close, I18n.T("v15.ms.close") + "  (Ctrl+W)");
            close.Click += (_, e) => { e.Handled = true; Close(doc); };
            var tab = new Border
            {
                Padding = new Thickness(12, 7, 6, 7), Cursor = new Cursor(StandardCursorType.Hand),
                Background = active ? Ui.Res("Surface2") : Brushes.Transparent,
                BorderBrush = active ? Ui.Res("Brand") : Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 2),
                Child = Ui.Row(8, mark, name, close),
            };
            ToolTip.SetTip(tab, d.Path ?? I18n.T("v15.ms.unsaved"));
            tab.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(tab).Properties.IsMiddleButtonPressed) { Close(doc); return; }
                if (doc != _active) Activate(doc);
            };
            row.Children.Add(tab);
        }
        var plus = new Button { Classes = { "icon", "ghost" }, Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0), Content = Ui.Icon(Icons.Plus, 14, Ui.Res("Muted")), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(plus, I18n.T("v15.ms.new") + "  (Ctrl+N)");
        plus.Click += (_, _) => NewFile();
        row.Children.Add(plus);
        _tabs.Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = row };
        _tabs.BorderBrush = Ui.Res("Line");
        _tabs.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    void RenderEditor()
    {
        if (_active is { } d)
        {
            if (d.Editor.Parent is Decorator old && old != _editorHost) old.Child = null;
            _editorHost.Child = d.Editor;
            return;
        }
        // Пусто: что можно сделать и горячие клавиши.
        var keys = Ui.Col(6);
        foreach (var (k, key) in new[] { ("F5", "v15.ms.key.run"), ("F6", "v15.ms.key.inject"), ("Shift+F6", "v15.ms.key.eject"), ("Ctrl+S", "v15.ms.key.save"), ("Ctrl+F / Ctrl+H", "v15.ms.key.find"), ("Ctrl+Space", "v15.ms.key.suggest"), ("F8", "v15.ms.key.next") })
            keys.Children.Add(Ui.Row(12, new TextBlock { Text = k, FontFamily = Mono, FontSize = 12, Width = 130, Foreground = Ui.Res("Brand2") }, Ui.Text(I18n.T(key), "small muted")));
        _editorHost.Child = new Border
        {
            Padding = new Thickness(40, 36),
            Child = Ui.Col(16,
                Ui.Text(I18n.T("v15.ms.empty"), "h2"),
                Ui.Text(I18n.T("v15.ms.empty.text"), "muted", wrap: true),
                Ui.Row(10,
                    Ui.Button(I18n.T("v15.ms.new"), NewFile, "primary", Icons.FilePlus),
                    Ui.Button(I18n.T("v15.ms.open"), () => _ = OpenFile(), "", Icons.Folder),
                    Ui.Button(I18n.T("v15.ms.example"), () => OpenText(CreatorPage.ArenaExample(), "arena.modx"), "ghost", Icons.Sparkles)),
                keys),
        };
    }

    // ---------------------------------------------------------------- проводник и структура

    void RenderExplorer()
    {
        var col = new StackPanel { Spacing = 2, Margin = new Thickness(8, 10) };
        var head = new DockPanel { Margin = new Thickness(6, 0, 0, 6) };
        var tools = Ui.Row(2,
            Small(Icons.FilePlus, I18n.T("v15.ms.new") + "  (Ctrl+N)", NewFile),
            Small(Icons.Folder, I18n.T("v15.ms.open") + "  (Ctrl+O)", () => _ = OpenFile()),
            Small(Icons.External, I18n.T("v15.ms.reveal"), () => Actions.OpenFolder(Workspace)),
            Small(Icons.Refresh, I18n.T("cr.refresh"), RenderExplorer));
        DockPanel.SetDock(tools, Avalonia.Controls.Dock.Right);
        head.Children.Add(tools);
        head.Children.Add(Caption(I18n.T("v15.ms.files")));
        col.Children.Add(head);

        List<string> files;
        try { files = Directory.EnumerateFiles(Workspace, "*" + MX.ModxDoc.Extension, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).Take(300).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { files = []; }
        if (files.Count == 0) col.Children.Add(new TextBlock { Text = I18n.T("v15.ms.files.none"), FontSize = 12, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 8, 8) });
        foreach (var f in files)
        {
            var path = f;
            var open = _docs.FirstOrDefault(d => d.Path is { } p && string.Equals(p, f, StringComparison.OrdinalIgnoreCase));
            var row = FileRow(Icons.Code, System.IO.Path.GetRelativePath(Workspace, f), open is not null && open == _active, open?.Dirty == true, () => Open(path));
            row.ContextFlyout = Ctx.Menu(
                Ctx.Item(I18n.T("ctx.open"), Icons.Eye, () => Open(path)),
                Ctx.Item(I18n.T("v15.ms.rename"), Icons.Edit, () => Rename(path)),
                Ctx.Item(I18n.T("v15.ms.duplicate"), Icons.Copy, () => Duplicate(path)),
                Ctx.Folder(I18n.T("v15.ms.reveal"), System.IO.Path.GetDirectoryName(path)!),
                "-",
                Ctx.Item(I18n.T("ctx.delete"), Icons.Trash, () => Delete(path)));
            col.Children.Add(row);
        }

        col.Children.Add(new Border { Height = 12 });
        col.Children.Add(Caption(I18n.T("v15.ms.examples"), 6));
        foreach (var (name, title) in new[] { ("arena", "Arena Rewards"), ("gold-boost", "Gold Boost") })
        {
            var n = name;
            col.Children.Add(FileRow(Icons.Sparkles, title, false, false, () => OpenText(CreatorPage.Example(n), n + MX.ModxDoc.Extension)));
        }

        if (_active is { } doc && Outline(doc.Editor.Text) is { Count: > 0 } outline)
        {
            col.Children.Add(new Border { Height = 12 });
            col.Children.Add(Caption(I18n.T("v15.ms.outline"), 6));
            foreach (var (line, kind, name) in outline)
            {
                var l = line;
                var icon = kind switch { "hook" => Icons.Zap, "fn" => Icons.Code, "native" => Icons.Tools, _ => Icons.Info };
                col.Children.Add(FileRow(icon, kind == "mod" ? name : $"{kind} {name}", false, false, () => doc.Editor.GoToLine(l), l.ToString()));
            }
        }
        _explorer.Child = new ScrollViewer { Content = col };
    }

    static Control Caption(string text, double left = 0) => new TextBlock
    {
        Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, Foreground = Ui.Res("Faint"),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 0, 0),
    };

    static Button Small(string icon, string tip, Action run)
    {
        var b = new Button { Classes = { "icon", "ghost" }, Width = 26, Height = 26, Padding = new Thickness(0), Content = Ui.Icon(icon, 13, Ui.Res("Muted")) };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => run();
        return b;
    }

    static Button FileRow(string icon, string text, bool active, bool dirty, Action open, string? right = null)
    {
        var line = new DockPanel();
        if (right is not null || dirty)
        {
            var tail = right is not null ? (Control)new TextBlock { Text = right, FontSize = 11, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center } : Ui.Dot(Ui.Res("Warn"), 7);
            DockPanel.SetDock(tail, Avalonia.Controls.Dock.Right);
            line.Children.Add(tail);
        }
        line.Children.Add(Ui.Row(8, Ui.Icon(icon, 13, active ? Ui.Res("Brand2") : Ui.Res("Muted")),
            new TextBlock { Text = text, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = active ? Ui.Res("Text") : Ui.Res("Muted") }));
        var b = new Button
        {
            Classes = { "ghost" }, Padding = new Thickness(8, 5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = active ? Ui.Res("Surface2") : Brushes.Transparent, Content = line,
        };
        b.Click += (_, _) => open();
        return b;
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

    void Rename(string path)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var box = new TextBox { Text = System.IO.Path.GetFileNameWithoutExtension(path), FontFamily = Mono };
        w.Dialog(I18n.T("v15.ms.rename"), box,
            Ui.Button(I18n.T("cr.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("v15.ms.rename"), () =>
            {
                var name = Regex.Replace(box.Text ?? "", @"[\\/:*?""<>|]", "").Trim();
                if (name == "") return;
                var to = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, name + MX.ModxDoc.Extension);
                try
                {
                    if (!string.Equals(to, path, StringComparison.OrdinalIgnoreCase)) File.Move(path, to);
                    foreach (var d in _docs.Where(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase))) { d.Path = to; d.Title = System.IO.Path.GetFileName(to); }
                    w.CloseDialog();
                    Remember();
                    RenderAll();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { w.Toast(e.Message, bad: true); }
            }, "primary", Icons.Edit));
    }

    void Duplicate(string path)
    {
        try
        {
            var to = CreatorPage.FreeModxPath(System.IO.Path.GetDirectoryName(path)!, System.IO.Path.GetFileNameWithoutExtension(path));
            File.Copy(path, to);
            Open(to);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); }
    }

    void Delete(string path)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        w.Dialog(I18n.T("v15.ms.delete.title", ("file", System.IO.Path.GetFileName(path))), Ui.Text(I18n.T("v15.ms.delete.text"), "muted", wrap: true),
            Ui.Button(I18n.T("cr.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("ctx.delete"), () =>
            {
                w.CloseDialog();
                try { File.Delete(path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { w.Toast(e.Message, bad: true); return; }
                foreach (var d in _docs.Where(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)).ToList()) { d.Saved = d.Editor.Text; Close(d); }
                RenderExplorer();
            }, "danger", Icons.Trash));
    }

    // ---------------------------------------------------------------- проверка на лету

    void Schedule()
    {
        _analyze?.Stop();
        _analyze = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _analyze.Tick += (_, _) =>
        {
            _analyze!.Stop();
            if (_active is not { } d) return;
            Analyze(d);
            RenderTabs();
            RenderExplorer();
            if (_panel is "problems" or "code") RenderPanel();
            else RenderPanelTabs();
            RenderStatus();
        };
        _analyze.Start();
    }

    static void Analyze(Doc d)
    {
        var doc = MX.ModxDoc.Parse(d.Editor.Text);
        d.Diags = [.. doc.Diags.OrderBy(x => x.Warning).ThenBy(x => x.Line)];
        d.Editor.MarkErrors(d.Diags.Where(x => !x.Warning && x.Line > 0).Select(x => x.Line));
    }

    void NextProblem()
    {
        if (_active is not { } d || d.Diags.Count == 0) return;
        var next = d.Diags.Where(x => x.Line > d.Editor.CaretLine).Select(x => x.Line).DefaultIfEmpty(d.Diags[0].Line).First();
        d.Editor.GoToLine(Math.Max(1, next));
        _panel = "problems";
        RenderPanel();
    }

    // ---------------------------------------------------------------- панель инструментов

    void RenderToolbar()
    {
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var pick = new ComboBox { MinWidth = 180, MaxWidth = 240, PlaceholderText = I18n.T("v11.lx.pickGame"), VerticalAlignment = VerticalAlignment.Center };
        foreach (var g in games) pick.Items.Add(new ComboBoxItem { Content = g.Def.Name, Tag = g.Def.Id });
        pick.SelectedItem = pick.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == GameId);
        pick.SelectionChanged += (_, _) => { if (pick.SelectedItem is ComboBoxItem { Tag: string id } && id != GameId) { GameId = id; RenderPanel(); RenderStatus(); } };
        ToolTip.SetTip(pick, I18n.T("v15.ms.game.tip"));

        var engine = new ComboBox { MinWidth = 140, MaxWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        engine.Items.Add(new ComboBoxItem { Content = I18n.T("v15.ms.engine.auto"), Tag = "auto" });
        foreach (var t in MX.Target.All) engine.Items.Add(new ComboBoxItem { Content = t.Title, Tag = t.Id });
        engine.SelectedItem = engine.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == Engine) ?? engine.Items[0];
        engine.SelectionChanged += (_, _) => { if (engine.SelectedItem is ComboBoxItem { Tag: string id } && id != Engine) { Engine = id; RenderPanel(); RenderStatus(); } };
        ToolTip.SetTip(engine, I18n.T("v15.ms.engine.tip"));

        var run = Ui.Button(I18n.T("v15.ms.run"), Run, "", Icons.Play, I18n.T("v15.ms.run.tip") + "  (F5)");
        var inject = Ui.Button(_busy ? I18n.T("v15.ms.injecting") : I18n.T("v15.ms.inject"), () => _ = InjectNow(), "primary", Icons.Zap, I18n.T("v15.ms.inject.tip") + "  (F6)");
        inject.IsEnabled = !_busy;
        var eject = Ui.Button("", () => _ = EjectNow(), "icon", Icons.Undo, I18n.T("v15.ms.eject.tip") + "  (Shift+F6)");
        var watch = new ToggleButton
        {
            Classes = { "chip" }, IsChecked = _watch, VerticalAlignment = VerticalAlignment.Center,
            Content = Ui.Row(6, Ui.Icon(Icons.Eye, 13), new TextBlock { Text = I18n.T("v15.ms.watch"), VerticalAlignment = VerticalAlignment.Center }),
        };
        ToolTip.SetTip(watch, I18n.T("v15.ms.watch.tip"));
        watch.IsCheckedChanged += (_, _) => { _watch = watch.IsChecked == true; RenderStatus(); };
        var play = Ui.Button("", () => _ = PlayWithInject(), "icon", Icons.Gamepad, I18n.T("v15.ms.play") + " — " + I18n.T("v15.ms.play.tip"));
        var save = Ui.Button("", () => { if (_active is { } d) Save(d); }, "icon", Icons.Save, I18n.T("cr.save") + "  (Ctrl+S)");
        var more = Ui.Button("", () => { }, "icon ghost", Icons.More, I18n.T("top.more"));
        more.Flyout = Ctx.Menu(
            Ctx.Item(I18n.T("v15.ms.open"), Icons.Folder, () => _ = OpenFile(), gesture: "Ctrl+O"),
            Ctx.Item(I18n.T("v15.ms.find"), Icons.Search, () => _active?.Editor.Find(), gesture: "Ctrl+F"),
            Ctx.Item(I18n.T("v15.ms.replace"), Icons.Edit, () => _active?.Editor.Find(true), gesture: "Ctrl+H"),
            "-",
            Ctx.Item(I18n.T("v11.lx.check"), Icons.Shield, Check),
            Ctx.Item(I18n.T("v15.ms.attach"), Icons.Layers, () => Attach()),
            Ctx.Item(I18n.T("v11.lx.sell"), Icons.Bag, Sell),
            "-",
            Ctx.Item(I18n.T("v15.ms.library"), Icons.Zap, () => MainWindow.Current?.Navigate(() => new CreatorPage("injects"))),
            Ctx.Folder(I18n.T("v15.ms.reveal"), Workspace));

        // Слева — что делать с модом (запуск, инжект), справа — куда (игра, движок) и файл.
        var left = Ui.Row(8, run, inject, eject, watch, play);
        left.VerticalAlignment = VerticalAlignment.Center;
        var right = Ui.Row(8, pick, engine, save, more);
        right.VerticalAlignment = VerticalAlignment.Center;
        var bar = new DockPanel();
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(left);
        _toolbar.Child = bar;
    }

    // ---------------------------------------------------------------- нижняя панель

    void RenderPanelTabs()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 4) };
        var problems = _active?.Diags.Count ?? 0;
        foreach (var (id, key, icon) in new[] { ("problems", "v15.ms.p.problems", Icons.Alert), ("output", "v15.ms.p.output", Icons.Play), ("code", "v15.ms.p.code", Icons.Code), ("inject", "v15.ms.p.inject", Icons.Zap) })
        {
            var tab = id;
            var content = Ui.Row(6, Ui.Icon(icon, 13, _panel == id ? Ui.Res("Brand2") : Ui.Res("Muted")), new TextBlock { Text = I18n.T(key), VerticalAlignment = VerticalAlignment.Center });
            if (id == "problems" && problems > 0)
                content.Children.Add(new Border { Classes = { "tab-badge" }, Child = Ui.Text(problems.ToString(), "tiny") });
            var b = new Button { Classes = { "tab" }, Content = content };
            if (_panel == id) b.Classes.Add("active");
            b.Click += (_, _) => { _panel = tab; RenderPanel(); };
            row.Children.Add(b);
        }
        _panelTabs.Child = row;
        _panelTabs.BorderBrush = Ui.Res("Line");
        _panelTabs.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    void RenderPanel()
    {
        RenderPanelTabs();
        _panelBody.Child = _panel switch
        {
            "output" => OutputPane(),
            "code" => CodePane(),
            "inject" => InjectPane(),
            _ => ProblemsPane(),
        };
    }

    Control ProblemsPane()
    {
        var col = new StackPanel { Spacing = 2, Margin = new Thickness(10, 8) };
        if (_active is not { } d) return col;
        if (d.Diags.Count == 0)
        {
            col.Children.Add(Ui.Row(8, Ui.Icon(Icons.Check, 14, Ui.Res("Good")), Ui.Text(I18n.T("v15.ms.noProblems"), "small", color: Ui.Res("Good"))));
            return col;
        }
        foreach (var x in d.Diags)
        {
            var line = x.Line;
            var row = Ui.Row(10,
                Ui.Icon(x.Warning ? Icons.Alert : Icons.Close, 13, x.Warning ? Ui.Res("Warn") : Ui.Res("Bad")),
                new TextBlock { Text = line > 0 ? I18n.T("v15.ms.line", ("n", line)) : "—", FontFamily = Mono, FontSize = 12, Width = 70, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = CreatorPage.LxMsg(x), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(6, 4), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = row };
            b.Click += (_, _) => { if (line > 0) d.Editor.GoToLine(line); };
            col.Children.Add(b);
        }
        return new ScrollViewer { Content = col };
    }

    Control OutputPane()
    {
        var vars = new TextBox { Text = _vars, AcceptsReturn = true, FontFamily = Mono, FontSize = 12, Height = 92, TextWrapping = TextWrapping.NoWrap, VerticalContentAlignment = VerticalAlignment.Top };
        vars.TextChanged += (_, _) => _vars = vars.Text ?? "";
        var ticks = new NumericUpDown { Value = _ticks, Minimum = 0, Maximum = 100, Increment = 1, FormatString = "0", Width = 110 };
        ticks.ValueChanged += (_, _) => _ticks = (int)(ticks.Value ?? 3);
        var ticksRow = Ui.Row(8, Ui.Text(I18n.T("v11.lx.ticks"), "tiny muted"), ticks, Ui.Button(I18n.T("v15.ms.run"), Run, "primary", Icons.Play, "F5"));
        foreach (var c in ticksRow.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var varsTitle = new TextBlock { Text = I18n.T("v11.lx.vars"), FontSize = 11, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(varsTitle, I18n.T("v11.lx.vars"));
        var left = Ui.Col(6, ticksRow, varsTitle, vars);
        left.Width = 370;

        var right = new StackPanel { Spacing = 4 };
        if (_run is null) right.Children.Add(Ui.Text(I18n.T("v15.ms.output.none"), "small muted", wrap: true));
        else
        {
            right.Children.Add(Ui.Row(8, Ui.Icon(_run.Ok ? Icons.Check : Icons.Alert, 13, _run.Ok ? Ui.Res("Good") : Ui.Res("Bad")),
                Ui.Text(I18n.T(_run.Ok ? "v11.lx.ran" : "v11.lx.failed", ("steps", _run.Steps)), "small", color: _run.Ok ? Ui.Res("Good") : Ui.Res("Bad"))));
            var text = new StringBuilder();
            foreach (var o in _run.Output) text.Append(o).Append('\n');
            foreach (var e in _run.Errors) text.Append(e.Line > 0 ? $"! {I18n.T("v15.ms.line", ("n", e.Line))}: " : "! ").Append(CreatorPage.LxMsg(e)).Append('\n');
            right.Children.Add(new SelectableTextBlock { Text = text.ToString().TrimEnd(), FontFamily = Mono, FontSize = 12, LineHeight = 18, TextWrapping = TextWrapping.Wrap });
            if (_run.Vars.Count > 0)
                right.Children.Add(new SelectableTextBlock
                {
                    Text = string.Join("   ", _run.Vars.Select(kv => $"{kv.Key} = {MX.Sandbox.Show(kv.Value)}")), FontFamily = Mono, FontSize = 12,
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

    /// <summary>Движок для «Кода» и инжекта: выбранный, иначе из файла (engine), иначе узнанный по папке игры, иначе Ren'Py.</summary>
    MX.Target TargetFor(MX.ModxDoc doc) =>
        (Engine != "auto" ? MX.Target.Find(Engine) : null)
        ?? (doc.Engine is { } e ? MX.Target.Find(e) : null)
        ?? (GamePath is { } p ? SafeDetect(p) : null)
        ?? MX.Target.Find("renpy")!;

    static MX.Target? SafeDetect(string dir)
    {
        try { return MX.Engines.Detect(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    Control CodePane()
    {
        if (_active is not { } d) return new Border();
        var doc = MX.ModxDoc.Parse(d.Editor.Text);
        var t = TargetFor(doc);
        var text = doc.Ok ? EngineCode(doc, t, null) : I18n.T("v11.lx.fix");
        var head = Ui.Row(8, Ui.Text($"{t.Title} · {t.Language}", "small strong"), Ui.Text(I18n.T("v11.lx.code.hint"), "tiny muted"));
        var copy = Ui.Button("", () => _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text), "icon ghost", Icons.Copy, I18n.T("ctx.copy"));
        var bar = new DockPanel { Margin = new Thickness(12, 8, 8, 4) };
        DockPanel.SetDock(copy, Avalonia.Controls.Dock.Right);
        bar.Children.Add(copy);
        bar.Children.Add(head);
        var code = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 4, 8),
            Content = new SelectableTextBlock { Text = text, FontFamily = Mono, FontSize = 12, LineHeight = 18 },
        };
        return new DockPanel { Children = { Dock(bar, Avalonia.Controls.Dock.Top), code } };
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

    Control InjectPane()
    {
        var g = Game;
        var path = GamePath;
        var left = Ui.Col(8);
        if (g is null) left.Children.Add(Ui.Text(I18n.T("v15.ms.inj.pick"), "small muted", wrap: true));
        else if (path is null) left.Children.Add(Ui.Text(I18n.T("v15.ms.inj.notFound", ("game", g.Def.Name)), "small", color: Ui.Res("Warn"), wrap: true));
        else
        {
            string? ac = null;
            try { ac = MX.Engines.FindAntiCheat(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            var detected = SafeDetect(path);
            left.Children.Add(Ui.Text(g.Def.Name, "h3"));
            left.Children.Add(new TextBlock { Text = path, FontSize = 12, Foreground = Ui.Res("Faint"), TextTrimming = TextTrimming.CharacterEllipsis });
            var buttons = new WrapPanel();
            foreach (var b in new[]
            {
                Ui.Button(_busy ? I18n.T("v15.ms.injecting") : I18n.T("v15.ms.inject"), () => _ = InjectNow(), "primary", Icons.Zap, "F6"),
                Ui.Button(I18n.T("v15.ms.eject"), () => _ = EjectNow(), "", Icons.Undo, "Shift+F6"),
                Ui.Button(I18n.T("v15.ms.attach"), () => Attach(), "ghost", Icons.Layers, I18n.T("v15.ms.attach.tip")),
            })
            {
                b.Margin = new Thickness(0, 2, 8, 4);
                buttons.Children.Add(b);
            }
            left.Children.Add(buttons);
            var dot = Ui.Dot(ac is not null ? Ui.Res("Bad") : detected is null ? Ui.Res("Warn") : Ui.Res("Good"));
            dot.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(dot, Avalonia.Controls.Dock.Left);
            left.Children.Add(new DockPanel { Children = { dot,
                Ui.Text(ac is not null ? I18n.T("v15.ms.inj.anticheat", ("ac", ac)) : detected is null ? I18n.T("v15.ms.inj.engine.unknown") : I18n.T("v15.ms.inj.engine", ("engine", detected.Title)), "small", wrap: true) } });
            if (detected?.Id == "unity" && !Directory.Exists(Path.Combine(path, "BepInEx", "core")))
                left.Children.Add(Ui.Text(I18n.T("v15.inj.noBepinex"), "small", color: Ui.Res("Warn"), wrap: true));
            var installed = new List<string>();
            try { installed = MX.Injector.Installed(path).ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            left.Children.Add(Ui.Text(installed.Count == 0 ? I18n.T("v15.ms.inj.nothing") : I18n.T("v15.ms.inj.installed", ("list", string.Join(", ", installed))), "small muted", wrap: true));
        }
        if (_verdict is { } v) left.Children.Add(CreatorPage.VerdictView(v));
        left.Width = 430;

        var log = new StackPanel { Spacing = 3 };
        if (_log.Count == 0) log.Children.Add(Ui.Text(I18n.T("v15.ms.inj.log.empty"), "small muted", wrap: true));
        foreach (var (at, ok, text) in _log.Skip(Math.Max(0, _log.Count - 60)))
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
        if (_active is not { } d) return;
        var doc = MX.ModxDoc.Parse(d.Editor.Text);
        _panel = "output";
        if (!doc.Ok) { _panel = "problems"; RenderPanel(); MainWindow.Current?.Toast(I18n.T("v11.lx.fix"), bad: true); return; }
        var errors = new List<Diag>();
        var vars = MX.Sandbox.ParseVars(_vars, errors);
        _run = MX.Sandbox.Run(doc, vars, _ticks);
        foreach (var e in errors) _run.Errors.Add(new Diag(e.Line, I18n.T("v11.lx.varsBad") + ": " + CreatorPage.LxMsg(e), true));
        RenderPanel();
    }

    void Log(bool ok, string text)
    {
        _log.Add((DateTime.UtcNow, ok, text));
        if (_log.Count > 200) _log.RemoveRange(0, _log.Count - 200);
    }

    /// <summary>F6: перевести и поставить мод в папку выбранной игры сейчас (в фоне: сборка C# для Unity занимает секунды).</summary>
    async Task InjectNow(bool quiet = false)
    {
        if (_active is not { } d || _busy) return;
        var w = MainWindow.Current;
        if (Game is not { } g || GamePath is not { } path) { _panel = "inject"; RenderPanel(); w?.Toast(I18n.T("v15.ms.inj.pick"), bad: true); return; }
        var source = d.Editor.Text;
        var engine = Engine == "auto" ? null : Engine;
        _busy = true;
        _panel = "inject";
        RenderToolbar();
        RenderPanel();
        try
        {
            var v = Moderation.CheckModx(source);
            _verdict = v.Ok && v.Clean ? null : v;
            if (!v.Ok) { Log(false, I18n.T("v15.ms.inj.blocked")); return; }
            var r = await Task.Run(() => MX.Injector.Inject(source, path, engine));
            if (!r.Doc.Ok || r.Target is null)
            {
                foreach (var x in r.Doc.Diags.Where(x => !x.Warning)) Log(false, (x.Line > 0 ? I18n.T("v15.ms.line", ("n", x.Line)) + ": " : "") + CreatorPage.LxMsg(x));
                if (!quiet) w?.Toast(I18n.T("v15.ms.inj.failed"), bad: true);
                return;
            }
            var built = r.Target.Id != "unity" || Features.Inject.Built(path, r.Doc.Id);
            Log(built, I18n.T(built ? "v15.ms.inj.done" : "v15.ms.inj.notBuilt", ("mod", r.Doc.Name), ("game", g.Def.Name), ("engine", r.Target.Title), ("n", r.Files.Count)));
            foreach (var f in r.Files.Take(8)) Log(true, "  + " + f);
            if (r.Note is { } note) foreach (var line in note.Split('\n').Where(l => l.Trim() != "").Take(20)) Log(built, "  " + line.Trim());
            if (!quiet) w?.Toast(built ? I18n.T("v15.ms.inj.toast", ("game", g.Def.Name)) : I18n.T("v15.ms.inj.failed"), bad: !built);
        }
        catch (Exception e)
        {
            Log(false, e.Message);
            if (!quiet) w?.Toast(e.Message, bad: true);
        }
        finally
        {
            _busy = false;
            RenderToolbar();
            RenderPanel();
        }
    }

    async Task EjectNow()
    {
        if (_active is not { } d || GamePath is not { } path) { _panel = "inject"; RenderPanel(); return; }
        var id = MX.ModxDoc.Parse(d.Editor.Text).Id;
        try
        {
            var removed = await Task.Run(() => MX.Injector.Eject(id, path));
            Log(true, I18n.T("v15.ms.inj.ejected", ("n", removed.Count)));
            MainWindow.Current?.Toast(I18n.T("v15.ms.inj.ejected", ("n", removed.Count)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log(false, e.Message); }
        _panel = "inject";
        RenderPanel();
    }

    /// <summary>В библиотеку инжектов этой игры: ставится на каждом «Играть» и снимается после игры.</summary>
    Features.InjectItem? Attach(bool quiet = false)
    {
        if (_active is not { } d) return null;
        if (Game is not { } g) { _panel = "inject"; RenderPanel(); MainWindow.Current?.Toast(I18n.T("v15.ms.inj.pick"), bad: true); return null; }
        if (!Save(d)) return null;
        var (item, v) = Features.Inject.AddLocal(d.Path!, g.Def.Id);
        _verdict = v;
        if (item is not null)
        {
            Log(true, I18n.T("v11.lx.attached", ("title", item.Title), ("game", g.Def.Name)));
            if (!quiet) MainWindow.Current?.Toast(I18n.T("v11.lx.attached", ("title", item.Title), ("game", g.Def.Name)));
        }
        else Log(false, I18n.T("v15.ms.inj.blocked"));
        _panel = "inject";
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
        if (_active is not { } d) return;
        _verdict = Moderation.CheckModx(d.Editor.Text);
        _panel = "inject";
        RenderPanel();
    }

    void Sell()
    {
        if (_active is not { } d) return;
        var v = Moderation.CheckModx(d.Editor.Text);
        _verdict = v;
        if (!v.Ok || !Save(d)) { _panel = "inject"; RenderPanel(); return; }
        ListingWizard.Show(null, MarketData.Reload, d.Path, "script");
    }

    // ---------------------------------------------------------------- строка состояния и клавиши

    void RenderStatus()
    {
        var parts = new List<string>();
        if (_active is { } d)
        {
            parts.Add(I18n.T("v15.ms.caret", ("line", _caret.Line), ("col", _caret.Col)));
            parts.Add(d.Path is null ? I18n.T("v15.ms.unsaved") : d.Dirty ? I18n.T("v15.ms.modified") : I18n.T("v15.ms.saved"));
            var errors = d.Diags.Count(x => !x.Warning);
            parts.Add(errors == 0 ? I18n.T("v15.ms.noProblems") : I18n.T("v15.ms.errors", ("n", errors)));
            parts.Add(TargetFor(MX.ModxDoc.Parse(d.Editor.Text)).Title);
        }
        if (Game is { } g) parts.Add(g.Def.Name);
        if (_watch) parts.Add(I18n.T("v15.ms.watching"));
        var line = new TextBlock { Text = string.Join("   ·   ", parts), FontSize = 12, Foreground = Ui.Res("Faint"), FontFamily = Mono, TextTrimming = TextTrimming.CharacterEllipsis };
        var keys = new TextBlock { Text = "F5 · F6 · Ctrl+S · Ctrl+F · Ctrl+Space", FontSize = 12, Foreground = Ui.Res("Faint"), FontFamily = Mono };
        var bar = new DockPanel();
        DockPanel.SetDock(keys, Avalonia.Controls.Dock.Right);
        bar.Children.Add(keys);
        bar.Children.Add(line);
        _status.Child = bar;
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.F5: Run(); break;
            case Key.F6 when shift: _ = EjectNow(); break;
            case Key.F6: _ = InjectNow(); break;
            case Key.F8: NextProblem(); break;
            case Key.S when ctrl: if (_active is { } d) Save(d); break;
            case Key.N when ctrl: NewFile(); break;
            case Key.O when ctrl: _ = OpenFile(); break;
            case Key.W when ctrl: if (_active is { } c) Close(c); break;
            case Key.F when ctrl: _active?.Editor.Find(); break;
            case Key.H when ctrl: _active?.Editor.Find(true); break;
            case Key.Tab when ctrl && _docs.Count > 1:
                var i = _active is null ? 0 : _docs.IndexOf(_active);
                Activate(_docs[(i + (shift ? _docs.Count - 1 : 1)) % _docs.Count]);
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- снимки и самотест

    /// <summary>Для снимков экрана: пример «Арена», игра с инжектом, запуск и журнал.</summary>
    internal static void Demo(string panel)
    {
        var s = _shared ??= new ModStudio();
        foreach (var d in s._docs.ToList()) s._docs.Remove(d);
        s.OpenText(CreatorPage.ArenaExample(), "arena.modx");
        s.OpenText(CreatorPage.Example("gold-boost"), "gold-boost.modx");
        s._active = s._docs[0];
        s._panel = panel;
        if (AppState.Games.FirstOrDefault(g => g.Status == Detect.Found && !g.Def.IsMinecraft) is { } game) s.GameId = game.Def.Id;
        if (panel == "output") s.Run();
        if (panel == "inject" && s._log.Count == 0)
        {
            s.Log(true, I18n.T("v15.ms.inj.done", ("mod", "Arena Rewards"), ("game", s.Game?.Def.Name ?? "Game"), ("engine", "Unity (BepInEx 5)"), ("n", 2)));
            s.Log(true, "  + BepInEx/modscript/Arena_Rewards/ModScript_Arena_Rewards.cs");
            s.Log(true, "  built into BepInEx/plugins/ModScript_Arena_Rewards");
        }
        s._panel = panel;
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
}
