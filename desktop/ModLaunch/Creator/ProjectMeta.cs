using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Creator;

/// <summary>Последняя сборка: когда, получилось ли, сколько предупреждений, какой архив.</summary>
public sealed record BuildNote(DateTime Time, bool Ok, int Warnings, string? Zip, string Version);

/// <summary>Последняя установка в игру.</summary>
public sealed record InstallNote(DateTime Time, string Game, string Version);

/// <summary>Последняя проверка в игре: как закончилась и что мод оставил в логе загрузчика.</summary>
public sealed record TestNote(DateTime Time, bool Ok, int? Exit, double Seconds, bool ByUser, string Version, List<string> Lines, bool FreshLog = true, int Errors = 0);

/// <summary>Выпуск: какая версия и куда ушла (hub, market).</summary>
public sealed record ReleaseNote(string Version, DateTime Time, string Where);

/// <summary>
/// 12.0: project.json рядом с main.mls — история проекта (сборка, установка, проверка в игре,
/// выпуски) и куда он выложен (hubId, listingId). Главным остаётся скрипт: здесь только то,
/// чего из него не узнать. Неизвестные поля сохраняются как есть.
/// </summary>
public sealed class ProjectMeta
{
    public const string FileName = "project.json";
    const int KeepReleases = 50, KeepDays = 30, KeepLines = 40;

    public string? HubId;
    public string? ListingId;
    public BuildNote? LastBuild;
    public InstallNote? LastInstall;
    public TestNote? LastTest;
    public List<ReleaseNote> Releases = [];
    /// <summary>Загрузки в Hub на конец дня (yyyy-MM-dd → всего) — график без новых правил Firestore.</summary>
    public SortedDictionary<string, long> Downloads = new(StringComparer.Ordinal);
    JsonObject _extra = new();

    static readonly object Gate = new();
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string PathOf(string dir) => Path.Combine(dir, FileName);

    /// <summary>Прочитать; нет файла или он испорчен — пустая история (проект от этого не ломается).</summary>
    public static ProjectMeta Load(string dir)
    {
        try { return File.Exists(PathOf(dir)) ? FromJson(JsonNode.Parse(File.ReadAllText(PathOf(dir))) as JsonObject) : new(); }
        catch { return new(); }
    }

    public void Save(string dir)
    {
        if (!Directory.Exists(dir)) return;
        // Сначала во временный файл: оборванная запись не должна съесть историю.
        var tmp = PathOf(dir) + ".tmp";
        File.WriteAllText(tmp, ToJson().ToJsonString(Pretty));
        File.Move(tmp, PathOf(dir), true);
    }

    /// <summary>Прочитать, поменять и записать — одним шагом (сборка и выход игры могут прийти почти разом).</summary>
    public static ProjectMeta Update(string dir, Action<ProjectMeta> change)
    {
        lock (Gate)
        {
            var m = Load(dir);
            change(m);
            try { m.Save(dir); } catch { }
            return m;
        }
    }

    // ---------------------------------------------------------------- JSON

