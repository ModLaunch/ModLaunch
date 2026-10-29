using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace ModLaunch.Core;

/// <summary>
/// Внешний вид: тема, цвет акцента, масштаб, анимации. Цвета меняются прямо в
/// кистях ресурсов App.axaml — все экраны перекрашиваются без перезапуска.
/// </summary>
public static class Look
{
    public static readonly string[] Themes = [
        // 2.0: «Φ» — тёмный графит и светлый вариант к нему (оформление 2.0 по умолчанию).
        "phi", "phi-light",
        "slate", "store", "snow", "jet", "ink", "graphite", "paper", "dark", "black", "light", "midnight", "nord", "forest", "grape", "mocha", "sand", "contrast",
        // 17.0: восемь новых — пять тёмных и три светлых.
        "ocean", "crimson", "cyber", "olive", "rose", "mint", "sky", "blossom"];
    public static readonly string[] Designs = ["store", "vitrina", "studio", "aurora"];

    /// <summary>
    /// 9.2: три готовых оформления в духе Microsoft Store — тема и цвет акцента одним нажатием.
    /// «Тёмная» (графит и фиолетовый), «Красно-белая» и «Чёрно-зелёная».
    /// </summary>
    public sealed record LookStyle(string Id, string Theme, string Accent);
    public static readonly LookStyle[] Styles =
    [
        new("dark", "store", "#7B5CFF"),
        new("redwhite", "snow", "#D7262E"),
        new("blackgreen", "jet", "#2BD46F"),
    ];

    /// <summary>Какое из трёх оформлений сейчас включено (null — своя тема или акцент).</summary>
    public static LookStyle? CurrentStyle => Styles.FirstOrDefault(s => s.Theme == Theme && s.Accent.Equals(Accent, StringComparison.OrdinalIgnoreCase));

    public static void SetStyle(string id)
    {
        if (Styles.FirstOrDefault(s => s.Id == id) is not { } style) return;
        Settings.Data["design"] = "store";
        Settings.Data["theme"] = style.Theme;
        Settings.Data["accent"] = style.Accent;
        Settings.Save();
        Apply();
    }

    /// <summary>
    /// Дизайн (8.3): studio — строгий, в духе студийной школы (типографика и воздух
    /// вместо украшений, тонкие контуры, монохром и один акцент); aurora — живой фон
    /// и стеклянные карточки 8.1–8.2.
    /// </summary>
    public static string Design => Designs.Contains(Settings.Data.Str("design")) ? Settings.Data.Str("design")! : "store";
    /// <summary>Строгие дизайны без живого фона и стекла: Store (9.2), «Витрина» (9.0) и «Студия» (8.3).</summary>
    public static bool Studio => Design is "studio" or "vitrina" or "store";
    /// <summary>
    /// Store (9.2): как Microsoft Store — подписанная боковая панель, поиск по центру шапки,
    /// содержимое на отдельном «листе» со скруглённым углом, большие арты, ровная сетка.
    /// Строится поверх правил «Витрины».
    /// </summary>
    public static bool Store => Design == "store";
    /// <summary>
    /// «Витрина» (9.0): игра на первом плане — большие арты, обложки 2:3 на боковой панели,
    /// один фиолетовый акцент на главных действиях, шрифты Onest и Unbounded.
    /// </summary>
    public static bool Vitrina => Design is "vitrina" or "store";
    public static void SetDesign(string value)
    {
        Settings.Data["design"] = value;
        // К каждому дизайну — своя «родная» тема, если человек не выбрал другую.
        if (value == "studio" && Theme is "dark" or "ink") Settings.Data["theme"] = "graphite";
        if (value == "aurora" && Theme is "graphite" or "ink") Settings.Data["theme"] = "dark";
        if (value == "vitrina" && Theme is "graphite" or "dark") Settings.Data["theme"] = "ink";
        if (value != "store" && Theme is "store" or "snow" or "jet") Settings.Data["theme"] = value == "studio" ? "graphite" : value == "aurora" ? "dark" : "ink";
        if (value == "store" && Theme is "ink" or "graphite" or "dark") { Settings.Data["theme"] = "store"; if (Accent is "#6E4BFF" or "#7C5CFF") Settings.Data["accent"] = "#7B5CFF"; }
        Settings.Save();
        Apply();
    }

    /// <summary>
    /// 2.0: сначала прежние переходы (9.0 → 11.0), затем — оформление «Φ» тем, у кого вид не меняли:
    /// стандартный конфиг 11.0 («Строгий») или ничего. Своя тема, акцент или чужой конфиг остаются, раскладка главной — тоже.
    /// Рамка окна, шрифты и отступы 2.0 — у всех (их задаёт AddPhi, а не конфиг).
    /// </summary>
    static void Migrate()
    {
        MigrateOld();
        if (Settings.Data.Bool("look2")) return;
        Settings.Data["look2"] = true;
        if (Look2Untouched()) Configs.Apply(Phi2("widget"), apply: false);
        Settings.Save();
    }

    /// <summary>Вид 11.0 не трогали: конфиг «Строгий» (или никакой) и его тема, акцент и шрифт.</summary>
    static bool Look2Untouched()
    {
        var d = Settings.Data;
        return d.Str("config") is null or "pro" or "modlaunch"
            && d.Str("theme") is null or "slate" or "store"
            && d.Str("accent")?.ToUpperInvariant() is null or "#3B82F6" or "#7B5CFF"
            && d.Str("font") is null or "Inter" or "Onest"
            && d["customPalette"] is null;
    }

    /// <summary>Конфиг «ModLaunch 2.0» без части строк (widget…) — чтобы не трогать раскладку главной.</summary>
    public static UiConfig Phi2(params string[] skip)
    {
        var phi = Configs.BuiltIn().First(c => c.Id == "phi");
        return phi with { Lines = phi.Lines.Where(l => !skip.Contains(l.Key)).ToList() };
    }

    /// <summary>Первый запуск 8.3: включить «Студию» и её тему, если оформление не меняли.</summary>
    static void MigrateOld()
    {
        // 11.0: «взрослый» вид по умолчанию — конфиг «Строгий», если оформление не меняли (стандартный конфиг 10.0 или ничего).
        if (!Settings.Data.Bool("look11"))
        {
            Settings.Data["look11"] = true;
            // Выбор оформления 9.2 больше не показываем: оформление теперь — конфиги в «Внешнем виде».
            Settings.Data["stylePicked92"] = true;
            // Классическая главная 9.x убрана из настроек — у всех главная 10.0+.
            Settings.Data.Remove("homeStyle");
            // Свои тема, шрифт, углы, свечение или анимации остаются как были (вид 10.0: широкие заголовки и свечение — по умолчанию).
            if (LookUntouched())
            {
                Settings.Data["look92"] = true;
                Settings.Data["look9"] = true;
                Settings.Data["showBrand"] = false;
                // «Спокойное» движение целиком: без заставки и эффекта открытия мода, если их не выбирали сами.
                if (Settings.Data["splash"] is null) Settings.Data["splash"] = false;
                if (Settings.Data["modOpenFx"] is null) Settings.Data["modOpenFx"] = false;
                // Раскладку главной не трогаем: без неё и так стандартная раскладка 11.0, а свою не стираем.
                Configs.Apply(Pro("widget"), apply: false);
                return;
            }
        }
        // 9.2: всем — дизайн Store. Тёмные темы прошлых версий и их акцент меняем на «Тёмное» оформление Store.
        if (!Settings.Data.Bool("look92"))
        {
            Settings.Data["look92"] = true;
            Settings.Data["look9"] = true;
            Settings.Data["design"] = "store";
            if (Settings.Data.Str("theme") is null or "ink" or "dark" or "graphite") Settings.Data["theme"] = "store";
            if (Settings.Data.Str("accent") is null or "#6E4BFF" or "#7C5CFF" or "#F2483A") Settings.Data["accent"] = "#7B5CFF";
            if (Settings.Data.Str("font") is null or "Inter") Settings.Data["font"] = "Onest";
            Settings.Data["showBrand"] = false;
            Settings.Save();
            return;
        }
        // 9.0: всем — «Витрина». Тему и акцент меняем, только если они были стандартными для прежних версий.
        if (Settings.Data.Bool("look9")) return;
        Settings.Data["look9"] = true;
        Settings.Data["design"] = "vitrina";
        if (Settings.Data.Str("theme") is null or "dark" or "graphite") Settings.Data["theme"] = "ink";
        if (Settings.Data.Str("accent") is null or "#7C5CFF" or "#F2483A") Settings.Data["accent"] = "#6E4BFF";
        if (Settings.Data.Str("font") is null or "Inter") Settings.Data["font"] = "Onest";
        Settings.Data["showBrand"] = false;
        Settings.Save();
    }

