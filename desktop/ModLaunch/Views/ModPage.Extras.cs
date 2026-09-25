using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>
/// Страница мода, часть 2 — то, за что любят сайты модов: все версии со списком
/// изменений, установка любой версии, другие моды автора, отслеживание,
/// скрытие мода или автора; у модов ModLaunch Hub — лайки, комментарии, жалоба.
/// </summary>
public sealed partial class ModPage
{
    List<VersionInfo>? _versions;
    string? _versionsError;
    List<ModInfo>? _byAuthor;
    HubMod? _hubMod;
    List<HubComment>? _comments;
    string _comment = "";
    bool _showAllVersions;

    async Task LoadExtras()
    {
        if (Program.Demo)
        {
            _versions =
            [
                new("1.4.0", DateTime.UtcNow.AddDays(-2), 18204, "Новые декорации для базы\nИсправлен вылет при сохранении", null, 0, 2_400_000, null),
                new("1.3.2", DateTime.UtcNow.AddDays(-19), 40551, "Совместимость с последним обновлением игры", null, 0, 2_350_000, null),
                new("1.3.0", DateTime.UtcNow.AddDays(-47), 61230, "", null, 0, 2_300_000, null),
            ];
            _byAuthor = [];
            _comments = _brief.Source == "hub"
                ? [new("c1", "u2", "Kira", "Отличный мод, поставил за минуту через ModLaunch!", DateTime.UtcNow.AddHours(-5)), new("c2", "u1", _brief.Author, "Спасибо! В следующей версии будет больше скинов.", DateTime.UtcNow.AddHours(-3))]
                : [];
            return;
        }
        var versions = Task.Run(async () =>
        {
            try { _versions = await Extras.Versions(_g.Def, _brief); }
            catch (Exception e) { _versions = []; _versionsError = Jobs.Explain(e); }
        });
        var author = Task.Run(async () =>
        {
            try { _byAuthor = await Extras.ByAuthor(_g.Def, _brief); } catch { _byAuthor = []; }
        });
        var hub = _brief.Source != "hub" ? Task.CompletedTask : Task.Run(async () =>
        {
            try { _hubMod = await Hub.Get(_brief.Id); _comments = await Hub.Comments(_brief.Id); } catch { _comments = []; }
        });
        await Task.WhenAll(versions, author, hub);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { Build(); MainWindow.Current?.RenderAside(); });
    }

    /// <summary>
    /// Под «Установить» — только избранное, лайк (у модов Hub) и меню «⋯»,
    /// а редкие действия (отслеживать, скрыть, пожаловаться…) — в меню.
    /// </summary>
    Control ActionRow(ModInfo mod, bool installed)
    {
        var row = Ui.Row(6);
        var fav = Favorites.Has(_g.Def.Id, mod.Id);
        var heart = Ui.Button("", () =>
        {
            var on = Favorites.Toggle(_g.Def.Id, mod);
            MainWindow.Current?.Toast(I18n.T(on ? "fav.added" : "fav.removed"));
            Build();
        }, "icon", null, I18n.T(fav ? "fav.remove" : "fav.add"));
        heart.Content = Ui.Icon(Icons.Heart, 18, fav ? Ui.Res("Bad") : null, fill: fav);
        row.Children.Add(heart);

        if (mod.Source == "hub")
        {
            var liked = Hub.Liked(mod.Id);
            row.Children.Add(Ui.Button($"👍 {I18n.Compact(_hubMod?.Likes ?? mod.Rating)}", async () =>
            {
                if (_hubMod is null) return;
                try
                {
                    var on = await Hub.ToggleLike(_hubMod);
                    _hubMod = _hubMod with { Likes = Math.Max(0, _hubMod.Likes + (on ? 1 : -1)) };
                    Build();
                }
                catch (Exception e) { MainWindow.Current?.Toast(Social.Firebase.Explain("creator", e), bad: true); }
            }, liked ? "primary" : "", null, I18n.T("hub.like")));
        }

        var menu = new MenuFlyout();
        void Item(string text, Action run)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
        var tracked = Tracking.Has(_g.Def.Id, mod);
        Item(tracked ? I18n.T("nx.untrack") : I18n.T("nx.track"), () =>
        {
            var on = Tracking.Toggle(_g.Def.Id, mod);
            MainWindow.Current?.Toast(I18n.T(on ? "nx.tracked" : "nx.untracked", ("name", mod.Name)));
            Build();
        });
        if (mod.Source == "nexus" && installed && !Actions.IsEndorsed(_g, mod.Id))
            Item(I18n.T("v4.endorse"), async () => { await Actions.Endorse(_g, mod.Id, mod.Version); Build(); });
        if (mod.Url is not null) Item(I18n.T("mod.page"), () => Ui.OpenUrl(mod.Url));
        if (mod.Source == "hub")
        {
            Item(I18n.T("hub.share"), async () =>
            {
                var url = $"https://modlaunch.github.io/ModLaunch/hub.html#mod={Uri.EscapeDataString(mod.Id)}";
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip) await clip.SetTextAsync(url);
                MainWindow.Current?.Toast(I18n.T("hub.shared"));
            });
            if (_hubMod is { IsPackage: true, Sha256.Length: 64 } pkg)
                Item(I18n.T("hub.virustotal"), () => Ui.OpenUrl($"https://www.virustotal.com/gui/file/{pkg.Sha256}"));
            Item(I18n.T("hub.report"), Report);
        }
        menu.Items.Add(new Separator());
        Item(I18n.T("nx.hide.mod"), () => { Blocklist.HideMod(_g.Def.Id, mod); MainWindow.Current?.Toast(I18n.T("nx.hidden")); });
        if (mod.Author != "") Item(I18n.T("nx.hide.author", ("author", mod.Author)), () => { Blocklist.HideAuthor(mod); MainWindow.Current?.Toast(I18n.T("nx.hidden")); });
        var more = new Button { Classes = { "icon" }, Content = new TextBlock { Text = "⋯", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Flyout = menu };
        ToolTip.SetTip(more, I18n.T("nx.more"));
        row.Children.Add(more);
        if (tracked) row.Children.Add(new TextBlock { Text = "🔔", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });
        return row;
    }

    // ---------------------------------------------------------------- файлы и версии

    Control VersionsCard()
    {
        var col = Ui.Col(10, Ui.Row(10, Ui.Icon(Icons.Layers, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("nx.files"), "h2")));
        if (_versions is null) { col.Children.Add(Ui.Text(I18n.T("common.loading"), "muted")); return Ui.Card(col, 22); }
        if (_versions.Count == 0)
        {
            col.Children.Add(Ui.Text(_versionsError ?? I18n.T("nx.files.none"), "muted", wrap: true));
            return Ui.Card(col, 22);
        }
        var installedVersion = _g.Registry?.Get(_g.Def.RecordId(_brief.Id, _brief.Source))?.Str("version");
        var list = _showAllVersions ? _versions : _versions.Take(5).ToList();
        foreach (var v in list)
        {
            var vv = v;
            var current = installedVersion is not null && installedVersion == v.Version;
            var install = current
                ? Ui.Button("✓ " + I18n.T("nx.files.current"), () => { }, "ghost")
                : Ui.Button(v == _versions[0] ? I18n.T("mod.install") : I18n.T("nx.files.install"), () => _ = InstallVersion(vv), v == _versions[0] ? "primary" : "", Icons.Download);
            install.IsEnabled = !current && _g.Status == Detect.Found;
            install.VerticalAlignment = VerticalAlignment.Top;
            var facts = new List<string>();
            if (v.Date is not null) facts.Add(Ui.Ago(v.Date));
            if (v.Size > 0) facts.Add(GamePage.Size(v.Size));
            if (v.Downloads > 0) facts.Add("↓ " + v.Downloads.ToString("N0"));
            var info = Ui.Col(4,
                Ui.Row(8, Ui.Text(v.Version, "h3"), v == _versions[0] ? ModRow.Tag(I18n.T("nx.files.latest"), Ui.Res("BrandSoft"), Ui.Res("Brand2")) : new Control()),
                Ui.Text(string.Join(" · ", facts), "small muted"));
            if (v.Changelog.Trim() != "")
                info.Children.Add(new SelectableTextBlock { Text = v.Changelog.Trim(), TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted"), FontSize = 12.5, MaxHeight = 160 });
            var row = new DockPanel();
            DockPanel.SetDock(install, Dock.Right);
            row.Children.Add(install);
            row.Children.Add(info);
            col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Child = row });
        }
        if (_versions.Count > 5 && !_showAllVersions)
            col.Children.Add(Ui.Button(I18n.T("nx.files.all", ("n", _versions.Count)), () => { _showAllVersions = true; Build(); }, "ghost", Icons.List));
        return Ui.Card(col, 22);
    }

    async Task InstallVersion(VersionInfo v)
    {
        if (v == _versions?.FirstOrDefault()) { await Actions.Install(_g, Mod); Build(); return; }
        var pin = _brief.Source switch
        {
            "nexus" => new Pin(v.FileId, v.Version, v.FileName),
            _ => new Pin(0, v.Version, v.Url),
        };
        await Actions.Install(_g, Mod, pin, reinstall: true);
        Build();
    }

    // ---------------------------------------------------------------- другие моды автора

    Control? AuthorCard()
    {
        if (_byAuthor is not { Count: > 0 }) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var m in _byAuthor)
        {
            var mm = m;
            var card = new Button
            {
                Classes = { "card-btn" }, Width = 190, Padding = new Thickness(12),
                Content = Ui.Col(8, Ui.Thumb(m.Icon, m.Name, 64, 12), Ui.Text(m.Name, "h3"),
                    Ui.Text(m.Downloads > 0 ? "↓ " + I18n.Compact(m.Downloads) : m.Version, "small muted")),
            };
            card.Click += (_, _) => MainWindow.Current?.Navigate(() => new ModPage(_g.Def.Id, mm));
            row.Children.Add(card);
        }
        return Ui.Card(Ui.Col(12, Ui.Text(I18n.T("nx.author", ("author", Mod.Author)), "h2"),
            new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }), 22);
    }

    // ---------------------------------------------------------------- комментарии (ModLaunch Hub)

    Control CommentsCard()
    {
        var count = _hubMod?.Comments ?? _comments?.Count ?? 0;
        var col = Ui.Col(12, Ui.Row(10, Ui.Icon(Icons.Chat, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("hub.comments", ("n", count)), "h2")));
        if (Social.Account.SignedIn)
        {
            var box = new TextBox { Text = _comment, Watermark = I18n.T("hub.comment.hint"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxLength = 1000 };
            box.TextChanged += (_, _) => _comment = box.Text ?? "";
            var send = Ui.Button(I18n.T("hub.comment.send"), async () =>
            {
                if (_hubMod is null || _comment.Trim() == "") return;
                try
                {
                    await Hub.AddComment(_hubMod, _comment);
                    _comment = "";
                    _comments = await Hub.Comments(_hubMod.Id);
                    _hubMod = _hubMod with { Comments = _hubMod.Comments + 1 };
                    Build();
                }
                catch (Exception e) { MainWindow.Current?.Toast(Social.Firebase.Explain("creator", e), bad: true); }
            }, "primary", Icons.Chat);
            send.HorizontalAlignment = HorizontalAlignment.Right;
            col.Children.Add(Ui.Col(8, box, send));
        }
        else col.Children.Add(Ui.Text(I18n.T("hub.comment.signin"), "muted", wrap: true));

        if (_comments is null) col.Children.Add(Ui.Text(I18n.T("common.loading"), "muted"));
        else if (_comments.Count == 0) col.Children.Add(Ui.Text(I18n.T("hub.comment.none"), "muted"));
        else
        {
            var authorUid = _hubMod?.Uid;
            foreach (var c in _comments)
            {
                var cc = c;
                var title = Ui.Row(8, Ui.Text(c.Author, "h3"), Ui.Text(Ui.Ago(c.Created), "small muted"));
                if (c.Uid == authorUid) title.Children.Insert(1, ModRow.Tag(I18n.T("hub.comment.author"), Ui.Res("BrandSoft"), Ui.Res("Brand2")));
                foreach (var x in title.Children) x.VerticalAlignment = VerticalAlignment.Center;
                var body = Ui.Col(6, title, new SelectableTextBlock { Text = c.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Muted") });
                if (c.Mine || (_hubMod?.Mine ?? false))
                {
                    var del = Ui.Button(I18n.T("hub.comment.delete"), async () =>
                    {
                        try { await Hub.DeleteComment(_hubMod!, cc); _comments?.Remove(cc); Build(); }
                        catch (Exception e) { MainWindow.Current?.Toast(Social.Firebase.Explain("creator", e), bad: true); }
                    }, "ghost", Icons.Trash);
                    del.HorizontalAlignment = HorizontalAlignment.Left;
                    body.Children.Add(del);
                }
                col.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = body });
            }
        }
        return Ui.Card(col, 22);
    }

    void Report()
    {
        var w = MainWindow.Current!;
        var reason = new TextBox { Watermark = I18n.T("hub.report.hint"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxLength = 500 };
        w.Dialog(I18n.T("hub.report"), Ui.Col(8, Ui.Text(I18n.T("hub.report.text"), "muted", wrap: true), reason),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("hub.report.send"), async () =>
            {
                if (_hubMod is null) return;
                try { await Hub.Report(_hubMod, reason.Text ?? ""); w.CloseDialog(); w.Toast(I18n.T("hub.report.done")); }
                catch (Exception e) { w.CloseDialog(); w.Toast(Social.Firebase.Explain("creator", e), bad: true); }
            }, "primary", Icons.Flag));
    }
}
