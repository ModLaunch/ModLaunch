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
    /// <summary>Файлы из инвентаря креатора (asset "имя" to "путь"): имя предмета → путь в пакете.</summary>
    public readonly List<FileCopy> Assets = [];
    /// <summary>Куски кода, подключённые через use.</summary>
    public readonly List<string> Uses = [];
    /// <summary>Файлы, которые скрипт создаёт сам (write): путь → текст.</summary>
    public readonly List<(string Path, string Text)> Writes = [];
    /// <summary>Изменения Content Patcher (Stardew Valley): готовые объекты для content.json.</summary>
    public readonly List<JsonObject> Changes = [];
    public readonly List<Diag> Diags = [];
    public readonly List<string> Log = [];

    public bool Ok => Diags.All(d => d.Warning);
    public int Statements;
}

/// <summary>
/// ModScript — язык модов ModLaunch. Одна команда на строку, # — комментарий,
/// блоки в фигурных скобках. Переменные (let) и списки ([ … ]), арифметика,
/// функции (upper, join, range…) и свои (fn … { … return … }), циклы (for … in,
/// for … from … to, repeat), if/else с and/or/not, условия Content Patcher (when),
/// куски кода из инвентаря (use) и файлы оттуда же (asset). Компилируется в
/// настоящие файлы мода: пакет Content Patcher для Stardew Valley, пакет
/// Thunderstore для игр на BepInEx, архив с файлами для остальных.
/// </summary>
public static partial class ModScript
{
    public const string Extension = ".mls";

    /// <summary>Все команды языка — для подсказок и подсветки.</summary>
    public static readonly string[] Keywords =
        ["mod", "version", "author", "about", "game", "icon", "website", "needs", "let", "for", "in", "from", "to", "when", "if", "else", "edit", "entry", "dialogue", "mail", "image", "config", "ini", "copy", "write", "json", "print",
         "fn", "return", "call", "use", "asset", "repeat", "push", "warn", "and", "or", "not"];

    /// <summary>Встроенные функции значений: «let x = upper $name».</summary>
    public static readonly string[] Functions =
        ["upper", "lower", "trim", "len", "replace", "join", "split", "first", "last", "at", "range", "round", "floor", "ceil", "min", "max", "abs", "pad", "slug", "sort", "unique", "reverse", "count"];

    // ---------------------------------------------------------------- лексер

