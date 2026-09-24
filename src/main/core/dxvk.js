'use strict';

const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');
const { downloadFile, fetchJson } = require('./download');

/**
 * DXVK в один клик (3.1).
 *
 * DXVK переводит DirectX 8–11 на Vulkan. В старых играх это часто убирает
 * фризы и поднимает FPS. Ставится он просто — нужные DLL кладутся рядом
 * с exe игры, — но руками в этом легко ошибиться: взять 32-битные файлы
 * для 64-битной игры, положить dxgi.dll в игру на DirectX 9 или затереть
 * собственную DLL игры. Здесь всё это делается само:
 *
 *   1. По заголовку exe узнаём разрядность, по таблице импорта — DirectX.
 *      Движок часто грузит DirectX не из exe, а из своей DLL рядом
 *      (UnityPlayer.dll, engine.dll) — смотрим и их.
 *   2. Скачиваем свежий DXVK с GitHub (проверяем sha256, который дал GitHub)
 *      и берём из архива только нужные DLL нужной разрядности.
 *   3. Если в папке уже лежит файл с тем же именем, он уходит в резервную
 *      копию и возвращается при удалении DXVK.
 *   4. В папке игры остаётся отметка dxvk.modlaunch.json: что поставили
 *      и что сохранили — по ней DXVK убирается без следов.
 *
 * ReShade для DirectX 10–11 сам лежит как dxgi.dll, и вместе с DXVK так
 * не работает — в этом случае честно говорим об этом и ничего не трогаем.
 */

const RELEASES = 'https://api.github.com/repos/doitsujin/dxvk/releases/latest';
const MARKER = 'dxvk.modlaunch.json';
const BACKUP_SUFFIX = '.modlaunch-backup';

/** Какие DLL нужны для какого DirectX. */
const FILES = {
  dx8: ['d3d8.dll', 'd3d9.dll'],
  dx9: ['d3d9.dll'],
  dx10: ['d3d10core.dll', 'd3d11.dll', 'dxgi.dll'],
  dx11: ['d3d10core.dll', 'd3d11.dll', 'dxgi.dll'],
};
const APIS = Object.keys(FILES);

/* --- PE: разрядность и импорты ---------------------------------------- */

function readAt(fd, offset, length) {
  const buffer = Buffer.alloc(length);
  const read = fs.readSync(fd, buffer, 0, length, offset);
  return read === length ? buffer : buffer.subarray(0, read);
}

/**
 * Разрядность и список импортируемых DLL у exe или dll.
 * Читаем только заголовки и таблицу импорта, а не весь файл.
 * @returns {{arch: 'x86'|'x64', imports: string[]}|null}
 */
function peInfo(file) {
  let fd;
  try {
    fd = fs.openSync(file, 'r');
    const dos = readAt(fd, 0, 64);
    if (dos.length < 64 || dos.toString('latin1', 0, 2) !== 'MZ') return null;
    const peOffset = dos.readUInt32LE(0x3c);
    const head = readAt(fd, peOffset, 24);
    if (head.length < 24 || head.toString('latin1', 0, 4) !== 'PE\0\0') return null;
    const machine = head.readUInt16LE(4);
    const sections = head.readUInt16LE(6);
    const optSize = head.readUInt16LE(20);
    const opt = readAt(fd, peOffset + 24, optSize);
    const magic = opt.readUInt16LE(0);
    const arch = machine === 0x8664 || magic === 0x20b ? 'x64' : machine === 0x14c ? 'x86' : null;
    if (!arch) return null;

    // Каталог данных: у PE32 с 96-го байта, у PE32+ — со 112-го. Импорт — второй.
    const dirs = magic === 0x20b ? 112 : 96;
    const importRva = opt.length >= dirs + 16 ? opt.readUInt32LE(dirs + 8) : 0;
    const table = readAt(fd, peOffset + 24 + optSize, sections * 40);
    const toOffset = (rva) => {
      for (let i = 0; i < sections; i++) {
        const s = i * 40;
        const va = table.readUInt32LE(s + 12);
        const size = Math.max(table.readUInt32LE(s + 8), table.readUInt32LE(s + 16));
        if (rva >= va && rva < va + size) return rva - va + table.readUInt32LE(s + 20);
      }
      return null;
    };

    const imports = [];
    const at = importRva ? toOffset(importRva) : null;
    if (at != null) {
      for (let i = 0; i < 512; i++) {
        const desc = readAt(fd, at + i * 20, 20);
        if (desc.length < 20 || desc.every((b) => b === 0)) break;
        const nameAt = toOffset(desc.readUInt32LE(12));
        if (nameAt == null) continue;
        const raw = readAt(fd, nameAt, 64).toString('latin1');
        const name = raw.slice(0, raw.indexOf('\0') === -1 ? raw.length : raw.indexOf('\0')).toLowerCase();
        if (name) imports.push(name);
      }
    }
    return { arch, imports };
  } catch {
    return null;
  } finally {
    if (fd !== undefined) fs.closeSync(fd);
  }
}

