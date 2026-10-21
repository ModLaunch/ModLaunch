using System.Net;
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
/// В ручном офлайне и с выключенным «сам» проверок нет вовсе. Своя сеть (localhost, LAN) не блокируется никогда.
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
        // Пока был ручной офлайн, проверок не было — «нет сети» могло устареть.
        if (!value && Detected) Recheck();
        Changed?.Invoke();
        AppState.Notify();
    }

    public static void SetAuto(bool value)
    {
        Settings.Data["offlineAuto"] = value;
        Settings.Save();
        if (!value) Mark(false);
        else Recheck();
        Changed?.Invoke();
        AppState.Notify();
    }

    /// <summary>Локальные адреса (nxm, внутренние страницы, свой сервер, компьютеры в домашней сети) в офлайне разрешены: это не интернет.</summary>
    public static bool IsLocal(Uri? uri) => uri is null || uri.IsLoopback || IsLocal(uri.IdnHost);

    /// <summary>Адрес в своей сети: localhost, 127.x, 10.x, 172.16–31.x, 192.168.x, 169.254.x, fc00::/7, fe80::/10, имя *.local.</summary>
    public static bool IsLocal(string? host)
    {
        host = host?.Trim().Trim('[', ']').TrimEnd('.');
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>Обёртка над сетью: в офлайн-режиме отказывает сразу, а не ждёт таймаут; «нет сети» — повод проверить.</summary>
    public sealed class Gate(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (On && !IsLocal(request.RequestUri)) throw new OfflineException();
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                // Настоящий ответ из интернета вернее любой проверки: сеть есть.
                if (!IsLocal(request.RequestUri)) Reached();
                return response;
            }
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

    // Не строже основного клиента (Http: соединение до 20 секунд): медленная сеть или сломанный IPv6 — ещё не «нет интернета».
    static readonly HttpClient ProbeClient = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) }) { Timeout = TimeSpan.FromSeconds(25) };
    static readonly string[] ProbeUrls = ["https://www.gstatic.com/generate_204", "https://cloudflare.com/cdn-cgi/trace"];
    static readonly object Lock = new();
    static int _probing;
    /// <summary>Проверок подряд, на которые адреса не ответили.</summary>
    static int _fails;
    static Timer? _retry;
    static bool _watching;

    /// <summary>Проверять сеть самим можно: не ручной офлайн (он обещает ни байта в интернет) и «сам» не выключен.</summary>
    static bool MayProbe => !Manual && Auto;

    /// <summary>Проверка не по кнопке (старт, смена сети, повтор) — только когда она разрешена.</summary>
    static void Recheck() { if (MayProbe) _ = Probe(); }

    /// <summary>Следить за сетью: сообщения Windows о смене сети и первая проверка при старте.</summary>
    public static void StartWatching()
    {
        if (_watching) return;
        _watching = true;
        try
        {
            NetworkChange.NetworkAvailabilityChanged += (_, e) => { if (!e.IsAvailable) Mark(true); else Recheck(); };
            NetworkChange.NetworkAddressChanged += (_, _) => Recheck();
        }
        catch { }
        Recheck();
    }

    /// <summary>Запрос упал «без сети» — проверить (не чаще одной проверки за раз).</summary>
    public static void Suspect() { if (!Detected) Recheck(); }

    /// <summary>Запрос в интернет прошёл: неудачные проверки не в счёт, а «нет сети» устарело.</summary>
    static void Reached() { if (Volatile.Read(ref _fails) > 0 || Detected) Mark(false); }

    /// <summary>Есть ли интернет: хватает ответа любого из двух адресов. Сама переключает режим.</summary>
    public static async Task<bool> Probe()
    {
        if (Interlocked.Exchange(ref _probing, 1) == 1) return !Detected;
        try
        {
            var network = true;
            try { network = NetworkInterface.GetIsNetworkAvailable(); } catch { }
            var online = false;
            if (network)
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
            // Windows говорит «сети нет» — сразу; адреса не ответили — только со второй проверки подряд.
            Mark(!online, twice: network);
            return online;
        }
        finally { Interlocked.Exchange(ref _probing, 0); }
    }

    static void Mark(bool offline, bool twice = false)
    {
        if (offline && !Auto) return;
        lock (Lock)
        {
            // Пока сети нет (или одна проверка уже не прошла) — проверять раз в 15 секунд, чтобы вернуться в сеть самим.
            if (offline) _retry ??= new Timer(_ => Recheck(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
            else { _retry?.Dispose(); _retry = null; }
            _fails = offline ? _fails + 1 : 0;
            if ((twice && _fails < 2) || Detected == offline) return;
            Detected = offline;
            Since = offline ? DateTime.UtcNow : null;
        }
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
            foreach (var lan in new[] { "http://192.168.1.20:8080/", "http://10.0.0.5/", "http://172.20.1.1/", "http://169.254.3.4/", "http://[fe80::1]/", "http://[fd12::5]/", "http://nas.local/" })
                if (!IsLocal(new Uri(lan))) throw new Exception(lan + " blocked");
            if (!IsLocal("192.168.0.10") || !IsLocal("127.0.0.1") || !IsLocal("localhost")) throw new Exception("LAN host blocked");
            if (IsLocal(new Uri("http://172.32.0.1/")) || IsLocal("8.8.8.8") || IsLocal("play.example.com") || IsLocal("")) throw new Exception("internet counted as local");
        }
        finally { Settings.Data["offlineMode"] = was; }
        return "offline refuses internet at once, allows localhost and the home network";
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

    [SelfTest]
    static string AutoOfflineNeedsTwoFailures()
    {
        var (manual, auto, detected) = (Settings.Data["offlineMode"], Settings.Data["offlineAuto"], Detected);
        try
        {
            Settings.Data["offlineMode"] = false;
            Settings.Data["offlineAuto"] = true;
            Mark(false);
            Mark(true, twice: true);
            if (Detected) throw new Exception("one failed check switched offline");
            Reached();
            Mark(true, twice: true);
            if (Detected) throw new Exception("a real answer did not reset the failed checks");
            Mark(true, twice: true);
            if (!Detected) throw new Exception("two failed checks in a row did not switch offline");
            Reached();
            if (Detected) throw new Exception("a real answer did not end auto offline");
            Settings.Data["offlineMode"] = true;
            if (MayProbe) throw new Exception("checks run in manual offline");
            Settings.Data["offlineMode"] = false;
            Settings.Data["offlineAuto"] = false;
            if (MayProbe) throw new Exception("checks run with auto offline off");
        }
        finally
        {
            Mark(false);
            Detected = detected;
            Settings.Data["offlineMode"] = manual;
            Settings.Data["offlineAuto"] = auto;
        }
        return "auto offline needs two failed checks; no checks in manual offline or with auto off";
    }
}
