'use strict';

/* Главная страница (сайт 21.0, «Платформа»): сцена игры с полкой обложек, разделы программы
   настоящими скриншотами (русскими и английскими), игры, блоки ModScript X, моды Hub
   и «Что нового» прямо из выпусков на GitHub. */

Object.assign(EN, {
  'nav.platform': 'Platform', 'nav.library': 'Library', 'nav.mods': 'Mods', 'nav.community': 'Community',

  'hero.chip': '— blocks in ModScript X',
  'hero.title': 'The platform for games with mods',
  'hero.lead': 'Every game on your PC in one library — from Steam, Epic, GOG or any folder. Mods install in one click, friends and rewards live in your profile, and you can build your own mods in the ModScript X studio and put them on the Market.',
  'hero.tour': 'See the app',
  'hero.note': 'For Windows 10 and 11. 141,711 games in the catalog, over 100,000 mods, the interface in 8 languages.',

  'pl.title': 'Not a utility — a whole platform',
  'pl.sub': 'Game library, mods, friends, profile, a studio for authors and a Market — in one window. Pick a section on the left: these are real ModLaunch 21.0 screens.',
  'tab.home': 'Home', 'tab.library': 'Library', 'tab.game': 'Game page', 'tab.catalog': 'Mod catalog', 'tab.ai': 'AI builds',
  'tab.friends': 'Community', 'tab.profile': 'Profile & rewards', 'tab.market': 'Market', 'tab.look': 'Appearance',
  'cap.home': 'Your last game full-screen, a shelf of covers and widgets: level, play time, autopilot, friends. Widgets can be added, moved and resized, like on a phone.',
  'cap.library': 'Every game on your PC — from Steam, Epic, GOG and any folder. Covers, collections, favourites, play time and search across 141,711 games.',
  'cap.game-installed': 'Each game has its own page: Play, installed mods, catalog, mod settings, profiles, saves and the log.',
  'cap.game': 'Thunderstore, Nexus Mods and ModLaunch Hub in one list. Sections, Essentials, filters. Install sets up the loader and everything the mod needs.',
  'cap.ai': 'Describe how you want to play — AI builds a mod set from the real catalog, explains every pick and installs it in one click.',
  'cap.friends': 'One friends list for every game: who’s online and what they’re playing. Friends’ servers and your own Minecraft server right on this PC.',
  'cap.rewards': 'Your level grows as you play. Every level from 2 to 50 has a reward: frames, banners, titles and badges. Each game has its own achievements.',
  'cap.blocks': 'ModScript X is a mod development studio: blocks like in Scratch, code, a debugger and F6 to put the mod into the game.',
  'cap.market': 'A storefront of authors’ work: scripts, code pieces, models and builds. Purchases are injected into the game at launch, and authors see their income in their account.',
  'cap.bigpicture': 'F11 and ModLaunch goes full-screen, like on a TV. Gamepad control, launching and mods from the couch.',
  'cap.look': 'Themes and accents, fonts, size, motion and Home widgets. Ready-made styles in one click and your own style as a file.',

  'lib.title': 'One library instead of five launchers',
  'lib.sub': 'ModLaunch finds the games on your PC by itself and adds new ones as soon as they’re installed. Launching, play time, collections and covers — for every game at once, wherever it came from.',
  'lib.steam': 'your whole account library', 'lib.mc': 'through the official launcher', 'lib.any': 'Any folder', 'lib.any2': 'even a copy without a launcher',
  'lib.n1v': '141,711', 'lib.n1': 'games ModLaunch knows', 'lib.n2v': '≈3,000', 'lib.n2': 'games with a mod catalog', 'lib.n3': 'games with full support',
  'games.new': 'new', 'games.mods': '{n} mods',

  'mods.title': 'Mods in one click — without breaking the game',
  'mods.sub': 'No more “download the archive, unpack it, rename the dll”. ModLaunch installs, updates and fixes mods by itself.',
  'mods.1t': 'One button', 'mods.1p': 'The loader — BepInEx, SMAPI, Fabric, Forge — and every dependency come with the mod.',
  'mods.2t': 'Every catalog at once', 'mods.2p': 'Thunderstore, Nexus Mods, Modrinth, ModLinks and ModLaunch Hub. Nexus works even without Premium.',
  'mods.3t': 'AI builds', 'mods.3p': 'Type “beautiful graphics, no cheats” — AI builds a set from the real catalog and explains each mod.',
  'mods.4t': 'Autopilot', 'mods.4p': 'A build score out of 100, a check before launch and one-click repair — with a snapshot to roll back.',
  'mods.5t': 'Updates and rollback', 'mods.5p': 'Update all, any previous version, mod profiles and a save backup before every launch.',
  'mods.6t': 'ReShade and DXVK', 'mods.6p': 'Shaders with presets and DXVK for old games install and uninstall in one click.',
  'mods.x1': 'File conflicts', 'mods.x2': 'Ready-made mod settings', 'mods.x3': 'Nexus collections and Thunderstore packs', 'mods.x4': 'Mods from zip, 7z and rar',
  'mods.x5': 'Clean boot', 'mods.x6': 'Tracking and favourites', 'mods.x7': 'Crash reports',

  'com.title': 'Friends, servers and rewards',
  'com.sub': 'A ModLaunch account ties it all together: friends, servers, profile and rewards. On another PC your profile follows you.',
  'com.1': '<b>Friends across all games</b> — who’s online, what they play, Join in one click',
  'com.2': '<b>Friends’ and public servers</b> — Join installs the right build by itself',
  'com.3': '<b>Your own Minecraft server</b> right on this PC',
  'com.4': '<b>Profile and levels</b> — a reward on every level up to 50, achievements for each game',
  'com.5': '<b>Author groups</b> — a shared tag, colour and team projects',
  'com.6': '<b>Mod reviews and ratings</b>, shared by all players',
  'com.7': '<b>In-game overlay</b> — <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>M</kbd>: friends, session time, notes',

  'cr.title': 'Your own mod — no coding needed',
  'cr.sub': 'ModScript X is a mod development studio inside ModLaunch. You snap a mod together from blocks, like in Scratch, and ModLaunch translates it into the game engine’s code and puts it into the game.',
  'cr.1': '<b>Blocks and code are one file.</b> A Blocks · Code switch, and mistakes show right on the block',
  'cr.2': '<b>A bridge to 20 engines</b>: Unity, Unreal, Godot, RE Engine, Ren’Py, RPG Maker and more',
  'cr.3': '<b><kbd>F5</kbd> — sandbox</b> without the game, <b><kbd>F6</kbd> — the mod in the game</b>',
  'cr.4': '<b>Like big IDEs</b>: smart hints, a debugger that steps back, search, version history',
  'cr.5': '<b>Code pieces</b> from the Market plug in by name, eight of them free',
  'cr.go': 'Download and build a mod', 'cr.try': 'ModScript in the browser',
  'cr.blocks': 'Blocks', 'cr.code': 'Code', 'cr.ok': 'No problems', 'cr.checked': 'checked in 1 ms',
  'cr.m1t': 'Your first mod in a minute', 'cr.m1p': 'Create → Build a mod from blocks: four ready ideas for your game and one button.',
  'cr.m2t': 'Authors’ Market', 'cr.m2p': 'Scripts, code pieces, models and builds. Purchases are injected at launch, the source stays closed.',
  'cr.m3t': 'Earnings in your account', 'cr.m3p': 'Sales, downloads, likes and 30-day income — in Account → Earnings.',

  'mc.title': 'Minecraft on par with Modrinth App',
  'mc.p': 'Every version from 1.12 to the latest snapshots. The right loader installs itself, and you play through the official launcher — your Microsoft account stays yours.',
  'mc.1': 'Builds with their own mods, resource packs, shaders and worlds',
  'mc.2': 'A Modrinth catalog filtered to the build, modpacks and .mrpack in one click',
  'mc.3': 'Servers with player count and ping, your own server on this PC',
  'mc.4': 'Builds from Prism, Modrinth App, CurseForge, ATLauncher and MultiMC — no copying',

  'hub.title': 'Community mods — straight from authors',
  'hub.sub': 'Authors publish mods to ModLaunch Hub right from the app. No ads and no paid “fast download”: browse and download here, and in ModLaunch the mod installs in one click.',
  'hub.all': 'All Hub mods',
  'hub.empty': 'Be the first author on Hub',
  'hub.empty.p': 'Publish a ready mod archive or build one in Creator Hub — everyone sees it right away, in the app and on this site.',
  'hub.empty.btn': 'How to publish',
  'hub.soon': 'ModLaunch Hub is opening',
  'hub.soon.p': 'The gallery is being switched on right now. Mods published in the app will appear here.',

  'vs.title': 'ModLaunch, Steam and mod managers',
  'vs.sub': 'Everything people love about game platforms — plus what they lack for mods.',
  'vs.r1': 'One library for all games: Steam, Epic, GOG and your own folders', 'vs.shortcut': 'shortcuts',
  'vs.r2': 'Friends, profile, levels and achievements',
  'vs.r3': 'In-game overlay and Big Picture with a gamepad',
  'vs.r4': 'Thunderstore and Nexus Mods in one click',
  'vs.r5': 'Minecraft: builds, Fabric, Forge and Modrinth',
  'vs.r6': 'Mod profiles, version rollback, file conflicts', 'vs.partly': 'partly',
  'vs.r7': 'A mod platform for authors', 'vs.workshop': 'Workshop',
  'vs.r8': 'A mod development studio with blocks',
  'vs.r9': 'ReShade and DXVK in one click',
  'vs.r10': 'Free',
  'vs.note': 'Steam is also a store: that’s where games are bought. ModLaunch finds the games you own and adds mods, friends across all games and a studio for authors. The Steam Workshop exists only in some games.',

  'safe.title': 'Installs only what you chose',
  'safe.sub': 'Mods come from their authors’ sites, and every file and every script is checked.',
  'safe.1t': 'Originals from authors', 'safe.1p': 'Thunderstore, Nexus Mods, Modrinth and ModLinks — the file comes from where the author published it.',
  'safe.2t': 'Every file is checked', 'safe.2p': 'The checksum is verified on download. If it doesn’t match, the install is cancelled.',
  'safe.3t': 'Script auto-moderation', 'safe.3p': 'Market scripts are checked at publishing, at purchase and before every launch. If the game crashes twice with a script, it turns itself off.',
  'safe.4t': 'Honest updates', 'safe.4p': 'ModLaunch updates only over https and installs an update after verifying its checksum. The code is open on GitHub.',

  'news.title': 'What’s new', 'news.all': 'All versions',
  'news.21': 'Creator Hub for everyone', 'news.21a': 'Blocks in ModScript X — build a mod with the mouse, like in Scratch', 'news.21b': 'Code pieces from the Market by name, eight of them free', 'news.21c': 'Your first mod in a minute: four ideas, one button',
  'news.20': 'A simpler Creator Hub', 'news.20a': 'Only ModScript X and the Market on the left', 'news.20b': 'The Market is for buying: storefront, library, wishlist', 'news.20c': 'Account → Earnings: income, sales and a chart',
  'news.19': 'A real development studio', 'news.19a': 'Context hints, go to definition, rename everywhere', 'news.19b': 'A time-travel debugger', 'news.19c': 'Errors before you run, fixed in one click',

  'faq.title': 'FAQ',
  'faq.sub': 'Didn’t find an answer? Open an issue on GitHub — we reply there.',
  'faq.ask': 'Ask a question',
  'faq.1q': 'Is it free?', 'faq.1a': 'Yes. ModLaunch is free, no subscriptions. Only authors’ work on the Market can be paid — buying it is optional. There’s a narrow ad column on the right in the app, and you can close it.',
  'faq.2q': 'Do I still need Steam?', 'faq.2a': 'Games are still bought in stores — Steam, Epic Games, GOG. ModLaunch finds them on your PC, launches them and adds what stores don’t have: one-click mods, friends across all games, a profile with rewards and a mod studio. If you don’t have a game yet, ModLaunch installs it through Steam right from the game page.',
  'faq.3q': 'Windows says “unknown publisher”. Is it safe?', 'faq.3a': 'Windows says that about any program without a paid code-signing certificate. Click “More info” → “Run anyway”. The source code is open on GitHub, and the installer is built there automatically and passes a self-check before every release.',
  'faq.4q': 'Where do mods come from?', 'faq.4a': 'From the sites where authors publish them: Thunderstore, Nexus Mods, Modrinth and ModLinks — ModLaunch downloads the original. And from ModLaunch Hub, where authors publish mods right from the app.',
  'faq.5q': 'Do I need an account?', 'faq.5a': 'No. Games, mods and everything on this PC work without one. A ModLaunch account is for friends and servers, the cloud profile, reviews, the Market and publishing mods.',
  'faq.6q': 'How do I play Minecraft with mods?', 'faq.6a': 'Open Minecraft in ModLaunch and create a build: pick a version and a loader — Fabric, Quilt, Forge or NeoForge. Mods from the Modrinth catalog install into the build in one click, and the game starts through the official Minecraft launcher.',
  'faq.7q': 'My game isn’t listed', 'faq.7a': 'It most likely is: ModLaunch knows 141,711 games — the whole Steam library and almost 3,000 games with mods. If not, press “+” in the app: ModLaunch finds the game on disk, detects its engine and picks a mod catalog.',
  'faq.8q': 'What is ModScript X?', 'faq.8a': 'The mod development studio in Creator Hub. You build a mod from blocks or write it in ModScript X, and ModLaunch translates it into the game engine’s language — C#, Lua, GDScript, Python or JavaScript — and puts it into the game with F6. Single-player only: the injector doesn’t work with anti-cheat.',
  'faq.9q': 'Are Market scripts safe?', 'faq.9a': 'Every script is checked at publishing, at purchase and before every launch: dangerous calls, endless loops, executables. A purchased script is injected into the game at launch and removed after you quit, and the copy on disk is encrypted for your PC.',

  'final.title': 'All your games are waiting',
  'final.p': 'One installer, about 150 MB. ModLaunch finds your games by itself — all that’s left is to pick what to play.',
  'final.zip': 'Portable version (zip)',
});

