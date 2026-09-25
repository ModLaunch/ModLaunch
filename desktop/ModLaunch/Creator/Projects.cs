using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Creator;

public sealed record Project(string Id, string Dir, string Name, string Game, DateTime Updated)
{
    public string Script => Path.Combine(Dir, "main" + ModScript.Extension);
}

/// <summary>Результат сборки: архив мода и что в нём.</summary>
public sealed record Packed(string Zip, string Format, List<string> Files, List<string> Warnings);

/// <summary>
/// Проекты Creator Hub (%APPDATA%\ModHub\creator\&lt;проект&gt;): скрипт main.mls
/// и файлы мода рядом. Сборка — в пакет того вида, который понимает игра.
/// </summary>
public static partial class Projects
{
    public static string Root => Directory.CreateDirectory(Path.Combine(Paths.DataDir, "creator")).FullName;
    public static string OutDir => Directory.CreateDirectory(Path.Combine(Root, "_build")).FullName;

    [GeneratedRegex(@"^\s*mod\s+""([^""]*)""", RegexOptions.Multiline)]
    private static partial Regex ModLine();
    [GeneratedRegex(@"^\s*game\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex GameLine();

    public static List<Project> List()
    {
        var list = new List<Project>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('_')) continue;
            var script = Path.Combine(dir, "main" + ModScript.Extension);
            if (!File.Exists(script)) continue;
            var text = File.ReadAllText(script);
            list.Add(new Project(name, dir, ModLine().Match(text) is { Success: true } m ? m.Groups[1].Value : name,
                GameLine().Match(text) is { Success: true } g ? g.Groups[1].Value : "", File.GetLastWriteTimeUtc(script)));
        }
        return list.OrderByDescending(p => p.Updated).ToList();
    }

    public static Project? Get(string id) => List().FirstOrDefault(p => p.Id == id);

    public static Project Create(string name, string code)
    {
        var slug = CustomGames.Slug(name);
        if (slug == "") slug = "mod";
        var id = slug;
        for (var i = 2; Directory.Exists(Path.Combine(Root, id)); i++) id = $"{slug}-{i}";
        var dir = Directory.CreateDirectory(Path.Combine(Root, id)).FullName;
        File.WriteAllText(Path.Combine(dir, "main" + ModScript.Extension), code);
        Directory.CreateDirectory(Path.Combine(dir, "files"));
        return Get(id)!;
    }

    public static void Save(Project p, string code) => File.WriteAllText(p.Script, code);

    public static void Delete(Project p)
    {
        try { Directory.Delete(p.Dir, true); } catch { }
    }

    /// <summary>Имя пакета Thunderstore: только буквы, цифры и «_».</summary>
    public static string PackageName(string name)
    {
        var s = Regex.Replace(CustomGames.Translit(name.Trim()), @"[^A-Za-z0-9_]+", "_").Trim('_');
        return s == "" ? "MyMod" : s.Length > 60 ? s[..60] : s;
    }

    public static string UniqueId(ModBuild b) =>
        $"{PackageName(b.Author == "" ? "ModLaunch" : b.Author)}.{PackageName(b.Name)}";

    // ---------------------------------------------------------------- сборка

