using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Creator;

namespace ModScriptX;

/// <summary>
/// 2.0: песочница ModScript X — свой интерпретатор языка. Выполняет хуки и функции .modx прямо в
/// ModLaunch, без игры: ready → start → load → tick × N. Переменные игры задаются вручную (gold = 100),
/// say и log попадают в вывод, неизвестные функции игры только отмечаются. Те же встроенные функции
/// (len, rand, clamp…), что и в каждом движке. native-блоки здесь не выполняются: это код движка.
/// </summary>
public sealed class Sandbox
{
    public sealed record Result(List<string> Output, Dictionary<string, object?> Vars, List<Diag> Errors, int Steps)
    {
        public bool Ok => Errors.All(e => e.Warning);
    }

    enum Flow { Normal, Break, Continue, Return }

    sealed class StepLimit() : Exception("too many steps: an endless loop?");
    sealed class RunError(string message) : Exception(message);

    readonly ModxDoc _doc;
    readonly Dictionary<string, object?> _globals = new(StringComparer.Ordinal);
    readonly Dictionary<string, Function> _fns;
    readonly List<string> _out = [];
    readonly List<Diag> _errors = [];
    readonly HashSet<string> _guessed = [];
    readonly Random _random;
    readonly int _maxSteps;
    int _steps, _depth, _line;
    string _where = "";

    Sandbox(ModxDoc doc, int maxSteps, int seed)
    {
        _doc = doc;
        _maxSteps = maxSteps;
        _random = new Random(seed);
        _fns = doc.Functions.GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Выполнить мод. game — переменные игры до запуска; ticks — сколько раз вызвать tick.</summary>
    public static Result Run(ModxDoc doc, IDictionary<string, object?>? game = null, int ticks = 3, int maxSteps = 200_000, int seed = 1)
    {
        var s = new Sandbox(doc, maxSteps, seed);
        foreach (var (name, v) in doc.Shared) s._globals[name] = FromJson(v);
        if (game is not null) foreach (var (k, v) in game) s._globals[k] = v;
        if (doc.Natives.Count > 0) s._out.Add($"(native {string.Join(", ", doc.Natives.Select(n => n.Lang).Distinct())}: runs only inside the game)");
        try
        {
            foreach (var ev in ModxDoc.Events)
            {
                var times = ev == "tick" ? Math.Max(0, ticks) : 1;
                foreach (var h in doc.Hooks.Where(h => h.Event == ev))
                    for (var t = 0; t < times; t++)
                    {
                        s._where = ev == "tick" ? $"tick {t + 1}" : ev;
                        s.Exec(Tree(h.Body), new Dictionary<string, object?>(StringComparer.Ordinal));
                    }
            }
        }
        catch (StepLimit e) { s._errors.Add(new Diag(s._line, e.Message)); }
        catch (RunError e) { s._errors.Add(new Diag(s._line, e.Message)); }
        return new Result(s._out, s._globals, s._errors, s._steps);
    }

    /// <summary>«gold = 100», «name = "Bob"», «items = [1, 2]» — переменные игры из текста (по одной на строку).</summary>
    public static Dictionary<string, object?> ParseVars(string text, List<Diag>? errors = null)
    {
        var vars = new Dictionary<string, object?>(StringComparer.Ordinal);
        var empty = new Sandbox(new ModxDoc(), 10_000, 1);
        var no = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            no++;
            var line = raw.Trim();
            if (line == "" || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) { errors?.Add(new Diag(no, "use name = value")); continue; }
            var name = line[..eq].Trim();
            try { vars[name] = empty.Eval(line[(eq + 1)..].Trim(), new()); }
            catch (Exception e) when (e is RunError or StepLimit) { errors?.Add(new Diag(no, e.Message)); }
        }
        return vars;
    }

    // ---------------------------------------------------------------- дерево из плоского списка

    sealed class Node
    {
        public required Stmt S;
        public List<Node> Body = [];
        /// <summary>else if и else: условие (пусто у else) и тело.</summary>
        public List<(string Cond, List<Node> Body)> Branches = [];
    }

