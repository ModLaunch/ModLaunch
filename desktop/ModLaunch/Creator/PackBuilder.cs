using System.Text;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Creator;

/// <summary>Цель сборки: какие разделы каталога и какие слова в названии/описании ей подходят.</summary>
public sealed record PackGoal(string Id, string Icon, string Color, string[] Sections, string[] Words);

/// <summary>Мод в будущей сборке: почему выбран, чем можно заменить, включён ли.</summary>
public sealed class PackPick
{
    public required ModInfo Mod;
    public required string Goal;
    public double Score;
    public readonly List<string> Reasons = [];
    public readonly List<ModInfo> Alternatives = [];
    public bool Installed;
    public bool Stale;
    public bool On = true;
}

public sealed record PackPlan(GameDef Game, List<PackPick> Picks, List<ModInfo> Libraries, List<string> Warnings, List<string> Goals)
{
    public IEnumerable<PackPick> Chosen => Picks.Where(p => p.On);
    public int Total => Chosen.Count() + Libraries.Count;
}

/// <summary>
/// Интеллектуальный конструктор сборок: по целям («удобство», «графика»,
/// «кооп»…) и размеру подбирает моды из каталога игры. Каждый кандидат получает
/// оценку — популярность, рейтинг, свежесть и совпадение с целью; похожие моды
/// (две версии одного и того же) не попадают вместе — остаётся лучший, второй
/// становится заменой. Цели делят сборку поровну, библиотеки подтягиваются
/// сами. Всё на месте, без внешних сервисов: только открытые каталоги.
/// </summary>
public static partial class PackBuilder
{
    public static readonly PackGoal[] Goals =
    [
        new("qol", Views.Icons.Sparkles, "#3478F6", ["gameplay", "ui"], ["quality", "qol", "better", "improved", "tweak", "inventory", "stack", "auto", "quick", "convenien", "shortcut", "remember"]),
        new("content", Views.Icons.Layers, "#8B5CF6", ["content", "items", "buildings", "vehicles"], ["new", "more", "expansion", "items", "monsters", "moons", "maps", "biome", "weapons"]),
        new("visuals", Views.Icons.Palette, "#EC4899", ["visuals", "ui"], ["graphic", "texture", "hd", "lighting", "shader", "visual", "reshade", "remaster", "4k"]),
        new("coop", Views.Icons.Users, "#22C55E", ["gameplay", "tools"], ["multiplayer", "coop", "co-op", "players", "lobby", "company", "friends", "sync", "party", "crew"]),
        new("hardcore", Views.Icons.Flame, "#EF4444", ["gameplay", "content"], ["hard", "difficulty", "realistic", "survival", "hardcore", "challenge", "permadeath", "brutal"]),
        new("cosmetics", Views.Icons.Star, "#F59E0B", ["cosmetics"], ["cosmetic", "skin", "suit", "hat", "outfit", "emote"]),
        new("audio", Views.Icons.Music, "#06B6D4", ["audio"], ["sound", "music", "audio", "voice", "boombox"]),
        new("fixes", Views.Icons.Wrench, "#64748B", ["gameplay", "tools"], ["fix", "performance", "optimi", "fps", "bug", "lag", "stutter", "crash"]),
    ];

    public static PackGoal Goal(string id) => Goals.FirstOrDefault(g => g.Id == id) ?? Goals[0];

    static readonly HashSet<string> Stop = ["mod", "mods", "more", "better", "the", "and", "for", "a", "an", "of", "plus", "lite", "fix", "fixed", "api", "lib", "v2", "redux", "remastered", "edition"];

    [GeneratedRegex(@"(?<=[a-zа-я])(?=[A-ZА-Я])|[^\p{L}\p{N}]+")] private static partial Regex WordSplit();

    /// <summary>Слова названия без шума: «MoreCompanyPlus» → company.</summary>
    static HashSet<string> Words(string name) =>
        WordSplit().Split(name).Select(w => w.ToLowerInvariant()).Where(w => w.Length > 1 && !Stop.Contains(w)).ToHashSet();

    /// <summary>Похожи ли два мода настолько, что это скорее «одно и то же» (форки, версии, аналоги).</summary>
    public static bool Similar(ModInfo a, ModInfo b)
    {
        if (a.Id == b.Id) return true;
        var x = Words(a.Name);
        var y = Words(b.Name);
        if (x.Count == 0 || y.Count == 0) return false;
        var common = x.Intersect(y).Count();
        var jaccard = (double)common / x.Union(y).Count();
        return jaccard >= 0.6 || (common >= 1 && (x.IsSubsetOf(y) || y.IsSubsetOf(x)) && Math.Min(x.Count, y.Count) >= 2);
    }

