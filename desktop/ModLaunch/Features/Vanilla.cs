using System.Diagnostics;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>
/// «Играть без модов» и «Очистить игру» (как Purge в Vortex). Ничего не удаляем —
/// только откладываем файлы загрузчика в сторону (в папку игры ModHub/vanilla)
/// и возвращаем их обратно. Что отложено — записано в vanilla.json в папке данных,
/// поэтому даже после вылета программы всё можно вернуть.
/// </summary>
public static class Vanilla
{
    static readonly JsonFile State = new(Path.Combine(Paths.DataDir, "vanilla.json"), () => new JsonObject { ["games"] = new JsonObject() });

    static JsonObject Games => State.Data.Obj("games");

    /// <summary>Как «очищена» игра: temp — на один запуск без модов, purge — пока человек не вернёт моды.</summary>
    public static string? Mode(string gameId) => (Games[gameId] as JsonObject).Str("mode");

    public static bool IsPurged(string gameId) => Mode(gameId) == "purge";

    /// <summary>Загрузчик сейчас отложен (игра запустится без модов).</summary>
    public static bool IsParked(string gameId) => Games[gameId] is JsonObject;

    /// <summary>Моды, которые выключили при очистке, — их включим обратно.</summary>
    public static List<string> PurgedMods(string gameId) =>
        (Games[gameId] as JsonObject).Arr("mods").Select(x => x?.ToString()).OfType<string>().ToList();

    /// <summary>Файлы загрузчика, без которых игра стартует без модов (относительно папки игры).</summary>
    public static List<string> LoaderFiles(GameDef game, string gamePath) => game.Loader switch
    {
        // Doorstop BepInEx подхватывается через winhttp.dll (у старых сборок — version.dll).
        LoaderKind.Bepinex => new[] { "winhttp.dll", "version.dll", "doorstop_config.ini" }
            .Where(f => File.Exists(Path.Combine(gamePath, f))).ToList(),
        // Modding API заменяет Assembly-CSharp.dll: на время кладём родной из .vanilla.
        LoaderKind.HkApi when File.Exists(Path.Combine(Loaders.HkApi.ManagedDir(gamePath), "Assembly-CSharp.dll.vanilla")) =>
            [Path.GetRelativePath(gamePath, Path.Combine(Loaders.HkApi.ManagedDir(gamePath), "Assembly-CSharp.dll"))],
        _ => [],
    };

    /// <summary>Можно ли запустить игру без модов (у Stardew — просто другой exe).</summary>
    public static bool Supported(GameDef game, string gamePath) => game.Loader switch
    {
        LoaderKind.Smapi => File.Exists(Path.Combine(gamePath, "Stardew Valley.exe")),
        LoaderKind.None => false,
        _ => LoaderFiles(game, gamePath).Count > 0 || IsParked(game.Id),
    };

    /// <summary>Exe без загрузчика: у Stardew — сама игра вместо SMAPI, у остальных — тот же.</summary>
    public static string Exe(GameDef game, string gamePath) =>
        game.Loader == LoaderKind.Smapi && File.Exists(Path.Combine(gamePath, "Stardew Valley.exe"))
            ? Path.Combine(gamePath, "Stardew Valley.exe")
            : game.LaunchExe(gamePath);

    static string Shelf(string gamePath) => Path.Combine(gamePath, "ModHub", "vanilla");

    /// <summary>Отложить загрузчик. mode: temp или purge; mods — выключенные при очистке.</summary>
    public static void Park(GameDef game, string gamePath, string mode, IEnumerable<string>? mods = null)
    {
        var entry = Games[game.Id] as JsonObject;
        if (entry is null)
        {
            var moved = new JsonArray();
            var shelf = Shelf(gamePath);
            try
            {
                // У Hollow Knight в списке только Assembly-CSharp.dll и только если есть родной .vanilla:
                // без Modding API менять нечего, иначе игра осталась бы совсем без dll.
                foreach (var rel in LoaderFiles(game, gamePath))
                {
                    Directory.CreateDirectory(shelf);
                    var file = Path.Combine(gamePath, rel);
                    File.Move(file, Path.Combine(shelf, Path.GetFileName(rel)), true);
                    moved.Add((JsonNode)rel);
                    // Родной Assembly-CSharp на место модифицированного (как «Toggle API» в Scarab).
                    if (game.Loader == LoaderKind.HkApi) File.Copy(file + ".vanilla", file, true);
                }
            }
            catch
            {
                // Что-то не отложилось (файл занят) — возвращаем уже отложенное, чтобы загрузчик не потерялся.
                foreach (var rel in moved.Select(x => x?.ToString()).OfType<string>())
                    try { File.Move(Path.Combine(shelf, Path.GetFileName(rel)), Path.Combine(gamePath, rel), true); } catch { }
                throw;
            }
            entry = new JsonObject { ["path"] = gamePath, ["files"] = moved, ["at"] = DateTime.UtcNow.ToString("o") };
            Games[game.Id] = entry;
        }
        entry["mode"] = mode;
        if (mods is not null) entry["mods"] = new JsonArray(mods.Select(m => (JsonNode)m).ToArray());
        State.Save();
    }

