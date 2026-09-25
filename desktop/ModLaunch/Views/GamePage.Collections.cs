using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;
using ModLaunch.Sources;

namespace ModLaunch.Views;

public sealed partial class GamePage
{
    readonly List<CollectionInfo> _collections = [];
    int _collPage;
    bool _collMore, _collLoading;
    string? _collError;

    // ---------------------------------------------------------------- коллекции Nexus

    Control CollectionsBlock()
    {
        if (_g.Def.Catalog != CatalogKind.Nexus) return new Panel();
        if (_collPage == 0 && !_collLoading) _ = LoadCollections();

        var col = new StackPanel { Spacing = 12, Margin = new Thickness(0, 10, 0, 0) };
        col.Children.Add(Ui.Text(I18n.T("coll.title"), "h2"));
        col.Children.Add(Ui.Text(I18n.T("coll.hint"), "muted", wrap: true));

        var link = new TextBox { Watermark = I18n.T("coll.link"), Height = 42 };
        link.InnerLeftContent = new Border { Padding = new Thickness(12, 0, 0, 0), Child = Ui.Icon(Icons.Link, 16, Ui.Res("Muted")) };
        void Open() { if (!string.IsNullOrWhiteSpace(link.Text)) OpenCollection(link.Text); }
        link.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Open(); };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        bar.Children.Add(link);
        var open = Ui.Button(I18n.T("coll.linkOpen"), Open, "", Icons.External);
        Grid.SetColumn(open, 1);
        bar.Children.Add(open);
        col.Children.Add(bar);

