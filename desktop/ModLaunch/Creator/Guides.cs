using ModLaunch.Core;

namespace ModLaunch.Creator;

/// <summary>Инструмент моддера: что это, зачем и где взять.</summary>
public sealed record Tool(string Id, string Name, string Ru, string En, string Url, string Group)
{
    public string Desc => I18n.Lang == "en" ? En : Ru;
}

public sealed record Step(string Ru, string En)
{
    public string Text => I18n.Lang == "en" ? En : Ru;
}

/// <summary>Пошаговый путь: от идеи до работающего мода. Platforms — для каких игр показывать в первую очередь.</summary>
public sealed record Guide(string Id, string TitleRu, string TitleEn, string IntroRu, string IntroEn, string[] Platforms, Step[] Steps)
{
    public string Title => I18n.Lang == "en" ? TitleEn : TitleRu;
    public string Intro => I18n.Lang == "en" ? IntroEn : IntroRu;
}

/// <summary>Справочные данные Creator Hub: гайды, инструменты, чек-лист выпуска.</summary>
public static class Guides
{
    public static readonly string[] ToolGroups = ["code", "inspect", "models", "textures", "audio", "docs", "publish"];

    public static readonly Tool[] Tools =
    [
        new("vs", "Visual Studio Community", "Среда для кода на C#: подсветка, подсказки, отладка. Бесплатна для личных проектов.", "A C# IDE: highlighting, completion, debugging. Free for personal projects.", "https://visualstudio.microsoft.com/vs/community/", "code"),
        new("vscode", "VS Code", "Лёгкий редактор. С расширением C# Dev Kit подходит для модов не хуже.", "A light editor. With the C# Dev Kit extension it works well for mods.", "https://code.visualstudio.com/", "code"),
        new("rider", "JetBrains Rider", "Быстрая среда для C# и Unity; для некоммерческого использования бесплатна.", "A fast IDE for C# and Unity; free for non-commercial use.", "https://www.jetbrains.com/rider/", "code"),
        new("dotnet", ".NET SDK", "Без него не соберётся проект на C#. Creator Hub собирает мод командой dotnet build.", "Required to build C# projects. Creator Hub builds your mod with dotnet build.", "https://dotnet.microsoft.com/download", "code"),

        new("dnspy", "dnSpyEx", "Открыть библиотеки игры и увидеть её код: имена классов и методов для патчей Harmony.", "Open the game's assemblies and read its code: class and method names for Harmony patches.", "https://github.com/dnSpyEx/dnSpy", "inspect"),
        new("ilspy", "ILSpy", "Ещё один декомпилятор .NET: проще и быстрее для простого чтения кода.", "Another .NET decompiler: simpler and quicker for just reading code.", "https://github.com/icsharpcode/ILSpy", "inspect"),
        new("unityexplorer", "UnityExplorer", "Плагин, который показывает объекты, компоненты и значения прямо в запущенной игре.", "A plugin that shows objects, components and values inside the running game.", "https://github.com/sinai-dev/UnityExplorer", "inspect"),
        new("assetripper", "AssetRipper", "Вытаскивает из игры на Unity модели, текстуры, звуки и сцены как проект.", "Extracts models, textures, sounds and scenes from a Unity game as a project.", "https://github.com/AssetRipper/AssetRipper", "inspect"),
        new("assetstudio", "AssetStudio", "Просмотр и экспорт ресурсов Unity: удобно найти нужную текстуру или модель.", "Browse and export Unity resources: handy for finding the texture or model you need.", "https://github.com/Perfare/AssetStudio", "inspect"),
        new("uabea", "UABEA", "Правка ресурсов Unity (assets и bundle) и их повторная упаковка.", "Edit Unity assets and bundles and pack them again.", "https://github.com/nesrak1/UABEA", "inspect"),

        new("blender", "Blender", "Бесплатный пакет для 3D: моделирование, развёртка, анимация, экспорт в FBX и glTF.", "A free 3D suite: modelling, UV unwrapping, animation, export to FBX and glTF.", "https://www.blender.org/download/", "models"),
        new("unity", "Unity Editor (архив версий)", "Нужна версия, как у игры: в ней собирают AssetBundle с моделями и префабами.", "You need the game's Unity version: it builds AssetBundles with models and prefabs.", "https://unity.com/releases/editor/archive", "models"),

        new("gimp", "GIMP", "Бесплатный редактор картинок: слои, прозрачность, экспорт в PNG и DDS.", "A free image editor: layers, transparency, export to PNG and DDS.", "https://www.gimp.org/", "textures"),
        new("krita", "Krita", "Бесплатная рисовалка: удобна для спрайтов, иконок и портретов.", "Free painting software: good for sprites, icons and portraits.", "https://krita.org/", "textures"),
        new("paintnet", "Paint.NET", "Лёгкий редактор для Windows; с плагином умеет DDS.", "A light Windows editor; handles DDS with a plugin.", "https://www.getpaint.net/", "textures"),

        new("audacity", "Audacity", "Бесплатный звуковой редактор: обрезать, нормализовать громкость, сохранить в OGG и WAV.", "A free audio editor: trim, normalise volume, save to OGG and WAV.", "https://www.audacityteam.org/", "audio"),

        new("bepinexdocs", "BepInEx — документация", "Как устроены плагины, конфиги и журнал.", "How plugins, configs and the log work.", "https://docs.bepinex.dev/", "docs"),
        new("harmony", "Harmony — документация", "Всё про патчи: Prefix, Postfix, Transpiler, инъекция полей.", "Everything about patches: Prefix, Postfix, Transpiler, field injection.", "https://harmony.pardeike.net/", "docs"),
        new("smapi", "Stardew Valley Wiki — моддинг", "Главная справка по SMAPI и содержимому игры.", "The main reference for SMAPI and game content.", "https://stardewvalleywiki.com/Modding:Index", "docs"),
        new("cp", "Content Patcher", "Моды без кода: JSON-правила для изменения данных, текстур и диалогов Stardew.", "Code-free mods: JSON rules that change Stardew data, textures and dialogue.", "https://github.com/Pathoschild/StardewMods/tree/develop/ContentPatcher#readme", "docs"),
        new("hkapi", "Hollow Knight Modding API", "Исходники и описание хуков API для Hollow Knight.", "Source and hook reference for the Hollow Knight Modding API.", "https://github.com/hk-modding/api", "docs"),

        new("thunderstore", "Thunderstore", "Публикация модов BepInEx: нужен manifest.json, README и значок.", "Publish BepInEx mods: needs manifest.json, README and an icon.", "https://thunderstore.io/", "publish"),
        new("nexus", "Nexus Mods", "Публикация модов на Nexus: страница мода, скриншоты, описание, файлы.", "Publish mods on Nexus: mod page, screenshots, description, files.", "https://www.nexusmods.com/", "publish"),
    ];

