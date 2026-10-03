using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Features;

/// <summary>Скриншоты игры: из Steam (userdata/…/760/remote/&lt;appid&gt;/screenshots) и из папки Screenshots рядом с игрой. Новые первыми.</summary>
public static class ScreenshotGallery
{
    static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp"];

    public static List<string> Folders(GameDef game, string? gamePath)
    {
        var result = new List<string>();
        foreach (var root in Locator.SteamRoots())
        {
            try
            {
                var users = Path.Combine(root, "userdata");
                if (!Directory.Exists(users)) continue;
                foreach (var user in Directory.EnumerateDirectories(users))
                {
                    var dir = Path.Combine(user, "760", "remote", game.SteamAppId.ToString(), "screenshots");
                    if (Directory.Exists(dir)) result.Add(dir);
                }
            }
            catch { }
        }
        if (gamePath is not null)
            foreach (var name in new[] { "Screenshots", "screenshots" })
                if (Directory.Exists(Path.Combine(gamePath, name))) result.Add(Path.Combine(gamePath, name));
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> Find(IEnumerable<string> folders, int limit = 80)
    {
        var files = new List<FileInfo>();
        foreach (var dir in folders)
            try
            {
                files.AddRange(new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)));
            }
            catch { }
        return files.OrderByDescending(f => f.LastWriteTimeUtc).Take(limit).Select(f => f.FullName).ToList();
    }

    [SelfTest]
    static string FindsNewestImagesFirst()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "shots-test")).FullName;
        var old = Path.Combine(dir, "a.png");
        var fresh = Path.Combine(dir, "b.JPG");
        File.WriteAllText(old, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
        File.WriteAllText(fresh, "x");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
        var found = Find([dir]);
        Directory.Delete(dir, true);
        if (found.Count != 2 || !found[0].EndsWith("b.JPG")) throw new Exception("order/filter wrong: " + string.Join(",", found));
        return "2 images found, newest first, .txt ignored";
    }
}