// Подписи разделов, которых нет в разметке (в разметке — только первый).
Object.assign(ru, {
  'cap.library': 'Все игры с компьютера — из Steam, Epic, GOG и любых папок. Обложки, коллекции, избранное, время в игре и поиск по 141 711 играм.',
  'cap.game-installed': 'У каждой игры своя страница: «Играть», установленные моды, каталог, настройки модов, профили, сохранения и журнал.',
  'cap.game': 'Thunderstore, Nexus Mods и ModLaunch Hub в одном списке. Разделы, «Нужные», фильтры. «Установить» ставит и загрузчик, и всё, что нужно моду.',
  'cap.ai': 'Опишите словами, как хотите играть, — ИИ соберёт сборку из настоящего каталога, объяснит каждый выбор и поставит её одной кнопкой.',
  'cap.friends': 'Один список друзей на все игры: кто в сети и во что играет. Серверы друзей и свой сервер Minecraft прямо на этом компьютере.',
  'cap.rewards': 'Уровень растёт от игры. На каждом уровне со 2-го по 50-й — награда: рамки, баннеры, титулы и значки. Достижения у каждой игры свои.',
  'cap.blocks': 'ModScript X — среда разработки модов: блоки как в Scratch, код, отладчик и клавиша F6, которая ставит мод в игру.',
  'cap.market': 'Витрина работ авторов: скрипты, кусочки кода, модели и сборки. Купленное вставляется в игру при запуске, а доход автор видит в аккаунте.',
  'cap.bigpicture': 'F11 — и ModLaunch на весь экран, как на телевизоре. Управление геймпадом, запуск и моды с дивана.',
  'cap.look': 'Темы и акценты, шрифты, размер, анимации и виджеты главной. Готовые стили одним нажатием и свой стиль — файлом.',
  'games.new': 'новая',
  'hub.empty': 'Станьте первым автором в Hub',
  'hub.empty.p': 'Опубликуйте готовый архив мода или соберите мод в Creator Hub — его сразу увидят все, в программе и на этом сайте.',
  'hub.empty.btn': 'Как опубликовать',
  'hub.soon': 'ModLaunch Hub открывается',
  'hub.soon.p': 'Галерею прямо сейчас включают. Моды, опубликованные в программе, появятся здесь.',
});

