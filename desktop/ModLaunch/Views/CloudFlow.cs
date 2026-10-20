using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Loaders;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 10.0: профиль в облаке на деле. При запуске и при входе в аккаунт сверяем облако с этим ПК:
/// чего-то не хватает — спрашиваем «Ваш профиль с другого ПК — доустановить?», затем отправляем
/// профиль этого ПК. Ещё отправляем при каждом запуске игры (тогда сборка точно «настоящая»).
/// </summary>
public static class CloudFlow
{
    static int _busy;
    static string? _checkedFor;
    static MainWindow? W => MainWindow.Current;

    /// <summary>Вход в аккаунт (или смена аккаунта) — сверить ещё раз; обновление токена и прочие сигналы не в счёт.</summary>
    public static void OnAccount()
    {
        if (Account.Get() is not { SignedIn: true, Uid: { } uid } || uid == _checkedFor) return;
        _ = Task.Run(() => Check());
    }

    public static async Task Check(bool manual = false)
    {
        if (Program.Demo || !Account.SignedIn || (!manual && !CloudProfile.On)) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _checkedFor = Account.Get().Uid;
        try
        {
            var cloud = await CloudProfile.Fetch();
            if (cloud is null) { await CloudProfile.Push(null); if (manual) Dispatcher.UIThread.Post(() => W?.Toast(I18n.T("v10.cloud.pushed"))); return; }
            CloudProfile.TakeProfile(cloud);
            var diff = CloudProfile.Compare(cloud);
            if (manual ? !diff.Empty : CloudProfile.ShouldOffer(cloud, diff))
            {
                Dispatcher.UIThread.Post(() => Offer(cloud, diff));
                return;
            }
            await CloudProfile.Push(cloud);
            if (manual) Dispatcher.UIThread.Post(() => W?.Toast(I18n.T("v10.cloud.same")));
        }
        // Без сети или без правил в Firebase молча ждём следующего запуска; вручную — объясняем.
        catch (Exception e) { if (manual) Dispatcher.UIThread.Post(() => W?.Toast(Firebase.Explain("friends", e), bad: true)); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>Отправить профиль тихо (после запуска игры). Ошибки сети не мешают.</summary>
    public static void PushSoon()
    {
        if (Program.Demo || !Account.SignedIn || !CloudProfile.On) return;
        _ = Task.Run(async () =>
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try { await CloudProfile.Push(await CloudProfile.Fetch()); }
            catch { }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    static void Offer(JsonObject cloud, CloudDiff diff)
    {
        if (W is not { } w) return;
        var list = new StackPanel { Spacing = 8 };
        foreach (var (g, needLoader, missing) in diff.Here)
        {
            var parts = new List<string>();
            if (needLoader) parts.Add(g.Def.LoaderName);
            if (missing.Count > 0) parts.Add(I18n.T("v10.cloud.mods." + I18n.Plural(missing.Count, "one", "few", "many"), ("n", missing.Count)));
            var cover = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Ui.GameImage(g.Def, 92, art: Images.Art.Cover) };
            list.Children.Add(new Border
            {
                Classes = { "card" }, Padding = new Thickness(10),
                Child = Ui.Row(12, cover, Ui.Col(2, Ui.Text(g.Def.Name, "strong"), Ui.Text(string.Join(" · ", parts), "muted small"))),
            });
        }
        foreach (var (_, name, mods) in diff.NotFound)
            list.Children.Add(Ui.Text(I18n.T("v10.cloud.notFound", ("game", name), ("n", mods)), "muted small", wrap: true));
        CheckBox? style = null;
        if (diff.Style is not null)
        {
            style = new CheckBox { IsChecked = true, Content = Ui.Text(I18n.T("v10.cloud.style"), wrap: true) };
            list.Children.Add(style);
        }
        var when = diff.At == DateTime.MinValue ? "" : " · " + Ui.Ago(diff.At);
        var body = Ui.Col(14,
            Ui.Text(I18n.T("v10.cloud.text", ("pc", diff.Device == "" ? I18n.T("v10.cloud.otherPc") : diff.Device)) + when, "muted", wrap: true),
            new ScrollViewer { MaxHeight = 330, Content = list });
        w.Dialog(I18n.T("v10.cloud.title"), body, 540,
            Ui.Button(I18n.T("v10.cloud.later"), () => { w.CloseDialog(); CloudProfile.Dismiss(cloud); _ = Task.Run(() => CloudProfile.Push(cloud)); }),
            Ui.Button(diff.Here.Count > 0 ? I18n.T("v10.cloud.go") : I18n.T("v10.cloud.take"), () =>
            {
                w.CloseDialog();
                CloudProfile.Dismiss(cloud);
                Restore(diff, style?.IsChecked == true);
            }, "primary", Icons.Download));
    }

    /// <summary>Поставить загрузчики, затем моды — каждая игра своей задачей в очереди загрузок.</summary>
    public static void Restore(CloudDiff diff, bool takeStyle)
    {
        if (takeStyle && diff.Style is { } s) Configs.Apply(s);
        foreach (var (g, needLoader, missing) in diff.Here)
        {
            if (g.Path is null) continue;
            if (!needLoader) { if (missing.Count > 0) Actions.FixSetup(g, I18n.T("v10.cloud.job"), missing); continue; }
            var path = g.Path;
            Jobs.Run(g.Def.LoaderName, g.Def.Name, async (_, progress, ct) =>
            {
                await Loader.Install(g.Def, path, progress, ct);
                g.Refresh();
                if (missing.Count > 0) Dispatcher.UIThread.Post(() => Actions.FixSetup(g, I18n.T("v10.cloud.job"), missing));
            });
        }
        if (diff.Here.Count > 0) W?.Toast(I18n.T("v10.cloud.started"));
    }

    /// <summary>Для снимков: вопрос «доустановить?» с выдуманной сборкой с другого ПК.</summary>
    public static void DemoOffer()
    {
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft && g.Registry is not null).Take(2).ToList();
        var here = games.Select((g, i) => (g, i == 0, new List<SetupMod>
        {
            new("a", "ConfigurationManager", "18.0", "thunderstore", true),
            new("b", "BetterUI", "2.1", "thunderstore", true),
            new("c", "MoreCompany", "1.9", "thunderstore", i == 0),
        }.Take(i == 0 ? 3 : 2).ToList())).ToList();
        var style = Configs.BuiltIn().First(c => c.Id == "retro");
        Offer(new JsonObject(), new CloudDiff(here, [("valheim", "Valheim", 14)], "GAMING-PC", DateTime.UtcNow.AddHours(-3), style));
    }
}
