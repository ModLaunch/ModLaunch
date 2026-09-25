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
    static readonly SemaphoreSlim Parallel = new(6);

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

    /// <summary>Обложка игры, если она уже под рукой (из ресурсов или кэша); из сети — через Ui.GameImage.</summary>
    public static Bitmap? Game(Games.GameDef def, int width)
    {
        if (def.Art is not null) return Asset(def.Art, width);
        if (def.ArtUrl is null) return null;
        var task = FromUrl(def.ArtUrl, width);
        return task.IsCompleted ? task.Result : null;
    }

    public static Task<Bitmap?> FromUrl(string? url, int decodeWidth = 160)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult<Bitmap?>(null);
        var key = $"{url}:{decodeWidth}";
        if (Memory.TryGetValue(key, out var hit)) return Task.FromResult(hit);
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
                        var bytes = await Http.Client.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(file, bytes);
                    }
                    finally { Parallel.Release(); }
                }
                await using var stream = File.OpenRead(file);
                bmp = Bitmap.DecodeToWidth(stream, decodeWidth);
            }
            catch { try { File.Delete(file); } catch { } }
            Memory[key] = bmp;
            Pending.TryRemove(key, out Task<Bitmap?>? _);
            return bmp;
        }));
    }
}
