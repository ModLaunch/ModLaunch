using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Minecraft;

/// <summary>Версия Minecraft из манифеста Mojang.</summary>
public sealed record McVersion(string Id, string Type, DateTime Released)
{
    public bool Release => Type == "release";
}

/// <summary>
/// Версии игры и загрузчиков: манифест Mojang, мета Fabric и Quilt, промо Forge, Maven NeoForge.
/// Всё кэшируется на диске на несколько часов — список открывается сразу и без сети.
/// </summary>
public static class McMeta
{
    public const string Manifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string FabricMeta = "https://meta.fabricmc.net/v2";
    public const string QuiltMeta = "https://meta.quiltmc.org/v3";
    public const string ForgePromos = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    public const string ForgeMaven = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    public const string NeoVersions = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";
    public const string NeoMaven = "https://maven.neoforged.net/releases/net/neoforged/neoforge";

    static string CacheFile(string key) => Path.Combine(Paths.CacheDir, "minecraft", key + ".json");

    /// <summary>JSON с кэшем: свежий — из файла, иначе из сети; нет сети — старый из файла.</summary>
    static async Task<JsonNode?> Cached(string key, string url, TimeSpan fresh, CancellationToken ct)
    {
        var file = CacheFile(key);
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < fresh)
                return JsonNode.Parse(await File.ReadAllTextAsync(file, ct));
        }
        catch { }
        if (Program.Demo) return Demo(key);
        try
        {
            var json = await Http.GetJson(url, ct);
            if (json is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, json.ToJsonString(), ct);
            }
            return json;
        }
        catch when (File.Exists(file))
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(file, ct));
        }
    }

    // ---------------------------------------------------------------- Minecraft

    public static async Task<List<McVersion>> GameVersions(bool snapshots = false, CancellationToken ct = default)
    {
        var json = await Cached("manifest", Manifest, TimeSpan.FromHours(6), ct);
        return (json?["versions"] as JsonArray ?? [])
            .OfType<JsonNode>()
            .Select(v => new McVersion(v.Str("id") ?? "", v.Str("type") ?? "", DateTime.TryParse(v.Str("releaseTime"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : DateTime.MinValue))
            .Where(v => v.Id != "" && (v.Release || (snapshots && v.Type == "snapshot")))
            .ToList();
    }

    /// <summary>Самая новая вышедшая версия (релиз).</summary>
    public static async Task<string?> LatestRelease(CancellationToken ct = default)
    {
        var json = await Cached("manifest", Manifest, TimeSpan.FromHours(6), ct);
        return json?["latest"].Str("release") ?? (await GameVersions(false, ct)).FirstOrDefault()?.Id;
    }

    // ---------------------------------------------------------------- загрузчики

    /// <summary>Версии загрузчика для версии игры: свежие сверху, первая стабильная — по умолчанию.</summary>
    public static async Task<List<(string Version, bool Stable)>> LoaderVersions(string loader, string gameVersion, CancellationToken ct = default)
    {
        switch (loader)
        {
            case "fabric":
            {
                var json = await Cached($"fabric-{gameVersion}", $"{FabricMeta}/versions/loader/{Uri.EscapeDataString(gameVersion)}", TimeSpan.FromHours(6), ct);
                return (json as JsonArray ?? []).OfType<JsonNode>()
                    .Select(x => (x["loader"].Str("version") ?? "", x["loader"].Bool("stable", true)))
                    .Where(x => x.Item1 != "").ToList();
            }
            case "quilt":
            {
                var json = await Cached($"quilt-{gameVersion}", $"{QuiltMeta}/versions/loader/{Uri.EscapeDataString(gameVersion)}", TimeSpan.FromHours(6), ct);
                return (json as JsonArray ?? []).OfType<JsonNode>()
                    .Select(x => x["loader"].Str("version") ?? "")
                    .Where(v => v != "")
                    .Select(v => (v, !v.Contains("beta", StringComparison.OrdinalIgnoreCase) && !v.Contains("pre", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            case "forge":
            {
                var json = await Cached("forge-promos", ForgePromos, TimeSpan.FromHours(6), ct);
                var promos = json?["promos"] as JsonObject ?? [];
                var list = new List<(string, bool)>();
                if (promos.Str($"{gameVersion}-recommended") is { } rec) list.Add((rec, true));
                if (promos.Str($"{gameVersion}-latest") is { } latest && latest != promos.Str($"{gameVersion}-recommended")) list.Insert(0, (latest, list.Count == 0));
                return list;
            }
            case "neoforge":
            {
                var json = await Cached("neoforge", NeoVersions, TimeSpan.FromHours(6), ct);
                return (json?["versions"] as JsonArray ?? []).OfType<JsonNode>()
                    .Select(x => x?.ToString() ?? "")
                    .Where(v => NeoGameVersion(v) == gameVersion)
                    .Reverse()
                    .Select(v => (v, !v.Contains("beta", StringComparison.OrdinalIgnoreCase) && !v.Contains("alpha", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            default: return [];
        }
    }

    /// <summary>Какую версию загрузчика ставить: выбранную, иначе первую стабильную, иначе самую свежую.</summary>
    public static async Task<string?> PickLoader(string loader, string gameVersion, string wanted = "", CancellationToken ct = default)
    {
        if (loader == "vanilla") return "";
        var list = await LoaderVersions(loader, gameVersion, ct);
        if (wanted != "" && list.Any(x => x.Version == wanted)) return wanted;
        return list.FirstOrDefault(x => x.Stable).Version ?? list.FirstOrDefault().Version;
    }

    /// <summary>
    /// Версия Minecraft по номеру NeoForge. Старая схема: 21.1.77 → 1.21.1, 20.4.12 → 1.20.4, 21.0.5 → 1.21.
    /// Новая (с 26.1): 26.1.0.5-beta → 26.1, 26.1.2.3 → 26.1.2.
    /// </summary>
    public static string NeoGameVersion(string neo)
    {
        var core = neo.Split('-')[0];
        var parts = core.Split('.');
        if (parts.Length >= 4) return parts[2] == "0" ? $"{parts[0]}.{parts[1]}" : $"{parts[0]}.{parts[1]}.{parts[2]}";
        if (parts.Length == 3) return parts[1] == "0" ? $"1.{parts[0]}" : $"1.{parts[0]}.{parts[1]}";
        return "";
    }

    public static string FabricProfile(string gameVersion, string loaderVersion) =>
        $"{FabricMeta}/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";

    public static string QuiltProfile(string gameVersion, string loaderVersion) =>
        $"{QuiltMeta}/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";

    public static string ForgeInstaller(string gameVersion, string forge) => $"{ForgeMaven}/{gameVersion}-{forge}/forge-{gameVersion}-{forge}-installer.jar";
    public static string NeoInstaller(string neo) => $"{NeoMaven}/{neo}/neoforge-{neo}-installer.jar";

    // ---------------------------------------------------------------- без сети (снимки экрана)

    static JsonNode? Demo(string key)
    {
        if (key == "manifest")
        {
            var versions = new JsonArray();
            var date = new DateTime(2026, 9, 1);
            foreach (var id in new[] { "26.3-snapshot-4", "26.2", "26.1.2", "26.1.1", "26.1", "1.21.11", "1.21.10", "1.21.8", "1.21.5", "1.21.4", "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.2", "1.18.2", "1.16.5", "1.12.2" })
            {
                versions.Add(new JsonObject { ["id"] = id, ["type"] = id.Contains("snapshot") ? "snapshot" : "release", ["releaseTime"] = date.ToString("o") });
                date = date.AddDays(-40);
            }
            return new JsonObject { ["latest"] = new JsonObject { ["release"] = "26.2", ["snapshot"] = "26.3-snapshot-4" }, ["versions"] = versions };
        }
        if (key.StartsWith("fabric-") || key.StartsWith("quilt-"))
            return new JsonArray(new JsonObject { ["loader"] = new JsonObject { ["version"] = key.StartsWith("fabric-") ? "0.17.2" : "0.29.1", ["stable"] = true } });
        if (key == "forge-promos")
            return new JsonObject { ["promos"] = new JsonObject { ["1.20.1-recommended"] = "47.4.0", ["1.21.1-latest"] = "52.1.0", ["26.2-latest"] = "62.0.4" } };
        if (key == "neoforge")
            return new JsonObject { ["versions"] = new JsonArray("21.1.200", "26.1.0.12", "26.2.0.7") };
        return null;
    }
}
