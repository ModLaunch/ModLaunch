import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createSign, generateKeyPairSync, createHash } from 'node:crypto';
import * as bepaid from '../lib/bepaid.js';
import * as webpay from '../lib/webpay.js';
import * as pricing from '../lib/pricing.js';
import { verdictOf } from '../lib/scan.js';
import * as mail from '../lib/mail.js';

test('WEBPAY: подпись формы совпадает с примером из документации (SHA1, wsb_version 2)', () => {
  const sig = webpay.formSignature({ seed: '1242649174', storeId: '11111111', orderNum: 'ORDER-12345678', test: true, currency: 'BYN', total: '21.90', secret: '12345678901234567890' });
  assert.equal(sig, '338d1647833079f9353907ad266ec0bb5264c0d9');
  // Тот же текст через MD5 — пример документации для запросов без версии (проверка, что строка собрана верно).
  const md5 = createHash('md5').update('124264917411111111ORDER-123456781BYN21.9012345678901234567890').digest('hex');
  assert.equal(md5, '5b712daa1743d1a62dfdb7054b3978a1');
});

test('WEBPAY: сумма в форме, поля и страница автоотправки', () => {
  assert.equal(webpay.formatTotal(2190), '21.90');
  assert.equal(webpay.formatTotal(100), '1');
  const f = webpay.formFields({ storeId: '1', secret: 's', orderNum: 'p_1_aaaaaaaaaaaa', currency: 'BYN', amount: 2190, test: true, title: 'Игра', returnUrl: 'https://r', cancelUrl: 'https://c', notifyUrl: 'https://n' });
  assert.equal(f.wsb_total, '21.90');
  assert.equal(f['wsb_invoice_item_price[0]'], '21.90');
  assert.equal(f.wsb_signature, webpay.formSignature({ seed: f.wsb_seed, storeId: '1', orderNum: 'p_1_aaaaaaaaaaaa', test: true, currency: 'BYN', total: '21.90', secret: 's' }));
  const html = webpay.autoSubmitPage('https://securesandbox.webpay.by/', { a: '"<x>' });
  assert.ok(html.includes('&quot;&lt;x>') && !html.includes('"<x>'));
});

test('WEBPAY: уведомление — MD5 по полям, с картой и без; подделка не проходит', () => {
  const f = { batch_timestamp: '1700000000', currency_id: 'BYN', amount: '21.90', payment_method: 'cc', order_id: '127386', site_order_id: 'p_1_aaaaaaaaaaaa', transaction_id: '858578101', payment_type: '4', rrn: '786755995452' };
  const base = Object.values(f).join('');
  const ok = { ...f, wsb_signature: createHash('md5').update(base + 'secret').digest('hex') };
  assert.ok(webpay.verifyNotify(ok, 'secret'));
  assert.ok(!webpay.verifyNotify({ ...ok, amount: '0.01' }, 'secret'));
  assert.ok(!webpay.verifyNotify(ok, 'other'));
  const withCard = { ...f, card: '4****1111', wsb_signature: createHash('md5').update(base + '4****1111' + 'secret').digest('hex') };
  assert.ok(webpay.verifyNotify(withCard, 'secret'));
  const n = webpay.parseNotify(ok);
  assert.equal(n.success, true);
  assert.ok(webpay.amountMatches(2190, '21.90') && webpay.amountMatches(2190, '2190') && !webpay.amountMatches(2190, '21.9a') && !webpay.amountMatches(2190, '22'));
  assert.equal(webpay.parseNotify({ ...f, payment_type: '2' }).success, false);
});

test('bePaid: подпись уведомления RSA-SHA256 по сырому телу; чужой ключ и правка тела — отказ', () => {
  const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
  const body = JSON.stringify({ transaction: { uid: '1-310b0da80b', status: 'successful', amount: 2190, currency: 'BYN', tracking_id: 'p_1_aaaaaaaaaaaa', test: true, type: 'payment' } });
  const sig = createSign('RSA-SHA256').update(body).sign(privateKey, 'base64');
  const pem = publicKey.export({ type: 'spki', format: 'pem' });
  assert.ok(bepaid.verifyWebhook(body, sig, pem));
  // Ключ из кабинета без PEM-обёртки — тоже.
  const bare = pem.replace(/-----[^-]+-----/g, '').replace(/\s+/g, '');
  assert.ok(bepaid.verifyWebhook(body, sig, bare));
  assert.ok(!bepaid.verifyWebhook(body.replace('2190', '1'), sig, pem));
  const other = generateKeyPairSync('rsa', { modulusLength: 2048 }).publicKey.export({ type: 'spki', format: 'pem' });
  assert.ok(!bepaid.verifyWebhook(body, sig, other));
  assert.ok(!bepaid.verifyWebhook(body, '', pem));
  const n = bepaid.parseWebhook(JSON.parse(body));
  assert.deepEqual([n.trackingId, n.status, n.amount, n.currency, n.providerId], ['p_1_aaaaaaaaaaaa', 'successful', 2190, 'BYN', '1-310b0da80b']);
});

