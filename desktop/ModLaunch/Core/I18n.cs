using System.Text.Json.Nodes;
using Avalonia.Platform;

namespace ModLaunch.Core;

/// <summary>
/// Тексты интерфейса на двух языках. Словарь общий с версией 3.x
/// (Assets/strings.json), подстановки — в фигурных скобках: {name}.
/// </summary>
public static class I18n
{
    static readonly Dictionary<string, Dictionary<string, string>> Tables = Load();

    public static string Lang { get; set; } = "ru";
    public static event Action? Changed;

    static Dictionary<string, Dictionary<string, string>> Load()
    {
        try
        {
            // JsonNode, а не JsonSerializer: в урезанной сборке сериализация через отражение выключена.
            // strings.json — основной словарь, strings5.json — строки версии 5 (дополняют его).
            var tables = new Dictionary<string, Dictionary<string, string>> { ["ru"] = new(), ["en"] = new() };
            foreach (var file in new[] { "strings.json", "strings5.json" })
            {
                try
                {
                    using var stream = AssetLoader.Open(new Uri($"avares://ModLaunch/Assets/{file}"));
                    foreach (var (lang, table) in JsonNode.Parse(stream) as JsonObject ?? new JsonObject())
                    {
                        var target = tables.TryGetValue(lang, out var t) ? t : tables[lang] = new();
                        foreach (var (k, v) in table as JsonObject ?? new JsonObject()) target[k] = v?.GetValue<string>() ?? "";
                    }
                }
                catch { }
            }
            return tables;
        }
        catch
        {
            return new() { ["ru"] = new(), ["en"] = new() };
        }
    }

    public static void Set(string lang)
    {
        Lang = lang == "en" ? "en" : "ru";
        Changed?.Invoke();
    }

    public static bool Has(string key) => Tables.GetValueOrDefault(Lang)?.ContainsKey(key) == true;

    public static string T(string key, params (string Name, object Value)[] args)
    {
        var text = Tables.GetValueOrDefault(Lang)?.GetValueOrDefault(key)
                   ?? Tables.GetValueOrDefault("ru")?.GetValueOrDefault(key)
                   ?? key;
        foreach (var (name, value) in args) text = text.Replace("{" + name + "}", value?.ToString());
        return text;
    }

    public static string Plural(long n, string one, string few, string many)
    {
        if (Lang == "en") return n == 1 ? one : few;
        var m10 = n % 10;
        var m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 is >= 2 and <= 4 && (m100 < 10 || m100 >= 20)) return few;
        return many;
    }

    /// <summary>Большие числа коротко: 12,3 тыс., 1,2 млн (1.2K / 1.2M).</summary>
    public static string Compact(long n)
    {
        var ru = Lang != "en";
        string F(double v) => v.ToString(v < 10 ? "0.#" : "0", ru ? System.Globalization.CultureInfo.GetCultureInfo("ru-RU") : System.Globalization.CultureInfo.InvariantCulture);
        if (n >= 1_000_000) return F(n / 1_000_000d) + (ru ? " млн" : "M");
        if (n >= 1_000) return F(n / 1_000d) + (ru ? " тыс." : "K");
        return n.ToString();
    }
}
