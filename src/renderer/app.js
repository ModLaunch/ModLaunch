'use strict';

/**
 * Интерфейс ModHub 1.9.7.
 *
 * Без сборщиков и фреймворков: обычный DOM, один файл на логику и один на
 * словарь. Для программы такого размера это осознанный выбор — нет шага
 * сборки, значит нет и целого класса проблем при передаче проекта дальше.
 *
 * Экраны собираются строками и вешаются в #main, а обработчики — один
 * делегированный клик на весь экран. Так добавление кнопки не требует
 * помнить про её отдельную подписку: достаточно data-action.
 *
 * Всё общение с системой идёт через window.modhub из preload.js.
 */

const api = window.modhub;

/** Словарь живёт в i18n.js; здесь только короткий доступ к нему. */
const t = (key, params) => window.I18N.t(key, params);

const state = {
  /** id -> { info, status: 'unknown'|'searching'|'found'|'missing'|'error', game, progress } */
  games: new Map(),
  order: [],
  view: 'home', // home | games | popular | mod | game | premium | donate | settings | notfound
  activeGameId: null,
  gameTab: 'downloads', // downloads | market | log
  modView: null, // { gameId, modId }
  query: '',
  catalog: [],
  catalogFor: null,
  catalogLoading: false,
  /** Страницы каталога: сколько всего, есть ли ещё, и почему не загрузилось. */
  catalogMeta: { total: 0, hasMore: false, page: 1, error: null, query: '' },
  home: new Map(), // gameId -> mods[] (популярное)
  homeNew: new Map(), // gameId -> mods[] (свежие обновления)
  /** Какая игра выбрана в «Топе» на главной: 'all' или id игры. */
  homeChart: 'all',
  /** Реклама в шапке: что крутим и какая сейчас. */
  ads: { items: [], index: 0, rotate: 15 },
  homeErrors: new Map(), // gameId -> текст ошибки
  /** История переходов: стрелки «Назад» и «Вперёд» в шапке. */
  nav: { back: [], forward: [] },
  /** Ждём архив из браузера (Nexus): { gameId, modId, name, withKey }. */
  browserWait: null,
  mods: [],
  unmanaged: [],
  problems: [],
  issues: null,
  appInfo: null,
  settings: {},
  theme: 'dark',
  currency: 'USD',
  /** Большой баннер на главной: какой слайд сейчас и сколько их. */
  hero: 0,
  heroCount: 0,
  /** Сколько модов в каталоге каждой игры — для карточек «Ваши игры». */
  catalogTotals: {},
  /** Оценки ModHub: «игра|мод» -> { avg, count }. Нет записи — 0.00, «не оценено». */
  ratings: {},
  /** Подключён ли общий сервер отзывов в этой сборке. */
  reviewsConfigured: false,
  reviewsCount: 0,
  reviewsUnavailable: false,
  // Аккаунт ModHub (1.9.4): кто вошёл. Пропусков тут нет — только то, что видно.
  account: { configured: false, signedIn: false, email: null, name: null, verified: false, admin: false, adminPending: false },
  /**
   * Отзывы открытого мода: { key, loading, error, reviews, stats, configured, gate }.
   * gate (1.9.7) — можно ли оценить: { ok, own, installed, enabled, played }.
   */
  reviewView: null,
  /** Недописанные отзывы: «игра|мод» -> { stars, text, name } — переживают перерисовку. */
  reviewDraft: {},
  reviewSending: false,
  /** Картинки установленных модов по играм: gameId -> { id: адрес }. */
  installedMedia: {},
  /**
   * Настоящие картинки игр из Steam — только те адреса, что уже загрузились:
   * gameId -> { hero, logo, cover, header, shots: [{ full, thumb }] }.
   */
  gameMedia: {},
  /** Загрузки: что ставится сейчас и что закончилось недавно (новые — первыми). */
  jobs: [],
  /** Открыто ли окно загрузок в шапке. */
  dlOpen: false,
  /** Раздел настроек. */
  settingsTab: 'look',
  /** Установленные моды всех найденных игр: gameId -> mods[] (для «Установлен» и правой панели). */
  library: {},
  /** Каталог: порядок и категория (категории есть у ModLinks). */
  catalogSort: 'popular',
  catalogCategory: null,
  catalogCategories: {},
};

/* ================================================================== *
 *  Настройки внешнего вида и поведения
 * ================================================================== */

/**
 * Значения по умолчанию. В файле настроек хранится только то, что человек
 * поменял сам, — поэтому новые настройки появляются у всех сразу.
 */
const PREF_DEFAULTS = {
  theme: 'dark', // dark | light | system
  accent: 'violet', // violet | blue | green | orange | pink | gold
  zoom: 1, // 0.9 | 1 | 1.1 | 1.25
  density: 'normal', // compact | normal | large
  motion: 'full', // full | reduced | off
  startView: 'home', // home | last
  heroAutoplay: true,
  homeSections: { recommend: true, chart: true, shelves: true, fresh: true },
  notifyDone: true,
  soundDone: false,
  dlAutoOpen: false,
  gameArt: 'steam', // steam | drawn
  aside: true, // правая панель
  catalogView: 'grid', // grid | list
  hideInstalled: false,
};

const ACCENTS = {
  violet: '#7c5cff',
  blue: '#3d8bff',
  green: '#2fbf71',
  orange: '#ff8a3d',
  pink: '#ff5fa2',
  gold: '#e7b44a',
};

function pref(key) {
  const value = state.settings?.[key];
  if (value === undefined || value === null) return PREF_DEFAULTS[key];
  if (key === 'homeSections') return { ...PREF_DEFAULTS.homeSections, ...value };
  return value;
}

let systemTheme = null;
function effectiveTheme() {
  const theme = pref('theme');
  if (theme !== 'system') return theme === 'light' ? 'light' : 'dark';
  return window.matchMedia?.('(prefers-color-scheme: light)')?.matches ? 'light' : 'dark';
}

/** Применить настройки к окну: тема, акцент, масштаб, плотность, анимации. */
function applyPrefs() {
  const root = document.documentElement;
  root.dataset.theme = effectiveTheme();
  root.dataset.accent = ACCENTS[pref('accent')] ? pref('accent') : 'violet';
  root.dataset.density = pref('density');
  root.dataset.motion = pref('motion');
  try {
    api.app.setZoom?.(Number(pref('zoom')) || 1);
  } catch {
    /* масштаб — не главное */
  }
  // «Как в системе»: следим за сменой темы Windows на лету.
  if (!systemTheme && window.matchMedia) {
    systemTheme = window.matchMedia('(prefers-color-scheme: light)');
    systemTheme.addEventListener?.('change', () => {
      if (pref('theme') === 'system') root.dataset.theme = effectiveTheme();
    });
  }
}

/** Сохранить одну настройку и сразу применить. */
async function setPref(key, value) {
  state.settings[key] = value;
  if (key === 'theme') state.theme = value;
  applyPrefs();
  await call(api.settings.write({ [key]: value }), { silent: true }).catch(() => {});
}

function motionOn() {
  return pref('motion') !== 'off';
}

/* ================================================================== *
 *  Мелкие помощники
 * ================================================================== */

const $ = (selector) => document.querySelector(selector);

/** Экранирование: описания модов приходят из сети и в разметку как есть не идут. */
function esc(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function formatCount(n) {
  if (!n) return '0';
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1).replace('.0', '') + 'M';
  if (n >= 1000) return (n / 1000).toFixed(1).replace('.0', '') + 'K';
  return String(n);
}

/** «38 211» — полное число с разрядами, как в панели и карточках игр. */
function fullNumber(n) {
  return Number(n || 0).toLocaleString(window.I18N?.lang === 'en' ? 'en-US' : 'ru-RU');
}

/** «март 2025» — когда мод обновлялся; день тут лишний. */
function formatMonth(value) {
  const date = value ? new Date(value) : null;
  if (!date || Number.isNaN(date.getTime())) return '';
  const lang = window.I18N?.lang === 'en' ? 'en-US' : 'ru-RU';
  return date.toLocaleDateString(lang, { month: 'short', year: 'numeric' }).replace(/\s*г\.$/, '');
}

/** Устойчивое число из строки — чтобы одинаковый мод всегда выглядел одинаково. */
function hash(value) {
  let sum = 0;
  for (const ch of String(value ?? '')) sum = (sum * 31 + ch.codePointAt(0)) % 100000;
  return sum;
}

/** «1 зависимость», «3 зависимости», «7 зависимостей». */
function plural(n, key) {
  const count = Math.abs(Number(n) || 0);
  const lang = window.I18N.lang;
  let form = 'many';
  if (lang === 'ru') {
    const tens = count % 100;
    const ones = count % 10;
    if (ones === 1 && tens !== 11) form = 'one';
    else if (ones >= 2 && ones <= 4 && (tens < 12 || tens > 14)) form = 'few';
  } else if (count === 1) form = 'one';
  const text = t(`${key}.${form}`);
  return text === `${key}.${form}` ? t(key) : text;
}

/** «1 отзыв», «3 отзыва», «7 отзывов» — с числом внутри фразы. */
function pluralN(n, key) {
  const count = Math.abs(Number(n) || 0);
  let form = 'many';
  if (window.I18N.lang === 'ru') {
    const tens = count % 100;
    const ones = count % 10;
    if (ones === 1 && tens !== 11) form = 'one';
    else if (ones >= 2 && ones <= 4 && (tens < 12 || tens > 14)) form = 'few';
  } else if (count === 1) form = 'one';
  return t(`${key}.${form}`, { n: count });
}

/* --- оценки ModHub ---------------------------------------------------- */

/**
 * Оценка мода в ModHub — средняя по отзывам людей, у которых стоит ModHub.
 * Лайки Thunderstore и одобрения Nexus сюда больше не подмешиваются: это
 * другая шкала и чужие люди. Пока отзывов нет — честные 0.00, «не оценено».
 */
function ratingKey(gameId, modId) {
  return `${gameId}|${modId}`;
}

function ratingOf(gameId, modId) {
  const entry = state.ratings[ratingKey(gameId, modId)];
  return entry && entry.count > 0 ? entry : { avg: 0, count: 0 };
}

function formatScore(avg) {
  return (Number(avg) || 0).toFixed(2);
}

/** Строка оценки под названием мода: ★★★★★ 4.50 · 3 отзыва / 0.00 · не оценено. */
function ratingLine(gameId, modId, { compact = false } = {}) {
  const { avg, count } = ratingOf(gameId, modId);
  const note = count ? pluralN(count, 'rev.count') : t('rev.unrated');
  return `
    <span class="rate${count ? '' : ' is-empty'}${compact ? ' rate--compact' : ''}" title="${esc(`${formatScore(avg)} · ${note}`)}">
      <span class="rate__stars" style="--rate:${Math.round((avg / 5) * 100)}%"></span>
      <b>${esc(formatScore(avg))}</b>
      ${compact ? '' : `<span class="rate__note">${esc(note)}</span>`}
    </span>`;
}

/** Обновляет средние оценки: при запуске, раз в несколько минут и после своего отзыва. */
function applyRatings(all) {
  if (!all || typeof all !== 'object') return;
  state.ratings = all;
}

async function refreshRatings({ force = false } = {}) {
  try {
    const status = await call(api.reviews.status(), { silent: true });
    state.reviewsConfigured = Boolean(status?.configured);
    state.reviewsCount = status?.count ?? 0;
    // Сначала то, что уже лежит на диске, — цифры появляются мгновенно.
    applyRatings(await call(api.reviews.stats(), { silent: true }));
    softRender();
    if (!state.reviewsConfigured) return;
    const fresh = await call(api.reviews.sync({ force }), { silent: true });
    applyRatings(fresh?.stats);
    state.reviewsCount = fresh?.status?.count ?? state.reviewsCount;
    state.reviewsUnavailable = false;
    softRender();
  } catch {
    // Нет сети или кончился лимит — остаются последние известные оценки.
    const status = await call(api.reviews.status(), { silent: true }).catch(() => null);
    state.reviewsUnavailable = Boolean(status?.unavailable);
  }
}

/**
 * Перерисовка по фоновым событиям (пришли оценки, картинки). Если человек
 * в этот момент пишет отзыв или что-то вводит — не трогаем экран, иначе
 * у него из-под пальцев пропадёт курсор.
 */
function softRender() {
  const active = document.activeElement;
  if (active && active !== document.body && /^(INPUT|TEXTAREA)$/.test(active.tagName) && $('#main').contains(active)) return;
  render();
}

function initials(name) {
  return String(name ?? '?')
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0])
    .join('')
    .toUpperCase();
}

function hexToRgb(hex) {
  const value = String(hex ?? '#888').replace('#', '');
  return [0, 2, 4].map((i) => parseInt(value.slice(i, i + 2), 16) || 128);
}

