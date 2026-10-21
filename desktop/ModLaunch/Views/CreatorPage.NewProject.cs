using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

public sealed partial class CreatorPage
{
    // ---------------------------------------------------------------- 12.0: мастер нового проекта

    /// <summary>Что выбрано в мастере (отдельно — чтобы снимки могли открыть его в нужном виде).</summary>
    internal sealed class NewProjectState
    {
        public string Name = "";
        public string? Game;
        /// <summary>blank — пустой, template — шаблон, import — готовый мод (папка или .zip).</summary>
        public string Source = "blank";
        public string? TemplateId;
        public string? ImportPath;
        public ImportedMod? Imported;
        public string? ImportError;
        public bool Busy;
    }

    /// <summary>
    /// Мастер нового проекта (контракт: его зовут «Мои проекты», шапка и «путь автора»). Имя, игра — найденные
    /// на ПК первыми — и с чего начать: пустой проект, шаблон для этой игры или импорт готового мода.
    /// </summary>
    public static void NewProjectDialog() => ShowNewProject(new NewProjectState());

    /// <summary>Игры мастера: найденные на ПК — первыми, дальше по имени. Без Minecraft — у него свои сборки.</summary>
    static List<(GameDef Def, bool Found)> WizardGames() =>
        AppState.Games.Where(g => !g.Def.IsMinecraft)
            .Select(g => (g.Def, Found: g.Status == Detect.Found))
            .OrderBy(x => x.Found ? 0 : 1).ThenBy(x => x.Def.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    internal static void ShowNewProject(NewProjectState s)
    {
        var w = MainWindow.Current;
        if (w is null) return;
        var games = WizardGames();
        // Игра по умолчанию: последняя открытая, если она найдена, иначе первая найденная, иначе Stardew Valley.
        s.Game ??= games.Where(x => x.Found).Select(x => x.Def.Id).FirstOrDefault(id => id == Settings.Data.Str("lastGame"))
            ?? games.Where(x => x.Found).Select(x => x.Def.Id).FirstOrDefault() ?? "stardew-valley";
        var author = Templates.DefaultAuthor();
        var ticket = 0;

        TextBox name = new() { Text = s.Name, MaxLength = 80 };
        var game = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 380 };
        game.ItemsSource = games.Select(x => x.Found ? $"{x.Def.Name}  ·  {I18n.T("v12h2.np.found")}" : x.Def.Name).ToList();
        game.SelectedIndex = Math.Max(0, games.FindIndex(x => x.Def.Id == s.Game));
        var tiles = new UniformGrid { Columns = 3 };
        var source = new ContentControl();
        var preview = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, Foreground = Ui.Res("Muted"),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 10, LineHeight = 17,
        };
        var create = Ui.Button(I18n.T("v12h2.np.create"), () => { }, "primary", Icons.Plus);

        GameDef? Game() => GameCatalog.ById(s.Game ?? "");
        Template? Picked() => s.Source == "template" && s.TemplateId is { } id ? Templates.For(s.Game ?? "").FirstOrDefault(t => t.Id == id) : null;
        // Имя: что ввели, иначе подсказка в поле — название шаблона, имя из манифеста или «Мой мод».
        string Suggested() => s.Source switch
        {
            "template" when Picked() is { } t => I18n.T($"cr.ex.{t.Id}"),
            "import" when s.Imported is { } m && m.Name.Trim() != "" => m.Name.Trim(),
            _ => Templates.DefaultName(),
        };
        string FinalName() => (name.Text ?? "").Trim() is { Length: > 0 } typed ? typed : Suggested();
        string Code() => s.Source switch
        {
            "template" => Picked() is { } t ? Templates.Apply(t, FinalName(), s.Game ?? "", author) : "",
            "import" => s.Imported is { } m ? ProjectImport.Script(m, FinalName(), s.Game ?? "", author) : "",
            _ => Templates.Apply(Templates.ById("blank")!, FinalName(), s.Game ?? "", author),
        };

        // Имя меняется по буквам — тогда обновляем только подсказку, код и кнопку, без пересборки списков.
        void RenderCode()
        {
            name.Watermark = Suggested();
            var code = Code().Replace("\r", "").TrimEnd('\n');
            var lines = code.Split('\n');
            preview.Text = lines.Length > 10 ? string.Join('\n', lines.Take(9)).TrimEnd('\n') + "\n…" : code;
            create.IsEnabled = !s.Busy && Game() is not null && s.Source switch
            {
                "template" => Picked() is not null,
                "import" => s.Imported is not null,
                _ => true,
            };
        }

