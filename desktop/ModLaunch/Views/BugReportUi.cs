using Avalonia.Controls;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>25.0: окно «Сообщить о проблеме» — что случилось, приложить лог, и готовый отчёт открывается в браузере.</summary>
public static class BugReportUi
{
    public static void Show(GameState? g = null, string? crash = null)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        g ??= AppState.Games.FirstOrDefault(x => x.Def.Id == Settings.Data.Str("lastGame") && x.Status == Detect.Found);
        var log = g?.Path is { } path ? (g.Def.IsMinecraft && Minecraft.Mc.Active is { } a ? Path.Combine(a.Dir, "logs", "latest.log") : Logs.PathFor(g.Def, path)) : null;
        var what = new TextBox { Watermark = I18n.T("v25.bug.placeholder"), AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 110, MaxLength = 1500 };
        var attach = new CheckBox { Content = I18n.T("v25.bug.attach", ("game", g?.Def.Name ?? "—")), IsChecked = log is not null && File.Exists(log), IsEnabled = log is not null && File.Exists(log) };
        var body = Ui.Col(12,
            Ui.Text(I18n.T("v25.bug.text"), "muted", wrap: true),
            what, attach,
            Ui.Text(I18n.T("v25.bug.privacy"), "small muted", wrap: true));
        Button? send = null;
        send = Ui.Button(I18n.T("v25.bug.send"), async () =>
        {
            send!.IsEnabled = false;
            try
            {
                var url = await BugReport.Prepare(what.Text ?? "", g, log, attach.IsChecked == true, crash);
                w.CloseDialog();
                Ui.OpenUrl(url);
                w.Toast(I18n.T("v25.bug.opened"));
            }
            catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); send.IsEnabled = true; }
        }, "primary", Icons.Flag);
        w.Dialog(I18n.T("v25.bug.title"), body, 560, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), send);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => what.Focus());
    }
}
