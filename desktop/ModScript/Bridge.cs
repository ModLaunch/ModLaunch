namespace ModScriptX;

/// <summary>
/// 16.0: что за игра в папке и как ModScript до неё дотянуться — «мост».
/// Path — как мод попадает в игру: code (свой код в движке через загрузчик), files (только файлы, конфиги и ассеты).
/// </summary>
public sealed record BridgeReport(
    string Engine, string Title, int Confidence, List<string> Evidence, string? TargetId, string Loader, bool LoaderReady,
    string Path, string? AntiCheat, string? Hint)
{
    /// <summary>Есть ли у ModScript X перевод для этой игры (свой код, а не только файлы).</summary>
    public bool Code => Path == "code" && TargetId is not null && AntiCheat is null;
}

/// <summary>
/// 16.0: мост ModScript к любой игре по алгоритму. Три шага:
/// 1) отпечатки — каждый движок узнаётся по своим файлам (Assembly-CSharp.dll, *.pak, data.win, *.rpa, *.esm…),
///    у каждой улики свой вес; побеждает движок с наибольшей суммой, из неё — уверенность (0–100);
/// 2) маршрут — для движка выбирается загрузчик модов и язык (BepInEx → C#, UE4SS → Lua, plugins.js → JavaScript…);
///    нет загрузчика с кодом — маршрут «файлы» (конфиги, ini, замена ассетов через ModScript);
/// 3) проверки — античит (тогда только файлы для одиночной игры) и установлен ли загрузчик.
/// Папка просматривается неглубоко (до трёх уровней, не больше нескольких тысяч записей) — это быстро даже на HDD.
/// </summary>
public static class Bridge
{
    sealed record Engine(string Id, string Title, string? Target, string Loader, string Path);

    static readonly Engine[] Engines =
    [
        new("unity-mono", "Unity (Mono)", "unity", "BepInEx 5", "code"),
        new("unity-il2cpp", "Unity (IL2CPP)", "unity", "BepInEx 6 IL2CPP", "code"),
        new("unreal", "Unreal Engine", "unreal", "UE4SS", "code"),
        new("godot", "Godot", "godot", "Godot Mod Loader", "code"),
        new("renpy", "Ren'Py", "renpy", "game/*.rpy", "code"),
        new("rpgmaker", "RPG Maker MV/MZ", "rpgmaker", "js/plugins.js", "code"),
        new("html5", "HTML5 (NW.js / Electron)", "html5", "index.html", "code"),
        new("reengine", "RE Engine", "reengine", "REFramework", "code"),
        new("redengine", "REDengine (Cyberpunk 2077)", "cyberpunk", "Cyber Engine Tweaks", "code"),
        new("rage-gta5", "RAGE (GTA V)", "gta5", "ScriptHookV .NET", "code"),
        new("rage-rdr2", "RAGE (Red Dead Redemption 2)", "rdr2", "ScriptHookRDR2 .NET", "code"),
        new("love", "LÖVE (Lua)", "lua", "mods/*.lua", "code"),
        new("lua", "Lua-moddable game", "lua", "mods/*.lua", "code"),
        new("rpgmaker-ruby", "RPG Maker XP/VX/VX Ace", null, "Scripts.rvdata", "files"),
        new("gamemaker", "GameMaker", null, "data.win", "files"),
        new("source", "Source", null, "gameinfo.txt / custom", "files"),
        new("source2", "Source 2", null, "gameinfo.gi / addons", "files"),
        new("creation", "Creation Engine (Bethesda)", null, "Data/*.esp", "files"),
        new("java", "Java (LWJGL)", null, "mods/*.jar", "files"),
        new("xna", "XNA / FNA / MonoGame", null, "Content/", "files"),
        new("cryengine", "CryEngine", null, "*.pak", "files"),
        new("frostbite", "Frostbite", null, "Frosty Mod Manager", "files"),
        new("idtech", "id Tech", null, "*.pk3 / *.pk4", "files"),
        new("generic", "Unknown engine", null, "files", "files"),
    ];

    /// <summary>Снимок папки: имена файлов и папок до трёх уровней (относительные пути, «/»).</summary>
    sealed class Tree
    {
        public readonly List<string> Files = [];
        public readonly List<string> Dirs = [];

