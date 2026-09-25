'use strict';

/* Главная страница: игры, живые цифры, свежие моды Hub, печатающийся ModScript,
   скриншоты и «Что нового» прямо из выпусков на GitHub. */

Object.assign(EN, {
  'hero.chip': 'Out now:',
  'hero.chip2': 'the beloved ModLaunch 3 design and Steam collections',
  'hero.title1': 'Mods in one click.',
  'hero.title2': 'And your own mod hub.',
  'hero.lead': 'ModLaunch finds your games, installs the mod loader, pulls in dependencies and keeps everything updated. A library with real covers, Big Picture mode for gamepads, the ad-free ModLaunch Hub and its own modding language, ModScript.',
  'hero.hub': 'Open Hub',
  'hero.games': 'games',
  'hero.mods': 'mods',
  'hero.free': 'forever',
  'float.a1': 'Nautilus installed',
  'float.a2': 'together with BepInEx',
  'float.b1': 'Update ready',
  'float.b2': '3 mods · 1 click',
  'float.c1': 'Published to Hub',
  'float.c2': 'everyone can see your mod',
  'band.games': 'games',
  'band.mods': 'mods in catalogs',
  'band.examples': 'ready-made ModScript examples',
  'band.hub': 'mods on ModLaunch Hub',
  'games.eyebrow': 'Supported games',
  'games.title': '17 games — and any other',
  'games.sub': 'The loader installs itself, mods come from every catalog at once. Game missing? The “+” button finds it on disk and picks a mod catalog.',
  'games.plus': 'Any game',
  'games.plus.p': 'The “+” button: Steam, Epic, GOG or any exe',
  'games.new': 'new',
  'games.mods': '{n} mods',
  'feat.eyebrow': 'Features',
  'feat.title': 'Everything for mods. In one app.',
  'feat.sub': 'No copying files by hand, no unpacking archives, no guessing why the game won’t start.',
  'f.install': 'One-click install',
  'f.install.p': 'Hit “Install” and ModLaunch sets up BepInEx, SMAPI or the Modding API, downloads the mod with every dependency and puts it where it belongs. Mods from Thunderstore, Nexus, ModLinks and ModLaunch Hub — in one catalog.',
  'f.hub': 'ModLaunch Hub',
  'f.hub.p': 'Our own ad-free mod site with no paid “fast download”: versions with changelogs, likes, comments, tracking, SHA-256 and VirusTotal file checks.',
  'f.ms': 'Your own mod language',
  'f.ms.p': 'ModScript: variables, loops, conditions. A script builds into a Content Patcher or Thunderstore package.',
  'f.plus': 'Any game',
  'f.plus.p': 'The “+” button finds Steam, Epic and GOG games, detects the engine and installs BepInEx into Unity games.',
  'f.versions': 'Versions and rollback',
  'f.versions.p': 'Every version of a mod with its changelog. Install any — or roll back when a new one breaks the game.',
  'f.track': 'Tracking',
  'f.track.p': '“Track” on any mod — the bell tells you about new versions. Plus “More from the author” and hiding mods.',
  'f.friends': 'Friends and overlay',
  'f.friends.p': 'See what your friends are playing. In game, Ctrl+Shift+M: timer, notes, save backups.',
  'f.gfx': 'ReShade and DXVK',
  'f.gfx.p': 'Shaders and presets in one click. DXVK removes stutter in older games.',
  'f.profiles': 'Profiles and saves',
  'f.profiles.p': 'Mod sets for different playthroughs, save backups, file conflicts and a download archive.',
  'f.look': 'Your look',
  'f.look.p': 'Dark, OLED black and light themes, accent color, scale, what to show — and a quick jump with Ctrl+P.',
  'f.update': 'Updates itself',
  'f.update.p': 'ModLaunch downloads its own updates, verifies the checksum and installs them with one button.',
  'hub.eyebrow': 'ModLaunch Hub',
  'hub.title': 'Community mods — no middlemen',
  'hub.sub': 'Fresh mods from ModLaunch Hub. Each one installs in the app with one button, and here you can view and download it.',
  'hub.all': 'All Hub mods',
  'hub.empty': 'Hub has 0 mods yet — be the first author',
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
  'vs.eyebrow': 'Comparison',
  'vs.title': 'Why ModLaunch',
  'vs.r1': 'Thunderstore mods in one click',
  'vs.r2': 'Nexus Mods mods',
  'vs.r3': 'Several catalogs for one game',
  'vs.r4': 'Own ad-free mod site',
  'vs.r5': 'Own mod language and publishing from the app',
  'vs.r6': 'Friends and in-game overlay',
  'vs.r7': 'ReShade and DXVK in one click',
  'vs.r8': 'Profiles, version rollback, file conflicts',
  'vs.r9': 'Free, no subscriptions',
  'vs.part': 'partly',
  'sc.eyebrow': 'Screenshots',
  'sc.title': 'See it in action',
  'sc.home': 'Home',
  'sc.hub': 'Hub',
  'sc.editor': 'Editor',
  'sc.versions': 'Mod page',
  'sc.catalog': 'Catalog',
  'sc.examples': 'Examples',
  'sc.add': 'Any game',
  'sc.light': 'Light theme',
  'sc.overlay': 'Overlay',
  'cap.home': 'Your games as covers, “Continue playing” and popular mods on one screen.',
  'cap.hub': 'ModLaunch Hub: trending, new and loved mods, tags and filters.',
  'cap.editor': 'The ModScript editor: highlighting, hints and a live “what you get” panel.',
  'cap.versions': 'Files and versions with changelogs — install any version or roll back.',
  'cap.catalog': 'Tens of thousands of mods with sections, search and sorting.',
  'cap.examples': '29 examples: modpacks for every game, Stardew tricks and language samples.',
  'cap.add-game': 'The “+” button finds every game on your PC and detects its engine.',
  'cap.home-light': 'The light theme — plus OLED black, eight accents and interface scale.',
  'cap.overlay': 'Ctrl+Shift+M in game: session time, friends, notes and a save backup.',
  'news.eyebrow': 'What’s new',
  'news.title': 'Latest versions',
  'news.all': 'All versions and the full changelog',
  'news.70': 'ModLaunch 3 design', 'news.70a': 'Breadcrumbs, info panel and game-colored glow', 'news.70b': '“ModLaunch pick” carousel and top mods', 'news.70c': 'Grid catalog, “Hot” badges, hide installed', 'news.70d': 'Steam-style collections and sorting',
  'f.v7': 'The ModLaunch 3 design — only better', 'f.v7.p': 'Breadcrumbs in the header, an info panel for the game and mod, a glow in the game’s color, a big “ModLaunch pick” carousel, top mods and a list or grid catalog like Modrinth.',
  'f.coll': 'Collections like Steam', 'f.coll.p': 'Favorites and your own collections, sorting by hours played, hiding games and a right-click menu on covers.',
  'cap.mod': 'The mod page: big picture, stats tiles, install and open on the source site.',
  'news.60': 'Big Picture', 'news.60a': 'Big Picture mode and PC control with a gamepad', 'news.60b': 'A Steam-style library with covers', 'news.60c': 'Mod tabs and a Mods center, like Nexus', 'news.60d': 'Logo is Home, Creator Hub moved down',
  'f.bp': 'Big Picture and gamepad', 'f.bp.p': 'Press F11 and ModLaunch goes fullscreen like Steam on a TV: big covers, game art in the background, launch games, toggle mods and power off the PC from the couch. The View button turns the gamepad into a mouse and keyboard: stick moves the cursor, A clicks, X opens the on-screen keyboard.',
  'f.lib': 'A Steam-style library', 'f.lib.p': 'Real covers for all 17 games — even offline. Your games on top, the rest below. Every game gets a header with its art and logo.',
  'f.tabs': 'Everything Nexus has', 'f.tabs.p': 'Mod tabs: files, changelog, requirements, images, videos, reviews and stats. Random and name sorting, time period and an 18+ switch.',
  'f.center': 'Mods center', 'f.center.p': 'Updates, tracked, favorites, recently viewed, download history and hidden — on one page.',
  'vs.r10': 'Big Picture and gamepad PC control',
  'sc.library': 'Library', 'sc.bp': 'Big Picture', 'sc.modtabs': 'Mod tabs', 'sc.center': 'Mods center',
  'cap.library': 'A Steam-style library: covers, collections, sorting and hidden games.',
  'cap.bigpicture': 'Big Picture: fullscreen, gamepad, mods and power menu from the couch.',
  'cap.mod-stats': 'Nexus-style mod tabs: files, changelog, requirements, videos, reviews and stats.',
  'cap.mods-center': 'Mods center: updates, tracked, favorites, history, recently viewed and hidden.',
  'news.55': 'ModLaunch Hub', 'news.55a': 'Our own mod site in the app', 'news.55b': 'ModScript editor with highlighting', 'news.55c': 'Versions, tracking, author mods', 'news.55d': 'Silksong, GTFO and Outward',
  'news.50': 'Creator Hub', 'news.50a': 'The ModScript mod language', 'news.50b': 'The “+” button for any game', 'news.50c': 'Six new games', 'news.50d': 'Themes and flexible settings',
  'news.40': 'New engine', 'news.40a': 'C# and Avalonia instead of Electron', 'news.40b': 'A 4× smaller file', 'news.40c': 'Several catalogs per game', 'news.40d': 'Vortex and Modrinth App tricks',
  'steps.title': 'Up and running in a minute',
  'steps.1t': 'Download', 'steps.1p': 'One file, about 25 MB.',
  'steps.2t': 'Install', 'steps.2p': 'Click “Install” and ModLaunch is on your desktop.',
  'steps.3t': 'Play with mods', 'steps.3p': 'Your games are found automatically. Pick a mod and hit “Play”.',
  'faq.title': 'FAQ',
  'faq.1q': 'Is it free?', 'faq.1a': 'Yes, completely. No subscriptions, ads or paid features.',
  'faq.2q': 'Windows says “unknown publisher”. Is it safe?', 'faq.2a': 'Windows says that about any app without a paid code-signing certificate. Click “More info” → “Run anyway”. The source code is open on GitHub, and the installer is built there automatically and passes a self-check before every release.',
  'faq.3q': 'Where do the mods come from?', 'faq.3a': 'From the sites where authors publish them: Thunderstore, Nexus Mods and ModLinks — ModLaunch downloads the original. And from ModLaunch Hub, where authors publish right from the app.',
  'faq.4q': 'Are Hub mods safe?', 'faq.4a': 'Every file is checked by SHA-256 on download, the mod page links a VirusTotal report for the hash, and bad mods can be reported. ModScript mods contain no program code at all — only data and settings.',
  'faq.5q': 'Do I need an account?', 'faq.5a': 'No. A ModLaunch account is only needed to publish to Hub, comment, add friends and write reviews; everything else works without it.',
  'faq.6q': 'My game isn’t on the list', 'faq.6a': 'Press “+” in the app: ModLaunch finds the game on disk, detects the engine and picks a mod catalog. Or tell us in GitHub Issues which game to build in next.',
  'faq.7q': 'What is ModScript?', 'faq.7a': 'ModLaunch’s simple modding language: one command per line. A script becomes a real mod — a Content Patcher pack for Stardew Valley or a Thunderstore package for BepInEx games. You can try it right on this site.',
  'final.title': 'Ready to try it?',
  'final.zip': 'Portable version (zip)',
});
Object.assign(ru, {
  'games.plus': 'Любая игра', 'games.plus.p': 'Кнопка «+»: Steam, Epic, GOG или любой exe', 'games.new': 'новая', 'games.mods': '{n} модов',
  'hub.empty': 'В Hub пока 0 модов — станьте первым автором',
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
  'cap.versions': 'Файлы и версии со списком изменений — поставить любую или откатиться.',
  'cap.catalog': 'Десятки тысяч модов с разделами, поиском и сортировкой.',
  'cap.examples': '29 примочек: сборки для каждой игры, трюки Stardew и примеры языка.',
  'cap.add-game': 'Кнопка «+» находит все игры на компьютере и узнаёт движок.',
  'cap.home-light': 'Светлая тема — а ещё чёрная OLED, восемь акцентов и масштаб интерфейса.',
  'cap.overlay': 'Ctrl+Shift+M в игре: время сеанса, друзья, заметки и копия сохранений.',
});

