'use strict';

/**
 * Окно установки и удаления ModHub.
 *
 * ModHub-Setup.exe — это сама программа: обёртка распаковывает её во
 * временную папку и запускает. Если имя файла содержит «Setup», вместо
 * лаунчера открывается это окно. Отсюда же можно просто запустить ModHub
 * без установки — тогда окно уступает место обычному.
 *
 * Удаление из «Установленных приложений» Windows запускает установленный
 * ModHub.exe с флагом --uninstall — и попадает сюда же.
 */

const { app, BrowserWindow, ipcMain, dialog, shell } = require('electron');
const path = require('node:path');
const os = require('node:os');
const fs = require('node:fs');
const { execFile, spawn } = require('node:child_process');
const core = require('./core');
const { STRINGS, pickLanguage } = require('./strings');

const PORTABLE_ENV = ['PORTABLE_EXECUTABLE_DIR', 'PORTABLE_EXECUTABLE_FILE', 'PORTABLE_EXECUTABLE_APP_FILENAME', 'CHROME_CRASHPAD_PIPE_NAME'];
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** 'install' | 'uninstall' | null — в каком режиме запущена программа. */
function detectMode(argv = process.argv, env = process.env) {
  if (argv.includes('--uninstall')) return 'uninstall';
  // Автообновление (3.0): программа скачала новый установщик и запустила его так.
  if (argv.includes('--update')) return 'update';
  if (argv.includes('--setup')) return 'install';
  if (argv.includes('--portable')) return null;
  const outer = env.PORTABLE_EXECUTABLE_FILE;
  return outer && /setup/i.test(path.basename(outer)) ? 'install' : null;
}

/**
 * Сценарий PowerShell кладём во временный .ps1 с BOM и запускаем через -File.
 * BOM нужен, чтобы PowerShell 5.1 прочитал кириллицу в путях как UTF-8, а не
 * в кодировке системы. Закодированную команду (-EncodedCommand) не берём
 * нарочно: антивирусы справедливо считают её приметой вредоносных программ.
 */
let psCounter = 0;
function runPowerShell(script, timeout = 30000) {
  const full = `$ErrorActionPreference='Stop'\r\n[Console]::OutputEncoding=[Text.Encoding]::UTF8\r\n${script}`;
  const file = path.join(os.tmpdir(), `modhub-setup-${process.pid}-${(psCounter += 1)}.ps1`);
  const exe = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  return new Promise((resolve, reject) => {
    try {
      fs.writeFileSync(file, `\uFEFF${full}`, 'utf8');
    } catch (error) {
      reject(Object.assign(new Error(error.message), { code: 'SETUP_PS' }));
      return;
    }
    const args = ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', file];
    execFile(exe, args, { windowsHide: true, timeout, maxBuffer: 1 << 20 }, (error, stdout, stderr) => {
      fs.rm(file, { force: true }, () => {});
      if (error) reject(Object.assign(new Error(String(stderr || error.message).trim().split('\n')[0]), { code: 'SETUP_PS' }));
      else resolve(String(stdout).trim());
    });
  });
}

function shortcutPaths() {
  return {
    start: path.join(app.getPath('appData'), 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'ModLaunch.lnk'),
    desktop: path.join(app.getPath('desktop'), 'ModLaunch.lnk'),
  };
}

/** Папки, где Windows держит ярлыки: Пуск и рабочий стол (свои и общие), закреплённое на панели задач. */
function shortcutFolders() {
  const appData = app.getPath('appData');
  const programData = process.env.ProgramData || 'C:\\ProgramData';
  const publicDir = process.env.PUBLIC || 'C:\\Users\\Public';
  return [
    { dir: path.join(appData, 'Microsoft', 'Windows', 'Start Menu', 'Programs'), kind: 'menu', depth: 2 },
    { dir: path.join(programData, 'Microsoft', 'Windows', 'Start Menu', 'Programs'), kind: 'menu', depth: 2 },
    { dir: app.getPath('desktop'), kind: 'desktop', depth: 0 },
    { dir: path.join(publicDir, 'Desktop'), kind: 'desktop', depth: 0 },
    { dir: path.join(appData, 'Microsoft', 'Internet Explorer', 'Quick Launch', 'User Pinned', 'TaskBar'), kind: 'pinned', depth: 0 },
  ];
}

/** Все ярлыки с «ModHub» в имени и то, куда они ведут. */
function findShortcuts() {
  const found = [];
  const walk = (dir, kind, depth) => {
    let entries = [];
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory() && depth > 0) walk(full, kind, depth - 1);
      else if (entry.isFile() && /\.lnk$/i.test(entry.name) && /modhub|modlaunch/i.test(entry.name)) {
        let target = '';
        try {
          target = shell.readShortcutLink(full).target;
        } catch {
          /* битый ярлык — пропускаем */
        }
        found.push({ path: full, target, kind });
      }
    }
  };
  for (const { dir, kind, depth } of shortcutFolders()) walk(dir, kind, depth);
  return found;
}