    /// <summary>
    /// 11.0: оформление не меняли — каждый ключ, который пишет «Строгий», не задан или стоит как в 10.0.
    /// До 9.2 стандартными считаются и тема, акцент и шрифт прошлых версий: их всё равно сменил бы переход 9.2.
    /// </summary>
    static bool LookUntouched()
    {
        var d = Settings.Data;
        var old = !d.Bool("look92");
        return d.Str("config") is null or "modlaunch"
            && (d.Str("theme") is null or "store" || old && d.Str("theme") is "ink" or "dark" or "graphite")
            && (d.Str("accent")?.ToUpperInvariant() is null or "#7B5CFF" || old && d.Str("accent")?.ToUpperInvariant() is "#6E4BFF" or "#7C5CFF" or "#F2483A")
            && (d.Str("font") is null or "Onest" || old && d.Str("font") == "Inter")
            && d.Str("radius") is null or "normal"
            && d.Str("headings") is null or "ui"
            && Glow >= 1
            && !d.Bool("fxScanlines") && !d.Bool("fxVignette")
            && d.Str("gameOpenStyle") is null or "calm" && d.Bool("gameOpenFx", true)
            && d.Str("launchFx") is null or "calm"
            && d.Str("homeHero") is null or "stage";
    }

    /// <summary>Конфиг «Строгий» без части строк (widget, home_hero…) — чтобы не трогать то, что к оформлению не относится.</summary>
    public static UiConfig Pro(params string[] skip)
    {
        var pro = Configs.BuiltIn().First(c => c.Id == "pro");
        return pro with { Lines = pro.Lines.Where(l => !skip.Contains(l.Key)).ToList() };
    }

    [SelfTest]
    static string Look11KeepsCustomizations()
    {
        var saved = (System.Text.Json.Nodes.JsonObject)Settings.Data.DeepClone();
        string? Run(Action<System.Text.Json.Nodes.JsonObject> setup, Func<System.Text.Json.Nodes.JsonObject, bool> ok)
        {
            Settings.Data.Clear();
            setup(Settings.Data);
            MigrateOld();
            return ok(Settings.Data) ? null : Settings.Data.ToJsonString();
        }
        try
        {
            var widgets = new System.Text.Json.Nodes.JsonArray("clock m", "level s");
            var fails = new[]
            {
                // Первый запуск: «Строгий» и «Спокойное» движение, раскладка главной — стандартная 11.0.
                ("fresh", Run(_ => { }, d => d.Str("theme") == "slate" && d.Str("headings") == "ui" && d["widgets"] is null && !d.Bool("splash", true))),
                // 10.0 со стандартным видом, но своей раскладкой главной: вид меняется, раскладка — нет.
                ("widgets", Run(d => { d["look92"] = true; d["look9"] = true; d["config"] = "modlaunch"; d["widgets"] = widgets.DeepClone(); },
                    d => d.Str("theme") == "slate" && d["widgets"]?.ToJsonString() == widgets.ToJsonString())),
                // Свой шрифт или анимация в 10.0 — «Строгий» не включается.
                ("font", Run(d => { d["look92"] = true; d["look9"] = true; d["theme"] = "store"; d["font"] = "Segoe UI"; }, d => d.Str("theme") == "store" && d.Str("font") == "Segoe UI" && d["headings"] is null)),
                ("anim", Run(d => { d["look92"] = true; d["look9"] = true; d["gameOpenStyle"] = "classic"; }, d => d["theme"] is null && d.Str("gameOpenStyle") == "classic")),
                // 9.1 со своей темой и акцентом — их сохраняет переход 9.2, а не заменяет 11.0.
                ("9.1 custom", Run(d => { d["look9"] = true; d["theme"] = "light"; d["accent"] = "#22C55E"; }, d => d.Str("theme") == "light" && d.Str("accent") == "#22C55E")),
                // 9.1 со стандартным видом 9.0 — «Строгий».
                ("9.1 default", Run(d => { d["look9"] = true; d["theme"] = "ink"; d["accent"] = "#6E4BFF"; d["font"] = "Onest"; }, d => d.Str("theme") == "slate")),
            }.Where(f => f.Item2 is not null).ToList();
            if (fails.Count > 0) throw new Exception(string.Join("; ", fails.Select(f => $"{f.Item1}: {f.Item2}")));
            return "look11 switches only untouched looks, keeps widgets";
        }
        finally
        {
            Settings.Data.Clear();
            foreach (var key in saved.Select(kv => kv.Key).ToList())
            {
                var v = saved[key];
                saved.Remove(key);
                Settings.Data[key] = v;
            }
            Settings.Save();
        }
    }

    [SelfTest]
    static string Look2SwitchesOnlyDefault()
    {
        var saved = (System.Text.Json.Nodes.JsonObject)Settings.Data.DeepClone();
        string? Run(Action<System.Text.Json.Nodes.JsonObject> setup, Func<System.Text.Json.Nodes.JsonObject, bool> ok)
        {
            Settings.Data.Clear();
            setup(Settings.Data);
            Migrate();
            return ok(Settings.Data) ? null : Settings.Data.ToJsonString();
        }
        try
        {
            var widgets = new System.Text.Json.Nodes.JsonArray("clock m", "level s");
            var fails = new[]
            {
                ("fresh", Run(_ => { }, d => d.Str("theme") == "phi" && d.Str("config") == "phi" && d.Bool("look2"))),
                // Стандартный «Строгий» 11.0–20.0 со своей раскладкой главной: вид — «Φ», раскладка — своя.
                ("pro", Run(d => { d["look11"] = true; d["look92"] = true; d["look9"] = true; d["config"] = "pro"; d["theme"] = "slate"; d["accent"] = "#3B82F6"; d["widgets"] = widgets.DeepClone(); },
                    d => d.Str("theme") == "phi" && d["widgets"]?.ToJsonString() == widgets.ToJsonString())),
                // Свой акцент или чужой конфиг — остаются.
                ("accent", Run(d => { d["look11"] = true; d["look92"] = true; d["look9"] = true; d["config"] = "pro"; d["theme"] = "slate"; d["accent"] = "#22C55E"; },
                    d => d.Str("theme") == "slate" && d.Str("accent") == "#22C55E")),
                ("retro", Run(d => { d["look11"] = true; d["look92"] = true; d["look9"] = true; d["config"] = "retro"; d["theme"] = "jet"; }, d => d.Str("theme") == "jet" && d.Str("config") == "retro")),
            }.Where(f => f.Item2 is not null).ToList();
            if (fails.Count > 0) throw new Exception(string.Join("; ", fails.Select(f => $"{f.Item1}: {f.Item2}")));
            return "2.0 look only for untouched looks, home layout kept";
        }
        finally
        {
            Settings.Data.Clear();
            foreach (var key in saved.Select(kv => kv.Key).ToList())
            {
                var v = saved[key];
                saved.Remove(key);
                Settings.Data[key] = v;
            }
            Settings.Save();
        }
    }

