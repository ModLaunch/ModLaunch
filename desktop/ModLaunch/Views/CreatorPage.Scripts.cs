using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using ModLaunch.Games;
using MX = ModScriptX;

namespace ModLaunch.Views;

/// <summary>
/// 18.0: раздел «Скрипты». Первая вкладка — библиотека чужих скриптов: купленные в Creator Hub и импортированные
/// файлом. Каждый автоматически проходит защиту (<see cref="ScriptGuard"/>: минимизация, компиляция со сверкой
/// в песочнице, упаковка) и шифруется под этот компьютер (<see cref="Vault"/>): исходник не открыть, не скопировать
/// и не изменить — подменённый файл не запустится. Свои .modx, подключённые из среды, остаются открытыми.
/// Остальные вкладки — консоль и справка ModScript (бывший раздел «ModScript»). Бывшие «Мои инжекты»
/// из «Магазин → Ещё» — здесь же.
/// </summary>
public sealed partial class CreatorPage
{
    /// <summary>Покупки прошлых версий защищаются один раз за сеанс — при первом открытии библиотеки.</summary>
    static bool _hardened;

    /// <summary>20.0: раздел ModScript X — обзор среды, консоль и справка; библиотека скриптов — в «Моих проектах».</summary>
    Control XHub() => _ms switch
    {
        "console" => Console(),
        "commands" => Commands(),
        "functions" => LibraryDocs(),
        _ => XHome(),
    };

    /// <summary>Главная кнопка «Скриптов»: импорт файла (.modx, .mls или архив).</summary>
    Control ImportAction() => Ui.Button(I18n.T("v18.lib.import.button"), () => _ = ImportScript(), "primary", Icons.Upload);

