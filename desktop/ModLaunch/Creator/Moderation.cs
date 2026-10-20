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
            (@"\bimport\s+(os|subprocess|socket|shutil|ctypes|urllib|requests|http|ftplib|smtplib|winreg|_winreg)\b|\bfrom\s+(os|subprocess|socket|shutil|ctypes|urllib|requests|http)\b", "system or network module"),
            (@"\b(eval|exec|compile|__import__)\s*\(", "runs text as code"),
            (@"\bopen\s*\(", "opens files"),
        ],
        ["javascript"] =
        [
            (@"\brequire\s*\(|\bprocess\.|child_process|\bnw\.", "Node.js system access"),
            (@"\b(eval|Function)\s*\(|new\s+Function\b", "runs text as code"),
            (@"\b(fetch|XMLHttpRequest|WebSocket)\b", "network access"),
        ],
        ["lua"] =
        [
            (@"\bos\.(execute|remove|rename|exit|getenv)\b|\bio\.(open|popen|lines|write)\b", "system or file access"),
            (@"\b(loadstring|load|dofile|loadfile)\s*\(|package\.loadlib", "runs text or files as code"),
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

    /// <summary>Файлы, которых не бывает в модах игр: программы и скрипты Windows.</summary>
    static readonly HashSet<string> Executables = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".com", ".scr", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".wsf", ".hta", ".msi", ".msp", ".reg", ".lnk", ".cpl", ".sys", ".inf" };

    /// <summary>Проверить .modx: разбор, native-блоки, песочница.</summary>
    public static Verdict CheckModx(string source)
    {
        var found = new List<Finding>();
        if (Encoding.UTF8.GetByteCount(source) > MaxSource) found.Add(new("block", I18n.T("v11.mod.big", ("kb", MaxSource / 1024))));
        var doc = MX.ModxDoc.Parse(source);
        foreach (var d in doc.Diags.Where(d => !d.Warning).Take(3))
            found.Add(new("block", I18n.T("v11.mod.parse", ("line", d.Line), ("text", d.Message))));
        foreach (var n in doc.Natives)
        {
            if (!Forbidden.TryGetValue(n.Lang, out var rules)) continue;
            foreach (var (pattern, why) in rules)
                if (Regex.IsMatch(n.Code, pattern)) found.Add(new("block", I18n.T("v11.mod.native", ("lang", n.Lang), ("line", n.Line), ("why", why))));
        }
        if (doc.Natives.Count > 0 && found.All(f => f.Level != "block"))
            found.Add(new("warn", I18n.T("v11.mod.nativeOk", ("langs", string.Join(", ", doc.Natives.Select(x => x.Lang).Distinct())))));
        if (doc.Ok)
        {
            // Прогон в песочнице: 20 тиков, лимит шагов. Бесконечный цикл или падение — не пускаем.
            var run = MX.Sandbox.Run(doc, ticks: 20, maxSteps: 300_000);
            foreach (var e in run.Errors.Where(e => !e.Warning).Take(2))
                found.Add(new(e.Message.Contains("endless") ? "block" : "warn", I18n.T("v11.mod.run", ("line", e.Line), ("text", e.Message))));
        }
        if (doc.Hooks.Count == 0 && doc.Natives.Count == 0) found.Add(new("warn", I18n.T("v11.mod.empty")));
        return new Verdict(found.All(f => f.Level != "block"), found, Changes(doc));
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
        var locals = body.Where(s => s.Kind is "let" or "for").Select(s => s.Target).Concat(doc.Functions.SelectMany(f => f.Params)).ToHashSet();
        var vars = body.Where(s => s.Kind is "set" or "add" && !locals.Contains(s.Target)).Select(s => s.Target).Distinct().Take(12).ToList();
        var parts = new List<string>();
        if (vars.Count > 0) parts.Add(I18n.T("v11.mod.ch.vars", ("list", string.Join(", ", vars))));
        if (doc.Hooks.Count > 0) parts.Add(I18n.T("v11.mod.ch.events", ("list", string.Join(", ", doc.Hooks.Select(h => h.Event).Distinct()))));
        if (body.Any(s => s.Kind == "say")) parts.Add(I18n.T("v11.mod.ch.say"));
        if (doc.Engine is { } e) parts.Add(I18n.T("v11.mod.ch.engine", ("engine", e)));
        if (doc.Natives.Count > 0) parts.Add(I18n.T("v11.mod.ch.native", ("list", string.Join(", ", doc.Natives.Select(n => n.Lang).Distinct()))));
        return string.Join(" · ", parts);
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
                if (name.StartsWith('/') || name.Contains("../") || name.Contains(':'))
                    found.Add(new("block", I18n.T("v11.mod.zip.path", ("path", name))));
                var ext = Path.GetExtension(name);
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
        if (Executables.Contains(ext)) found.Add(new("block", I18n.T("v11.mod.zip.exe", ("path", fileName))));
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
        var good = CheckModx("mod \"Ok\"\nlet bonus = 5\nhook start {\n  add gold bonus\n}\n");
        if (!good.Ok || !good.Changes.Contains("gold")) throw new Exception("clean mod blocked: " + string.Join("; ", good.Findings.Select(f => f.Text)));
        var bad = CheckModx("mod \"Bad\"\nhook start {\n  add gold 1\n}\nnative python {\n    import os\n    os.remove('save')\n}\n");
        if (bad.Ok) throw new Exception("os in python passed");
        var loop = CheckModx("mod \"Loop\"\nhook tick {\n  while true {\n    add x 1\n  }\n}\n");
        if (loop.Ok) throw new Exception("endless loop passed");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("../evil.txt").Open())) w.Write("x");
            using (var w = new StreamWriter(zip.CreateEntry("run.bat").Open())) w.Write("x");
        }
        var pkg = CheckPackage(ms.ToArray());
        if (pkg.Ok || pkg.Findings.Count(f => f.Level == "block") < 2) throw new Exception("zip slip or .bat passed");
        return "clean .modx passes; os/eval, endless loops, ../ and .bat are blocked";
    }
}
