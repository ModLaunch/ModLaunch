using System.Text;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Features;

/// <summary>
/// 23.0: «Играть как разработчик». ModLaunch собирает конструктор ModScript Studio (Assets/studio) под эту игру —
/// из её собственных UnityEngine и BepInEx, поэтому подходит любой версии Unity, — кладёт его плагином BepInEx,
/// включает режим разработчика (dev.flag) и запускает игру. В игре появляется кнопка «Создать мод» (F8):
/// свободная камера, выбор объектов, стрелки перемещения/поворота/масштаба, объекты игры и фигуры, материалы
/// и свои текстуры, удаление объектов уровня, отмена. Сделанное сохраняется по уровням (*.layout) и после игры
/// остаётся обычным модом: без dev.flag плагин только расставляет сохранённое. Сейчас — игры на Unity (Mono) с BepInEx.
/// </summary>
public static class DevMode
{
    public const string Plugin = "ModScriptStudio";

    public static string PluginDir(string game) => Path.Combine(game, "BepInEx", "plugins", Plugin);
    public static string ConfigDir(string game) => Path.Combine(game, "BepInEx", "config", Plugin);
    static string Flag(string game) => Path.Combine(ConfigDir(game), "dev.flag");

    /// <summary>Исходник конструктора (встроен в программу ресурсом — читается и без окна).</summary>
    public static string Source()
    {
        // 23.4: три части одного класса (карта; инструменты; окно редактора) — один исходник, using только в первой.
        var text = new StringBuilder();
        foreach (var part in new[] { "studio-source", "studio-tools", "studio-ui", "studio-kits", "studio-commands", "studio-fx" })
        {
            using var s = typeof(DevMode).Assembly.GetManifestResourceStream(part) ?? throw new InvalidOperationException(part + " is missing from the build");
            using var r = new StreamReader(s, Encoding.UTF8);
            text.Append(r.ReadToEnd()).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// Командная строка: ModLaunch.exe --dev-prepare "папка игры" [--autotest]. Собирает конструктор в игру и включает режим
    /// разработчика; с --autotest плагин в игре сам откроет конструктор, сделает снимки и отчёт (autotest.txt) и закроет игру.
    /// </summary>
    public static int Cli(string[] args)
    {
        var at = Array.IndexOf(args, "--dev-prepare");
        if (at < 0 || at + 1 >= args.Length) { Console.WriteLine("usage: --dev-prepare <game folder> [--autotest]"); return 2; }
        var game = args[at + 1];
        // Как перед обычным запуском: загрузчик, убранный между сессиями (инжектор, «без модов» на раз), — на место.
        try { Vanilla.UnparkFolder(game); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.WriteLine("unpark: " + e.Message); }
        // 23.7: меню под игру — как при запуске из программы (имя и номер Steam — по манифесту библиотеки Steam).
        var full = Path.GetFullPath(game).TrimEnd('\\', '/');
        var app = Games.Locator.SteamApps().FirstOrDefault(a => string.Equals(Path.GetFullPath(a.Path).TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase));
        try
        {
            // Папка не из библиотеки Steam (перенесённая игра) — номер по справочнику, по имени папки.
            var name = app?.Name ?? Path.GetFileName(full);
            var appId = app?.AppId ?? Games.GameDirectory.ByName(name)?.SteamAppId ?? 0;
            var p = Task.Run(() => GameProfile.Apply(game, name, appId)).GetAwaiter().GetResult();
            Console.WriteLine("profile " + p.Name + ": " + p.Preset + " " + p.Accent + " [" + string.Join(", ", p.Tags.Take(5)) + "]");
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException) { Console.WriteLine("profile: " + e.Message); }
        // В фоне, как в программе: загрузки ждут ответа, а у главного потока здесь свой диспетчер Avalonia.
        var error = Task.Run(() => Prepare(game, new Progress<string>(t => Console.WriteLine("installing " + t)))).GetAwaiter().GetResult();
        if (error is not null) { Console.WriteLine("FAIL " + error); return 1; }
        if (args.Contains("--autotest") && KindOf(game) == "unity") File.WriteAllText(Path.Combine(ConfigDir(game), "autotest.flag"), DateTime.UtcNow.ToString("o"));
        Console.WriteLine("ok " + KindOf(game) + " " + (KindOf(game) switch { "cyberpunk" => DevCyberpunk.ModDir(game), "unity" => PluginDir(game), _ => game }));
        return 0;
    }

    /// <summary>Почему режим разработчика недоступен (ключ строки) или null — можно.</summary>
    public static string? Unsupported(string? gamePath)
    {
        if (gamePath is null || !Directory.Exists(gamePath)) return "v23.dev.no.game";
        if (ModScriptX.Engines.FindAntiCheat(gamePath) is not null) return "v23.dev.no.anticheat";
        // 23.2: Cyberpunk 2077 — конструктор мира на Cyber Engine Tweaks (DevCyberpunk), ставится с GitHub.
        if (DevCyberpunk.Is(gamePath)) return null;
        // 23.3: Unreal, RE Engine, Unity IL2CPP, Creation Engine — инструменты движка (DevEngines).
        if (DevEngines.KindOf(gamePath) is not null) return null;
        if (File.Exists(Path.Combine(gamePath, "GameAssembly.dll"))) return "v23.dev.no.il2cpp";
        var managed = Directory.EnumerateDirectories(gamePath, "*_Data").Select(d => Path.Combine(d, "Managed")).FirstOrDefault(Directory.Exists);
        if (managed is null || !File.Exists(Path.Combine(managed, "UnityEngine.dll")) && !File.Exists(Path.Combine(managed, "UnityEngine.CoreModule.dll"))) return "v23.dev.no.engine";
        // 23.2: BepInEx нет — поставим сами (Prepare), так что Unity-игра на Mono подходит и без него.
        return null;
    }

    /// <summary>Разрядность игры по UnityPlayer.dll (или exe у старых Unity): x64 или x86.</summary>
    static string Bits(string gamePath)
    {
        var probe = Path.Combine(gamePath, "UnityPlayer.dll");
        if (!File.Exists(probe))
        {
            var data = Directory.EnumerateDirectories(gamePath, "*_Data").Select(Path.GetFileName).FirstOrDefault();
            probe = data is null ? "" : Path.Combine(gamePath, data[..^5] + ".exe");
        }
        try
        {
            using var s = File.OpenRead(probe);
            using var pe = new System.Reflection.PortableExecutable.PEReader(s);
            return pe.PEHeaders.CoffHeader.Machine == System.Reflection.PortableExecutable.Machine.I386 ? "x86" : "x64";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException) { return "x64"; }
    }

    /// <summary>Главный номер версии Unity (2017…2022, 6000) по UnityPlayer.dll или exe старых Unity; 0 — не узнали.</summary>
    public static int UnityMajor(string gamePath)
    {
        var probe = Path.Combine(gamePath, "UnityPlayer.dll");
        if (!File.Exists(probe))
        {
            var data = Directory.EnumerateDirectories(gamePath, "*_Data").Select(Path.GetFileName).FirstOrDefault();
            probe = data is null ? "" : Path.Combine(gamePath, data[..^5] + ".exe");
        }
        try { return File.Exists(probe) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(probe).FileMajorPart : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }

    /// <summary>Поставить BepInEx 5 с официального GitHub (нужной разрядности) — для Unity-игр, где его ещё нет.</summary>
    static string? InstallBepInEx(string gamePath, IProgress<string>? progress)
    {
        var bits = Bits(gamePath);
        var tool = new DevCyberpunk.Tool("bepinex", "BepInEx 5", "BepInEx/BepInEx",
            n => n.StartsWith($"BepInEx_win_{bits}_5.", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal), Path.Combine("BepInEx", "core", "BepInEx.dll"));
        try
        {
            progress?.Report(tool.Title);
            var (url, name, size, _) = DevCyberpunk.Latest(tool, default).GetAwaiter().GetResult();
            var file = Path.Combine(Directory.CreateDirectory(Path.Combine(Paths.CacheDir, "dev-unity")).FullName, name);
            DevCyberpunk.Download(url, size, file, default).GetAwaiter().GetResult();
            DevCyberpunk.Extract(file, gamePath);
            return DevCyberpunk.Installed(gamePath, tool) ? null : "BepInEx: " + I18n.T("v23.dev.cp.badArchive");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException)
        {
            return "BepInEx: " + e.Message;
        }
    }

    /// <summary>Строки интерфейса конструктора на языке программы (плагин читает labels.txt).</summary>
    static readonly string[] Labels =
    [
        "saved", "hint", "create", "cantPlace", "move", "rotate", "scale", "undo", "save", "play", "search",

        "group.all", "group.vehicles", "group.buildings", "group.nature", "group.lights", "group.props", "group.scene",
        "refresh", "more", "nothing", 
        "shape.Cube", "shape.Plane", "shape.Sphere", "shape.Cylinder", "shape.Capsule", "shape.Quad", "shape.Light", "shape.Spot", "shape.Empty",
        "pickFirst", "applyTo", "myTextures", "openTextures", "gameMaterials", "sceneMaterial",
        "placed", "yours", "fromMap", "position", "rotation",
        "duplicate", "ground", "parent", "focus", "tint", "delete", "snap", "physics",
        "copied", "shot", "shotSaved", "selected", "level", "array", "openModels", "models.none",
        "section.models", "section.transform", "section.edit", "section.behaviour", "section.look",
        "beh.spin", "beh.bob", "world.text", "world.sun", "world.noSun", "world.brightness", "world.height", "world.direction",
        "world.dawn", "world.noon", "world.sunset", "world.night", "world.ambient", "world.fog", "world.fogOn", "world.density",
        // 23.4: редактор как UE5 / Roblox Studio — лента, проводник, свойства компонентов, вывод, группы, отмена и повтор.
        "select", "world", "copy", "paste", "group", "ungroup", "groupName", "groupOnlyYours", "redo", "undone", "redone",
        "toolbox", "explorer.title",
        "properties", "output", "clear", "lighting", "components", "noFields", "propsEmpty", "placing", "cancelled", "camSpeed",
        "dragHint", "shapesTip", "cmd.place", "simulate", "simulating", "simStopped",
        // 23.7: меню под игру и облегчённый док.
        "forGame", "thisGame", "cats.more", "cats.less", "dockMore", "keysTitle",
        // 23.6: текст, эффекты, земля, погода, день и ночь, кисть.
        "c.pick", "c.applyPick", "c.paintMode", "c.weather", "c.weatherOff", "c.cycle", "c.cycleOff", "paint.mode", "paint.on", "paint.picked", "paint.empty", "k.text", "k.textBig", "k.textGlow", "k.water", "k.hill", "k.road", "k.crossroad", "k.mountain", "fx.fire", "fx.smoke", "fx.sparks", "fx.magic", "fx.fireflies", "fx.confetti", "fx.bubbles", "fx.snow", "fx.rain", "fx.mist", "fx.jet", "weather.rain", "weather.storm", "weather.snow", "weather.dust", "weather.petals", "weather.embers", "cat.terrain", "cat.effects", "cat.text", "tab.materials",
        // 23.5: 500+ команд, 200 готовых деталей, острова, палитра команд.
        "g.add", "g.transform", "g.arrange", "g.edit", "g.color", "g.look", "g.behaviour", "g.physics", 
        "g.world", "g.camera", "g.select", "g.view", "g.file", "g.info", "cat.build", "cat.shapes", 
        "cat.nature", "cat.furniture", "cat.vehicles", "cat.lights", "cat.game", "cat.fromGame", "cat.models", "cat.materials", 
        "hue.red", "hue.orange", "hue.amber", "hue.yellow", "hue.lime", "hue.green", "hue.teal", "hue.cyan", 
        "hue.blue", "hue.indigo", "hue.purple", "hue.pink", "hue.white", "hue.silver", "hue.gray", "hue.graphite", 
        "hue.black", "hue.cream", "hue.sand", "hue.brown", "tone.warm", "tone.white", "tone.cold", "tone.red", 
        "tone.green", "tone.blue", "tone.purple", "tone.pink", "tone.cyan", "tone.black", "look.matte", "look.satin", 
        "look.glossy", "look.mirror", "look.metal", "look.brushed", "look.chrome", "look.gold", "look.copper", "look.silver", 
        "look.rubber", "view.top", "view.bottom", "view.front", "view.back", "view.left", "view.right", "view.iso", 
        "where.min", "where.center", "where.max", "fog.light", "fog.medium", "fog.thick", "fr.ice", "fr.normal", 
        "fr.grip", "amb.day", "amb.night", "amb.sunset", "amb.toxic", "amb.neutral", "accent.amber", "accent.sky", 
        "accent.mint", "accent.rose", "accent.violet", "arrange", "dockHint", "done", "lightsTip", "moreCmds", 
        "needSel", "needTwo", "needThree", "onlyYours", "palette", "palHint", "palRecent", "palFound", 
        "showUi", "unsaved", "helpText", "shade.0", "shade.2", "shade.3", "c.add", "c.nudge", 
        "c.rot", "c.scale", "c.scaleAxis", "c.size", "c.resetRot", "c.resetScale", "c.resetAll", "c.mirror", 
        "c.randRotY", "c.randRot", "c.randScale", "c.jitter", "c.align", "c.distribute", "c.snapPos", "c.snapRot", 
        "c.stackOn", "c.toCamera", "c.faceCamera", "c.lookAt", "c.alignNormal", "c.copyT", "c.pasteP", "c.pasteR", 
        "c.pasteS", "c.array", "c.circle", "c.gridArr", "c.scatter", "c.stack", "c.spiral", "c.dupHere", 
        "c.dupTo", "c.pasteHere", "c.hide", "c.showAll", "c.isolate", "c.restoreLevel", "c.clearAll", "c.color", 
        "c.colorNone", "c.look", "c.glow", "c.lookPlain", "c.spin", "c.bob", "c.pulse", "c.swing", 
        "c.orbit", "c.blink", "c.rainbowSlow", "c.rainbowFast", "c.flicker", "c.wobble", "c.billboard", "c.drift", 
        "c.behNone", "c.physOn", "c.physOff", "c.mass", "c.gravityOff", "c.gravityOn", "c.freezeRot", "c.bounce", 
        "c.friction", "c.trigger", "c.solid", "c.addBox", "c.kick", "c.kickBig", "c.push", "c.explode", 
        "c.time", "c.sunI", "c.sunC", "c.ambI", "c.ambC", "c.fogOff", "c.fog", "c.fogC", 
        "c.shadow", "c.worldReset", "c.camSpeed", "c.fov", "c.view", "c.bmSave", "c.bmGo", "c.bmSaved", 
        "c.bmNone", "c.frameAll", "c.ortho", "c.camGame", "c.selAll", "c.selNone", "c.selInvert", "c.selLights", 
        "c.selShapes", "c.selKits", "c.selModels", "c.selGroups", "c.selPhysics", "c.selBeh", "c.selGame", "c.selSame", 
        "c.selColor", "c.selLook", "c.selChildren", "c.selSiblings", "c.selNearest", "c.selNear", "c.selVisible", "c.gridToggle", 
        "c.gridSize", "c.snapToggle", "c.localToggle", "c.labels", "c.stats", "c.panelAdd", "c.panelScene", "c.panelProps", 
        "c.panelOutput", "c.hideUi", "c.accent", "c.uiScale", "c.help", "c.revert", "c.reverted", "c.backup", 
        "c.backupDone", "c.export", "c.exported", "c.insertSet", "c.insertSetName", "c.shot", "c.openMaps", "c.distance", 
        "c.distanceIs", "c.sizeInfo", "c.sizeIs", "c.sceneInfo", "c.sceneIs", "k.wall", "k.floor", "k.platform", 
        "k.beam", "k.pillar", "k.ramp", "k.stairs", "k.spiral", "k.arch", "k.door", "k.window", 
        "k.fence", "k.railing", "k.house", "k.hut", "k.tower", "k.bridge", "k.gate", "k.castle", 
        "k.prim", "k.cone", "k.pyramid", "k.prism", "k.wedge", "k.torus", "k.tube", "k.dome", 
        "k.ring", "k.hex", "k.tree", "k.pine", "k.bush", "k.rock", "k.boulders", "k.log", 
        "k.stump", "k.flowers", "k.cactus", "k.mushroom", "k.table", "k.chair", "k.bench", "k.stool", 
        "k.shelf", "k.bed", "k.sofa", "k.crate", "k.barrel", "k.bin", "k.sign", "k.chest", 
        "k.fountain", "k.well", "k.ladder", "k.mailbox", "k.lamppost", "k.streetlamp", "k.campfire", "k.car", 
        "k.truck", "k.kart", "k.boat", "k.bus", "k.plane", "k.rocket", "k.light", "k.spot", 
        "k.bulb", "k.torch", "k.neon", "k.coin", "k.gem", "k.checkpoint", "k.jumppad", "k.spawn", 
        "k.flag", "k.spikes", "k.target", "k.button", "k.portal", "k.laser", "k.arrow", 
    ];

    public static IReadOnlyList<string> LabelKeys => Labels;

    /// <summary>Собрать конструктор под игру и включить режим разработчика. null — готово, иначе — текст ошибки.</summary>
    public static string? Prepare(string gamePath, IProgress<string>? progress = null)
    {
        if (Unsupported(gamePath) is { } why) return I18n.T(why);
        if (DevCyberpunk.Is(gamePath)) return DevCyberpunk.Prepare(gamePath, progress).GetAwaiter().GetResult();
        if (DevEngines.KindOf(gamePath) is { } engine) return DevEngines.Prepare(engine, gamePath, progress).GetAwaiter().GetResult();
        if (!File.Exists(Path.Combine(gamePath, "BepInEx", "core", "BepInEx.dll")) && InstallBepInEx(gamePath, progress) is { } noLoader) return noLoader;
        var error = UnityCompiler.Compile(Source(), Plugin, PluginDir(gamePath), gamePath, engineOnly: true);
        if (error is not null) return error;
        // 23.3: UnityExplorer рядом — «Все свойства» объекта и компоненты (как Properties в Roblox Studio). Не встал — конструктор работает и без него.
        // В Unity 6 он пока ломается (MissingMethodException: Scene.handle, проверено в PEAK) — там не ставим, а свой убираем.
        if (UnityMajor(gamePath) >= 6000) DevEngines.RemoveExplorer(gamePath);
        else if (DevEngines.Install([DevEngines.ExplorerMono], gamePath, progress, default).GetAwaiter().GetResult() is null) DevEngines.SetupExplorer(gamePath);
        Directory.CreateDirectory(ConfigDir(gamePath));
        Directory.CreateDirectory(Path.Combine(ConfigDir(gamePath), "textures"));
        Directory.CreateDirectory(Path.Combine(ConfigDir(gamePath), "models"));
        File.WriteAllLines(Path.Combine(ConfigDir(gamePath), "labels.txt"),
            Labels.Select(k => k + "=" + I18n.T("v23.st." + k).Replace("\r", "").Replace("\n", "\\n")), new UTF8Encoding(false));
        File.WriteAllText(Flag(gamePath), DateTime.UtcNow.ToString("o"));
        return null;
    }

    /// <summary>Игра закрылась — выключить режим разработчика: сделанное остаётся модом, кнопки в игре больше нет.</summary>
    public static void Leave(string? gamePath)
    {
        if (gamePath is null) return;
        foreach (var flag in new[] { Flag(gamePath), DevCyberpunk.Flag(gamePath) })
            try { if (File.Exists(flag)) File.Delete(flag); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        DevEngines.Leave(gamePath);
    }

    public static bool Active(string? gamePath) => gamePath is not null && (File.Exists(Flag(gamePath)) || File.Exists(DevCyberpunk.Flag(gamePath)) || DevEngines.Active(gamePath));

    /// <summary>
    /// Как устроен режим разработчика этой игры: unity (свой конструктор, F8), cyberpunk (CET и Entity Spawner),
    /// unreal (UE4SS), re (REFramework), il2cpp (UnityExplorer), creation (консоль Bethesda).
    /// </summary>
    public static string KindOf(string? gamePath) => DevCyberpunk.Is(gamePath) ? "cyberpunk" : DevEngines.KindOf(gamePath) ?? "unity";

    /// <summary>Ключ строки для движка этой игры: route (что будет), inside (уже в игре), started, preparing.</summary>
    public static string Key(string? gamePath, string what) => KindOf(gamePath) switch
    {
        "unity" => what == "preparing" ? "v23.dev.building" : "v23.dev." + what,
        "cyberpunk" => "v23.dev.cp." + what,
        var kind => $"v23.dev.{kind}.{what}",
    };

    /// <summary>Сохранённые уровни (карты), сделанные в конструкторе: имя уровня и число поставленных объектов.</summary>
    public static List<(string Level, int Objects, DateTime At)> Worlds(string? gamePath)
    {
        var list = new List<(string, int, DateTime)>();
        if (gamePath is not null && DevCyberpunk.Is(gamePath)) return DevCyberpunk.Worlds(gamePath).OrderByDescending(w => w.At).ToList();
        if (gamePath is null || !Directory.Exists(ConfigDir(gamePath))) return list;
        foreach (var f in Directory.EnumerateFiles(ConfigDir(gamePath), "*.layout"))
            try { list.Add((Path.GetFileNameWithoutExtension(f), File.ReadLines(f).Count(l => l.StartsWith("place|", StringComparison.Ordinal) || l.StartsWith("hide|", StringComparison.Ordinal) || l.StartsWith("move|", StringComparison.Ordinal)), File.GetLastWriteTimeUtc(f))); }
            catch (IOException) { }
        return list.OrderByDescending(w => w.Item3).ToList();
    }

    /// <summary>Убрать конструктор и все сделанные в нём карты из игры.</summary>
    public static void Remove(string gamePath)
    {
        foreach (var dir in new[] { PluginDir(gamePath), ConfigDir(gamePath) })
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        DevEngines.Remove(gamePath);
    }

    /// <summary>Найти Unity-игру с BepInEx для проверки сборки: известные игры ModLaunch или MODLAUNCH_UNITY_GAME.</summary>
    static IEnumerable<string> TestGames()
    {
        if (Environment.GetEnvironmentVariable("MODLAUNCH_UNITY_GAME") is { Length: > 0 } env) foreach (var p in env.Split(';')) yield return p;
        foreach (var g in AppState.Games.Where(g => g.Path is not null)) yield return g.Path!;
    }

    [SelfTest]
    static string StudioBuildsForRealGames()
    {
        var source = Source();
        foreach (var key in Labels)
            // Ключ стоит в исходнике строкой "ключ" или собирается из приставки: T("приставка." + …).
            if (!source.Contains($"\"{key}\"") && !(key.Contains('.') && source.Contains($"T(\"{key[..key.LastIndexOf('.')]}.\" +")))
                throw new Exception("label not used by the studio: " + key);
        var games = TestGames().Where(p => Directory.Exists(p) && Unsupported(p) is null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (games.Count == 0) return "no Unity game with BepInEx here — compile check skipped (set MODLAUNCH_UNITY_GAME)";
        var done = new List<string>();
        foreach (var game in games)
        {
            var outDir = Path.Combine(Path.GetTempPath(), "modlaunch-studio-" + Environment.ProcessId);
            try
            {
                var error = UnityCompiler.Compile(source, Plugin, outDir, game, engineOnly: true);
                if (error is not null) throw new Exception(Path.GetFileName(game) + ":\n" + error);
                done.Add(Path.GetFileName(game.TrimEnd('\\', '/')));
            }
            finally { try { Directory.Delete(outDir, true); } catch (IOException) { } }
        }
        return "studio compiles against " + string.Join(", ", done);
    }
}
