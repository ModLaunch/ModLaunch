'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { fetchJson } = require('../download');

/**
 * Настоящие картинки игр — из магазина Steam.
 *
 * Внутрь программы они не кладутся: это официальные арты и скриншоты
 * разработчиков, и права на них у них. ModHub, как Steam, GOG Galaxy или
 * Playnite, показывает их прямо с серверов Steam — по номеру игры в магазине
 * (steamAppId в описании игры). Нет интернета — остаются рисованные
 * картинки ModHub, интерфейс от этого не ломается.
 *
 * Список картинок берётся из открытого API магазина (appdetails) и хранится
 * на диске неделю: при каждом запуске в магазин не ходим.
 *
 * У части игр Steam хранит арт в папке с хэшем (у Hollow Knight — так),
 * поэтому на широкий арт и логотип отдаётся несколько адресов по очереди:
 * интерфейс берёт первый, который загрузился, а если ни один — скриншот.
 */

const CDN = 'https://shared.akamai.steamstatic.com/store_item_assets/steam/apps';
const OLD_CDN = 'https://cdn.akamai.steamstatic.com/steam/apps';
const API = 'https://store.steampowered.com/api/appdetails';
const FRESH_MS = 7 * 24 * 60 * 60 * 1000;
const MAX_SHOTS = 8;

function httpsUrl(value) {
  if (!value || typeof value !== 'string') return null;
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:') return null;
    // Только серверы Steam: из ответа магазина больше ничего не берём.
    if (!/(^|\.)steamstatic\.com$|(^|\.)steampowered\.com$|(^|\.)akamaihd\.net$/.test(url.hostname)) return null;
    return url.toString();
  } catch {
    return null;
  }
}

/** Папка, где лежит шапка игры: у новых игр — с хэшем внутри. */
function folderOf(url) {
  const clean = String(url ?? '').split('?')[0];
  const slash = clean.lastIndexOf('/');
  return slash > 0 ? clean.slice(0, slash) : null;
}

/**
 * Адреса без запроса к магазину — по известной схеме Steam.
 * Годятся как запасной вариант, когда магазин недоступен.
 */
function staticMedia(appId) {
  const id = Number(appId);
  if (!Number.isInteger(id) || id <= 0) return null;
  return {
    appId: id,
    hero: [`${CDN}/${id}/library_hero.jpg`, `${OLD_CDN}/${id}/library_hero.jpg`],
    logo: [`${CDN}/${id}/logo.png`, `${OLD_CDN}/${id}/logo.png`],
    cover: [`${CDN}/${id}/library_600x900.jpg`, `${OLD_CDN}/${id}/library_600x900.jpg`],
    header: [`${CDN}/${id}/header.jpg`, `${OLD_CDN}/${id}/header.jpg`],
    shots: [],
    name: null,
    source: 'static',
  };
}

/**
 * Разбирает ответ appdetails в список картинок.
 * @param {number} appId
 * @param {object} json — ответ магазина целиком
 */
function mediaFromAppDetails(appId, json) {
  const base = staticMedia(appId);
  if (!base) return null;
  const data = json?.[String(appId)]?.success ? json[String(appId)].data : null;
  if (!data) return base;

  const header = httpsUrl(data.header_image);
  const folder = header ? folderOf(header) : null;
  const plainFolder = `${CDN}/${base.appId}`;
  const hashed = folder && folder !== plainFolder ? folder : null;

  const shots = (Array.isArray(data.screenshots) ? data.screenshots : [])
    .map((shot) => ({ full: httpsUrl(shot?.path_full), thumb: httpsUrl(shot?.path_thumbnail) }))
    .filter((shot) => shot.full)
    .slice(0, MAX_SHOTS)
    .map((shot) => ({ full: shot.full, thumb: shot.thumb ?? shot.full }));

  const withHashed = (file, list) => (hashed ? [`${hashed}/${file}`, ...list] : list);

  return {
    appId: base.appId,
    hero: withHashed('library_hero.jpg', base.hero),
    logo: withHashed('logo.png', base.logo),
    cover: withHashed('library_600x900.jpg', base.cover),
    header: [header, ...base.header].filter(Boolean),
    shots,
    name: typeof data.name === 'string' ? data.name.slice(0, 80) : null,
    source: 'store',
  };
}

class SteamMedia {
  /**
   * @param {{cacheFile: string, fetch?: Function, now?: () => number, lang?: () => string}} options
   */
  constructor({ cacheFile, fetch = fetchJson, now = () => Date.now(), lang = () => 'russian' }) {
    this.cacheFile = cacheFile;
    this.fetch = fetch;
    this.now = now;
    this.lang = lang;
    this.cache = this._read();
    this.pending = new Map();
  }

  _read() {
    try {
      const data = JSON.parse(fs.readFileSync(this.cacheFile, 'utf8'));
      return data && typeof data === 'object' && data.apps ? data : { apps: {} };
    } catch {
      return { apps: {} };
    }
  }

  _write() {
    try {
      fs.mkdirSync(path.dirname(this.cacheFile), { recursive: true });
      const tmp = this.cacheFile + '.tmp';
      fs.writeFileSync(tmp, JSON.stringify(this.cache), 'utf8');
      fs.renameSync(tmp, this.cacheFile);
    } catch {
      /* кэш — не главное */
    }
  }

  /** Забыть сохранённое (кнопка «Очистить кэш картинок» в настройках). */
  clear() {
    this.cache = { apps: {} };
    try {
      fs.rmSync(this.cacheFile, { force: true });
    } catch {
      /* нечего удалять */
    }
  }

  /**
   * Картинки одной игры.
   * @param {number} appId
   */
  async forApp(appId) {
    const id = Number(appId);
    const base = staticMedia(id);
    if (!base) return null;

    const cached = this.cache.apps[id];
    if (cached && this.now() - cached.at < FRESH_MS) return mediaFromAppDetails(id, cached.data);

    if (this.pending.has(id)) return this.pending.get(id);
    const task = (async () => {
      try {
        const url = `${API}?appids=${id}&filters=basic,screenshots&l=${encodeURIComponent(this.lang())}`;
        const data = await this.fetch(url, { timeout: 12000 });
        if (!data?.[String(id)]?.success) throw new Error('no data');
        // Храним только нужное: ответ магазина большой.
        const item = data[String(id)].data;
        const slim = {
          [String(id)]: {
            success: true,
            data: {
              name: item.name,
              header_image: item.header_image,
              screenshots: (item.screenshots ?? []).slice(0, MAX_SHOTS).map((s) => ({
                path_full: s.path_full,
                path_thumbnail: s.path_thumbnail,
              })),
            },
          },
        };
        this.cache.apps[id] = { at: this.now(), data: slim };
        this._write();
        return mediaFromAppDetails(id, slim);
      } catch {
        // Магазин недоступен: прошлый ответ, даже устаревший, лучше схемы по умолчанию.
        return cached ? mediaFromAppDetails(id, cached.data) : base;
      } finally {
        this.pending.delete(id);
      }
    })();
    this.pending.set(id, task);
    return task;
  }

  /**
   * Картинки нескольких игр сразу.
   * @param {Array<{id: string, steamAppId?: number}>} games
   * @returns {Promise<Record<string, object>>}
   */
  async forGames(games) {
    const out = {};
    await Promise.all(
      (games ?? []).map(async (game) => {
        if (!game?.steamAppId) return;
        const media = await this.forApp(game.steamAppId);
        if (media) out[game.id] = media;
      })
    );
    return out;
  }
}

module.exports = { SteamMedia, mediaFromAppDetails, staticMedia };
