using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 23.3: «Играть как разработчик» для больших игр на чужих движках. На каждый движок — свой проверенный открытый
/// инструмент сообщества, который даёт разработческий режим прямо внутри игры; ModLaunch ставит его с официального
/// GitHub (с докачкой, только внутрь папки игры) и добавляет ModScript Studio:
/// · Unreal Engine 4/5 — UE4SS: свободная камера Unreal (F8), пауза мира (F7), полёт (F6), консоль разработчика (~),
///   менеджер читов для summon/teleport/set и окно Live View со всеми объектами и их свойствами (Ctrl+O);
/// · RE Engine (Resident Evil, Monster Hunter, Devil May Cry, Dragon's Dogma) — REFramework: меню в игре (Insert),
///   свободная камера, обозреватель объектов, свои Lua-скрипты, плашка ModScript Studio;
/// · Unity на IL2CPP — BepInEx 6 и UnityExplorer (F7): дерево сцены, свойства и компоненты объектов, свободная камера, консоль C#;
/// · Creation Engine (Skyrim, Fallout 4, Starfield) — своя консоль игры (~) и команда «bat studio»: свободная камера без интерфейса.
/// У Unity на Mono UnityExplorer ставится рядом с конструктором ModScript Studio — кнопка «Все свойства» в нём.
/// </summary>
public static partial class DevEngines
{
    static readonly string[] CreationExes = ["SkyrimSE.exe", "SkyrimVR.exe", "Fallout4.exe", "Fallout4VR.exe", "Starfield.exe"];

    /// <summary>Движок по файлам игры: re, creation, il2cpp, unreal или null.</summary>
    public static string? KindOf(string? gamePath)
    {
        if (gamePath is null || !Directory.Exists(gamePath)) return null;
        if (File.Exists(Path.Combine(gamePath, "re_chunk_000.pak"))) return "re";
        if (CreationExes.Any(e => File.Exists(Path.Combine(gamePath, e))) && Directory.Exists(Path.Combine(gamePath, "Data"))) return "creation";
        // 23.7: визуальные новеллы на Ren'Py, игры на RPG Maker MV/MZ и на Source (Half-Life 2, Portal, Garry's Mod…).
        if (Directory.Exists(Path.Combine(gamePath, "renpy")) && Directory.Exists(Path.Combine(gamePath, "game"))) return "renpy";
        if (RpgMakerJs(gamePath) is not null) return "rpgmaker";
        if (SourceMod(gamePath) is not null) return "source";
        if (File.Exists(Path.Combine(gamePath, "GameAssembly.dll")) && Directory.EnumerateDirectories(gamePath, "*_Data").Any()) return "il2cpp";
        if (ShippingDir(gamePath) is not null) return "unreal";
        return null;
    }

