using System.Diagnostics;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Minecraft;

/// <summary>
/// Загрузчики в .minecraft\versions — так их видит официальный лаунчер (и TLauncher, и другие).
/// Fabric и Quilt — готовый профиль из их меты; Forge и NeoForge — их же установщик
/// в тихом режиме (--installClient) на Java из лаунчера Minecraft или скачанной.
/// </summary>
public static class McInstall
{
    /// <summary>Поставить загрузчик сборки (если ещё нет). Возвращает номер версии для лаунчера.</summary>
    public static async Task<string> EnsureVersion(McInstance i, IProgress<InstallStep>? progress = null, CancellationToken ct = default)
    {
        if (!i.Modded) return i.GameVersion;
        if (i.LoaderVersion == "")
        {
            progress?.Report(new InstallStep("loader.lookup", i.LoaderTitle));
            i.LoaderVersion = await McMeta.PickLoader(i.Loader, i.GameVersion, ct: ct)
                ?? throw new InvalidOperationException(I18n.T("mine.err.noLoader", ("loader", i.LoaderTitle), ("version", i.GameVersion)));
            Mc.Save(i);
        }
        if (Mc.VersionReady(i)) return i.VersionId;
        var label = $"{i.LoaderTitle} {i.LoaderVersion}";
        switch (i.Loader)
        {
            case "fabric":
            case "quilt":
                await InstallProfile(i, i.Loader == "fabric" ? McMeta.FabricProfile(i.GameVersion, i.LoaderVersion) : McMeta.QuiltProfile(i.GameVersion, i.LoaderVersion), label, progress, ct);
                break;
            case "forge":
            case "neoforge":
                await RunInstaller(i, label, progress, ct);
                break;
        }
        if (!Mc.VersionReady(i)) throw new InvalidOperationException(I18n.T("mine.err.loaderFailed", ("loader", label)));
        progress?.Report(new InstallStep("loader.done", label));
        return i.VersionId;
    }

    /// <summary>Профиль версии Fabric/Quilt: versions\&lt;id&gt;\&lt;id&gt;.json и пустой .jar (так делает их установщик).</summary>
    static async Task InstallProfile(McInstance i, string url, string label, IProgress<InstallStep>? progress, CancellationToken ct)
    {
        progress?.Report(new InstallStep("loader.download", label));
        var json = await Http.GetJson(url, ct) as JsonObject ?? throw new InvalidOperationException(label);
        var id = json.Str("id") ?? i.VersionId;
        var dir = Directory.CreateDirectory(Path.Combine(Mc.Root, "versions", id)).FullName;
        progress?.Report(new InstallStep("loader.install", label));
        await File.WriteAllTextAsync(Path.Combine(dir, id + ".json"), json.ToJsonString(), ct);
        var jar = Path.Combine(dir, id + ".jar");
        if (!File.Exists(jar)) await File.WriteAllBytesAsync(jar, [], ct);
        EnsureProfilesFile();
    }

