using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Minecraft;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>9.9: общие кусочки главной (лента убрана совсем — остались только плитки и кнопка «Установить»).</summary>
public sealed partial class HomePage
{
    /// <summary>Свои игры: найденные и не скрытые, сначала те, во что играли недавно.</summary>
    static List<GameState> MyGames() => MainWindow.OrderedGames()
        .Where(g => g.Status == Detect.Found && !Features.GameCollections.IsHidden(g.Def.Id))
        .OrderByDescending(g => Features.PlayTime.Get(g.Def.Id).LastPlayed ?? DateTime.MinValue).ToList();

    static Border Tile(Control content, Action open, string? extra = null)
    {
        var t = new Border { Classes = { "store-tile", "feed-tile" }, Child = content };
        if (extra is not null) t.Classes.Add(extra);
        StoreKit.OnClick(t, open);
        return t;
    }

    static Control InstallButton(GameState g, ModInfo m, bool big)
    {
        var installed = Actions.IsInstalled(g, m.Id);
        var busy = Actions.IsBusy(g, m.Id);
        Button b;
        if (installed) b = Ui.Button(big ? I18n.T("mod.installed") : "", () => { }, big ? "hero-ghost" : "icon", Icons.Check, I18n.T("mod.installed"));
        else
        {
            var gs = g; var mm = m;
            b = Ui.Button(big ? I18n.T("mod.install") : "", () => _ = Actions.Install(gs, mm), big ? "primary" : "icon primary", Icons.Download, I18n.T("mod.install"));
        }
        b.IsEnabled = !installed && !busy && g.Status == Detect.Found;
        if (big) b.Padding = new Thickness(20, 11);
        else { b.Width = b.Height = 36; b.CornerRadius = new CornerRadius(18); }
        return b;
    }

    /// <summary>
    /// 9.9, как «Jump back in» в Modrinth App: во что вы играли недавно — игры и сборки Minecraft —
    /// одним рядом, у каждой своя кнопка «Играть». Первая игра уже стоит в «Продолжить», её тут нет.
    /// </summary>
    static Control? JumpBack(List<GameState> mine)
    {
        var items = new List<(DateTime When, Control Tile)>();
        foreach (var g in mine.Skip(1))
            if (Features.PlayTime.Get(g.Def.Id) is { LastPlayed: { } when } t && !g.Def.IsMinecraft)
                items.Add((when, JumpTile(Ui.GameImage(g.Def, 480), g.Def.Name, Ui.Ago(when) + (t.TotalMs >= 3_600_000 ? " · " + Features.PlayTime.Format(t.TotalMs) : ""),
                    Features.Launcher.IsRunning(g.Def.Id), () => Actions.Play(g), () => MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id)))));
        if (Mc.State is { Status: Detect.Found })
            foreach (var i in Mc.Instances().Where(i => i.LastPlayed is not null))
            {
                var inst = i;
                var icon = McIcons.Effective(i);
                Control art = icon.StartsWith("builtin:", StringComparison.Ordinal) && Images.Asset(McIcons.AssetName(icon[8..]), 256) is { } bmp
                    ? new Panel { Children = { Gx.Gradient(Mc.Def?.Accent), new Image { Source = bmp, Width = 64, Height = 64 } } }
                    : Ui.GameImage(Mc.Def!, 480);
                items.Add((i.LastPlayed!.Value.ToLocalTime(), JumpTile(art, i.Name, $"{i.LoaderTitle} {i.GameVersion} · " + Ui.Ago(i.LastPlayed),
                    Features.Launcher.IsRunning(Mc.Id), () => MinecraftPage.Play(inst), () => MainWindow.Current?.Navigate(() => new MinecraftPage("mods")))));
            }
        if (items.Count == 0) return null;
        var tiles = items.OrderByDescending(x => x.When).Take(6).Select(x => x.Tile).ToList();
        var row = new UniformGrid { Columns = 6 };
        for (var i = 0; i < tiles.Count; i++) { tiles[i].Margin = new Thickness(i == 0 ? 0 : MainGap / 2, 0, MainGap / 2, 0); row.Children.Add(tiles[i]); }
        var head = CardHead(I18n.T("v99.jump"), I18n.T("v99.jump.sub"), LinkButton(I18n.T("home.all"), () => MainWindow.Current?.Navigate(() => new LibraryPage())));
        return new Border { Classes = { "card", "home-card" }, Padding = new Thickness(16, 14, 16, 16), Child = Ui.Col(0, head, row) };
    }

    static Control JumpTile(Control art, string name, string sub, bool running, Action play, Action open)
    {
        var top = new Panel { Height = 96, ClipToBounds = true, Children = { art, new Border { Background = Gx.ShadeUp(0.3, 200) } } };
        Control action = running
            ? StoreKit.Pill(I18n.T("time.running"), Ui.Res("Good"))
            : Ui.Button("", play, "icon primary", Icons.Play, I18n.T("games.play"));
        if (action is Button b) { b.Width = b.Height = 34; b.CornerRadius = new CornerRadius(17); }
        action.HorizontalAlignment = HorizontalAlignment.Right;
        action.VerticalAlignment = VerticalAlignment.Bottom;
        action.Margin = new Thickness(8);
        top.Children.Add(action);
        var words = Ui.Col(2,
            new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = sub, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        words.Margin = new Thickness(10, 8, 10, 10);
        return Tile(Ui.Col(0, top, words), open);
    }
}
