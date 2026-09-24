'use strict';

/**
 * Сборка модов в файле — «поделиться сборкой», как экспорт профиля
 * в r2modman или .mrpack в Modrinth (1.10).
 *
 * Внутри не сами моды, а их номера в каталоге: файл весит пару килобайт,
 * и у друга ModHub скачает всё из тех же мест, откуда качали вы.
 * Моды, поставленные из файла, в каталоге не найти — они попадают в сборку
 * только для списка и при импорте честно показываются как «поставьте сами».
 */

const FORMAT = 'modhub-pack';
const VERSION = 1;
const MAX_MODS = 500;

function str(value, max = 200) {
  return typeof value === 'string' ? value.slice(0, max) : '';
}

/**
 * @param {object} game адаптер
 * @param {object[]} records записи реестра
 * @param {{name?: string, now?: number, app?: string}} [meta]
 */
function buildPack(game, records, meta = {}) {
  return {
    format: FORMAT,
    version: VERSION,
    app: meta.app ?? '',
    game: game.id,
    gameName: game.name,
    name: str(meta.name, 60) || game.name,
    createdAt: new Date(meta.now ?? Date.now()).toISOString(),
    mods: (records ?? [])
      .filter((r) => !r.missing)
      // Зависимости поставятся сами вслед за модом, который их попросил.
      .filter((r) => !r.requestedBy)
      .map((r) => ({
        id: r.id,
        name: r.name,
        version: r.version ?? '',
        source: r.source ?? 'file',
        url: r.url ?? null,
        enabled: r.enabled !== false,
      })),
  };
}

/**
 * Проверяет и нормализует чужой файл сборки. Ничему в нём не верим на слово:
 * файл мог прийти откуда угодно.
 * @returns {{game: string, name: string, mods: object[]}}
 */
function parsePack(text) {
  let data;
  try {
    data = JSON.parse(String(text ?? ''));
  } catch {
    throw Object.assign(new Error('not json'), { code: 'PACK_FORMAT' });
  }
  if (!data || data.format !== FORMAT || typeof data.game !== 'string' || !Array.isArray(data.mods)) {
    throw Object.assign(new Error('not a modhub pack'), { code: 'PACK_FORMAT' });
  }
  if (Number(data.version) > VERSION) {
    throw Object.assign(new Error('newer pack'), { code: 'PACK_NEWER' });
  }
  const mods = data.mods
    .slice(0, MAX_MODS)
    .filter((m) => m && typeof m.id === 'string' && m.id.length <= 200)
    .map((m) => ({
      id: m.id,
      name: str(m.name) || m.id,
      version: str(m.version, 40),
      source: str(m.source, 20) || 'file',
      url: /^https:\/\//i.test(m.url ?? '') ? str(m.url, 500) : null,
      enabled: m.enabled !== false,
    }));
  return { game: data.game, gameName: str(data.gameName, 80), name: str(data.name, 60), mods };
}

module.exports = { buildPack, parsePack, FORMAT };
