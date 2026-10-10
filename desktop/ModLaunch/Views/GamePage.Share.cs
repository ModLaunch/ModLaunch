using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Вкладка «Профили», вторая половина: код сборки (как в r2modman), история изменений
/// с «Вернуть как было» и перенос профилей из r2modman, Thunderstore Mod Manager и Gale.
/// </summary>
public sealed partial class GamePage
{
    List<ManagerProfile>? _managers;
    bool _sharing;

    // ---------------------------------------------------------------- код сборки

    Control ShareCard()
    {
        var (inCode, left) = ProfileCode.Pick(_g.Registry!);
        var get = Ui.Button(_sharing ? I18n.T("code.getting") : I18n.T("code.get"), GetCode, "primary", Icons.Upload);
        get.IsEnabled = !_sharing && inCode.Count > 0;
        var col = Ui.Col(14,
            Ui.Text(I18n.T("code.title"), "h2"),
            Ui.Text(I18n.T("code.hint"), "muted", wrap: true),
            Ui.Row(10,
                get,
                Ui.Button(I18n.T("code.enter"), EnterCode, "", Icons.Download),
                Ui.Button(I18n.T("code.saveFile"), SaveR2z, "ghost", Icons.Save),
                Ui.Button(I18n.T("code.openFile"), OpenR2z, "ghost", Icons.FilePlus)));
        if (left.Count > 0)
            col.Children.Add(Ui.Row(8, Ui.Icon(Icons.Info, 15, Ui.Res("Muted")), Ui.Text(I18n.T("code.left", ("list", string.Join(", ", left.Take(5)))), "small muted", wrap: true)));
        return Ui.Card(col, 22);
    }

    string ProfileName() => Profiles.List(_g.Def.Id).FirstOrDefault(p => p.Active)?.Name ?? _g.Def.Name;

