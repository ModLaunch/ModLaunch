using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Sources;

public sealed record NexusFile(long FileId, string Name, string Version, string? FileName, long Size);

/// <summary>
/// Nexus Mods. Каталог — через открытый GraphQL v2 (без ключа). Скачивание:
/// с ключом Premium — прямой ссылкой по API; без Premium Nexus требует нажать
/// кнопку на сайте, поэтому открываем страницу файла и ждём архив в «Загрузках».
/// </summary>
public static partial class Nexus
{
    const string V1 = "https://api.nexusmods.com/v1";
    const string V2 = "https://api.nexusmods.com/v2/graphql";
    const int PageSize = 24;

    static Dictionary<string, string> AppHeaders => new()
    {
        ["Application-Name"] = "ModLaunch",
        ["Application-Version"] = Http.Version,
    };

    internal const string ModFields = "modId name summary author version pictureUrl thumbnailUrl thumbnailLargeUrl downloads endorsements adultContent updatedAt modCategory { name } uploader { name }";

    public static Task<JsonNode> Query(string query, JsonObject variables, CancellationToken ct) => GraphQl(query, variables, ct);

    static async Task<JsonNode> GraphQl(string query, JsonObject? variables = null, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var body = new JsonObject { ["query"] = query, ["variables"] = variables?.DeepClone() ?? new JsonObject() };
                var json = await Http.PostJson(V2, body, AppHeaders, ct);
                return json?["data"] ?? throw new InvalidOperationException(I18n.T("err.nexus.http", ("status", json?["errors"]?[0]?.Str("message") ?? "?")));
            }
            catch (HttpRequestException) when (attempt == 0 && !ct.IsCancellationRequested) { }
        }
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex Br();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex(@"\[[^\]]+\]")] private static partial Regex BbCode();

    public static string Plain(string? value)
    {
        var s = Br().Replace(value ?? "", "\n");
        s = Tag().Replace(s, "");
        s = System.Net.WebUtility.HtmlDecode(s);
        return BbCode().Replace(s, "").Trim();
    }

    internal static ModInfo? FromNode(JsonNode? node, string domain)
    {
        var id = node.Long("modId");
        if (id == 0) return null;
        return new ModInfo
        {
            Source = "nexus",
            Id = id.ToString(),
            Name = Plain(node.Str("name")) is { Length: > 0 } n ? n : $"#{id}",
            Author = node.Str("author") ?? node?["uploader"].Str("name") ?? "",
            Version = node.Str("version") ?? "",
            Description = Plain(node.Str("summary")),
            Icon = node.Str("thumbnailLargeUrl") ?? node.Str("pictureUrl") ?? node.Str("thumbnailUrl"),
            Url = $"https://www.nexusmods.com/{domain}/mods/{id}",
            Downloads = node.Long("downloads"),
            Rating = node.Long("endorsements"),
            UpdatedAt = DateTime.TryParse(node.Str("updatedAt"), out var d) ? d.ToUniversalTime() : null,
            Categories = node?["modCategory"].Str("name") is string c ? [c] : [],
        };
    }

    static JsonObject Sort(SortBy sort) => sort switch
    {
        SortBy.Rating => new() { ["endorsements"] = new JsonObject { ["direction"] = "DESC" } },
        SortBy.Updated or SortBy.New => new() { ["updatedAt"] = new JsonObject { ["direction"] = "DESC" } },
        _ => new() { ["downloads"] = new JsonObject { ["direction"] = "DESC" } },
    };

    static JsonObject Eq(string field, JsonNode value, string op = "EQUALS") =>
        new() { [field] = new JsonArray(new JsonObject { ["value"] = value, ["op"] = op }) };

    public static async Task<Page> Browse(string domain, Query q, string[] categories, string[] hide, CancellationToken ct = default)
    {
        var include = categories.Where(c => !c.StartsWith('!')).Take(12).ToArray();
        var exclude = categories.Where(c => c.StartsWith('!')).Select(c => c[1..]).ToArray();

        JsonObject BaseFilter()
        {
            var f = new JsonObject
            {
                ["gameDomainName"] = new JsonArray(new JsonObject { ["value"] = domain, ["op"] = "EQUALS" }),
                ["adultContent"] = new JsonArray(new JsonObject { ["value"] = false, ["op"] = "EQUALS" }),
            };
            var sub = new JsonArray();
            if (include.Length == 1) f["categoryName"] = new JsonArray(new JsonObject { ["value"] = include[0], ["op"] = "EQUALS" });
            else if (include.Length > 1)
                sub.Add(new JsonObject { ["op"] = "OR", ["filter"] = new JsonArray(include.Select(c => (JsonNode)Eq("categoryName", c)).ToArray()) });
            foreach (var c in exclude) sub.Add(Eq("categoryName", c, "NOT_EQUALS"));
            if (sub.Count > 0)
            {
                if (f["categoryName"] is JsonNode single) { f.Remove("categoryName"); sub.Add(new JsonObject { ["categoryName"] = single }); }
                f["filter"] = sub;
            }
            return f;
        }

        async Task<JsonNode?> Run(JsonObject? extra)
        {
            var filter = BaseFilter();
            foreach (var (k, v) in extra ?? new JsonObject()) filter[k] = v?.DeepClone();
            var data = await GraphQl(
                $"query($filter: ModsFilter, $sort: [ModsSort!], $count: Int, $offset: Int) {{ mods(filter: $filter, sort: $sort, count: $count, offset: $offset) {{ totalCount nodes {{ {ModFields} }} }} }}",
                new JsonObject
                {
                    ["filter"] = filter,
                    ["sort"] = new JsonArray(Sort(q.Sort)),
                    ["count"] = PageSize,
                    ["offset"] = (q.Page - 1) * PageSize,
                }, ct);
            return data["mods"];
        }

        var needle = q.Text.Trim();
        var result = await Run(needle == "" ? null : Eq("name", needle, "WILDCARD"));
        if (needle.Contains(' ') && result.Long("totalCount") == 0)
            result = await Run(Eq("nameStemmed", needle, "MATCHES"));

        var mods = result.Arr("nodes")
            .Where(n => !n.Bool("adultContent"))
            .Select(n => FromNode(n, domain))
            .OfType<ModInfo>()
            .Where(m => !hide.Contains(m.Id))
            .ToList();
        var total = result.Long("totalCount");
        return new Page(mods, total, q.Page * PageSize < total, q.Page);
    }

    public static async Task<(ModInfo? Mod, NexusFile? MainFile, List<(string Id, string Name)> Requirements)> Details(string domain, int gameId, string modId, CancellationToken ct = default)
    {
        if (!long.TryParse(modId, out var mid)) return (null, null, []);
        var data = await GraphQl($@"query {{
            mod(modId: {mid}, gameId: {gameId}) {{ {ModFields} modRequirements {{ nexusRequirements {{ nodes {{ modId modName gameId }} }} }} }}
            modFiles(modId: {mid}, gameId: {gameId}) {{ fileId name version category primary sizeInBytes date uri }}
        }}", ct: ct);
        var mod = FromNode(data["mod"], domain);
        var reqs = data["mod"]?["modRequirements"]?["nexusRequirements"].Arr("nodes")
            .Where(r => r.Long("modId") > 0 && (r.Long("gameId") == 0 || r.Long("gameId") == gameId))
            .Select(r => (r.Long("modId").ToString(), Plain(r.Str("modName"))))
            .ToList() ?? [];
        return (mod, PickMainFile(data.Arr("modFiles")), reqs);
    }

    /// <summary>То же, что Details, плюс описание мода (BBCode) — для страницы мода.</summary>
    public static async Task<(ModInfo? Mod, NexusFile? MainFile, List<(string Id, string Name)> Requirements, string Description)> DetailsFull(string domain, int gameId, string modId, CancellationToken ct = default)
    {
        if (!long.TryParse(modId, out var mid)) return (null, null, [], "");
        var data = await GraphQl($@"query {{
            mod(modId: {mid}, gameId: {gameId}) {{ {ModFields} description modRequirements {{ nexusRequirements {{ nodes {{ modId modName gameId }} }} }} }}
            modFiles(modId: {mid}, gameId: {gameId}) {{ fileId name version category primary sizeInBytes date uri }}
        }}", ct: ct);
        var mod = FromNode(data["mod"], domain);
        var reqs = data["mod"]?["modRequirements"]?["nexusRequirements"].Arr("nodes")
            .Where(r => r.Long("modId") > 0 && (r.Long("gameId") == 0 || r.Long("gameId") == gameId))
            .Select(r => (r.Long("modId").ToString(), Plain(r.Str("modName"))))
            .ToList() ?? [];
        return (mod, PickMainFile(data.Arr("modFiles")), reqs, data["mod"].Str("description") ?? "");
    }

    static NexusFile? PickMainFile(JsonArray files)
    {
        var usable = files.Where(f => f is not null && f.Str("category") is not ("OLD_VERSION" or "ARCHIVED" or "DELETED")).ToList();
        JsonNode? Newest(IEnumerable<JsonNode?> list) => list.OrderByDescending(f => f.Long("date")).FirstOrDefault();
        var pick = usable.FirstOrDefault(f => f.Long("primary") == 1 || f.Bool("primary"))
                   ?? Newest(usable.Where(f => f.Str("category") == "MAIN"))
                   ?? Newest(usable);
        if (pick is null) return null;
        return new NexusFile(pick.Long("fileId"), pick.Str("name") ?? "", pick.Str("version") ?? "", pick.Str("uri"), pick.Long("sizeInBytes"));
    }

    public static string FilePageUrl(string domain, string modId, long fileId) =>
        $"https://www.nexusmods.com/{domain}/mods/{modId}?tab=files&file_id={fileId}";

    /// <summary>Прямая ссылка на файл — только для Premium (или со ссылкой nxm с ключом).</summary>
    public static async Task<string> DownloadLink(string domain, string modId, long fileId, string apiKey, string? key = null, string? expires = null, CancellationToken ct = default)
    {
        var url = $"{V1}/games/{domain}/mods/{modId}/files/{fileId}/download_link.json";
        if (key is not null) url += $"?key={Uri.EscapeDataString(key)}&expires={Uri.EscapeDataString(expires ?? "")}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        foreach (var (k, v) in AppHeaders) request.Headers.TryAddWithoutValidation(k, v);
        using var response = await Http.Client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonArray;
        return data?.FirstOrDefault().Str("URI") ?? throw new InvalidOperationException(I18n.T("err.nexus.noLink"));
    }

    /// <summary>Сведения о моде и файле по ссылке nxm:// (нужен ключ API).</summary>
    public static async Task<(string Name, string Version, string Author, string? Icon, string? FileName)> FileInfo(string domain, string modId, long fileId, string apiKey, CancellationToken ct = default)
    {
        async Task<JsonNode?> Get(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("apikey", apiKey);
            foreach (var (k, v) in AppHeaders) request.Headers.TryAddWithoutValidation(k, v);
            using var response = await Http.Client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        var mod = await Get($"{V1}/games/{domain}/mods/{modId}.json");
        JsonNode? file = null;
        try { file = await Get($"{V1}/games/{domain}/mods/{modId}/files/{fileId}.json"); } catch { }
        return (Plain(mod.Str("name")) is { Length: > 0 } n ? n : $"#{modId}", file.Str("version") ?? mod.Str("version") ?? "",
            mod.Str("author") ?? mod.Str("uploaded_by") ?? "", mod.Str("picture_url"), file.Str("file_name"));
    }

    /// <summary>Одобрить мод на Nexus (как в Vortex). Нужен ключ; Nexus требует, чтобы мод был скачан.</summary>
    public static async Task Endorse(string domain, string modId, string version, string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{V1}/games/{domain}/mods/{modId}/endorse.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["version"] = version }),
        };
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        foreach (var (k, v) in AppHeaders) request.Headers.TryAddWithoutValidation(k, v);
        using var response = await Http.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode) return;
        string? message = null;
        try { message = JsonNode.Parse(body).Str("message"); } catch { }
        throw new InvalidOperationException($"Nexus {(int)response.StatusCode}: {message ?? response.ReasonPhrase}");
    }

    public static async Task<(string Name, bool Premium)> ValidateKey(string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{V1}/users/validate.json");
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        foreach (var (k, v) in AppHeaders) request.Headers.TryAddWithoutValidation(k, v);
        using var response = await Http.Client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return (data.Str("name") ?? "", data.Bool("is_premium"));
    }
}

