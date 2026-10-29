using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
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

    /// <summary>
    /// 2.0: номер, который видит человек. Внутренний (Version) остался выше 20 — его сравнивает обновление,
    /// и все, у кого 16.0–20.0, получат новую версию сами. На экране: 21.0.0 → «2.0», 21.0.1 → «2.0.1»,
    /// 21.1.0 → «2.1», 22.0.0 → «3.0». Версии до 21 показываются как были.
    /// </summary>
    public static readonly string Display = DisplayOf(Version);

    public static string DisplayOf(string? version)
    {
        var parts = (version ?? "").TrimStart('v', 'V').Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) || major < 21) return version ?? "";
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        return patch > 0 ? $"{major - 19}.{minor}.{patch}" : $"{major - 19}.{minor}";
    }

    [SelfTest]
    static string DisplayVersionMapsBack()
    {
        foreach (var (inner, shown) in new[] { ("21.0.0", "2.0"), ("21.0.1", "2.0.1"), ("21.1.0", "2.1"), ("22.0.0", "3.0"), ("20.0.0", "20.0.0"), ("16.0.1", "16.0.1") })
            if (DisplayOf(inner) != shown) throw new Exception($"{inner} shows as {DisplayOf(inner)}, want {shown}");
        // Обновление сравнивает внутренние номера: 2.0 (21.0.0) новее 20.0.0 и 16.0.0.
        if (Features.Versions.Compare("21.0.0", "20.0.0") <= 0 || Features.Versions.Compare("21.0.0", "16.0.0") <= 0) throw new Exception("2.0 is not newer than 20.0");
        return "21.0.0 shows as 2.0 and still updates 16–20";
    }

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

    /// <summary>Контрольные суммы файла (любые известные): sha256, sha1 (Modrinth, модпаки), sha512.</summary>
    public sealed record Hashes(string? Sha256 = null, string? Sha1 = null, string? Sha512 = null)
    {
        public bool Any => !string.IsNullOrEmpty(Sha256) || !string.IsNullOrEmpty(Sha1) || !string.IsNullOrEmpty(Sha512);
    }

    /// <summary>Файл не совпал с контрольной суммой — с другого зеркала может прийти правильный.</summary>
    public sealed class HashMismatchException(string file) : IOException($"checksum mismatch: {file}");

    /// <summary>
    /// Скачать файл во временную папку. Возвращает путь. Недокачанное лежит в «.part»:
    /// после обрыва, паузы или ошибки загрузка продолжится с того же места (если сервер умеет).
    /// Сеть пропала — ещё несколько попыток с паузой 1, 2, 4… секунды.
    /// </summary>
    public static Task<string> Download(string url, string fileName, IProgress<double>? progress = null, string? sha256 = null, CancellationToken ct = default) =>
        Download([url], fileName, progress, new Hashes(sha256), ct);

    /// <summary>
    /// 9.4: загрузка с зеркалами — ссылки пробуются по очереди: одна не отвечает, файл битый или
    /// не тот — берём следующую. Большие файлы качаются в несколько потоков (как в менеджерах
    /// загрузок), байты всех загрузок одной задачи складываются в её общую полосу.
    /// </summary>
    public static async Task<string> Download(IReadOnlyList<string> urls, string fileName, IProgress<double>? progress, Hashes? hashes, CancellationToken ct = default)
    {
        CleanOldParts();
        var list = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
        if (list.Count == 0) throw new ArgumentException(I18n.T("mr.noFile"));
        Exception? last = null;
        foreach (var url in list)
        {
            try { return await DownloadOne(url, fileName, progress, hashes ?? new Hashes(), ct); }
            catch (Exception e) when (!ct.IsCancellationRequested && e is not OfflineException && e is not NoSpaceException && url != list[^1])
            {
                last = e;
            }
        }
        throw last ?? new IOException(fileName);
    }

    /// <summary>Не хватает места на диске — повторять бессмысленно.</summary>
    public sealed class NoSpaceException(string message) : IOException(message);

    /// <summary>Сколько частей у большого файла (Настройки → Загрузки); 1 — один поток.</summary>
    public static int Segments
    {
        get => _segments;
        set => _segments = Math.Clamp(value, 1, 8);
    }
    static volatile int _segments = 4;

    /// <summary>Настройки загрузок (части и предел скорости) — при запуске и после изменения в настройках.</summary>
    public static void ApplySettings()
    {
        Segments = Settings.Data.Long("dlSegments") is var n && n > 0 ? (int)n : 4;
        Throttle.Limit = (long)(Settings.Data.Double("dlLimitMb") * 1024 * 1024);
    }

    /// <summary>С какого размера файл качается частями: мелочь быстрее одним потоком.</summary>
    public static long SegmentMin { get; set; } = 8L << 20;

    static async Task<string> DownloadOne(string url, string fileName, IProgress<double>? progress, Hashes hashes, CancellationToken ct)
    {
        var job = Jobs.CurrentJob;
        var transfer = job?.BeginTransfer() ?? new Transfer();
        var part = Reserve(PartName(url, fileName));
        var meta = part + ".meta";
        var ok = false;
        try
        {
            // Прошлый раз файл качался частями — продолжаем частями.
            if (ReadMeta(meta) is { Segmented: true } saved && Segments > 1)
            {
                var resumed = await Segmented(saved.Url ?? url, part, meta, saved.Validator, saved.Size, fileName, progress, transfer, job, ct, fresh: false);
                if (resumed is not null) { ok = true; return Finish(part, meta, fileName, hashes); }
            }

            var failures = 0;
            while (true)
            {
                long gained = 0;
                try
                {
                    var have = File.Exists(part) ? new FileInfo(part).Length : 0;
                    // Докачиваем, только если знаем, что файл на сервере тот же (ETag / Last-Modified):
                    // иначе к старому куску прилипнет новый файл и архив выйдет битым.
                    var validator = have > 0 ? ReadMeta(meta)?.Validator ?? "" : "";
                    if (have > 0 && validator == "") { File.Delete(part); have = 0; }
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (have > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(have, null);
                        request.Headers.TryAddWithoutValidation("If-Range", validator);
                    }

                    var response = await Send(request, ct);
                    using var answer = response;
                    if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        // Кусок уже целый (дальше сервер отдавать нечего) — готово; иначе начинаем заново.
                        if (response.Content.Headers.ContentRange?.Length is long full && full == have) { ok = true; return Finish(part, meta, fileName, hashes); }
                        File.Delete(part);
                        throw new IOException("range not satisfiable");
                    }
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} — {url}", null, response.StatusCode);
                    // Вместо файла пришла веб-страница (вход, капча, ошибка CDN) — не сохраняем её как архив.
                    if (response.Content.Headers.ContentType?.MediaType == "text/html" && !fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        throw new HttpRequestException(I18n.T("dl.gotPage"), null, System.Net.HttpStatusCode.UnsupportedMediaType);

                    var resumed = have > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                    if (!resumed) have = 0;
                    var total = response.Content.Headers.ContentLength is long length ? length + have : 0;
                    EnsureSpace(total - have, fileName);
                    if (!resumed)
                    {
                        var etag = Validator(response);
                        var ranges = response.Headers.AcceptRanges.Contains("bytes");
                        // Большой файл, сервер умеет отдавать куски и знает версию файла — качаем частями.
                        if (Segments > 1 && total >= SegmentMin && ranges && etag != "")
                        {
                            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                            answer.Dispose();
                            var done = await Segmented(finalUrl, part, meta, etag, total, fileName, progress, transfer, job, ct, fresh: true);
                            if (done is not null) { ok = true; return Finish(part, meta, fileName, hashes); }
                            // Сервер передумал отдавать куски — качаем одним потоком.
                            failures = 0;
                            continue;
                        }
                        if (etag != "" && ranges) WriteMeta(meta, new Meta(etag, false, 0, null));
                        else if (File.Exists(meta)) File.Delete(meta);
                    }
                    long received = have;
                    transfer.SetSize(total);
                    transfer.SetGot(received);
                    await using (var input = await response.Content.ReadAsStreamAsync(ct))
                    await using (var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 17, true))
                    {
                        var buffer = new byte[1 << 17];
                        var reported = DateTime.MinValue;
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
                            transfer.Add(read);
                            await Throttle.Take(read, ct);
                            // Интерфейсу хватает 10 обновлений в секунду — чаще только тормозит окно.
                            var now = DateTime.UtcNow;
                            if ((now - reported).TotalMilliseconds >= 100)
                            {
                                reported = now;
                                if (total > 0) progress?.Report((double)received / total);
                                if (job is not null) Jobs.Touch(job);
                            }
                        }
                        if (total > 0 && received != total) throw new IOException("download cut off");
                    }
                    progress?.Report(1);
                    ok = true;
                    return Finish(part, meta, fileName, hashes);
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
            job?.EndTransfer(transfer, ok);
        }
    }

    /// <summary>Запрос с ожиданием ответа не дольше 45 с (сам файл может идти сколько угодно).</summary>
    static async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(TimeSpan.FromSeconds(45));
        try { return await Files.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, wait.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("server did not answer in 45 s"); }
    }

    /// <summary>Чем сервер подтверждает, что файл тот же: сильный ETag, иначе дата изменения (слабый ETag для докачки не годится).</summary>
    static string Validator(HttpResponseMessage response)
    {
        if (response.Headers.ETag is { IsWeak: false } strong) return strong.ToString();
        return response.Content.Headers.LastModified?.ToString("R") ?? "";
    }

    /// <summary>Что записано рядом с недокачанным файлом: версия файла на сервере и, если качался частями, — размер и ссылка.</summary>
    sealed record Meta(string Validator, bool Segmented, long Size, string? Url);

    static Meta? ReadMeta(string meta)
    {
        try
        {
            if (!File.Exists(meta)) return null;
            var lines = File.ReadAllLines(meta);
            if (lines.Length == 0 || lines[0].Trim() == "") return null;
            if (lines.Length >= 3 && lines[1].StartsWith("seg ", StringComparison.Ordinal) && long.TryParse(lines[1][4..], out var size) && size > 0)
                return new Meta(lines[0].Trim(), true, size, lines[2].Trim());
            return new Meta(lines[0].Trim(), false, 0, null);
        }
        catch { return null; }
    }

    static void WriteMeta(string meta, Meta m) =>
        File.WriteAllText(meta, m.Segmented ? $"{m.Validator}\nseg {m.Size}\n{m.Url}" : m.Validator);

    /// <summary>Перед загрузкой: хватит ли места во временной папке (с запасом 64 МБ).</summary>
    static void EnsureSpace(long need, string fileName)
    {
        if (need <= 0) return;
        try
        {
            var root = Path.GetPathRoot(Paths.DownloadsTemp);
            if (string.IsNullOrEmpty(root)) return;
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < need + (64L << 20))
                throw new NoSpaceException(I18n.T("dl.noSpace", ("file", fileName), ("need", Size(need)), ("free", Size(free)), ("drive", root.TrimEnd('\\', '/'))));
        }
        catch (NoSpaceException) { throw; }
        catch { }
    }

    static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        _ => $"{Math.Max(1, bytes / 1024)} KB",
    };

    /// <summary>
    /// Загрузка частями: файл делится на N кусков, каждый качается своим соединением в свой
    /// «.sN»-файл (с докачкой), потом куски склеиваются. Возвращает путь к целому файлу или
    /// null, если сервер перестал отдавать куски (тогда качаем одним потоком).
    /// </summary>
    static async Task<string?> Segmented(string url, string part, string meta, string validator, long size, string fileName,
        IProgress<double>? progress, Transfer transfer, Job? job, CancellationToken ct, bool fresh)
    {
        var count = Math.Max(2, Segments);
        var piece = size / count;
        var ranges = Enumerable.Range(0, count).Select(i => (Start: i * piece, End: i == count - 1 ? size - 1 : (i + 1) * piece - 1, File: $"{part}.s{i}")).ToList();
        void Wipe()
        {
            foreach (var r in ranges) try { File.Delete(r.File); } catch { }
            try { File.Delete(meta); } catch { }
        }
        if (fresh)
        {
            Wipe();
            try { File.Delete(part); } catch { }
            WriteMeta(meta, new Meta(validator, true, size, url));
        }
        // Кусок не может быть длиннее своего места — значит, это кусок другого деления: начинаем заново.
        if (ranges.Any(r => File.Exists(r.File) && new FileInfo(r.File).Length > r.End - r.Start + 1)) { Wipe(); return null; }
        var have = ranges.Sum(r => File.Exists(r.File) ? new FileInfo(r.File).Length : 0);
        EnsureSpace(size - have, fileName);
        transfer.SetSize(size);
        transfer.SetGot(have);
        long reportedAt = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var restart = false;

        async Task Piece((long Start, long End, string File) r)
        {
            var failures = 0;
            while (true)
            {
                long gained = 0;
                try
                {
                    var got = File.Exists(r.File) ? new FileInfo(r.File).Length : 0;
                    var length = r.End - r.Start + 1;
                    if (got >= length) return;
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new RangeHeaderValue(r.Start + got, r.End);
                    request.Headers.TryAddWithoutValidation("If-Range", validator);
                    using var response = await Send(request, stop.Token);
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != r.Start + got)
                    {
                        // Сервер прислал весь файл (поменялся или не умеет куски) — так не склеить.
                        if (response.IsSuccessStatusCode) { restart = true; stop.Cancel(); return; }
                        throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} — {url}", null, response.StatusCode);
                    }
                    await using var input = await response.Content.ReadAsStreamAsync(stop.Token);
                    await using var output = new FileStream(r.File, FileMode.Append, FileAccess.Write, FileShare.None, 1 << 16, true);
                    var buffer = new byte[1 << 16];
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    stall.CancelAfter(TimeSpan.FromSeconds(60));
                    while (got < length)
                    {
                        int read;
                        try { read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - got)), stall.Token); }
                        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { throw new TimeoutException("no data for 60 s"); }
                        if (read <= 0) break;
                        stall.CancelAfter(TimeSpan.FromSeconds(60));
                        await output.WriteAsync(buffer.AsMemory(0, read), stop.Token);
                        got += read;
                        gained += read;
                        transfer.Add(read);
                        await Throttle.Take(read, stop.Token);
                        var now = Environment.TickCount64;
                        var last = Interlocked.Read(ref reportedAt);
                        if (now - last >= 100 && Interlocked.CompareExchange(ref reportedAt, now, last) == last)
                        {
                            progress?.Report((double)transfer.Got / size);
                            if (job is not null) Jobs.Touch(job);
                        }
                    }
                    if (got < length) throw new IOException("download cut off");
                    return;
                }
                catch (Exception e) when (!stop.IsCancellationRequested && !Hopeless(e))
                {
                    failures = gained > 0 ? 1 : failures + 1;
                    if (failures > 6) throw;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, 1 << (failures - 1))), stop.Token);
                }
            }
        }

        try
        {
            await Task.WhenAll(ranges.Select(Piece));
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            if (!restart)
            {
                // Ссылка устарела (подписанные ссылки Nexus живут недолго) — при докачке начинаем с нуля.
                if (!fresh && Hopeless(e) && e is not OfflineException and not NoSpaceException) { Wipe(); return null; }
                ExceptionDispatchInfo.Capture(e is AggregateException { InnerException: { } inner } ? inner : e).Throw();
            }
        }
        if (restart) { Wipe(); transfer.SetGot(0); return null; }
        ct.ThrowIfCancellationRequested();

        // Склеиваем куски по порядку.
        await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 17, true))
            foreach (var r in ranges)
                await using (var input = new FileStream(r.File, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 17, true))
                    await input.CopyToAsync(output, ct);
        foreach (var r in ranges) try { File.Delete(r.File); } catch { }
        if (new FileInfo(part).Length != size) { try { File.Delete(part); } catch { } try { File.Delete(meta); } catch { } throw new IOException("download cut off"); }
        progress?.Report(1);
        return part;
    }

    /// <summary>Проверить контрольные суммы и подпись архива, отдать готовый файл.</summary>
    static string Finish(string part, string meta, string fileName, Hashes hashes)
    {
        void Drop() { try { File.Delete(part); } catch { } try { File.Delete(meta); } catch { } }
        if (!Matches(part, hashes)) { Drop(); throw new HashMismatchException(fileName); }
        if (LooksBroken(part, fileName)) { Drop(); throw new IOException(I18n.T("dl.broken")); }
        var target = Path.Combine(Paths.DownloadsTemp, $"{DateTime.UtcNow.Ticks}-{Safe(fileName)}");
        File.Move(part, target);
        try { File.Delete(meta); } catch { }
        return target;
    }

    /// <summary>Совпадает ли файл со всеми известными контрольными суммами.</summary>
    public static bool Matches(string file, Hashes hashes)
    {
        if (!hashes.Any) return true;
        using var stream = File.OpenRead(file);
        if (hashes.Sha256 is { Length: > 0 } a && !a.Equals(Convert.ToHexString(SHA256.HashData(stream)), StringComparison.OrdinalIgnoreCase)) return false;
        stream.Position = 0;
        if (hashes.Sha1 is { Length: > 0 } b && !b.Equals(Convert.ToHexString(SHA1.HashData(stream)), StringComparison.OrdinalIgnoreCase)) return false;
        stream.Position = 0;
        if (hashes.Sha512 is { Length: > 0 } c && !c.Equals(Convert.ToHexString(SHA512.HashData(stream)), StringComparison.OrdinalIgnoreCase)) return false;
        return true;
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
    static bool Hopeless(Exception e) => e is OfflineException or NoSpaceException or HashMismatchException ||
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

/// <summary>
/// 9.4: ограничение скорости всех загрузок вместе (Настройки → Загрузки), как в Steam: игра по сети
/// не тормозит, пока качается модпак. Ведро токенов на одну секунду: байты «тратят» токены,
/// токены копятся со скоростью предела.
/// </summary>
public static class Throttle
{
    static readonly object Gate = new();
    static double _tokens;
    static long _at = Environment.TickCount64;
    static long _limit;

    /// <summary>Предел в байтах в секунду; 0 — без ограничения.</summary>
    public static long Limit
    {
        get => Interlocked.Read(ref _limit);
        set => Interlocked.Exchange(ref _limit, Math.Max(0, value));
    }

    public static async ValueTask Take(int bytes, CancellationToken ct)
    {
        var limit = Limit;
        if (limit <= 0) return;
        TimeSpan wait;
        lock (Gate)
        {
            var now = Environment.TickCount64;
            _tokens = Math.Min(limit, _tokens + (now - _at) / 1000.0 * limit);
            _at = now;
            _tokens -= bytes;
            if (_tokens >= 0) return;
            wait = TimeSpan.FromSeconds(-_tokens / limit);
        }
        await Task.Delay(wait < TimeSpan.FromMilliseconds(5) ? TimeSpan.FromMilliseconds(5) : wait, ct);
    }
}
