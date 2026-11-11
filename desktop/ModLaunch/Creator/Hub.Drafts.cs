using System.Text.RegularExpressions;
using ModLaunch.Games;

namespace ModLaunch.Creator;

/// <summary>
/// 26.0: черновик выпуска в Hub — из чего он складывается. Новая версия берёт код из проекта на этом компьютере
/// (а не прошлый код из Hub), не стирает описание, теги и картинки карточки, не выкладывает исходником мод,
/// которому нужны файлы проекта, и не превращает незнакомую игру в первую из списка.
/// </summary>
public static partial class Hub
{
    [GeneratedRegex(@"^([ \t]*version[ \t]+)(""?)([^""\s]+)(""?)", RegexOptions.Multiline)]
    private static partial Regex VersionLine();
    [GeneratedRegex(@"^[ \t]*mod[ \t]+[^\r\n]*$", RegexOptions.Multiline)]
    private static partial Regex ModLine();

    /// <summary>
    /// Версия в коде ModScript = версия выпуска: иначе у игроков мод ставится со старой версией, и Hub вечно
    /// предлагает «обновление». Строки version нет — встаёт после строки mod.
    /// </summary>
    public static string SetCodeVersion(string code, string version)
    {
        if (VersionLine().Match(code) is { Success: true } m)
            return code[..m.Index] + m.Groups[1].Value + m.Groups[2].Value + version + m.Groups[4].Value + code[(m.Index + m.Length)..];
        var nl = code.Contains("\r\n") ? "\r\n" : "\n";
        if (ModLine().Match(code) is { Success: true } mod)
            return code[..(mod.Index + mod.Length)] + nl + "version " + version + code[(mod.Index + mod.Length)..];
        return "version " + version + nl + code;
    }

    /// <summary>
    /// Мод берёт файлы из папки проекта (copy, image): исходником его не выложить — у игроков этих файлов нет,
    /// и сборка падает у каждого («есть только у автора»). Такой мод выкладывается готовым пакетом.
    /// </summary>
    public static bool NeedsFiles(ModBuild b) => b.Copies.Count > 0;

    /// <summary>Какая версия предлагается: своя из кода, если она новее выложенной, иначе следующая за выложенной.</summary>
    public static string SuggestVersion(string local, string published) =>
        CompareVersions(local, published) > 0 ? local : NextVersion(published);

    /// <summary>
    /// Выпуск поверх карточки, что уже в Hub: кратко, описание, теги и картинки — с карточки (автор их здесь не
    /// трогал — они не должны пропасть), имя, игра и код — из проекта, версия — не ниже следующей за выложенной.
    /// </summary>
    public static HubDraft Merge(HubDraft fresh, HubMod existing)
    {
        fresh.Id = existing.Id;
        if (existing.Summary.Trim() != "") fresh.Summary = existing.Summary;
        if (existing.Description.Trim() != "") fresh.Description = existing.Description;
        if (fresh.Tags.Count == 0) fresh.Tags = [.. existing.Tags];
        if (fresh.Images.Count == 0) fresh.Images = [.. existing.Images];
        fresh.Version = SuggestVersion(fresh.Version, existing.Version);
        return fresh;
    }

    /// <summary>
    /// Проект этого компьютера, из которого выложен мод: по hubId из project.json, иначе по id из имени (новому
    /// или до 26.0), иначе по тому же имени и игре. Проект с hubId другого мода не подходит.
    /// </summary>
    public static Project? ProjectFor(HubMod m, IEnumerable<Project> projects, Func<Project, string?> hubIdOf)
    {
        var list = projects.Select(p => (p, Hub: hubIdOf(p))).ToList();
        return list.FirstOrDefault(x => x.Hub == m.Id).p
            ?? list.FirstOrDefault(x => x.Hub is null && (DocId(m.Uid, x.p.Name) == m.Id || LegacyDocId(m.Uid, x.p.Name) == m.Id)).p
            ?? list.FirstOrDefault(x => x.Hub is null && x.p.Game == m.Game && string.Equals(x.p.Name.Trim(), m.Name.Trim(), StringComparison.OrdinalIgnoreCase)).p;
    }

