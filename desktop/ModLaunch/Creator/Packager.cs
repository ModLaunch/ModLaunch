using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Creator;

/// <summary>Что упаковываем: папка с готовым модом и сведения для пакета.</summary>
public sealed class PackSpec
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public string Website { get; set; } = "";
    public string Author { get; set; } = "";
    public string Game { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Icon { get; set; }
    public string? Readme { get; set; }
    public List<string> Needs { get; set; } = [];
}

/// <summary>Одна найденная проблема: ошибка не даёт собрать, предупреждение — только сообщает.</summary>
public sealed record Problem(bool Error, string Key, params (string, object)[] Args);

/// <summary>
/// Упаковщик: папка с собранным модом → zip нужного вида (Thunderstore для BepInEx,
/// мод SMAPI, просто архив). Проверяет то, на чём обычно спотыкаются при публикации:
/// имя пакета, версию, длину описания, значок 256×256, manifest.json.
/// </summary>
public static partial class Packager
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+$")] private static partial Regex Semver();
    [GeneratedRegex(@"^[A-Za-z0-9_]+$")] private static partial Regex PackageNameRule();

    static readonly string[] Roots = ["plugins", "patchers", "config", "core", "monomod"];

    public static List<Problem> Validate(PackSpec s)
    {
        var list = new List<Problem>();
        var game = GameCatalog.ById(s.Game);
        if (game is null) list.Add(new(true, "cr.pack.p.game"));
        if (s.Name.Trim() == "") list.Add(new(true, "cr.pack.p.name"));
        else if (!PackageNameRule().IsMatch(s.Name)) list.Add(new(false, "cr.pack.p.nameFix", ("name", Projects.PackageName(s.Name))));
        if (!Semver().IsMatch(s.Version)) list.Add(new(true, "cr.pack.p.version"));
        if (s.Description.Length > 250) list.Add(new(false, "cr.pack.p.desc"));
        if (s.Description.Trim() == "") list.Add(new(false, "cr.pack.p.noDesc"));
        if (s.Source == "" || !Directory.Exists(s.Source)) list.Add(new(true, "cr.pack.p.source"));
        else
        {
            var files = Directory.EnumerateFiles(s.Source, "*", SearchOption.AllDirectories).Where(f => !Skip(Path.GetRelativePath(s.Source, f))).ToList();
            if (files.Count == 0) list.Add(new(true, "cr.pack.p.empty"));
            else if (game?.Loader == LoaderKind.Bepinex && !files.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) list.Add(new(false, "cr.pack.p.noDll"));
            else if (game?.Loader == LoaderKind.Smapi && !files.Any(f => Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))) list.Add(new(true, "cr.pack.p.noManifest"));
        }
        if (s.Icon is not null && !File.Exists(s.Icon)) list.Add(new(true, "cr.pack.p.iconMissing"));
        if (s.Icon is null && game?.Loader == LoaderKind.Bepinex) list.Add(new(false, "cr.pack.p.icon"));
        if (game is not null && game.Loader == LoaderKind.None) list.Add(new(false, "cr.pack.p.noLoader"));
        return list;
    }

    /// <summary>Служебное из папки сборки в пакет не идёт: отладочные файлы, .pdb, следы сборки, наши заметки.</summary>
    static bool Skip(string rel)
    {
        rel = rel.Replace('\\', '/');
        var name = Path.GetFileName(rel);
        return name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || name is ".gitignore" or "modlaunch-project.json" or "Thumbs.db"
            || rel.StartsWith("obj/") || rel.StartsWith(".vs/");
    }

    public static string OutDir => Projects.OutDir;

    public static async Task<Packed> Pack(PackSpec s, CancellationToken ct = default)
    {
        var errors = Validate(s).Where(p => p.Error).ToList();
        if (errors.Count > 0) throw new InvalidOperationException(I18n.T(errors[0].Key, errors[0].Args));
        var game = GameCatalog.ById(s.Game)!;
        var name = Projects.PackageName(s.Name);
        var warnings = new List<string>();
        var files = new Dictionary<string, byte[]>();
        var source = Directory.EnumerateFiles(s.Source, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(s.Source, f).Replace('\\', '/'))).Where(f => !Skip(f.Rel)).ToList();
        string format;

        if (game.Loader == LoaderKind.Smapi)
        {
            format = "SMAPI";
            // Мод лежит в папке со своим именем: Mods/<Имя>/manifest.json.
            var manifest = source.First(f => Path.GetFileName(f.Full).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)).Rel;
            var prefix = manifest.Contains('/') ? manifest[..manifest.LastIndexOf('/')] : "";
            foreach (var f in source.Where(f => prefix == "" || f.Rel.StartsWith(prefix + "/")))
                files[$"{name}/{(prefix == "" ? f.Rel : f.Rel[(prefix.Length + 1)..])}"] = await File.ReadAllBytesAsync(f.Full, ct);
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
            foreach (var need in s.Needs.Where(n => n.Trim() != ""))
            {
                if (Regex.IsMatch(need, @"-\d+\.\d+\.\d+$")) { deps.Add(need.Trim()); continue; }
                try
                {
                    var mod = game.ThunderstoreCommunity is null ? null : await Sources.Thunderstore.Get(game.ThunderstoreCommunity, need.Trim(), ct);
                    if (mod is null) warnings.Add(I18n.T("cr.warn.needMissing", ("mod", need)));
                    else deps.Add($"{mod.Id}-{mod.Version}");
                }
                catch { warnings.Add(I18n.T("cr.warn.needMissing", ("mod", need))); }
            }
            files["manifest.json"] = Utf8(new JsonObject
            {
                ["name"] = name,
                ["version_number"] = s.Version,
                ["website_url"] = s.Website,
                ["description"] = s.Description.Length > 250 ? s.Description[..250] : s.Description,
                ["dependencies"] = new JsonArray(deps.Distinct().Select(x => (JsonNode)x).ToArray()),
            }.ToJsonString(Pretty));
            files["README.md"] = s.Readme is not null && File.Exists(s.Readme) ? await File.ReadAllBytesAsync(s.Readme, ct) : Utf8($"# {s.Name}\n\n{s.Description}\n");
            if (Icon(s.Icon) is { } icon) files["icon.png"] = icon;
            // Свои плагины кладём в plugins/<Имя>/, чтобы не смешиваться с чужими; готовая структура остаётся как есть.
            var structured = source.Any(f => Roots.Contains(f.Rel.Split('/')[0], StringComparer.OrdinalIgnoreCase));
            foreach (var f in source) files[structured ? f.Rel : $"plugins/{name}/{f.Rel}"] = await File.ReadAllBytesAsync(f.Full, ct);
        }
        else
        {
            format = "zip";
            foreach (var f in source) files[f.Rel] = await File.ReadAllBytesAsync(f.Full, ct);
        }

        var zip = Path.Combine(OutDir, $"{name}-{s.Version}.zip");
        if (File.Exists(zip)) File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (path, bytes) in files)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                await using var stream = entry.Open();
                await stream.WriteAsync(bytes, ct);
            }
        }
        return new Packed(zip, format, files.Keys.OrderBy(k => k).ToList(), warnings);
    }

    static readonly System.Text.Json.JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    /// <summary>Значок 256×256: свой файл или значок ModLaunch, если своего нет.</summary>
    static byte[]? Icon(string? path)
    {
        try { return Projects.IconBytes(path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null); }
        catch { return null; }
    }
}
