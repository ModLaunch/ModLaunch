'use strict';

/* Главная страница (сайт 9.0): стена обложек, игры, свежие моды Hub, печатающийся ModScript,
   скриншоты и «Что нового» прямо из выпусков на GitHub. */

Object.assign(EN, {
  'nav.how': 'How it works',
  'hero.chip': 'Out now:',
  'hero.chip2': 'the new “Showcase” design',
  'hero.t1': 'Mods in one click.',
  'hero.t2': 'No guides, no archives, no copying files by hand.',
  'hero.lead': 'ModLaunch finds the games on your PC, installs the mod loader and every mod with its dependencies, then keeps them up to date. 18 games — from Stardew Valley to Minecraft — and any other with the “+” button.',
  'hero.how': 'How it works',
  'hero.games': 'games',
  'hero.mods': 'mods',
  'hero.free': 'forever',
  'float.a1': 'Nautilus installed',
  'float.a2': 'together with BepInEx',
  'float.b1': 'Updates ready',
  'float.b2': '3 mods · one click',
  'float.c1': 'Published to Hub',
  'float.c2': 'everyone can see your mod',
  'how.eyebrow': 'How it works',
  'how.title': 'Three steps and you’re playing with mods',
  'how.sub': 'No more “download the archive, unpack it into the folder, rename the dll”. ModLaunch does it for you.',
  'how.1t': 'Finds your games', 'how.1p': 'Steam, Epic, GOG and any folder — ModLaunch scans your drives and shows what it found.', 'how.1m': 'Looking for more…',
  'how.2t': 'Installs the loader', 'how.2p': 'BepInEx, SMAPI, Modding API, Fabric or Forge — the right one, in the right version.',
  'how.3t': 'Mods in one click', 'how.3p': 'Hit “Install” — the mod comes from its author’s site with everything it needs, and it already works in game.',
  'how.3m': '+ BepInEx pack, automatically', 'how.3b': 'Install', 'how.3u': 'updates itself',
  'games.eyebrow': 'Games',
  'games.title': '18 games — and any other',
  'games.sub': 'The loader installs itself, mods come from every catalog at once. Game missing? The “+” button finds it on disk.',
  'games.missing': 'Game missing? The “+” button',
  'games.plus': 'Any game',
  'games.plus.p': 'The “+” button: Steam, Epic, GOG or any exe',
  'games.new': 'new',
  'games.mods': '{n} mods',
  'mc.eyebrow': 'New in 8.5',
  'mc.title': 'Minecraft — with builds and Modrinth',
  'mc.p': 'Every version from 1.12 to the latest snapshots. The right loader installs itself, and you play through the official launcher.',
  'mc.1': 'Builds with their own mods, resource packs, shaders and worlds',
  'mc.2': 'The Modrinth catalog — only what fits your version and loader',
  'mc.3': 'Modpacks and .mrpack, import from Modrinth App, Prism Launcher and CurseForge',
  'mc.4': 'One-click mod updates, world backups and crash reports explained',
  'mc.cta': 'Download ModLaunch',
  'feat.eyebrow': 'Features',
  'feat.title': 'Everything for mods — in one app',
  'feat.sub': 'Installing is just the start. ModLaunch keeps track of versions, saves and making sure the game starts.',
  'f.versions': 'Updates and rollback', 'f.versions.p': 'Every version of a mod with its changelog. Update everything with one button — or roll back when a new version breaks the game.',
  'f.bp': 'Big Picture and gamepad', 'f.bp.p': 'Press F11 and ModLaunch goes fullscreen like Steam on a TV. Mods and launching — from the couch.',
  'f.center': 'Mods center', 'f.center.p': 'Updates, tracked, favorites and download history — on one page.',
  'f.lib': 'A Steam-style library', 'f.lib.p': 'Real covers, collections, sorting by hours played and a right-click menu.',
  'f.overlay': 'In-game overlay', 'f.overlay.p': 'Ctrl+Shift+M right in the game: session time, friends, notes and a save backup.',
  'f.gfx': 'ReShade and DXVK', 'f.gfx.p': 'Shaders and presets in one click. DXVK removes stutter in older games.',
  'x.1': 'Profiles and save backups', 'x.2': 'File conflicts', 'x.3': 'Nexus without Premium', 'x.4': '“+” for any game', 'x.5': 'Friends — who plays what',
  'x.6': 'Dark, OLED and light themes', 'x.7': 'Quick jump with Ctrl+P', 'x.8': 'Mod tracking', 'x.9': 'Updates itself',
  'hub.eyebrow': 'ModLaunch Hub',
  'hub.title': 'Community mods — no middlemen',
  'hub.sub': 'No ads and no paid “fast download”. Every mod installs in the app with one button, and here you can view and download it.',
  'hub.all': 'All Hub mods',
  'hub.empty': 'Be the first author on Hub',
  'hub.empty.p': 'Publish a ready mod archive or build one in the ModLaunch workshop — everyone sees it right away, in the app and on this site.',
  'hub.empty.btn': 'How to publish',
  'hub.soon': 'ModLaunch Hub is opening',
  'hub.soon.p': 'The gallery is being switched on right now. Mods published in the app will appear here.',
  'cr.eyebrow': 'Creator Hub · ModScript',
  'cr.title': 'Make your own mod in five minutes',
  'cr.sub': 'ModScript is ModLaunch’s modding language. One command per line, plain words, errors shown as you type. The script builds into a real mod.',
  'cr.t1': '<b>Variables, loops and conditions</b> — let, for, if / else',
  'cr.t2': '<b>Stardew Valley</b> — a Content Patcher pack: prices, dialogue, letters, images',
  'cr.t3': '<b>BepInEx games</b> — a Thunderstore package with dependencies and settings',
  'cr.t4': '<b>29 examples</b> and one-click publishing to Hub',
  'cr.try': 'Try it in the browser',
  'cr.ref': 'Language reference',
  'cr.out': '✓ 6 changes',
  'safe.eyebrow': 'Safety',
  'safe.title': 'Installs only what you chose',
  'safe.sub': 'Mods come from their authors’ sites, and every file is checked before it is installed.',
  'safe.1t': 'Originals from authors', 'safe.1p': 'Thunderstore, Nexus Mods, Modrinth and ModLinks — the file comes from where its author published it.',
  'safe.2t': 'Every file checked', 'safe.2p': 'The checksum is verified on download. If it doesn’t match, the install is cancelled.',
  'safe.3t': 'Honest updates', 'safe.3p': 'ModLaunch updates itself over https only and installs an update after verifying its checksum.',
  'safe.4t': 'Code in the open', 'safe.4p': 'The source is on GitHub, and the installer is built there automatically and passes a self-check.',
  'vs.eyebrow': 'Comparison',
  'vs.title': 'Why ModLaunch',
  'vs.r1': 'Thunderstore mods in one click',
  'vs.r2': 'Nexus Mods mods',
  'vs.r11': 'Minecraft: builds, Fabric, Forge and Modrinth',
  'vs.r3': 'Several catalogs for one game',
  'vs.r4': 'Own ad-free mod site',
  'vs.r5': 'Own mod language and publishing from the app',
  'vs.r6': 'Friends and in-game overlay',
  'vs.r7': 'ReShade and DXVK in one click',
  'vs.r8': 'Profiles, version rollback, file conflicts',
  'vs.r9': 'Free, no subscriptions',
  'vs.r10': 'Big Picture and gamepad PC control',
  'vs.part': 'partly',
  'sc.eyebrow': 'Screenshots',
  'sc.title': 'See it in action',
  'sc.home': 'Home', 'sc.library': 'Library', 'sc.bp': 'Big Picture', 'sc.modtabs': 'Mod tabs', 'sc.center': 'Mods center',
  'sc.hub': 'Hub', 'sc.editor': 'Editor', 'sc.versions': 'Mod page', 'sc.catalog': 'Catalog', 'sc.examples': 'Examples',
  'sc.add': 'Any game', 'sc.light': 'Light theme', 'sc.overlay': 'Overlay',
  'cap.home': 'Your games as covers, “Continue playing” and popular mods on one screen.',
  'cap.library': 'A Steam-style library: covers, collections, sorting and hidden games.',
  'cap.bigpicture': 'Big Picture: fullscreen, gamepad, mods and power menu from the couch.',
  'cap.mod-stats': 'Nexus-style mod tabs: files, changelog, requirements, videos, reviews and stats.',
  'cap.mods-center': 'Mods center: updates, tracked, favorites, history, recently viewed and hidden.',
  'cap.hub': 'ModLaunch Hub: trending, new and loved mods, tags and filters.',
  'cap.editor': 'The ModScript editor: highlighting, hints and a live “what you get” panel.',
  'cap.mod': 'The mod page: big picture, stats tiles, install and open on the source site.',
  'cap.catalog': 'Tens of thousands of mods with sections, search and sorting.',
  'cap.examples': '29 examples: modpacks for every game, Stardew tricks and language samples.',
  'cap.add-game': 'The “+” button finds every game on your PC and detects its engine.',
  'cap.home-light': 'The light theme — plus OLED black, eight accents and interface scale.',
  'cap.overlay': 'Ctrl+Shift+M in game: session time, friends, notes and a save backup.',
  'news.eyebrow': 'What’s new',
  'news.title': 'Latest versions',
  'news.all': 'All versions',
  'news.90': '“Showcase” design', 'news.90a': 'Big game art and “Continue playing” on Home', 'news.90b': 'Game covers on the side rail', 'news.90c': 'Full-width game art on the game page', 'news.90d': 'Links from mods can’t launch programs',
  'news.85': 'Minecraft', 'news.85a': 'Fabric, Quilt, Forge and NeoForge install themselves', 'news.85b': 'Builds with their own mods and worlds', 'news.85c': 'A Modrinth catalog that fits your version and loader', 'news.85d': 'Modpacks, .mrpack and one-click updates',
  'news.70': 'ModLaunch 3 design', 'news.70a': 'Breadcrumbs, info panel and game-colored glow', 'news.70b': '“ModLaunch pick” carousel and top mods', 'news.70c': 'Grid catalog, “Hot” badges, hide installed', 'news.70d': 'Steam-style collections and sorting',
  'news.60': 'Big Picture', 'news.60a': 'Big Picture mode and PC control with a gamepad', 'news.60b': 'A Steam-style library with covers', 'news.60c': 'Mod tabs and a Mods center, like Nexus', 'news.60d': 'Logo is Home, Creator Hub moved down',
  'faq.title': 'FAQ',
  'faq.sub': 'Didn’t find an answer? Ask us in GitHub Issues — we reply right there.',
  'faq.ask': 'Ask a question',
  'faq.1q': 'Is it free?', 'faq.1a': 'Yes, completely. No subscriptions, ads or paid features.',
  'faq.2q': 'Windows says “unknown publisher”. Is it safe?', 'faq.2a': 'Windows says that about any app without a paid code-signing certificate. Click “More info” → “Run anyway”. The source code is open on GitHub, and the installer is built there automatically and passes a self-check before every release.',
  'faq.3q': 'Where do the mods come from?', 'faq.3a': 'From the sites where authors publish them: Thunderstore, Nexus Mods, Modrinth and ModLinks — ModLaunch downloads the original. And from ModLaunch Hub, where authors publish right from the app.',
  'faq.8q': 'How do I play Minecraft with mods?', 'faq.8a': 'Open Minecraft in ModLaunch and create a build: pick a version and a loader — Fabric, Quilt, Forge or NeoForge. Mods from the Modrinth catalog go into the build with one button, and the game starts through the official Minecraft launcher.',
  'faq.4q': 'Are Hub mods safe?', 'faq.4a': 'Every file is checked by SHA-256 on download, the mod page links a VirusTotal report for the hash, and bad mods can be reported. ModScript mods contain no program code at all — only data and settings.',
  'faq.5q': 'Do I need an account?', 'faq.5a': 'No. A ModLaunch account is only needed to publish to Hub, comment, add friends and write reviews; everything else works without it.',
  'faq.6q': 'My game isn’t on the list', 'faq.6a': 'Press “+” in the app: ModLaunch finds the game on disk, detects the engine and picks a mod catalog. Or tell us in GitHub Issues which game to build in next.',
  'faq.7q': 'What is ModScript?', 'faq.7a': 'ModLaunch’s simple modding language: one command per line. A script becomes a real mod — a Content Patcher pack for Stardew Valley or a Thunderstore package for BepInEx games. You can try it right on this site.',
  'final.title': 'Ready to try it?',
  'final.p': 'One file, about 30 MB. Your games are found automatically — all that’s left is picking mods.',
  'final.zip': 'Portable version (zip)',
});
Object.assign(ru, {
  'games.plus': 'Любая игра', 'games.plus.p': 'Кнопка «+»: Steam, Epic, GOG или любой exe', 'games.new': 'новая', 'games.mods': '{n} модов',
  'hub.empty': 'Станьте первым автором в Hub',
  'hub.empty.p': 'Опубликуйте готовый архив мода или соберите мод в мастерской ModLaunch — его сразу увидят все, в программе и на этом сайте.',
  'hub.empty.btn': 'Как опубликовать',
  'hub.soon': 'ModLaunch Hub открывается',
  'hub.soon.p': 'Галерею прямо сейчас включают. Моды, опубликованные в программе, появятся здесь.',
  'cap.library': 'Библиотека как в Steam: обложки, коллекции, сортировка и скрытые игры.',
  'cap.mod': 'Страница мода: большая картинка, плитки с цифрами, установка и переход на сайт мода.',
  'cap.bigpicture': 'Big Picture: весь экран, геймпад, моды и меню питания — с дивана.',
  'cap.mod-stats': 'Вкладки мода как на Nexus: файлы, изменения, требования, видео, отзывы и статистика.',
  'cap.mods-center': 'Центр модов: обновления, отслеживаемые, избранное, история, недавнее и скрытое.',
  'cap.hub': 'ModLaunch Hub: моды в тренде, новые и любимые, теги и фильтры.',
  'cap.editor': 'Редактор ModScript: подсветка, подсказки и панель «что получится».',
  'cap.catalog': 'Десятки тысяч модов с разделами, поиском и сортировкой.',
  'cap.examples': '29 примочек: сборки для каждой игры, трюки Stardew и примеры языка.',
  'cap.add-game': 'Кнопка «+» находит все игры на компьютере и узнаёт движок.',
  'cap.home-light': 'Светлая тема — а ещё чёрная OLED, восемь акцентов и масштаб интерфейса.',
  'cap.overlay': 'Ctrl+Shift+M в игре: время сеанса, друзья, заметки и копия сохранений.',
});

