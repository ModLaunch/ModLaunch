using System.Text.RegularExpressions;

namespace ModLaunch.Sources;

/// <summary>
/// 26.0: умный поиск модов. Каталоги ищут по-английски и требуют каждое слово подстрокой: «random enemies» на
/// Thunderstore не находит SimpleEnemyRando («enemies» ≠ «Enemy»), а «рандомные враги» не находит ничего нигде.
/// Здесь запрос приводится к тому, что понимают каталоги: русские и украинские слова — по словарю в английские
/// основы, английские — к основе (enemies → enem, randomizer → rando), служебные слова отбрасываются, а «слабые»
/// («больше», «без», «новые») не сужают поиск, только поднимают подходящие моды выше. Если по всей фразе находится
/// мало, каталог спрашивают по каждому слову отдельно и сортируют по числу совпавших слов (Catalog.Smart).
/// </summary>
public static partial class SearchWords
{
    /// <summary>
    /// Что спросить у каталога. Query — основные слова одной строкой; Keywords — они же по отдельности (для запасного
    /// поиска); Match — все формы каждого слова, включая «слабые» (для оценки совпадений); Weak — какие из них слабые.
    /// </summary>
    public sealed record Plan(string Query, List<string> Keywords, List<string[]> Match, List<bool> Weak, bool Translated)
    {
        public bool Changed(string original) => !string.Equals(Query, original.Trim(), StringComparison.OrdinalIgnoreCase);
        /// <summary>Сколько слов должно совпасть, чтобы мод считался найденным «по всем словам».</summary>
        public int Counted => Math.Max(1, Weak.Count(w => !w));
    }

    [GeneratedRegex(@"\p{IsCyrillic}")] private static partial Regex Cyrillic();
    [GeneratedRegex(@"^(.{4,}?)(izations?|isations?|izers?|isers?|izes|ises|ized|ised|izing|ising|ize|ise)$")] private static partial Regex Izer();
    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")] private static partial Regex NotWord();
    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")] private static partial Regex Camel();

