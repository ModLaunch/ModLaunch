using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Views;

/// <summary>
/// Инвентарь креатора — один на все игры: свои файлы и куски кода, взятое и
/// купленное в хабе, свои ассеты на витрине и обмены. Любой предмет вставляется
/// в проект одной строкой (use / asset).
/// </summary>
public sealed partial class CreatorPage
{
    string _invKind = "all";
    string? _invGame;
    List<Owned>? _owned;
    List<Trade>? _trades;
    string? _invError;
    bool _invLoading;

    async Task LoadInventory()
    {
        if (_invLoading || (!Account.SignedIn && !Program.Demo)) return;
        _invLoading = true;
        try
        {
            var owned = Market.MyItems();
            var trades = Market.Trades();
            _assets ??= await Market.Assets();
            _owned = await owned;
            _trades = await trades;
            _invError = null;
        }
        catch (Exception e) { _invError = Explain(e); _owned ??= []; _trades ??= []; }
        _invLoading = false;
        Build();
    }

    Control InventoryView()
    {
        if (_owned is null && !_invLoading && !Program.Screenshot) _ = LoadInventory();
        var col = new StackPanel { Spacing = 16 };
        var game = _invGame is null ? null : GameCatalog.ById(_invGame);

        col.Children.Add(new Border
        {
            Classes = { "card", "hero" }, Padding = new Thickness(24, 20),
            Child = new DockPanel
            {
                Children =
                {
                    DockRight(Ui.Row(8,
                        Ui.Button(I18n.T("inv.fromMod"), TakeFromMod, "", Icons.Package),
                        Ui.Button(I18n.T("inv.newCode"), () => EditSnippet(null), "", Icons.Code),
                        Ui.Button(I18n.T("inv.addFile"), AddFile, "primary", Icons.FilePlus))),
                    Ui.Col(4, Ui.Text(I18n.T("inv.eyebrow"), "eyebrow"), Ui.Text(I18n.T("inv.title"), "h2"), Ui.Text(I18n.T("inv.text"), "muted", wrap: true)),
                },
            },
        });

        // Фильтры: вид и игра (что к ней подходит — ярко, остальное приглушено).
        var kinds = Ui.Row(6);
        foreach (var k in new[] { "all" }.Concat(Inventory.Kinds))
        {
            var kk = k;
            var chip = Ui.Button(I18n.T("mk.kind." + k), () => { _invKind = kk; Build(); }, "chip", k == "all" ? null : KindLook[k].Icon);
            if (_invKind == k) chip.Classes.Add("active");
            kinds.Children.Add(chip);
        }
        var gameBox = new ComboBox { Width = 220 };
        var ids = new List<string?> { null };
        gameBox.Items.Add(I18n.T("inv.anyGame"));
        foreach (var g in AppState.Games) { ids.Add(g.Def.Id); gameBox.Items.Add(I18n.T("inv.forGame", ("game", g.Def.ShortName))); }
        gameBox.SelectedIndex = Math.Max(0, ids.IndexOf(_invGame));
        gameBox.SelectionChanged += (_, _) => { var v = ids[Math.Max(0, gameBox.SelectedIndex)]; if (v != _invGame) { _invGame = v; Build(); } };
        var bar = new DockPanel();
        bar.Children.Add(DockRight(gameBox));
        bar.Children.Add(new ScrollViewer { Content = kinds, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        col.Children.Add(bar);

        // Свои предметы.
        var items = Inventory.List().Where(i => (_invKind == "all" || i.Kind == _invKind)
            && (_filter == "" || i.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || i.Author.Contains(_filter, StringComparison.OrdinalIgnoreCase))).ToList();
        if (items.Count == 0)
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("inv.empty"), "h3"), Ui.Text(I18n.T("inv.empty.text"), "small muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("mk.tab"), () => { _tab = "assets"; Build(); }, "primary", Icons.Bag), Ui.Button(I18n.T("inv.addFile"), AddFile, "", Icons.FilePlus))), 24));
        else
        {
            var grid = new TileGrid { MinItemWidth = 210 };
            foreach (var item in items.OrderByDescending(i => Inventory.Fits(i, game)).ThenByDescending(i => i.Added)) grid.Children.Add(ItemTile(item, game));
            col.Children.Add(grid);
        }

        // Облако: копии, которые есть на аккаунте, но ещё не скачаны сюда.
        if (_owned is { Count: > 0 } owned)
        {
            var local = Inventory.List().Select(i => i.AssetId).OfType<string>().ToHashSet();
            var missing = owned.Where(o => !local.Contains(o.Asset)).Select(o => (o, a: _assets?.FirstOrDefault(x => x.Id == o.Asset))).Where(x => x.a is not null).ToList();
            if (missing.Count > 0)
            {
                var rows = new StackPanel { Spacing = 8 };
                foreach (var (o, a) in missing)
                {
                    var aa = a!;
                    var row = new DockPanel();
                    row.Children.Add(DockRight(Ui.Button(I18n.T("mk.download"), () => Run(() => Download(aa, o.Serial), I18n.T("mk.downloaded", ("name", aa.Name))), "", Icons.Download)));
                    row.Children.Add(Ui.Row(10, Ui.Icon(KindLook.GetValueOrDefault(aa.Kind, KindLook["other"]).Icon, 16, Ui.Res("Muted")),
                        Ui.Text(aa.Name, "strong"), Ui.Text(aa.Limited ? I18n.T("mk.copy", ("n", o.Serial)) : I18n.T("mod.by", ("author", aa.Author)), "small muted")));
                    rows.Children.Add(row);
                }
                col.Children.Add(Ui.Card(Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Globe, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("inv.cloud", ("n", missing.Count)), "h3")),
                    Ui.Text(I18n.T("inv.cloud.text"), "small muted", wrap: true), rows), 18));
            }
        }

