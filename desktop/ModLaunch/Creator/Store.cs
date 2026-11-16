using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Оплата картой: заказ на сервере и его состояние.</summary>
public sealed record CardPayment(string Id, string Type, string Target, string Title, string Currency, long Amount, string Status,
    string Provider, string? GiftTo, bool Test, DateTime? Created, DateTime? PaidAt);

/// <summary>Чек: продавец (ООО), что куплено, сумма, способ оплаты.</summary>
public sealed record Receipt(string Id, string Item, long Amount, string Currency, string Provider, string ProviderId, string Type, DateTime? Date,
    bool Refunded, string Company, string Unp, string Address, string Email, bool Test);

public sealed record Gift(string Id, string From, string FromName, string Listing, string Title, string Message, DateTime? At);

/// <summary>Заработок разработчика в одной валюте (копейки).</summary>
public sealed record EarningTotal(string Currency, long Gross, long Fee, long Net, long Paid, long Pending)
{
    public long Available => Net - Paid - Pending;
}

public sealed record EarningEntry(string Id, string Type, string Title, string Currency, long Gross, long Fee, long Net, DateTime? At, bool Refunded, bool Gift);

/// <summary>
/// 31.0: магазин с оплатой картой (bePaid или WEBPAY, принимает белорусское ООО). Цену, выдачу покупки, чек, возврат и
/// выплаты делает сервер (server/functions) — программа только просит «купить это» и показывает результат.
/// Сервер не выложен или платёжная система не подключена — «Оплата картой временно недоступна».
/// </summary>
public static class Store
{
    public static readonly string[] Currencies = ["BYN", "RUB", "USD", "EUR"];
    public static readonly string[] Plans = ["month", "year"];

    static string FunctionsUrl =>
        Firebase.Emulator is { } h ? $"http://{h}:5001/{Firebase.ProjectId}/europe-west1"
        : Resources.Json("market.config.json").Str("functionsUrl") is { Length: > 0 } u ? u.TrimEnd('/') : $"https://europe-west1-{Firebase.ProjectId}.cloudfunctions.net";

    // ---------------------------------------------------------------- валюта

    static readonly HashSet<string> Euro = ["AT", "BE", "HR", "CY", "EE", "FI", "FR", "DE", "GR", "IE", "IT", "LV", "LT", "LU", "MT", "NL", "PT", "SK", "SI", "ES"];

    /// <summary>Валюта человека: выбранная им или по стране Windows (Беларусь — BYN, Россия — RUB, еврозона — EUR, иначе USD).</summary>
    public static string Currency
    {
        get
        {
            if (Settings.Data.Str("storeCurrency") is { } c && Currencies.Contains(c)) return c;
            string country;
            try { country = RegionInfo.CurrentRegion.TwoLetterISORegionName; } catch { country = ""; }
            return country == "BY" ? "BYN" : country == "RU" ? "RUB" : Euro.Contains(country) ? "EUR" : "USD";
        }
        set { if (Currencies.Contains(value)) { Settings.Data["storeCurrency"] = value; Settings.Save(); } }
    }

    public static string Symbol(string currency) => currency switch { "BYN" => "Br", "RUB" => "₽", "USD" => "$", "EUR" => "€", _ => currency };

    /// <summary>«29,90 Br», «$9.99», «1 490 ₽».</summary>
    public static string Money(long minor, string currency)
    {
        var value = minor / 100m;
        var text = value.ToString(minor % 100 == 0 ? "#,0" : "#,0.00", I18n.Culture);
        return currency is "USD" ? "$" + text : currency is "EUR" ? text + " €" : $"{text} {Symbol(currency)}";
    }

    /// <summary>Цена лота в валюте со скидкой (как посчитает сервер) или null — в этой валюте не продаётся.</summary>
    public static long? PriceFor(Listing l, string currency, DateTime? now = null)
    {
        if (l.Prices is null || !l.Prices.TryGetValue(currency, out var basePrice) || basePrice <= 0) return null;
        return SaleActive(l, now) ? (long)Math.Round(basePrice * (100 - l.SalePercent) / 100m, MidpointRounding.AwayFromZero) : basePrice;
    }

