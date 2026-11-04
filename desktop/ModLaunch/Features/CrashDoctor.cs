using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Minecraft;

namespace ModLaunch.Features;

/// <summary>Что нашлось в логе: вид беды, мод (как он назван в логе), чего ему не хватает и сама строка.</summary>
public sealed record CrashClue(string Kind, string Mod, string? Need, string Line);

/// <summary>
/// Причина вылета простыми словами и что сделает «Исправить». Kind: missing (нет нужного мода),
/// outdated (мод устарел после обновления игры), broken (мод падает), loader (устарел загрузчик),
/// memory (не хватило памяти), recent (виноватого в логе нет — выключим моды, поставленные после
/// последней удачной игры), unknown (ничего не нашли).
/// </summary>
public sealed record CrashCause(string Kind, string Title, string Fix, string Line)
{
    public string? ModId { get; init; }
    public string? ModName { get; init; }
    public List<string> Needs { get; init; } = [];
    public ModUpdate? Update { get; init; }
    public McUpdate? McUpdate { get; init; }
    public McItem? McItem { get; init; }
    public List<string> Recent { get; init; } = [];
    public int Memory { get; init; }
    /// <summary>У причины есть что делать по кнопке «Исправить».</summary>
    public bool Fixable => Kind != "unknown" && !(Kind == "memory" && Memory == 0);
}

public sealed record CrashReport(string GameId, List<CrashCause> Causes, string? LogPath)
{
    public bool Fixable => Causes.Any(c => c.Fixable);
}

/// <summary>
/// 23.9: «Почему игра упала». Лог загрузчика (SMAPI, BepInEx, Modding API, Minecraft и его отчёт о вылете)
/// разбирается на понятные причины, у каждой — одно действие: поставить недостающий мод, обновить
/// или выключить сломанный, обновить загрузчик, дать Minecraft больше памяти. Если виноватого в логе
/// нет — подозреваются моды, поставленные после последней удачной игры.
/// </summary>
public static partial class CrashDoctor
{
    // ---------------------------------------------------------------- разбор текста лога

    [GeneratedRegex(@"Could not load \[(.+?)(?: [\d.]+)?\] because it has missing dependencies: (.+)")] private static partial Regex BepMissing();
    [GeneratedRegex(@"Could not load \[(.+?)(?: [\d.]+)?\] because it is incompatible with: (.+)")] private static partial Regex BepIncompatible();
    [GeneratedRegex(@"\[(Error|Fatal)\s*:\s*([^\]]+)\]\s*(.+)", RegexOptions.IgnoreCase)] private static partial Regex BepLine();
    [GeneratedRegex(@"MissingMethodException|MissingFieldException|TypeLoadException|Method not found|Field not found|Could not load type|ReflectionTypeLoadException")] private static partial Regex Outdated();
    [GeneratedRegex(@"\bat (?:\(wrapper [^)]*\) )?([A-Za-z_][\w]*)\.")] private static partial Regex StackFrame();
    [GeneratedRegex(@"-\s+(.+?)\s+because\s+(.+?)\s*$")] private static partial Regex SmapiSkip();
    [GeneratedRegex(@"\[[\d:]+\s+(?:ERROR|ALERT)\s+([^\]]+)\]\s*(.+)")] private static partial Regex SmapiError();
    [GeneratedRegex(@"\((.+)\)\.?\s*$")] private static partial Regex Parens();
    [GeneratedRegex(@"only compatible up to Stardew Valley|newer version of SMAPI|please update SMAPI|SMAPI (?:is|was) outdated", RegexOptions.IgnoreCase)] private static partial Regex SmapiOld();
    [GeneratedRegex(@"\s+v?\d+(?:\.\d+)+\S*$")] private static partial Regex TrailingVersion();
    [GeneratedRegex(@"Could not load file or assembly '([^',]+)")] private static partial Regex NoAssembly();
    [GeneratedRegex(@"\[ERROR\]:?\s*\[([^\]]+)\]\s*-?\s*(.+)")] private static partial Regex HkError();
    [GeneratedRegex(@"Failed to load Mod `([^`]+)`|Error (?:initializing|loading) mod[: ]+`?([^`\r\n]+?)`?\s*$")] private static partial Regex HkFailed();
    [GeneratedRegex(@"Mod '([^']+)' \(([^)]+)\)\s+\S+\s+requires\s+.*?\bof\s+(?:mod\s+)?(?:'([^']+)'\s+\(([^)]+)\)|([\w.\-]+)),\s+which is missing")] private static partial Regex FabricMissing();
    [GeneratedRegex(@"Mod '([^']+)' \(([^)]+)\)\s+\S+\s+requires\s+.*?(?:'Minecraft' \(minecraft\)|\bminecraft\b).*?but only the wrong version is present", RegexOptions.IgnoreCase)] private static partial Regex FabricWrongMc();
    [GeneratedRegex(@"Mod '([^']+)' \(([^)]+)\)\s+\S+\s+is incompatible with")] private static partial Regex FabricBreaks();
    [GeneratedRegex(@"Mod ID: '([^']+)', Requested by: '([^']+)', Expected range: '[^']*', Actual version: '([^']+)'")] private static partial Regex ForgeMissing();
    [GeneratedRegex(@"Mixin apply for mod ([\w\-]+) failed|from mod ([\w\-]+) failed injection check|MixinApplyError.*?from mod ([\w\-]+)")] private static partial Regex Mixin();
    [GeneratedRegex(@"Suspected Mods?(?:\(s\))?:\s*\r?\n?\s*([^\r\n(]+?)\s*\(([\w\-]+)\)")] private static partial Regex Suspected();
    [GeneratedRegex(@"OutOfMemoryError|OutOfMemoryException|Out of memory|insufficient memory", RegexOptions.IgnoreCase)] private static partial Regex Memory();

