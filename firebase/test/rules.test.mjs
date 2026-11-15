// Проверка правил Firestore на эмуляторе: firebase emulators:exec --only firestore "npm test"
// (из папки firebase/, см. .github/workflows/rules.yml). Деньги — в копейках.
import { test, before, after, beforeEach } from 'node:test';
import { readFileSync } from 'node:fs';
import { initializeTestEnvironment, assertSucceeds, assertFails } from '@firebase/rules-unit-testing';
import { collectionGroup, doc, getDoc, setDoc, updateDoc, deleteDoc, writeBatch, serverTimestamp, increment, collection, getDocs, query, where, arrayUnion, arrayRemove, deleteField } from 'firebase/firestore';

const FEE = 10;
let env;

const ADMIN = { uid: 'admin1', email: 'admin@ml.test' };
const SELLER = { uid: 'seller1', email: 'seller@ml.test' };
const BUYER = { uid: 'buyer1', email: 'buyer@ml.test' };
const OTHER = { uid: 'other1', email: 'other@ml.test' };

const db = (who) => env.authenticatedContext(who.uid, { email: who.email, email_verified: true }).firestore();
const anon = (uid) => env.authenticatedContext(uid, {}).firestore();
const raw = async (fn) => env.withSecurityRulesDisabled(async (c) => fn(c.firestore()));

const LOT = 'seller1_lot';
const FREE = 'seller1_free';
const PRICE = 19900;
const fee = (p) => Math.floor(p * FEE / 100);

function listing(price, sales = 0) {
  return {
    uid: SELLER.uid, author: 'Seller', title: 'Lot', summary: '', description: '', kind: 'model', game: 'subnautica',
    price, license: 'personal', version: '1.0.0', preview: '', tags: [], images: [], size: 10, sha256: '', chunks: 1,
    fileName: 'a.zip', status: 'active', sales, created: new Date(), updated: new Date(),
  };
}

const ZERO = { balance: 0, earned: 0, spent: 0, paidOut: 0, toppedUp: 0, seq: 0 };

async function walletOf(uid) {
  let w = null;
  await raw(async (d) => { const s = await getDoc(doc(d, 'wallets', uid)); w = s.exists() ? s.data() : null; });
  return w ?? { ...ZERO };
}

/** Новое состояние кошелька и запись журнала — как в приложении (Creator/Wallet.cs). */
function next(prev, kind, amount) {
  const n = {
    balance: (prev.balance ?? 0) + amount,
    earned: (prev.earned ?? 0) + (kind === 'sale' || kind === 'fee' || kind === 'tip-in' ? amount : 0),
    spent: (prev.spent ?? 0) + (kind === 'purchase' || kind === 'tip' || kind === 'refund' ? -amount : 0),
    paidOut: (prev.paidOut ?? 0) + (kind === 'payout' || kind === 'payout-return' ? -amount : 0),
    toppedUp: (prev.toppedUp ?? 0) + (kind === 'topup' ? amount : 0),
    seq: (prev.seq ?? 0) + 1,
  };
  return n;
}

function ledger(batch, d, uid, prev, kind, amount, { ref = '', party = '', note = '', by }) {
  const n = next(prev, kind, amount);
  batch.set(doc(d, 'wallets', uid), { ...n, updated: serverTimestamp() });
  batch.set(doc(d, 'wallets', uid, 'ledger', String(n.seq)), {
    kind, amount, balance: n.balance, seq: n.seq, ref, party, note, by, created: serverTimestamp(),
  });
  return n;
}

before(async () => {
  env = await initializeTestEnvironment({
    projectId: 'modlaunch-rules-test',
    firestore: { rules: readFileSync(new URL('../firestore.rules', import.meta.url), 'utf8') },
  });
});
after(async () => { await env?.cleanup(); });

beforeEach(async () => {
  await env.clearFirestore();
  await raw(async (d) => {
    await setDoc(doc(d, 'admins', ADMIN.email), { note: 'test' });
    await setDoc(doc(d, 'market', LOT), listing(PRICE));
    await setDoc(doc(d, 'market', FREE), listing(0));
    // У покупателя 500 ₽ — пополнение, проведённое админом (запись 1 в журнале).
    await setDoc(doc(d, 'wallets', BUYER.uid), { balance: 50000, earned: 0, spent: 0, paidOut: 0, toppedUp: 50000, seq: 1, updated: new Date() });
    await setDoc(doc(d, 'wallets', BUYER.uid, 'ledger', '1'), { kind: 'topup', amount: 50000, balance: 50000, seq: 1, ref: 'seed', party: '', note: '', by: ADMIN.uid, created: new Date() });
  });
});

/** Покупка платного лота — одна пачка, как Market.Buy. */
async function buy(who = BUYER, { amount = -PRICE, price = PRICE, claimed = false, withWallet = true, lot = LOT, feeValue = fee(price) } = {}) {
  const d = db(who);
  const prev = await walletOf(who.uid);
  const b = writeBatch(d);
  b.set(doc(d, 'market', lot, 'sales', who.uid), {
    buyerName: 'Buyer', seller: SELLER.uid, title: 'Lot', price, fee: feeValue, claimed, created: serverTimestamp(),
  });
  if (withWallet) ledger(b, d, who.uid, prev, 'purchase', amount, { ref: lot, party: SELLER.uid, by: who.uid });
  b.update(doc(d, 'market', lot), { sales: increment(1) });
  b.set(doc(d, 'wallets', who.uid, 'purchases', lot), { title: 'Lot', kind: 'model', seller: 'Seller', price, created: serverTimestamp() });
  return b.commit();
}

async function claim(who = SELLER, { amount = PRICE - fee(PRICE), buyer = BUYER.uid } = {}) {
  const d = db(who);
  const prev = await walletOf(who.uid);
  const b = writeBatch(d);
  b.update(doc(d, 'market', LOT, 'sales', buyer), { claimed: true });
  ledger(b, d, who.uid, prev, 'sale', amount, { ref: LOT, party: buyer, by: who.uid });
  return b.commit();
}

// ---------------------------------------------------------------- покупка

test('покупка: списание, продажа и библиотека одной пачкой', async () => {
  await assertSucceeds(buy());
  const w = await walletOf(BUYER.uid);
  if (w.balance !== 50000 - PRICE || w.spent !== PRICE || w.seq !== 2) throw new Error(JSON.stringify(w));
});

test('покупка: сумма списания не равна цене — отказ', async () => {
  await assertFails(buy(BUYER, { amount: -100 }));
});

test('покупка: без списания — отказ', async () => {
  await assertFails(buy(BUYER, { withWallet: false }));
});

test('покупка: денег не хватает — отказ', async () => {
  await raw(async (d) => updateDoc(doc(d, 'market', LOT), { price: 90000 }));
  await assertFails(buy(BUYER, { amount: -90000, price: 90000 }));
});

test('покупка: второй раз тот же лот — отказ', async () => {
  await assertSucceeds(buy());
  await assertFails(buy());
});

test('бесплатный лот: без кошелька, сразу «claimed»', async () => {
  await assertSucceeds(buy(OTHER, { price: 0, claimed: true, withWallet: false, lot: FREE }));
  await assertFails(buy(BUYER, { price: 0, claimed: false, withWallet: false, lot: FREE }));
});

// ---------------------------------------------------------------- кошелёк

