using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Лот маркета: модель, часть кода, скрипт, ассет или сборка. Цена — в копейках (сотых долях валюты).</summary>
public sealed record Listing(
    string Id, string Uid, string Author, string Title, string Summary, string Description, string Kind, string Game,
    long Price, string License, string Version, string Preview, List<string> Tags, List<string> Images,
    long Size, string Sha256, int Chunks, string FileName, string Status, long Sales, DateTime? Created, DateTime? Updated)
{
    public bool Mine => Account.SignedIn && Account.Uid == Uid;
    public bool Free => Price == 0;
    public bool Active => Status == "active";
}

public sealed record Sale(string ListingId, string Buyer, string BuyerName, string Seller, string Title, long Price, long Fee, DateTime? Created)
{
    public long Net => Price - Fee;
}

public sealed record Purchase(string ListingId, string Title, string Kind, string Seller, long Price, DateTime? Created);
public sealed record Wallet(long Balance, long Earned, long Spent, long PaidOut);
public sealed record Payout(string Id, string Uid, string Author, long Amount, string Method, string Status, string Note, DateTime? Created);
public sealed record TopUp(string Id, string Uid, string Author, long Amount, string Reference, string Status, DateTime? Created);
public sealed record ListingReview(string Uid, string Author, int Stars, string Text, DateTime? Created);

/// <summary>Что выставляем на продажу.</summary>
public sealed class ListingDraft
{
    public string Title = "";
    public string Summary = "";
    public string Description = "";
    public string Kind = "model";
    public string Game = "";
    public long Price;
    public string License = "personal";
    public string Version = "1.0.0";
    public string Preview = "";
    public List<string> Tags = [];
    public List<string> Images = [];
    public bool Hidden;
    /// <summary>Файл товара (null — оставить прежний).</summary>
    public string? File;
}

/// <summary>
/// Маркет Creator Hub: продажа моделей, частей кода, скриптов и ассетов за баланс
/// ModLaunch. Покупка — одна пачка записей в Firestore: продажа, списание у
/// покупателя, зачисление продавцу (за вычетом комиссии площадки) и комиссия в
/// кошелёк площадки. Сходятся ли суммы, проверяют правила базы
/// (firebase/market.rules), а не программа. Пополнение и выплаты — заявками,
/// их подтверждает админ (почта в коллекции admins).
/// </summary>
public static class Market
{
    public const string Platform = "_platform";
    public const int MaxPreview = 4000;
    public const long MaxFile = 8 * 1024 * 1024;
    const int ChunkChars = 900_000;

    public static readonly string[] Kinds = ["model", "code", "script", "asset", "pack"];
    public static readonly string[] Licenses = ["personal", "commercial", "open"];

    /// <summary>Комиссия площадки, %. Должна совпадать с FEE в firebase/market.rules.</summary>
    public static readonly int FeePercent;
    public static readonly string Currency;
    public static readonly long MinPayout;
    public static readonly string TopUpUrl;

    static Market()
    {
        var json = Resources.Json("market.config.json");
        FeePercent = (int)Math.Clamp(json.Long("feePercent") is > 0 and var f ? f : 10, 0, 90);
        Currency = json.Str("currency") ?? "₽";
        MinPayout = json.Long("minPayout") is > 0 and var m ? m : 50000;
        TopUpUrl = json.Str("topUpUrl") ?? "";
    }

    public static long Fee(long price) => price * FeePercent / 100;

    public static string Money(long minor) =>
        (minor / 100m).ToString(minor % 100 == 0 ? "#,0" : "#,0.00", I18n.Culture) + " " + Currency;

    /// <summary>«199», «199.90», «199,9» → копейки; null — не число.</summary>
    public static long? ParseMoney(string? text)
    {
        var t = (text ?? "").Trim().Replace(" ", "").Replace(',', '.');
        if (t == "") return 0;
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0 && v <= 1_000_000
            ? (long)Math.Round(v * 100) : null;
    }

    // ---------------------------------------------------------------- чтение

    static Listing FromDoc(JsonObject doc)
    {
        var f = Firebase.FromFields(doc["fields"] as JsonObject);
        var name = doc.Str("name") ?? "";
        return new Listing(
            name[(name.LastIndexOf('/') + 1)..], f.S("uid"), f.S("author"), f.S("title"), f.S("summary"), f.S("description"),
            f.S("kind"), f.S("game"), f.L("price"), f.S("license"), f.S("version"), f.S("preview"), f.A("tags"), f.A("images"),
            f.L("size"), f.S("sha256"), (int)f.L("chunks"), f.S("fileName"), f.S("status"), f.L("sales"),
            Firebase.Time(f.S("created")), Firebase.Time(f.S("updated")));
    }

