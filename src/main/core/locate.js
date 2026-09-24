'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const { findSteamRoot, listLibraryFolders, findSteamApp } = require('./steam');

const execFileAsync = promisify(execFile);

/**
 * Поиск игры на диске.
 *
 * Главный принцип: игра опознаётся по содержимому папки, а не по её имени.
 * Имя может быть любым — «Hollow Knight», «Hollow.Knight.v1.5.78», «HK репак»,
 * да хоть «новая папка». А вот содержимое у всех изданий одинаковое: у игр на
 * Unity рядом с исполняемым файлом всегда лежит папка «<имя exe>_Data», у
 * Stardew Valley — «Content» и «Stardew Valley.dll».
 *
 * Порядок от точного к широкому, и каждый способ сообщает о себе: человеку
 * важно видеть, откуда взялся путь, чтобы понять, тот ли он.
 *
 * Наружу уходят коды (`steam`, `scan`), а не готовые фразы: язык интерфейса
 * переключается на лету, и текст должен рождаться там, где он показывается.
 */

/** Известные источники пути. Подписи к ним живут в интерфейсе. */
const SOURCES = ['steam', 'steam-folder', 'epic', 'gog', 'scan', 'manual'];

/** Предел обхода: защита от папки с десятью тысячами подпапок. */
const MAX_DIRS_PER_ROOT = 400;
const MAX_TOTAL_DIRS = 6000;
/** Глубокий поиск человек запрашивает сам и готов подождать — лимиты выше. */
const DEEP_MAX_DIRS_PER_ROOT = 1200;
const DEEP_MAX_TOTAL_DIRS = 40000;

/**
 * Папки, внутрь которых лезть бессмысленно.
 *
 * Дело не только в скорости: системные деревья огромные, и обход C:\Windows
 * съедал бы весь лимит проверок до того, как очередь дойдёт до Program Files,
 * где игра и лежит. Пропуск этих папок — не оптимизация, а условие того,
 * что поиск вообще доходит до нужного места.
 */
const SKIP_DIRS = new Set([
  'windows',
  'windows.old',
  '$recycle.bin',
  'system volume information',
  'recovery',
  'perflogs',
  'appdata',
  'programdata',
  'node_modules',
  'msocache',
  'config.msi',
  'inetpub',
  'windowsapps',
  'packagecache',
]);

function isDir(candidate) {
  try {
    return fs.statSync(candidate).isDirectory();
  } catch {
    return false;
  }
}

function readDirNames(dir) {
  try {
    return fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return [];
  }
}

/** Буквы дисков, которые реально существуют. Проверка мгновенная. */
function drives() {
  if (process.platform !== 'win32') return ['/'];
  const found = [];
  for (let code = 'C'.charCodeAt(0); code <= 'Z'.charCodeAt(0); code++) {
    const letter = String.fromCharCode(code) + ':\\';
    if (isDir(letter)) found.push(String.fromCharCode(code) + ':');
  }
  return found;
}

/* ------------------------------------------------------------------ *
 *  Epic Games
 * ------------------------------------------------------------------ */

function findInEpic(game) {
  if (process.platform !== 'win32') return null;
  const manifestDir = path.join(
    process.env.PROGRAMDATA || 'C:\\ProgramData',
    'Epic',
    'EpicGamesLauncher',
    'Data',
    'Manifests'
  );
  return matchEpicManifests(manifestDir, game);
}

/** Отделено от findInEpic, чтобы поддаваться проверке тестом на любой системе. */
function matchEpicManifests(manifestDir, game) {
  if (!isDir(manifestDir)) return null;

  let entries;
  try {
    entries = fs.readdirSync(manifestDir).filter((f) => f.toLowerCase().endsWith('.item'));
  } catch {
    return null;
  }

  const wanted = [game.name, ...game.folderNames].map((n) => n.toLowerCase().replace(/\s+/g, ''));

  for (const entry of entries) {
    let manifest;
    try {
      manifest = JSON.parse(fs.readFileSync(path.join(manifestDir, entry), 'utf8'));
    } catch {
      continue;
    }

    const names = [manifest.DisplayName, manifest.InstallationGuid, manifest.MandatoryAppFolderName]
      .filter(Boolean)
      .map((n) => String(n).toLowerCase().replace(/\s+/g, ''));

    if (!names.some((n) => wanted.includes(n))) continue;
    if (manifest.InstallLocation && isDir(manifest.InstallLocation)) return manifest.InstallLocation;
  }

  return null;
}

