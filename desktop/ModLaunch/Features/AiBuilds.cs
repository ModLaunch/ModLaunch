using System.Text.Json;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>Мод в ИИ-сборке: что это и почему ИИ его взял.</summary>
public sealed record AiPick(ModInfo Mod, string Why);

/// <summary>Готовая ИИ-сборка (ai-builds.json): игра, запрос, стиль и моды.</summary>
public sealed record AiBuild(string Id, string Game, string Name, string Prompt, string Style, string Engine, string Note, DateTime CreatedAt, List<AiPick> Picks, int Likes = 0);

/// <summary>
/// 9.8: ИИ-сборки. Запрос словами («красивая графика, без читов, для кооператива») —
/// и сборка модов из каталога игры. С ключом Claude (Настройки → Сервисы, поле «Ключ ИИ»)
/// моды выбирает модель Claude; без ключа — встроенный подборщик ModLaunch: он ищет по
/// словам запроса и стилю, смотрит на популярность и оценки и добирает зависимости.
/// Выбор в обоих случаях — только из настоящего каталога, выдуманных модов не бывает.
/// </summary>
public static class AiBuilds
{
    const string Model = "claude-sonnet-5-5";
    const int Max = 40;
    static readonly JsonFile File = new(Path.Combine(Paths.DataDir, "ai-builds.json"), () => new JsonObject { ["items"] = new JsonArray() });

    public static string? Key => Settings.Data.Str("aiKey") is { Length: > 10 } k ? k : null;
    public static bool HasClaude => Key is not null;

    /// <summary>Стили-заготовки: id и слова, которые они добавляют к поиску.</summary>
    public static readonly (string Id, string[] Words)[] Styles =
    [
        ("vanilla", ["qol", "quality", "fix", "tweak", "ui", "utility", "map", "inventory"]),
        ("beauty", ["graphics", "texture", "lighting", "visual", "hd", "shader", "weather", "model"]),
        ("hardcore", ["difficulty", "hard", "survival", "realism", "enemy", "combat", "challenge"]),
        ("coop", ["multiplayer", "coop", "co-op", "player", "lobby", "server", "friends"]),
        ("content", ["content", "items", "weapon", "biome", "creature", "new", "expansion", "quest"]),
        ("speed", ["performance", "fps", "optimization", "fast", "lag", "load"]),
    ];

