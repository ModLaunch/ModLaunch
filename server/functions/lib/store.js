// Магазин на сервере: заказ, выдача покупки после оплаты, чек, возврат, ключи, подарки, премиум, выплаты.
// Всё пишет только сервер (Admin SDK); люди читают своё по правилам базы. Деньги — копейки валюты платежа.
import { createHash, randomBytes, randomInt } from 'node:crypto';
import { FieldValue, Timestamp } from 'firebase-admin/firestore';
import { CURRENCIES, PLAN_DAYS, feeFor, inLimits, premiumPrice, priceFor } from './pricing.js';

export class StoreError extends Error {
  constructor(code, message) { super(message ?? code); this.code = code; }
}

const clip = (s, n) => String(s ?? '').slice(0, n);

export function newPaymentId(now = new Date()) {
  return `p_${now.getTime()}_${randomBytes(6).toString('hex')}`;
}

export async function storeConfig(db) {
  return (await db.doc('config/store').get()).data() ?? {};
}

export async function isAdmin(db, token) {
  if (!token?.email || token.email_verified !== true) return false;
  return (await db.doc(`admins/${token.email}`).get()).exists;
}

/** Друзья ли двое (подарок — только другу). */
async function areFriends(db, a, b) {
  const pair = a < b ? `${a}_${b}` : `${b}_${a}`;
  const f = await db.doc(`friendships/${pair}`).get();
  return f.exists && f.data().status === 'accepted';
}

async function owns(db, uid, listingId) {
  const s = await db.doc(`market/${listingId}/sales/${uid}`).get();
  return s.exists && s.data().refunded !== true;
}

/**
 * Заказ: проверить, что покупать, по какой цене (её считает сервер, а не программа), и записать payments/{id}.
 * input: { type: game|gift|premium|tip, target, currency, plan?, amount? (для поддержки), giftTo?, message? }
 */
export async function createPayment(db, user, input, { provider, now = new Date(), test = false } = {}) {
  const { type, currency } = input ?? {};
  if (!CURRENCIES.includes(currency)) throw new StoreError('BAD_CURRENCY');
  if (!user?.uid || !user.email) throw new StoreError('SIGN_IN');
  if ((await db.doc(`bans/${user.uid}`).get()).exists) throw new StoreError('BANNED');
  const store = await storeConfig(db);
  if (store.enabled === false) throw new StoreError('STORE_OFF');
  const base = { uid: user.uid, name: clip(user.name, 32), type, currency, provider, status: 'created', test: !!test, created: Timestamp.fromDate(now) };

  if (type === 'game' || type === 'gift') {
    const id = clip(input.target, 140);
    const snap = await db.doc(`market/${id}`).get();
    const l = snap.data();
    if (!snap.exists || l.status !== 'active') throw new StoreError('NOT_FOUND');
    if ((await db.doc(`takedowns/listing_${id}`).get()).exists) throw new StoreError('NOT_FOUND');
    if (l.uid === user.uid) throw new StoreError('OWN');
    const amount = priceFor(l, currency, now);
    if (amount === null) throw new StoreError('NO_PRICE');
    let giftTo = null;
    if (type === 'gift') {
      giftTo = clip(input.giftTo, 128);
      if (!giftTo || giftTo === user.uid) throw new StoreError('BAD_GIFT');
      if (!(await areFriends(db, user.uid, giftTo))) throw new StoreError('NOT_FRIEND');
      if (await owns(db, giftTo, id)) throw new StoreError('GIFT_OWNED');
    } else if (await owns(db, user.uid, id)) throw new StoreError('OWNED');
    const pid = newPaymentId(now);
    await db.doc(`payments/${pid}`).set({
      ...base, target: id, title: clip(l.title, 60), seller: l.uid, sellerName: clip(l.author, 32), kind: l.kind,
      amount, fee: feeFor(amount, store, l.uid), giftTo, message: clip(input.message, 300),
    });
    return { pid, amount, title: l.title };
  }

  if (type === 'premium') {
    const plan = input.plan === 'year' ? 'year' : 'month';
    const amount = premiumPrice(store, currency, plan);
    if (amount === null) throw new StoreError('NO_PRICE');
    const pid = newPaymentId(now);
    await db.doc(`payments/${pid}`).set({ ...base, target: plan, title: plan === 'year' ? 'ModLaunch Premium · 12 months' : 'ModLaunch Premium · 1 month', seller: '_platform', amount, fee: amount });
    return { pid, amount, title: plan };
  }

  if (type === 'tip') {
    const author = clip(input.target, 128);
    const amount = Number(input.amount);
    if (!inLimits(currency, amount)) throw new StoreError('BAD_AMOUNT');
    if (!author || author === user.uid) throw new StoreError('OWN');
    const platform = author === '_platform';
    const pid = newPaymentId(now);
    await db.doc(`payments/${pid}`).set({
      ...base, target: author, title: platform ? 'Support ModLaunch' : 'Support the author', seller: author,
      amount, fee: platform ? amount : feeFor(amount, store, author), message: clip(input.message, 300),
    });
    return { pid, amount, title: 'tip' };
  }
  throw new StoreError('BAD_TYPE');
}