/* ------------------------------------------------------------------ *
 *  GOG
 * ------------------------------------------------------------------ */

async function findInGog(game) {
  if (process.platform !== 'win32') return null;

  const hives = ['HKLM\\SOFTWARE\\WOW6432Node\\GOG.com\\Games', 'HKLM\\SOFTWARE\\GOG.com\\Games'];
  const wanted = [game.name, ...game.folderNames].map((n) => n.toLowerCase().replace(/\s+/g, ''));

  for (const hive of hives) {
    let stdout;
    try {
      ({ stdout } = await execFileAsync('reg', ['query', hive, '/s'], {
        windowsHide: true,
        timeout: 8000,
        maxBuffer: 4 * 1024 * 1024,
      }));
    } catch {
      continue;
    }

    for (const block of stdout.split(/\r?\n\r?\n/)) {
      const nameMatch = block.match(/gameName\s+REG_SZ\s+(.+)/i);
      const pathMatch = block.match(/\bpath\s+REG_SZ\s+(.+)/i);
      if (!nameMatch || !pathMatch) continue;
      if (!wanted.includes(nameMatch[1].trim().toLowerCase().replace(/\s+/g, ''))) continue;
      const candidate = pathMatch[1].trim();
      if (isDir(candidate)) return candidate;
    }
  }

  return null;
}

/* ------------------------------------------------------------------ *
 *  Обход дисков с опознаванием по содержимому
 * ------------------------------------------------------------------ */

/**
 * Проверяет, похожа ли папка на искомую игру.
 * Адаптер игры получает функцию has() — регистронезависимую проверку наличия
 * файла или папки внутри, потому что регистр у изданий разный, — и match(),
 * проверку по маске: у GOG рядом с игрой лежит goggame-<id>.info, и это
 * такой же надёжный признак, как папка данных Unity.
 */
function matchesSignature(game, dir) {
  const entries = readDirNames(dir);
  if (entries.length === 0) return false;

  const names = entries.map((e) => e.name);
  const lower = new Set(names.map((n) => n.toLowerCase()));
  const has = (name) => lower.has(String(name).toLowerCase());
  const match = (pattern) => names.some((n) => pattern.test(n));

  if (typeof game.signature === 'function') return Boolean(game.signature(dir, has, match));

  // Запасной вариант для адаптеров без подписи — по имени папки.
  return game.folderNames.some((n) => path.basename(dir).toLowerCase() === n.toLowerCase());
}

/**
 * Места, где вообще имеет смысл искать игры.
 *
 * @param {string[]} steamLibraries
 * @param {{deep?: boolean}} [options]
 */
function scanRoots(steamLibraries, options = {}) {
  const roots = [];
  const deep = Boolean(options.deep);

  for (const library of steamLibraries) {
    roots.push({ dir: path.join(library, 'steamapps', 'common'), depth: 1 });
  }

  const templates = [
    ['', deep ? 3 : 1], // корень диска: D:\Hollow Knight
    ['Games', deep ? 4 : 2],
    ['Игры', deep ? 4 : 2],
    ['Game', 2],
    ['SteamLibrary\\steamapps\\common', 1],
    ['Steam\\steamapps\\common', 1],
    // Репаки регулярно ставятся именно сюда, причём иногда ещё на уровень
    // глубже — C:\Program Files (x86)\Games\Hollow Knight.
    ['Program Files', deep ? 3 : 2],
    ['Program Files (x86)', deep ? 3 : 2],
    ['Program Files\\Steam\\steamapps\\common', 1],
    ['Program Files (x86)\\Steam\\steamapps\\common', 1],
    ['GOG Games', 2],
    ['GOG Galaxy\\Games', 2],
    ['Epic Games', 2],
    ['Program Files\\Epic Games', 2],
    ['Program Files (x86)\\Epic Games', 2],
    ['XboxGames', 2],
    ['Downloads', 2],
    ['Загрузки', 2],
    ['Torrents', 2],
    ['Торренты', 2],
    ['Repacks', 2],
    ['Репаки', 2],
    ['Igruha', 2],
  ];

  for (const drive of drives()) {
    for (const [suffix, depth] of templates) {
      const dir = process.platform === 'win32' ? (suffix ? `${drive}\\${suffix}` : `${drive}\\`) : drive;
      roots.push({ dir, depth });
    }
  }

  const home = os.homedir();
  for (const folder of [
    'Desktop',
    'Downloads',
    'Documents',
    'Games',
    'Рабочий стол',
    'Загрузки',
    'OneDrive\\Desktop',
    'OneDrive\\Рабочий стол',
  ]) {
    roots.push({ dir: path.join(home, folder), depth: 2 });
  }

  // Убираем дубликаты, сохраняя наибольшую глубину.
  const unique = new Map();
  for (const root of roots) {
    const key = root.dir.toLowerCase();
    if (!unique.has(key) || unique.get(key).depth < root.depth) unique.set(key, root);
  }
  return [...unique.values()];
}

