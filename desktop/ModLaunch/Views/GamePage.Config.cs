using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Вкладка «Настройки» (8.4): готовые наборы в один щелчок, свои наборы (сохранить, применить,
/// поделиться файлом) и полный редактор настроек всех модов — поиск по всем файлам сразу,
/// «только изменённые», ползунки для чисел с диапазоном, сброс по одной настройке и целиком.
/// </summary>
public sealed partial class GamePage
{
    string? _cfgFile;
    string _cfgFilter = "";
    bool _cfgOnlyChanged;

    Control ConfigView()
    {
        var files = CfgFile.Files(_g.Registry!);
        var col = Ui.Col(18);
        if (files.Count == 0)
        {
            var empty = Ui.Col(10,
                Ui.Icon(Icons.Sliders, 30, Ui.Res("Faint")),
                Ui.Text(I18n.T("cfg.none"), "h2"),
                Ui.Text(I18n.T(_g.Def.Loader switch
                {
                    Games.LoaderKind.Smapi => "cfg.none.smapi",
                    Games.LoaderKind.HkApi => "cfg.none.hk",
                    _ => "cfg.none.text",
                }), "muted", wrap: true));
            ((Control)empty.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
            if (_g.LoaderInstalled && _g.ModCount > 0) empty.Children.Add(Ui.Row(10, PlayControls.PlayButton(_g)));
            col.Children.Add(Ui.Card(empty, 26));
            col.Children.Add(MinePresetsCard());
            return col;
        }

        col.Children.Add(PresetsCard());
        col.Children.Add(MinePresetsCard());
        col.Children.Add(EditorCard(files));
        return col;
    }

    // ---------------------------------------------------------------- готовые наборы

    static string PresetIcon(string icon) => icon switch
    {
        "star" => Icons.Star,
        "zap" => Icons.Zap,
        "bug" => Icons.Bug,
        "undo" => Icons.Undo,
        _ => Icons.Save,
    };

    Control PresetsCard()
    {
        var last = CfgPresets.Last(_g.Def.Id);
        var head = new DockPanel();
        if (CfgPresets.CanUndo(_g.Def.Id))
        {
            var undo = Ui.Button(I18n.T("cfgp.undo"), UndoPreset, "", Icons.Undo);
            DockPanel.SetDock(undo, Dock.Right);
            head.Children.Add(undo);
        }
        head.Children.Add(Ui.Col(4, Ui.Text(I18n.T("cfgp.title"), "h2"), Ui.Text(I18n.T("cfgp.hint"), "muted", wrap: true)));

        var grid = new UniformGrid { Columns = 4 };
        var presets = CfgPresets.BuiltIn(_g.Def);
        grid.Columns = presets.Count;
        foreach (var p in presets)
        {
            var preset = p;
            var on = last == p.Id;
            var badge = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left,
                Background = on ? Ui.Res("Brand") : Ui.Res("Surface3"),
                Child = Ui.Icon(PresetIcon(p.Icon), 19, on ? Brushes.White : Ui.Res("Text")),
            };
            var title = Ui.Row(8, Ui.Text(p.Title, "h3"));
            if (on) title.Children.Add(new Border { Classes = { "tab-badge", "good" }, Child = Ui.Text(I18n.T("cfgp.applied"), "tiny") });
            var body = Ui.Col(10, badge, Ui.Col(4, title, Ui.Text(p.Text, "small muted", wrap: true)));
            var b = new Button { Classes = { "preset" }, Content = body, Margin = new Thickness(0, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
            if (on) b.Classes.Add("on");
            b.Click += (_, _) => ConfirmPreset(preset);
            grid.Children.Add(b);
        }
        return Ui.Card(Ui.Col(16, head, grid), 22);
    }

    void ConfirmPreset(CfgPreset preset)
    {
        var w = MainWindow.Current!;
        var plan = CfgPresets.Plan(_g.Registry!, preset.Id);
        var resets = preset.Id == CfgPresets.Defaults ? CfgPresets.ResetFiles(_g.Registry!) : [];
        var total = plan.Values.Sum(v => v.Count);
        if (total == 0 && resets.Count == 0) { w.Toast(I18n.T("cfgp.same", ("name", preset.Title))); return; }

        var rows = Ui.Col(6);
        foreach (var (file, changes) in plan)
            foreach (var (entry, value) in changes.Take(40))
                rows.Children.Add(ChangeRow(CfgFile.Label(file), entry.Title ?? entry.Key, entry.Value, value));
        foreach (var file in resets) rows.Children.Add(ChangeRow(CfgFile.Label(file), I18n.T("cfgp.reset.file"), "", I18n.T("cfgp.reset.new")));
        var summary = I18n.T("cfgp.confirm.text", ("n", total + resets.Count), ("files", plan.Count + resets.Count));
        w.Dialog(I18n.T("cfgp.confirm", ("name", preset.Title)),
            Ui.Col(12, Ui.Text(summary, "muted", wrap: true), new ScrollViewer { Content = rows, MaxHeight = 340 }, Ui.Text(I18n.T("cfgp.confirm.undo"), "small muted", wrap: true)),
            620,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("cfgp.apply"), () =>
            {
                w.CloseDialog();
                try
                {
                    if (Features.PlayTime.IsRunning(_g.Def.Id)) w.Toast(I18n.T("cfgp.running"));
                    var (values, count) = CfgPresets.Apply(_g, preset.Id);
                    w.Toast(I18n.T("cfgp.done", ("name", preset.Title), ("n", values), ("files", count)));
                }
                catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
                Build();
            }, "primary", Icons.Check));
    }

    static Control ChangeRow(string file, string key, string from, string to)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(Ui.Col(1, Ui.Text(key, "small"), Ui.Text(file, "tiny muted")));
        var change = Ui.Row(6);
        if (from != "") change.Children.Add(Ui.Text(Short(from), "small muted"));
        if (from != "") change.Children.Add(Ui.Icon(Icons.Forward, 12, Ui.Res("Faint")));
        change.Children.Add(Ui.Text(Short(to), "small", color: Ui.Res("Good")));
        change.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(change, 1);
        grid.Children.Add(change);
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8), Child = grid };

        static string Short(string s) => s.Length > 34 ? s[..33] + "…" : s;
    }

    void UndoPreset()
    {
        var w = MainWindow.Current!;
        try { var n = CfgPresets.Undo(_g); w.Toast(I18n.T("cfgp.undone", ("n", n))); }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
        Build();
    }

    // ---------------------------------------------------------------- свои наборы

    Control MinePresetsCard()
    {
        var w = MainWindow.Current;
        var name = new TextBox { Watermark = I18n.T("cfgp.mine.placeholder"), Height = 42 };
        void Save()
        {
            try { var p = CfgPresets.Save(_g, name.Text ?? ""); w?.Toast(I18n.T("cfgp.saved", ("name", p.Title))); Build(); }
            catch (Exception e) { w?.Toast(e.Message, bad: true); }
        }
        name.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Save(); };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10 };
        bar.Children.Add(name);
        var save = Ui.Button(I18n.T("cfgp.mine.save"), Save, "primary", Icons.Save);
        save.IsEnabled = CfgFile.Files(_g.Registry!).Count > 0 || Features.ModSetup.ConfigFiles(_g.Registry!).Count > 0;
        Grid.SetColumn(save, 1);
        bar.Children.Add(save);
        var import = Ui.Button(I18n.T("cfgp.mine.import"), ImportPreset, "", Icons.Download);
        Grid.SetColumn(import, 2);
        bar.Children.Add(import);

        var list = Ui.Col(8);
        var mine = CfgPresets.Mine(_g.Def.Id);
        var last = CfgPresets.Last(_g.Def.Id);
        if (mine.Count == 0) list.Children.Add(Ui.Text(I18n.T("cfgp.mine.empty"), "small muted", wrap: true));
        foreach (var p in mine)
        {
            var preset = p;
            var info = Ui.Col(3, Ui.Row(8, Ui.Text(p.Title, "h3")), Ui.Text(I18n.T("cfgp.mine.saved", ("when", Ui.Ago(p.Saved))), "small muted"));
            if (last == "mine:" + p.Id && info.Children[0] is StackPanel t) t.Children.Add(new Border { Classes = { "tab-badge", "good" }, Child = Ui.Text(I18n.T("cfgp.applied"), "tiny") });
            info.VerticalAlignment = VerticalAlignment.Center;
            var buttons = Ui.Row(6,
                Ui.Button(I18n.T("cfgp.apply"), () => ApplyMine(preset), "primary", Icons.Check),
                Ui.Button("", () => ExportPreset(preset), "icon ghost", Icons.Upload, I18n.T("cfgp.mine.export")),
                Ui.Button("", () => RenamePreset(preset), "icon ghost", Icons.Edit, I18n.T("prof.rename")),
                Ui.Button("", () => { CfgPresets.Delete(_g.Def.Id, preset.Id); Build(); }, "icon ghost", Icons.Trash, I18n.T("prof.remove")));
            buttons.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            row.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.Save, 18, Ui.Res("Text")) });
            Grid.SetColumn(info, 1);
            row.Children.Add(info);
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10), Child = row });
        }
        return Ui.Card(Ui.Col(14, Ui.Col(4, Ui.Text(I18n.T("cfgp.mine"), "h2"), Ui.Text(I18n.T("cfgp.mine.hint"), "muted", wrap: true)), bar, list), 22);
    }

    void ApplyMine(CfgPreset p)
    {
        var w = MainWindow.Current!;
        try { var n = CfgPresets.ApplyMine(_g, p.Id); w.Toast(I18n.T("cfgp.mine.applied", ("name", p.Title), ("n", n))); }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
        Build();
    }

    void RenamePreset(CfgPreset p)
    {
        var w = MainWindow.Current!;
        var box = new TextBox { Text = p.Title };
        w.Dialog(I18n.T("prof.rename"), box,
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("common.save"), () => { w.CloseDialog(); CfgPresets.Rename(_g.Def.Id, p.Id, box.Text ?? ""); Build(); }, "primary", Icons.Check));
        box.AttachedToVisualTree += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    async void ExportPreset(CfgPreset p)
    {
        var w = MainWindow.Current!;
        var safe = string.Concat(p.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var file = await w.PickSaveAny(I18n.T("cfgp.mine.export"), $"{_g.Def.ShortName} - {safe}.mlcfg", "mlcfg", I18n.T("cfgp.filetype"));
        if (file is null) return;
        try { CfgPresets.Export(_g.Def.Id, p.Id, file); w.Toast(I18n.T("cfgp.exported")); }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
    }

    async void ImportPreset()
    {
        var w = MainWindow.Current!;
        var file = await w.PickAny(I18n.T("cfgp.mine.import"), ["*.mlcfg", "*.zip"], I18n.T("cfgp.filetype"));
        if (file is null) return;
        try { var p = CfgPresets.Import(_g, file); w.Toast(I18n.T("cfgp.imported", ("name", p.Title))); }
        catch (Exception e) { w.Toast(e.Message, bad: true); }
        Build();
    }

    // ---------------------------------------------------------------- редактор

    Control EditorCard(List<string> files)
    {
        if (_cfgFile is null || !files.Contains(_cfgFile)) _cfgFile = files[0];
        var parsed = files.ToDictionary(f => f, CfgFile.Parse);
        var changedTotal = parsed.Values.Sum(l => l.Count(e => e.Changed));

        // Шапка: поиск по всем файлам, «только изменённые», сбросить всё, папка.
        var search = new TextBox { Text = _cfgFilter, Watermark = I18n.T("cfg.search.all"), Height = 40, MinWidth = 260 };
        search.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Search, 15, Ui.Res("Muted")) };
        var onlyChanged = new ToggleSwitch { IsChecked = _cfgOnlyChanged, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        var onlyLabel = Ui.Text(I18n.T("cfg.onlyChanged", ("n", changedTotal)), "muted");
        onlyLabel.VerticalAlignment = VerticalAlignment.Center;
        var resetAll = Ui.Button(I18n.T("cfg.resetFile"), () => ResetFile(_cfgFile!), "ghost", Icons.Undo);
        var open = Ui.Button("", () => Actions.OpenFolder(_cfgFile), "icon ghost", Icons.Folder, I18n.T("cfg.open"));
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), ColumnSpacing = 10 };
        tools.Children.Add(search);
        var toggle = Ui.Row(8, onlyChanged, onlyLabel);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(toggle, 1);
        tools.Children.Add(toggle);
        Grid.SetColumn(resetAll, 3);
        tools.Children.Add(resetAll);
        Grid.SetColumn(open, 4);
        tools.Children.Add(open);

        // Слева — моды (файлы), справа — их настройки.
        var fileList = Ui.Col(2);
        var entries = Ui.Col(8);
        Button? selected = null;
        foreach (var f in files)
        {
            var file = f;
            var changed = parsed[f].Count(e => e.Changed);
            var row = new DockPanel();
            if (changed > 0)
            {
                var dot = new Border { Classes = { "tab-badge" }, Child = Ui.Text(changed.ToString(), "tiny"), Margin = new Thickness(6, 0, 0, 0) };
                DockPanel.SetDock(dot, Dock.Right);
                row.Children.Add(dot);
            }
            row.Children.Add(new TextBlock { Text = CfgFile.Label(f), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Classes = { "chip" }, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };
            ToolTip.SetTip(b, f);
            if (f == _cfgFile) { b.Classes.Add("active"); selected = b; }
            b.Click += (_, _) =>
            {
                if (_cfgFile == file && _cfgFilter == "") return;
                _cfgFile = file;
                _cfgFilter = "";
                search.Text = "";
                selected?.Classes.Remove("active");
                b.Classes.Add("active");
                selected = b;
                Fill(true);
            };
            fileList.Children.Add(b);
        }

        void Fill(bool animate)
        {
            entries.Children.Clear();
            var q = _cfgFilter.Trim();
            IEnumerable<(string File, CfgEntry Entry)> shown = q.Length > 0
                ? parsed.SelectMany(kv => kv.Value.Select(e => (kv.Key, e)))
                    .Where(x => x.e.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || (x.e.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || x.e.Description.Contains(q, StringComparison.OrdinalIgnoreCase) || x.e.Section.Contains(q, StringComparison.OrdinalIgnoreCase))
                : parsed[_cfgFile!].Select(e => (_cfgFile!, e));
            if (_cfgOnlyChanged) shown = shown.Where(x => x.Entry.Changed);
            var list = shown.Take(400).ToList();
            resetAll.IsVisible = q.Length == 0 && parsed[_cfgFile!].Any(e => e.Changed);
            if (list.Count == 0)
            {
                entries.Children.Add(Ui.Text(q.Length > 0 ? I18n.T("cfg.nothing", ("q", q)) : _cfgOnlyChanged ? I18n.T("cfg.noChanged") : I18n.T("cfg.empty"), "muted"));
                return;
            }
            var i = 0;
            foreach (var group in list.GroupBy(x => (q.Length > 0 ? CfgFile.Label(x.File) + " · " : "") + x.Entry.Section))
            {
                var head = Ui.Text(group.Key == "" ? CfgFile.Label(group.First().File) : group.Key, "h3");
                head.Margin = new Thickness(2, i == 0 ? 0 : 10, 0, 2);
                entries.Children.Add(head);
                foreach (var (file, entry) in group)
                {
                    var row = CfgRow(file, entry);
                    entries.Children.Add(row);
                    if (animate && i < 14) Animate.From(row, "translateX(14px)", 320, i * 22);
                    i++;
                }
            }
            if (shown.Count() > list.Count) entries.Children.Add(Ui.Text(I18n.T("cfg.more", ("n", shown.Count() - list.Count)), "small muted"));
        }

        DispatcherTimer? debounce = null;
        search.TextChanged += (_, _) =>
        {
            _cfgFilter = search.Text ?? "";
            debounce?.Stop();
            debounce = DispatcherTimer.RunOnce(() => Fill(false), TimeSpan.FromMilliseconds(180)) as DispatcherTimer;
        };
        onlyChanged.IsCheckedChanged += (_, _) => { _cfgOnlyChanged = onlyChanged.IsChecked == true; Fill(true); };
        Fill(false);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("250,*"), ColumnSpacing = 18 };
        var left = new Border
        {
            Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Top,
            Child = new ScrollViewer { Content = fileList, MaxHeight = 620, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        layout.Children.Add(left);
        Grid.SetColumn(entries, 1);
        layout.Children.Add(entries);

        var title = Ui.Col(4, Ui.Text(I18n.T("cfg.all"), "h2"), Ui.Text(I18n.T("cfg.hint"), "muted", wrap: true));
        return Ui.Card(Ui.Col(16, title, tools, layout), 22);
    }

    void ResetFile(string file)
    {
        var w = MainWindow.Current!;
        var changes = CfgFile.Parse(file).Where(e => e.Changed).Select(e => (e, e.Default)).ToList();
        if (changes.Count == 0) return;
        w.Dialog(I18n.T("cfg.resetFile.title", ("name", CfgFile.Label(file))), Ui.Text(I18n.T("cfg.resetFile.text", ("n", changes.Count)), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("cfg.resetFile"), () =>
            {
                w.CloseDialog();
                try { var n = CfgFile.SetMany(file, changes); w.Toast(I18n.T("cfg.resetFile.done", ("n", n))); }
                catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); }
                Build();
            }, "primary", Icons.Undo));
    }

    Control CfgRow(string path, CfgEntry e)
    {
        Control editor;
        Button? reset = null;
        Border? card = null;
        void Save(string v)
        {
            try
            {
                CfgFile.Set(path, e, v);
                if (reset is not null) reset.IsVisible = e.Changed;
                card?.Classes.Set("changed", e.Changed);
            }
            catch (Exception ex) { MainWindow.Current?.Toast(Jobs.Explain(ex), bad: true); }
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (e.IsBool)
        {
            var sw = new ToggleSwitch { IsChecked = e.Value.Equals("true", StringComparison.OrdinalIgnoreCase), OnContent = "", OffContent = "", MinWidth = 0 };
            sw.IsCheckedChanged += (_, _) => Save(sw.IsChecked == true ? "true" : "false");
            editor = sw;
        }
        else if (e.Multi && e.Choices.Count > 1 || e.Choices.Count > 2 && (e.Value.Contains(',') || e.Choices.Contains("All") && e.Choices.Contains("None")))
        {
            // Флаги (LogLevels и т.п.): несколько значений через запятую — галочками.
            var label = new TextBlock { Text = e.Value, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Content = Ui.Row(8, label, Ui.Icon(Icons.ChevronDown, 13)), MinWidth = 180 };
            var panel = Ui.Col(4);
            var boxes = new List<CheckBox>();
            var current = e.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var choice in e.Choices.Where(c => c is not ("None" or "All")))
            {
                var box = new CheckBox { Content = choice, IsChecked = current.Contains(choice) || current.Contains("All") };
                boxes.Add(box);
                panel.Children.Add(box);
            }
            void Update()
            {
                var on = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList();
                var value = on.Count == 0 ? (e.Choices.Contains("None") ? "None" : "") : on.Count == boxes.Count && e.Choices.Contains("All") ? "All" : string.Join(", ", on);
                label.Text = value;
                if (value != e.Value) Save(value);
            }
            foreach (var b in boxes) b.IsCheckedChanged += (_, _) => Update();
            button.Flyout = new Flyout { Content = new Border { Padding = new Thickness(6), Child = panel }, Placement = PlacementMode.BottomEdgeAlignedRight };
            editor = button;
        }
        else if (e.Choices.Count > 1 && !e.Value.Contains(','))
        {
            var box = new ComboBox { ItemsSource = e.Choices, SelectedItem = e.Choices.FirstOrDefault(c => c.Equals(e.Value, StringComparison.OrdinalIgnoreCase)), MinWidth = 180 };
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is string s && s != e.Value) Save(s); };
            editor = box;
        }
        else if (e.Min is double min && e.Max is double max && max > min && double.TryParse(e.Value, System.Globalization.NumberStyles.Float, inv, out var current))
        {
            // Число с диапазоном: ползунок и поле рядом. Целые — шагом 1.
            var whole = e.Type is "Int32" or "Int64" or "Byte" or "Int16" or "UInt32";
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(current, min, max), Width = 200, VerticalAlignment = VerticalAlignment.Center };
            if (whole) { slider.IsSnapToTickEnabled = true; slider.TickFrequency = 1; }
            var text = new TextBox { Text = e.Value, Width = 84, HorizontalContentAlignment = HorizontalAlignment.Right };
            DispatcherTimer? wait = null;
            string Format(double v) => whole ? Math.Round(v).ToString(inv) : Math.Round(v, 3).ToString(inv);
            slider.ValueChanged += (_, _) =>
            {
                text.Text = Format(slider.Value);
                wait?.Stop();
                wait = DispatcherTimer.RunOnce(() => { if (text.Text != e.Value) Save(text.Text!); }, TimeSpan.FromMilliseconds(350)) as DispatcherTimer;
            };
            void Commit()
            {
                if (!double.TryParse(text.Text, System.Globalization.NumberStyles.Float, inv, out var v)) { text.Text = e.Value; return; }
                v = Math.Clamp(v, min, max);
                text.Text = Format(v);
                slider.Value = v;
                if (text.Text != e.Value) Save(text.Text);
            }
            text.LostFocus += (_, _) => Commit();
            text.KeyDown += (_, k) => { if (k.Key == Avalonia.Input.Key.Enter) Commit(); };
            editor = Ui.Row(10, slider, text);
        }
        else
        {
            var box = new TextBox { Text = e.Value, MinWidth = 220, MaxWidth = 360 };
            void Commit() { if ((box.Text ?? "") != e.Value) Save(box.Text ?? ""); }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, k) => { if (k.Key == Avalonia.Input.Key.Enter) Commit(); };
            editor = box;
        }
        editor.VerticalAlignment = VerticalAlignment.Center;

        reset = Ui.Button("", () =>
        {
            Save(e.Default);
            Build();
        }, "icon ghost", Icons.Undo, I18n.T("cfg.reset", ("v", e.Default)));
        reset.IsVisible = e.Changed;

        var title = Ui.Col(2, Ui.Text(e.Title ?? e.Key, "h3"));
        if (e.Title is not null) ToolTip.SetTip(title.Children[0], e.Key);
        if (e.Description.Length > 0) title.Children.Add(Ui.Text(e.Description, "small muted", wrap: true));
        if (e.Default != "" && !e.IsBool) title.Children.Add(Ui.Text(I18n.T("cfg.default", ("v", e.Default)), "tiny muted"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(title);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        Grid.SetColumn(reset, 2);
        grid.Children.Add(reset);
        card = new Border { Classes = { "cfg-row" }, Child = grid };
        card.Classes.Set("changed", e.Changed);
        return card;
    }

    /// <summary>Для снимков: окно подтверждения набора.</summary>
    public void DemoPreset(string id) => ConfirmPreset(CfgPresets.BuiltIn(_g.Def).First(p => p.Id == id));
}
