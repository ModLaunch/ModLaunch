using System.Text.RegularExpressions;
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

    public static async Task<Page> Browse(GameDef game, Query q, CancellationToken ct = default, string? source = null)
    {
        source ??= game.PrimarySource;
        var section = Section.All.GetValueOrDefault(q.SectionId) ?? Section.All["all"];
        // «Лучшие» — весь каталог по оценкам.
        if (section.Special == "best") { q = q with { Sort = SortBy.Rating }; section = Section.All["all"]; }
        if (source == "none" || !game.Sources.Contains(source)) return new Page([], 0, false, q.Page);
        var primary = source == game.PrimarySource;
        Task<Page> Fetch(Query query) => source switch
        {
            "hub" => HubPage(game, query, section, ct),
            "thunderstore" => Thunderstore.Search(game.ThunderstoreCommunity!, query, primary ? section.Thunderstore ?? [] : [], ct),
            "modlinks" => ModLinks.Search(query, section.ModLinks ?? [], ct),
            _ => Nexus.Browse(game.NexusDomain!, query, primary ? game.NexusCategories.GetValueOrDefault(section.Id) ?? [] : [], game.NexusHide, ct),
        };

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
        "thunderstore" => "Thunderstore",
        "modlinks" => "ModLinks",
        "hub" => "ModLaunch Hub",
        _ => "Nexus Mods",
    };
}
