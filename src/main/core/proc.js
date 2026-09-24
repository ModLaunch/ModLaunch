'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');

/**
 * Запуск чужих установщиков с записью всего, что они сказали.
 *
 * Версия 1.2 запускала установщик SMAPI и в случае беды показывала только
 * то, что упало в stderr. А упало там вот что:
 *
 *   Unhandled exception. System.InvalidOperationException: Cannot read keys
 *   when either application does not have a console…
 *
 * Это не причина, а следствие: установщик SMAPI, наткнувшись на ошибку,
 * печатает объяснение и ждёт нажатия клавиши. Клавиатуры у него нет —
 * мы запустили его без консоли, — и он падает уже на ожидании. Настоящее
 * сообщение к этому моменту уже напечатано в stdout, и именно его мы
 * выбрасывали.
 *
 * Поэтому здесь: собираем оба потока, кладём всё в файл журнала целиком,
 * а человеку показываем то, что сказал сам установщик, отрезав предсмертную
 * трассировку .NET.
 */

/** Убирает управляющие последовательности цвета — в окне они выглядят мусором. */
function stripAnsi(text) {
  // eslint-disable-next-line no-control-regex
  return text.replace(/\u001b\[[0-9;]*m/g, '');
}

/**
 * Вытаскивает человеческое сообщение из вывода установщика.
 *
 * @param {string} output объединённые stdout и stderr
 * @returns {string}
 */
function meaningfulOutput(output) {
  const clean = stripAnsi(output).replace(/\r/g, '').trim();
  if (!clean) return '';

  // Всё, начиная с «Unhandled exception» — это падение на ожидании клавиши,
  // а не причина. Настоящее сообщение стоит выше.
  const crash = clean.search(/^Unhandled exception\./m);
  const head = (crash === -1 ? clean : clean.slice(0, crash)).trim();

  const lines = (head || clean)
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)
    // Строки трассировки ничего не объясняют человеку.
    .filter((line) => !/^at [\w.<>+]+\(/.test(line));

  return lines.slice(-14).join('\n').slice(0, 1200);
}

/**
 * Запускает программу, пишет её вывод в файл и возвращает результат.
 *
 * @param {string} command
 * @param {string[]} args
 * @param {{cwd?: string, timeout?: number, logFile?: string, header?: string}} options
 * @returns {Promise<{code: number|null, output: string, message: string, logFile: string|null}>}
 */
function runLogged(command, args, options = {}) {
  const { cwd, timeout = 180000, logFile = null, header = '' } = options;

  return new Promise((resolve, reject) => {
    let output = '';
    let finished = false;

    const child = spawn(command, args, {
      cwd,
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    const collect = (chunk) => {
      output += chunk.toString('utf8');
      // Лог не должен съесть память, если программа сошла с ума.
      if (output.length > 400000) output = output.slice(-400000);
    };

    child.stdout?.on('data', collect);
    child.stderr?.on('data', collect);

    const timer = setTimeout(() => {
      if (!finished) {
        finished = true;
        child.kill();
        resolve(finish(null, output + '\n[ModHub] превышено время ожидания'));
      }
    }, timeout);

    function finish(code, text) {
      clearTimeout(timer);
      if (logFile) {
        try {
          fs.mkdirSync(path.dirname(logFile), { recursive: true });
          fs.writeFileSync(
            logFile,
            `${header}\n${command} ${args.join(' ')}\n${new Date().toISOString()}\n\n${stripAnsi(text)}\n`,
            'utf8'
          );
        } catch {
          /* журнал — удобство, а не условие работы */
        }
      }
      return { code, output: text, message: meaningfulOutput(text), logFile };
    }

    child.once('error', (error) => {
      if (finished) return;
      finished = true;
      clearTimeout(timer);
      reject(error);
    });

    child.once('close', (code) => {
      if (finished) return;
      finished = true;
      resolve(finish(code, output));
    });
  });
}

module.exports = { runLogged, meaningfulOutput, stripAnsi };