        public Tree(string root)
        {
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));
            var budget = 6000;
            while (queue.Count > 0 && budget > 0)
            {
                var (dir, depth) = queue.Dequeue();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                    {
                        Files.Add(Rel(root, f));
                        if (--budget <= 0) break;
                    }
                    foreach (var d in Directory.EnumerateDirectories(dir))
                    {
                        Dirs.Add(Rel(root, d));
                        if (depth < 3) queue.Enqueue((d, depth + 1));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }

        static string Rel(string root, string p) => System.IO.Path.GetRelativePath(root, p).Replace('\\', '/');

        public bool File(string name) => Files.Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase));
        public bool Dir(string name) => Dirs.Any(d => d.Equals(name, StringComparison.OrdinalIgnoreCase));
        /// <summary>Файл с таким окончанием пути (без учёта регистра): «_Data/Managed/Assembly-CSharp.dll».</summary>
        public bool EndsWith(string tail) => Files.Any(f => f.EndsWith(tail, StringComparison.OrdinalIgnoreCase));
        public bool DirEndsWith(string tail) => Dirs.Any(d => d.EndsWith(tail, StringComparison.OrdinalIgnoreCase));
        public bool Ext(string ext, int maxDepth = 3) => Files.Any(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && f.Count(c => c == '/') <= maxDepth);
        public int Count(string ext) => Files.Count(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Улики: (движок, вес, что нашли). Веса подобраны так, что одна «железная» улика даёт уверенность 90+.</summary>
    static List<(string Engine, int Weight, string What)> Clues(Tree t)
    {
        var c = new List<(string, int, string)>();
        void Add(bool hit, string engine, int weight, string what) { if (hit) c.Add((engine, weight, what)); }

        // Unity
        var managed = t.EndsWith("_Data/Managed/Assembly-CSharp.dll");
        Add(managed, "unity-mono", 70, "*_Data/Managed/Assembly-CSharp.dll");
        Add(t.File("UnityPlayer.dll"), managed ? "unity-mono" : "unity-il2cpp", 25, "UnityPlayer.dll");
        Add(t.File("GameAssembly.dll"), "unity-il2cpp", 70, "GameAssembly.dll");
        Add(t.DirEndsWith("_Data/il2cpp_data"), "unity-il2cpp", 25, "*_Data/il2cpp_data");
        Add(!managed && t.EndsWith("_Data/globalgamemanagers"), "unity-il2cpp", 5, "*_Data/globalgamemanagers");
        Add(managed && t.EndsWith("_Data/globalgamemanagers"), "unity-mono", 5, "*_Data/globalgamemanagers");
        // Unreal
        Add(t.Ext(".pak") && t.DirEndsWith("Content/Paks"), "unreal", 60, "*/Content/Paks/*.pak");
        Add(t.Ext(".utoc") || t.Ext(".ucas"), "unreal", 20, "*.utoc / *.ucas (UE5 IoStore)");
        Add(t.Files.Any(f => f.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase)), "unreal", 30, "*-Win64-Shipping.exe");
        Add(t.Dir("Engine") && t.DirEndsWith("Engine/Binaries"), "unreal", 20, "Engine/Binaries");
        // Godot
        Add(t.File("project.godot"), "godot", 80, "project.godot");
        Add(t.Ext(".pck", 1), "godot", 60, "*.pck");
        // Ren'Py
        Add(t.Dir("renpy"), "renpy", 70, "renpy/");
        Add(t.Files.Any(f => f.StartsWith("game/", StringComparison.OrdinalIgnoreCase) && (f.EndsWith(".rpy") || f.EndsWith(".rpyc") || f.EndsWith(".rpa"))), "renpy", 40, "game/*.rpy / *.rpa");
        // RPG Maker MV/MZ и HTML5
        var rpgmv = t.File("www/js/rpg_core.js") || t.File("js/rmmz_core.js") || t.File("www/js/rpg_managers.js");
        Add(rpgmv, "rpgmaker", 80, "js/rpg_core.js / rmmz_core.js");
        Add(t.File("www/js/plugins.js") || t.File("js/plugins.js"), "rpgmaker", 25, "js/plugins.js");
        var index = t.File("www/index.html") || t.File("index.html") || t.File("resources/app/index.html") || t.File("package.nw/index.html");
        Add(!rpgmv && (t.File("nw.dll") || t.File("nw.exe") || t.File("package.nw") || t.Dir("package.nw")), "html5", 40, "NW.js (nw.dll / package.nw)");
        Add(!rpgmv && (t.File("resources/app.asar") || t.Dir("resources/app")), "html5", 40, "Electron (resources/app)");
        Add(!rpgmv && index, "html5", 25, "index.html");
        // RE Engine
        Add(t.Files.Any(f => f.StartsWith("re_chunk_000.pak", StringComparison.OrdinalIgnoreCase)), "reengine", 85, "re_chunk_000.pak");
        Add(t.Dir("reframework") || t.File("dinput8.dll") && t.Files.Any(f => f.StartsWith("re_chunk", StringComparison.OrdinalIgnoreCase)), "reengine", 10, "reframework/");
        // 22.0: REDengine 4 (Cyberpunk 2077) и RAGE (GTA V, RDR2)
        Add(t.File("bin/x64/Cyberpunk2077.exe"), "redengine", 85, "bin/x64/Cyberpunk2077.exe");
        Add(t.Dir("archive/pc/content"), "redengine", 20, "archive/pc/content");
        Add(t.Dir("r6") || t.Dir("red4ext"), "redengine", 5, "r6/ (redscript)");
        Add(t.File("GTA5.exe") || t.File("GTA5_Enhanced.exe"), "rage-gta5", 80, t.File("GTA5_Enhanced.exe") ? "GTA5_Enhanced.exe" : "GTA5.exe");
        Add(t.File("x64a.rpf") || t.File("common.rpf") && t.File("update/update.rpf"), "rage-gta5", 20, "x64a.rpf / update/update.rpf");
        Add(t.File("RDR2.exe"), "rage-rdr2", 85, "RDR2.exe");
        Add(t.File("ScriptHookRDR2.dll"), "rage-rdr2", 10, "ScriptHookRDR2.dll");
        // LÖVE и Lua
        Add(t.File("love.dll") || t.File("love.exe"), "love", 60, "love.dll");
        Add(t.Ext(".love", 1), "love", 50, "*.love");
        Add(t.Count(".lua") >= 3, "lua", 25, $"{t.Count(".lua")} Lua scripts");
        Add(t.File("main.lua"), "lua", 20, "main.lua");
        // RPG Maker на Ruby
        Add(t.Files.Any(f => f.StartsWith("Game.rgss", StringComparison.OrdinalIgnoreCase)), "rpgmaker-ruby", 70, "Game.rgss*a");
        Add(t.Files.Any(f => f.StartsWith("RGSS", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)), "rpgmaker-ruby", 30, "RGSS*.dll");
        // GameMaker
        Add(t.File("data.win"), "gamemaker", 80, "data.win");
        Add(t.File("options.ini") && t.File("data.win"), "gamemaker", 5, "options.ini");
        // Source / Source 2
        Add(t.Files.Any(f => f.EndsWith("/gameinfo.txt", StringComparison.OrdinalIgnoreCase)), "source", 60, "*/gameinfo.txt");
        Add(t.File("bin/engine.dll"), "source", 25, "bin/engine.dll");
        Add(t.Files.Any(f => f.EndsWith("/gameinfo.gi", StringComparison.OrdinalIgnoreCase)), "source2", 70, "*/gameinfo.gi");
        Add(t.DirEndsWith("game/bin/win64"), "source2", 20, "game/bin/win64");
        // Bethesda
        Add(t.Files.Any(f => f.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)), "creation", 70, "Data/*.esm");
        Add(t.Files.Any(f => f.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) && (f.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bsa", StringComparison.OrdinalIgnoreCase))), "creation", 25, "Data/*.ba2 / *.bsa");
        // Java
        Add(t.Files.Any(f => f.StartsWith("lwjgl", StringComparison.OrdinalIgnoreCase) || f.Contains("/lwjgl", StringComparison.OrdinalIgnoreCase)), "java", 40, "lwjgl");
        Add(t.Ext(".jar", 1) && (t.Dir("jre") || t.Dir("runtime") || t.Dir("jdk")), "java", 40, "*.jar + bundled Java");
        // XNA / FNA / MonoGame
        Add(t.File("FNA.dll"), "xna", 70, "FNA.dll");
        Add(t.File("MonoGame.Framework.dll"), "xna", 70, "MonoGame.Framework.dll");
        Add(t.Files.Any(f => f.StartsWith("Microsoft.Xna.Framework", StringComparison.OrdinalIgnoreCase)), "xna", 60, "Microsoft.Xna.Framework*.dll");
        // CryEngine, Frostbite, id Tech
        Add(t.File("Bin64/CrySystem.dll") || t.EndsWith("/CrySystem.dll"), "cryengine", 80, "CrySystem.dll");
        Add(t.File("Data/cas.cat") || t.Files.Any(f => f.EndsWith("/cas.cat", StringComparison.OrdinalIgnoreCase)), "frostbite", 70, "Data/cas.cat");
        Add(t.Files.Any(f => f.EndsWith(".pk3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pk4", StringComparison.OrdinalIgnoreCase)), "idtech", 60, "*.pk3 / *.pk4");
        return c;
    }

    /// <summary>Установлен ли загрузчик модов для движка.</summary>
    static bool LoaderReady(string engine, Tree t, string dir) => engine switch
    {
        "unity-mono" or "unity-il2cpp" => t.Dir("BepInEx/core") || t.File("winhttp.dll") && t.Dir("BepInEx"),
        "unreal" => t.Files.Any(f => f.EndsWith("/UE4SS.dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith("/ue4ss/UE4SS.dll", StringComparison.OrdinalIgnoreCase)),
        "reengine" => t.Dir("reframework") && t.File("dinput8.dll"),
        "godot" => t.Dir("mods") || t.File("override.cfg"),
        "redengine" => t.File("bin/x64/plugins/cyber_engine_tweaks.asi"),
        "rage-gta5" => t.File("ScriptHookV.dll") && t.File("ScriptHookVDotNet3.dll"),
        "rage-rdr2" => t.File("ScriptHookRDR2.dll") && t.Files.Any(f => !f.Contains('/') && f.StartsWith("ScriptHookRDR2DotNet", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)),
        "renpy" or "rpgmaker" or "html5" or "love" or "lua" => true,
        _ => false,
    };

    static string? Hint(string engine, bool ready) => (engine, ready) switch
    {
        ("unity-mono", false) => "install BepInEx 5 (x64) into the game folder and run the game once",
        ("unity-il2cpp", false) => "install BepInEx 6 IL2CPP (bleeding edge) and run the game once so it builds interop assemblies",
        ("unity-il2cpp", true) => "IL2CPP: plugins build against BepInEx/interop; run the game once after updates",
        ("unreal", false) => "install UE4SS into <Project>/Binaries/Win64 (next to the game exe)",
        ("reengine", false) => "install REFramework (dinput8.dll) for this game",
        ("godot", false) => "set up Godot Mod Loader (mods/*.zip) for this game",
        ("redengine", false) => "install Cyber Engine Tweaks into bin/x64 and start the game once (CET asks for an overlay key)",
        ("rage-gta5", false) => "install ScriptHookV (ScriptHookV.dll, dinput8.dll) and ScriptHookVDotNet 3 into the game folder; story mode only",
        ("rage-gta5", true) => "story mode only: ScriptHookV switches itself off in GTA Online",
        ("rage-rdr2", false) => "install ScriptHookRDR2 (ScriptHookRDR2.dll, dinput8.dll) and ScriptHookRDR2 .NET into the game folder; story mode only",
        ("rage-rdr2", true) => "story mode only: ScriptHookRDR2 switches itself off in Red Dead Online",
        ("html5", _) => "packed Electron games (resources/app.asar) need unpacking first",
        ("creation", _) => "Bethesda games: plugins (.esp) and Papyrus need the Creation Kit; ModScript manages Data files and ini",
        ("source" or "source2", _) => "Source games: ModScript manages addons/custom files, cfg and launch options",
        ("gamemaker", _) => "GameMaker: data.win patches need UndertaleModTool; ModScript manages files and options.ini",
        ("generic", _) => "no known engine: ModScript works through files — configs, ini and replaced assets",
        _ => null,
    };

    /// <summary>Узнать игру и выбрать мост. Ошибки чтения папки не бросаются — получится «неизвестный движок».</summary>
    public static BridgeReport Scan(string gameDir)
    {
        var t = new Tree(gameDir);
        var clues = Clues(t);
        string? ac = null;
        try { ac = ModScriptX.Engines.FindAntiCheat(gameDir); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var best = clues.GroupBy(x => x.Engine).Select(g => (Engine: g.Key, Score: g.Sum(x => x.Weight), What: g.Select(x => x.What).ToList()))
            .OrderByDescending(x => x.Score).ThenBy(x => Array.FindIndex(Engines, e => e.Id == x.Engine)).FirstOrDefault();
        var engine = Engines.First(e => e.Id == (best.Engine ?? "generic"));
        var score = best.Engine is null ? 0 : Math.Min(99, 30 + best.Score * 7 / 10);
        // RAGE: BattlEye у Rockstar — для GTA Online и Red Dead Online, а ScriptHook сам не работает в сети: сюжету код можно.
        if (engine.Id.StartsWith("rage-")) ac = null;
        var ready = LoaderReady(engine.Id, t, gameDir);
        var path = ac is not null ? "files" : engine.Path;
        var evidence = best.What ?? ["no engine fingerprints found"];
        return new BridgeReport(engine.Id, engine.Title, score, evidence, engine.Target, engine.Loader, ready, path, ac, ac is not null
            ? $"{ac} found: code mods are off for anti-cheat protected games; ModScript works through files only"
            : Hint(engine.Id, ready));
    }

    /// <summary>Движок ModScript X для отчёта (null — только файлы).</summary>
    public static Target? TargetOf(BridgeReport r) => r.Code ? Target.Find(r.TargetId!) : null;

    /// <summary>Все движки, которые узнаёт мост: (id, название, есть ли перевод кода).</summary>
    public static IEnumerable<(string Id, string Title, bool Code)> Known => Engines.Where(e => e.Id != "generic").Select(e => (e.Id, e.Title, e.Target is not null));
}
