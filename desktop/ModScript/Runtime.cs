using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
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

    sealed class StepLimit(string message = "too many steps: an endless loop?") : Exception(message);
    sealed class RunError(string message) : Exception(message);

    /// <summary>Вложенность скобок, not, минусов и блоков: глубже — ошибка мода, а не переполнение стека (оно убивает весь процесс).</summary>
    const int MaxNest = 200;
    /// <summary>Самая длинная строка или список: склейка s = s + s иначе за 30 шагов съедает всю память.</summary>
    const int MaxSize = 1_000_000;
    /// <summary>Сколько знаков показывает текст списка или словаря; строк вывода и знаков в строке вывода.</summary>
    const int MaxShow = 100_000, MaxLines = 1000, MaxLine = 2000;
    /// <summary>Запасной предел по времени: песочница идёт в потоке интерфейса, мод не должен его вешать.</summary>
    static readonly TimeSpan MaxTime = TimeSpan.FromSeconds(3);

    readonly ModxDoc _doc;
    readonly Dictionary<string, object?> _globals = new(StringComparer.Ordinal);
    readonly Dictionary<string, Function> _fns;
    readonly List<string> _out = [];
    readonly List<Diag> _errors = [];
    readonly HashSet<string> _guessed = [];
    readonly Random _random;
    readonly int _maxSteps;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    int _steps, _depth, _nest, _line;
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
        // Запасной выход: любая другая ошибка .NET — тоже ошибка мода, а не падение ModLaunch на запуске игры.
        catch (Exception e) { s._errors.Add(new Diag(s._line, "sandbox error: " + e.Message)); }
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
            empty._nest = 0;
            try { vars[name] = empty.Eval(line[(eq + 1)..].Trim(), new()); }
            catch (Exception e) { errors?.Add(new Diag(no, e.Message)); }
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
            Step();
            _line = n.S.Line;
            var s = n.S;
            switch (s.Kind)
            {
                case "let": locals[s.Target] = Eval(s.Expr, locals); break;
                case "set": Assign(s.Target, Eval(s.Expr, locals), locals); break;
                // items[0] += 1: текущее значение — выражением, как в Assign (Get не знает скобок).
                case "add": Assign(s.Target, Add(Eval(s.Target, locals), Eval(s.Expr, locals)), locals); break;
                case "say": Print($"[{_where}] 💬 {Text(Eval(s.Expr, locals))}"); break;
                case "log": Print($"[{_where}] {Text(Eval(s.Expr, locals))}"); break;
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
                    if (run is not null && Block(run, locals) is var f && f != Flow.Normal) return f;
                    break;
                }
                case "while":
                    while (Truthy(Eval(s.Expr, locals)))
                    {
                        Step();
                        var f = Block(n.Body, locals);
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
                    Cost(items.Count);
                    foreach (var item in items)
                    {
                        // Шаг на каждый проход, как у while: иначе пустой for в for — 10^10 проходов за 100 тысяч шагов.
                        Step();
                        locals[s.Target] = item;
                        var f = Block(n.Body, locals);
                        if (f == Flow.Break) break;
                        if (f == Flow.Return) return f;
                    }
                    break;
                }
            }
        }
        return Flow.Normal;
    }

    /// <summary>Тело if, while или for: ещё один уровень вложенности.</summary>
    Flow Block(List<Node> body, Dictionary<string, object?> locals)
    {
        Enter();
        var f = Exec(body, locals);
        _nest--;
        return f;
    }

    /// <summary>Глубже на уровень (скобка, not, минус, блок, аргументы). Предел и край стека — RunError, а не StackOverflow.</summary>
    void Enter()
    {
        if (++_nest > MaxNest || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new RunError($"nested too deep: over {MaxNest} brackets, blocks or calls inside each other");
    }

    void Step(int n = 1)
    {
        _steps += n;
        if (_steps > _maxSteps) throw new StepLimit();
        if (_clock.Elapsed > MaxTime) throw new StepLimit("runs too long: an endless loop?");
    }

    /// <summary>Работа, которая растёт с длиной (склейка, range, contains, копия списка): шаг за каждую тысячу.</summary>
    void Cost(long size)
    {
        if (size >= 1000) Step((int)Math.Min(size / 1000, _maxSteps + 1L));
    }

    /// <summary>Строка вывода: не больше MaxLines строк по MaxLine знаков — say в цикле не съест память.</summary>
    void Print(string line)
    {
        Cost(line.Length);
        if (_out.Count > MaxLines) return;
        _out.Add(_out.Count == MaxLines ? "… (output is cut)" : line.Length > MaxLine ? line[..MaxLine] + "…" : line);
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
        if (dot > 0 && !RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw new RunError($"name is too long: {name[..Math.Min(name.Length, 40)]}…");
        if (dot > 0 && Get(name[..dot], locals) is Dictionary<string, object?> obj && obj.TryGetValue(name[(dot + 1)..], out v)) return v;
        // Переменная игры, которую не задали: считаем нулём и предупреждаем один раз.
        if (_guessed.Add(name)) _errors.Add(new Diag(_line, $"'{name}' is not set: using 0 (add it to the game variables)", true));
        _globals[name] = 0d;
        return 0d;
    }

    // ---------------------------------------------------------------- выражения

    object? Eval(string src, Dictionary<string, object?> locals, bool skip = false)
    {
        var p = new Parser(src, this, locals, skip);
        var v = p.Expr();
        p.End();
        return v;
    }

    /// <summary>
    /// Разбор и вычисление выражения за один проход (приоритеты: or, and, not, сравнения, +, *, унарные).
    /// skip — только разобрать, ничего не вычисляя: правая часть and/or, когда левая уже всё решила (как во всех движках).
    /// </summary>
    sealed class Parser(string src, Sandbox box, Dictionary<string, object?> locals, bool skip = false)
    {
        int _i;
        int _skip = skip ? 1 : 0;
        bool Off => _skip > 0;

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
            while (Word("or"))
            {
                if (Truthy(a)) { _skip++; And(); _skip--; a = true; }
                else a = Truthy(And());
            }
            return a;
        }

        object? And()
        {
            var a = Not();
            while (Word("and"))
            {
                if (!Truthy(a)) { _skip++; Not(); _skip--; a = false; }
                else a = Truthy(Not());
            }
            return a;
        }

        object? Not()
        {
            if (!Word("not")) return Compare();
            box.Enter();
            var v = !Truthy(Not());
            box._nest--;
            return v;
        }

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
                if (Take("+")) a = box.Add(a, Product());
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
                    if (b == 0 && !Off) throw new RunError("division by zero");
                    a = Num(a) / b;
                }
                else if (Take("%"))
                {
                    var b = Num(Unary());
                    if (b == 0 && !Off) throw new RunError("division by zero");
                    a = Num(a) % b;
                }
                else return a;
            }
        }

        object? Unary()
        {
            var minus = Take("-");
            if (!minus && !Take("+")) return Postfix(Primary());
            box.Enter();
            var v = Num(Unary());
            box._nest--;
            return minus ? -v : v;
        }

        object? Postfix(object? v)
        {
            while (Peek("["))
            {
                _i++;
                box.Enter();
                var key = Expr();
                box._nest--;
                Need("]");
                if (Off) { v = null; continue; }
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
            if (c == '(')
            {
                _i++;
                box.Enter();
                var v = Expr();
                box._nest--;
                Need(")");
                return v;
            }
            if (c == '[')
            {
                _i++;
                var list = new List<object?>();
                box.Enter();
                if (!Take("]"))
                {
                    do list.Add(Expr()); while (Take(","));
                    Need("]");
                }
                box._nest--;
                return Off ? null : list;
            }
            if (c == '"') return StringLit();
            if (char.IsDigit(c) || (c == '.' && _i + 1 < src.Length && char.IsDigit(src[_i + 1])))
            {
                // Не больше одной точки: «1.2.3» — ошибка в выражении, а не FormatException из double.Parse.
                var start = _i;
                var dot = false;
                while (_i < src.Length && (char.IsDigit(src[_i]) || src[_i] == '.' && !dot && _i + 1 < src.Length && char.IsDigit(src[_i + 1])))
                    if (src[_i++] == '.') dot = true;
                return double.TryParse(src.AsSpan(start, _i - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var num)
                    ? num : throw new RunError($"bad number '{src[start.._i]}'");
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
                    box.Enter();
                    if (!Take(")"))
                    {
                        do args.Add(Expr()); while (Take(","));
                        Need(")");
                    }
                    box._nest--;
                    return Off ? null : box.Call(name, args);
                }
                return Off ? null : box.Get(name, locals);
            }
            throw new RunError($"unexpected '{c}' in: {src}");
        }

        /// <summary>"текст {выражение}" — подстановка, {{ и }} — сами скобки.</summary>
        string? StringLit()
        {
            _i++;
            var sb = new StringBuilder();
            while (_i < src.Length && src[_i] != '"')
            {
                if (sb.Length > MaxSize) throw new RunError($"text is too long: more than {MaxSize} characters");
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
                    box.Enter();
                    var part = box.Eval(src[(_i + 1)..close], locals, Off);
                    box._nest--;
                    if (!Off) sb.Append(Text(part));
                    _i = close + 1;
                    continue;
                }
                sb.Append(c);
                _i++;
            }
            if (_i >= src.Length) throw new RunError("string is not closed with \"");
            _i++;
            if (Off) return null;
            box.Cost(sb.Length);
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------- функции

    object? Call(string name, List<object?> a)
    {
        Step();
        if (_fns.TryGetValue(name, out var fn))
        {
            if (a.Count > fn.Params.Count) throw new RunError($"{name}() takes {fn.Params.Count} arguments, got {a.Count}");
            if (++_depth > 200 || !RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw new RunError($"{name}() calls itself too deep");
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
            case "str": { var t = Text(A(0)); Cost(t.Length); return t; }
            case "int": return Math.Truncate(Num(A(0) is string s1 && double.TryParse(s1, NumberStyles.Float, CultureInfo.InvariantCulture, out var p1) ? p1 : A(0)));
            case "num": return A(0) is string s2 ? double.TryParse(s2, NumberStyles.Float, CultureInfo.InvariantCulture, out var p2) ? p2 : throw new RunError($"num(\"{s2}\"): not a number") : Num(A(0));
            case "rand":
            {
                // Как randint и math.random: a ≤ b, иначе ошибка мода, а не ArgumentOutOfRangeException.
                double lo = Num(A(0)), hi = Num(A(1));
                if (!(lo <= hi)) throw new RunError($"rand({Text(lo)}, {Text(hi)}): the first number must not be bigger than the second");
                return (double)_random.NextInt64((long)Math.Clamp(lo, -1e15, 1e15), (long)Math.Clamp(hi, -1e15, 1e15) + 1);
            }
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
                // Считаем проходы, а не i++: у 1e16 шаг +1 теряется, и цикл не кончился бы.
                var count = Math.Ceiling(to - from);
                var list = new List<object?>();
                for (var k = 0; k < count; k++) list.Add(from + k);
                Cost(list.Count);
                return list;
            }
            case "contains":
                Cost(A(0) switch { string s => s.Length, List<object?> l => l.Count, _ => 0 });
                return A(0) switch { string s => s.Contains(Text(A(1)), StringComparison.Ordinal), List<object?> l => l.Any(x => Same(x, A(1))), Dictionary<string, object?> d => d.ContainsKey(Text(A(1))), _ => false };
            case "upper": { var t = Text(A(0)); Cost(t.Length); return t.ToUpperInvariant(); }
            case "lower": { var t = Text(A(0)); Cost(t.Length); return t.ToLowerInvariant(); }
            // 13.0
            case "floor": return Math.Floor(Num(A(0)));
            case "ceil": return Math.Ceiling(Num(A(0)));
            case "sqrt": { var x = Num(A(0)); return x < 0 ? throw new RunError($"sqrt({Text(x)}): negative number") : Math.Sqrt(x); }
            case "pow": return Math.Pow(Num(A(0)), Num(A(1)));
            case "sign": return (double)Math.Sign(Num(A(0)));
            case "lerp": { double x = Num(A(0)), y = Num(A(1)); return x + (y - x) * Num(A(2)); }
            case "pick": return A(0) is List<object?> { Count: > 0 } pl ? pl[_random.Next(pl.Count)] : throw new RunError("pick() needs a non-empty list");
            case "sum":
            {
                if (A(0) is not List<object?> sl) throw new RunError($"sum() of {TypeName(A(0))}");
                Cost(sl.Count);
                return sl.Sum(Num);
            }
            case "join":
            {
                if (A(0) is not List<object?> jl) throw new RunError($"join() of {TypeName(A(0))}");
                var t = string.Join(Text(A(1)), jl.Select(Text));
                Cost(t.Length);
                return t.Length > MaxSize ? throw new RunError("join(): the text is too long") : t;
            }
            case "split":
            {
                var t = Text(A(0));
                var sep = Text(A(1));
                Cost(t.Length);
                return (sep == "" ? t.Select(c => (object?)c.ToString()) : t.Split(sep).Select(x => (object?)x)).ToList();
            }
            case "trim": { var t = Text(A(0)); Cost(t.Length); return t.Trim(); }
            case "replace":
            {
                var t = Text(A(0));
                var from = Text(A(1));
                Cost(t.Length);
                var r = from == "" ? t : t.Replace(from, Text(A(2)), StringComparison.Ordinal);
                return r.Length > MaxSize ? throw new RunError("replace(): the text is too long") : r;
            }
            case "startswith": return Text(A(0)).StartsWith(Text(A(1)), StringComparison.Ordinal);
            case "endswith": return Text(A(0)).EndsWith(Text(A(1)), StringComparison.Ordinal);
        }
        // Функция игры (renpy.notify, player.heal…): в песочнице её нет — отмечаем и идём дальше.
        Print($"[{_where}] → {name}({string.Join(", ", a.Select(Show))})  (game function)");
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

    /// <summary>+ : числа складываются, строки и списки склеиваются — не длиннее MaxSize и с ценой по длине.</summary>
    object? Add(object? a, object? b)
    {
        object r = a switch
        {
            string or null when b is string => Text(a) + Text(b),
            string => Text(a) + Text(b),
            List<object?> l when b is List<object?> m => l.Concat(m).ToList(),
            _ when b is string => Text(a) + Text(b),
            _ => Num(a) + Num(b),
        };
        var size = r switch { string s => s.Length, List<object?> l => l.Count, _ => 0 };
        if (size > MaxSize) throw new RunError(r is string ? $"text is too long: more than {MaxSize} characters" : $"list is too long: more than {MaxSize} items");
        Cost(size);
        return r;
    }

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
        List<object?> or Dictionary<string, object?> => Nested(v),
        _ => v.ToString() ?? "",
    };

    /// <summary>
    /// Список или словарь текстом. items[0] = items кладёт список в сам себя, а l = [l] в цикле строит
    /// огромную глубину: повтор и глубже MaxNest — […], длиннее MaxShow знаков — «…», а не переполнение стека.
    /// </summary>
    static string Nested(object v)
    {
        var sb = new StringBuilder();
        var open = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Put(object? x)
        {
            if (sb.Length > MaxShow) return;
            if (x is not (List<object?> or Dictionary<string, object?>)) { sb.Append(Show(x)); return; }
            if (open.Count >= MaxNest || !open.Add(x)) { sb.Append(x is List<object?> ? "[…]" : "{…}"); return; }
            if (x is List<object?> l)
            {
                sb.Append('[');
                for (var i = 0; i < l.Count && sb.Length <= MaxShow; i++) { if (i > 0) sb.Append(", "); Put(l[i]); }
                sb.Append(']');
            }
            else
            {
                sb.Append('{');
                var first = true;
                foreach (var (key, value) in (Dictionary<string, object?>)x)
                {
                    if (sb.Length > MaxShow) break;
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append('"').Append(key).Append("\": ");
                    Put(value);
                }
                sb.Append('}');
            }
            open.Remove(x);
        }
        Put(v);
        return sb.Length > MaxShow ? sb.ToString(0, MaxShow) + "…" : sb.ToString();
    }

    /// <summary>Значение для вывода: строки — в кавычках.</summary>
    public static string Show(object? v) => v is string s ? "\"" + s + "\"" : Text(v);
}
