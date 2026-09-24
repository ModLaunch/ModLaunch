'use strict';

const path = require('node:path');
const { findGameExecutable } = require('../core/executable');

/**
 * Адаптер Subnautica.
 *
 * Игра на Unity. После обновления 2.0 («Living Large») сообщество перешло
 * на BepInEx: собранный под игру BepInExPack лежит в сообществе Subnautica
 * на Thunderstore вместе с самими модами. Поэтому устройство такое же, как
 * у Lethal Company: загрузчик — одним пакетом в корень игры, плагины —
 * в BepInEx/plugins, каталог и зависимости — с Thunderstore.
 *
 * Сохранения Subnautica хранит прямо в папке игры (SNAppData/SavedGames),
 * а не в профиле Windows, — их и копируют резервные копии ModHub.
 */
module.exports = {
  id: 'subnautica',
  name: 'Subnautica',
  shortName: 'Subnautica',
  steamAppId: 264710,
  folderNames: ['Subnautica'],
  accent: '#2BB3C0',

  loader: {
    kind: 'bepinex',
    name: 'BepInEx',
    site: 'https://thunderstore.io/c/subnautica/p/Subnautica_Modding/BepInExPack/',
    thunderstorePackage: 'Subnautica_Modding-BepInExPack',
  },

  thunderstoreCommunity: 'subnautica',

  catalog: {
    kind: 'thunderstore',
    community: 'subnautica',
    browseUrl: 'https://thunderstore.io/c/subnautica/',
  },

  modMarker: {
    markerPattern: /\.dll$/i,
    maxDepth: 4,
  },

  modsDir: (gamePath) => path.join(gamePath, 'BepInEx', 'plugins'),

  /**
   * Папка данных «Subnautica_Data». У Below Zero она называется
   * «SubnauticaZero_Data», так что две игры друг с другом не путаются.
   */
  signature: (dir, has) =>
    has('Subnautica_Data') || (has('Subnautica.exe') && has('UnityPlayer.dll')),

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
      preferred: process.platform === 'win32' ? ['Subnautica.exe'] : ['Subnautica.x86_64'],
    });
    return {
      command: found?.path ?? path.join(gamePath, 'Subnautica.exe'),
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
