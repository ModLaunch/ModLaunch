using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>9.9: награда за достижение — рамка аватара, баннер профиля, титул или значок на витрину.</summary>
public sealed record Reward(string Kind, string Id)
{
    public string Key => Kind + ":" + Id;
}

/// <summary>
/// Достижение: сколько уже есть, сколько нужно и редкость (как в Steam — по порогу).
/// Set — набор: modlaunch (общий) или id игры; Slot — вид достижения в наборе игры.
/// </summary>
public sealed record Achievement(string Id, string Icon, long Have, long Need, string Tier, string Set = Achievements.Main, Reward? Reward = null, string Slot = "")
{
    public bool Done => Have >= Need;
    public double Progress => Need <= 0 ? 1 : Math.Clamp((double)Have / Need, 0, 1);
    public int Xp => Tier switch { "legend" => 1000, "epic" => 500, "rare" => 250, _ => 100 };
    public string Title => Achievements.Text(this, "");
    public string Desc => Achievements.Text(this, ".d");
}

/// <summary>
/// 9.8: достижения и уровень игрока ModLaunch — как в Steam, но считаются по тому, что уже
/// есть на компьютере (игры, моды, часы, сборки, копии сохранений). Ничего не отправляется.
/// 9.9: у каждой игры свой набор (первый запуск, моды, часы, марафон, мастер) со своими
/// названиями, а за достижения дают награды для профиля. «Итоги» — как Steam Replay.
/// </summary>
public static class Achievements
{
    public const string Main = "modlaunch";

    /// <summary>Виды достижений в наборе игры — по порядку на экране.</summary>
    public static readonly string[] Slots = ["play", "mods", "hours", "backup", "marathon", "master"];

    public static List<Achievement> All() => [.. Global(), .. AppState.Games.Where(g => g.Status == Detect.Found).SelectMany(ForGame)];

