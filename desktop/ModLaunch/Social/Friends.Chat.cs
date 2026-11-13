using System.Text.Json.Nodes;
using ModLaunch.Core;

namespace ModLaunch.Social;

/// <summary>Сообщение чата. Pending — ещё отправляется (видно сразу, до ответа сервера).</summary>
public sealed record ChatMessage(string Id, string From, string Text, string Kind, string Game, string GameName, DateTime At, bool Mine, bool Pending);

public static partial class Friends
{
    /// <summary>
    /// 28.0: чат с друзьями и приглашения в игру. Сообщения — chats/{пара}/messages (пару пишут и читают только двое из
    /// принятой дружбы), пометка «написал» — в почтовом ящике друга inbox/{uid} (одно поле на отправителя). Фоном
    /// читается только свой ящик — раз в две минуты; пока открыт разговор — каждые четыре секунды. История — в chats.json.
    /// Сервер ещё без правил чата (DENIED) — чат честно говорит «временно недоступен» и не тратит запросы.
    /// </summary>
    public static class Chat
    {
        const int Keep = 300;
        const int MaxText = 1000;
        static readonly TimeSpan Background = TimeSpan.FromMinutes(2);
        static readonly TimeSpan Open = TimeSpan.FromSeconds(4);
        static readonly JsonFile Store = new(Path.Combine(Paths.DataDir, "chats.json"), () => new JsonObject());
        static readonly SemaphoreSlim Gate = new(1);
        static readonly object Lock = new();
        static Timer? _timer;
        static DateTime _next;

        /// <summary>Что-то поменялось в разговоре с этим другом ("" — во многих сразу).</summary>
        public static event Action<string>? Changed;

        /// <summary>С кем сейчас открыт разговор (его ящик проверяется часто, его сообщения сразу прочитаны).</summary>
        public static string? OpenWith { get; set; }

        /// <summary>Последняя ошибка: DENIED (правила чата не выложены), BUSY, OFFLINE… null — всё хорошо.</summary>
        public static string? Error { get; private set; }

        /// <summary>Для снимков экрана: разговоры без сервера.</summary>
        internal static Dictionary<string, List<ChatMessage>>? Demo { get; set; }

        static JsonObject Thread(string uid)
        {
            if (Store.Data[uid] is not JsonObject t) Store.Data[uid] = t = new JsonObject { ["msgs"] = new JsonArray(), ["read"] = 0L, ["last"] = null, ["inbox"] = null };
            return t;
        }

        static ChatMessage Read(JsonNode m, string me) => new(m.Str("id") ?? "", m.Str("from") ?? "", m.Str("text") ?? "", m.Str("kind") ?? "text",
            m.Str("game") ?? "", m.Str("gameName") ?? "", Firebase.Time(m.Str("at")) ?? DateTime.UtcNow, m.Str("from") == me, m.Bool("pending"));

        public static IReadOnlyList<ChatMessage> Messages(string uid)
        {
            if (Program.Demo && Demo is { } d) return d.GetValueOrDefault(uid) ?? [];
            var me = Account.Uid ?? "";
            lock (Lock) return Store.Data[uid] is JsonObject t ? t.Arr("msgs").OfType<JsonNode>().Select(m => Read(m, me)).ToList() : [];
        }

        public static int Unread(string uid)
        {
            if (Program.Demo && Demo is { } d) return uid == "u1" ? 2 : 0;
            if (OpenWith == uid) return 0;
            var me = Account.Uid ?? "";
            lock (Lock)
            {
                if (Store.Data[uid] is not JsonObject t) return 0;
                var read = t.Long("read");
                return t.Arr("msgs").OfType<JsonNode>().Count(m => m.Str("from") != me && (Firebase.Time(m.Str("at")) is DateTime at ? new DateTimeOffset(at).ToUnixTimeMilliseconds() : 0) > read);
            }
        }

        public static int UnreadTotal
        {
            get
            {
                if (Program.Demo && Demo is not null) return 2;
                List<string> uids;
                lock (Lock) uids = Store.Data.Select(kv => kv.Key).Where(k => k != "uid").ToList();
                return uids.Sum(Unread);
            }
        }

        public static void MarkRead(string uid)
        {
            lock (Lock)
            {
                if (Store.Data[uid] is not JsonObject t) return;
                var newest = t.Arr("msgs").OfType<JsonNode>().Select(m => Firebase.Time(m.Str("at"))).OfType<DateTime>().DefaultIfEmpty(DateTime.UtcNow).Max();
                var ms = Math.Max(new DateTimeOffset(newest).ToUnixTimeMilliseconds(), t.Long("read"));
                if (ms == t.Long("read")) return;
                t["read"] = ms;
            }
            Store.Save();
            Changed?.Invoke(uid);
        }

        /// <summary>Чужая история (другой аккаунт на этом ПК) не показывается: файл привязан к uid.</summary>
        static void Own(string me)
        {
            lock (Lock)
            {
                if (Store.Data.Str("uid") == me) return;
                Store.Data.Clear();
                Store.Data["uid"] = me;
            }
            Store.Save();
        }

