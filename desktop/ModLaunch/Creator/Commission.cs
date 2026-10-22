using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Продажа глазами площадки: сколько из неё — комиссия и забрана ли она.</summary>
public sealed record FeeSale(string ListingId, string Buyer, string Seller, string Title, long Price, long Fee, bool Taken, DateTime? Created);

/// <summary>Итоги комиссии: всего, уже на кошельке владельца и ещё не забрано.</summary>
public sealed record FeeSummary(int Percent, long Total, long Taken, long Pending, int Sales, long Turnover, List<FeeSale> Recent);

/// <summary>
/// 14.0: комиссия владельца рынка. С каждой платной продажи площадка удерживает Market.FeePercent %
/// (продавцу приходит остальное — ClaimSales). Владелец (админ из admins/) видит все продажи, меняет
/// процент (config/market, 0–50 %) и забирает накопленное себе в кошелёк записью «fee» — по одной
/// на продажу, правила базы сверяют сумму с полем fee продажи и не дают забрать дважды.
/// Настоящие деньги из кошелька выводятся как обычно — заявкой на выплату.
/// </summary>
public static class Commission
{
    /// <summary>Все продажи рынка (до 1000 свежих): один запрос по всем лотам сразу.</summary>
    public static async Task<List<FeeSale>> AllSales()
    {
        if (Program.Demo) return Demo();
        var (_, token, _) = await Hub.Member();
        var q = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "sales", ["allDescendants"] = true }),
            ["limit"] = 1000,
        };
        var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = q }, token);
        var list = new List<FeeSale>();
        foreach (var d in (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>())
        {
            // …/market/{lot}/sales/{buyer}
            var parts = (d.Str("name") ?? "").Split('/');
            if (parts.Length < 4 || parts[^4] != "market") continue;
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            list.Add(new FeeSale(parts[^3], parts[^1], f.S("seller"), f.S("title"), f.L("price"), f.L("fee"), f.B("feeTaken"), Firebase.Time(f.S("created"))));
        }
        return [.. list.OrderByDescending(s => s.Created)];
    }

    public static FeeSummary Summary(List<FeeSale> sales)
    {
        var paid = sales.Where(s => s.Price > 0).ToList();
        return new FeeSummary(Market.FeePercent, paid.Sum(s => s.Fee), paid.Where(s => s.Taken).Sum(s => s.Fee), paid.Where(s => !s.Taken).Sum(s => s.Fee),
            paid.Count, paid.Sum(s => s.Price), [.. paid.Take(50)]);
    }

    /// <summary>Сохранить новый процент. Он сразу действует для новых продаж: правила базы читают тот же документ.</summary>
    public static async Task SetPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 50);
        if (Program.Demo || Program.Screenshot) { Market.SetFee(percent); return; }
        var (uid, token, _) = await Hub.Member();
        await Hub.Commit(
        [
            new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc("config/market"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["fee"] = (long)percent, ["by"] = uid }),
                },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
            },
        ], token);
        Market.SetFee(percent);
    }

    /// <summary>Забрать всю незабранную комиссию себе в кошелёк. Возвращает число продаж и сумму.</summary>
    public static async Task<(int Count, long Amount)> Claim(IEnumerable<FeeSale> sales)
    {
        var (uid, token, _) = await Hub.Member();
        var count = 0;
        long amount = 0;
        foreach (var s in sales.Where(s => !s.Taken && s.Fee > 0 && s.Price > 0))
        {
            var sale = s;
            await Ledger.Apply(uid, "fee", sale.Fee, token, uid, sale.ListingId, sale.Buyer, Hub.Clip(sale.Title, 300), _ =>
            [
                new JsonObject
                {
                    ["update"] = new JsonObject { ["name"] = Firebase.Doc($"market/{sale.ListingId}/sales/{sale.Buyer}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["feeTaken"] = true }) },
                    ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("feeTaken") },
                    ["currentDocument"] = new JsonObject { ["exists"] = true },
                },
            ]);
            count++;
            amount += sale.Fee;
        }
        return (count, amount);
    }

    /// <summary>Снимки и демо: продажи из демо-лотов.</summary>
    static List<FeeSale> Demo()
    {
        var rnd = new Random(14);
        var lots = Core.Demo.Listings().Where(l => l.Price > 0).ToList();
        var list = new List<FeeSale>();
        for (var i = 0; i < 38 && lots.Count > 0; i++)
        {
            var l = lots[i % lots.Count];
            list.Add(new FeeSale(l.Id, "buyer" + i, l.Uid, l.Title, l.Price, Market.Fee(l.Price), i >= 9, DateTime.UtcNow.AddHours(-i * 7 - rnd.Next(6))));
        }
        return list;
    }

    [SelfTest]
    static string CommissionSumsUp()
    {
        var sales = new List<FeeSale>
        {
            new("a", "b1", "s", "A", 19900, 1990, false, DateTime.UtcNow),
            new("a", "b2", "s", "A", 19900, 1990, true, DateTime.UtcNow.AddDays(-1)),
            new("f", "b3", "s", "Free", 0, 0, false, DateTime.UtcNow),
        };
        var s = Summary(sales);
        if (s.Total != 3980 || s.Taken != 1990 || s.Pending != 1990 || s.Sales != 2 || s.Turnover != 39800) throw new Exception(s.ToString());
        var next = Ledger.Next(WalletState.Zero, "fee", 1990);
        if (next.Balance != 1990 || next.Earned != 1990 || next.Seq != 1) throw new Exception(next.ToString());
        return "fees total, taken and pending; free sales ignored; fee entry counts as earned";
    }
}
