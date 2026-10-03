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
        // Мод просит другой загрузчик (MelonLoader, BepInEx IL2CPP, UE4SS) — в plugins он не заработает: говорим честно.
        if (!IsLoaderPackage(game, mod.Id) && !Features.Compat.Confirmed.Contains(mod.Id) && Features.Compat.Foreign(game, plan.Order.Select(m => m.Id).Concat(plan.Missing).Concat(mod.Dependencies)) is { } loader)
            throw new InvalidOperationException(I18n.T("compat.install", ("mod", mod.Name), ("loader", loader), ("ours", game.LoaderName)));
        plan.Order.RemoveAll(m => IsLoaderPackage(game, m.Id));
        return plan;
    }

    /// <summary>
    /// Каталожный мод с прямой ссылкой (Thunderstore, ModLinks) — вместе с зависимостями.
    /// refreshDeps — при обновлении заодно обновить устаревшие зависимости (включённые и не закреплённые).
    /// </summary>
    public static async Task<int> InstallFromCatalog(ModRegistry registry, ModInfo mod, IProgress<InstallStep> progress, CancellationToken ct, bool reinstall = false, string? pinVersion = null, string? pinUrl = null, bool refreshDeps = false)
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
            var before = registry.Get(entry.Id);
            var stale = refreshDeps && before is not null && before.Bool("enabled", true) && !before.Bool("hold") && Features.Versions.IsNewer(entry.Version, before.Str("version"));
            if (!(reinstall && entry.Id == mod.Id) && !stale && before is { } existing && !existing.Bool("missing"))
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
                    ["dependencies"] = new JsonArray(entry.Dependencies.Select(d => (JsonNode)d).ToArray()),
                    ["requestedBy"] = entry.Id == mod.Id ? null : stale ? before!.Str("requestedBy") : mod.Id,
                }, progress, ct);
                // Обновлённая зависимость могла лечь в папку с другим именем — старую убираем, чтобы не было двух версий.
                if (stale && registry.OwnFolder(before!) is string old && registry.Get(entry.Id) is { } fresh && registry.FolderFor(fresh) != old && Directory.Exists(old))
                    Directory.Delete(old, true);
                if (entry.Source == "thunderstore" && registry.Game.Loader == LoaderKind.Bepinex && IsModpack(entry)) ApplyPackConfig(registry.GamePath, file);
                Features.DownloadArchive.Add(registry.Game.Id, entry.Id, entry.Version, file);
                installed++;
            }
            finally { TryDelete(file); }
        }
        progress.Report(new InstallStep("install.done", mod.Name));
        return installed;
    }

    /// <summary>Сборка Thunderstore (modpack) задаёт свои настройки — заменяем, сохранив старые в ModHub/config-backup.</summary>
    static void ApplyPackConfig(string gamePath, string archive)
    {
        try
        {
            foreach (var part in BepInExLayout.Find(Archive.Files(archive)).Where(p => p.Kind == "config"))
                BepInExLayout.ApplyConfig(archive, part, gamePath, overwrite: true);
        }
        catch { }
    }

    static bool IsModpack(ModInfo mod) => mod.Categories.Any(c => c.Contains("modpack", StringComparison.OrdinalIgnoreCase));

    /// <summary>Любой архив: пресет ReShade ставится как пресет, остальное — как мод.</summary>
    public static async Task<JsonObject> InstallAny(ModRegistry registry, string archivePath, JsonObject meta, IProgress<InstallStep> progress, CancellationToken ct)
    {
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

    /// <summary>Разложить архив мода по папкам и записать в список.</summary>
    public static JsonObject InstallArchive(ModRegistry registry, string archivePath, JsonObject meta)
    {
        var game = registry.Game;
        // BepInEx: patchers/, monomod/, config/ и core/ — не в plugins (см. BepInExLayout).
        var parts = game.Loader == LoaderKind.Bepinex ? BepInExLayout.Find(Archive.Files(archivePath)).Where(p => p.Kind != "plugins").ToList() : [];
        Func<string, bool>? skip = parts.Count > 0 ? entry => BepInExLayout.Inside(entry, parts) : null;
        var roots = Archive.FindRoots(archivePath, game.ModMarker, game.ModMarker == "manifest" ? 5 : 4, skip);
        if (roots.Count == 0) throw new InvalidOperationException(I18n.T("err.archiveStructure"));
        Directory.CreateDirectory(registry.ModsDir);

        // У BepInEx и HK корень мода — папка с .dll, а manifest.json Thunderstore лежит в корне архива:
        // зависимости берём из каталога или оттуда, иначе не видно, каким модам нужна библиотека.
        var packDeps = meta["dependencies"] is JsonArray given ? given.Select(d => d?.ToString()).OfType<string>().ToList()
            : game.ModMarker == "manifest" ? [] : ReadManifest(game, Archive.ReadText(archivePath, "manifest.json"), "").Dependencies;

        JsonObject? primary = null;
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
            var files = Archive.Extract(archivePath, destination, root.Prefix, skip: skip);
            folders.Add(folder);

            var record = new JsonObject
            {
                ["id"] = primary is not null && metaId is not null ? $"{metaId}#{folder}" : metaId ?? fileMeta.Id ?? folder,
                ["name"] = primary is not null ? fileMeta.Name ?? folder : metaName ?? fileMeta.Name ?? folder,
                ["version"] = fileMeta.Version ?? meta.Str("version") ?? "",
                ["author"] = fileMeta.Author ?? meta.Str("author") ?? "",
                ["source"] = meta.Str("source") ?? "file",
                ["url"] = meta.Str("url"),
                ["icon"] = meta.Str("icon"),
                ["folder"] = folder,
                ["fileCount"] = files.Count,
                ["dependencies"] = new JsonArray((primary is null && fileMeta.Dependencies.Count == 0 ? packDeps : fileMeta.Dependencies).Select(d => (JsonNode)d).ToArray()),
                ["uniqueId"] = fileMeta.Id is not null && game.ModMarker == "manifest" ? fileMeta.Id : null,
                ["requires"] = new JsonArray(),
                ["requestedBy"] = primary is not null && metaId is not null ? metaId : meta.Str("requestedBy"),
                ["enabled"] = true,
                ["missing"] = false,
            };
            var saved = registry.Add(record);
            primary ??= saved;
        }
        InstallParts(registry, archivePath, parts, primary!);
        return primary!;
    }

    /// <summary>
    /// Патчеры — в BepInEx/patchers/&lt;мод&gt; отдельной записью «мод#patchers» (выключается и удаляется вместе с модом),
    /// настройки — в BepInEx/config без замены уже настроенного, core — никогда.
    /// </summary>
    static void InstallParts(ModRegistry registry, string archivePath, List<BepInExLayout.Part> parts, JsonObject primary)
    {
        var id = primary.Str("id")!;
        var name = primary.Str("name") ?? id;
        foreach (var part in parts)
        {
            if (part.Kind == "config") { BepInExLayout.ApplyConfig(archivePath, part, registry.GamePath, overwrite: false); continue; }
            if (!BepInExLayout.IsRouted(part.Kind)) continue;
            var folder = Sanitize(primary.Str("folder") ?? name);
            var destination = Path.Combine(registry.GamePath, "BepInEx", part.Kind, folder);
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            var files = Archive.Extract(archivePath, destination, part.Prefix, ignoreCase: true);
            registry.Add(new JsonObject
            {
                ["id"] = $"{id}#{part.Kind}",
                ["name"] = $"{name} ({part.Kind})",
                ["version"] = primary.Str("version") ?? "",
                ["author"] = primary.Str("author") ?? "",
                ["source"] = primary.Str("source") ?? "file",
                ["url"] = primary.Str("url"),
                ["icon"] = primary.Str("icon"),
                ["folder"] = folder,
                ["target"] = part.Kind,
                ["fileCount"] = files.Count,
                ["requestedBy"] = id,
                ["enabled"] = true,
                ["missing"] = false,
            });
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
