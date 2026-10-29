using Avalonia;

namespace ModLaunch.Core;

/// <summary>
/// 2.0: мера всего оформления — золотое сечение φ = 1,618.
/// Отступы — числа Фибоначчи (соседние относятся как φ), шрифты — лестница 13 → 21 → 34 → 55
/// (каждая ступень в φ раз больше; 16 и 26 — полшага, ×√φ), углы — 5 / 8 / 13 / 21.
/// Две колонки делят ширину как 61,8 % : 38,2 %, широкие картинки — прямоугольник 1,618 : 1.
/// Образцы, на которые смотрели: Linear (тихая боковая панель, один акцент, вложенные пункты),
/// приложение Apple TV (большой арт и светлая кнопка «Играть»), Vercel Geist (нейтральная шкала серого,
/// волосяные линии, моноширинный шрифт только для кода), библиотека Steam Deck (полки обложек).
/// </summary>
public static class Phi
{
    public const double Ratio = 1.6180339887;

    /// <summary>Отступы: 3 · 5 · 8 · 13 · 21 · 34 · 55 · 89.</summary>
    public const double S3 = 3, S5 = 5, S8 = 8, S13 = 13, S21 = 21, S34 = 34, S55 = 55, S89 = 89;

    /// <summary>Шрифты: подпись 11, мелкий 12, текст 14, заголовок карточки 16, раздела 21, страницы 34, крупный 55.</summary>
    public const double Caption = 11, Small = 12, Body = 14, H3 = 16, H2 = 21, Title = 26, H1 = 34, Display = 55;

    /// <summary>Углы: метки 5, кнопки и поля 8, карточки 13, большие сцены 21.</summary>
    public const double R5 = 5, R8 = 8, R13 = 13, R21 = 21;

    /// <summary>Поля страницы: 34 по бокам и сверху, 55 снизу.</summary>
    public static readonly Thickness Page = new(S34, S34, S34, S55);

    /// <summary>Большая и меньшая часть отрезка: total = major + minor, major / minor = φ.</summary>
    public static (double Major, double Minor) Split(double total) => (total / Ratio, total - total / Ratio);

    /// <summary>Высота прямоугольника φ : 1 для ширины.</summary>
    public static double Golden(double width) => width / Ratio;

    /// <summary>Звёздочки колонок «61,8 % : 38,2 %» для Grid.</summary>
    public const string Columns = "1.618*,*";

    [SelfTest]
    static string ScaleIsGolden()
    {
        // Соседние шаги шкал — почти ровно φ (числа Фибоначчи), а Split делит в той же пропорции.
        double[] space = [S5, S8, S13, S21, S34, S55, S89];
        for (var i = 1; i < space.Length; i++)
            if (Math.Abs(space[i] / space[i - 1] - Ratio) > 0.12) throw new Exception($"space {space[i - 1]} → {space[i]}");
        double[] type = [S13, H2, H1, Display];
        for (var i = 1; i < type.Length; i++)
            if (Math.Abs(type[i] / type[i - 1] - Ratio) > 0.02) throw new Exception($"type {type[i - 1]} → {type[i]}");
        var (major, minor) = Split(1000);
        if (Math.Abs(major / minor - Ratio) > 1e-6 || Math.Abs(major + minor - 1000) > 1e-9) throw new Exception("split");
        return "spacing and type steps are φ apart; split 61.8/38.2";
    }
}
