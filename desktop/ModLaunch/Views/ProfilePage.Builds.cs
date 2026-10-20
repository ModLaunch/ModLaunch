using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 11.0: «Часы по сборкам» — сколько человек провёл в каждой сборке (профиле модов, сборке Minecraft),
/// пороги 1 · 10 · 50 · 100 · 500 ч, и «Сохранить»: карточка PNG и полный JSON достижений
/// в «Документы\ModLaunch\Достижения». Те же данные уходят в облачный профиль и в бэкап.
/// </summary>
public sealed partial class ProfilePage
{
    static Control BuildsView()
    {
        var builds = BuildTime.All();
        var total = builds.Sum(b => b.TotalMs);
        var head = Ui.Col(10,
            Ui.Row(10, Ui.Icon(Icons.Hourglass, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("v11.bt.title"), "h2")),
            Ui.Text(I18n.T("v11.bt.lead"), "muted", wrap: true),
            Ui.Row(28,
                Gx.Stat(((long)(total / 3_600_000)).ToString(), I18n.T("v11.bt.stat.hours")),
                Gx.Stat(builds.Count.ToString(), I18n.T("v11.bt.stat.builds")),
                Gx.Stat(builds.Count(b => b.Hours >= 1).ToString(), I18n.T("v11.bt.stat.lived"))));
        var col = Ui.Col(12, Ui.Card(head, 22));
        if (builds.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Text(I18n.T("v11.bt.empty"), "muted", wrap: true), 20));
            return col;
        }
        var max = Math.Max(1, builds[0].TotalMs);
        foreach (var b in builds.Take(40)) col.Children.Add(BuildRow(b, max));
        return col;
    }

    static Control BuildRow(BuildPlay b, long max)
    {
        var def = GameCatalog.ById(b.Game);
        Control pic = def is null
            ? new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface2"), Child = Ui.Icon(Icons.Layers, 20, Ui.Res("Muted")) }
            : new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = Ui.GameImage(def, 160, art: Images.Art.Cover) };
        var (done, next) = BuildTime.Milestone(b);
        var tags = Ui.Row(6);
        if (b.Mods > 0) tags.Children.Add(StoreKit.Pill(I18n.T("v11.bt.mods", ("n", b.Mods)), onArt: false));
        if (b.Running) tags.Children.Add(Gx.Tag(I18n.T("v11.bt.now"), Ui.Res("Good"), Ui.Res("OnBrand"), 11));
        var facts = new List<string>();
        if (b.Sessions > 0) facts.Add(I18n.T("v11.bt.sessions", ("n", b.Sessions)));
        if (b.LongestMs > 0) facts.Add(I18n.T("v11.bt.longest", ("time", PlayTime.Format(b.LongestMs))));
        if (b.Last is { } last) facts.Add(I18n.T("v11.bt.last", ("ago", Ui.Ago(last))));

        // Пороги: открытые — цветом, следующий — с прогрессом.
        var steps = Ui.Row(6);
        foreach (var m in BuildTime.Milestones)
        {
            var reached = b.Hours >= m;
            steps.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2),
                Background = reached ? Ui.Res("BrandSoft") : Ui.Res("Surface2"),
                Child = new TextBlock { Text = I18n.T("v11.bt.h", ("n", m)), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = reached ? Ui.Res("Brand2") : Ui.Res("Faint") },
            });
        }
        var progress = done >= BuildTime.Milestones.Length ? 1 : Math.Clamp(b.Hours / next, 0, 1);
        var words = Ui.Col(6,
            Ui.Row(8, Ui.Text(b.Name, "h3"), tags),
            Ui.Text((def?.Name ?? b.Game) + (facts.Count > 0 ? " · " + string.Join(" · ", facts) : ""), "small muted"),
            new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = def is null ? Ui.Res("Brand") : Ui.Hex(def.Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(8, 420.0 * b.TotalMs / max) },
            steps);
        var hours = Ui.Col(2,
            new TextBlock { Text = PlayTime.Format(b.TotalMs), FontFamily = Gx.Display, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Ui.Res("Text"), HorizontalAlignment = HorizontalAlignment.Right },
            Ui.Text(done >= BuildTime.Milestones.Length ? I18n.T("v11.bt.allDone") : I18n.T("v11.bt.next", ("n", next), ("p", (int)(progress * 100))), "small muted"));
        hours.VerticalAlignment = VerticalAlignment.Center;
        ((TextBlock)hours.Children[1]).HorizontalAlignment = HorizontalAlignment.Right;
        var row = new DockPanel();
        DockPanel.SetDock(hours, Dock.Right);
        row.Children.Add(hours);
        var left = Ui.Row(14, pic, words);
        row.Children.Add(left);
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Child = row };
    }

    // ---------------------------------------------------------------- сохранить

    /// <summary>Папка «Документы\ModLaunch\Достижения».</summary>
    static string SaveDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (docs == "") docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.CreateDirectory(Path.Combine(docs, "ModLaunch", I18n.T("v11.bt.folder"))).FullName;
    }

    /// <summary>Всё о достижениях одним файлом: уровень, достижения, награды, часы по играм и сборкам.</summary>
    public static JsonObject Export(List<Achievement> all)
    {
        var (level, xp, _) = Achievements.Level(all);
        var achievements = new JsonArray();
        foreach (var a in all)
            achievements.Add(new JsonObject
            {
                ["id"] = a.Id, ["set"] = a.Set, ["title"] = a.Title, ["tier"] = a.Tier, ["done"] = a.Done, ["have"] = a.Have, ["need"] = a.Need,
                ["reward"] = a.Reward?.Key,
            });
        var builds = new JsonArray();
        foreach (var b in BuildTime.All())
            builds.Add(new JsonObject
            {
                ["game"] = b.Game, ["gameName"] = GameCatalog.ById(b.Game)?.Name ?? b.Game, ["build"] = b.Name, ["mods"] = b.Mods,
                ["hours"] = Math.Round(b.Hours, 2), ["sessions"] = b.Sessions, ["first"] = b.First?.ToString("o"), ["last"] = b.Last?.ToString("o"),
            });
        var games = new JsonObject();
        foreach (var g in AppState.Games)
            if (PlayTime.Get(g.Def.Id) is { TotalMs: > 0 } t) games[g.Def.Id] = new JsonObject { ["name"] = g.Def.Name, ["hours"] = Math.Round(t.TotalMs / 3_600_000.0, 2), ["sessions"] = t.Sessions };
        return new JsonObject
        {
            ["app"] = "ModLaunch", ["version"] = Http.Version, ["saved"] = DateTime.UtcNow.ToString("o"),
            ["player"] = ProfileKit.PlayerName(), ["level"] = level, ["xp"] = xp,
            ["done"] = all.Count(a => a.Done), ["total"] = all.Count,
            ["achievements"] = achievements, ["builds"] = builds, ["games"] = games,
            ["cosmetics"] = Settings.Data.Obj("profile").DeepClone(),
        };
    }

    /// <summary>Сохранить карточку PNG и JSON, открыть папку.</summary>
    static async Task SaveAll(List<Achievement> all)
    {
        var w = MainWindow.Current;
        try
        {
            var dir = SaveDir();
            var stamp = DateTime.Now.ToString("yyyy-MM-dd");
            var json = Path.Combine(dir, $"modlaunch-{stamp}.json");
            await File.WriteAllTextAsync(json, Export(all).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            var png = Path.Combine(dir, $"modlaunch-{stamp}.png");
            SaveCard(all, png);
            w?.Toast(I18n.T("v11.bt.saved", ("dir", dir)));
            if (!Program.Screenshot) Actions.OpenFolder(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            w?.Toast(e.Message, bad: true);
        }
    }

    /// <summary>Карточка 1200×630 (как превью ссылки): баннер, аватар, уровень, достижения и три главные сборки.</summary>
    public static void SaveCard(List<Achievement> all, string path)
    {
        var card = ShareCard(all);
        var size = new Size(1200, 630);
        card.Measure(size);
        card.Arrange(new Rect(size));
        using var bmp = new RenderTargetBitmap(new PixelSize(1200, 630), new Vector(96, 96));
        bmp.Render(card);
        bmp.Save(path);
    }

    static Control ShareCard(List<Achievement> all)
    {
        var (level, xp, _) = Achievements.Level(all);
        var name = ProfileKit.PlayerName();
        var white = Brushes.White;
        var soft = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        TextBlock T(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal, bool display = false) => new()
        {
            Text = text, FontSize = size, Foreground = brush, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis,
            FontFamily = display ? Gx.Display : FontFamily.Default,
        };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26 };
        top.Children.Add(ProfileKit.Avatar(name, 132, ring: 7));
        var who = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(T(name, 46, white, FontWeight.Bold, display: true));
        who.Children.Add(T(I18n.T("v98.level", ("n", level), ("xp", xp)) + " · " + I18n.T("v98.ach.count", ("done", all.Count(a => a.Done)), ("all", all.Count)), 20, soft));
        top.Children.Add(who);

        var rows = new StackPanel { Spacing = 14 };
        rows.Children.Add(T(I18n.T("v11.bt.title").ToUpper(I18n.Culture), 15, soft, FontWeight.Bold));
        var builds = BuildTime.All().Take(3).ToList();
        if (builds.Count == 0) rows.Children.Add(T(I18n.T("v11.bt.empty.short"), 20, soft));
        foreach (var b in builds)
        {
            var line = new DockPanel();
            var hours = T(PlayTime.Format(b.TotalMs), 26, white, FontWeight.Bold, display: true);
            DockPanel.SetDock(hours, Dock.Right);
            line.Children.Add(hours);
            line.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children = { T(b.Name, 24, white, FontWeight.SemiBold), T(GameCatalog.ById(b.Game)?.Name ?? b.Game, 16, soft) },
            });
            rows.Children.Add(line);
        }

        var body = new DockPanel { Margin = new Thickness(56, 48) };
        var brand = T("ModLaunch", 18, soft, FontWeight.Bold, display: true);
        brand.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(brand, Dock.Bottom);
        body.Children.Add(brand);
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);
        rows.Margin = new Thickness(0, 40, 0, 0);
        body.Children.Add(rows);
        return new Panel
        {
            Width = 1200, Height = 630,
            Children = { ProfileKit.Banner(Cosmetics.Equipped("banner")), new Border { Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)) }, body },
        };
    }
}
