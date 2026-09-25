using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Sources;

/// <summary>Версия мода: для вкладки «Файлы и версии» и установки старой версии.</summary>
public sealed record VersionInfo(string Version, DateTime? Date, long Downloads, string Changelog, string? Url, long FileId, long Size, string? FileName);

/// <summary>
/// То, чем сильны сайты модов: все версии со списками изменений и другие моды
/// того же автора — для Thunderstore, Nexus и ModLaunch Hub.
/// </summary>
public static class Extras
{
    public static async Task<List<VersionInfo>> Versions(GameDef game, ModInfo mod, CancellationToken ct = default)
    {
        switch (mod.Source)
        {
            case "thunderstore":
            {
                if (Thunderstore.Split(mod.Id) is not var (ns, name)) return [];
                var data = await Http.GetJson($"https://thunderstore.io/api/cyberstorm/package/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/versions/", ct, 20);
                return (data as JsonArray ?? []).OfType<JsonObject>().Select(v => new VersionInfo(
                    v.Str("version_number") ?? "", DateTime.TryParse(v.Str("datetime_created"), out var d) ? d.ToUniversalTime() : null,
                    v.Long("download_count"), "", v.Str("download_url"), 0, 0, null))
                    .OrderByDescending(v => v.Date).Take(40).ToList();
            }
            case "nexus":
            {
                if (!long.TryParse(mod.Id, out var mid) || game.NexusDomain is null) return [];
                var data = await Nexus.Query($"query {{ modFiles(modId: {mid}, gameId: {game.NexusGameId}) {{ fileId name version category date sizeInBytes uri changelogText }} }}", new JsonObject(), ct);
                return data.Arr("modFiles").OfType<JsonObject>()
                    .Where(f => f.Str("category") is not ("DELETED" or "ARCHIVED"))
                    .OrderByDescending(f => f.Long("date"))
                    .Select(f => new VersionInfo(
                        f.Str("version") is { Length: > 0 } v ? v : f.Str("name") ?? "",
                        f.Long("date") > 0 ? DateTimeOffset.FromUnixTimeSeconds(f.Long("date")).UtcDateTime : null, 0,
                        string.Join("\n", f.Arr("changelogText").Select(x => x?.ToString()).OfType<string>().Select(Nexus.Plain)),
                        null, f.Long("fileId"), f.Long("sizeInBytes"), f.Str("uri")))
                    .Take(40).ToList();
            }
            case "hub":
                return (await Creator.Hub.Versions(mod.Id)).Select(v => new VersionInfo(v.Version, v.Created, 0, v.Changelog, null, 0, v.Size, null)).ToList();
            default:
                return [];
        }
    }

    /// <summary>Другие моды этого автора для этой же игры.</summary>
    public static async Task<List<ModInfo>> ByAuthor(GameDef game, ModInfo mod, CancellationToken ct = default)
    {
        List<ModInfo> list;
        switch (mod.Source)
        {
            case "thunderstore":
            {
                if (Thunderstore.Split(mod.Id) is not var (ns, _) || game.ThunderstoreCommunity is null) return [];
                var data = await Http.GetJson($"https://thunderstore.io/api/cyberstorm/listing/{Uri.EscapeDataString(game.ThunderstoreCommunity)}/{Uri.EscapeDataString(ns)}/?ordering=most-downloaded", ct, 20);
                list = data.Arr("results").Where(i => i is not null && !i.Bool("is_nsfw") && !i.Bool("is_deprecated"))
                    .Select(i => Thunderstore.ToMod(i!, game.ThunderstoreCommunity)).ToList();
                break;
            }
            case "nexus":
            {
                if (!long.TryParse(mod.Id, out var mid) || game.NexusDomain is null) return [];
                var who = await Nexus.Query($"query {{ mod(modId: {mid}, gameId: {game.NexusGameId}) {{ uploader {{ memberId }} }} }}", new JsonObject(), ct);
                var member = who["mod"]?["uploader"].Long("memberId") ?? 0;
                if (member == 0) return [];
                var filter = new JsonObject
                {
                    ["gameDomainName"] = new JsonArray(new JsonObject { ["value"] = game.NexusDomain, ["op"] = "EQUALS" }),
                    ["uploaderId"] = new JsonArray(new JsonObject { ["value"] = member.ToString(), ["op"] = "EQUALS" }),
                    ["adultContent"] = new JsonArray(new JsonObject { ["value"] = false, ["op"] = "EQUALS" }),
                };
                var data = await Nexus.Query($"query($f: ModsFilter) {{ mods(filter: $f, count: 20, sort: [{{ downloads: {{ direction: DESC }} }}]) {{ nodes {{ {Nexus.ModFields} }} }} }}", new JsonObject { ["f"] = filter }, ct);
                list = data["mods"].Arr("nodes").Select(n => Nexus.FromNode(n, game.NexusDomain)).OfType<ModInfo>().ToList();
                break;
            }
            case "hub":
            {
                var me = await Creator.Hub.Get(mod.Id, ct);
                if (me is null) return [];
                list = (await Creator.Hub.All(ct: ct)).Where(m => m.Uid == me.Uid && m.Game == game.Id).Select(Creator.Hub.ToModInfo).ToList();
                break;
            }
            default:
                return [];
        }
        return list.Where(m => m.Id != mod.Id && !Catalog.IsHidden(game, m)).Take(12).ToList();
    }
}
