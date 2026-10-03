using Avalonia.Headless;
using ModLaunch.Core;
using ModLaunch.Setup;

namespace ModLaunch.Views;

public sealed partial class SetupWindow
{
    [DemoShots]
    static void SetupShots(Shots s)
    {
        var setup = new SetupWindow(SetupMode.Install);
        setup.Show();
        s.Pump(700);
        setup.CaptureRenderedFrame()?.Save(Path.Combine(s.OutDir, "setup-1-welcome.png"));
        Console.WriteLine("saved setup-1-welcome");
        setup.DemoProgress(0.64);
        s.Pump(700);
        setup.CaptureRenderedFrame()?.Save(Path.Combine(s.OutDir, "setup-2-progress.png"));
        Console.WriteLine("saved setup-2-progress");
        setup.DemoDone();
        s.Pump(700);
        setup.CaptureRenderedFrame()?.Save(Path.Combine(s.OutDir, "setup-3-done.png"));
        Console.WriteLine("saved setup-3-done");
        setup.Close();
        var remove = new SetupWindow(SetupMode.Uninstall);
        remove.Show();
        s.Pump(700);
        remove.CaptureRenderedFrame()?.Save(Path.Combine(s.OutDir, "setup-4-uninstall.png"));
        Console.WriteLine("saved setup-4-uninstall");
        remove.Close();
    }
}
