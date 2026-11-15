// Логика магазина на эмуляторе Firestore: firebase emulators:exec --only firestore "cd ../server/functions && npm test"
import { test, before, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { initializeApp } from 'firebase-admin/app';
import { getFirestore, Timestamp } from 'firebase-admin/firestore';
import * as store from '../lib/store.js';
import { scanItem } from '../lib/scan.js';

const emulated = !!process.env.FIRESTORE_EMULATOR_HOST;
let db;
const BUYER = { uid: 'buyer1', email: 'buyer@ml.test', name: 'Buyer' };
const FRIEND = { uid: 'friend1', email: 'friend@ml.test', name: 'Friend' };
const DEV = 'dev1';

before(() => {
  if (!emulated) return;
  initializeApp({ projectId: 'demo-modlaunch' });
  db = getFirestore();
});

async function clear() {
  await fetch(`http://${process.env.FIRESTORE_EMULATOR_HOST}/emulator/v1/projects/demo-modlaunch/databases/(default)/documents`, { method: 'DELETE' });
}

beforeEach(async () => {
  if (!emulated) return;
  await clear();
  await db.doc('config/store').set({ fee: 10, premium: { BYN: { month: 990, year: 9900 } }, minPayout: { BYN: 1000 }, company: { name: 'ООО «МодЛаунч»', unp: '000000000', address: 'Минск', email: 'support@modlaunchapp.com' } });
  await db.doc('market/dev1_game').set({ uid: DEV, author: 'Indie Dev', title: 'Космос', kind: 'game', status: 'active', price: 0, sales: 0, prices: { BYN: 2990, USD: 999 } });
});

const paid = (pid, amountOk = true, currency = 'BYN') => store.markPaid(db, pid, { provider: 'bepaid', providerId: 'bp-' + pid, amountOk, currency, test: false });

test('покупка игры: заказ по серверной цене → оплата → продажа, библиотека, чек, заработок; повтор уведомления не дублирует', { skip: !emulated }, async () => {
  const o = await store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }, { provider: 'bepaid' });
  assert.equal(o.amount, 2990);
  assert.equal(await paid(o.pid), 'paid');
  assert.equal(await paid(o.pid), 'duplicate');
  const sale = (await db.doc('market/dev1_game/sales/buyer1').get()).data();
  assert.deepEqual([sale.price, sale.fee, sale.currency, sale.claimed, sale.card], [2990, 299, 'BYN', true, true]);
  assert.ok((await db.doc('wallets/buyer1/purchases/dev1_game').get()).exists);
  const receipt = (await db.doc(`receipts/${o.pid}`).get()).data();
  assert.deepEqual([receipt.amount, receipt.currency, receipt.company.unp, receipt.refunded], [2990, 'BYN', '000000000', false]);
  const e = (await db.doc('earnings/dev1').get()).data().totals.BYN;
  assert.deepEqual([e.gross, e.fee, e.net], [2990, 299, 2691]);
  assert.equal((await db.doc('market/dev1_game').get()).data().sales, 1);
  await assert.rejects(store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }, { provider: 'bepaid' }), (e) => e.code === 'OWNED');
});

test('подмена суммы, валюты или тестового режима — заказ не выдаётся', { skip: !emulated }, async () => {
  const o = await store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }, { provider: 'bepaid' });
  assert.equal(await paid(o.pid, false), 'mismatch');
  assert.equal((await db.doc('market/dev1_game/sales/buyer1').get()).exists, false);
  const o2 = await store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'USD' }, { provider: 'bepaid' });
  assert.equal(await paid(o2.pid, true, 'BYN'), 'mismatch');
  const o3 = await store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'USD' }, { provider: 'bepaid' });
  assert.equal(await store.markPaid(db, o3.pid, { provider: 'bepaid', providerId: 'x', amountOk: true, currency: 'USD', test: true }), 'mismatch');
  assert.equal(await store.markPaid(db, 'p_0_000000000000', { provider: 'bepaid', providerId: 'x', amountOk: true, currency: 'USD', test: false }), 'missing');
});

test('нельзя: своё, нет цены в валюте, снятое, заблокированному, без почты', { skip: !emulated }, async () => {
  const code = async (user, input) => { try { await store.createPayment(db, user, input, { provider: 'bepaid' }); return 'ok'; } catch (e) { return e.code; } };
  assert.equal(await code({ uid: DEV, email: 'dev@ml.test', name: 'D' }, { type: 'game', target: 'dev1_game', currency: 'BYN' }), 'OWN');
  assert.equal(await code(BUYER, { type: 'game', target: 'dev1_game', currency: 'EUR' }), 'NO_PRICE');
  assert.equal(await code(BUYER, { type: 'game', target: 'dev1_game', currency: 'GBP' }), 'BAD_CURRENCY');
  assert.equal(await code({ uid: 'x', name: 'x' }, { type: 'game', target: 'dev1_game', currency: 'BYN' }), 'SIGN_IN');
  await db.doc('bans/buyer1').set({ note: 'x' });
  assert.equal(await code(BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }), 'BANNED');
  await db.doc('bans/buyer1').delete();
  await db.doc('takedowns/listing_dev1_game').set({ reason: 'malware' });
  assert.equal(await code(BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }), 'NOT_FOUND');
  await db.doc('takedowns/listing_dev1_game').delete();
  await db.doc('config/store').set({ enabled: false }, { merge: true });
  assert.equal(await code(BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }), 'STORE_OFF');
});