test('нельзя начислить себе деньги «из воздуха»', async () => {
  const d = db(BUYER);
  const prev = await walletOf(BUYER.uid);
  for (const kind of ['grant', 'topup', 'sale', 'payout-return']) {
    const b = writeBatch(d);
    ledger(b, d, BUYER.uid, prev, kind, 100000, { ref: 'x', note: 'hi', by: BUYER.uid });
    await assertFails(b.commit());
  }
  // И просто поменять баланс без журнала.
  await assertFails(updateDoc(doc(d, 'wallets', BUYER.uid), { balance: 9999999 }));
});

test('журнал не меняется и не удаляется', async () => {
  const d = db(BUYER);
  await assertFails(updateDoc(doc(d, 'wallets', BUYER.uid, 'ledger', '1'), { amount: 1 }));
  await assertFails(deleteDoc(doc(d, 'wallets', BUYER.uid, 'ledger', '1')));
  await assertFails(deleteDoc(doc(d, 'wallets', BUYER.uid)));
});

test('нельзя пропустить номер записи (seq + 2)', async () => {
  const d = db(BUYER);
  const prev = await walletOf(BUYER.uid);
  const b = writeBatch(d);
  ledger(b, d, BUYER.uid, { ...prev, seq: prev.seq + 1 }, 'purchase', -PRICE, { ref: LOT, party: SELLER.uid, by: BUYER.uid });
  await assertFails(b.commit());
});

test('покупатель не пишет в кошелёк продавца', async () => {
  await assertSucceeds(buy());
  const d = db(BUYER);
  const b = writeBatch(d);
  b.update(doc(d, 'market', LOT, 'sales', BUYER.uid), { claimed: true });
  ledger(b, d, SELLER.uid, { ...ZERO }, 'sale', PRICE - fee(PRICE), { ref: LOT, party: BUYER.uid, by: BUYER.uid });
  await assertFails(b.commit());
});

test('чужой кошелёк и журнал не читаются', async () => {
  await assertFails(getDoc(doc(db(OTHER), 'wallets', BUYER.uid)));
  await assertFails(getDocs(collection(db(OTHER), 'wallets', BUYER.uid, 'ledger')));
  await assertSucceeds(getDoc(doc(db(BUYER), 'wallets', BUYER.uid)));
  await assertSucceeds(getDoc(doc(db(ADMIN), 'wallets', BUYER.uid)));
});

// ---------------------------------------------------------------- выручка продавца

test('продавец забирает выручку один раз', async () => {
  await assertSucceeds(buy());
  await assertSucceeds(claim());
  const w = await walletOf(SELLER.uid);
  if (w.balance !== PRICE - fee(PRICE) || w.earned !== PRICE - fee(PRICE) || w.seq !== 1) throw new Error(JSON.stringify(w));
  await assertFails(claim());
});

test('выручка: сумма без комиссии или чужая продажа — отказ', async () => {
  await assertSucceeds(buy());
  await assertFails(claim(SELLER, { amount: PRICE }));
  await assertFails(claim(OTHER));
});

test('продавец видит свои продажи, чужой — нет', async () => {
  await assertSucceeds(buy());
  await assertSucceeds(getDocs(query(collection(db(SELLER), 'market', LOT, 'sales'), where('claimed', '==', false))));
  await assertFails(getDocs(collection(db(OTHER), 'market', LOT, 'sales')));
});

// ---------------------------------------------------------------- вывод

async function payout(who, amount, pid) {
  const d = db(who);
  const prev = await walletOf(who.uid);
  const b = writeBatch(d);
  b.set(doc(d, 'payouts', pid), { uid: who.uid, author: 'Seller', amount, method: 'card 1234', status: 'pending', note: '', created: serverTimestamp() });
  ledger(b, d, who.uid, prev, 'payout', -amount, { ref: pid, by: who.uid });
  return b.commit();
}

test('вывод: списание вместе с заявкой; больше баланса — нельзя', async () => {
  await assertSucceeds(buy());
  await assertSucceeds(claim());
  await assertFails(payout(SELLER, 999999, `${SELLER.uid}_20261011120000`));
  await assertSucceeds(payout(SELLER, 10000, `${SELLER.uid}_20261011120001`));
  const w = await walletOf(SELLER.uid);
  if (w.balance !== PRICE - fee(PRICE) - 10000 || w.paidOut !== 10000) throw new Error(JSON.stringify(w));
});

test('вывод: заявка без списания — отказ', async () => {
  await assertFails(setDoc(doc(db(SELLER), 'payouts', `${SELLER.uid}_20261011120002`),
    { uid: SELLER.uid, author: 'Seller', amount: 100, method: 'x', status: 'pending', note: '', created: serverTimestamp() }));
});

test('админ: «переведено» — отметка, «отклонено» — с возвратом денег', async () => {
  await assertSucceeds(buy());
  await assertSucceeds(claim());
  const p1 = `${SELLER.uid}_20261011120003`, p2 = `${SELLER.uid}_20261011120004`;
  await assertSucceeds(payout(SELLER, 5000, p1));
  await assertSucceeds(payout(SELLER, 5000, p2));
  await assertSucceeds(updateDoc(doc(db(ADMIN), 'payouts', p1), { status: 'paid', note: 'ok' }));
  // Отклонить без возврата — нельзя; не админу — нельзя.
  await assertFails(updateDoc(doc(db(ADMIN), 'payouts', p2), { status: 'rejected', note: '' }));
  const back = async (who) => {
    const d = db(who);
    const prev = await walletOf(SELLER.uid);
    const b = writeBatch(d);
    b.update(doc(d, 'payouts', p2), { status: 'rejected', note: 'wrong card' });
    ledger(b, d, SELLER.uid, prev, 'payout-return', 5000, { ref: p2, by: who.uid });
    return b.commit();
  };
  await assertFails(back(SELLER));
  await assertSucceeds(back(ADMIN));
  await assertFails(back(ADMIN));
  const w = await walletOf(SELLER.uid);
  if (w.paidOut !== 5000) throw new Error(JSON.stringify(w));
});

// ---------------------------------------------------------------- пополнение и начисления

test('пополнение: заявка пользователя, зачисление только админом и один раз', async () => {
  const tid = `${OTHER.uid}_20261011120000`;
  await assertSucceeds(setDoc(doc(db(OTHER), 'topups', tid), { uid: OTHER.uid, author: 'Other', amount: 30000, reference: 'tx-1', status: 'pending', created: serverTimestamp() }));
  const approve = async (who) => {
    const d = db(who);
    const prev = await walletOf(OTHER.uid);
    const b = writeBatch(d);
    b.update(doc(d, 'topups', tid), { status: 'done' });
    ledger(b, d, OTHER.uid, prev, 'topup', 30000, { ref: tid, by: who.uid });
    return b.commit();
  };
  await assertFails(approve(OTHER));
  await assertSucceeds(approve(ADMIN));
  await assertFails(approve(ADMIN));
  const w = await walletOf(OTHER.uid);
  if (w.balance !== 30000 || w.toppedUp !== 30000 || w.seq !== 1) throw new Error(JSON.stringify(w));
});

