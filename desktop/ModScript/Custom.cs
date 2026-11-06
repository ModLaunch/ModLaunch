using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Creator;

namespace ModScriptX;

/// <summary>
/// 22.0: описание самодельного движка — файл modscript.engine.json в папке игры. Его пишет автор игры (или
/// «modscript profile», который сам угадывает язык). С ним ModScript X ставит моды в любой движок, где есть скрипты
/// на Lua, Python, JavaScript, C# или GDScript, — без отдельной поддержки в самом ModScript X.
///
///     {
///       "name": "Моя игра",
///       "language": "lua",                       // lua | python | javascript | csharp | gdscript
///       "file": "mods/{id}.lua",                 // куда положить мод; {id} — имя мода латиницей
///       "say": "Game.toast({text})",             // как показать текст игроку (необязательно)
///       "log": "print({text})",                  // как написать в лог (необязательно)
///       "events": {                              // как подключить события; {fn} — функция события
///         "ready": "{fn}()",
///         "start": "Game.on(\"start\", {fn})",
///         "tick":  "Game.on(\"update\", {fn})"
///       },
///       "header": ["-- строки в начале файла мода"],
///       "footer": ["-- строки в конце"]
///     }
/// </summary>
public sealed record EngineProfile(
    string Name, string Language, string File, string? Say, string? Log,
    Dictionary<string, string> Events, List<string> Header, List<string> Footer, string? Extends)
{
    public const string FileName = "modscript.engine.json";

    /// <summary>Профиль из папки игры (или null, если файла нет). Ошибка в файле — исключение с понятным текстом.</summary>
    public static EngineProfile? Find(string? gameDir)
    {
        if (gameDir is null) return null;
        var path = Path.Combine(gameDir, FileName);
        return System.IO.File.Exists(path) ? Load(path) : null;
    }

    public static EngineProfile Load(string path)
    {
        JsonObject o;
        try { o = JsonNode.Parse(System.IO.File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? throw new InvalidOperationException("not a JSON object"); }
        catch (JsonException e) { throw new InvalidOperationException($"{FileName}: {e.Message}"); }
        string? S(string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim() != "" ? s : null;
        List<string> Lines(string key) => o[key] switch
        {
            JsonArray a => a.Select(x => x?.ToString() ?? "").ToList(),
            JsonValue v when v.TryGetValue<string>(out var s) => [.. s.Split('\n')],
            _ => [],
        };
        var lang = ModxDoc.NormalizeLang((S("language") ?? "").ToLowerInvariant())
                   ?? throw new InvalidOperationException($"{FileName}: \"language\" must be one of {string.Join(", ", ModxDoc.Languages)}");
        var events = new Dictionary<string, string>();
        if (o["events"] is JsonObject ev)
            foreach (var (k, v) in ev)
            {
                if (!ModxDoc.Events.Contains(k)) throw new InvalidOperationException($"{FileName}: unknown event \"{k}\" (use {string.Join(", ", ModxDoc.Events)})");
                events[k] = v?.ToString() ?? "";
            }
        var file = S("file") ?? $"mods/{{id}}.{Ext(lang)}";
        if (file.Contains("..") || Path.IsPathRooted(file)) throw new InvalidOperationException($"{FileName}: \"file\" must be a path inside the game folder");
        return new EngineProfile(S("name") ?? "Custom engine", lang, file, S("say"), S("log"), events, Lines("header"), Lines("footer"), S("extends"));
    }

    public static string Ext(string lang) => lang switch { "python" => "py", "javascript" => "js", "csharp" => "cs", "gdscript" => "gd", _ => "lua" };

    /// <summary>Заготовка профиля: язык угадывается по скриптам в папке игры.</summary>
    public static string Template(string gameDir, string? language)
    {
        var lang = language is not null ? ModxDoc.NormalizeLang(language.ToLowerInvariant()) ?? "lua" : Guess(gameDir);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDir)));
        var ext = Ext(lang);
        var events = lang switch
        {
            "gdscript" => new JsonObject { ["ready"] = "_init", ["start"] = "_ready", ["tick"] = "_process" },
            "csharp" => new JsonObject(),
            _ => new JsonObject { ["ready"] = "{fn}()" },
        };
        var o = new JsonObject
        {
            ["name"] = name,
            ["language"] = lang,
            ["file"] = $"mods/{{id}}.{ext}",
            ["say"] = null,
            ["log"] = null,
            ["events"] = events,
            ["header"] = new JsonArray(),
            ["footer"] = new JsonArray(),
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Язык, на котором написано больше всего скриптов игры.</summary>
    static string Guess(string gameDir)
    {
        var counts = new Dictionary<string, int>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(gameDir, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true }).Take(20000))
            {
                var lang = Path.GetExtension(f).ToLowerInvariant() switch { ".lua" => "lua", ".py" or ".rpy" => "python", ".js" => "javascript", ".gd" => "gdscript", ".cs" => "csharp", _ => null };
                if (lang != null) counts[lang] = counts.GetValueOrDefault(lang) + 1;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "lua";
    }
}

