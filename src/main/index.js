'use strict';

const { app, BrowserWindow, ipcMain, dialog, shell, screen, safeStorage } = require('electron');
const fs = require('node:fs');
const path = require('node:path');

const games = require('./games');
const { JsonStore } = require('./core/store');
const { ModRegistry } = require('./core/registry');
const { downloadFile } = require('./core/download');
const install = require('./core/install');
const diagnostics = require('./core/diagnostics');
const thunderstore = require('./core/sources/thunderstore');
const modlinks = require('./core/sources/modlinks');
const nexus = require('./core/sources/nexus');
const hkmedia = require('./core/sources/hkmedia');
const { SteamMedia } = require('./core/sources/steammedia');
const { ReviewsClient, ReviewsError, statsKey } = require('./core/reviews');
const { PlayLog, catalogIdOf } = require('./core/playlog');
const { Account, AccountError } = require('./core/account');
const { AdsClient, loadAdsConfig } = require('./core/ads');
const { Profiles } = require('./core/profiles');
const { Backups } = require('./core/backups');
const { PlayTime } = require('./core/playtime');
const { buildPack, parsePack } = require('./core/modpack');
const { isNewer } = require('./core/updates');
const { sectionOf } = require('./games/sections');
const watchdl = require('./core/watchdl');
const winstate = require('./core/winstate');
const { APP_ID, ICON_FILE } = require('./setup/core');
const {
  explainAccessError,
  relaunchElevated,
  isElevated,
  RELAUNCH_FLAG,
} = require('./core/permissions');
const { t, setLang, getLang } = require('./i18n');

const isDev = process.argv.includes('--dev');

let mainWindow = null;
let settings = null;
/** Отзывы и оценки: общий сервер плюс копия на диске. */
let reviews = null;
let account = null;
/** Какие моды человек скачал и с какими играл: пропуск к оценке (1.9.7). */
let playlog = null;
/** Лента рекламы для места в шапке. */
let ads = null;
/** Настоящие картинки игр из магазина Steam. */
let steamMedia = null;
/** Картинки установленных модов, найденные по сети: чтобы не искать их при каждом открытии. */
let installedMedia = null;
/** Профили модов, резервные копии сохранений и игровое время (1.10). */
let profiles = null;
let backups = null;
let playtime = null;
const registries = new Map();
/** Ссылка nxm://, пришедшая до того, как окно успело открыться. */
let pendingNxmLink = null;

/* ------------------------------------------------------------------ *
 *  Протокол nxm://
 *
 *  Это главный канал связи с Nexus Mods и единственный законный способ
 *  получить файл для бесплатного аккаунта: человек жмёт кнопку на сайте,
 *  браузер передаёт ссылку нам, дальше всё делает программа.
 * ------------------------------------------------------------------ */

function registerProtocol() {
  // Запуск без установки идёт из временной папки, которая исчезнет после
  // выхода. Прописать nxm:// туда — значит сломать ссылки с сайта.
  if (process.env.PORTABLE_EXECUTABLE_FILE) return;
  if (process.defaultApp && process.argv.length >= 2) {
    // Режим разработки: надо явно указать, чем запускать.
    app.setAsDefaultProtocolClient('nxm', process.execPath, [path.resolve(process.argv[1])]);
  } else {
    app.setAsDefaultProtocolClient('nxm');
  }
}

function extractNxmLink(argv) {
  return argv.find((arg) => typeof arg === 'string' && arg.startsWith('nxm://')) ?? null;
}

function deliverNxmLink(link) {
  if (!link) return;
  if (mainWindow && !mainWindow.isDestroyed()) {
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.focus();
    mainWindow.webContents.send('nxm-link', { link });
  } else {
    pendingNxmLink = link;
  }
}

/* ------------------------------------------------------------------ *
 *  Окно
 * ------------------------------------------------------------------ */

/**
 * Иконка окна задаётся явно. Без неё запуск из папки с исходниками
 * (npm start) показывал бы в заголовке и на панели задач значок Electron,
 * а не ModHub: знак в окне и снаружи расходились.
 */
function appIcon() {
  const file = path.join(__dirname, '..', 'assets', process.platform === 'win32' ? 'icon.ico' : 'icon.png');
  return fs.existsSync(file) ? file : undefined;
}

function createWindow() {
  const elevated = isElevated();

  // Окно открывается таким, каким его закрыли; в первый раз — под экран.
  const stateFile = path.join(dataDir(), 'window.json');
  const main = screen.getPrimaryDisplay();
  const others = screen.getAllDisplays().filter((display) => display.id !== main.id);
  const areas = [main, ...others].map((display) => display.workArea);
  const place = winstate.restoreBounds(winstate.readState(stateFile), areas);

  mainWindow = new BrowserWindow({
    title: `ModHub ${app.getVersion()}${elevated ? t('window.admin') : ''}`,
    ...place.bounds,
    minWidth: winstate.MIN_W,
    minHeight: winstate.MIN_H,
    show: false,
    icon: appIcon(),
    // Цвет фона заставки: окно появляется сразу тёмным, без белой вспышки.
    backgroundColor: '#0f1116',
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
    },
  });

  // Если окно закрепят на панели задач, значок возьмётся из отдельного
  // .ico рядом с программой, а запускаться будет установленный ModHub.exe.
  // У запуска без установки программа живёт во временной папке — закреплять
  // её некуда, поэтому там ничего не задаём.
  const ico = path.join(process.resourcesPath || '', ICON_FILE);
  if (process.platform === 'win32' && app.isPackaged && !process.env.PORTABLE_EXECUTABLE_FILE && fs.existsSync(ico)) {
    mainWindow.setAppDetails({
      appId: APP_ID,
      appIconPath: ico,
      appIconIndex: 0,
      relaunchCommand: `"${process.execPath}"`,
      relaunchDisplayName: 'ModHub',
    });
  }

  mainWindow.loadFile(path.join(__dirname, '..', 'renderer', 'index.html'));

  winstate.track(mainWindow, stateFile);

  mainWindow.once('ready-to-show', () => {
    if (place.maximized) mainWindow.maximize();
    mainWindow.show();
    if (pendingNxmLink) {
      deliverNxmLink(pendingNxmLink);
      pendingNxmLink = null;
    }
    if (isDev) mainWindow.webContents.openDevTools({ mode: 'detach' });
  });

  // Внешние ссылки открываем в браузере, а не внутри приложения.
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });
}

/* ------------------------------------------------------------------ *
 *  Состояние
 * ------------------------------------------------------------------ */

function dataDir() {
  return app.getPath('userData');
}

/**
 * Файл-рукопожатие между старым и новым экземпляром при повышении прав.
 * Лежит в данных пользователя, поэтому виден обоим: права меняются,
 * профиль пользователя — нет.
 */
function elevationMarker() {
  return path.join(dataDir(), 'elevation-handshake');
}

/** Журналы чужих установщиков: SMAPI и прочих, кто умеет падать по-своему. */
function logFileFor(name) {
  return path.join(dataDir(), 'logs', `${name}.log`);
}

function loadSettings() {
  settings = new JsonStore(path.join(dataDir(), 'settings.json'), {
    gamePaths: {},
    /**
     * Пути, которые ModHub нашёл сам.
     *
     * Ради этой строчки и затевалась половина версии 1.2. Раньше путь
     * не запоминался вовсе: каждый запуск программа заново обходила диски
     * для каждой игры, окно при этом стояло пустым, а игра, которую
     * не удалось найти, искалась снова и снова при каждом открытии.
     * Теперь поиск — разовая работа, а её результат живёт здесь.
     */
    detected: {},
    nexusApiKey: '',
    theme: 'dark',
    language: 'ru',
    onboardingDone: false,
  });
  setLang(settings.data.language);
  return settings;
}

