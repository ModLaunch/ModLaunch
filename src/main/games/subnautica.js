'use strict';

const path = require('node:path');
const { findGameExecutable } = require('../core/executable');
const { pick } = require('./sections');

/**
 * Адаптер Subnautica.
 *
 * Игра на Unity. После обновления 2.0 («Living Large») сообщество перешло
 * на BepInEx: собранный под игру BepInExPack берётся из сообщества Subnautica
 * на Thunderstore, плагины кладутся в BepInEx/plugins.
 *
 * Каталог (2.0) — Nexus Mods: на Thunderstore у Subnautica всего несколько
 * десятков модов, а всё нужное — Nautilus, Decorations Mod, постройки,
 * транспорт — выкладывают на Nexus. Картинки модов ModHub показывает
 * оттуда же, по ссылке.
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
    kind: 'nexus',
    nexusDomain: 'subnautica',
    nexusGameId: 1155,
    browseUrl: 'https://www.nexusmods.com/subnautica/mods',
    // Моды, не обновлявшиеся с перехода игры на BepInEx и Nautilus (конец
    // 2022), помечаются «Устарел»: они сделаны для QModManager и, скорее всего,
    // не заработают на актуальной версии.
    legacyBefore: '2022-12-01',
  },

  // Шейдеры (2.1): DirectX 11: ReShade встаёт как dxgi.dll рядом с exe.
  reshade: { api: 'dx11' },

  sections: pick('all', 'picks', 'buildings', 'vehicles', 'items', 'gameplay', 'ui', 'tools', 'visuals', 'packs'),
  // Точные названия категорий Nexus (проверены на сайте, tools/probe.js).
  nexusCategories: {
    buildings: ['Buildables'],
    vehicles: ['Vehicles and Upgrades'],
    items: ['Items', 'Crafting'],
    gameplay: ['Gameplay', 'Creatures', 'Environment', 'Adventure'],
    ui: ['User Interface'],
    tools: ['Libraries', 'Utilities', 'Modding Tools'],
    visuals: ['Visuals and Graphics'],
  },
  featured: {
    // Номера модов на Nexus. Nautilus — библиотека, без которой не работает
    // почти ничего, поэтому она первая и входит в каждый набор.
    picks: [
      '1262', // Nautilus
      '1112', // Configuration Manager for BepInEx
      '859', // Vehicle Framework
      '1457', // ECC Library 2.0
      '207', // Radial tabs
      '984', // Quick Slots Plus
      '142', // Slot Extender
      '12', // Map
      '24', // EasyCraft
      '2800', // Decorations Mod (Continued)
      '1119', // Base Kits
      '3143', // Composite Buildables (continued)
      '2447', // Modularily Based
      '3816', // Builder Module
      '1180', // SleekBases
      '1121', // Building Tweaks
      '1504', // AutoSortLockers
      '141', // Alien Rifle
      '216', // Defabricator
      '398', // More Modified Items
      '220', // Pickupable Storage Enhanced
      '1116', // All Items 1x1
      '1206', // Inventory Size
      '640', // De-Extinction 2.0
      '1604', // Bloop and Blaza Leviathans
      '1820', // The Red Plague
      '1542', // Epic Weather Mod
      '871', // Odyssey Vehicle
      '1748', // Beluga Submarine
      '1912', // The Hydra Submarine
      '2153', // Echelon
      '2461', // The Prototype Expansion
      '365', // Seamoth Arms
      '136', // Laser Cannon
      '1135', // More Seamoth Depth Modules
      '235', // Better Scanner Room
      '1453', // CustomBatteries (Purple Edition)
      '722', // Tweaks and Fixes
      '237', // Subnautica Autosave
      '389', // Performance Booster
      '3419', // Inventory Stacking
      '517', // Free Look
      '1300', // Blueprint Search Bar
      '229', // Storage Info
      '125', // Accelerated Start
    ],
    kits: [
      { id: 'sn-builder', mods: ['1262', '2800', '1119', '3143', '2447', '1180', '1121'] },
      { id: 'sn-vehicles', mods: ['1262', '142', '365', '859', '1135', '3816'] },
      { id: 'sn-comfort', mods: ['1262', '1112', '207', '984', '722', '3419', '235', '1453', '237'] },
      // Новые подлодки (3.2): все на Vehicle Framework — он встанет сам.
      { id: 'sn-subs', mods: ['1262', '859', '871', '1748', '1912', '2153', '2461'] },
      // Новые существа и сюжет (3.2).
      { id: 'sn-story', mods: ['1262', '1457', '640', '1604', '1820', '1542'] },
    ],
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
