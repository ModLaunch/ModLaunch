using System.Text.RegularExpressions;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Creator;

/// <summary>
/// Шаблон мода. Raw — текст с подстановками: {{name}}, {{game}}, {{author}}, {{hints}} (примеры команд
/// для загрузчика игры) и {{t:ключ}} (строка интерфейса). Code — тот же текст со значениями по умолчанию.
/// </summary>
public sealed record Template(string Id, string Game, string Raw)
{
    /// <summary>
    /// Готовый код: имя — название примера на языке интерфейса (у пустого — «Мой мод» / «My mod»),
    /// автор — сам человек, а не «ModLaunch», игра — игра шаблона.
    /// </summary>
    public string Code => Templates.Apply(this, Id == "blank" ? Templates.DefaultName() : I18n.T($"cr.ex.{Id}"), Game, Templates.DefaultAuthor());
}

/// <summary>«Примочки» — готовые примеры модов на ModScript: открыть, поменять, поставить.</summary>
public static partial class Templates
{
    public static readonly Template[] All =
    [
        // 12.0: пустой проект — без «Мой мод»/«Я» по-русски и без привязки к Valheim: всё подставляется.
        new("blank", "valheim", """
            # {{t:v12h2.tpl.blank.head}}
            mod "{{name}}"
            version 1.0.0
            author "{{author}}"
            about "{{t:v12h2.tpl.blank.about}}"
            game {{game}}

            # {{t:v12h2.tpl.blank.uncomment}}
            {{hints}}
            """),

        new("code-style", "valheim", """
            # ModScript в стиле Python и C++: функции, циклы, списки и стандартная библиотека.
            # Полный список функций — «Справка → Стандартная библиотека», подсказки — Ctrl+Пробел.
            import math
            using namespace std

            mod "Баланс оружия"
            version 1.0.0
            author "{{author}}"
            about "Урон считается формулой, а не руками"
            game valheim

            def damage(level, base = 10) {
                return round(base * math.pow(1.15, level - 1))
            }

            let tiers = ["bronze", "iron", "silver", "black_metal"]
            for i in range(len(tiers)) {
                let name = tiers[i]
                let dmg = std::clamp(damage(i + 1), 10, 200)
                config "weapons.cfg" [$name] Damage = $dmg
                print(format("{:<12} → {}", name, dmg))
            }

            let rarity = weighted_choice(["common", "rare", "epic"], [70, 25, 5])
            let id = slug("Баланс оружия") + "-" + sha256(today())[0:6]
            print("id:", id, "rarity:", rarity)
            """),

        new("sdv-seeds", "stardew-valley", """
            # Stardew Valley: весенние семена вдвое дешевле.
            # Собирается в пакет Content Patcher — его ставит сам ModLaunch.
            mod "Дешёвые семена"
            version 1.0.0
            author "ModLaunch"
            about "Весенние семена стоят вдвое меньше"
            game stardew-valley

            let base = 40
            let sale = 2
            let price = $base / $sale

            # Пастернак, фасоль, цветная капуста, картофель, тюльпан, капуста кейл
            for seed in 472 473 474 475 427 477 {
              edit "Data/Objects" "$seed" Price = $price
            }
            print "Новая цена: $price"
            """),

        new("sdv-dialogue", "stardew-valley", """
            # Stardew Valley: новые реплики жителей по сезону и погоде.
            mod "Весенние приветствия"
            version 1.0.0
            author "ModLaunch"
            about "Жители по-новому здороваются весной и в дождь"
            game stardew-valley

            when Season = spring {
              dialogue "Abigail" "Mon" = "Весна! Пойдём искать аметисты в шахтах?"
              dialogue "Sebastian" "Tue" = "Даже весной я бы лучше остался в подвале."
              dialogue "Penny" "Wed" = "Дети сегодня весь урок смотрели в окно на цветы."
            }

            when Weather = rain {
              dialogue "Abigail" "Thu" = "Обожаю дождь — идеальная погода для приключений."
            }
            """),

        new("sdv-portrait", "stardew-valley", """
            # Stardew Valley: свой портрет персонажа.
            # Положите картинку abigail.png в папку files проекта
            # (кнопка «Файлы мода»), размер — как у оригинала (128×…).
            mod "Мой портрет Эбигейл"
            version 1.0.0
            author "ModLaunch"
            about "Заменяет портрет Эбигейл"
            game stardew-valley

            image "Portraits/Abigail" from "abigail.png"
            """),

        new("lc-friends", "lethal-company", """
            # Lethal Company: сборка для игры с друзьями.
            # Мод без своего кода — только список модов (как сборка на Thunderstore).
            mod "Вечер с друзьями"
            version 1.0.0
            author "ModLaunch"
            about "Больше игроков, заход в идущую игру, костюмы и удобные слоты"
            game lethal-company

            needs notnotnotswipez-MoreCompany anormaltwig-LateCompany x753-More_Suits
            needs tinyhoot-ShipLoot FlipMods-ReservedFlashlightSlot
            """),

        new("valheim-builder", "valheim", """
            # Valheim: стройка без боли — моды и настройки одним пакетом.
            mod "Комфортная стройка"
            version 1.0.0
            author "ModLaunch"
            about "Посадка чего угодно, быстрые слоты, общие сундуки"
            game valheim

            needs Advize-PlantEverything Advize-PlantEasily
            needs RandyKnapp-EquipmentAndQuickSlots MSchmoecker-MultiUserChest

            # Настройка BepInEx: без окна консоли.
            config "BepInEx.cfg" [Logging.Console] Enabled = false
            """),

        new("bepinex-debug", "risk-of-rain-2", """
            # Любая игра на BepInEx: окно консоли и подробный журнал —
            # удобно, когда нужно понять, какой мод ломает игру.
            mod "Режим отладки"
            version 1.0.0
            author "ModLaunch"
            about "Консоль BepInEx и журнал Unity в LogOutput.log"
            game risk-of-rain-2

            config "BepInEx.cfg" [Logging.Console] Enabled = true
            config "BepInEx.cfg" [Logging.Console] LogLevels = All
            config "BepInEx.cfg" [Logging.Disk] WriteUnityLog = true
            """),

        new("peak-pack", "peak", """
            # PEAK: восхождение вчетвером и больше.
            mod "Большая экспедиция"
            version 1.0.0
            author "ModLaunch"
            about "Больше игроков, рюкзак удобнее, шляпы для всех"
            game peak

            needs glarmer-PEAK_Unlimited nickklmao-EasyBackpack TeddyBRB-Too_Many_Hats
            """),

        new("sdv-fish", "stardew-valley", """
            # Stardew Valley: речная рыба стоит вдвое дороже.
            mod "Дорогая рыба"
            version 1.0.0
            author "ModLaunch"
            about "Речная рыба продаётся вдвое дороже"
            game stardew-valley

            let bonus = 2

            # Окунь, лещ, радужная форель, карп, сом, солнечник
            for fish in 136 132 138 142 143 145 {
              edit "Data/Objects" "$fish" Price = 60 * $bonus
            }
            """),
        new("sdv-difficulty", "stardew-valley", """
            # Stardew Valley: цены зависят от выбранного режима.
            # Поменяйте mode на "easy" или "hard" и нажмите «Установить».
            mod "Режим сложности"
            version 1.0.0
            author "ModLaunch"
            about "Лёгкий или хардкорный режим цен на семена"
            game stardew-valley

            let mode = "hard"

            if $mode == "hard" {
              let price = 80
              print "Хардкор: семена дороже"
            }
            else if $mode == "easy" {
              let price = 10
              print "Лёгкий режим: семена почти даром"
            }
            else {
              let price = 40
            }

            for seed in 472 473 474 475 {
              edit "Data/Objects" "$seed" Price = $price
            }
            """),
        new("sdv-letter", "stardew-valley", """
            # Stardew Valley: письмо, которое приходит в первый же день.
            mod "Письмо от ModLaunch"
            version 1.0.0
            author "ModLaunch"
            about "Приветственное письмо в почтовом ящике"
            game stardew-valley

            mail "ModLaunch.Welcome" = "Привет, фермер!^Пусть урожай будет богатым, а рыба — крупной.^   -- ModLaunch"

            # Игра 1.6 сама кладёт письмо в ящик утром.
            entry "Data/TriggerActions" "ModLaunch.Welcome" = json "{\"Id\":\"ModLaunch.Welcome\",\"Trigger\":\"DayStarted\",\"Actions\":[\"AddMail Current ModLaunch.Welcome\"]}"
            """),
        new("sdv-rain", "stardew-valley", """
            # Stardew Valley: в дождь у жителей свои реплики — на каждый день.
            mod "Дождливые разговоры"
            version 1.0.0
            author "ModLaunch"
            about "Жители говорят о погоде в дождливые дни"
            game stardew-valley

            when Weather = rain {
              for npc in Abigail Sam Sebastian Penny Leah {
                for day in Mon Tue Wed Thu Fri Sat Sun {
                  dialogue "$npc" "$day" = "Слышишь, как стучит дождь? Самое время для чашки чая."
                }
              }
            }
            """),
        new("sdv-food", "stardew-valley", """
            # Stardew Valley: салат, пицца и суп восстанавливают больше энергии.
            mod "Сытная еда"
            version 1.0.0
            author "ModLaunch"
            about "Блюда дают больше энергии"
            game stardew-valley

            let boost = 2

            # Салат, пицца, томатный суп... (edibility — «сытость» блюда)
            edit "Data/Objects" "196" Edibility = 45 * $boost
            edit "Data/Objects" "206" Edibility = 60 * $boost
            edit "Data/Objects" "218" Edibility = 30 * $boost
            """),
        new("sdv-names", "stardew-valley", """
            # Stardew Valley: свои названия предметов.
            mod "Свои названия"
            version 1.0.0
            author "ModLaunch"
            about "Переименовывает несколько предметов"
            game stardew-valley

            edit "Data/Objects" "472" DisplayName = "Пастернак «Люкс»"
            edit "Data/Objects" "24" DisplayName = "Золотой пастернак"
            edit "Data/Objects" "128" DisplayName = "Колючий иглобрюх"
            """),
        new("bepinex-quiet", "valheim", """
            # Любая игра на BepInEx: журнал без лишнего шума.
            mod "Тихий журнал"
            version 1.0.0
            author "ModLaunch"
            about "Журнал BepInEx — только ошибки и предупреждения"
            game valheim

            config "BepInEx.cfg" [Logging.Console] Enabled = false
            config "BepInEx.cfg" [Logging.Disk] LogLevels = Error, Warning
            config "BepInEx.cfg" [Logging.Disk] WriteUnityLog = false
            """),
        new("bepinex-compat", "lethal-company", """
            # Любая игра на BepInEx: частая починка «моды не грузятся».
            mod "Совместимость BepInEx"
            version 1.0.0
            author "ModLaunch"
            about "HideManagerGameObject — починка для части модов"
            game lethal-company

            config "BepInEx.cfg" [Chainloader] HideManagerGameObject = true
            """),
        new("files-readme", "valheim", """
            # write — создать файл внутри мода. Удобно для заметок и настроек.
            mod "Мод с файлами"
            version 1.0.0
            author "ModLaunch"
            about "Пример команды write"
            game valheim

            let players = 4

            write "MyMod/readme.txt" = "Этот мод собран в ModLaunch Creator Hub.\nИгроков: $players"
            write "MyMod/settings.json" = json "{\"players\": 4, \"hardcore\": false}"

            if $players > 2 {
              print "Большая компания — $players игрока"
            }
            """),
        new("ror2-content", "risk-of-rain-2", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Больше контента"
            version 1.0.0
            author "ModLaunch"
            about "Новые выжившие, предметы и сохранение"
            game risk-of-rain-2

            needs TeamMoonstorm-Starstorm2 KingEnderBrine-ProperSave KomradeSpectre-Aetherium
            needs Zenithrium-VanillaVoid DropPod-LookingGlass
            """),
        new("repo-crew", "repo", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Команда R.E.P.O."
            version 1.0.0
            author "ModLaunch"
            about "Шапки, улучшения и настройки"
            game repo

            needs YMC_MHZ-MoreHead BULLETBOT-MoreUpgrades Zehs-ExtractionPointConfirmButton
            needs nickklmao-REPOConfig
            """),
        new("cw-viral", "content-warning", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Вирусная съёмка"
            version 1.0.0
            author "ModLaunch"
            about "Больше игроков и удобный магазин"
            game content-warning

            needs MaxWasUnavailable-Virality CommanderCat101-ContentSettings hyydsz-ShopUtils
            """),
        new("ultrakill-arsenal", "ultrakill", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Арсенал ULTRAKILL"
            version 1.0.0
            author "ModLaunch"
            about "Новые пушки и уровни"
            game ultrakill

            needs EternalsTeam-PluginConfigurator EternalsTeam-AngryLevelLoader Hydraxous-UltraFunGuns
            """),
        new("rounds-party", "rounds", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Вечеринка ROUNDS"
            version 1.0.0
            author "ModLaunch"
            about "Больше игроков и новых карт"
            game rounds

            needs olavim-RoundsWithFriends willis81808-UnboundLib Pykess-ModdingUtils
            needs XAngelMoonX-CR
            """),
        new("dsp-factory", "dyson-sphere-program", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Фабрика мечты"
            version 1.0.0
            author "ModLaunch"
            about "Мультиплеер, удобства и чертежи"
            game dyson-sphere-program

            needs CommonAPI-CommonAPI nebula-NebulaMultiplayerMod soarqin-UXAssist
            needs kremnev8-BlueprintTweaks
            """),
        new("h3vr-range", "h3vr", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Тир H3VR"
            version 1.0.0
            author "ModLaunch"
            about "Библиотеки, оружие и мультиплеер"
            game h3vr

            needs nrgill28-Sodalite devyndamonster-OtherLoader VIP-H3MP
            """),
        new("lc-horror", "lethal-company", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Больше страха"
            version 1.0.0
            author "ModLaunch"
            about "Мимики, луны и монстры"
            game lethal-company

            needs x753-Mimics Magic_Wesley-Wesleys_Moons TwinDimensionalProductions-CoilHeadStare
            """),
        new("valheim-qol", "valheim", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Удобный Valheim"
            version 1.0.0
            author "ModLaunch"
            about "Слоты, раскладка и автопочинка"
            game valheim

            needs shudnal-ExtraSlots Goldenrevolver-Quick_Stack_Store_Sort_Trash_Restock Tekla-AutoRepair
            """),
        new("gtfo-squad", "gtfo", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Отряд GTFO"
            version 1.0.0
            author "ModLaunch"
            about "Метки, боты и попадания"
            game gtfo

            needs Localia-PingEverything easternunit100-BetterBots randomuserhi-KillIndicatorFix
            """),
        new("outward-explorer", "outward", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Исследователь Outward"
            version 1.0.0
            author "ModLaunch"
            about "Миникарта, интерфейс боя и карта"
            game outward

            needs sinai-dev-SideLoader sinai-dev-CombatHUD sinai-dev-More_Map_Details
            needs sinai-dev-Minimap
            """),
        new("silksong-helper", "hollow-knight-silksong", """
            # Сборка: моды одним пакетом (как сборка на Thunderstore).
            mod "Помощник Silksong"
            version 1.0.0
            author "ModLaunch"
            about "Меню модов и здоровье врагов"
            game hollow-knight-silksong

            needs silksong_modding-ModMenu XiaohaiMod-ShowDamage_HealthBar
            """),
    ];

    public static Template? ById(string id) => All.FirstOrDefault(t => t.Id == id);

    public static readonly string[] Categories = ["all", "packs", "stardew", "bepinex", "logic"];

    /// <summary>Раздел примочки: сборка (только needs), Stardew, настройки BepInEx или логика языка.</summary>
    public static string Category(Template t)
    {
        var body = t.Raw.Split('\n').Select(l => l.Trim()).Where(l => l != "" && !l.StartsWith('#'))
            .Where(l => !new[] { "mod ", "version ", "author ", "about ", "game " }.Any(l.StartsWith)).ToList();
        if (body.Count > 0 && body.All(l => l.StartsWith("needs "))) return "packs";
        if (t.Raw.Contains("if $") || t.Raw.Contains("write ") || t.Id == "blank") return "logic";
        if (t.Game == "stardew-valley") return "stardew";
        return "bepinex";
    }

    // ---------------------------------------------------------------- 12.0: подстановки и мастер

    /// <summary>Имя нового мода по умолчанию — на языке интерфейса (раньше всегда «Мой мод»).</summary>
    public static string DefaultName() => I18n.T("v12h2.tpl.name");

    /// <summary>Автор по умолчанию: имя аккаунта, иначе «Я» / «Me» по языку интерфейса.</summary>
    public static string DefaultAuthor() =>
        Social.Account.SignedIn && Social.Account.Get().Name is { Length: > 0 } n ? n : I18n.T("v12h2.tpl.author");

    [GeneratedRegex(@"\{\{(name|game|author|hints|t:[\w.\-]+)\}\}")]
    private static partial Regex Slot();
    [GeneratedRegex(@"^([ \t]*)mod[ \t]+""(?:[^""\\]|\\.)*""", RegexOptions.Multiline)]
    private static partial Regex ModLine();
    [GeneratedRegex(@"^([ \t]*)author[ \t]+""(?:[^""\\]|\\.)*""", RegexOptions.Multiline)]
    private static partial Regex AuthorLine();
    [GeneratedRegex(@"^([ \t]*)game[ \t]+\S+", RegexOptions.Multiline)]
    private static partial Regex GameLine();

