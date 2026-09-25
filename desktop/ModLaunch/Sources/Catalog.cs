using ModLaunch.Games;

namespace ModLaunch.Sources;

/// <summary>Каталог игры — один вход, а источник выбирается по описанию игры.</summary>
public static class Catalog
{
    public static Task<Page> Browse(GameDef game, Query q, CancellationToken ct = default)
    {
        var section = Section.All.GetValueOrDefault(q.SectionId) ?? Section.All["all"];
        return game.Catalog switch
        {
            CatalogKind.Thunderstore => Thunderstore.Search(game.ThunderstoreCommunity!, q, section.Thunderstore ?? [], ct),
            CatalogKind.ModLinks => ModLinks.Search(q, section.ModLinks ?? [], ct),
            _ => Nexus.Browse(game.NexusDomain!, q, game.NexusCategories.GetValueOrDefault(section.Id) ?? [], game.NexusHide, ct),
        };
    }

    public static async Task<ModInfo?> Get(GameDef game, string id, CancellationToken ct = default) => game.Catalog switch
    {
        CatalogKind.Thunderstore => await Thunderstore.Get(game.ThunderstoreCommunity!, id, ct),
        CatalogKind.ModLinks => (await ModLinks.Load(ct)).FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)),
        _ => (await Nexus.Details(game.NexusDomain!, game.NexusGameId, id, ct)).Mod,
    };

    /// <summary>Несколько модов по номерам — для подборки «Нужные» и наборов. Порядок сохраняется.</summary>
    public static async Task<List<ModInfo>> Many(GameDef game, IEnumerable<string> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().Take(60).ToList();
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
}
