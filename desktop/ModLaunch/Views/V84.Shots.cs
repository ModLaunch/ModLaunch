using ModLaunch.Core;
using ModLaunch.Features;

namespace ModLaunch.Views;

public sealed partial class GamePage
{
    /// <summary>Снимки 8.4: шапка, вкладки, «Настройки» с наборами, состояние сервисов, «Что нового».</summary>
    [DemoShots]
    static void V84Shots(Shots s)
    {
        var sub = AppState.Game("subnautica");
        var cfg = Path.Combine(sub.Path!, "BepInEx", "config");
        Directory.CreateDirectory(cfg);
        File.WriteAllLines(Path.Combine(cfg, "BepInEx.cfg"),
        [
            "[Caching]", "", "## Enable/disable assembly metadata cache", "# Setting type: Boolean", "# Default value: true", "EnableAssemblyCache = true", "",
            "[Logging]", "", "## Enables showing unity log messages in the BepInEx logging system.", "# Setting type: Boolean", "# Default value: true", "UnityLogListening = true", "",
            "[Logging.Console]", "", "## Enables showing a console for log output.", "# Setting type: Boolean", "# Default value: false", "Enabled = true", "",
            "## Which log levels to show in the console output.", "# Setting type: LogLevel", "# Default value: Fatal, Error, Warning, Message, Info", "# Acceptable values: None, Fatal, Error, Warning, Message, Info, Debug, All", "LogLevels = Fatal, Error, Warning, Message, Info", "",
            "[Logging.Disk]", "", "## Include unity log messages in log file output.", "# Setting type: Boolean", "# Default value: false", "WriteUnityLog = false", "",
            "## Enables writing log messages to disk.", "# Setting type: Boolean", "# Default value: true", "Enabled = true", "",
            "## Which log leves are saved to the disk log output.", "# Setting type: LogLevel", "# Default value: Fatal, Error, Warning, Message, Info", "LogLevels = All",
        ]);
        File.WriteAllLines(Path.Combine(cfg, "com.snmodding.nautilus.cfg"),
        [
            "[General]", "", "## Show debug messages in the log", "# Setting type: Boolean", "# Default value: false", "DebugLogs = true", "",
            "## How often the mod checks for new recipes, seconds", "# Setting type: Int32", "# Default value: 30", "# Acceptable value range: From 5 to 120", "RecipeScanInterval = 45", "",
            "## Language for mod texts", "# Setting type: String", "# Default value: Auto", "# Acceptable values: Auto, English, Russian, German", "Language = Auto",
        ]);
        File.WriteAllLines(Path.Combine(cfg, "MapMod.cfg"),
        [
            "[Map]", "", "## Map opacity", "# Setting type: Single", "# Default value: 0.8", "# Acceptable value range: From 0 to 1", "Opacity = 0.65", "",
            "## Show base markers", "# Setting type: Boolean", "# Default value: true", "ShowBases = true", "",
            "## Key to open the map", "# Setting type: KeyCode", "# Default value: M", "OpenKey = M",
        ]);
        AppState.Notify();

        s.Window.Navigate(() => new HomePage());
        s.Pump(900);
        s.Save("v84-1-home");

        s.Window.Navigate(() => new GamePage("subnautica", "installed"));
        s.Pump(900);
        s.Save("v84-2-tabs");

        s.Window.Navigate(() => new GamePage("subnautica", "config"));
        s.Pump(900);
        s.Save("v84-3-config");
        s.Window.Height = 1750;
        s.Pump(600);
        s.Window.Navigate(() => new GamePage("subnautica", "config"));
        s.Pump(900);
        s.Save("v84-3b-config-tall");
        s.Window.Width = 1700;
        s.Window.Height = 800;
        s.Pump(600);
        s.Window.Navigate(() => new HomePage());
        s.Pump(900);
        s.Save("v84-0-wide-header");
        s.Window.Width = 1366;
        s.Pump(600);

        var page = new GamePage("subnautica", "config");
        s.Window.Navigate(() => page);
        s.Pump(500);
        page.DemoPreset(CfgPresets.Performance);
        s.Pump(700);
        s.Save("v84-4-preset-confirm");
        s.Window.CloseDialog();
        CfgPresets.Apply(sub, CfgPresets.Performance);
        CfgPresets.Save(sub, "Для стрима");
        s.Window.Navigate(() => new GamePage("subnautica", "config"));
        s.Pump(900);
        s.Save("v84-5-preset-applied");

        // Пакет Content Patcher: настройки строками + схема в content.json + русские названия из i18n.
        var sv = AppState.Games.FirstOrDefault(g => g.Def.Id == "stardew-valley");
        if (sv is not null)
        {
            var (oldPath, oldStatus) = (sv.Path, sv.Status);
            sv.Path = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(sub.Path!)!, "Stardew Valley")).FullName;
            sv.Status = Detect.Found;
            var pack = Path.Combine(sv.Def.ModsDir(sv.Path), "[CP] Big Farm Pack");
            Directory.CreateDirectory(Path.Combine(pack, "i18n"));
            File.WriteAllText(Path.Combine(pack, "config.json"), "{ \"SeasonalEdits\": \"true\", \"WarpLocation\": \"default\", \"FarmSize\": \"48\", \"Animals\": \"cow, chicken\", \"ReplaceDefaultFences\": \"false\" }");
            File.WriteAllText(Path.Combine(pack, "content.json"), """
                { "Format": "2.0.0", "ConfigSchema": {
                  "SeasonalEdits": { "AllowValues": "true, false", "Default": "true" },
                  "WarpLocation": { "AllowValues": "default, farm, town, beach", "Default": "default", "Section": "Travel" },
                  "FarmSize": { "Default": "32", "Description": "Farm width in tiles" },
                  "Animals": { "AllowValues": "cow, chicken, goat, pig", "AllowMultiple": true, "Default": "cow" },
                  "ReplaceDefaultFences": { "AllowValues": "true, false", "Default": "true" } } }
                """);
            File.WriteAllText(Path.Combine(pack, "i18n", "ru.json"), """
                { "config.SeasonalEdits.name": "Сезонные изменения", "config.SeasonalEdits.description": "Карты и здания меняются по сезонам.",
                  "config.WarpLocation.name": "Точка входа", "config.section.Travel.name": "Путешествия",
                  "config.FarmSize.name": "Размер фермы", "config.FarmSize.description": "Ширина фермы в клетках.",
                  "config.Animals.name": "Животные на старте", "config.ReplaceDefaultFences.name": "Новые заборы" }
                """);
            s.Window.Height = 1450;
            s.Pump(500);
            s.Window.Navigate(() => new GamePage("stardew-valley", "config"));
            s.Pump(900);
            s.Save("v84-9-config-cp");
            s.Window.Height = 800;
            s.Pump(500);
            (sv.Path, sv.Status) = (oldPath, oldStatus);
        }

        s.Window.Navigate(() => new GamePage("subnautica", "log"));
        s.Pump(800);
        s.Save("v84-6-more-tab");

        Services.Last["reviews"] = new ServiceStatus("reviews", ServiceState.Ok, I18n.T("svc.ok.reviews"));
        Services.Last["accounts"] = new ServiceStatus("accounts", ServiceState.Ok, I18n.T("svc.ok.accounts.in"));
        Services.Last["friends"] = Services.FromError("friends", new Social.ServiceError("DENIED", "PERMISSION_DENIED: Missing or insufficient permissions."));
        Services.Last["hub"] = new ServiceStatus("hub", ServiceState.Ok, I18n.T("svc.ok.hub", ("n", 12)));
        Services.Last["market"] = Services.FromError("market", new Social.ServiceError("DENIED", "PERMISSION_DENIED: Missing or insufficient permissions."));
        Services.Last["ads"] = new ServiceStatus("ads", ServiceState.Warn, I18n.T("svc.ads.unreachable"), I18n.T("svc.fix.ads.upload", ("url", Ads.FeedUrl ?? "")));
        Services.Last["updates"] = new ServiceStatus("updates", ServiceState.Ok, I18n.T("svc.ok.updates", ("version", Http.Version), ("source", "modlaunchapp.com")));
        s.Window.Navigate(() => new SettingsPage("services"));
        s.Pump(900);
        s.Save("v84-7-services");

        WhatsNew.Show();
        s.Pump(900);
        s.Save("v84-8-whatsnew");
        s.Window.CloseDialog();
    }
}
