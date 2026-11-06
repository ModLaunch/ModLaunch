using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Mods;
using ModLaunch.Social;

namespace ModLaunch.Features;

/// <summary>Мод в коде сборки. FileId — точный файл Nexus (чтобы у друга была та же версия, а не последняя).</summary>
public sealed record ShareMod(string Id, string Name, string Version, string Source, bool Enabled, long FileId = 0)
{
    public SetupMod Setup => new(Id, Name, Version, Source, Enabled);
}

/// <summary>Файл сборки Minecraft с Modrinth: путь в сборке, ссылка и контрольные суммы.</summary>
public sealed record ShareFile(string Path, string Sha1, string Sha512, string Url, long Size);

/// <summary>
/// Сборка из кода: игра, моды с версиями, у Minecraft — версия игры, загрузчик и файлы, настройки (zip)
/// и что не попало в код (моды из файла: их нет ни в одном каталоге).
/// </summary>
public sealed record SharePack(string Game, string GameName, string Name, List<ShareMod> Mods,
    string? McVersion = null, string? McLoader = null, string? McLoaderVersion = null, List<ShareFile>? Files = null,
    byte[]? Config = null, List<string>? Left = null)
{
    public bool IsMinecraft => McVersion is not null;
    public int Count => IsMinecraft ? Files?.Count ?? 0 : Mods.Count;
}

/// <summary>
/// 24.0: «Поделиться сборкой» для всех игр. Код из 8 знаков (ABCD-EFGH) — документ packs/{код} в Firestore:
/// друг вставляет код и получает точно такие же моды, у Thunderstore и Nexus — тех же версий. Сервер
/// недоступен — длинный код «ML1-…» со сборкой внутри (без настроек): работает и так.
/// Для Thunderstore по-прежнему есть код r2modman (ProfileCode) — его понимают r2modman и Gale.
/// </summary>
public static partial class ShareCode
{
    const string Format = "modlaunch-share";
    const int FormatVersion = 1;
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    const string LongPrefix = "ML1-";
    /// <summary>Сколько можно положить в один документ (у Firestore предел — 1 МБ на документ).</summary>
    public const int MaxData = 400_000;
    /// <summary>Настройки больше этого в код не кладём — моды важнее.</summary>
    const int MaxConfig = 250_000;

    [GeneratedRegex("^[A-HJ-NP-Z2-9]{8}$")] private static partial Regex ShortRe();
    [GeneratedRegex(@"(?:modlaunch://pack/|[#?&/]pack[=/])([A-Za-z0-9-]{8,9})", RegexOptions.IgnoreCase)] private static partial Regex LinkRe();
    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")] private static partial Regex UuidRe();

    /// <summary>Что вставили: short — наш код, long — код со сборкой внутри, r2 — код r2modman (Thunderstore).</summary>
    public sealed record Input(string Kind, string Value);

    /// <summary>Длинный код внутри текста: «ML1-…» до конца (строки по 64 знака тоже склеиваются).</summary>
    [GeneratedRegex(@"ML1-([A-Za-z0-9_\-]+(?:\r?\n[A-Za-z0-9_\-]+)*)", RegexOptions.IgnoreCase)] private static partial Regex LongInTextRe();
    /// <summary>Короткий код внутри текста приглашения: «… вставь: K7QM-9XPA».</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-HJ-NP-Za-hj-np-z2-9]{4})-([A-HJ-NP-Za-hj-np-z2-9]{4})(?![A-Za-z0-9])")] private static partial Regex ShortInTextRe();
    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")] private static partial Regex UuidInTextRe();

