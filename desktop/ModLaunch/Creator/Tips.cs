using System.Globalization;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Поддержка: от кого, кому, сколько, сколько из неё комиссия, сообщение и забрана ли.</summary>
public sealed record Tip(string Id, string From, string FromName, string To, string ToName, long Amount, long Fee, string Message, bool Claimed, bool FeeTaken, DateTime? Created)
{
    public long Net => Amount - Fee;
}

/// <summary>
/// 27.0: «Поддержать автора» и «Поддержать ModLaunch» — деньги с баланса ModLaunch, как покупка на рынке.
/// Поддержка — документ tips/{tid} и списание «tip» в журнале дарящего одной пачкой; правила базы сверяют сумму,
/// комиссию (та же, что у продаж, — config/market) и что запись одна. Автор забирает поддержку себе сам (Claim, запись
/// «tip-in»), владелец — комиссию (TakeFees, запись «fee»). Поддержка ModLaunch: to = «_platform», комиссия — вся сумма.
/// Пока правила с поддержкой не выложены на сервер, запись отклоняется (DENIED) — и человек видит «временно недоступно».
/// </summary>
public static class Tips
{
    public const string Platform = "_platform";
    public const long Min = 1000, Max = 10_000_000;

    /// <summary>Быстрые суммы (в сотых долях валюты): 50, 100, 300, 500.</summary>
    public static readonly long[] Presets = [5000, 10000, 30000, 50000];

    /// <summary>Комиссия с поддержки: автору — как у продаж, ModLaunch — вся сумма идёт площадке.</summary>
    public static long FeeFor(string to, long amount) => to == Platform ? amount : Market.Fee(amount);

    /// <summary>Номер поддержки: «t_» + uid + «_» + время до секунды (правила проверяют этот вид).</summary>
    internal static string NewId(string uid, DateTime utc) => $"t_{uid}_{utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

