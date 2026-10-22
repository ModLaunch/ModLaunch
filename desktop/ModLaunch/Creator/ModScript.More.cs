using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModLaunch.Creator;

/// <summary>
/// 13.0: новые команды ModScript. Описание пакета (id, tags, license, update_key, min_api, changelog, readme,
/// credits, optional), настройки для игрока и свои токены (option, token), данные Stardew Valley одной строкой
/// (recipe, craft, gift, quest, event, schedule, trigger, item, shop, remove), карты, картинки и звуки
/// (map, patch_image, patch_map, warp, sound, include), файлы (append, mkdir, jsonset) и сам язык
/// (repeat, switch, try/catch, inc/dec, push/pop/put/del/swap, assert, warn, error, stop, debug).
/// </summary>
public static partial class ModScript
{
    /// <summary>Новые команды 13.0 — для подсветки, подсказок и справочника.</summary>
    public static readonly string[] Commands13 =
    [
        "id", "tags", "license", "update_key", "min_api", "changelog", "readme", "credits", "optional",
        "option", "token", "remove", "recipe", "craft", "gift", "quest", "event", "schedule", "trigger", "item", "shop",
        "map", "patch_image", "patch_map", "warp", "sound", "include",
        "append", "mkdir", "jsonset",
        "repeat", "switch", "case", "default", "try", "catch", "inc", "dec", "push", "pop", "put", "del", "swap",
        "assert", "warn", "error", "stop", "debug",
        "text", "data", "foreach", "log", "letter", "cfg",
    ];

