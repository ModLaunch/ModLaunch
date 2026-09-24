'use strict';

const path = require('node:path');
const { JsonStore } = require('../store');
const { extractMedia, youtubeThumb } = require('./media');

/**
 * Картинки модов Hollow Knight.
 *
 * В самом каталоге ModLinks картинок нет — ни одной. Но у многих модов они
 * есть: скриншоты и ролики в README, страницы на Hollow Knight Wiki,
 * обложки репозиториев на GitHub. Всё это заранее собрано во встроенный
 * указатель (data/hk-media.json, его строит tools/build-hk-media.js),
 * поэтому карточки показывают фото сразу, без единого запроса в сеть.
 *
 * Моды, которые появились в каталоге позже указателя, программа
 * досматривает сама: читает их README и запоминает найденное на диске,
 * чтобы не читать второй раз.
 */

let INDEX = { known: [], mods: {} };
try {
  INDEX = require('../../data/hk-media.json');
} catch {
  /* указателя нет — все моды будут досмотрены по ходу дела */
}

const known = new Set(INDEX.known ?? []);
const CACHE_TTL = 14 * 24 * 60 * 60 * 1000;

let cache = null; // JsonStore: { mods: { [name]: { at, i, v } } }

/** Где хранить досмотренное. Вызывается один раз при запуске. */
function init(dataDir) {
  cache = new JsonStore(path.join(dataDir, 'hk-media-cache.json'), { mods: {} });
}

function entryOf(name) {
  const fromIndex = INDEX.mods?.[name];
  if (fromIndex) return { images: fromIndex.i ?? [], videos: fromIndex.v ?? [], wiki: fromIndex.w ?? null };
  const cached = cache?.data.mods[name];
  if (cached) return { images: cached.i ?? [], videos: cached.v ?? [], wiki: null };
  return null;
}

/**
 * Картинки мода по имени из ModLinks.
 * @returns {{images: string[], videos: string[], cover: string|null, wiki: string|null, pending: boolean}}
 */
function lookup(name) {
  const entry = entryOf(name);
  const images = entry?.images ?? [];
  const videos = entry?.videos ?? [];
  const cover = images[0] ?? (videos[0] ? youtubeThumb(videos[0]) : null);
  const checked = known.has(name) || Boolean(cache?.data.mods[name]);
  return { images, videos, cover, wiki: entry?.wiki ?? null, pending: !checked };
}

/** Что досматривать: мода нет в указателе, и в кэше он не свежий. */
function needsLookup(name) {
  if (INDEX.mods?.[name] || known.has(name)) return false;
  const cached = cache?.data.mods[name];
  return !cached || Date.now() - Date.parse(cached.at) > CACHE_TTL;
}

/**
 * Досматривает моды, которых нет в указателе: README → картинки и ролики.
 * @param {object[]} mods записи каталога ModLinks
 * @param {(mod: object) => Promise<object|null>} readReadme modlinks.readme
 * @returns {Promise<Record<string, ReturnType<typeof lookup>>>}
 */
async function resolve(mods, readReadme) {
  const todo = mods.filter((mod) => mod && needsLookup(mod.id)).slice(0, 24);
  const result = {};

  let next = 0;
  const worker = async () => {
    while (next < todo.length) {
      const mod = todo[next++];
      try {
        const readme = await readReadme(mod);
        const media = readme?.content ? extractMedia(readme.content, readme.base) : { images: [], videos: [] };
        if (cache) cache.data.mods[mod.id] = { at: new Date().toISOString(), i: media.images.slice(0, 12), v: media.videos.slice(0, 4) };
      } catch {
        // Нет сети или GitHub отказал — попробуем в другой раз, а пока обложка нарисованная.
        continue;
      }
      result[mod.id] = lookup(mod.id);
    }
  };
  await Promise.all([worker(), worker(), worker(), worker()]);

  if (cache && Object.keys(result).length) {
    try {
      cache.save();
    } catch {
      /* не записалось — досмотрим снова в следующий раз */
    }
  }
  return result;
}

/** Сколько модов с картинками в указателе — для README и тестов. */
function stats() {
  return { indexed: Object.keys(INDEX.mods ?? {}).length, known: known.size, generatedAt: INDEX.generatedAt ?? null };
}

module.exports = { init, lookup, resolve, needsLookup, stats };
