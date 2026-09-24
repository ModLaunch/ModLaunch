'use strict';

/**
 * Самопроверка программы целиком (2.1).
 *
 *   npx electron tools/selftest.js
 *
 * ModHub из исходников с ненастоящей Subnautica (папка данных Unity, одно
 * сохранение, а вместо игры — ping на 25 секунд) проходит по экранам,
 * «запускает игру», открывает оверлей поверх своего окна, делает копию
 * сохранений из оверлея и снимает всё в PNG: tools/selftest-out/.
 * На CI (Windows) снимки уходят в ветку ci-screens.
 *
 * Данные программы — во временной папке: настоящие настройки не трогаются.
 */

const { app, BrowserWindow, desktopCapturer, globalShortcut, screen } = require('electron');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const OUT = path.join(__dirname, 'selftest-out');
const ROOT = fs.mkdtempSync(path.join(os.tmpdir(), 'modhub-selftest-'));
const WIN = process.platform === 'win32';
const KEY = 'CommandOrControl+Shift+M';

fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(OUT, { recursive: true });

/* --- ненастоящая Subnautica --- */
const GAME = path.join(ROOT, 'Games', 'Subnautica');
fs.mkdirSync(path.join(GAME, 'Subnautica_Data'), { recursive: true });
fs.mkdirSync(path.join(GAME, 'SNAppData', 'SavedGames', 'slot0000'), { recursive: true });
fs.writeFileSync(path.join(GAME, 'SNAppData', 'SavedGames', 'slot0000', 'gameinfo.json'), '{"selftest":true}');
fs.writeFileSync(path.join(GAME, 'UnityPlayer.dll'), '');
const exe = path.join(GAME, WIN ? 'Subnautica.exe' : 'Subnautica.x86_64');
fs.copyFileSync(WIN ? path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'PING.EXE') : '/bin/sleep', exe);
if (!WIN) fs.chmodSync(exe, 0o755);

/* --- свои данные программы --- */
const USER = path.join(ROOT, 'user');
fs.mkdirSync(USER, { recursive: true });
process.env.MODLAUNCH_KEEP_USERDATA = '1';
app.setPath('userData', USER);
fs.writeFileSync(
  path.join(USER, 'settings.json'),
  JSON.stringify({
    gamePaths: { subnautica: GAME },
    detected: {},
    language: 'ru',
    theme: 'dark',
    onboardingDone: true,
    launchArgs: { subnautica: WIN ? '-n 26 127.0.0.1' : '25' },
    notes: { subnautica: 'База у Затерянной реки: -1150, -680\nНужно: 4 кианита, 2 алмаза' },
  })
);

const results = [];
function check(name, ok, detail = '') {
  results.push({ name, ok: Boolean(ok), detail });
  console.log(`${ok ? 'PASS' : 'FAIL'}  самопроверка: ${name}${detail ? `  —  ${detail}` : ''}`);
}

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitFor(fn, timeout = 30000, step = 250) {
  const until = Date.now() + timeout;
  while (Date.now() < until) {
    const value = await fn();
    if (value) return value;
    await delay(step);
  }
  return null;
}

