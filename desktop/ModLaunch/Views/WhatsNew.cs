using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModLaunch.Core;

namespace ModLaunch.Views;

/// <summary>«Что нового»: один раз после обновления на новую версию — короткий список с кнопками «попробовать».</summary>
public static class WhatsNew
{
    static readonly (string Icon, string Key, Func<Page>? Open)[] Items =
    [
        // 25.0 (юбилейная): Premium, кошельки, «Найти виновника», «Сообщить о проблеме», юбилейные награды.
        (Icons.Sparkles, "new.v25.premium", () => new AccountPage("premium")),
        (Icons.Bag, "new.v25.wallet", () => new AccountPage("wallet")),
        (Icons.Search, "new.v25.bisect", null),
        (Icons.Flag, "new.v25.report", null),
        (Icons.Refresh, "new.v25.patch", null),
        (Icons.Trophy, "new.v25.jubilee", () => new ProfilePage("rewards")),
        // 24.0: «Почему игра упала», сборка по коду для всех игр, «Обновить всё» одной кнопкой.
        (Icons.Wand, "new.v24.crash", null),
        (Icons.Users, "new.v24.share", () => new HomePage()),
        (Icons.ArrowUp, "new.v24.updates", () => new HomePage()),
        // 23.8: конструктор в играх, весь Steam, меню под игру, новые движки, рынок, безопасность.
        (Icons.Cube, "new.v23.builder", () => new CreatorPage("ingame")),
        (Icons.Gamepad, "new.v23.allgames", () => new CreatorPage("ingame")),
        (Icons.Palette, "new.v23.menu", () => new CreatorPage("ingame")),
        (Icons.Layers, "new.v23.engines", () => new CreatorPage("ingame")),
        (Icons.Bag, "new.v23.market", () => new CreatorPage("shop")),
        (Icons.Shield, "new.v23.safety", null),
        (Icons.Grid, "new.v21.blocks", XStudio.Entry),
        (Icons.Package, "new.v21.pieces", XStudio.Entry),
        (Icons.Sparkles, "new.v21.kids", () => CreatorPage.NewForm()),
        (Icons.Grid, "new.v20.hub", () => new CreatorPage("home")),
        (Icons.Chart, "new.v20.earnings", () => new AccountPage("earnings")),
        (Icons.Code, "new.v19.ide", XStudio.Entry),
        (Icons.Bug, "new.v19.debug", XStudio.Entry),
        (Icons.Alert, "new.v19.lint", XStudio.Entry),
        (Icons.Keyboard, "new.v19.tools", XStudio.Entry),
        (Icons.Folder, "new.v18.projects", () => new CreatorPage("home")),
        (Icons.Code, "new.v18.studio", () => CreatorPage.NewForm()),
        (Icons.Shield, "new.v18.guard", () => new CreatorPage("scripts")),
        (Icons.Grid, "new.v18.hover", () => new CreatorPage("home")),
        (Icons.Trophy, "new.v17.builds", () => new ProfilePage("rewards")),
        (Icons.Palette, "new.v17.colors", () => new SettingsPage("look")),
        (Icons.Play, "new.v17.motion", null),
        (Icons.Activity, "new.v17.fast", null),
        (Icons.Code, "new.v16.studio", () => CreatorPage.NewForm()),
        (Icons.Layers, "new.v16.bridge", XStudio.Entry),
        (Icons.Sparkles, "new.v16.ai", () => new AiBuildsPage()),
        (Icons.Trophy, "new.v16.road", () => new ProfilePage("rewards")),
        (Icons.Gamepad, "new.v16.covers", () => new LibraryPage()),
        (Icons.Code, "new.v15.studio", XStudio.Entry),
        (Icons.Zap, "new.v15.inject", XStudio.Entry),
        (Icons.Gamepad, "new.v15.games", () => new AddGamePage()),
        (Icons.Activity, "new.v15.fast", null),
        (Icons.Bag, "new.v14.market", () => new AccountPage("earnings")),
        (Icons.Palette, "new.v14.look", () => new SettingsPage("look")),
        (Icons.Creator, "new.v13.hub", () => new CreatorPage("home")),
        (Icons.Code, "new.v13.modscript", () => new CreatorPage("console")),
        (Icons.Users, "new.v13.team", () => new CreatorPage("team")),
        (Icons.Grid, "new.v13.widgets", () => new HomePage()),
        (Icons.Gamepad, "new.v12.games", () => new AddGamePage()),
        (Icons.Creator, "new.v12.hub", () => new CreatorPage("mine")),
        (Icons.Grid, "new.v12.widgets", () => new HomePage()),
        (Icons.Layers, "new.v12.steam", () => new SteamGamesPage()),
        (Icons.Zap, "new.v12.fast", null),
        (Icons.Code, "new.v11.lang", () => CreatorPage.NewForm()),
        (Icons.Zap, "new.v11.inject", () => new CreatorPage("injects")),
        (Icons.Shield, "new.v11.moderation", () => new CreatorPage("injects")),
        (Icons.WifiOff, "new.v11.offline", () => new SettingsPage("general")),
        (Icons.Hourglass, "new.v11.builds", () => new ProfilePage("builds")),
        (Icons.Shield, "new.v11.backup", () => new SettingsPage("backups")),
        (Icons.Server, "new.v11.servers", () => new CommunityPage("servers")),
        (Icons.Settings, "new.v11.settings", () => new SettingsPage("general")),
        (Icons.Palette, "new.v11.look", () => new SettingsPage("look")),
        (Icons.Cube, "new.v101.mc", () => new MinecraftPage()),
        (Icons.Check, "new.v101.cases", () => new SettingsPage("configs")),
        (Icons.Home, "new.v10.home", () => new HomePage()),
        (Icons.Shield, "new.v10.autopilot", () => new AutopilotPage()),
        (Icons.Download, "new.v10.cloud", () => new SettingsPage("backups")),
        (Icons.Sparkles, "new.v10.clean", () => new SettingsPage("launch")),
        (Icons.Play, "new.v10.motion", () => new SettingsPage("configs")),
        (Icons.Creator, "new.v10.creator", () => new CreatorPage()),
        (Icons.Trophy, "new.v99.ach", () => new ProfilePage()),
        (Icons.Home, "new.v99.home", () => new HomePage()),
        (Icons.Play, "new.v99.launch", () => new LibraryPage()),
        (Icons.Creator, "new.v99.creator", () => new CreatorPage()),
        (Icons.Zap, "new.v97.modscriptx", () => new CreatorPage("inject")),
        (Icons.Home, "new.v95.home", () => new HomePage()),
        (Icons.Sparkles, "new.v95.forYou", () => new HomePage()),
        (Icons.Bag, "new.v95.wallet", () => new AccountPage("wallet")),
        (Icons.Creator, "new.v95.hub", () => new MarketPage()),
        (Icons.Cube, "new.v95.mcinto", () => new MinecraftPage("catalog")),
        (Icons.Check, "new.v95.fixes", null),
        (Icons.Cube, "new.v94.minecraft", () => new MinecraftPage("builds")),
        (Icons.Users, "new.v94.servers", () => new MinecraftPage("servers")),
        (Icons.Download, "new.v94.downloads", () => new SettingsPage("downloads")),
        (Icons.Gamepad, "new.v94.get", () => new LibraryPage()),
        (Icons.Check, "new.v94.fixes", null),
        (Icons.Home, "new.v93.home", () => new HomePage()),
        (Icons.Bag, "new.v93.market", () => new MarketPage()),
        (Icons.Play, "new.v93.gamefx", () => new LibraryPage()),
        (Icons.Sidebar, "new.v93.rail", null),
        (Icons.Home, "new.v92.store", () => new HomePage()),
        (Icons.Palette, "new.v92.styles", () => new SettingsPage("look")),
        (Icons.Gamepad, "new.v92.game", () => new LibraryPage()),
        (Icons.Sparkles, "new.v92.openfx", null),
        (Icons.Grid, "new.v91.home", () => new HomePage()),
        (Icons.Download, "new.v91.fx", null),
        (Icons.Creator, "new.v91.creator", () => new MarketPage()),
        (Icons.Sparkles, "new.v91.splash", () => new SettingsPage("interface")),
        (Icons.Palette, "new.v9.design", () => new SettingsPage("look")),
        (Icons.Shield, "new.v9.safety", null),
        (Icons.Cube, "new.v85.minecraft", () => new MinecraftPage()),
        (Icons.Layers, "new.v85.builds", () => new MinecraftPage("builds")),
        (Icons.Bag, "new.v85.catalog", () => new MinecraftPage("catalog")),
        (Icons.ArrowUp, "new.v85.updates", () => new MinecraftPage("mods")),
        (Icons.Sidebar, "new.v85.rail", null),
    ];

