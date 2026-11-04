using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>
/// 23.9: «Поделиться сборкой» и «Ввести код» для любой игры. Код крупно и сразу в буфере вместе
/// с приглашением (где взять ModLaunch). Друг вставляет код (или всё приглашение) — видит, что
/// поставится, и получает те же моды. Код в буфере обмена ModLaunch замечает сам.
/// </summary>
public static class BuildShare
{
    static MainWindow W => MainWindow.Current!;

    /// <summary>Можно ли делиться сборкой этой игры: есть каталог модов (у Minecraft — выбранная сборка).</summary>
    public static bool Supported(GameState g) => g.Def.IsMinecraft ? Mc.Active is not null : g.Registry is not null && g.Def.HasCatalog;

    static string NameOf(GameState g) => g.Def.IsMinecraft ? Mc.Active?.Name ?? "Minecraft"
        : Profiles.List(g.Def.Id).FirstOrDefault(p => p.Active)?.Name ?? g.Def.Name;

    /// <summary>Карточка для страницы игры: одна главная кнопка и «Ввести код».</summary>
    public static Control Card(GameState g, Action<CodeProfile>? r2m, params Control[] more)
    {
        var buttons = Ui.Row(10, Ui.Button(I18n.T("v23.code.share"), () => Share(g), "primary", Icons.Upload), Ui.Button(I18n.T("code.enter"), () => Enter(r2m), "", Icons.Download));
        foreach (var m in more) buttons.Children.Add(m);
        return Ui.Card(Ui.Col(14, Ui.Text(I18n.T("v23.code.title"), "h2"), Ui.Text(I18n.T("v23.code.hint"), "muted", wrap: true), buttons), 22);
    }

    // ---------------------------------------------------------------- поделиться

    public static async void Share(GameState g)
    {
        var name = NameOf(g);
        var status = Ui.Text(I18n.T("code.getting"), "muted");
        W.Dialog(I18n.T("v23.code.share"), status, Ui.Button(I18n.T("common.cancel"), W.CloseDialog));
        string code;
        try { code = await BuildCode.Upload(g, name); }
        catch (Exception e)
        {
            var text = e is InvalidOperationException ? e.Message : Social.Account.Owner ? Social.Firebase.Explain("builds", e) : I18n.T("v23.code.offline");
            W.Dialog(I18n.T("v23.code.share"), Ui.Text(text, "muted", wrap: true), Ui.Button(I18n.T("common.close"), W.CloseDialog));
            return;
        }
        ShowCode(g, code, name);
    }

