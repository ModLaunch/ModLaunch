'use strict';

/* Сайт ModLaunch: язык, свежий выпуск с GitHub, анимации при прокрутке. */

const REPO = 'ModLaunch/ModLaunch';
document.documentElement.classList.add('js');

/* --- язык --- */

const EN = {
  'nav.features': 'Features',
  'nav.games': 'Games',
  'nav.screens': 'Screenshots',
  'nav.faq': 'FAQ',
  'nav.download': 'Download',
  'hero.chip': 'Out now:',
  'hero.title1': 'Mods',
  'hero.title2': 'in one click',
  'hero.lead': 'ModLaunch finds your games, installs the mod loader, pulls in dependencies and keeps everything updated. You just pick a mod and play.',
  'hero.download': 'Download for Windows',
  'hero.source': 'Source code',
  'hero.downloads': 'downloads',
  'hero.games': 'games',
  'hero.free': 'forever',
  'float.a1': 'Nautilus installed',
  'float.a2': 'together with BepInEx',
  'float.b1': 'Update ready',
  'float.b2': '3 mods · 1 click',
  'games.title': 'Eight games — one launcher',
  'feat.title': 'Everything you need for mods',
  'feat.sub': 'No copying files by hand, no unpacking archives, no guessing why the game won’t start.',
  'feat.1t': 'One-click install',
  'feat.1p': 'Hit “Install” and ModLaunch sets up BepInEx, SMAPI or the Modding API, downloads the mod and puts it where it belongs.',
  'feat.2t': 'Dependencies handled',
  'feat.2p': 'Required libraries come with the mod. Anything missing is shown right away.',
  'feat.3t': 'Shaders and graphics',
  'feat.3p': 'ReShade and presets install with the same single click.',
  'feat.4t': 'Friends and overlay',
  'feat.4p': 'See what your friends are playing. In game, press Ctrl+Shift+M for a timer, notes and save backups.',
  'feat.5t': 'Profiles and saves',
  'feat.5p': 'Separate mod sets for different playthroughs, and save backups before you experiment.',
  'feat.6t': 'Always up to date',
  'feat.6p': 'ModLaunch downloads its own updates, verifies the checksum and installs them with one button. Mods are checked for new versions too.',
  'feat.7t': 'Collections and modpacks',
  'feat.7p': 'Nexus collections and Thunderstore modpacks install as a whole — in exactly the versions the author picked.',
  'feat.8t': 'DXVK for older games',
  'feat.8p': 'Pick the exe of any older game and ModLaunch installs DXVK to get rid of stutter. One button removes it.',
  'screens.title': 'See it in action',
  'screens.home': 'Home',
  'screens.catalog': 'Catalog',
  'screens.mod': 'Mod page',
  'screens.overlay': 'In-game overlay',
  'cap.home': 'All your games, featured mods and top downloads on one screen.',
  'cap.catalog': 'Thousands of mods with sections, categories, search and sorting.',
  'cap.mod': 'Everything about a mod, and what will be installed along with it.',
  'cap.overlay': 'Ctrl+Shift+M in game: session time, friends, notes and a save backup.',
  'steps.title': 'Up and running in a minute',
  'steps.1t': 'Download',
  'steps.1p': 'One file, about 80 MB.',
  'steps.2t': 'Install',
  'steps.2p': 'Click “Install” and ModLaunch is on your desktop.',
  'steps.3t': 'Play with mods',
  'steps.3p': 'Your games are found automatically. Pick a mod and hit “Play”.',
  'faq.title': 'FAQ',
  'faq.1q': 'Is it free?',
  'faq.1a': 'Yes, completely. No subscriptions and no paid features.',
  'faq.2q': 'Windows says “unknown publisher”. Is it safe?',
  'faq.2a': 'Windows says that about any app without a paid code-signing certificate. Click “More info” → “Run anyway”. The source code is viewable on GitHub, and the installer is built there automatically.',
  'faq.3q': 'Where do the mods come from?',
  'faq.3a': 'From the same sites where the authors publish them: Nexus Mods, Thunderstore and ModLinks. ModLaunch doesn’t re-upload anything; it downloads the original.',
  'faq.4q': 'Do I need an account?',
  'faq.4a': 'No. A ModLaunch account is only needed for friends and reviews; everything else works without it.',
  'faq.5q': 'My game isn’t on the list',
  'faq.5a': 'Tell us in GitHub Issues which game to add next.',
  'final.title': 'Ready to try it?',
  'final.zip': 'Portable version (zip)',
  'final.nexus': 'Also on Nexus Mods',
  'foot.releases': 'All versions',
  'foot.issues': 'Report a bug',
  'foot.note': 'Subnautica, Stardew Valley, Lethal Company and Hollow Knight are trademarks of their owners. ModLaunch is an unofficial project.',
  meta: 'Windows 10 / 11 · free',
  'screens.shaders': 'Shaders',
  'cap.shaders': 'Graphics mods and ReShade shaders in their own section, installed with one click.',
};
const CAPTIONS_RU = {
  'cap.home': 'Все ваши игры, подборка модов и топ скачиваний — на одном экране.',
  'cap.catalog': 'Тысячи модов с разделами, категориями, поиском и сортировкой.',
  'cap.mod': 'Всё о моде и что поставится вместе с ним.',
  'cap.overlay': 'Ctrl+Shift+M в игре: время сеанса, друзья, заметки и копия сохранений.',
  'cap.shaders': 'Графические моды и шейдеры ReShade — в своём разделе, ставятся одной кнопкой.',
};

