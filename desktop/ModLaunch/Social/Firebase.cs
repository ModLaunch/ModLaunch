using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Platform;
using ModLaunch.Core;

namespace ModLaunch.Social;

/// <summary>Ошибка с кодом — тексты к кодам лежат в словаре (acc.err.*, friends.*, rev.*).</summary>
public sealed class ServiceError(string code, string? message = null) : Exception(message ?? code)
{
    public string Code { get; } = code;
}

/// <summary>Проект Firebase (общий для аккаунта, друзей и отзывов) и значения Firestore.</summary>
public static class Firebase
{
    public static readonly string ProjectId;
    public static readonly string ApiKey;

    static Firebase()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ModLaunch/Assets/reviews.config.json"));
            var json = JsonNode.Parse(stream);
            ProjectId = json.Str("projectId") ?? "";
            ApiKey = json.Str("apiKey") ?? "";
        }
        catch { ProjectId = ""; ApiKey = ""; }
    }

    public static bool Configured => ProjectId != "" && ApiKey != "";
    public static string Documents => $"projects/{ProjectId}/databases/(default)/documents";
    public static string Doc(string relative) => $"{Documents}/{relative}";
    public static string Url(string suffix = "") => $"https://firestore.googleapis.com/v1/{Documents}{suffix}?key={Uri.EscapeDataString(ApiKey)}";

    /// <summary>Разница часов сервера и наших (по заголовку Date) — для «в сети».</summary>
    public static TimeSpan Skew { get; private set; }

    public sealed record Reply(HttpStatusCode Status, JsonNode? Body);

    public static async Task<Reply> Send(string url, HttpMethod method, JsonNode? body = null, string? token = null, string? form = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Firebase-Locale", I18n.Lang == "en" ? "en" : "ru");
        if (token is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (form is not null) request.Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");
        else if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await Http.Client.SendAsync(request, cts.Token); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { throw new ServiceError("OFFLINE", e.Message); }
        using (response)
        {
            if (response.Headers.Date is DateTimeOffset date) Skew = date.UtcDateTime - DateTime.UtcNow;
            JsonNode? json = null;
            try { json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cts.Token)); } catch { }
            return new Reply(response.StatusCode, json);
        }
    }

    /// <summary>Текст ошибки из ответа Google: «STATUS: message».</summary>
    public static string Reason(Reply r)
    {
        var error = r.Body is JsonArray a ? a.FirstOrDefault()?["error"] : r.Body?["error"];
        var parts = new[] { error.Str("status"), error.Str("message") }.Where(s => !string.IsNullOrEmpty(s));
        var text = string.Join(": ", parts);
        return text == "" ? ((int)r.Status).ToString() : text;
    }

    // ---------------------------------------------------------------- значения Firestore

    public static JsonNode ToValue(object? value) => value switch
    {
        null => new JsonObject { ["nullValue"] = null },
        bool b => new JsonObject { ["booleanValue"] = b },
        int or long => new JsonObject { ["integerValue"] = Convert.ToInt64(value).ToString() },
        double d => new JsonObject { ["doubleValue"] = d },
        IEnumerable<string> list => new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(list.Select(x => ToValue(x)).ToArray()) } },
        _ => new JsonObject { ["stringValue"] = value.ToString() },
    };

    public static JsonObject ToFields(IDictionary<string, object?> values)
    {
        var o = new JsonObject();
        foreach (var (k, v) in values) o[k] = ToValue(v);
        return o;
    }

    public static object? FromValue(JsonNode? v)
    {
        if (v is not JsonObject o) return null;
        if (o["stringValue"] is JsonNode s) return s.GetValue<string>();
        if (o["integerValue"] is JsonNode i) return long.Parse(i.ToString());
        if (o["doubleValue"] is JsonNode d) return d.GetValue<double>();
        if (o["booleanValue"] is JsonNode b) return b.GetValue<bool>();
        if (o["timestampValue"] is JsonNode t) return t.GetValue<string>();
        if (o["arrayValue"] is JsonObject arr) return arr["values"] is JsonArray vals ? vals.Select(FromValue).ToList() : new List<object?>();
        if (o["mapValue"] is JsonObject map) return FromFields(map["fields"] as JsonObject);
        return null;
    }

    public static Dictionary<string, object?> FromFields(JsonObject? fields)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in fields ?? new JsonObject()) d[k] = FromValue(v);
        return d;
    }

    public static string S(this Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) && v is string s ? s : "";
    public static long L(this Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) ? v switch { long l => l, double x => (long)x, _ => 0 } : 0;
    public static bool B(this Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) && v is true;
    public static List<string> A(this Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) && v is List<object?> l ? l.OfType<string>().ToList() : [];

    /// <summary>Время Firestore в микросекундах — чтобы не терять порядок записей.</summary>
    public static long Micros(string? value)
    {
        var m = System.Text.RegularExpressions.Regex.Match(value ?? "", @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d+))?Z$");
        if (!m.Success) return DateTime.TryParse(value, out var p) ? new DateTimeOffset(p.ToUniversalTime()).ToUnixTimeMilliseconds() * 1000 : 0;
        var baseMs = DateTimeOffset.Parse(m.Groups[1].Value + "Z").ToUnixTimeMilliseconds();
        var frac = (m.Groups[2].Value + "000000")[..6];
        return baseMs * 1000 + long.Parse(frac);
    }

    /// <summary>Текст ошибки службы: err.{service}.{КОД} из словаря, с причиной.</summary>
    public static string Explain(string service, Exception e)
    {
        if (e is not ServiceError s) return Jobs.Explain(e);
        var key = $"err.{service}.{s.Code}";
        return I18n.Has(key) ? I18n.T(key, ("reason", s.Message)) : I18n.T($"err.{service}.SERVER", ("reason", s.Message));
    }

    public static DateTime? Time(string? value) => DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
