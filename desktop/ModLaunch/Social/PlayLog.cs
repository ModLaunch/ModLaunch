using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Social;

public sealed record Gate(bool Ok, bool Installed, bool Enabled, bool Played, bool Own, bool GameFound);

/// <summary>
/// Какие моды побывали в игре (plays.json, как в 3.x): отмечается при запуске
/// из ModLaunch и по свежему логу загрузчика — если игру запускали из Steam.
/// Оценить мод можно только после этого.
/// </summary>
public static class PlayLog
{
    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "plays.json"), () => new JsonObject { ["mods"] = new JsonObject(), ["launches"] = new JsonObject() });

    static string Key(string gameId, string modId) => $"{gameId}|{modId}";

    static void Mark(GameDef game, IEnumerable<JsonObject> records, DateTime at, string how, DateTime installedBefore)
    {
        var mods = File.Data.Obj("mods");
        var changed = false;
        foreach (var r in records)
        {
            if (!r.Bool("enabled", true) || r.Bool("missing")) continue;
            var installed = DateTime.TryParse(r.Str("installedAt"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var t) ? t : (DateTime?)null;
            if (installed is null ? how != "launch" : installed >= installedBefore) continue;
            var modId = game.CatalogId(r.Str("id") ?? "") ?? r.Str("id") ?? "";
            if (mods[Key(game.Id, modId)]?["playedAt"] is not null) continue;
            mods[Key(game.Id, modId)] = new JsonObject { ["playedAt"] = at.ToString("o"), ["how"] = how };
            changed = true;
        }
        if (changed) File.Save();
    }

    public static void NoteLaunch(GameDef game, ModRegistry registry)
    {
        var at = DateTime.UtcNow;
        File.Data.Obj("launches")[game.Id] = at.ToString("o");
        Mark(game, registry.List(), at, "launch", at.AddSeconds(1));
        File.Save();
    }

    public static void ObserveLog(GameDef game, ModRegistry registry, string? logPath)
    {
        if (logPath is null || !System.IO.File.Exists(logPath)) return;
        var modified = System.IO.File.GetLastWriteTimeUtc(logPath);
        if (modified > DateTime.UtcNow.AddMinutes(1)) return;
        Mark(game, registry.List(), modified, "log", modified);
    }

    public static Gate Check(GameState g, string catalogId)
    {
        var registry = g.Registry;
        if (registry is not null)
        {
            try { ObserveLog(g.Def, registry, Features.Logs.PathFor(g.Def, g.Path!)); } catch { }
        }
        var record = registry?.Get(g.Def.RecordId(catalogId));
        var played = File.Data.Obj("mods")[Key(g.Def.Id, catalogId)]?["playedAt"] is not null;
        var own = Reviews.Mine(g.Def.Id, catalogId) is not null;
        var installed = record is not null && !record.Bool("missing");
        return new Gate(played || own, installed, installed && record!.Bool("enabled", true), played, own, g.Status == Detect.Found);
    }
}
