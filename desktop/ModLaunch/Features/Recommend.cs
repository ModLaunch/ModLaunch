using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>Совет «вам может понравиться»: мод, игра и почему он здесь («у вас стоит X»).</summary>
public sealed record Suggestion(GameState Game, ModInfo Mod, string Reason, string Seed, double Score);

/// <summary>
/// 9.5: рекомендации по вашим модам. Кандидаты — популярные моды и подборка ModLaunch для игры
/// (то, что уже загружено для ленты, — без лишних запросов). Сигналы, от сильного к слабому:
/// 1) мод работает на библиотеке, которая у вас уже стоит («Работает с вашим X»);
/// 2) тот же автор, что у вашего мода («От автора X»);
/// 3) общие разделы каталога с вашими модами («Похоже на X»).
/// Уже установленное, скрытое и устаревшее не советуем.
/// </summary>
public static class Recommend
{
    /// <summary>Что у игры стоит: имя, автор, id в каталоге (без «nexus:…» и версии).</summary>
    public sealed record Owned(string Key, string Name, string Author, string[] Categories);

    public static List<Owned> OwnedOf(GameState g, IEnumerable<ModInfo> known)
    {
        var byId = known.GroupBy(m => m.Id).ToDictionary(x => x.Key, x => x.First());
        var list = new List<Owned>();
        foreach (var r in g.Registry?.List() ?? [])
        {
            if (r.Bool("missing")) continue;
            var id = r.Str("id") ?? "";
            var key = g.Def.CatalogId(id) ?? id;
            var name = r.Str("name") ?? key;
            var author = r.Str("author") ?? "";
            // Разделы: из каталога (если мод есть среди кандидатов) или записанные при установке (9.5).
            var cats = byId.TryGetValue(key, out var m) && m.Categories.Length > 0 ? m.Categories
                : r["categories"] is System.Text.Json.Nodes.JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x != "").ToArray() : [];
            list.Add(new Owned(key, name, author, cats));
        }
        return list;
    }

    /// <summary>«Author-Name-1.2.3» → «Author-Name» (зависимости Thunderstore с версией).</summary>
    public static string Bare(string dep)
    {
        var parts = dep.Split('-');
        return parts.Length >= 3 && char.IsDigit(parts[^1].FirstOrDefault()) ? string.Join('-', parts[..^1]) : dep;
    }

    /// <summary>Советы для одной игры по её установленным модам.</summary>
    public static List<Suggestion> For(GameState g, IEnumerable<ModInfo> candidates, IReadOnlyList<Owned> owned, Func<string, bool>? isInstalled = null)
    {
        isInstalled ??= id => Views.Actions.IsInstalled(g, id);
        var result = new List<Suggestion>();
        if (owned.Count == 0) return result;
        var ownedKeys = owned.Select(o => o.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var libraries = owned.ToDictionary(o => o.Key, o => o, StringComparer.OrdinalIgnoreCase);
        var authors = owned.Where(o => o.Author != "").GroupBy(o => o.Author, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var m in candidates.GroupBy(c => c.Id).Select(x => x.First()))
        {
            if (ownedKeys.Contains(m.Id) || isInstalled(m.Id) || m.Deprecated || Blocklist.Hides(g.Def.Id, m)) continue;
            Owned? seed = null;
            string? reason = null;
            double score = 0;
            // 1. Работает на том, что уже стоит (не считаем общий загрузчик вроде BepInExPack — он стоит у всех).
            var dep = m.Dependencies.Select(Bare).FirstOrDefault(d => libraries.ContainsKey(d) && !d.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase));
            if (dep is not null) { seed = libraries[dep]; reason = I18n.T("rec.uses", ("mod", seed.Name)); score = 3; }
            // 2. Тот же автор.
            else if (m.Author != "" && authors.TryGetValue(m.Author, out var byAuthor)) { seed = byAuthor; reason = I18n.T("rec.author", ("author", m.Author), ("mod", byAuthor.Name)); score = 2; }
            // 3. Общие разделы.
            else
            {
                var best = owned.Select(o => (Owned: o, Common: o.Categories.Intersect(m.Categories, StringComparer.OrdinalIgnoreCase).Count()))
                    .Where(x => x.Common > 0).OrderByDescending(x => x.Common).FirstOrDefault();
                if (best.Owned is not null) { seed = best.Owned; reason = I18n.T("rec.like", ("mod", best.Owned.Name)); score = 1 + 0.2 * Math.Min(3, best.Common); }
            }
            if (seed is null || reason is null) continue;
            score += Math.Log10(Math.Max(10, m.Downloads)) / 10;
            result.Add(new Suggestion(g, m, reason, seed.Name, score));
        }
        return result.OrderByDescending(s => s.Score).ToList();
    }

    /// <summary>Советы по всем играм: из того, что уже загружено (популярное и подборки), по очереди из разных игр.</summary>
    public static List<Suggestion> All(IEnumerable<GameState> games, Func<GameState, IEnumerable<ModInfo>> candidates, int max = 12)
    {
        var lists = new List<List<Suggestion>>();
        foreach (var g in games)
        {
            var pool = candidates(g).ToList();
            if (pool.Count == 0) continue;
            var s = For(g, pool, OwnedOf(g, pool));
            if (s.Count > 0) lists.Add(s);
        }
        var mixed = new List<Suggestion>();
        for (var i = 0; mixed.Count < max && lists.Any(l => l.Count > i); i++)
            foreach (var l in lists.OrderByDescending(l => l[0].Score))
                if (l.Count > i && mixed.Count < max) mixed.Add(l[i]);
        return mixed;
    }

    [SelfTest]
    static string SuggestsByLibraryAuthorAndCategory()
    {
        var g = AppState.Games.First(x => !x.Def.IsMinecraft);
        var owned = new List<Owned>
        {
            new("Evaisa-LethalLib", "LethalLib", "Evaisa", ["libraries"]),
            new("x753-More_Suits", "More Suits", "x753", ["suits", "cosmetics"]),
        };
        ModInfo M(string id, string author, long dl, string[] cats, params string[] deps) =>
            new() { Source = "thunderstore", Id = id, Name = id.Split('-')[^1], Author = author, Downloads = dl, Categories = cats, Dependencies = [.. deps] };
        var pool = new[]
        {
            M("Someone-NewMoons", "Someone", 50_000, ["moons"], "Evaisa-LethalLib-0.16.1", "BepInEx-BepInExPack-5.4.2100"),
            M("x753-Mimics", "x753", 900_000, ["monsters"]),
            M("Other-CoolSuits", "Other", 200_000, ["suits"]),
            M("Other-Unrelated", "Other", 5_000_000, ["audio"]),
            M("x753-More_Suits", "x753", 1_000_000, ["suits"]),
        };
        var s = For(g, pool, owned, _ => false);
        if (s.Count != 3) throw new Exception($"{s.Count} suggestions: " + string.Join(", ", s.Select(x => x.Mod.Id)));
        if (s[0].Mod.Id != "Someone-NewMoons" || s[1].Mod.Id != "x753-Mimics" || s[2].Mod.Id != "Other-CoolSuits") throw new Exception("wrong order: " + string.Join(", ", s.Select(x => x.Mod.Id)));
        if (s.Any(x => x.Mod.Id is "Other-Unrelated" or "x753-More_Suits")) throw new Exception("suggested unrelated or installed");
        if (Bare("BepInEx-BepInExPack-5.4.2100") != "BepInEx-BepInExPack" || Bare("Author-Name") != "Author-Name") throw new Exception("Bare");
        return "library > author > category; installed and unrelated skipped";
    }
}
