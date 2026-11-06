using System.Diagnostics;
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
/// «Конструктор в играх». 23.6: игры, в которые ModLaunch встраивает режим разработчика. 23.7: весь Steam (139 тыс. игр):
/// · установленные — движок узнаём по файлам (Unity, IL2CPP, Unreal, RE, Bethesda, Cyberpunk, Ren'Py, RPG Maker, Source),
///   в том числе игры, которых ещё нет в библиотеке ModLaunch (добавляются по кнопке сами);
/// · игры справочника — движок известен заранее (загрузчик модов);
/// · остальные игры Steam — движок станет известен после установки (по файлам игры), кнопка «Установить».
/// У каждой игры своё меню конструктора (GameProfile): на карточке видно, какое.
/// </summary>
public sealed partial class CreatorPage
{
    public sealed record DevGame(string Key, string Name, string Kind, string? Art, int SteamAppId, GameState? State, string? Why, FoundGame? Found = null)
    {
        public bool Installed => State?.Status == Detect.Found || Found is not null;
    }

    string _dgKind = "all";
    string _dgQuery = "";
    int _dgShown = 60;
    WrapPanel? _dgGrid;
    TextBlock? _dgCount;
    static List<DevGame>? _dgScanned;
    static bool _dgScanning;

    /// <summary>Движки с режимом разработчика. Ещё два вида: auto — движок узнаем после установки, none — пока нельзя.</summary>
    static readonly string[] DevKinds = ["unity", "il2cpp", "unreal", "re", "creation", "cyberpunk", "renpy", "rpgmaker", "source"];

    static string KindOfDef(GameDef def) => def.Id == "cyberpunk-2077" ? "cyberpunk"
        : def.Loader == LoaderKind.Bepinex ? "unity"
        : def.Engine is "unreal" or "re" or "creation" ? def.Engine
        : "";

    static (string Kind, string? Why) KindOnDisk(string path)
    {
        var why = DevMode.Unsupported(path);
        return why is null ? (DevMode.KindOf(path), null) : ("none", why);
    }

    /// <summary>Игры с известным движком: библиотека ModLaunch, найденные на диске и справочник. Сначала установленные.</summary>
    public static List<DevGame> DevGames()
    {
        var list = new List<DevGame>();
        var steam = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(DevGame g)
        {
            list.Add(g);
            if (g.SteamAppId > 0) steam.Add(g.SteamAppId);
            names.Add(g.Name);
        }
        foreach (var g in AppState.Games)
        {
            if (g.Def.IsMinecraft) continue;
            string kind;
            string? why = null;
            if (g.Status == Detect.Found && g.Path is not null)
            {
                (kind, why) = KindOnDisk(g.Path);
                // Движок известен по справочнику, но сейчас нельзя (античит, нет файлов) — показываем движок и причину.
                if (why is not null && KindOfDef(g.Def) is { Length: > 0 } byDef) kind = byDef;
            }
            else kind = KindOfDef(g.Def);
            if (kind == "") continue;
            Add(new DevGame(g.Def.Id, g.Def.Name, kind, g.Def.ArtUrl ?? (g.Def.SteamAppId > 0 ? GameDef.SteamArt(g.Def.SteamAppId) : null), g.Def.SteamAppId, g, why));
        }
        foreach (var g in _dgScanned ?? [])
            if (!(g.SteamAppId > 0 && steam.Contains(g.SteamAppId)) && !names.Contains(g.Name)) Add(g);
        foreach (var d in GameDirectory.All.OrderByDescending(d => d.Mods))
        {
            if (d.Loader is not ("bepinex" or "bepinex-il2cpp" or "melonloader")) continue;
            if (d.SteamAppId > 0 && steam.Contains(d.SteamAppId) || names.Contains(d.Name)) continue;
            Add(new DevGame("dir:" + d.Key, d.Name, d.Loader == "bepinex-il2cpp" ? "il2cpp" : "unity", d.Art, d.SteamAppId, null, null));
        }
        return list.OrderBy(x => x.Installed ? x.Why is null ? 0 : 1 : 2).ToList();
    }