    static List<Node> Tree(List<Stmt> list)
    {
        var root = new List<Node>();
        var stack = new Stack<(Node? Owner, List<Node> Into)>();
        stack.Push((null, root));
        foreach (var s in list)
        {
            switch (s.Kind)
            {
                case "if" or "while" or "for":
                {
                    var n = new Node { S = s };
                    stack.Peek().Into.Add(n);
                    stack.Push((n, n.Body));
                    break;
                }
                case "elseif" or "else":
                {
                    if (stack.Count < 2 || stack.Peek().Owner is not { } owner) break;
                    var body = new List<Node>();
                    owner.Branches.Add((s.Kind == "else" ? "" : s.Expr, body));
                    stack.Pop();
                    stack.Push((owner, body));
                    break;
                }
                case "end":
                    if (stack.Count > 1) stack.Pop();
                    break;
                default:
                    stack.Peek().Into.Add(new Node { S = s });
                    break;
            }
        }
        return root;
    }

    // ---------------------------------------------------------------- операторы

    object? _returned;

    Flow Exec(List<Node> nodes, Dictionary<string, object?> locals)
    {
        foreach (var n in nodes)
        {
            if (++_steps > _maxSteps) throw new StepLimit();
            _line = n.S.Line;
            var s = n.S;
            switch (s.Kind)
            {
                case "let": locals[s.Target] = Eval(s.Expr, locals); break;
                case "set": Assign(s.Target, Eval(s.Expr, locals), locals); break;
                case "add": Assign(s.Target, Add(Get(s.Target, locals), Eval(s.Expr, locals)), locals); break;
                case "say": _out.Add($"[{_where}] 💬 {Text(Eval(s.Expr, locals))}"); break;
                case "log": _out.Add($"[{_where}] {Text(Eval(s.Expr, locals))}"); break;
                case "call": Eval(s.Expr, locals); break;
                case "return":
                    _returned = s.Expr == "" ? null : Eval(s.Expr, locals);
                    return Flow.Return;
                case "break": return Flow.Break;
                case "continue": return Flow.Continue;
                case "if":
                {
                    List<Node>? run = Truthy(Eval(s.Expr, locals)) ? n.Body : null;
                    if (run is null)
                        foreach (var (cond, body) in n.Branches)
                            if (cond == "" || Truthy(Eval(cond, locals))) { run = body; break; }
                    if (run is not null && Exec(run, locals) is var f && f != Flow.Normal) return f;
                    break;
                }
                case "while":
                    while (Truthy(Eval(s.Expr, locals)))
                    {
                        if (++_steps > _maxSteps) throw new StepLimit();
                        var f = Exec(n.Body, locals);
                        if (f == Flow.Break) break;
                        if (f == Flow.Return) return f;
                    }
                    break;
                case "for":
                {
                    var items = Eval(s.Expr, locals) switch
                    {
                        List<object?> l => [.. l],
                        string str => str.Select(c => (object?)c.ToString()).ToList(),
                        Dictionary<string, object?> d => d.Keys.Select(k => (object?)k).ToList(),
                        null => [],
                        var other => throw new RunError($"for … in needs a list, got {TypeName(other)}"),
                    };
                    foreach (var item in items)
                    {
                        locals[s.Target] = item;
                        var f = Exec(n.Body, locals);
                        if (f == Flow.Break) break;
                        if (f == Flow.Return) return f;
                    }
                    break;
                }
            }
        }
        return Flow.Normal;
    }

    void Assign(string target, object? value, Dictionary<string, object?> locals)
    {
        // items[0] = 5 и player.hp = 10 (если player — словарь).
        var bracket = target.IndexOf('[');
        if (bracket > 0 && target.EndsWith(']'))
        {
            var owner = Get(target[..bracket], locals);
            var key = Eval(target[(bracket + 1)..^1], locals);
            switch (owner)
            {
                case List<object?> l when key is double d && (int)d >= 0 && (int)d < l.Count: l[(int)d] = value; return;
                case Dictionary<string, object?> m: m[Text(key)] = value; return;
                default: throw new RunError($"cannot set {target}");
            }
        }
        if (locals.ContainsKey(target)) locals[target] = value;
        else if (!_globals.ContainsKey(target) && target.LastIndexOf('.') is > 0 and var dot && Get(target[..dot], locals) is Dictionary<string, object?> obj) obj[target[(dot + 1)..]] = value;
        else _globals[target] = value;
    }

