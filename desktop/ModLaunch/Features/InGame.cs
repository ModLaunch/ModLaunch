using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Creator;
using MX = ModScriptX;

namespace ModLaunch.Features;

/// <summary>
/// 23.0: создание мода прямо во время игры (оверлей → «Создать мод»). Инструменты — готовые правила простыми
/// словами («на старте дать 500 золота», «здоровье не ниже 10», «сообщение при старте»); каждое правило
/// дописывается в мод этой игры (обычный проект ModScript, его потом можно открыть в Creator Hub) и мод
/// ставится в игру тем же путём, что F6 в среде: проверка модерации, перевод в язык движка, установка.
/// В играх с античитом инструменты не работают — ModLaunch не помогает читерить в онлайне.
/// </summary>
public static partial class InGame
{
    /// <summary>Инструменты: id, значок, нужна ли переменная игры, нужно ли число, нужен ли текст.</summary>
    public static readonly (string Id, string Icon, bool Var, bool Number, bool Text)[] Tools =
    [
        ("say", Views.Icons.Bell, false, false, true),
        ("bonus", Views.Icons.Bag, true, true, false),
        ("floor", Views.Icons.Shield, true, true, false),
        ("reload", Views.Icons.Refresh, true, true, false),
    ];

    /// <summary>Переменные игры, которые понимает ModScript на всех движках.</summary>
    public static readonly string[] Vars = ["gold", "money", "health", "xp", "ammo", "score"];