/** Раскрашивает блок в цвет игры: всё внутри читает эти переменные. */
function accentStyle(hex) {
  const [r, g, b] = hexToRgb(hex);
  // Текст на кнопке цвета игры: на светлом золоте и зелени белый не читается.
  const lin = (c) => ((c /= 255) <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
  const luminance = 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
  return [
    `--accent:${hex}`,
    `--accent-ink:${luminance > 0.2 ? '#14120c' : '#ffffff'}`,
    `--accent-soft:rgba(${r},${g},${b},0.16)`,
    `--accent-line:rgba(${r},${g},${b},0.45)`,
    `--accent-deep:rgb(${Math.round(r * 0.55)} ${Math.round(g * 0.55)} ${Math.round(b * 0.55)})`,
  ].join(';');
}

const ICONS = {
  /** Знак ModHub: хаб, к которому подключаются моды. Он же на иконке, в установщике и на заставке. */
  logo:
    '<svg viewBox="0 0 24 24"><path d="M4.3 4.3 19.7 19.7M19.7 4.3 4.3 19.7" stroke="#7a62d6" stroke-width="1.5"/><circle cx="4.3" cy="4.3" r="3" fill="#cbbcff"/><circle cx="19.7" cy="4.3" r="3" fill="#f2c25c"/><circle cx="4.3" cy="19.7" r="3" fill="#8f73ff"/><circle cx="19.7" cy="19.7" r="3" fill="#cbbcff"/><circle cx="12" cy="12" r="5.6" fill="#fff" stroke="#b9a6ff" stroke-width=".6"/><circle cx="12" cy="12" r="2.6" fill="#7c5cff"/></svg>',
  video:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><rect x="3" y="5.5" width="18" height="13" rx="3"/><path d="M10.5 9.8v4.4l4-2.2z" fill="currentColor" stroke="none"/></svg>',
  book: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M4 5.5A1.5 1.5 0 0 1 5.5 4H11v16H5.5A1.5 1.5 0 0 1 4 18.5z" stroke-linejoin="round"/><path d="M20 5.5A1.5 1.5 0 0 0 18.5 4H13v16h5.5a1.5 1.5 0 0 0 1.5-1.5z" stroke-linejoin="round"/></svg>',
  // Знаки тем для обложек модов Hollow Knight.
  gamepad:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M7.5 7h9a5 5 0 0 1 4.9 6l-.8 3.6a2.3 2.3 0 0 1-4 .9L15 15.6H9l-1.6 1.9a2.3 2.3 0 0 1-4-.9L2.6 13A5 5 0 0 1 7.5 7z"/><path d="M8 10v3.5M6.25 11.75h3.5"/><path d="M15.5 11h.01M17.6 13.1h.01" stroke-width="2.6"/></svg>',
  swords:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M20 4h-3.2l-8.4 8.4 3.2 3.2L20 7.2z"/><path d="m6.8 13.2 4 4M8.4 15.6l-3.2 3.2"/><circle cx="4.3" cy="19.7" r="1"/><path d="M4 4h3.2l3.6 3.6M16 12.4l-.4.4-3.2-3.2M4 4v3.2l2.8 2.8"/><path d="m17.2 13.2-4 4M15.6 15.6l3.2 3.2"/><circle cx="19.7" cy="19.7" r="1"/></svg>',
  wrench:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M15.5 3.8a4.6 4.6 0 0 0-4.3 6.2L4.4 16.8a1.9 1.9 0 0 0 2.7 2.7l6.8-6.8a4.6 4.6 0 0 0 6.2-4.3l-2.6 2.6-2.8-.6-.6-2.8z"/></svg>',
  brush:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M19.5 3.5c-3 1.2-7.6 5.6-9.3 8.5l1.8 1.8c2.9-1.7 7.3-6.3 8.5-9.3z"/><path d="M9.3 13.2c-2 0-3.3 1.4-3.5 3.3-.1 1.3-.8 2.2-2.3 2.8 3.2 1.3 7.4.6 7.6-3.4z"/></svg>',
  puzzle:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"><path d="M9 4.5a2 2 0 1 1 4 0V6h4a1 1 0 0 1 1 1v4h-1.5a2 2 0 1 0 0 4H18v4a1 1 0 0 1-1 1h-4v-1.5a2 2 0 1 0-4 0V20H5a1 1 0 0 1-1-1v-4h1.5a2 2 0 1 0 0-4H4V7a1 1 0 0 1 1-1h4z"/></svg>',
  map: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"><path d="m3.5 6.5 5-2.5 7 3 5-2.5v13l-5 2.5-7-3-5 2.5z"/><path d="M8.5 4v13M15.5 7v13"/></svg>',
  amulet:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M7 3.5c0 3 2.2 5 5 5s5-2 5-5"/><circle cx="12" cy="15" r="5.5"/><path d="m12 12 1.4 2.5L12 17l-1.4-2.5z"/></svg>',
  smile:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"><circle cx="12" cy="12" r="8.5"/><path d="M8.5 14.5c1.8 2.2 5.2 2.2 7 0M9 9.5h.01M15 9.5h.01" stroke-width="2"/></svg>',
  user: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="8.5" r="3.8"/><path d="M4.5 20c1.2-3.6 4-5.5 7.5-5.5s6.3 1.9 7.5 5.5"/></svg>',
  lock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><rect x="5" y="10.5" width="14" height="10" rx="2.5"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5" stroke-linecap="round"/></svg>',
  mail: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><rect x="3.5" y="5.5" width="17" height="13" rx="2.5"/><path d="m4.5 7 7.5 6 7.5-6" stroke-linecap="round"/></svg>',
  logout: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 4.5H7a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2h7"/><path d="M11 12h9.5M17 8.5l3.5 3.5-3.5 3.5"/></svg>',
  shield: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M12 3.5 5 6v5.5c0 4.2 2.9 7.6 7 9 4.1-1.4 7-4.8 7-9V6z"/><path d="m9 12 2.2 2.2L15.5 10" stroke-linecap="round"/></svg>',
  eyeOff: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"><path d="M4 4l16 16"/><path d="M10.6 5.6A10 10 0 0 1 12 5.5c6 0 9.5 6.5 9.5 6.5a17 17 0 0 1-3.2 3.9M6.5 7.4A16.6 16.6 0 0 0 2.5 12S6 18.5 12 18.5a9.4 9.4 0 0 0 4-.9"/><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2"/></svg>',
  eye: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z" stroke-linejoin="round"/><circle cx="12" cy="12" r="3"/></svg>',
  tag: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M3.5 12.5V4.5a1 1 0 0 1 1-1h8l8 8-9 9z"/><circle cx="8.5" cy="8.5" r="1.4"/></svg>',
  crown:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M4 18h16l1.2-9-5 3.2L12 4.5 7.8 12.2l-5-3.2z"/></svg>',
  menu: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 7h16M4 12h16M4 17h16"/></svg>',
  cup: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M5 8h11v6a4 4 0 0 1-4 4H9a4 4 0 0 1-4-4z" stroke-linejoin="round"/><path d="M16 9h2.2a2.3 2.3 0 0 1 0 4.6H16"/><path d="M7 3.5v2M10.5 3v2.5M14 3.5v2" stroke-linecap="round"/></svg>',
  gear: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><circle cx="12" cy="12" r="3.2"/><path d="M12 2.8v2.4M12 18.8v2.4M21.2 12h-2.4M5.2 12H2.8M18.5 5.5l-1.7 1.7M7.2 16.8l-1.7 1.7M18.5 18.5l-1.7-1.7M7.2 7.2 5.5 5.5" stroke-linecap="round"/></svg>',
  search:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5" stroke-linecap="round"/></svg>',
  download:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 4v11m0 0 4-4m-4 4-4-4" stroke-linecap="round" stroke-linejoin="round"/><path d="M5 19h14" stroke-linecap="round"/></svg>',
  play: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5.5v13l11-6.5z"/></svg>',
  folder:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" stroke-linejoin="round"/></svg>',
  external:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 5h5v5M19 5l-8 8" stroke-linecap="round" stroke-linejoin="round"/><path d="M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4" stroke-linecap="round"/></svg>',
  star: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="m12 4 2.3 4.9 5.2.7-3.8 3.7.9 5.3-4.6-2.6-4.6 2.6.9-5.3L4.5 9.6l5.2-.7z"/></svg>',
  heart:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M12 19.5 4.8 12.6a4.3 4.3 0 0 1 6.1-6l1.1 1.1 1.1-1.1a4.3 4.3 0 1 1 6.1 6z" stroke-linejoin="round"/></svg>',
  list: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 7h12M8 12h12M8 17h12M4 7h.01M4 12h.01M4 17h.01"/></svg>',
  shop: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M4 8h16l-1 11H5z" stroke-linejoin="round"/><path d="M9 8V6a3 3 0 0 1 6 0v2"/></svg>',
  trash:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M5 7h14M10 7V5h4v2M7 7l1 12h8l1-12" stroke-linejoin="round"/></svg>',
  check:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  warn: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 4.5 21 20H3z" stroke-linejoin="round"/><path d="M12 10v4M12 17h.01" stroke-linecap="round"/></svg>',
  info: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 7.5h.01" stroke-linecap="round"/></svg>',
  refresh:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 12a8 8 0 1 1-2.6-5.9" stroke-linecap="round"/><path d="M20 4v4h-4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  chat: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><path d="M4.5 6.5A2.5 2.5 0 0 1 7 4h10a2.5 2.5 0 0 1 2.5 2.5v7A2.5 2.5 0 0 1 17 16h-5.5L7 19.5V16a2.5 2.5 0 0 1-2.5-2.5z"/><path d="M8.5 8.8h7M8.5 11.8h4.5" stroke-linecap="round"/></svg>',
  edit: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><path d="M4.5 19.5h4l10-10-4-4-10 10z"/><path d="m12.5 7.5 4 4" stroke-linecap="round"/></svg>',
  // 1.8: стрелки истории, витрина главной и реклама.
  arrowLeft:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5"/><path d="m11 6-6 6 6 6"/></svg>',
  arrowRight:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m13 6 6 6-6 6"/></svg>',
  chevLeft:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m14.5 5.5-6.5 6.5 6.5 6.5"/></svg>',
  chevRight:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m9.5 5.5 6.5 6.5-6.5 6.5"/></svg>',
  flame:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M13.1 2.6c.5 3.1-1.2 4.8-2.8 6.4C8.7 10.6 7.2 12.2 7.2 14.8a4.9 4.9 0 0 0 4.9 4.9 5 5 0 0 0 5-5c0-1.8-.7-3.2-1.6-4.3-.2 1.4-.9 2.4-2 2.8.6-3.1-.2-7-.4-10.6Z"/></svg>',
  trophy:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M8 4h8v5.5a4 4 0 0 1-8 0z"/><path d="M8 6H5.2a3 3 0 0 0 3 4.2M16 6h2.8a3 3 0 0 1-3 4.2"/><path d="M12 13.5v3.5M8.8 20h6.4M10 17h4"/></svg>',
  sparkle:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="m11 2.8 1.9 5.4 5.4 1.9-5.4 1.9L11 17.4 9.1 12 3.7 10.1 9.1 8.2z"/><path d="m18.3 14.6.9 2.3 2.3.9-2.3.9-.9 2.3-.9-2.3-2.3-.9 2.3-.9z"/></svg>',
  megaphone:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round" stroke-linecap="round"><path d="M4 10v4h3l7.5 4.5v-13L7 10z"/><path d="M17.7 9.3a3.8 3.8 0 0 1 0 5.4"/><path d="m7.8 14.5 1.3 4.5h2.2"/></svg>',
  grid:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><rect x="4" y="4" width="7" height="7" rx="2"/><rect x="13" y="4" width="7" height="7" rx="2"/><rect x="4" y="13" width="7" height="7" rx="2"/><rect x="13" y="13" width="7" height="7" rx="2"/></svg>',
  close:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/></svg>',
  // 1.9: разделы настроек.
  palette:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><path d="M12 3.5a8.5 8.5 0 1 0 0 17c1.3 0 1.9-.8 1.9-1.7 0-1.3-1.1-1.6-1.1-2.7 0-1 .8-1.6 1.9-1.6h1.9a3.9 3.9 0 0 0 3.9-3.9C20.5 6.6 16.7 3.5 12 3.5z"/><circle cx="7.6" cy="11" r="1.3" fill="currentColor" stroke="none"/><circle cx="10.2" cy="7.3" r="1.3" fill="currentColor" stroke="none"/><circle cx="14.8" cy="7.3" r="1.3" fill="currentColor" stroke="none"/></svg>',
  home:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><path d="M4 10.5 12 4l8 6.5V19a1 1 0 0 1-1 1h-4.5v-5.5h-5V20H5a1 1 0 0 1-1-1z"/></svg>',
  image:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><rect x="3.5" y="5" width="17" height="14" rx="2.5"/><circle cx="9" cy="10" r="1.7"/><path d="m4.5 17.5 5-5 3.5 3.5 2.5-2.5 4 4" stroke-linecap="round"/></svg>',
  key:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="15" r="4"/><path d="m10.9 12.1 8.6-8.6M16.5 6.5l2.5 2.5M14 9l2 2"/></svg>',
  sort:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 4v16M3.5 16.5 7 20l3.5-3.5M17 20V4M13.5 7.5 17 4l3.5 3.5"/></svg>',
  panel:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><rect x="3.5" y="4.5" width="17" height="15" rx="2.5"/><path d="M14.5 4.5v15"/><path d="M16.8 8.5h1.4M16.8 11.5h1.4" stroke-linecap="round"/></svg>',
  keyboard:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6.5 10h.01M10 10h.01M13.5 10h.01M17 10h.01M7.5 14h9"/></svg>',
};

function icon(name) {
  return ICONS[name] ?? '';
}

/**
 * Картинки.
 *
 * Лежат рядом с программой, а не грузятся из сети: без интернета интерфейс
 * должен оставаться собой. Фотографии свободной лицензии, затемнены заранее,
 * чтобы белый текст читался поверх без дополнительных слоёв.
 */
const ART = {
  banners: ['art/banner-hero.jpg', 'art/banner-deps.jpg', 'art/banner-broken.jpg'],
  notFound: 'art/not-found.jpg',
  games: {
    'stardew-valley': 'art/game-stardew-valley.jpg',
    'hollow-knight': 'art/game-hollow-knight.jpg',
    'lethal-company': 'art/game-lethal-company.jpg',
  },
};

/**
 * Кастомизация картинок.
 *
 * Стандартные картинки нарисованы для ModHub и лежат внутри программы.
 * В настройках к ним можно выбрать фотографии с Unsplash — их лицензия
 * разрешает такое использование, а грузятся они прямо с Unsplash, как того
 * требуют их правила. Нет интернета — остаются стандартные.
 */
const PHOTO_IDS = {"1A": "photo-1617507171089-6cb9aa5add36", "1B": "photo-1715279240000-9a50953e327d", "1C": "photo-1708032563898-9ed0c17117b8", "2A": "photo-1654198340681-a2e0fc449f1b", "2B": "photo-1566410824233-a8011929225c", "2C": "photo-1637825891028-564f672aa42c", "3A": "photo-1563127673-b35a42ef206c", "3B": "photo-1569787811498-32f428436f46", "3C": "photo-1759711896972-5d441aadec06", "4A": "photo-1762284288853-439152998dfc", "4B": "photo-1789182095184-4e18e2fd778d", "4C": "photo-1767779670896-a02bc4f0b5e3", "5A": "photo-1642278624449-774e31a7754d", "5B": "photo-1573546066056-1c3307c08221", "5C": "photo-1673209034002-4dcfabff76ec", "6A": "photo-1495709165577-351d14380bdd", "6B": "photo-1619204715997-1367fe5812f1", "6C": "photo-1699205269431-1ab18afabac6"};

const PHOTO_SLOTS = {
  banner: ['1A', '1B', '1C', '2A', '2B', '2C'],
  'stardew-valley': ['3A', '3B', '3C'],
  'hollow-knight': ['4A', '4B', '4C'],
  'lethal-company': ['5A', '5B', '5C'],
  notfound: ['6A', '6B', '6C'],
};

function photoUrl(code, size = 'w=1600&q=70') {
  const id = PHOTO_IDS[code];
  return id ? `https://images.unsplash.com/${id}?${size}&fm=jpg&fit=crop` : null;
}

/** Выбор человека из настроек, если он есть, иначе стандартная картинка. */
function customArt(slot) {
  const code = state.settings.customArt?.[slot];
  return code ? photoUrl(code) : null;
}

/**
 * Настоящая картинка игры из Steam, если она уже загрузилась.
 * kind: hero — широкий арт; icon — вертикальная обложка для значков;
 * header — шапка магазина.
 */
function steamArt(gameId, kind = 'hero') {
  if (pref('gameArt') === 'drawn') return null;
  const media = state.gameMedia[gameId];
  if (!media) return null;
  if (kind === 'icon') return media.cover ?? media.hero ?? media.header ?? null;
  if (kind === 'header') return media.header ?? media.hero ?? null;
  return media.hero ?? media.shots?.[0]?.full ?? media.header ?? null;
}

/** Картинка игры: выбор человека → настоящая из Steam → нарисованная ModHub. */
function gameArt(gameId, kind = 'hero') {
  return customArt(gameId) ?? steamArt(gameId, kind) ?? ART.games[gameId] ?? ART.banners[0];
}

/** Официальный логотип игры (прозрачный PNG из Steam) или null. */
function gameLogo(gameId) {
  if (pref('gameArt') === 'drawn' || customArt(gameId)) return null;
  return state.gameMedia[gameId]?.logo ?? null;
}

/** Скриншоты игры из Steam — только если Steam вообще доступен. */
function gameShots(gameId) {
  if (pref('gameArt') === 'drawn') return [];
  const media = state.gameMedia[gameId];
  return media?.hero || media?.header ? media.shots ?? [] : [];
}

/** Первый адрес из списка, который действительно загрузился (или null). */
function firstLoaded(urls, timeout = 9000) {
  const list = (urls ?? []).filter(Boolean);
  return new Promise((resolve) => {
    let i = 0;
    const next = () => {
      if (i >= list.length) return resolve(null);
      const url = list[i++];
      const img = new Image();
      img.referrerPolicy = 'no-referrer';
      let done = false;
      const timer = setTimeout(() => {
        if (done) return;
        done = true;
        next();
      }, timeout);
      img.onload = () => {
        if (done) return;
        done = true;
        clearTimeout(timer);
        // Заглушка в 1×1 — не картинка.
        if (img.naturalWidth < 8) next();
        else resolve(url);
      };
      img.onerror = () => {
        if (done) return;
        done = true;
        clearTimeout(timer);
        next();
      };
      img.src = url;
    };
    next();
  });
}

/**
 * Картинки игр из Steam. Адреса приходят из основного процесса, а здесь
 * каждый проверяется загрузкой: в интерфейс попадает только то, что
 * реально показалось, — без пустых рамок и мигания.
 */
async function loadGameMedia() {
  if (!api.media?.games) return;
  const all = await call(api.media.games(), { silent: true }).catch(() => null);
  if (!all) return;
  await Promise.all(
    Object.entries(all).map(async ([gameId, media]) => {
      const shots = Array.isArray(media?.shots) ? media.shots.filter((s) => s?.full) : [];
      const [hero, logo, cover, header] = await Promise.all([
        firstLoaded([...(media.hero ?? []), ...(shots[0] ? [shots[0].full] : [])]),
        firstLoaded(media.logo ?? []),
        firstLoaded(media.cover ?? []),
        firstLoaded(media.header ?? []),
      ]);
      if (!hero && !header) return; // Steam недоступен — остаются рисованные
      state.gameMedia[gameId] = { hero, logo, cover, header, shots };
      renderRail();
      softRender();
    })
  );
}

function bannerArt(index) {
  return customArt('banner') ?? ART.banners[index] ?? ART.banners[0];
}

function notFoundArt() {
  return customArt('notfound') ?? ART.notFound;
}

/**
 * Что показать, когда игры на компьютере нет.
 * Ролик и ссылки — чужие, поэтому открываются в браузере, а не внутри.
 */
const HELP = {
  'stardew-valley': {
    video: 'Ly7M8EOSpt4',
    links: [
      ['Гайд по модам на вики Stardew Valley', 'https://stardewvalleywiki.com/Modding:Player_Guide/Getting_Started'],
      ['SMAPI — сайт загрузчика', 'https://smapi.io'],
      ['Каталог модов на Nexus', 'https://www.nexusmods.com/stardewvalley/mods'],
    ],
  },
  'hollow-knight': {
    video: 'ISN6EHxyyDk',
    links: [
      ['Каталог модов ModLinks', 'https://github.com/hk-modding/modlinks'],
      ['Modding API — как он устроен', 'https://github.com/hk-modding/api'],
      ['Lumafly — установщик сообщества', 'https://themulhima.github.io/Lumafly/'],
    ],
  },
  'lethal-company': {
    video: 'XL_Chiq_zZg',
    links: [
      ['Каталог модов Thunderstore', 'https://thunderstore.io/c/lethal-company/'],
      ['BepInEx — загрузчик', 'https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/'],
    ],
  },
};

function toast(message, kind = 'ok') {
  const node = document.createElement('div');
  node.className = `toast${kind === 'ok' ? '' : ' toast--' + kind}`;
  node.textContent = message;
  $('#toasts').append(node);
  setTimeout(
    () => {
      node.style.opacity = '0';
      setTimeout(() => node.remove(), 320);
    },
    kind === 'error' ? 7000 : 3600
  );
}

/**
 * Разворачивает ответ IPC, показывая ошибку человеку вместо тишины.
 * Короткое сообщение уходит во всплывашку, длинное или требующее действия —
 * в окно: объяснение на три строки в узкой всплывашке нечитаемо.
 */
async function call(promise, { silent = false } = {}) {
  const result = await promise;
  if (!result || result.ok) return result?.data;

  if (!silent) {
    const hasWayOut = result.manualPath || result.logFile || result.needsElevation;
    if (hasWayOut || (result.error ?? '').length > 110) {
      errorModal(result.error, {
        elevation: Boolean(result.needsElevation),
        manualPath: result.manualPath ?? null,
        logFile: result.logFile ?? null,
      });
    } else {
      toast(result.error, 'error');
    }
  }
  const error = new Error(result.error);
  error.result = result;
  throw error;
}

/* ================================================================== *
 *  Данные
 * ================================================================== */

function entry(gameId) {
  return state.games.get(gameId) ?? null;
}

function activeGame() {
  return entry(state.activeGameId)?.game ?? null;
}

function readyGames() {
  return state.order.map(entry).filter((item) => item?.game?.found);
}

/**
 * Первый список — без обращения к дискам: пути, найденные раньше, известны
 * сразу. Поиск для остальных идёт отдельно и по одной игре, чтобы окно было
 * живым, а человек видел, что происходит.
 */
async function loadGames() {
  const supported = await call(api.games.supported());
  state.order = supported.map((g) => g.id);
  for (const info of supported) {
    state.games.set(info.id, { info, status: 'unknown', game: null, progress: null });
  }
  renderRail();
  render();

  const list = await call(api.games.list(), { silent: true }).catch(() => []);
  for (const game of list ?? []) {
    const item = entry(game.id);
    if (!item) continue;
    item.game = game;
    item.status = game.found ? 'found' : 'unknown';
  }
  renderRail();
  render();

  // Известные игры уже на экране — заставку можно убирать, витрины грузятся
  // в фоне. Раньше окно ждало и поиска по дискам, и ответа всех каталогов.
  hideSplash();
  loadHomeRows();

  // Не найденные по памяти — ищем на дисках, последовательно.
  // Параллельный обход трёх игр читает одни и те же папки втрое и мешает сам себе.
  for (const id of state.order) {
    const item = entry(id);
    if (item.status === 'found') continue;
    const game = await searchGame(id);
    if (game?.found) {
      loadHomeRows();
      loadLibrary();
    }
  }
}

/**
 * @param {string} gameId
 * @param {{deep?: boolean, forget?: boolean, quiet?: boolean}} [options]
 */
async function searchGame(gameId, options = {}) {
  const item = entry(gameId);
  if (!item) return null;

  item.status = 'searching';
  item.progress = null;
  renderRail();
  render();

  try {
    const game = await call(
      options.forget || options.deep
        ? api.games.rescan(gameId, { deep: Boolean(options.deep) })
        : api.games.inspect(gameId),
      { silent: true }
    );
    item.game = game;
    item.status = game.found ? 'found' : 'missing';
    item.progress = null;
    if (!options.quiet) {
      toast(
        game.found
          ? t('toast.found', { game: item.info.name })
          : t('toast.notFound', { game: item.info.name }),
        game.found ? 'ok' : 'warn'
      );
    }
    return game;
  } catch (error) {
    item.status = 'error';
    item.error = error.message;
    return null;
  } finally {
    renderRail();
    render();
  }
}

async function refreshMods() {
  const game = activeGame();
  if (!game?.found) {
    state.mods = [];
    state.unmanaged = [];
    state.problems = [];
    return;
  }
  const [result, problems] = await Promise.all([
    call(api.mods.list(game.id), { silent: true }).catch(() => ({ mods: [], unmanaged: [] })),
    call(api.mods.problems(game.id), { silent: true }).catch(() => []),
  ]);
  state.mods = result?.mods ?? [];
  state.library[game.id] = state.mods;
  state.unmanaged = result?.unmanaged ?? [];
  state.problems = problems ?? [];
  loadInstalledMedia(game.id);
}

/**
 * Установленные моды всех найденных игр — для отметок «Установлен» на
 * главной и для правой панели («Недавно установлены», счётчики).
 * Список читается с диска, без сети: это быстро.
 */
async function loadLibrary() {
  await Promise.all(
    readyGames().map(async (item) => {
      const result = await call(api.mods.list(item.game.id), { silent: true }).catch(() => null);
      if (result?.mods) state.library[item.game.id] = result.mods;
    })
  );
  softRender();
}

/** Картинки установленных модов — во вкладке «Загрузки» они как в каталоге. */
async function loadInstalledMedia(gameId) {
  if (!api.mods.media || !state.mods.length) return;
  const found = await call(api.mods.media(gameId), { silent: true }).catch(() => null);
  if (!found) return;
  const before = JSON.stringify(state.installedMedia[gameId] ?? {});
  state.installedMedia[gameId] = found;
  if (JSON.stringify(found) !== before && state.view === 'game' && state.activeGameId === gameId) softRender();
}

/**
 * Каталог игры для экранов «Популярное», «Рынок» и поиска.
 * append — дописать следующую страницу к уже загруженным («Показать ещё»).
 * Ошибка не превращается в «ничего нет»: её текст показывается на месте
 * каталога вместе с кнопкой «Повторить».
 */
async function loadCatalog(gameId, query = '', { append = false } = {}) {
  const item = entry(gameId);
  if (!item?.game) {
    state.catalog = [];
    state.catalogFor = gameId;
    return;
  }
  // Категории и порядок — у каждой игры свои: при смене игры сбрасываем.
  if (state.catalogFor && state.catalogFor !== gameId) {
    state.catalogCategory = null;
    if (!(SORTS_BY_KIND[item.game.catalog?.kind] ?? ['popular']).includes(state.catalogSort)) state.catalogSort = 'popular';
  }
  loadCategories(item.game);

  const page = append ? state.catalogMeta.page + 1 : 1;
  const token = Symbol('catalog');
  state.catalogToken = token;
  state.catalogLoading = true;
  state.catalogFor = gameId;
  if (!append) {
    state.catalog = [];
    state.catalogMeta = { total: 0, hasMore: false, page: 1, error: null, query };
  }
  render();
  try {
    const result = await call(
      api.catalog.search(gameId, query, { page, sort: state.catalogSort, category: state.catalogCategory ?? undefined }),
      { silent: true }
    );
    if (state.catalogToken !== token) return;
    const mods = result?.mods ?? [];
    const known = new Set(state.catalog.map((m) => m.id));
    state.catalog = append ? state.catalog.concat(mods.filter((m) => !known.has(m.id))) : mods;
    state.catalogMeta = { total: result?.total ?? mods.length, hasMore: Boolean(result?.hasMore), page, error: null, query };
    fillMedia(gameId, mods);
  } catch (error) {
    if (state.catalogToken !== token) return;
    state.catalogMeta = { ...state.catalogMeta, error: error.message || t('catalog.error') };
  } finally {
    if (state.catalogToken === token) {
      state.catalogLoading = false;
      render();
    }
  }
}

/**
 * Витрина главной. Все игры грузятся параллельно: популярное (для баннера,
 * мозаики, топа и полок) и, где каталог умеет сортировать по дате, свежие
 * обновления. Ошибка свежих не мешает популярному — полка просто не видна.
 */
const HOME_LIMIT = 12;
const FRESH_KINDS = new Set(['thunderstore', 'nexus']);

async function loadHomeRows({ force = false } = {}) {
  await Promise.all(
    readyGames().map(async (item) => {
      const id = item.game.id;
      if (!force && state.home.has(id)) return;
      state.homeErrors.delete(id);

      const fresh = FRESH_KINDS.has(item.game.catalog?.kind)
        ? call(api.catalog.search(id, '', { limit: 10, sort: 'updated' }), { silent: true }).catch(() => null)
        : Promise.resolve(null);

      try {
        const result = await call(api.catalog.search(id, '', { limit: HOME_LIMIT }), { silent: true });
        state.home.set(id, (result?.mods ?? []).slice(0, HOME_LIMIT));
        if (result?.total) state.catalogTotals[id] = result.total;
        fillMedia(id, state.home.get(id));
      } catch (error) {
        state.home.set(id, []);
        state.homeErrors.set(id, error.message || t('catalog.error'));
      }

      const updated = await fresh;
      state.homeNew.set(id, (updated?.mods ?? []).slice(0, 10));
      if (state.homeNew.get(id).length) fillMedia(id, state.homeNew.get(id));
      if (state.view === 'home') softRender();
      else renderAside();
    })
  );
}

/* ================================================================== *
 *  Навигация
 *
 *  Как в браузере: каждый переход кладёт экран, с которого ушли, в стопку
 *  «Назад», а стопку «Вперёд» очищает. Стрелки в шапке, боковые кнопки
 *  мыши и Alt+←/→ ходят по этим стопкам. Возврат на экран сам новой
 *  записи не создаёт — иначе «Назад» и «Вперёд» гоняли бы по кругу.
 * ================================================================== */

const NAV_LIMIT = 60;

/** Где человек сейчас — ровно столько, сколько нужно, чтобы сюда вернуться. */
function here() {
  return {
    view: state.view,
    gameId: state.activeGameId,
    tab: state.gameTab,
    query: state.query,
    mod: state.view === 'mod' && state.modView ? { gameId: state.modView.gameId, modId: state.modView.modId } : null,
  };
}

function sameSpot(a, b) {
  if (!a || !b || a.view !== b.view) return false;
  switch (a.view) {
    case 'mod':
      return a.mod?.gameId === b.mod?.gameId && a.mod?.modId === b.mod?.modId;
    case 'game':
      return a.gameId === b.gameId && a.tab === b.tab;
    case 'popular':
      return a.gameId === b.gameId && (a.query ?? '') === (b.query ?? '');
    case 'notfound':
      return a.gameId === b.gameId;
    default:
      return true;
  }
}

/**
 * Запомнить текущий экран перед уходом с него.
 * next — куда идём: если это тот же экран, запись не нужна.
 */
function remember(next) {
  const current = here();
  if (next && sameSpot(current, next)) return;
  const back = state.nav.back;
  if (!sameSpot(back[back.length - 1], current)) back.push(current);
  if (back.length > NAV_LIMIT) back.splice(0, back.length - NAV_LIMIT);
  state.nav.forward = [];
}

/**
 * @param {string} view
 * @param {{gameId?: string, tab?: string, mod?: object}} [payload]
 * @param {{record?: boolean}} [options] record: false — переход из истории
 */
function go(view, payload = {}, { record = true } = {}) {
  if (record) {
    remember({
      view,
      gameId: 'gameId' in payload ? payload.gameId : state.activeGameId,
      tab: payload.tab ?? state.gameTab,
      query: state.query,
      mod: null,
    });
  }
  state.view = view;
  if ('gameId' in payload) state.activeGameId = payload.gameId;
  if ('tab' in payload) state.gameTab = payload.tab;
  if ('mod' in payload) state.modView = payload.mod;
  if (view !== 'popular') state.query = '';
  renderRail();
  render();
  $('#main').scrollTop = 0;
}

async function openGame(gameId, tab = 'downloads', { record = true } = {}) {
  const item = entry(gameId);

  // Игры нет — вместо пустого экрана показываем, что с этим делать.
  if (!item?.game?.found) {
    go('notfound', { gameId }, { record });
    return;
  }

  if (record) remember({ view: 'game', gameId, tab });
  if (state.settings.lastGame !== gameId) {
    state.settings.lastGame = gameId;
    call(api.settings.write({ lastGame: gameId }), { silent: true }).catch(() => {});
  }
  state.activeGameId = gameId;
  state.view = 'game';
  state.gameTab = tab;
  state.query = '';
  renderRail();
  render();

  await refreshMods();
  render();

  if (tab === 'market' && (state.catalogFor !== gameId || state.catalogMeta.query || !state.catalog.length)) {
    await loadCatalog(gameId);
  }
  if (tab === 'log') await loadIssues(gameId);
}

/** Вкладка на экране игры — тоже шаг истории: «Назад» вернёт на прошлую. */
async function openTab(tab, { record = true } = {}) {
  if (record) remember({ view: 'game', gameId: state.activeGameId, tab });
  state.gameTab = tab;
  render();
  // Каталог этой игры уже загружен (в том числе по поиску из шапки) — показываем его.
  if (tab === 'market' && (state.catalogFor !== state.activeGameId || (!state.catalog.length && !state.catalogLoading))) {
    await loadCatalog(state.activeGameId, state.query);
  }
  if (tab === 'log') await loadIssues(state.activeGameId);
}

async function openPopular(gameId, query = '', { record = true } = {}) {
  if (record) remember({ view: 'popular', gameId, query });
  state.activeGameId = gameId;
  state.query = query;
  state.view = 'popular';
  renderRail();
  render();
  $('#main').scrollTop = 0;
  if (state.catalogFor !== gameId || state.catalogMeta.query !== query || !state.catalog.length) {
    await loadCatalog(gameId, query);
  }
}

async function loadIssues(gameId) {
  state.issues = null;
  render();
  state.issues = await call(api.diagnostics.issues(gameId), { silent: true }).catch(() => null);
  render();
}

function knownMod(gameId, modId) {
  return (
    state.catalog.find((m) => m.id === modId) ??
    (state.home.get(gameId) ?? []).find((m) => m.id === modId) ??
    (state.homeNew.get(gameId) ?? []).find((m) => m.id === modId) ??
    null
  );
}

async function openMod(gameId, modId, { record = true } = {}) {
  const known = knownMod(gameId, modId);

  if (record) remember({ view: 'mod', mod: { gameId, modId } });

  state.modView = { gameId, modId, mod: known, details: null, detailsError: null };
  state.view = 'mod';
  $('#main').scrollTop = 0;
  renderRail();
  render();

  // Отзывы грузятся параллельно с описанием: оба запроса независимы.
  loadReviews(gameId, modId);

  try {
    const details = await call(api.catalog.details(gameId, modId), { silent: true });
    if (state.modView?.modId !== modId) return;
    state.modView.details = details;
    if (details?.mod) state.modView.mod = { ...(known ?? {}), ...details.mod };
  } catch (error) {
    if (state.modView?.modId !== modId) return;
    state.modView.detailsError = error.message;
  }
  if (state.view === 'mod' && state.modView?.modId === modId) render();
}

/** Вернуться на экран из истории, не записывая сам возврат. */
function restore(spot) {
  if (!spot) return;
  switch (spot.view) {
    case 'mod':
      if (spot.mod) return openMod(spot.mod.gameId, spot.mod.modId, { record: false });
      return go('home', {}, { record: false });
    case 'game':
      return openGame(spot.gameId, spot.tab ?? 'downloads', { record: false });
    case 'popular':
      return openPopular(spot.gameId, spot.query ?? '', { record: false });
    case 'notfound':
      return go('notfound', { gameId: spot.gameId }, { record: false });
    default:
      return go(spot.view, {}, { record: false });
  }
}

function canGoBack() {
  return state.nav.back.length > 0;
}

function canGoForward() {
  return state.nav.forward.length > 0;
}

function goBack() {
  const previous = state.nav.back.pop();
  if (!previous) return;
  state.nav.forward.push(here());
  restore(previous);
}

function goForward() {
  const next = state.nav.forward.pop();
  if (!next) return;
  state.nav.back.push(here());
  restore(next);
}

/* ================================================================== *
 *  Загрузки: кнопка, которая сама показывает прогресс, и окно загрузок
 *
 *  Как в Steam, Epic и Modrinth App: нажатая «Установить» превращается
 *  в полосу прогресса с процентами (а в тесном топе — в кольцо), по
 *  окончании на секунду-другую зеленеет галочкой. Всё, что ставится и
 *  недавно поставилось, собрано в окне за кнопкой со стрелкой в шапке:
 *  скорость, сколько осталось, «Открыть» и «Повторить».
 *
 *  Прогресс приходит часто, поэтому экран на каждое событие не
 *  перерисовывается: обновляются только узлы с data-job.
 * ================================================================== */

const JOB_KEEP = 20;
const DONE_SHOW_MS = 2600;
const JOB_STAGES = ['prepare', 'deps', 'download', 'extract', 'browser', 'done', 'error'];

function jobKey(gameId, modId) {
  return `${gameId}|${modId}`;
}

function startJob({ gameId, modId = null, name, kind = 'mod', mod = null }) {
  const key = kind === 'loader' ? `loader|${gameId}` : jobKey(gameId, modId);
  state.jobs = state.jobs.filter((job) => job.key !== key);
  const job = {
    key,
    kind,
    gameId,
    modId,
    name: name || modId || '',
    current: name || '',
    mod,
    status: 'active',
    stage: 'prepare',
    ratio: null,
    bytes: 0,
    bytesTotal: null,
    index: 0,
    total: 0,
    speed: 0,
    lastAt: 0,
    lastBytes: 0,
    started: Date.now(),
    finished: 0,
    error: null,
    seen: false,
    names: new Set([name].filter(Boolean)),
  };
  state.jobs.unshift(job);
  state.jobs = state.jobs.slice(0, JOB_KEEP);
  if (pref('dlAutoOpen') && !state.dlOpen) toggleDownloads(true);
  renderDownloads();
  softRender();
  return job;
}

/**
 * Чья это весть о прогрессе. Обычно в игре ставится что-то одно; если
 * несколько сразу — различаем по названию мода: у каждой загрузки свой
 * набор названий (сам мод и его зависимости по мере того, как они
 * пошли в работу).
 */
function activeJobFor(gameId, modName = null) {
  const active = state.jobs.filter((job) => job.status === 'active' && job.gameId === gameId);
  if (active.length <= 1 || !modName) return active[0] ?? null;
  const owner = active.find((job) => job.names.has(modName));
  if (owner) return owner;
  // Новое название: достаётся той загрузке, что ещё не получала вестей
  // (самой ранней из них), — это её зависимость или она сама.
  const waiting = active.filter((job) => job.stage === 'prepare').sort((a, b) => a.started - b.started)[0];
  const job = waiting ?? active[active.length - 1];
  job.names.add(modName);
  return job;
}

const STAGE_OF = {
  'install.deps': 'deps',
  'install.download': 'download',
  'install.extract': 'extract',
  'install.exists': 'extract',
  'install.nexus': 'prepare',
  'install.browser': 'browser',
  'loader.lookup': 'prepare',
  'loader.download': 'download',
  'loader.unpack': 'extract',
  'loader.backup': 'extract',
  'loader.install': 'extract',
};

/** Событие прогресса от основного процесса → состояние загрузки. */
function updateJob(job, payload) {
  const stage = STAGE_OF[payload.code];
  if (!stage) return;
  const changed = stage !== job.stage;
  job.stage = stage;
  if (payload.mod) job.current = payload.mod;
  if (payload.index) job.index = payload.index;
  if (payload.total) job.total = payload.total;

  if (stage === 'download') {
    if (changed) {
      job.ratio = null;
      job.lastAt = 0;
    }
    if (typeof payload.ratio === 'number') job.ratio = Math.max(0, Math.min(1, payload.ratio));
    if (typeof payload.bytes === 'number') {
      const now = performance.now();
      if (job.lastAt && payload.bytes >= job.lastBytes) {
        const dt = (now - job.lastAt) / 1000;
        if (dt >= 0.25) {
          const instant = (payload.bytes - job.lastBytes) / dt;
          job.speed = job.speed ? job.speed * 0.65 + instant * 0.35 : instant;
          job.lastAt = now;
          job.lastBytes = payload.bytes;
        }
      } else {
        job.lastAt = now;
        job.lastBytes = payload.bytes;
      }
      job.bytes = payload.bytes;
      if (payload.bytesTotal) job.bytesTotal = payload.bytesTotal;
    }
  } else if (changed) {
    job.ratio = null;
  }
  paintJobs();
}

/** Доля выполненного 0…1, или null — пока неизвестно (бегущая полоса). */
function jobProgress(job) {
  if (job.status === 'done') return 1;
  if (job.status !== 'active') return 0;
  const many = job.total > 1;
  const before = many ? Math.max(0, job.index - 1) / job.total : 0;
  const share = many ? 1 / job.total : 1;
  if (job.stage === 'download' && job.ratio !== null) return before + job.ratio * share * 0.9;
  if (job.stage === 'extract') return before + share * 0.95;
  return many && job.index > 1 ? before : null;
}

function formatBytes(bytes) {
  const n = Number(bytes) || 0;
  if (n < 1024) return `${n} ${t('unit.b')}`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(0)} ${t('unit.kb')}`;
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} ${t('unit.mb')}`;
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} ${t('unit.gb')}`;
}

function formatEta(seconds) {
  const s = Math.max(1, Math.round(seconds));
  if (s < 60) return t('dl.eta.s', { n: s });
  return t('dl.eta.m', { m: Math.floor(s / 60), s: String(s % 60).padStart(2, '0') });
}

/** Короткая подпись на кнопке. */
function jobLabel(job) {
  if (job.status === 'done') return t('dl.done');
  if (job.status === 'error') return t('dl.failed');
  if (job.stage === 'download') {
    const p = jobProgress(job);
    return p === null ? t('dl.download') : `${Math.round(p * 100)}%`;
  }
  return t('dl.' + job.stage);
}

/** Подробная строка в окне загрузок. */
function jobDetail(job) {
  if (job.status === 'done') {
    return job.kind === 'loader' ? t('dl.detail.loaderDone') : t('dl.detail.done', { time: formatClock(job.finished) });
  }
  if (job.status === 'error') return job.error || t('dl.failed');
  const parts = [];
  if (job.total > 1) parts.push(t('dl.detail.of', { i: job.index, n: job.total, name: job.current }));
  if (job.stage === 'download') {
    if (job.bytesTotal) parts.push(t('dl.detail.bytes', { a: formatBytes(job.bytes), b: formatBytes(job.bytesTotal) }));
    else if (job.bytes) parts.push(formatBytes(job.bytes));
    if (job.speed > 1024) {
      parts.push(`${formatBytes(job.speed)}/${t('unit.s')}`);
      if (job.bytesTotal && job.bytes < job.bytesTotal) parts.push(formatEta((job.bytesTotal - job.bytes) / job.speed));
    }
    if (!parts.length || (job.total <= 1 && parts.length === 0)) parts.push(t('dl.download'));
  } else {
    parts.push(t('dl.' + job.stage));
  }
  return parts.join(' · ');
}

function formatClock(time) {
  const lang = window.I18N?.lang === 'en' ? 'en-US' : 'ru-RU';
  return new Date(time).toLocaleTimeString(lang, { hour: '2-digit', minute: '2-digit' });
}

/** Загрузка этого мода, которую стоит показать на его кнопке. */
function visibleJob(gameId, modId) {
  const job = state.jobs.find((j) => j.kind === 'mod' && j.gameId === gameId && j.modId === modId);
  if (!job) return null;
  if (job.status === 'active') return job;
  if (job.status === 'done' && Date.now() - job.finished < DONE_SHOW_MS) return job;
  return null;
}

/** Кнопка «Установить», которая превратилась в прогресс. */
function jobCta(job, cls) {
  const p = jobProgress(job);
  const busy = job.status === 'active';
  const stateCls = `is-${job.status} is-${job.stage}${busy && p === null ? ' is-indeterminate' : ''}`;
  return `
    <span class="${cls} dl ${stateCls}" data-job="${esc(job.key)}" data-status="${job.status}" style="--p:${Math.round((p ?? 0) * 100)}%"
      role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${Math.round((p ?? 0) * 100)}" title="${esc(jobDetail(job))}">
      <span class="dl__fill"></span>
      <span class="dl__icon">${icon(job.status === 'done' ? 'check' : job.status === 'error' ? 'warn' : 'download')}</span>
      <span class="dl__label">${esc(jobLabel(job))}</span>
    </span>`;
}

/** Точечно обновить все места, где видна загрузка. */
function paintJobs() {
  for (const node of document.querySelectorAll('[data-job]')) {
    const job = state.jobs.find((j) => j.key === node.dataset.job);
    if (!job) continue;
    const p = jobProgress(job);
    const pct = Math.round((p ?? 0) * 100);
    node.style.setProperty('--p', `${pct}%`);
    node.setAttribute('aria-valuenow', String(pct));
    node.classList.toggle('is-indeterminate', job.status === 'active' && p === null);
    for (const stage of JOB_STAGES) node.classList.toggle(`is-${stage}`, job.stage === stage);
    for (const status of ['active', 'done', 'error']) node.classList.toggle(`is-${status}`, job.status === status);
    if (node.dataset.status !== job.status) {
      node.dataset.status = job.status;
      const holder = node.querySelector('.dl__icon');
      if (holder) holder.innerHTML = icon(job.status === 'done' ? 'check' : job.status === 'error' ? 'warn' : 'download');
    }
    const label = node.querySelector('.dl__label');
    if (label) label.textContent = jobLabel(job);
    const detail = node.querySelector('.dl__detail');
    if (detail) detail.textContent = jobDetail(job);
    if (node.classList.contains('dl')) node.title = jobDetail(job);
  }
  paintDownloadsButton();
}

function finishJob(job, { ok, error = null, cancelled = false } = {}) {
  if (cancelled) {
    state.jobs = state.jobs.filter((j) => j !== job);
  } else {
    job.status = ok ? 'done' : 'error';
    job.stage = ok ? 'done' : 'error';
    job.error = error;
    job.finished = Date.now();
    job.seen = state.dlOpen;
    if (ok) announceDone(job);
  }
  renderDownloads();
  softRender();
  // Через пару секунд галочка на кнопке сменяется обычным «Установлен».
  setTimeout(softRender, DONE_SHOW_MS + 60);
}

/* --- кнопка в шапке и окно загрузок -------------------------------- */

function paintDownloadsButton() {
  const button = $('#dlBtn');
  if (!button) return;
  const active = state.jobs.filter((job) => job.status === 'active');
  const known = active.map(jobProgress).filter((p) => p !== null);
  const overall = known.length ? known.reduce((a, b) => a + b, 0) / known.length : 0;
  button.classList.toggle('is-busy', active.length > 0);
  button.classList.toggle('is-indeterminate', active.length > 0 && !known.length);
  button.classList.toggle('has-new', !active.length && state.jobs.some((job) => job.status !== 'active' && !job.seen));
  button.classList.toggle('is-open', state.dlOpen);
  button.style.setProperty('--p', `${Math.round(overall * 100)}%`);
  button.style.setProperty('--p-num', String(Math.round(overall * 100)));
  const count = button.querySelector('.dlt__count');
  if (count) count.textContent = active.length > 1 ? String(active.length) : '';
  button.title = active.length ? t('dl.button.busy', { n: active.length }) : t('dl.button');
}

function toggleDownloads(open = !state.dlOpen) {
  state.dlOpen = open;
  const panel = $('#dlPanel');
  if (!panel) return;
  if (open) state.jobs.forEach((job) => (job.status !== 'active' ? (job.seen = true) : null));
  panel.hidden = !open;
  panel.classList.toggle('is-open', open);
  renderDownloads();
}

function jobThumb(job) {
  if (job.kind === 'loader') return `background-image:url('${esc(gameArt(job.gameId, 'icon'))}')`;
  const mod = job.mod ?? knownMod(job.gameId, job.modId);
  return mod ? thumbStyle(mod) : `background-image:url('${esc(gameArt(job.gameId, 'icon'))}')`;
}

function renderDownloads() {
  paintDownloadsButton();
  const panel = $('#dlPanel');
  if (!panel || !state.dlOpen) return;

  const rows = state.jobs
    .map((job) => {
      const game = entry(job.gameId)?.info;
      const p = jobProgress(job);
      const action =
        job.status === 'error' && job.kind === 'mod'
          ? `<button class="btn btn--ghost btn--sm" data-action="install-mod" data-game="${esc(job.gameId)}" data-mod="${esc(job.modId)}">${icon('refresh')}<span>${esc(t('common.retry'))}</span></button>`
          : job.status === 'done' && job.kind === 'mod' && !String(job.modId).startsWith('nxm:')
            ? `<button class="btn btn--ghost btn--sm" data-action="open-mod" data-game="${esc(job.gameId)}" data-mod="${esc(job.modId)}">${esc(t('common.open'))}</button>`
            : '';
      return `
        <article class="dlrow is-${job.status}${job.status === 'active' && p === null ? ' is-indeterminate' : ''}" data-job="${esc(job.key)}" data-status="${job.status}" style="--p:${Math.round((p ?? 0) * 100)}%">
          <span class="dlrow__pic" style="${jobThumb(job)}"><span class="dl__icon">${icon(job.status === 'done' ? 'check' : job.status === 'error' ? 'warn' : 'download')}</span></span>
          <div class="dlrow__body">
            <b title="${esc(job.name)}">${esc(job.name)}</b>
            <span class="dlrow__game">${esc(game?.name ?? '')}${job.kind === 'loader' ? ` · ${esc(t('dl.loader'))}` : ''}</span>
            <span class="dlrow__bar"><i></i></span>
            <span class="dl__detail">${esc(jobDetail(job))}</span>
          </div>
          ${action}
        </article>`;
    })
    .join('');

  panel.innerHTML = `
    <div class="dlp__head">
      <b>${esc(t('dl.title'))}</b>
      ${state.jobs.some((job) => job.status !== 'active') ? `<button class="linkbtn" data-action="dl-clear">${esc(t('dl.clear'))}</button>` : ''}
    </div>
    ${
      rows
        ? `<div class="dlp__list">${rows}</div>`
        : `<div class="dlp__empty">${icon('download')}<p>${esc(t('dl.empty'))}</p><span class="muted small">${esc(t('dl.empty.text'))}</span></div>`
    }`;
}

/* --- по окончании: звук и уведомление Windows ---------------------- */

let audio = null;
function chime() {
  try {
    audio = audio ?? new (window.AudioContext || window.webkitAudioContext)();
    const now = audio.currentTime;
    [659.25, 987.77].forEach((freq, i) => {
      const osc = audio.createOscillator();
      const gain = audio.createGain();
      osc.type = 'sine';
      osc.frequency.value = freq;
      gain.gain.setValueAtTime(0, now + i * 0.11);
      gain.gain.linearRampToValueAtTime(0.07, now + i * 0.11 + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, now + i * 0.11 + 0.42);
      osc.connect(gain).connect(audio.destination);
      osc.start(now + i * 0.11);
      osc.stop(now + i * 0.11 + 0.45);
    });
  } catch {
    /* звук — не главное */
  }
}

function announceDone(job) {
  if (pref('soundDone')) chime();
  const away = document.hidden || (typeof document.hasFocus === 'function' && !document.hasFocus());
  if (pref('notifyDone') && away && typeof Notification !== 'undefined') {
    try {
      const game = entry(job.gameId)?.info?.name ?? '';
      new Notification(t('dl.notify.title'), { body: t('dl.notify.body', { name: job.name, game }), silent: true });
    } catch {
      /* уведомления запрещены — ничего страшного */
    }
  }
}

/* ================================================================== *
 *  Правая панель — как в Modrinth App
 *
 *  На широком экране у ModHub нет пустых полей по бокам: лента экрана
 *  занимает всю ширину, а справа стоит панель, которая меняется вместе
 *  с экраном. На главной — быстрый запуск игр, загрузки, недавно
 *  поставленное, совет и премиум; в каталоге — сортировка и фильтры;
 *  на странице игры — её сводка и быстрые действия; на странице мода —
 *  сведения о нём. На узком окне панель прячется сама (CSS), кнопкой
 *  в шапке её можно убрать и на широком.
 * ================================================================== */

function asideVisible() {
  return pref('aside') !== false && state.view !== 'settings';
}

function asideCard(title, body, { icon: ico = null, extra = '', cls = '' } = {}) {
  return `
    <section class="acard${cls ? ' ' + cls : ''}">
      <header class="acard__head">${ico ? icon(ico) : ''}<h3>${esc(title)}</h3>${extra}</header>
      ${body}
    </section>`;
}

/** Строка быстрого запуска игры: арт, состояние, кнопка. */
function quickGameRow(gameId) {
  const item = entry(gameId);
  const info = item.info;
  const game = item.game;
  const { line, mark } = gameStatus(gameId);
  let action = '';
  if (game?.found && game.loader.installed) {
    action = `<button class="qbtn qbtn--play" type="button" data-action="play" data-game="${esc(gameId)}" title="${esc(t('games.play'))}">${icon('play')}</button>`;
  } else if (game?.found) {
    action = `<button class="qbtn" type="button" data-action="install-loader" data-game="${esc(gameId)}" title="${esc(t('games.installLoader', { loader: game.loader.name }))}">${icon('download')}</button>`;
  } else {
    action = `<button class="qbtn" type="button" data-action="not-found" data-game="${esc(gameId)}" title="${esc(t('games.setPath'))}">${icon('search')}</button>`;
  }
  const count = (state.library[gameId] ?? []).length;
  return `
    <div class="qrow${game?.found ? '' : ' is-off'}" style="${accentStyle(info.accent)}">
      <button class="qrow__main" type="button" data-action="${game?.found ? 'open-game' : 'not-found'}" data-game="${esc(gameId)}">
        <span class="qrow__pic" style="background-image:url('${esc(gameArt(gameId, 'icon'))}')"></span>
        <span class="qrow__text">
          <b>${esc(info.name)}</b>
          <span><i class="dot dot--${mark}"></i>${esc(game?.found && count ? pluralN(count, 'aside.mods') : line)}</span>
        </span>
      </button>
      ${action}
    </div>`;
}

/** Недавно установленные моды — из всех игр, новые первыми. */
function recentInstalls(limit = 5, onlyGame = null) {
  const out = [];
  for (const [gameId, mods] of Object.entries(state.library)) {
    if (onlyGame && gameId !== onlyGame) continue;
    for (const mod of mods ?? []) out.push({ gameId, mod });
  }
  return out
    .filter((x) => x.mod.installedAt)
    .sort((a, b) => String(b.mod.installedAt).localeCompare(String(a.mod.installedAt)))
    .slice(0, limit);
}

function recentBlock(limit = 5, onlyGame = null) {
  const list = recentInstalls(limit, onlyGame);
  if (!list.length) return '';
  const rows = list
    .map(({ gameId, mod }) => {
      const game = entry(gameId)?.game ?? entry(gameId)?.info;
      const picture = mod.icon || state.installedMedia[gameId]?.[mod.id] || null;
      const pic = picture ? `background-image:url('${esc(picture)}')` : gradientStyle(mod);
      const catalogId = game ? catalogIdOf(entry(gameId).game ?? { catalog: {} }, mod) : null;
      const tag = catalogId ? 'button' : 'div';
      const attrs = catalogId ? ` type="button" data-action="open-mod" data-game="${esc(gameId)}" data-mod="${esc(catalogId)}"` : '';
      return `
        <${tag} class="arow"${attrs}>
          <span class="arow__pic" style="${pic}">${picture ? '' : esc(initials(mod.name))}</span>
          <span class="arow__text"><b>${esc(mod.name)}</b><span>${esc(entry(gameId)?.info?.name ?? '')} · ${esc(agoText(mod.installedAt))}</span></span>
          ${mod.enabled === false ? `<span class="arow__off">${esc(t('inst.off'))}</span>` : ''}
        </${tag}>`;
    })
    .join('');
  return asideCard(t('aside.recent'), `<div class="alist">${rows}</div>`, { icon: 'check' });
}

/**
 * «Популярное для игры» — пять самых скачиваемых модов, которых ещё нет:
 * ставятся одной кнопкой прямо из панели, не открывая «Рынок».
 */
function popularBlock(game, limit = 5) {
  const list = (state.home.get(game.id) ?? []).filter((mod) => !isInstalled(mod, game)).slice(0, limit);
  if (!list.length) return '';
  const rows = list
    .map((mod) => {
      const job = visibleJob(game.id, mod.id);
      const button = job
        ? `<span class="qbtn is-busy" title="${esc(t('aside.installing'))}">${icon('refresh')}</span>`
        : `<button class="qbtn qbtn--get" type="button" data-action="install-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}" title="${esc(t('mod.install'))}">${icon('download')}</button>`;
      const generated = coverInfo(mod).kind === 'generated';
      return `
        <div class="qrow qrow--mod">
          <button class="qrow__main" type="button" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">
            <span class="qrow__pic" style="${thumbStyle(mod)}">${generated ? esc(initials(mod.name)) : ''}</span>
            <span class="qrow__text">
              <b>${esc(mod.name)}</b>
              <span>${mod.downloads ? `${icon('download')}${esc(formatCount(mod.downloads))}` : esc(mod.author ? shortAuthors(mod.author) : '')}</span>
            </span>
          </button>
          ${button}
        </div>`;
    })
    .join('');
  return asideCard(t('aside.popular'), `<div class="alist">${rows}</div>`, {
    icon: 'flame',
    extra: `<button class="linkbtn" data-action="tab" data-tab="market">${esc(t('aside.all'))}</button>`,
  });
}

function downloadsBlock() {
  const jobs = state.jobs.slice(0, 3);
  if (!jobs.length) return '';
  const rows = jobs
    .map((job) => {
      const p = jobProgress(job);
      return `
        <div class="arow arow--job dlrow is-${job.status}${job.status === 'active' && p === null ? ' is-indeterminate' : ''}" data-job="${esc(job.key)}" data-status="${job.status}" style="--p:${Math.round((p ?? 0) * 100)}%">
          <span class="arow__pic" style="${jobThumb(job)}"></span>
          <span class="arow__text"><b>${esc(job.name)}</b><span class="dlrow__bar"><i></i></span><span class="dl__detail">${esc(jobDetail(job))}</span></span>
        </div>`;
    })
    .join('');
  return asideCard(t('dl.title'), `<div class="alist">${rows}</div>`, {
    icon: 'download',
    extra: `<button class="linkbtn" data-action="dl-toggle">${esc(t('aside.all'))}</button>`,
  });
}

const TIP_COUNT = 6;
function tipBlock() {
  const n = (Math.floor(Date.now() / (1000 * 60 * 60)) % TIP_COUNT) + 1;
  return asideCard(t('aside.tip'), `<p class="atip">${esc(t('tip.' + n))}</p>`, { icon: 'sparkle', cls: 'acard--tip' });
}

function premiumBlock() {
  if (pref('aside') === false || state.settings.premium) return '';
  return `
    <button class="apremium" type="button" data-action="nav-premium">
      <span class="apremium__crown">${icon('crown')}</span>
      <b>${esc(t('ad.premium.title'))}</b>
      <span>${esc(t('aside.premium.text'))}</span>
      <span class="apremium__go">${esc(t('aside.premium.go'))}${icon('chevRight')}</span>
    </button>`;
}

function homeAside() {
  const games = state.order.map(quickGameRow).join('');
  return (
    asideCard(t('aside.play'), `<div class="alist">${games}</div>`, {
      icon: 'play',
      extra: `<button class="linkbtn" data-action="nav-games">${esc(t('aside.all'))}</button>`,
    }) +
    accountAsideBlock() +
    downloadsBlock() +
    recentBlock(5) +
    tipBlock() +
    premiumBlock()
  );
}

/* --- каталог: сортировка и фильтры -------------------------------- */

const SORTS_BY_KIND = {
  nexus: ['popular', 'rating', 'updated'],
  thunderstore: ['popular', 'rating', 'updated', 'new'],
  modlinks: ['popular', 'name'],
};

function catalogAside(game) {
  const kind = game.catalog?.kind ?? 'nexus';
  const sorts = SORTS_BY_KIND[kind] ?? ['popular'];
  if (!sorts.includes(state.catalogSort)) state.catalogSort = 'popular';
  const sortList = sorts
    .map(
      (id) => `
        <button class="aopt${state.catalogSort === id ? ' is-active' : ''}" type="button" role="radio" aria-checked="${state.catalogSort === id}" data-action="cat-sort" data-sort="${id}">
          <i class="aopt__radio"></i><span>${esc(t('sort.' + id))}</span>
        </button>`
    )
    .join('');

  const view = pref('catalogView');
  const viewSwitch = `
    <div class="seg seg--full">
      <button class="seg__btn${view === 'grid' ? ' is-active' : ''}" data-action="set-pref" data-key="catalogView" data-value="grid">${icon('grid')}<span>${esc(t('view.grid'))}</span></button>
      <button class="seg__btn${view === 'list' ? ' is-active' : ''}" data-action="set-pref" data-key="catalogView" data-value="list">${icon('list')}<span>${esc(t('view.list'))}</span></button>
    </div>`;

  const cats = state.catalogCategories[game.id] ?? [];
  const catBlock = cats.length
    ? asideCard(
        t('aside.categories'),
        `<div class="acats">
          <button class="aopt${!state.catalogCategory ? ' is-active' : ''}" data-action="cat-category" data-category=""><i class="aopt__radio"></i><span>${esc(t('aside.categories.all'))}</span></button>
          ${cats
            .slice(0, 14)
            .map(
              (c) => `<button class="aopt${state.catalogCategory === c.name ? ' is-active' : ''}" data-action="cat-category" data-category="${esc(c.name)}"><i class="aopt__radio"></i><span>${esc(tagLabel(c.name))}</span><em>${c.count}</em></button>`
            )
            .join('')}
        </div>`,
        { icon: 'tag' }
      )
    : '';

  const total = state.catalogTotals[game.id] || (state.catalogFor === game.id && !state.catalogMeta.query ? state.catalogMeta.total : 0) || 0;
  const source = { nexus: 'Nexus Mods', thunderstore: 'Thunderstore', modlinks: 'ModLinks' }[kind] ?? '';
  return (
    asideCard(t('aside.sort'), `<div class="aopts" role="radiogroup">${sortList}</div>`, { icon: 'sort' }) +
    asideCard(
      t('aside.display'),
      `${viewSwitch}
       <div class="aline">
         <span>${esc(t('aside.hideInstalled'))}</span>
         ${toggle('hideInstalled', pref('hideInstalled'))}
       </div>`,
      { icon: 'eye' }
    ) +
    catBlock +
    asideCard(
      t('aside.source'),
      `<p class="asource"><b>${esc(source)}</b><span class="muted small">${esc(total ? t('home.inCatalog', { n: total.toLocaleString(window.I18N.lang === 'en' ? 'en-US' : 'ru-RU') }) : '')}</span></p>
       ${game.catalog?.browseUrl ? `<button class="btn btn--ghost btn--sm btn--block" data-action="open-url" data-url="${esc(game.catalog.browseUrl)}">${icon('external')}<span>${esc(t('aside.openSite', { site: source }))}</span></button>` : ''}`,
      { icon: 'shop' }
    ) +
    downloadsBlock()
  );
}

/* --- страница игры ------------------------------------------------- */

function gameAside(game) {
  const mods = state.library[game.id] ?? state.mods ?? [];
  const enabled = mods.filter((m) => m.enabled !== false).length;
  const stats = `
    <div class="astats">
      <div><b>${mods.length}</b><span>${esc(t('aside.stat.installed'))}</span></div>
      <div><b>${enabled}</b><span>${esc(t('aside.stat.enabled'))}</span></div>
      <div class="${state.problems.length ? 'is-warn' : ''}"><b>${state.problems.length}</b><span>${esc(t('aside.stat.problems'))}</span></div>
    </div>`;
  const actions = [
    ['tab', 'market', 'shop', t('games.market')],
    ['install-file', null, 'folder', t('inst.fromFile')],
    ['tab', 'log', 'warn', t('games.log')],
    ['rescan', null, 'refresh', t('games.detectAgain')],
  ]
    .map(
      ([action, tab, ico, label]) =>
        `<button class="aact" type="button" data-action="${action}"${tab ? ` data-tab="${tab}"` : ''} data-game="${esc(game.id)}">${icon(ico)}<span>${esc(label)}</span></button>`
    )
    .join('');
  return (
    asideCard(game.name, stats + `<div class="aacts">${actions}</div>`, { icon: 'grid' }) +
    downloadsBlock() +
    (state.gameTab === 'market' ? '' : recentBlock(4, game.id)) +
    (state.gameTab === 'market' ? '' : popularBlock(game)) +
    tipBlock()
  );
}

/* --- страница мода --------------------------------------------------- */

function modAside() {
  const view = state.modView;
  const mod = view?.mod;
  const game = entry(view?.gameId)?.game;
  if (!mod || !game) return homeAside();
  const facts = [
    [t('aside.fact.game'), game.name],
    [t('aside.fact.author'), mod.author ? shortAuthors(mod.author) : '—'],
    [t('aside.fact.version'), mod.version || '—'],
    [t('aside.fact.updated'), mod.updatedAt ? `${formatMonth(mod.updatedAt)} · ${agoText(mod.updatedAt)}` : '—'],
    [t('aside.fact.downloads'), mod.downloads ? formatCount(mod.downloads) : '—'],
    [t('aside.fact.loader'), game.loader?.name ?? '—'],
    [t('aside.fact.source'), { nexus: 'Nexus Mods', thunderstore: 'Thunderstore', modlinks: 'ModLinks' }[game.catalog?.kind] ?? '—'],
  ];
  const score = ratingOf(game.id, mod.id);
  const cats = (mod.categories ?? []).filter(Boolean);
  const badges = modBadges(mod, game.id);
  const reqs = view.details?.requirements ?? [];
  const reqRows = reqs.length
    ? reqs
        .slice(0, 8)
        .map(
          (r) => `<button class="arow" type="button" ${r.id && r.available !== false ? `data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(r.id)}"` : 'disabled'}>
            <span class="arow__pic" style="${gradientStyle({ id: r.id || r.name, name: r.name })}">${esc(initials(r.name))}</span>
            <span class="arow__text"><b>${esc(r.name)}</b><span>${esc(isInstalled({ id: r.id }, game) ? t('mod.installed') : t('aside.dep'))}</span></span>
          </button>`
        )
        .join('')
    : `<p class="muted small">${esc(view.details ? t('aside.noDeps') : t('common.loading'))}</p>`;
  return (
    asideCard(
      t('aside.about'),
      `<dl class="afacts">${facts.map(([k, v]) => `<dt>${esc(k)}</dt><dd>${esc(v)}</dd>`).join('')}</dl>`,
      { icon: 'info' }
    ) +
    asideCard(
      t('rev.title'),
      `<div class="ascore${score.count ? '' : ' is-empty'}"><b>${esc(formatScore(score.avg))}</b>${ratingLine(game.id, mod.id)}</div>
       <button class="btn btn--ghost btn--sm btn--block" data-action="goto-reviews">${icon('chat')}<span>${esc(t('rev.write'))}</span></button>`,
      { icon: 'star' }
    ) +
    (cats.length || badges.length
      ? asideCard(
          t('aside.tags'),
          `<div class="achips">${badges.map(badgeTag).join('')}${cats.map((c) => `<span class="chip">${esc(tagLabel(c))}</span>`).join('')}</div>`,
          { icon: 'tag' }
        )
      : '') +
    asideCard(t('aside.deps'), `<div class="alist">${reqRows}</div>`, { icon: 'puzzle' }) +
    downloadsBlock()
  );
}

function renderAside() {
  const aside = $('#aside');
  if (!aside) return;
  const show = asideVisible();
  document.body.classList.toggle('has-aside', show);
  const button = $('#asideBtn');
  if (button) {
    button.classList.toggle('is-active', pref('aside') !== false);
    button.title = t(pref('aside') !== false ? 'aside.hide' : 'aside.show');
    button.setAttribute('aria-label', button.title);
  }
  if (!show) {
    aside.innerHTML = '';
    return;
  }
  const game = activeGame();
  let html;
  if (state.view === 'mod') html = modAside();
  else if (state.view === 'popular' && game) html = catalogAside(game);
  else if (state.view === 'game' && game?.found) html = state.gameTab === 'market' ? catalogAside(game) : gameAside(game);
  else html = homeAside();
  // Панель «въезжает» только когда сменился её смысл (другой экран или
  // игра), а не при каждой перерисовке — иначе она бы мигала.
  const kind = `${state.view}:${state.activeGameId ?? ''}:${state.view === 'game' && state.gameTab === 'market' ? 'm' : ''}`;
  const still = aside.dataset.kind === kind;
  aside.dataset.kind = kind;
  const scroll = aside.scrollTop;
  aside.innerHTML = `<div class="aside__inner${still ? ' is-still' : ''}">${html}</div>`;
  aside.scrollTop = still ? scroll : 0;
}

/* --- «где я»: хлебные крошки в шапке ------------------------------- */

function renderCrumbs() {
  const node = $('#crumbs');
  if (!node) return;
  const game = entry(state.activeGameId)?.info;
  const crumb = (label, action = null, attrs = '') =>
    action ? `<button class="crumb" type="button" data-action="${action}"${attrs}>${esc(label)}</button>` : `<span class="crumb is-here">${esc(label)}</span>`;
  const gameCrumb = game ? crumb(game.name, 'open-game', ` data-game="${esc(game.id)}"`) : '';
  let parts = [];
  switch (state.view) {
    case 'home':
      parts = [crumb(t('nav.menu'))];
      break;
    case 'games':
      parts = [crumb(t('nav.menu'), 'nav-home'), crumb(t('games.title'))];
      break;
    case 'popular':
      parts = [gameCrumb, crumb(state.query ? `«${state.query}»` : t('popular.title'))];
      break;
    case 'game':
      parts = [gameCrumb, crumb(t({ downloads: 'games.downloads', market: 'games.market', log: 'games.log' }[state.gameTab] ?? 'games.downloads'))];
      break;
    case 'mod':
      parts = [
        state.modView?.gameId ? crumb(entry(state.modView.gameId)?.info?.name ?? '', 'open-game', ` data-game="${esc(state.modView.gameId)}"`) : '',
        crumb(state.modView?.mod?.name ?? '…'),
      ];
      break;
    case 'settings':
      parts = [crumb(t('settings.title'), state.settingsTab !== 'look' ? 'settings-tab' : null, ' data-tab="look"'), crumb(t('settings.tab.' + state.settingsTab))];
      break;
    case 'notfound':
      parts = [gameCrumb, crumb(t('games.notDetected'))];
      break;
    case 'premium':
      parts = [crumb(t('nav.premium'))];
      break;
    case 'donate':
      parts = [crumb(t('nav.donate'))];
      break;
    default:
      parts = [crumb(t('nav.menu'))];
  }
  node.innerHTML = parts.filter(Boolean).join(`<span class="crumbs__sep">${icon('chevRight')}</span>`);
}

/* --- каталог списком, как в Modrinth -------------------------------- */

function modRow(mod, game, index = 0, { rank = -1 } = {}) {
  const installed = isInstalled(mod, game);
  const job = visibleJob(game.id, mod.id);
  const cats = (mod.categories ?? []).filter(Boolean).slice(0, 3);
  const cta = job
    ? jobCta(job, 'getbtn getbtn--sm')
    : installed
      ? `<span class="getbtn getbtn--sm is-done">${icon('check')}<span>${esc(t('mod.installed'))}</span></span>`
      : `<button class="getbtn getbtn--sm" type="button" data-action="install-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">${icon('download')}<span>${esc(t('mod.install'))}</span></button>`;
  return `
    <article class="mrow reveal" style="--i:${index % 12};${accentStyle(game.accent)}" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">
      <span class="mrow__pic" style="${thumbStyle(mod)}">${coverInfo(mod).kind === 'generated' ? esc(initials(mod.name)) : ''}</span>
      <div class="mrow__body">
        <div class="mrow__title"><h3 title="${esc(mod.name)}">${esc(mod.name)}</h3>${mod.author ? `<span>${esc(t('mod.by', { author: shortAuthors(mod.author) }))}</span>` : ''}</div>
        <p class="mrow__text">${esc(mod.description || '')}</p>
        <div class="mrow__tags">${installed ? '' : modBadges(mod, game.id, { rank }).map(badgeTag).join('')}${cats.map((c) => `<span class="chip">${esc(tagLabel(c))}</span>`).join('')}</div>
      </div>
      <div class="mrow__side">
        ${cta}
        <span class="mrow__stats">
          ${mod.downloads ? `<span class="meta">${icon('download')}${esc(formatCount(mod.downloads))}</span>` : ''}
          ${mod.updatedAt ? `<span class="meta">${icon('refresh')}${esc(agoText(mod.updatedAt))}</span>` : ''}
        </span>
        ${ratingLine(game.id, mod.id, { compact: true })}
      </div>
    </article>`;
}

async function loadCategories(game) {
  if (!game || game.catalog?.kind !== 'modlinks' || state.catalogCategories[game.id]) return;
  const list = await call(api.catalog.categories(game.id), { silent: true }).catch(() => null);
  if (Array.isArray(list) && list.length) {
    state.catalogCategories[game.id] = list;
    renderAside();
  }
}

/* ================================================================== *
 *  Рельс и шапка
 * ================================================================== */

function renderRail() {
  const container = $('#railGames');
  container.innerHTML = '';

  for (const gameId of state.order) {
    const item = entry(gameId);
    const info = item.info;

    const status =
      item.status === 'searching'
        ? 'busy'
        : !item.game?.found
          ? 'off'
          : item.game.loader.installed
            ? 'ok'
            : 'warn';

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'rail__btn rail__btn--game';
    button.style.cssText = accentStyle(info.accent);
    const here = (state.view === 'game' || state.view === 'notfound') && gameId === state.activeGameId;
    if (here) button.classList.add('is-active');
    if (status === 'off') button.classList.add('is-off');
    button.dataset.action = 'open-game';
    button.dataset.game = gameId;
    // Значок игры — её арт, как иконка приложения в магазине; буквы — запасной вариант.
    button.innerHTML =
      `<span class="rail__initials">${esc(initials(info.shortName))}</span>` +
      `<span class="rail__art" style="background-image:url('${esc(gameArt(gameId, 'icon'))}')"></span>` +
      `<span class="dot dot--${status}"></span>` +
      `<span class="rail__tip">${esc(info.name)}</span>`;
    container.append(button);
  }

  for (const [id, key] of [
    ['navPremium', 'nav.premium'],
    ['navHome', 'nav.menu'],
    ['navDonate', 'nav.donate'],
    ['navSettings', 'nav.settings'],
  ]) {
    const node = document.getElementById(id);
    node.querySelector('.rail__tip').textContent = t(key);
  }

  $('#navPremium').classList.toggle('is-active', state.view === 'premium');
  $('#navHome').classList.toggle('is-active', state.view === 'home' || state.view === 'games');
  $('#navDonate').classList.toggle('is-active', state.view === 'donate');
  $('#navSettings').classList.toggle('is-active', state.view === 'settings' && state.settingsTab !== 'accounts');
  renderAccountRail();

  // Иконки рисуются здесь, а не в разметке: так они переживают смену языка.
  for (const node of document.querySelectorAll('[data-icon]')) {
    if (!node.firstElementChild) node.innerHTML = icon(node.dataset.icon);
  }
}

function renderTopbar() {
  const game = activeGame();
  const input = $('#searchInput');
  input.placeholder = game ? t('search.game', { game: game.name }) : t('search.home');
  if (input.value !== state.query) input.value = state.query;

  // Стрелки истории: неактивная стрелка — значит, идти туда некуда.
  const back = $('#navBack');
  const forward = $('#navForward');
  back.disabled = !canGoBack();
  forward.disabled = !canGoForward();
  back.title = t('nav.back');
  forward.title = t('nav.forward');
  back.setAttribute('aria-label', t('nav.back'));
  forward.setAttribute('aria-label', t('nav.forward'));

  // Справа в шапке — реклама. У премиума её нет.
  $('#adSlot').hidden = adsHidden() || !state.ads.items.length;
}

/* --- реклама в шапке ------------------------------------------------ */

/**
 * Место справа в шапке. Объявления меняются сами раз в rotate секунд,
 * а пока на них наведена мышь — стоят. Слово «Реклама» рядом — не
 * украшение: по нему видно, что это не часть интерфейса, и по клику
 * человек узнаёт, как её убрать (премиум).
 *
 * Отрисовывается отдельно от экрана: иначе каждая перерисовка — пришли
 * оценки, нашлась игра — перезапускала бы объявление.
 */
function adsHidden() {
  return Boolean(state.settings.premium);
}

async function loadAds() {
  const feed = api.ads?.list ? await call(api.ads.list(), { silent: true }).catch(() => null) : null;
  state.ads.feed = feed;
  state.ads.rotate = Number(feed?.rotate) || 15;
  rebuildAds();
}

function rebuildAds() {
  state.ads.items = window.ModHubAds?.buildAds(state.ads.feed, window.I18N.lang) ?? [];
  state.ads.index = state.ads.items.length ? state.ads.index % state.ads.items.length : 0;
  renderAd();
}

function currentAd() {
  return state.ads.items[state.ads.index] ?? null;
}

function renderAd({ animate = false } = {}) {
  const slot = $('#adSlot');
  const ad = currentAd();
  if (!ad || adsHidden()) {
    slot.innerHTML = '';
    slot.hidden = true;
    return;
  }
  slot.hidden = false;

  const title = ad.remote ? ad.title : t(ad.title);
  const text = ad.remote ? ad.text : t(ad.text);
  const pic = ad.image
    ? `<span class="ad__pic ad__pic--img" style="background-image:url('${esc(ad.image)}')"></span>`
    : `<span class="ad__pic ad__pic--${esc(ad.kind ?? 'house')}">${icon(ad.icon ?? 'megaphone')}</span>`;

  slot.innerHTML = `
    <button class="ad${animate ? ' is-swap' : ''}" type="button" data-action="ad-open" title="${esc(text ? `${title} — ${text}` : title)}">
      ${pic}
      <span class="ad__body">
        <b class="ad__title">${esc(title)}</b>
        ${text ? `<span class="ad__text">${esc(text)}</span>` : ''}
      </span>
      ${ad.remote || ad.url ? `<span class="ad__go">${icon('external')}</span>` : `<span class="ad__go">${icon('chevRight')}</span>`}
    </button>
    <button class="ad__tag" type="button" data-action="ad-why" title="${esc(t('ad.why'))}">${esc(t('ad.label'))}</button>`;
}

function rotateAd() {
  if (state.ads.items.length < 2 || adsHidden()) return;
  const slot = $('#adSlot');
  if (slot.matches?.(':hover') || slot.contains(document.activeElement)) return;
  state.ads.index = (state.ads.index + 1) % state.ads.items.length;
  renderAd({ animate: true });
}

let adTimer = null;
function startAdRotation() {
  clearInterval(adTimer);
  adTimer = setInterval(rotateAd, Math.max(6, state.ads.rotate) * 1000);
}

/* ================================================================== *
 *  Отрисовка
 * ================================================================== */

function render() {
  renderTopbar();
  renderCrumbs();
  renderAside();
  const main = $('#main');

  const screens = {
    home: renderHome,
    games: renderGames,
    notfound: renderNotFound,
    popular: renderPopular,
    mod: renderModPage,
    game: renderGame,
    premium: renderPremium,
    donate: renderDonate,
    settings: renderSettings,
  };

  const view = screens[state.view] ?? renderHome;
  const key = `${state.view}:${state.activeGameId ?? ''}:${state.gameTab}:${state.modView?.modId ?? ''}`;
  const fresh = key !== lastViewKey;
  lastViewKey = key;

  // Прокрутку сохраняем, если это тот же экран: иначе «Показать ещё»
  // или пришедшее описание мода подбрасывали бы ленту наверх.
  // Шапка игры не «выезжает» заново, пока игра та же: переключили вкладку,
  // пришёл каталог, сменили порядок — логотип и кадры остаются на месте.
  const sceneKey = `${state.view}:${state.activeGameId ?? ''}:${state.modView?.modId ?? ''}`;
  main.classList.toggle('is-still', sceneKey === lastSceneKey);
  lastSceneKey = sceneKey;

  const keepScroll = fresh ? 0 : main.scrollTop;
  main.innerHTML = view();
  main.scrollTop = keepScroll;
  if (fresh) main.firstElementChild?.classList.add('is-entering');
  applyReveal(main, fresh);
  probeCovers(main);
}

/**
 * Анимация появления.
 *
 * Блоки выплывают, когда до них долистали, карточки — лесенкой. Но только
 * в первый раз: экран перерисовывается часто (идёт поиск игры, пришёл
 * каталог), и если бы всё каждый раз выплывало заново, окно бы мигало.
 * Поэтому помним, какие карточки уже показаны на этом экране.
 */
let lastViewKey = '';
let lastSceneKey = '';
let revealObserver = null;
const seenCards = new Set();

function applyReveal(root, fresh) {
  if (fresh) seenCards.clear();

  const pending = [];
  root.querySelectorAll('.reveal').forEach((node) => {
    const cardKey = node.dataset.mod ? `${node.dataset.game}:${node.dataset.mod}` : null;
    const animate = cardKey ? !seenCards.has(cardKey) : fresh;
    if (cardKey) seenCards.add(cardKey);
    if (animate) pending.push(node);
    else node.classList.add('is-in', 'is-static');
  });

  if (!pending.length) return;
  if (typeof IntersectionObserver === 'undefined') {
    pending.forEach((node) => node.classList.add('is-in'));
    return;
  }

  revealObserver?.disconnect();
  revealObserver = new IntersectionObserver(
    (entries) => {
      for (const item of entries) {
        if (!item.isIntersecting) continue;
        item.target.classList.add('is-in');
        revealObserver.unobserve(item.target);
      }
    },
    { root, rootMargin: '0px 0px -4% 0px', threshold: 0.05 }
  );
  pending.forEach((node) => revealObserver.observe(node));
}

/* --- главная: витрина как в Microsoft Store ------------------------ */

/**
 * Главная собрана как витрина магазина:
 *   — сверху большой баннер-карусель с лучшими модами каждой игры и
 *     миниатюрами под ним, по которым можно переключаться;
 *   — «Ваши игры» — крупные карточки с артом и состоянием;
 *   — «Рекомендуем» — мозаика плиток разного размера из всех игр;
 *   — «Топ модов» с переключателем игр, как чарты в магазине;
 *   — полки «Популярное» по играм, которые листаются стрелками;
 *   — «Свежие обновления».
 *
 * Надписи на плитках не выдуманы — у каждой своё правило:
 *   «Выбор ModHub» — мод из нашего короткого списка проверенных;
 *   «Лучшее»       — средняя оценка ModHub от 4.5, а пока отзывов нет —
 *                    больше всех лайков / одобрений среди популярных игры;
 *   «Хит»          — первое место по популярности в своей игре;
 *   «Новое»        — обновлялся за последние две недели.
 */

const BANNERS = [
  { title: 'home.hero.title', text: 'home.hero.text', art: 0 },
  { title: 'home.hero2.title', text: 'home.hero2.text', art: 1 },
  { title: 'home.hero3.title', text: 'home.hero3.text', art: 2 },
];

/** Проверенные моды, которые ModHub советует новичку первыми. */
const EDITORS = {
  'stardew-valley': ['lookupanything', 'npcmaplocations', 'automate', 'cjbcheatsmenu', 'chestsanywhere', 'uiinfosuite2'],
  'hollow-knight': ['customknight', 'benchwarp', 'palecourt', 'randomizer4', 'hkmp', 'qol'],
  'lethal-company': ['morecompany', 'shiploot', 'latecompany', 'moresuits', 'lethalthings', 'lategameupgrades'],
};

const HERO_SECONDS = 8;
const DAY_MS = 24 * 60 * 60 * 1000;

function normName(value) {
  return String(value ?? '')
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '');
}

function isEditorsPick(gameId, mod) {
  const list = EDITORS[gameId];
  return Boolean(list && mod && (list.includes(normName(mod.name)) || list.includes(normName(mod.id))));
}

function daysAgo(value) {
  const time = value ? Date.parse(value) : NaN;
  return Number.isNaN(time) ? Infinity : Math.max(0, (Date.now() - time) / DAY_MS);
}

/** «сегодня», «3 дня назад». */
function agoText(value) {
  const days = Math.floor(daysAgo(value));
  if (!Number.isFinite(days)) return '';
  return days < 1 ? t('time.today') : pluralN(days, 'time.daysAgo');
}

/** Мод с наибольшим числом лайков / одобрений среди популярных в игре. */
function mostLiked(gameId) {
  let best = null;
  for (const mod of state.home.get(gameId) ?? []) {
    if ((Number(mod.rating) || 0) > (Number(best?.rating) || 0)) best = mod;
  }
  return best?.id ?? null;
}

/**
 * Надписи на плитке мода — не больше двух, самые весомые первыми.
 * @param {object} mod
 * @param {string} gameId
 * @param {{rank?: number}} [where] rank — место в списке по популярности
 */
function modBadges(mod, gameId, { rank = -1 } = {}) {
  const out = [];
  const score = ratingOf(gameId, mod.id);
  if (isEditorsPick(gameId, mod)) out.push('pick');
  if ((score.count > 0 && score.avg >= 4.5) || (score.count === 0 && mostLiked(gameId) === mod.id)) out.push('best');
  if (rank === 0) out.push('hit');
  if (daysAgo(mod.updatedAt) <= 14) out.push('new');
  return out.slice(0, 2);
}

const BADGE_ICON = { pick: 'logo', best: 'trophy', hit: 'flame', new: 'sparkle' };

function badgeRow(badges) {
  if (!badges.length) return '';
  return `<span class="tags">${badges.map(badgeTag).join('')}</span>`;
}

function badgeTag(kind) {
  return `<span class="tagx tagx--${kind}">${icon(BADGE_ICON[kind])}<span>${esc(t('badge.' + kind))}</span></span>`;
}

/** Значок игры: кружок с её артом и, если нужно, название. */
function gameChip(gameId, { withName = true } = {}) {
  const info = entry(gameId)?.info;
  if (!info) return '';
  return `
    <span class="gchip${withName ? '' : ' gchip--dot'}" style="${accentStyle(info.accent)}" title="${esc(info.name)}">
      <i style="background-image:url('${esc(gameArt(gameId, 'icon'))}')"></i>${withName ? `<span>${esc(info.name)}</span>` : ''}
    </span>`;
}

/** Маленькая квадратная картинка мода: для миниатюр и топа. */
function thumbStyle(mod) {
  const cover = coverInfo(mod);
  if (cover.url) return `background-image:url('${esc(cover.url)}')`;
  if (cover.kind === 'art') return artStyle(mod, cover.art);
  return gradientStyle(mod);
}

/** Кнопка «Установить»; iconOnly — круглая, только значок (для тесного топа). */
function installButton(mod, game, { size = '', iconOnly = false } = {}) {
  const label = isInstalled(mod, game) ? t('mod.installed') : t('mod.install');
  const text = iconOnly ? '' : `<span>${esc(label)}</span>`;
  const cls = `getbtn${size}${iconOnly ? ' getbtn--icon' : ''}`;
  const job = visibleJob(game.id, mod.id);
  if (job) return jobCta(job, cls);
  if (isInstalled(mod, game)) {
    return `<span class="${cls} is-done" title="${esc(label)}">${icon('check')}${text}</span>`;
  }
  return `<button class="${cls}" type="button" title="${esc(label)}" data-action="install-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">${icon('download')}${text}</button>`;
}

function downloadsMeta(mod) {
  return mod.downloads ? `<span class="meta">${icon('download')}${esc(formatCount(mod.downloads))}</span>` : '';
}

/** Популярные моды всех игр вперемешку: первые места каждой игры, потом вторые… */
function homeFeed(source = state.home) {
  const lists = readyGames()
    .map((item) => ({ game: item.game, mods: source.get(item.game.id) ?? [] }))
    .filter((list) => list.mods.length);
  const out = [];
  const longest = Math.max(0, ...lists.map((list) => list.mods.length));
  for (let rank = 0; rank < longest; rank += 1) {
    for (const { game, mods } of lists) if (mods[rank]) out.push({ mod: mods[rank], game, rank });
  }
  return out;
}

const feedKey = (item) => `${item.game.id}|${item.mod.id}`;

/** Кого показать в большом баннере: по одному лучшему от каждой игры. */
function heroPicks(feed) {
  const picks = [];
  const used = new Set();
  for (const item of readyGames()) {
    const own = feed.filter((x) => x.game.id === item.game.id);
    const choice = own.find((x) => isEditorsPick(x.game.id, x.mod)) ?? own[0];
    if (choice) {
      picks.push(choice);
      used.add(feedKey(choice));
    }
  }
  for (const x of feed) {
    if (picks.length >= 4) break;
    if (used.has(feedKey(x))) continue;
    picks.push(x);
    used.add(feedKey(x));
  }
  return picks.slice(0, 5);
}

function heroSlides(feed) {
  const picks = heroPicks(feed);
  if (!picks.length) return BANNERS.map((banner) => ({ kind: 'brand', banner }));
  return [{ kind: 'brand', banner: BANNERS[0] }, ...picks.map((pick) => ({ kind: 'mod', ...pick }))];
}

function heroSlide(slide, index, active) {
  const attrs = `data-slide="${index}"${active ? '' : ' aria-hidden="true" inert'}`;
  const cls = `spot__slide${active ? ' is-active' : ''}`;

  if (slide.kind === 'brand') {
    const banner = slide.banner;
    return `
      <article class="${cls} spot__slide--brand" ${attrs}>
        <div class="spot__bg spot__bg--photo" style="background-image:url('${esc(bannerArt(banner.art))}')"></div>
        <div class="spot__text">
          <span class="tags"><span class="tagx tagx--brand">${icon('logo')}<span>ModHub</span></span></span>
          <h1>${esc(t(banner.title))}</h1>
          <p class="spot__desc">${esc(t(banner.text))}</p>
          <div class="spot__actions">
            <button class="btn btn--primary btn--lg" data-action="nav-games">${icon('grid')}<span>${esc(t('home.hero.action'))}</span></button>
          </div>
        </div>
        ${brandArt()}
      </article>`;
  }

  const { mod, game, rank } = slide;
  const by = mod.author ? t('mod.by', { author: shortAuthors(mod.author) }) : game.name;
  return `
    <article class="${cls}" ${attrs} style="${accentStyle(game.accent)}">
      <div class="spot__bg" style="${backdropStyle(mod)}"></div>
      <div class="spot__text">
        <span class="tags">${modBadges(mod, game.id, { rank }).map(badgeTag).join('')}${gameChip(game.id)}</span>
        <h1 title="${esc(mod.name)}">${esc(mod.name)}</h1>
        <p class="spot__by">${esc(by)}</p>
        <p class="spot__desc">${esc(mod.description || '')}</p>
        <div class="spot__meta">${ratingLine(game.id, mod.id)}${downloadsMeta(mod)}</div>
        <div class="spot__actions">
          ${installButton(mod, game, { size: ' getbtn--lg' })}
          <button class="btn btn--ghost btn--lg" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">${esc(t('home.more'))}</button>
        </div>
      </div>
      <div class="spot__art" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">
        ${coverBlock(mod, { className: 'spot__cover' + (mod.iconShape === 'square' ? ' is-square' : '') })}
      </div>
    </article>`;
}

/**
 * Правая часть приветственного слайда. Есть настоящие картинки игр —
 * веер из обложек трёх игр с их логотипами; нет — большой знак ModHub.
 */
function brandArt() {
  const games = state.order.filter((id) => steamArt(id, 'icon')).slice(0, 3);
  if (games.length < 2) return `<div class="spot__art spot__art--logo" aria-hidden="true">${icon('logo')}</div>`;
  return `
    <div class="spot__art spot__art--fan" style="--n:${games.length}">
      ${games
        .map((id, i) => {
          const logo = gameLogo(id);
          const found = entry(id)?.game?.found;
          return `<button class="fan__card" type="button" style="--i:${i};background-image:url('${esc(gameArt(id, 'icon'))}')" data-action="${found ? 'open-game' : 'not-found'}" data-game="${esc(id)}" title="${esc(entry(id)?.info?.name ?? id)}">
            ${logo ? `<img src="${esc(logo)}" alt="" referrerpolicy="no-referrer" />` : `<b>${esc(entry(id)?.info?.name ?? id)}</b>`}
          </button>`;
        })
        .join('')}
    </div>`;
}

function heroThumb(slide, index, active) {
  const style =
    slide.kind === 'brand' ? `background-image:url('${esc(bannerArt(slide.banner.art))}')` : thumbStyle(slide.mod);
  const name = slide.kind === 'brand' ? t(slide.banner.title) : slide.mod.name;
  const sub = slide.kind === 'brand' ? 'ModHub' : slide.game.name;
  return `
    <button class="spot__thumb${active ? ' is-active' : ''}" type="button" data-action="hero" data-index="${index}" style="--hero:${HERO_SECONDS}s">
      <span class="spot__thumbpic" style="${style}"></span>
      <span class="spot__thumbtext"><b>${esc(name)}</b><span>${esc(sub)}</span></span>
      <i class="spot__timer"></i>
    </button>`;
}

function heroBlock(feed) {
  const slides = heroSlides(feed);
  state.heroCount = slides.length;
  if (state.hero >= slides.length) state.hero = 0;
  return `
    <section class="spot${pref('heroAutoplay') ? '' : ' is-manual'}" id="spot">
      <div class="spot__stage">
        ${slides.map((slide, i) => heroSlide(slide, i, i === state.hero)).join('')}
        <button class="spot__nav spot__nav--prev" type="button" data-action="hero-step" data-dir="-1" aria-label="${esc(t('home.prev'))}">${icon('chevLeft')}</button>
        <button class="spot__nav spot__nav--next" type="button" data-action="hero-step" data-dir="1" aria-label="${esc(t('home.next'))}">${icon('chevRight')}</button>
      </div>
      <div class="spot__thumbs" style="--n:${slides.length}">
        ${slides.map((slide, i) => heroThumb(slide, i, i === state.hero)).join('')}
      </div>
    </section>`;
}

/** Переключить слайд без перерисовки всей главной: плавно и без мигания. */
function showHero(index) {
  const count = state.heroCount || 1;
  state.hero = ((index % count) + count) % count;
  const root = document.getElementById('spot');
  if (!root) return;
  root.querySelectorAll('.spot__slide').forEach((node) => {
    const on = Number(node.dataset.slide) === state.hero;
    node.classList.toggle('is-active', on);
    if (on) {
      node.removeAttribute('aria-hidden');
      node.removeAttribute('inert');
    } else {
      node.setAttribute('aria-hidden', 'true');
      node.setAttribute('inert', '');
    }
  });
  root.querySelectorAll('.spot__thumb').forEach((node) => {
    const on = Number(node.dataset.index) === state.hero;
    node.classList.remove('is-active');
    if (on) {
      // Перезапуск полоски-таймера: класс снимается и ставится заново.
      void node.offsetWidth;
      node.classList.add('is-active');
    }
  });
  heroTimerAt = Date.now();
}

let heroTimerAt = Date.now();
/**
 * Баннер листается сам раз в HERO_SECONDS. Пока на него наведена мышь или
 * открыт диалог — стоит (и полоска-таймер под миниатюрой тоже стоит).
 */
function rotateHero() {
  const root = document.getElementById('spot');
  if (!pref('heroAutoplay')) {
    heroTimerAt = Date.now();
    return;
  }
  if (state.view !== 'home' || !root || !$('#modal').hidden || root.matches?.(':hover') || document.hidden) {
    heroTimerAt += 1000;
    return;
  }
  if (Date.now() - heroTimerAt < HERO_SECONDS * 1000 - 300) return;
  showHero(state.hero + 1);
}

/* --- «Ваши игры» ---------------------------------------------------- */

function gameStatus(gameId) {
  const item = entry(gameId);
  const game = item.game;
  let line = t('games.notSearched');
  let mark = 'off';

  if (item.status === 'searching') {
    line = item.progress?.where
      ? t('games.searchingWhere', { where: shortPath(item.progress.where) })
      : t(item.progress?.code ? 'p.' + item.progress.code : 'games.searching');
    mark = 'busy';
  } else if (item.status === 'missing') {
    line = t('games.notDetected');
    mark = 'off';
  } else if (game?.found) {
    line = game.loader.installed
      ? t('games.loaderReady', { loader: game.loader.name })
      : t('games.loaderMissing', { loader: game.loader.name });
    mark = game.loader.installed ? 'ok' : 'warn';
  }
  return { line, mark };
}

function gameCard(gameId) {
  const item = entry(gameId);
  const info = item.info;
  const found = Boolean(item.game?.found);
  const { line, mark } = gameStatus(gameId);
  const count = found ? state.catalogTotals[gameId] : 0;
  return `
    <button class="gcard${found ? '' : ' is-off'}${gameLogo(gameId) ? ' has-logo' : ''}" style="${accentStyle(info.accent)}" data-action="${found ? 'open-game' : 'not-found'}" data-game="${esc(gameId)}">
      <span class="gcard__art" style="background-image:url('${esc(gameArt(gameId))}')"></span>
      ${gameLogo(gameId) ? `<img class="gcard__logo" src="${esc(gameLogo(gameId))}" alt="" referrerpolicy="no-referrer" />` : ''}
      <span class="gcard__badge">${esc(initials(info.shortName))}</span>
      <span class="gcard__body">
        <span class="gcard__name">${esc(info.name)}</span>
        <span class="gcard__line"><i class="dot dot--${mark}"></i>${esc(line)}</span>
        ${count ? `<span class="gcard__count">${esc(t('home.inCatalog', { n: count.toLocaleString(window.I18N.lang === 'en' ? 'en-US' : 'ru-RU') }))}</span>` : ''}
      </span>
      <span class="gcard__go">${icon(found ? 'arrowRight' : 'search')}</span>
    </button>`;
}

function gamesBlock() {
  return `
    <section class="block reveal">
      <div class="block__head">
        <h2>${esc(t('home.yourGames'))}</h2>
        <button class="linkbtn" data-action="nav-games">${esc(t('home.allGames'))}${icon('chevRight')}</button>
      </div>
      <div class="gcards">
        ${state.order.map(gameCard).join('')}
        <button class="gcard gcard--more" data-action="nav-games">
          <span class="gcard__plus">${icon('grid')}</span>
          <span class="gcard__name">${esc(t('home.allGames'))}</span>
          <span class="gcard__line">${esc(t('home.allGames.text'))}</span>
        </button>
      </div>
    </section>`;
}

/* --- «Рекомендуем»: мозаика ---------------------------------------- */

/**
 * Узор мозаики — два блока по четыре плитки: большая слева + широкая +
 * две маленькие, затем зеркально — большая справа. Плотная укладка сетки
 * (dense) сама ставит плитки на свои места.
 */
const MOSAIC = ['L', 'W', 'S', 'S', 'R', 'W', 'S', 'S'];

function mosaicTile({ mod, game, rank }, size, index) {
  const big = size === 'L' || size === 'R';
  const wide = size === 'W';
  const by = mod.author ? t('mod.by', { author: shortAuthors(mod.author) }) : game.name;
  const square = mod.iconShape === 'square' ? ' is-square' : '';
  return `
    <article class="mtile mtile--${size} reveal" style="--i:${index};${accentStyle(game.accent)}" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">
      ${coverBlock(mod, { className: 'mtile__cover' + square })}
      <div class="mtile__top">${badgeRow(modBadges(mod, game.id, { rank }))}${gameChip(game.id, { withName: big })}</div>
      <div class="mtile__body">
        <h3 title="${esc(mod.name)}">${esc(mod.name)}</h3>
        ${big || wide ? `<p class="mtile__by">${esc(by)}</p>` : ''}
        ${big ? `<p class="mtile__text">${esc(mod.description || '')}</p>` : ''}
        <div class="mtile__foot">
          ${ratingLine(game.id, mod.id, { compact: true })}
          ${downloadsMeta(mod)}
          ${big ? installButton(mod, game, { size: ' getbtn--sm' }) : ''}
        </div>
      </div>
    </article>`;
}

function recommendBlock(feed, loading) {
  const hero = new Set(heroPicks(feed).map(feedKey));
  const rest = feed.filter((x) => !hero.has(feedKey(x)));
  const pool = rest.length >= 4 ? rest : feed;
  const count = pool.length >= 8 ? 8 : pool.length >= 4 ? 4 : 0;

  let body = '';
  if (count) {
    body = pool
      .slice(0, count)
      .map((item, i) => mosaicTile(item, MOSAIC[i], i))
      .join('');
  } else if (loading) {
    body = MOSAIC.map((size) => `<div class="mtile mtile--${size} mtile--ghost"></div>`).join('');
  } else {
    return '';
  }

  return `
    <section class="block block--mosaic">
      <div class="block__head">
        <div>
          <h2>${esc(t('home.recommend'))}</h2>
          <p class="muted small">${esc(t('home.recommend.text'))}</p>
        </div>
      </div>
      <div class="mosaic">${body}</div>
    </section>`;
}

/* --- «Топ модов» ----------------------------------------------------- */

function chartBlock(feed, loading) {
  const games = readyGames().filter((item) => (state.home.get(item.game.id) ?? []).length);
  if (!feed.length && !loading) return '';
  if (state.homeChart !== 'all' && !games.some((item) => item.game.id === state.homeChart)) state.homeChart = 'all';

  const tabs = [['all', t('home.chart.all'), null], ...games.map((item) => [item.game.id, item.info.shortName || item.game.name, item.game.id])];
  const list = (state.homeChart === 'all' ? feed : feed.filter((x) => x.game.id === state.homeChart)).slice(0, 9);

  const rows = list.length
    ? list
        .map(
          ({ mod, game }, i) => `
          <article class="chart__row reveal" style="--i:${i};${accentStyle(game.accent)}" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">
            <span class="chart__n${i < 3 ? ' is-top' : ''}">${i + 1}</span>
            <span class="chart__pic" style="${thumbStyle(mod)}"></span>
            <span class="chart__body">
              <b title="${esc(mod.name)}">${esc(mod.name)}</b>
              <span class="chart__game">${gameChip(game.id, { withName: false })}${esc(game.name)}</span>
              <span class="chart__meta">${ratingLine(game.id, mod.id, { compact: true })}${downloadsMeta(mod)}</span>
            </span>
            ${installButton(mod, game, { size: ' getbtn--ghost', iconOnly: true })}
          </article>`
        )
        .join('')
    : '<div class="chart__row chart__row--ghost"></div>'.repeat(6);

  return `
    <section class="block">
      <div class="block__head">
        <h2>${icon('trophy')}<span>${esc(t('home.chart'))}</span></h2>
        <div class="seg">
          ${tabs
            .map(
              ([id, label, gameId]) =>
                `<button class="seg__btn${state.homeChart === id ? ' is-active' : ''}" type="button" data-action="chart" data-game="${esc(id)}">${gameId ? gameChip(gameId, { withName: false }) : ''}<span>${esc(label)}</span></button>`
            )
            .join('')}
        </div>
      </div>
      <div class="chart">${rows}</div>
    </section>`;
}

/* --- полки, которые листаются стрелками ---------------------------- */

function shelfBlock({ id, title, sub = '', chip = '', cards, all = '' }) {
  return `
    <section class="block shelf">
      <div class="block__head">
        <h2>${chip}<span>${esc(title)}</span>${sub ? `<small class="muted">${esc(sub)}</small>` : ''}</h2>
        <div class="shelf__tools">
          ${all}
          <button class="roundbtn" type="button" data-action="shelf" data-shelf="${esc(id)}" data-dir="-1" aria-label="${esc(t('home.prev'))}">${icon('chevLeft')}</button>
          <button class="roundbtn" type="button" data-action="shelf" data-shelf="${esc(id)}" data-dir="1" aria-label="${esc(t('home.next'))}">${icon('chevRight')}</button>
        </div>
      </div>
      <div class="shelf__track" id="${esc(id)}">${cards}</div>
    </section>`;
}

function gameShelves() {
  return readyGames()
    .map((item) => {
      const game = item.game;
      const mods = state.home.get(game.id);
      const failed = state.homeErrors.get(game.id);
      const title = t('home.popular', { game: game.name });
      if (failed) {
        return `
          <section class="block reveal" style="${accentStyle(game.accent)}">
            <div class="block__head"><h2>${gameChip(game.id, { withName: false })}<span>${esc(title)}</span></h2></div>
            ${errorBlock(failed, 'retry-home')}
          </section>`;
      }
      if (mods && mods.length === 0) return '';
      const cards = mods
        ? mods.map((mod, i) => modCard(mod, game, i, { rank: i })).join('')
        : '<div class="card card--ghost"></div>'.repeat(5);
      return `<div style="${accentStyle(game.accent)}">${shelfBlock({
        id: `shelf-${game.id}`,
        title,
        chip: gameChip(game.id, { withName: false }),
        cards,
        all: `<button class="linkbtn" data-action="open-popular" data-game="${esc(game.id)}">${esc(t('common.all'))}${icon('chevRight')}</button>`,
      })}</div>`;
    })
    .join('');
}

function freshShelf() {
  const feed = homeFeed(state.homeNew).filter((x) => Number.isFinite(daysAgo(x.mod.updatedAt)));
  if (!feed.length) return '';
  const cards = feed
    .slice(0, 14)
    .map(({ mod, game }, i) => modCard(mod, game, i, { fresh: true, showGame: true }))
    .join('');
  return shelfBlock({ id: 'shelf-fresh', title: t('home.fresh'), sub: t('home.fresh.text'), chip: icon('sparkle'), cards });
}

function renderHome() {
  const ready = readyGames();
  const feed = homeFeed();
  const loading = ready.some((item) => !state.home.has(item.game.id));

  const show = pref('homeSections');
  return `
    <div class="page page--home">
      ${heroBlock(feed)}
      ${gamesBlock()}
      ${ready.length ? (show.recommend !== false ? recommendBlock(feed, loading) : '') : emptyGamesBlock()}
      ${ready.length && show.chart !== false ? chartBlock(feed, loading) : ''}
      ${show.shelves !== false ? gameShelves() : ''}
      ${show.fresh !== false ? freshShelf() : ''}
    </div>`;
}

/** Каталог не загрузился: что случилось и кнопка «Повторить». */
function errorBlock(message, action = 'retry-catalog') {
  return `
    <section class="failbox reveal">
      <span class="failbox__icon">${icon('warn')}</span>
      <div class="failbox__body">
        <h3>${esc(t('catalog.error'))}</h3>
        <p>${esc(message)}</p>
      </div>
      <button class="btn btn--ghost btn--sm" data-action="${action}">${icon('refresh')}<span>${esc(t('common.retry'))}</span></button>
    </section>`;
}

function emptyGamesBlock() {
  return `
    <section class="empty">
      <h3>${esc(t('home.noGames.title'))}</h3>
      <p>${esc(t('home.noGames.text'))}</p>
      <button class="btn btn--primary" data-action="nav-games">${esc(t('home.noGames.action'))}</button>
    </section>`;
}

function shortPath(value) {
  const parts = String(value).split(/[\\/]/).filter(Boolean);
  return parts.length > 2 ? '…\\' + parts.slice(-2).join('\\') : String(value);
}

/* --- обложки модов ------------------------------------------------ */

/**
 * Обложка мода — одна на все экраны: карточка, страница мода, «Загрузки».
 *
 * Четыре вида, в порядке предпочтения:
 *   photo — настоящий кадр мода во всю ширину (Nexus, скриншоты из README,
 *           вики Hollow Knight, превью ролика);
 *   icon  — квадратный значок (Thunderstore, логотипы): плиткой по центру
 *           на размытом фоне из самого же значка, а не растянутый в кашу;
 *   art   — рисованная обложка Hollow Knight для модов, у которых картинок
 *           нет нигде: кадр из нашего арта и знак темы мода;
 *   generated — градиент и инициалы, если нет даже темы.
 *
 * Какой кадр квадратный, а какой широкий, заранее не всегда известно —
 * это выясняется, когда картинка загрузится (probeCovers), и обложка
 * тихо перестраивается. Не загрузилась — берётся следующий кадр мода,
 * а если их нет, рисованная обложка.
 */
const ART_SCENE = 'art/game-hollow-knight.jpg';
const ART_KINDS = {
  gameplay: { icon: 'gamepad', glow: '#8fb8ff' },
  boss: { icon: 'swords', glow: '#ff7d7d' },
  utility: { icon: 'wrench', glow: '#6fe3d5' },
  cosmetic: { icon: 'brush', glow: '#d9a0ff' },
  library: { icon: 'puzzle', glow: '#ffc56b' },
  expansion: { icon: 'map', glow: '#93e59a' },
  charm: { icon: 'amulet', glow: '#ffd76b' },
  joke: { icon: 'smile', glow: '#ff9fd2' },
  accessibility: { icon: 'eye', glow: '#a4d6ff' },
};
const TAG_KIND = {
  Gameplay: 'gameplay', Boss: 'boss', Utility: 'utility', Cosmetic: 'cosmetic', Library: 'library',
  Expansion: 'expansion', Charm: 'charm', Joke: 'joke', Accessibility: 'accessibility',
};

/** Форма картинок, которые уже загружались: адрес -> 'photo' | 'icon' | 'broken'. */
const shapes = new Map();
const probing = new Set();

function artOf(mod) {
  if (!mod || !mod.kind) return null;
  const kind = ART_KINDS[mod.kind] ? mod.kind : 'gameplay';
  const h = hash(mod.id || mod.name);
  return {
    kind,
    ...ART_KINDS[kind],
    x: h % 101,
    y: 20 + ((h >>> 7) % 61),
    zoom: 190 + ((h >>> 13) % 80),
    hue: ((h >>> 3) % 41) - 20,
  };
}

/** Тег ModLinks по-русски; незнакомые (и категории Nexus) — как есть. */
function tagLabel(tag) {
  const kind = TAG_KIND[tag];
  const key = kind ? `kind.${kind}` : `tag.${tag}`;
  const text = t(key);
  return text === key ? tag : text;
}

/** «Pale Court» делали 60 человек — в строке автора хватит двух имён и счётчика. */
function shortAuthors(author) {
  const names = String(author ?? '').split(/\s*,\s*/).filter(Boolean);
  return names.length > 3 ? `${names.slice(0, 2).join(', ')} +${names.length - 2}` : names.join(', ');
}

const YT_THUMB = /^https:\/\/i\.ytimg\.com\/vi\/([\w-]{11})\//;

/** Все картинки мода по порядку: обложка, большая картинка, кадры из каталога. */
function coverCandidates(mod) {
  const list = [mod?.icon, mod?.picture, ...(mod?.media?.images ?? [])];
  for (const id of mod?.media?.videos ?? []) list.push(`https://i.ytimg.com/vi/${id}/mqdefault.jpg`);
  return [...new Set(list.filter(Boolean))];
}