/**
 * Адрес общего сервера отзывов. Лежит внутри программы (reviews.config.json),
 * поэтому у всех, кто поставил одну и ту же сборку, сервер один и тот же —
 * и отзыв друга виден каждому. Пустой файл — отзывы выключены, программа
 * работает как раньше и честно пишет, что сервер не подключён.
 */
function loadReviewsConfig() {
  try {
    const file = path.join(__dirname, 'reviews.config.json');
    const data = JSON.parse(fs.readFileSync(file, 'utf8'));
    return { projectId: data.projectId ?? '', apiKey: data.apiKey ?? '' };
  } catch {
    return { projectId: '', apiKey: '' };
  }
}

/**
 * Пропуск аккаунта на диске шифруется средствами Windows (DPAPI через
 * safeStorage): скопированный на другой компьютер файл не откроется.
 */
function tokenProtection() {
  try {
    if (!safeStorage?.isEncryptionAvailable?.()) return null;
    return {
      encrypt: (text) => safeStorage.encryptString(text).toString('base64'),
      decrypt: (text) => safeStorage.decryptString(Buffer.from(text, 'base64')),
    };
  } catch {
    return null;
  }
}

function startReviews() {
  const config = loadReviewsConfig();
  reviews = new ReviewsClient({ config, dataDir: dataDir(), version: app.getVersion() });
  account = new Account({
    config,
    file: path.join(dataDir(), 'account.json'),
    protect: tokenProtection(),
    legacyAuth: reviews.legacyAuth(),
    lang: () => getLang(),
  });
  reviews.account = account;
  playlog = new PlayLog({ file: path.join(dataDir(), 'plays.json') });
  ads = new AdsClient({
    config: loadAdsConfig(path.join(__dirname, 'ads.config.json')),
    cacheFile: path.join(dataDir(), 'ads-cache.json'),
  });
  steamMedia = new SteamMedia({
    cacheFile: path.join(dataDir(), 'steam-media.json'),
    lang: () => (getLang() === 'en' ? 'english' : 'russian'),
  });
  installedMedia = new JsonStore(path.join(dataDir(), 'media-cache.json'), { items: {} });
  profiles = new Profiles({ file: path.join(dataDir(), 'profiles.json') });
  backups = new Backups({ dir: path.join(dataDir(), 'backups') });
  playtime = new PlayTime({ file: path.join(dataDir(), 'playtime.json') });
}

/** Сколько резервных копий держать на игру (настройка «Резервные копии»). */
function backupKeep() {
  const keep = Number(settings.data.backupKeep);
  return Number.isFinite(keep) && keep > 0 ? keep : 10;
}

/** Папка сохранений игры или null, если адаптер её не знает. */
function savesDirFor(game, state) {
  if (typeof game.savesDir !== 'function') return null;
  try {
    return game.savesDir(state.path) ?? null;
  } catch {
    return null;
  }
}

/** Ошибки новых функций 1.10 — текстом из словаря, как всё остальное. */
function explainCode(error) {
  if (error?.code && /^(PROFILE|BACKUP|PACK)_/.test(error.code)) {
    const explained = new Error(t('err.' + error.code));
    explained.alreadyExplained = true;
    return explained;
  }
  return error;
}

async function coded(fn) {
  try {
    return await fn();
  } catch (error) {
    throw explainCode(error);
  }
}

/** Ошибка сервера отзывов — человеческим языком и на языке интерфейса. */
function explainReviews(error) {
  if (error instanceof AccountError) return explainAccount(error);
  if (!(error instanceof ReviewsError)) return error;
  const explained = new Error(t(`err.reviews.${error.code}`, { reason: error.message }));
  explained.alreadyExplained = true;
  explained.code = `REVIEWS_${error.code}`;
  return explained;
}

function explainAccount(error) {
  if (!(error instanceof AccountError)) return error;
  const explained = new Error(t(`err.account.${error.code}`, { reason: error.message }));
  explained.alreadyExplained = true;
  explained.code = `ACCOUNT_${error.code}`;
  return explained;
}

async function accountCall(fn) {
  try {
    return await fn();
  } catch (error) {
    throw explainAccount(error);
  }
}

async function reviewsCall(fn) {
  try {
    return await fn();
  } catch (error) {
    throw explainReviews(error);
  }
}

/**
 * Можно ли этому человеку оценить этот мод (1.9.7).
 *
 * Оценка — только после того, как мод скачан через ModHub и с ним хотя бы
 * раз запускали игру. Заодно смотрим лог загрузчика: если игру запускали
 * мимо ModHub (Steam, ярлык), отметка «сыграно» появится и так.
 * Свой уже опубликованный отзыв править и удалять можно всегда.
 */
async function rateGate(gameId, modId) {
  const game = games.byId(gameId);
  const id = String(modId ?? '');
  if (!game || !id) return { ok: false, own: false, installed: false, enabled: false, played: false, gameFound: false };

  let records = [];
  let gameFound = false;
  try {
    const { state } = await stateFor(gameId, undefined, { scan: false });
    gameFound = Boolean(state.found);
    const registry = registryFor(gameId, state);
    if (registry) {
      records = registry.reconcile();
      playlog.observeLog(game, records, game.logPath(state.path));
    }
  } catch {
    /* игру не нашли — значит, и мод в ней не стоит */
  }

  const record = records.find((r) => catalogIdOf(game, r) === id) ?? null;
  const status = playlog.status(gameId, id, record);
  const own = Boolean(reviews.mine(gameId, id));
  return { ...status, gameFound, own, ok: status.played || own };
}

/** Путь, который мы уже знаем: сначала указанный человеком, потом найденный сами. */
function knownPathFor(gameId) {
  return settings.data.gamePaths[gameId] ?? settings.data.detected[gameId] ?? null;
}

/**
 * Запоминает найденный путь и чистит протухший.
 * Ручной выбор человека при этом не трогаем: он главнее нашего поиска.
 */
function rememberPath(gameId, state) {
  let changed = false;

  if (state.stalePath) {
    if (settings.data.detected[gameId] === state.stalePath) {
      delete settings.data.detected[gameId];
      changed = true;
    }
    if (settings.data.gamePaths[gameId] === state.stalePath) {
      delete settings.data.gamePaths[gameId];
      changed = true;
    }
  }

  if (state.found && settings.data.detected[gameId] !== state.path) {
    settings.data.detected[gameId] = state.path;
    changed = true;
  }

  if (changed) settings.save();
  return state;
}

/**
 * @param {string} gameId
 * @param {(stage: object) => void} [onProgress]
 * @param {{scan?: boolean, deep?: boolean, forget?: boolean}} [options]
 */
async function stateFor(gameId, onProgress, options = {}) {
  const game = games.byId(gameId);
  if (!game) throw new Error(t('err.unknownGame', { id: gameId }));

  if (options.forget) {
    delete settings.data.detected[gameId];
    settings.save();
  }

  const known = options.forget ? null : knownPathFor(gameId);
  const manual = Boolean(settings.data.gamePaths[gameId]);
  const state = await games.inspect(game, known, onProgress, {
    ...options,
    manual,
  });

  return { game, state: rememberPath(gameId, state) };
}

function registryFor(gameId, state) {
  if (!state.found) return null;
  const key = `${gameId}:${state.path}`;
  if (!registries.has(key)) {
    registries.set(
      key,
      new ModRegistry(dataDir(), gameId, {
        modsDir: state.modsDir,
        storageDir: path.join(state.path, 'ModHub'),
      })
    );
  }
  return registries.get(key);
}

function sendProgress(payload) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('progress', payload);
  }
}

/**
 * Единая обёртка: ошибки доезжают до интерфейса текстом, а не тишиной.
 *
 * Системные ошибки доступа переводятся в человеческое объяснение здесь, в одном
 * месте: «EPERM: operation not permitted, copyfile …» ничего не говорит о том,
 * что делать, а «нет прав на запись в папку игры, нужен перезапуск от имени
 * администратора» — говорит.
 */
