using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Views;

/// <summary>Настройки: язык, пути к играм, ключ Nexus и сведения о программе.</summary>
public sealed class SettingsPage : Page
{
    string _tab;
    string? _keyStatus;

    public SettingsPage(string tab = "look") => _tab = tab;

    public override string Title => I18n.T("nav.settings");

    public override void Build()
    {
        var tabs = new StackPanel { Spacing = 4, Width = 220 };
        foreach (var (id, key, icon) in new[]
        {
            ("look", "settings.tab.look", Icons.Globe),
            ("games", "settings.tab.games", Icons.Folder),
            ("accounts", "settings.tab.accounts", Icons.Key),
            ("about", "settings.tab.about", Icons.Star),
        })
        {
            var b = Ui.Button(I18n.T(key), () => { _tab = id; Build(); }, "tab", icon);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (_tab == id) b.Classes.Add("active");
            tabs.Children.Add(b);
        }

        Control body = _tab switch
        {
            "games" => GamesTab(),
            "accounts" => AccountsTab(),
            "about" => AboutTab(),
            _ => LookTab(),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24, Margin = new Thickness(34, 26, 34, 34), MaxWidth = 1100 };
        grid.Children.Add(Ui.Card(tabs, 10));
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        Content = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static Control Section(string title, string? hint, params Control[] rows)
    {
        var col = Ui.Col(14, Ui.Text(title, "h2"));
        if (hint is not null) col.Children.Add(Ui.Text(hint, "muted", wrap: true));
        col.Children.AddRange(rows);
        return Ui.Card(col, 24);
    }

    Control LookTab()
    {
        var lang = new ComboBox { Width = 240 };
        lang.Items.Add("Русский");
        lang.Items.Add("English");
        lang.SelectedIndex = I18n.Lang == "en" ? 1 : 0;
        lang.SelectionChanged += (_, _) =>
        {
            var value = lang.SelectedIndex == 1 ? "en" : "ru";
            if (value == I18n.Lang) return;
            Settings.Language = value;
            I18n.Set(value);
        };
        return Section(I18n.T("settings.language"), null, lang);
    }

    Control GamesTab()
    {
        var list = new StackPanel { Spacing = 10 };
        foreach (var g in AppState.Games)
        {
            var status = g.Status switch
            {
                Detect.Found => g.Path!,
                Detect.Searching => g.SearchingWhere is null ? I18n.T("games.searching") : I18n.T("games.searchingWhere", ("where", g.SearchingWhere)),
                Detect.NotFound => I18n.T("games.notDetected"),
                _ => I18n.T("games.notSearched"),
            };
            var info = Ui.Col(4, Ui.Text(g.Def.Name, "h3"), Ui.Text(status, "small muted"));
            info.VerticalAlignment = VerticalAlignment.Center;
            var buttons = Ui.Row(6,
                Ui.Button("", () => Actions.PickGameFolder(g), "icon", Icons.Folder, I18n.T("games.setPath")),
                Ui.Button("", () => _ = AppState.DetectOne(g), "icon", Icons.Refresh, I18n.T("games.detectAgain")));
            if (g.Path is not null)
                buttons.Children.Add(Ui.Button("", () => { AppState.SetPath(g, null); MainWindow.Current?.Toast(I18n.T("toast.pathForgotten")); _ = AppState.DetectOne(g); }, "icon ghost", Icons.Trash, I18n.T("games.forget")));
            buttons.VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
            var art = g.Def.Art is null ? null : Images.Asset(g.Def.Art, 120);
            grid.Children.Add(new Border
            {
                Width = 56, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                Background = Ui.Hex(g.Def.Accent),
                Child = art is null ? null : new Image { Source = art, Stretch = Stretch.UniformToFill },
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            list.Children.Add(new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = grid });
        }
        return Section(I18n.T("settings.games"), I18n.T("settings.games.hint"), list,
            Ui.Button(I18n.T("games.deep"), () => { foreach (var g in AppState.Games.Where(x => x.Status != Detect.Found)) _ = AppState.DetectOne(g, deep: true); }, "", Icons.Search));
    }

    Control AccountsTab()
    {
        var box = new TextBox { Text = Settings.NexusApiKey ?? "", PasswordChar = '•', Watermark = "API key", Width = 420 };
        var check = Ui.Button(I18n.T("settings.nexus.check"), async () =>
        {
            var key = box.Text?.Trim() ?? "";
            Settings.Data["nexusApiKey"] = key == "" ? null : key;
            Settings.Data["nexusPremium"] = false;
            Settings.Save();
            if (key == "") { _keyStatus = null; Build(); return; }
            try
            {
                var (name, premium) = await Nexus.ValidateKey(key);
                Settings.Data["nexusPremium"] = premium;
                Settings.Save();
                _keyStatus = I18n.T("settings.nexus.ok", ("name", name)) + (premium ? " · Premium" : "");
            }
            catch (Exception e) { _keyStatus = Jobs.Explain(e); }
            Build();
        }, "primary", Icons.Check);
        var row = Ui.Row(10, box, check);
        var col = new List<Control> { row, Ui.Button("nexusmods.com/users/myaccount?tab=api", () => Ui.OpenUrl("https://www.nexusmods.com/users/myaccount?tab=api"), "ghost", Icons.External) };
        if (_keyStatus is not null) col.Insert(1, Ui.Text(_keyStatus, "small brand"));
        return Section(I18n.T("settings.nexus"), I18n.T("settings.nexus.hint"), col.ToArray());
    }

    static Control AboutTab()
    {
        var logo = new Image { Source = Images.Asset("icon.png", 128), Width = 64, Height = 64 };
        var name = Ui.Col(4, Ui.Text("ModLaunch", "h2"), Ui.Text($"{Http.Version} · Avalonia UI · .NET {Environment.Version.ToString(2)}", "muted small"));
        name.VerticalAlignment = VerticalAlignment.Center;
        return Section(I18n.T("settings.tab.about"), null,
            Ui.Row(16, logo, name),
            Ui.Row(10,
                Ui.Button("GitHub", () => Ui.OpenUrl("https://github.com/ModLaunch/ModLaunch"), "", Icons.External),
                Ui.Button(I18n.T("games.openFolder"), () => Actions.OpenFolder(Paths.DataDir), "", Icons.Folder)));
    }
}
