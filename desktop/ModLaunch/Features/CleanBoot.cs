using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Features;

/// <summary>Итог чистки: сколько файлов и папок убрано, сколько места освободилось, сколько модов в карантине.</summary>
public sealed record CleanResult(int Files, long Bytes, int Quarantined)
{
    public bool Any => Files > 0 || Quarantined > 0;
}

/// <summary>
/// 10.0: «чистая загрузка» — перед каждым запуском ModLaunch сам прибирает папку игры. Трогает
/// только то, что добавили ModLaunch, загрузчик и моды, и никогда — файлы самой игры:
/// кэш и логи загрузчика (BepInEx/cache, LogOutput.log), недокачанные и временные файлы в папке
/// модов, пустые папки, старые копии настроек ModLaunch (оставляем пять последних). По желанию —
/// убирает в карантин моды, поставленные вручную мимо ModLaunch; их можно вернуть одной кнопкой.
/// По умолчанию выключена (Настройки → Запуск).
/// </summary>
public static class CleanBoot
{
    public static bool On => Settings.Data.Bool("cleanBoot");
    public static bool Quarantine => Settings.Data.Bool("cleanBootUnknown");

    static readonly string[] JunkExt = [".tmp", ".partial", ".crdownload", ".download"];

    static string QuarantineDir(string gameId) => Path.Combine(Paths.DataDir, "cleanboot", gameId);

    /// <summary>Что убрала бы чистка сейчас (без изменений) — для кнопки «Показать».</summary>
    public static List<string> Preview(GameState g) => Collect(g).Select(x => x.Path).ToList();