    /// <summary>
    /// Папка с exe игры на Unreal: …/Binaries/Win64 (или WinGDK) с *-Shipping.exe — либо с любым exe, если рядом
    /// с проектом лежит Content/Paks (так устроены игры с переименованным exe, например FF VII Remake).
    /// </summary>
    public static string? ShippingDir(string gamePath)
    {
        try
        {
            foreach (var project in Directory.EnumerateDirectories(gamePath).Append(gamePath))
                foreach (var plat in new[] { "Win64", "WinGDK" })
                {
                    var dir = Path.Combine(project, "Binaries", plat);
                    if (!Directory.Exists(dir)) continue;
                    if (Directory.EnumerateFiles(dir, "*-Shipping.exe").Any()) return dir;
                    if (Directory.Exists(Path.Combine(project, "Content", "Paks")) && Directory.EnumerateFiles(dir, "*.exe").Any()) return dir;
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    static string Bits(string gamePath)
    {
        try
        {
            using var s = File.OpenRead(Path.Combine(gamePath, "GameAssembly.dll"));
            using var pe = new System.Reflection.PortableExecutable.PEReader(s);
            return pe.PEHeaders.CoffHeader.Machine == System.Reflection.PortableExecutable.Machine.I386 ? "x86" : "x64";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException) { return "x64"; }
    }

    /// <summary>UnityExplorer для BepInEx 5 (Unity на Mono) — ставится в BepInEx рядом с конструктором.</summary>
    public static readonly DevCyberpunk.Tool ExplorerMono = new("explorer", "UnityExplorer", "yukieiji/UnityExplorer",
        n => n == "UnityExplorer.BepInEx5.Mono.zip", Path.Combine("plugins", "sinai-dev-UnityExplorer", "UnityExplorer.BIE5.Mono.dll"), Into: "BepInEx");

    /// <summary>Инструменты движка (и куда их класть).</summary>
    public static DevCyberpunk.Tool[] ToolsFor(string kind, string gamePath) => kind switch
    {
        "unreal" =>
        [
            new("ue4ss", "UE4SS", "UE4SS-RE/RE-UE4SS", n => n.StartsWith("UE4SS_v", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal),
                Path.Combine("ue4ss", "UE4SS.dll"), Tag: "experimental-latest", Into: Path.GetRelativePath(gamePath, ShippingDir(gamePath) ?? gamePath)),
        ],
        "re" =>
        [
            new("reframework", "REFramework", "praydog/REFramework-nightly", n => n == "REFramework.zip", "dinput8.dll"),
        ],
        "il2cpp" =>
        [
            new("bepinex6", "BepInEx 6", "BepInEx/BepInEx", n => n == $"BepInEx-Unity.IL2CPP-win-{Bits(gamePath)}-6.0.0-pre.2.zip", Path.Combine("BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"), Tag: "v6.0.0-pre.2"),
            new("explorer", "UnityExplorer", "yukieiji/UnityExplorer", n => n == "UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR.zip",
                Path.Combine("plugins", "sinai-dev-UnityExplorer", "UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll"), Into: "BepInEx"),
        ],
        _ => [],
    };

    /// <summary>Поставить недостающие инструменты. null — готово, иначе — текст ошибки.</summary>
    public static async Task<string?> Install(IEnumerable<DevCyberpunk.Tool> tools, string gamePath, IProgress<string>? progress, CancellationToken ct)
    {
        var temp = Directory.CreateDirectory(Path.Combine(Paths.CacheDir, "dev-engines")).FullName;
        foreach (var t in tools)
        {
            if (DevCyberpunk.Installed(gamePath, t)) continue;
            try
            {
                progress?.Report(t.Title);
                var (url, name, size, _) = await DevCyberpunk.Latest(t, ct);
                var file = Path.Combine(temp, name);
                await DevCyberpunk.Download(url, size, file, ct);
                DevCyberpunk.Extract(file, Path.Combine(gamePath, t.Into));
                if (!DevCyberpunk.Installed(gamePath, t)) return $"{t.Title}: {I18n.T("v23.dev.cp.badArchive")}";
                // Отметка «поставил ModLaunch»: «Убрать конструктор» удаляет только своё.
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.Combine(gamePath, t.Into, t.Marker))!, ".modlaunch"), t.Id);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException)
            {
                return $"{t.Title}: {e.Message}";
            }
        }
        return null;
    }