test('начисление админом — только с пояснением; баланс не уходит в минус', async () => {
  const grant = async (who, amount, note) => {
    const d = db(who);
    const prev = await walletOf(BUYER.uid);
    const b = writeBatch(d);
    ledger(b, d, BUYER.uid, prev, 'grant', amount, { note, by: who.uid });
    return b.commit();
  };
  await assertFails(grant(ADMIN, 1000, ''));
  await assertSucceeds(grant(ADMIN, 1000, 'bonus'));
  await assertFails(grant(ADMIN, -999999, 'too much'));
  await assertFails(grant(BUYER, 1000, 'self'));
});

test('админ без подтверждённой почты — не админ', async () => {
  const d = env.authenticatedContext(ADMIN.uid, { email: ADMIN.email, email_verified: false }).firestore();
  const prev = await walletOf(BUYER.uid);
  const b = writeBatch(d);
  ledger(b, d, BUYER.uid, prev, 'grant', 1000, { note: 'x', by: ADMIN.uid });
  await assertFails(b.commit());
});

// ---------------------------------------------------------------- отзывы, друзья, Hub

const KEY = '0123456789abcdef0123456789abcdef';
function review(uid, extra = {}) {
  return { game: 'subnautica', mod: '1262', modName: 'Nautilus', key: KEY, uid, name: 'Me', stars: 5, text: 'good', deleted: false,
    app: '9.5.0', created: serverTimestamp(), updated: serverTimestamp(), ...extra };
}

test('отзывы: свой — можно (и анониму), чужой uid — нельзя, «админ» — только админу', async () => {
  await assertSucceeds(setDoc(doc(anon('anon1'), 'reviews', `anon1_${KEY}`), review('anon1')));
  await assertFails(setDoc(doc(db(OTHER), 'reviews', `${BUYER.uid}_${KEY}`), review(BUYER.uid)));
  await assertFails(setDoc(doc(db(BUYER), 'reviews', `${BUYER.uid}_${KEY}`), review(BUYER.uid, { role: 'admin' })));
  await assertSucceeds(getDocs(collection(anon('x'), 'reviews')));
});

test('отзывы: админ скрывает, автор не может вернуть', async () => {
  await assertSucceeds(setDoc(doc(db(BUYER), 'reviews', `${BUYER.uid}_${KEY}`), review(BUYER.uid)));
  await assertFails(updateDoc(doc(db(OTHER), 'reviews', `${BUYER.uid}_${KEY}`), { deleted: true, stars: 0, text: '', moderated: true, updated: serverTimestamp() }));
  await assertSucceeds(updateDoc(doc(db(ADMIN), 'reviews', `${BUYER.uid}_${KEY}`), { deleted: true, stars: 0, text: '', moderated: true, updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(BUYER), 'reviews', `${BUYER.uid}_${KEY}`), { deleted: false, stars: 4, text: 'again', updated: serverTimestamp() }));
});

test('друзья: свой профиль пишет только хозяин', async () => {
  const p = { name: 'Me', code: 'ABCDEFGH', state: 'online', game: '', gameName: '', app: '9.5.0', seen: serverTimestamp(), since: new Date(Date.now() - 1000) };
  await assertSucceeds(setDoc(doc(db(BUYER), 'profiles', BUYER.uid), p));
  await assertFails(setDoc(doc(db(OTHER), 'profiles', BUYER.uid), p));
  await assertFails(setDoc(doc(anon('anon2'), 'profiles', 'anon2'), p));
});

test('Hub: карточку создаёт автор со своим префиксом', async () => {
  const c = { uid: BUYER.uid, author: 'Me', name: 'Mod', summary: '', description: '', game: 'subnautica', version: '1.0.0', kind: 'script', code: '',
    tags: [], images: [], size: 0, sha256: '', chunks: 0, fileName: '', changelog: '', app: '9.5.0', likes: 0, downloads: 0, comments: 0,
    created: serverTimestamp(), updated: serverTimestamp() };
  await assertSucceeds(setDoc(doc(db(BUYER), 'creations', `${BUYER.uid}_mod`), c));
  await assertFails(setDoc(doc(db(BUYER), 'creations', `${OTHER.uid}_mod`), c));
});

test('Hub: счётчик комментариев — только вместе со своим комментарием (26.0)', async () => {
  const id = `${SELLER.uid}_talk`;
  await raw(async (d) => setDoc(doc(d, 'creations', id), { uid: SELLER.uid, author: 'Seller', name: 'Talk', summary: '', description: '',
    game: 'subnautica', version: '1.0.0', kind: 'script', code: '', tags: [], images: [], size: 0, sha256: '', chunks: 0, fileName: '',
    changelog: '', app: '26.0.0', likes: 0, downloads: 0, comments: 0, created: new Date(), updated: new Date() }));
  const comment = (who) => ({ uid: who.uid, author: 'Me', text: 'hi', created: serverTimestamp() });
  const o = db(OTHER);
  // Накрутка: +1 без комментария, комментарий без +1 — нельзя (раньше счётчик двигал кто угодно).
  await assertFails(updateDoc(doc(o, 'creations', id), { comments: increment(1) }));
  await assertFails(updateDoc(doc(o, 'creations', id), { comments: increment(1), lastComment: 'ghost' }));
  await assertFails(setDoc(doc(o, 'creations', id, 'comments', 'c0'), comment(OTHER)));
  // +1 вместе со своим новым комментарием и его id в lastComment — можно.
  let b = writeBatch(o);
  b.set(doc(o, 'creations', id, 'comments', 'c1'), comment(OTHER));
  b.update(doc(o, 'creations', id), { comments: increment(1), lastComment: 'c1' });
  await assertSucceeds(b.commit());
  // Два комментария на одну единицу и +2 — нельзя.
  b = writeBatch(o);
  b.set(doc(o, 'creations', id, 'comments', 'c2'), comment(OTHER));
  b.set(doc(o, 'creations', id, 'comments', 'c3'), comment(OTHER));
  b.update(doc(o, 'creations', id), { comments: increment(1), lastComment: 'c2' });
  await assertFails(b.commit());
  b = writeBatch(o);
  b.set(doc(o, 'creations', id, 'comments', 'c4'), comment(OTHER));
  b.update(doc(o, 'creations', id), { comments: increment(2), lastComment: 'c4' });
  await assertFails(b.commit());
  // −1 без удаления и удаление без −1 — нельзя; удалить свой вместе с −1 — можно.
  await assertFails(updateDoc(doc(o, 'creations', id), { comments: increment(-1), lastComment: 'c1' }));
  await assertFails(deleteDoc(doc(o, 'creations', id, 'comments', 'c1')));
  b = writeBatch(o);
  b.delete(doc(o, 'creations', id, 'comments', 'c1'));
  b.update(doc(o, 'creations', id), { comments: increment(-1), lastComment: 'c1' });
  await assertSucceeds(b.commit());
  // Автор мода убирает чужой комментарий вместе с −1.
  b = writeBatch(o);
  b.set(doc(o, 'creations', id, 'comments', 'c5'), comment(OTHER));
  b.update(doc(o, 'creations', id), { comments: increment(1), lastComment: 'c5' });
  await assertSucceeds(b.commit());
  const s = db(SELLER);
  b = writeBatch(s);
  b.delete(doc(s, 'creations', id, 'comments', 'c5'));
  b.update(doc(s, 'creations', id), { comments: increment(-1), lastComment: 'c5' });
  await assertSucceeds(b.commit());
  let after = null;
  await raw(async (d) => { after = (await getDoc(doc(d, 'creations', id))).data(); });
  if (after.comments !== 0) throw new Error('comments ' + after.comments);
});