// «5 модов», «2 мода», «1 мод».
function modsWord(n) {
  if (lang === 'en') return n === 1 ? 'mod' : 'mods';
  const a = n % 10, b = n % 100;
  if (a === 1 && b !== 11) return 'мод';
  if (a >= 2 && a <= 4 && (b < 12 || b > 14)) return 'мода';
  return 'модов';
}
const shotSrc = (name) => `img/app/${lang === 'en' ? 'en' : 'ru'}/${name}.webp`;

/* --- первый экран: сцена игры и полка обложек --- */
const HERO = [
  { key: 'minecraft', art: 'minecraft' },
  { key: 'lethal-company', art: 1966720 },
  { key: 'stardew-valley', art: 413150 },
  { key: 'subnautica', art: 264710 },
  { key: 'hollow-knight-silksong', art: 1030300 },
  { key: 'valheim', art: 892970 },
  { key: 'repo', art: 3241660 },
  { key: 'hollow-knight', art: 367520 },
];
let heroAt = 0;
function heroMeta(g) {
  if (g.id === 'minecraft') return lang === 'en' ? 'Fabric, Quilt, Forge and NeoForge · Modrinth' : 'Fabric, Quilt, Forge и NeoForge · Modrinth';
  const installs = lang === 'en' ? `${g.loader} installs itself` : `${g.loader} ставится сам`;
  return `${installs} · ${fmt(g.mods)} ${modsWord(g.mods)}`;
}
function renderShelf() {
  const shelf = document.getElementById('shelf');
  if (!shelf) return;
  shelf.innerHTML = HERO.map((h, i) => {
    const g = gameById(h.key);
    return `<li style="--i:${i}"${i === heroAt ? ' class="is-on"' : ''}><button type="button" aria-pressed="${i === heroAt}" aria-label="${esc(g.name)}" data-i="${i}">
      <img src="img/art/${esc(String(h.art))}.webp" alt="" width="200" height="300" /></button></li>`;
  }).join('');
  shelf.querySelectorAll('button').forEach((b) => b.addEventListener('click', () => setHero(Number(b.dataset.i))));
}
function setHero(i) {
  const h = HERO[i];
  const g = gameById(h.key);
  if (!g) return;
  heroAt = i;
  const hero = document.getElementById('hero');
  hero.style.setProperty('--game', g.color);
  document.querySelectorAll('#shelf button').forEach((b) => { const on = Number(b.dataset.i) === i; b.setAttribute('aria-pressed', String(on)); b.parentElement.classList.toggle('is-on', on); });
  const scene = document.getElementById('scene');
  const next = new Image();
  next.alt = '';
  next.src = `img/hero/${h.key}.webp`;
  const swap = () => {
    scene.append(next);
    requestAnimationFrame(() => requestAnimationFrame(() => {
      scene.querySelectorAll('img.is-on').forEach((old) => {
        old.classList.remove('is-on');
        setTimeout(() => old.remove(), 800);
      });
      next.classList.add('is-on');
    }));
  };
  if (next.complete) swap(); else next.addEventListener('load', swap, { once: true });
  const logo = document.getElementById('nowLogo');
  logo.src = `img/logo/${h.key}.webp`;
  logo.alt = g.name;
  document.getElementById('nowMeta').textContent = heroMeta(g);
}
// Заранее подгружаем арты полки, чтобы смена сцены шла без пауз.
function preloadHeroes() {
  const go = () => HERO.slice(1).forEach((h) => { const i = new Image(); i.src = `img/hero/${h.key}.webp`; });
  if ('requestIdleCallback' in window) requestIdleCallback(go, { timeout: 3000 }); else setTimeout(go, 1500);
}

