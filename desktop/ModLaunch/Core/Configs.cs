using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>
/// Конфиг оформления: имя, автор и команды «ключ значение», как autoexec.cfg в CS2.
/// </summary>
public sealed record UiConfig(string Id, string Name, string Author, string About, List<(string Key, string Value)> Lines, bool BuiltIn, string? File = null)
{
    public string? Get(string key) => Lines.LastOrDefault(l => l.Key == key).Value;
    public List<(string Id, string Size)> Widgets() => Lines.Where(l => l.Key == "widget")
        .Select(l => l.Value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
        .Where(p => p.Length > 0).Select(p => (p[0], p.Length > 1 ? p[1] : "s")).ToList();
}

/// <summary>
/// 10.0: конфиги оформления, как в CS2. Файл .mlstyle — обычный текст: одна команда на строку,
/// «//» — комментарий. В нём всё оформление: тема или каждый цвет по отдельности, акцент, шрифт,
/// углы, эффекты «экрана» (строки ЭЛТ, виньетка), анимации и раскладка виджетов главной.
/// Свои конфиги лежат в папке configs (её можно открыть из настроек и положить туда чужие файлы);
/// файл можно и перетащить в окно. Встроенные — «ModLaunch», «Ретро», «Минимал», «Неон», «Бумага»
/// «Терминал» и «Синтвейв».
/// </summary>
public static class Configs
{
    public const string Ext = ".mlstyle";

    public static string Dir => Directory.CreateDirectory(Path.Combine(Paths.DataDir, "configs")).FullName;

    /// <summary>Какой конфиг применён последним (id); после ручной правки — null.</summary>
    public static string? CurrentId => Settings.Data.Str("config");

    static readonly Dictionary<string, string> ColorCommand = Look.ColorKeys.ToDictionary(k => k, k => "color_" + k.ToLowerInvariant());

    // ---------------------------------------------------------------- встроенные

    static UiConfig Make(string id, string author, string text)
    {
        var c = Parse(text, id);
        return c with { Id = id, Name = I18n.T($"v10.cfg.{id}"), About = I18n.T($"v10.cfg.{id}.d"), Author = author, BuiltIn = true };
    }

