using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Minecraft;

namespace ModLaunch.Views;

/// <summary>Вкладка «Настройки»: версия и загрузчик сборки, память и Java, лаунчер и папка .minecraft.</summary>
public sealed partial class MinecraftPage
{
    Control SettingsView()
    {
        var col = new StackPanel { Spacing = 16 };
        var a = Mc.Active;
        if (a is not null)
        {
            col.Children.Add(VersionCard(a));
            col.Children.Add(MemoryCard(a));
        }
        col.Children.Add(LauncherCard());
        if (a is not null) col.Children.Add(DangerCard(a));
        return col;
    }

    static Control Section(string title, string text, params Control[] rows)
    {
        var col = Ui.Col(14, Ui.Col(4, Ui.Text(title, "h2"), Ui.Text(text, "small muted", wrap: true)));
        foreach (var r in rows) col.Children.Add(r);
        return Ui.Card(col, 24);
    }

    /// <summary>Версия игры и загрузчик. Смена — новая установка в лаунчере; моды старой версии могут не подойти.</summary>
    Control VersionCard(McInstance a)
    {
        var loader = a.Loader;
        List<McVersion> versions = [];
        List<(string Version, bool Stable)> loaderVersions = [];
        var ticket = 0;
        var version = new ComboBox { Width = 240, MaxDropDownHeight = 420, PlaceholderText = a.GameVersion };
        var snapshots = new CheckBox { Content = I18n.T("mine.create.snapshots"), VerticalAlignment = VerticalAlignment.Center };
        var loaderVersion = new ComboBox { Width = 240, MaxDropDownHeight = 360, PlaceholderText = a.LoaderVersion == "" ? "—" : a.LoaderVersion };
        var loaders = Ui.Row(8);
        var note = Ui.Text("", "small muted", wrap: true);
        var save = Ui.Button(I18n.T("mine.settings.apply"), () => { }, "primary", Icons.Check);
        save.IsEnabled = false;

        string? Game() => version.SelectedIndex >= 0 && version.SelectedIndex < versions.Count ? versions[version.SelectedIndex].Id : null;
        string Lv() => loader == "vanilla" || loaderVersion.SelectedIndex < 0 || loaderVersion.SelectedIndex >= loaderVersions.Count ? "" : loaderVersions[loaderVersion.SelectedIndex].Version;
        void Dirty() => save.IsEnabled = Game() is { } gv && (gv != a.GameVersion || loader != a.Loader || (loader != "vanilla" && Lv() != "" && Lv() != a.LoaderVersion));

        void RenderLoaders()
        {
            loaders.Children.Clear();
            foreach (var x in Mc.Loaders)
            {
                var id = x;
                var chip = new Button { Classes = { "chip" }, Content = Ui.Row(8, LoaderBadge(x, 18), new TextBlock { Text = x == "vanilla" ? I18n.T("mine.loader.vanilla") : Mc.LoaderTitle(x), VerticalAlignment = VerticalAlignment.Center }) };
                if (x == loader) chip.Classes.Add("active");
                chip.Click += (_, _) => { loader = id; RenderLoaders(); _ = LoadLoaders(); };
                loaders.Children.Add(chip);
            }
        }

        async Task LoadVersions()
        {
            var my = ++ticket;
            try
            {
                var list = await McMeta.GameVersions(snapshots.IsChecked == true || !McVersionIsRelease(a.GameVersion));
                if (my != ticket) return;
                versions = list;
                version.ItemsSource = list.Select(v => v.Release ? v.Id : $"{v.Id}  ·  {I18n.T("mine.create.snapshot")}").ToList();
                version.SelectedIndex = Math.Max(0, list.FindIndex(v => v.Id == a.GameVersion));
            }
            catch (Exception e) { note.Text = I18n.T("mine.err.offline", ("reason", Jobs.Explain(e))); }
            await LoadLoaders();
        }

        async Task LoadLoaders()
        {
            var my = ++ticket;
            loaderVersion.ItemsSource = null;
            loaderVersions = [];
            if (Game() is not { } gv) return;
            note.Text = gv != a.GameVersion && McContent.Count(a, "mod") > 0 ? I18n.T("mine.settings.versionWarn") : "";
            if (loader == "vanilla")
            {
                loaderVersion.ItemsSource = new[] { I18n.T("mine.create.noLoader") };
                loaderVersion.SelectedIndex = 0;
                loaderVersion.IsEnabled = false;
                Dirty();
                return;
            }
            loaderVersion.IsEnabled = true;
            try
            {
                var list = await McMeta.LoaderVersions(loader, gv);
                if (my != ticket) return;
                loaderVersions = list;
                if (list.Count == 0)
                {
                    loaderVersion.ItemsSource = new[] { "—" };
                    loaderVersion.SelectedIndex = 0;
                    loaderVersion.IsEnabled = false;
                    note.Text = I18n.T("mine.err.noLoader", ("loader", Mc.LoaderTitle(loader)), ("version", gv));
                }
                else
                {
                    loaderVersion.ItemsSource = list.Select(x => x.Stable ? x.Version : $"{x.Version}  ·  beta").ToList();
                    var current = loader == a.Loader && gv == a.GameVersion ? list.FindIndex(x => x.Version == a.LoaderVersion) : -1;
                    loaderVersion.SelectedIndex = current >= 0 ? current : Math.Max(0, list.FindIndex(x => x.Stable));
                }
            }
            catch (Exception e) { note.Text = I18n.T("mine.err.offline", ("reason", Jobs.Explain(e))); }
            Dirty();
        }

        version.SelectionChanged += (_, _) => { if (version.SelectedIndex >= 0) _ = LoadLoaders(); };
        loaderVersion.SelectionChanged += (_, _) => Dirty();
        snapshots.IsCheckedChanged += (_, _) => _ = LoadVersions();
        save.Click += (_, _) =>
        {
            if (Game() is not { } gv) return;
            var lv = Lv();
            if (loader != "vanilla" && lv == "") return;
            a.GameVersion = gv;
            a.Loader = loader;
            a.LoaderVersion = lv;
            Mc.Save(a);
            try { McProfiles.Sync(a); } catch { }
            McContent.ForgetAllUpdates(a);
            W.Toast(I18n.T("mine.settings.saved", ("label", a.Label)));
            if (a.Modded && !Program.Demo && !Mc.VersionReady(a))
                Jobs.Run($"{a.LoaderTitle} {a.GameVersion}", "Minecraft", async (_, progress, ct) => { await McInstall.EnsureVersion(a, progress, ct); McProfiles.Sync(a); });
            if (!Program.Demo) _ = Refresh(a, force: true);
            Mc.Notify();
        };
        RenderLoaders();
        if (Shown || Program.Screenshot) _ = LoadVersions();
        else Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadVersions());

