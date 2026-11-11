using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;
using ModLaunch.Social;

namespace ModLaunch.Creator;

/// <summary>Мод в ModLaunch Hub.</summary>
public sealed record HubMod(
    string Id, string Uid, string Author, string Name, string Summary, string Description, string Game, string Version,
    string Kind, string Code, List<string> Tags, List<string> Images, long Size, string Sha256, int Chunks, string FileName,
    string Changelog, long Likes, long Downloads, long Comments, DateTime? Created, DateTime? Updated)
{
    public bool Mine => Account.SignedIn && Account.Uid == Uid;
    /// <summary>script — исходник ModScript (собирается у каждого), package — готовый архив.</summary>
    public bool IsPackage => Kind == "package";
}

public sealed record HubVersion(string Version, string Changelog, long Size, string Sha256, int Chunks, DateTime? Created);
public sealed record HubComment(string Id, string Uid, string Author, string Text, DateTime? Created)
{
    public bool Mine => Account.Uid == Uid;
}

/// <summary>Что публикуем: скрипт, собранный пакет или любой готовый архив мода.</summary>
public sealed class HubDraft
{
    public string Name = "";
    public string Summary = "";
    public string Description = "";
    public string Game = "";
    public string Version = "1.0.0";
    public string Changelog = "";
    public string Code = "";
    public List<string> Tags = [];
    public List<string> Images = [];
    /// <summary>Архив для публикации (null — только скрипт).</summary>
    public string? Archive;
    /// <summary>
    /// 26.0: id карточки, если мод уже выложен (новая версия из «Опубликованных», проект с hubId). Пусто — id по имени.
    /// После публикации здесь id, под которым карточка записана.
    /// </summary>
    public string? Id;
}

/// <summary>
/// ModLaunch Hub — свой сайт модов внутри программы, без посредников. Всё
/// хранится в Firestore проекта ModLaunch: карточки модов (creations), история
/// версий, лайки, комментарии, жалобы и сами файлы — кусками по ~650 КБ
/// (blobs), с проверкой SHA-256 при скачивании. Правила — firebase/firestore.rules.
/// </summary>
public static partial class Hub
{
    public const int MaxCode = 20000;
    public const long MaxFile = 8 * 1024 * 1024;
    const int ChunkChars = 900_000;
    const int KeepVersions = 3;

    public static readonly string[] Tags =
        ["qol", "gameplay", "content", "items", "visuals", "audio", "ui", "cosmetics", "multiplayer", "tweaks", "library", "modpack", "cheats", "fixes"];

    // ---------------------------------------------------------------- сеть

    internal static async Task<JsonNode?> Call(string url, HttpMethod method, JsonNode? body, string? token)
    {
        if (!Firebase.Configured) throw new ServiceError("NOT_CONFIGURED");
        var reply = await Firebase.Send(url, method, body, token);
        if ((int)reply.Status is >= 200 and < 300) return reply.Body;
        var reason = Firebase.Reason(reply);
        var status = (int)reply.Status;
        if (status is 429 or 503 || Regex.IsMatch(reason, "RESOURCE_EXHAUSTED|quota", RegexOptions.IgnoreCase)) throw new ServiceError("BUSY", reason);
        if (status == 404) throw new ServiceError("NOT_FOUND", reason);
        // 9.5: кошелёк изменился с момента чтения (условие updateTime) — повторим с новым состоянием.
        if (reason.Contains("FAILED_PRECONDITION") || reason.Contains("ABORTED")) throw new ServiceError("PRECONDITION", reason);
        if (reason.Contains("ALREADY_EXISTS") || status == 409) throw new ServiceError("EXISTS", reason);
        if (status == 403 || reason.Contains("PERMISSION_DENIED")) throw new ServiceError("DENIED", reason);
        throw new ServiceError("SERVER", reason);
    }

    internal static Task<JsonNode?> Commit(JsonArray writes, string token) =>
        Call(Firebase.Url(":commit"), HttpMethod.Post, new JsonObject { ["writes"] = writes }, token);

