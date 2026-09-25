using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Sources;

/// <summary>
/// Каталог Thunderstore: тот же открытый API, которым пользуется сам сайт
/// (cyberstorm) — постранично, с сортировкой и категориями.
/// </summary>
public static class Thunderstore
{
    const string Base = "https://thunderstore.io";
    static readonly ConcurrentDictionary<string, (DateTime At, ModInfo Mod)> Details = new();
    static readonly ConcurrentDictionary<string, Dictionary<string, string>> CategoryMaps = new();
    static readonly SemaphoreSlim Parallel = new(6);

    static string Ordering(SortBy sort) => sort switch
    {
        SortBy.Rating => "top-rated",
        SortBy.New => "newest",
        SortBy.Updated => "last-updated",
        _ => "most-downloaded",
    };

    public static (string Ns, string Name)? Split(string id)
    {
        // Имя пакета — без дефисов, а в имени автора они бывают (FunkFrog-and-Sipondo): делим по последнему.
        var dash = id.LastIndexOf('-');
        return dash <= 0 || dash == id.Length - 1 ? null : (id[..dash], id[(dash + 1)..]);
    }

    static ModInfo ToMod(JsonNode item, string community)
    {
        var ns = item.Str("namespace") ?? "";
        var name = item.Str("name") ?? "";
        var communityId = item.Str("community_identifier") ?? community;
        DateTime? updated = DateTime.TryParse(item.Str("last_updated") ?? item.Str("version_created"), out var d) ? d.ToUniversalTime() : null;
        return new ModInfo
        {
            Source = "thunderstore",
            Id = $"{ns}-{name}",
            Name = name.Replace('_', ' '),
            Author = ns,
            Version = item.Str("latest_version_number") ?? "",
            Description = item.Str("description") ?? "",
            Icon = item.Str("icon_url"),
            Url = $"{Base}/c/{communityId}/p/{ns}/{name}/",
            Downloads = item.Long("download_count"),
            Rating = item.Long("rating_count"),
            UpdatedAt = updated,
            Categories = item.Arr("categories").Select(c => c.Str("name")).OfType<string>().ToArray(),
        };
    }

    public static async Task<Page> Search(string community, Query q, string[] categorySlugs, CancellationToken ct = default)
    {
        var args = new List<string> { $"page={q.Page}", $"ordering={Ordering(q.Sort)}" };
        if (!string.IsNullOrWhiteSpace(q.Text)) args.Add("q=" + Uri.EscapeDataString(q.Text.Trim()));
        if (categorySlugs.Length > 0)
        {
            var ids = await CategoryIds(community, categorySlugs, ct);
            if (ids.Count == 0) return new Page([], 0, false, q.Page);
            args.AddRange(ids.Select(id => "included_categories=" + id));
        }
        var data = await Http.GetJson($"{Base}/api/cyberstorm/listing/{Uri.EscapeDataString(community)}/?{string.Join('&', args)}", ct, 20);
        var mods = data.Arr("results")
            .Where(i => i is not null && !i.Bool("is_nsfw") && !i.Bool("is_deprecated"))
            .Select(i => ToMod(i!, community))
            .ToList();
        return new Page(mods, data.Long("count"), data?["next"] is JsonValue, q.Page);
    }

    static async Task<List<string>> CategoryIds(string community, string[] slugs, CancellationToken ct)
    {
        if (!CategoryMaps.TryGetValue(community, out var map))
        {
            var data = await Http.GetJson($"{Base}/api/cyberstorm/community/{Uri.EscapeDataString(community)}/filters/", ct, 20);
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in data.Arr("package_categories"))
            {
                var slug = c.Str("slug");
                var id = c?["id"]?.ToString();
                if (slug is not null && id is not null) map[slug] = id;
            }
            CategoryMaps[community] = map;
        }
        return slugs.Select(s => map.GetValueOrDefault(s)).OfType<string>().ToList();
    }

    public static async Task<ModInfo?> Get(string community, string id, CancellationToken ct = default)
    {
        if (Split(id) is not var (ns, name)) return null;
        var key = $"{community}/{id}";
        if (Details.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(15)) return hit.Mod;

        await Parallel.WaitAsync(ct);
        JsonNode? data;
        try
        {
            data = await Http.GetJson($"{Base}/api/cyberstorm/listing/{Uri.EscapeDataString(community)}/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/", ct, 20);
        }
        catch (HttpRequestException e) when (e.Message.StartsWith("404")) { return null; }
        finally { Parallel.Release(); }
        if (data is null) return null;

        var mod = ToMod(data, community);
        mod.DownloadUrl = data.Str("download_url") ?? $"{Base}/package/download/{ns}/{name}/{mod.Version}/";
        mod.Dependencies = data.Arr("dependencies")
            .Where(d => d.Str("namespace") is not null && d.Str("name") is not null && !d.Bool("is_removed"))
            .Select(d => $"{d.Str("namespace")}-{d.Str("name")}")
            .ToList();
        Details[key] = (DateTime.UtcNow, mod);
        return mod;
    }

    /// <summary>Мод и все его зависимости в порядке установки (сначала зависимости).</summary>
    public static async Task<(List<ModInfo> Order, List<string> Missing)> Resolve(string community, string rootId, CancellationToken ct = default)
    {
        var order = new List<ModInfo>();
        var missing = new List<string>();
        var tasks = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        var gate = new object();

        Task Visit(string id, ImmutableTrail trail)
        {
            if (trail.Contains(id)) return Task.CompletedTask;
            lock (gate)
            {
                if (tasks.TryGetValue(id, out var existing)) return existing;
                var task = Run();
                tasks[id] = task;
                return task;
            }

            async Task Run()
            {
                var mod = await Get(community, id, ct).ConfigureAwait(false);
                if (mod is null) { lock (gate) missing.Add(id); return; }
                await Task.WhenAll(mod.Dependencies.Select(dep => Visit(dep, trail.With(id))));
                lock (gate) order.Add(mod);
            }
        }

        await Visit(rootId, ImmutableTrail.Empty);
        return (order, missing);
    }

    sealed class ImmutableTrail
    {
        public static readonly ImmutableTrail Empty = new(null, "");
        readonly ImmutableTrail? _parent;
        readonly string _id;
        ImmutableTrail(ImmutableTrail? parent, string id) { _parent = parent; _id = id; }
        public ImmutableTrail With(string id) => new(this, id);
        public bool Contains(string id)
        {
            for (var t = this; t is not null && t._parent is not null; t = t._parent)
                if (string.Equals(t._id, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>Последняя версия пакета (для загрузчика BepInExPack).</summary>
    public static async Task<(string Version, string Url)> Latest(string community, string fullName, CancellationToken ct = default)
    {
        if (Split(fullName) is not var (ns, name)) throw new ArgumentException(fullName);
        try
        {
            var one = await Http.GetJson($"{Base}/api/experimental/package/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/", ct, 20);
            var latest = one?["latest"];
            if (latest.Str("download_url") is string url) return (latest.Str("version_number") ?? "", url);
        }
        catch when (!ct.IsCancellationRequested) { }

        var list = await Http.GetJson($"{Base}/c/{community}/api/v1/package/", ct, 45) as JsonArray;
        var pkg = list?.FirstOrDefault(p => p.Str("owner") == ns && p.Str("name") == name);
        var version = pkg.Arr("versions").FirstOrDefault();
        if (version.Str("download_url") is not string dl)
            throw new InvalidOperationException(I18n.T("err.bepinex.noPack", ("community", community), ("pkg", fullName)));
        return (version.Str("version_number") ?? "", dl);
    }
}