/**
 * Какую обложку рисовать.
 * @returns {{kind: 'photo'|'icon'|'art'|'generated', url?: string, art?: object, video?: boolean}}
 */
function coverInfo(mod) {
  for (const url of coverCandidates(mod)) {
    const shape = shapes.get(url);
    if (shape === 'broken') continue;
    const square = mod.iconShape === 'square' && url === mod.icon;
    return { kind: square || shape === 'icon' ? 'icon' : 'photo', url, video: YT_THUMB.test(url) };
  }
  const art = artOf(mod);
  return art ? { kind: 'art', art } : { kind: 'generated' };
}

function artStyle(mod, art) {
  return (
    `background-image:url('${ART_SCENE}');background-size:${art.zoom}% auto;` +
    `background-position:${art.x}% ${art.y}%;--hue:${art.hue}deg;--glow:${art.glow}`
  );
}

function gradientStyle(mod) {
  const h1 = hash(mod.id || mod.name) % 360;
  const h2 = (h1 + 40 + (hash(mod.name) % 90)) % 360;
  return (
    `background-image:` +
    `radial-gradient(circle at 18% 22%, hsl(${h1} 85% 64% / .55), transparent 52%),` +
    `radial-gradient(circle at 82% 88%, hsl(${h2} 90% 58% / .5), transparent 58%),` +
    `linear-gradient(135deg, hsl(${h1} 48% 20%), hsl(${h2} 55% 11%))`
  );
}

