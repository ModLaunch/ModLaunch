using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using MX = ModScriptX;

namespace ModLaunch.Creator;

/// <summary>Замечание автомодерации: block — не пускаем, warn — пускаем с пометкой, info — для сведения.</summary>
public sealed record Finding(string Level, string Text);

/// <summary>Итог проверки: можно ли ставить, что найдено и что инжект меняет в игре (вместо исходника).</summary>
public sealed record Verdict(bool Ok, List<Finding> Findings, string Changes)
{
    public bool Clean => Ok && Findings.All(f => f.Level == "info");
}

/// <summary>
/// 11.0: автомодерация инжектов Creator Hub. Проверяет товар трижды: при выставлении (продавец не
/// выставит опасное), при подключении у покупателя (файл мог подменить кто-то по дороге) и после
/// каждой игры (вылеты сразу после инжекта — «страйки»; два подряд — инжект выключается сам).
/// Что проверяется в .modx: разбор без ошибок, размер, вызовы опасного в native-блоках (файлы,
/// сеть, процессы, eval), прогон в песочнице с лимитом шагов (бесконечный цикл в tick не пройдёт).
/// В архиве: выход за папку (../), исполняемые файлы и скрипты Windows, размер и число файлов.
/// </summary>
public static class Moderation
{
    public const int MaxSource = 64 * 1024;
    public const long MaxUnpacked = 256L * 1024 * 1024;

    /// <summary>Опасное в native-блоках: доступ к файлам, сети, процессам и выполнение строк как кода.</summary>
    static readonly Dictionary<string, (string Pattern, string Why)[]> Forbidden = new()
    {
        ["python"] =
        [
            // import os | import sys, os | from os import … — любой опасный модуль в списке импорта (одна строка).
            (@"\b(?:import|from)\b[^\n#]*\b(os|subprocess|socket|shutil|ctypes|urllib|requests|http|ftplib|smtplib|winreg|_winreg|importlib|sys|pickle|pty)\b", "system or network module"),
            (@"\b(eval|exec|compile|__import__|getattr|setattr|globals|vars)\s*\(", "runs text as code or reflection"),
            (@"\bopen\s*\(|\b(os|sys|subprocess)\.", "opens files or system access"),
        ],
        ["javascript"] =
        [
            (@"\brequire\s*\(|\bprocess\.|child_process|\bnw\.", "Node.js system access"),
            (@"\b(eval|Function)\s*\(|new\s+Function\b|\bimport\s*\(", "runs text as code"),
            (@"\b(fetch|XMLHttpRequest|WebSocket)\b", "network access"),
        ],
        ["lua"] =
        [
            (@"\bos\.|\bio\.", "system or file access"),
            // load("…"), load"…", load[[…]], dofile, require — загрузка кода или модулей.
            (@"\b(loadstring|load|dofile|loadfile)\s*[(""\[]|\brequire\b|package\.loadlib", "runs text/files as code or loads modules"),
        ],
        ["csharp"] =
        [
            (@"System\.IO|\bFile\.|\bDirectory\.|\bFileStream\b", "file access"),
            (@"System\.Net|\bHttpClient\b|\bWebClient\b|\bSocket\b|UnityWebRequest", "network access"),
            (@"System\.Diagnostics\.Process|\bProcess\.Start\b|DllImport|Marshal\.|Assembly\.Load|Microsoft\.Win32|Registry\.", "processes, native code or registry"),
        ],
        ["gdscript"] =
        [
            (@"\bOS\.(execute|shell_open|kill|create_process)\b", "runs programs"),
            (@"\b(FileAccess|DirAccess|File|Directory)\b\s*(\.|\()", "file access"),
            (@"\b(HTTPRequest|HTTPClient|StreamPeerTCP|PacketPeerUDP|WebSocketPeer)\b", "network access"),
        ],
    };

    /// <summary>
    /// Опасное в хуках и функциях: имена, которые исполняют строку как код или лезут в систему. Lang.Expr
    /// копирует их в код движка как есть, поэтому проверяем не только native-блоки, но и тело хуков.
    /// </summary>
    static readonly HashSet<string> CallDenylist = new(StringComparer.Ordinal)
    {
        "eval", "exec", "execfile", "compile", "__import__", "getattr", "setattr", "delattr", "globals", "locals", "vars",
        "open", "require", "Function", "import", "load", "loadstring", "dofile", "loadfile", "system", "popen", "spawn", "dlopen",
    };

