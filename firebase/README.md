# Правила Firestore для ModLaunch

`firestore.rules` — полный файл правил базы: друзья, ModLaunch Hub, отзывы о модах,
рынок Creator Hub и кошельки. Серверных функций не нужно — бесплатного тарифа Spark хватает.

## Как поставить

* Firebase Console → Firestore Database → **Правила** → выделить всё, вставить `firestore.rules`
  целиком → **Опубликовать**. (В программе: «Настройки → Сервисы → Подключить» копирует этот же файл.)
* или из этой папки: `firebase deploy --only firestore:rules`

Админ — документ `admins/<почта>` (создаётся в консоли), почта админа должна быть подтверждена.

## Кошельки

Баланс (`wallets/{uid}`) меняется только вместе с новой записью журнала
`wallets/{uid}/ledger/{seq}` (seq + 1, сумма = разница балансов). Правила проверяют основание:

| запись | кто | основание |
|---|---|---|
| `purchase` | покупатель | новая продажа `market/{id}/sales/{uid}` на ту же цену |
| `sale` | продавец | своя продажа с `claimed: false` → `true`, сумма = цена − комиссия |
| `payout` | владелец | новая заявка `payouts/{id}` на ту же сумму |
| `payout-return` | админ | заявка на вывод `pending → rejected` |
| `topup` | админ | заявка на пополнение `pending → done` |
| `grant` | админ | с пояснением в `note` |

## Тесты

`test/rules.test.mjs` — 25 сценариев на эмуляторе Firestore (CI: `.github/workflows/rules.yml`).
Локально: `cd test && npm ci && cd .. && npx firebase-tools emulators:exec --only firestore "cd test && npm test"`.
