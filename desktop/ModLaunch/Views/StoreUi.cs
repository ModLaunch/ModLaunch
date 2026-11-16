using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 31.0: покупка картой. Цена — в валюте человека (BYN, RUB, USD, EUR, можно сменить), оплата — на странице
/// платёжной системы в браузере, программа ждёт подтверждения сервера и сразу открывает покупку. Подарок — другу из
/// списка друзей. Чек сохраняется файлом. Сервер не подключён — «Оплата картой временно недоступна».
/// </summary>
public static class StoreUi
{
    /// <summary>Выбор валюты (общий для всего магазина).</summary>
    public static ComboBox CurrencyPicker(Action changed)
    {
        var box = new ComboBox { ItemsSource = Store.Currencies, SelectedItem = Store.Currency, MinWidth = 86 };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string c && c != Store.Currency) { Store.Currency = c; changed(); } };
        ToolTip.SetTip(box, I18n.T("v31.store.currency"));
        return box;
    }

    /// <summary>Цена лота: со скидкой — зачёркнутая старая и процент.</summary>
    public static Control PriceTag(Listing l, string currency, double size = 22)
    {
        var row = Ui.Row(8);
        if (Store.PriceFor(l, currency) is not { } price)
        {
            row.Children.Add(Ui.Text(I18n.T("v31.store.noPrice"), "small muted", wrap: true));
            return row;
        }
        if (Store.SaleActive(l) && l.Prices!.TryGetValue(currency, out var full))
        {
            row.Children.Add(HubLook.Badge($"−{l.SalePercent}%", "good"));
            row.Children.Add(new TextBlock { Text = Store.Money(full, currency), TextDecorations = TextDecorations.Strikethrough, Foreground = Ui.Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
        }
        row.Children.Add(new TextBlock { Text = Store.Money(price, currency), FontFamily = Look.Display, FontSize = size, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        foreach (var c in row.Children) c.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    /// <summary>Кнопки «Купить» и «Подарить» для лота с ценами для карты.</summary>
    public static Control BuyButtons(Listing l, Action afterPaid)
    {
        var currency = Store.Currency;
        var buy = Ui.Button(I18n.T("v31.store.buy"), () => Confirm("game", l.Id, l.Title, currency, null, afterPaid), "primary", Icons.Bag);
        buy.Padding = new Thickness(26, 12);
        buy.IsEnabled = Store.PriceFor(l, currency) is not null;
        var gift = Ui.Button(I18n.T("v31.store.gift"), () => PickFriend(l, currency, afterPaid), "", Icons.Heart);
        gift.IsEnabled = buy.IsEnabled;
        return Ui.Row(8, gift, buy);
    }

    static void PickFriend(Listing l, string currency, Action afterPaid)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var friends = Friends.View().Friends;
        if (friends.Count == 0) { w.Toast(I18n.T("v31.store.gift.noFriends"), bad: true); return; }
        var list = new ListBox { ItemsSource = friends.Select(f => f.Name).ToList(), SelectedIndex = 0, MaxHeight = 260 };
        var message = new TextBox { Watermark = I18n.T("v31.store.gift.message"), MaxLength = 300, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        w.Dialog(I18n.T("v31.store.gift.title", ("title", l.Title)), Ui.Col(10, Ui.Text(I18n.T("v31.store.gift.text"), "muted", wrap: true), list, message),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v31.store.gift.next"), () =>
            {
                if (list.SelectedIndex < 0) return;
                var friend = friends[list.SelectedIndex];
                w.CloseDialog();
                Confirm("gift", l.Id, l.Title, currency, friend.Uid, afterPaid, friendName: friend.Name, message: message.Text);
            }, "primary", Icons.Heart));
    }

    /// <summary>Подтверждение и оплата: что, сколько, кому — затем страница оплаты в браузере и ожидание результата.</summary>
    public static void Confirm(string type, string target, string title, string currency, string? giftTo, Action afterPaid,
        string? friendName = null, string? message = null, string? plan = null, long? amount = null)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        if (!Account.SignedIn) { w.Toast(I18n.T("v31.store.signIn"), bad: true); return; }
        if (!Account.Get().Verified) { w.Toast(I18n.T("err.store.VERIFY_FIRST"), bad: true); return; }
        var what = type == "gift" ? I18n.T("v31.store.confirm.gift", ("title", title), ("friend", friendName ?? "")) : title;
        w.Dialog(I18n.T("v31.store.confirm.title"), Ui.Col(10,
                Ui.Text(what, "h3", wrap: true),
                Ui.Text(I18n.T("v31.store.confirm.text"), "muted", wrap: true),
                Ui.Text(I18n.T("v31.store.confirm.refund"), "small muted", wrap: true)),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v31.store.pay"), async () =>
            {
                w.CloseDialog();
                Store.Checkout order;
                try { order = await Store.Order(type, target, currency, giftTo, plan, amount, message); }
                catch (Exception e) { w.Toast(Store.Explain(e), bad: true); return; }
                Ui.OpenUrl(order.Url);
                Wait(order, title, afterPaid);
            }, "primary", Icons.Bag));
    }

    /// <summary>Окно «Ждём оплату»: сервер получает подтверждение от платёжной системы — тогда покупка готова.</summary>
    static void Wait(Store.Checkout order, string title, Action afterPaid)
    {
        var w = MainWindow.Current!;
        var cts = new CancellationTokenSource();
        w.Dialog(I18n.T("v31.store.wait.title"), Ui.Col(12,
                Ui.Row(12, new ProgressBar { IsIndeterminate = true, Width = 160 }, Ui.Text(I18n.T("v31.store.wait.text", ("amount", Store.Money(order.Amount, order.Currency))), "", wrap: true)),
                order.Test ? Ui.Text(I18n.T("v31.store.wait.test"), "small", color: Ui.Res("Warn"), wrap: true) : new Panel(),
                Ui.Button(I18n.T("v31.store.wait.reopen"), () => Ui.OpenUrl(order.Url), "ghost", Icons.External)),
            Ui.Button(I18n.T("v31.store.wait.later"), () => { cts.Cancel(); w.CloseDialog(); w.Toast(I18n.T("v31.store.wait.laterText")); }));
        _ = Task.Run(async () =>
        {
            var result = await Store.WaitResult(order.PaymentId, cts.Token);
            if (cts.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                w.CloseDialog();
                switch (result?.Status)
                {
                    case "paid":
                        w.Toast(result.GiftTo is not null ? I18n.T("v31.store.paid.gift", ("title", title)) : I18n.T("v31.store.paid", ("title", title)));
                        afterPaid();
                        break;
                    case "failed": w.Toast(I18n.T("v31.store.failed"), bad: true); break;
                    case "mismatch": w.Toast(I18n.T("v31.store.mismatch"), bad: true); break;
                    default: w.Toast(I18n.T("v31.store.wait.laterText")); break;
                }
            });
        });
    }

    // ---------------------------------------------------------------- покупки, чеки, ключи

    /// <summary>Аккаунт → Покупки: оплаты картой с чеками (новые сверху).</summary>
    public static Control PaymentsCard()
    {
        var body = Ui.Col(6, Ui.Text(I18n.T("common.loading"), "muted"));
        var card = Ui.Card(Ui.Col(10,
            Ui.Row(10, Ui.Text(I18n.T("v31.store.payments"), "h3"), Ui.Button(I18n.T("v31.store.key.redeem"), RedeemKey, "ghost", Icons.Key)),
            body), 22);
        _ = Task.Run(async () =>
        {
            List<CardPayment>? list = null;
            string? error = null;
            try { list = await Store.MyPayments(); } catch (Exception e) { error = Store.Explain(e); }
            Dispatcher.UIThread.Post(() =>
            {
                body.Children.Clear();
                if (error is not null) { body.Children.Add(Ui.Text(error, "small muted", wrap: true)); return; }
                if (list!.Count == 0) { body.Children.Add(Ui.Text(I18n.T("v31.store.payments.none"), "muted")); return; }
                foreach (var p in list.Take(100))
                {
                    var item = p;
                    var line = new DockPanel();
                    var buttons = Ui.Row(6);
                    if (item.Status is "paid" or "refunded")
                        buttons.Children.Add(Ui.Button(I18n.T("v31.store.receipt"), async () =>
                        {
                            try
                            {
                                if (await Store.ReceiptFor(item.Id) is { } r) Ui.OpenUrl(Store.SaveReceipt(r));
                                else MainWindow.Current?.Toast(I18n.T("v31.store.receipt.none"), bad: true);
                            }
                            catch (Exception e) { MainWindow.Current?.Toast(Store.Explain(e), bad: true); }
                        }, "ghost", Icons.List));
                    DockPanel.SetDock(buttons, Dock.Right);
                    line.Children.Add(buttons);
                    var status = HubLook.Badge(I18n.T("v31.store.status." + item.Status), item.Status switch { "paid" => "good", "created" => "", _ => "accent" });
                    var head = Ui.Row(8, Ui.Text(item.Title, "h3"), status);
                    foreach (var c in head.Children) c.VerticalAlignment = VerticalAlignment.Center;
                    line.Children.Add(Ui.Col(2, head, Ui.Text($"{Store.Money(item.Amount, item.Currency)} · {Ui.Ago(item.PaidAt ?? item.Created)}{(item.Test ? " · TEST" : "")}", "small muted")));
                    body.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = line });
                }
            });
        });
        return card;
    }

    /// <summary>«Активировать ключ»: игра от разработчика (пресса, конкурс) — в библиотеку.</summary>
    public static void RedeemKey()
    {
        var w = MainWindow.Current;
        if (w is null) return;
        if (!Account.SignedIn) { w.Toast(I18n.T("v31.store.signIn"), bad: true); return; }
        var code = new TextBox { Watermark = "XXXXX-XXXXX-XXXXX", MaxLength = 40, Width = 280 };
        w.Dialog(I18n.T("v31.store.key.redeem"), Ui.Col(10, Ui.Text(I18n.T("v31.store.key.text"), "muted", wrap: true), code),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v31.store.key.go"), async () =>
            {
                try
                {
                    var (_, title) = await Store.RedeemKey(code.Text ?? "");
                    w.CloseDialog();
                    w.Toast(I18n.T("v31.store.key.done", ("title", title)));
                }
                catch (Exception e) { w.Toast(Store.Explain(e), bad: true); }
            }, "primary", Icons.Key));
    }

    /// <summary>При запуске: новые подарки от друзей.</summary>
    public static void CheckGiftsLater()
    {
        if (Program.Demo || Program.Screenshot) return;
        DispatcherTimer.RunOnce(() => _ = Task.Run(async () =>
        {
            List<Gift> gifts;
            try { gifts = await Store.NewGifts(); } catch { return; }
            foreach (var g in gifts)
            {
                var gift = g;
                Dispatcher.UIThread.Post(() =>
                {
                    var w = MainWindow.Current;
                    if (w is null) return;
                    w.Dialog(I18n.T("v31.store.gift.got.title"), Ui.Col(10,
                            Ui.Text(I18n.T("v31.store.gift.got", ("friend", gift.FromName), ("title", gift.Title)), "h3", wrap: true),
                            gift.Message != "" ? Ui.Text("«" + gift.Message + "»", "", wrap: true) : new Panel()),
                        Ui.Button(I18n.T("v31.store.gift.open"), async () =>
                        {
                            w.CloseDialog();
                            try { await Store.MarkSeen(gift); } catch { }
                            if (await Market.Get(gift.Listing) is { } l) MarketViews.Open(l);
                        }, "primary", Icons.Heart));
                });
            }
        }), TimeSpan.FromSeconds(25));
    }

    // ---------------------------------------------------------------- премиум и поддержка картой

    /// <summary>Подписка премиум картой: месяц или год в валюте человека (цены — config/store.premium).</summary>
    public static Control PremiumCard(Action changed)
    {
        var body = Ui.Col(8, Ui.Text(I18n.T("common.loading"), "muted"));
        _ = Task.Run(async () =>
        {
            Store.StoreConfig? cfg = null;
            try { cfg = await Store.Config(); } catch { }
            Dispatcher.UIThread.Post(() =>
            {
                body.Children.Clear();
                var cur = Store.Currency;
                if (cfg is null || !cfg.Enabled || !cfg.Premium.TryGetValue(cur, out var prices) || prices.Month <= 0 && prices.Year <= 0)
                {
                    body.Children.Add(Ui.Text(I18n.T("v31.store.off"), "small muted", wrap: true));
                    return;
                }
                if (Features.Premium.CardUntil is { } until && until > DateTime.UtcNow)
                    body.Children.Add(Ui.Text(I18n.T("v31.store.premium.until", ("date", until.ToLocalTime().ToString("d MMMM yyyy", I18n.Culture))), "small", color: Ui.Res("Good")));
                var row = Ui.Row(8, CurrencyPicker(changed));
                if (prices.Month > 0)
                    row.Children.Add(Ui.Button(I18n.T("v31.store.premium.month", ("price", Store.Money(prices.Month, cur))),
                        () => Confirm("premium", "month", I18n.T("v31.store.premium.monthTitle"), cur, null, () => { _ = Features.Premium.Refresh(); changed(); }, plan: "month"), "primary", Icons.Sparkles));
                if (prices.Year > 0)
                    row.Children.Add(Ui.Button(I18n.T("v31.store.premium.year", ("price", Store.Money(prices.Year, cur))),
                        () => Confirm("premium", "year", I18n.T("v31.store.premium.yearTitle"), cur, null, () => { _ = Features.Premium.Refresh(); changed(); }, plan: "year"), "", Icons.Sparkles));
                body.Children.Add(row);
            });
        });
        return Ui.Card(Ui.Col(8, Ui.Text(I18n.T("v31.store.premium.title"), "h3"), Ui.Text(I18n.T("v31.store.premium.text"), "small muted", wrap: true), body), 20);
    }

    /// <summary>Поддержать автора картой: сумма в валюте человека.</summary>
    public static void TipByCard(string authorUid, string authorName)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var cur = Store.Currency;
        var sum = new TextBox { Watermark = cur == "RUB" ? "300" : cur == "BYN" ? "10" : "5", Width = 140 };
        var note = new TextBox { Watermark = I18n.T("v31.store.tip.note"), MaxLength = 300, Width = 320 };
        w.Dialog(I18n.T("v31.store.tip.title", ("name", authorName)), Ui.Col(10, Ui.Text(I18n.T("v31.store.tip.text"), "muted", wrap: true),
                Ui.Row(8, sum, Ui.Text(Store.Symbol(cur), "muted")), note),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v31.store.tip.go"), () =>
            {
                if (!decimal.TryParse((sum.Text ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0)
                { w.Toast(I18n.T("mk.f.price.bad"), bad: true); return; }
                w.CloseDialog();
                Confirm("tip", authorUid, I18n.T("v31.store.tip.what", ("name", authorName)), cur, null, () => { }, message: note.Text, amount: (long)Math.Round(v * 100));
            }, "primary", Icons.Heart));
    }
}