/* --- стена обложек на первом экране --- */
function renderWall() {
  const wall = document.getElementById('wall');
  if (!wall) return;
  const ids = GAMES.map((g) => g.steam);
  const cols = 4;
  let html = '';
  for (let c = 0; c < cols; c++) {
    // Каждая колонка — свой порядок игр; список повторён дважды, чтобы прокрутка шла без шва.
    const list = ids.slice(c * 4).concat(ids.slice(0, c * 4)).slice(0, 7);
    const imgs = list.map((id) => `<img src="img/art/${esc(String(id))}.webp" alt="" loading="lazy" decoding="async" />`).join('');
    html += `<div class="wall__col">${imgs}${imgs}</div>`;
  }
  wall.innerHTML = html;
}

/* --- игры --- */
function renderGames() {
  const grid = document.getElementById('gamesGrid');
  grid.innerHTML = GAMES.map((g) => `
    <article class="game reveal" style="--g:${/^#[0-9a-f]{6}$/i.test(g.color || '') ? g.color : '#6e4bff'}">
      <img src="img/art/${esc(String(g.steam))}.webp" alt="" loading="lazy" data-fallback="${esc(steamArt(g.steam))}" />
      <span class="count">${esc(t('games.mods', { n: compact(g.mods) }))}</span>
      ${g.fresh ? `<span class="badge">${esc(t('games.new'))}</span>` : ''}
      <div><h3>${esc(g.name)}</h3><p>${esc(g.loader)} · ${esc(g.src)}</p></div>
    </article>`).join('');
  // Своей обложки нет — берём из Steam; не вышло и там — убираем картинку. Без встроенных обработчиков (CSP).
  grid.querySelectorAll('img[data-fallback]').forEach((img) => img.addEventListener('error', () => {
    if (img.dataset.tried) img.remove();
    else { img.dataset.tried = '1'; img.src = img.dataset.fallback; }
  }));
  reveal(grid);
}

