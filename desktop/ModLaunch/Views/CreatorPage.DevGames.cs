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
    StackPanel? _dgGrid;
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

        // 26.0: поиск хаба — с лупой и тонкой рамкой (было голое поле, похожее на недоделанное).
        var search = HubLook.Search(I18n.T("v23.dg.search2"), _dgQuery);
        _dgCount = new TextBlock { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        search.TextChanged += (_, _) => { _dgQuery = search.Text ?? ""; _dgShown = 60; Fill(known, knownIds, steamMore); };
        var row = Ui.Row(0, search, _dgCount);
        if (_dgScanning) row.Children.Add(new TextBlock { Text = I18n.T("v23.dg.scanning"), Classes = { "small", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        root.Children.Add(row);

        _dgGrid = new StackPanel { Spacing = 4 };
        root.Children.Add(_dgGrid);
        Fill(known, knownIds, steamMore);
        return root;
    }

    /// <summary>
    /// Сетка игр. 26.0: столбцы подстраиваются под ширину (StoreKit.Tiles, плитка от 240 px) и сетка доходит до правого
    /// края — раньше карточки были по 252 px в WrapPanel, и справа оставалась пустая полоса. «Показать ещё» — под сеткой.
    /// </summary>
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
        var shown = list.Take(_dgShown).ToList();
        // extra меньше высоты подписи (~118): обложка выходит около 2:1 — шапка Steam (460×215) почти не режется по бокам.
        if (shown.Count > 0) _dgGrid.Children.Add(StoreKit.Tiles(shown.Select(DevCard), 240, extra: 100));
        if (total > _dgShown)
        {
            var more = Ui.Button(I18n.T("v23.dg.more", ("n", (total - _dgShown).ToString("N0", I18n.Culture))), () => { _dgShown += 60; Fill(known, knownIds, steamMore); }, "ghost", Icons.ChevronDown);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            _dgGrid.Children.Add(more);
        }
        if (list.Count == 0) _dgGrid.Children.Add(HubLook.Empty(Icons.Search, I18n.T("v23.dg.none"), ""));
    }

    /// <summary>
    /// Цвет движка — 26.0: только точкой у тихого ярлыка (раньше — пять ярких плашек на сетке, а Unity был янтарным,
    /// как выбранный фильтр и главная кнопка).
    /// </summary>
    static string KindDot(string kind) => kind switch
    {
        "unity" => "#C9CED6",
        "il2cpp" => "#A99BFF",
        "unreal" => "#5BB8F5",
        "re" => "#F27A6B",
        "creation" => "#5BD68F",
        "renpy" => "#FF7BC8",
        "rpgmaker" => "#B5E05B",
        "source" => "#FF9F4A",
        "cyberpunk" => "#E8E05B",
        _ => "#6B7280",
    };

    /// <summary>Обложки нет: спокойный градиент по названию и буквы игры — как у обложек игр без картинки, а не голый кубик.</summary>
    static Control NoCover(string name)
    {
        var initials = string.Concat(name.Split(' ', '-', ':', '_').Where(w => w.Length > 0 && char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        var hue = (int)(name.Aggregate(17u, (h, c) => h * 31 + c) % 360);
        return new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Ui.HslColor(hue, 0.32, 0.34), 0), new GradientStop(Ui.HslColor((hue + 40) % 360, 0.3, 0.18), 1) },
            },
            Child = new TextBlock
            {
                Text = initials == "" ? "?" : initials, FontSize = 30, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>Почему пока нельзя — коротко: первая фраза причины (вся — в подсказке).</summary>
    static string? WhyLine(DevGame g)
    {
        if (g.Why is null) return null;
        // Общая причина «движок не поддерживается» у «Пока нельзя» уже сказана ярлыком — строку не повторяем.
        if (g.Why == "v23.dev.no.engine") return g.Kind == "none" ? null : I18n.T("v23.dg.k.none");
        var text = I18n.T(g.Why, ("ac", "")).Trim();
        var end = text.IndexOfAny(['.', '!', '?', '。']);
        return end > 0 && end < text.Length - 1 ? text[..(end + 1)] : text;
    }

    /// <summary>
    /// Карточка игры: обложка, имя (точка — можно играть разработчиком), движок и меню под игру, одна кнопка.
    /// 26.0: та же карточка, что у остальных листов хаба — фон листа, тонкая линия, радиус 10; ширина — от сетки.
    /// </summary>
    Control DevCard(DevGame g)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var cover = new Border
        {
            CornerRadius = new CornerRadius(10, 10, 0, 0), ClipToBounds = true,
            Child = new Panel { Children = { NoCover(g.Name), image } },
        };
        if (!string.IsNullOrEmpty(g.Art))
            _ = Images.FromUrl(g.Art, 400).ContinueWith(t => { if (t.Result is Bitmap bmp) Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bmp); });
        var ready = g.Installed && g.Why is null;
        var text = new StackPanel { Spacing = 8, Margin = new Thickness(14, 11, 14, 14) };
        var name = new TextBlock { Text = g.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(name, g.Name);
        var title = new DockPanel();
        if (ready)
        {
            var dot = Ui.Dot(Ui.Res("Good"), 7);
            dot.Margin = new Thickness(0, 0, 7, 0);
            ToolTip.SetTip(dot, I18n.T("v26.design.dg.ready"));
            DockPanel.SetDock(dot, Dock.Left);
            title.Children.Add(dot);
        }
        title.Children.Add(name);
        text.Children.Add(title);
        // Ярлыки одной строкой: движок (с точкой его цвета) и, если уже известно, меню под эту игру (по меткам Steam).
        var tags = new DockPanel { LastChildFill = true };
        var kindTag = HubLook.BadgeDot(I18n.T("v23.dg.k." + g.Kind), Ui.Hex(KindDot(g.Kind)));
        kindTag.Margin = new Thickness(0, 0, 6, 0);
        ToolTip.SetTip(kindTag, I18n.T("v23.dg.w." + g.Kind));
        DockPanel.SetDock(kindTag, Dock.Left);
        tags.Children.Add(kindTag);
        if (g.Kind == "unity" && g.SteamAppId > 0 && GameProfile.Cached(g.SteamAppId) is { } profile)
        {
            var menu = HubLook.Badge(I18n.T("v23.gp." + profile.Preset));
            ToolTip.SetTip(menu, I18n.T("v23.gp.tip"));
            tags.Children.Add(menu);
        }
        else tags.Children.Add(new Border());
        text.Children.Add(tags);
        Control action;
        if (g.Why is not null)
        {
            var line = WhyLine(g);
            var why = new DockPanel { MinHeight = 34, Background = Brushes.Transparent };
            ToolTip.SetTip(why, I18n.T(g.Why, ("ac", "")));
            if (line is not null)
            {
                var info = Ui.Icon(Icons.Info, 13, Ui.Res("Faint"));
                info.Margin = new Thickness(0, 2, 7, 0);
                info.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(info, Dock.Left);
                why.Children.Add(info);
                why.Children.Add(new TextBlock { Text = line, Classes = { "small" }, Foreground = Ui.Res("Faint"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
            }
            action = why;
        }
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
        else action = new TextBlock { Text = I18n.T("v23.dg.notInstalled"), Classes = { "small" }, Foreground = Ui.Res("Faint"), MinHeight = 34, Padding = new Thickness(0, 9, 0, 0) };
        text.Children.Add(action);
        // Текст — снизу по своей высоте, обложка забирает остальное (высоту карточки задаёт сетка).
        var body = new DockPanel();
        DockPanel.SetDock(text, Dock.Bottom);
        body.Children.Add(text);
        body.Children.Add(cover);
        return new Border
        {
            CornerRadius = new CornerRadius(10), Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), ClipToBounds = true,
            Child = body,
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
