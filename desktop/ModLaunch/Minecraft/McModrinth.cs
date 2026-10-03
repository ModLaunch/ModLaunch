using System.IO.Compression;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Minecraft;

/// <summary>
/// Каталог Modrinth под сборку: только то, что подходит к её версии и загрузчику; установка
/// с обязательными зависимостями; модпаки (.mrpack) — сразу новой сборкой.
/// </summary>
public static class McModrinth
{
    const string Api = "https://api.modrinth.com/v2";

    public static readonly string[] Types = ["mod", "modpack", "resourcepack", "shader", "datapack"];
    public static readonly string[] Sorts = ["relevance", "downloads", "follows", "newest", "updated"];

    static JsonArray Facet(params string[] any) => new(any.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray());

    /// <summary>Фильтры поиска: вид, загрузчик (у модов), версия игры (у всего, кроме модпаков — они создают свою сборку).</summary>
    public static JsonArray Facets(string type, McInstance? i, string category = "")
    {
        var facets = new JsonArray { Facet($"project_type:{type}") };
        if (i is not null && type == "mod" && i.Modded)
            facets.Add(i.Loader == "quilt" ? Facet("categories:quilt", "categories:fabric") : Facet($"categories:{i.Loader}"));
        if (i is not null && type is "mod" or "resourcepack" or "shader" or "datapack" && i.GameVersion != "") facets.Add(Facet($"versions:{i.GameVersion}"));
        if (category != "") facets.Add(Facet($"categories:{category}"));
        return facets;
    }

    public static async Task<(List<MrProject> Hits, long Total)> Search(string text, string type, string sort, int offset, McInstance? i, string category = "", CancellationToken ct = default)
    {
        if (Program.Demo) return McDemo.Hits(type);
        var url = $"{Api}/search?query={Uri.EscapeDataString(text)}&limit=20&offset={offset}&index={sort}&facets={Uri.EscapeDataString(Facets(type, i, category).ToJsonString())}";
        var json = await Http.GetJson(url, ct);
        return ((json?["hits"] as JsonArray ?? []).OfType<JsonNode>().Select(Modrinth.FromHit).ToList(), json.Long("total_hits"));
    }

    public static MrVersion FromVersion(JsonNode v) => Modrinth.FromVersion(v);

    /// <summary>Подходящие сборке версии проекта (свежие сверху).</summary>
    public static async Task<List<MrVersion>> Versions(string projectId, string kind, McInstance? i, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (i is not null && kind != "modpack")
        {
            var loaders = kind == "mod" ? (i.Modded ? McContent.LoadersFor(i, kind) : []) : kind == "datapack" ? ["datapack"] : [];
            if (loaders.Length > 0) q.Add("loaders=" + Uri.EscapeDataString(new JsonArray(loaders.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()).ToJsonString()));
            if (i.GameVersion != "") q.Add("game_versions=" + Uri.EscapeDataString(new JsonArray(JsonValue.Create(i.GameVersion)).ToJsonString()));
        }
        var json = await Http.GetJson($"{Api}/project/{Uri.EscapeDataString(projectId)}/version" + (q.Count > 0 ? "?" + string.Join("&", q) : ""), ct);
        return (json as JsonArray ?? []).OfType<JsonNode>().Select(FromVersion)
            .OrderBy(v => v.Type == "release" ? 0 : v.Type == "beta" ? 1 : 2)
            .ThenByDescending(v => v.Published)
            .ToList();
    }

