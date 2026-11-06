using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>
/// Улика из лога, словами загрузчика. Kind — что случилось, Mod — с каким модом (как он назван в логе),
/// Need — что ему нужно (зависимость, версия Java), Detail — строка лога или ссылка.
/// </summary>
public sealed record CrashClue(string Kind, string Mod, string? Need = null, string? Detail = null);

/// <summary>
/// Что сделать по кнопке «Исправить». Kind: deps (поставить недостающее), enable / disable / update (моды по id),
/// loader (поставить или обновить загрузчик), memory (МБ), java (версия), url (открыть страницу).
/// Names — что ставить по имени, если зависимость нашлась только в логе.
/// </summary>
public sealed record CrashFix(string Kind, List<string> Ids, List<string>? Names = null, int Number = 0, string? Url = null);

/// <summary>Понятная причина: заголовок, объяснение и что сделает «Исправить» (Plan), если умеем.</summary>
public sealed record CrashCause(string Kind, string Title, string Text, CrashFix? Fix, string? Plan);

/// <summary>Итог разбора: упала ли игра, главная фраза, причины, лог.</summary>
public sealed record CrashReport(string GameId, bool Crashed, string Headline, List<CrashCause> Causes, string? LogPath, int? Code, long Ms, string? McId = null)
{
    public bool CanFix => Causes.Any(c => c.Fix is { Kind: not "url" });
}

/// <summary>
/// 24.0: «Почему игра упала». Читает свежие логи загрузчика (SMAPI, BepInEx, Modding API Hollow Knight,
/// Minecraft с отчётами о вылетах, Player.log Unity), находит в них понятные причины — нет нужного мода,
/// мод устарел после обновления игры, мод стоит дважды, загрузчик старый, не та Java, мало памяти — и
/// собирает план исправления. Разбор логов — чистые функции (Scan), их проверяют самотесты на настоящих строках.
/// </summary>
public static partial class CrashDoctor
{
    // ---------------------------------------------------------------- разбор лога