    public static readonly Guide[] All =
    [
        new("script", "Мод без кода (ModScript)", "A mod without code (ModScript)",
            "Самый быстрый путь: несколько строк на ModScript, и мод готов — для Stardew, Valheim, Lethal Company и других.",
            "The fastest way: a few lines of ModScript and the mod is done — for Stardew, Valheim, Lethal Company and others.",
            ["bepinex", "smapi", "hk"],
            [
                new("Откройте «Примеры» и нажмите «Попробовать» у подходящего — получится готовый проект.", "Open “Examples” and click “Try” on a fitting one — you get a ready project."),
                new("В разделе «Моды» поменяйте значения: цены, настройки, зависимости. Справа сразу видно ошибки.", "In “Mods” change the values: prices, settings, dependencies. Errors show up on the right at once."),
                new("Нажмите «Поставить в игру» — мод ляжет в нужную папку, а настройки допишутся в конфиги.", "Click “Install” — the mod lands in the right folder and settings are written to the configs."),
                new("Запустите игру из ModLaunch и проверьте. Что-то не так — правьте скрипт и ставьте снова.", "Launch the game from ModLaunch and check. Something wrong — edit the script and install again."),
                new("Готово? «Опубликовать» — мод появится в ModLaunch Hub, а «Упаковка» соберёт zip для Thunderstore или Nexus.", "Done? “Publish” puts it in ModLaunch Hub, and “Packaging” builds a zip for Thunderstore or Nexus."),
            ]),

        new("bepinex", "Мод на C# для Unity-игры (BepInEx)", "A C# mod for a Unity game (BepInEx)",
            "Для Lethal Company, Valheim, R.E.P.O., Risk of Rain 2 и других игр на BepInEx: свой код, патчи Harmony, свои предметы.",
            "For Lethal Company, Valheim, R.E.P.O., Risk of Rain 2 and other BepInEx games: your own code, Harmony patches, your own items.",
            ["bepinex"],
            [
                new("Поставьте .NET SDK и редактор (Visual Studio Community или VS Code) — ссылки в «Инструментах».", "Install the .NET SDK and an editor (Visual Studio Community or VS Code) — links in “Tools”."),
                new("В разделе «Код» создайте проект для своей игры. Мастер сам пропишет путь к игре и библиотеки Unity.", "In “Code” create a project for your game. The wizard fills in the game path and Unity libraries."),
                new("Откройте dnSpyEx, загрузите Assembly-CSharp.dll из папки игры и найдите метод, поведение которого хотите изменить.", "Open dnSpyEx, load Assembly-CSharp.dll from the game folder and find the method whose behaviour you want to change."),
                new("Возьмите из «Библиотеки кода» патч Harmony и подставьте имена класса и метода.", "Take a Harmony patch from the “Code library” and fill in the class and method names."),
                new("Нажмите «Собрать»: мод сам скопируется в BepInEx\\plugins. Запустите игру и смотрите журнал BepInEx.", "Click “Build”: the mod copies itself to BepInEx\\plugins. Start the game and watch the BepInEx log."),
                new("UnityExplorer поможет разобраться, какие объекты и значения есть в запущенной игре.", "UnityExplorer helps you see which objects and values exist in the running game."),
                new("Когда всё работает — «Упаковка» соберёт пакет Thunderstore с manifest.json и значком.", "When it works, “Packaging” builds a Thunderstore package with manifest.json and an icon."),
            ]),

        new("smapi", "Мод для Stardew Valley", "A mod for Stardew Valley",
            "Два пути: Content Patcher (JSON, без кода) или SMAPI (C#, любая механика).",
            "Two paths: Content Patcher (JSON, no code) or SMAPI (C#, any mechanic).",
            ["smapi"],
            [
                new("Нужны только данные, текстуры или диалоги? Берите ModScript: он соберёт пакет Content Patcher за вас.", "Only data, textures or dialogue? Use ModScript: it builds a Content Patcher pack for you."),
                new("Нужна своя механика — в разделе «Код» создайте проект SMAPI. Папка игры и ModBuildConfig уже настроены.", "Need your own mechanic — create a SMAPI project in “Code”. The game folder and ModBuildConfig are already set up."),
                new("События игры (день, нажатие кнопки, загрузка сохранения) — в «Библиотеке кода», раздел SMAPI.", "Game events (day, button press, save loaded) — in the “Code library”, SMAPI section."),
                new("Свои картинки и звуки кладите в assets/, а в код вставляйте приём «Своя картинка из папки мода».", "Put your images and sounds in assets/ and use the “Your own image from the mod folder” snippet."),
                new("«Собрать» копирует мод прямо в Mods. Смотрите журнал SMAPI: ошибки там подписаны строкой кода.", "“Build” copies the mod to Mods. Watch the SMAPI log: errors name the code line."),
                new("Выпуск: «Упаковка» соберёт zip в нужной структуре для Nexus Mods.", "Release: “Packaging” builds a zip in the layout Nexus Mods expects."),
            ]),

        new("hk", "Мод для Hollow Knight", "A mod for Hollow Knight",
            "Hollow Knight Modding API: хуки на события игры, перехват методов через MMHOOK, предзагрузка объектов.",
            "Hollow Knight Modding API: hooks on game events, method interception through MMHOOK, object preloading.",
            ["hk"],
            [
                new("Установите Modding API из ModLaunch (страница игры → загрузчик).", "Install the Modding API from ModLaunch (game page → loader)."),
                new("В разделе «Код» создайте проект Hollow Knight: в нём уже есть класс Mod и ссылки на библиотеки игры.", "In “Code” create a Hollow Knight project: it already has a Mod class and references to the game libraries."),
                new("Хуки (душа, урон, обновление героя) и перехват методов On.* — в «Библиотеке кода», раздел Hollow Knight.", "Hooks (soul, damage, hero update) and On.* method interception — in the “Code library”, Hollow Knight section."),
                new("Собрать — мод копируется в Managed\\Mods\\<имя>. Запускайте игру, список модов виден в главном меню.", "Build — the mod copies to Managed\\Mods\\<name>. Start the game; the mod list is in the main menu."),
                new("Для публикации упакуйте мод в zip и выложите на Nexus Mods или в список модов API.", "To publish, pack the mod into a zip and upload it to Nexus Mods or the API mod list."),
            ]),

        new("model", "Своя 3D-модель в игре на Unity", "Your own 3D model in a Unity game",
            "От Blender до префаба в игре: оружие, предмет, декор, персонаж.",
            "From Blender to a prefab in the game: a weapon, item, decoration, character.",
            ["bepinex", "hk"],
            [
                new("Сделайте модель в Blender. Масштаб — 1 единица = 1 метр; примените масштаб и вращение (Ctrl+A), проверьте нормали.", "Model it in Blender. Scale is 1 unit = 1 metre; apply scale and rotation (Ctrl+A), check the normals."),
                new("Развёртка (UV) и текстуры: один-два материала на модель — так игре проще. Назовите объекты без пробелов.", "Unwrap (UV) and texture: one or two materials per model keeps things simple. Name objects without spaces."),
                new("Экспортируйте в FBX: «Apply Transform», «Selected Objects». Файл положите в «Модели и ассеты».", "Export to FBX with “Apply Transform” and “Selected Objects”. Put the file in “Models & assets”."),
                new("Установите ту же версию Unity, что у игры: она видна в свойствах UnityPlayer.dll (вкладка «Подробно»).", "Install the same Unity version as the game: it is shown in the UnityPlayer.dll properties (Details tab)."),
                new("В Unity импортируйте FBX, настройте материал, соберите префаб. В инспекторе префаба внизу задайте AssetBundle, например mymod.", "In Unity import the FBX, set up the material, build a prefab. At the bottom of the prefab inspector set an AssetBundle, for example mymod."),
                new("Соберите бандл: BuildPipeline.BuildAssetBundles(папка, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64).", "Build the bundle: BuildPipeline.BuildAssetBundles(folder, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64)."),
                new("Добавьте .bundle в проект кнопкой «Добавить в проект» и вставьте приём «Модель из AssetBundle» из библиотеки кода.", "Add the .bundle to the project with “Add to project” and paste the “Model from an AssetBundle” snippet."),
                new("Модель розовая? Шейдер из Unity не нашёлся в игре. Возьмите материал игры или Shader.Find(\"...\") с шейдером, который в ней есть.", "Model is pink? The shader from Unity is missing in the game. Use a game material or Shader.Find(\"...\") with a shader the game has."),
            ]),

        new("textures", "Подмена текстур, иконок и портретов", "Replacing textures, icons and portraits",
            "Свой скин, иконка предмета, портрет персонажа — без правки самой игры.",
            "Your own skin, item icon, character portrait — without editing the game itself.",
            ["bepinex", "smapi", "hk"],
            [
                new("Найдите оригинал: AssetStudio или AssetRipper вытащат текстуры игры на Unity, а у Stardew они лежат в Content.", "Find the original: AssetStudio or AssetRipper extract a Unity game's textures; in Stardew they are in Content."),
                new("Рисуйте поверх оригинала в GIMP или Krita с тем же размером и расположением частей.", "Draw over the original in GIMP or Krita keeping the same size and layout."),
                new("Сохраните в PNG с прозрачностью и положите в «Модели и ассеты».", "Save as PNG with transparency and put it in “Models & assets”."),
                new("Unity-игры: приём «Подменить текстуру из PNG» из библиотеки кода. Stardew: команда image в ModScript или AssetRequested в SMAPI.", "Unity games: the “Replace a texture from a PNG” snippet. Stardew: the image command in ModScript or AssetRequested in SMAPI."),
                new("Если картинка не меняется, проверьте, что объект ещё не создан к моменту подмены: делайте её на смене сцены.", "If nothing changes, the object may be created before your swap: do it on scene change."),
            ]),

        new("audio", "Свои звуки и музыка", "Your own sounds and music",
            "Звук выстрела, музыка в меню, голос персонажа.",
            "A gunshot, menu music, a character's voice.",
            ["bepinex", "smapi", "hk"],
            [
                new("Подготовьте звук в Audacity: обрежьте тишину, выровняйте громкость (Normalize −1 дБ), частота 44100 Гц.", "Prepare the sound in Audacity: trim silence, normalise volume (−1 dB), 44100 Hz."),
                new("Сохраните в OGG Vorbis: он маленький и играет везде. WAV — для коротких эффектов без сжатия.", "Save as OGG Vorbis: it is small and plays everywhere. WAV is for short uncompressed effects."),
                new("Положите файл в «Модели и ассеты» → «Добавить в проект».", "Put the file in “Models & assets” → “Add to project”."),
                new("В коде используйте приём «Звук из .ogg / .wav». Для Stardew — Content API, звуковые данные лежат в Data/AudioChanges.", "In code use the “Sound from .ogg / .wav” snippet. For Stardew use the Content API; audio data lives in Data/AudioChanges."),
            ]),
    ];

