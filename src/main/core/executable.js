'use strict';

const fs = require('node:fs');
const path = require('node:path');

/**
 * Поиск исполняемого файла игры.
 *
 * Имя exe жёстко зашивать нельзя: у одной и той же игры оно отличается между
 * изданиями. Hollow Knight из Steam — hollow_knight.exe, из GOG — Hollow Knight.exe.
 * Угадывание тут обречено.
 *
 * Зато у игр на Unity есть надёжное правило: рядом с исполняемым файлом лежит
 * папка «<имя файла>_Data». Значит имя exe можно не угадывать, а прочитать
 * с диска — папка данных сама его называет.
 *
 * Служебные файлы Unity при этом надо исключить: UnityCrashHandler64.exe
 * запускается, но игру не открывает, и человек будет долго выяснять, почему
 * «кнопка нажимается, а ничего не происходит».
 */

const UNITY_SERVICE = /^(UnityCrashHandler|UnityPlayer|UnityWebRequest|baselib)/i;

function executableNamesFor(base) {
  if (process.platform === 'win32') return [`${base}.exe`];
  if (process.platform === 'darwin') return [path.join('Contents', 'MacOS', base), base];
  return [`${base}.x86_64`, `${base}.x86`, base];
}

/**
 * @param {string} gamePath папка игры
 * @param {object} [options]
 * @param {string[]} [options.preferred] имена, которые стоит проверить в первую очередь
 * @returns {{path: string, source: string}|null}
 */
function findGameExecutable(gamePath, options = {}) {
  if (!gamePath || !fs.existsSync(gamePath)) return null;

  // 1. Явно названные варианты — самый быстрый и точный путь.
  for (const name of options.preferred ?? []) {
    const full = path.join(gamePath, name);
    if (fs.existsSync(full)) return { path: full, source: 'known' };
  }

  let entries;
  try {
    entries = fs.readdirSync(gamePath, { withFileTypes: true });
  } catch {
    return null;
  }

  // 2. Имя из папки данных Unity — работает для любого издания игры.
  const dataDir = entries.find((e) => e.isDirectory() && /_Data$/i.test(e.name));
  if (dataDir) {
    const base = dataDir.name.replace(/_Data$/i, '');
    for (const name of executableNamesFor(base)) {
      const full = path.join(gamePath, name);
      if (fs.existsSync(full)) return { path: full, source: 'unity-data' };
    }
  }

  // 3. Последняя попытка: единственный подходящий исполняемый файл в папке.
  if (process.platform === 'win32') {
    const executables = entries
      .filter((e) => e.isFile() && e.name.toLowerCase().endsWith('.exe'))
      .map((e) => e.name)
      .filter((name) => !UNITY_SERVICE.test(name));

    if (executables.length === 1) {
      return { path: path.join(gamePath, executables[0]), source: 'only-exe' };
    }
  }

  return null;
}

/** Список исполняемых файлов в папке — чтобы показать человеку, что там вообще есть. */
function listExecutables(gamePath) {
  try {
    return fs
      .readdirSync(gamePath, { withFileTypes: true })
      .filter((e) => e.isFile())
      .map((e) => e.name)
      .filter((name) =>
        process.platform === 'win32'
          ? name.toLowerCase().endsWith('.exe')
          : /\.(x86_64|x86|sh)$/i.test(name)
      )
      .slice(0, 20);
  } catch {
    return [];
  }
}

module.exports = { findGameExecutable, listExecutables };