        var ready = Mc.VersionReady(a);
        var status = Ui.Row(8, Ui.Dot(ready ? Ui.Res("Good") : Ui.Res("Warn")),
            Ui.Text(ready ? I18n.T("mine.settings.ready", ("id", a.VersionId)) : I18n.T("mine.settings.notReady", ("loader", a.LoaderTitle)), "small"));
        return Section(I18n.T("mine.settings.version"), I18n.T("mine.settings.version.text"),
            Field(I18n.T("mine.create.version"), Ui.Row(14, version, snapshots)),
            Field(I18n.T("mine.create.loader"), loaders),
            Field(I18n.T("mine.create.loaderVersion"), loaderVersion),
            note,
            Ui.Row(14, save, status));
    }

    static bool McVersionIsRelease(string id) => !id.Contains("snapshot", StringComparison.OrdinalIgnoreCase) && !id.Contains("-pre", StringComparison.OrdinalIgnoreCase) && !id.Contains("-rc", StringComparison.OrdinalIgnoreCase) && !System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d\dw\d\d[a-z]$");

    Control MemoryCard(McInstance a)
    {
        var maxGb = Math.Max(2, Math.Min(32, Mc.TotalMemoryMb() / 1024 - 2));
        var memory = new Slider { Minimum = 1, Maximum = maxGb, Value = Math.Min(maxGb, a.MemoryMb / 1024.0), TickFrequency = 0.5, IsSnapToTickEnabled = true, Width = 360, VerticalAlignment = VerticalAlignment.Center };
        var memText = Ui.Text("", "small");
        memText.VerticalAlignment = VerticalAlignment.Center;
        void Show() => memText.Text = I18n.T("mine.memory.value", ("gb", memory.Value.ToString("0.#", I18n.Culture)));
        Show();
        memory.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Show(); };
        var args = new TextBox { Text = a.JavaArgs, Watermark = "-Dfile.encoding=UTF-8", Width = 560 };
        var save = Ui.Button(I18n.T("common.save"), () =>
        {
            a.MemoryMb = (int)(memory.Value * 1024);
            a.JavaArgs = args.Text?.Trim() ?? "";
            Mc.Save(a);
            try { McProfiles.Sync(a); } catch { }
            W.Toast(I18n.T("mine.settings.memorySaved"));
        }, "primary", Icons.Save);
        var hint = Ui.Text(I18n.T("mine.memory.hint", ("total", (Mc.TotalMemoryMb() / 1024.0).ToString("0", I18n.Culture))), "small muted", wrap: true);
        return Section(I18n.T("mine.settings.java"), I18n.T("mine.settings.java.text"),
            Field(I18n.T("mine.memory"), Ui.Row(14, memory, memText)),
            hint,
            Field(I18n.T("mine.settings.args"), args),
            Ui.Row(10, save, Ui.Button(I18n.T("mine.settings.reset"), () => { memory.Value = Math.Min(maxGb, Mc.DefaultMemory() / 1024.0); args.Text = ""; }, "ghost", Icons.Undo)));
    }

    Control LauncherCard()
    {
        var launcher = Program.Demo ? new Mc.LauncherInfo("store", null) : Mc.FindLauncher();
        var (dot, text) = launcher switch
        {
            null => (Ui.Res("Warn"), I18n.T("mine.launcher.none")),
            { Kind: "classic" } => (Ui.Res("Good"), I18n.T("mine.launcher.classic")),
            _ => (Ui.Res("Good"), I18n.T("mine.launcher.store")),
        };
        var rows = new List<Control>
        {
            Ui.Row(8, Ui.Dot(dot), Ui.Text(text, "small")),
            Field(I18n.T("mine.settings.root"), Ui.Row(10,
                new TextBox { Text = Mc.Root, IsReadOnly = true, Width = 460 },
                Ui.Button(I18n.T("games.setPath"), () => Actions.PickGameFolder(G), "", Icons.Folder),
                Ui.Button("", () => Actions.OpenFolder(Mc.Root), "icon ghost", Icons.External, I18n.T("games.openFolder")))),
        };
        var buttons = Ui.Row(10);
        if (launcher is null) buttons.Children.Add(Ui.Button(I18n.T("mine.notFound.download"), () => Ui.OpenUrl("https://www.minecraft.net/download"), "primary", Icons.Download));
        buttons.Children.Add(Ui.Button(I18n.T("mine.launcher.sync"), () =>
        {
            var n = 0;
            foreach (var i in Mc.Instances()) { try { McProfiles.Sync(i); n++; } catch { } }
            W.Toast(I18n.T("mine.launcher.synced", ("n", n)));
        }, "", Icons.Refresh));
        rows.Add(buttons);
        return Section(I18n.T("mine.settings.launcher"), I18n.T("mine.settings.launcher.text"), rows.ToArray());
    }

    Control DangerCard(McInstance a) => Section(I18n.T("mine.settings.build"), a.Linked ? I18n.T("mine.aside.linked", ("source", McImport.SourceTitle(a.Source ?? ""))) : I18n.T("mine.aside.own"),
        Field(I18n.T("mine.settings.dir"), Ui.Row(10, new TextBox { Text = a.Dir, IsReadOnly = true, Width = 460 }, Ui.Button("", () => Actions.OpenFolder(a.Dir), "icon ghost", Icons.Folder, I18n.T("games.openFolder")))),
        Ui.Row(10,
            Ui.Button(I18n.T("mine.build.rename"), () => RenameDialog(a), "", Icons.Edit),
            Ui.Button(I18n.T("mine.build.duplicate"), () => DuplicateDialog(a), "", Icons.Copy),
            Ui.Button(a.Linked ? I18n.T("mine.build.unlink") : I18n.T("mine.build.delete"), () => DeleteDialog(a), "ghost", Icons.Trash)));
}
