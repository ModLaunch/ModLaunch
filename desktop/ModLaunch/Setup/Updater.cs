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
        var json = Resources.Json("update.config.json");
        return (json.Str("owner") ?? "", json.Str("repo") ?? "");
    }

    /// <summary>8.4: свой сервер обновлений (сайт ModLaunch) — файл update.json. GitHub — запасной путь.</summary>
    static readonly string? Manifest = Ads.HttpsUrl(Resources.Json("update.config.json").Str("manifest"));

    /// <summary>Откуда пришла последняя проверка: адрес сайта или GitHub.</summary>
    public static string Source { get; private set; } = "";
    public static string? LastError { get; private set; }

    public static bool Configured => Manifest is not null || (Repo.Owner != "" && Repo.Repo != "");
    public static string RepoName => $"{Repo.Owner}/{Repo.Repo}";
    public static bool Available => Latest is not null && Features.Versions.Compare(Latest.Version, Http.Version) > 0;
    public static bool CanInstall => Available && Installer.IsInstalledCopy && Latest?.SetupUrl is not null && OperatingSystem.IsWindows();
    public static bool AutoDownload => Settings.Data.Bool("autoDownloadUpdates", true);

    [GeneratedRegex(@"^ModLaunch-Setup-[\d.]+\.exe$", RegexOptions.IgnoreCase)] private static partial Regex SetupName();

    public static async Task<Release?> Check()
    {
        if (!Configured) return null;
        if (Manifest is not null)
        {
            try
            {
                if (FromManifest(await Http.GetJson(Manifest, default, 12)) is { } own)
                {
                    Latest = own;
                    Source = new Uri(Manifest).Host;
                    LastError = null;
                    Changed?.Invoke();
                    if (CanInstall && AutoDownload) _ = Fetch(null).ContinueWith(_ => { });
                    return Latest;
                }
            }
            catch (Exception e) { LastError = e.Message; if (Repo.Owner == "") throw; }
        }
        if (Repo.Owner == "" || Repo.Repo == "") return null;
        Source = "GitHub";
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

    /// <summary>
    /// update.json на своём сайте:
    /// { "version": "8.4.0", "name": "ModLaunch 8.4.0", "notes": "…", "page": "https://…",
    ///   "url": "https://…/ModLaunch-Setup-8.4.0.exe", "sha256": "…" }
    /// Принимаются только https и файл с именем ModLaunch-Setup-x.y.z.exe.
    /// </summary>
    public static Release? FromManifest(System.Text.Json.Nodes.JsonNode? data)
    {
        var version = (data.Str("version") ?? "").TrimStart('v', 'V');
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) return null;
        var url = Ads.HttpsUrl(data.Str("url"));
        var name = url is null ? null : Path.GetFileName(new Uri(url).AbsolutePath);
        if (name is not null && !SetupName().IsMatch(name)) { url = null; name = null; }
        var sha = Regex.IsMatch(data.Str("sha256") ?? "", "^[0-9a-fA-F]{64}$") ? data.Str("sha256")!.ToLowerInvariant() : null;
        var notes = data.Str("notes") ?? "";
        return new Release(version, data.Str("name") ?? $"ModLaunch {version}", notes.Length > 6000 ? notes[..6000] : notes, Ads.HttpsUrl(data.Str("page")), url, name, sha);
    }

    [SelfTest]
    static string ReadsManifest()
    {
        var ok = FromManifest(System.Text.Json.Nodes.JsonNode.Parse("""{ "version": "9.1.2", "url": "https://modlaunchapp.com/download/ModLaunch-Setup-9.1.2.exe", "sha256": "AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12" }"""));
        if (ok?.SetupName != "ModLaunch-Setup-9.1.2.exe" || ok.Sha256 is null) throw new Exception("good manifest rejected");
        var bad = FromManifest(System.Text.Json.Nodes.JsonNode.Parse("""{ "version": "9.1.2", "url": "http://evil/x.exe" }"""));
        if (bad is null || bad.SetupUrl is not null) throw new Exception("http or odd name accepted");
        if (FromManifest(System.Text.Json.Nodes.JsonNode.Parse("""{ "version": "latest" }""")) is not null) throw new Exception("bad version accepted");
        return "https + ModLaunch-Setup name + sha256 checked";
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
