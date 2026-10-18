namespace ModScriptX;

/// <summary>Встроенные проверки: перевод примера во все движки, установка в поддельные папки игр и снятие.</summary>
static class SelfTest
{
    public static int Run()
    {
        var failed = 0;
        void Check(string name, Action test)
        {
            try { test(); Console.WriteLine("ok   " + name); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
        }
        static void Has(string text, params string[] parts)
        {
            foreach (var p in parts) if (!text.Contains(p)) throw new Exception($"missing «{p}» in:\n{text}");
        }
        var example = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", "gold-boost.modx"));

        Check("parse", () =>
        {
            var d = ModxDoc.Parse(example);
            if (!d.Ok) throw new Exception(string.Join("; ", d.Diags.Select(x => x.Line + " " + x.Message)));
            if (d.Id != "Gold_Boost" || d.Hooks.Count != 2 || d.Natives.Count != 2) throw new Exception($"{d.Id} {d.Hooks.Count} {d.Natives.Count}");
            if (d.Shared.Single(s => s.Name == "start_gold").Value!.ToJsonString() != "500") throw new Exception("start_gold");
        });
        Check("errors", () =>
        {
            var d = ModxDoc.Parse("mod \"X\"\nhook boom {\n}\nhook start {\n  jump x\n");
            if (d.Ok || d.Diags.Count < 3) throw new Exception(string.Join("; ", d.Diags.Select(x => x.Line + " " + x.Message)));
        });
        Check("expressions", () =>
        {
            Has(Lang.For("lua").Expr("a != 1 and not b"), "a ~= 1 and not b");
            Has(Lang.For("javascript").Expr("a == null or true"), "a === null || true");
            Has(Lang.For("python").Expr("\"hp {hp * 2}!\""), "\"hp {}!\".format(hp * 2)");
            Has(Lang.For("csharp").Expr("\"{a} {{x}}\""), "$\"{a} {{x}}\"");
            Has(Lang.For("gdscript").Expr("\"{a}%\""), "\"%s%%\" % [a]");
        });

        var root = Path.Combine(Path.GetTempPath(), "modscript-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        string Game(string name, params string[] files)
        {
            var dir = Path.Combine(root, name);
            foreach (var f in files)
            {
                var p = Path.Combine(dir, f);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, f.EndsWith("plugins.js") ? "var $plugins =\n[\n{\"name\":\"Core\",\"status\":true,\"description\":\"\",\"parameters\":{}}\n];\n" : "");
            }
            return dir;
        }
        try
        {
            Check("renpy", () =>
            {
                var g = Game("renpy", "game/script.rpy");
                var r = Injector.Inject(example, g, null);
                var text = File.ReadAllText(Path.Combine(g, r.Files.Single()));
                Has(text, "init 999 python:", "start_gold = 500", "global gold", "config.start_callbacks.append(_modx_Gold_Boost_start)",
                    "renpy.notify(\"Gold Boost: you start with {} gold\".format(start_gold))", "        if gold < bonus:", "            gold += bonus", "    gold = 0");
            });
            Check("rpgmaker", () =>
            {
                var g = Game("rpg", "www/js/plugins.js");
                var r = Injector.Inject(example, g, null);
                Has(File.ReadAllText(Path.Combine(g, "www/js/plugins/ModScript_Gold_Boost.js")), "let start_gold = 500;", "DataManager.setupNewGame = function()", "$gameMessage.add(String(`Gold Boost: you start with ${start_gold} gold`));", "console.log(\"Gold Boost loaded");
                Has(File.ReadAllText(Path.Combine(g, "www/js/plugins.js")), "},\n{\"name\":\"ModScript_Gold_Boost\",\"status\":true");
                Injector.Eject("Gold_Boost", g);
                var after = File.ReadAllText(Path.Combine(g, "www/js/plugins.js"));
                if (after.Contains("ModScript_") || after.Contains("},\n]") || File.Exists(Path.Combine(g, "www/js/plugins/ModScript_Gold_Boost.js"))) throw new Exception("eject left: " + after);
            });
            Check("unity", () =>
            {
                var g = Game("unity", "Game_Data/Managed/Assembly-CSharp.dll");
                var r = Injector.Build(example, g, "unity");
                Has(File.ReadAllText(Path.Combine(g, r.Files[0])), "public static long start_gold = 500;", "Logger.LogMessage($\"Gold Boost: you start with {start_gold} gold\");", "if (gold < bonus) {");
            });
            Check("godot", () =>
            {
                var g = Game("godot", "game.pck");
                var r = Injector.Inject(example, g, null);
                using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(g, r.Files.Single()));
                using var s = new StreamReader(zip.GetEntry("mods-unpacked/ModLaunch-Gold_Boost/mod_main.gd")!.Open());
                Has(s.ReadToEnd(), "extends Node", "func _ready():", "\tprint(\"Gold Boost: you start with %s gold\" % [start_gold])");
                if (!r.Doc.Diags.Any(d => d.Warning && d.Message.Contains("'load'"))) throw new Exception("no warning for load");
            });
            Check("lua", () =>
            {
                var g = Game("lua", "main.lua");
                var r = Injector.Inject(example, g, null);
                Has(File.ReadAllText(Path.Combine(g, r.Files.Single())), "start_gold = 500", "say((\"Gold Boost: you start with \" .. tostring(start_gold) .. \" gold\"))", "gold = gold + (bonus)", "return M");
            });
            Check("anti-cheat refused", () =>
            {
                var g = Game("online", "EasyAntiCheat/settings.json", "main.lua");
                var r = Injector.Inject(example, g, null);
                if (r.Doc.Ok || r.Files.Count > 0) throw new Exception("injected into a protected game");
            });
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
        Console.WriteLine(failed == 0 ? "all checks passed" : $"{failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