function handle(channel, fn) {
  ipcMain.handle(channel, async (_event, payload) => {
    try {
      return { ok: true, data: await fn(payload) };
    } catch (error) {
      console.error(`[${channel}]`, error);

      // Подробности, по которым интерфейс может предложить выход:
      // запустить установщик руками или открыть журнал.
      const extra = {
        needsKey: error.code === 'NEXUS_KEY',
        fallback: error.loaderFallback ?? null,
        manualPath: error.manualPath ?? null,
        logFile: error.logFile ?? null,
      };

      // Загрузчик уже перевёл ошибку на человеческий — не переводим второй раз.
      if (error.alreadyExplained) {
        return {
          ok: false,
          error: error.message,
          needsElevation: error.code === 'EPERM' || error.code === 'EACCES',
          ...extra,
        };
      }

      const explained = explainAccessError(error);
      if (explained) {
        return { ok: false, error: explained, needsElevation: error.code !== 'EBUSY', ...extra };
      }
      return { ok: false, error: error.message ?? String(error), ...extra };
    }
  });
}

/* ------------------------------------------------------------------ *
 *  IPC
 * ------------------------------------------------------------------ */

function registerIpc() {
  handle('app:info', async () => ({
    version: app.getVersion(),
    platform: process.platform,
    dataDir: dataDir(),
    logsDir: path.join(dataDir(), 'logs'),
    isDefaultProtocolClient: app.isDefaultProtocolClient('nxm'),
    elevated: isElevated(),
    language: getLang(),
  }));

  handle('app:openExternal', async (url) => {
    if (!/^https?:\/\//i.test(url)) throw new Error(t('err.linkRejected'));
    await shell.openExternal(url);
    return true;
  });

  handle('app:openFolder', async (target) => {
    if (!target || !fs.existsSync(target)) throw new Error(t('err.folderMissing'));
    await shell.openPath(target);
    return true;
  });

  /**
   * Запуск установщика загрузчика вручную.
   *
   * Когда тихая установка не удалась, лучшее, что можно сделать, — отдать
   * человеку тот же установщик в обычном окне: он увидит вопрос, на который
   * программа не смогла ответить за него, и ответит сам.
   */
  handle('app:runManual', async (target) => {
    if (!target || !fs.existsSync(target)) throw new Error(t('err.folderMissing'));
    const result = await shell.openPath(target);
    if (result) throw new Error(result);
    return true;
  });

  /**
   * Перезапуск с правами администратора.
   *
   * Повысить права текущему процессу нельзя — запускаем новый и закрываем себя.
   * Здесь легко получить худший из возможных результатов: программа закрылась,
   * новая не открылась, объяснений нет. Именно так и было раньше, и вот почему.
   *
   * Замок единственного экземпляра. Пока мы живы, мы его держим. Новый
   * экземпляр — уже с правами администратора — стартует, видит занятый замок,
   * решает, что ModHub уже открыт, и молча закрывается. Через полторы секунды
   * закрываемся и мы. Со стороны: нажал кнопку, всё исчезло.
   * Поэтому замок отдаём ДО запуска нового процесса.
   *
   * Момент выхода. Уйти сразу нельзя: человек ещё не ответил на запрос
   * Windows, и если он откажется, мы закроем единственную работающую копию.
   * Поэтому ждём рукопожатия: новый экземпляр отмечается файлом, и только
   * тогда мы уходим. Не дождались за две минуты — забираем замок обратно
   * и продолжаем работать как обычно.
   */
  handle('app:relaunchAsAdmin', async () => {
    const marker = elevationMarker();
    try {
      fs.rmSync(marker, { force: true });
    } catch {
      /* файла и так нет */
    }

    if (typeof app.releaseSingleInstanceLock === 'function') app.releaseSingleInstanceLock();

    try {
      await relaunchElevated({
        execPath: process.execPath,
        // В режиме разработки запускается electron.exe, и ему нужен путь к проекту.
        args: process.defaultApp && process.argv[1] ? [path.resolve(process.argv[1])] : [],
      });
    } catch (error) {
      app.requestSingleInstanceLock();
      throw new Error(t('err.elevateFailed', { reason: error?.message ?? String(error) }));
    }

    if (mainWindow && !mainWindow.isDestroyed()) mainWindow.hide();

    const startedAt = Date.now();
    const waiting = setInterval(() => {
      if (fs.existsSync(marker)) {
        clearInterval(waiting);
        app.exit(0);
        return;
      }
      if (Date.now() - startedAt < 120000) return;

      // Подтверждения так и не было: скорее всего человек нажал «Нет».
      clearInterval(waiting);
      app.requestSingleInstanceLock();
      if (mainWindow && !mainWindow.isDestroyed()) {
        mainWindow.show();
        mainWindow.webContents.send('elevation-cancelled');
      }
    }, 400);

    return true;
  });

  handle('app:pickFolder', async () => {
    const result = await dialog.showOpenDialog(mainWindow, {
      title: t('dialog.pickGame'),
      properties: ['openDirectory'],
    });
    return result.canceled ? null : result.filePaths[0];
  });

  handle('app:pickArchive', async () => {
    const result = await dialog.showOpenDialog(mainWindow, {
      title: t('dialog.pickArchive'),
      properties: ['openFile', 'multiSelections'],
      filters: [{ name: t('dialog.archives'), extensions: ['zip', '7z', 'rar'] }],
    });
    return result.canceled ? [] : result.filePaths;
  });

  /* --- настройки --- */

  handle('settings:read', async () => ({ ...settings.data, nexusApiKey: settings.data.nexusApiKey ? '••••' : '' }));

  handle('settings:write', async (patch) => {
    for (const [key, value] of Object.entries(patch ?? {})) {
      if (key === 'nexusApiKey' && value === '••••') continue;
      settings.data[key] = value;
    }
    settings.save();
    // Язык меняется на лету: сообщения основного процесса должны говорить
    // на том же языке, что и кнопки вокруг них, уже со следующего ответа.
    if (patch && 'language' in patch) setLang(settings.data.language);
    return true;
  });

  handle('settings:checkNexusKey', async (key) => {
    const info = await nexus.validateKey(key || settings.data.nexusApiKey);
    if (key) settings.data.nexusApiKey = key;
    // Премиум решает, как ставить моды Stardew: в один клик или через сайт.
    settings.data.nexusPremium = Boolean(info.premium);
    settings.save();
    return info;
  });

  /* --- игры --- */

  /**
   * Список игр без единого обращения к дискам.
   *
   * Окно рисуется сразу: известные пути отдаются мгновенно, остальные игры
   * приходят с пометкой «ещё не искали». Поиск начинает интерфейс, по одной
   * игре, и показывает его ход. Раньше этот вызов запускал три полных обхода
   * дисков подряд, и до первой отрисовки проходили десятки секунд.
   */
  handle('games:list', async () => {
    const out = [];
    for (const game of games.all()) {
      const { state } = await stateFor(game.id, undefined, { scan: false });
      out.push(state);
    }
    return out;
  });

  handle('games:supported', async () => games.supported());

  handle('games:inspect', async (payload) => {
    const gameId = typeof payload === 'string' ? payload : payload.gameId;
    const options = typeof payload === 'string' ? {} : payload;

    // Поиск может занять секунды — рассказываем о ходе дела, а не молчим.
    const { state } = await stateFor(
      gameId,
      (stage) => sendProgress({ scope: 'search', gameId, ...stage }),
      { deep: Boolean(options.deep), forget: Boolean(options.forget) }
    );
    return state;
  });

  handle('games:setPath', async ({ gameId, path: candidate }) => {
    const game = games.byId(gameId);
    if (!game) throw new Error(t('err.unknownGame', { id: gameId }));

    // Пустое значение — «забудь мой выбор и поищи сам заново».
    if (!candidate) {
      delete settings.data.gamePaths[gameId];
      settings.save();
      registries.clear();
      return (await stateFor(gameId, undefined, { forget: true })).state;
    }

    const check = games.validatePath(game, candidate);
    if (!check.ok) throw new Error(check.reason);
    settings.data.gamePaths[gameId] = candidate;
    settings.save();
    registries.clear();
    return (await stateFor(gameId, undefined, { scan: false })).state;
  });

  handle('games:installLoader', async (gameId) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    if (!state.found) throw new Error(t('err.pickGameFolder'));

    const loader = games.loaderFor(game);
    await loader.install(
      { ...game, path: state.path },
      (stage) => sendProgress({ scope: 'loader', gameId, ...stage }),
      { dataDir: dataDir(), logFile: logFileFor(`${game.loader.kind}-install`) }
    );

    return (await stateFor(gameId, undefined, { scan: false })).state;
  });

  handle('games:launch', async (gameId) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    if (!state.found) throw new Error(t('err.gameNotFound'));
    // Резервная копия сохранений перед запуском — если это не выключено.
    // Не получилась — игру всё равно запускаем: копия не должна мешать играть.
    let backup = null;
    if (settings.data.backupOnLaunch !== false) {
      try {
        backup = backups.create(gameId, savesDirFor(game, state), 'launch', { keep: backupKeep() });
      } catch (error) {
        console.error('[backup]', error);
      }
    }

    const track = settings.data.trackPlaytime !== false;
    const launched = diagnostics.launchGame(game, state.path, {
      extraArgs: settings.data.launchArgs?.[gameId] ?? '',
      onExit: () => {
        const session = track ? playtime.stop(gameId) : { counted: false, ms: 0 };
        if (mainWindow && !mainWindow.isDestroyed()) {
          // Игру закрыли — вернуть окно ModHub, если его сворачивали.
          if (settings.data.afterLaunch === 'minimize' && settings.data.restoreAfterGame !== false && mainWindow.isMinimized()) {
            mainWindow.restore();
          }
          mainWindow.webContents.send('game-exit', { gameId, ...session, playtime: playtime.all() });
        }
      },
    });
    if (track) playtime.start(gameId);
    if (settings.data.afterLaunch === 'minimize' && mainWindow && !mainWindow.isDestroyed()) mainWindow.minimize();
    // Всё, что сейчас стоит и включено, поехало в игру вместе с ней:
    // такие моды теперь можно оценить.
    let played = [];
    try {
      const registry = registryFor(gameId, state);
      if (registry) played = playlog.noteLaunch(game, registry.reconcile());
    } catch (error) {
      console.error('[playlog]', error);
    }
    return { ...launched, played, backup };
  });

  /* --- моды --- */

  handle('mods:list', async (gameId) => {
    const { state } = await stateFor(gameId, undefined, { scan: false });
    const registry = registryFor(gameId, state);
    if (!registry) return { mods: [], unmanaged: [] };
    return { mods: registry.reconcile(), unmanaged: registry.findUnmanaged() };
  });

  handle('mods:setEnabled', async ({ gameId, modId, enabled }) => {
    const { state } = await stateFor(gameId, undefined, { scan: false });
    const registry = registryFor(gameId, state);
    return registry.setEnabled(modId, enabled);
  });

  handle('mods:remove', async ({ gameId, modId }) => {
    const { state } = await stateFor(gameId, undefined, { scan: false });
    const registry = registryFor(gameId, state);
    return registry.remove(modId);
  });

  handle('mods:installFromCatalog', async ({ gameId, modId }) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    if (!state.loader.installed) {
      throw new Error(t('err.installLoaderFirst', { loader: game.loader.name }));
    }
    const registry = registryFor(gameId, state);
    if (game.catalog.kind === 'nexus') {
      return installFromNexus(game, state, registry, modId, gameId);
    }
    const mod = await lookupCatalogMod(game, modId);
    if (!mod) throw new Error(t('err.modNotInCatalog'));

    return install.installFromCatalog({ game, state, registry }, mod, (stage) =>
      sendProgress({ scope: 'install', gameId, ...stage })
    );
  });

  handle('mods:installFromFile', async ({ gameId, filePath }) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    if (!state.loader.installed) {
      throw new Error(t('err.installLoaderFirst', { loader: game.loader.name }));
    }
    const registry = registryFor(gameId, state);

    const paths = Array.isArray(filePath) ? filePath : [filePath];
    const results = [];
    for (const [index, file] of paths.entries()) {
      sendProgress({
        scope: 'install',
        gameId,
        code: 'install.extract',
        mod: path.basename(file),
        index: index + 1,
        total: paths.length,
      });
      results.push(install.installArchive({ game, state, registry }, file, { source: 'file' }));
    }
    sendProgress({ scope: 'install', gameId, code: 'install.done', installed: results.length });
    return results;
  });

  handle('mods:problems', async (gameId) => {
    const { state } = await stateFor(gameId, undefined, { scan: false });
    const registry = registryFor(gameId, state);
    if (!registry) return [];
    return install.checkDependencies(registry);
  });

  /**
   * Картинки установленных модов для вкладки «Загрузки».
   * С 1.7 картинка запоминается при установке; для модов, поставленных
   * раньше, её один раз находим в каталоге и держим в media-cache.json.
   */
  handle('mods:media', async (gameId) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    const registry = registryFor(gameId, state);
    if (!registry) return {};

    const out = {};
    const todo = [];
    const items = installedMedia.data.items;
    const WEEK = 7 * 24 * 60 * 60 * 1000;

    for (const mod of registry.list()) {
      if (mod.icon) {
        out[mod.id] = mod.icon;
        continue;
      }
      if (game.catalog.kind === 'modlinks') {
        const cover = hkmedia.lookup(mod.id).cover;
        if (cover) out[mod.id] = cover;
        continue;
      }
      const cached = items[`${gameId}:${mod.id}`];
      if (cached && (cached.url || Date.now() - Date.parse(cached.at) < WEEK)) {
        if (cached.url) out[mod.id] = cached.url;
        continue;
      }
      todo.push(mod);
    }

    let changed = false;
    for (const mod of todo.slice(0, 12)) {
      let url = null;
      try {
        if (game.catalog.kind === 'thunderstore' && thunderstore.splitId(mod.id)) {
          url = (await thunderstore.getById(game.catalog.community, mod.id))?.icon ?? null;
        } else if (game.catalog.kind === 'nexus') {
          const match = /^nexus:[^:]+:(\d+)$/.exec(mod.id);
          if (match) {
            const details = await nexus.getDetails(game.catalog.nexusDomain, game.catalog.nexusGameId, match[1]);
            url = details?.mod?.icon ?? null;
          }
        }
      } catch {
        continue; // нет сети — попробуем в следующий раз
      }
      items[`${gameId}:${mod.id}`] = { url, at: new Date().toISOString() };
      changed = true;
      if (url) out[mod.id] = url;
    }
    if (changed) {
      try {
        installedMedia.save();
      } catch {
        /* не записалось — найдём снова в следующий раз */
      }
    }
    return out;
  });

  /* --- каталог --- */

  /**
   * Страница каталога. Ответ у всех источников один: { mods, total, hasMore, page }.
   * Ошибка сети больше не превращается в пустой список — она уходит в окно,
   * и человек видит причину, а не «ничего нет».
   */
  handle('catalog:search', async ({ gameId, query, options }) => {
    const { game } = await stateFor(gameId, undefined, { scan: false });
    const opts = options ?? {};
    const page = Math.max(1, Number(opts.page) || 1);
    let result = { mods: [], total: 0, hasMore: false, page };
    // Раздел каталога (2.0): «Постройки», «Графика»… — свой фильтр у каждого сайта.
    const section = sectionOf(game, opts.section);

    if (game.catalog.kind === 'thunderstore') {
      result = await thunderstore.search(game.catalog.community, query ?? '', { ...opts, page, categories: section.thunderstore });
    } else if (game.catalog.kind === 'modlinks') {
      // Категории есть только у ModLinks; «по алфавиту» — тоже здесь, список целиком на диске.
      let all = await modlinks.search(query ?? '', { category: opts.category || undefined, categories: section.modlinks });
      if (opts.sort === 'name') all = [...all].sort((a, b) => a.name.localeCompare(b.name, 'ru'));
      const size = 24;
      result = { mods: all.slice((page - 1) * size, page * size), total: all.length, hasMore: page * size < all.length, page };
    } else if (game.catalog.kind === 'nexus') {
      result = await nexus.browse(game.catalog.nexusDomain, {
        ...opts,
        page,
        query,
        hide: game.catalog.hide,
        categories: section.nexus,
      });
    }

    if (opts.limit) result = { ...result, mods: result.mods.slice(0, opts.limit) };
    return result;
  });

  /** Всё для страницы мода: полная карточка, описание, требования. */
  handle('catalog:details', async ({ gameId, modId }) => {
    const { game } = await stateFor(gameId, undefined, { scan: false });
    return catalogDetails(game, modId);
  });

  handle('catalog:categories', async (gameId) => {
    const { game } = await stateFor(gameId, undefined, { scan: false });
    if (game.catalog.kind === 'thunderstore') return thunderstore.categories(game.catalog.community);
    if (game.catalog.kind === 'modlinks') return modlinks.categories();
    return [];
  });

  /**
   * Несколько модов по номерам — для «Нужных модов» и готовых наборов (2.0).
   * По одному запросу на мод, не больше четырёх разом; пропавший из
   * каталога мод просто не попадает в ответ.
   */
  handle('catalog:many', async ({ gameId, ids }) => {
    const { game } = await stateFor(gameId, undefined, { scan: false });
    const list = (Array.isArray(ids) ? ids : []).map(String).slice(0, 40);
    const out = new Array(list.length).fill(null);
    let next = 0;
    const worker = async () => {
      while (next < list.length) {
        const index = next++;
        try {
          out[index] = await lookupCatalogMod(game, list[index]);
        } catch {
          out[index] = null;
        }
      }
    };
    await Promise.all([worker(), worker(), worker(), worker()]);
    return out.filter(Boolean);
  });

  handle('catalog:get', async ({ gameId, modId }) => {
    const { game } = await stateFor(gameId, undefined, { scan: false });
    return lookupCatalogMod(game, modId);
  });

  /**
   * Картинки модов, которых нет во встроенном указателе (Hollow Knight):
   * программа сама читает их README и отдаёт найденное окну.
   */
  handle('catalog:media', async ({ gameId, ids }) => {
    const { game } = await stateFor(gameId, undefined, { scan: false });
    if (game.catalog.kind !== 'modlinks') return {};
    return modlinks.media(Array.isArray(ids) ? ids.slice(0, 48) : []);
  });

  /* --- отзывы и оценки --- */

  /** Реклама в шапке: лента из ads.config.json или пусто — тогда свои объявления. */
  handle('ads:list', async () => ads.list());

  /** Настоящие картинки игр: арт, логотип, скриншоты — адреса на серверах Steam. */
  handle('media:games', async () => steamMedia.forGames(games.all()));

  /**
   * «Очистить кэш картинок» в настройках: браузерный кэш окна и сохранённые
   * списки картинок. Настройки, моды и отзывы не трогаются.
   */
  handle('app:clearCache', async () => {
    const { session } = require('electron');
    await session.defaultSession.clearCache();
    steamMedia.clear();
    for (const file of ['ads-cache.json', 'media-cache.json']) {
      try {
        fs.rmSync(path.join(dataDir(), file), { force: true });
      } catch {
        /* не удалось — не страшно */
      }
    }
    return true;
  });

  handle('reviews:status', async () => reviews.status());

  /** Забрать новое с сервера; отвечает свежими средними оценками. */
  handle('reviews:sync', async (payload) =>
    reviewsCall(async () => {
      await reviews.sync({ force: Boolean(payload?.force) });
      return { status: reviews.status(), stats: reviews.stats() };
    })
  );

  handle('reviews:stats', async () => reviews.stats());

  /**
   * Отзывы одного мода. Сначала подтягиваем новое с сервера, но если сети
   * нет — всё равно отдаём то, что знаем, с пометкой об ошибке.
   */
  handle('reviews:list', async ({ gameId, modId }) => {
    let error = null;
    let errorCode = null;
    if (reviews.configured) {
      try {
        await reviews.sync({ maxAgeMs: 15_000 });
      } catch (failure) {
        error = explainReviews(failure).message;
        errorCode = failure?.code ?? 'SERVER';
      }
    }
    const stats = reviews.stats();
    return {
      configured: reviews.configured,
      error,
      errorCode,
      reviews: reviews.forMod(gameId, modId),
      stats: stats[statsKey(gameId, modId)] ?? { avg: 0, count: 0 },
      name: settings.data.reviewName ?? '',
      all: stats,
      gate: await rateGate(gameId, modId),
    };
  });

  /** Только пропуск к оценке — окно спрашивает, когда человек вернулся из игры. */
  handle('reviews:gate', async ({ gameId, modId }) => rateGate(gameId, modId));

  handle('reviews:submit', async (input) =>
    reviewsCall(async () => {
      // Оценить можно только то, что скачал и попробовал в игре. Проверка
      // здесь, а не только в окне: кнопку в окне можно обойти, этот вызов — нет.
      const gate = await rateGate(input?.gameId, input?.modId);
      if (!gate.ok) throw new ReviewsError(gate.installed ? 'NOT_PLAYED' : 'NOT_INSTALLED');

      // Вошли в аккаунт — отзыв подписан именем аккаунта, а не полем формы.
      const profile = account.profile();
      const name = profile.signedIn && profile.name ? profile.name : String(input?.name ?? '').trim().slice(0, 32);
      if (!profile.signedIn && name && name !== settings.data.reviewName) {
        settings.data.reviewName = name;
        settings.save();
      }
      await reviews.submit({
        game: input?.gameId,
        mod: input?.modId,
        modName: input?.modName,
        stars: input?.stars,
        text: input?.text,
        name,
        played: gate.played,
      });
      const stats = reviews.stats();
      return {
        reviews: reviews.forMod(input.gameId, input.modId),
        stats: stats[statsKey(input.gameId, input.modId)] ?? { avg: 0, count: 0 },
        all: stats,
        gate: await rateGate(input.gameId, input.modId),
      };
    })
  );

  handle('reviews:remove', async ({ gameId, modId }) =>
    reviewsCall(async () => {
      await reviews.remove({ game: gameId, mod: modId });
      const stats = reviews.stats();
      return {
        reviews: reviews.forMod(gameId, modId),
        stats: stats[statsKey(gameId, modId)] ?? { avg: 0, count: 0 },
        all: stats,
        // Удалил свой отзыв без игры с модом — новый напишется только после игры.
        gate: await rateGate(gameId, modId),
      };
    })
  );

  /** Администратор скрывает чужой отзыв. */
  handle('reviews:moderate', async ({ gameId, modId, reviewId }) =>
    reviewsCall(async () => {
      if (!account.profile().admin) throw new ReviewsError('DENIED', 'not admin');
      await reviews.moderate(reviewId);
      const stats = reviews.stats();
      return {
        reviews: reviews.forMod(gameId, modId),
        stats: stats[statsKey(gameId, modId)] ?? { avg: 0, count: 0 },
        all: stats,
      };
    })
  );

  /* --- аккаунт --- */

  handle('account:get', async () => account.profile());

  /** Свежие данные с сервера: подтверждена ли почта, имя. Без сети — что есть. */
  handle('account:refresh', async () => account.refresh().catch(() => account.profile()));

  handle('account:signUp', async (input) =>
    accountCall(async () => {
      const profile = await account.signUp(input);
      reviews.sync({ force: true }).catch(() => {});
      return profile;
    })
  );

  handle('account:signIn', async (input) =>
    accountCall(async () => {
      const profile = await account.signIn(input);
      reviews.sync({ force: true }).catch(() => {});
      return profile;
    })
  );

  handle('account:signOut', async () => account.signOut());

  handle('account:rename', async ({ name }) => accountCall(() => account.rename(name)));

  handle('account:verify', async () => accountCall(() => account.sendVerification()));

  handle('account:reset', async ({ email } = {}) => accountCall(() => account.resetPassword(email)));

  /** Удалить аккаунт: сперва его отзывы (пометкой «удалён»), потом сам аккаунт. */
  handle('account:delete', async ({ password, withReviews = true } = {}) =>
    accountCall(async () => {
      if (!account.signedIn) throw new AccountError('SESSION');
      // Сначала проверяем пароль — чтобы не стереть отзывы при опечатке.
      await account.request(account.identity('accounts:signInWithPassword'), {
        email: account.profile().email,
        password: String(password ?? ''),
        returnSecureToken: true,
      });
      if (withReviews) {
        for (const review of reviews.own()) {
          await reviews.remove({ game: review.game, mod: review.mod }).catch(() => {});
        }
      }
      return account.deleteAccount(password);
    })
  );

  /* --- профили модов (1.10) --- */

  handle('profiles:list', async (gameId) => profiles.list(gameId));

  handle('profiles:save', async ({ gameId, name }) =>
    coded(async () => {
      const { state } = await stateFor(gameId, undefined, { scan: false });
      const registry = registryFor(gameId, state);
      if (!registry) throw new Error(t('err.gameNotFound'));
      profiles.save(gameId, name, registry.reconcile());
      return profiles.list(gameId);
    })
  );

  handle('profiles:apply', async ({ gameId, name }) =>
    coded(async () => {
      const { state } = await stateFor(gameId, undefined, { scan: false });
      const registry = registryFor(gameId, state);
      if (!registry) throw new Error(t('err.gameNotFound'));
      registry.reconcile();
      const result = profiles.apply(gameId, name, registry);
      return { ...result, profiles: profiles.list(gameId) };
    })
  );

  handle('profiles:rename', async ({ gameId, from, to }) => {
    profiles.rename(gameId, from, to);
    return profiles.list(gameId);
  });

  handle('profiles:remove', async ({ gameId, name }) => {
    profiles.remove(gameId, name);
    return profiles.list(gameId);
  });

  /* --- сборка в файле: экспорт и импорт (1.10) --- */

  handle('pack:export', async ({ gameId, name }) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    const registry = registryFor(gameId, state);
    if (!registry) throw new Error(t('err.gameNotFound'));
    const pack = buildPack(game, registry.reconcile(), { name, app: app.getVersion() });
    const safe = String(pack.name).replace(/[<>:"/\\|?*\x00-\x1f]/g, '').trim() || game.id;
    const result = await dialog.showSaveDialog(mainWindow, {
      title: t('dialog.packExport'),
      defaultPath: path.join(app.getPath('documents'), `${safe}.modhub.json`),
      filters: [{ name: t('dialog.pack'), extensions: ['json'] }],
    });
    if (result.canceled || !result.filePath) return null;
    fs.writeFileSync(result.filePath, JSON.stringify(pack, null, 2), 'utf8');
    return { file: result.filePath, count: pack.mods.length };
  });

  /** Прочитать файл сборки и сказать, что в нём есть и чего нет у вас. */
  handle('pack:import', async () =>
    coded(async () => {
      const result = await dialog.showOpenDialog(mainWindow, {
        title: t('dialog.packImport'),
        properties: ['openFile'],
        filters: [{ name: t('dialog.pack'), extensions: ['json'] }],
      });
      if (result.canceled || !result.filePaths[0]) return null;
      const stat = fs.statSync(result.filePaths[0]);
      if (stat.size > 2 * 1024 * 1024) throw Object.assign(new Error('too big'), { code: 'PACK_FORMAT' });
      const pack = parsePack(fs.readFileSync(result.filePaths[0], 'utf8'));
      const game = games.byId(pack.game);
      if (!game) throw Object.assign(new Error('unknown game'), { code: 'PACK_GAME' });

      let installed = new Set();
      try {
        const { state } = await stateFor(pack.game, undefined, { scan: false });
        const registry = registryFor(pack.game, state);
        if (registry) installed = new Set(registry.reconcile().map((r) => r.id));
      } catch {
        /* игры нет — значит, ничего и не стоит */
      }
      const mods = pack.mods.map((m) => {
        const nexusId = /^nexus:[^:]+:(\d+)$/.exec(m.id);
        // Номер для каталога: у Nexus в реестре он с приставкой.
        const catalogId = m.source === 'file' ? null : nexusId ? nexusId[1] : m.id;
        return { ...m, catalogId, installed: installed.has(m.id) };
      });
      return { ...pack, gameName: game.name, mods };
    })
  );

  /* --- обновления модов (1.10) --- */

  /**
   * Какие установленные моды обновились в каталоге.
   * Nexus отдаёт версию, но файл — только через сайт: такие обновления
   * помечаются manual, и кнопка ведёт на страницу мода.
   */
  handle('mods:updates', async (gameId) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    const registry = registryFor(gameId, state);
    if (!registry) return [];
    const out = [];
    const records = registry.reconcile().filter((r) => !r.missing && r.source !== 'file');
    await Promise.all(
      records.map(async (record) => {
        let latest = null;
        let catalogId = record.id;
        try {
          if (game.catalog.kind === 'nexus') {
            const match = /^nexus:[^:]+:(\d+)$/.exec(record.id);
            if (!match) return;
            catalogId = match[1];
            latest = (await lookupCatalogMod(game, catalogId))?.version ?? null;
          } else {
            latest = (await lookupCatalogMod(game, record.id))?.version ?? null;
          }
        } catch {
          return; // нет сети или мод убрали из каталога — просто не знаем
        }
        if (isNewer(latest, record.version)) {
          out.push({
            id: record.id,
            catalogId,
            name: record.name,
            current: record.version,
            latest,
            icon: record.icon ?? null,
            manual: game.catalog.kind === 'nexus',
          });
        }
      })
    );
    return out.sort((a, b) => a.name.localeCompare(b.name, 'ru'));
  });

  /** Поставить новую версию поверх старой (Thunderstore и ModLinks). */
  handle('mods:update', async ({ gameId, modId }) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    if (game.catalog.kind === 'nexus') throw new Error(t('err.updateManual'));
    const registry = registryFor(gameId, state);
    const old = registry?.get(modId);
    if (!old) throw new Error(t('err.modNotFound', { id: modId }));
    const mod = await lookupCatalogMod(game, modId);
    if (!mod) throw new Error(t('err.modNotInCatalog'));

    // Новая версия ставится в Mods: выключенный мод сперва возвращаем на место.
    const wasEnabled = old.enabled !== false;
    if (!wasEnabled) registry.setEnabled(modId, true);
    const oldFolder = registry.folderFor(registry.get(modId));

    let result;
    try {
      result = await install.installFromCatalog(
        { game, state, registry },
        mod,
        (stage) => sendProgress({ scope: 'install', gameId, ...stage }),
        { reinstall: new Set([modId]) }
      );
      // Новая версия легла в другую папку — старую убираем, иначе загрузятся обе.
      const fresh = registry.get(modId);
      if (fresh && registry.folderFor(fresh) !== oldFolder && fs.existsSync(oldFolder)) {
        fs.rmSync(oldFolder, { recursive: true, force: true });
      }
    } finally {
      // Мод был выключен — таким и остаётся, с новой версией или со старой.
      if (!wasEnabled && registry.has(modId)) registry.setEnabled(modId, false);
    }
    return result;
  });

  /* --- резервные копии сохранений (1.10) --- */

  handle('backups:list', async (gameId) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    const savesDir = state.found ? savesDirFor(game, state) : null;
    return {
      supported: typeof game.savesDir === 'function',
      saves: backups.describe(savesDir),
      folder: backups.folderFor(gameId),
      items: backups.list(gameId),
    };
  });

  handle('backups:create', async (gameId) =>
    coded(async () => {
      const { game, state } = await stateFor(gameId, undefined, { scan: false });
      const savesDir = state.found ? savesDirFor(game, state) : null;
      const made = backups.create(gameId, savesDir, 'manual', { keep: backupKeep() });
      if (!made) throw Object.assign(new Error('no saves'), { code: 'BACKUP_NO_SAVES' });
      return made;
    })
  );

  handle('backups:restore', async ({ gameId, name }) =>
    coded(async () => {
      if (playtime.isRunning(gameId)) throw Object.assign(new Error('game running'), { code: 'BACKUP_RUNNING' });
      const { game, state } = await stateFor(gameId, undefined, { scan: false });
      if (!state.found) throw new Error(t('err.gameNotFound'));
      return backups.restore(gameId, name, savesDirFor(game, state), { keep: backupKeep() });
    })
  );

  handle('backups:remove', async ({ gameId, name }) => coded(async () => backups.remove(gameId, name)));

  /** Все игры сразу — для раздела настроек. */
  handle('backups:summary', async () => {
    const out = {};
    for (const game of games.all()) {
      const items = backups.list(game.id);
      out[game.id] = { count: items.length, last: items[0]?.at ?? null, bytes: items.reduce((n, b) => n + b.size, 0) };
    }
    return { dir: path.join(dataDir(), 'backups'), games: out };
  });

  /* --- игровое время (1.10) --- */

  handle('playtime:all', async () => playtime.all());
  handle('playtime:reset', async (gameId) => {
    playtime.reset(gameId || null);
    return playtime.all();
  });

  /* --- диагностика --- */

  handle('diagnostics:issues', async (gameId) => {
    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    if (!state.found) return { available: false, issues: [] };
    return diagnostics.readIssues(game, state.path);
  });

  /* --- установка по ссылке nxm:// --- */

  handle('nxm:install', async ({ gameId, link }) => {
    const parsed = nexus.parseNxmLink(link);
    if (!parsed) throw new Error(t('err.linkUnparsed'));

    const { game, state } = await stateFor(gameId, undefined, { scan: false });
    if (!state.loader.installed) throw new Error(t('err.installLoaderFirst', { loader: game.loader.name }));

    const apiKey = settings.data.nexusApiKey;
    const registry = registryFor(gameId, state);

    sendProgress({ scope: 'install', gameId, code: 'install.nexus' });
    const [info, mirrors] = await Promise.all([
      nexus.getModInfo(parsed, apiKey),
      nexus.getDownloadLinks(parsed, apiKey),
    ]);

    sendProgress({ scope: 'install', gameId, code: 'install.download', mod: info.name });
    const archive = await downloadFile(mirrors[0].url, {
      fileName: info.fileName ?? 'mod.zip',
      onProgress: (p) =>
        sendProgress({ scope: 'install', gameId, code: 'install.download', mod: info.name, ratio: p.ratio, bytes: p.received, bytesTotal: p.total }),
    });

    sendProgress({ scope: 'install', gameId, code: 'install.extract', mod: info.name });
    const record = install.installArchive({ game, state, registry }, archive, {
      id: info.id,
      name: info.name,
      version: info.version,
      author: info.author,
      source: 'nexus',
      url: info.url,
      icon: info.icon ?? null,
    });

    fs.rmSync(archive, { force: true });
    sendProgress({ scope: 'install', gameId, code: 'install.done', mod: info.name, installed: 1 });
    // Если этот мод ждали из браузера — ожидание закончено.
    finishBrowserWait(`${gameId}:${parsed.modId}`, { record });
    return record;
  });

  /** Окно ожидания: «Отмена» и «Выбрать файл». */
  handle('mods:browserCancel', async ({ gameId, modId }) => finishBrowserWait(`${gameId}:${modId}`, { cancelled: true }));
  handle('mods:browserFile', async ({ gameId, modId, filePath }) =>
    finishBrowserWait(`${gameId}:${modId}`, { file: filePath })
  );

  /** Какая из поддерживаемых игр стоит за доменом в nxm-ссылке. */
  handle('nxm:describe', async (link) => {
    const parsed = nexus.parseNxmLink(link);
    if (!parsed) throw new Error(t('err.linkUnparsed'));
    const game = games.all().find((g) => g.catalog?.nexusDomain === parsed.game) ?? null;
    return { ...parsed, gameId: game?.id ?? null, gameName: game?.name ?? parsed.game };
  });

  handle('diagnostics:bisect', async ({ gameId, suspects, lastResult }) => {
    const { state } = await stateFor(gameId, undefined, { scan: false });
    const registry = registryFor(gameId, state);
    const step = diagnostics.bisectStep(suspects ?? [], lastResult ?? null);

    // Применяем предложенное состояние к реальным папкам.
    for (const modId of step.enable) {
      if (registry.has(modId)) registry.setEnabled(modId, true);
    }
    for (const modId of step.disable) {
      if (registry.has(modId)) registry.setEnabled(modId, false);
    }

    return step;
  });
}