test('лоты: продавец создаёт свой; чужое и «продажи +1» без продажи — нельзя', async () => {
  const l = { ...listing(1000), created: serverTimestamp(), updated: serverTimestamp() };
  await assertSucceeds(setDoc(doc(db(SELLER), 'market', `${SELLER.uid}_new`), l));
  await assertFails(setDoc(doc(db(OTHER), 'market', `${SELLER.uid}_new2`), l));
  await assertFails(updateDoc(doc(db(OTHER), 'market', LOT), { sales: increment(1) }));
});

// ---------------------------------------------------------------- 11.0: бэкап в облаке и доска серверов

const SHA = 'a'.repeat(64);
const backupMeta = (over = {}) => ({ at: serverTimestamp(), size: 1024, sha256: SHA, chunks: 1, device: 'PC', version: '11.0.0', ...over });

test('бэкап: свой пишет и читает только хозяин; чужой — нельзя', async () => {
  await assertSucceeds(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta()));
  await assertSucceeds(setDoc(doc(db(BUYER), 'backups', BUYER.uid, 'files', '0'), { data: 'UEsDBA==' }));
  await assertSucceeds(getDoc(doc(db(BUYER), 'backups', BUYER.uid, 'files', '0')));
  // Вторая половина частей: новый бэкап пишется туда, описание переключается последним.
  await assertSucceeds(setDoc(doc(db(BUYER), 'backups', BUYER.uid, 'files', '16'), { data: 'UEsDBA==' }));
  await assertSucceeds(setDoc(doc(db(BUYER), 'backups', BUYER.uid, 'files', '31'), { data: 'UEsDBA==' }));
  await assertSucceeds(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta({ base: 16 })));
  await assertSucceeds(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta({ base: 0 })));
  await assertFails(setDoc(doc(db(OTHER), 'backups', BUYER.uid, 'files', '16'), { data: 'x' }));
  await assertFails(getDoc(doc(db(OTHER), 'backups', BUYER.uid)));
  await assertFails(getDoc(doc(db(OTHER), 'backups', BUYER.uid, 'files', '0')));
  await assertFails(setDoc(doc(db(OTHER), 'backups', BUYER.uid), backupMeta()));
  await assertFails(setDoc(doc(anon('anon3'), 'backups', 'anon3'), backupMeta()));
});

test('бэкап: лишние поля, больше 8 МБ, 17 частей, неверный base, часть за files/31 или часть больше 700 000 знаков — отказ', async () => {
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta({ token: 'x' })));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta({ size: 8388609 })));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta({ chunks: 17 })));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta({ sha256: 'xyz' })));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta({ base: 8 })));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid), backupMeta({ base: '16' })));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid, 'files', '32'), { data: 'x' }));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid, 'files', '016'), { data: 'x' }));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid, 'files', '0'), { data: 'x'.repeat(700001) }));
  await assertFails(setDoc(doc(db(BUYER), 'backups', BUYER.uid, 'files', '0'), { data: 'x', extra: 1 }));
});

const SRV = 'AbCdEfGhIjKlMnOpQrSt';
const server = (who, over = {}) => ({
  owner: who.uid, ownerName: 'Me', name: 'Выживание', game: 'minecraft', address: 'mc.example.org', build: '', mods: 0,
  visibility: 'friends', players: 0, max: 10, version: '1.21.1', created: serverTimestamp(), updated: serverTimestamp(), ...over,
});

test('серверы: хозяин создаёт, правит и удаляет свой; чужой — только читает', async () => {
  await assertSucceeds(setDoc(doc(db(BUYER), 'servers', SRV), server(BUYER)));
  await assertSucceeds(getDoc(doc(db(OTHER), 'servers', SRV)));
  await assertSucceeds(getDocs(collection(db(OTHER), 'servers')));
  await assertSucceeds(updateDoc(doc(db(BUYER), 'servers', SRV), { players: 3, updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(OTHER), 'servers', SRV), { players: 3, updated: serverTimestamp() }));
  await assertFails(deleteDoc(doc(db(OTHER), 'servers', SRV)));
  await assertSucceeds(deleteDoc(doc(db(BUYER), 'servers', SRV)));
});

test('серверы: чужой owner, неверный id, «created» не сервером, лишнее поле, без почты — отказ', async () => {
  await assertFails(setDoc(doc(db(BUYER), 'servers', SRV), server(OTHER)));
  await assertFails(setDoc(doc(db(BUYER), 'servers', 'short'), server(BUYER)));
  await assertFails(setDoc(doc(db(BUYER), 'servers', SRV), server(BUYER, { created: new Date(2020, 0, 1) })));
  await assertFails(setDoc(doc(db(BUYER), 'servers', SRV), server(BUYER, { password: 'x' })));
  await assertFails(setDoc(doc(db(BUYER), 'servers', SRV), server(BUYER, { visibility: 'secret' })));
  await assertFails(setDoc(doc(anon('anon4'), 'servers', SRV), server({ uid: 'anon4' })));
  await assertSucceeds(setDoc(doc(db(BUYER), 'servers', SRV), server(BUYER)));
  await assertFails(updateDoc(doc(db(BUYER), 'servers', SRV), { created: serverTimestamp(), updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(BUYER), 'servers', SRV), { players: 1 }));
});

// ---------------------------------------------------------------- 13.0: группы разработчиков

const TEAM = 'TeAmTeAmTeAmTeAmTeAm';
const CODE = 'ABCD2345';
const team = (who, over = {}) => ({
  name: 'Ночная студия', tag: 'NITE', color: 'violet', about: '', owner: who.uid, members: [who.uid], names: { [who.uid]: 'Owner' },
  invites: [], code: CODE, created: serverTimestamp(), updated: serverTimestamp(), ...over,
});

async function makeTeam() {
  const d = db(SELLER);
  const b = writeBatch(d);
  b.set(doc(d, 'teams', TEAM), team(SELLER));
  b.set(doc(d, 'teamcodes', CODE), { team: TEAM, owner: SELLER.uid });
  await assertSucceeds(b.commit());
}

const joinByCode = (who, code = CODE) => updateDoc(doc(db(who), 'teams', TEAM), {
  members: arrayUnion(who.uid), [`names.${who.uid}`]: 'Joiner', invites: arrayRemove(who.uid), joined: code, updated: serverTimestamp(),
});

test('группы: владелец создаёт группу с кодом; код находит группу; посторонний группу не видит', async () => {
  await makeTeam();
  await assertSucceeds(getDoc(doc(db(BUYER), 'teamcodes', CODE)));
  await assertFails(getDocs(collection(db(BUYER), 'teamcodes')));
  await assertFails(getDoc(doc(db(BUYER), 'teams', TEAM)));
  await assertSucceeds(getDoc(doc(db(SELLER), 'teams', TEAM)));
  await assertSucceeds(getDocs(query(collection(db(SELLER), 'teams'), where('members', 'array-contains', SELLER.uid))));
  await assertFails(getDocs(collection(db(BUYER), 'teams')));
});

