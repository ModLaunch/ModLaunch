'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { pick } = require('./sections');
const { findGameExecutable } = require('../core/executable');
const common = require('./bepinex-common');

/**
 * Адаптер Risk of Rain 2 (3.1).
 *
 * Unity, BepInEx (общий BepInExPack от bbepis) и каталог Thunderstore.
 * RoR2BepInExPack от RiskofThunder — это не загрузчик, а исправления для
 * модов, поэтому он ставится как обычный мод, когда его попросят.
 */

const APP_ID = 632360;

/**
 * Сохранения RoR2 лежат не у игры, а в Steam: userdata/<id>/632360/remote.
 * Ищем в папке Steam, из которой стоит игра, и в обычных местах Steam.
 */
function steamSaves(gamePath) {
  const roots = [];
  const marker = /[\\/]steamapps[\\/]common[\\/]/i.exec(gamePath ?? '');
  if (marker) roots.push(gamePath.slice(0, marker.index));
  if (process.platform === 'win32') {
    for (const drive of ['C:', 'D:', 'E:']) roots.push(`${drive}\\Program Files (x86)\\Steam`, `${drive}\\Steam`);
  }
  for (const root of roots) {
    let users = [];
    try {
      users = fs.readdirSync(path.join(root, 'userdata'));
    } catch {
      continue;
    }
    const found = users
      .map((u) => path.join(root, 'userdata', u, String(APP_ID), 'remote', 'UserProfiles'))
      .filter((dir) => fs.existsSync(dir))
      .sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
    if (found[0]) return found[0];
  }
  return null;
}

module.exports = {
  id: 'risk-of-rain-2',
  name: 'Risk of Rain 2',
  shortName: 'Risk of Rain 2',
  steamAppId: APP_ID,
  folderNames: ['Risk of Rain 2'],
  accent: '#4FB0C6',

  loader: {
    kind: 'bepinex',
    name: 'BepInEx',
    site: 'https://thunderstore.io/c/riskofrain2/p/bbepis/BepInExPack/',
    thunderstorePackage: 'bbepis-BepInExPack',
  },

  thunderstoreCommunity: 'riskofrain2',

  catalog: {
    kind: 'thunderstore',
    community: 'riskofrain2',
    browseUrl: 'https://thunderstore.io/c/riskofrain2/',
  },

  reshade: { api: 'dx11' },

  sections: pick('all', 'picks', 'content', 'items', 'gameplay', 'cosmetics', 'audio', 'tools', 'modpacks'),
  featured: {
    picks: [
      'TeamMoonstorm-Starstorm2',
      'KingEnderBrine-ProperSave',
      'DropPod-LookingGlass',
      'KingEnderBrine-ScrollableLobbyUI',
      'EnforcerGang-Enforcer',
      'Paladin_Alliance-PaladinMod',
      'Zenithrium-VanillaVoid',
      'MagnusMagnuson-BiggerBazaar',
      'duckduckgreyduck-ArtificerExtended',
      'niwith-DropinMultiplayer',
    ],
    kits: [],
  },

  modMarker: { markerPattern: /\.dll$/i, maxDepth: 4 },
  modsDir: common.modsDir,

  signature: (dir, has) => has('Risk of Rain 2_Data') || (has('Risk of Rain 2.exe') && has('UnityPlayer.dll')),

  writeTargets: common.writeTargets,
  readModMeta: common.readThunderstoreMeta,
  savesDir: steamSaves,

  launch(gamePath) {
    const found = findGameExecutable(gamePath, {
      preferred: process.platform === 'win32' ? ['Risk of Rain 2.exe'] : ['Risk of Rain 2.x86_64'],
    });
    return { command: found?.path ?? path.join(gamePath, 'Risk of Rain 2.exe'), args: [], cwd: gamePath };
  },

  logPath: common.logPath,
  parseLog: common.parseBepInExLog,
};