async function lookupCatalogMod(game, modId) {
  if (game.catalog.kind === 'thunderstore') return thunderstore.getById(game.catalog.community, modId);
  if (game.catalog.kind === 'modlinks') return modlinks.getById(modId);
  if (game.catalog.kind === 'nexus') {
    const details = await nexus.getDetails(game.catalog.nexusDomain, game.catalog.nexusGameId, modId);
    return details?.mod ?? null;
  }
  return null;
}

async function catalogDetails(game, modId) {
  if (game.catalog.kind === 'thunderstore') {
    const [mod, readme] = await Promise.all([
      thunderstore.getById(game.catalog.community, modId),
      thunderstore.readme(modId).catch(() => null),
    ]);
    if (!mod) throw new Error(t('err.modNotInCatalog'));
    return { mod, readme, requirements: mod.requirements ?? [] };
  }
  if (game.catalog.kind === 'modlinks') {
    const mod = await modlinks.getById(modId);
    if (!mod) throw new Error(t('err.modNotInCatalog'));
    // README, счётчик загрузок с GitHub и картинки нового мода — параллельно;
    // без цифр и картинок страница всё равно откроется.
    const [readme, stats, media] = await Promise.all([
      modlinks.readme(mod).catch(() => null),
      modlinks.stats(mod).catch(() => null),
      mod.mediaPending ? modlinks.media([mod.id]).catch(() => ({})) : Promise.resolve({}),
    ]);
    const all = await modlinks.loadMods().catch(() => []);
    const byId = new Map(all.map((m) => [m.id, m]));
    const requirements = (mod.dependencies ?? []).map((id) => ({
      id,
      name: byId.get(id)?.name ?? id,
      icon: byId.get(id)?.icon ?? null,
      available: byId.has(id),
    }));
    const full = { ...mod, ...(media[mod.id] ?? {}) };
    return {
      mod: stats ? { ...full, downloads: stats.downloads, updatedAt: stats.updatedAt } : full,
      readme,
      requirements,
    };
  }
  if (game.catalog.kind === 'nexus') {
    const details = await nexus.getDetails(game.catalog.nexusDomain, game.catalog.nexusGameId, modId);
    if (!details) throw new Error(t('err.modNotInCatalog'));
    const hide = new Set(game.catalog.hide ?? []);
    return {
      mod: details.mod,
      readme: details.description
        ? { format: 'bbcode', content: details.description, base: 'https://www.nexusmods.com/' }
        : null,
      requirements: details.requirements.filter((r) => !hide.has(r.id)),
      mainFile: details.mainFile,
    };
  }
  return { mod: null, readme: null, requirements: [] };
}