    static List<(string Path, bool Dir)> Collect(GameState g)
    {
        var list = new List<(string, bool)>();
        if (g.Path is not { } root || !Directory.Exists(root) || g.Def.IsMinecraft) return list;
        void File(string rel) { var p = System.IO.Path.Combine(root, rel); if (System.IO.File.Exists(p)) list.Add((p, false)); }
        void DirContents(string rel)
        {
            var p = System.IO.Path.Combine(root, rel);
            if (!Directory.Exists(p)) return;
            try { foreach (var e in Directory.EnumerateFileSystemEntries(p)) list.Add((e, Directory.Exists(e))); } catch { }
        }
        if (g.Def.Loader == LoaderKind.Bepinex)
        {
            DirContents(System.IO.Path.Combine("BepInEx", "cache"));
            File(System.IO.Path.Combine("BepInEx", "LogOutput.log"));
            File(System.IO.Path.Combine("BepInEx", "ErrorLog.log"));
        }
        var mods = g.Registry?.ModsDir;
        if (mods is not null && Directory.Exists(mods) && Inside(mods, root))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(mods, "*", SearchOption.AllDirectories).Take(20_000))
                    if (JunkExt.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())) list.Add((f, false));
                foreach (var d in Directory.EnumerateDirectories(mods, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Take(5_000))
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) list.Add((d, true));
            }
            catch { }
        }
        var backups = System.IO.Path.Combine(root, "ModHub", "config-backup");
        if (Directory.Exists(backups))
            try { foreach (var d in Directory.GetDirectories(backups).OrderByDescending(d => d, StringComparer.Ordinal).Skip(5)) list.Add((d, true)); } catch { }
        return list;
    }

    static bool Inside(string path, string root) =>
        System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Почистить игру. Ошибки отдельных файлов (занят, нет прав) пропускаются — запуск из-за них не срывается.</summary>
    public static CleanResult Run(GameState g, bool quarantine = true)
    {
        if (g.Path is not { } root) return new(0, 0, 0);
        int files = 0, quarantined = 0;
        long bytes = 0;
        foreach (var (path, dir) in Collect(g))
        {
            if (!Inside(path, root)) continue;
            try
            {
                if (dir)
                {
                    bytes += Conflicts.FolderSize(path);
                    Directory.Delete(path, true);
                }
                else
                {
                    bytes += new FileInfo(path).Length;
                    System.IO.File.Delete(path);
                }
                files++;
            }
            catch { }
        }
        if (quarantine && Quarantine && g.Registry is { } registry && Directory.Exists(registry.ModsDir))
        {
            var target = System.IO.Path.Combine(QuarantineDir(g.Def.Id), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            foreach (var name in registry.Unmanaged())
            {
                var from = System.IO.Path.Combine(registry.ModsDir, name);
                try
                {
                    Directory.CreateDirectory(target);
                    MoveDir(from, System.IO.Path.Combine(target, name));
                    quarantined++;
                }
                catch { }
            }
        }
        var result = new CleanResult(files, bytes, quarantined);
        if (result.Any)
        {
            Settings.Data.Obj("cleanBootLast")[g.Def.Id] = $"{DateTime.UtcNow:o}|{files}|{bytes}|{quarantined}";
            Settings.Save();
        }
        return result;
    }

    /// <summary>Перенос папки и между дисками (Directory.Move работает только в пределах одного).</summary>
    static void MoveDir(string from, string to)
    {
        try { Directory.Move(from, to); return; }
        catch (IOException) { }
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var dest = System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, f));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            System.IO.File.Copy(f, dest, true);
        }
        Directory.Delete(from, true);
    }

    /// <summary>Моды в карантине (папки) — из всех чисток этой игры.</summary>
    public static List<string> Quarantined(string gameId)
    {
        var dir = QuarantineDir(gameId);
        if (!Directory.Exists(dir)) return [];
        try { return Directory.GetDirectories(dir).SelectMany(Directory.GetDirectories).ToList(); } catch { return []; }
    }

    /// <summary>Вернуть всё из карантина обратно в папку модов. Возвращает, сколько модов вернулось.</summary>
    public static int Restore(GameState g)
    {
        if (g.Registry is not { } registry) return 0;
        var n = 0;
        foreach (var path in Quarantined(g.Def.Id))
        {
            var dest = System.IO.Path.Combine(registry.ModsDir, System.IO.Path.GetFileName(path));
            if (Directory.Exists(dest)) continue;
            try { Directory.CreateDirectory(registry.ModsDir); MoveDir(path, dest); n++; } catch { }
        }
        try
        {
            foreach (var stamp in Directory.GetDirectories(QuarantineDir(g.Def.Id)))
                if (!Directory.EnumerateFileSystemEntries(stamp).Any()) Directory.Delete(stamp);
        }
        catch { }
        return n;
    }

    [SelfTest]
    static string CleanBootKeepsGameFiles()
    {
        var root = System.IO.Path.Combine(Paths.DataDir, "cleanboot-test", "Lethal Company");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var def = GameCatalog.ById("lethal-company")!;
        var plugins = Directory.CreateDirectory(def.ModsDir(root)).FullName;
        Directory.CreateDirectory(System.IO.Path.Combine(root, "BepInEx", "cache"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "BepInEx", "cache", "chainloader.dat"), "x");
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "BepInEx", "LogOutput.log"), "log");
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Lethal Company.exe"), "game");
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "winhttp.dll"), "doorstop");
        Directory.CreateDirectory(System.IO.Path.Combine(plugins, "Empty"));
        Directory.CreateDirectory(System.IO.Path.Combine(plugins, "Real"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(plugins, "Real", "Real.dll"), "mod");
        System.IO.File.WriteAllText(System.IO.Path.Combine(plugins, "Real", "half.dll.partial"), "half");
        var g = new GameState { Def = def, Path = root, Status = Detect.Found };
        var r = Run(g);
        if (!System.IO.File.Exists(System.IO.Path.Combine(root, "Lethal Company.exe")) || !System.IO.File.Exists(System.IO.Path.Combine(root, "winhttp.dll"))) throw new Exception("game files touched");
        if (!System.IO.File.Exists(System.IO.Path.Combine(plugins, "Real", "Real.dll"))) throw new Exception("mod removed");
        if (System.IO.File.Exists(System.IO.Path.Combine(root, "BepInEx", "LogOutput.log")) || Directory.Exists(System.IO.Path.Combine(plugins, "Empty")) || System.IO.File.Exists(System.IO.Path.Combine(plugins, "Real", "half.dll.partial")))
            throw new Exception("junk left");
        Directory.Delete(System.IO.Path.Combine(Paths.DataDir, "cleanboot-test"), true);
        return $"removed {r.Files} junk items, game and mods untouched";
    }
}
