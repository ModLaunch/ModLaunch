using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModLaunch.Creator;

/// <summary>Функция стандартной библиотеки ModScript: имена (через «|»), сигнатура и описание — для подсказок и справки.</summary>
public sealed record LibFn(string Group, string[] Names, string Sig, string Doc, Func<List<object?>, object?> Run);

/// <summary>
/// Выражения и стандартная библиотека ModScript: функции в стиле Python
/// (math, random, str, list, dict, re, json, datetime, os.path, hashlib,
/// base64, itertools, statistics, textwrap…) и C++ (std::min, std::clamp,
/// std::accumulate…), плюс помощники для игр (lerp, remap, vec, color).
/// Значения — числа, строки, логические, списки и словари; в переменных
/// хранятся строкой (списки и словари — как JSON).
/// </summary>
public static partial class ModScript
{
    // ---------------------------------------------------------------- значения

    static readonly JsonSerializerOptions JsonOut = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ToText(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        double d => Num(d),
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => ToJsonNode(v)?.ToJsonString(JsonOut) ?? "",
    };

    static string Num(double d) =>
        double.IsFinite(d) && Math.Abs(d) < 1e15 && d == Math.Floor(d) ? ((long)d).ToString(CultureInfo.InvariantCulture)
        : double.IsPositiveInfinity(d) ? "inf" : double.IsNegativeInfinity(d) ? "-inf" : double.IsNaN(d) ? "nan"
        : Math.Round(d, 10).ToString("R", CultureInfo.InvariantCulture);

