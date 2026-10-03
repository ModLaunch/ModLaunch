using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;

namespace ModLaunch.Features;

/// <summary>Профиль другой программы: какая, для какой игры, название, папка и моды.</summary>
public sealed record ManagerProfile(string Manager, string Name, string Folder, List<CodeMod> Mods);

/// <summary>
/// Перенос из r2modman, Thunderstore Mod Manager и Gale. Их профили — папки с BepInEx внутри,
/// моды уже скачаны: копируем их к себе без интернета, с версиями, выключенными модами и настройками.
/// </summary>
public static class ManagerImport
{
    /// <summary>Где эти программы хранят данные (папка → внутри по папке на игру).</summary>
    static IEnumerable<(string Manager, string Root)> Roots()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetEnvironmentVariable("MODLAUNCH_MANAGERS_ROOT"); // для проверок
        if (local is not null)
        {
            yield return ("r2modman", Path.Combine(local, "r2modmanPlus-local"));
            yield break;
        }
        yield return ("r2modman", Path.Combine(appData, "r2modmanPlus-local"));
        yield return ("Thunderstore Mod Manager", Path.Combine(appData, "Thunderstore Mod Manager", "DataFolder"));
        yield return ("Gale", Path.Combine(appData, "com.kesomannen.gale"));
    }

    /// <summary>Папка игры у менеджера называется по-разному (LethalCompany, lethal-company) — сравниваем без знаков.</summary>
    static bool SameGame(GameDef game, string folder)
    {
        var key = Deps.Norm(folder);
        return key != "" && (key == Deps.Norm(game.Name) || key == Deps.Norm(game.ThunderstoreCommunity) || key == Deps.Norm(game.ShortName));
    }

    /// <summary>Профили для этой игры во всех найденных программах.</summary>
    public static List<ManagerProfile> Discover(GameDef game)
    {
        var result = new List<ManagerProfile>();
        if (game.Loader != LoaderKind.Bepinex) return result;
        foreach (var (manager, root) in Roots())
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var gameDir in Directory.EnumerateDirectories(root).Where(d => SameGame(game, Path.GetFileName(d))))
                {
                    var profiles = Path.Combine(gameDir, "profiles");
                    if (!Directory.Exists(profiles)) continue;
                    foreach (var dir in Directory.EnumerateDirectories(profiles))
                    {
                        var mods = ReadMods(dir);
                        if (mods.Count > 0) result.Add(new ManagerProfile(manager, Path.GetFileName(dir), dir, mods));
                    }
                }
            }
            catch { }
        }
        return result;
    }

    /// <summary>Моды профиля: из mods.yml (r2modman), иначе — по папкам BepInEx/plugins/Автор-Название.</summary>
    static List<CodeMod> ReadMods(string profile)
    {
        var yml = Path.Combine(profile, "mods.yml");
        if (File.Exists(yml))
        {
            var (_, list) = ProfileCode.ReadYaml(File.ReadAllText(yml));
            return list.Where(m => !IsLoader(m.Id)).ToList();
        }
        var plugins = Path.Combine(profile, "BepInEx", "plugins");
        if (!Directory.Exists(plugins)) return [];
        return Directory.EnumerateDirectories(plugins)
            .Select(d => (Id: Path.GetFileName(d), Manifest: Path.Combine(d, "manifest.json")))
            .Where(x => x.Id.Contains('-') && !IsLoader(x.Id))
            .Select(x => new CodeMod(x.Id, File.Exists(x.Manifest) ? JsonNode.Parse(File.ReadAllText(x.Manifest)).Str("version_number") ?? "" : "", true))
            .ToList();
    }

    static bool IsLoader(string id) => id.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Перенести профиль: скопировать моды (plugins и patchers), включить/выключить как было,
    /// положить настройки (старые — в ModHub/config-backup) и сохранить как профиль ModLaunch.
    /// Возвращает, сколько модов перенесено и каких папок не нашлось.
    /// </summary>
    public static (int Copied, List<string> Missing) Import(GameState g, ManagerProfile profile, IProgress<InstallStep>? progress = null)
    {
        var registry = g.Registry ?? throw new InvalidOperationException(I18n.T("err.gameNotFound"));
        Snapshots.Take(g, "manager");
        var copied = 0;
        var missing = new List<string>();
        for (var i = 0; i < profile.Mods.Count; i++)
        {
            var mod = profile.Mods[i];
            var name = mod.Id[(mod.Id.IndexOf('-') + 1)..].Replace('_', ' ');
            progress?.Report(new InstallStep("install.extract", name, i + 1, profile.Mods.Count));
            var from = Path.Combine(profile.Folder, "BepInEx", "plugins", mod.Id);
            if (!Directory.Exists(from)) { missing.Add(mod.Id); continue; }
            // Если мод уже стоит у нас, но выключен, — сначала включаем, чтобы заменить на месте.
            if (registry.Get(mod.Id) is { } have && !have.Bool("enabled", true)) registry.SetEnabled(mod.Id, true);
            // Уже стоит у нас в своей папке (LethalLib, а не Evaisa-LethalLib) — заменяем там же, иначе будет две копии.
            var folder = registry.Get(mod.Id)?.Str("folder") is { Length: > 0 } own ? own : mod.Id;
            var to = Path.Combine(registry.ModsDir, folder);
            Copy(from, to, stripOld: !mod.Enabled);
            var manifest = Path.Combine(to, "manifest.json");
            var meta = File.Exists(manifest) ? JsonNode.Parse(File.ReadAllText(manifest)) : null;
            registry.Add(new JsonObject
            {
                ["id"] = mod.Id,
                ["name"] = meta.Str("name")?.Replace('_', ' ') ?? name,
                ["version"] = mod.Version != "" ? mod.Version : meta.Str("version_number") ?? "",
                ["author"] = mod.Id[..mod.Id.IndexOf('-')],
                ["source"] = "thunderstore",
                ["url"] = g.Def.ThunderstoreCommunity is { } c ? $"https://thunderstore.io/c/{c}/p/{mod.Id.Replace('-', '/')}/" : null,
                ["folder"] = folder,
                ["fileCount"] = Directory.EnumerateFiles(to, "*", SearchOption.AllDirectories).Count(),
                ["dependencies"] = new JsonArray(meta.Arr("dependencies").Select(d => d?.ToString()).OfType<string>()
                    .Select(d => (JsonNode)string.Join('-', d.Split('-').Take(2))).ToArray()),
                ["requires"] = new JsonArray(),
                ["enabled"] = true,
                ["missing"] = false,
            });
            var patchers = Path.Combine(profile.Folder, "BepInEx", "patchers", mod.Id);
            if (Directory.Exists(patchers))
            {
                var target = Path.Combine(registry.GamePath, "BepInEx", "patchers", folder);
                Copy(patchers, target, stripOld: !mod.Enabled);
                registry.Add(new JsonObject
                {
                    ["id"] = mod.Id + "#patchers", ["name"] = name + " (patchers)", ["version"] = mod.Version, ["source"] = "thunderstore",
                    ["folder"] = folder, ["target"] = "patchers", ["requestedBy"] = mod.Id, ["enabled"] = true, ["missing"] = false,
                });
            }
            if (!mod.Enabled) registry.SetEnabled(mod.Id, false);
            copied++;
        }
        // Настройки профиля — поверх наших (наши — в резервную копию).
        var config = Path.Combine(profile.Folder, "BepInEx", "config");
        if (Directory.Exists(config))
        {
            var backup = Path.Combine(registry.GamePath, "ModHub", "config-backup", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            var target = Path.Combine(registry.GamePath, "BepInEx", "config");
            foreach (var file in Directory.EnumerateFiles(config, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(config, file);
                var dest = Path.Combine(target, relative);
                try
                {
                    if (File.Exists(dest))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(backup, relative))!);
                        File.Copy(dest, Path.Combine(backup, relative), true);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, true);
                }
                catch { }
            }
        }
        try { Profiles.Save(g.Def.Id, $"{profile.Name} ({profile.Manager})", registry); } catch { }
        return (copied, missing);
    }

    /// <summary>Скопировать папку. stripOld — у выключенных в r2modman модов файлы переименованы в «.old»: возвращаем имена.</summary>
    static void Copy(string from, string to, bool stripOld)
    {
        if (Directory.Exists(to)) Directory.Delete(to, true);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (stripOld && relative.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) relative = relative[..^4];
            var dest = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    [SelfTest]
    static string ImportsR2modmanProfile()
    {
        var root = Path.Combine(Paths.DataDir, "managers");
        Environment.SetEnvironmentVariable("MODLAUNCH_MANAGERS_ROOT", root);
        try
        {
            var profile = Path.Combine(root, "r2modmanPlus-local", "LethalCompany", "profiles", "Friends");
            void Write(string rel, string text) { var p = Path.Combine(profile, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }
            Write("mods.yml", "- manifestVersion: 1\n  name: Evaisa-LethalLib\n  versionNumber:\n    major: 0\n    minor: 16\n    patch: 1\n  enabled: true\n- manifestVersion: 1\n  name: x753-More_Suits\n  versionNumber:\n    major: 1\n    minor: 4\n    patch: 3\n  enabled: false\n");
            Write("BepInEx/plugins/Evaisa-LethalLib/LethalLib.dll", "lib");
            Write("BepInEx/plugins/Evaisa-LethalLib/manifest.json", "{\"name\":\"LethalLib\",\"version_number\":\"0.16.1\",\"dependencies\":[\"BepInEx-BepInExPack-5.4.2100\"]}");
            Write("BepInEx/plugins/x753-More_Suits/MoreSuits.dll.old", "suits");
            Write("BepInEx/config/MoreSuits.cfg", "from r2modman");
            var game = Path.Combine(Paths.DataDir, "managers-game", "Lethal Company");
            Directory.CreateDirectory(Path.Combine(game, "BepInEx", "plugins"));
            var def = GameCatalog.ById("lethal-company")!;
            var found = Discover(def);
            if (found.Count != 1 || found[0].Mods.Count != 2) throw new Exception("r2modman profile not found");
            var state = new GameState { Def = def, Path = game };
            var (copied, missingDirs) = Import(state, found[0]);
            var registry = state.Registry!;
            if (copied != 2 || missingDirs.Count != 0) throw new Exception($"copied {copied}");
            if (registry.Get("Evaisa-LethalLib")?.Str("version") != "0.16.1") throw new Exception("version lost");
            var suits = registry.Get("x753-More_Suits")!;
            if (suits.Bool("enabled", true) || !File.Exists(Path.Combine(registry.FolderFor(suits), "MoreSuits.dll"))) throw new Exception("disabled mod not restored as .dll");
            if (File.ReadAllText(Path.Combine(game, "BepInEx", "config", "MoreSuits.cfg")) != "from r2modman") throw new Exception("config not copied");
            return "2 mods copied offline (1 disabled, .old stripped), config applied";
        }
        finally { Environment.SetEnvironmentVariable("MODLAUNCH_MANAGERS_ROOT", null); }
    }

    /// <summary>Мод уже стоит у нас (папка по имени из manifest, выключен) — перенос заменяет его, а не кладёт вторую копию рядом.</summary>
    [SelfTest]
    static string ReplacesInstalledModInPlace()
    {
        var root = Path.Combine(Paths.DataDir, "managers-2");
        Environment.SetEnvironmentVariable("MODLAUNCH_MANAGERS_ROOT", root);
        try
        {
            var profile = Path.Combine(root, "r2modmanPlus-local", "REPO", "profiles", "Default");
            Directory.CreateDirectory(Path.Combine(profile, "BepInEx", "plugins", "Zehs-REPOLib"));
            File.WriteAllText(Path.Combine(profile, "BepInEx", "plugins", "Zehs-REPOLib", "REPOLib.dll"), "new");
            File.WriteAllText(Path.Combine(profile, "mods.yml"), "- name: Zehs-REPOLib\n  versionNumber:\n    major: 2\n    minor: 1\n    patch: 0\n  enabled: true\n");
            var def = GameCatalog.ById("repo")!;
            var state = new GameState { Def = def, Path = Path.Combine(Paths.DataDir, "managers-2-game", "REPO") };
            var registry = state.Registry!;
            var old = Directory.CreateDirectory(Path.Combine(registry.StorageDir, "disabled", "REPOLib")).FullName;
            File.WriteAllText(Path.Combine(old, "REPOLib.dll"), "old");
            registry.Add(new JsonObject { ["id"] = "Zehs-REPOLib", ["name"] = "REPOLib", ["folder"] = "REPOLib", ["version"] = "1.0.0", ["source"] = "thunderstore", ["enabled"] = false });
            Import(state, Discover(def).Single());
            var copies = Directory.GetDirectories(registry.ModsDir).Select(Path.GetFileName).ToList();
            if (copies.Count != 1) throw new Exception("two copies of the mod in plugins: " + string.Join(", ", copies));
            if (File.ReadAllText(Path.Combine(registry.FolderFor(registry.Get("Zehs-REPOLib")!), "REPOLib.dll")) != "new") throw new Exception("old version left");
            return "installed mod replaced in its own folder, no second copy";
        }
        finally { Environment.SetEnvironmentVariable("MODLAUNCH_MANAGERS_ROOT", null); }
    }
}