/** Фон для размытой подложки страницы мода — та же картинка, что и обложка. */
function backdropStyle(mod) {
  const cover = coverInfo(mod);
  if (cover.url) return `background-image:url('${esc(cover.url)}')`;
  if (cover.kind === 'art') return artStyle(mod, cover.art);
  return gradientStyle(mod);
}

/**
 * Разметка обложки. extra — значки поверх (флаг «Установлен» на карточке).
 * @param {object} mod
 * @param {{className?: string, extra?: string}} [options]
 */
function coverBlock(mod, { className = '', extra = '' } = {}) {
  const cover = coverInfo(mod);
  const probe = cover.url && !shapes.has(cover.url) ? ` data-probe="${esc(cover.url)}"` : '';
  const cls = `card__cover ${className} is-${cover.kind}`;

  if (cover.kind === 'photo' || cover.kind === 'icon') {
    const bg = `background-image:url('${esc(cover.url)}')`;
    return `
      <div class="${cls}" data-src="${esc(cover.url)}"${probe}>
        <div class="card__img" style="${bg}"></div>
        <div class="card__tile" style="${bg}"></div>
        ${cover.video ? `<span class="card__play">${icon('play')}</span>` : ''}
        ${extra}
      </div>`;
  }
  if (cover.kind === 'art') {
    const art = cover.art;
    return `
      <div class="${cls}" style="--glow:${art.glow}">
        <div class="card__img" style="${artStyle(mod, art)}"></div>
        <span class="card__emblem" style="--glow:${art.glow}">${icon(art.icon)}</span>
        ${extra}
      </div>`;
  }
  return `
    <div class="${cls}">
      <div class="card__img" style="${gradientStyle(mod)}"></div>
      <span class="card__mark">${esc(initials(mod.name))}</span>
      ${extra}
    </div>`;
}

