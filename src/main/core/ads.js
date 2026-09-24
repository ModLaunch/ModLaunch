'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { fetchJson } = require('./download');

/**
 * Лента рекламы для места в шапке.
 *
 * Адрес ленты лежит внутри программы, в ads.config.json. Лента — обычный
 * JSON-файл на любом хостинге (GitHub, Firebase Hosting, свой сайт):
 *
 *   {
 *     "rotateSeconds": 15,
 *     "items": [
 *       { "id": "shop-1", "title": "Магазин ключей", "text": "Скидки до −70%",
 *         "image": "https://…/logo.png", "url": "https://…", "until": "2026-12-31", "lang": "ru" }
 *     ]
 *   }
 *
 * Всё, что пришло из сети, проверяется здесь: только https, короткие
 * тексты, просроченные объявления отбрасываются. Последняя удачная лента
 * хранится на диске — без интернета показывается она, а не пустое место.
 */

const MAX_ITEMS = 12;
const FRESH_MS = 6 * 60 * 60 * 1000;

function httpsUrl(value) {
  if (!value) return null;
  try {
    const url = new URL(String(value));
    return url.protocol === 'https:' ? url.toString() : null;
  } catch {
    return null;
  }
}

function clip(value, max) {
  return String(value ?? '')
    .replace(/[\u0000-\u001f]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, max);
}

function rotateOf(value, fallback = 15) {
  const n = Number(value);
  if (!Number.isFinite(n) || n <= 0) return fallback;
  return Math.min(120, Math.max(6, Math.round(n)));
}

/**
 * Приводит ленту к безопасному виду.
 * @param {unknown} data
 * @param {number} [now]
 * @returns {{items: object[], rotate: number}}
 */
function sanitizeFeed(data, now = Date.now()) {
  const list = Array.isArray(data) ? data : Array.isArray(data?.items) ? data.items : [];
  const items = [];
  const seen = new Set();

  for (const raw of list) {
    if (!raw || typeof raw !== 'object') continue;
    const title = clip(raw.title, 48);
    const url = httpsUrl(raw.url);
    if (!title || !url) continue;

    const until = raw.until ? Date.parse(raw.until) : NaN;
    // «until: 2026-12-31» — объявление живёт весь этот день.
    if (!Number.isNaN(until) && until + 24 * 60 * 60 * 1000 <= now) continue;
    const from = raw.from ? Date.parse(raw.from) : NaN;
    if (!Number.isNaN(from) && from > now) continue;

    const id = clip(raw.id || title, 64);
    if (seen.has(id)) continue;
    seen.add(id);

    items.push({
      id,
      title,
      text: clip(raw.text, 80),
      image: httpsUrl(raw.image),
      url,
      lang: raw.lang === 'ru' || raw.lang === 'en' ? raw.lang : null,
    });
    if (items.length >= MAX_ITEMS) break;
  }

  return { items, rotate: rotateOf(data?.rotateSeconds) };
}

/** Настройки рекламы из файла внутри программы. */
function loadAdsConfig(file) {
  try {
    const data = JSON.parse(fs.readFileSync(file, 'utf8'));
    return {
      feedUrl: httpsUrl(data.feedUrl),
      advertiseUrl: httpsUrl(data.advertiseUrl),
      rotateSeconds: rotateOf(data.rotateSeconds),
    };
  } catch {
    return { feedUrl: null, advertiseUrl: null, rotateSeconds: 15 };
  }
}

class AdsClient {
  /**
   * @param {{config: {feedUrl: string|null, advertiseUrl: string|null, rotateSeconds: number},
   *          cacheFile: string, fetch?: Function, now?: () => number}} options
   */
  constructor({ config, cacheFile, fetch = fetchJson, now = () => Date.now() }) {
    this.config = config;
    this.cacheFile = cacheFile;
    this.fetch = fetch;
    this.now = now;
  }

  _readCache() {
    try {
      const cached = JSON.parse(fs.readFileSync(this.cacheFile, 'utf8'));
      return cached && typeof cached.at === 'number' ? cached : null;
    } catch {
      return null;
    }
  }

  _writeCache(data) {
    try {
      fs.mkdirSync(path.dirname(this.cacheFile), { recursive: true });
      fs.writeFileSync(this.cacheFile, JSON.stringify({ at: this.now(), url: this.config.feedUrl, data }), 'utf8');
    } catch {
      /* кэш — не главное */
    }
  }

  /** @returns {Promise<{items: object[], rotate: number, advertiseUrl: string|null}>} */
  async list() {
    const base = { items: [], rotate: this.config.rotateSeconds, advertiseUrl: this.config.advertiseUrl };
    if (!this.config.feedUrl) return base;

    const cached = this._readCache();
    const usable = cached && cached.url === this.config.feedUrl ? cached : null;
    if (usable && this.now() - usable.at < FRESH_MS) {
      const feed = sanitizeFeed(usable.data, this.now());
      return { ...base, items: feed.items, rotate: rotateOf(usable.data?.rotateSeconds, base.rotate) };
    }

    try {
      const data = await this.fetch(this.config.feedUrl, { timeout: 8000 });
      this._writeCache(data);
      const feed = sanitizeFeed(data, this.now());
      return { ...base, items: feed.items, rotate: rotateOf(data?.rotateSeconds, base.rotate) };
    } catch {
      // Нет сети — показываем прошлую ленту, даже устаревшую.
      if (!usable) return base;
      return { ...base, items: sanitizeFeed(usable.data, this.now()).items };
    }
  }
}

module.exports = { AdsClient, sanitizeFeed, loadAdsConfig, httpsUrl };
