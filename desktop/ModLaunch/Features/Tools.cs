using System.Diagnostics;
using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Features;

public sealed record Tool(string Name, string Exe);

/// <summary>Свои программы у игры (как «Tools» в Vortex): settings.tools[игра].</summary>
public static class Tools
{
    public static List<Tool> For(string gameId) =>
        Settings.Data.Obj("tools").Arr(gameId).Select(t => new Tool(t.Str("name") ?? "", t.Str("exe") ?? "")).Where(t => t.Exe != "").ToList();

    public static void Add(string gameId, string exe)
    {
        var list = Settings.Data.Obj("tools").Arr(gameId).Select(x => x!.DeepClone()).Where(x => x.Str("exe") != exe).ToList();
        list.Add(new JsonObject { ["name"] = Path.GetFileNameWithoutExtension(exe), ["exe"] = exe });
        Settings.Data.Obj("tools")[gameId] = new JsonArray(list.Take(20).ToArray());
        Settings.Save();
    }

    public static void Remove(string gameId, string exe)
    {
        Settings.Data.Obj("tools")[gameId] = new JsonArray(Settings.Data.Obj("tools").Arr(gameId).Where(x => x.Str("exe") != exe).Select(x => x!.DeepClone()).ToArray());
        Settings.Save();
    }

    public static void Run(Tool tool)
    {
        Process.Start(new ProcessStartInfo(tool.Exe) { WorkingDirectory = Path.GetDirectoryName(tool.Exe)!, UseShellExecute = true });
    }
}
