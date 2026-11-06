using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 23.7: режим разработчика для ещё трёх движков — всё родное, без загрузки чужих программ:
/// · Ren'Py (тысячи визуальных новелл) — config.developer и config.console: Shift+D — меню разработчика (переменные,
///   картинки, интерактивный режиссёр D — ставить сцены и картинки прямо в игре), Shift+O — консоль, Shift+R — перезагрузка;
/// · RPG Maker MV/MZ — тестовая игра: F9 — переключатели и переменные, Ctrl — сквозь стены, F8 — консоль;
/// · Source (Half-Life 2, Portal, Garry's Mod…) — консоль (~), читы на своём сервере, F6 — полёт сквозь стены, F7 — замедление.
/// Всё, что меняем в файлах игры, возвращается как было, когда игра закрывается (Leave).
/// </summary>
public static partial class DevEngines
{
    const string Mark = "ModScriptStudio";

    /// <summary>Папка js игры на RPG Maker: www/js (MV) или js (MV без www и MZ).</summary>
    public static string? RpgMakerJs(string gamePath)
    {
        foreach (var dir in new[] { Path.Combine(gamePath, "www", "js"), Path.Combine(gamePath, "js") })
            if (File.Exists(Path.Combine(dir, "plugins.js")) && (File.Exists(Path.Combine(dir, "rpg_core.js")) || File.Exists(Path.Combine(dir, "rmmz_core.js")))) return dir;
        return null;
    }

