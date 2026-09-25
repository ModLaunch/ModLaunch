using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

public sealed partial class GamePage
{
    // ---------------------------------------------------------------- профили и сборка в файле

    Control ProfilesView()
    {
        var registry = _g.Registry!;
        var col = new StackPanel { Spacing = 14 };

        var name = new TextBox { Watermark = I18n.T("prof.placeholder"), Height = 42 };
        void SaveProfile()
        {
            try
            {
                Profiles.Save(_g.Def.Id, name.Text ?? "", registry);
                MainWindow.Current?.Toast(I18n.T("prof.savedToast", ("name", Profiles.Clean(name.Text))));
                Build();
            }
            catch (Exception e) { MainWindow.Current?.Toast(e.Message, bad: true); }
        }
        name.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) SaveProfile(); };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        bar.Children.Add(name);
        var save = Ui.Button(I18n.T("prof.save"), SaveProfile, "primary", Icons.Save);
        Grid.SetColumn(save, 1);
        bar.Children.Add(save);

        var list = new StackPanel { Spacing = 8 };
        var profiles = Profiles.List(_g.Def.Id);
        if (profiles.Count == 0)
            list.Children.Add(Ui.Col(6, Ui.Text(I18n.T("prof.empty"), "h3"), Ui.Text(I18n.T("prof.empty.text"), "muted", wrap: true)));
        foreach (var p in profiles)
        {
            var title = Ui.Row(8, Ui.Text(p.Name, "h3"));
            if (p.Active) title.Children.Add(ModRow.Tag(I18n.T("prof.active"), Ui.Res("BrandSoft"), Ui.Res("Brand2")));
            var info = Ui.Col(4, title, Ui.Text(
                I18n.T("prof.mods." + I18n.Plural(p.Count, "one", "few", "many"), ("n", p.Count)) + " · " + I18n.T("prof.saved", ("when", Ui.Ago(p.UpdatedAt))), "small muted"));
            info.VerticalAlignment = VerticalAlignment.Center;
            var profile = p.Name;
            var buttons = Ui.Row(6,
                Ui.Button(I18n.T("prof.apply"), () => ApplyProfile(profile), p.Active ? "" : "primary", Icons.Check),
                Ui.Button("", () => Overwrite(profile), "icon ghost", Icons.Save, I18n.T("prof.overwrite")),
                Ui.Button("", () => Rename(profile), "icon ghost", Icons.Edit, I18n.T("prof.rename")),
                Ui.Button("", () => RemoveProfile(profile), "icon ghost", Icons.Trash, I18n.T("prof.remove")));
            buttons.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(info);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row });
        }
        col.Children.Add(Ui.Card(Ui.Col(14, Ui.Text(I18n.T("prof.title"), "h2"), Ui.Text(I18n.T("prof.hint"), "muted"), bar, list), 22));

        col.Children.Add(Ui.Card(Ui.Col(14,
            Ui.Text(I18n.T("pack.title"), "h2"),
            Ui.Text(I18n.T("pack.hint"), "muted"),
            Ui.Row(10,
                Ui.Button(I18n.T("pack.export"), ExportPack, "", Icons.Upload),
                Ui.Button(I18n.T("pack.import"), () => ImportPack(MainWindow.Current!), "", Icons.Download))), 22));
        return col;
    }

    void ApplyProfile(string name)
    {
        try
        {
            var (on, off, missing) = Profiles.Apply(_g.Def.Id, name, _g.Registry!);
            Mods.Installer.SyncPreset(_g.Registry!, null);
            MainWindow.Current?.Toast(missing > 0
                ? I18n.T("prof.applied.missing", ("name", name), ("n", missing))
                : I18n.T("prof.applied", ("name", name), ("on", on), ("off", off)));
        }
        catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
        _missing = null;
        AppState.Notify();
    }

    void Overwrite(string name)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("prof.overwrite.title", ("name", name)), Ui.Text(I18n.T("prof.overwrite.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("common.save"), () => { w.CloseDialog(); Profiles.Save(_g.Def.Id, name, _g.Registry!); w.Toast(I18n.T("prof.savedToast", ("name", name))); Build(); }, "primary", Icons.Save));
    }

    void Rename(string name)
    {
        var w = MainWindow.Current!;
        var box = new TextBox { Text = name };
        w.Dialog(I18n.T("prof.rename"), box,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("common.save"), () => { w.CloseDialog(); Profiles.Rename(_g.Def.Id, name, box.Text ?? ""); Build(); }, "primary", Icons.Check));
        box.AttachedToVisualTree += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    void RemoveProfile(string name)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("prof.remove.title", ("name", name)), Ui.Text(I18n.T("prof.remove.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("prof.remove"), () => { w.CloseDialog(); Profiles.Remove(_g.Def.Id, name); Build(); }, "primary", Icons.Trash));
    }

    async void ExportPack()
    {
        var w = MainWindow.Current!;
        var json = ModPack.Build(_g.Def, _g.Registry!, Profiles.List(_g.Def.Id).FirstOrDefault(p => p.Active)?.Name ?? _g.Def.Name);
        var file = await w.PickSaveFile(I18n.T("dialog.packExport"), $"{_g.Def.Id}.modhub.json");
        if (file is null) return;
        await File.WriteAllTextAsync(file, json);
        w.Toast(I18n.T("pack.exported", ("n", ModPack.Count(json))));
    }

    /// <summary>Открыть сборку: что в ней есть, чего не хватает, и поставить недостающее очередью.</summary>
    public static async void ImportPack(MainWindow w)
    {
        var file = await w.PickFile(I18n.T("dialog.packImport"), json: true);
        if (file is null) return;
        Pack pack;
        try
        {
            if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException(I18n.T("err.PACK_FORMAT"));
            pack = ModPack.Parse(await File.ReadAllTextAsync(file));
        }
        catch (Exception e) { w.Toast(e.Message, bad: true); return; }
        var g = AppState.Games.FirstOrDefault(x => x.Def.Id == pack.Game);
        if (g is null) { w.Toast(I18n.T("err.PACK_GAME"), bad: true); return; }
        if (g.Registry is null)
        {
            w.Dialog(I18n.T("pack.import.title", ("name", pack.Name)), Ui.Text(I18n.T("pack.noGame", ("game", g.Def.Name)), "muted", wrap: true),
                Ui.Button(I18n.T("common.close"), w.CloseDialog));
            return;
        }

        var rows = new StackPanel { Spacing = 6 };
        var toInstall = new List<PackMod>();
        foreach (var m in pack.Mods)
        {
            var have = g.Registry.Has(m.Id);
            var catalogId = m.Source == "file" ? null : g.Def.CatalogId(m.Id);
            var state = have ? I18n.T("pack.have") : catalogId is null ? I18n.T("pack.manual") : I18n.T("pack.will");
            if (!have && catalogId is not null) toInstall.Add(m);
            var row = new DockPanel();
            var tag = ModRow.Tag(state, have ? Ui.Res("Surface3") : catalogId is null ? Ui.Hex("#3A2A12") : Ui.Res("BrandSoft"), have ? Ui.Res("Muted") : catalogId is null ? Ui.Res("Warn") : Ui.Res("Brand2"));
            DockPanel.SetDock(tag, Dock.Right);
            row.Children.Add(tag);
            row.Children.Add(Ui.Text(m.Name + (m.Version != "" ? $" · {m.Version}" : "")));
            rows.Children.Add(row);
        }
        var missing = pack.Mods.Count(m => !g.Registry.Has(m.Id));
        var body = Ui.Col(12,
            Ui.Text(I18n.T("pack.import.text", ("game", g.Def.Name), ("n", pack.Mods.Count), ("missing", missing)), "muted", wrap: true),
            new ScrollViewer { Content = rows, MaxHeight = 320 });
        if (pack.Mods.Any(m => m.Source == "file" && !g.Registry.Has(m.Id))) body.Children.Add(Ui.Text(I18n.T("pack.manual.hint"), "small muted", wrap: true));

        var n = toInstall.Count;
        var go = Ui.Button(I18n.T("pack.installN." + I18n.Plural(n, "one", "few", "many"), ("n", n)), async () =>
        {
            w.CloseDialog();
            var ids = toInstall.Select(m => g.Def.CatalogId(m.Id)!).ToList();
            var mods = Program.Demo ? Demo.Many(g.Def, ids) : await Catalog.Many(g.Def, ids);
            await Actions.InstallQueue(g, pack.Name, mods.Select(m => (m, (Pin?)null)).ToList());
            // Выключенные в сборке — выключаем и у себя.
            foreach (var m in pack.Mods.Where(m => !m.Enabled && g.Registry.Has(m.Id))) try { g.Registry.SetEnabled(m.Id, false); } catch { }
            w.Toast(I18n.T("pack.done"));
            AppState.Notify();
        }, "primary", Icons.Download);
        go.IsEnabled = n > 0;
        w.Dialog(I18n.T("pack.import.title", ("name", pack.Name)), body, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
    }

    // ---------------------------------------------------------------- сохранения

    Control SavesView()
    {
        var col = new StackPanel { Spacing = 14 };
        var savesDir = Launcher.SavesDir(_g.Def, _g.Path!);
        var head = new DockPanel();
        var buttons = Ui.Row(8,
            Ui.Button(I18n.T("bak.savesFolder"), () => Actions.OpenFolder(savesDir), "", Icons.Folder),
            Ui.Button(I18n.T("bak.create"), () =>
            {
                try
                {
                    if (Backups.Create(_g.Def.Id, savesDir, "manual") is null) throw new InvalidOperationException(I18n.T("err.BACKUP_NO_SAVES"));
                    MainWindow.Current?.Toast(I18n.T("bak.created"));
                }
                catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
                Build();
            }, "primary", Icons.Save));
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(Ui.Text(I18n.T("bak.title"), "h2"));

        var info = new StackPanel { Spacing = 6 };
        if (savesDir is null) info.Children.Add(Ui.Text(I18n.T("bak.unsupported"), "muted", wrap: true));
        else
        {
            var (exists, bytes, files) = Backups.Describe(savesDir);
            info.Children.Add(Ui.Text(exists
                ? I18n.T("bak.saves", ("files", files), ("size", Size(bytes)), ("path", savesDir))
                : I18n.T("bak.noSaves", ("path", savesDir)), "muted small", wrap: true));
            info.Children.Add(Ui.Text(Backups.OnLaunch ? I18n.T("bak.auto.on", ("n", Backups.Keep)) : I18n.T("bak.auto.off"), "muted small", wrap: true));
        }
        col.Children.Add(Ui.Card(Ui.Col(12, head, info), 22));

        var items = Backups.List(_g.Def.Id);
        if (items.Count == 0)
            col.Children.Add(Ui.Card(Ui.Col(6, Ui.Text(I18n.T("bak.empty"), "h3"), Ui.Text(I18n.T("bak.empty.text"), "muted", wrap: true)), 22));
        foreach (var b in items)
        {
            var text = Ui.Col(4, Ui.Text(b.At.ToString("d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.GetCultureInfo(I18n.Lang == "en" ? "en-US" : "ru-RU")), "h3"),
                Ui.Text($"{I18n.T("bak.reason." + b.Reason)} · {Size(b.Size)}", "small muted"));
            text.VerticalAlignment = VerticalAlignment.Center;
            var name = b.Name;
            var actions = Ui.Row(6,
                Ui.Button(I18n.T("bak.restore"), () => Restore(name, savesDir), "", Icons.Refresh),
                Ui.Button("", () => RemoveBackup(name), "icon ghost", Icons.Trash, I18n.T("bak.remove")));
            actions.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            row.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Ui.Res("Surface2"), Child = Ui.Icon(Icons.Shield, 18, Ui.Res("Brand2")) });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            col.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(12), Child = row });
        }
        return col;
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => I18n.T("bytes.b", ("n", bytes)),
        < 1024 * 1024 => I18n.T("bytes.kb", ("n", Math.Round(bytes / 1024d))),
        _ => I18n.T("bytes.mb", ("n", Math.Round(bytes / 1048576d, 1))),
    };

    void Restore(string name, string? savesDir)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("bak.restore.title"), Ui.Text(I18n.T("bak.restore.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("bak.restore"), () =>
            {
                w.CloseDialog();
                try
                {
                    if (PlayTime.IsRunning(_g.Def.Id)) throw new InvalidOperationException(I18n.T("err.BACKUP_RUNNING"));
                    if (savesDir is null) throw new InvalidOperationException(I18n.T("err.BACKUP_NO_SAVES"));
                    Backups.Restore(_g.Def.Id, name, savesDir);
                    w.Toast(I18n.T("bak.restored"));
                }
                catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
                Build();
            }, "primary", Icons.Refresh));
    }

    void RemoveBackup(string name)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("bak.remove.title"), Ui.Text(I18n.T("bak.remove.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("bak.remove"), () => { w.CloseDialog(); try { Backups.Remove(_g.Def.Id, name); } catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); } Build(); }, "primary", Icons.Trash));
    }

    // ---------------------------------------------------------------- лог

    Control LogView()
    {
        var report = Logs.Read(_g.Def, _g.Path!);
        var col = Ui.Col(14);
        var head = new DockPanel();
        var buttons = Ui.Row(8, Ui.Button(I18n.T("common.retry"), Build, "", Icons.Refresh));
        if (report.Available) buttons.Children.Add(Ui.Button(I18n.T("log.open"), () => Ui.OpenUrl(report.Path!), "", Icons.External));
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(Ui.Text(I18n.T("log.title"), "h2"));
        col.Children.Add(head);
        if (!report.Available)
        {
            col.Children.Add(Ui.Text(I18n.T("log.none"), "muted", wrap: true));
            return Ui.Card(col, 22);
        }
        col.Children.Add(Ui.Text(I18n.T("log.updated", ("time", report.Modified?.ToString("g", System.Globalization.CultureInfo.GetCultureInfo(I18n.Lang == "en" ? "en-US" : "ru-RU")) ?? "")) + " · " + report.Path, "small muted", wrap: true));
        if (report.Issues.Count == 0)
        {
            col.Children.Add(Ui.Row(10, Ui.Icon(Icons.Check, 18, Ui.Res("Good")), Ui.Text(I18n.T("log.clean"))));
            return Ui.Card(col, 22);
        }
        col.Children.Add(Ui.Text(I18n.T("log.problems"), "h3"));
        foreach (var issue in report.Issues)
        {
            var match = _g.Registry?.List().FirstOrDefault(m => Deps.Norm(m.Str("name")) == Deps.Norm(issue.Mod) || Deps.Norm(m.Str("folder")) == Deps.Norm(issue.Mod));
            var row = new DockPanel();
            if (match is not null && match.Bool("enabled", true))
            {
                var id = match.Str("id")!;
                var off = Ui.Button(I18n.T("mod.disable"), () =>
                {
                    try { _g.Registry!.SetEnabled(id, false); MainWindow.Current?.Toast(I18n.T("toast.disabled")); } catch (Exception e) { MainWindow.Current?.Toast(Jobs.Explain(e), bad: true); }
                    Build();
                });
                DockPanel.SetDock(off, Dock.Right);
                row.Children.Add(off);
            }
            row.Children.Add(Ui.Col(4, Ui.Text(issue.Mod, "h3", color: Ui.Res("Bad")), new SelectableTextBlock { Text = issue.Message, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), FontSize = 12, MaxHeight = 60 }));
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row });
        }
        return Ui.Card(col, 22);
    }
}
