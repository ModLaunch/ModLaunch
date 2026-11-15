// bePaid (Беларусь): страница оплаты (checkout), проверка подписи уведомлений, возврат.
// Документация: https://docs.bepaid.by/en/integration/widget/payment_token/ , /en/using_api/webhooks/ ,
// /en/integration/card_api/transactions/refund/
import { createVerify, randomUUID } from 'node:crypto';

export const CHECKOUT_URL = 'https://checkout.bepaid.by/ctp/api/checkouts';
export const REFUND_URL = 'https://gateway.bepaid.by/transactions/refunds';

const basic = (shopId, secret) => 'Basic ' + Buffer.from(`${shopId}:${secret}`).toString('base64');

/** Тело запроса на страницу оплаты. amount — в копейках (минимальных единицах валюты). */
export function checkoutBody({ test, amount, currency, description, trackingId, language, successUrl, failUrl, cancelUrl, notificationUrl, email }) {
  return {
    checkout: {
      test: !!test,
      transaction_type: 'payment',
      attempts: 3,
      order: { amount, currency, description: description.slice(0, 255), tracking_id: trackingId },
      settings: {
        success_url: successUrl, decline_url: failUrl, fail_url: failUrl, cancel_url: cancelUrl ?? failUrl,
        notification_url: notificationUrl, language: language === 'ru' || language === 'be' ? 'ru' : 'en',
      },
      ...(email ? { customer: { email } } : {}),
    },
  };
}

/** Создать страницу оплаты. Возвращает { token, redirectUrl }. fetchImpl — для тестов. */
export async function createCheckout(creds, params, fetchImpl = fetch) {
  const r = await fetchImpl(CHECKOUT_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-API-Version': '2', Authorization: basic(creds.shopId, creds.secret) },
    body: JSON.stringify(checkoutBody(params)),
  });
  const json = await r.json().catch(() => null);
  if (!r.ok || !json?.checkout?.redirect_url) throw new Error(`bePaid checkout ${r.status}: ${json?.message ?? JSON.stringify(json?.errors ?? json)}`);
  return { token: json.checkout.token, redirectUrl: json.checkout.redirect_url };
}

/**
 * Проверка уведомления: заголовок Content-Signature — RSA-SHA256 подпись СЫРОГО тела (base64), ключ — публичный
 * ключ магазина из кабинета bePaid. Без PEM-обёртки ключ тоже принимается.
 */
export function verifyWebhook(rawBody, signatureB64, publicKey) {
  if (!rawBody || !signatureB64 || !publicKey) return false;
  const pem = publicKey.includes('BEGIN') ? publicKey
    : `-----BEGIN PUBLIC KEY-----\n${publicKey.replace(/\s+/g, '').match(/.{1,64}/g).join('\n')}\n-----END PUBLIC KEY-----\n`;
  try {
    const v = createVerify('RSA-SHA256');
    v.update(rawBody);
    v.end();
    return v.verify(pem, Buffer.from(signatureB64, 'base64'));
  } catch {
    return false;
  }
}

/**
 * Что пришло в уведомлении: номер нашего платежа (tracking_id), итог и сумма. Поддерживает и уведомление о
 * транзакции ({ transaction: {...} }), и уведомление страницы оплаты (верхний уровень с order/status).
 */
export function parseWebhook(json) {
  const t = json?.transaction;
  if (t) return { trackingId: t.tracking_id, status: t.status, amount: Number(t.amount), currency: t.currency, providerId: t.uid, test: !!t.test, type: t.type };
  const order = json?.order ?? json?.checkout?.order;
  return { trackingId: order?.tracking_id, status: json?.status, amount: Number(order?.amount), currency: order?.currency, providerId: json?.token, test: !!json?.test, type: json?.transaction_type };
}

/** Возврат: parent_uid — uid платежа в bePaid. RequestID — ключ идемпотентности (тот же при повторе). */
export async function refund(creds, { parentUid, amount, reason, trackingId, requestId = randomUUID() }, fetchImpl = fetch) {
  const r = await fetchImpl(REFUND_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-API-Version': '3', RequestID: requestId, Authorization: basic(creds.shopId, creds.secret) },
    body: JSON.stringify({ request: { parent_uid: parentUid, amount, reason: (reason || 'Customer request').slice(0, 255), tracking_id: trackingId } }),
  });
  const json = await r.json().catch(() => null);
  const status = json?.transaction?.status;
  if (!r.ok || status !== 'successful') throw new Error(`bePaid refund ${r.status}: ${json?.transaction?.message ?? json?.message ?? status}`);
  return { refundUid: json.transaction.uid, requestId };
}
