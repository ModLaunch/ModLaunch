using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

public sealed record ArchivedFile(string Path, string GameId, string RecordId, string Version, DateTime At, long Size);

/// <summary>
/// Архив загрузок, как в Vortex: скачанные архивы модов хранятся в папке
/// downloads/&lt;игра&gt;, с описью (index.json) — какой мод и какая версия.
/// Из архива мод ставится без интернета, в том числе старой версии.
/// </summary>
public static class DownloadArchive
{
    public static string Root => Directory.CreateDirectory(Path.Combine(Paths.DataDir, "downloads")).FullName;
    static readonly JsonFile Index = new(Path.Combine(Paths.DataDir, "downloads", "index.json"), () => new JsonObject { ["files"] = new JsonArray() });

    public static bool Keep => Settings.Data.Bool("keepArchives", true);
    public static double LimitGb => Settings.Data["archiveLimitGb"] is JsonValue v && v.TryGetValue<double>(out var d) && d > 0 ? d : 10;

    static JsonArray Files => Index.Data["files"] as JsonArray ?? (JsonArray)(Index.Data["files"] = new JsonArray());

    /// <summary>Положить копию скачанного архива (если архив включён). Старое сверх предела удаляется.</summary>
    public static void Add(string gameId, string recordId, string version, string file)
    {
        if (!Keep || !File.Exists(file)) return;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(Root, gameId)).FullName;
            var name = $"{Safe(recordId)}-{Safe(version == "" ? "latest" : version)}{Path.GetExtension(file)}";
            var target = Path.Combine(dir, name);
            File.Copy(file, target, true);
            lock (Files)
            {
                foreach (var old in Files.Where(f => f.Str("path") == target).ToList()) Files.Remove(old);
                Files.Add(new JsonObject { ["path"] = target, ["game"] = gameId, ["id"] = recordId, ["version"] = version, ["at"] = DateTime.UtcNow.ToString("o"), ["size"] = new FileInfo(target).Length });
                Prune();
                Index.Save();
            }
        }
        catch { /* архив — не главное */ }
    }

    static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' || c == '#' ? '_' : c));

    static void Prune()
    {
        var limit = (long)(LimitGb * 1024 * 1024 * 1024);
        var all = Files.Where(f => File.Exists(f.Str("path"))).OrderByDescending(f => f.Str("at")).ToList();
        long total = 0;
        foreach (var f in all)
        {
            total += f.Long("size");
            if (total <= limit) continue;
            try { File.Delete(f.Str("path")!); } catch { }
            Files.Remove(f);
        }
        foreach (var gone in Files.Where(f => !File.Exists(f.Str("path"))).ToList()) Files.Remove(gone);
    }

    public static List<ArchivedFile> For(string gameId, string recordId) =>
        Files.Where(f => f.Str("game") == gameId && f.Str("id") == recordId && File.Exists(f.Str("path")))
            .Select(f => new ArchivedFile(f.Str("path")!, gameId, recordId, f.Str("version") ?? "", DateTime.Parse(f.Str("at") ?? "2000-01-01").ToUniversalTime(), f.Long("size")))
            .OrderByDescending(f => f.At).ToList();

    public static (int Count, long Bytes) Size()
    {
        var existing = Files.Where(f => File.Exists(f.Str("path"))).ToList();
        return (existing.Count, existing.Sum(f => f.Long("size")));
    }

    public static void Clear()
    {
        lock (Files)
        {
            foreach (var f in Files.ToList()) try { File.Delete(f.Str("path")!); } catch { }
            Files.Clear();
            Index.Save();
        }
    }
}
