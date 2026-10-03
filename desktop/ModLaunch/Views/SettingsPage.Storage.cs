using Avalonia.Controls;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

public sealed partial class SettingsPage
{
    /// <summary>«Занимаемое место»: сколько весят данные ModLaunch и кнопки «Очистить» там, где это безопасно.</summary>
    Control StorageSection()
    {
        var parts = Storage.Measure();
        var rows = new List<Control>();
        foreach (var p in parts)
        {
            var row = new DockPanel();
            if (p.Clearable)
            {
                var clear = Ui.Button(I18n.T("storage.clear"), () =>
                {
                    var freed = Storage.Clear(p.Dir);
                    MainWindow.Current?.Toast(I18n.T("storage.cleared", ("size", GamePage.Size(freed))));
                    Build();
                }, "ghost", Icons.Trash);
                DockPanel.SetDock(clear, Dock.Right);
                row.Children.Add(clear);
            }
            var size = Ui.Text(GamePage.Size(p.Bytes), "h3");
            size.VerticalAlignment = VerticalAlignment.Center;
            size.Margin = new Avalonia.Thickness(0, 0, 14, 0);
            DockPanel.SetDock(size, Dock.Right);
            row.Children.Add(size);
            row.Children.Add(Ui.Col(2, Ui.Text(I18n.T("storage." + p.Key), "h3"), Ui.Text(I18n.T("storage." + p.Key + ".hint"), "small muted", wrap: true)));
            rows.Add(row);
        }
        rows.Add(Ui.Text(I18n.T("storage.total", ("size", GamePage.Size(parts.Sum(p => p.Bytes)))), "muted"));
        return Section(I18n.T("storage.title"), null, rows.ToArray());
    }
}