    internal static async Task<(string Uid, string Token, string Name)> Member()
    {
        if (!Account.SignedIn) throw new ServiceError("SIGN_IN");
        var (uid, token) = await Account.Token();
        var name = Account.CleanName(Account.Get().Name) is { Length: > 0 } n ? n : "ModLaunch";
        return (uid, token, name);
    }

    /// <summary>Токен для счётчика загрузок: вошли — свой, нет — анонимный.</summary>
    static async Task<string?> AnyToken()
    {
        try { return (await Account.Token()).IdToken; } catch { return null; }
    }

    internal static JsonObject Increment(string doc, string field, int by) => new()
    {
        ["transform"] = new JsonObject
        {
            ["document"] = Firebase.Doc(doc),
            ["fieldTransforms"] = new JsonArray(new JsonObject { ["fieldPath"] = field, ["increment"] = new JsonObject { ["integerValue"] = by.ToString() } }),
        },
    };

    internal static JsonObject ServerTime(string field) => new() { ["fieldPath"] = field, ["setToServerValue"] = "REQUEST_TIME" };

    // ---------------------------------------------------------------- чтение

    static HubMod FromDoc(JsonObject doc)
    {
        var f = Firebase.FromFields(doc["fields"] as JsonObject);
        var name = doc.Str("name") ?? "";
        return new HubMod(
            name[(name.LastIndexOf('/') + 1)..], f.S("uid"), f.S("author"), f.S("name"), f.S("summary") is { Length: > 0 } s ? s : f.S("about"),
            f.S("description"), f.S("game"), f.S("version"), f.S("kind") is { Length: > 0 } k ? k : "script", f.S("code"), f.A("tags"), f.A("images").Where(u => Ads.HttpsUrl(u) is not null).Take(6).ToList(),
            f.L("size"), f.S("sha256"), (int)f.L("chunks"), f.S("fileName"), f.S("changelog"),
            f.L("likes"), f.L("downloads"), f.L("comments"), Firebase.Time(f.S("created")), Firebase.Time(f.S("updated")));
    }

    static List<HubMod>? _cache;
    static DateTime _cachedAt;
    public static string? LastError { get; private set; }