    /// <summary>Общий набор ModLaunch (как в 9.8) — теперь с наградами.</summary>
    public static List<Achievement> Global()
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
            new("first-game", "Gamepad", found.Count, 1, "common", Reward: new("frame", "bronze")),
            new("library-5", "Grid", found.Count, 5, "rare", Reward: new("frame", "silver")),
            new("first-mod", "Package", mods, 1, "common", Reward: new("title", "modder")),
            new("mods-25", "Layers", mods, 25, "rare", Reward: new("banner", "aurora")),
            new("mods-100", "Trophy", mods, 100, "epic", Reward: new("frame", "gold")),
            new("hours-10", "Clock", hours, 10, "common", Reward: new("badge", "clock")),
            new("hours-100", "Flame", hours, 100, "epic", Reward: new("frame", "flame")),
            new("sessions-50", "Play", sessions, 50, "rare", Reward: new("banner", "sunset")),
            new("ai-first", "Sparkles", builds, 1, "common", Reward: new("title", "builder")),
            new("ai-5", "Wand", builds, 5, "rare", Reward: new("frame", "neon")),
            new("installs-50", "Download", installs, 50, "rare", Reward: new("banner", "midnight")),
            new("backup", "Save", backups, 1, "common", Reward: new("title", "keeper")),
            new("favorites-10", "Heart", favorites, 10, "rare", Reward: new("badge", "heart")),
            new("collector", "Bag", collections, 3, "rare", Reward: new("title", "collector")),
        ];
    }

    /// <summary>Набор игры: первый запуск → значок, 5 модов → титул, 10 ч → рамка, копия → значок, 50 ч → баннер, всё → «Мастер».</summary>
    public static List<Achievement> ForGame(GameState g)
    {
        var id = g.Def.Id;
        var t = PlayTime.Get(id);
        long mods = 0, backups = 0;
        try { mods = g.ModCount; } catch { }
        try { backups = Backups.List(id).Count; } catch { }
        var hours = t.TotalMs / 3_600_000;
        var list = new List<Achievement>
        {
            new($"g:{id}:play", "Play", t.Sessions, 1, "common", id, new("badge", "game:" + id), "play"),
            new($"g:{id}:mods", "Package", mods, 5, "rare", id, new("title", "game:" + id), "mods"),
            new($"g:{id}:hours", "Clock", hours, 10, "rare", id, new("frame", "game:" + id), "hours"),
            new($"g:{id}:backup", "Save", backups, 1, "common", id, null, "backup"),
            new($"g:{id}:marathon", "Flame", hours, 50, "epic", id, new("banner", "game:" + id), "marathon"),
        };
        list.Add(new($"g:{id}:master", "Trophy", list.Count(a => a.Done), list.Count, "legend", id, new("title", "master:" + id), "master"));
        return list;
    }

    /// <summary>Наборы для экрана: общий и по играм, в которые уже играли (потом — остальные найденные).</summary>
    public static List<(string Set, List<Achievement> List)> Sets(List<Achievement>? all = null)
    {
        all ??= All();
        return all.GroupBy(a => a.Set)
            .Select(x => (x.Key, x.ToList()))
            .OrderByDescending(x => x.Key == Main)
            .ThenByDescending(x => x.Item2.Count(a => a.Done))
            .ThenByDescending(x => PlayTime.Get(x.Key).TotalMs)
            .ToList();
    }

    public static string SetName(string set) => set == Main ? "ModLaunch" : AppState.Games.FirstOrDefault(g => g.Def.Id == set)?.Def.Name ?? set;

    /// <summary>Название и описание: у игр — свои («Первое погружение» в Subnautica), иначе общее с именем игры.</summary>
    public static string Text(Achievement a, string suffix)
    {
        if (a.Set == Main) return I18n.T($"v98.ach.{a.Id}{suffix}");
        var game = AppState.Games.FirstOrDefault(g => g.Def.Id == a.Set)?.Def;
        var own = $"v99.g.{a.Set}.{a.Slot}{suffix}";
        var key = I18n.Has(own) ? own : $"v99.g.any.{a.Slot}{suffix}";
        return I18n.T(key, ("game", game?.ShortName ?? a.Set), ("n", a.Need));
    }

    /// <summary>Уровень: очки за открытые достижения (100 · 250 · 500 · 1000), по 300 на уровень.</summary>
    public static (int Level, int Xp, int Next) Level(List<Achievement> list)
    {
        var xp = list.Where(a => a.Done).Sum(a => a.Xp);
        return (1 + xp / 300, xp, 300 - xp % 300);
    }

    /// <summary>Итоги: часы, сеансы, топ игр по времени.</summary>
    public static (long Hours, long Sessions, List<(GameState Game, long Ms)> Top) Replay()
    {
        var rows = AppState.Games.Select(g => (Game: g, Ms: PlayTime.Get(g.Def.Id).TotalMs)).Where(x => x.Ms > 0).OrderByDescending(x => x.Ms).ToList();
        return (rows.Sum(r => r.Ms) / 3_600_000, AppState.Games.Sum(g => (long)PlayTime.Get(g.Def.Id).Sessions), rows.Take(5).ToList());
    }

    // ---------------------------------------------------------------- новые достижения

    /// <summary>
    /// Что открылось с прошлой проверки. В первый раз ничего не показываем — только запоминаем,
    /// что уже открыто (иначе после обновления посыпались бы десятки уведомлений).
    /// </summary>
    public static List<Achievement> TakeNew(List<Achievement>? all = null)
    {
        all ??= All();
        var profile = Settings.Data.Obj("profile");
        var first = profile["seen"] is not JsonArray;
        var seen = profile.Arr("seen");
        var known = seen.Select(n => n?.GetValue<string>()).ToHashSet();
        var fresh = all.Where(a => a.Done && !known.Contains(a.Id)).ToList();
        if (fresh.Count == 0 && !first) return [];
        foreach (var a in fresh) seen.Add(a.Id);
        profile["seen"] = seen.DeepClone();
        Settings.Save();
        return first ? [] : fresh;
    }
}