    /// <summary>Подставить значения в текст шаблона: {{name}}, {{game}}, {{author}}, {{hints}}, {{t:ключ}}.</summary>
    public static string Fill(string raw, string name, string game, string author) => Slot().Replace(raw, m => m.Groups[1].Value switch
    {
        "name" => Text(name),
        "author" => Text(author),
        "game" => Word(game),
        "hints" => Hints(game),
        var key => Text(I18n.T(key[2..])),
    });

    /// <summary>
    /// Код нового проекта из шаблона: подстановки, а у примеров без них — строки mod, author и game
    /// заменяются на то, что выбрал человек (имя, он сам, его игра).
    /// </summary>
    public static string Apply(Template t, string name, string game, string author)
    {
        if (name.Trim() == "") name = DefaultName();
        if (author.Trim() == "") author = DefaultAuthor();
        if (Word(game) == "") game = t.Game;
        var code = Fill(t.Raw, name, game, author);
        // Через функцию, а не строку замены: в имени может быть «$1».
        code = ModLine().Replace(code, m => $"{m.Groups[1].Value}mod \"{Text(name)}\"", 1);
        code = AuthorLine().Replace(code, m => $"{m.Groups[1].Value}author \"{Text(author)}\"", 1);
        code = GameLine().Replace(code, m => $"{m.Groups[1].Value}game {Word(game)}", 1);
        return code;
    }

