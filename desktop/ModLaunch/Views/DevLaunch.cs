using Avalonia.Controls;
using Avalonia.Media;
using ModLaunch.Core;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Views;

/// <summary>
/// 23.0: запуск «как разработчик» из любого места (кнопка «Играть», «Новый мод → Карта и мир», оверлей, «Мои моды»):
/// собрать конструктор под игру, включить защиту прогресса и запустить. Нет BepInEx — предложить поставить.
/// </summary>
public static class DevLaunch
{
    public static async void Start(GameState g)
    {
        var w = MainWindow.Current;
        if (w is null || g.Path is null) return;
        if (Launcher.IsRunning(g.Def.Id)) { w.Toast(I18n.T("v23.dev.running"), bad: true); return; }
        if (DevMode.Unsupported(g.Path) is { } why)
        {
            if (why == "v23.dev.no.bepinex" && g.Def.Loader == LoaderKind.Bepinex)
            {
                w.Dialog(I18n.T("v23.dev.title"), Ui.Text(I18n.T(why), "muted", wrap: true),
                    Ui.Button(I18n.T("common.cancel"), w.CloseDialog, "ghost"),
                    Ui.Button(I18n.T("games.installLoader", ("loader", g.Def.LoaderName)), () => { w.CloseDialog(); Actions.InstallLoader(g); }, "primary", Icons.Download));
                return;
            }
            w.Toast(I18n.T(why, ("ac", "")), bad: true);
            return;
        }
        var path = g.Path;
        var cyberpunk = DevMode.KindOf(path) == "cyberpunk";
        w.Toast(I18n.T(DevMode.Key(path, "preparing"), ("game", g.Def.Name)));
        // Что ставим сейчас (у Cyberpunk — инструменты с GitHub, это может занять минуту).
        var progress = new Progress<string>(tool => w.Toast(I18n.T("v23.dev.cp.installing", ("tool", tool))));
        string? error;
        // 23.7: меню под эту игру (метки Steam и цвет обложки) — до установки, чтобы его прочли и плашки движков.
        try { await GameProfile.Apply(path, g.Def.Name, g.Def.SteamAppId); } catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or InvalidOperationException) { }
        try { error = await Task.Run(() => DevMode.Prepare(path, progress)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException) { error = e.Message; }
        if (error is not null)
        {
            var text = new SelectableTextBlock { Text = error, FontFamily = HubLook.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 280 };
            w.Dialog(I18n.T("v23.dev.failed"), new ScrollViewer { Content = text, MaxHeight = 300 }, Ui.Button(I18n.T("common.close"), w.CloseDialog, "primary"));
            return;
        }
        // Защита прогресса — в фоне (копия сохранений бывает большой). Конструктор мира Cyberpunk сохранений не трогает.
        if (!cyberpunk) await Task.Run(() => { try { ProgressGuard.Begin(g, force: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { } });
        Actions.Play(g, skipUpdate: true);
        w.Toast(I18n.T(DevMode.Key(path, "started"), ("game", g.Def.Name)));
    }

    /// <summary>Можно ли играть как разработчик (для показа кнопок).</summary>
    public static bool Can(GameState? g) => g?.Path is { } p && g.Status == Detect.Found && DevMode.Unsupported(p) is null;
}