    static JsonNode? ToJsonNode(object? v) => v switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        double d => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? JsonValue.Create((long)d) : JsonValue.Create(d),
        List<object?> l => new JsonArray(l.Select(ToJsonNode).ToArray()),
        Dictionary<string, object?> m => new JsonObject(m.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, ToJsonNode(kv.Value)))),
        _ => JsonValue.Create(v.ToString()),
    };

    static object? FromJsonNode(JsonNode? n) => n switch
    {
        null => null,
        JsonArray a => a.Select(FromJsonNode).ToList(),
        JsonObject o => o.ToDictionary(kv => kv.Key, kv => FromJsonNode(kv.Value)),
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<long>(out var l) => (double)l,
        JsonValue v => v.ToString(),
    };

    /// <summary>Значение переменной: число, логическое, список/словарь (JSON) или строка.</summary>
    public static object? FromText(string s)
    {
        if (s.StartsWith(JsonMark)) s = s[JsonMark.Length..];
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !s.Contains(',') && s.Trim() == s && s != "") return d;
        if (s is "true" or "false") return s == "true";
        if (s.Length > 1 && (s[0] == '[' && s[^1] == ']' || s[0] == '{' && s[^1] == '}'))
        {
            try { return FromJsonNode(JsonNode.Parse(s)); } catch { }
        }
        return s;
    }

    static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s is not ("" or "0" or "false" or "no"),
        List<object?> l => l.Count > 0,
        Dictionary<string, object?> m => m.Count > 0,
        _ => true,
    };

    static double D(object? v) => v switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        null => 0,
        string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        string s => throw new LibError($"«{Clip(s)}» — не число"),
        List<object?> => throw new LibError("список, а нужно число"),
        _ => throw new LibError("нужно число"),
    };

    static string S(object? v) => ToText(v);

    static List<object?> L(object? v) => v switch
    {
        List<object?> l => l,
        string s => s.Select(c => (object?)c.ToString()).ToList(),
        Dictionary<string, object?> m => m.Keys.Select(k => (object?)k).ToList(),
        null => [],
        _ => [v],
    };

    static Dictionary<string, object?> M(object? v) => v switch
    {
        Dictionary<string, object?> m => m,
        List<object?> l => l.Select((x, i) => x is List<object?> { Count: 2 } p ? (S(p[0]), p[1]) : (i.ToString(CultureInfo.InvariantCulture), x)).ToDictionary(p => p.Item1, p => p.Item2),
        null => [],
        _ => throw new LibError("нужен словарь"),
    };

    static string Clip(string s) => s.Length > 30 ? s[..30] + "…" : s;

    static int Cmp(object? a, object? b) =>
        a is double x && b is double y ? x.CompareTo(y)
        : a is bool p && b is bool q ? p.CompareTo(q)
        : string.CompareOrdinal(S(a), S(b));

    static bool Eq(object? a, object? b) =>
        a is double x && b is double y ? x == y
        : (a is double || b is double) && double.TryParse(S(a), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && double.TryParse(S(b), NumberStyles.Float, CultureInfo.InvariantCulture, out var q) ? p == q
        : S(a) == S(b);

    static int Int(object? v) => (int)Math.Clamp(Math.Floor(D(v)), int.MinValue, int.MaxValue);

    /// <summary>Индекс как в Python: −1 — последний.</summary>
    static int Idx(int i, int count) => i < 0 ? i + count : i;

    sealed class LibError(string message) : Exception(message);

    // ---------------------------------------------------------------- выражения

    enum K { Num, Str, Name, Op, End }
    readonly record struct XT(K Kind, string Text, double Value = 0);

    static List<XT> XLex(string s)
    {
        var list = new List<XT>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsDigit(c) || c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
            {
                var st = i;
                if (c == '0' && i + 1 < s.Length && s[i + 1] is 'x' or 'X' or 'b' or 'B')
                {
                    var hex = s[i + 1] is 'x' or 'X';
                    i += 2;
                    var ds = i;
                    while (i < s.Length && (hex ? Uri.IsHexDigit(s[i]) : s[i] is '0' or '1' or '_')) i++;
                    var digits = s[ds..i].Replace("_", "");
                    list.Add(new XT(K.Num, s[st..i], hex ? Convert.ToInt64(digits, 16) : Convert.ToInt64(digits, 2)));
                    continue;
                }
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or '_' || (s[i] is 'e' or 'E' && i + 1 < s.Length && (char.IsDigit(s[i + 1]) || s[i + 1] is '-' or '+')) || (s[i] is '-' or '+' && s[i - 1] is 'e' or 'E'))) i++;
                list.Add(new XT(K.Num, s[st..i], double.Parse(s[st..i].Replace("_", ""), CultureInfo.InvariantCulture)));
                continue;
            }
            if (c is '"' or '\'')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != c)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        var n = s[i + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', _ => n });
                        i += 2;
                        continue;
                    }
                    sb.Append(s[i++]);
                }
                if (i >= s.Length) throw new LibError("не закрыта кавычка");
                i++;
                list.Add(new XT(K.Str, sb.ToString()));
                continue;
            }
            if (char.IsLetter(c) || c is '_' or '$')
            {
                var st = i;
                i++;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '_')
                       || s[i] == ':' && i + 2 < s.Length && s[i + 1] == ':' && char.IsLetter(s[i + 2]))) i += s[i] == ':' ? 2 : 1;
                var name = s[st..i];
                if (name.StartsWith('$')) name = name[1..].Trim('{', '}');
                list.Add(new XT(K.Name, name));
                continue;
            }
            if (c == '$' && i + 1 < s.Length && s[i + 1] == '{')
            {
                var end = s.IndexOf('}', i);
                if (end < 0) throw new LibError("не закрыта ${");
                list.Add(new XT(K.Name, s[(i + 2)..end]));
                i = end + 1;
                continue;
            }
            foreach (var op in new[] { "**", "//", "==", "!=", "<=", ">=", "&&", "||", "<<", ">>", "+", "-", "*", "/", "%", "<", ">", "!", "(", ")", "[", "]", ",", ":", "?", "{", "}", "&", "|", "^", "~", "." })
            {
                if (string.CompareOrdinal(s, i, op, 0, op.Length) != 0) continue;
                list.Add(new XT(K.Op, op));
                i += op.Length;
                goto next;
            }
            throw new LibError($"непонятный символ «{c}»");
            next:;
        }
        list.Add(new XT(K.End, ""));
        return list;
    }

    /// <summary>Разбор и вычисление выражения (рекурсивный спуск, приоритеты как в Python).</summary>
    sealed class Evaluator(List<XT> toks, Env env, ModBuild b, int line)
    {
        int _p;
        XT Peek => toks[_p];
        bool Is(string op) => Peek.Kind is K.Op or K.Name && Peek.Text == op;
        bool Take(string op) { if (!Is(op)) return false; _p++; return true; }
        void Expect(string op) { if (!Take(op)) throw new LibError($"ожидалось «{op}»"); }

        public object? Run()
        {
            var v = Ternary();
            if (Peek.Kind != K.End) throw new LibError($"лишнее «{Peek.Text}»");
            return v;
        }

        object? Ternary()
        {
            var v = Or();
            if (Take("?")) { var a = Ternary(); Expect(":"); var c = Ternary(); return Truthy(v) ? a : c; }
            // Python: a if cond else b
            if (Take("if")) { var cond = Or(); Expect("else"); var other = Ternary(); return Truthy(cond) ? v : other; }
            return v;
        }

        object? Or()
        {
            var v = And();
            while (Is("or") || Is("||")) { _p++; var r = And(); v = Truthy(v) ? v : r; }
            return v;
        }

        object? And()
        {
            var v = Not();
            while (Is("and") || Is("&&")) { _p++; var r = Not(); v = Truthy(v) ? r : v; }
            return v;
        }

        object? Not() => Take("not") || Take("!") ? !Truthy(Not()) : Compare();

        object? Compare()
        {
            var v = BitOr();
            while (true)
            {
                if (Take("==")) v = Eq(v, BitOr());
                else if (Take("!=")) v = !Eq(v, BitOr());
                else if (Take("<=")) v = Cmp(v, BitOr()) <= 0;
                else if (Take(">=")) v = Cmp(v, BitOr()) >= 0;
                else if (Take("<")) v = Cmp(v, BitOr()) < 0;
                else if (Take(">")) v = Cmp(v, BitOr()) > 0;
                else if (Is("not") && _p + 1 < toks.Count && toks[_p + 1].Text == "in") { _p += 2; v = !Contains(BitOr(), v); }
                else if (Take("in")) v = Contains(BitOr(), v);
                else if (Take("contains")) { var r = BitOr(); v = Contains(v, r); }
                else return v;
            }
        }

        static bool Contains(object? container, object? item) => container switch
        {
            List<object?> l => l.Any(x => Eq(x, item)),
            Dictionary<string, object?> m => m.ContainsKey(S(item)),
            _ => S(container).Contains(S(item), StringComparison.Ordinal),
        };

        object? BitOr()
        {
            var v = Shift();
            while (true)
            {
                if (Take("|")) v = (double)((long)D(v) | (long)D(Shift()));
                else if (Take("&")) v = (double)((long)D(v) & (long)D(Shift()));
                else if (Take("^")) v = (double)((long)D(v) ^ (long)D(Shift()));
                else return v;
            }
        }

        object? Shift()
        {
            var v = Sum();
            while (true)
            {
                if (Take("<<")) v = (double)((long)D(v) << Int(Sum()));
                else if (Take(">>")) v = (double)((long)D(v) >> Int(Sum()));
                else return v;
            }
        }

        object? Sum()
        {
            var v = Product();
            while (true)
            {
                if (Take("+")) v = Add(v, Product());
                else if (Take("-")) { var r = Product(); v = v is List<object?> l ? l.Where(x => !L(r).Any(y => Eq(x, y))).ToList() : D(v) - D(r); }
                else return v;
            }
        }

        static object? Add(object? a, object? c) => (a, c) switch
        {
            (List<object?> x, List<object?> y) => x.Concat(y).ToList(),
            (List<object?> x, _) => x.Append(c).ToList(),
            (Dictionary<string, object?> x, Dictionary<string, object?> y) => x.Concat(y).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value),
            (double x, double y) => x + y,
            (double x, bool y) => x + (y ? 1 : 0),
            _ when a is string || c is string => S(a) + S(c),
            _ => D(a) + D(c),
        };

        object? Product()
        {
            var v = Unary();
            while (true)
            {
                if (Take("*"))
                {
                    var r = Unary();
                    v = (v, r) switch
                    {
                        (string s, double n) => string.Concat(Enumerable.Repeat(s, Math.Max(0, (int)n))),
                        (List<object?> l, double n) => Enumerable.Repeat(l, Math.Max(0, (int)n)).SelectMany(x => x).ToList(),
                        _ => D(v) * D(r),
                    };
                }
                else if (Take("//")) { var r = D(Unary()); if (r == 0) throw new LibError("деление на ноль"); v = Math.Floor(D(v) / r); }
                else if (Take("/")) { var r = D(Unary()); if (r == 0) throw new LibError("деление на ноль"); v = D(v) / r; }
                else if (Take("%"))
                {
                    var r = Unary();
                    if (v is string fmt) v = PyFormat(fmt, r is List<object?> args ? args : [r]);
                    else { var m = D(r); if (m == 0) throw new LibError("деление на ноль"); v = D(v) - m * Math.Floor(D(v) / m); }
                }
                else return v;
            }
        }

        object? Unary()
        {
            if (Take("-")) return -D(Unary());
            if (Take("+")) return D(Unary());
            if (Take("~")) return (double)~(long)D(Unary());
            return Power();
        }

        object? Power()
        {
            var v = Postfix(Primary());
            if (Take("**")) return Math.Pow(D(v), D(Unary()));
            return v;
        }

        object? Postfix(object? v)
        {
            while (true)
            {
                if (Take("["))
                {
                    object? a = null, c = null;
                    var slice = false;
                    if (!Is(":")) a = Ternary();
                    if (Take(":")) { slice = true; if (!Is("]")) c = Ternary(); }
                    Expect("]");
                    v = slice ? Slice(v, a, c) : Index(v, a);
                }
                else if (Is(".") && toks[_p + 1].Kind == K.Name)
                {
                    // Метод через точку: "abc".upper(), xs.join(", ") → str.upper("abc").
                    _p++;
                    var name = toks[_p++].Text;
                    var args = new List<object?> { v };
                    if (Take("(")) args.AddRange(Args(")"));
                    v = CallMethod(name, args);
                }
                else return v;
            }
        }

        static object? Index(object? v, object? i) => v switch
        {
            Dictionary<string, object?> m => m.TryGetValue(S(i), out var x) ? x : null,
            List<object?> l => Idx(Int(i), l.Count) is var k && k >= 0 && k < l.Count ? l[k] : throw new LibError($"нет элемента {S(i)}"),
            _ => S(v) is var s && Idx(Int(i), s.Length) is var k2 && k2 >= 0 && k2 < s.Length ? s[k2].ToString() : throw new LibError($"нет символа {S(i)}"),
        };

        static object? Slice(object? v, object? a, object? c)
        {
            var count = v is List<object?> l0 ? l0.Count : S(v).Length;
            var from = Math.Clamp(a is null ? 0 : Idx(Int(a), count), 0, count);
            var to = Math.Clamp(c is null ? count : Idx(Int(c), count), 0, count);
            if (to < from) to = from;
            return v is List<object?> l ? l.GetRange(from, to - from) : S(v)[from..to];
        }

        List<object?> Args(string close)
        {
            var args = new List<object?>();
            if (Take(close)) return args;
            do
            {
                if (Take("*")) args.AddRange(L(Ternary())); // распаковка списка: f(*xs)
                else args.Add(Ternary());
            } while (Take(","));
            Expect(close);
            return args;
        }

        object? Primary()
        {
            var t = Peek;
            switch (t.Kind)
            {
                case K.Num: _p++; return t.Value;
                case K.Str: _p++; return t.Text;
                case K.Op when t.Text == "(":
                {
                    _p++;
                    var v = Ternary();
                    if (Take(",")) { var tuple = new List<object?> { v }; do { if (Is(")")) break; tuple.Add(Ternary()); } while (Take(",")); Expect(")"); return tuple; }
                    Expect(")");
                    return v;
                }
                case K.Op when t.Text == "[":
                {
                    _p++;
                    // Генератор списка: [x * 2 for x in xs if x > 1]
                    var start = _p;
                    if (!Is("]"))
                    {
                        var depth = 0;
                        for (var q = _p; q < toks.Count; q++)
                        {
                            var x = toks[q];
                            if (x.Kind == K.Op && x.Text is "(" or "[" or "{") depth++;
                            else if (x.Kind == K.Op && x.Text is ")" or "]" or "}") { if (depth == 0) break; depth--; }
                            else if (depth == 0 && x.Kind == K.Name && x.Text == "for") return Comprehension(start, q);
                        }
                    }
                    return Args("]");
                }
                case K.Op when t.Text == "{":
                {
                    _p++;
                    var m = new Dictionary<string, object?>();
                    if (Take("}")) return m;
                    do { if (Is("}")) break; var k = Ternary(); Expect(":"); m[S(k)] = Ternary(); } while (Take(","));
                    Expect("}");
                    return m;
                }
                case K.Name:
                {
                    _p++;
                    if (Is("(")) { _p++; return Call(t.Text, Args(")")); }
                    return Resolve(t.Text);
                }
            }
            throw new LibError(t.Kind == K.End ? "выражение оборвалось" : $"неожиданное «{t.Text}»");
        }

        object? Comprehension(int start, int forAt)
        {
            // [выражение for имя in список (if условие)]
            _p = forAt + 1;
            var name = toks[_p++].Text;
            Expect("in");
            var source = Or();
            var condAt = -1;
            if (Is("if"))
            {
                // Условие только пропускаем: вычислится для каждого элемента.
                condAt = ++_p;
                for (var depth = 0; Peek.Kind != K.End; _p++)
                {
                    if (Peek.Kind == K.Op && Peek.Text is "(" or "[" or "{") depth++;
                    else if (Peek.Kind == K.Op && Peek.Text is ")" or "]" or "}") { if (depth == 0) break; depth--; }
                }
            }
            Expect("]");
            var end = _p;
            var result = new List<object?>();
            var had = env.Vars.TryGetValue(name, out var old);
            foreach (var item in L(source))
            {
                env.Vars[name] = ToText(item);
                if (condAt >= 0) { _p = condAt; if (!Truthy(Or())) continue; }
                _p = start;
                result.Add(Ternary());
            }
            if (had) env.Vars[name] = old!; else env.Vars.Remove(name);
            _p = end;
            return result;
        }

        object? Resolve(string name)
        {
            if (env.Vars.TryGetValue(name, out var v)) return FromText(v);
            if (Constants.TryGetValue(name, out var c)) return c;
            if (Constants.TryGetValue(name.ToLowerInvariant(), out c)) return c;
            if (env.Funcs.ContainsKey(name) || Library.ContainsKey(name)) return "fn:" + name;
            // player.hp — поле словаря через точку.
            if (name.LastIndexOf('.') is var dot && dot > 0 && (env.Vars.ContainsKey(name[..dot]) || name[..dot].Contains('.')))
                return Index(Resolve(name[..dot]), name[(dot + 1)..]);
            throw new ScriptError("err.unknownVar|" + name);
        }

        object? CallMethod(string name, List<object?> args)
        {
            var self = args[0];
            foreach (var ns in self switch { List<object?> => new[] { "list", "str" }, Dictionary<string, object?> => ["dict"], _ => ["str", "list"] })
                if (Library.ContainsKey($"{ns}.{name}")) return Call($"{ns}.{name}", args);
            return Call(name, args);
        }

        public object? Call(string name, List<object?> args)
        {
            if (name.StartsWith("fn:")) name = name[3..];
            if (name is "print" or "log" or "std::cout" or "console.log")
            {
                b.Log.Add($"{line}: {string.Join(" ", args.Select(ToText))}");
                return null;
            }
            if (name is "map" or "filter" or "sorted_by" or "reduce" or "functools.reduce" or "any_of" or "all_of" or "std::transform" or "std::count_if" or "std::any_of" or "std::all_of" or "std::find_if")
                return HigherOrder(name, args);
            if (env.Funcs.TryGetValue(name, out var f)) return CallUser(name, f, args);
            if (env.Vars.TryGetValue(name, out var alias) && alias.StartsWith("fn:")) return Call(alias, args);
            // xs.join(", ") / name.upper() — метод у переменной.
            if (!Library.ContainsKey(name) && name.LastIndexOf('.') is var dot && dot > 0 && env.Vars.ContainsKey(name[..dot]))
                return CallMethod(name[(dot + 1)..], args.Prepend(Resolve(name[..dot])).ToList());
            if (!Library.TryGetValue(name, out var fn))
            {
                var guess = Library.Keys.Concat(env.Funcs.Keys).OrderBy(k => Distance(k, name)).FirstOrDefault();
                throw new ScriptError("err.unknownFunc|" + name + (guess is not null && Distance(guess, name) <= 3 ? $" → {guess}?" : ""));
            }
            try { return fn.Run(args); }
            catch (ScriptError) { throw; }
            catch (LibError e) { throw new ScriptError($"err.call|{name}: {e.Message}"); }
            catch (ArgumentOutOfRangeException) { throw new ScriptError($"err.call|{name}: {I18nArgs(fn)}"); }
            catch (Exception e) { throw new ScriptError($"err.call|{name}: {e.Message}"); }
        }

        static string I18nArgs(LibFn fn) => "аргументы: " + fn.Sig;

        object? CallFn(object? fn, params object?[] args) => Call(S(fn), args.ToList());

        object? HigherOrder(string name, List<object?> a)
        {
            // Функция передаётся именем: map(xs, "str.upper"), filter(xs, "is_even"), reduce(xs, "+").
            if (a.Count < 2) throw new ScriptError($"err.call|{name}: нужен список и функция");
            var (items, fn) = a[0] is string s0 && a[1] is List<object?> ? (L(a[1]), (object?)s0) : (L(a[0]), a[1]);
            var f = S(fn);
            object? Apply(object? x) => f switch
            {
                "+" or "-" or "*" or "/" => x,
                _ => CallFn(f, x),
            };
            switch (name)
            {
                case "map" or "std::transform": return items.Select(Apply).ToList();
                case "filter": return items.Where(x => Truthy(Apply(x))).ToList();
                case "std::count_if": return (double)items.Count(x => Truthy(Apply(x)));
                case "any_of" or "std::any_of": return items.Any(x => Truthy(Apply(x)));
                case "all_of" or "std::all_of": return items.All(x => Truthy(Apply(x)));
                case "std::find_if": return items.FirstOrDefault(x => Truthy(Apply(x)));
                case "sorted_by": return items.OrderBy(Apply, Comparer<object?>.Create(Cmp)).ToList();
                default:
                {
                    if (items.Count == 0) return a.Count > 2 ? a[2] : null;
                    var acc = a.Count > 2 ? a[2] : items[0];
                    foreach (var x in a.Count > 2 ? items : items.Skip(1))
                        acc = f switch
                        {
                            "+" => Add(acc, x),
                            "-" => D(acc) - D(x),
                            "*" => D(acc) * D(x),
                            "/" => D(acc) / D(x),
                            "max" => Cmp(acc, x) >= 0 ? acc : x,
                            "min" => Cmp(acc, x) <= 0 ? acc : x,
                            _ => CallFn(f, acc, x),
                        };
                    return acc;
                }
            }
        }

        object? CallUser(string name, UserFn f, List<object?> args)
        {
            if (++env.Depth[0] > 64) throw new ScriptError("err.recursion|" + name);
            var inner = new Env { Funcs = env.Funcs, Depth = env.Depth, Build = b };
            foreach (var (k, v) in env.Vars) inner.Vars[k] = v;
            inner.When.AddRange(env.When);
            for (var i = 0; i < f.Params.Count; i++)
            {
                var (pn, def) = f.Params[i];
                inner.Vars[pn] = i < args.Count ? ToText(args[i]) : def ?? throw new ScriptError($"err.call|{name}: не хватает аргумента {pn}");
            }
            inner.Vars["args"] = ToText(args);
            try { RunBody(f.Body, inner, b); return null; }
            catch (ReturnSignal r) { return r.Value; }
            finally { env.Depth[0]--; }
        }
    }

    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>Вычислить выражение в окружении скрипта.</summary>
    static object? Eval(string expr, Env env, ModBuild b, int line)
    {
        try { return new Evaluator(XLex(expr), env, b, line).Run(); }
        catch (LibError e) { throw new ScriptError("err.expr|" + e.Message); }
    }

    // ---------------------------------------------------------------- форматирование

    /// <summary>"%s и %d" % [a, b] и f-строки: "{name}: {x:.2f}".</summary>
    static string PyFormat(string fmt, List<object?> args)
    {
        var i = 0;
        return Regex.Replace(fmt, @"%(\.\d+)?([sdfixX%])", m =>
        {
            if (m.Groups[2].Value == "%") return "%";
            var v = i < args.Count ? args[i++] : null;
            return m.Groups[2].Value switch
            {
                "d" or "i" => ((long)Math.Truncate(D(v))).ToString(CultureInfo.InvariantCulture),
                "f" => D(v).ToString("F" + (m.Groups[1].Success ? m.Groups[1].Value[1..] : "6"), CultureInfo.InvariantCulture),
                "x" => ((long)D(v)).ToString("x"),
                "X" => ((long)D(v)).ToString("X"),
                _ => ToText(v),
            };
        });
    }

    static string FormatSpec(object? v, string spec)
    {
        if (spec == "") return ToText(v);
        var m = Regex.Match(spec, @"^(?:(.)?([<>^]))?([+])?(0)?(\d+)?(,)?(?:\.(\d+))?([dfeEgxXobs%])?$");
        if (!m.Success) return ToText(v);
        var type = m.Groups[8].Value;
        var prec = m.Groups[7].Success ? int.Parse(m.Groups[7].Value) : -1;
        string text = type switch
        {
            "f" => D(v).ToString("F" + (prec < 0 ? 6 : prec), CultureInfo.InvariantCulture),
            "e" or "E" => D(v).ToString(type + (prec < 0 ? 6 : prec), CultureInfo.InvariantCulture),
            "%" => (D(v) * 100).ToString("F" + (prec < 0 ? 0 : prec), CultureInfo.InvariantCulture) + "%",
            "x" => ((long)D(v)).ToString("x"),
            "X" => ((long)D(v)).ToString("X"),
            "o" => Convert.ToString((long)D(v), 8),
            "b" => Convert.ToString((long)D(v), 2),
            "d" => ((long)D(v)).ToString(CultureInfo.InvariantCulture),
            _ when prec >= 0 && v is double d => d.ToString("F" + prec, CultureInfo.InvariantCulture),
            _ => ToText(v),
        };
        if (m.Groups[6].Success && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            text = n.ToString(prec >= 0 ? "N" + prec : "N0", CultureInfo.InvariantCulture);
        if (m.Groups[3].Success && D(v) >= 0) text = "+" + text;
        var width = m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : 0;
        var fill = m.Groups[4].Success ? '0' : m.Groups[1].Success ? m.Groups[1].Value[0] : ' ';
        var align = m.Groups[2].Success ? m.Groups[2].Value : v is double ? ">" : "<";
        if (text.Length >= width) return text;
        var pad = width - text.Length;
        return align switch
        {
            "<" => text + new string(fill, pad),
            "^" => new string(fill, pad / 2) + text + new string(fill, pad - pad / 2),
            _ => new string(fill, pad) + text,
        };
    }

    // ---------------------------------------------------------------- библиотека

    public static readonly Dictionary<string, LibFn> Library = new(StringComparer.OrdinalIgnoreCase);
    public static readonly List<LibFn> LibraryList = [];
    static readonly Dictionary<string, object?> Constants = new(StringComparer.Ordinal)
    {
        ["pi"] = Math.PI, ["math.pi"] = Math.PI, ["M_PI"] = Math.PI, ["e"] = Math.E, ["math.e"] = Math.E, ["tau"] = Math.Tau, ["math.tau"] = Math.Tau,
        ["inf"] = double.PositiveInfinity, ["math.inf"] = double.PositiveInfinity, ["nan"] = double.NaN, ["math.nan"] = double.NaN,
        ["true"] = true, ["false"] = false, ["True"] = true, ["False"] = false, ["None"] = null, ["null"] = null, ["nullptr"] = null,
        ["INT_MAX"] = (double)int.MaxValue, ["INT_MIN"] = (double)int.MinValue, ["LLONG_MAX"] = (double)long.MaxValue, ["FLT_EPSILON"] = (double)float.Epsilon,
        ["DBL_EPSILON"] = double.Epsilon, ["sys.maxsize"] = (double)long.MaxValue, ["std::numeric_limits::max"] = double.MaxValue,
        ["string.ascii_letters"] = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ", ["string.ascii_lowercase"] = "abcdefghijklmnopqrstuvwxyz",
        ["string.ascii_uppercase"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ", ["string.digits"] = "0123456789", ["string.hexdigits"] = "0123456789abcdefABCDEF",
        ["string.punctuation"] = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~", ["string.whitespace"] = " \t\n\r", ["os.sep"] = "/", ["os.linesep"] = "\n",
        ["std::endl"] = "\n", ["newline"] = "\n", ["tab"] = "\t",
    };

    /// <summary>Все имена, которые знает язык: команды, функции, константы — для автодополнения.</summary>
    public static IEnumerable<string> AllNames => Keywords.Concat(Library.Keys).Concat(Constants.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

    static void F(string group, string names, string sig, string doc, Func<List<object?>, object?> run)
    {
        var fn = new LibFn(group, names.Split('|'), sig, doc, run);
        LibraryList.Add(fn);
        foreach (var n in fn.Names) Library[n] = fn;
    }

    static object? A(List<object?> a, int i, object? fallback = null) => i < a.Count ? a[i] : fallback;

    [ThreadStatic] static Random? _rng;
    static Random Rng => _rng ??= new Random();

    static ModScript()
    {
        // ---- встроенные (Python builtins)
        F("builtins", "len|size|std::size|count_of", "len(x)", "Длина строки, списка или словаря.", a => (double)(a[0] switch { List<object?> l => l.Count, Dictionary<string, object?> m => m.Count, var v => S(v).Length }));
        F("builtins", "str|std::to_string|to_string|String", "str(x)", "Превратить в строку.", a => ToText(A(a, 0)));
        F("builtins", "int|std::stoi|stoi|parseInt|floor_int", "int(x, base=10)", "Целое число (можно из строки, в том числе \"ff\", 16).", a => a.Count > 1 ? (double)Convert.ToInt64(S(a[0]).Trim(), Int(a[1])) : Math.Truncate(D(a[0])));
        F("builtins", "float|std::stof|std::stod|stof|stod|parseFloat|number", "float(x)", "Дробное число.", a => D(a[0]));
        F("builtins", "bool", "bool(x)", "Логическое значение: пусто, 0, false → false.", a => Truthy(A(a, 0)));
        F("builtins", "abs|std::abs|math.fabs|fabs", "abs(x)", "Модуль числа.", a => Math.Abs(D(a[0])));
        F("builtins", "round|std::round", "round(x, digits=0)", "Округлить (как в Python — к чётному на .5).", a => Math.Round(D(a[0]), a.Count > 1 ? Int(a[1]) : 0, MidpointRounding.ToEven));
        F("builtins", "min|std::min|std::min_element|min_element", "min(a, b, …) / min(list)", "Наименьшее значение.", a => (a.Count == 1 ? L(a[0]) : a).Aggregate((x, y) => Cmp(x, y) <= 0 ? x : y));
        F("builtins", "max|std::max|std::max_element|max_element", "max(a, b, …) / max(list)", "Наибольшее значение.", a => (a.Count == 1 ? L(a[0]) : a).Aggregate((x, y) => Cmp(x, y) >= 0 ? x : y));
        F("builtins", "sum|std::accumulate|accumulate_sum", "sum(list, start=0)", "Сумма списка.", a => L(a[0]).Sum(D) + D(A(a, 1, 0.0)));
        F("builtins", "range|std::iota|iota", "range(stop) / range(start, stop, step=1)", "Список чисел (до 10 000).", a =>
        {
            double start = a.Count > 1 ? D(a[0]) : 0, stop = a.Count > 1 ? D(a[1]) : D(a[0]), step = a.Count > 2 ? D(a[2]) : 1;
            if (step == 0) throw new LibError("шаг 0");
            var list = new List<object?>();
            for (var x = start; step > 0 ? x < stop : x > stop; x += step) { list.Add(x); if (list.Count > 10000) throw new LibError("слишком длинный диапазон"); }
            return list;
        });
        F("builtins", "sorted|std::sort|sort", "sorted(list, reverse=false)", "Отсортированная копия.", a => { var l = L(a[0]).OrderBy(x => x, Comparer<object?>.Create(Cmp)).ToList(); if (Truthy(A(a, 1, false))) l.Reverse(); return l; });
        F("builtins", "reversed|std::reverse|reverse", "reversed(x)", "Задом наперёд (список или строка).", a => a[0] is List<object?> l ? Enumerable.Reverse(l).ToList() : new string(S(a[0]).Reverse().ToArray()));
        F("builtins", "enumerate", "enumerate(list, start=0)", "Список пар [номер, элемент].", a => L(a[0]).Select((x, i) => (object?)new List<object?> { i + D(A(a, 1, 0.0)), x }).ToList());
        F("builtins", "zip", "zip(a, b, …)", "Склеить списки попарно.", a => { var ls = a.Select(L).ToList(); var n = ls.Min(l => l.Count); return Enumerable.Range(0, n).Select(i => (object?)ls.Select(l => l[i]).ToList()).ToList(); });
        F("builtins", "any", "any(list)", "Есть ли хоть один истинный.", a => L(a[0]).Any(Truthy));
        F("builtins", "all", "all(list)", "Все ли истинны.", a => L(a[0]).All(Truthy));
        F("builtins", "list|list.of|vector|std::vector", "list(x)", "Список из строки, словаря или значений.", a => a.Count == 1 ? new List<object?>(L(a[0])) : a.ToList());
        F("builtins", "dict|map.of|std::map|std::unordered_map", "dict(pairs) / dict(\"k\", v, …)", "Словарь.", a => a.Count == 1 ? new Dictionary<string, object?>(M(a[0])) : Enumerable.Range(0, a.Count / 2).ToDictionary(i => S(a[i * 2]), i => a[i * 2 + 1]));
        F("builtins", "set|std::set|unique|std::unique", "set(list)", "Без повторов (порядок сохраняется).", a => L(a[0]).DistinctBy(ToText).ToList());
        F("builtins", "tuple|pair|std::make_pair|std::make_tuple", "tuple(a, b, …)", "Кортеж (список).", a => a.ToList());
        F("builtins", "type|typeof|typeid", "type(x)", "Тип: number, string, bool, list, dict, none.", a => A(a, 0) switch { double => "number", string => "string", bool => "bool", List<object?> => "list", Dictionary<string, object?> => "dict", _ => "none" });
        F("builtins", "isinstance", "isinstance(x, \"number\")", "Проверка типа.", a => (A(a, 0) switch { double => "number|int|float", string => "string|str", bool => "bool", List<object?> => "list|tuple", Dictionary<string, object?> => "dict|map", _ => "none" }).Split('|').Contains(S(a[1])));
        F("builtins", "chr|char", "chr(code)", "Символ по коду.", a => char.ConvertFromUtf32(Int(a[0])));
        F("builtins", "ord", "ord(char)", "Код символа.", a => (double)char.ConvertToUtf32(S(a[0]), 0));
        F("builtins", "hex", "hex(n)", "0x… запись.", a => "0x" + ((long)D(a[0])).ToString("x"));
        F("builtins", "oct", "oct(n)", "0o… запись.", a => "0o" + Convert.ToString((long)D(a[0]), 8));
        F("builtins", "bin", "bin(n)", "0b… запись.", a => "0b" + Convert.ToString((long)D(a[0]), 2));
        F("builtins", "pow|math.pow|std::pow", "pow(x, y, mod?)", "Степень (третьим — по модулю).", a => a.Count > 2 ? (double)System.Numerics.BigInteger.ModPow((long)D(a[0]), (long)D(a[1]), (long)D(a[2])) : Math.Pow(D(a[0]), D(a[1])));
        F("builtins", "divmod", "divmod(a, b)", "[частное, остаток].", a => new List<object?> { Math.Floor(D(a[0]) / D(a[1])), D(a[0]) - D(a[1]) * Math.Floor(D(a[0]) / D(a[1])) });
        F("builtins", "format|f|fstr", "format(x, \".2f\") / format(\"{0} и {1}\", a, b)", "Форматирование как в Python.", a =>
        {
            if (a.Count == 2 && a[0] is not string) return FormatSpec(a[0], S(a[1]));
            var fmt = S(a[0]);
            var auto = 1;
            return Regex.Replace(fmt, @"\{(\w*)(?::([^}]*))?\}", m =>
            {
                var key = m.Groups[1].Value;
                object? v = key == "" ? A(a, auto++) : int.TryParse(key, out var n) ? A(a, n + 1) : a.Count > 1 && a[1] is Dictionary<string, object?> d && d.TryGetValue(key, out var x) ? x : m.Value;
                return FormatSpec(v, m.Groups[2].Value);
            });
        });
        F("builtins", "repr", "repr(x)", "Запись значения (строки — в кавычках).", a => a[0] is string s ? JsonValue.Create(s).ToJsonString(JsonOut) : ToText(a[0]));
        F("builtins", "hash|std::hash", "hash(x)", "Стабильный хеш-число.", a => (double)(BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(ToText(a[0]))), 0) & 0x7fffffff));
        F("builtins", "input|getenv|os.getenv", "getenv(name, default)", "Скрипт работает без ввода: всегда default.", a => A(a, 1, ""));
        F("builtins", "assert|static_assert", "assert(cond, \"сообщение\")", "Остановить сборку с ошибкой, если условие ложно.", a => Truthy(a[0]) ? null : throw new LibError(S(A(a, 1, "проверка не прошла"))));
        F("builtins", "error|raise|throw", "error(\"текст\")", "Остановить строку с ошибкой.", a => throw new LibError(S(A(a, 0, "ошибка"))));
        F("builtins", "default|coalesce|or_else", "default(x, fallback)", "x, если не пусто, иначе fallback.", a => Truthy(a[0]) ? a[0] : A(a, 1));
        F("builtins", "iif|ternary|choose", "iif(cond, a, b)", "a, если условие истинно, иначе b.", a => Truthy(a[0]) ? A(a, 1) : A(a, 2));
        F("builtins", "swap|std::swap", "swap(a, b)", "[b, a].", a => new List<object?> { a[1], a[0] });

        // ---- math
        F("math", "math.sqrt|sqrt|std::sqrt", "sqrt(x)", "Квадратный корень.", a => Math.Sqrt(D(a[0])));
        F("math", "math.cbrt|cbrt|std::cbrt", "cbrt(x)", "Кубический корень.", a => Math.Cbrt(D(a[0])));
        F("math", "math.isqrt|isqrt", "isqrt(n)", "Целый корень.", a => Math.Floor(Math.Sqrt(D(a[0]))));
        F("math", "math.exp|exp|std::exp", "exp(x)", "e в степени x.", a => Math.Exp(D(a[0])));
        F("math", "math.log|log|std::log", "log(x, base=e)", "Логарифм.", a => a.Count > 1 ? Math.Log(D(a[0]), D(a[1])) : Math.Log(D(a[0])));
        F("math", "math.log10|log10|std::log10", "log10(x)", "Десятичный логарифм.", a => Math.Log10(D(a[0])));
        F("math", "math.log2|log2|std::log2", "log2(x)", "Двоичный логарифм.", a => Math.Log2(D(a[0])));
        foreach (var (n, fn) in new (string, Func<double, double>)[] { ("sin", Math.Sin), ("cos", Math.Cos), ("tan", Math.Tan), ("asin", Math.Asin), ("acos", Math.Acos), ("atan", Math.Atan), ("sinh", Math.Sinh), ("cosh", Math.Cosh), ("tanh", Math.Tanh), ("asinh", Math.Asinh), ("acosh", Math.Acosh), ("atanh", Math.Atanh) })
        {
            var f = fn;
            F("math", $"math.{n}|{n}|std::{n}", $"{n}(x)", $"{n} (радианы).", a => f(D(a[0])));
        }
        F("math", "math.atan2|atan2|std::atan2", "atan2(y, x)", "Угол вектора (радианы).", a => Math.Atan2(D(a[0]), D(a[1])));
        F("math", "math.degrees|degrees|rad2deg", "degrees(rad)", "Радианы → градусы.", a => D(a[0]) * 180 / Math.PI);
        F("math", "math.radians|radians|deg2rad", "radians(deg)", "Градусы → радианы.", a => D(a[0]) * Math.PI / 180);
        F("math", "math.floor|floor|std::floor", "floor(x)", "Вниз до целого.", a => Math.Floor(D(a[0])));
        F("math", "math.ceil|ceil|std::ceil", "ceil(x)", "Вверх до целого.", a => Math.Ceiling(D(a[0])));
        F("math", "math.trunc|trunc|std::trunc", "trunc(x)", "Отбросить дробную часть.", a => Math.Truncate(D(a[0])));
        F("math", "math.fmod|fmod|std::fmod", "fmod(x, y)", "Остаток со знаком x (как в C).", a => Math.IEEERemainder(D(a[0]), D(a[1])) is var r && Math.Sign(r) != Math.Sign(D(a[0])) && r != 0 ? r + Math.Abs(D(a[1])) * Math.Sign(D(a[0])) : r);
        F("math", "math.factorial|factorial", "factorial(n)", "n! (до 170).", a => { var n = Int(a[0]); if (n is < 0 or > 170) throw new LibError("n от 0 до 170"); double r = 1; for (var i = 2; i <= n; i++) r *= i; return r; });
        F("math", "math.gcd|gcd|std::gcd", "gcd(a, b, …)", "НОД.", a => a.Select(x => (long)Math.Abs(D(x))).Aggregate((x, y) => { while (y != 0) (x, y) = (y, x % y); return x; }) is var g ? (double)g : 0);
        F("math", "math.lcm|lcm|std::lcm", "lcm(a, b, …)", "НОК.", a => a.Select(x => (long)Math.Abs(D(x))).Aggregate((x, y) => { long p = x, q = y; while (q != 0) (p, q) = (q, p % q); return p == 0 ? 0 : x / p * y; }) is var l ? (double)l : 0);
        F("math", "math.hypot|hypot|std::hypot", "hypot(x, y, …)", "Длина вектора.", a => Math.Sqrt(a.Sum(x => D(x) * D(x))));
        F("math", "math.dist|dist|distance", "dist(p, q)", "Расстояние между точками [x, y(, z)].", a => Math.Sqrt(L(a[0]).Zip(L(a[1])).Sum(p => Math.Pow(D(p.First) - D(p.Second), 2))));
        F("math", "math.isclose|isclose", "isclose(a, b, tol=1e-9)", "Почти равны.", a => Math.Abs(D(a[0]) - D(a[1])) <= Math.Max(D(A(a, 2, 1e-9)) * Math.Max(Math.Abs(D(a[0])), Math.Abs(D(a[1]))), 1e-12));
        F("math", "math.comb|comb|nCr", "comb(n, k)", "Сочетания.", a => { double n = Int(a[0]), k = Int(a[1]), r = 1; for (var i = 1; i <= k; i++) r = r * (n - k + i) / i; return Math.Round(r); });
        F("math", "math.perm|perm|nPr", "perm(n, k)", "Размещения.", a => { double r = 1; for (int n = Int(a[0]), k = Int(a[1]), i = 0; i < k; i++) r *= n - i; return r; });
        F("math", "math.prod|prod", "prod(list)", "Произведение.", a => L(a[0]).Aggregate(1.0, (x, y) => x * D(y)));
        F("math", "math.copysign|copysign|std::copysign", "copysign(x, y)", "x со знаком y.", a => Math.CopySign(D(a[0]), D(a[1])));
        F("math", "math.isnan|isnan|std::isnan", "isnan(x)", "Это NaN?", a => double.IsNaN(D(a[0])));
        F("math", "math.isinf|isinf|std::isinf", "isinf(x)", "Бесконечность?", a => double.IsInfinity(D(a[0])));
        F("math", "math.isfinite|isfinite|std::isfinite", "isfinite(x)", "Обычное число?", a => double.IsFinite(D(a[0])));
        F("math", "sign|math.sign|std::signbit", "sign(x)", "−1, 0 или 1.", a => (double)Math.Sign(D(a[0])));
        F("math", "is_even|even", "is_even(n)", "Чётное?", a => D(a[0]) % 2 == 0);
        F("math", "is_odd|odd", "is_odd(n)", "Нечётное?", a => Math.Abs(D(a[0]) % 2) == 1);
        F("math", "is_prime|isprime", "is_prime(n)", "Простое?", a => { var n = (long)D(a[0]); if (n < 2) return false; for (long i = 2; i * i <= n; i++) if (n % i == 0) return false; return true; });
        F("math", "percent|pct", "percent(part, total)", "Процент части от целого.", a => D(a[1]) == 0 ? 0 : D(a[0]) / D(a[1]) * 100);

        // ---- игровые помощники (как в Unity/Godot)
        F("game", "clamp|std::clamp|Mathf.Clamp", "clamp(x, lo, hi)", "Ограничить диапазоном.", a => Math.Clamp(D(a[0]), D(a[1]), D(a[2])));
        F("game", "clamp01|saturate", "clamp01(x)", "Ограничить 0…1.", a => Math.Clamp(D(a[0]), 0, 1));
        F("game", "lerp|std::lerp|Mathf.Lerp|mix", "lerp(a, b, t)", "Плавно между a и b (t от 0 до 1).", a => D(a[0]) + (D(a[1]) - D(a[0])) * D(a[2]));
        F("game", "inverse_lerp|unlerp|Mathf.InverseLerp", "inverse_lerp(a, b, x)", "Где x между a и b (0…1).", a => D(a[1]) == D(a[0]) ? 0 : (D(a[2]) - D(a[0])) / (D(a[1]) - D(a[0])));
        F("game", "remap|map_range", "remap(x, a1, b1, a2, b2)", "Перевести из одного диапазона в другой.", a => D(a[3]) + (D(a[0]) - D(a[1])) * (D(a[4]) - D(a[3])) / (D(a[2]) - D(a[1])));
        F("game", "smoothstep|Mathf.SmoothStep", "smoothstep(edge0, edge1, x)", "Плавная ступенька.", a => { var t = Math.Clamp((D(a[2]) - D(a[0])) / (D(a[1]) - D(a[0])), 0, 1); return t * t * (3 - 2 * t); });
        F("game", "move_towards|Mathf.MoveTowards", "move_towards(cur, target, step)", "Сдвинуть к цели не больше чем на step.", a => Math.Abs(D(a[1]) - D(a[0])) <= D(a[2]) ? D(a[1]) : D(a[0]) + Math.Sign(D(a[1]) - D(a[0])) * D(a[2]));
        F("game", "wrap|Mathf.Repeat", "wrap(x, lo, hi)", "Зациклить в диапазоне.", a => { double lo = D(a[1]), r = D(a[2]) - lo; return lo + ((D(a[0]) - lo) % r + r) % r; });
        F("game", "ping_pong|Mathf.PingPong", "ping_pong(t, length)", "Туда-обратно.", a => { var l = D(a[1]); var t = D(a[0]) % (2 * l); return l - Math.Abs(t - l); });
        F("game", "approx|Mathf.Approximately", "approx(a, b)", "Почти равны (1e-6).", a => Math.Abs(D(a[0]) - D(a[1])) < 1e-6);
        F("game", "chance|roll", "chance(percent)", "Случайное «да» с вероятностью percent %.", a => Rng.NextDouble() * 100 < D(a[0]));
        F("game", "dice|roll_dice", "dice(\"2d6+1\")", "Бросок костей в нотации D&D.", a =>
        {
            var m = Regex.Match(S(a[0]).Replace(" ", ""), @"^(\d*)d(\d+)([+-]\d+)?$", RegexOptions.IgnoreCase);
            if (!m.Success) throw new LibError("пример: 2d6+1");
            var n = m.Groups[1].Value == "" ? 1 : int.Parse(m.Groups[1].Value);
            var total = Enumerable.Range(0, Math.Min(n, 1000)).Sum(_ => Rng.Next(1, int.Parse(m.Groups[2].Value) + 1));
            return (double)(total + (m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0));
        });
        F("game", "weighted_choice|random.weighted", "weighted_choice(items, weights)", "Случайный элемент с весами.", a =>
        {
            var items = L(a[0]); var w = L(a[1]).Select(D).ToList();
            var r = Rng.NextDouble() * w.Sum();
            for (var i = 0; i < items.Count; i++) { r -= w[i]; if (r <= 0) return items[i]; }
            return items[^1];
        });
        F("game", "vec|vec2|vec3|Vector3|glm::vec3", "vec(x, y, z?)", "Вектор [x, y(, z)].", a => a.Select(x => (object?)D(x)).ToList());
        F("game", "vec.add|vadd", "vec.add(a, b)", "Сумма векторов.", a => L(a[0]).Zip(L(a[1])).Select(p => (object?)(D(p.First) + D(p.Second))).ToList());
        F("game", "vec.sub|vsub", "vec.sub(a, b)", "Разность векторов.", a => L(a[0]).Zip(L(a[1])).Select(p => (object?)(D(p.First) - D(p.Second))).ToList());
        F("game", "vec.scale|vscale", "vec.scale(v, k)", "Умножить вектор на число.", a => L(a[0]).Select(x => (object?)(D(x) * D(a[1]))).ToList());
        F("game", "vec.dot|dot|glm::dot", "vec.dot(a, b)", "Скалярное произведение.", a => L(a[0]).Zip(L(a[1])).Sum(p => D(p.First) * D(p.Second)));
        F("game", "vec.cross|cross|glm::cross", "vec.cross(a, b)", "Векторное произведение (3D).", a => { var x = L(a[0]).Select(D).ToList(); var y = L(a[1]).Select(D).ToList(); return new List<object?> { x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0] }; });
        F("game", "vec.length|vlen|magnitude|glm::length", "vec.length(v)", "Длина вектора.", a => Math.Sqrt(L(a[0]).Sum(x => D(x) * D(x))));
        F("game", "vec.normalize|normalize|glm::normalize", "vec.normalize(v)", "Единичный вектор.", a => { var v = L(a[0]).Select(D).ToList(); var n = Math.Sqrt(v.Sum(x => x * x)); return v.Select(x => (object?)(n == 0 ? 0 : x / n)).ToList(); });
        F("game", "vec.lerp|vlerp", "vec.lerp(a, b, t)", "Плавно между векторами.", a => L(a[0]).Zip(L(a[1])).Select(p => (object?)(D(p.First) + (D(p.Second) - D(p.First)) * D(a[2]))).ToList());
        F("game", "color.hex_to_rgb|hex_to_rgb", "hex_to_rgb(\"#ff8800\")", "Цвет [r, g, b] 0…255.", a => { var h = S(a[0]).TrimStart('#'); if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}")); return new List<object?> { (double)Convert.ToInt32(h[..2], 16), (double)Convert.ToInt32(h[2..4], 16), (double)Convert.ToInt32(h[4..6], 16) }; });
        F("game", "color.rgb_to_hex|rgb_to_hex|rgb", "rgb_to_hex(r, g, b)", "Цвет в #RRGGBB.", a => { var c = a.Count == 1 ? L(a[0]) : a; return "#" + string.Concat(c.Take(3).Select(x => ((int)Math.Clamp(D(x), 0, 255)).ToString("X2"))); });
        F("game", "color.lerp|lerp_color", "color.lerp(\"#000\", \"#fff\", t)", "Смешать два цвета.", a =>
        {
            var x = (List<object?>)Library["hex_to_rgb"].Run([a[0]])!; var y = (List<object?>)Library["hex_to_rgb"].Run([a[1]])!;
            return Library["rgb_to_hex"].Run(x.Zip(y).Select(p => (object?)(D(p.First) + (D(p.Second) - D(p.First)) * D(a[2]))).ToList());
        });
        F("game", "colorsys.rgb_to_hsv|rgb_to_hsv", "rgb_to_hsv(r, g, b)", "RGB (0…1) → HSV.", a => { double r = D(a[0]), g = D(a[1]), bl = D(a[2]); double mx = Math.Max(r, Math.Max(g, bl)), mn = Math.Min(r, Math.Min(g, bl)), dd = mx - mn; double h = dd == 0 ? 0 : mx == r ? ((g - bl) / dd % 6) : mx == g ? (bl - r) / dd + 2 : (r - g) / dd + 4; return new List<object?> { (h / 6 + 1) % 1, mx == 0 ? 0 : dd / mx, mx }; });
        F("game", "colorsys.hsv_to_rgb|hsv_to_rgb", "hsv_to_rgb(h, s, v)", "HSV (0…1) → RGB (0…1).", a => { double h = D(a[0]) * 6, s = D(a[1]), v = D(a[2]); var i = (int)Math.Floor(h) % 6; var f = h - Math.Floor(h); double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s); var (r, g, bb) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) }; return new List<object?> { r, g, bb }; });
        F("game", "seconds|time.seconds", "seconds(\"1h30m\")", "Длительность в секундах из 1h30m15s.", a => Regex.Matches(S(a[0]), @"(\d+(?:\.\d+)?)\s*(d|h|m|s|ms)").Sum(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * m.Groups[2].Value switch { "d" => 86400, "h" => 3600, "m" => 60, "ms" => 0.001, _ => 1 }));
        F("game", "ticks|frames", "ticks(seconds, fps=60)", "Кадры за время.", a => Math.Round(D(a[0]) * D(A(a, 1, 60.0))));

        // ---- random
        F("random", "random.random|random|rand01", "random()", "Случайное 0…1.", _ => Rng.NextDouble());
        F("random", "random.randint|randint|std::rand|rand", "randint(a, b)", "Случайное целое от a до b включительно.", a => a.Count == 0 ? (double)Rng.Next() : (double)Rng.NextInt64((long)D(a[0]), (long)D(a[1]) + 1));
        F("random", "random.uniform|uniform|std::uniform_real_distribution", "uniform(a, b)", "Случайное дробное между a и b.", a => D(a[0]) + Rng.NextDouble() * (D(a[1]) - D(a[0])));
        F("random", "random.choice|choice|pick", "choice(list)", "Случайный элемент.", a => L(a[0]) is { Count: > 0 } l ? l[Rng.Next(l.Count)] : throw new LibError("пустой список"));
        F("random", "random.choices|choices", "choices(list, k=1)", "k случайных элементов с повторами.", a => { var l = L(a[0]); return Enumerable.Range(0, Int(A(a, 1, 1.0))).Select(_ => l[Rng.Next(l.Count)]).ToList(); });
        F("random", "random.sample|sample", "sample(list, k)", "k разных случайных элементов.", a => L(a[0]).OrderBy(_ => Rng.Next()).Take(Int(a[1])).ToList());
        F("random", "random.shuffle|shuffle|std::shuffle", "shuffle(list)", "Перемешанная копия.", a => L(a[0]).OrderBy(_ => Rng.Next()).ToList());
        F("random", "random.seed|seed|std::srand|srand", "seed(n)", "Повторяемые случайные числа.", a => { _rng = new Random(Int(a[0])); return null; });
        F("random", "random.gauss|gauss|random.normalvariate|std::normal_distribution", "gauss(mu, sigma)", "Нормальное распределение.", a => D(a[0]) + D(a[1]) * Math.Sqrt(-2 * Math.Log(1 - Rng.NextDouble())) * Math.Cos(2 * Math.PI * Rng.NextDouble()));
        F("random", "uuid.uuid4|uuid4|uuid|guid", "uuid4()", "Уникальный идентификатор.", _ => Guid.NewGuid().ToString());
        F("random", "secrets.token_hex|token_hex", "token_hex(n=16)", "Случайная hex-строка.", a => Convert.ToHexString(RandomNumberGenerator.GetBytes(Int(A(a, 0, 16.0)))).ToLowerInvariant());

        // ---- строки (str.*, вызываются и как "abc".upper())
        F("str", "str.upper|upper|std::toupper|toupper|toUpperCase", "upper(s)", "ПРОПИСНЫЕ.", a => S(a[0]).ToUpperInvariant());
        F("str", "str.lower|lower|std::tolower|tolower|toLowerCase", "lower(s)", "строчные.", a => S(a[0]).ToLowerInvariant());
        F("str", "str.title|title", "title(s)", "Каждое Слово С Большой.", a => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(S(a[0]).ToLowerInvariant()));
        F("str", "str.capitalize|capitalize", "capitalize(s)", "Первая буква большая.", a => S(a[0]) is { Length: > 0 } s ? char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant() : "");
        F("str", "str.swapcase|swapcase", "swapcase(s)", "Поменять регистр.", a => string.Concat(S(a[0]).Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))));
        F("str", "str.casefold|casefold", "casefold(s)", "Для сравнения без регистра.", a => S(a[0]).ToLowerInvariant());
        F("str", "str.strip|strip|trim", "strip(s, chars?)", "Убрать пробелы (или chars) по краям.", a => a.Count > 1 ? S(a[0]).Trim(S(a[1]).ToCharArray()) : S(a[0]).Trim());
        F("str", "str.lstrip|lstrip|trim_start|trimStart", "lstrip(s, chars?)", "Слева.", a => a.Count > 1 ? S(a[0]).TrimStart(S(a[1]).ToCharArray()) : S(a[0]).TrimStart());
        F("str", "str.rstrip|rstrip|trim_end|trimEnd", "rstrip(s, chars?)", "Справа.", a => a.Count > 1 ? S(a[0]).TrimEnd(S(a[1]).ToCharArray()) : S(a[0]).TrimEnd());
        F("str", "str.split|split", "split(s, sep=пробелы, max=-1)", "Разбить на список.", a =>
        {
            var s = S(a[0]);
            var parts = a.Count > 1 && a[1] is not null && S(a[1]) != "" ? (a.Count > 2 && D(a[2]) >= 0 ? s.Split(S(a[1]), Int(a[2]) + 1) : s.Split(S(a[1]))) : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(p => (object?)p).ToList();
        });
        F("str", "str.rsplit|rsplit", "rsplit(s, sep, max)", "Разбить справа.", a => { var s = S(a[0]); var sep = S(a[1]); var max = Int(A(a, 2, -1.0)); var parts = s.Split(sep).ToList(); while (max >= 0 && parts.Count > max + 1) { parts[0] += sep + parts[1]; parts.RemoveAt(1); } return parts.Select(p => (object?)p).ToList(); });
        F("str", "str.splitlines|splitlines|lines", "splitlines(s)", "Строки текста.", a => S(a[0]).Replace("\r\n", "\n").Split('\n').Select(p => (object?)p).ToList());
        F("str", "str.join|join", "join(sep, list) / list.join(sep)", "Склеить список через sep.", a => a[0] is List<object?> l ? string.Join(S(A(a, 1, "")), l.Select(ToText)) : string.Join(S(a[0]), L(a[1]).Select(ToText)));
        F("str", "str.replace|replace|std::replace", "replace(s, old, new, count=-1)", "Заменить.", a =>
        {
            var s = S(a[0]); var o = S(a[1]); var n = S(a[2]); var c = Int(A(a, 3, -1.0));
            if (c < 0 || o == "") return o == "" ? s : s.Replace(o, n);
            var sb = new StringBuilder(); var i = 0;
            while (c-- > 0 && s.IndexOf(o, i, StringComparison.Ordinal) is var k && k >= 0) { sb.Append(s, i, k - i).Append(n); i = k + o.Length; }
            return sb.Append(s[i..]).ToString();
        });
        F("str", "str.find|find|std::string::find|indexOf", "find(s, sub, start=0)", "Позиция или −1.", a => (double)S(a[0]).IndexOf(S(a[1]), Math.Min(Int(A(a, 2, 0.0)), S(a[0]).Length), StringComparison.Ordinal));
        F("str", "str.rfind|rfind|lastIndexOf", "rfind(s, sub)", "Позиция с конца или −1.", a => (double)S(a[0]).LastIndexOf(S(a[1]), StringComparison.Ordinal));
        F("str", "str.index", "str.index(s, sub)", "Позиция (ошибка, если нет).", a => S(a[0]).IndexOf(S(a[1]), StringComparison.Ordinal) is var i && i >= 0 ? (double)i : throw new LibError("подстрока не найдена"));
        F("str", "str.count", "str.count(s, sub)", "Сколько раз встречается.", a => S(a[1]) == "" ? 0 : (double)((S(a[0]).Length - S(a[0]).Replace(S(a[1]), "").Length) / S(a[1]).Length));
        F("str", "str.startswith|startswith|starts_with|startsWith", "startswith(s, prefix)", "Начинается с…", a => S(a[0]).StartsWith(S(a[1]), StringComparison.Ordinal));
        F("str", "str.endswith|endswith|ends_with|endsWith", "endswith(s, suffix)", "Заканчивается на…", a => S(a[0]).EndsWith(S(a[1]), StringComparison.Ordinal));
        F("str", "str.contains|includes|has", "contains(s, sub)", "Содержит ли (и для списков).", a => a[0] is List<object?> l ? l.Any(x => Eq(x, a[1])) : S(a[0]).Contains(S(a[1]), StringComparison.Ordinal));
        F("str", "str.isdigit|isdigit|isnumeric|str.isnumeric|std::isdigit", "isdigit(s)", "Только цифры.", a => S(a[0]) is { Length: > 0 } s && s.All(char.IsDigit));
        F("str", "str.isalpha|isalpha|std::isalpha", "isalpha(s)", "Только буквы.", a => S(a[0]) is { Length: > 0 } s && s.All(char.IsLetter));
        F("str", "str.isalnum|isalnum|std::isalnum", "isalnum(s)", "Буквы и цифры.", a => S(a[0]) is { Length: > 0 } s && s.All(char.IsLetterOrDigit));
        F("str", "str.isspace|isspace|std::isspace", "isspace(s)", "Только пробелы.", a => S(a[0]) is { Length: > 0 } s && s.All(char.IsWhiteSpace));
        F("str", "str.isupper|isupper|std::isupper", "isupper(s)", "Все буквы большие.", a => S(a[0]).Any(char.IsLetter) && !S(a[0]).Any(char.IsLower));
        F("str", "str.islower|islower|std::islower", "islower(s)", "Все буквы маленькие.", a => S(a[0]).Any(char.IsLetter) && !S(a[0]).Any(char.IsUpper));
        F("str", "str.isidentifier|isidentifier", "isidentifier(s)", "Годится как имя переменной.", a => Regex.IsMatch(S(a[0]), @"^[A-Za-z_]\w*$"));
        F("str", "str.zfill|zfill", "zfill(s, width)", "Дополнить нулями слева.", a => S(a[0]).PadLeft(Int(a[1]), '0'));
        F("str", "str.center|center", "center(s, width, fill=\" \")", "По центру.", a => FormatSpec(S(a[0]), $"{S(A(a, 2, " "))[0]}^{Int(a[1])}"));
        F("str", "str.ljust|ljust|pad_right|padEnd", "ljust(s, width, fill=\" \")", "Влево, дополнить справа.", a => S(a[0]).PadRight(Int(a[1]), S(A(a, 2, " "))[0]));
        F("str", "str.rjust|rjust|pad_left|padStart", "rjust(s, width, fill=\" \")", "Вправо, дополнить слева.", a => S(a[0]).PadLeft(Int(a[1]), S(A(a, 2, " "))[0]));
        F("str", "str.partition|partition", "partition(s, sep)", "[до, sep, после].", a => { var s = S(a[0]); var i = s.IndexOf(S(a[1]), StringComparison.Ordinal); return i < 0 ? new List<object?> { s, "", "" } : [s[..i], S(a[1]), s[(i + S(a[1]).Length)..]]; });
        F("str", "str.removeprefix|removeprefix", "removeprefix(s, prefix)", "Убрать приставку.", a => S(a[0]).StartsWith(S(a[1]), StringComparison.Ordinal) ? S(a[0])[S(a[1]).Length..] : S(a[0]));
        F("str", "str.removesuffix|removesuffix", "removesuffix(s, suffix)", "Убрать окончание.", a => S(a[0]).EndsWith(S(a[1]), StringComparison.Ordinal) ? S(a[0])[..^S(a[1]).Length] : S(a[0]));
        F("str", "str.expandtabs|expandtabs", "expandtabs(s, n=8)", "Табуляции → пробелы.", a => S(a[0]).Replace("\t", new string(' ', Int(A(a, 1, 8.0)))));
        F("str", "str.substr|substr|std::string::substr|substring|mid", "substr(s, start, len?)", "Кусок строки (как в C++).", a => { var s = S(a[0]); var st = Math.Clamp(Int(a[1]), 0, s.Length); return a.Count > 2 ? s.Substring(st, Math.Clamp(Int(a[2]), 0, s.Length - st)) : s[st..]; });
        F("str", "str.slice|slice", "slice(x, start, end?)", "Срез строки или списка (−1 — с конца).", a => { var count = a[0] is List<object?> l0 ? l0.Count : S(a[0]).Length; var f = Math.Clamp(Idx(Int(a[1]), count), 0, count); var t = Math.Clamp(a.Count > 2 ? Idx(Int(a[2]), count) : count, f, count); return a[0] is List<object?> l ? l.GetRange(f, t - f) : S(a[0])[f..t]; });
        F("str", "str.left|left", "left(s, n)", "Первые n символов.", a => S(a[0])[..Math.Min(Int(a[1]), S(a[0]).Length)]);
        F("str", "str.right|right", "right(s, n)", "Последние n символов.", a => S(a[0])[Math.Max(0, S(a[0]).Length - Int(a[1]))..]);
        F("str", "str.repeat|repeat", "repeat(s, n)", "Повторить n раз.", a => string.Concat(Enumerable.Repeat(S(a[0]), Math.Clamp(Int(a[1]), 0, 10000))));
        F("str", "str.reverse", "str.reverse(s)", "Строка задом наперёд.", a => new string(S(a[0]).Reverse().ToArray()));
        F("str", "str.chars|chars", "chars(s)", "Список символов.", a => S(a[0]).Select(c => (object?)c.ToString()).ToList());
        F("str", "str.words|words", "words(s)", "Список слов.", a => Regex.Matches(S(a[0]), @"[\p{L}\p{N}_']+").Select(m => (object?)m.Value).ToList());
        F("str", "str.slug|slug|slugify", "slug(s)", "my-mod-name из «My Mod Name!».", a => Regex.Replace(Regex.Replace(S(a[0]).ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-"), "^-|-$", ""));
        F("str", "str.snake|snake_case", "snake_case(s)", "my_mod_name.", a => Regex.Replace(Regex.Replace(S(a[0]), @"([a-z0-9])([A-Z])", "$1_$2"), @"[^\p{L}\p{N}]+", "_").Trim('_').ToLowerInvariant());
        F("str", "str.camel|camel_case|camelCase", "camel_case(s)", "myModName.", a => { var w = Regex.Split(S(a[0]), @"[^\p{L}\p{N}]+").Where(x => x != "").ToList(); return string.Concat(w.Select((x, i) => i == 0 ? x.ToLowerInvariant() : char.ToUpperInvariant(x[0]) + x[1..].ToLowerInvariant())); });
        F("str", "str.pascal|pascal_case|PascalCase", "pascal_case(s)", "MyModName.", a => string.Concat(Regex.Split(S(a[0]), @"[^\p{L}\p{N}]+").Where(x => x != "").Select(x => char.ToUpperInvariant(x[0]) + x[1..].ToLowerInvariant())));
        F("str", "str.kebab|kebab_case", "kebab_case(s)", "my-mod-name.", a => Regex.Replace(Regex.Replace(S(a[0]), @"([a-z0-9])([A-Z])", "$1-$2"), @"[^\p{L}\p{N}]+", "-").Trim('-').ToLowerInvariant());
        F("str", "str.truncate|truncate|ellipsis", "truncate(s, n, \"…\")", "Обрезать с многоточием.", a => S(a[0]).Length <= Int(a[1]) ? S(a[0]) : S(a[0])[..Math.Max(0, Int(a[1]) - S(A(a, 2, "…")).Length)] + S(A(a, 2, "…")));
        F("str", "str.format", "\"{} и {}\".format(a, b)", "Подставить значения.", a => Library["format"].Run(a));
        F("str", "str.encode|encode|utf8_bytes", "encode(s)", "Байты UTF-8 (список чисел).", a => Encoding.UTF8.GetBytes(S(a[0])).Select(x => (object?)(double)x).ToList());
        F("str", "str.decode|decode", "decode(bytes)", "Строка из байтов UTF-8.", a => Encoding.UTF8.GetString(L(a[0]).Select(x => (byte)D(x)).ToArray()));
        F("str", "str.ord_sum|checksum", "checksum(s)", "Сумма кодов символов.", a => (double)S(a[0]).Sum(c => c));

        // ---- textwrap / string helpers
        F("textwrap", "textwrap.wrap|wrap_text", "textwrap.wrap(s, width=70)", "Разбить текст на строки по ширине.", a =>
        {
            var width = Int(A(a, 1, 70.0)); var lines = new List<object?>(); var cur = new StringBuilder();
            foreach (var w in S(a[0]).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (cur.Length > 0 && cur.Length + 1 + w.Length > width) { lines.Add(cur.ToString()); cur.Clear(); }
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(w);
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            return lines;
        });
        F("textwrap", "textwrap.fill|fill_text", "textwrap.fill(s, width=70)", "Текст с переносами строк.", a => string.Join("\n", ((List<object?>)Library["textwrap.wrap"].Run(a)!).Select(ToText)));
        F("textwrap", "textwrap.shorten|shorten", "textwrap.shorten(s, width)", "Сократить по словам с « […]».", a => { var s = Regex.Replace(S(a[0]), @"\s+", " ").Trim(); var w = Int(a[1]); if (s.Length <= w) return s; var cut = s[..Math.Max(0, w - 6)]; var sp = cut.LastIndexOf(' '); return (sp > 0 ? cut[..sp] : cut) + " [...]"; });
        F("textwrap", "textwrap.dedent|dedent", "textwrap.dedent(s)", "Убрать общий отступ.", a => { var ls = S(a[0]).Split('\n'); var ind = ls.Where(l => l.Trim() != "").Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min(); return string.Join("\n", ls.Select(l => l.Length >= ind ? l[ind..] : l.TrimStart())); });
        F("textwrap", "textwrap.indent|indent", "textwrap.indent(s, prefix)", "Добавить отступ каждой строке.", a => string.Join("\n", S(a[0]).Split('\n').Select(l => S(a[1]) + l)));

        // ---- списки
        F("list", "list.append|append|push|push_back|std::vector::push_back", "append(list, x)", "Новый список с x в конце.", a => L(a[0]).Append(A(a, 1)).ToList());
        F("list", "list.prepend|prepend|push_front|unshift", "prepend(list, x)", "Новый список с x в начале.", a => L(a[0]).Prepend(A(a, 1)).ToList());
        F("list", "list.extend|extend|concat", "extend(a, b)", "Склеить списки.", a => a.SelectMany(L).ToList());
        F("list", "list.insert|insert", "insert(list, i, x)", "Вставить x на место i.", a => { var l = L(a[0]).ToList(); l.Insert(Math.Clamp(Idx(Int(a[1]), l.Count), 0, l.Count), A(a, 2)); return l; });
        F("list", "list.remove|remove|erase", "remove(list, x)", "Убрать первое x.", a => { var l = L(a[0]).ToList(); var i = l.FindIndex(y => Eq(y, a[1])); if (i >= 0) l.RemoveAt(i); return l; });
        F("list", "list.remove_all|remove_all", "remove_all(list, x)", "Убрать все x.", a => L(a[0]).Where(y => !Eq(y, a[1])).ToList());
        F("list", "list.pop|pop|pop_back", "pop(list, i=-1)", "Список без элемента i.", a => { var l = L(a[0]).ToList(); if (l.Count > 0) l.RemoveAt(Idx(Int(A(a, 1, -1.0)), l.Count)); return l; });
        F("list", "list.index|index_of", "index_of(list, x)", "Позиция x или −1.", a => (double)L(a[0]).FindIndex(y => Eq(y, a[1])));
        F("list", "list.count|count|std::count", "count(list, x)", "Сколько раз x.", a => a[0] is string s ? (double)Regex.Matches(s, Regex.Escape(S(a[1]))).Count : L(a[0]).Count(y => Eq(y, a[1])));
        F("list", "list.sort", "list.sort(xs, reverse=false)", "Отсортировать.", a => Library["sorted"].Run(a));
        F("list", "list.reverse", "list.reverse(xs)", "Развернуть.", a => Enumerable.Reverse(L(a[0])).ToList());
        F("list", "list.get|get|at|std::vector::at", "get(x, key, default)", "Элемент списка/словаря или default.", a => a[0] switch { Dictionary<string, object?> m => m.TryGetValue(S(a[1]), out var v) ? v : A(a, 2), List<object?> l => Idx(Int(a[1]), l.Count) is var i && i >= 0 && i < l.Count ? l[i] : A(a, 2), _ => A(a, 2) });
        F("list", "list.set|set_at", "set_at(list, i, x)", "Новый список с x на месте i.", a => { var l = L(a[0]).ToList(); l[Idx(Int(a[1]), l.Count)] = A(a, 2); return l; });
        F("list", "list.first|first|front|std::vector::front", "first(list)", "Первый элемент.", a => L(a[0]).FirstOrDefault());
        F("list", "list.last|last|back|std::vector::back", "last(list)", "Последний элемент.", a => L(a[0]).LastOrDefault());
        F("list", "list.take|take|head", "take(list, n)", "Первые n.", a => L(a[0]).Take(Int(a[1])).ToList());
        F("list", "list.skip|skip|drop|tail", "skip(list, n)", "Без первых n.", a => L(a[0]).Skip(Int(a[1])).ToList());
        F("list", "list.flatten|flatten", "flatten(list)", "Раскрыть вложенные списки.", a => { var r = new List<object?>(); void Go(object? x) { if (x is List<object?> l) foreach (var y in l) Go(y); else r.Add(x); } Go(a[0]); return r; });
        F("list", "list.chunk|chunk|batched|itertools.batched", "chunk(list, n)", "Разбить на части по n.", a => L(a[0]).Chunk(Math.Max(1, Int(a[1]))).Select(c => (object?)c.ToList()).ToList());
        F("list", "list.empty|empty|std::empty|is_empty", "empty(x)", "Пусто ли.", a => !Truthy(A(a, 0)));
        F("list", "list.fill|fill|std::fill|std::vector::assign", "fill(n, value)", "Список из n одинаковых.", a => Enumerable.Repeat(A(a, 1), Math.Clamp(Int(a[0]), 0, 10000)).ToList());
        F("list", "list.find|std::find", "std::find(list, x)", "Позиция x или −1.", a => (double)L(a[0]).FindIndex(y => Eq(y, a[1])));
        F("list", "list.binary_search|std::binary_search|bisect.contains", "std::binary_search(sorted, x)", "Есть ли x в отсортированном списке.", a => L(a[0]).BinarySearch(A(a, 1), Comparer<object?>.Create(Cmp)) >= 0);
        F("list", "bisect.bisect_left|bisect_left|std::lower_bound|lower_bound", "bisect_left(sorted, x)", "Куда вставить x слева.", a => { var l = L(a[0]); var i = 0; while (i < l.Count && Cmp(l[i], a[1]) < 0) i++; return (double)i; });
        F("list", "bisect.bisect_right|bisect_right|bisect.bisect|std::upper_bound|upper_bound", "bisect_right(sorted, x)", "Куда вставить x справа.", a => { var l = L(a[0]); var i = 0; while (i < l.Count && Cmp(l[i], a[1]) <= 0) i++; return (double)i; });
        F("list", "heapq.nlargest|nlargest", "nlargest(n, list)", "n наибольших.", a => L(a[1]).OrderByDescending(x => x, Comparer<object?>.Create(Cmp)).Take(Int(a[0])).ToList());
        F("list", "heapq.nsmallest|nsmallest", "nsmallest(n, list)", "n наименьших.", a => L(a[1]).OrderBy(x => x, Comparer<object?>.Create(Cmp)).Take(Int(a[0])).ToList());
        F("list", "list.sort_by_key|sort_by", "sort_by(list_of_dicts, \"key\", reverse=false)", "Сортировать словари по полю.", a => { var l = L(a[0]).OrderBy(x => x is Dictionary<string, object?> m && m.TryGetValue(S(a[1]), out var v) ? v : null, Comparer<object?>.Create(Cmp)).ToList(); if (Truthy(A(a, 2, false))) l.Reverse(); return l; });
        F("list", "list.pluck|pluck", "pluck(list_of_dicts, \"key\")", "Достать поле у каждого.", a => L(a[0]).Select(x => x is Dictionary<string, object?> m && m.TryGetValue(S(a[1]), out var v) ? v : null).ToList());
        F("list", "list.group_by|group_by|itertools.groupby", "group_by(list_of_dicts, \"key\")", "Словарь: значение поля → список.", a => L(a[0]).GroupBy(x => x is Dictionary<string, object?> m && m.TryGetValue(S(a[1]), out var v) ? ToText(v) : ToText(x)).ToDictionary(g => g.Key, g => (object?)g.ToList()));
        F("list", "list.min_by|min_by", "min_by(list_of_dicts, \"key\")", "Элемент с наименьшим полем.", a => L(a[0]).MinBy(x => x is Dictionary<string, object?> m && m.TryGetValue(S(a[1]), out var v) ? D(v) : double.MaxValue));
        F("list", "list.max_by|max_by", "max_by(list_of_dicts, \"key\")", "Элемент с наибольшим полем.", a => L(a[0]).MaxBy(x => x is Dictionary<string, object?> m && m.TryGetValue(S(a[1]), out var v) ? D(v) : double.MinValue));

        // ---- itertools / functools / collections
        F("itertools", "itertools.chain|chain", "chain(a, b, …)", "Последовательно все списки.", a => a.SelectMany(L).ToList());
        F("itertools", "itertools.product|product", "product(a, b)", "Все пары из двух списков.", a => a.Skip(1).Aggregate(L(a[0]).Select(x => new List<object?> { x }), (acc, l) => acc.SelectMany(p => L(l).Select(y => p.Append(y).ToList()))).Take(10000).Select(p => (object?)p).ToList());
        F("itertools", "itertools.permutations|permutations|std::next_permutation", "permutations(list, r=len)", "Перестановки (до 8 элементов).", a =>
        {
            var l = L(a[0]); var r = Int(A(a, 1, (double)l.Count)); if (l.Count > 8) throw new LibError("не больше 8 элементов");
            IEnumerable<List<object?>> Go(List<object?> rest, int k) => k == 0 ? [[]] : rest.SelectMany((x, i) => Go(rest.Where((_, j) => j != i).ToList(), k - 1).Select(t => t.Prepend(x).ToList()));
            return Go(l, r).Select(p => (object?)p).ToList();
        });
        F("itertools", "itertools.combinations|combinations", "combinations(list, r)", "Сочетания по r (до 20 элементов).", a =>
        {
            var l = L(a[0]); if (l.Count > 20) throw new LibError("не больше 20 элементов");
            IEnumerable<List<object?>> Go(int start, int k) => k == 0 ? [[]] : Enumerable.Range(start, l.Count - start).SelectMany(i => Go(i + 1, k - 1).Select(t => t.Prepend(l[i]).ToList()));
            return Go(0, Int(a[1])).Select(p => (object?)p).ToList();
        });
        F("itertools", "itertools.accumulate|accumulate|std::partial_sum|partial_sum", "accumulate(list)", "Нарастающие суммы.", a => { double s = 0; return L(a[0]).Select(x => (object?)(s += D(x))).ToList(); });
        F("itertools", "itertools.repeat", "itertools.repeat(x, n)", "x n раз.", a => Enumerable.Repeat(a[0], Math.Clamp(Int(a[1]), 0, 10000)).ToList());
        F("itertools", "itertools.cycle|cycle", "cycle(list, n)", "Повторять список, пока не наберётся n.", a => { var l = L(a[0]); return l.Count == 0 ? [] : Enumerable.Range(0, Math.Clamp(Int(a[1]), 0, 10000)).Select(i => l[i % l.Count]).ToList(); });
        F("itertools", "itertools.pairwise|pairwise", "pairwise(list)", "Соседние пары.", a => L(a[0]).Zip(L(a[0]).Skip(1)).Select(p => (object?)new List<object?> { p.First, p.Second }).ToList());
        F("itertools", "itertools.zip_longest|zip_longest", "zip_longest(a, b, fill)", "Попарно до самого длинного.", a => { var x = L(a[0]); var y = L(a[1]); return Enumerable.Range(0, Math.Max(x.Count, y.Count)).Select(i => (object?)new List<object?> { i < x.Count ? x[i] : A(a, 2), i < y.Count ? y[i] : A(a, 2) }).ToList(); });
        F("functools", "map", "map(list, \"функция\")", "Применить функцию (по имени) к каждому.", a => null);
        F("functools", "filter", "filter(list, \"функция\")", "Оставить, где функция истинна.", a => null);
        F("functools", "reduce|functools.reduce", "reduce(list, \"+\" | \"*\" | \"max\" | \"функция\", start?)", "Свернуть список.", a => null);
        F("functools", "sorted_by", "sorted_by(list, \"функция\")", "Сортировка по ключу-функции.", a => null);
        F("collections", "collections.Counter|Counter|counter|tally", "Counter(list)", "Словарь: элемент → сколько раз.", a => L(a[0]).GroupBy(ToText).ToDictionary(g => g.Key, g => (object?)(double)g.Count()));
        F("collections", "collections.most_common|most_common", "most_common(list, n)", "Самые частые [элемент, раз].", a => L(a[0]).GroupBy(ToText).OrderByDescending(g => g.Count()).Take(Int(A(a, 1, 1000.0))).Select(g => (object?)new List<object?> { g.Key, (double)g.Count() }).ToList());
        F("collections", "collections.OrderedDict|OrderedDict|collections.defaultdict|defaultdict", "OrderedDict(pairs)", "Словарь.", a => a.Count > 0 ? new Dictionary<string, object?>(M(a[0])) : new Dictionary<string, object?>());
        F("collections", "collections.deque|deque|std::deque|std::queue|std::stack|stack|queue", "deque(list)", "Очередь — это просто список.", a => a.Count > 0 ? L(a[0]).ToList() : new List<object?>());

        // ---- statistics
        F("statistics", "statistics.mean|mean|avg|average", "mean(list)", "Среднее.", a => L(a[0]) is { Count: > 0 } l ? l.Average(D) : throw new LibError("пустой список"));
        F("statistics", "statistics.median|median", "median(list)", "Медиана.", a => { var l = L(a[0]).Select(D).Order().ToList(); if (l.Count == 0) throw new LibError("пустой список"); return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; });
        F("statistics", "statistics.mode|mode", "mode(list)", "Самое частое.", a => L(a[0]).GroupBy(ToText).OrderByDescending(g => g.Count()).First().First());
        F("statistics", "statistics.variance|variance", "variance(list)", "Дисперсия выборки.", a => { var l = L(a[0]).Select(D).ToList(); var m = l.Average(); return l.Sum(x => (x - m) * (x - m)) / (l.Count - 1); });
        F("statistics", "statistics.pvariance|pvariance", "pvariance(list)", "Дисперсия совокупности.", a => { var l = L(a[0]).Select(D).ToList(); var m = l.Average(); return l.Sum(x => (x - m) * (x - m)) / l.Count; });
        F("statistics", "statistics.stdev|stdev", "stdev(list)", "Стандартное отклонение выборки.", a => Math.Sqrt(D(Library["variance"].Run(a))));
        F("statistics", "statistics.pstdev|pstdev", "pstdev(list)", "Отклонение совокупности.", a => Math.Sqrt(D(Library["pvariance"].Run(a))));
        F("statistics", "statistics.quantiles|quantile|percentile", "quantile(list, q)", "Квантиль (q от 0 до 1).", a => { var l = L(a[0]).Select(D).Order().ToList(); var pos = (l.Count - 1) * D(a[1]); var lo = (int)Math.Floor(pos); return lo + 1 < l.Count ? l[lo] + (l[lo + 1] - l[lo]) * (pos - lo) : l[lo]; });
        F("statistics", "normalize_list|minmax_scale", "normalize_list(list)", "Привести к 0…1.", a => { var l = L(a[0]).Select(D).ToList(); double mn = l.Min(), mx = l.Max(); return l.Select(x => (object?)(mx == mn ? 0 : (x - mn) / (mx - mn))).ToList(); });

        // ---- словари
        F("dict", "dict.keys|keys", "keys(d)", "Ключи.", a => M(a[0]).Keys.Select(k => (object?)k).ToList());
        F("dict", "dict.values|values", "values(d)", "Значения.", a => M(a[0]).Values.ToList());
        F("dict", "dict.items|items", "items(d)", "Пары [ключ, значение].", a => M(a[0]).Select(kv => (object?)new List<object?> { kv.Key, kv.Value }).ToList());
        F("dict", "dict.get", "dict.get(d, key, default)", "Значение или default.", a => M(a[0]).TryGetValue(S(a[1]), out var v) ? v : A(a, 2));
        F("dict", "dict.set|dict.put|put", "dict.set(d, key, value)", "Новый словарь с key = value.", a => new Dictionary<string, object?>(M(a[0])) { [S(a[1])] = A(a, 2) });
        F("dict", "dict.del|dict.pop|dict.remove|del", "dict.del(d, key)", "Новый словарь без key.", a => { var m = new Dictionary<string, object?>(M(a[0])); m.Remove(S(a[1])); return m; });
        F("dict", "dict.has|has_key|dict.contains", "has_key(d, key)", "Есть ли ключ.", a => M(a[0]).ContainsKey(S(a[1])));
        F("dict", "dict.merge|merge|dict.update", "merge(a, b, …)", "Объединить (последние побеждают).", a => a.SelectMany(x => M(x)).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value));
        F("dict", "dict.fromkeys|fromkeys", "fromkeys(keys, value)", "Словарь с одинаковым значением.", a => L(a[0]).DistinctBy(ToText).ToDictionary(ToText, _ => A(a, 1)));
        F("dict", "dict.invert|invert", "invert(d)", "Поменять ключи и значения.", a => M(a[0]).GroupBy(kv => ToText(kv.Value)).ToDictionary(g => g.Key, g => (object?)g.Last().Key));
        F("dict", "dict.pick|pick_keys", "pick_keys(d, keys)", "Только указанные ключи.", a => M(a[0]).Where(kv => L(a[1]).Any(k => S(k) == kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
        F("dict", "zip_dict|dict.from_lists", "zip_dict(keys, values)", "Словарь из двух списков.", a => L(a[0]).Zip(L(a[1])).GroupBy(p => ToText(p.First)).ToDictionary(g => g.Key, g => g.Last().Second));

        // ---- json
        F("json", "json.dumps|json.stringify|JSON.stringify|to_json", "json.dumps(x, indent=0)", "В JSON-текст.", a => ToJsonNode(a[0])?.ToJsonString(new JsonSerializerOptions(JsonOut) { WriteIndented = D(A(a, 1, 0.0)) > 0 }) ?? "null");
        F("json", "json.loads|json.parse|JSON.parse|from_json", "json.loads(text)", "Из JSON-текста.", a => { try { return FromJsonNode(JsonNode.Parse(S(a[0]))); } catch (JsonException e) { throw new LibError("неверный JSON: " + e.Message); } });
        F("json", "json.get|jget|get_path", "json.get(obj, \"a.b.0\", default)", "Значение по пути через точки.", a =>
        {
            object? cur = a[0] is string s0 ? FromText(s0) : a[0];
            foreach (var part in S(a[1]).Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                cur = cur switch
                {
                    Dictionary<string, object?> m => m.TryGetValue(part, out var v) ? v : null,
                    List<object?> l when int.TryParse(part, out var i) && Idx(i, l.Count) is var k && k >= 0 && k < l.Count => l[k],
                    _ => null,
                };
                if (cur is null) return A(a, 2);
            }
            return cur;
        });
        F("json", "json.set|jset|set_path", "json.set(obj, \"a.b\", value)", "Новый объект со значением по пути.", a =>
        {
            var root = FromJsonNode(ToJsonNode(a[0] is string s0 ? FromText(s0) : a[0])) ?? new Dictionary<string, object?>();
            var parts = S(a[1]).Split('.', StringSplitOptions.RemoveEmptyEntries);
            var cur = root;
            for (var i = 0; i < parts.Length; i++)
            {
                var last = i == parts.Length - 1;
                if (cur is Dictionary<string, object?> m)
                {
                    if (last) m[parts[i]] = A(a, 2);
                    else { if (!m.TryGetValue(parts[i], out var next) || next is not (Dictionary<string, object?> or List<object?>)) m[parts[i]] = next = new Dictionary<string, object?>(); cur = next; }
                }
                else if (cur is List<object?> l && int.TryParse(parts[i], out var idx))
                {
                    idx = Idx(idx, l.Count);
                    while (l.Count <= idx) l.Add(null);
                    if (last) l[idx] = A(a, 2); else { l[idx] ??= new Dictionary<string, object?>(); cur = l[idx]; }
                }
                else throw new LibError("путь упёрся в значение");
            }
            return root;
        });

        // ---- re (регулярные выражения)
        static Regex Rx(object? p, object? flags = null) =>
            new(S(p), (S(flags).Contains('i') ? RegexOptions.IgnoreCase : 0) | (S(flags).Contains('m') ? RegexOptions.Multiline : 0) | (S(flags).Contains('s') ? RegexOptions.Singleline : 0), TimeSpan.FromSeconds(1));
        static object? Groups(Match m) => m.Success ? (m.Groups.Count > 1 ? m.Groups.Cast<Group>().Skip(1).Select(g => (object?)g.Value).ToList() : m.Value) : null;
        F("re", "re.match|regex_match|std::regex_match", "re.match(pattern, s, flags?)", "Совпадение в начале строки (группы или текст).", a => Rx("^(?:" + S(a[0]) + ")", A(a, 2)).Match(S(a[1])) is var m && m.Success ? Groups(m) : null);
        F("re", "re.fullmatch", "re.fullmatch(pattern, s)", "Совпадение со всей строкой.", a => Rx("^(?:" + S(a[0]) + ")$", A(a, 2)).Match(S(a[1])) is var m && m.Success ? Groups(m) : null);
        F("re", "re.search|regex_search|std::regex_search", "re.search(pattern, s, flags?)", "Первое совпадение где угодно.", a => Rx(a[0], A(a, 2)).Match(S(a[1])) is var m && m.Success ? Groups(m) : null);
        F("re", "re.test|regex_test|matches", "re.test(pattern, s, flags?)", "Есть ли совпадение (true/false).", a => Rx(a[0], A(a, 2)).IsMatch(S(a[1])));
        F("re", "re.findall|regex_findall", "re.findall(pattern, s, flags?)", "Все совпадения.", a => Rx(a[0], A(a, 2)).Matches(S(a[1])).Select(Groups).ToList());
        F("re", "re.sub|regex_replace|std::regex_replace", "re.sub(pattern, repl, s, flags?)", "Заменить (\\1 / $1 — группы).", a => Rx(a[0], A(a, 3)).Replace(S(a[2]), Regex.Replace(S(a[1]), @"\\(\d)", "$$$1")));
        F("re", "re.split", "re.split(pattern, s)", "Разбить по шаблону.", a => Rx(a[0]).Split(S(a[1])).Select(p => (object?)p).ToList());
        F("re", "re.escape", "re.escape(s)", "Экранировать для шаблона.", a => Regex.Escape(S(a[0])));
        F("re", "re.count", "re.count(pattern, s)", "Сколько совпадений.", a => (double)Rx(a[0]).Matches(S(a[1])).Count);
        F("re", "re.named|regex_named", "re.named(pattern, s)", "Именованные группы (?<name>…) словарём.", a => Rx(a[0]).Match(S(a[1])) is var m && m.Success ? Rx(a[0]).GetGroupNames().Where(n => !int.TryParse(n, out _)).ToDictionary(n => n, n => (object?)m.Groups[n].Value) : null);

        // ---- время (datetime, time)
        static DateTime Dt(object? v) => v switch
        {
            null => DateTime.Now,
            double d => DateTimeOffset.FromUnixTimeMilliseconds((long)(d * 1000)).LocalDateTime,
            _ => DateTime.TryParse(S(v), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : throw new LibError($"«{S(v)}» — не дата"),
        };
        static string PyTime(DateTime t, string f) => Regex.Replace(f, "%[YmdHMSjaAbByIpzZ%]", m => m.Value switch
        {
            "%Y" => t.ToString("yyyy"), "%y" => t.ToString("yy"), "%m" => t.ToString("MM"), "%d" => t.ToString("dd"), "%H" => t.ToString("HH"),
            "%I" => t.ToString("hh"), "%M" => t.ToString("mm"), "%S" => t.ToString("ss"), "%j" => t.DayOfYear.ToString("000"), "%p" => t.ToString("tt", CultureInfo.InvariantCulture),
            "%a" => t.ToString("ddd", CultureInfo.InvariantCulture), "%A" => t.ToString("dddd", CultureInfo.InvariantCulture),
            "%b" => t.ToString("MMM", CultureInfo.InvariantCulture), "%B" => t.ToString("MMMM", CultureInfo.InvariantCulture),
            "%z" => t.ToString("zzz").Replace(":", ""), "%Z" => TimeZoneInfo.Local.StandardName, _ => "%",
        });
        F("time", "time.time|now_ts|std::time|timestamp", "time.time()", "Секунды с 1970 года.", _ => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        F("time", "datetime.now|now|datetime.today", "datetime.now(fmt?)", "Сейчас (ISO или по формату %Y-%m-%d).", a => a.Count > 0 ? PyTime(DateTime.Now, S(a[0])) : DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
        F("time", "date.today|today", "date.today()", "Сегодняшняя дата ГГГГ-ММ-ДД.", _ => DateTime.Today.ToString("yyyy-MM-dd"));
        F("time", "datetime.utcnow|utcnow", "datetime.utcnow()", "Сейчас по UTC.", _ => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        F("time", "time.strftime|strftime|datetime.strftime|std::put_time", "strftime(fmt, date?)", "Дата по формату (%Y %m %d %H %M %S %A %B…).", a => PyTime(Dt(A(a, 1)), S(a[0])));
        F("time", "datetime.fromtimestamp|fromtimestamp", "fromtimestamp(sec)", "Дата из секунд.", a => Dt(D(a[0])).ToString("yyyy-MM-ddTHH:mm:ss"));
        F("time", "datetime.timestamp|to_timestamp", "to_timestamp(date)", "Секунды с 1970 года.", a => new DateTimeOffset(Dt(a[0])).ToUnixTimeMilliseconds() / 1000.0);
        F("time", "datetime.add|date_add|timedelta", "date_add(date, days=0, hours=0, minutes=0)", "Сдвинуть дату.", a => Dt(a[0]).AddDays(D(A(a, 1, 0.0))).AddHours(D(A(a, 2, 0.0))).AddMinutes(D(A(a, 3, 0.0))).ToString("yyyy-MM-ddTHH:mm:ss"));
        F("time", "datetime.diff|date_diff|days_between", "date_diff(a, b)", "Разница в днях (b − a).", a => (Dt(a[1]) - Dt(a[0])).TotalDays);
        F("time", "datetime.weekday|weekday", "weekday(date?)", "День недели: 0 — понедельник.", a => (double)(((int)Dt(A(a, 0)).DayOfWeek + 6) % 7));
        F("time", "datetime.year|year", "year(date?)", "Год.", a => (double)Dt(A(a, 0)).Year);
        F("time", "datetime.month|month", "month(date?)", "Месяц.", a => (double)Dt(A(a, 0)).Month);
        F("time", "datetime.day|day", "day(date?)", "День месяца.", a => (double)Dt(A(a, 0)).Day);
        F("time", "datetime.hour|hour", "hour(date?)", "Час.", a => (double)Dt(A(a, 0)).Hour);
        F("time", "calendar.isleap|isleap", "isleap(year)", "Високосный год?", a => DateTime.IsLeapYear(Int(a[0])));
        F("time", "calendar.monthrange|days_in_month", "days_in_month(year, month)", "Дней в месяце.", a => (double)DateTime.DaysInMonth(Int(a[0]), Int(a[1])));
        F("time", "time.format_duration|format_duration|humanize_seconds", "format_duration(sec)", "1ч 05м 03с.", a => { var t = TimeSpan.FromSeconds(D(a[0])); return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" : $"{t.TotalSeconds:0.#}s"; });

        // ---- пути (os.path, pathlib) — только строки, к диску скрипт не ходит
        F("os.path", "os.path.join|path.join|join_path|std::filesystem::path", "os.path.join(a, b, …)", "Склеить путь через /.", a => string.Join("/", a.Select(x => S(x).Replace('\\', '/').Trim('/')).Where(x => x != "")));
        F("os.path", "os.path.basename|basename|path.name", "basename(p)", "Имя файла.", a => S(a[0]).Replace('\\', '/').Split('/')[^1]);
        F("os.path", "os.path.dirname|dirname|path.parent", "dirname(p)", "Папка.", a => S(a[0]).Replace('\\', '/') is var p && p.LastIndexOf('/') is var i && i > 0 ? p[..i] : "");
        F("os.path", "os.path.splitext|splitext", "splitext(p)", "[путь без расширения, расширение].", a => { var p = S(a[0]); var e = Path.GetExtension(p); return new List<object?> { p[..^e.Length], e }; });
        F("os.path", "path.ext|extension|path.suffix", "extension(p)", "Расширение (.dll).", a => Path.GetExtension(S(a[0])));
        F("os.path", "path.stem|stem", "stem(p)", "Имя без расширения.", a => Path.GetFileNameWithoutExtension(S(a[0])));
        F("os.path", "os.path.normpath|normpath", "normpath(p)", "Убрать ./ и ../", a => { var st = new Stack<string>(); foreach (var part in S(a[0]).Replace('\\', '/').Split('/')) { if (part is "" or ".") continue; if (part == ".." && st.Count > 0) st.Pop(); else st.Push(part); } return string.Join("/", st.Reverse()); });
        F("os.path", "os.path.isabs|isabs", "isabs(p)", "Абсолютный ли путь.", a => Path.IsPathRooted(S(a[0])));
        F("os.path", "fnmatch.fnmatch|fnmatch|glob_match", "fnmatch(name, \"*.dll\")", "Подходит ли имя под шаблон * ?.", a => Regex.IsMatch(S(a[0]), "^" + Regex.Escape(S(a[1])).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase));

        // ---- хеши, кодировки, url
        F("hashlib", "hashlib.md5|md5", "md5(s)", "MD5 (hex).", a => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(S(a[0])))).ToLowerInvariant());
        F("hashlib", "hashlib.sha1|sha1", "sha1(s)", "SHA-1 (hex).", a => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(S(a[0])))).ToLowerInvariant());
        F("hashlib", "hashlib.sha256|sha256", "sha256(s)", "SHA-256 (hex).", a => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(S(a[0])))).ToLowerInvariant());
        F("hashlib", "hashlib.sha512|sha512", "sha512(s)", "SHA-512 (hex).", a => Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(S(a[0])))).ToLowerInvariant());
        F("hashlib", "hmac.sha256|hmac", "hmac(key, s)", "HMAC-SHA256 (hex).", a => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(S(a[0])), Encoding.UTF8.GetBytes(S(a[1])))).ToLowerInvariant());
        F("hashlib", "zlib.crc32|crc32", "crc32(s)", "CRC-32.", a => { uint c = 0xFFFFFFFF; foreach (var x in Encoding.UTF8.GetBytes(S(a[0]))) { c ^= x; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1; } return (double)~c; });
        F("base64", "base64.b64encode|b64encode|btoa", "b64encode(s)", "В Base64.", a => Convert.ToBase64String(Encoding.UTF8.GetBytes(S(a[0]))));
        F("base64", "base64.b64decode|b64decode|atob", "b64decode(s)", "Из Base64.", a => Encoding.UTF8.GetString(Convert.FromBase64String(S(a[0]))));
        F("base64", "base64.urlsafe_b64encode|urlsafe_b64encode", "urlsafe_b64encode(s)", "Base64 для ссылок.", a => Convert.ToBase64String(Encoding.UTF8.GetBytes(S(a[0]))).Replace('+', '-').Replace('/', '_'));
        F("base64", "binascii.hexlify|hexlify|to_hex", "hexlify(s)", "Байты строки в hex.", a => Convert.ToHexString(Encoding.UTF8.GetBytes(S(a[0]))).ToLowerInvariant());
        F("base64", "binascii.unhexlify|unhexlify|from_hex", "unhexlify(hex)", "Строка из hex.", a => Encoding.UTF8.GetString(Convert.FromHexString(S(a[0]))));
        F("urllib", "urllib.parse.quote|quote|url_encode|encodeURIComponent", "quote(s)", "Кодировать для ссылки.", a => Uri.EscapeDataString(S(a[0])));
        F("urllib", "urllib.parse.unquote|unquote|url_decode|decodeURIComponent", "unquote(s)", "Раскодировать из ссылки.", a => Uri.UnescapeDataString(S(a[0])));
        F("urllib", "urllib.parse.urlencode|urlencode", "urlencode(dict)", "a=1&b=2.", a => string.Join("&", M(a[0]).Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(ToText(kv.Value)))));
        F("urllib", "urllib.parse.urlparse|urlparse", "urlparse(url)", "Части ссылки словарём.", a => { var u = new Uri(S(a[0])); return new Dictionary<string, object?> { ["scheme"] = u.Scheme, ["netloc"] = u.Authority, ["path"] = u.AbsolutePath, ["query"] = u.Query.TrimStart('?'), ["fragment"] = u.Fragment.TrimStart('#') }; });
        F("html", "html.escape|escape_html", "html.escape(s)", "&lt; &gt; &amp;.", a => System.Net.WebUtility.HtmlEncode(S(a[0])));
        F("html", "html.unescape|unescape_html", "html.unescape(s)", "Обратно из HTML.", a => System.Net.WebUtility.HtmlDecode(S(a[0])));
        F("csv", "csv.parse|csv.reader|parse_csv", "csv.parse(text, sep=\",\")", "Таблица (список строк-списков).", a => S(a[0]).Replace("\r\n", "\n").Split('\n').Where(l => l != "").Select(l => (object?)Regex.Split(l, $"{Regex.Escape(S(A(a, 1, ",")))}(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)").Select(c => (object?)c.Trim().Trim('"')).ToList()).ToList());
        F("csv", "csv.write|csv.writer|to_csv", "csv.write(rows, sep=\",\")", "Текст CSV из списка строк.", a => string.Join("\n", L(a[0]).Select(r => string.Join(S(A(a, 1, ",")), L(r).Select(c => ToText(c) is var t && (t.Contains(',') || t.Contains('"')) ? "\"" + t.Replace("\"", "\"\"") + "\"" : ToText(c))))));
        F("ini", "configparser.parse|ini.parse|parse_ini", "ini.parse(text)", "INI в словарь {секция: {ключ: значение}}.", a =>
        {
            var r = new Dictionary<string, object?>(); var sec = new Dictionary<string, object?>(); r[""] = sec;
            foreach (var raw in S(a[0]).Replace("\r\n", "\n").Split('\n'))
            {
                var l = raw.Trim();
                if (l == "" || l[0] is ';' or '#') continue;
                if (l.StartsWith('[') && l.EndsWith(']')) { r[l[1..^1]] = sec = new Dictionary<string, object?>(); continue; }
                var eq = l.IndexOf('=');
                if (eq > 0) sec[l[..eq].Trim()] = l[(eq + 1)..].Trim();
            }
            if (((Dictionary<string, object?>)r[""]!).Count == 0) r.Remove("");
            return r;
        });
        F("version", "version.compare|semver.compare|compare_versions", "version.compare(a, b)", "−1, 0 или 1 для версий 1.2.10 и 1.10.0.", a => (double)Version.Parse(Regex.Replace(S(a[0]), @"[^\d.]", "") is var x && x.Count(c => c == '.') == 0 ? x + ".0" : x).CompareTo(Version.Parse(Regex.Replace(S(a[1]), @"[^\d.]", "") is var y && y.Count(c => c == '.') == 0 ? y + ".0" : y)));
        F("version", "version.bump|semver.bump|bump_version", "version.bump(\"1.2.3\", \"minor\")", "Поднять major/minor/patch.", a => { var p = S(a[0]).Split('.').Select(int.Parse).Concat([0, 0, 0]).Take(3).ToArray(); switch (S(A(a, 1, "patch"))) { case "major": p = [p[0] + 1, 0, 0]; break; case "minor": p = [p[0], p[1] + 1, 0]; break; default: p[2]++; break; } return string.Join(".", p); });
        F("version", "version.valid|semver.valid", "version.valid(s)", "Похоже ли на 1.0.0.", a => Regex.IsMatch(S(a[0]), @"^\d+\.\d+\.\d+([-+].*)?$"));
    }
}