        // ---------------------------------------------------------------- отправка

        public static Task Send(string friendUid, string text) => Post(friendUid, text.Trim(), "text", "", "");

        /// <summary>Позвать в игру: game — общий ключ игры (как у отзывов: steam:730, epic:…), gameName — как её показать.</summary>
        public static Task Invite(string friendUid, string game, string gameName) => Post(friendUid, "", "invite", game, gameName);

        static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

        static async Task Post(string friend, string text, string kind, string game, string gameName)
        {
            if (kind == "text" && text == "") return;
            text = Clip(text.Replace("\r\n", "\n"), MaxText);
            game = Clip(game, 40);
            gameName = Clip(gameName, 60);
            var me = await Auth();
            Own(me.Uid);
            var mid = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():D13}_{me.Uid}";
            var local = new JsonObject
            {
                ["id"] = mid, ["from"] = me.Uid, ["text"] = text, ["kind"] = kind, ["game"] = game, ["gameName"] = gameName,
                ["at"] = DateTime.UtcNow.ToString("o"), ["pending"] = true,
            };
            lock (Lock) Thread(friend).Arr("msgs").Add(local);
            Changed?.Invoke(friend);

            var preview = kind == "invite" ? gameName : Clip(text.Replace('\n', ' '), 120);
            var writes = new JsonArray
            {
                new JsonObject
                {
                    ["update"] = new JsonObject
                    {
                        ["name"] = Firebase.Doc($"chats/{Pair(me.Uid, friend)}/messages/{mid}"),
                        ["fields"] = Firebase.ToFields(new Dictionary<string, object?> { ["from"] = me.Uid, ["text"] = text, ["kind"] = kind, ["game"] = game, ["gameName"] = gameName }),
                    },
                    ["updateTransforms"] = new JsonArray(Server("at")),
                    ["currentDocument"] = new JsonObject { ["exists"] = false },
                },
                new JsonObject
                {
                    ["update"] = new JsonObject
                    {
                        ["name"] = Firebase.Doc($"inbox/{friend}"),
                        ["fields"] = Firebase.ToFields(new Dictionary<string, object?>
                        {
                            [me.Uid] = new Dictionary<string, string> { ["text"] = preview, ["name"] = me.Name, ["kind"] = kind },
                        }),
                    },
                    ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray($"`{me.Uid}`") },
                    ["updateTransforms"] = new JsonArray(Server($"`{me.Uid}`.at")),
                },
            };
            try
            {
                await Commit(writes, me);
                Error = null;
                lock (Lock) local.Remove("pending");
                Store.Save();
                Changed?.Invoke(friend);
            }
            catch (ServiceError e)
            {
                lock (Lock) Thread(friend).Arr("msgs").Remove(local);
                if (e.Code is "DENIED" or "BUSY") Error = e.Code;
                Changed?.Invoke(friend);
                throw;
            }
        }

        // ---------------------------------------------------------------- получение

        /// <summary>Сообщения разговора новее after (или последние 50, если истории ещё нет), по порядку.</summary>
        static async Task<List<JsonObject>> Fetch(Me me, string friend, string? after)
        {
            var query = new JsonObject
            {
                ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "messages" }),
                ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "at" }, ["direction"] = after is null ? "DESCENDING" : "ASCENDING" }),
                ["limit"] = after is null ? 50 : 200,
            };
            if (after is not null)
                query["where"] = new JsonObject { ["fieldFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "at" }, ["op"] = "GREATER_THAN", ["value"] = new JsonObject { ["timestampValue"] = after } } };
            var rows = await Call(Firebase.Url($"/chats/{Pair(me.Uid, friend)}:runQuery"), HttpMethod.Post, new JsonObject { ["structuredQuery"] = query }, me.Token);
            var list = (rows as JsonArray ?? []).Select(r => r?["document"]).OfType<JsonNode>().Select(doc =>
            {
                var f = Firebase.FromFields(doc["fields"] as JsonObject);
                return new JsonObject
                {
                    ["id"] = doc.Str("name")?.Split('/')[^1], ["from"] = f.S("from"), ["text"] = f.S("text"), ["kind"] = f.S("kind") is { Length: > 0 } k ? k : "text",
                    ["game"] = f.S("game"), ["gameName"] = f.S("gameName"), ["at"] = f.S("at"),
                };
            }).ToList();
            if (after is null) list.Reverse();
            return list;
        }

        /// <summary>
        /// Проверить свой ящик и догрузить новые сообщения. only — разговор открыт: догрузить его, даже если в ящике
        /// пусто (например, пишем с другого ПК). Возвращает, сколько пришло новых чужих сообщений.
        /// </summary>
        public static async Task<int> Poll(string? only = null)
        {
            if (!Firebase.Configured || !Account.SignedIn || Program.Demo) return 0;
            if (!await Gate.WaitAsync(0)) return 0;
            try
            {
                var me = await Auth();
                Own(me.Uid);
                var inbox = await GetDoc($"inbox/{me.Uid}", me) ?? new Dictionary<string, object?>();
                var due = new List<(string Uid, string? InboxAt)>();
                lock (Lock)
                {
                    foreach (var (uid, value) in inbox)
                        if (value is Dictionary<string, object?> e && e.S("at") is { Length: > 0 } at
                            && (Store.Data[uid] is not JsonObject t || Firebase.Micros(at) > Firebase.Micros(t.Str("inbox"))))
                            due.Add((uid, at));
                    if (only is not null && due.All(d => d.Uid != only)) due.Add((only, null));
                }
                var fresh = 0;
                var toast = (Name: "", Text: "", Count: 0);
                foreach (var (uid, inboxAt) in due)
                {
                    string? after;
                    lock (Lock) after = Store.Data[uid] is JsonObject t ? t.Str("last") : null;
                    var got = await Fetch(me, uid, after);
                    lock (Lock)
                    {
                        var t = Thread(uid);
                        var msgs = t.Arr("msgs");
                        var known = msgs.OfType<JsonNode>().Select(m => m.Str("id")).ToHashSet();
                        foreach (var m in got)
                        {
                            var id = m.Str("id");
                            // Своё сообщение, отправленное отсюда, уже лежит (с временем ПК) — заменяем серверным.
                            if (known.Contains(id))
                            {
                                foreach (var old in msgs.OfType<JsonObject>().Where(o => o.Str("id") == id).ToList()) msgs.Remove(old);
                            }
                            else if (m.Str("from") != me.Uid)
                            {
                                fresh++;
                                if (OpenWith != uid)
                                {
                                    var name = (inbox.GetValueOrDefault(uid) as Dictionary<string, object?>)?.S("name") ?? "";
                                    toast = (name, m.Str("kind") == "invite" ? I18n.T("v28.chat.invite.short", ("game", m.Str("gameName") ?? "")) : m.Str("text") ?? "", toast.Count + 1);
                                }
                            }
                            msgs.Add(m);
                        }
                        while (msgs.Count > Keep) msgs.RemoveAt(0);
                        if (got.Count > 0) t["last"] = got[^1].Str("at");
                        if (inboxAt is not null) t["inbox"] = inboxAt;
                        if (t["last"] is null && got.Count == 0) t["last"] = DateTime.UtcNow.AddMinutes(-1).ToString("o");
                    }
                    if (OpenWith == uid) MarkRead(uid);
                }
                if (due.Count > 0) Store.Save();
                Error = null;
                if (fresh > 0 || due.Count > 0) Changed?.Invoke(due.Count == 1 ? due[0].Uid : "");
                if (toast.Count > 0)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => Views.MainWindow.Current?.Toast(toast.Count == 1 && toast.Name != ""
                        ? I18n.T("v28.chat.toast", ("name", toast.Name), ("text", Clip(toast.Text.Replace('\n', ' '), 80)))
                        : I18n.T("v28.chat.toast.many", ("n", toast.Count))));
                return fresh;
            }
            catch (ServiceError e)
            {
                Error = e.Code;
                // Правила чата ещё не выложены — не стучимся каждые две минуты, только изредка.
                if (e.Code == "DENIED") _next = DateTime.UtcNow.AddMinutes(30);
                Changed?.Invoke(only ?? "");
                return 0;
            }
            catch (Exception) { Error = "OFFLINE"; return 0; }
            finally { Gate.Release(); }
        }

        /// <summary>Проверять ящик фоном (раз в две минуты, а пока открыт разговор — каждые четыре секунды).</summary>
        public static void Start()
        {
            if (_timer is not null || Program.Demo || Program.Screenshot) return;
            _timer = new Timer(_ =>
            {
                var open = OpenWith;
                if (open is not null) { _ = Poll(open); return; }
                if (DateTime.UtcNow < _next) return;
                _next = DateTime.UtcNow + Background;
                if (View().Friends.Count > 0) _ = Poll();
            }, null, TimeSpan.FromSeconds(20), Open);
        }

        [SelfTest]
        static string ChatRulesShipWithApp()
        {
            using var s = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ModLaunch/Assets/firestore.rules"));
            var rules = new StreamReader(s).ReadToEnd();
            foreach (var need in new[] { "match /chats/{pair}/messages/{mid}", "match /inbox/{uid}", "inboxEntryOk", "chatAllowed(pair)" })
                if (!rules.Contains(need)) throw new Exception("rules lack " + need);
            var mid = $"{1760000000123:D13}_abcDEF123";
            if (!System.Text.RegularExpressions.Regex.IsMatch(mid, "^[0-9]{13}_abcDEF123$")) throw new Exception("message id shape");
            return "chat and inbox rules are in the shipped rules file; message ids match ^[0-9]{13}_uid$";
        }
    }
}
