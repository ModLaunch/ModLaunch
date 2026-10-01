using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Mods;

public sealed record InstallStep(string Code, string Mod, int Index = 0, int Total = 0, double Ratio = -1);

/// <summary>Установка модов: из каталога (с зависимостями) и из файла.</summary>
public static partial class Installer
{
    [GeneratedRegex(@"^(plugins|bepinex|patchers|core|mods|files|release|releases|bin|dll|dlls|build|output|x64|net\d*|netstandard[\d.]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericFolder();

    [GeneratedRegex(@"-bepinexpack(_\w+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LoaderPack();

    static bool IsLoaderPackage(GameDef game, string id) =>
        id.Equals(game.ThunderstorePackage, StringComparison.OrdinalIgnoreCase) || LoaderPack().IsMatch(id);

    /// <summary>План установки: мод и его зависимости по порядку.</summary>
    public static async Task<(List<ModInfo> Order, List<string> Missing)> Plan(GameDef game, ModInfo mod, CancellationToken ct)
    {
        (List<ModInfo> Order, List<string> Missing) plan = mod.Source switch
        {
            "thunderstore" => await Thunderstore.Resolve(game.ThunderstoreCommunity!, mod.Id, ct),
            "modlinks" => await ModLinks.Resolve(mod.Id, ct),
            _ => (new List<ModInfo> { mod }, new List<string>()),
        };
        plan.Order.RemoveAll(m => IsLoaderPackage(game, m.Id));
        return plan;
    }

    /// <summary>Каталожный мод с прямой ссылкой (Thunderstore, ModLinks) — вместе с зависимостями.</summary>
    public static async Task<int> InstallFromCatalog(ModRegistry registry, ModInfo mod, IProgress<InstallStep> progress, CancellationToken ct, bool reinstall = false, string? pinVersion = null, string? pinUrl = null)
    {
        progress.Report(new InstallStep("install.deps", mod.Name));
        var (steps, _) = await Plan(registry.Game, mod, ct);
        // Своя версия (вкладка «Файлы и версии»): вместо последней — выбранная.
        if (pinVersion is not null)
        {
            reinstall = true;
            var at = steps.FindIndex(x => x.Id == mod.Id);
            if (at >= 0) steps[at] = steps[at].WithVersion(pinVersion, pinUrl);
        }
        var installed = 0;
        for (var i = 0; i < steps.Count; i++)
        {
            var entry = steps[i];
            if (!(reinstall && entry.Id == mod.Id) && registry.Get(entry.Id) is { } existing && !existing.Bool("missing"))
            {
                progress.Report(new InstallStep("install.exists", entry.Name, i + 1, steps.Count));
                continue;
            }
            if (entry.Source == "hub")
            {
                // ModLaunch Hub: архив из кусков (пакет) или сборка из исходника (скрипт).
                var hubVersion = entry.Id == mod.Id && pinVersion is not null ? (await Creator.Hub.Versions(entry.Id)).FirstOrDefault(v => v.Version == pinVersion) : null;
                var step = i + 1;
                await Creator.HubInstaller.Install(registry, entry.Id, new Progress<double>(r => progress.Report(new InstallStep("install.download", entry.Name, step, steps.Count, r))), ct, hubVersion);
                installed++;
                continue;
            }
            var url = entry.DownloadUrl ?? throw new InvalidOperationException(I18n.T("err.modNotInCatalog"));
            var index = i;
            var file = await Http.Download(url, Regex.Replace(entry.Id, @"[^\w.-]+", "_") + ".zip",
                new Progress<double>(r => progress.Report(new InstallStep("install.download", entry.Name, index + 1, steps.Count, r))),
                entry.Sha256, ct);
            try
            {
                progress.Report(new InstallStep("install.extract", entry.Name, i + 1, steps.Count));
                await InstallAny(registry, file, new JsonObject
                {
                    ["id"] = entry.Id,
                    ["name"] = entry.Name,
                    ["version"] = entry.Version,
                    ["author"] = entry.Author,
                    ["source"] = entry.Source,
                    ["url"] = entry.Url,
                    ["icon"] = entry.Icon,
                    ["requestedBy"] = entry.Id == mod.Id ? null : mod.Id,
                }, progress, ct);
                if (entry.Source == "thunderstore" && registry.Game.Loader == LoaderKind.Bepinex && IsModpack(entry)) ApplyPackConfig(registry.GamePath, file);
                Features.DownloadArchive.Add(registry.Game.Id, entry.Id, entry.Version, file);
                installed++;
            }
            finally { TryDelete(file); }
        }
        progress.Report(new InstallStep("install.done", mod.Name));
        return installed;
    }

    public static bool IsModpack(ModInfo mod) => mod.Categories.Any(c => c.Contains("modpack", StringComparison.OrdinalIgnoreCase));

    /// <summary>Настройки сборки Thunderstore (config/ или BepInEx/config/) — в BepInEx/config поверх своих: сборка без них не та.</summary>
    static void ApplyPackConfig(string gamePath, string archive)
    {
        var target = Path.Combine(gamePath, "BepInEx", "config");
        foreach (var folder in new[] { "config", "BepInEx/config" })
        {
            try { if (Archive.HasFolder(archive, folder)) Archive.Extract(archive, target, folder, ignoreCase: true); } catch { }
        }
    }

    /// <summary>Любой архив: пресет ReShade ставится как пресет, остальное — как мод.</summary>
    public static async Task<JsonObject> InstallAny(ModRegistry registry, string archivePath, JsonObject meta, IProgress<InstallStep> progress, CancellationToken ct)
    {
        Archive.EnsureArchive(archivePath);
        if (Features.ReShade.LooksLikePreset(archivePath)) return await InstallPreset(registry, archivePath, meta, progress, ct);
        return InstallArchive(registry, archivePath, meta);
    }

    /// <summary>Пресет ReShade: ставим ReShade, если его нет, докачиваем эффекты и делаем пресет текущим.</summary>
    static async Task<JsonObject> InstallPreset(ModRegistry registry, string archivePath, JsonObject meta, IProgress<InstallStep> progress, CancellationToken ct)
    {
        var game = registry.Game;
        var dir = registry.PresetDir;
        if (Features.ReShade.Detect(dir).Dll is null) await Features.ReShade.Install(dir, game.ReShadeApi, progress, ct);
        var presets = Features.ReShade.ExtractPreset(dir, archivePath);
        if (presets.Count == 0) throw new InvalidOperationException(I18n.T("err.presetEmpty"));
        var wanted = presets.SelectMany(p => Features.ReShade.PresetEffects(File.ReadAllText(Path.Combine(dir, p)))).Distinct().ToList();
        var missing = await Features.ReShade.EnsureEffects(dir, wanted, progress, ct);
        Features.ReShade.Activate(dir, Path.Combine(dir, presets[0]));

        JsonObject? primary = null;
        var metaId = meta.Str("id");
        foreach (var file in presets)
        {
            var title = Path.GetFileNameWithoutExtension(file);
            var saved = registry.Add(new JsonObject
            {
                ["id"] = primary is not null && metaId is not null ? $"{metaId}#{file}" : metaId ?? $"preset:{file}",
                ["name"] = primary is not null ? title : meta.Str("name") ?? title,
                ["version"] = meta.Str("version") ?? "",
                ["author"] = meta.Str("author") ?? "",
                ["source"] = meta.Str("source") ?? "file",
                ["url"] = meta.Str("url"),
                ["icon"] = meta.Str("icon"),
                ["kind"] = "preset",
                ["folder"] = file,
                ["fileCount"] = 1,
                ["dependencies"] = new JsonArray(),
                ["requires"] = new JsonArray(),
                ["missingEffects"] = new JsonArray(missing.Select(x => (JsonNode)x).ToArray()),
                ["requestedBy"] = primary is not null && metaId is not null ? metaId : null,
                ["enabled"] = true,
                ["missing"] = false,
            });
            primary ??= saved;
        }
        return primary!;
    }

    /// <summary>Текущий пресет ReShade после включения, выключения или удаления.</summary>
    public static void SyncPreset(ModRegistry registry, JsonObject? preferred)
    {
        try
        {
            var active = preferred ?? registry.List().LastOrDefault(m => m.Str("kind") == "preset" && m.Bool("enabled", true) && !m.Bool("missing"));
            Features.ReShade.Activate(registry.PresetDir, active is null ? null : Path.Combine(registry.PresetDir, active.Str("folder")!));
        }
        catch { }
    }

    /// <summary>
    /// Разложить архив мода по папкам и записать в список. При обновлении прошлая
    /// копия мода убирается целиком (в том числе из другой папки), а его config.json остаётся.
    /// </summary>
    public static JsonObject InstallArchive(ModRegistry registry, string archivePath, JsonObject meta)
    {
        var game = registry.Game;
        var entries = Archive.Files(archivePath);
        if (entries.Count == 0) throw new InvalidOperationException(I18n.T("err.archiveStructure"));
        var metaId = meta.Str("id");
        var previous = metaId is null ? [] : registry.Family(metaId);
        var oldPaths = previous.SelectMany(registry.PathsOf).ToList();
        var configs = SaveConfigs(registry, previous, metaId);

        var records = game.Loader == LoaderKind.Bepinex && BepinexBase(entries) is { } root
            ? [InstallRouted(registry, archivePath, entries, root, meta)]
            : InstallRoots(registry, archivePath, meta);

        var keep = records.SelectMany(registry.PathsOf).Select(Full).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in oldPaths.Where(p => !keep.Contains(Full(p))))
            try { registry.DeletePath(path); } catch { }
        var fresh = records.Select(r => r.Str("id")).ToHashSet();
        foreach (var old in previous.Where(p => !fresh.Contains(p.Str("id")))) registry.Forget(old.Str("id")!);
        RestoreConfigs(registry, records, configs, metaId);
        return records[0];
    }

    static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>Каждый корень (папка с manifest.json или .dll) — своя папка и своя запись.</summary>
    static List<JsonObject> InstallRoots(ModRegistry registry, string archivePath, JsonObject meta)
    {
        var game = registry.Game;
        var roots = Archive.FindRoots(archivePath, game.ModMarker, game.ModMarker == "manifest" ? 5 : 4);
        if (roots.Count == 0) throw new InvalidOperationException(I18n.T("err.archiveStructure"));
        Directory.CreateDirectory(registry.ModsDir);

        var records = new List<JsonObject>();
        var folders = new List<string>();
        var metaId = meta.Str("id");
        var metaName = meta.Str("name");
        foreach (var root in roots)
        {
            var fileMeta = root.Marker is not null && root.Marker.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)
                ? ReadManifest(game, Archive.ReadText(archivePath, root.Marker), root.Name)
                : new Manifest(null, null, null, null, []);

            var rawName = fileMeta.Name ?? root.Name ?? metaName ?? "mod";
            var generic = GenericFolder().IsMatch(rawName.Trim());
            var folder = Sanitize(generic ? metaName ?? metaId ?? rawName : rawName);
            if (folders.Contains(folder)) folder = Sanitize($"{folder} ({rawName})");
            var destination = Path.Combine(registry.ModsDir, folder);
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            var files = Archive.Extract(archivePath, destination, root.Prefix);
            folders.Add(folder);

            var first = records.Count == 0;
            records.Add(registry.Add(new JsonObject
            {
                ["id"] = !first && metaId is not null ? $"{metaId}#{folder}" : metaId ?? fileMeta.Id ?? folder,
                ["name"] = !first ? fileMeta.Name ?? folder : metaName ?? fileMeta.Name ?? folder,
                ["version"] = fileMeta.Version ?? meta.Str("version") ?? "",
                ["author"] = fileMeta.Author ?? meta.Str("author") ?? "",
                ["source"] = meta.Str("source") ?? "file",
                ["url"] = meta.Str("url"),
                ["icon"] = meta.Str("icon"),
                ["folder"] = folder,
                ["fileCount"] = files.Count,
                ["dependencies"] = new JsonArray(fileMeta.Dependencies.Select(d => (JsonNode)d).ToArray()),
                ["uniqueId"] = fileMeta.Id is not null && game.ModMarker == "manifest" ? fileMeta.Id : null,
                ["requires"] = new JsonArray(),
                ["requestedBy"] = !first && metaId is not null ? metaId : meta.Str("requestedBy"),
                ["enabled"] = true,
                ["missing"] = false,
            }));
        }
        return records;
    }

    static readonly string[] BepinexFolders = ["bepinex", "plugins", "patchers", "monomod"];

    [GeneratedRegex(@"^(winhttp\.dll|doorstop_config\.ini|\.doorstop_version)$", RegexOptions.IgnoreCase)]
    private static partial Regex LoaderFile();

    /// <summary>
    /// Откуда раскладывать архив по-BepInEx: папка, где лежат BepInEx/, plugins/, patchers/
    /// или monomod/, либо manifest.json пакета Thunderstore рядом с .dll. Не глубже двух
    /// обёрток; если таких папок несколько (несколько модов в архиве) — null.
    /// </summary>
    static string? BepinexBase(List<string> entries)
    {
        for (var depth = 0; depth <= 2; depth++)
        {
            var found = entries.Select(e => e.Split('/')).Where(p => p.Length > depth)
                .GroupBy(p => depth == 0 ? "" : string.Join('/', p[..depth]) + "/", StringComparer.OrdinalIgnoreCase)
                .Where(g =>
                {
                    var folders = g.Where(p => p.Length > depth + 1).Select(p => p[depth].ToLowerInvariant()).ToHashSet();
                    if (folders.Overlaps(BepinexFolders)) return true;
                    var manifest = g.Any(p => p.Length == depth + 1 && p[depth].Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
                    return manifest && g.Any(p => p[^1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                })
                .Select(g => g.Key).ToList();
            if (found.Count == 1) return found[0];
            if (found.Count > 1) return null;
        }
        return null;
    }

    /// <summary>
    /// Мод BepInEx одной папкой, как это делает r2modman: plugins/ и файлы пакета —
    /// в BepInEx/plugins/&lt;мод&gt;, patchers/ и monomod/ — в свои папки BepInEx под тем же
    /// именем, config/ — в BepInEx/config (свои настройки человека не перезаписываются).
    /// Сам загрузчик (BepInEx/core, winhttp.dll) из архива не берём — его ставит ModLaunch.
    /// </summary>
    static JsonObject InstallRouted(ModRegistry registry, string archivePath, List<string> entries, string root, JsonObject meta)
    {
        var game = registry.Game;
        var manifestEntry = entries.FirstOrDefault(e => e.Equals(root + "manifest.json", StringComparison.OrdinalIgnoreCase));
        var fileMeta = manifestEntry is null ? new Manifest(null, null, null, null, []) : ReadManifest(game, Archive.ReadText(archivePath, manifestEntry), "");
        var metaId = meta.Str("id");
        var folder = Sanitize(meta.Str("source") == "thunderstore" && metaId is not null ? metaId
            : meta.Str("name") ?? fileMeta.Name ?? Path.GetFileNameWithoutExtension(archivePath));
        var withLoader = entries.Any(e => e.StartsWith(root + "BepInEx/", StringComparison.OrdinalIgnoreCase));

        foreach (var part in new[] { "plugins", "patchers", "monomod" })
            registry.DeletePath(Path.Combine(registry.GamePath, "BepInEx", part, folder));

        var parts = new HashSet<string>();
        (string, bool)? Map(string entry)
        {
            if (!entry.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            var rel = entry[root.Length..];
            var inLoader = rel.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase);
            if (inLoader) rel = rel["BepInEx/".Length..];
            var slash = rel.IndexOf('/');
            var head = slash < 0 ? "" : rel[..slash].ToLowerInvariant();
            var rest = slash < 0 ? rel : rel[(slash + 1)..];
            switch (head)
            {
                case "plugins": return ($"BepInEx/plugins/{folder}/{rest}", false);
                case "patchers" or "monomod": parts.Add(head); return ($"BepInEx/{head}/{folder}/{rest}", false);
                case "config": return ($"BepInEx/config/{rest}", true);
            }
            if (inLoader || withLoader || head == "core" || LoaderFile().IsMatch(rel)) return null;
            return ($"BepInEx/plugins/{folder}/{rel}", false);
        }

        var written = Archive.ExtractMapped(archivePath, registry.GamePath, Map);
        var files = written.Count(w => !w.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase));
        if (files == 0 && written.Count == 0) throw new InvalidOperationException(I18n.T("err.archive.loaderOnly"));
        Directory.CreateDirectory(Path.Combine(registry.ModsDir, folder));

        return registry.Add(new JsonObject
        {
            ["id"] = metaId ?? fileMeta.Id ?? folder,
            ["name"] = meta.Str("name") ?? fileMeta.Name ?? folder,
            ["version"] = meta.Str("version") is { Length: > 0 } v ? v : fileMeta.Version ?? "",
            ["author"] = meta.Str("author") ?? fileMeta.Author ?? "",
            ["source"] = meta.Str("source") ?? "file",
            ["url"] = meta.Str("url"),
            ["icon"] = meta.Str("icon"),
            ["folder"] = folder,
            ["extra"] = new JsonArray(parts.Order().Select(p => (JsonNode)$"BepInEx/{p}/{folder}").ToArray()),
            ["fileCount"] = written.Count,
            ["dependencies"] = new JsonArray(fileMeta.Dependencies.Select(d => (JsonNode)d).ToArray()),
            ["requires"] = new JsonArray(),
            ["requestedBy"] = meta.Str("requestedBy"),
            ["enabled"] = true,
            ["missing"] = false,
        });
    }

    /// <summary>config.json модов SMAPI: игра пишет туда настройки человека, а в архиве его нет.</summary>
    static Dictionary<string, byte[]> SaveConfigs(ModRegistry registry, List<JsonObject> previous, string? metaId)
    {
        var saved = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (registry.Game.Loader != LoaderKind.Smapi) return saved;
        foreach (var mod in previous)
        {
            var file = Path.Combine(registry.FolderFor(mod), "config.json");
            try
            {
                if (!File.Exists(file)) continue;
                var bytes = File.ReadAllBytes(file);
                saved["folder:" + (mod.Str("folder") ?? "")] = bytes;
                if (mod.Str("id") == metaId) saved["primary"] = bytes;
            }
            catch { }
        }
        return saved;
    }

    static void RestoreConfigs(ModRegistry registry, List<JsonObject> records, Dictionary<string, byte[]> saved, string? metaId)
    {
        foreach (var mod in records)
        {
            var file = Path.Combine(registry.FolderFor(mod), "config.json");
            if (File.Exists(file)) continue;
            if (saved.TryGetValue("folder:" + (mod.Str("folder") ?? ""), out var bytes) || (mod.Str("id") == metaId && saved.TryGetValue("primary", out bytes)))
                try { File.WriteAllBytes(file, bytes); } catch { }
        }
    }

    sealed record Manifest(string? Id, string? Name, string? Version, string? Author, List<string> Dependencies);

    static Manifest ReadManifest(GameDef game, string? text, string fallback)
    {
        if (text is null) return new(null, null, null, null, []);
        try
        {
            var node = JsonNode.Parse(Regex.Replace(text, @",\s*([}\]])", "$1"), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (game.Loader == LoaderKind.Smapi)
            {
                var deps = node.Arr("Dependencies").Where(d => d.Bool("IsRequired", true)).Select(d => d.Str("UniqueID")).OfType<string>().ToList();
                if (node?["ContentPackFor"].Str("UniqueID") is string cp) deps.Add(cp);
                return new(node.Str("UniqueID"), node.Str("Name"), node.Str("Version"), node.Str("Author"), deps);
            }
            // Thunderstore manifest.json: зависимости вида Owner-Name-1.2.3.
            var ts = node.Arr("dependencies").Select(d => d?.GetValue<string>()).OfType<string>()
                .Select(d => string.Join('-', d.Split('-').Take(2))).ToList();
            return new(node.Str("name"), node.Str("name"), node.Str("version_number"), null, ts);
        }
        catch { return new(fallback, fallback, null, null, []); }
    }

    static string Sanitize(string name)
    {
        var s = Regex.Replace(name, "[<>:\"/\\\\|?*\\x00-\\x1f]", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length > 120) s = s[..120];
        return s == "" ? "mod" : s;
    }

    static void TryDelete(string file) { try { File.Delete(file); } catch { } }

    /// <summary>Зависимости, которых нет среди установленных модов.</summary>
    public static List<(string Mod, string Missing)> CheckDependencies(ModRegistry registry)
    {
        var mods = registry.List();
        var ids = mods.SelectMany(m => new[] { m.Str("id"), m.Str("uniqueId") }).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<(string, string)>();
        foreach (var mod in mods.Where(m => m.Bool("enabled", true)))
        foreach (var dep in mod.Arr("dependencies").Select(d => d?.ToString()).OfType<string>())
        {
            if (IsLoaderPackage(registry.Game, dep) || ids.Contains(dep)) continue;
            if (dep.Equals("Pathoschild.SMAPI", StringComparison.OrdinalIgnoreCase)) continue;
            problems.Add((mod.Str("name") ?? "", dep));
        }
        return problems;
    }
}
