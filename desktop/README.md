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