    /// <summary>
    /// Что вставили. Понимает и голый код, и целое приглашение из чата («Играем в … вставь: K7QM-9XPA») —
    /// по умолчанию в буфер обмена кладётся именно приглашение.
    /// </summary>
    public static Input? Parse(string? text)
    {
        text = (text ?? "").Trim().Trim('"', '\'', '«', '»', '.', ',');
        if (text == "" || text.Length > 200_000) return null;
        if (text.StartsWith(LongPrefix, StringComparison.OrdinalIgnoreCase)) return new Input("long", Regex.Replace(text[LongPrefix.Length..], @"\s+", ""));
        if (LinkRe().Match(text) is { Success: true } link) text = link.Groups[1].Value;
        if (UuidRe().IsMatch(text)) return new Input("r2", text.ToLowerInvariant());
        var compact = text.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (ShortRe().IsMatch(compact)) return new Input("short", compact);
        // Код внутри текста приглашения.
        if (LongInTextRe().Match(text) is { Success: true } inLong) return new Input("long", Regex.Replace(inLong.Groups[1].Value, @"\s+", ""));
        if (ShortInTextRe().Matches(text) is { Count: > 0 } found)
            return new Input("short", (found[^1].Groups[1].Value + found[^1].Groups[2].Value).ToUpperInvariant());
        if (UuidInTextRe().Match(text) is { Success: true } uuid) return new Input("r2", uuid.Value.ToLowerInvariant());
        return null;
    }

    /// <summary>
    /// Похоже на код в строке поиска: «ABCD-EFGH» заглавными (так код и показывается), ссылка, длинный код
    /// или приглашение целиком. «wall-jump» и слово из 8 букв — это поиск.
    /// </summary>
    public static bool LooksLikeCode(string? text)
    {
        text = (text ?? "").Trim();
        if (text.StartsWith(LongPrefix, StringComparison.OrdinalIgnoreCase) || LinkRe().IsMatch(text)) return true;
        if (Regex.IsMatch(text, "^[A-Z2-9]{4}-[A-Z2-9]{4}$")) return Parse(text) is { Kind: "short" };
        // Вставили приглашение целиком: длинный текст с кодом заглавными в конце.
        return text.Length > 30 && Regex.IsMatch(text, @"(?<![A-Za-z0-9])[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}\s*$") || LongInTextRe().IsMatch(text);
    }

    public static string Pretty(string code) => code.Length == 8 ? $"{code[..4]}-{code[4..]}" : code;

    // ---------------------------------------------------------------- собрать

    /// <summary>Моды игры для кода: всё из каталогов; из файла — в Left (другу их не скачать).</summary>
    public static SharePack FromRegistry(GameDef game, ModRegistry registry, string name, bool withConfig = true)
    {
        registry.Reconcile();
        var mods = new List<ShareMod>();
        var left = new List<string>();
        foreach (var r in registry.List().Where(r => !r.Bool("missing") && r.Str("target") is null && r.Str("kind") != "preset"))
        {
            var id = r.Str("id")!;
            if (id.Contains('#')) continue; // вторая папка мода — едет вместе с ним
            var source = r.Str("source") ?? "file";
            if (source == "file" || game.CatalogId(id) is null) { left.Add(r.Str("name") ?? id); continue; }
            mods.Add(new ShareMod(id, r.Str("name") ?? id, r.Str("version") ?? "", source, r.Bool("enabled", true), r.Long("fileId")));
        }
        byte[]? config = null;
        if (withConfig)
        {
            try
            {
                using var buffer = new MemoryStream();
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
                    foreach (var (path, entry) in ModSetup.ConfigFiles(registry))
                        try { zip.CreateEntryFromFile(path, entry, CompressionLevel.SmallestSize); } catch { }
                if (buffer.Length is > 22 and <= MaxConfig) config = buffer.ToArray();
            }
            catch { }
        }
        return new SharePack(game.Id, game.Name, name, mods, Config: config, Left: left);
    }

    /// <summary>Сборка Minecraft: моды, ресурспаки и шейдеры с Modrinth (по SHA-1, одним запросом); остальное — в Left.</summary>
    public static async Task<SharePack> FromMinecraft(Minecraft.McInstance i, CancellationToken ct = default,
        Func<IReadOnlyList<string>, CancellationToken, Task<Dictionary<string, Minecraft.McExport.Remote>>>? lookup = null)
    {
        var root = Path.GetFullPath(i.Dir);
        var local = new List<(string Rel, string Sha1, string Sha512, long Size, string Name)>();
        foreach (var kind in new[] { "mod", "resourcepack", "shader" })
            foreach (var item in Minecraft.McContent.List(i, kind).Where(x => x.Enabled))
            {
                var full = Path.Combine(i.Folder(kind), item.File);
                if (!File.Exists(full)) continue;
                await using var s = File.OpenRead(full);
                var sha1 = Convert.ToHexString(await SHA1.HashDataAsync(s, ct)).ToLowerInvariant();
                s.Position = 0;
                var sha512 = Convert.ToHexString(await SHA512.HashDataAsync(s, ct)).ToLowerInvariant();
                local.Add((Path.GetRelativePath(root, full).Replace('\\', '/'), sha1, sha512, s.Length, item.Name));
            }
        var remote = await (lookup ?? Minecraft.McExport.LookupModrinth)(local.Select(l => l.Sha1).Distinct().ToList(), ct);
        var files = new List<ShareFile>();
        var left = new List<string>();
        foreach (var l in local)
        {
            if (remote.TryGetValue(l.Sha1, out var r)) files.Add(new ShareFile(l.Rel, l.Sha1, l.Sha512, r.Url, l.Size));
            else left.Add(l.Name);
        }
        return new SharePack("minecraft", "Minecraft", i.Name, [], i.GameVersion, i.Loader, i.LoaderVersion, files, null, left);
    }

