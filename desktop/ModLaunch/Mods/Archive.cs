using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ModLaunch.Mods;

public sealed record ModRoot(string Prefix, string? Marker, string Name);

/// <summary>
/// Архивы модов: zip, 7z и rar — всё через SharpCompress, без внешних программ.
/// Главная задача — найти в архиве «корень» мода (папку с manifest.json или
/// с .dll), какой бы вложенности он ни был.
/// </summary>
public static partial class Archive
{
    [GeneratedRegex(@"(^|/)(__MACOSX/|\.DS_Store$|Thumbs\.db$|desktop\.ini$)", RegexOptions.IgnoreCase)]
    private static partial Regex Junk();

    static string Norm(string key) => key.Replace('\\', '/').TrimStart('/');

    static IArchive Open(string path)
    {
        try { return ArchiveFactory.Open(path); }
        catch (Exception e) { throw new InvalidDataException(Core.I18n.T("err.archiveFormat", ("file", Path.GetFileName(path))), e); }
    }

    public static List<string> Files(string path)
    {
        using var archive = Open(path);
        return archive.Entries.Where(e => !e.IsDirectory && e.Key is not null)
            .Select(e => Norm(e.Key!)).Where(n => !Junk().IsMatch(n)).ToList();
    }

    /// <summary>Корни модов. marker — "manifest" (SMAPI) или "dll" (BepInEx, HK).</summary>
    public static List<ModRoot> FindRoots(string path, string marker, int maxDepth)
    {
        var entries = Files(path);
        var roots = new List<ModRoot>();
        foreach (var entry in entries)
        {
            var parts = entry.Split('/');
            var file = parts[^1];
            if (parts.Length - 1 > maxDepth) continue;
            var isMarker = marker switch
            {
                "manifest" => file.Equals("manifest.json", StringComparison.OrdinalIgnoreCase),
                "dll" => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                _ => false, // «any»: мод своей игры кладём целиком
            };
            if (!isMarker) continue;
            var prefix = string.Join('/', parts[..^1]);
            if (roots.Any(r => r.Prefix == prefix)) continue;
            roots.Add(new ModRoot(prefix, entry, prefix == "" ? Path.GetFileNameWithoutExtension(path) : parts[^2]));
        }

        var filtered = roots.Where(r => !roots.Any(o => o != r && r.Prefix.StartsWith(o.Prefix + "/", StringComparison.Ordinal))).ToList();
        if (filtered.Count > 0) return filtered;

        var top = entries.Select(e => e.Split('/')[0]).Distinct().ToList();
        var wrapper = top.Count == 1 && entries.All(e => e.Contains('/')) ? top[0] : "";
        return [new ModRoot(wrapper, null, wrapper != "" ? wrapper : Path.GetFileNameWithoutExtension(path))];
    }

    /// <summary>Распаковать всё под prefix (или весь архив) в dest. Пути наружу dest отбрасываются.</summary>
    public static List<string> Extract(string path, string dest, string prefix = "", bool ignoreCase = false)
    {
        var p = prefix == "" ? "" : Norm(prefix).TrimEnd('/') + "/";
        var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.GetFullPath(dest);
        Directory.CreateDirectory(root);
        var written = new List<string>();
        using var archive = Open(path);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || entry.Key is null) continue;
            var name = Norm(entry.Key);
            if (Junk().IsMatch(name) || !name.StartsWith(p, cmp)) continue;
            var relative = name[p.Length..];
            if (relative == "") continue;
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.WriteToFile(target, new ExtractionOptions { Overwrite = true });
            written.Add(relative);
        }
        return written;
    }

    public static bool HasFolder(string path, string folder)
    {
        var p = Norm(folder).TrimEnd('/') + "/";
        return Files(path).Any(f => f.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ReadText(string path, string entryName)
    {
        using var archive = Open(path);
        var entry = archive.Entries.FirstOrDefault(e => e.Key is not null && Norm(e.Key).Equals(Norm(entryName), StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        using var reader = new StreamReader(entry.OpenEntryStream());
        return reader.ReadToEnd().TrimStart('﻿');
    }

    public static bool IsArchive(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".7z" or ".rar";
}
