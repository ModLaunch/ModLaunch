using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Страница мода, часть 3 — вкладки, как на Nexus: описание, файлы, изменения,
/// требования, картинки, видео, отзывы и статистика.
/// </summary>
public sealed partial class ModPage
{
    string _tab;
    List<Block>? _changelog;
    bool _changelogLoading;

    Control Tabs()
    {
        Button Tab(string id, string text, string icon, int? count = null)
        {
            var content = Ui.Row(7, Ui.Icon(icon, 15), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (count is > 0)
                content.Children.Add(new Border { Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 1), Child = Ui.Text(count.Value.ToString(), "small") });
            var b = new Button { Classes = { "tab" }, Content = content };
            if (_tab == id) b.Classes.Add("active");
            b.Click += (_, _) => { if (_tab == id) return; _tab = id; Build(); };
            return b;
        }
        var d = _details;
        var reviews = _brief.Source == "hub" ? _comments?.Count ?? 0 : Social.Reviews.Stats().GetValueOrDefault($"{_g.Def.Id}|{Mod.Id}")?.Count ?? 0;
        var bar = new WrapPanel();
        foreach (var t in new[]
        {
            Tab("about", I18n.T("mt.about"), Icons.Book),
            Tab("files", I18n.T("mt.files"), Icons.Layers, _versions?.Count),
            Tab("changes", I18n.T("mt.changes"), Icons.Refresh),
            Tab("reqs", I18n.T("mt.reqs"), Icons.Link, d?.Requirements.Count),
            Tab("images", I18n.T("mt.images"), Icons.Image, d?.Images.Count),
            Tab("videos", I18n.T("mt.videos"), Icons.Video, d?.Videos.Count),
            Tab("reviews", _brief.Source == "hub" ? I18n.T("mt.comments") : I18n.T("mt.reviews"), Icons.Chat, reviews),
            Tab("stats", I18n.T("mt.stats"), Icons.Chart),
        }) bar.Children.Add(t);
        return new Border { Classes = { "card" }, Padding = new Thickness(6), CornerRadius = new CornerRadius(16), Child = bar, HorizontalAlignment = HorizontalAlignment.Left };
    }

    Control TabContent()
    {
        if (_tab is "files") return VersionsCard();
        if (_tab is "reviews") return _brief.Source == "hub" ? CommentsCard() : ReviewsCard();
        if (_tab is "stats") return StatsCard();
        if (_error is not null) return Ui.Card(Ui.Text(_error, "muted", wrap: true), 20);
        if (_details is null) return Ui.Card(Ui.Text(I18n.T("common.loading"), "muted"), 20);
        switch (_tab)
        {
            case "changes": return ChangesCard();
            case "reqs": return _details.Requirements.Count > 0 ? RequirementsList() : Empty(I18n.T("mt.reqs.none"));
            case "images": return _details.Images.Count > 0 ? ImagesGrid() : Empty(I18n.T("mt.images.none"));
            case "videos": return _details.Videos.Count > 0 ? VideosGrid() : Empty(I18n.T("mt.videos.none"));
        }
        var col = Ui.Col(18);
        if (_details.Requirements.Count > 0) col.Children.Add(RequirementsCard());
        if (_details.Images.Count > 0) col.Children.Add(Gallery());
        col.Children.Add(About());
        if (AuthorCard() is { } author) col.Children.Add(author);
        return col;
    }

    static Control Empty(string text) => Ui.Card(Ui.Text(text, "muted", wrap: true), 22);

    // ---------------------------------------------------------------- изменения

