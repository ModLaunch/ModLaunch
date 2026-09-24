'use strict';

const path = require('node:path');
const { findGameExecutable } = require('../core/executable');

/**
 * Адаптер Subnautica: Below Zero.
 *
 * Продолжение Subnautica на том же движке и с тем же устройством модов:
 * BepInExPack и моды — из сообщества Below Zero на Thunderstore, плагины —
 * в BepInEx/plugins. Папка данных — «SubnauticaZero_Data», по ней игра
 * и отличается от первой части.
 *
 * Сохранения, как и у первой части, лежат в папке игры: SNAppData/SavedGames.
 */
module.exports = {
  id: 'subnautica-below-zero',
  name: 'Subnautica: Below Zero',
  shortName: 'Below Zero',
  steamAppId: 848450,
  folderNames: ['SubnauticaZero', 'Subnautica Below Zero', 'Subnautica - Below Zero'],
  accent: '#7FB4E6',

  loader: {
    kind: 'bepinex',
    name: 'BepInEx',
    site: 'https://thunderstore.io/c/subnautica-below-zero/p/Subnautica_Modding/BepInExPack/',
    thunderstorePackage: 'Subnautica_Modding-BepInExPack',
  },

  thunderstoreCommunity: 'subnautica-below-zero',

  catalog: {
    kind: 'thunderstore',
    community: 'subnautica-below-zero',
    browseUrl: 'https://thunderstore.io/c/subnautica-below-zero/',
  },

  modMarker: {
    markerPattern: /\.dll$/i,
    maxDepth: 4,
  },

  modsDir: (gamePath) => path.join(gamePath, 'BepInEx', 'plugins'),

  /** Папка данных «SubnauticaZero_Data» — у первой части её нет. */
  signature: (dir, has) =>
    has('SubnauticaZero_Data') || (has('SubnauticaZero.exe') && has('UnityPlayer.dll')),

  writeTargets: (gamePath) => [gamePath, path.join(gamePath, 'BepInEx', 'plugins')],

  /** Сохранения: в папке игры, у Steam- и Epic-изданий одинаково. */
  savesDir: (gamePath) => path.join(gamePath, 'SNAppData', 'SavedGames'),

  readModMeta(manifestText, fallbackName) {
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

  launch(gamePath) {
    const found = findGameExecutable(gamePath, {
      preferred: process.platform === 'win32' ? ['SubnauticaZero.exe'] : ['SubnauticaZero.x86_64'],
    });
    return {
      command: found?.path ?? path.join(gamePath, 'SubnauticaZero.exe'),
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
