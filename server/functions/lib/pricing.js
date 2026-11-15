// Цены магазина: региональные цены (BYN, RUB, USD, EUR — в копейках), скидки, комиссия площадки.

export const CURRENCIES = ['BYN', 'RUB', 'USD', 'EUR'];

/** Валюта по стране (код ISO 3166-1): Беларусь — BYN, Россия — RUB, еврозона — EUR, остальные — USD. */
const EURO = new Set(['AT', 'BE', 'HR', 'CY', 'EE', 'FI', 'FR', 'DE', 'GR', 'IE', 'IT', 'LV', 'LT', 'LU', 'MT', 'NL', 'PT', 'SK', 'SI', 'ES']);
export function currencyForCountry(country) {
  const c = String(country ?? '').toUpperCase();
  if (c === 'BY') return 'BYN';
  if (c === 'RU') return 'RUB';
  if (EURO.has(c)) return 'EUR';
  return 'USD';
}

/** Минимальная и максимальная сумма платежа (копейки) — защита от «оплаты на 1 копейку» и ошибок с нулями. */
export const LIMITS = {
  BYN: { min: 100, max: 500_000 },       // 1 … 5 000 BYN
  RUB: { min: 3_000, max: 15_000_000 },  // 30 … 150 000 ₽
  USD: { min: 50, max: 200_000 },        // 0,50 … 2 000 $
  EUR: { min: 50, max: 200_000 },
};

export function inLimits(currency, amount) {
  const l = LIMITS[currency];
  return !!l && Number.isInteger(amount) && amount >= l.min && amount <= l.max;
}

/** Действующая скидка (1–90 %), если её срок не истёк. */
export function salePercent(listing, now = new Date()) {
  const s = listing?.sale;
  if (!s || !Number.isInteger(s.percent) || s.percent < 1 || s.percent > 90) return 0;
  const until = s.until?.toDate ? s.until.toDate() : s.until ? new Date(s.until) : null;
  if (!until || until <= now) return 0;
  return s.percent;
}

/** Цена товара в валюте со скидкой (копейки) или null — в этой валюте не продаётся. */
export function priceFor(listing, currency, now = new Date()) {
  const base = listing?.prices?.[currency];
  if (!CURRENCIES.includes(currency) || !Number.isInteger(base) || base <= 0) return null;
  const pct = salePercent(listing, now);
  const price = pct ? Math.round((base * (100 - pct)) / 100) : base;
  return inLimits(currency, price) ? price : null;
}

/** Комиссия площадки: общая (config/store.fee) или отдельная для разработчика; всегда 0–29 %. */
export function feePercent(store, sellerUid) {
  const raw = store?.feeOverrides?.[sellerUid] ?? store?.fee ?? 10;
  return Math.min(29, Math.max(0, Number.isInteger(raw) ? raw : 10));
}

export function feeFor(amount, store, sellerUid) {
  return Math.floor((amount * feePercent(store, sellerUid)) / 100);
}

/** Цена премиума: config/store.premium = { BYN: { month: 990, year: 9900 }, ... }. */
export function premiumPrice(store, currency, plan) {
  const p = store?.premium?.[currency]?.[plan];
  return Number.isInteger(p) && inLimits(currency, p) ? p : null;
}

export const PLAN_DAYS = { month: 30, year: 365 };
