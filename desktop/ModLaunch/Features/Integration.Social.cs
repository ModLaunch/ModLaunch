using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch;

/// <summary>31.0: интеграционные проверки остальных функций с сервером — так, как их проходят люди.</summary>
public static partial class IntegrationSocial
{
    static void Expect(bool ok, string what) { if (!ok) throw new Exception(what); }

    static async Task<T> Step<T>(string name, Func<Task<T>> body)
    {
        try { return await body(); }
        catch (Exception e) when (!e.Message.StartsWith("step ")) { throw new Exception($"step {name}: {(e is ServiceError s ? s.Code + " " : "")}{e.Message}"); }
    }

    static Task Step(string name, Func<Task> body) => Step(name, async () => { await body(); return 0; });

    static async Task As(string email)
    {
        if (Account.SignedIn) Account.SignOut();
        await Account.SignIn(email, "correct-horse-1");
    }

    [IntegrationTest]
    static async Task<string> FriendsAndChat()
    {
        var anna = await Integration.NewUser("Анна", "anna@example.test");
        var code = await Step("my code", () => Friends.MyCode());
        var boris = await Integration.NewUser("Борис", "boris@example.test");
        var (status, name) = await Step("add by code", () => Friends.Add(code));
        Expect(status == "sent" && name == "Анна", $"add: {status} {name}");

        await As("anna@example.test");
        var view = await Step("refresh", () => Friends.Refresh(force: true));
        Expect(view.Incoming.Any(r => r.Uid == boris), "request not visible to the receiver");
        await Step("accept", () => Friends.Accept(boris));
        view = await Friends.Refresh(force: true);
        Expect(view.Friends.Any(f => f.Uid == boris), "friend not listed after accept");

        await Step("send", () => Friends.Chat.Send(boris, "Привет! Сыграем в Valheim?"));
        await Step("invite", () => Friends.Chat.Invite(boris, "steam:892970", "Valheim"));

        await As("boris@example.test");
        var fresh = await Step("poll", () => Friends.Chat.Poll());
        var messages = Friends.Chat.Messages(anna);
        Expect(fresh >= 2 && messages.Count >= 2, $"chat: {fresh} new, {messages.Count} stored");
        await Step("remove friend", () => Friends.Remove(anna));
        view = await Friends.Refresh(force: true);
        Expect(view.Friends.All(f => f.Uid != anna), "friend still listed after removal");
        return "code → request → accept → chat + game invite delivered → remove";
    }