    static string Id(JsonObject doc) { var n = doc.Str("name") ?? ""; return n[(n.LastIndexOf('/') + 1)..]; }

    static async Task<List<JsonObject>> Query(string collection, string? field, string? value, string? parent = null, string order = "created", int limit = 300, string? token = null)
    {
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = collection }),
            ["limit"] = limit,
        };
        if (field is not null)
            q["where"] = new JsonObject
            {
                ["fieldFilter"] = new JsonObject
                {
                    ["field"] = new JsonObject { ["fieldPath"] = field }, ["op"] = "EQUAL", ["value"] = Firebase.ToValue(value),
                },
            };
        else q["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = order }, ["direction"] = "DESCENDING" });
        var url = parent is null ? Firebase.Url(":runQuery") : $"https://firestore.googleapis.com/v1/{Firebase.Doc(parent)}:runQuery?key={Uri.EscapeDataString(Firebase.ApiKey)}";
        var rows = await Hub.Call(url, HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().ToList();
    }

    static List<Listing>? _cache;
    static DateTime _cachedAt;

    /// <summary>Все активные лоты (до 300 свежих), кэш на минуту.</summary>
    public static async Task<List<Listing>> All(bool force = false)
    {
        if (!force && _cache is not null && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(1)) return _cache;
        _cache = (await Query("market", null, null, order: "updated")).Select(FromDoc).Where(l => l.Active).ToList();
        _cachedAt = DateTime.UtcNow;
        return _cache;
    }

    public static void Invalidate() => _cache = null;

    public static IEnumerable<Listing> Sort(IEnumerable<Listing> list, string sort) => sort switch
    {
        "cheap" => list.OrderBy(l => l.Price),
        "expensive" => list.OrderByDescending(l => l.Price),
        "popular" => list.OrderByDescending(l => l.Sales),
        _ => list.OrderByDescending(l => l.Updated),
    };

    /// <summary>Свои лоты, включая скрытые.</summary>
    public static async Task<List<Listing>> Mine()
    {
        var (uid, token, _) = await Hub.Member();
        return (await Query("market", "uid", uid, token: token)).Select(FromDoc).OrderByDescending(l => l.Updated).ToList();
    }

    public static async Task<Listing?> Get(string id)
    {
        try { return await Hub.Call(Firebase.Url($"/market/{id}"), HttpMethod.Get, null, null) is JsonObject doc ? FromDoc(doc) : null; }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
    }

    /// <summary>Продажи своего лота (видит только продавец).</summary>
    public static async Task<List<Sale>> Sales(Listing l)
    {
        var (_, token, _) = await Hub.Member();
        var rows = await Hub.Call(Firebase.Url($"/market/{l.Id}/sales") + "&pageSize=300", HttpMethod.Get, null, token);
        return (rows?["documents"] as JsonArray ?? []).OfType<JsonObject>().Select(d =>
        {
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return new Sale(l.Id, Id(d), f.S("buyerName"), f.S("seller"), f.S("title"), f.L("price"), f.L("fee"), Firebase.Time(f.S("created")));
        }).OrderByDescending(s => s.Created).ToList();
    }

    public static async Task<List<Sale>> AllSales(IEnumerable<Listing> mine)
    {
        var all = new List<Sale>();
        foreach (var l in mine.Where(l => l.Sales > 0)) all.AddRange(await Sales(l));
        return all.OrderByDescending(s => s.Created).ToList();
    }

    public static async Task<List<Purchase>> Purchases()
    {
        var (uid, token, _) = await Hub.Member();
        var rows = await Hub.Call(Firebase.Url($"/wallets/{uid}/purchases") + "&pageSize=300", HttpMethod.Get, null, token);
        var list = (rows?["documents"] as JsonArray ?? []).OfType<JsonObject>().Select(d =>
        {
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return new Purchase(Id(d), f.S("title"), f.S("kind"), f.S("seller"), f.L("price"), Firebase.Time(f.S("created")));
        }).OrderByDescending(p => p.Created).ToList();
        foreach (var p in list) Owned.Add(p.ListingId);
        return list;
    }

    /// <summary>Лоты, купленные в этой сессии или найденные в «Покупках».</summary>
    public static readonly HashSet<string> Owned = [];

    public static async Task<bool> Bought(Listing l)
    {
        if (!Account.SignedIn) return false;
        if (l.Mine || Owned.Contains(l.Id)) return true;
        var (uid, token, _) = await Hub.Member();
        try
        {
            await Hub.Call(Firebase.Url($"/market/{l.Id}/sales/{uid}"), HttpMethod.Get, null, token);
            Owned.Add(l.Id);
            return true;
        }
        catch (ServiceError e) when (e.Code is "NOT_FOUND" or "DENIED") { return false; }
    }

    // ---------------------------------------------------------------- кошелёк

    static Wallet WalletOf(JsonObject? doc)
    {
        var f = Firebase.FromFields(doc?["fields"] as JsonObject);
        return new Wallet(f.L("balance"), f.L("earned"), f.L("spent"), f.L("paidOut"));
    }

    static async Task<JsonObject?> WalletDoc(string uid, string? token)
    {
        try { return await Hub.Call(Firebase.Url($"/wallets/{uid}"), HttpMethod.Get, null, token) as JsonObject; }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
    }

    static JsonObject CreateWallet(string uid) => new()
    {
        ["update"] = new JsonObject
        {
            ["name"] = Firebase.Doc($"wallets/{uid}"),
            ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["balance"] = 0L, ["earned"] = 0L, ["spent"] = 0L, ["paidOut"] = 0L }),
        },
        ["currentDocument"] = new JsonObject { ["exists"] = false },
    };

    /// <summary>Свой кошелёк; нет — заводим пустой.</summary>
    public static async Task<Wallet> MyWallet()
    {
        var (uid, token, _) = await Hub.Member();
        var doc = await WalletDoc(uid, token);
        if (doc is not null) return WalletOf(doc);
        try { await Hub.Commit([CreateWallet(uid)], token); } catch (ServiceError e) when (e.Code == "EXISTS") { }
        return new Wallet(0, 0, 0, 0);
    }

    static async Task EnsurePlatform(string token)
    {
        if (await WalletDoc(Platform, token) is not null) return;
        try { await Hub.Commit([CreateWallet(Platform)], token); } catch (ServiceError e) when (e.Code is "EXISTS" or "DENIED") { }
    }

    static JsonObject Credit(string wallet, Dictionary<string, object?> mark, params (string Field, long By)[] by) => new()
    {
        ["update"] = new JsonObject { ["name"] = Firebase.Doc($"wallets/{wallet}"), ["fields"] = Firebase.ToFields(mark) },
        ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(mark.Keys.Select(k => (JsonNode)k).ToArray()) },
        ["updateTransforms"] = new JsonArray(by.Select(b => (JsonNode)new JsonObject
        {
            ["fieldPath"] = b.Field, ["increment"] = new JsonObject { ["integerValue"] = b.By.ToString() },
        }).ToArray()),
        ["currentDocument"] = new JsonObject { ["exists"] = true },
    };

    // ---------------------------------------------------------------- покупка

    /// <summary>Купить лот (или забрать бесплатный). Деньги переходят одной пачкой; правила базы сверяют суммы.</summary>
    public static async Task Buy(Listing l)
    {
        var (uid, token, name) = await Hub.Member();
        if (l.Uid == uid) throw new ServiceError("OWN");
        if (await Bought(l)) throw new ServiceError("OWNED");
        var fresh = await Get(l.Id) ?? throw new ServiceError("NOT_FOUND");
        if (!fresh.Active) throw new ServiceError("NOT_FOUND");
        var price = fresh.Price;
        var fee = Fee(price);
        var wallet = await MyWallet();
        if (wallet.Balance < price) throw new ServiceError("NO_MONEY");
        await EnsurePlatform(token);

        var mark = new Dictionary<string, object?> { ["lastSale"] = l.Id, ["lastBuyer"] = uid };
        var writes = new JsonArray
        {
            new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"market/{l.Id}/sales/{uid}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                    {
                        ["buyerName"] = name, ["seller"] = fresh.Uid, ["title"] = fresh.Title, ["price"] = price, ["fee"] = fee,
                    }),
                },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
                ["currentDocument"] = new JsonObject { ["exists"] = false },
            },
            Credit(uid, new() { ["lastSale"] = l.Id }, ("balance", -price), ("spent", price)),
            Credit(fresh.Uid, mark, ("balance", price - fee), ("earned", price - fee)),
            Credit(Platform, mark, ("balance", fee), ("earned", fee)),
            Hub.Increment($"market/{l.Id}", "sales", 1),
            new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"wallets/{uid}/purchases/{l.Id}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                    {
                        ["title"] = fresh.Title, ["kind"] = fresh.Kind, ["seller"] = fresh.Author, ["price"] = price,
                    }),
                },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
            },
        };
        await Hub.Commit(writes, token);
        Owned.Add(l.Id);
        Invalidate();
    }

    // ---------------------------------------------------------------- продажа

    /// <summary>Выставить или обновить лот. Файл — кусками в market/{id}/files (читают продавец и купившие).</summary>
    public static async Task<string> Save(ListingDraft d, Listing? existing, IProgress<double>? progress = null)
    {
        var (uid, token, author) = await Hub.Member();
        var title = Hub.Clip(d.Title, 60);
        if (title == "" || !Kinds.Contains(d.Kind) || !Licenses.Contains(d.License)) throw new ServiceError("INVALID");
        if (!Regex.IsMatch(d.Version, @"^\d+\.\d+\.\d+$")) throw new ServiceError("INVALID", "version");
        if (d.Price is < 0 or > 100_000_000) throw new ServiceError("INVALID", "price");
        if (existing is null && d.File is null) throw new ServiceError("NO_FILE");
        await MyWallet();
        var id = existing?.Id ?? Hub.DocId(uid, title);
        if (existing is null && await Get(id) is not null) throw new ServiceError("EXISTS");

        long size = existing?.Size ?? 0;
        string sha = existing?.Sha256 ?? "", fileName = existing?.FileName ?? "";
        var chunks = existing?.Chunks ?? 0;
        if (d.File is not null)
        {
            var bytes = await File.ReadAllBytesAsync(d.File);
            if (bytes.LongLength > MaxFile) throw new ServiceError("FILE_TOO_BIG");
            size = bytes.LongLength;
            sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            fileName = Hub.Clip(Path.GetFileName(d.File), 120);
            var text = Convert.ToBase64String(bytes);
            var old = chunks;
            chunks = (text.Length + ChunkChars - 1) / ChunkChars;
            for (var n = 0; n < chunks; n++)
            {
                var part = text.Substring(n * ChunkChars, Math.Min(ChunkChars, text.Length - n * ChunkChars));
                await Hub.Commit([new JsonObject
                {
                    ["update"] = new JsonObject
                    {
                        ["name"] = Firebase.Doc($"market/{id}/files/{n}"),
                        ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["uid"] = uid, ["data"] = part }),
                    },
                }], token);
                progress?.Report((n + 1.0) / (chunks + 1));
            }
            if (old > chunks)
            {
                var extra = new JsonArray();
                for (var n = chunks; n < old; n++) extra.Add(new JsonObject { ["delete"] = Firebase.Doc($"market/{id}/files/{n}") });
                try { await Hub.Commit(extra, token); } catch { }
            }
        }

        var fields = new Dictionary<string, object?>
        {
            ["uid"] = uid,
            ["author"] = author,
            ["title"] = title,
            ["summary"] = Hub.Clip(d.Summary, 200),
            ["description"] = Hub.Clip(d.Description, 5000, multiline: true),
            ["kind"] = d.Kind,
            ["game"] = Hub.Clip(d.Game, 40),
            ["price"] = d.Price,
            ["license"] = d.License,
            ["version"] = d.Version,
            ["preview"] = (d.Preview ?? "").Replace("\r\n", "\n") is var p && p.Length > MaxPreview ? p[..MaxPreview] : d.Preview ?? "",
            ["tags"] = d.Tags.Where(Hub.Tags.Contains).Distinct().Take(6).ToList(),
            ["images"] = d.Images.Where(u => u.StartsWith("https://")).Select(u => Hub.Clip(u, 400)).Take(6).ToList(),
            ["size"] = size,
            ["sha256"] = sha,
            ["chunks"] = chunks,
            ["fileName"] = fileName,
            ["status"] = d.Hidden ? "hidden" : "active",
        };
        var main = new JsonObject { ["update"] = new JsonObject { ["name"] = Firebase.Doc($"market/{id}"), ["fields"] = Firebase.ToFields(fields) } };
        var times = new JsonArray(Hub.ServerTime("updated"));
        if (existing is null)
        {
            ((JsonObject)main["update"]!["fields"]!)["sales"] = Firebase.ToValue(0L);
            times.Add(Hub.ServerTime("created"));
            main["currentDocument"] = new JsonObject { ["exists"] = false };
        }
        else
        {
            main["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) };
            main["currentDocument"] = new JsonObject { ["exists"] = true };
        }
        main["updateTransforms"] = times;
        await Hub.Commit([main], token);
        progress?.Report(1);
        Invalidate();
        return id;
    }

    /// <summary>Скрыть лот с витрины или вернуть (купившие сохраняют доступ).</summary>
    public static async Task SetHidden(Listing l, bool hidden)
    {
        var (_, token, _) = await Hub.Member();
        await Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"market/{l.Id}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["status"] = hidden ? "hidden" : "active" }) },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("status") },
            ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
        }], token);
        Invalidate();
    }

    /// <summary>Удалить лот целиком — только пока его никто не купил.</summary>
    public static async Task Delete(Listing l)
    {
        if (l.Sales > 0) throw new ServiceError("HAS_SALES");
        var (_, token, _) = await Hub.Member();
        var writes = new JsonArray();
        for (var n = 0; n < l.Chunks; n++) writes.Add(new JsonObject { ["delete"] = Firebase.Doc($"market/{l.Id}/files/{n}") });
        writes.Add(new JsonObject { ["delete"] = Firebase.Doc($"market/{l.Id}") });
        await Hub.Commit(writes, token);
        Invalidate();
    }

    /// <summary>Скачать купленный (или свой) товар и проверить SHA-256.</summary>
    public static async Task<string> Download(Listing l, IProgress<double>? progress = null)
    {
        var (_, token, _) = await Hub.Member();
        if (l.Chunks <= 0) throw new ServiceError("NO_FILE");
        using var ms = new MemoryStream();
        for (var n = 0; n < l.Chunks; n++)
        {
            var doc = await Hub.Call(Firebase.Url($"/market/{l.Id}/files/{n}"), HttpMethod.Get, null, token);
            await ms.WriteAsync(Convert.FromBase64String(Firebase.FromFields(doc?["fields"] as JsonObject).S("data")));
            progress?.Report((n + 1.0) / l.Chunks);
        }
        var got = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        if (l.Sha256 != "" && got != l.Sha256) throw new ServiceError("BAD_HASH");
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DownloadsTemp, "market")).FullName;
        var file = Path.Combine(dir, Regex.Replace(l.FileName is { Length: > 0 } f ? f : $"{Projects.PackageName(l.Title)}.zip", @"[^\w.\-]+", "_"));
        await File.WriteAllBytesAsync(file, ms.ToArray());
        return file;
    }

    // ---------------------------------------------------------------- отзывы

    public static async Task<List<ListingReview>> Reviews(Listing l)
    {
        var rows = await Hub.Call(Firebase.Url($"/market/{l.Id}/reviews") + "&pageSize=100", HttpMethod.Get, null, null);
        return (rows?["documents"] as JsonArray ?? []).OfType<JsonObject>().Select(d =>
        {
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return new ListingReview(Id(d), f.S("author"), (int)f.L("stars"), f.S("text"), Firebase.Time(f.S("created")));
        }).OrderByDescending(r => r.Created).ToList();
    }

    public static async Task Review(Listing l, int stars, string text)
    {
        var (uid, token, name) = await Hub.Member();
        await Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject
            {
                ["name"] = Firebase.Doc($"market/{l.Id}/reviews/{uid}"),
                ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["author"] = name, ["stars"] = (long)Math.Clamp(stars, 1, 5), ["text"] = Hub.Clip(text, 1000, multiline: true) }),
            },
            ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
        }], token);
    }

    // ---------------------------------------------------------------- пополнение и выплаты

    static JsonObject NewDoc(string path, Dictionary<string, object?> fields) => new()
    {
        ["update"] = new JsonObject { ["name"] = Firebase.Doc(path), ["fields"] = Firebase.ToFields(fields) },
        ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
        ["currentDocument"] = new JsonObject { ["exists"] = false },
    };

    /// <summary>Заявка на вывод: деньги сразу уходят с баланса в «ожидает», админ переводит и отмечает.</summary>
    public static async Task RequestPayout(long amount, string method)
    {
        var (uid, token, name) = await Hub.Member();
        if (amount < MinPayout) throw new ServiceError("MIN_PAYOUT", Money(MinPayout));
        if (Hub.Clip(method, 300) == "") throw new ServiceError("INVALID");
        var wallet = await MyWallet();
        if (wallet.Balance < amount) throw new ServiceError("NO_MONEY");
        var id = $"{uid}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        await Hub.Commit([
            NewDoc($"payouts/{id}", new() { ["uid"] = uid, ["author"] = name, ["amount"] = amount, ["method"] = Hub.Clip(method, 300), ["status"] = "pending", ["note"] = "" }),
            Credit(uid, new() { ["lastPayout"] = id }, ("balance", -amount), ("paidOut", amount)),
        ], token);
    }

    /// <summary>Заявка на пополнение: человек оплатил по ссылке, админ сверяет и зачисляет.</summary>
    public static async Task RequestTopUp(long amount, string reference)
    {
        var (uid, token, name) = await Hub.Member();
        if (amount <= 0 || Hub.Clip(reference, 300) == "") throw new ServiceError("INVALID");
        await MyWallet();
        await Hub.Commit([NewDoc($"topups/{uid}_{DateTime.UtcNow:yyyyMMddHHmmss}",
            new() { ["uid"] = uid, ["author"] = name, ["amount"] = amount, ["reference"] = Hub.Clip(reference, 300), ["status"] = "pending" })], token);
    }

    static Payout PayoutOf(JsonObject d)
    {
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        return new Payout(Id(d), f.S("uid"), f.S("author"), f.L("amount"), f.S("method"), f.S("status"), f.S("note"), Firebase.Time(f.S("created")));
    }

    static TopUp TopUpOf(JsonObject d)
    {
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        return new TopUp(Id(d), f.S("uid"), f.S("author"), f.L("amount"), f.S("reference"), f.S("status"), Firebase.Time(f.S("created")));
    }

    public static async Task<List<Payout>> MyPayouts()
    {
        var (uid, token, _) = await Hub.Member();
        return (await Query("payouts", "uid", uid, token: token)).Select(PayoutOf).OrderByDescending(p => p.Created).ToList();
    }

    public static async Task<List<TopUp>> MyTopUps()
    {
        var (uid, token, _) = await Hub.Member();
        return (await Query("topups", "uid", uid, token: token)).Select(TopUpOf).OrderByDescending(p => p.Created).ToList();
    }

    // ---------------------------------------------------------------- админ

    public static async Task<List<Payout>> PendingPayouts()
    {
        var (_, token, _) = await Hub.Member();
        return (await Query("payouts", "status", "pending", token: token)).Select(PayoutOf).OrderBy(p => p.Created).ToList();
    }

    public static async Task<List<TopUp>> PendingTopUps()
    {
        var (_, token, _) = await Hub.Member();
        return (await Query("topups", "status", "pending", token: token)).Select(TopUpOf).OrderBy(p => p.Created).ToList();
    }

    public static async Task<Wallet> PlatformWallet()
    {
        var (_, token, _) = await Hub.Member();
        return WalletOf(await WalletDoc(Platform, token));
    }

    static JsonObject SetStatus(string path, string status, string note = "") => new()
    {
        ["update"] = new JsonObject { ["name"] = Firebase.Doc(path), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["status"] = status, ["note"] = note }) },
        ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("status", "note") },
    };

    /// <summary>Выплата: отметить переведённой или отклонить (деньги вернутся на баланс).</summary>
    public static async Task ResolvePayout(Payout p, bool paid, string note = "")
    {
        var (_, token, _) = await Hub.Member();
        var writes = new JsonArray { SetStatus($"payouts/{p.Id}", paid ? "paid" : "rejected", Hub.Clip(note, 300)) };
        if (!paid) writes.Add(Credit(p.Uid, new() { ["lastAdmin"] = p.Id }, ("balance", p.Amount), ("paidOut", -p.Amount)));
        await Hub.Commit(writes, token);
    }

    /// <summary>Пополнение: зачислить на баланс или отклонить.</summary>
    public static async Task ResolveTopUp(TopUp t, bool ok)
    {
        var (_, token, _) = await Hub.Member();
        var writes = new JsonArray { SetStatus($"topups/{t.Id}", ok ? "done" : "rejected") };
        if (ok) writes.Add(Credit(t.Uid, new() { ["lastAdmin"] = t.Id }, ("balance", t.Amount)));
        await Hub.Commit(writes, token);
    }

    /// <summary>Ручное начисление админом (бонус, возврат).</summary>
    public static async Task Grant(string uid, long amount)
    {
        var (_, token, _) = await Hub.Member();
        if (await WalletDoc(uid, token) is null) throw new ServiceError("NOT_FOUND");
        await Hub.Commit([Credit(uid, new() { ["lastAdmin"] = "grant" }, ("balance", amount))], token);
    }

    public static string Explain(Exception e) => Firebase.Explain("market", e);
}
