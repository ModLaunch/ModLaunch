'use strict';

const { XMLParser } = require('fast-xml-parser');
const { fetchText, fetchJson } = require('../download');
const hkmedia = require('./hkmedia');

/**
 * Каталог ModLinks — открытый репозиторий сообщества Hollow Knight.
 *
 * По сути это то, чем должен быть каталог модов: XML с прямыми ссылками,
 * контрольными суммами SHA256 и явными зависимостями. На нём работает Scarab,
 * значит схема проверена практикой, а не только на бумаге.
 */

const MODLINKS_URL = 'https://raw.githubusercontent.com/hk-modding/modlinks/main/ModLinks.xml';
const APILINKS_URL = 'https://raw.githubusercontent.com/hk-modding/modlinks/main/ApiLinks.xml';

const TTL_MS = 15 * 60 * 1000;
let cache = null; // { at: number, mods: object[] }

const parser = new XMLParser({
  ignoreAttributes: false,
  attributeNamePrefix: '@',
  cdataPropName: '#cdata',
  trimValues: true,
});

/** Значение узла, который может быть строкой, CDATA или объектом с атрибутами. */
function textOf(node) {
  if (node === undefined || node === null) return '';
  if (typeof node === 'string' || typeof node === 'number') return String(node).trim();
  if (typeof node === 'object') {
    if ('#cdata' in node) return String(node['#cdata']).trim();
    if ('#text' in node) return String(node['#text']).trim();
  }
  return '';
}

/** Приводит «либо один элемент, либо массив» к массиву. */
function asArray(value) {
  if (value === undefined || value === null) return [];
  return Array.isArray(value) ? value : [value];
}

/** Выбирает ссылку под текущую платформу: бывает общий <Link>, бывает <Links> по ОС. */
function pickLink(manifest) {
  if (manifest.Link !== undefined) {
    return { url: textOf(manifest.Link), sha256: manifest.Link?.['@SHA256'] ?? null };
  }
  const links = manifest.Links;
  if (!links) return { url: '', sha256: null };

  const key = process.platform === 'win32' ? 'Windows' : process.platform === 'darwin' ? 'Mac' : 'Linux';
  const node = links[key] ?? links.Windows ?? links.Linux ?? links.Mac;
  return { url: textOf(node), sha256: node?.['@SHA256'] ?? null };
}

/**
 * @param {{force?: boolean}} [options]
 * @returns {Promise<object[]>}
 */
async function loadMods(options = {}) {
  if (!options.force && cache && Date.now() - cache.at < TTL_MS) return cache.mods;

  const xml = await fetchText(MODLINKS_URL, { timeout: 30000 });
  const parsed = parser.parse(xml);
  const manifests = asArray(parsed?.ModLinks?.Manifest);

  const mods = manifests
    .map((manifest) => {
      const { url, sha256 } = pickLink(manifest);
      const name = textOf(manifest.Name);
      if (!name || !url) return null;

      const repository = textOf(manifest.Repository) || null;
      const description = textOf(manifest.Description);
      const tags = asArray(manifest.Tags?.Tag).map(textOf).filter(Boolean);
      // Картинок в ModLinks нет, но у мода они часто есть в других местах:
      // README, вики, обложка репозитория. Их знает указатель hkmedia.
      const media = hkmedia.lookup(name);
      return {
        source: 'modlinks',
        id: name, // в ModLinks имя и есть идентификатор
        name: textOf(manifest.DisplayName) || name,
        // Авторы указаны не у всех; тогда автор — владелец репозитория.
        author: asArray(manifest.Authors?.Author).map(textOf).filter(Boolean).join(', ') || githubRepo(repository)?.owner || '',
        version: textOf(manifest.Version),
        description,
        icon: media.cover,
        picture: media.cover,
        media: { images: media.images, videos: media.videos, wiki: media.wiki },
        mediaPending: media.pending,
        kind: guessKind(tags, `${name} ${description}`),
        url: repository,
        readmeUrl: textOf(manifest.ReadMe) || null,
        downloadUrl: url,
        sha256,
        fileSize: null,
        downloads: 0,
        rating: 0,
        updatedAt: null,
        categories: tags,
        dependencies: asArray(manifest.Dependencies?.Dependency).map(textOf).filter(Boolean),
        integrations: asArray(manifest.Integrations?.Integration).map(textOf).filter(Boolean),
      };
    })
    .filter(Boolean);

  cache = { at: Date.now(), mods };
  return mods;
}

