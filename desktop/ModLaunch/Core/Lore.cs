using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Core;

/// <summary>
/// 12.0: у каждой игры свой «лор» запуска — слои, которые рисует LoreVeil (щупальца, частицы,
/// лучи, помехи…), в четырёх стадиях. Стадия растёт вместе с прохождением: сначала по сохранению
/// игры (Hollow Knight, Silksong, Minecraft, Stardew Valley), иначе по достижениям и часам в ModLaunch.
/// Kind: motes — частицы вверх, pixels — квадратные частицы, snow/leaves/rain — сверху вниз,
/// bubbles — пузыри, tendrils — щупальца от краёв (Color2 — светлая кромка), threads — нити шёлка,
/// rays — лучи сверху, aurora — сияние, scan — строки помех, ring — кольцо, stars — мерцание,
/// vignette — тёмные края.
/// </summary>
public static class Lore
{
    public sealed record Layer(string Kind, string Color, double Amount, string? Color2 = null);
    public sealed record Theme(string Key, Layer[][] Stages, int[]? Hours = null);
    /// <summary>Стадия 0…3 и откуда она известна: save — сохранение, ach — достижения, hours — часы, none — ещё не играли.</summary>
    public sealed record Depth(int Stage, string Source);

    public static bool Enabled => Settings.Data.Bool("loreFx", true);

    static Layer L(string kind, string color, double amount, string? color2 = null) => new(kind, color, amount, color2);