/**
 * Оплата подтверждена платёжной системой (подпись уже проверена): выдать покупку ровно один раз.
 * Возвращает 'paid' | 'duplicate' | 'mismatch' | 'missing'.
 */
export async function markPaid(db, pid, { provider, providerId, amountOk, currency, test, now = new Date() }) {
  const ref = db.doc(`payments/${pid}`);
  const result = await db.runTransaction(async (tx) => {
    const snap = await tx.get(ref);
    if (!snap.exists) return 'missing';
    const p = snap.data();
    if (p.status === 'paid' || p.status === 'refunded') return 'duplicate';
    if (!amountOk || currency !== p.currency || provider !== p.provider || (!!test) !== (!!p.test)) {
      tx.update(ref, { status: 'mismatch', providerId: clip(providerId, 120), checkedAt: Timestamp.fromDate(now) });
      return 'mismatch';
    }
    const at = Timestamp.fromDate(now);
    const store = await storeConfig(db);
    // Чтения транзакции — до записей.
    const premiumRef = db.doc(`premium/${p.uid}`);
    const premium = p.type === 'premium' ? await tx.get(premiumRef) : null;
    const earningsRef = db.doc(`earnings/${p.seller}`);
    const earnings = p.seller !== '_platform' ? await tx.get(earningsRef) : null;

    tx.update(ref, { status: 'paid', providerId: clip(providerId, 120), paidAt: at });
    tx.set(db.doc(`receipts/${pid}`), {
      uid: p.uid, number: pid, date: at, type: p.type, item: p.title, amount: p.amount, currency: p.currency, provider,
      providerId: clip(providerId, 120), giftTo: p.giftTo ?? null, test: !!p.test, refunded: false,
      company: store.company ?? { name: 'ModLaunch', unp: '', address: '', email: 'support@modlaunchapp.com' },
    });

    if (p.type === 'game' || p.type === 'gift') {
      const owner = p.giftTo ?? p.uid;
      tx.set(db.doc(`market/${p.target}/sales/${owner}`), {
        buyerName: p.type === 'gift' ? clip(`🎁 ${p.name}`, 32) : p.name, seller: p.seller, title: p.title, price: p.amount, fee: p.fee,
        claimed: true, created: at, currency: p.currency, payment: pid, card: true,
      });
      tx.set(db.doc(`wallets/${owner}/purchases/${p.target}`), {
        title: p.title, kind: p.kind ?? 'game', seller: p.sellerName ?? '', price: p.amount, currency: p.currency, payment: pid, created: at,
      });
      if (p.type === 'gift') tx.set(db.doc(`gifts/${pid}`), { from: p.uid, fromName: p.name, to: owner, listing: p.target, title: p.title, message: p.message ?? '', at, seen: false });
    }
    if (p.type === 'premium') {
      const current = premium?.data()?.until?.toDate?.() ?? new Date(0);
      const from = current > now ? current : now;
      const until = new Date(from.getTime() + PLAN_DAYS[p.target] * 86_400_000);
      tx.set(premiumRef, { until: Timestamp.fromDate(until), plan: p.target, lastPayment: pid, updated: at }, { merge: true });
    }
    if (earnings) {
      const net = p.amount - p.fee;
      tx.set(db.doc(`earnings/${p.seller}/entries/${pid}`), {
        type: p.type === 'tip' ? 'tip' : 'sale', listing: p.type === 'tip' ? null : p.target, title: p.title,
        currency: p.currency, gross: p.amount, fee: p.fee, net, at, refunded: false, gift: p.type === 'gift',
      });
      tx.set(earningsRef, { totals: { [p.currency]: { gross: FieldValue.increment(p.amount), fee: FieldValue.increment(p.fee), net: FieldValue.increment(net) } }, updated: at }, { merge: true });
    }
    return 'paid';
  });
  // Счётчик продаж — отдельно и «по возможности»: при хите он горячий, а покупка от него не должна падать.
  if (result === 'paid') {
    const p = (await ref.get()).data();
    if (p.type === 'game' || p.type === 'gift') await db.doc(`market/${p.target}`).update({ sales: FieldValue.increment(1) }).catch(() => {});
  }
  return result;
}