    /// <summary>
    /// Заголовок и описание пункта. До 20.x — две строки (key и key.text), с 21.0 — одна «Заголовок: описание».
    /// Раньше у таких пунктов вместо описания виден был сам ключ «new.v21.blocks.text».
    /// </summary>
    public static (string Title, string Text) Split(string key) => Split(key, k => I18n.T(k));

    /// <summary>То же для любого языка (в самотесте — по всем восьми).</summary>
    static (string Title, string Text) Split(string key, Func<string, string> t)
    {
        var title = t(key);
        var text = t(key + ".text");
        if (text != key + ".text") return (title, text);
        // Двоеточие: «: », во французском « : », в китайском полноширинное «：».
        var m = System.Text.RegularExpressions.Regex.Match(title, @"\s?[:：]\s*");
        while (m.Success && m.Value.Trim() == ":" && !m.Value.EndsWith(' ')) m = m.NextMatch(); // «14:00», «Nexus:123» — не делитель
        if (!m.Success || m.Index <= 0 || m.Index + m.Length >= title.Length) return (title, "");
        var rest = title[(m.Index + m.Length)..];
        return (title[..m.Index].TrimEnd(), char.ToUpper(rest[0], I18n.Culture) + rest[1..]);
    }

    [SelfTest]
    static string WhatsNewHasNoRawKeys()
    {
        if (I18n.T("new.title") == "new.title") return "strings not loaded (headless)";
        string[] langs = ["ru", "en", "uk", "es", "de", "fr", "pt-BR", "zh-CN"];
        var missing = new List<string>();
        foreach (var lang in langs)
            foreach (var (_, key, _) in Items)
            {
                var (title, text) = Split(key, k => I18n.Raw(lang, k) ?? k);
                if (title.StartsWith("new.", StringComparison.Ordinal) || text.StartsWith("new.", StringComparison.Ordinal) || text == "") missing.Add(lang + ":" + key);
            }
        // 23.8: все пункты переведены на все восемь языков — новый пункт без перевода тест не пропустит.
        if (missing.FirstOrDefault() is { } bad) throw new Exception("raw or empty: " + bad + (missing.Count > 1 ? $" (+{missing.Count - 1})" : ""));
        if (I18n.T("new.v25.subtitle") == "new.v25.subtitle") throw new Exception("no subtitle for 25");
        var (fr, frText) = Split("new.v21.blocks", k => k == "new.v21.blocks" ? "Blocs dans ModScript : texte" : k);
        var (zh, zhText) = Split("new.v21.blocks", k => k == "new.v21.blocks" ? "积木：文字" : k);
        if (fr != "Blocs dans ModScript" || frText != "Texte" || zh != "积木" || zhText != "文字") throw new Exception("colon split: " + fr + "|" + zh);
        return Items.Length + " items × 8 languages, " + missing.Count + " not translated yet";
    }

