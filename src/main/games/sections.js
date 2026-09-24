'use strict';

/**
 * Разделы каталога — «Моды / Постройки / Графика / Сборки» (2.0).
 *
 * У каждого сайта свои категории, поэтому раздел описывается для всех
 * источников сразу, а нужный фильтр выбирает сам источник:
 *
 *   nexus        — шаблоны имени категории Nexus («*Buildable*»); пробуются
 *                  по очереди, пока какой-то не даст результат, а если
 *                  категорий нет — слова из названия мода (keywords);
 *   thunderstore — слаги категорий Thunderstore (included_categories);
 *   modlinks     — теги ModLinks (Hollow Knight).
 *
 * Особые разделы: all — весь каталог; picks — «Нужные моды», список
 * проверенных модов из описания игры (featured.picks); packs — «Сборки»:
 * готовые наборы модов (featured.kits) и сборки из файла.
 */

const S = {
  all: { id: 'all' },
  picks: { id: 'picks', special: 'picks' },
  packs: { id: 'packs', special: 'packs' },
  buildings: {
    id: 'buildings',
    nexus: ['*Buildable*', '*Building*', '*Base*', '*Furniture*', '*Decor*'],
    keywords: ['build', 'base', 'decor', 'furniture'],
  },
  vehicles: { id: 'vehicles', nexus: ['*Vehicle*', '*Seamoth*', '*Cyclops*'], keywords: ['seamoth', 'cyclops', 'prawn', 'seatruck', 'vehicle'] },
  visuals: {
    id: 'visuals',
    nexus: ['*Visual*', '*Graphic*', '*Shader*', '*ReShade*', '*Lighting*', '*Texture*'],
    thunderstore: ['asset-replacements', 'cosmetics', 'visuals', 'graphics'],
    keywords: ['shader', 'reshade', 'graphics', 'texture', 'lighting'],
  },
  gameplay: { id: 'gameplay', nexus: ['*Gameplay*', '*Balance*', '*Mechanic*'], modlinks: ['Gameplay'], keywords: ['gameplay', 'tweak'] },
  items: { id: 'items', nexus: ['*Item*', '*Equipment*', '*Tool*', '*Weapon*'], thunderstore: ['items', 'equipment'], keywords: ['item', 'tool', 'module'] },
  content: {
    id: 'content',
    nexus: ['*Expansion*', '*New Content*', '*Location*', '*Map*'],
    thunderstore: ['moons', 'interiors', 'monsters', 'items'],
    modlinks: ['Expansion', 'Boss'],
    keywords: ['expansion', 'expanded'],
  },
  cosmetics: { id: 'cosmetics', nexus: ['*Cosmetic*', '*Character*', '*Portrait*'], thunderstore: ['cosmetics', 'suits', 'emotes'], modlinks: ['Cosmetic'], keywords: ['skin', 'outfit', 'portrait'] },
  audio: { id: 'audio', nexus: ['*Audio*', '*Sound*', '*Music*'], thunderstore: ['audio'], keywords: ['music', 'sound'] },
  ui: { id: 'ui', nexus: ['*User Interface*', '*UI*', '*Interface*'], keywords: ['ui', 'hud', 'menu'] },
  tools: {
    id: 'tools',
    nexus: ['*Modders*', '*Modding*', '*Librar*', '*Utilit*', '*Framework*'],
    thunderstore: ['libraries', 'tools', 'bepinex'],
    modlinks: ['Library', 'Utility'],
    keywords: ['library', 'api', 'framework', 'lib'],
  },
  modpacks: { id: 'modpacks', thunderstore: ['modpacks'], keywords: ['modpack'] },
};

/** Список разделов по id: pick('all', 'buildings', …). */
function pick(...ids) {
  return ids.map((id) => {
    if (!S[id]) throw new Error(`Unknown section: ${id}`);
    return S[id];
  });
}

/** Раздел игры по id; неизвестный — «все». */
function sectionOf(game, id) {
  return (game.sections ?? []).find((s) => s.id === id) ?? S.all;
}

module.exports = { SECTIONS: S, pick, sectionOf };
