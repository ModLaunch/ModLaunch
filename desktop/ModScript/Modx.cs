using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Creator;

namespace ModScriptX;

/// <summary>Оператор нейтрального хука: set, add, say, log, if, else, end, call.</summary>
public sealed record Stmt(string Kind, string Target, string Expr, int Line);

public sealed record Hook(string Event, List<Stmt> Body, int Line);

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

    static List<Stmt> ParseHook(string[] body, int firstLine, List<Diag> diags)
    {
        var list = new List<Stmt>();
        var depth = 0;
        for (var k = 0; k < body.Length; k++)
        {
            var no = firstLine + k;
            var s = StripComment(body[k]).Trim();
            if (s == "") continue;
            if (s.StartsWith('}'))
            {
                var after = s[1..].Trim();
                if (Regex.IsMatch(after, @"^else\s*\{$")) { list.Add(new Stmt("else", "", "", no)); continue; }
                if (depth == 0) diags.Add(new Diag(no, "err.extraBrace"));
                else { depth--; list.Add(new Stmt("end", "", "", no)); }
                if (after != "") diags.Add(new Diag(no, $"unexpected '{after}' after '}}'"));
                continue;
            }
            Match m;
            if ((m = Regex.Match(s, @"^if\s+(.+?)\s*\{$")).Success) { depth++; list.Add(new Stmt("if", "", m.Groups[1].Value, no)); }
            else if ((m = Regex.Match(s, @"^set\s+([\p{L}_][\p{L}\w.\[\]]*)\s*=\s*(.+)$")).Success) list.Add(new Stmt("set", m.Groups[1].Value, m.Groups[2].Value, no));
            else if ((m = Regex.Match(s, @"^add\s+([\p{L}_][\p{L}\w.\[\]]*)\s+(.+)$")).Success) list.Add(new Stmt("add", m.Groups[1].Value, m.Groups[2].Value, no));
            else if ((m = Regex.Match(s, @"^(say|log|call)\s+(.+)$")).Success) list.Add(new Stmt(m.Groups[1].Value, "", m.Groups[2].Value, no));
            else diags.Add(new Diag(no, $"unknown statement '{s}' (use set, add, say, log, call, if)"));
        }
        for (; depth > 0; depth--) list.Add(new Stmt("end", "", "", firstLine + body.Length));
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
