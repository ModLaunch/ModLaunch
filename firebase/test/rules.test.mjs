// Проверка правил Firestore на эмуляторе: firebase emulators:exec --only firestore "npm test"
// (из папки firebase/, см. .github/workflows/rules.yml). Деньги — в копейках.
import { test, before, after, beforeEach } from 'node:test';
import { readFileSync } from 'node:fs';
import { initializeTestEnvironment, assertSucceeds, assertFails } from '@firebase/rules-unit-testing';
import { doc, getDoc, setDoc, updateDoc, deleteDoc, writeBatch, serverTimestamp, increment, collection, getDocs, query, where, arrayUnion, arrayRemove, deleteField } from 'firebase/firestore';

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
    earned: (prev.earned ?? 0) + (kind === 'sale' ? amount : 0),
    spent: (prev.spent ?? 0) + (kind === 'purchase' ? -amount : 0),
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
async function buy(who = BUYER, { amount = -PRICE, price = PRICE, claimed = false, withWallet = true, lot = LOT } = {}) {
  const d = db(who);
  const prev = await walletOf(who.uid);
  const b = writeBatch(d);
  b.set(doc(d, 'market', lot, 'sales', who.uid), {
    buyerName: 'Buyer', seller: SELLER.uid, title: 'Lot', price, fee: fee(price), claimed, created: serverTimestamp(),
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
