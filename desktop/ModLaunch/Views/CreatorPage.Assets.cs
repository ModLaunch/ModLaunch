using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Views;

/// <summary>Раздел «Модели и ассеты»: библиотека файлов для модов и подсказки по форматам.</summary>
public sealed partial class CreatorPage
{
    AssetKind? _assetKind;

    static string KindIcon(AssetKind k) => k switch
    {
        AssetKind.Model => Icons.Package,
        AssetKind.Texture => Icons.Image,
        AssetKind.Sound => Icons.Music,
        AssetKind.Bundle => Icons.Layers,
        _ => Icons.List,
    };

    static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024:0.#} MB",
    };

    Control AssetsView()
    {
        var col = new StackPanel { Spacing = 16 };
        var all = AssetLibrary.List();

        var chips = Ui.Row(6);
        void Chip(AssetKind? kind, string label, int n)
        {
            var chip = Ui.Button($"{label} · {n}", () => { _assetKind = kind; Build(); }, "chip");
            if (_assetKind == kind) chip.Classes.Add("active");
            chips.Children.Add(chip);
        }
        Chip(null, I18n.T("cr.cat.all"), all.Count);
        foreach (var k in Enum.GetValues<AssetKind>()) Chip(k, I18n.T("cr.asset.k." + k), all.Count(a => a.Kind == k));

        var import = Ui.Button(I18n.T("cr.asset.import"), () => _ = ImportAssets(), "primary", Icons.Plus);
        var bar = new DockPanel();
        var right = Ui.Row(8, Ui.Button(I18n.T("cr.asset.folder"), () => Actions.OpenFolder(AssetLibrary.Root), "ghost", Icons.Folder), import);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        col.Children.Add(bar);

        // Подсказка по выбранному виду: какой формат нужен и чем его сделать.
        var hintKind = _assetKind ?? AssetKind.Model;
        col.Children.Add(new Border
        {
            Background = Ui.Hex("#142A2350"), BorderBrush = Ui.Hex("#447C5CFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12),
            Child = Ui.Row(12, Ui.Icon(Icons.Info, 18, Ui.Res("Brand2")), new TextBlock { Text = I18n.T("cr.asset.hint." + hintKind), TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Res("Muted"), MaxWidth = 1100 }),
        });

        var shown = all.Where(a => (_assetKind is null || a.Kind == _assetKind) && (_filter == "" || a.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))).ToList();
        if (shown.Count == 0)
        {
            col.Children.Add(Ui.Card(Ui.Col(10, Ui.Text(I18n.T("cr.asset.empty"), "h3"), Ui.Text(I18n.T("cr.asset.empty.text"), "muted", wrap: true),
                Ui.Row(8, Ui.Button(I18n.T("cr.asset.import"), () => _ = ImportAssets(), "primary", Icons.Plus), Ui.Button(I18n.T("cr.nav.guides"), () => { _guide = "model"; Go("guides"); }, "", Icons.Book))), 28));
            return col;
        }

        var grid = new UniformGrid { Columns = 4 };
        foreach (var a in shown)
        {
            var item = a;
            Control preview = new Border { Height = 120, Background = Ui.Res("Surface2"), CornerRadius = new CornerRadius(10), Child = Ui.Icon(KindIcon(a.Kind), 34, Ui.Res("Muted")) };
            string? dims = null;
            if (AssetLibrary.HasPreview(a))
            {
                try
                {
                    using var stream = File.OpenRead(a.FullPath);
                    var bmp = Bitmap.DecodeToWidth(stream, 260);
                    preview = new Border { Height = 120, CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Ui.Res("Surface2"), Child = new Image { Source = bmp, Stretch = Stretch.Uniform } };
                }
                catch { }
                dims = PngSize(a.FullPath);
            }
            var meta = $"{a.Ext} · {Size(a.Size)}" + (dims is null ? "" : $" · {dims}");
            var card = Ui.Card(Ui.Col(8, preview, Ui.Text(a.Name, "h3"), Ui.Text(meta, "small muted"),
                Ui.Row(6,
                    Ui.Button(I18n.T("cr.asset.use"), () => UseAsset(item), "primary", Icons.Plus),
                    Ui.Button("", () => Actions.OpenFolder(Path.GetDirectoryName(item.FullPath)), "icon", Icons.Folder, I18n.T("cr.files")),
                    Ui.Button("", () => Copy(item.FullPath, I18n.T("cr.asset.pathCopied")), "icon", Icons.Link, I18n.T("cr.asset.copyPath")),
                    Ui.Button("", () => { AssetLibrary.Remove(item); Build(); }, "icon ghost", Icons.Trash, I18n.T("cr.delete")))), 14);
            card.Margin = new Thickness(0, 0, 12, 12);
            grid.Children.Add(card);
        }
        col.Children.Add(grid);
        return col;
    }

    /// <summary>Размер PNG из заголовка файла — без распаковки всей картинки.</summary>
    static string? PngSize(string path)
    {
        try
        {
            using var s = File.OpenRead(path);
            var h = new byte[24];
            if (s.Read(h, 0, 24) < 24 || h[1] != (byte)'P' || h[2] != (byte)'N' || h[3] != (byte)'G') return null;
            int Be(int i) => h[i] << 24 | h[i + 1] << 16 | h[i + 2] << 8 | h[i + 3];
            return $"{Be(16)}×{Be(20)}";
        }
        catch { return null; }
    }

    async Task ImportAssets()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = I18n.T("cr.asset.import"), AllowMultiple = true });
        var paths = result.Select(f => f.TryGetLocalPath()).Where(p => p is not null).ToList();
        if (paths.Count == 0) return;
        var ok = 0;
        foreach (var path in paths)
        {
            try { AssetLibrary.Import(path!, ""); ok++; }
            catch (Exception e) { MainWindow.Current?.Toast($"{Path.GetFileName(path)}: {e.Message}", bad: true); }
        }
        if (ok > 0) MainWindow.Current?.Toast(I18n.T("cr.asset.imported", ("n", ok)));
        Build();
    }

    /// <summary>«Добавить в мод»: окно со списком проектов обоих видов; ассет попадает в выбранный.</summary>
    void UseAsset(AssetItem a)
    {
        var w = MainWindow.Current!;
        var list = Ui.Col(6);
        var scripts = Projects.List();
        var codes = CodeProjects.List();
        if (scripts.Count + codes.Count == 0)
        {
            list.Children.Add(Ui.Text(I18n.T("cr.asset.noProjects"), "muted", wrap: true));
        }
        foreach (var p in scripts)
        {
            var pp = p;
            var b = Ui.Button($"{p.Name}  ·  {I18n.T("cr.studio.kind.script")}", () =>
            {
                w.CloseDialog();
                try { var line = AssetLibrary.AttachToScript(a, pp); w.Toast(I18n.T("cr.asset.addedScript", ("name", pp.Name))); }
                catch (Exception e) { w.Toast(e.Message, bad: true); }
            }, "ghost");
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            list.Children.Add(b);
        }
        foreach (var p in codes)
        {
            var pp = p;
            var b = Ui.Button($"{p.Name}  ·  {CodeProjects.Label(p.Kind)}", () =>
            {
                w.CloseDialog();
                try { var rel = AssetLibrary.AttachToCode(a, pp); w.Toast(I18n.T("cr.asset.addedCode", ("file", rel))); }
                catch (Exception e) { w.Toast(e.Message, bad: true); }
            }, "ghost");
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            list.Children.Add(b);
        }
        w.Dialog(I18n.T("cr.asset.use.title", ("name", a.Name)), new ScrollViewer { Content = list, MaxHeight = 360, Width = 460 }, Ui.Button(I18n.T("common.cancel"), w.CloseDialog));
    }
}
