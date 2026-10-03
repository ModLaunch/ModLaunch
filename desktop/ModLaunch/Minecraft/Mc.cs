using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Minecraft;

/// <summary>
/// Сборка Minecraft (как «установка» в официальном лаунчере или экземпляр в Prism/Modrinth App):
/// своя папка игры, версия Minecraft, загрузчик модов и память.
/// </summary>
public sealed class McInstance
{
    public required string Id { get; init; }
    public string Name { get; set; } = "";
    public string GameVersion { get; set; } = "";
    /// <summary>vanilla | fabric | quilt | forge | neoforge</summary>
    public string Loader { get; set; } = "vanilla";
    /// <summary>Версия загрузчика; пусто — самая свежая стабильная при установке.</summary>
    public string LoaderVersion { get; set; } = "";
    /// <summary>Папка игры этой сборки (моды, миры, настройки).</summary>
    public string Dir { get; set; } = "";
    /// <summary>Чужая папка (Modrinth App, Prism, CurseForge): работаем на месте, при удалении — не стираем.</summary>
    public bool Linked { get; set; }
    public string? Source { get; set; }
    public int MemoryMb { get; set; } = 4096;
    public string JavaArgs { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayed { get; set; }
    /// <summary>Сборка из модпака Modrinth: проект и версия — чтобы предложить обновление пака.</summary>
    public string? PackProject { get; set; }
    public string? PackVersion { get; set; }
    public string? Icon { get; set; }

    public bool Modded => Loader != "vanilla";
    public string LoaderTitle => Mc.LoaderTitle(Loader);
    /// <summary>«Fabric 1.21.1», «Minecraft 26.2».</summary>
    public string Label => Modded ? $"{LoaderTitle} {GameVersion}" : $"Minecraft {GameVersion}";
    /// <summary>Номер версии в папке versions официального лаунчера.</summary>
    public string VersionId => Mc.VersionIdFor(this);

    public string Folder(string kind) => Path.Combine(Dir, Mc.KindFolder(kind));

    public JsonObject ToJson() => new()
    {
        ["name"] = Name, ["gameVersion"] = GameVersion, ["loader"] = Loader, ["loaderVersion"] = LoaderVersion,
        ["dir"] = Dir, ["linked"] = Linked, ["source"] = Source, ["memoryMb"] = MemoryMb, ["javaArgs"] = JavaArgs,
        ["created"] = Created.ToString("o"), ["lastPlayed"] = LastPlayed?.ToString("o"),
        ["packProject"] = PackProject, ["packVersion"] = PackVersion, ["icon"] = Icon,
    };

    public static McInstance FromJson(string id, JsonNode n) => new()
    {
        Id = id,
        Name = n.Str("name") ?? id,
        GameVersion = n.Str("gameVersion") ?? "",
        Loader = n.Str("loader") ?? "vanilla",
        LoaderVersion = n.Str("loaderVersion") ?? "",
        Dir = n.Str("dir") ?? "",
        Linked = n.Bool("linked"),
        Source = n.Str("source"),
        MemoryMb = (int)Math.Clamp(n.Long("memoryMb") is var m && m > 0 ? m : 4096, 1024, 65536),
        JavaArgs = n.Str("javaArgs") ?? "",
        Created = DateTime.TryParse(n.Str("created"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var c) ? c : DateTime.UtcNow,
        LastPlayed = DateTime.TryParse(n.Str("lastPlayed"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var p) ? p : null,
        PackProject = n.Str("packProject"),
        PackVersion = n.Str("packVersion"),
        Icon = n.Str("icon"),
    };
}

/// <summary>
/// Minecraft: Java Edition (8.5). Папка .minecraft, сборки со своими модами, версии и загрузчики
/// (Fabric, Quilt, Forge, NeoForge). Играем через официальный лаунчер: ModLaunch добавляет туда
/// установку сборки и открывает его — вход в аккаунт Microsoft остаётся за лаунчером.
/// </summary>
public static partial class Mc
{
    public const string Id = "minecraft";
    public static readonly string[] Loaders = ["vanilla", "fabric", "quilt", "forge", "neoforge"];
    public static readonly string[] Kinds = ["mod", "resourcepack", "shader", "datapack"];

    public static string LoaderTitle(string loader) => loader switch
    {
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        _ => "Minecraft",
    };

    public static string KindFolder(string kind) => kind switch
    {
        "resourcepack" => "resourcepacks",
        "shader" => "shaderpacks",
        "datapack" => "datapacks",
        _ => "mods",
    };

    public static GameState? State => AppState.Games.FirstOrDefault(g => g.Def.Id == Id);
    public static GameDef? Def => State?.Def;

    static JsonObject Store => Settings.Data.Obj("minecraft");

    // ---------------------------------------------------------------- где Minecraft

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Обычная папка игры: %APPDATA%\.minecraft.</summary>
    public static string DefaultRoot => Path.Combine(AppData, ".minecraft");

    /// <summary>Папка .minecraft, с которой работает ModLaunch (найденная или указанная).</summary>
    public static string Root => State?.Path ?? Settings.GamePath(Id) ?? DefaultRoot;

    /// <summary>Пакет лаунчера из Microsoft Store / приложения Xbox.</summary>
    public const string StorePackage = "Microsoft.4297127D64EC6_8wekyb3d8bbwe";

    /// <summary>Официальный лаунчер: где он и как его открыть.</summary>
    public sealed record LauncherInfo(string Kind, string? Exe);

    public static LauncherInfo? FindLauncher()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        {
            if (dir == "") continue;
            var exe = Path.Combine(dir, "Minecraft Launcher", "MinecraftLauncher.exe");
            if (File.Exists(exe)) return new("classic", exe);
        }
        if (Directory.Exists(Path.Combine(LocalAppData, "Packages", StorePackage))) return new("store", null);
        // Приложение Xbox ставит лаунчер в X:\XboxGames\Minecraft Launcher — открываем тоже через пакет.
        foreach (var drive in SafeDrives())
            if (Directory.Exists(Path.Combine(drive, "XboxGames", "Minecraft Launcher"))) return new("store", null);
        return null;
    }

    static IEnumerable<string> SafeDrives()
    {
        try { return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList(); }
        catch { return []; }
    }

    /// <summary>Найти Minecraft: папка .minecraft есть или стоит официальный лаунчер.</summary>
    public static string? Locate()
    {
        if (Directory.Exists(DefaultRoot)) return DefaultRoot;
        return FindLauncher() is not null ? Directory.CreateDirectory(DefaultRoot).FullName : null;
    }

    /// <summary>Похожа ли папка на .minecraft (для «указать папку вручную»).</summary>
    public static bool LooksLikeRoot(string dir) =>
        Directory.Exists(Path.Combine(dir, "versions")) || File.Exists(Path.Combine(dir, "launcher_profiles.json"))
        || File.Exists(Path.Combine(dir, "launcher_profiles_microsoft_store.json")) || Path.GetFileName(dir.TrimEnd('\\', '/')).Equals(".minecraft", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- сборки

    /// <summary>Собственные сборки ModLaunch — в .minecraft\ModLaunch\instances (рядом с игрой, не в данных программы:
    /// удаление ModLaunch с «удалить данные» не должно стирать миры).</summary>
    public static string InstancesDir => Path.Combine(Root, "ModLaunch", "instances");

    static JsonObject InstancesStore => Store.Obj("instances");

    public static List<McInstance> Instances()
    {
        Recover();
        return InstancesStore.Select(kv => kv.Value is JsonObject o ? McInstance.FromJson(kv.Key, o) : null)
            .OfType<McInstance>()
            .OrderByDescending(i => i.LastPlayed ?? i.Created)
            .ToList();
    }

    public static McInstance? Get(string? id) => id is not null && InstancesStore[id] is JsonObject o ? McInstance.FromJson(id, o) : null;

    /// <summary>Выбранная сборка: её моды показываются на странице, её запускает «Играть».</summary>
    public static McInstance? Active
    {
        get => Get(Store.Str("active")) ?? Instances().FirstOrDefault();
        set { OnUi(() => { Store["active"] = value?.Id; Settings.Save(); }); Changed?.Invoke(); }
    }

    public static event Action? Changed;
    public static void Notify() { Changed?.Invoke(); AppState.Notify(); }

    /// <summary>
    /// Настройки читает интерфейс, а сборки создаются и из фоновых задач (модпак, загрузчик) —
    /// общий словарь настроек меняем только в потоке интерфейса.
    /// </summary>
    static void OnUi(Action action)
    {
        if (Avalonia.Application.Current is null || Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Invoke(action);
    }

    public static void Save(McInstance i)
    {
        var json = i.ToJson();
        OnUi(() => { InstancesStore[i.Id] = json; Settings.Save(); });
        if (!i.Linked && Directory.Exists(i.Dir))
        {
            // Копия описания в папке сборки: если настройки программы пропадут, сборка найдётся снова.
            try { File.WriteAllText(Path.Combine(i.Dir, "modlaunch-instance.json"), i.ToJson().ToJsonString()); } catch { }
        }
    }

    static bool _recovered;

    /// <summary>Сборки, которые лежат в папке, но пропали из настроек (переустановка, перенос), — вернуть в список.</summary>
    static void Recover()
    {
        if (_recovered) return;
        _recovered = true;
        try
        {
            if (!Directory.Exists(InstancesDir)) return;
            var changed = false;
            foreach (var dir in Directory.EnumerateDirectories(InstancesDir))
            {
                var id = Path.GetFileName(dir);
                var file = Path.Combine(dir, "modlaunch-instance.json");
                if (InstancesStore.ContainsKey(id) || !File.Exists(file)) continue;
                if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject o) continue;
                o["dir"] = dir;
                InstancesStore[id] = o;
                changed = true;
            }
            if (changed) Settings.Save();
        }
        catch { }
    }

    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex NotSlug();

    /// <summary>Свободное имя папки по названию сборки.</summary>
    public static string NewId(string name)
    {
        var translit = Translit(name.ToLowerInvariant());
        var slug = NotSlug().Replace(translit, "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        if (slug == "") slug = "build";
        var id = slug;
        for (var n = 2; InstancesStore.ContainsKey(id) || Directory.Exists(Path.Combine(InstancesDir, id)); n++) id = $"{slug}-{n}";
        return id;
    }

    static string Translit(string s)
    {
        const string ru = "абвгдеёжзийклмнопрстуфхцчшщъыьэюяієїґ";
        string[] lat = ["a", "b", "v", "g", "d", "e", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "h", "c", "ch", "sh", "sch", "", "y", "", "e", "yu", "ya", "i", "e", "yi", "g"];
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            var at = ru.IndexOf(ch);
            sb.Append(at >= 0 ? lat[at] : ch.ToString());
        }
        return sb.ToString();
    }

    /// <summary>Новая сборка: папка с mods, resourcepacks, shaderpacks — и она становится выбранной.</summary>
    public static McInstance Create(string name, string gameVersion, string loader, string loaderVersion = "", int memoryMb = 0)
    {
        var id = NewId(name);
        var dir = Directory.CreateDirectory(Path.Combine(InstancesDir, id)).FullName;
        foreach (var kind in new[] { "mods", "resourcepacks", "shaderpacks", "saves", "config" }) Directory.CreateDirectory(Path.Combine(dir, kind));
        var i = new McInstance
        {
            Id = id, Name = name.Trim() == "" ? $"{LoaderTitle(loader)} {gameVersion}" : name.Trim(), GameVersion = gameVersion, Loader = loader,
            LoaderVersion = loaderVersion, Dir = dir, MemoryMb = memoryMb > 0 ? memoryMb : DefaultMemory(),
        };
        Save(i);
        Active = i;
        return i;
    }

    /// <summary>Подключить чужую папку (Modrinth App, Prism, CurseForge) как сборку — без копирования.</summary>
    public static McInstance Link(string name, string dir, string gameVersion, string loader, string loaderVersion, string source)
    {
        var existing = Instances().FirstOrDefault(i => Path.GetFullPath(i.Dir).TrimEnd('\\', '/').Equals(Path.GetFullPath(dir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { Active = existing; return existing; }
        var i = new McInstance
        {
            Id = NewId(name), Name = name, GameVersion = gameVersion, Loader = loader, LoaderVersion = loaderVersion,
            Dir = dir, Linked = true, Source = source, MemoryMb = DefaultMemory(),
        };
        Save(i);
        Active = i;
        return i;
    }

    public static void Rename(McInstance i, string name)
    {
        if (name.Trim() == "") return;
        i.Name = name.Trim();
        Save(i);
        McProfiles.Sync(i);
        Notify();
    }

    /// <summary>Убрать сборку. Свою папку — в корзину-«мусор» рядом (миры не пропадают молча), чужую — не трогаем.</summary>
    public static void Delete(McInstance i, bool files)
    {
        InstancesStore.Remove(i.Id);
        if (Store.Str("active") == i.Id) Store.Remove("active");
        Settings.Save();
        McProfiles.Remove(i);
        if (files && !i.Linked && Directory.Exists(i.Dir))
        {
            var trash = Path.Combine(Root, "ModLaunch", "deleted", $"{i.Id}-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
            try { Directory.Move(i.Dir, trash); }
            catch { try { Directory.Delete(i.Dir, true); } catch { } }
        }
        try { File.Delete(McContent.MetaPath(i)); } catch { }
        Notify();
    }

    /// <summary>Копия сборки: моды, настройки и ресурспаки (миры — по желанию).</summary>
    public static McInstance Duplicate(McInstance source, bool worlds)
    {
        var copy = Create(source.Name + " — " + I18n.T("mine.copy"), source.GameVersion, source.Loader, source.LoaderVersion, source.MemoryMb);
        foreach (var dir in new[] { "mods", "config", "resourcepacks", "shaderpacks", "datapacks", "defaultconfigs", "kubejs" }.Concat(worlds ? ["saves"] : Array.Empty<string>()))
            CopyDir(Path.Combine(source.Dir, dir), Path.Combine(copy.Dir, dir));
        foreach (var file in new[] { "options.txt", "servers.dat" })
            try { if (File.Exists(Path.Combine(source.Dir, file))) File.Copy(Path.Combine(source.Dir, file), Path.Combine(copy.Dir, file), true); } catch { }
        copy.JavaArgs = source.JavaArgs;
        Save(copy);
        McContent.CopyMeta(source, copy);
        return copy;
    }

    public static void CopyDir(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
    }

    /// <summary>Память по умолчанию: четверть ОЗУ, но от 2 до 8 ГБ.</summary>
    public static int DefaultMemory()
    {
        try
        {
            var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
            return (int)Math.Clamp(total / 4 / 512 * 512, 2048, 8192);
        }
        catch { return 4096; }
    }

    public static int TotalMemoryMb()
    {
        try { return (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024); } catch { return 16384; }
    }

    /// <summary>Номер версии в папке versions: так её видит официальный лаунчер.</summary>
    public static string VersionIdFor(McInstance i) => i.Loader switch
    {
        "fabric" => $"fabric-loader-{i.LoaderVersion}-{i.GameVersion}",
        "quilt" => $"quilt-loader-{i.LoaderVersion}-{i.GameVersion}",
        "forge" => $"{i.GameVersion}-forge-{i.LoaderVersion}",
        "neoforge" => $"neoforge-{i.LoaderVersion}",
        _ => i.GameVersion,
    };

    /// <summary>Готова ли версия сборки в .minecraft\versions (загрузчик поставлен).</summary>
    public static bool VersionReady(McInstance i) =>
        !i.Modded || (i.LoaderVersion != "" && File.Exists(Path.Combine(Root, "versions", i.VersionId, i.VersionId + ".json")));

    /// <summary>Папка модов выбранной сборки (для общего кода программы).</summary>
    public static string ModsDirFor(string root) => Active?.Folder("mod") ?? Path.Combine(root, "mods");

    /// <summary>Строка под обложкой: «Fabric 1.21.1 · 12 модов».</summary>
    public static string CardLine()
    {
        var a = Active;
        if (a is null) return I18n.T("mine.card.noInstance");
        var n = McContent.Count(a, "mod");
        var updates = McContent.UpdateCount(a);
        var line = a.Modded ? $"{a.Label} · {I18n.T("mine.mods." + I18n.Plural(n, "one", "few", "many"), ("n", n))}" : a.Label;
        return updates > 0 ? $"{line} · ↑{updates}" : line;
    }

    /// <summary>Мир и логи выбранной сборки — для общих вкладок программы.</summary>
    public static string? SavesDir(string root) => Active is { } a ? Path.Combine(a.Dir, "saves") : Path.Combine(root, "saves");
}
