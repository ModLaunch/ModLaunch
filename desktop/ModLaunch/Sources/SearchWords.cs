using System.Text.RegularExpressions;

namespace ModLaunch.Sources;

/// <summary>
/// 26.0: умный поиск модов. Каталоги ищут по-английски и требуют каждое слово целиком: «random enemies» на
/// Thunderstore не находит SimpleEnemyRando («enemies» ≠ «Enemy»), а «рандомные враги» не находит ничего нигде.
/// Здесь запрос приводится к тому, что понимают каталоги: русские и украинские слова — по словарю в английские
/// основы, английские — к основе (enemies → enemy, randomizer → rando), служебные слова отбрасываются.
/// Если по всей фразе ничего нет, каталог спрашивают по каждому слову отдельно, а результаты сортируются по тому,
/// сколько слов совпало (Catalog.Browse).
/// </summary>
public static partial class SearchWords
{
    /// <summary>Что спросить у каталога: Query — вся фраза, Keywords — основы по отдельности (для запасного поиска),
    /// Match — все формы каждого слова (для оценки совпадений).</summary>
    public sealed record Plan(string Query, List<string> Keywords, List<string[]> Match)
    {
        public bool Changed(string original) => !string.Equals(Query, original.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[\p{L}\p{Nd}]+(?:['’][\p{L}]+)?")] private static partial Regex Word();
    [GeneratedRegex(@"\p{IsCyrillic}")] private static partial Regex Cyrillic();

    static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "of", "for", "and", "or", "to", "in", "on", "with", "mod", "mods", "modded",
        "мод", "моды", "модов", "моде", "модами", "модик", "для", "и", "или", "в", "во", "на", "с", "со", "по", "из", "от", "к", "про", "чтобы", "что", "как",
        "мода", "моди", "модів", "та", "й", "що", "щоб", "які", "який", "который", "которые", "которая", "которых", "чтоб", "это", "все", "всех",
        "игра", "игры", "игре", "гра", "гри", "хочу", "нужен", "нужно", "надо", "где", "мне", "можно",
    };

    /// <summary>
    /// Словарь: начало русского или украинского слова → английские основы (первая — для запроса, остальные — для
    /// оценки совпадений). Основы подобраны так, чтобы находиться подстрокой в названиях модов.
    /// Самые длинные начала проверяются первыми («полоск здоровья» раньше «здоров»).
    /// </summary>
    static readonly (string Stem, string En)[] Dictionary =
    [
        ("рандом", "rando|random"), ("случайн", "rando|random"), ("випадков", "rando|random"), ("перемеш", "rando|shuffle"),
        ("враг", "enemy|enemies|monster"), ("ворог", "enemy|enemies|monster"), ("противник", "enemy|enemies"), ("монстр", "monster|enemy|creature"),
        ("моб", "mob|monster|enemy"), ("существ", "creature|monster"), ("істот", "creature|monster"), ("босс", "boss"), ("бос", "boss"),
        ("нпс", "npc"), ("непис", "npc"), ("персонаж", "character|npc"), ("герой", "hero|character|player"), ("игрок", "player"), ("гравц", "player"), ("гравец", "player"),
        ("оруж", "weapon|gun|sword"), ("зброя", "weapon|gun"), ("зброї", "weapon|gun"), ("пушк", "gun|weapon"), ("ружь", "gun|rifle"), ("пистолет", "pistol|gun"),
        ("меч", "sword|blade"), ("нож", "knife|blade"), ("лук", "bow"), ("стрел", "arrow"), ("патрон", "ammo"), ("боеприпас", "ammo"),
        ("брон", "armor|armour"), ("обладун", "armor|armour"), ("доспех", "armor|armour"), ("щит", "shield"), ("шлем", "helmet|helm"),
        ("одежд", "outfit|clothes|clothing"), ("одяг", "outfit|clothes|clothing"), ("костюм", "suit|costume|outfit"), ("скин", "skin"), ("шкурк", "skin"),
        ("прическ", "hair|hairstyle"), ("волос", "hair"), ("зачіск", "hair|hairstyle"), ("лицо", "face"), ("лица", "face"), ("обличч", "face"), ("тело", "body"), ("тела", "body"),
        ("картин", "texture|image|picture"), ("карт", "map"), ("мап", "map"), ("локац", "location|area|map"), ("уровн", "level"), ("рівн", "level"), ("мир", "world"), ("світ", "world"), ("остров", "island"),
        ("предмет", "item"), ("вещ", "item"), ("реч", "item"), ("лут", "loot"), ("добыч", "loot"), ("сундук", "chest|storage"), ("скрин", "chest|storage"),
        ("инвентар", "inventory"), ("інвентар", "inventory"), ("хранил", "storage"), ("склад", "storage"), ("рюкзак", "backpack|bag"), ("сумк", "bag|backpack"),
        ("чит", "cheat"), ("бессмерт", "god|invincib|immortal"), ("безсмерт", "god|invincib|immortal"), ("неуязв", "invincib|god"), ("бог", "god"),
        ("деньг", "money|gold|currency"), ("гроші", "money|gold|currency"), ("грош", "money|gold|currency"), ("монет", "coin|money"), ("золот", "gold"), ("валют", "currency|money"),
        ("опыт", "xp|experience"), ("досвід", "xp|experience"), ("прокач", "level|xp|skill"), ("навык", "skill"), ("навич", "skill"), ("умени", "skill|ability"), ("способн", "ability|skill"), ("здібн", "ability|skill"),
        ("сложн", "difficulty|hard"), ("складн", "difficulty|hard"), ("хардкор", "hardcore|hard"), ("легк", "easy"), ("лёгк", "easy"), ("легш", "easy"), ("баланс", "balance"),
        ("мультиплеер", "multiplayer|coop"), ("мультиплеєр", "multiplayer|coop"), ("кооп", "coop|co-op|multiplayer"), ("сетев", "multiplayer|online"), ("онлайн", "online|multiplayer"),
        ("больше игрок", "more players|morecompany|bigger lobby"), ("лобби", "lobby"), ("лоббі", "lobby"), ("друз", "friend"),
        ("интерфейс", "ui|hud|interface"), ("інтерфейс", "ui|hud|interface"), ("худ", "hud"), ("меню", "menu"), ("миникарт", "minimap"), ("мінікарт", "minimap"),
        ("полоск здоровья", "health bar|healthbar|hp bar"), ("полоса здоровья", "health bar|healthbar|hp bar"), ("смуг здоров", "health bar|healthbar"),
        ("здоров", "health|hp"), ("хп", "hp|health"), ("жизн", "life|health"), ("житт", "life|health"), ("урон", "damage|dmg"), ("шкод", "damage"), ("пошкодж", "damage"),
        ("скорост", "speed|fast"), ("швидк", "speed|fast"), ("быстр", "fast|speed|quick"), ("медлен", "slow"), ("прыж", "jump"), ("стриб", "jump"), ("стрибк", "jump"),
        ("бег", "run|sprint"), ("біг", "run|sprint"), ("спринт", "sprint"), ("выносл", "stamina"), ("витривал", "stamina"), ("стамин", "stamina"), ("летать", "fly|flight"), ("полет", "fly|flight"), ("політ", "fly|flight"),
        ("камер", "camera"), ("вид от перв", "first person|fpv"), ("от перв", "first person"), ("от трет", "third person"), ("зум", "zoom"), ("приближ", "zoom"),
        ("график", "graphics|visual|shader"), ("графік", "graphics|visual|shader"), ("шейдер", "shader"), ("текстур", "texture|retexture"), ("освещ", "lighting|light"), ("освітл", "lighting|light"),
        ("свет", "light"), ("світл", "light"), ("тени", "shadow"), ("тін", "shadow"), ("погод", "weather"), ("дожд", "rain"), ("снег", "snow"), ("сніг", "snow"), ("ночь", "night"), ("ніч", "night"), ("день", "day"),
        ("музык", "music|soundtrack"), ("музик", "music|soundtrack"), ("звук", "sound|audio|sfx"), ("голос", "voice"), ("озвуч", "voice|dub"),
        ("перевод", "translation|russian|localization"), ("переклад", "translation|ukrainian|localization"), ("русификатор", "russian|translation"), ("русск", "russian"), ("українськ", "ukrainian"), ("украинск", "ukrainian"), ("локализ", "localization|translation"),
        ("сохран", "save"), ("збереж", "save"), ("сейв", "save"), ("загруз", "load|loader"), ("автосейв", "autosave|save"),
        ("крафт", "craft|crafting"), ("создан", "craft|crafting|create"), ("рецепт", "recipe"), ("строит", "build|building|construction"), ("стройк", "build|building|construction"), ("будів", "build|building|construction"), ("постро", "build|building"), ("дом", "house|home"), ("будин", "house|home"), ("мебел", "furniture|decor"), ("декор", "decor|decoration"),
        ("ферм", "farm|farming"), ("урожа", "crop|harvest"), ("посев", "seed|plant"), ("растен", "plant"), ("рослин", "plant"), ("семен", "seed"), ("насін", "seed"), ("рыбалк", "fishing|fish"), ("рибалк", "fishing|fish"), ("рыб", "fish"), ("риб", "fish"),
        ("животн", "animal|pet"), ("тварин", "animal|pet"), ("питом", "pet"), ("кошк", "cat"), ("котик", "cat"), ("собак", "dog"), ("лошад", "horse"), ("коня", "horse"), ("кінь", "horse"),
        ("машин", "car|vehicle"), ("автомоб", "car|vehicle"), ("транспорт", "vehicle|transport"), ("лодк", "boat"), ("корабл", "ship|boat"), ("самолет", "plane|aircraft"), ("літак", "plane|aircraft"),
        ("еда", "food"), ("еды", "food"), ("їж", "food"), ("голод", "hunger|food"), ("кулинар", "cooking|food"), ("готовк", "cooking"),
        ("качеств жизн", "quality of life|qol"), ("удобств", "qol|quality of life|convenience"), ("зручн", "qol|convenience"), ("автоматиз", "automation|auto"), ("авто", "auto"),
        ("производительн", "performance|fps|optimization"), ("оптимиз", "optimization|performance|fps"), ("оптиміз", "optimization|performance|fps"), ("фпс", "fps|performance"), ("лаг", "lag|performance"),
        ("исправ", "fix|patch"), ("виправ", "fix|patch"), ("баг", "bug|fix"), ("фикс", "fix"), ("патч", "patch"), ("библиотек", "library|lib|api"), ("бібліотек", "library|lib|api"), ("api", "api"),
        ("квест", "quest"), ("задани", "quest|task"), ("завдан", "quest|task"), ("сюжет", "story"), ("диалог", "dialogue|dialog"), ("діалог", "dialogue|dialog"),
        ("магия", "magic|spell"), ("маги", "magic|spell"), ("магі", "magic|spell"), ("заклинан", "spell"), ("закля", "spell"),
        ("испытан", "challenge|trial"), ("випроб", "challenge|trial"), ("арен", "arena"), ("волн", "wave"), ("хвил", "wave"), ("спавн", "spawn"), ("появлен", "spawn"),
        ("больше", "more"), ("більше", "more"), ("бесконеч", "infinite|unlimited"), ("нескінчен", "infinite|unlimited"), ("безлимит", "unlimited|infinite"),
        ("новы", "new"), ("нов", "new"), ("смешн", "funny|meme"), ("мем", "meme|funny"), ("страшн", "horror|scary"), ("хоррор", "horror"), ("жах", "horror|scary"),
        ("телепорт", "teleport"), ("портал", "portal"), ("редактор", "editor"), ("конструктор", "editor|builder"), ("консол", "console|command"), ("команд", "command"),
        ("щит", "shield"), ("амулет", "charm|amulet"), ("талисман", "charm|amulet"), ("кольц", "ring"), ("зелье", "potion"), ("зілл", "potion"), ("эликсир", "potion|elixir"),
    ];

    /// <summary>Английские слова, которые каталоги пишут по-разному: одна основа ловит все формы.</summary>
    static readonly (string Prefix, string Stem)[] EnglishStems =
    [
        ("random", "rando"), ("rando", "rando"), ("enem", "enemy"), ("monster", "monster"), ("weapon", "weapon"), ("skin", "skin"),
        ("cheat", "cheat"), ("invincib", "invincib"), ("multiplayer", "multiplayer"), ("coop", "coop"), ("co-op", "coop"), ("texture", "texture"),
        ("optimi", "optimi"), ("translat", "translat"), ("localiz", "locali"), ("localis", "locali"), ("crafting", "craft"), ("building", "build"),
    ];

    public static Plan Make(string text)
    {
        var raw = (text ?? "").Trim();
        if (raw == "") return new Plan("", [], []);
        var lower = raw.ToLowerInvariant().Replace('ё', 'е');
        var keywords = new List<string>();
        var match = new List<string[]>();
        void Add(string[] forms)
        {
            if (forms.Length == 0 || keywords.Contains(forms[0], StringComparer.OrdinalIgnoreCase)) return;
            keywords.Add(forms[0]);
            match.Add(forms);
        }

        // Сначала словосочетания из словаря («полоска здоровья», «больше игроков»), потом отдельные слова.
        var rest = " " + string.Join(' ', Word().Matches(lower).Select(m => m.Value)) + " ";
        foreach (var (stem, en) in Dictionary.Where(d => d.Stem.Contains(' ')).OrderByDescending(d => d.Stem.Length))
        {
            var words = stem.Split(' ');
            var pattern = @"(?<=\s)" + string.Join(@"\S*\s+", words.Select(Regex.Escape)) + @"\S*(?=\s)";
            var hit = Regex.Match(rest, pattern);
            if (!hit.Success) continue;
            Add(en.Split('|'));
            rest = rest.Remove(hit.Index, hit.Length).Insert(hit.Index, " ");
        }
        foreach (var token in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Stop.Contains(token) || token.Length < 2 && !char.IsDigit(token[0])) continue;
            if (Cyrillic().IsMatch(token))
            {
                var found = Dictionary.Where(d => !d.Stem.Contains(' ') && token.StartsWith(d.Stem, StringComparison.Ordinal))
                    .OrderByDescending(d => d.Stem.Length).Select(d => d.En).FirstOrDefault();
                // Незнакомое русское слово оставляем как есть: вдруг это название мода.
                Add(found is null ? [token] : found.Split('|'));
            }
            else Add(English(token));
        }
        if (keywords.Count == 0) keywords.Add(raw);
        if (match.Count == 0) match.Add([raw.ToLowerInvariant()]);
        return new Plan(string.Join(' ', keywords), keywords, match);
    }

    /// <summary>Основа английского слова и его формы: enemies → enemy, randomizer → rando.</summary>
    static string[] English(string word)
    {
        foreach (var (prefix, stem) in EnglishStems)
            if (word.StartsWith(prefix, StringComparison.Ordinal)) return stem == word ? [stem] : [stem, word];
        string? single = word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal) ? word[..^3] + "y"
            : word.Length > 4 && (word.EndsWith("ches", StringComparison.Ordinal) || word.EndsWith("shes", StringComparison.Ordinal) || word.EndsWith("xes", StringComparison.Ordinal) || word.EndsWith("sses", StringComparison.Ordinal)) ? word[..^2]
            : word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal) ? word[..^1]
            : null;
        return single is null ? [word] : [single, word];
    }

