namespace ModLaunch.Creator;

public sealed record Template(string Id, string Game, string Code);

/// <summary>«Примочки» — готовые примеры модов на ModScript: открыть, поменять, поставить.</summary>
public static class Templates
{
    public static readonly Template[] All =
    [
        new("blank", "valheim", """
            # Мой мод на ModScript. Справка по командам — вкладка «Справка».
            mod "Мой мод"
            version 1.0.0
            author "Я"
            about "Что делает мод — одной строкой"
            game valheim

            # Уберите # в начале строки, чтобы включить команду.
            # needs RandyKnapp-EquipmentAndQuickSlots
            # config "BepInEx.cfg" [Logging.Console] Enabled = true
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
    ];

    public static Template? ById(string id) => All.FirstOrDefault(t => t.Id == id);
}
