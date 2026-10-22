using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Creator;

namespace ModScriptX;

/// <summary>
/// Оператор нейтрального хука: set, add, say, log, call, if, elseif, else, end; с 2.0 — ещё let, while,
/// for, return, break, continue. Блоки (if, while, for) закрываются оператором end.
/// </summary>
public sealed record Stmt(string Kind, string Target, string Expr, int Line);

public sealed record Hook(string Event, List<Stmt> Body, int Line);

/// <summary>2.0: своя функция — fn name(a, b) { … }. Вызывается из хуков и других функций, переводится в каждый язык.</summary>
public sealed record Function(string Name, List<string> Params, List<Stmt> Body, int Line);

/// <summary>Вставка на родном языке движка: попадает только в свой язык.</summary>
public sealed record Native(string Lang, string Code, int Line);

/// <summary>
/// Разобранный файл .modx. Верх файла — обычный ModScript (mod, version, let …),
/// плюс строка engine, блоки hook &lt;событие&gt; { … } и native &lt;язык&gt; { … }.
/// </summary>
public sealed partial class ModxDoc
{
    public const string Extension = ".modx";
    public static readonly string[] Events = ["ready", "start", "load", "tick"];
    public static readonly string[] Languages = ["python", "javascript", "lua", "csharp", "gdscript"];

    public string Name = "";
    public string Version = "1.0.0";
    public string Author = "";
    public string About = "";
    public string Game = "";
    public string? Engine;
    /// <summary>Общие переменные (let верхнего уровня) — попадают в каждый язык.</summary>
    public readonly List<(string Name, JsonNode? Value)> Shared = [];
    public readonly List<Hook> Hooks = [];
    public readonly List<Function> Functions = [];
    public readonly List<Native> Natives = [];
    public readonly List<Diag> Diags = [];

    public bool Ok => Diags.All(d => d.Warning);

    /// <summary>Имя мода в виде идентификатора: годится для файла и для кода.</summary>
    public string Id
    {
        get
        {
            var id = Regex.Replace(Name, @"[^\p{L}\p{Nd}]+", "_").Trim('_');
            if (id == "") id = "mod";
            return char.IsDigit(id[0]) ? "m" + id : id;
        }
    }