/* --- игры --- */
function renderGames() {
  const grid = document.getElementById('gamesGrid');
  grid.innerHTML = GAMES.map((g) => `
    <article class="game reveal" style="--g:${g.color || '#7c5cff'}">
      <img src="img/art/${g.steam}.webp" alt="" loading="lazy" onerror="if(this.dataset.f){this.remove()}else{this.dataset.f=1;this.src='${steamArt(g.steam)}'}" />
      <span class="count">${esc(t('games.mods', { n: compact(g.mods) }))}</span>
      ${g.fresh ? `<span class="badge">${esc(t('games.new'))}</span>` : ''}
      <div><h3>${esc(g.name)}</h3><p>${esc(g.loader)} · ${esc(g.src)}</p></div>
    </article>`).join('') + `
    <article class="game game--plus reveal">
      <div><span class="plus">+</span><h3>${esc(t('games.plus'))}</h3><p>${esc(t('games.plus.p'))}</p></div>
    </article>`;
  reveal(grid);
}

/* --- цифры --- */
document.querySelector('.js-total').dataset.count = String(TOTAL_MODS);

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
  const counter = document.querySelector('.js-hubcount');
  counter.dataset.count = String(hubMods.length);
  countUp(counter, hubMods.length);
  renderHub();
}
function renderHub() {
  const box = document.getElementById('hubTeaser');
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
tabs.forEach((tab) => { new Image().src = `img/${tab.dataset.shot}.webp`; });

/* --- окно на первом экране наклоняется за мышью, карточки подсвечиваются --- */
const tilt = document.getElementById('tilt');
if (!calm && tilt) {
  const win = tilt.querySelector('.window');
  tilt.addEventListener('pointermove', (e) => {
    const b = tilt.getBoundingClientRect();
    win.style.setProperty('--ry', `${((e.clientX - b.left) / b.width - 0.5) * 12}deg`);
    win.style.setProperty('--rx', `${-((e.clientY - b.top) / b.height - 0.5) * 8}deg`);
  });
  tilt.addEventListener('pointerleave', () => { win.style.removeProperty('--ry'); win.style.removeProperty('--rx'); });
}
document.querySelectorAll('.fcard').forEach((card) => card.addEventListener('pointermove', (e) => {
  const b = card.getBoundingClientRect();
  card.style.setProperty('--mx', `${e.clientX - b.left}px`);
  card.style.setProperty('--my', `${e.clientY - b.top}px`);
}));

/* --- «Что нового» из выпусков GitHub (заметки к выпуску — build/release-notes.md) --- */
releaseHooks.push((rel) => {
  const body = rel.all[0]?.body || '';
  const parts = [...body.matchAll(/## (?:Что нового в|What's new in) ([\d.]+)\n([\s\S]*?)(?=\n## |$)/g)].slice(0, 3);
  if (!parts.length || lang === 'en') return;
  const date = new Date(rel.all[0].published_at).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' });
  document.getElementById('newsList').innerHTML = parts.map(([, ver, text], i) => {
    const items = [...text.matchAll(/^- \*\*(.+?)\*\*/gm)].map((m) => m[1].replace(/[.:]$/, '')).slice(0, 5);
    return `<article class="reveal"><span class="ver">${esc(ver)}</span>${i === 0 ? `<time>${esc(date)}</time>` : ''}
      <ul style="margin-top:14px">${items.map((x) => `<li>${esc(x)}</li>`).join('')}</ul></article>`;
  }).join('');
  reveal(document.getElementById('newsList'));
});

langHooks.push(() => { renderGames(); if (hubMods) renderHub(); });
renderGames();
renderHub();
loadHub();
typeCode();
autoplay();
if (!calm) bar.classList.add('is-run');