/** Платёжная система вернула деньги: закрыть покупку, вычесть из заработка, отметить чек. */
export async function markRefunded(db, pid, { refundUid, reason, by, now = new Date() }) {
  const ref = db.doc(`payments/${pid}`);
  return db.runTransaction(async (tx) => {
    const snap = await tx.get(ref);
    if (!snap.exists) throw new StoreError('NOT_FOUND');
    const p = snap.data();
    if (p.status === 'refunded') return 'duplicate';
    if (p.status !== 'paid') throw new StoreError('NOT_PAID');
    const premiumRef = db.doc(`premium/${p.uid}`);
    const premium = p.type === 'premium' ? await tx.get(premiumRef) : null;
    const requestRef = db.doc(`refundRequests/${pid}`);
    const request = await tx.get(requestRef);
    const at = Timestamp.fromDate(now);
    tx.update(ref, { status: 'refunded', refundedAt: at, refundUid: clip(refundUid, 120), refundReason: clip(reason, 300), refundedBy: by });
    // Заявка покупателя (если была) закрывается этой же записью.
    if (request?.exists) tx.update(requestRef, { status: 'refunded', resolvedBy: by, resolvedAt: at });
    tx.update(db.doc(`receipts/${pid}`), { refunded: true, refundedAt: at });
    if (p.type === 'game' || p.type === 'gift') {
      const owner = p.giftTo ?? p.uid;
      tx.update(db.doc(`market/${p.target}/sales/${owner}`), { refunded: true });
      tx.delete(db.doc(`wallets/${owner}/purchases/${p.target}`));
    }
    if (p.type === 'premium' && premium?.exists) {
      const until = premium.data().until.toDate();
      tx.update(premiumRef, { until: Timestamp.fromDate(new Date(until.getTime() - PLAN_DAYS[p.target] * 86_400_000)), updated: at });
    }
    if (p.seller !== '_platform') {
      const net = p.amount - p.fee;
      tx.update(db.doc(`earnings/${p.seller}/entries/${pid}`), { refunded: true });
      tx.set(db.doc(`earnings/${p.seller}`), { totals: { [p.currency]: { gross: FieldValue.increment(-p.amount), fee: FieldValue.increment(-p.fee), net: FieldValue.increment(-net) } }, updated: at }, { merge: true });
    }
    return 'refunded';
  });
}

// ---------------------------------------------------------------- ключи

const KEY_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const keyHash = (code) => createHash('sha256').update(code.replace(/[^A-Z0-9]/gi, '').toUpperCase()).digest('hex');

export function newKey() {
  const part = () => Array.from({ length: 5 }, () => KEY_ALPHABET[randomInt(KEY_ALPHABET.length)]).join('');
  return `${part()}-${part()}-${part()}`;
}

/** Ключи для раздачи (пресса, блогеры, конкурсы): видит их один раз разработчик, в базе — только отпечатки. */
export async function generateKeys(db, uid, listingId, count, now = new Date()) {
  const n = Number(count);
  if (!Number.isInteger(n) || n < 1 || n > 500) throw new StoreError('BAD_COUNT');
  const l = (await db.doc(`market/${listingId}`).get()).data();
  if (!l || l.uid !== uid) throw new StoreError('NOT_YOURS');
  const codes = Array.from({ length: n }, newKey);
  const batch = db.batch();
  for (const code of codes) batch.set(db.doc(`storeKeys/${keyHash(code)}`), { listing: listingId, dev: uid, created: Timestamp.fromDate(now), usedBy: null });
  await batch.commit();
  return codes;
}

/** Активировать ключ: игра появляется в библиотеке, ключ больше не действует. */
export async function redeemKey(db, user, code, now = new Date()) {
  const ref = db.doc(`storeKeys/${keyHash(String(code ?? ''))}`);
  return db.runTransaction(async (tx) => {
    const k = await tx.get(ref);
    if (!k.exists) throw new StoreError('BAD_KEY');
    if (k.data().usedBy) throw new StoreError('KEY_USED');
    const listingId = k.data().listing;
    const l = (await tx.get(db.doc(`market/${listingId}`))).data();
    if (!l) throw new StoreError('NOT_FOUND');
    const sale = await tx.get(db.doc(`market/${listingId}/sales/${user.uid}`));
    if (sale.exists && sale.data().refunded !== true) throw new StoreError('OWNED');
    const at = Timestamp.fromDate(now);
    tx.update(ref, { usedBy: user.uid, usedAt: at });
    tx.set(db.doc(`market/${listingId}/sales/${user.uid}`), { buyerName: clip(user.name, 32), seller: l.uid, title: l.title, price: 0, fee: 0, claimed: true, created: at, key: true });
    tx.set(db.doc(`wallets/${user.uid}/purchases/${listingId}`), { title: l.title, kind: l.kind, seller: l.author, price: 0, created: at, key: true });
    return { listing: listingId, title: l.title };
  });
}

