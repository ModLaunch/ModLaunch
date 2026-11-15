using System.IO.Compression;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>
/// 31.0: критичные пути целиком, без сети — так, как их проходит человек: настоящий архив мода →
/// установка → правка настроек → обновление → откат на прежнюю версию → удаление → возврат.
/// </summary>
static class CriticalPathTests
{
    static string Zip(string dir, string name, params (string Path, string Text)[] files)
    {
        var path = Path.Combine(dir, name);
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, text) in files)
            using (var w = new StreamWriter(zip.CreateEntry(p).Open())) w.Write(text);
        return path;
    }

    static string Manifest(string version) =>
        $$"""{ "Name": "Cool Mod", "Author": "Tester", "Version": "{{version}}", "UniqueID": "Tester.CoolMod", "EntryDll": "CoolMod.dll" }""";

    [SelfTest]
    static string ModInstallUpdateRollback()
    {
        var root = Path.Combine(Paths.DataDir, "critical-mods");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var game = Directory.CreateDirectory(Path.Combine(root, "Stardew Valley")).FullName;
        var registry = new ModRegistry(Games.GameCatalog.ById("stardew-valley")!, game);
        var meta = new JsonObject { ["id"] = "nexus:stardewvalley:99999", ["name"] = "Cool Mod", ["source"] = "nexus" };
        var id = "nexus:stardewvalley:99999";
        // Реестр общий на игру (по id): за собой убираем, чтобы не мешать другим проверкам Stardew.
        try { return Run(registry, root, meta, id); }
        finally
        {
            if (registry.Has(id)) registry.Remove(id);
            ModSafe.Clear(registry);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static string Run(ModRegistry registry, string root, JsonObject meta, string id)
    {

        // 1. Установка из архива: папка мода, запись с версией из manifest.json.
        var v1 = Zip(root, "CoolMod-1.0.0.zip", ("Cool Mod/manifest.json", Manifest("1.0.0")), ("Cool Mod/CoolMod.dll", "dll v1"));
        Installer.InstallArchive(registry, v1, (JsonObject)meta.DeepClone());
        var folder = Path.Combine(registry.ModsDir, registry.Get(id).Str("folder") ?? "?");
        if (registry.Get(id).Str("version") != "1.0.0" || File.ReadAllText(Path.Combine(folder, "CoolMod.dll")) != "dll v1")
            throw new Exception("v1 not installed: " + registry.Get(id)?.ToJsonString());

        // 2. Человек настроил мод.
        File.WriteAllText(Path.Combine(folder, "config.json"), """{ "speed": 7 }""");

        // 3. Обновление на 2.0.0: новые файлы, старая версия — в сейфе.
        var v2 = Zip(root, "CoolMod-2.0.0.zip", ("Cool Mod/manifest.json", Manifest("2.0.0")), ("Cool Mod/CoolMod.dll", "dll v2"));
        Installer.InstallArchive(registry, v2, (JsonObject)meta.DeepClone());
        if (registry.Get(id).Str("version") != "2.0.0" || File.ReadAllText(Path.Combine(folder, "CoolMod.dll")) != "dll v2")
            throw new Exception("v2 not installed");
        if (ModSafe.Find(registry, id, "1.0.0") is not { } old) throw new Exception("v1 not kept for rollback");

        // 4. Откат: снова 1.0.0 с настройками человека, а 2.0.0 теперь в сейфе.
        if (!ModSafe.Restore(registry, old)) throw new Exception("rollback failed");
        if (registry.Get(id).Str("version") != "1.0.0" || File.ReadAllText(Path.Combine(folder, "CoolMod.dll")) != "dll v1")
            throw new Exception("rollback left wrong files");
        if (File.ReadAllText(Path.Combine(folder, "config.json")) != """{ "speed": 7 }""") throw new Exception("settings lost on rollback");
        if (ModSafe.Find(registry, id, "2.0.0") is null) throw new Exception("v2 not kept after rollback");

        // 5. Удаление и возврат удалённого.
        ModSafe.Remove(registry, folder, registry.Get(id), "remove");
        registry.Remove(id);
        if (Directory.Exists(folder) || registry.Has(id)) throw new Exception("mod not removed");
        if (ModSafe.List(registry).FirstOrDefault(i => i.Id == id && i.Version == "1.0.0") is not { } removed || !ModSafe.Restore(registry, removed))
            throw new Exception("removed mod not restorable");
        if (!registry.Has(id) || File.ReadAllText(Path.Combine(folder, "CoolMod.dll")) != "dll v1") throw new Exception("restored mod incomplete");

        // 6. Битый архив не ломает установленное.
        var broken = Path.Combine(root, "broken.zip");
        File.WriteAllBytes(broken, [1, 2, 3, 4]);
        try { Installer.InstallArchive(registry, broken, (JsonObject)meta.DeepClone()); throw new Exception("broken archive accepted"); }
        catch (Exception e) when (e.Message != "broken archive accepted") { }
        if (registry.Get(id).Str("version") != "1.0.0" || !File.Exists(Path.Combine(folder, "CoolMod.dll"))) throw new Exception("broken archive damaged the mod");
        return "zip → install 1.0.0 → user config → update 2.0.0 → rollback 1.0.0 (config kept) → remove → restore; broken zip refused";
    }
}