public sealed record CollectionInfo(string Slug, string Name, string Summary, string? Image, string Author, long Endorsements, long Downloads, int ModCount, string Url);
public sealed record CollectionMod(string Id, long FileId, string Name, string Version, string? FileName, string? Icon, bool Optional);

/// <summary>Коллекции Nexus: список, поиск и состав ревизии с точными файлами.</summary>
public static partial class NexusCollections
{
    const int PageSize = 12;

    static CollectionInfo? From(System.Text.Json.Nodes.JsonNode? node, string domain, int? count = null)
    {
        var slug = node.Str("slug");
        if (slug is null) return null;
        return new CollectionInfo(slug,
            Nexus.Plain(node.Str("name")) is { Length: > 0 } n ? n : slug,
            Nexus.Plain(node.Str("summary")),
            node?["tileImage"].Str("url"),
            node?["user"].Str("name") ?? "",
            node.Long("endorsements"),
            node.Long("totalDownloads"),
            count ?? (int)(node?["latestPublishedRevision"].Long("modCount") ?? 0),
            $"https://www.nexusmods.com/games/{domain}/collections/{slug}");
    }

    public static async Task<(List<CollectionInfo> Items, long Total, bool HasMore)> Browse(string domain, int page, string query, CancellationToken ct = default)
    {
        var filter = new System.Text.Json.Nodes.JsonObject
        {
            ["gameDomain"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["value"] = domain, ["op"] = "EQUALS" }),
            ["adultContent"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["value"] = false, ["op"] = "EQUALS" }),
        };
        if (query.Trim() != "") filter["name"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["value"] = query.Trim(), ["op"] = "WILDCARD" });
        const string fields = "slug name summary endorsements totalDownloads tileImage { url } user { name } latestPublishedRevision { modCount adultContent }";
        async Task<System.Text.Json.Nodes.JsonNode> Run(bool sort)
        {
            var vars = new System.Text.Json.Nodes.JsonObject { ["filter"] = filter.DeepClone(), ["count"] = PageSize, ["offset"] = (page - 1) * PageSize };
            if (sort) vars["sort"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["endorsements"] = new System.Text.Json.Nodes.JsonObject { ["direction"] = "DESC" } });
            return await Nexus.Query(
                $"query($filter: CollectionsSearchFilter, $count: Int, $offset: Int{(sort ? ", $sort: [CollectionsSearchSort!]" : "")}) {{ collectionsV2(filter: $filter, count: $count, offset: $offset{(sort ? ", sort: $sort" : "")}) {{ totalCount nodes {{ {fields} }} }} }}",
                vars, ct);
        }
        System.Text.Json.Nodes.JsonNode data;
        try { data = await Run(true); } catch { data = await Run(false); }
        var result = data["collectionsV2"];
        var items = result.Arr("nodes").Where(n => !(n?["latestPublishedRevision"].Bool("adultContent") ?? false))
            .Select(n => From(n, domain)).OfType<CollectionInfo>().ToList();
        var total = result.Long("totalCount");
        return (items, total, page * PageSize < total);
    }

    [GeneratedRegex(@"/collections/([a-z0-9]{4,12})(?:[/?#]|$)", RegexOptions.IgnoreCase)] private static partial Regex FromUrl();
    [GeneratedRegex(@"^[a-z0-9]{4,12}$", RegexOptions.IgnoreCase)] private static partial Regex Bare();

    public static string? Slug(string input)
    {
        var text = input.Trim();
        if (FromUrl().Match(text) is { Success: true } m) return m.Groups[1].Value.ToLowerInvariant();
        return Bare().IsMatch(text) ? text.ToLowerInvariant() : null;
    }

    public static async Task<(CollectionInfo Info, List<CollectionMod> Mods)> Get(string domain, string input, CancellationToken ct = default)
    {
        var slug = Slug(input) ?? throw new InvalidOperationException(I18n.T("err.NEXUS_COLLECTION"));
        var data = await Nexus.Query(@"query($slug: String!, $domain: String) {
            collectionRevision(slug: $slug, domainName: $domain, viewAdultContent: false) {
              revisionNumber adultContent
              collection { slug name summary endorsements totalDownloads tileImage { url } user { name } game { domainName } }
              modFiles { optional fileId file { fileId name version sizeInBytes uri modId mod { modId name pictureUrl adultContent } } }
            }
          }", new System.Text.Json.Nodes.JsonObject { ["slug"] = slug, ["domain"] = domain }, ct);
        var revision = data["collectionRevision"];
        var collection = revision?["collection"] ?? throw new InvalidOperationException(I18n.T("err.NEXUS_COLLECTION"));
        if (collection["game"].Str("domainName") is string d && d != domain) throw new InvalidOperationException(I18n.T("err.NEXUS_COLLECTION_GAME"));
        var seen = new HashSet<string>();
        var mods = new List<CollectionMod>();
        foreach (var entry in revision.Arr("modFiles"))
        {
            var file = entry?["file"];
            var modId = file.Long("modId") is > 0 and var id ? id : file?["mod"].Long("modId") ?? 0;
            if (modId == 0 || !seen.Add(modId.ToString()) || (file?["mod"].Bool("adultContent") ?? false)) continue;
            var name = Nexus.Plain(file?["mod"].Str("name"));
            mods.Add(new CollectionMod(modId.ToString(), entry.Long("fileId") is > 0 and var f ? f : file.Long("fileId"),
                name != "" ? name : Nexus.Plain(file.Str("name")) is { Length: > 0 } fn ? fn : $"#{modId}",
                file.Str("version") ?? "", file.Str("uri"), file?["mod"].Str("pictureUrl"), entry.Bool("optional")));
        }
        return (From(collection, domain, mods.Count)!, mods);
    }
}
