using System.Text;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using MX = ModScriptX;

namespace ModLaunch.Creator;

/// <summary>
/// 19.0: понимание кода ModScript X — то, чем среда отличается от редактора текста. Знает, где что объявлено
/// (общие переменные, функции, параметры, переменные хуков и циклов) и к чему относится каждое слово; отсюда
/// переход к определению, все ссылки, переименование, подсказки по месту, справка под курсором, подсказка
/// аргументов, сворачивание блоков, выравнивание кода и проверка того, что разбор пропускает (опечатки,
/// лишние переменные, не то число аргументов, код после return). Всё по тексту, за миллисекунды, без игры.
/// Строки считаются с 1, столбцы — с 0.
/// </summary>
public static partial class ModxInsight
{
    public enum Kind { Shared, Function, Param, Local, Loop, Game, Builtin, Keyword, Event }

    /// <summary>Объявление: имя, вид, где, чьё (функция или хук; у общих и функций — пусто) и строка объявления.</summary>
    public sealed record Symbol(string Name, Kind Kind, int Line, int Col, string Scope, string Detail)
    {
        public string Key => Kind is Kind.Param or Kind.Local or Kind.Loop ? Scope + ":" + Name : Kind + ":" + Name;
        public bool Renamable => Kind is Kind.Shared or Kind.Function or Kind.Param or Kind.Local or Kind.Loop;
    }

    /// <summary>Слово в коде: где стоит, в чьём теле, пишется ли в него и вызывается ли оно.</summary>
    public sealed record Ref(int Line, int Col, string Name, string Scope, bool Write, bool Call);

    /// <summary>Находка проверки: место, текст, предупреждение ли и замена слова (быстрое исправление).</summary>
    public sealed record Issue(int Line, int Col, int Length, string Message, bool Warning, string? Fix = null);

    /// <summary>Подсказка набора: что показать, что вставить (${…} — выделить, $0 — курсор), пояснение и вес.</summary>
    public sealed record Item(string Label, string Insert, string Detail, Kind Kind, double Priority, bool Snippet = false);

    /// <summary>Разобранный текст: объявления, слова и чьё тело на каждой строке.</summary>
    public sealed class Model
    {
        public string[] Lines = [];
        public readonly List<Symbol> Symbols = [];
        public readonly List<Ref> Refs = [];
        /// <summary>Чьё тело на строке (индекс с 0): "" — верх файла, "fn:имя", "hook:событие:строка", "native".</summary>
        public string[] Scope = [];
        /// <summary>Строки, после которых в том же блоке код не выполнится (return, break, continue).</summary>
        public readonly List<int> Exits = [];
        public bool HasNatives;

        Dictionary<string, Symbol>? _index;
        Dictionary<string, List<Ref>>? _uses;

        /// <summary>Объявления по ключу (первое объявление выигрывает): L:тело:имя, F:имя, S:имя.</summary>
        internal Dictionary<string, Symbol> Index
        {
            get
            {
                if (_index is not null) return _index;
                var ix = new Dictionary<string, Symbol>(StringComparer.Ordinal);
                foreach (var s in Symbols)
                    ix.TryAdd(s.Kind switch { Kind.Function => "F:" + s.Name, Kind.Shared => "S:" + s.Name, _ => "L:" + s.Scope + ":" + s.Name }, s);
                return _index = ix;
            }
        }

        /// <summary>Слова кода по символу, к которому они относятся (ключ Symbol.Key) — ссылки за один проход.</summary>
        internal Dictionary<string, List<Ref>> Uses
        {
            get
            {
                if (_uses is not null) return _uses;
                var uses = new Dictionary<string, List<Ref>>(StringComparer.Ordinal);
                foreach (var r in Refs)
                {
                    var key = Resolve(this, r).Key;
                    if (!uses.TryGetValue(key, out var list)) uses[key] = list = [];
                    list.Add(r);
                }
                return _uses = uses;
            }
        }
    }

    static readonly HashSet<string> Keywords = new(MX.ModxDoc.Keywords.Concat(["var", "const", "elif"]), StringComparer.Ordinal);
    static readonly HashSet<string> Builtins = new(MX.ModxDoc.Builtins, StringComparer.Ordinal);
    static readonly HashSet<string> Headers = new(["mod", "version", "author", "about", "game", "engine", "use"], StringComparer.Ordinal);

    [GeneratedRegex(@"^(\s*)fn\s+([\p{L}_][\p{L}\w]*)\s*\(([^)]*)\)\s*\{\s*$")]
    private static partial Regex FnStart();

    [GeneratedRegex(@"^(\s*)(hook|native)\s+([\p{L}\w]+)\s*\{\s*$")]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^\s*(?:let|var|const)\s+([\p{L}_][\p{L}\w]*)\s*=")]
    private static partial Regex LetLine();

    [GeneratedRegex(@"^\s*for\s+([\p{L}_][\p{L}\w]*)\s+in\s")]
    private static partial Regex ForLine();

    [GeneratedRegex(@"^\s*(?:set\s+|add\s+)?([\p{L}_][\p{L}\w]*)[\p{L}\w.\[\]]*\s*(?:=(?!=)|\+=|-=|\*=|/=|\s+\S)")]
    private static partial Regex Assign();

