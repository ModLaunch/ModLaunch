'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');
const { t } = require('../i18n');

/**
 * Права на запись в папку игры.
 *
 * Игру часто ставят в Program Files, а туда Windows не пускает без прав
 * администратора. Загрузчик модов обязан класть файлы именно в папку игры,
 * поэтому упереться в это — вопрос времени.
 *
 * Важно проверять заранее, а не ловить ошибку на середине установки: к тому
 * моменту часть файлов уже может быть записана, и состояние игры окажется
 * промежуточным. Лучше сказать человеку до того, как что-то началось.
 */

/**
 * Реально ли мы можем писать в папку.
 *
 * fs.accessSync с W_OK на Windows верить нельзя: для защищённых папок он
 * регулярно отвечает «можно», а запись потом падает с EPERM. Единственная
 * надёжная проверка — попробовать создать файл и убрать за собой.
 *
 * @param {string} dir
 * @returns {boolean}
 */
function canWriteTo(dir) {
  if (!dir) return false;

  let target = dir;
  // Если самой папки ещё нет, проверяем ближайшего существующего родителя:
  // создавать её мы будем там же и с теми же правами.
  while (target && !fs.existsSync(target)) {
    const parent = path.dirname(target);
    if (parent === target) return false;
    target = parent;
  }

  const probe = path.join(target, `.modhub-write-test-${process.pid}-${Date.now()}`);
  try {
    fs.writeFileSync(probe, '');
    fs.rmSync(probe, { force: true });
    return true;
  } catch {
    return false;
  }
}

/**
 * Проверяет сразу несколько папок и возвращает первую недоступную.
 * Проверять только корень игры недостаточно: у Hollow Knight файлы пишутся
 * в Managed внутри неё, и права там могут отличаться от прав на корень.
 *
 * @param {string[]} dirs
 * @returns {{ok: boolean, blocked: string|null}}
 */
function checkWriteTargets(dirs) {
  for (const dir of dirs.filter(Boolean)) {
    if (!canWriteTo(dir)) return { ok: false, blocked: dir };
  }
  return { ok: true, blocked: null };
}

/**
 * Запущены ли мы с правами администратора.
 *
 * Надёжных API для этого в Node нет, поэтому проверяем делом: пробуем создать
 * файл в системной папке Windows. Обычному пользователю туда нельзя.
 */
function isElevated() {
  if (process.platform !== 'win32') {
    return typeof process.getuid === 'function' ? process.getuid() === 0 : false;
  }
  const systemRoot = process.env.SystemRoot || 'C:\\Windows';
  const probe = path.join(systemRoot, `modhub-elev-${process.pid}.tmp`);
  try {
    fs.writeFileSync(probe, '');
    fs.rmSync(probe, { force: true });
    return true;
  } catch {
    return false;
  }
}

/** Похоже ли, что папка лежит в защищённом системном месте. */
function isProtectedLocation(dir) {
  if (process.platform !== 'win32' || !dir) return false;
  const lower = dir.toLowerCase();
  return (
    lower.includes('\\program files') ||
    lower.includes('\\windows\\') ||
    lower.startsWith(path.join(os.homedir(), 'AppData', 'Local', 'Temp').toLowerCase())
  );
}

/**
 * Превращает системную ошибку доступа в объяснение для человека.
 * Возвращает null, если ошибка не про права — тогда её надо показывать как есть.
 *
 * @param {Error & {code?: string, path?: string}} error
 * @param {string} [gamePath]
 * @returns {string|null}
 */
function explainAccessError(error, gamePath) {
  const code = error?.code;
  if (code !== 'EPERM' && code !== 'EACCES' && code !== 'EBUSY') return null;

  const where = gamePath || error.path || '';

  if (code === 'EBUSY') return t('err.busy');

  const inProtected = isProtectedLocation(where);

  return (
    t('err.noAccess.head') +
    (inProtected
      ? t('err.noAccess.protected', { where: path.dirname(where) || where })
      : t('err.noAccess.plain')) +
    t('err.noAccess.tail')
  );
}

/**
 * Метка в аргументах: этот запуск — повышение прав, а не обычный старт.
 *
 * Новый экземпляр по ней понимает, что предыдущий сейчас закрывается, и что
 * замок единственного экземпляра стоит подождать, а не сдаваться сразу.
 */
const RELAUNCH_FLAG = '--modhub-elevated-relaunch';

/**
 * Перезапускает приложение с запросом прав администратора.
 *
 * Повысить права у уже запущенного процесса нельзя — только запустить новый.
 * Делаем это через Start-Process с -Verb RunAs: именно он показывает штатное
 * окно подтверждения Windows, а не пытается его обойти.
 *
 * Возвращает промис, который выполняется, когда процесс-посредник реально
 * стартовал. Раньше здесь был просто spawn без ожидания: если запустить его
 * не удавалось, приложение всё равно закрывалось, и со стороны это выглядело
 * как «нажал кнопку — программа исчезла». Теперь ошибку видно.
 *
 * @param {{execPath: string, args: string[]}} target
 * @returns {Promise<void>}
 */
function relaunchElevated(target) {
  if (process.platform !== 'win32') {
    return Promise.reject(new Error('Перезапуск с правами администратора доступен только в Windows.'));
  }

  // В PowerShell строка в одинарных кавычках экранируется удвоением кавычки.
  const quote = (value) => `'${String(value).replace(/'/g, "''")}'`;

  const args = Array.isArray(target.args) ? [...target.args] : [];
  if (!args.includes(RELAUNCH_FLAG)) args.push(RELAUNCH_FLAG);

  const parts = [
    `-FilePath ${quote(target.execPath)}`,
    `-ArgumentList ${args.map(quote).join(',')}`,
    '-Verb RunAs',
  ];

  return new Promise((resolve, reject) => {
    const child = spawn(
      'powershell.exe',
      ['-NoProfile', '-WindowStyle', 'Hidden', '-Command', `Start-Process ${parts.join(' ')}`],
      { detached: true, stdio: 'ignore', windowsHide: true }
    );
    child.once('error', reject);
    child.once('spawn', () => {
      child.unref();
      resolve();
    });
  });
}

/**
 * Бросает понятную ошибку до того, как что-либо начнёт записываться.
 * Загрузчики вызывают её первым делом: остановиться до первой записи лучше,
 * чем остановиться на середине и оставить игру в промежуточном состоянии.
 *
 * @param {string[]} dirs папки, в которые предстоит писать
 * @param {string} [gamePath]
 */
function assertWritable(dirs, gamePath) {
  const { ok, blocked } = checkWriteTargets(dirs);
  if (ok) return;

  const error = new Error(
    explainAccessError(Object.assign(new Error('EPERM'), { code: 'EPERM', path: blocked }), gamePath || blocked)
  );
  error.code = 'EPERM';
  error.path = blocked;
  error.alreadyExplained = true;
  throw error;
}

module.exports = {
  RELAUNCH_FLAG,
  canWriteTo,
  checkWriteTargets,
  isElevated,
  isProtectedLocation,
  explainAccessError,
  assertWritable,
  relaunchElevated,
};