    static bool IsLibrary(ModInfo m) => m.Categories.Any(c => Regex.IsMatch(c, "librar|api|tools?$|utilit|modpack", RegexOptions.IgnoreCase))
        || Regex.IsMatch(m.Name, @"\b(lib|library|api|core|framework)\b", RegexOptions.IgnoreCase);

    /// <summary>Найти кандидатов для цели: её разделы каталога (популярные и лучшие) плюс поиск по ключевым словам.</summary>
    static async Task<List<ModInfo>> Candidates(GameDef game, PackGoal goal, bool adult, CancellationToken ct)
    {
        var sections = goal.Sections.Where(s => game.Sections.Any(x => x.Id == s)).DefaultIfEmpty("all").Distinct().ToList();
        var queries = new List<Query>();
        foreach (var s in sections)
        {
            queries.Add(new Query(SectionId: s, Sort: SortBy.Popular, Adult: adult));
            queries.Add(new Query(SectionId: s, Sort: SortBy.Rating, Adult: adult));
        }
        foreach (var w in goal.Words.Take(3)) queries.Add(new Query(Text: w, Sort: SortBy.Popular, Adult: adult));
        var pages = await Task.WhenAll(queries.Select(async q =>
        {
            try { return Program.Demo ? Demo.Catalog(game, q with { Text = "" }).Mods : (await Catalog.Browse(game, q, ct)).Mods; }
            catch when (!ct.IsCancellationRequested) { return []; }
        }));
        return pages.SelectMany(p => p).Where(m => adult || !m.Adult).DistinctBy(m => m.Id).ToList();
    }

    static double Freshness(ModInfo m)
    {
        if (m.UpdatedAt is not { } at) return 0.5;
        var days = (DateTime.UtcNow - at).TotalDays;
        return days < 90 ? 1 : days < 365 ? 0.75 : days < 730 ? 0.45 : 0.15;
    }