    /// <summary>Вернуть загрузчик на место. Файл, появившийся заново (загрузчик переставили), не трогаем.</summary>
    public static void Unpark(GameDef game)
    {
        if (Games[game.Id] is not JsonObject entry) return;
        var gamePath = entry.Str("path");
        if (gamePath is not null && Directory.Exists(gamePath))
        {
            foreach (var rel in entry.Arr("files").Select(x => x?.ToString()).OfType<string>())
            {
                var shelved = Path.Combine(Shelf(gamePath), Path.GetFileName(rel));
                if (!File.Exists(shelved)) continue;
                var target = Path.Combine(gamePath, rel);
                if (game.Loader == LoaderKind.HkApi) File.Move(shelved, target, true);
                else if (!File.Exists(target)) File.Move(shelved, target);
            }
        }
        Games.Remove(game.Id);
        State.Save();
    }

    /// <summary>Вернуть всё, что осталось отложенным «на один запуск» (программа закрылась, пока шла игра).</summary>
    public static void RestoreLeftovers()
    {
        foreach (var id in Games.Select(kv => kv.Key).ToList())
        {
            if (Mode(id) != "temp") continue;
            var def = GameCatalog.ById(id);
            if (def is null) { Games.Remove(id); State.Save(); continue; }
            // Игра ещё идёт — вернём, когда она закроется.
            if (GameProcessRunning(def)) { _ = Task.Run(() => RestoreAfterExit(def, TimeSpan.MaxValue)); continue; }
            try { Unpark(def); } catch { }
        }
    }

    // ---------------------------------------------------------------- очистка (purge)

    /// <summary>Очистить игру: выключить все моды и отложить загрузчик. Возвращает, сколько модов выключено.</summary>
    public static int Purge(GameState g)
    {
        if (g.Path is null || g.Registry is null) return 0;
        var registry = g.Registry;
        // Сначала загрузчик: если его не отложить (файл занят), моды остаются как были.
        Park(g.Def, g.Path, "purge");
        var off = new List<string>();
        // Запись без своей папки указала бы на всю папку модов — такую не трогаем (загрузчик и так отложен).
        foreach (var mod in registry.List().Where(m => m.Bool("enabled", true) && !m.Bool("missing") && registry.OwnFolder(m) is not null))
        {
            var id = mod.Str("id")!;
            try
            {
                registry.SetEnabled(id, false);
                off.Add(id);
            }
            catch { }
        }
        if (registry.List().Any(m => m.Str("kind") == "preset")) Installer.SyncPreset(registry, null);
        Park(g.Def, g.Path, "purge", off.Concat(PurgedMods(g.Def.Id)).Distinct());
        g.Refresh();
        return off.Count;
    }

    /// <summary>Вернуть моды после очистки: загрузчик и всё, что было включено.</summary>
    public static int RestorePurge(GameState g)
    {
        var mods = PurgedMods(g.Def.Id);
        Unpark(g.Def);
        var on = 0;
        if (g.Registry is { } registry)
        {
            foreach (var id in mods)
            {
                if (registry.Get(id) is not { } mod) continue;
                // Пока игра была чистой, на место мода положили свою папку (или файл) с тем же именем — не затираем.
                if (!mod.Bool("enabled", true) && mod.Str("target") is null && mod.Str("kind") != "preset" && mod.Str("folder") is { Length: > 0 } folder
                    && (Directory.Exists(Path.Combine(registry.ModsDir, folder)) || File.Exists(Path.Combine(registry.ModsDir, folder)))) continue;
                try { registry.SetEnabled(id, true); on++; } catch { }
            }
            if (registry.List().Any(m => m.Str("kind") == "preset")) Installer.SyncPreset(registry, null);
        }
        g.Refresh();
        return on;
    }

    // ---------------------------------------------------------------- «играть без модов»