    public static void ShowCode(GameState g, string code, string name)
    {
        var invite = BuildCode.Invite(code, g.Def.Name, name);
        _ = TopLevel.GetTopLevel(W)?.Clipboard?.SetTextAsync(invite);
        BuildCode.Remember(code);
        var big = new TextBlock
        {
            Text = BuildCode.Show(code), FontSize = 30, FontWeight = FontWeight.Bold, LetterSpacing = 2, HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = new FontFamily("Consolas, monospace"), Foreground = Ui.Res("Text"),
        };
        var box = new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 18), Child = big };
        var preview = new TextBlock { Text = invite, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Ui.Res("Muted") };
        W.Dialog(I18n.T("code.ready"),
            Ui.Col(12, Ui.Text(I18n.T("v23.code.ready.text", ("game", g.Def.Name)), "muted", wrap: true), box,
                Ui.Row(8, Ui.Icon(Icons.Check, 15, Ui.Res("Good")), Ui.Text(I18n.T("v23.code.copied"), "small muted")), preview),
            Ui.Button(I18n.T("common.close"), W.CloseDialog),
            Ui.Button(I18n.T("code.copy"), () => { _ = TopLevel.GetTopLevel(W)?.Clipboard?.SetTextAsync(BuildCode.Show(code)); W.Toast(I18n.T("code.copied")); }, "", Icons.List),
            Ui.Button(I18n.T("v23.code.copyInvite"), () => { _ = TopLevel.GetTopLevel(W)?.Clipboard?.SetTextAsync(invite); W.Toast(I18n.T("v23.code.copied")); }, "primary", Icons.Upload));
    }

    // ---------------------------------------------------------------- ввести код

    /// <summary>Окно ввода. r2m — что делать с кодом r2modman (в нём нет игры: его понимает только страница игры).</summary>
    public static void Enter(Action<CodeProfile>? r2m = null, string? text = null)
    {
        var box = new TextBox { Watermark = I18n.T("v23.code.placeholder"), FontFamily = new FontFamily("Consolas, monospace"), FontSize = 16, Text = text ?? "" };
        var status = Ui.Text("", "small muted", wrap: true);
        Button? show = null;
        async void Load()
        {
            var value = (box.Text ?? "").Trim();
            var ml = BuildCode.Find(value);
            var thunder = ml is null && ProfileCode.LooksLikeCode(value);
            if (ml is null && !thunder) { status.Text = I18n.T("code.bad"); return; }
            if (thunder && r2m is null) { status.Text = I18n.T("v23.code.r2mHere"); return; }
            show!.IsEnabled = false;
            status.Text = I18n.T("common.loading");
            try
            {
                if (thunder) { var p = await ProfileCode.Fetch(value); W.CloseDialog(); r2m!(p); return; }
                var build = Program.Demo ? Demo() : await BuildCode.Fetch(ml!);
                BuildCode.Remember(build.Code);
                W.CloseDialog();
                Preview(build);
            }
            catch (Exception e) { status.Text = e is InvalidDataException ? e.Message : I18n.T("v23.code.offline"); show.IsEnabled = true; }
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Load(); };
        show = Ui.Button(I18n.T("code.show"), Load, "primary", Icons.Search);
        W.Dialog(I18n.T("code.enter.title"), Ui.Col(10, Ui.Text(I18n.T("v23.code.enter.text"), "muted", wrap: true), box, status),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog), show);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { box.Focus(); box.CaretIndex = box.Text?.Length ?? 0; });
        if (text is not null && BuildCode.Find(text) is not null) Load();
    }

    /// <summary>Что в сборке друга и что с ней будет. Игры нет — сказать, какую поставить.</summary>
    public static void Preview(SharedBuild build)
    {
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == build.Game);
        if (g is null || (build.IsMinecraft ? Mc.State?.Status != Detect.Found : g.Registry is null))
        {
            var game = g?.Def.Name ?? build.Game;
            W.Dialog(I18n.T("code.preview.title", ("name", build.Name)), Ui.Text(I18n.T("v23.code.noGame", ("game", game)), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), W.CloseDialog));
            return;
        }
        if (build.IsMinecraft) { PreviewMinecraft(build); return; }

        var registry = g.Registry!;
        var mods = build.Mods.Where(m => !Mods.Installer.IsLoader(g.Def, m.Id)).ToList();
        var rows = new StackPanel { Spacing = 6 };
        var work = 0;
        foreach (var m in mods)
        {
            var have = registry.Get(m.Id);
            var same = have is not null && !have.Bool("missing") && (m.Version == "" || Versions.Same(have.Str("version"), m.Version));
            var manual = !same && (m.Source == "file" || g.Def.CatalogId(m.Id) is null);
            var (text, bg, fg) = same ? (I18n.T("pack.have"), Ui.Res("Surface3"), Ui.Res("Muted"))
                : manual ? (I18n.T("pack.manual"), Ui.Soft("#3A2A12", "#F2B84B"), Ui.Res("Warn"))
                : have is not null ? (I18n.T("code.other", ("version", have.Str("version") ?? "?")), Ui.Soft("#3A2A12", "#F2B84B"), Ui.Res("Warn"))
                : (I18n.T("pack.will"), Ui.Res("BrandSoft"), Ui.Res("Brand2"));
            if (!same && !manual) work++;
            var tag = ModRow.Tag(text, bg, fg);
            DockPanel.SetDock(tag, Dock.Right);
            var label = Ui.Text(m.Name + (m.Version != "" ? $" · {m.Version}" : "") + (m.Enabled ? "" : " · " + I18n.T("inst.off")), m.Enabled ? "" : "muted");
            label.VerticalAlignment = VerticalAlignment.Center;
            rows.Children.Add(new DockPanel { Children = { tag, label } });
        }
        var configs = build.ConfigFiles;
        var withConfig = new CheckBox { Content = I18n.T("code.withConfig", ("n", configs)), IsChecked = configs > 0, IsEnabled = configs > 0 };
        var others = new CheckBox { Content = I18n.T("code.disableOthers"), IsChecked = true };
        var body = Ui.Col(12,
            Ui.Text(I18n.T("code.preview.text", ("n", mods.Count), ("k", work)), "muted", wrap: true),
            new ScrollViewer { Content = rows, MaxHeight = 300 }, withConfig, others);
        if (g.Def.PrimarySource == "nexus" && !Settings.NexusPremium) body.Children.Add(Ui.Text(I18n.T("v23.code.nexus"), "small muted", wrap: true));
        var go = Ui.Button(I18n.T("code.install"), () =>
        {
            W.CloseDialog();
            _ = Actions.InstallBuild(g, build, withConfig.IsChecked == true, others.IsChecked == true);
            W.Navigate(() => new GamePage(g.Def.Id, "installed"));
        }, "primary", Icons.Download);
        W.Dialog(I18n.T("code.preview.title", ("name", build.Name)), body, 600, Ui.Button(I18n.T("common.cancel"), W.CloseDialog), go);
    }

    static void PreviewMinecraft(SharedBuild build)
    {
        var temp = Path.Combine(Paths.DataDir, "temp", $"code-{build.Code}.mrpack");
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        File.WriteAllBytes(temp, build.Zip);
        McModrinth.PackIndex index;
        try { index = McModrinth.ReadIndex(temp); }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); return; }
        var loader = index.Loader is "" or "vanilla" ? "Minecraft" : Mc.LoaderTitle(index.Loader);
        var body = Ui.Col(12,
            Ui.Text(I18n.T("v23.code.mc.text", ("version", $"{loader} {index.GameVersion}"), ("n", index.Files.Count(f => f.Path.StartsWith("mods/")))), "muted", wrap: true),
            Ui.Text(string.Join(", ", index.Files.Where(f => f.Path.StartsWith("mods/")).Take(12).Select(f => Path.GetFileNameWithoutExtension(f.Path))), "small muted", wrap: true));
        W.Dialog(I18n.T("code.preview.title", ("name", build.Name)), body, 560, Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("v23.code.mc.go"), () => { W.CloseDialog(); MinecraftPage.ImportMrpack(temp); W.Navigate(() => new MinecraftPage("builds")); }, "primary", Icons.Download));
    }

    // ---------------------------------------------------------------- код в буфере обмена

    /// <summary>Окно снова активно — в буфере код сборки, которого мы ещё не предлагали? Предложить открыть.</summary>
    public static async Task CheckClipboard()
    {
        if (W.DialogOpen || Program.Screenshot) return;
        string? text;
        try { text = await (TopLevel.GetTopLevel(W)?.Clipboard?.GetTextAsync() ?? Task.FromResult<string?>(null)); }
        catch { return; }
        if (text is null || text.Length > 2000 || BuildCode.Find(text) is not { } code || BuildCode.Seen(code)) return;
        BuildCode.Remember(code);
        W.Dialog(I18n.T("v23.code.clip.title"), Ui.Text(I18n.T("v23.code.clip.text", ("code", BuildCode.Show(code))), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("code.show"), () => { W.CloseDialog(); Enter(null, code); }, "primary", Icons.Search));
    }

    static SharedBuild Demo()
    {
        var def = Games.GameCatalog.ById("lethal-company")!;
        var mods = new List<SetupMod>
        {
            new("notnotnotswipez-MoreCompany", "MoreCompany", "1.11.0", "thunderstore", true), new("x753-More_Suits", "More Suits", "1.4.5", "thunderstore", true),
            new("Evaisa-LethalLib", "LethalLib", "0.16.2", "thunderstore", true), new("anormaltwig-LateCompany", "LateCompany", "1.0.18", "thunderstore", true),
            new("tinyhoot-ShipLoot", "ShipLoot", "1.1.0", "thunderstore", false),
        };
        return BuildCode.Demo(def, "Ночь с друзьями", mods);
    }
}