/* --- разделы программы --- */
const rail = document.getElementById('rail');
const shot = document.getElementById('shot');
function showTab(btn, focus = false, center = false) {
  if (!btn) return;
  // На узком экране разделы — лента: прокручиваем только её, не страницу.
  if (center && rail.scrollWidth > rail.clientWidth) rail.scrollTo({ left: btn.offsetLeft - (rail.clientWidth - btn.offsetWidth) / 2, behavior: calm ? 'auto' : 'smooth' });
  rail.querySelectorAll('[role="tab"]').forEach((b) => { b.setAttribute('aria-selected', String(b === btn)); b.tabIndex = b === btn ? 0 : -1; });
  if (focus) btn.focus({ preventScroll: true });
  const name = btn.dataset.shot;
  const titleKey = btn.querySelector('[data-t]')?.dataset.t;
  const title = titleKey ? t(titleKey) : btn.textContent.trim();
  const capKey = `cap.${name}`;
  const titleNode = document.getElementById('shotTitle');
  titleNode.textContent = title;
  titleNode.removeAttribute('data-t');
  const text = document.getElementById('shotText');
  text.textContent = t(capKey);
  text.removeAttribute('data-t');
  const src = shotSrc(name);
  if (shot.getAttribute('src') === src) return;
  shot.classList.add('is-out');
  const img = new Image();
  img.src = src;
  const done = () => { shot.src = src; shot.alt = title; shot.classList.remove('is-out'); };
  if (img.complete) setTimeout(done, 120); else { img.addEventListener('load', done, { once: true }); img.addEventListener('error', done, { once: true }); }
}
if (rail) {
  const tabs = [...rail.querySelectorAll('[role="tab"]')];
  tabs.forEach((b, i) => {
    b.tabIndex = i === 0 ? 0 : -1;
    b.addEventListener('click', () => showTab(b, false, true));
    b.addEventListener('keydown', (e) => {
      const k = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 }[e.key];
      if (k) { e.preventDefault(); showTab(tabs[(i + k + tabs.length) % tabs.length], true, true); }
      if (e.key === 'Home') { e.preventDefault(); showTab(tabs[0], true, true); }
      if (e.key === 'End') { e.preventDefault(); showTab(tabs[tabs.length - 1], true, true); }
    });
  });
}
function swapShots() {
  document.querySelectorAll('img.js-shot').forEach((img) => (img.src = shotSrc(img.dataset.shot)));
  const on = rail?.querySelector('[aria-selected="true"]');
  if (on) showTab(on);
}

