using System.Text.RegularExpressions;

namespace ModLaunch.Creator;

/// <summary>26.0: проверки публикации в Hub без сети — id карточек, порядок версий.</summary>
public static partial class Hub
{
    [SelfTest]
    static string HubDocIdsDoNotCollide()
    {
        const string uid = "AbC123";
        var rule = new Regex("^abc123_[a-z0-9_]{1,60}$");
        // Латиница и русский — тот же id, что до 26.0 (выложенные моды остаются на месте).
        foreach (var same in new[] { "More Slots", "Больше слотов", "Ёжик и йод", "Мод «Ночь» №2", "MyMod" })
            if (DocId(uid, same) != LegacyDocId(uid, same)) throw new Exception($"id changed for «{same}»: {DocId(uid, same)} vs {LegacyDocId(uid, same)}");
        // Иероглифы, эмодзи и смешанные имена — разные документы, устойчивые от вызова к вызову.
        var pairs = new[] { ("更多敌人", "随机敌人"), ("Mod 🔥", "Mod 💧"), ("もっと敵", "더 많은 적"), ("Mod 更多", "Mod 随机") };
        foreach (var (a, b) in pairs)
        {
            var x = DocId(uid, a);
            var y = DocId(uid, b);
            if (x == y) throw new Exception($"collision: «{a}» and «{b}» → {x}");
            if (x != DocId(uid, " " + a + " ")) throw new Exception("not stable: " + a);
            foreach (var id in new[] { x, y })
                if (!rule.IsMatch(id)) throw new Exception("rules would reject " + id);
        }
        if (DocId(uid, "更多敌人") == DocId(uid, "MyMod")) throw new Exception("CJK name fell back to mymod");
        if (DocId(uid, "Café Ünter") != "abc123_cafe_unter") throw new Exception("accents: " + DocId(uid, "Café Ünter"));
        var longName = new string('x', 55) + " 更多";
        if (!rule.IsMatch(DocId(uid, longName))) throw new Exception("long id: " + DocId(uid, longName));
        if (!rule.IsMatch(DocId(uid, "Mod", hashed: true)) || DocId(uid, "Mod", hashed: true) == DocId(uid, "Mod")) throw new Exception("hashed id");
        // Один мод: регистр и знаки в латинском имени — тот же; иероглифы с тем же началом — разные.
        if (!SameMod("More Slots", "more slots!") || SameMod("Mod 更多", "Mod 随机") || SameMod("Mod", "Mod 更多")) throw new Exception("same mod");
        return $"{pairs.Length} non-Latin pairs apart, Latin/Cyrillic ids unchanged, all match the rules";
    }

    [SelfTest]
    static string HubMineQueryNeedsNoIndex()
    {
        // Свои моды — по автору, а не из окна 300 свежих: одно равенство, без сортировки (иначе нужен составной индекс).
        var q = MineQuery("u1");
        if (q["orderBy"] is not null) throw new Exception("orderBy needs a composite index");
        var f = q["where"]?["fieldFilter"];
        if (f?["field"]?["fieldPath"]?.ToString() != "uid" || f["op"]?.ToString() != "EQUAL" || f["value"]?["stringValue"]?.ToString() != "u1") throw new Exception(q.ToJsonString());
        if ((int?)q["limit"] is not > 300) throw new Exception("limit");
        return "where uid == author, no orderBy, limit " + q["limit"];
    }

    [SelfTest]
    static string HubCommentCounterNamesTheComment()
    {
        // Счётчик меняет только lastComment и comments: по новым правилам ±1 — лишь вместе с этим комментарием.
        var w = CommentCounter("u_mod", "c123", -1);
        var mask = w["updateMask"]?["fieldPaths"]?.AsArray().Select(n => n?.ToString()).ToList() ?? [];
        if (mask is not ["lastComment"]) throw new Exception("mask " + string.Join(",", mask));
        if (w["update"]?["fields"]?["lastComment"]?["stringValue"]?.ToString() != "c123") throw new Exception("lastComment");
        var t = w["updateTransforms"]?[0];
        if (t?["fieldPath"]?.ToString() != "comments" || t["increment"]?["integerValue"]?.ToString() != "-1") throw new Exception("increment");
        if (w["currentDocument"]?["exists"]?.GetValue<bool>() != true) throw new Exception("exists");
        // Правила в программе (Настройки → Сервисы копирует их владельцу) связывают счётчик с комментарием.
        // Без окна программы (самопроверка на CI) ресурсов Avalonia нет — тогда сверяем только саму запись.
        string rules;
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/firestore.rules"));
            rules = new StreamReader(stream).ReadToEnd();
        }
        catch (InvalidOperationException) { return "comment counter carries lastComment (rules text not checked: no app assets in a headless run)"; }
        foreach (var need in new[] { "existsAfter(hubComment(id, request.resource.data.lastComment))", "hubCommentsMoved(id, comment, 1)", "hubCommentsMoved(id, comment, -1)", "'lastComment'" })
            if (!rules.Contains(need)) throw new Exception("rules miss " + need);
        return "comment counter carries lastComment; rules tie ±1 to the comment both ways";
    }

    [SelfTest]
    static string HubVersionsOnlyGoUp()
    {
        if (CompareVersions("1.10.0", "1.9.9") <= 0) throw new Exception("1.10.0 vs 1.9.9");
        if (CompareVersions("1.2.3", "1.2.3") != 0) throw new Exception("equal");
        if (CompareVersions("1.2.3", "1.2.4") >= 0) throw new Exception("1.2.3 vs 1.2.4");
        if (CompareVersions("2.0.0", "10.0.0") >= 0) throw new Exception("2 vs 10");
        if (CompareVersions("v1.0.0", "1.0.0") != 0) throw new Exception("v prefix");
        if (NextVersion("1.2.3") != "1.2.4" || NextVersion("1.9.9") != "1.9.10" || NextVersion("") != "0.0.1") throw new Exception("next " + NextVersion("1.9.9"));
        return "numeric compare, equal and lower versions refused, next = patch + 1";
    }
}
