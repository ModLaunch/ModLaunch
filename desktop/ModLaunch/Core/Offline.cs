using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ModLaunch.Core;

/// <summary>Офлайн-режим: сеть выключена по желанию человека (самолёт, лимит трафика). Всё, что лежит на диске, работает как обычно.</summary>
public sealed class OfflineException() : HttpRequestException("offline mode");

/// <summary>
/// Офлайн-режим. 11.0: включается сам, когда интернета нет (кабель, Wi-Fi, провайдер), и сам выключается,
/// когда сеть вернулась, — ничего не ждёт таймаутов и не сыплет ошибками. Признаки: Windows сообщила о смене
/// сети, запрос упал с «нет сети» или «имя не найдено». Тогда — короткая проверка двух адресов; пока
/// сети нет, проверка повторяется раз в 15 секунд. Ручной офлайн (самолёт, лимит трафика) остался.
/// </summary>
public static class Offline
{
    /// <summary>Офлайн, включённый вручную.</summary>
    public static bool Manual => Settings.Data.Bool("offlineMode");
    /// <summary>Включать офлайн самому, когда сети нет (по умолчанию да).</summary>
    public static bool Auto => Settings.Data.Bool("offlineAuto", true);
    /// <summary>Проверка показала: интернета нет.</summary>
    public static bool Detected { get; private set; }
    public static bool On => Manual || (Auto && Detected);
    /// <summary>Когда сеть пропала (для строки «нет сети 5 мин»).</summary>
    public static DateTime? Since { get; private set; }

    /// <summary>Режим поменялся (сам или вручную) — строка состояния и страницы перерисовываются.</summary>
    public static event Action? Changed;

    public static void Set(bool value)
    {
        Settings.Data["offlineMode"] = value;
        Settings.Save();
        Changed?.Invoke();
        AppState.Notify();
    }

    public static void SetAuto(bool value)
    {
        Settings.Data["offlineAuto"] = value;
        Settings.Save();
        if (!value && Detected) Mark(false);
        else if (value) _ = Probe();
        Changed?.Invoke();
        AppState.Notify();
    }

    /// <summary>Локальные адреса (nxm, внутренние страницы) в офлайне разрешены.</summary>
    public static bool IsLocal(Uri? uri) => uri is null || uri.IsLoopback;

    /// <summary>Обёртка над сетью: в офлайн-режиме отказывает сразу, а не ждёт таймаут; «нет сети» — повод проверить.</summary>
    public sealed class Gate(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (On && !IsLocal(request.RequestUri)) throw new OfflineException();
            try { return await base.SendAsync(request, cancellationToken); }
            catch (HttpRequestException e) when (!IsLocal(request.RequestUri) && LooksOffline(e))
            {
                Suspect();
                throw;
            }
        }
    }

    /// <summary>Ошибка похожа на «нет интернета», а не на сбой одного сайта.</summary>
    static bool LooksOffline(Exception e) => e.InnerException is SocketException s && s.SocketErrorCode is
        SocketError.HostNotFound or SocketError.NetworkUnreachable or SocketError.NetworkDown or SocketError.HostUnreachable or SocketError.TryAgain or SocketError.NoData;

    static readonly HttpClient ProbeClient = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(4) }) { Timeout = TimeSpan.FromSeconds(6) };
    static readonly string[] ProbeUrls = ["https://www.gstatic.com/generate_204", "https://cloudflare.com/cdn-cgi/trace"];
    static int _probing;
    static Timer? _retry;
    static bool _watching;

    /// <summary>Следить за сетью: сообщения Windows о смене сети и первая проверка при старте.</summary>
    public static void StartWatching()
    {
        if (_watching) return;
        _watching = true;
        try
        {
            NetworkChange.NetworkAvailabilityChanged += (_, e) => { if (!e.IsAvailable) Mark(true); else _ = Probe(); };
            NetworkChange.NetworkAddressChanged += (_, _) => _ = Probe();
        }
        catch { }
        _ = Probe();
    }

    /// <summary>Запрос упал «без сети» — проверить (не чаще одной проверки за раз).</summary>
    public static void Suspect() { if (Auto && !Detected) _ = Probe(); }

    /// <summary>Есть ли интернет: хватает ответа любого из двух адресов. Сама переключает режим.</summary>
    public static async Task<bool> Probe()
    {
        if (Interlocked.Exchange(ref _probing, 1) == 1) return !Detected;
        try
        {
            var online = false;
            try { online = NetworkInterface.GetIsNetworkAvailable(); } catch { online = true; }
            if (online)
            {
                online = false;
                foreach (var url in ProbeUrls)
                {
                    try
                    {
                        using var r = await ProbeClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        online = true;
                        break;
                    }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
                }
            }
            Mark(!online);
            return online;
        }
        finally { Interlocked.Exchange(ref _probing, 0); }
    }

    static void Mark(bool offline)
    {
        if (offline && !Auto) return;
        if (offline)
        {
            // Пока сети нет — проверять раз в 15 секунд, чтобы вернуться в сеть самим.
            _retry ??= new Timer(_ => _ = Probe(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }
        else { _retry?.Dispose(); _retry = null; }
        if (Detected == offline) return;
        Detected = offline;
        Since = offline ? DateTime.UtcNow : null;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Changed?.Invoke();
            AppState.Notify();
        });
    }

    /// <summary>Для снимков экрана: показать, как выглядит авто-офлайн.</summary>
    public static void Demo(bool offline) { Detected = offline; Since = offline ? DateTime.UtcNow.AddMinutes(-3) : null; Changed?.Invoke(); }

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

    [SelfTest]
    static string AutoOfflineFollowsNetwork()
    {
        var was = Detected;
        try
        {
            Detected = true;
            if (Auto && !On) throw new Exception("detected outage did not switch offline");
            Detected = false;
            if (!Manual && On) throw new Exception("still offline after the network came back");
            if (!LooksOffline(new HttpRequestException("x", new SocketException((int)SocketError.HostNotFound)))) throw new Exception("DNS failure not recognised");
            if (LooksOffline(new HttpRequestException("500"))) throw new Exception("server error counted as offline");
        }
        finally { Detected = was; }
        return "auto offline turns on with the outage and off when the network is back";
    }
}