test('скидка со сроком считается сервером', { skip: !emulated }, async () => {
  await db.doc('market/dev1_game').update({ sale: { percent: 50, until: Timestamp.fromDate(new Date(Date.now() + 86_400_000)) } });
  const o = await store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }, { provider: 'bepaid' });
  assert.equal(o.amount, 1495);
});

test('подарок: только другу и только если у него нет игры; у друга — игра и записка', { skip: !emulated }, async () => {
  const code = async (input) => { try { await store.createPayment(db, BUYER, input, { provider: 'webpay' }); return 'ok'; } catch (e) { return e.code; } };
  assert.equal(await code({ type: 'gift', target: 'dev1_game', currency: 'BYN', giftTo: 'friend1' }), 'NOT_FRIEND');
  await db.doc('friendships/buyer1_friend1').set({ users: ['buyer1', 'friend1'], status: 'accepted' });
  const o = await store.createPayment(db, BUYER, { type: 'gift', target: 'dev1_game', currency: 'BYN', giftTo: 'friend1', message: 'С днём рождения!' }, { provider: 'webpay' });
  assert.equal(await store.markPaid(db, o.pid, { provider: 'webpay', providerId: 'w1', amountOk: true, currency: 'BYN', test: false }), 'paid');
  assert.ok((await db.doc('market/dev1_game/sales/friend1').get()).exists);
  assert.equal((await db.doc('market/dev1_game/sales/buyer1').get()).exists, false);
  const gift = (await db.doc(`gifts/${o.pid}`).get()).data();
  assert.deepEqual([gift.to, gift.message], ['friend1', 'С днём рождения!']);
  assert.equal(await code({ type: 'gift', target: 'dev1_game', currency: 'BYN', giftTo: 'friend1' }), 'GIFT_OWNED');
});

test('премиум: месяц + месяц = 60 дней; возврат снимает свой срок', { skip: !emulated }, async () => {
  const a = await store.createPayment(db, BUYER, { type: 'premium', plan: 'month', currency: 'BYN' }, { provider: 'bepaid' });
  assert.equal(a.amount, 990);
  await paid(a.pid);
  const b = await store.createPayment(db, BUYER, { type: 'premium', plan: 'month', currency: 'BYN' }, { provider: 'bepaid' });
  await paid(b.pid);
  const days = ((await db.doc('premium/buyer1').get()).data().until.toMillis() - Date.now()) / 86_400_000;
  assert.ok(days > 59.9 && days < 60.1, String(days));
  await store.markRefunded(db, b.pid, { refundUid: 'r1', reason: 'x', by: 'admin' });
  const after = ((await db.doc('premium/buyer1').get()).data().until.toMillis() - Date.now()) / 86_400_000;
  assert.ok(after > 29.9 && after < 30.1, String(after));
  assert.equal((await db.doc('earnings/_platform').get()).exists, false);
});

test('поддержка автора: в пределах суммы, комиссия площадки, заработок автора', { skip: !emulated }, async () => {
  await assert.rejects(store.createPayment(db, BUYER, { type: 'tip', target: DEV, currency: 'BYN', amount: 50 }, { provider: 'bepaid' }), (e) => e.code === 'BAD_AMOUNT');
  const o = await store.createPayment(db, BUYER, { type: 'tip', target: DEV, currency: 'BYN', amount: 1000, message: 'Спасибо!' }, { provider: 'bepaid' });
  await paid(o.pid);
  const e = (await db.doc('earnings/dev1').get()).data().totals.BYN;
  assert.deepEqual([e.gross, e.fee, e.net], [1000, 100, 900]);
  assert.equal((await db.doc(`earnings/dev1/entries/${o.pid}`).get()).data().type, 'tip');
});

