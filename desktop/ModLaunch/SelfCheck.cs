using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Features;
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

        await Check("nexus collections", async () =>
        {
            var (items, total, _) = await NexusCollections.Browse("subnautica", 1, "");
            if (items.Count == 0) throw new Exception("empty");
            var (info, mods) = await NexusCollections.Get("subnautica", items[0].Url);
            if (mods.Count == 0 || mods[0].FileId == 0) throw new Exception("no files in " + info.Slug);
            return $"{total} collections; «{info.Name}»: {mods.Count} mods, {mods.Count(m => m.Optional)} optional";
        });
        await Check("nexus deps plan", async () =>
        {
            var game = GameCatalog.ById("subnautica")!;
            var dir = Path.Combine(root, "Subnautica");
            Directory.CreateDirectory(Path.Combine(dir, "Subnautica_Data"));
            var plan = await Features.Deps.NexusPlan(game, new ModRegistry(game, dir), "1119", default);
            if (plan.Count == 0) throw new Exception("Base Kits should need Nautilus");
            return string.Join(", ", plan.Select(p => $"{p.Name} #{p.Id}"));
        });
        await Check("smapi index", async () =>
        {
            var found = await Features.SmapiIndex.Lookup(["Pathoschild.ContentPatcher"], default);
            return found.TryGetValue("Pathoschild.ContentPatcher", out var hit) && hit.NexusId is not null ? $"Content Patcher → #{hit.NexusId}" : throw new Exception("not found");
        });
        await Check("mod updates", async () =>
        {
            var game = GameCatalog.ById("lethal-company")!;
            var dir = Path.Combine(root, "LC-updates");
            Directory.CreateDirectory(Path.Combine(dir, "Lethal Company_Data"));
            var registry = new ModRegistry(game, dir);
            Directory.CreateDirectory(Path.Combine(registry.ModsDir, "MoreCompany"));
            registry.Add(new JsonObject { ["id"] = "notnotnotswipez-MoreCompany", ["name"] = "MoreCompany", ["version"] = "0.0.1", ["source"] = "thunderstore", ["folder"] = "MoreCompany" });
            var state = new GameState { Def = game, Path = dir, Status = Detect.Found };
            var ups = await Features.ModUpdates.Check(state);
            return ups.Count == 1 ? $"MoreCompany 0.0.1 → {ups[0].Latest}" : throw new Exception($"{ups.Count} updates");
        });
        await Check("dxvk", async () =>
        {
            if (!OperatingSystem.IsWindows()) return "skipped (not Windows)";
            var dir = Path.Combine(root, "OldGame");
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "game.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "notepad.exe"), exe);
            File.WriteAllText(Path.Combine(dir, "d3d9.dll"), "original");
            var info = Features.Dxvk.Inspect(exe);
            var (version, api) = await Features.Dxvk.Install(exe, "dx9", new Progress<InstallStep>(_ => { }), default);
            if (new FileInfo(Path.Combine(dir, "d3d9.dll")).Length < 100_000) throw new Exception("d3d9.dll not replaced");
            if (!Features.Dxvk.Status(dir).Installed) throw new Exception("no marker");
            Features.Dxvk.Remove(dir);
            if (File.ReadAllText(Path.Combine(dir, "d3d9.dll")) != "original") throw new Exception("original not restored");
            return $"DXVK {version} ({api}, {info.Arch}) installed and removed";
        });
        await Check("reshade", async () =>
        {
            var dir = Path.Combine(root, "ShaderGame");
            Directory.CreateDirectory(dir);
            var state = await Features.ReShade.Install(dir, "dx11", new Progress<InstallStep>(_ => { }), default);
            if (state.Dll is null) throw new Exception("dll not detected");
            var fx = Directory.EnumerateFiles(Path.Combine(dir, "reshade-shaders"), "*.fx", SearchOption.AllDirectories).Count();
            Features.ReShade.Activate(dir, Path.Combine(dir, "My.ini"));
            var preset = Features.ReShade.Detect(dir).Preset;
            return $"{state.Dll}, {fx} effects, preset {preset}";
        });
        await Check("backups + profiles + pack", () =>
        {
            var game = GameCatalog.ById("subnautica")!;
            var dir = Path.Combine(root, "Subnautica");
            var saves = Path.Combine(dir, "SNAppData", "SavedGames", "slot0000");
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(saves, "save.json"), "v1");
            var b = Features.Backups.Create("subnautica", Path.GetDirectoryName(saves), "manual") ?? throw new Exception("no backup");
            File.WriteAllText(Path.Combine(saves, "save.json"), "v2");
            Features.Backups.Restore("subnautica", b.Name, Path.GetDirectoryName(saves)!);
            if (File.ReadAllText(Path.Combine(saves, "save.json")) != "v1") throw new Exception("restore failed");

            var registry = new ModRegistry(game, dir);
            foreach (var n in new[] { "A", "B" })
            {
                Directory.CreateDirectory(Path.Combine(registry.ModsDir, n));
                registry.Add(new JsonObject { ["id"] = "nexus:subnautica:" + n, ["name"] = n, ["source"] = "nexus", ["folder"] = n });
            }
            Features.Profiles.Save("subnautica", "both", registry);
            registry.SetEnabled("nexus:subnautica:B", false);
            var (on, _, _) = Features.Profiles.Apply("subnautica", "both", registry);
            if (on != 1 || !Directory.Exists(Path.Combine(registry.ModsDir, "B"))) throw new Exception("profile apply failed");
            var pack = Features.ModPack.Parse(Features.ModPack.Build(game, registry, "test"));
            if (pack.Mods.Count != 2) throw new Exception("pack roundtrip");
            return Task.FromResult($"backup {b.Name} restored; profile on={on}; pack {pack.Mods.Count} mods");
        });
        await Check("log parse", () =>
        {
            var issues = Features.Logs.Parse(GameCatalog.ById("valheim")!, "[Error  : Some Mod] boom\n[Error  :   BepInEx] ignore\n");
            return issues.Count == 1 ? Task.FromResult(issues[0].Mod) : throw new Exception($"{issues.Count}");
        });

        await Check("reviews sync (read-only)", async () =>
        {
            if (!Social.Firebase.Configured) throw new Exception("Firebase config not loaded");
            await Social.Reviews.Sync(force: true);
            var all = Social.Reviews.List();
            return $"{all.Count} reviews, {Social.Reviews.Stats().Count} rated mods";
        });
        await Check("app update check", async () =>
        {
            var latest = await Setup.Updater.Check() ?? throw new Exception("not configured");
            return $"latest {latest.Version}, setup asset: {latest.SetupName ?? "none"}, newer than us: {Setup.Updater.Available}";
        });
        await Check("installer", async () =>
        {
            if (!OperatingSystem.IsWindows()) return "skipped (not Windows)";
            var target = Path.Combine(root, "Programs", "ModLaunch");
            var result = await Setup.Installer.Install(target, desktop: false, new Progress<(string, double)>(_ => { }));
            if (!File.Exists(result.Exe) || !File.Exists(Path.Combine(target, Setup.Installer.Marker))) throw new Exception("files missing");
            var inspected = Setup.Installer.Inspect(target);
            if (inspected.Kind != Setup.Installer.TargetKind.Ours) throw new Exception("not recognised as ours");
            var again = await Setup.Installer.Install(target, desktop: false, new Progress<(string, double)>(_ => { }));
            if (!again.Updated) throw new Exception("second install should be an update");
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ModHub");
            var uninstall = key?.GetValue("UninstallString") as string ?? throw new Exception("no uninstall entry");
            var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ModLaunch.lnk");
            if (!File.Exists(link)) throw new Exception("no start menu shortcut");
            return $"installed {new FileInfo(result.Exe).Length / 1048576} MB, updated in place, uninstall: {uninstall}";
        });
        await Check("pe + hotkey parse", () =>
        {
            if (!OperatingSystem.IsWindows()) return Task.FromResult("skipped (not Windows)");
            var ok = Features.Hotkey.Arm("Shift+F1", () => { });
            Features.Hotkey.Disarm();
            return Task.FromResult(ok ? "Shift+F1 registered and released" : "could not register (another app holds it)");
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