    /// <summary>9.5: самые свежие пункты — для карточки «Что нового» на главной.</summary>
    public static IEnumerable<(string Icon, string Key, Func<Page>? Open)> Latest(int n) => Items.Take(n);

    /// <summary>«Настройки» первой найденной игры с модами.</summary>
    static Page ConfigPage()
    {
        var g = AppState.Games.FirstOrDefault(g => g.Status == Detect.Found && g.ModCount > 0) ?? AppState.Games.FirstOrDefault(g => g.Status == Detect.Found);
        return g is null ? new LibraryPage() : new GamePage(g.Def.Id, "config");
    }

    /// <summary>Показать, если эту версию ещё не видели (при самом первом запуске — молча запомнить).</summary>
    public static void MaybeShow()
    {
        if (Program.Screenshot) return;
        var seen = Settings.Data.Str("seenVersion");
        Settings.Data["seenVersion"] = Http.Version;
        Settings.Save();
        // 9.2: после обновления — сначала выбор оформления, потом «Что нового».
        var picker = StylePicker.ShouldShow;
        if (seen == Http.Version && !picker) return;
        // 9.1: сначала заставка при запуске, потом «Что нового».
        DispatcherTimer.RunOnce(() =>
        {
            if (picker) StylePicker.Show(seen == Http.Version ? null : Show);
            else Show();
        }, TimeSpan.FromSeconds(Splash.Enabled ? 2.6 : 1.2));
    }

    public static void Show()
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var list = Ui.Col(10);
        foreach (var (icon, key, open) in Items)
        {
            var (title, text) = Split(key);
            var words = Ui.Col(2, Ui.Text(title, "h3"), Ui.Text(text, "small muted", wrap: true));
            words.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(icon, 19, Ui.Res("Brand2")), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(badge);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            if (open is not null)
            {
                var go = Ui.Button(I18n.T("new.try"), () => { w.CloseDialog(); w.Navigate(open); }, "ghost", Icons.Forward);
                go.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(go, 2);
                row.Children.Add(go);
            }
            list.Children.Add(row);
            Animate.From(row, "translateY(14px)", 420, 160 + list.Children.Count * 70, new Avalonia.Animation.Easings.CubicEaseOut());
        }
        var hero = new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 16), Margin = new Thickness(0, 0, 0, 6),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(Look.Accent), 0), new GradientStop(Look.Mix(Color.Parse(Look.Accent), Color.Parse("#111"), 0.55), 1) },
            },
            Child = Ui.Col(2,
                new TextBlock { Text = $"ModLaunch {Http.Version}", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                new TextBlock { Text = I18n.T("new.v25.subtitle"), Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), TextWrapping = TextWrapping.Wrap }),
        };
        w.Dialog(I18n.T("new.title"), new ScrollViewer { MaxHeight = 560, Content = Ui.Col(12, hero, list) }, 620,
            Ui.Button(I18n.T("new.ok"), w.CloseDialog, "primary", Icons.Check));
    }
}