function writeShortcuts(exe, target, withDesktop) {
  // Картинку ярлыкам даём из отдельного .ico, а не из ModHub.exe: exe лежит
  // по тому же пути, что и у 1.0–1.4, и Windows по этому пути помнит старый
  // значок-кубик. Для нового файла старой картинки в кэше нет.
  const ico = path.join(target, 'resources', core.ICON_FILE);
  const icon = fs.existsSync(ico) ? ico : exe;
  const options = { target: exe, cwd: target, description: 'ModLaunch', icon, iconIndex: 0, appUserModelId: core.APP_ID };
  const links = shortcutPaths();
  const keep = [links.start, ...(withDesktop ? [links.desktop] : [])];

  // Старые ярлыки ModHub (Пуск, рабочий стол, закреплённые на панели задач)
  // ведут на прежний exe и показывают прежний значок. Лишние копии в Пуске
  // убираем, остальные переписываем на новую версию — место значка остаётся.
  for (const step of core.planShortcutCleanup(findShortcuts(), { keep })) {
    try {
      if (step.action === 'delete') fs.unlinkSync(step.path);
      else shell.writeShortcutLink(step.path, 'replace', options);
    } catch {
      /* защищённый или занятый ярлык — не трогаем */
    }
  }

  fs.mkdirSync(path.dirname(links.start), { recursive: true });
  shell.writeShortcutLink(links.start, 'create', options);
  if (withDesktop) shell.writeShortcutLink(links.desktop, 'create', options);
}

/** Просим Проводник перечитать значки — иначе старая картинка держится до перезагрузки. */
function refreshShellIcons() {
  const tool = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'ie4uinit.exe');
  if (process.platform !== 'win32' || !fs.existsSync(tool)) return;
  try {
    spawn(tool, ['-show'], { detached: true, stdio: 'ignore', windowsHide: true }).unref();
  } catch {
    /* не вышло — обновятся после перезагрузки */
  }
}

function removeShortcuts(exe) {
  for (const link of findShortcuts()) {
    try {
      if (path.resolve(link.target).toLowerCase() === path.resolve(exe).toLowerCase()) fs.unlinkSync(link.path);
    } catch {
      /* ярлык чужой или защищённый — не трогаем */
    }
  }
}

function probeExisting() {
  if (process.platform !== 'win32' || !app.isPackaged) return Promise.resolve(null);
  return runPowerShell(core.probeScript(), 10000)
    .then((out) => (out ? JSON.parse(out) : null))
    .catch(() => null);
}

/** В режиме разработки ничего не копируем — только показываем, как это выглядит. */
async function simulate(send) {
  for (const step of ['close', 'clean']) {
    send({ step });
    await delay(380);
  }
  const total = 262 * 1024 * 1024;
  for (let i = 1; i <= 50; i += 1) {
    send({ step: 'copy', done: (total * i) / 50, total, ratio: i / 50 });
    await delay(40);
  }
  send({ step: 'integrate' });
  await delay(600);
  send({ step: 'done', ratio: 1 });
}

