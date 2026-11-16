// Сервер ModLaunch (Firebase Cloud Functions, 2-е поколение). Ключи платёжных систем, VirusTotal и почты —
// только здесь (Secret Manager), в программу не попадают. Как выложить — server/README.md.
import { createHmac, timingSafeEqual } from 'node:crypto';
import { initializeApp } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';
import { getFirestore, Timestamp } from 'firebase-admin/firestore';
import { setGlobalOptions } from 'firebase-functions/v2';
import { HttpsError, onCall, onRequest } from 'firebase-functions/v2/https';
import { onDocumentCreated, onDocumentWritten } from 'firebase-functions/v2/firestore';
import { onSchedule } from 'firebase-functions/v2/scheduler';
import { defineSecret, defineString } from 'firebase-functions/params';
import * as bepaid from './lib/bepaid.js';
import * as webpay from './lib/webpay.js';
import * as store from './lib/store.js';
import * as scan from './lib/scan.js';
import * as mail from './lib/mail.js';

initializeApp();
const db = getFirestore();
setGlobalOptions({ region: 'europe-west1', maxInstances: 20 });

// ---------------------------------------------------------------- настройки и секреты

const PROVIDER = defineString('PAYMENT_PROVIDER', { default: 'none', description: 'bepaid | webpay | none' });
const TEST = defineString('PAYMENT_TEST', { default: 'true', description: 'true — тестовый режим платёжной системы' });
const SITE = defineString('PUBLIC_SITE', { default: 'https://modlaunchapp.com' });
const FUNCTIONS_URL = defineString('FUNCTIONS_URL', { default: 'https://europe-west1-modhub-reviews.cloudfunctions.net' });
const WEBPAY_FORM = defineString('WEBPAY_FORM_URL', { default: webpay.SANDBOX_URL, description: 'адрес формы из договора WEBPAY' });
const MAIL_FROM = defineString('MAIL_FROM', { default: 'ModLaunch <no-reply@modlaunchapp.com>' });
const BEPAID_SHOP_ID = defineSecret('BEPAID_SHOP_ID');
const BEPAID_SECRET_KEY = defineSecret('BEPAID_SECRET_KEY');
const BEPAID_PUBLIC_KEY = defineSecret('BEPAID_PUBLIC_KEY');
const WEBPAY_STORE_ID = defineSecret('WEBPAY_STORE_ID');
const WEBPAY_SECRET = defineSecret('WEBPAY_SECRET');
const VT_API_KEY = defineSecret('VT_API_KEY');
const SMTP_URL = defineSecret('SMTP_URL');
const LINK_SECRET = defineSecret('LINK_SECRET');

const testMode = () => TEST.value() !== 'false';

function fail(e) {
  if (e instanceof HttpsError) throw e;
  if (e instanceof store.StoreError) throw new HttpsError('failed-precondition', e.code);
  console.error(e);
  throw new HttpsError('internal', 'SERVER');
}

function needUser(req, verified = true) {
  const t = req.auth?.token;
  if (!req.auth?.uid || !t?.email) throw new HttpsError('unauthenticated', 'SIGN_IN');
  if (verified && t.email_verified !== true) throw new HttpsError('failed-precondition', 'VERIFY_FIRST');
  return { uid: req.auth.uid, email: t.email, name: t.name ?? t.email.split('@')[0] };
}

async function needAdmin(req) {
  const user = needUser(req);
  if (!(await store.isAdmin(db, req.auth.token))) throw new HttpsError('permission-denied', 'DENIED');
  return user;
}

const sign = (secret, value) => createHmac('sha256', secret).update(value).digest('hex').slice(0, 32);
const sameSig = (a, b) => a.length === b.length && timingSafeEqual(Buffer.from(a), Buffer.from(b));

// ---------------------------------------------------------------- оплата

