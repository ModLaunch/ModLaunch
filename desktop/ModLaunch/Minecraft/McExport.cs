using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Minecraft;

/// <summary>
/// 9.4: «Экспорт в .mrpack» — сборка становится модпаком Modrinth, как в Modrinth App. Моды, ресурспаки
/// и шейдеры, которые есть на Modrinth, попадают в список со ссылками и контрольными суммами (файл
/// весит килобайты); остальное и настройки — в overrides. Такой файл открывают Modrinth App, Prism,
/// ATLauncher и сам ModLaunch.
/// </summary>
public static class McExport
{
    public sealed record Options(string Name, string Version, string Summary, bool Config = true, bool Packs = true, bool Worlds = false, bool GameOptions = true, bool Servers = true);

    public sealed record Result(int Linked, int Bundled, long Bytes);

    /// <summary>Файл на Modrinth: ссылка и размер.</summary>
    public sealed record Remote(string Url, long Size);

    /// <summary>Поиск файлов на Modrinth по sha1 (одним запросом на всю сборку).</summary>
    public static async Task<Dictionary<string, Remote>> LookupModrinth(IReadOnlyList<string> sha1s, CancellationToken ct)
    {
        var result = new Dictionary<string, Remote>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in sha1s.Chunk(500))
        {
            var body = new JsonObject { ["hashes"] = new JsonArray(chunk.Select(h => (JsonNode)h).ToArray()), ["algorithm"] = "sha1" };
            var json = await Http.PostJson("https://api.modrinth.com/v2/version_files", body, ct: ct, timeoutSec: 40) as JsonObject;
            foreach (var (hash, version) in json ?? [])
            {
                var file = (version?["files"] as JsonArray ?? []).OfType<JsonNode>()
                    .FirstOrDefault(f => string.Equals(f["hashes"].Str("sha1"), hash, StringComparison.OrdinalIgnoreCase));
                if (file.Str("url") is { Length: > 0 } url) result[hash] = new Remote(url, file.Long("size"));
            }
        }
        return result;
    }

    /// <summary>Какие папки и файлы сборки — содержимое (может быть ссылкой) и что идёт в overrides.</summary>
    static IEnumerable<(string Rel, bool Content)> Files(McInstance i, Options o)
    {
        var root = Path.GetFullPath(i.Dir);
        IEnumerable<string> All(string sub, string pattern = "*") =>
            Directory.Exists(Path.Combine(root, sub)) ? Directory.EnumerateFiles(Path.Combine(root, sub), pattern, SearchOption.AllDirectories) : [];
        foreach (var f in All("mods").Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
            yield return (Path.GetRelativePath(root, f), true);
        if (o.Packs)
            foreach (var sub in new[] { "resourcepacks", "shaderpacks" })
                foreach (var f in All(sub).Where(f => Path.GetDirectoryName(f) == Path.Combine(root, sub)))
                    yield return (Path.GetRelativePath(root, f), f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (o.Config)
            foreach (var sub in new[] { "config", "defaultconfigs", "kubejs", "shaderpacks" })
                foreach (var f in All(sub).Where(f => sub != "shaderpacks" || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                    yield return (Path.GetRelativePath(root, f), false);
        if (o.GameOptions && File.Exists(Path.Combine(root, "options.txt"))) yield return ("options.txt", false);
        if (o.Servers && File.Exists(Path.Combine(root, "servers.dat"))) yield return ("servers.dat", false);
        if (o.Worlds) foreach (var f in All("saves")) yield return (Path.GetRelativePath(root, f), false);
    }

    public static async Task<Result> Export(McInstance i, Options o, string target, IProgress<InstallStep>? progress, CancellationToken ct,
        Func<IReadOnlyList<string>, CancellationToken, Task<Dictionary<string, Remote>>>? lookup = null)
    {
        var root = Path.GetFullPath(i.Dir);
        var files = Files(i, o).DistinctBy(f => f.Rel, StringComparer.OrdinalIgnoreCase).ToList();
        progress?.Report(new InstallStep("install.deps", o.Name));
        var hashes = new Dictionary<string, (string Sha1, string Sha512, long Size)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, content) in files.Where(f => f.Content))
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(root, rel);
            await using var s = File.OpenRead(full);
            var sha1 = Convert.ToHexString(await SHA1.HashDataAsync(s, ct)).ToLowerInvariant();
            s.Position = 0;
            var sha512 = Convert.ToHexString(await SHA512.HashDataAsync(s, ct)).ToLowerInvariant();
            hashes[rel] = (sha1, sha512, s.Length);
        }
        Dictionary<string, Remote> remote;
        try { remote = await (lookup ?? LookupModrinth)(hashes.Values.Select(h => h.Sha1).Distinct().ToList(), ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { remote = []; }

        var index = new JsonObject
        {
            ["formatVersion"] = 1, ["game"] = "minecraft", ["versionId"] = o.Version, ["name"] = o.Name,
        };
        if (o.Summary.Trim() != "") index["summary"] = o.Summary.Trim();
        var list = new JsonArray();
        var bundled = new List<string>();
        foreach (var (rel, content) in files)
        {
            var path = rel.Replace('\\', '/');
            if (content && hashes.TryGetValue(rel, out var h) && remote.TryGetValue(h.Sha1, out var r))
            {
                var server = path.StartsWith("mods/", StringComparison.Ordinal) ? "required" : "unsupported";
                list.Add(new JsonObject
                {
                    ["path"] = path,
                    ["hashes"] = new JsonObject { ["sha1"] = h.Sha1, ["sha512"] = h.Sha512 },
                    ["env"] = new JsonObject { ["client"] = "required", ["server"] = server },
                    ["downloads"] = new JsonArray(r.Url),
                    ["fileSize"] = h.Size,
                });
            }
            else bundled.Add(rel);
        }
        index["files"] = list;
        var deps = new JsonObject { ["minecraft"] = i.GameVersion };
        var key = i.Loader switch { "fabric" => "fabric-loader", "quilt" => "quilt-loader", "forge" => "forge", "neoforge" => "neoforge", _ => null };
        if (key is not null && i.LoaderVersion != "") deps[key] = i.LoaderVersion;
        index["dependencies"] = deps;

        progress?.Report(new InstallStep("install.extract", o.Name));
        var temp = target + ".tmp";
        await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("modrinth.index.json", CompressionLevel.Optimal);
            await using (var w = new StreamWriter(entry.Open()))
                await w.WriteAsync(index.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var n = 0;
            foreach (var rel in bundled)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Файлы открытого мира и логи могут быть заняты игрой — читаем с FileShare.ReadWrite.
                    await using var input = new FileStream(Path.Combine(root, rel), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var e = zip.CreateEntry("overrides/" + rel.Replace('\\', '/'), CompressionLevel.Optimal);
                    await using var output = e.Open();
                    await input.CopyToAsync(output, ct);
                }
                catch (IOException) { }
                if (++n % 20 == 0) progress?.Report(new InstallStep("install.extract", o.Name, n, bundled.Count, (double)n / bundled.Count));
            }
        }
        File.Move(temp, target, true);
        return new Result(list.Count, bundled.Count, new FileInfo(target).Length);
    }

    [SelfTest]
    static string ExportsAPack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ml-export-" + Environment.ProcessId);
        var target = dir + ".mrpack";
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "mods"));
            Directory.CreateDirectory(Path.Combine(dir, "config"));
            File.WriteAllText(Path.Combine(dir, "mods", "sodium.jar"), "sodium bytes");
            File.WriteAllText(Path.Combine(dir, "mods", "private.jar"), "my own mod");
            File.WriteAllText(Path.Combine(dir, "config", "sodium.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "options.txt"), "fov:0.5");
            var sodiumSha1 = Convert.ToHexString(SHA1.HashData("sodium bytes"u8.ToArray())).ToLowerInvariant();
            var i = new McInstance { Id = "t", Dir = dir, GameVersion = "1.21.1", Loader = "fabric", LoaderVersion = "0.16.10" };
            var result = Task.Run(() => Export(i, new Options("Test", "1.0.0", "s"), target, null, default,
                (_, _) => Task.FromResult(new Dictionary<string, Remote> { [sodiumSha1] = new("https://cdn.modrinth.com/data/x/sodium.jar", 12) }))).GetAwaiter().GetResult();
            var back = McModrinth.ReadIndex(target);
            if (result.Linked != 1 || back.Files.Count != 1 || back.Files[0].Urls[0] != "https://cdn.modrinth.com/data/x/sodium.jar") throw new Exception("linked files wrong");
            if (back.Loader != "fabric" || back.LoaderVersion != "0.16.10" || back.GameVersion != "1.21.1") throw new Exception("dependencies wrong");
            using var zip = ZipFile.OpenRead(target);
            var names = zip.Entries.Select(e => e.FullName).ToHashSet();
            if (!names.Contains("overrides/mods/private.jar") || !names.Contains("overrides/config/sodium.json") || !names.Contains("overrides/options.txt") || names.Contains("overrides/mods/sodium.jar"))
                throw new Exception(string.Join(", ", names));
            return $"{result.Linked} linked from Modrinth, {result.Bundled} in overrides, index reads back";
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            try { File.Delete(target); } catch { }
        }
    }
}
