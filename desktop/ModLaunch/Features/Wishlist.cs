using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Features;

/// <summary>Цена в магазине Steam: суммы в сотых долях валюты и готовый текст, как его показывает Steam.</summary>
public sealed record WishPrice(string Currency, long Initial, long Final, int Discount, string InitialText, string FinalText)
{
    public bool OnSale => Discount > 0 && Final < Initial;
}

/// <summary>
/// Игра в списке желаемого. Price — последняя известная цена (null — бесплатна, не продаётся в этой стране или ещё
/// не проверяли), Lowest — самая низкая из увиденных, Target — «скажи, когда будет дешевле» (в сотых), Notified —
/// о какой скидке уже сказали, чтобы не повторять одно и то же каждые шесть часов.
/// </summary>
public sealed record WishItem(int AppId, string Name, DateTime Added, WishPrice? Price, string State, DateTime? Checked, long? Lowest, long? Target, int Notified, bool Seen);

/// <summary>
/// 28.0: список желаемого и скидки. Цены — из магазина Steam (appdetails, до сотни игр одним запросом, без ключа),
/// для страны из настроек. Раз в шесть часов, пока ModLaunch открыт, цены проверяются сами; новая скидка или цена
/// ниже заданной — уведомление в колокольчике и всплывающая подсказка. Своё желаемое можно забрать из Steam.
/// </summary>
public static class Wishlist
{
    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "wishlist.json"), () => new JsonObject { ["items"] = new JsonArray() });
    static readonly object Gate = new();
    static readonly TimeSpan Every = TimeSpan.FromHours(6);
    static Timer? _timer;
    static Task<int>? _checking;

    public static event Action? Changed;

    /// <summary>Страны, для которых Steam показывает цены; по умолчанию — по языку программы.</summary>
    public static readonly string[] Countries = ["ru", "ua", "kz", "us", "gb", "de", "fr", "es", "br", "cn", "tr", "pl"];

    public static string Country
    {
        get => Settings.Data.Str("wishCountry") is { Length: 2 } c ? c : I18n.Lang switch
        {
            "ru" => "ru", "uk" => "ua", "de" => "de", "fr" => "fr", "es" => "es", "pt-BR" => "br", "zh-CN" => "cn", _ => "us",
        };
        set
        {
            if (value == Country) return;
            Settings.Data["wishCountry"] = value;
            Settings.Save();
            // Другая валюта: старые цены и «самая низкая» больше не сравнимы.
            lock (Gate)
                foreach (var o in Items_())
                {
                    o.Remove("price"); o.Remove("lowest"); o.Remove("checked"); o["notified"] = 0;
                    o["state"] = "unknown";
                }
            Save();
            _ = Check(force: true);
        }
    }

    static JsonArray List_()
    {
        if (File.Data["items"] is not JsonArray a) File.Data["items"] = a = new JsonArray();
        return a;
    }

    static IEnumerable<JsonObject> Items_() => List_().OfType<JsonObject>();

    public static IReadOnlyList<WishItem> Items
    {
        get
        {
            if (Program.Demo || Program.Screenshot) return Demo();
            lock (Gate) return Items_().Select(Read).ToList();
        }
    }

    public static bool Has(int appId) { lock (Gate) return Items_().Any(o => o.Long("app") == appId); }

    public static int Count { get { lock (Gate) return Items_().Count(); } }

    /// <summary>Скидки, о которых человек ещё не знает (для колокольчика).</summary>
    public static List<WishItem> Fresh => Items.Where(i => !i.Seen).ToList();

    static WishItem Read(JsonObject o)
    {
        WishPrice? price = o["price"] is JsonObject p
            ? new WishPrice(p.Str("currency") ?? "", p.Long("initial"), p.Long("final"), (int)p.Long("discount"), p.Str("initialText") ?? "", p.Str("finalText") ?? "")
            : null;
        return new WishItem((int)o.Long("app"), o.Str("name") ?? "", DateTime.TryParse(o.Str("added"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var a) ? a : DateTime.UtcNow,
            price, o.Str("state") ?? "unknown", DateTime.TryParse(o.Str("checked"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var c) ? c : null,
            o["lowest"] is null ? null : o.Long("lowest"), o["target"] is null ? null : o.Long("target"), (int)o.Long("notified"), o.Bool("seen", true));
    }

    static void Save()
    {
        File.Save();
        Changed?.Invoke();
    }

    public static void Add(int appId, string name)
    {
        if (appId <= 0) return;
        lock (Gate)
        {
            if (Items_().Any(o => o.Long("app") == appId)) return;
            List_().Add(new JsonObject { ["app"] = appId, ["name"] = name, ["added"] = DateTime.UtcNow.ToString("o"), ["state"] = "unknown", ["notified"] = 0, ["seen"] = true });
        }
        Save();
        _ = Check(only: [appId]);
    }

    public static void Remove(int appId)
    {
        lock (Gate)
        {
            var arr = List_();
            foreach (var o in arr.OfType<JsonObject>().Where(o => o.Long("app") == appId).ToList()) arr.Remove(o);
        }
        Save();
    }

    /// <summary>«Скажите, когда будет дешевле»: цена в сотых долях валюты; null — убрать.</summary>
    public static void SetTarget(int appId, long? cents)
    {
        lock (Gate)
            foreach (var o in Items_().Where(o => o.Long("app") == appId))
            {
                if (cents is > 0) o["target"] = cents; else o.Remove("target");
                o["seen"] = true;
            }
        Save();
    }

    public static void MarkSeen()
    {
        var any = false;
        lock (Gate)
            foreach (var o in Items_().Where(o => !o.Bool("seen", true))) { o["seen"] = true; any = true; }
        if (any) Save();
    }

    // ---------------------------------------------------------------- цены

    /// <summary>Разобрать ответ appdetails: appid → цена; бесплатная игра — «free», нет в этой стране — «unavailable».</summary>
    internal static Dictionary<int, (string State, WishPrice? Price)> ParsePrices(JsonNode? json)
    {
        var result = new Dictionary<int, (string, WishPrice?)>();
        if (json is not JsonObject root) return result;
        foreach (var (key, node) in root)
        {
            if (!int.TryParse(key, out var app)) continue;
            if (node is not JsonObject entry || !entry.Bool("success")) { result[app] = ("unavailable", null); continue; }
            if (entry["data"] is not JsonObject data || data["price_overview"] is not JsonObject p) { result[app] = ("free", null); continue; }
            var initial = p.Long("initial");
            var final = p.Long("final");
            if (initial <= 0) initial = final;
            result[app] = ("price", new WishPrice(p.Str("currency") ?? "", initial, final, (int)p.Long("discount_percent"),
                p.Str("initial_formatted") is { Length: > 0 } it ? it : p.Str("final_formatted") ?? "", p.Str("final_formatted") ?? ""));
        }
        return result;
    }

    /// <summary>
    /// Стоит ли сказать о цене: новая скидка, скидка глубже той, о которой уже говорили, или цена дошла до заданной.
    /// Возвращает новое значение «о какой скидке сказали» (0 — скидки нет, забыть) и надо ли уведомлять.
    /// </summary>
    internal static (int Notified, bool Tell) Decide(int notifiedBefore, long? target, long? finalBefore, WishPrice now)
    {
        var targetHit = target is long t && now.Final <= t && (finalBefore is not long f || f > t);
        if (!now.OnSale) return (0, targetHit);
        if (now.Discount > notifiedBefore) return (now.Discount, true);
        return (notifiedBefore, targetHit);
    }

    /// <summary>Проверить цены (всех или только перечисленных). Возвращает, о скольких скидках сказали.</summary>
    public static Task<int> Check(bool force = false, int[]? only = null)
    {
        if (Program.Demo || Program.Screenshot) return Task.FromResult(0);
        if (only is null && _checking is { IsCompleted: false } running) return running;
        var task = Task.Run(() => CheckNow(force, only));
        if (only is null) _checking = task;
        return task;
    }

    static async Task<int> CheckNow(bool force, int[]? only)
    {
        List<int> apps;
        lock (Gate)
            apps = Items_()
                .Where(o => only?.Contains((int)o.Long("app")) ?? (force || !DateTime.TryParse(o.Str("checked"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var c) || DateTime.UtcNow - c > Every - TimeSpan.FromMinutes(5)))
                .Select(o => (int)o.Long("app")).Distinct().ToList();
        if (apps.Count == 0) return 0;
        var cc = Country;
        var told = new List<(WishItem Item, bool Target)>();
        foreach (var chunk in apps.Chunk(90))
        {
            var json = await Http.GetJson($"https://store.steampowered.com/api/appdetails?appids={string.Join(',', chunk)}&filters=price_overview&cc={cc}", default, 20);
            var prices = ParsePrices(json);
            lock (Gate)
                foreach (var o in Items_())
                {
                    var app = (int)o.Long("app");
                    if (!prices.TryGetValue(app, out var p)) continue;
                    o["checked"] = DateTime.UtcNow.ToString("o");
                    o["state"] = p.State;
                    if (p.Price is not { } now) { o.Remove("price"); o["notified"] = 0; continue; }
                    long? before = o["price"] is JsonObject old ? old.Long("final") : null;
                    long? target = o["target"] is null ? null : o.Long("target");
                    var (notified, tell) = Decide((int)o.Long("notified"), target, before, now);
                    o["price"] = new JsonObject
                    {
                        ["currency"] = now.Currency, ["initial"] = now.Initial, ["final"] = now.Final, ["discount"] = now.Discount,
                        ["initialText"] = now.InitialText, ["finalText"] = now.FinalText,
                    };
                    if (o["lowest"] is null || o.Long("lowest") > now.Final) o["lowest"] = now.Final;
                    o["notified"] = notified;
                    if (tell)
                    {
                        o["seen"] = false;
                        told.Add((Read(o), target is long t && now.Final <= t));
                    }
                }
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Save();
            if (told.Count == 1)
            {
                var (item, target) = told[0];
                Views.MainWindow.Current?.Toast(target
                    ? I18n.T("v28.wish.toast.target", ("name", item.Name), ("price", item.Price!.FinalText))
                    : I18n.T("v28.wish.toast.sale", ("name", item.Name), ("pct", item.Price!.Discount), ("price", item.Price.FinalText)));
            }
            else if (told.Count > 1) Views.MainWindow.Current?.Toast(I18n.T("v28.wish.toast.many", ("n", told.Count)));
        });
        return told.Count;
    }

    /// <summary>Проверять цены раз в шесть часов, пока программа открыта (первый раз — через пару минут после запуска).</summary>
    public static void StartWatch()
    {
        if (_timer is not null || Program.Demo || Program.Screenshot) return;
        _timer = new Timer(_ => { if (Count > 0) _ = Check().ContinueWith(t => _ = t.Exception); }, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(30));
    }

    // ---------------------------------------------------------------- желаемое из Steam

    /// <summary>SteamID64 последнего вошедшего в Steam на этом ПК (папка userdata/&lt;id&gt; с самым свежим localconfig.vdf).</summary>
    internal static string? SteamId64()
    {
        var dir = Locator.SteamRoots()
            .SelectMany(r => { try { return Directory.EnumerateDirectories(Path.Combine(r, "userdata")); } catch { return []; } })
            .Where(d => System.IO.File.Exists(Path.Combine(d, "config", "localconfig.vdf")))
            .OrderByDescending(d => { try { return System.IO.File.GetLastWriteTimeUtc(Path.Combine(d, "config", "localconfig.vdf")); } catch { return DateTime.MinValue; } })
            .FirstOrDefault();
        return dir is not null && ulong.TryParse(Path.GetFileName(dir), out var account) && account > 0 ? (76561197960265728UL + account).ToString() : null;
    }

    /// <summary>
    /// Забрать желаемое из Steam (оно должно быть открыто в настройках приватности Steam). Возвращает, сколько игр
    /// добавилось; NO_STEAM — Steam на этом ПК не входил ни в один аккаунт, PRIVATE — список закрыт или пуст.
    /// </summary>
    public static async Task<int> ImportSteam()
    {
        var id = SteamId64() ?? throw new ServiceError("NO_STEAM");
        var json = await Http.GetJson($"https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid={id}", default, 20);
        var apps = (json?["response"]).Arr("items").Select(i => (int)i.Long("appid")).Where(a => a > 0).ToList();
        if (apps.Count == 0) throw new ServiceError("PRIVATE");
        await SteamCatalog.EnsureLoaded();
        var added = new List<int>();
        lock (Gate)
        {
            var known = Items_().Select(o => (int)o.Long("app")).ToHashSet();
            foreach (var app in apps.Where(a => !known.Contains(a)))
            {
                List_().Add(new JsonObject
                {
                    ["app"] = app, ["name"] = SteamCatalog.Get(app)?.Name ?? $"Steam {app}", ["added"] = DateTime.UtcNow.ToString("o"),
                    ["state"] = "unknown", ["notified"] = 0, ["seen"] = true, ["from"] = "steam",
                });
                added.Add(app);
            }
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(Save);
        if (added.Count > 0) _ = Check(only: added.ToArray());
        return added.Count;
    }

    // ---------------------------------------------------------------- демо и проверки

    static List<WishItem> Demo()
    {
        var now = DateTime.UtcNow;
        WishPrice P(long initial, long final, int pct) => new("RUB", initial, final, pct, $"{initial / 100:N0} ₽", $"{final / 100:N0} ₽");
        return
        [
            new(1145360, "Hades II", now.AddDays(-20), P(116_000, 69_600, 40), "price", now.AddHours(-1), 69_600, null, 40, false),
            new(2246340, "Monster Hunter Wilds", now.AddDays(-9), P(499_900, 499_900, 0), "price", now.AddHours(-1), 449_900, 400_000, 0, true),
            new(1086940, "Baldur's Gate 3", now.AddDays(-60), P(199_900, 99_900, 50), "price", now.AddHours(-1), 99_900, 120_000, 50, false),
            new(553850, "HELLDIVERS 2", now.AddDays(-3), P(259_900, 259_900, 0), "price", now.AddHours(-1), 207_900, null, 0, true),
            new(1030300, "Hollow Knight: Silksong", now.AddDays(-1), null, "unknown", null, null, null, 0, true),
        ];
    }

    [SelfTest]
    static string WishlistReadsPricesAndDecides()
    {
        var json = JsonNode.Parse("""
            {"10":{"success":true,"data":{"price_overview":{"currency":"RUB","initial":99900,"final":49900,"discount_percent":50,"initial_formatted":"999 руб.","final_formatted":"499 руб."}}},
             "20":{"success":true,"data":[]},
             "30":{"success":false},
             "40":{"success":true,"data":{"price_overview":{"currency":"USD","initial":0,"final":1999,"discount_percent":0,"initial_formatted":"","final_formatted":"$19.99"}}}}
            """);
        var p = ParsePrices(json);
        if (p[10] is not ("price", { OnSale: true, Final: 49900, Discount: 50, InitialText: "999 руб." })) throw new Exception("sale: " + p[10]);
        if (p[20].State != "free" || p[30].State != "unavailable") throw new Exception($"free/unavailable: {p[20]} {p[30]}");
        if (p[40] is not ("price", { OnSale: false, Initial: 1999, InitialText: "$19.99" })) throw new Exception("full price: " + p[40]);

        var sale50 = p[10].Price!;
        if (Decide(0, null, 99900, sale50) != (50, true)) throw new Exception("new sale not told");
        if (Decide(50, null, 49900, sale50) != (50, false)) throw new Exception("same sale told twice");
        if (Decide(30, null, 69900, sale50) != (50, true)) throw new Exception("deeper sale not told");
        if (Decide(50, null, 49900, p[40].Price!) != (0, false)) throw new Exception("sale end should reset");
        if (Decide(0, 50000, 99900, sale50) != (50, true)) throw new Exception("target + sale");
        var full = new WishPrice("RUB", 60000, 45000, 0, "", "");
        if (Decide(0, 50000, 60000, full) != (0, true)) throw new Exception("target hit without sale not told");
        if (Decide(0, 50000, 45000, full) != (0, false)) throw new Exception("target told twice");
        return "appdetails parsed (sale, free, unavailable, full price); new/deeper sale and target price told once";
    }
}