    public static readonly string[] Accents = [
        // 2.0: лавандовый с логотипа ModLaunch (белый текст на нём читается, контраст 4,7 : 1) и золото его четвёртой точки.
        "#6D5EF0", "#F2B544",
        "#7B5CFF", "#D7262E", "#2BD46F", "#6E4BFF", "#F2483A", "#7C5CFF","#3B82F6", "#14B8A6", "#22C55E", "#EAB308", "#F97316", "#EF4444", "#EC4899", "#06B6D4", "#A3E635", "#F43F5E", "#8B5CF6", "#1BD96A", "#0078D4", "#FFFFFF",
        // 17.0: коралл, аква, небесный, лаванда, мандарин, сталь, мята и вишня.
        "#FF7A59", "#00C2A8", "#38BDF8", "#B79CFF", "#FFA94D", "#94A3B8", "#5EEAD4", "#E11D74"];
    public static readonly string[] Fonts = ["Onest", "Inter", "Segoe UI", "Segoe UI Variable Display", "Arial", "Verdana", "Bahnschrift", "Consolas", "Cascadia Code"];
    public static readonly string[] Radii = ["sharp", "normal", "round"];
    public static readonly string[] Backdrops = ["aurora", "soft", "grid", "plain"];
    /// <summary>Фон окна: aurora — живые пятна и сетка, soft — пятна без движения, grid — сетка, plain — ровный.</summary>
    public static string Backdrop => Backdrops.Contains(Settings.Data.Str("backdrop")) ? Settings.Data.Str("backdrop")! : Mature ? "soft" : "aurora";
    public static void SetBackdrop(string value) { Settings.Data["backdrop"] = value; Settings.Save(); Apply(); }

    /// <summary>Шрифт интерфейса (Inter встроен, остальные — из Windows).</summary>
    public static string Font => Fonts.Contains(Settings.Data.Str("font")) ? Settings.Data.Str("font")! : "Onest";
    /// <summary>Встроенные шрифты: Onest (текст) и Unbounded (крупные заголовки «Витрины»).</summary>
    /// <summary>11.0: заголовки — широким Unbounded (как в 9.x) или тем же шрифтом, что и текст («Строгий»).</summary>
    public static bool WideHeadings => Settings.Data.Str("headings") != "ui";
    public static FontFamily HeadingFont => WideHeadings ? Unbounded : UiFont;
    /// <summary>«Строгий» вид: обычные заголовки, меньше скруглений, без свечения у кнопок и пунктов меню.</summary>
    public static bool Calm => !WideHeadings;

    public static readonly FontFamily Onest = new("avares://ModLaunch/Assets/fonts#Onest");
    public static readonly FontFamily Unbounded = new("avares://ModLaunch/Assets/fonts#Unbounded");
    /// <summary>Шрифт крупных заголовков: в «Витрине» — Unbounded, иначе — шрифт интерфейса.</summary>
    public static FontFamily Display => Store ? UiFont : Vitrina ? Unbounded : UiFont;
    public static FontFamily UiFont => Font switch
    {
        "Onest" => Onest,
        "Inter" => new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"),
        var f => new FontFamily(f),
    };
    /// <summary>Скругление углов: sharp — почти квадратные, round — мягкие.</summary>
    public static string Radius => Radii.Contains(Settings.Data.Str("radius")) ? Settings.Data.Str("radius")! : "normal";
    /// <summary>Сила цветного свечения фона, 0…1.</summary>
    public static double Glow => Settings.Data["glow"] is { } g && double.TryParse(g.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 1) : 1;
    /// <summary>Светлые темы (для подсветки кода и иконок).</summary>
    /// <summary>Светлая тема — по яркости фона (17.0: так и новые светлые темы, и свои палитры считаются верно).</summary>
    public static bool IsLight => Luma(Color.Parse(Current["Bg"])) > 0.55;

    public static void SetFont(string font) { Settings.Data["font"] = font; Settings.Save(); Apply(); }
    public static void SetRadius(string radius) { Settings.Data["radius"] = radius; Settings.Save(); Apply(); }
    public static void SetGlow(double glow) { Settings.Data["glow"] = Math.Round(glow, 2); Settings.Save(); GlowChanged?.Invoke(); }
    public static event Action? GlowChanged;

    /// <summary>Оформление одной строкой — поделиться с друзьями: ML-LOOK:base64.</summary>
    public static string Export()
    {
        var o = new System.Text.Json.Nodes.JsonObject { ["theme"] = Theme, ["accent"] = Accent, ["font"] = Font, ["radius"] = Radius, ["glow"] = Glow, ["animations"] = Animations, ["backdrop"] = Backdrop };
        return "ML-LOOK:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(o.ToJsonString()));
    }

    public static bool Import(string code)
    {
        try
        {
            code = code.Trim();
            if (!code.StartsWith("ML-LOOK:")) return false;
            var o = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(code[8..])));
            if (o is null) return false;
            foreach (var key in new[] { "theme", "accent", "font", "radius", "glow", "animations", "backdrop" })
                if (o[key] is { } v) Settings.Data[key] = v.DeepClone();
            Settings.Save();
            Apply();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Сохранённые наборы оформления: имя → код.</summary>
    public static Dictionary<string, string> Presets() =>
        Settings.Data.Obj("lookPresets").ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
    public static void SavePreset(string name) { Settings.Data.Obj("lookPresets")[name] = Export(); Settings.Save(); }
    public static void DeletePreset(string name) { Settings.Data.Obj("lookPresets").Remove(name); Settings.Save(); }

    /// <summary>Сбросить оформление к стандартному.</summary>
    public static void Reset()
    {
        foreach (var key in new[] { "theme", "accent", "font", "radius", "glow", "uiScale", "animations", "backdrop", "customPalette", "customBase", "fxScanlines", "fxVignette", "config" }) Settings.Data.Remove(key);
        Settings.Save();
        Apply();
    }
    public static readonly double[] Scales = [0.85, 0.9, 1.0, 1.1, 1.25];

    public static string Theme => Settings.Data.Str("theme") == "custom" && Custom() is not null ? "custom" : Themes.Contains(Settings.Data.Str("theme")) ? Settings.Data.Str("theme")! : "phi";

    /// <summary>10.0: ключи цветов темы — их можно задать по одному в конфиге (color_bg, color_text…).</summary>
    public static readonly string[] ColorKeys = ["Bg", "Rail", "Layer", "Surface", "Surface2", "Surface3", "Line", "Text", "Muted", "Faint"];

    /// <summary>10.0: своя тема из конфига — все цвета заданы вручную (settings.customPalette).</summary>
    public static Dictionary<string, string>? Custom()
    {
        if (Settings.Data["customPalette"] is not System.Text.Json.Nodes.JsonObject o) return null;
        var basis = Palette(Settings.Data.Str("customBase") is { } b && Palettes.ContainsKey(b) ? b : "store");
        var p = new Dictionary<string, string>(basis);
        foreach (var key in ColorKeys)
            if (o.Str(key) is { } hex && Color.TryParse(hex, out _)) p[key] = hex;
        return p;
    }

    /// <summary>Цвета, которые сейчас на экране (своя тема или встроенная).</summary>
    public static IReadOnlyDictionary<string, string> Current => Theme == "custom" ? Custom()! : Palette(Theme);

    /// <summary>Поменять один цвет: тема становится своей, остальные цвета берутся из текущей.</summary>
    public static void SetColor(string key, string hex)
    {
        if (!ColorKeys.Contains(key) || !Color.TryParse(hex, out _)) return;
        var now = Current;
        var o = new System.Text.Json.Nodes.JsonObject();
        foreach (var k in ColorKeys) o[k] = now.TryGetValue(k, out var v) ? v : null;
        o[key] = hex;
        if (Theme != "custom") Settings.Data["customBase"] = Theme;
        Settings.Data["customPalette"] = o;
        Settings.Data["theme"] = "custom";
        Settings.Save();
        Apply();
    }

