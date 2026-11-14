using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 30.0: двухфакторная защита в интерфейсе: код при входе, карточка в «Аккаунт → Безопасность» (включить/выключить)
/// и пошаговое подключение приложения-аутентификатора (ключ вручную или ссылкой otpauth://, проверка кода).
/// </summary>
public static class MfaUi
{
    /// <summary>Пароль подошёл, нужен код: спросить и закончить вход. null — человек передумал.</summary>
    public static Task<Profile?> AskCode()
    {
        var done = new TaskCompletionSource<Profile?>();
        if (MainWindow.Current is not { } w || Account.Pending is null) { done.SetResult(null); return done.Task; }
        var code = new TextBox { Watermark = "123456", MaxLength = 7, Width = 160, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var error = Ui.Text("", "small", color: Ui.Res("Bad"), wrap: true);
        Button? go = null;
        async Task Send()
        {
            go!.IsEnabled = false;
            error.Text = "";
            try
            {
                var p = await Account.SignInSecondFactor(code.Text ?? "");
                w.CloseDialog();
                done.TrySetResult(p);
            }
            catch (Exception e) { error.Text = Account.Explain(e); go.IsEnabled = true; }
        }
        go = Ui.Button(I18n.T("v30.mfa.signin"), () => _ = Send(), "primary", Icons.Key);
        code.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) _ = Send(); };
        w.Dialog(I18n.T("v30.mfa.code.title"), Ui.Col(10, Ui.Text(I18n.T("v30.mfa.code.text"), "muted", wrap: true), code, error),
            Ui.Button(I18n.T("common.cancel"), () => { w.CloseDialog(); done.TrySetResult(null); }), go);
        Dispatcher.UIThread.Post(() => code.Focus());
        return done.Task;
    }

    /// <summary>Карточка «Двухфакторная защита»: статус и кнопка (данные — с сервера, в фоне).</summary>
    public static Control Card(Profile p)
    {
        var body = Ui.Col(10, Ui.Text(I18n.T("v30.mfa.loading"), "small muted"));
        // Как соседние карточки «Безопасности»: заголовок h2, пояснение muted, отступ 24.
        var card = Ui.Card(Ui.Col(14,
            Ui.Text(I18n.T("v30.mfa.title"), "h2"),
            Ui.Text(I18n.T("v30.mfa.lead"), "muted", wrap: true), body), 24);

        void Render(List<SecondFactor>? factors, string? error)
        {
            body.Children.Clear();
            if (error is not null) { body.Children.Add(Ui.Text(error, "small muted", wrap: true)); return; }
            if (factors is { Count: > 0 })
            {
                var f = factors[0];
                body.Children.Add(Ui.Text("✓ " + (f.Since is DateTime at ? I18n.T("v30.mfa.on.since", ("date", at.ToLocalTime().ToString("d", I18n.Culture))) : I18n.T("v30.mfa.on")), "small", color: Ui.Res("Good")));
                var off = Ui.Button(I18n.T("v30.mfa.off"), () => Off(f, () => Load()), "ghost", Icons.Close);
                off.HorizontalAlignment = HorizontalAlignment.Left;
                body.Children.Add(off);
            }
            else
            {
                if (!p.Verified) body.Children.Add(Ui.Text(I18n.T("v30.mfa.verifyFirst"), "small", color: Ui.Res("Warn"), wrap: true));
                var on = Ui.Button(I18n.T("v30.mfa.enable"), () => Enable(() => Load()), "primary", Icons.Shield);
                on.HorizontalAlignment = HorizontalAlignment.Left;
                on.IsEnabled = p.Verified;
                body.Children.Add(on);
            }
        }
        void Load()
        {
            if (Program.Demo || Program.Screenshot) { Render([], null); return; }
            _ = Task.Run(async () =>
            {
                try { var f = await Account.SecondFactors(); Dispatcher.UIThread.Post(() => Render(f, null)); }
                catch (Exception e) { var text = Account.Explain(e); Dispatcher.UIThread.Post(() => Render(null, text)); }
            });
        }
        Load();
        return card;
    }

    /// <summary>Подключить приложение: ключ (вручную или ссылкой), код из приложения — готово.</summary>
    internal static async void Enable(Action done)
    {
        if (MainWindow.Current is not { } w) return;
        TotpSetup setup;
        try { setup = Program.Screenshot ? new TotpSetup("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", "", 6, 30, "otpauth://totp/ModLaunch:demo") : await Account.StartTotp(); }
        catch (ServiceError e) when (e.Code == "NOT_ENABLED") { w.Toast(I18n.T("v30.mfa.unavailable"), bad: true); return; }
        catch (Exception e) { w.Toast(Account.Explain(e), bad: true); return; }

        var key = new SelectableTextBlock { Text = Account.Grouped(setup.Secret), FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 17, FontWeight = FontWeight.SemiBold };
        var copy = Ui.Button(I18n.T("v30.mfa.copyKey"), async () => { if (w.Clipboard is { } c) await c.SetTextAsync(setup.Secret); w.Toast(I18n.T("v30.mfa.copied")); }, "", Icons.Copy);
        var link = Ui.Button(I18n.T("v30.mfa.copyLink"), async () => { if (w.Clipboard is { } c) await c.SetTextAsync(setup.Uri); w.Toast(I18n.T("v30.mfa.copied")); }, "ghost", Icons.Link);
        var code = new TextBox { Watermark = "123456", MaxLength = setup.Digits + 1, Width = 160, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var hint = Ui.Text("", "small", wrap: true);
        // Сверяем код на этом ПК (±30 с): опечатка в ключе или неточные часы видны сразу, ещё до сервера.
        code.TextChanged += (_, _) =>
        {
            var typed = new string((code.Text ?? "").Where(char.IsDigit).ToArray());
            if (typed.Length != setup.Digits) { hint.Text = ""; return; }
            var secret = Account.Base32(setup.Secret);
            var now = DateTimeOffset.UtcNow;
            var ok = new[] { -1, 0, 1 }.Any(d => Account.Totp(secret, now.AddSeconds(d * setup.Period), setup.Digits, setup.Period) == typed);
            hint.Text = ok ? "✓ " + I18n.T("v30.mfa.codeOk") : I18n.T("v30.mfa.codeBad");
            hint.Foreground = ok ? Ui.Res("Good") : Ui.Res("Warn");
        };
        var error = Ui.Text("", "small", color: Ui.Res("Bad"), wrap: true);
        Button? finish = null;
        async Task Finish()
        {
            finish!.IsEnabled = false;
            error.Text = "";
            try
            {
                await Account.FinishTotp(setup, code.Text ?? "");
                w.CloseDialog();
                w.Toast(I18n.T("v30.mfa.enabled"));
                done();
            }
            catch (Exception e) { error.Text = Account.Explain(e); finish.IsEnabled = true; }
        }
        finish = Ui.Button(I18n.T("v30.mfa.finish"), () => _ = Finish(), "primary", Icons.Check);
        var steps = Ui.Col(12,
            Ui.Text(I18n.T("v30.mfa.step1"), wrap: true),
            Ui.Text(I18n.T("v30.mfa.step2"), wrap: true),
            new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), Child = key },
            Ui.Row(8, copy, link),
            Ui.Text(I18n.T("v30.mfa.step3"), wrap: true),
            code, hint, error,
            Ui.Text(I18n.T("v30.mfa.warn"), "tiny muted", wrap: true));
        w.Dialog(I18n.T("v30.mfa.enable"), new ScrollViewer { Content = steps, MaxHeight = 560 }, 560, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), finish);
    }

    static void Off(SecondFactor f, Action done)
    {
        if (MainWindow.Current is not { } w) return;
        w.Dialog(I18n.T("v30.mfa.off.title"), Ui.Text(I18n.T("v30.mfa.off.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v30.mfa.off"), async () =>
            {
                w.CloseDialog();
                try { await Account.RemoveSecondFactor(f.Id); w.Toast(I18n.T("v30.mfa.disabled")); }
                catch (Exception e) { w.Toast(Account.Explain(e), bad: true); }
                done();
            }, "primary", Icons.Close));
    }
}