    [GeneratedRegex(@"^[\p{L}_][\p{L}\w]*$")]
    private static partial Regex Ident();

    static int Indent(string line) => line.Length - line.TrimStart().Length;

    // ---------------------------------------------------------------- разбор

    public static Model Read(string text)
    {
        var m = new Model { Lines = text.Replace("\r\n", "\n").Split('\n') };
        var lines = m.Lines;
        m.Scope = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            m.Scope[i] = "";
            var f = FnStart().Match(lines[i]);
            var b = f.Success ? Match.Empty : BlockStart().Match(lines[i]);
            if (f.Success || b.Success)
            {
                var indent = (f.Success ? f : b).Groups[1].Value.Length;
                var end = i + 1;
                while (end < lines.Length && !(lines[end].Trim() == "}" && Indent(lines[end]) <= indent)) end++;
                string scope;
                if (f.Success)
                {
                    var name = f.Groups[2].Value;
                    scope = "fn:" + name;
                    var pars = f.Groups[3].Value;
                    m.Symbols.Add(new Symbol(name, Kind.Function, i + 1, f.Groups[2].Index, "", $"fn {name}({string.Join(", ", Split(pars))})"));
                    var at = f.Groups[3].Index;
                    foreach (var w in Words(pars))
                        if (!w.Member) m.Symbols.Add(new Symbol(w.Name, Kind.Param, i + 1, at + w.Col, scope, $"{name}({string.Join(", ", Split(pars))})"));
                }
                else if (b.Groups[2].Value == "native") { scope = "native"; m.HasNatives = true; }
                else scope = $"hook:{b.Groups[3].Value}:{i + 1}";
                for (var k = i + 1; k < Math.Min(end + 1, lines.Length); k++) m.Scope[k] = scope;
                if (scope != "native") Body(m, i + 1, Math.Min(end, lines.Length), scope);
                i = end;
                continue;
            }
            var t = lines[i].TrimStart();
            var head = Regex.Match(t, @"^[a-z]+").Value;
            if (Headers.Contains(head) && !LetLine().IsMatch(t)) continue;
            var l = LetLine().Match(lines[i]);
            if (l.Success && Indent(lines[i]) == 0)
            {
                var name = l.Groups[1].Value;
                if (!m.Symbols.Any(s => s.Kind == Kind.Shared && s.Name == name))
                    m.Symbols.Add(new Symbol(name, Kind.Shared, i + 1, l.Groups[1].Index, "", Code(lines[i]).Trim()));
                foreach (var w in Words(lines[i]))
                    if (!w.Member && !Keywords.Contains(w.Name) && !(w.Col == l.Groups[1].Index))
                        m.Refs.Add(new Ref(i + 1, w.Col, w.Name, "", false, w.Call));
                m.Refs.Add(new Ref(i + 1, l.Groups[1].Index, name, "", true, false));
            }
        }
        return m;
    }

    /// <summary>Тело хука или функции: объявления let и for, запись в переменные, остальные слова — чтение и вызовы.</summary>
    static void Body(Model m, int from, int to, string scope)
    {
        for (var i = from; i < to; i++)
        {
            var line = m.Lines[i];
            var code = Code(line);
            var t = code.Trim();
            if (t == "") continue;
            if (t is "break" or "continue" || t.StartsWith("return", StringComparison.Ordinal) && (t.Length == 6 || !char.IsLetterOrDigit(t[6]) && t[6] != '_')) m.Exits.Add(i + 1);
            int? decl = null, write = null;
            if (LetLine().Match(code) is { Success: true } l)
            {
                decl = l.Groups[1].Index;
                if (!m.Symbols.Any(s => s.Kind == Kind.Local && s.Scope == scope && s.Name == l.Groups[1].Value)
                    && !m.Symbols.Any(s => s.Kind == Kind.Param && s.Scope == scope && s.Name == l.Groups[1].Value))
                    m.Symbols.Add(new Symbol(l.Groups[1].Value, Kind.Local, i + 1, decl.Value, scope, t));
            }
            else if (ForLine().Match(code) is { Success: true } fl)
            {
                decl = fl.Groups[1].Index;
                if (!m.Symbols.Any(s => s.Kind == Kind.Loop && s.Scope == scope && s.Name == fl.Groups[1].Value))
                    m.Symbols.Add(new Symbol(fl.Groups[1].Value, Kind.Loop, i + 1, decl.Value, scope, t.TrimEnd('{', ' ')));
            }
            else if (!t.StartsWith('}') && Assign().Match(code) is { Success: true } a && !Keywords.Contains(a.Groups[1].Value) && a.Groups[1].Value is not ("say" or "log" or "call" or "if" or "while" or "return"))
                write = a.Groups[1].Index;
            foreach (var w in Words(line))
            {
                if (w.Member || Keywords.Contains(w.Name)) continue;
                var isDecl = w.Col == decl;
                m.Refs.Add(new Ref(i + 1, w.Col, w.Name, scope, isDecl || w.Col == write, w.Call));
            }
        }
    }

    static string[] Split(string pars) => pars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Строка без комментария (# вне кавычек).</summary>
    public static string Code(string line)
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

    /// <summary>
    /// Слова строки: имена вне кавычек и комментариев и внутри подстановок "{…}" в строках. Member — после точки
    /// (player.gold: gold — поле), Call — за словом идёт скобка.
    /// </summary>
    public static IEnumerable<(int Col, string Name, bool Member, bool Call, bool InString)> Words(string line)
    {
        var inStr = false;
        var interp = 0;
        for (var i = 0; i < line.Length;)
        {
            var c = line[i];
            if (inStr && interp == 0)
            {
                if (c == '\\') { i += 2; continue; }
                if (c == '"') inStr = false;
                else if (c == '{')
                {
                    if (i + 1 < line.Length && line[i + 1] == '{') { i += 2; continue; }
                    interp = 1;
                }
                i++;
                continue;
            }
            if (inStr && c == '}') { interp = 0; i++; continue; }
            if (!inStr && c == '#') yield break;
            if (c == '"' && !inStr) { inStr = true; i++; continue; }
            if (char.IsDigit(c))
            {
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1]))) i++;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                var p = start - 1;
                while (p >= 0 && line[p] == ' ') p--;
                var member = p >= 0 && line[p] == '.' && !(p > 0 && line[p - 1] == '.');
                var n = i;
                while (n < line.Length && line[n] == ' ') n++;
                yield return (start, line[start..i], member, n < line.Length && line[n] == '(', inStr);
                continue;
            }
            i++;
        }
    }

    // ---------------------------------------------------------------- определения и ссылки

    /// <summary>К чему относится слово: своё объявление, иначе встроенное слово или переменная игры.</summary>
    public static Symbol Resolve(Model m, Ref r)
    {
        var ix = m.Index;
        if (r.Scope != "" && ix.TryGetValue("L:" + r.Scope + ":" + r.Name, out var local)) return local;
        if (r.Call && ix.TryGetValue("F:" + r.Name, out var fn)) return fn;
        if (ix.TryGetValue("S:" + r.Name, out var shared)) return shared;
        if (Builtins.Contains(r.Name)) return new Symbol(r.Name, Kind.Builtin, 0, 0, "", r.Name);
        if (ix.TryGetValue("F:" + r.Name, out var named)) return named;
        return new Symbol(r.Name, Kind.Game, 0, 0, "", r.Name);
    }

    /// <summary>Что стоит в этом месте: объявление, слово кода, слово языка или событие хука.</summary>
    public static Symbol? At(Model m, int line, int col)
    {
        if (m.Symbols.FirstOrDefault(s => s.Line == line && col >= s.Col && col <= s.Col + s.Name.Length) is { } decl) return decl;
        if (m.Refs.FirstOrDefault(r => r.Line == line && col >= r.Col && col <= r.Col + r.Name.Length) is { } r) return Resolve(m, r);
        if (line < 1 || line > m.Lines.Length) return null;
        var text = m.Lines[line - 1];
        foreach (var w in Words(text))
            if (col >= w.Col && col <= w.Col + w.Name.Length)
            {
                if (Regex.IsMatch(text, @"^\s*hook\s+" + Regex.Escape(w.Name) + @"\b") && MX.ModxDoc.Events.Contains(w.Name)) return new Symbol(w.Name, Kind.Event, 0, 0, "", w.Name);
                if (Keywords.Contains(w.Name) || Headers.Contains(w.Name)) return new Symbol(w.Name, Kind.Keyword, 0, 0, "", w.Name);
            }
        return null;
    }

    /// <summary>Все места символа: объявление и каждое слово, которое к нему относится.</summary>
    public static List<(int Line, int Col, int Length, bool Write)> References(Model m, Symbol s)
    {
        var list = new List<(int, int, int, bool)>();
        if (s.Line > 0) list.Add((s.Line, s.Col, s.Name.Length, true));
        foreach (var r in m.Uses.GetValueOrDefault(s.Key) ?? [])
            if (!(r.Line == s.Line && r.Col == s.Col)) list.Add((r.Line, r.Col, r.Name.Length, r.Write));
        return [.. list.OrderBy(x => x.Item1).ThenBy(x => x.Item2)];
    }

    /// <summary>
    /// Переименовать символ во всём файле (и в подстановках "{имя}" в строках). Новое имя не должно быть словом
    /// языка или уже занятым: иначе код молча поменял бы смысл. Возвращает новый текст или причину отказа.
    /// </summary>
    public static (string? Text, string? Error) Rename(string text, Symbol s, string name)
    {
        name = name.Trim();
        if (!s.Renamable) return (null, "v19.rename.fixed");
        if (!Ident().IsMatch(name)) return (null, "v19.rename.bad");
        if (name == s.Name) return (text, null);
        if (Keywords.Contains(name) || Builtins.Contains(name) || Headers.Contains(name) || MX.ModxDoc.Events.Contains(name)) return (null, "v19.rename.reserved");
        var m = Read(text);
        var taken = s.Kind is Kind.Param or Kind.Local or Kind.Loop
            ? m.Refs.Any(r => r.Name == name && (r.Scope == s.Scope || Resolve(m, r).Kind is Kind.Shared or Kind.Function)) || m.Symbols.Any(x => x.Name == name && (x.Scope == s.Scope || x.Scope == ""))
            : m.Refs.Any(r => r.Name == name) || m.Symbols.Any(x => x.Name == name);
        if (taken) return (null, "v19.rename.taken");
        var spots = References(m, s);
        var lines = (string[])m.Lines.Clone();
        foreach (var g in spots.GroupBy(x => x.Line))
        {
            var line = lines[g.Key - 1];
            foreach (var (_, col, len, _) in g.OrderByDescending(x => x.Col)) line = line[..col] + name + line[(col + len)..];
            lines[g.Key - 1] = line;
        }
        var result = string.Join('\n', lines);
        var before = MX.ModxDoc.Parse(text);
        var after = MX.ModxDoc.Parse(result);
        if (after.Diags.Count(d => !d.Warning) > before.Diags.Count(d => !d.Warning) || after.Hooks.Count != before.Hooks.Count || after.Functions.Count != before.Functions.Count)
            return (null, "v19.rename.broke");
        return (result, null);
    }

    // ---------------------------------------------------------------- справка

    /// <summary>Подпись и пояснение встроенной функции: «clamp(x, lo, hi)» и «— число в пределах…».</summary>
    public static (string Sig, string Doc) BuiltinDoc(string name)
    {
        var key = $"v15.ms.fn.{name}";
        var text = I18n.Has(key) ? I18n.T(key) : name + "()";
        var dash = text.IndexOf(" — ", StringComparison.Ordinal);
        if (dash < 0) dash = text.IndexOf(": ", StringComparison.Ordinal);
        return dash > 0 ? (text[..dash].Trim(), text[(dash + 2)..].Trim().TrimStart('—', ' ')) : (name + "()", text);
    }

    /// <summary>Комментарии сразу над строкой объявления — его описание.</summary>
    static string DocAbove(Model m, int line)
    {
        var doc = new List<string>();
        for (var i = line - 2; i >= 0; i--)
        {
            var t = m.Lines[i].Trim();
            if (!t.StartsWith('#')) break;
            doc.Insert(0, t.TrimStart('#').Trim());
        }
        return string.Join(" ", doc).Trim();
    }

    /// <summary>Справка под курсором: заголовок (подпись или объявление) и текст.</summary>
    public static (string Title, string Text)? Hover(Model m, int line, int col)
    {
        if (At(m, line, col) is not { } s) return null;
        int Uses() => References(m, s).Count(x => !(x.Line == s.Line && x.Col == s.Col));
        switch (s.Kind)
        {
            case Kind.Builtin:
            {
                var (sig, doc) = BuiltinDoc(s.Name);
                return (sig, doc + "\n" + I18n.T("v19.hover.builtin"));
            }
            case Kind.Keyword:
                return (s.Name, I18n.Has($"v15.ms.kw.{s.Name}") ? I18n.T($"v15.ms.kw.{s.Name}") : I18n.T("v19.hover.keyword"));
            case Kind.Event:
                return ("hook " + s.Name, I18n.T($"v19.event.{s.Name}"));
            case Kind.Function:
            {
                var doc = DocAbove(m, s.Line);
                return (s.Detail, (doc == "" ? "" : doc + "\n") + I18n.T("v19.hover.fn", ("line", s.Line), ("n", Uses())));
            }
            case Kind.Shared:
            {
                var doc = DocAbove(m, s.Line);
                return (s.Detail, (doc == "" ? "" : doc + "\n") + I18n.T("v19.hover.shared", ("line", s.Line), ("n", Uses())));
            }
            case Kind.Param: return (s.Name, I18n.T("v19.hover.param", ("fn", s.Detail)));
            case Kind.Local: return (s.Detail, I18n.T("v19.hover.local", ("where", Where(s.Scope)), ("line", s.Line), ("n", Uses())));
            case Kind.Loop: return (s.Detail, I18n.T("v19.hover.loop", ("where", Where(s.Scope))));
            default: return (s.Name, I18n.T("v19.hover.game", ("n", Uses())));
        }
    }

    /// <summary>«hook tick», «fn award» — для людей.</summary>
    public static string Where(string scope) => scope.Split(':') switch
    {
        ["fn", var n] => $"fn {n}",
        ["hook", var e, _] => $"hook {e}",
        _ => scope,
    };

    /// <summary>Подсказка аргументов: внутри скобок вызова — функция, её параметры и номер того, что набирается.</summary>
    public static (string Name, string[] Params, int Active, string Doc)? Signature(Model m, string line, int col)
    {
        col = Math.Min(col, line.Length);
        var depth = 0;
        var commas = 0;
        var inStr = line[..col].Count(c => c == '"') % 2 == 1;
        if (inStr) return null;
        for (var i = col - 1; i >= 0; i--)
        {
            var c = line[i];
            if (c == '"') { do i--; while (i >= 0 && line[i] != '"'); continue; }
            if (c == ')' || c == ']') depth++;
            else if (c == '[') { if (depth == 0) return null; depth--; }
            else if (c == '(')
            {
                if (depth > 0) { depth--; continue; }
                var end = i;
                while (end > 0 && line[end - 1] == ' ') end--;
                var start = end;
                while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_')) start--;
                if (start == end) return null;
                var name = line[start..end];
                if (m.Symbols.FirstOrDefault(s => s.Kind == Kind.Function && s.Name == name) is { } fn)
                {
                    var pars = Regex.Match(fn.Detail, @"\((.*)\)").Groups[1].Value;
                    return (name, Split(pars), commas, DocAbove(m, fn.Line));
                }
                if (Builtins.Contains(name))
                {
                    var (sig, doc) = BuiltinDoc(name);
                    return (name, Split(Regex.Match(sig, @"\((.*)\)").Groups[1].Value), commas, doc);
                }
                return null;
            }
            else if (c == ',' && depth == 0) commas++;
        }
        return null;
    }

    // ---------------------------------------------------------------- подсказки набора

    static readonly (string Label, string Insert, string Key)[] Snippets =
    [
        ("hook", "hook ${start} {\n    \n}", "v19.snip.hook"),
        ("fn", "fn ${name}() {\n    \n}", "v19.snip.fn"),
        ("if", "if ${condition} {\n    \n}", "v19.snip.if"),
        ("ifelse", "if ${condition} {\n    \n} else {\n    \n}", "v19.snip.ifelse"),
        ("for", "for ${i} in 1..10 {\n    \n}", "v19.snip.for"),
        ("foreach", "for ${item} in items {\n    \n}", "v19.snip.foreach"),
        ("while", "while ${condition} {\n    \n}", "v19.snip.while"),
        ("native", "native ${csharp} {\n    \n}", "v19.snip.native"),
        ("say", "say \"${text}\"", "v19.snip.say"),
    ];

    static readonly string[] Statements = ["let", "set", "add", "say", "log", "call", "if", "while", "for", "return", "break", "continue"];

    /// <summary>Что предложить в этом месте: после hook — события, после native — языки, после engine — движки, в коде — по смыслу.</summary>
    public static List<Item> Complete(Model m, int line, int col)
    {
        var text = line >= 1 && line <= m.Lines.Length ? m.Lines[line - 1] : "";
        var before = text[..Math.Min(col, text.Length)];
        var items = new List<Item>();
        if (before.TrimStart().StartsWith('#')) return items;
        var quotes = Code(before).Count(c => c == '"');
        var inString = Code(before).Length < before.Length || quotes % 2 == 1;
        var scope = line >= 1 && line <= m.Scope.Length ? m.Scope[line - 1] : "";
        if (scope == "native") return items;
        if (Regex.IsMatch(before, @"^\s*hook\s+\w*$"))
            return [.. MX.ModxDoc.Events.Select(e => new Item(e, e, I18n.T($"v19.event.{e}"), Kind.Event, 5))];
        if (Regex.IsMatch(before, @"^\s*native\s+\w*$"))
            return [.. MX.ModxDoc.Languages.Select(l => new Item(l, l, I18n.T("v19.native.lang"), Kind.Keyword, 5))];
        // 21.0: после use " — кусочки по названию (купленные и бесплатные).
        if (Regex.IsMatch(before, @"^use\s+""[^""]*$"))
            return [.. Pieces.All().Select(p => new Item(p.Title, p.Title + "\"", p.Free ? I18n.T("v21.piece.free") + " · " + p.About : I18n.T("v21.piece.bought", ("seller", p.Seller)), Kind.Function, 6))];
        if (Regex.IsMatch(before, @"^\s*engine\s+[\w-]*$"))
            return [.. MX.Target.All.Select(t => new Item(t.Id, t.Id, $"{t.Title} · {t.Language}", Kind.Keyword, 5))];
        if (inString)
        {
            // В строке — только подстановка "{имя}".
            var open = before.LastIndexOf('{');
            if (open < 0 || before.LastIndexOf('}') > open || before.LastIndexOf('"') > open) return items;
        }
        else if (Regex.IsMatch(before, @"\.\s*[\p{L}\w]*$") && !Regex.IsMatch(before, @"\.\.\s*[\p{L}\w]*$")) return items;
        var statement = !inString && Regex.IsMatch(before, @"^\s*[\p{L}\w]*$");
        var top = scope == "";

        foreach (var s in m.Symbols.Where(s => s.Kind is Kind.Param or Kind.Local or Kind.Loop && s.Scope == scope && scope != ""))
            items.Add(new Item(s.Name, s.Name, s.Kind == Kind.Param ? I18n.T("v19.kind.param") : s.Detail, s.Kind, 6));
        foreach (var s in m.Symbols.Where(s => s.Kind == Kind.Shared))
            items.Add(new Item(s.Name, s.Name, s.Detail, Kind.Shared, 5));
        foreach (var g in m.Refs.Where(r => !r.Call).Select(r => Resolve(m, r)).Where(s => s.Kind == Kind.Game).Select(s => s.Name).Distinct())
            items.Add(new Item(g, g, I18n.T("v19.kind.game"), Kind.Game, 3));
        if (!inString)
        {
            foreach (var s in m.Symbols.Where(s => s.Kind == Kind.Function))
                items.Add(new Item(s.Name, s.Name + "($0)", s.Detail, Kind.Function, 4));
            foreach (var b in MX.ModxDoc.Builtins)
            {
                var (sig, doc) = BuiltinDoc(b);
                items.Add(new Item(b, b + "($0)", sig + " — " + doc, Kind.Builtin, 2));
            }
            if (statement)
            {
                foreach (var (label, insert, key) in Snippets)
                    if (top ? label is "hook" or "fn" or "native" : label is not ("hook" or "fn" or "native"))
                        items.Add(new Item(label, insert, I18n.T(key), Kind.Keyword, top ? 7 : 3, true));
                foreach (var k in top ? ["let", "mod", "version", "author", "about", "engine"] : Statements)
                    items.Add(new Item(k, k + " ", I18n.Has($"v15.ms.kw.{k}") ? I18n.T($"v15.ms.kw.{k}") : "", Kind.Keyword, 1));
            }
            else
                foreach (var k in new[] { "and", "or", "not", "true", "false", "null", "in" })
                    items.Add(new Item(k, k + " ", I18n.Has($"v15.ms.kw.{k}") ? I18n.T($"v15.ms.kw.{k}") : "", Kind.Keyword, 1));
        }
        return [.. items.DistinctBy(i => (i.Label, i.Snippet))];
    }

    // ---------------------------------------------------------------- проверка

    /// <summary>Расстояние правки (Левенштейн) — для «может, вы имели в виду».</summary>
    static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 99;
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var keep = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = keep;
            }
        }
        return d[b.Length];
    }

    static string? Closest(string name, IEnumerable<string> known) =>
        known.Where(k => k != name).Select(k => (k, d: Distance(name.ToLowerInvariant(), k.ToLowerInvariant()))).Where(x => x.d <= (name.Length > 5 ? 2 : 1))
            .OrderBy(x => x.d).ThenByDescending(x => Prefix(name, x.k)).Select(x => x.k).FirstOrDefault();

    /// <summary>Сколько первых букв совпадает: rond — скорее round, чем rand.</summary>
    static int Prefix(string a, string b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && char.ToLowerInvariant(a[n]) == char.ToLowerInvariant(b[n])) n++;
        return n;
    }

    /// <summary>Сколько аргументов у вызова, который начинается со скобки в столбце open (−1 — скобка не закрыта).</summary>
    static int Args(string line, int open)
    {
        var depth = 0;
        var count = 0;
        var any = false;
        for (var i = open; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { do i++; while (i < line.Length && line[i] != '"'); any = true; continue; }
            if (c is '(' or '[' or '{') { if (depth++ > 0 || c != '(') any = true; continue; }
            if (c is ')' or ']' or '}')
            {
                if (--depth == 0) return any ? count + 1 : 0;
                continue;
            }
            if (c == ',' && depth == 1) count++;
            else if (!char.IsWhiteSpace(c)) any = true;
        }
        return -1;
    }

    /// <summary>То, что разбор пропускает: опечатки в именах и функциях, лишние переменные, аргументы, код после выхода.</summary>
    public static List<Issue> Lint(Model m)
    {
        var issues = new List<Issue>();
        var fns = m.Symbols.Where(s => s.Kind == Kind.Function).ToList();
        var declared = m.Symbols.Where(s => s.Kind is not Kind.Function).Select(s => s.Name).ToHashSet();
        var written = m.Refs.Where(r => r.Write).Select(r => r.Name).ToHashSet();

        foreach (var r in m.Refs)
        {
            var s = Resolve(m, r);
            if (r.Call && s.Kind is not (Kind.Function or Kind.Builtin))
            {
                // Неизвестная функция может быть функцией игры — отмечаем, только если очень похоже на свою или встроенную.
                if (Closest(r.Name, fns.Select(f => f.Name).Concat(MX.ModxDoc.Builtins)) is { } fix)
                    issues.Add(new Issue(r.Line, r.Col, r.Name.Length, I18n.T("v19.lint.unknownFn", ("name", r.Name), ("fix", fix)), true, fix));
                continue;
            }
            if (r.Call && s.Kind == Kind.Function)
            {
                var line = m.Lines[r.Line - 1];
                var open = line.IndexOf('(', r.Col + r.Name.Length);
                var want = Split(Regex.Match(s.Detail, @"\((.*)\)").Groups[1].Value).Length;
                if (open >= 0 && Args(line, open) is var got and >= 0 && got != want)
                    issues.Add(new Issue(r.Line, r.Col, r.Name.Length,
                        I18n.T(got > want ? "v19.lint.tooMany" : "v19.lint.tooFew", ("name", r.Name), ("want", want), ("got", got)), got < want));
                continue;
            }
            if (s.Kind == Kind.Game && !r.Write && !written.Contains(r.Name) && r.Name.Length >= 4)
            {
                var scope = m.Symbols.Where(x => x.Kind == Kind.Shared || x.Scope == r.Scope && r.Scope != "").Select(x => x.Name);
                if (Closest(r.Name, scope) is { } fix)
                    issues.Add(new Issue(r.Line, r.Col, r.Name.Length, I18n.T("v19.lint.typo", ("name", r.Name), ("fix", fix)), true, fix));
            }
        }

        foreach (var s in m.Symbols)
        {
            var uses = s.Kind is Kind.Function or Kind.Local or Kind.Param or Kind.Shared ? References(m, s).Count(x => !x.Write) : 1;
            if (s.Kind == Kind.Local && uses == 0)
                issues.Add(new Issue(s.Line, s.Col, s.Name.Length, I18n.T("v19.lint.unused", ("name", s.Name)), true));
            else if (s.Kind == Kind.Param && uses == 0)
                issues.Add(new Issue(s.Line, s.Col, s.Name.Length, I18n.T("v19.lint.unusedParam", ("name", s.Name)), true));
            else if (s.Kind == Kind.Function && uses == 0 && !m.HasNatives)
                issues.Add(new Issue(s.Line, s.Col, s.Name.Length, I18n.T("v19.lint.uncalled", ("name", s.Name)), true));
            if (s.Kind == Kind.Local && m.Symbols.Any(x => x.Kind == Kind.Shared && x.Name == s.Name))
                issues.Add(new Issue(s.Line, s.Col, s.Name.Length, I18n.T("v19.lint.shadow", ("name", s.Name)), true));
        }

        // Код сразу после return, break или continue в том же блоке не выполнится.
        foreach (var exit in m.Exits)
            for (var i = exit; i < m.Lines.Length; i++)
            {
                var t = Code(m.Lines[i]).Trim();
                if (t == "") continue;
                if (!t.StartsWith('}')) issues.Add(new Issue(i + 1, Indent(m.Lines[i]), t.Length, I18n.T("v19.lint.unreachable"), true));
                break;
            }
        _ = declared;
        return [.. issues.DistinctBy(x => (x.Line, x.Col, x.Message)).OrderBy(x => x.Line).ThenBy(x => x.Col)];
    }

    // ---------------------------------------------------------------- блоки и выравнивание

    /// <summary>Что можно свернуть: блоки hook, fn, native, if, while, for (строки начала и конца).</summary>
    public static List<(int Start, int End)> Folds(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var folds = new List<(int, int)>();
        var open = new Stack<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            var b = BlockStart().Match(lines[i]);
            if (b.Success && b.Groups[2].Value == "native")
            {
                var indent = b.Groups[1].Value.Length;
                var end = i + 1;
                while (end < lines.Length && !(lines[end].Trim() == "}" && Indent(lines[end]) <= indent)) end++;
                if (end < lines.Length) folds.Add((i + 1, end + 1));
                i = end;
                continue;
            }
            var t = Code(lines[i]).Trim();
            if (t.StartsWith('}') && open.Count > 0)
            {
                var start = open.Pop();
                if (i > start) folds.Add((start + 1, i + 1));
            }
            if (t.EndsWith('{')) open.Push(i);
        }
        return [.. folds.OrderBy(f => f.Item1)];
    }

    /// <summary>
    /// Выровнять код: отступ 4 пробела на уровень, без пробелов в конце строк и без лишних пустых строк;
    /// вставки на родном языке сохраняют свои отступы. Результат сверяется разбором: смысл мода не меняется,
    /// иначе возвращается исходный текст (Safe = false).
    /// </summary>
    public static (string Text, bool Safe) Format(string text)
    {
        var source = text.Replace("\r\n", "\n");
        var lines = source.Split('\n');
        var output = new List<string>();
        var depth = 0;
        string Pad(int d) => new(' ', d * 4);
        void Blank()
        {
            if (output.Count > 0 && output[^1] != "" && !Code(output[^1]).TrimEnd().EndsWith('{')) output.Add("");
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t == "") { Blank(); continue; }
            var b = BlockStart().Match(lines[i]);
            if (b.Success && b.Groups[2].Value == "native")
            {
                var indent = b.Groups[1].Value.Length;
                var end = i + 1;
                while (end < lines.Length && !(lines[end].Trim() == "}" && Indent(lines[end]) <= indent)) end++;
                output.Add(Pad(depth) + t);
                var body = lines[(i + 1)..Math.Min(end, lines.Length)];
                var cut = body.Where(l => l.Trim() != "").Select(Indent).DefaultIfEmpty(0).Min();
                foreach (var l in body) output.Add(l.Trim() == "" ? "" : Pad(depth + 1) + l[Math.Min(cut, l.Length)..].TrimEnd());
                if (end < lines.Length) output.Add(Pad(depth) + "}");
                i = end;
                continue;
            }
            var code = Code(t).TrimEnd();
            if (code.StartsWith('}'))
            {
                depth = Math.Max(0, depth - 1);
                while (output.Count > 0 && output[^1] == "") output.RemoveAt(output.Count - 1);
            }
            output.Add(Pad(depth) + t);
            if (code.EndsWith('{')) depth++;
        }
        while (output.Count > 0 && output[^1] == "") output.RemoveAt(output.Count - 1);
        var result = string.Join('\n', output) + "\n";
        if (result == source) return (text, true);
        var a = MX.ModxDoc.Parse(source);
        if (!a.Ok) return (text, false);
        var formatted = MX.ModxDoc.Parse(result);
        return formatted.Ok && ScriptGuard.Same(a, formatted) ? (result, true) : (text, false);
    }

    // ---------------------------------------------------------------- самотест

    internal const string Sample = """
        mod "Insight"
        version 1.0.0

        # Сколько золота за волну
        let reward = 50

        # Награда за волну n
        fn award(n, unused) {
            let total = reward * n
            let spare = 1
            say "+{total} gold"
            return total
            log "never"
        }

        hook tick {
            let gold = award(wave)
            gold = awrd(1, 2)
            for i in 1..3 {
                log "{i} {rewrd}"
            }
            clamp(gold, 0, 10)
        }
        """;

    [SelfTest]
    static string ModxInsightUnderstandsCode()
    {
        var m = Read(Sample);
        var lines = m.Lines;
        int L(string needle) => Array.FindIndex(lines, l => l.Contains(needle)) + 1;
        var reward = m.Symbols.Single(s => s.Name == "reward");
        if (reward.Kind != Kind.Shared) throw new Exception("reward is not shared");
        var refs = References(m, reward);
        if (refs.Count != 2) throw new Exception($"reward refs: {refs.Count}");
        // Переход к определению с места использования.
        var use = L("let total");
        var def = At(m, use, lines[use - 1].IndexOf("reward", StringComparison.Ordinal) + 1);
        if (def?.Line != L("let reward")) throw new Exception("definition of reward not found");
        // Подстановка в строке — тоже ссылка.
        var total = m.Symbols.Single(s => s.Name == "total");
        if (References(m, total).Count != 3) throw new Exception($"total refs: {References(m, total).Count}");
        // Переименование меняет и "{total}", но не трогает слово в строке без скобок.
        var (renamed, error) = Rename(Sample, total, "amount");
        if (error is not null || renamed is null || !renamed.Contains("\"+{amount} gold\"") || renamed.Contains("total")) throw new Exception("rename failed: " + error);
        if (Rename(Sample, total, "reward").Error != "v19.rename.taken") throw new Exception("rename into a taken name was allowed");
        if (Rename(Sample, total, "if").Error != "v19.rename.reserved") throw new Exception("rename into a keyword was allowed");
        // Проверка: лишний параметр, лишняя переменная, похожая функция, похожая переменная, код после return.
        var issues = Lint(m);
        string[] expect = ["unused", "spare", "awrd", "rewrd", "never"];
        foreach (var e in expect)
            if (!issues.Any(i => lines[i.Line - 1].Contains(e))) throw new Exception($"lint missed {e}: " + string.Join("; ", issues.Select(i => i.Message)));
        if (issues.FirstOrDefault(i => lines[i.Line - 1].Contains("awrd"))?.Fix != "award") throw new Exception("no fix for awrd");
        if (issues.Any(i => lines[i.Line - 1].Contains("clamp"))) throw new Exception("clamp flagged");
        // Подсказки: после hook — события, в хуке — локальные и встроенные, подпись аргументов.
        if (!Complete(m, L("hook tick"), 5).Any(i => i.Label == "tick")) throw new Exception("no events after hook");
        var inTick = Complete(m, L("let gold"), 4);
        if (!inTick.Any(i => i.Label == "gold") || !inTick.Any(i => i.Label == "clamp") || !inTick.Any(i => i.Label == "award")) throw new Exception("completion misses names");
        var clampLine = lines[L("clamp(") - 1];
        if (Signature(m, clampLine, clampLine.IndexOf("0,", StringComparison.Ordinal)) is not { Name: "clamp", Active: 1 }) throw new Exception("signature help is wrong");
        if (Hover(m, use, lines[use - 1].IndexOf("reward", StringComparison.Ordinal))?.Text.Contains("Сколько золота") != true
            && Hover(m, use, lines[use - 1].IndexOf("reward", StringComparison.Ordinal))?.Text.Contains("золота") != true) throw new Exception("hover has no doc comment");
        // Сворачивание и выравнивание: смысл не меняется, повторное — без изменений.
        if (Folds(Sample).Count != 3) throw new Exception($"folds: {Folds(Sample).Count}");
        var messy = "mod \"F\"\nhook start {\nlet a = 1\n      if a > 0 {\n say \"{a}\"   \n  }\n\n\n\n}\nnative python {\n    def f():\n        return 1\n}\n";
        var (pretty, safe) = Format(messy);
        if (!safe || !pretty.Contains("\n    let a = 1\n    if a > 0 {\n        say \"{a}\"\n    }\n}") || !pretty.Contains("    def f():\n        return 1")) throw new Exception("format:\n" + pretty);
        if (Format(pretty).Text != pretty) throw new Exception("format is not stable");
        // Пример студии (строка в коде, без ресурсов Avalonia: самопроверка в CI идёт без окна).
        var (arenaPretty, arenaSafe) = Format(Views.XStudio.DemoCode);
        if (!arenaSafe) throw new Exception("studio example cannot be formatted safely");
        // Большой файл (≈2000 строк) разбирается и проверяется быстро — это происходит на каждой паузе в наборе.
        var big = new StringBuilder("mod \"Big\"\nlet base = 1\n");
        for (var i = 0; i < 250; i++) big.Append($"fn f{i}(a, b) {{\n    let x = a * base + b\n    if x > {i} {{\n        x += f{Math.Max(0, i - 1)}(x, 1)\n    }}\n    return x\n}}\n\n");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var bigModel = Read(big.ToString());
        var bigIssues = Lint(bigModel);
        var ms = clock.ElapsedMilliseconds;
        if (ms > 1500) throw new Exception($"2000 lines took {ms} ms");
        return $"{m.Symbols.Count} symbols, {m.Refs.Count} refs, {issues.Count} issues, rename/format verified ({arenaPretty.Length} chars); 2000 lines in {ms} ms ({bigIssues.Count} issues)";
    }

}
