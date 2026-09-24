'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { downloadFile } = require('../download');
const { extractAll } = require('../archive');
const modlinks = require('../sources/modlinks');
const { assertWritable } = require('../permissions');
const { t } = require('../../i18n');

/**
 * Установка Hollow Knight Modding API.
 *
 * Загрузчик здесь работает иначе, чем у остальных: он не добавляется рядом
 * с игрой, а подменяет Assembly-CSharp.dll в папке Managed. Поэтому перед
 * первой установкой оригинальный файл сохраняется как Assembly-CSharp.dll.vanilla —
 * ровно так же поступает Scarab, и благодаря этому «отключить моддинг целиком»
 * остаётся операцией на одну секунду, а не переустановкой игры.
 */

function managedDir(gamePath) {
  // Windows и Linux: hollow_knight_Data; macOS: Contents/Resources/Data
  const candidates = [
    path.join(gamePath, 'hollow_knight_Data', 'Managed'),
    path.join(gamePath, 'Hollow Knight_Data', 'Managed'),
    path.join(gamePath, 'Contents', 'Resources', 'Data', 'Managed'),
  ];
  return candidates.find((p) => fs.existsSync(p)) ?? candidates[0];
}

function detect(gamePath) {
  const managed = managedDir(gamePath);
  const modsDir = path.join(managed, 'Mods');
  const vanillaBackup = path.join(managed, 'Assembly-CSharp.dll.vanilla');
  const hooks = path.join(managed, 'MMHOOK_Assembly-CSharp.dll');

  const installed = fs.existsSync(vanillaBackup) && fs.existsSync(hooks);
  return { installed, version: installed ? t('loader.installed') : null, modsDir };
}

/** Папки, в которые этот загрузчик будет писать. */
function targetsFor(game) {
  return game.writeTargets ? game.writeTargets(game.path) : [game.path];
}
/**
 * @param {object} game адаптер игры
 * @param {(stage: object) => void} [onProgress]
 */
async function install(game, onProgress = () => {}) {
  // Права проверяем до первой записи: остановиться сейчас безопасно,
  // остановиться на середине подмены сборок игры — уже нет.
  assertWritable(targetsFor(game), game.path);

  const managed = managedDir(game.path);
  if (!fs.existsSync(managed)) throw new Error(t('err.hk.noManaged', { path: managed }));

  onProgress({ code: 'loader.lookup', detail: 'Modding API' });
  const api = await modlinks.loadApi();
  if (!api.downloadUrl) throw new Error(t('err.hk.noApiBuild'));

  onProgress({ code: 'loader.download', detail: `Modding API ${api.version}` });
  const archive = await downloadFile(api.downloadUrl, {
    fileName: `moddingapi-${api.version}.zip`,
    sha256: api.sha256 ?? undefined,
    onProgress: (p) =>
      onProgress({
        code: 'loader.download',
        detail: `Modding API ${api.version}`,
        ratio: p.ratio,
      }),
  });

  // Сохраняем оригинальную сборку игры до того, как что-либо перезапишем.
  const vanilla = path.join(managed, 'Assembly-CSharp.dll');
  const backup = path.join(managed, 'Assembly-CSharp.dll.vanilla');
  if (fs.existsSync(vanilla) && !fs.existsSync(backup)) {
    onProgress({ code: 'loader.backup' });
    fs.copyFileSync(vanilla, backup);
  }

  onProgress({ code: 'loader.install', detail: 'Modding API' });
  const workDir = fs.mkdtempSync(path.join(os.tmpdir(), 'modhub-hkapi-'));
  extractAll(archive, workDir);
  fs.rmSync(archive, { force: true });

  for (const entry of fs.readdirSync(workDir, { withFileTypes: true })) {
    if (!entry.isFile()) continue;
    fs.copyFileSync(path.join(workDir, entry.name), path.join(managed, entry.name));
  }
  fs.rmSync(workDir, { recursive: true, force: true });

  fs.mkdirSync(path.join(managed, 'Mods'), { recursive: true });

  onProgress({ code: 'loader.done' });
  return { installed: true, version: api.version };
}

/** Возвращает игру к ванильному состоянию, не трогая сами моды. */
function uninstall(gamePath) {
  const managed = managedDir(gamePath);
  const backup = path.join(managed, 'Assembly-CSharp.dll.vanilla');
  if (!fs.existsSync(backup)) return false;
  fs.copyFileSync(backup, path.join(managed, 'Assembly-CSharp.dll'));
  return true;
}

module.exports = { detect, install, uninstall, managedDir };