/**
 * Узнаём форму картинок, которые рисуются впервые. Квадратный значок
 * переводим в плитку, битую картинку — меняем на следующую или на
 * рисованную обложку (перерисовкой экрана, это дёшево).
 */
function probeCovers(root) {
  root.querySelectorAll('[data-probe]').forEach((node) => {
    const url = node.dataset.probe;
    node.removeAttribute('data-probe');
    if (shapes.has(url) || probing.has(url)) return;
    probing.add(url);
    const img = new Image();
    img.referrerPolicy = 'no-referrer';
    img.onload = () => {
      probing.delete(url);
      const w = img.naturalWidth;
      const h = img.naturalHeight;
      const ratio = h ? w / h : 1;
      const shape = ratio >= 0.8 && ratio <= 1.3 ? 'icon' : w < 300 ? 'icon' : 'photo';
      shapes.set(url, shape);
      // Перестраиваем уже нарисованные обложки с этим адресом — без перерисовки экрана.
      document.querySelectorAll('.card__cover[data-src]').forEach((cover) => {
        if (cover.dataset.src !== url) return;
        const iconLike = shape === 'icon' || cover.classList.contains('is-square');
        cover.classList.toggle('is-icon', iconLike);
        cover.classList.toggle('is-photo', !iconLike);
      });
    };
    img.onerror = () => {
      probing.delete(url);
      shapes.set(url, 'broken');
      scheduleRerender();
    };
    img.src = url;
  });
}

let rerenderTimer = null;
function scheduleRerender() {
  clearTimeout(rerenderTimer);
  rerenderTimer = setTimeout(softRender, 180);
}

/* --- карточка мода ------------------------------------------------- */

function isInstalled(mod, game) {
  const list = state.library[game.id] ?? (state.activeGameId === game.id ? state.mods : []);
  return list.some((m) => m.id === mod.id || m.id === `nexus:${game.catalog?.nexusDomain}:${mod.id}`);
}

/**
 * @param {object} mod
 * @param {object} game
 * @param {number} [index] — для лесенки появления
 * @param {{rank?: number, fresh?: boolean}} [options]
 *   rank — место по популярности (даёт надпись «Хит»), fresh — вместо
 *   загрузок показать, когда мод обновился, showGame — значок игры на
 *   обложке (на полках, где вперемешку моды разных игр)
 */
function modCard(mod, game, index = 0, { rank = -1, fresh = false, showGame = false } = {}) {
  const installed = isInstalled(mod, game);
  const art = artOf(mod);
  // У модов Hollow Knight нет счётчика загрузок в каталоге — вместо «0» показываем тему мода.
  const meta = fresh && mod.updatedAt
    ? `<span class="meta meta--fresh">${icon('refresh')}${esc(agoText(mod.updatedAt))}</span>`
    : mod.downloads
      ? `<span class="meta">${icon('download')}${esc(formatCount(mod.downloads))}</span>`
      : art
        ? `<span class="meta">${icon(art.icon)}${esc(tagLabel(mod.categories?.[0]) || t(`kind.${art.kind}`))}</span>`
        : '<span class="meta"></span>';
  // Установленный мод отмечен флажком; остальные — надписями «Хит», «Лучшее»…
  const flag = installed
    ? `<span class="card__flag">${icon('check')}${esc(t('mod.installed'))}</span>`
    : badgeRow(modBadges(mod, game.id, { rank }));
  const gameMark = showGame ? `<span class="card__game">${gameChip(game.id, { withName: false })}</span>` : '';

  // Вся карточка открывает страницу мода, а кнопка — ставит мод сразу.
  return `
    <article class="card reveal" style="--i:${index % 12};${accentStyle(game.accent)}" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">
      ${coverBlock(mod, { className: mod.iconShape === 'square' ? 'is-square' : '', extra: flag + gameMark })}
      <div class="card__body">
        <h3 title="${esc(mod.name)}">${esc(mod.name)}</h3>
        <p class="card__by">${esc(mod.author ? t('mod.by', { author: shortAuthors(mod.author) }) : game.name)}</p>
        ${ratingLine(game.id, mod.id)}
        <p class="card__text">${esc(mod.description || '')}</p>
      </div>
      <div class="card__foot">
        ${meta}
        ${
          visibleJob(game.id, mod.id)
            ? jobCta(visibleJob(game.id, mod.id), 'card__cta')
            : installed
              ? `<span class="card__cta is-done">${icon('check')}<span>${esc(t('mod.installed'))}</span></span>`
              : `<button class="card__cta" type="button" data-action="install-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">${icon('download')}<span>${esc(t('mod.install'))}</span></button>`
        }
      </div>
    </article>`;
}

/**
 * Моды Hollow Knight, которых нет во встроенном указателе картинок:
 * программа сама читает их README и обновляет карточки.
 */
const mediaAsked = new Set();
async function fillMedia(gameId, mods) {
  const ids = (mods ?? []).filter((m) => m?.mediaPending && !mediaAsked.has(`${gameId}|${m.id}`)).map((m) => m.id);
  if (!ids.length || !api.catalog.media) return;
  ids.forEach((id) => mediaAsked.add(`${gameId}|${id}`));
  const found = await call(api.catalog.media(gameId, ids), { silent: true }).catch(() => null);
  if (!found || !Object.keys(found).length) return;
  const patch = (list) =>
    (list ?? []).forEach((mod) => {
      if (found[mod.id]) Object.assign(mod, found[mod.id], { mediaPending: false });
    });
  patch(state.catalog);
  patch(state.home.get(gameId));
  if (state.modView?.mod && found[state.modView.mod.id]) Object.assign(state.modView.mod, found[state.modView.mod.id]);
  softRender();
}

/* --- экран «Самые популярные» -------------------------------------- */

function renderPopular() {
  const item = entry(state.activeGameId) ?? readyGames()[0];
  const game = item?.game;
  if (!game) return emptyGamesBlock();

  return `
    <div class="page" style="${accentStyle(game.accent)}">
      <div class="page__head">
        <h1 class="page__title">${esc(state.query ? game.name : catalogTitle())}</h1>
        <div class="panel__tail">
          <span class="muted">${esc(t('popular.count', { n: fullNumber(state.catalogMeta.total || state.catalog.length) }))}</span>
          ${catalogTools(game)}
        </div>
      </div>
      ${catalogBody(game)}
    </div>`;
}

/** Содержимое каталога: загрузка, ошибка, пусто или сетка карточек с «Показать ещё». */
function catalogBody(game) {
  const meta = state.catalogMeta;
  if (meta.error && state.catalog.length === 0) return errorBlock(meta.error);
  if (state.catalogLoading && state.catalog.length === 0) {
    return `<div class="grid">${'<div class="card card--ghost"></div>'.repeat(8)}</div>`;
  }
  if (state.catalog.length === 0) {
    return `<section class="empty reveal"><h3>${esc(meta.query ? t('catalog.nothingFound', { query: meta.query }) : t('popular.empty'))}</h3></section>`;
  }
  const more = meta.hasMore
    ? `<div class="more"><button class="btn btn--ghost" data-action="more"${state.catalogLoading ? ' disabled' : ''}>
         ${state.catalogLoading ? `<span class="spinner spinner--sm"></span>` : icon('download')}<span>${esc(t(state.catalogLoading ? 'catalog.loading' : 'catalog.more'))}</span>
       </button></div>`
    : '';
  const failedMore = meta.error ? errorBlock(meta.error, 'more') : '';
  const byPopularity = !meta.query && state.catalogSort === 'popular';
  // «Скрыть установленные» — фильтр по уже загруженной странице каталога.
  const hide = pref('hideInstalled');
  const shown = state.catalog.map((mod, i) => ({ mod, i })).filter(({ mod }) => !hide || !isInstalled(mod, game));
  const hiddenNote =
    hide && shown.length < state.catalog.length
      ? `<p class="muted small catalog__note">${icon('eye')}<span>${esc(t('catalog.hidden', { n: state.catalog.length - shown.length }))}</span></p>`
      : '';
  const list = pref('catalogView') === 'list';
  const items = shown
    .map(({ mod, i }, n) => (list ? modRow(mod, game, n, { rank: byPopularity ? i : -1 }) : modCard(mod, game, n, { rank: byPopularity ? i : -1 })))
    .join('');
  return `${hiddenNote}<div class="${list ? 'mlist' : 'grid'}">${items}</div>${failedMore}${more}`;
}

/* --- описание мода: README и BBCode в безопасный HTML --------------- */

/**
 * Nexus хранит описание смесью BBCode и HTML. Переводим основное в HTML,
 * остальные теги просто снимаем. Дальше всё равно идёт DOMPurify.
 */
function bbcodeToHtml(text) {
  let html = String(text ?? '');
  const list = (_, body) =>
    `<ul>${body
      .split(/\[\*\]/)
      .slice(1)
      .map((item) => `<li>${item}</li>`)
      .join('')}</ul>`;
  const rules = [
    [/\[b\]([\s\S]*?)\[\/b\]/gi, '<strong>$1</strong>'],
    [/\[i\]([\s\S]*?)\[\/i\]/gi, '<em>$1</em>'],
    [/\[u\]([\s\S]*?)\[\/u\]/gi, '<u>$1</u>'],
    [/\[s\]([\s\S]*?)\[\/s\]/gi, '<s>$1</s>'],
    [/\[center\]([\s\S]*?)\[\/center\]/gi, '<div class="rt-center">$1</div>'],
    [/\[(?:left|right|justify)\]([\s\S]*?)\[\/(?:left|right|justify)\]/gi, '<div>$1</div>'],
    [/\[size=["']?([4-9])["']?\]([\s\S]*?)\[\/size\]/gi, '<span class="rt-big">$2</span>'],
    [/\[heading\]([\s\S]*?)\[\/heading\]/gi, '<h3>$1</h3>'],
    [/\[line\]/gi, '<hr>'],
    [/\[img(?:=[^\]]*)?\]\s*(https?:\/\/[^\s[\]]+?)\s*\[\/img\]/gi, '<img src="$1">'],
    [/\[url=["']?([^\]"']+)["']?\]([\s\S]*?)\[\/url\]/gi, '<a href="$1">$2</a>'],
    [/\[url\]\s*(https?:\/\/[^\s[\]]+?)\s*\[\/url\]/gi, '<a href="$1">$1</a>'],
    [/\[youtube\]\s*([\w-]{6,})\s*\[\/youtube\]/gi, '<a href="https://www.youtube.com/watch?v=$1">YouTube ▶</a>'],
    [/\[quote(?:=[^\]]*)?\]([\s\S]*?)\[\/quote\]/gi, '<blockquote>$1</blockquote>'],
    [/\[code\]([\s\S]*?)\[\/code\]/gi, '<pre>$1</pre>'],
    [/\[spoiler\]([\s\S]*?)\[\/spoiler\]/gi, '<details><summary>Spoiler</summary>$1</details>'],
    [/\[list(?:=[^\]]*)?\]([\s\S]*?)\[\/list\]/gi, list],
  ];
  // Несколько проходов: теги бывают вложены друг в друга.
  for (let pass = 0; pass < 3; pass += 1) {
    for (const [pattern, replacement] of rules) html = html.replace(pattern, replacement);
  }
  html = html.replace(/\[\/?[a-z*]+(?:=[^\]]*)?\]/gi, '');
  // Где автор ставил <br />, переносы строк лишние; где нет — они и есть абзацы.
  return /<br\s*\/?>/i.test(html) ? html.replace(/\r?\n/g, ' ') : html.replace(/\r?\n/g, '<br>');
}

function absUrl(value, base) {
  if (!value) return null;
  try {
    const url = new URL(value, base || undefined);
    if (url.protocol === 'http:') url.protocol = 'https:'; // http-картинки окно не покажет
    return url.protocol === 'https:' ? url.href : null;
  } catch {
    return null;
  }
}

