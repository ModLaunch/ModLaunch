using ModLaunch.Core;

namespace ModLaunch.Mods;

/// <summary>
/// Ждёт, пока браузер докачает архив в «Загрузки» (Nexus без Premium).
/// Файл считается готовым, когда он архив, появился после начала ожидания
/// и его размер перестал меняться.
/// </summary>
public static class DownloadWatch
{
    public static async Task<string> WaitForArchive(DateTime since, CancellationToken ct, string? folder = null)
    {
        folder ??= Paths.UserDownloads;
        var sizes = new Dictionary<string, long>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (!Archive.IsArchive(file)) continue;
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < since.ToUniversalTime().AddSeconds(-2) && info.CreationTimeUtc < since.ToUniversalTime().AddSeconds(-2)) continue;
                    if (sizes.TryGetValue(file, out var last) && last == info.Length && info.Length > 0 && CanOpen(file)) return file;
                    sizes[file] = info.Length;
                }
            }
            catch (IOException) { }
            await Task.Delay(1000, ct);
        }
    }

    static bool CanOpen(string file)
    {
        try { using var _ = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch { return false; }
    }
}