/** Сведения о загрузчике Hollow Knight (Modding API) и списке его файлов. */
async function loadApi() {
  const xml = await fetchText(APILINKS_URL, { timeout: 30000 });
  const parsed = parser.parse(xml);
  const manifest = parsed?.ApiLinks?.Manifest;
  if (!manifest) throw new Error('Не удалось прочитать ApiLinks.xml');

  const key = process.platform === 'win32' ? 'Windows' : process.platform === 'darwin' ? 'Mac' : 'Linux';
  const node = manifest.Links?.[key];

  return {
    version: textOf(manifest.Version),
    downloadUrl: textOf(node),
    sha256: node?.['@SHA256'] ?? null,
    files: asArray(manifest.Files?.File).map(textOf).filter(Boolean),
  };
}

/**
 * В ModLinks нет счётчика скачиваний, поэтому «популярное» — это известные
 * всем моды сообщества в начале списка, дальше остальные по алфавиту.
 * Иначе витрина открывалась бы модами на букву «A».
 */
const POPULAR = [
  'Benchwarp', 'Custom Knight', 'Randomizer 4', 'QoL', 'HKMP', 'DebugMod', 'Pale Court',
  'Transcendence', 'Toggleable Bindings', 'Lightbringer', 'Fyrenest', 'MapChanger',
  'RandoMapMod', 'Charm Changer', 'GodSeekerPlus', 'AdditionalMaps', 'Mantis Gods',
  'HKTimer', 'RandoPlus', 'Satchel', 'ItemChanger',
];
const popularRank = new Map(POPULAR.map((name, index) => [name.toLowerCase(), index]));

/**
 * Порядок витрины. Первая страница должна и показать главное, и не быть
 * рядом одинаковых рисованных обложек, поэтому ярусы:
 *
 *   0 — известные моды с настоящими картинками;
 *   1 — самые известные без картинок (первые восемь из списка);
 *   2 — остальные моды с картинками;
 *   3 — остальные известные без картинок;
 *   4 — всё прочее по алфавиту.
 */
const TOP = 8;

function tierOf(mod, rank) {
  const photo = Boolean(mod.icon);
  if (rank !== Infinity) return photo ? 0 : rank < TOP ? 1 : 3;
  return photo ? 2 : 4;
}

function byPopularity(a, b) {
  const ra = popularRank.get(a.id.toLowerCase()) ?? Infinity;
  const rb = popularRank.get(b.id.toLowerCase()) ?? Infinity;
  return tierOf(a, ra) - tierOf(b, rb) || ra - rb || a.name.localeCompare(b.name, 'ru');
}

async function search(query, options = {}) {
  const mods = await loadMods();
  const needle = String(query ?? '').trim().toLowerCase();

  let result = mods;
  if (options.category) result = result.filter((m) => m.categories.includes(options.category));

  if (needle) {
    result = result
      .filter(
        (m) =>
          m.name.toLowerCase().includes(needle) ||
          m.author.toLowerCase().includes(needle) ||
          m.description.toLowerCase().includes(needle)
      )
      .sort((a, b) => {
        const an = a.name.toLowerCase().startsWith(needle) ? 0 : 1;
        const bn = b.name.toLowerCase().startsWith(needle) ? 0 : 1;
        return an - bn || byPopularity(a, b);
      });
  } else {
    result = [...result].sort(byPopularity);
  }

  return options.limit ? result.slice(0, options.limit) : result;
}