        if (_collError is not null && _collections.Count == 0) col.Children.Add(Ui.Text(I18n.T("coll.error") + " " + _collError, "muted small", wrap: true));
        else if (!_collLoading && _collections.Count == 0) col.Children.Add(Ui.Text(I18n.T("coll.none"), "muted"));

        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (var c in _collections) grid.Children.Add(CollectionTile(c));
        col.Children.Add(grid);
        if (_collLoading) col.Children.Add(Ui.Text(I18n.T("catalog.loading"), "muted small"));
        else if (_collMore) col.Children.Add(Ui.Button(I18n.T("coll.more"), () => _ = LoadCollections(), "", Icons.Refresh));
        return col;
    }

    async Task LoadCollections()
    {
        _collLoading = true;
        try
        {
            var page = _collPage + 1;
            var (items, _, more) = Program.Demo ? Demo.Collections(page) : await NexusCollections.Browse(_g.Def.NexusDomain!, page, "");
            _collections.AddRange(items);
            _collPage = page;
            _collMore = more;
        }
        catch (Exception e) { _collError = Jobs.Explain(e); _collPage = Math.Max(_collPage, 1); }
        _collLoading = false;
        if (_section == "packs") RenderList();
    }

    Control CollectionTile(CollectionInfo c)
    {
        var image = new Border { Height = 110, ClipToBounds = true, CornerRadius = new CornerRadius(12, 12, 0, 0), Background = Ui.Res("Surface2") };
        var img = new Image { Stretch = Stretch.UniformToFill };
        image.Child = img;
        if (c.Image is not null) _ = Images.FromUrl(c.Image, 480).ContinueWith(t => { if (t.Result is { } b) Avalonia.Threading.Dispatcher.UIThread.Post(() => img.Source = b); });
        var text = Ui.Col(6,
            Ui.Text(c.Name, "h3"),
            Ui.Text(I18n.T("coll.by", ("author", c.Author)), "small brand"),
            Ui.Row(12,
                Ui.Row(5, Ui.Icon(Icons.Package, 12, Ui.Res("Muted")), Ui.Text(c.ModCount.ToString(), "small muted")),
                Ui.Row(5, Ui.Icon(Icons.Heart, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(c.Endorsements), "small muted")),
                Ui.Row(5, Ui.Icon(Icons.Download, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(c.Downloads), "small muted"))));
        text.Margin = new Thickness(14, 10, 14, 14);
        var tile = new Button
        {
            Classes = { "tile" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 12, 12),
            Content = new StackPanel { Children = { image, text } },
        };
        var slug = c.Slug;
        tile.Click += (_, _) => OpenCollection(slug);
        return tile;
    }

    async void OpenCollection(string input)
    {
        var w = MainWindow.Current!;
        CollectionInfo info;
        List<CollectionMod> mods;
        try
        {
            (info, mods) = Program.Demo ? Demo.Collection(_g.Def) : await NexusCollections.Get(_g.Def.NexusDomain!, input);
        }
        catch (Exception e) { w.Toast(Jobs.Explain(e), bad: true); return; }

        var required = mods.Where(m => !m.Optional).ToList();
        var optional = mods.Where(m => m.Optional).ToList();
        var have = mods.Count(m => Actions.IsInstalled(_g, m.Id));

        var list = new StackPanel { Spacing = 6 };
        foreach (var m in mods)
        {
            var row = new DockPanel();
            var installed = Actions.IsInstalled(_g, m.Id);
            var tag = installed ? ModRow.Tag(I18n.T("pack.have"), Ui.Res("Surface3"), Ui.Res("Muted"))
                : m.Optional ? ModRow.Tag(I18n.T("coll.optional"), Ui.Res("Surface3"), Ui.Res("Muted"))
                : ModRow.Tag(I18n.T("pack.will"), Ui.Res("BrandSoft"), Ui.Res("Brand2"));
            DockPanel.SetDock(tag, Dock.Right);
            row.Children.Add(tag);
            row.Children.Add(Ui.Row(10, Ui.Thumb(m.Icon, m.Name, 28, 7, 64), Ui.Text(m.Name + (m.Version != "" ? $" · {m.Version}" : ""))));
            list.Children.Add(row);
        }

        var premium = !string.IsNullOrEmpty(Settings.NexusApiKey) && Settings.NexusPremium;
        var body = Ui.Col(12,
            Ui.Text(I18n.T("coll.by", ("author", info.Author)) + (have > 0 ? " · " + I18n.T("coll.have", ("n", have)) : ""), "small brand"),
            Ui.Text(info.Summary, "muted", wrap: true),
            new ScrollViewer { Content = list, MaxHeight = 300 },
            Ui.Text(I18n.T(premium ? "coll.premium" : "coll.free"), "small muted", wrap: true));

        List<(ModInfo, Pin?)> Items(IEnumerable<CollectionMod> set) => set.Where(m => !Actions.IsInstalled(_g, m.Id))
            .Select(m => (new ModInfo { Source = "nexus", Id = m.Id, Name = m.Name, Icon = m.Icon, Version = m.Version }, (Pin?)new Pin(m.FileId, m.Version, m.FileName))).ToList();

        var actions = new List<Control>
        {
            Ui.Button(I18n.T("coll.onNexus"), () => Ui.OpenUrl(info.Url), "", Icons.External),
        };
        if (optional.Count > 0)
            actions.Add(Ui.Button(I18n.T("coll.withOptional", ("n", optional.Count)), () => { w.CloseDialog(); _ = Actions.InstallQueue(_g, info.Name, Items(mods)); }));
        var n = required.Count(m => !Actions.IsInstalled(_g, m.Id));
        var go = Ui.Button(I18n.T("pack.installN." + I18n.Plural(n, "one", "few", "many"), ("n", n)), () => { w.CloseDialog(); _ = Actions.InstallQueue(_g, info.Name, Items(required)); }, "primary", Icons.Download);
        go.IsEnabled = n > 0;
        actions.Add(go);
        w.Dialog(info.Name, body, actions.ToArray());
    }

    // ---------------------------------------------------------------- ReShade в разделе «Графика»

    Control ReShadeCard()
    {
        var dir = Features.ReShade.DirOf(_g.Def, _g.Path!);
        var state = Features.ReShade.Detect(dir);
        var busy = Jobs.All.Any(j => j.Status == JobStatus.Running && j.Title == "ReShade" && j.GameName == _g.Def.Name);
        Control button = state.Dll is not null
            ? Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(dir), "", Icons.Folder)
            : Ui.Button(busy ? I18n.T("rs.installing") : I18n.T("rs.install"), () =>
            {
                Jobs.Run("ReShade", _g.Def.Name, async (_, progress, ct) => { await Features.ReShade.Install(dir, _g.Def.ReShadeApi, progress, ct); });
                Build();
            }, "primary", Icons.Sparkles);
        button.IsEnabled = !busy;
        button.VerticalAlignment = VerticalAlignment.Center;
        var text = Ui.Col(4,
            Ui.Text(state.Dll is not null ? I18n.T("rs.on") : I18n.T("rs.off"), "h3"),
            Ui.Text(state.Dll is not null
                ? state.Preset is { } p && !p.EndsWith("ReShadePreset.ini", StringComparison.OrdinalIgnoreCase) ? I18n.T("rs.on.preset", ("preset", Path.GetFileNameWithoutExtension(p))) : I18n.T("rs.on.hint")
                : I18n.T("rs.off.hint"), "small muted", wrap: true));
        text.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Sparkles, 20, Ui.Res("Brand2")) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        return Ui.Card(grid, 16);
    }
}
