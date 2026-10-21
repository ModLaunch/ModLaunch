using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Mods;

namespace ModLaunch.Minecraft;

/// <summary>Свой сервер Minecraft: папка, версия, ядро (vanilla или Fabric) и главное из server.properties.</summary>
public sealed class McHosted
{
    public required string Id { get; init; }
    public string Name { get; set; } = "";
    public string GameVersion { get; set; } = "";
    /// <summary>vanilla | fabric</summary>
    public string Loader { get; set; } = "vanilla";
    /// <summary>Версия Fabric; пусто — самая свежая стабильная при установке.</summary>
    public string LoaderVersion { get; set; } = "";
    public string Dir { get; set; } = "";
    public int Port { get; set; } = McHost.DefaultPort;
    public int MaxPlayers { get; set; } = 10;
    public string Motd { get; set; } = "";
    /// <summary>peaceful | easy | normal | hard</summary>
    public string Difficulty { get; set; } = "normal";
    public bool WhiteList { get; set; }
    public int MemoryMb { get; set; } = 2048;
    /// <summary>Запись на доске серверов (Social.Servers), если сервер на неё выложен.</summary>
    public string Board { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.UtcNow;

    public bool Fabric => Loader == "fabric";
    /// <summary>Чем запускать: у Fabric — его загрузчик сервера (он берёт рядом лежащий server.jar).</summary>
    public string Jar => Fabric ? "fabric-server-launch.jar" : "server.jar";
    public bool Installed => File.Exists(Path.Combine(Dir, "server.jar")) && File.Exists(Path.Combine(Dir, Jar));
    /// <summary>«Fabric 1.21.1», «Minecraft 26.2».</summary>
    public string Label => Fabric ? $"Fabric {GameVersion}" : $"Minecraft {GameVersion}";

    public JsonObject ToJson() => new()
    {
        ["name"] = Name, ["gameVersion"] = GameVersion, ["loader"] = Loader, ["loaderVersion"] = LoaderVersion, ["port"] = Port,
        ["maxPlayers"] = MaxPlayers, ["motd"] = Motd, ["difficulty"] = Difficulty, ["whiteList"] = WhiteList, ["memoryMb"] = MemoryMb,
        ["board"] = Board, ["created"] = Created.ToString("o"),
    };

    public static McHosted FromJson(string id, string dir, JsonNode n) => new()
    {
        Id = id,
        Dir = dir,
        Name = n.Str("name") ?? id,
        GameVersion = n.Str("gameVersion") ?? "",
        Loader = n.Str("loader") == "fabric" ? "fabric" : "vanilla",
        LoaderVersion = n.Str("loaderVersion") ?? "",
        Port = (int)Math.Clamp(n.Long("port") is var p && p > 0 ? p : McHost.DefaultPort, 1024, 65535),
        MaxPlayers = (int)Math.Clamp(n.Long("maxPlayers") is var m && m > 0 ? m : 10, 1, McHost.MaxPlayers),
        Motd = n.Str("motd") ?? "",
        Difficulty = McHost.Difficulties.Contains(n.Str("difficulty")) ? n.Str("difficulty")! : "normal",
        WhiteList = n.Bool("whiteList"),
        MemoryMb = (int)Math.Clamp(n.Long("memoryMb") is var mb && mb > 0 ? mb : 2048, 1024, 65536),
        Board = n.Str("board") ?? "",
        Created = DateTime.TryParse(n.Str("created"), null, DateTimeStyles.AdjustToUniversal, out var c) ? c : DateTime.UtcNow,
    };
}

/// <summary>
/// 11.0: свой сервер Minecraft на этом ПК. Папка — рядом со сборками (.minecraft\ModLaunch\servers),
/// ядро — официальный server.jar Mojang (с проверкой sha1) и для Fabric — его загрузчик сервера.
/// eula.txt пишется только после галочки «Принимаю EULA Minecraft». Сервер идёт на Java из
/// программы, вывод консоли виден в ModLaunch, команды уходят в консоль сервера; при выходе из
/// ModLaunch сервер останавливается командой stop (мир сохраняется).
/// </summary>
public static class McHost
{
    public const int DefaultPort = 25565, MaxPlayers = 100, MaxMotd = 59;
    public const string EulaUrl = "https://aka.ms/MinecraftEULA";
    public static readonly string[] Difficulties = ["peaceful", "easy", "normal", "hard"];
    const string MetaFile = "modlaunch-server.json";
    const string InstallerMeta = McMeta.FabricMeta + "/versions/installer";

