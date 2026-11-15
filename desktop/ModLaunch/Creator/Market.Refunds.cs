using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Заявка на возврат покупки: кто, что, за сколько, почему и что решил админ.</summary>
public sealed record Refund(string Id, string Uid, string Author, string ListingId, string Title, string Seller, long Price,
    string Reason, string Status, string Note, DateTime? Created);

/// <summary>
/// 30.0: возврат покупок, как в больших магазинах. Покупатель в течение 14 дней пишет, что не так
/// (refunds/r_{uid}_{лот} — одна заявка на покупку). Решает админ: «вернуть» — деньги приходят покупателю
/// записью «refund» в журнале кошелька, продажа помечается refunded (файл товара больше не открыть,
/// невыплаченная выручка продавцу уже не уйдёт, комиссия с неё не берётся); «отказать» — с пояснением.
/// Всё проверяют правила базы (firebase/firestore.rules), молча вернуть деньги нельзя.
/// </summary>
public static partial class Market
{
    public const int RefundDays = 14;
    public const int MinRefundReason = 5;

    public static string RefundId(string uid, string listingId) => $"r_{uid}_{listingId}";

    /// <summary>Можно ли ещё попросить возврат: платная покупка и 14 дней не прошли.</summary>
    public static bool RefundOpen(Purchase p, DateTime? now = null) =>
        p.Price > 0 && p.Refund is null && p.Created is DateTime at && (now ?? DateTime.UtcNow) - at.ToUniversalTime() < TimeSpan.FromDays(RefundDays);

    /// <summary>До какого времени можно вернуть.</summary>
    public static DateTime? RefundUntil(Purchase p) => p.Created?.ToUniversalTime().AddDays(RefundDays);

    static Refund RefundOf(JsonObject d)
    {
        var f = Firebase.FromFields(d["fields"] as JsonObject);
        return new Refund(Id(d), f.S("uid"), f.S("author"), f.S("listing"), f.S("title"), f.S("seller"), f.L("price"),
            f.S("reason"), f.S("status"), f.S("note"), Firebase.Time(f.S("created")));
    }

    /// <summary>Попросить вернуть деньги. Цену, продавца и дату берём из записи продажи — правила сверят их же.</summary>
    public static async Task RequestRefund(Purchase p, string reason)
    {
        var (uid, token, name) = await Hub.Member();
        reason = Hub.Clip(reason, 500);
        if (reason.Length < MinRefundReason) throw new ServiceError("REFUND_REASON");
        var sale = await Hub.Call(Firebase.Url($"/market/{p.ListingId}/sales/{uid}"), HttpMethod.Get, null, token) as JsonObject
            ?? throw new ServiceError("NOT_FOUND");
        var f = Firebase.FromFields(sale["fields"] as JsonObject);
        if (f.B("refunded")) throw new ServiceError("REFUNDED");
        var price = f.L("price");
        if (price <= 0) throw new ServiceError("INVALID");
        if (Firebase.Time(f.S("created")) is not DateTime bought || DateTime.UtcNow - bought.ToUniversalTime() >= TimeSpan.FromDays(RefundDays))
            throw new ServiceError("REFUND_LATE");
        try
        {
            await Hub.Commit([NewDoc($"refunds/{RefundId(uid, p.ListingId)}", new()
            {
                ["uid"] = uid, ["author"] = name, ["listing"] = p.ListingId, ["title"] = Hub.Clip(f.S("title") is { Length: > 0 } t ? t : p.Title, 60),
                ["seller"] = f.S("seller"), ["price"] = price, ["reason"] = reason, ["status"] = "pending", ["note"] = "",
            })], token);
        }
        catch (ServiceError e) when (e.Code is "EXISTS" or "PRECONDITION") { throw new ServiceError("REFUND_EXISTS"); }
        // Сроки и цену мы уже проверили сами: отказ правил значит, что правил возвратов ещё нет в базе.
        catch (ServiceError e) when (e.Code == "DENIED") { throw new ServiceError("REFUND_OFF"); }
    }

    /// <summary>Свои заявки на возврат.</summary>
    public static async Task<List<Refund>> MyRefunds()
    {
        var (uid, token, _) = await Hub.Member();
        return (await Query("refunds", "uid", uid, token: token)).Select(RefundOf).OrderByDescending(r => r.Created).ToList();
    }

    /// <summary>Админ: заявки, которые ждут решения.</summary>
    public static async Task<List<Refund>> PendingRefunds()
    {
        var (_, token, _) = await Hub.Member();
        return (await Query("refunds", "status", "pending", token: token)).Select(RefundOf).OrderBy(r => r.Created).ToList();
    }

    /// <summary>
    /// Админ решает. «Вернуть»: запись «refund» на цену покупки в журнал покупателя, заявка — done,
    /// продажа — refunded и claimed (продавец её уже не заберёт). Всё одной пачкой.
    /// </summary>
    public static async Task ResolveRefund(Refund r, bool ok, string note = "")
    {
        var (admin, token, _) = await Hub.Member();
        note = Hub.Clip(note, 300);
        if (!ok) { await Hub.Commit([SetStatus($"refunds/{r.Id}", "rejected", note)], token); return; }
        await Ledger.Apply(r.Uid, "refund", r.Price, token, admin, r.Id, r.Seller, Hub.Clip(r.Title, 300), _ =>
        [
            SetStatus($"refunds/{r.Id}", "done", note),
            new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"market/{r.ListingId}/sales/{r.Uid}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["refunded"] = true, ["claimed"] = true }),
                },
                ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("refunded", "claimed") },
                ["currentDocument"] = new JsonObject { ["exists"] = true },
            },
        ]);
        Invalidate();
    }

    /// <summary>Продажи, деньги за которые вернули: купить снова нельзя (запись продажи одна на покупателя).</summary>
    public static readonly HashSet<string> RefundedSales = [];

    static bool IsRefunded(JsonNode? saleDoc) => Firebase.FromFields(saleDoc?["fields"] as JsonObject).B("refunded");

    [SelfTest]
    static string RefundWindowAndLedger()
    {
        var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        var fresh = new Purchase("lot", "Mod", "code", "Seller", 500, now.AddDays(-13.9));
        if (!RefundOpen(fresh, now)) throw new Exception("13.9 days should be open");
        if (RefundOpen(fresh with { Created = now.AddDays(-14) }, now)) throw new Exception("14 days should be closed");
        if (RefundOpen(fresh with { Price = 0 }, now)) throw new Exception("free items have nothing to refund");
        var asked = fresh with { Refund = new Refund("r_u_lot", "u", "A", "lot", "Mod", "s", 500, "broken", "pending", "", now) };
        if (RefundOpen(asked, now)) throw new Exception("one request per purchase");
        if (RefundId("abc123", "lot9") != "r_abc123_lot9") throw new Exception("id");
        // Та же арифметика, что в правилах: возврат возвращает баланс и уменьшает «потрачено».
        var w = Ledger.Next(Ledger.Next(WalletState.Zero with { Balance = 1000 }, "purchase", -500), "refund", 500);
        if (w.Balance != 1000 || w.Spent != 0 || w.Earned != 0) throw new Exception($"ledger {w}");
        return "14-day window, one request per purchase, refund restores balance and spent";
    }
}