    Control ChangesCard()
    {
        if (_changelog is null && !_changelogLoading && !Program.Demo)
        {
            _changelogLoading = true;
            _ = Task.Run(async () =>
            {
                List<Block> list;
                try { list = await Extras.Changelog(_g.Def, Mod, _versions); } catch { list = []; }
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { _changelog = list; _changelogLoading = false; if (_tab == "changes") Build(); });
            });
        }
        var blocks = _changelog ?? (Program.Demo ? Extras.Changelog(_g.Def, Mod, _versions).GetAwaiter().GetResult() : null);
        var col = Ui.Col(10, Ui.Text(I18n.T("mt.changes"), "h2"));
        if (blocks is null) col.Children.Add(Ui.Text(I18n.T("common.loading"), "muted"));
        else if (blocks.Count == 0) col.Children.Add(Ui.Text(I18n.T("mt.changes.none"), "muted", wrap: true));
        foreach (var b in blocks ?? [])
            col.Children.Add(b.Kind switch
            {
                "h" => new SelectableTextBlock { Text = b.Text, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) },
                "li" => new SelectableTextBlock { Text = "•  " + b.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), Margin = new Thickness(10, 0, 0, 0) },
                _ => new SelectableTextBlock { Text = b.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), LineHeight = 22 },
            });
        return Ui.Card(col, 22);
    }

    // ---------------------------------------------------------------- требования

    Control RequirementsList()
    {
        var col = Ui.Col(10, Ui.Text(I18n.T("mt.reqs"), "h2"), Ui.Text(I18n.T("mt.reqs.hint"), "small muted", wrap: true));
        foreach (var r in _details!.Requirements)
        {
            var have = Actions.IsInstalled(_g, r.Id);
            var rr = r;
            var open = Ui.Button(I18n.T("common.open"), () => MainWindow.Current?.Navigate(() => new ModPage(_g.Def.Id, new ModInfo { Source = Mod.Source == "hub" ? Catalog.GuessSource(_g.Def, rr.Id) : Mod.Source, Id = rr.Id, Name = rr.Name })), "ghost", Icons.External);
            DockPanel.SetDock(open, Dock.Right);
            var row = new DockPanel();
            row.Children.Add(open);
            row.Children.Add(Ui.Row(12, Ui.Thumb(null, r.Name, 40, 10),
                Ui.Col(2, Ui.Text(r.Name, "h3"), Ui.Text(have ? I18n.T("mod.deps.have") : I18n.T("mod.deps.will"), "small", color: have ? Ui.Res("Good") : Ui.Res("Muted")))));
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10), Child = row });
        }
        return Ui.Card(col, 22);
    }

    // ---------------------------------------------------------------- картинки и видео

    Control ImagesGrid()
    {
        var wrap = new WrapPanel();
        foreach (var url in _details!.Images)
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            var frame = new Border { Width = 330, Height = 186, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Ui.Res("Surface2"), Child = image, Margin = new Thickness(0, 0, 12, 12), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            _ = Images.FromUrl(url, 660).ContinueWith(t => { if (t.Result is Bitmap b) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = b); });
            var link = url;
            frame.PointerPressed += (_, _) => ShowImage(link);
            wrap.Children.Add(frame);
        }
        return Ui.Card(Ui.Col(12, Ui.Text(I18n.T("mt.images"), "h2"), wrap), 22);
    }

    Control VideosGrid()
    {
        var wrap = new WrapPanel();
        foreach (var id in _details!.Videos)
        {
            var url = $"https://www.youtube.com/watch?v={id}";
            var image = new Image { Stretch = Stretch.UniformToFill };
            _ = Images.FromUrl($"https://i.ytimg.com/vi/{id}/hqdefault.jpg", 660).ContinueWith(t => { if (t.Result is Bitmap b) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = b); });
            var play = new Border
            {
                Width = 60, Height = 42, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.Parse("#E0FF0033")),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Ui.Icon(Icons.Play, 20, Brushes.White),
            };
            var frame = new Button
            {
                Classes = { "cover-btn" }, Padding = new Thickness(0), Margin = new Thickness(0, 0, 12, 12),
                Content = new Border { Width = 330, Height = 186, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Ui.Res("Surface2"), Child = new Panel { Children = { image, play } } },
            };
            ToolTip.SetTip(frame, url);
            frame.Click += (_, _) => Ui.OpenUrl(url);
            wrap.Children.Add(frame);
        }
        return Ui.Card(Ui.Col(12, Ui.Text(I18n.T("mt.videos"), "h2"), Ui.Text(I18n.T("mt.videos.hint"), "small muted"), wrap), 22);
    }

    // ---------------------------------------------------------------- статистика

    Control StatsCard()
    {
        var mod = Mod;
        var stat = Social.Reviews.Stats().GetValueOrDefault($"{_g.Def.Id}|{mod.Id}");
        var versions = _versions ?? [];
        var first = versions.Where(v => v.Date is not null).Select(v => v.Date).DefaultIfEmpty(null).Min();

        Control Tile(string value, string label) => new Border
        {
            Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14), Margin = new Thickness(0, 0, 12, 12), MinWidth = 170,
            Child = Ui.Col(4, new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeight.Bold }, Ui.Text(label, "small muted")),
        };
        var tiles = new WrapPanel();
        tiles.Children.Add(Tile(mod.Downloads > 0 ? mod.Downloads.ToString("N0") : "—", I18n.T("mt.stats.downloads")));
        tiles.Children.Add(Tile(mod.Rating > 0 ? mod.Rating.ToString("N0") : "—", mod.Source == "nexus" ? I18n.T("mt.stats.endorse") : I18n.T("mt.stats.likes")));
        tiles.Children.Add(Tile(versions.Count > 0 ? versions.Count.ToString() : "—", I18n.T("mt.stats.versions")));
        tiles.Children.Add(Tile(stat is null ? "—" : $"{stat.Avg:0.0} ★", I18n.T("mt.stats.rating", ("n", stat?.Count ?? 0))));
        tiles.Children.Add(Tile(first is null ? "—" : first.Value.ToLocalTime().ToString("dd.MM.yyyy"), I18n.T("mt.stats.first")));
        tiles.Children.Add(Tile(mod.UpdatedAt is null ? "—" : Ui.Ago(mod.UpdatedAt), I18n.T("mt.stats.updated")));
        var col = Ui.Col(14, Ui.Text(I18n.T("mt.stats"), "h2"), tiles);

        // Загрузки по версиям — горизонтальные полосы (у Thunderstore и Hub есть счётчик на версию).
        var perVersion = versions.Where(v => v.Downloads > 0).Take(12).ToList();
        if (perVersion.Count > 1)
        {
            var max = perVersion.Max(v => v.Downloads);
            var bars = Ui.Col(6);
            foreach (var v in perVersion)
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,90"), ColumnSpacing = 12 };
                grid.Children.Add(Ui.Text(v.Version, "small"));
                var track = new Border { Height = 12, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                var fill = new Border { Height = 12, CornerRadius = new CornerRadius(4), Background = Ui.Res("Brand"), HorizontalAlignment = HorizontalAlignment.Left };
                var share = (double)v.Downloads / max;
                track.SizeChanged += (_, e) => fill.Width = Math.Max(4, e.NewSize.Width * share);
                track.Child = fill;
                Grid.SetColumn(track, 1);
                grid.Children.Add(track);
                var n = Ui.Text(v.Downloads.ToString("N0"), "small muted");
                n.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(n, 2);
                grid.Children.Add(n);
                ToolTip.SetTip(grid, $"{v.Version}: {v.Downloads:N0} · {(v.Date is null ? "" : v.Date.Value.ToLocalTime().ToString("dd.MM.yyyy"))}");
                bars.Children.Add(grid);
            }
            col.Children.Add(Ui.Text(I18n.T("mt.stats.perVersion"), "h3"));
            col.Children.Add(bars);
        }
        return Ui.Card(col, 22);
    }
}
