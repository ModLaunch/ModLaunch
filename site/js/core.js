'use strict';

/* Общее для всех страниц сайта ModLaunch: язык, тема, меню, свежий выпуск,
   список игр, появление при прокрутке и доступ к ModLaunch Hub (Firestore). */

const REPO = 'ModLaunch/ModLaunch';
const SITE = 'https://modlaunch.github.io/ModLaunch/';
document.documentElement.classList.add('js');

/* ---------------------------------------------------------------- игры */

// Число модов — по всем каталогам игры (Thunderstore, Nexus, ModLinks), данные самопроверки 5.5.
const GAMES = [
  { id: 'lethal-company', color: '#C9A227', name: 'Lethal Company', steam: 1966720, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 39275 },
  { id: 'stardew-valley', color: '#6BAA3C', name: 'Stardew Valley', steam: 413150, loader: 'SMAPI', src: 'Nexus Mods', mods: 32660 },
  { id: 'valheim', color: '#E09F3E', name: 'Valheim', steam: 892970, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 9276 },
  { id: 'h3vr', color: '#D9A441', name: 'H3VR', steam: 450540, loader: 'BepInEx', src: 'Thunderstore', mods: 5788 },
  { id: 'repo', color: '#F2A93B', name: 'R.E.P.O.', steam: 3241660, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 5029 },
  { id: 'risk-of-rain-2', color: '#4FB0C6', name: 'Risk of Rain 2', steam: 632360, loader: 'BepInEx', src: 'Thunderstore', mods: 4784 },
  { id: 'subnautica', color: '#2BB3C0', name: 'Subnautica', steam: 264710, loader: 'BepInEx', src: 'Nexus · Thunderstore', mods: 2312 },
  { id: 'peak', color: '#E8743B', name: 'PEAK', steam: 3527290, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 2124 },
  { id: 'hollow-knight-silksong', color: '#C8394B', name: 'Hollow Knight: Silksong', steam: 1030300, loader: 'BepInEx', src: 'Nexus · Thunderstore', mods: 1231, fresh: true },
  { id: 'content-warning', color: '#F5D90A', name: 'Content Warning', steam: 2881650, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 1009 },
  { id: 'ultrakill', color: '#E03C31', name: 'ULTRAKILL', steam: 1229490, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 1000 },
  { id: 'rounds', color: '#F2C14E', name: 'ROUNDS', steam: 1557740, loader: 'BepInEx', src: 'Thunderstore', mods: 967 },
  { id: 'gtfo', color: '#9FB4C7', name: 'GTFO', steam: 493520, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 839, fresh: true },
  { id: 'hollow-knight', color: '#6F9BFF', name: 'Hollow Knight', steam: 367520, loader: 'Modding API', src: 'ModLinks · Nexus', mods: 805 },
  { id: 'outward', color: '#B98B4E', name: 'Outward', steam: 794260, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 669, fresh: true },
  { id: 'subnautica-below-zero', color: '#7FB4E6', name: 'Subnautica: Below Zero', steam: 848450, loader: 'BepInEx', src: 'Nexus · Thunderstore', mods: 569 },
  { id: 'dyson-sphere-program', color: '#3FA7F5', name: 'Dyson Sphere Program', steam: 1366540, loader: 'BepInEx', src: 'Thunderstore · Nexus', mods: 561 },
];
const TOTAL_MODS = GAMES.reduce((s, g) => s + g.mods, 0);
const gameById = (id) => GAMES.find((g) => g.id === id);
const steamArt = (id, kind = 'library_600x900') => `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/${id}/${kind}.jpg`;
const steamHeader = (id) => `https://cdn.akamai.steamstatic.com/steam/apps/${id}/header.jpg`;

/* ---------------------------------------------------------------- язык */

