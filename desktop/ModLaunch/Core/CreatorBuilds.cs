using ModLaunch.Features;

namespace ModLaunch.Core;

/// <summary>
/// 17.0: личные сборки настроек от создателей ModLaunch — награды пути уровней (как «конфиги про-игроков» в CS2).
/// Сборка — обычный конфиг .mlstyle: тема, акцент, шрифт, анимации, раскладка и вид виджетов главной, плюс
/// настройки поведения (плавная прокрутка, строка состояния, компактные списки, заставка…). Открывается на своём
/// уровне, видна в «Награды → Сборки создателей», на пути наград и среди конфигов «Внешнего вида». Применяется
/// одной кнопкой и снимается любым другим конфигом — свои настройки не теряются: перед первой сборкой
/// текущее оформление сохраняется в папку configs («До сборки создателя»).
/// </summary>
public static class CreatorBuilds
{
    public sealed record Build(string Id, int Level, string Text);

    public const string Prefix = "creator:";

    public static readonly Build[] All =
    [
        // Холодный ночной синий, ничего не мигает: так удобно смотреть в лаунчер в два часа ночи.
        new("night", 3, """
            theme cyber
            accent "#38BDF8"
            font Inter
            headings ui
            radius normal
            glow 0.3
            backdrop plain
            anim_open calm
            anim_launch off
            home_hero compact
            status_bar 1
            widget_grid normal tight
            widget continue m style=tinted;tint=cyan
            widget clock s style=glass;tint=cyan
            widget playtime s tint=cyan
            widget downloads m
            widget rewards w tint=cyan
            widget attention s
            """),
        // Для слабых ноутбуков: без эффектов и живого фона, компактные списки, мгновенные переходы.
        new("light", 6, """
            theme graphite
            accent "#22C55E"
            font Onest
            headings ui
            radius sharp
            glow 0
            backdrop plain
            anim 0
            anim_open off
            anim_launch off
            fx_scanlines 0
            fx_vignette 0
            home_hero off
            smooth_scroll 0
            compact_lists 1
            splash 0
            widget_grid compact tight
            widget continue m style=outline
            widget downloads m style=outline
            widget attention s style=outline
            widget playtime s style=outline
            widget autopilot s style=outline
            """),
        // Сборка для стрима: крупно, контрастно, кейсы и уровень на виду у зрителей.
        new("stream", 10, """
            theme rose
            accent "#E11D74"
            font Onest
            headings wide
            radius round
            glow 0.8
            backdrop soft
            scale 1.1
            anim_open calm
            anim_launch calm
            home_hero stage
            widget_grid roomy normal
            widget continue l style=tinted;tint=pink
            widget level s tint=pink
            widget streak s tint=pink
            widget rewards w tint=pink
            widget friends m style=glass
            """),
        // Строгая рабочая: Creator Hub, сборки и часы — всё, что нужно, когда делаешь моды.
        new("studio", 15, """
            theme ocean
            accent "#00C2A8"
            font Inter
            headings ui
            radius normal
            glow 0
            backdrop grid
            anim_open calm
            anim_launch off
            home_hero compact
            status_bar 1
            widget_grid normal normal
            widget builds s tint=mint
            widget playtime s tint=mint
            widget note m style=tinted;tint=mint
            widget continue m
            widget downloads m
            widget attention s
            """),
        // Тёплый вечер: бордо и золото, мягкие углы, спокойное движение.
        new("evening", 20, """
            theme crimson
            accent "#E8B84A"
            font Onest
            headings wide
            radius round
            glow 0.6
            backdrop soft
            anim_open calm
            anim_launch calm
            home_hero stage
            widget_grid normal normal
            widget continue l style=glass;tint=yellow
            widget clock s tint=yellow
            widget level s tint=yellow
            widget library m
            widget playtime s tint=orange
            widget rewards w tint=yellow
            """),
        // Полевая: хаки, зелёный, квадратные углы и плотная сетка — как планшет на броне.
        new("field", 25, """
            theme olive
            accent "#A3E635"
            font Bahnschrift
            headings ui
            radius sharp
            glow 0.2
            backdrop grid
            anim_open calm
            anim_launch calm
            home_hero stage
            compact_lists 1
            widget_grid compact tight
            widget continue m style=outline;tint=green
            widget autopilot s tint=green
            widget clock s style=outline;tint=green
            widget attention s tint=green
            widget playtime s tint=green
            widget downloads m style=outline
            widget streak s tint=green
            """),
        // Утро: светлая мята, много воздуха, ничего лишнего.
        new("morning", 30, """
            theme mint
            accent "#14B8A6"
            font Onest
            headings ui
            radius round
            glow 0
            backdrop plain
            anim_open calm
            anim_launch calm
            home_hero stage
            widget_grid roomy wide
            widget continue m style=clear
            widget clock s style=clear;tint=mint
            widget note m style=tinted;tint=mint
            widget rewards w style=clear
            """),
        // Сборка-легенда: неоновый синтвейв, строки развёртки, всё сияет.
        new("legend", 40, """
            theme custom
            color_bg "#0B0716"
            color_rail "#080512"
            color_layer "#100A1F"
            color_surface "#170F2B"
            color_surface2 "#201538"
            color_surface3 "#2B1C49"
            color_line "#3A2860"
            color_text "#F4EBFF"
            color_muted "#C2ADE0"
            color_faint "#8670A8"
            accent "#B79CFF"
            font Onest
            headings wide
            radius round
            glow 1
            backdrop aurora
            fx_scanlines 1
            fx_vignette 1
            anim_open classic
            anim_launch calm
            home_hero stage
            widget_grid normal normal
            widget continue l style=glass;tint=violet
            widget level s style=glass;tint=violet
            widget streak s style=glass;tint=pink
            widget rewards w style=glass;tint=violet
            widget friends m style=glass
            widget clock s style=glass;tint=cyan
            """),
    ];