/**
 * Тема мода для рисованной обложки — для тех модов, у которых настоящих
 * картинок не нашлось нигде: ни в README, ни на вики, ни на GitHub.
 * Тема — тег из ModLinks, а у модов без тегов — по словам из названия
 * и описания.
 */
const KIND_BY_TAG = {
  Gameplay: 'gameplay', Boss: 'boss', Utility: 'utility', Cosmetic: 'cosmetic', Library: 'library',
  Expansion: 'expansion', Charm: 'charm', Joke: 'joke', Accessibility: 'accessibility', Optimization: 'utility',
};
const KIND_WORDS = [
  ['library', /\b(librar(y|ies)|api|frameworks?|dependency|helpers?|core|lib)\b/i],
  ['boss', /\b(boss(es)?|fights?|arenas?|pantheons?|godhome|hall of gods)\b/i],
  ['cosmetic', /\b(skins?|textures?|sprites?|cosmetics?|visuals?|colou?rs?|hud|shaders?|retextures?)\b/i],
  ['charm', /\bcharms?\b/i],
  ['expansion', /\b(new areas?|expansions?|new zones?|new worlds?|new maps?|dlc)\b/i],
  ['utility', /\b(tools?|utilit(y|ies)|timers?|debug|menus?|displays?|trackers?|saves?|speedruns?|practice|config|keybinds?|hotkeys?|logs?|warp\w*|teleport\w*|quality of life|qol)\b/i],
  ['joke', /\b(jokes?|memes?|funny|silly)\b/i],
];

function guessKind(tags, text) {
  for (const tag of tags) if (KIND_BY_TAG[tag]) return KIND_BY_TAG[tag];
  for (const [kind, pattern] of KIND_WORDS) if (pattern.test(text)) return kind;
  return 'gameplay';
}

/**
 * Загрузки мода: Scarab и Lumafly качают моды по ссылкам из ModLinks,
 * то есть с релизов GitHub, — счётчик скачиваний релизов и есть
 * популярность мода. Без ключа GitHub отвечает 60 раз в час на адрес,
 * поэтому ответы запоминаем, а при отказе просто не показываем цифру.
 */
const statsCache = new Map();
const STATS_TTL_MS = 60 * 60 * 1000;