    /// <summary>
    /// Игра закрылась — возвращаем загрузчик. Если Steam перезапустил игру сам
    /// (процесс закрылся за секунды), ждём, пока закроется и настоящий.
    /// </summary>
    public static async Task RestoreAfterExit(GameDef game, TimeSpan ran)
    {
        if (Mode(game.Id) != "temp") return;
        // Закрылась за секунды — похоже, Steam перезапускает её сам: даём ему время.
        if (ran < TimeSpan.FromSeconds(20)) await Task.Delay(15000);
        // Ждём до двух часов: дольше загрузчик «без модов» не держим.
        for (var i = 0; i < 2400 && GameProcessRunning(game); i++) await Task.Delay(3000);
        // Возвращаем в потоке окна: список отложенного в это же время читает и интерфейс.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Пока ждали, игру могли «очистить» — тогда загрузчик остаётся отложенным, а список модов цел.
            if (Mode(game.Id) == "temp") try { Unpark(game); } catch { }
            AppState.Games.FirstOrDefault(x => x.Def.Id == game.Id)?.Refresh();
            AppState.Notify();
        });
    }

    static bool GameProcessRunning(GameDef game)
    {
        // Только exe этой игры: Stardew загрузчик не откладывает, а её процесс держал бы чужие игры «без модов».
        foreach (var exe in game.Executables)
        {
            try
            {
                if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length > 0) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>Проверка без диска: какие файлы отложит «без модов» и какой exe запустит.</summary>
    [SelfTest]
    static string ParkAndRestore()
    {
        var dir = Path.Combine(Paths.DataDir, "vanilla-test", "Game");
        Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(dir, "winhttp.dll"), "doorstop");
        File.WriteAllText(Path.Combine(dir, "doorstop_config.ini"), "[General]");
        var game = GameCatalog.ById("lethal-company")!;
        if (LoaderFiles(game, dir).Count != 2) throw new Exception("loader files not found");
        Park(game, dir, "temp");
        if (File.Exists(Path.Combine(dir, "winhttp.dll"))) throw new Exception("winhttp.dll still in place");
        if (!IsParked(game.Id) || Mode(game.Id) != "temp") throw new Exception("state not saved");
        Unpark(game);
        if (File.ReadAllText(Path.Combine(dir, "winhttp.dll")) != "doorstop") throw new Exception("winhttp.dll not restored");
        if (IsParked(game.Id)) throw new Exception("state not cleared");
        File.WriteAllText(Path.Combine(dir, "Stardew Valley.exe"), "");
        var stardew = GameCatalog.ById("stardew-valley")!;
        if (Path.GetFileName(Exe(stardew, dir)) != "Stardew Valley.exe") throw new Exception("stardew vanilla exe is wrong");
        return "parked 2 loader files and restored them; Stardew starts without SMAPI";
    }

    /// <summary>Hollow Knight без Modding API и занятый файл: загрузчик и игра не должны пострадать.</summary>
    [SelfTest]
    static string ParkNeverLosesFiles()
    {
        // Modding API не стоит (нет .vanilla) — очистка не должна уносить Assembly-CSharp.dll.
        var hkDir = Path.Combine(Paths.DataDir, "vanilla-test", "Hollow Knight");
        var managed = Directory.CreateDirectory(Path.Combine(hkDir, "hollow_knight_Data", "Managed")).FullName;
        File.WriteAllText(Path.Combine(managed, "Assembly-CSharp.dll"), "game");
        var hk = GameCatalog.ById("hollow-knight")!;
        try { Park(hk, hkDir, "purge"); } catch { }
        if (File.ReadAllText(Path.Combine(managed, "Assembly-CSharp.dll")) != "game") throw new Exception("Assembly-CSharp.dll was taken away");
        Unpark(hk);

        // Второй файл занят — первый (winhttp.dll) должен вернуться на место.
        var dir = Path.Combine(Paths.DataDir, "vanilla-test", "Locked");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "winhttp.dll"), "doorstop");
        File.WriteAllText(Path.Combine(dir, "doorstop_config.ini"), "[General]");
        var game = GameCatalog.ById("valheim")!;
        using (new FileStream(Path.Combine(dir, "doorstop_config.ini"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { Park(game, dir, "temp"); } catch (IOException) { }
        }
        if (IsParked(game.Id)) throw new Exception("park should fail while the file is busy");
        if (!File.Exists(Path.Combine(dir, "winhttp.dll"))) throw new Exception("winhttp.dll lost after a failed park");
        return "no Modding API: game dll kept; locked file: loader put back";
    }

    /// <summary>Пока игра была чистой, человек положил папку с тем же именем — «Вернуть моды» её не затирает.</summary>
    [SelfTest]
    static string RestoreKeepsHandPlacedFolder()
    {
        var game = GameCatalog.ById("gtfo")!;
        var dir = Path.Combine(Paths.DataDir, "vanilla-test", "GTFO");
        var plugin = Path.Combine(dir, "BepInEx", "plugins", "ModA");
        Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "core"));
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(dir, "winhttp.dll"), "doorstop");
        File.WriteAllText(Path.Combine(plugin, "ModA.dll"), "mod");
        var g = new GameState { Def = game, Path = dir, Status = Detect.Found };
        g.Registry!.Add(new JsonObject { ["id"] = "Owner-ModA", ["name"] = "ModA", ["folder"] = "ModA" });
        // Старая запись без папки: её выключение унесло бы всю папку plugins.
        g.Registry!.Add(new JsonObject { ["id"] = "legacy", ["name"] = "Legacy" });
        if (Purge(g) != 1 || Directory.Exists(plugin) || File.Exists(Path.Combine(dir, "winhttp.dll"))) throw new Exception("purge did not clean the game");
        if (!Directory.Exists(Path.Combine(dir, "BepInEx", "plugins"))) throw new Exception("the whole plugins folder was moved");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(plugin).FullName, "mine.txt"), "by hand");
        RestorePurge(g);
        if (!File.Exists(Path.Combine(plugin, "mine.txt"))) throw new Exception("hand-placed folder was overwritten");
        if (File.ReadAllText(Path.Combine(dir, "winhttp.dll")) != "doorstop") throw new Exception("loader not restored");
        return "purged 1 mod, restored the loader, hand-placed ModA kept";
    }
}