    public static async Task<MrProject> Project(string idOrSlug, CancellationToken ct = default) =>
        Modrinth.FromHit(await Http.GetJson($"{Api}/project/{Uri.EscapeDataString(idOrSlug)}", ct) ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog")));

    public static async Task<Dictionary<string, MrProject>> Projects(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var result = new Dictionary<string, MrProject>();
        foreach (var chunk in ids.Distinct().Chunk(100))
        {
            var url = $"{Api}/projects?ids={Uri.EscapeDataString(new JsonArray(chunk.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()).ToJsonString())}";
            foreach (var p in (await Http.GetJson(url, ct) as JsonArray ?? []).OfType<JsonNode>().Select(Modrinth.FromHit)) result[p.Id] = p;
        }
        return result;
    }

    // ---------------------------------------------------------------- установка в сборку

    /// <summary>Поставить проект в сборку с обязательными зависимостями. Возвращает, что поставлено.</summary>
    public static async Task<List<string>> Install(McInstance i, MrProject p, MrVersion? version = null, IProgress<InstallStep>? progress = null, CancellationToken ct = default, HashSet<string>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(p.Id)) return [];
        var kind = p.Type is "resourcepack" or "shader" or "datapack" ? p.Type : "mod";
        if (Program.Demo) { McDemo.Install(i, p); return [p.Title]; }
        var v = version ?? (await Versions(p.Id, kind, i, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException(I18n.T("mine.err.noVersion", ("title", p.Title), ("build", i.Label)));
        var installed = new List<string>();
        var have = McContent.InstalledProjects(i);
        var deps = v.Deps.Where(d => d.Kind == "required" && d.ProjectId is not null && !have.Contains(d.ProjectId!)).ToList();
        foreach (var (dep, n) in deps.Select((d, n) => (d, n)))
        {
            progress?.Report(new InstallStep("install.deps", p.Title, n + 1, deps.Count));
            var dp = await Project(dep.ProjectId!, ct);
            MrVersion? dv = null;
            if (dep.VersionId is not null) { try { dv = Modrinth.FromVersion(await Http.GetJson($"{Api}/version/{dep.VersionId}", ct) ?? throw new InvalidOperationException()); } catch { } }
            installed.AddRange(await Install(i, dp, dv, progress, ct, seen));
        }
        var file = v.File ?? throw new InvalidOperationException(I18n.T("mr.noFile"));
        var dir = Directory.CreateDirectory(i.Folder(kind)).FullName;
        var temp = await Http.Download(file.Url, file.Name, new Progress<double>(r => progress?.Report(new InstallStep("install.download", p.Title, Ratio: r))), ct: ct);
        var sha1 = McContent.Sha1(temp);
        if (file.Sha1 is { Length: > 0 } want && !want.Equals(sha1, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(temp); } catch { }
            throw new InvalidOperationException(I18n.T("mine.err.hash", ("file", file.Name)));
        }
        // Старая версия того же проекта — убираем, чтобы в папке не было двух копий.
        if (McContent.Find(i, kind, p.Id) is { } old && old.Key != file.Name)
            try { File.Delete(Path.Combine(dir, old.File)); } catch { }
        File.Move(temp, Path.Combine(dir, file.Name), true);
        McContent.Remember(i, kind, file.Name, p, v, sha1);
        if (kind == "datapack") CopyToWorlds(i, Path.Combine(dir, file.Name));
        progress?.Report(new InstallStep("install.done", p.Title));
        installed.Add(p.Title);
        return installed;
    }

    /// <summary>Датапак работает в мире: кладём копию в datapacks каждого мира сборки.</summary>
    static void CopyToWorlds(McInstance i, string file)
    {
        var saves = Path.Combine(i.Dir, "saves");
        if (!Directory.Exists(saves)) return;
        foreach (var world in Directory.EnumerateDirectories(saves))
            try { File.Copy(file, Path.Combine(Directory.CreateDirectory(Path.Combine(world, "datapacks")).FullName, Path.GetFileName(file)), true); } catch { }
    }

    public static async Task Update(McInstance i, McUpdate u, IProgress<InstallStep>? progress, CancellationToken ct)
    {
        if (Program.Demo) { McContent.ForgetUpdate(i, u.Item); return; }
        var p = await Project(u.Latest.ProjectId, ct);
        await Install(i, p, u.Latest, progress, ct);
        McContent.ForgetUpdate(i, u.Item);
    }

    // ---------------------------------------------------------------- модпаки

    /// <summary>Модпак с Modrinth: скачать .mrpack нужной версии и сделать из него сборку.</summary>
    public static async Task<McInstance> InstallPack(MrProject p, MrVersion? version, IProgress<InstallStep>? progress, CancellationToken ct)
    {
        if (Program.Demo) return McDemo.PackInstance(p);
        var v = version ?? (await Versions(p.Id, "modpack", null, ct)).FirstOrDefault() ?? throw new InvalidOperationException(I18n.T("mr.noFile"));
        var file = v.File ?? throw new InvalidOperationException(I18n.T("mr.noFile"));
        var mrpack = await Http.Download(file.Url, file.Name, new Progress<double>(r => progress?.Report(new InstallStep("install.download", p.Title, Ratio: r))), ct: ct);
        try
        {
            var i = await ImportPack(mrpack, p.Title, progress, ct);
            i.PackProject = p.Id;
            i.PackVersion = v.Id;
            i.Icon = p.Icon;
            Mc.Save(i);
            return i;
        }
        finally { try { File.Delete(mrpack); } catch { } }
    }

    public sealed record PackIndex(string Name, string GameVersion, string Loader, string LoaderVersion, List<PackFile> Files);
    public sealed record PackFile(string Path, string? Sha1, string[] Urls, long Size, bool Client);

    /// <summary>modrinth.index.json из .mrpack.</summary>
    public static PackIndex ReadIndex(string mrpack)
    {
        using var zip = ZipFile.OpenRead(mrpack);
        var entry = zip.GetEntry("modrinth.index.json") ?? throw new InvalidDataException(I18n.T("mine.err.notPack"));
        using var r = new StreamReader(entry.Open());
        var j = JsonNode.Parse(r.ReadToEnd()) ?? throw new InvalidDataException(I18n.T("mine.err.notPack"));
        var deps = j["dependencies"] as JsonObject ?? [];
        var (loader, lv) = deps.Str("fabric-loader") is { } f ? ("fabric", f)
            : deps.Str("quilt-loader") is { } q ? ("quilt", q)
            : deps.Str("neoforge") is { } n ? ("neoforge", n)
            : deps.Str("forge") is { } fo ? ("forge", fo)
            : ("vanilla", "");
        var files = (j["files"] as JsonArray ?? []).OfType<JsonNode>().Select(x => new PackFile(
            x.Str("path") ?? "", x["hashes"].Str("sha1"), Modrinth.Strs(x["downloads"]).ToArray(), x.Long("fileSize"),
            x["env"].Str("client") != "unsupported")).Where(x => x.Path != "").ToList();
        return new PackIndex(j.Str("name") ?? Path.GetFileNameWithoutExtension(mrpack), deps.Str("minecraft") ?? "", loader, lv, files);
    }

    /// <summary>Сборка из файла .mrpack: папка, файлы с проверкой SHA-1, overrides, загрузчик.</summary>
    public static async Task<McInstance> ImportPack(string mrpack, string? name, IProgress<InstallStep>? progress, CancellationToken ct)
    {
        var index = ReadIndex(mrpack);
        var i = Mc.Create(name ?? index.Name, index.GameVersion, index.Loader, index.LoaderVersion);
        i.Source = "mrpack";
        Mc.Save(i);
        var root = Path.GetFullPath(i.Dir);
        var files = index.Files.Where(f => f.Client && f.Urls.Length > 0).ToList();
        var done = 0;
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(files.Select(async f =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var target = Path.GetFullPath(Path.Combine(root, f.Path));
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temp = await Http.Download(f.Urls[0], Path.GetFileName(target), ct: ct);
                if (f.Sha1 is { Length: > 0 } want && !want.Equals(McContent.Sha1(temp), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(temp); } catch { }
                    throw new InvalidOperationException(I18n.T("mine.err.hash", ("file", Path.GetFileName(target))));
                }
                File.Move(temp, target, true);
                var n = Interlocked.Increment(ref done);
                progress?.Report(new InstallStep("install.download", Path.GetFileName(target), n, files.Count, (double)n / files.Count));
            }
            finally { gate.Release(); }
        }));
        progress?.Report(new InstallStep("install.extract", index.Name));
        using (var zip = ZipFile.OpenRead(mrpack))
        {
            foreach (var prefix in new[] { "overrides/", "client-overrides/" })
            {
                foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
                {
                    var target = Path.GetFullPath(Path.Combine(root, e.FullName[prefix.Length..]));
                    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, true);
                }
            }
        }
        await McInstall.EnsureVersion(i, progress, ct);
        McProfiles.Sync(i);
        _ = McContent.Identify(i);
        return i;
    }
}
