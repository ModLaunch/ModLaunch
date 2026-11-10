using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using Insight = ModLaunch.Creator.ModxInsight;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 22.0: среда разработки целиком. Простой язык (когда / если / конец) — родной язык среды: блоки, шпаргалка,
/// подсказки, «показать как обычный ModScript X» и перевод файла. Файлы проекта — любые (скрипты, заметки, JSON,
/// Lua…), с меню «переименовать, копия, удалить». Консоль внизу: команда выполняется в песочнице сразу, переменные
/// живут между командами, ↑/↓ — история. Мост умеет описать свой движок (modscript.engine.json) прямо из среды.
/// </summary>
public sealed partial class XStudio
{
    /// <summary>Открытый файл написан простым языком.</summary>
    bool ActiveSimple => _s.Active is { Modx: true } d && MX.Simple.IsSimple(d.Editor.Text ?? "");

    /// <summary>Файлы, которые среда показывает в проекте и открывает как текст.</summary>
    static readonly string[] TextExtensions = [".modx", ".mls", ".md", ".txt", ".json", ".lua", ".py", ".js", ".cs", ".gd", ".rpy", ".cfg", ".ini", ".xml", ".yml", ".yaml", ".csv"];

    static bool IsText(string path) => TextExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    // ---------------------------------------------------------------- простой язык

    const string SimpleTemplate = """
        мод "{name}"
        автор {author}
        версия 1

        когда игра началась
            скажи Мод {name} работает!

        каждый кадр
            # здесь то, что происходит всё время
        """;

    /// <summary>Блоки простым языком: те же группы, что у обычных блоков.</summary>
    internal static IEnumerable<(string Group, (string Key, string Code, string? Event)[] Items)> SimpleBlocks() =>
    [
        ("events", [
            ("v16.event.ready", "когда мод загружен\n    запиши мод загружен\nконец\n", "ready"),
            ("v16.event.start", "когда игра началась\n    скажи Привет!\nконец\n", "start"),
            ("v16.event.load", "когда игра загружена\n    запиши сохранение загружено\nконец\n", "load"),
            ("v16.event.tick", "каждый кадр\n    дай тики 1\nконец\n", "tick"),
        ]),
        ("data", [
            ("v16.x.b.let", "пусть бонус = 100\n", null),
            ("v16.x.b.set", "поставь золото в золото + бонус\n", null),
            ("v16.x.b.add", "дай очки 1\n", null),
            ("v22.b.take", "забери золото 10\n", null),
        ]),
        ("logic", [
            ("v16.x.b.if", "если здоровье меньше 20\n    скажи Мало здоровья!\nиначе\n    запиши всё хорошо\nконец\n", null),
            ("v16.x.b.for", "повтори 3 раза\n    дай золото 10\nконец\n", null),
            ("v16.x.b.while", "пока золото меньше 100\n    дай золото 10\nконец\n", null),
            ("v16.x.b.fn", "функция удвоить(число)\n    верни число * 2\nконец\n", null),
        ]),
        ("talk", [
            ("v16.x.b.say", "скажи Золото: {золото}\n", null),
            ("v16.x.b.log", "запиши золото = {золото}\n", null),
        ]),
        ("tricks", [
            ("v16.x.b.timer", "дай таймер 1\nесли таймер больше или равно 600\n    таймер = 0\n    скажи Прошло 10 секунд\nконец\n", null),
            ("v16.x.b.chance", "если шанс(10)\n    дай золото случайно(1, 50)\n    скажи Повезло!\nконец\n", null),
            ("v16.x.b.clamp", "здоровье = между(здоровье, 0, 100)\n", null),
        ]),
    ];

