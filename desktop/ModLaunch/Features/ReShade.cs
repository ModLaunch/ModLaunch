using System.Text;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using SharpCompress.Archives;

namespace ModLaunch.Features;

public sealed record ReShadeState(bool Installed, string? Dll, bool HasIni, string? Preset);

/// <summary>
/// Шейдеры через ReShade: сама библиотека из официального установщика
/// (в exe дописан zip), пакеты эффектов по списку EffectPackages.ini,
/// пресеты из архивов модов. Логика и файлы — как в 3.x.
/// </summary>
public static partial class ReShade
{
    const string Site = "https://reshade.me/";
    const string PackagesUrl = "https://raw.githubusercontent.com/crosire/reshade-shaders/list/EffectPackages.ini";
    static readonly string[] ProxyDlls = ["dxgi.dll", "d3d11.dll", "d3d9.dll", "opengl32.dll", "d3d12.dll"];
    static string Cache => Directory.CreateDirectory(Path.Combine(Paths.CacheDir, "reshade")).FullName;

    public static string DllName(string api) => api switch { "dx9" => "d3d9.dll", "opengl" => "opengl32.dll", _ => "dxgi.dll" };

    /// <summary>ReShade ставится рядом с exe игры.</summary>
    public static string DirOf(GameDef game, string gamePath) => Path.GetDirectoryName(game.LaunchExe(gamePath)) ?? gamePath;

    static bool FileHas(string file, string text)
    {
        try
        {
            if (!File.Exists(file)) return false;
            using var fs = File.OpenRead(file);
            var buffer = new byte[Math.Min(fs.Length, 8 * 1024 * 1024)];
            fs.ReadExactly(buffer);
            return buffer.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;
        }
        catch { return false; }
    }

    public static bool IsReShadeDll(string file) => FileHas(file, "ReShade") || FileHas(file, "crosire");

    public static ReShadeState Detect(string dir)
    {
        var ini = Path.Combine(dir, "ReShade.ini");
        var dll = ProxyDlls.Select(n => Path.Combine(dir, n)).FirstOrDefault(f => FileHas(f, "ReShade"));
        string? preset = null;
        try { preset = Ini.Parse(File.ReadAllText(ini)).Get("GENERAL", "PresetPath"); } catch { }
        return new ReShadeState(dll is not null || File.Exists(ini), dll is null ? null : Path.GetFileName(dll), File.Exists(ini), preset);
    }

    [GeneratedRegex("href=\"([^\"]*ReShade_Setup_([\\d.]+)\\.exe)\"")] private static partial Regex SetupLink();

