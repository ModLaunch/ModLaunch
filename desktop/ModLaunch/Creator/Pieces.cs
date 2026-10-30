using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Views;
using MX = ModScriptX;

namespace ModLaunch.Creator;

/// <summary>
/// Кусочек кода ModScript X: готовое умение мода, которое ставится в проект одной строкой use "Название"
/// (по-русски — использовать "Название"). Бесплатные идут с ModLaunch, остальные покупаются на Рынке (лот «Часть кода» или
/// «Скрипт» с файлом .modx) — их исходник закрыт, в проекте видно только название.
/// </summary>
public sealed record Piece(string Name, string Title, string About, string Icon, string Color, string Seller, bool Free, string? Listing,
    IReadOnlyList<(string Name, string Value)> Settings, IReadOnlyList<string> Events);

public static class Pieces
{
    /// <summary>Подключить кусочки к языку: ModxDoc.Parse берёт их код отсюда.</summary>
    public static void Register() => MX.ModxDoc.Pieces = Source;

    static bool _watching;

    /// <summary>
    /// Купленные и импортированные .modx: у каждого имя — название лота. Библиотеку трогаем только здесь, а не при
    /// регистрации: иначе папка данных определилась бы до --selftest и MODLAUNCH_DATA.
    /// </summary>
    static IEnumerable<InjectItem> Bought()
    {
        if (!_watching)
        {
            _watching = true;
            Inject.Changed += () => { lock (Cache) Cache.Clear(); };
        }
        return Inject.All().Where(i => i.Kind == "modx" && i.Sealed);
    }

    /// <summary>Бесплатный кусочек: имя в коде (английское, одинаковое на любом языке), ключ подписи, значок, цвет и код.</summary>
    sealed record Builtin(string Name, string Key, string Icon, string Color, string[] Settings, string Code);

    static readonly Builtin[] Starter =
    [
        new("Double gold", "gold", Icons.Sparkles, "#F5B94A", ["gold_bonus"],
            "let gold_bonus = 2\n\nhook start {\n    set gold = gold * gold_bonus\n    say \"Gold x{gold_bonus}!\"\n}\n"),
        new("Healing", "heal", Icons.Heart, "#4CC38A", ["heal", "every"],
            "let heal = 1\nlet every = 120\nlet heal_timer = 0\n\nhook tick {\n    add heal_timer 1\n    if heal_timer >= every {\n        set heal_timer = 0\n        set health = min(health + heal, 100)\n    }\n}\n"),
        new("Lucky find", "luck", Icons.Star, "#FFCB6B", ["luck", "prize"],
            "let luck = 10\nlet prize = 50\nlet luck_timer = 0\n\nhook tick {\n    add luck_timer 1\n    if luck_timer >= 600 {\n        set luck_timer = 0\n        if chance(luck) {\n            add gold prize\n            say \"Lucky! +{prize} gold\"\n        }\n    }\n}\n"),
        new("Welcome", "hello", Icons.Chat, "#9B7BFF", ["text"],
            "let text = \"Hi! My mod is on\"\n\nhook start {\n    say text\n}\n"),
        new("Break reminder", "break", Icons.Coffee, "#36C2E0", ["minutes"],
            "let minutes = 30\nlet play_ticks = 0\n\nhook tick {\n    add play_ticks 1\n    if play_ticks >= minutes * 3600 {\n        set play_ticks = 0\n        say \"You've played {minutes} min. Time for a break!\"\n    }\n}\n"),
        new("Hard mode", "hard", Icons.Flame, "#FF5A5F", ["enemy_power"],
            "let enemy_power = 2\n\nhook start {\n    set enemy_damage = enemy_damage * enemy_power\n    say \"Hard mode x{enemy_power}\"\n}\n"),
        new("God mode", "god", Icons.Shield, "#FF6FB1", ["max_health"],
            "let max_health = 100\n\nhook tick {\n    set health = max_health\n}\n"),
        new("Speed boost", "speed", Icons.Zap, "#4C8DFF", ["boost"],
            "let boost = 1.5\n\nhook start {\n    set speed = speed * boost\n    say \"Speed x{boost}\"\n}\n"),
    ];

    /// <summary>Расшифрованный код купленных кусочков — в памяти, пока библиотека не поменялась.</summary>
    static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    static string Title(Builtin b) => I18n.T($"v21.piece.{b.Key}");

    static readonly string[] Langs = ["ru", "en", "de", "es", "fr", "pt-BR", "uk", "zh-CN"];

