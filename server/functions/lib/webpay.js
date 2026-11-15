// WEBPAY (Беларусь): поля формы оплаты с подписью и проверка уведомления об оплате.
// Документация: https://docs.webpay.by/en/paymentIntegration/cardIntegration/ (orderSignature, paymentNotification).
import { createHash, randomBytes } from 'node:crypto';

export const SANDBOX_URL = 'https://securesandbox.webpay.by/';

const sha1 = (s) => createHash('sha1').update(s, 'utf8').digest('hex');
const md5 = (s) => createHash('md5').update(s, 'utf8').digest('hex');

/** Сумма из копеек в формат формы: «21.90», целое — без дробной части («1»). */
export function formatTotal(minor) {
  return minor % 100 === 0 ? String(minor / 100) : (minor / 100).toFixed(2);
}

/** Подпись формы (wsb_version 2 — SHA1): seed + storeid + order_num + test + currency_id + total + SecretKey. */
export function formSignature({ seed, storeId, orderNum, test, currency, total, secret }) {
  return sha1(`${seed}${storeId}${orderNum}${test ? 1 : 0}${currency}${total}${secret}`);
}

/** Все поля формы оплаты. production — адрес формы из договора (публично не опубликован). */
export function formFields({ storeId, storeName, secret, orderNum, currency, amount, test, title, returnUrl, cancelUrl, notifyUrl, language, email }) {
  const seed = `${Date.now()}${randomBytes(4).toString('hex')}`;
  const total = formatTotal(amount);
  return {
    '*scart': '',
    wsb_version: '2',
    wsb_language_id: language === 'ru' ? 'russian' : 'english',
    wsb_storeid: storeId,
    wsb_store: storeName ?? 'ModLaunch',
    wsb_order_num: orderNum,
    wsb_test: test ? '1' : '0',
    wsb_currency_id: currency,
    wsb_seed: seed,
    wsb_signature: formSignature({ seed, storeId, orderNum, test, currency, total, secret }),
    wsb_return_url: returnUrl,
    wsb_cancel_return_url: cancelUrl,
    wsb_notify_url: notifyUrl,
    'wsb_invoice_item_name[0]': title.slice(0, 255),
    'wsb_invoice_item_quantity[0]': '1',
    'wsb_invoice_item_price[0]': total,
    wsb_total: total,
    ...(email ? { wsb_email: email } : {}),
  };
}

/** HTML-страница, которая сама отправляет форму в WEBPAY (у программы нет своей формы). */
export function autoSubmitPage(action, fields) {
  const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
  const inputs = Object.entries(fields).map(([k, v]) => `<input type="hidden" name="${esc(k)}" value="${esc(v)}">`).join('');
  return `<!doctype html><html><head><meta charset="utf-8"><title>ModLaunch → WEBPAY</title></head>`
    + `<body onload="document.forms[0].submit()"><form method="post" action="${esc(action)}">${inputs}`
    + `<noscript><button type="submit">Pay / Оплатить</button></noscript></form></body></html>`;
}

/**
 * Проверка уведомления (MD5): batch_timestamp + currency_id + amount + payment_method + order_id + site_order_id +
 * transaction_id + payment_type + rrn [+ card] + SecretKey. Поля — ровно как пришли.
 */
export function verifyNotify(f, secret) {
  if (!f?.wsb_signature) return false;
  const base = `${f.batch_timestamp ?? ''}${f.currency_id ?? ''}${f.amount ?? ''}${f.payment_method ?? ''}${f.order_id ?? ''}`
    + `${f.site_order_id ?? ''}${f.transaction_id ?? ''}${f.payment_type ?? ''}${f.rrn ?? ''}`;
  const want = String(f.wsb_signature).toLowerCase();
  // С картой и без — оба варианта из документации (поле card приходит не во всех сценариях).
  return md5(base + (f.card ?? '') + secret) === want || md5(base + secret) === want;
}

/**
 * Сумма из уведомления совпадает с заказом (в копейках)? Документация не говорит, приходит ли «21.90» или «2190»,
 * поэтому принимаем оба написания — но только точное совпадение.
 */
export function amountMatches(minor, raw) {
  const s = String(raw ?? '').trim().replace(',', '.');
  if (!/^\d+(\.\d{1,2})?$/.test(s)) return false;
  if (s.includes('.')) return Math.round(Number(s) * 100) === minor;
  return Number(s) === minor || Number(s) * 100 === minor;
}

/** Успешная оплата: payment_type 1 или 4. */
export function parseNotify(f) {
  return {
    trackingId: f.site_order_id,
    success: f.payment_type === '1' || f.payment_type === '4',
    amountRaw: f.amount,
    currency: f.currency_id,
    providerId: f.transaction_id,
  };
}
