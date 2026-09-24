'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const { pipeline } = require('node:stream/promises');
const { Readable, PassThrough } = require('node:stream');
const { t } = require('../i18n');

let VERSION = '1.9.7';
try {
  VERSION = require('../../../package.json').version || VERSION;
} catch {
  /* версия нужна только для заголовка запроса */
}
const USER_AGENT = `ModLaunch/${VERSION}`;

/**
 * Скачивание файлов с отчётом о прогрессе и проверкой контрольной суммы.
 *
 * Опознаваемый User-Agent здесь не формальность: Modrinth требует его прямо
 * в правилах API, а Thunderstore и GitHub по нему отличают вменяемых клиентов
 * от скриптов, которые потом блокируют.
 *
 * Проверка контрольной суммы устроена подробнее, чем «совпало/не совпало».
 * Практика показала, почему: самая частая причина несовпадения — не подмена
 * файла, а оборванная закачка. Файл при этом выглядит нормальным, но короче,
 * и пользователю бесполезно сообщать «файл повреждён или подменён»: он ничего
 * не может с этим сделать. Поэтому здесь три вещи:
 *
 *   1. Размер сверяется с Content-Length — это отличает обрыв связи
 *      от действительно другого содержимого.
 *   2. При несовпадении закачка повторяется: обрывы обычно разовые.
 *   3. Сообщение об ошибке говорит, что именно случилось и что делать.
 */

const MAX_ATTEMPTS = 3;
const RETRY_DELAY_MS = 1200;

function tempDir() {
  const dir = path.join(os.tmpdir(), 'modhub-downloads');
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

function safeBaseName(urlString, fallback = 'download.bin') {
  try {
    const raw = path.basename(new URL(urlString).pathname);
    return decodeURIComponent(raw) || fallback;
  } catch {
    return fallback;
  }
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * @param {string} url
 * @param {object} [options]
 * @param {string} [options.fileName] имя файла назначения
 * @param {string} [options.sha256] ожидаемая контрольная сумма (hex)
 * @param {(progress: {received: number, total: number|null, ratio: number|null}) => void} [options.onProgress]
 * @param {Record<string,string>} [options.headers]
 * @param {number} [options.attempts] сколько раз пробовать при обрыве
 * @returns {Promise<string>} путь к скачанному файлу
 */
async function downloadFile(url, options = {}) {
  const maxAttempts = options.attempts ?? MAX_ATTEMPTS;
  let lastProblem = null;

  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    const result = await attemptDownload(url, options, attempt);

    if (result.ok) return result.file;

    lastProblem = result;
    fs.rmSync(result.file, { force: true });

    // Обрыв и несовпадение суммы лечатся повтором; отказ сервера — нет.
    if (attempt < maxAttempts) {
      options.onProgress?.({ received: 0, total: null, ratio: null, retrying: attempt });
      await sleep(RETRY_DELAY_MS * attempt);
    }
  }

  throw new Error(describeProblem(lastProblem, url, maxAttempts));
}

async function attemptDownload(url, options, attempt) {
  const response = await fetch(url, {
    headers: { 'User-Agent': USER_AGENT, ...(options.headers ?? {}) },
    redirect: 'follow',
  });

  if (!response.ok) {
    // Отказ сервера повторять бессмысленно — сообщаем сразу.
    throw new Error(
      t('dl.serverRefused', { status: response.status, statusText: response.statusText, url })
    );
  }

  const fileName = options.fileName || safeBaseName(response.url);
  const target = path.join(tempDir(), `${Date.now()}-${attempt}-${fileName}`);

  const lengthHeader = response.headers.get('content-length');
  const expectedSize = lengthHeader ? Number(lengthHeader) : null;

  // Прогресс считаем внутри конвейера, а не отдельным слушателем 'data':
  // подписка на 'data' переводит поток в текущий режим до того, как он
  // подключён к файлу, и это ровно тот способ потерять первые байты.
  const counter = new PassThrough();
  const hash = crypto.createHash('sha256');
  let received = 0;

  counter.on('data', (chunk) => {
    received += chunk.length;
    hash.update(chunk);
    options.onProgress?.({
      received,
      total: expectedSize,
      ratio: expectedSize ? received / expectedSize : null,
    });
  });

  await pipeline(Readable.fromWeb(response.body), counter, fs.createWriteStream(target));

  const actualSha = hash.digest('hex');

  if (expectedSize !== null && received !== expectedSize) {
    return { ok: false, kind: 'truncated', file: target, received, expectedSize };
  }

  if (options.sha256 && actualSha.toLowerCase() !== options.sha256.toLowerCase()) {
    return {
      ok: false,
      kind: 'mismatch',
      file: target,
      received,
      expectedSize,
      expectedSha: options.sha256.toLowerCase(),
      actualSha,
    };
  }

  return { ok: true, file: target, size: received, sha256: actualSha };
}

/** Превращает технический сбой в объяснение, с которым можно что-то сделать. */
function describeProblem(problem, url, attempts) {
  const host = (() => {
    try {
      return new URL(url).hostname;
    } catch {
      return url;
    }
  })();

  if (!problem) return t('dl.failed', { host });

  if (problem.kind === 'truncated') {
    return t('dl.truncated', {
      got: formatBytes(problem.received),
      want: formatBytes(problem.expectedSize),
      attempts,
      host,
    });
  }

  return t('dl.mismatch', {
    size: formatBytes(problem.received),
    expected: problem.expectedSha?.slice(0, 16),
    actual: problem.actualSha?.slice(0, 16),
    host,
  });
}

function formatBytes(bytes) {
  if (bytes === null || bytes === undefined) return t('bytes.unknown');
  if (bytes < 1024) return t('bytes.b', { n: bytes });
  if (bytes < 1024 * 1024) return t('bytes.kb', { n: (bytes / 1024).toFixed(0) });
  return t('bytes.mb', { n: (bytes / (1024 * 1024)).toFixed(1) });
}

function hashFile(filePath) {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    const stream = fs.createReadStream(filePath);
    stream.on('data', (chunk) => hash.update(chunk));
    stream.on('error', reject);
    stream.on('end', () => resolve(hash.digest('hex')));
  });
}

/** JSON-запрос с тем же User-Agent и разумным таймаутом. */
async function fetchJson(url, options = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), options.timeout ?? 20000);
  try {
    const response = await fetch(url, {
      headers: { 'User-Agent': USER_AGENT, Accept: 'application/json', ...(options.headers ?? {}) },
      signal: controller.signal,
    });
    if (!response.ok) throw new Error(`${response.status} ${response.statusText} — ${url}`);
    return await response.json();
  } finally {
    clearTimeout(timer);
  }
}

/** Текстовый запрос — нужен для XML-каталога Hollow Knight. */
async function fetchText(url, options = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), options.timeout ?? 20000);
  try {
    const response = await fetch(url, {
      headers: { 'User-Agent': USER_AGENT, ...(options.headers ?? {}) },
      signal: controller.signal,
    });
    if (!response.ok) throw new Error(`${response.status} ${response.statusText} — ${url}`);
    return await response.text();
  } finally {
    clearTimeout(timer);
  }
}

module.exports = { downloadFile, fetchJson, fetchText, hashFile, USER_AGENT, formatBytes };