/// <summary>
/// 9.9: косметика профиля — что открыто наградами и что надето. Хранится в настройках
/// (profile.frame / banner / title / badges), видна в профиле, на кнопке аккаунта и в шапке главной.
/// </summary>
public static class Cosmetics
{
    public static readonly string[] Kinds = ["frame", "banner", "title", "badge"];

    /// <summary>То, что есть у всех с самого начала.</summary>
    static readonly Reward[] Starter = [new("frame", "basic"), new("banner", "accent"), new("title", "newbie")];

    public static HashSet<string> Unlocked(List<Achievement>? all = null)
    {
        all ??= Achievements.All();
        var set = Starter.Select(r => r.Key).ToHashSet();
        foreach (var a in all) if (a.Done && a.Reward is { } r) set.Add(r.Key);
        return set;
    }

    /// <summary>Все награды, которые можно получить: стартовые и за каждое достижение.</summary>
    public static List<(Reward Reward, Achievement? From)> Catalog(List<Achievement>? all = null)
    {
        all ??= Achievements.All();
        var list = Starter.Select(r => (r, (Achievement?)null)).ToList();
        foreach (var a in all) if (a.Reward is { } r) list.Add((r, a));
        return list;
    }

    public static string Equipped(string kind, HashSet<string>? unlocked = null)
    {
        var id = Settings.Data.Obj("profile").Str(kind);
        unlocked ??= Unlocked();
        return id is not null && unlocked.Contains(kind + ":" + id) ? id : Starter.First(r => r.Kind == kind).Id;
    }

    /// <summary>Значки на витрине профиля (до трёх, только открытые).</summary>
    public static List<string> Badges(HashSet<string>? unlocked = null)
    {
        unlocked ??= Unlocked();
        return Settings.Data.Obj("profile").Arr("badges").Select(n => n?.GetValue<string>() ?? "").Where(b => unlocked.Contains("badge:" + b)).Take(3).ToList();
    }

    public static bool IsOn(Reward r, HashSet<string>? unlocked = null) =>
        r.Kind == "badge" ? Badges(unlocked).Contains(r.Id) : Equipped(r.Kind, unlocked) == r.Id;

    /// <summary>Надеть награду; значок — добавить на витрину или убрать с неё (не больше трёх).</summary>
    public static void Toggle(Reward r)
    {
        var profile = Settings.Data.Obj("profile");
        if (r.Kind == "badge")
        {
            var list = Badges();
            if (!list.Remove(r.Id)) { list.Add(r.Id); if (list.Count > 3) list.RemoveAt(0); }
            profile["badges"] = new JsonArray(list.Select(b => (JsonNode)JsonValue.Create(b)!).ToArray());
        }
        else profile[r.Kind] = r.Id;
        Settings.Save();
        Changed?.Invoke();
    }

    public static event Action? Changed;

    /// <summary>Название награды: «Рамка „Золото“», «Титул „Мастер Subnautica“».</summary>
    public static string Name(Reward r)
    {
        var (key, game) = Split(r.Id);
        var name = game is not null
            ? I18n.T($"v99.{r.Kind}.{key}", ("game", AppState.Games.FirstOrDefault(g => g.Def.Id == game)?.Def.ShortName ?? game))
            : I18n.T($"v99.{r.Kind}.{key}");
        return name;
    }

    public static string KindName(string kind) => I18n.T($"v99.kind.{kind}");

    /// <summary>«game:subnautica» → («game», "subnautica»); «gold» → («gold», null).</summary>
    public static (string Key, string? Game) Split(string id)
    {
        var i = id.IndexOf(':');
        return i < 0 ? (id, null) : (id[..i], id[(i + 1)..]);
    }
}
