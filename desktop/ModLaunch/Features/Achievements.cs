using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Достижение: сколько уже есть, сколько нужно и редкость (как в Steam — по порогу).</summary>
public sealed record Achievement(string Id, string Icon, long Have, long Need, string Tier)
{
    public bool Done => Have >= Need;
    public double Progress => Need <= 0 ? 1 : Math.Clamp((double)Have / Need, 0, 1);
}

/// <summary>
/// 9.8: достижения и уровень игрока ModLaunch — как в Steam, но считаются по тому, что уже
/// есть на компьютере (игры, моды, часы, сборки, копии сохранений). Ничего не отправляется.
/// «Итоги» — как Steam Replay: сколько играли, во что больше всего, сколько модов поставили.
/// </summary>
public static class Achievements
{
    public static List<Achievement> All()
    {
        var found = AppState.Games.Where(g => g.Status == Detect.Found).ToList();
        long mods = 0;
        foreach (var g in found) try { mods += g.ModCount; } catch { }
        var hours = AppState.Games.Sum(g => PlayTime.Get(g.Def.Id).TotalMs) / 3_600_000;
        var sessions = AppState.Games.Sum(g => (long)PlayTime.Get(g.Def.Id).Sessions);
        long backups = 0;
        foreach (var g in AppState.Games) try { backups += Backups.List(g.Def.Id).Count; } catch { }
        var installs = History.All().Count(h => h.Ok);
        var builds = AiBuilds.All().Count;
        var favorites = Favorites.All().Count;
        var collections = GameCollections.Names().Count;
        return
        [
            new("first-game", "Gamepad", found.Count, 1, "common"),
            new("library-5", "Grid", found.Count, 5, "rare"),
            new("first-mod", "Package", mods, 1, "common"),
            new("mods-25", "Layers", mods, 25, "rare"),
            new("mods-100", "Trophy", mods, 100, "epic"),
            new("hours-10", "Clock", hours, 10, "common"),
            new("hours-100", "Flame", hours, 100, "epic"),
            new("sessions-50", "Play", sessions, 50, "rare"),
            new("ai-first", "Sparkles", builds, 1, "common"),
            new("ai-5", "Wand", builds, 5, "rare"),
            new("installs-50", "Download", installs, 50, "rare"),
            new("backup", "Save", backups, 1, "common"),
            new("favorites-10", "Heart", favorites, 10, "rare"),
            new("collector", "Bag", collections, 3, "rare"),
        ];
    }

    /// <summary>Уровень: по 100 очков за обычное, 250 — за редкое, 500 — за эпическое.</summary>
    public static (int Level, int Xp, int Next) Level(List<Achievement> list)
    {
        var xp = list.Where(a => a.Done).Sum(a => a.Tier switch { "epic" => 500, "rare" => 250, _ => 100 });
        return (1 + xp / 300, xp, 300 - xp % 300);
    }

    /// <summary>Итоги: часы, сеансы, топ игр по времени.</summary>
    public static (long Hours, long Sessions, List<(GameState Game, long Ms)> Top) Replay()
    {
        var rows = AppState.Games.Select(g => (Game: g, Ms: PlayTime.Get(g.Def.Id).TotalMs)).Where(x => x.Ms > 0).OrderByDescending(x => x.Ms).ToList();
        return (rows.Sum(r => r.Ms) / 3_600_000, AppState.Games.Sum(g => (long)PlayTime.Get(g.Def.Id).Sessions), rows.Take(5).ToList());
    }
}
