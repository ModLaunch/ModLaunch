using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModLaunch.Creator;

/// <summary>Ошибка или подсказка компилятора с номером строки.</summary>
public sealed record Diag(int Line, string Message, bool Warning = false);

public sealed record ConfigEdit(string File, string Section, string Key, string Value);
public sealed record FileCopy(string From, string To);

/// <summary>Что получилось из скрипта: описание мода и список изменений.</summary>
public sealed class ModBuild
{
    public string Name = "";
    public string Version = "1.0.0";
    public string Author = "";
    public string About = "";
    public string Game = "";
    public string? Icon;
    public string? Website;
    public readonly List<string> Needs = [];
    public readonly List<ConfigEdit> Configs = [];
    public readonly List<ConfigEdit> Inis = [];
    public readonly List<FileCopy> Copies = [];
    /// <summary>Изменения Content Patcher (Stardew Valley): готовые объекты для content.json.</summary>
    public readonly List<JsonObject> Changes = [];
    public readonly List<Diag> Diags = [];
    public readonly List<string> Log = [];

    public bool Ok => Diags.All(d => d.Warning);
    public int Statements;
}

/// <summary>
/// ModScript — язык модов ModLaunch. Одна команда на строку, # — комментарий,
/// блоки в фигурных скобках. Переменные (let), арифметика, циклы (for … in /
/// for … from … to), условия Content Patcher (when). Компилируется в настоящие
/// файлы мода: пакет Content Patcher для Stardew Valley, пакет Thunderstore
/// для игр на BepInEx, архив с файлами для остальных.
/// </summary>
public static partial class ModScript
{
    public const string Extension = ".mls";

    /// <summary>Все команды языка — для подсказок и подсветки.</summary>
    public static readonly string[] Keywords =
        ["mod", "version", "author", "about", "game", "icon", "website", "needs", "let", "for", "in", "from", "to", "when", "edit", "entry", "dialogue", "mail", "image", "config", "ini", "copy", "print"];

    // ---------------------------------------------------------------- лексер

    enum T { Word, Str, Sym }
    readonly record struct Tok(T Kind, string Text);

