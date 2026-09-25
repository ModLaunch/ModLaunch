using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

public sealed record Conflict(string A, string B, int Files);

/// <summary>
/// Конфликты файлов, как в Vortex: два включённых мода, в которых лежат
/// одинаковые .dll (или одинаковые файлы в Content у Stardew). Загрузится
/// только один из них — обычно это две копии одного мода.
/// </summary>
public static class Conflicts
{
    public static List<Conflict> Find(ModRegistry registry)
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in registry.List())
        {
            if (!mod.Bool("enabled", true) || mod.Bool("missing") || mod.Str("kind") == "preset") continue;
            var folder = registry.FolderFor(mod);
            if (!Directory.Exists(folder)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Take(500).ToList(); } catch { continue; }
            var name = mod.Str("name") ?? mod.Str("id") ?? "";
            foreach (var f in files.Select(Path.GetFileName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!owners.TryGetValue(f, out var list)) owners[f] = list = [];
                if (!list.Contains(name)) list.Add(name);
            }
        }
        return owners.Values.Where(l => l.Count > 1)
            .SelectMany(l => l.SelectMany((a, i) => l.Skip(i + 1).Select(b => (a, b))))
            .GroupBy(p => p)
            .Select(g => new Conflict(g.Key.a, g.Key.b, g.Count()))
            .OrderByDescending(c => c.Files).ToList();
    }

    /// <summary>Размер папки мода — для сортировки «по размеру».</summary>
    public static long FolderSize(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        }
        catch { return 0; }
    }
}
