using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Minecraft;

/// <summary>Мод, ресурспак, шейдер или датапак в сборке.</summary>
public sealed record McItem(
    string Kind, string File, string Name, bool Enabled, long Size, DateTime Modified,
    string? Version, string? Icon, string? ProjectId, string? VersionId, string? Author, string? Description)
{
    /// <summary>Имя файла без «.disabled».</summary>
    public string Key => File.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? File[..^9] : File;
    public string? Url => ProjectId is null ? null : $"https://modrinth.com/project/{ProjectId}";
}

/// <summary>Найденное обновление: что стоит и что поставить.</summary>
public sealed record McUpdate(McItem Item, MrVersion Latest);

/// <summary>
/// Содержимое сборки: файлы в mods, resourcepacks, shaderpacks, datapacks. Выключенный файл
/// получает «.disabled» (как в Prism и Modrinth App). Откуда файл (проект Modrinth, версия)
/// — в minecraft\&lt;сборка&gt;.json в данных программы, чтобы не трогать чужие папки.
/// </summary>
public static partial class McContent
{
    public static string MetaPath(McInstance i) => Path.Combine(Paths.DataDir, "minecraft", i.Id + ".json");

    static readonly Dictionary<string, JsonFile> Files = [];

    /// <summary>Записи о файлах меняются и из установок в фоне, и из интерфейса — по очереди.</summary>
    static readonly object Gate = new();

    static JsonFile Meta(McInstance i)
    {
        lock (Files)
        {
            if (!Files.TryGetValue(i.Id, out var f)) Files[i.Id] = f = new JsonFile(MetaPath(i), () => new JsonObject { ["files"] = new JsonObject() });
            return f;
        }
    }

    static JsonObject FilesOf(McInstance i) => Meta(i).Data.Obj("files");

    /// <summary>Ключ записи: «mods/sodium.jar».</summary>
    static string RecordKey(string kind, string key) => Mc.KindFolder(kind) + "/" + key;

    static bool IsContent(string kind, string file)
    {
        var name = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? file[..^9] : file;
        return kind == "mod" ? name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    public static List<McItem> List(McInstance i, string kind)
    {
        var dir = i.Folder(kind);
        if (!Directory.Exists(dir)) return [];
        lock (Gate) return ListLocked(i, kind, dir);
    }

    static List<McItem> ListLocked(McInstance i, string kind, string dir)
    {
        var meta = FilesOf(i);
        var list = new List<McItem>();
        var changed = false;
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFiles(dir).ToList(); } catch { return []; }
        foreach (var path in entries)
        {
            var file = Path.GetFileName(path);
            if (!IsContent(kind, file)) continue;
            var info = new FileInfo(path);
            var enabled = !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var key = enabled ? file : file[..^9];
            var record = meta[RecordKey(kind, key)] as JsonObject;
            // Имя и версию у модов, поставленных вручную, читаем из самого .jar (fabric.mod.json, mods.toml) — один раз.
            if (record is null || record.Long("size") != info.Length)
            {
                record ??= new JsonObject();
                record["size"] = info.Length;
                if (record.Str("project") is null && kind == "mod" && ReadJar(path) is { } jar)
                {
                    record["title"] = jar.Name;
                    record["version"] = jar.Version;
                    if (jar.Description is not null) record["description"] = jar.Description;
                    if (jar.Author is not null) record["author"] = jar.Author;
                }
                meta[RecordKey(kind, key)] = record;
                changed = true;
            }
            list.Add(new McItem(kind, file, record.Str("title") ?? Pretty(key), enabled, info.Length, info.LastWriteTimeUtc,
                record.Str("version"), record.Str("icon"), record.Str("project"), record.Str("versionId"), record.Str("author"), record.Str("description")));
        }
        if (changed) Save(i);
        return list.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static int Count(McInstance i, string kind)
    {
        try { return Directory.Exists(i.Folder(kind)) ? Directory.EnumerateFiles(i.Folder(kind)).Count(f => IsContent(kind, Path.GetFileName(f))) : 0; }
        catch { return 0; }
    }

    static void Save(McInstance i) { lock (Gate) { try { Meta(i).Save(); } catch { } } }

    [GeneratedRegex(@"[-_+](mc|fabric|forge|neoforge|quilt)?[\d.]+.*$", RegexOptions.IgnoreCase)] private static partial Regex VersionTail();

    /// <summary>«sodium-fabric-0.6.0+mc1.21.jar» → «sodium-fabric».</summary>
    static string Pretty(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var cut = VersionTail().Replace(name, "");
        return (cut.Length >= 3 ? cut : name).Replace('_', ' ');
    }

    public sealed record JarInfo(string Name, string? Version, string? Description, string? Author, string? Id);

    /// <summary>Описание мода внутри .jar: fabric.mod.json, quilt.mod.json или META-INF/(neoforge.)mods.toml.</summary>
    public static JarInfo? ReadJar(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry("fabric.mod.json") is { } fabric)
            {
                using var r = new StreamReader(fabric.Open());
                var j = JsonNode.Parse(r.ReadToEnd(), documentOptions: new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
                var authors = j?["authors"] as JsonArray;
                var author = authors?.FirstOrDefault() is JsonValue av ? av.ToString() : authors?.FirstOrDefault().Str("name");
                return new JarInfo(j.Str("name") ?? j.Str("id") ?? Path.GetFileNameWithoutExtension(path), j.Str("version"), j.Str("description"), author, j.Str("id"));
            }
            if (zip.GetEntry("quilt.mod.json") is { } quilt)
            {
                using var r = new StreamReader(quilt.Open());
                var q = JsonNode.Parse(r.ReadToEnd())?["quilt_loader"];
                return new JarInfo(q?["metadata"].Str("name") ?? q.Str("id") ?? Path.GetFileNameWithoutExtension(path), q.Str("version"), q?["metadata"].Str("description"), null, q.Str("id"));
            }
            var toml = zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml");
            if (toml is not null)
            {
                using var r = new StreamReader(toml.Open());
                var text = r.ReadToEnd();
                string? Field(string key) => Regex.Match(text, $@"(?ms)^\s*{key}\s*=\s*(?:'''(.*?)'''|""([^""\n]*)"")") is { Success: true } m ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
                var version = Field("version");
                if (version is null || version.Contains("${"))
                {
                    if (zip.GetEntry("META-INF/MANIFEST.MF") is { } mf)
                    {
                        using var mr = new StreamReader(mf.Open());
                        version = Regex.Match(mr.ReadToEnd(), @"(?m)^Implementation-Version:\s*(\S+)") is { Success: true } vm ? vm.Groups[1].Value : null;
                    }
                }
                return new JarInfo(Field("displayName") ?? Field("modId") ?? Path.GetFileNameWithoutExtension(path), version, Field("description")?.Trim(), Field("authors"), Field("modId"));
            }
        }
        catch { }
        return null;
    }