/* --- Hub на главной --- */
let hubMods = null;
let hubState = 'loading';
async function loadHub() {
  try {
    hubMods = await Hub.all();
    hubState = 'ok';
  } catch (e) {
    hubState = e.code === 'DENIED' ? 'soon' : 'error';
    hubMods = [];
  }
  renderHub();
}
function renderHub() {
  const box = document.getElementById('hubTeaser');
  if (!box) return;
  if (hubState === 'loading') {
    box.innerHTML = `<div class="hub-grid">${'<div class="skeleton"></div>'.repeat(3)}</div>`;
    return;
  }
  if (!hubMods.length) {
    const soon = hubState !== 'ok';
    box.innerHTML = `<div class="empty reveal"><div class="empty__art">✦</div><div>
      <h3>${esc(t(soon ? 'hub.soon' : 'hub.empty'))}</h3><p>${esc(t(soon ? 'hub.soon.p' : 'hub.empty.p'))}</p>
      <a class="btn btn--primary" href="hub.html#publish">${esc(t('hub.empty.btn'))}</a></div></div>`;
  } else {
    box.innerHTML = `<div class="hub-grid">${Hub.sort(hubMods, 'trending').slice(0, 6).map(modCard).join('')}</div>`;
    box.querySelectorAll('[data-mod]').forEach((b) => b.addEventListener('click', () => (location.href = `hub.html#mod=${encodeURIComponent(b.dataset.mod)}`)));
  }
  reveal(box);
}