    static readonly Dictionary<string, McHostRun> Runs = [];
    static bool _exitHooked;

    /// <summary>Сервер создан, удалён, запущен, остановлен, поменялось число игроков (бывает из фонового потока).</summary>
    public static event Action? Changed;
    internal static void Notify() => Changed?.Invoke();

    /// <summary>Свои серверы — в .minecraft\ModLaunch\servers, рядом со сборками.</summary>
    public static string ServersDir => Path.Combine(Mc.Root, "ModLaunch", "servers");

    public static List<McHosted> List()
    {
        var list = new List<McHosted>();
        try
        {
            if (!Directory.Exists(ServersDir)) return list;
            foreach (var dir in Directory.EnumerateDirectories(ServersDir))
            {
                var file = Path.Combine(dir, MetaFile);
                if (!File.Exists(file)) continue;
                try { if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject o) list.Add(McHosted.FromJson(Path.GetFileName(dir), dir, o)); }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list.OrderByDescending(h => h.Created).ToList();
    }

    public static McHosted? Get(string? id) => id is null ? null : List().FirstOrDefault(h => h.Id == id);

    /// <summary>Порт по умолчанию — 25565; занят другим своим сервером — следующий свободный.</summary>
    public static int FreePort()
    {
        var used = List().Select(h => h.Port).ToHashSet();
        var port = DefaultPort;
        while (used.Contains(port)) port++;
        return port;
    }

    /// <summary>Новый сервер: папка и настройки. Файлы сервера скачиваются при первом запуске.</summary>
    public static McHosted Create(string name, string gameVersion, string loader, int port, int maxPlayers, string motd, string difficulty, bool whiteList, int memoryMb)
    {
        var title = name.Trim() == "" ? $"{Mc.LoaderTitle(loader)} {gameVersion}" : name.Trim();
        var slug = Mc.NewId(title);
        var id = slug;
        for (var n = 2; Directory.Exists(Path.Combine(ServersDir, id)); n++) id = $"{slug}-{n}";
        var h = new McHosted
        {
            Id = id, Dir = Directory.CreateDirectory(Path.Combine(ServersDir, id)).FullName, Name = title, GameVersion = gameVersion,
            Loader = loader == "fabric" ? "fabric" : "vanilla", Port = Math.Clamp(port, 1024, 65535), MaxPlayers = Math.Clamp(maxPlayers, 1, MaxPlayers),
            Motd = motd.Trim(), Difficulty = Difficulties.Contains(difficulty) ? difficulty : "normal", WhiteList = whiteList,
            MemoryMb = Math.Clamp(memoryMb, 1024, 65536),
        };
        Save(h);
        Notify();
        return h;
    }

    /// <summary>Записать описание сервера и его server.properties (чужие ключи в файле остаются как были).</summary>
    public static void Save(McHosted h)
    {
        Directory.CreateDirectory(h.Dir);
        File.WriteAllText(Path.Combine(h.Dir, MetaFile), h.ToJson().ToJsonString());
        var file = Path.Combine(h.Dir, "server.properties");
        var existing = File.Exists(file) ? File.ReadAllText(file) : null;
        File.WriteAllText(file, Properties(h, existing));
    }

    /// <summary>Убрать сервер: папка — в «удалённое» рядом (мир не пропадает молча). Запущенный не трогаем.</summary>
    public static void Delete(McHosted h)
    {
        if (Running(h.Id) is { Active: true }) throw new InvalidOperationException(I18n.T("v11.cm.host.err.running"));
        var trash = Path.Combine(Mc.Root, "ModLaunch", "deleted", $"server-{h.Id}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        Directory.Move(h.Dir, trash);
        if (h.Board != "") Social.Servers.Release(h.Board);
        Notify();
    }

    /// <summary>Сервер выложен на доску (или снят с неё): запомнить, и если он идёт — держать запись «в сети».</summary>
    public static void SetBoard(McHosted h, string board)
    {
        if (h.Board != "" && h.Board != board) Social.Servers.Release(h.Board);
        h.Board = board;
        Save(h);
        if (board != "" && Running(h.Id) is { State: "running" } run) Social.Servers.KeepAlive(board, () => run.Players);
        Notify();
    }

    // ---------------------------------------------------------------- server.properties и EULA

    /// <summary>Значение для .properties: обратная косая — дважды, не-ASCII — \uXXXX (так файл читает любая версия сервера).</summary>
    static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch < 0x20) continue;
            if (ch == '\\') sb.Append("\\\\");
            else if (ch > 0x7E) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>server.properties: свои ключи — поверх того, что уже в файле; остальные настройки сервера не трогаем.</summary>
    public static string Properties(McHosted h, string? existing)
    {
        var motd = h.Motd.Trim() == "" ? h.Name : h.Motd.Trim();
        var whiteList = h.WhiteList ? "true" : "false";
        var ours = new List<(string Key, string Value)>
        {
            ("motd", Escape(motd.Length > MaxMotd ? motd[..MaxMotd] : motd)),
            ("server-port", h.Port.ToString(CultureInfo.InvariantCulture)),
            ("max-players", h.MaxPlayers.ToString(CultureInfo.InvariantCulture)),
            ("difficulty", h.Difficulty),
            ("online-mode", "true"),
            ("white-list", whiteList),
            ("enforce-whitelist", whiteList),
            ("enable-status", "true"),
        };
        var lines = (existing ?? "").Replace("\r", "").Split('\n').Where(l => l.Trim() != "").ToList();
        var done = new HashSet<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith('#') || line.IndexOf('=') is var at && at <= 0) continue;
            var key = line[..at].Trim();
            var mine = ours.FindIndex(o => o.Key == key);
            if (mine < 0) continue;
            lines[i] = $"{key}={ours[mine].Value}";
            done.Add(key);
        }
        if (lines.Count == 0) lines.Add("#Minecraft server properties (ModLaunch)");
        lines.AddRange(ours.Where(o => !done.Contains(o.Key)).Select(o => $"{o.Key}={o.Value}"));
        return string.Join("\n", lines) + "\n";
    }

