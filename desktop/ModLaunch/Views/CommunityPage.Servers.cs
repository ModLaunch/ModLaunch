using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Minecraft;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: вкладка «Серверы» — доска серверов (свои, друзей, открытые), «Создать сервер»
/// и «Подключиться»: сборка по коду, адрес в «Сетевую игру» Minecraft или в буфер обмена, запуск игры.
/// </summary>
public sealed partial class CommunityPage
{
    Control Board()
    {
        var view = Friends.View();
        if (!view.Configured) return Ui.Card(Ui.Text(I18n.T("v11.cm.f.off"), "muted", wrap: true), 22);
        if (!view.SignedIn) return SignInCard();
        var me = Servers.Me;
        var friendIds = view.Friends.Select(f => f.Uid).ToHashSet();
        var all = Servers.Cached;
        var mine = all.Where(s => s.Owner == me).ToList();
        var friends = all.Where(s => s.Owner != me && friendIds.Contains(s.Owner)).ToList();
        var open = all.Where(s => s.Owner != me && !friendIds.Contains(s.Owner)).ToList();

        var create = Ui.Button(I18n.T("v11.cm.s.create"), () => ServerDialog(null), "primary", Icons.Plus);
        var refresh = Ui.Button(I18n.T("v11.cm.refresh"), () => { if (!Program.Demo) _ = LoadBoard(); }, "ghost", Icons.Refresh);
        var buttons = Ui.Row(8, refresh, create);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        var bar = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        var lead = Ui.Text(I18n.T("v11.cm.s.lead"), "muted", wrap: true);
        lead.VerticalAlignment = VerticalAlignment.Center;
        lead.Margin = new Thickness(0, 0, 16, 0);
        bar.Children.Add(lead);

        var col = Ui.Col(16, bar);
        if (_boardError is not null) col.Children.Add(Notice(_boardError, Icons.Alert, Ui.Res("Warn")));
        if (all.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10,
                Ui.Row(10, Ui.Icon(Icons.Server, 18, Ui.Res("Muted")), Ui.Text(I18n.T(_loading ? "common.loading" : "v11.cm.s.empty.title"), "h3")),
                Ui.Text(I18n.T("v11.cm.s.empty.text"), "muted", wrap: true)), 22));
            return col;
        }
        void Section(string key, List<GameServer> list, bool own)
        {
            if (list.Count == 0) return;
            var head = Ui.Row(8, Ui.Text(I18n.T(key), "h3"), Ui.Text(list.Count.ToString(), "h3", color: Ui.Res("Faint")));
            var items = Ui.Col(10, head);
            foreach (var s in list) items.Children.Add(ServerCard(s, own));
            col.Children.Add(items);
        }
        Section("v11.cm.s.mine", mine, true);
        Section("v11.cm.s.ofFriends", friends, false);
        Section("v11.cm.s.open", open, false);
        return col;
    }

    static string ModsText(int n) => I18n.T("v11.cm.s.mods." + I18n.Plural(n, "one", "few", "many"), ("n", n));

    Control ServerCard(GameServer s, bool mine)
    {
        var on = Servers.IsOnline(s);
        var def = GameCatalog.ById(s.Game);
        var title = Ui.Row(10, Ui.Text(s.Name, "h3"), StoreKit.Pill(I18n.T(on ? "v11.cm.s.on" : "v11.cm.s.off"), on ? Ui.Res("Good") : Ui.Res("Faint"), onArt: false));
        var facts = new List<string> { def?.Name ?? s.Game };
        if (s.Version != "") facts.Add(s.Version);
        if (!mine) facts.Add(I18n.T("v11.cm.s.by", ("name", s.OwnerName)));
        if (s.Updated is not null) facts.Add(I18n.T(on ? "v11.cm.s.beat" : "v11.cm.s.last", ("ago", Ago(s.Updated))));
        var pills = Ui.Row(6,
            StoreKit.Pill(I18n.T("v11.cm.s.players", ("n", s.Players), ("max", s.Max)), onArt: false),
            StoreKit.Pill(I18n.T(s.Public ? "v11.cm.s.public" : "v11.cm.s.friendsOnly"), onArt: false));
        if (s.Mods > 0) pills.Children.Add(StoreKit.Pill(ModsText(s.Mods), onArt: false));
        if (s.Build != "") pills.Children.Add(StoreKit.Pill(I18n.T("v11.cm.s.build"), onArt: false));
        if (mine && Servers.IsLive(s.Id)) pills.Children.Add(StoreKit.Pill(I18n.T("v11.cm.s.live"), Ui.Res("Brand2"), onArt: false));
        var words = Ui.Col(6, title, Ui.Text(string.Join(" · ", facts), "small muted"), pills);
        if (s.Address != "")
        {
            var address = s.Address;
            var copy = Ui.Button("", () => Copy(address, I18n.T("v11.cm.join.copied", ("address", address))), "icon ghost", Icons.Copy, I18n.T("v11.cm.s.copy"));
            copy.Width = copy.Height = 28;
            words.Children.Add(Ui.Row(6,
                new Border
                {
                    Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center,
                    Child = new SelectableTextBlock { Text = address, FontFamily = Mono, FontSize = 12.5 },
                }, copy));
        }

        var server = s;
        var join = Ui.Button(I18n.T("v11.cm.s.join"), () => Join(server), "primary", Icons.Play);
        var right = Ui.Col(8, join);
        if (mine)
        {
            var live = Servers.IsLive(s.Id);
            var tools = Ui.Row(4,
                Ui.Button("", () => ServerDialog(server), "icon ghost", Icons.Edit, I18n.T("v11.cm.s.edit")),
                Ui.Button("", () => ToggleLive(server), "icon ghost", live ? Icons.WifiOff : Icons.Wifi, I18n.T(live ? "v11.cm.s.pause" : "v11.cm.s.resume")),
                Ui.Button("", () => ConfirmRemoveServer(server), "icon ghost", Icons.Trash, I18n.T("v11.cm.s.remove")));
            tools.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(tools);
        }
        right.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        right.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(right);
        var pic = GamePic(s.Game, 64);
        pic.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(Ui.Row(16, pic, words));
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Child = row, Opacity = on || mine ? 1 : 0.82 };
    }

    static async void ToggleLive(GameServer s)
    {
        if (Servers.IsLive(s.Id)) { Servers.Release(s.Id); MainWindow.Current?.Toast(I18n.T("v11.cm.s.paused", ("name", s.Name))); return; }
        try
        {
            // Свой идущий сервер Minecraft считает игроков сам — отдаём доске его счётчик.
            var run = McHost.RunningFor(s.Id);
            await Servers.Heartbeat(s.Id, run?.Players ?? s.Players);
            Servers.KeepAlive(s.Id, run is null ? null : () => run.Players);
            MainWindow.Current?.Toast(I18n.T("v11.cm.s.resumed", ("name", s.Name)));
        }
        catch (ServiceError e) { MainWindow.Current?.Toast(Servers.Explain(e), bad: true); }
    }

    static void ConfirmRemoveServer(GameServer s)
    {
        var w = MainWindow.Current!;
        async void Go()
        {
            w.CloseDialog();
            try
            {
                await Servers.Remove(s.Id);
                if (McHost.List().FirstOrDefault(h => h.Board == s.Id) is { } hosted) McHost.SetBoard(hosted, "");
                w.Toast(I18n.T("v11.cm.s.removed", ("name", s.Name)));
            }
            catch (ServiceError e) { w.Toast(Servers.Explain(e), bad: true); }
        }
        w.Dialog(I18n.T("v11.cm.s.remove.title", ("name", s.Name)), Ui.Text(I18n.T("v11.cm.s.remove.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v11.cm.s.remove"), Go, "primary", Icons.Trash));
    }

    // ---------------------------------------------------------------- «Создать сервер»

    /// <summary>
    /// Создать сервер на доске или поправить свой. preset — готовые поля (свой сервер Minecraft
    /// выкладывает себя с адресом в сети), saved — что сделать с сохранённой записью.
    /// </summary>
    public static void ServerDialog(GameServer? edit, ServerDraft? preset = null, Action<GameServer>? saved = null)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var d = preset ?? (edit is null
            ? new ServerDraft(null, "", Settings.Data.Str("lastGame") ?? "", "")
            : new ServerDraft(edit.Id, edit.Name, edit.Game, edit.Address, edit.Build, edit.Mods, edit.Visibility, edit.Players, edit.Max, edit.Version));
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !GameCollections.IsHidden(g.Def.Id)).Select(g => g.Def).OrderBy(g => g.Name).ToList();
        if (games.Count == 0) games = [.. GameCatalog.All.OrderBy(g => g.Name)];
        if (GameCatalog.ById(d.Game) is { } current && !games.Contains(current)) games.Insert(0, current);
        var gameId = games.Any(g => g.Id == d.Game) ? d.Game : games.FirstOrDefault()?.Id ?? "";
        var mods = d.Mods;
        var visibility = d.Visibility == "public" ? "public" : "friends";

        var name = new TextBox { Text = d.Name, MaxLength = Servers.MaxName, Watermark = I18n.T("v11.cm.d.name.hint") };
        var game = new ComboBox { MinWidth = 300, MaxDropDownHeight = 380 };
        foreach (var g in games)
            game.Items.Add(new ComboBoxItem { Content = Ui.Row(8, GamePic(g.Id, 22), new TextBlock { Text = g.Name, VerticalAlignment = VerticalAlignment.Center }), Tag = g.Id });
        game.SelectedItem = game.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == gameId);
        var address = new TextBox { Text = d.Address, MaxLength = Servers.MaxAddress, FontFamily = Mono, Watermark = I18n.T("v11.cm.d.address.hint"), MinWidth = 300 };
        var here = Ui.Button(I18n.T("v11.cm.d.address.lan"), () =>
        {
            if (McHost.LanIp() is not { } ip) { w.Toast(I18n.T("v11.cm.d.address.noLan"), bad: true); return; }
            address.Text = gameId == Mc.Id ? $"{ip}:{McHost.DefaultPort}" : ip;
        }, "ghost", Icons.Wifi);
        var build = new TextBox { Text = d.Build, MaxLength = Servers.MaxBuild, FontFamily = Mono, Watermark = "018f2c3a-…", MinWidth = 300 };
        var makeCode = Ui.Button(I18n.T("v11.cm.d.build.make"), () => { }, "ghost", Icons.Upload, I18n.T("v11.cm.d.build.make.tip"));
        var version = new TextBox { Text = d.Version, MaxLength = Servers.MaxVersion, Width = 180, Watermark = "1.21.1" };
        var max = new NumericUpDown { Value = Math.Clamp(d.Max, 1, Servers.MaxPlayers), Minimum = 1, Maximum = Servers.MaxPlayers, Increment = 1, FormatString = "0", Width = 150 };
        var error = Ui.Text("", "small", color: Ui.Res("Bad"), wrap: true);

        var chips = Ui.Row(8);
        void RenderChips()
        {
            chips.Children.Clear();
            foreach (var (id, key) in new[] { ("friends", "v11.cm.d.vis.friends"), ("public", "v11.cm.d.vis.public") })
            {
                var value = id;
                var chip = Ui.Button(I18n.T(key), () => { visibility = value; RenderChips(); }, "chip", value == "public" ? Icons.Globe : Icons.Users);
                if (visibility == id) chip.Classes.Add("active");
                chips.Children.Add(chip);
            }
        }
        RenderChips();

        var buildField = Field(I18n.T("v11.cm.d.build"), Ui.Row(8, build, makeCode), I18n.T("v11.cm.d.build.hint"));
        void GameChanged()
        {
            var def = GameCatalog.ById(gameId);
            buildField.IsVisible = def is not null && ProfileCode.Supported(def);
            var g = AppState.Games.FirstOrDefault(x => x.Def.Id == gameId);
            makeCode.IsEnabled = g is { Status: Detect.Found, Registry: not null };
        }
        game.SelectionChanged += (_, _) =>
        {
            if (game.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            gameId = id;
            GameChanged();
        };
        GameChanged();

        async void MakeCode()
        {
            var g = AppState.Games.FirstOrDefault(x => x.Def.Id == gameId);
            if (g?.Registry is not { } registry) return;
            makeCode.IsEnabled = false;
            error.Foreground = Ui.Res("Muted");
            error.Text = I18n.T("v11.cm.d.build.making");
            try
            {
                var title = (name.Text ?? "").Trim() is { Length: > 0 } t ? t : g.Def.Name;
                var zip = await ProfileCode.Build(g, title, withConfig: true);
                build.Text = Program.Demo ? "018f2c3a-6b1e-4f1b-9c55-2d4e8a1f3b77" : await ProfileCode.Upload(zip);
                mods = ProfileCode.Pick(registry).In.Count;
                error.Text = I18n.T("v11.cm.d.build.made", ("mods", ModsText(mods)));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException)
            {
                error.Foreground = Ui.Res("Bad");
                error.Text = Jobs.Explain(e);
            }
            makeCode.IsEnabled = true;
        }
        makeCode.Click += (_, _) => MakeCode();

        var go = Ui.Button(I18n.T(edit is null ? "v11.cm.d.publish" : "common.save"), () => { }, "primary", edit is null ? Icons.Upload : Icons.Save);
        async void Save()
        {
            var limit = (int)(max.Value ?? 10);
            var draft = new ServerDraft(d.Id, name.Text ?? "", gameId, address.Text ?? "", build.Text ?? "", mods, visibility, Math.Min(d.Players, limit), limit, version.Text ?? "");
            error.Foreground = Ui.Res("Bad");
            if (Servers.Check(draft) is [var first, ..]) { error.Text = I18n.T(first, ("n", Servers.MaxName)); return; }
            go.IsEnabled = false;
            error.Text = "";
            try
            {
                var s = await Servers.Save(draft);
                w.CloseDialog();
                w.Toast(I18n.T(edit is null ? "v11.cm.d.published" : "v11.cm.d.saved", ("name", s.Name)));
                saved?.Invoke(s);
            }
            catch (Exception e) when (e is ServiceError or ArgumentException)
            {
                error.Text = Servers.Explain(e);
                go.IsEnabled = true;
            }
        }
        go.Click += (_, _) => Save();

        var numbers = Ui.Row(16, Field(I18n.T("v11.cm.d.version"), version), Field(I18n.T("v11.cm.d.max"), max));
        var body = Ui.Col(14,
            Field(I18n.T("v11.cm.d.name"), name),
            Field(I18n.T("v11.cm.d.game"), game),
            Field(I18n.T("v11.cm.d.address"), Ui.Row(8, address, here), I18n.T("v11.cm.d.address.note")),
            buildField,
            numbers,
            Field(I18n.T("v11.cm.d.vis"), chips, I18n.T("v11.cm.d.vis.note")),
            error);
        w.Dialog(I18n.T(edit is null ? "v11.cm.d.title" : "v11.cm.d.title.edit"), new ScrollViewer { Content = body, MaxHeight = 560 }, 640,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
    }

    // ---------------------------------------------------------------- «Подключиться»

    /// <summary>
    /// Подключиться к серверу: Minecraft — адрес в «Сетевую игру» выбранной сборки и запуск этой сборки;
    /// другие игры — сначала сборка по коду (как «Ввести код» на вкладке «Профили»), потом адрес
    /// в буфер обмена и обычный запуск игры.
    /// </summary>
    public static void Join(GameServer s)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var (address, error) = Servers.CheckAddress(s.Address);
        if (error is not null) { w.Toast(I18n.T("v11.cm.join.badAddress"), bad: true); return; }
        var game = AppState.Games.FirstOrDefault(g => g.Def.Id == s.Game);
        var plan = Servers.Plan(s, game);
        if (!plan.GameHere) { NoGame(s); return; }
        if (plan.Minecraft) JoinMinecraft(s, address);
        else if (plan.InstallBuild) JoinWithBuild(s, game!, address);
        else StartGame(game!, address);
    }

    static void NoGame(GameServer s)
    {
        var w = MainWindow.Current!;
        var def = GameCatalog.ById(s.Game);
        var buttons = new List<Control> { Ui.Button(I18n.T("common.close"), w.CloseDialog) };
        if (def is not null)
            buttons.Add(Ui.Button(I18n.T("v11.cm.join.openGame"), () => { w.CloseDialog(); w.Navigate(() => new GamePage(def.Id)); }, "primary", Icons.External));
        w.Dialog(I18n.T("v11.cm.join.noGame.title"), Ui.Text(I18n.T("v11.cm.join.noGame.text", ("game", def?.Name ?? s.Game)), "muted", wrap: true), buttons.ToArray());
    }

    static void StartGame(GameState game, string address)
    {
        if (address != "") Copy(address, I18n.T("v11.cm.join.copied", ("address", address)));
        Actions.Play(game);
    }

    [GeneratedRegex(@"\d+(?:\.\d+)+")] private static partial Regex VersionNumber();

    /// <summary>Сборка под сервер: та же версия (и загрузчик, если он назван), иначе выбранная, иначе первая.</summary>
    static McInstance BestInstance(List<McInstance> list, string serverVersion)
    {
        var number = VersionNumber().Match(serverVersion).Value;
        var loader = Mc.Loaders.FirstOrDefault(l => l != "vanilla" && serverVersion.Contains(l, StringComparison.OrdinalIgnoreCase));
        return list.FirstOrDefault(i => number != "" && i.GameVersion == number && (loader is null ? !i.Modded : i.Loader == loader))
            ?? list.FirstOrDefault(i => number != "" && i.GameVersion == number)
            ?? (Mc.Active is { } a && list.Any(i => i.Id == a.Id) ? list.First(i => i.Id == a.Id) : list[0]);
    }

    static void JoinMinecraft(GameServer s, string address)
    {
        var w = MainWindow.Current!;
        var instances = Mc.Instances();
        if (instances.Count == 0) { w.Toast(I18n.T("v11.cm.join.noBuild")); MinecraftPage.Play(); return; }
        var pick = new ComboBox { MinWidth = 360 };
        foreach (var i in instances) pick.Items.Add(new ComboBoxItem { Content = $"{i.Name} · {i.Label}", Tag = i.Id });
        var best = BestInstance(instances, s.Version);
        pick.SelectedItem = pick.Items.OfType<ComboBoxItem>().First(x => (string)x.Tag! == best.Id);
        var note = Ui.Text("", "small", color: Ui.Res("Warn"), wrap: true);
        McInstance? Chosen() => pick.SelectedItem is ComboBoxItem { Tag: string id } ? instances.FirstOrDefault(i => i.Id == id) : null;
        void Check()
        {
            var number = VersionNumber().Match(s.Version).Value;
            note.Text = Chosen() is { } i && number != "" && i.GameVersion != number ? I18n.T("v11.cm.join.otherVersion", ("server", number), ("build", i.GameVersion)) : "";
        }
        pick.SelectionChanged += (_, _) => Check();
        Check();
        var go = Ui.Button(I18n.T("v11.cm.s.join"), () =>
        {
            if (Chosen() is not { } i) return;
            try
            {
                if (!McServers.List(i).Any(x => x.Address.Equals(address, StringComparison.OrdinalIgnoreCase))) McServers.Add(i, s.Name, address);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                w.Toast(Jobs.Explain(e), bad: true);
                return;
            }
            w.CloseDialog();
            w.Toast(I18n.T("v11.cm.join.added", ("name", s.Name), ("build", i.Name)));
            MinecraftPage.Play(i);
        }, "primary", Icons.Play);
        w.Dialog(I18n.T("v11.cm.join.title", ("name", s.Name)),
            Ui.Col(12, Ui.Text(I18n.T("v11.cm.join.mc.text", ("address", address)), "muted", wrap: true), Field(I18n.T("v11.cm.join.mc.build"), pick), note),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
    }

    /// <summary>Сборка друга по коду: что поставим, затем запуск — тот же код и та же установка, что на вкладке «Профили».</summary>
    static async void JoinWithBuild(GameServer s, GameState game, string address)
    {
        var w = MainWindow.Current!;
        w.Toast(I18n.T("v11.cm.join.loading"));
        CodeProfile profile;
        try { profile = await ProfileCode.Fetch(s.Build); }
        catch (Exception e) when (e is InvalidDataException or HttpRequestException or TaskCanceledException)
        {
            w.Toast(e is InvalidDataException ? e.Message : Jobs.Explain(e), bad: true);
            return;
        }
        var registry = game.Registry!;
        var mods = profile.Mods.Where(m => !Mods.Installer.IsLoader(game.Def, m.Id)).ToList();
        var work = mods.Count(m => registry.Get(m.Id) is not { } have || have.Bool("missing") || !Versions.Same(have.Str("version"), m.Version));
        var configs = profile.ConfigFiles;
        var withConfig = new CheckBox { Content = I18n.T("v11.cm.join.config", ("n", configs)), IsChecked = configs > 0, IsEnabled = configs > 0 };
        var others = new CheckBox { Content = I18n.T("v11.cm.join.others"), IsChecked = true };
        async void Install()
        {
            w.CloseDialog();
            // Без загрузчика InstallCode сам предложит его поставить и ничего не начнёт — это не «сборка не встала».
            var loader = game.LoaderInstalled;
            if (await Actions.InstallCode(game, profile, withConfig.IsChecked == true, others.IsChecked == true)) StartGame(game, address);
            else w.Toast(loader ? I18n.T("v11.cm.join.buildFailed") : I18n.T("v11.fix.cm.join.loaderFirst", ("loader", game.Def.LoaderName)), bad: loader);
        }
        var text = work == 0
            ? I18n.T("v11.cm.join.build.ready", ("name", profile.Name), ("game", game.Def.Name))
            : I18n.T("v11.cm.join.build.text", ("name", profile.Name), ("n", mods.Count), ("k", work), ("game", game.Def.Name));
        var body = Ui.Col(12, Ui.Text(text, "muted", wrap: true));
        if (work > 0) { body.Children.Add(withConfig); body.Children.Add(others); }
        var play = Ui.Button(I18n.T("v11.cm.join.play"), () => { w.CloseDialog(); StartGame(game, address); }, work == 0 ? "primary" : "ghost", Icons.Play);
        var buttons = new List<Control> { Ui.Button(I18n.T("common.cancel"), w.CloseDialog), play };
        if (work > 0) buttons.Add(Ui.Button(I18n.T("v11.cm.join.install"), Install, "primary", Icons.Download));
        w.Dialog(I18n.T("v11.cm.join.title", ("name", s.Name)), body, 560, buttons.ToArray());
    }
}