    object? Get(string name, Dictionary<string, object?> locals)
    {
        if (locals.TryGetValue(name, out var v)) return v;
        if (_globals.TryGetValue(name, out v)) return v;
        // player.gold: свойство словаря player.
        var dot = name.LastIndexOf('.');
        if (dot > 0 && Get(name[..dot], locals) is Dictionary<string, object?> obj && obj.TryGetValue(name[(dot + 1)..], out v)) return v;
        // Переменная игры, которую не задали: считаем нулём и предупреждаем один раз.
        if (_guessed.Add(name)) _errors.Add(new Diag(_line, $"'{name}' is not set: using 0 (add it to the game variables)", true));
        _globals[name] = 0d;
        return 0d;
    }

    // ---------------------------------------------------------------- выражения

    object? Eval(string src, Dictionary<string, object?> locals)
    {
        var p = new Parser(src, this, locals);
        var v = p.Expr();
        p.End();
        return v;
    }

    /// <summary>Разбор и вычисление выражения за один проход (приоритеты: or, and, not, сравнения, +, *, унарные).</summary>
    sealed class Parser(string src, Sandbox box, Dictionary<string, object?> locals)
    {
        int _i;

        void Ws() { while (_i < src.Length && char.IsWhiteSpace(src[_i])) _i++; }
        bool Peek(string s) { Ws(); return string.CompareOrdinal(src, _i, s, 0, s.Length) == 0; }
        bool Word(string w)
        {
            Ws();
            if (string.CompareOrdinal(src, _i, w, 0, w.Length) != 0) return false;
            var after = _i + w.Length;
            if (after < src.Length && (char.IsLetterOrDigit(src[after]) || src[after] == '_')) return false;
            _i = after;
            return true;
        }
        bool Take(string s) { if (!Peek(s)) return false; _i += s.Length; return true; }
        void Need(string s) { if (!Take(s)) throw new RunError($"expected '{s}' in: {src}"); }
        public void End() { Ws(); if (_i < src.Length) throw new RunError($"unexpected '{src[_i..]}' in: {src}"); }

        public object? Expr() => Or();

        object? Or()
        {
            var a = And();
            while (Word("or")) { var b = And(); a = Truthy(a) || Truthy(b); }
            return a;
        }

        object? And()
        {
            var a = Not();
            while (Word("and")) { var b = Not(); a = Truthy(a) && Truthy(b); }
            return a;
        }

        object? Not() => Word("not") ? !Truthy(Not()) : Compare();

        object? Compare()
        {
            var a = Sum();
            while (true)
            {
                if (Take("==")) a = Same(a, Sum());
                else if (Take("!=")) a = !Same(a, Sum());
                else if (Take("<=")) a = Order(a, Sum()) <= 0;
                else if (Take(">=")) a = Order(a, Sum()) >= 0;
                else if (Take("<")) a = Order(a, Sum()) < 0;
                else if (Take(">")) a = Order(a, Sum()) > 0;
                else return a;
            }
        }

        object? Sum()
        {
            var a = Product();
            while (true)
            {
                if (Peek("+=") || Peek("-=")) return a;
                if (Take("+")) a = Add(a, Product());
                else if (Take("-")) a = Num(a) - Num(Product());
                else return a;
            }
        }

        object? Product()
        {
            var a = Unary();
            while (true)
            {
                if (Take("*")) a = Num(a) * Num(Unary());
                else if (Take("/"))
                {
                    var b = Num(Unary());
                    if (b == 0) throw new RunError("division by zero");
                    a = Num(a) / b;
                }
                else if (Take("%"))
                {
                    var b = Num(Unary());
                    if (b == 0) throw new RunError("division by zero");
                    a = Num(a) % b;
                }
                else return a;
            }
        }

        object? Unary()
        {
            if (Take("-")) return -Num(Unary());
            if (Take("+")) return Num(Unary());
            return Postfix(Primary());
        }

        object? Postfix(object? v)
        {
            while (Peek("["))
            {
                _i++;
                var key = Expr();
                Need("]");
                v = v switch
                {
                    List<object?> l when key is double d => (int)d >= 0 && (int)d < l.Count ? l[(int)d] : throw new RunError($"index {Text(key)} is outside the list ({l.Count} items)"),
                    Dictionary<string, object?> m => m.GetValueOrDefault(Text(key)),
                    string s when key is double d => (int)d >= 0 && (int)d < s.Length ? s[(int)d].ToString() : throw new RunError($"index {Text(key)} is outside the text"),
                    _ => throw new RunError($"cannot index {TypeName(v)}"),
                };
            }
            return v;
        }

