using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>Недостающая зависимость: кому нужна, чего нет и как это поставить из каталога.</summary>
public sealed class Missing
{
    public required string Mod { get; init; }
    public required string ModId { get; init; }
    public required string Name { get; set; }
    public required string Id { get; init; }
    public required string Kind { get; init; } // manifest | nexus
    public string? ResolveId { get; set; }     // номер в каталоге этой игры
}

/// <summary>
/// Зависимости модов. Что «есть» — считаем щедро: номер в списке, UniqueID из
/// manifest.json любого мода в папке, имя папки. Так мод, поставленный руками,
/// тоже закрывает зависимость.
/// </summary>
public static partial class Deps
{
    [GeneratedRegex(@"\(.*?\)")] private static partial Regex Brackets();
    [GeneratedRegex(@"[^a-z0-9а-яё]+", RegexOptions.IgnoreCase)] private static partial Regex NotWord();
    [GeneratedRegex(@"^nexus:[^:]+:(\d+)$")] private static partial Regex NexusRecord();
    [GeneratedRegex(@"-bepinexpack(_\w+)?$", RegexOptions.IgnoreCase)] private static partial Regex LoaderPack();

    public static string Norm(string? value) => NotWord().Replace(Brackets().Replace((value ?? "").ToLowerInvariant(), ""), "");