    /// <summary>
    /// Имя бесплатного кусочка в коде — подпись на языке программы («Двойное золото»), но узнаётся и английское имя,
    /// и подпись на любом другом языке: файл, написанный по-русски, откроется и на английском ModLaunch.
    /// </summary>
    static bool Named(Builtin b, string name) => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Title(b), name, StringComparison.OrdinalIgnoreCase)
        || Langs.Any(l => string.Equals(I18n.Raw(l, $"v21.piece.{b.Key}"), name, StringComparison.OrdinalIgnoreCase));


    /// <summary>Все кусочки: сначала купленные (по дате), потом бесплатные.</summary>
    public static List<Piece> All()
    {
        var list = new List<Piece>();
        foreach (var i in Bought())
        {
            var doc = Source(i.Title) is { } code ? MX.ModxDoc.Parse(code) : null;
            list.Add(new Piece(i.Title, i.Title, i.Changes, Icons.Package, "#C9A8FF", i.Seller, false, i.Listing,
                [], doc?.Hooks.Select(h => h.Event).Distinct().ToList() ?? []));
        }
        foreach (var b in Starter)
        {
            var doc = MX.ModxDoc.Parse(b.Code);
            var settings = b.Settings.Select(s => (s, doc.Shared.FirstOrDefault(x => x.Name == s).Value?.ToJsonString() ?? "")).ToList();
            list.Add(new Piece(b.Name, Title(b), I18n.T($"v21.piece.{b.Key}.text"), b.Icon, b.Color, "ModLaunch", true, null, settings,
                doc.Hooks.Select(h => h.Event).Distinct().ToList()));
        }
        return list.DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Кусочек по названию из строки use: купленный (название лота) или бесплатный (имя или подпись на любом языке).</summary>
    public static Piece? Find(string name) => All().FirstOrDefault(p => Same(p, name));

    static bool Same(Piece p, string name) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Title, name, StringComparison.OrdinalIgnoreCase)
        || p.Free && Starter.FirstOrDefault(b => b.Name == p.Name) is { } b && Named(b, name);

    /// <summary>Код кусочка для разбора .modx; null — такого кусочка нет.</summary>
    public static string? Source(string name)
    {
        var free = Starter.FirstOrDefault(b => Named(b, name));
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var known)) return known ?? free?.Code;
        }
        string? code = null;
        try
        {
            if (Bought().FirstOrDefault(i => string.Equals(i.Title, name, StringComparison.OrdinalIgnoreCase)) is { } item) code = Inject.Code(item);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        lock (Cache) Cache[name] = code;
        return code ?? free?.Code;
    }

    /// <summary>Строка use для кода (в русской программе — «использовать»): название и настройки, которые отличаются от значений кусочка.</summary>
    public static string Line(string name, IEnumerable<(string Name, string Value)>? settings = null)
    {
        var list = settings?.Where(s => s.Value.Trim() != "").Select(s => $"{s.Name} = {s.Value.Trim()}").ToList() ?? [];
        return $"{(SimpleWords.Ru ? "использовать" : "use")} \"{name.Replace("\"", "'")}\"" + (list.Count > 0 ? $" ({string.Join(", ", list)})" : "");
    }

    /// <summary>Кусочек есть в проекте (строкой use): чтобы не вставить его дважды.</summary>
    public static bool Used(string text, string name) => text.Replace("\r\n", "\n").Split('\n')
        .Select((l, i) => MX.ModxDoc.ReadUse(l, i + 1)).Any(u => u is not null && (string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)
            || Find(u.Name) is { } p && Same(p, name)));

    [SelfTest]
    static string PiecesExpandByName()
    {
        var saved = MX.ModxDoc.Pieces;
        try
        {
            MX.ModxDoc.Pieces = Source;
            var doc = MX.ModxDoc.Parse("mod \"Kid mod\"\nengine renpy\nuse \"Double gold\" (gold_bonus = 3)\nuse \"Welcome\" (text = \"Hey, it works\")\n\nhook start {\n    say \"Gold: {gold}\"\n}\n");
            if (!doc.Ok) throw new Exception("diags: " + string.Join("; ", doc.Diags.Select(d => d.Message)));
            var start = doc.Hooks.Single(h => h.Event == "start");
            // Кусочки первыми, свой код — после; шаги кусочка — на строке use.
            if (start.Body.Count != 4 || start.Body[0].Line != 3 || start.Body[2].Line != 4 || start.Body[3].Kind != "say" || start.Body[3].Line != 7)
                throw new Exception("merge: " + string.Join(", ", start.Body.Select(s => $"{s.Kind}@{s.Line}")));
            if (doc.Shared.FirstOrDefault(s => s.Name == "gold_bonus").Value?.ToJsonString() != "3") throw new Exception("setting not applied");
            if (doc.Shared.FirstOrDefault(s => s.Name == "text").Value?.GetValue<string>() != "Hey, it works") throw new Exception("text setting");
            var run = MX.Sandbox.Run(doc, MX.Sandbox.ParseVars("gold = 100", []), 1);
            if (!run.Ok || !string.Join("\n", run.Output).Contains("Gold: 300")) throw new Exception("sandbox: " + string.Join(" | ", run.Output));
            var missing = MX.ModxDoc.Parse("mod \"x\"\nuse \"No such piece\"\nhook start {\n    log \"a\"\n}\n");
            if (missing.Ok || missing.Diags[0].Line != 2) throw new Exception("missing piece passed");
            if (MX.ModxDoc.ReadUse("use \"A b\" (x = 1, s = \"a, b\")", 1) is not { Settings: [("x", "1"), ("s", "\"a, b\"")] }) throw new Exception("use settings");
            var py = MX.Target.Find("renpy")!.Emit(doc, null);
            if (!py.Any(f => f.Text.Contains("gold_bonus"))) throw new Exception("renpy lost the piece");
            return $"{Starter.Length} free pieces, use + settings merge into hooks, sandbox and Ren'Py";
        }
        finally { MX.ModxDoc.Pieces = saved; }
    }
}
