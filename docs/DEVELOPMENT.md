# ModLaunch: заметки для разработки

Для пользователей всё описано в [README](../README.ru.md). Здесь то, что нужно владельцу проекта.

## Запуск и сборка

```
npm install
npm start          # запустить из исходников
npm run dist       # собрать установщик и zip в dist/
node tools/probe.js  # проверить каталоги и установку на живых сайтах
node tools/friends-check.js  # друзья на правилах базы в эмуляторе Firebase
                             # (нужны firebase-tools и Java 21)
```

## Выпуск новой версии

1. Поднять `version` в `package.json`.
2. Обновить текст релиза в [`build/release-notes.md`](../build/release-notes.md).
3. Вкладка **Actions → release → Run workflow**. GitHub соберёт установщик и портативную версию и выложит их в «Релизы».

Установленные копии увидят новую версию в течение нескольких часов (или сразу: «Настройки → Обновления → Проверить»).

## Автообновление

Новые версии ModLaunch берёт из «Релизов» репозитория, указанного в
[`src/main/update.config.json`](../src/main/update.config.json). Переедет
репозиторий, поменяйте там `owner` и `repo`.

## Статистика

Скачивания и оценки: пять щелчков по номеру версии в «Настройки → О программе»,
и в левой панели появится кнопка «Статистика».

## Самопроверка на CI

При каждом изменении в `src/` GitHub Actions запускает программу на настоящем Windows
(`selftest`), проходит по экранам и кладёт снимки в ветку `ci-screens`.
Скриншоты для README лежат отдельно, в [`docs/screenshots`](screenshots): их можно
обновлять снимками из `ci-screens`.

## Друзья: один раз включить на сервере

Друзья живут в той же базе Firebase, что отзывы и аккаунты
(`src/main/reviews.config.json`). Чтобы база пускала их запросы, правила
из [`firebase/friends.rules`](../firebase/friends.rules) нужно один раз
вставить в консоли Firebase:

1. [console.firebase.google.com](https://console.firebase.google.com) →
   проект → **Firestore Database → Правила**;
2. вставить содержимое `firebase/friends.rules` внутрь блока
   `match /databases/{database}/documents { … }`, рядом с правилами отзывов;
3. **Опубликовать**.

Правила отзывов эти строки не трогают. Пока правила не вставлены, ModLaunch
на экране «Друзья» так и пишет: сервер отклонил запрос.

Ключ `apiKey` в `reviews.config.json` публичный по задумке Firebase: доступ
к данным ограничивают правила базы, а не ключ.

## Маркет Creator Hub: один раз включить на сервере

Маркет (продажа моделей, частей кода, скриптов и ассетов), кошельки и заявки
на пополнение/вывод живут в той же базе Firebase. Правила из
[`firebase/market.rules`](../firebase/market.rules) нужно вставить в консоли
Firebase так же, как правила друзей и Hub, и опубликовать. Именно правила
сверяют суммы при покупке: цену, комиссию площадки и зачисление продавцу.

- Комиссия площадки: `feePercent` в `desktop/ModLaunch/Assets/market.config.json`
  и `mFee()` в `firebase/market.rules` — менять обе сразу.
- `topUpUrl` в том же файле — ссылка на оплату для пополнения (можно с `{uid}`).
- Деньги площадки копятся в `wallets/_platform`.
- Пополнения и выплаты подтверждает админ (почта в коллекции `admins`) на
  странице «Аккаунт → Админ-панель».

## ModLaunch 8.4: сборка, сайт и службы

- **Сборка и установка у себя**: двойной щелчок по `BUILD-8.4.bat` в корне. Он собирает
  `out/ModLaunch.exe`, кладёт установщик и `update.json` (версия, заметки, sha256) в `site/`
  и ставит программу в `%LOCALAPPDATA%\Programs\ModLaunch`.
- **Сайт** (`site/`) загружается на хостинг modlaunchapp.com целиком: там `update.json`
  (обновления), `ads.json` (реклама), `download/` (установщик) и `advertise.html`.
- **Обновления**: программа сначала читает `https://modlaunchapp.com/update.json`
  (адрес — `manifest` в `desktop/ModLaunch/Assets/update.config.json`), при ошибке — «Релизы»
  GitHub. Принимаются только https и файл `ModLaunch-Setup-x.y.z.exe`, сумма sha256 сверяется.
- **Реклама**: лента `ads.json` (адрес — `desktop/ModLaunch/Assets/ads.config.json`). Пустая
  лента — крутятся объявления самого ModLaunch (Hub, Creator Hub, друзья, «разместить рекламу»).
- **Состояние сервисов**: «Настройки → Система → Состояние сервисов» проверяет отзывы,
  аккаунты, друзей, Hub, маркет, рекламу и обновления настоящими запросами. Кнопка
  «Подключить» копирует правила (`Assets/server-rules.txt` = `firebase/friends.rules` +
  `creations.rules` + `market.rules`) и открывает консоль Firebase. Поменяли правила в
  `firebase/` — обновите и `server-rules.txt`.
- **Готовые настройки модов**: `Features/CfgPresets.cs` (встроенные наборы и свои наборы в
  `%APPDATA%\ModHub\cfg-presets\<игра>`), редактор — `Views/GamePage.Config.cs`.
