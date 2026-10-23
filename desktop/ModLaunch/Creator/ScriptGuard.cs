using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MX = ModScriptX;

namespace ModLaunch.Creator;

/// <summary>
/// 18.0: защита чужого кода. Скрипт, купленный в Creator Hub или импортированный в «Скрипты», прежде чем лечь
/// в библиотеку, проходит три шага:
/// 1) минимизация — без комментариев, пустых строк и отступов (вложенность — одним пробелом), свои имена
///    (let, fn, параметры, переменные циклов) — короткими метками _a, _b…;
/// 2) компиляция — разбор ModScript X до и после и сверка в песочнице: сжатый мод обязан вести себя так же,
///    как исходный (иначе шаг откатывается — купленный мод не должен сломаться от защиты);
/// 3) упаковка — заголовок MLXC и сжатие, а потом <see cref="Core.Vault"/> шифрует пакет под этот компьютер.
/// Исходник с комментариями и понятными именами в библиотеку не попадает, а в папку игры на время игры
/// ложится перевод уже сжатого мода.
/// </summary>
public static partial class ScriptGuard
{
    /// <summary>Что сделала защита: формат, размер до и после, сколько комментариев и имён убрано, сверен ли результат.</summary>
    public sealed record Report(string Format, int Before, int After, int Comments, int Names, bool Verified)
    {
        public int Saved => Before <= 0 ? 0 : Math.Max(0, (int)Math.Round(100.0 * (Before - After) / Before));

        public JsonObject Json() => new()
        {
            ["format"] = Format, ["before"] = Before, ["after"] = After, ["comments"] = Comments, ["names"] = Names, ["verified"] = Verified,
        };

        public static Report? From(JsonNode? n) => n is JsonObject o && o["format"]?.GetValue<string>() is { } f
            ? new Report(f, Int(o, "before"), Int(o, "after"), Int(o, "comments"), Int(o, "names"), o["verified"]?.GetValue<bool>() == true)
            : null;

        static int Int(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
    }

    static readonly byte[] Magic = "MLXC"u8.ToArray();
    const byte Version = 1;

    /// <summary>Защитить файл: модуль .modx и скрипт .mls — сжать и скомпилировать, архив — так же каждый скрипт внутри, остальное — упаковать.</summary>
    public static (byte[] Packed, Report Report) Protect(string fileName, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var (body, report) = ext switch
        {
            MX.ModxDoc.Extension => Modx(bytes),
            ".mls" => Classic(bytes),
            ".zip" => Package(bytes),
            _ => (bytes, new Report("file", bytes.Length, bytes.Length, 0, 0, false)),
        };
        var packed = Pack(body);
        return (packed, report with { After = packed.Length });
    }