test('группы: вход по нынешнему коду — да, по старому или без кода — нет; лишнего при входе не поменять', async () => {
  await makeTeam();
  await assertFails(joinByCode(BUYER, 'WRONG234'));
  await assertFails(updateDoc(doc(db(BUYER), 'teams', TEAM), { members: arrayUnion(BUYER.uid), [`names.${BUYER.uid}`]: 'B', updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(BUYER), 'teams', TEAM), { members: arrayUnion(BUYER.uid), [`names.${BUYER.uid}`]: 'B', joined: CODE, name: 'Моя', updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(BUYER), 'teams', TEAM), { members: arrayUnion(BUYER.uid, OTHER.uid), [`names.${BUYER.uid}`]: 'B', joined: CODE, updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(BUYER), 'teams', TEAM), { members: arrayUnion(BUYER.uid), [`names.${SELLER.uid}`]: 'Взлом', [`names.${BUYER.uid}`]: 'B', joined: CODE, updated: serverTimestamp() }));
  await assertSucceeds(joinByCode(BUYER));
  await assertSucceeds(getDoc(doc(db(BUYER), 'teams', TEAM)));
  // Владелец сменил код: старый больше не пускает.
  const d = db(SELLER);
  const b = writeBatch(d);
  b.update(doc(d, 'teams', TEAM), { code: 'ZZZZ2345', updated: serverTimestamp() });
  b.delete(doc(d, 'teamcodes', CODE));
  b.set(doc(d, 'teamcodes', 'ZZZZ2345'), { team: TEAM, owner: SELLER.uid });
  await assertSucceeds(b.commit());
  await assertFails(joinByCode(OTHER, CODE));
  await assertSucceeds(joinByCode(OTHER, 'ZZZZ2345'));
});

test('группы: приглашение — принять или отклонить; участник выходит сам; убирает только владелец', async () => {
  await makeTeam();
  await assertSucceeds(updateDoc(doc(db(SELLER), 'teams', TEAM), { invites: [BUYER.uid, OTHER.uid], updated: serverTimestamp() }));
  await assertSucceeds(getDocs(query(collection(db(BUYER), 'teams'), where('invites', 'array-contains', BUYER.uid))));
  await assertSucceeds(updateDoc(doc(db(BUYER), 'teams', TEAM), { members: arrayUnion(BUYER.uid), [`names.${BUYER.uid}`]: 'B', invites: arrayRemove(BUYER.uid), updated: serverTimestamp() }));
  await assertSucceeds(updateDoc(doc(db(OTHER), 'teams', TEAM), { invites: arrayRemove(OTHER.uid), updated: serverTimestamp() }));
  await assertFails(getDoc(doc(db(OTHER), 'teams', TEAM)));
  // Участник не может убрать другого, сменить название или код.
  await assertFails(updateDoc(doc(db(BUYER), 'teams', TEAM), { name: 'Моя', updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(BUYER), 'teams', TEAM), { code: 'QQQQ2345', updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(BUYER), 'teams', TEAM), { members: arrayRemove(SELLER.uid), updated: serverTimestamp() }));
  await assertFails(deleteDoc(doc(db(BUYER), 'teams', TEAM)));
  // Выход: только себя.
  await assertSucceeds(updateDoc(doc(db(BUYER), 'teams', TEAM), { members: arrayRemove(BUYER.uid), [`names.${BUYER.uid}`]: deleteField(), updated: serverTimestamp() }));
  // Владелец не добавляет людей сам (только приглашает) и не отдаёт группу.
  await assertFails(updateDoc(doc(db(SELLER), 'teams', TEAM), { members: arrayUnion(OTHER.uid), updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(SELLER), 'teams', TEAM), { owner: BUYER.uid, updated: serverTimestamp() }));
  await assertSucceeds(updateDoc(doc(db(SELLER), 'teams', TEAM), { name: 'Новое имя', tag: 'NEW', color: 'green', updated: serverTimestamp() }));
  await assertSucceeds(deleteDoc(doc(db(SELLER), 'teams', TEAM)));
});

test('группы: неверный тег, цвет, чужой owner, лишнее поле, без почты, код чужой группы — отказ', async () => {
  await assertFails(setDoc(doc(db(SELLER), 'teams', TEAM), team(SELLER, { tag: 'nite' })));
  await assertFails(setDoc(doc(db(SELLER), 'teams', TEAM), team(SELLER, { color: 'gold' })));
  await assertFails(setDoc(doc(db(SELLER), 'teams', TEAM), team(BUYER)));
  await assertFails(setDoc(doc(db(SELLER), 'teams', TEAM), team(SELLER, { members: [SELLER.uid, BUYER.uid] })));
  await assertFails(setDoc(doc(db(SELLER), 'teams', TEAM), team(SELLER, { secret: 1 })));
  await assertFails(setDoc(doc(anon('anon5'), 'teams', TEAM), team({ uid: 'anon5' })));
  await makeTeam();
  await assertFails(setDoc(doc(db(BUYER), 'teamcodes', 'BBBB2345'), { team: TEAM, owner: BUYER.uid }));
  await assertFails(deleteDoc(doc(db(BUYER), 'teamcodes', CODE)));
});

test('группы: проекты команды — участники читают и выкладывают, посторонние нет; by — только свой', async () => {
  await makeTeam();
  await assertSucceeds(joinByCode(BUYER));
  const proj = (who, over = {}) => ({ name: 'Дешёвые семена', game: 'stardew-valley', version: '1.0.0', by: who.uid, byName: 'B', script: 'mod "x"', updated: serverTimestamp(), ...over });
  await assertSucceeds(setDoc(doc(db(BUYER), 'teams', TEAM, 'projects', 'seeds'), proj(BUYER)));
  await assertSucceeds(getDocs(collection(db(SELLER), 'teams', TEAM, 'projects')));
  await assertSucceeds(setDoc(doc(db(SELLER), 'teams', TEAM, 'projects', 'seeds'), proj(SELLER, { version: '1.1.0' })));
  await assertFails(getDocs(collection(db(OTHER), 'teams', TEAM, 'projects')));
  await assertFails(setDoc(doc(db(OTHER), 'teams', TEAM, 'projects', 'evil'), proj(OTHER)));
  await assertFails(setDoc(doc(db(BUYER), 'teams', TEAM, 'projects', 'seeds'), proj(SELLER)));
  await assertFails(setDoc(doc(db(BUYER), 'teams', TEAM, 'projects', 'big'), proj(BUYER, { script: 'x'.repeat(100001) })));
  await assertFails(setDoc(doc(db(BUYER), 'teams', TEAM, 'projects', 'bad id!'), proj(BUYER)));
  // Удалить: тот, кто выложил последним, или владелец.
  await assertFails(deleteDoc(doc(db(BUYER), 'teams', TEAM, 'projects', 'seeds')));
  await assertSucceeds(deleteDoc(doc(db(SELLER), 'teams', TEAM, 'projects', 'seeds')));
});

// ---------------------------------------------------------------- 14.0: комиссия владельца

async function takeFee(who = ADMIN, { amount = fee(PRICE), buyer = BUYER.uid } = {}) {
  const d = db(who);
  const prev = await walletOf(who.uid);
  const b = writeBatch(d);
  b.update(doc(d, 'market', LOT, 'sales', buyer), { feeTaken: true });
  ledger(b, d, who.uid, prev, 'fee', amount, { ref: LOT, party: buyer, note: 'Lot', by: who.uid });
  return b.commit();
}