async function stats(mod) {
  const repo = githubRepo(mod?.url);
  if (!repo) return null;
  const key = `${repo.owner}/${repo.repo}`.toLowerCase();
  const hit = statsCache.get(key);
  if (hit && Date.now() - hit.at < STATS_TTL_MS) return hit.value;

  let value = null;
  try {
    const releases = await fetchJson(`https://api.github.com/repos/${repo.owner}/${repo.repo}/releases?per_page=100`, {
      timeout: 15000,
      headers: { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' },
    });
    if (Array.isArray(releases)) {
      let downloads = 0;
      let updatedAt = null;
      for (const release of releases) {
        for (const asset of release?.assets ?? []) downloads += Number(asset?.download_count) || 0;
        const date = release?.published_at || release?.created_at;
        if (date && (!updatedAt || date > updatedAt)) updatedAt = date;
      }
      value = { downloads, updatedAt };
    }
  } catch {
    value = null; // лимит GitHub или нет сети — цифр просто не будет
  }
  statsCache.set(key, { at: Date.now(), value });
  return value;
}

/**
 * Описание мода — README из его репозитория на GitHub.
 * Картинки и ссылки в README часто относительные («docs/shot.png»),
 * поэтому вместе с текстом отдаём адреса, от которых их достраивать.
 */
const readmeCache = new Map();

function githubRepo(url) {
  const match = /^https?:\/\/github\.com\/([^/]+)\/([^/#?]+)/i.exec(String(url ?? ''));
  return match ? { owner: match[1], repo: match[2].replace(/\.git$/i, '') } : null;
}

/** Ссылку вида github.com/…/raw/… или …/blob/… превращаем в прямую на raw.githubusercontent.com. */
function rawUrl(url) {
  const match = /^https?:\/\/github\.com\/([^/]+)\/([^/]+)\/(?:raw|blob)\/(?:refs\/heads\/)?(.+)$/i.exec(String(url ?? ''));
  return match ? `https://raw.githubusercontent.com/${match[1]}/${match[2]}/${match[3]}` : String(url ?? '');
}

async function readme(mod) {
  const repo = githubRepo(mod?.url);
  if (!repo) return null;
  const key = `${repo.owner}/${repo.repo}`;
  if (readmeCache.has(key)) return readmeCache.get(key);

  let result = null;
  // Некоторые авторы прямо указывают свой README в ModLinks — он главнее.
  if (mod.readmeUrl) {
    const direct = rawUrl(mod.readmeUrl);
    try {
      const text = await fetchText(direct, { timeout: 15000 });
      const base = direct.slice(0, direct.lastIndexOf('/') + 1);
      result = { format: 'markdown', content: text, base, linkBase: `https://github.com/${key}/blob/HEAD/` };
    } catch {
      result = null; // не вышло — ищем README в корне, как у всех
    }
  }
  for (const name of result ? [] : ['README.md', 'readme.md', 'Readme.md', 'README.MD', 'README']) {
    try {
      const text = await fetchText(`https://raw.githubusercontent.com/${key}/HEAD/${name}`, { timeout: 15000 });
      result = {
        format: 'markdown',
        content: text,
        base: `https://raw.githubusercontent.com/${key}/HEAD/`,
        linkBase: `https://github.com/${key}/blob/HEAD/`,
      };
      break;
    } catch (error) {
      if (!/^404\b/.test(String(error.message))) throw error;
    }
  }
  readmeCache.set(key, result);
  return result;
}

/**
 * Досматривает картинки модов, которых нет во встроенном указателе,
 * и обновляет их записи в каталоге: следующая отрисовка уже с фото.
 * @param {string[]} ids имена модов
 */
async function media(ids) {
  const mods = await loadMods();
  const wanted = new Set((ids ?? []).map(String));
  const targets = mods.filter((mod) => wanted.has(mod.id) && mod.mediaPending);
  const found = await hkmedia.resolve(targets, readme);
  const out = {};
  for (const mod of targets) {
    const entry = found[mod.id];
    if (!entry) continue;
    mod.icon = entry.cover;
    mod.picture = entry.cover;
    mod.media = { images: entry.images, videos: entry.videos, wiki: entry.wiki };
    mod.mediaPending = false;
    out[mod.id] = { icon: mod.icon, picture: mod.picture, media: mod.media };
  }
  return out;
}

async function categories() {
  const mods = await loadMods();
  const counts = new Map();
  for (const mod of mods) {
    for (const tag of mod.categories) counts.set(tag, (counts.get(tag) ?? 0) + 1);
  }
  return [...counts.entries()].sort((a, b) => b[1] - a[1]).map(([name, count]) => ({ name, count }));
}

async function getById(id) {
  const mods = await loadMods();
  return mods.find((m) => m.id === id) ?? null;
}

/** Разворачивает зависимости в порядок установки (зависимости первыми). */
async function resolveDependencies(rootId) {
  const mods = await loadMods();
  const byId = new Map(mods.map((m) => [m.id, m]));

  const order = [];
  const seen = new Set();
  const missing = [];

  function visit(id, trail) {
    if (seen.has(id) || trail.includes(id)) return;
    const mod = byId.get(id);
    if (!mod) {
      missing.push(id);
      return;
    }
    for (const dep of mod.dependencies) visit(dep, [...trail, id]);
    seen.add(id);
    order.push(mod);
  }

  visit(rootId, []);
  return { order, missing };
}

module.exports = {
  readme,
  media,
  stats,
  guessKind,
  rawUrl,
  githubRepo,
  loadMods,
  loadApi,
  search,
  categories,
  getById,
  resolveDependencies,
};