export const createPayment = onCall({ secrets: [BEPAID_SHOP_ID, BEPAID_SECRET_KEY, LINK_SECRET], enforceAppCheck: false }, async (req) => {
  const user = needUser(req);
  const provider = PROVIDER.value();
  if (provider !== 'bepaid' && provider !== 'webpay') throw new HttpsError('failed-precondition', 'PAYMENTS_OFF');
  try {
    const order = await store.createPayment(db, user, req.data, { provider, test: testMode() });
    const back = `${SITE.value()}/pay/`;
    let redirectUrl;
    if (provider === 'bepaid') {
      const c = await bepaid.createCheckout({ shopId: BEPAID_SHOP_ID.value(), secret: BEPAID_SECRET_KEY.value() }, {
        test: testMode(), amount: order.amount, currency: req.data.currency, description: `ModLaunch: ${order.title}`, trackingId: order.pid,
        language: req.data.lang, successUrl: `${back}success.html?p=${order.pid}`, failUrl: `${back}fail.html?p=${order.pid}`,
        notificationUrl: `${FUNCTIONS_URL.value()}/bepaidWebhook`, email: user.email,
      });
      await db.doc(`payments/${order.pid}`).update({ providerToken: c.token });
      redirectUrl = c.redirectUrl;
    } else {
      redirectUrl = `${FUNCTIONS_URL.value()}/payForm?p=${order.pid}&t=${sign(LINK_SECRET.value(), order.pid)}`;
    }
    return { pid: order.pid, amount: order.amount, currency: req.data.currency, redirectUrl, test: testMode() };
  } catch (e) { fail(e); }
});

/** WEBPAY: страница, которая отправляет подписанную форму оплаты (ссылка подписана, чужой заказ не открыть). */
export const payForm = onRequest({ secrets: [WEBPAY_STORE_ID, WEBPAY_SECRET, LINK_SECRET] }, async (req, res) => {
  const pid = String(req.query.p ?? '');
  if (!/^p_\d+_[0-9a-f]{12}$/.test(pid) || !sameSig(String(req.query.t ?? ''), sign(LINK_SECRET.value(), pid))) { res.status(403).send('Forbidden'); return; }
  const p = (await db.doc(`payments/${pid}`).get()).data();
  if (!p || p.status !== 'created' || p.provider !== 'webpay') { res.status(410).send('This payment link is no longer valid.'); return; }
  const fields = webpay.formFields({
    storeId: WEBPAY_STORE_ID.value(), secret: WEBPAY_SECRET.value(), orderNum: pid, currency: p.currency, amount: p.amount,
    test: p.test, title: `ModLaunch: ${p.title}`, returnUrl: `${SITE.value()}/pay/success.html?p=${pid}`, cancelUrl: `${SITE.value()}/pay/fail.html?p=${pid}`,
    notifyUrl: `${FUNCTIONS_URL.value()}/webpayNotify`, language: 'ru',
  });
  res.set('Content-Type', 'text/html; charset=utf-8').set('Cache-Control', 'no-store').send(webpay.autoSubmitPage(WEBPAY_FORM.value(), fields));
});

export const bepaidWebhook = onRequest({ secrets: [BEPAID_PUBLIC_KEY] }, async (req, res) => {
  const raw = req.rawBody?.toString('utf8') ?? '';
  if (!bepaid.verifyWebhook(raw, req.get('Content-Signature'), BEPAID_PUBLIC_KEY.value())) { res.status(403).send('bad signature'); return; }
  const n = bepaid.parseWebhook(JSON.parse(raw));
  if (n.status === 'successful' && n.trackingId && (!n.type || n.type === 'payment')) {
    const p = (await db.doc(`payments/${n.trackingId}`).get()).data();
    const outcome = p ? await store.markPaid(db, n.trackingId, { provider: 'bepaid', providerId: n.providerId, amountOk: n.amount === p.amount, currency: n.currency, test: n.test }) : 'missing';
    console.log('bepaid', n.trackingId, outcome);
  } else if (n.trackingId && ['failed', 'expired', 'declined', 'error'].includes(n.status)) {
    await db.doc(`payments/${n.trackingId}`).update({ status: 'failed', failedAt: Timestamp.now(), failReason: String(n.status) }).catch(() => {});
  }
  res.status(200).send('OK');
});

export const webpayNotify = onRequest({ secrets: [WEBPAY_SECRET] }, async (req, res) => {
  const f = req.body ?? {};
  if (!webpay.verifyNotify(f, WEBPAY_SECRET.value())) { res.status(403).send('bad signature'); return; }
  const n = webpay.parseNotify(f);
  if (n.success && n.trackingId) {
    const p = (await db.doc(`payments/${n.trackingId}`).get()).data();
    const outcome = p ? await store.markPaid(db, n.trackingId, { provider: 'webpay', providerId: n.providerId, amountOk: webpay.amountMatches(p.amount, n.amountRaw), currency: n.currency, test: p.test }) : 'missing';
    console.log('webpay', n.trackingId, outcome);
  }
  res.status(200).send('OK');
});

