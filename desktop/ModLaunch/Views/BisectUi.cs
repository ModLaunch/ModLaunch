using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>25.0: окна «Найти виновника» — объяснение, вопрос после каждого запуска и итог.</summary>
public static class BisectUi
{
    static MainWindow W => MainWindow.Current!;

    static string ModName(GameState g, string id) => g.Registry?.Get(id)?.Str("name") ?? id;

    public static void Offer(GameState g)
    {
        if (g.Registry is null) return;
        // Поиск уже идёт или игра запущена — второй поиск поверх первого перепутал бы, какие моды как были.
        if (Bisect.CantStart(g) is { } why) { W.Toast(why, bad: true); return; }
        var (_, suspects) = Bisect.Candidates(g.Registry);
        W.Dialog(I18n.T("v25.bis.title"), Ui.Col(10,
                Ui.Text(I18n.T("v25.bis.text", ("n", suspects.Count), ("runs", Bisect.Runs(suspects.Count))), "muted", wrap: true),
                Ui.Text(I18n.T("v25.bis.safe"), "small muted", wrap: true)),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("v25.bis.start"), () =>
            {
                W.CloseDialog();
                try
                {
                    var s = Bisect.Start(g);
                    W.Toast(I18n.T("v25.bis.step", ("n", s.Round + 1), ("left", Bisect.RunsLeft(s))));
                    AppState.Notify();
                    Actions.Play(g);
                }
                catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
            }, "primary", Icons.Search));
    }

    /// <summary>«Остановить поиск»: моды — как были. Пока игра запущена, нельзя: её файлы заняты.</summary>
    public static void Stop(GameState g)
    {
        try { Bisect.Cancel(g); }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); return; }
        AppState.Notify();
        W.Toast(I18n.T("v25.bis.stopped"));
    }

    /// <summary>Игра закрылась во время поиска — спросить, упала ли она.</summary>
    public static void Ask(GameState g)
    {
        if (Bisect.Get(g.Def.Id) is not { } s) return;
        var on = s.Verify is { } v
            ? I18n.T("v25.bis.verify.on", ("mod", ModName(g, v)))
            : I18n.T("v25.bis.on", ("list", string.Join(", ", s.Testing.Take(8).Select(id => ModName(g, id))) + (s.Testing.Count > 8 ? $" +{s.Testing.Count - 8}" : "")));
        W.Dialog(I18n.T("v25.bis.ask"), Ui.Col(10,
                Ui.Text(I18n.T("v25.bis.ask.text", ("n", s.Round + 1), ("left", Bisect.RunsLeft(s))), "muted", wrap: true),
                Ui.Text(on, "small muted", wrap: true)),
            Ui.Button(I18n.T("v25.bis.stop"), () => { W.CloseDialog(); Stop(g); }, "ghost"),
            Ui.Button(I18n.T("v25.bis.works"), () => Reply(g, false), "", Icons.Check),
            Ui.Button(I18n.T("v25.bis.crashed"), () => Reply(g, true), "primary", Icons.Alert));
    }

    static void Reply(GameState g, bool crashed)
    {
        W.CloseDialog();
        var (done, culprit, next) = Bisect.Answer(g, crashed);
        AppState.Notify();
        if (!done && next is not null)
        {
            // Остался один подозреваемый — проверочный запуск без него, прежде чем называть виновника.
            var (title, text) = next.Verify is { } v
                ? (I18n.T("v25.bis.verify"), I18n.T("v25.bis.verify.text", ("mod", ModName(g, v))))
                : (I18n.T("v25.bis.next"), I18n.T("v25.bis.next.text", ("n", next.Round + 1), ("left", Bisect.RunsLeft(next)), ("count", next.Suspects.Count)));
            W.Dialog(title, Ui.Text(text, "muted", wrap: true),
                Ui.Button(I18n.T("v25.bis.stop"), () => { W.CloseDialog(); Stop(g); }, "ghost"),
                Ui.Button(I18n.T("v24.fixed.play"), () => { W.CloseDialog(); Actions.Play(g); }, "primary", Icons.Play));
            return;
        }
        if (culprit is not null)
        {
            Premium.Count("crashFixed");
            W.Dialog(I18n.T("v25.bis.found", ("mod", ModName(g, culprit))), Ui.Text(I18n.T("v25.bis.found.text", ("mod", ModName(g, culprit))), "muted", wrap: true),
                Ui.Button(I18n.T("v25.bug.short"), () => { W.CloseDialog(); BugReportUi.Show(g, I18n.T("v25.bis.found", ("mod", ModName(g, culprit)))); }, "ghost", Icons.Flag),
                Ui.Button(I18n.T("v24.fixed.play"), () => { W.CloseDialog(); Actions.Play(g); }, "primary", Icons.Play));
            return;
        }
        W.Dialog(I18n.T("v25.bis.none"), Ui.Text(I18n.T("v25.bis.none.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.close"), W.CloseDialog),
            Ui.Button(I18n.T("v25.bug.short"), () => { W.CloseDialog(); BugReportUi.Show(g); }, "primary", Icons.Flag));
    }
}
