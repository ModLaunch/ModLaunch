using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ModLaunch.Core;
using ModLaunch.Views;

namespace ModLaunch;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Ошибка в обработчике нажатия не должна закрывать программу: пишем в журнал и сообщаем коротко.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashLog.Write("ui", e.Exception);
            e.Handled = true;
            try { MainWindow.Current?.Toast(I18n.T("crash.toast", ("message", e.Exception.Message)), bad: true); } catch { }
        };
        I18n.Set(Settings.Language);
        Look.Apply();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Program.SetupMode == ModLaunch.Setup.SetupMode.None
                ? new MainWindow()
                : new SetupWindow(Program.SetupMode);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
