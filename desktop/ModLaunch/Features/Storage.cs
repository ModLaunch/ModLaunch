using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Сколько места занимает ModLaunch и что из этого можно безопасно очистить (кэш картинок, логи).</summary>
public static class Storage
{
    /// <summary>Часть данных: ключ для подписи, папка, можно ли очищать без потерь.</summary>
    public sealed record Part(string Key, string Dir, bool Clearable, long Bytes);

    public static List<Part> Measure()
    {
        var root = Paths.DataDir;
        (string Key, string Dir, bool Clear)[] parts =
        [
            ("downloads", Path.Combine(root, "downloads"), false), // чистится своей кнопкой «Архив загрузок»
            ("backups", Path.Combine(root, "backups"), false),
            ("cache", Path.Combine(root, "cache"), true),
            ("logs", Path.Combine(root, "logs"), true),
            ("reshade", Path.Combine(root, "reshade"), false),
        ];
        return parts.Select(p => new Part(p.Key, p.Dir, p.Clear, DirSize(p.Dir))).ToList();
    }

    public static long DirSize(string dir)
    {
        long total = 0;
        try
        {
            if (!Directory.Exists(dir)) return 0;
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                try { total += f.Length; } catch { }
        }
        catch { }
        return total;
    }

    /// <summary>Удалить содержимое папки (саму папку оставить). Файлы, занятые программой, пропускаются. Возвращает освобождённые байты.</summary>
    public static long Clear(string dir)
    {
        var before = DirSize(dir);
        try
        {
            if (!Directory.Exists(dir)) return 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) try { File.Delete(f); } catch { }
            foreach (var d in Directory.EnumerateDirectories(dir)) try { Directory.Delete(d, true); } catch { }
        }
        catch { }
        return Math.Max(0, before - DirSize(dir));
    }

    [SelfTest]
    static string ClearsOnlyContents()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DataDir, "storage-test", "sub")).Parent!.FullName;
        File.WriteAllText(Path.Combine(dir, "a.bin"), new string('x', 1000));
        File.WriteAllText(Path.Combine(dir, "sub", "b.bin"), new string('y', 500));
        if (DirSize(dir) != 1500) throw new Exception("size wrong: " + DirSize(dir));
        var freed = Clear(dir);
        if (freed != 1500 || !Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) throw new Exception("not emptied");
        Directory.Delete(dir);
        return "1500 bytes counted and freed, folder kept";
    }
}
