using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Minecraft;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Вкладка «Каталог»: Modrinth, отфильтрованный под версию и загрузчик выбранной сборки.</summary>
public sealed partial class MinecraftPage
{
    string _query = "";
    static string _type = "mod";
    static string _sort = "relevance";
    static string _category = "";
    readonly List<MrProject> _hits = [];
    long _total;
    bool _loading;
    string? _error;
    readonly HashSet<string> _busy = [];
    CancellationTokenSource? _cts;

    /// <summary>Популярные категории Modrinth для каждого вида (идентификаторы как на сайте).</summary>
    static readonly Dictionary<string, string[]> Categories = new()
    {
        ["mod"] = ["optimization", "technology", "adventure", "magic", "decoration", "utility", "worldgen", "storage", "equipment", "food", "mobs", "library"],
        ["modpack"] = ["adventure", "kitchen-sink", "lightweight", "magic", "multiplayer", "optimization", "quests", "technology"],
        ["shader"] = ["realistic", "semi-realistic", "cartoon", "fantasy", "vanilla-like", "potato"],
        ["resourcepack"] = ["vanilla-like", "realistic", "simplistic", "themed", "tweaks", "utility", "16x", "32x"],
        ["datapack"] = ["adventure", "decoration", "magic", "technology", "utility", "worldgen"],
    };

    static string CategoryTitle(string id) =>
        I18n.Has("mine.cat." + id) ? I18n.T("mine.cat." + id) : id;

    async Task LoadCatalog(bool reset = false)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _loading = true;
        _error = null;
        if (reset) _hits.Clear();
        if (Shown) Build();
        try
        {
            var target = _type == "modpack" ? null : Mc.Active;
            var (hits, total) = await McModrinth.Search(_query, _type, _sort, reset ? 0 : _hits.Count, target, _category, cts.Token);
            if (cts.IsCancellationRequested) return;
            _hits.AddRange(hits.Where(h => _hits.All(x => x.Id != h.Id)));
            _total = total;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { _error = Jobs.Explain(e); }
        _loading = false;
        if (Shown && MainWindow.Current?.CurrentPage == this) Build();
    }

