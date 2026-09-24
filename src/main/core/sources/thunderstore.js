'use strict';

/**
 * Каталог Thunderstore (Lethal Company).
 *
 * До 1.6 ModHub качал весь список пакетов сообщества (/api/v1/package/).
 * Для Lethal Company это 35 МБ в сжатом виде и сотни мегабайт после
 * распаковки: окно подвисало, запрос не укладывался во время ожидания,
 * и каталог оставался пустым. Теперь — API нового сайта Thunderstore
 * (cyberstorm): по 20 пакетов на страницу, сразу отсортированные, с поиском
 * на стороне сервера. Подробности пакета, зависимости и описание берутся
 * отдельными маленькими запросами, только когда человек их открыл.
 */

const { fetchJson } = require('../download');

const BASE = 'https://thunderstore.io';
const DETAIL_TTL = 15 * 60 * 1000;
const PARALLEL = 6;

const ORDERINGS = {
  popular: 'most-downloaded',
  rating: 'top-rated',
  new: 'newest',
  updated: 'last-updated',
};

const detailCache = new Map(); // "community/Namespace-Name" -> { at, mod }

/** "Namespace-Name" -> { namespace, name }. Дефис в именах Thunderstore запрещён. */
function splitId(id) {
  const text = String(id ?? '');
  const dash = text.indexOf('-');
  if (dash <= 0 || dash === text.length - 1) return null;
  return { namespace: text.slice(0, dash), name: text.slice(dash + 1) };
}

function isNotFound(error) {
  return /^404\b/.test(String(error?.message ?? ''));
}

/** Ограничитель параллельных запросов: дерево зависимостей сборки бывает на сотню пакетов. */
let active = 0;
const queue = [];
async function limited(task) {
  if (active >= PARALLEL) await new Promise((resolve) => queue.push(resolve));
  active += 1;
  try {
    return await task();
  } finally {
    active -= 1;
    queue.shift()?.();
  }
}

/** Приводит пакет Thunderstore к внутреннему виду ModHub. */
function toMod(item, community) {
  const communityId = item.community_identifier ?? community;
  return {
    source: 'thunderstore',
    id: `${item.namespace}-${item.name}`,
    name: String(item.name ?? '').replace(/_/g, ' '),
    author: item.namespace ?? '',
    version: item.latest_version_number ?? '',
    description: item.description ?? '',
    icon: item.icon_url ?? null,
    // Значки Thunderstore всегда квадратные 256×256: в карточке их ставим
    // плиткой на размытом фоне, а не растягиваем на всю ширину.
    iconShape: 'square',
    url: `${BASE}/c/${communityId}/p/${item.namespace}/${item.name}/`,
    downloads: Number(item.download_count ?? 0),
    rating: Number(item.rating_count ?? 0),
    updatedAt: item.last_updated ?? item.version_created ?? null,
    categories: (item.categories ?? []).map((c) => c?.name).filter(Boolean),
    pinned: Boolean(item.is_pinned),
    dependencies: [],
  };
}

/**
 * Страница каталога.
 * @returns {Promise<{mods: object[], total: number, hasMore: boolean, page: number}>}
 */
async function search(community, query = '', options = {}) {
  const page = Math.max(1, Number(options.page) || 1);
  const params = new URLSearchParams({ page: String(page), ordering: ORDERINGS[options.sort] ?? ORDERINGS.popular });
  const needle = String(query ?? '').trim();
  if (needle) params.set('q', needle);

  const data = await fetchJson(`${BASE}/api/cyberstorm/listing/${encodeURIComponent(community)}/?${params}`, {
    timeout: 20000,
  });
  const mods = (data?.results ?? [])
    .filter((item) => item && !item.is_nsfw && !item.is_deprecated)
    .map((item) => toMod(item, community));
  return { mods, total: Number(data?.count ?? mods.length), hasMore: Boolean(data?.next), page };
}

/** Полная карточка пакета: версия, ссылка на архив, зависимости. */
async function getById(community, id) {
  const parts = splitId(id);
  if (!parts) return null;

  const key = `${community}/${id}`;
  const cached = detailCache.get(key);
  if (cached && Date.now() - cached.at < DETAIL_TTL) return cached.mod;

  let data;
  try {
    data = await limited(() =>
      fetchJson(
        `${BASE}/api/cyberstorm/listing/${encodeURIComponent(community)}/${encodeURIComponent(parts.namespace)}/${encodeURIComponent(parts.name)}/`,
        { timeout: 20000 }
      )
    );
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }

  const deps = (data?.dependencies ?? []).filter((d) => d?.namespace && d?.name);
  const version = data?.latest_version_number ?? '';
  const mod = {
    ...toMod(data, community),
    version,
    downloadUrl: data?.download_url || `${BASE}/package/download/${parts.namespace}/${parts.name}/${version}/`,
    website: data?.website_url || null,
    fileSize: Number(data?.size ?? 0) || null,
    dependencies: deps.filter((d) => !d.is_removed).map((d) => `${d.namespace}-${d.name}`),
    requirements: deps.map((d) => ({
      id: `${d.namespace}-${d.name}`,
      name: String(d.name).replace(/_/g, ' '),
      icon: d.icon_url ?? null,
      available: !d.is_removed && !d.is_unavailable,
    })),
  };
  detailCache.set(key, { at: Date.now(), mod });
  return mod;
}

/** Описание пакета — README, уже переведённый сайтом в HTML. */
async function readme(id) {
  const parts = splitId(id);
  if (!parts) return null;
  try {
    const data = await fetchJson(
      `${BASE}/api/cyberstorm/package/${encodeURIComponent(parts.namespace)}/${encodeURIComponent(parts.name)}/latest/readme/`,
      { timeout: 20000 }
    );
    return data?.html ? { format: 'html', content: data.html, base: `${BASE}/` } : null;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

/**
 * Разворачивает дерево зависимостей в плоский список для установки.
 *
 * Именно этот шаг убирает главный источник сломанных сборок: автор упомянул
 * нужную библиотеку одной строкой в описании, человек её не заметил, игра
 * не запустилась. Здесь зависимости машиночитаемые, так что гадать не нужно.
 *
 * @returns {Promise<{order: object[], missing: string[]}>} зависимости раньше зависимых
 */
async function resolveDependencies(community, rootId) {
  const order = [];
  const missing = [];
  const tasks = new Map();

  function visit(id, trail) {
    if (trail.includes(id)) return Promise.resolve(); // цикл — просто не зацикливаемся
    if (tasks.has(id)) return tasks.get(id);
    const task = (async () => {
      const mod = await getById(community, id).catch(() => null);
      if (!mod) {
        missing.push(id);
        return;
      }
      await Promise.all(mod.dependencies.map((dep) => visit(dep, [...trail, id])));
      order.push(mod);
    })();
    tasks.set(id, task);
    return task;
  }

  await visit(rootId, []);
  return { order, missing };
}

async function categories() {
  return [];
}

module.exports = {
  search,
  getById,
  readme,
  categories,
  resolveDependencies,
  splitId,
  toMod,
};