    static List<Tok> Lex(string line, int no, List<Diag> diags)
    {
        var toks = new List<Tok>();
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#') break;
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                var closed = false;
                while (i < line.Length)
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        var n = line[i + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                        i += 2;
                        continue;
                    }
                    if (line[i] == '"') { closed = true; i++; break; }
                    sb.Append(line[i++]);
                }
                if (!closed) diags.Add(new Diag(no, "err.quote"));
                toks.Add(new Tok(T.Str, sb.ToString()));
                continue;
            }
            if ("={}[]".Contains(c)) { toks.Add(new Tok(T.Sym, c.ToString())); i++; continue; }
            var start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i]) && !"={}[]\"#".Contains(line[i])) i++;
            toks.Add(new Tok(T.Word, line[start..i]));
        }
        return toks;
    }

    // ---------------------------------------------------------------- дерево

    sealed class Node
    {
        public int Line;
        public List<Tok> Toks = [];
        public List<Node>? Body;
    }

    static List<Node> Parse(string source, List<Diag> diags)
    {
        var root = new List<Node>();
        var stack = new Stack<(Node? Owner, List<Node> List)>();
        stack.Push((null, root));
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var toks = Lex(lines[n], n + 1, diags);
            if (toks.Count == 0) continue;
            if (toks.Count == 1 && toks[0] is { Kind: T.Sym, Text: "}" })
            {
                if (stack.Count == 1) diags.Add(new Diag(n + 1, "err.extraBrace"));
                else stack.Pop();
                continue;
            }
            var opens = toks[^1] is { Kind: T.Sym, Text: "{" };
            var node = new Node { Line = n + 1, Toks = opens ? toks[..^1] : toks };
            stack.Peek().List.Add(node);
            if (opens)
            {
                node.Body = [];
                stack.Push((node, node.Body));
            }
        }
        while (stack.Count > 1)
        {
            var (owner, _) = stack.Pop();
            diags.Add(new Diag(owner!.Line, "err.openBrace"));
        }
        return root;
    }

    // ---------------------------------------------------------------- выполнение

    sealed class Env
    {
        public readonly Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(string Token, string Value)> When = [];
    }

    [GeneratedRegex(@"\$\{(\w+)\}|\$(\w+)")]
    private static partial Regex VarRef();

    static string Subst(string text, Env env, int line, List<Diag> diags) => VarRef().Replace(text, m =>
    {
        var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        if (env.Vars.TryGetValue(name, out var v)) return v;
        diags.Add(new Diag(line, $"err.unknownVar|{name}"));
        return m.Value;
    });

    /// <summary>Значение после «=»: строка, число, выражение из чисел и переменных.</summary>
    static string Value(List<Tok> toks, Env env, int line, List<Diag> diags)
    {
        if (toks.Count == 0) { diags.Add(new Diag(line, "err.noValue")); return ""; }
        if (toks.Count == 1 && toks[0].Kind == T.Str) return Subst(toks[0].Text, env, line, diags);
        var expr = string.Join(" ", toks.Select(t => t.Kind == T.Str ? "\"" + t.Text + "\"" : t.Text));
        expr = Subst(expr, env, line, diags);
        if (Regex.IsMatch(expr, @"^[\d\s.+\-*/()%]+$") && Regex.IsMatch(expr, @"[+\-*/%]") && Calc(expr) is double d)
            return d.ToString(CultureInfo.InvariantCulture);
        return toks.All(t => t.Kind == T.Str) ? string.Concat(toks.Select(t => Subst(t.Text, env, line, diags))) : expr;
    }

    /// <summary>Маленький калькулятор: + − × ÷ %, скобки, приоритет операций.</summary>
    public static double? Calc(string expr)
    {
        var s = expr.Replace(" ", "");
        var pos = 0;
        double Num()
        {
            if (pos < s.Length && s[pos] == '(') { pos++; var v = Sum(); if (pos < s.Length && s[pos] == ')') pos++; return v; }
            if (pos < s.Length && s[pos] == '-') { pos++; return -Num(); }
            var start = pos;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
            return double.Parse(s[start..pos], CultureInfo.InvariantCulture);
        }
        double Mul()
        {
            var v = Num();
            while (pos < s.Length && s[pos] is '*' or '/' or '%')
            {
                var op = s[pos++];
                var r = Num();
                v = op == '*' ? v * r : op == '/' ? v / r : v % r;
            }
            return v;
        }
        double Sum()
        {
            var v = Mul();
            while (pos < s.Length && s[pos] is '+' or '-')
            {
                var op = s[pos++];
                var r = Mul();
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }
        try { var v = Sum(); return pos == s.Length && double.IsFinite(v) ? Math.Round(v, 6) : null; }
        catch { return null; }
    }

    static JsonNode Json(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l)!;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && value.Contains('.')) return JsonValue.Create(d)!;
        if (value is "true" or "false") return JsonValue.Create(value == "true")!;
        return JsonValue.Create(value)!;
    }

    /// <summary>Скомпилировать скрипт. Ошибки — в Diags (ключи словаря cr.err.*, после «|» — подстановка).</summary>
    public static ModBuild Compile(string source, string? loaderHint = null)
    {
        var build = new ModBuild();
        var tree = Parse(source, build.Diags);
        Run(tree, new Env(), build);
        if (build.Name == "") build.Diags.Add(new Diag(1, "err.noName"));
        if (build.Game == "") build.Diags.Add(new Diag(1, "err.noGame"));
        if (!Regex.IsMatch(build.Version, @"^\d+\.\d+\.\d+$")) build.Diags.Add(new Diag(1, "err.version"));
        // В цикле одна и та же ошибка повторяется — показываем один раз.
        var unique = build.Diags.DistinctBy(d => (d.Line, d.Message)).OrderBy(d => d.Line).ToList();
        build.Diags.Clear();
        build.Diags.AddRange(unique);
        return build;
    }

    static void Run(List<Node> nodes, Env env, ModBuild b)
    {
        foreach (var node in nodes)
        {
            if (b.Statements++ > 5000) { b.Diags.Add(new Diag(node.Line, "err.tooMuch")); return; }
            try { Exec(node, env, b); }
            catch (ScriptError e) { b.Diags.Add(new Diag(node.Line, e.Message)); }
            catch (Exception e) { b.Diags.Add(new Diag(node.Line, "err.generic|" + e.Message)); }
        }
    }

    static void Exec(Node n, Env env, ModBuild b)
    {
        var t = n.Toks;
        var line = n.Line;
        var d = b.Diags;
        var cmd = t[0].Text.ToLowerInvariant();
        string S(int i) => i < t.Count ? Subst(t[i].Text, env, line, d) : "";
        int Eq() => t.FindIndex(x => x is { Kind: T.Sym, Text: "=" });
        void Need(int count) { if (t.Count < count) throw new ScriptError("err.args|" + cmd); }

        if (n.Body is not null && cmd is not ("for" or "when"))
        {
            d.Add(new Diag(line, "err.blockHere|" + cmd));
            return;
        }

        switch (cmd)
        {
            case "mod": Need(2); b.Name = S(1); break;
            case "version": Need(2); b.Version = S(1); break;
            case "author": Need(2); b.Author = S(1); break;
            case "about": Need(2); b.About = string.Join(" ", Enumerable.Range(1, t.Count - 1).Select(S)); break;
            case "game": Need(2); b.Game = S(1).ToLowerInvariant(); break;
            case "icon": Need(2); b.Icon = S(1); break;
            case "website": Need(2); b.Website = S(1); break;
            case "needs":
                Need(2);
                for (var i = 1; i < t.Count; i++) if (!b.Needs.Contains(S(i))) b.Needs.Add(S(i));
                break;
            case "print": b.Log.Add($"{line}: {Value(t.Skip(1).ToList(), env, line, d)}"); break;

            case "let":
            {
                var eq = Eq();
                if (eq != 2 || t[1].Kind != T.Word || !Regex.IsMatch(t[1].Text, @"^[A-Za-z_]\w*$")) throw new ScriptError("err.let");
                env.Vars[t[1].Text] = Value(t.Skip(3).ToList(), env, line, d);
                break;
            }

            case "for":
            {
                if (n.Body is null) throw new ScriptError("err.forBlock");
                if (t.Count < 4 || !Regex.IsMatch(t[1].Text, @"^[A-Za-z_]\w*$")) throw new ScriptError("err.for");
                var name = t[1].Text;
                IEnumerable<string> items;
                if (t[2].Text == "in") items = t.Skip(3).Select(x => Subst(x.Text, env, line, d)).ToList();
                else if (t[2].Text == "from" && t.Count == 6 && t[4].Text == "to"
                    && long.TryParse(S(3), out var from) && long.TryParse(S(5), out var to))
                {
                    if (Math.Abs(to - from) > 1000) throw new ScriptError("err.forRange");
                    var step = to >= from ? 1 : -1;
                    var list = new List<string>();
                    for (var i = from; step > 0 ? i <= to : i >= to; i += step) list.Add(i.ToString(CultureInfo.InvariantCulture));
                    items = list;
                }
                else throw new ScriptError("err.for");
                var had = env.Vars.TryGetValue(name, out var old);
                foreach (var item in items)
                {
                    env.Vars[name] = item;
                    Run(n.Body, env, b);
                }
                if (had) env.Vars[name] = old!; else env.Vars.Remove(name);
                break;
            }

            case "when":
            {
                if (n.Body is null) throw new ScriptError("err.whenBlock");
                var eq = Eq();
                if (eq != 2) throw new ScriptError("err.when");
                env.When.Add((S(1), Value(t.Skip(3).ToList(), env, line, d)));
                Run(n.Body, env, b);
                env.When.RemoveAt(env.When.Count - 1);
                break;
            }

            // Stardew Valley (Content Patcher): поле записи данных.
            case "edit":
            {
                var eq = Eq();
                if (eq != 4) throw new ScriptError("err.edit");
                var field = t[3].Text;
                var value = Json(Value(t.Skip(5).ToList(), env, line, d));
                AddChange(b, env, new JsonObject
                {
                    ["Action"] = "EditData",
                    ["Target"] = S(1),
                    ["Fields"] = new JsonObject { [S(2)] = new JsonObject { [Subst(field, env, line, d)] = value } },
                });
                break;
            }
            case "entry" or "dialogue" or "mail":
            {
                var eq = Eq();
                var need = cmd == "mail" ? 2 : 3;
                if (eq != need) throw new ScriptError("err." + cmd);
                var target = cmd switch { "dialogue" => $"Characters/Dialogue/{S(1)}", "mail" => "Data/mail", _ => S(1) };
                var key = cmd == "mail" ? S(1) : S(2);
                AddChange(b, env, new JsonObject
                {
                    ["Action"] = "EditData",
                    ["Target"] = target,
                    ["Entries"] = new JsonObject { [key] = Json(Value(t.Skip(eq + 1).ToList(), env, line, d)) },
                });
                break;
            }
            case "image":
            {
                if (t.Count != 4 || t[2].Text != "from") throw new ScriptError("err.image");
                var file = S(3).Replace('\\', '/');
                b.Copies.Add(new FileCopy(file, "assets/" + Path.GetFileName(file)));
                AddChange(b, env, new JsonObject { ["Action"] = "Load", ["Target"] = S(1), ["FromFile"] = "assets/" + Path.GetFileName(file) });
                break;
            }

            // BepInEx: строка в .cfg (BepInEx/config).
            case "config" or "ini":
            {
                // config "file.cfg" [Section] Key = value
                var eq = Eq();
                if (t.Count < 7 || t[2] is not { Kind: T.Sym, Text: "[" } || t[4] is not { Kind: T.Sym, Text: "]" } || eq != 6)
                    throw new ScriptError("err." + cmd);
                var file = S(1).Replace('\\', '/');
                if (file.Contains("..") || Path.IsPathRooted(file)) throw new ScriptError("err.path");
                var edit = new ConfigEdit(file, S(3), Subst(t[5].Text, env, line, d), Value(t.Skip(7).ToList(), env, line, d));
                (cmd == "config" ? b.Configs : b.Inis).Add(edit);
                break;
            }

            case "copy":
            {
                if (t.Count != 4 || t[2].Text != "to") throw new ScriptError("err.copy");
                var from = S(1).Replace('\\', '/');
                var to = S(3).Replace('\\', '/');
                if (from.Contains("..") || to.Contains("..") || Path.IsPathRooted(from) || Path.IsPathRooted(to)) throw new ScriptError("err.path");
                b.Copies.Add(new FileCopy(from, to));
                break;
            }

            default:
                d.Add(new Diag(line, "err.unknown|" + t[0].Text));
                break;
        }
    }

    static void AddChange(ModBuild b, Env env, JsonObject change)
    {
        if (env.When.Count > 0)
        {
            var when = new JsonObject();
            foreach (var (token, value) in env.When) when[token] = value;
            change["When"] = when;
        }
        change["LogName"] = $"{change["Action"]} {change["Target"]} #{b.Changes.Count + 1}";
        b.Changes.Add(change);
    }

    sealed class ScriptError(string code) : Exception(code);

    /// <summary>Сообщение для строки ошибки: ключ словаря cr.* и подстановка после «|».</summary>
    public static string Explain(Diag diag, Func<string, (string, object)[], string> t)
    {
        var parts = diag.Message.Split('|', 2);
        var key = parts[0].StartsWith("err.") ? "cr." + parts[0] : parts[0];
        return t(key, [("x", parts.Length > 1 ? parts[1] : "")]);
    }
}