    /// <summary>Установщик Forge/NeoForge без окон: java -jar installer.jar --installClient &lt;.minecraft&gt;.</summary>
    static async Task RunInstaller(McInstance i, string label, IProgress<InstallStep>? progress, CancellationToken ct)
    {
        var url = i.Loader == "forge" ? McMeta.ForgeInstaller(i.GameVersion, i.LoaderVersion) : McMeta.NeoInstaller(i.LoaderVersion);
        var installer = await Http.Download(url, Path.GetFileName(url), new Progress<double>(r => progress?.Report(new InstallStep("loader.download", label, Ratio: r))), ct: ct);
        var java = await Java(progress, ct, Math.Max(17, JavaFor(i.GameVersion)));
        EnsureProfilesFile();
        progress?.Report(new InstallStep("loader.install", label));
        var psi = new ProcessStartInfo(java)
        {
            WorkingDirectory = Path.GetDirectoryName(installer)!,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "-jar", installer, "--installClient", Mc.Root }) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException(java);
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
        }
        var log = (await output) + (await errors);
        try { await File.WriteAllTextAsync(Path.Combine(Paths.DataDir, $"{i.Loader}-install.log"), log, ct); } catch { }
        try { File.Delete(installer); } catch { }
        if (!Mc.VersionReady(i))
        {
            var reason = log.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l != "" && !l.StartsWith("at ")) ?? "";
            throw new InvalidOperationException(I18n.T("mine.err.installer", ("loader", label), ("reason", reason)));
        }
    }

    /// <summary>Лаунчер требует launcher_profiles.json: без него установщик Forge отказывается работать.</summary>
    public static void EnsureProfilesFile()
    {
        Directory.CreateDirectory(Mc.Root);
        var file = Path.Combine(Mc.Root, "launcher_profiles.json");
        if (!File.Exists(file)) File.WriteAllText(file, new JsonObject { ["profiles"] = new JsonObject(), ["settings"] = new JsonObject(), ["version"] = 3 }.ToJsonString());
    }

    // ---------------------------------------------------------------- Java

    /// <summary>Какая Java нужна версии игры: 26.x — Java 25, 1.20.5–1.21.x — 21, 1.17–1.20.4 — 17, старше — 8 (установщики идут и на новее).</summary>
    public static int JavaFor(string gameVersion)
    {
        var parts = gameVersion.Split('.', '-', ' ');
        if (!int.TryParse(parts[0], out var major)) return 21;
        if (major >= 26) return 25;
        if (major != 1 || parts.Length < 2 || !int.TryParse(parts[1], out var minor)) return 21;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        if (minor > 20 || (minor == 20 && patch >= 5)) return 21;
        return minor >= 17 ? 17 : 8;
    }

    /// <summary>
    /// Java для установщиков Forge и NeoForge: из лаунчера Minecraft (он держит свою), из JAVA_HOME
    /// и PATH — если она не старше нужной; иначе скачиваем Eclipse Temurin (JRE) в папку программы.
    /// </summary>
    public static async Task<string> Java(IProgress<InstallStep>? progress, CancellationToken ct, int min = 21)
    {
        if (FindJava(min) is { } found) return found;
        var want = min <= 21 ? 21 : 25;
        progress?.Report(new InstallStep("loader.download", $"Java {want}"));
        var url = $"https://api.adoptium.net/v3/binary/latest/{want}/ga/windows/x64/jre/hotspot/normal/eclipse";
        var zip = await Http.Download(url, $"temurin-{want}-jre.zip", new Progress<double>(r => progress?.Report(new InstallStep("loader.download", $"Java {want}", Ratio: r))), ct: ct);
        var target = Path.Combine(Paths.DataDir, "java", $"temurin-{want}");
        if (Directory.Exists(target)) Directory.Delete(target, true);
        progress?.Report(new InstallStep("loader.unpack", $"Java {want}"));
        Archive.Extract(zip, target);
        try { File.Delete(zip); } catch { }
        return FindJava(min) ?? throw new InvalidOperationException(I18n.T("mine.err.java"));
    }

    /// <summary>Номер Java по файлу release рядом с bin (JAVA_VERSION="21.0.7" → 21, "1.8.0_402" → 8).</summary>
    public static int JavaMajor(string javaExe)
    {
        try
        {
            var home = Path.GetDirectoryName(Path.GetDirectoryName(javaExe))!;
            var release = Path.Combine(home, "release");
            if (!File.Exists(release)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(release), @"JAVA_VERSION=""?(\d+)(?:\.(\d+))?");
            if (!m.Success) return 0;
            var major = int.Parse(m.Groups[1].Value);
            return major == 1 && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : major;
        }
        catch { return 0; }
    }

    public static string? FindJava(int min = 21)
    {
        var exe = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var found = new List<string>();
        var roots = new List<string>
        {
            Path.Combine(Paths.DataDir, "java"),
            Path.Combine(Mc.Root, "runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", Mc.StorePackage, "LocalCache", "Local", "runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "runtime"),
        };
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                found.AddRange(Directory.EnumerateFiles(root, exe, SearchOption.AllDirectories).Where(p => Path.GetFileName(Path.GetDirectoryName(p)) == "bin"));
            }
            catch { }
        }
        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home && File.Exists(Path.Combine(home, "bin", exe))) found.Add(Path.Combine(home, "bin", exe));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { if (dir != "" && File.Exists(Path.Combine(dir, exe))) found.Add(Path.Combine(dir, exe)); } catch { }
        }
        // Самая старая из подходящих: установщикам старых версий спокойнее на той Java, под которую их писали.
        return found.Select(p => (Path: p, Major: JavaMajor(p))).Where(x => x.Major >= min).OrderBy(x => x.Major).Select(x => x.Path).FirstOrDefault();
    }
}