/**
 * Ожидания файлов из браузера: ключ «игра:мод». Окно может отменить
 * ожидание или подсунуть файл руками; установка по nxm:// тоже закрывает
 * ожидание, если человек нажал на сайте «Mod Manager Download».
 */
const browserWaits = new Map();

function finishBrowserWait(key, outcome) {
  const wait = browserWaits.get(key);
  if (!wait) return false;
  browserWaits.delete(key);
  wait.settle(outcome);
  return true;
}

/**
 * Установка мода из каталога Nexus.
 *
 * Премиум с ключом — прямая ссылка и установка в один клик. Иначе открываем
 * на сайте страницу нужного файла и ждём архив в «Загрузках»: человек жмёт
 * там одну кнопку, остальное ModHub делает сам. Ключ для этого не нужен.
 * Так требуют правила Nexus: обход стоил бы пользователю аккаунта.
 */
async function installFromNexus(game, state, registry, modId, gameId) {
  const key = settings.data.nexusApiKey;
  const domain = game.catalog.nexusDomain;
  const details = await nexus.getDetails(domain, game.catalog.nexusGameId, modId);
  if (!details?.mod) throw new Error(t('err.modNotInCatalog'));
  const { mod, mainFile } = details;
  if (!mainFile) throw new Error(t('err.nexus.noFiles'));

  const meta = {
    id: `nexus:${domain}:${modId}`,
    name: mod.name,
    version: mainFile.version || mod.version,
    author: mod.author,
    source: 'nexus',
    url: mod.url,
    icon: mod.icon ?? null,
  };

  let archive;
  let downloaded = false;

  if (key && settings.data.nexusPremium) {
    sendProgress({ scope: 'install', gameId, code: 'install.download', mod: mod.name });
    const mirrors = await nexus.getDownloadLinks({ game: domain, modId, fileId: mainFile.fileId }, key);
    archive = await downloadFile(mirrors[0].url, {
      fileName: mainFile.fileName ?? 'mod.zip',
      onProgress: (p) =>
        sendProgress({ scope: 'install', gameId, code: 'install.download', mod: mod.name, ratio: p.ratio, bytes: p.received, bytesTotal: p.total }),
    });
    downloaded = true;
  } else {
    const waitKey = `${gameId}:${modId}`;
    finishBrowserWait(waitKey, { cancelled: true });
    const controller = new AbortController();
    const outcome = new Promise((resolve) => {
      browserWaits.set(waitKey, { settle: resolve, controller });
    });

    // Всегда обычная страница файла, не «Mod Manager Download»: ссылка nxm://
    // запускала бы второй ModHub с запросом прав администратора, а архив из
    // «Загрузок» мы подхватим и так. Кто нажмёт кнопку менеджера — тоже сработает.
    const pageUrl = nexus.filePageUrl(domain, modId, mainFile.fileId);
    await shell.openExternal(pageUrl);
    sendProgress({
      scope: 'install',
      gameId,
      code: 'install.browser',
      mod: mod.name,
      modId: String(modId),
      fileName: mainFile.fileName,
      url: pageUrl,
    });

    watchdl
      .waitForDownload({ dir: app.getPath('downloads'), match: watchdl.nexusMatcher(modId), signal: controller.signal })
      .then((file) => finishBrowserWait(waitKey, { file }))
      .catch((error) => {
        if (error.code === 'DL_TIMEOUT') finishBrowserWait(waitKey, { error: t('err.dl.timeout') });
      });

    const result = await outcome;
    controller.abort();
    if (result.cancelled) return { cancelled: true, installed: [], missing: [] };
    if (result.error) throw new Error(result.error);
    if (result.record) return { installed: [result.record], missing: [] }; // пришло по nxm://
    archive = result.file;
  }

  sendProgress({ scope: 'install', gameId, code: 'install.extract', mod: mod.name });
  try {
    const record = install.installArchive({ game, state, registry }, archive, meta);
    sendProgress({ scope: 'install', gameId, code: 'install.done', mod: mod.name, installed: 1 });
    return { installed: [record], missing: [] };
  } finally {
    // Скачанное нами — убираем; скачанное браузером остаётся у человека в «Загрузках».
    if (downloaded) fs.rmSync(archive, { force: true });
  }
}

