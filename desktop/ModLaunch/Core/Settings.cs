using System.Text.Json.Nodes;

namespace ModLaunch.Core;

/// <summary>Настройки программы: settings.json, общий с версией на Electron.</summary>
public static class Settings
{
    static readonly JsonFile File = new(Paths.SettingsFile, () => new JsonObject
    {
        ["gamePaths"] = new JsonObject(),
        ["language"] = "auto",
        ["onboardingDone"] = false,
    });

    public static JsonObject Data => File.Data;
    public static void Save() => File.Save();

    public static string Language
    {
        get => Data.Str("language") is { Length: > 0 } l ? l : "auto";
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

    public static string? NexusApiKey => Secret("nexusApiKey");
    public static bool NexusPremium => Data.Bool("nexusPremium");

    // ---------------------------------------------------------------- 23.8: ключи (Nexus, ИИ) — не открытым текстом

    static readonly string[] SecretKeys = ["nexusApiKey", "aiKey"];
    static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("ModLaunch.settings.secret");

    /// <summary>Ключ, зашифрованный DPAPI для этого пользователя Windows (чужой пользователь или другой ПК его не прочтёт).</summary>
    public static JsonNode? Seal(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!OperatingSystem.IsWindows()) return value;
        var data = System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(value), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return new JsonObject { ["dpapi"] = Convert.ToBase64String(data) };
    }

    /// <summary>Расшифровать: старая запись открытым текстом читается как есть; не расшифровалась (другой ПК) — null.</summary>
    public static string? Unseal(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out var plain)) return plain == "" ? null : plain;
        if (node is not JsonObject o || o.Str("dpapi") is not { } blob || !OperatingSystem.IsWindows()) return null;
        try { return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(blob), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or FormatException) { return null; }
    }

    public static string? Secret(string key) => Unseal(Data[key]);

    public static void SetSecret(string key, string? value)
    {
        Data[key] = Seal(value);
        Save();
    }

    /// <summary>При запуске: ключи, сохранённые прежними версиями открытым текстом, — зашифровать.</summary>
    public static void MigrateSecrets()
    {
        var changed = false;
        foreach (var key in SecretKeys)
            if (Data[key] is JsonValue v && v.TryGetValue<string>(out var plain) && plain != "" && OperatingSystem.IsWindows()) { Data[key] = Seal(plain); changed = true; }
        if (changed) Save();
    }

    [SelfTest]
    static string SecretsAreSealed()
    {
        const string key = "nexus-test-key-1234567890";
        var sealedNode = Seal(key);
        if (OperatingSystem.IsWindows() && (sealedNode is not JsonObject || sealedNode.ToJsonString().Contains(key))) throw new Exception("key stored in plain text: " + sealedNode?.ToJsonString());
        if (Unseal(sealedNode) != key) throw new Exception("round trip");
        if (Unseal(JsonValue.Create("old-plain")) != "old-plain") throw new Exception("legacy plain key not read");
        if (Unseal(new JsonObject { ["dpapi"] = "AAAA" }) is not null || Unseal(new JsonObject { ["dpapi"] = "%%%" }) is not null) throw new Exception("garbage decrypted");
        if (Seal("") is not null || Seal(null) is not null) throw new Exception("empty key kept");
        return "Nexus and AI keys: DPAPI (current user), plain keys of older versions read and re-sealed at start";
    }
}