/// <summary>
/// Установки в официальном лаунчере (launcher_profiles.json и launcher_profiles_microsoft_store.json):
/// у каждой сборки своя установка «ModLaunch · …» со своей папкой игры, версией и памятью.
/// Чужие установки и настройки лаунчера не трогаем.
/// </summary>
public static class McProfiles
{
    public static string Key(McInstance i) => "modlaunch-" + i.Id;

    static IEnumerable<string> Files()
    {
        var list = new List<string>();
        foreach (var name in new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" })
        {
            var f = Path.Combine(Mc.Root, name);
            if (File.Exists(f)) list.Add(f);
        }
        if (list.Count == 0) { McInstall.EnsureProfilesFile(); list.Add(Path.Combine(Mc.Root, "launcher_profiles.json")); }
        return list;
    }

    /// <summary>Аргументы Java как у лаунчера по умолчанию, но со своей памятью и добавками человека.</summary>
    public static string JavaArgs(McInstance i) =>
        ($"-Xmx{i.MemoryMb}M -XX:+UnlockExperimentalVMOptions -XX:+UseG1GC -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M " + i.JavaArgs).Trim();

    /// <summary>Записать (или обновить) установку сборки. played — поставить «последний запуск» на сейчас, чтобы лаунчер выбрал её.</summary>
    public static void Sync(McInstance i, bool played = false)
    {
        foreach (var file in Files()) Write(file, i, played);
    }

    public static void Write(string file, McInstance i, bool played)
    {
        JsonObject root;
        try { root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new JsonObject(); }
        catch { root = new JsonObject(); }
        var profiles = root.Obj("profiles");
        var key = Key(i);
        var profile = profiles[key] as JsonObject ?? new JsonObject { ["created"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") };
        profile["name"] = "ModLaunch · " + i.Name;
        profile["type"] = "custom";
        profile["lastVersionId"] = i.VersionId;
        profile["gameDir"] = i.Dir;
        profile["javaArgs"] = JavaArgs(i);
        profile["icon"] = Icon;
        if (played || profile["lastUsed"] is null) profile["lastUsed"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        profiles[key] = profile;
        var tmp = file + ".modlaunch.tmp";
        File.WriteAllText(tmp, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, file, true);
    }

    public static void Remove(McInstance i)
    {
        foreach (var file in new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }.Select(n => Path.Combine(Mc.Root, n)).Where(File.Exists))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject root) continue;
                if (root["profiles"] is JsonObject p && p.Remove(Key(i))) File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    /// <summary>Значок установки в лаунчере — логотип ModLaunch (лаунчер понимает data:image/png;base64).</summary>
    static string Icon
    {
        get
        {
            if (_icon is not null) return _icon;
            try
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/icon-128.png"));
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                _icon = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
            catch { _icon = "Crafting_Table"; }
            return _icon;
        }
    }
    static string? _icon;
}
