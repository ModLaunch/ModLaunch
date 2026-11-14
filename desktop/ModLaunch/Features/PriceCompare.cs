using System.Globalization;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Предложение одного магазина: цена и обычная цена в долларах США, скидка в процентах, ссылка на покупку.</summary>
public sealed record StoreDeal(string StoreId, string Store, decimal Price, decimal Retail, int Savings, string Url);

/// <summary>Цены игры по магазинам: предложения (дешёвые первыми) и самая низкая цена за всё время.</summary>
public sealed record PriceInfo(int AppId, List<StoreDeal> Deals, decimal? LowestEver, DateTime? LowestEverAt, DateTime Fetched)
{
    public StoreDeal? Cheapest => Deals.FirstOrDefault();
}

/// <summary>
/// 29.0: сравнение цен по магазинам — Steam, GOG, Humble, Fanatical, GreenManGaming, GamesPlanet и другие —
/// по данным CheapShark (бесплатно, без ключа, по номеру игры в Steam). Цены у CheapShark в долларах США; цена
/// Steam для своей страны — в «Желаемом» (Wishlist). Ответы кэшируются на шесть часов (cache/prices.json).
/// </summary>
public static class PriceCompare
{
    const string Api = "https://www.cheapshark.com/api/1.0";
    static readonly TimeSpan Fresh = TimeSpan.FromHours(6);
    static readonly JsonFile Cache = new(Path.Combine(Paths.CacheDir, "prices.json"), () => new JsonObject());
    static readonly object Gate = new();
    static Dictionary<string, string>? _stores;

    public static event Action<int>? Changed;

    /// <summary>Уже известное (из кэша, даже старое) — без сети.</summary>
    public static PriceInfo? Known(int appId)
    {
        if (Program.Demo || Program.Screenshot) return Demo(appId);
        lock (Gate) return Cache.Data[appId.ToString(CultureInfo.InvariantCulture)] is JsonObject o ? FromCache(appId, o) : null;
    }

    /// <summary>Цены игры (из кэша, если свежие). null — CheapShark этой игры не знает.</summary>
    public static async Task<PriceInfo?> For(int appId, bool force = false, CancellationToken ct = default)
    {
        if (Program.Demo || Program.Screenshot) return Demo(appId);
        if (!force && Known(appId) is { } known && DateTime.UtcNow - known.Fetched < Fresh) return known;
        var stores = await Stores(ct);
        var found = await Http.GetJson($"{Api}/games?steamAppID={appId}", ct, 20) as JsonArray;
        var gameId = found?.FirstOrDefault()?.Str("gameID");
        PriceInfo? info = null;
        JsonObject record;
        if (gameId is null) record = new JsonObject { ["none"] = true, ["at"] = DateTime.UtcNow.ToString("o") };
        else
        {
            var game = await Http.GetJson($"{Api}/games?id={Uri.EscapeDataString(gameId)}", ct, 20);
            info = Parse(appId, game, stores, DateTime.UtcNow);
            record = ToCache(info);
        }
        lock (Gate) Cache.Data[appId.ToString(CultureInfo.InvariantCulture)] = record;
        Cache.Save();
        Changed?.Invoke(appId);
        return info;
    }

    /// <summary>Магазины CheapShark: номер → название (только работающие). Кэш — в памяти до перезапуска.</summary>
    static async Task<Dictionary<string, string>> Stores(CancellationToken ct)
    {
        if (_stores is { } s) return s;
        var list = await Http.GetJson($"{Api}/stores", ct, 20) as JsonArray ?? [];
        _stores = list.OfType<JsonObject>().Where(o => o.Long("isActive") == 1 && o.Str("storeID") is not null)
            .ToDictionary(o => o.Str("storeID")!, o => o.Str("storeName") ?? "?");
        return _stores;
    }

