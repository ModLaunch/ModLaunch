using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch;

/// <summary>
/// --catalog-report: сколько модов у каждой игры в каждом источнике и какие
/// самые популярные. Нужен, чтобы выбирать «Нужные» моды по живым данным.
/// </summary>
public static class CatalogReport
{
    static readonly (string Game, string? Community, string? Domain)[] Targets =
    [
        ("stardew-valley", null, "stardewvalley"),
        ("hollow-knight", "hollow-knight", "hollowknight"),
        ("lethal-company", "lethal-company", "lethalcompany"),
        ("subnautica", "subnautica", "subnautica"),
        ("subnautica-below-zero", "subnautica-below-zero", "subnauticabelowzero"),
        ("valheim", "valheim", "valheim"),
        ("risk-of-rain-2", "riskofrain2", "riskofrain2"),
        ("repo", "repo", "repo"),
    ];

    public static async Task<int> Run()
    {
        var modlinks = (await ModLinks.Load()).Count;
        Console.WriteLine($"modlinks hollow-knight: {modlinks}");
        foreach (var (game, community, domain) in Targets)
        {
            Console.WriteLine($"=== {game}");
            if (community is not null)
            {
                try
                {
                    var page = await Thunderstore.Search(community, new Query(), []);
                    Console.WriteLine($"thunderstore {community}: {page.Total}");
                    var top = await Thunderstore.Search(community, new Query(Sort: SortBy.Rating), []);
                    Console.WriteLine("  top rated: " + string.Join(" | ", top.Mods.Take(24).Select(m => $"{m.Id} ({m.Rating})")));
                    Console.WriteLine("  top downloads: " + string.Join(" | ", page.Mods.Take(24).Select(m => $"{m.Id} ({m.Downloads})")));
                }
                catch (Exception e) { Console.WriteLine($"thunderstore {community}: ERROR {e.Message}"); }
            }
            if (domain is not null)
            {
                try
                {
                    var q = await Nexus.Query("query($d: String!) { game(domainName: $d) { id name modCount } }", new JsonObject { ["d"] = domain }, default);
                    Console.WriteLine($"nexus game {domain}: {q["game"]?.ToJsonString()}");
                }
                catch (Exception e) { Console.WriteLine($"nexus game {domain}: ERROR {e.Message}"); }
                try
                {
                    var page = await Nexus.Browse(domain, new Query(), [], []);
                    Console.WriteLine($"nexus {domain}: {page.Total}");
                    var top = await Nexus.Browse(domain, new Query(Sort: SortBy.Rating), [], []);
                    Console.WriteLine("  top endorsed: " + string.Join(" | ", top.Mods.Take(24).Select(m => $"{m.Id} {m.Name} ({m.Rating}, {m.UpdatedAt:yyyy-MM})")));
                    var fresh = await Nexus.Browse(domain, new Query(Page: 1, Sort: SortBy.Popular), [], []);
                    Console.WriteLine("  top downloads: " + string.Join(" | ", fresh.Mods.Take(24).Select(m => $"{m.Id} {m.Name} ({m.Downloads}, {m.UpdatedAt:yyyy-MM})")));
                }
                catch (Exception e) { Console.WriteLine($"nexus {domain}: ERROR {e.Message}"); }
            }
        }
        return 0;
    }
}