/** DirectX по списку импортов. null — не поняли (или это не DirectX 8–11). */
function apiFromImports(imports) {
  const has = (name) => imports.includes(name);
  if (has('d3d12.dll')) return 'dx12';
  if (has('d3d11.dll') || has('dxgi.dll')) return 'dx11';
  if (has('d3d10.dll') || has('d3d10_1.dll') || has('d3d10core.dll')) return 'dx10';
  if (has('d3d9.dll')) return 'dx9';
  if (has('d3d8.dll')) return 'dx8';
  if (has('opengl32.dll')) return 'opengl';
  if (has('vulkan-1.dll')) return 'vulkan';
  return null;
}

/**
 * Что за игра: разрядность exe и её DirectX.
 * @returns {{exe: string, dir: string, name: string, arch: string, api: string|null, supported: boolean}}
 */
function inspect(exe) {
  const info = peInfo(exe);
  if (!info) throw Object.assign(new Error('not an exe'), { code: 'DXVK_NOT_EXE' });
  const dir = path.dirname(exe);
  let api = apiFromImports(info.imports);

  // Движок нередко грузит DirectX из своей DLL рядом с exe.
  if (!api || api === 'opengl') {
    let entries = [];
    try {
      entries = fs.readdirSync(dir).filter((n) => /\.dll$/i.test(n)).slice(0, 80);
    } catch {
      /* нет доступа к папке — останемся с тем, что знаем */
    }
    for (const name of entries) {
      if (FILES.dx11.includes(name.toLowerCase()) || /^d3d[89]\.dll$/i.test(name)) continue;
      const found = apiFromImports(peInfo(path.join(dir, name))?.imports ?? []);
      if (found && found !== 'opengl' && found !== 'vulkan') {
        api = found;
        break;
      }
      if (found && !api) api = found;
    }
  }

  return {
    exe,
    dir,
    name: gameName(exe),
    arch: info.arch,
    api,
    supported: APIS.includes(api),
  };
}