    static decimal? Money(JsonNode? node, string key) =>
        decimal.TryParse(node.Str(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Разобрать ответ games?id=…: предложения работающих магазинов, дешёвые первыми, по одному на магазин.</summary>
    internal static PriceInfo Parse(int appId, JsonNode? game, IReadOnlyDictionary<string, string> stores, DateTime now)
    {
        var deals = game.Arr("deals").OfType<JsonObject>()
            .Where(d => d.Str("storeID") is { } id && stores.ContainsKey(id) && Money(d, "price") is not null && d.Str("dealID") is { Length: > 0 })
            .Select(d =>
            {
                var price = Money(d, "price")!.Value;
                var retail = Money(d, "retailPrice") ?? price;
                var savings = retail > 0 && price < retail ? (int)Math.Round((1 - price / retail) * 100) : 0;
                // dealID у CheapShark уже закодирован для адреса (%2F…) — повторно не кодируем.
                return new StoreDeal(d.Str("storeID")!, stores[d.Str("storeID")!], price, retail, savings, $"https://www.cheapshark.com/redirect?dealID={d.Str("dealID")}");
            })
            .GroupBy(d => d.StoreId).Select(g => g.OrderBy(d => d.Price).First())
            .OrderBy(d => d.Price).ThenBy(d => d.Store, StringComparer.OrdinalIgnoreCase).ToList();
        var ever = game?["cheapestPriceEver"];
        DateTime? at = ever is not null && ever.Long("date") > 0 ? DateTimeOffset.FromUnixTimeSeconds(ever.Long("date")).UtcDateTime : null;
        return new PriceInfo(appId, deals, Money(ever, "price"), at, now);
    }

    static JsonObject ToCache(PriceInfo p) => new()
    {
        ["at"] = p.Fetched.ToString("o"),
        ["lowest"] = p.LowestEver?.ToString(CultureInfo.InvariantCulture),
        ["lowestAt"] = p.LowestEverAt?.ToString("o"),
        ["deals"] = new JsonArray(p.Deals.Select(d => (JsonNode)new JsonObject
        {
            ["id"] = d.StoreId, ["store"] = d.Store, ["price"] = d.Price.ToString(CultureInfo.InvariantCulture),
            ["retail"] = d.Retail.ToString(CultureInfo.InvariantCulture), ["savings"] = d.Savings, ["url"] = d.Url,
        }).ToArray()),
    };

    static PriceInfo? FromCache(int appId, JsonObject o)
    {
        if (o.Bool("none")) return null;
        static DateTime? T(string? s) => DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var d) ? d : null;
        var deals = o.Arr("deals").OfType<JsonObject>().Select(d => new StoreDeal(d.Str("id") ?? "", d.Str("store") ?? "?",
            Money(d, "price") ?? 0, Money(d, "retail") ?? 0, (int)d.Long("savings"), d.Str("url") ?? "")).ToList();
        return new PriceInfo(appId, deals, Money(o, "lowest"), T(o.Str("lowestAt")), T(o.Str("at")) ?? DateTime.MinValue);
    }

    /// <summary>Цена в долларах так, как её привыкли видеть: $19.99.</summary>
    public static string Usd(decimal v) => "$" + v.ToString("0.00", CultureInfo.InvariantCulture);

    static PriceInfo? Demo(int appId)
    {
        var now = DateTime.UtcNow;
        StoreDeal D(string id, string store, decimal price, decimal retail) =>
            new(id, store, price, retail, price < retail ? (int)Math.Round((1 - price / retail) * 100) : 0, "https://www.cheapshark.com/");
        return appId switch
        {
            1086940 => new(appId, [D("7", "GOG", 41.99m, 59.99m), D("15", "Fanatical", 44.99m, 59.99m), D("1", "Steam", 47.99m, 59.99m), D("3", "GreenManGaming", 49.79m, 59.99m)], 29.99m, now.AddDays(-160), now),
            1145360 => new(appId, [D("11", "Humble Store", 12.49m, 24.99m), D("1", "Steam", 14.99m, 24.99m), D("7", "GOG", 24.99m, 24.99m)], 6.24m, now.AddDays(-58), now),
            _ => new(appId, [D("1", "Steam", 29.99m, 29.99m)], 19.99m, now.AddDays(-30), now),
        };
    }

    [SelfTest]
    static string PricesAcrossStoresParse()
    {
        var game = JsonNode.Parse("""
            {"info":{"title":"X","steamAppID":"10"},"cheapestPriceEver":{"price":"7.09","date":1577335020},
             "deals":[{"storeID":"7","dealID":"OR%2BVz0QI","price":"39.99","retailPrice":"49.99","savings":"20.0"},
                      {"storeID":"1","dealID":"euqnW%3D","price":"49.99","retailPrice":"49.99","savings":"0"},
                      {"storeID":"1","dealID":"other","price":"44.99","retailPrice":"49.99","savings":"10"},
                      {"storeID":"99","dealID":"gone","price":"1.00","retailPrice":"49.99"},
                      {"storeID":"7","dealID":"","price":"0.99","retailPrice":"49.99"}]}
            """);
        var stores = new Dictionary<string, string> { ["1"] = "Steam", ["7"] = "GOG" };
        var p = Parse(10, game, stores, DateTime.UtcNow);
        if (p.Deals.Count != 2) throw new Exception("deals: " + p.Deals.Count);
        if (p.Cheapest is not { Store: "GOG", Price: 39.99m, Savings: 20 }) throw new Exception("cheapest: " + p.Cheapest);
        if (p.Deals[1] is not { Store: "Steam", Price: 44.99m }) throw new Exception("one per store, cheapest kept: " + p.Deals[1]);
        if (!p.Cheapest.Url.EndsWith("dealID=OR%2BVz0QI")) throw new Exception("deal id re-encoded: " + p.Cheapest.Url);
        if (p.LowestEver != 7.09m || p.LowestEverAt?.Year != 2019) throw new Exception("lowest ever");
        var back = FromCache(10, ToCache(p))!;
        if (back.Deals.Count != 2 || back.Cheapest!.Price != 39.99m || back.LowestEver != 7.09m) throw new Exception("cache round trip");
        if (Usd(5m) != "$5.00") throw new Exception("usd");
        return "inactive stores and empty deals dropped, one deal per store, cheapest first, cache round trip";
    }
}
