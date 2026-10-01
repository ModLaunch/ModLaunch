using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ModLaunch.Core;

/// <summary>
/// Картинки: из ресурсов программы и из сети (с кэшем на диске и в памяти).
///
/// Картинки из сети держатся в памяти не все подряд, а последние показанные —
/// в пределах NetBudget. Раньше каждая иконка каталога и каждый скриншот со
/// страницы мода (до 1600 точек, ~6 МБ в распакованном виде) оставались в
/// памяти до закрытия программы: через пару часов листания каталогов
/// набирались гигабайты, и ModLaunch вылетал. Вытесненная картинка не
/// уничтожается — её может ещё показывать экран, — она просто больше не
/// держится кэшем и освобождается сборщиком мусора, когда её никто не видит.
/// </summary>
public static class Images
{
    /// <summary>Встроенные картинки (обложки игр, значок): их немного, держим всегда.</summary>
    static readonly ConcurrentDictionary<string, Bitmap?> Memory = new();
    static readonly ConcurrentDictionary<string, Task<Bitmap?>> Pending = new();
    static readonly SemaphoreSlim Parallel = new(6);

    /// <summary>Сколько распакованных картинок из сети держать в памяти.</summary>
    const long NetBudget = 192L * 1024 * 1024;
    /// <summary>Файл больше этого — не картинка для карточки (или битая ссылка): не качаем и не распаковываем.</summary>
    const long MaxFileBytes = 20L * 1024 * 1024;
    static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(5);

    sealed record NetEntry(string Key, Bitmap Bitmap, long Bytes);
    static readonly object NetLock = new();
    static readonly Dictionary<string, LinkedListNode<NetEntry>> Net = [];
    static readonly LinkedList<NetEntry> Recent = new();
    static long _netBytes, _evictedSinceGc;
    static DateTime _lastGc = DateTime.MinValue;
    static readonly ConcurrentDictionary<string, DateTime> Failed = new();

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
    public static Task Prewarm(IEnumerable<Games.GameDef> games)
    {
        var list = games.ToList();
        return Task.Run(() =>
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
        if (def.SteamAppId <= 0) return null;
        var name = $"art/{def.SteamAppId}-{Kind(art)}.{(art == Art.Logo ? "png" : "jpg")}";
        return AssetExists(name) ? Asset(name, width) : null;
    }

    static readonly ConcurrentDictionary<string, bool> Exists = new();
    static bool AssetExists(string name) => Exists.GetOrAdd(name, n =>
    {
        try { return AssetLoader.Exists(new Uri($"avares://ModLaunch/Assets/{n}")); } catch { return false; }
    });

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
        foreach (var url in SteamUrls(def.SteamAppId, art))
            if (await FromUrl(url, width) is { } bmp) return bmp;
        if (art == Art.Cover && GameAsset(def, Art.Hero, width * 3) is { } hero) return hero;
        if (art is Art.Header or Art.Cover && def.Art is not null) return Asset(def.Art, width);
        return def.ArtUrl is null ? null : await FromUrl(def.ArtUrl, width);
    }

