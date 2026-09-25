using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Platform;
using ModLaunch.Core;

namespace ModLaunch.Setup;

public sealed record Release(string Version, string Name, string Notes, string? Page, string? SetupUrl, string? SetupName, string? Sha256);

/// <summary>
/// Обновления программы: свежий выпуск с GitHub Releases, скачивание установщика
/// заранее и запуск его с --update (он закроет нас, заменит exe и откроет снова).
/// </summary>
public static partial class Updater
{
    static readonly (string Owner, string Repo) Repo = LoadRepo();
    public static Release? Latest { get; private set; }
    public static string? Downloaded { get; private set; }
    public static event Action? Changed;

    static (string, string) LoadRepo()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ModLaunch/Assets/update.config.json"));
            var json = JsonNode.Parse(stream);
            return (json.Str("owner") ?? "", json.Str("repo") ?? "");
        }
        catch { return ("", ""); }
    }

    public static bool Configured => Repo.Owner != "" && Repo.Repo != "";
    public static string RepoName => $"{Repo.Owner}/{Repo.Repo}";
    public static bool Available => Latest is not null && Features.Versions.Compare(Latest.Version, Http.Version) > 0;
    public static bool CanInstall => Available && Installer.IsInstalledCopy && Latest?.SetupUrl is not null && OperatingSystem.IsWindows();
    public static bool AutoDownload => Settings.Data.Bool("autoDownloadUpdates", true);

    [GeneratedRegex(@"^ModLaunch-Setup-[\d.]+\.exe$", RegexOptions.IgnoreCase)] private static partial Regex SetupName();

    public static async Task<Release?> Check()
    {
        if (!Configured) return null;
        var data = await Http.GetJson($"https://api.github.com/repos/{RepoName}/releases/latest", default, 15, "application/vnd.github+json");
        var asset = data.Arr("assets").FirstOrDefault(a => SetupName().IsMatch(a.Str("name") ?? ""));
        var sha = Regex.Match(asset.Str("digest") ?? "", "^sha256:([0-9a-f]{64})$", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
        var version = (data.Str("tag_name") ?? "").TrimStart('v', 'V');
        var notes = data.Str("body") ?? "";
        Latest = new Release(version, data.Str("name") ?? $"ModLaunch {version}", notes.Length > 6000 ? notes[..6000] : notes, data.Str("html_url"),
            asset.Str("browser_download_url"), asset.Str("name"), sha);
        Changed?.Invoke();
        if (CanInstall && AutoDownload) _ = Fetch(null).ContinueWith(_ => { });
        return Latest;
    }

    static Task<string>? _busy;

    public static Task<string> Fetch(IProgress<double>? progress)
    {
        if (Downloaded is not null && File.Exists(Downloaded)) return Task.FromResult(Downloaded);
        return _busy ??= Task.Run(async () =>
        {
            try
            {
                var release = Latest ?? throw new InvalidOperationException("no update");
                var file = await Http.Download(release.SetupUrl!, release.SetupName!, progress, release.Sha256);
                var target = Path.Combine(Path.GetTempPath(), release.SetupName!);
                File.Move(file, target, true);
                Downloaded = target;
                Changed?.Invoke();
                return target;
            }
            finally { _busy = null; }
        });
    }

    /// <summary>Запустить установщик в режиме обновления; программа закроется сама.</summary>
    public static void Launch()
    {
        if (Downloaded is null || !File.Exists(Downloaded)) throw new InvalidOperationException("not downloaded");
        Process.Start(new ProcessStartInfo(Downloaded, "--update") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    /// <summary>Скачивания по выпускам — для экрана статистики.</summary>
    public static async Task<List<(string Version, DateTime? Published, long Setup, long Zip, long Total, string? Page)>> Releases()
    {
        var list = await Http.GetJson($"https://api.github.com/repos/{RepoName}/releases?per_page=100", default, 15, "application/vnd.github+json") as JsonArray ?? [];
        return list.Where(r => !r.Bool("draft")).Select(r =>
        {
            var assets = r.Arr("assets");
            long Count(string pattern) => assets.Where(a => Regex.IsMatch(a.Str("name") ?? "", pattern, RegexOptions.IgnoreCase)).Sum(a => a.Long("download_count"));
            return ((r.Str("tag_name") ?? "").TrimStart('v', 'V'), Firebase(r.Str("published_at")), Count(@"setup.*\.exe$"), Count(@"\.zip$"), assets.Sum(a => a.Long("download_count")), r.Str("html_url"));
        }).OrderBy(r => r.Item2).ToList();

        static DateTime? Firebase(string? s) => DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
    }
}
