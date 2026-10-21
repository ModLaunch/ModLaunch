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
/// 12.0: обзор проекта — статус (собран, проверен, менялся), последняя сборка и проверка в игре,
/// сборки по версиям, выпуски и, если проект выложен, цифры Hub и продажи маркета.
/// Без входа карточек Hub (если id неизвестен) и маркета просто нет — никаких «войдите, чтобы…».
/// </summary>
public sealed partial class CreatorPage
{
    /// <summary>Hub и маркет по проекту: что нашлось и ошибка (уже понятным текстом).</summary>
    sealed record ProjectStats(HubMod? Hub, string? HubError, Listing? Listing, int Sales, long Revenue, string? MarketError);

    static readonly Dictionary<string, ProjectStats> _projStats = [];
    static readonly HashSet<string> _projLoading = [];

    static void ForgetStats(string projectId) => _projStats.Remove(projectId);

    Control Overview(Project p)
    {
        var meta = p.Meta();
        var edited = File.Exists(p.Script) ? File.GetLastWriteTimeUtc(p.Script) : p.Updated;
        var status = meta.Status(edited);
        var game = GameCatalog.ById(p.Game);
        var version = File.Exists(p.Script) ? ModScript.Compile(File.ReadAllText(p.Script)).Version : p.Version;
        var stats = _projStats.GetValueOrDefault(p.Id);
        if (stats is null && !Program.Screenshot) _ = LoadStats(p, meta);

        var col = new StackPanel { Spacing = 16 };
        col.Children.Add(StatusCard(p, game, status, version, edited));

        // Четыре цифры: сборка, проверка, установка и Hub (или выпуски, если в Hub проекта нет).
        var kpis = new UniformGrid { Columns = 4 };
        void Kpi(string label, string value, string sub, IBrush? color = null)
        {
            var i = kpis.Children.Count;
            // 12.1: показания приборов — подпись «// СБОРКА», значение моноширинным, слева — тонкая фирменная черта.
            var rule = new Border { Width = 2, Background = HubLook.Signature(), CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 2, 12, 2) };
            var words = Ui.Col(5,
                HubLook.Eyebrow(label),
                new TextBlock { Text = value, FontFamily = HubLook.Mono, FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = color ?? Ui.Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = sub, FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            var dock = new DockPanel { Children = { rule, words } };
            DockPanel.SetDock(rule, Dock.Left);
            kpis.Children.Add(new Border
            {
                Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(14, 12),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 3 ? 0 : 6, 0), Child = dock,
            });
        }
        if (meta.LastBuild is { } b)
            Kpi(I18n.T("v12.pj.k.build"), b.Ok ? Since(b.Time) : I18n.T("v12.pj.k.build.bad"),
                b.Ok ? Dotted(b.Version, b.Warnings > 0 ? I18n.T("v12.pj.k.warnings", ("n", b.Warnings)) : I18n.T("v12.pj.k.clean")) : Dotted(b.Version, Since(b.Time)),
                b.Ok ? null : Ui.Res("Bad"));
        else Kpi(I18n.T("v12.pj.k.build"), "—", I18n.T("v12.pj.k.never"));
        if (meta.LastTest is { } t)
            Kpi(I18n.T("v12.pj.k.test"), I18n.T(t.Ok ? "v12.pj.k.test.ok" : "v12.pj.k.test.bad"),
                Dotted(I18n.T("v12.pj.ran", ("time", ProjectMeta.Duration(t.Seconds))), t.ByUser ? I18n.T("v12.pj.byUser") : t.Exit is { } code ? I18n.T("v12.pj.exit", ("code", code)) : "", Since(t.Time)),
                t.Ok ? Ui.Res("Good") : Ui.Res("Bad"));
        else Kpi(I18n.T("v12.pj.k.test"), "—", I18n.T("v12.pj.k.never"));
        if (meta.LastInstall is { } inst)
            Kpi(I18n.T("v12.pj.k.install"), inst.Version, Dotted(GameCatalog.ById(inst.Game)?.Name ?? inst.Game, Since(inst.Time)));
        else Kpi(I18n.T("v12.pj.k.install"), "—", I18n.T("v12.pj.k.never"));
        if (stats?.Hub is { } hub)
            Kpi("Hub", I18n.Compact(hub.Downloads) + " ↓", Dotted("♥ " + I18n.Compact(hub.Likes), "💬 " + I18n.Compact(hub.Comments), hub.Version));
        else
            Kpi(I18n.T("v12.pj.k.releases"), meta.Releases.Count.ToString(), meta.Releases.LastOrDefault() is { } r ? Dotted(r.Version, Since(r.Time)) : I18n.T("v12.pj.k.never"));
        col.Children.Add(kpis);

        // Слева — проверка и сборки (рабочий цикл), справа — что видят игроки: Hub, маркет, выпуски.
        var left = Ui.Col(16, Block(I18n.T("v12.pj.last"), Icons.Activity,
            meta.LastTest is { } last ? TestResult(last, LogPathFor(p), compact: false) : Ui.Text(I18n.T("v12.pj.hint"), "small muted", wrap: true)),
            BuildsCard(p, meta));
        var right = Ui.Col(16);
        if (HubCard(meta, stats) is { } hubCard) right.Children.Add(hubCard);
        if (MarketCard(stats) is { } marketCard) right.Children.Add(marketCard);
        right.Children.Add(ReleasesCard(meta));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        col.Children.Add(grid);
        return col;
    }

