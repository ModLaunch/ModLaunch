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

        var arena = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", "arena.modx"));
        Check("language 2.0: functions, loops, else if", () =>
        {
            var d = ModxDoc.Parse(arena);
            if (!d.Ok) throw new Exception(string.Join("; ", d.Diags.Select(x => x.Line + " " + x.Message)));
            if (d.Functions.Count != 1 || d.Functions[0].Params.Single() != "wave") throw new Exception("fn reward");
            var kinds = string.Join(" ", d.Hooks.SelectMany(h => h.Body).Select(s => s.Kind));
            Has(kinds, "let for add end set say", "while add if continue end add end if set log set end");
        });
        Check("sandbox runs the language", () =>
        {
            var r = Sandbox.Run(ModxDoc.Parse(arena), new Dictionary<string, object?> { ["gold"] = 100d, ["streak"] = 0d }, ticks: 3);
            if (!r.Ok) throw new Exception(string.Join("; ", r.Errors.Select(e => e.Line + " " + e.Message)));
            // 50 + (100 + 10) + (150 + 25) = 335; три тика по 2 = streak 6 → золото ×2.
            if (Sandbox.Text(r.Vars["gold"]) != "870" || Sandbox.Text(r.Vars["streak"]) != "0") throw new Exception($"gold {Sandbox.Text(r.Vars["gold"])}, streak {Sandbox.Text(r.Vars["streak"])}");
            Has(string.Join("\n", r.Output), "[start] 💬 Arena: +335 gold for 3 waves", "[tick 3] lucky streak: gold doubled to 870");
        });
        Check("sandbox stops endless loops", () =>
        {
            var r = Sandbox.Run(ModxDoc.Parse("mod \"Loop\"\nhook start {\n  while true {\n    add x 1\n  }\n}\n"), maxSteps: 5000);
            if (r.Ok || !r.Errors.Any(e => e.Message.Contains("endless"))) throw new Exception("no step limit");
        });
        Check("sandbox: a list inside itself, deep nesting", () =>
        {
            var r = Sandbox.Run(ModxDoc.Parse("mod \"C\"\nlet items = [0]\nhook start {\n  items[0] = items\n  say \"{items}\"\n}\n"));
            if (!r.Ok || Sandbox.Show(r.Vars["items"]) != "[[…]]") throw new Exception(string.Join("; ", r.Errors.Select(e => e.Message)));
            Has(string.Join("\n", r.Output), "[start] 💬 [[…]]");
            foreach (var deep in new[] { new string('(', 20000) + "1" + new string(')', 20000), string.Concat(Enumerable.Repeat("not ", 20000)) + "1", string.Concat(Enumerable.Repeat("- ", 20000)) + "1" })
            {
                var d = Sandbox.Run(ModxDoc.Parse($"mod \"D\"\nhook start {{\n  let x = {deep}\n}}\n"));
                if (!d.Errors.Any(e => e.Message.Contains("nested too deep"))) throw new Exception("no nesting limit for " + deep[..10]);
            }
        });
        Check("sandbox bounds work and memory", () =>
        {
            var loops = Sandbox.Run(ModxDoc.Parse("mod \"W\"\nhook start {\n  let big = range(100000)\n  for a in big {\n    for b in big {\n    }\n  }\n}\n"));
            if (!loops.Errors.Any(e => e.Message.Contains("endless"))) throw new Exception("for loops are not counted");
            var grow = Sandbox.Run(ModxDoc.Parse("mod \"W\"\nhook start {\n  let s = \"xx\"\n  while true {\n    s = s + s\n  }\n}\n"));
            if (!grow.Errors.Any(e => e.Message.Contains("too long"))) throw new Exception("text grows without a limit");
        });
        Check("sandbox reports errors instead of throwing", () =>
        {
            foreach (var bad in new[] { "let v = 1.2.3", "log rand(5, 1)", "log rand(1, len([]) - 1)" })
                if (Sandbox.Run(ModxDoc.Parse($"mod \"E\"\nhook start {{\n  {bad}\n}}\n")).Ok) throw new Exception("no error for " + bad);
            var errors = new List<ModLaunch.Creator.Diag>();
            Sandbox.ParseVars("gold = 1.2.3", errors);
            if (errors.Count != 1) throw new Exception("gold = 1.2.3");
        });
        Check("and/or stop early, items[i] += x", () =>
        {
            var r = Sandbox.Run(ModxDoc.Parse("mod \"S\"\nlet items = [5, 6]\nlet none = []\nhook start {\n  if len(none) > 0 and none[0] == 5 {\n    say \"no\"\n  }\n" +
                "  if true or boom() {\n    items[0] += 1\n    add items[1] 2\n  }\n}\n"));
            if (!r.Ok) throw new Exception(string.Join("; ", r.Errors.Select(e => e.Message)));
            if (Sandbox.Text(r.Vars["items"]) != "[6, 8]" || r.Vars.ContainsKey("items[0]") || r.Output.Any(o => o.Contains("boom"))) throw new Exception($"items {Sandbox.Text(r.Vars["items"])}; {string.Join("; ", r.Output)}");
        });
        Check("else after else is an error", () =>
        {
            foreach (var tail in new[] { "} else {", "} else if gold > 2 {" })
                if (ModxDoc.Parse($"mod \"E\"\nhook start {{\n  if gold > 1 {{\n  }} else {{\n  {tail}\n  }}\n}}\n").Ok) throw new Exception("accepted: " + tail);
        });
        Check("translation 2.0", () =>
        {
            var d = ModxDoc.Parse(arena);
            string Emit(string t) => string.Concat(Target.Find(t)!.Emit(ModxDoc.Parse(arena), null).Select(f => f.Bytes is null ? f.Text : ""));
            Has(Emit("renpy"), "def reward(wave):", "    pay = base_pay * wave", "    elif wave == 2:", "    return mx_clamp(pay, 0, 500)", "def mx_clamp(x, lo, hi)", "    for w in waves:", "            continue");
            Has(Emit("rpgmaker"), "function reward(wave) {", "let pay;", "pay = base_pay * wave;", "} else if (wave === 2) {", "for (const w of waves) {", "const mx_len = x =>");
            Has(Emit("unity"), "dynamic reward(dynamic wave)", "dynamic pay = null;", "pay = base_pay * wave;", "foreach (var w in waves) {", "static dynamic mx_clamp(");
            var lua = Emit("lua");
            Has(lua, "local reward", "reward = function(wave)", "local pay\n", "pay = base_pay * wave", "elseif wave == 2 then", "do return mx_clamp(pay, 0, 500) end", "for _, w in ipairs(waves) do", "goto mx_continue_1", "::mx_continue_1::");
            if (d.Functions.Count != 1) throw new Exception("fn");
            // Пример из комплекта собирается в каждом движке: ни одного ключевого слова языка в именах.
            foreach (var t in Target.All)
            {
                var x = ModxDoc.Parse(arena);
                t.Emit(x, null);
                if (!x.Ok) throw new Exception(t.Id + ": " + string.Join("; ", x.Diags.Select(e => e.Message)));
            }
        });
        Check("let is declared once per function, bare return in C#", () =>
        {
            var src = "mod \"L\"\nfn check(x) {\n  if x < 0 {\n    return\n  }\n  return x\n}\nhook start {\n  if gold > 1 {\n    let bonus = 5\n  } else {\n    let bonus = 1\n  }\n" +
                "  gold += bonus\n  let msg = \"a\"\n  let msg = \"b\"\n}\nhook tick {\n  let n = 1\n}\nhook tick {\n  let n = 2\n}\n";
            string Emit(string t) => string.Concat(Target.Find(t)!.Emit(ModxDoc.Parse(src), null).Select(f => f.Bytes is null ? f.Text : ""));
            var js = Emit("rpgmaker");
            Has(js, "let bonus, msg;", "      bonus = 5;", "msg = \"b\";");
            if (js.Contains("let bonus =") || js.Contains("let msg =")) throw new Exception(js);
            Has(Emit("unity"), "dynamic bonus = null, msg = null;", "if (x < 0) {\n            return null;", "dynamic n = null;\n        n = 1;\n        n = 2;");
            Has(Emit("lua"), "local bonus, msg\n");
        });
        Check("reserved words of the engine language", () =>
        {
            ModxDoc For(string t)
            {
                var d = ModxDoc.Parse("mod \"R\"\nlet base = 1\nhook start {\n  let end = 2\n}\n");
                Target.Find(t)!.Emit(d, null);
                return d;
            }
            if (For("unity").Ok || For("lua").Ok) throw new Exception("base or end accepted");
            if (!For("renpy").Ok || !For("rpgmaker").Ok) throw new Exception("base and end are fine in Python and JavaScript");
        });
        Check("metadata stays inside comments", () =>
        {
            var d = ModxDoc.Parse("mod \"A */ B -- C\"\nauthor \"x\\ny\"\nabout \"evil */ code\"\nhook start {\n  log 1\n}\n");
            var js = Target.Find("rpgmaker")!.Emit(d, null)[0].Text;
            if (js.Contains("A */") || js.Contains("evil */") || js.Split('\n').Any(l => l.StartsWith('y'))) throw new Exception(js);
            Has(js, "@author x y");
            var comment = Target.Find("unity")!.Emit(d, null)[1].Text.Split('\n')[1].Trim();
            if (!comment.StartsWith("<!--") || comment[4..^3].Contains("--")) throw new Exception(comment);
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
                // Ren'Py сам компилирует .rpy в .rpyc и грузит его и без .rpy: eject снимает оба.
                File.WriteAllText(Path.Combine(g, "game", "modscript_Gold_Boost.rpyc"), "");
                Injector.Eject("Gold_Boost", g);
                if (Directory.EnumerateFiles(Path.Combine(g, "game"), "modscript_*").Any()) throw new Exception("eject left the compiled .rpyc");
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
                Has(File.ReadAllText(Path.Combine(g, r.Files[1])), "<OutputPath>$(GameDir)/BepInEx/plugins/ModScript_Gold_Boost/</OutputPath>");
                // Что оставляет dotnet build: папка плагина и obj/ с ещё одной dll — снимаются целиком, чужое остаётся.
                var built = Game("unity", "BepInEx/plugins/ModScript_Gold_Boost/ModScript_Gold_Boost.dll", "BepInEx/plugins/ModScript_Gold_Boost/Microsoft.CSharp.dll",
                    "BepInEx/modscript/Gold_Boost/obj/Release/ModScript_Gold_Boost.dll", "BepInEx/plugins/Other.dll");
                Target.Find("unity")!.AfterEject("Gold_Boost", built);
                var left = Directory.EnumerateFiles(Path.Combine(built, "BepInEx"), "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(built, f).Replace('\\', '/')).ToList();
                if (left.Count != 1 || left[0] != "BepInEx/plugins/Other.dll") throw new Exception("left: " + string.Join(", ", left));
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
