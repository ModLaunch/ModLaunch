using ModLaunch.Core;

namespace ModLaunch.Features;

/// <summary>
/// 25.0: «Сообщить о проблеме» (этап 2 плана). Лог уходит на mclo.gs (сервис сам прячет IP и токены,
/// имя пользователя Windows заменяется на &lt;user&gt;), а в браузере открывается готовый отчёт на GitHub:
/// версия программы и Windows, игра, число модов, что человек написал, ссылка на лог. Аккаунт GitHub
/// нужен только для отправки — ModLaunch ничего не отправляет сам, без нажатия человека.
/// </summary>
public static class BugReport
{
    public const string Repo = "ModLaunch/ModLaunch";

    /// <summary>Текст отчёта (Markdown) — то, что окажется в issue.</summary>
    public static string Body(string what, string? game, int mods, string? logLink, string? crash) =>
        $"""
        **Что случилось**
        {(string.IsNullOrWhiteSpace(what) ? "—" : what.Trim())}

        **ModLaunch** {Http.Version} · Windows {Environment.OSVersion.Version} · {I18n.Lang}
        **Игра** {game ?? "—"}{(game is null ? "" : $" · модов: {mods}")}
        **Разбор вылета** {crash ?? "—"}
        **Лог** {logLink ?? "—"}
        """;

    /// <summary>Ссылка «новый отчёт» с заполненными заголовком и текстом (GitHub режет адрес ~8 КБ — текст укорачиваем).</summary>
    public static string IssueUrl(string title, string body)
    {
        // Кириллица в адресе раздувается в 6 раз — режем текст, пока ссылка не влезет.
        var url = Make(title, body);
        while (url.Length > 7500 && body.Length > 100)
        {
            body = body[..(int)(body.Length * 0.8)] + "…";
            url = Make(title, body);
        }
        return url;
    }

    /// <summary>
    /// Отчёт целиком: длинный рассказ человека укорачивается первым — версия, игра, разбор вылета и ссылка
    /// на лог остаются (иначе загруженный лог потерялся бы в отрезанном хвосте).
    /// </summary>
    public static string IssueUrl(string title, string what, string? game, int mods, string? logLink, string? crash)
    {
        var url = Make(title, Body(what, game, mods, logLink, crash));
        while (url.Length > 7500 && what.Length > 40)
        {
            what = what[..(int)(what.Length * 0.8)].TrimEnd() + "…";
            url = Make(title, Body(what, game, mods, logLink, crash));
        }
        return url.Length > 7500 ? IssueUrl(title, Body(what, game, mods, logLink, crash)) : url;
    }

    static string Make(string title, string body)
    {
        title = title.Trim();
        if (title.Length > 120) title = title[..120];
        return $"https://github.com/{Repo}/issues/new?labels=bug&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }

    /// <summary>Загрузить лог (если есть и разрешили) и вернуть ссылку на отчёт.</summary>
    public static async Task<string> Prepare(string what, GameState? g, string? logPath, bool attachLog, string? crash)
    {
        string? link = null;
        if (attachLog && logPath is not null && File.Exists(logPath) && !Program.Demo)
            try { link = await LogShare.Upload(logPath); } catch { /* без лога — тоже отчёт */ }
        var title = (g is null ? "" : g.Def.ShortName + ": ") + (string.IsNullOrWhiteSpace(what) ? I18n.T("v25.bug.defaultTitle") : what.Trim().Split('\n')[0]);
        return IssueUrl(title, what, g?.Def.Name, g?.ModCount ?? 0, link, crash);
    }

    [SelfTest]
    static string BuildsIssueLink()
    {
        var url = IssueUrl("Stardew: игра падает", Body("Падает при загрузке фермы", "Stardew Valley", 42, "https://mclo.gs/abc", "Не хватает мода «Content Patcher»"));
        if (!url.StartsWith("https://github.com/ModLaunch/ModLaunch/issues/new?labels=bug&title=") || !url.Contains(Uri.EscapeDataString("https://mclo.gs/abc")) || url.Length > 8000)
            throw new Exception("bad issue url");
        var huge = IssueUrl(new string('x', 500), new string('y', 20000));
        if (huge.Length > 8000) throw new Exception("too long: " + huge.Length);
        // Длинный рассказ по-русски: режется он, а ссылка на лог и разбор вылета остаются.
        var russian = IssueUrl("Stardew: падает", new string('я', 1500), "Stardew Valley", 42, "https://mclo.gs/abc", "Не хватает мода");
        if (russian.Length > 7500 || !russian.Contains(Uri.EscapeDataString("https://mclo.gs/abc")) || !russian.Contains(Uri.EscapeDataString("Не хватает мода")))
            throw new Exception("log link lost when the text was cut");
        return $"issue link with version, game, mods, crash cause and log ({url.Length} chars); long text cut to fit, log link kept";
    }
}
