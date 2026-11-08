using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Features;

/// <summary>
/// 25.0: ModLaunch Premium. Принцип из плана: ничего бесплатного не закрываем — Premium даёт оформление,
/// расширенную статистику и знак поддержки проекта. Покупка — с кошелька ModLaunch, как любой лот рынка:
/// владелец создаёт лот «ModLaunch Premium» и выбирает его в «Комиссии» (config/market.premium) — так
/// работают уже проверенные правила оплаты, комиссия и журнал кошелька. Premium — навсегда, без подписки.
/// </summary>
public static class Premium
{
    /// <summary>Юбилейная цена 25.0 — 299 ₽ навсегда (копейки, как в кошельке).</summary>
    public const long Price = 29_900;
    /// <summary>Обычная цена после юбилея.</summary>
    public const long RegularPrice = 49_900;

    /// <summary>Что даёт Premium (ключи строк v25.prem.perk.*).</summary>
    public static readonly (string Id, string Icon)[] Perks =
    [
        ("look", "Palette"), ("frame", "Trophy"), ("crown", "Star"), ("stats", "Chart"), ("forever", "Clock"), ("thanks", "Heart"),
    ];

    /// <summary>Награды, которые открывает Premium.</summary>
    public static readonly Reward[] Rewards = [new("frame", "premium"), new("banner", "premium"), new("title", "premium"), new("badge", "premium")];

    /// <summary>Юбилейный подарок 25.0 — всем, бесплатно.</summary>
    public static readonly Reward[] Jubilee = [new("frame", "jubilee"), new("banner", "jubilee"), new("title", "jubilee")];

    public static event Action? Changed;

    /// <summary>Лот Premium на рынке (из config/market.premium).</summary>
    public static string? ListingId { get; private set; }

    /// <summary>Все лоты, которые когда-либо продавались как Premium (config/market.premiumAll): сменил владелец лот —
    /// купившие раньше остаются с Premium «навсегда».</summary>
    public static IReadOnlyList<string> AllListings { get; private set; } = [];

    static bool? _demo;

    /// <summary>Для снимков экрана: показать как с Premium или без.</summary>
    public static void Demo(bool? on) { _demo = on; Changed?.Invoke(); }

    static JsonObject State => Settings.Data.Obj("premium");

    /// <summary>
    /// Есть ли Premium: куплен этим аккаунтом (запомнено после проверки покупки) или это владелец площадки.
    /// Без входа и на чужом аккаунте Premium нет — отметка в настройках привязана к uid покупателя.
    /// </summary>
    public static bool Active => _demo ?? (Account.SignedIn && Account.Get() is var me
        && (me.Admin || State.Str("listing") is { Length: > 0 } && me.Uid is { } uid && State.Str("uid") == uid));