    async void GetCode()
    {
        var w = MainWindow.Current!;
        _sharing = true;
        Build();
        string code;
        try
        {
            var zip = await ProfileCode.Build(_g, ProfileName(), withConfig: true);
            code = Program.Demo ? "018f2c3a-6b1e-4f1b-9c55-2d4e8a1f3b77" : await ProfileCode.Upload(zip);
        }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); return; }
        finally { _sharing = false; Build(); }
        ShowCode(code);
    }

    /// <summary>Код крупно, сразу в буфере обмена.</summary>
    void ShowCode(string code)
    {
        var w = MainWindow.Current!;
        _ = TopLevel.GetTopLevel(w)?.Clipboard?.SetTextAsync(code);
        var box = new TextBox { Text = code, IsReadOnly = true, FontSize = 20, FontFamily = new FontFamily("Consolas, monospace"), HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 14) };
        w.Dialog(I18n.T("code.ready"),
            Ui.Col(12, Ui.Text(I18n.T("code.ready.text"), "muted", wrap: true), box,
                Ui.Row(8, Ui.Icon(Icons.Check, 15, Ui.Res("Good")), Ui.Text(I18n.T("code.copied"), "small muted"))),
            Ui.Button(I18n.T("common.close"), w.CloseDialog),
            Ui.Button(I18n.T("code.copy"), () => { _ = TopLevel.GetTopLevel(w)?.Clipboard?.SetTextAsync(code); w.Toast(I18n.T("code.copied")); }, "primary", Icons.List));
    }

    void EnterCode()
    {
        var w = MainWindow.Current!;
        var box = new TextBox { Watermark = I18n.T("code.placeholder"), FontFamily = new FontFamily("Consolas, monospace") };
        var status = Ui.Text("", "small muted", wrap: true);
        Button? show = null;
        async void Load()
        {
            var text = (box.Text ?? "").Trim();
            if (!ProfileCode.LooksLikeCode(text)) { status.Text = I18n.T("code.bad"); return; }
            show!.IsEnabled = false;
            status.Text = I18n.T("common.loading");
            try
            {
                var profile = Program.Demo ? DemoCode() : await ProfileCode.Fetch(text);
                w.CloseDialog();
                PreviewCode(profile);
            }
            catch (Exception e) { status.Text = e is InvalidDataException ? e.Message : Jobs.Explain(e); show.IsEnabled = true; }
        }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Load(); };
        show = Ui.Button(I18n.T("code.show"), Load, "primary", Icons.Search);
        w.Dialog(I18n.T("code.enter.title"), Ui.Col(10, Ui.Text(I18n.T("code.enter.text"), "muted", wrap: true), box, status),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog), show);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => box.Focus());
    }

    /// <summary>Что в сборке друга: есть, другая версия, поставим. Галочки: настройки и лишние моды.</summary>
    void PreviewCode(CodeProfile profile)
    {
        var w = MainWindow.Current!;
        var registry = _g.Registry!;
        var mods = profile.Mods.Where(m => !Mods.Installer.IsLoader(_g.Def, m.Id)).ToList();
        var rows = new StackPanel { Spacing = 6 };
        var work = 0;
        foreach (var m in mods)
        {
            var have = registry.Get(m.Id);
            var same = have is not null && !have.Bool("missing") && Versions.Same(have.Str("version"), m.Version);
            var (text, bg, fg) = same ? (I18n.T("pack.have"), Ui.Res("Surface3"), Ui.Res("Muted"))
                : have is not null ? (I18n.T("code.other", ("version", have.Str("version") ?? "?")), Ui.Soft("#3A2A12", "#F2B84B"), Ui.Res("Warn"))
                : (I18n.T("pack.will"), Ui.Res("Surface3"), Ui.Res("Text"));
            if (!same) work++;
            var tag = ModRow.Tag(text, bg, fg);
            DockPanel.SetDock(tag, Dock.Right);
            var name = m.Id[(m.Id.IndexOf('-') + 1)..].Replace('_', ' ');
            var label = Ui.Text($"{name} · {m.Version}" + (m.Enabled ? "" : " · " + I18n.T("inst.off")), m.Enabled ? "" : "muted");
            label.VerticalAlignment = VerticalAlignment.Center;
            rows.Children.Add(new DockPanel { Children = { tag, label } });
        }
        var configs = profile.ConfigFiles;
        var withConfig = new CheckBox { Content = I18n.T("code.withConfig", ("n", configs)), IsChecked = configs > 0, IsEnabled = configs > 0 };
        var others = new CheckBox { Content = I18n.T("code.disableOthers"), IsChecked = true };
        var body = Ui.Col(12,
            Ui.Text(I18n.T("code.preview.text", ("n", mods.Count), ("k", work)), "muted", wrap: true),
            new ScrollViewer { Content = rows, MaxHeight = 300 },
            withConfig, others);
        var go = Ui.Button(I18n.T("code.install"), () =>
        {
            w.CloseDialog();
            Actions.InstallCode(_g, profile, withConfig.IsChecked == true, others.IsChecked == true);
        }, "primary", Icons.Download);
        w.Dialog(I18n.T("code.preview.title", ("name", profile.Name)), body, 600, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
    }

    async void SaveR2z()
    {
        var w = MainWindow.Current!;
        var file = await w.PickSaveFile(I18n.T("code.saveFile"), $"{ProfileName()}.r2z");
        if (file is null) return;
        try
        {
            await File.WriteAllBytesAsync(file, await ProfileCode.Build(_g, ProfileName(), withConfig: true));
            w.Toast(I18n.T("code.saved"));
        }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
    }

    async void OpenR2z()
    {
        var w = MainWindow.Current!;
        var file = await w.PickProfileFile(I18n.T("code.openFile"));
        if (file is null) return;
        try { PreviewCode(ProfileCode.Read(await File.ReadAllBytesAsync(file))); }
        catch (Exception e) { w.Toast(e.Message, bad: true); }
    }

    static CodeProfile DemoCode()
    {
        var yaml = ProfileCode.WriteYaml("Ночь с друзьями",
        [
            new("BepInEx-BepInExPack", "5.4.2100", true), new("Evaisa-LethalLib", "1.0.1", true), new("notnotnotswipez-MoreCompany", "1.10.1", true),
            new("x753-More_Suits", "1.4.3", true), new("Sligili-More_Emotes", "1.3.3", false), new("FlipMods-ReservedItemSlotCore", "2.0.38", true),
        ]);
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            using (var s = new StreamWriter(zip.CreateEntry("export.r2x").Open())) s.Write(yaml);
            using (var s = new StreamWriter(zip.CreateEntry("config/MoreCompany.cfg").Open())) s.Write("[General]\nPlayerCount = 8\n");
        }
        return ProfileCode.Read(buffer.ToArray());
    }

    // ---------------------------------------------------------------- история изменений

    Control? HistoryCard()
    {
        var snaps = Snapshots.List(_g.Def.Id);
        if (snaps.Count == 0) return null;
        var list = new StackPanel { Spacing = 6 };
        foreach (var s in snaps.Take(6))
        {
            var snap = s;
            var back = Ui.Button(I18n.T("snap.back"), () => ConfirmRestore(snap), "", Icons.Refresh);
            back.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(back, Dock.Right);
            var info = Ui.Col(2, Ui.Text(I18n.T("snap.reason." + s.Reason), "h3"),
                Ui.Text(Ui.Ago(s.At) + " · " + I18n.T("snap.mods", ("n", s.Count)) + (s.HasConfig ? " · " + I18n.T("snap.withConfig") : ""), "small muted"));
            info.VerticalAlignment = VerticalAlignment.Center;
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = new DockPanel { Children = { back, info } } });
        }
        return Ui.Card(Ui.Col(14, Ui.Text(I18n.T("snap.title"), "h2"), Ui.Text(I18n.T("snap.hint"), "muted", wrap: true), list), 22);
    }

    void ConfirmRestore(Snapshot snap)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("snap.confirm", ("when", Ui.Ago(snap.At))), Ui.Text(I18n.T("snap.confirm.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("snap.back"), () => { w.CloseDialog(); Actions.RestoreSnapshot(_g, snap); }, "primary", Icons.Refresh));
    }

    // ---------------------------------------------------------------- из других программ

    Control? ManagersCard()
    {
        _managers ??= ManagerImport.Discover(_g.Def);
        if (_managers.Count == 0) return null;
        var list = new StackPanel { Spacing = 6 };
        foreach (var p in _managers.Take(8))
        {
            var profile = p;
            var go = Ui.Button(I18n.T("mgr.import"), () => ConfirmManager(profile), "", Icons.Download);
            go.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(go, Dock.Right);
            var info = Ui.Col(2, Ui.Text(p.Name, "h3"), Ui.Text(p.Manager + " · " + I18n.T("snap.mods", ("n", p.Mods.Count)), "small muted"));
            info.VerticalAlignment = VerticalAlignment.Center;
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = new DockPanel { Children = { go, info } } });
        }
        return Ui.Card(Ui.Col(14, Ui.Text(I18n.T("mgr.title"), "h2"), Ui.Text(I18n.T("mgr.hint"), "muted", wrap: true), list), 22);
    }

    void ConfirmManager(ManagerProfile p)
    {
        var w = MainWindow.Current!;
        var names = string.Join(", ", p.Mods.Take(8).Select(m => m.Id[(m.Id.IndexOf('-') + 1)..].Replace('_', ' '))) + (p.Mods.Count > 8 ? $" +{p.Mods.Count - 8}" : "");
        w.Dialog(I18n.T("mgr.confirm", ("name", p.Name), ("manager", p.Manager)),
            Ui.Col(10, Ui.Text(I18n.T("mgr.confirm.text", ("n", p.Mods.Count)), "muted", wrap: true), Ui.Text(names, "small muted", wrap: true)),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("mgr.import"), () => { w.CloseDialog(); Actions.ImportManager(_g, p); }, "primary", Icons.Download));
    }

    /// <summary>Снимки: вкладка «Профили» с кодом, историей и переносом; код; сборка друга; «вернуть как было».</summary>
    [DemoShots]
    static void ProfilesShots(Shots s)
    {
        var w = s.Window;
        var lc = AppState.Game("lethal-company");
        var registry = lc.Registry!;
        foreach (var (id, version) in new[] { ("Evaisa-LethalLib", "0.16.1"), ("notnotnotswipez-MoreCompany", "1.10.1"), ("x753-More_Suits", "1.4.3") })
        {
            Directory.CreateDirectory(Path.Combine(registry.ModsDir, id));
            registry.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["name"] = id[(id.IndexOf('-') + 1)..].Replace('_', ' '), ["folder"] = id, ["version"] = version, ["source"] = "thunderstore" });
        }
        Directory.CreateDirectory(Path.Combine(lc.Path!, "BepInEx", "config"));
        File.WriteAllText(Path.Combine(lc.Path!, "BepInEx", "config", "MoreCompany.cfg"), "[General]\nPlayerCount = 6\n");
        Profiles.Save("lethal-company", "Ночь с друзьями", registry);
        Snapshots.Take(lc, "update");
        Snapshots.Take(lc, "code");
        // Профиль r2modman на диске — чтобы появилась карточка переноса.
        var root = Path.Combine(Paths.DataDir, "demo-managers");
        var profile = Path.Combine(root, "r2modmanPlus-local", "LethalCompany", "profiles", "Default");
        Directory.CreateDirectory(Path.Combine(profile, "BepInEx", "plugins", "Evaisa-LethalLib"));
        File.WriteAllText(Path.Combine(profile, "mods.yml"), ProfileCode.WriteYaml("Default", [new("Evaisa-LethalLib", "0.16.1", true), new("x753-More_Suits", "1.4.3", true), new("Sligili-More_Emotes", "1.3.3", true)]).Replace("profileName: 'Default'\nmods:\n", "").Replace("\n  ", "\n").TrimStart(' '));
        Environment.SetEnvironmentVariable("MODLAUNCH_MANAGERS_ROOT", root);
        AppState.Notify();

        var page = new GamePage("lethal-company", "profiles");
        w.Navigate(() => page);
        s.Save("profiles-1-tab");
        s.Window.Height = 1400;
        s.Pump(400);
        s.Save("profiles-1b-tab-full");
        s.Window.Height = 800;

        page.ShowCode("018f2c3a-6b1e-4f1b-9c55-2d4e8a1f3b77");
        s.Save("profiles-2-code");
        w.CloseDialog();
        page.PreviewCode(DemoCode());
        s.Save("profiles-3-preview");
        w.CloseDialog();
        page.ConfirmRestore(Snapshots.List("lethal-company")[0]);
        s.Save("profiles-4-restore");
        w.CloseDialog();
        if (page._managers is { Count: > 0 } found) page.ConfirmManager(found[0]);
        s.Save("profiles-5-manager");
        w.CloseDialog();
        Environment.SetEnvironmentVariable("MODLAUNCH_MANAGERS_ROOT", null);
    }

    /// <summary>После «Применить профиль»: чего не хватает и что другой версии — поставить?</summary>
    void OfferProfileFix(string name)
    {
        var (missing, other) = Profiles.Differences(_g.Def.Id, name, _g.Registry!);
        var fix = missing.Concat(other).Where(m => m.Source != "file" && m.Source != "").ToList();
        if (fix.Count == 0) return;
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("prof.fix.title", ("name", name)),
            Ui.Text(I18n.T("prof.fix.text", ("missing", missing.Count), ("other", other.Count), ("list", string.Join(", ", fix.Take(6).Select(m => m.Name)))), "muted", wrap: true),
            Ui.Button(I18n.T("deps.orphans.keep"), w.CloseDialog),
            Ui.Button(I18n.T("prof.fix.go", ("n", fix.Count)), () => { w.CloseDialog(); Actions.FixSetup(_g, name, fix); }, "primary", Icons.Download));
    }
}
