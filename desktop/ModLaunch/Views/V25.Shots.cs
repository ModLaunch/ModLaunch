using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Снимки 25.0 (MODLAUNCH_SHOTS=V25): Premium без и с покупкой, кошелёк, комиссия владельца, «Найти виновника», «Сообщить о проблеме».</summary>
public static class V25Shots
{
    [DemoShots]
    static void V25Jubilee(Shots s)
    {
        var w = s.Window;
        Premium.Demo(false);
        w.Navigate(() => new AccountPage("premium"));
        s.Pump(1200);
        s.Save("v25-1-premium-offer");
        V23Shots.Scroll(s, 1);
        s.Save("v25-2-premium-perks");

        Premium.Demo(true);
        Premium.Count("crashFixed", 3);
        Premium.Count("modsUpdated", 27);
        Premium.Count("codesShared", 2);
        w.Navigate(() => new AccountPage("premium"));
        s.Pump(1200);
        s.Save("v25-3-premium-active");
        V23Shots.Scroll(s, 1);
        s.Save("v25-4-premium-stats");

        _ = PayMethods.Save(new Dictionary<string, string>
        {
            ["sbp"] = "+7 900 123-45-67 · Т-Банк · Максим", ["card"] = "2200 7012 3456 7890", ["yoomoney"] = "4100 1234 5678 901", ["usdt"] = "TRC20 · TJYeasTPa6gpEEfYqP8RNyjsiNPCZCxvM8",
        });
        s.Pump(200);
        WalletPanel.TopUpDialog(() => { });
        s.Pump(600);
        s.Save("v25-5-topup");
        w.CloseDialog();
        WalletPanel.PayoutDialog(123_400, () => { });
        s.Pump(300);
        s.Save("v25-6-payout");
        w.CloseDialog();

        w.Dialog(I18n.T("v14.mk.tab.commission"), new ScrollViewerHost(new CommissionPanel()), 1100);
        s.Pump(1500);
        s.Save("v25-7-commission");
        w.CloseDialog();

        w.OpenAccountPanel();
        s.Pump(300);
        s.Save("v25-8-account-menu");
        w.CloseAccountPanel();

        // «Найти виновника» на Lethal Company.
        var lc = AppState.Game("lethal-company");
        var registry = lc.Registry!;
        foreach (var (id, name) in new[] { ("x753-More_Suits", "More Suits"), ("Evaisa-LethalThings", "LethalThings"), ("notnotnotswipez-MoreCompany", "MoreCompany"), ("tinyhoot-ShipLoot", "ShipLoot"), ("anormaltwig-LateCompany", "LateCompany") })
        {
            Directory.CreateDirectory(Path.Combine(registry.ModsDir, id));
            registry.Add(new JsonObject { ["id"] = id, ["name"] = name, ["folder"] = id, ["version"] = "1.0.0", ["source"] = "thunderstore" });
        }
        AppState.Notify();
        BisectUi.Offer(lc);
        s.Pump(300);
        s.Save("v25-9-bisect-offer");
        w.CloseDialog();
        Bisect.Start(lc);
        BisectUi.Ask(lc);
        s.Pump(300);
        s.Save("v25-10-bisect-ask");
        w.CloseDialog();
        w.Navigate(() => new GamePage("lethal-company", "health"));
        s.Pump(900);
        V23Shots.Scroll(s, 1);
        s.Save("v25-11-bisect-health");
        // Остался один подозреваемый — проверочный запуск без него.
        while (Bisect.Get(lc.Def.Id) is { Verify: null }) Bisect.Answer(lc, crashed: true);
        BisectUi.Ask(lc);
        s.Pump(300);
        s.Save("v25-11b-bisect-verify");
        w.CloseDialog();
        Bisect.Cancel(lc);

        BugReportUi.Show(lc, "Не хватает мода «LethalLib»");
        s.Pump(300);
        s.Save("v25-12-report");
        w.CloseDialog();

        w.Navigate(() => new ProfilePage("rewards"));
        s.Pump(1200);
        s.Save("v25-13-rewards");
        Premium.Demo(null);
    }

    /// <summary>Панель в окне — с прокруткой и нужной шириной.</summary>
    sealed class ScrollViewerHost : Avalonia.Controls.ScrollViewer
    {
        public ScrollViewerHost(Avalonia.Controls.Control c) { Content = c; MaxHeight = 640; HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled; }
    }
}
