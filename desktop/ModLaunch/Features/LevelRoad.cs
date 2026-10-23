using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 16.0: путь наград за уровни, как в боевом пропуске. Раньше награды давали только достижения
/// (около двадцати на всё), и новый уровень ничего не приносил. Теперь каждый уровень со 2-го по 50-й
/// открывает награду: каждый 5-й — рамку, каждый 4-й — баннер, уровни 3, 7, 11… — титул, остальные — значки.
/// Награда открывается сама; «новые» — пока их не забрали на главной или в профиле (profile.roadSeen).
/// </summary>
public static class LevelRoad
{
    public const int Max = 50;

    static readonly string[] Frames = ["ruby", "emerald", "sapphire", "amethyst", "obsidian", "prism", "lava", "ice", "royal", "cosmic"];
    static readonly string[] Banners = ["ocean", "forest", "ember", "nebula", "glacier", "synth", "dawn", "void", "neon", "cosmos"];
    static readonly string[] Titles = ["regular", "explorer", "tinkerer", "veteran", "architect", "curator", "pathfinder", "virtuoso", "grandmaster", "legend"];
    static readonly string[] Badges = ["star", "bolt", "gem", "rocket", "shield", "crown", "comet", "music", "dragon", "anvil", "key", "infinity", "phoenix", "compass", "sword", "potion", "moon", "controller", "planet"];

    /// <summary>Уровень → награда, со 2-го по 50-й.</summary>
    public static readonly IReadOnlyList<(int Level, Reward Reward)> Road = Make();

    static List<(int, Reward)> Make()
    {
        var list = new List<(int, Reward)>();
        int f = 0, b = 0, t = 0, g = 0;
        for (var level = 2; level <= Max; level++)
        {
            Reward r = level % 5 == 0 ? new("frame", Frames[f++])
                : level % 4 == 0 ? new("banner", Banners[b++])
                : level % 4 == 3 ? new("title", Titles[t++])
                : new("badge", Badges[g++]);
            list.Add((level, r));
        }
        return list;
    }

    /// <summary>
    /// 17.0: вторая дорожка — личные сборки настроек от создателей (Core/CreatorBuilds). На своих уровнях они
    /// открываются вместе с обычной наградой, на пути видны значком-шестерёнкой у ступени.
    /// </summary>
    public static readonly IReadOnlyList<(int Level, Reward Reward)> Bonus =
        CreatorBuilds.All.Select(b => (b.Level, new Reward("config", b.Id))).ToList();

    /// <summary>Обе дорожки по порядку уровней.</summary>
    static IEnumerable<(int Level, Reward Reward)> Both => Road.Concat(Bonus).OrderBy(x => x.Level);

    /// <summary>Сборка создателя на этом уровне (null — нет).</summary>
    public static Reward? BonusAt(int level) => Bonus.FirstOrDefault(x => x.Level == level).Reward;

    /// <summary>На каком уровне открывается награда (null — не с пути уровней).</summary>
    public static int? LevelOf(Reward r)
    {
        foreach (var (level, reward) in Both) if (reward == r) return level;
        return null;
    }

    /// <summary>Большие ступени (каждый 5-й уровень) — для подсветки на пути.</summary>
    public static bool Milestone(int level) => level % 5 == 0;

    public static IEnumerable<Reward> Unlocked(int level) => Both.Where(x => x.Level <= level).Select(x => x.Reward);

    /// <summary>До какого уровня награды уже забраны. В первый раз — текущий: старые уровни не сыплют «новыми».</summary>
    public static int Seen(int level)
    {
        var profile = Settings.Data.Obj("profile");
        if (profile["roadSeen"] is null)
        {
            profile["roadSeen"] = Math.Max(1, level - 1);
            Settings.Save();
        }
        return (int)profile.Long("roadSeen");
    }

    /// <summary>Новые награды: открыты, но ещё не забраны.</summary>
    public static List<(int Level, Reward Reward)> Fresh(int level)
    {
        var seen = Seen(level);
        return Both.Where(x => x.Level > seen && x.Level <= level).ToList();
    }

    /// <summary>Забрать всё открытое; лучшее новое надевается само, если ничего своего не надето.</summary>
    public static List<(int Level, Reward Reward)> Claim(int level)
    {
        var fresh = Fresh(level);
        var profile = Settings.Data.Obj("profile");
        profile["roadSeen"] = Math.Max(level, Seen(level));
        foreach (var kind in new[] { "frame", "banner", "title" })
            if (profile.Str(kind) is null && fresh.LastOrDefault(x => x.Reward.Kind == kind) is { Reward: not null } best)
                profile[kind] = best.Reward.Id;
        Settings.Save();
        Cosmetics.Notify();
        return fresh;
    }

    /// <summary>Отрезок пути вокруг уровня: одна ступень назад и count вперёд (для главной).</summary>
    public static List<(int Level, Reward Reward)> Around(int level, int count)
    {
        var from = Math.Clamp(level - 1, 2, Math.Max(2, Max - count + 1));
        return Road.Where(x => x.Level >= from).Take(count).ToList();
    }

    [SelfTest]
    static string LevelRoadGivesEveryLevel()
    {
        if (Road.Count != Max - 1 || Road[0].Level != 2 || Road[^1].Level != Max) throw new Exception("road length " + Road.Count);
        if (Road.Select(x => x.Reward.Key).Distinct().Count() != Road.Count) throw new Exception("duplicate rewards");
        if (Road.Count(x => x.Reward.Kind == "frame") != Frames.Length || Road.Count(x => x.Reward.Kind == "banner") != Banners.Length
            || Road.Count(x => x.Reward.Kind == "title") != Titles.Length || Road.Count(x => x.Reward.Kind == "badge") != Badges.Length)
            throw new Exception("every name used once");
        if (LevelOf(new Reward("frame", "ruby")) != 5 || LevelOf(new Reward("frame", "gold")) is not null) throw new Exception("level of");
        if (Unlocked(4).Count() != 3 + Bonus.Count(x => x.Level <= 4) || Around(1, 6)[0].Level != 2 || Around(Max, 6).Count != 6 || Around(12, 6)[0].Level != 11) throw new Exception("around");
        if (LevelOf(new Reward("config", CreatorBuilds.All[0].Id)) != CreatorBuilds.All[0].Level || BonusAt(CreatorBuilds.All[0].Level) is null || BonusAt(2) is not null)
            throw new Exception("creator builds on the road");
        if (Unlocked(Max).Count() != Road.Count + Bonus.Count) throw new Exception("both tracks unlock");
        return $"{Road.Count} rewards on levels 2–{Max}, each once; {Bonus.Count} creator builds on the second track";
    }
}