    public static DateTime? Since => DateTime.TryParse(State.Str("since"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;

    internal static void SetListing(string? id, IEnumerable<string>? all = null)
    {
        ListingId = string.IsNullOrWhiteSpace(id) ? null : id;
        if (all is not null) AllListings = all.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        Changed?.Invoke();
    }

    static bool _watching;

    /// <summary>Вошли, вышли или сменили аккаунт — Premium проверяется заново для этого аккаунта.</summary>
    public static void Watch()
    {
        if (_watching) return;
        _watching = true;
        Account.Changed += () =>
        {
            Market.Owned.Clear();
            OnUi(() => Changed?.Invoke());
            _ = Task.Run(async () => { try { await Refresh(); } catch { } });
        };
    }

    static void Remember(string listing, string uid)
    {
        var s = State;
        if (s.Str("listing") == listing && s.Str("uid") == uid) return;
        if (s.Str("uid") != uid) s.Remove("since");
        s["listing"] = listing;
        s["uid"] = uid;
        s["since"] ??= DateTime.UtcNow.ToString("o");
        Settings.Save();
        Changed?.Invoke();
    }

    /// <summary>Проверить покупку (после входа, на другом ПК, после переустановки).</summary>
    public static async Task Refresh()
    {
        if (Program.Demo || !Account.SignedIn || Account.Get().Uid is not { } uid) return;
        await PayMethods.Load();
        // Свой запомненный лот, нынешний лот Premium и все прежние: купленный когда-то Premium — навсегда.
        var ids = new List<string>();
        if (State.Str("uid") == uid && State.Str("listing") is { Length: > 0 } mine && mine != "demo") ids.Add(mine);
        if (ListingId is { } cur) ids.Add(cur);
        ids.AddRange(AllListings);
        ids = ids.Distinct().ToList();
        if (ids.Count == 0) return;
        try
        {
            string? found = null;
            foreach (var id in ids)
                if (await Market.HasSale(id)) { found = id; break; }
            // Нет покупки на этом аккаунте — снимаем отметку (её не включить правкой настроек).
            OnUi(() => { if (found is not null) Remember(found, uid); else Forget(uid); });
        }
        catch { /* нет сети или правила не пустили — остаётся, как было */ }
    }

    static void OnUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    /// <summary>Снять отметку этого аккаунта (запись другого аккаунта на этом ПК не трогаем).</summary>
    static void Forget(string uid)
    {
        if (State.Str("listing") is null || State.Str("uid") is { } owner && owner != uid) return;
        Settings.Data.Remove("premium");
        Settings.Save();
        Changed?.Invoke();
    }

    /// <summary>Цена сейчас: цена лота владельца, если он выложен, иначе юбилейная.</summary>
    public static async Task<long> CurrentPrice()
    {
        if (ListingId is { } id && !Program.Demo)
            try { if (await Market.Get(id) is { Active: true } l) return l.Price; } catch { }
        return Price;
    }

    /// <summary>
    /// Купить с кошелька. expected — цена, которую человек подтвердил: цена лота с тех пор поменялась —
    /// не покупаем (ServiceError PRICE_CHANGED, в сообщении — новая цена), чтобы не списать больше обещанного.
    /// Нет лота — владелец ещё не открыл продажу.
    /// </summary>
    public static async Task Buy(long expected)
    {
        var uid = Account.Get().Uid ?? "";
        if (Program.Demo) { Remember("demo", uid); return; }
        await PayMethods.Load(force: true);
        var listing = ListingId is { } id ? await Market.Get(id) : null;
        if (listing is null || !listing.Active) throw new ServiceError("PREMIUM_OFF");
        if (!await Market.Bought(listing)) await Market.Buy(listing, expected);
        Remember(listing.Id, uid);
    }

    /// <summary>Сделать лот Premium: владелец выбирает свой лот рынка (его цена — цена Premium). Прежние лоты
    /// запоминаются в premiumAll — их покупатели Premium не теряют.</summary>
    public static async Task SetOffer(string? listingId)
    {
        var all = AllListings.Concat(ListingId is { } cur ? [cur] : []).Concat(listingId is { } next ? [next] : [])
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().TakeLast(20).ToList();
        if (Program.Demo || Program.Screenshot) { SetListing(listingId, all); return; }
        await PayMethods.SaveFields(new Dictionary<string, object?> { ["premium"] = listingId ?? "", ["premiumAll"] = all });
        SetListing(listingId, all);
    }

    /// <summary>«Надеть всё Premium»: золотой акцент, рамка, баннер и титул одной кнопкой.</summary>
    public static void WearAll()
    {
        if (!Active) return;
        var profile = Settings.Data.Obj("profile");
        profile["frame"] = "premium";
        profile["banner"] = "premium";
        profile["title"] = "premium";
        Look.SetAccent("#F0B429");
        Settings.Save();
        Cosmetics.NotifyChanged();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- статистика

    /// <summary>Счётчики для расширенной статистики: исправленные вылеты, коды, обновления.</summary>
    public static void Count(string key, int by = 1)
    {
        if (by <= 0) return;
        // Настройки меняем только в потоке интерфейса — считают и фоновые задачи (обновления модов).
        void Add()
        {
            var stats = Settings.Data.Obj("stats");
            stats[key] = stats.Long(key) + by;
            Settings.Save();
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Add();
        else Avalonia.Threading.Dispatcher.UIThread.Post(Add);
    }

    public static long Counter(string key) => Settings.Data.Obj("stats").Long(key);

    [SelfTest]
    static string PremiumRewardsAndPrice()
    {
        if (Price >= RegularPrice || Price <= 0) throw new Exception("jubilee price must be lower");
        Demo(true);
        try
        {
            var on = Cosmetics.Unlocked();
            if (Rewards.Concat(Jubilee).Any(r => !on.Contains(r.Key))) throw new Exception("premium rewards not unlocked");
            Demo(false);
            var off = Cosmetics.Unlocked();
            if (Rewards.Any(r => off.Contains(r.Key))) throw new Exception("premium rewards open without premium");
            if (Jubilee.Any(r => !off.Contains(r.Key))) throw new Exception("jubilee gift must be free");
        }
        finally { Demo(null); }
        return $"Premium {Price / 100} ₽ (обычно {RegularPrice / 100} ₽): 4 награды только с Premium, юбилейные — всем";
    }
}