    // Русские слова запроса → английские слова каталогов.
    static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["графика"] = ["graphics", "visual", "texture"], ["красив"] = ["graphics", "visual", "hd"], ["свет"] = ["lighting"], ["текстур"] = ["texture", "hd"],
        ["интерфейс"] = ["ui", "hud"], ["удобн"] = ["qol", "quality", "utility"], ["карт"] = ["map", "minimap"], ["инвентар"] = ["inventory", "storage"],
        ["хардкор"] = ["hard", "difficulty", "survival"], ["сложн"] = ["difficulty", "hard"], ["реализм"] = ["realism", "realistic"], ["выживан"] = ["survival"],
        ["кооп"] = ["coop", "multiplayer"], ["друз"] = ["multiplayer", "coop"], ["сетев"] = ["multiplayer", "server"], ["мультиплеер"] = ["multiplayer"],
        ["оруж"] = ["weapon", "gun"], ["предмет"] = ["items"], ["монстр"] = ["creature", "enemy", "monster"], ["враг"] = ["enemy"], ["квест"] = ["quest"],
        ["оптимиз"] = ["performance", "fps", "optimization"], ["fps"] = ["performance", "fps"], ["лаг"] = ["performance", "lag"], ["быстр"] = ["fast", "performance"],
        ["звук"] = ["sound", "audio", "music"], ["музык"] = ["music"], ["смешн"] = ["funny", "meme"], ["мем"] = ["meme", "funny"], ["строит"] = ["building", "build"],
        ["стро"] = ["building"], ["баз"] = ["base", "building"], ["транспорт"] = ["vehicle"], ["машин"] = ["vehicle", "car"], ["косметик"] = ["cosmetic", "skin", "suit"],
    };

    static readonly string[] Cheats = ["cheat", "god mode", "infinite", "unlock all", "trainer", "чит"];

    public static List<AiBuild> All() => File.Data.Arr("items").OfType<JsonObject>().Select(Read).OfType<AiBuild>().OrderByDescending(b => b.CreatedAt).ToList();

    public static void Save(AiBuild build)
    {
        var rest = File.Data.Arr("items").OfType<JsonObject>().Where(o => o.Str("id") != build.Id).Select(o => (JsonNode)o.DeepClone()).ToList();
        rest.Insert(0, Write(build));
        File.Data["items"] = new JsonArray(rest.Take(Max).ToArray());
        File.Save();
    }

    public static void Remove(string id)
    {
        File.Data["items"] = new JsonArray(File.Data.Arr("items").OfType<JsonObject>().Where(o => o.Str("id") != id).Select(o => (JsonNode)o.DeepClone()).ToArray());
        File.Save();
    }

    /// <summary>«Нравится» как в Steam: у своей сборки — отметка для порядка и витрины.</summary>
    public static AiBuild Like(AiBuild b) { var n = b with { Likes = b.Likes > 0 ? 0 : 1 }; Save(n); return n; }

    /// <summary>Сборка в файл .modhub.json — открывается в любой ModLaunch («Открыть сборку»).</summary>
    public static string Export(AiBuild b)
    {
        var mods = b.Picks.Select(p => (JsonNode)new JsonObject
        {
            ["id"] = p.Mod.Id, ["name"] = p.Mod.Name, ["version"] = p.Mod.Version, ["source"] = p.Mod.Source, ["url"] = p.Mod.Url, ["enabled"] = true,
        }).ToArray();
        return new JsonObject
        {
            ["format"] = "modhub-pack", ["version"] = 1, ["app"] = Http.Version, ["game"] = b.Game,
            ["gameName"] = AppState.Games.FirstOrDefault(g => g.Def.Id == b.Game)?.Def.Name ?? b.Game,
            ["name"] = b.Name, ["createdAt"] = b.CreatedAt.ToString("o"), ["ai"] = b.Prompt, ["mods"] = new JsonArray(mods),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Собрать: кандидаты из каталога → Claude (если есть ключ) или встроенный подборщик.</summary>
    public static async Task<AiBuild> Generate(GameState g, string prompt, string style, int size, bool noCheats, CancellationToken ct)
    {
        prompt = prompt.Trim();
        if (prompt.Length > 600) prompt = prompt[..600];
        var words = Words(prompt, style);
        var pool = await Candidates(g, words, ct);
        if (noCheats) pool = pool.Where(m => !Cheats.Any(c => (m.Name + " " + m.Description).Contains(c, StringComparison.OrdinalIgnoreCase))).ToList();
        if (pool.Count == 0) throw new InvalidOperationException(I18n.T("v98.ai.err.empty"));
        if (HasClaude && !Program.Demo)
        {
            try { return await AskClaude(g, prompt, style, size, pool, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* без сети или ключ не подошёл — подберём сами */ }
        }
        return Local(g, prompt, style, size, words, pool);
    }

    static List<string> Words(string prompt, string style)
    {
        var words = new List<string>();
        foreach (var raw in prompt.ToLowerInvariant().Split(new[] {' ', ',', '.', ';', '!', '?', '\n', '\r', '\t', '/', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 3) continue;
            var hit = Synonyms.Where(kv => raw.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)).SelectMany(kv => kv.Value).ToList();
            if (hit.Count > 0) words.AddRange(hit);
            else if (raw.All(c => c < 128)) words.Add(raw);
        }
        if (Styles.FirstOrDefault(s => s.Id == style) is { Id: not null } st) words.AddRange(st.Words);
        return words.Distinct().Take(24).ToList();
    }

    static async Task<List<ModInfo>> Candidates(GameState g, List<string> words, CancellationToken ct)
    {
        if (Program.Demo) return Demo.Catalog(g.Def, new Query()).Mods;
        var found = new Dictionary<string, ModInfo>();
        async Task Take(Query q)
        {
            try { foreach (var m in (await Catalog.Browse(g.Def, q, ct)).Mods.Where(m => !m.Adult && !m.Deprecated)) found.TryAdd(m.Id, m); }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        await Take(new Query(Sort: SortBy.Popular));
        await Take(new Query(Page: 2, Sort: SortBy.Popular));
        await Take(new Query(Sort: SortBy.Rating));
        foreach (var w in words.Take(5)) await Take(new Query(Text: w));
        return found.Values.ToList();
    }

    static double Score(ModInfo m, List<string> words)
    {
        var text = (m.Name + " " + m.Description + " " + string.Join(' ', m.Categories)).ToLowerInvariant();
        var hits = words.Count(w => text.Contains(w, StringComparison.Ordinal));
        return hits * 3 + Math.Log10(m.Downloads + 10) + Math.Log10(m.Rating + 10) * 0.6;
    }

    static AiBuild Local(GameState g, string prompt, string style, int size, List<string> words, List<ModInfo> pool)
    {
        var ranked = pool.Select(m => (Mod: m, Score: Score(m, words), Hit: words.FirstOrDefault(w => (m.Name + " " + m.Description).Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(x => x.Score).ToList();
        var picks = new List<AiPick>();
        foreach (var (m, _, hit) in ranked)
        {
            if (picks.Count >= size) break;
            var why = hit is not null ? I18n.T("v98.ai.why.match", ("word", hit)) : m.Downloads > 0 ? I18n.T("v98.ai.why.popular", ("n", Short(m.Downloads))) : I18n.T("v98.ai.why.rated");
            picks.Add(new AiPick(m, why));
        }
        // Зависимости, которые есть в пуле, — в сборку, чтобы всё запустилось.
        foreach (var dep in picks.SelectMany(p => p.Mod.Dependencies).Select(Recommend.Bare).Distinct().ToList())
            if (pool.FirstOrDefault(m => Recommend.Bare(m.Id) == dep) is { } d && picks.All(p => p.Mod.Id != d.Id))
                picks.Add(new AiPick(d, I18n.T("v98.ai.why.dep")));
        var name = Title(prompt, style, g);
        return new AiBuild(Guid.NewGuid().ToString("N")[..10], g.Def.Id, name, prompt, style, "local", I18n.T("v98.ai.note.local", ("n", pool.Count)), DateTime.UtcNow, picks);
    }

    static string Short(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();

    static string Title(string prompt, string style, GameState g)
    {
        if (prompt.Length is > 0 and <= 32) return char.ToUpper(prompt[0]) + prompt[1..];
        return $"{g.Def.Name} · {I18n.T("v98.ai.style." + style)}";
    }

    static async Task<AiBuild> AskClaude(GameState g, string prompt, string style, int size, List<ModInfo> pool, CancellationToken ct)
    {
        var list = string.Join('\n', pool.Take(120).Select((m, i) =>
            $"{i}. {m.Name} | {Clip(m.Description, 140)} | tags: {string.Join(',', m.Categories.Take(4))} | downloads: {m.Downloads}"));
        var user = $"Game: {g.Def.Name}\nStyle: {style}\nPlayer request: {prompt}\nPick up to {size} mods that work well together.\n\nCatalog:\n{list}\n\n" +
                   "Answer with JSON only: {\"name\": \"short build name in the player's language\", \"note\": \"one sentence about the build in the player's language\", " +
                   "\"mods\": [{\"i\": catalog number, \"why\": \"short reason in the player's language\"}]}";
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 1500,
            ["system"] = "You build mod lists for a game mod launcher. Only choose mods from the given catalog. Avoid cheats, conflicting duplicates and adult content. Reply with JSON only.",
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        var answer = await Http.PostJson("https://api.anthropic.com/v1/messages", body,
            new Dictionary<string, string> { ["x-api-key"] = Key!, ["anthropic-version"] = "2023-06-01" }, ct, 90);
        var text = answer?["content"] is JsonArray parts ? string.Concat(parts.Select(p => p.Str("text"))) : "";
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("no json");
        var json = JsonNode.Parse(text[start..(end + 1)]);
        var picks = new List<AiPick>();
        foreach (var item in json.Arr("mods"))
            if (item?["i"]?.GetValue<int>() is int i && i >= 0 && i < pool.Count && picks.All(p => p.Mod.Id != pool[i].Id))
                picks.Add(new AiPick(pool[i], Clip(item.Str("why") ?? "", 120)));
        if (picks.Count == 0) throw new InvalidDataException("empty");
        var name = Clip(json.Str("name") ?? "", 40);
        return new AiBuild(Guid.NewGuid().ToString("N")[..10], g.Def.Id, name == "" ? Title(prompt, style, g) : name, prompt, style, "claude",
            Clip(json.Str("note") ?? "", 200), DateTime.UtcNow, picks.Take(size).ToList());
    }

    static string Clip(string s, int max) { s = s.Replace('\n', ' ').Replace('|', '/').Trim(); return s.Length > max ? s[..max] + "…" : s; }

    static JsonObject Write(AiBuild b) => new()
    {
        ["id"] = b.Id, ["game"] = b.Game, ["name"] = b.Name, ["prompt"] = b.Prompt, ["style"] = b.Style, ["engine"] = b.Engine, ["note"] = b.Note,
        ["at"] = b.CreatedAt.ToString("o"), ["likes"] = b.Likes,
        ["mods"] = new JsonArray(b.Picks.Select(p => (JsonNode)new JsonObject
        {
            ["source"] = p.Mod.Source, ["id"] = p.Mod.Id, ["name"] = p.Mod.Name, ["author"] = p.Mod.Author, ["version"] = p.Mod.Version,
            ["icon"] = p.Mod.Icon, ["url"] = p.Mod.Url, ["downloads"] = p.Mod.Downloads, ["why"] = p.Why,
        }).ToArray()),
    };

    static AiBuild? Read(JsonObject o)
    {
        if (o.Str("id") is not { } id || o.Str("game") is not { } game) return null;
        var picks = o.Arr("mods").OfType<JsonObject>().Where(m => m.Str("id") is not null).Select(m => new AiPick(new ModInfo
        {
            Source = m.Str("source") ?? "thunderstore", Id = m.Str("id")!, Name = m.Str("name") ?? m.Str("id")!, Author = m.Str("author") ?? "",
            Version = m.Str("version") ?? "", Icon = m.Str("icon"), Url = m.Str("url"), Downloads = m.Long("downloads"),
        }, m.Str("why") ?? "")).ToList();
        return new AiBuild(id, game, o.Str("name") ?? "", o.Str("prompt") ?? "", o.Str("style") ?? "vanilla", o.Str("engine") ?? "local", o.Str("note") ?? "",
            DateTime.TryParse(o.Str("at"), out var d) ? d.ToUniversalTime() : DateTime.UtcNow, picks, (int)o.Long("likes"));
    }
}
