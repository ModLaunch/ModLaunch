using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 31.0: отчёты о сбоях. При запуске: если в прошлый раз программа упала, а человек ещё не решал — спросить,
/// можно ли отправлять отчёты (что в них есть — сказано прямо). Согласие можно поменять в настройках.
/// Админу — вкладка «Сбои»: самые частые ошибки по подписи, версии и последний текст ошибки.
/// </summary>
public static class CrashUi
{
    public static void CheckLater()
    {
        if (Program.Demo || Program.Screenshot) return;
        DispatcherTimer.RunOnce(() =>
        {
            if (CrashReports.Pending().Count == 0) return;
            if (CrashReports.Consent is null) Ask();
            else if (CrashReports.Consent == true) _ = Task.Run(CrashReports.Send);
        }, TimeSpan.FromSeconds(8));
    }

    static void Ask()
    {
        var w = MainWindow.Current;
        if (w is null) return;
        w.Dialog(I18n.T("v31.crash.ask.title"), Ui.Col(10,
                Ui.Text(I18n.T("v31.crash.ask.text"), "", wrap: true),
                Ui.Text(I18n.T("v31.crash.ask.what"), "small muted", wrap: true)),
            Ui.Button(I18n.T("v31.crash.ask.no"), () => { CrashReports.SetConsent(false); w.CloseDialog(); }),
            Ui.Button(I18n.T("v31.crash.ask.yes"), () =>
            {
                CrashReports.SetConsent(true);
                w.CloseDialog();
                _ = Task.Run(async () =>
                {
                    var sent = await CrashReports.Send();
                    if (sent > 0) Dispatcher.UIThread.Post(() => w.Toast(I18n.T("v31.crash.sent")));
                });
            }, "primary", Icons.Check));
    }
}

/// <summary>Админ: «Сбои программы» — что падает чаще всего.</summary>
public sealed class CrashesPanel : UserControl
{
    static List<(string Sig, int Count, AppCrash Latest, List<string> Versions)>? _top;
    static string? _error;
    static bool _loading;

    public CrashesPanel() => Render();

    void Render()
    {
        var col = Ui.Col(14);
        if (_top is null && _error is null)
        {
            if (!_loading)
            {
                _loading = true;
                _ = Task.Run(async () =>
                {
                    try { _top = await CrashReports.Top(); _error = null; }
                    catch (Exception e) { _error = Firebase.Explain("safety", e); }
                    _loading = false;
                    Dispatcher.UIThread.Post(Render);
                });
            }
            col.Children.Add(Ui.Card(Ui.Row(12, new ProgressBar { IsIndeterminate = true, Width = 120 }, Ui.Text(I18n.T("v31.crash.loading"), "muted")), 22));
            Content = col;
            return;
        }
        if (_error is not null) col.Children.Add(Ui.Card(Ui.Text(_error, "muted", wrap: true), 22));
        var top = _top ?? [];
        col.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("v31.crash.title"), "h3"),
            Ui.Text(I18n.T("v31.crash.summary", ("n", top.Sum(t => t.Count)), ("kinds", top.Count)), "small muted", wrap: true)), 20));
        if (top.Count == 0) col.Children.Add(Ui.Text(I18n.T("v31.crash.none"), "muted"));
        foreach (var (sig, count, latest, versions) in top.Take(40))
        {
            var head = Ui.Row(8, HubLook.Badge(count.ToString(I18n.Culture), latest.Kind == "fatal" ? "accent" : "", Icons.Bug), Ui.Text(latest.Type, "h3"));
            var body = Ui.Col(6, head,
                Ui.Text(latest.Message, "", wrap: true),
                Ui.Text(I18n.T("v31.crash.meta", ("sig", sig), ("versions", string.Join(", ", versions.Take(5))), ("when", Ui.Ago(latest.At)), ("os", latest.Os)), "small muted", wrap: true),
                new Expander
                {
                    Header = I18n.T("v31.crash.stack"),
                    Content = new SelectableTextBlock { Text = latest.Stack, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, monospace"), FontSize = 11 },
                });
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = body });
        }
        col.Children.Add(Ui.Button(I18n.T("cr.refresh"), () => { _top = null; _error = null; Render(); }, "ghost", Icons.Refresh));
        Content = col;
    }
}
