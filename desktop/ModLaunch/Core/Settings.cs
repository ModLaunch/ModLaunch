using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>Настройки программы: settings.json, общий с версией на Electron.</summary>
public static class Settings
{
    static readonly JsonFile File = new(Paths.SettingsFile, () => new JsonObject
    {
        ["gamePaths"] = new JsonObject(),
        ["language"] = "ru",
        ["onboardingDone"] = false,
    });

    public static JsonObject Data => File.Data;
    public static void Save() => File.Save();

    public static string Language
    {
        get => Data.Str("language") == "en" ? "en" : "ru";
        set { Data["language"] = value; Save(); }
    }

    /// <summary>Путь к игре: указанный человеком, иначе найденный сами (в 3.x — поле detected).</summary>
    public static string? GamePath(string gameId) => Data.Obj("gamePaths").Str(gameId) ?? (Data["detected"] as System.Text.Json.Nodes.JsonObject).Str(gameId);

    public static void SetGamePath(string gameId, string? path)
    {
        var paths = Data.Obj("gamePaths");
        if (path is null) paths.Remove(gameId); else paths[gameId] = path;
        Save();
    }

    public static string? NexusApiKey => Data.Str("nexusApiKey");
    public static bool NexusPremium => Data.Bool("nexusPremium");
}