    // ---------------------------------------------------------------- в JSON и обратно

    public static string ToJson(SharePack p)
    {
        var o = new JsonObject
        {
            ["format"] = Format, ["v"] = FormatVersion, ["app"] = Http.Version,
            ["game"] = p.Game, ["gameName"] = p.GameName, ["name"] = p.Name,
            ["mods"] = new JsonArray(p.Mods.Select(m =>
            {
                var j = new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["version"] = m.Version, ["source"] = m.Source, ["enabled"] = m.Enabled };
                if (m.FileId > 0) j["fileId"] = m.FileId;
                return (JsonNode)j;
            }).ToArray()),
        };
        if (p.IsMinecraft)
            o["mc"] = new JsonObject
            {
                ["gameVersion"] = p.McVersion, ["loader"] = p.McLoader ?? "vanilla", ["loaderVersion"] = p.McLoaderVersion ?? "",
                ["files"] = new JsonArray((p.Files ?? []).Select(f => (JsonNode)new JsonObject
                {
                    ["path"] = f.Path, ["sha1"] = f.Sha1, ["sha512"] = f.Sha512, ["url"] = f.Url, ["size"] = f.Size,
                }).ToArray()),
            };
        if (p.Config is { Length: > 0 } config) o["config"] = Convert.ToBase64String(config);
        if (p.Left is { Count: > 0 } left) o["left"] = new JsonArray(left.Take(50).Select(x => (JsonNode)x).ToArray());
        return o.ToJsonString();
    }

    static string Cut(string? s, int max) => s is null ? "" : s.Length > max ? s[..max] : s;

    /// <summary>Разобрать сборку. Всё, что пришло извне, проверяется: длины, пути файлов, ссылки только https.</summary>
    public static SharePack FromJson(string json)
    {
        JsonNode? o;
        try { o = JsonNode.Parse(json); }
        catch { throw new InvalidDataException(I18n.T("v24.share.bad")); }
        if (o.Str("format") != Format || o.Str("game") is not { Length: > 0 } game) throw new InvalidDataException(I18n.T("v24.share.bad"));
        if (o.Long("v") > FormatVersion) throw new InvalidDataException(I18n.T("err.PACK_NEWER"));
        var mods = o.Arr("mods").OfType<JsonObject>().Take(800)
            .Where(m => m.Str("id") is { Length: > 0 and <= 200 })
            .Select(m => new ShareMod(Cut(m.Str("id"), 200), Cut(m.Str("name") ?? m.Str("id"), 200), Cut(m.Str("version"), 60), Cut(m.Str("source") ?? "file", 20), m.Bool("enabled", true), Math.Max(0, m.Long("fileId"))))
            .ToList();
        string? mcVersion = null, loader = null, loaderVersion = null;
        List<ShareFile>? files = null;
        if (o?["mc"] is JsonObject mc)
        {
            mcVersion = Cut(mc.Str("gameVersion"), 40);
            loader = mc.Str("loader") is "fabric" or "quilt" or "forge" or "neoforge" ? mc.Str("loader") : "vanilla";
            loaderVersion = Cut(mc.Str("loaderVersion"), 60);
            files = mc.Arr("files").OfType<JsonObject>().Take(1500)
                .Select(f => new ShareFile(f.Str("path") ?? "", f.Str("sha1") ?? "", f.Str("sha512") ?? "", f.Str("url") ?? "", f.Long("size")))
                .Where(f => SafePath(f.Path) && SafeUrl(f.Url) && HexRe().IsMatch(f.Sha1) && f.Sha1.Length == 40 && HexRe().IsMatch(f.Sha512) && f.Sha512.Length == 128)
                .ToList();
        }
        byte[]? config = null;
        if (o.Str("config") is { Length: > 0 and <= MaxConfig * 2 } b64)
            try { config = Convert.FromBase64String(b64); } catch { }
        var left = o.Arr("left").Select(x => x?.ToString()).OfType<string>().Take(50).Select(x => Cut(x, 120)).ToList();
        return new SharePack(Cut(game, 40), Cut(o.Str("gameName") ?? game, 80), Cut(o.Str("name") is { Length: > 0 } n ? n : game, 80), mods, mcVersion, loader, loaderVersion, files, config, left);
    }

    [GeneratedRegex("^[0-9a-fA-F]+$")] private static partial Regex HexRe();

    /// <summary>
    /// Файл Minecraft из кода качаем только с Modrinth: свои коды ModLaunch всегда ссылаются на cdn.modrinth.com,
    /// а хеш в чужом коде ничего не доказывает — его пишет тот же, кто подставил бы ссылку на свой .jar.
    /// </summary>
    public static bool SafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Путь файла Minecraft — только внутрь сборки и только в папки содержимого.</summary>
    public static bool SafePath(string path) =>
        path.Length is > 0 and < 260 && !path.Contains("..") && !path.Contains(':') && !path.StartsWith('/') && !path.StartsWith('\\')
        && (path.StartsWith("mods/", StringComparison.Ordinal) || path.StartsWith("resourcepacks/", StringComparison.Ordinal) || path.StartsWith("shaderpacks/", StringComparison.Ordinal));

    // ---------------------------------------------------------------- длинный код (без сервера)

    static byte[] Deflate(string text)
    {
        using var buffer = new MemoryStream();
        using (var z = new DeflateStream(buffer, CompressionLevel.SmallestSize, true)) z.Write(Encoding.UTF8.GetBytes(text));
        return buffer.ToArray();
    }

    static string Inflate(byte[] data)
    {
        using var z = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
        using var limited = new MemoryStream();
        var chunk = new byte[16384];
        int n;
        while ((n = z.Read(chunk, 0, chunk.Length)) > 0)
        {
            limited.Write(chunk, 0, n);
            if (limited.Length > 8_000_000) throw new InvalidDataException(I18n.T("v24.share.bad"));
        }
        return Encoding.UTF8.GetString(limited.ToArray());
    }

    static string ToBase64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - s.Length % 4) % 4);
        return Convert.FromBase64String(s);
    }

    /// <summary>Код со сборкой внутри: без настроек — так он короче.</summary>
    public static string LongCode(SharePack p) => LongPrefix + ToBase64Url(Deflate(ToJson(p with { Config = null })));

    public static SharePack FromLongCode(string value)
    {
        try { return FromJson(Inflate(FromBase64Url(value))); }
        catch (InvalidDataException) { throw; }
        catch { throw new InvalidDataException(I18n.T("v24.share.bad")); }
    }

    // ---------------------------------------------------------------- сервер (Firestore packs/{код})

    static ServiceError Error(Firebase.Reply reply)
    {
        var reason = Firebase.Reason(reply);
        var status = (int)reply.Status;
        if (reason.Contains("ALREADY_EXISTS") || status == 409) return new ServiceError("EXISTS", reason);
        if (status == 404 || reason.Contains("NOT_FOUND")) return new ServiceError("NOT_FOUND", reason);
        if (status == 403 || reason.Contains("PERMISSION_DENIED")) return new ServiceError("DENIED", reason);
        if (status is 429 or 503) return new ServiceError("BUSY", reason);
        return new ServiceError("SERVER", reason);
    }

    /// <summary>Выложить сборку и получить короткий код. Не вышло — исключение (тогда показываем длинный код).</summary>
    public static async Task<string> Upload(SharePack pack, CancellationToken ct = default)
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        var data = Convert.ToBase64String(Deflate(ToJson(pack)));
        // Слишком большие настройки — без них (моды важнее).
        if (data.Length > MaxData && pack.Config is not null) data = Convert.ToBase64String(Deflate(ToJson(pack with { Config = null })));
        if (data.Length > MaxData) throw new InvalidOperationException(I18n.T("v24.share.tooBig"));
        var (uid, token) = await Account.Token();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = new string(RandomNumberGenerator.GetBytes(8).Select(b => Alphabet[b % Alphabet.Length]).ToArray());
            var write = new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"packs/{code}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                    {
                        ["uid"] = uid, ["game"] = Cut(pack.Game, 40), ["name"] = Cut(pack.Name, 80), ["data"] = data, ["app"] = Cut(Http.Version, 20),
                    }),
                },
                ["updateTransforms"] = new JsonArray(new JsonObject { ["fieldPath"] = "created", ["setToServerValue"] = "REQUEST_TIME" }),
                ["currentDocument"] = new JsonObject { ["exists"] = false },
            };
            var reply = await Firebase.Send(Firebase.Url(":commit"), HttpMethod.Post, new JsonObject { ["writes"] = new JsonArray(write) }, token, ct: ct);
            if ((int)reply.Status is >= 200 and < 300) return code;
            var error = Error(reply);
            if (error.Code is "EXISTS" || Firebase.Reason(reply).Contains("FAILED_PRECONDITION")) continue;
            throw error;
        }
        throw new ServiceError("SERVER", "code");
    }

    /// <summary>Сборка по короткому коду. Читать может кто угодно — даже без аккаунта.</summary>
    public static async Task<SharePack> Fetch(string code, CancellationToken ct = default)
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        var reply = await Firebase.Send(Firebase.Url($"/packs/{code}"), HttpMethod.Get, ct: ct);
        if ((int)reply.Status == 403)
        {
            // Правила просят вход — пробуем с анонимным входом программы.
            var (_, token) = await Account.Token();
            reply = await Firebase.Send(Firebase.Url($"/packs/{code}"), HttpMethod.Get, token: token, ct: ct);
        }
        if ((int)reply.Status == 404) throw new InvalidDataException(I18n.T("code.notFound"));
        if ((int)reply.Status is < 200 or >= 300) throw Error(reply);
        var fields = Firebase.FromFields(reply.Body?["fields"] as JsonObject);
        var data = fields.S("data");
        if (data == "") throw new InvalidDataException(I18n.T("v24.share.bad"));
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); }
        catch { throw new InvalidDataException(I18n.T("v24.share.bad")); }
        return FromJson(Inflate(bytes));
    }

    // ---------------------------------------------------------------- Minecraft: сборка из кода

    /// <summary>Временный .mrpack из кода — ставится так же, как модпак Modrinth (с проверкой SHA-1/SHA-512).</summary>
    public static string WriteMrpack(SharePack p)
    {
        var path = Path.Combine(Paths.DataDir, "cache", $"share-{Guid.NewGuid():N}.mrpack");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deps = new JsonObject { ["minecraft"] = p.McVersion };
        var key = p.McLoader switch { "fabric" => "fabric-loader", "quilt" => "quilt-loader", "forge" => "forge", "neoforge" => "neoforge", _ => null };
        if (key is not null && !string.IsNullOrEmpty(p.McLoaderVersion)) deps[key] = p.McLoaderVersion;
        var index = new JsonObject
        {
            ["formatVersion"] = 1, ["game"] = "minecraft", ["versionId"] = "1", ["name"] = p.Name,
            ["files"] = new JsonArray((p.Files ?? []).Select(f => (JsonNode)new JsonObject
            {
                ["path"] = f.Path,
                ["hashes"] = new JsonObject { ["sha1"] = f.Sha1, ["sha512"] = f.Sha512 },
                ["env"] = new JsonObject { ["client"] = "required", ["server"] = f.Path.StartsWith("mods/", StringComparison.Ordinal) ? "required" : "unsupported" },
                ["downloads"] = new JsonArray(f.Url),
                ["fileSize"] = f.Size,
            }).ToArray()),
            ["dependencies"] = deps,
        };
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open(), new UTF8Encoding(false)))
            w.Write(index.ToJsonString());
        return path;
    }

    // ---------------------------------------------------------------- самотесты

    [SelfTest]
    static string ParsesCodes()
    {
        var fail = "";
        if (Parse("abcd-efgh") is not { Kind: "short", Value: "ABCDEFGH" }) fail += "short; ";
        if (Parse("  K7QM 9XPA ") is not { Kind: "short", Value: "K7QM9XPA" }) fail += "spaces; ";
        if (Parse("modlaunch://pack/K7QM-9XPA") is not { Kind: "short", Value: "K7QM9XPA" }) fail += "app link; ";
        if (Parse("https://modlaunchapp.com/pack.html#pack=K7QM-9XPA") is not { Kind: "short" }) fail += "site link; ";
        if (Parse("018f2c3a-6b1e-4f1b-9c55-2d4e8a1f3b77") is not { Kind: "r2" }) fail += "r2modman; ";
        if (Parse("ML1-abc_-") is not { Kind: "long", Value: "abc_-" }) fail += "long; ";
        if (Parse("IOIO-0101") is not null || Parse("hello world") is not null || Parse("") is not null) fail += "garbage accepted; ";
        if (LooksLikeCode("STARDEWS") || !LooksLikeCode("K7QM-9XPA") || !LooksLikeCode("modlaunch://pack/K7QM9XPA")) fail += "search detection; ";
        if (LooksLikeCode("wall-jump") || LooksLikeCode("mega-pack") || LooksLikeCode("better crafting")) fail += "search words taken as codes; ";
        // Приглашение целиком (так его копирует «Скопировать приглашение») — на всех языках.
        foreach (var lang in new[] { "ru", "en", "uk", "es", "de", "fr", "pt-BR", "zh-CN" })
        {
            if (I18n.Raw(lang, "v24.share.invite") is not { } invite) continue;
            var filled = invite.Replace("{game}", "Stardew Valley").Replace("{site}", "https://modlaunchapp.com").Replace("{code}", "K7QM-9XPA");
            if (Parse(filled) is not { Kind: "short", Value: "K7QM9XPA" }) fail += $"invite {lang}; ";
            if (!LooksLikeCode(filled)) fail += $"invite in search {lang}; ";
            var longInvite = invite.Replace("{game}", "Minecraft").Replace("{site}", "https://modlaunchapp.com").Replace("{code}", "ML1-abc_DEF-123\nxyz");
            if (Parse(longInvite) is not { Kind: "long", Value: "abc_DEF-123xyz" }) fail += $"long invite {lang}; ";
        }
        if (fail != "") throw new Exception(fail);
        return "ABCD-EFGH, spaces, app and site links, r2modman UUID, ML1- long code; search words are not codes";
    }

    [SelfTest]
    static string PackRoundTrip()
    {
        var pack = new SharePack("stardew-valley", "Stardew Valley", "Ферма с друзьями",
            [new("nexus:stardewvalley:1915", "Content Patcher", "2.4.4", "nexus", true, 128345), new("nexus:stardewvalley:3753", "Ridgeside Village", "2.5.17", "nexus", false)],
            Config: [1, 2, 3], Left: ["My Local Mod"]);
        var back = FromJson(ToJson(pack));
        if (back.Name != pack.Name || !back.Mods.SequenceEqual(pack.Mods) || back.Config?.SequenceEqual(pack.Config!) != true || back.Left?.SingleOrDefault() != "My Local Mod")
            throw new Exception("json round trip lost data");
        var viaLong = FromLongCode(Parse(LongCode(pack))!.Value);
        if (!viaLong.Mods.SequenceEqual(pack.Mods) || viaLong.Config is not null) throw new Exception("long code round trip");
        var mc = new SharePack("minecraft", "Minecraft", "Survival", [], "1.21.1", "fabric", "0.16.5",
        [
            new("mods/sodium.jar", new string('a', 40), new string('b', 128), "https://cdn.modrinth.com/data/AANobbMI/versions/x/sodium.jar", 900_000),
            new("../evil.jar", new string('a', 40), "", "https://x/evil.jar", 1),
            new("mods/plain-http.jar", new string('a', 40), "", "http://x/a.jar", 1),
            new("mods/sodium-fake.jar", new string('a', 40), new string('b', 128), "https://evil.example/sodium.jar", 1),
            new("config/../../x.jar", new string('a', 40), "", "https://x/a.jar", 1),
        ]);
        var mcBack = FromJson(ToJson(mc));
        if (!mcBack.IsMinecraft || mcBack.Files!.Count != 1 || mcBack.McLoader != "fabric" || mcBack.McLoaderVersion != "0.16.5")
            throw new Exception("minecraft pack: unsafe files kept or loader lost: " + string.Join(", ", mcBack.Files!.Select(f => f.Path)));
        var mrpack = WriteMrpack(mcBack);
        try
        {
            var index = Minecraft.McModrinth.ReadIndex(mrpack);
            if (index.GameVersion != "1.21.1" || index.Loader != "fabric" || index.LoaderVersion != "0.16.5" || index.Files.Count != 1 || index.Files[0].Sha512 != new string('b', 128))
                throw new Exception("mrpack from code is wrong");
        }
        finally { File.Delete(mrpack); }
        try { FromJson("{\"format\":\"other\"}"); throw new Exception("foreign json accepted"); } catch (InvalidDataException) { }
        return $"pack, long code ({LongCode(pack).Length} chars) and Minecraft .mrpack round trip; ../, http:// and non-Modrinth files dropped";
    }

    /// <summary>Настройки из чужого кода: только файлы настроек — не .dll и не новые файлы рядом с модом.</summary>
    [SelfTest]
    static string ConfigFromCodeIsOnlyConfig()
    {
        string[] ok = ["Mods/ContentPatcher/config.json", "config/BepInEx.cfg", "config/sub/MoreCompany.json", "settings/Benchwarp.GlobalSettings.json"];
        string[] bad = ["Mods/ContentPatcher/ContentPatcher.dll", "Mods/ContentPatcher/x/manifest.json", "config/evil.dll", "config/run.exe", "settings/evil.dll"];
        var wrong = ok.Where(e => !ModSetup.IsConfigFile(e)).Concat(bad.Where(ModSetup.IsConfigFile)).ToList();
        if (wrong.Count > 0) throw new Exception("misjudged: " + string.Join(", ", wrong));
        if (!SafeUrl("https://cdn.modrinth.com/data/AANobbMI/versions/x/sodium.jar") || SafeUrl("https://evil.example/x.jar") || SafeUrl("http://cdn.modrinth.com/x.jar"))
            throw new Exception("url filter wrong");
        return "config.json / .cfg / GlobalSettings pass, .dll / .exe / new manifest are dropped; only https://cdn.modrinth.com files";
    }

    [SelfTest]
    static string PicksModsForCode()
    {
        var dir = Path.Combine(Paths.DataDir, "share-test", "Hollow Knight");
        var game = GameCatalog.ById("hollow-knight")!;
        // Папки модов должны быть на месте: сверка (Reconcile) помечает пропавшие, а их в код не берём.
        foreach (var folder in new[] { "Benchwarp", "HKMP", "MyMod" }) Directory.CreateDirectory(Path.Combine(game.ModsDir(dir), folder));
        var registry = new ModRegistry(game, dir);
        registry.Add(new JsonObject { ["id"] = "Benchwarp", ["name"] = "Benchwarp", ["folder"] = "Benchwarp", ["source"] = "modlinks", ["version"] = "3.2.1.0" });
        registry.Add(new JsonObject { ["id"] = "HKMP", ["name"] = "HKMP", ["folder"] = "HKMP", ["source"] = "modlinks", ["version"] = "2.4.3.0" });
        // Выключить по-настоящему: у Hollow Knight выключенный мод переезжает в Mods/Disabled.
        registry.SetEnabled("HKMP", false);
        registry.Add(new JsonObject { ["id"] = "local:MyMod", ["name"] = "My Mod", ["folder"] = "MyMod", ["source"] = "file" });
        var pack = FromRegistry(game, registry, "Coop", withConfig: false);
        // Список модов игры общий с другими проверками — смотрим только на свои записи.
        if (!pack.Mods.Any(m => m.Id == "Benchwarp" && m.Version == "3.2.1.0" && m.Enabled) || pack.Mods.Any(m => m.Id == "local:MyMod")
            || pack.Left?.Contains("My Mod") != true || pack.Mods.SingleOrDefault(m => m.Id == "HKMP")?.Enabled != false)
            throw new Exception("wrong mods: " + string.Join(", ", pack.Mods.Select(m => m.Id)) + " | left " + string.Join(", ", pack.Left ?? []));
        return "Benchwarp and HKMP (off) go into the code, a mod from a file is listed as left out";
    }
}