    /// <summary>Собрать план. registry — что уже стоит (такие моды отмечаются, но в счёт не идут дважды).</summary>
    public static async Task<PackPlan> Build(GameDef game, IReadOnlyList<string> goalIds, int size, bool adult, ModRegistry? registry, CancellationToken ct = default)
    {
        var goals = goalIds.Select(Goal).DistinctBy(g => g.Id).ToList();
        if (goals.Count == 0) goals = [Goals[0]];
        size = Math.Clamp(size, 3, 80);

        var pools = await Task.WhenAll(goals.Select(g => Candidates(game, g, adult, ct)));
        var maxDownloads = pools.SelectMany(p => p).Select(m => m.Downloads).DefaultIfEmpty(1).Max();
        var maxRating = pools.SelectMany(p => p).Select(m => m.Rating).DefaultIfEmpty(1).Max();
        var installed = registry?.List().Select(r => r.Str("id")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        // Каждый мод — в одну цель, туда, где он подходит лучше всего.
        var scored = new Dictionary<string, PackPick>();
        for (var gi = 0; gi < goals.Count; gi++)
        {
            var goal = goals[gi];
            foreach (var mod in pools[gi])
            {
                if (IsLibrary(mod)) continue;
                var text = (mod.Name + " " + mod.Description).ToLowerInvariant();
                var hits = goal.Words.Where(w => text.Contains(w)).ToList();
                var nameHit = goal.Words.Any(w => mod.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
                var pop = Math.Log10(mod.Downloads + 10) / Math.Log10(maxDownloads + 10);
                var rate = maxRating <= 0 ? 0 : Math.Log10(mod.Rating + 1) / Math.Log10(maxRating + 1);
                var fresh = Freshness(mod);
                var score = 0.45 * pop + 0.25 * rate + 0.2 * fresh + Math.Min(0.3, hits.Count * 0.08 + (nameHit ? 0.1 : 0));
                if (scored.TryGetValue(mod.Id, out var had) && had.Score >= score) continue;
                var pick = new PackPick { Mod = mod, Goal = goal.Id, Score = score, Installed = installed.Contains(mod.Id) || installed.Contains(game.RecordId(mod.Id, mod.Source)), Stale = fresh <= 0.15 };
                if (mod.Downloads > 0 && pop > 0.75) pick.Reasons.Add(I18n.T("pb.why.popular", ("n", I18n.Compact(mod.Downloads))));
                if (rate > 0.7) pick.Reasons.Add(I18n.T("pb.why.rated"));
                if (fresh >= 1 && mod.UpdatedAt is { } u) pick.Reasons.Add(I18n.T("pb.why.fresh", ("when", Views.Ui.Ago(u))));
                if (hits.Count > 0) pick.Reasons.Add(I18n.T("pb.why.match", ("words", string.Join(", ", hits.Take(3)))));
                if (pick.Installed) pick.Reasons.Add(I18n.T("pb.why.installed"));
                if (pick.Stale) pick.Reasons.Add(I18n.T("pb.why.stale"));
                scored[mod.Id] = pick;
            }
        }

        // Похожие моды вместе не берём: лучший остаётся, остальные — замены.
        var ordered = scored.Values.OrderByDescending(p => p.Score).ToList();
        var unique = new List<PackPick>();
        foreach (var p in ordered)
        {
            var twin = unique.FirstOrDefault(u => Similar(u.Mod, p.Mod));
            if (twin is null) unique.Add(p);
            else if (twin.Alternatives.Count < 4) twin.Alternatives.Add(p.Mod);
        }

        // Цели делят сборку поровну, остаток — лучшим из оставшихся.
        var quota = (int)Math.Ceiling((double)size / goals.Count);
        var chosen = new List<PackPick>();
        foreach (var goal in goals)
            chosen.AddRange(unique.Where(p => p.Goal == goal.Id).Take(quota));
        chosen = chosen.OrderByDescending(p => p.Score).Take(size).ToList();
        if (chosen.Count < size) chosen.AddRange(unique.Except(chosen).Take(size - chosen.Count));
        // Запасные — то, что не вошло: их можно включить вручную.
        var spare = unique.Except(chosen).Take(Math.Max(6, size / 2)).ToList();
        foreach (var s in spare) s.On = false;

        var plan = new PackPlan(game, [.. chosen, .. spare], [], [], goals.Select(g => g.Id).ToList());
        await ResolveLibraries(plan, ct);
        Warn(plan);
        return plan;
    }

    /// <summary>Библиотеки, без которых выбранные моды не работают (Thunderstore, ModLinks).</summary>
    public static async Task ResolveLibraries(PackPlan plan, CancellationToken ct = default)
    {
        plan.Libraries.Clear();
        if (Program.Demo)
        {
            plan.Libraries.AddRange(Demo.Many(plan.Game, ["lib1", "lib2", "lib3"]).Select((m, i) => new ModInfo
            {
                Source = m.Source, Id = m.Id, Name = new[] { "LethalLib", "CSync", "BepInEx Config Manager" }[i], Author = "lib", Categories = ["Libraries"],
            }));
            return;
        }
        var chosen = plan.Chosen.Select(p => p.Mod).ToList();
        var ids = chosen.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(6);
        var orders = await Task.WhenAll(chosen.Where(m => m.Source is "thunderstore" or "modlinks").Select(async m =>
        {
            await gate.WaitAsync(ct);
            try { return (await Installer.Plan(plan.Game, m, ct)).Order; }
            catch when (!ct.IsCancellationRequested) { return []; }
            finally { gate.Release(); }
        }));
        plan.Libraries.AddRange(orders.SelectMany(o => o).Where(m => !ids.Contains(m.Id)).DistinctBy(m => m.Id).OrderBy(m => m.Name));
    }

    static void Warn(PackPlan plan)
    {
        plan.Warnings.Clear();
        var chosen = plan.Chosen.ToList();
        if (chosen.Count == 0) { plan.Warnings.Add(I18n.T("pb.warn.empty")); return; }
        var stale = chosen.Count(p => p.Stale);
        if (stale > 0) plan.Warnings.Add(I18n.T("pb.warn.stale", ("n", stale)));
        if (plan.Total > 60) plan.Warnings.Add(I18n.T("pb.warn.big", ("n", plan.Total)));
        if (chosen.Any(p => p.Mod.Source == "nexus") && !Settings.NexusPremium) plan.Warnings.Add(I18n.T("pb.warn.nexus"));
        var twins = chosen.SelectMany((a, i) => chosen.Skip(i + 1).Where(b => Similar(a.Mod, b.Mod)).Select(b => (a, b))).ToList();
        foreach (var (a, b) in twins.Take(3)) plan.Warnings.Add(I18n.T("pb.warn.twins", ("a", a.Mod.Name), ("b", b.Mod.Name)));
    }

    /// <summary>Пересчитать библиотеки и предупреждения после ручных правок.</summary>
    public static async Task Refresh(PackPlan plan, CancellationToken ct = default)
    {
        await ResolveLibraries(plan, ct);
        Warn(plan);
    }

    /// <summary>Сборка как скрипт ModScript: можно опубликовать в хабе, у скачавших поставятся все моды.</summary>
    public static string ToScript(PackPlan plan, string name)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Сборка из конструктора ModLaunch: {string.Join(", ", plan.Goals.Select(g => I18n.T("pb.goal." + g)))}");
        sb.AppendLine($"mod \"{name.Replace("\"", "'")}\"");
        sb.AppendLine("version 1.0.0");
        sb.AppendLine($"game {plan.Game.Id}");
        sb.AppendLine($"about \"{I18n.T("pb.script.about", ("n", plan.Chosen.Count()))}\"");
        foreach (var p in plan.Chosen) sb.AppendLine($"needs {p.Mod.Id}   # {p.Mod.Name}");
        return sb.ToString();
    }
}