    Control Library()
    {
        if (!_hardened && !Program.Screenshot) { _hardened = true; Features.Inject.Harden(); }
        var all = Features.Inject.All().Where(i => _filter == "" || i.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var sealedOnes = all.Where(i => i.Sealed).ToList();
        var own = all.Where(i => !i.Sealed).ToList();
        var col = new StackPanel { Spacing = 16 };
        col.Children.Add(GuardHero(sealedOnes));

        if (!Features.Inject.On)
        {
            var on = Ui.Button(I18n.T("v18.lib.off.on"), () => { Features.Inject.SetOn(true); Build(); }, "", Icons.Power);
            var row = new DockPanel();
            DockPanel.SetDock(on, Dock.Right);
            row.Children.Add(on);
            row.Children.Add(Ui.Row(10, Ui.Icon(Icons.Alert, 16, Ui.Res("Warn")), Ui.Text(I18n.T("v18.lib.off"), "small", wrap: true)));
            col.Children.Add(HubLook.Frame(row, new Thickness(16, 10), ticks: false));
        }

        if (all.Count == 0)
        {
            col.Children.Add(HubLook.Frame(Ui.Col(12,
                Ui.Text(_filter == "" ? I18n.T("v18.lib.none") : I18n.T("cr.lib.none"), "h3"),
                Ui.Text(I18n.T("v18.lib.none.text"), "muted", wrap: true),
                Ui.Row(10, Ui.Button(I18n.T("v18.lib.import.button"), () => _ = ImportScript(), "primary", Icons.Upload),
                    Ui.Button(I18n.T("v18.lib.buy"), () => HubNav.Open("browse"), "ghost", Icons.Bag))), new Thickness(24, 20)));
            return col;
        }
        if (sealedOnes.Count > 0)
        {
            col.Children.Add(Section(I18n.T("v18.lib.sealed.n", ("n", sealedOnes.Count)), Ui.Button(I18n.T("v18.lib.buy"), () => HubNav.Open("browse"), "ghost", Icons.Bag)));
            var list = Ui.Col(8);
            foreach (var i in sealedOnes) list.Children.Add(ScriptRow(i));
            col.Children.Add(list);
        }
        if (own.Count > 0)
        {
            col.Children.Add(Section(I18n.T("v18.lib.own.n", ("n", own.Count))));
            var list = Ui.Col(8);
            foreach (var i in own) list.Children.Add(ScriptRow(i));
            col.Children.Add(list);
        }
        col.Children.Add(ModerationLog());
        return col;
    }

    /// <summary>Что делает защита — три шага и итог по библиотеке (сколько скриптов, насколько сжаты, сколько имён скрыто).</summary>
    static Control GuardHero(List<InjectItem> items)
    {
        var mark = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(8), Background = HubLook.Signature(), VerticalAlignment = VerticalAlignment.Top,
            Child = Ui.Icon(Icons.Shield, 22, Brushes.White),
        };
        var steps = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (n, icon, key) in new[] { (1, Icons.Wand, "v18.guard.1"), (2, Icons.Code, "v18.guard.2"), (3, Icons.Lock, "v18.guard.3") })
            steps.Children.Add(new Border
            {
                BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 10), Margin = new Thickness(0, 0, 10, 0), Background = HubLook.Raised,
                Child = Ui.Col(6, Ui.Row(8, HubLook.MonoText($"0{n}", 11, HubLook.Cyan), Ui.Icon(icon, 15, HubLook.Violet), Ui.Text(I18n.T(key), "strong")),
                    Ui.Text(I18n.T(key + ".text"), "small muted", wrap: true)),
            });
        var left = Ui.Col(10,
            HubLook.Eyebrow(I18n.T("v18.guard.eyebrow"), HubLook.Violet),
            new TextBlock { Text = I18n.T("v18.guard.title"), FontFamily = Look.Display, FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
            Ui.Text(I18n.T("v18.guard.text"), "muted", wrap: true), steps);
        var guarded = items.Where(i => i.Guard is not null).ToList();
        var saved = guarded.Count == 0 ? 0 : (int)Math.Round(guarded.Average(i => i.Guard!.Saved));
        var stats = Ui.Col(14,
            Gx.Stat(items.Count.ToString(), I18n.T("v18.guard.stat.n"), HubLook.Cyan),
            Gx.Stat(guarded.Count == 0 ? "—" : $"−{saved} %", I18n.T("v18.guard.stat.saved"), HubLook.Violet),
            Gx.Stat(guarded.Sum(i => i.Guard!.Names).ToString(), I18n.T("v18.guard.stat.names"), null));
        stats.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 22 };
        grid.Children.Add(mark);
        Grid.SetColumn(left, 1);
        grid.Children.Add(left);
        Grid.SetColumn(stats, 2);
        grid.Children.Add(stats);
        return HubLook.Frame(grid, new Thickness(24, 20));
    }

    /// <summary>Итог защиты одной строкой: «AES-256 · −38 % · 5 имён скрыто» (покупки до 18.0 — пока только шифр).</summary>
    internal static string GuardLine(InjectItem i)
    {
        if (!i.Sealed) return I18n.T("v18.lib.open");
        if (i.Guard is not { } g) return "AES-256";
        var parts = new List<string> { "AES-256" };
        if (g.Saved > 0) parts.Add($"−{g.Saved} %");
        if (g.Names > 0) parts.Add(I18n.T("v18.lib.names", ("n", g.Names)));
        else if (g.Comments > 0) parts.Add(I18n.T("v18.lib.comments", ("n", g.Comments)));
        return string.Join(" · ", parts);
    }

    /// <summary>Строка библиотеки: значок, название с ярлыками, откуда и для какой игры, итог защиты; при наведении — «Запуск» и «Настройка».</summary>
    Control ScriptRow(InjectItem i)
    {
        var game = i.Game == "" ? null : GameCatalog.ById(i.Game);
        Control icon = game is not null
            ? new Border { Width = 34, Height = 46, CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = Ui.GameImage(game, 96, art: Images.Art.Cover) }
            : new Border { Width = 34, Height = 46, CornerRadius = new CornerRadius(4), Background = HubLook.Raised, Child = Ui.Icon(i.Sealed ? Icons.Lock : Icons.Code, 16, Ui.Res("Faint")) };
        var tags = Ui.Row(6, StoreKit.Pill(I18n.T(i.Source switch { "market" => "v18.lib.bought", "import" => "v18.lib.import", _ => "v11.inj.own" }), onArt: false),
            StoreKit.Pill(I18n.T("v11.inj.kind." + i.Kind), onArt: false));
        if (!i.Enabled) tags.Children.Add(StoreKit.Pill(I18n.T("v18.lib.disabled"), onArt: false));
        var strikes = Moderation.Strikes(i.Id);
        if (strikes > 0) tags.Children.Add(Gx.Tag(I18n.T("v11.inj.strikes", ("n", strikes)), Ui.Res("Warn"), Ui.Res("OnBrand"), 11));
        var title = Ui.Text(i.Title, "h3");
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var from = i.Source switch { "market" => I18n.T("v18.lib.from", ("seller", i.Seller)), "import" => I18n.T("v18.lib.imported"), _ => I18n.T("v18.lib.yours") };
        var words = Ui.Col(4, Ui.Row(8, title, tags), Ui.Text(from + " · " + (game?.Name ?? I18n.T("v18.lib.noGame")), "small muted"));
        var guard = Ui.Row(6, Ui.Icon(i.Sealed ? Icons.Lock : Icons.Code, 13, i.Sealed ? HubLook.Cyan : Ui.Res("Faint")), HubLook.MonoText(GuardLine(i), 11, i.Sealed ? HubLook.Cyan : null));
        guard.VerticalAlignment = VerticalAlignment.Center;
        guard.Margin = new Thickness(0, 0, 18, 0);

        var row = new Border
        {
            Background = HubLook.Panel, BorderBrush = HubLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 10),
            Opacity = i.Enabled ? 1 : 0.65,
        };
        var actions = HoverActions.InRow(row, () => LaunchScript(i), () => ScriptSettings(i), I18n.T("v18.hover.play"));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        guard.HorizontalAlignment = HorizontalAlignment.Right;
        var right = new Panel { Children = { guard, actions }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 230 };
        // Итог защиты виден всегда; при наведении он уступает место кнопкам.
        row.PointerEntered += (_, _) => Animate.FadeTo(guard, 0, 120);
        row.PointerExited += (_, _) => Animate.FadeTo(guard, 1, 160);
        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(Ui.Row(12, icon, words));
        row.Child = dock;
        row.PointerEntered += (_, _) => row.BorderBrush = HubLook.EdgeHi;
        row.PointerExited += (_, _) => row.BorderBrush = HubLook.Edge;
        Ctx.Attach(row, () => Ctx.Menu(
            Ctx.Item(I18n.T("v18.hover.play"), Icons.Play, () => LaunchScript(i)),
            Ctx.Item(I18n.T("v18.hover.configure"), Icons.Sliders, () => ScriptSettings(i)),
            "-",
            Ctx.Item(I18n.T("ctx.delete"), Icons.Trash, () => RemoveScript(i))));
        return row;
    }

    /// <summary>Журнал автомодерации — свёрнутым внизу: нужен, когда что-то не запустилось.</summary>
    static Control ModerationLog()
    {
        var log = Ui.Col(8);
        foreach (var e in Moderation.Log().Take(20))
        {
            var line = Ui.Row(8, Ui.Icon(e.Ok ? Icons.Check : Icons.Alert, 13, e.Ok ? Ui.Res("Good") : Ui.Res("Bad")),
                Ui.Text($"{e.Title} · {I18n.T("v11.mod.stage." + e.Stage)} · {Ui.Ago(e.At)}", "small"));
            var c = Ui.Col(3, line);
            foreach (var f in e.Findings.Where(f => f.Level != "info").Take(3)) c.Children.Add(Ui.Text("   " + f.Text, "small muted", wrap: true));
            log.Children.Add(c);
        }
        if (log.Children.Count == 0) log.Children.Add(Ui.Text(I18n.T("v11.mod.log.empty"), "small muted"));
        var box = new Expander
        {
            Header = Ui.Row(8, Ui.Icon(Icons.Shield, 15, HubLook.Violet), Ui.Text(I18n.T("v11.mod.title"), "strong")),
            Content = Ui.Col(10, Ui.Text(I18n.T("v11.mod.lead"), "small muted", wrap: true), log),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        return box;
    }

    /// <summary>Импорт чужого скрипта: файл → автомодерация → защита → шифрование → библиотека. Игра — из файла, иначе последняя.</summary>
    async Task ImportScript()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var type = new FilePickerFileType(I18n.T("v18.lib.import.types")) { Patterns = ["*" + MX.ModxDoc.Extension, "*.mls", "*.zip"] };
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = I18n.T("v18.lib.import.button"), AllowMultiple = true, FileTypeFilter = [type] });
        var paths = result.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        var game = Settings.Data.Str("lastGame") is { } last && GameCatalog.ById(last) is not null ? last : "";
        var added = new List<InjectItem>();
        foreach (var path in paths)
        {
            try
            {
                var (item, verdict) = await Task.Run(() => Features.Inject.AddImport(path, game));
                if (item is null) MainWindow.Current?.Toast(I18n.T("v18.lib.import.blocked", ("name", Path.GetFileName(path)), ("why", verdict.Findings.FirstOrDefault(f => f.Level != "info")?.Text ?? "")), bad: true);
                else added.Add(item);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MainWindow.Current?.Toast(e.Message, bad: true); }
        }
        _tab = "scripts";
        _open = null;
        Build();
        if (added.Count == 1) MainWindow.Current?.Toast(I18n.T("v18.lib.import.done", ("name", added[0].Title), ("guard", GuardLine(added[0]))));
        else if (added.Count > 1) MainWindow.Current?.Toast(I18n.T("v18.lib.import.many", ("n", added.Count)));
        // Игру не узнали — сразу настройка, чтобы скрипт было куда ставить.
        if (added.Count == 1 && added[0].Game == "") ScriptSettings(added[0]);
    }

    /// <summary>«Запуск» скрипта: включить, если выключен, и запустить его игру (скрипт ставится инжектом на время игры).</summary>
    void LaunchScript(InjectItem i)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        if (AppState.Games.FirstOrDefault(g => g.Def.Id == i.Game && g.Status == Detect.Found) is not { } game)
        {
            w.Toast(I18n.T("v18.lib.pickGame", ("title", i.Title)));
            ScriptSettings(i);
            return;
        }
        if (!Features.Inject.On) { w.Toast(I18n.T("v18.lib.off"), bad: true); return; }
        if (!i.Enabled) Features.Inject.SetEnabled(i.Id, true);
        Actions.Play(game);
    }

    /// <summary>«Настройка» скрипта: игра, включён ли, что сделала защита; удалить. Исходника здесь нет — и не будет.</summary>
    void ScriptSettings(InjectItem i)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var games = AppState.Games.Where(g => g.Status == Detect.Found && !g.Def.IsMinecraft).OrderBy(g => g.Def.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var game = new ComboBox { MinWidth = 260, PlaceholderText = I18n.T("v11.lx.pickGame"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var g in games) game.Items.Add(new ComboBoxItem { Content = g.Def.Name, Tag = g.Def.Id });
        game.SelectedItem = game.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag! == i.Game);
        var on = new ToggleSwitch { IsChecked = i.Enabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };

        var facts = Ui.Col(6);
        void Fact(string label, string value)
        {
            var v = HubLook.MonoText(value, 11.5, Ui.Res("Text"));
            var r = new DockPanel();
            DockPanel.SetDock(v, Dock.Right);
            r.Children.Add(v);
            r.Children.Add(Ui.Text(label, "small muted"));
            facts.Children.Add(r);
        }
        if (i.Sealed)
        {
            Fact(I18n.T("v18.set.source"), i.Source == "market" ? I18n.T("v18.lib.from", ("seller", i.Seller)) : I18n.T("v18.lib.imported"));
            if (i.Guard is { } g)
            {
                Fact(I18n.T("v18.set.size"), $"{Size(g.Before)} → {Size(g.After)}" + (g.Saved > 0 ? $"  (−{g.Saved} %)" : ""));
                Fact(I18n.T("v18.set.min"), I18n.T("v18.set.min.value", ("c", g.Comments), ("n", g.Names)));
                Fact(I18n.T("v18.set.compile"), I18n.T(g.Verified ? "v18.set.compile.ok" : "v18.set.compile.packed"));
            }
            Fact(I18n.T("v18.set.cipher"), "AES-256-GCM · " + I18n.T("v18.set.cipher.pc"));
            Fact(I18n.T("v18.set.sign"), "SHA-256 · " + (i.Sha256.Length >= 12 ? i.Sha256[..12] : i.Sha256));
        }
        else Fact(I18n.T("v18.set.source"), i.LocalPath ?? i.FileName);

        var note = Ui.Text(I18n.T(i.Sealed ? "v18.set.sealed" : "v18.set.open"), "small muted", wrap: true);
        var body = Ui.Col(14,
            Ui.Col(6, Ui.Text(I18n.T("v18.set.game"), "small muted"), game),
            Ui.Row(10, on, Ui.Text(I18n.T("v18.set.enabled"))),
            new Border { Height = 1, Background = HubLook.Edge },
            Ui.Row(8, Ui.Icon(i.Sealed ? Icons.Lock : Icons.Code, 15, i.Sealed ? HubLook.Cyan : Ui.Res("Muted")), Ui.Text(I18n.T(i.Sealed ? "v18.set.guard" : "v18.set.own"), "strong")),
            facts, note);

        void Apply()
        {
            if (game.SelectedItem is ComboBoxItem { Tag: string id } && id != i.Game) Features.Inject.SetGame(i.Id, id);
            if ((on.IsChecked == true) != i.Enabled) Features.Inject.SetEnabled(i.Id, on.IsChecked == true);
            w.CloseDialog();
            Build();
        }
        w.Dialog(i.Title, body, 520,
            Ui.Button(I18n.T("ctx.delete"), () => { w.CloseDialog(); RemoveScript(i); }, "ghost", Icons.Trash),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("cr.save"), Apply, "primary", Icons.Check));
    }

    static string Size(int bytes) => bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.#} KB";

    void RemoveScript(InjectItem i)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        w.Dialog(I18n.T("v18.lib.remove.title", ("name", i.Title)), Ui.Text(I18n.T(i.Sealed ? "v18.lib.remove.sealed" : "v18.lib.remove.own"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
            Ui.Button(I18n.T("ctx.delete"), () => { w.CloseDialog(); Features.Inject.Remove(i.Id); Build(); }, "danger", Icons.Trash));
    }
}
public sealed partial class CreatorPage
{
    /// <summary>Для снимков: окно «Настройка» скрипта из библиотеки.</summary>
    internal void DemoSettings(string id)
    {
        if (Features.Inject.Get(id) is { } i) ScriptSettings(i);
    }
}