    public static Task<Bitmap?> FromUrl(string? url, int decodeWidth = 160)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult<Bitmap?>(null);
        var key = $"{url}:{decodeWidth}";
        if (Recall(key) is { } hit) return Task.FromResult<Bitmap?>(hit);
        // Не загрузилась недавно — не дёргаем сеть на каждой перерисовке; через несколько минут попробуем снова.
        if (Failed.TryGetValue(key, out var failedAt) && DateTime.UtcNow - failedAt < RetryFailedAfter) return Task.FromResult<Bitmap?>(null);
        var task = Pending.GetOrAdd(key, _ => Task.Run(() => Load(url, decodeWidth, key)));
        // Задача могла закончиться раньше, чем попала в словарь, — тогда убираем её сами.
        if (task.IsCompleted) Pending.TryRemove(new KeyValuePair<string, Task<Bitmap?>>(key, task));
        return task;
    }

    static async Task<Bitmap?> Load(string url, int decodeWidth, string key)
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
                    await File.WriteAllBytesAsync(file, await Download(url));
                }
                finally { Parallel.Release(); }
            }
            if (new FileInfo(file).Length > MaxFileBytes) throw new InvalidDataException("image too big");
            await using var stream = File.OpenRead(file);
            bmp = Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch { try { File.Delete(file); } catch { } }
        if (bmp is not null) Remember(key, bmp);
        else
        {
            if (Failed.Count > 4000) Failed.Clear();
            Failed[key] = DateTime.UtcNow;
        }
        Pending.TryRemove(key, out Task<Bitmap?>? _);
        return bmp;
    }

    /// <summary>
    /// Скачать картинку: не дольше 30 секунд и не больше MaxFileBytes. Общий
    /// HttpClient ждёт до получаса (для архивов модов), и одна зависшая
    /// картинка надолго занимала одно из шести мест в очереди.
    /// </summary>
    static async Task<byte[]> Download(string url)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxFileBytes) throw new InvalidDataException("image too big");
        await using var input = await response.Content.ReadAsStreamAsync(cts.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, cts.Token)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFileBytes) throw new InvalidDataException("image too big");
        }
        return buffer.ToArray();
    }

    /// <summary>Картинка из сети, если она ещё в памяти (заодно — «недавно показанная»).</summary>
    static Bitmap? Recall(string key)
    {
        lock (NetLock)
        {
            if (!Net.TryGetValue(key, out var node)) return null;
            Recent.Remove(node);
            Recent.AddFirst(node);
            return node.Value.Bitmap;
        }
    }

    /// <summary>
    /// «Вес» картинки для сборщика мусора. Пиксели лежат в памяти Skia, и сборщик
    /// видит только маленький объект Bitmap — с ним он мог не приходить минутами,
    /// пока память процесса росла на сотни мегабайт. AddMemoryPressure сообщает
    /// ему настоящий размер, а когда картинку соберут, вес снимается.
    /// </summary>
    sealed class Pressure
    {
        readonly long _bytes;
        public Pressure(long bytes) { _bytes = bytes; GC.AddMemoryPressure(bytes); }
        ~Pressure() => GC.RemoveMemoryPressure(_bytes);
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Bitmap, Pressure> Weights = new();

    static void Remember(string key, Bitmap bmp)
    {
        var bytes = Math.Max(1L, (long)bmp.PixelSize.Width * bmp.PixelSize.Height * 4);
        Weights.AddOrUpdate(bmp, new Pressure(bytes));
        var collect = false;
        lock (NetLock)
        {
            if (Net.Remove(key, out var old))
            {
                Recent.Remove(old);
                _netBytes -= old.Value.Bytes;
            }
            Net[key] = Recent.AddFirst(new NetEntry(key, bmp, bytes));
            _netBytes += bytes;
            // Самые давно показанные — из памяти вон (только что загруженную оставляем всегда).
            while (_netBytes > NetBudget && Recent.Last is { } last && last != Recent.First)
            {
                Recent.RemoveLast();
                Net.Remove(last.Value.Key);
                _netBytes -= last.Value.Bytes;
                _evictedSinceGc += last.Value.Bytes;
            }
            // Набралось много вытесненного (быстро листают каталог) — зовём сборщик сами,
            // не дожидаясь, пока он решит прийти.
            if (_evictedSinceGc > 64L * 1024 * 1024 && DateTime.UtcNow - _lastGc > TimeSpan.FromSeconds(2))
            {
                _evictedSinceGc = 0;
                _lastGc = DateTime.UtcNow;
                collect = true;
            }
        }
        if (collect) GC.Collect(2, GCCollectionMode.Forced, blocking: false);
    }

    /// <summary>Отпустить все картинки из сети (например, когда памяти не хватило). Встроенные остаются.</summary>
    public static void Trim()
    {
        lock (NetLock)
        {
            Net.Clear();
            Recent.Clear();
            _netBytes = 0;
            _evictedSinceGc = 0;
            _lastGc = DateTime.UtcNow;
        }
        GC.Collect(2, GCCollectionMode.Forced, blocking: false);
    }

    /// <summary>Сколько сейчас занимают картинки из сети (для самопроверки).</summary>
    public static long NetBytes { get { lock (NetLock) return _netBytes; } }
}
