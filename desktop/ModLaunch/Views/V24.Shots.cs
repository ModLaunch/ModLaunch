using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

/// <summary>
/// Снимки 24.0 (MODLAUNCH_SHOTS=V24): «Почему игра упала» (готовый пример и настоящий разбор лога
/// Lethal Company и отчёта Minecraft), «Исправили», код сборки, ввод кода, предпросмотр, вкладка «Профили»,
/// «Обновить всё» на странице игры и на главной.
/// </summary>
public static class V24Shots
{
    [DemoShots]
    static void V24Stage1(Shots s)
    {
        var w = s.Window;
        var lc = AppState.Game("lethal-company");
        var registry = lc.Registry!;
        // Сборка Lethal Company: More Suits без LethalLib, LethalThings устарел после патча игры.
        foreach (var (id, name, version) in new[] { ("x753-More_Suits", "More Suits", "1.4.3"), ("Evaisa-LethalThings", "LethalThings", "0.10.7"), ("notnotnotswipez-MoreCompany", "MoreCompany", "1.10.1") })
        {
            Directory.CreateDirectory(Path.Combine(registry.ModsDir, id));
            registry.Add(new JsonObject { ["id"] = id, ["name"] = name, ["folder"] = id, ["version"] = version, ["source"] = "thunderstore" });
        }
        if (registry.Get("Evaisa-LethalLib") is not null) registry.SetEnabled("Evaisa-LethalLib", false);
        ModUpdates.Found["lethal-company"] =
        [
            new("Evaisa-LethalThings", "Evaisa-LethalThings", "LethalThings", "0.10.7", "0.10.8", null, false, "thunderstore"),
            new("notnotnotswipez-MoreCompany", "notnotnotswipez-MoreCompany", "MoreCompany", "1.10.1", "1.11.0", null, false, "thunderstore"),
        ];
        var log = Path.Combine(lc.Path!, "BepInEx", "LogOutput.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllText(log, """
            [Info   :   BepInEx] BepInEx 5.4.21.0 - Lethal Company
            [Error  :   BepInEx] Could not load [More Suits 1.4.3] because it has missing dependencies: evaisa.lethallib (v0.10.1 or newer)
            [Error  :   BepInEx] Error loading [LethalThings 0.10.7] : Method not found: 'void GameNetcodeStuff.PlayerControllerB.DamagePlayer()' MissingMethodException
            """);
        AppState.Notify();

        w.ShowCrash(lc, MainWindow.DemoCrash(lc));
        s.Pump(400);
        s.Save("v24-1-crash-demo");
        w.CloseDialog();

        w.ShowCrash(lc, CrashDoctor.Analyze(lc, null));
        s.Pump(400);
        s.Save("v24-2-crash-real-lc");
        w.CloseDialog();

        Actions.ShowFixed(lc, [I18n.T("v24.fixed.installed", ("list", "LethalLib")), I18n.T("v24.fixed.updated", ("n", 1))], []);
        s.Pump(300);
        s.Save("v24-3-fixed");
        w.CloseDialog();

        if (Minecraft.Mc.State is { } mc)
        {
            // Отчёт о вылете с Fabric: нет Fabric API и Sodium, мало памяти.
            if (Minecraft.Mc.Active is { } a)
            {
                var reports = Directory.CreateDirectory(Path.Combine(a.Dir, "crash-reports")).FullName;
                File.WriteAllText(Path.Combine(reports, "crash-2026-11-05_20.10.00-client.txt"), """
                    ---- Minecraft Crash Report ----
                    Description: Mod loading
                    net.fabricmc.loader.impl.FormattedException: Mod resolution encountered an incompatible mod set!
                    A potential solution has been determined, this may resolve your problem:
                    	 - Install fabric-api, any version.
                    More details:
                    	 - Mod 'Sodium Extra' (sodium-extra) 0.5.4+mc1.21.1 requires any version of mod 'Indium' (indium), which is missing!
                    java.lang.OutOfMemoryError: Java heap space
                    """);
            }
            w.ShowCrash(mc, CrashDoctor.Analyze(mc, null));
            s.Pump(400);
            s.Save("v24-4-crash-real-mc");
            w.CloseDialog();
            w.Navigate(() => new MinecraftPage("log"));
            s.Pump(800);
            s.Save("v24-5-mc-log-why");
        }

        var pack = ShareUi.DemoPack();
        ShareUi.ShowCode(pack, "K7QM9XPA", offline: false);
        s.Pump(300);
        s.Save("v24-6-share-code");
        w.CloseDialog();
        ShareUi.ShowCode(pack, ShareCode.LongCode(pack), offline: true);
        s.Pump(300);
        s.Save("v24-7-share-long");
        w.CloseDialog();
        ShareUi.Enter();
        s.Pump(300);
        s.Save("v24-8-enter");
        w.CloseDialog();
        ShareUi.Preview(pack);
        s.Pump(300);
        s.Save("v24-9-preview");
        w.CloseDialog();
        ShareUi.Preview(pack with { Game = "valheim-missing", GameName = "Valheim" });
        s.Pump(300);
        s.Save("v24-10-no-game");
        w.CloseDialog();

        w.Navigate(() => new GamePage("lethal-company", "profiles"));
        s.Pump(800);
        s.Save("v24-11-profiles-share");
        w.Navigate(() => new GamePage("lethal-company", "installed"));
        s.Pump(800);
        s.Save("v24-12-update-all");
        w.Navigate(() => new HomePage());
        s.Pump(1200);
        s.Save("v24-13-home");
        ModUpdates.Found.Clear();
        AppState.Notify();
    }
}
