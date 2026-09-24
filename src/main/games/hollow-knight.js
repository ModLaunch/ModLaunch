'use strict';

const path = require('node:path');
const os = require('node:os');
const { pick } = require('./sections');
const hkapi = require('../core/loaders/hkapi');
const { findGameExecutable } = require('../core/executable');

/**
 * Адаптер Hollow Knight.
 *
 * Лучший случай из трёх: у сообщества есть открытый каталог ModLinks —
 * XML с прямыми ссылками, контрольными суммами и зависимостями. Значит
 * настоящая витрина с установкой в один клик работает прямо внутри программы,
 * без ключей, регистрации и походов в браузер.
 *
 * Моды — папки с .dll внутри Managed/Mods.
 */
module.exports = {
  id: 'hollow-knight',
  name: 'Hollow Knight',
  shortName: 'Hollow Knight',
  steamAppId: 367520,
  folderNames: ['Hollow Knight'],
  accent: '#6F9BFF',

  loader: {
    kind: 'hkapi',
    name: 'Modding API',
    site: 'https://github.com/hk-modding/api',
  },

  catalog: {
    kind: 'modlinks',
    browseUrl: 'https://github.com/hk-modding/modlinks',
  },

  sections: pick('all', 'picks', 'content', 'gameplay', 'cosmetics', 'tools', 'packs'),
  featured: {
    picks: ['Custom Knight', 'Benchwarp', 'Pale Court', 'Randomizer 4', 'HKMP', 'QoL', 'DebugMod'],
    kits: [
      { id: 'hk-comfort', mods: ['Benchwarp', 'QoL'] },
      { id: 'hk-coop', mods: ['HKMP', 'Custom Knight'] },
    ],
  },

  modMarker: {
    markerPattern: /\.dll$/i,
    maxDepth: 3,
  },

  modsDir: (gamePath) => path.join(hkapi.managedDir(gamePath), 'Mods'),

  /**
   * Папка данных Unity называется по имени исполняемого файла, поэтому у
   * разных изданий она пишется по-разному — проверяем оба варианта.
   */
  signature: (dir, has, match) =>
    has('hollow_knight_Data') ||
    has('Hollow Knight_Data') ||
    (typeof match === 'function' && match(/^goggame-1308320804\./i)),

  // Загрузчик подменяет сборки игры внутри Managed, поэтому права нужны
  // именно там — права на корень игры об этом ничего не говорят.
  writeTargets: (gamePath) => [
    gamePath,
    hkapi.managedDir(gamePath),
    path.join(hkapi.managedDir(gamePath), 'Mods'),
  ],

  readModMeta(_text, fallbackName) {
    // У модов Hollow Knight нет файла манифеста: имя папки и есть имя мода,
    // а версии и зависимости берутся из каталога ModLinks.
    return { id: fallbackName, name: fallbackName, version: '', dependencies: [] };
  },

  launch(gamePath) {
    // Имя файла отличается между изданиями: hollow_knight.exe в Steam,
    // Hollow Knight.exe в других. Поэтому не угадываем, а находим.
    const found = findGameExecutable(gamePath, {
      preferred:
        process.platform === 'win32'
          ? ['hollow_knight.exe', 'Hollow Knight.exe']
          : ['hollow_knight.x86_64', 'Hollow Knight.x86_64'],
    });
    return { command: found?.path ?? path.join(gamePath, 'hollow_knight.exe'), args: [], cwd: gamePath };
  },

  /** Сохранения (user1.dat…) лежат в LocalLow рядом с логом Modding API. */
  savesDir() {
    if (process.platform === 'win32') return path.join(os.homedir(), 'AppData', 'LocalLow', 'Team Cherry', 'Hollow Knight');
    return path.join(os.homedir(), '.config', 'unity3d', 'Team Cherry', 'Hollow Knight');
  },

  logPath() {
    if (process.platform === 'win32') {
      return path.join(
        os.homedir(),
        'AppData',
        'LocalLow',
        'Team Cherry',
        'Hollow Knight',
        'ModLog.txt'
      );
    }
    return path.join(os.homedir(), '.config', 'unity3d', 'Team Cherry', 'Hollow Knight', 'ModLog.txt');
  },

  parseLog(text) {
    const issues = [];
    for (const m of text.matchAll(/\[ERROR\]:?\s*\[([^\]]+)\]\s*-?\s*(.+)/g)) {
      const mod = m[1].trim();
      if (issues.some((i) => i.mod === mod)) continue;
      issues.push({ mod, level: 'error', message: m[2].trim() });
    }
    return issues;
  },
};
