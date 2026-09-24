'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const { parseVdf, getPath } = require('./vdf');

const execFileAsync = promisify(execFile);

/**
 * Поиск установленных игр Steam.
 *
 * Никакого перебора дисков: Steam сам хранит список своих библиотек в
 * steamapps/libraryfolders.vdf, а точную папку игры — в appmanifest_<appid>.acf.
 * Поэтому нестандартная установка (например, D:\Games) находится ровно так же
 * быстро, как обычная, и мы не читаем чужие диски без нужды.
 */

/** Реестровые ключи, где Windows-клиент Steam отмечает свой путь. */
const REGISTRY_KEYS = [
  { hive: 'HKCU\\Software\\Valve\\Steam', value: 'SteamPath' },
  { hive: 'HKLM\\SOFTWARE\\WOW6432Node\\Valve\\Steam', value: 'InstallPath' },
  { hive: 'HKLM\\SOFTWARE\\Valve\\Steam', value: 'InstallPath' },
];

async function readRegistryValue(hive, value) {
  try {
    const { stdout } = await execFileAsync(
      'reg',
      ['query', hive, '/v', value],
      { windowsHide: true, timeout: 5000 }
    );
    // Формат строки: "    SteamPath    REG_SZ    C:\Program Files (x86)\Steam"
    const match = stdout.match(/REG_[A-Z_]+\s+(.+?)\s*$/m);
    return match ? match[1].trim() : null;
  } catch {
    return null;
  }
}

/** Запасные пути на случай, если реестр недоступен или ключа нет. */
function fallbackSteamPaths() {
  const home = os.homedir();
  if (process.platform === 'win32') {
    const drives = ['C:', 'D:', 'E:'];
    const suffixes = ['\\Program Files (x86)\\Steam', '\\Program Files\\Steam', '\\Steam'];
    const out = [];
    for (const drive of drives) for (const suffix of suffixes) out.push(drive + suffix);
    return out;
  }
  if (process.platform === 'darwin') {
    return [path.join(home, 'Library/Application Support/Steam')];
  }
  return [
    path.join(home, '.steam/steam'),
    path.join(home, '.local/share/Steam'),
    path.join(home, '.var/app/com.valvesoftware.Steam/data/Steam'),
  ];
}

function isDir(p) {
  try {
    return fs.statSync(p).isDirectory();
  } catch {
    return false;
  }
}

/**
 * Корневая папка Steam или null, если клиент не установлен.
 * @returns {Promise<string|null>}
 */
async function findSteamRoot() {
  if (process.platform === 'win32') {
    for (const { hive, value } of REGISTRY_KEYS) {
      const found = await readRegistryValue(hive, value);
      if (found && isDir(found)) return found;
    }
  }
  for (const candidate of fallbackSteamPaths()) {
    if (isDir(candidate)) return candidate;
  }
  return null;
}

/**
 * Все библиотеки Steam — то есть все диски и папки, куда пользователь
 * когда-либо ставил игры.
 *
 * @param {string} steamRoot
 * @returns {string[]} пути вида "D:\SteamLibrary"
 */
function listLibraryFolders(steamRoot) {
  const libraries = new Set([steamRoot]);
  const vdfPath = path.join(steamRoot, 'steamapps', 'libraryfolders.vdf');

  let raw;
  try {
    raw = fs.readFileSync(vdfPath, 'utf8');
  } catch {
    return [...libraries];
  }

  const parsed = parseVdf(raw);
  const root = getPath(parsed, ['libraryfolders']) ?? parsed;
  if (!root || typeof root !== 'object') return [...libraries];

  for (const entry of Object.values(root)) {
    // Современный формат: { "0": { "path": "D:\\SteamLibrary", ... } }
    if (entry && typeof entry === 'object' && typeof entry.path === 'string') {
      libraries.add(entry.path);
      continue;
    }
    // Старый формат: { "1": "D:\\SteamLibrary" }
    if (typeof entry === 'string' && (entry.includes('\\') || entry.includes('/'))) {
      libraries.add(entry);
    }
  }

  return [...libraries];
}

/**
 * Папка установленной игры по её Steam AppID.
 *
 * @param {string|number} appId например 413150 для Stardew Valley
 * @returns {Promise<{path: string, library: string, name: string}|null>}
 */
async function findSteamApp(appId) {
  const steamRoot = await findSteamRoot();
  if (!steamRoot) return null;

  for (const library of listLibraryFolders(steamRoot)) {
    const manifestPath = path.join(library, 'steamapps', `appmanifest_${appId}.acf`);
    let raw;
    try {
      raw = fs.readFileSync(manifestPath, 'utf8');
    } catch {
      continue;
    }

    const manifest = parseVdf(raw);
    const installDir = getPath(manifest, ['AppState', 'installdir']);
    if (typeof installDir !== 'string' || installDir === '') continue;

    const gamePath = path.join(library, 'steamapps', 'common', installDir);
    if (!isDir(gamePath)) continue;

    return {
      path: gamePath,
      library,
      name: getPath(manifest, ['AppState', 'name']) ?? installDir,
    };
  }

  return null;
}

module.exports = { findSteamRoot, listLibraryFolders, findSteamApp };
