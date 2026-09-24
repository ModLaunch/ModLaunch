'use strict';

/**
 * Ожидание файла, скачанного браузером.
 *
 * Nexus отдаёт файлы бесплатным аккаунтам только со своей страницы: человек
 * жмёт там «Slow download», браузер кладёт архив в «Загрузки». ModHub в это
 * время смотрит в папку и, как только архив докачался, ставит его сам —
 * ключ API для этого не нужен.
 *
 * Опрос раз в секунду, а не fs.watch: у браузеров разные привычки (Chrome
 * пишет .crdownload и переименовывает, Firefox держит рядом .part), и
 * надёжнее всего просто дождаться, пока файл перестанет расти.
 */

const fs = require('node:fs');
const path = require('node:path');

const ARCHIVE = /\.(zip|7z|rar)$/i;
const PARTIAL = /\.(crdownload|part|partial|download|tmp|opdownload)$/i;

function list(dir) {
  const result = new Map();
  let names = [];
  try {
    names = fs.readdirSync(dir);
  } catch {
    return result;
  }
  for (const name of names) {
    try {
      const stat = fs.statSync(path.join(dir, name));
      if (stat.isFile()) result.set(name, { size: stat.size, mtimeMs: stat.mtimeMs });
    } catch {
      /* файл исчез между чтением папки и stat — бывает при докачке */
    }
  }
  return result;
}

/**
 * @param {{dir: string, match: (name: string) => boolean, timeout?: number, interval?: number, signal?: AbortSignal}} options
 * @returns {Promise<string>} путь к докачанному архиву
 */
function waitForDownload({ dir, match, timeout = 20 * 60 * 1000, interval = 1000, signal }) {
  const before = list(dir);
  const started = Date.now();
  const sizes = new Map(); // имя -> размер на прошлом шаге

  return new Promise((resolve, reject) => {
    let timer = null;

    const finish = (fn, value) => {
      clearTimeout(timer);
      signal?.removeEventListener('abort', onAbort);
      fn(value);
    };
    const onAbort = () => finish(reject, Object.assign(new Error('cancelled'), { code: 'DL_CANCELLED' }));
    if (signal?.aborted) return onAbort();
    signal?.addEventListener('abort', onAbort);

    const tick = () => {
      if (Date.now() - started > timeout) {
        return finish(reject, Object.assign(new Error('timeout'), { code: 'DL_TIMEOUT' }));
      }
      const now = list(dir);
      for (const [name, info] of now) {
        if (!ARCHIVE.test(name) || PARTIAL.test(name) || !match(name)) continue;
        const old = before.get(name);
        // Новый файл или тот же, но перезаписанный заново.
        if (old && old.mtimeMs === info.mtimeMs && old.size === info.size) continue;
        // Firefox держит рядом «name.part», пока качает.
        if (now.has(`${name}.part`)) continue;
        if (info.size > 0 && sizes.get(name) === info.size) {
          return finish(resolve, path.join(dir, name));
        }
        sizes.set(name, info.size);
      }
      timer = setTimeout(tick, interval);
    };
    timer = setTimeout(tick, interval);
  });
}

/** Архив Nexus называется «Имя-1915-2-9-1-1713265122.zip»: номер мода — между дефисами. */
function nexusMatcher(modId) {
  const marker = `-${modId}-`;
  return (name) => name.includes(marker);
}

module.exports = { waitForDownload, nexusMatcher, ARCHIVE, PARTIAL };
