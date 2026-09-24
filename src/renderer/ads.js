'use strict';

/**
 * Реклама в шапке ModHub.
 *
 * Место справа в шапке — рекламное. Пока своих рекламодателей нет, его
 * занимают объявления самого ModHub: премиум, подсказка про игры, «на чай»
 * и — если в ads.config.json указан адрес — «разместите у нас рекламу».
 *
 * Как только в src/main/ads.config.json появится адрес ленты (feedUrl),
 * программа будет брать объявления оттуда. Пересобирать ModHub ради новой
 * рекламы не нужно: достаточно поменять файл ленты на сервере.
 *
 * Внутреннее объявление ведёт на экран программы (go), внешнее — открывается
 * в браузере (url, только https). Своих объявлений из ленты программа
 * не додумывает: чего нет в ленте, того и не показывает.
 */

const HOUSE_ADS = [
  { id: 'house-premium', kind: 'premium', go: 'premium', icon: 'crown', title: 'ad.premium.title', text: 'ad.premium.text' },
  { id: 'house-games', kind: 'games', go: 'games', icon: 'grid', title: 'ad.games.title', text: 'ad.games.text' },
  { id: 'house-donate', kind: 'donate', go: 'donate', icon: 'cup', title: 'ad.donate.title', text: 'ad.donate.text' },
  {
    id: 'house-advertise',
    kind: 'advertise',
    icon: 'megaphone',
    title: 'ad.advertise.title',
    text: 'ad.advertise.text',
    needs: 'advertiseUrl',
  },
];

/**
 * Что крутить в шапке.
 * @param {{items?: object[], advertiseUrl?: string|null}|null} feed — ответ основного процесса
 * @param {'ru'|'en'} lang
 * @returns {object[]}
 */
function buildAds(feed, lang) {
  const remote = (feed?.items ?? [])
    .filter((ad) => ad && ad.title && ad.url && (!ad.lang || ad.lang === lang))
    .map((ad) => ({ ...ad, remote: true }));
  if (remote.length) return remote;

  return HOUSE_ADS.filter((ad) => !ad.needs || feed?.[ad.needs]).map((ad) =>
    ad.needs ? { ...ad, url: feed[ad.needs] } : { ...ad }
  );
}

const ModHubAds = { HOUSE_ADS, buildAds };

if (typeof window !== 'undefined') window.ModHubAds = ModHubAds;
if (typeof module !== 'undefined' && module.exports) module.exports = ModHubAds;