    public static bool EulaAccepted(McHosted h)
    {
        var file = Path.Combine(h.Dir, "eula.txt");
        try { return File.Exists(file) && File.ReadAllLines(file).Any(l => l.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Человек сам поставил галочку «Принимаю EULA Minecraft» — только тогда пишем eula=true (как просит сервер).</summary>
    public static void AcceptEula(McHosted h)
    {
        Directory.CreateDirectory(h.Dir);
        File.WriteAllText(Path.Combine(h.Dir, "eula.txt"),
            $"#By changing the setting below to TRUE you are indicating your agreement to our EULA ({EulaUrl}).\n" +
            $"#{DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture)} (ModLaunch)\neula=true\n");
        Notify();
    }

    // ---------------------------------------------------------------- файлы сервера

    /// <summary>Ссылка и sha1 официального server.jar версии: манифест Mojang → описание версии → downloads.server.</summary>
    static async Task<(string Url, string Sha1)> ServerJar(string gameVersion, CancellationToken ct)
    {
        var manifest = await Http.GetJson(McMeta.Manifest, ct);
        var entry = (manifest?["versions"] as JsonArray ?? []).OfType<JsonNode>().FirstOrDefault(v => v.Str("id") == gameVersion)
            ?? throw new InvalidOperationException(I18n.T("v11.cm.host.err.noVersion", ("version", gameVersion)));
        var info = await Http.GetJson(entry.Str("url") ?? throw new InvalidOperationException(gameVersion), ct);
        var server = info?["downloads"]?["server"];
        if (server.Str("url") is not { Length: > 0 } url || server.Str("sha1") is not { Length: > 0 } sha1)
            throw new InvalidOperationException(I18n.T("v11.cm.host.err.noServerJar", ("version", gameVersion)));
        return (url, sha1);
    }

    /// <summary>Свежий стабильный установщик Fabric — его номер нужен ссылке на загрузчик сервера.</summary>
    static async Task<string> FabricInstaller(CancellationToken ct)
    {
        var list = (await Http.GetJson(InstallerMeta, ct) as JsonArray ?? []).OfType<JsonNode>().ToList();
        return (list.FirstOrDefault(x => x.Bool("stable")) ?? list.FirstOrDefault()).Str("version")
            ?? throw new InvalidOperationException(I18n.T("v11.cm.host.err.fabricInstaller"));
    }

    /// <summary>Скачать server.jar (sha1 сверяется) и, для Fabric, загрузчик сервера.</summary>
    public static async Task Install(McHosted h, IProgress<InstallStep>? progress, CancellationToken ct)
    {
        var label = h.Label;
        var vanilla = Path.Combine(h.Dir, "server.jar");
        if (!File.Exists(vanilla))
        {
            progress?.Report(new InstallStep("loader.download", label));
            var (url, sha1) = await ServerJar(h.GameVersion, ct);
            var file = await Http.Download([url], "server.jar", new Progress<double>(r => progress?.Report(new InstallStep("loader.download", label, Ratio: r))), new Http.Hashes(Sha1: sha1), ct);
            File.Move(file, vanilla, true);
        }
        if (!h.Fabric || File.Exists(Path.Combine(h.Dir, h.Jar))) return;
        if (h.LoaderVersion == "")
        {
            h.LoaderVersion = await McMeta.PickLoader("fabric", h.GameVersion, ct: ct)
                ?? throw new InvalidOperationException(I18n.T("v11.cm.host.err.noFabric", ("version", h.GameVersion)));
            Save(h);
        }
        var installer = await FabricInstaller(ct);
        var launcher = $"{McMeta.FabricMeta}/versions/loader/{Uri.EscapeDataString(h.GameVersion)}/{Uri.EscapeDataString(h.LoaderVersion)}/{Uri.EscapeDataString(installer)}/server/jar";
        var jar = await Http.Download([launcher], h.Jar, new Progress<double>(r => progress?.Report(new InstallStep("loader.download", $"Fabric {h.LoaderVersion}", Ratio: r))), null, ct);
        File.Move(jar, Path.Combine(h.Dir, h.Jar), true);
        // Загрузчик Fabric берёт уже скачанный и проверенный server.jar, а не качает свой.
        File.WriteAllText(Path.Combine(h.Dir, "fabric-server-launcher.properties"), "serverJar=server.jar\n");
    }

    /// <summary>Нужна ли перед запуском загрузка (файлы сервера или Java) — тогда запуск идёт задачей в «Загрузках».</summary>
    public static bool NeedsDownload(McHosted h) => !h.Installed || McInstall.FindJava(McInstall.JavaFor(h.GameVersion)) is null;

    /// <summary>Аргументы Java: память из настроек сервера, вывод в UTF-8, без окна сервера.</summary>
    public static List<string> Args(McHosted h) =>
    [
        $"-Xmx{h.MemoryMb}M", $"-Xms{Math.Min(h.MemoryMb, 1024)}M", "-Dfile.encoding=UTF-8", "-Dstdout.encoding=UTF-8", "-Dstderr.encoding=UTF-8",
        "-jar", h.Jar, "nogui",
    ];

    /// <summary>Запустить сервер. Без принятой EULA — отказ (eula.txt сам никогда не пишется).</summary>
    public static async Task<McHostRun> Start(McHosted h, IProgress<InstallStep>? progress = null, CancellationToken ct = default)
    {
        if (Running(h.Id) is { Active: true } already) return already;
        if (!EulaAccepted(h)) throw new InvalidOperationException(I18n.T("v11.cm.host.err.eula"));
        if (!h.Installed) await Install(h, progress, ct);
        var java = McInstall.FindJava(McInstall.JavaFor(h.GameVersion)) ?? await McInstall.Java(progress, ct, McInstall.JavaFor(h.GameVersion));
        Save(h);
        var psi = new ProcessStartInfo(java)
        {
            WorkingDirectory = h.Dir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in Args(h)) psi.ArgumentList.Add(a);
        var process = Process.Start(psi) ?? throw new InvalidOperationException(java);
        var run = new McHostRun(h, process);
        lock (Runs)
        {
            Runs[h.Id] = run;
            if (!_exitHooked)
            {
                _exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll();
            }
        }
        run.Begin();
        Notify();
        return run;
    }

    public static McHostRun? Running(string id) { lock (Runs) return Runs.GetValueOrDefault(id); }

    internal static void Forget(McHostRun run)
    {
        lock (Runs) if (Runs.GetValueOrDefault(run.Host.Id) == run) Runs.Remove(run.Host.Id);
    }

    /// <summary>ModLaunch закрывается: каждому серверу — stop (мир сохраняется), не ответил за 10 секунд — закрыть.</summary>
    static void StopAll()
    {
        List<McHostRun> runs;
        lock (Runs) runs = [.. Runs.Values];
        foreach (var r in runs) r.StopNow(TimeSpan.FromSeconds(10));
    }

    /// <summary>Для снимков экрана: «идущий» сервер без Java — консоль и игроки из примера.</summary>
    internal static McHostRun Demo(McHosted h, IEnumerable<string> lines, int players)
    {
        var run = new McHostRun(h, null);
        run.DemoFill(lines, players);
        lock (Runs) Runs[h.Id] = run;
        return run;
    }

    // ---------------------------------------------------------------- моды и адрес

    /// <summary>Сборки Fabric той же версии, из которых можно взять моды.</summary>
    public static List<McInstance> ModSources(McHosted h) =>
        Mc.Instances().Where(i => i.Loader == "fabric" && i.GameVersion == h.GameVersion).ToList();

    /// <summary>Скопировать включённые моды сборки (mods\*.jar) в папку сервера. Возвращает, сколько скопировано.</summary>
    public static int CopyMods(McHosted h, McInstance from)
    {
        var source = from.Folder("mod");
        if (!Directory.Exists(source)) return 0;
        var target = Directory.CreateDirectory(Path.Combine(h.Dir, "mods")).FullName;
        var n = 0;
        foreach (var jar in Directory.EnumerateFiles(source, "*.jar"))
        {
            File.Copy(jar, Path.Combine(target, Path.GetFileName(jar)), true);
            n++;
        }
        Notify();
        return n;
    }

    public static int ModCount(McHosted h)
    {
        try { return Directory.Exists(Path.Combine(h.Dir, "mods")) ? Directory.GetFiles(Path.Combine(h.Dir, "mods"), "*.jar").Length : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    static bool Private(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
    }

    /// <summary>Адрес в домашней сети: частный IPv4 (192.168.*, 10.*, 172.16–31.*); у сетевой карты со шлюзом — первым.</summary>
    public static string? PickLan(IEnumerable<(IPAddress Address, bool Gateway)> candidates) =>
        candidates.Where(c => c.Address.AddressFamily == AddressFamily.InterNetwork && Private(c.Address))
            .OrderByDescending(c => c.Gateway).ThenBy(c => c.Address.GetAddressBytes()[0] == 192 ? 0 : c.Address.GetAddressBytes()[0] == 10 ? 1 : 2)
            .Select(c => c.Address.ToString()).FirstOrDefault();

    /// <summary>IPv4 этого ПК в локальной сети (внешний адрес сами не узнаём — его вводит человек).</summary>
    public static string? LanIp()
    {
        if (Program.Demo) return "192.168.1.5";
        var found = new List<(IPAddress, bool)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                var gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                found.AddRange(props.UnicastAddresses.Select(u => (u.Address, gateway)));
            }
        }
        catch (NetworkInformationException) { }
        return PickLan(found);
    }

    /// <summary>«192.168.1.5:25565» — адрес для друзей в той же сети.</summary>
    public static string? LanAddress(McHosted h) => LanIp() is { } ip ? $"{ip}:{h.Port}" : null;

    // ---------------------------------------------------------------- проверки

    [SelfTest]
    static string HostPropertiesWritten()
    {
        var h = new McHosted { Id = "t", Name = "Выживание", Port = 25570, MaxPlayers = 8, Difficulty = "hard", WhiteList = true, Motd = "Дом\\сад" };
        var text = Properties(h, "#Minecraft server properties\nlevel-seed=123\nserver-port=1\nmotd=Old\nmax-players=20\n");
        foreach (var want in new[] { "level-seed=123", "server-port=25570", "max-players=8", "difficulty=hard", "online-mode=true", "white-list=true", "enforce-whitelist=true", @"motd=\u0414\u043e\u043c\\\u0441\u0430\u0434" })
            if (!text.Split('\n').Contains(want)) throw new Exception($"missing «{want}» in:\n{text}");
        if (text.Split('\n').Count(l => l.StartsWith("server-port=")) != 1 || text.Contains("motd=Old")) throw new Exception("old values kept:\n" + text);
        var fresh = Properties(new McHosted { Id = "n", Name = "Мир" }, null);
        if (!fresh.Contains("server-port=25565") || !fresh.Contains("white-list=false") || !fresh.Contains(@"motd=\u041c\u0438\u0440")) throw new Exception(fresh);
        h.MemoryMb = 3072;
        var args = Args(h);
        if (!args.Contains("-Xmx3072M") || args[^1] != "nogui" || args[^2] != "server.jar") throw new Exception(string.Join(' ', args));
        return "own keys replaced, other keys kept, non-ASCII escaped; java gets -Xmx from the memory setting";
    }

    [SelfTest]
    static string HostNeedsEulaConsent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ml-host-" + Environment.ProcessId);
        try
        {
            var h = new McHosted { Id = "eula-test", Dir = dir, Name = "Test", GameVersion = "1.21.1" };
            Save(h);
            if (File.Exists(Path.Combine(dir, "eula.txt")) || EulaAccepted(h)) throw new Exception("eula.txt written on save");
            try
            {
                Task.Run(() => Start(h)).GetAwaiter().GetResult();
                throw new Exception("started without EULA");
            }
            catch (InvalidOperationException) { }
            if (File.Exists(Path.Combine(dir, "eula.txt"))) throw new Exception("eula.txt written by start");
            AcceptEula(h);
            if (!EulaAccepted(h) || !File.ReadAllText(Path.Combine(dir, "eula.txt")).Contains(EulaUrl)) throw new Exception("consent not saved");
            if (PickLan([(IPAddress.Parse("10.0.0.5"), false), (IPAddress.Parse("192.168.56.1"), false), (IPAddress.Parse("169.254.3.3"), true),
                    (IPAddress.Parse("8.8.8.8"), true), (IPAddress.Parse("192.168.1.5"), true)]) != "192.168.1.5")
                throw new Exception("wrong LAN address");
            return "eula.txt only after the checkbox; start refuses without it; LAN address is the private IPv4 with a gateway";
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}

/// <summary>
/// Запущенный сервер: процесс Java, последние строки консоли, команды в консоль и игроки онлайн
/// (сервер спрашиваем так же, как список серверов в игре, — McServers.Ping).
/// </summary>
public sealed class McHostRun
{
    const int Keep = 500;
    readonly Process? _process;
    readonly List<string> _lines = [];
    readonly CancellationTokenSource _watch = new();

    public McHosted Host { get; }
    /// <summary>starting | running | stopping | stopped</summary>
    public string State { get; private set; } = "starting";
    public int Players { get; private set; }
    public int Max { get; private set; }
    public int? ExitCode { get; private set; }
    public bool Active => State != "stopped";

    /// <summary>Новая строка консоли (из фонового потока).</summary>
    public event Action<string>? Line;

    internal McHostRun(McHosted host, Process? process)
    {
        Host = host;
        _process = process;
        Max = host.MaxPlayers;
    }

    public List<string> Lines { get { lock (_lines) return [.. _lines]; } }

    internal void Begin()
    {
        if (_process is null) return;
        _process.EnableRaisingEvents = true;
        _process.OutputDataReceived += (_, e) => { if (e.Data is { } line) Add(line); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) Add(line); };
        _process.Exited += (_, _) => Ended();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        if (_process.HasExited) Ended();
        _ = Task.Run(WatchPlayers);
    }

    void Add(string line)
    {
        lock (_lines)
        {
            _lines.Add(line);
            if (_lines.Count > Keep) _lines.RemoveRange(0, _lines.Count - Keep);
        }
        // «Done (3.2s)! For help, type "help"» — сервер готов принимать игроков.
        if (State == "starting" && line.Contains("Done (", StringComparison.Ordinal) && line.Contains("help", StringComparison.Ordinal))
        {
            State = "running";
            if (Host.Board != "") Social.Servers.KeepAlive(Host.Board, () => Players);
            McHost.Notify();
        }
        Line?.Invoke(line);
    }

    void Ended()
    {
        if (State == "stopped") return;
        State = "stopped";
        try { ExitCode = _process?.ExitCode; } catch (InvalidOperationException) { }
        _watch.Cancel();
        if (Host.Board != "") Social.Servers.Release(Host.Board);
        McHost.Forget(this);
        McHost.Notify();
    }

    /// <summary>Сколько игроков на сервере — раз в 15 секунд, пока он идёт.</summary>
    async Task WatchPlayers()
    {
        try
        {
            while (!_watch.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), _watch.Token);
                if (State != "running") continue;
                var status = await McServers.Ping($"127.0.0.1:{Host.Port}", _watch.Token);
                if (!status.Online || (status.Players == Players && status.Max == Max)) continue;
                (Players, Max) = (status.Players, status.Max);
                McHost.Notify();
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Команда в консоль сервера (одна строка, без переводов строки).</summary>
    public void Send(string command)
    {
        var line = command.Replace("\r", " ").Replace("\n", " ").Trim();
        if (line == "" || !Active) return;
        if (line.Length > 256) line = line[..256];
        Add("> " + line);
        if (_process is null) return;
        try
        {
            _process.StandardInput.WriteLine(line);
            _process.StandardInput.Flush();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException) { }
    }

    /// <summary>Остановить командой stop (мир сохраняется); не остановился за минуту — закрыть процесс.</summary>
    public async Task Stop()
    {
        if (!Active) return;
        State = "stopping";
        McHost.Notify();
        Send("stop");
        if (_process is null) { Ended(); return; }
        using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try { await _process.WaitForExitAsync(wait.Token); }
        catch (OperationCanceledException) { Kill(); }
    }

    /// <summary>Выход из программы: то же, но ждём не дольше <paramref name="wait"/>.</summary>
    internal void StopNow(TimeSpan wait)
    {
        if (!Active || _process is null) return;
        Send("stop");
        try { if (!_process.WaitForExit(wait)) Kill(); }
        catch (InvalidOperationException) { }
    }

    void Kill()
    {
        try { _process?.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    internal void DemoFill(IEnumerable<string> lines, int players)
    {
        lock (_lines) _lines.AddRange(lines);
        State = "running";
        Players = players;
    }
}
