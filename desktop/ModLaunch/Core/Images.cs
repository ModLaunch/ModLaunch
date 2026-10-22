using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ModLaunch.Core;

/// <summary>Картинки: из ресурсов программы и из сети (с кэшем на диске и в памяти).</summary>
public static class Images
{
    static readonly ConcurrentDictionary<string, Bitmap?> Memory = new();
    static readonly ConcurrentDictionary<string, Task<Bitmap?>> Pending = new();
    static readonly SemaphoreSlim Parallel = new(12);

    public static Bitmap? Asset(string name, int decodeWidth = 0)
    {
        var key = $"asset:{name}:{decodeWidth}";
        return Memory.GetOrAdd(key, _ =>
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri($"avares://ModLaunch/Assets/{name}"));
                return decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth) : new Bitmap(stream);
            }
            catch { return null; }
        });
    }

    /// <summary>Вид обложки: вертикальная (как в библиотеке Steam), широкая шапка, фон или логотип.</summary>
    public enum Art { Cover, Header, Hero, Logo }

    static string Kind(Art art) => art switch { Art.Cover => "cover", Art.Header => "header", Art.Hero => "hero", _ => "logo" };

    /// <summary>Встроенная картинка игры (Assets/art/&lt;appid&gt;-&lt;вид&gt;), если есть.</summary>
    /// <summary>Ширины декодирования округляются до нескольких ступеней: меньше разных копий в памяти и меньше работы.</summary>
    static readonly int[] Buckets = [64, 128, 256, 384, 512, 768, 1024, 1600, 1920];
    static int Bucket(int width) => Buckets.FirstOrDefault(b => b >= width, Buckets[^1]);

    /// <summary>Заранее, в фоне, декодировать обложки и фоны — чтобы страницы открывались без подтормаживаний.</summary>
    public static void Prewarm(IEnumerable<Games.GameDef> games)
    {
        var list = games.ToList();
        _ = Task.Run(() =>
        {
            foreach (var g in list)
            {
                GameAsset(g, Art.Cover, 256); GameAsset(g, Art.Cover, 384); GameAsset(g, Art.Header, 256);
            }
            foreach (var g in list.Take(8)) { GameAsset(g, Art.Hero, 1600); GameAsset(g, Art.Logo, 768); }
        });
    }

    public static Bitmap? GameAsset(Games.GameDef def, Art art, int width)
    {
        width = Bucket(width);
        // 9.4: официальная картинка из Microsoft Store (Minecraft), если уже скачана.
        if (OfficialArt.File(def, art) is { } official)
        {
            var stamp = File.GetLastWriteTimeUtc(official).Ticks;
            var hit = Memory.GetOrAdd($"official:{official}:{stamp}:{width}", _ =>
            {
                try { using var s = File.OpenRead(official); return Bitmap.DecodeToWidth(s, width); } catch { return null; }
            });
            if (hit is not null) return hit;
        }
        var key = def.ArtKey ?? (def.SteamAppId > 0 ? def.SteamAppId.ToString() : null);
        if (key is null) return null;
        var name = $"art/{key}-{Kind(art)}.{(art == Art.Logo ? "png" : "jpg")}";
        return AssetExists(name) ? Asset(name, width) : null;
    }

    static readonly ConcurrentDictionary<string, bool> Exists = new();
    static bool AssetExists(string name) => Exists.GetOrAdd(name, n =>
    {
        try { return AssetLoader.Exists(new Uri($"avares://ModLaunch/Assets/{n}")); } catch { return false; }
    });

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Bitmap, Bitmap> Tinies = new();

    /// <summary>
    /// 16.0: крошечная копия картинки (32 px в ширину) для фона постера: растянутая на всю рамку, она
    /// выглядит размытой — без эффекта размытия, который дорого перерисовывать на каждой обложке.
    /// </summary>
    public static Bitmap Tiny(Bitmap source)
    {
        return Tinies.GetValue(source, s =>
        {
            try
            {
                var w = Math.Max(1, Math.Min(32, s.PixelSize.Width));
                var h = Math.Max(1, (int)Math.Round((double)s.PixelSize.Height * w / Math.Max(1, s.PixelSize.Width)));
                return s.CreateScaledBitmap(new Avalonia.PixelSize(w, h), Avalonia.Media.Imaging.BitmapInterpolationMode.MediumQuality);
            }
            catch { return s; }
        });
    }

    /// <summary>Ссылки Steam на картинку игры — если встроенной нет (свои игры, новые игры).</summary>
    public static string[] SteamUrls(int appId, Art art)
    {
        if (appId <= 0) return [];
        var file = art switch { Art.Cover => "library_600x900.jpg", Art.Hero => "library_hero.jpg", Art.Logo => "logo.png", _ => "header.jpg" };
        return
        [
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/{file}",
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/{file}",
        ];
    }

    /// <summary>Обложка игры, если она уже под рукой (встроенная или в кэше); из сети — через Ui.GameImage.</summary>
    public static Bitmap? Game(Games.GameDef def, int width)
    {
        if (GameAsset(def, Art.Header, width) is { } header) return header;
        if (def.Art is not null) return Asset(def.Art, width);
        if (def.ArtUrl is null) return null;
        var task = FromUrl(def.ArtUrl, width);
        return task.IsCompleted ? task.Result : null;
    }

    /// <summary>Картинка игры любого вида: встроенная, иначе из Steam по очереди ссылок.</summary>
    public static async Task<Bitmap?> GameAsync(Games.GameDef def, Art art, int width)
    {
        if (GameAsset(def, art, width) is { } local) return local;
        // 16.0: кэш Steam на ПК, старые адреса CDN и настоящие адреса из магазина (GameArt).
        if (await GameArt.Steam(def.SteamAppId, art, width) is { } steam) return steam;
        // Нужной формы нет: другая картинка игры — Ui.GameImage сложит из неё постер, а не обрежет.
        if (art == Art.Cover && GameAsset(def, Art.Hero, width * 3) is { } hero) return hero;
        if (art == Art.Cover && await GameArt.Steam(def.SteamAppId, Art.Header, width * 2) is { } wide) return wide;
        if (art is Art.Header or Art.Cover && def.Art is not null) return Asset(def.Art, width);
        return def.ArtUrl is null ? null : await FromUrl(def.ArtUrl, width);
    }

    /// <summary>
    /// Картинка по ссылке: память → диск → сеть. 15.0: своё соединение для картинок (12 одновременно,
    /// на подключение 8 с, на всю картинку 20 с, одна повторная попытка). Раньше картинки шли по общему
    /// клиенту с 30-минутным лимитом: несколько «зависших» адресов (часто — CDN, который медленно отвечает
    /// из России) занимали все 6 мест, и остальные обложки модов не загружались вовсе. Неудача больше не
    /// запоминается до перезапуска — через 2 минуты картинку можно попробовать снова.
    /// </summary>
    public static Task<Bitmap?> FromUrl(string? url, int decodeWidth = 160)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult<Bitmap?>(null);
        var key = $"{url}:{decodeWidth}";
        if (Memory.TryGetValue(key, out var hit)) return Task.FromResult<Bitmap?>(hit);
        if (Failed.TryGetValue(key, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(2)) return Task.FromResult<Bitmap?>(null);
        return Pending.GetOrAdd(key, _ => Task.Run(async () =>
        {
            var file = Path.Combine(Paths.CacheDir, "img", Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + ".bin");
            Bitmap? bmp = null;
            try
            {
                if (!File.Exists(file))
                {
                    await Parallel.WaitAsync();
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        var bytes = await Fetch(url);
                        if (bytes is not null && bytes.Length > 0)
                        {
                            var part = file + ".part";
                            await File.WriteAllBytesAsync(part, bytes);
                            File.Move(part, file, true);
                        }
                    }
                    finally { Parallel.Release(); }
                }
                if (File.Exists(file))
                {
                    await using var stream = File.OpenRead(file);
                    bmp = Bitmap.DecodeToWidth(stream, decodeWidth);
                }
            }
            catch { try { File.Delete(file); } catch { } bmp = null; }
            if (bmp is not null) { Memory[key] = bmp; Failed.TryRemove(key, out DateTime _); }
            else Failed[key] = DateTime.UtcNow;
            Pending.TryRemove(key, out Task<Bitmap?>? _);
            return bmp;
        }));
    }

    static readonly ConcurrentDictionary<string, DateTime> Failed = new();

    /// <summary>Клиент только для картинок: короткие лимиты, много соединений к одному CDN, браузерный Accept.</summary>
    static readonly HttpClient Pictures = CreatePictures();

    static HttpClient CreatePictures()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
        };
        var client = new HttpClient(new Offline.Gate(handler)) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) ModLaunch/{Http.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/png,image/jpeg,image/*;q=0.8,*/*;q=0.5");
        return client;
    }

    /// <summary>Скачать картинку: до двух попыток; 404 — сразу «нет».</summary>
    static async Task<byte[]?> Fetch(string url)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var response = await Pictures.GetAsync(url);
                if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) return null;
                if (!response.IsSuccessStatusCode) { await Task.Delay(400); continue; }
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (OfflineException) { return null; }
            catch when (attempt == 0) { await Task.Delay(400); }
            catch { return null; }
        }
        return null;
    }
}
