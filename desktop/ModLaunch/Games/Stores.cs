using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using ModLaunch.Core;

namespace ModLaunch.Games;

/// <summary>Запись «Программы и компоненты» (Uninstall в реестре). SizeKb — EstimatedSize, как его пишет установщик.</summary>
public sealed record UninstallEntry(string Key, string Name, string Publisher, string Location, string Uninstall, long SizeKb);

/// <summary>
/// Игра из лаунчера (кроме Steam — его читает SteamAccount). Id — номер в магазине; запуск — Uri (протокол лаунчера)
/// или Exe с Args. AltDir — настоящая папка, если Dir — ссылка (игры Xbox в WindowsApps ведут в XboxGames).
/// </summary>
public sealed record StoreGame(string Store, string Id, string Name, string Dir, string? Exe = null, string? Uri = null, string? Args = null,
    long Size = 0, string? Logo = null, string? AltDir = null);

/// <summary>
/// 27.0: игры всех лаунчеров на этом ПК — Epic, GOG, Xbox/Microsoft Store, EA app, Ubisoft Connect, Battle.net.
/// Всё читается с диска и из реестра, без входа в аккаунты и без сети. Каждый лаунчер — отдельно:
/// сбой одного (нет прав на ключ, битый манифест) не прячет игры остальных.
/// </summary>
public static partial class Stores
{
    /// <summary>Порядок магазинов в фильтрах. other — игры, добавленные в ModLaunch вручную (репаки, свои папки).</summary>
    public static readonly string[] Order = ["steam", "epic", "gog", "xbox", "ea", "ubisoft", "battlenet", "other"];

    public static string Title(string store) => store switch
    {
        "steam" => "Steam",
        "epic" => "Epic Games",
        "gog" => "GOG",
        "xbox" => "Xbox",
        "ea" => "EA app",
        "ubisoft" => "Ubisoft Connect",
        "battlenet" => "Battle.net",
        _ => I18n.T("v27.lib.store.other"),
    };

