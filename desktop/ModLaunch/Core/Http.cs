using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>
/// Сеть: один HttpClient на всю программу, узнаваемый User-Agent (Thunderstore
/// и GitHub по нему отличают программы от скриптов), JSON и скачивание
/// с прогрессом и проверкой sha256.
/// </summary>
public static class Http
{
    public static readonly string Version = typeof(Http).Assembly.GetName().Version?.ToString(3) ?? "4.0.0";

    public static readonly HttpClient Client = Create();

    static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(new Offline.Gate(handler)) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ModLaunch/{Version}");
        return client;
    }

    public static async Task<string> GetString(string url, CancellationToken ct = default, int timeoutSec = 30)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        using var response = await Client.GetAsync(url, cts.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} — {url}");
        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    public static async Task<JsonNode?> GetJson(string url, CancellationToken ct = default, int timeoutSec = 30, string? accept = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept ?? "application/json"));
        using var response = await Client.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} — {url}");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cts.Token));
    }

    public static async Task<JsonNode?> PostJson(string url, JsonNode body, IDictionary<string, string>? headers = null, CancellationToken ct = default, int timeoutSec = 20)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        foreach (var (k, v) in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(k, v);
        using var response = await Client.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} — {url}");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cts.Token));
    }

    /// <summary>
    /// Для файлов — отдельный клиент без сжатия: иначе байты «докачать с N-го» не совпадут
    /// с байтами на диске.
    /// </summary>
    static readonly HttpClient Files = CreateFiles();

    static HttpClient CreateFiles()
    {
        // Без общего лимита времени: большой файл на медленной связи качается сколько нужно,
        // а зависание ловят свои таймеры (ответ сервера — 45 с, тишина в потоке — 60 с).
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };
        var client = new HttpClient(new Offline.Gate(handler)) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ModLaunch/{Version}");
        return client;
    }

    /// <summary>Недокачанные файлы, которые сейчас пишутся, — второй загрузке того же файла нужен свой.</summary>
    static readonly HashSet<string> Writing = [];
    static bool _partsCleaned;

    /// <summary>
    /// Скачать файл во временную папку. Возвращает путь. Недокачанное лежит в «.part»:
    /// после обрыва, паузы или ошибки загрузка продолжится с того же места (если сервер умеет).
    /// Сеть пропала — ещё три попытки через 1, 2 и 4 секунды.
    /// </summary>
    public static async Task<string> Download(string url, string fileName, IProgress<double>? progress = null, string? sha256 = null, CancellationToken ct = default)
    {
        CleanOldParts();
        var job = Jobs.CurrentJob;
        var part = Reserve(PartName(url, fileName));
        var meta = part + ".meta";
        try
        {
            var failures = 0;
            while (true)
            {
                long gained = 0;
                try
                {
                    var have = File.Exists(part) ? new FileInfo(part).Length : 0;
                    // Докачиваем, только если знаем, что файл на сервере тот же (ETag / Last-Modified):
                    // иначе к старому куску прилипнет новый файл и архив выйдет битым.
                    var validator = have > 0 && File.Exists(meta) ? File.ReadAllText(meta).Trim() : "";
                    if (have > 0 && validator == "") { File.Delete(part); have = 0; }
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (have > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(have, null);
                        request.Headers.TryAddWithoutValidation("If-Range", validator);
                    }

                    HttpResponseMessage response;
                    using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        wait.CancelAfter(TimeSpan.FromSeconds(45));
                        try { response = await Files.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, wait.Token); }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("server did not answer in 45 s"); }
                    }
                    using var answer = response;
                    if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        // Кусок уже целый (дальше сервер отдавать нечего) — готово; иначе начинаем заново.
                        if (response.Content.Headers.ContentRange?.Length is long full && full == have) return Finish(part, meta, fileName, sha256);
                        File.Delete(part);
                        throw new IOException("range not satisfiable");
                    }
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} — {url}", null, response.StatusCode);
                    // Вместо файла пришла веб-страница (вход, капча, ошибка CDN) — не сохраняем её как архив.
                    if (response.Content.Headers.ContentType?.MediaType == "text/html" && !fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        throw new HttpRequestException(I18n.T("dl.gotPage"), null, System.Net.HttpStatusCode.UnsupportedMediaType);

                    var resumed = have > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                    if (!resumed) have = 0;
                    if (!resumed)
                    {
                        var etag = response.Headers.ETag?.ToString() ?? response.Content.Headers.LastModified?.ToString("R") ?? "";
                        if (etag != "" && response.Headers.AcceptRanges.Contains("bytes")) File.WriteAllText(meta, etag);
                        else if (File.Exists(meta)) File.Delete(meta);
                    }
                    var total = response.Content.Headers.ContentLength is long length ? length + have : 0;
                    long received = have;
                    await using (var input = await response.Content.ReadAsStreamAsync(ct))
                    await using (var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 17, true))
                    {
                        var buffer = new byte[1 << 17];
                        var meter = new SpeedMeter(received);
                        var reported = DateTime.MinValue;
                        if (job is not null) { job.Total = total; job.Received = received; }
                        // Минуту ни байта — связь зависла: считаем обрывом и пробуем снова.
                        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        stall.CancelAfter(TimeSpan.FromSeconds(60));
                        while (true)
                        {
                            int read;
                            try { read = await input.ReadAsync(buffer, stall.Token); }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("no data for 60 s"); }
                            if (read <= 0) break;
                            stall.CancelAfter(TimeSpan.FromSeconds(60));
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                            received += read;
                            gained += read;
                            // Интерфейсу хватает 10 обновлений в секунду — чаще только тормозит окно.
                            var now = DateTime.UtcNow;
                            if ((now - reported).TotalMilliseconds >= 100)
                            {
                                reported = now;
                                if (total > 0) progress?.Report((double)received / total);
                                if (job is not null) job.Received = received;
                            }
                            if (job is not null && meter.Tick(received) is double speed) job.Speed = speed;
                        }
                        if (job is not null) job.Received = received;
                        if (total > 0 && received != total) throw new IOException("download cut off");
                    }
                    progress?.Report(1);
                    return Finish(part, meta, fileName, sha256);
                }
                catch (Exception e) when (!ct.IsCancellationRequested && !Hopeless(e))
                {
                    // Пока байты идут, обрывы не считаются: медленная, но живая связь докачает файл.
                    failures = gained > 0 ? 1 : failures + 1;
                    if (failures > 6) throw;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, 1 << (failures - 1))), ct);
                }
            }
        }
        finally
        {
            lock (Writing) Writing.Remove(part);
            if (job is not null) { job.Total = 0; job.Speed = 0; }
        }
    }

    /// <summary>Проверить контрольную сумму и подпись архива, отдать готовый файл.</summary>
    static string Finish(string part, string meta, string fileName, string? sha256)
    {
        void Drop() { try { File.Delete(part); } catch { } try { File.Delete(meta); } catch { } }
        if (sha256 is not null && !string.Equals(Sha256(part), sha256, StringComparison.OrdinalIgnoreCase)) { Drop(); throw new IOException("checksum mismatch"); }
        if (LooksBroken(part, fileName)) { Drop(); throw new IOException(I18n.T("dl.broken")); }
        var target = Path.Combine(Paths.DownloadsTemp, $"{DateTime.UtcNow.Ticks}-{Safe(fileName)}");
        File.Move(part, target);
        try { File.Delete(meta); } catch { }
        return target;
    }

    /// <summary>Архив, который не начинается со своей подписи (zip — PK, 7z, rar), — битый или вовсе не архив.</summary>
    static bool LooksBroken(string file, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".zip" or ".7z" or ".rar" or ".jar" or ".mrpack")) return false;
        var head = new byte[4];
        using var s = File.OpenRead(file);
        if (s.Read(head, 0, 4) < 4) return true;
        return ext switch
        {
            ".7z" => !(head[0] == (byte)'7' && head[1] == (byte)'z'),
            ".rar" => !(head[0] == (byte)'R' && head[1] == (byte)'a' && head[2] == (byte)'r'),
            _ => !(head[0] == (byte)'P' && head[1] == (byte)'K'),
        };
    }

    /// <summary>Ответ «нет такого файла» или «нельзя» — повторять бесполезно.</summary>
    static bool Hopeless(Exception e) => e is OfflineException ||
        e is HttpRequestException { StatusCode: { } code } && (int)code is >= 400 and < 500 && (int)code is not (408 or 429);

    /// <summary>Имя недокачанного файла: одно и то же для той же ссылки (без подписи после «?» — у Nexus она каждый раз новая).</summary>
    public static string PartName(string url, string fileName)
    {
        var stable = url.Split('?')[0] + "|" + fileName;
        var hash = Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(stable)))[..12].ToLowerInvariant();
        return $"{hash}-{Safe(fileName)}.part";
    }

    static string Reserve(string name)
    {
        var path = Path.Combine(Paths.DownloadsTemp, name);
        lock (Writing)
        {
            if (Writing.Add(path)) return path;
            var own = Path.Combine(Paths.DownloadsTemp, $"{DateTime.UtcNow.Ticks}-{name}");
            Writing.Add(own);
            return own;
        }
    }

    /// <summary>Недокачанное старше недели уже не пригодится.</summary>
    static void CleanOldParts()
    {
        if (_partsCleaned) return;
        _partsCleaned = true;
        try
        {
            foreach (var file in Directory.EnumerateFiles(Paths.DownloadsTemp, "*.part"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(7)) try { File.Delete(file); } catch { }
        }
        catch { }
    }

    /// <summary>Скорость загрузки, сглаженная, чтобы цифры не прыгали; обновляется раз в полсекунды.</summary>
    sealed class SpeedMeter(long start)
    {
        long _bytes = start;
        DateTime _at = DateTime.UtcNow;
        double _speed;

        public double? Tick(long received)
        {
            var now = DateTime.UtcNow;
            var seconds = (now - _at).TotalSeconds;
            if (seconds < 0.5) return null;
            var current = (received - _bytes) / seconds;
            _speed = _speed == 0 ? current : _speed * 0.7 + current * 0.3;
            _bytes = received;
            _at = now;
            return _speed;
        }
    }

    [SelfTest]
    static string PartNameIgnoresSignature()
    {
        var a = PartName("https://cf-files.nexusmods.com/cdn/1/12/Map-12-2-1.zip?md5=abc&expires=1", "Map.zip");
        var b = PartName("https://cf-files.nexusmods.com/cdn/1/12/Map-12-2-1.zip?md5=xyz&expires=2", "Map.zip");
        var c = PartName("https://cf-files.nexusmods.com/cdn/1/12/Map-12-2-2.zip", "Map.zip");
        if (a != b) throw new Exception("same file got different .part names");
        if (a == c) throw new Exception("different files share a .part name");
        if (!Hopeless(new HttpRequestException("", null, System.Net.HttpStatusCode.NotFound)) || Hopeless(new HttpRequestException("", null, System.Net.HttpStatusCode.TooManyRequests)))
            throw new Exception("wrong retry rule");
        return a;
    }

    public static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
