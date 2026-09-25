using System.Text.Json;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

public sealed record PackMod(string Id, string Name, string Version, string Source, string? Url, bool Enabled);
public sealed record Pack(string Game, string GameName, string Name, List<PackMod> Mods);

/// <summary>Сборка в файле (.modhub.json) — тот же формат, что у 3.x, файлы совместимы.</summary>
public static class ModPack
{
    const string Format = "modhub-pack";
    const int Version = 1;

    static string Str(JsonNode? n, string key, int max = 200)
    {
        var s = n.Str(key) ?? "";
        return s.Length > max ? s[..max] : s;
    }

    public static string Build(GameDef game, ModRegistry registry, string name)
    {
        var mods = registry.List().Where(r => !r.Bool("missing") && r.Str("requestedBy") is null)
            .Select(r => (JsonNode)new JsonObject
            {
                ["id"] = r.Str("id"),
                ["name"] = r.Str("name"),
                ["version"] = r.Str("version") ?? "",
                ["source"] = r.Str("source") ?? "file",
                ["url"] = r.Str("url"),
                ["enabled"] = r.Bool("enabled", true),
            }).ToArray();
        var pack = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["app"] = Http.Version,
            ["game"] = game.Id,
            ["gameName"] = game.Name,
            ["name"] = string.IsNullOrWhiteSpace(name) ? game.Name : name.Trim(),
            ["createdAt"] = DateTime.UtcNow.ToString("o"),
            ["mods"] = new JsonArray(mods),
        };
        return pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static int Count(string json) => JsonNode.Parse(json).Arr("mods").Count;

    public static Pack Parse(string text)
    {
        JsonNode? data;
        try { data = JsonNode.Parse(text); }
        catch { throw new InvalidDataException(I18n.T("err.PACK_FORMAT")); }
        if (data.Str("format") != Format || data.Str("game") is null || data?["mods"] is not JsonArray list)
            throw new InvalidDataException(I18n.T("err.PACK_FORMAT"));
        if (data.Long("version") > Version) throw new InvalidDataException(I18n.T("err.PACK_NEWER"));
        var mods = list.Take(500)
            .Where(m => m.Str("id") is { Length: > 0 and <= 200 })
            .Select(m => new PackMod(
                m.Str("id")!,
                Str(m, "name") is { Length: > 0 } n ? n : m.Str("id")!,
                Str(m, "version", 40),
                Str(m, "source", 20) is { Length: > 0 } s ? s : "file",
                (m.Str("url") ?? "").StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? Str(m, "url", 500) : null,
                m.Bool("enabled", true)))
            .ToList();
        return new Pack(data.Str("game")!, Str(data, "gameName", 80), Str(data, "name", 60), mods);
    }
}

/// <summary>Сравнение версий модов: 1.10.0 новее 1.9.2, «v» в начале не мешает.</summary>
public static class Versions
{
    static double[]? Parts(string? version)
    {
        var clean = (version ?? "").Trim();
        if (clean.Length > 1 && (clean[0] is 'v' or 'V') && char.IsDigit(clean[1])) clean = clean[1..];
        var main = clean.Split('-', '+', ' ')[0];
        var parts = main.Split('.');
        var result = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out result[i])) return null;
        return result;
    }

    public static int Compare(string? a, string? b)
    {
        var x = Parts(a);
        var y = Parts(b);
        if (x is null || y is null) return (a ?? "").Trim() == (b ?? "").Trim() ? 0 : 1;
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var d = (i < x.Length ? x[i] : 0) - (i < y.Length ? y[i] : 0);
            if (d != 0) return d > 0 ? 1 : -1;
        }
        return 0;
    }

    public static bool IsNewer(string? latest, string? installed) =>
        !string.IsNullOrEmpty(latest) && !string.IsNullOrEmpty(installed) && Compare(latest, installed) > 0;
}
