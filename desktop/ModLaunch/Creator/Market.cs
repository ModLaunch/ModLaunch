using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Ассет: модель, текстура, звук, кусок кода. Price 0 — бесплатно, Supply 0 — без ограничения тиража.</summary>
public sealed record Asset(
    string Id, string Uid, string Author, string Name, string Summary, string Description, string Kind, List<string> Games, string Engine,
    List<string> Tags, List<string> Images, string FileName, long Size, string Sha256, int Chunks, string Preview,
    long Price, long Supply, long Sold, long Owners, bool Listed, string Version, DateTime? Created, DateTime? Updated)
{
    public bool Mine => Market.Me == Uid;
    public bool Free => Price == 0;
    public bool Limited => Supply > 0;
    public bool SoldOut => Limited && Sold >= Supply;
    public long Left => Limited ? Math.Max(0, Supply - Sold) : long.MaxValue;
}

/// <summary>Копия ассета у человека: Serial — номер копии (для тиражных — «№3 из 10»).</summary>
public sealed record Owned(string Uid, string Asset, long Serial, string Via, long Paid, string Prev, DateTime? At);
public sealed record Listing(string Uid, string Asset, long Price, long Serial, DateTime? At);
public sealed record Trade(string Id, string From, string FromName, string To, string ToName, string Give, long GiveSerial,
    string Take, long TakeSerial, long Credits, string Text, string Status, DateTime? At)
{
    public bool Incoming => Market.Me == To;
}
public sealed record Wallet(long Balance, DateTime? BonusAt, string LastOp)
{
    public static readonly TimeSpan BonusEvery = TimeSpan.FromHours(20);
    public DateTime NextBonus => (BonusAt ?? DateTime.MinValue) + BonusEvery;
    public bool BonusReady => DateTime.UtcNow + Firebase.Skew > NextBonus.AddSeconds(30);
}
public sealed record LedgerOp(string Id, string Kind, string From, string To, long Amount, string Asset, string Ref, DateTime? At)
{
    public bool Income => To == Market.Me;
}

/// <summary>Живой заказ: заказчик описывает мод, креаторы делают ставки, деньги замораживаются до сдачи.</summary>
public sealed record Order(
    string Id, string Uid, string Author, string Title, string Text, string Game, long Budget, DateTime Closes, int Days, string Status,
    long Bids, string Winner, string WinnerName, long Price, DateTime? Due, string Delivery, string Note, DateTime? DeliveredAt, int Rating,
    DateTime? Created, DateTime? Updated)
{
    public bool Mine => Market.Me == Uid;
    public bool Working => Market.Me == Winner;
    public bool Open => Status == "open" && DateTime.UtcNow + Firebase.Skew < Closes;
    public TimeSpan Left => Closes - (DateTime.UtcNow + Firebase.Skew);
    /// <summary>Исполнитель может забрать деньги сам, если заказчик молчит трое суток после сдачи.</summary>
    public bool Claimable => Status == "delivered" && DeliveredAt is { } d && DateTime.UtcNow + Firebase.Skew > d.AddHours(72).AddMinutes(1);
    public bool Overdue => Status == "assigned" && Due is { } due && DateTime.UtcNow + Firebase.Skew > due.AddMinutes(1);
}
public sealed record Bid(string Uid, string Author, long Price, int Days, string Text, DateTime? At)
{
    public bool Mine => Market.Me == Uid;
}

public sealed class AssetDraft
{
    public string Name = "";
    public string Summary = "";
    public string Description = "";
    public string Kind = "model";
    public List<string> Games = [];
    public string Engine = "any";
    public List<string> Tags = [];
    public List<string> Images = [];
    public long Price;
    public long Supply;
    public bool Listed = true;
    public string Version = "1.0.0";
    /// <summary>Файл ассета (модель, текстура…) или null — тогда Code.</summary>
    public string? File;
    public string Code = "";
    /// <summary>Что видно до покупки платного кода.</summary>
    public string Preview = "";
}

/// <summary>
/// Рынок Creator Hub: кредиты (◈), ассеты, копии и их перепродажа, обмены и живые
/// заказы. Своего сервера нет — каждое движение кредитов идёт одним пакетом
/// записей вместе с записью в журнале ops, а правила firebase/market.rules
/// проверяют, что всё сходится (см. там же, как это устроено).
/// </summary>
public static class Market
{
    public const string Coin = "◈";

    /// <summary>Кто я: uid аккаунта (в показе без сети — «demoMe»).</summary>
    public static string? Me => Program.Demo && Account.Uid is null ? "demoMe" : Account.Uid;
    public const long StartBalance = 100;
    public const long Bonus = 25;

    // ---------------------------------------------------------------- запись

    static JsonObject Fields(Dictionary<string, object?> values) => Firebase.ToFields(values);

    static JsonArray Times(IEnumerable<string> fields) => new(fields.Select(f => (JsonNode)Hub.ServerTime(f)).ToArray());

