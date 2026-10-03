using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>Вкладка «Скриншоты»: плитки из Steam и папки игры, клик открывает снимок.</summary>
public sealed partial class GamePage
{
    List<string>? _shotFiles;

    List<string> ShotFiles() => _shotFiles ??= ScreenshotGallery.Find(ScreenshotGallery.Folders(_g.Def, _g.Path));

    Control ShotsView()
    {
        var files = ShotFiles();
        var folders = ScreenshotGallery.Folders(_g.Def, _g.Path);
        var head = new DockPanel();
        var open = Ui.Button(I18n.T("shots.folder"), () => Actions.OpenFolder(folders.FirstOrDefault()), "", Icons.Folder);
        DockPanel.SetDock(open, Dock.Right);
        if (folders.Count > 0) head.Children.Add(open);
        head.Children.Add(Ui.Text(I18n.T("shots.title." + I18n.Plural(files.Count, "one", "few", "many"), ("n", files.Count)), "h2"));

        var wrap = new WrapPanel();
        foreach (var file in files)
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            var tile = new Button
            {
                Classes = { "card-btn" }, Padding = new Thickness(0), Margin = new Thickness(0, 0, 12, 12), Width = 232, Height = 131, ClipToBounds = true,
                Content = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = image },
            };
            var path = file;
            tile.Click += (_, _) => Ui.OpenUrl(path);
            ToolTip.SetTip(tile, System.IO.Path.GetFileName(path) + " · " + File.GetLastWriteTime(path).ToString("d", I18n.Culture));
            wrap.Children.Add(tile);
            // Картинки читаем не в потоке окна: сразу уменьшаем до ширины плитки.
            _ = Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var bitmap = Bitmap.DecodeToWidth(stream, 464);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = bitmap);
                }
                catch { }
            });
        }
        return Ui.Col(14, head, files.Count == 0 ? Ui.Text(I18n.T("shots.none"), "muted", wrap: true) : wrap);
    }
}
