using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Кошелёк: баланс и итоги — кэш журнала (seq — номер последней записи).</summary>
public sealed record WalletState(long Balance, long Earned, long Spent, long PaidOut, long ToppedUp, long Seq, string? UpdateTime = null)
{
    public static readonly WalletState Zero = new(0, 0, 0, 0, 0, 0);
}

/// <summary>Запись журнала кошелька: что случилось, на сколько, сколько стало и на каком основании.</summary>
public sealed record LedgerEntry(long Seq, string Kind, long Amount, long Balance, string Ref, string Party, string Note, string By, DateTime? Created);

/// <summary>
/// 9.5: кошельки на журнале. Баланс меняется только вместе с новой записью журнала
/// (wallets/{uid}/ledger/{seq}) — одной пачкой; правила базы (firebase/firestore.rules)
/// сверяют сумму записи с её основанием: покупка — с новой продажей, выручка — с продажей
/// этого продавца, вывод — с новой заявкой, пополнение и возврат — с решением админа.
/// Деньги нельзя ни добавить, ни списать молча, а история видна целиком. Серверных функций
/// не нужно. Покупатель не пишет в кошелёк продавца: продавец сам «забирает» выручку (Claim).
/// </summary>
public static class Ledger
{
    public static readonly string[] Kinds = ["purchase", "sale", "payout", "payout-return", "topup", "grant", "fee"];

    /// <summary>Новое состояние после записи — та же арифметика, что в правилах (mWalletOk).</summary>
    public static WalletState Next(WalletState prev, string kind, long amount) => new(
        prev.Balance + amount,
        prev.Earned + (kind is "sale" or "fee" ? amount : 0),
        prev.Spent + (kind == "purchase" ? -amount : 0),
        prev.PaidOut + (kind is "payout" or "payout-return" ? -amount : 0),
        prev.ToppedUp + (kind == "topup" ? amount : 0),
        prev.Seq + 1);

    static WalletState From(JsonObject doc)
    {
        var f = Firebase.FromFields(doc["fields"] as JsonObject);
        return new WalletState(f.L("balance"), f.L("earned"), f.L("spent"), f.L("paidOut"), f.L("toppedUp"), f.L("seq"), doc.Str("updateTime"));
    }

