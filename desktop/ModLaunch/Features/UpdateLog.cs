using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>
/// Запись журнала: какой мод какой игры, с какой версии на какую, сам (автообновление) или по кнопке.
/// Changelog — что написал автор об изменениях (null — ещё загружается, "" — автор ничего не написал или сети не было).
/// </summary>
public sealed record UpdateEntry(string Id, DateTime At, string GameId, string GameName, string ModId, string Name, string From, string To,
    string Source, bool Auto, string? Changelog, string? Url);

/// <summary>
/// 27.0: журнал обновлений модов. Каждое обновление (по кнопке, «Обновить всё», перед игрой и автообновление)
/// записывается с версиями «было → стало», а список изменений автора догружается в фоне: Thunderstore — из CHANGELOG
/// пакета, Nexus и Hub — из записей к версиям. Хранятся последние 400 записей (update-log.json в папке данных).
/// </summary>
public static partial class UpdateLog
{
    const int Keep = 400;
    const int MaxText = 3000;
    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "update-log.json"), () => new JsonObject { ["items"] = new JsonArray() });
    static readonly object Gate = new();

    /// <summary>Журнал изменился (новая запись или догрузился список изменений). Вызывается из любого потока.</summary>
    public static event Action? Changed;

    static JsonArray Items
    {
        get
        {
            if (File.Data["items"] is JsonArray a) return a;
            var fresh = new JsonArray();
            File.Data["items"] = fresh;
            return fresh;
        }
    }

    /// <summary>Записать обновление (из задачи обновления, любой поток). Версия не поменялась — не пишем. Возвращает номер записи.</summary>
    public static string? Add(string gameId, string gameName, string modId, string name, string from, string to, string source, bool auto, string? url)
    {
        if (to == "" || Versions.Same(from, to)) return null;
        var id = Guid.NewGuid().ToString("N")[..12];
        lock (Gate)
        {
            Items.Insert(0, new JsonObject
            {
                ["id"] = id, ["at"] = DateTime.UtcNow.ToString("o"), ["game"] = gameId, ["gameName"] = gameName, ["mod"] = modId, ["name"] = name,
                ["from"] = from, ["to"] = to, ["source"] = source, ["auto"] = auto, ["changelog"] = null, ["url"] = url,
            });
            while (Items.Count > Keep) Items.RemoveAt(Items.Count - 1);
            File.Save();
        }
        Changed?.Invoke();
        return id;
    }

    public static List<UpdateEntry> All(string? gameId = null)
    {
        lock (Gate)
            return Items.OfType<JsonObject>().Where(o => gameId is null || o.Str("game") == gameId).Select(o => new UpdateEntry(
                o.Str("id") ?? "", DateTime.TryParse(o.Str("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.UtcNow,
                o.Str("game") ?? "", o.Str("gameName") ?? "", o.Str("mod") ?? "", o.Str("name") ?? "", o.Str("from") ?? "", o.Str("to") ?? "",
                o.Str("source") ?? "", o.Bool("auto"), o["changelog"] is null ? null : o.Str("changelog") ?? "", o.Str("url"))).ToList();
    }

    /// <summary>Обновления игры за последние days дней.</summary>
    public static List<UpdateEntry> Recent(string gameId, int days) => All(gameId).Where(e => DateTime.UtcNow - e.At < TimeSpan.FromDays(days)).ToList();

    public static void Clear(string? gameId = null)
    {
        lock (Gate)
        {
            if (gameId is null) File.Data["items"] = new JsonArray();
            else foreach (var o in Items.OfType<JsonObject>().Where(o => o.Str("game") == gameId).ToList()) Items.Remove(o);
            File.Save();
        }
        Changed?.Invoke();
    }

    static void SetChangelog(string id, string text)
    {
        lock (Gate)
        {
            if (Items.OfType<JsonObject>().FirstOrDefault(o => o.Str("id") == id) is not { } o) return;
            o["changelog"] = text.Length > MaxText ? text[..MaxText].TrimEnd() + "…" : text;
            File.Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Догрузить список изменений в фоне. Без сети или без текста у автора — пустой текст (а не вечное «загружается»).</summary>
    public static void FetchChangelog(Games.GameDef game, string entryId, string catalogId, string source, string from, string to)
    {
        _ = Task.Run(async () =>
        {
            var text = "";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                if (await Catalog.Get(game, catalogId, cts.Token, source) is { } mod)
                {
                    if (mod.Source == "thunderstore") text = FromBlocks(await Extras.Changelog(game, mod, null, cts.Token), from, to);
                    if (text == "") text = Between(await Extras.Versions(game, mod, cts.Token), from, to);
                }
            }
            catch { }
            SetChangelog(entryId, text);
        });
    }

    /// <summary>Снимки экрана: три обновления Subnautica (два — сами, с текстом автора, одно — без текста).</summary>
    internal static void DemoFill()
    {
        Clear();
        if (Add("subnautica", "Subnautica", "Nautilus", "Nautilus", "2.1.23", "2.1.24", "nexus", false, "https://www.nexusmods.com/subnautica/mods/1262") is { } a)
            SetChangelog(a, "");
        if (Add("subnautica", "Subnautica", "BepInEx-Tweaks", "Tweaks and Fixes", "2.1.0", "2.2.1", "nexus", true, "https://www.nexusmods.com/subnautica/mods/1") is { } b)
            SetChangelog(b, "2.2.1\n• Fixed the crash when opening the PDA after loading a save\n\n2.2.0\n• Seamoth lights stay on after docking\n• New option: no oxygen warning in the base");
        if (Add("subnautica", "Subnautica", "MapMod", "Map", "1.4.3", "1.5.0", "nexus", true, "https://www.nexusmods.com/subnautica/mods/2") is { } c)
            SetChangelog(c, "1.5.0\n• Biome names on the map\n• Pings for wrecks you have scanned");
    }

    [GeneratedRegex(@"\d+(?:\.\d+)+")] private static partial Regex VersionToken();

    /// <summary>Записи автора к версиям новее from и не новее to — от свежей к старой.</summary>
    internal static string Between(IEnumerable<VersionInfo> versions, string from, string to)
    {
        var sb = new StringBuilder();
        foreach (var v in versions.Where(v => v.Changelog.Trim() != "" && Versions.IsNewer(v.Version, from) && !Versions.IsNewer(v.Version, to))
                     .OrderByDescending(v => v.Version, Comparer<string>.Create(Versions.Compare)))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(v.Version).Append('\n');
            foreach (var line in v.Changelog.Split('\n').Select(l => l.Trim().TrimStart('-', '*', '•').Trim()).Where(l => l != ""))
                sb.Append("• ").Append(line).Append('\n');
        }
        return sb.ToString().Trim();
    }

    /// <summary>Из CHANGELOG (заголовки с номерами версий) — только разделы версий новее from и не новее to.</summary>
    internal static string FromBlocks(IEnumerable<Block> blocks, string from, string to)
    {
        var sb = new StringBuilder();
        var taking = false;
        foreach (var b in blocks)
        {
            if (b.Kind == "h" && VersionToken().Match(b.Text) is { Success: true } m)
            {
                taking = Versions.IsNewer(m.Value, from) && !Versions.IsNewer(m.Value, to);
                if (taking) sb.Append(sb.Length > 0 ? "\n" : "").Append(m.Value).Append('\n');
                continue;
            }
            if (taking && b.Text.Trim() != "") sb.Append(b.Kind == "li" ? "• " : "").Append(b.Text.Trim()).Append('\n');
        }
        return sb.ToString().Trim();
    }

    [SelfTest]
    static string UpdateLogPicksChanges()
    {
        VersionInfo V(string v, string log) => new(v, null, 0, log, null, 0, 0, null);
        var between = Between([V("1.4.0", "- too new"), V("1.3.0", "- fixed crash\n* faster"), V("1.2.1", "small fix"), V("1.2.0", "- already had"), V("1.1.0", "")], "1.2.0", "1.3.0");
        if (between != "1.3.0\n• fixed crash\n• faster\n\n1.2.1\n• small fix") throw new Exception("between: " + between);
        Block[] md = [new("h", "Changelog"), new("h", "v1.3.0 (2026-11-01)"), new("li", "new menu"), new("p", "thanks!"), new("h", "1.2.1"), new("li", "fix"), new("h", "1.2.0"), new("li", "old")];
        var blocks = FromBlocks(md, "1.2.0", "1.3.0");
        if (blocks != "1.3.0\n• new menu\nthanks!\n\n1.2.1\n• fix") throw new Exception("blocks: " + blocks);
        if (FromBlocks(md, "1.3.0", "1.3.0") != "" || Between([], "1", "2") != "") throw new Exception("empty");
        return "versions between old and new picked from release notes and from CHANGELOG headings";
    }
}
