using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>Окно публикации в ModLaunch Hub: карточка мода, версия, файлы.</summary>
public static class HubPublish
{
    /// <summary>
    /// 26.0: откуда берётся то, что выкладываем. Project — проект мастерской на этом компьютере (код читается с диска
    /// в момент публикации, пакет собирается из его папки); Existing — карточка, что уже в Hub (новая версия);
    /// Script/Pack — старый вход без проекта: исходник из черновика или пакет из pack.
    /// </summary>
    sealed record Origin(Project? Project, HubMod? Existing, bool Script, Func<Task<string?>>? Pack);

    /// <summary>Для снимков: кнопка «Опубликовать» последнего окна.</summary>
    internal static Button? LastGo;

    /// <param name="fromProject">Мод из мастерской: можно выложить исходник или собранный пакет.</param>
    /// <param name="pack">Собрать пакет проекта (для варианта «готовый пакет»).</param>
    public static void Show(HubDraft d, bool fromProject, Func<Task<string?>>? pack, Action? done = null, bool lockName = false) =>
        Open(d, new Origin(null, null, fromProject, pack), done, lockName);

    /// <summary>
    /// 26.0: выложить проект мастерской (или новую версию его мода). Код и версия — из main.mls как он сохранён сейчас;
    /// мод с файлами проекта (copy, image) — только пакетом; после выпуска версия в main.mls = выложенной.
    /// </summary>
    public static void ShowProject(HubDraft d, Project project, HubMod? existing, Action? done = null, bool lockName = false) =>
        Open(d, new Origin(project, existing, true, null), done, lockName);

    /// <summary>
    /// 26.0: новая версия выложенного мода, проекта которого на этом компьютере нет. Исходник — честно «тот же код»,
    /// что в Hub (обновить карточку), или готовый архив; пакет — новый архив.
    /// </summary>
    public static void ShowVersion(HubDraft d, HubMod existing, Action? done = null) =>
        Open(d, new Origin(null, existing, false, null), done, lockName: true);