// ---------------------------------------------------------------- выплаты разработчикам

/** Заявка на выплату из заработка в одной валюте (сумма резервируется сразу). */
export async function requestPayout(db, uid, currency, amount, details, now = new Date()) {
  if (!CURRENCIES.includes(currency) || !Number.isInteger(amount) || amount <= 0) throw new StoreError('BAD_AMOUNT');
  const store = await storeConfig(db);
  const min = store.minPayout?.[currency] ?? 0;
  if (amount < min) throw new StoreError('MIN_PAYOUT');
  if (clip(details, 300).trim().length < 5) throw new StoreError('NO_DETAILS');
  const ref = db.doc(`earnings/${uid}`);
  const id = `${uid}_${now.getTime()}`;
  await db.runTransaction(async (tx) => {
    const t = (await tx.get(ref)).data()?.totals?.[currency] ?? {};
    const available = (t.net ?? 0) - (t.paid ?? 0) - (t.pending ?? 0);
    if (amount > available) throw new StoreError('NO_MONEY');
    tx.set(ref, { totals: { [currency]: { pending: FieldValue.increment(amount) } } }, { merge: true });
    tx.set(db.doc(`payoutRequests/${id}`), { uid, currency, amount, details: clip(details, 300), status: 'pending', created: Timestamp.fromDate(now) });
  });
  return id;
}

/** Админ: перевели деньги (paid) или отказ (вернуть в доступное). */
export async function resolvePayout(db, adminUid, id, paid, note, now = new Date()) {
  const ref = db.doc(`payoutRequests/${id}`);
  return db.runTransaction(async (tx) => {
    const r = (await tx.get(ref)).data();
    if (!r) throw new StoreError('NOT_FOUND');
    if (r.status !== 'pending') throw new StoreError('ALREADY');
    tx.update(ref, { status: paid ? 'paid' : 'rejected', note: clip(note, 300), resolvedBy: adminUid, resolvedAt: Timestamp.fromDate(now) });
    tx.set(db.doc(`earnings/${r.uid}`), { totals: { [r.currency]: { pending: FieldValue.increment(-r.amount), ...(paid ? { paid: FieldValue.increment(r.amount) } : {}) } } }, { merge: true });
    return paid ? 'paid' : 'rejected';
  });
}

// ---------------------------------------------------------------- заявка покупателя на возврат

export const REFUND_DAYS = 14;

/** Покупатель просит вернуть деньги: своя оплата, оплачена, не старше 14 дней, не поддержка автора. */
export async function requestRefund(db, uid, pid, reason, now = new Date()) {
  const text = clip(reason, 500).trim();
  if (text.length < 5) throw new StoreError('REFUND_REASON');
  const p = (await db.doc(`payments/${pid}`).get()).data();
  if (!p || p.uid !== uid) throw new StoreError('NOT_FOUND');
  if (p.status !== 'paid') throw new StoreError('NOT_PAID');
  if (p.type === 'tip') throw new StoreError('TIP_FINAL');
  const paidAt = p.paidAt?.toDate?.() ?? new Date(0);
  if (now - paidAt > REFUND_DAYS * 86_400_000) throw new StoreError('REFUND_LATE');
  const ref = db.doc(`refundRequests/${pid}`);
  await db.runTransaction(async (tx) => {
    if ((await tx.get(ref)).exists) throw new StoreError('REFUND_EXISTS');
    tx.set(ref, { uid, pid, title: p.title, amount: p.amount, currency: p.currency, type: p.type, reason: text, status: 'pending', created: Timestamp.fromDate(now) });
  });
  return 'pending';
}

/** Админ: отказ по заявке (одобрение — через refundPayment, он же закрывает заявку). */
export async function rejectRefund(db, adminUid, pid, note, now = new Date()) {
  const ref = db.doc(`refundRequests/${pid}`);
  const r = (await ref.get()).data();
  if (!r) throw new StoreError('NOT_FOUND');
  if (r.status !== 'pending') throw new StoreError('ALREADY');
  await ref.update({ status: 'rejected', note: clip(note, 300), resolvedBy: adminUid, resolvedAt: Timestamp.fromDate(now) });
  return 'rejected';
}