/** Имя игры для списка: папка, где лежит exe (без «bin», «x64» и т. п.). */
function gameName(exe) {
  let dir = path.dirname(exe);
  while (/^(bin(32|64)?|x64|x86|win(32|64)|binaries|release|game)$/i.test(path.basename(dir))) {
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return path.basename(dir) || path.basename(exe, path.extname(exe));
}

/* --- tar.gz ----------------------------------------------------------- */

/**
 * Файлы из tar.gz (DXVK выпускается так). Нужны только обычные файлы;
 * длинные имена бывают в записях GNU «L» и pax «x» — их тоже понимаем.
 * @returns {Map<string, Buffer>} путь → содержимое
 */
function readTarGz(buffer) {
  const tar = zlib.gunzipSync(buffer);
  const out = new Map();
  let offset = 0;
  let longName = null;
  while (offset + 512 <= tar.length) {
    const header = tar.subarray(offset, offset + 512);
    if (header.every((b) => b === 0)) break;
    const field = (start, length) => {
      const raw = header.toString('utf8', start, start + length);
      const end = raw.indexOf('\0');
      return end === -1 ? raw : raw.slice(0, end);
    };
    const size = parseInt(field(124, 12).trim() || '0', 8) || 0;
    const type = field(156, 1) || '0';
    const prefix = field(345, 155);
    let name = longName ?? (prefix ? `${prefix}/${field(0, 100)}` : field(0, 100));
    longName = null;
    const body = tar.subarray(offset + 512, offset + 512 + size);
    if (type === 'L') longName = body.toString('utf8').replace(/\0+$/, '');
    else if (type === 'x') {
      const pathLine = /\d+ path=([^\n]+)\n/.exec(body.toString('utf8'));
      if (pathLine) longName = pathLine[1];
    } else if (type === '0' || type === '\0') out.set(name.replace(/^\.\//, ''), Buffer.from(body));
    offset += 512 + Math.ceil(size / 512) * 512;
  }
  return out;
}

/** Это ReShade под именем системной DLL (dxgi.dll, d3d9.dll…)? */
function isReShade(file) {
  try {
    if (!fs.statSync(file).isFile()) return false;
    const text = fs.readFileSync(file).toString('latin1');
    return text.includes('crosire') || text.includes('ReShade');
  } catch {
    return false;
  }
}

/* --- установка -------------------------------------------------------- */

class Dxvk {
  /**
   * @param {object} [options]
   * @param {string} [options.cacheDir] где держать скачанный архив
   * @param {Function} [options.download]
   * @param {Function} [options.getJson]
   */
  constructor({ cacheDir, download = downloadFile, getJson = fetchJson } = {}) {
    this.cacheDir = cacheDir;
    this.download = download;
    this.getJson = getJson;
  }

  /** Стоит ли DXVK в этой папке (по нашей отметке). */
  status(dir) {
    try {
      const data = JSON.parse(fs.readFileSync(path.join(dir, MARKER), 'utf8'));
      return { installed: true, version: data.version ?? '', arch: data.arch, api: data.api, files: data.files ?? [], installedAt: data.installedAt ?? null };
    } catch {
      return { installed: false };
    }
  }

  /** Свежий выпуск DXVK: версия, ссылка, sha256. */
  async latest() {
    const data = await this.getJson(RELEASES, { timeout: 20000, headers: { Accept: 'application/vnd.github+json' } });
    const asset = (data?.assets ?? []).find((a) => /^dxvk-[\d.]+\.tar\.gz$/i.test(a?.name ?? ''));
    if (!asset) throw Object.assign(new Error('no dxvk asset'), { code: 'DXVK_DOWNLOAD' });
    const sha256 = /^sha256:([0-9a-f]{64})$/i.exec(String(asset.digest ?? ''))?.[1] ?? undefined;
    return { version: String(data.tag_name ?? '').replace(/^v/i, ''), url: asset.browser_download_url, name: asset.name, sha256 };
  }

  /** Архив DXVK: из кэша, если эта версия уже скачана. */
  async archive(onStage = () => {}) {
    const release = await this.latest();
    const cached = this.cacheDir ? path.join(this.cacheDir, release.name) : null;
    if (cached && fs.existsSync(cached)) return { release, file: cached };
    onStage({ stage: 'download', ratio: 0 });
    const file = await this.download(release.url, {
      fileName: release.name,
      sha256: release.sha256,
      onProgress: (p) => onStage({ stage: 'download', ratio: p.ratio }),
    });
    if (!cached) return { release, file };
    fs.mkdirSync(this.cacheDir, { recursive: true });
    fs.copyFileSync(file, cached);
    fs.rmSync(file, { force: true });
    return { release, file: cached };
  }

  /**
   * Поставить DXVK рядом с exe.
   * @param {string} exe
   * @param {{api?: string}} [options] DirectX, если его не удалось определить самим
   */
  async install(exe, { api: chosen } = {}, onStage = () => {}) {
    const game = inspect(exe);
    const api = APIS.includes(chosen) ? chosen : game.api;
    if (!APIS.includes(api)) throw Object.assign(new Error(`unsupported api ${api}`), { code: 'DXVK_API', api: game.api });
    if (this.status(game.dir).installed) this.remove(game.dir);

    const names = FILES[api];
    if (names.some((n) => isReShade(path.join(game.dir, n)))) {
      throw Object.assign(new Error('reshade conflict'), { code: 'DXVK_RESHADE' });
    }

    const { release, file } = await this.archive(onStage);
    onStage({ stage: 'install' });
    const entries = readTarGz(fs.readFileSync(file));
    const folder = game.arch === 'x64' ? 'x64' : 'x32';
    const pick = (name) => [...entries.entries()].find(([p]) => p.toLowerCase().endsWith(`/${folder}/${name}`))?.[1];
    const files = names.filter((n) => pick(n));
    if (!files.length) throw Object.assign(new Error('dxvk archive has no dlls'), { code: 'DXVK_DOWNLOAD' });

    const backups = [];
    const written = [];
    try {
      for (const name of files) {
        const target = path.join(game.dir, name);
        if (fs.existsSync(target)) {
          fs.renameSync(target, target + BACKUP_SUFFIX);
          backups.push(name);
        }
        fs.writeFileSync(target, pick(name));
        written.push(name);
      }
      fs.writeFileSync(
        path.join(game.dir, MARKER),
        JSON.stringify({ version: release.version, arch: game.arch, api, files: written, backups, exe: path.basename(exe), installedAt: new Date().toISOString() }, null, 2)
      );
    } catch (error) {
      // Не оставляем игру наполовину переделанной.
      for (const name of written) fs.rmSync(path.join(game.dir, name), { force: true });
      for (const name of backups) {
        const target = path.join(game.dir, name);
        if (fs.existsSync(target + BACKUP_SUFFIX)) fs.renameSync(target + BACKUP_SUFFIX, target);
      }
      throw error;
    }
    return { ...game, api, version: release.version, files: written, backups };
  }

  /** Убрать DXVK и вернуть то, что лежало на месте его файлов. */
  remove(dir) {
    const marker = path.join(dir, MARKER);
    let data;
    try {
      data = JSON.parse(fs.readFileSync(marker, 'utf8'));
    } catch {
      return { removed: false };
    }
    for (const name of data.files ?? []) {
      if (!FILES.dx8.concat(FILES.dx11).includes(String(name).toLowerCase())) continue;
      fs.rmSync(path.join(dir, name), { force: true });
    }
    for (const name of data.backups ?? []) {
      const target = path.join(dir, path.basename(String(name)));
      if (fs.existsSync(target + BACKUP_SUFFIX)) fs.renameSync(target + BACKUP_SUFFIX, target);
    }
    fs.rmSync(marker, { force: true });
    return { removed: true };
  }
}

module.exports = { Dxvk, peInfo, apiFromImports, inspect, readTarGz, gameName, isReShade, FILES, MARKER };