    static readonly Dictionary<string, Theme> Themes = new()
    {
        // Хорошо известный пример: сначала как обычно, потом душа, потом Бездна — чёрные щупальца, в конце — с белой кромкой Сияния.
        ["hollow-knight"] = new("hollow-knight",
        [
            [L("motes", "#DCE6F5", 0.25), L("vignette", "#05070C", 0.35)],
            [L("motes", "#EEF4FF", 0.65), L("vignette", "#060A16", 0.5)],
            [L("tendrils", "#000000", 0.55), L("motes", "#EEF4FF", 0.4), L("vignette", "#000000", 0.65)],
            [L("tendrils", "#000000", 1.0, "#FFFFFF"), L("rays", "#FFF4CF", 0.45), L("motes", "#FFF6DA", 0.7), L("vignette", "#000000", 0.75)],
        ]),
        ["hollow-knight-silksong"] = new("silksong",
        [
            [L("threads", "#B8322A", 0.25), L("vignette", "#0A0405", 0.35)],
            [L("threads", "#C93A2E", 0.55), L("motes", "#F3D7A6", 0.35), L("vignette", "#120607", 0.45)],
            [L("threads", "#D8432F", 0.85), L("motes", "#F3D7A6", 0.5), L("vignette", "#1C0505", 0.65)],
            [L("threads", "#E04A32", 1.0, "#FFF1D6"), L("rays", "#FFE2A8", 0.45), L("motes", "#FFE9C2", 0.6), L("vignette", "#140303", 0.7)],
        ]),
        // Minecraft — та же система: обычный мир, свой дом, Незер, Край (по достижениям мира).
        ["minecraft"] = new("minecraft",
        [
            [L("pixels", "#FFFFFF", 0.25)],
            [L("pixels", "#8FD14F", 0.5), L("rays", "#FFF6C8", 0.25)],
            [L("pixels", "#FF7A1F", 0.75), L("vignette", "#3A0A00", 0.6)],
            [L("pixels", "#C58CFF", 0.8), L("stars", "#EAD9FF", 0.7), L("vignette", "#0B0016", 0.75)],
        ]),
        ["stardew-valley"] = new("stardew",
        [
            [L("leaves", "#F6A9C9", 0.3)],
            [L("leaves", "#E99B3A", 0.55), L("rays", "#FFE8B0", 0.25)],
            [L("motes", "#8EE07B", 0.65), L("leaves", "#E99B3A", 0.3)],
            [L("motes", "#FFE27A", 0.85), L("rays", "#FFE9A8", 0.45), L("stars", "#FFF1B8", 0.4)],
        ]),
        ["subnautica"] = new("subnautica",
        [
            [L("rays", "#A8ECFF", 0.35), L("bubbles", "#CFF3FF", 0.3)],
            [L("rays", "#7FD8F5", 0.3), L("bubbles", "#CFF3FF", 0.55), L("vignette", "#00283A", 0.45)],
            [L("motes", "#4FE3C1", 0.65), L("bubbles", "#9FE7FF", 0.3), L("vignette", "#00121F", 0.7)],
            [L("motes", "#7CFF6B", 0.8), L("tendrils", "#03140F", 0.35, "#4FE3C1"), L("vignette", "#00090F", 0.8)],
        ], [1, 10, 30]),
        ["subnautica-below-zero"] = new("belowzero",
        [
            [L("snow", "#F2FBFF", 0.3)],
            [L("snow", "#F2FBFF", 0.5), L("aurora", "#6CF2C8", 0.35, "#7A7CFF")],
            [L("bubbles", "#CFF3FF", 0.4), L("motes", "#5FE8FF", 0.5), L("vignette", "#001428", 0.6)],
            [L("aurora", "#6CF2C8", 0.6, "#B07CFF"), L("motes", "#7CFFB8", 0.7), L("vignette", "#000A18", 0.7)],
        ], [1, 10, 30]),
        ["valheim"] = new("valheim",
        [
            [L("motes", "#FFB45C", 0.3)],
            [L("motes", "#FFB45C", 0.4), L("vignette", "#0A0F08", 0.5)],
            [L("snow", "#EEF6FF", 0.65), L("aurora", "#5CF2B0", 0.4, "#6A8CFF")],
            [L("aurora", "#5CF2B0", 0.7, "#B07CFF"), L("motes", "#9FDFFF", 0.6), L("vignette", "#02060C", 0.7)],
        ], [2, 15, 50]),
        ["lethal-company"] = new("lethal",
        [
            [L("scan", "#B7FFB0", 0.3)],
            [L("scan", "#B7FFB0", 0.5), L("vignette", "#000000", 0.45)],
            [L("scan", "#FFB65C", 0.7), L("motes", "#FF9C2A", 0.3), L("vignette", "#000000", 0.6)],
            [L("scan", "#FF6B4A", 0.9), L("vignette", "#1A0000", 0.8)],
        ]),
        ["risk-of-rain-2"] = new("ror2",
        [
            [L("rain", "#BFD3E6", 0.3)],
            [L("rain", "#BFD3E6", 0.5), L("ring", "#FF4A4A", 0.3)],
            [L("rain", "#9FB6CC", 0.6), L("ring", "#FF4A4A", 0.6), L("vignette", "#0C0510", 0.5)],
            [L("ring", "#6FB7FF", 0.8, "#FFFFFF"), L("stars", "#BFE0FF", 0.6), L("vignette", "#000814", 0.7)],
        ]),
        ["repo"] = new("repo",
        [
            [L("scan", "#FFD27A", 0.25)],
            [L("scan", "#FFD27A", 0.45), L("motes", "#FFB13D", 0.3)],
            [L("scan", "#FF9A3D", 0.65), L("vignette", "#0C0600", 0.6)],
            [L("scan", "#FF5A3D", 0.85), L("motes", "#FF9A3D", 0.45), L("vignette", "#140200", 0.75)],
        ]),
        ["peak"] = new("peak",
        [
            [L("rays", "#FFF0C2", 0.3)],
            [L("leaves", "#7FCF6A", 0.45), L("rays", "#FFF0C2", 0.3)],
            [L("snow", "#F4FAFF", 0.7), L("vignette", "#0A1420", 0.4)],
            [L("snow", "#FFFFFF", 0.5), L("rays", "#FFE7B8", 0.6), L("stars", "#FFFFFF", 0.3)],
        ]),
        ["h3vr"] = new("h3vr",
        [
            [L("motes", "#FFD45C", 0.25)],
            [L("motes", "#FFD45C", 0.45), L("scan", "#FFE7A8", 0.2)],
            [L("motes", "#FF9F3D", 0.65), L("vignette", "#0A0600", 0.5)],
            [L("motes", "#FF6A3D", 0.85), L("ring", "#FFD45C", 0.4), L("vignette", "#120400", 0.6)],
        ]),
        ["content-warning"] = new("cw",
        [
            [L("scan", "#E6F0FF", 0.3)],
            [L("scan", "#E6F0FF", 0.5), L("vignette", "#000000", 0.5)],
            [L("scan", "#C7FFE0", 0.7), L("motes", "#DFFFEF", 0.3), L("vignette", "#000000", 0.65)],
            [L("scan", "#FFFFFF", 0.9), L("tendrils", "#020403", 0.35), L("vignette", "#000000", 0.8)],
        ]),
        ["ultrakill"] = new("ultrakill",
        [
            [L("motes", "#FF5A3D", 0.3)],
            [L("motes", "#FF3D2E", 0.5), L("vignette", "#160000", 0.5)],
            [L("rain", "#FF4433", 0.5), L("motes", "#FF3D2E", 0.6), L("vignette", "#200000", 0.65)],
            [L("rays", "#FFD9A8", 0.5), L("motes", "#FFF0C8", 0.6), L("tendrils", "#140000", 0.4, "#FF3D2E"), L("vignette", "#120000", 0.75)],
        ]),
        ["rounds"] = new("rounds",
        [
            [L("ring", "#FFB13D", 0.25)],
            [L("ring", "#FFB13D", 0.45), L("motes", "#4FB7FF", 0.3)],
            [L("ring", "#FF5A5A", 0.65), L("motes", "#4FB7FF", 0.5)],
            [L("ring", "#FFE07A", 0.85, "#FFFFFF"), L("motes", "#FF7A5A", 0.6), L("vignette", "#06040A", 0.5)],
        ]),
        ["dyson-sphere-program"] = new("dsp",
        [
            [L("stars", "#E6F0FF", 0.35)],
            [L("stars", "#E6F0FF", 0.5), L("motes", "#5CC8FF", 0.3)],
            [L("ring", "#5CC8FF", 0.55), L("stars", "#E6F0FF", 0.5)],
            [L("ring", "#FFD45C", 0.85, "#5CC8FF"), L("rays", "#FFE7A8", 0.4), L("stars", "#FFFFFF", 0.6)],
        ], [2, 20, 60]),
        ["gtfo"] = new("gtfo",
        [
            [L("scan", "#D7E3FF", 0.25), L("vignette", "#000000", 0.5)],
            [L("motes", "#E8EEFF", 0.35), L("vignette", "#000000", 0.65)],
            [L("scan", "#D7E3FF", 0.5), L("tendrils", "#030303", 0.35), L("vignette", "#000000", 0.75)],
            [L("tendrils", "#000000", 0.7, "#8A1C1C"), L("motes", "#E8EEFF", 0.4), L("vignette", "#000000", 0.85)],
        ]),
        ["outward"] = new("outward",
        [
            [L("motes", "#E8D7B0", 0.3)],
            [L("leaves", "#C9A066", 0.45), L("rays", "#FFE8B8", 0.25)],
            [L("snow", "#EEF3FF", 0.45), L("motes", "#9FD0FF", 0.4)],
            [L("rays", "#FFD9A0", 0.5), L("motes", "#FFC870", 0.6), L("vignette", "#0C0703", 0.6)],
        ]),
    };

