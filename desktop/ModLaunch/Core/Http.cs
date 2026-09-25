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
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
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

    /// <summary>Скачать файл во временную папку. Возвращает путь.</summary>
    public static async Task<string> Download(string url, string fileName, IProgress<double>? progress = null, string? sha256 = null, CancellationToken ct = default)
    {
        var target = Path.Combine(Paths.DownloadsTemp, $"{DateTime.UtcNow.Ticks}-{Safe(fileName)}");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} — {url}");
                var total = response.Content.Headers.ContentLength;
                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = File.Create(target))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        received += read;
                        if (total > 0) progress?.Report((double)received / total.Value);
                    }
                    if (total is long expected && received != expected) throw new IOException("download cut off");
                }
                if (sha256 is not null && !string.Equals(Sha256(target), sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("checksum mismatch");
                return target;
            }
            catch (Exception) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                try { File.Delete(target); } catch { }
                await Task.Delay(1200 * attempt, ct);
            }
        }
    }

    public static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