/** Значки-бейджи из README (сборка, лицензия, Discord) — не иллюстрации. */
const BADGE = /shields\.io|badge|badgen\.net|img\.shields|discord(app)?\.com\/api|travis-ci|appveyor|codecov|ko-fi\.com\/img|buymeacoffee|repobeats|star-history|contrib\.rocks|komarev|hits\.|visitor|\/workflows\/|forthebadge|paypal|patreon|liberapay|tokei\.rs|sonarcloud|codeclimate|wakatime|deepsource|readme-stats|profile-counter|circleci|coveralls|snyk\.io/i;
/** Ролик YouTube по ссылке или картинке-превью. */
const YT_LINK = /(?:youtube\.com\/(?:watch\?(?:[^\s"'<>]*&)?v=|embed\/|shorts\/|live\/)|youtu\.be\/|img\.youtube\.com\/vi\/|i\.ytimg\.com\/vi\/)([\w-]{11})/;

/**
 * Описание мода в HTML, пригодный для вставки: всё чужое прошло через
 * DOMPurify, ссылки открываются в браузере, относительные адреса
 * картинок достроены от репозитория.
 */
function richContent(readme) {
  if (!readme?.content || !window.DOMPurify) return { html: '', images: [], videos: [] };
  let html = readme.content;
  if (readme.format === 'markdown' && window.marked) html = window.marked.parse(html, { gfm: true });
  if (readme.format === 'bbcode') html = bbcodeToHtml(html);

  const clean = window.DOMPurify.sanitize(html, {
    FORBID_TAGS: ['style', 'form', 'input', 'button', 'textarea', 'select', 'iframe', 'object', 'embed', 'video', 'audio'],
    FORBID_ATTR: ['style', 'class', 'id', 'width', 'height', 'align'],
    ADD_TAGS: ['details', 'summary'],
  });

  const box = document.createElement('div');
  box.innerHTML = clean;
  const images = [];
  const videos = [];
  for (const img of box.querySelectorAll('img')) {
    const src = absUrl(img.getAttribute('src'), readme.base);
    if (!src) {
      img.remove();
      continue;
    }
    img.setAttribute('src', src);
    img.setAttribute('loading', 'lazy');
    img.setAttribute('alt', img.getAttribute('alt') ?? '');
    const video = YT_LINK.exec(src)?.[1] ?? YT_LINK.exec(img.closest('a')?.getAttribute('href') ?? '')?.[1];
    if (BADGE.test(src)) img.classList.add('rt-badge');
    else if (video) {
      // Превью ролика: по клику — сам ролик, а не картинка крупно.
      img.setAttribute('data-action', 'zoom');
      img.setAttribute('data-src', src);
      img.setAttribute('data-video', video);
      if (!videos.includes(video)) videos.push(video);
    } else {
      img.setAttribute('data-action', 'zoom');
      img.setAttribute('data-src', src);
      images.push(src);
    }
  }
  for (const link of box.querySelectorAll('a')) {
    const href = absUrl(link.getAttribute('href'), readme.linkBase ?? readme.base);
    link.removeAttribute('href');
    const video = href ? YT_LINK.exec(href)?.[1] : null;
    if (video && !videos.includes(video)) videos.push(video);
    if (href && !link.querySelector('img[data-action]')) {
      link.setAttribute('data-action', 'open-url');
      link.setAttribute('data-url', href);
      link.setAttribute('href', '#');
    }
  }
  return { html: box.innerHTML, images: [...new Set(images)], videos };
}

/* --- страница мода ------------------------------------------------- */

function ytThumb(id, size = 'mqdefault') {
  return `https://i.ytimg.com/vi/${id}/${size}.jpg`;
}

function renderModPage() {
  const view = state.modView;
  const item = entry(view?.gameId);
  const game = item?.game;
  const mod = view?.mod;

  if (!game) return emptyGamesBlock();
  if (!mod) {
    return view?.detailsError
      ? `<div class="page">${backButton()}${errorBlock(view.detailsError, 'retry-mod')}</div>`
      : `<div class="loading"><div class="spinner"></div></div>`;
  }

  const details = view.details;
  const installed = state.mods.find((m) => m.id === mod.id || m.id === `nexus:${game.catalog.nexusDomain}:${mod.id}`);
  const installedIds = new Set(state.mods.map((m) => m.id));
  const nexus = game.catalog.kind === 'nexus';
  const nexusBrowser = nexus && !state.settings.nexusPremium;
  const requirements = details?.requirements ?? [];

  const rich = richContent(details?.readme);
  // Галерея: все известные картинки мода и кадры из описания; ролики — отдельными плитками.
  // Квадратный значок Thunderstore уже стоит обложкой — в галерее он был бы единственным «кадром».
  const ownIcon = mod.iconShape === 'square' ? null : mod.icon;
  const pictures = [...new Set([mod.picture, ownIcon, ...(mod.media?.images ?? []), ...rich.images].filter(Boolean))].filter(
    (src) => shapes.get(src) !== 'broken' && !YT_THUMB.test(src)
  );
  const videos = [...new Set([...(mod.media?.videos ?? []), ...rich.videos])].slice(0, 4);
  const gallery = [...pictures.slice(0, 12).map((src) => ({ src })), ...videos.map((id) => ({ src: ytThumb(id, 'hqdefault'), video: id }))];

  // Своей картинки у мода нет, но в описании есть кадр или ролик — он и становится обложкой.
  const face =
    coverCandidates(mod).length || !(rich.images[0] || videos[0])
      ? mod
      : { ...mod, icon: rich.images[0] ?? ytThumb(videos[0]), iconShape: null };
  const updated = formatMonth(mod.updatedAt);
  const chip = mod.categories?.[0] ? tagLabel(mod.categories[0]) : mod.kind ? t(`kind.${mod.kind}`) : '';
  const score = ratingOf(game.id, mod.id);

  const description = details
    ? rich.html
      ? `<div class="richtext">${rich.html}</div>`
      : `<p class="mod__text">${esc(mod.description || t('mod.noDescription'))}</p>`
    : view.detailsError
      ? `<p class="mod__text">${esc(mod.description || '')}</p>${errorBlock(view.detailsError, 'retry-mod')}`
      : `<p class="mod__text">${esc(mod.description || '')}</p><div class="skeleton-lines"><i></i><i></i><i></i></div>`;

  const requirementRow = (r) => {
    const have = installedIds.has(r.id) || installedIds.has(`nexus:${game.catalog.nexusDomain}:${r.id}`);
    const status = have
      ? `<span class="badge badge--ok">${esc(t('mod.deps.have'))}</span>`
      : nexus
        ? r.sameGame === false
          ? `<button class="btn btn--ghost btn--sm" data-action="open-url" data-url="${esc(r.url)}">${icon('external')}<span>${esc(t('mod.page'))}</span></button>`
          : `<button class="btn btn--ghost btn--sm" data-action="install-mod" data-game="${esc(game.id)}" data-mod="${esc(r.id)}">${icon('download')}<span>${esc(t('mod.install'))}</span></button>`
        : `<span class="badge">${esc(t('mod.deps.will'))}</span>`;
    const thumb = r.icon
      ? `<span class="deps__icon" style="background-image:url('${esc(r.icon)}')"></span>`
      : `<span class="deps__icon is-empty">${esc(initials(r.name))}</span>`;
    const clickable = r.available !== false && (nexus ? r.sameGame !== false : true);
    const title = clickable
      ? `<button class="deps__name" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(r.id)}">${thumb}<span>${esc(r.name)}</span></button>`
      : `<span class="deps__name">${thumb}<span>${esc(r.name)}</span></span>`;
    return `<li>${title}${status}</li>`;
  };

  const wiki = mod.media?.wiki ? `https://hollowknight.wiki/w/${encodeURIComponent(`Mod:${mod.media.wiki}`).replace(/%20/g, '_')}` : null;

  return `
    <div class="page" style="${accentStyle(game.accent)}">
      ${backButton()}

      <section class="product">
        <div class="product__backdrop" style="${backdropStyle(face)}"></div>
        <div class="product__main">
          ${coverBlock(face, { className: `product__cover${face.iconShape === 'square' ? ' is-square' : ''}` })}

          <div class="product__info">
            <span class="product__game">${esc(game.name)}${chip ? ` · ${esc(chip)}` : ''}</span>
            <h1>${esc(mod.name)}</h1>
            <p class="product__by">${esc(mod.author ? t('mod.by', { author: shortAuthors(mod.author) }) : game.name)} · ${esc(
              t('mod.version', { version: mod.version || '—' })
            )}</p>

            <div class="product__stats">
              <button class="stat stat--rate${score.count ? '' : ' is-empty'}" data-action="goto-reviews" title="${esc(t('rev.title'))}">
                <b>${esc(formatScore(score.avg))} <span class="stars" style="--rate:${Math.round((score.avg / 5) * 100)}%">★★★★★</span></b>
                <span>${esc(score.count ? pluralN(score.count, 'rev.count') : t('rev.unrated'))}</span>
              </button>
              ${mod.downloads ? `<div><b>${esc(formatCount(mod.downloads))}</b><span>${esc(t('mod.stat.downloads'))}</span></div>` : ''}
              ${updated ? `<div><b>${esc(updated)}</b><span>${esc(t('mod.stat.updated'))}</span></div>` : ''}
              <div><b>${details ? (requirements.length ? requirements.length : esc(t('mod.stat.none'))) : '…'}</b><span>${esc(
                details ? plural(requirements.length, 'mod.stat.deps') : t('mod.stat.deps')
              )}</span></div>
            </div>

            <div class="product__actions">
              ${
                visibleJob(game.id, mod.id)
                  ? jobCta(visibleJob(game.id, mod.id), 'btn btn--primary btn--lg dl--big')
                  : installed
                  ? `<span class="badge badge--ok badge--lg">${icon('check')}${esc(t('mod.installed'))}</span>
                     <button class="btn btn--ghost" data-action="remove-mod" data-game="${esc(game.id)}" data-mod="${esc(installed.id)}">
                       ${icon('trash')}<span>${esc(t('mod.remove'))}</span>
                     </button>`
                  : `<button class="btn btn--primary btn--lg btn--glow" data-action="install-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">
                       ${icon('download')}<span>${esc(t('mod.install'))}</span>
                     </button>`
              }
              ${
                mod.url
                  ? `<button class="btn btn--ghost" data-action="open-url" data-url="${esc(mod.url)}">
                       ${icon('external')}<span>${esc(t(nexus ? 'mod.openNexus' : 'mod.page'))}</span>
                     </button>`
                  : ''
              }
              ${
                wiki
                  ? `<button class="btn btn--ghost" data-action="open-url" data-url="${esc(wiki)}">${icon('book')}<span>${esc(t('mod.wiki'))}</span></button>`
                  : ''
              }
              <button class="btn btn--ghost" data-action="goto-reviews">${icon('chat')}<span>${esc(t('rev.write'))}</span></button>
            </div>
            ${nexusBrowser && !installed ? `<p class="product__hint">${icon('info')}<span>${esc(t('nexus.hint.browser'))}</span></p>` : ''}
          </div>
        </div>
      </section>

      ${
        gallery.length
          ? `<section class="panel reveal">
               <div class="panel__head"><h2>${esc(t('mod.gallery'))}</h2><span class="muted small">${esc(t('mod.gallery.count', { n: gallery.length }))}${
                 mod.media?.wiki ? ` · ${esc(t('mod.gallery.wiki'))}` : ''
               }</span></div>
               <div class="shots">${gallery
                 .map(
                   (shot, i) =>
                     `<button class="shot${shot.video ? ' shot--video' : ''}" style="--i:${i}" data-action="zoom" data-src="${esc(shot.src)}"${
                       shot.video ? ` data-video="${esc(shot.video)}"` : ''
                     }><img src="${esc(shot.src)}" alt="" loading="lazy" referrerpolicy="no-referrer" />${
                       shot.video ? `<span class="shot__play">${icon('play')}</span>` : ''
                     }</button>`
                 )
                 .join('')}</div>
             </section>`
          : ''
      }

      ${
        requirements.length
          ? `<section class="panel reveal">
               <div class="panel__head"><h2>${esc(t(nexus ? 'mod.deps.nexus' : 'mod.deps'))}</h2></div>
               <ul class="deps">${requirements.map(requirementRow).join('')}</ul>
             </section>`
          : ''
      }

      <section class="panel reveal">
        <div class="panel__head"><h2>${esc(t('mod.about'))}</h2></div>
        ${description}
      </section>

      ${reviewsPanel(game, mod)}
    </div>`;
}

/* --- отзывы -------------------------------------------------------- */

function formatDay(value) {
  const date = value ? new Date(value) : null;
  if (!date || Number.isNaN(date.getTime())) return '';
  const lang = window.I18N?.lang === 'en' ? 'en-US' : 'ru-RU';
  return date.toLocaleDateString(lang, { day: 'numeric', month: 'short', year: 'numeric' }).replace(/\s*г\.$/, '');
}

/** Черновик отзыва: пока человек не начал писать, в нём его опубликованный отзыв. */
function draftFor(key, mine) {
  let draft = state.reviewDraft[key];
  if (!draft || (!draft.touched && mine && (draft.stars !== mine.stars || draft.text !== mine.text))) {
    draft = {
      stars: mine?.stars ?? draft?.stars ?? 0,
      text: mine?.text ?? draft?.text ?? '',
      name: mine?.name ?? draft?.name ?? state.settings.reviewName ?? '',
      touched: false,
    };
    state.reviewDraft[key] = draft;
  }
  return draft;
}

function reviewForm(game, mod, reviews) {
  const key = ratingKey(game.id, mod.id);
  const mine = reviews.find((r) => r.mine) ?? null;
  const draft = draftFor(key, mine);
  const stars = [1, 2, 3, 4, 5]
    .map(
      (n) =>
        `<button type="button" class="starpick__btn${n <= draft.stars ? ' is-on' : ''}" data-action="rev-star" data-value="${n}" title="${esc(
          t(`rev.star.${n}`)
        )}" aria-label="${n}">★</button>`
    )
    .join('');
  return `
    <div class="review-form${mine ? ' is-edit' : ''}" data-key="${esc(key)}">
      <div class="review-form__head">
        <h3>${esc(t(mine ? 'rev.yours' : 'rev.write'))}</h3>
        <div class="starpick" role="radiogroup">${stars}<span class="starpick__label">${esc(
          draft.stars ? t(`rev.star.${draft.stars}`) : t('rev.pick')
        )}</span></div>
      </div>
      <div class="review-form__fields">
        ${
          state.account.signedIn
            ? `<div class="review-form__as">${myAvatar()}<span>${esc(t('rev.as', { name: state.account.name ?? '' }))}</span>${
                state.account.admin ? `<span class="badge badge--admin">${icon('crown')}<span>${esc(t('rev.admin'))}</span></span>` : ''
              }</div>`
            : `<input class="review-form__name" data-draft="name" maxlength="32" placeholder="${esc(t('rev.name'))}" value="${esc(draft.name)}" />`
        }
        <textarea class="review-form__text" data-draft="text" maxlength="1000" rows="3" placeholder="${esc(t('rev.text'))}">${esc(draft.text)}</textarea>
      </div>
      <div class="review-form__foot">
        <span class="muted small" data-count>${draft.text.length}/1000</span>
        ${
          !state.account.signedIn && state.account.configured
            ? `<button type="button" class="linkbtn review-form__login" data-action="account-open" data-mode="signin">${icon('user')}<span>${esc(t('rev.loginHint'))}</span></button>`
            : ''
        }
        <span class="review-form__spacer"></span>
        ${
          mine
            ? `<button type="button" class="btn btn--ghost btn--sm btn--danger" data-action="rev-delete">${icon('trash')}<span>${esc(t('rev.delete'))}</span></button>`
            : ''
        }
        <button type="button" class="btn btn--primary" data-action="rev-submit"${state.reviewSending ? ' disabled' : ''}>
          ${state.reviewSending ? '<span class="spinner spinner--sm"></span>' : icon('chat')}<span>${esc(
            t(state.reviewSending ? 'rev.sending' : mine ? 'rev.update' : 'rev.publish')
          )}</span>
        </button>
      </div>
    </div>`;
}

/** Запись реестра об этом моде, если он стоит (у модов Nexus номер с приставкой). */
function installedRecord(game, modId) {
  const list = state.library[game.id] ?? state.mods;
  return (
    (list ?? []).find((m) => m.id === modId || m.id === `nexus:${game.catalog?.nexusDomain}:${modId}`) ?? null
  );
}

/**
 * Вместо формы — два шага к оценке (1.9.7): «Скачайте мод» и «Сыграйте
 * с ним». Стоит ли мод и включён ли он, окно знает само (список модов
 * обновляется сразу после установки), а «сыграно» — только основной
 * процесс: он смотрит на запуски из ModHub и на лог загрузчика.
 */
function reviewGate(game, mod, gate) {
  const record = installedRecord(game, mod.id);
  const installed = Boolean(record && !record.missing);
  const enabled = installed && record.enabled !== false;
  const job = visibleJob(game.id, mod.id);

  const mark = (done, n) => `<span class="rgate__mark">${done ? icon('check') : n}</span>`;

  const step1 = installed
    ? `<li class="rgate__step is-done">
         ${mark(true, 1)}
         <div class="rgate__text"><b>${esc(t('rev.gate.step1.done'))}</b><span>${esc(t('rev.gate.step1.doneHint'))}</span></div>
       </li>`
    : `<li class="rgate__step is-now">
         ${mark(false, 1)}
         <div class="rgate__text"><b>${esc(t('rev.gate.step1'))}</b><span>${esc(t('rev.gate.step1.hint'))}</span></div>
         ${
           job
             ? jobCta(job, 'btn btn--primary btn--sm')
             : `<button type="button" class="btn btn--primary btn--sm" data-action="install-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">${icon('download')}<span>${esc(t('mod.install'))}</span></button>`
         }
       </li>`;

  let step2;
  if (!installed) {
    step2 = `<li class="rgate__step is-later">
         ${mark(false, 2)}
         <div class="rgate__text"><b>${esc(t('rev.gate.step2'))}</b><span>${esc(t('rev.gate.step2.later'))}</span></div>
       </li>`;
  } else if (!enabled) {
    step2 = `<li class="rgate__step is-now">
         ${mark(false, 2)}
         <div class="rgate__text"><b>${esc(t('rev.gate.step2'))}</b><span>${esc(t('rev.gate.step2.off'))}</span></div>
         <button type="button" class="btn btn--ghost btn--sm" data-action="toggle-mod" data-game="${esc(game.id)}" data-mod="${esc(record.id)}" data-enabled="1">${icon('check')}<span>${esc(t('rev.gate.enable'))}</span></button>
       </li>`;
  } else {
    step2 = `<li class="rgate__step is-now">
         ${mark(false, 2)}
         <div class="rgate__text"><b>${esc(t('rev.gate.step2'))}</b><span>${esc(t('rev.gate.step2.hint'))}</span></div>
         <button type="button" class="btn btn--play btn--sm" data-action="play" data-game="${esc(game.id)}">${icon('play')}<span>${esc(t('games.play'))}</span></button>
       </li>`;
  }

  return `
    <div class="rgate${installed ? ' is-half' : ''}">
      <div class="rgate__head">
        <span class="rgate__lock">${icon('lock')}</span>
        <div>
          <h3>${esc(t('rev.gate.title'))}</h3>
          <p class="muted small">${esc(t('rev.gate.text'))}</p>
        </div>
      </div>
      <ol class="rgate__steps">${step1}${step2}</ol>
    </div>`;
}

/**
 * Проверить пропуск к оценке ещё раз — после «Играть», после включения
 * мода и когда человек вернулся в окно из игры.
 * @returns {Promise<boolean>} открылась ли оценка только что
 */
async function refreshGate() {
  const view = state.modView;
  if (state.view !== 'mod' || !view?.mod) return false;
  const key = ratingKey(view.gameId, view.mod.id);
  if (state.reviewView?.key !== key) return false;
  const before = state.reviewView.gate ?? null;
  const gate = await call(api.reviews.gate(view.gameId, view.mod.id), { silent: true }).catch(() => null);
  if (!gate || state.reviewView?.key !== key) return false;
  state.reviewView = { ...state.reviewView, gate };
  const unlocked = Boolean(gate.ok && before && !before.ok);
  if (unlocked || before?.installed !== gate.installed || before?.enabled !== gate.enabled) softRender();
  return unlocked;
}

function reviewItem(r) {
  const created = Date.parse(r.created ?? '');
  const updated = Date.parse(r.updated ?? '');
  const edited = created && updated && updated - created > 60_000;
  return `
    <li class="review${r.mine ? ' is-mine' : ''}${r.admin ? ' is-admin' : ''}">
      <span class="review__avatar" style="--h:${hash(r.name) % 360}">${esc(initials(r.name))}</span>
      <div class="review__body">
        <div class="review__head">
          <b>${esc(r.name)}</b>
          ${r.admin ? `<span class="badge badge--admin" title="${esc(t('rev.admin.title'))}">${icon('crown')}<span>${esc(t('rev.admin'))}</span></span>` : ''}
          ${r.mine ? `<span class="badge badge--mine">${esc(t('rev.mine'))}</span>` : ''}
          ${r.played ? `<span class="badge badge--played" title="${esc(t('rev.played.title'))}">${icon('gamepad')}<span>${esc(t('rev.played'))}</span></span>` : ''}

          <span class="stars stars--sm" style="--rate:${r.stars * 20}%">★★★★★</span>
          <span class="muted small">${esc(formatDay(r.updated || r.created))}${edited ? ` · ${esc(t('rev.edited'))}` : ''}</span>
          ${
            state.account.admin && !r.mine
              ? `<button type="button" class="review__mod" data-action="rev-moderate" data-id="${esc(r.id)}" title="${esc(t('rev.moderate.title'))}">${icon('trash')}<span>${esc(t('rev.moderate'))}</span></button>`
              : ''
          }
        </div>
        ${r.text ? `<p class="review__text">${esc(r.text)}</p>` : ''}
      </div>
    </li>`;
}

/** Блок «Отзывы» на странице мода: общая оценка, форма и отзывы людей. */
function reviewsPanel(game, mod) {
  const key = ratingKey(game.id, mod.id);
  const view = state.reviewView?.key === key ? state.reviewView : null;
  const score = ratingOf(game.id, mod.id);
  const reviews = view?.reviews ?? [];
  const configured = view ? view.configured : state.reviewsConfigured;

  const bars = [5, 4, 3, 2, 1]
    .map((n) => {
      const count = reviews.filter((r) => r.stars === n).length;
      const pct = reviews.length ? Math.round((count / reviews.length) * 100) : 0;
      return `<div class="bars__row"><span>${n}★</span><i><b style="width:${pct}%"></b></i><em>${count}</em></div>`;
    })
    .join('');

  const down = configured && REVIEWS_DOWN.has(view?.errorCode);
  let body;
  if (!configured) {
    body = `
      <div class="reviews__off">
        <span class="reviews__off-icon">${icon('info')}</span>
        <div>
          <h3>${esc(t('rev.off.title'))}</h3>
          <p class="muted small">${esc(t('rev.off.text'))}</p>
        </div>
      </div>`;
  } else if (down) {
    // Сервер временно недоступен: формы нет, но всё, что уже знаем, — видно.
    body = `
      <div class="reviews__off">
        <span class="reviews__off-icon">${icon('info')}</span>
        <div>
          <h3>${esc(t('rev.down.title'))}</h3>
          <p class="muted small">${esc(t('rev.down.text'))}</p>
        </div>
      </div>
      ${reviews.length ? `<ul class="reviews__list">${reviews.map(reviewItem).join('')}</ul>` : ''}`;
  } else {
    const list = reviews.length
      ? `<ul class="reviews__list">${reviews.map(reviewItem).join('')}</ul>`
      : view?.loading
        ? `<div class="skeleton-lines"><i></i><i></i></div>`
        : `<p class="reviews__empty muted">${esc(t('rev.empty'))}</p>`;
    // 1.9.7: оценка — только после «скачал и поиграл». Пока пропуск не
    // пришёл, формы нет вовсе: иначе она мелькнула бы и сменилась шагами.
    const gate = view?.gate ?? null;
    const top = gate
      ? gate.ok
        ? reviewForm(game, mod, reviews)
        : reviewGate(game, mod, gate)
      : view && !view.loading
        ? reviewForm(game, mod, reviews)
        : '';
    body = `
      ${top}
      ${view?.error ? `<p class="reviews__error">${icon('warn')}<span>${esc(view.error)}</span></p>` : ''}
      ${list}`;
  }

  return `
    <section class="panel reveal reviews" id="reviews">
      <div class="panel__head">
        <h2>${icon('chat')}<span>${esc(t('rev.title'))}</span></h2>
        <span class="muted small">${esc(t('rev.shared'))}</span>
      </div>
      <div class="reviews__top">
        <div class="score${score.count ? '' : ' is-empty'}">
          <b class="score__num">${esc(formatScore(score.avg))}</b>
          <span class="stars stars--lg" style="--rate:${Math.round((score.avg / 5) * 100)}%">★★★★★</span>
          <span class="muted small">${esc(score.count ? pluralN(score.count, 'rev.count') : t('rev.unrated'))}</span>
        </div>
        <div class="bars">${bars}</div>
      </div>
      ${body}
    </section>`;
}

/**
 * Ошибки сервера отзывов, которые человек исправить не может: кончился
 * бесплатный лимит, сервер не настроен до конца или лёг. Для него это
 * одно — «отзывы пока что временно недоступны»; подробности видит
 * хозяин сборки в Настройках → Отзывы.
 */
const REVIEWS_DOWN = new Set(['BUSY', 'SERVER', 'AUTH_DISABLED', 'NO_DATABASE', 'BAD_KEY', 'DENIED']);

async function loadReviews(gameId, modId) {
  const key = ratingKey(gameId, modId);
  const previous = state.reviewView?.key === key ? state.reviewView : null;
  state.reviewView = {
    key,
    loading: true,
    error: null,
    reviews: previous?.reviews ?? [],
    configured: state.reviewsConfigured,
    gate: previous?.gate ?? null,
  };
  try {
    const data = await call(api.reviews.list(gameId, modId), { silent: true });
    if (state.reviewView?.key !== key) return;
    state.reviewView = {
      key,
      loading: false,
      error: data?.error ?? null,
      errorCode: data?.errorCode ?? null,
      reviews: data?.reviews ?? [],
      configured: Boolean(data?.configured),
      gate: data?.gate ?? null,
    };
    state.reviewsUnavailable = REVIEWS_DOWN.has(data?.errorCode);
    state.reviewsConfigured = Boolean(data?.configured);
    applyRatings(data?.all);
    if (data?.name && !state.settings.reviewName) state.settings.reviewName = data.name;
  } catch (error) {
    if (state.reviewView?.key !== key) return;
    state.reviewView = { ...state.reviewView, loading: false, error: error.message };
  }
  if (state.view === 'mod' && state.modView?.modId === modId) softRender();
}

function backButton() {
  return `<button class="link-back" data-action="back">← ${esc(t('common.back'))}</button>`;
}

/**
 * Картинка во весь экран: клик по кадру из галереи или описания.
 * Ролик YouTube играет прямо здесь, в рамке, — как на экране «игры нет».
 */
function openLightbox(src, video = null) {
  const items = [];
  for (const node of document.querySelectorAll('#main [data-action="zoom"]')) {
    const item = { src: node.dataset.src, video: node.dataset.video || null };
    if (!items.some((i) => i.src === item.src)) items.push(item);
  }
  let index = Math.max(0, items.findIndex((i) => i.src === src));
  if (!items.length) items.push({ src, video });

  const box = document.createElement('div');
  box.className = 'lightbox';
  box.id = 'lightbox';
  box.innerHTML = `
    <button class="lightbox__close" aria-label="close">✕</button>
    ${items.length > 1 ? '<button class="lightbox__nav lightbox__nav--prev" aria-label="prev">‹</button><button class="lightbox__nav lightbox__nav--next" aria-label="next">›</button>' : ''}
    <div class="lightbox__stage"></div><span class="lightbox__count"></span>`;

  const show = () => {
    const stage = box.querySelector('.lightbox__stage');
    const item = items[index];
    stage.innerHTML = item.video
      ? `<iframe class="lightbox__frame" src="https://www.youtube-nocookie.com/embed/${esc(item.video)}?rel=0&autoplay=1"` +
        ' allow="autoplay; encrypted-media; picture-in-picture" referrerpolicy="strict-origin-when-cross-origin" allowfullscreen></iframe>'
      : `<img alt="" referrerpolicy="no-referrer" src="${esc(item.src)}" />`;
    box.querySelector('.lightbox__count').textContent = items.length > 1 ? `${index + 1} / ${items.length}` : '';
  };
  const close = () => {
    box.classList.add('is-leaving');
    document.removeEventListener('keydown', onKey);
    setTimeout(() => box.remove(), 180);
  };
  const step = (delta) => {
    index = (index + delta + items.length) % items.length;
    show();
  };
  const onKey = (event) => {
    if (event.key === 'Escape') close();
    if (event.key === 'ArrowRight') step(1);
    if (event.key === 'ArrowLeft') step(-1);
  };
  box.addEventListener('click', (event) => {
    event.stopPropagation();
    if (event.target.closest('.lightbox__nav--prev')) return step(-1);
    if (event.target.closest('.lightbox__nav--next')) return step(1);
    if (event.target.tagName !== 'IMG' && event.target.tagName !== 'IFRAME') close();
  });
  document.addEventListener('keydown', onKey);
  document.body.append(box);
  show();
}

/* --- экран «игры нет» ---------------------------------------------- */

/**
 * Что человек видит, когда игры на компьютере не нашлось.
 *
 * Пустой экран с надписью «не найдено» — это тупик. Здесь вместо тупика
 * три шага, кнопки поиска, ролик о том, как это делают руками, и ссылки
 * на каталоги. Ролик и ссылки чужие, поэтому живут за границей программы:
 * видео — в отдельной рамке, ссылки открываются в браузере.
 */
function renderNotFound() {
  const item = entry(state.activeGameId);
  if (!item) return emptyGamesBlock();

  const info = item.info;
  const help = HELP[info.id] ?? { links: [] };
  const searching = item.status === 'searching';

  const steps = [1, 2, 3]
    .map(
      (n) => `
        <li class="step">
          <span class="step__n">${n}</span>
          <div>
            <h4>${esc(t(`notfound.step${n}`))}</h4>
            <p class="muted small">${esc(t(`notfound.step${n}.text`))}</p>
          </div>
        </li>`
    )
    .join('');

  return `
    <div class="page" style="${accentStyle(info.accent)}">
      <section class="lost" style="background-image:url('${esc(customArt('notfound') ?? steamArt(info.id) ?? notFoundArt())}')">
        <div class="lost__text">
          <span class="lost__badge">${esc(t('games.notDetected'))}</span>
          <h1>${esc(t('notfound.title', { game: info.name }))}</h1>
          <p>${esc(t('notfound.lead', { game: info.name }))}</p>
          <div class="lost__actions">
            <button class="btn btn--primary" data-action="pick-path" data-game="${esc(info.id)}">
              ${icon('folder')}<span>${esc(t('games.setPath'))}</span>
            </button>
            <button class="btn btn--ghost" data-action="rescan" data-game="${esc(info.id)}"${searching ? ' disabled' : ''}>
              ${icon('refresh')}<span>${esc(searching ? t('games.searching') : t('games.detectAgain'))}</span>
            </button>
            <button class="btn btn--ghost" data-action="deep-scan" data-game="${esc(info.id)}"${searching ? ' disabled' : ''}>
              ${esc(t('games.deep'))}
            </button>
          </div>
          ${searching ? `<p class="muted small"><i class="dot dot--busy"></i>${esc(
            item.progress?.where
              ? t('games.searchingWhere', { where: shortPath(item.progress.where) })
              : t(item.progress?.code ? 'p.' + item.progress.code : 'games.searching')
          )}</p>` : ''}
        </div>
      </section>

      <ol class="steps">${steps}</ol>

      <section class="panel">
        <div class="panel__head">
          <h2>${icon('video')}<span>${esc(t('notfound.video'))}</span></h2>
          <span class="muted small">${esc(t('notfound.videoHint'))}</span>
        </div>
        ${
          help.video
            ? `<div class="video" id="videoBox" style="background-image:url('${esc(gameArt(info.id))}')">
                 <button class="video__play" data-action="play-video" data-video="${esc(help.video)}" data-title="${esc(t('notfound.video'))}">
                   ${icon('play')}
                 </button>
               </div>
               <div class="row__head">
                 <span class="muted small">${esc(t('notfound.offline'))}</span>
                 <button class="btn btn--ghost btn--sm" data-action="open-url" data-url="https://www.youtube.com/watch?v=${esc(help.video)}">
                   ${icon('external')}<span>${esc(t('notfound.openVideo'))}</span>
                 </button>
               </div>`
            : `<p class="muted">${esc(t('notfound.offline'))}</p>`
        }
      </section>

      ${
        help.links.length
          ? `<section class="panel">
               <div class="panel__head"><h2>${icon('book')}<span>${esc(t('notfound.guides'))}</span></h2></div>
               <ul class="links">
                 ${help.links
                   .map(
                     ([label, url]) =>
                       `<li><button class="linkrow" data-action="open-url" data-url="${esc(url)}">
                          <span>${esc(label)}</span>${icon('external')}
                        </button></li>`
                   )
                   .join('')}
               </ul>
             </section>`
          : ''
      }
    </div>`;
}

/* --- экран игры ---------------------------------------------------- */

function renderGame() {
  const item = entry(state.activeGameId);
  const game = item?.game;
  if (!item) return emptyGamesBlock();
  if (!game?.found) return renderGames();

  // Счётчики на вкладках, как у Modrinth: сразу видно, сколько модов и есть ли проблемы.
  const installedCount = (state.library[game.id] ?? (state.activeGameId === game.id ? state.mods : []) ?? []).length;
  const catalogTotal =
    (state.catalogFor === game.id && !state.catalogMeta.query ? state.catalogMeta.total : 0) || state.catalogTotals[game.id] || 0;
  const catalogCount = catalogTotal ? formatCount(catalogTotal) : '';
  const tabs = [
    ['downloads', t('games.downloads'), 'list', installedCount ? String(installedCount) : ''],
    ['market', t('games.market'), 'shop', catalogCount],
    ['log', t('games.log'), 'warn', state.problems.length ? String(state.problems.length) : '', true],
  ];

  const body =
    state.gameTab === 'market'
      ? renderMarket(game)
      : state.gameTab === 'log'
        ? renderLog(game)
        : renderInstalled(game);

  return `
    <div class="page page--game" style="${accentStyle(game.accent)}">
      <header class="ghero${gameLogo(game.id) ? ' has-logo' : ''}">
        <div class="ghero__bg" style="background-image:url('${esc(gameArt(game.id))}');animation-delay:${driftPhase()}"></div>
        <div class="ghero__body">
          ${
            gameLogo(game.id)
              ? `<h1 class="sr-only">${esc(game.name)}</h1><img class="ghero__logo" src="${esc(gameLogo(game.id))}" alt="${esc(game.name)}" referrerpolicy="no-referrer" />`
              : `<h1 class="ghero__title">${esc(game.name)}</h1>`
          }
          <p class="ghero__loader">
            <i class="dot dot--${game.loader.installed ? 'ok' : 'warn'}"></i>
            ${esc(
              game.loader.installed
                ? t('games.loaderReady', { loader: game.loader.name })
                : t('games.loaderMissing', { loader: game.loader.name })
            )}
          </p>
          <p class="ghero__path" title="${esc(game.path)}">${icon('folder')}<span>${esc(game.path)}</span><span class="muted">· ${esc(t('source.' + game.pathSource))}</span></p>
          <div class="ghero__actions">
            ${
              game.loader.installed
                ? `<button class="btn btn--play" data-action="play" data-game="${esc(game.id)}">
                     ${icon('play')}<span>${esc(t('games.play'))}</span>
                   </button>`
                : `<button class="btn btn--play" data-action="install-loader" data-game="${esc(game.id)}">
                     ${icon('download')}<span>${esc(t('games.installLoader', { loader: game.loader.name }))}</span>
                   </button>`
            }
            <button class="btn btn--glass" data-action="open-folder" data-path="${esc(game.path)}">
              ${icon('folder')}<span>${esc(t('games.openFolder'))}</span>
            </button>
          </div>
        </div>
        ${gameShotsBlock(game.id)}
      </header>

      ${game.writable ? '' : `<div class="warnbar">${icon('warn')}<span>${esc(t('games.noWrite'))}</span><button class="btn btn--sm btn--primary" data-action="elevate">${esc(t('error.elevate.btn'))}</button></div>`}

      <div class="layout">
        <nav class="sidetabs" role="tablist" aria-label="${esc(game.name)}">
          ${tabs
            .map(
              ([id, label, ico, count, warn]) =>
                `<button class="sidetab${state.gameTab === id ? ' is-active' : ''}" role="tab" aria-selected="${state.gameTab === id}" data-action="tab" data-tab="${id}">
                   ${icon(ico)}<span>${esc(label)}</span>${count ? `<em class="sidetab__count${warn ? ' is-warn' : ''}">${esc(count)}</em>` : ''}
                 </button>`
            )
            .join('')}
        </nav>
        <div class="layout__body">${body}</div>
      </div>
    </div>`;
}

/**
 * Медленный «наплыв» арта в шапке идёт по общим часам: после перерисовки
 * он продолжается с того же места, а не прыгает к началу.
 */
function driftPhase() {
  const period = 42000; // 21 с туда и 21 с обратно
  return `-${((performance.now() % period) / 1000).toFixed(2)}s`;
}

/** Кадры из игры в шапке её страницы: четыре скриншота, клик — во весь экран. */
function gameShotsBlock(gameId) {
  const shots = gameShots(gameId);
  if (!shots.length) return '';
  const shown = shots.slice(0, 4);
  const more = shots.length - shown.length;
  return `
    <div class="ghero__shots" aria-label="${esc(t('games.shots'))}">
      ${shown
        .map(
          (shot, i) => `
            <button class="gshot" type="button" style="--i:${i}" data-action="zoom" data-src="${esc(shot.full)}" title="${esc(t('games.shots'))}">
              <img src="${esc(shot.thumb)}" alt="" loading="lazy" referrerpolicy="no-referrer" />
              ${i === shown.length - 1 && more > 0 ? `<span class="gshot__more">+${more}</span>` : ''}
            </button>`
        )
        .join('')}
      ${shots
        .slice(4)
        .map((shot) => `<span hidden data-action="zoom" data-src="${esc(shot.full)}"></span>`)
        .join('')}
    </div>`;
}

function renderInstalled(game) {
  const problems = state.problems.length
    ? `<div class="warnbar">${icon('warn')}<span>${esc(
        t('inst.problems', { list: state.problems.map((p) => p.missing).join(', ') })
      )}</span></div>`
    : '';

  const unmanaged = state.unmanaged.length
    ? `<p class="muted small">${esc(t('inst.unmanaged', { list: state.unmanaged.join(', ') }))}</p>`
    : '';

  const list = state.mods.length
    ? `<div class="mods">${state.mods.map((mod) => installedRow(game, mod)).join('')}</div>`
    : `<section class="empty">
         <h3>${esc(t('inst.empty'))}</h3>
         <p>${esc(t('inst.empty.text'))}</p>
         <button class="btn btn--primary" data-action="tab" data-tab="market">${esc(t('games.market'))}</button>
       </section>`;

  return `
    <div class="panel">
      <div class="panel__head">
        <h2>${esc(t('inst.title'))}</h2>
        <button class="btn btn--ghost btn--sm" data-action="install-file" data-game="${esc(game.id)}">
          ${icon('folder')}<span>${esc(t('inst.fromFile'))}</span>
        </button>
      </div>
      ${problems}
      ${list}
      ${unmanaged}
    </div>`;
}

/** Идентификатор мода в каталоге: у модов с Nexus в реестре он с приставкой. */
function catalogIdOf(game, mod) {
  if (mod.source === 'file') return null;
  const nexusId = /^nexus:[^:]+:(\d+)$/.exec(mod.id);
  if (nexusId) return game.catalog.kind === 'nexus' ? nexusId[1] : null;
  return game.catalog.kind === 'nexus' ? null : mod.id;
}

function installedRow(game, mod) {
  const picture = mod.icon || state.installedMedia[game.id]?.[mod.id] || null;
  const catalogId = catalogIdOf(game, mod);
  // Картинки нет — такая же цветная обложка с инициалами, как у карточек без картинки.
  const thumb = picture
    ? `<div class="modrow__icon has-img" style="background-image:url('${esc(picture)}')"></div>`
    : `<div class="modrow__icon is-generated" style="${gradientStyle(mod)}">${esc(initials(mod.name))}</div>`;
  const title = catalogId
    ? `<button class="modrow__name" data-action="open-mod" data-game="${esc(game.id)}" data-mod="${esc(catalogId)}">${esc(mod.name)}</button>`
    : `<span class="modrow__name">${esc(mod.name)}</span>`;
  return `
    <article class="modrow${mod.enabled ? '' : ' is-off'}">
      ${thumb}
      <div class="modrow__body">
        <h4>${title}${mod.version ? ` <span class="muted">${esc(mod.version)}</span>` : ''}</h4>
        <p class="muted small">
          ${catalogId ? ratingLine(game.id, catalogId, { compact: true }) : ''}
          ${esc(mod.author || '')}
          ${mod.missing ? `· <span class="danger">${esc(t('inst.missing'))}</span>` : ''}
          ${!mod.enabled ? `· ${esc(t('inst.off'))}` : ''}
        </p>
      </div>
      <div class="modrow__actions">
        <button class="btn btn--ghost btn--sm" data-action="toggle-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}" data-enabled="${mod.enabled ? '0' : '1'}">
          ${esc(mod.enabled ? t('mod.disable') : t('mod.enable'))}
        </button>
        <button class="btn btn--ghost btn--sm btn--danger" data-action="remove-mod" data-game="${esc(game.id)}" data-mod="${esc(mod.id)}">
          ${icon('trash')}
        </button>
      </div>
    </article>`;
}

function renderMarket(game) {
  return `
    <div class="panel">
      <div class="panel__head">
        <h2>${esc(catalogTitle())}</h2>
        <div class="panel__tail">
          <span class="muted">${esc(t('popular.count', { n: fullNumber(state.catalogMeta.total || state.catalog.length) }))}</span>
          ${catalogTools(game)}
        </div>
      </div>
      ${catalogBody(game)}
    </div>`;
}

/** Заголовок каталога называет выбранный порядок: «Самые популярные», «Новые»… */
function catalogTitle() {
  if (state.catalogMeta.query) return t('popular.title');
  return state.catalogSort === 'popular' ? t('popular.title') : t('sort.' + state.catalogSort);
}

/**
 * Порядок и вид прямо над каталогом. Видны, когда правой панели нет:
 * окно уже 1360 px или панель скрыта кнопкой в шапке.
 */
function catalogTools(game) {
  const sorts = SORTS_BY_KIND[game.catalog?.kind] ?? ['popular'];
  const view = pref('catalogView');
  const sortButtons = sorts
    .map(
      (id) =>
        `<button class="ctool${state.catalogSort === id ? ' is-active' : ''}" type="button" role="radio" aria-checked="${state.catalogSort === id}" data-action="cat-sort" data-sort="${id}">${esc(t('sort.' + id))}</button>`
    )
    .join('');
  const viewButton = (id, ico) =>
    `<button class="ctool__view${view === id ? ' is-active' : ''}" type="button" data-action="set-pref" data-key="catalogView" data-value="${id}" title="${esc(t('view.' + id))}" aria-label="${esc(t('view.' + id))}">${icon(ico)}</button>`;
  return `
    <div class="cattools">
      <div class="cattools__sorts" role="radiogroup" aria-label="${esc(t('aside.sort'))}">${sortButtons}</div>
      <div class="cattools__view">${viewButton('grid', 'grid')}${viewButton('list', 'list')}</div>
    </div>`;
}

function renderLog(game) {
  const issues = state.issues;

  if (!issues) return `<div class="panel"><div class="loading"><div class="spinner"></div></div></div>`;

  if (!issues.available) {
    return `<div class="panel"><section class="empty"><h3>${esc(t('log.title'))}</h3><p>${esc(t('log.none'))}</p></section></div>`;
  }

  const rows = issues.issues.length
    ? `<h3>${esc(t('log.problems'))}</h3>
       <ul class="issues">
         ${issues.issues
           .map(
             (issue) =>
               `<li><b>${esc(issue.mod)}</b><span class="muted small">${esc(issue.message)}</span></li>`
           )
           .join('')}
       </ul>`
    : `<p class="ok-line">${icon('check')}${esc(t('log.clean'))}</p>`;

  return `
    <div class="panel">
      <div class="panel__head">
        <h2>${esc(t('log.title'))}</h2>
        <span class="muted small">${esc(
          t('log.updated', { time: new Date(issues.modifiedAt).toLocaleString() })
        )}</span>
      </div>
      ${rows}
      <button class="btn btn--ghost btn--sm" data-action="open-folder" data-path="${esc(issues.path)}">
        ${icon('external')}<span>${esc(t('log.open'))}</span>
      </button>
    </div>`;
}

/* --- экран «Игры» -------------------------------------------------- */

function renderGames() {
  return `
    <div class="page">
      <div class="page__head">
        <h1 class="page__title">${esc(t('games.title'))}</h1>
        <span class="muted">${esc(t('games.subtitle'))}</span>
      </div>
      <div class="gamelist">${state.order.map(gameRow).join('')}</div>
    </div>`;
}

function gameRow(gameId) {
  const item = entry(gameId);
  const info = item.info;
  const game = item.game;
  const searching = item.status === 'searching';

  const status = searching
    ? `<p class="muted"><i class="dot dot--busy"></i>${esc(
        item.progress?.where
          ? t('games.searchingWhere', { where: shortPath(item.progress.where) })
          : t(item.progress?.code ? 'p.' + item.progress.code : 'games.searching')
      )}</p>`
    : game?.found
      ? `<p class="path" title="${esc(game.path)}">${esc(game.path)}</p>
         <p class="muted small">${esc(t('source.' + game.pathSource))} · ${esc(
           game.loader.installed
             ? t('games.loaderReady', { loader: game.loader.name })
             : t('games.loaderMissing', { loader: game.loader.name })
         )}</p>`
      : `<p class="danger">${esc(t('games.notDetected'))}</p>
         <p class="muted small">${esc(
           item.status === 'missing' ? t('games.notDetected.text', { game: info.name }) : t('games.notSearched')
         )}</p>`;

  const actions = game?.found
    ? `<button class="btn btn--primary btn--sm" data-action="open-game" data-game="${esc(gameId)}">${esc(t('games.downloads'))}</button>
       <button class="btn btn--ghost btn--sm" data-action="pick-path" data-game="${esc(gameId)}">${esc(t('games.setPath'))}</button>
       <button class="btn btn--ghost btn--sm" data-action="rescan" data-game="${esc(gameId)}">${icon('refresh')}<span>${esc(t('games.detectAgain'))}</span></button>`
    : `<button class="btn btn--primary btn--sm" data-action="not-found" data-game="${esc(gameId)}">${esc(t('games.detect'))}</button>
       <button class="btn btn--ghost btn--sm" data-action="not-detected" data-game="${esc(gameId)}">${esc(t('games.setPath'))}</button>
       <button class="btn btn--ghost btn--sm" data-action="rescan" data-game="${esc(gameId)}">${icon('refresh')}<span>${esc(t('games.detectAgain'))}</span></button>
       <button class="btn btn--ghost btn--sm" data-action="deep-scan" data-game="${esc(gameId)}">${esc(t('games.deep'))}</button>`;

  return `
    <article class="gamerow" style="${accentStyle(info.accent)}">
      <div class="gamerow__icon">${esc(initials(info.shortName))}</div>
      <div class="gamerow__body">
        <h3>${esc(info.name)}</h3>
        ${status}
        ${game?.stalePath ? `<p class="muted small">${esc(t('games.stale', { path: game.stalePath }))}</p>` : ''}
      </div>
      <div class="gamerow__actions"${searching ? ' hidden' : ''}>${actions}</div>
    </article>`;
}

/* --- премиум, чай, настройки --------------------------------------- */

function renderPremium() {
  const plans = [
    ['premium.month1', 1],
    ['premium.month3', 3],
    ['premium.month12', 12],
  ];

  return `
    <div class="page page--narrow">
      <section class="premium">
        <div class="premium__head">
          <span class="premium__crown">${icon('crown')}</span>
          <div>
            <h1>${esc(t('premium.title'))}</h1>
            <p class="premium__price">${esc(t('premium.price'))}</p>
          </div>
          <div class="switch">
            ${['USD', 'BYN']
              .map(
                (code) =>
                  `<button class="switch__btn${state.currency === code ? ' is-active' : ''}" data-action="currency" data-currency="${code}">${code}</button>`
              )
              .join('')}
          </div>
        </div>

        <ul class="perks">
          ${['premium.perk1', 'premium.perk2', 'premium.perk3', 'premium.perk4']
            .map((key) => `<li>${icon('check')}<span>${esc(t(key))}</span></li>`)
            .join('')}
        </ul>

        <h3 class="premium__plans">${esc(t('premium.plans'))}</h3>
        <div class="plans">
          ${plans
            .map(
              ([key]) => `
                <div class="plan">
                  <span class="plan__name">${esc(t(key))}</span>
                  <span class="plan__price">${esc(t('premium.na'))}</span>
                  <span class="plan__cur">${esc(state.currency)}</span>
                </div>`
            )
            .join('')}
        </div>

        <button class="btn btn--gold btn--lg" disabled title="${esc(t('premium.soon'))}">${icon('crown')}<span>${esc(t('premium.buySoon'))}</span></button>
        <p class="muted small">${esc(t('premium.soon'))}</p>
      </section>
    </div>`;
}

function renderDonate() {
  return `
    <div class="page page--narrow">
      <section class="notice notice--center">
        <span class="notice__icon">${icon('cup')}</span>
        <h1>${esc(t('donate.title'))}</h1>
        <p>${esc(t('donate.text'))}</p>
        <p class="muted small">${esc(t('donate.soon'))}</p>
      </section>
    </div>`;
}

function renderSettings() {
  const tabs = [
    ['look', 'palette'],
    ['home', 'home'],
    ['downloads', 'download'],
    ['images', 'image'],
    ['accounts', 'key'],
    ['games', 'grid'],
    ['keys', 'keyboard'],
    ['about', 'info'],
  ];
  if (!tabs.some(([id]) => id === state.settingsTab)) state.settingsTab = 'look';

  const body = {
    look: settingsLook,
    home: settingsHome,
    downloads: settingsDownloads,
    images: settingsImages,
    accounts: settingsAccounts,
    games: settingsGames,
    keys: settingsKeys,
    about: settingsAbout,
  }[state.settingsTab]();

  return `
    <div class="page page--settings">
      <div class="page__head"><h1 class="page__title">${esc(t('settings.title'))}</h1></div>
      <div class="settings">
        <nav class="settings__nav" aria-label="${esc(t('settings.title'))}">
          ${tabs
            .map(
              ([id, ico]) => `
                <button class="settings__tab${state.settingsTab === id ? ' is-active' : ''}" data-action="settings-tab" data-tab="${id}">
                  ${icon(ico)}<span>${esc(t('settings.tab.' + id))}</span>
                </button>`
            )
            .join('')}
        </nav>
        <div class="settings__body" id="settingsBody">${body}</div>
      </div>
    </div>`;
}

/** Строка настройки: подпись и пояснение слева, управление справа. */
function settingRow(title, hint, control, { wide = false } = {}) {
  return `
    <div class="srow${wide ? ' srow--wide' : ''}">
      <div class="srow__text">
        <b>${esc(title)}</b>
        ${hint ? `<span class="muted small">${esc(hint)}</span>` : ''}
      </div>
      <div class="srow__control">${control}</div>
    </div>`;
}

/** Переключатель из нескольких вариантов. */
function segmented(key, options, current) {
  return `
    <div class="seg seg--settings" role="radiogroup">
      ${options
        .map(
          ([value, label]) =>
            `<button class="seg__btn${String(current) === String(value) ? ' is-active' : ''}" role="radio" aria-checked="${String(current) === String(value)}" data-action="set-pref" data-key="${esc(key)}" data-value="${esc(String(value))}">${esc(label)}</button>`
        )
        .join('')}
    </div>`;
}

/** Вкл/выкл. path — ключ настройки или «homeSections.chart». */
function toggle(path, on) {
  return `<button class="toggle${on ? ' is-on' : ''}" role="switch" aria-checked="${on}" data-action="toggle-pref" data-key="${esc(path)}"><i></i></button>`;
}

function settingsCard(title, rows, hint = '') {
  return `
    <section class="scard">
      <header class="scard__head"><h2>${esc(title)}</h2>${hint ? `<p class="muted small">${esc(hint)}</p>` : ''}</header>
      <div class="scard__rows">${rows}</div>
    </section>`;
}

function settingsLook() {
  const accents = Object.entries(ACCENTS)
    .map(
      ([id, color]) =>
        `<button class="swatch${pref('accent') === id ? ' is-active' : ''}" style="--c:${color}" data-action="set-pref" data-key="accent" data-value="${id}" title="${esc(t('settings.accent.' + id))}" aria-label="${esc(t('settings.accent.' + id))}"></button>`
    )
    .join('');
  return (
    settingsCard(
      t('settings.tab.look'),
      settingRow(
        t('settings.language'),
        '',
        `<div class="seg seg--settings">${[
          ['ru', 'Русский'],
          ['en', 'English'],
        ]
          .map(
            ([code, label]) =>
              `<button class="seg__btn${window.I18N.lang === code ? ' is-active' : ''}" data-action="set-lang" data-lang="${code}">${esc(label)}</button>`
          )
          .join('')}</div>`
      ) +
        settingRow(
          t('settings.theme'),
          t('settings.theme.hint'),
          segmented('theme', [
            ['dark', t('settings.theme.dark')],
            ['light', t('settings.theme.light')],
            ['system', t('settings.theme.system')],
          ], pref('theme'))
        ) +
        settingRow(t('settings.accent'), t('settings.accent.hint'), `<div class="swatches">${accents}</div>`) +
        settingRow(
          t('settings.zoom'),
          t('settings.zoom.hint'),
          segmented('zoom', [
            [0.9, '90%'],
            [1, '100%'],
            [1.1, '110%'],
            [1.25, '125%'],
          ], pref('zoom'))
        ) +
        settingRow(
          t('settings.density'),
          t('settings.density.hint'),
          segmented('density', [
            ['compact', t('settings.density.compact')],
            ['normal', t('settings.density.normal')],
            ['large', t('settings.density.large')],
          ], pref('density'))
        ) +
        settingRow(
          t('settings.motion'),
          t('settings.motion.hint'),
          segmented('motion', [
            ['full', t('settings.motion.full')],
            ['reduced', t('settings.motion.reduced')],
            ['off', t('settings.motion.off')],
          ], pref('motion'))
        )
    ) + `<p class="settings__note muted small">${icon('info')}<span>${esc(t('settings.golden'))}</span></p>`
  );
}

function settingsHome() {
  const sections = pref('homeSections');
  return (
    settingsCard(
      t('settings.tab.home'),
      settingRow(
        t('settings.start'),
        t('settings.start.hint'),
        segmented('startView', [
          ['home', t('settings.start.home')],
          ['last', t('settings.start.last')],
        ], pref('startView'))
      ) + settingRow(t('settings.heroAutoplay'), t('settings.heroAutoplay.hint'), toggle('heroAutoplay', pref('heroAutoplay')))
    ) +
    settingsCard(
      t('settings.sections'),
      ['recommend', 'chart', 'shelves', 'fresh']
        .map((id) => settingRow(t('settings.section.' + id), t('settings.section.' + id + '.hint'), toggle('homeSections.' + id, sections[id] !== false)))
        .join(''),
      t('settings.sections.hint')
    )
  );
}

function settingsDownloads() {
  return settingsCard(
    t('settings.tab.downloads'),
    settingRow(t('settings.notify'), t('settings.notify.hint'), toggle('notifyDone', pref('notifyDone'))) +
      settingRow(t('settings.sound'), t('settings.sound.hint'), `${toggle('soundDone', pref('soundDone'))}<button class="btn btn--ghost btn--sm" data-action="test-sound">${icon('play')}<span>${esc(t('settings.sound.test'))}</span></button>`) +
      settingRow(t('settings.dlAutoOpen'), t('settings.dlAutoOpen.hint'), toggle('dlAutoOpen', pref('dlAutoOpen')))
  );
}

function settingsImages() {
  const steamOk = Object.keys(state.gameMedia).length > 0;
  return (
    settingsCard(
      t('settings.gameArt'),
      settingRow(
        t('settings.gameArt.source'),
        steamOk ? t('settings.gameArt.hint') : t('settings.gameArt.offline'),
        segmented('gameArt', [
          ['steam', t('settings.gameArt.steam')],
          ['drawn', t('settings.gameArt.drawn')],
        ], pref('gameArt'))
      ) +
        `<div class="artpreview">${state.order
          .map((id) => {
            const info = entry(id)?.info;
            const logo = gameLogo(id);
            return `<figure class="artpreview__item" style="${accentStyle(info?.accent)}">
              <span class="artpreview__img" style="background-image:url('${esc(gameArt(id))}')">${logo ? `<img src="${esc(logo)}" alt="" referrerpolicy="no-referrer" />` : ''}</span>
              <figcaption>${esc(info?.name ?? id)}</figcaption>
            </figure>`;
          })
          .join('')}</div>` +
        settingRow(
          t('settings.cache'),
          t('settings.cache.hint'),
          `<button class="btn btn--ghost btn--sm" data-action="clear-cache">${icon('trash')}<span>${esc(t('settings.cache.clear'))}</span></button>`
        )
    ) +
    settingsCard(t('custom.title'), Object.entries(PHOTO_SLOTS).map(([slot, codes]) => customRow(slot, codes)).join(''), t('custom.hint'))
  );
}

function settingsAccounts() {
  return (
    accountSettingsCard() +
    settingsCard(
      t('settings.nexus'),
      `<p class="muted small">${esc(t('settings.nexus.hint'))}</p>
       <div class="field">
         <input id="nexusKey" type="password" value="${esc(state.settings.nexusApiKey ?? '')}" />
         <button class="btn btn--ghost btn--sm" data-action="nexus-check">${esc(t('settings.nexus.check'))}</button>
       </div>`
    ) +
    settingsCard(
      t('settings.reviews'),
      `<p class="muted small">${esc(
        !state.reviewsConfigured
          ? t('settings.reviews.off')
          : state.reviewsUnavailable
            ? t('settings.reviews.down')
            : t('settings.reviews.on', { n: state.reviewsCount })
      )}</p>
       ${
         state.account.signedIn
           ? `<p class="muted small">${esc(t('acc.reviewsAs', { name: state.account.name ?? '' }))}</p>`
           : `<span class="picks__label">${esc(t('settings.reviews.name'))}</span>
              <div class="field">
                <input id="reviewName" type="text" maxlength="32" placeholder="${esc(t('rev.name'))}" value="${esc(state.settings.reviewName ?? '')}" />
                <button class="btn btn--ghost btn--sm" data-action="save-review-name">${esc(t('common.save'))}</button>
              </div>`
       }`
    )
  );
}

function settingsGames() {
  const info = state.appInfo ?? {};
  return (
    settingsCard(
      t('settings.games'),
      settingRow(
        t('settings.rescanAll'),
        t('settings.rescanAll.hint'),
        `<button class="btn btn--ghost btn--sm" data-action="rescan-all">${icon('refresh')}<span>${esc(t('settings.rescanAll.go'))}</span></button>`
      ) +
        settingRow(
          t('games.title'),
          t('settings.games.hint'),
          `<button class="btn btn--ghost btn--sm" data-action="nav-games">${icon('grid')}<span>${esc(t('common.open'))}</span></button>`
        )
    ) +
    settingsCard(
      t('settings.data'),
      `<p class="muted small">${esc(t('settings.data.hint'))}</p>
       <p class="path">${esc(info.dataDir ?? '')}</p>` +
        settingRow(
          t('settings.data.folder'),
          '',
          `<button class="btn btn--ghost btn--sm" data-action="open-folder" data-path="${esc(info.dataDir ?? '')}">${icon('folder')}<span>${esc(t('common.open'))}</span></button>`
        ) +
        settingRow(
          t('settings.reset'),
          t('settings.reset.hint'),
          `<button class="btn btn--ghost btn--sm btn--danger" data-action="reset-prefs">${icon('refresh')}<span>${esc(t('settings.reset.go'))}</span></button>`
        )
    )
  );
}

function settingsKeys() {
  const keys = [
    ['Ctrl K', 'keys.search'],
    ['Alt ←', 'keys.back'],
    ['Alt →', 'keys.forward'],
    ['Ctrl J', 'keys.downloads'],
    ['Ctrl ,', 'keys.settings'],
    ['Ctrl 0', 'keys.home'],
    ['Ctrl 1–3', 'keys.games'],
    ['Esc', 'keys.close'],
  ];
  return settingsCard(
    t('settings.tab.keys'),
    `<dl class="keys">${keys.map(([k, key]) => `<dt>${k.split(' ').map((part) => `<kbd>${esc(part)}</kbd>`).join('')}</dt><dd>${esc(t(key))}</dd>`).join('')}</dl>`,
    t('keys.hint')
  );
}

function settingsAbout() {
  const info = state.appInfo ?? {};
  return (
    settingsCard(
      t('settings.about'),
      `<div class="about">
         <span class="about__logo">${icon('logo')}</span>
         <div><b class="about__name">Mod<span>Hub</span></b><span class="muted">${esc(t('settings.version'))} ${esc(info.version ?? '')}</span></div>
       </div>
       <dl class="facts">
         <dt>${esc(t('settings.admin'))}</dt><dd>${esc(info.elevated ? t('settings.admin.yes') : t('settings.admin.no'))}</dd>
         <dt>${esc(t('settings.nxm'))}</dt><dd>${esc(info.isDefaultProtocolClient ? t('settings.nxm.yes') : t('settings.nxm.no'))}</dd>
       </dl>`
    ) +
    settingsCard(
      t('settings.whatsnew'),
      `<ul class="news">${[1, 2, 3, 4, 5, 6].map((n) => `<li>${icon('sparkle')}<span>${esc(t('news.' + n))}</span></li>`).join('')}</ul>`
    )
  );
}

function customRow(slot, codes) {
  const current = state.settings.customArt?.[slot] ?? null;
  const label =
    slot === 'banner'
      ? t('custom.banner')
      : slot === 'notfound'
        ? t('custom.notfound')
        : t('custom.game', { game: entry(slot)?.info.name ?? slot });
  const fallback =
    slot === 'banner' ? ART.banners[0] : slot === 'notfound' ? ART.notFound : ART.games[slot];

  const tile = (code, url) => `
    <button class="pick${current === code ? ' is-active' : ''}" data-action="custom-art" data-slot="${esc(slot)}" data-code="${esc(code ?? '')}"
      style="background-image:url('${esc(url)}')">
      <span>${esc(code ?? t('custom.default'))}</span>
    </button>`;

  return `
    <div class="picks">
      <span class="picks__label">${esc(label)}</span>
      <div class="picks__row">
        ${tile(null, fallback)}
        ${codes.map((code) => tile(code, photoUrl(code, 'w=320&h=180&q=60'))).join('')}
      </div>
    </div>`;
}

/* ================================================================== *
 *  Аккаунт ModHub (1.9.4)
 *
 *  Почта и пароль — через Firebase того же проекта, что и отзывы.
 *  Зачем аккаунт: отзывы под вашим именем, и их можно править с любого
 *  компьютера. Без аккаунта всё работает как раньше.
 * ================================================================== */

/** Кружок с буквами имени — в рельсе, панели, настройках и форме отзыва. */
function avatarHtml(name, cls = '') {
  const label = name || '?';
  return `<span class="avatar${cls ? ' ' + cls : ''}" style="--h:${hash(label) % 360}">${esc(initials(label))}</span>`;
}

/** Свой кружок: у администратора — золотой, с короной. */
function myAvatar(cls = '') {
  const acc = state.account;
  const admin = acc.admin ? ` avatar--admin` : '';
  const crown = acc.admin ? `<i class="avatar__crown">${icon('crown')}</i>` : '';
  return `<span class="avatar-wrap${acc.admin ? ' is-admin' : ''}">${avatarHtml(acc.name, cls + admin)}${crown}</span>`;
}

async function loadAccount({ refresh = true } = {}) {
  if (!api.account) return;
  const profile = await call(api.account.get(), { silent: true }).catch(() => null);
  if (profile) state.account = profile;
  renderRail();
  softRender();
  if (!refresh || !state.account.signedIn) return;
  // С сервера — «почта подтверждена» и имя, если их меняли на другом компьютере.
  const fresh = await call(api.account.refresh(), { silent: true }).catch(() => null);
  if (fresh) {
    state.account = fresh;
    renderRail();
    softRender();
  }
}

/** После входа, выхода или регистрации — всё, что зависит от «кто я». */
function accountChanged(profile) {
  if (profile) state.account = profile;
  renderRail();
  render();
  refreshRatings({ force: true });
  if (state.view === 'mod' && state.modView?.mod) loadReviews(state.modView.gameId, state.modView.mod.id);
}

/** Надёжность пароля: 0 — слабый, 1 — сойдёт, 2 — хороший, 3 — отличный. */
function passwordScore(value) {
  const text = String(value ?? '');
  if (text.length < 8) return 0;
  let score = 1;
  if (/[a-zа-яё]/i.test(text) && /\d/.test(text)) score += 1;
  if (text.length >= 12 && /[^a-zа-яё0-9]/i.test(text)) score += 1;
  else if (text.length >= 14) score += 1;
  return Math.min(score, 3);
}

/**
 * Окно входа и регистрации. mode: signin | signup | reset.
 * Ошибки показываются прямо в окне, а не всплывашкой: человек видит,
 * что не так, рядом с полем.
 */
function accountModal(mode = 'signin', carry = {}) {
  if (!state.account.configured) {
    toast(t('acc.off'), 'warn');
    return;
  }
  const email = carry.email ?? '';
  const name = carry.name ?? state.settings.reviewName ?? '';
  const title = t(`acc.${mode}.title`);
  const lead = t(`acc.${mode}.lead`);
  const tabs =
    mode === 'reset'
      ? ''
      : `<div class="seg seg--full acc__tabs" role="tablist">
           <button type="button" class="seg__btn${mode === 'signin' ? ' is-active' : ''}" data-acc-mode="signin">${esc(t('acc.tab.signin'))}</button>
           <button type="button" class="seg__btn${mode === 'signup' ? ' is-active' : ''}" data-acc-mode="signup">${esc(t('acc.tab.signup'))}</button>
         </div>`;
  const passwordField = (autocomplete) => `
    <label class="acc__field">
      <span>${esc(t('acc.password'))}</span>
      <span class="acc__pass">
        <input id="accPassword" type="password" autocomplete="${autocomplete}" maxlength="128" required />
        <button type="button" class="acc__eye" id="accEye" title="${esc(t('acc.showPassword'))}" aria-label="${esc(t('acc.showPassword'))}">${icon('eye')}</button>
      </span>
    </label>`;

  openModal(`
    <div class="acc acc--${mode}">
      <button type="button" class="acc__x" data-close aria-label="${esc(t('common.close'))}">×</button>
      <div class="acc__brand"><span class="acc__logo">${icon('logo')}</span><b>Mod<span>Hub</span></b></div>
      <h3 class="acc__title">${esc(title)}</h3>
      <p class="acc__lead">${esc(lead)}</p>
      ${tabs}
      <form class="acc__form" id="accForm" novalidate>
        ${
          mode === 'signup'
            ? `<label class="acc__field"><span>${esc(t('acc.name'))}</span>
                 <input id="accName" type="text" maxlength="32" autocomplete="nickname" value="${esc(name)}" placeholder="${esc(t('acc.name.hint'))}" required /></label>`
            : ''
        }
        <label class="acc__field"><span>${esc(t('acc.email'))}</span>
          <input id="accEmail" type="email" maxlength="254" autocomplete="email" value="${esc(email)}" placeholder="name@mail.ru" required /></label>
        ${mode === 'signin' ? passwordField('current-password') : mode === 'signup' ? passwordField('new-password') : ''}
        ${
          mode === 'signup'
            ? `<div class="acc__meter" id="accMeter" data-score="0"><i></i><i></i><i></i><span>${esc(t('acc.pass.0'))}</span></div>`
            : ''
        }
        <p class="acc__error" id="accError" role="alert" hidden></p>
        <button class="btn btn--primary btn--lg acc__submit" id="accSubmit" type="submit">${esc(t(`acc.${mode}.go`))}</button>
      </form>
      ${
        mode === 'signin'
          ? `<button type="button" class="linkbtn acc__link" data-acc-mode="reset">${esc(t('acc.forgot'))}</button>`
          : mode === 'reset'
            ? `<button type="button" class="linkbtn acc__link" data-acc-mode="signin">← ${esc(t('acc.backToSignin'))}</button>`
            : ''
      }
      <p class="acc__note">${icon('shield')}<span>${esc(t('acc.note'))}</span></p>
    </div>`);

  const panel = $('#modalPanel');
  panel.classList.add('modal__panel--acc');
  panel.querySelectorAll('[data-acc-mode]').forEach((node) =>
    node.addEventListener('click', () =>
      accountModal(node.dataset.accMode, {
        email: $('#accEmail')?.value ?? email,
        name: $('#accName')?.value ?? name,
      })
    )
  );
  $('#accEye')?.addEventListener('click', () => {
    const input = $('#accPassword');
    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    $('#accEye').innerHTML = icon(show ? 'eyeOff' : 'eye');
    input.focus();
  });
  $('#accPassword')?.addEventListener('input', (event) => {
    const meter = $('#accMeter');
    if (!meter) return;
    const score = passwordScore(event.target.value);
    meter.dataset.score = String(score);
    meter.querySelector('span').textContent = t(`acc.pass.${score}`);
  });

  const focusFirst = mode === 'signup' && !name ? '#accName' : email ? '#accPassword' : '#accEmail';
  setTimeout(() => ($(focusFirst) ?? $('#accEmail'))?.focus(), 30);

  $('#accForm').addEventListener('submit', async (event) => {
    event.preventDefault();
    const button = $('#accSubmit');
    const errorBox = $('#accError');
    const showError = (text) => {
      errorBox.textContent = text;
      errorBox.hidden = false;
      panel.querySelector('.acc')?.classList.remove('is-shake');
      void panel.offsetWidth;
      panel.querySelector('.acc')?.classList.add('is-shake');
    };
    errorBox.hidden = true;

    const payload = {
      name: $('#accName')?.value ?? '',
      email: $('#accEmail')?.value ?? '',
      password: $('#accPassword')?.value ?? '',
    };
    // Самое частое — проверяем сразу, не дожидаясь сервера.
    if (mode === 'signup' && !payload.name.trim()) return showError(t('acc.err.name'));
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(payload.email.trim())) return showError(t('acc.err.email'));
    if (mode === 'signup' && payload.password.length < 8) return showError(t('acc.err.short'));
    if (mode === 'signin' && !payload.password) return showError(t('acc.err.password'));

    button.disabled = true;
    button.innerHTML = `<span class="spinner spinner--sm"></span><span>${esc(t(`acc.${mode}.busy`))}</span>`;
    const request =
      mode === 'signup' ? api.account.signUp(payload) : mode === 'signin' ? api.account.signIn(payload) : api.account.reset(payload.email);
    const result = await request.catch((error) => ({ ok: false, error: error.message }));
    if (!document.body.contains(button)) return;
    button.disabled = false;
    button.textContent = t(`acc.${mode}.go`);
    if (!result?.ok) return showError(result?.error || t('acc.err.generic'));

    if (mode === 'reset') {
      panel.querySelector('.acc__form').outerHTML = `
        <div class="acc__sent">${icon('mail')}<p>${esc(t('acc.reset.sent', { email: payload.email.trim() }))}</p></div>`;
      return;
    }
    closeModal();
    accountChanged(result.data);
    toast(t(mode === 'signup' ? 'acc.welcomeNew' : 'acc.welcome', { name: result.data?.name ?? '' }), 'ok');
  });
}

/** Окно «Удалить аккаунт»: пароль ещё раз — случайный клик ничего не сотрёт. */
function deleteAccountModal() {
  openModal(`
    <div class="modal__head"><h3>${esc(t('acc.delete.title'))}</h3></div>
    <p class="modal__text">${esc(t('acc.delete.text'))}</p>
    <label class="acc__field"><span>${esc(t('acc.password'))}</span>
      <input id="delPassword" type="password" autocomplete="current-password" maxlength="128" /></label>
    <p class="acc__error" id="delError" role="alert" hidden></p>
    <div class="modal__foot">
      <button class="btn btn--ghost" data-close>${esc(t('common.cancel'))}</button>
      <button class="btn btn--danger" id="delGo">${icon('trash')}<span>${esc(t('acc.delete.go'))}</span></button>
    </div>`);
  setTimeout(() => $('#delPassword')?.focus(), 30);
  const run = async () => {
    const password = $('#delPassword').value;
    if (!password) return;
    const button = $('#delGo');
    button.disabled = true;
    const result = await api.account.remove(password).catch((error) => ({ ok: false, error: error.message }));
    if (!document.body.contains(button)) return;
    button.disabled = false;
    if (!result?.ok) {
      $('#delError').textContent = result?.error || t('acc.err.generic');
      $('#delError').hidden = false;
      return;
    }
    closeModal();
    accountChanged(result.data);
    toast(t('acc.deleted'), 'ok');
  };
  $('#delGo').addEventListener('click', run);
  $('#delPassword').addEventListener('keydown', (event) => {
    if (event.key === 'Enter') run();
  });
}

/** Карточка аккаунта в Настройках → Аккаунты. */
function accountSettingsCard() {
  const acc = state.account;
  if (!acc.configured) {
    return settingsCard(t('acc.title'), `<p class="muted small">${esc(t('acc.off'))}</p>`);
  }
  if (!acc.signedIn) {
    return settingsCard(
      t('acc.title'),
      `<div class="accard accard--out">
         <span class="accard__icon">${icon('user')}</span>
         <div class="accard__body">
           <b>${esc(t('acc.out.title'))}</b>
           <ul class="accard__perks">
             <li>${icon('check')}<span>${esc(t('acc.perk.1'))}</span></li>
             <li>${icon('check')}<span>${esc(t('acc.perk.2'))}</span></li>
             <li>${icon('check')}<span>${esc(t('acc.perk.3'))}</span></li>
           </ul>
           <div class="accard__actions">
             <button class="btn btn--primary" data-action="account-open" data-mode="signup">${icon('user')}<span>${esc(t('acc.tab.signup'))}</span></button>
             <button class="btn btn--ghost" data-action="account-open" data-mode="signin">${esc(t('acc.tab.signin'))}</button>
           </div>
         </div>
       </div>`
    );
  }
  const since = acc.since ? formatMonth(acc.since) : '';
  return settingsCard(
    t('acc.title'),
    `<div class="accard">
       ${myAvatar('avatar--lg')}
       <div class="accard__who">
         <b>${esc(acc.name ?? '')}</b>
         <span class="muted small">${esc(acc.email ?? '')}</span>
         <span class="accard__badges">
           ${acc.admin ? `<span class="accard__badge is-admin">${icon('crown')}<span>${esc(t('acc.admin'))}</span></span>` : ''}
           <span class="accard__badge${acc.verified ? ' is-ok' : ''}">${icon(acc.verified ? 'shield' : 'mail')}<span>${esc(
             t(acc.verified ? 'acc.verified' : 'acc.unverified')
           )}</span></span>
         </span>
       </div>
       <button class="btn btn--ghost btn--sm" data-action="account-signout">${icon('logout')}<span>${esc(t('acc.signout'))}</span></button>
     </div>
     ${
       acc.admin
         ? `<div class="accard__admin">${icon('crown')}<div><b>${esc(t('acc.admin.title'))}</b><span>${esc(t('acc.admin.text'))}</span></div></div>`
         : acc.adminPending
           ? `<div class="accard__admin is-pending">${icon('crown')}<div><b>${esc(t('acc.admin.title'))}</b><span>${esc(t('acc.admin.pending'))}</span></div></div>`
           : ''
     }
     ${
       acc.verified
         ? ''
         : `<div class="accard__warn">${icon('mail')}<span>${esc(t('acc.verify.text', { email: acc.email ?? '' }))}</span>
              <button class="btn btn--ghost btn--sm" data-action="account-verify">${esc(t('acc.verify.again'))}</button>
              <button class="btn btn--ghost btn--sm" data-action="account-check">${esc(t('acc.verify.check'))}</button></div>`
     }` +
      settingRow(
        t('acc.rename'),
        t('acc.rename.hint'),
        `<div class="field field--inline"><input id="accRename" type="text" maxlength="32" value="${esc(acc.name ?? '')}" />
           <button class="btn btn--ghost btn--sm" data-action="account-rename">${esc(t('common.save'))}</button></div>`
      ) +
      settingRow(
        t('acc.password.change'),
        t('acc.password.hint', { email: acc.email ?? '' }),
        `<button class="btn btn--ghost btn--sm" data-action="account-password">${icon('lock')}<span>${esc(t('acc.password.send'))}</span></button>`
      ) +
      settingRow(
        t('acc.delete.title'),
        since ? t('acc.delete.hint', { since }) : t('acc.delete.hintPlain'),
        `<button class="btn btn--ghost btn--sm btn--danger" data-action="account-delete">${icon('trash')}<span>${esc(t('acc.delete.go'))}</span></button>`
      )
  );
}

/** Карточка в правой панели главной: войти — или «это вы». */
function accountAsideBlock() {
  const acc = state.account;
  if (!acc.configured) return '';
  if (!acc.signedIn) {
    return asideCard(
      t('acc.title'),
      `<p class="muted small">${esc(t('acc.aside.text'))}</p>
       <div class="aacts">
         <button class="aact aact--primary" type="button" data-action="account-open" data-mode="signup">${icon('user')}<span>${esc(t('acc.tab.signup'))}</span></button>
         <button class="aact" type="button" data-action="account-open" data-mode="signin">${icon('lock')}<span>${esc(t('acc.tab.signin'))}</span></button>
       </div>`,
      { icon: 'user' }
    );
  }
  return asideCard(
    t('acc.title'),
    `<button class="aprofile" type="button" data-action="nav-account">
       ${myAvatar()}
       <span class="aprofile__text"><b>${esc(acc.name ?? '')}${acc.admin ? ` <em class="aprofile__admin">${esc(t('rev.admin'))}</em>` : ''}</b><span>${esc(acc.verified ? acc.email ?? '' : t('acc.unverified'))}</span></span>
       ${icon('chevRight')}
     </button>`,
    { icon: 'user' }
  );
}

function openAccountSettings() {
  state.settingsTab = 'accounts';
  if (state.view === 'settings') render();
  else go('settings');
  renderRail();
}

/** Кнопка аккаунта внизу рельса, как в Modrinth App и Discord. */
function renderAccountRail() {
  const node = $('#navAccount');
  if (!node) return;
  const acc = state.account;
  node.hidden = !acc.configured;
  node.classList.toggle('is-in', Boolean(acc.signedIn));
  node.classList.toggle('is-admin', Boolean(acc.admin));
  node.classList.toggle('is-active', state.view === 'settings' && state.settingsTab === 'accounts');
  const face = node.querySelector('.rail__icon');
  const html = acc.signedIn ? myAvatar('avatar--rail') : icon('user');
  if (face.dataset.face !== html) {
    face.innerHTML = html;
    face.dataset.face = html;
  }
  node.querySelector('.rail__tip').textContent = acc.signedIn
    ? `${acc.name ?? ''}${acc.admin ? ' · ' + t('acc.admin') : ''}`
    : t('acc.tab.signin');
}

/* ================================================================== *
 *  Окна
 * ================================================================== */

function openModal(html) {
  const modal = $('#modal');
  $('#modalPanel').className = 'modal__panel';
  $('#modalPanel').innerHTML = html;
  modal.hidden = false;
  modal.querySelectorAll('[data-close]').forEach((node) =>
    node.addEventListener('click', closeModal)
  );
}

function closeModal() {
  $('#modal').hidden = true;
  $('#modalPanel').innerHTML = '';
}

function errorModal(text, { elevation = false, manualPath = null, logFile = null } = {}) {
  openModal(`
    <div class="modal__head"><h3>${esc(elevation ? t('error.elevation') : t('error.title'))}</h3></div>
    <p class="modal__text">${esc(text)}</p>
    <div class="modal__foot">
      <button class="btn btn--ghost" data-close>${esc(t('common.close'))}</button>
      ${
        logFile
          ? `<button class="btn btn--ghost" data-action="open-folder" data-path="${esc(logFile)}">
               ${icon('book')}<span>${esc(t('loader.log'))}</span>
             </button>`
          : ''
      }
      ${
        manualPath
          ? `<button class="btn btn--primary" data-action="run-manual" data-path="${esc(manualPath)}">
               ${icon('wrench')}<span>${esc(t('loader.manual'))}</span>
             </button>`
          : ''
      }
      ${elevation ? `<button class="btn btn--primary" id="elevateBtn">${esc(t('error.elevate.btn'))}</button>` : ''}
    </div>`);

  document.getElementById('elevateBtn')?.addEventListener('click', elevate);
}

function confirmModal({ title, text, confirmLabel, danger = false }) {
  return new Promise((resolve) => {
    openModal(`
      <div class="modal__head"><h3>${esc(title)}</h3></div>
      <p class="modal__text">${esc(text)}</p>
      <div class="modal__foot">
        <button class="btn btn--ghost" data-close>${esc(t('common.cancel'))}</button>
        <button class="btn ${danger ? 'btn--danger' : 'btn--primary'}" id="confirmYes">${esc(
          confirmLabel ?? t('common.continue')
        )}</button>
      </div>`);

    $('#confirmYes').addEventListener('click', () => {
      closeModal();
      resolve(true);
    });
    $('#modal')
      .querySelectorAll('[data-close]')
      .forEach((node) => node.addEventListener('click', () => resolve(false)));
  });
}

/** Диалог «Not detected!» с макета: поле пути, обзор и подсказка. */
function notDetectedModal(gameId) {
  const item = entry(gameId);
  const name = item.info.name;

  openModal(`
    <div class="modal__head"><h3>${esc(t('games.notDetected'))}</h3></div>
    <p class="modal__text">${esc(t('games.notDetected.text', { game: name }))}</p>
    <div class="field">
      <input id="pathInput" type="text" placeholder="C:\\Program Files (x86)\\${esc(name)}" value="${esc(
        item.game?.path ?? item.game?.stalePath ?? ''
      )}" />
      <button class="btn btn--ghost btn--sm" id="browseBtn">${esc(t('games.browse'))}</button>
    </div>
    <div class="modal__foot">
      <button class="btn btn--ghost" id="guideBtn">${esc(t('games.guide'))}</button>
      <button class="btn btn--ghost" data-close>${esc(t('common.cancel'))}</button>
      <button class="btn btn--primary" id="savePathBtn">${esc(t('common.save'))}</button>
    </div>`);

  $('#browseBtn').addEventListener('click', async () => {
    const picked = await call(api.app.pickFolder(), { silent: true }).catch(() => null);
    if (picked) $('#pathInput').value = picked;
  });

  $('#guideBtn').addEventListener('click', () => guideModal(gameId));

  $('#savePathBtn').addEventListener('click', async () => {
    const value = $('#pathInput').value.trim();
    if (!value) return;
    try {
      const game = await call(api.games.setPath(gameId, value));
      const current = entry(gameId);
      current.game = game;
      current.status = game.found ? 'found' : 'missing';
      closeModal();
      toast(t('toast.pathSaved'));
      renderRail();
      render();
    } catch {
      /* сообщение уже показано */
    }
  });
}

function guideModal(gameId) {
  openModal(`
    <div class="modal__head"><h3>${esc(t('guide.title'))}</h3></div>
    <ul class="guide">
      <li>${esc(t('guide.steam'))}</li>
      <li>${esc(t('guide.gog'))}</li>
      <li>${esc(t('guide.repack'))}</li>
      <li>${esc(t('guide.check'))}</li>
    </ul>
    <div class="modal__foot">
      <button class="btn btn--ghost" data-close>${esc(t('common.close'))}</button>
      <button class="btn btn--primary" id="guideBack">${esc(t('games.setPath'))}</button>
    </div>`);

  $('#guideBack').addEventListener('click', () => notDetectedModal(gameId));
}

/* ================================================================== *
 *  Действия
 * ================================================================== */

async function elevate() {
  closeModal();
  toast(t('toast.elevating'));
  await call(api.app.relaunchAsAdmin()).catch(() => toast(t('toast.elevateFailed'), 'error'));
}

async function installMod(gameId, modId) {
  const item = entry(gameId);
  if (!item?.game?.found) {
    go('notfound', { gameId });
    return;
  }

  // Без загрузчика мод не заработает — предлагаем поставить его сразу,
  // а не показываем ошибку «сначала установите SMAPI».
  if (!item.game.loader.installed) {
    const ok = await confirmModal({
      title: t('loader.needed.title', { loader: item.game.loader.name }),
      text: t('loader.needed.text', { loader: item.game.loader.name, game: item.game.name }),
      confirmLabel: t('games.installLoader', { loader: item.game.loader.name }),
    });
    if (!ok) return;
    await installLoader(gameId);
    if (!entry(gameId)?.game?.loader.installed) return;
  }

  // Кнопка сама станет прогрессом: отдельная полоса внизу окна не нужна.
  const known = knownMod(gameId, modId) ?? (state.modView?.mod?.id === modId ? state.modView.mod : null);
  const job = startJob({ gameId, modId, name: known?.name ?? modId, mod: known });
  try {
    const result = await call(api.mods.installFromCatalog(gameId, modId));
    if (result?.cancelled) {
      finishJob(job, { cancelled: true });
      return;
    }

    await refreshMods();
    finishJob(job, { ok: true });
    render();

    const count = result?.installed?.length ?? 0;
    if (result?.missing?.length) {
      toast(t('toast.missing', { n: count, list: result.missing.join(', ') }), 'warn');
    } else if (count > 1) {
      toast(t('toast.installedN', { n: count }));
    } else {
      toast(t('toast.installed'));
    }
  } catch (error) {
    // Причина уже показана окном; в загрузках остаётся «Повторить».
    finishJob(job, { ok: false, error: error?.message ?? null });
  } finally {
    closeBrowserWait();
  }
}

/**
 * Окно «скачайте файл на Nexus». Открывается, когда основной процесс
 * открыл страницу файла и ждёт архив в «Загрузках».
 */
function openBrowserWait(payload) {
  state.browserWait = { gameId: payload.gameId, modId: payload.modId, name: payload.mod, url: payload.url };
  openModal(`
    <div class="modal__head"><h3>${esc(t('nexus.wait.title'))}</h3></div>
    <ol class="howto">
      <li>${esc(t('nexus.wait.step1', { mod: payload.mod ?? '' }))}</li>
      <li>${esc(t('nexus.wait.step2'))}</li>
      <li>${esc(t('nexus.wait.step3'))}</li>
    </ol>
    <div class="waiting"><span class="spinner spinner--sm"></span><span>${esc(t('nexus.wait.status'))}</span></div>
    <div class="modal__foot">
      ${payload.url ? `<button class="btn btn--ghost" data-action="open-url" data-url="${esc(payload.url)}">${icon('external')}<span>${esc(t('nexus.wait.reopen'))}</span></button>` : ''}
      <button class="btn btn--ghost" data-action="bw-file">${icon('folder')}<span>${esc(t('nexus.wait.file'))}</span></button>
      <button class="btn btn--ghost" data-action="bw-cancel">${esc(t('common.cancel'))}</button>
    </div>`);
}

function closeBrowserWait() {
  if (!state.browserWait) return;
  state.browserWait = null;
  if (!$('#modal').hidden && $('#modalPanel .howto')) closeModal();
}

async function installLoader(gameId) {
  const item = entry(gameId);
  const job = startJob({ gameId, kind: 'loader', name: item?.game?.loader?.name ?? item?.info?.loaderName ?? '' });
  try {
    const game = await call(api.games.installLoader(gameId));
    item.game = game;
    item.status = 'found';
    finishJob(job, { ok: true });
    toast(t('toast.loaderReady', { loader: game.loader.name }));
    renderRail();
    await refreshMods();
    render();
  } catch (error) {
    finishJob(job, { ok: false, error: error?.message ?? null });
  }
}

async function installFromFile(gameId) {
  const files = await call(api.app.pickArchive(), { silent: true }).catch(() => []);
  if (!files || files.length === 0) return;

  showProgress(t('p.install.extract'));
  try {
    await call(api.mods.installFromFile(gameId, files));
    await refreshMods();
    render();
    toast(t('toast.installed'));
  } catch {
    /* сообщение уже показано */
  } finally {
    hideProgress();
  }
}

async function setLanguage(lang) {
  window.I18N.set(lang);
  state.settings.language = lang;
  await call(api.settings.write({ language: lang }), { silent: true }).catch(() => {});
  renderRail();
  render();
  rebuildAds();
}

async function setTheme(theme) {
  await setPref('theme', theme);
  render();
}

function applyTheme() {
  document.documentElement.dataset.theme = effectiveTheme();
}

/** Значение из data-value кнопки настройки — с правильным типом. */
function prefValue(key, raw) {
  if (key === 'zoom') return Number(raw) || 1;
  if (raw === 'true') return true;
  if (raw === 'false') return false;
  return raw;
}

async function resetPrefs() {
  const ok = await confirmModal({
    title: t('settings.reset.confirm'),
    text: t('settings.reset.confirm.text'),
    confirmLabel: t('settings.reset.go'),
    danger: true,
  });
  if (!ok) return;
  const patch = { customArt: {} };
  for (const key of Object.keys(PREF_DEFAULTS)) patch[key] = null;
  Object.assign(state.settings, patch);
  await call(api.settings.write(patch), { silent: true }).catch(() => {});
  applyPrefs();
  renderRail();
  render();
  toast(t('settings.reset.done'));
}

/* ================================================================== *
 *  Обработчики
 * ================================================================== */

const ACTIONS = {
  'nav-games': () => go('games'),
  'nav-settings': () => go('settings'),
  'custom-art': async (node) => {
    const next = { ...(state.settings.customArt ?? {}) };
    next[node.dataset.slot] = node.dataset.code || null;
    state.settings.customArt = next;
    await call(api.settings.write({ customArt: next }), { silent: true }).catch(() => {});
    render();
  },
  'nav-home': () => go('home'),
  'ad-open': () => {
    const ad = currentAd();
    if (!ad) return;
    if (ad.go) return go(ad.go);
    if (ad.url) api.app.openExternal(ad.url);
  },
  'ad-why': () => go('premium'),
  'dl-toggle': () => toggleDownloads(),
  'aside-toggle': async () => {
    await setPref('aside', pref('aside') === false);
    render();
  },
  'nav-premium': () => go('premium'),
  'account-open': (node) => accountModal(node.dataset.mode || 'signin'),
  'nav-account': () => openAccountSettings(),
  'account-signout': async () => {
    const profile = await call(api.account.signOut()).catch(() => null);
    accountChanged(profile);
    toast(t('acc.signedOut'), 'ok');
  },
  'account-rename': async () => {
    const value = $('#accRename')?.value ?? '';
    const profile = await call(api.account.rename(value)).catch(() => null);
    if (!profile) return;
    accountChanged(profile);
    toast(t('acc.renamed'), 'ok');
  },
  'account-verify': async () => {
    const ok = await call(api.account.verify()).catch(() => null);
    if (ok) toast(t('acc.verify.sent', { email: state.account.email ?? '' }), 'ok');
  },
  'account-check': async () => {
    const profile = await call(api.account.refresh(), { silent: true }).catch(() => null);
    if (profile) state.account = profile;
    render();
    toast(t(state.account.verified ? 'acc.verify.done' : 'acc.verify.notYet'), state.account.verified ? 'ok' : 'warn');
  },
  'account-password': async () => {
    const ok = await call(api.account.reset(state.account.email)).catch(() => null);
    if (ok) toast(t('acc.password.sent', { email: state.account.email ?? '' }), 'ok');
  },
  'account-delete': () => deleteAccountModal(),
  'cat-sort': (node) => {
    state.catalogSort = node.dataset.sort || 'popular';
    loadCatalog(state.catalogFor ?? state.activeGameId, state.catalogMeta.query);
  },
  'cat-category': (node) => {
    state.catalogCategory = node.dataset.category || null;
    loadCatalog(state.catalogFor ?? state.activeGameId, state.catalogMeta.query);
  },
  'settings-tab': (node) => {
    state.settingsTab = node.dataset.tab;
    render();
    renderRail();
    if (node.dataset.tab === 'accounts') loadAccount();
  },
  'set-pref': async (node) => {
    const key = node.dataset.key;
    await setPref(key, prefValue(key, node.dataset.value));
    if (key === 'gameArt') renderRail();
    render();
  },
  'toggle-pref': async (node) => {
    const [key, sub] = node.dataset.key.split('.');
    if (sub) {
      const next = { ...pref(key), [sub]: !(pref(key)[sub] !== false) };
      await setPref(key, next);
    } else {
      await setPref(key, !pref(key));
    }
    render();
  },
  'test-sound': () => chime(),
  'clear-cache': async () => {
    await call(api.app.clearCache?.() ?? Promise.resolve({ ok: true, data: true }), { silent: true }).catch(() => {});
    state.gameMedia = {};
    toast(t('settings.cache.done'));
    renderRail();
    render();
    loadGameMedia();
  },
  'reset-prefs': () => resetPrefs(),
  'dl-clear': () => {
    state.jobs = state.jobs.filter((job) => job.status === 'active');
    renderDownloads();
  },
  back: () => goBack(),
  forward: () => goForward(),
  hero: (node) => showHero(Number(node.dataset.index) || 0),
  'hero-step': (node) => showHero(state.hero + (Number(node.dataset.dir) || 1)),
  chart: (node) => {
    state.homeChart = node.dataset.game || 'all';
    render();
  },
  shelf: (node) => {
    const track = document.getElementById(node.dataset.shelf);
    if (!track) return;
    const step = Math.max(260, track.clientWidth * 0.85) * (Number(node.dataset.dir) || 1);
    track.scrollBy({ left: step, behavior: 'smooth' });
  },
  'open-game': (node) => openGame(node.dataset.game),
  'open-popular': (node) => openPopular(node.dataset.game),
  'open-mod': (node) => openMod(node.dataset.game, node.dataset.mod),
  'install-mod': (node) => installMod(node.dataset.game, node.dataset.mod),
  'remove-mod': async (node) => {
    const mod = state.mods.find((m) => m.id === node.dataset.mod);
    const ok = await confirmModal({
      title: t('mod.removeConfirm', { name: mod?.name ?? node.dataset.mod }),
      text: t('mod.removeConfirm.text'),
      confirmLabel: t('mod.remove'),
      danger: true,
    });
    if (!ok) return;
    await call(api.mods.remove(node.dataset.game, node.dataset.mod)).catch(() => {});
    await refreshMods();
    toast(t('toast.removed'));
    render();
  },
  'toggle-mod': async (node) => {
    const enabled = node.dataset.enabled === '1';
    await call(api.mods.setEnabled(node.dataset.game, node.dataset.mod, enabled)).catch(() => {});
    await refreshMods();
    toast(enabled ? t('toast.enabled') : t('toast.disabled'));
    render();
    refreshGate();
  },
  'install-loader': (node) => installLoader(node.dataset.game),
  'install-file': (node) => installFromFile(node.dataset.game),
  play: async (node) => {
    try {
      await call(api.games.launch(node.dataset.game));
      // Запуск с модом открывает его оценку — говорим об этом сразу.
      const unlocked = state.modView?.gameId === node.dataset.game && (await refreshGate());
      toast(t(unlocked ? 'rev.gate.unlocked' : 'toast.launched'));
    } catch {
      /* причина уже показана */
    }
  },
  tab: (node) => openTab(node.dataset.tab),
  'not-detected': (node) => notDetectedModal(node.dataset.game),
  'not-found': (node) => go('notfound', { gameId: node.dataset.game }),
  /**
   * Ролик подгружается только по клику.
   *
   * Так окно не лезет в сеть само по себе, а без интернета человек видит
   * картинку с кнопкой, а не белый прямоугольник вместо видео.
   */
  'play-video': (node) => {
    const box = document.getElementById('videoBox');
    if (!box) return;
    box.classList.add('is-playing');
    box.innerHTML =
      `<iframe src="https://www.youtube-nocookie.com/embed/${esc(node.dataset.video)}?rel=0&autoplay=1"` +
      ` title="${esc(node.dataset.title ?? '')}" allow="autoplay; encrypted-media; picture-in-picture"` +
      ' referrerpolicy="strict-origin-when-cross-origin" allowfullscreen></iframe>';
  },
  'run-manual': async (node) => {
    closeModal();
    await call(api.app.runManual(node.dataset.path)).catch(() => {});
  },
  'pick-path': (node) => notDetectedModal(node.dataset.game),
  guide: (node) => guideModal(node.dataset.game),
  rescan: (node) => searchGame(node.dataset.game, { forget: true }),
  'deep-scan': (node) => searchGame(node.dataset.game, { forget: true, deep: true }),
  'rescan-all': async () => {
    go('games');
    for (const id of state.order) await searchGame(id, { forget: true, quiet: true });
  },
  'open-url': (node) => api.app.openExternal(node.dataset.url),
  'open-folder': (node) => api.app.openFolder(node.dataset.path),
  elevate,
  currency: (node) => {
    state.currency = node.dataset.currency;
    render();
  },
  'set-lang': (node) => setLanguage(node.dataset.lang),
  'set-theme': (node) => setTheme(node.dataset.theme),
  'retry-catalog': () => loadCatalog(state.catalogFor ?? state.activeGameId, state.catalogMeta.query),
  'retry-home': () => loadHomeRows({ force: true }),
  'retry-mod': () => state.modView && openMod(state.modView.gameId, state.modView.modId),
  more: () => loadCatalog(state.catalogFor ?? state.activeGameId, state.catalogMeta.query, { append: true }),
  zoom: (node) => openLightbox(node.dataset.src, node.dataset.video || null),
  'bw-cancel': async () => {
    const wait = state.browserWait;
    closeBrowserWait();
    if (wait) await call(api.mods.browserCancel(wait.gameId, wait.modId), { silent: true }).catch(() => {});
  },
  'bw-file': async () => {
    const wait = state.browserWait;
    if (!wait) return;
    const files = await call(api.app.pickArchive(), { silent: true }).catch(() => []);
    if (!files?.length) return;
    await call(api.mods.browserFile(wait.gameId, wait.modId, files[0]), { silent: true }).catch(() => {});
  },
  'goto-reviews': () => {
    const target = document.getElementById('reviews');
    if (!target) return;
    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    setTimeout(() => target.querySelector('.review-form__name, .review-form__text')?.focus({ preventScroll: true }), 450);
  },
  'rev-star': (node) => {
    const form = node.closest('.review-form');
    const key = form?.dataset.key;
    if (!key) return;
    const draft = state.reviewDraft[key] ?? (state.reviewDraft[key] = { stars: 0, text: '', name: state.settings.reviewName ?? '' });
    draft.stars = Number(node.dataset.value) || 0;
    draft.touched = true;
    // Без перерисовки экрана: иначе пропал бы текст, который человек уже набрал.
    form.querySelectorAll('.starpick__btn').forEach((b) => b.classList.toggle('is-on', Number(b.dataset.value) <= draft.stars));
    const label = form.querySelector('.starpick__label');
    if (label) label.textContent = t(`rev.star.${draft.stars}`);
  },
  'rev-submit': async () => {
    const view = state.modView;
    if (!view?.mod || state.reviewSending) return;
    const gameId = view.gameId;
    const modId = view.mod.id;
    const key = ratingKey(gameId, modId);
    const form = [...document.querySelectorAll('.review-form')].find((node) => node.dataset.key === key) ?? null;
    const draft = state.reviewDraft[key] ?? { stars: 0, text: '', name: '' };
    if (form) {
      draft.name = form.querySelector('[data-draft="name"]')?.value ?? draft.name;
      draft.text = form.querySelector('[data-draft="text"]')?.value ?? draft.text;
    }
    if (!(draft.stars >= 1)) {
      toast(t('rev.pickStars'), 'warn');
      return;
    }
    const name = state.account.signedIn ? state.account.name ?? '' : String(draft.name ?? '').trim();
    if (!name) {
      toast(t('rev.needName'), 'warn');
      form?.querySelector('[data-draft="name"]')?.focus();
      return;
    }
    const hadMine = (state.reviewView?.reviews ?? []).some((r) => r.mine);
    state.reviewSending = true;
    render();
    try {
      const data = await call(
        api.reviews.submit({ gameId, modId, modName: view.mod.name, stars: draft.stars, text: draft.text ?? '', name })
      );
      state.reviewView = {
        key,
        loading: false,
        error: null,
        reviews: data?.reviews ?? [],
        configured: true,
        gate: data?.gate ?? state.reviewView?.gate ?? null,
      };
      applyRatings(data?.all);
      state.settings.reviewName = name;
      state.reviewDraft[key] = { stars: draft.stars, text: draft.text ?? '', name, touched: false };
      toast(t(hadMine ? 'rev.updatedToast' : 'rev.published'));
    } catch {
      // Причина уже показана. Если это «сначала поиграйте» — вместо формы
      // появятся шаги с кнопками.
      await refreshGate();
    } finally {
      state.reviewSending = false;
      render();
    }
  },
  'rev-delete': async () => {
    const view = state.modView;
    if (!view?.mod) return;
    const ok = await confirmModal({
      title: t('rev.deleteConfirm'),
      text: t('rev.deleteConfirm.text'),
      confirmLabel: t('rev.delete'),
      danger: true,
    });
    if (!ok) return;
    const key = ratingKey(view.gameId, view.mod.id);
    try {
      const data = await call(api.reviews.remove(view.gameId, view.mod.id));
      state.reviewView = {
        key,
        loading: false,
        error: null,
        reviews: data?.reviews ?? [],
        configured: true,
        gate: data?.gate ?? state.reviewView?.gate ?? null,
      };
      applyRatings(data?.all);
      delete state.reviewDraft[key];
      toast(t('rev.deleted'));
      render();
    } catch {
      /* причина уже показана */
    }
  },
  'rev-moderate': async (node) => {
    const view = state.modView;
    if (!view?.mod || !state.account.admin) return;
    const ok = await confirmModal({
      title: t('rev.moderate.confirm'),
      text: t('rev.moderate.text'),
      confirmLabel: t('rev.moderate'),
      danger: true,
    });
    if (!ok) return;
    const key = ratingKey(view.gameId, view.mod.id);
    try {
      const data = await call(api.reviews.moderate(view.gameId, view.mod.id, node.dataset.id));
      state.reviewView = { ...state.reviewView, key, loading: false, error: null, reviews: data?.reviews ?? [], configured: true };
      applyRatings(data?.all);
      toast(t('rev.moderated'));
      render();
    } catch {
      /* причина уже показана */
    }
  },
  'save-review-name': async () => {
    const name = ($('#reviewName')?.value ?? '').trim().slice(0, 32);
    state.settings.reviewName = name;
    await call(api.settings.write({ reviewName: name }), { silent: true }).catch(() => {});
    for (const draft of Object.values(state.reviewDraft)) if (!draft.touched) draft.name = name;
    toast(t('settings.reviews.saved'));
  },
  'nexus-check': async () => {
    const key = $('#nexusKey').value.trim();
    try {
      const info = await call(api.settings.checkNexusKey(key));
      toast(t('settings.nexus.ok', { name: info.name }));
      state.settings.nexusPremium = Boolean(info.premium);
      state.settings.nexusApiKey = '••••';
      render();
    } catch {
      /* сообщение уже показано */
    }
  },
};

/**
 * Вернулся из игры (запущенной из Steam или ярлыком) — проверяем пропуск
 * к оценке: лог загрузчика уже свежий, и шаги сменяются формой сами.
 */
window.addEventListener('focus', () => {
  if (!state.reviewView?.gate || state.reviewView.gate.ok) return;
  refreshGate()
    .then((unlocked) => {
      if (unlocked) toast(t('rev.gate.unlockedBack'));
    })
    .catch(() => {});
});

document.addEventListener('click', (event) => {
  const node = event.target.closest('[data-action]');
  if (!node) return;
  const action = ACTIONS[node.dataset.action];
  if (!action) return;
  event.preventDefault();
  action(node);
});

/** Текст отзыва сохраняется в черновик на каждое нажатие — перерисовка его не сотрёт. */
document.addEventListener('input', (event) => {
  const field = event.target.closest?.('[data-draft]');
  const key = field?.closest('.review-form')?.dataset.key;
  if (!key) return;
  const draft = state.reviewDraft[key] ?? (state.reviewDraft[key] = { stars: 0, text: '', name: '' });
  draft[field.dataset.draft] = field.value;
  draft.touched = true;
  if (field.dataset.draft === 'text') {
    const counter = field.closest('.review-form').querySelector('[data-count]');
    if (counter) counter.textContent = `${field.value.length}/1000`;
  }
});

$('#brand').addEventListener('click', () => go('home'));
$('#navHome').addEventListener('click', () => go('home'));
$('#navPremium').addEventListener('click', () => go('premium'));
$('#navDonate').addEventListener('click', () => go('donate'));
$('#navSettings').addEventListener('click', () => go('settings'));
$('#navAccount').addEventListener('click', () => {
  if (state.account.signedIn) openAccountSettings();
  else accountModal('signin');
});

let searchTimer = null;
$('#searchInput').addEventListener('input', (event) => {
  state.query = event.target.value;
  clearTimeout(searchTimer);
  searchTimer = setTimeout(async () => {
    const target = (entry(state.activeGameId)?.game?.found ? state.activeGameId : null) ?? readyGames()[0]?.game.id;
    if (!target) return;
    // Первый символ поиска уводит на экран каталога — это шаг истории.
    // Дальнейший набор уточняет тот же экран и новых шагов не добавляет.
    if (state.view !== 'game' && state.view !== 'popular') {
      remember({ view: 'popular', gameId: target, query: state.query });
    }
    state.activeGameId = target;
    if (state.view !== 'game') state.view = 'popular';
    renderRail();
    await loadCatalog(target, state.query);
  }, 250);
});

document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && !$('#modal').hidden) closeModal();
  // Alt+← / Alt+→ — как в браузере и в проводнике Windows.
  if (event.altKey && !event.ctrlKey && !event.shiftKey && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
    if (!$('#modal').hidden || document.getElementById('lightbox')) return;
    event.preventDefault();
    if (event.key === 'ArrowLeft') goBack();
    else goForward();
  }
});

