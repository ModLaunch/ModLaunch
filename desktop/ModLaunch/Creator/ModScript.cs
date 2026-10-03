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
    /// <summary>Файлы, которые скрипт создаёт сам (write): путь → текст.</summary>
    public readonly List<(string Path, string Text)> Writes = [];
    /// <summary>Изменения Content Patcher (Stardew Valley): готовые объекты для content.json.</summary>
    public readonly List<JsonObject> Changes = [];
    public readonly List<Diag> Diags = [];
    public readonly List<string> Log = [];
    /// <summary>Переменные в конце скрипта — для панели «Переменные» и проверок.</summary>
    public readonly Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Значения переменных после выполнения скрипта.</summary>
    public static Dictionary<string, string> Debug(string source) => Compile(source).Vars;

    /// <summary>Все команды языка — для подсказок и подсветки.</summary>
    public static readonly string[] Keywords =
        ["mod", "version", "author", "about", "game", "icon", "website", "needs", "let", "for", "in", "from", "to", "when", "if", "else", "edit", "entry", "dialogue", "mail", "image", "config", "ini", "copy", "write", "json", "print",
         "def", "return", "while", "break", "continue", "import", "using", "and", "or", "not"];

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
            if (toks[0] is { Kind: T.Sym, Text: "}" })
            {
                if (stack.Count == 1) diags.Add(new Diag(n + 1, "err.extraBrace"));
                else stack.Pop();
                // «} else {» — закрыть блок и сразу открыть следующий.
                toks = toks[1..];
                if (toks.Count == 0) continue;
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
        /// <summary>Свои функции (def) — общие для всего скрипта.</summary>
        public Dictionary<string, UserFn> Funcs = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Глубина вызовов своих функций — общая на скрипт.</summary>
        public int[] Depth = [0];
        public ModBuild? Build;
    }

    sealed record UserFn(List<(string Name, string? Default)> Params, List<Node> Body);

    /// <summary>return / break / continue — прерывают выполнение блока.</summary>
    abstract class FlowSignal : Exception;
    sealed class ReturnSignal(object? value) : FlowSignal { public object? Value { get; } = value; }
    sealed class BreakSignal : FlowSignal;
    sealed class ContinueSignal : FlowSignal;

    static void RunBody(List<Node> body, Env env, ModBuild b) => Run(body, env, b);

    /// <summary>Нужен полный вычислитель: вызов функции, список, логика или Python-операторы.</summary>
    static bool NeedsEval(List<Tok> toks) =>
        toks.Any(t => t.Kind == T.Word && (t.Text.Contains('(') || t.Text is "and" or "or" or "not" or "**" or "//" or "in" or "&&" or "||" or "if"
                                          || t.Text.StartsWith('\'') || t.Text.Contains("::") || Regex.IsMatch(t.Text, @"^[A-Za-z_]\w*(\.[A-Za-z_]\w*)+$"))
                      || t is { Kind: T.Sym, Text: "[" or "{" });

    /// <summary>Вычислитель нужен и когда в выражении есть переменная без $: hp * 2, n < 10.</summary>
    static bool UsesEval(List<Tok> toks, Env env) => NeedsEval(toks) || toks.Count > 1 && toks.Any(t => t.Kind == T.Word && env.Vars.ContainsKey(t.Text));

    /// <summary>Текст выражения из токенов: строки снова в кавычках (с подстановкой $x), «> =» → «>=».</summary>
    static string ExprText(List<Tok> toks, Env env, int line, List<Diag> d)
    {
        var sb = new StringBuilder();
        foreach (var t in toks)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(t.Kind == T.Str ? Quote(Subst(t.Text, env, line, d)) : t.Text);
        }
        return Regex.Replace(sb.ToString(), @"([<>!=])\s+=", "$1=");
    }

    static string Quote(string s) => JsonValue.Create(s).ToJsonString(JsonOut);

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
        // json "…" — готовый JSON-объект или список (например, запись Data/TriggerActions).
        if (toks.Count == 2 && toks[0] is { Kind: T.Word, Text: "json" } && toks[1].Kind == T.Str)
        {
            var text = Subst(toks[1].Text, env, line, diags);
            try { JsonNode.Parse(text); } catch { diags.Add(new Diag(line, "err.json")); }
            return JsonMark + text;
        }
        if (UsesEval(toks, env) && env.Build is { } eb) return ToText(Eval(ExprText(toks, env, line, diags), env, eb, line));
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

    const string JsonMark = "\u0001json:";

    static JsonNode Json(string value)
    {
        if (value.StartsWith(JsonMark)) { try { return JsonNode.Parse(value[JsonMark.Length..]) ?? JsonValue.Create("")!; } catch { return JsonValue.Create("")!; } }
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
        var env = new Env { Build = build };
        Run(tree, env, build);
        foreach (var (k, v) in env.Vars) build.Vars[k] = v;
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
        bool? last = null; // результат последнего if в этом блоке — для else
        foreach (var node in nodes)
        {
            if (b.Statements++ > 50000) { b.Diags.Add(new Diag(node.Line, "err.tooMuch")); return; }
            var head = node.Toks[0].Text.ToLowerInvariant();
            try
            {
                if (head == "if")
                {
                    if (node.Body is null) throw new ScriptError("err.ifBlock");
                    var ok = Cond(node.Toks.Skip(1).ToList(), env, node.Line, b.Diags);
                    if (ok) Run(node.Body, env, b);
                    last = ok;
                    continue;
                }
                if (head == "else")
                {
                    if (last is null) throw new ScriptError("err.else");
                    if (node.Body is null) throw new ScriptError("err.ifBlock");
                    if (node.Toks.Count > 1 && node.Toks[1].Text == "if")
                    {
                        var ok = last == false && Cond(node.Toks.Skip(2).ToList(), env, node.Line, b.Diags);
                        if (ok) Run(node.Body, env, b);
                        last = last == true || ok;
                        continue;
                    }
                    if (last == false) Run(node.Body, env, b);
                    last = null;
                    continue;
                }
            }
            catch (ScriptError e) { b.Diags.Add(new Diag(node.Line, e.Message)); continue; }
            last = null;
            try { if (!Flow(node, head, env, b)) Exec(node, env, b); }
            catch (FlowSignal) { throw; }
            catch (ScriptError e) { b.Diags.Add(new Diag(node.Line, e.Message)); }
            catch (Exception e) { b.Diags.Add(new Diag(node.Line, "err.generic|" + e.Message)); }
        }
    }

    /// <summary>Конструкции в духе Python/C++: def, return, while, break, continue, import, вызов функции строкой, let x += 1.</summary>
    static bool Flow(Node n, string head, Env env, ModBuild b)
    {
        var t = n.Toks;
        var line = n.Line;
        var text = string.Join(" ", t.Select(x => x.Kind == T.Str ? Quote(x.Text) : x.Text));
        switch (head)
        {
            case "import" or "using" or "#include" or "include":
                return true; // всё уже подключено — строки для привычки из Python и C++
            case "from" when text.Contains(" import "):
                return true;
            case "def" or "func" or "function" or "fn":
            {
                if (n.Body is null) throw new ScriptError("err.defBlock");
                // Значения по умолчанию: def f(a, b = 2) — «=» лексер отдаёт отдельно, склеиваем заново.
                var raw = string.Join(" ", t.Skip(1).Select(x => x.Kind == T.Str ? Quote(x.Text) : x.Text));
                var m = Regex.Match(raw, @"^([A-Za-z_][\w.]*)\s*\((.*)\)\s*(->.*)?:?$");
                if (!m.Success) throw new ScriptError("err.def");
                var pars = new List<(string, string?)>();
                foreach (var p in m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var eq = p.IndexOf('=');
                    var name = Regex.Replace(eq < 0 ? p : p[..eq], @"[:].*$|^(int|float|double|auto|string|bool|const|var|let)\s+|[&*]", "").Trim();
                    if (!Regex.IsMatch(name, @"^[A-Za-z_]\w*$")) throw new ScriptError("err.def");
                    pars.Add((name, eq < 0 ? null : ToText(Eval(p[(eq + 1)..], env, b, line))));
                }
                env.Funcs[m.Groups[1].Value] = new UserFn(pars, n.Body);
                return true;
            }
            case "return":
                throw new ReturnSignal(t.Count > 1 ? Eval(ExprText(t.Skip(1).ToList(), env, line, b.Diags), env, b, line) : null);
            case "break":
                throw new BreakSignal();
            case "continue":
                throw new ContinueSignal();
            case "while":
            {
                if (n.Body is null) throw new ScriptError("err.whileBlock");
                var cond = t.Skip(1).ToList();
                for (var guard = 0; Cond(cond, env, line, b.Diags); guard++)
                {
                    if (guard >= 10000) throw new ScriptError("err.loop");
                    try { Run(n.Body, env, b); }
                    catch (BreakSignal) { break; }
                    catch (ContinueSignal) { }
                }
                return true;
            }
            case "let" or "var" or "auto" or "const" when t.Count >= 4 && t[2] is { Kind: T.Word, Text: "+" or "-" or "*" or "/" or "%" or "//" or "**" } && t[3] is { Kind: T.Sym, Text: "=" }:
            {
                // let x += 1
                var name = t[1].Text;
                if (!env.Vars.TryGetValue(name, out var old)) throw new ScriptError("err.unknownVar|" + name);
                env.Vars[name] = ToText(Eval($"{name} {t[2].Text} ({ExprText(t.Skip(4).ToList(), env, line, b.Diags)})", env, b, line));
                return true;
            }
            case "var" or "auto" or "const" or "local" when t.Count >= 3:
                // var x = 1 / auto x = 1 — как let
                return false;
        }
        // Строка-вызов: my_func(1, 2) или print("…") в стиле Python.
        if (n.Body is null && t[0].Kind == T.Word && Regex.IsMatch(t[0].Text, @"^[A-Za-z_][\w.:]*\(") && !Keywords.Contains(head))
        {
            Eval(ExprText(t, env, line, b.Diags), env, b, line);
            return true;
        }
        // x = 5 без let — тоже присваивание (если x — обычное имя, а не команда).
        if (n.Body is null && t.Count >= 3 && t[1] is { Kind: T.Sym, Text: "=" } && t[0].Kind == T.Word && Regex.IsMatch(t[0].Text, @"^[A-Za-z_]\w*$") && !Keywords.Contains(head))
        {
            env.Vars[t[0].Text] = Value(t.Skip(2).ToList(), env, line, b.Diags);
            return true;
        }
        // x += 1 без let
        if (n.Body is null && t.Count >= 3 && t[1] is { Kind: T.Word, Text: "+" or "-" or "*" or "/" or "%" } && t[2] is { Kind: T.Sym, Text: "=" } && env.Vars.ContainsKey(t[0].Text))
        {
            env.Vars[t[0].Text] = ToText(Eval($"{t[0].Text} {t[1].Text} ({ExprText(t.Skip(3).ToList(), env, line, b.Diags)})", env, b, line));
            return true;
        }
        return false;
    }

    static void Exec(Node n, Env env, ModBuild b)
    {
        var t = n.Toks;
        var line = n.Line;
        var d = b.Diags;
        var cmd = t[0].Text.ToLowerInvariant();
        if (cmd is "var" or "auto" or "const" or "local") cmd = "let";
        string S(int i) => i < t.Count ? Subst(t[i].Text, env, line, d) : "";
        int Eq() => t.FindIndex(x => x is { Kind: T.Sym, Text: "=" });
        void Need(int count) { if (t.Count < count) throw new ScriptError("err.args|" + cmd); }

        if (n.Body is not null && cmd is not ("for" or "when" or "while" or "def"))
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
                if (t.Count == 4 && t[3].Kind == T.Word && env.Vars.TryGetValue(t[3].Text, out var bare) && FromText(bare) is List<object?> or Dictionary<string, object?>) t = [.. t[..3], new Tok(T.Word, "$" + t[3].Text)];
                if (t[2].Text == "in" && (NeedsEval(t.Skip(3).ToList()) || t.Count == 4 && t[3].Kind == T.Word && FromText(Subst(t[3].Text, env, line, d)) is List<object?> or Dictionary<string, object?>))
                {
                    // for x in range(10) / for x in $list / for x in ["a", "b"]
                    var v = t.Count == 4 && !NeedsEval(t.Skip(3).ToList()) ? FromText(Subst(t[3].Text, env, line, d)) : Eval(ExprText(t.Skip(3).ToList(), env, line, d), env, b, line);
                    items = (v switch { List<object?> l => l, Dictionary<string, object?> m => m.Keys.Select(k => (object?)k).ToList(), string s => s.Select(c => (object?)c.ToString()).ToList(), _ => [v] }).Select(ToText).ToList();
                }
                else if (t[2].Text == "in") items = t.Skip(3).Select(x => Subst(x.Text, env, line, d)).ToList();
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
                    try { Run(n.Body, env, b); }
                    catch (BreakSignal) { break; }
                    catch (ContinueSignal) { }
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

            case "write":
            {
                // write "plugins/MyMod/readme.txt" = "текст"
                var eq = Eq();
                if (eq != 2) throw new ScriptError("err.write");
                var path = S(1).Replace('\\', '/');
                if (path.Contains("..") || Path.IsPathRooted(path)) throw new ScriptError("err.path");
                var text = Value(t.Skip(3).ToList(), env, line, d);
                b.Writes.Add((path, text.StartsWith(JsonMark) ? text[JsonMark.Length..] : text));
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

    /// <summary>Условие if: «a == b», «a != b», «a &gt; b» (и &lt;, &gt;=, &lt;=), «a contains b» или просто значение.</summary>
    static bool Cond(List<Tok> t, Env env, int line, List<Diag> d)
    {
        if (t.Count == 0) throw new ScriptError("err.if");
        if (UsesEval(t, env) && env.Build is { } eb) return Truthy(Eval(ExprText(t, env, line, d), env, eb, line));
        for (var i = 0; i < t.Count; i++)
        {
            var x = t[i];
            if (x.Kind == T.Str) continue;
            string? op = x.Text switch { "==" or "!=" or ">" or "<" or ">=" or "<=" or "contains" => x.Text, "=" or "!" when x.Kind == T.Sym || x.Text == "!" => x.Text, _ => null };
            if (op is null) continue;
            var skip = 1;
            if (op is "=" or "!" or ">" or "<" && i + 1 < t.Count && t[i + 1] is { Kind: T.Sym, Text: "=" }) { op += "="; skip = 2; }
            if (op is "=" or "!") throw new ScriptError("err.if");
            var left = Value(t.Take(i).ToList(), env, line, d);
            var right = Value(t.Skip(i + skip).ToList(), env, line, d);
            var num = double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) & double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var c);
            return op switch
            {
                "==" => num ? a == c : left == right,
                "!=" => num ? a != c : left != right,
                ">" => num ? a > c : string.CompareOrdinal(left, right) > 0,
                "<" => num ? a < c : string.CompareOrdinal(left, right) < 0,
                ">=" => num ? a >= c : string.CompareOrdinal(left, right) >= 0,
                "<=" => num ? a <= c : string.CompareOrdinal(left, right) <= 0,
                _ => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            };
        }
        var v = Value(t, env, line, d);
        return v is not ("" or "0" or "false" or "no");
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