        // Обмены.
        if (_trades is { Count: > 0 } trades) col.Children.Add(TradesCard(trades));

        // Своё на витрине: сколько копий разошлось.
        var mineAssets = (_assets ?? []).Where(a => a.Mine).ToList();
        if (mineAssets.Count > 0)
        {
            var rows = new StackPanel { Spacing = 8 };
            foreach (var a in mineAssets)
            {
                var aa = a;
                var row = new DockPanel();
                row.Children.Add(DockRight(Ui.Button("", () => OpenAsset(aa), "icon ghost", Icons.Eye, I18n.T("hub.open"))));
                row.Children.Add(Ui.Row(12, Ui.Icon(KindLook.GetValueOrDefault(a.Kind, KindLook["other"]).Icon, 16, Ui.Res("Muted")),
                    Ui.Text(a.Name, "strong"), Price(a), Ui.Text(I18n.T("mk.sold", ("n", a.Sold)) + (a.Limited ? $" / {a.Supply}" : ""), "small muted"),
                    a.Listed ? new Control() : Ui.Text(I18n.T("mk.hidden"), "small", color: Ui.Res("Warn"))));
                rows.Children.Add(row);
            }
            var earned = mineAssets.Sum(a => a.Sold * a.Price);
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Upload, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("inv.mine"), "h3"),
                Ui.Text(I18n.T("inv.earned", ("price", Credits(earned))), "small muted")), rows), 18));
        }
        if (_invError is not null) col.Children.Add(Ui.Text(_invError, "small muted", wrap: true));
        return col;
    }

    Control ItemTile(InvItem item, GameDef? game)
    {
        var (icon, color) = KindLook.GetValueOrDefault(item.Kind, KindLook["other"]);
        var fits = Inventory.Fits(item, game);
        var origin = item.Origin switch
        {
            "hub" => Tiles.Label(item.Serial > 0 ? I18n.T("inv.origin.hubN", ("n", item.Serial)) : I18n.T("inv.origin.hub"), "#3478F6"),
            "mod" => Tiles.Label(I18n.T("inv.origin.mod"), "#F59E0B"),
            _ => Tiles.Label(I18n.T("inv.origin.local"), "#22C55E"),
        };
        var where = item.Games.Count > 0 ? string.Join(", ", item.Games.Take(2).Select(g => GameCatalog.ById(g)?.ShortName ?? g))
            : I18n.T("mk.engine." + (item.Engine is "" ? "any" : item.Engine));
        var top = new Border
        {
            Height = 86, CornerRadius = new CornerRadius(14, 14, 0, 0), ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(color), 0), new GradientStop(Color.Parse("#17171C"), 1) },
            },
            Child = new Panel
            {
                Children =
                {
                    Ui.Icon(icon, 30, Brushes.White),
                    new Border { Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = origin },
                },
            },
        };
        var body = Ui.Col(4,
            new TextBlock { Text = item.Name, FontSize = 14.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            Ui.Text(where + (item.Size > 0 ? " · " + GamePage.Size(item.Size) : ""), "small muted"),
            Ui.Text(fits ? (item.IsCode ? "use" : "asset") + $" \"{item.Name}\"" : I18n.T("inv.otherGame"), "small", color: fits ? Ui.Res("Brand2") : Ui.Res("Warn")));
        body.Margin = new Thickness(12, 10, 12, 12);
        var b = new Button { Classes = { "tile" }, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, Opacity = fits ? 1 : 0.55, Content = new StackPanel { Children = { top, body } } };
        b.Click += (_, _) => OpenItem(item);
        return b;
    }

    void OpenItem(InvItem item)
    {
        var w = W;
        var usage = Inventory.Usage(item, _invGame is null ? null : GameCatalog.ById(_invGame));
        var name = new TextBox { Text = item.Name, MaxLength = 60 };
        var body = Ui.Col(12,
            Ui.Col(5, Ui.Text(I18n.T("hub.f.name.label"), "small muted"), name),
            Ui.Col(6, Ui.Text(I18n.T("inv.usage"), "eyebrow"), new Border
            {
                Classes = { "inset" }, Padding = new Thickness(12, 8),
                Child = new SelectableTextBlock { Text = usage.TrimEnd(), FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 13, Foreground = Ui.Res("Brand2") },
            }),
            Ui.Text(I18n.T(item.IsCode ? "inv.usage.code" : "inv.usage.file"), "small muted", wrap: true));
        if (item.Note != "") body.Children.Add(Ui.Text(item.Note, "small muted", wrap: true));
        if (item.Private) body.Children.Add(Ui.Text(I18n.T("inv.private"), "small", color: Ui.Res("Warn"), wrap: true));
        if (item.IsCode && item.FilePath is { } f && File.Exists(f))
            body.Children.Add(new Border
            {
                Classes = { "inset" }, Padding = new Thickness(12),
                Child = new ScrollViewer { MaxHeight = 180, Content = new SelectableTextBlock { Text = File.ReadAllText(f), FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap } },
            });

        var projects = Projects.List();
        var insert = Ui.Button(I18n.T("inv.insert"), () =>
        {
            if (projects.Count == 0) { w.Toast(I18n.T("inv.noProjects"), bad: true); return; }
            var p = _open ?? projects[0];
            var text = File.ReadAllText(p.Script);
            Projects.Save(p, text.TrimEnd('\n') + "\n" + usage);
            w.CloseDialog();
            w.Toast(I18n.T("inv.inserted", ("project", p.Name)));
            _tab = "mine";
            Open(Projects.Get(p.Id));
            Build();
        }, "primary", Icons.Plus);
        ToolTip.SetTip(insert, projects.Count == 0 ? I18n.T("inv.noProjects") : I18n.T("inv.insert.tip", ("project", (_open ?? projects[0]).Name)));
        var actions = new List<Control>
        {
            Ui.Button("", () => { w.CloseDialog(); Inventory.Remove(item); Build(); }, "icon ghost", Icons.Trash, I18n.T("inv.remove")),
            Ui.Button("", () => Actions.OpenFolder(item.Dir), "icon ghost", Icons.Folder, I18n.T("cr.files")),
            Ui.Button(I18n.T("common.save"), () => { Inventory.Rename(item, name.Text ?? ""); w.CloseDialog(); Build(); }, "", Icons.Save),
        };
        if (item.IsCode) actions.Add(Ui.Button(I18n.T("inv.edit"), () => EditSnippet(item), "", Icons.Edit));
        if (!item.Private && item.Origin != "hub")
            actions.Add(Ui.Button(I18n.T("inv.share"), () =>
            {
                if (NeedAccount()) return;
                PublishAsset(new AssetDraft
                {
                    Name = item.Name, Kind = item.Kind, Games = [.. item.Games], Engine = item.Engine, Summary = item.Note,
                    File = item.IsCode ? null : item.FilePath, Code = item.IsCode && item.FilePath is { } cf && File.Exists(cf) ? File.ReadAllText(cf) : "",
                });
            }, "", Icons.Upload));
        actions.Add(insert);
        w.Dialog(item.Name, new ScrollViewer { MaxHeight = 520, Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, actions.ToArray());
    }

    async void AddFile()
    {
        var path = await W.PickFile(I18n.T("inv.addFile"));
        if (path is null) return;
        try
        {
            var item = Inventory.AddFile(path, games: _invGame is null ? null : [_invGame], engine: Inventory.EngineOf(_invGame is null ? null : GameCatalog.ById(_invGame)));
            W.Toast(I18n.T("inv.added", ("name", item.Name)));
        }
        catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
        Build();
    }

    /// <summary>Новый или правка куска кода: имя и ModScript, проверка на лету.</summary>
    void EditSnippet(InvItem? item)
    {
        var name = new TextBox { Text = item?.Name ?? "", Watermark = I18n.T("inv.code.name"), MaxLength = 60 };
        var editor = new ScriptEditor { MinHeight = 260 };
        editor.Text = item?.FilePath is { } f && File.Exists(f) ? File.ReadAllText(f) : "# " + I18n.T("inv.code.sample") + "\nfn greet who {\n  print \"hi $who\"\n}\n";
        var check = Ui.Text("", "small", wrap: true);
        void Check()
        {
            // Кусок кода не обязан быть модом: «нет mod/game» — не ошибка.
            var b = ModScript.Compile("mod \"x\"\ngame any\n" + (editor.Text ?? ""));
            var errors = b.Diags.Where(d => !d.Warning && d.Line > 2).ToList();
            check.Text = errors.Count == 0 ? "✓ " + I18n.T("cr.ok") : string.Join("\n", errors.Take(4).Select(d => $"{I18n.T("cr.line", ("n", d.Line - 2))}: {Msg(d)}"));
            check.Foreground = errors.Count == 0 ? Ui.Res("Good") : Ui.Res("Bad");
        }
        editor.TextChanged += Check;
        Check();
        W.Dialog(I18n.T(item is null ? "inv.newCode" : "inv.edit"), Ui.Col(10,
                Ui.Text(I18n.T("inv.code.text"), "small muted", wrap: true), name,
                new Border { Classes = { "card" }, Padding = new Thickness(4), Child = editor }, check),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("common.save"), () =>
            {
                var title = (name.Text ?? "").Trim();
                if (title == "") { W.Toast(I18n.T("inv.code.needName"), bad: true); return; }
                if (item is not null) Inventory.Remove(item);
                Inventory.AddCode(title, editor.Text ?? "", item?.Origin ?? "local", item?.Author ?? "", item?.Games, item?.Note ?? "", item?.AssetId);
                W.CloseDialog();
                W.Toast(I18n.T("inv.added", ("name", title)));
                Build();
            }, "primary", Icons.Save));
    }

    /// <summary>Взять файлы из установленного мода: модели, текстуры, звуки — только для себя.</summary>
    void TakeFromMod()
    {
        var games = AppState.Games.Where(g => g.Registry is not null).ToList();
        if (games.Count == 0) { W.Toast(I18n.T("inv.fromMod.none"), bad: true); return; }
        var gameBox = new ComboBox { MinWidth = 260 };
        foreach (var g in games) gameBox.Items.Add(g.Def.Name);
        var modBox = new ComboBox { MinWidth = 260 };
        var files = new StackPanel { Spacing = 4 };
        var picked = new HashSet<string>();
        List<System.Text.Json.Nodes.JsonObject> mods = [];
        void FillMods()
        {
            modBox.Items.Clear();
            var g = games[Math.Max(0, gameBox.SelectedIndex)];
            mods = g.Registry!.List().Where(m => !m.Bool("missing") && m.Str("kind") != "preset").ToList();
            foreach (var m in mods) modBox.Items.Add(m.Str("name") ?? "?");
            modBox.SelectedIndex = mods.Count > 0 ? 0 : -1;
        }
        void FillFiles()
        {
            files.Children.Clear();
            picked.Clear();
            if (modBox.SelectedIndex < 0) return;
            var g = games[Math.Max(0, gameBox.SelectedIndex)];
            var folder = g.Registry!.FolderFor(mods[modBox.SelectedIndex]);
            var list = Inventory.Takeable(folder);
            if (list.Count == 0) files.Children.Add(Ui.Text(I18n.T("inv.fromMod.nothing"), "small muted"));
            foreach (var file in list.Take(120))
            {
                var path = file;
                var box = new CheckBox { Content = $"{Path.GetRelativePath(folder, file)} · {GamePage.Size(new FileInfo(file).Length)}" };
                box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) picked.Add(path); else picked.Remove(path); };
                files.Children.Add(box);
            }
        }
        gameBox.SelectionChanged += (_, _) => FillMods();
        modBox.SelectionChanged += (_, _) => FillFiles();
        gameBox.SelectedIndex = 0;
        W.Dialog(I18n.T("inv.fromMod"), Ui.Col(10,
                Ui.Text(I18n.T("inv.fromMod.text"), "small muted", wrap: true),
                Ui.Row(10, gameBox, modBox),
                new Border { Classes = { "inset" }, Padding = new Thickness(10), Child = new ScrollViewer { MaxHeight = 260, Content = files } }),
            Ui.Button(I18n.T("common.cancel"), W.CloseDialog),
            Ui.Button(I18n.T("inv.take"), () =>
            {
                if (picked.Count == 0) return;
                var g = games[Math.Max(0, gameBox.SelectedIndex)];
                var mod = mods[modBox.SelectedIndex];
                foreach (var path in picked)
                    try { Inventory.AddFile(path, games: [g.Def.Id], engine: Inventory.EngineOf(g.Def), origin: "mod", note: I18n.T("inv.fromMod.note", ("mod", mod.Str("name") ?? ""), ("author", mod.Str("author") ?? ""))); }
                    catch (Exception e) { W.Toast(Jobs.Explain(e), bad: true); }
                W.CloseDialog();
                W.Toast(I18n.T("inv.taken", ("n", picked.Count)));
                Build();
            }, "primary", Icons.Download));
    }

    Control TradesCard(List<Trade> trades)
    {
        var rows = new StackPanel { Spacing = 10 };
        string Name(string asset) => _assets?.FirstOrDefault(a => a.Id == asset)?.Name ?? asset;
        foreach (var t in trades.Take(12))
        {
            var tt = t;
            var what = t.Give == ""
                ? I18n.T("trade.forCredits", ("price", Credits(t.Credits)), ("item", Name(t.Take)), ("n", t.TakeSerial))
                : I18n.T("trade.forItem", ("give", Name(t.Give)), ("gn", t.GiveSerial), ("item", Name(t.Take)), ("n", t.TakeSerial));
            var row = new DockPanel();
            Control right = t.Status != "open" ? Ui.Text(I18n.T("trade.st." + t.Status), "small muted")
                : t.Incoming
                    ? Ui.Row(6,
                        Ui.Button(I18n.T("trade.decline"), () => Run(() => Market.CloseTrade(tt), I18n.T("trade.declined")), "ghost"),
                        Ui.Button(I18n.T("trade.accept"), () => Run(async () => { await Market.AcceptTrade(tt); _owned = null; }, I18n.T("trade.accepted")), "primary", Icons.Check))
                    : Ui.Button(I18n.T("trade.cancel"), () => Run(() => Market.CloseTrade(tt), I18n.T("trade.cancelled")), "ghost");
            row.Children.Add(DockRight(right));
            row.Children.Add(Ui.Col(2,
                Ui.Text(t.Incoming ? I18n.T("trade.incoming", ("name", t.FromName)) : I18n.T("trade.outgoing"), "strong"),
                Ui.Text(what, "small", wrap: true),
                t.Text == "" ? new Control() : Ui.Text("«" + t.Text + "»", "small muted", wrap: true)));
            rows.Children.Add(row);
        }
        return Ui.Card(Ui.Col(10, Ui.Row(8, Ui.Icon(Icons.Refresh, 16, Ui.Res("Brand2")), Ui.Text(I18n.T("trade.title"), "h3"),
            Ui.Text(I18n.T("trade.open", ("n", trades.Count(t => t.Status == "open" && t.Incoming))), "small muted")), rows), 18);
    }
}