    static async Task<(string File, string Version)> Dll(IProgress<InstallStep> progress, CancellationToken ct)
    {
        var html = await Http.GetString(Site, ct, 20);
        var m = SetupLink().Match(html);
        if (!m.Success) throw new InvalidOperationException("ReShade: no download link on reshade.me");
        var url = new Uri(new Uri(Site), m.Groups[1].Value).ToString();
        var version = m.Groups[2].Value;
        var cached = Path.Combine(Cache, $"ReShade64-{version}.dll");
        if (File.Exists(cached) && new FileInfo(cached).Length > 100 * 1024) return (cached, version);

        var label = $"ReShade {version}";
        var setup = await Http.Download(url, $"ReShade_Setup_{version}.exe", new Progress<double>(r => progress.Report(new InstallStep("loader.download", label, Ratio: r))), ct: ct);
        try
        {
            var bytes = await File.ReadAllBytesAsync(setup, ct);
            using var zip = AppendedZip(bytes);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Split('/')[^1].Equals("ReShade64.dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("ReShade64.dll not found in setup");
            using var input = entry.Open();
            using var output = File.Create(cached);
            await input.CopyToAsync(output, ct);
        }
        finally { try { File.Delete(setup); } catch { } }
        return (cached, version);
    }

    /// <summary>Установщик ReShade — exe с дописанным в конец zip: ищем конец каталога zip и отрезаем начало.</summary>
    static System.IO.Compression.ZipArchive AppendedZip(byte[] buffer)
    {
        var eocd = buffer.AsSpan().LastIndexOf(new byte[] { 0x50, 0x4b, 0x05, 0x06 });
        if (eocd < 0) throw new InvalidDataException("no zip inside");
        var cdSize = BitConverter.ToUInt32(buffer, eocd + 12);
        var cdOffset = BitConverter.ToUInt32(buffer, eocd + 16);
        var start = eocd - (long)cdSize - cdOffset;
        if (start < 0) throw new InvalidDataException("broken zip offsets");
        return new System.IO.Compression.ZipArchive(new MemoryStream(buffer, (int)start, buffer.Length - (int)start), System.IO.Compression.ZipArchiveMode.Read);
    }

    public static async Task<ReShadeState> Install(string dir, string api, IProgress<InstallStep> progress, CancellationToken ct)
    {
        if (Detect(dir).Dll is null)
        {
            var (file, version) = await Dll(progress, ct);
            progress.Report(new InstallStep("loader.install", $"ReShade {version}"));
            File.Copy(file, Path.Combine(dir, DllName(api)), true);
        }
        WriteIni(dir, null);
        Directory.CreateDirectory(Path.Combine(dir, "reshade-shaders", "Shaders"));
        Directory.CreateDirectory(Path.Combine(dir, "reshade-shaders", "Textures"));
        await EnsureEffects(dir, [], progress, ct, required: true);
        progress.Report(new InstallStep("loader.done", "ReShade"));
        return Detect(dir);
    }

    public static void WriteIni(string dir, string? presetPath)
    {
        var file = Path.Combine(dir, "ReShade.ini");
        var text = File.Exists(file) ? File.ReadAllText(file) : "";
        var ini = Ini.Parse(text);
        var general = new Dictionary<string, string>
        {
            ["EffectSearchPaths"] = ini.Get("GENERAL", "EffectSearchPaths") ?? @".\reshade-shaders\Shaders\**",
            ["TextureSearchPaths"] = ini.Get("GENERAL", "TextureSearchPaths") ?? @".\reshade-shaders\Textures\**",
        };
        if (presetPath is not null) general["PresetPath"] = presetPath;
        else if (ini.Get("GENERAL", "PresetPath") is null) general["PresetPath"] = @".\ReShadePreset.ini";
        text = Ini.Patch(text, "GENERAL", general);
        if (ini.Get("OVERLAY", "TutorialProgress") is null) text = Ini.Patch(text, "OVERLAY", new Dictionary<string, string> { ["TutorialProgress"] = "4" });
        if (ini.Get("INPUT", "KeyOverlay") is null) text = Ini.Patch(text, "INPUT", new Dictionary<string, string> { ["KeyOverlay"] = "36,0,0,0" });
        File.WriteAllText(file, text);
    }

    /// <summary>Сделать пресет текущим (null — пустой пресет, эффекты выключены).</summary>
    public static void Activate(string dir, string? presetFile) =>
        WriteIni(dir, presetFile is null ? @".\ReShadePreset.ini" : @".\" + Path.GetRelativePath(dir, presetFile));

    // ---------------------------------------------------------------- эффекты

    sealed record Package(string Key, string Name, bool Required, string Url, string InstallPath, string TexturePath, string[] Effects, string[] Deny);

    static async Task<List<Package>> Packages(CancellationToken ct)
    {
        var file = Path.Combine(Cache, "EffectPackages.ini");
        string? text = null;
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(1)) text = File.ReadAllText(file);
        if (text is null)
        {
            try { text = await Http.GetString(PackagesUrl, ct, 20); File.WriteAllText(file, text); }
            catch { if (File.Exists(file)) text = File.ReadAllText(file); else throw; }
        }
        static string[] List(string? s) => (s ?? "").Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x != "").ToArray();
        return Ini.Parse(text)
            .Where(kv => kv.Key.All(char.IsDigit) && kv.Key != "")
            .Select(kv => new Package(kv.Key,
                kv.Value.GetValueOrDefault("PackageName") ?? kv.Key,
                kv.Value.GetValueOrDefault("Required") == "1",
                kv.Value.GetValueOrDefault("DownloadUrl") ?? "",
                kv.Value.GetValueOrDefault("InstallPath") is { Length: > 0 } ip ? ip : @".\reshade-shaders\Shaders",
                kv.Value.GetValueOrDefault("TextureInstallPath") is { Length: > 0 } tp ? tp : @".\reshade-shaders\Textures",
                List(kv.Value.GetValueOrDefault("EffectFiles")),
                List(kv.Value.GetValueOrDefault("DenyEffectFiles"))))
            .Where(p => p.Url != "")
            .ToList();
    }

    static HashSet<string> PresentEffects(string dir)
    {
        var root = Path.Combine(dir, "reshade-shaders");
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.fx", SearchOption.AllDirectories).Select(f => Path.GetFileName(f).ToLowerInvariant()).ToHashSet();
    }

    public static async Task<List<string>> EnsureEffects(string dir, IList<string> wanted, IProgress<InstallStep> progress, CancellationToken ct, bool required = false)
    {
        var list = await Packages(ct);
        var have = PresentEffects(dir);
        var need = wanted.Where(fx => !have.Contains(fx)).ToHashSet();
        var chosen = list.Where(p => (required && p.Required && !p.Effects.All(have.Contains)) || p.Effects.Any(need.Contains)).ToList();
        foreach (var pack in chosen)
        {
            progress.Report(new InstallStep("loader.download", pack.Name));
            var archive = await Http.Download(pack.Url, $"reshade-{pack.Key}.zip", ct: ct);
            try { ExtractPackage(dir, archive, pack); }
            finally { try { File.Delete(archive); } catch { } }
        }
        var after = PresentEffects(dir);
        return wanted.Where(fx => !after.Contains(fx)).ToList();
    }

