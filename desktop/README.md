# ModLaunch 4 (Avalonia)

Новая версия ModLaunch без Electron: C# и [Avalonia UI](https://avaloniaui.net/), .NET 8.
Один exe около 24 МБ вместо 82 МБ, запускается быстрее и занимает меньше памяти.

Данные общие с версией 3.x (`%APPDATA%\ModHub`): пути к играм, язык, ключ Nexus
и списки установленных модов подхватываются сами.

## Что уже есть (этап 1)

- своя рамка окна, тёмная тема, русский и английский;
- главная с играми, поиск игр (Steam, Epic, GOG, обход дисков);
- все 8 игр: загрузчики BepInEx, SMAPI, Hollow Knight Modding API;
- каталоги Thunderstore, Nexus Mods и ModLinks: разделы, поиск, сортировка, «Нужные», наборы;
- установка с зависимостями, из файла (zip, 7z, rar), включение, выключение, удаление;
- Nexus без Premium: страница файла в браузере и автоустановка из «Загрузок».

Дальше: коллекции, DXVK, ReShade, профили, сохранения, логи, время в игре,
затем друзья, оверлей, установщик и автообновление.

## Сборка

```
cd desktop/ModLaunch
dotnet run                                     # запустить
dotnet publish -c Release -r win-x64 -o out    # один exe
out/ModLaunch.exe --selfcheck                  # живые каталоги и пробная установка
out/ModLaunch.exe --screenshot shots           # снимки экранов на поддельных данных
```

## Установщик

`ModLaunch-Setup-<версия>.exe` — та же программа: по слову «Setup» в имени файла она открывает окно установки.

- Ставит всегда в `%LOCALAPPDATA%\Programs\ModLaunch` (или `<выбранная папка>\ModLaunch`), создаёт ярлыки и привязывает `nxm://`.
- Старую установку (`Programs\modhub` с 2.x/3.x, прежняя папка из реестра) находит сама: переносит программу в `ModLaunch`, убирает старую папку, ярлыки «ModHub» и запись в «Приложениях Windows», переводит автозапуск на новый exe. Настройки и моды (`%APPDATA%\ModHub`) не трогает.
- Новый exe сначала пишется как `ModLaunch.exe.part` и только потом заменяет старые файлы, поэтому сбой не оставляет сломанной копии. Чужие папки установщик не трогает.
- `--update` — тихое обновление (его запускает автообновление), `--uninstall` — удаление.

```
out/ModLaunch.exe --installer-check      # установка в пробную папку, перенос из modhub, без сети
out/ModLaunch.exe --setup-shots shots    # снимки всех экранов установщика
```

## Creator Hub и Big Picture

- **Creator Hub** (`Views/CreatorPage*.cs`, `Creator/`): студия — моды без кода (ModScript), проекты на C# (`CodeProjects`: BepInEx 5/6, SMAPI, HK Modding API, сборка `dotnet build` с копированием в игру), библиотека кода (`Snippets`), ассеты (`AssetLibrary`), упаковщик (`Packager`), гайды и инструменты (`Guides`), галерея ModLaunch Hub.
- **Big Picture** (`Views/BigPictureWindow*.cs`): обложки, каталог модов, профили, обновления, друзья и загрузки — геймпадом или клавиатурой.

```
out/ModLaunch.exe --creator-check        # Creator Hub без сети: проекты под все загрузчики, ассеты, упаковщик
out/ModLaunch.exe --creator-shots shots  # снимки всех разделов Creator Hub
out/ModLaunch.exe --bp-shots shots       # снимки Big Picture и его меню
```
