'use strict';

const fs = require('node:fs');
const { locateGame, matchesSignature } = require('../core/locate');
const { checkWriteTargets, isProtectedLocation } = require('../core/permissions');
const { t } = require('../i18n');

const smapi = require('../core/loaders/smapi');
const bepinex = require('../core/loaders/bepinex');
const hkapi = require('../core/loaders/hkapi');

/**
 * Реестр поддерживаемых игр.
 *
 * Добавить игру — значит положить сюда ещё один файл адаптера. Ядро,
 * интерфейс и установка при этом не меняются вообще: в этом и была вся
 * идея разделения на слои, и именно отсюда берётся «много игр» без
 * переписывания программы.
 */

const ADAPTERS = [
  require('./stardew-valley'),
  require('./hollow-knight'),
  require('./lethal-company'),
  require('./subnautica'),
  require('./subnautica-below-zero'),
];

const LOADERS = { smapi, bepinex, hkapi };

function all() {
  return ADAPTERS;
}

function byId(gameId) {
  return ADAPTERS.find((g) => g.id === gameId) ?? null;
}

function loaderFor(game) {
  const loader = LOADERS[game.loader.kind];
  if (!loader) throw new Error(`Unknown loader: ${game.loader.kind}`);
  return loader;
}

/**
 * Ищет игру на диске: Steam, Epic, GOG и обычные папки.
 * Подробности — в core/locate.js.
 */
async function locate(game, onProgress, options) {
  return locateGame(game, onProgress, options);
}

/**
 * Проверяет, что указанная папка действительно содержит эту игру.
 * Нужна для ручного выбора: без проверки человек укажет папку с ярлыком
 * и будет полчаса выяснять, почему ничего не работает.
 */
function validatePath(game, candidate) {
  if (!candidate || !fs.existsSync(candidate)) {
    return { ok: false, reason: t('err.pathNotExists') };
  }

  // Папка другой поддерживаемой игры — это точно не эта игра. Иначе
  // Subnautica приняла бы папку Below Zero по похожему имени exe.
  const other = ADAPTERS.find((g) => g !== game && matchesSignature(g, candidate));
  if (other && !matchesSignature(game, candidate)) {
    return { ok: false, reason: t('err.notGameFolder', { game: game.name }) };
  }

  const expected = game.launch(candidate).command;
  if (fs.existsSync(expected)) return { ok: true };

  // Папка опознаётся тем же признаком, что и при автопоиске: содержимым.
  // Это важнее имени — репак может называться как угодно.
  if (matchesSignature(game, candidate)) return { ok: true };

  // Загрузчик ещё не установлен и подпись не сошлась — смотрим на имена файлов.
  const entries = fs.readdirSync(candidate).map((e) => e.toLowerCase());
  const looksRight = game.folderNames.some((name) => {
    const stem = name.toLowerCase().replace(/\s+/g, '');
    return entries.some((e) => e.replace(/\s+/g, '').startsWith(stem));
  });

  if (looksRight) return { ok: true };

  return { ok: false, reason: t('err.notGameFolder', { game: game.name }) };
}

/** Короткая карточка игры без обращения к диску — для мгновенной отрисовки. */
function describe(game) {
  return {
    id: game.id,
    name: game.name,
    shortName: game.shortName,
    accent: game.accent,
    tagline: game.tagline ?? null,
    loaderName: game.loader.name,
    loaderKind: game.loader.kind,
    catalogKind: game.catalog.kind,
    browseUrl: game.catalog.browseUrl ?? null,
    steamAppId: game.steamAppId ?? null,
    hasSaves: typeof game.savesDir === 'function',
    sections: (game.sections ?? []).map((s) => s.id),
    featured: game.featured ?? { picks: [], kits: [] },
  };
}

/**
 * Полное состояние игры: найдена ли, стоит ли загрузчик, где папка модов.
 *
 * @param {object} game адаптер
 * @param {string|null} knownPath путь, который уже известен (указан руками или найден раньше)
 * @param {(stage: object) => void} [onProgress]
 * @param {{deep?: boolean, scan?: boolean}} [options] scan:false — не трогать диски вообще
 */
async function inspect(game, knownPath = null, onProgress, options = {}) {
  let located = null;
  let stalePath = null;

  if (knownPath) {
    // Путь мог протухнуть: игру перенесли, диск отключили, папку удалили.
    // Раньше он брался на веру, и дальше всё ломалось по очереди без
    // единого внятного сообщения.
    if (fs.existsSync(knownPath)) {
      located = { path: knownPath, source: options.manual === false ? 'scan' : 'manual' };
    } else {
      stalePath = knownPath;
    }
  }

  if (!located && options.scan !== false) {
    located = await locate(game, onProgress, options);
  }

  if (!located) {
    return {
      ...describe(game),
      found: false,
      scanned: options.scan !== false,
      stalePath,
      path: null,
      pathSource: null,
      loader: { kind: game.loader.kind, name: game.loader.name, installed: false, version: null },
      modsDir: null,
      catalog: game.catalog,
    };
  }

  const loader = loaderFor(game);
  const state = loader.detect(located.path);
  const modsDir = game.modsDir(located.path);
  const access = checkWriteTargets(
    game.writeTargets ? game.writeTargets(located.path) : [located.path, modsDir]
  );

  return {
    ...describe(game),
    found: true,
    scanned: true,
    stalePath,
    path: located.path,
    pathSource: located.source,
    loader: {
      kind: game.loader.kind,
      name: game.loader.name,
      site: game.loader.site,
      installed: state.installed,
      version: state.version,
    },
    modsDir,
    modsDirExists: fs.existsSync(modsDir),
    // Проверяем права заранее и по всем папкам, куда предстоит писать:
    // упереться в них посреди установки означает оставить игру
    // в наполовину изменённом состоянии.
    writable: access.ok,
    blockedPath: access.blocked,
    protectedLocation: isProtectedLocation(access.blocked ?? located.path),
    catalog: game.catalog,
  };
}

/** Мгновенный список игр без обращения к диску — чтобы интерфейс нарисовался сразу. */
function supported() {
  return ADAPTERS.map(describe);
}

module.exports = { all, byId, supported, describe, loaderFor, locate, validatePath, inspect };