    static string Under(string dir, string rel) => Path.Combine(dir, rel.StartsWith(@".\") ? rel[2..] : rel).Replace('\\', Path.DirectorySeparatorChar);

    static void ExtractPackage(string dir, string archivePath, Package pack)
    {
        var safeRoot = Path.GetFullPath(Path.Combine(dir, "reshade-shaders")) + Path.DirectorySeparatorChar;
        using var archive = ArchiveFactory.Open(archivePath);
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key is not null))
        {
            var name = entry.Key!.Replace('\\', '/');
            var sh = Regex.Match(name, @"(^|/)Shaders/(.+)$", RegexOptions.IgnoreCase);
            var tx = Regex.Match(name, @"(^|/)Textures/(.+)$", RegexOptions.IgnoreCase);
            string? output = null;
            if (sh.Success)
            {
                if (pack.Deny.Contains(Path.GetFileName(sh.Groups[2].Value).ToLowerInvariant())) continue;
                output = Path.Combine(Under(dir, pack.InstallPath), sh.Groups[2].Value);
            }
            else if (tx.Success) output = Path.Combine(Under(dir, pack.TexturePath), tx.Groups[2].Value);
            if (output is null) continue;
            output = Path.GetFullPath(output);
            if (!output.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.WriteToFile(output, new SharpCompress.Common.ExtractionOptions { Overwrite = true });
        }
    }

    // ---------------------------------------------------------------- пресеты

    static bool IsPresetText(string text) => Regex.IsMatch(text, @"^\s*(Techniques|TechniqueSorting)\s*=", RegexOptions.Multiline);

    /// <summary>Эффекты (.fx), которые нужны пресету.</summary>
    public static List<string> PresetEffects(string text)
    {
        var ini = Ini.Parse(text);
        var list = string.Join(',', new[] { ini.Get("", "Techniques"), ini.Get("", "TechniqueSorting"), ini.Get("GENERAL", "Techniques") }.OfType<string>());
        var files = new HashSet<string>();
        foreach (var item in list.Split(','))
        {
            var at = item.IndexOf('@');
            if (at > 0) files.Add(item[(at + 1)..].Trim().ToLowerInvariant());
        }
        foreach (var name in ini.Keys.Where(k => k.EndsWith(".fx", StringComparison.OrdinalIgnoreCase))) files.Add(name.ToLowerInvariant());
        return files.ToList();
    }

    /// <summary>Похож ли архив на пресет ReShade, а не на мод.</summary>
    public static bool LooksLikePreset(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entries = archive.Entries.Where(e => !e.IsDirectory && e.Key is not null).ToList();
            var names = entries.Select(e => e.Key!.Replace('\\', '/')).ToList();
            if (names.Any(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(n, @"(^|/)(dxgi|d3d9|d3d11|d3d12|opengl32|reshade\d*)\.dll$", RegexOptions.IgnoreCase))) return false;
            if (names.Any(n => Regex.IsMatch(n, @"(^|/)manifest\.json$", RegexOptions.IgnoreCase))) return false;
            if (names.Any(n => Regex.IsMatch(n, @"(^|/)reshade-shaders/", RegexOptions.IgnoreCase) || Regex.IsMatch(n, @"\.fxh?$", RegexOptions.IgnoreCase))) return true;
            foreach (var e in entries.Where(e => e.Key!.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && e.Size < 512 * 1024))
            {
                using var reader = new StreamReader(e.OpenEntryStream());
                if (IsPresetText(reader.ReadToEnd())) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Разложить пресет: .ini — к exe, шейдеры и текстуры — в reshade-shaders. Возвращает имена пресетов.</summary>
    public static List<string> ExtractPreset(string dir, string archivePath)
    {
        var presets = new List<string>();
        var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        using var archive = ArchiveFactory.Open(archivePath);
        void Put(string rel, IArchiveEntry entry)
        {
            var output = Path.GetFullPath(Path.Combine(dir, rel));
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.WriteToFile(output, new SharpCompress.Common.ExtractionOptions { Overwrite = true });
        }
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key is not null))
        {
            var name = entry.Key!.Replace('\\', '/');
            var file = name.Split('/')[^1];
            if (name.Contains("__MACOSX/")) continue;
            if (Regex.IsMatch(file, @"^(dxgi|d3d9|d3d11|d3d12|opengl32)\.dll$", RegexOptions.IgnoreCase) || Regex.IsMatch(file, @"^reshade(\d*)?\.(ini|dll|log)$", RegexOptions.IgnoreCase)) continue;
            var inShaders = Regex.Match(name, @"(^|/)reshade-shaders/(.+)$", RegexOptions.IgnoreCase);
            var textures = Regex.Match(name, @"(^|/)Textures/(.+)$", RegexOptions.IgnoreCase);
            if (inShaders.Success) Put(Path.Combine("reshade-shaders", inShaders.Groups[2].Value), entry);
            else if (Regex.IsMatch(file, @"\.fxh?$", RegexOptions.IgnoreCase)) Put(Path.Combine("reshade-shaders", "Shaders", file), entry);
            else if (textures.Success) Put(Path.Combine("reshade-shaders", "Textures", textures.Groups[2].Value), entry);
            else if (file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            {
                string text;
                using (var reader = new StreamReader(entry.OpenEntryStream())) text = reader.ReadToEnd();
                if (!IsPresetText(text)) continue;
                Put(file, entry);
                presets.Add(file);
            }
        }
        return presets;
    }
}
