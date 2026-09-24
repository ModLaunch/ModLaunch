'use strict';

const fs = require('node:fs');
const path = require('node:path');
const AdmZip = require('adm-zip');

/**
 * Резервные копии сохранений — как облачные сохранения GOG Galaxy
 * и резервные копии Playnite, только на своём диске (1.10).
 *
 * Моды чаще всего ломают не игру, а сохранение: новый мод дописал в него
 * свои предметы, мод удалили — и файл больше не открывается. Поэтому
 * ModHub умеет копировать папку сохранений в zip — по кнопке и сам перед
 * каждым запуском игры, — и возвращать любую из копий обратно.
 *
 * Копии лежат в данных ModHub: <данные>/backups/<игра>/<время>-<повод>.zip.
 * Старые удаляются, когда их становится больше, чем задано в настройках.
 */

const REASONS = new Set(['manual', 'launch', 'restore']);
const FILE = /^(\d{8}-\d{6})-(manual|launch|restore)\.zip$/;

function stamp(date) {
  const p = (n) => String(n).padStart(2, '0');
  return (
    `${date.getFullYear()}${p(date.getMonth() + 1)}${p(date.getDate())}-` +
    `${p(date.getHours())}${p(date.getMinutes())}${p(date.getSeconds())}`
  );
}

/** Размер папки: показываем его рядом с кнопкой, чтобы копия не была сюрпризом. */
function folderSize(dir) {
  let total = 0;
  let files = 0;
  const walk = (current) => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile()) {
        total += fs.statSync(full).size;
        files += 1;
      }
    }
  };
  walk(dir);
  return { bytes: total, files };
}

class Backups {
  /** @param {{dir: string, now?: () => Date}} options */
  constructor({ dir, now = () => new Date() }) {
    this.dir = dir;
    this.now = now;
  }

  folderFor(gameId) {
    if (!/^[a-z0-9-]+$/.test(String(gameId))) throw new Error('bad game id');
    return path.join(this.dir, gameId);
  }

  /** Имя файла копии → полный путь, только внутри папки игры. */
  _file(gameId, name) {
    if (!FILE.test(String(name))) throw Object.assign(new Error('bad backup name'), { code: 'BACKUP_MISSING' });
    const full = path.join(this.folderFor(gameId), name);
    if (!fs.existsSync(full)) throw Object.assign(new Error('no such backup'), { code: 'BACKUP_MISSING' });
    return full;
  }

  /** Что есть в папке сохранений сейчас. */
  describe(savesDir) {
    if (!savesDir || !fs.existsSync(savesDir)) return { exists: false, path: savesDir ?? null, bytes: 0, files: 0 };
    return { exists: true, path: savesDir, ...folderSize(savesDir) };
  }

  /** Копии игры, новые — первыми. */
  list(gameId) {
    const folder = this.folderFor(gameId);
    if (!fs.existsSync(folder)) return [];
    return fs
      .readdirSync(folder)
      .map((name) => {
        const match = FILE.exec(name);
        if (!match) return null;
        const [, when, reason] = match;
        const at = new Date(
          `${when.slice(0, 4)}-${when.slice(4, 6)}-${when.slice(6, 8)}T${when.slice(9, 11)}:${when.slice(11, 13)}:${when.slice(13, 15)}`
        );
        const { size } = fs.statSync(path.join(folder, name));
        return { name, at: at.toISOString(), reason, size };
      })
      .filter(Boolean)
      .sort((a, b) => b.name.localeCompare(a.name));
  }

  /**
   * Скопировать папку сохранений в zip.
   * @returns {object|null} запись о копии; null — сохранений ещё нет
   */
  create(gameId, savesDir, reason = 'manual', { keep = 10 } = {}) {
    if (!REASONS.has(reason)) reason = 'manual';
    if (!savesDir || !fs.existsSync(savesDir)) return null;
    if (fs.readdirSync(savesDir).length === 0) return null;

    const folder = this.folderFor(gameId);
    fs.mkdirSync(folder, { recursive: true });

    // Две копии в одну секунду (кнопку нажали дважды) — не затираем первую.
    let base = stamp(this.now());
    let name = `${base}-${reason}.zip`;
    for (let i = 1; fs.existsSync(path.join(folder, name)) && i < 60; i += 1) {
      const next = new Date(this.now().getTime() + i * 1000);
      base = stamp(next);
      name = `${base}-${reason}.zip`;
    }

    const zip = new AdmZip();
    zip.addLocalFolder(savesDir);
    const tmp = path.join(folder, name + '.tmp');
    zip.writeZip(tmp);
    fs.renameSync(tmp, path.join(folder, name));

    this.prune(gameId, keep);
    return this.list(gameId).find((b) => b.name === name) ?? null;
  }

  /** Оставить только keep последних копий. Копии «перед восстановлением» живут наравне со всеми. */
  prune(gameId, keep = 10) {
    const limit = Math.max(1, Math.min(200, Number(keep) || 10));
    const all = this.list(gameId);
    for (const old of all.slice(limit)) {
      fs.rmSync(path.join(this.folderFor(gameId), old.name), { force: true });
    }
    return Math.max(0, all.length - limit);
  }

  /**
   * Вернуть копию на место. Сначала — копия того, что лежит сейчас:
   * восстановление тоже можно отменить.
   */
  restore(gameId, name, savesDir, { keep = 10 } = {}) {
    const file = this._file(gameId, name);
    if (!savesDir) throw Object.assign(new Error('no saves dir'), { code: 'BACKUP_NO_SAVES' });

    // Проверяем архив до того, как трогать папку: битый zip не должен
    // оставить человека вовсе без сохранений.
    const zip = new AdmZip(file);
    const entries = zip.getEntries();
    for (const entry of entries) {
      const target = path.resolve(savesDir, entry.entryName);
      if (!target.startsWith(path.resolve(savesDir) + path.sep) && target !== path.resolve(savesDir)) {
        throw Object.assign(new Error('unsafe entry'), { code: 'BACKUP_BROKEN' });
      }
    }

    // Архив уже прочитан в память: копия «перед восстановлением» может
    // вытеснить старые файлы, и восстанавливаемый среди них не пострадает.
    const safety = this.create(gameId, savesDir, 'restore', { keep: keep + 1 });

    fs.mkdirSync(savesDir, { recursive: true });
    for (const entry of fs.readdirSync(savesDir)) {
      fs.rmSync(path.join(savesDir, entry), { recursive: true, force: true });
    }
    zip.extractAllTo(savesDir, true);
    return { restored: name, safety: safety?.name ?? null };
  }

  remove(gameId, name) {
    fs.rmSync(this._file(gameId, name), { force: true });
    return true;
  }
}

module.exports = { Backups, stamp };
