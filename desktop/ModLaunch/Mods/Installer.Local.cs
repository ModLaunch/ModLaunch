using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Mods;

/// <summary>
/// Моды с диска: архив, папка или .dll (перетащили в окно или выбрали), моды, положенные
/// в папку руками («взять в список»), и узнавание таких модов — чтобы у них были обновления.
/// </summary>
public static partial class Installer
{
    [GeneratedRegex(@"^[A-Za-z0-9_]+-[A-Za-z0-9_]+$")]
    private static partial Regex OwnerName();

    /// <summary>Это сам загрузчик (BepInExPack), а не мод.</summary>
    public static bool IsLoader(Games.GameDef game, string id) => IsLoaderPackage(game, id);

    /// <summary>Можно ли поставить это: архив, папку или .dll.</summary>
    public static bool CanInstallLocal(string path) =>
        Directory.Exists(path) || (File.Exists(path) && (Archive.IsArchive(path) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Поставить с диска. Папку и .dll упаковываем во временный zip — дальше всё как с архивом
    /// (поиск корня мода, раскладка BepInEx). Потом пробуем узнать мод.
    /// </summary>
    public static async Task<JsonObject> InstallLocal(ModRegistry registry, string path, IProgress<InstallStep> progress, CancellationToken ct)
    {
        var name = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
        var meta = new JsonObject { ["source"] = "file", ["name"] = name };
        progress.Report(new InstallStep("install.extract", name));
        JsonObject record;
        if (Archive.IsArchive(path)) record = await InstallAny(registry, path, meta, progress, ct);
        else
        {
            // Одна .dll годится только играм, где мод — это .dll (BepInEx, MelonLoader…), не SMAPI.
            if (!Directory.Exists(path) && registry.Game.ModMarker == "manifest") throw new InvalidOperationException(I18n.T("local.needManifest"));
            var dir = Directory.CreateDirectory(Path.Combine(Paths.DownloadsTemp, "local-" + DateTime.UtcNow.Ticks)).FullName;
            try
            {
                var zip = Path.Combine(dir, name + ".zip");
                if (Directory.Exists(path)) ZipFile.CreateFromDirectory(path, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
                else
                {
                    using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
                    archive.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Fastest);
                }
                record = InstallArchive(registry, zip, meta);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
        var id = record.Str("id")!;
        try { if (await Identify(registry, id, ct) is { } known) record = known; } catch { }
        return record;
    }

    /// <summary>Взять в список мод, который положили в папку модов руками.</summary>
    public static JsonObject Adopt(ModRegistry registry, string folder)
    {
        // Уже в списке (кнопку нажали дважды) — вторая запись на ту же папку при удалении унесла бы мод целиком.
        if (registry.List().FirstOrDefault(m => m.Bool("enabled", true) && m.Str("kind") != "preset" && m.Str("target") is null
                && string.Equals(m.Str("folder"), folder, StringComparison.OrdinalIgnoreCase)) is { } known) return known;
        var dir = Path.Combine(registry.ModsDir, folder);
        var manifestPath = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "manifest.json", SearchOption.AllDirectories).FirstOrDefault() : null;
        var manifest = manifestPath is null ? new Manifest(null, null, null, null, []) : ReadManifest(registry.Game, File.ReadAllText(manifestPath), folder);
        var id = manifest.Id ?? folder;
        if (registry.Has(id)) id = folder;
        if (registry.Has(id)) id = $"{folder}#{DateTime.UtcNow.Ticks}";
        return registry.Add(new JsonObject
        {
            ["id"] = id,
            ["name"] = manifest.Name ?? folder,
            ["version"] = manifest.Version ?? "",
            ["author"] = manifest.Author ?? "",
            ["source"] = "file",
            ["folder"] = folder,
            ["fileCount"] = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0,
            ["dependencies"] = new JsonArray(manifest.Dependencies.Select(d => (JsonNode)d).ToArray()),
            ["uniqueId"] = manifest.Id is not null && registry.Game.ModMarker == "manifest" ? manifest.Id : null,
            ["requires"] = new JsonArray(),
            ["enabled"] = true,
            ["missing"] = false,
        });
    }

    /// <summary>
    /// Узнать мод, поставленный из файла, чтобы у него были обновления: у SMAPI — по UpdateKeys
    /// в manifest («Nexus:1234»), у Thunderstore — по папке вида Автор-Название (как у r2modman).
    /// Возвращает обновлённую запись или null.
    /// </summary>
    public static async Task<JsonObject?> Identify(ModRegistry registry, string id, CancellationToken ct = default)
    {
        if (registry.Get(id) is not { } record || record.Str("source") != "file" || registry.OwnFolder(record) is not { } folder) return null;
        var game = registry.Game;
        var manifest = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "manifest.json", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (manifest is not null && game.NexusDomain is not null && NexusKey(File.ReadAllText(manifest)) is { } nexusId)
            return registry.Rekey(id, game.RecordId(nexusId, "nexus"), m =>
            {
                m["source"] = "nexus";
                m["url"] = $"https://www.nexusmods.com/{game.NexusDomain}/mods/{nexusId}";
            });
        if (game.ThunderstoreCommunity is { } community && !Program.Demo)
        {
            var name = record.Str("folder") ?? "";
            if (!OwnerName().IsMatch(name)) return null;
            var mod = await Thunderstore.Get(community, name, ct);
            if (mod is null) return null;
            return registry.Rekey(id, mod.Id, m =>
            {
                m["source"] = "thunderstore";
                m["url"] = mod.Url;
                m["icon"] = mod.Icon;
                if (m.Str("author") is null or "") m["author"] = mod.Author;
            });
        }
        return null;
    }

    /// <summary>Номер мода на Nexus из UpdateKeys манифеста SMAPI: «Nexus:1234» или «Nexus:1234@часть».</summary>
    public static string? NexusKey(string manifestText)
    {
        try
        {
            var node = JsonNode.Parse(manifestText.TrimStart('﻿'), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var key in node.Arr("UpdateKeys").Select(k => k?.ToString()).OfType<string>())
                if (Regex.Match(key.Trim(), @"^nexus:\s*(\d+)", RegexOptions.IgnoreCase) is { Success: true } m) return m.Groups[1].Value;
        }
        catch { }
        return null;
    }

    [SelfTest]
    static string LocalModsAreAdoptedAndIdentified()
    {
        if (NexusKey("{ \"Name\": \"X\", // comment\n \"UpdateKeys\": [ \"Chucklefish:5\", \"Nexus:2400@beta\" ], }") != "2400") throw new Exception("UpdateKeys not parsed");
        var game = Path.Combine(Paths.DataDir, "local-test", "Stardew Valley");
        var def = Games.GameCatalog.ById("stardew-valley")!;
        var registry = new ModRegistry(def, game);
        var mod = Directory.CreateDirectory(Path.Combine(registry.ModsDir, "Lookup Anything")).FullName;
        File.WriteAllText(Path.Combine(mod, "manifest.json"), "{\"Name\":\"Lookup Anything\",\"UniqueID\":\"Pathoschild.LookupAnything\",\"Version\":\"1.40.0\",\"UpdateKeys\":[\"Nexus:541\"]}");
        if (!registry.Unmanaged().Contains("Lookup Anything")) throw new Exception("hand-installed folder not noticed");
        var adopted = Adopt(registry, "Lookup Anything");
        if (adopted.Str("id") != "Pathoschild.LookupAnything" || adopted.Str("version") != "1.40.0") throw new Exception("manifest not read on adopt");
        if (Adopt(registry, "Lookup Anything").Str("id") != adopted.Str("id") || registry.List().Count != 1) throw new Exception("second adopt made a duplicate record");
        var known = Identify(registry, adopted.Str("id")!).GetAwaiter().GetResult();
        if (known?.Str("id") != def.RecordId("541", "nexus") || known.Str("source") != "nexus") throw new Exception("not identified by UpdateKeys");
        if (registry.Unmanaged().Count != 0) throw new Exception("adopted folder still unmanaged");
        return $"adopted Lookup Anything → {known.Str("id")}";
    }
}