        void Render()
        {
            tiles.Children.Clear();
            foreach (var (id, icon, key) in new[] { ("blank", Icons.FilePlus, "v12h2.np.src.blank"), ("template", Icons.Wand, "v12h2.np.src.template"), ("import", Icons.Folder, "v12h2.np.src.import") })
            {
                var sid = id;
                var words = Ui.Col(4, Ui.Row(8, Ui.Icon(icon, 16, Ui.Res("Brand2")), Ui.Text(I18n.T(key), "h3")),
                    Ui.Text(I18n.T(key + ".text"), "small muted", wrap: true));
                var tile = Choice(words, s.Source == id, () => { s.Source = sid; Render(); });
                tile.Margin = new Thickness(tiles.Children.Count == 0 ? 0 : 5, 0, tiles.Children.Count == 2 ? 0 : 5, 0);
                tiles.Children.Add(tile);
            }
            source.Content = s.Source switch { "template" => TemplateList(), "import" => ImportPanel(), _ => BlankNote() };
            RenderCode();
        }

        Control BlankNote() => Ui.Text(I18n.T("v12h2.np.blank.note", ("loader", Game()?.LoaderName ?? "ModScript")), "small muted", wrap: true);

        Control TemplateList()
        {
            var list = Templates.For(s.Game ?? "");
            if (list.Count == 0) return Ui.Text(I18n.T("v12h2.np.noTemplates"), "small muted", wrap: true);
            if (Picked() is null) s.TemplateId = list[0].Id;
            var rows = Ui.Col(6);
            var marks = new List<(string Id, Border Box)>();
            foreach (var t in list)
            {
                var tt = t;
                var g = GameCatalog.ById(t.Game);
                var words = Ui.Col(1,
                    new TextBlock { Text = I18n.T($"cr.ex.{t.Id}"), FontWeight = FontWeight.SemiBold, FontSize = 13.5 },
                    new TextBlock { Text = I18n.T($"cr.ex.{t.Id}.text"), FontSize = 12, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
                words.VerticalAlignment = VerticalAlignment.Center;
                // Выбор шаблона не пересобирает список: прокрутка остаётся на месте, меняются рамки и код.
                var row = Choice(Ui.Row(10, Ui.Thumb(g?.ArtUrl, g?.Name ?? t.Game, 30, 8), words), s.TemplateId == t.Id, () =>
                {
                    s.TemplateId = tt.Id;
                    foreach (var (id, box) in marks) Mark(box, id == tt.Id);
                    RenderCode();
                }, new Thickness(10, 8));
                marks.Add((t.Id, row));
                rows.Children.Add(row);
            }
            // Выбранный (из снимка или после смены вкладки) может быть ниже края — показать его.
            if (marks.FirstOrDefault(x => x.Id == s.TemplateId).Box is { } picked && marks[0].Box != picked)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => picked.BringIntoView(), Avalonia.Threading.DispatcherPriority.Loaded);
            return new ScrollViewer { Content = rows, MaxHeight = 196, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        Control ImportPanel()
        {
            var col = Ui.Col(10,
                Ui.Row(8,
                    Ui.Button(I18n.T("v12h2.np.import.folder"), () => _ = Pick(folder: true), "", Icons.Folder),
                    Ui.Button(I18n.T("v12h2.np.import.zip"), () => _ = Pick(folder: false), "", Icons.Package)),
                Ui.Text(I18n.T("v12h2.np.import.hint"), "small muted", wrap: true));
            if (s.Busy) col.Children.Add(Ui.Row(8, Ui.Icon(Icons.Hourglass, 14, Ui.Res("Muted")), Ui.Text(I18n.T("v12h2.np.import.reading"), "small muted")));
            if (s.ImportError is { } error) col.Children.Add(Ui.Row(8, Ui.Icon(Icons.Alert, 14, Ui.Res("Bad")), Ui.Text(error, "small", color: Ui.Res("Bad"), wrap: true)));
            if (s.Imported is { } m && !s.Busy)
            {
                var facts = new List<string> { ProjectImport.KindTitle(m.Kind), I18n.T("v12h2.np.import.files", ("n", m.Files.Count)), GamePage.Size(m.Bytes) };
                if (m.Needs.Count > 0) facts.Add(I18n.T("v12h2.np.import.needs", ("n", m.Needs.Count)));
                var head = Ui.Row(12,
                    new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Ui.Res("BrandSoft"), Child = Ui.Icon(Icons.Package, 18, Ui.Res("Brand2")) },
                    Ui.Col(2, Ui.Text($"{m.Name} · {m.Version}", "h3"), Ui.Text(string.Join(" · ", facts), "small muted")));
                var card = Ui.Col(8, head);
                if (s.ImportPath is { } from)
                    card.Children.Add(new TextBlock { Text = from, FontSize = 11.5, Foreground = Ui.Res("Muted"), TextTrimming = TextTrimming.PrefixCharacterEllipsis });
                foreach (var key in m.Warnings)
                    card.Children.Add(Ui.Row(8, Ui.Icon(Icons.Info, 13, Ui.Res("Warn")), Ui.Text(I18n.T(key, ("n", m.Skipped.Count), ("max", ProjectImport.MaxCopyLines)), "small muted", wrap: true)));
                col.Children.Add(Ui.Card(card, 14));
            }
            return col;
        }

        async Task Pick(bool folder)
        {
            var path = folder
                ? await w.PickFolder(I18n.T("v12h2.np.import.folder"))
                : await Pickers.Typed(w, I18n.T("v12h2.np.import.zip"), ["*.zip"], "ZIP");
            if (string.IsNullOrEmpty(path)) return;
            await ReadImport(path);
        }

        async Task ReadImport(string path)
        {
            var my = ++ticket;
            s.ImportPath = path;
            s.Imported = null;
            s.ImportError = null;
            s.Busy = true;
            Render();
            try
            {
                var m = await Task.Run(() => ProjectImport.Read(path));
                if (my != ticket) return;
                s.Imported = m;
                // Мод SMAPI — это Stardew Valley: выбираем её сами.
                if (m.Game is { } g && games.FindIndex(x => x.Def.Id == g) is var i && i >= 0) { s.Game = g; game.SelectedIndex = i; }
            }
            catch (Exception e) { if (my == ticket) s.ImportError = Jobs.Explain(e); }
            if (my != ticket) return;
            s.Busy = false;
            Render();
        }

        async Task Create()
        {
            if (!create.IsEnabled || Game() is not { } def) return;
            Project p;
            try
            {
                if (s.Source == "import" && s.ImportPath is { } path)
                {
                    s.Busy = true;
                    Render();
                    // Пустое имя — пусть импорт возьмёт его из манифеста.
                    var typed = (name.Text ?? "").Trim();
                    p = await Task.Run(() => ProjectImport.Create(path, typed, def.Id, author));
                }
                else p = Projects.Create(FinalName(), Code());
            }
            catch (Exception e)
            {
                s.Busy = false;
                s.ImportError = Jobs.Explain(e);
                Render();
                if (s.Source != "import") w.Toast(s.ImportError, bad: true);
                return;
            }
            w.CloseDialog();
            InstallFx.Play(new FxCard(p.Name, I18n.T("v91.fx.project") + " · " + def.Name, null, def, Icons.Plus), null,
                () => MainWindow.Current?.Navigate(() => new CreatorPage("mine", p.Id)));
        }

        create.Click += (_, _) => _ = Create();
        name.TextChanged += (_, _) => RenderCode();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = Create(); } };
        game.SelectionChanged += (_, _) =>
        {
            if (game.SelectedIndex < 0 || games[game.SelectedIndex].Def.Id == s.Game) return;
            s.Game = games[game.SelectedIndex].Def.Id;
            // Шаблон другой игры не подходит — возьмём первый для новой.
            if (Picked() is null) s.TemplateId = null;
            Render();
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14 };
        top.Children.Add(Field(I18n.T("v12h2.np.name"), name));
        var gameField = Field(I18n.T("v12h2.np.game"), game);
        Grid.SetColumn(gameField, 1);
        top.Children.Add(gameField);
        var code = new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), MinHeight = 64, Child = preview };
        var body = Ui.Col(14, top, Field(I18n.T("v12h2.np.source"), tiles), source, Field("main" + ModScript.Extension, code));
        Render();
        w.Dialog(I18n.T("v12h2.np.title"), body, 680, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), create);
        if (s.Source == "import" && s.ImportPath is { } demo && s.Imported is null) _ = ReadImport(demo);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => name.Focus(), Avalonia.Threading.DispatcherPriority.Background);
    }

    static Control Field(string label, Control input) => Ui.Col(6, Ui.Text(label, "small muted"), input);

    /// <summary>Плитка выбора: рамка акцентом и мягкая заливка у выбранной.</summary>
    static Border Choice(Control content, bool selected, Action click, Thickness? padding = null)
    {
        var b = new Button
        {
            Classes = { "card-btn" }, Background = Brushes.Transparent, Padding = padding ?? new Thickness(14, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Content = content,
        };
        b.Click += (_, _) => click();
        var box = new Border { CornerRadius = new CornerRadius(14), Child = b };
        Mark(box, selected);
        return box;
    }

    static void Mark(Border box, bool selected)
    {
        box.BorderThickness = new Thickness(selected ? 1.5 : 1);
        box.BorderBrush = selected ? Ui.Res("Brand") : Ui.Res("Line");
        box.Background = selected ? Ui.Res("BrandSoft") : Brushes.Transparent;
    }
}