        object? Primary()
        {
            Ws();
            if (_i >= src.Length) throw new RunError("missing value in: " + src);
            var c = src[_i];
            if (c == '(') { _i++; var v = Expr(); Need(")"); return v; }
            if (c == '[')
            {
                _i++;
                var list = new List<object?>();
                if (!Take("]"))
                {
                    do list.Add(Expr()); while (Take(","));
                    Need("]");
                }
                return list;
            }
            if (c == '"') return StringLit();
            if (char.IsDigit(c) || (c == '.' && _i + 1 < src.Length && char.IsDigit(src[_i + 1])))
            {
                var start = _i;
                while (_i < src.Length && (char.IsDigit(src[_i]) || src[_i] == '.' && _i + 1 < src.Length && char.IsDigit(src[_i + 1]))) _i++;
                return double.Parse(src[start.._i], CultureInfo.InvariantCulture);
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = _i;
                while (_i < src.Length && (char.IsLetterOrDigit(src[_i]) || src[_i] is '_' or '.')) _i++;
                var name = src[start.._i].TrimEnd('.');
                _i = start + name.Length;
                switch (name)
                {
                    case "true": return true;
                    case "false": return false;
                    case "null": return null;
                }
                if (Peek("("))
                {
                    _i++;
                    var args = new List<object?>();
                    if (!Take(")"))
                    {
                        do args.Add(Expr()); while (Take(","));
                        Need(")");
                    }
                    return box.Call(name, args);
                }
                return box.Get(name, locals);
            }
            throw new RunError($"unexpected '{c}' in: {src}");
        }

        /// <summary>"текст {выражение}" — подстановка, {{ и }} — сами скобки.</summary>
        string StringLit()
        {
            _i++;
            var sb = new StringBuilder();
            while (_i < src.Length && src[_i] != '"')
            {
                var c = src[_i];
                if (c == '\\' && _i + 1 < src.Length)
                {
                    sb.Append(src[_i + 1] switch { 'n' => '\n', 't' => '\t', var o => o });
                    _i += 2;
                    continue;
                }
                if (c == '{' && _i + 1 < src.Length && src[_i + 1] == '{') { sb.Append('{'); _i += 2; continue; }
                if (c == '}' && _i + 1 < src.Length && src[_i + 1] == '}') { sb.Append('}'); _i += 2; continue; }
                if (c == '{')
                {
                    var close = src.IndexOf('}', _i + 1);
                    if (close < 0) throw new RunError("'{' without '}' in a string");
                    sb.Append(Text(box.Eval(src[(_i + 1)..close], locals)));
                    _i = close + 1;
                    continue;
                }
                sb.Append(c);
                _i++;
            }
            if (_i >= src.Length) throw new RunError("string is not closed with \"");
            _i++;
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------- функции

    object? Call(string name, List<object?> a)
    {
        if (++_steps > _maxSteps) throw new StepLimit();
        if (_fns.TryGetValue(name, out var fn))
        {
            if (a.Count > fn.Params.Count) throw new RunError($"{name}() takes {fn.Params.Count} arguments, got {a.Count}");
            if (++_depth > 200) throw new RunError($"{name}() calls itself too deep");
            var locals = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < fn.Params.Count; i++) locals[fn.Params[i]] = i < a.Count ? a[i] : null;
            var line = _line;
            try
            {
                _returned = null;
                return Exec(Tree(fn.Body), locals) == Flow.Return ? _returned : null;
            }
            finally { _depth--; _line = line; }
        }
        object? A(int i) => i < a.Count ? a[i] : throw new RunError($"{name}() needs {i + 1} arguments");
        switch (name)
        {
            case "len": return A(0) switch { string s => (double)s.Length, List<object?> l => l.Count, Dictionary<string, object?> d => d.Count, null => 0d, var o => throw new RunError($"len() of {TypeName(o)}") };
            case "str": return Text(A(0));
            case "int": return Math.Truncate(Num(A(0) is string s1 && double.TryParse(s1, NumberStyles.Float, CultureInfo.InvariantCulture, out var p1) ? p1 : A(0)));
            case "num": return A(0) is string s2 ? double.TryParse(s2, NumberStyles.Float, CultureInfo.InvariantCulture, out var p2) ? p2 : throw new RunError($"num(\"{s2}\"): not a number") : Num(A(0));
            case "rand": return (double)_random.NextInt64((long)Num(A(0)), (long)Num(A(1)) + 1);
            case "chance": return _random.NextDouble() * 100 < Num(A(0));
            case "min": return Math.Min(Num(A(0)), Num(A(1)));
            case "max": return Math.Max(Num(A(0)), Num(A(1)));
            case "abs": return Math.Abs(Num(A(0)));
            case "round": return Math.Round(Num(A(0)), MidpointRounding.AwayFromZero);
            case "clamp": return Math.Clamp(Num(A(0)), Num(A(1)), Math.Max(Num(A(1)), Num(A(2))));
            case "range":
            {
                double from = a.Count > 1 ? Num(A(0)) : 0, to = a.Count > 1 ? Num(A(1)) : Num(A(0));
                if (to - from > 100_000) throw new RunError("range() is too long");
                var list = new List<object?>();
                for (var i = from; i < to; i++) list.Add(i);
                return list;
            }
            case "contains": return A(0) switch { string s => s.Contains(Text(A(1)), StringComparison.Ordinal), List<object?> l => l.Any(x => Same(x, A(1))), Dictionary<string, object?> d => d.ContainsKey(Text(A(1))), _ => false };
            case "upper": return Text(A(0)).ToUpperInvariant();
            case "lower": return Text(A(0)).ToLowerInvariant();
        }
        // Функция игры (renpy.notify, player.heal…): в песочнице её нет — отмечаем и идём дальше.
        _out.Add($"[{_where}] → {name}({string.Join(", ", a.Select(Show))})  (game function)");
        return null;
    }