    /// <summary>Все моды Hub (до 300 свежих). Кэш на минуту; сортировка и фильтры — на месте.</summary>
    public static async Task<List<HubMod>> All(bool force = false, CancellationToken ct = default)
    {
        if (!force && _cache is not null && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(1)) return _cache;
        var query = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "creations" }),
            ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "updated" }, ["direction"] = "DESCENDING" }),
            ["limit"] = 300,
        };
        try
        {
            var rows = await Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = query }, null);
            _cache = (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(FromDoc).ToList();
            _cachedAt = DateTime.UtcNow;
            LastError = null;
            CheckFollowed(_cache);
            return _cache;
        }
        catch (Exception e) { LastError = Firebase.Explain("creator", e); throw; }
    }

    public static void Invalidate() => _cache = null;

    public static IEnumerable<HubMod> Sort(IEnumerable<HubMod> list, string sort) => sort switch
    {
        "downloads" => list.OrderByDescending(m => m.Downloads),
        "likes" => list.OrderByDescending(m => m.Likes).ThenByDescending(m => m.Downloads),
        "new" => list.OrderByDescending(m => m.Created),
        // «В тренде»: лайки и загрузки с поправкой на возраст, как «Hot» на сайтах модов.
        "trending" => list.OrderByDescending(Trend),
        _ => list.OrderByDescending(m => m.Updated),
    };

    public static double Trend(HubMod m)
    {
        var hours = Math.Max(2, (DateTime.UtcNow - (m.Updated ?? m.Created ?? DateTime.UtcNow)).TotalHours);
        return (m.Likes * 3 + m.Downloads + m.Comments * 2 + 1) / Math.Pow(hours, 1.3);
    }

    public static async Task<HubMod?> Get(string id, CancellationToken ct = default)
    {
        try { return FromDoc((JsonObject)(await Call(Firebase.Url($"/creations/{id}"), HttpMethod.Get, null, null))!); }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { return null; }
    }

    public static async Task<List<HubVersion>> Versions(string id)
    {
        var data = await Call(Firebase.Url($"/creations/{id}/versions") + "&pageSize=50", HttpMethod.Get, null, null);
        return data.Arr("documents").OfType<JsonObject>().Select(d =>
        {
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            return new HubVersion(f.S("version"), f.S("changelog"), f.L("size"), f.S("sha256"), (int)f.L("chunks"), Firebase.Time(f.S("created")));
        }).OrderByDescending(v => v.Created).ToList();
    }

    public static async Task<List<HubComment>> Comments(string id)
    {
        var data = await Call(Firebase.Url($"/creations/{id}/comments") + "&pageSize=200&orderBy=created%20desc", HttpMethod.Get, null, null);
        return data.Arr("documents").OfType<JsonObject>().Select(d =>
        {
            var f = Firebase.FromFields(d["fields"] as JsonObject);
            var name = d.Str("name") ?? "";
            return new HubComment(name[(name.LastIndexOf('/') + 1)..], f.S("uid"), f.S("author"), f.S("text"), Firebase.Time(f.S("created")));
        }).ToList();
    }

    // ---------------------------------------------------------------- публикация

    /// <summary>
    /// id карточки по автору и имени. 26.0: раньше от имени оставался только транслит — китайские, японские,
    /// корейские имена и эмодзи стирались до «mymod», и разные моды одного автора делили один документ.
    /// Теперь, если от имени что-то теряется, к id добавляется устойчивый хвост из хэша полного имени
    /// (латиница и русский дают тот же id, что и раньше; é, ü, ñ — без значков, «cafe»).
    /// </summary>
    public static string DocId(string uid, string name) => DocId(uid, name, false);

    static string DocId(string uid, string name, bool hashed)
    {
        var (slug, lossy) = Slug(name);
        if (lossy || hashed)
        {
            var full = name.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..8].ToLowerInvariant();
            // Правила: после «uid_» — до 60 знаков [a-z0-9_]; хвост «_» + 8 — значит, начало не длиннее 51.
            slug = (slug == "" ? "mod" : slug.Length > 51 ? slug[..51].TrimEnd('_') : slug) + "_" + hash;
        }
        var id = $"{uid.ToLowerInvariant()}_{slug.ToLowerInvariant()}";
        return id.Length > 120 ? id[..120] : id;
    }

    /// <summary>id до 26.0 (только транслит, пусто — «mymod»): под ним лежат моды, выложенные раньше.</summary>
    public static string LegacyDocId(string uid, string name)
    {
        var id = $"{uid.ToLowerInvariant()}_{Projects.PackageName(name).ToLowerInvariant()}";
        return id.Length > 120 ? id[..120] : id;
    }

    /// <summary>Имя латиницей для id и потеряно ли что-то из имени (иероглифы, эмодзи, ß…).</summary>
    static (string Slug, bool Lossy) Slug(string name)
    {
        // Значки над латиницей (é, ü, ñ) снимаем; кириллицу не трогаем — «й» и «ё» переводит Translit.
        var plain = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (c is >= 'À' and < 'ɐ')
            {
                var bare = c.ToString().Normalize(NormalizationForm.FormD).Where(x => x < 128).ToArray();
                if (bare.Length > 0) { plain.Append(bare); continue; }
            }
            plain.Append(c);
        }
        var t = CustomGames.Translit(plain.ToString());
        // «№» в русских именах был всегда и id не ломал; остальные знаки-символы (★, ♥) различают моды.
        var lossy = t.Any(c => c >= 128 && c != '№' && (char.IsLetterOrDigit(c) || char.IsSurrogate(c) || char.GetUnicodeCategory(c) == UnicodeCategory.OtherSymbol));
        var s = Regex.Replace(t, @"[^A-Za-z0-9_]+", "_").Trim('_');
        if (s.Length > 60) s = s[..60];
        return (s, lossy || s == "");
    }

    /// <summary>
    /// 26.0: куда писать карточку. Явный id (новая версия выложенного мода) — туда; иначе id по имени. Мод, выложенный
    /// до 26.0 под старым id с тем же именем, остаётся на своём месте — новая версия не плодит второй карточки.
    /// </summary>
    static async Task<(string Id, HubMod? Existing)> Target(string uid, HubDraft d, CancellationToken ct)
    {
        var prefix = uid.ToLowerInvariant() + "_";
        if (d.Id is { Length: > 0 } own && own.StartsWith(prefix, StringComparison.Ordinal) && await Get(own, ct) is { } mine && mine.Uid == uid)
            return (own, mine);
        var id = DocId(uid, d.Name);
        var legacy = LegacyDocId(uid, d.Name);
        if (legacy != id && await Get(legacy, ct) is { } old && old.Uid == uid && SameMod(old.Name, d.Name))
            return (legacy, old);
        var existing = await Get(id, ct);
        // Под этим id — другой мод (старое имя из иероглифов стёрлось до того же): свою карточку — с хвостом из хэша.
        if (existing is not null && !SameMod(existing.Name, d.Name))
        {
            id = DocId(uid, d.Name, hashed: true);
            existing = await Get(id, ct);
            if (existing is not null && !SameMod(existing.Name, d.Name)) throw new ServiceError("NAME_TAKEN", existing.Name);
        }
        return (id, existing);
    }

    /// <summary>
    /// 26.0: своя карточка в Hub для проекта (по hubId из project.json, иначе по имени) — чтобы новый выпуск не стёр
    /// описание, теги и картинки. Не вошли, нет сети, карточки нет — null.
    /// </summary>
    public static async Task<HubMod?> FindMine(string? hubId, string name, CancellationToken ct = default)
    {
        if (!Account.SignedIn || Account.Uid is not { Length: > 0 } uid || Clip(name, 60) == "") return null;
        try { return (await Target(uid, new HubDraft { Id = hubId, Name = name }, ct)).Existing; }
        catch { return null; }
    }

    /// <summary>Тот же мод: имя совпадает без учёта регистра, или оба имени целиком латиницей/кириллицей (тогда id = имя).</summary>
    internal static bool SameMod(string a, string b) =>
        string.Equals(Clip(a, 60), Clip(b, 60), StringComparison.OrdinalIgnoreCase) || (!Slug(a).Lossy && !Slug(b).Lossy && Slug(a).Slug.Equals(Slug(b).Slug, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- версии

    /// <summary>26.0: сравнить версии x.y.z по числам (1.10.0 новее 1.9.9). Не версия — считается 0.0.0.</summary>
    public static int CompareVersions(string a, string b)
    {
        var x = VersionParts(a);
        var y = VersionParts(b);
        for (var i = 0; i < 3; i++)
            if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return 0;
    }

    static long[] VersionParts(string v)
    {
        var parts = (v ?? "").Trim().TrimStart('v', 'V').Split('.');
        var r = new long[3];
        for (var i = 0; i < 3 && i < parts.Length; i++)
            r[i] = long.TryParse(Regex.Match(parts[i], @"^\d+").Value, out var n) ? n : 0;
        return r;
    }

    /// <summary>Следующая версия после данной: +1 к последнему числу (1.2.3 → 1.2.4).</summary>
    public static string NextVersion(string v)
    {
        var p = VersionParts(v);
        return $"{p[0]}.{p[1]}.{p[2] + 1}";
    }

    static string BlobId(string docId, string version, int n) => $"{docId}__{version}__{n}";

    public static string Clip(string? s, int max, bool multiline = false)
    {
        s = (s ?? "").Replace("\r\n", "\n");
        s = Regex.Replace(s, multiline ? @"[\u0000-\u0009\u000b-\u001f]" : @"[\u0000-\u001f]", " ").Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>Выложить мод или новую версию. Файл (если есть) режется на куски и пишется отдельными запросами.</summary>
    public static async Task<string> Publish(HubDraft d, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var (uid, token, author) = await Member();
        if (Clip(d.Name, 60) == "") throw new ServiceError("INVALID", "name");
        if (!Regex.IsMatch(d.Version, @"^\d+\.\d+\.\d+$")) throw new ServiceError("INVALID", "version");
        if (d.Code.Length > MaxCode) throw new ServiceError("TOO_BIG");
        var (id, existing) = await Target(uid, d, ct);
        // 26.0: версия только растёт — иначе у подписчиков «обновление» на старую. В сообщении — текущая версия.
        if (existing is not null && CompareVersions(d.Version, existing.Version) <= 0) throw new ServiceError("OLD_VERSION", existing.Version);

        long size = 0;
        string sha = "", fileName = "";
        var chunks = 0;
        if (d.Archive is not null)
        {
            var bytes = await File.ReadAllBytesAsync(d.Archive, ct);
            if (bytes.LongLength > MaxFile) throw new ServiceError("FILE_TOO_BIG");
            size = bytes.LongLength;
            sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            fileName = Clip(Path.GetFileName(d.Archive), 120);
            var text = Convert.ToBase64String(bytes);
            chunks = (text.Length + ChunkChars - 1) / ChunkChars;
            for (var n = 0; n < chunks; n++)
            {
                ct.ThrowIfCancellationRequested();
                var part = text.Substring(n * ChunkChars, Math.Min(ChunkChars, text.Length - n * ChunkChars));
                await Commit(new JsonArray(new JsonObject
                {
                    ["update"] = new JsonObject
                    {
                        ["name"] = Firebase.Doc($"blobs/{BlobId(id, d.Version, n)}"),
                        ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["uid"] = uid, ["n"] = n, ["data"] = part }),
                    },
                }), token);
                progress?.Report((n + 1.0) / (chunks + 1));
            }
        }

        var fields = new Dictionary<string, object?>
        {
            ["uid"] = uid,
            ["author"] = author,
            ["name"] = Clip(d.Name, 60),
            ["summary"] = Clip(d.Summary, 200),
            ["description"] = Clip(d.Description, 5000, multiline: true),
            ["game"] = Clip(d.Game, 40),
            ["version"] = d.Version,
            ["kind"] = d.Archive is null ? "script" : "package",
            ["code"] = d.Code,
            ["tags"] = d.Tags.Where(Tags.Contains).Distinct().Take(6).ToList(),
            ["images"] = d.Images.Where(u => u.StartsWith("https://")).Select(u => Clip(u, 400)).Take(6).ToList(),
            ["size"] = size,
            ["sha256"] = sha,
            ["chunks"] = chunks,
            ["fileName"] = fileName,
            ["changelog"] = Clip(d.Changelog, 2000, multiline: true),
            ["app"] = Http.Version,
        };
        var writes = new JsonArray();
        var main = new JsonObject { ["update"] = new JsonObject { ["name"] = Firebase.Doc($"creations/{id}"), ["fields"] = Firebase.ToFields(fields) } };
        var times = new JsonArray(ServerTime("updated"));
        if (existing is null)
        {
            // Новый мод: счётчики с нуля.
            ((JsonObject)main["update"]!["fields"]!)["likes"] = Firebase.ToValue(0L);
            ((JsonObject)main["update"]!["fields"]!)["downloads"] = Firebase.ToValue(0L);
            ((JsonObject)main["update"]!["fields"]!)["comments"] = Firebase.ToValue(0L);
            times.Add(ServerTime("created"));
            main["currentDocument"] = new JsonObject { ["exists"] = false };
        }
        else
        {
            main["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) };
            main["currentDocument"] = new JsonObject { ["exists"] = true };
        }
        main["updateTransforms"] = times;
        writes.Add(main);
        writes.Add(new JsonObject
        {
            ["update"] = new JsonObject
            {
                ["name"] = Firebase.Doc($"creations/{id}/versions/{d.Version}"),
                ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                {
                    ["version"] = d.Version, ["changelog"] = fields["changelog"], ["size"] = size, ["sha256"] = sha, ["chunks"] = chunks,
                }),
            },
            ["updateTransforms"] = new JsonArray(ServerTime("created")),
        });
        await Commit(writes, token);
        progress?.Report(1);

        // Старые файлы: храним последние версии, остальные убираем (место в базе не бесконечно).
        try
        {
            var old = (await Versions(id)).Skip(KeepVersions).Where(v => v.Chunks > 0).ToList();
            foreach (var v in old)
            {
                var deletes = new JsonArray();
                for (var n = 0; n < v.Chunks; n++) deletes.Add(new JsonObject { ["delete"] = Firebase.Doc($"blobs/{BlobId(id, v.Version, n)}") });
                deletes.Add(new JsonObject { ["delete"] = Firebase.Doc($"creations/{id}/versions/{v.Version}") });
                await Commit(deletes, token);
            }
        }
        catch { }
        Invalidate();
        Follow(id, d.Version);
        d.Id = id;
        return id;
    }

    public static async Task Delete(HubMod mod)
    {
        var (_, token, _) = await Member();
        var writes = new JsonArray();
        foreach (var v in await Versions(mod.Id))
        {
            for (var n = 0; n < v.Chunks; n++) writes.Add(new JsonObject { ["delete"] = Firebase.Doc($"blobs/{BlobId(mod.Id, v.Version, n)}") });
            writes.Add(new JsonObject { ["delete"] = Firebase.Doc($"creations/{mod.Id}/versions/{v.Version}") });
        }
        writes.Add(new JsonObject { ["delete"] = Firebase.Doc($"creations/{mod.Id}") });
        await Commit(writes, token);
        Invalidate();
    }

    // ---------------------------------------------------------------- скачивание

    /// <summary>Скачать архив мода (последняя или указанная версия), собрать из кусков и проверить SHA-256.</summary>
    public static async Task<string> Download(HubMod mod, HubVersion? version = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var ver = version?.Version ?? mod.Version;
        var chunks = version?.Chunks ?? mod.Chunks;
        var sha = version?.Sha256 ?? mod.Sha256;
        if (chunks <= 0) throw new ServiceError("NO_FILE");
        using var ms = new MemoryStream();
        for (var n = 0; n < chunks; n++)
        {
            var doc = await Call(Firebase.Url($"/blobs/{BlobId(mod.Id, ver, n)}"), HttpMethod.Get, null, null);
            var data = Firebase.FromFields(doc?["fields"] as JsonObject).S("data");
            var bytes = Convert.FromBase64String(data);
            await ms.WriteAsync(bytes, ct);
            progress?.Report((n + 1.0) / chunks);
        }
        var got = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        if (sha != "" && got != sha) throw new ServiceError("BAD_HASH");
        var dir = Directory.CreateDirectory(Path.Combine(Paths.DownloadsTemp, "hub")).FullName;
        var file = Path.Combine(dir, Regex.Replace(mod.FileName is { Length: > 0 } f ? f : $"{Projects.PackageName(mod.Name)}-{ver}.zip", @"[^\w.\-]+", "_").TrimStart('.'));
        await File.WriteAllBytesAsync(file, ms.ToArray(), ct);
        _ = CountDownload(mod.Id);
        return file;
    }

    public static async Task CountDownload(string id)
    {
        if (await AnyToken() is not string token) return;
        try { await Commit(new JsonArray(Increment($"creations/{id}", "downloads", 1)), token); } catch { }
    }

    // ---------------------------------------------------------------- лайки, комментарии, жалобы

    static JsonObject Local => Settings.Data.Obj("hub");

    public static bool Liked(string id) => Local.Obj("likes").Bool(id);

    public static async Task<bool> ToggleLike(HubMod mod)
    {
        var (uid, token, _) = await Member();
        var liked = Liked(mod.Id);
        var likeDoc = Firebase.Doc($"creations/{mod.Id}/likes/{uid}");
        var writes = new JsonArray(
            liked
                ? new JsonObject { ["delete"] = likeDoc, ["currentDocument"] = new JsonObject { ["exists"] = true } }
                : new JsonObject
                {
                    ["update"] = new JsonObject { ["name"] = likeDoc, ["fields"] = new JsonObject() },
                    ["currentDocument"] = new JsonObject { ["exists"] = false },
                    ["updateTransforms"] = new JsonArray(ServerTime("created")),
                },
            Increment($"creations/{mod.Id}", "likes", liked ? -1 : 1));
        try { await Commit(writes, token); }
        catch (ServiceError e) when (e.Code is "EXISTS" or "NOT_FOUND" or "PRECONDITION") { }
        if (liked) Local.Obj("likes").Remove(mod.Id); else Local.Obj("likes")[mod.Id] = true;
        Settings.Save();
        Invalidate();
        return !liked;
    }

    public static async Task AddComment(HubMod mod, string text)
    {
        var (uid, token, author) = await Member();
        text = Clip(text, 1000, multiline: true);
        if (text == "") return;
        var cid = Guid.NewGuid().ToString("N")[..20];
        await Commit(new JsonArray(
            new JsonObject
            {
                ["update"] = new JsonObject
                {
                    ["name"] = Firebase.Doc($"creations/{mod.Id}/comments/{cid}"),
                    ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["uid"] = uid, ["author"] = author, ["text"] = text }),
                },
                ["currentDocument"] = new JsonObject { ["exists"] = false },
                ["updateTransforms"] = new JsonArray(ServerTime("created")),
            },
            Increment($"creations/{mod.Id}", "comments", 1)), token);
        Invalidate();
    }

    public static async Task DeleteComment(HubMod mod, HubComment c)
    {
        var (_, token, _) = await Member();
        await Commit(new JsonArray(
            new JsonObject { ["delete"] = Firebase.Doc($"creations/{mod.Id}/comments/{c.Id}") },
            Increment($"creations/{mod.Id}", "comments", -1)), token);
        Invalidate();
    }

    public static async Task Report(HubMod mod, string reason)
    {
        var (uid, token) = await Account.Token();
        await Commit(new JsonArray(new JsonObject
        {
            ["update"] = new JsonObject
            {
                ["name"] = Firebase.Doc($"reports/{mod.Id}__{uid}"),
                ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["creation"] = mod.Id, ["uid"] = uid, ["reason"] = Clip(reason, 500) }),
            },
            ["updateTransforms"] = new JsonArray(ServerTime("created")),
        }), token);
    }

    // ---------------------------------------------------------------- отслеживание (как «Track» на Nexus)

    public static bool Followed(string id) => Local.Obj("follow").Str(id) is not null;

    public static void Follow(string id, string version) { Local.Obj("follow")[id] = version; Settings.Save(); }
    public static void Unfollow(string id) { Local.Obj("follow").Remove(id); Settings.Save(); }

    /// <summary>Отслеживаемые моды, у которых вышла новая версия.</summary>
    public static List<HubMod> Updates { get; } = [];

    static void CheckFollowed(List<HubMod> all)
    {
        Updates.Clear();
        foreach (var (id, seen) in Local.Obj("follow"))
        {
            var mod = all.FirstOrDefault(m => m.Id == id);
            if (mod is not null && mod.Version != seen?.ToString()) Updates.Add(mod);
        }
    }

    public static void MarkSeen(HubMod mod) { if (Followed(mod.Id)) Follow(mod.Id, mod.Version); Updates.RemoveAll(m => m.Id == mod.Id); }

    // ---------------------------------------------------------------- в каталог игры

    public static Sources.ModInfo ToModInfo(HubMod m) => new()
    {
        Source = "hub",
        Id = m.Id,
        Name = m.Name,
        Author = m.Author,
        Version = m.Version,
        Description = m.Summary,
        Icon = m.Images.FirstOrDefault(),
        Downloads = m.Downloads,
        Rating = m.Likes,
        UpdatedAt = m.Updated,
        Categories = m.Tags.ToArray(),
    };
}
