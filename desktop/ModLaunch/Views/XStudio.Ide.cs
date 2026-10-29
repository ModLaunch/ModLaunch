using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Creator;
using Insight = ModLaunch.Creator.ModxInsight;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 19.0: среда ModScript X как настоящая IDE. Отладчик с записью хода (точки останова F9, шаги F10/F11 вперёд и
/// Shift+F10 назад, ползунок по всем шагам, переменные, стек, вывод к шагу, горячие строки), переход к
/// определению F12, все ссылки Shift+F12, переименование F2, выравнивание Shift+Alt+F, поиск по проекту
/// Ctrl+Shift+F, палитра Ctrl+P (файлы, &gt; команды, @ символы, : строка), история файла с откатом и проблемы
/// с быстрыми исправлениями.
/// </summary>
public sealed partial class XStudio
{
    readonly Border _overlay = new() { IsVisible = false };
    TextBlock? _dbgPos;
    Border? _dbgBody;

    // ---------------------------------------------------------------- понимание кода: определение, ссылки, имя, вид

    void Ide(string command)
    {
        switch (command)
        {
            case "definition": Definition(); break;
            case "references": FindReferences(); break;
            case "rename": RenameSymbol(); break;
            case "format": FormatCode(); break;
        }
    }

    (Doc Doc, Insight.Model Model, int Line, int Col)? Here(int? offset = null)
    {
        if (_s.Active is not { Modx: true } d) return null;
        var (line, col) = offset is { } o ? d.Editor.PositionOf(o) : (d.Editor.CaretLine, d.Editor.CaretColumn);
        return (d, d.Editor.Model, line, col);
    }

    void Definition(int? offset = null)
    {
        if (Here(offset) is not { } h) return;
        var s = Insight.At(h.Model, h.Line, h.Col);
        if (s is { Line: > 0 }) { h.Doc.Editor.Select(s.Line, s.Col, s.Name.Length); return; }
        if (s is not null && Insight.Hover(h.Model, h.Line, h.Col) is { } info) MainWindow.Current?.Toast(info.Title + " — " + info.Text.Split('\n')[0]);
    }

    void FindReferences()
    {
        if (Here() is not { } h || Insight.At(h.Model, h.Line, h.Col) is not { } s || s.Kind is Insight.Kind.Keyword or Insight.Kind.Event) return;
        var path = h.Doc.Path;
        _s.Found = [.. Insight.References(h.Model, s).Select(r => (path, r.Line, r.Col, r.Length, h.Model.Lines[r.Line - 1].Trim(), r.Write))];
        _s.FoundTitle = I18n.T("v19.refs.title", ("n", _s.Found.Count), ("name", s.Name));
        _s.Panel = "refs";
        RenderPanel();
    }

    void RenameSymbol()
    {
        var w = MainWindow.Current;
        if (w is null || Here() is not { } h) return;
        if (Insight.At(h.Model, h.Line, h.Col) is not { Renamable: true } s) { w.Toast(I18n.T("v19.rename.none")); return; }
        var count = Insight.References(h.Model, s).Count;
        var box = new TextBox { Text = s.Name, FontFamily = Mono, MinWidth = 320 };
        var error = new TextBlock { FontSize = 12, Foreground = Ui.Res("Bad"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        void Apply()
        {
            var (text, err) = Insight.Rename(h.Doc.Editor.Text, s, box.Text ?? "");
            if (err is not null || text is null) { error.Text = I18n.T(err ?? "v19.rename.bad"); error.IsVisible = true; return; }
            h.Doc.Editor.Apply(text);
            w.CloseDialog();
            w.Toast(I18n.T("v19.rename.done", ("n", count), ("name", box.Text?.Trim() ?? "")));
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Apply(); } };
        w.Dialog(I18n.T("v19.rename.title", ("name", s.Name)),
            Ui.Col(10, Ui.Text(I18n.T("v19.rename.text", ("n", count), ("kind", I18n.T($"v19.kind.{s.Kind.ToString().ToLowerInvariant()}"))), "muted", wrap: true), box, error),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("v19.cmd.rename"), Apply, "primary", Icons.Edit));
        Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Background);
    }

    void FormatCode()
    {
        if (_s.Active is not { Modx: true } d) return;
        if (d.Editor.Format()) MainWindow.Current?.Toast(I18n.T("v19.format.done"));
        else MainWindow.Current?.Toast(I18n.T("v19.format.unsafe"), bad: true);
    }

    // ---------------------------------------------------------------- поиск по проекту

    void SearchProject(string query)
    {
        _s.SearchText = query;
        _s.Found = [];
        if (_p is null || query.Trim().Length < 2) { _s.FoundTitle = ""; return; }
        var files = new List<string>();
        try
        {
            files = [.. Directory.EnumerateFiles(_p.Dir).Where(f => f.EndsWith(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var d in _s.Docs) if (d.Path is not null && !files.Any(f => Same(f, d.Path))) files.Add(d.Path);
        foreach (var f in files)
        {
            string text;
            if (_s.Docs.FirstOrDefault(d => Same(d.Path, f)) is { } open) text = open.Editor.Text;
            else
                try { text = File.ReadAllText(f); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length && _s.Found.Count < 300; i++)
                for (var at = lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase); at >= 0 && _s.Found.Count < 300; at = lines[i].IndexOf(query, at + query.Length, StringComparison.OrdinalIgnoreCase))
                    _s.Found.Add((f, i + 1, at, query.Length, lines[i].Trim(), false));
        }
        _s.FoundTitle = I18n.T("v19.search.found", ("n", _s.Found.Count), ("files", _s.Found.Select(x => x.Path).Distinct().Count()));
    }

    void GoTo(string? path, int line, int col, int length)
    {
        if (path is not null && !Same(_s.Active?.Path, path)) Open(path);
        _s.Active?.Editor.Select(line, col, length);
    }

    Control FoundPane(bool search)
    {
        var col = new StackPanel { Spacing = 1, Margin = new Thickness(10, 8) };
        if (search)
        {
            var box = new TextBox { Text = _s.SearchText, Watermark = I18n.T("v19.search.hint"), FontFamily = Mono, MinWidth = 340 };
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                SearchProject(box.Text ?? "");
                RenderPanel();
            };
            var go = Ui.Button(I18n.T("v19.search.go"), () => { SearchProject(box.Text ?? ""); RenderPanel(); }, "", Icons.Search);
            col.Children.Add(Ui.Row(8, box, go));
            Dispatcher.UIThread.Post(() => { if (box.IsAttachedToVisualTree()) box.Focus(); }, DispatcherPriority.Background);
        }
        if (_s.FoundTitle != "") col.Children.Add(new Border { Padding = new Thickness(6, 8, 6, 4), Child = Ui.Text(_s.FoundTitle, "small strong") });
        foreach (var group in _s.Found.GroupBy(x => x.Path))
        {
            if (search) col.Children.Add(new Border { Padding = new Thickness(6, 6, 6, 2), Child = Ui.Row(6, Ui.Icon(Icons.Code, 12, Color), HubLook.Code(System.IO.Path.GetFileName(group.Key ?? ""), 11, Ui.Res("Muted"))) });
            foreach (var f in group)
            {
                var hit = f;
                var row = Ui.Row(10,
                    new TextBlock { Text = I18n.T("v15.ms.line", ("n", f.Line)), FontFamily = Mono, FontSize = 12, Width = 78, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center },
                    Ui.Icon(f.Write ? Icons.Edit : Icons.Eye, 12, f.Write ? HubLook.Violet : Ui.Res("Faint")),
                    new TextBlock { Text = f.Preview, FontFamily = Mono, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var b = new Button { Classes = { "ghost" }, Padding = new Thickness(6, 3), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = row };
                b.Click += (_, _) => GoTo(hit.Path, hit.Line, hit.Col, hit.Length);
                col.Children.Add(b);
            }
        }
        if (!search && _s.Found.Count == 0) col.Children.Add(Ui.Text(I18n.T("v19.refs.none"), "small muted", wrap: true));
        return new ScrollViewer { Content = col };
    }

    // ---------------------------------------------------------------- проблемы и быстрые исправления

    Control ProblemsPane()
    {
        var col = new StackPanel { Spacing = 2, Margin = new Thickness(10, 8) };
        if (_s.Active is not { } d) return col;
        if (d.Diags.Count == 0 && d.Lints.Count == 0)
        {
            col.Children.Add(Ui.Row(8, Ui.Icon(Icons.Check, 14, Ui.Res("Good")), Ui.Text(I18n.T("v15.ms.noProblems"), "small", color: Ui.Res("Good"))));
            if (d.Modx) col.Children.Add(Ui.Text(I18n.T("v16.x.ready", ("engine", TargetFor(ActiveDoc()).Title)), "small muted", wrap: true));
            return col;
        }
        var all = d.Diags.Select(x => (x.Line, Col: 0, Length: 0, Text: CreatorPage.LxMsg(x), x.Warning, Fix: (string?)null))
            .Concat(d.Lints.Select(x => (x.Line, x.Col, x.Length, Text: x.Message, x.Warning, x.Fix)))
            .OrderBy(x => x.Warning).ThenBy(x => x.Line);
        foreach (var x in all)
        {
            var p = x;
            var row = new DockPanel();
            if (x.Fix is { } fix)
            {
                var apply = Ui.Button(I18n.T("v19.fix", ("fix", fix)), () => QuickFix(d, p.Line, p.Col, p.Length, fix), "ghost", Icons.Wand);
                apply.Padding = new Thickness(8, 2);
                DockPanel.SetDock(apply, Dock.Right);
                row.Children.Add(apply);
            }
            row.Children.Add(Ui.Row(10,
                Ui.Icon(x.Warning ? Icons.Alert : Icons.Close, 13, x.Warning ? Ui.Res("Warn") : Ui.Res("Bad")),
                new TextBlock { Text = x.Line > 0 ? I18n.T("v15.ms.line", ("n", x.Line)) : "—", FontFamily = Mono, FontSize = 12, Width = 70, Foreground = Ui.Res("Faint"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = x.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }));
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(6, 4), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = row };
            b.Click += (_, _) =>
            {
                if (p.Line <= 0) return;
                if (p.Length > 0) d.Editor.Select(p.Line, p.Col, p.Length);
                else d.Editor.GoToLine(p.Line);
            };
            col.Children.Add(b);
        }
        return new ScrollViewer { Content = col };
    }

    static void QuickFix(Doc d, int line, int col, int length, string fix)
    {
        var lines = d.Editor.Text.Split('\n');
        if (line < 1 || line > lines.Length || col + length > lines[line - 1].Length) return;
        lines[line - 1] = lines[line - 1][..col] + fix + lines[line - 1][(col + length)..];
        d.Editor.Apply(string.Join('\n', lines));
    }

    // ---------------------------------------------------------------- отладчик

    /// <summary>F5: при отладке — дальше до точки останова; иначе запуск с записью хода.</summary>
    void RunOrContinue()
    {
        if (_s.Debugging) Step("continue");
        else Run();
    }

    /// <summary>После запуска: есть точка останова на пути — встать на первой и открыть отладку.</summary>
    void AfterRun(MX.Sandbox.Trace trace)
    {
        _s.Trace = trace;
        _s.TraceFile = _s.Active?.Path;
        var bps = _s.Active?.Editor.Breakpoints ?? [];
        var hit = bps.Count == 0 ? -1 : trace.Frames.FindIndex(f => bps.Contains(f.Line));
        if (hit < 0) { StopDebug(render: false); return; }
        _s.Debugging = true;
        _s.Panel = "debug";
        GoFrame(hit, render: false);
    }

    Doc? TraceDoc() => _s.Docs.FirstOrDefault(d => d.Path is not null && Same(d.Path, _s.TraceFile)) ?? _s.Active;

    void GoFrame(int i, bool render = true)
    {
        if (_s.Trace is not { Frames.Count: > 0 } t) return;
        _s.Frame = Math.Clamp(i, 0, t.Frames.Count - 1);
        var f = t.Frames[_s.Frame];
        var doc = TraceDoc();
        if (doc is not null && doc != _s.Active && Content is not null) Activate(doc);
        doc?.Editor.SetDebug(f.Line, f.Vars.GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.First().Value));
        if (!render || Content is null) return;
        if (_s.Panel == "debug" && _dbgBody is not null && _dbgPos is not null && _dbgBody.IsAttachedToVisualTree()) { _dbgPos.Text = Position(t); _dbgBody.Child = DebugBody(t); }
        else { _s.Panel = "debug"; RenderPanel(); }
        RenderStatus();
    }

    void Step(string how)
    {
        if (_s.Trace is not { Frames.Count: > 0 } t) { Run(); return; }
        if (!_s.Debugging)
        {
            _s.Debugging = true;
            GoFrame(how == "back" ? t.Frames.Count - 1 : 0);
            return;
        }
        var depth = t.Frames[_s.Frame].Stack.Count;
        var bps = TraceDoc()?.Editor.Breakpoints ?? [];
        int Find(Func<MX.Sandbox.Frame, bool> ok)
        {
            for (var i = _s.Frame + 1; i < t.Frames.Count; i++) if (ok(t.Frames[i])) return i;
            return t.Frames.Count - 1;
        }
        var next = how switch
        {
            "back" => _s.Frame - 1,
            "into" => _s.Frame + 1,
            "out" => Find(f => f.Stack.Count < depth),
            "continue" => Find(f => bps.Contains(f.Line)),
            _ => Find(f => f.Stack.Count <= depth),
        };
        GoFrame(next);
    }

    void StopDebug(bool render = true)
    {
        _s.Debugging = false;
        foreach (var d in _s.Docs) d.Editor.SetDebug(null, null);
        if (!render || Content is null) return;
        RenderPanel();
        RenderStatus();
    }

    string Position(MX.Sandbox.Trace t)
    {
        var f = t.Frames[_s.Frame];
        var where = f.Stack.Count > 0 ? $"{f.Where} → {string.Join(" → ", f.Stack.Select(s => s + "()"))}" : f.Where;
        return I18n.T("v19.dbg.pos", ("i", _s.Frame + 1), ("n", t.Frames.Count + (t.Cut ? "+" : "")), ("line", f.Line), ("where", where));
    }

    Control DebugPane()
    {
        _dbgBody = null;
        _dbgPos = null;
        if (_s.Trace is not { Frames.Count: > 0 } t)
            return new Border
            {
                Padding = new Thickness(14, 12),
                Child = Ui.Col(10, Ui.Text(I18n.T("v19.dbg.empty"), "small muted", wrap: true),
                    Ui.Row(8, Ui.Button(I18n.T("v19.dbg.start"), Run, "primary", Icons.Bug, "F5"),
                        Ui.Button(I18n.T("v19.cmd.breakpoint"), () => { if (_s.Active is { } d) d.Editor.ToggleBreakpoint(d.Editor.CaretLine); }, "", Icons.Plus, "F9"))),
            };
        Button Tool(string icon, string key, string gesture, Action run)
        {
            var b = new Button { Classes = { "icon", "ghost" }, Width = 30, Height = 30, Padding = new Thickness(0), Content = Ui.Icon(icon, 15, Ui.Res("Text")) };
            ToolTip.SetTip(b, I18n.T(key) + "  (" + gesture + ")");
            b.Click += (_, _) => run();
            return b;
        }
        var tools = Ui.Row(2,
            Tool(Icons.Play, "v19.dbg.continue", "F5", () => Step("continue")),
            Tool(Icons.Back, "v19.dbg.back", "Shift+F10", () => Step("back")),
            Tool(Icons.Forward, "v19.dbg.over", "F10", () => Step("over")),
            Tool(Icons.ChevronDown, "v19.dbg.into", "F11", () => Step("into")),
            Tool(Icons.ArrowUp, "v19.dbg.out", "Shift+F11", () => Step("out")),
            Tool(Icons.Refresh, "v19.dbg.restart", "Ctrl+Shift+F5", Run),
            Tool(Icons.Stop, "v19.dbg.stop", "Shift+F5", () => StopDebug()));
        var slider = new Slider { Minimum = 0, Maximum = Math.Max(1, t.Frames.Count - 1), Value = _s.Frame, SmallChange = 1, LargeChange = 10, MinWidth = 220, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, -6, 12, -6) };
        ToolTip.SetTip(slider, I18n.T("v19.dbg.slider"));
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            var i = (int)Math.Round(slider.Value);
            if (i == _s.Frame) return;
            _s.Debugging = true;
            GoFrame(i);
        };
        _dbgPos = new TextBlock { Text = Position(t), FontFamily = Mono, FontSize = 11.5, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(8, 6, 12, 2) };
        bar.Children.Add(tools);
        Grid.SetColumn(slider, 1);
        bar.Children.Add(slider);
        Grid.SetColumn(_dbgPos, 2);
        bar.Children.Add(_dbgPos);
        _dbgBody = new Border { Child = DebugBody(t) };
        return new DockPanel { Children = { Docked(bar, Dock.Top), _dbgBody } };
    }

    Control DebugBody(MX.Sandbox.Trace t)
    {
        var f = t.Frames[_s.Frame];
        var prev = _s.Frame > 0 ? t.Frames[_s.Frame - 1].Vars.GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.First().Value) : [];

        var vars = new StackPanel { Spacing = 1 };
        vars.Children.Add(Caption(I18n.T("v19.dbg.vars")));
        foreach (var v in f.Vars.OrderByDescending(v => v.Local).Take(60))
        {
            var changed = !prev.TryGetValue(v.Name, out var was) || was != v.Value;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), Margin = new Thickness(8, 1) };
            row.Children.Add(Ui.Row(6, Ui.Dot(v.Local ? HubLook.Cyan : Ui.Res("Faint"), 5), new TextBlock { Text = v.Name, FontFamily = Mono, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }));
            var value = new SelectableTextBlock { Text = v.Value, FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.NoWrap, Foreground = changed && _s.Frame > 0 ? new SolidColorBrush(Avalonia.Media.Color.Parse("#E5B567")) : Ui.Res("Text") };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            vars.Children.Add(row);
        }
        if (f.Vars.Count == 0) vars.Children.Add(new Border { Padding = new Thickness(8, 2), Child = Ui.Text(I18n.T("v19.dbg.noVars"), "small muted") });

        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(Caption(I18n.T("v19.dbg.stack")));
        foreach (var s in f.Stack.Reverse().Append(f.Where))
            stack.Children.Add(new Border { Padding = new Thickness(8, 1), Child = HubLook.Code(s == f.Where ? "hook " + s : s + "()", 12, s == f.Where ? Ui.Res("Muted") : HubLook.Violet) });
        stack.Children.Add(Caption(I18n.T("v19.dbg.output")));
        var lines = _s.Run?.Output.Take(f.Output).TakeLast(6).ToList() ?? [];
        stack.Children.Add(new Border
        {
            Padding = new Thickness(8, 1),
            Child = new SelectableTextBlock { Text = lines.Count == 0 ? I18n.T("v19.dbg.noOutput") : string.Join('\n', lines), FontFamily = Mono, FontSize = 12, Foreground = lines.Count == 0 ? Ui.Res("Faint") : Ui.Res("Text"), TextWrapping = TextWrapping.Wrap },
        });

        var hot = new StackPanel { Spacing = 1 };
        hot.Children.Add(Caption(I18n.T("v19.dbg.hot")));
        var total = Math.Max(1, t.Hits.Values.Sum());
        var max = Math.Max(1, t.Hits.Values.DefaultIfEmpty(1).Max());
        foreach (var (line, hits) in t.Hits.OrderByDescending(kv => kv.Value).Take(6))
        {
            var at = line;
            var barFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Color, Width = 90.0 * hits / max, HorizontalAlignment = HorizontalAlignment.Left };
            var row = Ui.Row(8, new TextBlock { Text = I18n.T("v15.ms.line", ("n", line)), FontFamily = Mono, FontSize = 12, Width = 78 },
                new Border { Width = 90, Background = HubLook.Raised, CornerRadius = new CornerRadius(2), Child = barFill, VerticalAlignment = VerticalAlignment.Center },
                HubLook.Code($"{hits}×  {100.0 * hits / total:0}%", 11, Ui.Res("Muted")));
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(8, 2), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = row };
            ToolTip.SetTip(b, I18n.T("v19.dbg.hot.tip"));
            b.Click += (_, _) => TraceDoc()?.Editor.GoToLine(at);
            hot.Children.Add(b);
        }
        var bps = TraceDoc()?.Editor.Breakpoints ?? [];
        hot.Children.Add(Caption(I18n.T("v19.dbg.bps")));
        if (bps.Count == 0) hot.Children.Add(new Border { Padding = new Thickness(8, 1), Child = Ui.Text(I18n.T("v19.dbg.noBps"), "small muted", wrap: true) });
        foreach (var line in bps.Order())
        {
            var at = line;
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(8, 2), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = Ui.Row(8, Ui.Dot(new SolidColorBrush(Avalonia.Media.Color.Parse("#E5484D")), 8), new TextBlock { Text = I18n.T("v15.ms.line", ("n", line)), FontFamily = Mono, FontSize = 12 }) };
            b.Click += (_, _) => TraceDoc()?.Editor.GoToLine(at);
            hot.Children.Add(b);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.3*,*,*"), ColumnSpacing = 12, Margin = new Thickness(4, 0, 4, 6) };
        grid.Children.Add(new ScrollViewer { Content = vars });
        var mid = new ScrollViewer { Content = stack };
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);
        var right = new ScrollViewer { Content = hot };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Значение имени в текущем шаге отладки (для справки под курсором).</summary>
    string? DebugValueOf(string name) =>
        _s is { Debugging: true, Trace: { Frames.Count: > 0 } t } && _s.Frame < t.Frames.Count && t.Frames[_s.Frame].Vars.FirstOrDefault(v => v.Name == name) is { Name: not null } v ? v.Value : null;

    // ---------------------------------------------------------------- история файла

    Control HistorySide()
    {
        var col = new StackPanel { Spacing = 1, Margin = new Thickness(8, 0, 8, 12) };
        if (_s.Active is not { Path: { } path } d)
        {
            col.Children.Add(new Border { Padding = new Thickness(8), Child = Ui.Text(I18n.T("v19.hist.none"), "small muted", wrap: true) });
            return col;
        }
        col.Children.Add(new Border { Padding = new Thickness(8, 0, 8, 8), Child = Ui.Text(I18n.T("v19.hist.text"), "small muted", wrap: true) });
        var list = XHistory.List(path);
        if (list.Count == 0) col.Children.Add(new Border { Padding = new Thickness(8), Child = Ui.Text(I18n.T("v19.hist.empty"), "small muted", wrap: true) });
        var current = d.Editor.Text;
        foreach (var e in list.Take(40))
        {
            var entry = e;
            var diff = XHistory.Diff(XHistory.Read(e), current);
            var added = diff.Count(x => x.Op == '+');
            var removed = diff.Count(x => x.Op == '-');
            var local = e.At.ToLocalTime();
            var row = new DockPanel();
            var counts = Ui.Row(6, HubLook.Code("+" + added, 11, Ui.Res("Good")), HubLook.Code("−" + removed, 11, Ui.Res("Bad")));
            DockPanel.SetDock(counts, Dock.Right);
            row.Children.Add(counts);
            row.Children.Add(Ui.Row(8, Ui.Icon(Icons.Clock, 13, added + removed == 0 ? Ui.Res("Good") : Ui.Res("Faint")),
                Ui.Col(0, new TextBlock { Text = local.ToString(local.Date == DateTime.Today ? "HH:mm:ss" : "d MMM HH:mm", I18n.Culture), FontFamily = Mono, FontSize = 12.5 },
                    Ui.Text(Ago(e.At), "tiny muted"))));
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(8, 5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = row };
            b.Click += (_, _) => ShowVersion(d, entry);
            col.Children.Add(b);
        }
        return col;
    }

    static string Ago(DateTime at)
    {
        var span = DateTime.UtcNow - at;
        return span.TotalMinutes < 1 ? I18n.T("v19.ago.now") : span.TotalHours < 1 ? I18n.T("v19.ago.min", ("n", (int)span.TotalMinutes))
            : span.TotalDays < 1 ? I18n.T("v19.ago.hour", ("n", (int)span.TotalHours)) : I18n.T("v19.ago.day", ("n", (int)span.TotalDays));
    }

    /// <summary>Снимок файла: что вернётся, если откатиться (красное уйдёт, зелёное вернётся), и кнопка отката.</summary>
    void ShowVersion(Doc d, XHistory.Entry e)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var old = XHistory.Read(e);
        var diff = XHistory.Diff(d.Editor.Text, old);
        var lines = new StackPanel();
        var shown = 0;
        var gap = false;
        for (var i = 0; i < diff.Count && shown < 400; i++)
        {
            // Без изменений — только по две строки вокруг правок; пропуск — «⋯».
            var near = Enumerable.Range(Math.Max(0, i - 2), Math.Min(diff.Count, i + 3) - Math.Max(0, i - 2)).Any(k => diff[k].Op != ' ');
            if (diff[i].Op == ' ' && !near) { gap = shown > 0; continue; }
            if (gap) lines.Children.Add(new Border { Padding = new Thickness(8, 1), Child = new TextBlock { Text = "  ⋯", FontFamily = Mono, FontSize = 12, Foreground = Ui.Res("Faint") } });
            gap = false;
            shown++;
            var (op, text) = diff[i];
            lines.Children.Add(new Border
            {
                Background = op == '+' ? new SolidColorBrush(Avalonia.Media.Color.FromArgb(40, 70, 200, 120)) : op == '-' ? new SolidColorBrush(Avalonia.Media.Color.FromArgb(40, 229, 72, 77)) : Brushes.Transparent,
                Padding = new Thickness(8, 1),
                Child = new TextBlock { Text = (op == ' ' ? "  " : op + " ") + text, FontFamily = Mono, FontSize = 12, Foreground = op == ' ' ? Ui.Res("Muted") : Ui.Res("Text") },
            });
        }
        if (diff.All(x => x.Op == ' ')) lines.Children.Add(Ui.Text(I18n.T("v19.hist.same"), "small muted"));
        var body = Ui.Col(10, Ui.Text(I18n.T("v19.hist.diff"), "small muted", wrap: true),
            new Border { BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MaxHeight = 380, Child = new ScrollViewer { Content = lines, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } });
        w.Dialog(I18n.T("v19.hist.title", ("time", e.At.ToLocalTime().ToString("d MMM HH:mm:ss", I18n.Culture))), body, 720,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("v19.hist.restore"), () =>
            {
                w.CloseDialog();
                d.Editor.Apply(old);
                w.Toast(I18n.T("v19.hist.restored"));
            }, "primary", Icons.Undo));
    }

    // ---------------------------------------------------------------- палитра

    sealed record PaletteItem(string Icon, string Title, string Hint, Action Run);

    IEnumerable<PaletteItem> Commands()
    {
        PaletteItem C(string icon, string key, string gesture, Action run) => new(icon, I18n.T(key), gesture, run);
        yield return C(Icons.Play, "v15.ms.run", "F5", Run);
        yield return C(Icons.Zap, "v16.x.inject", "F6", () => _ = InjectNow());
        yield return C(Icons.Undo, "v15.ms.eject", "Shift+F6", () => _ = EjectNow());
        yield return C(Icons.Bug, "v19.cmd.breakpoint", "F9", () => { if (_s.Active is { } d) d.Editor.ToggleBreakpoint(d.Editor.CaretLine); });
        yield return C(Icons.Forward, "v19.dbg.over", "F10", () => Step("over"));
        yield return C(Icons.Back, "v19.dbg.back", "Shift+F10", () => Step("back"));
        yield return C(Icons.ChevronDown, "v19.dbg.into", "F11", () => Step("into"));
        yield return C(Icons.ArrowUp, "v19.dbg.out", "Shift+F11", () => Step("out"));
        yield return C(Icons.Stop, "v19.dbg.stop", "Shift+F5", () => StopDebug());
        yield return C(Icons.Forward, "v19.cmd.definition", "F12", () => Definition());
        yield return C(Icons.Search, "v19.cmd.references", "Shift+F12", FindReferences);
        yield return C(Icons.Edit, "v19.cmd.rename", "F2", RenameSymbol);
        yield return C(Icons.Wand, "v19.cmd.format", "Shift+Alt+F", FormatCode);
        yield return C(Icons.Search, "v19.cmd.search", "Ctrl+Shift+F", () => { _s.Panel = "search"; RenderPanel(); });
        yield return C(Icons.Alert, "v15.ms.key.next", "F8", NextProblem);
        yield return C(Icons.Save, "cr.save", "Ctrl+S", () => { if (_s.Active is { } d) Save(d); });
        yield return C(Icons.FilePlus, "v15.ms.new", "Ctrl+N", NewFile);
        yield return C(Icons.Folder, "v15.ms.open", "Ctrl+O", () => _ = OpenFile());
        yield return C(Icons.Close, "v15.ms.close", "Ctrl+W", () => { if (_s.Active is { } d) Close(d); });
        yield return C(Icons.Eye, "v15.ms.watch", "", () => { _s.Watch = !_s.Watch; RenderStatus(); RenderTop(); });
        yield return C(Icons.Gamepad, "v15.ms.play", "", () => _ = PlayWithInject());
        yield return C(Icons.Shield, "v11.lx.check", "", Check);
        foreach (var (id, icon, key) in Sides) yield return new PaletteItem(icon, I18n.T("v19.palette.side", ("name", I18n.T(key))), "", () => Side(id));
        foreach (var (id, key, icon) in PanelTabs) yield return new PaletteItem(icon, I18n.T("v19.palette.panel", ("name", I18n.T(key))), "", () => { _s.Panel = id; RenderPanel(); });
        yield return C(Icons.Back, "v18.hub.projects", "", () => MainWindow.Current?.Navigate(() => new CreatorPage()));
    }

    /// <summary>Совпадение «по буквам по порядку»: чем плотнее и раньше, тем выше. null — не подходит.</summary>
    static int? Score(string text, string query)
    {
        if (query == "") return 0;
        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) return 1000 - at;
        var score = 0;
        var last = -1;
        foreach (var c in query)
        {
            var i = text.IndexOf(c.ToString(), last + 1, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            score += i == last + 1 ? 10 : 1;
            last = i;
        }
        return score;
    }

    List<PaletteItem> PaletteFind(string query)
    {
        if (query.StartsWith('>'))
        {
            var q = query[1..].Trim();
            return [.. Commands().Select(c => (c, s: Score(c.Title, q))).Where(x => x.s is not null).OrderByDescending(x => x.s).Select(x => x.c).Take(40)];
        }
        if (query.StartsWith(':'))
        {
            if (_s.Active is not { } d || !int.TryParse(query[1..].Trim(), out var n)) return [];
            return [new PaletteItem(Icons.Forward, I18n.T("v19.palette.line", ("n", n)), "", () => d.Editor.GoToLine(n))];
        }
        if (query.StartsWith('@'))
        {
            if (_s.Active is not { Modx: true } d) return [];
            var q = query[1..].Trim();
            var m = d.Editor.Model;
            return [.. m.Symbols.Where(s => s.Kind is Insight.Kind.Function or Insight.Kind.Shared or Insight.Kind.Local)
                .Select(s => (s, score: Score(s.Name, q))).Where(x => x.score is not null).OrderByDescending(x => x.score).ThenBy(x => x.s.Line)
                .Select(x => new PaletteItem(x.s.Kind == Insight.Kind.Function ? Icons.Code : Icons.Package, x.s.Kind == Insight.Kind.Function ? x.s.Detail : x.s.Name,
                    (x.s.Scope == "" ? "" : Insight.Where(x.s.Scope) + " · ") + I18n.T("v15.ms.line", ("n", x.s.Line)), () => d.Editor.Select(x.s.Line, x.s.Col, x.s.Name.Length)))
                .Take(40)];
        }
        var files = new List<string>();
        if (_p is not null)
            try { files = [.. Directory.EnumerateFiles(_p.Dir).Where(f => f.EndsWith(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))]; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var q2 = query.Trim();
        var found = files.Select(f => (f, s: Score(System.IO.Path.GetFileName(f), q2))).Where(x => x.s is not null).OrderByDescending(x => x.s)
            .Select(x => new PaletteItem(x.f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? Icons.Book : Icons.Code, System.IO.Path.GetFileName(x.f), I18n.T("v19.palette.file"), () => Open(x.f))).ToList();
        // Без префикса — ещё и команды: палитра одна на всё.
        if (q2 != "") found.AddRange(Commands().Select(c => (c, s: Score(c.Title, q2))).Where(x => x.s is not null).OrderByDescending(x => x.s).Select(x => x.c).Take(12));
        return [.. found.Take(40)];
    }

    void Palette(string prefix)
    {
        var box = new TextBox { Text = prefix, Watermark = I18n.T("v19.palette.hint"), FontFamily = Mono, FontSize = 13.5, Padding = new Thickness(12, 9) };
        var list = new StackPanel { Spacing = 1 };
        var selected = 0;
        List<PaletteItem> items = [];
        void Close()
        {
            _overlay.IsVisible = false;
            _overlay.Child = null;
            _s.Active?.Editor.FocusText();
        }
        void RunAt(int i)
        {
            if (i < 0 || i >= items.Count) return;
            Close();
            items[i].Run();
        }
        void Fill()
        {
            items = PaletteFind(box.Text ?? "");
            selected = Math.Clamp(selected, 0, Math.Max(0, items.Count - 1));
            list.Children.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                var at = i;
                var e = items[i];
                var row = new DockPanel();
                if (e.Hint != "")
                {
                    var hint = HubLook.Code(e.Hint, 11, Ui.Res("Faint"));
                    hint.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(hint, Dock.Right);
                    row.Children.Add(hint);
                }
                row.Children.Add(Ui.Row(10, Ui.Icon(e.Icon, 14, i == selected ? Color : Ui.Res("Muted")), new TextBlock { Text = e.Title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));
                var b = new Button { Classes = { "ghost" }, Padding = new Thickness(12, 6), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = row, Background = i == selected ? HubLook.Raised : Brushes.Transparent };
                b.Click += (_, _) => RunAt(at);
                list.Children.Add(b);
            }
            if (items.Count == 0) list.Children.Add(new Border { Padding = new Thickness(14, 10), Child = Ui.Text(I18n.T("v19.palette.nothing"), "small muted") });
        }
        box.TextChanged += (_, _) => { selected = 0; Fill(); };
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down: selected = Math.Min(items.Count - 1, selected + 1); Fill(); break;
                case Key.Up: selected = Math.Max(0, selected - 1); Fill(); break;
                case Key.Enter: RunAt(selected); break;
                case Key.Escape: Close(); break;
                default: return;
            }
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        var tips = HubLook.Code(I18n.T("v19.palette.tips"), 10.5, Ui.Res("Faint"));
        tips.Margin = new Thickness(12, 6, 12, 8);
        var card = new Border
        {
            Width = 600, MaxHeight = 460, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 64, 0, 0),
            Background = HubLook.Panel, BorderBrush = HubLook.Premium(170), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 40, OffsetY = 12, Color = Avalonia.Media.Color.FromArgb(120, 0, 0, 0) }),
            Child = new DockPanel { Children = { Docked(new Border { Padding = new Thickness(8, 8, 8, 4), Child = box }, Dock.Top), Docked(tips, Dock.Bottom), new ScrollViewer { Content = list, Margin = new Thickness(4, 0) } } },
        };
        card.PointerPressed += (_, e) => e.Handled = true;
        _overlay.Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb(90, 0, 0, 0));
        _overlay.Child = card;
        _overlay.IsVisible = true;
        _overlay.PointerPressed -= OverlayPressed;
        _overlay.PointerPressed += OverlayPressed;
        Fill();
        Dispatcher.UIThread.Post(() => { box.Focus(); box.CaretIndex = box.Text?.Length ?? 0; }, DispatcherPriority.Background);
    }

    void OverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        _overlay.IsVisible = false;
        _overlay.Child = null;
    }

    // ---------------------------------------------------------------- клавиши среды

    /// <summary>Клавиши IDE: true — нажатие разобрано.</summary>
    bool IdeKey(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (_overlay.IsVisible && e.Key == Key.Escape) { OverlayPressed(null, null!); return true; }
        switch (e.Key)
        {
            case Key.F5 when ctrl && shift: Run(); return true;
            case Key.F5 when shift: StopDebug(); return true;
            case Key.F5: RunOrContinue(); return true;
            case Key.F9: if (_s.Active is { } d) d.Editor.ToggleBreakpoint(d.Editor.CaretLine); return true;
            case Key.F10: Step(shift ? "back" : "over"); return true;
            case Key.F11 when ctrl: ToggleFocus(); return true;
            case Key.F11: Step(shift ? "out" : "into"); return true;
            case Key.F12 when shift: FindReferences(); return true;
            case Key.F12: Definition(); return true;
            case Key.F2: RenameSymbol(); return true;
            case Key.F when alt && shift: FormatCode(); return true;
            case Key.F when ctrl && shift: _s.Panel = "search"; RenderPanel(); return true;
            case Key.P when ctrl && shift: Palette(">"); return true;
            case Key.P when ctrl: Palette(""); return true;
            case Key.O when ctrl && shift: Palette("@"); return true;
            case Key.G when ctrl: Palette(":"); return true;
            default: return false;
        }
    }

    // ---------------------------------------------------------------- снимки

    internal const string DemoCode = """
        mod "Arena Insight"
        version 1.0.0
        engine unity

        # Сколько золота за одну волну
        let reward = 50
        let bonus_rate = 0.25

        # Награда за волну n: базовая плюс бонус за серию побед
        fn award(n, streak) {
            let total = reward * n
            if streak > 2 {
                total += round(total * bonus_rate)
            }
            return clamp(total, 0, 1000)
        }

        hook start {
            say "Arena: +{reward} gold per wave"
            log rond(bonus_rate * 100)
        }

        hook tick {
            let gained = award(wave, streak)
            gold += gained
            let unused = 3
            if gold > 500 {
                say "Rich! {gold} gold"
            }
            log "wave {wave}: +{gainde}"
        }

        """;

    /// <summary>Сцены для снимков 19.0: понимание кода, подсказки, отладчик, ссылки, палитра, история.</summary>
    internal void Demo(string scene)
    {
        if (_p is null) return;
        var path = System.IO.Path.Combine(_p.Dir, "insight.modx");
        Doc? D() => _s.Docs.FirstOrDefault(d => Same(d.Path, path));
        int Line(string needle) => Array.FindIndex(DemoCode.Split('\n'), l => l.Contains(needle, StringComparison.Ordinal)) + 1;
        switch (scene)
        {
            case "insight":
                File.WriteAllText(path, DemoCode);
                if (D() is { } stale) { stale.Saved = stale.Editor.Text; Close(stale); }
                Open(path);
                if (D() is not { } d) return;
                Analyze(d);
                _s.Vars = "gold = 420\nwave = 3\nstreak = 4";
                _s.Side = "files";
                _s.Panel = "problems";
                RenderAll();
                d.Editor.Select(Line("let total"), DemoCode.Split('\n')[Line("let total") - 1].IndexOf("reward", StringComparison.Ordinal), 0);
                break;
            case "complete":
                if (D() is not { } c) return;
                var at = c.Editor.OffsetOf(Line("let total"), 999);
                c.Editor.CaretIndex = at;
                c.Editor.Insert("    let capped = cla");
                c.Editor.Suggest();
                break;
            case "debug":
                if (D() is not { } g) return;
                g.Editor.CloseHints();
                g.Editor.Undo();
                g.Editor.Text = DemoCode;
                g.Saved = DemoCode;
                Analyze(g);
                g.Editor.ToggleBreakpoint(Line("total += round"));
                Run();
                Step("over");
                break;
            case "refs":
                if (D() is not { } r) return;
                r.Editor.Select(Line("let reward"), 4, 0);
                FindReferences();
                break;
            case "palette":
                Palette(">");
                break;
            case "close":
                OverlayPressed(null, null!);
                break;
            case "history":
                if (D() is not { } h) return;
                StopDebug();
                XHistory.Snap(path, DemoCode.Replace("let reward = 50", "let reward = 25").Replace("    let unused = 3\n", ""), DateTime.UtcNow.AddHours(-3));
                XHistory.Snap(path, DemoCode.Replace("let reward = 50", "let reward = 40"), DateTime.UtcNow.AddMinutes(-42));
                XHistory.Snap(path, DemoCode);
                Side("history");
                if (XHistory.List(path).Skip(2).FirstOrDefault() is { } old) ShowVersion(h, old);
                break;
        }
    }
}