const ru = {};
document.querySelectorAll('[data-t]').forEach((node) => {
  ru[node.dataset.t] = node.textContent.trim();
});
Object.assign(ru, CAPTIONS_RU, { meta: 'Windows 10 / 11 · бесплатно' });

let lang = 'ru';
try {
  lang = localStorage.getItem('lang') || (/^ru|^uk|^be|^kk/i.test(navigator.language) ? 'ru' : 'en');
} catch {
  lang = /^ru/i.test(navigator.language) ? 'ru' : 'en';
}
const t = (key) => (lang === 'en' ? EN[key] : ru[key]) ?? ru[key] ?? key;

let release = null;
function applyLang() {
  document.documentElement.lang = lang;
  document.querySelectorAll('[data-t]').forEach((node) => {
    node.textContent = t(node.dataset.t);
  });
  document.getElementById('lang').textContent = lang === 'en' ? 'RU' : 'EN';
  renderRelease();
}
document.getElementById('lang').addEventListener('click', () => {
  lang = lang === 'en' ? 'ru' : 'en';
  try {
    localStorage.setItem('lang', lang);
  } catch {
    /* без сохранения */
  }
  applyLang();
});

/* --- свежий выпуск с GitHub: ссылка на установщик, размер, скачивания --- */

function renderRelease() {
  if (!release) {
    document.querySelectorAll('.js-meta').forEach((n) => (n.textContent = t('meta')));
    return;
  }
  const { version, setup, zip } = release;
  document.querySelectorAll('.js-version').forEach((n) => (n.textContent = version));
  document.querySelectorAll('.js-meta').forEach((n) => {
    n.textContent = `v${version} · ${setup ? `${Math.round(setup.size / 1048576)} ${lang === 'en' ? 'MB' : 'МБ'} · ` : ''}${t('meta')}`;
  });
  if (setup) document.querySelectorAll('.js-setup').forEach((n) => (n.href = setup.browser_download_url));
  if (zip) document.querySelectorAll('.js-zip').forEach((n) => (n.href = zip.browser_download_url));
}

async function loadRelease() {
  try {
    const response = await fetch(`https://api.github.com/repos/${REPO}/releases?per_page=100`, { headers: { Accept: 'application/vnd.github+json' } });
    if (!response.ok) return;
    const list = (await response.json()).filter((r) => !r.draft && !r.prerelease);
    if (!list.length) return;
    const latest = list[0];
    release = {
      version: String(latest.tag_name).replace(/^v/i, ''),
      setup: latest.assets.find((a) => /-Setup-.*\.exe$/i.test(a.name)),
      zip: latest.assets.find((a) => /\.zip$/i.test(a.name)),
    };
    const total = list.reduce((sum, r) => sum + r.assets.reduce((s, a) => s + (a.download_count || 0), 0), 0);
    const counter = document.querySelector('.js-downloads');
    counter.dataset.count = String(total);
    // Маленькое число на витрине не показываем — только когда скачиваний уже заметно много.
    document.querySelector('.js-dl').hidden = total < 100;
    if (counter.dataset.shown) countUp(counter);
    renderRelease();
  } catch {
    /* без сети — остаются ссылки на страницу выпусков */
  }
}