    static string Iso(DateTime t) => t.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    static DateTime Time(JsonNode? o, string key) =>
        DateTime.TryParse(o.Str(key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : DateTime.MinValue;

    static int? Int(JsonNode? o, string key) => o is JsonObject j && j[key] is JsonValue ? (int)o.Long(key) : null;

    public JsonObject ToJson()
    {
        var o = (JsonObject)_extra.DeepClone();
        void Put(string key, JsonNode? value) { if (value is null) o.Remove(key); else o[key] = value; }
        Put("hubId", HubId);
        Put("listingId", ListingId);
        Put("lastBuild", LastBuild is { } b ? new JsonObject { ["time"] = Iso(b.Time), ["ok"] = b.Ok, ["warnings"] = b.Warnings, ["zip"] = b.Zip, ["version"] = b.Version } : null);
        Put("lastInstall", LastInstall is { } i ? new JsonObject { ["time"] = Iso(i.Time), ["game"] = i.Game, ["version"] = i.Version } : null);
        Put("lastTest", LastTest is { } t ? new JsonObject
        {
            ["time"] = Iso(t.Time), ["ok"] = t.Ok, ["exit"] = t.Exit, ["seconds"] = Math.Round(t.Seconds, 1), ["byUser"] = t.ByUser,
            ["version"] = t.Version, ["freshLog"] = t.FreshLog, ["errors"] = t.Errors, ["lines"] = new JsonArray(t.Lines.Take(KeepLines).Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()),
        } : null);
        o["releases"] = new JsonArray(Releases.TakeLast(KeepReleases).Select(r => (JsonNode)new JsonObject { ["version"] = r.Version, ["time"] = Iso(r.Time), ["where"] = r.Where }).ToArray());
        var downloads = new JsonObject();
        foreach (var (day, n) in Downloads.TakeLast(KeepDays)) downloads[day] = n;
        o["downloads"] = downloads;
        return o;
    }

    public static ProjectMeta FromJson(JsonObject? o)
    {
        var m = new ProjectMeta();
        if (o is null) return m;
        m._extra = (JsonObject)o.DeepClone();
        m.HubId = o.Str("hubId") is { Length: > 0 } h ? h : null;
        m.ListingId = o.Str("listingId") is { Length: > 0 } l ? l : null;
        if (o["lastBuild"] is JsonObject b)
            m.LastBuild = new BuildNote(Time(b, "time"), b.Bool("ok"), (int)b.Long("warnings"), b.Str("zip"), b.Str("version") ?? "");
        if (o["lastInstall"] is JsonObject i)
            m.LastInstall = new InstallNote(Time(i, "time"), i.Str("game") ?? "", i.Str("version") ?? "");
        if (o["lastTest"] is JsonObject t)
            m.LastTest = new TestNote(Time(t, "time"), t.Bool("ok"), Int(t, "exit"), t.Double("seconds"), t.Bool("byUser"), t.Str("version") ?? "",
                t.Arr("lines").Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").Take(KeepLines).ToList(), t.Bool("freshLog", true), (int)t.Long("errors"));
        foreach (var r in o.Arr("releases").OfType<JsonObject>())
            m.Releases.Add(new ReleaseNote(r.Str("version") ?? "", Time(r, "time"), r.Str("where") ?? "hub"));
        if (o["downloads"] is JsonObject d)
            foreach (var (day, _) in d) m.Downloads[day] = d.Long(day);
        return m;
    }

    // ---------------------------------------------------------------- состояние

    /// <summary>
    /// Состояние проекта для точки на карточке: new — не собирали, error — сборка не удалась,
    /// changed — код менялся после сборки, built — собран, но не проверен, tested / failed — итог проверки в игре.
    /// </summary>
    public string Status(DateTime edited)
    {
        if (LastBuild is not { } b) return "new";
        if (!b.Ok) return "error";
        // Сборка сама сохраняет скрипт за мгновение до себя — пара секунд допуска.
        if (edited > b.Time.AddSeconds(3)) return "changed";
        if (LastTest is not { } t || t.Time < b.Time.AddSeconds(-1)) return "built";
        return t.Ok ? "tested" : "failed";
    }

    /// <summary>Записать загрузки на сегодня (за день — последнее значение), держим месяц.</summary>
    public void NoteDownloads(long total, DateTime now)
    {
        Downloads[now.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = total;
        while (Downloads.Count > KeepDays) Downloads.Remove(Downloads.Keys.First());
    }

    public void NoteRelease(string version, string where, DateTime now)
    {
        Releases.Add(new ReleaseNote(version, now, where));
        if (Releases.Count > KeepReleases) Releases.RemoveRange(0, Releases.Count - KeepReleases);
    }

    /// <summary>Записать сборку проекта (удачную или нет) — для точки статуса и обзора.</summary>
    public static void NoteBuild(string dir, bool ok, int warnings, string? zip, string version) =>
        Update(dir, m => m.LastBuild = new BuildNote(DateTime.UtcNow, ok, warnings, zip, version));

    /// <summary>
    /// Проверка пройдена: в логе нет бед этого мода и игра не упала — закрыта вами (кнопкой «Остановить»)
    /// или вышла сама с кодом 0. Кода нет (игру запустил чужой лаунчер) — судим только по логу.
    /// </summary>
    public static bool Passed(int? exit, bool byUser, int errors) => errors == 0 && (byUser || exit is null or 0);

    // ---------------------------------------------------------------- лог проверки

    static readonly Regex ErrorWord = new(@"\b(error|fatal|exception|failed|skipped)\b|\[(ERROR|FATAL)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Уровень WARN в начале строки: SMAPI «[12:00:01 WARN  …]», BepInEx «[Warning  : …]», MelonLoader «[…] [WARNING]».</summary>
    static readonly Regex WarnLevel = new(@"^\s*\[(?:[\d:.]+\s+)?WARN(?:ING)?\b|^\s*\[Warning\s*:|^\s*\[[\d:.]+\]\s*(?:\[[^\]]*\]\s*)?\[WARN(?:ING)?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Строка лога — о беде (её красим и по ней проверка не пройдена). Предупреждение со словом «skipped» — не беда.</summary>
    public static bool IsError(string line) => !WarnLevel.IsMatch(line) && ErrorWord.IsMatch(line);

    /// <summary>Строка лога — предупреждение загрузчика (красим жёлтым, проверку не проваливает).</summary>
    public static bool IsWarning(string line) => WarnLevel.IsMatch(line);

    /// <summary>По каким словам узнать мод в логе: имя, имя пакета, UniqueID, папка Content Patcher.</summary>
    public static List<string> Needles(ModBuild b) =>
        new[] { b.Name, Projects.PackageName(b.Name), Projects.UniqueId(b), "creator:" + Projects.PackageName(b.Name) }
            .Where(n => n.Trim().Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Строки лога про этот мод: где встречается его имя, и следом стек ошибки («  at …»), чтобы было видно, где упало.
    /// Чужие моды и сам загрузчик — мимо.
    /// </summary>
    public static List<string> ModLines(IEnumerable<string> lines, IReadOnlyCollection<string> needles, int max = KeepLines)
    {
        var found = new List<string>();
        var trace = 0;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { trace = 0; continue; }
            if (needles.Any(n => line.Contains(n, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(line.Length > 300 ? line[..300] : line);
                trace = IsError(line) ? 4 : 0;
            }
            else if (trace > 0 && (char.IsWhiteSpace(raw[0]) || line.StartsWith("at ", StringComparison.Ordinal)))
            {
                found.Add(line.Length > 300 ? line[..300] : line);
                trace--;
            }
            else trace = 0;
            if (found.Count >= max) break;
        }
        return found;
    }

    /// <summary>
    /// Строки мода и сколько в них бед: ошибки в самих строках или жалобы загрузчика на этот мод
    /// (SMAPI пишет «Skipped mods» отдельным блоком — строка мода там без слова «ошибка»).
    /// </summary>
    public static (List<string> Lines, int Errors) Scan(GameDef game, IList<string> log, ModBuild b)
    {
        var needles = Needles(b);
        var lines = ModLines(log, needles);
        var issues = Logs.Parse(game, string.Join('\n', log)).Count(i => needles.Any(n => i.Mod.Contains(n, StringComparison.OrdinalIgnoreCase)));
        return (lines, Math.Max(lines.Count(IsError), issues));
    }

    /// <summary>
    /// Лог загрузчика после проверки. fresh = false — лог не обновлялся с начала проверки
    /// (игра закрылась раньше, чем загрузились моды, или лога нет вовсе).
    /// </summary>
    public static (List<string> Lines, int Errors, bool Fresh, string? Path) ModLog(GameDef game, string gamePath, ModBuild b, DateTime since)
    {
        var path = Logs.PathFor(game, gamePath);
        if (path is null || !File.Exists(path)) return ([], 0, false, path);
        if (File.GetLastWriteTimeUtc(path) < since.AddSeconds(-5)) return ([], 0, false, path);
        var (lines, errors) = Scan(game, Logs.Tail(path, 8000), b);
        return (lines, errors, true, path);
    }

    /// <summary>«2:31», «1:05:09» — сколько шла игра.</summary>
    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    // ---------------------------------------------------------------- проверки

    [SelfTest]
    static string ProjectMetaRoundTrip()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modlaunch-meta-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            File.WriteAllText(PathOf(dir), "{\"future\":{\"x\":1},\"hubId\":\"u_old\"}");
            var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
            Update(dir, m =>
            {
                m.LastBuild = new BuildNote(now, true, 2, "/b/My_Mod-1.2.0.zip", "1.2.0");
                m.LastTest = new TestNote(now.AddMinutes(1), false, 3, 151.4, false, "1.2.0", ["[Error  : My Mod] boom"], Errors: 1);
                m.NoteRelease("1.2.0", "hub", now);
                for (var d = 0; d < 40; d++) m.NoteDownloads(d * 10, now.AddDays(d));
            });
            var back = Load(dir);
            if (back.HubId != "u_old" || back.LastBuild?.Warnings != 2 || back.LastBuild.Time != now) throw new Exception("build " + back.LastBuild);
            if (back.LastTest is not { Ok: false, Exit: 3, Lines.Count: 1, Errors: 1 } t || Math.Abs(t.Seconds - 151.4) > 0.01) throw new Exception("test " + back.LastTest);
            if (back.Releases.Count != 1 || back.Downloads.Count != KeepDays || back.Downloads.Values.Last() != 390) throw new Exception("releases/downloads");
            // Поля новых версий не теряются при записи старой.
            if (back.ToJson()["future"]?["x"]?.GetValue<int>() != 1) throw new Exception("unknown field dropped");
            if (Load(Path.Combine(dir, "nope")).LastBuild is not null) throw new Exception("missing file");
            File.WriteAllText(PathOf(dir), "{broken");
            if (Load(dir).HubId is not null) throw new Exception("broken file");
            return "project.json keeps history, unknown fields and survives a broken file";
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [SelfTest]
    static string ProjectStatusFollowsBuildAndTest()
    {
        var t0 = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var m = new ProjectMeta();
        var s = new List<string> { m.Status(t0) };
        m.LastBuild = new BuildNote(t0, false, 0, null, "1.0.0");
        s.Add(m.Status(t0));
        m.LastBuild = new BuildNote(t0, true, 0, "z", "1.0.0");
        s.Add(m.Status(t0.AddSeconds(-1)));
        s.Add(m.Status(t0.AddMinutes(5)));
        m.LastTest = new TestNote(t0.AddSeconds(30), true, 0, 60, false, "1.0.0", []);
        s.Add(m.Status(t0));
        m.LastTest = m.LastTest with { Ok = false };
        s.Add(m.Status(t0));
        // Проверка старее сборки — сборка ещё не проверена.
        m.LastBuild = new BuildNote(t0.AddHours(1), true, 0, "z", "1.0.1");
        s.Add(m.Status(t0.AddHours(1)));
        var got = string.Join(",", s);
        if (got != "new,error,built,changed,tested,failed,built") throw new Exception(got);
        return got;
    }

    [SelfTest]
    static string ProjectBuildsAreVersioned()
    {
        var code = Templates.ById("sdv-seeds")!.Code;
        var p = Projects.Create("Build Keep Test", code);
        try
        {
            string? first = null;
            for (var i = 0; i < Projects.KeepBuilds + 2; i++)
            {
                var b = ModScript.Compile(Regex.Replace(code, @"(?m)^\s*version\s+\S+", $"version 1.0.{i}"));
                var packed = Projects.Pack(b, p.Dir).GetAwaiter().GetResult();
                first ??= packed.Zip;
                if (Projects.ProjectIdOfZip(packed.Zip) != p.Id) throw new Exception("zip outside project builds: " + packed.Zip);
                // Время записи у файлов одной секунды на некоторых дисках совпадает — разводим явно.
                File.SetLastWriteTimeUtc(packed.Zip, DateTime.UtcNow.AddSeconds(i - 100));
            }
            var builds = Projects.Builds(p.Id);
            if (builds.Count != Projects.KeepBuilds) throw new Exception($"{builds.Count} builds kept");
            if (File.Exists(first) || !builds[0].Name.EndsWith($"-1.0.{Projects.KeepBuilds + 1}.zip")) throw new Exception("oldest kept: " + string.Join(", ", builds.Select(f => f.Name)));
            // Сохранение без правок не делает проект «изменённым после сборки».
            var stamp = File.GetLastWriteTimeUtc(p.Script);
            File.SetLastWriteTimeUtc(p.Script, stamp.AddMinutes(-10));
            Projects.Save(p, File.ReadAllText(p.Script));
            if (File.GetLastWriteTimeUtc(p.Script) != stamp.AddMinutes(-10)) throw new Exception("same text rewritten");
            var meta = Load(p.Dir);
            if (meta.LastBuild is not { Ok: true } last || last.Version != $"1.0.{Projects.KeepBuilds + 1}" || last.Zip != builds[0].FullName) throw new Exception("meta " + meta.LastBuild);
            // Скрипт без проекта (из галереи) — в общую _build, как раньше; в историю проекта не попадает.
            var loose = Projects.Pack(ModScript.Compile(code), null).GetAwaiter().GetResult();
            if (Projects.ProjectIdOfZip(loose.Zip) is not null || Projects.ProjectIdOf(Projects.OutDir) is not null) throw new Exception("loose build " + loose.Zip);
            // Сборка с ошибкой — тоже в историю: точка станет красной.
            try { Projects.Pack(ModScript.Compile("mod \"x\"\ngame nope-game\n"), p.Dir).GetAwaiter().GetResult(); } catch { }
            if (Load(p.Dir).Status(DateTime.MinValue) != "error") throw new Exception("failed build not noted");
            if (!Passed(0, false, 0) || !Passed(-1, true, 0) || Passed(3, false, 0) || Passed(0, true, 1) || !Passed(null, false, 0)) throw new Exception("verdict");
            Projects.Delete(p);
            if (Directory.Exists(Projects.BuildsDir(p.Id))) throw new Exception("builds left after delete");
            return $"{Projects.KeepBuilds} of {Projects.KeepBuilds + 2} versions kept in _build/{p.Id}, newest noted in project.json";
        }
        finally { Projects.Delete(p); }
    }

    [SelfTest]
    static string ModLogKeepsOnlyThisMod()
    {
        var b = new ModBuild { Name = "Cheap Seeds", Author = "Mira", Version = "1.0.0", Game = "valheim" };
        var log = """
            [Info   :   BepInEx] Loading [Nautilus 1.0]
            [Error  : Map Mod] NullReferenceException: Object reference not set
              at MapMod.Plugin.Awake ()
            [Info   :Cheap Seeds] prices patched
            [Error  :Mira.Cheap_Seeds] NullReferenceException: boom
              at CheapSeeds.Patch.Apply ()
              at CheapSeeds.Plugin.Awake ()
            [Info   : Other] Cheap Seed is a different mod
            [12:00:01 ERROR SMAPI]    Skipped mods
               - [CP] Cheap Seeds because it needs Content Patcher
            """.Split('\n');
        var (lines, errors) = Scan(GameCatalog.ById("valheim")!, log, b);
        if (lines.Count != 5) throw new Exception(string.Join(" | ", lines));
        if (lines.Any(l => l.Contains("Map Mod") || l.Contains("MapMod") || l.Contains("Other"))) throw new Exception("foreign mod: " + string.Join(" | ", lines));
        if (errors != 1 || !lines[2].TrimStart().StartsWith("at CheapSeeds")) throw new Exception($"errors {errors}, trace: " + string.Join(" | ", lines));
        // SMAPI: мод пропущен — строка без слова «ошибка», но это беда.
        var smapi = Scan(GameCatalog.ById("stardew-valley")!, ["[12:00:01 INFO  SMAPI] Loading mods...", "Skipped mods", "   - [CP] Cheap Seeds because it needs Content Patcher", ""], b);
        if (smapi.Lines.Count != 1 || smapi.Errors != 1) throw new Exception($"smapi {smapi.Lines.Count}/{smapi.Errors}");
        if (Duration(151.4) != "2:31" || Duration(3909) != "1:05:09") throw new Exception("duration " + Duration(151.4));
        // Предупреждение загрузчика со словом «skipped» — жёлтое, не ошибка; ошибка BepInEx — ошибка.
        const string warn = "[12:04:15 WARN  Content Patcher] [CP] Cheap Seeds > entry #2 was skipped.";
        if (IsError(warn) || !IsWarning(warn) || !IsWarning("[Warning:  Cheap Seeds] slow") || IsWarning("[Info   :Cheap Seeds] warning: none")
            || !IsError("[Error  :Cheap Seeds] boom") || !IsError("[12:00:01 ERROR SMAPI]    Skipped mods")) throw new Exception("levels");
        return $"{lines.Count} of {log.Length} log lines are this mod's, {errors} error; SMAPI skip counted";
    }
}
