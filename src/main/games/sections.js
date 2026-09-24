'use strict';

/**
 * Разделы каталога (2.0.1).
 *
 * Раздел описывается для всех источников сразу, а нужный фильтр выбирает
 * сам источник:
 *
 *   nexus        — ТОЧНЫЕ названия категорий Nexus. Они у каждой игры свои
 *                  («Buildables» у Subnautica, «Base Pieces» у Below Zero),
 *                  поэтому живут в описании игры: game.nexusCategories.
 *                  Шаблоны вида «*Build*» Nexus не понимает — это проверено
 *                  на живом сайте (tools/experiment.js).
 *   thunderstore — слаги категорий Thunderstore; в номера их переводит
 *                  источник по списку категорий сообщества.
 *   modlinks     — теги ModLinks (Hollow Knight).
 *
 * Особые разделы: all — весь каталог; picks — «Нужные моды» из описания
 * игры (featured.picks); packs — «Сборки»: наборы (featured.kits) и файлы.
 */

const S = {
  all: { id: 'all' },
  picks: { id: 'picks', special: 'picks' },
  packs: { id: 'packs', special: 'packs' },
  // Слаги Thunderstore у каждой игры свои; чужие сообщество просто не знает,
  // и они отбрасываются (Valheim — building, Lethal Company — furniture…).
  buildings: { id: 'buildings', thunderstore: ['furniture', 'building'] },
  vehicles: { id: 'vehicles', thunderstore: ['vehicles', 'transportation'] },
  items: { id: 'items', thunderstore: ['items', 'equipment', 'gear', 'crafting'] },
  gameplay: { id: 'gameplay', thunderstore: ['tweaks-and-quality-of-life', 'performance', 'bug-fixes', 'tweaks', 'utility', 'gamemodes', 'artifacts'], modlinks: ['Gameplay'] },
  content: { id: 'content', thunderstore: ['moons', 'interiors', 'monsters', 'weather', 'hazards', 'enemies', 'npcs', 'world-generation', 'player-characters', 'maps', 'skills'], modlinks: ['Expansion', 'Boss'] },
  visuals: { id: 'visuals', thunderstore: ['asset-replacements'] },
  cosmetics: { id: 'cosmetics', thunderstore: ['cosmetics', 'suits', 'emotes', 'skins'], modlinks: ['Cosmetic'] },
  audio: { id: 'audio', thunderstore: ['audio', 'boombox'] },
  ui: { id: 'ui' },
  tools: { id: 'tools', thunderstore: ['libraries', 'tools'], modlinks: ['Library', 'Utility'] },
  modpacks: { id: 'modpacks', thunderstore: ['modpacks'] },
};

/** Список разделов по id: pick('all', 'buildings', …). */
function pick(...ids) {
  return ids.map((id) => {
    if (!S[id]) throw new Error(`Unknown section: ${id}`);
    return S[id];
  });
}

/** Раздел игры по id с фильтром Nexus этой игры; неизвестный — «все». */
function sectionOf(game, id) {
  const section = (game.sections ?? []).find((s) => s.id === id) ?? S.all;
  const nexus = game.nexusCategories?.[section.id] ?? [];
  return { ...section, nexus };
}

module.exports = { SECTIONS: S, pick, sectionOf };
