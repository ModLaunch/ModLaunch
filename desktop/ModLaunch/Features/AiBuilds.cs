using System.Text.Json;
using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Sources;

namespace ModLaunch.Features;

/// <summary>Мод в ИИ-сборке: что это, почему ИИ его взял и насколько он подходит к запросу (16.0: 0–100).</summary>
public sealed record AiPick(ModInfo Mod, string Why, int Match = 0);

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
        // 16.0: больше слов — запрос «как другу» понимается чаще.
        ["скин"] = ["skin", "cosmetic", "suit"], ["одежд"] = ["clothes", "outfit", "cosmetic"], ["брон"] = ["armor", "armour"], ["маг"] = ["magic", "spell"],
        ["заклин"] = ["spell", "magic"], ["животн"] = ["animal", "creature", "pet"], ["питом"] = ["pet", "companion"], ["спутник"] = ["companion", "follower"],
        ["рыб"] = ["fishing", "fish"], ["ферм"] = ["farm", "farming", "crop"], ["крафт"] = ["crafting", "craft", "recipe"], ["рецепт"] = ["recipe", "crafting"],
        ["торг"] = ["trade", "shop", "economy"], ["магазин"] = ["shop", "store"], ["экономик"] = ["economy", "money"], ["деньг"] = ["money", "economy"],
        ["сюжет"] = ["story", "quest"], ["истор"] = ["story", "lore"], ["диалог"] = ["dialogue", "dialog"], ["персонаж"] = ["character", "npc"],
        ["нпс"] = ["npc"], ["бот"] = ["ai", "bot", "npc"], ["камер"] = ["camera"], ["ноч"] = ["night", "darkness"], ["погод"] = ["weather"],
        ["перевод"] = ["translation", "russian", "localization"], ["русск"] = ["russian", "translation"], ["голос"] = ["voice"], ["атмосфер"] = ["atmosphere", "ambient", "lighting"],
        ["страш"] = ["horror", "scary"], ["хоррор"] = ["horror"], ["весел"] = ["fun", "funny"], ["мир"] = ["world", "biome"], ["локац"] = ["location", "map", "biome"],
        ["хардмод"] = ["hard", "difficulty"], ["легк"] = ["easy", "casual"], ["прост"] = ["easy", "simple", "qol"], ["автомат"] = ["auto", "automation"],
        ["сохран"] = ["save", "autosave"], ["меню"] = ["menu", "ui"], ["худ"] = ["hud", "ui"], ["миникарт"] = ["minimap", "map"], ["стабил"] = ["fix", "stability", "bugfix"],
        ["баг"] = ["fix", "bugfix", "patch"], ["фикс"] = ["fix", "bugfix", "patch"], ["лут"] = ["loot", "items"], ["сундук"] = ["chest", "storage"], ["инструм"] = ["tool", "tools"],
        ["эмоц"] = ["emote", "emotes"], ["танц"] = ["emote", "dance"], ["игрок"] = ["player", "multiplayer"], ["лобби"] = ["lobby", "player"],
    };

    /// <summary>16.0: слова, после которых идёт то, чего в сборке быть не должно («без читов», «no graphics», «убери звук»).</summary>
    static readonly string[] Negators = ["без", "не", "нет", "убери", "убрать", "кроме", "no", "not", "without", "remove", "except"];

    /// <summary>
    /// 16.0: семейства модов, которые делают одно и то же — в сборку по одному (две мини-карты или два «больше игроков» спорят).
    /// </summary>
    static readonly string[][] Families =
    [
        ["minimap", "mini map", "mini-map"], ["morecompany", "more company", "more players", "biggerlobby", "bigger lobby", "lobby size", "max players"],
        ["skin", "suit", "outfit"] , ["reshade", "enb", "preset"], ["hd texture", "texture pack", "retexture"], ["fps unlock", "unlock fps", "framerate"],
    ];

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

    /// <summary>16.0: что понято из запроса — что искать (Want) и чего в сборке быть не должно (Avoid).</summary>
    public sealed record Intent(List<string> Want, List<string> Avoid);

    /// <summary>Последний набор кандидатов по игре — для «Похожих» и замены мода без нового запроса к каталогу.</summary>
    static readonly Dictionary<string, List<ModInfo>> Pools = [];

    /// <summary>Собрать: кандидаты из каталога → Claude (если есть ключ) или встроенный подборщик.</summary>
    public static async Task<AiBuild> Generate(GameState g, string prompt, string style, int size, bool noCheats, CancellationToken ct)
    {
        prompt = prompt.Trim();
        if (prompt.Length > 600) prompt = prompt[..600];
        var intent = Understand(prompt, style);
        var pool = await Pool(g, intent, noCheats, ct);
        if (HasClaude && !Program.Demo)
        {
            try { return await AskClaude(g, prompt, style, size, intent, pool, null, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* без сети или ключ не подошёл — подберём сами */ }
        }
        return Local(g, prompt, style, size, intent, pool, []);
    }

    /// <summary>
    /// 16.0: «Уточнить» — поправить готовую сборку словами («больше графики», «убери звук», «меньше модов»).
    /// Что подходит — остаётся, что запрещено — уходит, остальное добирается из каталога. Id сборки тот же.
    /// </summary>
    public static async Task<AiBuild> Refine(GameState g, AiBuild b, string instruction, int size, bool noCheats, CancellationToken ct)
    {
        instruction = instruction.Trim();
        if (instruction.Length > 300) instruction = instruction[..300];
        var lower = instruction.ToLowerInvariant();
        if (lower.Contains("меньше") || lower.Contains("fewer") || lower.Contains("less")) size = Math.Max(3, b.Picks.Count - 3);
        else if (lower.Contains("больше мод") || lower.Contains("more mods") || lower.Contains("ещё") || lower.Contains("еще")) size = b.Picks.Count + 5;
        else size = Math.Max(size, b.Picks.Count);
        var basis = Understand(b.Prompt, b.Style);
        var extra = Understand(instruction, "-");
        var intent = new Intent([.. extra.Want, .. basis.Want.Where(w => !extra.Avoid.Contains(w))], [.. basis.Avoid.Where(w => !extra.Want.Contains(w)), .. extra.Avoid]);
        var pool = await Pool(g, intent, noCheats, ct);
        foreach (var p in b.Picks) if (pool.All(m => m.Id != p.Mod.Id)) pool.Add(p.Mod);
        var prompt = b.Prompt == "" ? instruction : b.Prompt + " · " + instruction;
        AiBuild next;
        if (HasClaude && !Program.Demo)
        {
            try { next = await AskClaude(g, prompt, b.Style, size, intent, pool, b, ct); return next with { Id = b.Id, Likes = b.Likes }; }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        // Свои: моды прошлой сборки, которые не попали под «без …», остаются первыми.
        var keep = b.Picks.Where(p => !Avoided(p.Mod, intent)).ToList();
        next = Local(g, prompt, b.Style, size, intent, pool, keep);
        return next with { Id = b.Id, Name = b.Name, Likes = b.Likes };
    }

    /// <summary>Кандидаты: каталог игры по популярности, оценке и словам запроса; без читов — если просили.</summary>
    static async Task<List<ModInfo>> Pool(GameState g, Intent intent, bool noCheats, CancellationToken ct)
    {
        var pool = await Candidates(g, intent.Want, ct);
        if (noCheats) pool = pool.Where(m => !Cheats.Any(c => (m.Name + " " + m.Description).Contains(c, StringComparison.OrdinalIgnoreCase))).ToList();
        if (pool.Count == 0) throw new InvalidOperationException(I18n.T("v98.ai.err.empty"));
        lock (Pools) Pools[g.Def.Id] = pool;
        return pool;
    }

    static readonly char[] ClauseEnds = [',', '.', ';', ':', '+'];
    static readonly char[] Separators = [' ', '\n', '\r', '\t', '/', '-', '!', '?', '"', '«', '»', '(', ')'];

    /// <summary>Запрос → слова каталогов. «без X», «no X», «убери X» — в Avoid; запятая и точка заканчивают «без».</summary>
    public static Intent Understand(string prompt, string style)
    {
        var want = new List<string>();
        var avoid = new List<string>();
        foreach (var clause in prompt.ToLowerInvariant().Split(ClauseEnds, StringSplitOptions.RemoveEmptyEntries))
        {
            var negate = false;
            foreach (var raw in clause.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Negators.Contains(raw)) { negate = true; continue; }
                if (raw is "и" or "and" or "или" or "or" or "с" or "with" or "мне" or "хочу" or "нужн" or "нужно" or "нужен" or "нужны" or "чтобы") continue;
                var words = Expand(raw);
                if (words.Count == 0) continue;
                (negate ? avoid : want).AddRange(words);
            }
        }
        if (Styles.FirstOrDefault(s => s.Id == style) is { Id: not null } st) want.AddRange(st.Words.Where(w => !avoid.Contains(w)));
        return new Intent(want.Distinct().Take(24).ToList(), avoid.Distinct().Take(16).ToList());
    }

    static List<string> Expand(string raw)
    {
        if (raw.StartsWith("чит")) return ["cheat", "trainer"];
        if (raw.Length < 3 && raw is not ("ui" or "hd" or "ai")) return [];
        var hit = Synonyms.Where(kv => raw.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)).SelectMany(kv => kv.Value).ToList();
        if (hit.Count > 0) return hit;
        return raw.All(c => c < 128) ? [raw] : [];
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
        // 16.0: страницы каталога и поиск по словам — параллельно (раньше по очереди, до 8 запросов подряд).
        var queries = new List<Query> { new(Sort: SortBy.Popular), new(Page: 2, Sort: SortBy.Popular), new(Sort: SortBy.Rating), new(Sort: SortBy.Updated) };
        queries.AddRange(words.Take(6).Select(w => new Query(Text: w)));
        foreach (var batch in queries.Chunk(4)) await Task.WhenAll(batch.Select(Take));
        return found.Values.ToList();
    }

    static string Text(ModInfo m) => (m.Name + " " + m.Description + " " + string.Join(' ', m.Categories)).ToLowerInvariant();

    static bool Avoided(ModInfo m, Intent intent)
    {
        var text = Text(m);
        return intent.Avoid.Any(w => text.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>Очки мода под запрос: совпадения слов (в названии — дороже), популярность, оценка, свежесть.</summary>
    internal static double Score(ModInfo m, Intent intent)
    {
        var text = Text(m);
        var name = m.Name.ToLowerInvariant();
        var hits = intent.Want.Count(w => text.Contains(w, StringComparison.Ordinal));
        var inName = intent.Want.Count(w => name.Contains(w, StringComparison.Ordinal));
        var stale = m.UpdatedAt is { } u && DateTime.UtcNow - u > TimeSpan.FromDays(3 * 365) ? 1.2 : 0;
        var bad = intent.Avoid.Any(w => text.Contains(w, StringComparison.Ordinal)) ? 100 : 0;
        return hits * 3 + inName * 2 + Math.Log10(m.Downloads + 10) + Math.Log10(m.Rating + 10) * 0.6 - stale - bad;
    }

    /// <summary>Насколько мод подходит к запросу, 0–99: слова запроса и популярность.</summary>
    internal static int Match(ModInfo m, Intent intent)
    {
        var text = Text(m);
        var hits = intent.Want.Count(w => text.Contains(w, StringComparison.Ordinal));
        var popular = Math.Min(15, (int)(Math.Log10(m.Downloads + 10) * 2.5));
        return Math.Clamp((hits > 0 ? 58 + Math.Min(hits, 3) * 9 : 34) + popular, 0, 99);
    }

    static int Family(ModInfo m)
    {
        var text = Text(m);
        for (var i = 0; i < Families.Length; i++) if (Families[i].Any(w => text.Contains(w, StringComparison.Ordinal))) return i;
        return -1;
    }

    static string Norm(string name) => new(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Встроенный подборщик. 16.0: «без …» вычёркивает, из семейства (две мини-карты, два «больше игроков») — один,
    /// почти одинаковые названия («MoreCompany» и «MoreCompany Fixed») — один, одной категории — не больше трети сборки.
    /// keep — моды, которые остаются из прошлой сборки (уточнение).
    /// </summary>
    static AiBuild Local(GameState g, string prompt, string style, int size, Intent intent, List<ModInfo> pool, List<AiPick> keep)
    {
        var ranked = pool.Where(m => !Avoided(m, intent)).Select(m => (Mod: m, Score: Score(m, intent))).OrderByDescending(x => x.Score).ToList();
        var picks = new List<AiPick>(keep.Take(size));
        var families = picks.Select(p => Family(p.Mod)).Where(f => f >= 0).ToHashSet();
        var names = picks.Select(p => Norm(p.Mod.Name)).ToList();
        var perCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cap = Math.Max(2, (int)Math.Ceiling(size / 3.0));
        foreach (var p in picks) if (p.Mod.Categories.FirstOrDefault() is { } c0) perCategory[c0] = perCategory.GetValueOrDefault(c0) + 1;
        foreach (var (m, _) in ranked)
        {
            if (picks.Count >= size) break;
            if (picks.Any(p => p.Mod.Id == m.Id)) continue;
            var fam = Family(m);
            if (fam >= 0 && families.Contains(fam)) continue;
            var n = Norm(m.Name);
            if (n.Length >= 5 && names.Any(x => x.Length >= 5 && (x.Contains(n) || n.Contains(x)))) continue;
            var cat = m.Categories.FirstOrDefault();
            if (cat is not null && perCategory.GetValueOrDefault(cat) >= cap) continue;
            var hits = intent.Want.Where(w => Text(m).Contains(w, StringComparison.Ordinal)).Take(2).ToList();
            var why = hits.Count > 0 ? I18n.T("v98.ai.why.match", ("word", string.Join(", ", hits))) : m.Downloads > 0 ? I18n.T("v98.ai.why.popular", ("n", Short(m.Downloads))) : I18n.T("v98.ai.why.rated");
            picks.Add(new AiPick(m, why, Match(m, intent)));
            if (fam >= 0) families.Add(fam);
            names.Add(n);
            if (cat is not null) perCategory[cat] = perCategory.GetValueOrDefault(cat) + 1;
        }
        // Зависимости, которые есть в пуле, — в сборку, чтобы всё запустилось.
        foreach (var dep in picks.SelectMany(p => p.Mod.Dependencies).Select(Recommend.Bare).Distinct().ToList())
            if (pool.FirstOrDefault(m => Recommend.Bare(m.Id) == dep) is { } d && picks.All(p => p.Mod.Id != d.Id))
                picks.Add(new AiPick(d, I18n.T("v98.ai.why.dep"), Match(d, intent)));
        var name = Title(prompt, style, g);
        var note = I18n.T("v16.ai.note", ("n", pool.Count), ("hit", picks.Count(p => p.Match >= 58)),
            ("avoid", intent.Avoid.Count == 0 ? "—" : string.Join(", ", intent.Avoid.Take(4))));
        return new AiBuild(Guid.NewGuid().ToString("N")[..10], g.Def.Id, name, prompt, style, "local", note, DateTime.UtcNow, picks);
    }

    /// <summary>16.0: «Похожие» — замены для мода из той же сборки: то же семейство или категория, ещё не в сборке.</summary>
    public static async Task<List<ModInfo>> Alternatives(GameState g, AiBuild b, ModInfo mod, int count = 3, CancellationToken ct = default)
    {
        List<ModInfo>? pool;
        lock (Pools) Pools.TryGetValue(g.Def.Id, out pool);
        var intent = Understand(b.Prompt, b.Style);
        pool ??= await Candidates(g, intent.Want, ct);
        lock (Pools) Pools[g.Def.Id] = pool;
        var fam = Family(mod);
        var cats = mod.Categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var words = Text(mod).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 4).Take(30).ToHashSet();
        return pool.Where(m => m.Id != mod.Id && b.Picks.All(p => p.Mod.Id != m.Id) && !Avoided(m, intent))
            .Select(m => (m, s: (fam >= 0 && Family(m) == fam ? 6 : 0) + m.Categories.Count(cats.Contains) * 3 + Text(m).Split(' ').Count(words.Contains) * 0.4 + Math.Log10(m.Downloads + 10)))
            .OrderByDescending(x => x.s).Take(count).Select(x => x.m).ToList();
    }

    /// <summary>Заменить мод в сборке другим (из «Похожих»); сборка сохраняется.</summary>
    public static AiBuild Replace(AiBuild b, string oldId, ModInfo with)
    {
        var intent = Understand(b.Prompt, b.Style);
        var picks = b.Picks.Select(p => p.Mod.Id == oldId ? new AiPick(with, I18n.T("v16.ai.why.swap", ("mod", p.Mod.Name)), Match(with, intent)) : p).ToList();
        var next = b with { Picks = picks };
        Save(next);
        return next;
    }

    static string Short(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();

    static string Title(string prompt, string style, GameState g)
    {
        if (prompt.Length is > 0 and <= 32) return char.ToUpper(prompt[0]) + prompt[1..];
        return $"{g.Def.Name} · {I18n.T("v98.ai.style." + style)}";
    }

    /// <summary>Claude: каталог с номерами, запрос, чего избегать; при уточнении — прошлая сборка. Ответ — JSON.</summary>
    static async Task<AiBuild> AskClaude(GameState g, string prompt, string style, int size, Intent intent, List<ModInfo> pool, AiBuild? previous, CancellationToken ct)
    {
        var top = pool.OrderByDescending(m => Score(m, intent)).Take(140).ToList();
        if (previous is not null) foreach (var p in previous.Picks) if (top.All(m => m.Id != p.Mod.Id)) top.Add(p.Mod);
        var list = string.Join('\n', top.Select((m, i) =>
            $"{i}. {m.Name} | {Clip(m.Description, 140)} | tags: {string.Join(',', m.Categories.Take(4))} | downloads: {m.Downloads}{(m.UpdatedAt is { } u ? $" | updated: {u:yyyy-MM}" : "")}"));
        var installed = g.Registry is { } reg ? top.Where(m => reg.Has(m.Id)).Select(m => m.Name).Take(20).ToList() : [];
        var user = $"Game: {g.Def.Name}\nStyle: {style}\nPlayer request: {prompt}\n" +
                   (intent.Avoid.Count > 0 ? $"Must avoid: {string.Join(", ", intent.Avoid)}\n" : "") +
                   (installed.Count > 0 ? $"Already installed: {string.Join(", ", installed)}\n" : "") +
                   (previous is not null ? $"Current build: {string.Join(", ", previous.Picks.Select(p => p.Mod.Name))}\nChange it as the player asks; keep mods that still fit.\n" : "") +
                   $"Pick up to {size} mods that work well together: no two mods doing the same job, prefer recently updated ones.\n\nCatalog:\n{list}\n\n" +
                   "Answer with JSON only: {\"name\": \"short build name in the player's language\", \"note\": \"one sentence about the build in the player's language\", " +
                   "\"mods\": [{\"i\": catalog number, \"why\": \"short reason in the player's language\", \"fit\": 0-100 how well it fits the request}]}";
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 1800,
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
            if (item?["i"]?.GetValue<int>() is int i && i >= 0 && i < top.Count && picks.All(p => p.Mod.Id != top[i].Id))
                picks.Add(new AiPick(top[i], Clip(item.Str("why") ?? "", 120), item?["fit"] is JsonValue fv && fv.TryGetValue<int>(out var fit) ? Math.Clamp(fit, 0, 99) : Match(top[i], intent)));
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
            ["icon"] = p.Mod.Icon, ["url"] = p.Mod.Url, ["downloads"] = p.Mod.Downloads, ["why"] = p.Why, ["match"] = p.Match,
            ["description"] = p.Mod.Description.Length > 400 ? p.Mod.Description[..400] : p.Mod.Description, ["categories"] = new JsonArray(p.Mod.Categories.Take(4).Select(c => (JsonNode)c).ToArray()),
        }).ToArray()),
    };

    static AiBuild? Read(JsonObject o)
    {
        if (o.Str("id") is not { } id || o.Str("game") is not { } game) return null;
        var picks = o.Arr("mods").OfType<JsonObject>().Where(m => m.Str("id") is not null).Select(m => new AiPick(new ModInfo
        {
            Source = m.Str("source") ?? "thunderstore", Id = m.Str("id")!, Name = m.Str("name") ?? m.Str("id")!, Author = m.Str("author") ?? "",
            Version = m.Str("version") ?? "", Icon = m.Str("icon"), Url = m.Str("url"), Downloads = m.Long("downloads"),
            Description = m.Str("description") ?? "", Categories = m.Arr("categories").Select(c => c?.GetValue<string>() ?? "").Where(c => c != "").ToArray(),
        }, m.Str("why") ?? "", (int)m.Long("match"))).ToList();
        return new AiBuild(id, game, o.Str("name") ?? "", o.Str("prompt") ?? "", o.Str("style") ?? "vanilla", o.Str("engine") ?? "local", o.Str("note") ?? "",
            DateTime.TryParse(o.Str("at"), out var d) ? d.ToUniversalTime() : DateTime.UtcNow, picks, (int)o.Long("likes"));
    }
}
