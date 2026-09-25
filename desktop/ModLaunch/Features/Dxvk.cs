using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

public sealed record ExeInfo(string Exe, string Dir, string Name, string Arch, string? Api, bool Supported);
public sealed record DxvkState(bool Installed, string Version = "", string? Arch = null, string? Api = null);

/// <summary>
/// DXVK для любой игры: читаем exe (разрядность и какой DirectX он грузит),
/// берём свежий выпуск с GitHub и кладём dll рядом, сохранив старые файлы.
/// Метка dxvk.modlaunch.json в папке игры — общая с 3.x.
/// </summary>
public static class Dxvk
{
    const string Releases = "https://api.github.com/repos/doitsujin/dxvk/releases/latest";
    const string Marker = "dxvk.modlaunch.json";
    const string BackupSuffix = ".modlaunch-backup";

    public static readonly Dictionary<string, string[]> Files = new()
    {
        ["dx8"] = ["d3d8.dll", "d3d9.dll"],
        ["dx9"] = ["d3d9.dll"],
        ["dx10"] = ["d3d10core.dll", "d3d11.dll", "dxgi.dll"],
        ["dx11"] = ["d3d10core.dll", "d3d11.dll", "dxgi.dll"],
    };

    // ---------------------------------------------------------------- PE

    public static (string Arch, List<string> Imports)? PeInfo(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            using var br = new BinaryReader(fs);
            if (fs.Length < 64 || br.ReadUInt16() != 0x5A4D) return null;
            fs.Position = 0x3C;
            var pe = br.ReadUInt32();
            fs.Position = pe;
            if (br.ReadUInt32() != 0x00004550) return null;
            var machine = br.ReadUInt16();
            var sections = br.ReadUInt16();
            fs.Position = pe + 20;
            var optSize = br.ReadUInt16();
            fs.Position = pe + 24;
            var opt = br.ReadBytes(optSize);
            var magic = BitConverter.ToUInt16(opt, 0);
            var arch = machine == 0x8664 || magic == 0x20b ? "x64" : machine == 0x14c ? "x86" : null;
            if (arch is null) return null;
            var dirs = magic == 0x20b ? 112 : 96;
            var importRva = opt.Length >= dirs + 16 ? BitConverter.ToUInt32(opt, dirs + 8) : 0;
            fs.Position = pe + 24 + optSize;
            var table = br.ReadBytes(sections * 40);
            long? ToOffset(uint rva)
            {
                for (var i = 0; i < sections; i++)
                {
                    var s = i * 40;
                    var va = BitConverter.ToUInt32(table, s + 12);
                    var size = Math.Max(BitConverter.ToUInt32(table, s + 8), BitConverter.ToUInt32(table, s + 16));
                    if (rva >= va && rva < va + size) return rva - va + BitConverter.ToUInt32(table, s + 20);
                }
                return null;
            }
            var imports = new List<string>();
            if (importRva != 0 && ToOffset(importRva) is long at)
            {
                for (var i = 0; i < 512; i++)
                {
                    fs.Position = at + i * 20;
                    var desc = br.ReadBytes(20);
                    if (desc.Length < 20 || desc.All(b => b == 0)) break;
                    if (ToOffset(BitConverter.ToUInt32(desc, 12)) is not long nameAt) continue;
                    fs.Position = nameAt;
                    var raw = Encoding.Latin1.GetString(br.ReadBytes(64));
                    var name = raw.Split('\0')[0].ToLowerInvariant();
                    if (name != "") imports.Add(name);
                }
            }
            return (arch, imports);
        }
        catch { return null; }
    }

    public static string? ApiFromImports(IList<string> imports)
    {
        bool Has(string n) => imports.Contains(n);
        if (Has("d3d12.dll")) return "dx12";
        if (Has("d3d11.dll") || Has("dxgi.dll")) return "dx11";
        if (Has("d3d10.dll") || Has("d3d10_1.dll") || Has("d3d10core.dll")) return "dx10";
        if (Has("d3d9.dll")) return "dx9";
        if (Has("d3d8.dll")) return "dx8";
        if (Has("opengl32.dll")) return "opengl";
        if (Has("vulkan-1.dll")) return "vulkan";
        return null;
    }

    public static ExeInfo Inspect(string exe)
    {
        var info = PeInfo(exe) ?? throw new InvalidDataException(I18n.T("err.DXVK_NOT_EXE"));
        var dir = Path.GetDirectoryName(exe)!;
        var api = ApiFromImports(info.Imports);
        if (api is null or "opengl")
        {
            // Движок часто в отдельной dll рядом (UnityPlayer.dll, engine.dll).
            IEnumerable<string> dlls = [];
            try { dlls = Directory.EnumerateFiles(dir, "*.dll").Take(80).ToList(); } catch { }
            foreach (var dll in dlls)
            {
                var n = Path.GetFileName(dll).ToLowerInvariant();
                if (Files["dx11"].Contains(n) || Regex.IsMatch(n, @"^d3d[89]\.dll$")) continue;
                var found = ApiFromImports(PeInfo(dll)?.Imports ?? []);
                if (found is not null and not "opengl" and not "vulkan") { api = found; break; }
                if (found is not null && api is null) api = found;
            }
        }
        return new ExeInfo(exe, dir, GameName(exe), info.Arch, api, api is not null && Files.ContainsKey(api));
    }

    static string GameName(string exe)
    {
        var dir = Path.GetDirectoryName(exe)!;
        while (Regex.IsMatch(Path.GetFileName(dir), @"^(bin(32|64)?|x64|x86|win(32|64)|binaries|release|game)$", RegexOptions.IgnoreCase))
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return Path.GetFileName(dir) is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(exe);
    }

    // ---------------------------------------------------------------- tar.gz

    public static Dictionary<string, byte[]> ReadTarGz(string file)
    {
        using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        var tar = ms.ToArray();
        var result = new Dictionary<string, byte[]>();
        var offset = 0;
        string? longName = null;
        while (offset + 512 <= tar.Length)
        {
            var header = tar.AsSpan(offset, 512);
            if (header.IndexOfAnyExcept((byte)0) < 0) break;
            string Field(int start, int length) => Encoding.UTF8.GetString(tar, offset + start, length).Split('\0')[0];
            var sizeText = Field(124, 12).Trim();
            var size = sizeText == "" ? 0 : Convert.ToInt64(sizeText, 8);
            var type = Field(156, 1);
            var prefix = Field(345, 155);
            var name = longName ?? (prefix != "" ? $"{prefix}/{Field(0, 100)}" : Field(0, 100));
            longName = null;
            var body = tar.AsSpan(offset + 512, (int)size).ToArray();
            if (type == "L") longName = Encoding.UTF8.GetString(body).TrimEnd('\0');
            else if (type == "x")
            {
                var m = Regex.Match(Encoding.UTF8.GetString(body), @"\d+ path=([^\n]+)\n");
                if (m.Success) longName = m.Groups[1].Value;
            }
            else if (type is "0" or "") result[name.StartsWith("./") ? name[2..] : name] = body;
            offset += 512 + (int)((size + 511) / 512 * 512);
        }
        return result;
    }

    // ---------------------------------------------------------------- установка

    public static DxvkState Status(string dir)
    {
        try
        {
            var data = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, Marker)));
            return new DxvkState(true, data.Str("version") ?? "", data.Str("arch"), data.Str("api"));
        }
        catch { return new DxvkState(false); }
    }

    static async Task<(string Version, string File)> Archive(IProgress<InstallStep> progress, CancellationToken ct)
    {
        var data = await Http.GetJson(Releases, ct, 20, "application/vnd.github+json");
        var asset = data.Arr("assets").FirstOrDefault(a => Regex.IsMatch(a.Str("name") ?? "", @"^dxvk-[\d.]+\.tar\.gz$", RegexOptions.IgnoreCase))
            ?? throw new InvalidOperationException(I18n.T("err.DXVK_DOWNLOAD"));
        var version = (data.Str("tag_name") ?? "").TrimStart('v', 'V');
        var name = asset.Str("name")!;
        var sha = Regex.Match(asset.Str("digest") ?? "", "^sha256:([0-9a-f]{64})$", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
        var cached = Path.Combine(Directory.CreateDirectory(Path.Combine(Paths.CacheDir, "dxvk")).FullName, name);
        if (File.Exists(cached)) return (version, cached);
        var file = await Http.Download(asset.Str("browser_download_url")!, name, new Progress<double>(r => progress.Report(new InstallStep("loader.download", $"DXVK {version}", Ratio: r))), sha, ct);
        File.Move(file, cached, true);
        return (version, cached);
    }

    public static async Task<(string Version, string Api)> Install(string exe, string? chosenApi, IProgress<InstallStep> progress, CancellationToken ct)
    {
        var game = Inspect(exe);
        var api = chosenApi is not null && Files.ContainsKey(chosenApi) ? chosenApi : game.Api;
        if (api is null || !Files.ContainsKey(api)) throw new InvalidOperationException(I18n.T("err.DXVK_API"));
        if (Status(game.Dir).Installed) Remove(game.Dir);
        var names = Files[api];
        if (names.Any(n => ReShade.IsReShadeDll(Path.Combine(game.Dir, n)))) throw new InvalidOperationException(I18n.T("err.DXVK_RESHADE"));

        var (version, archive) = await Archive(progress, ct);
        progress.Report(new InstallStep("loader.install", "DXVK"));
        var entries = ReadTarGz(archive);
        var folder = game.Arch == "x64" ? "x64" : "x32";
        byte[]? Pick(string n) => entries.FirstOrDefault(kv => kv.Key.EndsWith($"/{folder}/{n}", StringComparison.OrdinalIgnoreCase)).Value;
        var files = names.Where(n => Pick(n) is not null).ToList();
        if (files.Count == 0) throw new InvalidOperationException(I18n.T("err.DXVK_DOWNLOAD"));

        var backups = new List<string>();
        var written = new List<string>();
        try
        {
            foreach (var n in files)
            {
                var target = Path.Combine(game.Dir, n);
                if (File.Exists(target)) { File.Move(target, target + BackupSuffix, true); backups.Add(n); }
                await File.WriteAllBytesAsync(target, Pick(n)!, ct);
                written.Add(n);
            }
            var marker = new JsonObject
            {
                ["version"] = version,
                ["arch"] = game.Arch,
                ["api"] = api,
                ["files"] = new JsonArray(written.Select(x => (JsonNode)x).ToArray()),
                ["backups"] = new JsonArray(backups.Select(x => (JsonNode)x).ToArray()),
                ["exe"] = Path.GetFileName(exe),
                ["installedAt"] = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(Path.Combine(game.Dir, Marker), marker.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            foreach (var n in written) try { File.Delete(Path.Combine(game.Dir, n)); } catch { }
            foreach (var n in backups)
            {
                var target = Path.Combine(game.Dir, n);
                if (File.Exists(target + BackupSuffix)) File.Move(target + BackupSuffix, target, true);
            }
            throw;
        }
        progress.Report(new InstallStep("loader.done", "DXVK"));
        return (version, api);
    }

    public static bool Remove(string dir)
    {
        JsonNode? data;
        try { data = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, Marker))); } catch { return false; }
        var known = Files["dx8"].Concat(Files["dx11"]).ToHashSet();
        foreach (var n in data.Arr("files").Select(x => x?.ToString() ?? ""))
            if (known.Contains(n.ToLowerInvariant())) try { File.Delete(Path.Combine(dir, n)); } catch { }
        foreach (var n in data.Arr("backups").Select(x => Path.GetFileName(x?.ToString() ?? "")))
        {
            var target = Path.Combine(dir, n);
            if (File.Exists(target + BackupSuffix)) File.Move(target + BackupSuffix, target, true);
        }
        File.Delete(Path.Combine(dir, Marker));
        return true;
    }

    // ---------------------------------------------------------------- список игр в настройках (dxvkGames)

    public sealed record Entry(string Exe, string Name, bool Exists, DxvkState State);

    public static List<Entry> List() =>
        Settings.Data.Arr("dxvkGames")
            .Where(g => g.Str("exe") is not null)
            .Select(g =>
            {
                var exe = g.Str("exe")!;
                var exists = File.Exists(exe);
                return new Entry(exe, g.Str("name") ?? Path.GetFileName(exe), exists, exists ? Status(Path.GetDirectoryName(exe)!) : new DxvkState(false));
            }).ToList();

    public static void Remember(string exe, string name)
    {
        var list = Settings.Data.Arr("dxvkGames").Where(g => g.Str("exe") != exe).Select(g => g!.DeepClone()).Take(49).ToList();
        list.Insert(0, new JsonObject { ["exe"] = exe, ["name"] = name });
        Settings.Data["dxvkGames"] = new JsonArray(list.ToArray());
        Settings.Save();
    }

    public static void Forget(string exe)
    {
        Settings.Data["dxvkGames"] = new JsonArray(Settings.Data.Arr("dxvkGames").Where(g => g.Str("exe") != exe).Select(g => g!.DeepClone()).ToArray());
        Settings.Save();
    }
}
