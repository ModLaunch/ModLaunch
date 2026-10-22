using System.Text.Json.Nodes;
using ModLaunch.Core;
using MX = ModScriptX;

namespace ModLaunch.Creator;

/// <summary>
/// 16.0: проект ModScript X — папка «Документы/ModScript X/Проекты/&lt;имя&gt;»: modscript.json (ответы анкеты и мост),
/// main.modx (код мода) и README.md. Создаётся анкетой Creator Hub, открывается в среде ModScript X (XStudio).
/// </summary>
public sealed class XProject
{
    public const string FileName = "modscript.json";

    public string Dir = "";
    public string Name = "";
    public string About = "";
    /// <summary>Игра ModLaunch (id) или пусто — тогда только папка GamePath.</summary>
    public string Game = "";
    public string? GamePath;
    /// <summary>Что делает мод (вопрос анкеты): gameplay, economy, messages, difficulty, cheats, empty.</summary>
    public string Kind = "gameplay";
    /// <summary>Движок по мосту (Bridge.Scan): unity-mono, unreal, renpy…</summary>
    public string Engine = "generic";
    /// <summary>Перевод ModScript X (unity, unreal, html5…); null — у игры только файлы.</summary>
    public string? Target;
    public string Version = "1.0.0";
    public string Author = "";
    public string Color = "violet";
    public List<string> Events = [];
    public DateTime Created = DateTime.UtcNow;

    public string Main => Path.Combine(Dir, "main" + MX.ModxDoc.Extension);
    public string Id => Path.GetFileName(Dir);
    public DateTime Updated => File.Exists(Main) ? File.GetLastWriteTimeUtc(Main) : Created;

    public JsonObject ToJson() => new()
    {
        ["format"] = "modscript-project", ["version"] = 1, ["name"] = Name, ["about"] = About, ["game"] = Game, ["gamePath"] = GamePath,
        ["kind"] = Kind, ["engine"] = Engine, ["target"] = Target, ["modVersion"] = Version, ["author"] = Author, ["color"] = Color,
        ["events"] = new JsonArray(Events.Select(e => (JsonNode)e).ToArray()), ["created"] = Created.ToString("o"),
    };

