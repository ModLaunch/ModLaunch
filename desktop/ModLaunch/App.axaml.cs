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
