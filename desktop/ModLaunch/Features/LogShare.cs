using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 9.4: «Поделиться логом» — как в Modrinth App и Prism: лог уходит на mclo.gs (сервис для логов
/// Minecraft, сам вырезает IP-адреса и токены), в буфер обмена — короткая ссылка, которую можно
/// кинуть другу или в чат поддержки мода.
/// </summary>
public static class LogShare
{
    const string Api = "https://api.mclo.gs/1/log";

    /// <summary>mclo.gs принимает до 10 МБ и 25 000 строк — отправляем хвост лога (самое свежее и вылет).</summary>
    public static string Tail(string text, int maxLines = 25_000, int maxChars = 9_000_000)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > maxLines) text = string.Join('\n', lines[^maxLines..]);
        if (text.Length > maxChars) text = text[^maxChars..];
        // Своё имя пользователя Windows в путях — не для чужих глаз.
        var user = Environment.UserName;
        if (user.Length > 2) text = text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>Загрузить лог и вернуть ссылку на него.</summary>
    public static async Task<string> Upload(string file, CancellationToken ct = default)
    {
        string text;
        await using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var r = new StreamReader(f))
            text = await r.ReadToEndAsync(ct);
        if (text.Trim() == "") throw new InvalidOperationException(I18n.T("share.empty"));
        var body = "content=" + WebUtility.UrlEncode(Tail(text));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(40));
        using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await Http.Client.PostAsync(Api, content, cts.Token);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cts.Token));
        if (!response.IsSuccessStatusCode || json?["success"]?.GetValue<bool>() != true)
            throw new HttpRequestException(json.Str("error") ?? $"{(int)response.StatusCode} {response.ReasonPhrase}");
        return json.Str("url") ?? throw new HttpRequestException("mclo.gs: no url");
    }

    [SelfTest]
    static string TailKeepsTheEnd()
    {
        var text = string.Join('\n', Enumerable.Range(0, 30_000).Select(i => $"line {i}"));
        var tail = Tail(text);
        if (!tail.EndsWith("line 29999") || tail.Contains("line 4999\n")) throw new Exception("wrong part kept");
        return $"{tail.Split('\n').Length} of 30000 lines kept, the end";
    }
}