    /// <summary>10.0: эффекты «экрана» поверх окна — строки развёртки (как у ЭЛТ) и затемнение по краям.</summary>
    public static bool Scanlines => Settings.Data.Bool("fxScanlines");
    public static bool Vignette => Settings.Data.Bool("fxVignette");
    public static void SetEffect(string key, bool on) { Settings.Data[key] = on; Settings.Save(); Changed?.Invoke(); }
    public static string Accent => Settings.Data.Str("accent") is string a && a.StartsWith('#') && a.Length == 7 ? a : Accents[0];
    /// <summary>«Авто»: на широких мониторах интерфейс крупнее, чтобы не было пустых полей.</summary>
    public static bool AutoScale => Settings.Data["uiScale"] is null || Settings.Data.Str("uiScale") == "auto";
    public static void SetAutoScale() { Settings.Data["uiScale"] = "auto"; Settings.Save(); Changed?.Invoke(); }
    public static double Scale => Settings.Data["uiScale"] is { } v && double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s is >= 0.7 and <= 1.6 ? s : 1.0;
    public static bool Animations => Settings.Data.Bool("animations", true);

    /// <summary>
    /// 14.0: «взрослый» вид — по умолчанию у всех. Цвета виджетов приглушены, цифры не кричат,
    /// в Creator Hub — сталь вместо фиолетово-бирюзового, без свечения. Выключается в «Тонкой настройке».
    /// </summary>
    public static bool Mature => Settings.Data.Bool("mature", true);
    public static void SetMature(bool on) { Settings.Data["mature"] = on; Settings.Save(); Changed?.Invoke(); }

    public static event Action? Changed;