    [GeneratedRegex(@"^(\s*)(hook|native)\s+([\p{L}\w]+)\s*\{\s*$")]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^(\s*)fn\s+([\p{L}_][\p{L}\w]*)\s*\(([^)]*)\)\s*\{\s*$")]
    private static partial Regex FnStart();

    /// <summary>Встроенные функции языка: одинаково работают в песочнице и в каждом движке (через mx_…).</summary>
    public static readonly string[] Builtins = ["len", "str", "int", "num", "rand", "chance", "min", "max", "abs", "round", "clamp", "range", "contains", "upper", "lower",
        // 13.0: математика, случайный элемент и строки.
        "floor", "ceil", "sqrt", "pow", "sign", "lerp", "pick", "sum", "join", "split", "trim", "replace", "startswith", "endswith"];

    [GeneratedRegex(@"^\s*engine\s+([\p{L}\w-]+)\s*(#.*)?$")]
    private static partial Regex EngineLine();

    [GeneratedRegex(@"^\s*(?:let|var|const)\s+([\p{L}_][\p{L}\w]*)\s*=")]
    private static partial Regex LetLine();

    public static ModxDoc Parse(string source)
    {
        var doc = new ModxDoc();
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var rest = new string[lines.Length];
        var lets = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            rest[i] = "";
            var f = FnStart().Match(lines[i]);
            if (f.Success)
            {
                var indent = f.Groups[1].Value.Length;
                var end = i + 1;
                while (end < lines.Length && !(lines[end].Trim() == "}" && Indent(lines[end]) <= indent)) end++;
                if (end >= lines.Length) doc.Diags.Add(new Diag(i + 1, "err.openBrace"));
                var name = f.Groups[2].Value;
                var pars = f.Groups[3].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (pars.Any(p => !Regex.IsMatch(p, @"^[\p{L}_][\p{L}\w]*$"))) doc.Diags.Add(new Diag(i + 1, $"bad parameters in fn {name}: use fn {name}(a, b)"));
                if (Builtins.Contains(name) || Events.Contains(name)) doc.Diags.Add(new Diag(i + 1, $"'{name}' is a built-in name: pick another function name"));
                else if (doc.Functions.Any(x => x.Name == name)) doc.Diags.Add(new Diag(i + 1, $"fn {name} is defined twice"));
                doc.Functions.Add(new Function(name, pars, ParseHook(lines[(i + 1)..Math.Min(end, lines.Length)], i + 2, doc.Diags), i + 1));
                i = end;
                continue;
            }
            var m = BlockStart().Match(lines[i]);
            if (m.Success)
            {
                var indent = m.Groups[1].Value.Length;
                var end = i + 1;
                while (end < lines.Length && !(lines[end].Trim() == "}" && Indent(lines[end]) <= indent)) end++;
                if (end >= lines.Length) doc.Diags.Add(new Diag(i + 1, "err.openBrace"));
                var body = lines[(i + 1)..Math.Min(end, lines.Length)];
                var word = m.Groups[3].Value.ToLowerInvariant();
                if (m.Groups[2].Value == "native")
                {
                    var lang = NormalizeLang(word);
                    if (lang == null) doc.Diags.Add(new Diag(i + 1, $"unknown language '{word}' (use {string.Join(", ", Languages)})"));
                    else doc.Natives.Add(new Native(lang, Dedent(body), i + 1));
                }
                else
                {
                    if (!Events.Contains(word)) doc.Diags.Add(new Diag(i + 1, $"unknown hook '{word}' (use {string.Join(", ", Events)})"));
                    else doc.Hooks.Add(new Hook(word, ParseHook(body, i + 2, doc.Diags), i + 1));
                }
                i = end;
                continue;
            }
            var e = EngineLine().Match(lines[i]);
            if (e.Success) { doc.Engine = e.Groups[1].Value.ToLowerInvariant(); continue; }
            var l = LetLine().Match(lines[i]);
            if (l.Success && Indent(lines[i]) == 0 && !lets.Contains(l.Groups[1].Value)) lets.Add(l.Groups[1].Value);
            rest[i] = lines[i];
        }

        // Верх файла выполняет обычный компилятор ModScript: метаданные и значения let.
        var build = ModScript.Compile(string.Join('\n', rest));
        doc.Name = build.Name;
        doc.Version = build.Version;
        doc.Author = build.Author;
        doc.About = build.About;
        doc.Game = build.Game;
        // Игра в .modx не обязательна: движок определяется по папке или строке engine.
        doc.Diags.AddRange(build.Diags.Where(d => d.Message != "err.noGame"));
        foreach (var name in lets)
            if (build.Vars.TryGetValue(name, out var v)) doc.Shared.Add((name, ToJson(v)));
        var unique = doc.Diags.DistinctBy(d => (d.Line, d.Message)).OrderBy(d => d.Line).ToList();
        doc.Diags.Clear();
        doc.Diags.AddRange(unique);
        return doc;
    }

    public static string? NormalizeLang(string word) => word switch
    {
        "python" or "py" or "renpy" => "python",
        "javascript" or "js" => "javascript",
        "lua" => "lua",
        "csharp" or "cs" or "c#" => "csharp",
        "gdscript" or "gd" => "gdscript",
        _ => null,
    };

    public static readonly string[] Keywords = ["set", "add", "say", "log", "call", "if", "elif", "else", "while", "for", "in", "let", "return", "break", "continue", "fn", "hook", "native", "and", "or", "not", "true", "false", "null"];

    /// <summary>
    /// Тело хука или функции. 2.0 — полноценный язык: let, присваивание (=, +=, -=), if / else if / else,
    /// while, for … in, return, break, continue и вызов функции отдельной строкой. Блоки закрываются «}».
    /// </summary>
    static List<Stmt> ParseHook(string[] body, int firstLine, List<Diag> diags)
    {
        var list = new List<Stmt>();
        // Что открыто: if, else (у if уже был else) или loop — break и continue можно только внутри цикла.
        var open = new Stack<string>();
        var name = @"[\p{L}_][\p{L}\w.\[\]]*";
        for (var k = 0; k < body.Length; k++)
        {
            var no = firstLine + k;
            var s = StripComment(body[k]).Trim();
            if (s == "") continue;
            if (s.StartsWith('}'))
            {
                var after = s[1..].Trim();
                Match em;
                // Второй else или else if после else дают неверный код в каждом движке.
                if (Regex.IsMatch(after, @"^else\s*\{$"))
                {
                    if (open.Count > 0 && open.Peek() == "else") diags.Add(new Diag(no, "else after else"));
                    else if (open.Count == 0 || open.Peek() != "if") diags.Add(new Diag(no, "else without if"));
                    else { open.Pop(); open.Push("else"); }
                    list.Add(new Stmt("else", "", "", no));
                    continue;
                }
                if ((em = Regex.Match(after, @"^else\s+if\s+(.+?)\s*\{$")).Success)
                {
                    if (open.Count > 0 && open.Peek() == "else") diags.Add(new Diag(no, "else if after else"));
                    else if (open.Count == 0 || open.Peek() != "if") diags.Add(new Diag(no, "else if without if"));
                    list.Add(new Stmt("elseif", "", em.Groups[1].Value, no));
                    continue;
                }
                if (open.Count == 0) diags.Add(new Diag(no, "err.extraBrace"));
                else { open.Pop(); list.Add(new Stmt("end", "", "", no)); }
                if (after != "") diags.Add(new Diag(no, $"unexpected '{after}' after '}}'"));
                continue;
            }
            Match m;
            if ((m = Regex.Match(s, @"^if\s+(.+?)\s*\{$")).Success) { open.Push("if"); list.Add(new Stmt("if", "", m.Groups[1].Value, no)); }
            else if ((m = Regex.Match(s, @"^while\s+(.+?)\s*\{$")).Success) { open.Push("loop"); list.Add(new Stmt("while", "", m.Groups[1].Value, no)); }
            else if ((m = Regex.Match(s, @"^for\s+([\p{L}_][\p{L}\w]*)\s+in\s+(.+?)\s*\{$")).Success)
            {
                open.Push("loop");
                var src = m.Groups[2].Value;
                // for i in 1..10 — числа от и до (включительно), как range(1, 11).
                var dots = Regex.Match(src, @"^(.+?)\.\.(.+)$");
                if (dots.Success && !src.Contains('"')) src = $"range({dots.Groups[1].Value.Trim()}, ({dots.Groups[2].Value.Trim()}) + 1)";
                list.Add(new Stmt("for", m.Groups[1].Value, src, no));
            }
            else if ((m = Regex.Match(s, @"^(?:let|var)\s+([\p{L}_][\p{L}\w]*)\s*=\s*(.+)$")).Success) list.Add(new Stmt("let", m.Groups[1].Value, m.Groups[2].Value, no));
            else if ((m = Regex.Match(s, @"^set\s+(" + name + @")\s*=\s*(.+)$")).Success) list.Add(new Stmt("set", m.Groups[1].Value, m.Groups[2].Value, no));
            else if ((m = Regex.Match(s, @"^add\s+(" + name + @")\s+(.+)$")).Success) list.Add(new Stmt("add", m.Groups[1].Value, m.Groups[2].Value, no));
            else if ((m = Regex.Match(s, @"^(say|log|call)\s+(.+)$")).Success) list.Add(new Stmt(m.Groups[1].Value, "", m.Groups[2].Value, no));
            else if ((m = Regex.Match(s, @"^return(?:\s+(.+))?$")).Success) list.Add(new Stmt("return", "", m.Groups[1].Success ? m.Groups[1].Value : "", no));
            else if (s is "break" or "continue")
            {
                if (!open.Contains("loop")) diags.Add(new Diag(no, $"'{s}' outside of a loop"));
                list.Add(new Stmt(s, "", "", no));
            }
            else if ((m = Regex.Match(s, @"^(" + name + @")\s*(\+=|-=|\*=|/=)\s*(.+)$")).Success)
            {
                var op = m.Groups[2].Value;
                list.Add(op == "+=" ? new Stmt("add", m.Groups[1].Value, m.Groups[3].Value, no)
                    : new Stmt("set", m.Groups[1].Value, $"{m.Groups[1].Value} {op[0]} ({m.Groups[3].Value})", no));
            }
            else if ((m = Regex.Match(s, @"^(" + name + @")\s*=\s*(.+)$")).Success && !Keywords.Contains(m.Groups[1].Value)) list.Add(new Stmt("set", m.Groups[1].Value, m.Groups[2].Value, no));
            else if (Regex.IsMatch(s, @"^[\p{L}_][\p{L}\w.]*\s*\(.*\)$")) list.Add(new Stmt("call", "", s, no));
            else diags.Add(new Diag(no, $"unknown statement '{s}' (use let, set, add, say, log, if, while, for, return or a function call)"));
        }
        for (; open.Count > 0; open.Pop()) list.Add(new Stmt("end", "", "", firstLine + body.Length));
        return list;
    }

    /// <summary># вне строки — комментарий.</summary>
    static string StripComment(string line)
    {
        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inStr) { i++; continue; }
            if (line[i] == '"') inStr = !inStr;
            else if (line[i] == '#' && !inStr) return line[..i];
        }
        return line;
    }

    static int Indent(string line) => line.Length - line.TrimStart().Length;

    static string Dedent(string[] body)
    {
        var cut = body.Where(l => l.Trim() != "").Select(Indent).DefaultIfEmpty(0).Min();
        return string.Join('\n', body.Select(l => l.Length >= cut ? l[cut..] : l.TrimStart())).TrimEnd();
    }

    /// <summary>Значение переменной ModScript (строка) → JSON: число, логическое, список, объект или текст.</summary>
    static JsonNode? ToJson(string v)
    {
        if (v is "true" or "false") return JsonValue.Create(v == "true");
        if (v == "null") return null;
        if (Regex.IsMatch(v, @"^-?\d+(\.\d+)?$"))
            return v.Contains('.') ? JsonValue.Create(double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)) : JsonValue.Create(long.Parse(v));
        if (v.StartsWith('[') || v.StartsWith('{'))
            try { return JsonNode.Parse(v); } catch { /* обычный текст */ }
        return JsonValue.Create(v);
    }
}