/**
 * Обходит правдоподобные места и опознаёт игру по содержимому папок.
 *
 * @param {object} game
 * @param {string[]} steamLibraries
 * @param {(info: {checked: number, where: string}) => void} [onProgress]
 * @param {{deep?: boolean}} [options]
 * @returns {string|null}
 */
function scanForGame(game, steamLibraries, onProgress, options = {}) {
  const deep = Boolean(options.deep);
  const maxTotal = deep ? DEEP_MAX_TOTAL_DIRS : MAX_TOTAL_DIRS;
  const maxPerRoot = deep ? DEEP_MAX_DIRS_PER_ROOT : MAX_DIRS_PER_ROOT;
  let checked = 0;

  for (const root of scanRoots(steamLibraries, options)) {
    if (checked >= maxTotal) break;
    if (!isDir(root.dir)) continue;

    onProgress?.({ checked, where: root.dir });

    // Сама папка-корень тоже может оказаться игрой (C:\Games — вряд ли,
    // а вот D:\Hollow Knight, попавшая сюда как корень диска, — нет).
    const queue = [{ dir: root.dir, depth: 0 }];

    while (queue.length > 0) {
      const { dir, depth } = queue.shift();
      if (checked >= maxTotal) break;

      if (depth > 0) {
        checked++;
        if (matchesSignature(game, dir)) return dir;
      }

      if (depth >= root.depth) continue;

      const children = readDirNames(dir)
        .filter(
          (e) =>
            e.isDirectory() &&
            !e.name.startsWith('$') &&
            !e.name.startsWith('.') &&
            !SKIP_DIRS.has(e.name.toLowerCase())
        )
        .slice(0, maxPerRoot);

      for (const child of children) {
        queue.push({ dir: path.join(dir, child.name), depth: depth + 1 });
      }
    }
  }

  return null;
}

/* ------------------------------------------------------------------ *
 *  Общая точка входа
 * ------------------------------------------------------------------ */

/**
 * @param {object} game адаптер игры
 * @param {(stage: {code: string, where?: string, checked?: number}) => void} [onProgress]
 * @param {{deep?: boolean}} [options] deep — обход дисков вглубь, по кнопке «искать тщательнее»
 * @returns {Promise<{path: string, source: string}|null>}
 */
async function locateGame(game, onProgress, options = {}) {
  onProgress?.({ code: 'search.steam' });

  const viaSteam = await findSteamApp(game.steamAppId);
  if (viaSteam) return { path: viaSteam.path, source: 'steam' };

  const steamRoot = await findSteamRoot();
  const libraries = steamRoot ? listLibraryFolders(steamRoot) : [];

  for (const library of libraries) {
    for (const folderName of game.folderNames) {
      const candidate = path.join(library, 'steamapps', 'common', folderName);
      if (isDir(candidate)) return { path: candidate, source: 'steam-folder' };
    }
  }

  onProgress?.({ code: 'search.stores' });

  const viaEpic = findInEpic(game);
  if (viaEpic) return { path: viaEpic, source: 'epic' };

  const viaGog = await findInGog(game);
  if (viaGog) return { path: viaGog, source: 'gog' };

  const code = options.deep ? 'search.deep' : 'search.disks';
  onProgress?.({ code });

  const scanned = scanForGame(
    game,
    libraries,
    (info) => onProgress?.({ code, where: info.where, checked: info.checked }),
    options
  );
  if (scanned) return { path: scanned, source: 'scan' };

  return null;
}

module.exports = {
  locateGame,
  matchEpicManifests,
  matchesSignature,
  scanForGame,
  scanRoots,
  drives,
  SOURCES,
  SKIP_DIRS,
};