    static readonly HashSet<string> NotMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "BepInEx", "Unity Log", "HarmonyX", "Harmony", "Preloader", "Message", "Info", "Warning", "Debug", "Fatal", "Error",
        "SMAPI", "game", "API", "Modding API", "UnityEngine", "System", "Mono", "MonoMod", "HarmonyLib", "Microsoft", "Unity", "DMD",
        "GameNetcodeStuff", "StardewValley", "StardewModdingAPI", "Netcode", "Steamworks", "TMPro", "Newtonsoft", "Assembly-CSharp",
    };

    static bool IsMod(string name) => name.Trim().Length >= 3 && !NotMods.Contains(name.Trim());

    /// <summary>Убрать хвост версии из имени («Better Crafting 2.1.0» → «Better Crafting»).</summary>
    static string Bare(string name) => TrailingVersion().Replace(name.Trim(), "").Trim();

    /// <summary>Мод, к которому ведёт стек вызовов под строкой ошибки: первое пространство имён, не принадлежащее игре или Unity.</summary>
    static string? FromStack(string[] lines, int from)
    {
        for (var k = from + 1; k < Math.Min(lines.Length, from + 10); k++)
            foreach (Match f in StackFrame().Matches(lines[k]))
                if (IsMod(f.Groups[1].Value)) return f.Groups[1].Value;
        return null;
    }

    public static List<CrashClue> Parse(LoaderKind loader, string text)
    {
        var clues = new List<CrashClue>();
        var lines = text.Replace("\r", "").Split('\n');
        void Add(string kind, string mod, string? need, string line)
        {
            mod = mod.Trim();
            line = line.Trim();
            if (line.Length > 220) line = line[..220] + "…";
            if (!clues.Any(c => c.Kind == kind && c.Mod.Equals(mod, StringComparison.OrdinalIgnoreCase) && c.Need == need)) clues.Add(new CrashClue(kind, mod, need, line));
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Memory().IsMatch(line)) Add("memory", "", null, line);
            switch (loader)
            {
                case LoaderKind.Smapi:
                    if (SmapiOld().IsMatch(line)) { Add("loader", "SMAPI", null, line); break; }
                    if (SmapiSkip().Match(line) is { Success: true } skip)
                    {
                        var mod = Bare(skip.Groups[1].Value);
                        var why = skip.Groups[2].Value;
                        if (why.Contains("aren't installed", StringComparison.OrdinalIgnoreCase) || why.Contains("not installed", StringComparison.OrdinalIgnoreCase))
                        {
                            var list = Parens().Match(why) is { Success: true } p ? p.Groups[1].Value : "";
                            foreach (var need in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Add("missing", mod, need, line);
                        }
                        else if (why.Contains("newer version of SMAPI", StringComparison.OrdinalIgnoreCase)) Add("loader", "SMAPI", null, line);
                        else if (why.Contains("newer version", StringComparison.OrdinalIgnoreCase) && why.Contains(" of ", StringComparison.OrdinalIgnoreCase))
                        {
                            // «it needs newer versions of some mods: SpaceCore (needs 1.25.0 or later)» — обновить нужный мод.
                            var tail = why[(why.IndexOf(':') + 1)..];
                            foreach (var part in tail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                Add("outdated", Regex.Replace(part, @"\s*\(.*$", ""), null, line);
                        }
                        else if (why.Contains("no longer compatible", StringComparison.OrdinalIgnoreCase) || why.Contains("not compatible", StringComparison.OrdinalIgnoreCase) || why.Contains("obsolete", StringComparison.OrdinalIgnoreCase))
                            Add("outdated", mod, null, line);
                        else Add("broken", mod, null, line);
                        break;
                    }
                    if (SmapiError().Match(line) is { Success: true } se && IsMod(se.Groups[1].Value))
                        Add(Outdated().IsMatch(line) || (i + 1 < lines.Length && Outdated().IsMatch(lines[i + 1])) ? "outdated" : "broken", se.Groups[1].Value, null, se.Groups[2].Value);
                    break;

                case LoaderKind.HkApi:
                    if (NoAssembly().Match(line) is { Success: true } na && IsMod(na.Groups[1].Value)) { Add("missing", "", na.Groups[1].Value, line); break; }
                    if (HkFailed().Match(line) is { Success: true } hf) { Add(Outdated().IsMatch(line) ? "outdated" : "broken", hf.Groups[1].Success ? hf.Groups[1].Value : hf.Groups[2].Value, null, line); break; }
                    if (HkError().Match(line) is { Success: true } he && IsMod(he.Groups[1].Value))
                        Add(Outdated().IsMatch(line) ? "outdated" : "broken", he.Groups[1].Value, null, he.Groups[2].Value);
                    break;

                case LoaderKind.Minecraft:
                    if (FabricMissing().Match(line) is { Success: true } fm)
                    {
                        var need = fm.Groups[4].Success ? fm.Groups[4].Value : fm.Groups[5].Value;
                        if (!need.Equals("minecraft", StringComparison.OrdinalIgnoreCase) && !need.Equals("java", StringComparison.OrdinalIgnoreCase)) Add("missing", fm.Groups[2].Value, need, line);
                        break;
                    }
                    if (FabricWrongMc().Match(line) is { Success: true } fw) { Add("outdated", fw.Groups[2].Value, null, line); break; }
                    if (FabricBreaks().Match(line) is { Success: true } fb) { Add("broken", fb.Groups[2].Value, null, line); break; }
                    if (ForgeMissing().Match(line) is { Success: true } fo)
                    {
                        if (fo.Groups[3].Value.Contains("MISSING", StringComparison.OrdinalIgnoreCase)) Add("missing", fo.Groups[2].Value, fo.Groups[1].Value, line);
                        else Add("outdated", fo.Groups[1].Value, null, line);
                        break;
                    }
                    if (Mixin().Match(line) is { Success: true } mx) { Add("broken", new[] { mx.Groups[1], mx.Groups[2], mx.Groups[3] }.First(g => g.Success).Value, null, line); break; }
                    break;

                default:
                    if (BepMissing().Match(line) is { Success: true } bm)
                    {
                        foreach (var dep in bm.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            Add("missing", bm.Groups[1].Value, Regex.Replace(dep, @"\s*\(.*$", ""), line);
                        break;
                    }
                    if (BepIncompatible().Match(line) is { Success: true } bi) { Add("broken", bi.Groups[1].Value, null, line); break; }
                    if (BepLine().Match(line) is { Success: true } bl)
                    {
                        var source = bl.Groups[2].Value.Trim();
                        var outdated = Outdated().IsMatch(line) || (i + 1 < lines.Length && Outdated().IsMatch(lines[i + 1]));
                        var mod = IsMod(source) ? source : FromStack(lines, i);
                        if (mod is not null) Add(outdated ? "outdated" : "broken", mod, null, bl.Groups[3].Value);
                    }
                    break;
            }
        }
        if (loader == LoaderKind.Minecraft && Suspected().Match(text) is { Success: true } sus) Add("broken", sus.Groups[2].Value, null, "Suspected mod: " + sus.Groups[1].Value.Trim());
        // Пропавшая зависимость важнее, чем «мод упал» у того же мода: вторая — следствие первой.
        clues.RemoveAll(c => c.Kind == "broken" && clues.Any(o => o.Kind is "missing" or "outdated" && o.Mod.Equals(c.Mod, StringComparison.OrdinalIgnoreCase)));
        return clues.Take(30).ToList();
    }

    // ---------------------------------------------------------------- удачные игры

    /// <summary>Игра закрылась как обычно — запомнить: моды, поставленные позже, станут первыми подозреваемыми.</summary>
    public static void MarkGood(string gameId)
    {
        Settings.Data.Obj("goodRuns")[gameId] = DateTime.UtcNow.ToString("o");
        Settings.Save();
    }

    public static DateTime? LastGood(string gameId) =>
        DateTime.TryParse(Settings.Data.Obj("goodRuns").Str(gameId), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime() : null;

    /// <summary>Похоже на вылет: код ошибки или закрылась сама в первые полторы минуты.</summary>
    public static bool LooksLikeCrash(Launcher.ExitInfo exit) => !exit.ByUser && ((exit.Code is int c && c != 0) || exit.Ms < 90_000);

    // ---------------------------------------------------------------- диагноз

    static string Norm(string? s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Понятное имя того, чего не хватает: «Pathoschild.ContentPatcher» → «ContentPatcher».</summary>
    public static string Pretty(string need)
    {
        need = need.Trim().Trim('\'', '"');
        var dot = need.LastIndexOf('.');
        var name = dot > 0 && dot < need.Length - 1 && !need.Contains(' ') ? need[(dot + 1)..] : need;
        // GUID BepInEx пишут строчными («evaisa.lethallib») — хотя бы первая буква заглавная.
        return name.Length > 0 && name.All(ch => !char.IsUpper(ch)) ? char.ToUpperInvariant(name[0]) + name[1..] : name;
    }

    static bool Same(string clue, string? name, string? id)
    {
        var n = Norm(clue);
        if (n.Length < 3) return false;
        var a = Norm(name);
        var tail = Norm((id ?? "").Split('-', ':').Last());
        return a == n || tail == n || (a.Length >= 4 && (n.Contains(a) || a.Contains(n))) || (tail.Length >= 4 && (n.Contains(tail) || tail.Contains(n)));
    }

    /// <summary>Свежие логи этого запуска: лог загрузчика и (у Minecraft) отчёт о вылете.</summary>
    static (string Text, string? Path) Fresh(GameState g, DateTime startedUtc)
    {
        if (g.Path is null && !g.Def.IsMinecraft) return ("", null);
        var since = startedUtc.ToLocalTime().AddSeconds(-5);
        var parts = new List<string>();
        string? shown = null;
        var main = Logs.PathFor(g.Def, g.Path ?? "");
        if (main is not null && File.Exists(main) && File.GetLastWriteTime(main) >= since)
        {
            parts.Add(string.Join('\n', Logs.Tail(main, 4000)));
            shown = main;
        }
        if (g.Def.IsMinecraft && Mc.Active is { } a && Directory.Exists(Path.Combine(a.Dir, "crash-reports")))
        {
            var report = new DirectoryInfo(Path.Combine(a.Dir, "crash-reports")).EnumerateFiles("*.txt").Where(f => f.LastWriteTime >= since).MaxBy(f => f.LastWriteTimeUtc);
            if (report is not null) { parts.Add(string.Join('\n', Logs.Tail(report.FullName, 600))); shown ??= report.FullName; }
        }
        return (string.Join('\n', parts), shown);
    }

    public static CrashReport Diagnose(GameState g, DateTime startedUtc)
    {
        var (text, path) = Fresh(g, startedUtc);
        return Diagnose(g, Parse(g.Def.Loader, text), path);
    }

    /// <summary>Улики из лога → причины с действиями: найти мод в списке, есть ли у него обновление.</summary>
    public static CrashReport Diagnose(GameState g, List<CrashClue> clues, string? path)
    {
        var causes = new List<CrashCause>();
        var mc = g.Def.IsMinecraft ? Mc.Active : null;
        var records = g.Registry?.List().Where(m => !m.Bool("missing") && m.Str("kind") != "preset").ToList() ?? [];
        var items = mc is null ? [] : McContent.List(mc, "mod");
        var updates = ModUpdates.Found.GetValueOrDefault(g.Def.Id) ?? [];
        var mcUpdates = mc is null ? [] : McContent.Updates(mc);
        JsonObject? Record(string name) => name == "" ? null : records.FirstOrDefault(r => Same(name, r.Str("name"), r.Str("id")));
        McItem? Item(string name) => name == "" ? null : items.FirstOrDefault(x => Same(name, x.Name, x.ProjectId) || Same(name, Path.GetFileNameWithoutExtension(x.Key), null));
        string Loader() => g.Def.LoaderName;

        foreach (var group in clues.Where(c => c.Kind == "missing").GroupBy(c => c.Mod, StringComparer.OrdinalIgnoreCase))
        {
            var needs = group.Select(c => c.Need!).Where(n => n != "" && Record(Pretty(n)) is null && Item(n) is null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (needs.Count == 0) continue;
            var who = group.Key == "" ? null : Record(group.Key)?.Str("name") ?? Item(group.Key)?.Name ?? group.Key;
            var list = string.Join(", ", needs.Select(Pretty));
            causes.Add(new CrashCause("missing",
                who is null ? I18n.T("v23.why.missing.any", ("need", list)) : I18n.T("v23.why.missing", ("mod", who), ("need", list)),
                I18n.T("v23.fix.install", ("need", list)), group.First().Line) { ModName = who, Needs = needs });
        }

        foreach (var c in clues.Where(c => c.Kind is "outdated" or "broken"))
        {
            var record = Record(c.Mod);
            var item = record is null ? Item(c.Mod) : null;
            if (record is null && item is null) continue;
            var id = record?.Str("id");
            if (causes.Any(x => (id is not null && x.ModId == id) || (item is not null && x.McItem?.Key == item.Key))) continue;
            var name = record?.Str("name") ?? item!.Name;
            var update = id is null ? null : updates.FirstOrDefault(u => u.RecordId == id && !u.Manual);
            var mcUpdate = item is null ? null : mcUpdates.FirstOrDefault(u => u.Item.Key == item.Key);
            var latest = update?.Latest ?? mcUpdate?.Latest.Number;
            var title = I18n.T(c.Kind == "outdated" ? "v23.why.outdated" : "v23.why.broken", ("mod", name));
            var fix = latest is not null ? I18n.T("v23.fix.update", ("mod", name), ("version", latest)) : I18n.T("v23.fix.disable", ("mod", name));
            causes.Add(new CrashCause(c.Kind, title, fix, c.Line) { ModId = id, ModName = name, Update = update, McUpdate = mcUpdate, McItem = item });
        }

        if (clues.FirstOrDefault(c => c.Kind == "loader") is { } old)
            causes.Insert(0, new CrashCause("loader", I18n.T("v23.why.loader", ("loader", Loader())), I18n.T("v23.fix.loader", ("loader", Loader())), old.Line));

        if (clues.FirstOrDefault(c => c.Kind == "memory") is { } mem)
        {
            var more = mc is not null && mc.MemoryMb < 8192 ? Math.Min(8192, Math.Max(4096, mc.MemoryMb * 2)) : 0;
            causes.Add(new CrashCause("memory", I18n.T("v23.why.memory"),
                more > 0 ? I18n.T("v23.fix.memory", ("from", (mc!.MemoryMb / 1024.0).ToString("0.#", I18n.Culture)), ("to", more / 1024)) : I18n.T("v23.fix.memory.close"), mem.Line) { Memory = more });
        }

        if (causes.Count == 0)
        {
            // Виноватого в логе нет — первые подозреваемые: что поставили или обновили после последней удачной игры.
            var good = LastGood(g.Def.Id);
            var recent = new List<string>();
            if (good is DateTime since)
            {
                recent.AddRange(records.Where(r => r.Bool("enabled", true) && DateTime.TryParse(r.Str("installedAt"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) && at.ToUniversalTime() > since)
                    .Select(r => r.Str("id")!));
                if (mc is not null) recent.AddRange(items.Where(x => x.Enabled && x.Modified.ToUniversalTime() > since).Select(x => "mc:" + x.Key));
            }
            if (recent.Count > 0)
            {
                var names = recent.Take(8).Select(r => r.StartsWith("mc:") ? items.FirstOrDefault(x => "mc:" + x.Key == r)?.Name ?? r[3..] : records.FirstOrDefault(x => x.Str("id") == r)?.Str("name") ?? r).ToList();
                causes.Add(new CrashCause("recent", I18n.T("v23.why.recent", ("list", string.Join(", ", names))), I18n.T("v23.fix.recent", ("n", recent.Count)), "") { Recent = recent });
            }
            else causes.Add(new CrashCause("unknown", I18n.T("v23.why.unknown"), "", ""));
        }
        return new CrashReport(g.Def.Id, causes.Take(6).ToList(), path);
    }

    // ---------------------------------------------------------------- самопроверка

    [SelfTest]
    static string ReadsCrashLogsOfFourGames()
    {
        var smapi = """
            [12:00:01 INFO  SMAPI] SMAPI 4.1.2 with Stardew Valley 1.6.14 on Microsoft Windows 10
            [12:00:03 ERROR SMAPI] Skipped mods
            [12:00:03 ERROR SMAPI]    These mods could not be added to your game.
            [12:00:03 ERROR SMAPI]       - Better Crafting 2.1.0 because it requires mods which aren't installed (leclair.bettercraftingcore, spacechase0.SpaceCore).
            [12:00:03 ERROR SMAPI]       - Old Farm Mod 1.0 because it's no longer compatible. Please check for a new version at https://smapi.io/mods
            [12:00:09 ERROR Automate] This mod failed in the GameLoop.UpdateTicked event. Technical details:
            MissingMethodException: Method not found: 'StardewValley.Object.get_Quality()'.
            """;
        var s = Parse(LoaderKind.Smapi, smapi);
        Expect(s.Count(c => c.Kind == "missing" && c.Mod == "Better Crafting") == 2, "smapi missing", s);
        Expect(s.Any(c => c.Kind == "outdated" && c.Mod == "Old Farm Mod"), "smapi no longer compatible", s);
        Expect(s.Any(c => c.Kind == "outdated" && c.Mod == "Automate"), "smapi mod error after patch", s);
        Expect(Parse(LoaderKind.Smapi, "[12:00:00 ERROR SMAPI] Oops! You're running Stardew Valley 1.7.0, but this version of SMAPI is only compatible up to Stardew Valley 1.6.15.").Any(c => c.Kind == "loader"), "smapi too old", s);

        var bep = """
            [Info   :   BepInEx] Loading [MoreCompany 1.11.0]
            [Error  :   BepInEx] Could not load [LethalThings 0.10.7] because it has missing dependencies: evaisa.lethallib (v0.13.0 or newer)
            [Error  : Unity Log] MissingMethodException: Method not found: 'Void GameNetcodeStuff.PlayerControllerB.DamagePlayer(Int32)'
            Stack trace:
            MoreCompany.Patches.HudPatch.Postfix (HUDManager __instance) (at <abc>:0)
            at MoreCompany.Patches.HudPatch.Postfix (HUDManager __instance)
            [Error  :ShipLoot] NullReferenceException: Object reference not set to an instance of an object
            """;
        var b = Parse(LoaderKind.Bepinex, bep);
        Expect(b.Any(c => c.Kind == "missing" && c.Mod == "LethalThings" && c.Need == "evaisa.lethallib"), "bepinex missing deps", b);
        Expect(b.Any(c => c.Kind == "outdated" && c.Mod == "MoreCompany"), "bepinex outdated from stack", b);
        Expect(b.Any(c => c.Kind == "broken" && c.Mod == "ShipLoot"), "bepinex mod error", b);
        Expect(Pretty("evaisa.lethallib") == "Lethallib" && Pretty("SpaceCore") == "SpaceCore", "pretty need", b);

        var hk = """
            [INFO]:[API] - Trying to load mods
            [ERROR]:[API] - Error: System.IO.FileNotFoundException: Could not load file or assembly 'Satchel, Version=0.8.12.0, Culture=neutral' or one of its dependencies.
            [ERROR]:[API] - Failed to load Mod `Pale Court`
            [ERROR]:[Benchwarp] - System.MissingMethodException: Method not found: 'Void HeroController.AddGeo(Int32)'
            """;
        var h = Parse(LoaderKind.HkApi, hk);
        Expect(h.Any(c => c.Kind == "missing" && c.Need == "Satchel"), "hk missing assembly", h);
        Expect(h.Any(c => c.Kind == "broken" && c.Mod == "Pale Court"), "hk failed mod", h);
        Expect(h.Any(c => c.Kind == "outdated" && c.Mod == "Benchwarp"), "hk outdated", h);

        var mc = """
            [main/ERROR]: Incompatible mods found!
            net.fabricmc.loader.impl.FormattedException: Some of your mods are incompatible with the game or each other!
            	 - Mod 'Iris' (iris) 1.7.2+mc1.21 requires any version of sodium, which is missing!
            	 - Mod 'Sodium Extra' (sodium-extra) 0.5.7 requires version 0.5.0 or later of mod 'Sodium' (sodium), which is missing!
            	 - Mod 'Old Minimap' (oldminimap) 1.0 requires version 1.20.1 of 'Minecraft' (minecraft), but only the wrong version is present: 1.21!
            Mod ID: 'geckolib', Requested by: 'alexsmobs', Expected range: '[4.4,)', Actual version: '[MISSING]'
            java.lang.OutOfMemoryError: Java heap space
            """;
        var m = Parse(LoaderKind.Minecraft, mc);
        Expect(m.Any(c => c.Kind == "missing" && c.Mod == "iris" && c.Need == "sodium"), "fabric missing (new format)", m);
        Expect(m.Any(c => c.Kind == "missing" && c.Mod == "sodium-extra" && c.Need == "sodium"), "fabric missing (old format)", m);
        Expect(m.Any(c => c.Kind == "outdated" && c.Mod == "oldminimap"), "fabric wrong minecraft", m);
        Expect(m.Any(c => c.Kind == "missing" && c.Mod == "alexsmobs" && c.Need == "geckolib"), "forge missing", m);
        Expect(m.Any(c => c.Kind == "memory"), "out of memory", m);
        Expect(Parse(LoaderKind.Minecraft, "---- Minecraft Crash Report ----\nSuspected Mods: Sodium (sodium), Version: 0.5.8\n").Any(c => c.Kind == "broken" && c.Mod == "sodium"), "crash report suspect", m);
        Expect(Parse(LoaderKind.Bepinex, "[Info   :   BepInEx] Chainloader startup complete\n[Message:   BepInEx] Loaded 12 plugins\n").Count == 0, "clean log has clues", m);
        return "SMAPI, BepInEx, Modding API and Minecraft crash logs read into causes";
    }

    [SelfTest]
    static string CrashCausesHaveFixes()
    {
        var def = GameCatalog.ById("lethal-company")!;
        var dir = Path.Combine(Paths.DataDir, "crash-test", "Lethal Company");
        foreach (var f in new[] { "LethalThings", "MoreCompany", "ShipLoot" }) Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "plugins", f));
        var registry = new Mods.ModRegistry(def, dir);
        registry.Add(new JsonObject { ["id"] = "Evaisa-LethalThings", ["name"] = "LethalThings", ["folder"] = "LethalThings", ["source"] = "thunderstore", ["version"] = "0.10.7" });
        registry.Add(new JsonObject { ["id"] = "notnotnotswipez-MoreCompany", ["name"] = "MoreCompany", ["folder"] = "MoreCompany", ["source"] = "thunderstore", ["version"] = "1.10.0" });
        registry.Add(new JsonObject { ["id"] = "tinyhoot-ShipLoot", ["name"] = "ShipLoot", ["folder"] = "ShipLoot", ["source"] = "thunderstore", ["version"] = "1.0.0" });
        var g = new GameState { Def = def, Path = dir };
        var had = ModUpdates.Found.GetValueOrDefault(def.Id);
        ModUpdates.Found[def.Id] = [new ModUpdate("notnotnotswipez-MoreCompany", "notnotnotswipez-MoreCompany", "MoreCompany", "1.10.0", "1.11.0", null, false, "thunderstore")];
        try
        {
            var r = Diagnose(g,
            [
                new("missing", "LethalThings", "evaisa.lethallib", "Could not load [LethalThings]"),
                new("outdated", "MoreCompany", null, "MissingMethodException"),
                new("broken", "ShipLoot", null, "NullReferenceException"),
                new("broken", "NotInstalledAtAll", null, "x"),
            ], null);
            var kinds = string.Join(",", r.Causes.Select(c => c.Kind));
            Expect(kinds == "missing,outdated,broken", "causes " + kinds, []);
            Expect(r.Causes[1].Update?.Latest == "1.11.0", "outdated mod is offered its update", []);
            Expect(r.Causes[2].Update is null && r.Causes[2].ModId == "tinyhoot-ShipLoot", "broken mod is offered to be disabled", []);
            Expect(r.Fixable, "report not fixable", []);

            // Чистый лог, но после последней удачной игры поставили мод — он первый подозреваемый.
            Settings.Data.Obj("goodRuns")[def.Id] = DateTime.UtcNow.AddHours(-1).ToString("o");
            var empty = Diagnose(g, [], null);
            Expect(empty.Causes.Single().Kind == "recent" && empty.Causes[0].Recent.Count == 3, "recent mods not suspected", []);
            Settings.Data.Obj("goodRuns")[def.Id] = DateTime.UtcNow.AddHours(1).ToString("o");
            Expect(Diagnose(g, [], null).Causes.Single().Kind == "unknown", "nothing to suspect → unknown", []);
            return "missing → install, outdated → update 1.11.0, broken → disable, clean log → recent mods";
        }
        finally
        {
            if (had is null) ModUpdates.Found.TryRemove(def.Id, out _); else ModUpdates.Found[def.Id] = had;
            Settings.Data.Obj("goodRuns").Remove(def.Id);
            try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { }
        }
    }

    static void Expect(bool ok, string what, List<CrashClue> got)
    {
        if (!ok) throw new Exception(what + ": " + string.Join(" | ", got.Select(c => $"{c.Kind}:{c.Mod}>{c.Need}")));
    }
}