    [GeneratedRegex(@"^\[\d{1,2}:\d{2}:\d{2}\s+(TRACE|DEBUG|INFO|WARN|ALERT|ERROR)\s+([^\]]+)\]\s?(.*)$")] private static partial Regex SmapiLine();
    [GeneratedRegex(@"^\s*-\s+(.+?)\s+because\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex SmapiSkip();
    [GeneratedRegex(@"requires mods which aren't installed \((.+?)\)", RegexOptions.IgnoreCase)] private static partial Regex SmapiMissing();
    [GeneratedRegex(@"needs the '(.+?)' mod,? which is (?:disabled|not enabled)", RegexOptions.IgnoreCase)] private static partial Regex SmapiDisabled();
    [GeneratedRegex(@"needs newer versions? of (?:some )?mods?:?\s*(.+)", RegexOptions.IgnoreCase)] private static partial Regex SmapiDepOld();
    [GeneratedRegex(@"(?:needs|requires) (?:a newer version of SMAPI|SMAPI \S+ or (?:later|newer))", RegexOptions.IgnoreCase)] private static partial Regex SmapiOld();
    [GeneratedRegex(@"(?:needs|requires) (?:a newer version of (?:the game|Stardew Valley)|Stardew Valley \S+ or (?:later|newer))", RegexOptions.IgnoreCase)] private static partial Regex SmapiGameOld();
    [GeneratedRegex(@"no longer compatible|not compatible|isn't compatible|marked .*(?:broken|obsolete)|obsolete|for an older version|broken code", RegexOptions.IgnoreCase)] private static partial Regex SmapiOutdated();
    [GeneratedRegex(@"multiple copies|same (?:unique ?)?ID|more than once|duplicate", RegexOptions.IgnoreCase)] private static partial Regex SmapiDuplicate();
    [GeneratedRegex(@"You're running Stardew Valley \S+, but the oldest supported version is", RegexOptions.IgnoreCase)] private static partial Regex SmapiTooOldGame();
    [GeneratedRegex(@"^(.+?):\s*(https?://\S+?)\.?$")] private static partial Regex SmapiLink();
    [GeneratedRegex(@"nexusmods\.com/[a-z0-9]+/mods/(\d+)", RegexOptions.IgnoreCase)] private static partial Regex NexusModId();

    [GeneratedRegex(@"^\[(Debug|Info|Message|Warning|Error|Fatal)\s*:\s*([^\]]+)\]\s?(.*)$", RegexOptions.IgnoreCase)] private static partial Regex BepLine();
    [GeneratedRegex(@"Could not load \[(.+?)\] because it has missing dependencies:\s*(.+)$", RegexOptions.IgnoreCase)] private static partial Regex BepMissing();
    [GeneratedRegex(@"Could not load \[(.+?)\] because it is incompatible with:\s*(.+)$", RegexOptions.IgnoreCase)] private static partial Regex BepIncompatible();
    [GeneratedRegex(@"(?:Could not load|Plugin) \[(.+?)\] (?:because it (?:requires|targets)|targets) (?:a )?(?:newer|wrong) version of BepInEx", RegexOptions.IgnoreCase)] private static partial Regex BepOld();
    [GeneratedRegex(@"Skipping \[(.+?)\] because a newer version exists", RegexOptions.IgnoreCase)] private static partial Regex BepDuplicate();
    [GeneratedRegex(@"Error loading \[(.+?)\]\s*:\s*(.+)$", RegexOptions.IgnoreCase)] private static partial Regex BepError();

    [GeneratedRegex(@"^\[(INFO|FINE|DEBUG|WARN|ERROR)\]:\[([^\]]+)\]\s*-?\s*(.*)$", RegexOptions.IgnoreCase)] private static partial Regex HkLine();
    [GeneratedRegex(@"(?:Failed to load|Error (?:loading|initializing|initialising)) mod:?\s*[`'""]?([^`'""\n:]+?)[`'""]?(?:[:\n]|$)", RegexOptions.IgnoreCase)] private static partial Regex HkFailed();
    [GeneratedRegex(@"Could not load (?:file or )?assembly '([^',]+)", RegexOptions.IgnoreCase)] private static partial Regex HkAssembly();

    [GeneratedRegex(@"Mod '([^']+)' \(([\w.\-]+)\) \S+ requires (?:any version|version \S+(?: or later)?|\S+) of (?:mod )?'?([^'(,!]+?)'?\s*(?:\(([\w.\-]+)\))?,? which is missing", RegexOptions.IgnoreCase)] private static partial Regex FabricMissing();
    [GeneratedRegex(@"^\s*-\s*Install ([\w.\-]+), (?:any version|version)", RegexOptions.IgnoreCase | RegexOptions.Multiline)] private static partial Regex FabricInstall();
    [GeneratedRegex(@"Mod '([^']+)' \(([\w.\-]+)\) \S+ requires version \S+ (?:or later )?of (?:mod )?'?([^'(]+?)'? \(([\w.\-]+)\), but only the wrong version is present", RegexOptions.IgnoreCase)] private static partial Regex FabricDepOld();
    [GeneratedRegex(@"Mod '([^']+)' \(([\w.\-]+)\) \S+ is incompatible with (?:any version of |version \S+ of )?(?:mod )?'?([^'(]+?)'? \(([\w.\-]+)\)", RegexOptions.IgnoreCase)] private static partial Regex FabricIncompatible();
    [GeneratedRegex(@"Mod ID: '([\w.\-]+)', Requested by: '([\w.\-]+)', Expected range: '([^']*)', Actual version: '([^']*)'", RegexOptions.IgnoreCase)] private static partial Regex ForgeMissing();
    [GeneratedRegex(@"Mod (.+?) requires (\S+) \S+ or above\s*[\r\n]+\s*Currently, \S+ is not installed", RegexOptions.IgnoreCase)] private static partial Regex NeoMissing();
    [GeneratedRegex(@"compiled by a more recent version of the Java Runtime \(class file version (\d+)", RegexOptions.IgnoreCase)] private static partial Regex JavaClass();
    [GeneratedRegex(@"Mixin apply (?:for mod ([\w.\-]+) )?failed ([\w\-]+)\.mixins?\.json|\[[\w.\-]+\.mixins?\.json:[^\]]* from mod ([\w.\-]+)\]", RegexOptions.IgnoreCase)] private static partial Regex Mixin();
    [GeneratedRegex(@"Found duplicate mods?:?\s*[\r\n]*\s*Mod ID: '([\w.\-]+)'|[Dd]uplicate mod[s]? (?:found|detected)?[^\n]*?['`]([\w.\-]+)['`]", RegexOptions.IgnoreCase)] private static partial Regex McDuplicate();
    [GeneratedRegex(@"Suspected Mods?:\s*[\r\n]*[\t ]*([^\r\n(]+?)\s*\(([\w.\-]+)\)", RegexOptions.IgnoreCase)] private static partial Regex Suspected();
    [GeneratedRegex(@"driver does not appear to support OpenGL|Pixel format not accelerated|GLFW error 65542|Couldn't set pixel format|No OpenGL context", RegexOptions.IgnoreCase)] private static partial Regex Graphics();

    [GeneratedRegex(@"(MissingMethodException|MissingFieldException|TypeLoadException|EntryPointNotFoundException)", RegexOptions.IgnoreCase)] private static partial Regex Outdated();
    [GeneratedRegex(@"\s+v?\d[\w.\-+]*$")] private static partial Regex TrailingVersion();
    [GeneratedRegex(@"\s*\(v?[^)]*\)\s*$")] private static partial Regex TrailingBrackets();

    /// <summary>Убрать версию в конце: «More Suits 1.4.3» → «More Suits».</summary>
    public static string StripVersion(string name) => TrailingVersion().Replace(name.Trim(), "").Trim();

    /// <summary>Улики из текста лога (или нескольких логов подряд) — по загрузчику игры.</summary>
    public static List<CrashClue> Scan(LoaderKind loader, string text)
    {
        var clues = new List<CrashClue>();
        void Add(string kind, string mod, string? need = null, string? detail = null)
        {
            mod = mod.Trim();
            need = need?.Trim();
            if (clues.Any(c => c.Kind == kind && string.Equals(c.Mod, mod, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Need, need, StringComparison.OrdinalIgnoreCase))) return;
            if (detail is { Length: > 220 }) detail = detail[..220] + "…";
            clues.Add(new CrashClue(kind, mod, need, detail));
        }
        text = text.Replace("\r\n", "\n");
        switch (loader)
        {
            case LoaderKind.Smapi: ScanSmapi(text, Add); break;
            case LoaderKind.HkApi: ScanHk(text, Add); break;
            case LoaderKind.Minecraft: ScanMinecraft(text, Add); break;
            default: ScanBepInEx(text, Add); break;
        }
        // Нативный вылет Unity (Player.log) — причину в моде не назвать, но это вылет.
        if (text.Contains("Crash!!!", StringComparison.Ordinal) || text.Contains("========== OUTPUTTING STACK TRACE", StringComparison.Ordinal))
            Add("native", "");
        return clues.Take(40).ToList();
    }

    static void ScanSmapi(string text, Action<string, string, string?, string?> add)
    {
        var inSkipped = false;
        foreach (var raw in text.Split('\n'))
        {
            // Строки SMAPI-latest.txt: «[19:59:49 ERROR SMAPI]    - Mod 1.0 because …». Без метки — как в консоли.
            string level = "", source = "", message = raw;
            if (SmapiLine().Match(raw) is { Success: true } line) { level = line.Groups[1].Value; source = line.Groups[2].Value.Trim(); message = line.Groups[3].Value; }
            var trimmed = message.Trim();
            if (trimmed.StartsWith("Skipped mods", StringComparison.OrdinalIgnoreCase)) { inSkipped = true; continue; }
            // Раздел «Skipped mods» SMAPI пишет уровнем ERROR — строка другого уровня его закончила.
            // Раздел «Skipped mods» SMAPI пишет уровнем ERROR, подробности — уровнем TRACE; закончился он, когда пошли INFO/WARN.
            if (inSkipped && level is "INFO" or "WARN" or "ALERT") inSkipped = false;
            if (SmapiTooOldGame().IsMatch(message)) add("gameOld", "", null, trimmed);
            if (inSkipped && SmapiSkip().Match(message) is { Success: true } skip)
            {
                var mod = StripVersion(skip.Groups[1].Value);
                var why = skip.Groups[2].Value.Trim();
                if (SmapiMissing().Match(why) is { Success: true } m)
                    foreach (var need in m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // Известные SMAPI зависимости приходят со ссылкой: «Content Patcher: https://www.nexusmods.com/stardewvalley/mods/1915».
                        // Имя — отдельно, номер Nexus — в Detail («nexus:1915»), чтобы «Исправить» поставил именно его.
                        if (SmapiLink().Match(need) is { Success: true } link)
                            add("missing", mod, link.Groups[1].Value.Trim(), NexusModId().Match(link.Groups[2].Value) is { Success: true } nid ? "nexus:" + nid.Groups[1].Value : why);
                        else add("missing", mod, need, why);
                    }
                else if (SmapiDisabled().Match(why) is { Success: true } d) add("depOff", mod, d.Groups[1].Value, why);
                else if (SmapiDepOld().Match(why) is { Success: true } o)
                    foreach (var need in o.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) add("depOld", mod, TrailingBrackets().Replace(need.TrimEnd('.', ' '), ""), why);
                else if (SmapiOld().IsMatch(why)) add("loaderOld", mod, null, why);
                else if (SmapiGameOld().IsMatch(why)) add("gameOld", mod, null, why);
                else if (SmapiDuplicate().IsMatch(why)) add("duplicate", mod, null, why);
                else if (SmapiOutdated().IsMatch(why)) add("outdated", mod, null, why);
                else add("error", mod, null, why);
                continue;
            }
            // Ошибки самих модов: «[12:00:01 ERROR Content Patcher] …».
            if (level == "ERROR" && source != "" && !source.Equals("SMAPI", StringComparison.OrdinalIgnoreCase) && !source.Equals("game", StringComparison.OrdinalIgnoreCase)
                && !source.Equals("Stardew Valley", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 0)
                add(Outdated().IsMatch(message) ? "outdated" : "error", source, null, trimmed);
        }
    }

    static void ScanBepInEx(string text, Action<string, string, string?, string?> add)
    {
        foreach (var raw in text.Split('\n'))
        {
            string level = "", source = "", message = raw;
            if (BepLine().Match(raw) is { Success: true } line) { level = line.Groups[1].Value; source = line.Groups[2].Value.Trim(); message = line.Groups[3].Value.Trim(); }
            Match m;
            if ((m = BepMissing().Match(message)).Success)
            {
                var mod = StripVersion(m.Groups[1].Value);
                foreach (var dep in m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    add("missing", mod, TrailingBrackets().Replace(dep, ""), message);
            }
            else if ((m = BepIncompatible().Match(message)).Success)
                foreach (var other in m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    add("incompatible", StripVersion(m.Groups[1].Value), StripVersion(other), message);
            else if ((m = BepOld().Match(message)).Success) add("loaderOld", StripVersion(m.Groups[1].Value), null, message);
            else if ((m = BepDuplicate().Match(message)).Success) add("duplicate", StripVersion(m.Groups[1].Value), null, message);
            else if ((m = BepError().Match(message)).Success)
                add(Outdated().IsMatch(m.Groups[2].Value) ? "outdated" : "error", StripVersion(m.Groups[1].Value), null, m.Groups[2].Value);
            else if (level is "Error" or "Fatal" or "error" or "fatal" && source != "" && !source.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
                     && !source.Equals("Unity Log", StringComparison.OrdinalIgnoreCase) && !source.Equals("Preloader", StringComparison.OrdinalIgnoreCase))
                add(Outdated().IsMatch(message) ? "outdated" : "error", source, null, message);
        }
    }

    static void ScanHk(string text, Action<string, string, string?, string?> add)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            string level = "", source = "", message = raw;
            if (HkLine().Match(raw) is { Success: true } line) { level = line.Groups[1].Value.ToUpperInvariant(); source = line.Groups[2].Value.Trim(); message = line.Groups[3].Value.Trim(); }
            if (HkFailed().Match(message) is { Success: true } failed)
            {
                var mod = failed.Groups[1].Value.Trim();
                // Дальше (в той же строке или в следующих строках стека) — чего не хватило.
                var tail = string.Join('\n', lines.Skip(i).Take(6));
                if (HkAssembly().Match(tail) is { Success: true } asm && !asm.Groups[1].Value.Equals(mod, StringComparison.OrdinalIgnoreCase))
                    add("missing", mod, asm.Groups[1].Value, message);
                else add(Outdated().IsMatch(tail) ? "outdated" : "error", mod, null, message);
                continue;
            }
            if (level == "ERROR" && HkAssembly().Match(message) is { Success: true } a && !IsSystemAssembly(a.Groups[1].Value))
            {
                add("missing", source.Equals("API", StringComparison.OrdinalIgnoreCase) ? "" : source, a.Groups[1].Value, message);
                continue;
            }
            if (level == "ERROR" && source != "" && !source.Equals("API", StringComparison.OrdinalIgnoreCase) && message.Length > 0)
                add(Outdated().IsMatch(message) ? "outdated" : "error", source, null, message);
        }
    }

    static bool IsSystemAssembly(string name) =>
        name.StartsWith("System", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase);

    static void ScanMinecraft(string text, Action<string, string, string?, string?> add)
    {
        foreach (Match m in FabricMissing().Matches(text))
            add("missing", m.Groups[1].Value, m.Groups[4].Success && m.Groups[4].Value != "" ? m.Groups[4].Value : m.Groups[3].Value.Trim(), m.Value);
        foreach (Match m in FabricInstall().Matches(text))
            if (!FabricMissing().Matches(text).Any(x => (x.Groups[4].Success ? x.Groups[4].Value : x.Groups[3].Value).Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase)))
                add("missing", "", m.Groups[1].Value, m.Value.Trim());
        foreach (Match m in FabricDepOld().Matches(text)) add("depOld", m.Groups[1].Value, m.Groups[4].Value, m.Value);
        foreach (Match m in FabricIncompatible().Matches(text)) add("incompatible", m.Groups[1].Value, m.Groups[4].Value, m.Value);
        foreach (Match m in ForgeMissing().Matches(text))
            add(m.Groups[4].Value.Contains("MISSING", StringComparison.OrdinalIgnoreCase) ? "missing" : "depOld", m.Groups[2].Value, m.Groups[1].Value, m.Value);
        foreach (Match m in NeoMissing().Matches(text)) add("missing", m.Groups[1].Value, m.Groups[2].Value, m.Value.Split('\n')[0]);
        if (JavaClass().Match(text) is { Success: true } j && int.TryParse(j.Groups[1].Value, out var cls)) add("java", "", (cls - 44).ToString(), j.Value);
        else if (text.Contains("UnsupportedClassVersionError", StringComparison.Ordinal)) add("java", "", "21", "UnsupportedClassVersionError");
        if (text.Contains("java.lang.OutOfMemoryError", StringComparison.Ordinal)) add("memory", "", null, "java.lang.OutOfMemoryError");
        foreach (Match m in Mixin().Matches(text))
        {
            var mod = m.Groups[1].Success && m.Groups[1].Value != "" ? m.Groups[1].Value : m.Groups[3].Success && m.Groups[3].Value != "" ? m.Groups[3].Value : m.Groups[2].Value;
            add("error", mod, null, m.Value);
        }
        foreach (Match m in McDuplicate().Matches(text)) add("duplicate", m.Groups[1].Success && m.Groups[1].Value != "" ? m.Groups[1].Value : m.Groups[2].Value, null, m.Value.Trim());
        foreach (Match m in Suspected().Matches(text))
        {
            var id = m.Groups[2].Value;
            if (id is "minecraft" or "forge" or "neoforge" or "fabricloader" or "java" || m.Groups[1].Value.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase)) continue;
            add("error", id, null, m.Value.Trim());
        }
        if (Graphics().Match(text) is { Success: true } g) add("graphics", "", null, g.Value);
    }

    // ---------------------------------------------------------------- какие логи смотреть

    /// <summary>Player.log Unity-игры: LocalLow\компания\игра по *_Data\app.info.</summary>
    public static string? UnityPlayerLog(string gamePath)
    {
        try
        {
            var data = Directory.EnumerateDirectories(gamePath, "*_Data").FirstOrDefault();
            var info = data is null ? null : Path.Combine(data, "app.info");
            if (info is null || !File.Exists(info)) return null;
            var lines = File.ReadAllLines(info);
            if (lines.Length < 2) return null;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "AppData", "LocalLow", lines[0].Trim(), lines[1].Trim(), "Player.log");
        }
        catch { return null; }
    }