    Control CatalogView()
    {
        var a = Mc.Active;
        var col = new StackPanel { Spacing = 14 };

        // Вид: моды, модпаки, ресурспаки, шейдеры, датапаки.
        var types = Ui.Row(6);
        foreach (var type in McModrinth.Types)
        {
            var t = type;
            var chip = Ui.Button(I18n.T($"mine.kind.{type}.many"), () => { _type = t; _category = ""; _ = LoadCatalog(reset: true); }, "chip");
            if (_type == type) chip.Classes.Add("active");
            types.Children.Add(chip);
        }
        types.VerticalAlignment = VerticalAlignment.Center;
        var sort = new ComboBox { Width = 190, ItemsSource = McModrinth.Sorts.Select(s => I18n.T("mr.sort." + s)).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(McModrinth.Sorts, _sort)), VerticalAlignment = VerticalAlignment.Center };
        sort.SelectionChanged += (_, _) => { var v = McModrinth.Sorts[Math.Max(0, sort.SelectedIndex)]; if (v != _sort) { _sort = v; _ = LoadCatalog(reset: true); } };
        var search = new TextBox { Width = 260, Watermark = I18n.T("mine.search"), Text = _query, VerticalAlignment = VerticalAlignment.Center };
        search.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { _query = search.Text?.Trim() ?? ""; _ = LoadCatalog(reset: true); } };
        var right = Ui.Row(8, search, sort);
        var bar = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(types);
        col.Children.Add(bar);

        // Категории и «для какой сборки» ищем.
        var cats = new WrapPanel();
        var all = Ui.Button(I18n.T("mine.cat.all"), () => { _category = ""; _ = LoadCatalog(reset: true); }, "chip");
        if (_category == "") all.Classes.Add("active");
        all.Margin = new Thickness(0, 0, 6, 6);
        cats.Children.Add(all);
        foreach (var c in Categories.GetValueOrDefault(_type) ?? [])
        {
            var id = c;
            var chip = Ui.Button(CategoryTitle(c), () => { _category = id; _ = LoadCatalog(reset: true); }, "chip");
            if (_category == c) chip.Classes.Add("active");
            chip.Margin = new Thickness(0, 0, 6, 6);
            cats.Children.Add(chip);
        }
        col.Children.Add(cats);

        var target = _type == "modpack"
            ? I18n.T("mine.catalog.packs")
            : a is null ? I18n.T("mine.catalog.noBuild") : I18n.T("mine.catalog.for", ("build", a.Name), ("label", a.Label));
        var summary = Ui.Row(10, Ui.Icon(_type == "modpack" ? Icons.Layers : Icons.Check, 14, Ui.Res("Brand")), Ui.Text(target, "small"));
        if (!_loading || _hits.Count > 0)
            summary.Children.Add(Ui.Text("·  " + (_query == "" ? I18n.T("mr.total", ("n", I18n.Compact(_total))) : I18n.T("mr.found", ("n", I18n.Compact(_total)), ("q", _query))), "small muted"));
        col.Children.Add(summary);

        if (_error is not null)
            col.Children.Add(Ui.Card(Ui.Col(8, Ui.Text(I18n.T("mr.error"), "h3"), Ui.Text(_error, "muted", wrap: true),
                Ui.Button(I18n.T("cr.refresh"), () => _ = LoadCatalog(reset: true), "primary", Icons.Refresh)), 22));

        var grid = new UniformGrid { Columns = 3 };
        var installed = a is null ? [] : McContent.InstalledProjects(a);
        foreach (var p in _hits) grid.Children.Add(Card(p, a, installed.Contains(p.Id)));
        if (_loading) for (var n = 0; n < (_hits.Count == 0 ? 9 : 3); n++) grid.Children.Add(new Border { Classes = { "card", "skeleton" }, Height = 168, Margin = new Thickness(0, 0, 14, 14) });
        col.Children.Add(grid);
        if (!_loading && _hits.Count == 0 && _error is null) col.Children.Add(Ui.Text(I18n.T("mr.empty"), "muted"));
        if (!_loading && _hits.Count < _total)
        {
            var more = Ui.Button(I18n.T("mr.more"), () => _ = LoadCatalog(), "", Icons.ChevronDown);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(more);
        }
        return col;
    }

    static string LoaderName(string l) => Mc.Loaders.Contains(l) ? Mc.LoaderTitle(l) : l.Length > 0 ? char.ToUpperInvariant(l[0]) + l[1..] : l;

    static string KindOf(MrProject p) => p.Type is "resourcepack" or "shader" or "datapack" or "modpack" ? p.Type : "mod";

    Control Card(MrProject p, McInstance? a, bool installed)
    {
        var busy = _busy.Contains(p.Id);
        var pack = KindOf(p) == "modpack";
        var (label, icon, cls) = busy ? (I18n.T("mr.installing"), Icons.Download, "ghost")
            : installed ? (I18n.T("mr.installed"), Icons.Check, "ghost")
            : pack ? (I18n.T("mr.install"), Icons.Plus, "primary")
            : a is null ? (I18n.T("mine.build.new"), Icons.Plus, "")
            : (I18n.T("mr.install"), Icons.Download, "primary");
        var install = Ui.Button(label, () => Install(p), cls, icon, pack ? I18n.T("mine.catalog.packs") : null);
        install.IsEnabled = !busy && !installed;

        var cats = new WrapPanel();
        foreach (var c in p.Categories.Where(c => !Mc.Loaders.Contains(c) && c != "minecraft").Take(4))
            cats.Children.Add(new Border { Background = Ui.Res("Surface3"), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 5, 5), Child = new TextBlock { Text = CategoryTitle(c), FontSize = 11, Foreground = Ui.Res("Muted") } });
        var head = Ui.Col(3, new TextBlock { Text = p.Title, Classes = { "h3" }, TextTrimming = TextTrimming.CharacterEllipsis }, Ui.Text(I18n.T("mod.by", ("author", p.Author)), "small brand"));
        head.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        top.Children.Add(Ui.Thumb(p.Icon, p.Title, 60, 14, 128));
        Grid.SetColumn(head, 1);
        top.Children.Add(head);
        var body = Ui.Col(8,
            top,
            new TextBlock { Text = p.Description, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Res("Muted"), FontSize = 13, Height = 36 },
            cats);
        var foot = new DockPanel();
        DockPanel.SetDock(install, Dock.Right);
        foot.Children.Add(install);
        var stats = Ui.Row(12,
            Ui.Row(4, Ui.Icon(Icons.Download, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(p.Downloads), "small muted")),
            Ui.Row(4, Ui.Icon(Icons.Heart, 12, Ui.Res("Muted")), Ui.Text(I18n.Compact(p.Follows), "small muted")));
        if (p.Updated is not null) ToolTip.SetTip(stats, I18n.T("mine.updatedAgo", ("ago", Ui.Ago(p.Updated))));
        stats.VerticalAlignment = VerticalAlignment.Center;
        stats.ClipToBounds = true;
        stats.Margin = new Thickness(0, 0, 8, 0);
        foot.Children.Add(stats);
        body.Children.Add(foot);
        var card = new Button
        {
            Classes = { "tile" }, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = body,
        };
        card.Click += (_, _) => Details(p);
        return Ctx.Attach(card, () => Ctx.Menu(
            Ctx.Item(I18n.T("ctx.open"), Icons.Eye, () => Details(p)),
            Ctx.Item(pack ? I18n.T("mine.pack.install") : I18n.T("mr.install"), Icons.Download, () => Install(p), !busy && !installed),
            Ctx.Item(I18n.T("mr.versions"), Icons.Layers, () => Details(p)),
            "-",
            Ctx.Link(I18n.T("ctx.openSite"), p.Url),
            Ctx.Copy(I18n.T("ctx.copyLink"), p.Url),
            Ctx.Copy(I18n.T("ctx.copyName"), p.Title)));
    }

    void Install(MrProject p, MrVersion? v = null)
    {
        if (KindOf(p) == "modpack") { InstallPack(p, v); return; }
        var a = Mc.Active;
        if (a is null) { CreateDialog(); return; }
        _busy.Add(p.Id);
        Build();
        Jobs.Run(p.Title, "Minecraft", async (_, progress, ct) =>
        {
            try
            {
                var done = await McModrinth.Install(a, p, v, progress, ct);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    W.Toast(done.Count > 1 ? I18n.T("mr.installedMany", ("title", p.Title), ("n", done.Count - 1)) : I18n.T("mr.installedOne", ("title", p.Title)));
                    if (KindOf(p) == "shader" && a.Modded && !HasShaderMod(a)) W.Toast(I18n.T("mine.hint.shaders", ("mod", ShaderMod(a).Name)));
                });
            }
            finally
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { _busy.Remove(p.Id); if (MainWindow.Current?.CurrentPage == this) Build(); Mc.Notify(); });
            }
        });
    }

    /// <summary>Модпак — это новая сборка: скачиваем .mrpack и всё, что в нём, ставим загрузчик.</summary>
    void InstallPack(MrProject p, MrVersion? v)
    {
        _busy.Add(p.Id);
        Build();
        W.Toast(I18n.T("mine.pack.started", ("title", p.Title)));
        Jobs.Run(p.Title, "Minecraft", async (_, progress, ct) =>
        {
            try
            {
                var i = await McModrinth.InstallPack(p, v, progress, ct);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Mc.Active = i;
                    W.Toast(I18n.T("mine.pack.done", ("title", i.Name)));
                    Mc.Notify();
                });
            }
            finally
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { _busy.Remove(p.Id); if (MainWindow.Current?.CurrentPage == this) Build(); });
            }
        });
    }

    /// <summary>Карточка проекта: описание, снимки и версии, которые подходят сборке.</summary>
    async void Details(MrProject p)
    {
        var a = Mc.Active;
        var kind = KindOf(p);
        var body = Ui.Col(12,
            Ui.Row(14, Ui.Thumb(p.Icon, p.Title, 64, 16, 128), Ui.Col(4, Ui.Text(I18n.T("mod.by", ("author", p.Author)), "brand"), Ui.Text(p.Description, "muted", wrap: true))),
            Ui.Text(I18n.T("common.loading"), "small muted"));
        ((StackPanel)body.Children[0]).Children[1].MaxWidth = 620;
        var list = Ui.Col(6);
        W.Dialog(p.Title, new ScrollViewer { MaxHeight = 580, Content = Ui.Col(14, body, list) }, 780,
            Ui.Button(I18n.T("ctx.openSite"), () => Ui.OpenUrl(p.Url), "ghost", Icons.External),
            Ui.Button(I18n.T("common.close"), W.CloseDialog),
            Ui.Button(kind == "modpack" ? I18n.T("mine.pack.install") : I18n.T("mr.install"), () => { W.CloseDialog(); Install(p); }, "primary", Icons.Download));
        if (Program.Demo) { body.Children.RemoveAt(1); return; }
        try
        {
            var (_, text, gallery) = await Modrinth.Project(p.Id);
            body.Children.RemoveAt(1);
            if (gallery.Count > 0)
            {
                var shots = Ui.Row(8);
                foreach (var g in gallery.Take(6)) shots.Children.Add(new Border { Width = 200, Height = 112, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = Ui.Thumb(g, p.Title, 200, 10, 400) });
                body.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = shots });
            }
            var plain = System.Text.RegularExpressions.Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)|<[^>]+>|\[!\[.*?\)\]\(.*?\)", "").Trim();
            if (plain.Length > 0) body.Children.Add(new SelectableTextBlock { Text = plain.Length > 3000 ? plain[..3000] + "…" : plain, TextWrapping = TextWrapping.Wrap, FontSize = 13 });

            var versions = await McModrinth.Versions(p.Id, kind, kind == "modpack" ? null : a);
            list.Children.Add(Ui.Text(versions.Count == 0
                ? (a is null || kind == "modpack" ? I18n.T("mr.noMatching") : I18n.T("mine.err.noVersion", ("title", p.Title), ("build", a.Label)))
                : kind == "modpack" || a is null ? I18n.T("mr.versions") : I18n.T("mine.versionsFor", ("label", a.Label)), "h3"));
            foreach (var v in versions.Take(25))
            {
                var vv = v;
                var line = new DockPanel();
                var go = Ui.Button(I18n.T("mr.install"), () => { W.CloseDialog(); Install(p, vv); }, "ghost", Icons.Download);
                DockPanel.SetDock(go, Dock.Right);
                line.Children.Add(go);
                var type = v.Type == "release" ? "" : v.Type + " · ";
                line.Children.Add(Ui.Col(2, Ui.Text($"{v.Number}  ·  {v.Name}"),
                    Ui.Text($"{type}{string.Join(", ", v.Loaders.Select(LoaderName))} · {string.Join(", ", v.GameVersions.TakeLast(4))} · {Ui.Ago(v.Published)}", "small muted")));
                list.Children.Add(Ui.Card(line, 10));
            }
        }
        catch (Exception e)
        {
            if (body.Children.Count > 1 && body.Children[1] is TextBlock) body.Children.RemoveAt(1);
            body.Children.Add(Ui.Text(Jobs.Explain(e), "small", color: Ui.Res("Bad"), wrap: true));
        }
    }
}