    /// <summary>Установленные игры, которых нет в библиотеке (Steam, Epic, GOG): движок по файлам — в фоне.</summary>
    void ScanDisk()
    {
        if (_dgScanned is not null || _dgScanning) return;
        _dgScanning = true;
        _ = Task.Run(async () =>
        {
            var result = new List<DevGame>();
            try
            {
                foreach (var f in await CustomGames.Scan())
                {
                    var (kind, why) = KindOnDisk(f.Path);
                    result.Add(new DevGame("scan:" + f.Path, f.Name, kind, f.AppId > 0 ? GameDef.SteamArt(f.AppId) : null, f.AppId, null, why, f));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            _dgScanned = result;
            _dgScanning = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_dgGrid is not null && Avalonia.VisualTree.VisualExtensions.GetVisualRoot(_dgGrid) is not null) Build(); });
            // Меню под игру для установленных — заранее (метки Steam и цвет обложки, по одной игре, с кэшем).
            var fetched = 0;
            foreach (var g in DevGames().Where(g => g.Installed && g.Why is null && g.SteamAppId > 0 && GameProfile.Cached(g.SteamAppId) is null).Take(40))
                try { await GameProfile.For(g.Name, g.SteamAppId); fetched++; } catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException) { }
            if (fetched > 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_dgGrid is not null && Avalonia.VisualTree.VisualExtensions.GetVisualRoot(_dgGrid) is not null) Build(); });
        });
    }

    Control DevGamesView()
    {
        ScanDisk();
        var known = DevGames();
        var knownIds = known.Where(g => g.SteamAppId > 0).Select(g => g.SteamAppId).ToHashSet();
        var steamMore = SteamCatalog.Ready ? SteamCatalog.ExtraOver(knownIds) : 0;
        var root = new StackPanel { Spacing = 14 };
        root.Children.Add(Ui.Text(I18n.T("v23.dg.lead2"), "muted", wrap: true));

        // Фильтры: все игры, установленные, движки (по числу игр) и «узнаем после установки».
        var chips = new WrapPanel();
        void Chip(string id, string label, int n)
        {
            var b = new Button { Content = label + "  " + n.ToString("N0", I18n.Culture), Classes = { "chip" }, Margin = new Thickness(0, 0, 6, 6) };
            if (_dgKind == id) b.Classes.Add("active");
            b.Click += (_, _) => { _dgKind = id; _dgShown = 60; Build(); };
            chips.Children.Add(b);
        }
        Chip("all", I18n.T("v23.dg.f.all"), known.Count + steamMore);
        Chip("installed", I18n.T("v23.dg.f.installedChip"), known.Count(g => g.Installed && g.Why is null));
        foreach (var k in DevKinds)
        {
            var n = known.Count(g => g.Kind == k);
            if (n > 0) Chip(k, I18n.T("v23.dg.k." + k), n);
        }
        if (steamMore > 0) Chip("auto", I18n.T("v23.dg.k.auto"), steamMore);
        root.Children.Add(chips);

        var search = new TextBox { Watermark = I18n.T("v23.dg.search2"), Text = _dgQuery, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        _dgCount = new TextBlock { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        search.TextChanged += (_, _) => { _dgQuery = search.Text ?? ""; _dgShown = 60; Fill(known, knownIds, steamMore); };
        var row = Ui.Row(0, search, _dgCount);
        if (_dgScanning) row.Children.Add(new TextBlock { Text = I18n.T("v23.dg.scanning"), Classes = { "small", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        root.Children.Add(row);

        _dgGrid = new WrapPanel();
        root.Children.Add(_dgGrid);
        Fill(known, knownIds, steamMore);
        return root;
    }

    void Fill(List<DevGame> known, HashSet<int> knownIds, int steamMore)
    {
        if (_dgGrid is null) return;
        var q = _dgQuery.Trim();
        var list = _dgKind == "auto" ? [] : known.Where(g => (_dgKind == "all" || _dgKind == g.Kind || _dgKind == "installed" && g.Installed && g.Why is null)
            && (q == "" || g.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        var total = list.Count;
        // Весь Steam: при поиске — совпадения по названию, без поиска — по популярности (столько, сколько видно).
        if (_dgKind is "all" or "auto" && SteamCatalog.Ready)
        {
            var steam = q == "" ? SteamCatalog.Popular(0, Math.Max(0, _dgShown - list.Count), knownIds) : SteamCatalog.Search(q, 200, knownIds);
            total += q == "" ? steamMore : steam.Count;
            list.AddRange(steam.Select(d => new DevGame("steam:" + d.SteamAppId, d.Name, "auto", GameDef.SteamArt(d.SteamAppId), d.SteamAppId, null, null)));
        }
        if (_dgCount is not null) _dgCount.Text = I18n.T("v23.dg.count", ("n", total.ToString("N0", I18n.Culture)));
        _dgGrid.Children.Clear();
        foreach (var g in list.Take(_dgShown)) _dgGrid.Children.Add(DevCard(g));
        if (total > _dgShown)
        {
            var more = Ui.Button(I18n.T("v23.dg.more", ("n", (total - _dgShown).ToString("N0", I18n.Culture))), () => { _dgShown += 60; Fill(known, knownIds, steamMore); }, "ghost", Icons.ChevronDown);
            more.Margin = new Thickness(0, 10, 0, 0);
            _dgGrid.Children.Add(more);
        }
        if (list.Count == 0) _dgGrid.Children.Add(Ui.Text(I18n.T("v23.dg.none"), "muted"));
    }

    static (string Bg, string Fg) KindColor(string kind) => kind switch
    {
        "unity" => ("#3A2A0E", "#F0B429"),
        "il2cpp" => ("#1E1A3A", "#A99BFF"),
        "unreal" => ("#10283A", "#5BB8F5"),
        "re" => ("#3A1414", "#F27A6B"),
        "creation" => ("#123A26", "#5BD68F"),
        "renpy" => ("#3A1630", "#FF7BC8"),
        "rpgmaker" => ("#2A3412", "#B5E05B"),
        "source" => ("#3A2412", "#FF9F4A"),
        "auto" or "none" => ("#24262C", "#9AA3B2"),
        _ => ("#3A3A10", "#E8E05B"),
    };

    /// <summary>Карточка игры: обложка, имя, движок и меню под игру, одна кнопка.</summary>
    Control DevCard(DevGame g)
    {
        const double w = 252;
        var image = new Image { Stretch = Stretch.UniformToFill };
        var cover = new Border
        {
            Width = w, Height = 118, CornerRadius = new CornerRadius(12, 12, 0, 0), ClipToBounds = true, Background = Ui.Res("Surface3"),
            Child = new Panel { Children = { Ui.Icon(Icons.Cube, 28, Ui.Res("Faint")), image } },
        };
        if (!string.IsNullOrEmpty(g.Art))
            _ = Images.FromUrl(g.Art, 300).ContinueWith(t => { if (t.Result is Bitmap bmp) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bmp); });
        var (bg, fg) = KindColor(g.Kind);
        var text = new StackPanel { Spacing = 8, Margin = new Thickness(14, 11, 14, 14) };
        text.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        var tags = new WrapPanel();
        var kindTag = ModRow.Tag(I18n.T("v23.dg.k." + g.Kind), Ui.Soft(bg, fg), Ui.Ink(fg));
        kindTag.Margin = new Thickness(0, 0, 6, 0);
        ToolTip.SetTip(kindTag, I18n.T("v23.dg.w." + g.Kind));
        tags.Children.Add(kindTag);
        // Меню под эту игру (по меткам Steam) — если уже известно.
        if (g.Kind == "unity" && g.SteamAppId > 0 && GameProfile.Cached(g.SteamAppId) is { } profile)
        {
            var menu = ModRow.Tag(I18n.T("v23.gp." + profile.Preset), Ui.Soft("#1F2229", profile.Accent), Ui.Ink(profile.Accent));
            ToolTip.SetTip(menu, I18n.T("v23.gp.tip"));
            tags.Children.Add(menu);
        }
        text.Children.Add(tags);
        Control action;
        if (g.Why is not null)
            action = new TextBlock { Text = I18n.T(g.Why, ("ac", "")), Classes = { "small" }, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap, MaxHeight = 34 };
        else if (g.State is { Status: Detect.Found } st) action = Wide(Ui.Button(I18n.T("v23.dev.play"), () => DevLaunch.Start(st), "primary", Icons.Play));
        else if (g.Found is { } found)
            action = Wide(Ui.Button(I18n.T("v23.dev.play"), async () =>
            {
                // Игры ещё нет в библиотеке — добавить (с папкой и движком) и сразу запустить как разработчик.
                var state = await CustomGames.Add(found);
                _dgScanned = null;
                DevLaunch.Start(state);
            }, "primary", Icons.Play));
        else if (g.SteamAppId > 0)
            action = Wide(Ui.Button(I18n.T("v23.dg.install"), () =>
            {
                try { Process.Start(new ProcessStartInfo($"steam://install/{g.SteamAppId}") { UseShellExecute = true }); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }, "ghost", Icons.Download));
        else action = new TextBlock { Text = I18n.T("v23.dg.notInstalled"), Classes = { "small" }, Foreground = Ui.Res("Faint") };
        text.Children.Add(action);
        return new Border
        {
            Width = w, Margin = new Thickness(0, 0, 14, 14), CornerRadius = new CornerRadius(12), Background = Ui.Res("Surface2"),
            BorderBrush = g.Installed && g.Why is null ? new SolidColorBrush(Color.Parse(fg), 0.55) : Ui.Res("Line"), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { cover, text } },
        };
    }

    static Control Wide(Button b)
    {
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        return b;
    }

    [SelfTest]
    static string DevGamesListIsBig()
    {
        var list = DevGames();
        if (list.Count < 100) throw new Exception("only " + list.Count + " games with a known engine");
        var kinds = list.GroupBy(g => g.Kind).ToDictionary(g => g.Key, g => g.Count());
        foreach (var k in new[] { "unity", "il2cpp", "unreal", "re", "creation", "cyberpunk" })
            if (!kinds.ContainsKey(k)) throw new Exception("no " + k + " games");
        var texts = I18n.T("v23.dev.route") != "v23.dev.route"; // в --selfcheck без окна строк нет
        foreach (var k in texts ? DevKinds.Append("auto").Append("none") : [])
            foreach (var p in new[] { "v23.dg.k.", "v23.dg.w." })
                if (I18n.T(p + k) == p + k) throw new Exception("no text " + p + k);
        foreach (var key in !texts ? [] : new[] { "v23.dg.lead2", "v23.dg.search2", "v23.dg.f.installedChip", "v23.dg.install", "v23.dg.scanning", "v23.gp.tip", "v23.rpy.hint", "v23.rpgm.hint", "v23.src.hint" })
            if (I18n.T(key) == key) throw new Exception("no text " + key);
        foreach (var kind in texts ? new[] { "renpy", "rpgmaker", "source" } : [])
            foreach (var what in new[] { "route", "inside", "started", "preparing" })
                if (I18n.T($"v23.dev.{kind}.{what}") == $"v23.dev.{kind}.{what}") throw new Exception($"no text v23.dev.{kind}.{what}");
        if (list.Select(g => g.Key).Distinct().Count() != list.Count) throw new Exception("duplicates");
        SteamCatalog.EnsureLoaded().GetAwaiter().GetResult();
        var steam = SteamCatalog.ExtraOver(list.Where(g => g.SteamAppId > 0).Select(g => g.SteamAppId));
        if (list.Count + steam < 130_000) throw new Exception("Steam catalog: " + steam);
        if (SteamCatalog.Search("portal", 20).Count == 0) throw new Exception("Steam search");
        return list.Count + " games with a known engine (" + string.Join(", ", kinds.Select(kv => kv.Key + " " + kv.Value)) + ") + " + steam + " Steam games";
    }
}
