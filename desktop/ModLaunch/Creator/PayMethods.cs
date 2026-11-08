using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Способ оплаты: id, пример реквизитов (подсказка в поле).</summary>
public sealed record PayMethod(string Id, string Example);

/// <summary>
/// 25.0: «чтобы все кошельки работали». Пополнение кошелька — перевод владельцу площадки любым удобным
/// способом (СБП, карта, ЮMoney, USDT, PayPal…) с кодом в комментарии и заявка, которую владелец
/// подтверждает (правила базы зачисляют деньги только вместе с записью «topup» на ту же сумму).
/// Реквизиты владелец вводит один раз — они лежат в config/market.pay и видны всем в окне пополнения.
/// Выплаты — те же способы, с проверкой формата реквизитов, чтобы деньги не ушли «в никуда».
/// </summary>
public static partial class PayMethods
{
    public static readonly PayMethod[] All =
    [
        new("sbp", "+7 900 123-45-67 · Т-Банк"),
        new("card", "2200 1234 5678 9010"),
        new("yoomoney", "4100 1234 5678 901"),
        new("usdt", "TRC20 · TXyz…"),
        new("paypal", "name@example.com"),
        new("other", ""),
    ];

    /// <summary>Реквизиты владельца для пополнения: способ → реквизиты. Пусто — способ не показывается.</summary>
    public static Dictionary<string, string> Owner { get; private set; } = [];
    static DateTime _loadedAt;
    public static event Action? Changed;

    /// <summary>Реквизиты уже прочитаны из базы: до этого форму владельца не сохраняем — иначе пустые поля
    /// затёрли бы сохранённые способы.</summary>
    public static bool Loaded => _loadedAt != default || Program.Demo || Program.Screenshot || !Firebase.Configured;

    [GeneratedRegex(@"\D")] private static partial Regex NotDigit();
    [GeneratedRegex(@"^T[1-9A-HJ-NP-Za-km-z]{33}$")] private static partial Regex Trc20();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex Email();

    /// <summary>Прочитать config/market (раз в 10 минут; force — сейчас): реквизиты и лот Premium.</summary>
    public static async Task Load(bool force = false)
    {
        if (Program.Demo || !Firebase.Configured) return;
        if (!force && DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(10)) return;
        try
        {
            var doc = await Hub.Call(Firebase.Url("/config/market"), HttpMethod.Get, null, null) as JsonObject;
            var f = Firebase.FromFields(doc?["fields"] as JsonObject);
            Owner = f.TryGetValue("pay", out var pay) && pay is Dictionary<string, object?> map
                ? map.Where(kv => kv.Value is string s && s.Trim() != "" && All.Any(m => m.Id == kv.Key)).ToDictionary(kv => kv.Key, kv => ((string)kv.Value!).Trim())
                : [];
            Features.Premium.SetListing(f.S("premium"),
                f.TryGetValue("premiumAll", out var all) && all is List<object?> ids ? ids.OfType<string>() : []);
            _loadedAt = DateTime.UtcNow;
            Changed?.Invoke();
        }
        catch { /* нет сети или документа — остаются прежние */ }
    }

    /// <summary>Сохранить реквизиты (только владелец; правила базы проверяют, что это админ).</summary>
    public static async Task Save(Dictionary<string, string> pay)
    {
        var clean = pay.Where(kv => All.Any(m => m.Id == kv.Key) && kv.Value.Trim() != "").ToDictionary(kv => kv.Key, kv => Hub.Clip(kv.Value.Trim(), 200));
        if (Program.Demo || Program.Screenshot) { Owner = clean; Changed?.Invoke(); return; }
        await SaveFields(new Dictionary<string, object?> { ["pay"] = clean });
        Owner = clean;
        Changed?.Invoke();
    }

    /// <summary>Записать поля config/market, не трогая остальные (процент комиссии, лот Premium, реквизиты).</summary>
    public static async Task SaveFields(Dictionary<string, object?> fields)
    {
        var (uid, token, _) = await Hub.Member();
        fields["by"] = uid;
        await Hub.Commit(
        [
            new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = Firebase.Doc("config/market"), ["fields"] = Firebase.ToFields(fields) },
                ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) },
                ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
            },
        ], token);
    }

    /// <summary>Код для комментария к переводу — по нему владелец находит заявку.</summary>
    public static string Code(string uid) => "ML-" + (uid.Length >= 8 ? uid[..8] : uid).ToUpperInvariant();

    /// <summary>Проверка реквизитов для выплаты. null — всё хорошо, иначе ключ строки с ошибкой.</summary>
    public static string? Check(string method, string details)
    {
        details = details.Trim();
        if (details.Length is 0 or > 200) return "v25.pay.bad.empty";
        var digits = NotDigit().Replace(details, "");
        return method switch
        {
            "sbp" => digits.Length is >= 10 and <= 12 ? null : "v25.pay.bad.phone",
            "card" => digits.Length is >= 16 and <= 19 && Luhn(digits) ? null : "v25.pay.bad.card",
            "yoomoney" => digits.Length is >= 11 and <= 20 && digits.StartsWith("41") ? null : "v25.pay.bad.yoomoney",
            "usdt" => Trc20().IsMatch(details.Replace("TRC20", "", StringComparison.OrdinalIgnoreCase).Trim(' ', ':', '·')) ? null : "v25.pay.bad.usdt",
            "paypal" => Email().IsMatch(details) ? null : "v25.pay.bad.email",
            _ => details.Length >= 4 ? null : "v25.pay.bad.empty",
        };
    }

    /// <summary>Контрольная цифра номера карты (алгоритм Луна) — опечатку видно сразу.</summary>
    public static bool Luhn(string digits)
    {
        var sum = 0;
        var odd = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (odd) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            odd = !odd;
        }
        return sum % 10 == 0;
    }

    [SelfTest]
    static string ChecksPayoutDetails()
    {
        var fail = "";
        if (Check("sbp", "+7 (900) 123-45-67") is not null || Check("sbp", "123") is null) fail += "phone; ";
        if (Check("card", "4111 1111 1111 1111") is not null || Check("card", "4111 1111 1111 1112") is null) fail += "card; ";
        if (Check("yoomoney", "4100 1234 5678 901") is not null || Check("yoomoney", "1234") is null) fail += "yoomoney; ";
        if (Check("usdt", "TRC20: TJYeasTPa6gpEEfYqP8RNyjsiNPCZCxvM8") is not null || Check("usdt", "0xabc") is null) fail += "usdt; ";
        if (Check("paypal", "me@mail.com") is not null || Check("paypal", "me") is null) fail += "paypal; ";
        if (Code("abcdef123456") != "ML-ABCDEF12") fail += "code; ";
        if (fail != "") throw new Exception(fail);
        return "СБП, карта (Луна), ЮMoney, USDT TRC20, PayPal: верные проходят, опечатки ловятся; код перевода ML-XXXXXXXX";
    }
}
