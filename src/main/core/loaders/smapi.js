'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { downloadFile, fetchJson } = require('../download');
const { extractAll } = require('../archive');
const { assertWritable } = require('../permissions');
const { runLogged } = require('../proc');
const { t } = require('../../i18n');

/**
 * Установка SMAPI — загрузчика модов Stardew Valley.
 *
 * SMAPI ставится собственным установщиком, и это хорошо: он умеет чинить
 * повреждённые установки и корректно обновляться. Нам остаётся скачать его
 * и запустить с уже найденным путём к игре, чтобы человек не отвечал
 * на вопросы, ответы на которые мы и так знаем:
 *
 *   --install --game-path "<путь>" --no-prompt
 *
 * На Linux и macOS аргументы командной строки SMAPI не поддерживает,
 * поэтому там установщик открывается как есть, и мы честно об этом говорим.
 */

const RELEASES_API = 'https://api.github.com/repos/Pathoschild/SMAPI/releases/latest';

/** Проверяет, установлен ли SMAPI в папке игры. */
function detect(gamePath) {
  const exe = path.join(gamePath, 'StardewModdingAPI.exe');
  const dll = path.join(gamePath, 'StardewModdingAPI.dll');
  const installed = fs.existsSync(exe) || fs.existsSync(dll);

  let version = null;
  if (installed) {
    try {
      const meta = path.join(gamePath, 'smapi-internal', 'metadata.json');
      if (fs.existsSync(meta)) version = t('loader.installed');
    } catch {
      /* версия не критична */
    }
  }

  return { installed, version };
}

/** Ищет последний релиз SMAPI и ссылку на обычный (не для разработчиков) установщик. */
async function findLatestRelease() {
  const release = await fetchJson(RELEASES_API, {
    headers: { Accept: 'application/vnd.github+json' },
    timeout: 20000,
  });

  const asset = (release.assets ?? []).find(
    (a) => /installer\.zip$/i.test(a.name) && !/for-developers/i.test(a.name)
  );

  if (!asset) throw new Error(t('err.smapi.noAsset'));

  return {
    version: (release.tag_name ?? '').replace(/^v/, ''),
    downloadUrl: asset.browser_download_url,
    fileName: asset.name,
  };
}

/** Папки, в которые этот загрузчик будет писать. */
function targetsFor(game) {
  return game.writeTargets ? game.writeTargets(game.path) : [game.path];
}
/**
 * Куда распаковывается установщик SMAPI.
 *
 * Не во временную папку: если установка не удалась, человек должен иметь
 * возможность запустить установщик руками, своими глазами прочитать, что
 * тот скажет, и ответить на его вопросы. Временную папку Windows может
 * вычистить в любой момент, поэтому держим её рядом с настройками.
 */
function installerDir(dataDir) {
  return path.join(dataDir ?? os.tmpdir(), 'smapi-installer');
}

/** Файл, который запускает установщик SMAPI в обычном окне консоли. */
function manualEntry(root) {
  const bat = findFile(root, /^install on Windows\.bat$/i);
  return bat ?? findInstallerExecutable(root);
}

/**
 * @param {object} game адаптер игры
 * @param {(stage: object) => void} [onProgress]
 * @param {{dataDir?: string, logFile?: string}} [ctx]
 */
async function install(game, onProgress = () => {}, ctx = {}) {
  assertWritable(targetsFor(game), game.path);

  onProgress({ code: 'loader.lookup', detail: 'SMAPI' });
  const release = await findLatestRelease();

  onProgress({ code: 'loader.download', detail: `SMAPI ${release.version}` });
  const archive = await downloadFile(release.downloadUrl, {
    fileName: release.fileName,
    onProgress: (p) =>
      onProgress({ code: 'loader.download', detail: `SMAPI ${release.version}`, ratio: p.ratio }),
  });

  onProgress({ code: 'loader.unpack' });
  const workDir = installerDir(ctx.dataDir);
  fs.rmSync(workDir, { recursive: true, force: true });
  fs.mkdirSync(workDir, { recursive: true });
  extractAll(archive, workDir);
  fs.rmSync(archive, { force: true });

  const installerExe = findInstallerExecutable(workDir);
  if (!installerExe) throw new Error(t('err.smapi.noExe'));

  if (process.platform !== 'win32') {
    throw new Error(t('err.smapi.notWindows', { path: installerExe }));
  }

  onProgress({ code: 'loader.install', detail: 'SMAPI' });
  const run = await runLogged(
    installerExe,
    ['--install', '--game-path', game.path, '--no-prompt'],
    {
      cwd: path.dirname(installerExe),
      timeout: 300000,
      logFile: ctx.logFile,
      header: '[ModHub] установка SMAPI',
    }
  );

  const result = detect(game.path);

  if (!result.installed) {
    // Установщик SMAPI - отдельная программа со своими причинами отказа.
    // Показываем его собственные слова, а не наш пересказ.
    const error = new Error(
      run.message
        ? t('err.smapi.failed', { reason: run.message })
        : t('err.smapi.noFiles')
    );
    error.alreadyExplained = true;
    error.loaderFallback = 'smapi';
    error.manualPath = manualEntry(workDir);
    error.logFile = run.logFile;
    throw error;
  }

  onProgress({ code: 'loader.done' });
  return { installed: true, version: release.version };
}

/** Ищет файл по маске внутри распакованного архива, не полагаясь на имена папок. */
function findFile(root, pattern) {
  const stack = [root];
  while (stack.length) {
    const dir = stack.pop();
    let entries;
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) stack.push(full);
      else if (pattern.test(entry.name)) return full;
    }
  }
  return null;
}

/** Ищет SMAPI.Installer.exe в распакованном архиве. */
function findInstallerExecutable(root) {
  return findFile(root, /^SMAPI\.Installer\.exe$/i);
}

module.exports = { detect, install, installerDir, manualEntry, findInstallerExecutable };