// Русский текст берётся прямо из разметки, английский — из словарей страниц.
const EN = {
  'nav.features': 'Features',
  'nav.games': 'Games',
  'nav.hub': 'Hub',
  'nav.creator': 'ModScript',
  'nav.faq': 'FAQ',
  'nav.download': 'Download',
  'nav.new': 'new',
  'foot.releases': 'All versions',
  'foot.issues': 'Report a bug',
  'foot.hub': 'ModLaunch Hub',
  'foot.modscript': 'ModScript language',
  'foot.note': 'Game names and art belong to their owners. ModLaunch is an unofficial fan project.',
  meta: 'Windows 10 / 11 · free',
  mb: 'MB',
  'hero.download': 'Download for Windows',
};
const ru = {};
function collectRu() {
  document.querySelectorAll('[data-t]').forEach((n) => { if (!(n.dataset.t in ru)) ru[n.dataset.t] = n.innerHTML.trim(); });
  document.querySelectorAll('[data-tp]').forEach((n) => { if (!(n.dataset.tp in ru)) ru[n.dataset.tp] = n.getAttribute('placeholder') || ''; });
}
collectRu();
Object.assign(ru, { meta: 'Windows 10 / 11 · бесплатно', mb: 'МБ' });

let lang = 'ru';
try {
  lang = localStorage.getItem('lang') || (/^(ru|uk|be|kk)/i.test(navigator.language) ? 'ru' : 'en');
} catch { lang = /^ru/i.test(navigator.language) ? 'ru' : 'en'; }
const t = (key, vars) => {
  let s = (lang === 'en' ? EN[key] : ru[key]) ?? ru[key] ?? EN[key] ?? key;
  if (vars) for (const [k, v] of Object.entries(vars)) s = s.replaceAll(`{${k}}`, v);
  return s;
};
const fmt = (n) => new Intl.NumberFormat(lang === 'en' ? 'en-US' : 'ru-RU').format(n);
const compact = (n) => new Intl.NumberFormat(lang === 'en' ? 'en-US' : 'ru-RU', { notation: 'compact', maximumFractionDigits: 1 }).format(n);
const langHooks = [];

function applyLang() {
  collectRu();
  document.documentElement.lang = lang;
  document.querySelectorAll('[data-t]').forEach((n) => (n.innerHTML = t(n.dataset.t)));
  document.querySelectorAll('[data-tp]').forEach((n) => n.setAttribute('placeholder', t(n.dataset.tp)));
  const btn = document.getElementById('lang');
  if (btn) btn.textContent = lang === 'en' ? 'RU' : 'EN';
  renderRelease();
  langHooks.forEach((f) => f());
}
document.getElementById('lang')?.addEventListener('click', () => {
  lang = lang === 'en' ? 'ru' : 'en';
  try { localStorage.setItem('lang', lang); } catch { /* без сохранения */ }
  applyLang();
});

/* ---------------------------------------------------------------- тема */