    public static bool SaleActive(Listing l, DateTime? now = null) => l.SalePercent is > 0 and <= 90 && l.SaleUntil is { } u && u > (now ?? DateTime.UtcNow);

    public static bool CardSale(Listing l) => l.Prices is { Count: > 0 };

    // ---------------------------------------------------------------- вызовы сервера

    /// <summary>Вызов функции сервера (протокол callable): { data } → { result } или { error: { message } }.</summary>
    static async Task<JsonNode?> Call(string name, JsonObject data, CancellationToken ct = default)
    {
        if (!Account.SignedIn) throw new ServiceError("SIGN_IN");
        var (_, token) = await Account.Token();
        Firebase.Reply reply;
        try { reply = await Firebase.Send($"{FunctionsUrl}/{name}", HttpMethod.Post, new JsonObject { ["data"] = data }, token, ct: ct); }
        catch (ServiceError e) when (e.Code == "OFFLINE") { throw; }
        // Сервер ещё не выложен — функции нет: оплата картой временно недоступна.
        if ((int)reply.Status is 404) throw new ServiceError("PAYMENTS_OFF");
        if ((int)reply.Status is >= 200 and < 300) return reply.Body?["result"];
        var code = reply.Body?["error"]?["message"]?.ToString() is { Length: > 0 and <= 40 } m && m.All(ch => char.IsAsciiLetterUpper(ch) || ch == '_') ? m : "SERVER";
        throw new ServiceError(code, reply.Body?["error"]?["message"]?.ToString());
    }

    public sealed record Checkout(string PaymentId, string Url, long Amount, string Currency, bool Test);