/** Админ: вернуть деньги за покупку. bePaid — через API; WEBPAY — деньги возвращают в кабинете, здесь отмечаем. */
export const refundPayment = onCall({ secrets: [BEPAID_SHOP_ID, BEPAID_SECRET_KEY] }, async (req) => {
  const admin = await needAdmin(req);
  const pid = String(req.data?.pid ?? '');
  const p = (await db.doc(`payments/${pid}`).get()).data();
  if (!p) throw new HttpsError('not-found', 'NOT_FOUND');
  if (p.status !== 'paid') throw new HttpsError('failed-precondition', 'NOT_PAID');
  try {
    let refundUid = String(req.data?.refundUid ?? '');
    if (p.provider === 'bepaid') {
      refundUid = (await bepaid.refund({ shopId: BEPAID_SHOP_ID.value(), secret: BEPAID_SECRET_KEY.value() },
        { parentUid: p.providerId, amount: p.amount, reason: req.data?.reason, trackingId: pid, requestId: `refund_${pid}` })).refundUid;
    } else if (!refundUid) throw new HttpsError('failed-precondition', 'REFUND_IN_CABINET');
    return { result: await store.markRefunded(db, pid, { refundUid, reason: req.data?.reason, by: admin.uid }) };
  } catch (e) { fail(e); }
});

// ---------------------------------------------------------------- ключи и выплаты

export const generateKeys = onCall(async (req) => {
  const user = needUser(req);
  try { return { codes: await store.generateKeys(db, user.uid, String(req.data?.listing ?? ''), req.data?.count) }; } catch (e) { fail(e); }
});

export const redeemKey = onCall(async (req) => {
  const user = needUser(req);
  try { return await store.redeemKey(db, user, req.data?.code); } catch (e) { fail(e); }
});

export const requestPayout = onCall(async (req) => {
  const user = needUser(req);
  try { return { id: await store.requestPayout(db, user.uid, req.data?.currency, Number(req.data?.amount), req.data?.details) }; } catch (e) { fail(e); }
});

export const resolvePayout = onCall(async (req) => {
  const admin = await needAdmin(req);
  try { return { result: await store.resolvePayout(db, admin.uid, String(req.data?.id ?? ''), !!req.data?.paid, req.data?.note) }; } catch (e) { fail(e); }
});

// ---------------------------------------------------------------- проверка файлов (VirusTotal)

const vtKey = () => { try { return VT_API_KEY.value(); } catch { return ''; } };

export const scanListing = onDocumentWritten({ document: 'market/{id}', secrets: [VT_API_KEY] }, async (event) => {
  const after = event.data?.after?.data();
  const before = event.data?.before?.data();
  if (!after || !vtKey() || !after.sha256 || after.sha256 === before?.sha256 || after.status !== 'active') return;
  const bytes = after.chunks > 0 ? await scan.listingBytes(db, event.params.id, after.chunks) : null;
  await scan.scanItem(db, vtKey(), { kind: 'listing', id: event.params.id, sha256: after.sha256, title: after.title, owner: after.uid, bytes });
});

export const scanCreation = onDocumentWritten({ document: 'creations/{id}', secrets: [VT_API_KEY] }, async (event) => {
  const after = event.data?.after?.data();
  const before = event.data?.before?.data();
  if (!after || !vtKey() || !after.sha256 || after.sha256 === before?.sha256) return;
  const bytes = after.chunks > 0 ? await scan.creationBytes(db, event.params.id, after.version, after.chunks) : null;
  await scan.scanItem(db, vtKey(), { kind: 'creation', id: event.params.id, sha256: after.sha256, title: after.name, owner: after.uid, bytes });
});

/** Файлы, отправленные на анализ: через 6 часов у VirusTotal уже есть вердикт. */
export const rescanQueued = onSchedule({ schedule: 'every 6 hours', secrets: [VT_API_KEY] }, async () => {
  if (!vtKey()) return;
  const queued = await db.collection('scans').where('verdict', '==', 'queued').limit(100).get();
  for (const d of queued.docs) {
    const s = d.data();
    await scan.scanItem(db, vtKey(), { kind: s.kind, id: s.id, sha256: d.id, title: s.title, owner: s.owner }).catch((e) => console.error(e));
  }
});

// ---------------------------------------------------------------- письма

export const newDeviceEmail = onDocumentCreated({ document: 'devices/{uid}/list/{device}', secrets: [SMTP_URL] }, async (event) => {
  const d = event.data?.data();
  if (!d || d.ack === true) return;
  const user = await getAuth().getUser(event.params.uid).catch(() => null);
  if (!user?.email || !user.emailVerified) return;
  const when = new Date().toISOString().replace('T', ' ').slice(0, 16) + ' UTC';
  const m = mail.newDeviceMail('ru', { name: d.name, os: d.os, app: d.app, when });
  await mail.transport(SMTP_URL.value()).sendMail({ from: MAIL_FROM.value(), to: user.email, subject: m.subject, text: m.text, html: m.html });
});

