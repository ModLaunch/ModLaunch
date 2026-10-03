using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Окно «Обновления модов», как в Prism и Modrinth App: у каждого мода галочка,
/// старая → новая версия, «Что нового» и «Не обновлять». Кнопка ставит отмеченное
/// одной задачей на игру.
/// </summary>
public static class UpdateReview
{
    static MainWindow W => MainWindow.Current!;

    public static void Show(IReadOnlyList<(GameState Game, ModUpdate Update)> items)
    {
        if (items.Count == 0) return;
        var rows = items.Select(x => new Item(x.Game, x.Update) { On = !x.Update.Manual }).ToList();
        var manyGames = rows.Select(r => r.Game.Def.Id).Distinct().Count() > 1;

        var list = Ui.Col(6);
        Button? go = null;
        var all = new CheckBox { Content = I18n.T("upd.review.all"), IsChecked = rows.All(r => r.On) };

        void Refresh()
        {
            var n = rows.Count(r => r.On);
            if (go is not null)
            {
                go.Content = Ui.Row(8, Ui.Icon(Icons.ArrowUp, 16), new TextBlock { Text = I18n.T("upd.review.go", ("n", n)), VerticalAlignment = VerticalAlignment.Center });
                go.IsEnabled = n > 0;
            }
            all.IsChecked = rows.All(r => r.On) ? true : rows.Any(r => r.On) ? null : false;
        }

        void Fill()
        {
            list.Children.Clear();
            foreach (var r in rows) list.Children.Add(Row(r, manyGames, Refresh, () => { rows.Remove(r); if (rows.Count == 0) W.CloseDialog(); else { Fill(); Refresh(); } }));
        }

        all.IsCheckedChanged += (_, _) =>
        {
            if (all.IsChecked is null) return;
            var on = all.IsChecked == true;
            if (rows.All(r => r.On == on)) return;
            foreach (var r in rows) r.On = on;
            Fill();
            Refresh();
        };
        Fill();

        var body = Ui.Col(12,
            Ui.Text(I18n.T("upd.review.text"), "muted", wrap: true),
            all,
            new ScrollViewer { Content = list, MaxHeight = 380, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        go = Ui.Button("", () =>
        {
            var chosen = rows.Where(r => r.On).ToList();
            W.CloseDialog();
            foreach (var game in chosen.GroupBy(r => r.Game)) _ = Actions.UpdateMods(game.Key, game.Select(r => r.Update).ToList());
            W.Toast(I18n.T("upd.review.started." + I18n.Plural(chosen.Count, "one", "few", "many"), ("n", chosen.Count)));
        }, "primary");
        Refresh();
        W.Dialog(I18n.T("upd.review.title." + I18n.Plural(rows.Count, "one", "few", "many"), ("n", rows.Count)), body, 640,
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog), go);
    }

    sealed class Item(GameState game, ModUpdate update)
    {
        public GameState Game { get; } = game;
        public ModUpdate Update { get; } = update;
        public bool On { get; set; }
        public Control? Notes { get; set; }
    }

    static Control Row(Item item, bool manyGames, Action changed, Action removed)
    {
        var u = item.Update;
        var check = new CheckBox { IsChecked = item.On, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        check.IsCheckedChanged += (_, _) => { item.On = check.IsChecked == true; changed(); };

        var sub = new List<string>();
        if (manyGames) sub.Add(item.Game.Def.ShortName);
        sub.Add($"{Clean(u.Current)} → {Clean(u.Latest)}");
        var title = Ui.Row(8, new TextBlock { Text = u.Name, Classes = { "h3" }, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
        if (u.Manual) title.Children.Add(ModRow.Tag(I18n.T("upd.viaBrowser"), Ui.Res("Surface3"), Ui.Res("Muted")));
        var middle = Ui.Col(2, title, Ui.Text(string.Join(" · ", sub), "small muted"));
        middle.VerticalAlignment = VerticalAlignment.Center;

        var notes = new StackPanel { Spacing = 4, IsVisible = false, Margin = new Thickness(88, 6, 8, 6) };
        var whatsNew = Ui.Button(I18n.T("upd.review.notes"), () =>
        {
            notes.IsVisible = !notes.IsVisible;
            if (notes.IsVisible && notes.Children.Count == 0) _ = LoadNotes(item, notes);
        }, "ghost");
        var hold = Ui.Button("", () =>
        {
            ModUpdates.Hold(item.Game, u.RecordId, true);
            W.Toast(I18n.T("upd.held.toast", ("name", u.Name)));
            AppState.Notify();
            removed();
        }, "icon ghost", Icons.Lock, I18n.T("upd.hold.tip"));
        var right = Ui.Row(2, whatsNew, hold);
        right.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(check);
        var thumb = Ui.Thumb(u.Icon, u.Name, 36, 9);
        Grid.SetColumn(thumb, 1);
        grid.Children.Add(thumb);
        Grid.SetColumn(middle, 2);
        grid.Children.Add(middle);
        Grid.SetColumn(right, 3);
        grid.Children.Add(right);

        return new Border
        {
            Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8),
            Child = Ui.Col(0, grid, notes),
        };
    }

    static string Clean(string version) => version.TrimStart('v', 'V');

    /// <summary>Что изменилось между установленной и новой версией.</summary>
    static async Task LoadNotes(Item item, StackPanel box)
    {
        var u = item.Update;
        box.Children.Add(Ui.Text(I18n.T("common.loading"), "small muted"));
        List<(string Head, List<string> Lines)> notes;
        try { notes = await Notes(item); }
        catch (Exception e) { notes = []; box.Children.Clear(); box.Children.Add(Ui.Text(Jobs.Explain(e), "small muted", wrap: true)); return; }
        box.Children.Clear();
        if (notes.Count == 0 || notes.All(n => n.Lines.Count == 0))
        {
            box.Children.Add(Ui.Text(I18n.T("upd.review.noNotes"), "small muted", wrap: true));
            return;
        }
        foreach (var (head, lines) in notes.Take(5))
        {
            box.Children.Add(Ui.Text(head, "small", color: Ui.Res("Text")));
            foreach (var line in lines.Take(8)) box.Children.Add(Ui.Text("•  " + line, "small muted", wrap: true));
        }
        if (item.Game.Def.SourceOf(u.RecordId, u.Source) is { } source && item.Game.Registry?.Get(u.RecordId)?.Str("url") is string url)
        {
            var more = Ui.Button(I18n.T("upd.review.page", ("source", Catalog.Title(source))), () => Ui.OpenUrl(url), "ghost", Icons.External);
            more.HorizontalAlignment = HorizontalAlignment.Left;
            more.Margin = new Thickness(-12, 2, 0, 0);
            box.Children.Add(more);
        }
    }

    /// <summary>Список изменений по версиям новее установленной (без сети в показе — придуманный).</summary>
    static async Task<List<(string Head, List<string> Lines)>> Notes(Item item)
    {
        var u = item.Update;
        if (Program.Demo) return DemoNotes(u);
        var game = item.Game.Def;
        var mod = new ModInfo { Source = u.Source, Id = u.CatalogId, Name = u.Name, Version = u.Latest };
        var versions = (await Extras.Versions(game, mod)).Where(v => Versions.IsNewer(v.Version, u.Current)).ToList();
        static List<string> Lines(string text) => text.Split('\n').Select(l => l.Trim().TrimStart('-', '*', '•').Trim()).Where(l => l != "").ToList();
        if (versions.Any(v => v.Changelog.Trim() != ""))
            return versions.Select(v => (v.Date is null ? v.Version : $"{v.Version} · {v.Date!.Value.ToString("d", I18n.Culture)}", Lines(v.Changelog))).ToList();
        // У Thunderstore изменения — одним файлом CHANGELOG: берём начало (там самое новое).
        var blocks = await Extras.Changelog(game, mod, versions);
        if (blocks.Count > 0)
        {
            var result = new List<(string, List<string>)>();
            foreach (var b in blocks.Take(24))
            {
                if (b.Kind == "h") result.Add((b.Text, []));
                else if (result.Count == 0) result.Add((u.Latest, [b.Text]));
                else result[^1].Item2.Add(b.Text);
            }
            return result;
        }
        return versions.Select(v => (v.Date is null ? v.Version : $"{v.Version} · {v.Date!.Value.ToString("d", I18n.Culture)}", new List<string>())).ToList();
    }

    /// <summary>Снимки: обновления на странице игры, окно со списком, загрузки, центр модов, настройки.</summary>
    [DemoShots]
    static void UpdatesShots(Shots s)
    {
        var w = s.Window;
        var sub = AppState.Game("subnautica");
        var lc = AppState.Game("lethal-company");
        var lcReg = lc.Registry!;
        foreach (var (id, name, version) in new[] { ("Evaisa-LethalLib", "LethalLib", "0.16.1"), ("notnotnotswipez-MoreCompany", "MoreCompany", "1.10.1"), ("x753-More_Suits", "More Suits", "1.4.3") })
        {
            Directory.CreateDirectory(Path.Combine(lcReg.ModsDir, name));
            lcReg.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["name"] = name, ["folder"] = name, ["version"] = version, ["source"] = "thunderstore", ["author"] = id.Split('-')[0] });
        }
        sub.Registry!.SetHold("nexus:subnautica:142", true);
        ModUpdates.Found["subnautica"] =
        [
            new("nexus:subnautica:12", "12", "Map Mod", "2.1.23", "2.3.1", null, true, "nexus"),
            new("nexus:subnautica:1262", "1262", "Nautilus", "2.1.24", "2.2.0", null, false, "nexus"),
        ];
        ModUpdates.Found["lethal-company"] =
        [
            new("Evaisa-LethalLib", "Evaisa-LethalLib", "LethalLib", "0.16.1", "1.0.1", null, false, "thunderstore"),
            new("notnotnotswipez-MoreCompany", "notnotnotswipez-MoreCompany", "MoreCompany", "1.10.1", "1.11.0", null, false, "thunderstore"),
        ];
        AppState.Notify();

        w.Navigate(() => new GamePage("subnautica", "installed"));
        s.Save("updates-1-installed");

        Show([.. ModUpdates.Found["subnautica"].Select(u => (sub, u)), .. ModUpdates.Found["lethal-company"].Select(u => (lc, u))]);
        s.Pump(300);
        var notes = w.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == I18n.T("upd.review.notes"));
        notes?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        s.Save("updates-2-review");
        w.CloseDialog();

        // Загрузки: идёт с байтами и скоростью, ждёт, на паузе, ошибка, готово.
        var demo = new List<Job>
        {
            new() { Title = "Nautilus", GameName = "Subnautica", Status = JobStatus.Running, Step = I18n.T("dl.download"), Received = 12_900_000, Total = 41_200_000, Speed = 2_300_000, Ratio = 0.31 },
            new() { Title = I18n.T("upd.job", ("n", 2)), GameName = "Lethal Company", Status = JobStatus.Running, Step = I18n.T("dl.extract") + " " + I18n.T("dl.detail.of", ("i", 1), ("n", 2), ("name", "LethalLib")), Ratio = -1 },
            new() { Title = "More Suits", GameName = "Lethal Company", Status = JobStatus.Queued },
            new() { Title = "Slot Extender", GameName = "Subnautica", Status = JobStatus.Canceled, Paused = true, Step = I18n.T("dl.paused") },
            new() { Title = "Vehicle Framework", GameName = "Subnautica", Status = JobStatus.Failed, Step = I18n.T("dl.failed"), Error = I18n.T("err.nexus.offline", ("reason", "timeout")) },
            new() { Title = "BepInEx", GameName = "Subnautica", Status = JobStatus.Done, Step = I18n.T("dl.done"), Ratio = 1 },
        };
        foreach (var job in demo.AsEnumerable().Reverse()) Jobs.All.Insert(0, job);
        w.Navigate(() => new GamePage("subnautica", "installed"));
        w.OpenDownloads();
        s.Save("updates-3-downloads");
        foreach (var job in demo) Jobs.All.Remove(job);
        w.OpenDownloads();
        s.Save("updates-3b-downloads-empty");

        w.Navigate(() => new ModsCenterPage("updates"));
        s.Save("updates-4-center");
        w.Navigate(() => new SettingsPage("updates"));
        s.Save("updates-5-settings");
        w.Navigate(() => new SettingsPage("downloads"));
        s.Save("updates-6-settings-downloads");
        ModUpdates.Found.Clear();
        AppState.Notify();
    }

    static List<(string, List<string>)> DemoNotes(ModUpdate u) =>
    [
        ($"{Clean(u.Latest)} · {DateTime.Now.AddDays(-2).ToString("d", I18n.Culture)}", ["Работает с последним обновлением игры", "Исправлен вылет при загрузке сохранения", "Настройки теперь меняются прямо в игре"]),
        ($"{Clean(u.Current)}.1 · {DateTime.Now.AddDays(-19).ToString("d", I18n.Culture)}", ["Меньше нагрузка на процессор в больших базах"]),
    ];
}