    // ---------------------------------------------------------------- действия

    public static void SetEnabled(McInstance i, McItem item, bool enabled)
    {
        if (item.Enabled == enabled) return;
        var dir = i.Folder(item.Kind);
        var from = Path.Combine(dir, item.File);
        var to = Path.Combine(dir, enabled ? item.Key : item.Key + ".disabled");
        if (File.Exists(from)) File.Move(from, to, true);
    }

    public static void Delete(McInstance i, McItem item)
    {
        var path = Path.Combine(i.Folder(item.Kind), item.File);
        if (File.Exists(path)) File.Delete(path);
        lock (Gate) FilesOf(i).Remove(RecordKey(item.Kind, item.Key));
        Save(i);
    }

    /// <summary>Запомнить, откуда файл: проект и версия Modrinth.</summary>
    public static void Remember(McInstance i, string kind, string file, MrProject? project, MrVersion version, string? sha1)
    {
        var record = new JsonObject
        {
            ["project"] = version.ProjectId, ["versionId"] = version.Id, ["version"] = version.Number,
            ["title"] = project?.Title, ["icon"] = project?.Icon, ["author"] = project?.Author == "" ? null : project?.Author,
            ["description"] = project?.Description, ["sha1"] = sha1,
            ["size"] = new FileInfo(Path.Combine(i.Folder(kind), file)).Length,
        };
        lock (Gate) FilesOf(i)[RecordKey(kind, file)] = record;
        Save(i);
    }

    /// <summary>Файл проекта в сборке (если стоит) — чтобы заменить при обновлении и показать «Установлено».</summary>
    public static McItem? Find(McInstance i, string kind, string projectId) =>
        List(i, kind).FirstOrDefault(x => x.ProjectId == projectId);

    public static HashSet<string> InstalledProjects(McInstance i)
    {
        lock (Gate) return FilesOf(i).Select(kv => kv.Value.Str("project")).OfType<string>().ToHashSet();
    }

    public static void CopyMeta(McInstance from, McInstance to)
    {
        try
        {
            if (!File.Exists(MetaPath(from))) return;
            Directory.CreateDirectory(Path.GetDirectoryName(MetaPath(to))!);
            File.Copy(MetaPath(from), MetaPath(to), true);
            lock (Files) Files.Remove(to.Id);
        }
        catch { }
    }

    public static string Sha1(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(f)).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- Modrinth: узнать и обновить

    /// <summary>Loaders для запроса обновлений по виду: у ресурспаков — minecraft, у шейдеров — iris/optifine…</summary>
    public static string[] LoadersFor(McInstance i, string kind) => kind switch
    {
        "resourcepack" => ["minecraft"],
        "shader" => ["iris", "optifine", "canvas", "vanilla"],
        "datapack" => ["datapack"],
        _ => i.Loader == "quilt" ? ["quilt", "fabric"] : [i.Loader],
    };

