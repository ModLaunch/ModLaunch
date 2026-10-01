using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>Раздел «Упаковка»: папка с готовым модом → zip для Thunderstore, Nexus или просто архив, с проверкой на лету.</summary>
public sealed partial class CreatorPage
{
    PackSpec _pack = new();
    readonly StackPanel _packProblems = new() { Spacing = 8 };
    bool _packing;

    Control PackView()
    {
        var spec = _pack;
        var game = GameCatalog.ById(spec.Game);

        var name = Box(spec.Name, I18n.T("cr.pack.f.name"), max: 60);
        name.TextChanged += (_, _) => { spec.Name = name.Text ?? ""; RenderProblems(); };
        var version = Box(spec.Version, "1.0.0", max: 20);
        version.Width = 140;
        version.TextChanged += (_, _) => { spec.Version = version.Text ?? ""; RenderProblems(); };
        var desc = Box(spec.Description, I18n.T("cr.pack.f.desc"), multi: true, max: 400);
        desc.MinHeight = 80;
        desc.TextChanged += (_, _) => { spec.Description = desc.Text ?? ""; RenderProblems(); };
        var site = Box(spec.Website, "https://", max: 200);
        site.TextChanged += (_, _) => spec.Website = site.Text ?? "";
        var needs = Box(string.Join(", ", spec.Needs), I18n.T("cr.pack.f.needs"), max: 400);
        needs.TextChanged += (_, _) => spec.Needs = (needs.Text ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        // Папка с модом и значок — поле показывает путь, рядом кнопка выбора.
        var source = Box(spec.Source, I18n.T("cr.pack.f.source"), max: 500);
        source.TextChanged += (_, _) => { spec.Source = (source.Text ?? "").Trim(); RenderProblems(); };
        var pickSource = Ui.Button(I18n.T("cr.pack.choose"), async () =>
        {
            if (await MainWindow.Current!.PickFolder(I18n.T("cr.pack.f.source")) is { } dir) { spec.Source = dir; Build(); }
        }, "", Icons.Folder);
        var icon = Box(spec.Icon ?? "", I18n.T("cr.pack.f.icon"), max: 500);
        icon.TextChanged += (_, _) => { spec.Icon = string.IsNullOrWhiteSpace(icon.Text) ? null : icon.Text!.Trim(); RenderProblems(); };
        var pickIcon = Ui.Button(I18n.T("cr.pack.choose"), async () =>
        {
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            var r = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = I18n.T("cr.pack.f.icon"), AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("PNG / JPG") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }] });
            if (r.FirstOrDefault()?.TryGetLocalPath() is { } path) { spec.Icon = path; Build(); }
        }, "", Icons.Image);
        Control WithButton(TextBox box, Button button) { var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 }; g.Children.Add(box); Grid.SetColumn(button, 1); g.Children.Add(button); return g; }

        // Быстрое заполнение: из проектов на C#, у которых уже есть сборка.
        var fromProjects = Ui.Row(6);
        foreach (var p in CodeProjects.List().Where(p => CodeProjects.OutputDir(p) is not null).Take(6))
        {
            var pp = p;
            var chip = Ui.Button(pp.Name, () =>
            {
                _pack = new PackSpec { Name = CodeProjects.Identifier(pp.Name), Game = pp.Game, Description = pp.Name, Source = CodeProjects.OutputDir(pp)!, Icon = spec.Icon };
                Build();
            }, "chip", Icons.Code);
            fromProjects.Children.Add(chip);
        }

        var format = game is null ? "?" : game.Loader switch { LoaderKind.Bepinex => "Thunderstore (BepInEx)", LoaderKind.Smapi => "SMAPI → Nexus Mods", _ => "zip" };
        var form = Ui.Col(14,
            Field(I18n.T("cr.pack.game"), GamePicker(spec.Game, id => { spec.Game = id; Build(); })),
            Ui.Text(I18n.T("cr.pack.format", ("format", format)), "small", color: Ui.Res("Brand2")),
            Field(I18n.T("cr.pack.source"), WithButton(source, pickSource)),
            Ui.Row(14, Field(I18n.T("cr.pack.name"), name), Field(I18n.T("cr.pack.version"), version)),
            Field(I18n.T("cr.pack.desc"), desc),
            Field(I18n.T("cr.pack.site"), site),
            Field(I18n.T("cr.pack.needs"), needs),
            Field(I18n.T("cr.pack.icon"), WithButton(icon, pickIcon)));
        if (fromProjects.Children.Count > 0)
            form.Children.Insert(0, Field(I18n.T("cr.pack.fromProject"), new ScrollViewer { Content = fromProjects, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }));

        var pack = Ui.Button(_packing ? I18n.T("cr.pack.packing") : I18n.T("cr.pack.go"), () => _ = RunPack(), "primary", Icons.Package);
        pack.FontSize = 15;
        pack.Padding = new Thickness(24, 12);
        pack.IsEnabled = !_packing;

        var side = Ui.Col(14,
            Ui.Card(Ui.Col(10, Ui.Text(I18n.T("cr.pack.check"), "h3"), _packProblems, pack), 18),
            Ui.Card(Ui.Col(8, Ui.Text(I18n.T("cr.pack.where"), "h3"), Ui.Text(I18n.T("cr.pack.where.text"), "small muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("cr.pack.folder"), () => Actions.OpenFolder(Packager.OutDir), "ghost", Icons.Folder), Ui.Button(I18n.T("cr.pack.checklist"), () => { _guide = "check"; Go("guides"); }, "ghost", Icons.Book))), 18));
        side.VerticalAlignment = VerticalAlignment.Top;
        RenderProblems();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,380"), ColumnSpacing = 16 };
        grid.Children.Add(Ui.Card(form, 22));
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        return grid;
    }

    /// <summary>Правая колонка: что не так с пакетом. Ошибки мешают, предупреждения только подсказывают.</summary>
    void RenderProblems()
    {
        _packProblems.Children.Clear();
        var problems = Packager.Validate(_pack);
        if (problems.Count == 0) { _packProblems.Children.Add(Dotted(true, I18n.T("cr.pack.ok"))); return; }
        foreach (var p in problems.OrderByDescending(p => p.Error)) _packProblems.Children.Add(Dotted(false, I18n.T(p.Key, p.Args), warn: !p.Error));
    }

    async Task RunPack()
    {
        var w = MainWindow.Current!;
        var spec = _pack;
        if (Packager.Validate(spec).FirstOrDefault(p => p.Error) is { } error) { w.Toast(I18n.T(error.Key, error.Args), bad: true); return; }
        _packing = true;
        Build();
        try
        {
            var packed = await Packager.Pack(spec);
            _packing = false;
            Build();
            var game = GameCatalog.ById(spec.Game);
            var list = Ui.Col(4, packed.Files.Take(16).Select(f => (Control)Ui.Text("• " + f, "small muted")).ToArray());
            if (packed.Files.Count > 16) list.Children.Add(Ui.Text($"… +{packed.Files.Count - 16}", "small muted"));
            var body = Ui.Col(10,
                Ui.Text(I18n.T("cr.export.text", ("format", packed.Format)), "muted", wrap: true),
                new Border { Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = list });
            foreach (var warn in packed.Warnings) body.Children.Add(Ui.Text("⚠ " + warn, "small", color: Ui.Res("Warn"), wrap: true));
            var buttons = new List<Control> { Ui.Button(I18n.T("common.close"), w.CloseDialog), Ui.Button(I18n.T("cr.export.folder"), () => Actions.OpenFolder(Path.GetDirectoryName(packed.Zip)), "", Icons.Folder) };
            if (game?.ThunderstoreCommunity is not null && game.Loader == LoaderKind.Bepinex)
                buttons.Add(Ui.Button("Thunderstore", () => Ui.OpenUrl($"https://thunderstore.io/c/{game.ThunderstoreCommunity}/create/"), "primary", Icons.External));
            else if (game?.NexusDomain is not null)
                buttons.Add(Ui.Button("Nexus Mods", () => Ui.OpenUrl($"https://www.nexusmods.com/{game.NexusDomain}/mods/add"), "primary", Icons.External));
            w.Dialog(I18n.T("cr.export.title", ("name", Path.GetFileName(packed.Zip))), body, buttons.ToArray());
        }
        catch (Exception e)
        {
            _packing = false;
            Build();
            w.Toast(e.Message, bad: true);
        }
    }
}
