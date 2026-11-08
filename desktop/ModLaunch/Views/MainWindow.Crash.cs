using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// 24.0: «Почему игра упала». Игра закрылась сама — ModLaunch читает свежие логи (CrashDoctor),
/// объясняет причину простыми словами и одной кнопкой «Исправить» делает то, что поможет:
/// ставит недостающие моды, обновляет устаревшие, выключает сломанные, обновляет загрузчик,
/// у Minecraft — Java и память. Перед исправлением — снимок «как было».
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Игра закрылась сама: разобрать логи и, если это вылет, показать окно.</summary>
    async Task CheckCrash(GameState g, Features.Launcher.ExitInfo exit)
    {
        if (g.Path is null || exit.ByUser) return;
        // Загрузчик дописывает лог уже после выхода игры.
        await Task.Delay(800);
        // Обновлений ещё не искали (выключено при запуске программы) — быстро ищем: после патча игры
        // чаще всего помогает именно обновление модов.
        if (!ModUpdates.Found.ContainsKey(g.Def.Id) && !Program.Demo)
            try { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await ModUpdates.Check(g, cts.Token); } catch { }
        // Minecraft: разбираем ту сборку, что запускали (с главной её запускают, не выбирая).
        var mc = g.Def.IsMinecraft ? Minecraft.McLaunch.Launched : null;
        CrashReport report;
        try { report = await Task.Run(() => CrashDoctor.Analyze(g, exit, mc)); }
        catch (Exception e) { Guard.Log(e); return; }
        if (!report.Crashed)
        {
            // 25.0: поиграли без вылета 5+ минут — запоминаем эту сборку как рабочую. Но не запуск без модов,
            // не «чистую игру» и не шаг поиска виновника: там играли вовсе не с этим набором модов.
            if (exit.Ms > 300_000 && !exit.Vanilla && !Vanilla.IsPurged(g.Def.Id) && !Bisect.Running(g.Def.Id))
                Snapshots.RememberWorking(g);
            return;
        }
        // Сначала пусть человек ответит на окно, открытое при выходе игры («оставить прогресс?»), — не закрываем его.
        for (var i = 0; i < 1200 && _overlay.IsVisible; i++) await Task.Delay(500);
        // Игру уже запустили снова — не мешаем.
        if (Features.Launcher.IsRunning(g.Def.Id) || _overlay.IsVisible) return;
        ShowCrash(g, report);
    }

    /// <summary>25.0: вопрос «Игра упала?» во время поиска виновника — после окна «оставить прогресс?», не поверх него.</summary>
    async Task AskBisectLater(GameState g)
    {
        await Task.Delay(400);
        for (var i = 0; i < 1200 && _overlay.IsVisible; i++) await Task.Delay(500);
        if (Features.Launcher.IsRunning(g.Def.Id) || !Bisect.Running(g.Def.Id)) return;
        BisectUi.Ask(g);
    }

    /// <summary>«Почему упала?» со вкладки «Проверка», лога или страницы Minecraft: разобрать последний запуск.</summary>
    public async void ExplainCrash(GameState g)
    {
        if (g.Path is null) return;
        var exit = Features.Launcher.LastExit(g.Def.Id);
        CrashReport report;
        var mc = g.Def.IsMinecraft ? Minecraft.Mc.Active : null;
        try { report = await Task.Run(() => CrashDoctor.Analyze(g, exit is { ByUser: false } ? exit : null, mc)); }
        catch (Exception e) { Toast(Jobs.Explain(e), bad: true); return; }
        ShowCrash(g, report);
    }

    public void ShowCrash(GameState g, CrashReport r)
    {
        var body = Ui.Col(12);
        var sub = r.Ms > 0
            ? I18n.T("v24.crash.after", ("game", g.Def.Name), ("time", PlayControls.Clock(TimeSpan.FromMilliseconds(r.Ms))))
            : g.Def.Name;
        if (r.Code is int code && code != 0) sub += " · " + I18n.T("v24.crash.code", ("code", CrashDoctor.Hex(code)));
        body.Children.Add(Ui.Text(sub, "muted", wrap: true));

        // Главное — одной фразой, крупно.
        var fixable = r.CanFix;
        var mark = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23),
            Background = fixable ? new SolidColorBrush(Color.FromArgb(40, 242, 184, 75)) : Ui.Res("Surface3"),
            Child = Ui.Icon(fixable ? Icons.Wand : Icons.Info, 22, fixable ? Ui.Res("Warn") : Ui.Res("Muted")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var head = Ui.Col(3, Ui.Text(r.Headline, "h3", wrap: true),
            Ui.Text(I18n.T(fixable ? "v24.crash.canFix" : "v24.crash.cantFix"), "small muted", wrap: true));
        head.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        top.Children.Add(mark);
        Grid.SetColumn(head, 1);
        top.Children.Add(head);
        body.Children.Add(top);

        foreach (var cause in r.Causes) body.Children.Add(CauseRow(cause));

        // Лог — для тех, кто хочет разобраться сам или спросить автора мода.
        var tools = Ui.Row(6);
        if (g.Def.IsMinecraft) tools.Children.Add(Ui.Button(I18n.T("crash.log"), () => { CloseDialog(); Navigate(() => new MinecraftPage("log")); }, "ghost", Icons.List));
        else tools.Children.Add(Ui.Button(I18n.T("crash.log"), () => { CloseDialog(); Navigate(() => new GamePage(g.Def.Id, "log")); }, "ghost", Icons.List));
        if (r.LogPath is { } log && File.Exists(log))
        {
            Button? share = null;
            share = Ui.Button(I18n.T("v24.crash.shareLog"), () => ShareCrashLog(log, share), "ghost", Icons.Link);
            tools.Children.Add(share);
        }
        tools.Children.Add(Ui.Button(I18n.T("v25.bug.short"), () => { CloseDialog(); BugReportUi.Show(g, r.Headline); }, "ghost", Icons.Flag));
        body.Children.Add(tools);
        if (fixable && !g.Def.IsMinecraft)
        {
            var safe = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            var shield = Ui.Icon(Icons.Shield, 14, Ui.Res("Good"));
            shield.VerticalAlignment = VerticalAlignment.Top;
            shield.Margin = new Thickness(0, 2, 0, 0);
            safe.Children.Add(shield);
            var note = Ui.Text(I18n.T("v24.crash.safe"), "small muted", wrap: true);
            Grid.SetColumn(note, 1);
            safe.Children.Add(note);
            body.Children.Add(safe);
        }

        var actions = new List<Control> { Ui.Button(I18n.T("common.close"), CloseDialog) };
        // 25.0: игра работала раньше с другим набором модов — одна кнопка вернёт ту сборку.
        if (!g.Def.IsMinecraft && !Bisect.Running(g.Def.Id) && Snapshots.DiffersFromWorking(g) && Snapshots.LastWorking(g.Def.Id) is { } works)
        {
            var back = Ui.Button(I18n.T("v25.works.back"), () => { CloseDialog(); Actions.RestoreSnapshot(g, works); }, "", Icons.Undo);
            ToolTip.SetTip(back, I18n.T("v25.works.tip", ("when", Ui.Ago(works.At))));
            actions.Add(back);
        }
        var urlOnly = !fixable && r.Causes.FirstOrDefault(c => c.Fix is { Kind: "url" })?.Fix?.Url is { } url ? url : null;
        if (fixable)
            actions.Add(Ui.Button(I18n.T("v24.crash.fix"), () => { CloseDialog(); Actions.FixCrash(g, r); }, "primary", Icons.Wand));
        else if (urlOnly is not null)
            actions.Add(Ui.Button(I18n.T("v24.crash.openPage"), () => Ui.OpenUrl(urlOnly), "primary", Icons.External));
        else
        {
            if (!g.Def.IsMinecraft && g.Path is not null && Vanilla.Supported(g.Def, g.Path) && !Vanilla.IsPurged(g.Def.Id))
                actions.Add(Ui.Button(I18n.T("vanilla.play"), () => { CloseDialog(); Actions.Play(g, vanilla: true); }, "", Icons.Play));
            // 25.0: причину не понять — «Найти виновника»: моды по половинам, пара запусков (если поиск уже не идёт).
            if (!g.Def.IsMinecraft && !Bisect.Running(g.Def.Id) && g.Registry is { } reg && Bisect.Candidates(reg).Suspects.Count >= 2)
                actions.Add(Ui.Button(I18n.T("v25.bis.title"), () => { CloseDialog(); BisectUi.Offer(g); }, "primary", Icons.Search));
        }
        Dialog(I18n.T("v24.crash.title"), body, 640, actions.ToArray());
    }

    static Control CauseRow(CrashCause c)
    {
        var serious = c.Kind is "missing" or "noLoader" or "java" or "loaderOld" or "depOff";
        var color = c.Fix is null ? Ui.Res("Muted") : serious ? Ui.Res("Bad") : Ui.Res("Warn");
        var badge = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = Ui.Res("Surface3"),
            Child = Ui.Icon(c.Fix is null ? Icons.Info : Icons.Alert, 16, color), VerticalAlignment = VerticalAlignment.Top,
        };
        var words = Ui.Col(4, Ui.Text(c.Title, "h3", wrap: true));
        if (c.Text != "") words.Children.Add(Ui.Text(c.Text, "small muted", wrap: true));
        if (c.Plan is not null)
        {
            var plan = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(0, 2, 0, 0) };
            var icon = Ui.Icon(Icons.Wand, 13, Ui.Res("Brand2"));
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            plan.Children.Add(icon);
            var text = Ui.Text(c.Plan, "small", color: Ui.Res("Brand2"), wrap: true);
            Grid.SetColumn(text, 1);
            plan.Children.Add(text);
            words.Children.Add(plan);
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(badge);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = grid };
    }

    /// <summary>Лог на mclo.gs (сервис сам прячет IP и токены), ссылка — в буфер обмена.</summary>
    async void ShareCrashLog(string file, Button? button)
    {
        if (button is not null) button.IsEnabled = false;
        try
        {
            var link = Program.Demo ? "https://mclo.gs/demo123" : await LogShare.Upload(file);
            await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(link) ?? Task.CompletedTask);
            Toast(I18n.T("share.done", ("url", link)));
        }
        catch (Exception e) { Toast(I18n.T("share.failed", ("reason", Jobs.Explain(e))), bad: true); }
        finally { if (button is not null) button.IsEnabled = true; }
    }

    /// <summary>Снимок для демо: Lethal Company после обновления игры — нет LethalLib, LethalThings устарел, MoreCompany дважды.</summary>
    public static CrashReport DemoCrash(GameState g) => new(g.Def.Id, true, I18n.T("v24.cr.many.few", ("n", 3)),
    [
        new CrashCause("missing", I18n.T("v24.cr.missing.one", ("need", "LethalLib")), I18n.T("v24.cr.missing.line", ("need", "LethalLib"), ("mod", "More Suits")),
            new CrashFix("deps", [], ["evaisa.lethallib"]), I18n.T("v24.cr.missing.plan")),
        new CrashCause("outdated", I18n.T("v24.cr.outdated", ("mod", "LethalThings")), I18n.T("v24.cr.outdated.text"),
            new CrashFix("update", ["Evaisa-LethalThings"]), I18n.T("v24.cr.update.plan", ("mod", "LethalThings"), ("version", "0.10.8"))),
        new CrashCause("duplicate", I18n.T("v24.cr.dup", ("mod", "MoreCompany")), I18n.T("v24.cr.dup.text"),
            new CrashFix("disable", ["notnotnotswipez-MoreCompany#old"]), I18n.T("v24.cr.dup.plan")),
    ], null, unchecked((int)0xC0000005), 47_000);
}