    /// <summary>
    /// Шаблоны для игры (мастер нового проекта): сначала свои примеры этой игры, затем общие —
    /// настройки BepInEx для любой игры на BepInEx и «Мод с файлами» для любой. Пустой — отдельно.
    /// </summary>
    public static List<Template> For(string game)
    {
        var bepinex = GameCatalog.ById(game)?.Loader == LoaderKind.Bepinex;
        return All.Where(t => t.Id != "blank" && (t.Game == game || t.Id == "files-readme" || bepinex && t.Id.StartsWith("bepinex-", StringComparison.Ordinal)))
            .OrderBy(t => t.Game == game ? 0 : 1).ToList();
    }

    /// <summary>Закомментированные примеры команд под загрузчик игры: пустой проект собирается сразу.</summary>
    static string Hints(string game) => GameCatalog.ById(game)?.Loader switch
    {
        LoaderKind.Smapi => "# edit \"Data/Objects\" \"472\" Price = 10\n# dialogue \"Abigail\" \"Mon\" = \"Hello!\"",
        LoaderKind.Bepinex => "# needs Author-ModName\n# config \"BepInEx.cfg\" [Logging.Console] Enabled = true",
        _ => "# write \"readme.txt\" = \"Hello!\"\n# copy \"my-file.txt\" to \"my-file.txt\"",
    };

