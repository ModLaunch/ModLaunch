using System.Text;
using System.Text.RegularExpressions;
using MX = ModScriptX;

namespace ModLaunch.Creator;

/// <summary>
/// 21.0: блок ModScript X — одна строка или одна конструкция файла .modx, показанная кирпичиком. Поля (F) — то, что
/// человек меняет мышкой и клавиатурой; пока блок не трогали, в код идёт его исходная строка как была (Orig).
/// </summary>
public sealed class XBlock
{
    /// <summary>
    /// hook, fn, native — верхние конструкции; let, use, note, raw — строки верха файла; в теле — let, set, add, op, say,
    /// log, call, if, while, for, return, break, continue, note, raw.
    /// </summary>
    public string Kind = "raw";
    public readonly Dictionary<string, string> F = new(StringComparer.Ordinal);
    /// <summary>Исходная строка без отступа и комментария (для if/while/for/hook/fn — только заголовок без «{»).</summary>
    public string Orig = "";
    public string Comment = "";
    public bool Dirty;
    public List<XBlock> Body = [];
    public List<XBranch> Elifs = [];
    public List<XBlock>? Else;
    /// <summary>Строка в коде (после последнего разбора или записи) — ошибки разбора показываются на блоке.</summary>
    public int Line;
    public int ElseLine;

    public string this[string field]
    {
        get => F.GetValueOrDefault(field) ?? "";
        set { F[field] = value; Dirty = true; }
    }

    public bool Container => Kind is "if" or "while" or "for" or "hook" or "fn";

    public static XBlock New(string kind, params (string Key, string Value)[] fields)
    {
        var b = new XBlock { Kind = kind, Dirty = true };
        foreach (var (k, v) in fields) b.F[k] = v;
        return b;
    }

    public XBlock Clone()
    {
        var b = new XBlock { Kind = Kind, Orig = Orig, Comment = Comment, Dirty = Dirty, Line = Line, ElseLine = ElseLine };
        foreach (var (k, v) in F) b.F[k] = v;
        b.Body = Body.Select(x => x.Clone()).ToList();
        b.Elifs = Elifs.Select(e => new XBranch { Cond = e.Cond, Line = e.Line, Body = e.Body.Select(x => x.Clone()).ToList() }).ToList();
        b.Else = Else?.Select(x => x.Clone()).ToList();
        return b;
    }

    /// <summary>Сказать/записать простым текстом: строка в кавычках без выражений вокруг — поле «текст» без кавычек.</summary>
    public bool PlainText => Kind is "say" or "log" && F.ContainsKey("text");

    /// <summary>Заголовок или строка кода по полям блока.</summary>
    public string Build()
    {
        if (!Dirty && Orig != "") return Orig;
        return Kind switch
        {
            "hook" => $"hook {this["event"]}",
            "fn" => $"fn {Name(this["name"], "my_command")}({this["params"].Trim()})",
            "let" => $"let {Name(this["name"], "value")} = {Or(this["value"], "0")}",
            "set" => $"set {Name(this["target"], "value")} = {Or(this["value"], "0")}",
            "add" => $"add {Name(this["target"], "value")} {Or(this["value"], "1")}",
            "op" => $"{Name(this["target"], "value")} {Or(this["op"], "+=")} {Or(this["value"], "1")}",
            "say" or "log" => Kind + " " + (PlainText ? Quote(this["text"]) : Or(this["expr"], "\"\"")),
            "call" => CallLike(this["expr"]) ? this["expr"].Trim() : "call " + Or(this["expr"], "nothing()"),
            "return" => this["value"].Trim() == "" ? "return" : $"return {this["value"].Trim()}",
            "if" or "while" => $"{Kind} {Or(this["cond"], "true")}",
            "for" => F.ContainsKey("from") ? $"for {Name(this["var"], "i")} in {Or(this["from"], "1")}..{Or(this["to"], "3")}" : $"for {Name(this["var"], "item")} in {Or(this["source"], "[]")}",
            "use" => Pieces.Line(this["name"], ParseSettings(this["settings"])),
            "note" => "# " + this["text"].Replace("\n", " "),
            _ => this["text"],
        };
    }