    static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "of", "for", "and", "or", "to", "in", "on", "with", "mod", "mods", "modded", "that", "which",
        "мод", "моды", "модов", "моде", "модами", "модик", "модики", "мода", "для", "и", "или", "в", "во", "на", "с", "со", "по", "из", "от", "к", "ко",
        "про", "чтобы", "чтоб", "что", "как", "который", "которые", "которая", "которых", "это", "все", "всех", "весь",
        "игра", "игры", "игре", "игру", "хочу", "нужен", "нужно", "нужна", "надо", "где", "мне", "можно", "есть", "найти", "какой", "какие",
        "режим", "режиме", "новые", "новый", "новая", "новое", "лучшие", "лучший", "популярные", "какой-нибудь",
        "моди", "модів", "та", "й", "що", "щоб", "які", "який", "гра", "гри", "грі", "знайти", "треба", "потрібен", "нові", "новий",
    };

    /// <summary>
    /// Словарь: начало русского или украинского слова → английские основы (первая — в запрос, остальные — для оценки).
    /// Основы — подстроки, которые ловят все формы в названиях модов («enem» — и Enemy, и Enemies). Проверяется самое
    /// длинное подходящее начало, поэтому «магазин» не станет «магией», а «миникарта» — «картой».
    /// </summary>
    static readonly Dictionary<string, string[]> Ru = Build(
        "рандом=rando|random  случайн=rando|random  випадков=rando|random  враг=enem|mob  враж=enem|hostile  ворог=enem|mob  ворож=enem|hostile  " +
        "противник=enem|foe  монстр=monster|creature  существ=creature|monster  істот=creature|monster  моб=mob|enem  босс=boss  бос=boss  нпс=npc  " +
        "урон=damage|dmg  шкод=damage|dmg  атак=attack  оруж=weapon  збро=weapon  меч=sword|blade  взрыв=explos|bomb  брон=armor|armour  здоров=health|hp  " +
        "хп=hp|health  жизн=life|lives  житт=life|lives  бессмерт=god|immortal  безсмерт=god|immortal  неуязвим=invinc|invuln  невразлив=invinc|invuln  " +
        "бог=god  чит=cheat|trainer  трейнер=trainer|cheat  бесконечн=infinit|unlimit  нескінченн=infinit|unlimit  деньг=money|currency  денег=money|currency  " +
        "грош=money|currency  монет=coin|money  золот=gold  сложност=difficult  сложн=difficult|hard  складн=difficult|hard  легк=easy|casual  хардкор=hardcore|hard  " +
        "мультиплеер=multiplayer  кооп=coop|co-op  сетев=multiplayer|online  онлайн=online|multiplayer  друз=friend|coop  сервер=server  лобби=lobby  " +
        "игрок=player  гравц=player  гравец=player  больш=more|bigger  більш=more|bigger  интерфейс=ui|hud  інтерфейс=ui|hud  худ=hud  меню=menu  полоск=bar  " +
        "шкал=bar|meter  таймер=timer  прицел=crosshair|aim  миникарт=minimap  мінікарт=minimap  карт=map  мап=map  метк=marker|pin  скорост=speed|fast  " +
        "быстр=fast|speed  швидк=fast|speed  прыж=jump  стриб=jump  двойн=double  подвійн=double  полет=fly|flight  політ=fly|flight  бег=run|sprint  " +
        "телепорт=teleport|warp  камер=camera|cam  график=graphic|visual  графік=graphic|visual  текстур=texture  освещ=lighting|light  освітл=lighting|light  " +
        "свет=light  світл=light  шейдер=shader  решейд=reshade  разрешен=resolution  качеств=quality  якіст=quality  якост=quality  реализм=realis  " +
        "погод=weather  ноч=night  музык=music  музик=music  звук=sound|audio  озвуч=voice|dub  перевод=translat|locali  переклад=translat|locali  " +
        "русифик=russian|translat  русск=russian  російськ=russian  украин=ukrain  україн=ukrain  локализ=locali|translat  локаліз=locali|translat  " +
        "сохран=save  збереж=save  крафт=craft  рецепт=recipe  строит=build  строй=build  будів=build  мебел=furniture  ферм=farm  рыб=fish  риб=fish  " +
        "охот=hunt  животн=animal|creature  тварин=animal|creature  транспорт=vehicle  машин=car|vehicle  автомоб=car|vehicle  лодк=boat  корабл=ship|boat  " +
        "самолет=plane|aircraft  персонаж=character  скин=skin  скін=skin  внешн=appearance|skin  зовнішн=appearance|skin  костюм=suit|costume  " +
        "одежд=cloth|outfit  одяг=cloth|outfit  прическ=hair  волос=hair  предмет=item  инвентар=inventory  інвентар=inventory  сундук=chest|storage  " +
        "лут=loot  зель=potion  амулет=charm|amulet  инструмент=tool  інструмент=tool  навык=skill  способн=abilit  заклин=spell  магазин=shop|store  " +
        "маг=magic|mage  уров=level  рівен=level  опыт=xp|experience  улучш=upgrade|improv  квест=quest  сюжет=story  диалог=dialog  діалог=dialog  " +
        "торгов=trade|merchant  смерт=death  респаун=respawn  удобн=qol|quality  зручн=qol|quality  производительн=performance|fps  оптимиз=optimi|performance  " +
        "оптиміз=optimi|performance  фпс=fps  лаг=lag|performance  фикс=fix  исправл=fix  виправл=fix  баг=bug|fix  вылет=crash|fix  настро=config|setting  " +
        "налашт=config|setting  конфиг=config  консол=console  дебаг=debug  редактор=editor  смешн=funny|meme  эмоц=emote  танц=dance|emote  страш=horror|scary  " +
        "хоррор=horror  спидран=speedrun  мир=world  мирн=peaceful|passive  локац=location|area  спутник=companion|follower  размер=size|scale  " +
        "пропуск=skip  без=no|without  лошад=horse|mount  лагер=camp  щит=shield  шлем=helmet|helm  лук=bow  стрел=arrow  патрон=ammo  " +
        "волн=wave  хвил=wave  арен=arena  спавн=spawn  карти=texture|image  питом=pet  собак=dog  кошк=cat  багато=many|more  много=many|more");

    static Dictionary<string, string[]> Build(string table)
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var pair in table.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            map[pair[..eq]] = pair[(eq + 1)..].Split('|');
        }
        return map;
    }

    static readonly int MaxStem = Ru.Keys.Max(k => k.Length);

    /// <summary>Английские слова, которые каталоги пишут по-разному: одна основа ловит все формы и синонимы.</summary>
    static readonly Dictionary<string, string[]> EnAlias = new(StringComparer.Ordinal)
    {
        ["rando"] = ["rando", "random"], ["random"] = ["rando", "random"], ["randomi"] = ["rando", "random"],
        ["god"] = ["god", "invinc"], ["godmode"] = ["god", "invinc"], ["coop"] = ["coop", "multiplayer"], ["co-op"] = ["coop", "multiplayer"],
        ["multiplayer"] = ["multiplayer", "coop"], ["qol"] = ["qol", "quality"], ["hp"] = ["hp", "health"], ["health"] = ["health", "hp"],
        ["ui"] = ["ui", "hud"], ["hud"] = ["hud", "ui"], ["fps"] = ["fps", "performance"], ["optimi"] = ["optimi", "performance"],
        ["translat"] = ["translat", "locali"], ["locali"] = ["locali", "translat"], ["armor"] = ["armor", "armour"], ["armour"] = ["armor", "armour"],
        ["cheat"] = ["cheat", "trainer"], ["dmg"] = ["dmg", "damage"], ["damage"] = ["damage", "dmg"],
    };

    /// <summary>Слабые слова: в запрос не идут (иначе «больше врагов» нашло бы один мод), но поднимают моды с ними в названии.</summary>
    static readonly HashSet<string> WeakWords = new(StringComparer.Ordinal) { "more", "bigger", "no", "without", "new", "many" };

    /// <summary>Основа английского слова: enemies → enem, randomizer → randomi, bosses → boss, building → build.</summary>
    public static string Stem(string word)
    {
        var w = word.ToLowerInvariant();
        if (w.Length < 4 || !w.All(c => c is >= 'a' and <= 'z')) return w;
        if (Izer().Match(w) is { Success: true } m) return m.Groups[1].Value + "i";
        if (w.EndsWith("ies", StringComparison.Ordinal) && w.Length > 4) return w[..^3];
        if (w.Length >= 5 && w[^1] == 'y' && !"aeiou".Contains(w[^2])) return w[..^1];
        if (w.Length > 4 && (w.EndsWith("ses", StringComparison.Ordinal) || w.EndsWith("xes", StringComparison.Ordinal) || w.EndsWith("zes", StringComparison.Ordinal)
            || w.EndsWith("ches", StringComparison.Ordinal) || w.EndsWith("shes", StringComparison.Ordinal))) return w[..^2];
        if (w.Length >= 7 && w.EndsWith("ing", StringComparison.Ordinal))
        {
            var s = w[..^3];
            return s.Length > 3 && s[^1] == s[^2] && !"aeiou".Contains(s[^1]) ? s[..^1] : s;
        }
        if (w[^1] == 's' && !"siu".Contains(w[^2])) return w[..^1];
        return w;
    }

    public static Plan Make(string text)
    {
        var raw = (text ?? "").Trim();
        if (raw == "") return new Plan("", [], [], [], false);
        var lower = raw.ToLowerInvariant().Replace('ё', 'е').Replace("'", "").Replace("’", "").Replace("ʼ", "").Replace("`", "");
        // Словосочетания, которые пишут по-разному.
        lower = " " + NotWord().Replace(lower, " ") + " ";
        foreach (var (from, to) in new[] { (" god mode ", " godmode "), (" co op ", " coop "), (" quality of life ", " qol "), (" режим бога ", " бог "), (" режиме бога ", " бог ") })
            lower = lower.Replace(from, to);
        var match = new List<string[]>();
        var weak = new List<bool>();
        var translated = false;
        void Add(string[] forms)
        {
            if (forms.Length == 0 || match.Any(f => f[0] == forms[0])) return;
            match.Add(forms);
            weak.Add(WeakWords.Contains(forms[0]));
        }
        foreach (var token in lower.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Stop.Contains(token)) continue;
            if (Cyrillic().IsMatch(token))
            {
                string[]? found = null;
                for (var n = Math.Min(token.Length, MaxStem); n >= 2 && found is null; n--)
                    Ru.TryGetValue(token[..n], out found);
                if (found is not null) translated = true;
                // Незнакомое русское слово каталогам не поможет (названий по-русски там нет) — только для оценки.
                Add(found ?? [token]);
            }
            else if (token.Length >= 2 || char.IsDigit(token[0]))
            {
                var stem = Stem(token);
                Add(EnAlias.TryGetValue(stem, out var alias) ? alias : stem == token ? [stem] : [stem, token]);
            }
        }
        if (match.Count == 0) { match.Add([raw.ToLowerInvariant()]); weak.Add(false); }
        // В запрос — основные слова (не слабые и не незнакомые русские); основа короче 3 букв — только если она одна.
        var useful = match.Select((f, i) => (f, i)).Where(x => !weak[x.i] && !Cyrillic().IsMatch(x.f[0])).Select(x => x.f[0]).ToList();
        if (useful.Count == 0) useful = match.Where(f => !Cyrillic().IsMatch(f[0])).Select(f => f[0]).ToList();
        if (useful.Count > 1) useful = useful.Where(k => k.Length >= 3).DefaultIfEmpty(useful[0]).ToList();
        var query = useful.Count > 0 ? string.Join(' ', useful) : raw;
        return new Plan(query, useful.Count > 0 ? useful : [raw], match, weak, translated);
    }

    static (string Text, HashSet<string> Words) Hay(string text)
    {
        var words = NotWord().Split(Camel().Replace(text ?? "", " ")).Where(w => w != "").Select(w => w.ToLowerInvariant()).ToHashSet();
        return ((text ?? "").ToLowerInvariant().Replace('_', ' '), words);
    }

    static bool Has((string Text, HashSet<string> Words) hay, string form) =>
        form.Length < 3 ? hay.Words.Contains(form) : hay.Text.Contains(form, StringComparison.Ordinal) || hay.Text.Replace(" ", "").Contains(form.Replace(" ", ""), StringComparison.Ordinal);

    /// <summary>Сколько основных (не слабых) слов запроса нашлось у мода — в названии, номере, описании или разделах.</summary>
    public static int Score(ModInfo mod, Plan plan)
    {
        var all = Hay(mod.Name + " " + mod.Id + " " + mod.Description + " " + string.Join(' ', mod.Categories));
        var hits = 0;
        for (var i = 0; i < plan.Match.Count; i++)
            if (!plan.Weak[i] && plan.Match[i].Any(f => Has(all, f))) hits++;
        return hits;
    }

    /// <summary>Сколько слов (и слабых тоже) нашлось в названии — совпадение в названии важнее описания.</summary>
    public static int NameScore(ModInfo mod, Plan plan)
    {
        var name = Hay(mod.Name + " " + mod.Id);
        return plan.Match.Count(forms => forms.Any(f => Has(name, f)));
    }

    /// <summary>Вес совпадений для сортировки: название ×3, разделы ×2, описание ×1; второстепенная форма ×0.8, слабое слово ×0.5.</summary>
    public static double Weight(ModInfo mod, Plan plan)
    {
        var name = Hay(mod.Name + " " + mod.Id);
        var cats = Hay(string.Join(' ', mod.Categories));
        var desc = Hay(mod.Description);
        double total = 0;
        for (var i = 0; i < plan.Match.Count; i++)
        {
            double best = 0;
            for (var j = 0; j < plan.Match[i].Length; j++)
            {
                var f = plan.Match[i][j];
                var w = Has(name, f) ? 3 : Has(cats, f) ? 2 : Has(desc, f) ? 1 : 0;
                best = Math.Max(best, w * (j == 0 ? 1 : 0.8));
            }
            total += plan.Weak[i] ? best * 0.5 : best;
        }
        return total;
    }

    [SelfTest]
    static string UnderstandsRussianAndPlurals()
    {
        var fail = new List<string>();
        void Expect(string input, string query)
        {
            var got = Make(input).Query;
            if (got != query) fail.Add($"«{input}» → «{got}», ждали «{query}»");
        }
        Expect("рандомные враги", "rando enem");
        Expect("random enemies", "rando enem");
        Expect("Enemy Randomizer", "enem rando");
        Expect("рандомні вороги", "rando enem");
        Expect("моды на оружие", "weapon");
        Expect("полоска здоровья врагов", "bar health enem");
        Expect("больше игроков", "player");
        Expect("без врагов", "enem");
        Expect("режим бога", "god");
        Expect("бессмертие", "god");
        Expect("русификатор", "russian");
        Expect("SimpleEnemyRando", "simpleenemyrando");
        Expect("Content Patcher", "content patcher");
        Expect("bosses", "boss");
        Expect("building", "build");
        Expect("магазин", "shop");
        Expect("миникарта", "minimap");
        var rando = new ModInfo { Source = "thunderstore", Id = "cometcake575-SimpleEnemyRando", Name = "SimpleEnemyRando", Description = "A Silksong enemy and boss randomiser made using Architect" };
        var bar = new ModInfo { Source = "thunderstore", Id = "ygsbzr-SSEnemyHPBar", Name = "SSEnemyHPBar", Description = "Shows enemy health" };
        var plan = Make("рандомные враги");
        if (Score(rando, plan) != 2 || Score(bar, plan) != 1 || Weight(rando, plan) <= Weight(bar, plan)) fail.Add($"score {Score(rando, plan)}/{Score(bar, plan)}");
        // Короткое слово «hp» — только целым словом (в SSEnemyHPBar оно отдельное слово по CamelCase).
        if (Score(bar, Make("HP bar")) != 2) fail.Add("HP bar");
        var more = Make("больше игроков");
        if (!more.Weak.Contains(true) || NameScore(new ModInfo { Source = "thunderstore", Id = "x-MoreCompany", Name = "MoreCompany" }, more) != 1) fail.Add("weak more");
        if (fail.Count > 0) throw new Exception(string.Join("; ", fail));
        return "«рандомные враги» и «random enemies» → «rando enem»: SimpleEnemyRando совпадает по обоим словам; «больше», «без» не сужают поиск; украинский, «режим бога», bosses → boss";
    }
}