/** Не перехватывать клавиши, пока человек печатает. */
function typing(event) {
  const el = event.target;
  return el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable);
}

document.addEventListener('keydown', (event) => {
  const ctrl = event.ctrlKey || event.metaKey;
  const key = event.key.toLowerCase();
  if ((ctrl && (key === 'k' || key === 'f' || key === 'л' || key === 'а')) || (!ctrl && event.key === '/' && !typing(event))) {
    event.preventDefault();
    $('#searchInput').focus();
    $('#searchInput').select();
    return;
  }
  if (ctrl && (key === 'j' || key === 'о')) {
    event.preventDefault();
    toggleDownloads();
    return;
  }
  if (ctrl && (event.key === ',' || event.key === 'б')) {
    event.preventDefault();
    go('settings');
    return;
  }
  if (ctrl && /^[0-9]$/.test(event.key) && $('#modal').hidden) {
    event.preventDefault();
    const n = Number(event.key);
    if (n === 0) go('home');
    else if (state.order[n - 1]) openGame(state.order[n - 1]);
    return;
  }
  if (event.key === 'Escape' && state.dlOpen) toggleDownloads(false);
});

// Окно загрузок закрывается кликом мимо него.
document.addEventListener('pointerdown', (event) => {
  if (!state.dlOpen) return;
  if (event.target.closest?.('#dlPanel, #dlBtn')) return;
  toggleDownloads(false);
});