    /// <summary>Опасные пространства имён и вызовы-методы: os.X(), System.Diagnostics.Process, child.exec() и т. п.</summary>
    static readonly (string Pattern, string Name)[] MemberDenylist =
    [
        (@"\bos\s*\.", "os"),
        (@"\bio\s*\.", "io"),
        (@"\bsys\s*\.", "sys"),
        (@"\bsubprocess\b", "subprocess"),
        (@"\bchild_process\b", "child_process"),
        (@"\bSystem\s*\.", "System"),
        (@"\bProcess\s*\.|\bnew\s+Process\b", "Process"),
        (@"\.\s*(system|popen|exec|execSync|spawn|execute)\s*\(", "system call"),
        (@"\b(importlib|ctypes|winreg|_winreg|Marshal|DllImport|Assembly)\b", "native module"),
    ];

    /// <summary>Файлы, которых не бывает в модах игр: программы и скрипты Windows.</summary>
    static readonly HashSet<string> Executables = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".com", ".scr", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".wsf", ".hta", ".msi", ".msp", ".reg", ".lnk", ".cpl", ".sys", ".inf" };

    /// <summary>Текст ошибки разбора: ключ err.* (с подстановкой после «|») — из словаря cr.*, остальное как есть.</summary>
    static string Explain(string message)
    {
        if (!message.StartsWith("err.", StringComparison.Ordinal)) return message;
        var parts = message.Split('|', 2);
        var text = I18n.T("cr." + parts[0], ("x", parts.Length > 1 ? parts[1] : ""));
        return text == "cr." + parts[0] ? message : text;
    }

    /// <summary>Проверить .modx: разбор, native-блоки, песочница.</summary>
    public static Verdict CheckModx(string source)
    {
        var found = new List<Finding>();
        if (Encoding.UTF8.GetByteCount(source) > MaxSource) found.Add(new("block", I18n.T("v11.mod.big", ("kb", MaxSource / 1024))));
        var doc = MX.ModxDoc.Parse(source);
        foreach (var d in doc.Diags.Where(d => !d.Warning).Take(3))
            found.Add(new("block", I18n.T("v11.mod.parse", ("line", d.Line), ("text", Explain(d.Message)))));
        foreach (var n in doc.Natives)
        {
            if (!Forbidden.TryGetValue(n.Lang, out var rules)) continue;
            // Пробелы вокруг точек убираем: «System . IO . File» — это тот же System.IO.File.
            var code = Regex.Replace(n.Code, @"\s*\.\s*", ".");
            foreach (var (pattern, why) in rules)
                if (Regex.IsMatch(code, pattern)) found.Add(new("block", I18n.T("v11.mod.native", ("lang", n.Lang), ("line", n.Line), ("why", why))));
        }
        if (doc.Natives.Count > 0 && found.All(f => f.Level != "block"))
            found.Add(new("warn", I18n.T("v11.mod.nativeOk", ("langs", string.Join(", ", doc.Natives.Select(x => x.Lang).Distinct())))));
        // Хуки и функции тоже попадают в код движка (Lang.Expr копирует имена как есть): опасные вызовы — не пускаем.
        CheckStatements(doc, found);
        if (doc.Ok)
        {
            try
            {
                // Прогон в песочнице: 20 тиков, лимит шагов. Бесконечный цикл или падение — не пускаем.
                var run = MX.Sandbox.Run(doc, ticks: 20, maxSteps: 300_000);
                foreach (var e in run.Errors.Where(e => !e.Warning).Take(2))
                    found.Add(new(e.Message.Contains("endless") ? "block" : "warn", I18n.T("v11.mod.run", ("line", e.Line), ("text", e.Message))));
            }
            // Мод уронил песочницу (кривые аргументы, не число и т. п.) — не пускаем вместо того, чтобы пропустить.
            catch (Exception) { found.Add(new("block", I18n.T("v11fix.mod.crash"))); }
        }
        if (doc.Hooks.Count == 0 && doc.Natives.Count == 0) found.Add(new("warn", I18n.T("v11.mod.empty")));
        return new Verdict(found.All(f => f.Level != "block"), found, Changes(doc));
    }

    /// <summary>
    /// Проверить тело хуков и функций: вызов, который не встроенная функция (len, rand…), не своя функция
    /// мода и попадает в чёрный список (eval, __import__, os.*, require, Process…), — не пускаем. Вызовы
    /// игрового API (renpy.notify, player.heal…) остаются разрешёнными.
    /// </summary>
    static void CheckStatements(MX.ModxDoc doc, List<Finding> found)
    {
        var fns = doc.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var stmts = doc.Hooks.SelectMany(h => h.Body).Concat(doc.Functions.SelectMany(f => f.Body));
        foreach (var s in stmts)
        {
            var expr = s.Expr;
            if (expr == "") continue;
            // Прямое обращение к опасным пространствам имён и вызовы-методы (os., System., child.exec()).
            foreach (var (pattern, name) in MemberDenylist)
                if (Regex.IsMatch(expr, pattern)) { found.Add(new("block", I18n.T("v11fix.mod.stmt", ("line", s.Line), ("name", name)))); break; }
            // Вызовы по имени: name(…). Встроенные и свои функции пропускаем, из чёрного списка — блокируем.
            foreach (Match c in Regex.Matches(expr, @"(?<![\p{L}\w.])([\p{L}_][\p{L}\w]*)\s*\("))
            {
                var name = c.Groups[1].Value;
                if (MX.ModxDoc.Builtins.Contains(name) || fns.Contains(name)) continue;
                if (CallDenylist.Contains(name)) { found.Add(new("block", I18n.T("v11fix.mod.stmt", ("line", s.Line), ("name", name)))); break; }
            }
        }
    }

    /// <summary>Начало описания «что меняет мод», которое мастер ставит в превью вместо исходника.</summary>
    public const string Mark = "🛡 ";

    /// <summary>Код, скрипт и сборка (кроме открытой лицензии) продаются инжектом: покупатель не видит исходник.</summary>
    public static bool Sealed(string kind, string license) => license != "open" && kind is "code" or "script" or "pack";

    /// <summary>Для покупателя: этот лот подключается к игре инжектом, а не скачивается.</summary>
    public static bool Sealed(Listing l) => !l.Mine && Sealed(l.Kind, l.License);

    /// <summary>Что меняет мод: переменные, события, свои функции и движок — это видит покупатель вместо кода.</summary>
    public static string Changes(MX.ModxDoc doc)
    {
        var body = doc.Hooks.SelectMany(h => h.Body).Concat(doc.Functions.SelectMany(f => f.Body)).ToList();
        var vars = ChangedVars(doc);
        var parts = new List<string>();
        if (vars.Count > 0) parts.Add(I18n.T("v11.mod.ch.vars", ("list", string.Join(", ", vars))));
        if (doc.Hooks.Count > 0) parts.Add(I18n.T("v11.mod.ch.events", ("list", string.Join(", ", doc.Hooks.Select(h => h.Event).Distinct()))));
        if (body.Any(s => s.Kind == "say")) parts.Add(I18n.T("v11.mod.ch.say"));
        if (doc.Engine is { } e) parts.Add(I18n.T("v11.mod.ch.engine", ("engine", e)));
        if (doc.Natives.Count > 0) parts.Add(I18n.T("v11.mod.ch.native", ("list", string.Join(", ", doc.Natives.Select(n => n.Lang).Distinct()))));
        return string.Join(" · ", parts);
    }

    /// <summary>Переменные игры, которые мод меняет (set/add), без своих let, for и параметров функций.</summary>
    internal static List<string> ChangedVars(MX.ModxDoc doc)
    {
        var body = doc.Hooks.SelectMany(h => h.Body).Concat(doc.Functions.SelectMany(f => f.Body)).ToList();
        var locals = body.Where(s => s.Kind is "let" or "for").Select(s => s.Target).Concat(doc.Functions.SelectMany(f => f.Params)).ToHashSet();
        return body.Where(s => s.Kind is "set" or "add" && !locals.Contains(s.Target)).Select(s => s.Target).Distinct().Take(12).ToList();
    }

    /// <summary>Проверить архив (сборка, ассет, модель): пути, исполняемые файлы, размер.</summary>
    public static Verdict CheckPackage(byte[] zipBytes)
    {
        var found = new List<Finding>();
        long total = 0;
        int files = 0, code = 0;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                files++;
                total += entry.Length;
                var name = entry.FullName.Replace('\\', '/');
                // Хвостовую точку/пробел и запрещённые символы Windows «нормализует» (run.bat. → run.bat), поэтому не пускаем такие имена.
                var bare = name.TrimEnd('.', ' ');
                if (name.StartsWith('/') || name.Contains("../") || name.Contains(':') || name != bare || name.Any(c => c < 32 || "<>|?*".Contains(c)))
                    found.Add(new("block", I18n.T("v11.mod.zip.path", ("path", name))));
                var ext = Path.GetExtension(bare);
                if (Executables.Contains(ext)) found.Add(new("block", I18n.T("v11.mod.zip.exe", ("path", name))));
                if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)) code++;
                if (ext.Equals(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase) && entry.Length < MaxSource)
                {
                    using var s = new StreamReader(entry.Open());
                    var inner = CheckModx(s.ReadToEnd());
                    found.AddRange(inner.Findings.Select(f => f with { Text = name + ": " + f.Text }));
                }
            }
        }
        catch (InvalidDataException) { found.Add(new("block", I18n.T("v11.mod.zip.bad"))); }
        if (files > 5000) found.Add(new("block", I18n.T("v11.mod.zip.many", ("n", files))));
        if (total > MaxUnpacked) found.Add(new("block", I18n.T("v11.mod.zip.big", ("mb", MaxUnpacked / 1024 / 1024))));
        if (code > 0) found.Add(new("warn", I18n.T("v11.mod.zip.dll", ("n", code))));
        var changes = I18n.T("v11.mod.ch.files", ("n", files), ("size", Format(total)));
        return new Verdict(found.All(f => f.Level != "block"), found.DistinctBy(f => f.Text).Take(12).ToList(), changes);
    }

    /// <summary>Проверить файл товара по имени: .modx, архив или одиночный файл.</summary>
    public static Verdict Check(string fileName, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(MX.ModxDoc.Extension, StringComparison.OrdinalIgnoreCase)) return CheckModx(Encoding.UTF8.GetString(bytes));
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)) return CheckPackage(bytes);
        var found = new List<Finding>();
        // Хвостовую точку/пробел Windows уберёт (run.exe → run.exe): смотрим расширение без них.
        if (Executables.Contains(Path.GetExtension(fileName.TrimEnd('.', ' ')))) found.Add(new("block", I18n.T("v11.mod.zip.exe", ("path", fileName))));
        return new Verdict(found.Count == 0, found, I18n.T("v11.mod.ch.files", ("n", 1), ("size", Format(bytes.LongLength))));
    }

    static string Format(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    // ---------------------------------------------------------------- страйки после игры

    static readonly JsonFile State = new(Path.Combine(Paths.DataDir, "moderation.json"), () => new JsonObject { ["injects"] = new JsonObject() });

    static JsonObject Entry(string id) => State.Data.Obj("injects").Obj(id);

    public static int Strikes(string id) => (int)Entry(id).Long("strikes");

    /// <summary>Игра вылетела сразу после инжекта: +1 страйк. Два подряд — инжект выключается. Возвращает true, если выключили.</summary>
    public static bool Strike(string id, string gameId)
    {
        var e = Entry(id);
        var n = e.Long("strikes") + 1;
        e["strikes"] = n;
        e["lastStrike"] = DateTime.UtcNow.ToString("o");
        e["game"] = gameId;
        State.Save();
        return n >= 2;
    }

    /// <summary>Игра отыграла нормально — страйки обнуляются.</summary>
    public static void Clear(string id)
    {
        if (Strikes(id) == 0) return;
        Entry(id)["strikes"] = 0;
        State.Save();
    }

    /// <summary>Журнал автомодерации — для экрана «Автомодерация» в Creator Hub.</summary>
    public static void Note(string id, string title, Verdict v, string stage)
    {
        var log = State.Data["log"] as JsonArray ?? new JsonArray();
        log.Insert(0, new JsonObject
        {
            ["id"] = id, ["title"] = title, ["stage"] = stage, ["ok"] = v.Ok, ["at"] = DateTime.UtcNow.ToString("o"),
            ["findings"] = new JsonArray(v.Findings.Select(f => (JsonNode)$"{f.Level}|{f.Text}").ToArray()),
        });
        while (log.Count > 200) log.RemoveAt(log.Count - 1);
        State.Data["log"] = log;
        State.Save();
    }

    public sealed record LogEntry(string Id, string Title, string Stage, bool Ok, DateTime? At, List<Finding> Findings);

    public static List<LogEntry> Log() => (State.Data["log"] as JsonArray ?? []).OfType<JsonObject>().Select(o => new LogEntry(
        o.Str("id") ?? "", o.Str("title") ?? "", o.Str("stage") ?? "", o.Bool("ok"), DateTime.TryParse(o.Str("at"), out var at) ? at.ToUniversalTime() : null,
        o.Arr("findings").Select(x => x?.ToString() ?? "").Select(x => x.Split('|', 2)).Select(p => new Finding(p[0], p.Length > 1 ? p[1] : "")).ToList())).ToList();

    [SelfTest]
    static string ModerationBlocksDangerousInjects()
    {
        const string clean = "mod \"Ok\"\nlet bonus = 5\nhook start {\n  add gold bonus\n}\n";
        var good = CheckModx(clean);
        // Не по тексту Changes: в --selfcheck (без окна) переводов нет, и I18n.T отдаёт ключи.
        if (!good.Ok || !ChangedVars(MX.ModxDoc.Parse(clean)).Contains("gold")) throw new Exception("clean mod blocked: " + string.Join("; ", good.Findings.Select(f => f.Text)));
        var bad = CheckModx("mod \"Bad\"\nhook start {\n  add gold 1\n}\nnative python {\n    import os\n    os.remove('save')\n}\n");
        if (bad.Ok) throw new Exception("os in python passed");
        // 1.6: обход прежних регулярок — import sys, os / System . IO / require в Lua.
        if (CheckModx("mod \"B\"\nnative python {\n    import sys, os\n    os.system('calc')\n}\n").Ok) throw new Exception("import sys, os passed");
        if (CheckModx("mod \"B\"\nnative csharp {\n    System . IO . File . Delete(\"save.dat\");\n}\n").Ok) throw new Exception("spaced System.IO passed");
        if (CheckModx("mod \"B\"\nnative lua {\n    local o = require(\"os\")\n    o.execute(\"calc\")\n}\n").Ok) throw new Exception("lua require passed");
        // 0.0/1.1: опасные вызовы в теле хука (не native) попадают в код движка — тоже не пускаем.
        if (CheckModx("mod \"X\"\nhook start {\n  let m = __import__(\"os\")\n  call m.system(\"calc\")\n}\n").Ok) throw new Exception("__import__ in hook passed");
        if (CheckModx("mod \"X\"\nengine renpy\nhook start {\n  call exec(\"import os\")\n}\n").Ok) throw new Exception("exec in hook passed");
        if (CheckModx("mod \"X\"\nhook start {\n  call os.execute(\"calc\")\n}\n").Ok) throw new Exception("os.execute in hook passed");
        // Вызовы игрового API в теле хука остаются разрешёнными.
        var api = CheckModx("mod \"Ok\"\nengine renpy\nfn give(n) {\n  add gold n\n}\nhook start {\n  give(10)\n  call renpy.notify(\"hi\")\n  call player.heal(5)\n}\n");
        if (!api.Ok) throw new Exception("game API call blocked: " + string.Join("; ", api.Findings.Select(f => f.Text)));
        var loop = CheckModx("mod \"Loop\"\nhook tick {\n  while true {\n    add x 1\n  }\n}\n");
        if (loop.Ok) throw new Exception("endless loop passed");
        // 1.2: то, что роняло песочницу (rand с перепутанными границами, список внутри себя), больше не роняет и проверку.
        CheckModx("mod \"Crash\"\nhook start {\n  set x = rand(5, missing)\n  let a = [0]\n  a[0] = a\n  log a\n}\n");
        // Каждое имя — отдельным архивом: в --selfcheck переводов нет, и одинаковые тексты находок склеиваются.
        static byte[] Zip(string name)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write("x");
            return ms.ToArray();
        }
        // 1.6: хвостовая точка — Windows превратит run.bat. в run.bat.
        foreach (var name in new[] { "../evil.txt", "run.bat", "run.bat." })
            if (CheckPackage(Zip(name)).Ok) throw new Exception($"zip slip, .bat or trailing-dot .bat passed: {name}");
        if (!CheckPackage(Zip("textures/stone.png")).Ok) throw new Exception("clean package blocked");
        return "clean .modx passes; os/eval/import, hook-body RCE, endless loops, ../ and .bat are blocked; sandbox crashes are gone";
    }
}
