using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Creator;

namespace ModLaunch.Social;

/// <summary>Группа разработчиков: название, тег, цвет, участники (uid → имя), приглашённые и код для входа.</summary>
public sealed record Team(string Id, string Name, string Tag, string Color, string About, string Owner, List<string> Members,
    Dictionary<string, string> Names, List<string> Invites, string Code, DateTime? Created, DateTime? Updated)
{
    public string NameOf(string uid) => Names.TryGetValue(uid, out var n) && n != "" ? n : "ModLaunch";
}

/// <summary>Проект, выложенный в команду: текст скрипта и кто выложил последним.</summary>
public sealed record TeamProject(string Id, string Name, string Game, string Version, string By, string ByName, string Script, DateTime? Updated);

/// <summary>Что человек ввёл в «Создать группу» или «Изменить группу».</summary>
public sealed record TeamDraft(string Name, string Tag, string Color, string About);

/// <summary>
/// 13.0: группы разработчиков на аккаунте — Firestore teams/{id} (правила — firebase/firestore.rules).
/// Создать группу может любой вошедший с почтой; в группу входят по коду (teamcodes/{код} → id группы)
/// или по приглашению от владельца (друг попадает в invites и видит его у себя). В группе — общие проекты
/// teams/{id}/projects/{pid}: участник выкладывает main.mls, другие берут копию или обновляют свою.
/// Владелец переименовывает, меняет код, убирает участников и удаляет группу; участник может выйти.
/// </summary>
public static partial class Teams
{
    public const int MaxName = 40, MaxAbout = 200, MaxMembers = 25, MaxMine = 5, MaxScript = 100_000;
    public static readonly string[] Colors = ["violet", "blue", "green", "orange", "pink", "cyan", "red", "yellow"];
    const string IdAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    [GeneratedRegex(@"^[A-Z0-9]{2,5}$")] private static partial Regex TagRe();
    [GeneratedRegex(@"^[A-Z0-9]{8}$")] private static partial Regex CodeRe();
    [GeneratedRegex(@"[\u0000-\u001f\u007f]")] private static partial Regex Control();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    static List<Team>? _mine, _invites;
    static readonly SemaphoreSlim Gate = new(1);

    /// <summary>Группы или приглашения поменялись.</summary>
    public static event Action? Changed;

    /// <summary>Для снимков экрана: свои группы и приглашения; свой uid — <see cref="DemoUid"/>.</summary>
    internal static List<Team>? DemoMine { get; set; }
    internal static List<Team>? DemoInvites { get; set; }
    internal static Dictionary<string, List<TeamProject>>? DemoProjects { get; set; }
    internal const string DemoUid = "demo-me";

    public static string? Me => Program.Demo && DemoMine is not null ? DemoUid : Account.Uid;

    /// <summary>Мои группы (последние загруженные).</summary>
    public static List<Team> Mine => Program.Demo && DemoMine is { } demo ? demo : _mine ?? [];

    /// <summary>Приглашения в чужие группы.</summary>
    public static List<Team> Invites => Program.Demo && DemoInvites is { } demo ? demo : _invites ?? [];

    public static bool Loaded => Program.Demo && DemoMine is not null || _mine is not null;

    // ---------------------------------------------------------------- проверки (без сети)

    static string Tidy(string? value) => Spaces().Replace(Control().Replace(value ?? "", ""), " ").Trim();

    /// <summary>Тег из названия: первые буквы слов (латиница и цифры), 2–5 знаков.</summary>
    public static string SuggestTag(string name)
    {
        var words = Regex.Split(Translit(name).ToUpperInvariant(), @"[^A-Z0-9]+").Where(w => w != "").ToList();
        var tag = words.Count >= 2 ? string.Concat(words.Take(4).Select(w => w[0])) : words.FirstOrDefault() ?? "";
        if (tag.Length > 5) tag = tag[..5];
        if (tag.Length < 2) tag = (tag + "DEV")[..3];
        return tag;
    }