/// <summary>
/// 22.0: «свой движок» — цель, которую описывает modscript.engine.json. Без профиля (engine custom без файла) —
/// понятная ошибка, как его завести.
/// </summary>
public sealed class CustomTarget(EngineProfile? profile = null) : Target
{
    /// <summary>Профиль для build без папки игры (modscript build … --profile file.json).</summary>
    public static string? ProfilePath;

    public EngineProfile? Profile { get; } = profile;
    public override string Id => "custom";
    public override string Title => Profile is null ? "Custom engine (modscript.engine.json)" : $"{Profile.Name} (custom engine)";
    public override string Language => Profile?.Language ?? "lua";
    protected override string[] Aliases => ["own", "homemade"];

    /// <summary>Цель «свой движок» с уже прочитанным профилем (из папки игры или --profile); другие цели — как есть.</summary>
    public static Target? Bind(Target? t, string? gameDir)
    {
        if (t is not CustomTarget { Profile: null }) return t;
        var p = EngineProfile.Find(gameDir) ?? (ProfilePath is not null ? EngineProfile.Load(ProfilePath) : null);
        return p is null ? t : new CustomTarget(p);
    }

    public override List<OutFile> Emit(ModxDoc d, string? gameDir)
    {
        var p = Profile ?? EngineProfile.Find(gameDir) ?? (ProfilePath is not null ? EngineProfile.Load(ProfilePath) : null);
        if (p is null)
        {
            // Без описания — обычный Lua-мод в mods/: так устроено большинство самодельных движков на Lua.
            d.Diags.Add(new Diag(0, $"custom engine without {EngineProfile.FileName}: built as a Lua mod in mods/ (modscript profile <gameDir> describes your engine)", true));
            p = new EngineProfile("Custom engine", "lua", "mods/{id}.lua", null, null, [], [], [], null);
        }
        return Profile is null ? new CustomTarget(p).Emit(d, gameDir) : Write(d, p);
    }