    /// <summary>
    /// Игра из строки game, которой ModLaunch не знает: на что похоже («silksong» → hollow-knight-silksong,
    /// «valhiem» → valheim). Ничего похожего — null.
    /// </summary>
    public static GameDef? SuggestGame(string id, IEnumerable<GameDef> games)
    {
        var want = Regex.Replace(id.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (want.Length < 3) return null;
        var list = games.Where(g => !string.IsNullOrEmpty(g.Id)).ToList();
        static string Flat(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var contains = list.Where(g => g.Id.Contains(want) || Flat(g.Name ?? "").Contains(want) || Flat(g.ShortName ?? "").Contains(want))
            .OrderBy(g => g.Id.Length).FirstOrDefault();
        if (contains is not null) return contains;
        return list.Select(g => (g, d: Distance(want, g.Id))).Where(x => x.d <= Math.Max(2, want.Length / 3)).OrderBy(x => x.d).Select(x => x.g).FirstOrDefault();
    }

    static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var keep = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = keep;
            }
        }
        return row[b.Length];
    }

    [SelfTest]
    static string HubDraftsKeepTheCard()
    {
        // Версия попадает в код: заменяется на месте (с кавычками и без) или встаёт после mod.
        if (SetCodeVersion("mod \"A\"\nversion 1.0.0\ngame valheim\n", "1.0.1") != "mod \"A\"\nversion 1.0.1\ngame valheim\n") throw new Exception("replace");
        if (SetCodeVersion("mod \"A\"\r\n  version \"2.0.0\"\r\n", "2.1.0") != "mod \"A\"\r\n  version \"2.1.0\"\r\n") throw new Exception("quoted");
        if (SetCodeVersion("# x\nmod \"A\"\ngame valheim\n", "1.2.3") != "# x\nmod \"A\"\nversion 1.2.3\ngame valheim\n") throw new Exception("insert");
        if (ModScript.Compile(SetCodeVersion("mod \"A\"\ngame valheim\nversion 1.0.0\n", "3.4.5")).Version != "3.4.5") throw new Exception("compile");

        // Карточка в Hub: описание, теги и картинки не пропадают; версия не ниже следующей.
        var card = new HubMod("u_a", "u", "Me", "A", "Long summary", new string('d', 2000), "valheim", "1.0.0", "script", "", ["qol", "ui"],
            ["https://x/1.png", "https://x/2.png"], 0, "", 0, "", "", 5, 10, 2, null, null);
        var d = Merge(new HubDraft { Name = "A", Summary = "about", Description = "about", Game = "valheim", Version = "1.0.0" }, card);
        if (d.Id != "u_a" || d.Description.Length != 2000 || d.Summary != "Long summary" || d.Tags.Count != 2 || d.Images.Count != 2 || d.Version != "1.0.1")
            throw new Exception($"merge: {d.Id} {d.Description.Length} {d.Summary} {d.Tags.Count} {d.Images.Count} {d.Version}");
        if (Merge(new HubDraft { Version = "2.0.0", Tags = ["fixes"] }, card) is not { Version: "2.0.0", Tags: ["fixes"] }) throw new Exception("own version and tags");

        // copy и image — только пакетом; обычный мод — исходником.
        if (!NeedsFiles(ModScript.Compile("mod \"T\"\ngame valheim\nversion 1.0.0\ncopy \"icon.png\" to \"plugins/T/icon.png\"\n"))) throw new Exception("copy not detected");
        if (NeedsFiles(ModScript.Compile("mod \"T\"\ngame valheim\nversion 1.0.0\nconfig \"a.cfg\" [S] K = 1\n"))) throw new Exception("false copy");

        // Незнакомая игра: подсказка по похожему id или имени.
        var games = GameCatalog.All;
        if (SuggestGame("silksong", games)?.Id != "hollow-knight-silksong") throw new Exception("silksong → " + SuggestGame("silksong", games)?.Id);
        if (SuggestGame("valhiem", games)?.Id != "valheim") throw new Exception("valhiem → " + SuggestGame("valhiem", games)?.Id);
        if (SuggestGame("hollow knight", games)?.Id != "hollow-knight") throw new Exception("hollow knight");
        if (SuggestGame("zzqqxx", games) is not null) throw new Exception("nonsense matched");

        // Проект мода на этом компьютере: hubId, затем id из имени; чужой hubId не подходит.
        var a = new Project("a", "C:/p/a", "A", "valheim", DateTime.UtcNow);
        var other = new Project("b", "C:/p/b", "A", "valheim", DateTime.UtcNow);
        if (ProjectFor(card, [other, a], p => p.Id == "b" ? "u_zzz" : null) != a) throw new Exception("by id");
        if (ProjectFor(card, [a, other], p => p.Id == "b" ? "u_a" : "u_zzz") != other) throw new Exception("by hubId");
        if (ProjectFor(card, [new Project("c", "C:/p/c", "B", "valheim", DateTime.UtcNow)], _ => null) is not null) throw new Exception("wrong project");
        return "version written into code, card fields kept, copy → package, game suggestions, local project found";
    }
}
