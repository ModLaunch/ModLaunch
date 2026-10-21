using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ModScriptX;

/// <summary>
/// Перевод нейтральных выражений .modx в синтаксис конкретного языка:
/// and/or/not, true/false/null, != и строки с подстановкой "{выражение}".
/// </summary>
public abstract class Lang
{
    public abstract string Id { get; }
    protected virtual string And => "and";
    protected virtual string Or => "or";
    protected virtual string Not => "not ";
    protected virtual string True => "true";
    protected virtual string False => "false";
    protected virtual string Null => "null";
    protected virtual string NotEq => "!=";
    protected virtual string Eq => "==";

    public static Lang For(string id) => id switch
    {
        "python" => new Python(),
        "javascript" => new JavaScript(),
        "lua" => new Lua(),
        "csharp" => new CSharp(),
        "gdscript" => new GdScript(),
        _ => throw new ArgumentException(id),
    };

    public string Expr(string src)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < src.Length)
        {
            var c = src[i];
            if (c == '"')
            {
                var (text, end) = ReadString(src, i);
                sb.Append(StringLit(text));
                i = end;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] is '_' or '.')) i++;
                var w = src[start..i];
                // 2.0: встроенные функции (len, rand, clamp…) — через mx_…, их определяет пролог каждого движка.
                var call = i < src.Length && src.AsSpan(i).TrimStart().StartsWith("(");
                if (call && ModxDoc.Builtins.Contains(w)) { sb.Append("mx_").Append(w); continue; }
                sb.Append(w switch { "and" => And, "or" => Or, "not" => Not, "true" => True, "false" => False, "null" => Null, _ => w });
                if (w == "not") while (i < src.Length && src[i] == ' ') i++;
            }
            else if (src.AsSpan(i).StartsWith("!=")) { sb.Append(NotEq); i += 2; }
            else if (src.AsSpan(i).StartsWith("==")) { sb.Append(Eq); i += 2; }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    static (string Text, int End) ReadString(string s, int i)
    {
        var sb = new StringBuilder();
        i++;
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                sb.Append(s[i + 1] switch { 'n' => '\n', 't' => '\t', var o => o });
                i += 2;
                continue;
            }
            sb.Append(s[i++]);
        }
        return (sb.ToString(), Math.Min(i + 1, s.Length));
    }

    /// <summary>Строка .modx: куски текста и выражения из {…}; {{ и }} — сами скобки.</summary>
    protected static List<(bool IsExpr, string Text)> Split(string text)
    {
        var parts = new List<(bool, string)>();
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' && i + 1 < text.Length && text[i + 1] == '{') { sb.Append('{'); i++; continue; }
            if (text[i] == '}' && i + 1 < text.Length && text[i + 1] == '}') { sb.Append('}'); i++; continue; }
            if (text[i] == '{')
            {
                var close = text.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(text[i..]); break; }
                if (sb.Length > 0) { parts.Add((false, sb.ToString())); sb.Clear(); }
                parts.Add((true, text[(i + 1)..close].Trim()));
                i = close;
                continue;
            }
            sb.Append(text[i]);
        }
        if (sb.Length > 0 || parts.Count == 0) parts.Add((false, sb.ToString()));
        return parts;
    }

    protected static string Escape(string s, string extra = "")
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            sb.Append(c switch
            {
                '\\' => "\\\\", '"' => "\\\"", '\n' => "\\n", '\t' => "\\t", '\r' => "\\r",
                _ when extra.Contains(c) => "\\" + c,
                _ => c.ToString(),
            });
        return sb.ToString();
    }

    protected static string Plain(string s) => "\"" + Escape(s) + "\"";

    protected abstract string StringLit(string text);

    /// <summary>Ключевые слова языка: переменная, функция или параметр с таким именем не соберётся (base в C#, end в Lua).</summary>
    public abstract IReadOnlySet<string> Reserved { get; }

    /// <summary>Значение общей переменной на этом языке.</summary>
    public virtual string Literal(JsonNode? v) => v switch
    {
        null => Null,
        JsonArray a => "[" + string.Join(", ", a.Select(Literal)) + "]",
        JsonObject o => "{" + string.Join(", ", o.Select(kv => Plain(kv.Key) + ": " + Literal(kv.Value))) + "}",
        JsonValue val when val.TryGetValue<bool>(out var b) => b ? True : False,
        JsonValue val when val.TryGetValue<string>(out var s) => Plain(s),
        _ => v.ToJsonString(),
    };

    // ---------------------------------------------------------------- языки

    public sealed class Python : Lang
    {
        public override string Id => "python";
        // print и exec — ключевые слова Python 2, на нём Ren'Py 7.
        public override IReadOnlySet<string> Reserved => Words;
        static readonly HashSet<string> Words = new(["False", "None", "True", "and", "as", "assert", "async", "await", "break",
            "class", "continue", "def", "del", "elif", "else", "except", "exec", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda",
            "nonlocal", "not", "or", "pass", "print", "raise", "return", "try", "while", "with", "yield"]);
        protected override string True => "True";
        protected override string False => "False";
        protected override string Null => "None";

        // .format вместо f-строк: работает и в Ren'Py 7 (Python 2), и в Ren'Py 8.
        protected override string StringLit(string text)
        {
            var parts = Split(text);
            if (parts.All(p => !p.IsExpr)) return Plain(text);
            var fmt = string.Concat(parts.Select(p => p.IsExpr ? "{}" : p.Text.Replace("{", "{{").Replace("}", "}}")));
            return Plain(fmt) + ".format(" + string.Join(", ", parts.Where(p => p.IsExpr).Select(p => Expr(p.Text))) + ")";
        }
    }

    public sealed class JavaScript : Lang
    {
        public override string Id => "javascript";
        // Плагин RPG Maker идёт в "use strict": там запрещены ещё и arguments, eval, let, static, interface…
        public override IReadOnlySet<string> Reserved => Words;
        static readonly HashSet<string> Words = new(["arguments", "await", "break", "case", "catch", "class", "const",
            "continue", "debugger", "default", "delete", "do", "else", "enum", "eval", "export", "extends", "false", "finally", "for", "function", "if",
            "implements", "import", "in", "instanceof", "interface", "let", "new", "null", "package", "private", "protected", "public", "return", "static",
            "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield"]);
        protected override string And => "&&";
        protected override string Or => "||";
        protected override string Not => "!";
        protected override string NotEq => "!==";
        protected override string Eq => "===";

        protected override string StringLit(string text)
        {
            var parts = Split(text);
            if (parts.All(p => !p.IsExpr)) return Plain(text);
            return "`" + string.Concat(parts.Select(p => p.IsExpr ? "${" + Expr(p.Text) + "}" : Escape(p.Text, "`$"))) + "`";
        }
    }

    public sealed class Lua : Lang
    {
        public override string Id => "lua";
        public override IReadOnlySet<string> Reserved => Words;
        static readonly HashSet<string> Words = new(["and", "break", "do", "else", "elseif", "end", "false", "for",
            "function", "goto", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"]);
        protected override string Null => "nil";
        protected override string NotEq => "~=";

        protected override string StringLit(string text)
        {
            var parts = Split(text);
            if (parts.All(p => !p.IsExpr)) return Plain(text);
            return "(" + string.Join(" .. ", parts.Select(p => p.IsExpr ? "tostring(" + Expr(p.Text) + ")" : Plain(p.Text))) + ")";
        }

        public override string Literal(JsonNode? v) => v switch
        {
            JsonArray a => "{" + string.Join(", ", a.Select(Literal)) + "}",
            JsonObject o => "{" + string.Join(", ", o.Select(kv => "[" + Plain(kv.Key) + "] = " + Literal(kv.Value))) + "}",
            _ => base.Literal(v),
        };
    }

    public sealed class CSharp : Lang
    {
        public override string Id => "csharp";
        public override IReadOnlySet<string> Reserved => Words;
        static readonly HashSet<string> Words = new(["abstract", "as", "base", "bool", "break", "byte", "case", "catch",
            "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
            "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
            "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return",
            "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
            "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"]);
        protected override string And => "&&";
        protected override string Or => "||";
        protected override string Not => "!";

        protected override string StringLit(string text)
        {
            var parts = Split(text);
            if (parts.All(p => !p.IsExpr)) return Plain(text);
            return "$\"" + string.Concat(parts.Select(p => p.IsExpr ? "{" + Expr(p.Text) + "}" : Escape(p.Text).Replace("{", "{{").Replace("}", "}}"))) + "\"";
        }

        /// <summary>Тип поля для общей переменной: C# строго типизирован.</summary>
        public static string TypeOf(JsonNode? v) => v switch
        {
            JsonArray => "System.Collections.Generic.List<object>",
            JsonObject => "System.Collections.Generic.Dictionary<string, object>",
            JsonValue val when val.TryGetValue<bool>(out _) => "bool",
            JsonValue val when val.TryGetValue<long>(out _) => "long",
            JsonValue val when val.TryGetValue<double>(out _) => "double",
            JsonValue val when val.TryGetValue<string>(out _) => "string",
            _ => "object",
        };

        public override string Literal(JsonNode? v) => v switch
        {
            JsonArray a => "new System.Collections.Generic.List<object> { " + string.Join(", ", a.Select(Literal)) + " }",
            JsonObject o => "new System.Collections.Generic.Dictionary<string, object> { " + string.Join(", ", o.Select(kv => "[" + Plain(kv.Key) + "] = " + Literal(kv.Value))) + " }",
            JsonValue val when val.TryGetValue<double>(out var d) && !val.TryGetValue<long>(out _) => d.ToString("R", CultureInfo.InvariantCulture) + "d",
            _ => base.Literal(v),
        };
    }

    public sealed class GdScript : Lang
    {
        public override string Id => "gdscript";
        public override IReadOnlySet<string> Reserved => Words;
        static readonly HashSet<string> Words = new(["and", "as", "assert", "await", "break", "breakpoint", "class",
            "class_name", "const", "continue", "elif", "else", "enum", "extends", "false", "for", "func", "if", "in", "is", "match", "namespace", "not",
            "null", "or", "pass", "preload", "return", "self", "signal", "static", "super", "trait", "true", "var", "void", "while", "yield",
            "PI", "TAU", "INF", "NAN"]);

        protected override string StringLit(string text)
        {
            var parts = Split(text);
            if (parts.All(p => !p.IsExpr)) return Plain(text);
            var fmt = string.Concat(parts.Select(p => p.IsExpr ? "%s" : p.Text.Replace("%", "%%")));
            return Plain(fmt) + " % [" + string.Join(", ", parts.Where(p => p.IsExpr).Select(p => Expr(p.Text))) + "]";
        }
    }
}
