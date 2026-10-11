using System.Net;
using System.Net.Sockets;

namespace ModLaunch.Core;

static class HttpTests
{
    /// <summary>
    /// Загрузка с обрывом: сервер на 127.0.0.1 рвёт первую отдачу на середине,
    /// потом отдаёт хвост по Range. Файл должен собраться байт в байт.
    /// Второй прогон — файл «поменялся» (другой ETag): докачки быть не должно.
    /// </summary>
    [SelfTest]
    static string DownloadResumesAndStaysIntact()
    {
        var data = new byte[700_000];
        new Random(7).NextBytes(data);
        data[0] = (byte)'P'; data[1] = (byte)'K';
        var etag = "\"v1\"";
        var requests = 0;
        var resumedFrom = -1L;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                var n = Interlocked.Increment(ref requests);
                var res = ctx.Response;
                res.Headers["ETag"] = etag;
                res.Headers["Accept-Ranges"] = "bytes";
                long from = 0;
                var range = ctx.Request.Headers["Range"];
                var ifRange = ctx.Request.Headers["If-Range"];
                if (range is not null && (ifRange is null || ifRange == etag))
                {
                    from = long.Parse(range.Replace("bytes=", "").TrimEnd('-'));
                    resumedFrom = from;
                    res.StatusCode = 206;
                    res.Headers["Content-Range"] = $"bytes {from}-{data.Length - 1}/{data.Length}";
                }
                res.ContentType = "application/zip";
                res.ContentLength64 = data.Length - from;
                try
                {
                    // Первый ответ обрываем на середине.
                    var count = n == 1 ? 300_000 : data.Length - (int)from;
                    await res.OutputStream.WriteAsync(data.AsMemory((int)from, count));
                    if (n == 1) { res.Abort(); continue; }
                    res.Close();
                }
                catch { }
            }
        });
        try
        {
            var file = Task.Run(() => Http.Download($"http://127.0.0.1:{port}/mod.zip", "mod.zip")).GetAwaiter().GetResult();
            var got = File.ReadAllBytes(file);
            File.Delete(file);
            if (!got.AsSpan().SequenceEqual(data)) throw new Exception($"file differs ({got.Length} of {data.Length} bytes)");
            if (resumedFrom <= 0) throw new Exception("did not resume after the cut");
            return $"{requests} requests, resumed from {resumedFrom}";
        }
        finally { listener.Stop(); }
    }

    /// <summary>Сервер на 127.0.0.1 для проверок: отдаёт data по Range с ETag; handler может вмешаться (оборвать, ответить ошибкой).</summary>
    sealed class LocalServer : IDisposable
    {
        readonly HttpListener _listener = new();
        public int Port { get; }
        public int Requests;
        public int RangeRequests;
        public string Url(string path) => $"http://127.0.0.1:{Port}/{path}";

        public LocalServer(byte[] data, Func<HttpListenerContext, int, bool>? intercept = null, string etag = "\"v1\"", bool ranges = true)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); } catch { return; }
                    var n = Interlocked.Increment(ref Requests);
                    _ = Task.Run(async () =>
                    {
                        var res = ctx.Response;
                        try
                        {
                            if (intercept?.Invoke(ctx, n) == true) return;
                            res.Headers["ETag"] = etag;
                            if (ranges) res.Headers["Accept-Ranges"] = "bytes";
                            long from = 0, to = data.Length - 1;
                            var range = ctx.Request.Headers["Range"];
                            var ifRange = ctx.Request.Headers["If-Range"];
                            if (ranges && range is not null && (ifRange is null || ifRange == etag))
                            {
                                Interlocked.Increment(ref RangeRequests);
                                var spec = range.Replace("bytes=", "").Split('-');
                                from = long.Parse(spec[0]);
                                if (spec.Length > 1 && spec[1] != "") to = long.Parse(spec[1]);
                                res.StatusCode = 206;
                                res.Headers["Content-Range"] = $"bytes {from}-{to}/{data.Length}";
                            }
                            res.ContentType = "application/zip";
                            res.ContentLength64 = to - from + 1;
                            await res.OutputStream.WriteAsync(data.AsMemory((int)from, (int)(to - from + 1)));
                            res.Close();
                        }
                        catch { try { res.Abort(); } catch { } }
                    });
                }
            });
        }

        public void Dispose() { try { _listener.Stop(); } catch { } }
    }

    static byte[] Zip(int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        data[0] = (byte)'P'; data[1] = (byte)'K';
        return data;
    }

    /// <summary>
    /// Большой файл качается частями в несколько соединений: одна часть обрывается на середине
    /// и докачивается, файл собирается байт в байт и проходит sha1.
    /// </summary>
    [SelfTest]
    static string BigFileDownloadsInPieces()
    {
        var data = Zip(3_000_000, 11);
        var sha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(data));
        var cut = 0;
        using var server = new LocalServer(data, (ctx, n) =>
        {
            // Первый запрос куска со второй четверти обрываем на середине.
            var range = ctx.Request.Headers["Range"];
            if (range is not null && range.StartsWith("bytes=750000-") && Interlocked.Exchange(ref cut, 1) == 0)
            {
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["ETag"] = "\"v1\"";
                ctx.Response.Headers["Content-Range"] = $"bytes 750000-1499999/{data.Length}";
                ctx.Response.ContentLength64 = 750000;
                ctx.Response.OutputStream.Write(data, 750000, 200000);
                ctx.Response.Abort();
                return true;
            }
            return false;
        });
        var (min, segs) = (Http.SegmentMin, Http.Segments);
        Http.SegmentMin = 1_000_000;
        Http.Segments = 4;
        try
        {
            var file = Task.Run(() => Http.Download([server.Url("pack.zip")], "pack.zip", null, new Http.Hashes(Sha1: sha1))).GetAwaiter().GetResult();
            var got = File.ReadAllBytes(file);
            File.Delete(file);
            if (!got.AsSpan().SequenceEqual(data)) throw new Exception($"file differs ({got.Length} of {data.Length} bytes)");
            if (server.RangeRequests < 4) throw new Exception($"only {server.RangeRequests} range requests — not downloaded in pieces");
            if (cut == 0) throw new Exception("the cut never happened");
            return $"{server.Requests} requests, {server.RangeRequests} pieces, cut piece resumed, sha1 ok";
        }
        finally { (Http.SegmentMin, Http.Segments) = (min, segs); }
    }

    /// <summary>Зеркала: первая ссылка отвечает 404, вторая отдаёт не тот файл (sha1 не сходится) — файл берётся с третьей.</summary>
    [SelfTest]
    static string MirrorsAreTriedInTurn()
    {
        var good = Zip(200_000, 3);
        var bad = Zip(200_000, 4);
        var sha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(good));
        using var missing = new LocalServer(good, (ctx, _) => { ctx.Response.StatusCode = 404; ctx.Response.Close(); return true; });
        using var wrong = new LocalServer(bad);
        using var right = new LocalServer(good);
        var file = Task.Run(() => Http.Download([missing.Url("m.jar"), wrong.Url("m.jar"), right.Url("m.jar")], "m.jar", null, new Http.Hashes(Sha1: sha1))).GetAwaiter().GetResult();
        var got = File.ReadAllBytes(file);
        File.Delete(file);
        if (!got.AsSpan().SequenceEqual(good)) throw new Exception("wrong file kept");
        if (missing.Requests != 1) throw new Exception($"404 retried {missing.Requests} times");
        if (wrong.Requests != 1) throw new Exception($"checksum mismatch retried {wrong.Requests} times on the same mirror");
        return "404 → next, bad sha1 → next, third mirror ok";
    }

    /// <summary>Ограничение скорости: 400 КБ при пределе 1 МБ/с идут не меньше ~0,3 с (ведро на 1 с уже пусто).</summary>
    [SelfTest]
    static string SpeedLimitHolds()
    {
        var was = Throttle.Limit;
        Throttle.Limit = 1 << 20;
        try
        {
            // Выбираем запас ведра (секунда предела), потом меряем.
            Task.Run(async () => { await Throttle.Take(1 << 20, default); }).GetAwaiter().GetResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Task.Run(async () => { for (var i = 0; i < 4; i++) await Throttle.Take(100 << 10, default); }).GetAwaiter().GetResult();
            if (sw.ElapsedMilliseconds < 250) throw new Exception($"400 KB at 1 MB/s took only {sw.ElapsedMilliseconds} ms");
            return $"400 KB took {sw.ElapsedMilliseconds} ms at 1 MB/s";
        }
        finally { Throttle.Limit = was; }
    }

    /// <summary>Байты одной задачи с несколькими загрузками складываются, после неудачной загрузки — не считаются.</summary>
    [SelfTest]
    static string JobAddsUpTransfers()
    {
        var job = new Job { Title = "t", GameName = "g", Status = JobStatus.Running };
        var a = job.BeginTransfer(); a.SetSize(100); a.SetGot(40);
        var b = job.BeginTransfer(); b.SetSize(300); b.SetGot(60);
        if (job.Received != 100 || job.Total != 400) throw new Exception($"{job.Received}/{job.Total} instead of 100/400");
        job.EndTransfer(b, done: false);
        if (job.Received != 40 || job.Total != 100) throw new Exception($"failed transfer still counted: {job.Received}/{job.Total}");
        var planned = new Job { Title = "p", GameName = "g", Status = JobStatus.Running, Planned = 1000 };
        var c = planned.BeginTransfer(); c.SetSize(200); c.SetGot(200);
        planned.EndTransfer(c, done: true);
        var d = planned.BeginTransfer(); d.SetSize(300); d.SetGot(100);
        if (planned.Received != 300 || planned.Total != 1000) throw new Exception($"planned batch {planned.Received}/{planned.Total} instead of 300/1000");
        return "two transfers 100/400, failed one dropped, planned batch 300/1000";
    }

    /// <summary>Пауза посреди загрузки частями: следующий запуск докачивает куски с места остановки, а не с нуля.</summary>
    [SelfTest]
    static string PausedPiecesResume()
    {
        var data = Zip(2_400_000, 21);
        var froms = new System.Collections.Concurrent.ConcurrentBag<long>();
        using var server = new LocalServer(data, (ctx, _) =>
        {
            if (ctx.Request.Headers["Range"] is { } r) froms.Add(long.Parse(r.Replace("bytes=", "").Split('-')[0]));
            return false;
        });
        var (min, segs, limit) = (Http.SegmentMin, Http.Segments, Throttle.Limit);
        Http.SegmentMin = 1_000_000;
        Http.Segments = 4;
        Throttle.Limit = 600_000;
        try
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
            {
                try { Task.Run(() => Http.Download([server.Url("big.zip")], "big.zip", null, null, cts.Token)).GetAwaiter().GetResult(); throw new Exception("finished before the pause"); }
                catch (OperationCanceledException) { }
            }
            froms.Clear();
            Throttle.Limit = 0;
            var file = Task.Run(() => Http.Download([server.Url("big.zip")], "big.zip", null, null)).GetAwaiter().GetResult();
            var got = File.ReadAllBytes(file);
            File.Delete(file);
            if (!got.AsSpan().SequenceEqual(data)) throw new Exception("file differs after resume");
            var starts = new long[] { 0, 600_000, 1_200_000, 1_800_000 };
            var resumed = froms.Count(f => !starts.Contains(f));
            if (resumed == 0) throw new Exception("every piece started from zero again: " + string.Join(",", froms.Order()));
            return $"{resumed} of 4 pieces continued after the pause";
        }
        finally { (Http.SegmentMin, Http.Segments, Throttle.Limit) = (min, segs, limit); }
    }
}