    /// <summary>Кошелёк как есть (нет документа — нулевой).</summary>
    public static async Task<WalletState> Read(string uid, string token)
    {
        try { return await Hub.Call(Firebase.Url($"/wallets/{uid}"), HttpMethod.Get, null, token) is JsonObject doc ? From(doc) : WalletState.Zero; }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return WalletState.Zero; }
    }

    /// <summary>Запись «кошелёк целиком»: только если с момента чтения его никто не менял (updateTime).</summary>
    static JsonObject WalletWrite(string uid, WalletState next, string? updateTime) => new()
    {
        ["update"] = new JsonObject
        {
            ["name"] = Firebase.Doc($"wallets/{uid}"),
            ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
            {
                ["balance"] = next.Balance, ["earned"] = next.Earned, ["spent"] = next.Spent, ["paidOut"] = next.PaidOut,
                ["toppedUp"] = next.ToppedUp, ["seq"] = next.Seq,
            }),
        },
        ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
        ["currentDocument"] = updateTime is null ? new JsonObject { ["exists"] = false } : new JsonObject { ["updateTime"] = updateTime },
    };

    static JsonObject EntryWrite(string uid, WalletState next, string kind, long amount, string reference, string party, string note, string by) => new()
    {
        ["update"] = new JsonObject
        {
            ["name"] = Firebase.Doc($"wallets/{uid}/ledger/{next.Seq}"),
            ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
            {
                ["kind"] = kind, ["amount"] = amount, ["balance"] = next.Balance, ["seq"] = next.Seq,
                ["ref"] = Hub.Clip(reference, 200), ["party"] = Hub.Clip(party, 128), ["note"] = Hub.Clip(note, 300), ["by"] = by,
            }),
        },
        ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
        ["currentDocument"] = new JsonObject { ["exists"] = false },
    };

    /// <summary>
    /// Изменить баланс: кошелёк + запись журнала + документы-основания (extra) — одной пачкой.
    /// Кто-то успел раньше (вторая покупка у того же продавца в ту же секунду) — перечитываем и повторяем.
    /// </summary>
    public static async Task<WalletState> Apply(string uid, string kind, long amount, string token, string by,
        string reference = "", string party = "", string note = "", Func<WalletState, IEnumerable<JsonObject>>? extra = null)
    {
        if (!Kinds.Contains(kind) || amount == 0) throw new ServiceError("INVALID");
        for (var attempt = 0; ; attempt++)
        {
            var prev = await Read(uid, token);
            var next = Next(prev, kind, amount);
            if (next.Balance < 0) throw new ServiceError("NO_MONEY");
            var writes = new JsonArray();
            if (extra is not null) foreach (var w in extra(prev)) writes.Add(w);
            writes.Add(WalletWrite(uid, next, prev.UpdateTime));
            writes.Add(EntryWrite(uid, next, kind, amount, reference, party, note, by));
            try
            {
                await Hub.Commit(writes, token);
                return next;
            }
            catch (ServiceError e) when (e.Code is "PRECONDITION" or "EXISTS" && attempt < 3)
            {
                await Task.Delay(250 * (attempt + 1));
            }
        }
    }

    /// <summary>Журнал кошелька: свежие записи сверху.</summary>
    public static async Task<List<LedgerEntry>> History(string uid, string token, int limit = 100)
    {
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "ledger" }),
            ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "seq" }, ["direction"] = "DESCENDING" }),
            ["limit"] = limit,
        };
        var url = $"https://firestore.googleapis.com/v1/{Firebase.Doc($"wallets/{uid}")}:runQuery?key={Uri.EscapeDataString(Firebase.ApiKey)}";
        var rows = await Hub.Call(url, HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(d =>
        {
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return new LedgerEntry(f.L("seq"), f.S("kind"), f.L("amount"), f.L("balance"), f.S("ref"), f.S("party"), f.S("note"), f.S("by"), Firebase.Time(f.S("created")));
        }).ToList();
    }

    /// <summary>Сходится ли журнал сам с собой: каждая запись = прошлый баланс + сумма (для «Проверить кошелёк»).</summary>
    public static bool Consistent(IReadOnlyList<LedgerEntry> newestFirst, WalletState wallet)
    {
        if (newestFirst.Count == 0) return true;
        if (newestFirst[0].Seq != wallet.Seq || newestFirst[0].Balance != wallet.Balance) return false;
        for (var i = 0; i + 1 < newestFirst.Count; i++)
        {
            var (cur, prev) = (newestFirst[i], newestFirst[i + 1]);
            if (cur.Seq != prev.Seq + 1 || cur.Balance != prev.Balance + cur.Amount) return false;
        }
        return true;
    }

    [SelfTest]
    static string RulesShipWithApp()
    {
        if (Avalonia.Application.Current is null) return "skipped (no UI assets in --selfcheck)";
        using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/firestore.rules"));
        var text = new StreamReader(stream).ReadToEnd();
        foreach (var part in new[] { "rules_version = '2'", "match /friendships/", "match /creations/", "match /reviews/", "function mWalletOk", "match /ledger/" })
            if (!text.Contains(part)) throw new Exception("rules file misses " + part);
        // 14.0: процент — в config/market; без документа правила берут те же 10 %, что market.config.json.
        if (!text.Contains($"get(mCfg()).data.get('fee', {Market.FeePercent}) : {Market.FeePercent}; }}")) throw new Exception("default fee in rules differs from market.config.json");
        if (!text.Contains("e.kind == 'fee'") || !Ledger.Kinds.Contains("fee")) throw new Exception("fee ledger kind missing");
        return $"complete rules file inside the app ({text.Length / 1024} KB), default fee {Market.FeePercent}% matches, owner fee kind present";
    }

    [SelfTest]
    static string MathMatchesRules()
    {
        var w = WalletState.Zero;
        w = Next(w, "topup", 50_000);
        w = Next(w, "purchase", -19_900);
        w = Next(w, "sale", 17_910);
        w = Next(w, "payout", -10_000);
        w = Next(w, "payout-return", 10_000);
        w = Next(w, "grant", 90);
        if (w != new WalletState(48_100, 17_910, 19_900, 0, 50_000, 6)) throw new Exception(w.ToString());
        var history = new List<LedgerEntry>
        {
            new(3, "sale", 17_910, 48_010, "", "", "", "", null),
            new(2, "purchase", -19_900, 30_100, "", "", "", "", null),
            new(1, "topup", 50_000, 50_000, "", "", "", "", null),
        };
        if (!Consistent(history, new WalletState(48_010, 17_910, 19_900, 0, 50_000, 3))) throw new Exception("consistent journal rejected");
        if (Consistent(history, new WalletState(99_999, 0, 0, 0, 0, 3))) throw new Exception("tampered balance accepted");
        return "topup, purchase, sale, payout and its return, grant: balance and totals as the rules expect";
    }
}