    /// <summary>Режим разработчика для движка: инструменты, настройки и ModScript Studio.</summary>
    public static async Task<string?> Prepare(string kind, string gamePath, IProgress<string>? progress, CancellationToken ct = default)
    {
        if (await Install(ToolsFor(kind, gamePath), gamePath, progress, ct) is { } error) return error;
        switch (kind)
        {
            case "unreal": SetupUnreal(gamePath); break;
            case "re": SetupRe(gamePath, true); break;
            case "il2cpp": SetupExplorer(gamePath); break;
            case "creation": SetupCreation(gamePath); break;
            case "renpy": SetupRenPy(gamePath); break;
            case "rpgmaker": SetupRpgMaker(gamePath); break;
            case "source": SetupSource(gamePath); break;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Flag(gamePath))!);
        File.WriteAllText(Flag(gamePath), gamePath);
        return null;
    }

    /// <summary>Отметка «игра запущена как разработчик» (у ModLaunch, не в папке игры).</summary>
    public static string Flag(string gamePath) =>
        Path.Combine(Paths.DataDir, "dev-active", Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(gamePath).TrimEnd('\\', '/').ToLowerInvariant())))[..16] + ".flag");

    static string? UnrealMod(string gamePath) => ShippingDir(gamePath) is { } dir ? Path.Combine(dir, "ue4ss", "Mods", "ModScriptStudio") : null;

    /// <summary>UE4SS: консоль и менеджер читов Unreal, окно Live View (Ctrl+O) и свой мод ModScript Studio с клавишами.</summary>
    static void SetupUnreal(string gamePath)
    {
        var root = Path.Combine(ShippingDir(gamePath)!, "ue4ss");
        var ini = Path.Combine(root, "UE4SS-settings.ini");
        if (File.Exists(ini))
        {
            // Окно Live View есть, но не выскакивает само — открывается Ctrl+O.
            var lines = File.ReadAllLines(ini).Select(l =>
                l.StartsWith("GuiConsoleEnabled", StringComparison.Ordinal) ? "GuiConsoleEnabled = 1" :
                l.StartsWith("GuiConsoleVisible", StringComparison.Ordinal) ? "GuiConsoleVisible = 0" : l).ToArray();
            File.WriteAllLines(ini, lines);
        }
        var mods = Path.Combine(root, "Mods");
        Directory.CreateDirectory(mods);
        var txt = Path.Combine(mods, "mods.txt");
        var list = File.Exists(txt) ? File.ReadAllLines(txt).ToList() : [];
        foreach (var mod in new[] { "ConsoleEnablerMod", "CheatManagerEnablerMod", "ConsoleCommandsMod", "ModScriptStudio" })
        {
            var at = list.FindIndex(l => l.Split(':')[0].Trim().Equals(mod, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) list[at] = mod + " : 1";
            else
            {
                // Перед строкой Keybinds: UE4SS просит держать её последней.
                var keys = list.FindIndex(l => l.TrimStart().StartsWith("Keybinds", StringComparison.Ordinal));
                list.Insert(keys >= 0 ? keys : list.Count, mod + " : 1");
            }
        }
        File.WriteAllLines(txt, list);
        // Новые сборки UE4SS читают ещё и mods.json.
        var json = Path.Combine(mods, "mods.json");
        try
        {
            var arr = File.Exists(json) && JsonNode.Parse(File.ReadAllText(json)) is JsonArray a ? a : [];
            if (!arr.OfType<JsonObject>().Any(o => (string?)o["mod_name"] == "ModScriptStudio"))
            {
                var keys = arr.OfType<JsonObject>().ToList().FindIndex(o => (string?)o["mod_name"] == "Keybinds");
                var entry = new JsonObject { ["mod_name"] = "ModScriptStudio", ["mod_enabled"] = true };
                if (keys >= 0) arr.Insert(keys, entry); else arr.Add(entry);
            }
            File.WriteAllText(json, arr.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (System.Text.Json.JsonException) { }
        var mine = UnrealMod(gamePath)!;
        var script = Directory.CreateDirectory(Path.Combine(mine, "Scripts")).FullName;
        File.WriteAllText(Path.Combine(script, "main.lua"), Resource("studio-unreal").Replace("{{hint}}", Lua(I18n.T("v23.ue.hint"))), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(mine, "enabled.txt"), "");
        File.WriteAllText(Path.Combine(mine, "dev.flag"), DateTime.UtcNow.ToString("o"));
    }

    /// <summary>REFramework: свой Lua-скрипт ModScript Studio (плашка и раздел в меню) и его строки.</summary>
    static void SetupRe(string gamePath, bool dev)
    {
        var autorun = Directory.CreateDirectory(Path.Combine(gamePath, "reframework", "autorun")).FullName;
        File.WriteAllText(Path.Combine(autorun, "ModScriptStudio.lua"), Resource("studio-re"), new UTF8Encoding(false));
        var data = Directory.CreateDirectory(Path.Combine(gamePath, "reframework", "data")).FullName;
        var json = new JsonObject { ["dev"] = dev };
        foreach (var k in ReLabels) json[k] = I18n.T("v23.re." + k);
        if (GameProfile.Of(gamePath) is { } profile) json["title"] = "ModScript Studio · " + profile.Name;
        // Шрифт Windows с буквами языка программы: во встроенном шрифте ImGui кириллицы и иероглифов нет (были бы «?»).
        if (File.Exists(SystemFont()))
        {
            var fonts = Directory.CreateDirectory(Path.Combine(gamePath, "reframework", "fonts")).FullName;
            var name = "ModScriptStudio" + Path.GetExtension(SystemFont());
            if (!File.Exists(Path.Combine(fonts, name))) File.Copy(SystemFont(), Path.Combine(fonts, name));
            json["font"] = name;
            json["fontSize"] = 18;
            json["ranges"] = new JsonArray(GlyphRanges().Select(r => (JsonNode)r).ToArray());
        }
        File.WriteAllText(Path.Combine(data, "ModScriptStudio.json"), json.ToJsonString(), new UTF8Encoding(false));
    }

    public static readonly string[] ReLabels = ["title", "hint", "menu", "freecam", "explorer", "lua", "credit"];

    /// <summary>Шрифт Windows с буквами языка программы для окон ImGui в играх (REFramework, Cyber Engine Tweaks).</summary>
    public static string SystemFont() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), I18n.Lang == "zh-CN" ? "msyh.ttc" : "segoeui.ttf");

    /// <summary>Нужные буквы: латиница, кириллица, знаки и стрелки (для китайского — ещё иероглифы); пары «от–до», в конце 0.</summary>
    public static int[] GlyphRanges() => I18n.Lang == "zh-CN"
        ? [0x20, 0x24F, 0x400, 0x4FF, 0x2000, 0x206F, 0x2190, 0x21FF, 0x25A0, 0x25FF, 0x3000, 0x30FF, 0x4E00, 0x9FAF, 0xFF00, 0xFFEF, 0]
        : [0x20, 0x24F, 0x400, 0x52F, 0x2000, 0x206F, 0x2190, 0x21FF, 0x25A0, 0x25FF, 0];

    /// <summary>Creation Engine: «bat studio» в консоли игры — свободная камера с остановленным временем и без интерфейса (ещё раз — назад).</summary>
    static void SetupCreation(string gamePath) =>
        File.WriteAllText(Path.Combine(gamePath, "studio.txt"), "tfc 1\r\ntm\r\n", new UTF8Encoding(false));

    /// <summary>UnityExplorer: не выскакивать при каждом запуске игры — открывается клавишей F7.</summary>
    public static void SetupExplorer(string gamePath)
    {
        var cfg = Path.Combine(gamePath, "BepInEx", "config", "com.sinai.unityexplorer.cfg");
        if (File.Exists(cfg)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
        File.WriteAllText(cfg, "[UnityExplorer]\n\nUnityExplorer Toggle = F7\n\nHide On Startup = true\n");
    }

    /// <summary>Игра закрылась — режим разработчика выключен: плашки и клавиш ModScript Studio больше нет.</summary>
    public static void Leave(string gamePath)
    {
        try
        {
            if (File.Exists(Flag(gamePath))) File.Delete(Flag(gamePath));
            if (UnrealMod(gamePath) is { } mod && File.Exists(Path.Combine(mod, "dev.flag"))) File.Delete(Path.Combine(mod, "dev.flag"));
            if (File.Exists(Path.Combine(gamePath, "reframework", "autorun", "ModScriptStudio.lua"))) SetupRe(gamePath, false);
            LeaveMore(gamePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static bool Active(string gamePath) => File.Exists(Flag(gamePath));

    /// <summary>Убрать ModScript Studio и инструменты, которые поставил ModLaunch (свои у игрока — не трогаем).</summary>
    public static void Remove(string gamePath)
    {
        Leave(gamePath);
        if (UnrealMod(gamePath) is { } mod && Directory.Exists(mod)) Directory.Delete(mod, true);
        foreach (var f in new[] { Path.Combine(gamePath, "reframework", "autorun", "ModScriptStudio.lua"), Path.Combine(gamePath, "reframework", "data", "ModScriptStudio.json"), Path.Combine(gamePath, "studio.txt") })
            if (File.Exists(f)) File.Delete(f);
        RemoveExplorer(gamePath);
    }

    /// <summary>Убрать UnityExplorer, если его ставил ModLaunch (свой у игрока — не трогаем).</summary>
    public static void RemoveExplorer(string gamePath)
    {
        var explorer = Path.Combine(gamePath, "BepInEx", "plugins", "sinai-dev-UnityExplorer");
        if (!File.Exists(Path.Combine(explorer, ".modlaunch"))) return;
        Directory.Delete(explorer, true);
        var cfg = Path.Combine(gamePath, "BepInEx", "config", "com.sinai.unityexplorer.cfg");
        if (File.Exists(cfg)) File.Delete(cfg);
    }

    static string Resource(string name)
    {
        using var s = typeof(DevEngines).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name + " is missing from the build");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    static string Lua(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

    /// <summary>Сбалансированы ли блоки Lua (function/if/for/while/do … end), без строк и комментариев.</summary>
    static int LuaBalance(string lua)
    {
        var code = System.Text.RegularExpressions.Regex.Replace(lua, @"--\[\[[\s\S]*?\]\]|--[^\n]*|""(\\.|[^""\\])*""|'(\\.|[^'\\])*'", "");
        int Count(string word) => System.Text.RegularExpressions.Regex.Matches(code, $@"\b{word}\b").Count;
        // «for/while … do» открывают один блок словом do; function, if и одиночный do — тоже.
        return Count("function") + Count("if") + Count("do") - Count("end");
    }

    [SelfTest]
    static string EnginesAreRecognised()
    {
        var root = Path.Combine(Paths.DataDir, "engine-probe");
        try
        {
            var ueGame = Path.Combine(root, "UE");
            var ue = Directory.CreateDirectory(Path.Combine(ueGame, "MyGame", "Binaries", "Win64")).FullName;
            File.WriteAllText(Path.Combine(ue, "MyGame-Win64-Shipping.exe"), "");
            var ff = Directory.CreateDirectory(Path.Combine(root, "FF", "End", "Binaries", "Win64")).FullName;
            File.WriteAllText(Path.Combine(ff, "ff7remake_.exe"), "");
            Directory.CreateDirectory(Path.Combine(root, "FF", "End", "Content", "Paks"));
            var re = Directory.CreateDirectory(Path.Combine(root, "RE")).FullName;
            File.WriteAllText(Path.Combine(re, "re_chunk_000.pak"), "");
            var il = Directory.CreateDirectory(Path.Combine(root, "IL", "Game_Data")).FullName;
            File.WriteAllText(Path.Combine(root, "IL", "GameAssembly.dll"), "");
            var be = Directory.CreateDirectory(Path.Combine(root, "BE", "Data")).FullName;
            File.WriteAllText(Path.Combine(root, "BE", "SkyrimSE.exe"), "");
            if (KindOf(ueGame) != "unreal") throw new Exception("unreal not recognised");
            if (ShippingDir(ueGame) != ue) throw new Exception("shipping dir " + ShippingDir(ueGame));
            if (ShippingDir(Path.Combine(root, "FF")) != ff) throw new Exception("renamed Unreal exe not found");
            if (KindOf(re) != "re") throw new Exception("RE Engine not recognised");
            if (KindOf(Path.Combine(root, "IL")) != "il2cpp") throw new Exception("IL2CPP not recognised");
            if (KindOf(Path.Combine(root, "BE")) != "creation") throw new Exception("Creation Engine not recognised");
            foreach (var dir in new[] { ueGame, re, Path.Combine(root, "IL"), Path.Combine(root, "BE") })
                if (DevMode.Unsupported(dir) is { } why) throw new Exception(dir + " still unsupported: " + why);
            if (DevMode.Key(ueGame, "started") != "v23.dev.unreal.started" || DevMode.Key(re, "route") != "v23.dev.re.route") throw new Exception("string keys");
            // Тексты — только когда строки программы загружены (в --selfcheck без окна их нет).
            if (I18n.T("v23.dev.route") != "v23.dev.route")
            foreach (var kind in new[] { "unreal", "re", "il2cpp", "creation" })
                foreach (var what in new[] { "route", "inside", "started", "preparing" })
                    if (I18n.T($"v23.dev.{kind}.{what}") == $"v23.dev.{kind}.{what}") throw new Exception($"no text v23.dev.{kind}.{what}");
            var unrealTool = ToolsFor("unreal", ueGame)[0];
            if (unrealTool.Into != Path.Combine("MyGame", "Binaries", "Win64")) throw new Exception("UE4SS goes to " + unrealTool.Into);

            // Настройка UE4SS на «распакованном» UE4SS: наш мод включён перед Keybinds, Live View не выскакивает сам.
            var mods = Directory.CreateDirectory(Path.Combine(ue, "ue4ss", "Mods")).FullName;
            File.WriteAllText(Path.Combine(ue, "ue4ss", "UE4SS-settings.ini"), "[Debug]\nGuiConsoleEnabled = 0\nGuiConsoleVisible = 1\n");
            File.WriteAllText(Path.Combine(mods, "mods.txt"), "ConsoleEnablerMod : 0\nBPModLoaderMod : 1\n\n; Built-in keybinds, do not move up!\nKeybinds : 1\n");
            File.WriteAllText(Path.Combine(mods, "mods.json"), "[{\"mod_name\":\"ConsoleEnablerMod\",\"mod_enabled\":true},{\"mod_name\":\"Keybinds\",\"mod_enabled\":true}]");
            SetupUnreal(ueGame);
            var txt = File.ReadAllLines(Path.Combine(mods, "mods.txt"));
            if (!txt.Contains("ModScriptStudio : 1") || !txt.Contains("ConsoleEnablerMod : 1") || Array.IndexOf(txt, "ModScriptStudio : 1") > Array.FindIndex(txt, l => l.StartsWith("Keybinds", StringComparison.Ordinal)))
                throw new Exception("mods.txt: " + string.Join(" / ", txt));
            if (!File.ReadAllText(Path.Combine(mods, "mods.json")).Contains("ModScriptStudio")) throw new Exception("mods.json");
            var ini = File.ReadAllText(Path.Combine(ue, "ue4ss", "UE4SS-settings.ini"));
            if (!ini.Contains("GuiConsoleEnabled = 1") || !ini.Contains("GuiConsoleVisible = 0")) throw new Exception("settings: " + ini);
            var lua = File.ReadAllText(Path.Combine(mods, "ModScriptStudio", "Scripts", "main.lua"));
            if (lua.Contains("{{hint}}") || !File.Exists(Path.Combine(mods, "ModScriptStudio", "dev.flag"))) throw new Exception("UE4SS mod not written");

            SetupRe(re, true);
            var data = JsonNode.Parse(File.ReadAllText(Path.Combine(re, "reframework", "data", "ModScriptStudio.json")))!.AsObject();
            if (data["dev"]?.GetValue<bool>() != true || ReLabels.Any(k => string.IsNullOrEmpty((string?)data[k]) || ((string?)data[k] == "v23.re." + k && I18n.T("v23.dev.route") != "v23.dev.route"))) throw new Exception("RE labels");
            if (File.Exists(SystemFont()) && ((string?)data["font"] is not { } fontName || !File.Exists(Path.Combine(re, "reframework", "fonts", fontName)) || data["ranges"] is not JsonArray { Count: > 2 } ranges || (int)ranges[^1]! != 0))
                throw new Exception("RE font: " + data["font"]);
            // Шрифт Cyber Engine Tweaks: свой путь игрока не трогаем, пустой — шрифт Windows с кириллицей.
            var cet = Directory.CreateDirectory(Path.Combine(re, "bin", "x64", "plugins", "cyber_engine_tweaks")).FullName;
            File.WriteAllText(Path.Combine(cet, "config.json"), "{\"font\":{\"base_size\":18.0,\"path\":\"\"},\"patches\":{}}");
            DevCyberpunk.CetFont(re);
            var cetFont = JsonNode.Parse(File.ReadAllText(Path.Combine(cet, "config.json")))!["font"]!;
            if (I18n.Lang is "ru" or "uk" && File.Exists(SystemFont()) && ((string?)cetFont["glyph_ranges"] != "Cyrillic" || (string?)cetFont["path"] != SystemFont() || (double)cetFont["base_size"]! != 18.0)) throw new Exception("CET font: " + cetFont.ToJsonString());
            Directory.CreateDirectory(Path.GetDirectoryName(Flag(re))!);
            File.WriteAllText(Flag(re), re);
            Leave(re);
            data = JsonNode.Parse(File.ReadAllText(Path.Combine(re, "reframework", "data", "ModScriptStudio.json")))!.AsObject();
            if (data["dev"]?.GetValue<bool>() != false || Active(re)) throw new Exception("RE dev mode left on");

            foreach (var res in new[] { "studio-unreal", "studio-re" })
                if (LuaBalance(Resource(res)) != 0) throw new Exception(res + ": blocks don't balance (" + LuaBalance(Resource(res)) + ")");
            foreach (var k in ReLabels) if (!Resource("studio-re").Contains($"T(\"{k}\"")) throw new Exception("RE label unused: " + k);
            return "Unreal (UE4SS → Binaries/Win64, mod + keys), RE Engine (REFramework), Unity IL2CPP (BepInEx 6 + UnityExplorer), Creation Engine recognised";
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
            foreach (var d in new[] { "RE", "UE", "IL", "BE", "FF" }) try { File.Delete(Flag(Path.Combine(root, d))); } catch (IOException) { }
        }
    }
}