    static HashSet<string> ManifestIds(string modsDir)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(modsDir)) return ids;
        void Visit(string dir, int depth)
        {
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); } catch { return; }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (File.Exists(entry) && name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var text = Regex.Replace(File.ReadAllText(entry).TrimStart('﻿'), @",\s*([}\]])", "$1");
                        if (JsonNode.Parse(text).Str("UniqueID") is string id) ids.Add(id);
                    }
                    catch { }
                }
                else if (Directory.Exists(entry) && depth < 3 && !name.StartsWith('.')) Visit(entry, depth + 1);
            }
        }
        Visit(modsDir, 0);
        return ids;
    }

    public sealed record Present(HashSet<string> Ids, HashSet<string> Names);

    public static Present PresentKeys(IEnumerable<JsonObject> records, string modsDir, IEnumerable<string> loaderIds)
    {
        var ids = new HashSet<string>(loaderIds, StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>();
        foreach (var r in records)
        {
            if (r.Bool("missing")) continue;
            var id = r.Str("id") ?? "";
            ids.Add(id);
            if (NexusRecord().Match(id) is { Success: true } m) ids.Add("nexus#" + m.Groups[1].Value);
            if (r.Str("uniqueId") is string u) ids.Add(u);
            names.Add(Norm(r.Str("name")));
            names.Add(Norm(r.Str("folder")));
        }
        ids.UnionWith(ManifestIds(modsDir));
        try
        {
            if (Directory.Exists(modsDir))
                foreach (var entry in Directory.EnumerateFileSystemEntries(modsDir))
                    names.Add(Norm(Regex.Replace(Path.GetFileName(entry), @"\.dll$", "", RegexOptions.IgnoreCase)));
        }
        catch { }
        names.Remove("");
        return new Present(ids, names);
    }

    public static List<Missing> Find(ModRegistry registry)
    {
        var game = registry.Game;
        var records = registry.List();
        var present = PresentKeys(records, registry.ModsDir, game.ThunderstorePackage is null ? [] : [game.ThunderstorePackage]);
        var hidden = game.NexusHide.ToHashSet();
        var seen = new HashSet<string>();
        var result = new List<Missing>();
        foreach (var r in records)
        {
            if (r.Bool("missing") || !r.Bool("enabled", true)) continue;
            var rid = r.Str("id") ?? "";
            var rname = r.Str("name") ?? rid;
            foreach (var dep in r.Arr("dependencies").Select(d => d?.ToString()?.Trim()).OfType<string>().Where(d => d != ""))
            {
                if (present.Ids.Contains(dep) || present.Names.Contains(Norm(dep))) continue;
                if (LoaderPack().IsMatch(dep) || dep.Equals("smapi", StringComparison.OrdinalIgnoreCase) || dep.Equals("Pathoschild.SMAPI", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add($"{rid}|{dep.ToLowerInvariant()}")) continue;
                result.Add(new Missing { Mod = rname, ModId = rid, Name = dep, Id = dep, Kind = "manifest" });
            }
            foreach (var req in r.Arr("requires"))
            {
                var id = req.Str("id") ?? "";
                if (id == "" || hidden.Contains(id)) continue;
                if (present.Ids.Contains("nexus#" + id) || present.Names.Contains(Norm(req.Str("name")))) continue;
                if (!seen.Add($"{rid}|nexus#{id}")) continue;
                result.Add(new Missing { Mod = rname, ModId = rid, Name = req.Str("name") ?? "#" + id, Id = id, Kind = "nexus", ResolveId = id });
            }
        }
        return result;
    }

    /// <summary>Недостающее с номерами в каталоге: у Stardew UniqueID → номер Nexus через smapi.io.</summary>
    public static async Task<List<Missing>> FindResolved(ModRegistry registry, CancellationToken ct = default)
    {
        var missing = Find(registry);
        var manifest = missing.Where(m => m.Kind == "manifest").ToList();
        if (manifest.Count == 0) return missing;
        if (registry.Game.Catalog == CatalogKind.Nexus)
        {
            var found = await SmapiIndex.Lookup(manifest.Select(m => m.Id), ct);
            foreach (var m in manifest)
            {
                if (!found.TryGetValue(m.Id, out var hit) || hit.NexusId is null || registry.Game.NexusHide.Contains(hit.NexusId)) continue;
                m.ResolveId = hit.NexusId;
                if (hit.Name is not null) m.Name = hit.Name;
            }
        }
        else foreach (var m in manifest) m.ResolveId = m.Id;
        return missing;
    }

    /// <summary>Требования мода Nexus (и их требования), которых ещё нет, — самые глубокие первыми.</summary>
    public static async Task<List<(string Id, string Name)>> NexusPlan(GameDef game, ModRegistry registry, string modId, CancellationToken ct)
    {
        var present = PresentKeys(registry.List(), registry.ModsDir, []);
        var hidden = game.NexusHide.ToHashSet();
        var order = new List<(string, string)>();
        var visited = new HashSet<string> { modId };
        async Task Visit(string id, int depth)
        {
            if (depth > 3 || order.Count >= 12) return;
            List<(string Id, string Name)> reqs;
            try { reqs = (await Nexus.Details(game.NexusDomain!, game.NexusGameId, id, ct)).Requirements; } catch { return; }
            foreach (var (rid, name) in reqs)
            {
                if (hidden.Contains(rid) || !visited.Add(rid)) continue;
                if (present.Ids.Contains("nexus#" + rid) || present.Names.Contains(Norm(name))) continue;
                await Visit(rid, depth + 1);
                order.Add((rid, name));
            }
        }
        await Visit(modId, 0);
        return order;
    }
}

/// <summary>UniqueID мода Stardew → номер на Nexus (API smapi.io), с недельным кэшем.</summary>
public static class SmapiIndex
{
    static readonly JsonFile Cache = new(Path.Combine(Paths.DataDir, "smapi-index.json"), () => new JsonObject { ["items"] = new JsonObject() });

    public static async Task<Dictionary<string, (string? NexusId, string? Name)>> Lookup(IEnumerable<string> uniqueIds, CancellationToken ct)
    {
        var items = Cache.Data.Obj("items");
        var result = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        var ask = new List<string>();
        foreach (var id in uniqueIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (items[id.ToLowerInvariant()] is JsonObject hit && DateTime.UtcNow - DateTime.Parse(hit.Str("at") ?? "2000-01-01").ToUniversalTime() < TimeSpan.FromDays(7))
                result[id] = (hit.Str("nexusId"), hit.Str("name"));
            else ask.Add(id);
        }
        if (ask.Count == 0) return result;
        try
        {
            var body = new JsonObject
            {
                ["mods"] = new JsonArray(ask.Take(50).Select(id => (JsonNode)new JsonObject { ["id"] = id, ["updateKeys"] = new JsonArray() }).ToArray()),
                ["apiVersion"] = "4.0.0",
                ["gameVersion"] = "1.6.15",
                ["platform"] = "Windows",
                ["includeExtendedMetadata"] = true,
            };
            if (await Http.PostJson("https://smapi.io/api/v3.0/mods", body, ct: ct, timeoutSec: 15) is JsonArray list)
            {
                foreach (var item in list)
                {
                    var id = item.Str("id");
                    if (id is null) continue;
                    var meta = item?["metadata"];
                    var nexusId = meta?["nexusID"]?.ToString() ?? meta?["nexusId"]?.ToString();
                    var name = meta.Str("name");
                    items[id.ToLowerInvariant()] = new JsonObject { ["nexusId"] = nexusId, ["name"] = name, ["at"] = DateTime.UtcNow.ToString("o") };
                    result[id] = (nexusId, name);
                }
                Cache.Save();
            }
        }
        catch { /* сайт SMAPI недоступен — недостающее можно поставить руками */ }
        return result;
    }
}
