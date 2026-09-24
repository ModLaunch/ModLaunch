'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { downloadFile, fetchJson } = require('../download');
const { extractAll } = require('../archive');
const { assertWritable } = require('../permissions');
const { t } = require('../../i18n');

/**
 * Установка BepInEx — загрузчика модов для игр на Unity
 * (Lethal Company, Valheim, REPO и десятки других).
 *
 * Устроен удобно: это просто набор файлов, который кладётся в корень игры.
 * Дальше winhttp.dll перехватывает запуск, поэтому игру можно запускать
 * как обычно — хоть из Steam, хоть нашей кнопкой. Никаких особых
 * параметров запуска не требуется.
 */

/** Признаки того, что BepInEx уже стоит. */
function detect(gamePath) {
  const coreDir = path.join(gamePath, 'BepInEx', 'core');
  const proxy = path.join(gamePath, 'winhttp.dll');
  const installed = fs.existsSync(coreDir) && (process.platform !== 'win32' || fs.existsSync(proxy));

  if (!installed) return { installed: false, version: null };

  let version = null;
  try {
    const changelog = path.join(gamePath, 'BepInEx', 'core', 'BepInEx.Core.dll');
    version = fs.existsSync(changelog) ? t('loader.installed') : null;
  } catch {
    /* версия не критична */
  }

  return { installed: true, version };
}

/**
 * BepInEx для конкретной игры берётся из её же сообщества на Thunderstore —
 * там лежит собранный под эту игру пакет BepInExPack с правильными настройками.
 */
async function findPack(community, packageFullName) {
  const [owner, name] = packageFullName.split('-');
  // Сначала — один пакет по имени (3.1): у Valheim и Lethal Company полный
  // список сообщества весит десятки мегабайт и качается долго.
  try {
    const one = await fetchJson(`https://thunderstore.io/api/experimental/package/${encodeURIComponent(owner)}/${encodeURIComponent(name)}/`, { timeout: 20000 });
    if (one?.latest?.download_url) return { version: one.latest.version_number, downloadUrl: one.latest.download_url };
  } catch {
    /* запасной путь — полный список ниже */
  }
  const data = await fetchJson(
    `https://thunderstore.io/c/${community}/api/v1/package/`,
    { timeout: 45000 }
  );
  const pkg = (Array.isArray(data) ? data : []).find(
    (p) => p.owner === owner && p.name === name
  );
  if (!pkg || !pkg.versions?.length) {
    throw new Error(t('err.bepinex.noPack', { community, pkg: packageFullName }));
  }
  return {
    version: pkg.versions[0].version_number,
    downloadUrl: pkg.versions[0].download_url,
  };
}

/** Папки, в которые этот загрузчик будет писать. */
function targetsFor(game) {
  return game.writeTargets ? game.writeTargets(game.path) : [game.path];
}
/**
 * @param {object} game адаптер игры
 * @param {(stage: {step: string, detail?: string, ratio?: number|null}) => void} [onProgress]
 */
async function install(game, onProgress = () => {}) {
  assertWritable(targetsFor(game), game.path);

  onProgress({ code: 'loader.lookup', detail: 'BepInEx' });
  const pack = await findPack(game.thunderstoreCommunity, game.loader.thunderstorePackage);

  onProgress({ code: 'loader.download', detail: `BepInEx ${pack.version}` });
  const archive = await downloadFile(pack.downloadUrl, {
    fileName: 'BepInExPack.zip',
    onProgress: (p) =>
      onProgress({ code: 'loader.download', detail: `BepInEx ${pack.version}`, ratio: p.ratio }),
  });

  onProgress({ code: 'loader.install', detail: 'BepInEx' });
  // Пакеты BepInEx на Thunderstore кладут файлы внутрь папки вида
  // "BepInExPack_<Игра>" — её надо развернуть, иначе загрузчик окажется
  // на уровень глубже и игра его не увидит.
  const stripPrefix = detectPackPrefix(archive);
  extractAll(archive, game.path, stripPrefix);

  fs.rmSync(archive, { force: true });
  fs.mkdirSync(path.join(game.path, 'BepInEx', 'plugins'), { recursive: true });

  onProgress({ code: 'loader.done' });
  return { installed: true, version: pack.version };
}

/** Находит внутри архива папку, в которой реально лежит BepInEx. */
function detectPackPrefix(zipPath) {
  const AdmZip = require('adm-zip');
  const entries = new AdmZip(zipPath).getEntries().map((e) => e.entryName.replace(/\\/g, '/'));

  // Ищем winhttp.dll или BepInEx/core — то, что должно оказаться в корне игры.
  const anchor = entries.find((e) => /(^|\/)(winhttp\.dll|BepInEx\/core\/)/i.test(e));
  if (!anchor) return '';

  const marker = anchor.match(/(^|\/)(winhttp\.dll|BepInEx\/core\/)/i);
  const cut = anchor.slice(0, marker.index === 0 ? 0 : marker.index + 1);
  return cut;
}

module.exports = { detect, install };
