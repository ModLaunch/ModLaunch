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

Для снимков есть две переменные: `MODLAUNCH_SHOT_ONLY=1-home,4-catalog` — снять только
эти экраны (по началу имени), `MODLAUNCH_SHOT_HEIGHT=1500` — окно выше, чтобы страница
поместилась целиком.

## Сервер Creator Hub (Firestore)

Своего сервера нет: хаб, рынок и заказы живут в Firestore, а защищают их правила.
Правила лежат в `firebase/` — их вставляют в консоли Firebase (Firestore Database →
Правила) внутрь блока `match /databases/{database}/documents { … }`:

- `firebase/creations.rules` — моды хаба, версии, лайки, комментарии, жалобы;
- `firebase/market.rules` — кошельки, журнал операций, ассеты, копии, перепродажа,
  обмены и живые заказы. Пока их нет, вкладки «Ассеты» и «Заказы» показывают, что рынок
  отклонил запрос, а остальная программа работает как раньше.

Проверить правила на эмуляторе (нужны Node.js и Java):

```
cd firebase/tests
npm install
npm test        # 75 сценариев: честные операции проходят, попытки обмана — нет
```

Код программы против эмулятора: `MODLAUNCH_FIRESTORE_EMULATOR=127.0.0.1:8181`
(и `MODLAUNCH_FIRESTORE_PROJECT=demo-mk`) направляет запросы Firestore на эмулятор.