/**
 * «Волна» от точки нажатия на кнопках — отклик, который чувствуется пальцем.
 * Только при полных анимациях; на рельсе её нет — там подсказки выходят
 * за край кнопки.
 */
const RIPPLE = '.btn, .getbtn, .card__cta, .navbtn, .seg__btn, .settings__tab, .gcard, .spot__thumb, .sidetab, .dlt__btn';
document.addEventListener('pointerdown', (event) => {
  if (pref('motion') !== 'full' || event.button !== 0) return;
  const host = event.target.closest?.(RIPPLE);
  if (!host || host.disabled || host.classList.contains('dl')) return;
  const rect = host.getBoundingClientRect();
  const size = Math.max(rect.width, rect.height) * 2.2;
  const wave = document.createElement('span');
  wave.className = 'ripple';
  wave.style.cssText = `width:${size}px;height:${size}px;left:${event.clientX - rect.left - size / 2}px;top:${event.clientY - rect.top - size / 2}px`;
  host.appendChild(wave);
  setTimeout(() => wave.remove(), 650);
});

// Боковые кнопки мыши «назад» и «вперёд».
document.addEventListener('mouseup', (event) => {
  if (event.button !== 3 && event.button !== 4) return;
  if (!$('#modal').hidden || document.getElementById('lightbox')) return;
  event.preventDefault();
  if (event.button === 3) goBack();
  else goForward();
});

/* ================================================================== *
 *  Прогресс и события основного процесса
 * ================================================================== */

function showProgress(step, detail = '') {
  $('#progressDock').hidden = false;
  $('#progressStep').textContent = step;
  $('#progressDetail').textContent = detail;
  $('#progressBar').classList.add('is-indeterminate');
  $('#progressBar').style.width = '';
}

function hideProgress() {
  $('#progressDock').hidden = true;
}

api.onProgress((payload) => {
  // Ход поиска игры показываем прямо в её карточке, а не в общем индикаторе.
  if (payload.scope === 'search') {
    const item = entry(payload.gameId);
    if (item && item.status === 'searching') {
      item.progress = { code: payload.code, where: payload.where };
      if (state.view === 'home' || state.view === 'games') render();
    }
    return;
  }

  if (payload.code === 'install.done' || payload.code === 'loader.done') {
    hideProgress();
    return;
  }

  if (payload.code === 'install.browser') openBrowserWait(payload);

  // Идёт загрузка, начатая кнопкой, — прогресс показывает сама кнопка
  // и окно загрузок, а не полоса внизу.
  const job = payload.gameId ? activeJobFor(payload.gameId, payload.mod ?? null) : null;
  if (job) {
    if (payload.code === 'install.warn') {
      if (payload.missing?.length) toast(t('toast.missing', { n: 0, list: payload.missing.join(', ') }), 'warn');
      return;
    }
    updateJob(job, payload);
    return;
  }

  if (payload.code === 'install.warn') {
    if (payload.missing?.length) {
      toast(t('toast.missing', { n: 0, list: payload.missing.join(', ') }), 'warn');
    }
    return;
  }

  const label = t('p.' + payload.code, { detail: payload.detail ?? '' });
  const withMod =
    payload.mod && payload.total
      ? `${label}: ${payload.mod} (${payload.index}/${payload.total})`
      : payload.mod
        ? `${label}: ${payload.mod}`
        : label;

  $('#progressDock').hidden = false;
  $('#progressStep').textContent = withMod;
  $('#progressDetail').textContent = payload.detail ?? '';

  const bar = $('#progressBar');
  if (typeof payload.ratio === 'number') {
    bar.classList.remove('is-indeterminate');
    bar.style.width = `${Math.round(payload.ratio * 100)}%`;
  } else {
    bar.classList.add('is-indeterminate');
    bar.style.width = '';
  }
});

api.onElevationCancelled?.(() => toast(t('toast.elevationCancelled'), 'warn'));

api.onNxmLink(async ({ link }) => {
  let job = null;
  try {
    const info = await call(api.nexus.describe(link));
    if (!info.gameId) {
      toast(t('toast.nxmForeign', { game: info.gameName }), 'warn');
      return;
    }

    // Если ModHub сам открыл эту страницу и ждёт файл — остаёмся где были.
    if (state.browserWait?.gameId !== info.gameId) await openGame(info.gameId);
    job = activeJobFor(info.gameId) ?? startJob({ gameId: info.gameId, modId: `nxm:${Date.now()}`, name: info.modName || t('p.install.nexus') });
    await call(api.nexus.install(info.gameId, link));
    await refreshMods();
    finishJob(job, { ok: true });
    render();
    toast(t('toast.nxmInstalled'));
  } catch (error) {
    if (job) finishJob(job, { ok: false, error: error?.message ?? null });
  } finally {
    hideProgress();
  }
});

/* ================================================================== *
 *  Запуск
 * ================================================================== */

/**
 * Заставка держится не меньше SPLASH_MIN — чтобы знак успел собраться и не
 * мигнул, — и не дольше SPLASH_MAX: сеть может думать долго, а человек
 * ждать не должен. Дальше главная проявляется из-под неё.
 */
const SPLASH_MIN = 1150;
const SPLASH_MAX = 3500;
const splashStarted = performance.now();
let splashHidden = false;

function hideSplash() {
  if (splashHidden) return;
  splashHidden = true;
  const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches;
  const left = Math.max(0, (reduced ? 120 : SPLASH_MIN) - (performance.now() - splashStarted));
  setTimeout(() => {
    document.body.classList.remove('is-booting');
    const splash = document.getElementById('splash');
    if (!splash) return;
    splash.dataset.state = 'out';
    setTimeout(() => splash.remove(), 520);
  }, left);
}
setTimeout(hideSplash, SPLASH_MAX);

(async function boot() {
  try {
    const [info, settings] = await Promise.all([
      call(api.app.info()),
      call(api.settings.read(), { silent: true }).catch(() => ({})),
    ]);

    state.appInfo = info;
    state.settings = settings ?? {};
    state.theme = pref('theme');
    window.I18N.set(settings?.language ?? info.language ?? 'ru');
    applyPrefs();
    // Настоящие картинки игр — параллельно со всем остальным.
    loadGameMedia();

    // Кто вошёл — до оценок: от этого зависит, какие отзывы «ваши».
    await loadAccount({ refresh: false });
    loadAccount();
    // Оценки с диска — сразу, свежие с сервера — следом и потом раз в три минуты.
    refreshRatings();
    loadAds().then(startAdRotation);
    await loadGames();
    loadLibrary();
    // «Открывать при запуске: последнюю игру».
    if (pref('startView') === 'last' && entry(state.settings.lastGame)?.game?.found && state.view === 'home') {
      openGame(state.settings.lastGame, 'downloads', { record: false });
    }
    await loadHomeRows();
    render();
    hideSplash();
    setInterval(rotateHero, 1000);
    setInterval(() => refreshRatings(), 3 * 60 * 1000);
  } catch (error) {
    $('#main').innerHTML = `
      <section class="empty"><h3>${esc(t('error.boot'))}</h3><p>${esc(error.message)}</p></section>`;
    hideSplash();
  }
})();
