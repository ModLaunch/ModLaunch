using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Mods;

/// <summary>
/// Ждёт, пока браузер докачает архив в «Загрузки» (Nexus без Premium).
/// Файлы Nexus называются «Имя-номерМода-версия-время.zip», поэтому архив
/// узнаётся по «-номер-» в имени. Готов — когда размер перестал меняться.
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

    public static Func<string, bool> NexusMatcher(string modId) => name => name.Contains($"-{modId}-", StringComparison.Ordinal);

    public static async Task<string> WaitForArchive(Func<string, bool> match, CancellationToken ct, string? folder = null, TimeSpan? timeout = null)
    {
        folder ??= Paths.UserDownloads;
        var before = List(folder);
        var sizes = new Dictionary<string, long>();
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
                if (info.Size > 0 && sizes.TryGetValue(name, out var last) && last == info.Size) return Path.Combine(folder, name);
                sizes[name] = info.Size;
            }
        }
    }
}
