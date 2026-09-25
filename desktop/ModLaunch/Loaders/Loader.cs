using System.Diagnostics;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch.Loaders;

public static class Loader
{
    public static bool IsInstalled(GameDef game, string gamePath) => game.Loader switch
    {
        LoaderKind.Smapi => File.Exists(Path.Combine(gamePath, "StardewModdingAPI.exe")) || File.Exists(Path.Combine(gamePath, "StardewModdingAPI.dll")),
        LoaderKind.HkApi => HkApi.IsInstalled(gamePath),
        LoaderKind.None => true,
        _ => Directory.Exists(Path.Combine(gamePath, "BepInEx", "core")) && (!OperatingSystem.IsWindows() || File.Exists(Path.Combine(gamePath, "winhttp.dll"))),
    };

    public static Task<string> Install(GameDef game, string gamePath, IProgress<InstallStep> progress, CancellationToken ct) => game.Loader switch
    {
        LoaderKind.Smapi => Smapi.Install(gamePath, progress, ct),
        LoaderKind.HkApi => HkApi.Install(gamePath, progress, ct),
        LoaderKind.None => Task.FromResult(""),
        _ => BepInEx.Install(game, gamePath, progress, ct),
    };
}

public static class BepInEx
{
    public static async Task<string> Install(GameDef game, string gamePath, IProgress<InstallStep> progress, CancellationToken ct)
    {
        progress.Report(new InstallStep("loader.lookup", "BepInEx"));
        var (version, url) = game.ThunderstoreCommunity is not null && game.ThunderstorePackage is not null
            ? await Thunderstore.Latest(game.ThunderstoreCommunity, game.ThunderstorePackage, ct)
            : await FromGitHub(Path.Combine(gamePath, game.Executables[0]), ct);
        var label = $"BepInEx {version}";
        var archive = await Http.Download(url, "BepInExPack.zip", new Progress<double>(r => progress.Report(new InstallStep("loader.download", label, Ratio: r))), ct: ct);
        try
        {
            progress.Report(new InstallStep("loader.install", "BepInEx"));
            Archive.Extract(archive, gamePath, PackPrefix(archive));
        }
        finally { try { File.Delete(archive); } catch { } }
        Directory.CreateDirectory(Path.Combine(gamePath, "BepInEx", "plugins"));
        progress.Report(new InstallStep("loader.done", "BepInEx"));
        return version;
    }

