# Сервер ModLaunch (Firebase Cloud Functions)

Что делает: принимает оплату картой (bePaid или WEBPAY), выдаёт покупку, пишет чек, делает возвраты, считает
заработок разработчиков и выплаты, выпускает ключи, проверяет выложенные файлы через VirusTotal, пишет письма
(вход с нового устройства, лист ожидания) и рассылает приглашения волнами.

Все ключи — только здесь (Google Secret Manager). В программу и на сайт они не попадают.

## 1. Один раз: подготовить проект

1. Firebase Console → проект **modhub-reviews** → тариф **Blaze** (оплата по факту). Без него функции не выложить,
   а на бесплатном тарифе база отказывает после 50 тыс. чтений в день.
2. Google Cloud Console → Billing → Budgets → бюджет с оповещениями (например 100 / 500 / 2 000 $ в месяц).
3. Установить Node.js 22 и войти: `npx firebase-tools@15 login`.

## 2. Секреты (вводятся один раз, хранятся у Google)

```bash
cd firebase
npx firebase-tools@15 functions:secrets:set LINK_SECRET          # любая длинная случайная строка
npx firebase-tools@15 functions:secrets:set BEPAID_SHOP_ID       # из кабинета bePaid
npx firebase-tools@15 functions:secrets:set BEPAID_SECRET_KEY    # из кабинета bePaid
npx firebase-tools@15 functions:secrets:set BEPAID_PUBLIC_KEY    # публичный ключ магазина (для подписи уведомлений)
npx firebase-tools@15 functions:secrets:set WEBPAY_STORE_ID      # если используете WEBPAY
npx firebase-tools@15 functions:secrets:set WEBPAY_SECRET
npx firebase-tools@15 functions:secrets:set VT_API_KEY           # ключ VirusTotal (можно пустым — проверка выключится)
npx firebase-tools@15 functions:secrets:set SMTP_URL             # smtps://логин:пароль@smtp.почта.com:465
```

Секрет, который вам не нужен (например, WEBPAY при работе через bePaid), всё равно задайте любым значением —
функции ждут все секреты.

## 3. Настройки (файл `server/functions/.env.modhub-reviews`, без секретов)

```
PAYMENT_PROVIDER=bepaid          # bepaid | webpay | none (none — «Оплата картой временно недоступна»)
PAYMENT_TEST=true                # сначала тестовый режим, после проверки — false
PUBLIC_SITE=https://modlaunchapp.com
FUNCTIONS_URL=https://europe-west1-modhub-reviews.cloudfunctions.net
WEBPAY_FORM_URL=https://securesandbox.webpay.by/   # боевой адрес формы — из договора с WEBPAY
MAIL_FROM=ModLaunch <no-reply@modlaunchapp.com>
```

## 4. Выложить

```bash
cd firebase
npx firebase-tools@15 deploy --only functions,firestore:rules,firestore:indexes --project modhub-reviews
```

## 5. Подключить платёжную систему

**bePaid:** в кабинете магазина адрес уведомлений можно не задавать — сервер передаёт его в каждом платеже
(`…/bepaidWebhook`). Успешная и неудачная страницы — `https://modlaunchapp.com/pay/success.html` и `/pay/fail.html`
(есть в папке `site/pay`).

**WEBPAY:** адрес уведомлений в настройках магазина — `https://europe-west1-modhub-reviews.cloudfunctions.net/webpayNotify`
(принимаются только порты 80/443/8080/8443 — у Cloud Functions 443). Возвраты WEBPAY делаются в их кабинете, затем в
программе: Аккаунт → Магазин → «Возврат денег по оплате картой» с номером возврата.

## 6. Проверить в тестовом режиме (до приёма денег)

1. `PAYMENT_TEST=true`, выложить.
2. В программе: игра с ценой → «Купить» → на странице bePaid тестовая карта **4200 0000 0000 0000** (успех) или
   **4005 5500 0000 0019** (отказ), срок — любой будущий, CVC — любой.
3. Игра появилась в библиотеке, в Аккаунт → Покупки есть чек с пометкой «ТЕСТОВЫЙ ПЛАТЁЖ».
4. Возврат: Аккаунт → Покупки → «Вернуть деньги» → в Аккаунт → Магазин одобрить.
5. Только после этого `PAYMENT_TEST=false` и выложить ещё раз.

## 7. Магазин в программе

Аккаунт → **Магазин** (только админ): включить магазин, комиссия (0–29 %), цены премиума по валютам, реквизиты ООО
для чеков (название, УНП, адрес, почта), минимальная выплата. Там же — выплаты разработчикам, заявки на возврат.

## 8. Тесты (без настоящих денег и без вашего проекта)

```bash
tools/integration.sh server   # логика магазина на эмуляторе базы: 19 сценариев
tools/integration.sh e2e      # функции целиком на эмуляторе: заказ → форма WEBPAY → подписанное уведомление
tools/integration.sh          # программа ↔ сервер: покупка и подарок картой (+ все прочие сценарии программы)
```

## Функции

| Функция | Что делает |
|---|---|
| `createPayment` | заказ: цену считает сервер (регион, скидка, комиссия), ссылка на оплату |
| `payForm` | страница с подписанной формой WEBPAY (ссылка подписана, чужой заказ не открыть) |
| `bepaidWebhook` / `webpayNotify` | уведомление об оплате: проверка подписи → выдача покупки ровно один раз |
| `refundPayment` / `requestRefund` / `rejectRefund` | возвраты: заявка покупателя (14 дней), решение админа |
| `generateKeys` / `redeemKey` | ключи для раздачи |
| `requestPayout` / `resolvePayout` | выплаты разработчикам по валютам |
| `scanListing` / `scanCreation` / `rescanQueued` | VirusTotal: опасное снимается автоматически |
| `newDeviceEmail` | письмо о входе с нового устройства |
| `waitlistSignup` / `waitlistConfirm` / `inviteWave` | лист ожидания с подтверждением почты, приглашения волнами |