    static string Or(string v, string fallback) => v.Trim() == "" ? fallback : v.Trim();

    static string Name(string v, string fallback)
    {
        var n = Regex.Replace(v.Trim(), @"[^\p{L}\w.\[\]]+", "_");
        return n == "" ? fallback : char.IsDigit(n[0]) ? "_" + n : n;
    }

    public static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";

    public static bool CallLike(string s) => Regex.IsMatch(s.Trim(), @"^[\p{L}_][\p{L}\w.]*\s*\(.*\)$");

    /// <summary>Настройки кусочка из поля: «a = 1, b = "x"».</summary>
    public static List<(string Name, string Value)> ParseSettings(string text) =>
        MX.ModxDoc.ReadUse($"use \"x\" ({text})", 1)?.Settings ?? [];
}

public sealed class XBranch
{
    public string Cond = "";
    public List<XBlock> Body = [];
    public int Line;
}

/// <summary>
/// 21.0: файл .modx как блоки и обратно. Разбор ничего не теряет: блок, который не трогали, пишется в код своей
/// строкой; непонятная строка становится блоком «Код» с текстом как есть. Если скобки не сходятся, блоками файл
/// не показывается (Error) — человек правит его кодом.
/// </summary>
public sealed partial class XBlockDoc
{
    /// <summary>Начало файла как есть: комментарии и шапка (mod, version, author, about, game, engine).</summary>
    public readonly List<string> Head = [];
    public readonly List<XBlock> Items = [];
    public string? Error;
    public int ErrorLine;

    static readonly HashSet<string> Headers = new(["mod", "version", "author", "about", "game", "engine"], StringComparer.Ordinal);