    static void Open(HubDraft d, Origin from, Action? done, bool lockName)
    {
        var w = MainWindow.Current!;
        if (!Social.Account.SignedIn && !Program.Screenshot)
        {
            w.Dialog(I18n.T("cr.publish"), Ui.Text(I18n.T("cr.publish.signin"), "muted", wrap: true),
                Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
                Ui.Button(I18n.T("acc.title"), () => { w.CloseDialog(); w.Navigate(() => new AccountPage()); }, "primary", Icons.User));
            return;
        }

        TextBox Box(string value, string hint, bool multi = false, int max = 200) => new()
        {
            Text = value, Watermark = hint, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multi ? 90 : 0, MaxLength = max, VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        Control Field(string label, Control input) => Ui.Col(5, Ui.Text(label, "small muted"), input);

        // Проект: игра и проверка файлов — по коду с диска (его же и выложим).
        var project = from.Project;
        ModBuild? built = project is not null && File.Exists(project.Script) ? ModScript.Compile(File.ReadAllText(project.Script)) : null;
        if (built is { Game.Length: > 0 }) d.Game = built.Game;
        var storedScript = project is null && from.Existing is { IsPackage: false } && !from.Script;
        // Игру решает код (проект, старый вход «исходник») или прошлый выпуск исходником — список не трогается.
        var codeGame = project is not null || from.Script || storedScript;

        var name = Box(d.Name, I18n.T("hub.f.name"), max: 60);
        name.IsEnabled = !lockName;
        var summary = Box(d.Summary, I18n.T("hub.f.summary"), max: 200);
        var description = Box(d.Description, I18n.T("hub.f.description"), multi: true, max: 5000);
        var version = Box(d.Version, "1.0.0", max: 20);
        version.Width = 120;
        var changelog = Box(d.Changelog, I18n.T("hub.f.changelog"), multi: true, max: 2000);
        var images = Box(string.Join("\n", d.Images), I18n.T("hub.f.images"), multi: true, max: 2400);

        // 26.0: незнакомая игра больше не становится первой в списке (Minecraft): её нет в списке — значит, «выберите игру»,
        // а у кода — ошибка с подсказкой. Игра из каталога, которой нет среди добавленных, — в списке тоже есть.
        var defs = AppState.Games.Select(g => g.Def).ToList();
        if (GameCatalog.ById(d.Game) is { } known && defs.All(g => g.Id != known.Id)) defs.Add(known);
        var game = new ComboBox { Width = 260, PlaceholderText = I18n.T("v26.bugs.game.pick") };
        foreach (var g in defs) game.Items.Add(g.Name);
        game.SelectedIndex = defs.FindIndex(g => g.Id == d.Game);
        game.IsEnabled = !codeGame;
        var unknownGame = codeGame && GameCatalog.ById(d.Game) is null;
        var gameError = Ui.Col(4);
        if (unknownGame)
        {
            gameError.Children.Add(Ui.Text(I18n.T("v26.bugs.game.unknown", ("game", d.Game)), "small", color: Ui.Res("Bad"), wrap: true));
            if (Hub.SuggestGame(d.Game, GameCatalog.All) is { } like)
                gameError.Children.Add(Ui.Text(I18n.T("v26.bugs.game.suggest", ("name", like.Name), ("id", like.Id)), "small muted", wrap: true));
        }

        var chosen = new HashSet<string>(d.Tags);
        var tags = new WrapPanel();
        foreach (var tag in Hub.Tags)
        {
            var t = tag;
            var chip = Ui.Button("#" + I18n.T("hub.tag." + tag), () => { }, "chip");
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Padding = new Thickness(10, 4);
            chip.FontSize = 12;
            if (chosen.Contains(tag)) chip.Classes.Add("active");
            chip.Click += (_, _) =>
            {
                if (!chosen.Remove(t) && chosen.Count < 6) chosen.Add(t);
                chip.Classes.Set("active", chosen.Contains(t));
            };
            tags.Children.Add(chip);
        }

        // Что выкладываем: исходник, собранный пакет проекта, тот же код из Hub или свой архив.
        var archive = d.Archive;
        var kind = project is not null || from.Script ? "script" : storedScript ? "stored" : "file";
        var file = Ui.Text(archive is null ? I18n.T("hub.f.noFile") : Path.GetFileName(archive), "small muted");
        file.VerticalAlignment = VerticalAlignment.Center;
        var pick = Ui.Button(I18n.T("hub.f.pick"), async () =>
        {
            var path = await w.PickFile(I18n.T("hub.f.pick"));
            if (path is null) return;
            if (new FileInfo(path).Length > Hub.MaxFile) { w.Toast(I18n.T("err.creator.FILE_TOO_BIG"), bad: true); return; }
            archive = path;
            file.Text = $"{Path.GetFileName(path)} · {GamePage.Size(new FileInfo(path).Length)}";
            if (name.Text is null or "") name.Text = Path.GetFileNameWithoutExtension(path);
        }, "", Icons.FilePlus);
        var group = "kind-" + Guid.NewGuid().ToString("N");
        RadioButton Radio(string text, bool on, Action pickIt)
        {
            var r = new RadioButton { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, GroupName = group, IsChecked = on };
            r.IsCheckedChanged += (_, _) => { if (r.IsChecked == true) pickIt(); };
            return r;
        }
        Control what;
        RadioButton? packageRadio = null;
        if (project is not null || from.Script)
        {
            // 26.0: мод с файлами проекта (copy, image) — только пакетом: исходник у игроков не соберётся.
            var needsFiles = built is not null && Hub.NeedsFiles(built);
            var package = needsFiles || from.Existing?.IsPackage == true;
            if (package) kind = "package";
            var script = Radio(I18n.T("hub.f.kind.script"), !package, () => kind = "script");
            script.IsEnabled = !needsFiles;
            packageRadio = Radio(I18n.T("hub.f.kind.package"), package, () => kind = "package");
            var col = Ui.Col(4, script, packageRadio);
            col.Children.Add(needsFiles
                ? Ui.Text(I18n.T("v26.bugs.files.packageOnly"), "small", color: Ui.Res("Warn"), wrap: true)
                : Ui.Text(I18n.T("hub.f.kind.hint"), "small muted", wrap: true));
            if (project is not null) col.Children.Add(Ui.Text(I18n.T("v26.bugs.source.local", ("name", project.Name)), "small muted", wrap: true));
            what = col;
        }
        else if (storedScript)
        {
            // 26.0: проекта нет — честно: тот же код, что в Hub, или готовый архив (раньше «новая версия» молча
            // выкладывала прошлый код, а «пакет» не выкладывал ничего).
            var row = Ui.Row(10, pick, file);
            row.IsVisible = false;
            var same = Radio(I18n.T("v26.bugs.source.storedKind", ("version", from.Existing!.Version)), true, () => { kind = "stored"; row.IsVisible = false; });
            var own = Radio(I18n.T("v26.bugs.source.archive"), false, () => { kind = "file"; row.IsVisible = true; });
            what = Ui.Col(6, Ui.Text(I18n.T("v26.bugs.source.stored"), "small muted", wrap: true), same, own, row);
        }
        else what = Ui.Row(10, pick, file);

        var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false };
        var status = Ui.Text("", "small muted", wrap: true);
        // Версия не выше выложенной: подсказка и кнопка «Поставить 1.2.4».
        string? suggested = null;
        Button? fix = null;
        fix = Ui.Button("", () =>
        {
            if (suggested is null) return;
            version.Text = suggested;
            fix!.IsVisible = false;
            status.Text = "";
        }, "chip");
        fix.IsVisible = false;
        fix.HorizontalAlignment = HorizontalAlignment.Left;
        void Problem(string text, string? next = null)
        {
            status.Text = text;
            status.Foreground = Ui.Res("Bad");
            suggested = next;
            fix.IsVisible = next is not null;
            if (next is not null) fix.Content = I18n.T("v26.bugs.version.use", ("next", next));
        }

        var form = Ui.Col(12,
            Field(I18n.T("hub.f.name.label"), name),
            Field(I18n.T("hub.f.summary.label"), summary),
            Ui.Row(12, Field(I18n.T("cr.r.game"), game), Field(I18n.T("hub.f.version"), version)));
        if (gameError.Children.Count > 0) form.Children.Add(gameError);
        form.Children.Add(Field(I18n.T("hub.f.what"), what));
        if (from.Existing is not null) form.Children.Add(Ui.Text(I18n.T("v26.bugs.publish.merged"), "small muted", wrap: true));
        form.Children.Add(Field(I18n.T("hub.f.description.label"), description));
        form.Children.Add(Field(I18n.T("hub.f.changelog.label"), changelog));
        form.Children.Add(Field(I18n.T("hub.f.tags"), tags));
        form.Children.Add(Field(I18n.T("hub.f.images.label"), images));
        form.Children.Add(new Border
        {
            Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12),
            Child = Ui.Row(10, Ui.Icon(Icons.Shield, 16, Ui.Res("Muted")), Ui.Text(I18n.T("hub.f.rules"), "small muted", wrap: true)),
        });
        form.Children.Add(bar);
        form.Children.Add(status);
        form.Children.Add(fix);
        var scroller = new ScrollViewer { Content = form, MaxHeight = 520, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        Button? go = null;
        go = Ui.Button(I18n.T("cr.publish"), async () =>
        {
            status.Foreground = Ui.Res("Muted");
            fix.IsVisible = false;
            d.Name = name.Text ?? "";
            d.Summary = summary.Text ?? "";
            d.Description = description.Text ?? "";
            d.Version = (version.Text ?? "").Trim();
            d.Changelog = changelog.Text ?? "";
            if (!codeGame)
            {
                if (game.SelectedIndex < 0) { Problem(I18n.T("v26.bugs.game.required")); return; }
                d.Game = defs[game.SelectedIndex].Id;
            }
            d.Tags = [.. chosen];
            d.Images = (images.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (d.Images.Any(u => !u.StartsWith("https://"))) { Problem(I18n.T("hub.f.imagesBad")); return; }
            if (!Regex.IsMatch(d.Version, @"^\d+\.\d+\.\d+$")) { Problem(I18n.T("v26.bugs.version.format")); return; }
            // Версия только растёт: проверяем сразу, без сети (сервер проверит ещё раз).
            if (from.Existing is { } ex && Hub.CompareVersions(d.Version, ex.Version) <= 0)
            {
                Problem(I18n.T("v26.bugs.version.old", ("version", d.Version), ("current", ex.Version), ("next", Hub.NextVersion(ex.Version))), Hub.NextVersion(ex.Version));
                return;
            }
            string? original = null;
            if (project is not null)
            {
                // Код — как он сохранён сейчас, с версией выпуска; ошибки и незнакомая игра — до загрузки.
                try { original = File.ReadAllText(project.Script); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Problem(e.Message); return; }
                d.Code = Hub.SetCodeVersion(original, d.Version);
                built = ModScript.Compile(d.Code);
                if (!built.Ok) { Problem(I18n.T("cr.build.hasErrors")); return; }
                if (GameCatalog.ById(built.Game) is null) { Problem(I18n.T("v26.bugs.game.unknown", ("game", built.Game))); return; }
                d.Game = built.Game;
                if (kind == "script" && Hub.NeedsFiles(built))
                {
                    if (packageRadio is not null) packageRadio.IsChecked = true;
                    Problem(I18n.T("v26.bugs.files.packageOnly"));
                    return;
                }
            }
            else if (codeGame && GameCatalog.ById(d.Game) is null) { Problem(I18n.T("v26.bugs.game.unknown", ("game", d.Game))); return; }
            go!.IsEnabled = false;
            bar.IsVisible = true;
            try
            {
                d.Archive = kind switch
                {
                    "file" => archive ?? throw new Social.ServiceError("NO_FILE"),
                    "package" when project is not null => (await Projects.Pack(built!, project.Dir)).Zip,
                    "package" => from.Pack is null ? null : await from.Pack(),
                    _ => null,
                };
                if (kind == "file") d.Code = "";
                if (kind == "stored") d.Code = from.Existing!.Code;
                status.Text = I18n.T("hub.f.uploading");
                await Hub.Publish(d, new Progress<double>(r => bar.Value = r));
                if (project is not null) Released(project, d, original);
                w.CloseDialog();
                w.Toast(I18n.T("cr.publish.done", ("name", d.Name)));
                done?.Invoke();
            }
            catch (Social.ServiceError e) when (e.Code == "OLD_VERSION")
            {
                go.IsEnabled = true;
                bar.IsVisible = false;
                Problem(I18n.T("v26.bugs.version.old", ("version", d.Version), ("current", e.Message), ("next", Hub.NextVersion(e.Message))), Hub.NextVersion(e.Message));
            }
            catch (Social.ServiceError e) when (e.Code == "NAME_TAKEN")
            {
                go.IsEnabled = true;
                bar.IsVisible = false;
                Problem(I18n.T("v26.bugs.publish.nameTaken", ("name", e.Message)));
            }
            catch (Exception e)
            {
                go.IsEnabled = true;
                bar.IsVisible = false;
                Problem(Social.Firebase.Explain("creator", e));
            }
        }, "primary", Icons.Upload);
        // Игра из кода незнакома — выкладывать нечего, пока строку game не исправят.
        go.IsEnabled = !unknownGame;
        LastGo = go;
        w.Dialog(I18n.T(lockName ? "hub.newVersion" : "hub.publish.title"), scroller, Ui.Button(I18n.T("common.cancel"), w.CloseDialog), go);
    }

    /// <summary>
    /// После выпуска проекта: версия в main.mls = выложенной (следующий выпуск начнётся с неё), прежний текст — в историю
    /// файла; если файл успели поменять за время загрузки — не трогаем. В project.json — id карточки и выпуск.
    /// </summary>
    static void Released(Project project, HubDraft d, string? original)
    {
        try
        {
            if (original is not null && d.Code != original && File.Exists(project.Script) && File.ReadAllText(project.Script) == original)
            {
                XHistory.Snap(project.Script, original, File.GetLastWriteTimeUtc(project.Script));
                Projects.Save(project, d.Code);
                XHistory.Snap(project.Script, d.Code);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (d.Id is { } id) ProjectMeta.Update(project.Dir, m => { m.HubId = id; m.NoteRelease(d.Version, "hub", DateTime.UtcNow); });
    }
}