    /// <summary>Собрать архив. dir — папка проекта (для copy/image), null — скрипт без своих файлов (из галереи).</summary>
    public static async Task<Packed> Pack(ModBuild b, string? dir, CancellationToken ct = default)
    {
        if (!b.Ok) throw new InvalidOperationException(I18n.T("cr.build.hasErrors"));
        var game = GameCatalog.ById(b.Game) ?? throw new InvalidOperationException(I18n.T("cr.build.noGame", ("game", b.Game)));
        var warnings = new List<string>();
        var files = new Dictionary<string, byte[]>();

        byte[] ReadCopy(FileCopy c)
        {
            if (dir is null) throw new InvalidOperationException(I18n.T("cr.build.noFiles", ("file", c.From)));
            var full = Path.GetFullPath(Path.Combine(dir, "files", c.From));
            if (!full.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(I18n.T("cr.err.path"));
            if (!File.Exists(full)) full = Path.GetFullPath(Path.Combine(dir, c.From));
            if (!File.Exists(full)) throw new InvalidOperationException(I18n.T("cr.build.missingFile", ("file", c.From)));
            return File.ReadAllBytes(full);
        }

        string format;
        if (game.Loader == LoaderKind.Smapi)
        {
            format = "Content Patcher";
            var folder = $"[CP] {Regex.Replace(b.Name, "[<>:\"/\\\\|?*]", "")}/";
            var deps = new JsonArray(b.Needs.Select(n => (JsonNode)new JsonObject { ["UniqueID"] = n, ["IsRequired"] = true }).ToArray());
            var manifest = new JsonObject
            {
                ["Name"] = b.Name,
                ["Author"] = b.Author == "" ? "ModLaunch" : b.Author,
                ["Version"] = b.Version,
                ["Description"] = b.About,
                ["UniqueID"] = UniqueId(b),
                ["UpdateKeys"] = new JsonArray(),
                ["ContentPackFor"] = new JsonObject { ["UniqueID"] = "Pathoschild.ContentPatcher" },
                ["Dependencies"] = deps,
            };
            var content = new JsonObject { ["Format"] = "2.0.0", ["Changes"] = new JsonArray(b.Changes.Select(c => (JsonNode)c.DeepClone()).ToArray()) };
            files[folder + "manifest.json"] = Utf8(manifest.ToJsonString(Pretty));
            files[folder + "content.json"] = Utf8(content.ToJsonString(Pretty));
            foreach (var c in b.Copies) files[folder + c.To] = ReadCopy(c);
            foreach (var (path, text) in b.Writes) files[folder + path] = Utf8(text);
            if (b.Configs.Count > 0) warnings.Add(I18n.T("cr.warn.configSmapi"));
            if (b.Changes.Count == 0 && b.Copies.Count == 0) warnings.Add(I18n.T("cr.warn.empty"));
        }
        else if (game.Loader == LoaderKind.Bepinex)
        {
            format = "Thunderstore";
            var deps = new List<string>();
            if (game.ThunderstorePackage is not null && game.ThunderstoreCommunity is not null)
            {
                try { deps.Add($"{game.ThunderstorePackage}-{(await Sources.Thunderstore.Latest(game.ThunderstoreCommunity, game.ThunderstorePackage, ct)).Version}"); }
                catch { warnings.Add(I18n.T("cr.warn.offline")); }
            }
            foreach (var need in b.Needs)
            {
                if (Regex.IsMatch(need, @"-\d+\.\d+\.\d+$")) { deps.Add(need); continue; }
                try
                {
                    var mod = game.ThunderstoreCommunity is null ? null : await Sources.Thunderstore.Get(game.ThunderstoreCommunity, need, ct);
                    if (mod is null) warnings.Add(I18n.T("cr.warn.needMissing", ("mod", need)));
                    else deps.Add($"{mod.Id}-{mod.Version}");
                }
                catch { warnings.Add(I18n.T("cr.warn.needMissing", ("mod", need))); }
            }
            var manifest = new JsonObject
            {
                ["name"] = PackageName(b.Name),
                ["version_number"] = b.Version,
                ["website_url"] = b.Website ?? "",
                ["description"] = (b.About == "" ? b.Name : b.About) is var d && d.Length > 250 ? d[..250] : d,
                ["dependencies"] = new JsonArray(deps.Select(x => (JsonNode)x).ToArray()),
            };
            files["manifest.json"] = Utf8(manifest.ToJsonString(Pretty));
            files["README.md"] = Utf8($"# {b.Name}\n\n{b.About}\n\n_Made with ModLaunch Creator Hub (ModScript)._\n");
            files["icon.png"] = Icon(b, dir) ?? [];
            if (files["icon.png"].Length == 0) { files.Remove("icon.png"); warnings.Add(I18n.T("cr.warn.icon")); }
            foreach (var group in b.Configs.GroupBy(c => c.File))
            {
                var text = "";
                foreach (var section in group.GroupBy(c => c.Section))
                    text = Features.Ini.Patch(text, section.Key, section.ToDictionary(c => c.Key, c => c.Value));
                files["config/" + group.Key] = Utf8(text);
            }
            string Place(string to) => to.StartsWith("plugins/") || to.StartsWith("config/") || to.StartsWith("patchers/") ? to : "plugins/" + to;
            foreach (var c in b.Copies) files[Place(c.To)] = ReadCopy(c);
            foreach (var (path, text) in b.Writes) files[Place(path)] = Utf8(text);
            if (b.Changes.Count > 0) warnings.Add(I18n.T("cr.warn.cpBepinex"));
        }
        else
        {
            format = "zip";
            foreach (var c in b.Copies) files[c.To] = ReadCopy(c);
            foreach (var (path, text) in b.Writes) files[path] = Utf8(text);
            if (b.Changes.Count > 0 || b.Configs.Count > 0) warnings.Add(I18n.T("cr.warn.onlyFiles"));
        }
        if (b.Inis.Count > 0)
        {
            var edits = new JsonArray(b.Inis.Select(i => (JsonNode)new JsonObject { ["file"] = i.File, ["section"] = i.Section, ["key"] = i.Key, ["value"] = i.Value }).ToArray());
            files["modlaunch-ini.json"] = Utf8(edits.ToJsonString(Pretty));
        }

        var zip = Path.Combine(OutDir, $"{PackageName(b.Name)}-{b.Version}.zip");
        if (File.Exists(zip)) File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (path, bytes) in files)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                await using var s = entry.Open();
                await s.WriteAsync(bytes, ct);
            }
        }
        return new Packed(zip, format, files.Keys.ToList(), warnings);
    }

    static readonly System.Text.Json.JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    /// <summary>Значок 256×256 для Thunderstore: свой (icon "file.png") или значок ModLaunch.</summary>
    static byte[]? Icon(ModBuild b, string? dir)
    {
        try
        {
            if (b.Icon is not null && dir is not null)
            {
                var own = new[] { Path.Combine(dir, "files", b.Icon), Path.Combine(dir, b.Icon) }.FirstOrDefault(File.Exists);
                if (own is not null) return Resize(File.ReadAllBytes(own));
            }
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/icon.png"));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Resize(ms.ToArray());
        }
        catch { return null; }
    }

    static byte[] Resize(byte[] png)
    {
        using var input = new MemoryStream(png);
        using var bmp = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(input, 256);
        using var scaled = bmp.CreateScaledBitmap(new Avalonia.PixelSize(256, 256));
        using var output = new MemoryStream();
        scaled.Save(output);
        return output.ToArray();
    }

    // ---------------------------------------------------------------- установка в игру

    /// <summary>Поставить собранный мод в игру: файлы как мод, строки настроек — прямо в .cfg/.ini.</summary>
    public static JsonObject Install(GameState g, ModBuild b, Packed packed)
    {
        if (g.Path is null || g.Registry is null) throw new InvalidOperationException(I18n.T("cr.install.noGame"));
        var record = Install(g.Registry, b, packed, "creator:" + PackageName(b.Name), "creator");
        g.Refresh();
        return record;
    }

    public static JsonObject Install(ModRegistry registry, ModBuild b, Packed packed, string id, string source, JsonObject? extra = null)
    {
        if (registry.Has(id)) registry.Remove(id);
        var meta = new JsonObject
        {
            ["id"] = id,
            ["name"] = b.Name,
            ["version"] = b.Version,
            ["author"] = b.Author,
            ["source"] = source,
        };
        foreach (var (k, v) in extra ?? new JsonObject()) meta[k] = v?.DeepClone();
        var record = Installer.InstallArchive(registry, packed.Zip, meta);
        ApplySettings(registry.Game, registry.GamePath, b);
        return record;
    }

    /// <summary>Строки config/ini из скрипта — прямо в файлы игры (с копией .modlaunch-bak).</summary>
    public static void ApplySettings(GameDef game, string gamePath, ModBuild b)
    {
        if (game.Loader == LoaderKind.Bepinex)
            foreach (var group in b.Configs.GroupBy(c => c.File))
                Patch(Path.Combine(gamePath, "BepInEx", "config", group.Key), group);
        foreach (var group in b.Inis.GroupBy(c => c.File))
        {
            var file = Path.GetFullPath(Path.Combine(gamePath, group.Key));
            if (!file.StartsWith(Path.GetFullPath(gamePath), StringComparison.OrdinalIgnoreCase)) continue;
            Patch(file, group);
        }
    }

    static void Patch(string file, IEnumerable<ConfigEdit> edits)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var text = File.Exists(file) ? File.ReadAllText(file) : "";
        if (File.Exists(file)) File.Copy(file, file + ".modlaunch-bak", true);
        foreach (var section in edits.GroupBy(e => e.Section))
            text = Features.Ini.Patch(text, section.Key, section.ToDictionary(e => e.Key, e => e.Value));
        File.WriteAllText(file, text);
    }

    /// <summary>Чего не хватает в игре, чтобы мод заработал (Content Patcher, моды из needs).</summary>
    public static List<string> MissingNeeds(GameState g, ModBuild b)
    {
        var have = g.Registry?.List().SelectMany(m => new[] { m.Str("id"), m.Str("uniqueId") }).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var need = new List<string>(b.Needs);
        if (g.Def.Loader == LoaderKind.Smapi && b.Changes.Count > 0) need.Insert(0, "Pathoschild.ContentPatcher");
        return need.Where(n => !have.Contains(n) && !have.Contains(g.Def.RecordId(n))).ToList();
    }
}
