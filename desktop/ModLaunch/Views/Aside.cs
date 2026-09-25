using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Правая панель, как в ModLaunch 3: коротко о том, что на экране. На главной —
/// аккаунт, друзья и обновления; у игры — счётчики и популярное; у мода — сведения.
/// </summary>
public static class Aside
{
    public static Control Section(string title, string icon, params Control[] body)
    {
        var col = Ui.Col(12, Ui.Row(8, Ui.Icon(icon, 15, Ui.Res("Brand2")), Ui.Text(title, "h3")));
        foreach (var b in body) col.Children.Add(b);
        return col;
    }

    static Button Stretch(Button b) { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Center; return b; }

    public static Control Divider() => new Border { Height = 1, Background = Ui.Res("Line"), Margin = new Thickness(-20, 4) };

    public static Control Stat(string value, string label) => new Border
    {
        Background = Ui.Res("Surface"), BorderBrush = Ui.Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 10),
        Child = Ui.Col(2,
            new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = label, FontSize = 11.5, Foreground = Ui.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }),
    };

    public static Control Stats(params (string Value, string Label)[] items)
    {
        var grid = new UniformGrid { Columns = items.Length, Margin = new Thickness(0) };
        foreach (var (v, l) in items)
        {
            var s = Stat(v, l);
            s.Margin = new Thickness(0, 0, 6, 0);
            grid.Children.Add(s);
        }
        return grid;
    }

    public static Control Pair(string key, string value, IBrush? color = null)
    {
        var dock = new DockPanel();
        var v = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = color ?? Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 };
        DockPanel.SetDock(v, Dock.Right);
        dock.Children.Add(v);
        dock.Children.Add(new TextBlock { Text = key, FontSize = 13, Foreground = Ui.Res("Muted") });
        return dock;
    }

    /// <summary>Строка мода: картинка, название, загрузки и кнопка установки.</summary>
    public static Control ModLine(GameState g, ModInfo mod)
    {
        var installed = Actions.IsInstalled(g, mod.Id);
        var install = installed
            ? Ui.Button("", () => MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id, "installed")), "icon", Icons.Check, I18n.T("mod.installed"))
            : Ui.Button("", () => _ = Actions.Install(g, mod), "icon", Icons.Download, I18n.T("mod.install"));
        install.IsEnabled = g.Status == Detect.Found;
        install.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(install, Dock.Right);
        var info = Ui.Col(1, new TextBlock { Text = mod.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(mod.Downloads > 0 ? "↓ " + I18n.Compact(mod.Downloads) : mod.Author, "small muted"));
        info.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel { Children = { install, Ui.Row(10, Ui.Thumb(mod.Icon, mod.Name, 38, 9), info) } };
        var b = new Button { Classes = { "ghost" }, Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = row };
        b.Click += (_, _) => MainWindow.Current?.Navigate(() => new ModPage(g.Def.Id, mod));
        return b;
    }

    // ---------------------------------------------------------------- популярное для игры (кэш на сеанс)

    static readonly ConcurrentDictionary<string, List<ModInfo>> PopularCache = new();
    static readonly ConcurrentDictionary<string, bool> Loading = new();

    /// <summary>Популярные моды игры (кэш на сеанс); null — ещё грузятся, по готовности перерисуется панель.</summary>
    public static List<ModInfo>? Popular(GameState g)
    {
        if (PopularCache.TryGetValue(g.Def.Id, out var hit)) return hit;
        if (Loading.TryAdd(g.Def.Id, true))
            _ = PopularAsync(g).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => MainWindow.Current?.RenderAside()));
        return null;
    }

    public static async Task<List<ModInfo>> PopularAsync(GameState g, int take = 12)
    {
        if (PopularCache.TryGetValue(g.Def.Id, out var hit)) return hit;
        if (!g.Def.HasCatalog) return PopularCache[g.Def.Id] = [];
        try
        {
            var page = Program.Demo ? Demo.Catalog(g.Def, new Query()) : await Catalog.Browse(g.Def, new Query());
            return PopularCache[g.Def.Id] = page.Mods.Take(take).ToList();
        }
        catch { return PopularCache[g.Def.Id] = []; }
    }

    // ---------------------------------------------------------------- готовые панели

    public static Control Home()
    {
        var col = Ui.Col(18);
        var profile = Social.Account.Get();
        if (profile.SignedIn)
        {
            var friends = Social.Friends.View().Friends;
            var online = friends.Where(f => f.State != "offline").ToList();
            var account = Section(I18n.T("aside.account"), Icons.User,
                Ui.Row(12, Ui.Thumb(null, profile.Name ?? "?", 44, 22), Ui.Col(2, Ui.Text(profile.Name ?? "", "h3"),
                    Ui.Text(I18n.T("aside.friendsOnline", ("n", online.Count), ("all", friends.Count)), "small muted"))));
            col.Children.Add(account);
            if (online.Count > 0)
            {
                var list = Ui.Col(8);
                foreach (var f in online.Take(6))
                    list.Children.Add(Ui.Row(10, Ui.Dot(f.State == "playing" ? Ui.Res("Good") : Ui.Res("Brand2")),
                        Ui.Col(1, Ui.Text(f.Name, "h3"), Ui.Text(f.State == "playing" && f.GameName != "" ? I18n.T("aside.playing", ("game", f.GameName)) : I18n.T("aside.online"), "small muted"))));
                col.Children.Add(Divider());
                col.Children.Add(Section(I18n.T("friends.title"), Icons.Users, list));
            }
        }
        else
        {
            var create = Ui.Button(I18n.T("aside.signup"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "primary", Icons.User);
            var login = Ui.Button(I18n.T("aside.login"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "", Icons.Lock);
            col.Children.Add(Section(I18n.T("aside.account"), Icons.User, Ui.Text(I18n.T("aside.account.text"), "small muted", wrap: true), Stretch(create), Stretch(login)));
        }

        var updates = Features.ModUpdates.Found.Sum(kv => kv.Value.Count) + Features.Tracking.Updates.Count;
        var mods = AppState.Games.Where(g => g.Status == Detect.Found).Sum(g => g.ModCount);
        var playMs = AppState.Games.Sum(g => Features.PlayTime.Get(g.Def.Id).TotalMs);
        col.Children.Add(Divider());
        col.Children.Add(Section(I18n.T("aside.summary"), Icons.Chart,
            Stats((AppState.Games.Count(g => g.Status == Detect.Found).ToString(), I18n.T("aside.games")), (mods.ToString(), I18n.T("aside.modsShort")), ((playMs / 3_600_000).ToString(), I18n.T("aside.hours")))));
        if (updates > 0)
            col.Children.Add(Ui.Button(I18n.T("aside.updates", ("n", updates)), () => MainWindow.Current?.Navigate(() => new ModsCenterPage("updates")), "primary", Icons.Refresh));

        var recent = Features.Recent.All().Take(4).ToList();
        if (recent.Count > 0)
        {
            var list = Ui.Col(4);
            foreach (var r in recent) list.Children.Add(ModLine(AppState.Game(r.Game), r.Mod));
            col.Children.Add(Divider());
            col.Children.Add(Section(I18n.T("mc.recent"), Icons.Eye, list));
        }
        return col;
    }

    public static Control Game(GameState g)
    {
        var col = Ui.Col(18);
        var list = g.Registry?.List() ?? [];
        var enabled = list.Count(m => m.Bool("enabled", true) && !m.Bool("missing"));
        var problems = list.Count(m => m.Bool("missing"));
        var updates = Features.ModUpdates.Found.TryGetValue(g.Def.Id, out var u) ? u.Count : 0;
        var head = Section(g.Def.Name, Icons.Grid,
            Stats((list.Count.ToString(), I18n.T("aside.installed")), (enabled.ToString(), I18n.T("aside.enabled")), ((updates > 0 ? updates : problems).ToString(), updates > 0 ? I18n.T("aside.updatesShort") : I18n.T("aside.problems"))));
        col.Children.Add(head);
        var played = Features.PlayTime.Get(g.Def.Id);
        if (played.TotalMs > 0 || played.LastPlayed is not null)
            col.Children.Add(Ui.Col(6,
                Pair(I18n.T("aside.playtime"), Features.PlayTime.Format(played.TotalMs)),
                Pair(I18n.T("aside.lastPlayed"), played.LastPlayed is null ? "—" : Ui.Ago(played.LastPlayed)),
                Pair(I18n.T("aside.loader"), g.Def.LoaderName, g.LoaderInstalled ? Ui.Res("Good") : Ui.Res("Warn"))));
        var rescan = Ui.Button(I18n.T("aside.rescan"), () => _ = AppState.DetectOne(g, deep: true), "", Icons.Refresh);
        rescan.HorizontalAlignment = HorizontalAlignment.Stretch;
        col.Children.Add(rescan);

        var popular = Popular(g);
        if (popular is null || popular.Count > 0)
        {
            var box = Ui.Col(4);
            if (popular is null) box.Children.Add(Ui.Text(I18n.T("common.loading"), "small muted"));
            else foreach (var m in popular.Take(6)) box.Children.Add(ModLine(g, m));
            var all = Ui.Button(I18n.T("home.all"), () => MainWindow.Current?.Navigate(() => new GamePage(g.Def.Id, "catalog")), "ghost");
            all.Padding = new Thickness(6, 2);
            var title = new DockPanel();
            DockPanel.SetDock(all, Dock.Right);
            title.Children.Add(all);
            title.Children.Add(Ui.Row(8, Ui.Icon(Icons.Flame, 15, Ui.Res("Brand2")), Ui.Text(I18n.T("aside.popular"), "h3")));
            col.Children.Add(Divider());
            col.Children.Add(Ui.Col(10, title, box));
        }
        return col;
    }

    public static Control Mod(GameState g, ModInfo mod, ModDetails? details, int versions)
    {
        var col = Ui.Col(18);
        var facts = Ui.Col(9,
            Pair(I18n.T("aside.needs"), g.Def.LoaderName),
            Pair(I18n.T("aside.source"), SourceName(mod.Source)));
        if (mod.Version != "") facts.Children.Add(Pair(I18n.T("aside.version"), mod.Version));
        if (versions > 0) facts.Children.Add(Pair(I18n.T("aside.versions"), versions.ToString()));
        if (mod.Downloads > 0) facts.Children.Add(Pair(I18n.T("aside.downloads"), mod.Downloads.ToString("N0")));
        if (mod.UpdatedAt is not null) facts.Children.Add(Pair(I18n.T("aside.updated"), mod.UpdatedAt.Value.ToLocalTime().ToString("dd.MM.yyyy")));
        if (details is { Requirements.Count: > 0 }) facts.Children.Add(Pair(I18n.T("mt.reqs"), details.Requirements.Count.ToString()));
        col.Children.Add(Section(I18n.T("aside.aboutMod"), Icons.Info, facts));
        if (mod.Categories.Length > 0)
        {
            var tags = new WrapPanel();
            foreach (var c in mod.Categories.Take(8))
            {
                var t = ModRow.Tag(c, Ui.Res("Surface3"), Ui.Res("Muted"));
                t.Margin = new Thickness(0, 0, 6, 6);
                tags.Children.Add(t);
            }
            col.Children.Add(tags);
        }
        if (mod.Url is not null)
        {
            var open = Ui.Button(I18n.T("aside.openOn", ("site", SourceName(mod.Source))), () => Ui.OpenUrl(mod.Url), "", Icons.External);
            open.HorizontalAlignment = HorizontalAlignment.Stretch;
            col.Children.Add(open);
        }
        if (mod.Author != "")
            col.Children.Add(Ui.Col(6, Divider(), Pair(I18n.T("aside.author"), mod.Author, Ui.Res("Brand2"))));
        return col;
    }

    public static string SourceName(string source) => source switch
    {
        "thunderstore" => "Thunderstore",
        "modlinks" => "ModLinks",
        "hub" => "ModLaunch Hub",
        _ => "Nexus Mods",
    };
}
