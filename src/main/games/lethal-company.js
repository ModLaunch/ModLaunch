'use strict';

const path = require('node:path');
const os = require('node:os');
const { findGameExecutable } = require('../core/executable');

/**
 * Адаптер Lethal Company.
 *
 * Игра на Unity с загрузчиком BepInEx и полноценным открытым каталогом
 * Thunderstore: список пакетов одним запросом, прямые ссылки на файлы,
 * машиночитаемые зависимости. Здесь витрина работает в полную силу —
 * ровно то, ради чего эта игра и взята третьей.
 *
 * Особенность BepInEx: плагины бывают и одиночными .dll, и папками
 * с ресурсами. Оба варианта кладутся в BepInEx/plugins.
 */
module.exports = {
  id: 'lethal-company',
  name: 'Lethal Company',
  shortName: 'Lethal Company',
  steamAppId: 1966720,
  folderNames: ['Lethal Company'],
  accent: '#C9A227',

  loader: {
    kind: 'bepinex',
    name: 'BepInEx',
    site: 'https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/',
    thunderstorePackage: 'BepInEx-BepInExPack',
  },

  thunderstoreCommunity: 'lethal-company',

  catalog: {
    kind: 'thunderstore',
    community: 'lethal-company',
    browseUrl: 'https://thunderstore.io/c/lethal-company/',
  },

  modMarker: {
    markerPattern: /\.dll$/i,
    maxDepth: 4,
  },

  modsDir: (gamePath) => path.join(gamePath, 'BepInEx', 'plugins'),

  /**
   * Папка данных Unity — надёжный признак независимо от имени папки игры.
   * У репаков папка иногда переименована под движок сборщика, поэтому
   * вторым признаком идёт пара «exe + UnityPlayer.dll».
   */
  signature: (dir, has) =>
    has('Lethal Company_Data') || (has('Lethal Company.exe') && has('UnityPlayer.dll')),

  // BepInEx кладёт winhttp.dll в корень игры, плагины — в BepInEx/plugins.
  writeTargets: (gamePath) => [gamePath, path.join(gamePath, 'BepInEx', 'plugins')],

  readModMeta(manifestText, fallbackName) {
    // У пакетов Thunderstore есть manifest.json, но со своей схемой.
    try {
      const data = JSON.parse(manifestText);
      return {
        id: data.name ?? fallbackName,
        name: data.name ?? fallbackName,
        version: data.version_number ?? '',
        description: data.description ?? '',
        dependencies: (data.dependencies ?? []).map((d) => ({
          id: d.split('-').slice(0, 2).join('-'),
          minVersion: d.split('-')[2] ?? null,
        })),
      };
    } catch {
      return { id: fallbackName, name: fallbackName, version: '', dependencies: [] };
    }
  },

  /** Сохранения (LCSaveFile1…) — в LocalLow у Unity. */
  savesDir() {
    if (process.platform === 'win32') return path.join(os.homedir(), 'AppData', 'LocalLow', 'ZeekerssRBLX', 'Lethal Company');
    return path.join(os.homedir(), '.config', 'unity3d', 'ZeekerssRBLX', 'Lethal Company');
  },

  launch(gamePath) {
    // BepInEx внедряется через winhttp.dll, поэтому игра запускается обычным
    // способом — никаких особых аргументов не нужно.
    const found = findGameExecutable(gamePath, {
      preferred:
        process.platform === 'win32' ? ['Lethal Company.exe'] : ['Lethal Company.x86_64'],
    });
    return {
      command: found?.path ?? path.join(gamePath, 'Lethal Company.exe'),
      args: [],
      cwd: gamePath,
    };
  },

  logPath(gamePath) {
    return path.join(gamePath, 'BepInEx', 'LogOutput.log');
  },

  parseLog(text) {
    const issues = [];
    for (const m of text.matchAll(/\[(Error|Fatal)\s*:\s*([^\]]+)\]\s*(.+)/gi)) {
      const mod = m[2].trim();
      if (/^BepInEx/i.test(mod)) continue;
      if (issues.some((i) => i.mod === mod)) continue;
      issues.push({ mod, level: 'error', message: m[3].trim() });
    }
    return issues;
  },
};
