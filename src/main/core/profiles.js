'use strict';

const { JsonStore } = require('./store');

/**
 * Профили модов — как в Vortex, r2modman и CurseForge (1.10).
 *
 * Профиль — это просто запомненный список «что включено». Моды не
 * копируются: включение и выключение в ModHub и так сделано переносом
 * папки (см. registry.js), поэтому переключить профиль — значит включить
 * одни папки и выключить другие. Быстро, обратимо и без дублей на диске.
 */

const MAX_PROFILES = 30;

function cleanName(value) {
  const name = String(value ?? '')
    .replace(/[\u0000-\u001f]/g, '')
    .trim()
    .slice(0, 40);
  // Имя — ключ объекта в JSON: служебные имена объектов не пускаем.
  return ['__proto__', 'constructor', 'prototype'].includes(name) ? '' : name;
}

class Profiles {
  /** @param {{file: string, now?: () => number}} options */
  constructor({ file, now = () => Date.now() }) {
    this.store = new JsonStore(file, { games: {}, active: {} });
    this.now = now;
  }

  _game(gameId) {
    if (!this.store.data.games[gameId]) this.store.data.games[gameId] = {};
    return this.store.data.games[gameId];
  }

  /** Профили игры, новые — первыми. */
  list(gameId) {
    const all = this.store.data.games[gameId] ?? {};
    return Object.entries(all)
      .map(([name, p]) => ({
        name,
        createdAt: p.createdAt,
        updatedAt: p.updatedAt ?? p.createdAt,
        count: p.enabled.length,
        active: this.store.data.active[gameId] === name,
      }))
      .sort((a, b) => String(b.updatedAt).localeCompare(String(a.updatedAt)));
  }

  get(gameId, name) {
    return this.store.data.games[gameId]?.[name] ?? null;
  }

  /**
   * Запомнить то, что сейчас включено, под именем.
   * @param {string} gameId
   * @param {string} rawName
   * @param {object[]} records записи реестра
   */
  save(gameId, rawName, records) {
    const name = cleanName(rawName);
    if (!name) throw Object.assign(new Error('empty profile name'), { code: 'PROFILE_NAME' });
    const game = this._game(gameId);
    if (!game[name] && Object.keys(game).length >= MAX_PROFILES) {
      throw Object.assign(new Error('too many profiles'), { code: 'PROFILE_LIMIT' });
    }
    const at = new Date(this.now()).toISOString();
    const enabled = (records ?? []).filter((r) => r.enabled !== false && !r.missing).map((r) => r.id);
    game[name] = { createdAt: game[name]?.createdAt ?? at, updatedAt: at, enabled };
    this.store.data.active[gameId] = name;
    this.store.save();
    return this.get(gameId, name);
  }

  /**
   * Включить моды профиля и выключить остальные.
   * @param {string} gameId
   * @param {string} name
   * @param {import('./registry').ModRegistry} registry
   * @returns {{enabled: number, disabled: number, missing: string[]}}
   */
  apply(gameId, name, registry) {
    const profile = this.get(gameId, name);
    if (!profile) throw Object.assign(new Error('no such profile'), { code: 'PROFILE_MISSING' });
    const wanted = new Set(profile.enabled);
    let enabled = 0;
    let disabled = 0;
    for (const mod of registry.list()) {
      const on = wanted.has(mod.id);
      if (mod.enabled === on) continue;
      registry.setEnabled(mod.id, on);
      if (on) enabled += 1;
      else disabled += 1;
    }
    // Моды, которые были в профиле, но с тех пор удалены.
    const missing = profile.enabled.filter((id) => !registry.has(id));
    this.store.data.active[gameId] = name;
    this.store.save();
    return { enabled, disabled, missing };
  }

  rename(gameId, from, rawTo) {
    const to = cleanName(rawTo);
    const game = this._game(gameId);
    if (!game[from] || !to) return false;
    if (to !== from) {
      game[to] = game[from];
      delete game[from];
      if (this.store.data.active[gameId] === from) this.store.data.active[gameId] = to;
      this.store.save();
    }
    return true;
  }

  remove(gameId, name) {
    const game = this._game(gameId);
    if (!game[name]) return false;
    delete game[name];
    if (this.store.data.active[gameId] === name) delete this.store.data.active[gameId];
    this.store.save();
    return true;
  }
}

module.exports = { Profiles, cleanName };