    /// <summary>Уже упакованный (заголовок MLXC)?</summary>
    public static bool IsPacked(byte[] data) => data.Length > Magic.Length && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    /// <summary>Содержимое пакета для запуска. Старые покупки (до 18.0) хранились без пакета — их отдаём как есть.</summary>
    public static byte[] Unpack(byte[] data)
    {
        if (!IsPacked(data)) return data;
        using var input = new MemoryStream(data, Magic.Length + 1, data.Length - Magic.Length - 1);
        using var z = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    static byte[] Pack(byte[] body)
    {
        using var output = new MemoryStream();
        output.Write(Magic);
        output.WriteByte(Version);
        using (var z = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(body);
        return output.ToArray();
    }

    static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('﻿').Replace("\r\n", "\n");

    // ---------------------------------------------------------------- ModScript X

    static (byte[], Report) Modx(byte[] bytes)
    {
        var (text, comments, names, verified) = ProtectModx(Text(bytes));
        return (Encoding.UTF8.GetBytes(text), new Report("modx", bytes.Length, 0, comments, names, verified));
    }

    /// <summary>Сжать и сверить. Не разобрался исходник или сжатый ведёт себя иначе — шаг откатывается.</summary>
    internal static (string Text, int Comments, int Names, bool Verified) ProtectModx(string source)
    {
        var original = MX.ModxDoc.Parse(source);
        if (!original.Ok) return (source, 0, 0, false);
        var (min, comments) = Minify(source);
        var small = MX.ModxDoc.Parse(min);
        if (!small.Ok || !Same(original, small) || !SameRun(original, small, null)) return (source, 0, 0, false);
        var (renamed, map) = Rename(min, small);
        if (map.Count == 0) return (min, comments, 0, true);
        var hidden = MX.ModxDoc.Parse(renamed);
        if (!hidden.Ok || !SameShape(small, hidden) || !SameRun(original, hidden, map)) return (min, comments, 0, true);
        return (renamed, comments, map.Count, true);
    }

    /// <summary>
    /// Без комментариев, пустых строк и лишних пробелов. Отступы нужны разбору (конец хука — «}» не правее его
    /// начала), поэтому вложенность пишется одним пробелом на уровень. Вставки native — дословно: у Python отступ — часть кода.
    /// </summary>
    internal static (string Text, int Comments) Minify(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var depth = 0;
        var comments = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var nat = NativeStart().Match(raw);
            if (nat.Success)
            {
                // Вставка на родном языке — до своей «}» (как у разбора: не правее начала), без изменений.
                var indent = Indent(raw);
                sb.Append(' ', depth).Append("native ").Append(nat.Groups[1].Value).Append(" {\n");
                var end = i + 1;
                for (; end < lines.Length && !(lines[end].Trim() == "}" && Indent(lines[end]) <= indent); end++)
                    if (lines[end].Trim() != "") sb.Append(lines[end].TrimEnd()).Append('\n');
                sb.Append(' ', depth).Append("}\n");
                i = end;
                continue;
            }
            var (code, had) = Strip(raw);
            if (had) comments++;
            var line = Collapse(code.Trim());
            if (line == "") continue;
            if (line.StartsWith('}')) depth = Math.Max(0, depth - 1);
            sb.Append(' ', depth).Append(line).Append('\n');
            if (line.EndsWith('{')) depth++;
        }
        return (sb.ToString().TrimEnd('\n') + "\n", comments);
    }

    [GeneratedRegex(@"^\s*native\s+([\p{L}\w#]+)\s*\{\s*$")]
    private static partial Regex NativeStart();

    static int Indent(string line) => line.Length - line.TrimStart().Length;

    /// <summary># вне строки — комментарий (как у разбора ModScript X).</summary>
    static (string Code, bool Comment) Strip(string line)
    {
        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inStr) { i++; continue; }
            if (line[i] == '"') inStr = !inStr;
            else if (line[i] == '#' && !inStr) return (line[..i], true);
        }
        return (line, false);
    }

