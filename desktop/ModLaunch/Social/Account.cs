using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Social;

public sealed record Profile(bool Configured, bool SignedIn, string? Uid, string? Email, string? Name, bool Verified, DateTime? Since, bool Admin, bool AdminPending);

/// <summary>
/// Аккаунт ModLaunch: Firebase Authentication по REST, как в 3.x. Без входа —
/// анонимный пользователь (отзывы всё равно работают). Токен обновления
/// хранится зашифрованным средствами Windows (DPAPI) в account.json.
/// </summary>
public static partial class Account
{
    const string Identity = "https://identitytoolkit.googleapis.com/v1";
    const string SecureToken = "https://securetoken.googleapis.com/v1";
    public const int MinPassword = 8;
    const int MaxName = 32;

    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "account.json"), () => new JsonObject());
    static readonly Dictionary<string, (string Uid, string IdToken, DateTime Expires)> Live = [];
    static readonly SemaphoreSlim TokenGate = new(1);

    public static event Action? Changed;

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$")] private static partial Regex EmailRe();

    static readonly (Regex Pattern, string Code)[] Codes =
    [
        (new(@"EMAIL_EXISTS"), "EMAIL_EXISTS"),
        (new(@"INVALID_EMAIL|MISSING_EMAIL"), "BAD_EMAIL"),
        (new(@"WEAK_PASSWORD|MISSING_PASSWORD|PASSWORD_DOES_NOT_MEET"), "WEAK_PASSWORD"),
        (new(@"INVALID_LOGIN_CREDENTIALS|EMAIL_NOT_FOUND|INVALID_PASSWORD"), "WRONG_LOGIN"),
        (new(@"USER_DISABLED"), "DISABLED"),
        (new(@"TOO_MANY_ATTEMPTS"), "TOO_MANY"),
        (new(@"OPERATION_NOT_ALLOWED|ADMIN_ONLY_OPERATION|CONFIGURATION_NOT_FOUND"), "NOT_ENABLED"),
        (new(@"CREDENTIAL_TOO_OLD"), "RELOGIN"),
        (new(@"TOKEN_EXPIRED|USER_NOT_FOUND|INVALID_REFRESH_TOKEN|INVALID_ID_TOKEN|INVALID_GRANT_TYPE"), "SESSION"),
        (new(@"RESOURCE_EXHAUSTED|quota|UNAVAILABLE|billing", RegexOptions.IgnoreCase), "BUSY"),
        (new(@"API.?key", RegexOptions.IgnoreCase), "BAD_KEY"),
    ];

    public static string CleanName(string? value)
    {
        var s = Regex.Replace(Regex.Replace(value ?? "", @"[\u0000-\u001f\u007f]", ""), @"\s+", " ").Trim();
        return s.Length > MaxName ? s[..MaxName] : s;
    }

    static JsonObject? Slot(string slot) => File.Data[slot] as JsonObject;

    // ---------------------------------------------------------------- хранение токена

    static JsonObject Seal(string token)
    {
        if (OperatingSystem.IsWindows())
        {
            try { return new JsonObject { ["dpapi"] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser)) }; }
            catch { }
        }
        return new JsonObject { ["raw"] = token };
    }

    static string? Open(JsonNode? sealedNode)
    {
        if (sealedNode is JsonValue v && v.TryGetValue<string>(out var plain)) return plain;
        if (sealedNode is not JsonObject o) return null;
        if (o.Str("dpapi") is string d && OperatingSystem.IsWindows())
        {
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(d), null, DataProtectionScope.CurrentUser)); } catch { return null; }
        }
        // «enc» — шифрование Electron из 3.x: здесь его не прочесть, войти придётся ещё раз.
        return o.Str("raw");
    }

    // ---------------------------------------------------------------- сеть

    static async Task<JsonNode> Request(string url, JsonNode? body = null, string? form = null)
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        var reply = await Firebase.Send(url, HttpMethod.Post, body, form: form);
        if ((int)reply.Status is >= 200 and < 300) return reply.Body ?? new JsonObject();
        var reason = Firebase.Reason(reply);
        var code = Codes.FirstOrDefault(c => c.Pattern.IsMatch(reason)).Code ?? "SERVER";
        throw new ServiceError(code, reason);
    }

    static string IdentityUrl(string path) => $"{Identity}/{path}?key={Uri.EscapeDataString(Firebase.ApiKey)}";

    // ---------------------------------------------------------------- кто сейчас

    public static Profile Get()
    {
        var user = Slot("user");
        var admin = user.Bool("admin");
        var verified = user.Bool("verified");
        return new Profile(Firebase.Configured, user is not null, user.Str("uid") ?? Slot("anon").Str("uid"),
            user.Str("email"), user.Str("name"), verified, Firebase.Time(user.Str("since")), admin && verified, admin && !verified);
    }

    public static bool SignedIn => Slot("user") is not null;
    public static string? Uid => Slot("user").Str("uid") ?? Slot("anon").Str("uid");

    /// <summary>Токен для запросов: пользователя, если вошли, иначе анонимный (создаётся сам).</summary>
    public static async Task<(string Uid, string IdToken)> Token()
    {
        var slot = SignedIn ? "user" : "anon";
        if (Live.TryGetValue(slot, out var live) && live.Expires - TimeSpan.FromMinutes(1) > DateTime.UtcNow && live.Uid == Slot(slot).Str("uid"))
            return (live.Uid, live.IdToken);
        await TokenGate.WaitAsync();
        try
        {
            if (Live.TryGetValue(slot, out live) && live.Expires - TimeSpan.FromMinutes(1) > DateTime.UtcNow && live.Uid == Slot(slot).Str("uid"))
                return (live.Uid, live.IdToken);
            var refresh = Open(Slot(slot)?["refreshToken"]);
            if (refresh is not null)
            {
                try { return await Refresh(slot, refresh); }
                catch (ServiceError e) when (e.Code == "SESSION")
                {
                    File.Data.Remove(slot);
                    Live.Remove(slot);
                    File.Save();
                    if (slot == "user") { Changed?.Invoke(); throw; }
                }
            }
            if (slot == "user") throw new ServiceError("SESSION");
            var data = await Request(IdentityUrl("accounts:signUp"), new JsonObject { ["returnSecureToken"] = true });
            return Remember("anon", data.Str("localId")!, data.Str("idToken")!, data.Str("refreshToken")!, data.Str("expiresIn"));
        }
        finally { TokenGate.Release(); }
    }

    static async Task<(string, string)> Refresh(string slot, string refreshToken)
    {
        var data = await Request($"{SecureToken}/token?key={Uri.EscapeDataString(Firebase.ApiKey)}",
            form: $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(refreshToken)}");
        return Remember(slot, data.Str("user_id")!, data.Str("id_token")!, data.Str("refresh_token") ?? refreshToken, data.Str("expires_in"));
    }

    static (string, string) Remember(string slot, string uid, string idToken, string refreshToken, string? expiresIn, JsonObject? extra = null)
    {
        Live[slot] = (uid, idToken, DateTime.UtcNow.AddSeconds(double.TryParse(expiresIn, out var s) ? s : 3600));
        var before = Slot(slot) ?? new JsonObject();
        var next = (JsonObject)before.DeepClone();
        foreach (var (k, v) in extra ?? new JsonObject()) next[k] = v?.DeepClone();
        next["uid"] = uid;
        next["refreshToken"] = Seal(refreshToken);
        File.Data[slot] = next;
        File.Save();
        return (uid, idToken);
    }

    // ---------------------------------------------------------------- регистрация и вход

    static (string Name, string Email, string Password) Check(string? name, string? email, string? password, bool needName)
    {
        var mail = (email ?? "").Trim().ToLowerInvariant();
        if (!EmailRe().IsMatch(mail) || mail.Length > 254) throw new ServiceError("BAD_EMAIL");
        if (password is null || password.Length < MinPassword || password.Length > 128) throw new ServiceError("WEAK_PASSWORD");
        var nick = CleanName(name);
        if (needName && nick == "") throw new ServiceError("NO_NAME");
        return (nick, mail, password);
    }

    public static async Task<Profile> SignUp(string name, string email, string password)
    {
        if (SignedIn) throw new ServiceError("ALREADY");
        var (nick, mail, pass) = Check(name, email, password, needName: true);
        JsonNode? created = null;
        string? createdUid = null;
        // Анонимный пользователь становится настоящим — его отзывы остаются за ним.
        if (Open(Slot("anon")?["refreshToken"]) is not null)
        {
            try
            {
                var anon = await Token();
                created = await Request(IdentityUrl("accounts:update"), new JsonObject { ["idToken"] = anon.IdToken, ["email"] = mail, ["password"] = pass, ["returnSecureToken"] = true });
                createdUid = created.Str("localId") ?? anon.Uid;
            }
            catch (ServiceError e) when (e.Code == "SESSION") { created = null; }
        }
        if (created is null)
        {
            created = await Request(IdentityUrl("accounts:signUp"), new JsonObject { ["email"] = mail, ["password"] = pass, ["returnSecureToken"] = true });
            createdUid = created.Str("localId");
        }
        Remember("user", createdUid!, created.Str("idToken")!, created.Str("refreshToken")!, created.Str("expiresIn"),
            new JsonObject { ["email"] = mail, ["name"] = nick, ["verified"] = false, ["since"] = DateTime.UtcNow.ToString("o") });
        if (Slot("anon").Str("uid") == createdUid) { File.Data.Remove("anon"); Live.Remove("anon"); File.Save(); }
        try { await SetName(nick); } catch { }
        try { await SendVerification(); } catch { }
        Changed?.Invoke();
        return Get();
    }

    public static async Task<Profile> SignIn(string email, string password)
    {
        var mail = (email ?? "").Trim().ToLowerInvariant();
        if (!EmailRe().IsMatch(mail)) throw new ServiceError("BAD_EMAIL");
        if (string.IsNullOrEmpty(password)) throw new ServiceError("WRONG_LOGIN");
        var data = await Request(IdentityUrl("accounts:signInWithPassword"), new JsonObject { ["email"] = mail, ["password"] = password, ["returnSecureToken"] = true });
        Remember("user", data.Str("localId")!, data.Str("idToken")!, data.Str("refreshToken")!, data.Str("expiresIn"), new JsonObject
        {
            ["email"] = data.Str("email") ?? mail,
            ["name"] = CleanName(data.Str("displayName")) is { Length: > 0 } n ? n : CleanName(mail.Split('@')[0]),
            ["verified"] = false,
        });
        try { await RefreshProfile(); } catch { }
        Changed?.Invoke();
        return Get();
    }

    public static Profile SignOut()
    {
        File.Data.Remove("user");
        Live.Remove("user");
        File.Save();
        Changed?.Invoke();
        return Get();
    }

    public static async Task<Profile> RefreshProfile()
    {
        if (!SignedIn) return Get();
        var (_, idToken) = await Token();
        var data = await Request(IdentityUrl("accounts:lookup"), new JsonObject { ["idToken"] = idToken });
        if (data["users"]?[0] is JsonNode info && Slot("user") is JsonObject user)
        {
            if (info.Bool("emailVerified") && !user.Bool("verified")) Live.Remove("user"); // новый токен с отметкой о почте
            user["email"] = info.Str("email") ?? user.Str("email");
            if (CleanName(info.Str("displayName")) is { Length: > 0 } n) user["name"] = n;
            user["verified"] = info.Bool("emailVerified");
            if (long.TryParse(info.Str("createdAt"), out var ms) && ms > 0) user["since"] = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("o");
            File.Save();
        }
        try { await CheckAdmin(); } catch { }
        Changed?.Invoke();
        return Get();
    }

    /// <summary>Админ — тот, чья почта есть в коллекции admins (читать её может только он сам).</summary>
    static async Task CheckAdmin()
    {
        if (Slot("user") is not JsonObject user || user.Str("email") is not string email) return;
        var (_, idToken) = await Token();
        var reply = await Firebase.Send(Firebase.Url($"/admins/{Uri.EscapeDataString(email)}"), HttpMethod.Get, token: idToken);
        if (reply.Status == System.Net.HttpStatusCode.OK) user["admin"] = true;
        else if (reply.Status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden) user["admin"] = false;
        File.Save();
    }

    static async Task SetName(string name)
    {
        var (_, idToken) = await Token();
        await Request(IdentityUrl("accounts:update"), new JsonObject { ["idToken"] = idToken, ["displayName"] = name });
        if (Slot("user") is JsonObject user) { user["name"] = name; File.Save(); }
    }

    public static async Task<Profile> Rename(string value)
    {
        if (!SignedIn) throw new ServiceError("SESSION");
        var name = CleanName(value);
        if (name == "") throw new ServiceError("NO_NAME");
        await SetName(name);
        Changed?.Invoke();
        return Get();
    }

    public static async Task SendVerification()
    {
        if (!SignedIn) throw new ServiceError("SESSION");
        var (_, idToken) = await Token();
        await Request(IdentityUrl("accounts:sendOobCode"), new JsonObject { ["requestType"] = "VERIFY_EMAIL", ["idToken"] = idToken });
    }

    public static async Task ResetPassword(string? email)
    {
        var mail = (email ?? Slot("user").Str("email") ?? "").Trim().ToLowerInvariant();
        if (!EmailRe().IsMatch(mail)) throw new ServiceError("BAD_EMAIL");
        await Request(IdentityUrl("accounts:sendOobCode"), new JsonObject { ["requestType"] = "PASSWORD_RESET", ["email"] = mail });
    }

    public static async Task Delete(string password)
    {
        if (Slot("user") is not JsonObject user) throw new ServiceError("SESSION");
        var fresh = await Request(IdentityUrl("accounts:signInWithPassword"), new JsonObject { ["email"] = user.Str("email"), ["password"] = password, ["returnSecureToken"] = true });
        if (fresh.Str("localId") != user.Str("uid")) throw new ServiceError("WRONG_LOGIN");
        await Request(IdentityUrl("accounts:delete"), new JsonObject { ["idToken"] = fresh.Str("idToken") });
        SignOut();
    }

    /// <summary>Понятный текст ошибки аккаунта.</summary>
    public static string Explain(Exception e) => Firebase.Explain("account", e);
}
