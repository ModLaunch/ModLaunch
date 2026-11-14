using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Social;

/// <summary>Второй шаг входа: пароль подошёл, теперь нужен код из приложения-аутентификатора.</summary>
public sealed record MfaChallenge(string Pending, string EnrollmentId, string Email, string Name);

/// <summary>Начатое подключение аутентификатора: секрет (base32), сессия и ссылка otpauth:// для приложения.</summary>
public sealed record TotpSetup(string Secret, string SessionInfo, int Digits, int Period, string Uri);

/// <summary>Подключённый второй фактор.</summary>
public sealed record SecondFactor(string Id, string Name, DateTime? Since);

/// <summary>
/// 30.0: двухфакторная защита входа — код из приложения-аутентификатора (TOTP: Google Authenticator, Microsoft
/// Authenticator, Aegis, 1Password…). Это функция Firebase Identity Platform: её включает владелец проекта
/// (TOTP MFA в консоли). Пока не включена — сервер отвечает OPERATION_NOT_ALLOWED, и программа говорит
/// «временно недоступно», ничего не ломая. Вход без 2FA работает как раньше.
/// </summary>
public static partial class Account
{
    const string IdentityV2 = "https://identitytoolkit.googleapis.com/v2";

    static string V2(string path) => $"{IdentityV2}/{path}?key={Uri.EscapeDataString(Firebase.ApiKey)}";

    /// <summary>Вход ждёт код: пароль верный, второй фактор ещё не введён.</summary>
    public static MfaChallenge? Pending { get; private set; }

    /// <summary>Ответ signInWithPassword с mfaPendingCredential — запомнить и попросить код (ошибка MFA_REQUIRED).</summary>
    static void NeedSecondFactor(JsonNode data, string email)
    {
        var info = data.Arr("mfaInfo").FirstOrDefault(m => m?["totpInfo"] is not null) ?? data.Arr("mfaInfo").FirstOrDefault();
        Pending = new MfaChallenge(data.Str("mfaPendingCredential")!, info.Str("mfaEnrollmentId") ?? "", email, CleanName(data.Str("displayName")));
        throw new ServiceError("MFA_REQUIRED");
    }

    /// <summary>uid из idToken (JWT): у ответа второго шага нет localId.</summary>
    internal static string? UidOf(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        var p = parts[1].Replace('-', '+').Replace('_', '/');
        p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
        try { return JsonNode.Parse(Convert.FromBase64String(p)).Str("user_id"); }
        catch (FormatException) { return null; }
    }