    /// <summary>Латиница из кириллицы — только для тега.</summary>
    static string Translit(string s)
    {
        const string ru = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] lat = ["a", "b", "v", "g", "d", "e", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "h", "c", "ch", "sh", "sch", "", "y", "", "e", "yu", "ya"];
        var sb = new System.Text.StringBuilder();
        foreach (var c in s.ToLowerInvariant()) sb.Append(ru.IndexOf(c) is var i and >= 0 ? lat[i] : c.ToString());
        return sb.ToString();
    }

    public static TeamDraft Normalize(TeamDraft d) => d with
    {
        Name = Tidy(d.Name), Tag = Tidy(d.Tag).ToUpperInvariant(), Color = Colors.Contains(d.Color) ? d.Color : Colors[0], About = Tidy(d.About),
    };

    /// <summary>Что не так с черновиком: ключи текстов ошибок (пусто — можно сохранять).</summary>
    public static List<string> Check(TeamDraft draft)
    {
        var d = Normalize(draft);
        var errors = new List<string>();
        if (d.Name.Length == 0) errors.Add("v13.team.err.name");
        else if (d.Name.Length > MaxName) errors.Add("v13.team.err.nameLong");
        if (!TagRe().IsMatch(d.Tag)) errors.Add("v13.team.err.tag");
        if (d.About.Length > MaxAbout) errors.Add("v13.team.err.about");
        return errors;
    }

    /// <summary>Код входа: 8 знаков без похожих (0/O, 1/I). Пробелы и дефисы при вводе не мешают.</summary>
    public static string? CleanCode(string? input)
    {
        var code = Regex.Replace((input ?? "").ToUpperInvariant(), @"[\s-]", "");
        return CodeRe().IsMatch(code) ? code : null;
    }

    static string NewId() => RandomNumberGenerator.GetString(IdAlphabet, 20);
    static string NewCode() => RandomNumberGenerator.GetString(CodeAlphabet, 8);

    /// <summary>Код для показа: «ABCD-EFGH».</summary>
    public static string ShowCode(string code) => code.Length == 8 ? code[..4] + "-" + code[4..] : code;

    // ---------------------------------------------------------------- чтение

    static Team FromDoc(JsonObject doc)
    {
        var f = Firebase.FromFields(doc["fields"] as JsonObject);
        var name = doc.Str("name") ?? "";
        var names = f.TryGetValue("names", out var n) && n is Dictionary<string, object?> m
            ? m.Where(kv => kv.Value is string).ToDictionary(kv => kv.Key, kv => Account.CleanName((string)kv.Value!))
            : [];
        return new Team(name[(name.LastIndexOf('/') + 1)..], Tidy(f.S("name")), f.S("tag"), Colors.Contains(f.S("color")) ? f.S("color") : Colors[0], Tidy(f.S("about")),
            f.S("owner"), f.A("members"), names, f.A("invites"), f.S("code"), Firebase.Time(f.S("created")), Firebase.Time(f.S("updated")));
    }

    static async Task<List<Team>> Query(string field, string uid, string token)
    {
        var rows = await Hub.Call(Firebase.Url(":runQuery"), HttpMethod.Post, new JsonObject
        {
            ["structuredQuery"] = new JsonObject
            {
                ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "teams" }),
                ["where"] = new JsonObject
                {
                    ["fieldFilter"] = new JsonObject
                    {
                        ["field"] = new JsonObject { ["fieldPath"] = field }, ["op"] = "ARRAY_CONTAINS", ["value"] = new JsonObject { ["stringValue"] = uid },
                    },
                },
                ["limit"] = 20,
            },
        }, token);
        return (rows as JsonArray ?? []).Select(r => r?["document"] as JsonObject).OfType<JsonObject>().Select(FromDoc).OrderBy(t => t.Created ?? DateTime.MaxValue).ToList();
    }

    /// <summary>Загрузить свои группы и приглашения.</summary>
    public static async Task Refresh()
    {
        if (Program.Demo && DemoMine is not null) return;
        await Gate.WaitAsync();
        try
        {
            var (uid, token, _) = await Hub.Member();
            _mine = await Query("members", uid, token);
            _invites = (await Query("invites", uid, token)).Where(t => !t.Members.Contains(uid)).ToList();
        }
        finally { Gate.Release(); }
        Changed?.Invoke();
    }

    static void Put(Team t)
    {
        var list = new List<Team>(_mine ?? []);
        var at = list.FindIndex(x => x.Id == t.Id);
        if (at >= 0) list[at] = t; else list.Add(t);
        _mine = list;
        _invites = (_invites ?? []).Where(x => x.Id != t.Id).ToList();
        Changed?.Invoke();
    }

    static void Drop(string id)
    {
        _mine = (_mine ?? []).Where(x => x.Id != id).ToList();
        _invites = (_invites ?? []).Where(x => x.Id != id).ToList();
        Changed?.Invoke();
    }

    static async Task<Team> Load(string id, string token)
    {
        var doc = await Hub.Call(Firebase.Url($"/teams/{id}"), HttpMethod.Get, null, token) as JsonObject ?? throw new ServiceError("NOT_FOUND");
        return FromDoc(doc);
    }

    // ---------------------------------------------------------------- запись

    static Dictionary<string, object?> Fields(Team t) => new()
    {
        ["name"] = t.Name, ["tag"] = t.Tag, ["color"] = t.Color, ["about"] = t.About, ["owner"] = t.Owner, ["members"] = t.Members,
        ["names"] = t.Names, ["invites"] = t.Invites, ["code"] = t.Code,
    };

    /// <summary>Запись группы: только перечисленные поля (keys — маска), updated — время сервера.</summary>
    static JsonObject Write(Team t, IEnumerable<string> keys, bool create = false)
    {
        var all = Fields(t);
        var fields = keys.ToDictionary(k => k, k => all[k]);
        var write = new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"teams/{t.Id}"), ["fields"] = Firebase.ToFields(fields) },
            ["updateTransforms"] = create ? new JsonArray(Hub.ServerTime("created"), Hub.ServerTime("updated")) : new JsonArray(Hub.ServerTime("updated")),
            ["currentDocument"] = new JsonObject { ["exists"] = !create },
        };
        if (!create) write["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Keys.Select(k => (JsonNode)k).ToArray()) };
        return write;
    }

    static JsonObject CodeWrite(string code, string team, string owner) => new()
    {
        ["update"] = new JsonObject
        {
            ["name"] = Firebase.Doc($"teamcodes/{code}"),
            ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["team"] = team, ["owner"] = owner }),
        },
        ["currentDocument"] = new JsonObject { ["exists"] = false },
    };

    static JsonObject Delete(string doc) => new() { ["delete"] = Firebase.Doc(doc) };

    /// <summary>Создать группу: я — владелец и единственный участник, код входа — новый.</summary>
    public static async Task<Team> Create(TeamDraft draft)
    {
        if (Check(draft) is [var first, ..]) throw new ArgumentException(I18n.T(first, ("n", MaxName)));
        var d = Normalize(draft);
        var (uid, token, name) = await Hub.Member();
        if (_mine is null) await Refresh();
        if (Mine.Count >= MaxMine) throw new ServiceError("TOO_MANY");
        var team = new Team(NewId(), d.Name, d.Tag, d.Color, d.About, uid, [uid], new() { [uid] = name }, [], NewCode(), null, null);
        await Hub.Commit(new JsonArray(Write(team, Fields(team).Keys, create: true), CodeWrite(team.Code, team.Id, uid)), token);
        var now = DateTime.UtcNow + Firebase.Skew;
        team = team with { Created = now, Updated = now };
        Put(team);
        return team;
    }

    /// <summary>Изменить название, тег, цвет и описание (только владелец).</summary>
    public static async Task<Team> Edit(Team t, TeamDraft draft)
    {
        if (Check(draft) is [var first, ..]) throw new ArgumentException(I18n.T(first, ("n", MaxName)));
        var d = Normalize(draft);
        var (_, token, _) = await Hub.Member();
        var changed = t with { Name = d.Name, Tag = d.Tag, Color = d.Color, About = d.About };
        await Hub.Commit(new JsonArray(Write(changed, ["name", "tag", "color", "about"])), token);
        Put(changed);
        return changed;
    }

    /// <summary>Новый код входа (старый перестаёт работать).</summary>
    public static async Task<Team> NewCode(Team t)
    {
        var (uid, token, _) = await Hub.Member();
        var changed = t with { Code = NewCode() };
        await Hub.Commit(new JsonArray(Write(changed, ["code"]), Delete($"teamcodes/{t.Code}"), CodeWrite(changed.Code, t.Id, uid)), token);
        Put(changed);
        return changed;
    }

    /// <summary>Войти по коду. Уже в группе — просто вернуть её.</summary>
    public static async Task<Team> Join(string input)
    {
        var code = CleanCode(input) ?? throw new ServiceError("BAD_CODE");
        var (uid, token, name) = await Hub.Member();
        JsonObject codeDoc;
        try { codeDoc = await Hub.Call(Firebase.Url($"/teamcodes/{code}"), HttpMethod.Get, null, token) as JsonObject ?? throw new ServiceError("BAD_CODE"); }
        catch (ServiceError e) when (e.Code == "NOT_FOUND") { throw new ServiceError("BAD_CODE"); }
        var id = Firebase.FromFields(codeDoc["fields"] as JsonObject).S("team");
        if (Mine.FirstOrDefault(t => t.Id == id) is { } already) return already;
        return await Enter(id, uid, token, name, code);
    }

    /// <summary>Принять приглашение.</summary>
    public static async Task<Team> Accept(Team invite)
    {
        var (uid, token, name) = await Hub.Member();
        return await Enter(invite.Id, uid, token, name, null);
    }

    static JsonObject Values(params string[] values) => new() { ["values"] = new JsonArray(values.Select(v => Firebase.ToValue(v)).ToArray()) };

    /// <summary>Поле-ключ словаря имён: names.`uid` (uid в обратных кавычках — на случай любых знаков).</summary>
    static string NameField(string uid) => $"names.`{uid.Replace("`", "")}`";

    /// <summary>
    /// Войти в группу, не читая её (до входа группу видят только участники и приглашённые): себя — в участники
    /// (appendMissingElements) и в имена, своё приглашение — убрать. По коду пишется ещё и joined = код:
    /// правила пускают постороннего, только если он приглашён или знает нынешний код. Полна ли группа — проверяют правила.
    /// </summary>
    static async Task<Team> Enter(string id, string uid, string token, string name, string? code)
    {
        if (Mine.Count >= MaxMine) throw new ServiceError("TOO_MANY");
        var fields = new JsonObject { ["names"] = new JsonObject { ["mapValue"] = new JsonObject { ["fields"] = new JsonObject { [uid] = Firebase.ToValue(name) } } } };
        var mask = new JsonArray(NameField(uid));
        if (code is not null) { fields["joined"] = Firebase.ToValue(code); mask.Add("joined"); }
        var write = new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"teams/{id}"), ["fields"] = fields },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = mask },
            ["updateTransforms"] = new JsonArray(
                new JsonObject { ["fieldPath"] = "members", ["appendMissingElements"] = Values(uid) },
                new JsonObject { ["fieldPath"] = "invites", ["removeAllFromArray"] = Values(uid) },
                Hub.ServerTime("updated")),
            ["currentDocument"] = new JsonObject { ["exists"] = true },
        };
        try { await Hub.Commit(new JsonArray(write), token); }
        catch (ServiceError e) when (e.Code == "DENIED" && code is not null) { throw new ServiceError("FULL_OR_CODE"); }
        var team = await Load(id, token);
        Put(team);
        return team;
    }

    /// <summary>Отклонить приглашение: убрать себя из invites.</summary>
    public static async Task Decline(Team invite)
    {
        var (uid, token, _) = await Hub.Member();
        await Hub.Commit(new JsonArray(new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"teams/{invite.Id}"), ["fields"] = new JsonObject() },
            ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray() },
            ["updateTransforms"] = new JsonArray(new JsonObject { ["fieldPath"] = "invites", ["removeAllFromArray"] = Values(uid) }, Hub.ServerTime("updated")),
            ["currentDocument"] = new JsonObject { ["exists"] = true },
        }), token);
        Drop(invite.Id);
    }

    /// <summary>Пригласить друга (владелец): он увидит группу в «Приглашениях».</summary>
    public static async Task<Team> Invite(Team t, string friendUid)
    {
        if (t.Members.Contains(friendUid) || t.Invites.Contains(friendUid)) return t;
        if (t.Members.Count + t.Invites.Count >= MaxMembers) throw new ServiceError("FULL");
        var (_, token, _) = await Hub.Member();
        var changed = t with { Invites = [.. t.Invites, friendUid] };
        await Hub.Commit(new JsonArray(Write(changed, ["invites"])), token);
        Put(changed);
        return changed;
    }

    /// <summary>Убрать участника или приглашение (владелец).</summary>
    public static async Task<Team> Kick(Team t, string uid)
    {
        if (uid == t.Owner) return t;
        var (_, token, _) = await Hub.Member();
        var names = new Dictionary<string, string>(t.Names);
        names.Remove(uid);
        var changed = t with { Members = t.Members.Where(m => m != uid).ToList(), Names = names, Invites = t.Invites.Where(i => i != uid).ToList() };
        await Hub.Commit(new JsonArray(Write(changed, ["members", "names", "invites"])), token);
        Put(changed);
        return changed;
    }

    /// <summary>Выйти из группы (не владелец) или удалить её вместе с кодом и проектами (владелец).</summary>
    public static async Task Leave(Team t)
    {
        var (uid, token, _) = await Hub.Member();
        if (uid == t.Owner)
        {
            var writes = new JsonArray();
            try { foreach (var p in await Projects(t)) writes.Add(Delete($"teams/{t.Id}/projects/{p.Id}")); } catch (ServiceError) { }
            writes.Add(Delete($"teamcodes/{t.Code}"));
            writes.Add(Delete($"teams/{t.Id}"));
            await Hub.Commit(writes, token);
        }
        else
        {
            // Себя — из участников (removeAllFromArray) и своё имя (в маске, но не в полях — ключ удаляется).
            await Hub.Commit(new JsonArray(new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = Firebase.Doc($"teams/{t.Id}"), ["fields"] = new JsonObject() },
                ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(NameField(uid)) },
                ["updateTransforms"] = new JsonArray(new JsonObject { ["fieldPath"] = "members", ["removeAllFromArray"] = Values(uid) }, Hub.ServerTime("updated")),
                ["currentDocument"] = new JsonObject { ["exists"] = true },
            }), token);
        }
        Drop(t.Id);
    }

    // ---------------------------------------------------------------- общие проекты

    /// <summary>id проекта в команде: из имени папки проекта (латиница, цифры, - и _).</summary>
    public static string ProjectId(Project p) => Regex.Replace(p.Id, @"[^A-Za-z0-9_-]", "-") is { Length: > 0 } id ? (id.Length > 64 ? id[..64] : id) : "project";

    public static async Task<List<TeamProject>> Projects(Team t)
    {
        if (Program.Demo && DemoProjects is { } demo) return demo.GetValueOrDefault(t.Id) ?? [];
        var (_, token, _) = await Hub.Member();
        var rows = await Hub.Call(Firebase.Url($"/teams/{t.Id}/projects") + "&pageSize=100", HttpMethod.Get, null, token);
        return (rows?["documents"] as JsonArray ?? []).OfType<JsonObject>().Select(doc =>
        {
            var f = Firebase.FromFields(doc["fields"] as JsonObject);
            var name = doc.Str("name") ?? "";
            return new TeamProject(name[(name.LastIndexOf('/') + 1)..], Tidy(f.S("name")), f.S("game"), f.S("version"), f.S("by"), Account.CleanName(f.S("byName")), f.S("script"), Firebase.Time(f.S("updated")));
        }).OrderByDescending(p => p.Updated).ToList();
    }

    /// <summary>Выложить свой проект в команду (или обновить выложенный): main.mls целиком.</summary>
    public static async Task Share(Team t, Project p)
    {
        var script = File.ReadAllText(p.Script);
        if (script.Length > MaxScript) throw new ServiceError("TOO_BIG");
        var (uid, token, name) = await Hub.Member();
        var fields = new Dictionary<string, object?>
        {
            ["name"] = Tidy(p.Name) is { Length: > 0 } n ? (n.Length > 60 ? n[..60] : n) : "Project", ["game"] = p.Game.Length > 60 ? p.Game[..60] : p.Game,
            ["version"] = p.Version.Length > 24 ? p.Version[..24] : p.Version, ["by"] = uid, ["byName"] = name, ["script"] = script,
        };
        await Hub.Commit(new JsonArray(new JsonObject
        {
            ["update"] = new JsonObject { ["name"] = Firebase.Doc($"teams/{t.Id}/projects/{ProjectId(p)}"), ["fields"] = Firebase.ToFields(fields) },
            ["updateTransforms"] = new JsonArray(Hub.ServerTime("updated")),
        }), token);
    }

    /// <summary>Убрать проект из команды (кто выложил последним или владелец).</summary>
    public static async Task Unshare(Team t, TeamProject p)
    {
        var (_, token, _) = await Hub.Member();
        await Hub.Commit(new JsonArray(Delete($"teams/{t.Id}/projects/{p.Id}")), token);
    }

    /// <summary>Взять проект команды к себе: новый проект или обновить свой с тем же id (main.mls заменяется).</summary>
    public static Project Take(TeamProject p)
    {
        var mine = Creator.Projects.List().FirstOrDefault(x => ProjectId(x) == p.Id);
        if (mine is null) return Creator.Projects.Create(p.Name, p.Script);
        Creator.Projects.Save(mine, p.Script);
        return Creator.Projects.Get(mine.Id) ?? mine;
    }

    /// <summary>Понятный текст ошибки групп.</summary>
    public static string Explain(Exception e) => e switch
    {
        ServiceError { Code: "SIGN_IN" } => I18n.T("v13.team.err.signIn"),
        ServiceError { Code: "BAD_CODE" } => I18n.T("v13.team.err.code"),
        ServiceError { Code: "FULL_OR_CODE" } => I18n.T("v13.team.err.fullOrCode", ("n", MaxMembers)),
        ServiceError { Code: "FULL" } => I18n.T("v13.team.err.full", ("n", MaxMembers)),
        ServiceError { Code: "TOO_MANY" } => I18n.T("v13.team.err.tooMany", ("n", MaxMine)),
        ServiceError { Code: "TOO_BIG" } => I18n.T("v13.team.err.tooBig"),
        ServiceError { Code: "DENIED" } => I18n.T(Account.Owner ? "v13.team.err.denied.owner" : "v13.team.err.down"),
        ServiceError { Code: "NOT_CONFIGURED" } when Account.Owner => I18n.T("v11.cm.err.config.owner"),
        ServiceError s when Account.Owner => I18n.T("v11.cm.err.server", ("reason", s.Message)),
        ServiceError => I18n.T("v13.team.err.down"),
        ArgumentException a => a.Message,
        _ => I18n.T("v13.team.err.down"),
    };

    [SelfTest]
    static string TeamsDraftsChecked()
    {
        if (Check(new TeamDraft("  Студия   Ночь ", "nite", "violet", "")).Count != 0) throw new Exception("valid draft rejected");
        if (Normalize(new TeamDraft(" a\u0007b ", "ab", "nope", "x")) is not { Name: "ab", Tag: "AB", Color: "violet" }) throw new Exception("normalize");
        foreach (var bad in new[] { new TeamDraft("", "AB", "blue", ""), new TeamDraft("x", "A", "blue", ""), new TeamDraft("x", "TOOLONG", "blue", ""),
                     new TeamDraft("x", "A-B", "blue", ""), new TeamDraft(new string('x', 41), "AB", "blue", ""), new TeamDraft("x", "AB", "blue", new string('y', 201)) })
            if (Check(bad).Count == 0) throw new Exception("bad draft accepted: " + bad);
        if (SuggestTag("Ночная студия") != "NS" || SuggestTag("modders") != "MODDE" || SuggestTag("x") != "XDE") throw new Exception("tag " + SuggestTag("Ночная студия") + "/" + SuggestTag("modders") + "/" + SuggestTag("x"));
        if (CleanCode("abcd-efgh") != "ABCDEFGH" || CleanCode(" ab cd ef gh ") != "ABCDEFGH" || CleanCode("abc") is not null || CleanCode("ABCD-EFG!") is not null) throw new Exception("code");
        var code = NewCode();
        if (CleanCode(code) != code || code.IndexOfAny(['0', 'O', '1', 'I']) >= 0 || ShowCode(code)[4] != '-') throw new Exception("new code " + code);
        // Запись владельца — только перечисленные поля; имена — словарём (mapValue).
        var t = new Team("T".PadRight(20, 'x'), "n", "AB", "blue", "", "o", ["o"], new() { ["o"] = "Owner" }, ["me"], "ABCDEFGH", null, null);
        var w = Write(t with { Members = ["o"] }, ["members", "names", "invites"]);
        if (w["updateMask"]!["fieldPaths"]!.AsArray().Count != 3 || w["update"]!["fields"]!["names"]!["mapValue"] is null) throw new Exception("owner write");
        if (NameField("abc") != "names.`abc`") throw new Exception("name field path");
        return "drafts, tags, codes and writes checked";
    }
}
