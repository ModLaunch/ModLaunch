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
        ("peak", "peak", "peak"), ("h3vr", "h3vr", null), ("content-warning", "content-warning", "contentwarning"),
        ("ultrakill", "ultrakill", "ultrakill"), ("rounds", "rounds", null), ("dyson-sphere-program", "dyson-sphere-program", "dysonsphereprogram"),
    ];

    /// <summary>5.5: все сообщества Thunderstore по числу модов и проверка API для новых функций.</summary>
    static async Task Probe()
    {
        var ids = new List<string>();
        string? url = "https://thunderstore.io/api/cyberstorm/community/?page_size=100";
        for (var guard = 0; url is not null && guard < 20; guard++)
        {
            try
            {
                var page = await Http.GetJson(url);
                ids.AddRange(page.Arr("results").Select(c => c.Str("identifier")).OfType<string>());
                url = page.Str("next");
            }
            catch (Exception e) { Console.WriteLine("communities page: " + e.Message); break; }
        }
        Console.WriteLine($"communities total: {ids.Count}");
        var have = Games.GameCatalog.Builtin.Select(g => g.ThunderstoreCommunity).OfType<string>().ToHashSet();
        var sized = new List<(string Id, long Total)>();
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(ids.Where(i => !have.Contains(i)).Select(async id =>
        {
            await gate.WaitAsync();
            try { var p = await Thunderstore.Search(id, new Query(), []); lock (sized) sized.Add((id, p.Total)); }
            catch { }
            finally { gate.Release(); }
        }));
        foreach (var (id, total) in sized.OrderByDescending(x => x.Total).Take(30))
        {
            var line = $"cand {id}: ts {total}";
            try
            {
                var packs = await Thunderstore.Search(id, new Query(Text: "BepInEx"), []);
                line += " | loaders: " + string.Join(", ", packs.Mods.Take(4).Select(m => $"{m.Id}({m.Downloads})"));
                var top = await Thunderstore.Search(id, new Query(), []);
                line += " | top: " + string.Join(" ", top.Mods.Take(16).Select(m => m.Id));
            }
            catch (Exception e) { line += " | err " + e.Message; }
            var domain = new string(id.Where(char.IsLetterOrDigit).ToArray());
            try
            {
                var q = await Nexus.Query("query($d: String!) { game(domainName: $d) { id name modCount } }", new JsonObject { ["d"] = domain }, default);
                line += $" | nexus {domain}: {q["game"]?.ToJsonString()}";
            }
            catch { }
            Console.WriteLine(line);
        }

        async Task Try(string name, string u)
        {
            try { var t = await Http.GetString(u); Console.WriteLine($"api {name}: OK {t.Length} {t[..Math.Min(300, t.Length)].Replace('\n', ' ')}"); }
            catch (Exception e) { Console.WriteLine($"api {name}: {e.Message}"); }
        }
        await Try("team listing", "https://thunderstore.io/api/cyberstorm/listing/lethal-company/notnotnotswipez/");
        await Try("changelog", "https://thunderstore.io/api/cyberstorm/package/notnotnotswipez/MoreCompany/latest/changelog/");
        await Try("readme", "https://thunderstore.io/api/cyberstorm/package/notnotnotswipez/MoreCompany/latest/readme/");
        await Try("versions", "https://thunderstore.io/api/cyberstorm/package/notnotnotswipez/MoreCompany/versions/");
        await Try("experimental", "https://thunderstore.io/api/experimental/package/notnotnotswipez/MoreCompany/");
        foreach (var (name, q) in new[]
        {
            ("nexus files changelog", "query { modFiles(modId: 1262, gameId: 1155) { fileId version changelogText } }"),
            ("nexus uploader", "query { mod(modId: 1262, gameId: 1155) { uploader { name memberId } } }"),
            ("nexus mods by uploader", "query($f: ModsFilter) { mods(filter: $f, count: 5) { totalCount nodes { modId name } } }"),
            ("nexus trending", "query($f: ModsFilter) { mods(filter: $f, count: 5, sort: [{ endorsements: { direction: DESC } }]) { totalCount nodes { modId name } } }"),
        })
        {
            try
            {
                var vars = new JsonObject { ["f"] = new JsonObject { ["gameDomainName"] = new JsonArray(new JsonObject { ["value"] = "subnautica", ["op"] = "EQUALS" }), ["uploaderId"] = new JsonArray(new JsonObject { ["value"] = "1", ["op"] = "EQUALS" }) } };
                var r = await Nexus.Query(q, name.Contains("uploader\u0020") || name.EndsWith("by uploader") ? vars : new JsonObject { ["f"] = new JsonObject { ["gameDomainName"] = new JsonArray(new JsonObject { ["value"] = "subnautica", ["op"] = "EQUALS" }) } }, default);
                var text = r.ToJsonString();
                Console.WriteLine($"api {name}: OK {text[..Math.Min(400, text.Length)]}");
            }
            catch (Exception e) { Console.WriteLine($"api {name}: {e.Message}"); }
        }
    }

    public static async Task<int> Run()
    {
        await Probe();
        try
        {
            var communities = await Http.GetJson("https://thunderstore.io/api/cyberstorm/community/?page_size=200");
            var slugs = communities.Arr("results").Select(c => c.Str("identifier")).OfType<string>().ToList();
            Console.WriteLine("thunderstore communities: " + string.Join(", ", slugs));
        }
        catch (Exception e) { Console.WriteLine("communities: ERROR " + e.Message); }
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
