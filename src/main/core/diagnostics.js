'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { listExecutables } = require('./executable');
const { t } = require('../i18n');

/**
 * Запуск игры и разбор её лога.
 *
 * Седьмой шаг любого гайда — «а теперь запускайте игру через загрузчик,
 * а если не работает, отключайте моды по одному». Первый вопрос человека
 * звучит «как поставить», второй, через неделю, — «почему всё сломалось».
 * Установщиков много, отвечающих на второй вопрос почти нет.
 */

/**
 * Строка параметров запуска («-novid -windowed "-log file.txt"») → список.
 * Кавычки держат пробелы внутри одного параметра, как в Steam.
 */
function splitArgs(text) {
  const out = [];
  for (const match of String(text ?? '').matchAll(/"([^"]*)"|'([^']*)'|(\S+)/g)) {
    out.push(match[1] ?? match[2] ?? match[3]);
  }
  return out.slice(0, 40).map((arg) => arg.slice(0, 400));
}

/**
 * Запускает игру через нужный исполняемый файл.
 * Процесс отвязывается от ModHub: закрытие лаунчера не должно убивать игру.
 *
 * @param {object} game адаптер
 * @param {string} gamePath папка игры
 * @param {{extraArgs?: string, onExit?: () => void}} [options]
 *   extraArgs — параметры запуска из настроек (1.10);
 *   onExit — игра закрылась (для учёта игрового времени)
 */
function launchGame(game, gamePath, options = {}) {
  const launch = game.launch(gamePath);
  const { command, cwd } = launch;
  const args = [...launch.args, ...splitArgs(options.extraArgs)];

  if (!fs.existsSync(command)) {
    // Разделяем два разных случая: не стоит загрузчик — и не найдена сама игра.
    // Раньше здесь всегда обвинялся загрузчик, и это уводило в сторону.
    const looksLikeLoader = /StardewModdingAPI/i.test(path.basename(command));

    if (looksLikeLoader) {
      throw new Error(t('err.loaderExeMissing', { loader: game.loader.name, path: command }));
    }

    const present = listExecutables(gamePath);
    throw new Error(
      present.length
        ? t('err.gameExeMissing', { path: command, present: present.join(', ') })
        : t('err.gameExeMissingEmpty', { path: command })
    );
  }

  const child = spawn(command, args, {
    cwd,
    detached: true,
    stdio: 'ignore',
    windowsHide: false,
  });
  // unref не мешает узнать о выходе игры, пока ModHub открыт.
  if (typeof options.onExit === 'function') {
    child.once('exit', () => options.onExit());
    child.once('error', () => options.onExit());
  }
  child.unref();

  return { pid: child.pid, command, args };
}

/**
 * Читает лог загрузчика и возвращает список проблемных модов.
 *
 * @returns {{available: boolean, path: string, modifiedAt: string|null, issues: object[]}}
 */
function readIssues(game, gamePath) {
  const logPath = game.logPath(gamePath);

  if (!logPath || !fs.existsSync(logPath)) {
    return { available: false, path: logPath ?? null, modifiedAt: null, issues: [] };
  }

  let text;
  try {
    text = fs.readFileSync(logPath, 'utf8');
  } catch (error) {
    return { available: false, path: logPath, modifiedAt: null, issues: [], error: error.message };
  }

  // Логи растут неограниченно; последних 512 КБ достаточно для последнего запуска.
  const MAX = 512 * 1024;
  if (text.length > MAX) text = text.slice(-MAX);

  const stat = fs.statSync(logPath);

  return {
    available: true,
    path: logPath,
    modifiedAt: stat.mtime.toISOString(),
    issues: game.parseLog(text).slice(0, 50),
  };
}

/**
 * Помощник поиска виновника методом половинного деления.
 *
 * Отключать моды по одному — это N запусков игры. Половинное деление
 * превращает их в log2(N): для тридцати модов пять запусков вместо тридцати.
 *
 * Состояние поиска хранится снаружи, здесь только чистая логика следующего шага.
 *
 * @param {string[]} suspects идентификаторы модов под подозрением
 * @param {{lastGroup: string[], brokeAgain: boolean}|null} lastResult
 * @returns {{done: boolean, culprit: string|null, enable: string[], disable: string[]}}
 */
function bisectStep(suspects, lastResult = null) {
  if (suspects.length === 0) {
    return { done: true, culprit: null, enable: [], disable: [] };
  }
  if (suspects.length === 1) {
    return { done: true, culprit: suspects[0], enable: [], disable: suspects };
  }

  let pool = suspects;

  if (lastResult) {
    // Сломалось снова — виновник среди включённых, иначе среди остальных.
    pool = lastResult.brokeAgain
      ? lastResult.lastGroup
      : suspects.filter((id) => !lastResult.lastGroup.includes(id));

    if (pool.length === 1) {
      return { done: true, culprit: pool[0], enable: [], disable: pool };
    }
    if (pool.length === 0) {
      return { done: true, culprit: null, enable: suspects, disable: [] };
    }
  }

  const half = pool.slice(0, Math.ceil(pool.length / 2));
  return {
    done: false,
    culprit: null,
    enable: half,
    disable: pool.filter((id) => !half.includes(id)),
    remaining: pool.length,
  };
}

module.exports = { launchGame, readIssues, bisectStep, splitArgs };