test('возврат игры: продажа закрыта, из библиотеки убрана, чек отмечен, заработок уменьшен; второй раз — duplicate', { skip: !emulated }, async () => {
  const o = await store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }, { provider: 'bepaid' });
  await assert.rejects(store.markRefunded(db, o.pid, { refundUid: 'r', by: 'a' }), (e) => e.code === 'NOT_PAID');
  await paid(o.pid);
  assert.equal(await store.markRefunded(db, o.pid, { refundUid: 'r-1', reason: 'не запускается', by: 'admin1' }), 'refunded');
  assert.equal(await store.markRefunded(db, o.pid, { refundUid: 'r-1', by: 'admin1' }), 'duplicate');
  assert.equal((await db.doc('market/dev1_game/sales/buyer1').get()).data().refunded, true);
  assert.equal((await db.doc('wallets/buyer1/purchases/dev1_game').get()).exists, false);
  assert.equal((await db.doc(`receipts/${o.pid}`).get()).data().refunded, true);
  assert.equal((await db.doc('earnings/dev1').get()).data().totals.BYN.net, 0);
  // После возврата можно купить снова.
  assert.ok((await store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }, { provider: 'bepaid' })).pid);
});

test('ключи: выпускает только владелец игры, ключ срабатывает один раз', { skip: !emulated }, async () => {
  await assert.rejects(store.generateKeys(db, 'stranger', 'dev1_game', 3), (e) => e.code === 'NOT_YOURS');
  await assert.rejects(store.generateKeys(db, DEV, 'dev1_game', 0), (e) => e.code === 'BAD_COUNT');
  const codes = await store.generateKeys(db, DEV, 'dev1_game', 3);
  assert.equal(codes.length, 3);
  assert.match(codes[0], /^[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}$/);
  const r = await store.redeemKey(db, BUYER, codes[0].toLowerCase());
  assert.equal(r.title, 'Космос');
  assert.ok((await db.doc('market/dev1_game/sales/buyer1').get()).data().key);
  await assert.rejects(store.redeemKey(db, FRIEND, codes[0]), (e) => e.code === 'KEY_USED');
  await assert.rejects(store.redeemKey(db, BUYER, codes[1]), (e) => e.code === 'OWNED');
  await assert.rejects(store.redeemKey(db, FRIEND, 'AAAAA-BBBBB-CCCCC'), (e) => e.code === 'BAD_KEY');
});

test('выплаты: не больше доступного, сумма резервируется, отказ возвращает', { skip: !emulated }, async () => {
  const o = await store.createPayment(db, BUYER, { type: 'game', target: 'dev1_game', currency: 'BYN' }, { provider: 'bepaid' });
  await paid(o.pid); // доступно 2691
  await assert.rejects(store.requestPayout(db, DEV, 'BYN', 3000, 'IBAN BY00...'), (e) => e.code === 'NO_MONEY');
  await assert.rejects(store.requestPayout(db, DEV, 'BYN', 500, 'IBAN BY00...'), (e) => e.code === 'MIN_PAYOUT');
  const id = await store.requestPayout(db, DEV, 'BYN', 2000, 'IBAN BY00 TEST 0000');
  await assert.rejects(store.requestPayout(db, DEV, 'BYN', 1000, 'IBAN BY00 TEST 0000'), (e) => e.code === 'NO_MONEY');
  assert.equal(await store.resolvePayout(db, 'admin1', id, false, 'неверный IBAN'), 'rejected');
  const id2 = await store.requestPayout(db, DEV, 'BYN', 2691, 'IBAN BY00 TEST 0000');
  assert.equal(await store.resolvePayout(db, 'admin1', id2, true, 'п/п 15'), 'paid');
  await assert.rejects(store.resolvePayout(db, 'admin1', id2, true, ''), (e) => e.code === 'ALREADY');
  const t = (await db.doc('earnings/dev1').get()).data().totals.BYN;
  assert.deepEqual([t.net, t.paid, t.pending], [2691, 2691, 0]);
});

test('VirusTotal: опасный файл снимается сам — снятие, сводка, чёрный список, жалоба в очередь', { skip: !emulated }, async () => {
  const sha = 'a'.repeat(64);
  const fake = async () => new Response(JSON.stringify({ data: { attributes: { last_analysis_stats: { malicious: 12, harmless: 50 } } } }), { status: 200 });
  assert.equal(await scanItem(db, 'key', { kind: 'listing', id: 'dev1_game', sha256: sha, title: 'Космос', owner: DEV }, fake), 'malicious');
  assert.ok((await db.doc('takedowns/listing_dev1_game').get()).exists);
  assert.ok((await db.doc('config/takedowns').get()).data().ids.includes('listing_dev1_game'));
  assert.ok((await db.doc('config/blocklist').get()).data().hashes.includes(sha));
  assert.equal((await db.doc('reports/listing_dev1_game__server').get()).data().status, 'open');
  const unknown = async () => new Response('{}', { status: 404 });
  assert.equal(await scanItem(db, 'key', { kind: 'creation', id: 'u_mod', sha256: 'b'.repeat(64), title: 'Mod', owner: 'u' }, unknown), 'queued');
});