    [GeneratedRegex(@"^(hook|native)\s+([\p{L}\w]+)\s*\{$")]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^fn\s+([\p{L}_][\p{L}\w]*)\s*\(([^)]*)\)\s*\{$")]
    private static partial Regex FnStart();

    const string Name = @"[\p{L}_][\p{L}\w.\[\]]*";

    static int Indent(string line) => line.Length - line.TrimStart().Length;

    /// <summary>Код и комментарий строки: # вне кавычек начинает комментарий.</summary>
    static (string Code, string Comment) Split(string line)
    {
        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inStr) { i++; continue; }
            if (line[i] == '"') inStr = !inStr;
            else if (line[i] == '#' && !inStr) return (line[..i].Trim(), line[i..].Trim());
        }
        return (line.Trim(), "");
    }

    static bool IsHeader(string t) => Regex.Match(t, @"^[a-z]+").Value is var w && Headers.Contains(w) && !Regex.IsMatch(t, @"^\w+\s*=");

    public static XBlockDoc Parse(string text)
    {
        var doc = new XBlockDoc();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var head = true;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t == "") continue;
            var (code, comment) = Split(t);
            if (head && (code == "" || IsHeader(code))) { doc.Head.Add(t); continue; }
            head = false;
            if (IsHeader(code) && Indent(lines[i]) == 0) { doc.Head.Add(t); continue; }
            var f = FnStart().Match(code);
            var b = BlockStart().Match(code);
            if (f.Success || b.Success)
            {
                var indent = Indent(lines[i]);
                var end = i + 1;
                while (end < lines.Length && !(lines[end].Trim() == "}" && Indent(lines[end]) <= indent)) end++;
                if (end >= lines.Length) return doc.Fail(i + 1, "err.openBrace");
                XBlock blk;
                if (b.Success && b.Groups[1].Value == "native")
                {
                    // Код на языке движка — блоком как есть, со всеми строками.
                    blk = new XBlock { Kind = "native", Orig = string.Join('\n', lines[i..(end + 1)]).TrimEnd(), Line = i + 1 };
                    blk.F["lang"] = b.Groups[2].Value;
                    blk.F["text"] = blk.Orig;
                }
                else
                {
                    blk = f.Success
                        ? new XBlock { Kind = "fn", Orig = code[..^1].TrimEnd(), Comment = comment, Line = i + 1 }
                        : new XBlock { Kind = "hook", Orig = code[..^1].TrimEnd(), Comment = comment, Line = i + 1 };
                    if (f.Success) { blk.F["name"] = f.Groups[1].Value; blk.F["params"] = f.Groups[2].Value.Trim(); }
                    else blk.F["event"] = b.Groups[2].Value.ToLowerInvariant();
                    var k = 0;
                    var body = lines[(i + 1)..end];
                    blk.Body = doc.Seq(body, ref k, i + 2, out var closer);
                    if (doc.Error is not null) return doc;
                    if (closer is not null) return doc.Fail(i + 2 + k - 1, "err.extraBrace");
                }
                doc.Items.Add(blk);
                i = end;
                continue;
            }
            if (code.StartsWith("use ", StringComparison.Ordinal) && MX.ModxDoc.ReadUse(code, i + 1) is { } use && Indent(lines[i]) == 0)
            {
                var u = new XBlock { Kind = "use", Orig = code, Comment = comment, Line = i + 1 };
                u.F["name"] = use.Name;
                u.F["settings"] = string.Join(", ", use.Settings.Select(s => $"{s.Name} = {s.Value}"));
                doc.Items.Add(u);
                continue;
            }
            doc.Items.Add(Statement(code, comment, i + 1, top: true));
        }
        return doc;
    }

    XBlockDoc Fail(int line, string error)
    {
        Error = error;
        ErrorLine = line;
        return this;
    }

    List<XBlock> Stop(List<XBlock> list, int line, string error)
    {
        Fail(line, error);
        return list;
    }

    /// <summary>Тело хука, функции или блока до закрывающей «}» (её текст — closer: «}», «} else {» или «} else if … {»).</summary>
    List<XBlock> Seq(string[] lines, ref int k, int first, out string? closer)
    {
        var list = new List<XBlock>();
        closer = null;
        while (k < lines.Length && Error is null)
        {
            var no = first + k;
            var (code, comment) = Split(lines[k]);
            k++;
            if (code == "" && comment == "") continue;
            if (code == "") { list.Add(Note(comment, no)); continue; }
            if (code.StartsWith('}')) { closer = code; return list; }
            Match m;
            if ((m = Regex.Match(code, @"^if\s+(.+?)\s*\{$")).Success)
            {
                var blk = new XBlock { Kind = "if", Orig = code[..^1].TrimEnd(), Comment = comment, Line = no };
                blk.F["cond"] = m.Groups[1].Value;
                blk.Body = Seq(lines, ref k, first, out var c);
                while (Error is null && c is not null)
                {
                    Match e;
                    if ((e = Regex.Match(c, @"^\}\s*else\s+if\s+(.+?)\s*\{$")).Success)
                    {
                        var branch = new XBranch { Cond = e.Groups[1].Value, Line = first + k - 1 };
                        branch.Body = Seq(lines, ref k, first, out c);
                        blk.Elifs.Add(branch);
                    }
                    else if (Regex.IsMatch(c, @"^\}\s*else\s*\{$"))
                    {
                        if (blk.Else is not null) return Stop(list, first + k - 1, "else after else");
                        blk.ElseLine = first + k - 1;
                        blk.Else = Seq(lines, ref k, first, out c);
                    }
                    else if (c == "}") break;
                    else return Stop(list, first + k - 1, $"unexpected '{c}'");
                }
                if (Error is not null) return list;
                if (c is null) return Stop(list, no, "err.openBrace");
                list.Add(blk);
                continue;
            }
            if ((m = Regex.Match(code, @"^(while)\s+(.+?)\s*\{$")).Success || (m = Regex.Match(code, @"^(for)\s+([\p{L}_][\p{L}\w]*)\s+in\s+(.+?)\s*\{$")).Success)
            {
                var blk = new XBlock { Kind = m.Groups[1].Value, Orig = code[..^1].TrimEnd(), Comment = comment, Line = no };
                if (blk.Kind == "while") blk.F["cond"] = m.Groups[2].Value;
                else
                {
                    blk.F["var"] = m.Groups[2].Value;
                    var range = Regex.Match(m.Groups[3].Value, @"^([^.""]+?)\s*\.\.\s*([^""]+)$");
                    if (range.Success) { blk.F["from"] = range.Groups[1].Value.Trim(); blk.F["to"] = range.Groups[2].Value.Trim(); }
                    else blk.F["source"] = m.Groups[3].Value;
                }
                blk.Body = Seq(lines, ref k, first, out var c);
                if (Error is not null) return list;
                if (c != "}") return Stop(list, c is null ? no : first + k - 1, c is null ? "err.openBrace" : $"unexpected '{c}'");
                list.Add(blk);
                continue;
            }
            list.Add(Statement(code, comment, no, top: false));
        }
        return list;
    }

    static XBlock Note(string comment, int line)
    {
        var b = new XBlock { Kind = "note", Orig = comment, Line = line };
        b.F["text"] = comment.TrimStart('#').Trim();
        return b;
    }

    /// <summary>Одна строка: что это за команда и какие у неё поля.</summary>
    static XBlock Statement(string code, string comment, int line, bool top)
    {
        if (code == "") return Note(comment, line);
        var b = new XBlock { Orig = code, Comment = comment, Line = line };
        Match m;
        if ((m = Regex.Match(code, @"^(?:let|var)\s+([\p{L}_][\p{L}\w]*)\s*=\s*(.+)$")).Success) Fields(b, "let", ("name", m.Groups[1].Value), ("value", m.Groups[2].Value));
        else if (top) Fields(b, "raw", ("text", code));
        else if ((m = Regex.Match(code, @"^set\s+(" + Name + @")\s*=\s*(.+)$")).Success) Fields(b, "set", ("target", m.Groups[1].Value), ("value", m.Groups[2].Value));
        else if ((m = Regex.Match(code, @"^add\s+(" + Name + @")\s+(.+)$")).Success) Fields(b, "add", ("target", m.Groups[1].Value), ("value", m.Groups[2].Value));
        else if ((m = Regex.Match(code, @"^(" + Name + @")\s*\+=\s*(.+)$")).Success) Fields(b, "add", ("target", m.Groups[1].Value), ("value", m.Groups[2].Value));
        else if ((m = Regex.Match(code, @"^(" + Name + @")\s*(-=|\*=|/=)\s*(.+)$")).Success) Fields(b, "op", ("target", m.Groups[1].Value), ("op", m.Groups[2].Value), ("value", m.Groups[3].Value));
        else if ((m = Regex.Match(code, @"^(say|log)\s+(.+)$")).Success)
        {
            var arg = m.Groups[2].Value.Trim();
            if (Regex.IsMatch(arg, @"^""(?:\\.|[^""\\])*""$")) Fields(b, m.Groups[1].Value, ("text", Regex.Replace(arg[1..^1], @"\\(.)", "$1")));
            else Fields(b, m.Groups[1].Value, ("expr", arg));
        }
        else if ((m = Regex.Match(code, @"^call\s+(.+)$")).Success) Fields(b, "call", ("expr", m.Groups[1].Value));
        else if ((m = Regex.Match(code, @"^return(?:\s+(.+))?$")).Success) Fields(b, "return", ("value", m.Groups[1].Value));
        else if (code is "break" or "continue") b.Kind = code;
        else if ((m = Regex.Match(code, @"^(" + Name + @")\s*=\s*(.+)$")).Success && !MX.ModxDoc.Keywords.Contains(m.Groups[1].Value)) Fields(b, "set", ("target", m.Groups[1].Value), ("value", m.Groups[2].Value));
        else if (XBlock.CallLike(code)) Fields(b, "call", ("expr", code));
        else Fields(b, "raw", ("text", code));
        return b;
    }

    static void Fields(XBlock b, string kind, params (string Key, string Value)[] fields)
    {
        b.Kind = kind;
        foreach (var (k, v) in fields) b.F[k] = v.Trim();
    }

    // ---------------------------------------------------------------- обратно в код

    /// <summary>Файл из блоков: шапка, строки верха, хуки и функции; отступ — четыре пробела. Номера строк обновляются.</summary>
    public string Code()
    {
        var w = new StringBuilder();
        var line = 0;
        void Put(string text) { w.Append(text).Append('\n'); line++; }
        foreach (var h in Head) Put(h);
        XBlock? prev = null;
        foreach (var b in Items)
        {
            var big = b.Kind is "hook" or "fn" or "native";
            if (line > 0 && (prev is null ? Head.Count > 0 : big || prev.Kind is "hook" or "fn" or "native" || prev.Kind != b.Kind && prev.Kind != "note")) Put("");
            Write(b, 0, Put, () => line);
            prev = b;
        }
        return w.ToString().TrimEnd() + "\n";
    }

    static string Tail(XBlock b) => b.Comment == "" ? "" : "  " + b.Comment;

    static void Write(XBlock b, int depth, Action<string> put, Func<int> at)
    {
        var pad = new string(' ', depth * 4);
        b.Line = at() + 1;
        switch (b.Kind)
        {
            case "native":
                foreach (var l in (b.Dirty ? b["text"] : b.Orig).Split('\n')) put(pad + l);
                return;
            case "hook" or "fn" or "if" or "while" or "for":
                put(pad + b.Build() + " {" + Tail(b));
                foreach (var c in b.Body) Write(c, depth + 1, put, at);
                foreach (var e in b.Elifs)
                {
                    e.Line = at() + 1;
                    put(pad + $"}} else if {(e.Cond.Trim() == "" ? "true" : e.Cond.Trim())} {{");
                    foreach (var c in e.Body) Write(c, depth + 1, put, at);
                }
                if (b.Else is not null)
                {
                    b.ElseLine = at() + 1;
                    put(pad + "} else {");
                    foreach (var c in b.Else) Write(c, depth + 1, put, at);
                }
                put(pad + "}");
                return;
            case "note":
                put(pad + (b.Dirty ? b.Build() : b.Orig));
                return;
            default:
                put(pad + b.Build() + Tail(b));
                return;
        }
    }

    // ---------------------------------------------------------------- правка

    /// <summary>Список, в котором лежит блок (верх файла, тело, ветка else if или else).</summary>
    public List<XBlock>? Owner(XBlock b) => Owner(Items, b);

    static List<XBlock>? Owner(List<XBlock> list, XBlock b)
    {
        if (list.Contains(b)) return list;
        foreach (var x in list)
        {
            if (Owner(x.Body, b) is { } o) return o;
            foreach (var e in x.Elifs) if (Owner(e.Body, b) is { } eo) return eo;
            if (x.Else is not null && Owner(x.Else, b) is { } el) return el;
        }
        return null;
    }

    /// <summary>Все блоки по порядку (для поиска ошибок по строке).</summary>
    public IEnumerable<XBlock> All() => Walk(Items);

    static IEnumerable<XBlock> Walk(List<XBlock> list)
    {
        foreach (var x in list)
        {
            yield return x;
            foreach (var c in Walk(x.Body)) yield return c;
            foreach (var e in x.Elifs) foreach (var c in Walk(e.Body)) yield return c;
            if (x.Else is not null) foreach (var c in Walk(x.Else)) yield return c;
        }
    }

    /// <summary>Блок, которому принадлежит строка кода: сама строка блока или строка его ветки.</summary>
    public XBlock? At(int line) => All().LastOrDefault(b => b.Line == line || b.ElseLine == line || b.Elifs.Any(e => e.Line == line));

    /// <summary>Хук события (первый, если их несколько).</summary>
    public XBlock? Hook(string ev) => Items.FirstOrDefault(b => b.Kind == "hook" && b["event"] == ev);

    /// <summary>Новый хук события — после последнего хука или функции.</summary>
    public XBlock AddHook(string ev)
    {
        var hook = XBlock.New("hook", ("event", ev));
        var last = Items.FindLastIndex(b => b.Kind is "hook" or "fn" or "native");
        Items.Insert(last < 0 ? Items.Count : last + 1, hook);
        return hook;
    }

    [SelfTest]
    static string BlocksRoundTrip()
    {
        var samples = new List<string>
        {
            "# Probe — ModScript X\nmod \"Probe\"\nversion 1.0.0\nengine renpy\n\nlet bonus = 5  # change me\nuse \"Double gold\" (gold_bonus = 3)\n\nfn pay(w) {\n    return w * bonus\n}\n\nhook start {\n    # first\n    gold += pay(2)\n    if gold > 10 {\n        say \"Rich: {gold}\"\n    } else if gold > 5 {\n        log \"mid\"\n    } else {\n        say \"Poor \\\"me\\\"\"\n    }\n    for i in 1..3 {\n        add score i\n        if i == 2 {\n            break\n        }\n    }\n    while health < 20 {\n        health = health + 5\n    }\n    speed *= 2\n    notify(\"x\")\n}\n\nnative csharp {\n    // raw\n    int x = 1;\n}\n",
        };
        foreach (var kind in XProjects.Kinds)
            samples.Add(XProjects.Template(new XProject { Name = "Kid " + kind, Kind = kind, Target = "renpy", Events = [.. MX.ModxDoc.Events], Version = "1.0.0" }));
        foreach (var text in samples)
        {
            var doc = Parse(text);
            if (doc.Error is not null) throw new Exception($"parse error {doc.Error} at {doc.ErrorLine}");
            var back = doc.Code();
            if (Shape(back) != Shape(text)) throw new Exception("round trip changed the mod:\n" + back);
            // Правка поля меняет только эту строку, остальное — как было.
            foreach (var b in doc.All().Where(b => b.Kind == "say")) b["text"] = "Hi \"kid\"";
            var edited = doc.Code();
            if (MX.ModxDoc.Parse(edited) is { Ok: false } bad) throw new Exception("edited code broken: " + bad.Diags[0].Message);
        }
        var first = Parse(samples[0]);
        var start = first.Hook("start")!;
        if (start.Body.Count != 7 || start.Body[2] is not { Kind: "if", Elifs.Count: 1, Else.Count: 1 } || start.Body[3] is not { Kind: "for" } loop || loop["to"] != "3")
            throw new Exception("tree: " + string.Join(", ", start.Body.Select(b => b.Kind)));
        if (start.Body[4].Body[0].Kind != "set" || start.Body[5].Kind != "op" || start.Body[6].Kind != "call") throw new Exception("statements");
        var say = start.Body[2].Else![0];
        if (say["text"] != "Poor \"me\"" || say.Build() != "say \"Poor \\\"me\\\"\"") throw new Exception("text escape: " + say.Build());
        // Перенос блока и новый хук — код остаётся верным.
        var moved = start.Body[3];
        start.Body.Remove(moved);
        first.AddHook("tick").Body.Add(moved);
        var code = first.Code();
        var parsed = MX.ModxDoc.Parse(code);
        if (!parsed.Ok || parsed.Hooks.All(h => h.Event != "tick")) throw new Exception("move broke the code:\n" + code);
        if (first.At(first.Hook("tick")!.Body[0].Line) != moved) throw new Exception("line map");
        if (Parse("hook start {\n    if x {\n        say \"a\"\n}\n").Error is null) throw new Exception("open brace not caught");
        return $"{samples.Count} files round-trip through blocks, edits, moves and new hooks stay valid";
    }

    /// <summary>Смысл мода без оформления: хуки, функции, общие переменные и кусочки — для сравнения до и после.</summary>
    static string Shape(string text)
    {
        var d = MX.ModxDoc.Parse(text);
        var w = new StringBuilder($"{d.Name}|{d.Version}|{d.Engine}|{d.Diags.Count}\n");
        foreach (var (n, v) in d.Shared) w.Append($"let {n}={v?.ToJsonString()}\n");
        foreach (var u in d.Uses) w.Append($"use {u.Name} {string.Join(",", u.Settings)}\n");
        foreach (var f in d.Functions) w.Append($"fn {f.Name}({string.Join(",", f.Params)}) {string.Join(";", f.Body.Select(s => $"{s.Kind}:{s.Target}:{s.Expr}"))}\n");
        foreach (var h in d.Hooks) w.Append($"hook {h.Event} {string.Join(";", h.Body.Select(s => $"{s.Kind}:{s.Target}:{s.Expr}"))}\n");
        foreach (var n in d.Natives) w.Append($"native {n.Lang} {n.Code}\n");
        return w.ToString();
    }
}
