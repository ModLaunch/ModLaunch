using System.Text.RegularExpressions;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>
/// «Заработает ли?»: мод просит другой загрузчик (MelonLoader, BepInEx для IL2CPP или BepInEx 6, UE4SS)
/// или сделан для старой версии игры. Раньше такие моды молча ставились в BepInEx/plugins и не работали.
/// </summary>
public static partial class Compat
{
    [GeneratedRegex(@"MelonLoader", RegexOptions.IgnoreCase)] private static partial Regex Melon();
    [GeneratedRegex(@"IL2CPP|BepInEx_Unity|BepInEx6|BepInEx_6", RegexOptions.IgnoreCase)] private static partial Regex Il2Cpp();
    [GeneratedRegex(@"UE4SS", RegexOptions.IgnoreCase)] private static partial Regex Ue4ss();

    /// <summary>Какой это загрузчик, если пакет — загрузчик (иначе null).</summary>
    public static string? LoaderOf(string packageId) =>
        Melon().IsMatch(packageId) ? "MelonLoader"
        : Ue4ss().IsMatch(packageId) ? "UE4SS"
        : Il2Cpp().IsMatch(packageId) && packageId.Contains("BepInEx", StringComparison.OrdinalIgnoreCase) ? "BepInEx IL2CPP"
        : null;

    /// <summary>Чужой загрузчик среди зависимостей: не тот, что ставит ModLaunch для этой игры.</summary>
    /// <summary>Моды, которые человек подтвердил («Всё равно поставить») — до закрытия программы.</summary>
    public static readonly HashSet<string> Confirmed = new(StringComparer.OrdinalIgnoreCase);

    public static string? Foreign(GameDef game, IEnumerable<string> dependencies)
    {
        foreach (var dep in dependencies)
        {
            // Свой загрузчик игры — «Автор-Пакет» или «Автор-Пакет-5.4.2100»; BepInExPack_IL2CPP — уже чужой.
            if (game.ThunderstorePackage is { } own && (dep.Equals(own, StringComparison.OrdinalIgnoreCase)
                || (dep.Length > own.Length + 1 && dep.StartsWith(own + "-", StringComparison.OrdinalIgnoreCase) && char.IsDigit(dep[own.Length + 1])))) continue;
            if (LoaderOf(dep) is { } loader) return loader;
        }
        return null;
    }

    /// <summary>Есть ли у игры что прятать переключателем «Только рабочие»: старая версия игры или устаревшие моды Thunderstore.</summary>
    public static bool CanFilter(GameDef game) => game.LegacyBefore is not null || game.PrimarySource == "thunderstore";

    /// <summary>Подпись для статуса SMAPI: null — всё хорошо, показывать нечего. Bad — красный бейдж.</summary>
    public static (string Key, bool Bad)? SmapiBadge(string? status) => status switch
    {
        "broken" => ("smapi.broken", true),
        "obsolete" => ("smapi.obsolete", true),
        "abandoned" => ("smapi.abandoned", true),
        "workaround" => ("smapi.workaround", false),
        "unofficial" => ("smapi.unofficial", false),
        _ => null,
    };

    /// <summary>Заработает ли мод (по тому, что уже известно о нём).</summary>
    public static bool Works(GameDef game, ModInfo mod) =>
        !mod.Deprecated && !game.IsLegacy(mod.UpdatedAt) && Foreign(game, mod.Dependencies.Concat(mod.Categories)) is null;

    [SelfTest]
    static string SmapiStatusesGetBadges()
    {
        if (SmapiBadge("ok") is not null || SmapiBadge("unknown") is not null) throw new Exception("a working mod got a badge");
        if (SmapiBadge("broken") is not { Bad: true } || SmapiBadge("unofficial") is not { Bad: false }) throw new Exception("wrong severity");
        return "ok/unknown quiet, broken red, unofficial amber";
    }

    [SelfTest]
    static string DeprecatedModsDoNotWork()
    {
        var lethal = GameCatalog.ById("lethal-company")!;
        var ok = new ModInfo { Source = "thunderstore", Id = "A-Fine", Name = "Fine", UpdatedAt = DateTime.UtcNow };
        var old = new ModInfo { Source = "thunderstore", Id = "A-Old", Name = "Old", UpdatedAt = DateTime.UtcNow, Deprecated = true };
        if (!Works(lethal, ok) || Works(lethal, old)) throw new Exception("deprecated mod counted as working");
        if (!CanFilter(lethal)) throw new Exception("Thunderstore game has nothing to filter");
        return "deprecated mods are hidden by Only working";
    }

    [SelfTest]
    static string DetectsForeignLoaders()
    {
        var lethal = GameCatalog.ById("lethal-company")!;
        if (Foreign(lethal, ["BepInEx-BepInExPack", "Evaisa-LethalLib"]) is not null) throw new Exception("own BepInEx flagged");
        if (Foreign(lethal, ["LavaGang-MelonLoader"]) != "MelonLoader") throw new Exception("MelonLoader not found");
        if (Foreign(lethal, ["BepInEx-BepInExPack_IL2CPP"]) != "BepInEx IL2CPP") throw new Exception("IL2CPP pack not found");
        if (Foreign(lethal, ["Thunderstore-UE4SS"]) != "UE4SS") throw new Exception("UE4SS not found");
        return "own pack ok; MelonLoader, BepInEx IL2CPP and UE4SS detected";
    }
}