    public static List<UiConfig> BuiltIn() =>
    [
        // 11.0: по умолчанию — спокойный «взрослый» лаунчер: графит, синий акцент, обычные заголовки, без свечения и игровых счётчиков.
        Make("pro", "ModLaunch", """
            theme slate
            accent "#3B82F6"
            font Inter
            headings ui
            radius normal
            glow 0
            fx_scanlines 0
            fx_vignette 0
            anim_open calm
            anim_launch off
            home_hero stage
            widget continue m
            widget builds s
            widget playtime s
            widget friends m
            widget attention s
            widget autopilot s
            widget downloads m
            """),
        Make("modlaunch", "ModLaunch", """
            theme store
            accent "#7B5CFF"
            font Onest
            radius normal
            fx_scanlines 0
            fx_vignette 0
            anim_open calm
            anim_launch calm
            home_hero stage
            widget continue m
            widget clock s
            widget level s
            widget autopilot s
            widget streak s
            widget friends m
            widget tip m
            """),
        Make("retro", "ModLaunch", """
            // Экран старого монитора: янтарь на чёрном, строки развёртки, квадратные углы.
            theme custom
            color_bg "#0B0A06"
            color_rail "#080704"
            color_layer "#100E08"
            color_surface "#17140B"
            color_surface2 "#1F1B0F"
            color_surface3 "#2A2414"
            color_line "#3A3118"
            color_text "#FFD58A"
            color_muted "#C9A35C"
            color_faint "#8A6F3A"
            accent "#FFB000"
            font Consolas
            radius sharp
            fx_scanlines 1
            fx_vignette 1
            anim_open calm
            anim_launch calm
            home_hero stage
            widget clock m
            widget continue m
            widget playtime s
            widget level s
            widget tip m
            widget autopilot s
            widget streak s
            """),
        Make("minimal", "ModLaunch", """
            // Ничего лишнего: графит, один белый акцент, без картинки на главной.
            theme graphite
            accent "#FFFFFF"
            font Onest
            radius normal
            fx_scanlines 0
            fx_vignette 0
            anim_open calm
            anim_launch calm
            home_hero compact
            widget continue m
            widget clock s
            widget playtime s
            widget autopilot s
            widget level s
            """),
        Make("neon", "ModLaunch", """
            theme jet
            accent "#FF2E88"
            font Onest
            radius round
            fx_scanlines 0
            fx_vignette 1
            anim_open calm
            anim_launch calm
            home_hero stage
            widget continue l
            widget level s
            widget streak s
            widget friends m
            widget clock s
            widget playtime s
            """),
        Make("paper", "ModLaunch", """
            theme snow
            accent "#D7262E"
            font Onest
            radius normal
            fx_scanlines 0
            fx_vignette 0
            anim_open calm
            anim_launch calm
            home_hero stage
            widget continue m
            widget clock s
            widget level s
            widget tip m
            widget playtime s
            widget autopilot s
            """),
        Make("terminal", "ModLaunch", """
            // Зелёный терминал.
            theme custom
            color_bg "#020803"
            color_rail "#010502"
            color_layer "#041006"
            color_surface "#071A0B"
            color_surface2 "#0B2411"
            color_surface3 "#103018"
            color_line "#164220"
            color_text "#9BFFB0"
            color_muted "#5FD47A"
            color_faint "#3A8A4E"
            accent "#33FF66"
            font Consolas
            radius sharp
            fx_scanlines 1
            fx_vignette 1
            anim_open calm
            anim_launch calm
            home_hero compact
            widget clock m
            widget autopilot s
            widget playtime s
            widget continue m
            widget tip m
            """),
        Make("synthwave", "ModLaunch", """
            // Закат восьмидесятых.
            theme custom
            color_bg "#120A1F"
            color_rail "#0E0718"
            color_layer "#170D27"
            color_surface "#1F1233"
            color_surface2 "#29183F"
            color_surface3 "#35204F"
            color_line "#43295F"
            color_text "#FBE7FF"
            color_muted "#C9A4D9"
            color_faint "#8A6A9C"
            accent "#FF6AD5"
            font Onest
            radius round
            fx_scanlines 1
            fx_vignette 1
            anim_open calm
            anim_launch calm
            home_hero stage
            widget continue l
            widget clock s
            widget streak s
            widget level s
            widget playtime s
            """),
    ];

    /// <summary>Встроенные и из папки configs (свои и чужие файлы).</summary>
    public static List<UiConfig> All()
    {
        var list = BuiltIn();
        try
        {
            foreach (var file in Directory.EnumerateFiles(Dir, "*" + Ext).Order(StringComparer.OrdinalIgnoreCase))
                if (Read(file) is { } c) list.Add(c);
        }
        catch { }
        return list;
    }


    // ---------------------------------------------------------------- файл

    public static UiConfig? Read(string path)
    {
        try
        {
            var text = System.IO.File.ReadAllText(path);
            if (text.Length > 64_000) return null;
            var c = Parse(text, Path.GetFileNameWithoutExtension(path));
            return c with { Id = "file:" + Path.GetFileName(path), File = path };
        }
        catch { return null; }
    }

