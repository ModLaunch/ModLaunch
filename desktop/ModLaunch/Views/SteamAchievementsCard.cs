using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 30.0: «Достижения Steam» на странице игры во «Всех играх»: сколько получено, полоса, последние три и полный список
/// по кнопке (полученные — первыми, скрытые неполученные — без спойлеров). Данные — с диска (Games/SteamAchievements.cs).
/// </summary>
public static class SteamAchievementsCard
{
    /// <summary>null — у игры нет достижений Steam на этом ПК (карточку не показываем).</summary>
    public static Control? For(LibGame g)
    {
        if (g.SteamAppId <= 0 || SteamAchievements.For(g.SteamAppId) is not { Count: > 0 } list) return null;
        var got = list.Count(a => a.Unlocked);
        var bar = new ProgressBar { Minimum = 0, Maximum = list.Count, Value = got, Height = 8, Margin = new Thickness(0, 2, 0, 0) };
        var head = new DockPanel();
        var all = Ui.Button(I18n.T("v30.ach.all"), () => ShowAll(g, list), "ghost", Icons.List);
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(Ui.Row(10, Ui.Icon(Icons.Trophy, 18, Ui.Hex("#F2B84B")),
            Ui.Text(I18n.T("v30.ach.title"), "h3"), Ui.Text(I18n.T("v30.ach.count", ("got", got), ("n", list.Count)), "small muted")));
        var col = Ui.Col(10, head, bar);
        var recent = list.Where(a => a.Unlocked).OrderByDescending(a => a.At ?? DateTime.MinValue).Take(3).ToList();
        if (recent.Count > 0)
        {
            var row = new WrapPanel();
            foreach (var a in recent) { var c = Chip(a); c.Margin = new Thickness(0, 0, 8, 8); row.Children.Add(c); }
            col.Children.Add(row);
        }
        else col.Children.Add(Ui.Text(I18n.T("v30.ach.none"), "small muted"));
        return Ui.Card(col, 18);
    }

    static Control Chip(SteamAchievement a) => new Border
    {
        Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6),
        Child = Ui.Row(8, Icon(a, 28), Ui.Col(0, new TextBlock { Text = a.Name, FontWeight = FontWeight.SemiBold, FontSize = 12.5, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(a.At is DateTime at ? Ui.Ago(at) : "", "tiny muted"))),
    };

    static Control Icon(SteamAchievement a, double size)
    {
        var holder = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Ui.Res("Surface3"), VerticalAlignment = VerticalAlignment.Center };
        holder.Child = a.Icon is { } url ? HomePage.WebArt(url, null, a.Name, 64) : Ui.Icon(Icons.Trophy, size * 0.55, a.Unlocked ? Ui.Hex("#F2B84B") : Ui.Res("Faint"));
        if (!a.Unlocked) holder.Opacity = 0.55;
        return holder;
    }

    static void ShowAll(LibGame g, List<SteamAchievement> list)
    {
        if (MainWindow.Current is not { } w) return;
        var col = Ui.Col(6);
        foreach (var a in list.OrderByDescending(a => a.Unlocked).ThenByDescending(a => a.At ?? DateTime.MinValue).ThenBy(a => a.Name))
        {
            var secret = a.Hidden && !a.Unlocked;
            var words = Ui.Col(1,
                new TextBlock { Text = secret ? I18n.T("v30.ach.hidden") : a.Name, FontWeight = FontWeight.SemiBold, Foreground = a.Unlocked ? Ui.Res("Text") : Ui.Res("Muted") },
                Ui.Text(secret ? I18n.T("v30.ach.hidden.text") : a.Description, "small muted", wrap: true));
            words.VerticalAlignment = VerticalAlignment.Center;
            var right = Ui.Text(a.Unlocked ? (a.At is DateTime at ? at.ToLocalTime().ToString("d MMM yyyy", I18n.Culture) : I18n.T("v30.ach.got")) : "", "tiny muted");
            right.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var icon = Icon(a, 40);
            icon.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(words);
            col.Children.Add(new Border { Padding = new Thickness(6, 6), Child = row });
        }
        var got = list.Count(a => a.Unlocked);
        w.Dialog(I18n.T("v30.ach.dialog", ("game", g.Name), ("got", got), ("n", list.Count)), new ScrollViewer { Content = col, MaxHeight = 520 }, 620,
            Ui.Button(I18n.T("common.close"), w.CloseDialog));
    }
}