/* --- игры с полной поддержкой --- */
function renderGames() {
  const grid = document.getElementById('gamesGrid');
  if (!grid) return;
  grid.innerHTML = GAMES.map((g) => `
    <article class="game" style="--g:${/^#[0-9a-f]{6}$/i.test(g.color || '') ? g.color : '#7b5cff'}">
      <img src="img/art/${esc(String(g.steam))}.webp" alt="" loading="lazy" data-fallback="${esc(steamArt(g.steam))}" />
      <span class="count">${esc(`${compact(g.mods)} ${modsWord(g.mods)}`)}</span>
      ${g.fresh ? `<span class="badge">${esc(t('games.new'))}</span>` : ''}
      <div><h3>${esc(g.name)}</h3><p>${esc(g.loader)} · ${esc(g.src)}</p></div>
    </article>`).join('');
  // Своей обложки нет — берём из Steam; не вышло и там — убираем картинку. Без встроенных обработчиков (CSP).
  grid.querySelectorAll('img[data-fallback]').forEach((img) => img.addEventListener('error', () => {
    if (img.dataset.tried) img.remove();
    else { img.dataset.tried = '1'; img.src = img.dataset.fallback; }
  }));
}

/* --- ModScript X: блоки и тот же мод кодом --- */
const STUDIO = {
  ru: {
    mod: 'Мой первый мод', piece: 'Двойное золото', pieceSub: 'кусочек · бесплатно', hello: 'Привет! Этот мод сделал я', lucky: 'Сегодня тебе везёт!',
    when: 'Когда', start: 'игра началась', tick: 'идёт игра (каждый кадр)', say: 'Сказать', ifChance: 'Если случайно, шанс', ifLess: 'Если', less: 'меньше',
    add: 'Прибавить', to: 'к',
  },
  en: {
    mod: 'My first mod', piece: 'Double gold', pieceSub: 'code piece · free', hello: 'Hi! I made this mod', lucky: 'Lucky you today!',
    when: 'When', start: 'the game starts', tick: 'the game runs (every frame)', say: 'Say', ifChance: 'If random, chance', ifLess: 'If', less: 'less than',
    add: 'Add', to: 'to',
  },
};
function renderStudio() {
  const box = document.getElementById('viewBlocks');
  const pre = document.getElementById('modx');
  if (!box || !pre) return;
  const s = STUDIO[lang === 'en' ? 'en' : 'ru'];
  const slot = (x) => `<span class="slot">${esc(x)}</span>`;
  box.innerHTML = `
    <div class="piece"><i>✦</i><span><b>${esc(s.piece)}</b><small>ModLaunch · ${esc(s.pieceSub)}</small></span></div>
    <div class="blk blk--when"><div class="blk__head">⚡ ${esc(s.when)} ${slot(s.start)}</div><div class="blk__body">
      <div class="cmd cmd--say">${esc(s.say)} ${slot(s.hello)}</div>
      <div class="blk blk--if"><div class="blk__head">${esc(s.ifChance)} ${slot('50')} %</div><div class="blk__body">
        <div class="cmd cmd--say">${esc(s.say)} ${slot(s.lucky)}</div>
      </div></div>
    </div></div>
    <div class="blk blk--when"><div class="blk__head">⚡ ${esc(s.when)} ${slot(s.tick)}</div><div class="blk__body">
      <div class="blk blk--if"><div class="blk__head">${esc(s.ifLess)} ${slot('health')} ${esc(s.less)} ${slot('20')}</div><div class="blk__body">
        <div class="cmd cmd--add">${esc(s.add)} ${slot('5')} ${esc(s.to)} ${slot('health')}</div>
      </div></div>
    </div></div>`;
  const k = (x) => `<span class="t-k">${x}</span>`;
  const str = (x) => `<span class="t-s">"${esc(x)}"</span>`;
  const n = (x) => `<span class="t-n">${x}</span>`;
  pre.innerHTML = [
    `${k('mod')} ${str(s.mod)}`,
    `${k('engine')} unity`,
    '',
    `${k('use')} <span class="t-u">"${esc(s.piece)}"</span> (gold_bonus = ${n(3)})`,
    '',
    `${k('hook')} start {`,
    `    ${k('say')} ${str(s.hello)}`,
    `    ${k('if')} <span class="t-v">chance</span>(${n(50)}) {`,
    `        ${k('say')} ${str(s.lucky)}`,
    '    }',
    '}',
    '',
    `${k('hook')} tick {`,
    `    ${k('if')} health &lt; ${n(20)} {`,
    `        ${k('add')} health ${n(5)}`,
    '    }',
    '}',
  ].join('\n');
}
const studio = document.getElementById('studio');
studio?.querySelectorAll('.switch button').forEach((b) => b.addEventListener('click', () => {
  const code = b.dataset.view === 'code';
  studio.querySelectorAll('.switch button').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
  const blocks = document.getElementById('viewBlocks');
  const codeView = document.getElementById('viewCode');
  blocks.hidden = code;
  codeView.hidden = !code;
  blocks.setAttribute('aria-hidden', String(code));
  codeView.setAttribute('aria-hidden', String(!code));
}));

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
    box.innerHTML = `<div class="empty"><div class="empty__art">✦</div><div>
      <h3>${esc(t(soon ? 'hub.soon' : 'hub.empty'))}</h3><p>${esc(t(soon ? 'hub.soon.p' : 'hub.empty.p'))}</p>
      <a class="btn btn--primary" href="hub.html#publish">${esc(t('hub.empty.btn'))}</a></div></div>`;
  } else {
    box.innerHTML = `<div class="hub-grid">${Hub.sort(hubMods, 'trending').slice(0, 6).map(modCard).join('')}</div>`;
    box.querySelectorAll('.hmod').forEach((n) => n.classList.remove('reveal'));
    box.querySelectorAll('[data-mod]').forEach((b) => b.addEventListener('click', () => (location.href = `hub.html#mod=${encodeURIComponent(b.dataset.mod)}`)));
  }
}

