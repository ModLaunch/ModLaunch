using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Features;

/// <summary>Проверки 22.0: один ModScript в одной среде и полные переводы.</summary>
static class V22Tests
{
    /// <summary>
    /// Проект правок файлов (main.mls) открывается средой ModScript на месте: описание проекта рядом, файл не тронут,
    /// главный файл — main.mls, повторное открытие ничего не пересоздаёт; путь с «..» не находит чужие папки.
    /// </summary>
    [SelfTest]
    static string ClassicProjectOpensInStudio()
    {
        const string code = "mod \"Studio test\"\ngame stardew-valley\nversion 1.2.0\n";
        var p = Projects.Create("Studio test v22", code);
        try
        {
            var x = XProjects.Adopt(p);
            if (!File.Exists(Path.Combine(p.Dir, XProject.FileName))) throw new Exception("modscript.json not written");
            if (File.ReadAllText(p.Script) != code) throw new Exception("main.mls changed");
            if (!x.Main.EndsWith(ModScript.Extension, StringComparison.OrdinalIgnoreCase)) throw new Exception("main file " + x.Main);
            if (x.Version != "1.2.0" || x.Game != "stardew-valley") throw new Exception($"meta {x.Version} {x.Game}");
            if (XProjects.Get(x.Id)?.Dir != p.Dir) throw new Exception("studio cannot find the project");
            if (XProjects.Adopt(p).Created != x.Created) throw new Exception("adopt twice rewrote the project");
            if (XProjects.Get("..") is not null || XProjects.Get("../" + x.Id) is not null || XProjects.Get(x.Id + "\\..") is not null) throw new Exception("path escape accepted");
            if (!ModScript.Compile(code).Ok) throw new Exception("sample does not compile");
            return "main.mls opens in the ModScript studio in place; path tricks rejected";
        }
        finally { Projects.Delete(p); }
    }

    /// <summary>
    /// Новые строки (22.0 и дальше, ключи v22.*) — сразу на всех восьми языках; провал, если хоть одной нет.
    /// Старый долг (строки 9.x–21 без перевода на uk, es, de, fr, pt-BR, zh-CN) — числом в отчёте, чтобы было видно, сколько осталось.
    /// </summary>
    [SelfTest]
    static string NewStringsInEveryLanguage()
    {
        string[] langs = ["en", "uk", "es", "de", "fr", "pt-BR", "zh-CN"];
        var all = I18n.Keys("ru").Union(I18n.Keys("en")).ToHashSet();
        var fresh = all.Where(k => k.StartsWith("v22.", StringComparison.Ordinal) || k.StartsWith("v23.", StringComparison.Ordinal) || k.StartsWith("v24.", StringComparison.Ordinal)
            || k.StartsWith("new.v24.", StringComparison.Ordinal) || k.StartsWith("err.share.", StringComparison.Ordinal) || k == "snap.reason.crash"
            || k.StartsWith("v25.", StringComparison.Ordinal) || k.StartsWith("new.v25.", StringComparison.Ordinal) || k is "snap.reason.bisect" or "snap.reason.works"
            || k is "v99.frame.premium" or "v99.banner.premium" or "v99.title.premium" or "v99.badge.premium" or "v99.frame.jubilee" or "v99.banner.jubilee" or "v99.title.jubilee").ToList();
        var missing = new List<string>();
        var backlog = new List<string>();
        foreach (var lang in langs)
        {
            var have = I18n.Keys(lang).ToHashSet();
            var lack = fresh.Where(k => !have.Contains(k)).Order(StringComparer.Ordinal).ToList();
            if (lack.Count > 0) missing.Add($"{lang} {lack.Count} ({string.Join(", ", lack.Take(4))})");
            var old = all.Count(k => !have.Contains(k));
            if (old > 0) backlog.Add($"{lang} {old}");
        }
        if (missing.Count > 0) throw new Exception("new strings untranslated: " + string.Join("; ", missing));
        return $"{fresh.Count} new strings in 8 languages; older untranslated: " + (backlog.Count == 0 ? "none" : string.Join(", ", backlog));
    }
}