test('bePaid: запрос страницы оплаты и возврата — адрес, авторизация, тело', async () => {
  const calls = [];
  const fake = async (url, init) => {
    calls.push({ url, init });
    if (url === bepaid.CHECKOUT_URL) return new Response(JSON.stringify({ checkout: { token: 'tok', redirect_url: 'https://checkout.bepaid.by/v2/checkout?token=tok' } }), { status: 201 });
    return new Response(JSON.stringify({ transaction: { uid: 'r-1', status: 'successful' } }), { status: 200 });
  };
  const c = await bepaid.createCheckout({ shopId: '361', secret: 'key' }, { test: true, amount: 2190, currency: 'BYN', description: 'ModLaunch: Игра', trackingId: 'p_1', language: 'ru', successUrl: 'https://s', failUrl: 'https://f', notificationUrl: 'https://n' }, fake);
  assert.equal(c.redirectUrl, 'https://checkout.bepaid.by/v2/checkout?token=tok');
  const body = JSON.parse(calls[0].init.body);
  assert.equal(calls[0].init.headers.Authorization, 'Basic ' + Buffer.from('361:key').toString('base64'));
  assert.equal(calls[0].init.headers['X-API-Version'], '2');
  assert.deepEqual([body.checkout.test, body.checkout.order.amount, body.checkout.order.tracking_id, body.checkout.settings.language], [true, 2190, 'p_1', 'ru']);
  const r = await bepaid.refund({ shopId: '361', secret: 'key' }, { parentUid: '1-310b0da80b', amount: 2190, reason: 'Не запускается', trackingId: 'p_1', requestId: 'refund_p_1' }, fake);
  assert.equal(r.refundUid, 'r-1');
  assert.equal(calls[1].init.headers.RequestID, 'refund_p_1');
  assert.deepEqual(JSON.parse(calls[1].init.body).request, { parent_uid: '1-310b0da80b', amount: 2190, reason: 'Не запускается', tracking_id: 'p_1' });
  await assert.rejects(bepaid.createCheckout({ shopId: '1', secret: '2' }, { amount: 1, currency: 'BYN', description: 'x', trackingId: 'x' }, async () => new Response('{"message":"fail_url is in invalid format"}', { status: 422 })), /invalid format/);
});

test('цены: регион → валюта, скидка со сроком, пределы, комиссия ≤ 29 %', () => {
  assert.equal(pricing.currencyForCountry('by'), 'BYN');
  assert.equal(pricing.currencyForCountry('DE'), 'EUR');
  assert.equal(pricing.currencyForCountry('KZ'), 'USD');
  const now = new Date('2026-11-15T12:00:00Z');
  const l = { prices: { BYN: 2990, USD: 999, RUB: 1, EUR: 'x' }, sale: { percent: 25, until: new Date('2026-11-20T00:00:00Z') } };
  assert.equal(pricing.priceFor(l, 'BYN', now), 2243);
  assert.equal(pricing.priceFor(l, 'BYN', new Date('2026-12-01')), 2990);
  assert.equal(pricing.priceFor(l, 'RUB', now), null);
  assert.equal(pricing.priceFor(l, 'EUR', now), null);
  assert.equal(pricing.priceFor(l, 'GBP', now), null);
  assert.equal(pricing.feePercent({ fee: 50 }, 'u'), 29);
  assert.equal(pricing.feePercent({ fee: 10, feeOverrides: { u: 5 } }, 'u'), 5);
  assert.equal(pricing.feeFor(2990, { fee: 10 }, 'u'), 299);
});

test('VirusTotal: вердикт по числу срабатываний; письма и токены листа ожидания', () => {
  assert.equal(verdictOf({ malicious: 3 }), 'malicious');
  assert.equal(verdictOf({ malicious: 1 }), 'suspicious');
  assert.equal(verdictOf({ suspicious: 1 }), 'suspicious');
  assert.equal(verdictOf({ harmless: 60 }), 'clean');
  const t = mail.waitlistToken('secret', 'A@B.cc ');
  assert.ok(mail.checkWaitlistToken('secret', 'a@b.cc', t));
  assert.ok(!mail.checkWaitlistToken('secret', 'x@b.cc', t));
  assert.ok(mail.isEmail('player@example.com') && !mail.isEmail('no-at-sign') && !mail.isEmail('a@b'));
  assert.ok(mail.waitlistMail('ru', 'https://x/?a=1&b=2').html.includes('href="https://x/?a=1&amp;b=2"'));
});
