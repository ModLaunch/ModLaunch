using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Features;

static partial class SelfTests
{
    // «${price}» в тексте — пример кода, а не подстановка.
    [GeneratedRegex(@"(?<!\$)\{(\w+)\}")] private static partial Regex Placeholder();

    /// <summary>Перевод не должен терять или выдумывать {подстановки}: иначе в окне останется «{name}» или пропадёт число.</summary>
    [SelfTest]
    static string TranslationsKeepPlaceholders()
    {
        var checkedLangs = 0;
        foreach (var lang in I18n.Available().Select(l => l.Code).Where(c => c != "en"))
        {
            var bad = new List<string>();
            foreach (var key in I18n.Keys(lang))
            {
                var en = I18n.Raw("en", key) ?? I18n.Raw("ru", key);
                var tr = I18n.Raw(lang, key);
                if (en is null || tr is null) continue;
                var a = Placeholder().Matches(en).Select(m => m.Value).Order().ToList();
                var b = Placeholder().Matches(tr).Select(m => m.Value).Order().ToList();
                if (!a.SequenceEqual(b)) bad.Add(key);
            }
            if (bad.Count > 0) throw new Exception($"{lang}: placeholders differ in {bad.Count} strings, e.g. {string.Join(", ", bad.Take(4))}");
            checkedLangs++;
        }
        return $"{checkedLangs} languages keep their {{placeholders}}";
    }

    [SelfTest]
    static string PluralRulesPerLanguage()
    {
        var was = I18n.Lang;
        try
        {
            string P(string lang, long n) => I18n.PluralFor(lang, n, "one", "few", "many");
            if (P("ru", 1) != "one" || P("ru", 3) != "few" || P("ru", 11) != "many" || P("ru", 21) != "one") throw new Exception("ru");
            if (P("pl", 1) != "one" || P("pl", 22) != "few" || P("pl", 12) != "many") throw new Exception("pl");
            if (P("en", 1) != "one" || P("en", 2) != "few") throw new Exception("en");
            return "ru, pl and en plural forms are right";
        }
        finally { I18n.Set(was); }
    }

    [SelfTest]
    static string UnknownLanguageFallsBackToEnglish()
    {
        var was = I18n.Lang;
        try
        {
            I18n.Set("xx-unknown");
            if (I18n.Lang != "en") throw new Exception("got " + I18n.Lang);
            if (I18n.Detect().Length == 0) throw new Exception("detect empty");
            return "unknown code → English; system language detected";
        }
        finally { I18n.Set(was); }
    }
}