test('комиссия: процент меняет только админ, 0–29 (меньше 30 % у Steam), и покупка считается по нему', async () => {
  const cfg = (who, over = {}) => setDoc(doc(db(who), 'config', 'market'), { fee: 20, by: who.uid, updated: serverTimestamp(), ...over });
  await assertFails(cfg(SELLER, { by: SELLER.uid }));
  await assertFails(cfg(ADMIN, { fee: 51 }));
  await assertFails(cfg(ADMIN, { fee: 30 }));
  await assertSucceeds(cfg(ADMIN, { fee: 29 }));
  await assertFails(cfg(ADMIN, { fee: 12.5 }));
  await assertFails(cfg(ADMIN, { by: SELLER.uid }));
  await assertFails(setDoc(doc(db(ADMIN), 'config', 'other'), { fee: 20, by: ADMIN.uid, updated: serverTimestamp() }));
  await assertSucceeds(cfg(ADMIN));
  await assertSucceeds(getDoc(doc(anon('anon9'), 'config', 'market')));
  // Старые 10 % больше не проходят, новые 20 % — да.
  await assertFails(buy());
  await assertSucceeds(buy(BUYER, { feeValue: Math.floor(PRICE * 20 / 100) }));
});

test('комиссия: админ забирает долю с продажи один раз и ровно fee', async () => {
  await assertSucceeds(buy());
  await assertFails(takeFee(ADMIN, { amount: fee(PRICE) + 1 }));
  await assertFails(takeFee(SELLER));
  await assertSucceeds(takeFee(ADMIN));
  const w = await walletOf(ADMIN.uid);
  if (w.balance !== fee(PRICE) || w.earned !== fee(PRICE)) throw new Error(JSON.stringify(w));
  await assertFails(takeFee(ADMIN));
  // Продавец по-прежнему забирает свою выручку.
  await assertSucceeds(claim());
});

test('комиссия: все продажи разом видит только админ', async () => {
  await assertSucceeds(buy());
  await assertSucceeds(getDocs(collectionGroup(db(ADMIN), 'sales')));
  await assertFails(getDocs(collectionGroup(db(SELLER), 'sales')));
  await assertFails(getDocs(collectionGroup(db(BUYER), 'sales')));
});

// ---------------------------------------------------------------- 27.0: поддержка автора и ModLaunch

const TIP = 10000;
let tipSeq = 0;
const tipId = (who) => `t_${who.uid}_2026111300${String(10 + tipSeq++).padStart(4, '0')}`;

/** Поддержка одной пачкой, как Creator/Tips.cs: документ tips/{tid} и списание «tip» на ту же сумму. */
async function give(who = BUYER, { to = SELLER.uid, amount = TIP, debit = amount, feeValue, claimed, withWallet = true, tid = tipId(who) } = {}) {
  const d = db(who);
  const prev = await walletOf(who.uid);
  const b = writeBatch(d);
  const platform = to === '_platform';
  b.set(doc(d, 'tips', tid), {
    from: who.uid, fromName: 'Buyer', to, toName: platform ? 'ModLaunch' : 'Seller', amount,
    fee: feeValue ?? (platform ? amount : fee(amount)), message: 'спасибо!', claimed: claimed ?? platform, feeTaken: false, created: serverTimestamp(),
  });
  if (withWallet) ledger(b, d, who.uid, prev, 'tip', -debit, { ref: tid, party: to, by: who.uid });
  await b.commit();
  return tid;
}

async function claimTip(tid, who = SELLER, { amount = TIP - fee(TIP), from = BUYER.uid } = {}) {
  const d = db(who);
  const prev = await walletOf(who.uid);
  const b = writeBatch(d);
  b.update(doc(d, 'tips', tid), { claimed: true });
  ledger(b, d, who.uid, prev, 'tip-in', amount, { ref: tid, party: from, by: who.uid });
  return b.commit();
}

async function takeTipFee(tid, who = ADMIN, amount = fee(TIP)) {
  const d = db(who);
  const prev = await walletOf(who.uid);
  const b = writeBatch(d);
  b.update(doc(d, 'tips', tid), { feeTaken: true });
  ledger(b, d, who.uid, prev, 'fee', amount, { ref: tid, party: BUYER.uid, by: who.uid });
  return b.commit();
}

test('поддержка: списание и поддержка одной пачкой; автор забирает без комиссии один раз', async () => {
  const tid = await assertSucceeds(give());
  const w = await walletOf(BUYER.uid);
  if (w.balance !== 50000 - TIP || w.spent !== TIP || w.seq !== 2) throw new Error(JSON.stringify(w));
  await assertFails(claimTip(tid, SELLER, { amount: TIP }));
  await assertFails(claimTip(tid, OTHER));
  await assertSucceeds(claimTip(tid));
  const s = await walletOf(SELLER.uid);
  if (s.balance !== TIP - fee(TIP) || s.earned !== TIP - fee(TIP)) throw new Error(JSON.stringify(s));
  await assertFails(claimTip(tid));
});

test('поддержка: без списания, не та сумма, себе, меньше 10 ₽, не та комиссия, чужой id — отказ', async () => {
  await assertFails(give(BUYER, { withWallet: false }));
  await assertFails(give(BUYER, { debit: 100 }));
  await assertFails(give(BUYER, { to: BUYER.uid }));
  await assertFails(give(BUYER, { amount: 500 }));
  await assertFails(give(BUYER, { feeValue: 0 }));
  await assertFails(give(BUYER, { claimed: true }));
  await assertFails(give(BUYER, { tid: tipId(OTHER) }));
  await assertFails(give(BUYER, { amount: 90000 }));
});

test('поддержка ModLaunch: вся сумма — комиссия, её забирает только админ и один раз', async () => {
  const tid = await assertSucceeds(give(BUYER, { to: '_platform' }));
  await assertFails(claimTip(tid, SELLER, { amount: 0 }));
  await assertFails(takeTipFee(tid, SELLER, TIP));
  await assertFails(takeTipFee(tid, ADMIN, TIP - 1));
  await assertSucceeds(takeTipFee(tid, ADMIN, TIP));
  const a = await walletOf(ADMIN.uid);
  if (a.balance !== TIP || a.earned !== TIP) throw new Error(JSON.stringify(a));
  await assertFails(takeTipFee(tid, ADMIN, TIP));
});

test('поддержка: видят дарящий, автор и админ; комиссию с поддержки автора админ забирает отдельно', async () => {
  const tid = await give();
  await assertSucceeds(getDoc(doc(db(BUYER), 'tips', tid)));
  await assertSucceeds(getDoc(doc(db(SELLER), 'tips', tid)));
  await assertFails(getDoc(doc(db(OTHER), 'tips', tid)));
  await assertSucceeds(getDocs(query(collection(db(SELLER), 'tips'), where('to', '==', SELLER.uid), where('claimed', '==', false))));
  await assertFails(getDocs(collection(db(OTHER), 'tips')));
  await assertSucceeds(takeTipFee(tid));
  await assertSucceeds(claimTip(tid));
});

// ── 28.0: чат друзей и почтовый ящик ───────────────────────────────────
const pairOf = (a, b) => (a < b ? `${a}_${b}` : `${b}_${a}`);
async function befriend(a, b, status = 'accepted') {
  await raw((f) => setDoc(doc(f, 'friendships', pairOf(a.uid, b.uid)), {
    users: [a.uid, b.uid].sort(), from: a.uid, to: b.uid, fromName: 'A', toName: 'B', status, created: new Date(), updated: new Date(),
  }));
}
let msgSeq = 0;
const msgId = (who) => `${1760000000000 + (++msgSeq)}_${who.uid}`;
const message = (who, extra = {}) => ({ from: who.uid, text: 'привет', kind: 'text', game: '', gameName: '', at: serverTimestamp(), ...extra });
const entry = (extra = {}) => ({ at: serverTimestamp(), text: 'привет', name: 'Me', kind: 'text', ...extra });