    /// <summary>Другие имена команд: text = entry, foreach = for и т. д.</summary>
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = "entry", ["data"] = "entry", ["foreach"] = "for", ["log"] = "print", ["letter"] = "mail", ["cfg"] = "config",
        ["overlay"] = "patch_image", ["json_set"] = "jsonset", ["updatekey"] = "update_key", ["smapi"] = "min_api", ["credit"] = "credits",
        ["tag"] = "tags", ["soft_needs"] = "optional", ["unset"] = "del", ["fail"] = "error", ["exit"] = "stop", ["uid"] = "id",
    };

    /// <summary>stop — закончить скрипт здесь.</summary>
    sealed class StopSignal : FlowSignal;

    static readonly JsonSerializerOptions JsonPretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Текст ошибки для $error в catch: «код: подробность», без перевода (язык работает и без ModLaunch).</summary>
    static string ErrorText(Diag d)
    {
        var parts = d.Message.Split('|', 2);
        var code = parts[0].StartsWith("err.") ? parts[0][4..] : parts[0];
        return parts.Length > 1 && parts[1] != "" ? $"{code}: {parts[1]}" : code;
    }

    /// <summary>try { … }: ошибки блока не валят сборку, а уходят в $error; true — была ошибка (для catch).</summary>
    static bool Attempt(List<Node> body, Env env, ModBuild b)
    {
        var before = b.Diags.Count;
        Run(body, env, b);
        var added = b.Diags.Skip(before).ToList();
        var errors = added.Where(x => !x.Warning).ToList();
        if (errors.Count == 0) return false;
        b.Diags.RemoveRange(before, added.Count);
        b.Diags.AddRange(added.Where(x => x.Warning));
        env.Vars["error"] = ErrorText(errors[0]);
        return true;
    }

    static string SafePath(string path)
    {
        path = path.Replace('\\', '/');
        if (path == "" || path.Contains("..") || Path.IsPathRooted(path)) throw new ScriptError("err.path");
        return path;
    }

    static int Int(string text, string error) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new ScriptError(error);

    /// <summary>Значение переменной списком: нет переменной — пустой список; не список — ошибка.</summary>
    static List<object?> ListVar(Env env, string name) =>
        !env.Vars.TryGetValue(name, out var cur) || cur == "" ? [] : FromText(cur) is List<object?> l ? l : throw new ScriptError("err.notList|" + name);

    static Dictionary<string, object?> DictVar(Env env, string name) =>
        !env.Vars.TryGetValue(name, out var cur) || cur == "" ? [] : FromText(cur) is Dictionary<string, object?> m ? m : throw new ScriptError("err.notDict|" + name);

    /// <summary>Положить файл проекта в assets/ пакета (один раз) и вернуть путь внутри пакета.</summary>
    static string Asset(ModBuild b, string file)
    {
        file = SafePath(file);
        var asset = "assets/" + Path.GetFileName(file);
        if (!b.Copies.Any(c => c.To == asset)) b.Copies.Add(new FileCopy(file, asset));
        return asset;
    }

    /// <summary>Новые команды 13.0; false — такой команды нет.</summary>
    static bool More(Node n, string cmd, Env env, ModBuild b)
    {
        var t = n.Toks;
        var line = n.Line;
        var d = b.Diags;
        string S(int i) => i < t.Count ? Subst(t[i].Text, env, line, d) : "";
        int Eq() => t.FindIndex(x => x is { Kind: T.Sym, Text: "=" });
        void Need(int count) { if (t.Count < count) throw new ScriptError("err.args|" + cmd); }
        string Rest(int from) => Value(t.Skip(from).ToList(), env, line, d);
        void Edit(string target, string key, JsonNode? value) => AddChange(b, env, new JsonObject
        {
            ["Action"] = "EditData", ["Target"] = target, ["Entries"] = new JsonObject { [key] = value },
        });
        void Add(List<string> list, string value, int max = 64) { if (value != "" && !list.Contains(value) && list.Count < max) list.Add(value); }

        switch (cmd)
        {
            // ---------------------------------------------------------------- описание пакета
            case "id":
                Need(2);
                if (!Regex.IsMatch(S(1), @"^[A-Za-z0-9_.-]{3,80}$")) throw new ScriptError("err.id");
                b.UniqueIdOverride = S(1);
                return true;
            case "tags":
                Need(2);
                for (var i = 1; i < t.Count; i++) Add(b.Tags, S(i).Trim().ToLowerInvariant(), 12);
                return true;
            case "license":
                Need(2);
                b.License = S(1);
                return true;
            case "update_key":
                Need(2);
                for (var i = 1; i < t.Count; i++)
                {
                    var key = S(i);
                    if (!Regex.IsMatch(key, @"^(Nexus|GitHub|ModDrop|CurseForge|Chucklefish):\S+$", RegexOptions.IgnoreCase)) d.Add(new Diag(line, "err.updateKey|" + key, Warning: true));
                    Add(b.UpdateKeys, key);
                }
                return true;
            case "min_api":
                Need(2);
                if (!Regex.IsMatch(S(1), @"^\d+\.\d+(\.\d+)?$")) throw new ScriptError("err.version");
                b.MinApi = S(1);
                return true;
            case "changelog":
                Need(3);
                b.Changelog.Add((S(1), string.Join(" ", Enumerable.Range(2, t.Count - 2).Select(S))));
                return true;
            case "readme":
                Need(2);
                b.Readme.Add(Rest(1));
                return true;
            case "credits":
                Need(2);
                for (var i = 1; i < t.Count; i++) Add(b.Credits, S(i));
                return true;
            case "optional":
                Need(2);
                for (var i = 1; i < t.Count; i++) Add(b.Optional, S(i));
                return true;

            // ---------------------------------------------------------------- настройки для игрока и токены
            case "option":
            {
                // option Difficulty = normal easy normal hard — первое после «=» по умолчанию, дальше — какие можно.
                if (Eq() != 2 || t.Count < 4 || !Regex.IsMatch(t[1].Text, @"^[A-Za-z]\w*$")) throw new ScriptError("err.option");
                var name = t[1].Text;
                var def = S(3);
                var allowed = Enumerable.Range(4, t.Count - 4).Select(S).Where(x => x != "").Distinct().ToList();
                if (allowed.Count > 0 && !allowed.Contains(def)) allowed.Insert(0, def);
                if (allowed.Count == 0 && def is "true" or "false") allowed = ["true", "false"];
                var schema = new JsonObject { ["Default"] = def };
                if (allowed.Count > 0) schema["AllowValues"] = string.Join(", ", allowed);
                b.Options.RemoveAll(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                b.Options.Add((name, schema));
                // $Difficulty в правках — токен Content Patcher: игрок поменяет настройку, правка поменяется сама.
                env.Vars[name] = "{{" + name + "}}";
                return true;
            }
            case "token":
            {
                // token Bonus = 2 (внутри when — со своим условием)
                if (Eq() != 2 || !Regex.IsMatch(t[1].Text, @"^[A-Za-z]\w*$")) throw new ScriptError("err.token");
                var token = new JsonObject { ["Name"] = t[1].Text, ["Value"] = Rest(3) };
                if (env.When.Count > 0)
                {
                    var when = new JsonObject();
                    foreach (var (k, v) in env.When) when[k] = v;
                    token["When"] = when;
                }
                b.Tokens.Add(token);
                env.Vars[t[1].Text] = "{{" + t[1].Text + "}}";
                return true;
            }

            // ---------------------------------------------------------------- данные Stardew Valley
            case "remove":
                // remove "Data/Objects" "472" — запись удаляется (null в Entries)
                if (t.Count != 3) throw new ScriptError("err.remove");
                Edit(S(1), S(2), null);
                return true;
            case "recipe" or "craft" or "gift" or "quest":
            {
                // recipe "Ягодный пирог" = "296 1 246 1/1 10/..."
                if (Eq() != 2) throw new ScriptError("err.entry2|" + cmd);
                var target = cmd switch { "recipe" => "Data/CookingRecipes", "craft" => "Data/CraftingRecipes", "gift" => "Data/NPCGiftTastes", _ => "Data/Quests" };
                Edit(target, S(1), Json(Rest(3)));
                return true;
            }
            case "event" or "schedule":
            {
                // event "Town" "90010001/t 600 1200" = "сценарий…"; schedule "Abigail" "spring" = "900 Town 10 10 2/…"
                if (Eq() != 3) throw new ScriptError("err.entry3|" + cmd);
                Edit(cmd == "event" ? $"Data/Events/{S(1)}" : $"Characters/schedules/{S(1)}", S(2), Json(Rest(4)));
                return true;
            }
            case "trigger":
            {
                // trigger "MyMod_Bonus" "DayStarted" = "AddMoney 500" "AddMail Current MyLetter"
                if (Eq() != 3 || t.Count < 5) throw new ScriptError("err.trigger");
                var id = S(1);
                var actions = new JsonArray(t.Skip(4).Select(x => (JsonNode?)JsonValue.Create(Subst(x.Text, env, line, d))).ToArray());
                Edit("Data/TriggerActions", id, new JsonObject { ["Id"] = id, ["Trigger"] = S(2), ["Actions"] = actions });
                return true;
            }
            case "item":
            {
                // item "MyMod_Gem" name "Звёздный камень" price 500 about "Блестит" category -2 texture "Mods/MyMod/Items" sprite 0
                if (t.Count < 2 || (t.Count - 2) % 2 != 0) throw new ScriptError("err.item");
                var id = S(1);
                var item = new JsonObject { ["Name"] = id, ["DisplayName"] = id, ["Description"] = "", ["Type"] = "Basic", ["Category"] = 0, ["Price"] = 0 };
                for (var i = 2; i + 1 < t.Count; i += 2)
                {
                    var v = S(i + 1);
                    switch (t[i].Text.ToLowerInvariant())
                    {
                        case "name": item["DisplayName"] = v; break;
                        case "about" or "description": item["Description"] = v; break;
                        case "price": item["Price"] = Int(v, "err.item"); break;
                        case "category": item["Category"] = Int(v, "err.item"); break;
                        case "edible" or "edibility": item["Edibility"] = Int(v, "err.item"); break;
                        case "type": item["Type"] = v; break;
                        case "texture": item["Texture"] = v; break;
                        case "sprite": item["SpriteIndex"] = Int(v, "err.item"); break;
                        default: throw new ScriptError("err.itemKey|" + t[i].Text);
                    }
                }
                Edit("Data/Objects", id, item);
                return true;
            }
            case "shop":
            {
                // shop "SeedShop" sells "(O)472" price 20
                if (t.Count is not (4 or 6) || t[2].Text != "sells" || t.Count == 6 && t[4].Text != "price") throw new ScriptError("err.shop");
                var item = S(3);
                var id = "ModLaunch_" + Regex.Replace(b.Name + "_" + item, @"[^\w]", "");
                var entry = new JsonObject { ["Id"] = id, ["ItemId"] = item };
                if (t.Count == 6) entry["Price"] = Int(S(5), "err.shop");
                AddChange(b, env, new JsonObject
                {
                    ["Action"] = "EditData", ["Target"] = "Data/Shops", ["TargetField"] = new JsonArray(S(1), "Items"),
                    ["Entries"] = new JsonObject { [id] = entry },
                });
                return true;
            }

            // ---------------------------------------------------------------- карты, картинки, звуки
            case "map":
                // map "Maps/MyCave" from "cave.tmx"
                if (t.Count != 4 || t[2].Text != "from") throw new ScriptError("err.map");
                AddChange(b, env, new JsonObject { ["Action"] = "Load", ["Target"] = S(1), ["FromFile"] = Asset(b, S(3)) });
                return true;
            case "sound":
            {
                // sound "MyMod_Ding" from "ding.ogg" — новый звук в Data/AudioChanges
                if (t.Count != 4 || t[2].Text != "from") throw new ScriptError("err.sound");
                var asset = Asset(b, S(3));
                var id = S(1);
                Edit("Data/AudioChanges", id, new JsonObject
                {
                    ["Id"] = id, ["Category"] = "Sound", ["FilePaths"] = new JsonArray(JsonValue.Create($"{{{{AbsoluteFilePath: {asset}}}}}")),
                    ["StreamedVorbis"] = false, ["Looped"] = false,
                });
                return true;
            }
            case "patch_image" or "patch_map":
            {
                // patch_image "Maps/springobjects" from "fix.png" at 0 0 16 16 — поверх части картинки (или карты)
                if (t.Count < 4 || t[2].Text != "from" || t.Count != 4 && (t.Count != 9 || t[4].Text != "at")) throw new ScriptError("err.patch|" + cmd);
                var change = new JsonObject { ["Action"] = cmd == "patch_image" ? "EditImage" : "EditMap", ["Target"] = S(1), ["FromFile"] = Asset(b, S(3)) };
                if (t.Count == 9)
                    change["ToArea"] = new JsonObject
                    {
                        ["X"] = Int(S(5), "err.patch|" + cmd), ["Y"] = Int(S(6), "err.patch|" + cmd),
                        ["Width"] = Int(S(7), "err.patch|" + cmd), ["Height"] = Int(S(8), "err.patch|" + cmd),
                    };
                AddChange(b, env, change);
                return true;
            }
            case "warp":
            {
                // warp "Farm" 64 15 "Town" 0 54
                if (t.Count != 7) throw new ScriptError("err.warp");
                var from = S(1);
                var warp = $"{Int(S(2), "err.warp")} {Int(S(3), "err.warp")} {S(4)} {Int(S(5), "err.warp")} {Int(S(6), "err.warp")}";
                AddChange(b, env, new JsonObject
                {
                    ["Action"] = "EditMap", ["Target"] = from.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? from : "Maps/" + from,
                    ["AddWarps"] = new JsonArray(JsonValue.Create(warp)),
                });
                return true;
            }
            case "include":
            {
                // include "more.json" — ещё один файл правок Content Patcher из папки проекта
                Need(2);
                var file = SafePath(S(1));
                if (!b.Copies.Any(c => c.To == file)) b.Copies.Add(new FileCopy(file, file));
                AddChange(b, env, new JsonObject { ["Action"] = "Include", ["FromFile"] = file });
                return true;
            }

            // ---------------------------------------------------------------- файлы
            case "append":
            {
                // append "plugins/MyMod/log.txt" = "строка" — дописать строку в файл (создаётся, если его нет)
                if (Eq() != 2) throw new ScriptError("err.append");
                var path = SafePath(S(1));
                var text = Rest(3);
                if (text.StartsWith(JsonMark)) text = text[JsonMark.Length..];
                var at = b.Writes.FindIndex(w => w.Path == path);
                if (at >= 0) b.Writes[at] = (path, b.Writes[at].Text + text + "\n");
                else b.Writes.Add((path, text + "\n"));
                return true;
            }
            case "mkdir":
            {
                Need(2);
                var path = SafePath(S(1)).TrimEnd('/') + "/.keep";
                if (!b.Writes.Any(w => w.Path == path)) b.Writes.Add((path, ""));
                return true;
            }
            case "jsonset":
            {
                // jsonset "config/settings.json" "graphics.fov" = 90 — ключ в JSON-файле пакета (вложенные — через точку)
                if (Eq() != 3) throw new ScriptError("err.jsonset");
                var path = SafePath(S(1));
                var keys = S(2).Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (keys.Length == 0) throw new ScriptError("err.jsonset");
                var at = b.Writes.FindIndex(w => w.Path == path);
                JsonObject root;
                try { root = at >= 0 ? JsonNode.Parse(b.Writes[at].Text) as JsonObject ?? [] : []; }
                catch (JsonException) { throw new ScriptError("err.json"); }
                var node = root;
                foreach (var key in keys[..^1])
                {
                    if (node[key] is not JsonObject child) node[key] = child = [];
                    node = child;
                }
                node[keys[^1]] = Json(Rest(4));
                var text = root.ToJsonString(JsonPretty);
                if (at >= 0) b.Writes[at] = (path, text);
                else b.Writes.Add((path, text));
                return true;
            }

            // ---------------------------------------------------------------- язык
            case "repeat":
            {
                // repeat 5 { … } — номер круга в $i; repeat 5 as k { … } — в $k
                if (n.Body is null) throw new ScriptError("err.repeatBlock");
                if (t.Count is not (2 or 4) || t.Count == 4 && t[2].Text != "as") throw new ScriptError("err.repeat");
                var times = Int(S(1), "err.repeat");
                if (times < 0) throw new ScriptError("err.repeat");
                if (times > 10000) throw new ScriptError("err.loop");
                var name = t.Count == 4 ? t[3].Text : "i";
                var had = env.Vars.TryGetValue(name, out var old);
                for (var k = 0; k < times; k++)
                {
                    env.Vars[name] = k.ToString(CultureInfo.InvariantCulture);
                    try { Run(n.Body, env, b); }
                    catch (BreakSignal) { break; }
                    catch (ContinueSignal) { }
                }
                if (had) env.Vars[name] = old!; else env.Vars.Remove(name);
                return true;
            }
            case "switch":
            {
                // switch $season { case spring summer { … } default { … } }
                if (n.Body is null) throw new ScriptError("err.switchBlock");
                Need(2);
                var value = Rest(1);
                List<Node>? fallback = null;
                List<Node>? hit = null;
                foreach (var c in n.Body)
                {
                    var head = c.Toks[0].Text.ToLowerInvariant();
                    if (c.Body is null || head is not ("case" or "default")) { d.Add(new Diag(c.Line, "err.case")); continue; }
                    if (head == "default") { fallback = c.Body; continue; }
                    if (hit is null && c.Toks.Skip(1).Any(x => Same(Subst(x.Text, env, c.Line, d), value))) hit = c.Body;
                }
                if ((hit ?? fallback) is { } run)
                {
                    try { Run(run, env, b); }
                    catch (BreakSignal) { }
                }
                return true;
            }
            case "case" or "default":
                throw new ScriptError("err.case");
            case "inc" or "dec":
            {
                // inc score / dec lives 2 — нет переменной — считаем от нуля
                Need(2);
                var name = t[1].Text;
                if (!Regex.IsMatch(name, @"^[A-Za-z_]\w*$")) throw new ScriptError("err.args|" + cmd);
                var by = t.Count > 2 ? D(FromText(Rest(2))) : 1;
                var now = env.Vars.TryGetValue(name, out var cur) && cur != "" ? D(FromText(cur)) : 0;
                env.Vars[name] = Num(cmd == "inc" ? now + by : now - by);
                return true;
            }
            case "push":
            {
                // push items = "меч" / push items "меч"
                Need(3);
                var list = ListVar(env, t[1].Text);
                if (list.Count >= 10000) throw new ScriptError("err.tooMuch");
                list.Add(FromText(Rest(t[2] is { Kind: T.Sym, Text: "=" } ? 3 : 2)));
                env.Vars[t[1].Text] = ToText(list);
                return true;
            }
            case "pop":
            {
                // pop items / pop items into last
                if (t.Count is not (2 or 4) || t.Count == 4 && t[2].Text != "into") throw new ScriptError("err.pop");
                var list = ListVar(env, t[1].Text);
                if (list.Count == 0) throw new ScriptError("err.empty|" + t[1].Text);
                var item = list[^1];
                list.RemoveAt(list.Count - 1);
                env.Vars[t[1].Text] = ToText(list);
                if (t.Count == 4) env.Vars[t[3].Text] = ToText(item);
                return true;
            }
            case "put":
            {
                // put prices "472" = 20
                if (Eq() != 3) throw new ScriptError("err.put");
                var map = DictVar(env, t[1].Text);
                map[S(2)] = FromText(Rest(4));
                env.Vars[t[1].Text] = ToText(map);
                return true;
            }
            case "del":
            {
                // del x / del prices "472"
                Need(2);
                if (t.Count == 2) { env.Vars.Remove(t[1].Text); return true; }
                var map = DictVar(env, t[1].Text);
                map.Remove(S(2));
                env.Vars[t[1].Text] = ToText(map);
                return true;
            }
            case "swap":
            {
                if (t.Count != 3) throw new ScriptError("err.args|" + cmd);
                var a = env.Vars.GetValueOrDefault(t[1].Text) ?? throw new ScriptError("err.unknownVar|" + t[1].Text);
                var c = env.Vars.GetValueOrDefault(t[2].Text) ?? throw new ScriptError("err.unknownVar|" + t[2].Text);
                env.Vars[t[1].Text] = c;
                env.Vars[t[2].Text] = a;
                return true;
            }
            case "assert":
            {
                // assert $price > 0 "цена должна быть больше нуля"
                Need(2);
                var message = t.Count > 2 && t[^1].Kind == T.Str ? Subst(t[^1].Text, env, line, d) : null;
                var cond = t.Skip(1).Take(t.Count - (message is null ? 1 : 2)).ToList();
                if (!Cond(cond, env, line, d)) d.Add(new Diag(line, "err.assert|" + (message ?? string.Join(" ", cond.Select(x => x.Text)))));
                return true;
            }
            case "warn" or "error":
                Need(2);
                d.Add(new Diag(line, "err.user|" + Rest(1), Warning: cmd == "warn"));
                return true;
            case "stop":
                throw new StopSignal();
            case "debug":
            {
                // debug price → «price = 20 (number)»
                Need(2);
                for (var i = 1; i < t.Count; i++)
                {
                    var name = t[i].Text.TrimStart('$');
                    var v = env.Vars.TryGetValue(name, out var cur) ? FromText(cur) : null;
                    var type = v switch { double => "number", string => "string", bool => "bool", List<object?> => "list", Dictionary<string, object?> => "dict", _ => "none" };
                    b.Log.Add($"{line}: {name} = {(cur is null ? "—" : ToText(v))} ({type})");
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>Совпадение для case: числа — по значению (1 и 1.0), остальное — как строки.</summary>
    static bool Same(string a, string b) =>
        a == b || double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && x == y;
}
