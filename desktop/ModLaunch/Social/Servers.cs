using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Creator;
using ModLaunch.Features;
using ModLaunch.Games;

namespace ModLaunch.Social;

/// <summary>Сервер на доске: кто открыл, во что играют, куда подключаться и сколько людей уже там.</summary>
public sealed record GameServer(string Id, string Owner, string OwnerName, string Name, string Game, string Address, string Build, int Mods,
    string Visibility, int Players, int Max, string Version, DateTime? Created, DateTime? Updated)
{
    public bool Public => Visibility == "public";
}

/// <summary>Что человек ввёл в «Создать сервер». Id есть только у правки своего сервера.</summary>
public sealed record ServerDraft(string? Id, string Name, string Game, string Address, string Build = "", int Mods = 0,
    string Visibility = "friends", int Players = 0, int Max = 10, string Version = "");

/// <summary>
/// Как подключиться: игра есть на этом ПК; это Minecraft (адрес — в «Сетевую игру» сборки)
/// или другая игра (сначала сборка по коду, потом адрес в буфер обмена и запуск).
/// </summary>
public sealed record JoinPlan(bool GameHere, bool Minecraft, bool InstallBuild, bool AddToList, bool CopyAddress);

/// <summary>
/// 11.0: доска серверов для игры вместе — Firestore, коллекция servers/{id} (правила — firebase/firestore.rules).
/// Видно свои серверы, серверы друзей и открытые всем. Сервер «в сети», пока его отметка (updated)
/// свежее 15 минут: ModLaunch обновляет её раз в пять минут, пока программа открыта.
/// «Только друзьям» — это «не в общем списке», а не секрет: правила дают читать доску любому
/// вошедшему с почтой, а чужое отсеивает программа. Поэтому паролей на доске нет и быть не должно —
/// закрытый сервер закрывают белым списком или паролем в самой игре.
/// </summary>
public static partial class Servers
{
    public const int MaxName = 40, MaxAddress = 100, MaxBuild = 64, MaxVersion = 24, MaxPlayers = 1000, MaxMods = 5000, MaxMine = 5;
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(15);
    static readonly TimeSpan Beat = TimeSpan.FromMinutes(5);
    /// <summary>Чужие серверы, молчащие дольше недели, на доску не попадают — их забыли закрыть.</summary>
    static readonly TimeSpan Stale = TimeSpan.FromDays(7);
    const string IdAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$")] private static partial Regex Label();
    [GeneratedRegex(@"^[0-9A-Fa-f:.]+$")] private static partial Regex Hex6();
    [GeneratedRegex(@"^[\p{L}\p{N} .+_-]*$")] private static partial Regex VersionRe();
    [GeneratedRegex(@"[\u0000-\u001f\u007f]")] private static partial Regex Control();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    static List<GameServer>? _cache;
    static DateTime _cachedAt;
    static readonly SemaphoreSlim ListGate = new(1);
    static readonly Dictionary<string, Func<int>?> Live = [];
    static Timer? _beat;

    /// <summary>Доска или «в сети» поменялись (бывает и из фонового потока).</summary>
    public static event Action? Changed;

    /// <summary>Для снимков экрана: доска без сервера; свои — с владельцем <see cref="DemoUid"/>.</summary>
    internal static List<GameServer>? DemoList { get; set; }
    internal const string DemoUid = "demo-me";

    /// <summary>Чей это аккаунт (свои серверы можно править и удалять).</summary>
    public static string? Me => Program.Demo && DemoList is not null ? DemoUid : Account.Uid;

    /// <summary>Последняя загруженная доска (для вкладки друзей: «их серверы»).</summary>
    public static List<GameServer> Cached => Program.Demo && DemoList is { } demo ? Visible(demo, DemoUid, FriendUids(), DateTime.UtcNow) : _cache ?? [];

    // ---------------------------------------------------------------- проверки (без сети)

    /// <summary>Строка без управляющих символов и лишних пробелов.</summary>
    static string Tidy(string? value) => Spaces().Replace(Control().Replace(value ?? "", ""), " ").Trim();