    // ---------------------------------------------------------------- значения

    static object? FromJson(JsonNode? v) => v switch
    {
        null => null,
        JsonArray a => a.Select(FromJson).ToList(),
        JsonObject o => o.ToDictionary(kv => kv.Key, kv => FromJson(kv.Value)),
        JsonValue val when val.TryGetValue<bool>(out var b) => b,
        JsonValue val when val.TryGetValue<double>(out var d) => d,
        JsonValue val when val.TryGetValue<long>(out var l) => (double)l,
        JsonValue val when val.TryGetValue<string>(out var s) => s,
        _ => v.ToJsonString(),
    };

    static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s != "",
        List<object?> l => l.Count > 0,
        Dictionary<string, object?> m => m.Count > 0,
        _ => true,
    };

    static double Num(object? v) => v switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        null => 0,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => throw new RunError($"expected a number, got {TypeName(v)} {Show(v)}"),
    };

    static object? Add(object? a, object? b) => a switch
    {
        string or null when b is string => Text(a) + Text(b),
        string => Text(a) + Text(b),
        List<object?> l when b is List<object?> m => l.Concat(m).ToList(),
        _ when b is string => Text(a) + Text(b),
        _ => Num(a) + Num(b),
    };

    static bool Same(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (double x, double y) => x == y,
        (string x, string y) => x == y,
        (bool x, bool y) => x == y,
        _ => false,
    };

    static int Order(object? a, object? b) => a is string x && b is string y ? string.CompareOrdinal(x, y) : Num(a).CompareTo(Num(b));

    static string TypeName(object? v) => v switch { null => "null", double => "number", string => "text", bool => "true/false", List<object?> => "list", _ => "object" };

    /// <summary>Текст значения: 500, а не 500.0; true/false, списки — [1, 2].</summary>
    public static string Text(object? v) => v switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("0.######", CultureInfo.InvariantCulture),
        string s => s,
        List<object?> l => "[" + string.Join(", ", l.Select(Show)) + "]",
        Dictionary<string, object?> m => "{" + string.Join(", ", m.Select(kv => $"\"{kv.Key}\": {Show(kv.Value)}")) + "}",
        _ => v.ToString() ?? "",
    };

    /// <summary>Значение для вывода: строки — в кавычках.</summary>
    public static string Show(object? v) => v is string s ? "\"" + s + "\"" : Text(v);
}