    /// <summary>Тема игры; у своих и новых игр — общая, частицы цвета акцента игры.</summary>
    public static Theme For(GameDef def) => Themes.TryGetValue(def.Id, out var t) ? t : new("any",
    [
        [L("motes", def.Accent, 0.25)],
        [L("motes", def.Accent, 0.5), L("vignette", "#000000", 0.35)],
        [L("motes", def.Accent, 0.65), L("rays", def.Accent, 0.3), L("vignette", "#000000", 0.5)],
        [L("motes", def.Accent, 0.85), L("rays", def.Accent, 0.45), L("stars", "#FFFFFF", 0.4), L("vignette", "#000000", 0.6)],
    ]);

    public static bool HasOwnTheme(string id) => Themes.ContainsKey(id);

    /// <summary>Название стадии: lore.&lt;тема&gt;.&lt;0…3&gt;.</summary>
    public static string StageName(GameDef def, int stage) => I18n.T($"lore.{For(def).Key}.{Math.Clamp(stage, 0, 3)}");

    // ---------------------------------------------------------------- глубина

    static readonly Dictionary<string, (DateTime At, Depth D)> Cache = [];
    /// <summary>Игры, чьи сохранения читаются прямо сейчас (под lock (Cache)): одно чтение на игру за раз.</summary>
    static readonly HashSet<string> Reading = [];
    /// <summary>Сколько стадия из сохранений считается свежей.</summary>
    static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);
    /// <summary>Для снимков экрана: стадия игры задана вручную.</summary>
    public static readonly Dictionary<string, int> Forced = [];

    /// <summary>
    /// Насколько далеко игрок зашёл в игру. Не ждёт диск: в первый раз — прикидка по часам и достижениям,
    /// сохранения читаются в фоне (не чаще раза в минуту); сменилась стадия — окно перерисуется.
    /// </summary>
    public static Depth Of(GameDef def)
    {
        if (Forced.TryGetValue(def.Id, out var shot)) return new(shot, "save");
        if (Environment.GetEnvironmentVariable("MODLAUNCH_LORE") is { } forced && int.TryParse(forced, out var f)) return new(Math.Clamp(f, 0, 3), "save");
        lock (Cache)
            if (Cache.TryGetValue(def.Id, out var old))
            {
                // Устарело — отдаём прежнее сразу, а сохранения перечитываем в фоне: интерфейс не ждёт диск.
                if (DateTime.UtcNow - old.At >= Fresh) Background(def);
                return old.D;
            }
        // Снимки экрана — как раньше, сразу с диска: кадр не меняется под камерой.
        if (Program.Screenshot) return Refresh(def, out _);
        var rough = Rough(def);
        lock (Cache)
        {
            if (Cache.TryGetValue(def.Id, out var got)) return got.D;
            // Прикидка сразу устаревшая (MinValue): следующий Of не примет её за прочитанную с диска.
            Cache[def.Id] = (DateTime.MinValue, rough);
            Background(def);
        }
        return rough;
    }

    /// <summary>Прочитать сохранения и запомнить стадию; changed — стадия не та, что была запомнена.</summary>
    static Depth Refresh(GameDef def, out bool changed)
    {
        var d = Compute(def);
        lock (Cache)
        {
            changed = Cache.TryGetValue(def.Id, out var was) && was.D.Stage != d.Stage;
            Cache[def.Id] = (DateTime.UtcNow, d);
        }
        return d;
    }

    /// <summary>Перечитать сохранения в фоне, если их уже не читают. Вызывать под lock (Cache).</summary>
    static void Background(GameDef def)
    {
        if (Reading.Add(def.Id)) Task.Run(() => Reread(def));
    }

    /// <summary>Перечитать сохранения (игра уже занята в Reading) и освободить её; сменилась стадия — перерисовать окно.</summary>
    static void Reread(GameDef def)
    {
        try
        {
            Refresh(def, out var changed);
            if (changed) Redraw();
        }
        catch { }
        finally { lock (Cache) Reading.Remove(def.Id); }
    }

    /// <summary>Перерисовать окно из фона. В --selfcheck окна нет, в снимках кадр не трогаем.</summary>
    static void Redraw()
    {
        if (Program.Screenshot) return;
        try { Avalonia.Threading.Dispatcher.UIThread.Post(() => AppState.Notify()); } catch { }
    }

    /// <summary>Прочитать сохранения всех найденных игр заранее (в фоне при запуске программы); свежие и уже читаемые — пропустить.</summary>
    public static void Warm()
    {
        if (!Enabled) return;
        foreach (var g in AppState.Games.Where(g => g.Status == Detect.Found).ToList())
        {
            lock (Cache)
                if ((Cache.TryGetValue(g.Def.Id, out var had) && DateTime.UtcNow - had.At < Fresh) || !Reading.Add(g.Def.Id)) continue;
            Reread(g.Def);
        }
    }

    static Depth Compute(GameDef def)
    {
        int? save = null;
        try
        {
            save = def.Id switch
            {
                "hollow-knight" => HollowKnight(Path.Combine(LocalLow, "Team Cherry", "Hollow Knight")),
                "hollow-knight-silksong" => HollowKnight(Path.Combine(LocalLow, "Team Cherry", "Hollow Knight Silksong")),
                "minecraft" => MinecraftWorlds(),
                "stardew-valley" => Stardew(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves")),
                _ => null,
            };
        }
        catch { }
        if (save is int s) return new(s, "save");
        return Rough(def);
    }

    /// <summary>Прикидка без сохранений игры: по часам в ModLaunch и достижениям.</summary>
    static Depth Rough(GameDef def)
    {
        var played = PlayTime.Get(def.Id);
        var h = For(def).Hours ?? [1, 8, 25];
        var hours = played.TotalMs / 3_600_000.0;
        var byHours = hours >= h[2] ? 3 : hours >= h[1] ? 2 : hours >= h[0] ? 1 : 0;
        var byAch = 0;
        try
        {
            if (AppState.Games.FirstOrDefault(g => g.Def.Id == def.Id) is { } g)
            {
                var done = Achievements.ForGame(g).Count(a => a.Done);
                byAch = done >= 6 ? 3 : done >= 4 ? 2 : done >= 2 ? 1 : 0;
            }
        }
        catch { }
        if (byAch > byHours) return new(byAch, "ach");
        return new(byHours, played.TotalMs > 0 ? "hours" : "none");
    }

    static string LocalLow => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");

    // ---------------------------------------------------------------- Hollow Knight и Silksong

    // Сохранения Team Cherry: строка .NET BinaryFormatter, внутри — Base64 от AES-ECB с открытым ключом игры.
    static readonly byte[] HkKey = Encoding.UTF8.GetBytes("UKu52ePUBwetZ9wNX88o54dnfKRu0T1l");

    /// <summary>Расшифровать userN.dat в JSON (null — не похоже на сохранение Team Cherry).</summary>
    public static JsonNode? DecodeTeamCherry(byte[] file)
    {
        const int header = 22;
        if (file.Length < header + 2) return null;
        var at = header;
        int len = 0, shift = 0;
        while (at < file.Length && shift < 35)
        {
            var b = file[at++];
            len |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        if (len <= 0 || at + len > file.Length) return null;
        var cipher = Convert.FromBase64String(Encoding.UTF8.GetString(file, at, len));
        using var aes = Aes.Create();
        aes.Key = HkKey;
        var plain = aes.DecryptEcb(cipher, PaddingMode.PKCS7);
        return JsonNode.Parse(plain);
    }

    /// <summary>Самая глубокая стадия по всем ячейкам: процент прохождения, Бездна, Полый Рыцарь, Сияние.</summary>
    static int? HollowKnight(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        int? best = null;
        foreach (var file in Directory.EnumerateFiles(dir, "user*.dat", SearchOption.AllDirectories).Where(f => Regex.IsMatch(Path.GetFileName(f), @"^user\d+\.dat$")).Take(16))
        {
            try
            {
                if (DecodeTeamCherry(File.ReadAllBytes(file))?["playerData"] is not JsonObject p) continue;
                best = Math.Max(best ?? 0, HollowKnightStage(p));
            }
            catch { }
        }
        return best;
    }

    public static int HollowKnightStage(JsonObject p)
    {
        double pct = 0;
        try { pct = p["completionPercentage"]?.GetValue<double>() ?? 0; } catch { }
        bool Flag(string key) { try { return p[key]?.GetValue<bool>() == true; } catch { return false; } }
        if (Flag("killedHollowKnight") || Flag("killedFinalBoss") || pct >= 85) return 3;
        if (Flag("visitedAbyss") || Flag("abyssGateOpened") || Flag("visitedDeepnest") || pct >= 40) return 2;
        if (pct >= 8) return 1;
        return 0;
    }

    // ---------------------------------------------------------------- Minecraft

    /// <summary>По достижениям миров: Край — 3, Незер — 2, добыт камень или есть мир — 1.</summary>
    static int? MinecraftWorlds()
    {
        var roots = new List<string>();
        try { roots.Add(Path.Combine(Minecraft.Mc.Root, "saves")); } catch { }
        try { roots.AddRange(Directory.EnumerateDirectories(Minecraft.Mc.InstancesDir).Select(d => Path.Combine(d, "saves"))); } catch { }
        int? best = null;
        foreach (var root in roots.Where(Directory.Exists))
            foreach (var world in Directory.EnumerateDirectories(root).Take(64))
            {
                var adv = Path.Combine(world, "advancements");
                var stage = 0;
                if (Directory.Exists(adv))
                    foreach (var f in Directory.EnumerateFiles(adv, "*.json").Take(8))
                    {
                        try { stage = Math.Max(stage, MinecraftStage(JsonNode.Parse(File.ReadAllText(f)) as JsonObject)); } catch { }
                    }
                best = Math.Max(best ?? 0, stage);
                if (best == 3) return 3;
            }
        return best;
    }

    public static int MinecraftStage(JsonObject? a)
    {
        bool Done(string key) { try { return a?[key]?["done"]?.GetValue<bool>() == true; } catch { return false; } }
        if (Done("minecraft:end/root") || Done("minecraft:story/enter_the_end")) return 3;
        if (Done("minecraft:nether/root") || Done("minecraft:story/enter_the_nether")) return 2;
        if (Done("minecraft:story/mine_stone") || Done("minecraft:story/root")) return 1;
        return 0;
    }

    // ---------------------------------------------------------------- Stardew Valley

    /// <summary>По SaveGameInfo: весна первого года — 0, остаток года — 1, второй год — 2, третий и дальше — 3.</summary>
    static int? Stardew(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        int? best = null;
        foreach (var farm in Directory.EnumerateDirectories(dir).Take(32))
        {
            var info = Path.Combine(farm, "SaveGameInfo");
            if (!File.Exists(info)) continue;
            try { best = Math.Max(best ?? 0, StardewStage(File.ReadAllText(info))); } catch { }
        }
        return best;
    }

    public static int StardewStage(string xml)
    {
        int Num(string tag) => Regex.Match(xml, $"<{tag}>(\\d+)</{tag}>") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
        var year = Math.Max(1, Num("yearForSaveGame"));
        var season = Num("seasonForSaveGame");
        return year >= 3 ? 3 : year == 2 ? 2 : season > 0 ? 1 : 0;
    }

    // ---------------------------------------------------------------- проверки

    [SelfTest]
    static string LoreStagesFromSaves()
    {
        // Сохранение Team Cherry собираем так же, как игра, и читаем обратно.
        var json = """{"playerData":{"completionPercentage":52.0,"visitedAbyss":true,"killedHollowKnight":false}}""";
        using var aes = Aes.Create();
        aes.Key = HkKey;
        var b64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(aes.EncryptEcb(Encoding.UTF8.GetBytes(json), PaddingMode.PKCS7)));
        var file = new List<byte> { 0, 1, 0, 0, 0, 255, 255, 255, 255, 1, 0, 0, 0, 0, 0, 0, 0, 6, 1, 0, 0, 0 };
        var n = b64.Length;
        while (n >= 0x80) { file.Add((byte)(n | 0x80)); n >>= 7; }
        file.Add((byte)n);
        file.AddRange(b64);
        file.Add(11);
        if (DecodeTeamCherry([.. file])?["playerData"] is not JsonObject p) throw new Exception("Team Cherry save not decoded");
        if (HollowKnightStage(p) != 2) throw new Exception("Hollow Knight: Abyss should be stage 2, got " + HollowKnightStage(p));
        var mc = JsonNode.Parse("""{"minecraft:nether/root":{"done":true},"minecraft:story/mine_stone":{"done":true}}""") as JsonObject;
        if (MinecraftStage(mc) != 2) throw new Exception("Minecraft: Nether should be stage 2");
        if (StardewStage("<Farmer><yearForSaveGame>2</yearForSaveGame><seasonForSaveGame>1</seasonForSaveGame></Farmer>") != 2) throw new Exception("Stardew: year 2 should be stage 2");
        foreach (var def in GameCatalog.Builtin)
            if (!Themes.ContainsKey(def.Id)) throw new Exception("no lore theme for " + def.Id);
            else if (Themes[def.Id].Stages.Length != 4) throw new Exception(def.Id + ": lore needs 4 stages");
        return $"lore: {Themes.Count} games with their own 4-stage launch, saves read for HK / Silksong / Minecraft / Stardew";
    }
}
