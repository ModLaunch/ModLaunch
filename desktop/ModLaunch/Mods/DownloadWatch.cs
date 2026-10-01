using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Mods;

/// <summary>
/// Ждёт, пока браузер докачает архив в «Загрузки» (Nexus без Premium).
/// Имя файла Nexus известно заранее — ловим именно его (браузер может
/// дописать « (1)»); без имени — строго «Имя-номерМода-версия-время.zip».
/// Готов — когда размер совпал с ожидаемым или перестал меняться.
/// </summary>
public static partial class DownloadWatch
{
    [GeneratedRegex(@"\.(zip|7z|rar)$", RegexOptions.IgnoreCase)] private static partial Regex ArchiveName();
    [GeneratedRegex(@"\.(crdownload|part|partial|download|tmp|opdownload)$", RegexOptions.IgnoreCase)] private static partial Regex Partial();

    static Dictionary<string, (long Size, DateTime Time)> List(string dir)
    {
        var result = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles())
                result[file.Name] = (file.Length, file.LastWriteTimeUtc);
        }
        catch { }
        return result;
    }

    [GeneratedRegex(@"\s?\(\d+\)$")] private static partial Regex CopySuffix();
    [GeneratedRegex(@"[^\p{L}\p{N}]+")] private static partial Regex NotAlnum();

    /// <summary>Имя без расширения, без « (1)» от браузера и без знаков, которые браузер мог заменить.</summary>
    static string Key(string name) =>
        NotAlnum().Replace(CopySuffix().Replace(Path.GetFileNameWithoutExtension(name).Trim(), ""), "").ToLowerInvariant();

    public static Func<string, bool> NexusMatcher(string modId, string? fileName = null)
    {
        if (fileName is { Length: > 0 })
        {
            var want = Key(fileName);
            if (want.Length > 0) return name => Key(name) == want;
        }
        var strict = new Regex($@"-{Regex.Escape(modId)}-[\w-]*\d(\s?\(\d+\))?\.(zip|7z|rar)$", RegexOptions.IgnoreCase);
        return name => strict.IsMatch(name);
    }

    public static async Task<string> WaitForArchive(Func<string, bool> match, CancellationToken ct, string? folder = null, TimeSpan? timeout = null, long expectedSize = 0)
    {
        folder ??= Paths.UserDownloads;
        var before = List(folder);
        var sizes = new Dictionary<string, (long Size, int Same)>();
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(20));
        while (true)
        {
            await Task.Delay(1000, ct);
            if (DateTime.UtcNow > until) throw new TimeoutException(I18n.T("err.dl.timeout"));
            var now = List(folder);
            foreach (var (name, info) in now)
            {
                if (!ArchiveName().IsMatch(name) || Partial().IsMatch(name) || !match(name)) continue;
                if (before.TryGetValue(name, out var old) && old == info) continue;
                if (now.ContainsKey(name + ".part")) continue;
                // Размер известен — ждём ровно его; иначе — пока не перестанет расти (не совпал с ожидаемым — ждём подольше).
                var same = sizes.TryGetValue(name, out var last) && last.Size == info.Size ? last.Same + 1 : 0;
                sizes[name] = (info.Size, same);
                if (info.Size <= 0) continue;
                if (expectedSize > 0 ? (info.Size == expectedSize && same >= 1) || same >= 3 : same >= 1) return Path.Combine(folder, name);
            }
        }
    }
}