    /// <summary>Разобрать текст: «ключ значение» или «ключ "значение в кавычках"», «//» — комментарий.</summary>
    public static UiConfig Parse(string text, string fallbackName)
    {
        var lines = new List<(string, string)>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            var comment = IndexOfComment(line);
            if (comment >= 0) line = line[..comment].Trim();
            if (line == "") continue;
            var space = line.IndexOfAny([' ', '\t']);
            var key = (space < 0 ? line : line[..space]).Trim().ToLowerInvariant();
            var value = space < 0 ? "" : line[(space + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            else value = value.Replace("\"", "");
            if (key.Length is > 0 and <= 40) lines.Add((key, value.Length > 200 ? value[..200] : value));
        }
        string Get(string k) => lines.LastOrDefault(l => l.Item1 == k).Item2 ?? "";
        var name = Get("name") is { Length: > 0 } n ? n : fallbackName;
        return new UiConfig(name, name, Get("author"), Get("about"), lines, false);
    }

    /// <summary>«//» вне кавычек.</summary>
    static int IndexOfComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (!quoted && line[i] == '/' && line[i + 1] == '/') return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------- применить и снять

    /// <summary>Применить конфиг: неизвестные команды пропускаются, всё остальное — сразу на экране.</summary>
    public static void Apply(UiConfig c, bool apply = true)
    {
        var d = Settings.Data;
        var palette = new JsonObject();
        foreach (var (key, value) in c.Lines)
        {
            switch (key)
            {
                case "theme": d["theme"] = value; break;
                case "accent" when value.StartsWith('#') && value.Length == 7 && Avalonia.Media.Color.TryParse(value, out _): d["accent"] = value.ToUpperInvariant(); break;
                case "font" when Look.Fonts.Contains(value): d["font"] = value; break;
                case "radius" when Look.Radii.Contains(value): d["radius"] = value; break;
                case "backdrop" when Look.Backdrops.Contains(value): d["backdrop"] = value; break;
                case "scale" when value == "auto": d["uiScale"] = "auto"; break;
                case "scale" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var k) && k is >= 0.7 and <= 1.6: d["uiScale"] = k; break;
                case "fx_scanlines": d["fxScanlines"] = On(value); break;
                case "fx_vignette": d["fxVignette"] = On(value); break;
                case "anim": d["animations"] = On(value); break;
                case "anim_open" when value is "calm" or "classic" or "off": d["gameOpenStyle"] = value; break;
                case "anim_launch" when value is "calm" or "off": d["launchFx"] = value; break;
                case "anim_lore": d["loreFx"] = On(value); break;
                case "home_hero" when value is "stage" or "compact" or "off": d["homeHero"] = value; break;
                case "headings" when value is "ui" or "wide": d["headings"] = value; break;
                case "glow" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && g is >= 0 and <= 1: d["glow"] = g; break;
                default:
                    if (ColorCommand.FirstOrDefault(x => x.Value == key).Key is { } color && Avalonia.Media.Color.TryParse(value, out _)) palette[color] = value;
                    break;
            }
        }
        if (d.Str("theme") == "custom" || palette.Count > 0)
        {
            d["customBase"] = d.Str("theme") is { } t && t != "custom" && Look.Themes.Contains(t) ? t : d.Str("customBase") ?? "store";
            d["customPalette"] = palette;
            d["theme"] = "custom";
        }
        // Конфиги 10.0 не знают заголовков и свечения — у них вид 10.0 (широкие заголовки, свечение).
        if (c.Get("headings") is null) d["headings"] = "wide";
        if (c.Get("glow") is null) d["glow"] = 1;
        var widgets = c.Widgets();
        if (widgets.Count > 0) Widgets.Set(widgets);
        d["design"] = "store";
        d["config"] = c.Id;
        Settings.Save();
        if (!apply) return;
        Look.Apply();
        Widgets.Notify();
    }

    static bool On(string v) => v is "1" or "true" or "on" or "yes";

    // ---------------------------------------------------------------- снять текущее

