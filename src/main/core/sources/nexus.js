'use strict';

/**
 * Nexus Mods — каталог Stardew Valley.
 *
 * Что изменилось в 1.6. Раньше витрина шла через API v1, которому нужен
 * личный ключ, а любая ошибка тихо превращалась в пустой список — человек
 * вводил ключ и всё равно видел «ничего нет». Теперь просмотр, поиск,
 * описание, требования и список файлов идут через открытый GraphQL API v2:
 * ему ключ не нужен вовсе. Ошибки больше не глотаются, а показываются.
 *
 * Ключ остаётся нужен только для скачивания без браузера — это правило
 * Nexus: прямую ссылку на файл API выдаёт лишь премиум-аккаунтам или по
 * ссылке nxm://, которую сайт создаёт после нажатия кнопки у себя.
 */

const { fetchJson } = require('../download');
const { t } = require('../../i18n');

let VERSION = '1.9.7';
try {
  VERSION = require('../../../../package.json').version || VERSION;
} catch {
  /* версия — только для заголовка запроса */
}

const API = 'https://api.nexusmods.com/v1';
const V2 = 'https://api.nexusmods.com/v2/graphql';
const PAGE = 24;
const APP_HEADERS = { 'Application-Name': 'ModHub', 'Application-Version': VERSION };

/* ------------------------------------------------------------------ *
 *  nxm:// и API v1 (скачивание)
 * ------------------------------------------------------------------ */

/**
 * Разбирает ссылку, которую Nexus открывает кнопкой «Mod Manager Download»:
 * nxm://stardewvalley/mods/1915/files/12345?key=...&expires=...&user_id=...
 */
function parseNxmLink(link) {
  let url;
  try {
    url = new URL(link);
  } catch {
    return null;
  }
  if (url.protocol !== 'nxm:') return null;

  const segments = url.pathname.split('/').filter(Boolean);
  const modsIndex = segments.indexOf('mods');
  const filesIndex = segments.indexOf('files');
  if (modsIndex === -1 || filesIndex === -1) return null;

  return {
    game: url.hostname,
    modId: segments[modsIndex + 1],
    fileId: segments[filesIndex + 1],
    key: url.searchParams.get('key'),
    expires: url.searchParams.get('expires'),
    userId: url.searchParams.get('user_id'),
  };
}

function keyError() {
  const error = new Error(t('err.nexus.noKey'));
  error.code = 'NEXUS_KEY';
  error.alreadyExplained = true;
  return error;
}

function headers(apiKey) {
  if (!apiKey) throw keyError();
  return { apikey: apiKey, Accept: 'application/json', ...APP_HEADERS };
}

async function validateKey(apiKey) {
  const data = await fetchJson(`${API}/users/validate.json`, { headers: headers(apiKey) });
  return { name: data.name, premium: Boolean(data.is_premium), email: data.email ?? null };
}

async function getDownloadLinks(nxm, apiKey) {
  const base = `${API}/games/${nxm.game}/mods/${nxm.modId}/files/${nxm.fileId}/download_link.json`;
  const query = new URLSearchParams();
  if (nxm.key) query.set('key', nxm.key);
  if (nxm.expires) query.set('expires', nxm.expires);
  const url = query.toString() ? `${base}?${query}` : base;

  const data = await fetchJson(url, { headers: headers(apiKey) });
  if (!Array.isArray(data) || data.length === 0) throw new Error(t('err.nexus.noLink'));
  return data.map((m) => ({ url: m.URI, name: m.short_name ?? m.name ?? 'CDN' }));
}

/** Сведения о моде и файле для установки по ссылке nxm://. */
async function getModInfo(nxm, apiKey) {
  const [mod, file] = await Promise.all([
    fetchJson(`${API}/games/${nxm.game}/mods/${nxm.modId}.json`, { headers: headers(apiKey) }),
    fetchJson(`${API}/games/${nxm.game}/mods/${nxm.modId}/files/${nxm.fileId}.json`, {
      headers: headers(apiKey),
    }).catch(() => null),
  ]);

  return {
    source: 'nexus',
    id: `nexus:${nxm.game}:${nxm.modId}`,
    name: plainText(mod.name) || `#${nxm.modId}`,
    author: mod.author ?? mod.uploaded_by ?? '',
    version: file?.version ?? mod.version ?? '',
    description: plainText(mod.summary),
    icon: mod.picture_url ?? null,
    url: `https://www.nexusmods.com/${nxm.game}/mods/${nxm.modId}`,
    fileName: file?.file_name ?? null,
    fileSize: file?.size_in_bytes ?? null,
  };
}