    /// <summary>Свежие логи этого запуска: файл и его хвост. Свежий — изменён после старта игры (с запасом).</summary>
    public static List<(string Path, string Text)> FreshLogs(GameState g, DateTime startedUtc, Minecraft.McInstance? mc = null)
    {
        var result = new List<(string, string)>();
        if (g.Path is null) return result;
        var since = startedUtc.AddSeconds(-5);
        void Take(string? path, int maxLines = 4000)
        {
            if (path is null || !File.Exists(path) || result.Any(r => r.Item1.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            try { if (File.GetLastWriteTimeUtc(path) < since) return; } catch { return; }
            var lines = Logs.Tail(path, maxLines);
            if (lines.Count > 0) result.Add((path, string.Join('\n', lines)));
        }
        var def = g.Def;
        if (def.Loader == LoaderKind.Minecraft)
        {
            // Отчёт о вылете — самое полезное: он и говорит, что случилось.
            var dir = (mc ?? Minecraft.Mc.Active)?.Dir ?? g.Path;
            var reports = Path.Combine(dir, "crash-reports");
            if (Directory.Exists(reports))
                Take(new DirectoryInfo(reports).EnumerateFiles("crash-*.txt").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName, 1500);
        }
        if (def.Loader == LoaderKind.Smapi)
            Take(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-crash.txt"));
        Take(def.Loader == LoaderKind.Minecraft && (mc ?? Minecraft.Mc.Active) is { } inst ? Path.Combine(inst.Dir, "logs", "latest.log") : Logs.PathFor(def, g.Path));
        if (def.Loader is not (LoaderKind.Minecraft or LoaderKind.Smapi)) Take(UnityPlayerLog(g.Path), 1500);
        return result;
    }

    /// <summary>Вылет точно был: SMAPI записал SMAPI-crash.txt, Minecraft — отчёт о вылете.</summary>
    static bool CrashFile(List<(string Path, string Text)> logs) =>
        logs.Any(l => Path.GetFileName(l.Path).Equals("SMAPI-crash.txt", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(Path.GetDirectoryName(l.Path) ?? "").Equals("crash-reports", StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- разбор целиком

    /// <summary>
    /// Разобрать последний запуск. exit — как закончился (null — «разобрать то, что в логах сейчас»: со вкладки «Проверка»).
    /// </summary>
    public static CrashReport Analyze(GameState g, Launcher.ExitInfo? exit, Minecraft.McInstance? mc = null)
    {
        var started = exit?.Started ?? DateTime.UtcNow.AddDays(-2);
        if (g.Def.IsMinecraft) mc ??= Minecraft.Mc.Active;
        var logs = FreshLogs(g, started, mc);
        var clues = new List<CrashClue>();
        foreach (var (_, text) in logs)
            foreach (var c in Scan(g.Def.Loader, text))
                if (!clues.Any(x => x.Kind == c.Kind && string.Equals(x.Mod, c.Mod, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Need, c.Need, StringComparison.OrdinalIgnoreCase)))
                    clues.Add(c);
        var code = exit?.Code;
        // Код ошибки — вылет, если это известный вылет Windows, в логах что-то есть или игра прожила недолго:
        // некоторые игры и при обычном выходе возвращают не 0 — после часа игры без единой улики это не вылет.
        var crashedByCode = exit is { ByUser: false } && code is int c0 && c0 != 0
            && (ExitCodeCause(c0) is not null || clues.Count > 0 || exit.Ms < 300_000);
        var strong = clues.Any(c => c.Kind is not ("error" or "native"));
        // Вылет: код ошибки, файл о вылете, нативный вылет Unity — или игра закрылась в первые полминуты,
        // а в логе серьёзные проблемы (нет зависимости, не та Java…). Просто ошибки в логе бывают и без вылета.
        var crashed = exit is null || crashedByCode || CrashFile(logs) || clues.Any(c => c.Kind == "native")
            || (exit is { ByUser: false } && exit.Ms < 30_000 && strong);
        var causes = g.Def.IsMinecraft ? ExplainMinecraft(clues, mc) : Explain(g, clues);
        if (code is int c1 && c1 != 0 && causes.Count == 0 && ExitCodeCause(c1) is { } byCode) causes.Add(byCode);
        if (causes.Count == 0 || causes.All(x => x.Fix is null))
            if (UpdatesCause(g, mc) is { } updates) causes.Add(updates);
        if (causes.Count == 0)
            causes.Add(new CrashCause("unknown", I18n.T("v24.cr.unknown"), I18n.T(code is int c2 && c2 != 0 ? "v24.cr.unknown.code" : "v24.cr.unknown.text", ("code", Hex(code ?? 0))), null, null));
        var headline = causes.Count > 1 ? I18n.T("v24.cr.many." + I18n.Plural(causes.Count, "one", "few", "many"), ("n", causes.Count)) : causes[0].Title;
        return new CrashReport(g.Def.Id, crashed, headline, causes.Take(8).ToList(), logs.FirstOrDefault(l => !l.Path.EndsWith("Player.log", StringComparison.OrdinalIgnoreCase)).Path ?? Logs.PathFor(g.Def, g.Path ?? ""), code, exit?.Ms ?? 0, mc?.Id);
    }

    public static string Hex(int code) => code < 0 || code > 0xFFFF ? "0x" + ((uint)code).ToString("X8") : code.ToString();

    /// <summary>Известные коды выхода Windows: чего не хватает или что это за вылет.</summary>
    public static CrashCause? ExitCodeCause(int code) => unchecked((uint)code) switch
    {
        0xC0000135 or 0xC0000142 or 0xC000007B => new CrashCause("dll", I18n.T("v24.cr.dll"), I18n.T("v24.cr.dll.text"),
            new CrashFix("url", [], Url: "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist"), I18n.T("v24.cr.dll.plan")),
        >= 0x80008080 and <= 0x800080A5 => new CrashCause("dotnet", I18n.T("v24.cr.dotnet"), I18n.T("v24.cr.dotnet.text"),
            new CrashFix("url", [], Url: "https://dotnet.microsoft.com/download/dotnet"), I18n.T("v24.cr.dll.plan")),
        0xC0000005 or 0xC0000409 or 0xC00000FD or 0xE0434352 => new CrashCause("native", I18n.T("v24.cr.native"), I18n.T("v24.cr.native.text", ("code", Hex(code))), null, null),
        _ => null,
    };

    /// <summary>Есть обновления модов — после патча игры чаще всего помогают они.</summary>
    static CrashCause? UpdatesCause(GameState g, Minecraft.McInstance? mc)
    {
        if (g.Def.IsMinecraft)
        {
            if (mc is not { } a) return null;
            var n = Minecraft.McContent.UpdateCount(a);
            return n == 0 ? null : new CrashCause("updates", I18n.T("v24.cr.updates", ("n", n)), I18n.T("v24.cr.updates.text"), new CrashFix("update", ["*"]), I18n.T("v24.cr.updates.plan"));
        }
        if (!ModUpdates.Found.TryGetValue(g.Def.Id, out var list) || list.Count == 0) return null;
        return new CrashCause("updates", I18n.T("v24.cr.updates", ("n", list.Count)), I18n.T("v24.cr.updates.text"),
            new CrashFix("update", list.Select(u => u.RecordId).ToList()), I18n.T("v24.cr.updates.plan"));
    }

    // ---------------------------------------------------------------- игры с модами в списке

    /// <summary>Запись мода по тому, как он назван в логе: имя, хвост id (Автор-Имя), UniqueID SMAPI, папка, GUID BepInEx.</summary>
    public static JsonObject? FindRecord(ModRegistry registry, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var clean = StripVersion(name);
        var n = Deps.Norm(clean);
        // GUID BepInEx «evaisa.lethallib» и UniqueID SMAPI «Pathoschild.ContentPatcher» — по последней части.
        var tail = clean.Contains('.') && !clean.Contains(' ') ? Deps.Norm(clean.Split('.').Last()) : null;
        var records = registry.List().Where(r => !r.Bool("missing") && r.Str("kind") != "preset").ToList();
        static bool Same(string? a, string? b) => a is { Length: > 0 } && b is { Length: > 0 } && a.Equals(b, StringComparison.OrdinalIgnoreCase);
        foreach (var r in records)
            if (Same(r.Str("uniqueId"), clean) || Same(r.Str("id"), clean)) return r;
        if (n.Length < 3) return null;
        foreach (var r in records)
        {
            var rn = Deps.Norm(r.Str("name"));
            var idTail = Deps.Norm((r.Str("id") ?? "").Split('-', ':').Last());
            var folder = Deps.Norm(r.Str("folder"));
            if (rn == n || idTail == n || folder == n) return r;
            if (tail is { Length: >= 4 } && (rn == tail || idTail == tail || folder == tail || Deps.Norm(r.Str("uniqueId")) == n)) return r;
        }
        foreach (var r in records)
        {
            var rn = Deps.Norm(r.Str("name"));
            if (rn.Length >= 5 && n.Length >= 5 && (rn.Contains(n) || n.Contains(rn))) return r;
        }
        return null;
    }

    static string Name(JsonObject r) => r.Str("name") ?? r.Str("id") ?? "?";

    /// <summary>Улики → понятные причины с планом. Моды ищем в списке установленных; обновления — из последней проверки.</summary>
    static List<CrashCause> Explain(GameState g, List<CrashClue> clues)
    {
        var causes = new List<CrashCause>();
        var registry = g.Registry;
        var loader = g.Def.LoaderName;
        var updates = ModUpdates.Found.TryGetValue(g.Def.Id, out var found) ? found : [];
        ModUpdate? UpdateFor(JsonObject? r) => r is null ? null : updates.FirstOrDefault(u => u.RecordId == r.Str("id"));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (g.Path is not null && !g.LoaderInstalled && !Vanilla.IsPurged(g.Def.Id))
            causes.Add(new CrashCause("noLoader", I18n.T("v24.cr.noLoader", ("loader", loader)), I18n.T("v24.cr.noLoader.text", ("loader", loader)),
                new CrashFix("loader", []), I18n.T("v24.cr.loader.plan", ("loader", loader))));

        // Недостающие зависимости: то, что видно по манифестам (Deps), и то, что назвал лог.
        // Нужный мод стоит, но выключен (лог говорит «нет зависимости») — это «включить», а не «поставить».
        var offFromLog = new List<Health.OffDependency>();
        if (registry is not null)
        {
            var missingDeps = Deps.Find(registry);
            var fromLog = clues.Where(c => c.Kind == "missing" && c.Need is not null).ToList();
            var names = new List<string>();
            // Что искать в каталоге: «nexus:1915», если SMAPI дал ссылку, иначе имя или UniqueID.
            var lookups = new List<string>();
            var lines = new List<string>();
            foreach (var group in missingDeps.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                lines.Add(I18n.T("v24.cr.missing.line", ("need", group.Key), ("mod", string.Join(", ", group.Select(x => x.Mod).Distinct()))));
            foreach (var c in fromLog)
            {
                if (FindRecord(registry, c.Need!) is { } have)
                {
                    // Уже стоит и включён (лог старый или имя другое) — не жалуемся; выключен — включим.
                    if (!have.Bool("enabled", true) && offFromLog.All(o => o.Id != have.Str("id")))
                        offFromLog.Add(new Health.OffDependency(have.Str("id")!, Name(have), FindRecord(registry, c.Mod) is { } needer ? Name(needer) : c.Mod));
                    continue;
                }
                if (missingDeps.Any(m => Deps.Norm(m.Name) == Deps.Norm(c.Need) || m.Id.Equals(c.Need, StringComparison.OrdinalIgnoreCase))) continue;
                if (names.Contains(c.Need!, StringComparer.OrdinalIgnoreCase)) continue;
                names.Add(c.Need!);
                lookups.Add(c.Detail is { } d && d.StartsWith("nexus:", StringComparison.Ordinal) ? d : c.Need!);
                var who = FindRecord(registry, c.Mod) is { } r ? Name(r) : c.Mod;
                lines.Add(who == "" ? I18n.T("v24.cr.missing.lineAny", ("need", Pretty(c.Need!))) : I18n.T("v24.cr.missing.line", ("need", Pretty(c.Need!)), ("mod", who)));
            }
            if (lines.Count > 0)
            {
                var all = missingDeps.Select(m => m.Name).Concat(names.Select(Pretty)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                causes.Add(new CrashCause("missing",
                    all.Count == 1 ? I18n.T("v24.cr.missing.one", ("need", all[0])) : I18n.T("v24.cr.missing.many", ("n", all.Count)),
                    string.Join("\n", lines.Take(6)),
                    new CrashFix("deps", [], lookups), I18n.T(all.Count == 1 ? "v24.cr.missing.plan.one" : "v24.cr.missing.plan")));
            }

            // Нужный мод стоит, но выключен.
            var off = Health.DisabledDependencies(registry);
            foreach (var o in offFromLog) if (off.All(x => x.Id != o.Id)) off.Add(o);
            foreach (var c in clues.Where(c => c.Kind == "depOff" && c.Need is not null))
                if (FindRecord(registry, c.Need!) is { } r && !r.Bool("enabled", true) && !off.Any(o => o.Id == r.Str("id")))
                    off.Add(new Health.OffDependency(r.Str("id")!, Name(r), c.Mod));
            if (off.Count > 0)
                causes.Add(new CrashCause("depOff",
                    off.Count == 1 ? I18n.T("v24.cr.depOff", ("need", off[0].Name)) : I18n.T("v24.cr.depOff.many", ("n", off.Count)),
                    string.Join("\n", off.Take(6).Select(o => I18n.T("health.depsOff.line", ("mod", o.Needer), ("dep", o.Name)))),
                    new CrashFix("enable", off.Select(o => o.Id).Distinct().ToList()), I18n.T(off.Count == 1 ? "v24.cr.depOff.plan.one" : "v24.cr.depOff.plan")));

            // Нужна более новая версия зависимости.
            foreach (var c in clues.Where(c => c.Kind == "depOld" && c.Need is not null))
            {
                var dep = FindRecord(registry, c.Need!);
                var who = FindRecord(registry, c.Mod) is { } r ? Name(r) : c.Mod;
                var title = I18n.T("v24.cr.depOld", ("mod", who), ("need", dep is null ? Pretty(c.Need!) : Name(dep)));
                if (dep is not null && UpdateFor(dep) is { } u && used.Add(u.RecordId))
                    causes.Add(new CrashCause("depOld", title, I18n.T("v24.cr.depOld.text"), new CrashFix("update", [u.RecordId]), I18n.T("v24.cr.update.plan", ("mod", u.Name), ("version", u.Latest))));
                else if (dep is null)
                    causes.Add(new CrashCause("depOld", title, I18n.T("v24.cr.depOld.text"), new CrashFix("deps", [], [c.Need!]), I18n.T("v24.cr.missing.plan")));
                else causes.Add(new CrashCause("depOld", title, I18n.T("v24.cr.depOld.text"), null, null));
            }
        }

        // Загрузчик и игра.
        if (clues.FirstOrDefault(c => c.Kind == "loaderOld") is { } old)
            causes.Add(new CrashCause("loaderOld", I18n.T("v24.cr.loaderOld", ("loader", loader)), I18n.T("v24.cr.loaderOld.text", ("loader", loader)) + (old.Mod != "" ? " " + I18n.T("v24.cr.loaderOld.mod", ("mod", old.Mod)) : ""),
                new CrashFix("loader", []), I18n.T("v24.cr.loaderOld.plan", ("loader", loader))));
        if (clues.Any(c => c.Kind == "gameOld"))
            causes.Add(new CrashCause("gameOld", I18n.T("v24.cr.gameOld"), I18n.T("v24.cr.gameOld.text", ("game", g.Def.Name), ("loader", loader)), null, null));

        // Мод стоит дважды.
        if (registry is not null)
        {
            var dupIds = new List<string>();
            var dupNames = new List<string>();
            foreach (var c in Conflicts.Find(registry).Where(c => c.Kind == "duplicate" && c.Loser is not null))
                if (!dupIds.Contains(c.Loser!)) { dupIds.Add(c.Loser!); dupNames.Add(c.WinnerName ?? c.A); }
            foreach (var c in clues.Where(c => c.Kind == "duplicate"))
                if (!dupNames.Any(n => Deps.Norm(n) == Deps.Norm(c.Mod))) dupNames.Add(c.Mod);
            if (dupNames.Count > 0)
                causes.Add(new CrashCause("duplicate",
                    dupNames.Count == 1 ? I18n.T("v24.cr.dup", ("mod", dupNames[0])) : I18n.T("v24.cr.dup.many", ("n", dupNames.Count)),
                    I18n.T("v24.cr.dup.text"), dupIds.Count > 0 ? new CrashFix("disable", dupIds) : null, dupIds.Count > 0 ? I18n.T("v24.cr.dup.plan") : null));
        }

        // Моды, которые не подходят друг к другу, устарели или падают с ошибкой.
        foreach (var c in clues.Where(c => c.Kind == "incompatible"))
        {
            var r = registry is null ? null : FindRecord(registry, c.Mod);
            var other = registry is null || c.Need is null ? null : FindRecord(registry, c.Need);
            if (r is null || !r.Bool("enabled", true) || !used.Add(r.Str("id")!)) continue;
            causes.Add(new CrashCause("incompatible", I18n.T("v24.cr.incompat", ("mod", Name(r)), ("other", other is null ? Pretty(c.Need ?? "") : Name(other))), I18n.T("v24.cr.incompat.text"),
                new CrashFix("disable", [r.Str("id")!]), I18n.T("v24.cr.disable.plan", ("mod", Name(r)))));
        }
        // Устаревшие моды и просто ошибки в логе. Ошибки бывают и в обычной игре (Content Patcher пишет их за
        // свои контент-паки постоянно), поэтому: есть серьёзная причина — ошибки не показываем вовсе; без неё
        // ошибку только показываем или обновляем. Выключаем лишь устаревший мод, на котором никто не держится:
        // выключить LethalLib или Content Patcher — значит сломать все моды, которые от них зависят.
        var strong = causes.Any(x => x.Kind is "missing" or "depOff" or "depOld" or "loaderOld" or "noLoader" or "duplicate" or "incompatible" or "gameOld");
        var graph = registry is null ? null : DepGraph.Build(registry);
        foreach (var kind in new[] { "outdated", "error" })
            foreach (var c in clues.Where(c => c.Kind == kind))
            {
                if (kind == "error" && strong) break;
                var r = registry is null ? null : FindRecord(registry, c.Mod);
                // Ошибки модов, которых в списке нет (сам загрузчик, игра), исправить нечем — их не показываем.
                if (r is null || !r.Bool("enabled", true) || r.Bool("missing") || !used.Add(r.Str("id")!)) continue;
                var name = Name(r);
                var id = r.Str("id")!;
                var title = I18n.T(kind == "outdated" ? "v24.cr.outdated" : "v24.cr.error", ("mod", name));
                var text = kind == "outdated" ? I18n.T("v24.cr.outdated.text") : c.Detail ?? "";
                var needed = graph?.NeededByOf(id).Any(d => d.Bool("enabled", true) && !d.Bool("missing")) == true;
                if (UpdateFor(r) is { } u)
                    causes.Add(new CrashCause(kind, title, text, new CrashFix("update", [u.RecordId]), I18n.T("v24.cr.update.plan", ("mod", name), ("version", u.Latest))));
                else if (kind == "outdated" && !needed)
                    causes.Add(new CrashCause(kind, title, text, new CrashFix("disable", [id]), I18n.T("v24.cr.disable.plan", ("mod", name))));
                else causes.Add(new CrashCause(kind, title, text, null, null));
                if (causes.Count(x => x.Kind is "outdated" or "error") >= 5) break;
            }
        if (clues.Any(c => c.Kind == "native") && !causes.Any(c => c.Fix is not null))
            causes.Add(new CrashCause("native", I18n.T("v24.cr.native"), I18n.T("v24.cr.native.unity"), null, null));
        return causes;
    }

    /// <summary>«evaisa.lethallib» → «lethallib», «Pathoschild.ContentPatcher» → «ContentPatcher»: в заголовке короче и понятнее.</summary>
    public static string Pretty(string need)
    {
        need = need.Trim();
        if (need.Contains('.') && !need.Contains(' ') && need.Split('.') is { Length: >= 2 } parts && parts[^1].Length >= 3 && !parts[^1].All(char.IsDigit))
            need = parts[^1];
        // GUID пишут маленькими буквами («lethallib») — в заголовке с заглавной.
        return need.Length > 1 && need.All(c => !char.IsLetter(c) || char.IsLower(c)) ? char.ToUpperInvariant(need[0]) + need[1..] : need;
    }

    // ---------------------------------------------------------------- Minecraft

    /// <summary>Мод сборки по id из лога: смотрим fabric.mod.json / mods.toml внутри .jar, затем имя и файл.</summary>
    public static Minecraft.McItem? FindJar(Minecraft.McInstance i, string idOrName, List<(string? Id, Minecraft.McItem Item)>? jars = null)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        jars ??= Jars(i);
        if (jars.FirstOrDefault(j => j.Id is { } id && id.Equals(idOrName, StringComparison.OrdinalIgnoreCase)).Item is { } byId) return byId;
        var n = Deps.Norm(idOrName);
        if (n.Length < 3) return null;
        return jars.Select(j => j.Item).FirstOrDefault(x => Deps.Norm(x.Name) == n || n.Length >= 4 && Deps.Norm(Path.GetFileNameWithoutExtension(x.Key)).StartsWith(n, StringComparison.Ordinal));
    }

    /// <summary>id самой игры, Java и загрузчиков — это не моды с Modrinth.</summary>
    static bool McCore(string id) => id.ToLowerInvariant() is "minecraft" or "java" or "fabricloader" or "fabric-loader" or "forge" or "neoforge"
        or "quilt_loader" or "quilt-loader" or "fml" or "javafml";

    /// <summary>Моды сборки с id из .jar — один раз на разбор (файлов бывают сотни).</summary>
    static List<(string? Id, Minecraft.McItem Item)> Jars(Minecraft.McInstance i) =>
        Minecraft.McContent.List(i, "mod").Select(item => (Minecraft.McContent.ReadJar(Path.Combine(i.Folder("mod"), item.File))?.Id, item)).ToList();

    static List<CrashCause> ExplainMinecraft(List<CrashClue> clues, Minecraft.McInstance? mc)
    {
        var causes = new List<CrashCause>();
        var a = mc;
        if (a is null) return causes;
        var jars = Jars(a);
        Minecraft.McItem? FindJar(Minecraft.McInstance i, string idOrName) => CrashDoctor.FindJar(i, idOrName, jars);
        var updates = Minecraft.McContent.Updates(a);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var missing = clues.Where(c => c.Kind == "missing" && c.Need is not null && !McCore(c.Need))
            .Where(c => FindJar(a, c.Need!) is not { Enabled: true }).Select(c => c).ToList();
        if (missing.Count > 0)
        {
            var needs = missing.Select(c => c.Need!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            causes.Add(new CrashCause("missing",
                needs.Count == 1 ? I18n.T("v24.cr.missing.one", ("need", needs[0])) : I18n.T("v24.cr.missing.many", ("n", needs.Count)),
                string.Join("\n", missing.Take(6).Select(c => c.Mod == "" ? I18n.T("v24.cr.missing.lineAny", ("need", c.Need!)) : I18n.T("v24.cr.missing.line", ("need", c.Need!), ("mod", c.Mod)))),
                new CrashFix("deps", [], needs), I18n.T(needs.Count == 1 ? "v24.cr.mc.missing.plan.one" : "v24.cr.mc.missing.plan")));
        }
        // Мод требует другую версию самого Minecraft или загрузчика (Forge: «Mod ID: 'minecraft', … Actual version: '1.20.1'»):
        // ставить тут нечего — мод для другой версии игры (обновить или выключить его) или нужен новее загрузчик.
        var loaderAsked = false;
        foreach (var c in clues.Where(c => c.Kind is "depOld" or "missing" && c.Need is not null && McCore(c.Need)))
        {
            if (c.Need is "java") continue;
            if (c.Need is "minecraft")
            {
                var item = FindJar(a, c.Mod);
                if (item is null || !item.Enabled || !used.Add(item.Key)) continue;
                var title = I18n.T("v24.cr.mc.version", ("mod", item.Name), ("version", a.GameVersion));
                if (updates.FirstOrDefault(u => u.Item.Key == item.Key) is { } u)
                    causes.Add(new CrashCause("mcVersion", title, I18n.T("v24.cr.mc.version.text", ("version", a.GameVersion)), new CrashFix("update", [item.Key]), I18n.T("v24.cr.update.plan", ("mod", item.Name), ("version", u.Latest.Number))));
                else causes.Add(new CrashCause("mcVersion", title, I18n.T("v24.cr.mc.version.text", ("version", a.GameVersion)), new CrashFix("disable", [item.Key]), I18n.T("v24.cr.disable.plan", ("mod", item.Name))));
            }
            else if (!loaderAsked && a.Modded)
            {
                loaderAsked = true;
                causes.Add(new CrashCause("mcLoader", I18n.T("v24.cr.mc.loaderOld", ("loader", a.LoaderTitle)), I18n.T("v24.cr.mc.loaderOld.text", ("loader", a.LoaderTitle), ("mod", c.Mod)),
                    new CrashFix("mcloader", []), I18n.T("v24.cr.mc.loaderOld.plan", ("loader", a.LoaderTitle), ("version", a.GameVersion))));
            }
        }
        foreach (var c in clues.Where(c => c.Kind == "depOld" && c.Need is not null && !McCore(c.Need)))
        {
            var dep = FindJar(a, c.Need!);
            var title = I18n.T("v24.cr.depOld", ("mod", c.Mod), ("need", dep?.Name ?? c.Need!));
            if (dep is not null && updates.FirstOrDefault(u => u.Item.Key == dep.Key) is { } u && used.Add(dep.Key))
                causes.Add(new CrashCause("depOld", title, I18n.T("v24.cr.depOld.text"), new CrashFix("update", [dep.Key]), I18n.T("v24.cr.update.plan", ("mod", dep.Name), ("version", u.Latest.Number))));
            else causes.Add(new CrashCause("depOld", title, I18n.T("v24.cr.depOld.text"), dep is null ? new CrashFix("deps", [], [c.Need!]) : null, dep is null ? I18n.T("v24.cr.mc.missing.plan") : null));
        }
        if (clues.FirstOrDefault(c => c.Kind == "java") is { } java && int.TryParse(java.Need, out var major))
            causes.Add(new CrashCause("java", I18n.T("v24.cr.java", ("n", major)), I18n.T("v24.cr.java.text"), new CrashFix("java", [], Number: major), I18n.T("v24.cr.java.plan", ("n", major))));
        if (clues.Any(c => c.Kind == "memory"))
        {
            var total = Minecraft.Mc.TotalMemoryMb();
            var next = Math.Min(Math.Max(a.MemoryMb + 2048, 6144), Math.Max(4096, total / 2));
            causes.Add(next > a.MemoryMb
                ? new CrashCause("memory", I18n.T("v24.cr.memory"), I18n.T("v24.cr.memory.text", ("mb", a.MemoryMb)), new CrashFix("memory", [], Number: next), I18n.T("v24.cr.memory.plan", ("mb", next)))
                : new CrashCause("memory", I18n.T("v24.cr.memory"), I18n.T("v24.cr.memory.full", ("mb", a.MemoryMb)), null, null));
        }
        var dups = clues.Where(c => c.Kind == "duplicate").Select(c => c.Mod).Where(m => m != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (dups.Count > 0)
            causes.Add(new CrashCause("duplicate", dups.Count == 1 ? I18n.T("v24.cr.dup", ("mod", dups[0])) : I18n.T("v24.cr.dup.many", ("n", dups.Count)), I18n.T("v24.cr.mc.dup.text"), null, null));
        foreach (var c in clues.Where(c => c.Kind is "incompatible" or "error" or "outdated"))
        {
            var item = FindJar(a, c.Mod);
            if (item is null || !item.Enabled || !used.Add(item.Key)) continue;
            var title = c.Kind == "incompatible" ? I18n.T("v24.cr.incompat", ("mod", item.Name), ("other", c.Need ?? "")) : I18n.T("v24.cr.error", ("mod", item.Name));
            var text = c.Kind == "incompatible" ? I18n.T("v24.cr.incompat.text") : c.Detail ?? "";
            if (c.Kind != "incompatible" && updates.FirstOrDefault(u => u.Item.Key == item.Key) is { } u)
                causes.Add(new CrashCause(c.Kind, title, text, new CrashFix("update", [item.Key]), I18n.T("v24.cr.update.plan", ("mod", item.Name), ("version", u.Latest.Number))));
            else causes.Add(new CrashCause(c.Kind, title, text, new CrashFix("disable", [item.Key]), I18n.T("v24.cr.disable.plan", ("mod", item.Name))));
            if (causes.Count(x => x.Kind is "incompatible" or "error" or "outdated") >= 5) break;
        }
        if (clues.Any(c => c.Kind == "graphics"))
            causes.Add(new CrashCause("graphics", I18n.T("v24.cr.graphics"), I18n.T("v24.cr.graphics.text"), null, null));
        return causes;
    }

    // ---------------------------------------------------------------- самотесты на настоящих строках

    /// <summary>SMAPI-latest.txt: строки с меткой времени, раздел «Skipped mods» и ошибки модов.</summary>
    [SelfTest]
    static string ReadsSmapiLog()
    {
        var log = """
            [19:59:48 INFO  SMAPI] SMAPI 4.1.10 with Stardew Valley 1.6.15 build 24356 on Microsoft Windows 11 Pro
            [19:59:49 ERROR SMAPI] Skipped mods
            [19:59:49 ERROR SMAPI] --------------------------------------------------
            [19:59:49 ERROR SMAPI]    These mods could not be added to your game.
            [19:59:49 ERROR SMAPI]
            [19:59:49 ERROR SMAPI]    - Better Ranching 1.8.2 because it's no longer compatible. Please check for a newer version at https://www.nexusmods.com/stardewvalley/mods/859
            [19:59:49 TRACE SMAPI]         (System.IO.FileNotFoundException: Could not load file or assembly 'BetterRanching')
            [19:59:49 ERROR SMAPI]    - Ridgeside Village 2.5.17 because it requires mods which aren't installed (Rafseazz.RSVCore, spacechase0.JsonAssets).
            [19:59:49 ERROR SMAPI]    - Stardew Valley Expanded 1.15.8 because it requires mods which aren't installed (Content Patcher: https://www.nexusmods.com/stardewvalley/mods/1915).
            [19:59:49 ERROR SMAPI]    - Tractor Mod 4.17.0 because it needs a newer version of SMAPI. Please update SMAPI to the latest version to use this mod.
            [19:59:49 ERROR SMAPI]    - Lookup Anything 1.45.0 because it needs newer versions of some mods: Content Patcher (needs 2.0.0 or later).
            [19:59:49 ERROR SMAPI]    - Automate because you have multiple copies of this mod installed.
            [19:59:50 INFO  SMAPI] Loaded 12 mods:
            [20:01:12 ERROR Content Patcher] Can't apply data patch "Farm Edits > Include" to Data/Locations: target not found.
            [20:01:13 ERROR SMAPI] An error occurred in the base update loop: NullReferenceException
            """;
        var clues = Scan(LoaderKind.Smapi, log);
        string Has(string kind, string mod, string? need = null) =>
            clues.Any(c => c.Kind == kind && c.Mod == mod && (need is null || c.Need == need)) ? "" : $"{kind}:{mod}:{need}; ";
        var fail = Has("outdated", "Better Ranching") + Has("missing", "Ridgeside Village", "Rafseazz.RSVCore") + Has("missing", "Ridgeside Village", "spacechase0.JsonAssets")
            + Has("loaderOld", "Tractor Mod") + Has("depOld", "Lookup Anything", "Content Patcher") + Has("duplicate", "Automate") + Has("error", "Content Patcher");
        if (clues.Any(c => c.Mod.Equals("SMAPI", StringComparison.OrdinalIgnoreCase))) fail += "SMAPI itself reported as a mod; ";
        if (clues.First(c => c.Kind == "outdated").Detail?.Contains("stardewvalley/mods/859") != true) fail += "reason lost; ";
        // Зависимость со ссылкой: имя без ссылки, номер Nexus — для «Исправить»; строка TRACE не обрывает раздел.
        if (clues.FirstOrDefault(c => c.Kind == "missing" && c.Mod == "Stardew Valley Expanded") is not { Need: "Content Patcher", Detail: "nexus:1915" })
            fail += "linked dependency: " + string.Join(" | ", clues.Where(c => c.Mod == "Stardew Valley Expanded")) + "; ";
        // Вкладка «Лог» и помощник (Logs.Parse) тоже должны видеть пропущенные моды в настоящем логе с метками времени.
        var old = Logs.Parse(GameCatalog.ById("stardew-valley")!, log);
        if (!old.Any(i => i.Mod == "Ridgeside Village") || !old.Any(i => i.Mod == "Content Patcher")) fail += "Logs.Parse misses timestamped SMAPI lines: " + string.Join(", ", old.Select(i => i.Mod)) + "; ";
        if (fail != "") throw new Exception(fail);
        return $"{clues.Count} clues: outdated, 3 missing deps (one with a Nexus link), old SMAPI, old dependency, duplicate, mod error";
    }

    /// <summary>BepInEx (Lethal Company): нет зависимости, несовместимы, дубль, ошибка загрузки, ошибка мода.</summary>
    [SelfTest]
    static string ReadsBepInExLog()
    {
        var log = """
            [Info   :   BepInEx] BepInEx 5.4.21.0 - Lethal Company (11/5/2026 8:00:00 PM)
            [Error  :   BepInEx] Could not load [More Suits 1.4.3] because it has missing dependencies: evaisa.lethallib (v0.10.1 or newer)
            [Error  :   BepInEx] Could not load [LateCompany 1.0.18] because it is incompatible with: LethalCompanyVR
            [Warning:   BepInEx] Skipping [MoreCompany 1.10.1] because a newer version exists (1.11.0)
            [Error  :   BepInEx] Error loading [LethalThings 0.10.7] : Method not found: 'void GameNetcodeStuff.PlayerControllerB.DamagePlayer()' MissingMethodException
            [Error  : ShipLoot] NullReferenceException: Object reference not set to an instance of an object
            [Error  : Unity Log] Some engine noise
            """;
        var clues = Scan(LoaderKind.Bepinex, log);
        string Has(string kind, string mod, string? need = null) =>
            clues.Any(c => c.Kind == kind && c.Mod == mod && (need is null || c.Need == need)) ? "" : $"{kind}:{mod}:{need}; ";
        var fail = Has("missing", "More Suits", "evaisa.lethallib") + Has("incompatible", "LateCompany", "LethalCompanyVR") + Has("duplicate", "MoreCompany")
            + Has("outdated", "LethalThings") + Has("error", "ShipLoot");
        if (clues.Any(c => c.Mod is "Unity Log" or "BepInEx")) fail += "engine lines reported as mods; ";
        if (Pretty("evaisa.lethallib") != "Lethallib" || Pretty("Pathoschild.ContentPatcher") != "ContentPatcher") fail += "GUID not shortened; ";
        if (fail != "") throw new Exception(fail);
        return $"{clues.Count} clues: missing LethalLib, incompatible, duplicate, outdated after game patch, mod error";
    }

    /// <summary>Hollow Knight (ModLog.txt): мод не загрузился без зависимости, ошибка мода.</summary>
    [SelfTest]
    static string ReadsHollowKnightLog()
    {
        var log = """
            [INFO]:[API] - Starting mod loading
            [ERROR]:[API] - Failed to load Mod `Pale Court`
            System.IO.FileNotFoundException: Could not load file or assembly 'SFCore, Version=1.5.0.0, Culture=neutral, PublicKeyToken=null' or one of its dependencies.
            [ERROR]:[API] - Error: System.IO.FileNotFoundException: Could not load file or assembly 'Satchel, Version=0.8.12.0, Culture=neutral'
            [ERROR]:[Benchwarp] - Exception in HeroController.Start: System.MissingMethodException: Method not found: 'Void HeroController.Foo()'
            [INFO]:[API] - Finished loading mods
            """;
        var clues = Scan(LoaderKind.HkApi, log);
        string Has(string kind, string mod, string? need = null) =>
            clues.Any(c => c.Kind == kind && c.Mod == mod && (need is null || c.Need == need)) ? "" : $"{kind}:{mod}:{need}; ";
        var fail = Has("missing", "Pale Court", "SFCore") + Has("missing", "", "Satchel") + Has("outdated", "Benchwarp");
        if (clues.Any(c => c.Mod == "API")) fail += "API reported as a mod; ";
        if (fail != "") throw new Exception(fail + " got: " + string.Join(" | ", clues));
        return $"{clues.Count} clues: Pale Court needs SFCore, Satchel missing, Benchwarp outdated";
    }

    /// <summary>Minecraft: Fabric и Forge без зависимостей, не та Java, мало памяти, Mixin, подозреваемый мод, драйвер.</summary>
    [SelfTest]
    static string ReadsMinecraftLog()
    {
        var fabric = """
            net.fabricmc.loader.impl.FormattedException: Mod resolution encountered an incompatible mod set!
            A potential solution has been determined, this may resolve your problem:
            	 - Install fabric-api, any version.
            More details:
            	 - Mod 'Sodium Extra' (sodium-extra) 0.5.4+mc1.20.4 requires any version of mod 'Sodium' (sodium), which is missing!
            	 - Mod 'Iris' (iris) 1.6.11 requires version 0.5.4 or later of mod 'Sodium' (sodium), but only the wrong version is present: 0.5.3!
            	 - Mod 'OptiFabric' (optifabric) 1.13.24 is incompatible with any version of mod 'Sodium' (sodium), but a matching version is present: 0.5.3!
            Caused by: org.spongepowered.asm.mixin.transformer.throwables.MixinTransformerError: Mixin apply for mod journeymap failed journeymap.mixins.json:client.MixinWorld
            java.lang.OutOfMemoryError: Java heap space
            """;
        var forge = """
            Missing or unsupported mandatory dependencies:
            	Mod ID: 'geckolib', Requested by: 'mutantmonsters', Expected range: '[4.4,)', Actual version: '[MISSING]'
            Exception: java.lang.UnsupportedClassVersionError: dev/x/Mod has been compiled by a more recent version of the Java Runtime (class file version 65.0), this version of the Java Runtime only recognizes class file versions up to 61.0
            -- Head --
            Thread: Render thread
            Suspected Mods:
            	Create (create), Version: 0.5.1.f
            GLFW error 65542: WGL: The driver does not appear to support OpenGL
            """;
        var clues = Scan(LoaderKind.Minecraft, fabric).Concat(Scan(LoaderKind.Minecraft, forge)).ToList();
        string Has(string kind, string mod, string? need = null) =>
            clues.Any(c => c.Kind == kind && c.Mod == mod && (need is null || c.Need == need)) ? "" : $"{kind}:{mod}:{need}; ";
        var fail = Has("missing", "", "fabric-api") + Has("missing", "Sodium Extra", "sodium") + Has("depOld", "Iris", "sodium") + Has("incompatible", "OptiFabric", "sodium")
            + Has("error", "journeymap") + Has("memory", "") + Has("missing", "mutantmonsters", "geckolib") + Has("java", "", "21") + Has("error", "create") + Has("graphics", "");
        if (fail != "") throw new Exception(fail + " got: " + string.Join(" | ", clues));
        return $"{clues.Count} clues: Fabric API and Sodium missing, old Sodium, OptiFabric conflict, Mixin, memory, GeckoLib, Java 21, Create, driver";
    }

    /// <summary>Мод из лога находится в списке: по имени с версией, GUID BepInEx, UniqueID SMAPI и хвосту id.</summary>
    [SelfTest]
    static string FindsModsByLogNames()
    {
        // Список модов хранится по игре, а Lethal Company заняли другие проверки — берём игру, которой они не трогают.
        var dir = Path.Combine(Paths.DataDir, "crash-test", "Content Warning");
        Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "plugins"));
        var registry = new ModRegistry(GameCatalog.ById("content-warning")!, dir);
        registry.Add(new JsonObject { ["id"] = "Evaisa-LethalLib", ["name"] = "LethalLib", ["folder"] = "Evaisa-LethalLib", ["source"] = "thunderstore" });
        registry.Add(new JsonObject { ["id"] = "x753-More_Suits", ["name"] = "More Suits", ["folder"] = "x753-More_Suits", ["source"] = "thunderstore" });
        registry.Add(new JsonObject { ["id"] = "nexus:stardewvalley:1915", ["name"] = "Content Patcher", ["uniqueId"] = "Pathoschild.ContentPatcher", ["folder"] = "ContentPatcher" });
        string Id(string name) => FindRecord(registry, name)?.Str("id") ?? "-";
        var fail = "";
        if (Id("evaisa.lethallib") != "Evaisa-LethalLib") fail += "GUID; ";
        if (Id("More Suits 1.4.3") != "x753-More_Suits") fail += $"name with version → {Id("More Suits 1.4.3")}; ";
        if (Id("Pathoschild.ContentPatcher") != "nexus:stardewvalley:1915") fail += "UniqueID; ";
        if (Id("Content Patcher") != "nexus:stardewvalley:1915") fail += "plain name; ";
        if (Id("SMAPI") != "-" || Id("") != "-") fail += "false match; ";
        if (fail != "") throw new Exception(fail);
        return "GUID, name with version, UniqueID and name all find their mod; SMAPI finds nothing";
    }

    /// <summary>Коды выхода Windows: не хватает DLL, .NET, системный вылет.</summary>
    [SelfTest]
    static string ExplainsExitCodes()
    {
        var dll = ExitCodeCause(unchecked((int)0xC0000135));
        var net = ExitCodeCause(unchecked((int)0x80008096));
        var av = ExitCodeCause(unchecked((int)0xC0000005));
        if (dll?.Kind != "dll" || dll.Fix?.Url is null || net?.Kind != "dotnet" || av?.Kind != "native" || ExitCodeCause(1) is not null) throw new Exception("exit codes misread");
        if (Hex(unchecked((int)0xC0000005)) != "0xC0000005" || Hex(1) != "1") throw new Exception("hex wrong");
        return "0xC0000135 → missing DLL, 0x80008096 → .NET, 0xC0000005 → native crash, 1 → nothing special";
    }
}