test('чат: пишут и читают только двое друзей, менять и удалять нельзя', async () => {
  await befriend(BUYER, SELLER);
  const pair = pairOf(BUYER.uid, SELLER.uid);
  const first = msgId(BUYER);
  await assertSucceeds(setDoc(doc(db(BUYER), 'chats', pair, 'messages', first), message(BUYER)));
  await assertSucceeds(getDocs(collection(db(SELLER), 'chats', pair, 'messages')));
  await assertFails(getDocs(collection(db(OTHER), 'chats', pair, 'messages')));
  await assertFails(setDoc(doc(db(OTHER), 'chats', pair, 'messages', msgId(OTHER)), message(OTHER)));
  await assertFails(setDoc(doc(db(BUYER), 'chats', pair, 'messages', msgId(SELLER)), message(BUYER)));
  await assertFails(setDoc(doc(db(BUYER), 'chats', pair, 'messages', msgId(BUYER) + 'x'), message(BUYER)));
  await assertFails(setDoc(doc(db(BUYER), 'chats', pair, 'messages', msgId(BUYER)), message(BUYER, { from: SELLER.uid })));
  await assertFails(setDoc(doc(db(BUYER), 'chats', pair, 'messages', msgId(BUYER)), message(BUYER, { text: '' })));
  await assertFails(setDoc(doc(db(BUYER), 'chats', pair, 'messages', msgId(BUYER)), message(BUYER, { at: new Date() })));
  await assertFails(setDoc(doc(db(BUYER), 'chats', pair, 'messages', msgId(BUYER)), message(BUYER, { extra: 1 })));
  await assertSucceeds(setDoc(doc(db(BUYER), 'chats', pair, 'messages', msgId(BUYER)), message(BUYER, { kind: 'invite', text: '', game: 'steam:730', gameName: 'Counter-Strike 2' })));
  await assertFails(updateDoc(doc(db(BUYER), 'chats', pair, 'messages', first), { text: 'исправил' }));
  await assertFails(deleteDoc(doc(db(BUYER), 'chats', pair, 'messages', first)));
  await assertFails(getDocs(collection(anon('anon3'), 'chats', pair, 'messages')));
});

test('чат: пока дружба не принята — ни писать, ни читать', async () => {
  await befriend(BUYER, SELLER, 'pending');
  const pair = pairOf(BUYER.uid, SELLER.uid);
  await assertFails(setDoc(doc(db(BUYER), 'chats', pair, 'messages', msgId(BUYER)), message(BUYER)));
  await assertFails(getDocs(collection(db(SELLER), 'chats', pair, 'messages')));
  await assertFails(setDoc(doc(db(BUYER), 'inbox', SELLER.uid), { [BUYER.uid]: entry() }));
});

test('почтовый ящик: друг пишет только своё поле, читает только хозяин', async () => {
  await befriend(BUYER, SELLER);
  await assertSucceeds(setDoc(doc(db(SELLER), 'inbox', BUYER.uid), { [SELLER.uid]: entry() }));
  await assertSucceeds(updateDoc(doc(db(SELLER), 'inbox', BUYER.uid), { [SELLER.uid]: entry({ text: 'ещё', kind: 'invite' }) }));
  await assertFails(updateDoc(doc(db(SELLER), 'inbox', BUYER.uid), { [BUYER.uid]: entry() }));
  await assertFails(updateDoc(doc(db(SELLER), 'inbox', BUYER.uid), { [SELLER.uid]: entry({ at: new Date() }) }));
  await assertFails(updateDoc(doc(db(SELLER), 'inbox', BUYER.uid), { [SELLER.uid]: entry({ text: 'x'.repeat(121) }) }));
  await assertFails(setDoc(doc(db(OTHER), 'inbox', BUYER.uid), { [OTHER.uid]: entry() }));
  await assertFails(setDoc(doc(db(BUYER), 'inbox', BUYER.uid), { [BUYER.uid]: entry() }));
  await assertSucceeds(getDoc(doc(db(BUYER), 'inbox', BUYER.uid)));
  await assertFails(getDoc(doc(db(SELLER), 'inbox', BUYER.uid)));
  await assertSucceeds(deleteDoc(doc(db(BUYER), 'inbox', BUYER.uid)));
});

// ── 29.0: инди-игры — лот вида game ─────────────────────────────────────
test('инди-игра: лот game создаётся, ссылку на сборку видят только разработчик и купившие', async () => {
  const game = { ...listing(29900), kind: 'game', game: 'windows', tags: ['puzzle', 'story'], size: 0, chunks: 0, sha256: 'a'.repeat(64), fileName: 'game.zip' };
  const id = `${SELLER.uid}_lantern`;
  await assertSucceeds(setDoc(doc(db(SELLER), 'market', id), { ...game, sales: 0, created: serverTimestamp(), updated: serverTimestamp() }));
  await assertFails(setDoc(doc(db(SELLER), 'market', `${SELLER.uid}_other`), { ...game, kind: 'film', sales: 0, created: serverTimestamp(), updated: serverTimestamp() }));
  const link = { uid: SELLER.uid, data: JSON.stringify({ url: 'https://example.com/game.zip', sha256: 'a'.repeat(64), size: 123456789 }) };
  await assertSucceeds(setDoc(doc(db(SELLER), 'market', id, 'files', 'link'), link));
  await assertFails(setDoc(doc(db(OTHER), 'market', id, 'files', 'link'), { ...link, uid: OTHER.uid }));
  await assertSucceeds(getDoc(doc(db(SELLER), 'market', id, 'files', 'link')));
  await assertFails(getDoc(doc(db(BUYER), 'market', id, 'files', 'link')));
});

// ---------------------------------------------------------------- 30.0: возврат покупки

const RID = `r_${BUYER.uid}_${LOT}`;
const refundReq = (over = {}) => ({
  uid: BUYER.uid, author: 'Buyer', listing: LOT, title: 'Lot', seller: SELLER.uid, price: PRICE,
  reason: 'не запускается', status: 'pending', note: '', created: serverTimestamp(), ...over,
});

/** Решение админа одной пачкой, как Market.ResolveRefund. */
async function decideRefund(who = ADMIN, { amount = PRICE, mark = true } = {}) {
  const d = db(who);
  const prev = await walletOf(BUYER.uid);
  const b = writeBatch(d);
  b.update(doc(d, 'refunds', RID), { status: 'done', note: 'ок' });
  if (mark) b.update(doc(d, 'market', LOT, 'sales', BUYER.uid), { refunded: true, claimed: true });
  ledger(b, d, BUYER.uid, prev, 'refund', amount, { ref: RID, party: SELLER.uid, note: 'Lot', by: who.uid });
  return b.commit();
}