async function tour() {
  const main = await waitFor(() => BrowserWindow.getAllWindows()[0]);
  if (!main) throw new Error('окно не открылось');
  await waitFor(() => main.isVisible(), 20000);
  main.setBounds({ x: 0, y: 0, width: 1366, height: 768 });
  const js = (code) => main.webContents.executeJavaScript(code);
  // Главная готова, когда ушла заставка.
  await waitFor(() => js(`!document.body.classList.contains('is-booting')`), 30000);
  await delay(3000);

  const shot = async (name, wait = 1500) => {
    await delay(wait);
    const image = await main.webContents.capturePage();
    fs.writeFileSync(path.join(OUT, `${name}.png`), image.toPNG());
  };
  const text = (selector) => js(`[...document.querySelectorAll(${JSON.stringify(selector)})].map((n) => n.textContent.trim())`);

  await shot('01-home');
  const tiles = await text('.gtile__title, .gtile__logo');
  const expected = require('../src/main/games').all().length;
  check(`на главной все игры (${expected})`, (await js(`document.querySelectorAll('.gtile').length`)) === expected, tiles.join(' · '));

  await js(`openGame('subnautica', 'downloads')`);
  await shot('02-game');
  check('Subnautica найдена по пути из настроек', await js(`Boolean(entry('subnautica')?.game?.found)`));

  await js(`openTab('market')`);
  await waitFor(() => js(`document.querySelectorAll('.mrow, .card').length > 3`), 30000);
  await shot('03-catalog');
  check('каталог Subnautica загрузился с Nexus', await js(`document.querySelectorAll('.mrow, .card').length > 3`));

  await js(`document.querySelectorAll('.secpill')[2]?.click()`);
  await waitFor(() => js(`Boolean(document.querySelector('.rsbar'))`), 15000);
  await shot('04-shaders', 3000);
  check('в «Шейдерах» — полоса ReShade', await js(`Boolean(document.querySelector('.rsbar'))`));

  // Поиск из шапки со страницы игры, открытой на «Установленных»: должен
  // показать каталог с найденным, а не остаться на прежнем экране.
  await js(`openGame('subnautica', 'downloads')`);
  await delay(800);
  await js(`(() => { const i = document.getElementById('searchInput'); i.value = 'seamoth'; i.dispatchEvent(new Event('input', { bubbles: true })); })()`);
  const searched = await waitFor(
    () => js(`state.gameTab === 'market' && state.catalogSection === 'all' && [...document.querySelectorAll('.mrow h3, .card h3')].some((n) => /seamoth/i.test(n.textContent))`),
    30000
  );
  await shot('04b-search', 800);
  check('поиск из шапки открывает каталог с найденным', searched, await js(`[...document.querySelectorAll('.mrow h3, .card h3')].slice(0, 3).map((n) => n.textContent).join(' / ')`));
  await js(`(() => { const i = document.getElementById('searchInput'); i.value = ''; state.query = ''; })()`);

  await js(`openMod('subnautica', '2800')`);
  await waitFor(() => js(`Boolean(document.querySelector('.product h1'))`), 20000);
  await shot('05-mod', 4000);
  const deps = await text('.deps__name span');
  check('у Decorations Mod в зависимостях Nautilus', deps.some((d) => /nautilus/i.test(d)), deps.join(', '));

  await js(`openFriends()`);
  await shot('06-friends');
  await js(`state.settingsTab = 'launch'; go('settings')`);
  await shot('07-settings-launch');
  await js(`go('home')`);
  await delay(1000);

  /* --- «игра»: оверлей, копия сохранений --- */
  const launched = await js(`window.modhub.games.launch('subnautica')`);
  check('игра запускается из ModHub', launched?.ok, launched?.ok ? '' : launched?.error);
  const armed = await waitFor(() => globalShortcut.isRegistered(KEY), 5000);
  check('пока идёт игра, Ctrl+Shift+M занят под оверлей', armed);

  await js(`window.modhub.overlay.preview('subnautica')`);
  const overlay = await waitFor(() => BrowserWindow.getAllWindows().find((w) => w !== main && w.isVisible()), 10000);
  check('оверлей открылся поверх всех окон', overlay && overlay.isAlwaysOnTop());
  if (overlay) {
    await delay(2500);
    const display = screen.getDisplayMatching(overlay.getBounds());
    const sources = await desktopCapturer.getSources({ types: ['screen'], thumbnailSize: display.size });
    const source = sources.find((s) => String(s.display_id) === String(display.id)) ?? sources[0];
    if (source) fs.writeFileSync(path.join(OUT, '08-overlay-screen.png'), source.thumbnail.toPNG());
    const own = await overlay.webContents.capturePage();
    fs.writeFileSync(path.join(OUT, '09-overlay-page.png'), own.toPNG());
    const dom = JSON.parse(
      await overlay.webContents.executeJavaScript(
        `JSON.stringify({ timer: document.getElementById('ovTimer').textContent, note: document.getElementById('ovNote')?.value ?? '', modhub: typeof window.modhub, require: typeof require })`
      )
    );
    check('в оверлее идёт время сеанса', /^\d+:\d\d/.test(dom.timer), dom.timer);
    check('в оверлее — заметка к игре', dom.note.includes('Затерянной'), dom.note.split('\n')[0]);
    check('оверлей в песочнице: ни window.modhub, ни require', dom.modhub === 'undefined' && dom.require === 'undefined');
    const backup = await overlay.webContents.executeJavaScript(`window.overlay.backup()`);
    check('копия сохранений из оверлея', backup?.ok, backup?.ok ? backup.data?.name ?? '' : backup?.error);
    overlay.webContents.sendInputEvent({ type: 'keyDown', keyCode: 'Escape' });
    await waitFor(() => !overlay.isVisible(), 5000);
    check('Esc прячет оверлей', !overlay.isVisible());
  }

  const released = await waitFor(() => !globalShortcut.isRegistered(KEY), 45000, 500);
  check('игру закрыли — сочетание снова свободно', released);
  await shot('10-after-game', 1500);
  const time = await js(`window.modhub.playtime.all()`);
  check('игровое время посчитано', (time?.data?.subnautica?.sessions ?? 0) >= 1, JSON.stringify(time?.data?.subnautica ?? {}));

  /* --- DXVK (3.1): настоящий DXVK с GitHub в ненастоящую старую игру --- */
  const OLD = path.join(ROOT, 'Games', 'Old Game', 'bin');
  fs.mkdirSync(OLD, { recursive: true });
  const oldExe = path.join(OLD, 'oldgame.exe');
  fs.copyFileSync(WIN ? path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'PING.EXE') : '/bin/sleep', oldExe);
  fs.writeFileSync(path.join(OLD, 'dxgi.dll'), 'own dll of the game');
  if (WIN) {
    const looked = await js(`window.modhub.dxvk.inspect(${JSON.stringify(oldExe)})`);
    check('DXVK: exe игры распознан', looked?.ok && looked.data.arch === 'x64' && looked.data.name === 'Old Game', JSON.stringify(looked?.data ?? looked));
    const put = await js(`window.modhub.dxvk.install(${JSON.stringify(oldExe)}, 'dx11')`);
    const files = fs.readdirSync(OLD);
    check('DXVK: скачан с GitHub и поставлен', put?.ok && ['d3d11.dll', 'dxgi.dll', 'd3d10core.dll', 'dxvk.modlaunch.json'].every((f) => files.includes(f)), put?.ok ? `${put.data.version}: ${files.join(', ')}` : put?.error);
    check('DXVK: своя DLL игры сохранена', files.includes('dxgi.dll.modlaunch-backup'));
    // После закрытия игры программа сама возвращается на главную — ждём этого и уходим в «Графику».
    await delay(2000);
    await js(`state.settingsTab = 'graphics'; state.dxvkGames = null; go('settings')`);
    await waitFor(() => js(`Boolean(document.querySelector('.dxvk__row'))`), 8000);
    await shot('11-dxvk', 1200);
    const off = await js(`window.modhub.dxvk.remove(${JSON.stringify(oldExe)})`);
    const left = fs.readdirSync(OLD).sort();
    check('DXVK: убран, всё как было', off?.ok && left.join(',') === 'dxgi.dll,oldgame.exe' && fs.readFileSync(path.join(OLD, 'dxgi.dll'), 'utf8') === 'own dll of the game', left.join(', '));
  }

  // Закрыли окно — программа должна выйти, спрятанный оверлей её не держит.
  const quit = new Promise((resolve) => app.once('will-quit', () => resolve(true)));
  main.close();
  check('закрыли окно — программа вышла', await Promise.race([quit, delay(8000).then(() => false)]));
}

app.whenReady().then(() => {
  tour()
    .catch((error) => check('самопроверка дошла до конца', false, error.stack || error.message))
    .finally(() => {
      const failed = results.filter((r) => !r.ok).length;
      fs.writeFileSync(path.join(OUT, 'results.json'), JSON.stringify({ results, failed, platform: process.platform }, null, 2));
      console.log(`\nИтого: ${results.length - failed} PASS, ${failed} FAIL`);
      app.exit(failed ? 1 : 0);
    });
});

require('../src/main/index.js');
