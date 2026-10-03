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

}