function run(mode, { startApp }) {
  let win = null;
  let lang = 'ru';
  let busy = false;
  let switching = false;
  let installed = null;
  let removal = null;
  const probe = mode === 'install' || mode === 'update' ? probeExisting() : Promise.resolve(null);
  const send = (payload) => win?.webContents.send('setup:progress', payload);

  if (process.platform === 'win32') app.setAppUserModelId(`${core.APP_ID}.Setup`);

  app.on('window-all-closed', () => {
    if (!switching) app.quit();
  });

  // Запущенный exe не может стереть сам себя — папку удалит cmd после нашего выхода.
  app.on('will-quit', () => {
    if (!removal) return;
    const cmd = process.env.ComSpec || path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'cmd.exe');
    spawn(cmd, [core.removalCommand(removal)], {
      windowsVerbatimArguments: true,
      detached: true,
      stdio: 'ignore',
      windowsHide: true,
      cwd: os.tmpdir(),
    }).unref();
    removal = null;
  });

  ipcMain.handle('setup:info', async () => {
    // Холодный PowerShell иногда думает секунду-две. Окно ждать не должно:
    // не успел — предлагаем папку по умолчанию, её мы проверим и так.
    const existing = await Promise.race([probe, delay(1500).then(() => null)]);
    // ModHub 2.x ставился через NSIS в Programs\modhub — обновляем его на месте.
    const nsis = core.nsisInstallDir();
    const nsisHere = core.inspectTarget(nsis).kind === 'ours';
    const target = core.normalizeTarget(existing?.dir || (nsisHere ? nsis : core.defaultInstallDir()));
    const state = core.inspectTarget(target);
    return {
      mode,
      lang,
      strings: STRINGS[lang],
      version: app.getVersion(),
      target,
      existing: state.kind === 'ours' ? { version: state.version || existing?.version || null } : null,
    };
  });

  ipcMain.handle('setup:choose', async (_event, current) => {
    const result = await dialog.showOpenDialog(win, {
      defaultPath: current || undefined,
      properties: ['openDirectory', 'createDirectory', 'promptToCreate'],
    });
    if (result.canceled || !result.filePaths?.[0]) return null;
    const target = core.normalizeTarget(result.filePaths[0]);
    const state = core.inspectTarget(target);
    return { target, existing: state.kind === 'ours' ? { version: state.version } : null };
  });

  ipcMain.handle('setup:install', async (_event, options = {}) => {
    busy = true;
    try {
      if (!app.isPackaged) {
        await simulate(send);
        return { ok: true, simulated: true };
      }
      installed = await core.install({
        source: path.dirname(process.execPath),
        target: options.target,
        version: app.getVersion(),
        onStep: (step) => send({ step }),
        onProgress: (progress) => send({ step: 'copy', ...progress }),
        closeRunning: (dir) => runPowerShell(core.closeScript({ target: dir, exceptPid: process.pid })).catch(() => '0'),
        integrate: async ({ target, exe, version, sizeKB }) => {
          writeShortcuts(exe, target, options.desktop !== false);
          await runPowerShell(core.registerScript({ target, exe, version, sizeKB }));
          refreshShellIcons();
        },
      });
      return { ok: true, ...installed };
    } catch (error) {
      return { ok: false, code: error.code || 'generic', message: String(error.message || error) };
    } finally {
      busy = false;
    }
  });

  ipcMain.handle('setup:launch', () => {
    if (installed?.exe) {
      const env = { ...process.env };
      for (const name of PORTABLE_ENV) delete env[name];
      spawn(installed.exe, [], { cwd: path.dirname(installed.exe), detached: true, stdio: 'ignore', env }).unref();
    }
    setTimeout(() => app.quit(), 350);
    return true;
  });

  ipcMain.handle('setup:portable', async () => {
    if (busy) return false;
    switching = true;
    const setupWindow = win;
    win = null;
    await startApp();
    setupWindow?.destroy();
    switching = false;
    return true;
  });

  ipcMain.handle('setup:uninstall', async (_event, options = {}) => {
    busy = true;
    try {
      if (!app.isPackaged) {
        await simulate(send);
        return { ok: true, simulated: true };
      }
      const dir = path.dirname(process.execPath);
      send({ step: 'close' });
      await runPowerShell(core.closeScript({ target: dir, exceptPid: process.pid })).catch(() => '0');
      send({ step: 'integrate' });
      removeShortcuts(process.execPath);
      await runPowerShell(core.unregisterScript({ exe: process.execPath })).catch(() => '');
      refreshShellIcons();

      // Папку стираем, только если в ней наша отметка: запуск с --uninstall
      // откуда-нибудь ещё не должен ничего удалить.
      const userData = app.getPath('userData');
      removal = [
        fs.existsSync(path.join(dir, core.MARKER)) ? dir : null,
        options.wipe && /^modhub$/i.test(path.basename(userData)) ? userData : null,
      ].filter(Boolean);
      send({ step: 'done', ratio: 1 });
      return { ok: true };
    } catch (error) {
      return { ok: false, code: error.code || 'generic', message: String(error.message || error) };
    } finally {
      busy = false;
    }
  });

  ipcMain.handle('setup:license', () => {
    try {
      return fs.readFileSync(path.join(__dirname, '..', '..', 'setup', 'license-ru.txt'), 'utf8').replace(/^\uFEFF/, '');
    } catch {
      return '';
    }
  });

  ipcMain.handle('setup:window', (_event, action) => {
    if (action === 'min') win?.minimize();
    if (action === 'close' && !busy) app.quit();
    return !busy;
  });

  app.whenReady().then(() => {
    lang = pickLanguage(app.getLocale());
    win = new BrowserWindow({
      width: 880,
      height: 540,
      useContentSize: true,
      resizable: false,
      maximizable: false,
      fullscreenable: false,
      frame: false,
      show: false,
      icon: path.join(__dirname, '..', '..', 'assets', process.platform === 'win32' ? 'icon.ico' : 'icon.png'),
      backgroundColor: '#0e0c16',
      title: STRINGS[lang][mode === 'uninstall' ? 'window.titleUninstall' : 'window.title'],
      webPreferences: {
        preload: path.join(__dirname, 'preload.js'),
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: false,
      },
    });
    win.setMenu(null);
    win.loadFile(path.join(__dirname, '..', '..', 'setup', 'setup.html'), { query: { mode } });
    win.once('ready-to-show', () => win.show());
    win.webContents.setWindowOpenHandler(({ url }) => {
      shell.openExternal(url);
      return { action: 'deny' };
    });
  });
}

module.exports = { detectMode, run, writeShortcuts, removeShortcuts };