    /// <summary>Текущее оформление одним конфигом — чтобы сохранить, отправить другу или поправить в Блокноте.</summary>
    public static string Capture(string name, string? author = null)
    {
        var d = Settings.Data;
        var sb = new StringBuilder();
        sb.AppendLine("// ModLaunch config — " + name);
        sb.AppendLine("// Каждая строка — команда «ключ значение», как autoexec.cfg в CS2. Можно править в Блокноте.");
        sb.AppendLine($"name \"{name.Replace("\"", "")}\"");
        if (!string.IsNullOrWhiteSpace(author)) sb.AppendLine($"author \"{author.Replace("\"", "")}\"");
        sb.AppendLine("theme " + Look.Theme);
        if (Look.Theme == "custom")
            foreach (var (key, cmd) in ColorCommand) sb.AppendLine($"{cmd} \"{Look.Current[key]}\"");
        sb.AppendLine($"accent \"{Look.Accent}\"");
        sb.AppendLine($"font \"{Look.Font}\"");
        sb.AppendLine("radius " + Look.Radius);
        sb.AppendLine("scale " + (Look.AutoScale ? "auto" : Look.Scale.ToString(CultureInfo.InvariantCulture)));
        sb.AppendLine("fx_scanlines " + (Look.Scanlines ? 1 : 0));
        sb.AppendLine("fx_vignette " + (Look.Vignette ? 1 : 0));
        sb.AppendLine("anim " + (Look.Animations ? 1 : 0));
        sb.AppendLine("anim_open " + (d.Str("gameOpenStyle") ?? "calm"));
        sb.AppendLine("anim_launch " + (d.Str("launchFx") ?? "calm"));
        sb.AppendLine("anim_lore " + (Lore.Enabled ? 1 : 0));
        sb.AppendLine("home_hero " + (d.Str("homeHero") ?? "stage"));
        sb.AppendLine("headings " + (Look.WideHeadings ? "wide" : "ui"));
        sb.AppendLine("glow " + Look.Glow.ToString(CultureInfo.InvariantCulture));
        foreach (var (id, size) in Widgets.Layout()) sb.AppendLine($"widget {id} {size}");
        return sb.ToString();
    }

    /// <summary>Сохранить текущее оформление в папку configs. Возвращает путь к файлу.</summary>
    public static string SaveCurrent(string name)
    {
        var safe = string.Concat(name.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        if (safe == "") safe = "config";
        var path = Path.Combine(Dir, safe + Ext);
        System.IO.File.WriteAllText(path, Capture(name.Trim() == "" ? safe : name.Trim(), Social.Account.SignedIn ? Social.Account.Get().Name : null));
        Settings.Data["config"] = "file:" + Path.GetFileName(path);
        Settings.Save();
        return path;
    }

    /// <summary>Чужой файл: скопировать в папку configs и сразу применить.</summary>
    public static UiConfig? Import(string path)
    {
        if (Read(path) is not { } c || c.Lines.Count == 0) return null;
        var target = Path.Combine(Dir, Path.GetFileName(path));
        try { if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) System.IO.File.Copy(path, target, true); } catch { }
        var copy = Read(target) ?? c;
        Apply(copy);
        return copy;
    }

    public static void Delete(UiConfig c)
    {
        if (c.File is null) return;
        try { System.IO.File.Delete(c.File); } catch { }
        if (CurrentId == c.Id) { Settings.Data.Remove("config"); Settings.Save(); }
    }

    /// <summary>Цвета для мини-экрана на карточке конфига (без применения).</summary>
    public static (Dictionary<string, string> Palette, string Accent, bool Scanlines, string Radius) Preview(UiConfig c)
    {
        var theme = c.Get("theme") ?? "store";
        var p = new Dictionary<string, string>(Look.Palette(Look.Themes.Contains(theme) ? theme : "store"));
        foreach (var (key, cmd) in ColorCommand)
            if (c.Get(cmd) is { } hex && Avalonia.Media.Color.TryParse(hex, out _)) p[key] = hex;
        var accent = c.Get("accent") is { Length: 7 } a && Avalonia.Media.Color.TryParse(a, out _) ? a : Look.Accent;
        return (p, accent, On(c.Get("fx_scanlines") ?? "0"), c.Get("radius") ?? "normal");
    }

    [SelfTest]
    static string ConfigsRoundTrip()
    {
        var c = Parse("""
            // comment
            name "Мой // стиль"
            theme custom
            color_bg "#101010" // trailing
            accent #FF0000
            widget clock m
            widget level
            """, "x");
        if (c.Name != "Мой // стиль" || c.Get("color_bg") != "#101010" || c.Get("accent") != "#FF0000") throw new Exception("parse: " + string.Join(";", c.Lines));
        var w = c.Widgets();
        if (w.Count != 2 || w[0] != ("clock", "m") || w[1] != ("level", "s")) throw new Exception("widgets");
        if (BuiltIn().Any(b => b.Lines.Count < 5)) throw new Exception("built-in config is empty");
        return $"cfg parsed, {BuiltIn().Count} built-in configs";
    }
}