    /// <summary>Value — уже посчитанное значение (вложенный вызов в скобках).</summary>
    enum T { Word, Str, Sym, Value }
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
        public Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(string Token, string Value)> When = [];
        /// <summary>Свои функции: fn имя параметры { … }.</summary>
        public readonly Dictionary<string, Node> Fns = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Used = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Declared = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Included = new(StringComparer.OrdinalIgnoreCase);
        public Func<string, string?>? Include;
        public int Depth;
        public int Including;
    }

    sealed class ReturnSignal(string value) : Exception
    {
        public string Value { get; } = value;
    }

    // ---------------------------------------------------------------- списки

    const string ListMark = "\u0002list:";

    static bool IsList(string value) => value.StartsWith(ListMark, StringComparison.Ordinal);

    static List<string> Items(string value)
    {
        if (!IsList(value)) return [value];
        try { return JsonNode.Parse(value[ListMark.Length..]) is JsonArray a ? a.Select(x => x?.GetValue<string>() ?? "").ToList() : []; }
        catch { return []; }
    }

    static string MakeList(IEnumerable<string> items) =>
        ListMark + new JsonArray(items.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()).ToJsonString();

    /// <summary>Список в тексте — через запятую.</summary>
    static string Show(string value) => IsList(value) ? string.Join(", ", Items(value)) : value.StartsWith(JsonMark) ? value[JsonMark.Length..] : value;

    /// <summary>Ближайшее похожее слово — для «может, вы имели в виду …».</summary>
    static string? Closest(string word, IEnumerable<string> options)
    {
        static int Distance(string a, string b)
        {
            // Расстояние с перестановкой соседних букв («pritn» → «print» — одна ошибка).
            static bool Same(char x, char y) => char.ToLowerInvariant(x) == char.ToLowerInvariant(y);
            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (var j = 0; j <= b.Length; j++) d[0, j] = j;
            for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (Same(a[i - 1], b[j - 1]) ? 0 : 1));
                if (i > 1 && j > 1 && Same(a[i - 1], b[j - 2]) && Same(a[i - 2], b[j - 1])) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
            return d[a.Length, b.Length];
        }
        var limit = word.Length <= 3 ? 1 : 2;
        var best = options.Where(o => !o.Equals(word, StringComparison.OrdinalIgnoreCase)).Select(o => (o, Distance(word, o)))
            .Where(x => x.Item2 <= limit).OrderBy(x => x.Item2).FirstOrDefault();
        return best.o;
    }

    [GeneratedRegex(@"\$\{(\w+)\}|\$(\w+)")]
    private static partial Regex VarRef();

    static string Subst(string text, Env env, int line, List<Diag> diags)
    {
        // Ровно «$список» — сам список (для for, join, len); внутри текста — через запятую.
        if (VarRef().Match(text) is { Success: true } whole && whole.Length == text.Length)
            return Lookup(whole, env, line, diags) ?? text;
        return VarRef().Replace(text, m => Lookup(m, env, line, diags) is { } v ? Show(v) : m.Value);
    }

    static string? Lookup(Match m, Env env, int line, List<Diag> diags)
    {
        var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        env.Used.Add(name);
        if (env.Vars.TryGetValue(name, out var v)) return v;
        var hint = Closest(name, env.Vars.Keys);
        diags.Add(new Diag(line, hint is null ? $"err.unknownVar|{name}" : $"err.unknownVarHint|{name}|{hint}"));
        return null;
    }

    /// <summary>Значение после «=»: строка, число, выражение из чисел и переменных.</summary>
    static string Value(List<Tok> toks, Env env, int line, List<Diag> diags)
    {
        if (toks.Count == 0) { diags.Add(new Diag(line, "err.noValue")); return ""; }
        if (toks.Count == 1 && toks[0].Kind == T.Str) return Subst(toks[0].Text, env, line, diags);
        // [ "a" "b" $c ] — список (запятые между элементами можно ставить, можно нет).
        if (toks[0] is { Kind: T.Sym, Text: "[" })
        {
            if (toks[^1] is not { Kind: T.Sym, Text: "]" }) throw new ScriptError("err.list");
            var items = new List<string>();
            foreach (var tok in toks.Skip(1).SkipLast(1))
            {
                var text = tok.Kind == T.Word ? tok.Text.Trim(',') : tok.Text;
                if (tok.Kind == T.Word && text == "") continue;
                items.AddRange(Items(Subst(text, env, line, diags)));
            }
            return MakeList(items);
        }
        if (toks[0].Kind == T.Word && toks.Count > 1)
        {
            var name = toks[0].Text.ToLowerInvariant();
            if (name == "call") return CallFn(toks[1].Text, toks.Skip(2).ToList(), env, line, diags) ?? "";
            if (env.Fns.ContainsKey(name)) return CallFn(name, toks.Skip(1).ToList(), env, line, diags) ?? "";
            if (Functions.Contains(name)) return Builtin(name, Group(toks.Skip(1).ToList(), env, line, diags).Select(t => Arg(t, env, line, diags)).ToList());
        }
        // json "…" — готовый JSON-объект или список (например, запись Data/TriggerActions).
        if (toks.Count == 2 && toks[0] is { Kind: T.Word, Text: "json" } && toks[1].Kind == T.Str)
        {
            var text = Subst(toks[1].Text, env, line, diags);
            try { JsonNode.Parse(text); } catch { diags.Add(new Diag(line, "err.json")); }
            return JsonMark + text;
        }
        var expr = string.Join(" ", toks.Select(t => t.Kind == T.Str ? "\"" + t.Text + "\"" : t.Text));
        expr = Subst(expr, env, line, diags);
        if (Regex.IsMatch(expr, @"^[\d\s.+\-*/()%]+$") && Regex.IsMatch(expr, @"[+\-*/%]") && Calc(expr) is double d)
            return d.ToString(CultureInfo.InvariantCulture);
        return toks.All(t => t.Kind == T.Str) ? string.Concat(toks.Select(t => Subst(t.Text, env, line, diags))) : expr;
    }

    static string Arg(Tok t, Env env, int line, List<Diag> diags) => t.Kind switch
    {
        T.Sym => t.Text,
        T.Value => t.Text,
        _ => Subst(t.Text, env, line, diags),
    };

    /// <summary>
    /// Аргументы функции: «(first $list)» — вложенный вызов, считается сразу.
    /// Скобки у ModScript прилипают к словам, поэтому группа — от «(» до парной «)».
    /// </summary>
    static List<Tok> Group(List<Tok> toks, Env env, int line, List<Diag> diags)
    {
        var result = new List<Tok>();
        for (var i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            // [ … ] — список целиком как один аргумент.
            if (t is { Kind: T.Sym, Text: "[" })
            {
                var end = toks.FindIndex(i, x => x is { Kind: T.Sym, Text: "]" });
                if (end < 0) throw new ScriptError("err.list");
                result.Add(new Tok(T.Value, Value(toks.GetRange(i, end - i + 1), env, line, diags)));
                i = end;
                continue;
            }
            if (t.Kind != T.Word || !t.Text.StartsWith('(')) { result.Add(t); continue; }
            var depth = 0;
            var inner = new List<Tok>();
            var j = i;
            for (; j < toks.Count; j++)
            {
                var x = toks[j];
                if (x.Kind == T.Word) depth += x.Text.Count(c => c == '(') - x.Text.Count(c => c == ')');
                inner.Add(x);
                if (depth <= 0) break;
            }
            if (depth > 0 || inner.Count == 0) { result.Add(t); continue; }
            // Снять внешние скобки: «(first» … «$list)».
            var first = inner[0].Text[1..];
            var last = inner[^1];
            if (inner.Count == 1) inner[0] = inner[0] with { Text = first.EndsWith(')') ? first[..^1] : first };
            else
            {
                inner[0] = inner[0] with { Text = first };
                inner[^1] = last with { Text = last.Text.EndsWith(')') ? last.Text[..^1] : last.Text };
            }
            inner = inner.Where(x => x.Kind != T.Word || x.Text != "").ToList();
            // Внутри — вызов функции или арифметика: Value разберётся сам.
            result.Add(new Tok(T.Value, inner.Count == 0 ? "" : Value(inner, env, line, diags)));
            i = j;
        }
        return result;
    }

    static double Num(string value, string fn) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : Calc(value) ?? throw new ScriptError($"err.number|{fn}");

    static string Fmt(double d) => Math.Round(d, 6).ToString(CultureInfo.InvariantCulture);

    /// <summary>Встроенные функции. Списки внутри — JSON с меткой, наружу — «как есть».</summary>
    static string Builtin(string name, List<string> a)
    {
        void Args(int min, int max) { if (a.Count < min || a.Count > max) throw new ScriptError($"err.fnArgs|{name}"); }
        switch (name)
        {
            case "upper": Args(1, 1); return Show(a[0]).ToUpperInvariant();
            case "lower": Args(1, 1); return Show(a[0]).ToLowerInvariant();
            case "trim": Args(1, 1); return Show(a[0]).Trim();
            case "len" or "count": Args(1, 1); return (IsList(a[0]) ? Items(a[0]).Count : a[0].Length).ToString(CultureInfo.InvariantCulture);
            case "replace": Args(3, 3); return IsList(a[0]) ? MakeList(Items(a[0]).Select(x => x.Replace(a[1], a[2]))) : a[0].Replace(a[1], a[2]);
            case "join": Args(1, 2); return string.Join(a.Count > 1 ? a[1] : ", ", Items(a[0]));
            case "split": Args(1, 2); return MakeList(a[0].Split(a.Count > 1 ? a[1] : ",").Select(x => x.Trim()).Where(x => x != ""));
            case "first": Args(1, 1); return Items(a[0]).FirstOrDefault() ?? "";
            case "last": Args(1, 1); return Items(a[0]).LastOrDefault() ?? "";
            case "at":
            {
                Args(2, 2);
                var list = Items(a[0]);
                var i = (int)Num(a[1], name);
                if (i < 0) i += list.Count;
                return i >= 0 && i < list.Count ? list[i] : throw new ScriptError($"err.index|{a[1]}");
            }
            case "range":
            {
                Args(2, 3);
                var from = (long)Num(a[0], name);
                var to = (long)Num(a[1], name);
                var step = a.Count > 2 ? (long)Num(a[2], name) : to >= from ? 1 : -1;
                if (step == 0 || Math.Abs(to - from) / Math.Abs(step) > 1000) throw new ScriptError("err.forRange");
                var list = new List<string>();
                for (var i = from; step > 0 ? i <= to : i >= to; i += step) list.Add(i.ToString(CultureInfo.InvariantCulture));
                return MakeList(list);
            }
            case "round": Args(1, 2); return Fmt(Math.Round(Num(a[0], name), a.Count > 1 ? (int)Num(a[1], name) : 0, MidpointRounding.AwayFromZero));
            case "floor": Args(1, 1); return Fmt(Math.Floor(Num(a[0], name)));
            case "ceil": Args(1, 1); return Fmt(Math.Ceiling(Num(a[0], name)));
            case "abs": Args(1, 1); return Fmt(Math.Abs(Num(a[0], name)));
            case "min" or "max":
            {
                var nums = a.SelectMany(Items).Select(x => Num(x, name)).ToList();
                if (nums.Count == 0) throw new ScriptError($"err.fnArgs|{name}");
                return Fmt(name == "min" ? nums.Min() : nums.Max());
            }
            case "pad": Args(2, 2); return Show(a[0]).PadLeft((int)Num(a[1], name), '0');
            case "slug": Args(1, 1); return Projects.PackageName(Show(a[0]));
            case "sort":
                Args(1, 1);
                return MakeList(Items(a[0]).OrderBy(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.MaxValue)
                    .ThenBy(x => x, StringComparer.CurrentCultureIgnoreCase));
            case "unique": Args(1, 1); return MakeList(Items(a[0]).Distinct());
            case "reverse": Args(1, 1); return IsList(a[0]) ? MakeList(Items(a[0]).AsEnumerable().Reverse()) : new string(a[0].Reverse().ToArray());
        }
        throw new ScriptError($"err.unknownFn|{name}");
    }

    /// <summary>Вызвать свою функцию: параметры — свои переменные, всё объявленное внутри остаётся внутри.</summary>
    static string? CallFn(string name, List<Tok> args, Env env, int line, List<Diag> diags, ModBuild? b = null)
    {
        if (!env.Fns.TryGetValue(name, out var fn))
        {
            var hint = Closest(name, env.Fns.Keys);
            throw new ScriptError(hint is null ? $"err.unknownFn|{name}" : $"err.unknownFnHint|{name}|{hint}");
        }
        var parameters = fn.Toks.Skip(2).Select(t => t.Text).ToList();
        var values = Group(args, env, line, diags).Select(t => Arg(t, env, line, diags)).ToList();
        if (values.Count != parameters.Count) throw new ScriptError($"err.fnArgs|{name}");
        if (env.Depth >= 32) throw new ScriptError("err.deep");
        var saved = env.Vars;
        env.Vars = new Dictionary<string, string>(saved, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parameters.Count; i++) { env.Vars[parameters[i]] = values[i]; env.Used.Add(parameters[i]); }
        env.Depth++;
        try
        {
            Run(fn.Body!, env, b ?? Scratch(diags));
            return null;
        }
        catch (ReturnSignal r) { return r.Value; }
        finally
        {
            env.Depth--;
            env.Vars = saved;
        }
    }

    /// <summary>Функция, вызванная внутри значения, может и что-то сделать (edit, write) — тогда это идёт в ту же сборку.</summary>
    [ThreadStatic] static ModBuild? _current;
    static ModBuild Scratch(List<Diag> diags) => _current ?? new ModBuild();

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
    public static ModBuild Compile(string source, Func<string, string?>? include = null)
    {
        var build = new ModBuild();
        var tree = Parse(source, build.Diags);
        var env = new Env { Include = include ?? Inventory.Snippet };
        Hoist(tree, env, build.Diags);
        var outer = _current;
        _current = build;
        try { Run(tree, env, build); }
        catch (ScriptError) { } // слишком много команд — уже в списке ошибок
        catch (ReturnSignal) { build.Diags.Add(new Diag(1, "err.return")); }
        finally { _current = outer; }
        foreach (var (name, line) in env.Declared)
            if (!env.Used.Contains(name)) build.Diags.Add(new Diag(line, $"warn.unused|{name}", Warning: true));
        if (build.Name == "") build.Diags.Add(new Diag(1, "err.noName"));
        if (build.Game == "") build.Diags.Add(new Diag(1, "err.noGame"));
        if (!Regex.IsMatch(build.Version, @"^\d+\.\d+\.\d+$")) build.Diags.Add(new Diag(1, "err.version"));
        // В цикле одна и та же ошибка повторяется — показываем один раз.
        var unique = build.Diags.DistinctBy(d => (d.Line, d.Message)).OrderBy(d => d.Line).ToList();
        build.Diags.Clear();
        build.Diags.AddRange(unique);
        return build;
    }

    /// <summary>Функции можно вызывать выше места, где они объявлены.</summary>
    static void Hoist(List<Node> nodes, Env env, List<Diag> diags)
    {
        foreach (var node in nodes.Where(n => n.Toks[0].Text.Equals("fn", StringComparison.OrdinalIgnoreCase)))
        {
            if (node.Body is null || node.Toks.Count < 2 || node.Toks.Skip(1).Any(t => t.Kind != T.Word || !Regex.IsMatch(t.Text, @"^[A-Za-z_]\w*$")))
            {
                diags.Add(new Diag(node.Line, "err.fn"));
                continue;
            }
            var name = node.Toks[1].Text;
            if (Keywords.Contains(name.ToLowerInvariant()) || Functions.Contains(name.ToLowerInvariant())) { diags.Add(new Diag(node.Line, $"err.fnName|{name}")); continue; }
            if (env.Fns.ContainsKey(name) && env.Fns[name] != node) diags.Add(new Diag(node.Line, $"warn.fnTwice|{name}", Warning: true));
            env.Fns[name] = node;
        }
    }

    static void Run(List<Node> nodes, Env env, ModBuild b)
    {
        bool? last = null; // результат последнего if в этом блоке — для else
        foreach (var node in nodes)
        {
            if (b.Statements++ > 5000) { b.Diags.Add(new Diag(node.Line, "err.tooMuch")); throw new ScriptError("err.tooMuch"); }
            var head = node.Toks[0].Text.ToLowerInvariant();
            if (head == "fn") { last = null; continue; } // объявлена заранее (Hoist)
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
            try { Exec(node, env, b); }
            catch (ReturnSignal) { throw; }
            catch (ScriptError e) when (e.Message == "err.tooMuch") { throw; }
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

        if (n.Body is not null && cmd is not ("for" or "when" or "repeat"))
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
            case "print": b.Log.Add($"{line}: {Show(Value(t.Skip(1).ToList(), env, line, d))}"); break;

            case "let":
            {
                var eq = Eq();
                if (eq != 2 || t[1].Kind != T.Word || !Regex.IsMatch(t[1].Text, @"^[A-Za-z_]\w*$")) throw new ScriptError("err.let");
                env.Vars[t[1].Text] = Value(t.Skip(3).ToList(), env, line, d);
                if (env.Including == 0 && env.Depth == 0) env.Declared.TryAdd(t[1].Text, line);
                break;
            }

            case "push":
            {
                // push список значение … — добавить в конец списка
                Need(3);
                if (!env.Vars.TryGetValue(t[1].Text, out var had)) throw new ScriptError($"err.unknownVar|{t[1].Text}");
                env.Used.Add(t[1].Text);
                var list = had == "" ? [] : Items(had);
                for (var i = 2; i < t.Count; i++) list.AddRange(Items(Arg(t[i], env, line, d)));
                env.Vars[t[1].Text] = MakeList(list);
                break;
            }

            case "return":
                if (env.Depth == 0) throw new ScriptError("err.return");
                throw new ReturnSignal(t.Count > 1 ? Value(t.Skip(1).ToList(), env, line, d) : "");

            case "call":
                Need(2);
                CallFn(t[1].Text, t.Skip(2).ToList(), env, line, d, b);
                break;

            case "warn":
                Need(2);
                d.Add(new Diag(line, "warn.user|" + Show(Value(t.Skip(1).ToList(), env, line, d)), Warning: true));
                break;

            case "use":
            {
                // use "имя" — кусок кода из инвентаря креатора (его функции и переменные становятся своими)
                Need(2);
                var name = S(1);
                if (!env.Included.Add(name)) break;
                var code = env.Include?.Invoke(name) ?? throw new ScriptError($"err.use|{name}");
                b.Uses.Add(name);
                var tree = Parse(code, []);
                Relabel(tree, line);
                Hoist(tree, env, d);
                env.Including++;
                try { Run(tree, env, b); }
                finally { env.Including--; }
                break;
            }

            case "asset":
            {
                // asset "имя предмета" to "путь/в/пакете" — файл из инвентаря креатора
                if (t.Count != 4 || t[2].Text != "to") throw new ScriptError("err.asset");
                var to = S(3).Replace('\\', '/');
                if (to.Contains("..") || Path.IsPathRooted(to)) throw new ScriptError("err.path");
                b.Assets.Add(new FileCopy(S(1), to));
                break;
            }

            case "repeat":
            {
                if (n.Body is null) throw new ScriptError("err.forBlock");
                Need(2);
                var times = (long)Num(Value(t.Skip(1).ToList(), env, line, d), "repeat");
                if (times < 0 || times > 1000) throw new ScriptError("err.forRange");
                for (var i = 0; i < times; i++) Run(n.Body, env, b);
                break;
            }

            case "for":
            {
                if (n.Body is null) throw new ScriptError("err.forBlock");
                if (t.Count < 4 || !Regex.IsMatch(t[1].Text, @"^[A-Za-z_]\w*$")) throw new ScriptError("err.for");
                var name = t[1].Text;
                IEnumerable<string> items;
                if (t[2].Text == "in") items = t[3] is { Kind: T.Sym, Text: "[" } ? Items(Value(t.Skip(3).ToList(), env, line, d))
                    : t.Skip(3).SelectMany(x => Items(Subst(x.Text, env, line, d))).ToList();
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

            case "write":
            {
                // write "plugins/MyMod/readme.txt" = "текст"
                var eq = Eq();
                if (eq != 2) throw new ScriptError("err.write");
                var path = S(1).Replace('\\', '/');
                if (path.Contains("..") || Path.IsPathRooted(path)) throw new ScriptError("err.path");
                var text = Value(t.Skip(3).ToList(), env, line, d);
                b.Writes.Add((path, IsList(text) ? string.Join("\n", Items(text)) : text.StartsWith(JsonMark) ? text[JsonMark.Length..] : text));
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
                if (env.Fns.ContainsKey(t[0].Text)) { CallFn(t[0].Text, t.Skip(1).ToList(), env, line, d, b); break; }
                var hint = Closest(t[0].Text, Keywords.Concat(env.Fns.Keys));
                d.Add(new Diag(line, hint is null ? "err.unknown|" + t[0].Text : $"err.unknownHint|{t[0].Text}|{hint}"));
                break;
        }
    }

    /// <summary>
    /// Условие if: «a == b», «a != b», «a &gt; b» (и &lt;, &gt;=, &lt;=), «a contains b», «a in список»
    /// или просто значение; несколько — через and / or (and сильнее), not — отрицание.
    /// </summary>
    static bool Cond(List<Tok> t, Env env, int line, List<Diag> d)
    {
        if (t.Count == 0) throw new ScriptError("err.if");
        static List<List<Tok>> SplitBy(List<Tok> toks, string word)
        {
            var parts = new List<List<Tok>> { new() };
            foreach (var tok in toks)
                if (tok.Kind == T.Word && tok.Text.Equals(word, StringComparison.OrdinalIgnoreCase)) parts.Add([]);
                else parts[^1].Add(tok);
            return parts;
        }
        var any = SplitBy(t, "or");
        if (any.Count > 1) return any.Select(p => p.Count == 0 ? throw new ScriptError("err.if") : p).ToList().Any(p => Cond(p, env, line, d));
        var all = SplitBy(t, "and");
        if (all.Count > 1) return all.Select(p => p.Count == 0 ? throw new ScriptError("err.if") : p).ToList().All(p => Cond(p, env, line, d));
        if (t[0] is { Kind: T.Word } w && w.Text.Equals("not", StringComparison.OrdinalIgnoreCase)) return !Cond(t.Skip(1).ToList(), env, line, d);
        return Compare(t, env, line, d);
    }

    static bool Compare(List<Tok> t, Env env, int line, List<Diag> d)
    {
        if (t.Count == 0) throw new ScriptError("err.if");
        for (var i = 0; i < t.Count; i++)
        {
            var x = t[i];
            if (x.Kind == T.Str) continue;
            string? op = x.Text switch { "==" or "!=" or ">" or "<" or ">=" or "<=" or "contains" or "in" => x.Text, "=" or "!" when x.Kind == T.Sym || x.Text == "!" => x.Text, _ => null };
            if (op is null) continue;
            var skip = 1;
            if (op is "=" or "!" or ">" or "<" && i + 1 < t.Count && t[i + 1] is { Kind: T.Sym, Text: "=" }) { op += "="; skip = 2; }
            if (op is "=" or "!") throw new ScriptError("err.if");
            var left = Value(t.Take(i).ToList(), env, line, d);
            var right = Value(t.Skip(i + skip).ToList(), env, line, d);
            if (op == "in") return Items(right).Contains(Show(left), StringComparer.OrdinalIgnoreCase);
            if (op == "contains" && IsList(left)) return Items(left).Contains(Show(right), StringComparer.OrdinalIgnoreCase);
            left = Show(left);
            right = Show(right);
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
        if (IsList(v)) return Items(v).Count > 0;
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

    /// <summary>Ошибки во вставленном куске кода показываем на строке use.</summary>
    static void Relabel(List<Node> nodes, int line)
    {
        foreach (var n in nodes)
        {
            n.Line = line;
            if (n.Body is not null) Relabel(n.Body, line);
        }
    }

    /// <summary>Сообщение для строки ошибки: ключ словаря cr.* и подстановки после «|» ({x}, {y}).</summary>
    public static string Explain(Diag diag, Func<string, (string, object)[], string> t)
    {
        var parts = diag.Message.Split('|', 3);
        var key = parts[0].StartsWith("err.") || parts[0].StartsWith("warn.") ? "cr." + parts[0] : parts[0];
        return t(key, [("x", parts.Length > 1 ? parts[1] : ""), ("y", parts.Length > 2 ? parts[2] : "")]);
    }

    [GeneratedRegex(@"^(\s*)use\s+""([^""]+)""\s*(#.*)?$", RegexOptions.Multiline)]
    private static partial Regex UseLine();

    /// <summary>
    /// Перед публикацией: «use» заменяется самим кодом — у того, кто скачает мод,
    /// этого куска в инвентаре нет, а собраться мод должен у всех одинаково.
    /// </summary>
    public static string Flatten(string source, Func<string, string?>? include = null)
    {
        include ??= Inventory.Snippet;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Expand(string text, int depth) => UseLine().Replace(text, m =>
        {
            var name = m.Groups[2].Value;
            if (depth > 8 || !seen.Add(name) || include(name) is not { } code) return m.Value;
            var indent = m.Groups[1].Value;
            var body = Expand(code.Replace("\r\n", "\n"), depth + 1).TrimEnd('\n');
            return $"{indent}# ── use \"{name}\" ──\n{body}\n{indent}# ── конец \"{name}\" ──";
        });
        return Expand(source, 0);
    }
}
