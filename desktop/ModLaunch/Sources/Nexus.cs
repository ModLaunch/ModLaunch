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

    const string ModFields = "modId name summary author version pictureUrl thumbnailUrl thumbnailLargeUrl downloads endorsements adultContent updatedAt modCategory { name } uploader { name }";

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

    static ModInfo? FromNode(JsonNode? node, string domain)
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
