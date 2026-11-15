using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 31.0: «Устройства» в «Безопасности» и вопрос «Новый вход в аккаунт — это вы?» при запуске.
/// «Нет, не я» — письмо для смены пароля: новый пароль закрывает вход на чужом ПК.
/// </summary>
public static class DevicesUi
{
    static bool _asked;

    /// <summary>Через 20 секунд после запуска: записать этот ПК и спросить про новые входы.</summary>
    public static void CheckLater()
    {
        if (Program.Demo || Program.Screenshot) return;
        DispatcherTimer.RunOnce(() => _ = Check(), TimeSpan.FromSeconds(20));
        Account.Changed += () => { if (Account.SignedIn) DispatcherTimer.RunOnce(() => _ = Check(), TimeSpan.FromSeconds(5)); };
    }

    static async Task Check()
    {
        if (_asked || !Account.SignedIn) return;
        List<Device> alerts;
        try
        {
            await Devices.Register();
            alerts = await Devices.Alerts();
        }
        catch { return; }
        if (alerts.Count == 0) return;
        _asked = true;
        Dispatcher.UIThread.Post(() => Ask(alerts));
    }

    static void Ask(List<Device> alerts)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var list = Ui.Col(6);
        foreach (var d in alerts)
            list.Children.Add(Ui.Card(Ui.Col(2, Ui.Text(d.Name is { Length: > 0 } n ? n : "?", "h3"),
                Ui.Text($"{d.Os} · ModLaunch {d.App} · {Ui.Ago(d.First)}", "small muted")), 12));
        w.Dialog(I18n.T("v31.dev.alert.title"), Ui.Col(10, Ui.Text(I18n.T("v31.dev.alert.text"), "", wrap: true), list),
            Ui.Button(I18n.T("v31.dev.notMe"), async () =>
            {
                w.CloseDialog();
                try
                {
                    await Account.ResetPassword(null);
                    w.Toast(I18n.T("v31.dev.notMe.sent", ("email", Account.Get().Email ?? "")));
                }
                catch (Exception e) { w.Toast(Account.Explain(e), bad: true); }
            }, "", Icons.Alert),
            Ui.Button(I18n.T("v31.dev.itsMe"), async () =>
            {
                w.CloseDialog();
                foreach (var d in alerts) try { await Devices.Acknowledge(d); } catch { }
            }, "primary", Icons.Check));
    }

    /// <summary>Карточка «Устройства» в «Безопасности».</summary>
    public static Control Card()
    {
        var body = Ui.Col(6, Ui.Text(I18n.T("v31.dev.loading"), "muted"));
        var card = Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v31.dev.title"), "h3"), Ui.Text(I18n.T("v31.dev.text"), "small muted", wrap: true), body), 22);
        _ = Task.Run(async () =>
        {
            List<Device>? list = null;
            string? error = null;
            try { list = await Devices.List(); }
            catch (Exception e) { error = Account.Explain(e); }
            Dispatcher.UIThread.Post(() => Fill(body, list, error));
        });
        return card;
    }

    static void Fill(StackPanel body, List<Device>? list, string? error)
    {
        body.Children.Clear();
        if (error is not null) { body.Children.Add(Ui.Text(error, "small muted", wrap: true)); return; }
        if (list is null || list.Count == 0) { body.Children.Add(Ui.Text(I18n.T("v31.dev.none"), "muted")); return; }
        foreach (var d in list)
        {
            var item = d;
            var line = new DockPanel();
            var actions = Ui.Row(6);
            if (!item.Mine && !item.Ack)
                actions.Children.Add(Ui.Button(I18n.T("v31.dev.itsMe"), async () =>
                {
                    try { await Devices.Acknowledge(item); Fill(body, await Devices.List(), null); } catch (Exception e) { MainWindow.Current?.Toast(Account.Explain(e), bad: true); }
                }, "ghost", Icons.Check));
            if (!item.Mine)
                actions.Children.Add(Ui.Button(I18n.T("v31.dev.forget"), async () =>
                {
                    try { await Devices.Forget(item); Fill(body, await Devices.List(), null); } catch (Exception e) { MainWindow.Current?.Toast(Account.Explain(e), bad: true); }
                }, "ghost", Icons.Trash));
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            var name = Ui.Row(8, Ui.Text(item.Name is { Length: > 0 } n ? n : "?", "h3"));
            if (item.Mine) name.Children.Add(HubLook.Badge(I18n.T("v31.dev.this"), "good", Icons.Check));
            else if (!item.Ack) name.Children.Add(HubLook.Badge(I18n.T("v31.dev.new"), "accent", Icons.Alert));
            foreach (var c in name.Children) c.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(Ui.Col(2, name, Ui.Text(I18n.T("v31.dev.meta", ("os", item.Os), ("app", item.App), ("first", Ui.Ago(item.First)), ("last", Ui.Ago(item.Last))), "small muted", wrap: true)));
            body.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = line });
        }
    }
}
