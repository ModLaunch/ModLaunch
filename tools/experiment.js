'use strict';

/**
 * Разовые опыты на живых сайтах. Запуск на GitHub: Actions → experiment.
 *
 * 3.2: самые популярные моды Subnautica на Nexus и Thunderstore
 * (что работает на актуальной версии, а что устарело вместе с QModManager),
 * и новая игра R.E.P.O. — сообщество Thunderstore, загрузчик, категории.
 */

const V2 = 'https://api.nexusmods.com/v2/graphql';
const H = { 'User-Agent': 'ModLaunch/experiment' };

async function gql(query, variables = {}) {
  const r = await fetch(V2, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Application-Name': 'ModLaunch', 'Application-Version': 'experiment' }, body: JSON.stringify({ query, variables }) });
  return r.json();
}

async function nexusTop(domain, pages = 3) {
  console.log(`=== Nexus ${domain}: самые скачиваемые ===`);
  for (let page = 0; page < pages; page++) {
    const json = await gql(
      `query($f: ModsFilter, $s: [ModsSort!], $c: Int, $o: Int) { mods(filter: $f, sort: $s, count: $c, offset: $o) { totalCount nodes { modId name downloads endorsements updatedAt modCategory { name } } } }`,
      { f: { gameDomainName: [{ value: domain, op: 'EQUALS' }], adultContent: [{ value: false, op: 'EQUALS' }] }, s: [{ downloads: { direction: 'DESC' } }], c: 40, o: page * 40 }
    );
    for (const m of json?.data?.mods?.nodes ?? []) console.log(`${m.modId}\t${m.downloads}\t${String(m.updatedAt).slice(0, 10)}\t${m.modCategory?.name ?? ''}\t${m.name}`);
  }
  console.log(`=== Nexus ${domain}: самые одобряемые за последний год ===`);
  const json = await gql(
    `query($f: ModsFilter, $s: [ModsSort!]) { mods(filter: $f, sort: $s, count: 40) { nodes { modId name downloads endorsements updatedAt modCategory { name } } } }`,
    { f: { gameDomainName: [{ value: domain, op: 'EQUALS' }], adultContent: [{ value: false, op: 'EQUALS' }], updatedAt: [{ value: String(Math.floor(Date.now() / 1000) - 365 * 86400), op: 'GT' }] }, s: [{ endorsements: { direction: 'DESC' } }] }
  );
  if (json.errors) console.log('errors', JSON.stringify(json.errors).slice(0, 400));
  for (const m of json?.data?.mods?.nodes ?? []) console.log(`${m.modId}\t${m.endorsements}\t${String(m.updatedAt).slice(0, 10)}\t${m.modCategory?.name ?? ''}\t${m.name}`);
}

async function thunderstoreTop(community, pages = 2) {
  console.log(`=== Thunderstore ${community} ===`);
  const filters = await (await fetch(`https://thunderstore.io/api/cyberstorm/community/${community}/filters/`, { headers: H })).json().catch(() => null);
  console.log('категории:', (filters?.package_categories ?? []).map((c) => `${c.slug}=${c.id}`).join(' '));
  for (let page = 1; page <= pages; page++) {
    const list = await (await fetch(`https://thunderstore.io/api/cyberstorm/listing/${community}/?ordering=most-downloaded&page=${page}`, { headers: H })).json().catch(() => null);
    console.log('всего', list?.count);
    for (const p of list?.results ?? []) console.log(`${p.namespace}-${p.name}\t${p.download_count}\t${String(p.last_updated).slice(0, 10)}\t${(p.categories ?? []).map((c) => c.name).join('/')}`);
  }
}

(async () => {
  await nexusTop('subnautica').catch((e) => console.log('ошибка', e));
  await thunderstoreTop('subnautica').catch((e) => console.log('ошибка', e));
  await thunderstoreTop('repo').catch((e) => console.log('ошибка', e));
  for (const pack of ['BepInEx/BepInExPack', 'BepInEx/BepInExPack_REPO']) {
    const r = await fetch(`https://thunderstore.io/api/experimental/package/${pack}/`, { headers: H });
    const j = await r.json().catch(() => null);
    console.log('загрузчик', pack, r.status, j?.latest?.version_number, (j?.community_listings ?? []).map((c) => c.community).join(','));
  }
  const steam = await (await fetch('https://store.steampowered.com/api/appdetails?appids=3241660&filters=basic', { headers: H })).json().catch(() => null);
  console.log('steam 3241660', JSON.stringify(steam?.['3241660']?.data ? { name: steam['3241660'].data.name, type: steam['3241660'].data.type } : steam).slice(0, 300));
})();
