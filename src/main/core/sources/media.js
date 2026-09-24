'use strict';

/**
 * Картинки и ролики из описания мода.
 *
 * README пишут как попало: картинки в markdown и в HTML, относительные пути
 * от корня репозитория, ссылки на «blob»-страницы GitHub вместо самих
 * файлов, а вперемешку с кадрами — значки сборки, лицензии и Discord.
 * Здесь из этого вынимается то, что можно показать человеку как «фото мода»:
 * настоящие кадры и ролики с YouTube, в том порядке, в каком их поставил автор.
 *
 * Модуль без зависимостей и без сети — только разбор текста, поэтому один
 * и тот же код строит встроенный указатель картинок (tools/build-hk-media.js)
 * и досматривает новые моды прямо в программе.
 */

/** Значки-бейджи и прочая служебная графика — не иллюстрации. */
const BADGE =
  /shields\.io|badge|badgen\.net|img\.shields|discord(app)?\.com\/api|travis-ci|appveyor|codecov|ko-fi\.com\/img|buymeacoffee|repobeats|star-history|contrib\.rocks|komarev|hits\.|visitor|\/workflows\/|forthebadge|paypal|patreon|liberapay|tokei\.rs|sonarcloud|codeclimate|wakatime|deepsource|api\.star|readme-stats|github-readme|spotify-github|profile-counter|gitmoji|awesome\.re|circleci|coveralls|snyk\.io/i;

/** Ролик вместо картинки: такие файлы в ленту кадров не идут. */
const VIDEO_FILE = /\.(mp4|webm|mov|mkv|avi)(\?|#|$)/i;
/** Векторные значки из README почти всегда логотипы и значки, а не кадры. */
const VECTOR = /\.svg(\?|#|$)/i;

const MD_IMAGE = /!\[[^\]]*\]\(\s*<?([^)\s>]+)>?(?:\s+["'][^"']*["'])?\s*\)/g;
const REF_DEF = /^\s{0,3}\[([^\]]+)\]:\s*<?(\S+?)>?(?:\s+["'][^"']*["'])?\s*$/gm;
const REF_IMAGE = /!\[([^\]]*)\]\[([^\]]*)\]/g;
const HTML_IMAGE = /<img\b[^>]*?\bsrc\s*=\s*["']([^"']+)["']/gi;
const YOUTUBE = /(?:youtube\.com\/(?:watch\?(?:[^\s)"'<>]*&)?v=|embed\/|shorts\/|live\/)|youtu\.be\/|img\.youtube\.com\/vi\/|i\.ytimg\.com\/vi\/)([\w-]{11})/g;

/** Ссылку на страницу файла GitHub превращаем в прямую, иначе вместо картинки придёт HTML. */
function directUrl(url) {
  const blob = /^https?:\/\/github\.com\/([^/]+)\/([^/]+)\/(?:blob|raw)\/(?:refs\/heads\/)?(.+)$/i.exec(url);
  if (blob) return `https://raw.githubusercontent.com/${blob[1]}/${blob[2]}/${blob[3].replace(/\?raw=true$/i, '')}`;
  return url;
}

function absolute(value, base) {
  const raw = String(value ?? '').trim();
  if (!raw || raw.startsWith('data:') || raw.startsWith('#')) return null;
  try {
    const url = new URL(raw, base || undefined);
    if (url.protocol === 'http:') url.protocol = 'https:';
    if (url.protocol !== 'https:') return null;
    return directUrl(url.href);
  } catch {
    return null;
  }
}

/** Ролик YouTube по картинке-превью или ссылке — это тоже «кадр» мода. */
function youtubeIds(text) {
  const ids = [];
  for (const match of String(text ?? '').matchAll(YOUTUBE)) {
    if (!ids.includes(match[1])) ids.push(match[1]);
  }
  return ids;
}

/**
 * @param {string} text README (markdown или HTML)
 * @param {string} [base] адрес, от которого достраиваются относительные пути
 * @returns {{images: string[], videos: string[]}} images — адреса кадров, videos — id роликов YouTube
 */
function extractMedia(text, base) {
  const source = String(text ?? '');
  const found = [];

  const refs = new Map();
  for (const match of source.matchAll(REF_DEF)) refs.set(match[1].toLowerCase(), match[2]);

  for (const match of source.matchAll(MD_IMAGE)) found.push({ at: match.index, url: match[1] });
  for (const match of source.matchAll(REF_IMAGE)) {
    const key = (match[2] || match[1]).toLowerCase();
    if (refs.has(key)) found.push({ at: match.index, url: refs.get(key) });
  }
  for (const match of source.matchAll(HTML_IMAGE)) found.push({ at: match.index, url: match[1] });

  found.sort((a, b) => a.at - b.at);

  const images = [];
  for (const { url } of found) {
    const full = absolute(url, base);
    if (!full || BADGE.test(full) || VIDEO_FILE.test(full) || VECTOR.test(full)) continue;
    // Превью ролика с YouTube уже учтено как ролик — второй раз картинкой не нужно.
    if (/(?:img\.youtube\.com|i\.ytimg\.com)\/vi\//i.test(full)) continue;
    if (!images.includes(full)) images.push(full);
  }

  return { images, videos: youtubeIds(source) };
}

/** Превью ролика: 16:9 без чёрных полос — для обложки; крупное — для галереи. */
function youtubeThumb(id, size = 'cover') {
  return size === 'cover' ? `https://i.ytimg.com/vi/${id}/mqdefault.jpg` : `https://i.ytimg.com/vi/${id}/hqdefault.jpg`;
}

module.exports = { extractMedia, youtubeIds, youtubeThumb, absolute, directUrl, BADGE };
