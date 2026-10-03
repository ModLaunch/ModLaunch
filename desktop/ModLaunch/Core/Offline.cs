namespace ModLaunch.Core;

/// <summary>Офлайн-режим: сеть выключена по желанию человека (самолёт, лимит трафика). Всё, что лежит на диске, работает как обычно.</summary>
public sealed class OfflineException() : HttpRequestException("offline mode");

public static class Offline
{
    public static bool On => Settings.Data.Bool("offlineMode");

    public static void Set(bool value)
    {
        Settings.Data["offlineMode"] = value;
        Settings.Save();
        AppState.Notify();
    }

    /// <summary>Локальные адреса (nxm, внутренние страницы) в офлайне разрешены.</summary>
    public static bool IsLocal(Uri? uri) => uri is null || uri.IsLoopback;

    /// <summary>Обёртка над сетью: в офлайн-режиме отказывает сразу, а не ждёт таймаут.</summary>
    public sealed class Gate(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (On && !IsLocal(request.RequestUri)) throw new OfflineException();
            return base.SendAsync(request, cancellationToken);
        }
    }

    [SelfTest]
    static string BlocksOnlyWhenOn()
    {
        var was = Settings.Data["offlineMode"];
        try
        {
            Settings.Data["offlineMode"] = true;
            using var client = new HttpClient(new Gate(new HttpClientHandler()));
            try { client.GetAsync("https://example.invalid/x").GetAwaiter().GetResult(); throw new Exception("request went out"); }
            catch (OfflineException) { }
            if (!IsLocal(new Uri("http://127.0.0.1:5000/a"))) throw new Exception("localhost blocked");
            if (IsLocal(new Uri("https://nexusmods.com"))) throw new Exception("internet counted as local");
        }
        finally { Settings.Data["offlineMode"] = was; }
        return "offline refuses internet at once, allows localhost";
    }
}
