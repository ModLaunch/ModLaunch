using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using ModLaunch.Social;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Страница мода: описание, картинки, требования, установка и отзывы.</summary>
public sealed partial class ModPage : Page
{
    readonly GameState _g;
    readonly ModInfo _brief;
    ModDetails? _details;
    string? _error;
    int _stars;
    string _text = "";
    bool _sending;

    public ModPage(string gameId, ModInfo mod)
    {
        _g = AppState.Game(gameId);
        _brief = mod;
        _ = Load();
        _ = SyncReviews();
        _ = LoadExtras();
    }

    public override string Title => _brief.Name;
    public override string? GameId => _g.Def.Id;
    public override string SearchHint => I18n.T("search.game", ("game", _g.Def.Name));
    public override void Search(string text) => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "catalog", text));

    async Task Load()
    {
        try { _details = Program.Demo ? Demo.Details(_g.Def, _brief) : await Details.Load(_g.Def, _brief.Id, source: _brief.Source); }
        catch (Exception e) { _error = Jobs.Explain(e); }
        Build();
    }

    async Task SyncReviews()
    {
        if (Program.Demo) return;
        try { await Reviews.Sync(); } catch { }
        Build();
    }

    ModInfo Mod => _details?.Mod ?? _brief;

    public override void Build()
    {
        var col = new StackPanel { Spacing = 18, Margin = new Thickness(34, 22, 34, 34), MaxWidth = 1100 };
        col.Children.Add(Header());
        if (_error is not null) col.Children.Add(Ui.Card(Ui.Text(_error, "muted", wrap: true), 20));
        else if (_details is null) col.Children.Add(Ui.Card(Ui.Text(I18n.T("common.loading"), "muted"), 20));
        else
        {
            if (_details.Requirements.Count > 0) col.Children.Add(RequirementsCard());
            if (_details.Images.Count > 0) col.Children.Add(Gallery());
            col.Children.Add(About());
        }
        col.Children.Add(VersionsCard());
        if (AuthorCard() is { } author) col.Children.Add(author);
        if (_brief.Source == "hub") col.Children.Add(CommentsCard());
        else col.Children.Add(ReviewsCard());
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Control Header()
    {
        var mod = Mod;
        var installed = Actions.IsInstalled(_g, mod.Id);
        var busy = Actions.IsBusy(_g, mod.Id);
        var stat = Reviews.Stats().GetValueOrDefault($"{_g.Def.Id}|{mod.Id}");

        var facts = Ui.Row(18);
        if (stat is not null) facts.Children.Add(Ui.Row(6, Stars(stat.Avg, 14), Ui.Text($"{stat.Avg:0.0} · " + I18n.T("rev.count." + I18n.Plural(stat.Count, "one", "few", "many"), ("n", stat.Count)), "small muted")));
        if (mod.Downloads > 0) facts.Children.Add(Ui.Row(6, Ui.Icon(Icons.Download, 13, Ui.Res("Muted")), Ui.Text(I18n.Compact(mod.Downloads), "small muted")));
        if (mod.Rating > 0) facts.Children.Add(Ui.Row(6, Ui.Icon(Icons.Heart, 13, Ui.Res("Muted")), Ui.Text(I18n.Compact(mod.Rating), "small muted")));
        if (mod.UpdatedAt is not null) facts.Children.Add(Ui.Row(6, Ui.Icon(Icons.Refresh, 13, Ui.Res("Muted")), Ui.Text(Ui.Ago(mod.UpdatedAt), "small muted")));
        if (mod.Version != "") facts.Children.Add(Ui.Text(I18n.T("mod.version", ("version", mod.Version)), "small muted"));

        var info = Ui.Col(6,
            new TextBlock { Text = mod.Name, FontSize = 28, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
            Ui.Text(mod.Author == "" ? _g.Def.Name : I18n.T("mod.by", ("author", mod.Author)) + " · " + _g.Def.Name, "brand"),
            facts);
        if (mod.Description != "") info.Children.Add(Ui.Text(mod.Description, "muted", wrap: true));
        if (_g.Def.IsLegacy(mod.UpdatedAt)) info.Children.Add(ModRow.Tag(I18n.T("badge.old.hint"), Ui.Hex("#3A2A12"), Ui.Res("Warn")));
        info.VerticalAlignment = VerticalAlignment.Center;

        Button action;
        if (installed) action = Ui.Button(I18n.T("mod.installed"), () => MainWindow.Current?.Navigate(() => new GamePage(_g.Def.Id, "installed")), "", Icons.Check);
        else if (busy) action = Ui.Button(I18n.T("aside.installing"), () => { }, "primary");
        else action = Ui.Button(I18n.T("mod.install"), async () => { await Actions.Install(_g, mod); Build(); }, "primary", Icons.Download);
        action.IsEnabled = !busy && _g.Status == Detect.Found;
        action.FontSize = 16;
        action.Padding = new Thickness(26, 12);
        var buttons = Ui.Col(8, action, ActionRow(mod, installed));
        buttons.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 22 };
        grid.Children.Add(Ui.Thumb(mod.Icon, mod.Name, 120, 18, 240));
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return Ui.Card(grid, 22);
    }

    Control RequirementsCard()
    {
        var list = new WrapPanel();
        foreach (var r in _details!.Requirements)
        {
            var have = Actions.IsInstalled(_g, r.Id);
            var chip = ModRow.Tag((have ? "✓ " : "") + r.Name + (have ? $" · {I18n.T("mod.deps.have")}" : $" · {I18n.T("mod.deps.will")}"),
                have ? Ui.Res("Surface3") : Ui.Res("BrandSoft"), have ? Ui.Res("Muted") : Ui.Res("Brand2"));
            chip.Margin = new Thickness(0, 0, 8, 8);
            list.Children.Add(chip);
        }
        return Ui.Card(Ui.Col(10, Ui.Text(_g.Def.Catalog == Games.CatalogKind.Nexus ? I18n.T("mod.deps.nexus") : I18n.T("mod.deps"), "h3"), list), 18);
    }

    Control Gallery()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var url in _details!.Images)
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            var frame = new Border { Width = 320, Height = 180, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Ui.Res("Surface2"), Child = image, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            _ = Images.FromUrl(url, 640).ContinueWith(t => { if (t.Result is Bitmap b) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = b); });
            var link = url;
            frame.PointerPressed += (_, _) => ShowImage(link);
            row.Children.Add(frame);
        }
        return Ui.Card(Ui.Col(10, Ui.Text(I18n.T("mod.gallery"), "h3"), new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }), 18);
    }

    static void ShowImage(string url)
    {
        var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 560 };
        _ = Images.FromUrl(url, 1600).ContinueWith(t => { if (t.Result is Bitmap b) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = b); });
        var w = MainWindow.Current!;
        w.Dialog("", image, Ui.Button(I18n.T("common.open"), () => Ui.OpenUrl(url), "", Icons.External), Ui.Button(I18n.T("common.close"), w.CloseDialog, "primary"));
    }

    Control About()
    {
        var col = Ui.Col(10, Ui.Text(I18n.T("mod.about"), "h2"));
        if (_details!.Blocks.Count == 0) col.Children.Add(Ui.Text(Mod.Description != "" ? Mod.Description : I18n.T("mod.noDescription"), "muted", wrap: true));
        foreach (var b in _details.Blocks)
        {
            col.Children.Add(b.Kind switch
            {
                "h" => new SelectableTextBlock { Text = b.Text, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) },
                "li" => new SelectableTextBlock { Text = "•  " + b.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), Margin = new Thickness(10, 0, 0, 0) },
                _ => new SelectableTextBlock { Text = b.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), LineHeight = 22 },
            });
        }
        return Ui.Card(col, 22);
    }

    // ---------------------------------------------------------------- отзывы

    static Control Stars(double value, double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var i = 1; i <= 5; i++)
            row.Children.Add(Ui.Icon(Icons.Star, size, null, fill: false) is Viewbox v ? Colorize(v, i <= Math.Round(value)) : new Panel());
        return row;

        static Control Colorize(Viewbox v, bool on)
        {
            var path = (Avalonia.Controls.Shapes.Path)((Canvas)v.Child!).Children[0];
            path.Stroke = on ? Ui.Hex("#F2B84B") : Ui.Res("Faint");
            path.Fill = on ? Ui.Hex("#F2B84B") : Brushes.Transparent;
            return v;
        }
    }

    Control ReviewsCard()
    {
        var mod = Mod;
        var col = Ui.Col(14);
        var stat = Reviews.Stats().GetValueOrDefault($"{_g.Def.Id}|{mod.Id}");
        var head = Ui.Row(12, Ui.Text(I18n.T("rev.title"), "h2"));
        if (stat is not null) head.Children.Add(Ui.Row(6, Stars(stat.Avg, 16), Ui.Text($"{stat.Avg:0.0}", "h3")));
        head.Children.Add(Ui.Text(I18n.T("rev.shared"), "small muted"));
        foreach (var c in head.Children) c.VerticalAlignment = VerticalAlignment.Center;
        col.Children.Add(head);

        if (!Reviews.Configured)
        {
            col.Children.Add(Ui.Col(4, Ui.Text(I18n.T("rev.off.title"), "h3"), Ui.Text(I18n.T("rev.off.text"), "muted", wrap: true)));
            return Ui.Card(col, 22);
        }
        if (Reviews.LastError == "BUSY") col.Children.Add(Ui.Col(4, Ui.Text(I18n.T("rev.down.title"), "h3"), Ui.Text(I18n.T("rev.down.text"), "muted", wrap: true)));

        col.Children.Add(Form());

        var list = Reviews.ForMod(_g.Def.Id, mod.Id);
        if (list.Count == 0) col.Children.Add(Ui.Text(I18n.T("rev.empty"), "muted", wrap: true));
        var admin = Account.Get().Admin;
        foreach (var r in list)
        {
            var title = Ui.Row(8, Ui.Text(r.Name, "h3"), Stars(r.Stars, 13));
            if (r.Admin) title.Children.Add(ModRow.Tag("👑 " + I18n.T("rev.admin"), Ui.Hex("#3A2E10"), Ui.Hex("#F2B84B")));
            if (r.Played) { var p = ModRow.Tag(I18n.T("rev.played"), Ui.Res("Surface3"), Ui.Res("Good")); ToolTip.SetTip(p, I18n.T("rev.played.title")); title.Children.Add(p); }
            if (r.Mine) title.Children.Add(ModRow.Tag(I18n.T("rev.mine"), Ui.Res("BrandSoft"), Ui.Res("Brand2")));
            var when = Ui.Ago(r.Updated ?? r.Created) + (r.Updated is not null && r.Created is not null && r.Updated - r.Created > TimeSpan.FromMinutes(1) ? " · " + I18n.T("rev.edited") : "");
            title.Children.Add(Ui.Text(when, "small muted"));
            foreach (var c in title.Children) c.VerticalAlignment = VerticalAlignment.Center;
            var body = Ui.Col(6, title);
            if (r.Text != "") body.Children.Add(new SelectableTextBlock { Text = r.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted") });
            if (admin && !r.Mine)
            {
                var id = r.Id;
                var hide = Ui.Button(I18n.T("rev.moderate"), () => Moderate(id), "ghost", Icons.Trash, I18n.T("rev.moderate.title"));
                hide.HorizontalAlignment = HorizontalAlignment.Left;
                body.Children.Add(hide);
            }
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = body });
        }
        return Ui.Card(col, 22);
    }

    Control Form()
    {
        var mod = Mod;
        var gate = PlayLog.Check(_g, mod.Id);
        if (!gate.Ok)
        {
            var steps = Ui.Col(8,
                Ui.Text(I18n.T("rev.gate.title"), "h3"),
                Ui.Text(I18n.T("rev.gate.text"), "small muted", wrap: true),
                Step(gate.Installed, gate.Installed ? I18n.T("rev.gate.step1.done") : I18n.T("rev.gate.step1"), gate.Installed ? I18n.T("rev.gate.step1.doneHint") : I18n.T("rev.gate.step1.hint")),
                Step(false, I18n.T("rev.gate.step2"), !gate.Installed ? I18n.T("rev.gate.step2.later") : !gate.Enabled ? I18n.T("rev.gate.step2.off") : I18n.T("rev.gate.step2.hint")));
            return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = steps };
        }

        var mine = Reviews.Mine(_g.Def.Id, mod.Id);
        if (_stars == 0 && mine is not null) { _stars = mine.Stars; _text = mine.Text; }
        var starRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (var i = 1; i <= 5; i++)
        {
            var n = i;
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(2), Content = OneStar(n <= _stars) };
            ToolTip.SetTip(b, I18n.T($"rev.star.{n}"));
            b.Click += (_, _) => { _stars = n; Build(); };
            starRow.Children.Add(b);
        }
        if (_stars > 0) starRow.Children.Add(new TextBlock { Text = I18n.T($"rev.star.{_stars}"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), Foreground = Ui.Res("Muted") });

        var profile = Account.Get();
        var name = new TextBox { Text = profile.SignedIn ? profile.Name : Settings.Data.Str("reviewName") ?? "", Watermark = I18n.T("rev.name"), IsReadOnly = profile.SignedIn, MaxLength = 32 };
        var text = new TextBox { Text = _text, Watermark = I18n.T("rev.text"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxLength = 1000 };
        text.TextChanged += (_, _) => _text = text.Text ?? "";

        var publish = Ui.Button(_sending ? I18n.T("rev.sending") : mine is null ? I18n.T("rev.publish") : I18n.T("rev.update"), async () =>
        {
            var w = MainWindow.Current!;
            if (_stars == 0) { w.Toast(I18n.T("rev.pickStars"), bad: true); return; }
            var who = profile.SignedIn && profile.Name is { Length: > 0 } pn ? pn : (name.Text ?? "").Trim();
            if (who == "") { w.Toast(I18n.T("rev.needName"), bad: true); return; }
            if (!profile.SignedIn) { Settings.Data["reviewName"] = who; Settings.Save(); }
            _sending = true;
            Build();
            try
            {
                await Reviews.Submit(_g.Def.Id, mod.Id, mod.Name, _stars, _text, who, gate.Played);
                w.Toast(mine is null ? I18n.T("rev.published") : I18n.T("rev.updatedToast"));
            }
            catch (Exception e) { w.Toast(Reviews.Explain(e), bad: true); }
            _sending = false;
            Build();
        }, "primary", Icons.Check);
        publish.IsEnabled = !_sending;

        var buttons = Ui.Row(8, publish);
        if (mine is not null) buttons.Children.Add(Ui.Button(I18n.T("rev.delete"), DeleteMine, "ghost", Icons.Trash));

        var form = Ui.Col(10, Ui.Text(mine is null ? I18n.T("rev.write") : I18n.T("rev.yours"), "h3"), starRow);
        if (profile.SignedIn) form.Children.Add(Ui.Text(I18n.T("rev.as", ("name", profile.Name ?? "")), "small muted"));
        else
        {
            form.Children.Add(name);
            var login = Ui.Button(I18n.T("rev.loginHint"), () => MainWindow.Current?.Navigate(() => new SettingsPage("accounts")), "ghost", Icons.Key);
            login.HorizontalAlignment = HorizontalAlignment.Left;
            form.Children.Add(login);
        }
        form.Children.Add(text);
        form.Children.Add(buttons);
        return new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = form };
    }

    static Control OneStar(bool on)
    {
        var v = (Viewbox)Ui.Icon(Icons.Star, 26);
        var path = (Avalonia.Controls.Shapes.Path)((Canvas)v.Child!).Children[0];
        path.Stroke = on ? Ui.Hex("#F2B84B") : Ui.Res("Faint");
        path.Fill = on ? Ui.Hex("#F2B84B") : Brushes.Transparent;
        return v;
    }

    static Control Step(bool done, string title, string hint) => Ui.Row(10,
        new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = done ? Ui.Res("Good") : Ui.Res("Surface3"), Child = done ? Ui.Icon(Icons.Check, 12, Brushes.Black) : null, VerticalAlignment = VerticalAlignment.Top },
        Ui.Col(2, Ui.Text(title, "h3"), Ui.Text(hint, "small muted", wrap: true)));

    void DeleteMine()
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("rev.deleteConfirm"), Ui.Text(I18n.T("rev.deleteConfirm.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("rev.delete"), async () =>
            {
                w.CloseDialog();
                try { await Reviews.Remove(_g.Def.Id, Mod.Id); _stars = 0; _text = ""; w.Toast(I18n.T("rev.deleted")); }
                catch (Exception e) { w.Toast(Reviews.Explain(e), bad: true); }
                Build();
            }, "primary", Icons.Trash));
    }

    void Moderate(string id)
    {
        var w = MainWindow.Current!;
        w.Dialog(I18n.T("rev.moderate.confirm"), Ui.Text(I18n.T("rev.moderate.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("rev.moderate"), async () =>
            {
                w.CloseDialog();
                try { await Reviews.Moderate(id); w.Toast(I18n.T("rev.moderated")); }
                catch (Exception e) { w.Toast(Reviews.Explain(e), bad: true); }
                Build();
            }, "primary", Icons.Trash));
    }
}