    /// <summary>Шпаргалка простого языка: пример целиком и все слова по группам.</summary>
    Control SimpleDocs()
    {
        var col = Ui.Col(10);
        col.Children.Add(Ui.Text(I18n.T("v22.docs.lead"), "small muted", wrap: true));
        col.Children.Add(new Border
        {
            Background = HubLook.Raised, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8),
            Child = new SelectableTextBlock
            {
                Text = "мод \"Много золота\"\n\nкогда игра началась\n    дай золото 1000\n    скажи У тебя {золото} золота\n\nкаждый кадр\n    если здоровье меньше 10\n        здоровье = 100",
                FontFamily = Mono, FontSize = 11.5, LineHeight = 17, TextWrapping = TextWrapping.Wrap,
            },
        });
        foreach (var group in new[] { "event", "header", "command", "block", "op", "fn" })
        {
            var rows = Ui.Col(3);
            foreach (var e in MX.Simple.Vocabulary.Where(e => e.Group == group))
            {
                var word = new TextBlock { Text = e.Word, FontFamily = Mono, FontSize = 11, Width = 112, Foreground = HubLook.Cyan, TextWrapping = TextWrapping.Wrap };
                var meaning = Ui.Text(e.Meaning, "tiny muted", wrap: true);
                var row = new DockPanel { Children = { Docked(word, Dock.Left), meaning } };
                ToolTip.SetTip(row, I18n.T("v22.hover.modx", ("code", e.Modx)));
                rows.Children.Add(row);
            }
            col.Children.Add(Ui.Col(6, Ui.Text(I18n.T("v22.docs.g." + group), "small strong"), rows));
        }
        col.Children.Add(Ui.Text(I18n.T("v22.docs.names"), "tiny muted", wrap: true));
        return col;
    }

    /// <summary>Новый файл простым языком (для первого мода).</summary>
    void NewSimpleFile()
    {
        if (_p is null) return;
        var path = CreatorPage.FreeModxPath(_p.Dir, "мой-мод");
        var text = SimpleTemplate.Replace("{name}", _p.Name).Replace("{author}", _p.Author == "" ? "я" : _p.Author);
        try { File.WriteAllText(path, text + "\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
        Open(path);
    }

    /// <summary>Во что превращается простой код — новой вкладкой рядом (файл не меняется).</summary>
    void ShowAsModx()
    {
        if (_s.Active is not { Modx: true } d || !ActiveSimple) { MainWindow.Current?.Toast(I18n.T("v22.modx.notSimple")); return; }
        var text = MX.Simple.ToModx(d.Editor.Text ?? "").Text;
        var doc = Add(null, System.IO.Path.GetFileNameWithoutExtension(d.Title) + " · modx", text, "");
        Activate(doc);
    }

    /// <summary>Перевести файл в обычный ModScript X (с подтверждением: комментарии пропадут).</summary>
    void ConvertToModx()
    {
        var w = MainWindow.Current;
        if (w is null || _s.Active is not { Modx: true } d || !ActiveSimple) { w?.Toast(I18n.T("v22.modx.notSimple")); return; }
        w.Dialog(I18n.T("v22.modx.title"), Ui.Text(I18n.T("v22.modx.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("v22.modx.do"), () =>
            {
                w.CloseDialog();
                d.Editor.Apply(MX.Simple.ToModx(d.Editor.Text ?? "").Text);
                d.Editor.Format();
                Analyze(d);
                RenderAll();
            }, "primary", Icons.Code));
    }

    // ---------------------------------------------------------------- файлы проекта

    List<string> ProjectFiles()
    {
        var p = _p!;
        try
        {
            return [.. Directory.EnumerateFiles(p.Dir, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Where(f => IsText(f) && !System.IO.Path.GetRelativePath(p.Dir, f).Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).Any(part => part.StartsWith('.') || part is "build" or "bin" or "obj"))
                .OrderBy(f => Same(f, p.Main) ? 0 : f.EndsWith(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(f => System.IO.Path.GetRelativePath(p.Dir, f), StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    static string IconFor(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".modx" => Icons.Code, ".md" or ".txt" => Icons.Book, ".json" or ".cfg" or ".ini" or ".yml" or ".yaml" or ".xml" => Icons.Sliders, _ => Icons.Tools,
    };

    MenuFlyout FileMenu(string path)
    {
        var main = Same(path, _p!.Main);
        return Ctx.Menu(
            Ctx.Item(I18n.T("v22.file.open"), Icons.Code, () => Open(path)),
            Ctx.Item(I18n.T("v22.file.rename"), Icons.Edit, () => RenameFile(path), enabled: !main),
            Ctx.Item(I18n.T("v22.file.copy"), Icons.Copy, () => DuplicateFile(path)),
            Ctx.Item(I18n.T("ctx.delete"), Icons.Trash, () => DeleteFile(path), enabled: !main),
            "-",
            Ctx.Copy(I18n.T("v22.file.path"), path),
            Ctx.Folder(I18n.T("v15.ms.reveal"), System.IO.Path.GetDirectoryName(path)!));
    }

    /// <summary>Спросить имя файла (без пути). null — отказались.</summary>
    void AskName(string title, string start, Func<string, string?> apply)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var box = new TextBox { Text = start, FontFamily = Mono, MinWidth = 320 };
        var error = new TextBlock { FontSize = 12, Foreground = Ui.Res("Bad"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        void Go()
        {
            var name = (box.Text ?? "").Trim();
            if (name == "" || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) { error.Text = I18n.T("v22.file.bad"); error.IsVisible = true; return; }
            if (apply(name) is { } err) { error.Text = err; error.IsVisible = true; return; }
            w.CloseDialog();
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Go(); } };
        w.Dialog(title, Ui.Col(10, box, error), Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"), Ui.Button("OK", Go, "primary", Icons.Check));
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            var dot = start.LastIndexOf('.');
            box.SelectionStart = 0;
            box.SelectionEnd = dot > 0 ? dot : start.Length;
        }, DispatcherPriority.Background);
    }

    void RenameFile(string path)
    {
        AskName(I18n.T("v22.file.rename"), System.IO.Path.GetFileName(path), name =>
        {
            var to = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, name);
            if (Same(to, path)) return null;
            if (File.Exists(to)) return I18n.T("v22.file.exists");
            try
            {
                if (_s.Docs.FirstOrDefault(d => Same(d.Path, path)) is { } open && open.Dirty) Save(open, quiet: true);
                File.Move(path, to);
                foreach (var d in _s.Docs.Where(d => Same(d.Path, path))) { d.Path = to; d.Title = name; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
            RenderAll();
            return null;
        });
    }

    void DuplicateFile(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path)!;
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);
        var to = System.IO.Path.Combine(dir, $"{stem}-копия{ext}");
        for (var n = 2; File.Exists(to); n++) to = System.IO.Path.Combine(dir, $"{stem}-копия-{n}{ext}");
        try
        {
            var text = _s.Docs.FirstOrDefault(d => Same(d.Path, path))?.Editor.Text ?? File.ReadAllText(path);
            File.WriteAllText(to, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
        Open(to);
    }

    void DeleteFile(string path)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        w.Dialog(I18n.T("v22.file.delete.title", ("file", System.IO.Path.GetFileName(path))), Ui.Text(I18n.T("v22.file.delete.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("ctx.delete"), () =>
            {
                w.CloseDialog();
                try
                {
                    // История файла остаётся: файл можно вернуть из «Истории», если удалили зря.
                    if (File.Exists(path)) XHistory.Snap(path, File.ReadAllText(path));
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { w.Toast(e.Message, bad: true); return; }
                foreach (var d in _s.Docs.Where(d => Same(d.Path, path)).ToList())
                {
                    d.Saved = d.Editor.Text ?? "";
                    _s.Docs.Remove(d);
                    if (_s.Active == d) _s.Active = _s.Docs.FirstOrDefault();
                }
                RenderAll();
            }, "danger", Icons.Trash));
    }

    /// <summary>Новый файл любого вида: простой мод, обычный .modx, заметки или файл движка.</summary>
    void NewAnyFile()
    {
        if (_p is null) return;
        var p = _p;
        AskName(I18n.T("v22.file.new"), "notes.md", name =>
        {
            var path = System.IO.Path.Combine(p.Dir, name);
            if (!IsText(path)) return I18n.T("v22.file.kinds", ("list", string.Join(" ", TextExtensions)));
            if (File.Exists(path)) return I18n.T("v22.file.exists");
            try { File.WriteAllText(path, path.EndsWith(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase) ? SimpleTemplate.Replace("{name}", p.Name).Replace("{author}", p.Author == "" ? "я" : p.Author) + "\n" : ""); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
            Open(path);
            return null;
        });
    }

    // ---------------------------------------------------------------- свой движок

    /// <summary>Мост: описание своего движка (modscript.engine.json) — создать или открыть.</summary>
    Control EngineProfileCard(string? gamePath)
    {
        var col = Ui.Col(6, HubLook.MonoText(I18n.T("v22.engine.title"), 10, Ui.Res("Faint")));
        if (gamePath is null) { col.Children.Add(Ui.Text(I18n.T("v22.engine.noGame"), "small muted", wrap: true)); return col; }
        var file = System.IO.Path.Combine(gamePath, MX.EngineProfile.FileName);
        if (File.Exists(file))
        {
            col.Children.Add(Ui.Text(I18n.T("v22.engine.has"), "small muted", wrap: true));
            col.Children.Add(Ui.Button(I18n.T("v22.engine.open"), () => Open(file), "", Icons.Sliders));
        }
        else
        {
            col.Children.Add(Ui.Text(I18n.T("v22.engine.lead"), "small muted", wrap: true));
            col.Children.Add(Ui.Button(I18n.T("v22.engine.make"), () =>
            {
                try { File.WriteAllText(file, MX.EngineProfile.Template(gamePath, null)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
                CreatorPage.Rescan(gamePath);
                Open(file);
                MainWindow.Current?.Toast(I18n.T("v22.engine.made"));
            }, "", Icons.Wrench));
        }
        return col;
    }

    /// <summary>Сохранили описание движка — мост перечитывается сразу.</summary>
    void AfterSave(Doc d)
    {
        if (d.Path is { } path && System.IO.Path.GetFileName(path).Equals(MX.EngineProfile.FileName, StringComparison.OrdinalIgnoreCase))
        {
            try { MX.EngineProfile.Load(path); }
            catch (InvalidOperationException e) { MainWindow.Current?.Toast(e.Message, bad: true); return; }
            CreatorPage.Rescan(System.IO.Path.GetDirectoryName(path)!);
            RenderTop();
            if (_s.Side == "bridge") RenderSide();
        }
    }

    // ---------------------------------------------------------------- консоль

    /// <summary>Строки консоли: текст и вид (0 — команда, 1 — вывод, 2 — ошибка, 3 — подсказка).</summary>
    sealed class Repl
    {
        public readonly List<(string Text, int Kind)> Lines = [];
        public readonly List<string> History = [];
        public Dictionary<string, object?> Vars = new(StringComparer.OrdinalIgnoreCase);
        public int At = -1;
    }

    static readonly Dictionary<string, Repl> Repls = new(StringComparer.OrdinalIgnoreCase);
    Repl Console_ => Repls.TryGetValue(_id, out var r) ? r : Repls[_id] = new Repl();

    /// <summary>Выполнить строку консоли. Возвращает строки вывода (вид 1 — вывод, 2 — ошибка, 3 — подсказка).</summary>
    internal static List<(string Text, int Kind)> ReplRun(Dictionary<string, object?> vars, string input)
    {
        var o = new List<(string, int)>();
        var cmd = input.Trim();
        switch (cmd.ToLowerInvariant())
        {
            case "помощь" or "help" or "?":
                o.Add((I18n.T("v22.console.help"), 3));
                return o;
            case "переменные" or "vars":
                o.Add((vars.Count == 0 ? I18n.T("v22.console.noVars") : string.Join("\n", vars.Select(kv => $"{kv.Key} = {MX.Sandbox.Show(kv.Value)}")), 1));
                return o;
            case "сброс" or "reset":
                vars.Clear();
                o.Add((I18n.T("v22.console.reset"), 3));
                return o;
        }
        // «пусть x = 5» в консоли живёт дольше одной команды — это просто присваивание.
        var body = Regex.Replace(cmd, @"^(?:пусть|переменная|запомни|let|var)\s+", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        MX.ModxDoc Doc(string code) => MX.ModxDoc.Parse("mod \"console\"\nhook start {\n" + code + "\n}\n");
        var simpleBody = MX.Simple.IsSimple(body) ? MX.Simple.ToModx("когда игра началась\n" + string.Join('\n', body.Split('\n').Select(l => "    " + l)) + "\nконец").Text : null;
        var doc = simpleBody is not null ? MX.ModxDoc.Parse("mod \"console\"\n" + simpleBody) : Doc(body);
        // Не команда, а выражение («золото * 2», «случайно(1, 6)») — показать значение.
        if (!doc.Ok && !body.Contains('\n'))
        {
            var expr = Doc("log " + MX.Simple.Expr(body, false));
            if (expr.Ok) doc = expr;
        }
        if (!doc.Ok)
        {
            foreach (var d in doc.Diags.Where(d => !d.Warning)) o.Add((CreatorPage.LxMsg(d), 2));
            return o;
        }
        var r = MX.Sandbox.Run(doc, vars, ticks: 0, maxSteps: 50_000);
        foreach (var line in r.Output) o.Add((Regex.Replace(line, @"^\[[^\]]*\]\s*", ""), 1));
        foreach (var e in r.Errors) o.Add((CreatorPage.LxMsg(e), e.Warning ? 3 : 2));
        foreach (var (k, v) in r.Vars) vars[k] = v;
        return o;
    }

    // ---------------------------------------------------------------- самопроверка

    /// <summary>
    /// 22.0: среда понимает простой язык — имена и ссылки, переименование, сворачивание, подсказки, справка, структура,
    /// выравнивание без смены смысла, блоки простым языком собираются, консоль помнит переменные и считает выражения.
    /// </summary>
    [SelfTest]
    static string SimpleSyntaxInStudio()
    {
        const string text = "мод \"Тест\"\nпусть бонус = 5\n\nкогда игра началась\n    дай золото бонус\n    если золото больше 3\n        скажи Много {золото}\n\nфункция удвоить(х)\n    верни х * 2\n";
        var m = Insight.Read(text);
        if (!m.Simple) throw new Exception("not simple");
        if (m.Symbols.Count(s => s.Kind == Insight.Kind.Shared && s.Name == "бонус") != 1 || !m.Symbols.Any(s => s.Kind == Insight.Kind.Function && s.Name == "удвоить")
            || !m.Symbols.Any(s => s.Kind == Insight.Kind.Param && s.Name == "х"))
            throw new Exception("symbols: " + string.Join(", ", m.Symbols.Select(s => s.Kind + " " + s.Name)));
        if (m.Refs.Any(r => r.Name is "Много" or "дай" or "больше" or "скажи")) throw new Exception("keywords or text taken as names");
        var gold = Insight.At(m, 5, 9) ?? throw new Exception("no symbol at золото");
        var golds = Insight.References(m, gold).Count;
        if (gold.Name != "золото" || golds != 3) throw new Exception($"refs of {gold.Name}: {golds}");
        var bonus = Insight.At(m, 2, 7)!;
        var (renamed, err) = Insight.Rename(text, bonus, "награда");
        if (renamed is null || !renamed.Contains("пусть награда = 5") || !renamed.Contains("дай золото награда")) throw new Exception("rename: " + err);
        var folds = Insight.Folds(text);
        if (!folds.Contains((4, 7)) || !folds.Contains((6, 7)) || !folds.Contains((9, 10))) throw new Exception("folds: " + string.Join(" ", folds));
        if (!Insight.Complete(m, 11, 0).Any(i => i.Label == "когда игра началась")) throw new Exception("no event suggestion at top");
        var inHook = Insight.Complete(Insight.Read(text.Replace("    дай золото бонус", "    д")), 5, 5);
        if (!inHook.Any(i => i.Label == "дай") || inHook.Any(i => i.Kind == Insight.Kind.Event)) throw new Exception("hook suggestions");
        if (Insight.Hover(m, 5, 5) is not { Title: "дай" }) throw new Exception("hover дай");
        if (Insight.Hover(m, 4, 3) is not { Title: "когда игра началась" }) throw new Exception("hover event");
        if (Insight.Hover(m, 5, 9) is null || MX.Simple.InCode("золото") != "gold") throw new Exception("hover gold");
        if (Insight.Lint(m).Count != 0) throw new Exception("lint: " + string.Join("; ", Insight.Lint(m).Select(x => x.Message)));
        var outline = Outline(text);
        if (!outline.Any(o => o is (4, "hook", _)) || !outline.Any(o => o is (9, "fn", _)) || !outline.Any(o => o is (1, "mod", "Тест"))) throw new Exception("outline: " + string.Join(" ", outline));
        var (formatted, safe) = Insight.Format(text.Replace("    дай золото бонус", "    дай золото бонус   "));
        if (!safe || formatted.TrimEnd() != text.TrimEnd()) throw new Exception("format:\n" + formatted);
        foreach (var (_, items) in SimpleBlocks())
            foreach (var (key, code, ev2) in items)
            {
                var src = "мод \"Блок\"\n" + (ev2 is not null || code.StartsWith("функция", StringComparison.Ordinal) ? code : "когда игра началась\n" + string.Join('\n', code.TrimEnd().Split('\n').Select(l => "    " + l)) + "\nконец\n");
                if (MX.ModxDoc.Parse(src) is { Ok: false } bad) throw new Exception($"block {key}: " + string.Join("; ", bad.Diags.Select(d => d.Message)));
            }
        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        ReplRun(vars, "золото = 100");
        ReplRun(vars, "дай золото 5");
        var twice = ReplRun(vars, "золото * 2");
        var hello = ReplRun(vars, "скажи Привет");
        var block = ReplRun(vars, "если золото больше 100\n    скажи Богач\nконец");
        if (MX.Sandbox.Text(vars.GetValueOrDefault("gold")) != "105" || !twice.Any(o => o.Text.Contains("210")) || !hello.Any(o => o.Text.Contains("Привет")) || !block.Any(o => o.Text.Contains("Богач")))
            throw new Exception($"console: gold {MX.Sandbox.Text(vars.GetValueOrDefault("gold"))}; {string.Join(" | ", twice.Concat(hello).Concat(block).Select(o => o.Text))}");
        if (!ReplRun(vars, "прыгни выше").Any(o => o.Kind == 2)) throw new Exception("console error not shown");
        return $"{m.Symbols.Count} symbols, {golds} refs of золото, {folds.Count} folds, {SimpleBlocks().Sum(g => g.Items.Length)} simple blocks, console gold = 105";
    }

    Control ConsolePane()
    {
        var repl = Console_;
        var output = new SelectableTextBlock { FontFamily = Mono, FontSize = 12, LineHeight = 18, TextWrapping = TextWrapping.Wrap };
        var scroll = new ScrollViewer { Content = output, Margin = new Thickness(12, 8, 8, 4) };
        void Fill()
        {
            output.Inlines ??= [];
            output.Inlines.Clear();
            if (repl.Lines.Count == 0) output.Inlines.Add(new Avalonia.Controls.Documents.Run(I18n.T("v22.console.lead")) { Foreground = Ui.Res("Muted") });
            foreach (var (text, kind) in repl.Lines)
            {
                output.Inlines.Add(new Avalonia.Controls.Documents.Run((kind == 0 ? "› " : "  ") + text + "\n")
                {
                    Foreground = kind switch { 0 => HubLook.Cyan, 2 => Ui.Res("Bad"), 3 => Ui.Res("Muted"), _ => Ui.Res("Text") },
                });
            }
            Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
        }
        var input = new TextBox { FontFamily = Mono, FontSize = 12.5, Watermark = I18n.T("v22.console.hint"), AcceptsReturn = false };
        void Exec()
        {
            var text = input.Text ?? "";
            if (text.Trim() == "") return;
            if (text.Trim() is "очистить" or "clear" or "cls") { repl.Lines.Clear(); input.Text = ""; Fill(); return; }
            repl.History.Remove(text);
            repl.History.Add(text);
            repl.At = -1;
            repl.Lines.Add((text.Replace("\n", "\n  "), 0));
            repl.Lines.AddRange(ReplRun(repl.Vars, text));
            if (repl.Lines.Count > 500) repl.Lines.RemoveRange(0, repl.Lines.Count - 500);
            input.Text = "";
            Fill();
        }
        input.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    // Shift+Enter — ещё строка (блок «если … конец» целиком).
                    var at = input.CaretIndex;
                    input.Text = (input.Text ?? "").Insert(at, "\n    ");
                    input.CaretIndex = at + 5;
                    e.Handled = true;
                    break;
                case Key.Enter: Exec(); e.Handled = true; break;
                case Key.Up when repl.History.Count > 0 && !(input.Text ?? "").Contains('\n'):
                    repl.At = repl.At < 0 ? repl.History.Count - 1 : Math.Max(0, repl.At - 1);
                    input.Text = repl.History[repl.At];
                    input.CaretIndex = input.Text.Length;
                    e.Handled = true;
                    break;
                case Key.Down when repl.At >= 0 && !(input.Text ?? "").Contains('\n'):
                    repl.At++;
                    input.Text = repl.At < repl.History.Count ? repl.History[repl.At] : "";
                    if (repl.At >= repl.History.Count) repl.At = -1;
                    input.CaretIndex = (input.Text ?? "").Length;
                    e.Handled = true;
                    break;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        input.AcceptsReturn = true;
        var run = Ui.Button("", Exec, "icon ghost", Icons.Play, I18n.T("v22.console.run"));
        var clear = Ui.Button("", () => { repl.Lines.Clear(); Fill(); }, "icon ghost", Icons.Trash, I18n.T("v22.console.clear"));
        var vars = Ui.Button("", () => { repl.Lines.Add(("переменные", 0)); repl.Lines.AddRange(ReplRun(repl.Vars, "vars")); Fill(); }, "icon ghost", Icons.List, I18n.T("v22.console.vars"));
        var bar = new DockPanel { Margin = new Thickness(12, 0, 8, 8) };
        var buttons = Ui.Row(2, vars, clear, run);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(new DockPanel { Children = { Docked(new TextBlock { Text = "›", FontFamily = Mono, FontSize = 14, Foreground = HubLook.Cyan, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, Dock.Left), input } });
        Fill();
        Dispatcher.UIThread.Post(() => input.Focus(), DispatcherPriority.Background);
        return new DockPanel { Children = { Docked(bar, Dock.Bottom), scroll } };
    }
}