    public static Build? Find(string id) => All.FirstOrDefault(b => b.Id == id || Prefix + b.Id == id);

    /// <summary>Сборка как конфиг: имя и заметка — из строк (v99.config.*), автор — «Создатели ModLaunch».</summary>
    public static UiConfig Config(Build b) => Configs.Parse(b.Text, b.Id) with
    {
        Id = Prefix + b.Id, Name = I18n.T($"v99.config.{b.Id}"), About = I18n.T($"v17.cb.{b.Id}.d"), Author = I18n.T("v17.cb.author"), BuiltIn = true,
    };

    /// <summary>Открыта ли сборка на текущем уровне.</summary>
    public static bool Open(Build b, int level) => level >= b.Level;

    /// <summary>
    /// Применить сборку. Перед первой сборкой создателя своё оформление сохраняется файлом в configs —
    /// вернуть его можно в «Внешний вид → Конфиги».
    /// </summary>
    public static void Apply(Build b)
    {
        if (Configs.CurrentId?.StartsWith(Prefix) != true && !Settings.Data.Bool("cbKept"))
        {
            try { Configs.SaveCurrent(I18n.T("v17.cb.kept")); } catch { }
            Settings.Data["cbKept"] = true;
        }
        Configs.Apply(Config(b));
    }

    [SelfTest]
    static string CreatorBuildsAreValid()
    {
        var levels = All.Select(b => b.Level).ToList();
        if (levels.Distinct().Count() != All.Length || !levels.SequenceEqual(levels.Order()) || levels[0] < 2 || levels[^1] > LevelRoad.Max)
            throw new Exception("levels must be unique, ascending, inside the road");
        // Каждая команда сборки — известная: иначе она молча не применится.
        var known = new HashSet<string>(Configs.Commands);
        foreach (var b in All)
        {
            var c = Config(b);
            if (c.Lines.FirstOrDefault(l => !known.Contains(l.Key)) is { Key: not null } bad) throw new Exception($"{b.Id}: unknown command {bad.Key}");
            if (c.Get("theme") is { } t && t != "custom" && !Look.Themes.Contains(t)) throw new Exception($"{b.Id}: theme {t}");
            if (c.Get("font") is { } f && !Look.Fonts.Contains(f)) throw new Exception($"{b.Id}: font {f}");
            if (c.WidgetLines().Select(Widgets.Parse).Any(e => e is null)) throw new Exception($"{b.Id}: widget line");
            if (c.Widgets().Count < 4) throw new Exception($"{b.Id}: too few widgets");
            foreach (var lang in new[] { "ru", "en" })
                foreach (var key in new[] { $"v99.config.{b.Id}", $"v17.cb.{b.Id}.d" })
                    if (I18n.Raw(lang, key) is null) throw new Exception($"{b.Id}: no {lang} string {key}");
        }
        return $"{All.Length} creator builds on levels {string.Join(", ", levels)}; every command known";
    }

    /// <summary>Сборка «Лёгкая» выключает прокрутку и анимации — следующий конфиг, где о них ни слова, их возвращает.</summary>
    [SelfTest]
    static string BuildsLeaveNoTrace()
    {
        var d = Settings.Data;
        var keep = d.DeepClone().AsObject();
        try
        {
            foreach (var k in new[] { "smoothScroll", "compactLists", "splash", "animations", "config" }) d.Remove(k);
            Configs.Apply(Config(Find("light")!), apply: false);
            if (d.Bool("smoothScroll", true) || !d.Bool("compactLists") || d.Bool("animations", true))
                throw new Exception("«light» did not switch scrolling, compact lists and animations");
            Configs.Apply(Configs.BuiltIn().First(c => c.Id == "modlaunch"), apply: false);
            if (d.ContainsKey("smoothScroll") || d.ContainsKey("compactLists") || d.ContainsKey("splash") || d.ContainsKey("animations"))
                throw new Exception("the next config kept the creator build's settings");
            return "a creator build's settings go back to normal with the next config";
        }
        finally
        {
            foreach (var k in d.Select(x => x.Key).ToList()) d.Remove(k);
            foreach (var (k, v) in keep) d[k] = v?.DeepClone();
            Settings.Save();
        }
    }
}
