using Avalonia.Controls;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

public sealed partial class MainWindow
{
    /// <summary>
    /// 23.0: игра с модами из Creator Hub закрылась — спросить, оставить ли прогресс или вернуть сохранения из копии
    /// «до». Если среди модов были читы, «Вернуть как было» — главная кнопка (читы не попадают в настоящий прогресс).
    /// </summary>
    void AskProgress(GameState g)
    {
        if (ProgressGuard.Current(g.Def.Id) is not { } s) return;
        var time = s.At.ToLocalTime().ToString("t");
        var text = I18n.T(s.Cheats ? "v23.pg.cheats" : "v23.pg.text", ("game", g.Def.Name), ("time", time));
        void Done(bool keep)
        {
            CloseDialog();
            try
            {
                ProgressGuard.Finish(g, keep);
                Toast(I18n.T(keep ? "v23.pg.kept" : "v23.pg.restored", ("time", time)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException) { Toast(e.Message, bad: true); }
            AppState.Notify();
        }
        var restore = Ui.Button(I18n.T("v23.pg.restore"), () => Done(false), s.Cheats ? "primary" : "", Icons.Undo);
        var keep = Ui.Button(I18n.T("v23.pg.keep"), () => Done(true), s.Cheats ? "ghost" : "primary", Icons.Check);
        Dialog(I18n.T("v23.pg.title"), Ui.Text(text, "muted", wrap: true), keep, restore);
    }
}
