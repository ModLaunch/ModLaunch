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