function countUp(node) {
  node.dataset.shown = '1';
  const target = Number(node.dataset.count || 0);
  if (!target) return;
  const start = performance.now();
  const fmt = new Intl.NumberFormat(lang === 'en' ? 'en-US' : 'ru-RU');
  const step = (now) => {
    const k = Math.min(1, (now - start) / 1400);
    node.textContent = fmt.format(Math.round(target * (1 - Math.pow(1 - k, 3))));
    if (k < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}

/* --- появление при прокрутке --- */

const groups = new Map();
document.querySelectorAll('.reveal').forEach((node) => {
  const parent = node.parentElement;
  const index = groups.get(parent) ?? 0;
  groups.set(parent, index + 1);
  node.style.setProperty('--d', `${Math.min(index, 6) * 0.08}s`);
});
const seen = new IntersectionObserver(
  (entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      entry.target.classList.add('is-in');
      seen.unobserve(entry.target);
      const counter = entry.target.querySelector?.('.js-downloads');
      if (counter) countUp(counter);
    }
  },
  { threshold: 0.12 }
);
document.querySelectorAll('.reveal').forEach((node) => seen.observe(node));

/* --- шапка темнеет при прокрутке --- */

const nav = document.getElementById('nav');
const onScroll = () => nav.classList.toggle('is-scrolled', window.scrollY > 10);
window.addEventListener('scroll', onScroll, { passive: true });
onScroll();

/* --- окно на первом экране наклоняется за мышью --- */

const calm = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
const tilt = document.getElementById('tilt');
if (!calm && tilt) {
  const win = tilt.querySelector('.window');
  tilt.addEventListener('pointermove', (event) => {
    const box = tilt.getBoundingClientRect();
    const x = (event.clientX - box.left) / box.width - 0.5;
    const y = (event.clientY - box.top) / box.height - 0.5;
    win.style.setProperty('--ry', `${x * 12}deg`);
    win.style.setProperty('--rx', `${-y * 8}deg`);
  });
  tilt.addEventListener('pointerleave', () => {
    win.style.removeProperty('--ry');
    win.style.removeProperty('--rx');
  });
}

/* --- подсветка карточек возможностей под курсором --- */

document.querySelectorAll('.fcard').forEach((card) => {
  card.addEventListener('pointermove', (event) => {
    const box = card.getBoundingClientRect();
    card.style.setProperty('--mx', `${event.clientX - box.left}px`);
    card.style.setProperty('--my', `${event.clientY - box.top}px`);
  });
});

/* --- скриншоты: вкладки и смена раз в несколько секунд --- */

const shot = document.getElementById('shot');
const cap = document.getElementById('shotCap');
const tabs = [...document.querySelectorAll('.tab')];
let current = 0;
let timer = null;
function show(index) {
  current = (index + tabs.length) % tabs.length;
  const name = tabs[current].dataset.shot;
  tabs.forEach((tab, i) => {
    tab.classList.toggle('is-on', i === current);
    tab.setAttribute('aria-selected', String(i === current));
  });
  shot.classList.add('is-out');
  setTimeout(() => {
    shot.src = `img/${name}.webp`;
    shot.alt = tabs[current].textContent;
    cap.dataset.t = `cap.${name}`;
    cap.textContent = t(`cap.${name}`);
    shot.classList.remove('is-out');
  }, 220);
}
function autoplay() {
  clearInterval(timer);
  if (!calm) timer = setInterval(() => show(current + 1), 6000);
}
tabs.forEach((tab, i) =>
  tab.addEventListener('click', () => {
    show(i);
    autoplay();
  })
);
// Картинки загружаем заранее, чтобы смена была мгновенной.
tabs.forEach((tab) => {
  new Image().src = `img/${tab.dataset.shot}.webp`;
});
autoplay();

applyLang();
loadRelease();