    /// <summary>Шапка обзора: картинка игры, имя, точка статуса и что с проектом делать дальше.</summary>
    static Control StatusCard(Project p, GameDef? game, string status, string version, DateTime edited)
    {
        Control art = game is not null
            ? Ui.GameImage(game, 200)
            : new Border { Background = Ui.Res("Surface3"), Child = Ui.Icon(Icons.Creator, 24, Ui.Res("Faint")) };
        var pic = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = art, VerticalAlignment = VerticalAlignment.Center };
        var color = StatusBrush(status);
        var pill = new Border
        {
            BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 3),
            Child = Ui.Row(7, Ui.Dot(color, 7), HubLook.MonoText(I18n.T($"v12.pj.st.{status}").ToUpper(I18n.Culture), 10.5, Ui.Res("Text"))),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock { Text = p.Name, FontFamily = Look.Display, FontSize = 20, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var words = Ui.Col(6,
            HubLook.Eyebrow(I18n.T("v12d.ov.project"), HubLook.Violet),
            Ui.Row(12, title, pill),
            Ui.Text(Dotted(version == "" ? "" : "v" + version, game?.Name ?? p.Game, I18n.T("cr.edited", ("when", Since(edited)))), "small muted"),
            Ui.Text(I18n.T($"v12.pj.st.{status}.text"), "small", color: status is "error" or "failed" ? Ui.Res("Bad") : Ui.Res("Muted"), wrap: true));
        words.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        grid.Children.Add(pic);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return HubLook.Frame(grid, new Thickness(18, 16));
    }

    static Control Block(string title, string icon, params Control[] body)
    {
        // 12.1: блок пульта — рамка с уголками и подписью «// НАЗВАНИЕ» моноширинным.
        var col = Ui.Col(12, Ui.Row(8, Ui.Icon(icon, 14, HubLook.Violet), HubLook.Eyebrow(title, Ui.Res("Text"))));
        foreach (var b in body) col.Children.Add(b);
        return HubLook.Frame(col, new Thickness(18, 16));
    }

