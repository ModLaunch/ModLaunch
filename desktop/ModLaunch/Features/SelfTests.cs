using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>Быстрые проверки общих частей программы (без сети).</summary>
static partial class SelfTests
{
    /// <summary>Тексты на двух языках: в русском и английском словаре одни и те же ключи.</summary>
    [SelfTest]
    static string StringsBothLanguages()
    {
        var ru = I18n.Keys("ru");
        var en = I18n.Keys("en");
        // --selfcheck идёт без интерфейса: ресурсы программы (а с ними и словари) там недоступны.
        if (ru.Count == 0 && Avalonia.Application.Current is null) return "skipped: no UI in --selfcheck (checked in --selftest)";
        var onlyRu = ru.Except(en).Take(5).ToList();
        if (ru.Count < 100) throw new Exception($"only {ru.Count} strings loaded");
        return $"{ru.Count} ru / {en.Count} en" + (onlyRu.Count > 0 ? $"; no English yet: {string.Join(", ", onlyRu)}" : "");
    }
}