    [IntegrationTest]
    static async Task<string> RefundFlow()
    {
        await Integration.Emu.Seed("config/market", new() { ["fee"] = 10L });
        await Integration.NewUser("Админ", "admin@example.test");
        await Integration.Emu.Seed("admins/admin@example.test", new() { ["note"] = "integration" });
        await Integration.NewUser("Автор", "seller@example.test");
        var file = Path.Combine(Paths.DataDir, "refund-lot.zip");
        File.WriteAllBytes(file, [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);
        var id = await Step("listing", () => Market.Save(new ListingDraft { Title = "Скрипт погоды", Kind = "script", Price = 15_000, File = file }, null));
        var buyer = await Integration.NewUser("Покупатель", "buyer@example.test");
        await Market.RequestTopUp(15_000, "ML-TEST-0005");
        await As("admin@example.test");
        await Market.ResolveTopUp((await Market.PendingTopUps()).Single(), ok: true);
        await As("buyer@example.test");
        var lot = (await Market.Get(id))!;
        await Step("buy", () => Market.Buy(lot));
        var purchase = (await Market.Purchases()).Single(p => p.ListingId == id);
        try { await Market.RequestRefund(purchase, "нет"); throw new Exception("short reason accepted"); }
        catch (ServiceError e) when (e.Code == "REFUND_REASON") { }
        await Step("request refund", () => Market.RequestRefund(purchase, "Скрипт не запускается на моей версии игры, проверил дважды."));

        await As("admin@example.test");
        var pending = await Step("pending refunds", () => Market.PendingRefunds());
        Expect(pending.Count == 1 && pending[0].Price == 15_000, "refund not visible to admin");
        await Step("approve refund", () => Market.ResolveRefund(pending[0], ok: true, "ok"));

        await As("buyer@example.test");
        var w = await Market.MyWallet();
        Expect(w.Balance == 15_000, "money not returned: " + w);
        Market.Owned.Clear();
        try { await Market.Download(lot); throw new Exception("refunded file still downloads"); }
        catch (ServiceError) { }
        await As("seller@example.test");
        Expect((await Market.ClaimSales()).Amount == 0, "seller claimed a refunded sale");
        return "buy 150 ₽ → short reason refused → refund request → admin approves → money back, file closed, seller gets nothing";
    }

    [IntegrationTest]
    static async Task<string> PremiumPurchase()
    {
        await Integration.Emu.Seed("config/market", new() { ["fee"] = 10L });
        await Integration.NewUser("Админ", "admin@example.test");
        await Integration.Emu.Seed("admins/admin@example.test", new() { ["note"] = "integration" });
        await Account.RefreshProfile();
        var file = Path.Combine(Paths.DataDir, "premium.zip");
        File.WriteAllBytes(file, [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);
        var id = await Step("premium listing", () => Market.Save(new ListingDraft { Title = "ModLaunch Premium", Kind = "pack", Price = 29_900, File = file }, null));
        await Step("set offer", () => Premium.SetOffer(id));

        await Integration.NewUser("Игрок", "player@example.test");
        await Market.RequestTopUp(30_000, "ML-TEST-0006");
        await As("admin@example.test");
        await Market.ResolveTopUp((await Market.PendingTopUps()).Single(), ok: true);
        await As("player@example.test");
        Expect(!Premium.Active, "premium active before purchase");
        var price = await Step("price", () => Premium.CurrentPrice());
        Expect(price == 29_900, "price " + price);
        try { await Premium.Buy(price - 100); throw new Exception("bought at a stale price"); }
        catch (ServiceError e) when (e.Code == "PRICE_CHANGED") { }
        await Step("buy premium", () => Premium.Buy(price));
        Expect(Premium.Active, "premium not active after purchase");
        Expect((await Market.MyWallet()).Balance == 100, "wallet after premium");
        return "owner sets Premium lot → player tops up → stale price refused → buys 299 ₽ → Premium active";
    }

    [IntegrationTest]
    static async Task<string> CuratorsFlow()
    {
        await Integration.NewUser("Куратор", "curator@example.test");
        var me = await Step("save profile", () => Curators.SaveProfile("Куратор", "Пишу про выживание", null));
        var game = new LibGame("steam:264710", "steam", "Subnautica", null, 264710, true, 0);
        await Step("save pick", () => Curators.SavePick(game, "yes", "Лучшее подводное выживание.", isNew: true));

        await Integration.NewUser("Читатель", "reader@example.test");
        var curator = await Step("get", () => Curators.Get(me.Uid)) ?? throw new Exception("curator not found");
        await Step("follow", () => Curators.Follow(curator));
        Expect((await Curators.Get(me.Uid))!.Followers == 1, "followers after follow");
        var picks = await Step("for game", () => Curators.ForGame(GameReviews.Key(game)));
        Expect(picks.Any(p => p.Pick.Verdict == "yes"), "pick not shown on the game page");
        await Step("unfollow", () => Curators.Unfollow(me.Uid));
        Expect((await Curators.Get(me.Uid))!.Followers == 0, "followers after unfollow");
        return "profile + pick → follow (+1) → pick on game page → unfollow (−1)";
    }

    [IntegrationTest]
    static async Task<string> ShareCodeRoundTrip()
    {
        await Integration.NewUser("Сборщик", "packer@example.test");
        var pack = new SharePack("lethal-company", "Lethal Company", "Моя сборка",
            [new ShareMod("notnotnotswipez-MoreCompany", "MoreCompany", "1.9.1", "thunderstore", true)]);
        var code = await Step("upload", () => ShareCode.Upload(pack));
        Expect(code.Length == 8 && !code.StartsWith("ML1-"), "short server code expected, got " + code);
        Account.SignOut();
        var back = await Step("fetch", () => ShareCode.Fetch(code));
        Expect(back.Name == "Моя сборка" && back.Mods.Single().Version == "1.9.1", "pack changed on the way");
        return $"short code {code} uploaded and read back without an account";
    }

    [IntegrationTest]
    static async Task<string> GameReviewRoundTrip()
    {
        await Integration.NewUser("Рецензент", "reviewer@example.test");
        var game = new LibGame("steam:264710", "steam", "Subnautica", null, 264710, true, 0);
        await Step("submit", () => Reviews.Submit(GameReviews.Key(game), GameReviews.Mod, game.Name, 5, "Шедевр.", "Рецензент", played: true));
        await Step("sync", () => Reviews.Sync(force: true));
        Expect(GameReviews.For(game).Any(r => r.Stars == 5), "review not visible after sync");
        await Step("remove", () => GameReviews.Remove(game));
        await Reviews.Sync(force: true);
        Expect(GameReviews.Mine(game) is null, "review still there after remove");
        return "5★ review saved, visible after sync, removed";
    }

    /// <summary>31.0: жалоба → очередь админа → снятие: лот пропал с витрины, не покупается, файл не скачать; вернуть.</summary>
    [IntegrationTest]
    static async Task<string> ReportAndTakedown()
    {
        await Integration.Emu.Seed("config/market", new() { ["fee"] = 10L });
        await Integration.NewUser("Админ", "admin@example.test");
        await Integration.Emu.Seed("admins/admin@example.test", new() { ["note"] = "integration" });
        var seller = await Integration.NewUser("Автор", "seller@example.test");
        var file = Path.Combine(Paths.DataDir, "takedown-lot.zip");
        File.WriteAllBytes(file, [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);
        var id = await Step("listing", () => Market.Save(new ListingDraft { Title = "Подозрительный мод", Kind = "pack", Price = 0, File = file }, null));
        var lot = (await Market.Get(id))!;

        await Integration.NewUser("Игрок", "player@example.test");
        await Step("report", () => Safety.Report("listing", id, seller, lot.Title, "malware", "Защитник Windows ругается на dll"));
        try { await Safety.Report("listing", id, seller, lot.Title, "malware", "ещё раз"); throw new Exception("second report accepted"); }
        catch (ServiceError e) when (e.Code == "REPORT_EXISTS") { }
        try { await Safety.OpenReports(); throw new Exception("player read the report queue"); }
        catch (ServiceError e) when (e.Code is "DENIED") { }

        await As("admin@example.test");
        var open = await Step("queue", () => Safety.OpenReports());
        var report = open.Single(r => r.Target == id);
        Expect(report.Reason == "malware" && report.Owner == seller, "report fields: " + report);
        await Step("take down", () => Safety.TakeDown("listing", id, "malware", "Defender: Trojan", lot.Sha256, lot.Title, seller));
        await Step("resolve", () => Safety.Resolve(report, true, "takedown", "снят"));
        Expect((await Safety.OpenReports()).All(r => r.Target != id), "report still open");
        Expect(Safety.IsBlocked(lot.Sha256), "malware hash not on the blocklist");

        await As("player@example.test");
        await Safety.Refresh(force: true);
        Expect((await Market.All(force: true)).All(l => l.Id != id), "taken-down lot still on the storefront");
        try { await Market.Buy((await Market.Get(id))!); throw new Exception("taken-down lot bought"); }
        catch (ServiceError e) when (e.Code is "DENIED") { }

        await As("seller@example.test");
        try { await Market.Save(new ListingDraft { Title = "Подозрительный мод 2", Kind = "pack", Price = 0, File = file }, null); throw new Exception("blocked file listed again"); }
        catch (ServiceError e) when (e.Code is "BLOCKED_FILE") { }

        await As("admin@example.test");
        await Step("restore", () => Safety.Restore("listing", id));
        await As("player@example.test");
        Expect((await Market.All(force: true)).Any(l => l.Id == id), "restored lot not back");
        return "report (once) → admin queue → takedown + blocklist → hidden, not buyable, file can't be re-listed → restore";
    }

    /// <summary>31.0: блокировка — заблокированный не выставляет товары и не жалуется; разблокировка возвращает.</summary>
    [IntegrationTest]
    static async Task<string> BanBlocksPublishing()
    {
        await Integration.Emu.Seed("config/market", new() { ["fee"] = 10L });
        await Integration.NewUser("Админ", "admin@example.test");
        await Integration.Emu.Seed("admins/admin@example.test", new() { ["note"] = "integration" });
        await Account.RefreshProfile();
        var bad = await Integration.NewUser("Нарушитель", "bad@example.test");
        await As("admin@example.test");
        await Step("ban", () => Safety.BanUser(bad, "Нарушитель", "спам"));
        Expect((await Safety.Bans()).Any(b => b.Uid == bad), "ban not listed");

        await As("bad@example.test");
        Expect(await Safety.MyBan() is not null, "banned user doesn't see own ban");
        var file = Path.Combine(Paths.DataDir, "ban-lot.zip");
        File.WriteAllBytes(file, [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);
        try { await Market.Save(new ListingDraft { Title = "Спам", Kind = "pack", Price = 0, File = file }, null); throw new Exception("banned user listed an item"); }
        catch (ServiceError e) when (e.Code is "DENIED") { }

        await As("admin@example.test");
        await Step("unban", () => Safety.Unban(bad));
        await As("bad@example.test");
        Expect(await Safety.MyBan() is null, "still banned");
        await Step("list after unban", () => Market.Save(new ListingDraft { Title = "Нормальный мод", Kind = "pack", Price = 0, File = file }, null));
        return "banned: can't list items, sees own ban; unbanned: can again";
    }

    /// <summary>31.0: вход с нового ПК — старый ПК видит «это вы?», «это я» снимает вопрос; первый ПК — без вопроса.</summary>
    [IntegrationTest]
    static async Task<string> NewDeviceAlert()
    {
        Settings.Data["deviceId"] = "dfirstpc00000001";
        await Integration.NewUser("Хозяин", "owner@example.test");
        await Step("register first", () => Devices.Register(force: true));
        Expect((await Devices.Alerts()).Count == 0, "first PC raised an alert");
        var first = (await Devices.List()).Single();
        Expect(first.Mine && first.Ack, "first device not acknowledged");

        // Второй ПК: тот же аккаунт, другой id копии программы.
        Account.SignOut();
        Settings.Data["deviceId"] = "dsecondpc0000002";
        await Step("sign in on PC 2", () => Account.SignIn("owner@example.test", "correct-horse-1"));
        Expect((await Devices.List()).Count == 2, "second device not registered");

        // Снова первый ПК: видит новый вход.
        Settings.Data["deviceId"] = "dfirstpc00000001";
        var alerts = await Step("alerts", () => Devices.Alerts());
        Expect(alerts.Count == 1 && alerts[0].Id == "dsecondpc0000002", "no alert for the new PC");
        await Step("ack", () => Devices.Acknowledge(alerts[0]));
        Expect((await Devices.Alerts()).Count == 0, "alert stays after «it's me»");

        // Подбор пароля: после 5 неверных — пауза, даже верный пароль не пускает до её конца.
        Account.SignOut();
        for (var i = 0; i < 5; i++)
            try { await Account.SignIn("owner@example.test", "wrong-pass-" + i); } catch (ServiceError) { }
        try { await Account.SignIn("owner@example.test", "correct-horse-1"); throw new Exception("not locked after 5 wrong passwords"); }
        catch (ServiceError e) when (e.Code == "LOCKED") { }
        Devices.Succeeded("owner@example.test");
        Settings.Data.Remove("deviceId");
        return "first PC silent → PC 2 sign-in → PC 1 asks «it's you?» → ack clears; 5 wrong passwords lock even the right one";
    }

    /// <summary>31.0: сбой с согласия уходит в базу без личного; без согласия — нет; админ видит его в «Сбоях».</summary>
    [IntegrationTest]
    static async Task<string> CrashReportsReachAdmin()
    {
        Exception Boom() { try { throw new InvalidOperationException("integration crash"); } catch (Exception e) { return e; } }
        CrashReports.SetConsent(false);
        CrashReports.Save(Boom(), fatal: true);
        Expect(await CrashReports.Send() == 0, "sent without consent");
        CrashReports.SetConsent(true);
        CrashReports.Save(Boom(), fatal: true);
        CrashReports.Save(Boom(), fatal: true);
        var sent = await Step("send", () => CrashReports.Send());
        Expect(sent == 2 && CrashReports.Pending().Count == 0, $"sent {sent}, left {CrashReports.Pending().Count}");

        await Integration.NewUser("Админ", "admin@example.test");
        await Integration.Emu.Seed("admins/admin@example.test", new() { ["note"] = "integration" });
        await Account.RefreshProfile();
        var top = await Step("top", () => CrashReports.Top());
        Expect(top.Count == 1 && top[0].Count == 2 && top[0].Latest.Message.Contains("integration crash"), "admin view: " + string.Join("; ", top.Select(t => $"{t.Sig} x{t.Count}")));
        Settings.Data.Remove("crashReports");
        return "no consent → nothing sent; consent → 2 reports sent (anonymous), admin sees one signature ×2";
    }
}