    /// <summary>Сборки по версиям (_build/&lt;проект&gt;): последние Projects.KeepBuilds архивов.</summary>
    static Control BuildsCard(Project p, ProjectMeta meta)
    {
        var builds = Projects.Builds(p.Id);
        var list = Ui.Col(6);
        if (builds.Count == 0) list.Children.Add(Ui.Text(I18n.T("v12.pj.builds.none"), "small muted", wrap: true));
        foreach (var f in builds)
        {
            var file = f;
            var latest = meta.LastBuild?.Zip is { } zip && string.Equals(Path.GetFullPath(zip), file.FullName, StringComparison.OrdinalIgnoreCase);
            var name = HubLook.MonoText(file.Name, 11.5, Ui.Res("Text"));
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (latest) name.FontWeight = FontWeight.SemiBold;
            var right = Ui.Text(Dotted(GamePage.Size(file.Length), Since(file.LastWriteTimeUtc)), "small muted");
            var row = new DockPanel();
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var left = Ui.Row(8, Ui.Icon(Icons.Package, 14, latest ? HubLook.Violet : Ui.Res("Faint")), name);
            if (latest) left.Children.Add(new Border
            {
                BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1),
                Child = Ui.Text(I18n.T("v12.pj.builds.latest"), "small muted"),
            });
            row.Children.Add(left);
            Ctx.Attach(row, () => Ctx.Menu(Ctx.Folder(I18n.T("games.openFolder"), file.FullName)));
            list.Children.Add(row);
        }
        var folder = Ui.Button(I18n.T("v12.pj.builds.folder"), () => Actions.OpenFolder(Directory.CreateDirectory(Projects.BuildsDir(p.Id)).FullName), "ghost", Icons.Folder);
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        return Block(I18n.T("v12.pj.builds", ("n", Projects.KeepBuilds)), Icons.Layers, list, folder);
    }

    /// <summary>Hub: только если проект выложен (id известен) — цифры, загрузки по дням и «Открыть в Hub».</summary>
    static Control? HubCard(ProjectMeta meta, ProjectStats? stats)
    {
        if (stats?.Hub is not { } hub)
            return stats?.HubError is { } error && meta.HubId is not null ? Block("Hub", Icons.Globe, Ui.Text(error, "small muted", wrap: true)) : null;
        var row = Ui.Row(28,
            Gx.Stat(I18n.Compact(hub.Downloads), I18n.T("hub.stat.downloads"), null, 20),
            Gx.Stat(I18n.Compact(hub.Likes), I18n.T("hub.stat.likes"), null, 20),
            Gx.Stat(I18n.Compact(hub.Comments), I18n.T("hub.stat.comments"), null, 20));
        var body = new List<Control> { row };
        // Загрузки по дням — из своих снимков в project.json: без новых правил базы и запросов.
        var days = meta.Downloads.Values.ToList();
        if (Spark(days, 300, 44) is { } spark)
        {
            var week = days.Count > 7 ? days[^1] - days[^8] : days[^1] - days[0];
            body.Add(Ui.Col(4, spark, Ui.Text(I18n.T("v12.pj.hub.trend", ("n", I18n.Compact(Math.Max(0, week))), ("days", Math.Min(7, days.Count - 1))), "small muted")));
        }
        body.Add(Ui.Text(Dotted(I18n.T("v12.pj.hub.version", ("v", hub.Version)), I18n.T("cr.edited", ("when", Since(hub.Updated ?? hub.Created ?? DateTime.UtcNow)))), "small muted"));
        var open = Ui.Button(I18n.T("hub.open"), () => OpenMod(hub), "ghost", Icons.External);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        body.Add(open);
        return Block("Hub", Icons.Globe, body.ToArray());
    }

    /// <summary>Маркет: только с входом и если у проекта есть лот (по id из project.json или по названию).</summary>
    static Control? MarketCard(ProjectStats? stats)
    {
        if (!Account.SignedIn && !Program.Screenshot) return null;
        if (stats?.Listing is not { } l) return stats?.MarketError is { } error ? Block(I18n.T("v12.pj.market"), Icons.Bag, Ui.Text(error, "small muted", wrap: true)) : null;
        var row = Ui.Row(28,
            Gx.Stat(stats.Sales.ToString(), I18n.T("v12.pj.market.sales"), null, 20),
            Gx.Stat(Market.Money(stats.Revenue), I18n.T("v12.pj.market.revenue"), Ui.Res("Good"), 20),
            Gx.Stat(l.Free ? I18n.T("v12.pj.market.free") : Market.Money(l.Price), I18n.T("v12.pj.market.price"), null, 20));
        return Block(I18n.T("v12.pj.market"), Icons.Bag, row, Ui.Text(Dotted(l.Title, l.Active ? "" : I18n.T("v12.pj.market.hidden")), "small muted"));
    }

    static Control ReleasesCard(ProjectMeta meta)
    {
        var list = Ui.Col(6);
        if (meta.Releases.Count == 0) list.Children.Add(Ui.Text(I18n.T("v12.pj.releases.none"), "small muted", wrap: true));
        foreach (var r in Enumerable.Reverse(meta.Releases).Take(6))
        {
            var when = Ui.Text(Since(r.Time), "small muted");
            var row = new DockPanel();
            DockPanel.SetDock(when, Dock.Right);
            row.Children.Add(when);
            var v = HubLook.MonoText("v" + r.Version, 11.5, Ui.Res("Text"));
            row.Children.Add(Ui.Row(8, Ui.Icon(Icons.Upload, 14, Ui.Res("Faint")), v, Ui.Text(r.Where == "market" ? I18n.T("v12.pj.market") : "Hub", "small muted")));
            list.Children.Add(row);
        }
        return Block(I18n.T("v12.pj.releases"), Icons.Upload, list);
    }

    /// <summary>Загрузки по дням — тонкая линия; меньше двух дней — нечего рисовать.</summary>
    static Control? Spark(IReadOnlyList<long> values, double width, double height)
    {
        if (values.Count < 2) return null;
        var min = values.Min();
        var span = Math.Max(1, values.Max() - min);
        var points = values.Select((v, i) => new Point(i * width / (values.Count - 1), height - 3 - (v - min) * (height - 6) / span)).ToList();
        return new Canvas
        {
            Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left,
            Children = { new Avalonia.Controls.Shapes.Polyline { Points = points, Stroke = HubLook.Cyan, StrokeThickness = 2, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round } },
        };
    }

    /// <summary>Цифры Hub и маркета для обзора: в фоне, ошибки — через Firebase.Explain (подробности сервера — только владельцу).</summary>
    async Task LoadStats(Project p, ProjectMeta meta)
    {
        if (!_projLoading.Add(p.Id)) return;
        HubMod? hub = null;
        Listing? listing = null;
        string? hubError = null, marketError = null;
        var sales = 0;
        long revenue = 0;
        try
        {
            // Выложенный до 12.0 проект: id в Hub такой же, каким его делает Hub.Publish, — из автора и имени.
            var hubId = meta.HubId ?? (Account.SignedIn && Account.Uid is { } uid ? Hub.DocId(uid, p.Name) : null);
            if (hubId is not null)
            {
                try { hub = await Hub.Get(hubId); }
                catch (Exception e) { hubError = Firebase.Explain("creator", e); }
            }
            if (Account.SignedIn)
            {
                try
                {
                    var mine = await Market.Mine();
                    listing = mine.FirstOrDefault(l => l.Id == meta.ListingId) ?? mine.FirstOrDefault(l => string.Equals(l.Title, p.Name, StringComparison.OrdinalIgnoreCase));
                    if (listing is { Sales: > 0 })
                    {
                        var list = await Market.Sales(listing);
                        sales = list.Count;
                        revenue = list.Sum(s => s.Net);
                    }
                }
                catch (Exception e) { marketError = Market.Explain(e); }
            }
            if (hub is not null || listing is not null)
                ProjectMeta.Update(p.Dir, m =>
                {
                    if (hub is not null) { m.HubId = hub.Id; m.NoteDownloads(hub.Downloads, DateTime.UtcNow); }
                    if (listing is not null) m.ListingId = listing.Id;
                });
            _projStats[p.Id] = new ProjectStats(hub, hubError, listing, sales, revenue, meta.ListingId is null && listing is null ? null : marketError);
        }
        finally { _projLoading.Remove(p.Id); }
        if (_open?.Id == p.Id && _pane == "overview") Build();
    }

    /// <summary>Цвет точки статуса проекта (ProjectMeta.Status): проверен — зелёный, беда — красный, менялся — жёлтый.</summary>
    static IBrush StatusBrush(string status) => status switch
    {
        "tested" => Ui.Res("Good"),
        "error" or "failed" => Ui.Res("Bad"),
        "changed" => Ui.Res("Warn"),
        "built" => Ui.Res("Brand2"),
        _ => Ui.Res("Faint"),
    };

    /// <summary>«только что», «5 мин назад», «3 ч назад», дальше — по дням, как везде в программе.</summary>
    static string Since(DateTime when)
    {
        if (when == DateTime.MinValue) return "";
        var span = DateTime.UtcNow - when.ToUniversalTime();
        if (span.TotalMinutes < 1) return I18n.T("v12.pj.ago.now");
        if (span.TotalHours < 1) return I18n.T("v12.pj.ago.min", ("n", (int)span.TotalMinutes));
        if (span.TotalDays < 1) return I18n.T("v12.pj.ago.hours", ("n", (int)span.TotalHours));
        return Ui.Ago(when.ToUniversalTime());
    }

    static string Dotted(params string[] parts) => string.Join(" · ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
}
