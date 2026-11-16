using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 27.0: окно «Поддержать автора» / «Поддержать ModLaunch»: сумма (быстрые кнопки или своя), пара слов автору,
/// сколько он получит после комиссии и что на балансе. Деньги — с баланса ModLaunch (Creator/Tips.cs).
/// Не вошли, не хватает денег, поддержка ещё не включена на сервере — понятное сообщение и что делать.
/// </summary>
public static class TipUi
{
    public static void Show(string to, string toName)
    {
        if (MainWindow.Current is not { } w) return;
        var platform = to == Tips.Platform;
        long amount = Tips.Presets[1];
        var custom = new TextBox { Watermark = I18n.T("v27.tip.custom", ("currency", Market.Currency)), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var message = new TextBox { Watermark = I18n.T(platform ? "v27.tip.message.platform" : "v27.tip.message"), MaxLength = 300, AcceptsReturn = false };
        var note = Ui.Text("", "small muted", wrap: true);
        var balance = Ui.Text(Account.SignedIn ? I18n.T("v27.tip.balance.loading") : I18n.T("v27.tip.signIn"), "small muted", wrap: true);
        var chips = new WrapPanel();
        Button? send = null;

        void Refresh()
        {
            var net = amount - Tips.FeeFor(to, amount);
            note.Text = platform
                ? I18n.T("v27.tip.platform.note")
                : I18n.T("v27.tip.author.note", ("net", Market.Money(net)), ("fee", Market.FeePercent));
            chips.Children.Clear();
            foreach (var p in Tips.Presets)
            {
                var value = p;
                var b = Ui.Button(Market.Money(value), () => { amount = value; custom.Text = ""; Refresh(); }, value == amount && (custom.Text ?? "") == "" ? "chip active" : "chip");
                b.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(b);
            }
            var valid = amount >= Tips.Min && amount <= Tips.Max;
            if (send is not null)
            {
                send.IsEnabled = valid;
                send.Content = valid ? I18n.T("v27.tip.send", ("sum", Market.Money(amount))) : I18n.T("v27.tip.range", ("min", Market.Money(Tips.Min)), ("max", Market.Money(Tips.Max)));
            }
        }
        custom.TextChanged += (_, _) =>
        {
            if ((custom.Text ?? "").Trim() == "") return;
            amount = Market.ParseMoney(custom.Text) ?? 0;
            Refresh();
        };

        async Task Send()
        {
            if (send is null) return;
            send.IsEnabled = false;
            try
            {
                await Tips.Give(to, toName, amount, message.Text ?? "");
                w.CloseDialog();
                w.Toast(platform ? I18n.T("v27.tip.thanks.platform") : I18n.T("v27.tip.thanks", ("name", toName), ("net", Market.Money(amount - Tips.FeeFor(to, amount)))));
            }
            catch (ServiceError e) when (e.Code == "SIGN_IN")
            {
                w.CloseDialog();
                w.Toast(I18n.T("v27.tip.signIn"), bad: true);
                w.Navigate(() => new AccountPage());
            }
            catch (ServiceError e) when (e.Code == "NO_MONEY")
            {
                w.CloseDialog();
                w.Dialog(I18n.T("v27.tip.noMoney"), Ui.Text(I18n.T("v27.tip.noMoney.text", ("sum", Market.Money(amount))), "muted", wrap: true),
                    Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
                    Ui.Button(I18n.T("v27.tip.topUp"), () => { w.CloseDialog(); w.Navigate(() => new AccountPage("wallet")); }, "primary", Icons.Plus));
            }
            catch (ServiceError e) when (e.Code == "DENIED")
            {
                // Правила с поддержкой ещё не выложены на сервер — честно говорим, что функция пока не работает.
                w.CloseDialog();
                w.Toast(I18n.T("v27.tip.unavailable"), bad: true);
            }
            catch (ServiceError e) when (e.Code is "OWN" or "INVALID")
            {
                w.Toast(I18n.T(e.Code == "OWN" ? "v27.tip.own" : "v27.tip.range", ("min", Market.Money(Tips.Min)), ("max", Market.Money(Tips.Max))), bad: true);
                send.IsEnabled = true;
            }
            catch (Exception e)
            {
                w.Toast(Jobs.Explain(e), bad: true);
                send.IsEnabled = true;
            }
        }

        send = Ui.Button("", () => _ = Send(), "primary", Icons.Heart);
        Refresh();

        if (Account.SignedIn && !Program.Screenshot)
            _ = Task.Run(async () =>
            {
                string text;
                try { text = I18n.T("v27.tip.balance", ("sum", Market.Money((await Market.MyWallet()).Balance))); }
                catch { text = I18n.T("v27.tip.balance.unknown"); }
                Dispatcher.UIThread.Post(() => balance.Text = text);
            });
        else if (Program.Screenshot) balance.Text = I18n.T("v27.tip.balance", ("sum", Market.Money(125_000)));

        var body = Ui.Col(12,
            Ui.Text(platform ? I18n.T("v27.tip.platform.text") : I18n.T("v27.tip.author.text", ("name", toName)), "muted", wrap: true),
            chips,
            custom,
            message,
            note,
            balance);
        // 31.0: поддержать картой (в своей валюте) — без пополнения баланса ModLaunch.
        var card = Ui.Button(I18n.T("v31.store.tip.card"), () => { w.CloseDialog(); StoreUi.TipByCard(to, platform ? "ModLaunch" : toName); }, "", Icons.Bag);
        w.Dialog(platform ? I18n.T("v27.tip.platform.title") : I18n.T("v27.tip.author.title", ("name", toName)), body,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), card, send);
    }
}