// ---------------------------------------------------------------- лист ожидания (сайт)

const cors = (req, res) => {
  const origin = req.get('Origin') ?? '';
  if (/^https:\/\/(www\.)?modlaunchapp\.com$/.test(origin)) res.set('Access-Control-Allow-Origin', origin).set('Vary', 'Origin');
  res.set('Access-Control-Allow-Methods', 'POST, OPTIONS').set('Access-Control-Allow-Headers', 'Content-Type');
  if (req.method === 'OPTIONS') { res.status(204).send(''); return true; }
  return false;
};

export const waitlistSignup = onRequest({ secrets: [SMTP_URL, LINK_SECRET] }, async (req, res) => {
  if (cors(req, res)) return;
  if (req.method !== 'POST') { res.status(405).send(''); return; }
  const email = String(req.body?.email ?? '').trim().toLowerCase();
  const lang = req.body?.lang === 'en' ? 'en' : 'ru';
  if (!mail.isEmail(email)) { res.status(400).json({ error: 'BAD_EMAIL' }); return; }
  const ref = db.doc(`waitlist/${mail.emailKey(email)}`);
  const doc = (await ref.get()).data();
  if (doc?.confirmed) { res.json({ ok: true, already: true }); return; }
  // Не чаще одного письма в 10 минут на адрес.
  if (doc?.sentAt && Date.now() - doc.sentAt.toMillis() < 600_000) { res.json({ ok: true }); return; }
  await ref.set({ email, lang, confirmed: false, invited: doc?.invited ?? false, created: doc?.created ?? Timestamp.now(), sentAt: Timestamp.now(), source: String(req.body?.source ?? 'site').slice(0, 40) }, { merge: true });
  const url = `${FUNCTIONS_URL.value()}/waitlistConfirm?e=${encodeURIComponent(email)}&t=${mail.waitlistToken(LINK_SECRET.value(), email)}`;
  const m = mail.waitlistMail(lang, url);
  await mail.transport(SMTP_URL.value()).sendMail({ from: MAIL_FROM.value(), to: email, subject: m.subject, text: m.text, html: m.html });
  res.json({ ok: true });
});

export const waitlistConfirm = onRequest({ secrets: [LINK_SECRET] }, async (req, res) => {
  const email = String(req.query.e ?? '').trim().toLowerCase();
  if (!mail.isEmail(email) || !mail.checkWaitlistToken(LINK_SECRET.value(), email, req.query.t)) { res.redirect(302, `${SITE.value()}/waitlist.html?bad=1`); return; }
  await db.doc(`waitlist/${mail.emailKey(email)}`).set({ email, confirmed: true, confirmedAt: Timestamp.now() }, { merge: true });
  res.redirect(302, `${SITE.value()}/waitlist.html?ok=1`);
});

/** Админ: пригласить следующую волну из листа ожидания (по порядку записи) — запуск без пиковой нагрузки. */
export const inviteWave = onCall({ secrets: [SMTP_URL] }, async (req) => {
  await needAdmin(req);
  const size = Math.min(10_000, Math.max(1, Number(req.data?.size ?? 1000)));
  const next = await db.collection('waitlist').where('confirmed', '==', true).where('invited', '==', false).orderBy('created').limit(size).get();
  const t = mail.transport(SMTP_URL.value());
  let sent = 0;
  for (const d of next.docs) {
    const { email, lang } = d.data();
    const ru = lang !== 'en';
    await t.sendMail({
      from: MAIL_FROM.value(), to: email,
      subject: ru ? 'ModLaunch открыт — ваше приглашение' : 'ModLaunch is live — your invite',
      text: ru ? `ModLaunch запущен! Скачать: ${SITE.value()}/download.html` : `ModLaunch is live! Download: ${SITE.value()}/download.html`,
    }).then(() => { sent++; return d.ref.update({ invited: true, invitedAt: Timestamp.now() }); }).catch((e) => console.error(email, e.message));
  }
  return { sent, left: next.size === size };
});

// ---------------------------------------------------------------- заявки покупателей на возврат

export const requestRefund = onCall(async (req) => {
  const user = needUser(req);
  try { return { result: await store.requestRefund(db, user.uid, String(req.data?.pid ?? ''), req.data?.reason) }; } catch (e) { fail(e); }
});

export const rejectRefund = onCall(async (req) => {
  const admin = await needAdmin(req);
  try { return { result: await store.rejectRefund(db, admin.uid, String(req.data?.pid ?? ''), req.data?.note) }; } catch (e) { fail(e); }
});
