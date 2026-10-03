using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Sources;

public sealed record MrProject(
    string Id, string Slug, string Title, string Description, string Author, string? Icon, long Downloads, long Follows,
    string Type, List<string> Categories, List<string> Loaders, List<string> GameVersions, DateTime? Updated, string? Color)
{
    public string Url => $"https://modrinth.com/{Type}/{Slug}";
}

public sealed record MrFile(string Url, string Name, bool Primary, long Size, string? Sha1);
public sealed record MrDep(string? ProjectId, string? VersionId, string Kind);
public sealed record MrVersion(string Id, string ProjectId, string Number, string Name, string Type, List<string> Loaders, List<string> GameVersions,
    List<MrFile> Files, List<MrDep> Deps, DateTime? Published, long Downloads, string Changelog)
{
    public MrFile? File => Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

/// <summary>Установленное с Modrinth: в какую папку, какой файл и какая версия.</summary>
public sealed record MrInstalled(string ProjectId, string Title, string Type, string VersionId, string Version, string File, string? Icon, DateTime Installed);

/// <summary>
/// Modrinth — открытая библиотека модов Minecraft (моды, модпаки, ресурс-паки,
/// шейдеры, датапаки, плагины). API v2 без ключа. Установка — в папку
/// Minecraft (по умолчанию %APPDATA%\.minecraft) с обязательными зависимостями.
/// </summary>
public static class Modrinth
{
    const string Api = "https://api.modrinth.com/v2";

    public static readonly string[] Types = ["mod", "modpack", "resourcepack", "shader", "datapack", "plugin"];
    public static readonly string[] Loaders = ["fabric", "forge", "neoforge", "quilt"];
    public static readonly string[] Sorts = ["relevance", "downloads", "follows", "newest", "updated"];

    static JsonObject Store => Settings.Data.Obj("modrinth");

    /// <summary>Папка Minecraft (или экземпляра Prism/MultiMC), куда ставим.</summary>
    public static string Folder
    {
        get => Store.Str("dir") is { Length: > 0 } d ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        set { Store["dir"] = value; Settings.Save(); }
    }

    public static string Loader { get => Store.Str("loader") ?? "fabric"; set { Store["loader"] = value; Settings.Save(); } }
    /// <summary>Версия игры; "" — любая.</summary>
    public static string GameVersion { get => Store.Str("version") ?? ""; set { Store["version"] = value; Settings.Save(); } }

    public static string SubFolder(string type) => type switch
    {
        "resourcepack" => "resourcepacks",
        "shader" => "shaderpacks",
        "datapack" => "datapacks",
        "plugin" => "plugins",
        "modpack" => "modpacks",
        _ => "mods",
    };

    /// <summary>Загрузчик важен только модам и модпакам; плагинам — свои ядра (paper, spigot…).</summary>
    static bool UsesLoader(string type) => type is "mod" or "modpack";

    public static List<string> Strs(JsonNode? n) => (n as JsonArray ?? []).Select(x => x?.ToString() ?? "").Where(x => x != "").ToList();
    static DateTime? Time(string? s) => DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    public static MrProject FromHit(JsonNode h) => new(
        h.Str("project_id") ?? h.Str("id") ?? "", h.Str("slug") ?? "", h.Str("title") ?? "", h.Str("description") ?? "",
        h.Str("author") ?? "", h.Str("icon_url") is { Length: > 0 } i ? i : null, h.Long("downloads"), h.Long("follows"),
        h.Str("project_type") ?? "mod", Strs(h["display_categories"] ?? h["categories"]), Strs(h["loaders"]),
        Strs(h["versions"] ?? h["game_versions"]), Time(h.Str("date_modified") ?? h.Str("updated")),
        h["color"] is JsonValue c && c.TryGetValue<long>(out var col) ? $"#{col:X6}" : null);

    /// <summary>Поиск: тип, загрузчик, версия игры, сортировка, страница по 24.</summary>
    public static async Task<(List<MrProject> Hits, long Total)> Search(string text, string type, string sort, int offset, CancellationToken ct = default)
    {
        var facets = new List<string> { $"[\"project_type:{type}\"]" };
        if (UsesLoader(type) && Loader != "") facets.Add($"[\"categories:{Loader}\"]");
        if (GameVersion != "") facets.Add($"[\"versions:{GameVersion}\"]");
        var url = $"{Api}/search?query={Uri.EscapeDataString(text)}&limit=24&offset={offset}&index={sort}&facets={Uri.EscapeDataString("[" + string.Join(",", facets) + "]")}";
        var json = await Http.GetJson(url, ct);
        return ((json?["hits"] as JsonArray ?? []).OfType<JsonNode>().Select(FromHit).ToList(), json.Long("total_hits"));
    }

    public static async Task<(MrProject Project, string Body, List<string> Gallery)> Project(string idOrSlug, CancellationToken ct = default)
    {
        var p = await Http.GetJson($"{Api}/project/{Uri.EscapeDataString(idOrSlug)}", ct) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
        var gallery = (p["gallery"] as JsonArray ?? []).Select(g => g.Str("url")).OfType<string>().ToList();
        var project = FromHit(p) with { Author = "" };
        return (project, p.Str("body") ?? "", gallery);
    }

    public static MrVersion FromVersion(JsonNode v) => new(
        v.Str("id") ?? "", v.Str("project_id") ?? "", v.Str("version_number") ?? "", v.Str("name") ?? "", v.Str("version_type") ?? "release",
        Strs(v["loaders"]), Strs(v["game_versions"]),
        (v["files"] as JsonArray ?? []).OfType<JsonNode>().Select(f => new MrFile(f.Str("url") ?? "", f.Str("filename") ?? "", f.Bool("primary"), f.Long("size"), f["hashes"].Str("sha1"))).ToList(),
        (v["dependencies"] as JsonArray ?? []).OfType<JsonNode>().Select(d => new MrDep(d.Str("project_id"), d.Str("version_id"), d.Str("dependency_type") ?? "required")).ToList(),
        Time(v.Str("date_published")), v.Long("downloads"), v.Str("changelog") ?? "");

    /// <summary>Версии проекта, подходящие под выбранные загрузчик и версию игры (свежие сверху).</summary>
    public static async Task<List<MrVersion>> Versions(string projectId, string type, bool filter = true, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (filter && UsesLoader(type) && Loader != "") q.Add("loaders=" + Uri.EscapeDataString($"[\"{Loader}\"]"));
        if (filter && GameVersion != "") q.Add("game_versions=" + Uri.EscapeDataString($"[\"{GameVersion}\"]"));
        var json = await Http.GetJson($"{Api}/project/{Uri.EscapeDataString(projectId)}/version" + (q.Count > 0 ? "?" + string.Join("&", q) : ""), ct);
        return (json as JsonArray ?? []).OfType<JsonNode>().Select(FromVersion).OrderByDescending(v => v.Published).ToList();
    }

    public static async Task<MrVersion> Version(string versionId, CancellationToken ct = default) =>
        FromVersion(await Http.GetJson($"{Api}/version/{Uri.EscapeDataString(versionId)}", ct) ?? throw new InvalidOperationException("version"));

    /// <summary>Версии Minecraft (только релизы) — для выбора.</summary>
    public static async Task<List<string>> GameVersions(CancellationToken ct = default)
    {
        if (Store["versionsCache"] is JsonArray cached && Store.Str("versionsAt") is { } at && DateTime.TryParse(at, out var when) && DateTime.UtcNow - when < TimeSpan.FromDays(3))
            return Strs(cached);
        var json = await Http.GetJson($"{Api}/tag/game_version", ct);
        var list = (json as JsonArray ?? []).OfType<JsonNode>().Where(v => v.Str("version_type") == "release").Select(v => v.Str("version") ?? "").Where(v => v != "").ToList();
        Store["versionsCache"] = new JsonArray(list.Select(v => (JsonNode)v).ToArray());
        Store["versionsAt"] = DateTime.UtcNow.ToString("o");
        Settings.Save();
        return list;
    }

    // ---------------------------------------------------------------- установленные

    public static List<MrInstalled> Installed() =>
        Store.Obj("installed").Select(kv => kv.Value is JsonObject o ? new MrInstalled(kv.Key, o.Str("title") ?? kv.Key, o.Str("type") ?? "mod", o.Str("versionId") ?? "",
            o.Str("version") ?? "", o.Str("file") ?? "", o.Str("icon"), DateTime.TryParse(o.Str("at"), out var at) ? at : DateTime.MinValue) : null)
        .OfType<MrInstalled>().OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static MrInstalled? InstalledOf(string projectId) => Installed().FirstOrDefault(i => i.ProjectId == projectId);

    static void Remember(MrProject p, MrVersion v, string file) =>
        Store.Obj("installed")[p.Id] = new JsonObject
        {
            ["title"] = p.Title, ["type"] = p.Type, ["versionId"] = v.Id, ["version"] = v.Number, ["file"] = file, ["icon"] = p.Icon, ["at"] = DateTime.UtcNow.ToString("o"),
        };

    /// <summary>Поставить проект (выбранную или самую свежую подходящую версию) и его обязательные зависимости.</summary>
    public static async Task<List<string>> Install(MrProject p, MrVersion? version = null, IProgress<string>? progress = null, CancellationToken ct = default, HashSet<string>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(p.Id)) return [];
        var v = version ?? (await Versions(p.Id, p.Type, ct: ct)).FirstOrDefault()
            ?? throw new InvalidOperationException(I18n.T("mr.noVersion", ("title", p.Title), ("loader", Loader), ("version", GameVersion == "" ? "*" : GameVersion)));
        var installed = new List<string>();
        foreach (var dep in v.Deps.Where(d => d.Kind == "required" && d.ProjectId is not null))
        {
            if (InstalledOf(dep.ProjectId!) is not null) continue;
            var (dp, _, _) = await Project(dep.ProjectId!, ct);
            var dv = dep.VersionId is not null ? await Version(dep.VersionId, ct) : null;
            installed.AddRange(await Install(dp, dv, progress, ct, seen));
        }
        var file = v.File ?? throw new InvalidOperationException(I18n.T("mr.noFile"));
        progress?.Report(p.Title);
        var temp = await Http.Download(file.Url, file.Name, ct: ct);
        var dir = Directory.CreateDirectory(Path.Combine(Folder, SubFolder(p.Type))).FullName;
        // Старая версия того же проекта — убрать, чтобы не было двух копий.
        if (InstalledOf(p.Id) is { } old && old.File != file.Name) { try { File.Delete(Path.Combine(dir, old.File)); } catch { } }
        File.Copy(temp, Path.Combine(dir, file.Name), true);
        Remember(p, v, file.Name);
        Settings.Save();
        installed.Add(p.Title);
        return installed;
    }

    public static void Remove(MrInstalled i)
    {
        try { File.Delete(Path.Combine(Folder, SubFolder(i.Type), i.File)); } catch { }
        Store.Obj("installed").Remove(i.ProjectId);
        Settings.Save();
    }

    /// <summary>Есть ли версия новее установленной (под текущие загрузчик и версию игры).</summary>
    public static async Task<List<(MrInstalled Item, MrVersion Latest)>> Updates(CancellationToken ct = default)
    {
        var list = new List<(MrInstalled, MrVersion)>();
        foreach (var i in Installed().Where(i => i.Type != "modpack"))
        {
            try
            {
                var latest = (await Versions(i.ProjectId, i.Type, ct: ct)).FirstOrDefault();
                if (latest is not null && latest.Id != i.VersionId) list.Add((i, latest));
            }
            catch { }
        }
        return list;
    }

    /// <summary>Файлы, которые лежат в папке, но поставлены не через ModLaunch — для честного счёта.</summary>
    public static int LooseFiles(string type)
    {
        var dir = Path.Combine(Folder, SubFolder(type));
        if (!Directory.Exists(dir)) return 0;
        var ours = Installed().Where(i => i.Type == type).Select(i => i.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(dir).Count(f => !ours.Contains(Path.GetFileName(f)));
    }
}