/* --- «Что нового» из выпусков GitHub (заметки к выпуску — build/release-notes.md) --- */
releaseHooks.push((rel) => {
  // Карточки в разметке уже про 21.0: заменяем их, только если на GitHub выпуск новее.
  const shown = document.querySelector('#newsList .ver')?.textContent || '0';
  const latest = rel.all?.[0];
  if (!latest || lang === 'en' || !verNewer(String(latest.tag_name).replace(/^v/i, ''), shown)) return;
  const body = latest.body || '';
  const parts = [...body.matchAll(/^#{1,2} (?:Что нового в|What's new in|ModLaunch) ([\d.]+)\n([\s\S]*?)(?=\n#{1,2} |(?![\s\S]))/gm)].slice(0, 3);
  if (!parts.length) return;
  const date = new Date(latest.published_at).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' });
  document.getElementById('newsList').innerHTML = parts.map(([, ver, text], i) => {
    const items = [...text.matchAll(/^- \*\*(.+?)\*\*/gm)].map((m) => m[1].replace(/[.:]$/, '')).slice(0, 5);
    return `<article><span class="ver">${esc(ver)}</span>${i === 0 ? `<time>${esc(date)}</time>` : ''}
      <ul style="margin-top:14px">${items.map((x) => `<li>${esc(x)}</li>`).join('')}</ul></article>`;
  }).join('');
});

langHooks.push(() => {
  renderGames();
  renderStudio();
  swapShots();
  const g = gameById(HERO[heroAt].key);
  if (g) document.getElementById('nowMeta').textContent = heroMeta(g);
  if (hubMods) renderHub();
});
renderShelf();
setHero(0);
preloadHeroes();
renderGames();
renderStudio();
renderHub();
loadHub();
