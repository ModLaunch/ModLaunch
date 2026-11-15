// Сквозная проверка функций на эмуляторе (auth + firestore + functions), проект demo-modlaunch:
//   firebase emulators:exec --only auth,firestore,functions "node ../server/functions/test/e2e.mjs"
// Человек входит → createPayment (WEBPAY, тест) → страница оплаты с подписанной формой → уведомление WEBPAY с
// верной подписью → игра выдана; поддельное уведомление отклонено; чужая ссылка на оплату — 403.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';

const P = 'demo-modlaunch';
const AUTH = 'http://127.0.0.1:9099/identitytoolkit.googleapis.com/v1';
const FS = `http://127.0.0.1:8080/v1/projects/${P}/databases/(default)/documents`;
const FN = `http://127.0.0.1:5001/${P}/europe-west1`;
const owner = { authorization: 'Bearer owner', 'content-type': 'application/json' };

async function json(url, init) { const r = await fetch(url, init); const t = await r.text(); return { status: r.status, body: t ? (() => { try { return JSON.parse(t); } catch { return t; } })() : null }; }
const put = (path, fields) => json(`${FS}/${path}`, { method: 'PATCH', headers: owner, body: JSON.stringify({ fields }) });
const get = async (path) => (await json(`${FS}/${path}`, { headers: owner })).body;

// Данные: игра с ценами, настройки магазина.
await put('config/store', { fee: { integerValue: '10' } });
await put('market/dev1_game', {
  uid: { stringValue: 'dev1' }, author: { stringValue: 'Indie Dev' }, title: { stringValue: 'Космос' }, kind: { stringValue: 'game' },
  status: { stringValue: 'active' }, price: { integerValue: '0' }, sales: { integerValue: '0' },
  prices: { mapValue: { fields: { BYN: { integerValue: '2990' } } } },
});

// Покупатель с подтверждённой почтой.
const email = `e2e${Date.now()}@example.test`;
const su = await json(`${AUTH}/accounts:signUp?key=x`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password: 'correct-horse-1', returnSecureToken: true }) });
await json(`${AUTH}/accounts:sendOobCode?key=x`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ requestType: 'VERIFY_EMAIL', idToken: su.body.idToken }) });
const codes = (await json(`http://127.0.0.1:9099/emulator/v1/projects/${P}/oobCodes`)).body.oobCodes;
await json(`${AUTH}/accounts:update?key=x`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ oobCode: codes.filter((c) => c.email === email).pop().oobCode }) });
const signin = await json(`${AUTH}/accounts:signInWithPassword?key=x`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password: 'correct-horse-1', returnSecureToken: true }) });
const token = signin.body.idToken;
const uid = signin.body.localId;

// Без входа — отказ.
const anon = await json(`${FN}/createPayment`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ data: { type: 'game', target: 'dev1_game', currency: 'BYN' } }) });
assert.equal(anon.status, 401, JSON.stringify(anon.body));

// Заказ.
const order = await json(`${FN}/createPayment`, { method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` }, body: JSON.stringify({ data: { type: 'game', target: 'dev1_game', currency: 'BYN', lang: 'ru' } }) });
assert.equal(order.status, 200, JSON.stringify(order.body));
const { pid, amount, redirectUrl } = order.body.result;
assert.equal(amount, 2990);
console.log('order', pid, redirectUrl);

// Страница оплаты: подписанная форма; с чужой подписью — 403.
const page = await fetch(redirectUrl);
const html = await page.text();
assert.equal(page.status, 200);
assert.ok(html.includes('securesandbox.webpay.by') && html.includes(`value="${pid}"`) && html.includes('name="wsb_signature"'));
assert.equal((await fetch(redirectUrl.replace(/t=[0-9a-f]+/, 't=' + '0'.repeat(32)))).status, 403);

// Уведомление WEBPAY: поддельное — 403 и ничего не выдано; настоящее — 200 и игра выдана.
const f = { batch_timestamp: String(Math.floor(Date.now() / 1000)), currency_id: 'BYN', amount: '29.90', payment_method: 'cc', order_id: '127386', site_order_id: pid, transaction_id: '858578101', payment_type: '4', rrn: '786755995452' };
const body = (sig) => new URLSearchParams({ ...f, wsb_signature: sig }).toString();
const forged = await fetch(`${FN}/webpayNotify`, { method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: body('0'.repeat(32)) });
assert.equal(forged.status, 403);
assert.equal((await get(`payments/${pid}`)).fields.status.stringValue, 'created');
const sig = createHash('md5').update(Object.values(f).join('') + 'e2e-webpay-secret').digest('hex');
const real = await fetch(`${FN}/webpayNotify`, { method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: body(sig) });
assert.equal(real.status, 200);
assert.equal((await get(`payments/${pid}`)).fields.status.stringValue, 'paid');
const sale = await get(`market/dev1_game/sales/${uid}`);
assert.equal(sale.fields.price.integerValue, '2990');
assert.equal((await get(`receipts/${pid}`)).fields.amount.integerValue, '2990');

// Повтор того же уведомления — 200, без второй выдачи.
assert.equal((await fetch(`${FN}/webpayNotify`, { method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: body(sig) })).status, 200);
assert.equal((await get('market/dev1_game')).fields.sales.integerValue, '1');

// Ключи: чужую игру — отказ.
const keys = await json(`${FN}/generateKeys`, { method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` }, body: JSON.stringify({ data: { listing: 'dev1_game', count: 2 } }) });
assert.equal(keys.status, 400);
assert.equal(keys.body.error.message, 'NOT_YOURS');

console.log('E2E OK: auth → createPayment → signed WEBPAY form → forged notify 403 → real notify → game, receipt; duplicate ignored; keys guarded');
