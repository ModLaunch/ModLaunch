using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Creator;

/// <summary>
/// 13.0: справочник команд ModScript для раздела «ModScript → Команды»: группа, имя и пометка «новое в 13.0».
/// Образец строки — ключ cr.doc.&lt;имя&gt;.syntax, описание — cr.doc.&lt;имя&gt; (их же берут подсказки редактора).
/// </summary>
public static class ModScriptCommands
{
    public sealed record Cmd(string Group, string Name, bool New);

    public static readonly string[] Groups = ["meta", "logic", "vars", "stardew", "world", "files"];

    static Cmd[] Make(string group, string old, string fresh) =>
        [.. old.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => new Cmd(group, n, false)),
         .. fresh.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => new Cmd(group, n, true))];

    public static readonly Cmd[] All =
    [
        .. Make("meta", "mod version author about game icon needs", "id tags license update_key min_api changelog readme credits optional"),
        .. Make("logic", "let if else for while when def return break import expr listcomp method", "repeat switch try"),
        .. Make("vars", "print", "inc dec push pop put del swap debug assert warn error stop"),
        .. Make("stardew", "edit entry dialogue mail image", "remove recipe craft gift quest event schedule trigger item shop option token"),
        .. Make("world", "", "map patch_image patch_map warp sound include"),
        .. Make("files", "config ini copy write json", "append mkdir jsonset"),
    ];

    public static int NewCount => All.Count(c => c.New);

    public static string Syntax(Cmd c) => I18n.T($"cr.doc.{c.Name}.syntax");
    public static string Doc(Cmd c) => I18n.T($"cr.doc.{c.Name}");

    /// <summary>Строки поиска: имя, образец и описание.</summary>
    public static bool Matches(Cmd c, string query) =>
        query == "" || c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || Syntax(c).Contains(query, StringComparison.OrdinalIgnoreCase)
        || Doc(c).Contains(query, StringComparison.OrdinalIgnoreCase);

    [SelfTest]
    static string ModScript13CommandsCompile()
    {
        const string src = """
            mod "Тест 13"
            version 1.0.0
            game stardew-valley
            id "Me.Test13"
            tags QoL farming qol
            license "MIT"
            update_key "Nexus:1234"
            min_api 4.0.0
            optional "spacechase0.JsonAssets"
            changelog "1.0.0" "Первый выпуск"
            option Prices = normal cheap normal
            token Bonus = 2
            edit "Data/Objects" "472" Price = $Prices
            remove "Data/Objects" "999"
            recipe "Пирог" = "296 1/1 10/236/default"
            trigger "Me_Bonus" "DayStarted" = "AddMoney 500" "AddMail Current Hi"
            item "Me_Gem" name "Камень" price 500 category -2
            shop "SeedShop" sells "(O)472" price 20
            warp "Farm" 64 15 "Town" 0 54
            jsonset "data/cfg.json" "a.b" = 5
            append "log.txt" = "раз"
            append "log.txt" = "два"
            let n = 0
            repeat 3 {
              inc n 2
            }
            switch $n {
              case 5 {
                print "пять"
              }
              case 6 {
                let hit = yes
              }
              default {
                print "нет"
              }
            }
            push list = 1
            push list "два"
            pop list into last
            put costs "472" = 20
            swap n last
            try {
              edit "Data/Objects"
            }
            catch {
              print "поймали $error"
            }
            assert $last == 6 "last должен быть 6"
            warn "проверка"
            stop
            print "не выполнится"
            """;
        var b = ModScript.Compile(src);
        var errors = b.Diags.Where(x => !x.Warning).Select(x => $"{x.Line}:{x.Message}").ToList();
        if (errors.Count > 0) throw new Exception(string.Join("; ", errors));
        if (b.UniqueIdOverride != "Me.Test13" || Projects.UniqueId(b) != "Me.Test13") throw new Exception("id");
        if (string.Join(",", b.Tags) != "qol,farming" || b.License != "MIT" || b.UpdateKeys is not ["Nexus:1234"] || b.MinApi != "4.0.0") throw new Exception("meta");
        if (b.Options is not [("Prices", var schema)] || schema["AllowValues"]?.ToString() != "cheap, normal" && schema["AllowValues"]?.ToString() != "normal, cheap") throw new Exception("option");
        var price = b.Changes[0]["Fields"]?["472"]?["Price"]?.ToString();
        if (price != "{{Prices}}") throw new Exception("option token in edit: " + price);
        if (b.Tokens.Count != 1 || b.Changes[1]["Entries"]?["999"] is not null || !((JsonObject)b.Changes[1]["Entries"]!).ContainsKey("999")) throw new Exception("token/remove");
        if (b.Changes.FirstOrDefault(c => c["Target"]?.ToString() == "Data/TriggerActions")?["Entries"]?["Me_Bonus"]?["Actions"] is not JsonArray { Count: 2 }) throw new Exception("trigger");
        if (b.Changes.FirstOrDefault(c => c["Target"]?.ToString() == "Data/Objects" && c["Entries"]?["Me_Gem"] is not null)?["Entries"]?["Me_Gem"]?["Price"]?.GetValue<int>() != 500) throw new Exception("item");
        if (b.Changes.FirstOrDefault(c => c["Target"]?.ToString() == "Data/Shops")?["TargetField"] is not JsonArray { Count: 2 }) throw new Exception("shop");
        if (b.Changes.FirstOrDefault(c => c["Action"]?.ToString() == "EditMap")?["AddWarps"]?[0]?.ToString() != "64 15 Town 0 54") throw new Exception("warp");
        if (b.Writes.FirstOrDefault(w => w.Path == "data/cfg.json").Text?.Replace(" ", "").Replace("\n", "").Replace("\r", "") != "{\"a\":{\"b\":5}}") throw new Exception("jsonset");
        if (b.Writes.FirstOrDefault(w => w.Path == "log.txt").Text != "раз\nдва\n") throw new Exception("append");
        if (b.Vars.GetValueOrDefault("hit") != "yes" || b.Vars.GetValueOrDefault("n") != "два" || b.Vars.GetValueOrDefault("last") != "6") throw new Exception("repeat/switch/pop/swap: " + string.Join(" ", b.Vars));
        if (b.Vars.GetValueOrDefault("costs") != "{\"472\":20}") throw new Exception("put: " + b.Vars.GetValueOrDefault("costs"));
        if (!b.Log.Any(l => l.Contains("поймали"))) throw new Exception("try/catch");
        if (b.Log.Any(l => l.Contains("не выполнится")) || !b.Diags.Any(x => x.Warning && x.Message == "err.user|проверка")) throw new Exception("stop/warn");
        // assert срабатывает, error — ошибка сборки, case вне switch — ошибка.
        var bad = ModScript.Compile("mod \"x\"\ngame stardew-valley\nassert 1 > 2 \"нет\"\nerror \"стоп\"\ncase 1 {\n}");
        if (bad.Diags.Count(x => !x.Warning) != 3) throw new Exception("assert/error/case: " + string.Join("; ", bad.Diags.Select(x => x.Message)));
        // Имена новых команд не отняли у переменных обычное присваивание.
        var vars = ModScript.Compile("mod \"x\"\ngame stardew-valley\ntext = \"hi\"\nitem = 2\nprint \"$text $item\"");
        if (!vars.Ok || vars.Log is not [var said] || !said.EndsWith("hi 2")) throw new Exception("assignment to command names");
        if (All.Select(c => c.Name).Distinct().Count() != All.Length || All.Any(c => c.New && !ModScript.Commands13.Contains(c.Name))) throw new Exception("catalog");
        return $"{NewCount} new commands compile into Content Patcher/package data";
    }
}
