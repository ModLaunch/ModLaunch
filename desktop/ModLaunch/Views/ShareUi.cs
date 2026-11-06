using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>
/// 24.0: «Поделиться сборкой» и «Ввести код» — одинаково для всех игр. Код короткий (ABCD-EFGH),
/// в буфер обмена сразу идёт приглашение со ссылкой на ModLaunch. Друг вставляет код где угодно:
/// на главной, на вкладке «Профили», в строке поиска или ссылкой modlaunch://pack/….
/// </summary>
public static class ShareUi
{
    static MainWindow W => MainWindow.Current!;
    const string Site = "https://modlaunchapp.com";
    static bool _busy;

    /// <summary>Приглашение для друга: код, игра и где взять ModLaunch.</summary>
    public static string Invite(string code, string game) => I18n.T("v24.share.invite", ("game", game), ("code", code), ("site", Site));

    // ---------------------------------------------------------------- поделиться

    /// <summary>Поделиться модами игры (не Minecraft).</summary>
    public static async void Share(GameState g)
    {
        if (g.Def.IsMinecraft) { if (Mc.Active is { } active) Share(active); return; }
        if (g.Registry is not { } registry || _busy) return;
        var name = Profiles.List(g.Def.Id).FirstOrDefault(p => p.Active)?.Name ?? g.Def.Name;
        SharePack pack;
        try { pack = await Task.Run(() => ShareCode.FromRegistry(g.Def, registry, name)); }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); return; }
        if (pack.Mods.Count == 0) { W.Toast(I18n.T("v24.share.empty"), bad: true); return; }
        await Publish(pack);
    }

    /// <summary>Поделиться сборкой Minecraft: файлы ищутся на Modrinth по SHA-1.</summary>
    public static async void Share(McInstance i)
    {
        if (_busy) return;
        _busy = true;
        SharePack pack;
        try { pack = Program.Demo ? DemoMc(i) : await ShareCode.FromMinecraft(i); }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); _busy = false; return; }
        _busy = false;
        if (pack.Count == 0) { W.Toast(I18n.T("v24.share.empty.mc"), bad: true); return; }
        await Publish(pack);
    }

    static async Task Publish(SharePack pack)
    {
        if (_busy) return;
        _busy = true;
        W.Toast(I18n.T("v24.share.preparing"));
        string code;
        var offline = false;
        try { code = Program.Demo ? "K7QM9XPA" : await ShareCode.Upload(pack); }
        catch (Exception e)
        {
            // Сервер недоступен (нет сети, правила не опубликованы) — длинный код работает и без него.
            Guard.Log(e);
            code = ShareCode.LongCode(pack);
            offline = true;
        }
        finally { _busy = false; }
        ShowCode(pack, code, offline);
    }

    public static void ShowCode(SharePack pack, string code, bool offline)
    {
        var shown = offline ? code : ShareCode.Pretty(code);
        var invite = Invite(shown, pack.GameName);
        _ = TopLevel.GetTopLevel(W)?.Clipboard?.SetTextAsync(offline ? shown : invite);
        // Длинный код показываем строками по 64 знака (переносы при вставке не мешают — пробелы и переносы отбрасываются).
        var display = offline ? string.Join("\n", Enumerable.Range(0, (shown.Length + 63) / 64).Select(i => shown.Substring(i * 64, Math.Min(64, shown.Length - i * 64)))) : shown;
        var box = new TextBox
        {
            Text = display, IsReadOnly = true, FontFamily = new FontFamily("Consolas, monospace"),
            FontSize = offline ? 12 : 30, FontWeight = offline ? FontWeight.Normal : FontWeight.Bold, LetterSpacing = offline ? 0 : 3,
            HorizontalContentAlignment = offline ? HorizontalAlignment.Left : HorizontalAlignment.Center, Padding = new Thickness(12, 14),
            TextWrapping = offline ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxHeight = offline ? 140 : double.PositiveInfinity,
        };
        var body = Ui.Col(12,
            Ui.Text(I18n.T(pack.IsMinecraft ? "v24.share.ready.mc" : "v24.share.ready.text", ("n", pack.Count), ("game", pack.GameName)), "muted", wrap: true),
            box,
            Ui.Row(8, Ui.Icon(Icons.Check, 15, Ui.Res("Good")), Ui.Text(I18n.T(offline ? "v24.share.copiedCode" : "v24.share.copied"), "small muted", wrap: true)));
        if (offline) body.Children.Add(Note(Icons.WifiOff, I18n.T("v24.share.offline")));
        var nexus = pack.Mods.Count(m => m.Source == "nexus");
        if (nexus > 0) body.Children.Add(Note(Icons.Info, I18n.T("v24.share.nexusNote", ("n", nexus))));
        if (pack.Left is { Count: > 0 } left) body.Children.Add(Note(Icons.Info, I18n.T("v24.share.left", ("list", string.Join(", ", left.Take(6))))));
        if (pack.Config is not null && !offline) body.Children.Add(Note(Icons.Sliders, I18n.T("v24.share.withConfig")));
        W.Dialog(I18n.T("v24.share.ready"), body, 560,
            Ui.Button(I18n.T("common.close"), W.CloseDialog),
            Ui.Button(I18n.T("code.copy"), () => { _ = TopLevel.GetTopLevel(W)?.Clipboard?.SetTextAsync(shown); W.Toast(I18n.T("v24.share.copiedCode")); }, "", Icons.Copy),
            Ui.Button(I18n.T("v24.share.copyInvite"), () => { _ = TopLevel.GetTopLevel(W)?.Clipboard?.SetTextAsync(offline ? Invite(shown, pack.GameName) : invite); W.Toast(I18n.T("v24.share.copied")); }, "primary", Icons.Users));
    }

    static Control Note(string icon, string text)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        var i = Ui.Icon(icon, 14, Ui.Res("Muted"));
        i.VerticalAlignment = VerticalAlignment.Top;
        i.Margin = new Thickness(0, 2, 0, 0);
        grid.Children.Add(i);
        var t = Ui.Text(text, "small muted", wrap: true);
        Grid.SetColumn(t, 1);
        grid.Children.Add(t);
        return grid;
    }

    // ---------------------------------------------------------------- ввести код

    /// <summary>Окно «Ввести код». game — игра, со страницы которой открыли (для кодов r2modman).</summary>
    public static void Enter(GameState? game = null, string? prefill = null)
    {
        var box = new TextBox { Watermark = I18n.T("v24.share.placeholder"), Text = prefill ?? "", FontFamily = new FontFamily("Consolas, monospace"), FontSize = 16 };
        var status = Ui.Text("", "small muted", wrap: true);
        Button? show = null;
        var loading = false;
        var cts = new CancellationTokenSource();
        // Окно закрыли (отмена, другое окно поверх) — пришедшая позже сборка ничего не закрывает и не открывает.
        bool Gone() => cts.IsCancellationRequested || TopLevel.GetTopLevel(box) is null;
        async void Load()
        {
            if (loading) return;
            var input = ShareCode.Parse(box.Text);
            if (input is null) { status.Text = I18n.T("v24.share.badInput"); return; }
            loading = true;
            show!.IsEnabled = false;
            status.Text = I18n.T("common.loading");
            try
            {
                if (input.Kind == "r2") { W.CloseDialog(); OpenR2(input.Value, game); return; }
                var pack = Program.Demo ? DemoPack() : input.Kind == "long" ? ShareCode.FromLongCode(input.Value) : await ShareCode.Fetch(input.Value, cts.Token);
                if (Gone()) return;
                W.CloseDialog();
                Preview(pack);
            }
            catch (Exception e)
            {
                if (Gone()) return;
                status.Text = e is InvalidDataException ? e.Message : Social.Firebase.Explain("share", e);
                show.IsEnabled = true;
            }
            finally { loading = false; }
        }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Load(); };
        show = Ui.Button(I18n.T("code.show"), Load, "primary", Icons.Search);
        W.Dialog(I18n.T("v24.share.enter"), Ui.Col(10, Ui.Text(I18n.T("v24.share.enter.text"), "muted", wrap: true), box, status),
            Ui.Button(I18n.T("common.cancel"), () => { cts.Cancel(); W.CloseDialog(); }), show);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => box.Focus());
        if (!string.IsNullOrWhiteSpace(prefill)) Load();
    }

    /// <summary>Код r2modman (Thunderstore): нужна игра — та, со страницы которой открыли, или единственная подходящая.</summary>
    static async void OpenR2(string code, GameState? game)
    {
        game ??= AppState.Games.Where(g => g.Status == Detect.Found && ProfileCode.Supported(g.Def)).ToList() is { Count: 1 } one ? one[0] : null;
        if (game is null || !ProfileCode.Supported(game.Def)) { W.Toast(I18n.T("v24.share.r2NoGame"), bad: true); return; }
        try
        {
            var profile = await ProfileCode.Fetch(code);
            var page = new GamePage(game.Def.Id, "profiles");
            W.Navigate(() => page);
            page.PreviewCode(profile);
        }
        catch (Exception e) { W.Toast(e is InvalidDataException ? e.Message : Jobs.Explain(e), bad: true); }
    }

    /// <summary>Что в сборке друга и что с ней будет. Игры нет — подсказываем, как её добавить.</summary>
    public static void Preview(SharePack pack)
    {
        if (pack.IsMinecraft) { PreviewMc(pack); return; }
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == pack.Game);
        if (g is null || g.Status != Detect.Found || g.Registry is null)
        {
            W.Dialog(I18n.T("v24.share.noGame", ("game", pack.GameName)), Ui.Text(I18n.T("v24.share.noGame.text", ("game", pack.GameName)), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), W.CloseDialog),
                Ui.Button(I18n.T("add.title"), () => { W.CloseDialog(); W.Navigate(() => new AddGamePage()); }, "primary", Icons.Plus));
            return;
        }
        var registry = g.Registry;
        var mods = pack.Mods.Where(m => !Mods.Installer.IsLoader(g.Def, m.Id)).ToList();
        var rows = new StackPanel { Spacing = 6 };
        var work = 0;
        foreach (var m in mods)
        {
            var have = registry.Get(m.Id);
            var same = have is not null && !have.Bool("missing") && (m.Version == "" || Versions.Same(have.Str("version"), m.Version));
            var (text, bg, fg) = same ? (I18n.T("pack.have"), Ui.Res("Surface3"), Ui.Res("Muted"))
                : have is not null && !have.Bool("missing") ? (I18n.T("code.other", ("version", have.Str("version") ?? "?")), Ui.Soft("#3A2A12", "#F2B84B"), Ui.Res("Warn"))
                : (I18n.T("pack.will"), Ui.Res("Surface3"), Ui.Res("Text"));
            if (!same) work++;
            var tag = ModRow.Tag(text, bg, fg);
            DockPanel.SetDock(tag, Dock.Right);
            var label = Ui.Text($"{m.Name}" + (m.Version != "" ? $" · {m.Version}" : "") + (m.Enabled ? "" : " · " + I18n.T("inst.off")), m.Enabled ? "" : "muted");
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            rows.Children.Add(new DockPanel { Children = { tag, label } });
        }
        var withConfig = new CheckBox { Content = I18n.T("v24.share.applyConfig"), IsChecked = pack.Config is not null, IsEnabled = pack.Config is not null };
        var others = new CheckBox { Content = I18n.T("code.disableOthers"), IsChecked = true };
        var body = Ui.Col(12,
            Ui.Text(I18n.T("v24.share.preview.text", ("game", g.Def.Name), ("n", mods.Count), ("k", work)), "muted", wrap: true),
            new ScrollViewer { Content = rows, MaxHeight = 300 },
            withConfig, others);
        var nexus = mods.Count(m => m.Source == "nexus" && registry.Get(m.Id) is null);
        if (nexus > 0 && !(Settings.NexusPremium && !string.IsNullOrEmpty(Settings.NexusApiKey))) body.Children.Add(Note(Icons.Globe, I18n.T("v24.share.nexusBrowser", ("n", nexus))));
        if (pack.Left is { Count: > 0 } left) body.Children.Add(Note(Icons.Info, I18n.T("v24.share.leftFriend", ("list", string.Join(", ", left.Take(6))))));
        var go = Ui.Button(I18n.T("v24.share.install"), () =>
        {
            W.CloseDialog();
            _ = Actions.InstallShared(g, pack, withConfig.IsChecked == true, others.IsChecked == true);
        }, "primary", Icons.Download);
        W.Dialog(I18n.T("code.preview.title", ("name", pack.Name)), body, 600, Ui.Button(I18n.T("common.cancel"), W.CloseDialog), go);
    }

    static void PreviewMc(SharePack pack)
    {
        if (Mc.State is not { Status: Detect.Found })
        {
            W.Dialog(I18n.T("v24.share.noGame", ("game", "Minecraft")), Ui.Text(I18n.T("v24.share.noGame.text", ("game", "Minecraft")), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), W.CloseDialog),
                Ui.Button(I18n.T("add.title"), () => { W.CloseDialog(); W.Navigate(() => new AddGamePage()); }, "primary", Icons.Plus));
            return;
        }
        var files = pack.Files ?? [];
        var names = files.Select(f => Path.GetFileNameWithoutExtension(f.Path)).ToList();
        var label = pack.McLoader is null or "vanilla" ? $"Minecraft {pack.McVersion}" : $"{Mc.LoaderTitle(pack.McLoader)} {pack.McVersion}";
        var list = new StackPanel { Spacing = 4 };
        foreach (var n in names.Take(60)) list.Children.Add(Ui.Text(n, "small"));
        if (names.Count > 60) list.Children.Add(Ui.Text($"+{names.Count - 60}", "small muted"));
        var body = Ui.Col(12,
            Ui.Text(I18n.T("v24.share.preview.mc", ("build", label), ("n", files.Count)), "muted", wrap: true),
            new ScrollViewer { Content = list, MaxHeight = 280 });
        if (pack.Left is { Count: > 0 } left) body.Children.Add(Note(Icons.Info, I18n.T("v24.share.leftFriend", ("list", string.Join(", ", left.Take(6))))));
        W.Dialog(I18n.T("code.preview.title", ("name", pack.Name)), body, 600,
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("v24.share.install.mc"), () => { W.CloseDialog(); Actions.InstallSharedMc(pack); }, "primary", Icons.Download));
    }

    // ---------------------------------------------------------------- для снимков

    public static SharePack DemoPack() => new("lethal-company", "Lethal Company", "Ночь с друзьями",
    [
        new("Evaisa-LethalLib", "LethalLib", "1.0.1", "thunderstore", true), new("notnotnotswipez-MoreCompany", "MoreCompany", "1.10.1", "thunderstore", true),
        new("x753-More_Suits", "More Suits", "1.4.3", "thunderstore", true), new("Sligili-More_Emotes", "More Emotes", "1.3.3", "thunderstore", false),
        new("FlipMods-ReservedItemSlotCore", "ReservedItemSlotCore", "2.0.38", "thunderstore", true),
    ], Config: [1], Left: ["My Local Tweaks"]);

    static SharePack DemoMc(McInstance i) => new("minecraft", "Minecraft", i.Name, [], i.GameVersion, i.Loader, i.LoaderVersion,
        [new("mods/sodium.jar", new string('a', 40), new string('b', 128), "https://cdn.modrinth.com/x/sodium.jar", 1), new("mods/iris.jar", new string('c', 40), new string('d', 128), "https://cdn.modrinth.com/x/iris.jar", 1)],
        null, ["custom-tweaks.jar"]);
}