    /// <summary>Узнать по хешам файлы, поставленные вручную: название, значок и проект Modrinth.</summary>
    public static async Task Identify(McInstance i, CancellationToken ct = default)
    {
        if (Program.Demo) return;
        var meta = FilesOf(i);
        var unknown = new Dictionary<string, (string Kind, string Key)>();
        foreach (var kind in Mc.Kinds)
        {
            foreach (var item in List(i, kind).Where(x => x.ProjectId is null))
            {
                bool known;
                lock (Gate) known = (meta[RecordKey(kind, item.Key)] as JsonObject)?.Bool("checked") == true;
                if (known) continue;
                try { unknown[Sha1(Path.Combine(i.Folder(kind), item.File))] = (kind, item.Key); } catch { }
            }
        }
        if (unknown.Count == 0) return;
        var body = new JsonObject { ["hashes"] = new JsonArray(unknown.Keys.Select(h => (JsonNode)JsonValue.Create(h)!).ToArray()), ["algorithm"] = "sha1" };
        var found = await Http.PostJson("https://api.modrinth.com/v2/version_files", body, ct: ct) as JsonObject ?? [];
        var projects = found.Select(kv => kv.Value.Str("project_id")).OfType<string>().Distinct().ToList();
        var info = projects.Count == 0 ? [] : await McModrinth.Projects(projects, ct);
        lock (Gate)
        foreach (var (hash, (kind, key)) in unknown)
        {
            var rec = meta[RecordKey(kind, key)] as JsonObject ?? new JsonObject();
            rec["checked"] = true;
            if (found[hash] is JsonObject v)
            {
                var pid = v.Str("project_id");
                rec["project"] = pid;
                rec["versionId"] = v.Str("id");
                rec["version"] = v.Str("version_number");
                rec["sha1"] = hash;
                if (pid is not null && info.TryGetValue(pid, out var p))
                {
                    rec["title"] = p.Title;
                    rec["icon"] = p.Icon;
                    rec["description"] = p.Description;
                }
            }
            meta[RecordKey(kind, key)] = rec;
        }
        Save(i);
    }

    static readonly Dictionary<string, List<McUpdate>> Found = [];

    public static int UpdateCount(McInstance i) { lock (Found) return Found.TryGetValue(i.Id, out var l) ? l.Count : 0; }
    public static List<McUpdate> Updates(McInstance i) { lock (Found) return Found.TryGetValue(i.Id, out var l) ? [.. l] : []; }

    /// <summary>Сменили версию или загрузчик — найденные обновления больше не про эту сборку.</summary>
    public static void ForgetAllUpdates(McInstance i) { lock (Found) Found.Remove(i.Id); }

    /// <summary>Есть ли новее — одним запросом на вид содержимого (version_files/update по SHA-1).</summary>
    public static async Task<List<McUpdate>> CheckUpdates(McInstance i, CancellationToken ct = default)
    {
        var result = new List<McUpdate>();
        if (Program.Demo)
        {
            result = Demo.Updates(i);
            lock (Found) Found[i.Id] = result;
            return result;
        }
        foreach (var kind in Mc.Kinds)
        {
            var items = List(i, kind).Where(x => x.ProjectId is not null && x.Enabled).ToList();
            if (items.Count == 0) continue;
            var byHash = new Dictionary<string, McItem>();
            foreach (var item in items)
            {
                try { byHash[Sha1(Path.Combine(i.Folder(kind), item.File))] = item; } catch { }
            }
            var body = new JsonObject
            {
                ["hashes"] = new JsonArray(byHash.Keys.Select(h => (JsonNode)JsonValue.Create(h)!).ToArray()),
                ["algorithm"] = "sha1",
                ["loaders"] = new JsonArray(LoadersFor(i, kind).Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()),
                ["game_versions"] = new JsonArray(JsonValue.Create(i.GameVersion)),
            };
            JsonObject? answer;
            try { answer = await Http.PostJson("https://api.modrinth.com/v2/version_files/update", body, ct: ct) as JsonObject; }
            catch { continue; }
            foreach (var (hash, item) in byHash)
            {
                if (answer?[hash] is not JsonObject v) continue;
                var latest = McModrinth.FromVersion(v);
                if (latest.Id != item.VersionId && latest.File is not null) result.Add(new McUpdate(item, latest));
            }
        }
        lock (Found) Found[i.Id] = result;
        return result;
    }

    public static void ForgetUpdate(McInstance i, McItem item)
    {
        lock (Found) if (Found.TryGetValue(i.Id, out var l)) l.RemoveAll(u => u.Item.Key == item.Key && u.Item.Kind == item.Kind);
    }

    /// <summary>Для снимков экрана: подставные обновления.</summary>
    public static class Demo
    {
        public static List<McUpdate> Updates(McInstance i) =>
            List(i, "mod").Where(x => x.ProjectId is "AANobbMI" or "gvQqBUqZ").Select(x => new McUpdate(x,
                new MrVersion("new-" + x.ProjectId, x.ProjectId!, (x.Version ?? "1.0") + ".1", "", "release", [i.Loader], [i.GameVersion],
                    [new MrFile("https://example.invalid/" + x.Key, x.Key, true, x.Size, null)], [], DateTime.UtcNow.AddDays(-2), 1000, ""))).ToList();
    }
}