    public static List<StoreGame> ScanAll()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var uninstall = Safe<UninstallEntry>(UninstallEntries);
        var list = new List<StoreGame>();
        list.AddRange(Safe<StoreGame>(Epic));
        list.AddRange(Safe<StoreGame>(Gog));
        list.AddRange(Safe<StoreGame>(Xbox));
        list.AddRange(Safe(() => OperatingSystem.IsWindows() ? Ubisoft(uninstall) : []));
        list.AddRange(Safe(() => FromUninstall(uninstall, BattleNetExe(uninstall), Directory.Exists, EaOffer)));
        return list;
    }

    static List<T> Safe<T>(Func<IEnumerable<T>> scan)
    {
        var list = new List<T>();
        try { foreach (var item in scan()) list.Add(item); } catch { }
        return list;
    }

    /// <summary>Полный путь к существующей папке или null: в реестре бывают кавычки, прямые слэши и удалённые игры.</summary>
    static string? ExistingDir(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var dir = Path.GetFullPath(raw.Trim().Trim('"').Replace('/', '\\')).TrimEnd('\\');
            return Directory.Exists(dir) ? dir : null;
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- Epic

    static IEnumerable<StoreGame> Epic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            StoreGame? game = null;
            try { game = ParseEpic(File.ReadAllText(file)); } catch { }
            if (game is not null && ExistingDir(game.Dir) is { } real) yield return game with { Dir = real };
        }
    }

    /// <summary>Манифест Epic (*.item): только игры — без DLC, движков и недокачанных установок.</summary>
    internal static StoreGame? ParseEpic(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string? S(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var app = S("AppName");
        var name = S("DisplayName");
        var dir = S("InstallLocation");
        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir)) return null;
        if (S("MainGameAppName") is { Length: > 0 } main && main != app) return null;
        if (r.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;
        if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array
            && !cats.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "games")) return null;
        var ns = S("CatalogNamespace");
        var item = S("CatalogItemId");
        var id = ns is { Length: > 0 } && item is { Length: > 0 } ? $"{ns}%3A{item}%3A{app}" : app;
        var exe = S("LaunchExecutable") is { Length: > 0 } rel ? Path.Combine(dir, rel.Replace('/', '\\')) : null;
        var size = r.TryGetProperty("InstallSize", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var bytes) ? bytes : 0;
        return new StoreGame("epic", app, name.Trim(), dir, exe, $"com.epicgames.launcher://apps/{id}?action=launch&silent=true", null, size);
    }

    // ---------------------------------------------------------------- GOG

    [SupportedOSPlatform("windows")]
    static IEnumerable<StoreGame> Gog()
    {
        var seen = new HashSet<string>();
        foreach (var hive in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(hive);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var g = key.OpenSubKey(sub);
                if (g is null || !seen.Add(sub)) continue;
                // DLC GOG пишет отдельным ключом с dependsOn — номером основной игры.
                if (g.GetValue("dependsOn") is string dep && dep.Trim() != "") continue;
                if (g.GetValue("gameName") is not string name || name.Trim() == "" || ExistingDir(g.GetValue("path") as string) is not { } dir) continue;
                var exe = g.GetValue("exe") as string;
                yield return new StoreGame("gog", sub, name.Trim(), dir, exe is { Length: > 0 } && File.Exists(exe) ? exe : null, null, g.GetValue("launchParam") as string);
            }
        }
    }

    // ---------------------------------------------------------------- Xbox / Microsoft Store

    const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>Игры Xbox — пакеты, у которых в корне лежит MicrosoftGame.config (у обычных приложений Store его нет).</summary>
    [SupportedOSPlatform("windows")]
    static IEnumerable<StoreGame> Xbox()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PackagesKey);
        if (key is null) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var full in key.GetSubKeyNames())
        {
            string? root, display;
            using (var p = key.OpenSubKey(full))
            {
                root = p?.GetValue("PackageRootFolder") as string;
                display = p?.GetValue("DisplayName") as string;
            }
            if (string.IsNullOrWhiteSpace(root)) continue;
            string? xml = null, manifest = null;
            try
            {
                var config = Path.Combine(root, "MicrosoftGame.config");
                if (File.Exists(config))
                {
                    xml = File.ReadAllText(config);
                    var appx = Path.Combine(root, "AppxManifest.xml");
                    if (File.Exists(appx)) manifest = File.ReadAllText(appx);
                }
            }
            catch { }
            if (xml is null || ParseXbox(full, root, xml, display, manifest) is not { } game || !seen.Add(game.Id)) continue;
            string? real = null;
            try { real = Directory.ResolveLinkTarget(root, true)?.FullName; } catch { }
            yield return real is null ? game : game with { AltDir = real };
        }
    }

    /// <summary>
    /// MicrosoftGame.config → игра. Семейство пакета = Имя_ИздательId (из полного имени Имя_Версия_Арх_Ресурс_ИздательId),
    /// запуск — shell:AppsFolder\Семейство!IdПриложения, как это делает приложение Xbox. Id приложения берётся из
    /// AppxManifest.xml (Application Id): в MicrosoftGame.config он бывает другим (у Minecraft Launcher там «Game», а в Windows — «Minecraft»).
    /// </summary>
    internal static StoreGame? ParseXbox(string fullName, string root, string configXml, string? displayName, string? appxManifest = null)
    {
        var parts = fullName.Split('_');
        if (parts.Length < 5 || parts[0] == "" || parts[^1] == "") return null;
        var family = parts[0] + "_" + parts[^1];
        XElement doc;
        try { doc = XElement.Parse(configXml); } catch { return null; }
        static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
        var exe = Child(doc, "ExecutableList")?.Elements().FirstOrDefault(x => x.Name.LocalName == "Executable" && !string.IsNullOrWhiteSpace((string?)x.Attribute("Id")));
        string? fromManifest = null;
        if (appxManifest is not null)
            try { fromManifest = ((string?)XElement.Parse(appxManifest).Descendants().FirstOrDefault(x => x.Name.LocalName == "Application")?.Attribute("Id"))?.Trim(); } catch { }
        var appId = fromManifest is { Length: > 0 } ? fromManifest : ((string?)exe?.Attribute("Id"))?.Trim() ?? "Game";
        var visuals = Child(doc, "ShellVisuals");
        static bool Readable(string? s) => !string.IsNullOrWhiteSpace(s) && !s.StartsWith('@') && !s.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase);
        var name = (string?)visuals?.Attribute("DefaultDisplayName");
        if (!Readable(name)) name = Readable(displayName) ? displayName : parts[0];
        var logo = new[] { "Square480x480Logo", "Square150x150Logo", "StoreLogo" }.Select(a => (string?)visuals?.Attribute(a)).FirstOrDefault(Readable);
        var exeName = (string?)exe?.Attribute("Name");
        return new StoreGame("xbox", family, name!.Trim(), root,
            Readable(exeName) ? Path.Combine(root, exeName!.Replace('/', '\\')) : null,
            $"shell:AppsFolder\\{family}!{appId}", null, 0,
            logo is null ? null : Path.Combine(root, logo.Replace('/', '\\')));
    }

    // ---------------------------------------------------------------- Ubisoft Connect

    [SupportedOSPlatform("windows")]
    static IEnumerable<StoreGame> Ubisoft(List<UninstallEntry> uninstall)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (key is null) yield break;
        foreach (var id in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(id);
            if (!int.TryParse(id, out _) || ExistingDir(g?.GetValue("InstallDir") as string) is not { } dir) continue;
            yield return UbisoftGame(id, dir, uninstall);
        }
    }

    /// <summary>Имя и размер Ubisoft пишет в Uninstall под ключом «Uplay Install &lt;номер&gt;»; нет его — имя папки.</summary>
    internal static StoreGame UbisoftGame(string id, string dir, IEnumerable<UninstallEntry> uninstall)
    {
        var entry = uninstall.FirstOrDefault(e => e.Key.Equals("Uplay Install " + id, StringComparison.OrdinalIgnoreCase));
        var name = entry?.Name is { Length: > 0 } n ? n : Path.GetFileName(dir.TrimEnd('\\'));
        return new StoreGame("ubisoft", id, name, dir, null, $"uplay://launch/{id}/0", null, (entry?.SizeKb ?? 0) * 1024);
    }

    // ---------------------------------------------------------------- EA app и Battle.net (через Uninstall)

    [SupportedOSPlatform("windows")]
    static IEnumerable<UninstallEntry> UninstallEntries()
    {
        var views = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, path) in views)
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var e = key.OpenSubKey(sub);
                if (e?.GetValue("DisplayName") is not string name || name.Trim() == "") continue;
                var size = e.GetValue("EstimatedSize") is int kb && kb > 0 ? kb : 0;
                yield return new UninstallEntry(sub, name.Trim(), (e.GetValue("Publisher") as string ?? "").Trim(),
                    (e.GetValue("InstallLocation") as string ?? "").Trim().Trim('"'), e.GetValue("UninstallString") as string ?? "", size);
            }
        }
    }

    [GeneratedRegex(@"Electronic Arts|^EA( |$)|EA Games|EA Sports|Respawn", RegexOptions.IgnoreCase)] private static partial Regex EaPublisher();
    [GeneratedRegex(@"^(EA app|EA|Origin|EA Desktop)$", RegexOptions.IgnoreCase)] private static partial Regex EaLauncher();
    [GeneratedRegex(@"--uid=""?([A-Za-z0-9_]+)", RegexOptions.IgnoreCase)] private static partial Regex BlizzardUid();

    /// <summary>Игры EA app: издатель EA и деинсталлятор EA (не Steam — те уже в библиотеке Steam).</summary>
    internal static bool IsEa(UninstallEntry e) =>
        EaPublisher().IsMatch(e.Publisher) && !EaLauncher().IsMatch(e.Name)
        && !e.Uninstall.Contains("steam://", StringComparison.OrdinalIgnoreCase)
        && (e.Uninstall.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase) || e.Uninstall.Contains("EA Desktop", StringComparison.OrdinalIgnoreCase)
            || e.Uninstall.Contains("Origin", StringComparison.OrdinalIgnoreCase) || e.Uninstall.Contains("__Installer", StringComparison.OrdinalIgnoreCase));

    internal static bool IsBlizzard(UninstallEntry e) =>
        e.Publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) && !e.Name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase)
        && e.Uninstall.Contains("Battle.net", StringComparison.OrdinalIgnoreCase);

    /// <summary>uid деинсталлятора Blizzard → код игры для «Battle.net.exe --exec="launch КОД"». Незнакомые — запуск exe из папки.</summary>
    static readonly Dictionary<string, string> BlizzardCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wow"] = "WoW", ["prometheus"] = "Pro", ["fenris"] = "Fen", ["diablo3"] = "D3", ["d3"] = "D3", ["osi"] = "OSI",
        ["s2"] = "S2", ["s1"] = "S1", ["hero"] = "Hero", ["hs_beta"] = "WTCG", ["w3"] = "W3",
        ["viper"] = "VIPR", ["odin"] = "ODIN", ["lazarus"] = "LAZR", ["zeus"] = "ZEUS", ["fore"] = "FORE", ["auks"] = "AUKS",
    };

    internal static IEnumerable<StoreGame> FromUninstall(IEnumerable<UninstallEntry> entries, string? battleNet, Func<string, bool> exists, Func<string, string?> eaOffer)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (e.Location == "" || !exists(e.Location)) continue;
            var dir = e.Location.TrimEnd('\\', '/');
            if (!seen.Add(dir)) continue;
            if (IsEa(e))
            {
                var offer = eaOffer(dir);
                yield return new StoreGame("ea", offer ?? e.Key, e.Name, dir, null, offer is null ? null : $"origin2://game/launch?offerIds={offer}&autoDownload=1", null, e.SizeKb * 1024);
            }
            else if (IsBlizzard(e))
            {
                var uid = BlizzardUid().Match(e.Uninstall) is { Success: true } m ? m.Groups[1].Value : e.Key;
                var code = BlizzardCodes.GetValueOrDefault(uid);
                yield return code is not null && battleNet is not null
                    ? new StoreGame("battlenet", uid, e.Name, dir, battleNet, null, $"--exec=\"launch {code}\"", e.SizeKb * 1024)
                    : new StoreGame("battlenet", uid, e.Name, dir, null, null, null, e.SizeKb * 1024);
            }
        }
    }

    static string? BattleNetExe(List<UninstallEntry> uninstall)
    {
        var dirs = uninstall.Where(e => e.Name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase)).Select(e => e.Location)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net"));
        foreach (var d in dirs)
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            var exe = Path.Combine(d, "Battle.net.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    /// <summary>Номер предложения EA (offerId) из __Installer\installerdata.xml — по нему EA app запускает игру.</summary>
    static string? EaOffer(string dir)
    {
        try
        {
            var file = Path.Combine(dir, "__Installer", "installerdata.xml");
            return File.Exists(file) ? ParseEaOffer(File.ReadAllText(file)) : null;
        }
        catch { return null; }
    }

    internal static string? ParseEaOffer(string xml)
    {
        try
        {
            var id = XElement.Parse(xml).Descendants().FirstOrDefault(x => x.Name.LocalName == "contentID")?.Value.Trim();
            return id is { Length: > 0 } && id.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or ':') ? id : null;
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- самопроверки

    [SelfTest]
    static string StoreManifestsParse()
    {
        var epic = ParseEpic("""
            { "AppName": "Fortnite", "DisplayName": "Fortnite", "InstallLocation": "D:\\Epic\\Fortnite", "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteLauncher.exe",
              "CatalogNamespace": "fn", "CatalogItemId": "4fe75bbc", "AppCategories": ["public", "games", "applications"], "InstallSize": 30000000000 }
            """);
        if (epic is not { Store: "epic", Name: "Fortnite", Size: 30000000000 } || epic.Uri != "com.epicgames.launcher://apps/fn%3A4fe75bbc%3AFortnite?action=launch&silent=true"
            || epic.Exe != @"D:\Epic\Fortnite\FortniteGame\Binaries\Win64\FortniteLauncher.exe") throw new Exception("epic: " + epic);
        if (ParseEpic("""{ "AppName": "UE_5.4", "DisplayName": "Unreal Engine", "InstallLocation": "C:\\UE", "AppCategories": ["engines"] }""") is not null) throw new Exception("engine kept");
        if (ParseEpic("""{ "AppName": "dlc1", "DisplayName": "DLC", "InstallLocation": "C:\\G", "MainGameAppName": "Game" }""") is not null) throw new Exception("dlc kept");
        if (ParseEpic("""{ "AppName": "G", "DisplayName": "G", "InstallLocation": "C:\\G", "bIsIncompleteInstall": true }""") is not null) throw new Exception("incomplete kept");

        var xbox = ParseXbox("ROBLOXCorporation.RobloxGDK_2.747.1480.0_x64__55nm5eh3cm0pr", @"C:\Program Files\WindowsApps\Roblox", """
            <?xml version="1.0" encoding="utf-8"?>
            <Game configVersion="1">
              <Identity Name="ROBLOXCorporation.RobloxGDK" Publisher="CN=6FEF" Version="2.747.1480.0" />
              <ExecutableList><Executable Name="RobloxPlayerBeta.exe" Id="Game" TargetDeviceFamily="PC" /></ExecutableList>
              <ShellVisuals DefaultDisplayName="Roblox" Square480x480Logo="Assets/LargeLogo.png" />
            </Game>
            """, "ignored");
        if (xbox is not { Store: "xbox", Name: "Roblox", Id: "ROBLOXCorporation.RobloxGDK_55nm5eh3cm0pr" } || xbox.Uri != @"shell:AppsFolder\ROBLOXCorporation.RobloxGDK_55nm5eh3cm0pr!Game"
            || xbox.Logo != @"C:\Program Files\WindowsApps\Roblox\Assets\LargeLogo.png") throw new Exception("xbox: " + xbox);
        var named = ParseXbox("Pub.Game_1.0.0.0_x64__abc", @"C:\X", """<Game><ShellVisuals DefaultDisplayName="ms-resource:Title" /></Game>""", "Forza Horizon 5");
        if (named is not { Name: "Forza Horizon 5" } || named.Uri != @"shell:AppsFolder\Pub.Game_abc!Game") throw new Exception("xbox name: " + named);
        var launcher = ParseXbox("Microsoft.4297127D64EC6_2.6.2.0_x64__8wekyb3d8bbwe", @"C:\M",
            """<Game><ExecutableList><Executable Name="Minecraft.exe" Id="Game" /></ExecutableList><ShellVisuals DefaultDisplayName="Minecraft Launcher" /></Game>""", null,
            """<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Applications><Application Id="Minecraft" Executable="GameLaunchHelper.exe" /></Applications></Package>""");
        if (launcher?.Uri != @"shell:AppsFolder\Microsoft.4297127D64EC6_8wekyb3d8bbwe!Minecraft") throw new Exception("xbox app id from manifest: " + launcher);
        if (ParseXbox("bad", @"C:\X", "<Game/>", null) is not null || ParseXbox("A_1_x64__p", @"C:\X", "not xml", null) is not null) throw new Exception("xbox junk kept");

        var ubi = UbisoftGame("4923", @"C:\Ubi\Anno 1800", [new("Uplay Install 4923", "Anno 1800", "Ubisoft", @"C:\Ubi\Anno 1800", "", 2048)]);
        if (ubi is not { Name: "Anno 1800", Uri: "uplay://launch/4923/0", Size: 2097152 } || UbisoftGame("1", @"C:\Ubi\Far Cry 6", []).Name != "Far Cry 6") throw new Exception("ubisoft: " + ubi);

        UninstallEntry[] entries =
        [
            new("{EA1}", "Battlefield 2042", "Electronic Arts", @"C:\EA\BF2042", @"C:\Program Files\Common Files\EAInstaller\Battlefield 2042\Cleanup.exe uninstall_game", 100),
            new("EA app", "EA app", "Electronic Arts", @"C:\Program Files\Electronic Arts\EA Desktop", @"C:\Program Files\Electronic Arts\EA Desktop\EAUninstall.exe", 1),
            new("Steam App 1238810", "Battlefield V", "Electronic Arts", @"C:\Steam\steamapps\common\Battlefield V", "\"C:\\Steam\\steam.exe\" steam://uninstall/1238810", 1),
            new("Overwatch", "Overwatch", "Blizzard Entertainment", @"C:\Games\Overwatch", "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid=prometheus --displayname=\"Overwatch\"", 50),
            new("Mystery", "New Blizzard Game", "Blizzard Entertainment", @"C:\Games\New", "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --uid=newgame", 0),
            new("Battle.net", "Battle.net", "Blizzard Entertainment", @"C:\Program Files (x86)\Battle.net", @"C:\Program Files (x86)\Battle.net\Battle.net Uninstall.exe", 1),
            new("Gone", "Removed EA Game", "Electronic Arts", @"C:\EA\Gone", "EAInstaller\\Cleanup.exe", 1),
        ];
        var found = FromUninstall(entries, @"C:\BNet\Battle.net.exe", d => !d.EndsWith("Gone"), d => d.EndsWith("BF2042") ? "Origin.OFR.50.0004657" : null).ToList();
        if (found is not [{ Store: "ea" } bf, { Store: "battlenet" } ow, { Store: "battlenet" } other]) throw new Exception("uninstall: " + string.Join("; ", found));
        if (bf.Uri != "origin2://game/launch?offerIds=Origin.OFR.50.0004657&autoDownload=1" || bf.Size != 102400) throw new Exception("ea: " + bf);
        if (ow.Exe != @"C:\BNet\Battle.net.exe" || ow.Args != "--exec=\"launch Pro\"" || other.Exe is not null || other.Id != "newgame") throw new Exception("bnet: " + ow + " / " + other);
        if (ParseEaOffer("<DiPManifest><contentIDs><contentID>Origin.OFR.50.0004657</contentID></contentIDs></DiPManifest>") != "Origin.OFR.50.0004657"
            || ParseEaOffer("<x><contentID>a&amp;b</contentID></x>") is not null || ParseEaOffer("junk") is not null) throw new Exception("ea offer");
        return "Epic (no engines/DLC), Xbox config, Ubisoft names, EA app and Battle.net from Uninstall (Steam copies and launchers skipped)";
    }
}
