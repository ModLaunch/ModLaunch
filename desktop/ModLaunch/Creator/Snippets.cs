using System.Text.Json.Nodes;
using ModLaunch.Core;
using ModLaunch.Games;

namespace ModLaunch.Creator;

/// <summary>Готовый кусок кода: что делает, на каком загрузчике работает и сам текст.</summary>
public sealed record Snippet(string Id, string Platform, string Ru, string En, string DescRu, string DescEn, string Code, bool Own = false)
{
    public string Title => I18n.Lang == "en" ? En : Ru;
    public string Desc => I18n.Lang == "en" ? DescEn : DescRu;
}

/// <summary>
/// Библиотека «частей кода»: самые частые приёмы моддинга на C# — от конфига и горячих клавиш до
/// патчей Harmony и загрузки моделей из AssetBundle. Плюс свои сниппеты (%APPDATA%\ModHub\creator-snippets.json).
/// </summary>
public static class Snippets
{
    public static readonly string[] Platforms = ["bepinex", "smapi", "hk", "unity"];

    /// <summary>Какие платформы подходят игре: её загрузчик плюс общая Unity (кроме Stardew).</summary>
    public static string[] ForGame(GameDef? game) => game?.Loader switch
    {
        LoaderKind.Smapi => ["smapi"],
        LoaderKind.HkApi => ["hk", "unity"],
        LoaderKind.Bepinex => ["bepinex", "unity"],
        _ => Platforms,
    };

    static string FilePath => Path.Combine(Paths.DataDir, "creator-snippets.json");

