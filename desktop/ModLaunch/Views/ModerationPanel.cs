using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 31.0: «Модерация» в аккаунте админа. Очередь жалоб (новые сверху) с решениями в один клик: снять мод или
/// лот, скрыть отзыв, заблокировать человека, «нарушения нет». Ниже — снятие по жалобе правообладателя
/// (DMCA), снятое (можно вернуть), заблокированные и чёрный список файлов. Все решения проверяют правила базы.
/// </summary>
public sealed class ModerationPanel : UserControl
{
    static List<ModReport>? _reports;
    static List<Takedown> _down = [];
    static List<Ban> _bans = [];
    static string? _error;
    static bool _loading;
    bool _busy;

    public ModerationPanel() => Render();

    public static void Reset() { _reports = null; _error = null; }

    void Load()
    {
        if (_loading) return;
        _loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _reports = await Safety.OpenReports();
                _down = await Safety.Takedowns();
                _bans = await Safety.Bans();
                _error = null;
            }
            catch (Exception e) { _error = Safety.Explain(e); _reports = null; }
            _loading = false;
            Dispatcher.UIThread.Post(Render);
        });
    }

    void Act(Func<Task> work, string done)
    {
        if (_busy) return;
        _busy = true;
        Render();
        _ = Task.Run(async () =>
        {
            string? fail = null;
            try { await work(); }
            catch (Exception e) { fail = Safety.Explain(e); }
            Dispatcher.UIThread.Post(() =>
            {
                _busy = false;
                MainWindow.Current?.Toast(fail ?? done, bad: fail is not null);
                Reset();
                Render();
            });
        });
    }

    static Border Section(string title, string? hint, Control body) =>
        Ui.Card(Ui.Col(10, Ui.Text(title, "h3"), hint is null ? new Panel() : Ui.Text(hint, "small muted", wrap: true), body), 20);

    static string TakedownReason(string reportReason) => reportReason switch
    {
        "malware" => "malware",
        "copyright" or "stolen" => "copyright",
        "scam" => "scam",
        _ => "rules",
    };

    void Render()
    {
        var col = Ui.Col(16);
        if (!Account.Get().Admin)
        {
            col.Children.Add(Ui.Card(Ui.Text(I18n.T("v31.mod.adminOnly"), "muted", wrap: true), 22));
            Content = col;
            return;
        }
        if (_reports is null && _error is null)
        {
            Load();
            col.Children.Add(Ui.Card(Ui.Row(12, new ProgressBar { IsIndeterminate = true, Width = 120 }, Ui.Text(I18n.T("v31.mod.loading"), "muted")), 22));
            Content = col;
            return;
        }
        if (_error is not null)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("v31.mod.error"), "h3"), Ui.Text(_error, "small muted", wrap: true),
                Ui.Button(I18n.T("cr.refresh"), () => { Reset(); Render(); }, "", Icons.Refresh)), 22));
            Content = col;
            return;
        }

        var reports = _reports!;
        var stats = new Avalonia.Controls.Primitives.UniformGrid { Columns = 4 };
        stats.Children.Add(Stat(I18n.T("v31.mod.stat.open"), reports.Count, reports.Count > 0 ? Ui.Res("Warn") : null));
        stats.Children.Add(Stat(I18n.T("v31.mod.stat.down"), _down.Count, null));
        stats.Children.Add(Stat(I18n.T("v31.mod.stat.bans"), _bans.Count, null));
        stats.Children.Add(Stat(I18n.T("v31.mod.stat.hashes"), Safety.BlockedCount, null));
        col.Children.Add(Ui.Card(stats, 16));

        // ---- очередь жалоб
        var queue = Ui.Col(8);
        if (reports.Count == 0) queue.Children.Add(Ui.Text(I18n.T("v31.mod.queue.empty"), "muted"));
        foreach (var r in reports.Take(100))
        {
            var item = r;
            var note = new TextBox { Watermark = I18n.T("adm.note"), Width = 220, MaxLength = 500 };
            var buttons = Ui.Row(6, note);
            if (item.Kind is "creation" or "listing")
                buttons.Children.Add(Ui.Button(I18n.T("v31.mod.takedown"), () => Act(async () =>
                {
                    await Safety.TakeDown(item.Kind, item.Target, TakedownReason(item.Reason), note.Text ?? "", "", item.Title, item.Owner);
                    await Safety.Resolve(item, true, "takedown", note.Text ?? "");
                }, I18n.T("v31.mod.done.down")), "primary", Icons.EyeOff));
            if (item.Kind == "review")
                buttons.Children.Add(Ui.Button(I18n.T("rev.moderate"), () => Act(async () =>
                {
                    await Reviews.Moderate(item.Target);
                    await Safety.Resolve(item, true, "hide-review", note.Text ?? "");
                }, I18n.T("rev.moderated")), "primary", Icons.EyeOff));
            if (item.Owner is { Length: >= 6 } owner && owner != Account.Uid)
                buttons.Children.Add(Ui.Button(I18n.T("v31.mod.ban"), () => Confirm(I18n.T("v31.mod.ban.ask", ("name", item.Title)), () => Act(async () =>
                {
                    await Safety.BanUser(owner, item.Kind == "user" ? item.Title : "", note.Text is { Length: > 0 } n ? n : I18n.T("v31.rep.r." + item.Reason));
                    await Safety.Resolve(item, true, "ban", note.Text ?? "");
                }, I18n.T("v31.mod.done.ban"))), "ghost", Icons.Lock));
            buttons.Children.Add(Ui.Button(I18n.T("v31.mod.dismiss"), () => Act(() => Safety.Resolve(item, false, "none", note.Text ?? ""), I18n.T("v31.mod.done.dismiss")), "ghost", Icons.Check));
            buttons.IsEnabled = !_busy;

            var reason = HubLook.Badge(I18n.T("v31.rep.r." + item.Reason), item.Reason is "malware" or "copyright" or "stolen" or "scam" ? "accent" : "", Icons.Flag);
            var kind = HubLook.Badge(I18n.T("v31.rep.kind." + item.Kind));
            var head = Ui.Row(8, kind, reason, Ui.Text(item.Title is { Length: > 0 } t ? t : item.Target, "h3"));
            foreach (var c in head.Children) c.VerticalAlignment = VerticalAlignment.Center;
            var body = Ui.Col(6, head);
            if (item.Text != "") body.Children.Add(new SelectableTextBlock { Text = "«" + item.Text + "»", TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new SelectableTextBlock
            {
                Text = I18n.T("v31.mod.meta", ("target", item.Target), ("owner", item.Owner == "" ? "—" : item.Owner), ("who", item.Uid), ("when", Ui.Ago(item.Created))),
                TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), FontSize = 12,
            });
            body.Children.Add(buttons);
            queue.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = body });
        }
        col.Children.Add(Section(I18n.T("v31.mod.queue"), I18n.T("v31.mod.queue.text"), queue));

        // ---- снятие по жалобе правообладателя (письмо на abuse@)
        var dKind = new ComboBox { Width = 150, ItemsSource = new[] { I18n.T("v31.rep.kind.creation"), I18n.T("v31.rep.kind.listing") }, SelectedIndex = 0 };
        var dId = new TextBox { Watermark = I18n.T("v31.mod.dmca.id"), Width = 280 };
        var dReason = new ComboBox { Width = 190, ItemsSource = Safety.TakedownReasons.Select(r => I18n.T("v31.mod.tr." + r)).ToArray(), SelectedIndex = 0 };
        var dNote = new TextBox { Watermark = I18n.T("v31.mod.dmca.note"), Width = 360, MaxLength = 500 };
        var dSha = new TextBox { Watermark = "sha256", Width = 300, MaxLength = 64 };
        var dGo = Ui.Button(I18n.T("v31.mod.takedown"), () =>
        {
            var kind = dKind.SelectedIndex == 1 ? "listing" : "creation";
            var id = (dId.Text ?? "").Trim();
            var reason = Safety.TakedownReasons[Math.Max(0, dReason.SelectedIndex)];
            if (id == "") { MainWindow.Current?.Toast(I18n.T("v31.mod.dmca.noId"), bad: true); return; }
            if (string.IsNullOrWhiteSpace(dNote.Text)) { MainWindow.Current?.Toast(I18n.T("v31.mod.dmca.noNote"), bad: true); return; }
            Act(() => Safety.TakeDown(kind, id, reason, dNote.Text ?? "", dSha.Text ?? ""), I18n.T("v31.mod.done.down"));
        }, "primary", Icons.EyeOff);
        col.Children.Add(Section(I18n.T("v31.mod.dmca"), I18n.T("v31.mod.dmca.text"),
            Ui.Col(8, Ui.Row(8, dKind, dId, dReason), Ui.Row(8, dNote, dSha), dGo)));

        // ---- снятое
        var downs = Ui.Col(6);
        if (_down.Count == 0) downs.Children.Add(Ui.Text(I18n.T("adm.none"), "muted"));
        foreach (var t in _down.Take(200))
        {
            var item = t;
            var line = new DockPanel();
            var back = Ui.Button(I18n.T("v31.mod.restore"), () => Confirm(I18n.T("v31.mod.restore.ask", ("name", item.Title is { Length: > 0 } n ? n : item.Target)),
                () => Act(() => Safety.Restore(item.Kind, item.Target), I18n.T("v31.mod.done.restore"))), "ghost", Icons.Undo);
            DockPanel.SetDock(back, Dock.Right);
            line.Children.Add(back);
            line.Children.Add(Ui.Col(2, Ui.Text($"{I18n.T("v31.rep.kind." + item.Kind)} · {(item.Title is { Length: > 0 } tt ? tt : item.Target)}", "h3"),
                Ui.Text($"{I18n.T("v31.mod.tr." + item.Reason)} · {item.Note} · {Ui.Ago(item.At)}", "small muted", wrap: true)));
            downs.Children.Add(Ui.Card(line, 12));
        }
        col.Children.Add(Section(I18n.T("v31.mod.down"), I18n.T("v31.mod.down.text"), downs));

        // ---- заблокированные
        var bans = Ui.Col(6);
        if (_bans.Count == 0) bans.Children.Add(Ui.Text(I18n.T("adm.none"), "muted"));
        foreach (var b in _bans)
        {
            var item = b;
            var line = new DockPanel();
            var un = Ui.Button(I18n.T("v31.mod.unban"), () => Act(() => Safety.Unban(item.Uid), I18n.T("v31.mod.done.unban")), "ghost", Icons.Undo);
            DockPanel.SetDock(un, Dock.Right);
            line.Children.Add(un);
            line.Children.Add(Ui.Col(2, Ui.Text(item.Name is { Length: > 0 } n ? $"{n} · {item.Uid}" : item.Uid, "h3"), Ui.Text($"{item.Note} · {Ui.Ago(item.At)}", "small muted", wrap: true)));
            bans.Children.Add(Ui.Card(line, 12));
        }
        var banUid = new TextBox { Watermark = "UID", Width = 300 };
        var banNote = new TextBox { Watermark = I18n.T("v31.mod.ban.note"), Width = 320, MaxLength = 500 };
        bans.Children.Add(Ui.Row(8, banUid, banNote, Ui.Button(I18n.T("v31.mod.ban"), () =>
        {
            var uid = (banUid.Text ?? "").Trim();
            if (uid == "" || string.IsNullOrWhiteSpace(banNote.Text)) { MainWindow.Current?.Toast(I18n.T("v31.mod.ban.need"), bad: true); return; }
            Confirm(I18n.T("v31.mod.ban.ask", ("name", uid)), () => Act(() => Safety.BanUser(uid, "", banNote.Text ?? ""), I18n.T("v31.mod.done.ban")));
        }, "primary", Icons.Lock)));
        col.Children.Add(Section(I18n.T("v31.mod.bans"), I18n.T("v31.mod.bans.text"), bans));

        // ---- чёрный список файлов
        var hash = new TextBox { Watermark = "sha256", Width = 480, MaxLength = 64 };
        col.Children.Add(Section(I18n.T("v31.mod.hashes"), I18n.T("v31.mod.hashes.text"), Ui.Row(8, hash, Ui.Button(I18n.T("v31.mod.hashes.add"), () =>
        {
            var h = (hash.Text ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(h, "^[0-9a-fA-F]{64}$")) { MainWindow.Current?.Toast(I18n.T("v31.mod.hashes.bad"), bad: true); return; }
            Act(() => Safety.AddToBlocklist([h]), I18n.T("adm.done"));
        }, "primary", Icons.Plus))));

        col.Children.Add(Ui.Button(I18n.T("cr.refresh"), () => { Reset(); Render(); }, "ghost", Icons.Refresh));
        Content = col;
    }

    static Control Stat(string label, int value, IBrush? color) => Ui.Col(2,
        new TextBlock { Text = value.ToString(I18n.Culture), FontFamily = Look.Display, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = color ?? Ui.Res("Text") },
        Ui.Text(label, "small muted"));

    static void Confirm(string text, Action yes)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        w.Dialog(I18n.T("v31.mod.sure"), Ui.Text(text, "", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v31.mod.yes"), () => { w.CloseDialog(); yes(); }, "primary", Icons.Check));
    }
}
