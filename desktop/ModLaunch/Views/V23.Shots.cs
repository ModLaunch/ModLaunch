using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 23.9 (MODLAUNCH_SHOTS=V23): «Почему игра упала» с «Исправить», код сборки, сборка друга по коду,
/// «Обновить всё» на странице игры и на главной.
/// </summary>
public static class V23Shots
{
    [DemoShots]
    static void V23Stage1(Shots s)
    {
        var w = s.Window;
        var lc = AppState.Game("lethal-company");
        var reg = lc.Registry!;
        foreach (var (id, name, version) in new[] { ("Evaisa-LethalThings", "LethalThings", "0.10.7"), ("notnotnotswipez-MoreCompany", "MoreCompany", "1.10.1"), ("tinyhoot-ShipLoot", "ShipLoot", "1.0.0") })
        {
            Directory.CreateDirectory(Path.Combine(reg.ModsDir, name));
            reg.Add(new JsonObject { ["id"] = id, ["name"] = name, ["folder"] = name, ["version"] = version, ["source"] = "thunderstore", ["author"] = id.Split('-')[0] });
        }
        ModUpdates.Found["lethal-company"] =
        [
            new("notnotnotswipez-MoreCompany", "notnotnotswipez-MoreCompany", "MoreCompany", "1.10.1", "1.11.0", null, false, "thunderstore"),
            new("Evaisa-LethalLib", "Evaisa-LethalLib", "LethalLib", "0.16.1", "1.0.1", null, false, "thunderstore"),
            new("x753-More_Suits", "x753-More_Suits", "More Suits", "1.4.3", "1.4.5", null, false, "thunderstore"),
        ];
        GamePatch.Patched["lethal-company"] = DateTime.UtcNow;
        AppState.Notify();

        w.Navigate(() => new GamePage("lethal-company", "installed"));
        s.Pump(400);
        var report = CrashDoctor.Diagnose(lc,
        [
            new("missing", "LethalThings", "evaisa.lethallib", "[Error  :   BepInEx] Could not load [LethalThings 0.10.7] because it has missing dependencies: evaisa.lethallib (v0.13.0 or newer)"),
            new("outdated", "MoreCompany", null, "MissingMethodException: Method not found: 'Void GameNetcodeStuff.PlayerControllerB.DamagePlayer(Int32)'"),
            new("broken", "ShipLoot", null, "NullReferenceException: Object reference not set to an instance of an object"),
        ], null);
        w.ShowWhy(lc, report, 14_000);
        s.Pump(300);
        s.Save("v23-1-crash");
        w.CloseDialog();

        s.Save("v23-2-updates");

        BuildShare.ShowCode(lc, "7K3Q9XZD", "Ночь с друзьями");
        s.Pump(300);
        s.Save("v23-3-code");
        w.CloseDialog();

        BuildShare.Preview(BuildCode.Demo(lc.Def, "Ночь с друзьями",
        [
            new("notnotnotswipez-MoreCompany", "MoreCompany", "1.11.0", "thunderstore", true), new("x753-More_Suits", "More Suits", "1.4.5", "thunderstore", true),
            new("Evaisa-LethalLib", "LethalLib", "0.16.2", "thunderstore", true), new("anormaltwig-LateCompany", "LateCompany", "1.0.18", "thunderstore", true),
            new("tinyhoot-ShipLoot", "ShipLoot", "1.0.0", "thunderstore", false),
        ]));
        s.Pump(300);
        s.Save("v23-4-code-preview");
        w.CloseDialog();

        w.Navigate(() => new HomePage());
        s.Pump(600);
        s.Save("v23-5-home");

        GamePatch.Patched.Clear();
        ModUpdates.Found.Clear();
        AppState.Notify();
    }
}
