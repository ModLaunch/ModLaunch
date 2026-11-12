using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Creator;

/// <summary>
/// 26.0: «Ремикс в ModScript» — своя копия скрипта из ModLaunch Hub. Получается обычный проект правок файлов (main.mls)
/// с кодом мода; первыми строками — комментарий, чей это мод (название, автор, id в Hub и ссылка), строка mod
/// переименована в «… (ремикс)», author — сам человек. Откуда проект, записано и в project.json (forkOf). Всё
/// локально: Hub и правила базы не меняются, а выложить ремикс можно как любой свой мод.
/// </summary>
public static partial class Remix
{
    [GeneratedRegex(@"^([ \t]*)mod[ \t]+""(?:[^""\\]|\\.)*""", RegexOptions.Multiline)]
    private static partial Regex ModLine();
    [GeneratedRegex(@"^([ \t]*)author[ \t]+""(?:[^""\\]|\\.)*""", RegexOptions.Multiline)]
    private static partial Regex AuthorLine();

    /// <summary>Есть что ремиксовать: скрипт ModScript с исходником (у готовых архивов кода нет).</summary>
    public static bool Can(HubMod? m) => m is { IsPackage: false } && m.Code.Trim() != "";

    /// <summary>Одна строка без управляющих символов: название из Hub не должно вписать в комментарий свою строку кода.</summary>
    static string Line(string s) => Hub.Clip(s, 120);

    /// <summary>Название ремикса: «Имя (ремикс)».</summary>
    public static string Name(HubMod m) => I18n.T("v26.features.remix.name", ("name", Line(m.Name)));

    /// <summary>Код ремикса: шапка с указанием оригинала, затем код мода с новым именем и автором.</summary>
    public static string Code(HubMod m, string author)
    {
        var header = string.Join("\n",
            "# " + I18n.T("v26.features.remix.header", ("name", Line(m.Name)), ("author", Line(m.Author))),
            "# " + I18n.T("v26.features.remix.source", ("url", CreatorStats.ShareUrl(m.Id))),
            "# " + I18n.T("v26.features.remix.keep"));
        var name = Templates.Text(Name(m));
        var code = m.Code.Replace("\r\n", "\n").TrimStart('﻿');
        // Через функцию, а не строку замены: в имени может быть «$1».
        code = ModLine().IsMatch(code) ? ModLine().Replace(code, x => $"{x.Groups[1].Value}mod \"{name}\"", 1) : $"mod \"{name}\"\n" + code;
        if (author.Trim() != "") code = AuthorLine().Replace(code, x => $"{x.Groups[1].Value}author \"{Templates.Text(author)}\"", 1);
        return header + "\n\n" + code;
    }

    /// <summary>Создать проект-ремикс (папка creator\&lt;имя&gt;): main.mls с кодом и project.json с forkOf.</summary>
    public static Project Create(HubMod m)
    {
        var p = Projects.Create(Name(m), Code(m, Templates.DefaultAuthor()));
        var meta = new JsonObject
        {
            ["forkOf"] = new JsonObject { ["id"] = m.Id, ["name"] = Line(m.Name), ["author"] = Line(m.Author), ["version"] = Line(m.Version) },
        };
        File.WriteAllText(ProjectMeta.PathOf(p.Dir), meta.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return p;
    }

    [SelfTest]
    static string RemixCreditsOriginal()
    {
        HubMod Mod(string name, string kind, string code) => new("u1_seeds", "u1", "Farmer", name, "", "", "stardew-valley", "1.2.0", kind, code,
            [], [], 0, "", 0, "", "", 0, 0, 0, null, null);
        var original = "# old comment\nmod \"Cheap seeds\"\nversion 1.2.0\nauthor \"Farmer\"\ngame stardew-valley\n\nedit \"Data/Objects\" \"472\" Price = 10\n";
        // Имя с переносом строки: шапка остаётся комментарием, лишней строки кода не появляется.
        var m = Mod("Cheap seeds\nmod \"evil\"", "script", original);
        var code = Remix.Code(m, "Me");
        var lines = code.Split('\n');
        if (!lines.Take(3).All(l => l.StartsWith("# ", StringComparison.Ordinal))) throw new Exception("header is not a comment: " + string.Join(" | ", lines.Take(3)));
        // Без загруженных строк (самопроверка на CI, без окна программы) в шапке — ключи: имя и автора проверяем только с текстами.
        var strings = I18n.T("v26.features.remix.header") != "v26.features.remix.header";
        if (strings && (!lines[0].Contains("Farmer") || !lines[0].Contains("Cheap seeds") || !lines[1].Contains("u1_seeds"))) throw new Exception("credit: " + lines[0] + " | " + lines[1]);
        if (lines.Count(l => l.StartsWith("mod ", StringComparison.Ordinal)) != 1) throw new Exception("one mod line: " + code);
        var modLine = lines.First(l => l.StartsWith("mod ", StringComparison.Ordinal));
        if (!modLine.Contains(Templates.Text(Remix.Name(m)))) throw new Exception("renamed: " + modLine);
        if (!code.Contains("author \"Me\"") || code.Contains("author \"Farmer\"")) throw new Exception("author line");
        if (!code.Contains("edit \"Data/Objects\" \"472\" Price = 10") || !code.Contains("# old comment") || !code.Contains("game stardew-valley")) throw new Exception("body kept");
        // Кода без строки mod: она появляется после шапки.
        var bare = Remix.Code(Mod("Bare", "script", "edit \"x\" \"1\" Price = 2"), "");
        if (!bare.Split('\n').SkipWhile(l => l.StartsWith('#') || l == "").First().StartsWith("mod \"", StringComparison.Ordinal)) throw new Exception("mod line added: " + bare);
        if (Remix.Can(Mod("Pkg", "package", "")) || Remix.Can(Mod("Empty", "script", "  ")) || !Remix.Can(m) || Remix.Can(null)) throw new Exception("can remix");
        return "header credits name, author and hub id as comments; mod line renamed, author replaced, body kept; packages and empty code cannot be remixed";
    }
}