    static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
    {
        // 2.0 «Φ»: почти чёрный графит без синевы. Шаги светлоты Bg → Layer → Surface → Surface2 → Surface3 одинаковые,
        // линия — едва заметнее листа: иерархию задают поверхность и шрифт, а не рамки и цвет.
        ["phi"] = new()
        {
            ["Bg"] = "#0B0B0D", ["Rail"] = "#0B0B0D", ["Layer"] = "#111114", ["Surface"] = "#17171B", ["Surface2"] = "#1E1E23", ["Surface3"] = "#28282E",
            ["Line"] = "#24242A", ["Text"] = "#EEEEF0", ["Muted"] = "#A0A0AA", ["Faint"] = "#68686F",
        },
        ["phi-light"] = new()
        {
            ["Bg"] = "#F2F2F4", ["Rail"] = "#F2F2F4", ["Layer"] = "#FFFFFF", ["Surface"] = "#FFFFFF", ["Surface2"] = "#F4F4F6", ["Surface3"] = "#E9E9EE",
            ["Line"] = "#E3E3E8", ["Text"] = "#141417", ["Muted"] = "#5F5F69", ["Faint"] = "#94949D",
        },
        // 11.0 «Строгий»: холодный графит без фиолетового оттенка — спокойный фон, на котором работают арты игр и один акцент.
        ["slate"] = new()
        {
            ["Bg"] = "#111315", ["Rail"] = "#111315", ["Layer"] = "#16191C", ["Surface"] = "#1C2024", ["Surface2"] = "#23282D", ["Surface3"] = "#2C3238",
            ["Line"] = "#272C32", ["Text"] = "#EDEFF2", ["Muted"] = "#A2AAB4", ["Faint"] = "#6D7680",
        },
        // 9.2 Store. Bg — шапка и боковая панель, Layer — «лист» с содержимым, Surface — карточки.
        ["store"] = new()
        {
            ["Bg"] = "#17171A", ["Rail"] = "#17171A", ["Layer"] = "#1E1E22", ["Surface"] = "#26262B", ["Surface2"] = "#2E2E34", ["Surface3"] = "#38383F",
            ["Line"] = "#303036", ["Text"] = "#F4F4F6", ["Muted"] = "#B1B1BA", ["Faint"] = "#7A7A85",
        },
        ["snow"] = new()
        {
            ["Bg"] = "#ECECEF", ["Rail"] = "#ECECEF", ["Layer"] = "#F7F7F8", ["Surface"] = "#FFFFFF", ["Surface2"] = "#F2F2F4", ["Surface3"] = "#E6E6EA",
            ["Line"] = "#E0E0E5", ["Text"] = "#17171A", ["Muted"] = "#5B5B65", ["Faint"] = "#93939D",
        },
        ["jet"] = new()
        {
            ["Bg"] = "#000000", ["Rail"] = "#000000", ["Layer"] = "#070908", ["Surface"] = "#0F1211", ["Surface2"] = "#161A18", ["Surface3"] = "#1F2421",
            ["Line"] = "#1B201D", ["Text"] = "#F0F5F1", ["Muted"] = "#9BA79F", ["Faint"] = "#646F68",
        },
        // «Витрина» (9.0): почти чёрный с лёгкой синевой — на нём сочно смотрятся арты игр и фиолетовый акцент.
        ["ink"] = new()
        {
            ["Bg"] = "#0B0C10", ["Rail"] = "#07080B", ["Surface"] = "#12141A", ["Surface2"] = "#191B23", ["Surface3"] = "#22252F",
            ["Line"] = "#22252E", ["Text"] = "#ECEEF4", ["Muted"] = "#9AA2B4", ["Faint"] = "#6C7385",
        },
        // «Студия»: нейтральный графит и бумага — без синевы, чтобы работали типографика и один акцент.
        ["graphite"] = new()
        {
            ["Bg"] = "#111112", ["Rail"] = "#0C0C0D", ["Surface"] = "#18181A", ["Surface2"] = "#1F1F22", ["Surface3"] = "#29292D",
            ["Line"] = "#2B2B2F", ["Text"] = "#F3F3F1", ["Muted"] = "#9C9C98", ["Faint"] = "#6A6A67",
        },
        ["paper"] = new()
        {
            ["Bg"] = "#F7F6F2", ["Rail"] = "#EFEDE7", ["Surface"] = "#FFFFFF", ["Surface2"] = "#F2F0EB", ["Surface3"] = "#E7E4DC",
            ["Line"] = "#E2DFD7", ["Text"] = "#141414", ["Muted"] = "#62605B", ["Faint"] = "#9A978F",
        },
        ["dark"] = new()
        {
            ["Bg"] = "#0F1116", ["Rail"] = "#12141A", ["Surface"] = "#171A21", ["Surface2"] = "#1E222B", ["Surface3"] = "#262B36",
            ["Line"] = "#2A2F3B", ["Text"] = "#E8EBF2", ["Muted"] = "#98A1B2", ["Faint"] = "#6B7385",
        },
        ["black"] = new()
        {
            ["Bg"] = "#000000", ["Rail"] = "#050506", ["Surface"] = "#0B0C0F", ["Surface2"] = "#131419", ["Surface3"] = "#1B1D24",
            ["Line"] = "#1E2027", ["Text"] = "#ECEEF3", ["Muted"] = "#9098A8", ["Faint"] = "#626979",
        },
        ["light"] = new()
        {
            ["Bg"] = "#F4F5F8", ["Rail"] = "#ECEEF3", ["Surface"] = "#FFFFFF", ["Surface2"] = "#F0F2F6", ["Surface3"] = "#E4E7EE",
            ["Line"] = "#DADEE7", ["Text"] = "#161922", ["Muted"] = "#5B6474", ["Faint"] = "#8A92A2",
        },
        ["midnight"] = new()
        {
            ["Bg"] = "#0B1020", ["Rail"] = "#0E1428", ["Surface"] = "#121A33", ["Surface2"] = "#18223F", ["Surface3"] = "#212C4D",
            ["Line"] = "#253154", ["Text"] = "#E6EBFA", ["Muted"] = "#93A0C4", ["Faint"] = "#64709A",
        },
        ["nord"] = new()
        {
            ["Bg"] = "#242933", ["Rail"] = "#2B303B", ["Surface"] = "#2E3440", ["Surface2"] = "#3B4252", ["Surface3"] = "#434C5E",
            ["Line"] = "#4C566A", ["Text"] = "#ECEFF4", ["Muted"] = "#B6BECC", ["Faint"] = "#8690A3",
        },
        ["forest"] = new()
        {
            ["Bg"] = "#0D1512", ["Rail"] = "#101A16", ["Surface"] = "#14211B", ["Surface2"] = "#1A2A23", ["Surface3"] = "#22352C",
            ["Line"] = "#284034", ["Text"] = "#E5F2EB", ["Muted"] = "#93AFA1", ["Faint"] = "#628072",
        },
        ["grape"] = new()
        {
            ["Bg"] = "#140F1E", ["Rail"] = "#181226", ["Surface"] = "#1E172E", ["Surface2"] = "#261D3A", ["Surface3"] = "#302548",
            ["Line"] = "#382B52", ["Text"] = "#F0EAFB", ["Muted"] = "#A99BC6", ["Faint"] = "#7A6C98",
        },
        ["mocha"] = new()
        {
            ["Bg"] = "#1A1512", ["Rail"] = "#1F1915", ["Surface"] = "#251E19", ["Surface2"] = "#2E2620", ["Surface3"] = "#382E27",
            ["Line"] = "#43372E", ["Text"] = "#F3EAE2", ["Muted"] = "#BBA999", ["Faint"] = "#8C7A6B",
        },
        ["sand"] = new()
        {
            ["Bg"] = "#F6F1E7", ["Rail"] = "#EFE8DA", ["Surface"] = "#FFFCF6", ["Surface2"] = "#F3ECDF", ["Surface3"] = "#E8DFCE",
            ["Line"] = "#DDD2BE", ["Text"] = "#2A2218", ["Muted"] = "#6E6150", ["Faint"] = "#9A8C78",
        },
        ["contrast"] = new()
        {
            ["Bg"] = "#000000", ["Rail"] = "#000000", ["Surface"] = "#0A0A0A", ["Surface2"] = "#141414", ["Surface3"] = "#222222",
            ["Line"] = "#FFFFFF", ["Text"] = "#FFFFFF", ["Muted"] = "#E0E0E0", ["Faint"] = "#BDBDBD",
        },
        // 17.0: «Океан» — глубокая сине-зелёная вода; спокойно смотрится с бирюзовым и голубым акцентом.
        ["ocean"] = new()
        {
            ["Bg"] = "#0A1418", ["Rail"] = "#0A1418", ["Layer"] = "#0E1A1F", ["Surface"] = "#122128", ["Surface2"] = "#182A32", ["Surface3"] = "#20353E",
            ["Line"] = "#1F333C", ["Text"] = "#E6F3F6", ["Muted"] = "#93B1BA", ["Faint"] = "#5F7F89",
        },
        // «Бордо» — тёмное вино: тёплый тёмный фон для красного, кораллового и золотого акцента.
        ["crimson"] = new()
        {
            ["Bg"] = "#160C0E", ["Rail"] = "#160C0E", ["Layer"] = "#1C1013", ["Surface"] = "#241518", ["Surface2"] = "#2D1B1F", ["Surface3"] = "#382328",
            ["Line"] = "#3A2328", ["Text"] = "#F7E9EB", ["Muted"] = "#C3A1A7", ["Faint"] = "#8C6A70",
        },
        // «Кибер» — ночной синий с холодным отливом: для неонового голубого и розового.
        ["cyber"] = new()
        {
            ["Bg"] = "#070B14", ["Rail"] = "#070B14", ["Layer"] = "#0B111D", ["Surface"] = "#101828", ["Surface2"] = "#152034", ["Surface3"] = "#1C2A42",
            ["Line"] = "#1B2740", ["Text"] = "#E3F1FF", ["Muted"] = "#8FA8C8", ["Faint"] = "#5A7090",
        },
        // «Хаки» — оливковый полевой: приглушённо, без синевы, под зелёный и жёлтый акцент.
        ["olive"] = new()
        {
            ["Bg"] = "#12130E", ["Rail"] = "#12130E", ["Layer"] = "#171812", ["Surface"] = "#1E2018", ["Surface2"] = "#26281F", ["Surface3"] = "#303327",
            ["Line"] = "#2F3226", ["Text"] = "#EEF0E4", ["Muted"] = "#ADB19A", ["Faint"] = "#777B66",
        },
        // «Роза» — тёмно-розовый бархат.
        ["rose"] = new()
        {
            ["Bg"] = "#170D13", ["Rail"] = "#170D13", ["Layer"] = "#1D1118", ["Surface"] = "#251620", ["Surface2"] = "#2E1C28", ["Surface3"] = "#3A2433",
            ["Line"] = "#3A2533", ["Text"] = "#FBEAF3", ["Muted"] = "#C9A2B8", ["Faint"] = "#8E6C80",
        },
        // Светлые: «Мята», «Небо» и «Сакура» — белые карточки на чуть цветном фоне.
        ["mint"] = new()
        {
            ["Bg"] = "#E6F1EC", ["Rail"] = "#E6F1EC", ["Layer"] = "#F3F8F5", ["Surface"] = "#FFFFFF", ["Surface2"] = "#EEF5F1", ["Surface3"] = "#E0EBE5",
            ["Line"] = "#D5E3DB", ["Text"] = "#12201A", ["Muted"] = "#4F6359", ["Faint"] = "#87998F",
        },
        ["sky"] = new()
        {
            ["Bg"] = "#E7EEF7", ["Rail"] = "#E7EEF7", ["Layer"] = "#F4F7FC", ["Surface"] = "#FFFFFF", ["Surface2"] = "#EEF3F9", ["Surface3"] = "#E1E9F3",
            ["Line"] = "#D5DFEB", ["Text"] = "#111A26", ["Muted"] = "#4E5D70", ["Faint"] = "#8794A6",
        },
        ["blossom"] = new()
        {
            ["Bg"] = "#F6EBEF", ["Rail"] = "#F6EBEF", ["Layer"] = "#FBF5F7", ["Surface"] = "#FFFFFF", ["Surface2"] = "#F7EDF1", ["Surface3"] = "#EEDDE4",
            ["Line"] = "#E8D5DD", ["Text"] = "#24121A", ["Muted"] = "#6E5360", ["Faint"] = "#9F8893",
        },
    };

    /// <summary>Все цвета темы (для образцов оформления): Bg, Layer, Surface…, Text, Muted.</summary>
    public static IReadOnlyDictionary<string, string> Palette(string theme)
    {
        if (theme == "custom" && Custom() is { } own) return own;
        var p = Palettes.TryGetValue(theme, out var found) ? found : Palettes["store"];
        var copy = new Dictionary<string, string>(p);
        copy.TryAdd("Layer", p["Bg"]);
        return copy;
    }

    /// <summary>Цвета образца темы для карточек выбора: фон, поверхность, текст.</summary>
    public static (string Bg, string Surface, string Text) Sample(string theme) =>
        Palettes.TryGetValue(theme, out var p) ? (p["Bg"], p["Surface2"], p["Text"]) : ("#0F1116", "#1E222B", "#E8EBF2");

    static Styles? _custom;