    static JsonObject Inc(string field, long by) => new()
    {
        ["fieldPath"] = field,
        ["increment"] = new JsonObject { ["integerValue"] = by.ToString() },
    };

    /// <summary>Новый документ (если уже есть — ошибка).</summary>
    static JsonObject Create(string path, Dictionary<string, object?> fields, params string[] times)
    {
        var w = new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc(path), ["fields"] = Fields(fields) },
            ["currentDocument"] = new JsonObject { ["exists"] = false },
        };
        if (times.Length > 0) w["updateTransforms"] = Times(times);
        return w;
    }

    /// <summary>Записать целиком (создать или заменить).</summary>
    static JsonObject Put(string path, Dictionary<string, object?> fields, params string[] times)
    {
        var w = new JsonObject { ["update"] = new JsonObject { ["name"] = Firebase.Doc(path), ["fields"] = Fields(fields) } };
        if (times.Length > 0) w["updateTransforms"] = Times(times);
        return w;
    }

    /// <summary>Поменять поля существующего документа (+ прибавки и время сервера).</summary>
    static JsonObject Patch(string path, Dictionary<string, object?> fields, Dictionary<string, long>? inc = null, params string[] times)
    {
        var transforms = new JsonArray();
        foreach (var (k, v) in inc ?? []) transforms.Add(Inc(k, v));
        foreach (var t in times) transforms.Add(Hub.ServerTime(t));
        var w = new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc(path), ["fields"] = Fields(fields) },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) },
            ["currentDocument"] = new JsonObject { ["exists"] = true },
        };
        if (transforms.Count > 0) w["updateTransforms"] = transforms;
        return w;
    }

    static JsonObject Delete(string path) => new() { ["delete"] = Firebase.Doc(path) };

    static string NewId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return string.Concat(RandomNumberGenerator.GetItems<char>(chars, 20));
    }

    static Dictionary<string, object?> Op(string kind, string from, string to, long amount, string by, string asset = "", string reference = "") => new()
    {
        ["kind"] = kind, ["from"] = from, ["to"] = to, ["amount"] = amount, ["asset"] = asset, ["ref"] = reference, ["by"] = by,
    };

    /// <summary>Списание и зачисление по операции op: прибавка к балансу и lastOp.</summary>
    static JsonObject Move(string uid, string op, long by) =>
        Patch($"wallets/{uid}", new() { ["lastOp"] = op }, new() { ["balance"] = by });

    // ---------------------------------------------------------------- чтение

    static async Task<JsonObject?> Doc(string path, string? token)
    {
        try { return await Hub.Call(Firebase.Url("/" + path), HttpMethod.Get, null, token) as JsonObject; }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
    }

    static string IdOf(JsonObject doc) { var name = doc.Str("name") ?? ""; return name[(name.LastIndexOf('/') + 1)..]; }

    static Dictionary<string, object?> F(JsonObject doc) => Firebase.FromFields(doc["fields"] as JsonObject);

    /// <summary>Запрос к коллекции: равенства, порядок, лимит. parent — документ для подколлекции.</summary>
    static async Task<List<JsonObject>> Query(string collection, string? token, (string Field, object Value)[]? where = null,
        string? orderBy = null, int limit = 200, string? parent = null)
    {
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = collection }),
            ["limit"] = limit,
        };
        if (where is { Length: > 0 })
        {
            var filters = new JsonArray(where.Select(w => (JsonNode)new JsonObject
            {
                ["fieldFilter"] = new JsonObject
                {
                    ["field"] = new JsonObject { ["fieldPath"] = w.Field },
                    ["op"] = "EQUAL",
                    ["value"] = Firebase.ToValue(w.Value),
                },
            }).ToArray());
            q["where"] = filters.Count == 1 ? filters[0]!.DeepClone() : new JsonObject { ["compositeFilter"] = new JsonObject { ["op"] = "AND", ["filters"] = filters } };
        }
        if (orderBy is not null)
            q["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = orderBy }, ["direction"] = "DESCENDING" });
        var url = Firebase.Url((parent is null ? "" : "/" + parent) + ":runQuery");
        var rows = await Hub.Call(url, HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().ToList();
    }

    static DateTime? Time(Dictionary<string, object?> f, string key) => Firebase.Time(f.S(key));

    static Asset ToAsset(JsonObject doc)
    {
        var f = F(doc);
        return new Asset(IdOf(doc), f.S("uid"), f.S("author"), f.S("name"), f.S("summary"), f.S("description"), f.S("kind"), f.A("games"),
            f.S("engine"), f.A("tags"), f.A("images"), f.S("fileName"), f.L("size"), f.S("sha256"), (int)f.L("chunks"), f.S("preview"),
            f.L("price"), f.L("supply"), f.L("sold"), f.L("owners"), f.B("listed"), f.S("version"), Time(f, "created"), Time(f, "updated"));
    }

    static Owned ToOwned(JsonObject doc)
    {
        var f = F(doc);
        return new Owned(f.S("uid"), f.S("asset"), f.L("serial"), f.S("via"), f.L("paid"), f.S("prev"), Time(f, "at"));
    }

    static Listing ToListing(JsonObject doc)
    {
        var f = F(doc);
        return new Listing(f.S("uid"), f.S("asset"), f.L("price"), f.L("serial"), Time(f, "at"));
    }

    static Trade ToTrade(JsonObject doc)
    {
        var f = F(doc);
        return new Trade(IdOf(doc), f.S("from"), f.S("fromName"), f.S("to"), f.S("toName"), f.S("give"), f.L("giveSerial"), f.S("take"),
            f.L("takeSerial"), f.L("credits"), f.S("text"), f.S("status"), Time(f, "at"));
    }

    static Order ToOrder(JsonObject doc)
    {
        var f = F(doc);
        return new Order(IdOf(doc), f.S("uid"), f.S("author"), f.S("title"), f.S("text"), f.S("game"), f.L("budget"), Time(f, "closes") ?? DateTime.UtcNow,
            (int)f.L("days"), f.S("status"), f.L("bids"), f.S("winner"), f.S("winnerName"), f.L("price"), Time(f, "due"), f.S("delivery"), f.S("note"),
            Time(f, "deliveredAt"), (int)f.L("rating"), Time(f, "created"), Time(f, "updated"));
    }

    static Bid ToBid(JsonObject doc)
    {
        var f = F(doc);
        return new Bid(f.S("uid"), f.S("author"), f.L("price"), (int)f.L("days"), f.S("text"), Time(f, "at"));
    }

    static LedgerOp ToOp(JsonObject doc)
    {
        var f = F(doc);
        return new LedgerOp(IdOf(doc), f.S("kind"), f.S("from"), f.S("to"), f.L("amount"), f.S("asset"), f.S("ref"), Time(f, "at"));
    }

    // ---------------------------------------------------------------- демо (снимки экрана)

    public static Wallet? DemoWallet;
    public static List<Asset>? DemoAssets;
    public static List<Order>? DemoOrders;
    public static Dictionary<string, List<Bid>>? DemoBids;
    public static List<Owned>? DemoOwned;
    public static List<Trade>? DemoTrades;
    public static List<LedgerOp>? DemoOps;
    static bool Demo => Program.Demo && DemoAssets is not null;

    // ---------------------------------------------------------------- кошелёк

    /// <summary>Свой кошелёк; нет — создаётся со стартовыми 100 ◈.</summary>
    public static async Task<Wallet> MyWallet(bool create = true)
    {
        if (Demo) return DemoWallet ?? new Wallet(StartBalance, null, "");
        var (uid, token, _) = await Hub.Member();
        var doc = await Doc($"wallets/{uid}", token);
        if (doc is null)
        {
            if (!create) throw new ServiceError("NO_WALLET");
            try { await Hub.Commit(new JsonArray(Create($"wallets/{uid}", new() { ["balance"] = StartBalance, ["lastOp"] = "" }, "bonusAt", "created")), token); }
            catch (ServiceError e) when (e.Code == "EXISTS") { }
            doc = await Doc($"wallets/{uid}", token) ?? throw new ServiceError("NO_WALLET");
        }
        var f = F(doc);
        return new Wallet(f.L("balance"), Time(f, "bonusAt"), f.S("lastOp"));
    }

    /// <summary>Ежедневный бонус: +25 ◈ раз в 20 часов.</summary>
    public static async Task<Wallet> ClaimBonus()
    {
        var wallet = await MyWallet();
        if (!wallet.BonusReady) throw new ServiceError("BONUS_LATER");
        var (uid, token, _) = await Hub.Member();
        var op = NewId();
        await Hub.Commit(new JsonArray(
            Patch($"wallets/{uid}", [], new() { ["balance"] = Bonus }, "bonusAt"),
            Create($"ops/{op}", Op("bonus", "bonus", uid, Bonus, uid), "at")), token);
        return await MyWallet();
    }

    static async Task NeedFunds(long amount)
    {
        var w = await MyWallet();
        if (w.Balance < amount) throw new ServiceError("NO_FUNDS", $"{amount - w.Balance}");
    }

    /// <summary>История: что пришло и что ушло, новые сверху.</summary>
    public static async Task<List<LedgerOp>> History()
    {
        if (Demo) return DemoOps ?? [];
        var (uid, token, _) = await Hub.Member();
        var outgoing = Query("ops", token, [("from", uid)], limit: 100);
        var incoming = Query("ops", token, [("to", uid)], limit: 100);
        await Task.WhenAll(outgoing, incoming);
        return outgoing.Result.Concat(incoming.Result).Select(ToOp).DistinctBy(o => o.Id).OrderByDescending(o => o.At).ToList();
    }

    // ---------------------------------------------------------------- ассеты

    static List<Asset>? _assets;
    static DateTime _assetsAt;

    public static void Invalidate() { _assets = null; _orders = null; }

    public static async Task<List<Asset>> Assets(bool force = false)
    {
        if (Demo) return DemoAssets!;
        if (!force && _assets is not null && DateTime.UtcNow - _assetsAt < TimeSpan.FromMinutes(1)) return _assets;
        _assets = (await Query("assets", null, orderBy: "updated", limit: 300)).Select(ToAsset).ToList();
        _assetsAt = DateTime.UtcNow;
        return _assets;
    }

    public static async Task<Asset?> GetAsset(string id)
    {
        if (Demo) return DemoAssets!.FirstOrDefault(a => a.Id == id);
        return await Doc($"assets/{id}", null) is { } d ? ToAsset(d) : null;
    }

    const int ChunkChars = 900_000;

    /// <summary>Выложить ассет или новую версию: файл — кусками в vault, карточка — в assets.</summary>
    public static async Task<string> Publish(AssetDraft d, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var (uid, token, author) = await Hub.Member();
        var name = Hub.Clip(d.Name, 60);
        if (name == "") throw new ServiceError("INVALID", "name");
        if (!Inventory.Kinds.Contains(d.Kind)) throw new ServiceError("INVALID", "kind");
        var id = Hub.DocId(uid, d.Name);
        var existing = await GetAsset(id);
        if (d.Price > 0) await MyWallet();

        byte[] bytes;
        string fileName;
        if (d.File is not null)
        {
            bytes = await System.IO.File.ReadAllBytesAsync(d.File, ct);
            fileName = Hub.Clip(Path.GetFileName(d.File), 120);
        }
        else
        {
            if (d.Code.Trim() == "") throw new ServiceError("NO_FILE");
            bytes = new UTF8Encoding(false).GetBytes(d.Code.Replace("\r\n", "\n"));
            fileName = Projects.PackageName(name) + ModScript.Extension;
        }
        if (bytes.LongLength > Hub.MaxFile) throw new ServiceError("FILE_TOO_BIG");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var text = Convert.ToBase64String(bytes);
        var chunks = Math.Max(1, (text.Length + ChunkChars - 1) / ChunkChars);
        for (var n = 0; n < chunks; n++)
        {
            ct.ThrowIfCancellationRequested();
            var part = text.Substring(Math.Min(text.Length, n * ChunkChars), Math.Min(ChunkChars, Math.Max(0, text.Length - n * ChunkChars)));
            await Hub.Commit(new JsonArray(Put($"vault/{id}_{n}", new() { ["uid"] = uid, ["asset"] = id, ["n"] = n, ["data"] = part })), token);
            progress?.Report((n + 1.0) / (chunks + 1));
        }
        // Прошлая версия была больше — лишние куски убираем.
        if (existing is not null && existing.Chunks > chunks)
        {
            var extra = new JsonArray();
            for (var n = chunks; n < existing.Chunks; n++) extra.Add(Delete($"vault/{id}_{n}"));
            try { await Hub.Commit(extra, token); } catch { }
        }

        var code = d.File is null;
        var fields = new Dictionary<string, object?>
        {
            ["uid"] = uid,
            ["author"] = author,
            ["name"] = name,
            ["summary"] = Hub.Clip(d.Summary, 200),
            ["description"] = Hub.Clip(d.Description, 5000, multiline: true),
            ["kind"] = d.Kind,
            ["games"] = d.Games.Select(g => Hub.Clip(g, 40)).Where(g => g != "").Distinct().Take(12).ToList(),
            ["engine"] = Hub.Clip(d.Engine, 20),
            ["tags"] = d.Tags.Select(t => Hub.Clip(t, 24)).Where(t => t != "").Distinct().Take(6).ToList(),
            ["images"] = d.Images.Where(u => u.StartsWith("https://")).Select(u => Hub.Clip(u, 400)).Take(6).ToList(),
            ["fileName"] = fileName,
            ["size"] = bytes.LongLength,
            ["sha256"] = sha,
            ["chunks"] = chunks,
            // Бесплатный код виден целиком; у платного — только то, что автор показал сам.
            ["preview"] = Hub.Clip(code && d.Price == 0 ? d.Code : d.Preview, 4000, multiline: true),
            ["price"] = Math.Clamp(d.Price, 0, 100000),
            ["supply"] = existing?.Supply ?? Math.Clamp(d.Supply, 0, 10000),
            ["listed"] = d.Listed,
            ["version"] = Hub.Clip(d.Version, 20),
        };
        JsonObject write;
        if (existing is null)
        {
            fields["sold"] = 0L;
            fields["owners"] = 0L;
            write = Create($"assets/{id}", fields, "created", "updated");
        }
        else write = Patch($"assets/{id}", fields, null, "updated");
        await Hub.Commit(new JsonArray(write), token);
        progress?.Report(1);
        Invalidate();
        return id;
    }

    /// <summary>Снять с продажи или вернуть на витрину.</summary>
    public static async Task SetListed(Asset a, bool listed)
    {
        var (_, token, _) = await Hub.Member();
        await Hub.Commit(new JsonArray(Patch($"assets/{a.Id}", new() { ["listed"] = listed }, null, "updated")), token);
        Invalidate();
    }

    /// <summary>Удалить можно, пока ни у кого нет копии.</summary>
    public static async Task DeleteAsset(Asset a)
    {
        if (a.Owners > 0) throw new ServiceError("HAS_OWNERS");
        var (_, token, _) = await Hub.Member();
        var writes = new JsonArray(Delete($"assets/{a.Id}"));
        for (var n = 0; n < a.Chunks; n++) writes.Add(Delete($"vault/{a.Id}_{n}"));
        await Hub.Commit(writes, token);
        Invalidate();
    }

    /// <summary>Файл ассета из кусков, с проверкой SHA-256. Платный отдаётся только владельцу копии.</summary>
    public static async Task<byte[]> Fetch(Asset a, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var token = await Hub.AnyToken() ?? throw new ServiceError("SIGN_IN");
        using var ms = new MemoryStream();
        for (var n = 0; n < a.Chunks; n++)
        {
            var doc = await Doc($"vault/{a.Id}_{n}", token) ?? throw new ServiceError("NOT_FOUND");
            await ms.WriteAsync(Convert.FromBase64String(F(doc).S("data")), ct);
            progress?.Report((n + 1.0) / a.Chunks);
        }
        var bytes = ms.ToArray();
        if (a.Sha256 != "" && Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != a.Sha256) throw new ServiceError("BAD_HASH");
        return bytes;
    }

    // ---------------------------------------------------------------- копии: взять, купить, перепродать

    public static async Task<List<Owned>> MyItems()
    {
        if (Demo) return DemoOwned ?? [];
        var (uid, _, _) = await Hub.Member();
        return (await Query("owns", null, [("uid", uid)], limit: 300)).Select(ToOwned).OrderByDescending(o => o.At).ToList();
    }

    /// <summary>Кто владеет копиями (для тиражных — номера копий).</summary>
    public static async Task<List<Owned>> OwnersOf(string asset)
    {
        if (Demo) return (DemoOwned ?? []).Where(o => o.Asset == asset).ToList();
        return (await Query("owns", null, [("asset", asset)], limit: 300)).Select(ToOwned).OrderBy(o => o.Serial).ToList();
    }

    public static async Task<Owned?> MyCopy(string asset)
    {
        if (Demo) return DemoOwned?.FirstOrDefault(o => o.Asset == asset);
        var (uid, _, _) = await Hub.Member();
        return await Doc($"owns/{uid}_{asset}", null) is { } d ? ToOwned(d) : null;
    }

    /// <summary>
    /// Получить копию у автора: бесплатно или за цену. Номер копии — следующий по счёту;
    /// если кто-то успел купить одновременно, правила откажут — пробуем ещё раз.
    /// </summary>
    public static async Task<Owned> Acquire(string assetId)
    {
        var (uid, token, _) = await Hub.Member();
        if (await MyCopy(assetId) is { } had) return had;
        for (var attempt = 0; ; attempt++)
        {
            var a = await GetAsset(assetId) ?? throw new ServiceError("NOT_FOUND");
            if (a.Uid == uid) throw new ServiceError("OWN_ITEM");
            if (!a.Listed) throw new ServiceError("NOT_LISTED");
            if (a.SoldOut) throw new ServiceError("SOLD_OUT");
            if (a.Price > 0) await NeedFunds(a.Price);
            var serial = a.Sold + 1;
            var op = a.Price > 0 ? NewId() : "";
            var writes = new JsonArray(
                Create($"owns/{uid}_{a.Id}", new() { ["uid"] = uid, ["asset"] = a.Id, ["serial"] = serial, ["via"] = a.Price > 0 ? "buy" : "free", ["op"] = op, ["ref"] = "", ["prev"] = "", ["paid"] = a.Price }, "at"),
                Patch($"assets/{a.Id}", [], new() { ["sold"] = 1, ["owners"] = 1 }));
            if (a.Price > 0)
            {
                writes.Add(Create($"ops/{op}", Op("buy", uid, a.Uid, a.Price, uid, a.Id), "at"));
                writes.Add(Move(uid, op, -a.Price));
                writes.Add(Move(a.Uid, op, a.Price));
            }
            try
            {
                await Hub.Commit(writes, token);
                Invalidate();
                return new Owned(uid, a.Id, serial, a.Price > 0 ? "buy" : "free", a.Price, "", DateTime.UtcNow);
            }
            catch (ServiceError e) when (e.Code == "DENIED" && attempt < 2) { await Task.Delay(400 * (attempt + 1)); }
        }
    }

    /// <summary>Выбросить свою копию (тиражная копия исчезает насовсем).</summary>
    public static async Task Drop(string assetId)
    {
        var (uid, token, _) = await Hub.Member();
        // Удалять можно только то, что есть: правила смотрят на удаляемый документ.
        var listed = await Doc($"listings/{uid}_{assetId}", null) is not null;
        JsonArray Writes()
        {
            var w = new JsonArray(Delete($"owns/{uid}_{assetId}"));
            if (listed) w.Add(Delete($"listings/{uid}_{assetId}"));
            return w;
        }
        var all = Writes();
        all.Add(Patch($"assets/{assetId}", [], new() { ["owners"] = -1 }));
        try { await Hub.Commit(all, token); }
        catch (ServiceError) { await Hub.Commit(Writes(), token); } // ассет уже удалён автором
        Invalidate();
    }

    public static async Task<List<Listing>> Listings(string? asset = null)
    {
        if (Demo) return [];
        var rows = asset is null ? await Query("listings", null, limit: 200) : await Query("listings", null, [("asset", asset)], limit: 100);
        return rows.Select(ToListing).OrderBy(l => l.Price).ToList();
    }

    /// <summary>Выставить свою копию на продажу (или поменять цену).</summary>
    public static async Task ListForSale(Owned copy, long price)
    {
        var (uid, token, _) = await Hub.Member();
        await MyWallet();
        await Hub.Commit(new JsonArray(Put($"listings/{uid}_{copy.Asset}",
            new() { ["uid"] = uid, ["asset"] = copy.Asset, ["price"] = Math.Clamp(price, 1, 1_000_000), ["serial"] = copy.Serial }, "at")), token);
    }

    public static async Task Unlist(string asset)
    {
        var (uid, token, _) = await Hub.Member();
        await Hub.Commit(new JsonArray(Delete($"listings/{uid}_{asset}")), token);
    }

    /// <summary>Купить копию у другого владельца: кредиты — ему, копия (с тем же номером) — себе.</summary>
    public static async Task<Owned> BuyListing(Listing l)
    {
        var (uid, token, _) = await Hub.Member();
        if (l.Uid == uid) throw new ServiceError("OWN_ITEM");
        if (await MyCopy(l.Asset) is not null) throw new ServiceError("OWNED");
        await NeedFunds(l.Price);
        var op = NewId();
        await Hub.Commit(new JsonArray(
            Create($"ops/{op}", Op("resale", uid, l.Uid, l.Price, uid, l.Asset), "at"),
            Move(uid, op, -l.Price),
            Move(l.Uid, op, l.Price),
            Delete($"listings/{l.Uid}_{l.Asset}"),
            Delete($"owns/{l.Uid}_{l.Asset}"),
            Create($"owns/{uid}_{l.Asset}", new() { ["uid"] = uid, ["asset"] = l.Asset, ["serial"] = l.Serial, ["via"] = "resale", ["op"] = op, ["ref"] = "", ["prev"] = l.Uid, ["paid"] = l.Price }, "at")), token);
        Invalidate();
        return new Owned(uid, l.Asset, l.Serial, "resale", l.Price, l.Uid, DateTime.UtcNow);
    }

    // ---------------------------------------------------------------- обмены

    /// <summary>Предложить обмен: своя копия (или кредиты) за копию другого человека.</summary>
    public static async Task<string> Offer(Owned theirs, string theirName, Owned? mine, long credits, string text)
    {
        var (uid, token, author) = await Hub.Member();
        if (theirs.Uid == uid) throw new ServiceError("OWN_ITEM");
        if (mine is null && credits <= 0) throw new ServiceError("INVALID", "credits");
        if (mine is not null) credits = 0;
        else await NeedFunds(credits);
        var id = NewId();
        await Hub.Commit(new JsonArray(Create($"trades/{id}", new()
        {
            ["from"] = uid, ["fromName"] = author, ["to"] = theirs.Uid, ["toName"] = Hub.Clip(theirName, 32),
            ["give"] = mine?.Asset ?? "", ["giveSerial"] = mine?.Serial ?? 0L, ["take"] = theirs.Asset, ["takeSerial"] = theirs.Serial,
            ["credits"] = credits, ["text"] = Hub.Clip(text, 500), ["status"] = "open",
        }, "at", "updated")), token);
        return id;
    }

    public static async Task<List<Trade>> Trades()
    {
        if (Demo) return DemoTrades ?? [];
        var (uid, token, _) = await Hub.Member();
        var sent = Query("trades", token, [("from", uid)], limit: 100);
        var got = Query("trades", token, [("to", uid)], limit: 100);
        await Task.WhenAll(sent, got);
        return sent.Result.Concat(got.Result).Select(ToTrade).OrderByDescending(t => t.Status == "open").ThenByDescending(t => t.At).ToList();
    }

    /// <summary>Принять обмен: обе копии меняют владельцев (или приходят кредиты) — всё одним пакетом.</summary>
    public static async Task AcceptTrade(Trade t)
    {
        var (uid, token, _) = await Hub.Member();
        if (t.To != uid || t.Status != "open") throw new ServiceError("INVALID");
        if (t.Give != "" && await MyCopy(t.Give) is not null) throw new ServiceError("OWNED");
        var writes = new JsonArray(
            Patch($"trades/{t.Id}", new() { ["status"] = "accepted" }, null, "updated"),
            // Своя копия уходит предложившему.
            Delete($"owns/{uid}_{t.Take}"),
            Create($"owns/{t.From}_{t.Take}", new() { ["uid"] = t.From, ["asset"] = t.Take, ["serial"] = t.TakeSerial, ["via"] = "trade", ["op"] = "", ["ref"] = t.Id, ["prev"] = uid, ["paid"] = 0L }, "at"));
        // Своё объявление о продаже этой копии больше не нужно.
        if (await Doc($"listings/{uid}_{t.Take}", null) is not null) writes.Add(Delete($"listings/{uid}_{t.Take}"));
        if (t.Give != "")
        {
            writes.Add(Delete($"owns/{t.From}_{t.Give}"));
            writes.Add(Create($"owns/{uid}_{t.Give}", new() { ["uid"] = uid, ["asset"] = t.Give, ["serial"] = t.GiveSerial, ["via"] = "trade", ["op"] = "", ["ref"] = t.Id, ["prev"] = t.From, ["paid"] = 0L }, "at"));
        }
        else
        {
            var op = NewId();
            writes.Add(Create($"ops/{op}", Op("trade", t.From, uid, t.Credits, uid, t.Take, t.Id), "at"));
            writes.Add(Move(t.From, op, -t.Credits));
            writes.Add(Move(uid, op, t.Credits));
        }
        try { await Hub.Commit(writes, token); }
        catch (ServiceError e) when (e.Code == "DENIED") { throw new ServiceError("TRADE_STALE", e.Message); }
        Invalidate();
    }

    public static async Task CloseTrade(Trade t)
    {
        var (uid, token, _) = await Hub.Member();
        var status = t.From == uid ? "cancelled" : "declined";
        await Hub.Commit(new JsonArray(Patch($"trades/{t.Id}", new() { ["status"] = status }, null, "updated")), token);
    }

    // ---------------------------------------------------------------- живые заказы

    static List<Order>? _orders;
    static DateTime _ordersAt;

    public static async Task<List<Order>> Orders(bool force = false)
    {
        if (Demo) return DemoOrders ?? [];
        if (!force && _orders is not null && DateTime.UtcNow - _ordersAt < TimeSpan.FromSeconds(8)) return _orders;
        _orders = (await Query("orders", null, orderBy: "updated", limit: 150)).Select(ToOrder).ToList();
        _ordersAt = DateTime.UtcNow;
        return _orders;
    }

    public static async Task<Order?> GetOrder(string id)
    {
        if (Demo) return DemoOrders?.FirstOrDefault(o => o.Id == id);
        return await Doc($"orders/{id}", null) is { } d ? ToOrder(d) : null;
    }

    public static async Task<List<Bid>> Bids(string order)
    {
        if (Demo) return DemoBids?.GetValueOrDefault(order) ?? [];
        return (await Query("bids", null, parent: $"orders/{order}", limit: 100)).Select(ToBid).OrderBy(b => b.Price).ThenBy(b => b.At).ToList();
    }

    /// <summary>Разместить заказ. Бюджет не замораживается, но должен быть в кошельке.</summary>
    public static async Task<string> PostOrder(string title, string text, string game, long budget, TimeSpan open, int days)
    {
        var (uid, token, author) = await Hub.Member();
        if (Hub.Clip(title, 80) == "") throw new ServiceError("INVALID", "title");
        await NeedFunds(budget);
        var id = NewId();
        var closes = DateTime.UtcNow + Firebase.Skew + (open > TimeSpan.FromDays(7) ? TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5) : open);
        await Hub.Commit(new JsonArray(Create($"orders/{id}", new()
        {
            ["uid"] = uid, ["author"] = author, ["title"] = Hub.Clip(title, 80), ["text"] = Hub.Clip(text, 3000, multiline: true), ["game"] = Hub.Clip(game, 40),
            ["budget"] = Math.Clamp(budget, 10, 1_000_000), ["closes"] = closes, ["days"] = (long)Math.Clamp(days, 1, 60), ["status"] = "open", ["bids"] = 0L,
            ["winner"] = "", ["winnerName"] = "", ["price"] = 0L, ["delivery"] = "", ["note"] = "", ["rating"] = 0L,
        }, "due", "deliveredAt", "created", "updated")), token);
        Invalidate();
        return id;
    }

    /// <summary>Сделать ставку или поменять свою (цена не выше бюджета).</summary>
    public static async Task PlaceBid(Order o, long price, int days, string text)
    {
        var (uid, token, author) = await Hub.Member();
        if (o.Uid == uid) throw new ServiceError("OWN_ITEM");
        if (!o.Open) throw new ServiceError("CLOSED");
        await MyWallet(); // деньги за работу придут сюда
        var had = await Doc($"orders/{o.Id}/bids/{uid}", null) is not null;
        var writes = new JsonArray(Put($"orders/{o.Id}/bids/{uid}", new()
        {
            ["uid"] = uid, ["author"] = author, ["price"] = Math.Clamp(price, 1, o.Budget), ["days"] = (long)Math.Clamp(days, 1, 60), ["text"] = Hub.Clip(text, 500, multiline: true),
        }, "at"));
        if (!had) writes.Add(Patch($"orders/{o.Id}", [], new() { ["bids"] = 1 }));
        await Hub.Commit(writes, token);
        Invalidate();
    }

    public static async Task WithdrawBid(Order o)
    {
        var (uid, token, _) = await Hub.Member();
        await Hub.Commit(new JsonArray(Delete($"orders/{o.Id}/bids/{uid}"), Patch($"orders/{o.Id}", [], new() { ["bids"] = -1 })), token);
        Invalidate();
    }

    /// <summary>Выбрать исполнителя: цена его ставки замораживается до сдачи работы.</summary>
    public static async Task Pick(Order o, Bid b)
    {
        var (uid, token, _) = await Hub.Member();
        if (o.Uid != uid || o.Status != "open") throw new ServiceError("INVALID");
        await NeedFunds(b.Price);
        var op = NewId();
        var due = DateTime.UtcNow + Firebase.Skew + TimeSpan.FromDays(b.Days);
        await Hub.Commit(new JsonArray(
            Patch($"orders/{o.Id}", new() { ["status"] = "assigned", ["winner"] = b.Uid, ["winnerName"] = b.Author, ["price"] = b.Price, ["due"] = due }, null, "updated"),
            Create($"ops/{op}", Op("escrow", uid, "order:" + o.Id, b.Price, uid, "", o.Id), "at"),
            Move(uid, op, -b.Price)), token);
        Invalidate();
    }

    /// <summary>Сдать работу: что сделано (мод, ассет или ссылка) и пара слов.</summary>
    public static async Task Deliver(Order o, string delivery, string note)
    {
        var (_, token, _) = await Hub.Member();
        if (Hub.Clip(delivery, 200) == "") throw new ServiceError("INVALID", "delivery");
        await Hub.Commit(new JsonArray(Patch($"orders/{o.Id}",
            new() { ["status"] = "delivered", ["delivery"] = Hub.Clip(delivery, 200), ["note"] = Hub.Clip(note, 1000, multiline: true) }, null, "deliveredAt", "updated")), token);
        Invalidate();
    }

    /// <summary>Принять работу с оценкой (заказчик) или забрать оплату после трёх дней тишины (исполнитель).</summary>
    public static async Task Complete(Order o, int rating)
    {
        var (uid, token, _) = await Hub.Member();
        if (uid == o.Winner) rating = 0;
        var op = NewId();
        await Hub.Commit(new JsonArray(
            Patch($"orders/{o.Id}", new() { ["status"] = "done", ["rating"] = (long)Math.Clamp(rating, 0, 5) }, null, "updated"),
            Create($"ops/{op}", Op("release", "order:" + o.Id, o.Winner, o.Price, uid, "", o.Id), "at"),
            Move(o.Winner, op, o.Price)), token);
        Invalidate();
    }

    /// <summary>Вернуть замороженное заказчику: срок вышел (заказчик) или исполнитель отказался.</summary>
    public static async Task Refund(Order o)
    {
        var (uid, token, _) = await Hub.Member();
        var op = NewId();
        await Hub.Commit(new JsonArray(
            Patch($"orders/{o.Id}", new() { ["status"] = "refunded" }, null, "updated"),
            Create($"ops/{op}", Op("refund", "order:" + o.Id, o.Uid, o.Price, uid, "", o.Id), "at"),
            Move(o.Uid, op, o.Price)), token);
        Invalidate();
    }

    public static async Task CancelOrder(Order o)
    {
        var (_, token, _) = await Hub.Member();
        await Hub.Commit(new JsonArray(Patch($"orders/{o.Id}", new() { ["status"] = "cancelled" }, null, "updated")), token);
        Invalidate();
    }

    /// <summary>Рейтинг креатора по выполненным заказам: средняя оценка и сколько сделано.</summary>
    public static (double Stars, int Done) Reputation(IEnumerable<Order> orders, string uid)
    {
        var done = orders.Where(o => o.Winner == uid && o.Status == "done").ToList();
        var rated = done.Where(o => o.Rating > 0).ToList();
        return (rated.Count == 0 ? 0 : rated.Average(o => o.Rating), done.Count);
    }
}