    /// <summary>Сколько слов запроса нашлось у мода (в названии, номере, описании или разделах).</summary>
    public static int Score(ModInfo mod, Plan plan)
    {
        var hay = (mod.Name + " " + mod.Id + " " + mod.Description + " " + string.Join(' ', mod.Categories)).ToLowerInvariant().Replace('_', ' ');
        var squashed = hay.Replace(" ", "").Replace("-", "");
        return plan.Match.Count(forms => forms.Any(f => hay.Contains(f, StringComparison.Ordinal) || squashed.Contains(f.Replace(" ", ""), StringComparison.Ordinal)));
    }

    /// <summary>Совпадение в названии весит больше, чем в описании: для сортировки найденного по словам.</summary>
    public static int NameScore(ModInfo mod, Plan plan)
    {
        var name = (mod.Name + " " + mod.Id).ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", "");
        return plan.Match.Count(forms => forms.Any(f => name.Contains(f.Replace(" ", ""), StringComparison.Ordinal)));
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
        Expect("рандомные враги", "rando enemy");
        Expect("random enemies", "rando enemy");
        Expect("Enemy Randomizer", "enemy rando");
        Expect("моды на оружие", "weapon");
        Expect("полоска здоровья врагов", "health bar enemy");
        Expect("больше игроков", "more players");
        Expect("рандомні вороги", "rando enemy");
        Expect("SimpleEnemyRando", "simpleenemyrando");
        Expect("бессмертие", "god");
        Expect("Content Patcher", "content patcher");
        var rando = new ModInfo { Source = "thunderstore", Id = "cometcake575-SimpleEnemyRando", Name = "SimpleEnemyRando", Description = "A Silksong enemy and boss randomiser made using Architect" };
        var plan = Make("рандомные враги");
        if (Score(rando, plan) != 2 || NameScore(rando, plan) != 2) fail.Add($"score {Score(rando, plan)}/{NameScore(rando, plan)}");
        if (fail.Count > 0) throw new Exception(string.Join("; ", fail));
        return "«рандомные враги» и «random enemies» → «rando enemy»: SimpleEnemyRando совпадает по обоим словам; словосочетания и украинский тоже";
    }
}
