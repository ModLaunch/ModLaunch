using System.Text.Json;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// Перенос настроек на другой компьютер: всё, кроме путей к играм, ключей и
/// токенов (они у каждого компьютера и аккаунта свои).
/// </summary>
public static class SettingsTransfer
{
    static readonly HashSet<string> Private = new(StringComparer.OrdinalIgnoreCase)
    {
        "gamePaths", "detected", "nexusApiKey", "nexusPremium", "account", "accounts", "friends", "lastGame", "customGames",
    };

    public static string Export()
    {
        var copy = new JsonObject { ["modlaunchSettings"] = 1, ["app"] = Http.Version };
        foreach (var (k, v) in Settings.Data)
            if (!Private.Contains(k) && !k.Contains("token", StringComparison.OrdinalIgnoreCase)) copy[k] = v?.DeepClone();
        return copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static int Import(string text)
    {
        if (JsonNode.Parse(text) is not JsonObject o || o["modlaunchSettings"] is null) throw new InvalidOperationException(I18n.T("sys.import.bad"));
        var n = 0;
        foreach (var (k, v) in o)
        {
            if (k is "modlaunchSettings" or "app" || Private.Contains(k)) continue;
            Settings.Data[k] = v?.DeepClone();
            n++;
        }
        Settings.Save();
        if (Settings.Data.Str("language") is string lang) I18n.Set(lang);
        return n;
    }
}
