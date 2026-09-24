'use strict';

/** Разовые опыты с фильтрами Nexus и Thunderstore: какой формат понимает сайт. */

async function gql(filter) {
  const r = await fetch('https://api.nexusmods.com/v2/graphql', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      query: 'query($filter: ModsFilter){ mods(filter: $filter, count: 3, sort: [{ downloads: { direction: DESC } }]) { totalCount nodes { name modCategory { name } } } }',
      variables: { filter },
    }),
  });
  const j = await r.json();
  if (j.errors) return 'ERR ' + JSON.stringify(j.errors).slice(0, 200);
  return `${j.data.mods.totalCount} :: ${j.data.mods.nodes.map((n) => `${n.name} [${n.modCategory?.name}]`).join(' | ')}`;
}

const base = { gameDomainName: [{ value: 'subnautica', op: 'EQUALS' }], adultContent: [{ value: false, op: 'EQUALS' }] };
const cat = (value, op = 'EQUALS') => ({ categoryName: [{ value, op }] });

const tests = {
  'cat EQUALS Buildables': { ...base, ...cat('Buildables') },
  'cat WILDCARD Buildables': { ...base, ...cat('Buildables', 'WILDCARD') },
  'cat WILDCARD Build*': { ...base, ...cat('Build*', 'WILDCARD') },
  'cat WILDCARD *uild*': { ...base, ...cat('*uild*', 'WILDCARD') },
  'cat MATCHES build': { ...base, ...cat('build', 'MATCHES') },
  'cat 2 values EQUALS': { ...base, categoryName: [{ value: 'Buildables', op: 'EQUALS' }, { value: 'Items', op: 'EQUALS' }] },
  'nested OR': { ...base, filter: [{ op: 'OR', filter: [cat('Buildables'), cat('Items')] }] },
  'root op OR (only cats)': { op: 'OR', filter: [cat('Buildables'), cat('Items')], ...base },
  'name WILDCARD decor': { ...base, name: [{ value: 'decor', op: 'WILDCARD' }] },
  'name WILDCARD *decor*': { ...base, name: [{ value: '*decor*', op: 'WILDCARD' }] },
  'name WILDCARD Decor*': { ...base, name: [{ value: 'Decor*', op: 'WILDCARD' }] },
  'nameStemmed MATCHES decoration': { ...base, nameStemmed: [{ value: 'decoration', op: 'MATCHES' }] },
  'cat Buildables + name WILDCARD *base*': { ...base, ...cat('Buildables'), name: [{ value: '*base*', op: 'WILDCARD' }] },
  'cat Buildables + nameStemmed base': { ...base, ...cat('Buildables'), nameStemmed: [{ value: 'base', op: 'MATCHES' }] },
};

async function ts(query) {
  const r = await fetch(`https://thunderstore.io/api/cyberstorm/listing/lethal-company/?${query}`);
  if (!r.ok) return `HTTP ${r.status}`;
  const j = await r.json();
  return `${j.count} :: ${(j.results ?? []).slice(0, 3).map((p) => `${p.name} [${(p.categories ?? []).map((c) => c.name ?? c).join(',')}]`).join(' | ')}`;
}

(async () => {
  console.log('=== Nexus ===');
  for (const [name, filter] of Object.entries(tests)) console.log(name.padEnd(40), await gql(filter).catch((e) => 'EXC ' + e.message));
  console.log('\n=== Thunderstore ===');
  for (const q of [
    'page=1',
    'page=1&included_categories=cosmetics',
    'page=1&included_categories=692',
    'page=1&included_categories=659',
    'page=1&included_categories[]=692',
    'page=1&included_categories=692&included_categories=686',
    'page=1&section=modpacks',
    'page=1&q=suit&included_categories=692',
  ]) console.log(q.padEnd(56), await ts(q).catch((e) => 'EXC ' + e.message));
  const f = await (await fetch('https://thunderstore.io/api/cyberstorm/community/lethal-company/filters/')).json();
  console.log('\nsections:', JSON.stringify(f.sections));
  const f2 = await (await fetch('https://thunderstore.io/api/cyberstorm/community/subnautica/filters/').catch(() => null))?.json?.().catch(() => null);
  console.log('subnautica filters:', JSON.stringify(f2).slice(0, 600));
})();
