'use strict';

const path = require('node:path');
const os = require('node:os');

/**
 * Адаптер Stardew Valley.
 *
 * Моды — это папки в <игра>/Mods, каждая с файлом manifest.json.
 * Именно manifest.json делает установку надёжной: по нему мы находим корень
 * мода внутри архива любой вложенности и читаем список зависимостей,
 * который в описании на сайте обычно теряется.
 *
 * Каталог здесь закрыт: моды живут на Nexus, и файл приходит через nxm://.
 * Витрины поэтому нет, но всё остальное — установка, зависимости,
 * включение/выключение, запуск — работает полностью.
 */
module.exports = {
  id: 'stardew-valley',
  name: 'Stardew Valley',
  shortName: 'Stardew',
  steamAppId: 413150,
  folderNames: ['Stardew Valley', 'StardewValley'],
  accent: '#6BAA3C',

  loader: {
    kind: 'smapi',
    name: 'SMAPI',
    site: 'https://smapi.io',
  },

  catalog: {
    kind: 'nexus',
    nexusDomain: 'stardewvalley',
    nexusGameId: 1303,
    // SMAPI ставится кнопкой загрузчика, в витрине ему делать нечего.
    hide: ['2400'],
    browseUrl: 'https://www.nexusmods.com/stardewvalley/mods',
  },

  /** Как опознать мод внутри архива. */
  modMarker: {
    markerFiles: ['manifest.json'],
    maxDepth: 5,
  },

  modsDir: (gamePath) => path.join(gamePath, 'Mods'),

  /**
   * Признак, по которому папка опознаётся как Stardew Valley.
   * Имя папки не проверяется вовсе: у репаков и переносов оно любое,
   * а содержимое всегда одинаковое.
   *
   * Проверок несколько, потому что изданий несколько. В версии 1.6 игра
   * переехала на .NET, и рядом с exe появился «Stardew Valley.dll»; у сборки
   * из GOG рядом лежит ещё и goggame-1453375253.info — по нему папка
   * опознаётся, даже если исполняемый файл переименован.
   */
  signature: (dir, has, match) =>
    ((has('Stardew Valley.exe') ||
      has('Stardew Valley.dll') ||
      has('StardewValley.exe') ||
      has('Stardew Valley.deps.json')) &&
      has('Content')) ||
    (typeof match === 'function' && match(/^goggame-1453375253\./i) && has('Content')),

  // SMAPI кладёт свои файлы в корень игры, моды — в Mods.
  writeTargets: (gamePath) => [gamePath, path.join(gamePath, 'Mods')],

  /**
   * Читает метаданные мода из его manifest.json.
   * Это и есть источник имени, версии и зависимостей.
   */
  readModMeta(manifestText, fallbackName) {
    let data;
    try {
      // В manifest.json моддеры регулярно оставляют висящие запятые.
      data = JSON.parse(manifestText.replace(/,\s*([}\]])/g, '$1'));
    } catch {
      return { id: fallbackName, name: fallbackName, version: '', dependencies: [] };
    }

    return {
      id: data.UniqueID ?? fallbackName,
      name: data.Name ?? fallbackName,
      version: data.Version ?? '',
      author: data.Author ?? '',
      description: data.Description ?? '',
      dependencies: (data.Dependencies ?? [])
        .filter((d) => d && d.IsRequired !== false)
        .map((d) => ({ id: d.UniqueID, minVersion: d.MinimumVersion ?? null })),
      contentPackFor: data.ContentPackFor?.UniqueID ?? null,
      updateKeys: data.UpdateKeys ?? [],
    };
  },

  launch(gamePath) {
    const exe = process.platform === 'win32' ? 'StardewModdingAPI.exe' : 'StardewModdingAPI';
    return { command: path.join(gamePath, exe), args: [], cwd: gamePath };
  },

  /** Где SMAPI оставляет лог — он и отвечает на вопрос «какой мод сломался». */
  logPath() {
    if (process.platform === 'win32') {
      return path.join(os.homedir(), 'AppData', 'Roaming', 'StardewValley', 'ErrorLogs', 'SMAPI-latest.txt');
    }
    return path.join(os.homedir(), '.config', 'StardewValley', 'ErrorLogs', 'SMAPI-latest.txt');
  },

  /** Разбор лога SMAPI: ищем моды, которые не загрузились или упали. */
  parseLog(text) {
    const issues = [];
    const skipped = /Skipped mods\s*[\s\S]*?(?=\n\[|\n\n\n|$)/i.exec(text);
    if (skipped) {
      for (const line of skipped[0].split('\n')) {
        const m = line.match(/^\s*-\s+(.+?)\s+because\s+(.+)$/i);
        if (m) issues.push({ mod: m[1].trim(), level: 'error', message: m[2].trim() });
      }
    }
    for (const m of text.matchAll(/\[\s*ERROR\s+([^\]]+)\]\s*(.+)/g)) {
      const mod = m[1].trim();
      if (mod.toLowerCase() === 'smapi') continue;
      if (issues.some((i) => i.mod === mod)) continue;
      issues.push({ mod, level: 'error', message: m[2].trim() });
    }
    return issues;
  },
};