test('возврат: заявка — только на свою платную покупку, с её ценой, с причиной и в течение 14 дней', async () => {
  await assertFails(setDoc(doc(db(BUYER), 'refunds', RID), refundReq()));
  await assertSucceeds(buy());
  await assertFails(setDoc(doc(db(OTHER), 'refunds', RID), refundReq({ uid: OTHER.uid })));
  await assertFails(setDoc(doc(db(BUYER), 'refunds', RID), refundReq({ price: PRICE - 1 })));
  await assertFails(setDoc(doc(db(BUYER), 'refunds', RID), refundReq({ reason: 'нет' })));
  await assertFails(setDoc(doc(db(BUYER), 'refunds', RID), refundReq({ status: 'done' })));
  await assertFails(setDoc(doc(db(BUYER), 'refunds', `r_x_${LOT}`), refundReq()));
  await assertSucceeds(setDoc(doc(db(BUYER), 'refunds', RID), refundReq()));
  await raw(async (d) => {
    await deleteDoc(doc(d, 'refunds', RID));
    await updateDoc(doc(d, 'market', LOT, 'sales', BUYER.uid), { created: new Date(Date.now() - 15 * 86400000) });
  });
  await assertFails(setDoc(doc(db(BUYER), 'refunds', RID), refundReq()));
});

test('возврат: решает только админ; деньги ровно цена, продажа закрыта, файл не открыть, выручку не забрать', async () => {
  await assertSucceeds(buy());
  await raw(async (d) => setDoc(doc(d, 'market', LOT, 'files', '0'), { uid: SELLER.uid, data: 'x' }));
  await assertSucceeds(getDoc(doc(db(BUYER), 'market', LOT, 'files', '0')));
  await assertSucceeds(setDoc(doc(db(BUYER), 'refunds', RID), refundReq()));
  await assertFails(decideRefund(BUYER));
  await assertFails(decideRefund(ADMIN, { amount: PRICE + 100 }));
  await assertFails(decideRefund(ADMIN, { mark: false }));
  await assertSucceeds(decideRefund(ADMIN));
  await assertFails(decideRefund(ADMIN));
  const w = await walletOf(BUYER.uid);
  if (w.balance !== 50000 || w.spent !== 0 || w.seq !== 3) throw new Error(JSON.stringify(w));
  await assertFails(getDoc(doc(db(BUYER), 'market', LOT, 'files', '0')));
  await assertFails(claim());
});

test('возврат: отказ — отметка админа; без заявки продажу «возвращённой» не сделать; чужие заявки не видны', async () => {
  await assertSucceeds(buy());
  await assertFails(updateDoc(doc(db(ADMIN), 'market', LOT, 'sales', BUYER.uid), { refunded: true, claimed: true }));
  await assertSucceeds(setDoc(doc(db(BUYER), 'refunds', RID), refundReq()));
  await assertFails(updateDoc(doc(db(BUYER), 'refunds', RID), { status: 'rejected', note: '' }));
  await assertSucceeds(updateDoc(doc(db(ADMIN), 'refunds', RID), { status: 'rejected', note: 'файл рабочий' }));
  await assertFails(decideRefund(ADMIN));
  await assertSucceeds(getDoc(doc(db(BUYER), 'refunds', RID)));
  await assertFails(getDoc(doc(db(OTHER), 'refunds', RID)));
});

// ---------------------------------------------------------------- 30.0: кураторы

const curator = (over = {}) => ({ name: 'Seller', about: 'игры без спешки', picks: 0, followers: 0, updated: serverTimestamp(), ...over });
const pick = (over = {}) => ({ game: 'steam:264710', title: 'Subnautica', verdict: 'yes', text: 'отлично', updated: serverTimestamp(), ...over });

/** Подписка одной пачкой, как Social/Curators.cs: запись в followers и +1 к счётчику. */
async function follow(who, uid = SELLER.uid, { by = 1, withDoc = true } = {}) {
  const d = db(who);
  const b = writeBatch(d);
  if (withDoc) b.set(doc(d, 'curators', uid, 'followers', who.uid), { created: serverTimestamp() });
  b.update(doc(d, 'curators', uid), { followers: increment(by) });
  return b.commit();
}

async function unfollow(who, uid = SELLER.uid) {
  const d = db(who);
  const b = writeBatch(d);
  b.delete(doc(d, 'curators', uid, 'followers', who.uid));
  b.update(doc(d, 'curators', uid), { followers: increment(-1) });
  return b.commit();
}

test('кураторы: страницу заводит и правит только хозяин; подписчиков себе не накрутить', async () => {
  await assertFails(setDoc(doc(db(OTHER), 'curators', SELLER.uid), curator()));
  await assertFails(setDoc(doc(db(SELLER), 'curators', SELLER.uid), curator({ followers: 100 })));
  await assertFails(setDoc(doc(anon('anon1'), 'curators', 'anon1'), curator()));
  await assertSucceeds(setDoc(doc(db(SELLER), 'curators', SELLER.uid), curator()));
  await assertSucceeds(updateDoc(doc(db(SELLER), 'curators', SELLER.uid), { about: 'новое', updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(SELLER), 'curators', SELLER.uid), { followers: 5, updated: serverTimestamp() }));
  await assertFails(updateDoc(doc(db(OTHER), 'curators', SELLER.uid), { about: 'чужое', updated: serverTimestamp() }));
  await assertSucceeds(getDoc(doc(anon('anon2'), 'curators', SELLER.uid)));
});

test('кураторы: мнения пишет только хозяин — ключ игры совпадает, вердикт из трёх, текст до 500', async () => {
  await assertSucceeds(setDoc(doc(db(SELLER), 'curators', SELLER.uid), curator()));
  const at = (who, game) => doc(db(who), 'curators', SELLER.uid, 'picks', game);
  await assertFails(setDoc(at(OTHER, 'steam:264710'), pick()));
  await assertFails(setDoc(at(SELLER, 'steam:1'), pick()));
  await assertFails(setDoc(at(SELLER, 'steam:264710'), pick({ verdict: 'maybe' })));
  await assertFails(setDoc(at(SELLER, 'steam:264710'), pick({ text: 'x'.repeat(501) })));
  await assertSucceeds(setDoc(at(SELLER, 'steam:264710'), pick()));
  await assertSucceeds(getDoc(doc(anon('anon3'), 'curators', SELLER.uid, 'picks', 'steam:264710')));
  await assertFails(deleteDoc(at(OTHER, 'steam:264710')));
  await assertSucceeds(deleteDoc(at(SELLER, 'steam:264710')));
});

test('кураторы: подписка — ровно +1 вместе со своей записью, один раз; отписка — −1', async () => {
  await assertSucceeds(setDoc(doc(db(SELLER), 'curators', SELLER.uid), curator()));
  await assertFails(follow(BUYER, SELLER.uid, { withDoc: false }));
  await assertFails(follow(BUYER, SELLER.uid, { by: 5 }));
  await assertFails(follow(SELLER));
  await assertSucceeds(follow(BUYER));
  await assertFails(follow(BUYER));
  await assertSucceeds(follow(OTHER));
  let c = null;
  await raw(async (d) => { c = (await getDoc(doc(d, 'curators', SELLER.uid))).data(); });
  if (c.followers !== 2) throw new Error(JSON.stringify(c));
  await assertFails(unfollow(ADMIN));
  await assertSucceeds(unfollow(BUYER));
  await raw(async (d) => { c = (await getDoc(doc(d, 'curators', SELLER.uid))).data(); });
  if (c.followers !== 1) throw new Error(JSON.stringify(c));
});