/** Страница файла на сайте. С nmm=1 сайт сразу предлагает «Mod Manager Download». */
function filePageUrl(domain, modId, fileId, { nmm = false } = {}) {
  return `https://www.nexusmods.com/${domain}/mods/${modId}?tab=files&file_id=${fileId}${nmm ? '&nmm=1' : ''}`;
}

/* ------------------------------------------------------------------ *
 *  GraphQL v2 — витрина, поиск, описание, файлы. Без ключа.
 * ------------------------------------------------------------------ */

async function graphql(query, variables = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 20000);
  let response;
  try {
    response = await fetch(V2, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...APP_HEADERS },
      body: JSON.stringify({ query, variables }),
      signal: controller.signal,
    });
  } catch (error) {
    throw new Error(t('err.nexus.offline', { reason: error.name === 'AbortError' ? 'timeout' : error.message }));
  } finally {
    clearTimeout(timer);
  }
  if (!response.ok) throw new Error(t('err.nexus.http', { status: response.status }));
  const json = await response.json();
  if (!json?.data) throw new Error(t('err.nexus.http', { status: json?.errors?.[0]?.message ?? '?' }));
  return json.data;
}

function plainText(value) {
  return String(value ?? '')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&quot;/g, '"')
    .replace(/&#0?39;/g, "'")
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/\[[^\]]+\]/g, '')
    .trim();
}

const MOD_FIELDS =
  'modId name summary author version pictureUrl thumbnailUrl thumbnailLargeUrl downloads endorsements adultContent updatedAt modCategory { name } uploader { name }';

function fromNode(node, domain) {
  const id = node?.modId;
  if (!id) return null;
  return {
    source: 'nexus',
    id: String(id),
    name: plainText(node.name) || `#${id}`,
    author: node.author || node.uploader?.name || '',
    version: node.version ?? '',
    description: plainText(node.summary),
    icon: node.thumbnailLargeUrl || node.pictureUrl || node.thumbnailUrl || null,
    picture: node.pictureUrl || null,
    url: `https://www.nexusmods.com/${domain}/mods/${id}`,
    downloads: Number(node.downloads ?? 0),
    rating: Number(node.endorsements ?? 0),
    updatedAt: node.updatedAt ?? null,
    categories: node.modCategory?.name ? [node.modCategory.name] : [],
    dependencies: [],
    adult: Boolean(node.adultContent),
  };
}

const SORTS = {
  popular: { downloads: { direction: 'DESC' } },
  rating: { endorsements: { direction: 'DESC' } },
  updated: { updatedAt: { direction: 'DESC' } },
};

/**
 * Страница витрины или поиска.
 *
 * categories (2.0) — шаблоны имени категории для раздела каталога
 * («*Buildable*»). Названия категорий у игр на Nexus разные, поэтому
 * шаблоны пробуются по очереди, пока один не найдёт моды; не нашёл ни один
 * (или сервер не понял фильтр) — ищем по словам из названия (keywords).
 *
 * @param {{page?: number, sort?: string, query?: string, hide?: string[],
 *          categories?: string[], keywords?: string[]}} options
 */
const sectionWinners = new Map();

async function browse(domain, options = {}) {
  const categories = (options.categories ?? []).filter(Boolean).slice(0, 6);
  const keywords = (options.keywords ?? []).filter(Boolean).slice(0, 6);
  if (!categories.length && !keywords.length) return browsePlain(domain, options);

  const attempts = [
    ...categories.map((value) => ({ categoryName: [{ value, op: 'WILDCARD' }] })),
    ...keywords.map((value) => ({ name: [{ value: `*${value}*`, op: 'WILDCARD' }] })),
  ];
  // Сработавший фильтр запоминаем: «Ещё» и повторные заходы в раздел
  // не перебирают заново шаблоны, которые у этой игры ничего не находят.
  const key = `${domain}|${categories.join(',')}|${keywords.join(',')}`;
  const known = sectionWinners.get(key);
  if (known !== undefined) attempts.unshift(attempts.splice(known, 1)[0]);
  let last = null;
  for (const extra of attempts) {
    try {
      const result = await browsePlain(domain, options, extra);
      if (result.total > 0) {
        if (known === undefined) sectionWinners.set(key, attempts.indexOf(extra));
        return result;
      }
      last = result;
    } catch (error) {
      last = last ?? { error };
    }
  }
  if (last?.error) throw last.error;
  return last ?? { mods: [], total: 0, hasMore: false, page: Math.max(1, Number(options.page) || 1) };
}