    public static XProject? Load(string dir)
    {
        try
        {
            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o) return null;
            return new XProject
            {
                Dir = dir, Name = o.Str("name") ?? Path.GetFileName(dir), About = o.Str("about") ?? "", Game = o.Str("game") ?? "", GamePath = o.Str("gamePath"),
                Kind = o.Str("kind") ?? "gameplay", Engine = o.Str("engine") ?? "generic", Target = o.Str("target"), Version = o.Str("modVersion") ?? "1.0.0",
                Author = o.Str("author") ?? "", Color = o.Str("color") ?? "violet",
                Events = o.Arr("events").Select(n => n?.ToString() ?? "").Where(e => e != "").ToList(),
                Created = DateTime.TryParse(o.Str("created"), out var c) ? c.ToUniversalTime() : Directory.GetCreationTimeUtc(dir),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }

    public void Save() => File.WriteAllText(Path.Combine(Dir, FileName), ToJson().ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>Ответы анкеты «Новый проект».</summary>
public sealed record XForm(string Name, string About, string Game, string? GamePath, string Kind, string Engine, string? Target,
    List<string> Events, string Author, string Version, string Color);

public static class XProjects
{
    /// <summary>Виды модов из анкеты: что мод делает — от этого зависит начальный код.</summary>
    public static readonly string[] Kinds = ["gameplay", "economy", "messages", "difficulty", "cheats", "empty"];

    /// <summary>Цвета проекта (плитка в Hub, полоса в среде).</summary>
    public static readonly (string Id, string Hex)[] Colors =
    [
        ("violet", "#8B6CFF"), ("blue", "#4C8DFF"), ("cyan", "#3CC8E6"), ("green", "#3FCB7E"), ("orange", "#FF9640"), ("pink", "#FF6FB1"), ("red", "#FF5A5F"),
    ];

    public static string Hex(string color) => Colors.FirstOrDefault(c => c.Id == color).Hex ?? Colors[0].Hex;

    /// <summary>Папка проектов: в документах (рядом с файлами ModScript X); в демо и снимках — в папке данных.</summary>
    public static string Root
    {
        get
        {
            if (Program.Demo || Program.Screenshot) return Directory.CreateDirectory(Path.Combine(Paths.DataDir, "modscript-projects")).FullName;
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Directory.CreateDirectory(Path.Combine(docs != "" ? docs : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ModScript X", "Projects")).FullName;
        }
    }

    public static List<XProject> List()
    {
        var list = new List<XProject>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Root))
                if (XProject.Load(dir) is { } p) list.Add(p);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list.OrderByDescending(p => p.Updated).ToList();
    }

    public static XProject? Get(string id) => XProject.Load(Path.Combine(Root, id)) is { } p ? p : null;

    /// <summary>Создать проект по анкете: папка, modscript.json, main.modx из шаблона вида, README.md.</summary>
    public static XProject Create(XForm f) => Create(f, Root);

    internal static XProject Create(XForm f, string root)
    {
        var slug = Games.CustomGames.Slug(f.Name);
        if (slug == "") slug = "mod";
        var id = slug;
        for (var i = 2; Directory.Exists(Path.Combine(root, id)); i++) id = $"{slug}-{i}";
        var dir = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
        var p = new XProject
        {
            Dir = dir, Name = f.Name.Trim() == "" ? "My mod" : f.Name.Trim(), About = f.About.Trim(), Game = f.Game, GamePath = f.GamePath, Kind = f.Kind,
            Engine = f.Engine, Target = f.Target, Version = f.Version.Trim() == "" ? "1.0.0" : f.Version.Trim(), Author = f.Author.Trim(), Color = f.Color,
            Events = f.Events.ToList(), Created = DateTime.UtcNow,
        };
        p.Save();
        File.WriteAllText(p.Main, Template(p));
        File.WriteAllText(Path.Combine(dir, "README.md"), $"# {p.Name}\n\n{(p.About == "" ? "" : p.About + "\n\n")}Made with ModLaunch Creator Hub · ModScript X.\n");
        return p;
    }

    public static void Delete(XProject p)
    {
        try { Directory.Delete(p.Dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "'").Replace("\n", " ") + "\"";

    /// <summary>
    /// Начальный код по анкете: шапка мода (имя, версия, автор, описание, движок), переменные вида и хуки
    /// только тех событий, что выбраны и есть у движка.
    /// </summary>
    public static string Template(XProject p)
    {
        var target = p.Target is { } t ? MX.Target.Find(t) : null;
        var events = (p.Events.Count > 0 ? p.Events : ["start"]).Where(e => target is null || target.Events.Contains(e)).Distinct().ToList();
        if (events.Count == 0) events.Add(target?.Events.FirstOrDefault() ?? "ready");
        var w = new System.Text.StringBuilder();
        w.Append($"# {p.Name} — ModScript X\n");
        w.Append($"mod {Q(p.Name)}\nversion {p.Version}\n");
        if (p.Author != "") w.Append($"author {Q(p.Author)}\n");
        if (p.About != "") w.Append($"about {Q(p.About)}\n");
        if (target is not null) w.Append($"engine {target.Id}\n");
        w.Append('\n');
        var (lets, fn, bodies) = Kind(p.Kind, p.Name);
        foreach (var l in lets) w.Append(l).Append('\n');
        if (lets.Length > 0) w.Append('\n');
        if (fn != "") w.Append(fn).Append("\n\n");
        foreach (var e in events)
        {
            var body = bodies.TryGetValue(e, out var b) ? b : bodies.GetValueOrDefault("*") ?? ["log \"" + e + "\""];
            w.Append($"hook {e} {{\n");
            foreach (var line in body) w.Append("    ").Append(line).Append('\n');
            w.Append("}\n\n");
        }
        return w.ToString().TrimEnd() + "\n";
    }

    static (string[] Lets, string Fn, Dictionary<string, string[]> Bodies) Kind(string kind, string name)
    {
        var title = name.Replace("\"", "'").Replace("{", "(").Replace("}", ")");
        return kind switch
        {
            "economy" => (
                ["# Награды: меняйте числа — остальное подстроится.", "let bonus = 250", "let per_wave = 50", "let wave_timer = 0"],
                "fn reward(wave) {\n    return clamp(per_wave * wave, 0, 1000)\n}",
                new()
                {
                    ["start"] = ["set gold = gold + bonus", $"say \"{title}: +{{bonus}} gold\""],
                    ["load"] = ["if gold < bonus {", "    add gold bonus", "    log \"bonus on load: +{bonus}\"", "}"],
                    ["tick"] = ["add wave_timer 1", "if wave_timer >= 600 {", "    set gold = gold + reward(2)", "    set wave_timer = 0", "}"],
                    ["*"] = ["log \"economy ready\""],
                }),
            "messages" => (
                ["let greeting = \"" + title + " is on\"", "let shown = 0", "let ticks = 0"],
                "",
                new()
                {
                    ["ready"] = ["log greeting"],
                    ["start"] = ["say greeting", "set shown = shown + 1"],
                    ["load"] = ["say \"Welcome back! Loaded {shown} times\""],
                    ["tick"] = ["add ticks 1", "if ticks == 3000 {", "    say \"Take a break? You've been playing a while\"", "}"],
                }),
            "difficulty" => (
                ["# Сложность: 1 — как в игре, 2 — вдвое тяжелее.", "let scale = 1.5", "let min_health = 10"],
                "fn harder(value) {\n    return round(value * scale)\n}",
                new()
                {
                    ["start"] = ["set enemy_damage = harder(enemy_damage)", $"say \"{title}: difficulty x{{scale}}\""],
                    ["tick"] = ["if health < min_health {", "    log \"low health: {health}\"", "}"],
                    ["*"] = ["log \"difficulty x{scale}\""],
                }),
            "cheats" => (
                ["# Только для одиночной игры.", "let god_mode = true", "let max_health = 100"],
                "",
                new()
                {
                    ["start"] = [$"say \"{title}: cheats on\""],
                    ["tick"] = ["if god_mode {", "    set health = max_health", "}"],
                    ["*"] = ["log \"cheats ready\""],
                }),
            "empty" => ([], "", new() { ["*"] = ["log \"" + title + " loaded\""] }),
            _ => (
                ["# Правила игры: меняйте числа — остальное подстроится.", "let multiplier = 2", "let started = 0", "let ticks = 0"],
                "fn boost(value) {\n    return value * multiplier\n}",
                new()
                {
                    ["ready"] = [$"log \"{title} ready\""],
                    ["start"] = ["set started = started + 1", "set speed = boost(speed)", $"say \"{title}: x{{multiplier}}\""],
                    ["load"] = ["log \"loaded, started {started} times\""],
                    ["tick"] = ["add ticks 1"],
                }),
        };
    }

    [SelfTest]
    static string QuestionnaireTemplatesBuildEverywhere()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modlaunch-xp-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var n = 0;
            foreach (var kind in Kinds)
            foreach (var t in MX.Target.All)
            {
                var p = Create(new XForm("Test \"" + kind + "\" mod", "About it", "", null, kind, "x", t.Id, ["ready", "start", "load", "tick"], "Me", "1.2.0", "blue"), root);
                var text = File.ReadAllText(p.Main);
                var doc = MX.ModxDoc.Parse(text);
                if (!doc.Ok) throw new Exception($"{kind}/{t.Id}: " + string.Join("; ", doc.Diags.Select(d => d.Line + " " + d.Message)));
                t.Emit(doc, null);
                if (!doc.Ok) throw new Exception($"{kind}/{t.Id} emit: " + string.Join("; ", doc.Diags.Where(d => !d.Warning).Select(d => d.Message)));
                if (doc.Hooks.Any(h => !t.Events.Contains(h.Event))) throw new Exception($"{kind}/{t.Id}: hook the engine lacks");
                if (doc.Engine != t.Id || doc.Version != "1.2.0") throw new Exception("header " + doc.Engine + " " + doc.Version);
                n++;
            }
            var all = Directory.EnumerateDirectories(root).Count();
            if (all != n) throw new Exception("same names must get their own folders");
            var back = XProject.Load(Directory.EnumerateDirectories(root).First())!;
            if (back.Version != "1.2.0" || back.Color != "blue" || back.Events.Count != 4) throw new Exception("modscript.json round trip");
            return $"{Kinds.Length} kinds × {MX.Target.All.Length} engines parse and translate";
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
