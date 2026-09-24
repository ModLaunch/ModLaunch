'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { JsonStore } = require('./store');
const { t } = require('../i18n');

/**
 * Реестр установленных модов и включение/выключение.
 *
 * Выключение сделано переносом папки мода из Mods в собственное хранилище
 * ModHub рядом с игрой, а не удалением и не переименованием с точкой.
 * Причины: работает одинаково для любого загрузчика, полностью обратимо,
 * и человек, который откроет папку игры руками, увидит понятную картину,
 * а не следы чужой магии.
 *
 * Отсюда же почти бесплатно получаются профили: профиль — это просто список
 * того, что сейчас лежит в игре.
 */
class ModRegistry {
  /**
   * @param {string} dataDir папка данных ModHub
   * @param {string} gameId идентификатор игры
   * @param {{modsDir: string, storageDir: string, presetDir?: string}} paths
   *   presetDir — папка exe игры: туда ложатся пресеты ReShade (2.1)
   */
  constructor(dataDir, gameId, paths) {
    this.gameId = gameId;
    this.modsDir = paths.modsDir;
    this.storageDir = paths.storageDir;
    this.presetDir = paths.presetDir ?? path.dirname(paths.storageDir);
    this.store = new JsonStore(path.join(dataDir, 'games', `${gameId}.json`), { mods: {} });
  }

  /** @returns {object[]} список модов, отсортированный по имени */
  list() {
    return Object.values(this.store.data.mods).sort((a, b) =>
      a.name.localeCompare(b.name, 'ru')
    );
  }

  get(modId) {
    return this.store.data.mods[modId] ?? null;
  }

  has(modId) {
    return modId in this.store.data.mods;
  }

  /**
   * Записывает установленный мод.
   * @param {object} record
   */
  add(record) {
    this.store.data.mods[record.id] = {
      enabled: true,
      installedAt: new Date().toISOString(),
      ...record,
    };
    this.store.save();
    return this.store.data.mods[record.id];
  }

  /**
   * Каталог мода в текущем его состоянии (включён — в Mods, выключен — в хранилище).
   * Пресет ReShade (kind: preset) — это файл в папке игры, а не папка в Mods.
   */
  folderFor(mod) {
    return path.join(this.baseFor(mod, mod.enabled), mod.folder);
  }

  baseFor(mod, enabled) {
    if (mod.kind === 'preset') return enabled ? this.presetDir : path.join(this.storageDir, 'disabled-presets');
    return enabled ? this.modsDir : path.join(this.storageDir, 'disabled');
  }

  /**
   * Включает или выключает мод переносом его папки.
   * @param {string} modId
   * @param {boolean} enabled
   */
  setEnabled(modId, enabled) {
    const mod = this.get(modId);
    if (!mod) throw new Error(t('err.modNotFound', { id: modId }));
    if (mod.enabled === enabled) return mod;

    const from = this.folderFor(mod);
    const to = path.join(this.baseFor(mod, enabled), mod.folder);

    if (!fs.existsSync(from)) {
      // Папку унесли мимо ModHub — не падаем, а честно отмечаем расхождение.
      mod.enabled = enabled;
      mod.missing = true;
      this.store.save();
      return mod;
    }

    fs.mkdirSync(path.dirname(to), { recursive: true });
    fs.renameSync(from, to);

    mod.enabled = enabled;
    mod.missing = false;
    this.store.save();
    return mod;
  }

  /**
   * Удаляет мод с диска и из реестра.
   * @param {string} modId
   */
  remove(modId) {
    const mod = this.get(modId);
    if (!mod) return false;
    const folder = this.folderFor(mod);
    if (fs.existsSync(folder)) {
      fs.rmSync(folder, { recursive: true, force: true });
    }
    delete this.store.data.mods[modId];
    this.store.save();
    return true;
  }

  /**
   * Сверяет реестр с тем, что реально лежит на диске.
   * Пользователь мог поставить мод руками или удалить папку мимо ModHub —
   * интерфейс, который врёт про содержимое папки, хуже, чем его отсутствие.
   */
  reconcile() {
    let changed = false;
    for (const mod of Object.values(this.store.data.mods)) {
      const exists = fs.existsSync(this.folderFor(mod));
      if (mod.missing !== !exists) {
        mod.missing = !exists;
        changed = true;
      }
    }
    if (changed) this.store.save();
    return this.list();
  }

  /** Папки в Mods, о которых ModHub ничего не знает (поставлены вручную). */
  findUnmanaged() {
    if (!fs.existsSync(this.modsDir)) return [];
    const known = new Set(
      Object.values(this.store.data.mods)
        .filter((m) => m.enabled && m.kind !== 'preset')
        .map((m) => m.folder.toLowerCase())
    );
    return fs
      .readdirSync(this.modsDir, { withFileTypes: true })
      .filter((e) => e.isDirectory() && !e.name.startsWith('.'))
      .map((e) => e.name)
      .filter((name) => !known.has(name.toLowerCase()));
  }
}

module.exports = { ModRegistry };
