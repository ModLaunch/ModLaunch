using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch;

/// <summary>
/// 31.0: программа ↔ сервер магазина (эмулятор функций, WEBPAY в тестовом режиме, секрет — server/functions/test/e2e.secret.local).
/// Без эмулятора функций (порт 5001) проверки пропускаются с пометкой.
/// </summary>
public static class IntegrationStore
{
    const string WebpaySecret = "e2e-webpay-secret";
    static void Expect(bool ok, string what) { if (!ok) throw new Exception(what); }

    static async Task<bool> FunctionsUp()
    {
        try { using var r = await Http.Client.GetAsync($"http://{Firebase.Emulator}:5001/"); return true; }
        catch { return false; }
    }

    static async Task SeedShop()
    {
        await Integration.Emu.Seed("config/store", new() { ["fee"] = 10L, ["enabled"] = true });
        await Integration.Emu.Seed("market/dev1_game", new()
        {
            ["uid"] = "dev1", ["author"] = "Indie Dev", ["title"] = "Космос", ["kind"] = "game", ["status"] = "active", ["price"] = 0L, ["sales"] = 0L,
            ["prices"] = new Dictionary<string, object?> { ["BYN"] = 2990L, ["USD"] = 999L },
        });
    }

    /// <summary>Уведомление WEBPAY об оплате, подписанное как настоящее (MD5 по полям + секрет).</summary>
    static async Task Notify(string pid, string amount)
    {
        var f = new Dictionary<string, string>
        {
            ["batch_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ["currency_id"] = "BYN", ["amount"] = amount, ["payment_method"] = "cc",
            ["order_id"] = "127386", ["site_order_id"] = pid, ["transaction_id"] = "858578101", ["payment_type"] = "4", ["rrn"] = "786755995452",
        };
        f["wsb_signature"] = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(string.Concat(f.Values) + WebpaySecret))).ToLowerInvariant();
        using var r = await Http.Client.PostAsync($"http://{Firebase.Emulator}:5001/{Firebase.ProjectId}/europe-west1/webpayNotify", new FormUrlEncodedContent(f));
        Expect(r.IsSuccessStatusCode, "notify " + (int)r.StatusCode);
    }

    [IntegrationTest]
    static async Task<string> CardPurchaseThroughServer()
    {
        if (!await FunctionsUp()) return "skipped: functions emulator not running (tools/integration.sh starts it)";
        await SeedShop();
        await Integration.NewUser("Покупатель", "card.buyer@example.test");
        var lot = await Market.Get("dev1_game") ?? throw new Exception("listing");
        Expect(Store.PriceFor(lot, "BYN") == 2990 && Store.CardSale(lot), "regional price read");

        var order = await Store.Order("game", lot.Id, "BYN");
        Expect(order.Amount == 2990 && order.Test, "order " + order);
        // Страница оплаты — подписанная форма WEBPAY с нашим номером заказа.
        var page = await Http.Client.GetStringAsync(order.Url);
        Expect(page.Contains("securesandbox.webpay.by") && page.Contains(order.PaymentId) && Regex.IsMatch(page, "name=\"wsb_signature\" value=\"[0-9a-f]{40}\""), "payment page");

        await Notify(order.PaymentId, "29.90");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await Store.WaitResult(order.PaymentId, cts.Token, TimeSpan.FromSeconds(60));
        Expect(result?.Status == "paid", "payment status: " + result?.Status);
        Market.Owned.Clear();
        Expect(await Market.Bought(lot), "game not in library after payment");
        var receipt = await Store.ReceiptFor(order.PaymentId) ?? throw new Exception("no receipt");
        Expect(receipt.Amount == 2990 && receipt.Currency == "BYN" && !receipt.Refunded, "receipt");
        Expect((await Store.MyPayments()).Any(p => p.Id == order.PaymentId && p.Status == "paid"), "payment list");
        try { await Store.Order("game", lot.Id, "BYN"); throw new Exception("bought twice"); }
        catch (ServiceError e) when (e.Code == "OWNED") { }
        return "order 29.90 BYN → signed WEBPAY page → signed notify → paid → in library, receipt, history; second purchase refused";
    }

    [IntegrationTest]
    static async Task<string> CardGiftToFriend()
    {
        if (!await FunctionsUp()) return "skipped: functions emulator not running";
        await SeedShop();
        var anna = await Integration.NewUser("Анна", "gift.anna@example.test");
        var code = await Friends.MyCode();
        var boris = await Integration.NewUser("Борис", "gift.boris@example.test");
        await Friends.Add(code);
        Account.SignOut();
        await Account.SignIn("gift.anna@example.test", "correct-horse-1");
        await Friends.Accept(boris);

        // Анна дарит Борису.
        var order = await Store.Order("gift", "dev1_game", "BYN", giftTo: boris, message: "Играй!");
        await Notify(order.PaymentId, "29.90");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Expect((await Store.WaitResult(order.PaymentId, cts.Token, TimeSpan.FromSeconds(60)))?.Status == "paid", "gift not paid");

        Account.SignOut();
        await Account.SignIn("gift.boris@example.test", "correct-horse-1");
        var gifts = await Store.NewGifts();
        Expect(gifts.Count == 1 && gifts[0].Message == "Играй!" && gifts[0].FromName == "Анна", "gift note");
        Market.Owned.Clear();
        Expect(await Market.Bought((await Market.Get("dev1_game"))!), "gift not in the friend's library");
        await Store.MarkSeen(gifts[0]);
        Expect((await Store.NewGifts()).Count == 0, "gift still unseen");
        return "Anna gifts Борису → paid → game in his library, note «Играй!», marked seen";
    }
}
