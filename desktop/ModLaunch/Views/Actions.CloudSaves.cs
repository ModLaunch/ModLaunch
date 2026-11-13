using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>28.0: облачные сохранения перед запуском — сверка, «взяли из облака», спор «какую копию оставить».</summary>
public static partial class Actions
{
    static Task CloudThenPlay(GameState g, bool animate, bool vanilla) =>
        CloudSync(g, () => Play(g, animate, vanilla, skipCheck: true, skipUpdate: true, skipCloud: true));

    /// <summary>Сверить сохранения с облаком и сделать нужное (взять, отправить, спросить); потом — then.</summary>
    public static async Task CloudSync(GameState g, Action then, bool tell = false)
    {
        void Go() => then();
        SaveCheck check;
        try { check = await Task.Run(() => CloudSaves.Check(g.Def, g.Path!)); }
        catch (Exception e)
        {
            W.Toast(I18n.T("v28.cs.checkFailed", ("error", Jobs.Explain(e))), bad: true);
            Go();
            return;
        }
        switch (check.State)
        {
            case "pull":
                try
                {
                    await Task.Run(() => CloudSaves.Pull(g.Def, g.Path!));
                    W.Toast(I18n.T("v28.cs.pulled", ("device", check.Cloud?.Device ?? "?")));
                }
                catch (Exception e) { W.Toast(I18n.T("v28.cs.pullFailed", ("error", Jobs.Explain(e))), bad: true); }
                break;
            case "push":
                // Здесь новее (играли без ModLaunch или отправка после игры сорвалась) — сначала в облако, потом играем.
                try { await Task.Run(() => CloudSaves.Push(g.Def, g.Path!)); if (tell) W.Toast(I18n.T("v28.cs.pushed", ("game", g.Def.Name))); }
                catch (Exception e) { W.Toast(I18n.T("v28.cs.pushFailed", ("error", Jobs.Explain(e))), bad: true); }
                break;
            case "conflict":
                CloudConflict(g, check, () => Go());
                return;
            case "noCloud":
                W.Toast(I18n.T("v28.cs.noPlaceToast"), bad: true);
                break;
            case "same" when tell:
                W.Toast(I18n.T("v28.cs.same"));
                break;
            case "empty" when tell:
                W.Toast(I18n.T("v28.cs.empty"));
                break;
            case "noSaves" when tell:
                W.Toast(I18n.T("bak.unsupported"), bad: true);
                break;
        }
        Go();
    }

    /// <summary>Сохранения поменялись и здесь, и на другом ПК: какую копию оставить (вторая — в резервные копии).</summary>
    internal static void CloudConflict(GameState g, SaveCheck check, Action then)
    {
        Control Side(string title, SaveSide? s, bool newer)
        {
            var col = Ui.Col(4, Ui.Row(8, Ui.Icon(title == "local" ? Icons.Tv : Icons.Cloud, 16, Ui.Res("Brand2")),
                Ui.Text(title == "local" ? I18n.T("v28.cs.side.local", ("device", s?.Device ?? Environment.MachineName)) : I18n.T("v28.cs.side.cloud", ("device", s?.Device ?? "?")), "h3")));
            if (s is not null)
            {
                col.Children.Add(Ui.Text(I18n.T("v28.cs.side.changed", ("when", s.At.ToLocalTime().ToString("d MMMM, HH:mm", I18n.Culture))), "small"));
                col.Children.Add(Ui.Text(I18n.T("v28.cs.side.size", ("files", s.Files), ("size", GamePage.Size(s.Bytes))), "small muted"));
            }
            if (newer)
            {
                var tag = ModRow.Tag(I18n.T("v28.cs.newer"), Ui.Soft("#16351F", "#3FB950"), Ui.Ink("#3FB950"));
                tag.HorizontalAlignment = HorizontalAlignment.Left;
                col.Children.Add(tag);
            }
            return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = col };
        }
        var localNewer = (check.Local?.At ?? DateTime.MinValue) >= (check.Cloud?.At ?? DateTime.MinValue);
        var sides = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        var left = Side("local", check.Local, localNewer);
        var right = Side("cloud", check.Cloud, !localNewer);
        Grid.SetColumn(right, 1);
        sides.Children.Add(left);
        sides.Children.Add(right);

        async Task Choose(bool keepLocal)
        {
            W.CloseDialog();
            try
            {
                await Task.Run(() => CloudSaves.Resolve(g.Def, g.Path!, keepLocal));
                W.Toast(I18n.T(keepLocal ? "v28.cs.keptLocal" : "v28.cs.keptCloud"));
            }
            catch (Exception e) { W.Toast(I18n.T("v28.cs.resolveFailed", ("error", Jobs.Explain(e))), bad: true); return; }
            then();
        }
        var keepCloud = Ui.Button(I18n.T("v28.cs.keepCloud"), () => _ = Choose(false), localNewer ? "" : "primary", Icons.Cloud);
        var keepLocal = Ui.Button(I18n.T("v28.cs.keepLocal"), () => _ = Choose(true), localNewer ? "primary" : "", Icons.Tv);
        W.Dialog(I18n.T("v28.cs.conflict.title", ("game", g.Def.Name)),
            Ui.Col(12, Ui.Text(I18n.T("v28.cs.conflict.text"), "muted", wrap: true), sides),
            600,
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog), keepCloud, keepLocal);
    }
}