    /// <summary>Хук и строки правила для инструмента. number — число, text — сообщение.</summary>
    public static (string Hook, List<string> Lines) Rule(string tool, string? var, double number, string? text)
    {
        static string Q(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ").Trim();
        var v = var is not null && Vars.Contains(var) ? var : "gold";
        var n = number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return tool switch
        {
            "say" => ("start", [$"say \"{Q(text ?? "")}\""]),
            "bonus" => ("start", [$"add {v} {n}"]),
            "floor" => ("tick", [$"if {v} < {n} {{", $"    set {v} = {n}", "}"]),
            "reload" => ("load", [$"add {v} {n}"]),
            _ => throw new ArgumentException("tool " + tool),
        };
    }

    [GeneratedRegex(@"^\s*hook\s+(\w+)\s*\{\s*$")] private static partial Regex HookLine();

    /// <summary>Дописать строки в блок «hook name { … }» (в конец блока), а если такого блока нет — новым блоком в конец файла.</summary>
    public static string Insert(string source, string hook, IReadOnlyList<string> lines)
    {
        var text = source.Replace("\r\n", "\n");
        var all = text.Split('\n').ToList();
        for (var i = 0; i < all.Count; i++)
        {
            if (HookLine().Match(all[i]) is not { Success: true } m || m.Groups[1].Value != hook) continue;
            var depth = 0;
            for (var j = i; j < all.Count; j++)
            {
                depth += all[j].Count(c => c == '{') - all[j].Count(c => c == '}');
                if (depth != 0) continue;
                all.InsertRange(j, lines.Select(l => "    " + l));
                return string.Join("\n", all);
            }
            break;
        }
        var tail = text.TrimEnd('\n');
        return tail + "\n\nhook " + hook + " {\n" + string.Join("\n", lines.Select(l => "    " + l)) + "\n}\n";
    }

    /// <summary>Мод, который собирается из игры: тот, что выбран для игры раньше, иначе — новый проект.</summary>
    public static XProject ModFor(GameState g, MX.BridgeReport bridge)
    {
        var known = Settings.Data.Obj("inGameMod").Str(g.Def.Id) is { } id ? XProjects.Get(id) : null;
        if (known is not null && File.Exists(known.Main) && known.Main.EndsWith(".modx", StringComparison.OrdinalIgnoreCase)) return known;
        var target = MX.Bridge.TargetOf(bridge) ?? throw new InvalidOperationException("no code bridge");
        var name = I18n.T("v23.ig.modName", ("game", g.Def.ShortName));
        var p = XProjects.Create(new XForm(name, I18n.T("v23.ig.modAbout"), g.Def.Id, g.Path, "empty", bridge.Engine, target.Id, [], Templates.DefaultAuthor(), "1.0.0", "amber"));
        Settings.Data.Obj("inGameMod")[g.Def.Id] = p.Id;
        Settings.Save();
        return p;
    }

    /// <summary>Добавить правило инструмента в мод игры (файл сохраняется сразу).</summary>
    public static void AddRule(XProject p, string tool, string? var, double number, string? text)
    {
        var (hook, lines) = Rule(tool, var, number, text);
        File.WriteAllText(p.Main, Insert(File.ReadAllText(p.Main), hook, lines));
    }

    /// <summary>Сколько правил (строк действий) в моде — для подписи «в моде N правил».</summary>
    public static int RuleCount(XProject p)
    {
        try { return MX.ModxDoc.Parse(File.ReadAllText(p.Main)).Hooks.Sum(h => h.Body.Count); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    public sealed record ApplyResult(bool Ok, bool Built, string Message);

    /// <summary>Поставить мод в игру — как F6 в среде: модерация, перевод, установка (сборка C# у Unity).</summary>
    public static ApplyResult Apply(XProject p, string gamePath)
    {
        if (MX.Engines.FindAntiCheat(gamePath) is { } ac) return new(false, false, I18n.T("v23.ig.anticheat", ("ac", ac)));
        var source = File.ReadAllText(p.Main);
        var verdict = Moderation.CheckModx(source);
        if (!verdict.Ok) return new(false, false, I18n.T("v15.ms.inj.blocked"));
        var engine = MX.ModxDoc.Parse(source).Engine is null ? p.Target : null;
        var r = MX.Injector.Inject(source, gamePath, engine);
        if (!r.Doc.Ok || r.Target is null)
            return new(false, false, string.Join("\n", r.Doc.Diags.Where(d => !d.Warning).Take(3).Select(d => (d.Line > 0 ? I18n.T("v15.ms.line", ("n", d.Line)) + ": " : "") + Views.CreatorPage.LxMsg(d))));
        var built = r.Target.Id != "unity" || Inject.Built(gamePath, r.Doc.Id);
        return new(built, built, built ? I18n.T("v23.ig.applied", ("mod", r.Doc.Name)) : I18n.T("v15.ms.inj.failed"));
    }

    [SelfTest]
    static string InGameToolsWriteValidMods()
    {
        const string head = "mod \"In game\"\nversion 1.0.0\n";
        var src = head;
        foreach (var (id, _, _, _, _) in Tools)
        {
            var (hook, lines) = Rule(id, "health", 10, "Привет \"игрок\"");
            src = Insert(src, hook, lines);
        }
        // Второе правило в тот же хук — внутрь существующего блока, а не вторым блоком.
        var (h2, l2) = Rule("bonus", "gold", 250, null);
        src = Insert(src, h2, l2);
        var doc = MX.ModxDoc.Parse(src);
        if (!doc.Ok) throw new Exception(string.Join("; ", doc.Diags.Select(d => d.Line + " " + d.Message)) + "\n" + src);
        if (doc.Hooks.Count(h => h.Event == "start") != 1) throw new Exception("start hook split in two:\n" + src);
        if (!src.Contains("add gold 250") || !src.Contains("set health = 10") || !src.Contains("\\\"игрок\\\"")) throw new Exception("rule text:\n" + src);
        var run = MX.Sandbox.Run(doc, new Dictionary<string, object?> { ["gold"] = 0d, ["health"] = 3d }, ticks: 1);
        // Здоровье: 3 + 10 на старте + 10 при загрузке = 23 (выше порога 10 — «не ниже» не срабатывает).
        if (MX.Sandbox.Text(run.Vars["gold"]) != "250" || MX.Sandbox.Text(run.Vars["health"]) != "23") throw new Exception($"sandbox gold {MX.Sandbox.Text(run.Vars["gold"])} health {MX.Sandbox.Text(run.Vars["health"])}");
        return $"{Tools.Length} in-game tools write one valid mod; rules merge into existing hooks and run in the sandbox";
    }
}
