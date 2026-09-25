/* Создано build/site-examples.py из исходников ModLaunch — не править руками. */
const MS_EXAMPLES = [
 {
  "id": "blank",
  "game": "valheim",
  "code": "# Мой мод на ModScript. Справка по командам — вкладка «Справка».\nmod \"Мой мод\"\nversion 1.0.0\nauthor \"Я\"\nabout \"Что делает мод — одной строкой\"\ngame valheim\n\n# Уберите # в начале строки, чтобы включить команду.\n# needs RandyKnapp-EquipmentAndQuickSlots\n# config \"BepInEx.cfg\" [Logging.Console] Enabled = true",
  "title": {
   "ru": "Чистый лист",
   "en": "Blank page"
  },
  "text": {
   "ru": "Пустой мод с подсказками — для своей идеи.",
   "en": "An empty mod with hints — for your own idea."
  }
 },
 {
  "id": "sdv-seeds",
  "game": "stardew-valley",
  "code": "# Stardew Valley: весенние семена вдвое дешевле.\n# Собирается в пакет Content Patcher — его ставит сам ModLaunch.\nmod \"Дешёвые семена\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Весенние семена стоят вдвое меньше\"\ngame stardew-valley\n\nlet base = 40\nlet sale = 2\nlet price = $base / $sale\n\n# Пастернак, фасоль, цветная капуста, картофель, тюльпан, капуста кейл\nfor seed in 472 473 474 475 427 477 {\n  edit \"Data/Objects\" \"$seed\" Price = $price\n}\nprint \"Новая цена: $price\"",
  "title": {
   "ru": "Дешёвые семена",
   "en": "Cheap seeds"
  },
  "text": {
   "ru": "Переменные, арифметика и цикл: одна строка меняет цену шести семян.",
   "en": "Variables, math and a loop: one line changes six seed prices."
  }
 },
 {
  "id": "sdv-dialogue",
  "game": "stardew-valley",
  "code": "# Stardew Valley: новые реплики жителей по сезону и погоде.\nmod \"Весенние приветствия\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Жители по-новому здороваются весной и в дождь\"\ngame stardew-valley\n\nwhen Season = spring {\n  dialogue \"Abigail\" \"Mon\" = \"Весна! Пойдём искать аметисты в шахтах?\"\n  dialogue \"Sebastian\" \"Tue\" = \"Даже весной я бы лучше остался в подвале.\"\n  dialogue \"Penny\" \"Wed\" = \"Дети сегодня весь урок смотрели в окно на цветы.\"\n}\n\nwhen Weather = rain {\n  dialogue \"Abigail\" \"Thu\" = \"Обожаю дождь — идеальная погода для приключений.\"\n}",
  "title": {
   "ru": "Весенние приветствия",
   "en": "Spring greetings"
  },
  "text": {
   "ru": "Новые реплики жителей — с условиями по сезону и погоде.",
   "en": "New villager lines with season and weather conditions."
  }
 },
 {
  "id": "sdv-portrait",
  "game": "stardew-valley",
  "code": "# Stardew Valley: свой портрет персонажа.\n# Положите картинку abigail.png в папку files проекта\n# (кнопка «Файлы мода»), размер — как у оригинала (128×…).\nmod \"Мой портрет Эбигейл\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Заменяет портрет Эбигейл\"\ngame stardew-valley\n\nimage \"Portraits/Abigail\" from \"abigail.png\"",
  "title": {
   "ru": "Свой портрет",
   "en": "Custom portrait"
  },
  "text": {
   "ru": "Замена картинки персонажа своим файлом.",
   "en": "Replace a character picture with your file."
  }
 },
 {
  "id": "lc-friends",
  "game": "lethal-company",
  "code": "# Lethal Company: сборка для игры с друзьями.\n# Мод без своего кода — только список модов (как сборка на Thunderstore).\nmod \"Вечер с друзьями\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Больше игроков, заход в идущую игру, костюмы и удобные слоты\"\ngame lethal-company\n\nneeds notnotnotswipez-MoreCompany anormaltwig-LateCompany x753-More_Suits\nneeds tinyhoot-ShipLoot FlipMods-ReservedFlashlightSlot",
  "title": {
   "ru": "Вечер с друзьями",
   "en": "Night with friends"
  },
  "text": {
   "ru": "Сборка модов для Lethal Company — одним пакетом.",
   "en": "A Lethal Company modpack in one package."
  }
 },
 {
  "id": "valheim-builder",
  "game": "valheim",
  "code": "# Valheim: стройка без боли — моды и настройки одним пакетом.\nmod \"Комфортная стройка\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Посадка чего угодно, быстрые слоты, общие сундуки\"\ngame valheim\n\nneeds Advize-PlantEverything Advize-PlantEasily\nneeds RandyKnapp-EquipmentAndQuickSlots MSchmoecker-MultiUserChest\n\n# Настройка BepInEx: без окна консоли.\nconfig \"BepInEx.cfg\" [Logging.Console] Enabled = false",
  "title": {
   "ru": "Комфортная стройка",
   "en": "Comfy building"
  },
  "text": {
   "ru": "Моды для стройки и настройка BepInEx вместе.",
   "en": "Building mods and a BepInEx setting together."
  }
 },
 {
  "id": "bepinex-debug",
  "game": "risk-of-rain-2",
  "code": "# Любая игра на BepInEx: окно консоли и подробный журнал —\n# удобно, когда нужно понять, какой мод ломает игру.\nmod \"Режим отладки\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Консоль BepInEx и журнал Unity в LogOutput.log\"\ngame risk-of-rain-2\n\nconfig \"BepInEx.cfg\" [Logging.Console] Enabled = true\nconfig \"BepInEx.cfg\" [Logging.Console] LogLevels = All\nconfig \"BepInEx.cfg\" [Logging.Disk] WriteUnityLog = true",
  "title": {
   "ru": "Режим отладки",
   "en": "Debug mode"
  },
  "text": {
   "ru": "Консоль и подробный журнал — найти мод, который ломает игру.",
   "en": "Console and verbose log — find the mod that breaks the game."
  }
 },
 {
  "id": "peak-pack",
  "game": "peak",
  "code": "# PEAK: восхождение вчетвером и больше.\nmod \"Большая экспедиция\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Больше игроков, рюкзак удобнее, шляпы для всех\"\ngame peak\n\nneeds glarmer-PEAK_Unlimited nickklmao-EasyBackpack TeddyBRB-Too_Many_Hats",
  "title": {
   "ru": "Большая экспедиция",
   "en": "Big expedition"
  },
  "text": {
   "ru": "PEAK на большую компанию.",
   "en": "PEAK for a big group."
  }
 },
 {
  "id": "sdv-fish",
  "game": "stardew-valley",
  "code": "# Stardew Valley: речная рыба стоит вдвое дороже.\nmod \"Дорогая рыба\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Речная рыба продаётся вдвое дороже\"\ngame stardew-valley\n\nlet bonus = 2\n\n# Окунь, лещ, радужная форель, карп, сом, солнечник\nfor fish in 136 132 138 142 143 145 {\n  edit \"Data/Objects\" \"$fish\" Price = 60 * $bonus\n}",
  "title": {
   "ru": "Дорогая рыба",
   "en": "Pricey fish"
  },
  "text": {
   "ru": "Цикл по рыбе и арифметика: вся речная рыба дороже вдвое.",
   "en": "A loop over fish and math: river fish sell for double."
  }
 },
 {
  "id": "sdv-difficulty",
  "game": "stardew-valley",
  "code": "# Stardew Valley: цены зависят от выбранного режима.\n# Поменяйте mode на \"easy\" или \"hard\" и нажмите «Установить».\nmod \"Режим сложности\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Лёгкий или хардкорный режим цен на семена\"\ngame stardew-valley\n\nlet mode = \"hard\"\n\nif $mode == \"hard\" {\n  let price = 80\n  print \"Хардкор: семена дороже\"\n}\nelse if $mode == \"easy\" {\n  let price = 10\n  print \"Лёгкий режим: семена почти даром\"\n}\nelse {\n  let price = 40\n}\n\nfor seed in 472 473 474 475 {\n  edit \"Data/Objects\" \"$seed\" Price = $price\n}",
  "title": {
   "ru": "Режим сложности",
   "en": "Difficulty mode"
  },
  "text": {
   "ru": "if и else: одна переменная переключает «лёгкий» и «хардкорный» режим цен.",
   "en": "if and else: one variable switches easy and hardcore prices."
  }
 },
 {
  "id": "sdv-letter",
  "game": "stardew-valley",
  "code": "# Stardew Valley: письмо, которое приходит в первый же день.\nmod \"Письмо от ModLaunch\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Приветственное письмо в почтовом ящике\"\ngame stardew-valley\n\nmail \"ModLaunch.Welcome\" = \"Привет, фермер!^Пусть урожай будет богатым, а рыба — крупной.^   -- ModLaunch\"\n\n# Игра 1.6 сама кладёт письмо в ящик утром.\nentry \"Data/TriggerActions\" \"ModLaunch.Welcome\" = json \"{\\\"Id\\\":\\\"ModLaunch.Welcome\\\",\\\"Trigger\\\":\\\"DayStarted\\\",\\\"Actions\\\":[\\\"AddMail Current ModLaunch.Welcome\\\"]}\"",
  "title": {
   "ru": "Письмо от ModLaunch",
   "en": "A letter from ModLaunch"
  },
  "text": {
   "ru": "Своё письмо в почтовом ящике — приходит само через Data/TriggerActions.",
   "en": "Your own letter in the mailbox, delivered via Data/TriggerActions."
  }
 },
 {
  "id": "sdv-rain",
  "game": "stardew-valley",
  "code": "# Stardew Valley: в дождь у жителей свои реплики — на каждый день.\nmod \"Дождливые разговоры\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Жители говорят о погоде в дождливые дни\"\ngame stardew-valley\n\nwhen Weather = rain {\n  for npc in Abigail Sam Sebastian Penny Leah {\n    for day in Mon Tue Wed Thu Fri Sat Sun {\n      dialogue \"$npc\" \"$day\" = \"Слышишь, как стучит дождь? Самое время для чашки чая.\"\n    }\n  }\n}",
  "title": {
   "ru": "Дождливые разговоры",
   "en": "Rainy chats"
  },
  "text": {
   "ru": "Вложенные циклы: жители говорят о дожде каждый день недели.",
   "en": "Nested loops: villagers talk about rain every weekday."
  }
 },
 {
  "id": "sdv-food",
  "game": "stardew-valley",
  "code": "# Stardew Valley: салат, пицца и суп восстанавливают больше энергии.\nmod \"Сытная еда\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Блюда дают больше энергии\"\ngame stardew-valley\n\nlet boost = 2\n\n# Салат, пицца, томатный суп... (edibility — «сытость» блюда)\nedit \"Data/Objects\" \"196\" Edibility = 45 * $boost\nedit \"Data/Objects\" \"206\" Edibility = 60 * $boost\nedit \"Data/Objects\" \"218\" Edibility = 30 * $boost",
  "title": {
   "ru": "Сытная еда",
   "en": "Hearty food"
  },
  "text": {
   "ru": "Поле Edibility: блюда дают больше энергии.",
   "en": "The Edibility field: dishes restore more energy."
  }
 },
 {
  "id": "sdv-names",
  "game": "stardew-valley",
  "code": "# Stardew Valley: свои названия предметов.\nmod \"Свои названия\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Переименовывает несколько предметов\"\ngame stardew-valley\n\nedit \"Data/Objects\" \"472\" DisplayName = \"Пастернак «Люкс»\"\nedit \"Data/Objects\" \"24\" DisplayName = \"Золотой пастернак\"\nedit \"Data/Objects\" \"128\" DisplayName = \"Колючий иглобрюх\"",
  "title": {
   "ru": "Свои названия",
   "en": "Custom names"
  },
  "text": {
   "ru": "DisplayName: переименовать предметы как хочется.",
   "en": "DisplayName: rename items however you like."
  }
 },
 {
  "id": "bepinex-quiet",
  "game": "valheim",
  "code": "# Любая игра на BepInEx: журнал без лишнего шума.\nmod \"Тихий журнал\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Журнал BepInEx — только ошибки и предупреждения\"\ngame valheim\n\nconfig \"BepInEx.cfg\" [Logging.Console] Enabled = false\nconfig \"BepInEx.cfg\" [Logging.Disk] LogLevels = Error, Warning\nconfig \"BepInEx.cfg\" [Logging.Disk] WriteUnityLog = false",
  "title": {
   "ru": "Тихий журнал",
   "en": "Quiet log"
  },
  "text": {
   "ru": "Только ошибки в журнале и без окна консоли — игра грузится быстрее.",
   "en": "Only errors in the log and no console window — faster loading."
  }
 },
 {
  "id": "bepinex-compat",
  "game": "lethal-company",
  "code": "# Любая игра на BepInEx: частая починка «моды не грузятся».\nmod \"Совместимость BepInEx\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"HideManagerGameObject — починка для части модов\"\ngame lethal-company\n\nconfig \"BepInEx.cfg\" [Chainloader] HideManagerGameObject = true",
  "title": {
   "ru": "Совместимость",
   "en": "Compatibility"
  },
  "text": {
   "ru": "Прячет служебный объект BepInEx — лечит часть модов, которые «пропадают» при загрузке.",
   "en": "Hides the BepInEx manager object — fixes some mods that vanish on load."
  }
 },
 {
  "id": "files-readme",
  "game": "valheim",
  "code": "# write — создать файл внутри мода. Удобно для заметок и настроек.\nmod \"Мод с файлами\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Пример команды write\"\ngame valheim\n\nlet players = 4\n\nwrite \"MyMod/readme.txt\" = \"Этот мод собран в ModLaunch Creator Hub.\\nИгроков: $players\"\nwrite \"MyMod/settings.json\" = json \"{\\\"players\\\": 4, \\\"hardcore\\\": false}\"\n\nif $players > 2 {\n  print \"Большая компания — $players игрока\"\n}",
  "title": {
   "ru": "Мод с файлами",
   "en": "Mod with files"
  },
  "text": {
   "ru": "write создаёт файлы прямо из скрипта: заметки, настройки, JSON.",
   "en": "write creates files right from the script: notes, settings, JSON."
  }
 },
 {
  "id": "ror2-content",
  "game": "risk-of-rain-2",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Больше контента\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Новые выжившие, предметы и сохранение\"\ngame risk-of-rain-2\n\nneeds TeamMoonstorm-Starstorm2 KingEnderBrine-ProperSave KomradeSpectre-Aetherium\nneeds Zenithrium-VanillaVoid DropPod-LookingGlass",
  "title": {
   "ru": "Больше контента",
   "en": "More content"
  },
  "text": {
   "ru": "Новые герои, предметы и сохранение забега.",
   "en": "New survivors, items and run saving."
  }
 },
 {
  "id": "repo-crew",
  "game": "repo",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Команда R.E.P.O.\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Шапки, улучшения и настройки\"\ngame repo\n\nneeds YMC_MHZ-MoreHead BULLETBOT-MoreUpgrades Zehs-ExtractionPointConfirmButton\nneeds nickklmao-REPOConfig",
  "title": {
   "ru": "Команда R.E.P.O.",
   "en": "R.E.P.O. crew"
  },
  "text": {
   "ru": "Шапки, улучшения и удобные настройки.",
   "en": "Hats, upgrades and handy settings."
  }
 },
 {
  "id": "cw-viral",
  "game": "content-warning",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Вирусная съёмка\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Больше игроков и удобный магазин\"\ngame content-warning\n\nneeds MaxWasUnavailable-Virality CommanderCat101-ContentSettings hyydsz-ShopUtils",
  "title": {
   "ru": "Вирусная съёмка",
   "en": "Going viral"
  },
  "text": {
   "ru": "Больше игроков и удобный магазин.",
   "en": "More players and a handy shop."
  }
 },
 {
  "id": "ultrakill-arsenal",
  "game": "ultrakill",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Арсенал ULTRAKILL\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Новые пушки и уровни\"\ngame ultrakill\n\nneeds EternalsTeam-PluginConfigurator EternalsTeam-AngryLevelLoader Hydraxous-UltraFunGuns",
  "title": {
   "ru": "Арсенал ULTRAKILL",
   "en": "ULTRAKILL arsenal"
  },
  "text": {
   "ru": "Новые пушки и свои уровни.",
   "en": "New guns and custom levels."
  }
 },
 {
  "id": "rounds-party",
  "game": "rounds",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Вечеринка ROUNDS\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Больше игроков и новых карт\"\ngame rounds\n\nneeds olavim-RoundsWithFriends willis81808-UnboundLib Pykess-ModdingUtils\nneeds XAngelMoonX-CR",
  "title": {
   "ru": "Вечеринка ROUNDS",
   "en": "ROUNDS party"
  },
  "text": {
   "ru": "Больше игроков и сотни новых карт.",
   "en": "More players and hundreds of new cards."
  }
 },
 {
  "id": "dsp-factory",
  "game": "dyson-sphere-program",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Фабрика мечты\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Мультиплеер, удобства и чертежи\"\ngame dyson-sphere-program\n\nneeds CommonAPI-CommonAPI nebula-NebulaMultiplayerMod soarqin-UXAssist\nneeds kremnev8-BlueprintTweaks",
  "title": {
   "ru": "Фабрика мечты",
   "en": "Dream factory"
  },
  "text": {
   "ru": "Мультиплеер, удобства и чертежи.",
   "en": "Multiplayer, comfort and blueprints."
  }
 },
 {
  "id": "h3vr-range",
  "game": "h3vr",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Тир H3VR\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Библиотеки, оружие и мультиплеер\"\ngame h3vr\n\nneeds nrgill28-Sodalite devyndamonster-OtherLoader VIP-H3MP",
  "title": {
   "ru": "Тир H3VR",
   "en": "H3VR range"
  },
  "text": {
   "ru": "Библиотеки, новое оружие и мультиплеер.",
   "en": "Libraries, new guns and multiplayer."
  }
 },
 {
  "id": "lc-horror",
  "game": "lethal-company",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Больше страха\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Мимики, луны и монстры\"\ngame lethal-company\n\nneeds x753-Mimics Magic_Wesley-Wesleys_Moons TwinDimensionalProductions-CoilHeadStare",
  "title": {
   "ru": "Больше страха",
   "en": "More horror"
  },
  "text": {
   "ru": "Мимики, новые луны и монстры.",
   "en": "Mimics, new moons and monsters."
  }
 },
 {
  "id": "valheim-qol",
  "game": "valheim",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Удобный Valheim\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Слоты, раскладка и автопочинка\"\ngame valheim\n\nneeds shudnal-ExtraSlots Goldenrevolver-Quick_Stack_Store_Sort_Trash_Restock Tekla-AutoRepair",
  "title": {
   "ru": "Удобный Valheim",
   "en": "Comfy Valheim"
  },
  "text": {
   "ru": "Лишние слоты, быстрая раскладка и автопочинка.",
   "en": "Extra slots, quick stacking and auto repair."
  }
 },
 {
  "id": "gtfo-squad",
  "game": "gtfo",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Отряд GTFO\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Метки, боты и попадания\"\ngame gtfo\n\nneeds Localia-PingEverything easternunit100-BetterBots randomuserhi-KillIndicatorFix",
  "title": {
   "ru": "Отряд GTFO",
   "en": "GTFO squad"
  },
  "text": {
   "ru": "Метки на всём, боты умнее, честные попадания.",
   "en": "Ping anything, smarter bots, fair hit markers."
  }
 },
 {
  "id": "outward-explorer",
  "game": "outward",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Исследователь Outward\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Миникарта, интерфейс боя и карта\"\ngame outward\n\nneeds sinai-dev-SideLoader sinai-dev-CombatHUD sinai-dev-More_Map_Details\nneeds sinai-dev-Minimap",
  "title": {
   "ru": "Исследователь Outward",
   "en": "Outward explorer"
  },
  "text": {
   "ru": "Миникарта, боевой интерфейс и подробная карта.",
   "en": "Minimap, combat HUD and a detailed map."
  }
 },
 {
  "id": "silksong-helper",
  "game": "hollow-knight-silksong",
  "code": "# Сборка: моды одним пакетом (как сборка на Thunderstore).\nmod \"Помощник Silksong\"\nversion 1.0.0\nauthor \"ModLaunch\"\nabout \"Меню модов и здоровье врагов\"\ngame hollow-knight-silksong\n\nneeds silksong_modding-ModMenu XiaohaiMod-ShowDamage_HealthBar",
  "title": {
   "ru": "Помощник Silksong",
   "en": "Silksong helper"
  },
  "text": {
   "ru": "Меню модов и полоска здоровья врагов.",
   "en": "Mod menu and enemy health bars."
  }
 }
];
const MS_DOCS = [
 {
  "title": {
   "ru": "Описание мода",
   "en": "Mod info"
  },
  "items": [
   {
    "cmd": "mod",
    "syntax": {
     "ru": "mod \"Название\"",
     "en": "mod \"Name\""
    },
    "text": {
     "ru": "Название мода. Обязательно.",
     "en": "Mod name. Required."
    }
   },
   {
    "cmd": "version",
    "syntax": {
     "ru": "version 1.0.0",
     "en": "version 1.0.0"
    },
    "text": {
     "ru": "Версия: три числа через точку.",
     "en": "Version: three numbers."
    }
   },
   {
    "cmd": "author",
    "syntax": {
     "ru": "author \"Имя\"",
     "en": "author \"Name\""
    },
    "text": {
     "ru": "Автор.",
     "en": "Author."
    }
   },
   {
    "cmd": "about",
    "syntax": {
     "ru": "about \"Что делает мод\"",
     "en": "about \"What it does\""
    },
    "text": {
     "ru": "Описание в одну строку.",
     "en": "One-line description."
    }
   },
   {
    "cmd": "game",
    "syntax": {
     "ru": "game valheim",
     "en": "game valheim"
    },
    "text": {
     "ru": "Для какой игры мод. Обязательно. Id игры: stardew-valley, valheim, lethal-company, repo, peak…",
     "en": "Target game. Required. Ids: stardew-valley, valheim, lethal-company, repo, peak…"
    }
   },
   {
    "cmd": "icon",
    "syntax": {
     "ru": "icon \"icon.png\"",
     "en": "icon \"icon.png\""
    },
    "text": {
     "ru": "Свой значок из папки files (для Thunderstore).",
     "en": "Your icon from the files folder (for Thunderstore)."
    }
   },
   {
    "cmd": "needs",
    "syntax": {
     "ru": "needs Автор-Мод Автор-Мод2",
     "en": "needs Author-Mod Author-Mod2"
    },
    "text": {
     "ru": "Моды, без которых ваш не работает. ModLaunch поставит их сам.",
     "en": "Mods yours depends on. ModLaunch installs them."
    }
   }
  ]
 },
 {
  "title": {
   "ru": "Логика",
   "en": "Logic"
  },
  "items": [
   {
    "cmd": "let",
    "syntax": {
     "ru": "let цена = 40 / 2",
     "en": "let price = 40 / 2"
    },
    "text": {
     "ru": "Переменная. Дальше пишите $цена или ${цена}.",
     "en": "A variable. Then write $price or ${price}."
    }
   },
   {
    "cmd": "if",
    "syntax": {
     "ru": "if $x > 3 { … } else if $x == 3 { … } else { … }",
     "en": "if $x > 3 { … } else if $x == 3 { … } else { … }"
    },
    "text": {
     "ru": "Условие: == != > < >= <= и contains (содержит).",
     "en": "A condition: == != > < >= <= and contains."
    }
   },
   {
    "cmd": "else",
    "syntax": {
     "ru": "else { … }",
     "en": "else { … }"
    },
    "text": {
     "ru": "Что сделать, если условие не выполнилось.",
     "en": "What to do when the condition is false."
    }
   },
   {
    "cmd": "for",
    "syntax": {
     "ru": "for x in 1 2 3 { … }   for i from 1 to 10 { … }",
     "en": "for x in 1 2 3 { … }   for i from 1 to 10 { … }"
    },
    "text": {
     "ru": "Повторить команды для каждого значения.",
     "en": "Repeat commands for every value."
    }
   },
   {
    "cmd": "when",
    "syntax": {
     "ru": "when Season = spring { … }",
     "en": "when Season = spring { … }"
    },
    "text": {
     "ru": "Условие Content Patcher: сезон, погода, день недели и т. д.",
     "en": "A Content Patcher condition: season, weather, weekday, etc."
    }
   },
   {
    "cmd": "print",
    "syntax": {
     "ru": "print \"Цена: $цена\"",
     "en": "print \"Price: $price\""
    },
    "text": {
     "ru": "Показать значение справа от редактора — для проверки.",
     "en": "Show a value next to the editor — for checking."
    }
   }
  ]
 },
 {
  "title": {
   "ru": "Stardew Valley (Content Patcher)",
   "en": "Stardew Valley (Content Patcher)"
  },
  "items": [
   {
    "cmd": "edit",
    "syntax": {
     "ru": "edit \"Data/Objects\" \"472\" Price = 20",
     "en": "edit \"Data/Objects\" \"472\" Price = 20"
    },
    "text": {
     "ru": "Поменять поле записи в данных игры.",
     "en": "Change a field of a game data entry."
    }
   },
   {
    "cmd": "entry",
    "syntax": {
     "ru": "entry \"Data/mail\" \"key\" = \"текст\"",
     "en": "entry \"Data/mail\" \"key\" = \"text\""
    },
    "text": {
     "ru": "Добавить или заменить запись целиком.",
     "en": "Add or replace a whole entry."
    }
   },
   {
    "cmd": "dialogue",
    "syntax": {
     "ru": "dialogue \"Abigail\" \"Mon\" = \"Привет!\"",
     "en": "dialogue \"Abigail\" \"Mon\" = \"Hi!\""
    },
    "text": {
     "ru": "Реплика жителя на день недели или событие.",
     "en": "A villager line for a weekday or event."
    }
   },
   {
    "cmd": "mail",
    "syntax": {
     "ru": "mail \"myLetter\" = \"Текст письма\"",
     "en": "mail \"myLetter\" = \"Letter text\""
    },
    "text": {
     "ru": "Новое письмо в почтовом ящике.",
     "en": "A new letter in the mailbox."
    }
   },
   {
    "cmd": "image",
    "syntax": {
     "ru": "image \"Portraits/Abigail\" from \"abigail.png\"",
     "en": "image \"Portraits/Abigail\" from \"abigail.png\""
    },
    "text": {
     "ru": "Заменить картинку игры своим файлом из папки files.",
     "en": "Replace a game image with your file from the files folder."
    }
   }
  ]
 },
 {
  "title": {
   "ru": "BepInEx и файлы",
   "en": "BepInEx and files"
  },
  "items": [
   {
    "cmd": "config",
    "syntax": {
     "ru": "config \"BepInEx.cfg\" [Logging.Console] Enabled = true",
     "en": "config \"BepInEx.cfg\" [Logging.Console] Enabled = true"
    },
    "text": {
     "ru": "Строка в настройках BepInEx или другого мода (BepInEx/config).",
     "en": "A line in BepInEx or mod settings (BepInEx/config)."
    }
   },
   {
    "cmd": "ini",
    "syntax": {
     "ru": "ini \"Game.ini\" [Section] Key = value",
     "en": "ini \"Game.ini\" [Section] Key = value"
    },
    "text": {
     "ru": "Строка в .ini-файле внутри папки игры.",
     "en": "A line in an .ini file inside the game folder."
    }
   },
   {
    "cmd": "copy",
    "syntax": {
     "ru": "copy \"MyMod.dll\" to \"plugins/MyMod/MyMod.dll\"",
     "en": "copy \"MyMod.dll\" to \"plugins/MyMod/MyMod.dll\""
    },
    "text": {
     "ru": "Положить свой файл из папки files в мод.",
     "en": "Put your file from the files folder into the mod."
    }
   },
   {
    "cmd": "write",
    "syntax": {
     "ru": "write \"MyMod/notes.txt\" = \"текст $x\"",
     "en": "write \"MyMod/notes.txt\" = \"text $x\""
    },
    "text": {
     "ru": "Создать файл в моде прямо из скрипта.",
     "en": "Create a file in the mod right from the script."
    }
   },
   {
    "cmd": "json",
    "syntax": {
     "ru": "entry \"Data/…\" \"key\" = json \"{…}\"",
     "en": "entry \"Data/…\" \"key\" = json \"{…}\""
    },
    "text": {
     "ru": "Сложное значение — объект или список в формате JSON.",
     "en": "A complex value — a JSON object or list."
    }
   }
  ]
 }
];
const MS_ERRORS = {
 "ru": {
  "quote": "Не закрыта кавычка",
  "extraBrace": "Лишняя «}»",
  "openBrace": "Блок не закрыт — не хватает «}»",
  "unknownVar": "Нет переменной ${x} — объявите её через let",
  "noValue": "После «=» нужно значение",
  "noName": "Нет названия: добавьте строку mod \"Название\"",
  "noGame": "Не указана игра: добавьте строку game <id>",
  "version": "Версия — три числа: 1.0.0",
  "tooMuch": "Слишком много команд — проверьте циклы",
  "generic": "Ошибка: {x}",
  "args": "Команде {x} не хватает значения",
  "blockHere": "У команды {x} не бывает блока { }",
  "let": "Пишите так: let имя = значение",
  "forBlock": "После for нужен блок { … }",
  "for": "Пишите так: for x in 1 2 3 { или for i from 1 to 10 {",
  "forRange": "Цикл длиннее 1000 шагов",
  "whenBlock": "После when нужен блок { … }",
  "when": "Пишите так: when Season = spring {",
  "edit": "Пишите так: edit \"Data/Objects\" \"472\" Price = 20",
  "entry": "Пишите так: entry \"Data/mail\" \"key\" = \"текст\"",
  "dialogue": "Пишите так: dialogue \"Abigail\" \"Mon\" = \"текст\"",
  "mail": "Пишите так: mail \"id\" = \"текст\"",
  "image": "Пишите так: image \"Portraits/Abigail\" from \"file.png\"",
  "config": "Пишите так: config \"file.cfg\" [Section] Key = value",
  "ini": "Пишите так: ini \"file.ini\" [Section] Key = value",
  "copy": "Пишите так: copy \"file\" to \"path/file\"",
  "path": "Путь должен быть внутри мода (без .. и C:\\)",
  "unknown": "Нет команды «{x}»",
  "if": "Пишите так: if $x > 3 {",
  "ifBlock": "После if и else нужен блок { … }",
  "else": "else без if перед ним",
  "write": "Пишите так: write \"file.txt\" = \"текст\"",
  "json": "В json \"…\" ошибка — проверьте кавычки и скобки"
 },
 "en": {
  "quote": "Unclosed quote",
  "extraBrace": "Extra “}”",
  "openBrace": "Block not closed — missing “}”",
  "unknownVar": "No variable ${x} — declare it with let",
  "noValue": "A value is needed after “=”",
  "noName": "No name: add mod \"Name\"",
  "noGame": "No game: add game <id>",
  "version": "Version is three numbers: 1.0.0",
  "tooMuch": "Too many commands — check your loops",
  "generic": "Error: {x}",
  "args": "{x} is missing a value",
  "blockHere": "{x} can't have a { } block",
  "let": "Write: let name = value",
  "forBlock": "for needs a { … } block",
  "for": "Write: for x in 1 2 3 { or for i from 1 to 10 {",
  "forRange": "Loop longer than 1000 steps",
  "whenBlock": "when needs a { … } block",
  "when": "Write: when Season = spring {",
  "edit": "Write: edit \"Data/Objects\" \"472\" Price = 20",
  "entry": "Write: entry \"Data/mail\" \"key\" = \"text\"",
  "dialogue": "Write: dialogue \"Abigail\" \"Mon\" = \"text\"",
  "mail": "Write: mail \"id\" = \"text\"",
  "image": "Write: image \"Portraits/Abigail\" from \"file.png\"",
  "config": "Write: config \"file.cfg\" [Section] Key = value",
  "ini": "Write: ini \"file.ini\" [Section] Key = value",
  "copy": "Write: copy \"file\" to \"path/file\"",
  "path": "The path must stay inside the mod (no .. or C:\\)",
  "unknown": "No command “{x}”",
  "if": "Write: if $x > 3 {",
  "ifBlock": "if and else need a { … } block",
  "else": "else without an if before it",
  "write": "Write: write \"file.txt\" = \"text\"",
  "json": "Error in json \"…\" — check quotes and brackets"
 }
};