/**
 * Видео с YouTube внутри окна.
 *
 * С лета 2025 года YouTube отказывается показывать встроенный ролик, если
 * запрос пришёл без адреса страницы (Referer) — это и есть «ошибка 153».
 * Окно ModHub открыто из файла, адреса у него нет, поэтому подставляем свой.
 */
function installVideoReferer() {
  const { session } = require('electron');
  const filter = {
    urls: [
      'https://www.youtube-nocookie.com/*',
      'https://www.youtube.com/*',
      'https://*.youtube.com/*',
      'https://*.ytimg.com/*',
      'https://*.googlevideo.com/*',
    ],
  };
  session.defaultSession.webRequest.onBeforeSendHeaders(filter, (details, callback) => {
    const headers = details.requestHeaders;
    if (!headers.Referer && !headers.referer) headers.Referer = 'https://modhub.app/';
    callback({ requestHeaders: headers });
  });
}

/* ------------------------------------------------------------------ *
 *  Жизненный цикл
 * ------------------------------------------------------------------ */

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Занимает замок единственного экземпляра.
 *
 * Обычный запуск: одна попытка. Если ModHub уже открыт — уступаем ему,
 * иначе nxm-ссылка уйдёт во второе окно, и человек получит два лаунчера
 * и ноль установок.
 *
 * Запуск ради повышения прав — случай особый. Мы стартуем в тот момент,
 * когда предыдущий экземпляр ещё дожидается ответа Windows и только
 * собирается закрыться. Сдаться сразу здесь означает не открыться вообще,
 * поэтому ждём до пятнадцати секунд.
 */