    /// <summary>Пробелы вне строк — по одному.</summary>
    static string Collapse(string line)
    {
        var sb = new StringBuilder(line.Length);
        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inStr)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < line.Length) sb.Append(line[++i]);
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') inStr = true;
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"[\p{L}_][\p{L}\w]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"^(?:let|var|const) [\p{L}_][\p{L}\w]* ?=")]
    private static partial Regex TopLet();

    /// <summary>
    /// Свои имена — короткими метками. Переименовывается только то, что объявлено в моде (let, fn, параметры,
    /// for) и нигде не значит чужое: не встречается в строках (подстановка {имя}), во вставках native, после точки
    /// (поле объекта игры), в строках шапки и не используется без объявления (там это переменная игры).
    /// </summary>
    internal static (string Text, Dictionary<string, string> Map) Rename(string min, MX.ModxDoc doc)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in doc.Shared) declared.Add(name);
        foreach (var f in doc.Functions) { declared.Add(f.Name); foreach (var p in f.Params) declared.Add(p); }
        foreach (var s in doc.Hooks.SelectMany(h => h.Body).Concat(doc.Functions.SelectMany(f => f.Body)))
            if (s.Kind is "let" or "for") declared.Add(s.Target);

        var banned = new HashSet<string>(MX.ModxDoc.Keywords.Concat(MX.ModxDoc.Builtins).Concat(MX.ModxDoc.Events), StringComparer.Ordinal);
        var lines = min.Split('\n');
        var kinds = Kinds(lines);
        for (var i = 0; i < lines.Length; i++)
        {
            if (kinds[i] == 'n' || kinds[i] == 'h') { foreach (Match m in Word().Matches(lines[i])) banned.Add(m.Value); continue; }
            foreach (var (part, inString) in Parts(lines[i]))
                if (inString) foreach (Match m in Word().Matches(part)) banned.Add(m.Value);
                else foreach (Match m in Word().Matches(part)) if (m.Index > 0 && part[m.Index - 1] == '.') banned.Add(m.Value);
        }
        // Имя, которое в каком-то теле используется без объявления, — переменная или функция игры: не трогаем.
        var globals = new HashSet<string>(doc.Shared.Select(s => s.Name).Concat(doc.Functions.Select(f => f.Name)), StringComparer.Ordinal);
        foreach (var (body, own) in doc.Hooks.Select(h => (h.Body, new HashSet<string>(StringComparer.Ordinal)))
            .Concat(doc.Functions.Select(f => (f.Body, new HashSet<string>(f.Params, StringComparer.Ordinal)))))
        {
            foreach (var s in body.Where(s => s.Kind is "let" or "for")) own.Add(s.Target);
            foreach (var s in body)
                foreach (var part in new[] { s.Target, s.Expr })
                    foreach (var (text, inString) in Parts(part))
                        if (!inString)
                            foreach (Match m in Word().Matches(text))
                                if (!own.Contains(m.Value) && !globals.Contains(m.Value)) banned.Add(m.Value);
        }

        var all = new HashSet<string>(Word().Matches(min).Select(m => m.Value), StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = 0;
        foreach (var name in declared.Where(d => !banned.Contains(d) && d.Length > 2).OrderBy(d => d, StringComparer.Ordinal))
        {
            string alias;
            do alias = "_" + Label(n++); while (all.Contains(alias) || declared.Contains(alias));
            map[name] = alias;
        }
        if (map.Count == 0) return (min, map);

        var sb = new StringBuilder(min.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            if (kinds[i] is 'n' or 'h') { sb.Append(lines[i]); continue; }
            foreach (var (part, inString) in Parts(lines[i]))
                sb.Append(inString ? part : Word().Replace(part, m => m.Index > 0 && part[m.Index - 1] == '.' ? m.Value : map.GetValueOrDefault(m.Value, m.Value)));
        }
        return (sb.ToString(), map);
    }

    /// <summary>Метка по номеру: a…z, aa, ab…</summary>
    static string Label(int n)
    {
        var s = "";
        do { s = (char)('a' + n % 26) + s; n = n / 26 - 1; } while (n >= 0);
        return s;
    }

    /// <summary>
    /// Что за строка сжатого текста: c — код (тело хука или функции, их заголовки, let верхнего уровня),
    /// n — вставка native (и её скобки), h — шапка (mod, version, engine… — не трогаем).
    /// </summary>
    static char[] Kinds(string[] lines)
    {
        var kinds = new char[lines.Length];
        var block = ' ';
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (block != ' ')
            {
                kinds[i] = block;
                if (l == "}") block = ' ';
                continue;
            }
            if (NativeStart().IsMatch(l)) { kinds[i] = 'n'; block = 'n'; continue; }
            if (l.StartsWith("hook ") || l.StartsWith("fn ")) { kinds[i] = 'c'; if (l.EndsWith('{')) block = 'c'; continue; }
            kinds[i] = TopLet().IsMatch(l) ? 'c' : 'h';
        }
        return kinds;
    }

    /// <summary>Строка по кускам: код и строковые литералы (с кавычками).</summary>
    static IEnumerable<(string Text, bool InString)> Parts(string line)
    {
        var start = 0;
        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (inStr && line[i] == '\\') { i++; continue; }
            if (line[i] != '"') continue;
            if (!inStr)
            {
                if (i > start) yield return (line[start..i], false);
                start = i;
                inStr = true;
            }
            else
            {
                yield return (line[start..(i + 1)], true);
                start = i + 1;
                inStr = false;
            }
        }
        if (start < line.Length) yield return (line[start..], inStr);
    }

    /// <summary>Тот же мод после разбора: шапка, общие переменные, хуки, функции и вставки (выражения — без учёта пробелов).</summary>
    static bool Same(MX.ModxDoc a, MX.ModxDoc b) =>
        a.Name == b.Name && a.Version == b.Version && a.Author == b.Author && a.About == b.About && a.Game == b.Game && a.Engine == b.Engine
        && a.Shared.Select(s => s.Name + "=" + s.Value?.ToJsonString()).SequenceEqual(b.Shared.Select(s => s.Name + "=" + s.Value?.ToJsonString()))
        && a.Hooks.Count == b.Hooks.Count && a.Hooks.Zip(b.Hooks).All(p => p.First.Event == p.Second.Event && SameBody(p.First.Body, p.Second.Body))
        && a.Functions.Count == b.Functions.Count && a.Functions.Zip(b.Functions).All(p => p.First.Name == p.Second.Name && p.First.Params.SequenceEqual(p.Second.Params) && SameBody(p.First.Body, p.Second.Body))
        && a.Natives.Select(n => n.Lang + ":" + Lines(n.Code)).SequenceEqual(b.Natives.Select(n => n.Lang + ":" + Lines(n.Code)));

    static bool SameBody(List<MX.Stmt> a, List<MX.Stmt> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Kind == p.Second.Kind && p.First.Target == p.Second.Target && Collapse(p.First.Expr) == Collapse(p.Second.Expr));

    static string Lines(string code) => string.Join('\n', code.Split('\n').Select(l => l.TrimEnd()).Where(l => l != ""));

    /// <summary>После переименования: то же число и вид хуков, функций, операторов и вставок.</summary>
    static bool SameShape(MX.ModxDoc a, MX.ModxDoc b) =>
        a.Name == b.Name && a.Version == b.Version && a.Engine == b.Engine && a.Shared.Count == b.Shared.Count && a.Natives.Count == b.Natives.Count
        && a.Hooks.Count == b.Hooks.Count && a.Hooks.Zip(b.Hooks).All(p => p.First.Event == p.Second.Event && p.First.Body.Select(s => s.Kind).SequenceEqual(p.Second.Body.Select(s => s.Kind)))
        && a.Functions.Count == b.Functions.Count && a.Functions.Zip(b.Functions).All(p => p.First.Params.Count == p.Second.Params.Count && p.First.Body.Select(s => s.Kind).SequenceEqual(p.Second.Body.Select(s => s.Kind)));

    /// <summary>
    /// Сверка в песочнице: тот же вывод, то же число ошибок и те же переменные (свои — по карте новых имён).
    /// Два прогона — без переменных игры и с типичными, — чтобы проверить и ветки «если».
    /// </summary>
    static bool SameRun(MX.ModxDoc a, MX.ModxDoc b, Dictionary<string, string>? map)
    {
        foreach (var game in new[] { null, Sample() })
        {
            var x = MX.Sandbox.Run(a, game is null ? null : new Dictionary<string, object?>(game), maxSteps: 50_000);
            var y = MX.Sandbox.Run(b, game is null ? null : new Dictionary<string, object?>(game), maxSteps: 50_000);
            if (x.Errors.Count != y.Errors.Count) return false;
            if (!x.Output.SequenceEqual(y.Output)) return false;
            foreach (var (name, v) in x.Vars)
            {
                var key = map is not null && map.TryGetValue(name, out var alias) ? alias : name;
                if (!y.Vars.TryGetValue(key, out var w) || MX.Sandbox.Text(v) != MX.Sandbox.Text(w)) return false;
            }
            if (x.Vars.Count != y.Vars.Count) return false;
        }
        return true;
    }

    static Dictionary<string, object?> Sample() => new(StringComparer.Ordinal) { ["gold"] = 100.0, ["health"] = 80.0, ["speed"] = 5.0, ["level"] = 3.0, ["wave"] = 2.0 };

    // ---------------------------------------------------------------- классический ModScript и архивы

    /// <summary>.mls: без строк-комментариев, пустых строк и хвостовых пробелов; сверка — сборкой до и после.</summary>
    static (byte[], Report) Classic(byte[] bytes)
    {
        var source = Text(bytes);
        var (min, comments) = MinifyClassic(source);
        var ok = SameBuild(source, min);
        var text = ok ? min : source;
        return (Encoding.UTF8.GetBytes(text), new Report("mls", bytes.Length, 0, ok ? comments : 0, 0, ok));
    }

    internal static (string Text, int Comments) MinifyClassic(string source)
    {
        var sb = new StringBuilder();
        var comments = 0;
        foreach (var raw in source.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.TrimEnd();
            if (t.TrimStart().StartsWith('#')) { comments++; continue; }
            if (t.Trim() == "") continue;
            sb.Append(t).Append('\n');
        }
        return (sb.ToString(), comments);
    }

    static bool SameBuild(string a, string b)
    {
        var x = ModScript.Compile(a);
        var y = ModScript.Compile(b);
        return x.Ok == y.Ok && x.Name == y.Name && x.Version == y.Version && x.Game == y.Game && x.Statements == y.Statements
            && x.Changes.Select(c => c.ToJsonString()).SequenceEqual(y.Changes.Select(c => c.ToJsonString()))
            && x.Writes.SequenceEqual(y.Writes) && x.Configs.Count == y.Configs.Count && x.Inis.Count == y.Inis.Count && x.Copies.Count == y.Copies.Count
            && x.Vars.Count == y.Vars.Count && x.Vars.All(kv => y.Vars.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    /// <summary>Архив: каждый .modx и .mls внутри — сжать и сверить; остальные файлы — как есть.</summary>
    static (byte[], Report) Package(byte[] bytes)
    {
        int comments = 0, names = 0, scripts = 0, verified = 0;
        try
        {
            using var input = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var e in input.Entries)
                {
                    var entry = zip.CreateEntry(e.FullName, CompressionLevel.SmallestSize);
                    if (e.FullName.EndsWith('/')) continue;
                    using var src = e.Open();
                    using var buffer = new MemoryStream();
                    src.CopyTo(buffer);
                    var data = buffer.ToArray();
                    var ext = Path.GetExtension(e.FullName).ToLowerInvariant();
                    if (ext is MX.ModxDoc.Extension or ".mls")
                    {
                        var (body, r) = ext == ".mls" ? Classic(data) : Modx(data);
                        data = body;
                        scripts++;
                        comments += r.Comments;
                        names += r.Names;
                        if (r.Verified) verified++;
                    }
                    using var dst = entry.Open();
                    dst.Write(data);
                }
            return (output.ToArray(), new Report("package", bytes.Length, 0, comments, names, scripts > 0 && verified == scripts));
        }
        catch (InvalidDataException)
        {
            // Не архив — упаковываем как обычный файл.
            return (bytes, new Report("file", bytes.Length, 0, 0, 0, false));
        }
    }

    // ---------------------------------------------------------------- самопроверка

    [SelfTest]
    static string ScriptGuardKeepsBehaviour()
    {
        const string source = """
            # Arena — купленный мод
            mod "Arena"
            version 1.2.0
            engine unity

            # Награды
            let bonus = 250   # стартовый бонус
            let per_wave = 50

            fn reward(wave_no) {
                let total = clamp(per_wave * wave_no, 0, 1000)
                return total
            }

            hook start {
                set gold = gold + bonus
                say "Bonus {bonus}!"
            }

            hook tick {
                for step in 1..2 {
                    if gold > 10 {
                        gold += reward(step)
                    } else {
                        log "poor"
                    }
                }
            }
            """;
        var (text, comments, names, verified) = ProtectModx(source);
        if (!verified) throw new Exception("not verified");
        if (comments < 3) throw new Exception("comments left: " + comments);
        if (text.Contains('#') || text.Contains("    ")) throw new Exception("not minified");
        // bonus — в строке «{bonus}»: имя остаётся; per_wave, reward, wave_no, total, step — прячутся.
        if (!text.Contains("bonus") || text.Contains("per_wave") || text.Contains("reward") || text.Contains("wave_no") || names < 4)
            throw new Exception("rename: " + text);
        if (!Regex.IsMatch(text, @"\b_a\b")) throw new Exception("no labels");
        var a = MX.Sandbox.Run(MX.ModxDoc.Parse(source), new Dictionary<string, object?> { ["gold"] = 100.0 });
        var b = MX.Sandbox.Run(MX.ModxDoc.Parse(text), new Dictionary<string, object?> { ["gold"] = 100.0 });
        if (!a.Output.SequenceEqual(b.Output) || MX.Sandbox.Text(a.Vars["gold"]) != MX.Sandbox.Text(b.Vars["gold"])) throw new Exception("behaviour changed");

        // Автомодерация на запуске видит уже защищённый текст — вердикт тот же.
        if (Moderation.CheckModx(source).Ok != Moderation.CheckModx(text).Ok) throw new Exception("moderation verdict changed");

        // Пакет: заголовок, сжатие, обратно — тот же сжатый текст; старые покупки без заголовка — как есть.
        var (packed, report) = Protect("arena.modx", Encoding.UTF8.GetBytes(source));
        if (!IsPacked(packed) || Encoding.UTF8.GetString(Unpack(packed)) != text) throw new Exception("pack round trip");
        if (report.After >= report.Before || report.Names != names) throw new Exception("report " + report);
        var legacy = Encoding.UTF8.GetBytes(source);
        if (!Unpack(legacy).SequenceEqual(legacy)) throw new Exception("legacy unpack");

        // Мод с ошибкой разбора не трогаем — только упаковка.
        var broken = "mod \"X\"\nhook start {\n  set gold = \n";
        if (ProtectModx(broken).Text != broken) throw new Exception("broken mod changed");
        // Имя из вставки native и поле после точки не переименовываются.
        var native = "mod \"N\"\nlet speed_up = 2\nhook start {\n  set player.speed_up = speed_up\n}\nnative lua {\n  print(speed_up)\n}\n";
        if (!ProtectModx(native).Text.Contains("speed_up = speed_up")) throw new Exception("native name renamed");
        return $"modx minified ({comments} comments, {names} names), sandbox-verified, packed; broken and native-bound code untouched";
    }
}
