using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Features;

public enum ServiceState { Checking, Ok, Warn, Bad }

/// <summary>Результат проверки одной службы: состояние, короткий итог и что сделать.</summary>
public sealed record ServiceStatus(string Id, ServiceState State, string Summary, string? Fix = null, string? Detail = null);

/// <summary>
/// «Состояние сервисов» (8.4): каждая служба программы проверяется настоящим запросом —
/// тем же, что делает экран (отзывы, аккаунты, друзья, Hub, маркет, реклама, обновления).
/// По ответу сервера видно, что именно не подключено: правила базы не опубликованы,
/// нет интернета, кончилась бесплатная квота, не задан адрес.
/// </summary>
public static class Services
{
    public static readonly string[] Ids = ["reviews", "accounts", "friends", "hub", "market", "ads", "updates"];

    public static readonly Dictionary<string, ServiceStatus> Last = [];
    public static event Action<ServiceStatus>? Changed;
    public static DateTime? CheckedAt { get; private set; }

    static void Put(ServiceStatus s)
    {
        Last[s.Id] = s;
        Changed?.Invoke(s);
    }

    /// <summary>Проверить всё параллельно; каждая служба сообщает о себе, как только ответит.</summary>
    public static async Task CheckAll()
    {
        foreach (var id in Ids) Put(new ServiceStatus(id, ServiceState.Checking, I18n.T("svc.checking")));
        await Task.WhenAll(Ids.Select(id => Task.Run(() => CheckOne(id))));
        CheckedAt = DateTime.UtcNow;
    }

    public static async Task CheckOne(string id)
    {
        try { Put(await Check(id)); }
        catch (Exception e) { Put(FromError(id, e)); }
    }

    static async Task<ServiceStatus> Check(string id)
    {
        if (Offline.On) return new ServiceStatus(id, ServiceState.Warn, I18n.T("svc.offlineMode"), I18n.T("svc.fix.offlineMode"));
        switch (id)
        {
            case "reviews":
                if (!Firebase.Configured) return NotConfigured(id);
                await Reviews.Sync(force: true);
                return new ServiceStatus(id, ServiceState.Ok, I18n.T("svc.ok.reviews"));
            case "accounts":
                if (!Firebase.Configured) return NotConfigured(id);
                await Account.Token();
                return new ServiceStatus(id, ServiceState.Ok, Account.SignedIn ? I18n.T("svc.ok.accounts.in") : I18n.T("svc.ok.accounts"));
            case "friends":
                if (!Firebase.Configured) return NotConfigured(id);
                if (!Account.SignedIn) return new ServiceStatus(id, ServiceState.Warn, I18n.T("svc.needAccount"), I18n.T("svc.fix.needAccount"));
                var view = await Friends.Refresh(force: true);
                return new ServiceStatus(id, ServiceState.Ok, I18n.T("svc.ok.friends", ("n", view.Friends.Count)));
            case "hub":
                if (!Firebase.Configured) return NotConfigured(id);
                var mods = await Creator.Hub.All(force: true);
                return new ServiceStatus(id, ServiceState.Ok, I18n.T("svc.ok.hub", ("n", mods.Count)));
            case "market":
                if (!Firebase.Configured) return NotConfigured(id);
                var lots = await Creator.Market.All(force: true);
                return new ServiceStatus(id, ServiceState.Ok, I18n.T("svc.ok.market", ("n", lots.Count)));
            case "ads":
                if (!Ads.Configured) return new ServiceStatus(id, ServiceState.Warn, I18n.T("svc.ads.house"), I18n.T("svc.fix.ads"));
                await Ads.Refresh();
                if (Ads.LastError is { } adsError) return new ServiceStatus(id, ServiceState.Warn, I18n.T("svc.ads.unreachable"), I18n.T("svc.fix.ads.upload", ("url", Ads.FeedUrl!)), adsError);
                return Ads.RemoteCount > 0
                    ? new ServiceStatus(id, ServiceState.Ok, I18n.T("svc.ok.ads", ("n", Ads.RemoteCount)))
                    : new ServiceStatus(id, ServiceState.Ok, I18n.T("svc.ok.ads.empty"));
            case "updates":
                if (!Setup.Updater.Configured) return NotConfigured(id);
                var latest = await Setup.Updater.Check();
                if (latest is null) return new ServiceStatus(id, ServiceState.Warn, I18n.T("svc.upd.none"), I18n.T("svc.fix.updates"));
                return Setup.Updater.Available
                    ? new ServiceStatus(id, ServiceState.Ok, I18n.T("svc.ok.updates.new", ("version", latest.Version)))
                    : new ServiceStatus(id, ServiceState.Ok, I18n.T("svc.ok.updates", ("version", Http.Version), ("source", Setup.Updater.Source)));
        }
        return new ServiceStatus(id, ServiceState.Warn, "?");
    }

    static ServiceStatus NotConfigured(string id) => new(id, ServiceState.Bad, I18n.T("svc.notConfigured"));

    /// <summary>Ошибка сервера → понятный итог и подсказка.</summary>
    public static ServiceStatus FromError(string id, Exception e)
    {
        var code = e is ServiceError s ? s.Code : e is OfflineException ? "OFFLINE" : e is HttpRequestException or TaskCanceledException ? "OFFLINE" : "SERVER";
        var detail = e.Message;
        return code switch
        {
            "SESSION" or "SIGN_IN" => new ServiceStatus(id, ServiceState.Warn, I18n.T("svc.session"), I18n.T("svc.fix.session")),
            "DENIED" => new ServiceStatus(id, ServiceState.Bad, I18n.T("svc.denied"), I18n.T(id is "friends" or "hub" or "market" ? "svc.fix.rules" : "svc.fix.rules.base"), detail),
            "OFFLINE" => new ServiceStatus(id, ServiceState.Warn, I18n.T("svc.offline"), I18n.T("svc.fix.offline"), detail),
            "BUSY" or "TOO_MANY" => new ServiceStatus(id, ServiceState.Warn, I18n.T("svc.busy"), I18n.T("svc.fix.busy"), detail),
            "AUTH_DISABLED" or "NOT_ENABLED" => new ServiceStatus(id, ServiceState.Bad, I18n.T("svc.authOff"), I18n.T("svc.fix.authOff"), detail),
            "NO_DATABASE" => new ServiceStatus(id, ServiceState.Bad, I18n.T("svc.noDb"), I18n.T("svc.fix.noDb"), detail),
            "BAD_KEY" => new ServiceStatus(id, ServiceState.Bad, I18n.T("svc.badKey"), null, detail),
            _ => new ServiceStatus(id, ServiceState.Bad, I18n.T("svc.error"), null, detail),
        };
    }
}
