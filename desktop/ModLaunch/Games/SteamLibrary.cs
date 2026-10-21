using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>
/// 12.0: все игры Steam — и те, для которых модов нет. Установленные читаются из манифестов (кеш на минуту),
/// играть и ставить — через клиент Steam (steam://), поиск по всему магазину — открытым поиском Steam без ключа.
/// </summary>
public static class SteamLibrary
{
    static List<SteamApp>? _installed;
    static DateTime _at;
    static readonly object Gate = new();

    /// <summary>Пример для снимков и демо: на машине сборки Steam нет.</summary>
    static readonly SteamApp[] DemoApps =
    [
        new(1245620, "ELDEN RING", "ELDEN RING", "", 1_760_000_000, 60_000_000_000),
        new(1086940, "Baldur's Gate 3", "Baldurs Gate 3", "", 1_759_000_000, 150_000_000_000),
        new(105600, "Terraria", "Terraria", "", 1_758_000_000, 500_000_000),
        new(1145350, "Hades II", "Hades II", "", 1_757_000_000, 12_000_000_000),
        new(504230, "Celeste", "Celeste", "", 1_740_000_000, 1_200_000_000),
        new(570, "Dota 2", "dota 2 beta", "", 1_700_000_000, 40_000_000_000),
        new(1091500, "Cyberpunk 2077", "Cyberpunk 2077", "", 1_690_000_000, 70_000_000_000),
        new(413150, "Stardew Valley", "Stardew Valley", "", 1_761_000_000, 700_000_000),
    ];

    /// <summary>Установленные игры Steam без инструментов, Proton и демо.</summary>
    public static IReadOnlyList<SteamApp> Installed
    {
        get
        {
            lock (Gate)
            {
                if (_installed is null || DateTime.UtcNow - _at > TimeSpan.FromMinutes(1))
                {
                    _installed = Program.Demo || Program.Screenshot ? [.. DemoApps] : [.. Locator.SteamApps().Where(a => a.AppId > 0 && !CustomGames.IsTool(a.Name))];
                    _at = DateTime.UtcNow;
                }
                return _installed;
            }
        }
    }

    public static IEnumerable<SteamApp> Sorted(string sort) => sort switch
    {
        "name" => Installed.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
        "size" => Installed.OrderByDescending(a => a.SizeOnDisk),
        _ => Installed.OrderByDescending(a => a.LastPlayed).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
    };

    public static SteamApp? ById(int appId) => Installed.FirstOrDefault(a => a.AppId == appId);

    /// <summary>Игра ModLaunch с тем же номером Steam: её «Играть» — с модами и через лаунчер.</summary>
    public static GameState? Managed(int appId) => appId <= 0 ? null : AppState.Games.FirstOrDefault(g => g.Def.SteamAppId == appId && g.Path is not null);

    public static void Play(int appId) => Run($"steam://rungameid/{appId}");
    public static void Install(int appId) => Run($"steam://install/{appId}");
    public static void StorePage(int appId) => Run($"https://store.steampowered.com/app/{appId}/");

    static void Run(string uri)
    {
        if (Program.Screenshot) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    public static string Cover(int appId) => $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg";
    public static string Hero(int appId) => $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_hero.jpg";

    /// <summary>Найденная в магазине игра. Price — уже с валютой; null — бесплатно или цены нет.</summary>
    public sealed record StoreHit(int AppId, string Name, string? Price);

    /// <summary>Поиск по всему магазину Steam. Пустой список — нет сети или ничего не нашлось.</summary>
    public static async Task<List<StoreHit>> Search(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || Program.Screenshot) return [];
        var ru = I18n.Lang == "ru";
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(query.Trim())}&l={(ru ? "russian" : "english")}&cc={(ru ? "RU" : "US")}";
        try { return ParseSearch(await Http.GetJson(url, ct, 15)); }
        catch { return []; }
    }

    internal static List<StoreHit> ParseSearch(JsonNode? root)
    {
        var hits = new List<StoreHit>();
        if (root?["items"] is not JsonArray items) return hits;
        foreach (var i in items.OfType<JsonObject>())
        {
            // В выдаче бывают наборы и подписки — нужны только приложения.
            if (i.Str("type") is { } type && type != "app") continue;
            var id = (int)i.Long("id");
            if (id <= 0 || i.Str("name") is not { Length: > 0 } name) continue;
            string? price = null;
            if (i["price"] is JsonObject p && p.Long("final") is var final and > 0 && p.Str("currency") is { } currency)
                price = (final / 100.0).ToString("0.##", CultureInfo.InvariantCulture) + " " + currency;
            hits.Add(new StoreHit(id, name, price));
        }
        return hits;
    }

    [SelfTest]
    static string SteamStoreSearchParses()
    {
        var json = JsonNode.Parse("""
            { "total": 3, "items": [
              { "type": "app", "name": "ELDEN RING", "id": 1245620, "price": { "currency": "USD", "initial": 5999, "final": 5999 } },
              { "type": "sub", "name": "Bundle", "id": 77 },
              { "type": "app", "name": "Dota 2", "id": 570 },
              { "type": "app", "name": "", "id": 5 } ] }
            """);
        var hits = ParseSearch(json);
        if (hits is not [{ AppId: 1245620, Price: "59.99 USD" }, { AppId: 570, Price: null }]) throw new Exception(string.Join("; ", hits));
        if (ParseSearch(null).Count != 0 || ParseSearch(JsonNode.Parse("{}")).Count != 0) throw new Exception("empty input");
        return "apps kept with price, bundles and nameless dropped";
    }
}