function setTheme(theme) {
  document.documentElement.dataset.theme = theme;
  try { localStorage.setItem('theme', theme); } catch { /* без сохранения */ }
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'light' ? '#f5f6fa' : '#0b0c12');
}
(() => {
  let saved = null;
  try { saved = localStorage.getItem('theme'); } catch { /* нет хранилища */ }
  setTheme(saved || (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'));
})();
document.getElementById('theme')?.addEventListener('click', () => setTheme(document.documentElement.dataset.theme === 'light' ? 'dark' : 'light'));

/* ---------------------------------------------------------------- шапка и меню */

const nav = document.getElementById('nav');
if (nav) {
  const onScroll = () => nav.classList.toggle('is-scrolled', window.scrollY > 10);
  addEventListener('scroll', onScroll, { passive: true });
  onScroll();
  document.getElementById('burger')?.addEventListener('click', () => nav.classList.toggle('is-open'));
  nav.querySelectorAll('.nav__links a').forEach((a) => a.addEventListener('click', () => nav.classList.remove('is-open')));
}

/* ---------------------------------------------------------------- свежий выпуск */

let release = null;
function renderRelease() {
  document.querySelectorAll('.js-meta').forEach((n) => {
    n.textContent = release
      ? `v${release.version} · ${release.setup ? `${Math.round(release.setup.size / 1048576)} ${t('mb')} · ` : ''}${t('meta')}`
      : t('meta');
  });
  if (!release) return;
  document.querySelectorAll('.js-version').forEach((n) => (n.textContent = release.version));
  if (release.setup) document.querySelectorAll('.js-setup').forEach((n) => (n.href = release.setup.browser_download_url));
  if (release.zip) document.querySelectorAll('.js-zip').forEach((n) => (n.href = release.zip.browser_download_url));
}
const releaseHooks = [];
async function loadRelease() {
  try {
    const r = await fetch(`https://api.github.com/repos/${REPO}/releases?per_page=100`, { headers: { Accept: 'application/vnd.github+json' } });
    if (!r.ok) return;
    const list = (await r.json()).filter((x) => !x.draft && !x.prerelease);
    if (!list.length) return;
    const latest = list[0];
    release = {
      version: String(latest.tag_name).replace(/^v/i, ''),
      setup: latest.assets.find((a) => /-Setup-.*\.exe$/i.test(a.name)),
      zip: latest.assets.find((a) => /\.zip$/i.test(a.name)),
      all: list,
      downloads: list.reduce((s, x) => s + x.assets.reduce((q, a) => q + (a.download_count || 0), 0), 0),
    };
    renderRelease();
    releaseHooks.forEach((f) => f(release));
  } catch { /* без сети — остаются ссылки на страницу выпусков */ }
}

/* ---------------------------------------------------------------- анимации */

function countUp(node, target) {
  if (!target) { node.textContent = '0'; return; }
  const start = performance.now();
  const suffix = node.dataset.suffix || '';
  const step = (now) => {
    const k = Math.min(1, (now - start) / 1500);
    node.textContent = fmt(Math.round(target * (1 - Math.pow(1 - k, 3)))) + suffix;
    if (k < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}
const seen = new IntersectionObserver((entries) => {
  for (const e of entries) {
    if (!e.isIntersecting) continue;
    e.target.classList.add('is-in');
    seen.unobserve(e.target);
    e.target.querySelectorAll?.('[data-count]').forEach((n) => countUp(n, Number(n.dataset.count)));
    if (e.target.dataset?.count) countUp(e.target, Number(e.target.dataset.count));
  }
}, { threshold: 0.12 });
function reveal(root = document) {
  const groups = new Map();
  root.querySelectorAll('.reveal:not(.is-in)').forEach((n) => {
    const i = groups.get(n.parentElement) ?? 0;
    groups.set(n.parentElement, i + 1);
    n.style.setProperty('--d', `${Math.min(i, 8) * 0.06}s`);
    seen.observe(n);
  });
}
const calm = matchMedia('(prefers-reduced-motion: reduce)').matches;

function toast(text) {
  const n = document.createElement('div');
  n.className = 'toast';
  n.textContent = text;
  document.body.append(n);
  setTimeout(() => n.remove(), 4200);
}

/* ---------------------------------------------------------------- ModLaunch Hub (Firestore, только чтение) */

const FB = { project: 'modhub-reviews', key: 'AIzaSyBl3EAL905nL2LT6HMtI_I-yPzZgujQvoY' };
const fbUrl = (suffix = '') => `https://firestore.googleapis.com/v1/projects/${FB.project}/databases/(default)/documents${suffix}`;
const withKey = (u) => u + (u.includes('?') ? '&' : '?') + 'key=' + FB.key;

function fromValue(v) {
  if (!v) return null;
  if ('stringValue' in v) return v.stringValue;
  if ('integerValue' in v) return Number(v.integerValue);
  if ('doubleValue' in v) return v.doubleValue;
  if ('booleanValue' in v) return v.booleanValue;
  if ('timestampValue' in v) return v.timestampValue;
  if ('arrayValue' in v) return (v.arrayValue.values || []).map(fromValue);
  if ('mapValue' in v) return fromFields(v.mapValue.fields);
  return null;
}
function fromFields(f = {}) {
  const o = {};
  for (const [k, v] of Object.entries(f)) o[k] = fromValue(v);
  return o;
}
class HubError extends Error {
  constructor(code, msg) { super(msg); this.code = code; }
}
async function fbFetch(url, body) {
  const r = await fetch(withKey(url), body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {});
  if (r.status === 403) throw new HubError('DENIED', 'rules');
  if (r.status === 404) throw new HubError('NOT_FOUND', 'missing');
  if (!r.ok) throw new HubError('SERVER', String(r.status));
  return r.json();
}
function toMod(doc) {
  const f = fromFields(doc.fields);
  const id = doc.name.slice(doc.name.lastIndexOf('/') + 1);
  return {
    id, uid: f.uid || '', author: f.author || '', name: f.name || '', summary: f.summary || f.about || '', description: f.description || '',
    game: f.game || '', version: f.version || '', kind: f.kind || 'script', code: f.code || '', tags: f.tags || [], images: f.images || [],
    size: f.size || 0, sha256: f.sha256 || '', chunks: f.chunks || 0, fileName: f.fileName || '', changelog: f.changelog || '',
    likes: f.likes || 0, downloads: f.downloads || 0, comments: f.comments || 0,
    created: f.created ? new Date(f.created) : null, updated: f.updated ? new Date(f.updated) : null,
  };
}
const Hub = {
  async all() {
    const rows = await fbFetch(fbUrl(':runQuery'), {
      structuredQuery: {
        from: [{ collectionId: 'creations' }],
        orderBy: [{ field: { fieldPath: 'updated' }, direction: 'DESCENDING' }],
        limit: 300,
      },
    });
    return rows.filter((r) => r.document).map((r) => toMod(r.document));
  },
  async get(id) { return toMod(await fbFetch(fbUrl(`/creations/${encodeURIComponent(id)}`))); },
  async versions(id) {
    const d = await fbFetch(fbUrl(`/creations/${encodeURIComponent(id)}/versions?pageSize=50`));
    return (d.documents || []).map((x) => fromFields(x.fields)).sort((a, b) => String(b.created).localeCompare(String(a.created)));
  },
  async comments(id) {
    const d = await fbFetch(fbUrl(`/creations/${encodeURIComponent(id)}/comments?pageSize=100&orderBy=created%20desc`));
    return (d.documents || []).map((x) => fromFields(x.fields));
  },
  trend(m) {
    const hours = Math.max(2, (Date.now() - (m.updated || m.created || new Date()).getTime()) / 3.6e6);
    return (m.likes * 3 + m.downloads + m.comments * 2 + 1) / Math.pow(hours, 1.3);
  },
  sort(list, how) {
    const by = {
      trending: (a, b) => Hub.trend(b) - Hub.trend(a),
      new: (a, b) => (b.created || 0) - (a.created || 0),
      updated: (a, b) => (b.updated || 0) - (a.updated || 0),
      downloads: (a, b) => b.downloads - a.downloads,
      likes: (a, b) => b.likes - a.likes || b.downloads - a.downloads,
    }[how] || ((a, b) => (b.updated || 0) - (a.updated || 0));
    return [...list].sort(by);
  },
  /** Собрать архив из кусков, проверить SHA-256 и отдать браузеру как файл. */
  async download(m, version, onProgress) {
    const ver = version?.version || m.version;
    const chunks = version?.chunks ?? m.chunks;
    const sha = version?.sha256 ?? m.sha256;
    let text = '';
    for (let n = 0; n < chunks; n++) {
      const d = await fbFetch(fbUrl(`/blobs/${encodeURIComponent(`${m.id}__${ver}__${n}`)}`));
      text += fromFields(d.fields).data || '';
      onProgress?.((n + 1) / chunks);
    }
    const bin = atob(text);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    const hash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map((b) => b.toString(16).padStart(2, '0')).join('');
    if (sha && hash !== sha) throw new HubError('BAD_HASH', 'sha');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([bytes], { type: 'application/zip' }));
    a.download = m.fileName || `${m.name.replace(/[^\w.-]+/g, '_')}-${ver}.zip`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 5000);
  },
};

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function ago(d) {
  if (!d) return '';
  const days = Math.floor((Date.now() - d.getTime()) / 864e5);
  const rtf = new Intl.RelativeTimeFormat(lang === 'en' ? 'en' : 'ru', { numeric: 'auto' });
  if (days < 1) return rtf.format(0, 'day');
  if (days < 30) return rtf.format(-days, 'day');
  if (days < 365) return rtf.format(-Math.floor(days / 30), 'month');
  return rtf.format(-Math.floor(days / 365), 'year');
}
const ICON = {
  code: '<svg viewBox="0 0 24 24"><path d="M16 18l6-6-6-6M8 6l-6 6 6 6"/></svg>',
  pkg: '<svg viewBox="0 0 24 24"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><path d="M3.27 6.96 12 12.01l8.73-5.05M12 22.08V12"/></svg>',
};

/** Карточка мода Hub — одна и та же на главной и на странице Hub. */
function modCard(m) {
  const g = gameById(m.game);
  const cover = m.images[0]
    ? `style="background-image:url('${esc(m.images[0])}')"`
    : g ? `style="background-image:linear-gradient(135deg,rgba(124,92,255,.55),rgba(10,11,16,.9)),url('${steamHeader(g.steam)}')"` : '';
  return `<button class="hmod reveal" data-mod="${esc(m.id)}">
    <div class="hmod__cover" ${cover}><span class="hmod__game">${esc(g?.name || m.game)} · ${esc(t('kind.' + m.kind))}</span>${m.images[0] ? '' : m.kind === 'package' ? ICON.pkg : ICON.code}</div>
    <div class="hmod__body">
      <h3>${esc(m.name)}</h3>
      <span class="hmod__by">${esc(t('by', { author: m.author }))} · ${esc(m.version)}</span>
      <span class="hmod__sum">${esc(m.summary)}</span>
      <span class="hmod__stats"><span>↓ ${compact(m.downloads)}</span><span>♥ ${compact(m.likes)}</span><span>💬 ${compact(m.comments)}</span><span>${esc(ago(m.updated))}</span></span>
    </div></button>`;
}
Object.assign(EN, { 'kind.script': 'Script', 'kind.package': 'Package', by: 'by {author}' });
Object.assign(ru, { 'kind.script': 'Скрипт', 'kind.package': 'Пакет', by: 'от {author}' });

/* ---------------------------------------------------------------- подсветка ModScript */

const KEYWORDS = ['mod', 'version', 'author', 'about', 'game', 'icon', 'website', 'needs', 'let', 'for', 'in', 'from', 'to', 'when', 'if', 'else',
  'edit', 'entry', 'dialogue', 'mail', 'image', 'config', 'ini', 'copy', 'write', 'json', 'print'];
function highlight(code) {
  return code.split('\n').map((line) => {
    let out = '';
    let first = true;
    const re = /#.*$|"(?:\\.|[^"\\])*"?|\$\{?\w+\}?|\[[^\]]*\]|\b\d+(?:\.\d+)*\b|[=+\-*/%{}]|[A-Za-z_][\w.\-]*|\s+|./g;
    for (const [tok] of line.matchAll(re)) {
      let cls = '';
      if (tok[0] === '#') cls = 't-c';
      else if (tok[0] === '"') cls = 't-s';
      else if (tok[0] === '$') cls = 't-v';
      else if (tok[0] === '[') cls = 't-sec';
      else if (/^\d/.test(tok)) cls = 't-n';
      else if (/^[=+\-*/%{}]$/.test(tok)) cls = 't-o';
      else if (KEYWORDS.includes(tok) && (first || ['in', 'from', 'to', 'if', 'json'].includes(tok))) cls = 't-k';
      if (!/^\s+$/.test(tok)) first = false;
      out += cls ? `<span class="${cls}">${esc(tok)}</span>` : esc(tok);
    }
    return out;
  }).join('\n');
}

addEventListener('DOMContentLoaded', () => {
  reveal();
  applyLang();
  loadRelease();
});
