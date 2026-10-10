using Avalonia;
using Avalonia.Controls;
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
        Guard.Install();
        I18n.Set(Settings.Language);
        Look.Apply();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Программа выходит, когда закрыто главное окно, — даже если где-то осталось
            // спрятанное окошко (оверлей в игре, подсказка): раньше оно держало процесс.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = Program.SetupMode == ModLaunch.Setup.SetupMode.None
                ? new MainWindow()
                : new SetupWindow(Program.SetupMode);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
