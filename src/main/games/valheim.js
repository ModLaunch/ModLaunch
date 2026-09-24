'use strict';

const path = require('node:path');
const os = require('node:os');
const { pick } = require('./sections');
const { findGameExecutable } = require('../core/executable');
const common = require('./bepinex-common');

/**
 * Адаптер Valheim (3.1).
 *
 * Unity, BepInEx и большой каталог Thunderstore — как у Lethal Company.
 * Своя сборка загрузчика: BepInExPack_Valheim от denikson (в ней
 * правильные библиотеки Unity для этой игры).
 */
module.exports = {
  id: 'valheim',
  name: 'Valheim',
  shortName: 'Valheim',
  steamAppId: 892970,
  folderNames: ['Valheim'],
  accent: '#E09F3E',

  loader: {
    kind: 'bepinex',
    name: 'BepInEx',
    site: 'https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/',
    thunderstorePackage: 'denikson-BepInExPack_Valheim',
  },

  thunderstoreCommunity: 'valheim',

  catalog: {
    kind: 'thunderstore',
    community: 'valheim',
    browseUrl: 'https://thunderstore.io/c/valheim/',
  },

  // Шейдеры: Valheim по умолчанию на DirectX 11.
  reshade: { api: 'dx11' },

  sections: pick('all', 'picks', 'buildings', 'items', 'content', 'gameplay', 'vehicles', 'cosmetics', 'audio', 'tools', 'modpacks'),
  featured: {
    // Самые скачиваемые моды на Thunderstore (проверено tools/experiment.js).
    picks: [
      'Advize-PlantEverything',
      'RandyKnapp-EquipmentAndQuickSlots',
      'shudnal-ExtraSlots',
      'Advize-PlantEasily',
      'OdinPlus-TeleportEverything',
      'MSchmoecker-MultiUserChest',
      'ishid4-BetterArchery',
      'Goldenrevolver-Quick_Stack_Store_Sort_Trash_Restock',
      'Tekla-AutoRepair',
      'BentoG-MissingPieces',
      'RustyMods-Seasonality',
      'Therzie-Warfare',
    ],
    kits: [],
  },

  modMarker: { markerPattern: /\.dll$/i, maxDepth: 4 },
  modsDir: common.modsDir,

  /** Папка данных valheim_Data; у выделенного сервера — valheim_server_Data. */
  signature: (dir, has) => has('valheim_Data') || (has('valheim.exe') && has('UnityPlayer.dll')),

  writeTargets: common.writeTargets,
  readModMeta: common.readThunderstoreMeta,

  /** Миры и персонажи — в LocalLow у Unity (worlds_local, characters_local). */
  savesDir() {
    if (process.platform === 'win32') return path.join(os.homedir(), 'AppData', 'LocalLow', 'IronGate', 'Valheim');
    return path.join(os.homedir(), '.config', 'unity3d', 'IronGate', 'Valheim');
  },

  launch(gamePath) {
    const found = findGameExecutable(gamePath, {
      preferred: process.platform === 'win32' ? ['valheim.exe'] : ['valheim.x86_64'],
    });
    return { command: found?.path ?? path.join(gamePath, 'valheim.exe'), args: [], cwd: gamePath };
  },

  logPath: common.logPath,
  parseLog: common.parseBepInExLog,
};
