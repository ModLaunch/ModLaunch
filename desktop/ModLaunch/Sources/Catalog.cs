using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Sources;

/// <summary>
/// Каталог игры — один вход для всех источников (Thunderstore, Nexus, ModLinks).
/// Источник выбирается в каталоге; по умолчанию — основной для игры.
/// </summary>
public static partial class Catalog
{
    /// <summary>Не моды, а программы и загрузчики: в каталоге они только мешают.</summary>
    [GeneratedRegex(@"^(ebkr-r2modman|Kesomannen-GaleModManager|.*-BepInExPack.*|BepInEx-.*|bbepis-BepInExPack|RiskofThunder-RoR2BepInExPack|Subnautica_Modding-(QModManager|SMLHelper).*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NotMods();

    public static bool IsHidden(GameDef game, ModInfo mod) =>
        (mod.Source == "nexus" ? game.NexusHide.Contains(mod.Id) : mod.Source != "hub" && NotMods().IsMatch(mod.Id))
        || Features.Blocklist.Hides(game.Id, mod);

    /// <summary>26.0: «Все источники» — каталоги игры (Nexus, Thunderstore, ModLinks, ModLaunch Hub) одним списком.</summary>
    public const string AllSources = "all";

    /// <summary>Источники, которые объединяет «Все источники» (у игры их больше одного, кроме Hub).</summary>
    public static bool HasSeveral(GameDef game) => game.Sources.Count(s => s != "hub") > 1;

    public static async Task<Page> Browse(GameDef game, Query q, CancellationToken ct = default, string? source = null)
    {
        source ??= game.PrimarySource;
        if (source == AllSources) return await BrowseAll(game, q, ct);
        var section = Section.All.GetValueOrDefault(q.SectionId) ?? Section.All["all"];
        // «Лучшие» — весь каталог по оценкам.
        if (section.Special == "best") { q = q with { Sort = SortBy.Rating }; section = Section.All["all"]; }
        if (source == "none" || !game.Sources.Contains(source)) return new Page([], 0, false, q.Page);
        var primary = source == game.PrimarySource;
        Task<Page> Raw(Query query) => source switch
        {
            "hub" => HubPage(game, query, section, ct),
            "thunderstore" => Thunderstore.Search(game.ThunderstoreCommunity!, query, primary ? section.Thunderstore ?? [] : [], ct),
            "modlinks" => ModLinks.Search(query, section.ModLinks ?? [], ct),
            _ => Nexus.Browse(game.NexusDomain!, query, primary ? game.NexusCategories.GetValueOrDefault(section.Id) ?? [] : [], game.NexusHide, ct),
        };
        // 26.0: текст запроса — через умный поиск (русские слова, множественное число, поиск по словам).
        Task<Page> Fetch(Query query) => query.Text.Trim() == "" ? Raw(query) : Smart(query, Raw);

        Page page;
        if (q.Sort == SortBy.Random)
        {
            // «Случайные», как на Nexus: случайная страница популярного списка, перемешанная.
            var popular = q with { Sort = SortBy.Popular, Page = 1 };
            page = await Fetch(popular);
            var size = Math.Max(1, page.Mods.Count);
            var pages = (int)Math.Clamp((page.Total + size - 1) / size, 1, 60);
            if (pages > 1) page = await Fetch(popular with { Page = Random.Shared.Next(1, pages + 1) });
            page = page with { Mods = page.Mods.OrderBy(_ => Random.Shared.Next()).ToList(), HasMore = true, Number = q.Page };
        }
        else page = await Fetch(q);

        IEnumerable<ModInfo> mods = page.Mods.Where(m => !IsHidden(game, m));
        if (q.Period > 0) mods = mods.Where(m => m.UpdatedAt is null || m.UpdatedAt >= DateTime.UtcNow.AddDays(-q.Period));
        if (q.Sort == SortBy.Name && source != "nexus") mods = mods.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase);
        return page with { Mods = mods.ToList() };
    }

    /// <summary>
    /// 26.0: поиск, который находит. Сначала — вся фраза в понятном каталогу виде («рандомные враги» → «rando enemy»);
    /// пусто — исходный текст как есть (вдруг это точное название); снова пусто, а слов несколько — каждое слово
    /// отдельно, и в список идут моды, где совпало больше всего слов (совпадение в названии — выше).
    /// </summary>
    static async Task<Page> Smart(Query q, Func<Query, Task<Page>> raw)
    {
        var plan = SearchWords.Make(q.Text);
        var first = await raw(q with { Text = plan.Query });
        if (first.Mods.Count > 0 || q.Page > 1) return first;
        if (plan.Changed(q.Text))
        {
            var original = await raw(q);
            if (original.Mods.Count > 0) return original;
        }
        if (plan.Keywords.Count < 2) return first;
        var pages = await Task.WhenAll(plan.Keywords.Take(4).Select(async k =>
        {
            try { return (await raw(q with { Text = k, Page = 1 })).Mods; }
            catch { return []; }
        }));
        var found = pages.SelectMany(p => p).GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Select(m => (Mod: m, Score: SearchWords.Score(m, plan), Name: SearchWords.NameScore(m, plan))).ToList();
        if (found.Count == 0) return first;
        // Нашлись моды со всеми словами — только они; иначе — с лучшим числом совпадений.
        var best = found.Max(x => x.Score);
        var list = found.Where(x => x.Score >= Math.Max(1, best)).OrderByDescending(x => x.Score).ThenByDescending(x => x.Name)
            .ThenByDescending(x => x.Mod.Downloads).Select(x => x.Mod).Take(40).ToList();
        return new Page(list, list.Count, false, 1);
    }

    /// <summary>
    /// 26.0: «Все источники»: та же страница из каждого каталога игры, одним списком без повторов (мод, выложенный
    /// и на Nexus, и на Thunderstore, показывается один раз — где у него больше скачиваний). Один каталог не ответил —
    /// показываем остальные; не ответил ни один — ошибка первого.
    /// </summary>
    static async Task<Page> BrowseAll(GameDef game, Query q, CancellationToken ct)
    {
        var sources = game.Sources.Where(s => s != "none" && s != AllSources).ToList();
        // Разделы есть только у основного каталога: во «Всех источниках» — весь каталог или «Лучшие».
        var query = q with { SectionId = q.SectionId == "best" ? "best" : "all" };
        Exception? error = null;
        var pages = await Task.WhenAll(sources.Select(async s =>
        {
            try { return await Browse(game, query, ct, s); }
            catch (Exception e) when (!ct.IsCancellationRequested) { error ??= e; return null; }
        }));
        var got = pages.OfType<Page>().ToList();
        if (got.Count == 0) throw error ?? new InvalidOperationException(I18n.T("catalog.error"));
        var merged = got.SelectMany(p => p.Mods).GroupBy(SameKey).Select(g => g.OrderByDescending(m => m.Downloads).First()).ToList();
        var text = q.Text.Trim();
        IEnumerable<ModInfo> ordered;
        if (text != "")
        {
            var plan = SearchWords.Make(text);
            ordered = merged.OrderByDescending(m => SearchWords.Score(m, plan)).ThenByDescending(m => SearchWords.NameScore(m, plan)).ThenByDescending(m => m.Downloads);
        }
        else ordered = (query.SectionId == "best" ? SortBy.Rating : q.Sort) switch
        {
            SortBy.Rating => merged.OrderByDescending(m => m.Rating).ThenByDescending(m => m.Downloads),
            SortBy.New or SortBy.Updated => merged.OrderByDescending(m => m.UpdatedAt ?? DateTime.MinValue),
            SortBy.Name => merged.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase),
            SortBy.Random => merged.OrderBy(_ => Random.Shared.Next()),
            _ => merged.OrderByDescending(m => m.Downloads),
        };
        return new Page(ordered.ToList(), got.Sum(p => p.Total), got.Any(p => p.HasMore), q.Page);
    }

