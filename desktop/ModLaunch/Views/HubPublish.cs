using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>Окно публикации в ModLaunch Hub: карточка мода, версия, файлы.</summary>
public static class HubPublish
{
    /// <param name="fromProject">Мод из мастерской: можно выложить исходник или собранный пакет.</param>
    /// <param name="pack">Собрать пакет проекта (для варианта «готовый пакет»).</param>
    public static void Show(HubDraft d, bool fromProject, Func<Task<string?>>? pack, Action? done = null, bool lockName = false)
    {
        var w = MainWindow.Current!;
        if (!Social.Account.SignedIn && !Program.Screenshot)
        {
            w.Dialog(I18n.T("cr.publish"), Ui.Text(I18n.T("cr.publish.signin"), "muted", wrap: true),
                Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
                Ui.Button(I18n.T("acc.title"), () => { w.CloseDialog(); w.Navigate(() => new SettingsPage("accounts")); }, "primary", Icons.User));
            return;
        }

        TextBox Box(string value, string hint, bool multi = false, int max = 200) => new()
        {
            Text = value, Watermark = hint, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multi ? 90 : 0, MaxLength = max, VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        Control Field(string label, Control input) => Ui.Col(5, Ui.Text(label, "small muted"), input);

        var name = Box(d.Name, I18n.T("hub.f.name"), max: 60);
        name.IsEnabled = !lockName;
        var summary = Box(d.Summary, I18n.T("hub.f.summary"), max: 200);
        var description = Box(d.Description, I18n.T("hub.f.description"), multi: true, max: 5000);
        var version = Box(d.Version, "1.0.0", max: 20);
        version.Width = 120;
        var changelog = Box(d.Changelog, I18n.T("hub.f.changelog"), multi: true, max: 2000);
        var images = Box(string.Join("\n", d.Images), I18n.T("hub.f.images"), multi: true, max: 2400);

        var game = new ComboBox { Width = 260 };
        var games = AppState.Games.Select(g => g.Def.Id).ToList();
        foreach (var g in AppState.Games) game.Items.Add(g.Def.Name);
        game.SelectedIndex = Math.Max(0, games.IndexOf(d.Game));
        game.IsEnabled = !fromProject;

        var chosen = new HashSet<string>(d.Tags);
        var tags = new WrapPanel();
        foreach (var tag in Hub.Tags)
        {
            var t = tag;
            var chip = Ui.Button("#" + I18n.T("hub.tag." + tag), () => { }, "chip");
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Padding = new Thickness(10, 4);
            chip.FontSize = 12;
            if (chosen.Contains(tag)) chip.Classes.Add("active");
            chip.Click += (_, _) =>
            {
                if (!chosen.Remove(t) && chosen.Count < 6) chosen.Add(t);
                chip.Classes.Set("active", chosen.Contains(t));
            };
            tags.Children.Add(chip);
        }

        // Что выкладываем: исходник, собранный пакет проекта или свой архив.
        var archive = d.Archive;
        var kind = fromProject ? "script" : "file";
        var file = Ui.Text(archive is null ? I18n.T("hub.f.noFile") : Path.GetFileName(archive), "small muted");
        var pick = Ui.Button(I18n.T("hub.f.pick"), async () =>
        {
            var path = await w.PickFile(I18n.T("hub.f.pick"));
            if (path is null) return;
            if (new FileInfo(path).Length > Hub.MaxFile) { w.Toast(I18n.T("err.creator.FILE_TOO_BIG"), bad: true); return; }
            archive = path;
            file.Text = $"{Path.GetFileName(path)} · {GamePage.Size(new FileInfo(path).Length)}";
            if (name.Text is null or "") name.Text = Path.GetFileNameWithoutExtension(path);
        }, "", Icons.FilePlus);
        Control what;
        if (fromProject)
        {
            var script = new RadioButton { Content = I18n.T("hub.f.kind.script"), GroupName = "kind", IsChecked = true };
            var package = new RadioButton { Content = I18n.T("hub.f.kind.package"), GroupName = "kind" };
            script.IsCheckedChanged += (_, _) => { if (script.IsChecked == true) kind = "script"; };
            package.IsCheckedChanged += (_, _) => { if (package.IsChecked == true) kind = "package"; };
            what = Ui.Col(4, script, package, Ui.Text(I18n.T("hub.f.kind.hint"), "small muted", wrap: true));
        }
        else what = Ui.Row(10, pick, file);

        var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false };
        var status = Ui.Text("", "small muted", wrap: true);

        var form = Ui.Col(12,
            Field(I18n.T("hub.f.name.label"), name),
            Field(I18n.T("hub.f.summary.label"), summary),
            Ui.Row(12, Field(I18n.T("cr.r.game"), game), Field(I18n.T("hub.f.version"), version)),
            Field(I18n.T("hub.f.what"), what),
            Field(I18n.T("hub.f.description.label"), description),
            Field(I18n.T("hub.f.changelog.label"), changelog),
            Field(I18n.T("hub.f.tags"), tags),
            Field(I18n.T("hub.f.images.label"), images),
            new Border
            {
                Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12),
                Child = Ui.Row(10, Ui.Icon(Icons.Shield, 16, Ui.Res("Muted")), Ui.Text(I18n.T("hub.f.rules"), "small muted", wrap: true)),
            },
            bar, status);
        var scroller = new ScrollViewer { Content = form, MaxHeight = 520, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        Button? go = null;
        go = Ui.Button(I18n.T("cr.publish"), async () =>
        {
            d.Name = name.Text ?? "";
            d.Summary = summary.Text ?? "";
            d.Description = description.Text ?? "";
            d.Version = (version.Text ?? "").Trim();
            d.Changelog = changelog.Text ?? "";
            d.Game = games[Math.Max(0, game.SelectedIndex)];
            d.Tags = [.. chosen];
            d.Images = (images.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (d.Images.Any(u => !u.StartsWith("https://"))) { status.Text = I18n.T("hub.f.imagesBad"); return; }
            go!.IsEnabled = false;
            bar.IsVisible = true;
            try
            {
                d.Archive = kind switch
                {
                    "file" => archive ?? throw new Social.ServiceError("NO_FILE"),
                    "package" => pack is null ? null : await pack(),
                    _ => null,
                };
                if (kind == "file") d.Code = "";
                status.Text = I18n.T("hub.f.uploading");
                await Hub.Publish(d, new Progress<double>(r => bar.Value = r));
                w.CloseDialog();
                w.Toast(I18n.T("cr.publish.done", ("name", d.Name)));
                done?.Invoke();
            }
            catch (Exception e)
            {
                status.Text = Social.Firebase.Explain("creator", e);
                go.IsEnabled = true;
                bar.IsVisible = false;
            }
        }, "primary", Icons.Upload);
        w.Dialog(I18n.T(lockName ? "hub.newVersion" : "hub.publish.title"), scroller, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
    }
}