    /// <summary>Поддержать: списать amount со своего баланса. Ошибки — ServiceError: SIGN_IN, OWN, INVALID, NO_MONEY, DENIED.</summary>
    public static async Task Give(string to, string toName, long amount, string message)
    {
        if (amount < Min || amount > Max) throw new ServiceError("INVALID");
        if (Program.Demo || Program.Screenshot) return;
        var (uid, token, name) = await Hub.Member();
        if (to == uid) throw new ServiceError("OWN");
        await Market.LoadFee(force: true);
        var fee = FeeFor(to, amount);
        var tid = NewId(uid, Market.UniqueUtc());
        var text = Hub.Clip(message.Trim(), 300);
        await Ledger.Apply(uid, "tip", -amount, token, uid, tid, to, text == "" ? Hub.Clip(toName, 300) : text, _ =>
        [
            new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"tips/{tid}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                    {
                        ["from"] = uid, ["fromName"] = Hub.Clip(name, 32), ["to"] = to, ["toName"] = Hub.Clip(toName, 32),
                        ["amount"] = amount, ["fee"] = fee, ["message"] = text, ["claimed"] = to == Platform, ["feeTaken"] = false,
                    }),
                },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
                ["currentDocument"] = new JsonObject { ["exists"] = false },
            },
        ]);
    }

    /// <summary>Поддержки по условиям (поле == значение), свежие сверху.</summary>
    static async Task<List<Tip>> Query(string token, params (string Field, object Value)[] where)
    {
        JsonObject Filter((string Field, object Value) w) => new()
        {
            ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = w.Field }, ["op"] = "EQUAL", ["value"] = Firebase.ToValue(w.Value) },
        };
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "tips" }),
            ["where"] = where.Length == 1 ? Filter(where[0])
                : new JsonObject { ["compositeFilter"] = new JsonObject { ["op"] = "AND", ["filters"] = new JsonArray(where.Select(w => (JsonNode)Filter(w)).ToArray()) } },
            ["limit"] = 300,
        };
        var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return [.. (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(From).OrderByDescending(t => t.Created)];
    }

    static Tip From(JsonObject d)
    {
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        var id = d.Str("name")?.Split('/')[^1] ?? "";
        return new Tip(id, f.S("from"), f.S("fromName"), f.S("to"), f.S("toName"), f.L("amount"), f.L("fee"), f.S("message"), f.B("claimed"), f.B("feeTaken"), Firebase.Time(f.S("created")));
    }

    /// <summary>Поддержки, полученные автором (для «кто поддержал»).</summary>
    public static async Task<List<Tip>> Received()
    {
        var (uid, token, _) = await Hub.Member();
        return await Query(token, ("to", uid));
    }

    /// <summary>Автор забирает новые поддержки к себе в кошелёк: по записи «tip-in» на каждую. Возвращает число и сумму.</summary>
    public static async Task<(int Count, long Amount)> Claim()
    {
        var (uid, token, _) = await Hub.Member();
        var count = 0;
        long amount = 0;
        foreach (var t in await Query(token, ("to", uid), ("claimed", false)))
        {
            if (t.Net <= 0) continue;
            var tip = t;
            await Ledger.Apply(uid, "tip-in", tip.Net, token, uid, tip.Id, tip.From, Hub.Clip(tip.FromName + (tip.Message == "" ? "" : ": " + tip.Message), 300), _ =>
            [
                new JsonObject
                {
                    ["update"] = new JsonObject { ["name"] = Firebase.Doc($"tips/{tip.Id}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["claimed"] = true }) },
                    ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("claimed") },
                    ["currentDocument"] = new JsonObject { ["exists"] = true },
                },
            ]);
            count++;
            amount += tip.Net;
        }
        return (count, amount);
    }

    /// <summary>Владелец: все поддержки с незабранной комиссией (и поддержки ModLaunch целиком).</summary>
    public static async Task<List<Tip>> PendingFees()
    {
        var (_, token, _) = await Hub.Member();
        return [.. (await Query(token, ("feeTaken", false))).Where(t => t.Fee > 0)];
    }

    /// <summary>Владелец забирает комиссию с поддержек к себе в кошелёк (запись «fee» на каждую).</summary>
    public static async Task<(int Count, long Amount)> TakeFees(IEnumerable<Tip> tips)
    {
        var (uid, token, _) = await Hub.Member();
        var count = 0;
        long amount = 0;
        foreach (var t in tips.Where(t => !t.FeeTaken && t.Fee > 0))
        {
            var tip = t;
            await Ledger.Apply(uid, "fee", tip.Fee, token, uid, tip.Id, tip.From, Hub.Clip(tip.To == Platform ? "ModLaunch: " + tip.FromName : tip.ToName, 300), _ =>
            [
                new JsonObject
                {
                    ["update"] = new JsonObject { ["name"] = Firebase.Doc($"tips/{tip.Id}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["feeTaken"] = true }) },
                    ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("feeTaken") },
                    ["currentDocument"] = new JsonObject { ["exists"] = true },
                },
            ]);
            count++;
            amount += tip.Fee;
        }
        return (count, amount);
    }

    [SelfTest]
    static string TipsMatchRules()
    {
        if (NewId("abc", new DateTime(2026, 11, 13, 9, 5, 7, DateTimeKind.Utc)) != "t_abc_20261113090507") throw new Exception("tip id");
        if (FeeFor(Platform, 10000) != 10000 || FeeFor("author", 10000) != Market.Fee(10000)) throw new Exception("fee");
        var w = Ledger.Next(WalletState.Zero, "topup", 50000);
        w = Ledger.Next(w, "tip", -10000);
        if (w.Balance != 40000 || w.Spent != 10000 || w.Earned != 0) throw new Exception("giver " + w);
        var a = Ledger.Next(WalletState.Zero, "tip-in", 9000);
        if (a.Balance != 9000 || a.Earned != 9000 || a.Spent != 0) throw new Exception("author " + a);
        if (!Ledger.Kinds.Contains("tip") || !Ledger.Kinds.Contains("tip-in")) throw new Exception("kinds");
        return "tip id format, platform takes all, giver spends and author earns as the rules count";
    }
}
