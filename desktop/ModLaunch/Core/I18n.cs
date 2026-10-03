using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Platform;

namespace ModLaunch.Core;

/// <summary>Язык в списке выбора: код, название на нём самом и сколько строк переведено (в процентах от английского).</summary>
public sealed record LangInfo(string Code, string Native, int Coverage);

/// <summary>
/// Тексты интерфейса на любом числе языков. Словарь общий с версией 3.x
/// (Assets/strings.json), подстановки — в фигурных скобках: {name}.
/// Языки берутся из файлов программы (strings*.json) и из папки languages рядом с данными:
/// туда любой может положить свой перевод (es.json — как в strings.json или просто «ключ: текст»).
/// Если строки на выбранном языке нет — показывается английская, потом русская.
/// </summary>
public static partial class I18n
{
    static readonly Dictionary<string, Dictionary<string, string>> Tables = Load();

    /// <summary>Родные названия языков для списка выбора.</summary>
    static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = "Русский", ["en"] = "English", ["es"] = "Español", ["de"] = "Deutsch", ["fr"] = "Français", ["pt-BR"] = "Português (Brasil)",
        ["pt"] = "Português", ["it"] = "Italiano", ["pl"] = "Polski", ["uk"] = "Українська", ["tr"] = "Türkçe", ["zh-CN"] = "简体中文",
        ["zh-TW"] = "繁體中文", ["ja"] = "日本語", ["ko"] = "한국어", ["cs"] = "Čeština", ["nl"] = "Nederlands", ["sv"] = "Svenska",
        ["ar"] = "العربية", ["he"] = "עברית", ["id"] = "Bahasa Indonesia", ["vi"] = "Tiếng Việt", ["th"] = "ไทย", ["hu"] = "Magyar",
        ["ro"] = "Română", ["el"] = "Ελληνικά", ["fi"] = "Suomi", ["da"] = "Dansk", ["nb"] = "Norsk", ["bg"] = "Български",
        ["hi"] = "हिन्दी", ["fa"] = "فارسی", ["be"] = "Беларуская", ["kk"] = "Қазақша",
    };

    /// <summary>Языки, которые пишутся справа налево.</summary>
    static readonly HashSet<string> Rtl = new(StringComparer.OrdinalIgnoreCase) { "ar", "he", "fa", "ur" };

    public static string Lang { get; private set; } = "ru";
    public static event Action? Changed;

    /// <summary>Правила чисел и дат выбранного языка (запятая или точка, порядок дня и месяца).</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    public static bool IsRtl => Rtl.Contains(Lang.Split('-')[0]);

    static Dictionary<string, Dictionary<string, string>> Load()
    {
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase) { ["ru"] = new(), ["en"] = new() };
        try
        {
            // JsonNode, а не JsonSerializer: в урезанной сборке сериализация через отражение выключена.
            // strings.json — основной словарь, strings5.json — строки версии 5 (дополняют его),
            // strings-<раздел>.json — строки отдельных функций (у каждой свой файл, чтобы не мешать друг другу).
            var extra = new List<string>();
            try
            {
                extra = AssetLoader.GetAssets(new Uri("avares://ModLaunch/Assets/"), null)
                    .Select(u => u.AbsolutePath.Split('/')[^1])
                    .Where(n => n.StartsWith("strings-", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal).ToList();
            }
            catch { }
            foreach (var file in new[] { "strings.json", "strings5.json" }.Concat(extra))
            {
                try
                {
                    using var stream = AssetLoader.Open(new Uri($"avares://ModLaunch/Assets/{file}"));
                    Merge(tables, JsonNode.Parse(stream) as JsonObject, null);
                }
                catch { }
            }
        }
        catch { }

        // Переводы пользователей: папка languages (поверх встроенных).
        try
        {
            var dir = Path.Combine(Paths.DataDir, "languages");
            if (Directory.Exists(dir))
                foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
                    try { Merge(tables, JsonNode.Parse(File.ReadAllText(file)) as JsonObject, Path.GetFileNameWithoutExtension(file)); } catch { }
        }
        catch { }
        return tables;
    }

    /// <summary>Влить файл в таблицы. Либо {"es": {ключ: текст}}, либо плоский {ключ: текст} (тогда язык — имя файла).</summary>
    static void Merge(Dictionary<string, Dictionary<string, string>> tables, JsonObject? root, string? flatLang)
    {
        if (root is null) return;
        foreach (var (name, node) in root)
        {
            if (node is JsonObject table)
            {
                var target = tables.TryGetValue(name, out var t) ? t : tables[name] = new();
                foreach (var (k, v) in table) target[k] = v?.GetValue<string>() ?? "";
            }
            else if (flatLang is not null && node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                var target = tables.TryGetValue(flatLang, out var t) ? t : tables[flatLang] = new();
                target[name] = text;
            }
        }
    }

    /// <summary>Язык системы, если для него есть перевод, иначе английский.</summary>
    public static string Detect()
    {
        try
        {
            var ui = CultureInfo.CurrentUICulture;
            var full = ui.Name;                       // pt-BR, zh-TW
            var two = ui.TwoLetterISOLanguageName;    // pt, zh
            if (Exists(full)) return Canon(full);
            if (two == "zh")
            {
                var trad = full.Contains("TW") || full.Contains("HK") || full.Contains("Hant");
                return Exists(trad ? "zh-TW" : "zh-CN") ? (trad ? "zh-TW" : "zh-CN") : "en";
            }
            if (two == "pt" && Exists("pt-BR")) return "pt-BR";
            if (Exists(two)) return Canon(two);
        }
        catch { }
        return "en";
    }

    static bool Exists(string code) => Tables.ContainsKey(code) && Tables[code].Count > 20;

    static string Canon(string code) => Tables.Keys.FirstOrDefault(k => k.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? code;

    /// <summary>Выбрать язык: код, «auto» (как в системе) или неизвестный (тогда английский).</summary>
    public static void Set(string? lang)
    {
        lang = string.IsNullOrWhiteSpace(lang) || lang.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Detect() : Canon(lang);
        Lang = Tables.ContainsKey(lang) ? lang : "en";
        try { Culture = CultureInfo.GetCultureInfo(Lang); }
        catch { Culture = CultureInfo.InvariantCulture; }
        Changed?.Invoke();
    }

    /// <summary>Языки для списка выбора: сначала русский и английский, дальше по алфавиту кода. Почти пустые переводы не показываем.</summary>
    public static List<LangInfo> Available()
    {
        var total = Math.Max(1, Tables["en"].Count);
        return Tables
            .Where(t => t.Key is "ru" or "en" || t.Value.Count >= total / 20)
            .Select(t => new LangInfo(t.Key, Names.GetValueOrDefault(t.Key) ?? t.Value.GetValueOrDefault("lang.name") ?? t.Key, (int)Math.Min(100, t.Value.Count * 100L / total)))
            .OrderBy(l => l.Code == "ru" ? 0 : l.Code == "en" ? 1 : 2).ThenBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool Has(string key) => Tables.GetValueOrDefault(Lang)?.ContainsKey(key) == true;

    /// <summary>Все ключи словаря языка — для проверок.</summary>
    public static IReadOnlyCollection<string> Keys(string lang) => Tables.GetValueOrDefault(lang)?.Keys ?? (IReadOnlyCollection<string>)[];

    /// <summary>Строка на языке (без отката на другие): нужна проверкам.</summary>
    public static string? Raw(string lang, string key) => Tables.GetValueOrDefault(lang)?.GetValueOrDefault(key);

    public static string T(string key, params (string Name, object Value)[] args)
    {
        var text = Tables.GetValueOrDefault(Lang)?.GetValueOrDefault(key)
                   ?? Tables.GetValueOrDefault("en")?.GetValueOrDefault(key)
                   ?? Tables.GetValueOrDefault("ru")?.GetValueOrDefault(key)
                   ?? key;
        foreach (var (name, value) in args) text = text.Replace("{" + name + "}", value?.ToString());
        return Lang is "ru" or "uk" or "be" ? Typograph(text) : text;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<=^|[\s(«""])(в|во|и|с|со|к|ко|о|об|у|а|на|по|не|ни|за|из|от|до|для|без|под|над|при|про|через|или|но|да|же|й|та|ти|з|із|на|що|як)\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ShortWords();

    static readonly Dictionary<string, string> TypoCache = new();

    /// <summary>
    /// Типограф по правилам вёрстки: короткий предлог или союз не висит в конце строки
    /// (неразрывный пробел после него), тире не начинает строку (неразрывный пробел перед ним),
    /// число не отрывается от единицы («12 ч», «8 МБ»). Строки берутся из кэша.
    /// </summary>
    public static string Typograph(string text)
    {
        if (text.Length < 4 || !text.Contains(' ')) return text;
        lock (TypoCache)
        {
            if (TypoCache.TryGetValue(text, out var done)) return done;
            var t = ShortWords().Replace(text, m => m.Value.TrimEnd() + " ");
            t = t.Replace(" — ", " — ").Replace(" – ", " – ");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(\d) (ч|мин|с|сек|МБ|КБ|ГБ|%|₽|шт|дн|год|лет)\b", "$1 $2");
            if (TypoCache.Count > 5000) TypoCache.Clear();
            TypoCache[text] = t;
            return t;
        }
    }

    /// <summary>
    /// Форма множественного числа: one / few / many (ключи .one/.few/.many у переводчика).
    /// Русский, украинский, белорусский, польский, чешский, словацкий — по своим правилам; английский и похожие — one и few;
    /// китайский, японский, корейский, вьетнамский, тайский, индонезийский — без форм (все три одинаковы).
    /// </summary>
    public static string Plural(long n, string one, string few, string many) => PluralFor(Lang, n, one, few, many);

    public static string PluralFor(string language, long n, string one, string few, string many)
    {
        var lang = language.Split('-')[0].ToLowerInvariant();
        var m10 = n % 10;
        var m100 = n % 100;
        switch (lang)
        {
            case "ru" or "uk" or "be":
                if (m10 == 1 && m100 != 11) return one;
                if (m10 is >= 2 and <= 4 && (m100 < 10 || m100 >= 20)) return few;
                return many;
            case "pl":
                if (n == 1) return one;
                if (m10 is >= 2 and <= 4 && (m100 < 10 || m100 >= 20)) return few;
                return many;
            case "cs" or "sk":
                if (n == 1) return one;
                if (n is >= 2 and <= 4) return few;
                return many;
            case "zh" or "ja" or "ko" or "vi" or "th" or "id":
                return many;
            case "fr" or "pt" when n is 0 or 1:
                return one;
            default:
                return n == 1 ? one : few;
        }
    }

    /// <summary>Большие числа коротко: 12,3 тыс., 1,2 млн (1.2K / 1.2M) — суффиксы и порядок берутся из строк языка.</summary>
    public static string Compact(long n)
    {
        string F(double v) => v.ToString(v < 10 ? "0.#" : "0", Culture);
        if (n >= 1_000_000) return T("num.m", ("n", F(n / 1_000_000d)));
        if (n >= 1_000) return T("num.k", ("n", F(n / 1_000d)));
        return n.ToString(Culture);
    }
}