    /// <summary>Шрифт и скругление — стилями поверх App.axaml, без перезапуска.</summary>
    static void ApplyStyles(Application app)
    {
        if (_custom is not null) app.Styles.Remove(_custom);
        var k = Radius switch { "sharp" => 0.3, "round" => 1.6, _ => 1.0 };
        var font = UiFont;
        _custom = new Styles
        {
            new Style(x => x.OfType<Avalonia.Controls.Window>()) { Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.FontFamilyProperty, font) } },
        };
        if (Radius != "normal")
        {
            foreach (var (selector, r) in new (Func<Selector?, Selector>, double)[]
            {
                (x => x.OfType<Avalonia.Controls.Button>(), 12),
                (x => x.OfType<Avalonia.Controls.Button>().Class("tile"), 16),
                (x => x.OfType<Avalonia.Controls.Button>().Class("card-btn"), 16),
                (x => x.OfType<Avalonia.Controls.TextBox>(), 12),
                (x => x.OfType<Avalonia.Controls.ComboBox>(), 12),
            })
                _custom.Add(new Style(selector) { Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, new CornerRadius(Math.Round(r * k))) } });
            _custom.Add(new Style(x => x.OfType<Avalonia.Controls.Border>().Class("card")) { Setters = { new Setter(Avalonia.Controls.Border.CornerRadiusProperty, new CornerRadius(Math.Round(18 * k))) } });
        }
        // Стекло (8.1): карточки чуть прозрачные, с тонким светлым контуром — фон мягко просвечивает.
        if (!Studio && Backdrop != "plain")
        {
            var surface = Color.Parse(Current["Surface"]);
            var glass = new SolidColorBrush(Color.FromArgb((byte)(IsLight ? 225 : 205), surface.R, surface.G, surface.B));
            var edge = new SolidColorBrush(IsLight ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(18, 255, 255, 255));
            foreach (var selector in new Func<Selector?, Selector>[]
            {
                x => x.OfType<Avalonia.Controls.Border>().Class("card"),
            })
                _custom.Add(new Style(selector)
                {
                    Setters =
                    {
                        new Setter(Avalonia.Controls.Border.BackgroundProperty, glass),
                        new Setter(Avalonia.Controls.Border.BorderBrushProperty, edge),
                        new Setter(Avalonia.Controls.Border.BorderThicknessProperty, new Thickness(1)),
                    },
                });
        }
        if (Studio) AddStudio(_custom);
        if (Vitrina) AddVitrina(_custom);
        if (Store) AddStore(_custom);
        // 2.0: поверх всего — правила «Φ» (шрифты, углы, кнопки, карточки, вкладки) у любой темы.
        AddPhi(_custom);
        app.Styles.Add(_custom);
    }

    /// <summary>
    /// «Студия» (8.3). Правила: иерархию задаёт шрифт, а не цвет и рамки; заголовки
    /// крупные и плотные, текст спокойный; карточки — плоские листы с волосяной
    /// линией; акцент только у главного действия; выбранное — инверсией, а не подсветкой.
    /// </summary>
    static void AddStudio(Styles s)
    {
        IBrush R(string key) => Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.Gray;
        void Add(Func<Selector?, Selector> sel, params (AvaloniaProperty P, object V)[] setters)
        {
            var style = new Style(sel);
            foreach (var (p, v) in setters) style.Setters.Add(new Setter(p, v));
            s.Add(style);
        }
        var round = Radius switch { "round" => 1.5, "sharp" => 0.4, _ => 1.0 };
        CornerRadius Cr(double r) => new(Math.Round(r * round));

        // Типографика: шкала 36 / 22 / 15 / 13.5 / 12, плотные заголовки.
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h1"), (Avalonia.Controls.TextBlock.FontSizeProperty, 36.0), (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.Bold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -1.0));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h2"), (Avalonia.Controls.TextBlock.FontSizeProperty, 22.0), (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.Bold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -0.45));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h3"), (Avalonia.Controls.TextBlock.FontSizeProperty, 15.0), (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.SemiBold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -0.15));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("small"), (Avalonia.Controls.TextBlock.FontSizeProperty, 12.0), (Avalonia.Controls.TextBlock.LineHeightProperty, 17.0));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("brand"), (Avalonia.Controls.TextBlock.ForegroundProperty, R("Text")));

        // Листы: плоские, с волосяной линией, без тени.
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("card"),
            (Avalonia.Controls.Border.BackgroundProperty, R("Surface")), (Avalonia.Controls.Border.BorderBrushProperty, R("Line")),
            (Avalonia.Controls.Border.BorderThicknessProperty, new Thickness(1)), (Avalonia.Controls.Border.CornerRadiusProperty, Cr(10)),
            (Avalonia.Controls.Border.BoxShadowProperty, new BoxShadows()));
        foreach (var cls in new[] { "tile", "card-btn" })
            Add(x => x.OfType<Avalonia.Controls.Button>().Class(cls),
                (Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, R("Line")),
                (Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(10)));

        // Кнопки: прямоугольнее и спокойнее.
        Add(x => x.OfType<Avalonia.Controls.Button>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(8)));
        Add(x => x.OfType<Avalonia.Controls.TextBox>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(8)));
        Add(x => x.OfType<Avalonia.Controls.ComboBox>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(8)));

        // Чипы и вкладки: выбранное — инверсия (текст на светлом), без акцентной заливки.
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip"), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(7)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("active"),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("Text")), (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, R("Bg")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("active").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("Text")), (Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, R("Bg")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("tab").Class("active").Not(y => y.Class("line")),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("Surface3")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("tab").Class("active").Not(y => y.Class("line")).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("Surface3")));
    }

    /// <summary>
    /// «Витрина» (9.0) поверх правил «Студии»: крупные заголовки — Unbounded, карточки мягче
    /// (скругление 14), выбранное в акцентной подложке, главная кнопка — акцентом.
    /// </summary>
    static void AddVitrina(Styles s)
    {
        IBrush R(string key) => Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.Gray;
        void Add(Func<Selector?, Selector> sel, params (AvaloniaProperty P, object V)[] setters)
        {
            var style = new Style(sel);
            foreach (var (p, v) in setters) style.Setters.Add(new Setter(p, v));
            s.Add(style);
        }
        var round = Radius switch { "round" => 1.4, "sharp" => 0.4, _ => 1.0 };
        CornerRadius Cr(double r) => new(Math.Round(r * round));

        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h1"),
            (Avalonia.Controls.TextBlock.FontFamilyProperty, Unbounded), (Avalonia.Controls.TextBlock.FontSizeProperty, 32.0),
            (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.Bold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -1.2));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h2"),
            (Avalonia.Controls.TextBlock.FontFamilyProperty, Unbounded), (Avalonia.Controls.TextBlock.FontSizeProperty, 19.0),
            (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.Bold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -0.5));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("brand"), (Avalonia.Controls.TextBlock.ForegroundProperty, R("Brand2")));

        Add(x => x.OfType<Avalonia.Controls.Border>().Class("card"),
            (Avalonia.Controls.Border.BackgroundProperty, R("Surface")), (Avalonia.Controls.Border.BorderBrushProperty, R("Line")),
            (Avalonia.Controls.Border.BorderThicknessProperty, new Thickness(1)), (Avalonia.Controls.Border.CornerRadiusProperty, Cr(14)));
        foreach (var cls in new[] { "tile", "card-btn" })
            Add(x => x.OfType<Avalonia.Controls.Button>().Class(cls),
                (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(14)));
        Add(x => x.OfType<Avalonia.Controls.Button>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(10)));
        Add(x => x.OfType<Avalonia.Controls.TextBox>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(10)));
        Add(x => x.OfType<Avalonia.Controls.ComboBox>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(10)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip"), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(9)));
        // Пункты левой колонки каталога: выбранный — спокойная подложка, а не инверсия.
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("side"),
            (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(10)), (Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty, 14.0));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("side").Class("active"),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("Surface2")), (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, R("Text")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("side").Class("active").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("Surface2")), (Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, R("Text")));
        // Выбранная вкладка-кнопка — мягкая акцентная подложка, а не серая.
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("tab").Class("active").Not(y => y.Class("line")),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("BrandSoft")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("tab").Class("active").Not(y => y.Class("line")).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("BrandSoft")));
    }

    /// <summary>
    /// Store (9.2) поверх «Витрины»: заголовки — шрифтом интерфейса (как Segoe в Microsoft Store),
    /// карточки с тонкой линией и скруглением 10, кнопки 6, фильтры — «пилюли».
    /// </summary>
    static void AddStore(Styles s)
    {
        IBrush R(string key) => Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.Gray;
        void Add(Func<Selector?, Selector> sel, params (AvaloniaProperty P, object V)[] setters)
        {
            var style = new Style(sel);
            foreach (var (p, v) in setters) style.Setters.Add(new Setter(p, v));
            s.Add(style);
        }
        var round = Radius switch { "round" => 1.5, "sharp" => 0.4, _ => 1.0 };
        CornerRadius Cr(double r) => new(Math.Round(r * round));
        var font = UiFont;

        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h1"),
            (Avalonia.Controls.TextBlock.FontFamilyProperty, font), (Avalonia.Controls.TextBlock.FontSizeProperty, 30.0),
            (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.Bold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -0.6));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h2"),
            (Avalonia.Controls.TextBlock.FontFamilyProperty, font), (Avalonia.Controls.TextBlock.FontSizeProperty, 20.0),
            (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.SemiBold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -0.3));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h3"),
            (Avalonia.Controls.TextBlock.FontSizeProperty, 14.5), (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.SemiBold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -0.1));

        Add(x => x.OfType<Avalonia.Controls.Border>().Class("card"),
            (Avalonia.Controls.Border.BackgroundProperty, R("Surface")), (Avalonia.Controls.Border.BorderBrushProperty, R("Line")),
            (Avalonia.Controls.Border.BorderThicknessProperty, new Thickness(1)), (Avalonia.Controls.Border.CornerRadiusProperty, Cr(10)));
        foreach (var cls in new[] { "tile", "card-btn" })
            Add(x => x.OfType<Avalonia.Controls.Button>().Class(cls),
                (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(10)),
                (Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, R("Line")),
                (Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        Add(x => x.OfType<Avalonia.Controls.Button>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(6)));
        Add(x => x.OfType<Avalonia.Controls.TextBox>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(6)));
        Add(x => x.OfType<Avalonia.Controls.ComboBox>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(6)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip"), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, new CornerRadius(999)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("side"), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(6)));
        // Выбранный фильтр — как в Microsoft Store: акцентная обводка и мягкая заливка, а не инверсия.
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("active").Not(y => y.Class("side")),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("BrandSoft")), (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, R("Brand2")),
            (Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, R("Brand")), (Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("active").Not(y => y.Class("side")).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("BrandSoft")), (Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, R("Brand2")),
            (Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, R("Brand")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Not(y => y.Class("active")).Not(y => y.Class("side")),
            (Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, R("Line")), (Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(1)));

        // 9.3 «игровой» слой: неоновая кромка и свечение цветом акцента при наведении на плитки,
        // светящиеся главные кнопки, выбранный раздел — в акцентной подложке.
        var accent = Color.Parse(Accent);
        string Hex(byte a) => $"#{a:X2}{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        var glow = BoxShadows.Parse($"0 0 0 1 {Hex(220)}, 0 18 42 -14 {Hex(150)}");
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("store-tile").Class(":pointerover"),
            (Avalonia.Controls.Border.BoxShadowProperty, glow), (Avalonia.Controls.Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B))));
        // 9.9: строки на главной («Ближайшие награды», «Вернуться в игру») подсвечиваются при наведении.
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("home-row").Class(":pointerover"), (Avalonia.Controls.Border.BackgroundProperty, R("Surface2")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("primary").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BoxShadowProperty, BoxShadows.Parse($"0 0 18 0 {Hex(150)}")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("nav").Class("active"), (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("BrandSoft")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("nav").Class("active").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("BrandSoft")));
        // Вкладки рынка: выбранная — подложка акцента и светлый текст.
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("market-nav").Descendant().OfType<Avalonia.Controls.Button>().Class("pivot").Class("active"),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("BrandSoft")), (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, R("Text")));
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("market-nav").Descendant().OfType<Avalonia.Controls.Button>().Class("pivot").Class("active").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("BrandSoft")));
    }

    /// <summary>
    /// 2.0 «Φ»: одна система для всего приложения. Шрифты — лестница φ (34 / 21 / 16 / 14 / 12 / 11), углы 8 у кнопок
    /// и полей, 13 у карточек, 21 у сцен; карточки — плоские листы с волосяной линией, без теней и свечения;
    /// выбранное — спокойной подложкой, а не цветной обводкой; акцент — только у главного действия,
    /// «Играть» — светлой кнопкой (как в Apple TV), установка в списках — мягкой кнопкой (не стена ярких плашек).
    /// </summary>
    static void AddPhi(Styles s)
    {
        IBrush R(string key) => Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.Gray;
        void Add(Func<Selector?, Selector> sel, params (AvaloniaProperty P, object V)[] setters)
        {
            var style = new Style(sel);
            foreach (var (p, v) in setters) style.Setters.Add(new Setter(p, v));
            s.Add(style);
        }
        var round = Radius switch { "round" => 1.4, "sharp" => 0.35, _ => 1.0 };
        CornerRadius Cr(double r) => new(Math.Round(r * round));
        var font = UiFont;
        var none = new BoxShadows();
        var palette = Current;
        var text = Color.Parse(palette["Text"]);
        var bg = Color.Parse(palette["Bg"]);
        var accent = Color.Parse(Accent);
        var light = IsLight;

        // ---- шрифты: лестница φ
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h1"),
            (Avalonia.Controls.TextBlock.FontFamilyProperty, font), (Avalonia.Controls.TextBlock.FontSizeProperty, Phi.H1),
            (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.Bold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -1.0));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h2"),
            (Avalonia.Controls.TextBlock.FontFamilyProperty, font), (Avalonia.Controls.TextBlock.FontSizeProperty, Phi.H2),
            (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.SemiBold), (Avalonia.Controls.TextBlock.LetterSpacingProperty, -0.4));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("h3"),
            (Avalonia.Controls.TextBlock.FontSizeProperty, Phi.H3), (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.SemiBold),
            (Avalonia.Controls.TextBlock.LetterSpacingProperty, -0.15));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("small"),
            (Avalonia.Controls.TextBlock.FontSizeProperty, Phi.Small), (Avalonia.Controls.TextBlock.LineHeightProperty, 18.0));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("caption"),
            (Avalonia.Controls.TextBlock.FontSizeProperty, Phi.Caption), (Avalonia.Controls.TextBlock.FontWeightProperty, FontWeight.SemiBold),
            (Avalonia.Controls.TextBlock.ForegroundProperty, R("Faint")), (Avalonia.Controls.TextBlock.LetterSpacingProperty, 0.2));
        Add(x => x.OfType<Avalonia.Controls.TextBlock>().Class("brand"), (Avalonia.Controls.TextBlock.ForegroundProperty, R("Brand2")));

        // ---- листы и плитки: плоские, волосяная линия, угол 13, без теней
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("card"),
            (Avalonia.Controls.Border.BackgroundProperty, R("Surface")), (Avalonia.Controls.Border.BorderBrushProperty, R("Line")),
            (Avalonia.Controls.Border.BorderThicknessProperty, new Thickness(1)), (Avalonia.Controls.Border.CornerRadiusProperty, Cr(Phi.R13)),
            (Avalonia.Controls.Border.BoxShadowProperty, none));
        foreach (var cls in new[] { "tile", "card-btn" })
        {
            Add(x => x.OfType<Avalonia.Controls.Button>().Class(cls),
                (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("Surface")),
                (Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, R("Line")),
                (Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(Phi.R13)));
            Add(x => x.OfType<Avalonia.Controls.Button>().Class(cls).Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
                (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("Surface2")), (Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, R("Surface3")),
                (Avalonia.Controls.Presenters.ContentPresenter.BoxShadowProperty, none));
        }
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("store-tile"), (Avalonia.Controls.Border.CornerRadiusProperty, Cr(Phi.R13)));
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("store-tile").Class(":pointerover"),
            (Avalonia.Controls.Border.BoxShadowProperty, none), (Avalonia.Controls.Border.BorderBrushProperty, R("Surface3")));

        // ---- кнопки: угол 8; главная — акцентом без свечения
        Add(x => x.OfType<Avalonia.Controls.Button>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(Phi.R8)));
        Add(x => x.OfType<Avalonia.Controls.TextBox>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(Phi.R8)));
        Add(x => x.OfType<Avalonia.Controls.ComboBox>(), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(Phi.R8)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("primary").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BoxShadowProperty, none));
        // «Играть» на арте (сцена главной, шапка игры) — светлая кнопка, как на Apple TV: на любой картинке она
        // заметнее всего и не спорит с акцентом. Арт всегда затемнён, поэтому цвет один для тёмных и светлых тем.
        var playBg = Color.Parse("#F4F4F6");
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("play"),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, new SolidColorBrush(playBg)),
            (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, new SolidColorBrush(Color.Parse("#0B0B0D"))),
            (Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(0)),
            (Avalonia.Controls.Primitives.TemplatedControl.FontWeightProperty, FontWeight.Bold));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("play").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new SolidColorBrush(Colors.White)),
            (Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, new SolidColorBrush(Color.Parse("#0B0B0D"))), (Avalonia.Controls.Presenters.ContentPresenter.BoxShadowProperty, none));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("play").Class(":pressed").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse("#D9D9DE"))));
        // Мягкая кнопка: подложка цвета акцента и акцентный текст — для «Установить» в списках и вторых действий.
        var soft = Mix(Color.Parse(palette["Surface2"]), accent, light ? 0.12 : 0.18);
        var softHover = Mix(Color.Parse(palette["Surface2"]), accent, light ? 0.2 : 0.28);
        var softText = light ? Mix(accent, Colors.Black, 0.2) : Mix(accent, Colors.White, 0.35);
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("soft"),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, new SolidColorBrush(soft)),
            (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, new SolidColorBrush(softText)),
            (Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(0)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("soft").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new SolidColorBrush(softHover)),
            (Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, new SolidColorBrush(softText)));

        // ---- фильтры-«пилюли»: выбранный — спокойная подложка, без акцентной обводки
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip"), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, new CornerRadius(999)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("side"), (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(Phi.R8)));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("active").Not(y => y.Class("side")),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("Surface3")), (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, R("Text")),
            (Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, R("Surface3")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("chip").Class("active").Not(y => y.Class("side")).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("Surface3")), (Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, R("Text")),
            (Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, R("Surface3")));

        // ---- вкладки-кнопки и разделы рынка: выбранное — подложкой Surface3, текст обычный
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("tab").Class("active").Not(y => y.Class("line")),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("Surface3")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("tab").Class("active").Not(y => y.Class("line")).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("Surface3")));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("tab").Class("line"), (Avalonia.Layout.Layoutable.MarginProperty, new Thickness(0, 0, Phi.S21, 0)));
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("market-nav").Descendant().OfType<Avalonia.Controls.Button>().Class("pivot").Class("active"),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, R("Surface3")), (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, R("Text")));
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("market-nav").Descendant().OfType<Avalonia.Controls.Button>().Class("pivot").Class("active").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("Surface3")));
        Add(x => x.OfType<Avalonia.Controls.Border>().Class("home-row").Class(":pointerover"), (Avalonia.Controls.Border.BackgroundProperty, R("Surface2")));

        // ---- боковая панель 2.0: тихие строки высотой 34, выбранная — подложкой Surface2 и значком акцента
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("sidenav"),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, Brushes.Transparent),
            (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, R("Muted")),
            (Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(0)),
            (Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(Phi.S8, 0)),
            (Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, Cr(Phi.R8)),
            (Avalonia.Controls.Primitives.TemplatedControl.FontWeightProperty, FontWeight.Medium),
            (Avalonia.Layout.Layoutable.HorizontalAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch),
            (Avalonia.Controls.ContentControl.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch),
            (Avalonia.Controls.ContentControl.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("sidenav").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, R("Hover")), (Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, R("Text")));
        // На светлых темах Surface2 почти совпадает с фоном панели — выбранная строка берёт Surface3.
        var sideOn = light ? R("Surface3") : R("Surface2");
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("sidenav").Class("active"),
            (Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, sideOn), (Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, R("Text")),
            (Avalonia.Controls.Primitives.TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        Add(x => x.OfType<Avalonia.Controls.Button>().Class("sidenav").Class("active").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
            (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, sideOn));
    }

    /// <summary>Применить сохранённые настройки (при запуске и после любой смены).</summary>
    public static void Apply()
    {
        Migrate();
        ApplyCore();
    }

    static void ApplyCore()
    {
        var app = Application.Current;
        if (app is null) return;
        var palette = Current;
        foreach (var (key, hex) in palette) Set(app, key, Color.Parse(hex));
        // «Лист» с содержимым есть только у тем Store; у остальных — цвет окна.
        if (!palette.ContainsKey("Layer")) Set(app, "Layer", Color.Parse(palette["Bg"]));
        var accent = Color.Parse(Accent);
        // Текст на акценте: на светлом (зелёный, белый) — тёмный, иначе белый.
        Set(app, "OnBrand", Luma(accent) > 0.55 ? Color.Parse("#08110B") : Colors.White);
        var bg = Color.Parse(palette["Bg"]);
        var light = IsLight;
        Set(app, "Brand", accent);
        // 2.0: золото — для редких отметок (уровень, награды, сетка φ); на светлых темах темнее, чтобы читалось.
        Set(app, "Gold", light ? Color.Parse("#B07A12") : Color.Parse("#F2B544"));
        Set(app, "Brand2", light ? Mix(accent, Colors.Black, 0.12) : Mix(accent, Colors.White, 0.22));
        Set(app, "BrandSoft", Mix(bg, accent, light ? 0.16 : 0.24));
        if (Studio && !Vitrina)
        {
            // Акцент только у главного действия: подсветки и значки — нейтральные.
            Set(app, "BrandSoft", Color.Parse(palette["Surface3"]));
            Set(app, "Brand2", Color.Parse(palette["Text"]));
        }
        if (app.Resources.TryGetResource("Hover", null, out var h) && h is SolidColorBrush hover)
            hover.Color = light ? Colors.Black : Colors.White;
        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.2);
        app.Resources["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.2);
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        // Шрифт и у кнопок, полей и списков (их шаблоны берут его из ресурса темы).
        app.Resources["ContentControlThemeFontFamily"] = UiFont;
        ApplyStyles(app);
        Changed?.Invoke();
    }

    static void Set(Application app, string key, Color color)
    {
        if (app.Resources.TryGetResource(key, null, out var v) && v is SolidColorBrush brush) brush.Color = color;
    }

    /// <summary>Яркость цвета 0…1 (как её видит глаз).</summary>
    public static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static void SetTheme(string theme) { Settings.Data["theme"] = theme; Settings.Save(); Apply(); }
    public static void SetAccent(string hex) { Settings.Data["accent"] = hex; Settings.Save(); Apply(); }
    public static void SetScale(double scale) { Settings.Data["uiScale"] = scale; Settings.Save(); Changed?.Invoke(); }
}