/* --- печатающийся ModScript --- */
const SAMPLE = `# Весенние семена вдвое дешевле
mod "Дешёвые семена"
version 1.0.0
game stardew-valley

let price = 40 / 2

for seed in 472 473 474 475 427 477 {
  edit "Data/Objects" "$seed" Price = $price
}
print "Новая цена: $price"`;
function typeCode() {
  const pre = document.getElementById('typing');
  if (!pre) return;
  const lines = (text) => text.split('\n').map((l) => `<span class="ln">${l || ' '}</span>`).join('');
  if (calm) { pre.innerHTML = lines(highlight(SAMPLE)); return; }
  let i = 0;
  const step = () => {
    i = Math.min(SAMPLE.length, i + (SAMPLE[i] === '\n' ? 1 : 2));
    pre.innerHTML = lines(highlight(SAMPLE.slice(0, i))).replace(/<\/span>$/, '<span class="caret"></span></span>');
    if (i < SAMPLE.length) setTimeout(step, 28);
    else setTimeout(() => { i = 0; step(); }, 6000);
  };
  const io = new IntersectionObserver((e) => { if (e[0].isIntersecting) { io.disconnect(); step(); } });
  io.observe(pre);
}

/* --- скриншоты --- */
const shot = document.getElementById('shot');
const cap = document.getElementById('shotCap');
const bar = document.getElementById('shotProgress');
const tabs = [...document.querySelectorAll('.tab')];
let current = 0;
let timer = null;
function show(index) {
  current = (index + tabs.length) % tabs.length;
  const name = tabs[current].dataset.shot;
  tabs.forEach((tab, i) => { tab.classList.toggle('is-on', i === current); tab.setAttribute('aria-selected', String(i === current)); });
  shot.classList.add('is-out');
  setTimeout(() => {
    shot.src = `img/${name}.webp`;
    shot.alt = tabs[current].textContent;
    cap.dataset.t = `cap.${name}`;
    cap.textContent = t(`cap.${name}`);
    shot.classList.remove('is-out');
  }, 220);
  bar.classList.remove('is-run');
  void bar.offsetWidth;
  if (!calm) bar.classList.add('is-run');
}
function autoplay() {
  clearInterval(timer);
  if (!calm) timer = setInterval(() => show(current + 1), 6000);
}
tabs.forEach((tab, i) => tab.addEventListener('click', () => { show(i); autoplay(); }));
// Картинки вкладок подгружаем, только когда до раздела дошли.
const screens = document.getElementById('screens');
if (screens) {
  const pre = new IntersectionObserver((e) => {
    if (!e[0].isIntersecting) return;
    pre.disconnect();
    tabs.forEach((tab) => { new Image().src = `img/${tab.dataset.shot}.webp`; });
  }, { rootMargin: '600px' });
  pre.observe(screens);
}