    /// <summary>Закончить вход кодом из приложения.</summary>
    public static async Task<Profile> SignInSecondFactor(string code)
    {
        var pending = Pending ?? throw new ServiceError("NO_MFA_SESSION");
        code = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != 6) throw new ServiceError("BAD_CODE");
        var data = await Request(V2("accounts/mfaSignIn:finalize"), new JsonObject
        {
            ["mfaPendingCredential"] = pending.Pending,
            ["mfaEnrollmentId"] = pending.EnrollmentId,
            ["totpVerificationInfo"] = new JsonObject { ["verificationCode"] = code },
        });
        var idToken = data.Str("idToken") ?? throw new ServiceError("SERVER", "no idToken");
        var uid = UidOf(idToken) ?? throw new ServiceError("SERVER", "no uid");
        Remember("user", uid, idToken, data.Str("refreshToken")!, "3600", new JsonObject
        {
            ["email"] = pending.Email,
            ["name"] = pending.Name is { Length: > 0 } n ? n : CleanName(pending.Email.Split('@')[0]),
            ["verified"] = false,
        });
        Pending = null;
        try { await RefreshProfile(); } catch { }
        Changed?.Invoke();
        return Get();
    }

    /// <summary>Подключённые факторы (из accounts:lookup).</summary>
    public static async Task<List<SecondFactor>> SecondFactors()
    {
        if (!SignedIn) return [];
        var (_, idToken) = await Token();
        var data = await Request(IdentityUrl("accounts:lookup"), new JsonObject { ["idToken"] = idToken });
        return (data["users"]?[0]).Arr("mfaInfo").Select(m => new SecondFactor(m.Str("mfaEnrollmentId") ?? "", m.Str("displayName") ?? "",
            Firebase.Time(m.Str("enrolledAt")))).Where(f => f.Id != "").ToList();
    }

    /// <summary>Начать подключение: сервер выдаёт секрет, человек добавляет его в приложение.</summary>
    public static async Task<TotpSetup> StartTotp()
    {
        if (!SignedIn) throw new ServiceError("SESSION");
        var (_, idToken) = await Token();
        var data = await Request(V2("accounts/mfaEnrollment:start"), new JsonObject { ["idToken"] = idToken, ["totpEnrollmentInfo"] = new JsonObject() });
        var s = data["totpSessionInfo"] ?? throw new ServiceError("SERVER", "no totpSessionInfo");
        var secret = s.Str("sharedSecretKey") ?? throw new ServiceError("SERVER", "no secret");
        var digits = (int)Math.Max(6, s.Long("verificationCodeLength"));
        var period = (int)Math.Max(30, s.Long("periodSec"));
        var email = Slot("user").Str("email") ?? "";
        var uri = $"otpauth://totp/ModLaunch:{Uri.EscapeDataString(email)}?secret={secret}&issuer=ModLaunch&algorithm=SHA1&digits={digits}&period={period}";
        return new TotpSetup(secret, s.Str("sessionInfo") ?? "", digits, period, uri);
    }

    /// <summary>Закончить подключение кодом из приложения (сервер выдаёт новый вход — запоминаем).</summary>
    public static async Task FinishTotp(TotpSetup setup, string code)
    {
        code = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != setup.Digits) throw new ServiceError("BAD_CODE");
        var (uid, idToken) = await Token();
        var data = await Request(V2("accounts/mfaEnrollment:finalize"), new JsonObject
        {
            ["idToken"] = idToken,
            ["displayName"] = "ModLaunch",
            ["totpVerificationInfo"] = new JsonObject { ["sessionInfo"] = setup.SessionInfo, ["verificationCode"] = code },
        });
        if (data.Str("idToken") is { } fresh && data.Str("refreshToken") is { } refresh) Remember("user", uid, fresh, refresh, "3600");
        Changed?.Invoke();
    }

    /// <summary>Выключить фактор (Firebase может попросить войти заново — RELOGIN).</summary>
    public static async Task RemoveSecondFactor(string enrollmentId)
    {
        var (uid, idToken) = await Token();
        var data = await Request(V2("accounts/mfaEnrollment:withdraw"), new JsonObject { ["idToken"] = idToken, ["mfaEnrollmentId"] = enrollmentId });
        if (data.Str("idToken") is { } fresh && data.Str("refreshToken") is { } refresh) Remember("user", uid, fresh, refresh, "3600");
        Changed?.Invoke();
    }

    /// <summary>Только для снимков экрана (данные во временной папке демо): вошедший игрок с подтверждённой почтой.</summary>
    internal static void DemoSignIn()
    {
        if (!Program.Screenshot) return;
        Remember("user", "demo-uid", "demo", "demo", "3600", new JsonObject { ["email"] = "player@example.com", ["name"] = "Player", ["verified"] = true, ["since"] = "2025-03-01T10:00:00Z" });
    }

    // ---------------------------------------------------------------- TOTP (RFC 6238) — для проверки ключа на этом ПК

    /// <summary>Base32 (RFC 4648) → байты; пробелы и регистр не важны.</summary>
    internal static byte[] Base32(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in text.ToUpperInvariant().Where(c => c is not (' ' or '-' or '=')))
        {
            var i = Alphabet.IndexOf(c);
            if (i < 0) throw new FormatException("base32");
            value = (value << 5) | i;
            bits += 5;
            if (bits >= 8) { output.Add((byte)((value >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return [.. output];
    }

    /// <summary>Код TOTP на момент времени (HMAC-SHA1, как у всех приложений-аутентификаторов).</summary>
    internal static string Totp(byte[] key, DateTimeOffset at, int digits = 6, int period = 30)
    {
        var counter = at.ToUnixTimeSeconds() / period;
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        var hash = HMACSHA1.HashData(key, msg);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % (int)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');
    }

    /// <summary>Секрет по четыре символа — так его проще перепечатать в приложение.</summary>
    public static string Grouped(string secret) => string.Join(" ", Enumerable.Range(0, (secret.Length + 3) / 4).Select(i => secret.Substring(i * 4, Math.Min(4, secret.Length - i * 4))));

    [SelfTest]
    static string TotpMatchesRfc6238()
    {
        // RFC 6238, приложение B: ключ «12345678901234567890», SHA1, 8 цифр.
        var key = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        (long T, string Code)[] vectors = [(59, "94287082"), (1111111109, "07081804"), (1111111111, "14050471"), (1234567890, "89005924"), (2000000000, "69279037")];
        foreach (var (t, code) in vectors)
            if (Totp(key, DateTimeOffset.FromUnixTimeSeconds(t), 8) != code) throw new Exception($"T={t}: {Totp(key, DateTimeOffset.FromUnixTimeSeconds(t), 8)} != {code}");
        if (!Base32("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ").SequenceEqual(key)) throw new Exception("base32");
        if (Grouped("ABCDEFGHIJ") != "ABCD EFGH IJ") throw new Exception("grouping");
        var jwt = "x." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"user_id\":\"u-42\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".y";
        if (UidOf(jwt) != "u-42") throw new Exception("jwt uid");
        return "RFC 6238 SHA1 vectors match, base32 secret decoded, uid read from the id token";
    }
}