async function browsePlain(domain, options = {}, section = {}) {
  const page = Math.max(1, Number(options.page) || 1);
  const hide = new Set((options.hide ?? []).map(String));
  const needle = String(options.query ?? '').trim();
  const base = {
    gameDomainName: [{ value: domain, op: 'EQUALS' }],
    adultContent: [{ value: false, op: 'EQUALS' }],
    ...section,
  };
  const run = async (extra) => {
    const data = await graphql(
      `query($filter: ModsFilter, $sort: [ModsSort!], $count: Int, $offset: Int) {
        mods(filter: $filter, sort: $sort, count: $count, offset: $offset) { totalCount nodes { ${MOD_FIELDS} } }
      }`,
      { filter: { ...base, ...extra }, sort: [SORTS[options.sort] ?? SORTS.popular], count: PAGE, offset: (page - 1) * PAGE }
    );
    return data?.mods ?? { totalCount: 0, nodes: [] };
  };

  // Имя «содержит» — для одного слова; для фразы, если так ничего нет,
  // — поиск по словам («stardew expanded» найдёт Stardew Valley Expanded).
  // Раздел ищет по имени (keywords), а человек ещё и ввёл запрос —
  // запрос главнее: он точнее слова раздела.
  let result = await run(needle ? { name: [{ value: needle, op: 'WILDCARD' }] } : {});
  if (needle && !result.totalCount && /\s/.test(needle)) {
    result = await run({ nameStemmed: [{ value: needle, op: 'MATCHES' }] });
  }

  const mods = (result.nodes ?? [])
    .map((node) => fromNode(node, domain))
    .filter((mod) => mod && !mod.adult && !hide.has(mod.id));
  const total = Number(result.totalCount ?? mods.length);
  return { mods, total, hasMore: page * PAGE < total, page };
}

/** Главный файл мода: отмеченный автором основным, иначе свежий из «Main files». */
function pickMainFile(files) {
  const usable = (files ?? []).filter((f) => f && !['OLD_VERSION', 'ARCHIVED', 'DELETED'].includes(f.category));
  const main = usable.filter((f) => f.category === 'MAIN');
  const newest = (list) => [...list].sort((a, b) => (b.date ?? 0) - (a.date ?? 0))[0];
  const pick = usable.find((f) => Number(f.primary) === 1) ?? newest(main) ?? newest(usable);
  if (!pick) return null;
  return {
    fileId: Number(pick.fileId),
    name: pick.name ?? '',
    version: pick.version ?? '',
    fileName: pick.uri ?? null,
    size: Number(pick.sizeInBytes ?? 0) || null,
  };
}

/**
 * Всё для страницы мода: описание (BBCode вперемешку с HTML — так хранит
 * Nexus), требования, главный файл.
 */
async function getDetails(domain, gameId, modId) {
  const mid = Number(modId);
  const gid = Number(gameId);
  if (!Number.isInteger(mid) || !Number.isInteger(gid)) throw new Error(t('err.modNotInCatalog'));

  const data = await graphql(`query {
    mod(modId: ${mid}, gameId: ${gid}) {
      ${MOD_FIELDS} description
      modRequirements { nexusRequirements { nodes { modId modName url gameId } } }
    }
    modFiles(modId: ${mid}, gameId: ${gid}) { fileId name version category primary sizeInBytes date uri }
  }`);

  const mod = fromNode(data?.mod, domain);
  if (!mod) return null;
  const requirements = (data.mod.modRequirements?.nexusRequirements?.nodes ?? [])
    .filter((r) => r?.modId)
    .map((r) => ({
      id: String(r.modId),
      name: plainText(r.modName) || `#${r.modId}`,
      sameGame: String(r.gameId ?? gid) === String(gid),
      url: r.url || `https://www.nexusmods.com/${domain}/mods/${r.modId}`,
    }));

  return { mod, description: data.mod.description ?? '', requirements, mainFile: pickMainFile(data.modFiles) };
}

module.exports = {
  parseNxmLink,
  validateKey,
  getDownloadLinks,
  getModInfo,
  filePageUrl,
  browse,
  getDetails,
  pickMainFile,
  fromNode,
  plainText,
  keyError,
  PAGE,
};