/* --- окно программы наклоняется за мышью --- */
const tilt = document.getElementById('tilt');
if (!calm && tilt && matchMedia('(pointer: fine)').matches) {
  const win = tilt.querySelector('.window');
  tilt.addEventListener('pointermove', (e) => {
    const b = tilt.getBoundingClientRect();
    win.style.setProperty('--ry', `${((e.clientX - b.left) / b.width - 0.5) * 6}deg`);
    win.style.setProperty('--rx', `${-((e.clientY - b.top) / b.height - 0.5) * 4}deg`);
  });
  tilt.addEventListener('pointerleave', () => { win.style.removeProperty('--ry'); win.style.removeProperty('--rx'); });
}

/* --- «Что нового» из выпусков GitHub (заметки к выпуску — build/release-notes.md) --- */
const newer = (a, b) => {
  const pa = String(a).split('.').map(Number);
  const pb = String(b).split('.').map(Number);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    if ((pa[i] || 0) !== (pb[i] || 0)) return (pa[i] || 0) > (pb[i] || 0);
  }
  return false;
};
releaseHooks.push((rel) => {
  // Карточки в разметке уже про 8.5: заменяем их, только если на GitHub выпуск новее.
  const shown = document.querySelector('#newsList .ver')?.textContent || '0';
  const latest = rel.all?.[0];
  if (!latest || lang === 'en' || !newer(String(latest.tag_name).replace(/^v/i, ''), shown)) return;
  const body = latest.body || '';
  const parts = [...body.matchAll(/^#{1,2} (?:Что нового в|What's new in|ModLaunch) ([\d.]+)\n([\s\S]*?)(?=\n#{1,2} |(?![\s\S]))/gm)].slice(0, 3);
  if (!parts.length) return;
  const date = new Date(latest.published_at).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' });
  document.getElementById('newsList').innerHTML = parts.map(([, ver, text], i) => {
    const items = [...text.matchAll(/^- \*\*(.+?)\*\*/gm)].map((m) => m[1].replace(/[.:]$/, '')).slice(0, 5);
    return `<article class="reveal"><span class="ver">${esc(ver)}</span>${i === 0 ? `<time>${esc(date)}</time>` : ''}
      <ul style="margin-top:14px">${items.map((x) => `<li>${esc(x)}</li>`).join('')}</ul></article>`;
  }).join('');
  reveal(document.getElementById('newsList'));
});

langHooks.push(() => { renderGames(); if (hubMods) renderHub(); });
renderWall();
renderGames();
renderHub();
loadHub();
typeCode();
autoplay();
if (!calm && bar) bar.classList.add('is-run');
