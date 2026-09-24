'use strict';

/**
 * Общее для игр на Unity с BepInEx и каталогом Thunderstore (3.1):
 * метаданные пакета, журнал BepInEx, куда класть моды.
 */

const path = require('node:path');

/** manifest.json пакета Thunderstore → метаданные мода. */
function readThunderstoreMeta(manifestText, fallbackName) {
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
}

/** Ошибки модов из BepInEx/LogOutput.log. */
function parseBepInExLog(text) {
  const issues = [];
  for (const m of String(text ?? '').matchAll(/\[(Error|Fatal)\s*:\s*([^\]]+)\]\s*(.+)/gi)) {
    const mod = m[2].trim();
    if (/^BepInEx/i.test(mod)) continue;
    if (issues.some((i) => i.mod === mod)) continue;
    issues.push({ mod, level: 'error', message: m[3].trim() });
  }
  return issues;
}

const modsDir = (gamePath) => path.join(gamePath, 'BepInEx', 'plugins');
const writeTargets = (gamePath) => [gamePath, path.join(gamePath, 'BepInEx', 'plugins')];
const logPath = (gamePath) => path.join(gamePath, 'BepInEx', 'LogOutput.log');

module.exports = { readThunderstoreMeta, parseBepInExLog, modsDir, writeTargets, logPath };