    /// <summary>Папка мода Source 1 (с gameinfo.txt) рядом с bin/engine.dll; Source 2 (сетевые игры с VAC) — нет.</summary>
    public static string? SourceMod(string gamePath)
    {
        if (!File.Exists(Path.Combine(gamePath, "bin", "engine.dll"))) return null;
        try
        {
            return Directory.EnumerateDirectories(gamePath).FirstOrDefault(d => File.Exists(Path.Combine(d, "gameinfo.txt")) && Path.GetFileName(d) is not ("platform" or "bin" or "sdktools"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static string RenPyFile(string gamePath) => Path.Combine(gamePath, "game", "zz_modscript_studio.rpy");

    /// <summary>Ren'Py: свой файл в game/ — включает разработчика и консоль и показывает плашку ModScript Studio.</summary>
    static void SetupRenPy(string gamePath)
    {
        // Фигурные скобки в тексте Ren'Py — теги, поэтому знак { } пишется удвоенными скобками.
        // Шрифт Ren'Py по умолчанию (DejaVu) не знает иероглифов — для китайского подсказка по-английски.
        var hint = I18n.Lang == "zh-CN" ? "Shift+D — developer menu · Shift+O — console · D — director" : I18n.T("v23.rpy.hint");
        var profile = GameProfile.Of(gamePath);
        var accent = profile?.Accent ?? "#F0B429";
        var title = profile is null ? "ModScript Studio" : "ModScript Studio · " + GameProfile.Clean(profile.Name).Replace("\"", "'").Replace("{", "{{").Replace("[", "[[");
        var text = new StringBuilder()
            .AppendLine("# ModScript Studio (ModLaunch): режим разработчика Ren'Py. Файл убирается, когда игра закрывается.")
            .AppendLine("init 999 python:")
            .AppendLine("    config.developer = True")
            .AppendLine("    config.console = True")
            .AppendLine("    if \"modscript_studio_badge\" not in config.overlay_screens:")
            .AppendLine("        config.overlay_screens.append(\"modscript_studio_badge\")")
            .AppendLine()
            .AppendLine("screen modscript_studio_badge():")
            .AppendLine("    zorder 999")
            .AppendLine("    frame:")
            .AppendLine("        xalign 1.0")
            .AppendLine("        yalign 0.0")
            .AppendLine("        xoffset -16")
            .AppendLine("        yoffset 16")
            .AppendLine("        background Solid(\"#0d0e12e0\")")
            .AppendLine("        padding (16, 10)")
            .AppendLine("        vbox:")
            .AppendLine("            text \"{color=" + accent + "}{b}{{ }}{/b}{/color}  {b}" + title + "{/b}\" size 20")
            .AppendLine("            text \"" + hint.Replace("\"", "'").Replace("{", "{{").Replace("[", "[[") + "\" size 14 color \"#9aa3b2\"");
        File.WriteAllText(RenPyFile(gamePath), text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>RPG Maker: свой плагин в начале plugins.js (прежний файл — в копию) — тестовая игра и плашка.</summary>
    static void SetupRpgMaker(string gamePath)
    {
        var js = RpgMakerJs(gamePath)!;
        var plugins = Path.Combine(js, "plugins.js");
        var backup = plugins + ".modscript.bak";
        if (!File.Exists(backup)) File.Copy(plugins, backup);
        var original = File.ReadAllText(backup);
        var at = original.IndexOf('[');
        if (at < 0) throw new InvalidDataException("plugins.js: no plugin list");
        var entry = "\n{\"name\":\"" + Mark + "\",\"status\":true,\"description\":\"ModScript Studio (ModLaunch)\",\"parameters\":{}},";
        File.WriteAllText(plugins, original[..(at + 1)] + entry + original[(at + 1)..], new UTF8Encoding(false));
        var hint = I18n.T("v23.rpgm.hint").Replace("\\", "\\\\").Replace("'", "\\'");
        var code = "// ModScript Studio (ModLaunch): режим разработчика RPG Maker — тестовая игра. Убирается, когда игра закрывается.\n"
            + "(function () {\n"
            + "  var valid = Utils.isOptionValid;\n"
            + "  Utils.isOptionValid = function (name) { return name === 'test' || valid.call(this, name); };\n"
            + "  var start = Scene_Map.prototype.start;\n"
            + "  Scene_Map.prototype.start = function () {\n"
            + "    start.call(this);\n"
            + "    var w = 420, h = 64, bmp = new Bitmap(w, h);\n"
            + "    bmp.fillRect(0, 0, w, h, 'rgba(13,14,18,0.88)');\n"
            + "    bmp.fontSize = 20; bmp.textColor = '" + (GameProfile.Of(gamePath)?.Accent ?? "#F0B429") + "'; bmp.drawText('{ }', 14, 6, 40, 30, 'left');\n"
            + "    bmp.textColor = '#ffffff'; bmp.drawText('ModScript Studio', 52, 6, 300, 30, 'left');\n"
            + "    bmp.fontSize = 14; bmp.textColor = '#9aa3b2'; bmp.drawText('" + hint + "', 14, 34, w - 28, 24, 'left');\n"
            + "    var badge = new Sprite(bmp);\n"
            + "    badge.x = Graphics.width - w - 12; badge.y = 12; badge.opacity = 230;\n"
            + "    this.addChild(badge);\n"
            + "  };\n"
            + "})();\n";
        File.WriteAllText(Path.Combine(js, "plugins", Mark + ".js"), code, new UTF8Encoding(false));
    }

    static void LeaveRpgMaker(string gamePath)
    {
        var js = RpgMakerJs(gamePath);
        if (js is null) return;
        var plugins = Path.Combine(js, "plugins.js");
        var backup = plugins + ".modscript.bak";
        if (File.Exists(backup)) { File.Copy(backup, plugins, true); File.Delete(backup); }
        var file = Path.Combine(js, "plugins", Mark + ".js");
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>Source: autoexec.cfg мода — консоль, читы на своём сервере, клавиши полёта и замедления (прежний файл — в копию).</summary>
    static void SetupSource(string gamePath)
    {
        var cfg = Directory.CreateDirectory(Path.Combine(SourceMod(gamePath)!, "cfg")).FullName;
        var file = Path.Combine(cfg, "autoexec.cfg");
        var backup = file + ".modscript.bak";
        if (!File.Exists(backup)) File.WriteAllText(backup, File.Exists(file) ? File.ReadAllText(file) : "");
        var text = File.ReadAllText(backup).TrimEnd() + "\n\n// ModScript Studio (ModLaunch): режим разработчика. Убирается, когда игра закрывается.\n"
            + "con_enable 1\ndeveloper 1\nsv_cheats 1\nbind \"F6\" \"noclip\"\nbind \"F7\" \"incrementvar host_timescale 0.25 1 0.75\"\n"
            + "echo \"" + I18n.T("v23.src.hint").Replace("\"", "'") + "\"\n";
        File.WriteAllText(file, text, new UTF8Encoding(false));
    }

    static void LeaveSource(string gamePath)
    {
        var mod = SourceMod(gamePath);
        if (mod is null) return;
        var file = Path.Combine(mod, "cfg", "autoexec.cfg");
        var backup = file + ".modscript.bak";
        if (!File.Exists(backup)) return;
        var original = File.ReadAllText(backup);
        if (original == "") File.Delete(file); else File.WriteAllText(file, original);
        File.Delete(backup);
    }

    static void LeaveRenPy(string gamePath)
    {
        foreach (var f in new[] { RenPyFile(gamePath), RenPyFile(gamePath) + "c" })
            if (File.Exists(f)) File.Delete(f);
    }

    /// <summary>Вернуть файлы движка как были (Ren'Py, RPG Maker, Source).</summary>
    static void LeaveMore(string gamePath)
    {
        LeaveRenPy(gamePath);
        LeaveRpgMaker(gamePath);
        LeaveSource(gamePath);
    }

    [SelfTest]
    static string MoreEnginesRoundTrip()
    {
        var root = Path.Combine(Paths.DataDir, "engine-probe-more");
        try
        {
            var rpy = Directory.CreateDirectory(Path.Combine(root, "RenPy")).FullName;
            Directory.CreateDirectory(Path.Combine(rpy, "renpy"));
            Directory.CreateDirectory(Path.Combine(rpy, "game"));
            var mv = Directory.CreateDirectory(Path.Combine(root, "MV", "www", "js", "plugins")).FullName;
            var mvJs = Path.GetDirectoryName(mv)!;
            File.WriteAllText(Path.Combine(mvJs, "rpg_core.js"), "");
            const string pluginsText = "// Generated by RPG Maker.\nvar $plugins =\n[\n{\"name\":\"Community_Basic\",\"status\":true,\"description\":\"\",\"parameters\":{}}\n];\n";
            File.WriteAllText(Path.Combine(mvJs, "plugins.js"), pluginsText);
            var src = Directory.CreateDirectory(Path.Combine(root, "HL2", "bin")).FullName;
            File.WriteAllText(Path.Combine(src, "engine.dll"), "");
            var mod = Directory.CreateDirectory(Path.Combine(root, "HL2", "hl2", "cfg")).FullName;
            File.WriteAllText(Path.Combine(root, "HL2", "hl2", "gameinfo.txt"), "");
            File.WriteAllText(Path.Combine(mod, "autoexec.cfg"), "fps_max 144");
            string Dir(string n) => Path.Combine(root, n);
            if (KindOf(rpy) != "renpy" || KindOf(Dir("MV")) != "rpgmaker" || KindOf(Dir("HL2")) != "source") throw new Exception("kinds: " + KindOf(rpy) + " " + KindOf(Dir("MV")) + " " + KindOf(Dir("HL2")));
            foreach (var d in new[] { rpy, Dir("MV"), Dir("HL2") })
            {
                if (DevMode.Unsupported(d) is { } why) throw new Exception(d + ": " + why);
                Prepare(KindOf(d)!, d, null, default).GetAwaiter().GetResult();
            }
            var rpyText = File.ReadAllText(RenPyFile(rpy));
            if (!rpyText.Contains("config.developer = True") || !rpyText.Contains("{{ }}")) throw new Exception("renpy file");
            var plugins = File.ReadAllText(Path.Combine(mvJs, "plugins.js"));
            if (!plugins.Contains("\"name\":\"ModScriptStudio\"") || !plugins.Contains("Community_Basic") || !File.Exists(Path.Combine(mv, "ModScriptStudio.js"))) throw new Exception("rpg maker plugin");
            var auto = File.ReadAllText(Path.Combine(mod, "autoexec.cfg"));
            if (!auto.StartsWith("fps_max 144") || !auto.Contains("bind \"F6\" \"noclip\"")) throw new Exception("source autoexec");
            foreach (var d in new[] { rpy, Dir("MV"), Dir("HL2") }) Leave(d);
            if (File.Exists(RenPyFile(rpy))) throw new Exception("renpy file left");
            if (File.ReadAllText(Path.Combine(mvJs, "plugins.js")) != pluginsText || File.Exists(Path.Combine(mv, "ModScriptStudio.js"))) throw new Exception("rpg maker not restored");
            if (File.ReadAllText(Path.Combine(mod, "autoexec.cfg")) != "fps_max 144") throw new Exception("autoexec not restored");
            return "Ren'Py (developer + console), RPG Maker (test play plugin), Source (autoexec) — set up and restored";
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
            foreach (var d in new[] { "RenPy", "MV", "HL2" }) try { File.Delete(Flag(Path.Combine(root, d))); } catch (IOException) { }
        }
    }
}