    /// <summary>Строка для кавычек ModScript: без переносов, «\» и «"» экранированы, «$» убран (это подстановка переменной).</summary>
    public static string Text(string s) =>
        s.Replace('\r', ' ').Replace('\n', ' ').Replace("$", "").Replace("\\", "\\\\").Replace("\"", "\\\"").Trim();

    /// <summary>Id игры как слово ModScript: только буквы, цифры, «.», «_» и «-».</summary>
    public static string Word(string s) => Regex.Replace(s.Trim().ToLowerInvariant(), @"[^a-z0-9._\-]", "");

    [SelfTest]
    static string TemplatesFillPlaceholders()
    {
        var blank = ById("blank")!;
        if (!blank.Raw.Contains("{{name}}") || blank.Code.Contains("{{")) throw new Exception("blank placeholders not filled");
        // Имя с кавычками и «$» не ломает скрипт, игра и автор — выбранные.
        var code = Apply(blank, "Big \"Mod\" $x", "stardew-valley", "Mira");
        var b = ModScript.Compile(code);
        if (!b.Ok) throw new Exception(string.Join("; ", b.Diags.Select(d => d.Line + " " + d.Message)));
        if (b.Name != "Big \"Mod\" x" || b.Game != "stardew-valley" || b.Author != "Mira") throw new Exception($"{b.Name}|{b.Game}|{b.Author}");
        if (!code.Contains("# edit ")) throw new Exception("no SMAPI hints");
        // Пример без подстановок: строки mod/author/game заменяются, остальное — как было.
        var seeds = ModScript.Compile(Apply(ById("sdv-seeds")!, "Cheap", "stardew-valley", "Me"));
        if (!seeds.Ok || seeds.Name != "Cheap" || seeds.Author != "Me" || seeds.Changes.Count == 0) throw new Exception("sdv-seeds apply");
        // Готовый код примера: автор — сам человек (не «ModLaunch»), имя — название примера, а не русская строка из кода.
        var ready = ModScript.Compile(ById("sdv-seeds")!.Code);
        if (ready.Author != DefaultAuthor() || ready.Name != Text(I18n.T("cr.ex.sdv-seeds"))) throw new Exception($"Code: {ready.Name}|{ready.Author}");
        // Общие шаблоны BepInEx — для любой игры на BepInEx, но не для Stardew.
        if (!For("lethal-company").Any(t => t.Id == "bepinex-debug") || For("stardew-valley").Any(t => t.Id.StartsWith("bepinex-")))
            throw new Exception("For(game) filter");
        if (For("stardew-valley").FirstOrDefault()?.Game != "stardew-valley") throw new Exception("own game first");
        var all = All.Count(t => ModScript.Compile(t.Code).Ok);
        if (all != All.Length) throw new Exception($"{All.Length - all} templates do not compile");
        return $"{All.Length} templates fill and compile";
    }
}