    /// <summary>Один и тот же мод на разных сайтах: имя без пробелов, знаков и регистра.</summary>
    static string SameKey(ModInfo m) => new string((m.Name ?? m.Id).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()) is { Length: > 2 } k ? k : m.Source + ":" + m.Id;

    /// <summary>ModLaunch Hub как каталог игры: поиск, сортировка и «Лучшие» — на месте.</summary>
    static async Task<Page> HubPage(GameDef game, Query q, Section section, CancellationToken ct)
    {
        List<Creator.HubMod> all;
        try { all = await Creator.Hub.All(ct: ct); }
        catch (Exception e) { throw new InvalidOperationException(Social.Firebase.Explain("creator", e)); }
        var text = q.Text.Trim();
        var list = all.Where(m => m.Game == game.Id && (text == "" || m.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || m.Summary.Contains(text, StringComparison.OrdinalIgnoreCase) || m.Author.Contains(text, StringComparison.OrdinalIgnoreCase)));
        var sorted = q.Sort == SortBy.Name
            ? list.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
            : Creator.Hub.Sort(list, q.Sort switch { SortBy.Rating => "likes", SortBy.New => "new", SortBy.Updated => "updated", _ => "downloads" }).ToList();
        const int size = 20;
        var mods = sorted.Skip((q.Page - 1) * size).Take(size).Select(Creator.Hub.ToModInfo).ToList();
        return new Page(mods, sorted.Count, q.Page * size < sorted.Count, q.Page);
    }

    public static async Task<ModInfo?> Get(GameDef game, string id, CancellationToken ct = default, string? source = null) => (source ?? GuessSource(game, id)) switch
    {
        "hub" => await Creator.Hub.Get(id, ct) is { } h ? Creator.Hub.ToModInfo(h) : null,
        "thunderstore" => await Thunderstore.Get(game.ThunderstoreCommunity!, id, ct),
        "modlinks" => (await ModLinks.Load(ct)).FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)),
        _ => (await Nexus.Details(game.NexusDomain!, game.NexusGameId, id, ct)).Mod,
    };

    /// <summary>Номер Nexus — число, пакет Thunderstore — «Автор-Имя», у ModLinks — имя.</summary>
    public static string GuessSource(GameDef game, string id)
    {
        // Номер мода в ModLaunch Hub: «uid_имя», uid Firebase — 20+ букв и цифр.
        if (System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]{20,}_[a-z0-9_]+$")) return "hub";
        if (id.All(char.IsDigit) && game.NexusDomain is not null) return "nexus";
        if (game.Catalog == CatalogKind.ModLinks) return "modlinks";
        return game.ThunderstoreCommunity is not null && id.Contains('-') ? "thunderstore" : game.PrimarySource;
    }

    /// <summary>Несколько модов по номерам — для подборки «Нужные» и наборов. Порядок сохраняется.</summary>
    public static async Task<List<ModInfo>> Many(GameDef game, IEnumerable<string> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().Take(80).ToList();
        var result = new ModInfo?[list.Count];
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(list.Select(async (id, i) =>
        {
            await gate.WaitAsync(ct);
            try { result[i] = await Get(game, id, ct); }
            catch { result[i] = null; }
            finally { gate.Release(); }
        }));
        return result.OfType<ModInfo>().ToList();
    }

    public static string Title(string source) => source switch
    {
        AllSources => I18n.T("v26.src.all"),
        "thunderstore" => "Thunderstore",
        "modlinks" => "ModLinks",
        "hub" => "ModLaunch Hub",
        "modrinth" => "Modrinth",
        _ => "Nexus Mods",
    };
}