    /// <summary>
    /// Адрес сервера: «192.168.1.5:25565», «play.example.net», «[2001:db8::1]:25565» или код комнаты («ABCD-1234», «123456»).
    /// Пусто — можно. Ссылки, логины с паролем и адреса этого ПК (127.0.0.1, localhost) — нет.
    /// </summary>
    public static (string Address, string? Error) CheckAddress(string? input)
    {
        var a = (input ?? "").Trim();
        if (a == "") return ("", null);
        if (a.Length > MaxAddress) return (a, "v11.cm.err.addressLong");
        if (a.Any(char.IsWhiteSpace) || a.Contains("://") || a.IndexOfAny(['@', '/', '\\', '?', '#']) >= 0) return (a, "v11.cm.err.address");
        string host;
        var rest = "";
        if (a.StartsWith('['))
        {
            var close = a.IndexOf(']');
            if (close < 0) return (a, "v11.cm.err.address");
            host = a[1..close];
            rest = a[(close + 1)..];
            if (!Hex6().IsMatch(host) || !IPAddress.TryParse(host, out var ip6) || ip6.AddressFamily != AddressFamily.InterNetworkV6) return (a, "v11.cm.err.address");
            if (IPAddress.IsLoopback(ip6)) return (a, "v11.cm.err.loopback");
        }
        else
        {
            var colon = a.IndexOf(':');
            if (colon >= 0 && a.LastIndexOf(':') != colon) return (a, "v11.cm.err.address"); // IPv6 — только в [скобках]
            host = colon >= 0 ? a[..colon] : a;
            rest = colon >= 0 ? a[colon..] : "";
            var labels = host.Split('.');
            if (labels.Any(l => !Label().IsMatch(l))) return (a, "v11.cm.err.address");
            if (labels.Length > 1 && labels.All(l => l.All(char.IsAsciiDigit)))
            {
                // Похоже на IPv4: ровно четыре числа до 255.
                if (labels.Length != 4 || labels.Any(l => l.Length > 3 || int.Parse(l, CultureInfo.InvariantCulture) > 255)) return (a, "v11.cm.err.address");
                var ip = IPAddress.Parse(host);
                if (IPAddress.IsLoopback(ip)) return (a, "v11.cm.err.loopback");
                if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast)) return (a, "v11.cm.err.address");
            }
            else if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return (a, "v11.cm.err.loopback");
        }
        if (rest != "")
        {
            if (!rest.StartsWith(':') || rest.Length > 6 || !int.TryParse(rest[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var port)) return (a, "v11.cm.err.address");
            if (port is < 1 or > 65535) return (a, "v11.cm.err.port");
        }
        return (a, null);
    }

    /// <summary>Черновик в том виде, в каком он уйдёт на доску: без лишних пробелов и управляющих символов.</summary>
    public static ServerDraft Normalize(ServerDraft d) => d with
    {
        Name = Tidy(d.Name), Game = d.Game.Trim(), Address = CheckAddress(d.Address).Address, Build = d.Build.Trim(), Version = Tidy(d.Version),
        Visibility = d.Visibility.Trim(),
    };

    /// <summary>Что не так с черновиком: ключи текстов ошибок (пусто — можно публиковать).</summary>
    public static List<string> Check(ServerDraft draft)
    {
        var d = Normalize(draft);
        var errors = new List<string>();
        if (d.Name.Length == 0) errors.Add("v11.cm.err.name");
        else if (d.Name.Length > MaxName) errors.Add("v11.cm.err.nameLong");
        var def = d.Game.Length is > 0 and <= 40 ? GameCatalog.ById(d.Game) : null;
        if (def is null) errors.Add("v11.cm.err.game");
        if (CheckAddress(draft.Address).Error is { } addressError) errors.Add(addressError);
        if (d.Build != "")
        {
            if (d.Build.Length > MaxBuild || !ProfileCode.LooksLikeCode(d.Build)) errors.Add("v11.cm.err.build");
            else if (def is not null && !ProfileCode.Supported(def)) errors.Add("v11.cm.err.buildGame");
        }
        if (def is { IsMinecraft: true } && d.Address == "") errors.Add("v11.cm.err.needAddress");
        else if (d.Address == "" && d.Build == "") errors.Add("v11.cm.err.nothing");
        if (d.Visibility is not ("friends" or "public")) errors.Add("v11.cm.err.visibility");
        if (d.Max is < 1 or > MaxPlayers) errors.Add("v11.cm.err.max");
        else if (d.Players < 0 || d.Players > d.Max) errors.Add("v11.cm.err.players");
        if (d.Mods is < 0 or > MaxMods) errors.Add("v11.cm.err.mods");
        if (d.Version.Length > MaxVersion || !VersionRe().IsMatch(d.Version)) errors.Add("v11.cm.err.version");
        return errors;
    }

    /// <summary>«В сети»: отметка свежее 15 минут по часам сервера. Отметка «из будущего» (часы ПК отстают) — тоже в сети.</summary>
    public static bool IsOnline(DateTime? updated, DateTime serverNow) => updated is DateTime u && serverNow - u < Fresh;

    public static bool IsOnline(GameServer s) => IsOnline(s.Updated, DateTime.UtcNow + Firebase.Skew);

    /// <summary>
    /// Что показать: свои, серверы друзей и открытые всем. Чужие «только друзьям», давно молчащие
    /// и записи с негодным адресом — нет. Сверху свои, потом те, что в сети, потом свежие.
    /// </summary>
    public static List<GameServer> Visible(IEnumerable<GameServer> all, string? me, ISet<string> friends, DateTime serverNow) =>
        all.Where(s => CheckAddress(s.Address).Error is null && s.Name != "")
            .Where(s => s.Owner == me || ((s.Public || friends.Contains(s.Owner)) && s.Updated is DateTime u && serverNow - u < Stale))
            .OrderByDescending(s => s.Owner == me).ThenByDescending(s => IsOnline(s.Updated, serverNow)).ThenByDescending(s => s.Updated)
            .ToList();

    /// <summary>Как подключиться к серверу с этого ПК.</summary>
    public static JoinPlan Plan(GameServer s, GameState? game)
    {
        var here = game is { Status: Detect.Found, Path: not null } && game.Def.Id == s.Game;
        var minecraft = s.Game == Minecraft.Mc.Id;
        var build = here && !minecraft && s.Build != "" && ProfileCode.Supported(game!.Def);
        return new JoinPlan(here, minecraft, build, here && minecraft && s.Address != "", here && !minecraft && s.Address != "");
    }

    // ---------------------------------------------------------------- доска

    static HashSet<string> FriendUids() => Friends.View().Friends.Select(f => f.Uid).ToHashSet();

    static GameServer FromDoc(JsonObject doc)
    {
        var f = Firebase.FromFields(doc["fields"] as JsonObject);
        var name = doc.Str("name") ?? "";
        return new GameServer(name[(name.LastIndexOf('/') + 1)..], f.S("owner"), Account.CleanName(f.S("ownerName")), Tidy(f.S("name")), f.S("game"),
            f.S("address"), f.S("build"), (int)Math.Clamp(f.L("mods"), 0, MaxMods), f.S("visibility") == "public" ? "public" : "friends",
            (int)Math.Clamp(f.L("players"), 0, MaxPlayers), (int)Math.Clamp(f.L("max"), 1, MaxPlayers), Tidy(f.S("version")),
            Firebase.Time(f.S("created")), Firebase.Time(f.S("updated")));
    }

    /// <summary>Доска: свои серверы, друзей и открытые (до 200 свежих). Кэш на полминуты.</summary>
    public static async Task<List<GameServer>> List(bool force = false)
    {
        if (Program.Demo && DemoList is not null) return Cached;
        if (!force && _cache is not null && DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(30)) return _cache;
        await ListGate.WaitAsync();
        try
        {
            var (uid, token, _) = await Hub.Member();
            // Друзья — из общего списка (Friends); не обновился — берём последний известный.
            try { await Friends.Refresh(); } catch (ServiceError) { }
            var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject
            {
                ["structuredQuery"] = new JsonObject
                {
                    ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "servers" }),
                    ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "updated" }, ["direction"] = "DESCENDING" }),
                    ["limit"] = 200,
                },
            }, token);
            var all = (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(FromDoc);
            _cache = Visible(all, uid, FriendUids(), DateTime.UtcNow + Firebase.Skew);
            _cachedAt = DateTime.UtcNow;
            Changed?.Invoke();
            return _cache;
        }
        finally { ListGate.Release(); }
    }

    static string NewId() => RandomNumberGenerator.GetString(IdAlphabet, 20);

    /// <summary>Создать свой сервер (Id = null) или поправить свой. Сервер сразу «в сети» и держится в ней, пока открыт ModLaunch.</summary>
    public static async Task<GameServer> Save(ServerDraft draft)
    {
        if (Check(draft) is [var first, ..]) throw new ArgumentException(I18n.T(first, ("n", MaxName)));
        var d = Normalize(draft);
        var (uid, token, name) = await Hub.Member();
        var create = d.Id is null;
        if (create && (await List()).Count(s => s.Owner == uid) >= MaxMine) throw new ServiceError("TOO_MANY");
        var id = d.Id ?? NewId();
        var fields = new Dictionary<string, object?>
        {
            ["owner"] = uid, ["ownerName"] = name, ["name"] = d.Name, ["game"] = d.Game, ["address"] = d.Address, ["build"] = d.Build,
            ["mods"] = d.Mods, ["visibility"] = d.Visibility, ["players"] = d.Players, ["max"] = d.Max, ["version"] = d.Version,
        };
        var write = new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"servers/{id}"), ["fields"] = Firebase.ToFields(fields) },
            ["updateTransforms"] = create ? new JsonArray(Hub.ServerTime("created"), Hub.ServerTime("updated")) : new JsonArray(Hub.ServerTime("updated")),
            ["currentDocument"] = new JsonObject { ["exists"] = !create },
        };
        if (!create) write["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) };
        await Hub.Commit(new JsonArray(write), token);
        var now = DateTime.UtcNow + Firebase.Skew;
        var saved = new GameServer(id, uid, name, d.Name, d.Game, d.Address, d.Build, d.Mods, d.Visibility, d.Players, d.Max, d.Version,
            create ? now : _cache?.FirstOrDefault(s => s.Id == id)?.Created, now);
        Put(saved);
        KeepAlive(id);
        return saved;
    }

    /// <summary>Убрать свой сервер с доски.</summary>
    public static async Task Remove(string id)
    {
        var (_, token, _) = await Hub.Member();
        await Hub.Commit(new JsonArray(new JsonObject { ["delete"] = Firebase.Doc($"servers/{id}") }), token);
        lock (Live) Live.Remove(id);
        _cache = _cache?.Where(s => s.Id != id).ToList();
        Changed?.Invoke();
    }

    /// <summary>Отметка «сервер жив» и сколько на нём игроков.</summary>
    public static async Task Heartbeat(string id, int players)
    {
        var (_, token, _) = await Hub.Member();
        var known = _cache?.FirstOrDefault(s => s.Id == id);
        var count = Math.Clamp(players, 0, known?.Max ?? MaxPlayers);
        await Hub.Commit(new JsonArray(new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"servers/{id}"), ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["players"] = count }) },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("players") },
            ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
            ["currentDocument"] = new JsonObject { ["exists"] = true },
        }), token);
        if (known is not null) Put(known with { Players = count, Updated = DateTime.UtcNow + Firebase.Skew });
    }

    static void Put(GameServer s)
    {
        var list = (_cache ?? []).Where(x => x.Id != s.Id).Prepend(s).ToList();
        _cache = Visible(list, Me, FriendUids(), DateTime.UtcNow + Firebase.Skew);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- «в сети», пока открыт ModLaunch

    /// <summary>
    /// Держать свой сервер «в сети»: отметка раз в пять минут, пока открыт ModLaunch. players — сколько
    /// игроков сейчас (свой сервер Minecraft считает их сам); без него — последнее известное число.
    /// </summary>
    public static void KeepAlive(string id, Func<int>? players = null)
    {
        lock (Live)
        {
            Live[id] = players;
            _beat ??= new Timer(_ => _ = BeatAll(), null, Beat, Beat);
        }
        Changed?.Invoke();
    }

    /// <summary>Больше не держать «в сети»: через 15 минут сервер на доске станет «не в сети».</summary>
    public static void Release(string id)
    {
        bool removed;
        lock (Live) removed = Live.Remove(id);
        if (removed) Changed?.Invoke();
    }

    public static bool IsLive(string id) { lock (Live) return Live.ContainsKey(id); }

    static async Task BeatAll()
    {
        // В снимках экрана доска — пример, отмечать некуда.
        if (Program.Demo && DemoList is not null) return;
        List<(string Id, Func<int>? Players)> live;
        lock (Live) live = Live.Select(kv => (kv.Key, kv.Value)).ToList();
        foreach (var (id, players) in live)
        {
            try { await Heartbeat(id, players?.Invoke() ?? _cache?.FirstOrDefault(s => s.Id == id)?.Players ?? 0); }
            // Записи больше нет, нет прав или вышли из аккаунта — отмечать нечего.
            catch (ServiceError e) when (e.Code is "NOT_FOUND" or "PRECONDITION" or "DENIED" or "SIGN_IN" or "SESSION") { Release(id); }
            // Нет сети или служба занята — попробуем через пять минут.
            catch (ServiceError) { }
            catch (Exception e) { Guard.Log(e); }
        }
    }

    /// <summary>Понятный текст ошибки доски. Подробности настройки сервиса — только владельцу.</summary>
    public static string Explain(Exception e) => e switch
    {
        ServiceError { Code: "DENIED" } => I18n.T(Account.Owner ? "v11.cm.err.denied.owner" : "v11.cm.err.down"),
        ServiceError { Code: "NOT_CONFIGURED" } when Account.Owner => I18n.T("v11.cm.err.config.owner"),
        ServiceError { Code: "SIGN_IN" or "SESSION" } => I18n.T("v11.cm.err.signin"),
        ServiceError { Code: "OFFLINE" } => I18n.T("v11.cm.err.offline"),
        ServiceError { Code: "BUSY" }=> I18n.T("v11.cm.err.busy"),
        ServiceError { Code: "TOO_MANY" } => I18n.T("v11.cm.err.tooMany", ("n", MaxMine)),
        ServiceError { Code: "NOT_FOUND" or "PRECONDITION" } => I18n.T("v11.cm.err.gone"),
        ServiceError s when Account.Owner => I18n.T("v11.cm.err.server", ("reason", s.Message)),
        ServiceError => I18n.T("v11.cm.err.down"),
        ArgumentException a => a.Message,
        OfflineException or HttpRequestException => I18n.T("v11.cm.err.offline"),
        _ => Jobs.Explain(e),
    };

    // ---------------------------------------------------------------- проверки

    [SelfTest]
    static string ServerAddressesChecked()
    {
        foreach (var good in new[] { "192.168.1.5:25565", "play.example.net", "mc.example.org:25566", "[2001:db8::1]:25565", "ABCD-1234", "123456", "Lobby-7" })
            if (CheckAddress(good).Error is { } e) throw new Exception($"rejected {good}: {e}");
        if (CheckAddress("  play.example.net  ").Address != "play.example.net") throw new Exception("not trimmed");
        var bad = new Dictionary<string, string>
        {
            ["999.1.1.1"] = "v11.cm.err.address", ["1.2.3"] = "v11.cm.err.address", ["host:70000"] = "v11.cm.err.port", ["host:0"] = "v11.cm.err.port",
            ["http://evil.example"] = "v11.cm.err.address", ["user:pass@host"] = "v11.cm.err.address", ["a b"] = "v11.cm.err.address",
            ["2001:db8::1"] = "v11.cm.err.address", ["127.0.0.1:25565"] = "v11.cm.err.loopback", ["localhost"] = "v11.cm.err.loopback",
            ["[::1]"] = "v11.cm.err.loopback", ["host:"] = "v11.cm.err.address", ["-bad-.example"] = "v11.cm.err.address",
            [new string('a', 101)] = "v11.cm.err.addressLong",
        };
        foreach (var (input, want) in bad)
            if (CheckAddress(input).Error != want) throw new Exception($"{input}: got {CheckAddress(input).Error ?? "ok"}, want {want}");
        if (CheckAddress("").Error is not null) throw new Exception("empty address must be allowed");
        return $"{bad.Count} bad addresses rejected (links, credentials, loopback, ports), IPs, hosts and room codes accepted";
    }

    [SelfTest]
    static string ServerDraftsChecked()
    {
        var ok = new ServerDraft(null, "  Ночь  с друзьями ", "lethal-company", "203.0.113.7:7777", "018f2c3a-6b1e-4f1b-9c55-2d4e8a1f3b77", 38, "friends", 2, 4, "v64");
        if (Check(ok) is [var e, ..]) throw new Exception("good draft rejected: " + e);
        if (Normalize(ok).Name != "Ночь с друзьями") throw new Exception("name not tidied");
        void Want(ServerDraft d, string key) { if (!Check(d).Contains(key)) throw new Exception($"{key} not reported: {string.Join(", ", Check(d))}"); }
        Want(ok with { Name = "   " }, "v11.cm.err.name");
        Want(ok with { Name = new string('x', MaxName + 1) }, "v11.cm.err.nameLong");
        Want(ok with { Game = "no-such-game" }, "v11.cm.err.game");
        Want(ok with { Visibility = "secret" }, "v11.cm.err.visibility");
        Want(ok with { Max = 0 }, "v11.cm.err.max");
        Want(ok with { Players = 5 }, "v11.cm.err.players");
        Want(ok with { Build = "not a code" }, "v11.cm.err.build");
        Want(ok with { Address = "", Build = "" }, "v11.cm.err.nothing");
        Want(ok with { Version = "1.0<script>" }, "v11.cm.err.version");
        var mc = new ServerDraft(null, "Выживание", Minecraft.Mc.Id, "", Max: 10, Version: "1.21.1");
        Want(mc, "v11.cm.err.needAddress");
        Want(mc with { Address = "192.168.1.5", Build = "018f2c3a-6b1e-4f1b-9c55-2d4e8a1f3b77" }, "v11.cm.err.buildGame");
        if (Check(mc with { Address = "192.168.1.5:25565" }).Count != 0) throw new Exception("minecraft draft rejected: " + string.Join(", ", Check(mc with { Address = "192.168.1.5:25565" })));
        return "names, games, addresses, build codes, players and visibility are checked before publishing";
    }

    [SelfTest]
    static string ServerHeartbeatFreshness()
    {
        var now = new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc);
        if (!IsOnline(now.AddMinutes(-14), now) || IsOnline(now.AddMinutes(-16), now) || IsOnline(null, now) || !IsOnline(now.AddMinutes(2), now))
            throw new Exception("15-minute window is wrong");
        GameServer S(string id, string owner, string visibility, double minutesAgo) =>
            new(id, owner, owner, id, "valheim", "203.0.113.7:2456", "", 0, visibility, 0, 10, "", now.AddDays(-30), now.AddMinutes(-minutesAgo));
        var list = Visible(
        [
            S("open-old", "stranger", "public", 20), S("mine", "me", "friends", 60 * 24 * 30), S("friend", "pal", "friends", 5),
            S("hidden", "stranger", "friends", 1), S("open-fresh", "stranger", "public", 3), S("forgotten", "pal", "public", 60 * 24 * 8),
            S("bad", "pal", "public", 1) with { Address = "http://evil" },
        ], "me", new HashSet<string> { "pal" }, now);
        var ids = string.Join(",", list.Select(s => s.Id));
        if (ids != "mine,open-fresh,friend,open-old") throw new Exception(ids);
        return "online for 15 minutes after the last heartbeat; friends-only servers of strangers and stale ones are hidden";
    }
}
