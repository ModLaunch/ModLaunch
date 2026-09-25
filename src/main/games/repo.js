'use strict';

const path = require('node:path');
const os = require('node:os');
const { pick } = require('./sections');
const { findGameExecutable } = require('../core/executable');
const common = require('./bepinex-common');

/**
 * Адаптер R.E.P.O. (3.2).
 *
 * Кооперативный хоррор на Unity: общий BepInExPack от BepInEx и почти пять
 * тысяч модов на Thunderstore (сообщество repo) — проверено tools/experiment.js.
 */
module.exports = {
  id: 'repo',
  name: 'R.E.P.O.',
  shortName: 'R.E.P.O.',
  steamAppId: 3241660,
  folderNames: ['REPO', 'R.E.P.O.'],
  accent: '#F2A93B',

  loader: {
    kind: 'bepinex',
    name: 'BepInEx',
    site: 'https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/',
    thunderstorePackage: 'BepInEx-BepInExPack',
  },

  thunderstoreCommunity: 'repo',

  catalog: {
    kind: 'thunderstore',
    community: 'repo',
    browseUrl: 'https://thunderstore.io/c/repo/',
  },

  reshade: { api: 'dx11' },

  sections: pick('all', 'picks', 'items', 'content', 'gameplay', 'cosmetics', 'audio', 'tools', 'modpacks'),
  featured: {
    // Самые скачиваемые моды (не библиотеки) на Thunderstore.
    picks: [
      'YMC_MHZ-MoreHead',
      'BULLETBOT-MoreUpgrades',
      'Zehs-ExtractionPointConfirmButton',
      'Magic_Wesley-Wesleys_Enemies',
      'flipf17-DeadTTS',
      'Cronchy-DeathHeadHopper',
      'Jettcodey-MoreShopItems_Updated',
      'XiaohaiMod-XH_DamageShow_EnemyHealthBar',
      'Lazarus-BetterTruckHeals',
      'Tidaleus-MoreReviveHP',
      'Magic_Wesley-Wesleys_Valuables',
      'Zehs-LethalCompanyValuables',
    ],
    kits: [],
  },

  modMarker: { markerPattern: /\.dll$/i, maxDepth: 4 },
  modsDir: common.modsDir,

  signature: (dir, has) => has('REPO_Data') || (has('REPO.exe') && has('UnityPlayer.dll')),

  writeTargets: common.writeTargets,
  readModMeta: common.readThunderstoreMeta,

  /** Сохранения — в LocalLow у Unity (разработчик semiwork). */
  savesDir() {
    if (process.platform === 'win32') return path.join(os.homedir(), 'AppData', 'LocalLow', 'semiwork', 'Repo', 'saves');
    return path.join(os.homedir(), '.config', 'unity3d', 'semiwork', 'Repo', 'saves');
  },

  launch(gamePath) {
    const found = findGameExecutable(gamePath, {
      preferred: process.platform === 'win32' ? ['REPO.exe'] : ['REPO.x86_64'],
    });
    return { command: found?.path ?? path.join(gamePath, 'REPO.exe'), args: [], cwd: gamePath };
  },

  logPath: common.logPath,
  parseLog: common.parseBepInExLog,
};
