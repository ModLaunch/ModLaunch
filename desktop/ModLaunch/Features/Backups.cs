using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Features;

public sealed record Backup(string Name, DateTime At, string Reason, long Size);

/// <summary>Резервные копии сохранений: backups/&lt;игра&gt;/ГГГГММДД-ЧЧММСС-причина.zip, как в 3.x.</summary>
public static partial class Backups
{
    [GeneratedRegex(@"^(\d{8}-\d{6})-(manual|launch|restore)\.zip$")] private static partial Regex FileName();

    public static string Root => Path.Combine(Paths.DataDir, "backups");

    public static string FolderFor(string gameId) => Path.Combine(Root, gameId);

    public static int Keep => Settings.Data.Long("backupKeep") is > 0 and var k ? (int)k : 10;
    public static bool OnLaunch => Settings.Data.Bool("backupOnLaunch", true);

    public static (bool Exists, long Bytes, int Files) Describe(string? savesDir)
    {
        if (savesDir is null || !Directory.Exists(savesDir)) return (false, 0, 0);
        var files = new DirectoryInfo(savesDir).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        return (true, files.Sum(f => f.Length), files.Count);
    }

    public static List<Backup> List(string gameId)
    {
        var folder = FolderFor(gameId);
        if (!Directory.Exists(folder)) return [];
        return new DirectoryInfo(folder).EnumerateFiles()
            .Select(f => (f, m: FileName().Match(f.Name)))
            .Where(x => x.m.Success)
            .Select(x => new Backup(x.f.Name, DateTime.ParseExact(x.m.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), x.m.Groups[2].Value, x.f.Length))
            .OrderByDescending(b => b.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static Backup? Create(string gameId, string? savesDir, string reason = "manual", int? keep = null)
    {
        if (savesDir is null || !Directory.Exists(savesDir) || !Directory.EnumerateFileSystemEntries(savesDir).Any()) return null;
        var folder = FolderFor(gameId);
        Directory.CreateDirectory(folder);
        var at = DateTime.Now;
        var name = $"{at:yyyyMMdd-HHmmss}-{reason}.zip";
        for (var i = 1; File.Exists(Path.Combine(folder, name)) && i < 60; i++) name = $"{at.AddSeconds(i):yyyyMMdd-HHmmss}-{reason}.zip";
        var tmp = Path.Combine(folder, name + ".tmp");
        if (File.Exists(tmp)) File.Delete(tmp);
        ZipFile.CreateFromDirectory(savesDir, tmp, CompressionLevel.Optimal, includeBaseDirectory: false);
        File.Move(tmp, Path.Combine(folder, name));
        Prune(gameId, keep ?? Keep);
        return List(gameId).FirstOrDefault(b => b.Name == name);
    }

    static void Prune(string gameId, int keep)
    {
        foreach (var old in List(gameId).Skip(Math.Clamp(keep, 1, 200)))
            File.Delete(Path.Combine(FolderFor(gameId), old.Name));
    }

    static string FileOf(string gameId, string name)
    {
        var full = Path.Combine(FolderFor(gameId), name);
        if (!FileName().IsMatch(name) || !File.Exists(full)) throw new FileNotFoundException(I18n.T("err.BACKUP_MISSING"));
        return full;
    }

    public static string? Restore(string gameId, string name, string savesDir)
    {
        var file = FileOf(gameId, name);
        var root = Path.GetFullPath(savesDir);
        using (var zip = ZipFile.OpenRead(file))
        {
            foreach (var e in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(root, e.FullName));
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && target != root)
                    throw new InvalidDataException(I18n.T("err.BACKUP_BROKEN"));
            }
        }
        var safety = Create(gameId, savesDir, "restore", Keep + 1);
        Directory.CreateDirectory(savesDir);
        foreach (var entry in Directory.EnumerateFileSystemEntries(savesDir).ToList())
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry);
        }
        ZipFile.ExtractToDirectory(file, savesDir, overwriteFiles: true);
        return safety?.Name;
    }

    public static void Remove(string gameId, string name) => File.Delete(FileOf(gameId, name));
}
