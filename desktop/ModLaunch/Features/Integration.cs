using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch;

/// <summary>
/// Метка «интеграционная проверка»: метод <c>static async Task&lt;string&gt; X()</c> гоняет настоящий
/// код программы против локального эмулятора Firebase (вход, деньги, правила базы).
/// Запуск: tools/integration.sh (поднимает эмулятор и вызывает ModLaunch.exe --integration).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IntegrationTestAttribute : Attribute;

/// <summary>31.0: интеграционные проверки критичных путей — вход в аккаунт и платежи.</summary>
public static class Integration
{
    /// <summary>--integration: все проверки с меткой [IntegrationTest]. Код выхода 0 — всё хорошо.</summary>
    public static int Run()
    {
        if (Firebase.Emulator is null)
        {
            Console.WriteLine("FAIL set MODLAUNCH_FIREBASE_EMULATOR=127.0.0.1 (run tools/integration.sh)");
            return 2;
        }
        var failed = 0;
        // MODLAUNCH_ONLY=часть имени — гонять только подходящие проверки (отладка одной).
        var only = Environment.GetEnvironmentVariable("MODLAUNCH_ONLY");
        foreach (var m in Hooks.Marked<IntegrationTestAttribute>().Where(m => string.IsNullOrEmpty(only) || m.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
        {
            var name = $"{m.DeclaringType!.Name}.{m.Name}";
            try
            {
                // Проверки async, а Avalonia держит свой поток: гоняем в пуле, чтобы не было взаимной блокировки.
                var result = Task.Run(async () => { await Emu.Reset(); return await (Task<string>)m.Invoke(null, null)!; }).GetAwaiter().GetResult();
                Console.WriteLine($"ok   {name}: {result}");
            }
            catch (Exception e)
            {
                failed++;
                var inner = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
                Console.WriteLine($"FAIL {name}: {inner.GetType().Name}: {inner.Message}");
            }
            finally { if (Account.SignedIn) Account.SignOut(); }
        }
        Console.WriteLine(failed == 0 ? "ALL OK" : $"{failed} FAILED");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Служебные ручки эмулятора: очистка, запись в обход правил, письма со ссылками.</summary>
    public static class Emu
    {
        static string Host => Firebase.Emulator ?? throw new InvalidOperationException("no emulator");
        static string Project => Firebase.ProjectId;

        static async Task<JsonNode?> Call(HttpMethod method, string url, JsonNode? body = null, bool owner = false)
        {
            using var request = new HttpRequestMessage(method, url);
            if (owner) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "owner");
            if (body is not null) request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            using var response = await Http.Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception($"{(int)response.StatusCode} {url}: {text}");
            return text.Length == 0 ? null : JsonNode.Parse(text);
        }

        /// <summary>Пустая база и ни одного аккаунта.</summary>
        public static async Task Reset()
        {
            await Call(HttpMethod.Delete, $"http://{Host}:8080/emulator/v1/projects/{Project}/databases/(default)/documents");
            await Call(HttpMethod.Delete, $"http://{Host}:9099/emulator/v1/projects/{Project}/accounts");
        }

        /// <summary>Записать документ как владелец (правила не проверяются) — подготовка данных.</summary>
        public static Task Seed(string path, Dictionary<string, object?> fields) =>
            Call(HttpMethod.Patch, $"http://{Host}:8080/v1/{Firebase.Doc(path)}", new JsonObject { ["fields"] = Firebase.ToFields(fields) }, owner: true);

        /// <summary>Прочитать документ как владелец.</summary>
        public static async Task<Dictionary<string, object?>?> Read(string path)
        {
            try { return Firebase.FromFields((await Call(HttpMethod.Get, $"http://{Host}:8080/v1/{Firebase.Doc(path)}", owner: true))?["fields"] as JsonObject); }
            catch (Exception e) when (e.Message.StartsWith("404")) { return null; }
        }

        /// <summary>Последнее «письмо» эмулятора нужного типа для почты: VERIFY_EMAIL, PASSWORD_RESET.</summary>
        public static async Task<string> OobCode(string email, string type)
        {
            var codes = await Call(HttpMethod.Get, $"http://{Host}:9099/emulator/v1/projects/{Project}/oobCodes");
            return codes?["oobCodes"]?.AsArray().LastOrDefault(c => c.Str("email") == email && c.Str("requestType") == type).Str("oobCode")
                ?? throw new Exception($"no {type} mail for {email}");
        }

        /// <summary>Нажать ссылку из письма «подтвердите почту».</summary>
        public static async Task VerifyEmail(string email) =>
            await Call(HttpMethod.Post, $"{Firebase.IdentityBase()}/accounts:update?key=emulator", new JsonObject { ["oobCode"] = await OobCode(email, "VERIFY_EMAIL") });

        /// <summary>Нажать ссылку из письма «сбросить пароль» и задать новый.</summary>
        public static async Task ResetPassword(string email, string newPassword) =>
            await Call(HttpMethod.Post, $"{Firebase.IdentityBase()}/accounts:resetPassword?key=emulator",
                new JsonObject { ["oobCode"] = await OobCode(email, "PASSWORD_RESET"), ["newPassword"] = newPassword });
    }

    /// <summary>Новый пользователь с подтверждённой почтой, вошедший в программу.</summary>
    public static async Task<string> NewUser(string name, string email, string password = "correct-horse-1")
    {
        if (Account.SignedIn) Account.SignOut();
        await Account.SignUp(name, email, password);
        await Emu.VerifyEmail(email);
        await Account.RefreshProfile();
        if (!Account.Get().Verified) throw new Exception("email not verified after the link");
        return Account.Uid!;
    }

    static async Task SignInAs(string email, string password = "correct-horse-1")
    {
        if (Account.SignedIn) Account.SignOut();
        await Account.SignIn(email, password);
    }

    static async Task<string> Denied(Func<Task> action)
    {
        try { await action(); }
        catch (ServiceError e) when (e.Code is "DENIED") { return "denied"; }
        throw new Exception("the server accepted a forbidden write");
    }

    static void Expect(bool ok, string what) { if (!ok) throw new Exception(what); }

    // ---------------------------------------------------------------- аккаунт

    [IntegrationTest]
    static async Task<string> AccountLifecycle()
    {
        const string mail = "new.player@example.test";
        // Регистрация: аккаунт создан, письмо с подтверждением ушло, почта ещё не подтверждена.
        await Account.SignUp("Новичок", mail, "first-pass-1");
        Expect(Account.SignedIn && !Account.Get().Verified, "fresh account must be signed in and unverified");
        await Emu.VerifyEmail(mail);
        await Account.RefreshProfile();
        Expect(Account.Get().Verified, "verified flag after the link");
        Expect(Account.Get().Name == "Новичок", "display name kept: " + Account.Get().Name);

        // Повторная регистрация на ту же почту — понятная ошибка, а не второй аккаунт.
        Account.SignOut();
        try { await Account.SignUp("Двойник", mail, "other-pass-1"); throw new Exception("duplicate email accepted"); }
        catch (ServiceError e) when (e.Code == "EMAIL_EXISTS") { }

        // Неверный пароль — отказ, программа не «входит».
        try { await Account.SignIn(mail, "wrong-pass-1"); throw new Exception("wrong password accepted"); }
        catch (ServiceError e) when (e.Code is "WRONG_LOGIN") { }
        Expect(!Account.SignedIn, "signed in after a wrong password");

        // Сброс пароля по письму, вход новым паролем, старый больше не подходит.
        await Account.ResetPassword(mail);
        await Emu.ResetPassword(mail, "second-pass-2");
        try { await Account.SignIn(mail, "first-pass-1"); throw new Exception("old password still works"); }
        catch (ServiceError e) when (e.Code is "WRONG_LOGIN") { }
        await Account.SignIn(mail, "second-pass-2");
        Expect(Account.SignedIn && Account.Get().Verified, "sign in with the new password");

        // Переименование и удаление аккаунта (с паролем), после удаления войти нельзя.
        await Account.Rename("Ветеран");
        Expect(Account.Get().Name == "Ветеран", "rename");
        await Account.Delete("second-pass-2");
        Expect(!Account.SignedIn, "still signed in after delete");
        try { await Account.SignIn(mail, "second-pass-2"); throw new Exception("deleted account signs in"); }
        catch (ServiceError e) when (e.Code is "WRONG_LOGIN" or "DISABLED") { }
        return "sign up, verify, duplicate, wrong password, reset, rename, delete";
    }

    [IntegrationTest]
    static async Task<string> AccountRejectsBadInput()
    {
        var bad = 0;
        foreach (var (name, mail, pass, code) in new[]
        {
            ("Имя", "not-an-email", "long-enough-1", "BAD_EMAIL"),
            ("Имя", "ok@example.test", "short", "WEAK_PASSWORD"),
            ("", "ok2@example.test", "long-enough-1", "NO_NAME"),
        })
        {
            try { await Account.SignUp(name, mail, pass); throw new Exception($"accepted {mail}/{pass}"); }
            catch (ServiceError e) when (e.Code == code) { bad++; }
        }
        Expect(!Account.SignedIn, "signed in after rejected input");
        return $"{bad} bad sign-ups rejected before reaching the server";
    }

    // ---------------------------------------------------------------- деньги

    /// <summary>Площадка с комиссией 10 %, админ (почта в admins), продавец с лотом и покупатель.</summary>
    static async Task<(string Admin, string Seller, string Buyer, Listing Lot)> Shop(long price)
    {
        await Emu.Seed("config/market", new() { ["fee"] = 10L });
        var admin = await Step("admin sign up", () => NewUser("Админ", "admin@example.test"));
        await Emu.Seed("admins/admin@example.test", new() { ["note"] = "integration" });
        await Step("admin check", () => Account.RefreshProfile());
        Expect(Account.Get().Admin, "admin flag from admins/");

        var seller = await Step("seller sign up", () => NewUser("Автор", "seller@example.test"));
        var file = Path.Combine(Paths.DataDir, "lot.zip");
        File.WriteAllBytes(file, [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);
        var id = await Step("listing save", () => Market.Save(new ListingDraft { Title = "Тестовая модель", Summary = "Проверка", Kind = "model", Price = price, File = file }, null));
        var lot = await Step("listing read", () => Market.Get(id)) ?? throw new Exception("listing not readable after save");
        Expect(lot.Price == price && lot.Active, "listing price/active");

        var buyer = await Step("buyer sign up", () => NewUser("Покупатель", "buyer@example.test"));
        return (admin, seller, buyer, lot);
    }

    /// <summary>Шаг проверки с именем — в отчёте видно, на чём споткнулось.</summary>
    static async Task<T> Step<T>(string name, Func<Task<T>> body)
    {
        try { return await body(); }
        catch (Exception e) when (e is not StepError) { throw new StepError($"{name}: {(e is ServiceError s ? s.Code + " " : "")}{e.Message}"); }
    }

    static async Task Step(string name, Func<Task> body) => await Step(name, async () => { await body(); return 0; });

    sealed class StepError(string message) : Exception(message);

    [IntegrationTest]
    static async Task<string> PurchaseFlow()
    {
        var (_, seller, buyer, lot) = await Shop(10_000);

        // Витрина: покупатель видит лот в общем списке, скрытые чужие — нет.
        var shelf = await Step("storefront", () => Market.All(force: true));
        Expect(shelf.Any(l => l.Id == lot.Id), "listing missing on the storefront");

        // Без денег купить нельзя.
        try { await Market.Buy(lot); throw new Exception("bought with empty wallet"); }
        catch (ServiceError e) when (e.Code == "NO_MONEY") { }

        // Пополнение: заявка → админ зачисляет.
        await Market.RequestTopUp(25_000, "ML-TEST-0001");
        await SignInAs("admin@example.test");
        var pending = await Market.PendingTopUps();
        Expect(pending.Count == 1 && pending[0].Amount == 25_000, "pending top-up visible to admin");
        await Market.ResolveTopUp(pending[0], ok: true);

        // Покупка: баланс −цена, лот в библиотеке, файл скачивается.
        await SignInAs("buyer@example.test");
        Expect((await Market.MyWallet()).Balance == 25_000, "balance after top-up");
        await Market.Buy(lot, expectedPrice: 10_000);
        var wallet = await Market.MyWallet();
        Expect(wallet.Balance == 15_000 && wallet.Spent == 10_000, $"balance after purchase: {wallet}");
        Market.Owned.Clear();
        Expect(await Market.Bought(lot), "purchase recorded");
        Expect((await Market.Purchases()).Any(p => p.ListingId == lot.Id), "purchase in library");
        var downloaded = await Market.Download(lot);
        Expect(File.Exists(downloaded), "bought file downloads");
        try { await Market.Buy(lot); throw new Exception("bought twice"); }
        catch (ServiceError e) when (e.Code == "OWNED") { }

        // Продавец забирает выручку за вычетом комиссии 10 %.
        await SignInAs("seller@example.test");
        var claimed = await Market.ClaimSales();
        var sellerWallet = await Market.MyWallet();
        Expect(claimed.Amount == 9_000 && sellerWallet.Balance == 9_000 && sellerWallet.Earned == 9_000, $"seller net: {claimed} {sellerWallet}");
        Expect((await Market.ClaimSales()).Count == 0, "sale claimed twice");
        return "top-up → admin → buy 100 ₽ → library + download → seller gets 90 ₽, no double claim";
    }

    [IntegrationTest]
    static async Task<string> MoneyCannotBeForged()
    {
        var (_, _, buyer, lot) = await Shop(10_000);
        var (_, token, _) = await Hub.Member();
        var checks = new List<string>();

        // Записать себе баланс напрямую.
        checks.Add("own balance " + await Denied(() => Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"wallets/{buyer}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["balance"] = 1_000_000L, ["seq"] = 1L }) },
        }], token)));

        // Запись «topup» в свой журнал без админа.
        checks.Add("self top-up " + await Denied(() => Ledger.Apply(buyer, "topup", 50_000, token, buyer)));

        // «Грант» самому себе.
        checks.Add("self grant " + await Denied(() => Ledger.Apply(buyer, "grant", 50_000, token, buyer, note: "free money")));

        // Купить без списания: продажа и библиотека без записи в журнал.
        checks.Add("sale without paying " + await Denied(() => Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject
            {
                ["name"] = Firebase.Doc($"market/{lot.Id}/sales/{buyer}"),
                ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["buyerName"] = "x", ["seller"] = lot.Uid, ["title"] = lot.Title, ["price"] = 10_000L, ["fee"] = 1_000L, ["claimed"] = false }),
            },
            ["updateTransforms"] = new JsonArray(Hub.ServerTime("created")),
            ["currentDocument"] = new JsonObject { ["exists"] = false },
        }], token)));

        // Подтвердить своё пополнение самому (это может только админ).
        await Market.RequestTopUp(5_000, "ML-TEST-0002");
        var mine = (await Market.MyTopUps()).Single();
        checks.Add("approve own top-up " + await Denied(() => Market.ResolveTopUp(mine, ok: true)));

        // Сделать себя админом.
        checks.Add("make self admin " + await Denied(() => Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc("admins/buyer@example.test"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["note"] = "me" }) },
        }], token)));

        // Поднять комиссию площадки или поменять реквизиты.
        checks.Add("change fee " + await Denied(() => Hub.Commit([new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc("config/market"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["fee"] = 0L }) },
        }], token)));

        var wallet = await Market.MyWallet();
        Expect(wallet.Balance == 0, "balance changed: " + wallet);
        return string.Join(", ", checks);
    }

    [IntegrationTest]
    static async Task<string> SupportAuthorFlow()
    {
        var (_, seller, _, _) = await Shop(10_000);
        await Step("top-up", () => Market.RequestTopUp(20_000, "ML-TEST-0004"));
        await SignInAs("admin@example.test");
        await Step("approve", async () => await Market.ResolveTopUp((await Market.PendingTopUps()).Single(), ok: true));

        // Игрок поддерживает автора 50 ₽ и ModLaunch 30 ₽.
        await SignInAs("buyer@example.test");
        try { await Tips.Give(seller, "Автор", Tips.Min - 1, ""); throw new Exception("tip below minimum accepted"); }
        catch (ServiceError e) when (e.Code == "INVALID") { }
        await Step("tip author", () => Tips.Give(seller, "Автор", 5_000, "Спасибо за мод!"));
        await Step("tip platform", () => Tips.Give(Tips.Platform, "ModLaunch", 3_000, ""));
        var w = await Market.MyWallet();
        Expect(w.Balance == 12_000 && w.Spent == 8_000, "giver wallet: " + w);

        // Автор забирает поддержку за вычетом комиссии 10 %, один раз.
        await SignInAs("seller@example.test");
        var got = await Step("claim tips", () => Tips.Claim());
        Expect(got.Amount == 4_500 && (await Market.MyWallet()).Balance == 4_500, $"author got {got}");
        Expect((await Tips.Claim()).Count == 0, "tips claimed twice");

        // Админ забирает долю площадки: 5 ₽ комиссии с автора + 30 ₽ поддержки ModLaunch.
        await SignInAs("admin@example.test");
        var fees = await Step("platform fees", async () => await Tips.TakeFees(await Tips.PendingFees()));
        Expect(fees.Amount == 3_500, $"platform fees {fees}");
        return "tip 50 ₽ + 30 ₽ to ModLaunch → author gets 45 ₽ once, platform 35 ₽; below-minimum refused";
    }

    [IntegrationTest]
    static async Task<string> PayoutFlow()
    {
        var (_, seller, _, lot) = await Shop(Market.MinPayout * 2);
        await Step("top-up request", () => Market.RequestTopUp(Market.MinPayout * 2, "ML-TEST-0003"));
        await SignInAs("admin@example.test");
        await Step("top-up approve", async () => await Market.ResolveTopUp((await Market.PendingTopUps()).Single(), ok: true));
        await SignInAs("buyer@example.test");
        await Step("buy", () => Market.Buy(lot));

        await SignInAs("seller@example.test");
        await Step("claim", () => Market.ClaimSales());
        var before = (await Market.MyWallet()).Balance;
        try { await Market.RequestPayout(before + 1, "card 4111 1111 1111 1111"); throw new Exception("payout above balance"); }
        catch (ServiceError e) when (e.Code is "NO_MONEY") { }
        await Step("payout 1", () => Market.RequestPayout(Market.MinPayout, "card 4111 1111 1111 1111"));
        Expect((await Market.MyWallet()).Balance == before - Market.MinPayout, "payout reserved");

        // Админ отклоняет — деньги возвращаются; вторая заявка — выплачена.
        await SignInAs("admin@example.test");
        await Step("payout reject", async () => await Market.ResolvePayout((await Market.PendingPayouts()).Single(), paid: false, "wrong card"));
        await SignInAs("seller@example.test");
        Expect((await Market.MyWallet()).Balance == before, "rejected payout returned");
        await Step("payout 2", () => Market.RequestPayout(Market.MinPayout, "card 4111 1111 1111 1111"));
        await SignInAs("admin@example.test");
        await Step("payout paid", async () => await Market.ResolvePayout((await Market.PendingPayouts()).Single(), paid: true));
        await SignInAs("seller@example.test");
        var w = await Market.MyWallet();
        Expect(w.Balance == before - Market.MinPayout && w.PaidOut == Market.MinPayout, "paid out: " + w);
        Expect(Ledger.Consistent(await Market.MyLedger(), new WalletState(w.Balance, w.Earned, w.Spent, w.PaidOut, w.ToppedUp, w.Seq)), "ledger adds up");
        return "payout above balance refused, rejected payout returned, paid payout recorded, ledger adds up";
    }
}