    public static readonly Step[] Checklist =
    [
        new("У мода есть понятное имя, версия вида 1.0.0 и короткое описание (до 250 знаков).", "The mod has a clear name, a version like 1.0.0 and a short description (up to 250 characters)."),
        new("Есть README: что делает мод, как настроить, что нужно для работы.", "There is a README: what the mod does, how to configure it, what it needs."),
        new("Значок 256×256 и 2–3 скриншота: по ним мод и выбирают.", "A 256×256 icon and 2–3 screenshots: that is how mods get picked."),
        new("Указаны все зависимости (загрузчик, чужие моды) с версиями.", "All dependencies (the loader, other mods) are listed with versions."),
        new("Проверено на чистой установке игры, без других модов.", "Tested on a clean game install without other mods."),
        new("Проверено вместе с самыми популярными модами игры: нет ли конфликтов.", "Tested together with the game's most popular mods: no conflicts."),
        new("Сохранения не ломаются: мод можно выключить и удалить без вреда.", "Saves are not broken: the mod can be disabled and removed safely."),
        new("Есть список изменений (changelog) — игроки любят знать, что нового.", "There is a changelog — players like to know what is new."),
        new("Права на чужие картинки, модели и звуки есть: используйте только своё или с разрешением.", "You have rights to any images, models and sounds you use: use only your own or with permission."),
    ];
}
