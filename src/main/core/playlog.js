'use strict';

const fs = require('node:fs');
const { JsonStore } = require('./store');

/**
 * «Скачал и поиграл» — пропуск к оценке мода (1.9.7).
 *
 * Оценку моду может поставить только тот, кто скачал его через ModHub
 * и хотя бы раз запускал с ним игру. Иначе средняя оценка — это мнение
 * людей, которые мод видели на картинке.
 *
 * Как ModHub узнаёт, что с модом играли — двумя способами, и хватает
 * любого из них:
 *
 *   • Игру запустили кнопкой «Играть» в ModHub. Всё, что в этот момент
 *     стояло и было включено, отмечается как «сыграно».
 *
 *   • Игру запустили мимо ModHub — из Steam, ярлыком, из папки. Тогда
 *     свидетель — лог загрузчика модов (SMAPI, BepInEx, Modding API):
 *     загрузчик переписывает его при каждом запуске игры. Лог изменён
 *     позже, чем поставлен мод, — значит, игра с этим модом запускалась.
 *
 * Отметка «сыграно» живёт в plays.json и переживает удаление мода:
 * поиграл, не понравилось, удалил — оценить (и поставить единицу) всё
 * равно можно. Именно такие отзывы самые полезные.
 */

/** Номер мода в каталоге по записи реестра: так его знают отзывы. */
function catalogIdOf(game, record) {
  const id = String(record?.id ?? '');
  const domain = game?.catalog?.nexusDomain;
  if (domain && id.startsWith(`nexus:${domain}:`)) return id.slice(`nexus:${domain}:`.length);
  return id;
}

function timeOf(value) {
  const ms = typeof value === 'number' ? value : Date.parse(value ?? '');
  return Number.isFinite(ms) ? ms : null;
}

class PlayLog {
  /**
   * @param {object} options
   * @param {string} options.file путь к plays.json
   * @param {() => number} [options.now]
   * @param {(file: string) => {mtimeMs: number}} [options.stat]
   */
  constructor({ file, now = () => Date.now(), stat = (target) => fs.statSync(target) } = {}) {
    this.store = new JsonStore(file, { mods: {}, launches: {} });
    this.now = now;
    this.stat = stat;
  }

  key(gameId, modId) {
    return `${gameId}|${modId}`;
  }

  /** Стоит и включён: только такой мод загружается вместе с игрой. */
  static active(record) {
    return Boolean(record) && record.enabled !== false && !record.missing;
  }

  /**
   * Отмечает моды как «сыграно».
   * @param {object} game адаптер игры
   * @param {object[]} records записи реестра
   * @param {{at: number, how: string, installedBefore?: number}} mark
   * @returns {string[]} номера модов, отмеченных впервые
   */
  mark(game, records, { at, how, installedBefore = at }) {
    const fresh = [];
    for (const record of records ?? []) {
      if (!PlayLog.active(record)) continue;
      const installed = timeOf(record.installedAt);
      // Без даты установки (моды из самых старых версий) верим только
      // запуску из ModHub: там мод точно стоял в момент запуска.
      if (installed === null ? how !== 'launch' : installed >= installedBefore) continue;
      const modId = catalogIdOf(game, record);
      const key = this.key(game.id, modId);
      if (this.store.data.mods[key]?.playedAt) continue;
      this.store.data.mods[key] = { playedAt: new Date(at).toISOString(), how };
      fresh.push(modId);
    }
    if (fresh.length) this.store.save();
    return fresh;
  }

  /** Игру запустили кнопкой «Играть» в ModHub. */
  noteLaunch(game, records) {
    const at = this.now();
    this.store.data.launches[game.id] = new Date(at).toISOString();
    const fresh = this.mark(game, records, { at, how: 'launch', installedBefore: at + 1 });
    this.store.save();
    return fresh;
  }

  /**
   * Игру запускали мимо ModHub: смотрим, когда загрузчик последний раз
   * писал свой лог. Лога нет — молча ничего не отмечаем.
   */
  observeLog(game, records, logPath) {
    if (!logPath) return [];
    let modified;
    try {
      modified = this.stat(logPath).mtimeMs;
    } catch {
      return [];
    }
    if (!Number.isFinite(modified) || modified <= 0) return [];
    // Лог «из будущего» (сбитые часы) ничего не доказывает.
    if (modified > this.now() + 60_000) return [];
    return this.mark(game, records, { at: modified, how: 'log', installedBefore: modified });
  }

  /** Играли ли с этим модом хоть раз. */
  played(gameId, modId) {
    return this.store.data.mods[this.key(gameId, modId)] ?? null;
  }

  /**
   * Всё, что нужно окну, чтобы показать шаги «скачайте → сыграйте».
   * @param {string} gameId
   * @param {string} modId номер мода в каталоге
   * @param {object|null} record запись реестра, если мод стоит
   */
  status(gameId, modId, record) {
    const played = this.played(gameId, modId);
    return {
      installed: Boolean(record) && !record.missing,
      enabled: Boolean(record) && record.enabled !== false && !record.missing,
      installedId: record?.id ?? null,
      played: Boolean(played?.playedAt),
      playedAt: played?.playedAt ?? null,
      lastLaunch: this.store.data.launches[gameId] ?? null,
    };
  }
}

module.exports = { PlayLog, catalogIdOf };
