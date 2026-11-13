using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>28.0: карточка «Облачные сохранения» на вкладке «Сохранения» игры.</summary>
public sealed partial class GamePage
{
    bool _cloudBusy;

    Control CloudSavesCard()
    {
        var id = _g.Def.Id;
        var on = CloudSaves.Enabled(id);
        var places = CloudSaves.Places().Where(p => p.Available && p.Folder is not null).ToList();

        var toggle = new ToggleSwitch { IsChecked = on, OnContent = "", OffContent = "", IsEnabled = places.Count > 0 && !_cloudBusy, VerticalAlignment = VerticalAlignment.Center };
        toggle.IsCheckedChanged += (_, _) =>
        {
            var want = toggle.IsChecked == true;
            if (want == CloudSaves.Enabled(id)) return;
            CloudSaves.SetEnabled(id, want);
            if (want) _ = Sync();
            else Build();
        };
        var head = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        head.Children.Add(toggle);
        head.Children.Add(Ui.Row(10, Ui.Icon(Icons.Cloud, 18, Ui.Res("Brand2")), Ui.Text(I18n.T("v28.cs.title"), "h2")));

        var col = Ui.Col(10, head, Ui.Text(I18n.T("v28.cs.lead"), "muted small", wrap: true));
        if (places.Count == 0)
        {
            col.Children.Add(new Border
            {
                Background = Ui.Soft("#3A2A12", "#F2B84B"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10),
                Child = Ui.Text(I18n.T("v28.cs.noPlaces"), "small", wrap: true),
            });
            return Ui.Card(col, 22);
        }

        var picker = new ComboBox { ItemsSource = places.Select(p => p.Title).ToList(), MinWidth = 200, IsEnabled = !_cloudBusy };
        picker.SelectedIndex = Math.Max(0, places.FindIndex(p => p.Id == CloudSaves.PlaceId));
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0 || places[picker.SelectedIndex].Id == CloudSaves.PlaceId) return;
            CloudSaves.PlaceId = places[picker.SelectedIndex].Id;
            if (CloudSaves.Enabled(id)) _ = Sync(); else Build();
        };
        var where = Ui.Row(10, Ui.Text(I18n.T("v28.cs.where"), "small muted"), picker);
        foreach (var c in where.Children) c.VerticalAlignment = VerticalAlignment.Center;
        col.Children.Add(where);

        if (on)
        {
            col.Children.Add(Ui.Text(_cloudBusy ? I18n.T("v28.cs.syncing")
                : CloudSaves.SyncedAt(id) is DateTime at ? I18n.T("v28.cs.synced", ("when", Ui.Ago(at))) : I18n.T("v28.cs.never"), "small", wrap: true));
            if (CloudSaves.CloudDir(id) is { } dir) col.Children.Add(Ui.Text(dir, "tiny muted", wrap: true));
            var sync = Ui.Button(I18n.T("v28.cs.syncNow"), () => _ = Sync(), "", Icons.Refresh);
            var pull = Ui.Button(I18n.T("v28.cs.pull"), AskPull, "ghost", Icons.Download);
            sync.IsEnabled = pull.IsEnabled = !_cloudBusy && !Launcher.IsRunning(id);
            col.Children.Add(Ui.Row(8, sync, pull));
        }
        return Ui.Card(col, 22);
    }

    async Task Sync()
    {
        if (_g.Path is null) return;
        if (Launcher.IsRunning(_g.Def.Id)) { MainWindow.Current?.Toast(I18n.T("v28.cs.running"), bad: true); return; }
        _cloudBusy = true;
        Build();
        // Спор остаётся открытым диалогом — карточку отпускаем сразу, а после выбора перерисуем.
        await Actions.CloudSync(_g, () => Avalonia.Threading.Dispatcher.UIThread.Post(Build), tell: true);
        _cloudBusy = false;
        Build();
    }

    void AskPull()
    {
        if (MainWindow.Current is not { } w || _g.Path is null) return;
        w.Dialog(I18n.T("v28.cs.pull.title"), Ui.Text(I18n.T("v28.cs.pull.text"), "muted", wrap: true),
            Ui.Button(I18n.T("common.cancel"), w.CloseDialog),
            Ui.Button(I18n.T("v28.cs.pull"), async () =>
            {
                w.CloseDialog();
                _cloudBusy = true;
                Build();
                try
                {
                    var cloud = await Task.Run(() => CloudSaves.Pull(_g.Def, _g.Path!));
                    w.Toast(I18n.T("v28.cs.pulled", ("device", cloud?.Device ?? "?")));
                }
                catch (Exception e) { w.Toast(I18n.T("v28.cs.pullFailed", ("error", Jobs.Explain(e))), bad: true); }
                _cloudBusy = false;
                Build();
            }, "primary", Icons.Download));
    }
}