async function acquireInstanceLock() {
  if (app.requestSingleInstanceLock()) return true;
  if (!process.argv.includes(RELAUNCH_FLAG)) return false;

  for (let attempt = 0; attempt < 60; attempt += 1) {
    await delay(250);
    if (app.requestSingleInstanceLock()) return true;
  }
  return false;
}

async function bootstrap() {
  if (!(await acquireInstanceLock())) {
    app.quit();
    return;
  }

  app.on('second-instance', (_event, argv) => {
    deliverNxmLink(extractNxmLink(argv));
  });

  app.on('open-url', (event, url) => {
    event.preventDefault();
    deliverNxmLink(url);
  });

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit();
  });

  await app.whenReady();

  // Тот же идентификатор, что у ярлыков установщика: закреплённый значок
  // на панели задач и открытое окно — одна кнопка, а не две.
  // С 1.6.1 он новый: со старым окно цеплялось за ярлыки 1.0–1.4 и брало
  // у них значок-кубик. Только у собранной программы: запуск из исходников
  // иначе склеился бы с установленным ModHub.
  if (process.platform === 'win32' && app.isPackaged) app.setAppUserModelId(APP_ID);

  loadSettings();
  hkmedia.init(dataDir());
  startReviews();
  installVideoReferer();

  // Отмечаемся перед прошлым экземпляром: «дошёл, права получены, можешь
  // закрываться». Без этого он ждал бы нас все две минуты.
  if (process.argv.includes(RELAUNCH_FLAG)) {
    try {
      fs.mkdirSync(dataDir(), { recursive: true });
      fs.writeFileSync(elevationMarker(), new Date().toISOString());
    } catch {
      /* не смогли отметиться — прошлый экземпляр закроется по таймауту */
    }
  }

  registerProtocol();
  registerIpc();
  pendingNxmLink = extractNxmLink(process.argv);
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
}

// ModHub-Setup.exe и удаление из «Установленных приложений» открывают
// окно установщика; оттуда можно и просто запустить программу.
const setup = require('./setup');
const setupMode = setup.detectMode();
if (setupMode) setup.run(setupMode, { startApp: bootstrap });
else bootstrap();