    /// <summary>Заказ: игра, подарок другу, премиум или поддержка автора. Возвращает ссылку на страницу оплаты.</summary>
    public static async Task<Checkout> Order(string type, string target, string currency, string? giftTo = null, string? plan = null, long? amount = null, string? message = null)
    {
        var data = new JsonObject { ["type"] = type, ["target"] = target, ["currency"] = currency, ["lang"] = I18n.Lang.Split('-')[0] };
        if (giftTo is not null) data["giftTo"] = giftTo;
        if (plan is not null) data["plan"] = plan;
        if (amount is not null) data["amount"] = amount.Value;
        if (!string.IsNullOrWhiteSpace(message)) data["message"] = Hub.Clip(message, 300, multiline: true);
        var r = await Call("createPayment", data);
        var url = r.Str("redirectUrl") ?? throw new ServiceError("SERVER");
        // Открываем только https-страницы платёжной системы или нашего сервера (на эмуляторе — локальный http).
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Firebase.Emulator is null) throw new ServiceError("SERVER", "insecure payment url");
        return new Checkout(r.Str("pid")!, url, r?["amount"]?.GetValue<long>() ?? 0, r.Str("currency") ?? currency, r?["test"]?.GetValue<bool>() ?? false);
    }

    static CardPayment PaymentOf(JsonObject d)
    {
        var name = d.Str("name") ?? "";
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        return new CardPayment(name[(name.LastIndexOf('/') + 1)..], f.S("type"), f.S("target"), f.S("title"), f.S("currency"), f.L("amount"), f.S("status"),
            f.S("provider"), f.S("giftTo") is { Length: > 0 } g ? g : null, f.B("test"), Firebase.Time(f.S("created")), Firebase.Time(f.S("paidAt")));
    }

    public static async Task<CardPayment?> Payment(string pid)
    {
        var (_, token) = await Account.Token();
        try { return await Hub.Call(Firebase.Url($"/payments/{pid}"), HttpMethod.Get, null, token) is JsonObject d ? PaymentOf(d) : null; }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
    }

    /// <summary>Ждать итога оплаты (сервер получает уведомление платёжной системы): paid, failed, mismatch — или null по таймауту.</summary>
    public static async Task<CardPayment?> WaitResult(string pid, CancellationToken ct, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(20));
        while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
        {
            try
            {
                if (await Payment(pid) is { Status: "paid" or "failed" or "mismatch" or "refunded" } p)
                {
                    if (p.Status == "paid") { Market.Owned.Add(p.Target); Market.Invalidate(); }
                    return p;
                }
            }
            catch (ServiceError e) when (e.Code is "OFFLINE" or "BUSY") { }
            await Task.Delay(3000, ct).ContinueWith(_ => { });
        }
        return null;
    }

    static async Task<List<JsonObject>> Mine(string collection, string field, int limit = 100)
    {
        var (uid, token) = await Account.Token();
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = collection }),
            ["where"] = new JsonObject { ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = field }, ["op"] = "EQUAL", ["value"] = Firebase.ToValue(uid) } },
            ["limit"] = limit,
        };
        var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().ToList();
    }

    /// <summary>Мои оплаты картой — новые сверху.</summary>
    public static async Task<List<CardPayment>> MyPayments() =>
        (await Mine("payments", "uid")).Select(PaymentOf).OrderByDescending(p => p.Created).ToList();

    public static async Task<Receipt?> ReceiptFor(string pid)
    {
        var (_, token) = await Account.Token();
        try
        {
            if (await Hub.Call(Firebase.Url($"/receipts/{pid}"), HttpMethod.Get, null, token) is not JsonObject d) return null;
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            var c = f.TryGetValue("company", out var cv) && cv is Dictionary<string, object?> m ? m : [];
            return new Receipt(pid, f.S("item"), f.L("amount"), f.S("currency"), f.S("provider"), f.S("providerId"), f.S("type"), Firebase.Time(f.S("date")),
                f.B("refunded"), c.S("name"), c.S("unp"), c.S("address"), c.S("email"), f.B("test"));
        }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
    }

    /// <summary>Чек — HTML-файл в «Документы\ModLaunch\Чеки» (открыть, распечатать или сохранить в PDF).</summary>
    public static string SaveReceipt(Receipt r)
    {
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var dir = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModLaunch", "Receipts")).FullName;
        var file = Path.Combine(dir, $"receipt-{r.Id}.html");
        var html = new StringBuilder()
            .Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(I18n.T("v31.store.receipt.title"))).Append(' ').Append(E(r.Id)).Append("</title>")
            .Append("<style>body{font:15px/1.5 Segoe UI,Arial,sans-serif;max-width:640px;margin:40px auto;color:#111}h1{font-size:22px}td{padding:6px 12px 6px 0;vertical-align:top}.m{color:#666}.r{color:#b00}</style></head><body>")
            .Append("<h1>").Append(E(I18n.T("v31.store.receipt.title"))).Append(" № ").Append(E(r.Id)).Append("</h1>")
            .Append(r.Test ? $"<p class=\"r\">{E(I18n.T("v31.store.receipt.test"))}</p>" : "")
            .Append(r.Refunded ? $"<p class=\"r\">{E(I18n.T("v31.store.receipt.refunded"))}</p>" : "")
            .Append("<table>")
            .Append($"<tr><td class=\"m\">{E(I18n.T("v31.store.receipt.seller"))}</td><td>{E(r.Company)}<br>{E(I18n.T("v31.store.receipt.unp"))} {E(r.Unp)}<br>{E(r.Address)}<br>{E(r.Email)}</td></tr>")
            .Append($"<tr><td class=\"m\">{E(I18n.T("v31.store.receipt.date"))}</td><td>{E(r.Date?.ToLocalTime().ToString("g", I18n.Culture) ?? "")}</td></tr>")
            .Append($"<tr><td class=\"m\">{E(I18n.T("v31.store.receipt.item"))}</td><td>{E(r.Item)}</td></tr>")
            .Append($"<tr><td class=\"m\">{E(I18n.T("v31.store.receipt.amount"))}</td><td><b>{E(Money(r.Amount, r.Currency))}</b></td></tr>")
            .Append($"<tr><td class=\"m\">{E(I18n.T("v31.store.receipt.method"))}</td><td>{E(r.Provider == "webpay" ? "WEBPAY" : "bePaid")} · {E(r.ProviderId)}</td></tr>")
            .Append("</table>")
            .Append($"<p class=\"m\">{E(I18n.T("v31.store.receipt.note"))}</p></body></html>");
        File.WriteAllText(file, html.ToString());
        return file;
    }

    // ---------------------------------------------------------------- подарки, премиум, ключи

    public static async Task<List<Gift>> NewGifts()
    {
        if (!Account.SignedIn) return [];
        return (await Mine("gifts", "to", 50)).Select(d =>
        {
            var name = d.Str("name") ?? "";
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return (Seen: f.B("seen"), Gift: new Gift(name[(name.LastIndexOf('/') + 1)..], f.S("from"), f.S("fromName"), f.S("listing"), f.S("title"), f.S("message"), Firebase.Time(f.S("at"))));
        }).Where(x => !x.Seen).Select(x => x.Gift).ToList();
    }

    public static async Task MarkSeen(Gift g)
    {
        var (_, token) = await Account.Token();
        await Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"gifts/{g.Id}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["seen"] = true }) },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("seen") },
        }], token);
    }

    /// <summary>Подписка премиум, оплаченная картой: до какого числа (null — нет).</summary>
    public static async Task<DateTime?> PremiumUntil()
    {
        if (!Account.SignedIn) return null;
        var (uid, token) = await Account.Token();
        try
        {
            var d = await Hub.Call(Firebase.Url($"/premium/{uid}"), HttpMethod.Get, null, token) as JsonObject;
            return Firebase.Time(Firebase.FromFields(d?["fields"] as JsonObject).S("until"));
        }
        catch (ServiceError e) when (e.Code is "NOT_FOUND" or "DENIED") { return null; }
    }

    public static async Task<(string Listing, string Title)> RedeemKey(string code)
    {
        var r = await Call("redeemKey", new JsonObject { ["code"] = code.Trim() });
        Market.Invalidate();
        return (r.Str("listing") ?? "", r.Str("title") ?? "");
    }

    public static async Task<List<string>> GenerateKeys(string listing, int count) =>
        ((await Call("generateKeys", new JsonObject { ["listing"] = listing, ["count"] = count }))?["codes"] as JsonArray ?? []).Select(c => c?.ToString() ?? "").Where(c => c != "").ToList();

    // ---------------------------------------------------------------- заработок разработчика и выплаты

    public static async Task<List<EarningTotal>> Earnings()
    {
        var (uid, token) = await Account.Token();
        try
        {
            var d = await Hub.Call(Firebase.Url($"/earnings/{uid}"), HttpMethod.Get, null, token) as JsonObject;
            var totals = Firebase.FromFields(d?["fields"] as JsonObject).TryGetValue("totals", out var t) && t is Dictionary<string, object?> m ? m : [];
            return Currencies.Where(totals.ContainsKey).Select(c =>
            {
                var x = totals[c] as Dictionary<string, object?> ?? [];
                return new EarningTotal(c, x.L("gross"), x.L("fee"), x.L("net"), x.L("paid"), x.L("pending"));
            }).ToList();
        }
        catch (ServiceError e) when (e.Code is "NOT_FOUND") { return []; }
    }

    public static async Task<List<EarningEntry>> EarningEntries(int limit = 300)
    {
        var (uid, token) = await Account.Token();
        var q = new JsonObject { ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "entries" }), ["limit"] = limit };
        var rows = await Hub.Call(Firebase.DocUrl(Firebase.Doc($"earnings/{uid}"), ":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(d =>
        {
            var name = d.Str("name") ?? "";
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return new EarningEntry(name[(name.LastIndexOf('/') + 1)..], f.S("type"), f.S("title"), f.S("currency"), f.L("gross"), f.L("fee"), f.L("net"), Firebase.Time(f.S("at")), f.B("refunded"), f.B("gift"));
        }).OrderByDescending(e => e.At).ToList();
    }

    public static Task RequestPayout(string currency, long amount, string details) =>
        Call("requestPayout", new JsonObject { ["currency"] = currency, ["amount"] = amount, ["details"] = details });

    // ---------------------------------------------------------------- админ

    public static async Task Refund(string pid, string reason, string? refundUid = null)
    {
        var data = new JsonObject { ["pid"] = pid, ["reason"] = reason };
        if (!string.IsNullOrWhiteSpace(refundUid)) data["refundUid"] = refundUid.Trim();
        await Call("refundPayment", data);
    }

    public static Task ResolvePayout(string id, bool paid, string note) =>
        Call("resolvePayout", new JsonObject { ["id"] = id, ["paid"] = paid, ["note"] = note });

    // ---------------------------------------------------------------- возвраты оплат картой (14 дней)

    public const int RefundDays = 14;

    /// <summary>Можно ли просить возврат: оплачено, не поддержка автора, не старше 14 дней.</summary>
    public static bool CanAskRefund(CardPayment p) => p.Status == "paid" && p.Type != "tip" && p.PaidAt is { } at && DateTime.UtcNow - at < TimeSpan.FromDays(RefundDays);

    public static Task AskRefund(string pid, string reason) => Call("requestRefund", new JsonObject { ["pid"] = pid, ["reason"] = reason });

    public static Task RejectRefund(string pid, string note) => Call("rejectRefund", new JsonObject { ["pid"] = pid, ["note"] = note });

    /// <summary>Админ: пригласить следующую волну из листа ожидания (по порядку записи). Возвращает, сколько писем ушло.</summary>
    public static async Task<(int Sent, bool More)> InviteWave(int size)
    {
        var r = await Call("inviteWave", new JsonObject { ["size"] = size });
        return (r?["sent"]?.GetValue<int>() ?? 0, r?["left"]?.GetValue<bool>() ?? false);
    }

    public sealed record RefundAsk(string Pid, string Uid, string Title, long Amount, string Currency, string Type, string Reason, string Status, DateTime? Created);

    static RefundAsk AskOf(JsonObject d)
    {
        var name = d.Str("name") ?? "";
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        return new RefundAsk(name[(name.LastIndexOf('/') + 1)..], f.S("uid"), f.S("title"), f.L("amount"), f.S("currency"), f.S("type"), f.S("reason"), f.S("status"), Firebase.Time(f.S("created")));
    }

    /// <summary>Свои заявки на возврат (по номеру оплаты).</summary>
    public static async Task<Dictionary<string, RefundAsk>> MyRefundAsks() =>
        (await Mine("refundRequests", "uid")).Select(AskOf).ToDictionary(a => a.Pid);

    /// <summary>Админ: заявки, ждущие решения.</summary>
    public static async Task<List<RefundAsk>> PendingRefundAsks()
    {
        var (_, token) = await Account.Token();
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "refundRequests" }),
            ["where"] = new JsonObject { ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "status" }, ["op"] = "EQUAL", ["value"] = Firebase.ToValue("pending") } },
            ["limit"] = 200,
        };
        var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(AskOf).OrderBy(a => a.Created).ToList();
    }

    public static string Explain(Exception e) => Firebase.Explain("store", e);

    // ---------------------------------------------------------------- настройки магазина (config/store)

    public sealed record StoreConfig(bool Enabled, int Fee, Dictionary<string, (long Month, long Year)> Premium, string Company, string Unp, string Address, string Email,
        Dictionary<string, long> MinPayout);

    static StoreConfig? _config;
    static DateTime _configAt;

    public static async Task<StoreConfig> Config(bool force = false)
    {
        if (!force && _config is not null && DateTime.UtcNow - _configAt < TimeSpan.FromMinutes(10)) return _config;
        Dictionary<string, object?> f = [];
        try { f = Firebase.FromFields((await Hub.Call(Firebase.Url("/config/store"), HttpMethod.Get, null, null) as JsonObject)?["fields"] as JsonObject); }
        catch (ServiceError e) when (e.Code is "NOT_FOUND") { }
        var prem = f.TryGetValue("premium", out var p) && p is Dictionary<string, object?> pm ? pm : [];
        var company = f.TryGetValue("company", out var c) && c is Dictionary<string, object?> cm ? cm : [];
        var min = f.TryGetValue("minPayout", out var mp) && mp is Dictionary<string, object?> mm ? mm : [];
        _config = new StoreConfig(!f.TryGetValue("enabled", out var en) || en is not false, f.TryGetValue("fee", out var fee) && fee is long fl ? (int)fl : 10,
            Currencies.Where(prem.ContainsKey).ToDictionary(x => x, x => prem[x] is Dictionary<string, object?> d ? (d.L("month"), d.L("year")) : (0L, 0L)),
            company.S("name"), company.S("unp"), company.S("address"), company.S("email"),
            Currencies.Where(min.ContainsKey).ToDictionary(x => x, x => min[x] is long v ? v : 0));
        _configAt = DateTime.UtcNow;
        return _config;
    }

    /// <summary>Админ: сохранить настройки магазина (комиссия 0–29 %, цены премиума, реквизиты для чеков).</summary>
    public static async Task SaveConfig(StoreConfig c)
    {
        var (uid, token, _) = await Hub.Member();
        if (!Account.Get().Admin) throw new ServiceError("DENIED");
        var fields = new Dictionary<string, object?>
        {
            ["enabled"] = c.Enabled, ["fee"] = (long)Math.Clamp(c.Fee, 0, Market.MaxFeePercent), ["by"] = uid,
            ["premium"] = c.Premium.ToDictionary(kv => kv.Key, kv => (object?)new Dictionary<string, object?> { ["month"] = kv.Value.Month, ["year"] = kv.Value.Year }),
            ["company"] = new Dictionary<string, object?> { ["name"] = Hub.Clip(c.Company, 120), ["unp"] = Hub.Clip(c.Unp, 20), ["address"] = Hub.Clip(c.Address, 200), ["email"] = Hub.Clip(c.Email, 120) },
            ["minPayout"] = c.MinPayout.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
        };
        await Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc("config/store"), ["fields"] = Firebase.ToFields(fields) },
            ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
        }], token);
        _config = null;
    }

    /// <summary>Админ: заявки на выплату разработчикам (ожидают перевода).</summary>
    public static async Task<List<(string Id, string Uid, string Currency, long Amount, string Details, DateTime? Created)>> PendingPayouts()
    {
        var (_, token) = await Account.Token();
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "payoutRequests" }),
            ["where"] = new JsonObject { ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "status" }, ["op"] = "EQUAL", ["value"] = Firebase.ToValue("pending") } },
            ["limit"] = 200,
        };
        var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(d =>
        {
            var name = d.Str("name") ?? "";
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return (name[(name.LastIndexOf('/') + 1)..], f.S("uid"), f.S("currency"), f.L("amount"), f.S("details"), Firebase.Time(f.S("created")));
        }).OrderBy(x => x.Item6).ToList();
    }

    [SelfTest]
    static string StorePricesAndMoney()
    {
        var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        var l = new Listing("id", "u", "a", "t", "", "", "game", "", 0, "personal", "1.0.0", "", [], [], 0, "", 0, "", "active", 0, null, null,
            new Dictionary<string, long> { ["BYN"] = 2990, ["USD"] = 999 }, 25, now.AddDays(5));
        if (PriceFor(l, "BYN", now) != 2243 || PriceFor(l, "USD", now.AddDays(10)) != 999 || PriceFor(l, "EUR", now) is not null)
            throw new Exception($"prices: {PriceFor(l, "BYN", now)} {PriceFor(l, "USD", now.AddDays(10))}");
        if (!Currencies.Contains(Currency)) throw new Exception("currency " + Currency);
        var money = Money(2990, "BYN");
        if (!money.Contains("29") || !money.Contains("Br") || !Money(999, "USD").StartsWith('$')) throw new Exception("money: " + money);
        return $"discount 25% → 2243 BYN kopecks (server rounds the same), expired sale ignored, no EUR price → not for sale; {money}, {Money(999, "USD")}; your currency {Currency}";
    }
}
