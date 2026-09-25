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
    public static readonly string[] Themes = ["dark", "black", "light"];
    public static readonly string[] Accents = ["#7C5CFF", "#3B82F6", "#14B8A6", "#22C55E", "#EAB308", "#F97316", "#EF4444", "#EC4899"];
    public static readonly double[] Scales = [0.85, 0.9, 1.0, 1.1, 1.25];

    public static string Theme => Themes.Contains(Settings.Data.Str("theme")) ? Settings.Data.Str("theme")! : "dark";
    public static string Accent => Settings.Data.Str("accent") is string a && a.StartsWith('#') && a.Length == 7 ? a : Accents[0];
    /// <summary>«Авто»: на широких мониторах интерфейс крупнее, чтобы не было пустых полей.</summary>
    public static bool AutoScale => Settings.Data["uiScale"] is null || Settings.Data.Str("uiScale") == "auto";
    public static void SetAutoScale() { Settings.Data["uiScale"] = "auto"; Settings.Save(); Changed?.Invoke(); }
    public static double Scale => Settings.Data["uiScale"] is { } v && double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s is >= 0.7 and <= 1.6 ? s : 1.0;
    public static bool Animations => Settings.Data.Bool("animations", true);

    public static event Action? Changed;

    static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
    {
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
    };

    /// <summary>Применить сохранённые настройки (при запуске и после любой смены).</summary>
    public static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;
        var palette = Palettes[Theme];
        foreach (var (key, hex) in palette) Set(app, key, Color.Parse(hex));
        var accent = Color.Parse(Accent);
        var bg = Color.Parse(palette["Bg"]);
        var light = Theme == "light";
        Set(app, "Brand", accent);
        Set(app, "Brand2", light ? Mix(accent, Colors.Black, 0.12) : Mix(accent, Colors.White, 0.22));
        Set(app, "BrandSoft", Mix(bg, accent, light ? 0.16 : 0.24));
        if (app.Resources.TryGetResource("Hover", null, out var h) && h is SolidColorBrush hover)
            hover.Color = light ? Colors.Black : Colors.White;
        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.2);
        app.Resources["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.2);
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        Changed?.Invoke();
    }

    static void Set(Application app, string key, Color color)
    {
        if (app.Resources.TryGetResource(key, null, out var v) && v is SolidColorBrush brush) brush.Color = color;
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static void SetTheme(string theme) { Settings.Data["theme"] = theme; Settings.Save(); Apply(); }
    public static void SetAccent(string hex) { Settings.Data["accent"] = hex; Settings.Save(); Apply(); }
    public static void SetScale(double scale) { Settings.Data["uiScale"] = scale; Settings.Save(); Changed?.Invoke(); }
}
