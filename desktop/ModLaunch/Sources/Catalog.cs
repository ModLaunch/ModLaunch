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
        mod.Source == "nexus" ? game.NexusHide.Contains(mod.Id) : NotMods().IsMatch(mod.Id);

    public static async Task<Page> Browse(GameDef game, Query q, CancellationToken ct = default, string? source = null)
    {
        source ??= game.PrimarySource;
        var section = Section.All.GetValueOrDefault(q.SectionId) ?? Section.All["all"];
        // «Лучшие» — весь каталог по оценкам.
        if (section.Special == "best") { q = q with { Sort = SortBy.Rating }; section = Section.All["all"]; }
        if (source == "none" || !game.Sources.Contains(source)) return new Page([], 0, false, q.Page);
        var primary = source == game.PrimarySource;
        var page = source switch
        {
            "thunderstore" => await Thunderstore.Search(game.ThunderstoreCommunity!, q, primary ? section.Thunderstore ?? [] : [], ct),
            "modlinks" => await ModLinks.Search(q, section.ModLinks ?? [], ct),
            _ => await Nexus.Browse(game.NexusDomain!, q, primary ? game.NexusCategories.GetValueOrDefault(section.Id) ?? [] : [], game.NexusHide, ct),
        };
        return page with { Mods = page.Mods.Where(m => !IsHidden(game, m)).ToList() };
    }

    public static async Task<ModInfo?> Get(GameDef game, string id, CancellationToken ct = default, string? source = null) => (source ?? GuessSource(game, id)) switch
    {
        "thunderstore" => await Thunderstore.Get(game.ThunderstoreCommunity!, id, ct),
        "modlinks" => (await ModLinks.Load(ct)).FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)),
        _ => (await Nexus.Details(game.NexusDomain!, game.NexusGameId, id, ct)).Mod,
    };

    /// <summary>Номер Nexus — число, пакет Thunderstore — «Автор-Имя», у ModLinks — имя.</summary>
    public static string GuessSource(GameDef game, string id)
    {
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
        _ => "Nexus Mods",
    };
}
