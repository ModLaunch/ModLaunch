'use strict';

const { JsonStore } = require('./store');

/**
 * Учёт игрового времени — как в Steam, GOG Galaxy и Playnite (1.10).
 *
 * Считается то, что запущено кнопкой «Играть» в ModHub: от старта процесса
 * игры до его завершения. Часть игр при запуске мимо Steam перезапускает
 * себя через Steam — такой процесс живёт пару секунд, и эти секунды за
 * сеанс не считаются: лучше недосчитать, чем записать нули и мусор.
 */

const MIN_SESSION_MS = 15_000;
/** Сеанс дольше суток — скорее всего спящий компьютер или сбитые часы. */
const MAX_SESSION_MS = 24 * 60 * 60 * 1000;

class PlayTime {
  /** @param {{file: string, now?: () => number}} options */
  constructor({ file, now = () => Date.now() }) {
    this.store = new JsonStore(file, { games: {} });
    this.now = now;
    this.running = new Map(); // gameId -> startedAt
  }

  /** Игра запущена. */
  start(gameId) {
    const at = this.now();
    this.running.set(gameId, at);
    const game = this._game(gameId);
    game.lastPlayed = new Date(at).toISOString();
    this.store.save();
    return at;
  }

  /**
   * Процесс игры завершился.
   * @returns {{counted: boolean, ms: number}}
   */
  stop(gameId) {
    const startedAt = this.running.get(gameId);
    if (startedAt === undefined) return { counted: false, ms: 0 };
    this.running.delete(gameId);
    const ms = this.now() - startedAt;
    if (ms < MIN_SESSION_MS || ms > MAX_SESSION_MS) return { counted: false, ms };
    const game = this._game(gameId);
    game.totalMs += ms;
    game.sessions += 1;
    game.lastSessionMs = ms;
    game.longestMs = Math.max(game.longestMs ?? 0, ms);
    this.store.save();
    return { counted: true, ms };
  }

  isRunning(gameId) {
    return this.running.has(gameId);
  }

  _game(gameId) {
    if (!this.store.data.games[gameId]) {
      this.store.data.games[gameId] = { totalMs: 0, sessions: 0, lastPlayed: null, lastSessionMs: 0, longestMs: 0 };
    }
    return this.store.data.games[gameId];
  }

  /** Всё по всем играм: { [gameId]: { totalMs, sessions, lastPlayed, running } }. */
  all() {
    const out = {};
    for (const [id, g] of Object.entries(this.store.data.games)) {
      out[id] = { ...g, running: this.running.has(id) };
    }
    for (const id of this.running.keys()) {
      if (!out[id]) out[id] = { totalMs: 0, sessions: 0, lastPlayed: null, running: true };
    }
    return out;
  }

  reset(gameId) {
    if (gameId) delete this.store.data.games[gameId];
    else this.store.data.games = {};
    this.store.save();
    return true;
  }
}

module.exports = { PlayTime, MIN_SESSION_MS };
