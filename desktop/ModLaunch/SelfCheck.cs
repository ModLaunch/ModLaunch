using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Loaders;
using ModLaunch.Mods;
using ModLaunch.Sources;

namespace ModLaunch;

/// <summary>
/// --selfcheck: живые каталоги и установка в пробную папку. Запускается на CI
/// под Windows — там, где есть сеть, — и падает, если что-то не работает.
/// </summary>
public static class SelfCheck
{
    public static async Task<int> Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "modlaunch-selfcheck");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Environment.SetEnvironmentVariable("MODLAUNCH_DATA", Path.Combine(root, "data"));
        var failed = 0;

        async Task Check(string name, Func<Task<string>> body)
        {
            try { Console.WriteLine($"ok   {name}: {await body()}"); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
        }

        await Check("thunderstore search", async () =>
        {
            var page = await Thunderstore.Search("lethal-company", new Query(), []);
            if (page.Mods.Count < 10) throw new Exception($"only {page.Mods.Count}");
            return $"{page.Total} mods, first {page.Mods[0].Name}";
        });
        await Check("thunderstore sections", async () =>
        {
            var page = await Catalog.Browse(GameCatalog.ById("valheim")!, new Query(SectionId: "buildings"));
            if (page.Mods.Count == 0) throw new Exception("empty");
            return $"{page.Total} building mods";
        });
        await Check("thunderstore deps", async () =>
        {
            var (order, missing) = await Thunderstore.Resolve("lethal-company", "notnotnotswipez-MoreCompany");
            return $"{order.Count} to install, {missing.Count} missing: {string.Join(", ", order.Select(m => m.Id))}";
        });
        await Check("nexus browse", async () =>
        {
            var page = await Nexus.Browse("subnautica", new Query(), [], []);
            if (page.Mods.Count < 10) throw new Exception($"only {page.Mods.Count}");
            return $"{page.Total} mods, first {page.Mods[0].Name}";
        });
        await Check("nexus section", async () =>
        {
            var g = GameCatalog.ById("stardew-valley")!;
            var page = await Catalog.Browse(g, new Query(SectionId: "cosmetics"));
            return $"{page.Total} cosmetics";
        });
        await Check("nexus search", async () =>
        {
            var page = await Nexus.Browse("subnautica", new Query("Nautilus"), [], []);
            return $"{page.Total} hits, first {page.Mods.FirstOrDefault()?.Name}";
        });
        await Check("nexus details", async () =>
        {
            var (mod, file, reqs) = await Nexus.Details("subnautica", 1155, "1262");
            if (mod is null || file is null) throw new Exception("no mod or file");
            return $"{mod.Name} file {file.FileId} {file.Version}, {reqs.Count} requirements";
        });
        await Check("picks", async () =>
        {
            var g = GameCatalog.ById("subnautica")!;
            var mods = await Catalog.Many(g, g.Picks.Take(8));
            if (mods.Count < 6) throw new Exception($"only {mods.Count}");
            return string.Join(", ", mods.Select(m => m.Name));
        });
        await Check("modlinks", async () =>
        {
            var page = await ModLinks.Search(new Query(), []);
            var (order, _) = await ModLinks.Resolve("Custom Knight");
            return $"{page.Total} mods; Custom Knight needs {order.Count - 1} deps";
        });
        await Check("modlinks api", async () => (await ModLinks.LoadApi()).Version);

        // Установка в пробную папку игры: BepInEx, мод с зависимостями, выключение, удаление.
        await Check("bepinex + mod install", async () =>
        {
            var game = GameCatalog.ById("lethal-company")!;
            var dir = Path.Combine(root, "Lethal Company");
            Directory.CreateDirectory(Path.Combine(dir, "Lethal Company_Data"));
            var steps = new Progress<InstallStep>(_ => { });
            var version = await BepInEx.Install(game, dir, steps, default);
            if (!Loader.IsInstalled(game, dir)) throw new Exception("BepInEx not detected after install");

            var registry = new ModRegistry(game, dir);
            var mod = await Thunderstore.Get("lethal-company", "notnotnotswipez-MoreCompany") ?? throw new Exception("no MoreCompany");
            var n = await Installer.InstallFromCatalog(registry, mod, steps, default);
            var record = registry.Get(mod.Id) ?? throw new Exception("not in registry");
            var folder = registry.FolderFor(record);
            if (!Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Any()) throw new Exception("no dll in " + folder);
            registry.SetEnabled(mod.Id, false);
            if (Directory.Exists(folder)) throw new Exception("disable did not move the folder");
            registry.SetEnabled(mod.Id, true);
            registry.Remove(mod.Id);
            if (registry.Has(mod.Id)) throw new Exception("remove failed");
            return $"BepInEx {version}, installed {n} package(s)";
        });

        await Check("valheim bepinex pack", async () =>
        {
            var game = GameCatalog.ById("valheim")!;
            var dir = Path.Combine(root, "Valheim");
            Directory.CreateDirectory(Path.Combine(dir, "valheim_Data"));
            var v = await BepInEx.Install(game, dir, new Progress<InstallStep>(_ => { }), default);
            if (!Loader.IsInstalled(game, dir)) throw new Exception("not detected");
            return v;
        });

        await Check("archive roots", () =>
        {
            // SharpCompress в урезанной сборке должен открывать архивы.
            var zip = Path.Combine(root, "t.zip");
            using (var zipFile = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(zipFile.CreateEntry("Some Mod/manifest.json").Open()))
                w.Write("{\"Name\":\"Some Mod\",\"UniqueID\":\"a.b\",\"Version\":\"1.0\"}");
            var roots = Archive.FindRoots(zip, "manifest", 5);
            return Task.FromResult($"{roots.Count} root: {roots[0].Prefix}");
        });

        await Check("locator", async () =>
        {
            var found = new List<string>();
            foreach (var g in GameCatalog.All.Take(3)) if (await Locator.Locate(g) is { } l) found.Add($"{g.Id}@{l.Source}");
            return found.Count == 0 ? "no games on this machine (expected on CI)" : string.Join(", ", found);
        });

        Console.WriteLine(failed == 0 ? "ALL OK" : $"{failed} FAILED");
        return failed == 0 ? 0 : 1;
    }
}