    /// <summary>
    /// Для своих игр без сообщества на Thunderstore — BepInEx 5 с GitHub,
    /// разрядность по заголовку exe (у старых Unity-игр бывает 32 бита).
    /// </summary>
    static async Task<(string Version, string Url)> FromGitHub(string exe, CancellationToken ct)
    {
        var release = await Http.GetJson("https://api.github.com/repos/BepInEx/BepInEx/releases/latest", ct, 20, "application/vnd.github+json");
        var arch = Is32Bit(exe) ? "win_x86" : "win_x64";
        var asset = release.Arr("assets").FirstOrDefault(a => (a.Str("name") ?? "").Contains(arch, StringComparison.OrdinalIgnoreCase) && (a.Str("name") ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("BepInEx: " + arch);
        return ((release.Str("tag_name") ?? "").TrimStart('v'), asset.Str("browser_download_url")!);
    }

    /// <summary>Машина в заголовке PE: 0x14c — 32 бита.</summary>
    public static bool Is32Bit(string exe)
    {
        try
        {
            using var f = File.OpenRead(exe);
            using var r = new BinaryReader(f);
            f.Position = 0x3C;
            var pe = r.ReadInt32();
            f.Position = pe + 4;
            return r.ReadUInt16() == 0x14c;
        }
        catch { return false; }
    }

    /// <summary>
    /// Пакеты BepInExPack для Thunderstore лежат внутри папки (BepInExPack/…,
    /// BepInExPack_Valheim/…): отрезаем всё до winhttp.dll или BepInEx/core/.
    /// </summary>
    static string PackPrefix(string archive)
    {
        foreach (var entry in Archive.Files(archive))
        {
            var lower = entry.ToLowerInvariant();
            var at = lower.EndsWith("winhttp.dll") ? lower.LastIndexOf("winhttp.dll", StringComparison.Ordinal) : lower.IndexOf("bepinex/core/", StringComparison.Ordinal);
            if (at < 0) continue;
            if (at > 0 && lower[at - 1] != '/') continue;
            return entry[..at];
        }
        return "";
    }
}

public static class Smapi
{
    public static async Task<string> Install(string gamePath, IProgress<InstallStep> progress, CancellationToken ct)
    {
        progress.Report(new InstallStep("loader.lookup", "SMAPI"));
        var release = await Http.GetJson("https://api.github.com/repos/Pathoschild/SMAPI/releases/latest", ct, 20, "application/vnd.github+json");
        var asset = release.Arr("assets").FirstOrDefault(a =>
            (a.Str("name") ?? "").EndsWith("installer.zip", StringComparison.OrdinalIgnoreCase) &&
            !(a.Str("name") ?? "").Contains("for-developers", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(I18n.T("err.smapi.noAsset"));
        var version = (release.Str("tag_name") ?? "").TrimStart('v');
        var label = $"SMAPI {version}";
        var archive = await Http.Download(asset.Str("browser_download_url")!, asset.Str("name")!, new Progress<double>(r => progress.Report(new InstallStep("loader.download", label, Ratio: r))), ct: ct);

        progress.Report(new InstallStep("loader.unpack", "SMAPI"));
        var work = Path.Combine(Paths.DataDir, "smapi-installer");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Archive.Extract(archive, work);
        try { File.Delete(archive); } catch { }

        var exe = Directory.EnumerateFiles(work, "SMAPI.Installer.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new InvalidOperationException(I18n.T("err.smapi.noExe"));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(I18n.T("err.smapi.notWindows", ("path", exe)));

        progress.Report(new InstallStep("loader.install", "SMAPI"));
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var a in new[] { "--install", "--game-path", gamePath, "--no-prompt" }) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeout.Token);
        }
        var log = (await output) + (await errors);
        File.WriteAllText(Path.Combine(Paths.DataDir, "smapi-install.log"), log);

        if (!File.Exists(Path.Combine(gamePath, "StardewModdingAPI.exe")) && !File.Exists(Path.Combine(gamePath, "StardewModdingAPI.dll")))
        {
            var reason = log.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l != "") ?? "";
            throw new InvalidOperationException(reason != "" ? I18n.T("err.smapi.failed", ("reason", reason)) : I18n.T("err.smapi.noFiles"));
        }
        progress.Report(new InstallStep("loader.done", "SMAPI"));
        return version;
    }
}

public static class HkApi
{
    public static string ManagedDir(string gamePath)
    {
        string[] candidates =
        [
            Path.Combine(gamePath, "hollow_knight_Data", "Managed"),
            Path.Combine(gamePath, "Hollow Knight_Data", "Managed"),
            Path.Combine(gamePath, "Contents", "Resources", "Data", "Managed"),
        ];
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    public static bool IsInstalled(string gamePath)
    {
        var managed = ManagedDir(gamePath);
        return File.Exists(Path.Combine(managed, "Assembly-CSharp.dll.vanilla")) && File.Exists(Path.Combine(managed, "MMHOOK_Assembly-CSharp.dll"));
    }

    public static async Task<string> Install(string gamePath, IProgress<InstallStep> progress, CancellationToken ct)
    {
        var managed = ManagedDir(gamePath);
        if (!Directory.Exists(managed)) throw new DirectoryNotFoundException(I18n.T("err.hk.noManaged", ("path", managed)));

        progress.Report(new InstallStep("loader.lookup", "Modding API"));
        var (version, url, sha) = await ModLinks.LoadApi(ct);
        if (url == "") throw new InvalidOperationException(I18n.T("err.hk.noApiBuild"));
        var label = $"Modding API {version}";
        var archive = await Http.Download(url, $"moddingapi-{version}.zip", new Progress<double>(r => progress.Report(new InstallStep("loader.download", label, Ratio: r))), sha, ct);

        var vanilla = Path.Combine(managed, "Assembly-CSharp.dll");
        var backup = vanilla + ".vanilla";
        if (File.Exists(vanilla) && !File.Exists(backup))
        {
            progress.Report(new InstallStep("loader.backup", "Modding API"));
            File.Copy(vanilla, backup);
        }

        progress.Report(new InstallStep("loader.install", "Modding API"));
        var work = Path.Combine(Path.GetTempPath(), "modhub-hkapi-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Archive.Extract(archive, work);
            foreach (var file in Directory.EnumerateFiles(work))
                File.Copy(file, Path.Combine(managed, Path.GetFileName(file)), true);
        }
        finally
        {
            try { File.Delete(archive); } catch { }
            try { Directory.Delete(work, true); } catch { }
        }
        Directory.CreateDirectory(Path.Combine(managed, "Mods"));
        progress.Report(new InstallStep("loader.done", "Modding API"));
        return version;
    }
}