    List<OutFile> Write(ModxDoc d, EngineProfile p)
    {
        Warn(d);
        string Fill(string template, string text) => template.Replace("{text}", text).Replace("{id}", d.Id);
        string SayCall(string e) => $"mx_say({e})";
        string LogCall(string e) => $"mx_log({e})";
        string? Hook(string ev) => p.Events.TryGetValue(ev, out var t) ? t : null;
        var lang = p.Language;
        var w = new Code(lang == "gdscript" ? "\t" : lang is "python" ? "    " : "  ");
        var comment = lang is "python" or "gdscript" ? "#" : lang == "lua" ? "--" : "//";
        w.Raw(Header(d, comment));
        foreach (var l in p.Header) w.Line(Fill(l, ""));

        switch (lang)
        {
            case "lua":
            {
                w.Line("local M = {}");
                w.Line($"local function mx_say(text) {(p.Say is null ? "print(tostring(text))" : Fill(p.Say, "text"))} end");
                w.Line($"local function mx_log(text) {(p.Log is null ? "print(tostring(text))" : Fill(p.Log, "text"))} end");
                foreach (var (name, v) in d.Shared) w.Line($"{name} = {L.Literal(v)}");
                Prelude(w, d);
                foreach (var code in NativeCode(d)) w.Block(code);
                Functions(w, d, SayCall, LogCall);
                foreach (var h in d.Hooks)
                {
                    w.Line($"function M.{h.Event}({(h.Event == "tick" ? "dt" : "")})");
                    w.In();
                    Declare(w, [h.Body], h.Event == "tick" ? ["dt"] : []);
                    Body(w, h.Body, SayCall, LogCall);
                    w.Out();
                    w.Line("end");
                }
                Attach(w, d, ev => $"M.{ev}", Hook);
                foreach (var l in p.Footer) w.Line(Fill(l, ""));
                w.Line("return M");
                break;
            }
            case "python":
            {
                w.Line($"def mx_say(text): {(p.Say is null ? "print(text)" : Fill(p.Say, "text"))}");
                w.Line($"def mx_log(text): {(p.Log is null ? "print(text)" : Fill(p.Log, "text"))}");
                foreach (var (name, v) in d.Shared) w.Line($"{name} = {L.Literal(v)}");
                foreach (var name in GameVars(d)) w.Line($"{name} = globals().get({Plain(name)}, 0)");
                Prelude(w, d);
                foreach (var code in NativeCode(d)) w.Block(code);
                Functions(w, d, SayCall, LogCall);
                foreach (var h in d.Hooks)
                {
                    w.Line($"def mx_{h.Event}(*args):");
                    w.In();
                    if (Globals(h.Body, []) is { Count: > 0 } g) w.Line("global " + string.Join(", ", g));
                    Body(w, h.Body, SayCall, LogCall);
                    w.Out();
                }
                w.Line("modx_hooks = {" + string.Join(", ", d.Hooks.Select(h => $"{Plain(h.Event)}: mx_{h.Event}")) + "}");
                Attach(w, d, ev => $"mx_{ev}", Hook);
                foreach (var l in p.Footer) w.Line(Fill(l, ""));
                break;
            }
            case "javascript":
            {
                w.Line("(() => {");
                w.In();
                w.Line($"const mx_say = text => {{ {(p.Say is null ? "console.log(String(text))" : Fill(p.Say, "text"))}; }};");
                w.Line($"const mx_log = text => {{ {(p.Log is null ? "console.log(String(text))" : Fill(p.Log, "text"))}; }};");
                foreach (var (name, v) in d.Shared) w.Line($"globalThis.{name} = {L.Literal(v)};");
                Prelude(w, d);
                foreach (var code in NativeCode(d)) w.Block(code);
                Functions(w, d, SayCall, LogCall);
                foreach (var h in d.Hooks)
                {
                    w.Line($"const mx_{h.Event} = ({(h.Event == "tick" ? "dt" : "")}) => {{");
                    w.In();
                    Declare(w, [h.Body], h.Event == "tick" ? ["dt"] : []);
                    Body(w, h.Body, SayCall, LogCall);
                    w.Out();
                    w.Line("};");
                }
                var hooks = "{ " + string.Join(", ", d.Hooks.Select(h => $"{h.Event}: mx_{h.Event}")) + " }";
                w.Line($"globalThis[{Plain("modx_" + d.Id)}] = {hooks};");
                w.Line($"if (typeof module !== \"undefined\" && module.exports) module.exports = {hooks};");
                Attach(w, d, ev => $"mx_{ev}", Hook);
                foreach (var l in p.Footer) w.Line(Fill(l, ""));
                w.Out();
                w.Line("})();");
                break;
            }
            case "gdscript":
            {
                w.Line($"extends {p.Extends ?? "Node"}");
                w.Line("");
                foreach (var (name, v) in d.Shared) w.Line($"var {name} = {L.Literal(v)}");
                foreach (var name in GameVars(d)) w.Line($"var {name} = 0");
                w.Line("func mx_say(text):");
                w.Line("\t" + (p.Say is null ? "print(text)" : Fill(p.Say, "text")));
                w.Line("func mx_log(text):");
                w.Line("\t" + (p.Log is null ? "print(text)" : Fill(p.Log, "text")));
                Prelude(w, d);
                foreach (var code in NativeCode(d)) w.Block(code);
                Functions(w, d, SayCall, LogCall);
                // В GDScript событие — это функция движка: имя берём из events (по умолчанию как в Godot).
                var names = new Dictionary<string, string> { ["ready"] = "_init", ["start"] = "_ready", ["tick"] = "_process" };
                foreach (var (ev, fn) in p.Events) if (Regex.IsMatch(fn, @"^[A-Za-z_]\w*$")) names[ev] = fn;
                foreach (var ev in ModxDoc.Events)
                {
                    var hooks = d.Hooks.Where(h => h.Event == ev).ToList();
                    if (hooks.Count == 0) continue;
                    var fn = names.GetValueOrDefault(ev, "mx_" + ev);
                    w.Line($"func {fn}({(fn is "_process" or "_physics_process" || ev == "tick" ? "_delta" : "")}):");
                    w.In();
                    Declare(w, hooks.Select(h => h.Body), []);
                    foreach (var h in hooks) Body(w, h.Body, SayCall, LogCall);
                    w.Out();
                }
                foreach (var l in p.Footer) w.Line(Fill(l, ""));
                break;
            }
            default: // csharp
            {
                var cls = "ModScript_" + Regex.Replace(d.Id, @"[^\w]", "_");
                w.Line($"public class {cls}");
                w.Line("{");
                w.In();
                foreach (var (name, v) in d.Shared) w.Line($"public static {Lang.CSharp.TypeOf(v)} {name} = {L.Literal(v)};");
                foreach (var name in GameVars(d)) w.Line($"public static dynamic {name} = 0;");
                w.Line($"static void mx_say(object text) {{ {(p.Say is null ? "System.Console.WriteLine(text)" : Fill(p.Say, "text"))}; }}");
                w.Line($"static void mx_log(object text) {{ {(p.Log is null ? "System.Console.WriteLine(text)" : Fill(p.Log, "text"))}; }}");
                Prelude(w, d);
                foreach (var code in NativeCode(d)) w.Block(code);
                Functions(w, d, SayCall, LogCall);
                // Движок создаёт объект мода и вызывает Ready / Start / Load / Tick(dt) — или подключает их строками events.
                var attach = d.Hooks.Select(h => h.Event).Distinct().Select(ev => Hook(ev)?.Replace("{fn}", Method(ev))).OfType<string>().ToList();
                w.Line($"public {cls}()");
                w.Line("{");
                w.In();
                foreach (var a in attach) w.Line(a.TrimEnd(';') + ";");
                w.Out();
                w.Line("}");
                foreach (var ev in ModxDoc.Events)
                {
                    var hooks = d.Hooks.Where(h => h.Event == ev).ToList();
                    w.Line($"public void {Method(ev)}({(ev == "tick" ? "double dt = 0" : "")})");
                    w.Line("{");
                    w.In();
                    Declare(w, hooks.Select(h => h.Body), ev == "tick" ? ["dt"] : []);
                    foreach (var h in hooks) Body(w, h.Body, SayCall, LogCall);
                    w.Out();
                    w.Line("}");
                }
                w.Out();
                w.Line("}");
                foreach (var l in p.Footer) w.Line(Fill(l, ""));
                break;
            }
        }
        return [new OutFile(p.File.Replace("{id}", d.Id).Replace('\\', '/'), w.ToString())];
    }

    static string Method(string ev) => char.ToUpperInvariant(ev[0]) + ev[1..];

    /// <summary>Строки подключения событий из профиля; ready без строки — вызвать сразу.</summary>
    void Attach(Code w, ModxDoc d, Func<string, string> fn, Func<string, string?> hook)
    {
        var semi = Language == "javascript" ? ";" : "";
        foreach (var ev in d.Hooks.Select(h => h.Event).Distinct())
        {
            var t = hook(ev) ?? (ev == "ready" ? "{fn}()" : null);
            if (t is null || t.Trim() == "") continue;
            w.Line(t.Replace("{fn}", fn(ev)).Replace("{id}", d.Id).TrimEnd(';') + semi);
        }
    }
}