    public static List<Snippet> Own()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return (JsonNode.Parse(File.ReadAllText(FilePath)) as JsonArray ?? []).Select(n => new Snippet(n.Str("id") ?? Guid.NewGuid().ToString("N")[..8], n.Str("platform") ?? "unity", n.Str("title") ?? "", n.Str("title") ?? "",
                n.Str("desc") ?? "", n.Str("desc") ?? "", n.Str("code") ?? "", true)).ToList();
        }
        catch { return []; }
    }

    static void SaveOwn(List<Snippet> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, new JsonArray(list.Select(s => (JsonNode)new JsonObject { ["id"] = s.Id, ["platform"] = s.Platform, ["title"] = s.Ru, ["desc"] = s.DescRu, ["code"] = s.Code }).ToArray()).ToJsonString());
    }

    public static void Save(string? id, string platform, string title, string desc, string code)
    {
        var list = Own();
        var sid = id ?? Guid.NewGuid().ToString("N")[..8];
        list.RemoveAll(s => s.Id == sid);
        list.Insert(0, new Snippet(sid, platform, title.Trim(), title.Trim(), desc.Trim(), desc.Trim(), code, true));
        SaveOwn(list);
    }

    public static void Remove(string id) => SaveOwn(Own().Where(s => s.Id != id).ToList());

    public static List<Snippet> All() => Own().Concat(Builtin).ToList();

    public static readonly Snippet[] Builtin =
    [
        // ---------------------------------------------------------------- BepInEx
        new("bx-config", "bepinex", "Настройки в конфиге", "Config entries",
            "Значения, которые игрок меняет в BepInEx/config/…cfg, с подсказкой и границами.",
            "Values players edit in BepInEx/config/*.cfg, with a description and limits.",
            """
            // Поля класса
            private ConfigEntry<float> _speed;
            private ConfigEntry<bool> _debug;

            // В Awake()
            _speed = Config.Bind("General", "Speed", 1.5f,
                new ConfigDescription("Множитель скорости", new AcceptableValueRange<float>(0.1f, 5f)));
            _debug = Config.Bind("General", "Debug", false, "Подробный журнал");

            // Использование
            float s = _speed.Value;
            _speed.SettingChanged += (_, _) => Log.LogInfo($"Speed = {_speed.Value}");
            """),

        new("bx-hotkey", "bepinex", "Горячая клавиша с модификатором", "Hotkey with modifier",
            "Сочетание вроде Ctrl+F6, которое игрок может поменять в конфиге.",
            "A shortcut like Ctrl+F6 that players can rebind in the config.",
            """
            private ConfigEntry<KeyboardShortcut> _toggle;

            // В Awake()
            _toggle = Config.Bind("Keys", "Toggle", new KeyboardShortcut(KeyCode.F6, KeyCode.LeftControl), "Включить и выключить");

            // В Update()
            if (_toggle.Value.IsDown()) _enabled = !_enabled;
            """),

        new("bx-prefix", "bepinex", "Патч Harmony: до и после метода", "Harmony patch: prefix and postfix",
            "Меняет поведение метода игры. Prefix может изменить аргументы или пропустить оригинал (return false).",
            "Changes a game method. A prefix can edit arguments or skip the original (return false).",
            """
            [HarmonyPatch(typeof(Player), nameof(Player.TakeDamage))]
            static class Player_TakeDamage_Patch
            {
                // До метода: меняем аргумент. return false — оригинал не вызывается.
                static bool Prefix(Player __instance, ref float damage)
                {
                    damage *= 0.5f;
                    return true;
                }

                // После метода
                static void Postfix(Player __instance)
                {
                    Plugin.Log.LogInfo("Получен урон");
                }
            }
            """),

        new("bx-result", "bepinex", "Патч Harmony: изменить результат", "Harmony patch: change the result",
            "Postfix с __result: подменяет то, что метод вернул (грузоподъёмность, цену, шанс…).",
            "A postfix with __result swaps the method's return value (capacity, price, chance…).",
            """
            [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetTotalWeight))]
            static class Weight_Patch
            {
                static void Postfix(ref float __result)
                {
                    __result *= 0.5f; // вес вдвое меньше
                }
            }
            """),

        new("bx-private", "bepinex", "Приватные поля и методы", "Private fields and methods",
            "Три способа добраться до private: инъекция ___поле в патче, Traverse и AccessTools.",
            "Three ways to reach private members: ___field injection, Traverse and AccessTools.",
            """
            // 1) В патче: три подчёркивания перед именем поля
            static void Postfix(Player __instance, ref float ___m_health) { ___m_health = 100f; }

            // 2) Traverse — читать и писать без лишнего кода
            var hp = Traverse.Create(__instance).Field("m_health").GetValue<float>();
            Traverse.Create(__instance).Field("m_health").SetValue(hp + 10f);

            // 3) AccessTools — вызвать приватный метод
            var method = AccessTools.Method(typeof(Player), "UpdateFood");
            method.Invoke(__instance, new object[] { 0.1f });
            """),

        new("bx-find", "bepinex", "Найти объекты в сцене", "Find objects in the scene",
            "Поиск по типу, по имени и по иерархии.",
            "Find by type, by name and through the hierarchy.",
            """
            var enemies = Object.FindObjectsOfType<Enemy>();            // все объекты типа
            var door = GameObject.Find("MainDoor");                       // по имени (только активные)
            var hand = player.transform.Find("Rig/Hand");                 // дочерний по пути
            var light = player.GetComponentInChildren<Light>(true);       // в потомках, в том числе выключенных
            """),

        new("bx-bundle", "bepinex", "Модель из AssetBundle", "Model from an AssetBundle",
            "Загрузить префаб (модель, звук, интерфейс), собранный в Unity, и поставить в сцену. Версия Unity в проекте — как у игры.",
            "Load a prefab (model, sound, UI) built in Unity and place it in the scene. Use the same Unity version as the game.",
            """
            var dir = Path.GetDirectoryName(Info.Location);
            var bundle = AssetBundle.LoadFromFile(Path.Combine(dir, "assets", "mymod.bundle"));
            if (bundle == null) { Log.LogError("Не удалось загрузить бандл"); return; }

            var prefab = bundle.LoadAsset<GameObject>("Assets/MyMod/Cube.prefab");
            var obj = Object.Instantiate(prefab, new Vector3(0, 1, 0), Quaternion.identity);
            obj.name = "MyMod_Cube";
            """),

        new("bx-texture", "bepinex", "Подменить текстуру из PNG", "Replace a texture from a PNG",
            "Читает картинку с диска и ставит на материал. Нужна ссылка на UnityEngine.ImageConversionModule (мастер добавляет все модули).",
            "Reads an image from disk and applies it to a material. Needs UnityEngine.ImageConversionModule (the wizard adds all modules).",
            """
            static Texture2D LoadTexture(string path)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));
                tex.name = Path.GetFileNameWithoutExtension(path);
                return tex;
            }

            // Использование
            var png = Path.Combine(Path.GetDirectoryName(Info.Location), "assets", "skin.png");
            renderer.material.mainTexture = LoadTexture(png);
            """),

        new("bx-audio", "bepinex", "Звук из .ogg / .wav", "Sound from .ogg / .wav",
            "Загружает звук с диска через UnityWebRequest и проигрывает. Для .wav поменяйте AudioType.",
            "Loads audio from disk with UnityWebRequest and plays it. Use AudioType.WAV for .wav.",
            """
            IEnumerator LoadClip(string path, System.Action<AudioClip> done)
            {
                using var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), AudioType.OGGVORBIS);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Log.LogError(req.error); yield break; }
                done(DownloadHandlerAudioClip.GetContent(req));
            }

            // Использование (в Awake)
            StartCoroutine(LoadClip(Path.Combine(Path.GetDirectoryName(Info.Location), "assets", "boom.ogg"),
                clip => AudioSource.PlayClipAtPoint(clip, Camera.main.transform.position)));
            """),

        new("bx-embedded", "bepinex", "Файл внутри dll", "Embedded resource",
            "Положить файл прямо в сборку мода и прочитать без отдельных файлов рядом.",
            "Bundle a file into the mod dll and read it without loose files.",
            """
            // В csproj:  <ItemGroup><EmbeddedResource Include="assets\data.json" /></ItemGroup>
            static byte[] ReadEmbedded(string name)
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var full = asm.GetManifestResourceNames().First(n => n.EndsWith(name));
                using var stream = asm.GetManifestResourceStream(full);
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            """),

        new("bx-gui", "bepinex", "Простое окно (IMGUI)", "Simple window (IMGUI)",
            "Окно с кнопками и ползунком — быстро сделать меню читов или настроек в игре.",
            "A window with buttons and a slider — a quick in-game settings or debug menu.",
            """
            private bool _show;
            private Rect _rect = new Rect(40, 40, 260, 160);

            private void Update() { if (Input.GetKeyDown(KeyCode.F7)) _show = !_show; }

            private void OnGUI()
            {
                if (!_show) return;
                _rect = GUILayout.Window(7331, _rect, id =>
                {
                    GUILayout.Label("Скорость: " + _speed.ToString("0.0"));
                    _speed = GUILayout.HorizontalSlider(_speed, 0.5f, 3f);
                    if (GUILayout.Button("Сбросить")) _speed = 1f;
                    GUI.DragWindow();
                }, "Мой мод");
            }
            """),

        new("bx-manager", "bepinex", "Постоянный объект мода", "Persistent mod object",
            "Создаёт объект, который живёт между сценами, и вешает на него свой компонент (Update, корутины).",
            "Creates an object that survives scene changes and attaches your own component (Update, coroutines).",
            """
            var go = new GameObject("MyModManager");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<MyBehaviour>();

            public class MyBehaviour : MonoBehaviour
            {
                private void Update() { /* каждый кадр */ }
            }
            """),

        new("bx-scene", "bepinex", "Реакция на смену сцены", "Reacting to scene changes",
            "Выполнить код, когда загрузился уровень или меню.",
            "Run code when a level or the menu has loaded.",
            """
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                Log.LogInfo($"Сцена: {scene.name}");
                if (scene.name == "MainMenu") { /* ... */ }
            };
            """),

        new("bx-save", "bepinex", "Свои сохранения в JSON", "Your own save data in JSON",
            "Хранить данные мода рядом с конфигами BepInEx.",
            "Keep mod data next to the BepInEx configs.",
            """
            [System.Serializable] class SaveData { public int Kills; public string Name = ""; }

            static string SavePath => Path.Combine(BepInEx.Paths.ConfigPath, "MyMod.save.json");

            static void Save(SaveData data) => File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
            static SaveData Load() => File.Exists(SavePath) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)) : new SaveData();
            """),

        // ---------------------------------------------------------------- SMAPI
        new("sm-events", "smapi", "События игры", "Game events",
            "Подписки на самые нужные события: день, секунда, нажатие кнопки, загрузка сохранения.",
            "Subscriptions to the most useful events: day, second, button press, save loaded.",
            """
            helper.Events.GameLoop.SaveLoaded += (_, _) => Monitor.Log("Сохранение загружено", LogLevel.Info);
            helper.Events.GameLoop.DayStarted += (_, _) => Monitor.Log($"День {Game1.dayOfMonth}, {Game1.currentSeason}", LogLevel.Debug);
            helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => { if (!Context.IsWorldReady) return; /* раз в секунду */ };
            helper.Events.Input.ButtonPressed += (_, e) => { if (e.Button == SButton.F6) Game1.showGlobalMessage("Привет!"); };
            """),

        new("sm-command", "smapi", "Консольная команда", "Console command",
            "Команда, которую можно ввести в консоли SMAPI: say_hello Имя.",
            "A command you can type in the SMAPI console: say_hello Name.",
            """
            helper.ConsoleCommands.Add("say_hello", "Здоровается. Использование: say_hello <имя>", (command, args) =>
            {
                Monitor.Log($"Привет, {(args.Length > 0 ? args[0] : "мир")}!", LogLevel.Info);
            });
            """),

        new("sm-edit", "smapi", "Изменить данные игры", "Edit game data",
            "Правка таблиц игры на лету, без замены файлов. Здесь — цена предмета 472 (семена пастернака).",
            "Edit game tables on the fly without replacing files. Here: the price of item 472 (parsnip seeds).",
            """
            helper.Events.Content.AssetRequested += (_, e) =>
            {
                if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
                {
                    e.Edit(asset =>
                    {
                        var data = asset.AsDictionary<string, StardewValley.GameData.Objects.ObjectData>().Data;
                        data["472"].Price = 10;
                    });
                }
            };
            """),

        new("sm-dialogue", "smapi", "Изменить реплику персонажа", "Edit a character's dialogue",
            "Подменяет строку диалога (здесь — Эбигейль в понедельник).",
            "Overrides a dialogue line (here: Abigail on Monday).",
            """
            helper.Events.Content.AssetRequested += (_, e) =>
            {
                if (e.NameWithoutLocale.IsEquivalentTo("Characters/Dialogue/Abigail"))
                    e.Edit(asset => asset.AsDictionary<string, string>().Data["Mon"] = "Привет! Как фермерство?");
            };
            """),

        new("sm-load", "smapi", "Своя картинка из папки мода", "Your own image from the mod folder",
            "Отдаёт игре PNG из assets/ под своим именем и читает его обратно.",
            "Serves a PNG from assets/ to the game under your own name and loads it back.",
            """
            helper.Events.Content.AssetRequested += (_, e) =>
            {
                if (e.NameWithoutLocale.IsEquivalentTo("Mods/YourName.YourMod/Portrait"))
                    e.LoadFromModFile<Microsoft.Xna.Framework.Graphics.Texture2D>("assets/portrait.png", AssetLoadPriority.Medium);
            };

            // Использование
            var tex = Game1.content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>("Mods/YourName.YourMod/Portrait");
            """),

        new("sm-item", "smapi", "Выдать предмет игроку", "Give an item to the player",
            "Создаёт предмет по ID и кладёт в инвентарь (74 — призматический осколок).",
            "Creates an item by ID and puts it into the inventory (74 is a prismatic shard).",
            """
            var item = ItemRegistry.Create("(O)74", 1);
            if (!Game1.player.addItemToInventoryBool(item))
                Game1.createItemDebris(item, Game1.player.getStandingPosition(), 0);
            Game1.showGlobalMessage("Получен подарок!");
            """),

        new("sm-harmony", "smapi", "Патч Harmony", "Harmony patch",
            "Изменить метод самой игры. В csproj уже включено EnableHarmony.",
            "Change a method of the game itself. EnableHarmony is already on in the csproj.",
            """
            // В Entry()
            var harmony = new HarmonyLib.Harmony(ModManifest.UniqueID);
            harmony.Patch(
                original: HarmonyLib.AccessTools.Method(typeof(StardewValley.Farmer), nameof(StardewValley.Farmer.doneEating)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(ModEntry), nameof(DoneEating_Postfix)));

            // В классе
            private static void DoneEating_Postfix(StardewValley.Farmer __instance)
            {
                __instance.health = __instance.maxHealth;
            }
            """),

        new("sm-data", "smapi", "Сохранить данные в сохранение игры", "Store data in the save file",
            "Данные мода лежат внутри сохранения и не теряются при смене фермы.",
            "Mod data lives inside the save and follows the farm.",
            """
            class SaveModel { public int Visits { get; set; } }

            helper.Events.GameLoop.SaveLoaded += (_, _) => _data = helper.Data.ReadSaveData<SaveModel>("my-data") ?? new SaveModel();
            helper.Events.GameLoop.Saving += (_, _) => helper.Data.WriteSaveData("my-data", _data);
            """),

        new("sm-i18n", "smapi", "Переводы (i18n)", "Translations (i18n)",
            "Тексты мода в файлах i18n/default.json и i18n/ru.json — SMAPI сам берёт язык игры.",
            "Mod texts in i18n/default.json and i18n/ru.json — SMAPI picks the game language itself.",
            """
            // i18n/default.json:  { "hello": "Hello, {{name}}!" }
            // i18n/ru.json:       { "hello": "Привет, {{name}}!" }
            Game1.showGlobalMessage(helper.Translation.Get("hello", new { name = Game1.player.Name }));
            """),

        // ---------------------------------------------------------------- Hollow Knight
        new("hk-hooks", "hk", "Хуки Modding API", "Modding API hooks",
            "События игры: душа, урон, обновление героя. Возвращаемое значение подменяет игровое.",
            "Game events: soul, damage, hero update. The return value replaces the game's.",
            """
            ModHooks.SoulGainHook += amount => amount * 2;                       // вдвое больше души
            ModHooks.AfterTakeDamageHook += (hazard, damage) => damage > 1 ? 1 : damage; // не больше 1 маски урона
            ModHooks.HeroUpdateHook += () => { /* каждый кадр, пока герой есть */ };
            """),

        new("hk-settings", "hk", "Настройки мода", "Mod settings",
            "Глобальные настройки: игра сама читает и пишет файл в папке Mods.",
            "Global settings: the game reads and writes the file in the Mods folder itself.",
            """
            public class Settings { public bool Enabled = true; public int Bonus = 1; }

            public class MyMod : Mod, IGlobalSettings<Settings>
            {
                private Settings _s = new();
                public void OnLoadGlobal(Settings s) => _s = s;
                public Settings OnSaveGlobal() => _s;
                // ...
            }
            """),

        new("hk-pd", "hk", "Данные игрока (PlayerData)", "Player data",
            "Читать и менять состояние героя: деньги, способности, здоровье.",
            "Read and change the hero's state: geo, abilities, health.",
            """
            var pd = PlayerData.instance;
            bool hasDash = pd.GetBool(nameof(PlayerData.hasDash));
            pd.SetBool(nameof(PlayerData.hasDoubleJump), true);       // выдать способность
            HeroController.instance.AddGeo(100);                      // деньги
            """),

        new("hk-on", "hk", "Перехват метода игры (On.)", "Hooking a game method (On.)",
            "MMHOOK даёт хуки на любой метод: вызвать оригинал можно до, после или вообще не вызывать.",
            "MMHOOK exposes a hook for any method: call the original before, after or not at all.",
            """
            On.HeroController.TakeDamage += (orig, self, go, side, amount, hazard) =>
            {
                Log($"Урон: {amount}");
                orig(self, go, side, amount, hazard);   // не вызвать = герой неуязвим
            };
            """),

        new("hk-preload", "hk", "Предзагрузка объектов игры", "Preloading game objects",
            "Взять префаб из сцены игры (врага, эффект) и использовать в своём коде.",
            "Grab a prefab from a game scene (an enemy, an effect) and reuse it.",
            """
            public override List<(string, string)> GetPreloadNames() => new() { ("Tutorial_01", "_Enemies/Crawler 1") };

            public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloads)
            {
                var crawler = preloads["Tutorial_01"]["_Enemies/Crawler 1"];
                // Object.Instantiate(crawler, position, Quaternion.identity);
            }
            """),

        // ---------------------------------------------------------------- Unity (общее)
        new("un-raycast", "unity", "Луч из камеры (на что смотрю)", "Raycast from the camera",
            "Определить объект под прицелом или курсором.",
            "Find the object under the crosshair or cursor.",
            """
            var cam = Camera.main;
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f));
            if (Physics.Raycast(ray, out var hit, 50f))
                Debug.Log($"Смотрю на {hit.collider.name} в {hit.distance:0.0} м");
            """),

        new("un-coroutine", "unity", "Задержка и таймер", "Delays and timers",
            "Выполнить действие через время или повторять — через корутину.",
            "Run an action later or repeatedly with a coroutine.",
            """
            IEnumerator DoLater()
            {
                yield return new WaitForSeconds(2f);        // через 2 секунды
                Debug.Log("Прошло две секунды");
                while (true) { yield return new WaitForSeconds(1f); /* каждую секунду */ }
            }

            StartCoroutine(DoLater());
            """),

        new("un-singleton", "unity", "Одиночка (Singleton)", "Singleton",
            "Один объект на всю игру с доступом отовсюду.",
            "One object for the whole game, reachable from anywhere.",
            """
            public class ModManager : MonoBehaviour
            {
                public static ModManager Instance { get; private set; }

                private void Awake()
                {
                    if (Instance != null) { Destroy(gameObject); return; }
                    Instance = this;
                    DontDestroyOnLoad(gameObject);
                }
            }
            """),

        new("un-material", "unity", "Цвет и свойства материала", "Material colour and properties",
            "Перекрасить модель или поменять параметр шейдера.",
            "Recolour a model or change a shader parameter.",
            """
            foreach (var r in obj.GetComponentsInChildren<Renderer>())
            {
                r.material.color = new Color(1f, 0.4f, 0.2f);          // основной цвет
                r.material.SetFloat("_Metallic", 0.8f);                 // свойство шейдера
                r.material.EnableKeyword("_EMISSION");
                r.material.SetColor("_EmissionColor", Color.cyan * 2f);  // свечение
            }
            """),

        new("un-prefs", "unity", "Простое хранилище (PlayerPrefs)", "Simple storage (PlayerPrefs)",
            "Запомнить число или строку между запусками без своих файлов.",
            "Remember a number or string between runs without your own files.",
            """
            PlayerPrefs.SetInt("MyMod.Kills", PlayerPrefs.GetInt("MyMod.Kills", 0) + 1);
            PlayerPrefs.Save();
            """),
    ];
}
