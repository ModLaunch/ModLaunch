'use strict';

/**
 * Разовые опыты на живых сайтах. Запуск на GitHub: Actions → experiment.
 *
 * 3.1: коллекции Nexus — какие поля есть в GraphQL v2 и что приходит
 * по настоящей коллекции; Thunderstore — сборки (modpacks) Valheim и
 * Risk of Rain 2, их зависимости и папка config.
 */

const V2 = 'https://api.nexusmods.com/v2/graphql';
const HEADERS = { 'Content-Type': 'application/json', Accept: 'application/json', 'Application-Name': 'ModLaunch', 'Application-Version': 'experiment' };

async function gql(query, variables = {}) {
  const r = await fetch(V2, { method: 'POST', headers: HEADERS, body: JSON.stringify({ query, variables }) });
  const json = await r.json().catch(() => null);
  return { status: r.status, json };
}

const short = (value, n = 1800) => {
  const text = JSON.stringify(value);
  return text.length > n ? text.slice(0, n) + '…' : text;
};

async function fieldsOf(type) {
  const { json } = await gql(`query($n: String!) { __type(name: $n) { name fields { name args { name type { name kind ofType { name kind } } } type { name kind ofType { name kind ofType { name kind } } } } inputFields { name type { name kind ofType { name kind } } } } }`, { n: type });
  const t = json?.data?.__type;
  if (!t) return console.log(type, '→ нет типа', short(json?.errors ?? json, 300));
  const list = (t.fields ?? t.inputFields ?? []).map((f) => `${f.name}${f.args?.length ? '(' + f.args.map((a) => a.name).join(',') + ')' : ''}:${f.type?.name ?? f.type?.ofType?.name ?? f.type?.ofType?.ofType?.name ?? f.type?.kind}`);
  console.log(`${type}: ${list.join('  ')}`);
}

async function nexusCollections() {
  console.log('=== Nexus: коллекции ===');
  const { json } = await gql(`{ __schema { queryType { fields { name args { name } } } } }`);
  const q = (json?.data?.__schema?.queryType?.fields ?? []).filter((f) => /collection/i.test(f.name));
  console.log('Query:', q.map((f) => `${f.name}(${f.args.map((a) => a.name).join(',')})`).join('  '));
  for (const type of ['Collection', 'CollectionRevision', 'CollectionRevisionMod', 'ModFile', 'CollectionsSearchFilter', 'CollectionsFilter', 'CollectionPage', 'CollectionsSort', 'CollectionsSearchSort']) await fieldsOf(type);

  for (const domain of ['stardewvalley', 'subnautica']) {
    for (const [name, query, vars] of [
      ['collectionsV2', `query($f: CollectionsSearchFilter) { collectionsV2(filter: $f, count: 3) { totalCount nodes { slug name } } }`, { f: { gameDomain: [{ value: domain, op: 'EQUALS' }] } }],
      ['collectionsV2/gameDomainName', `query($f: CollectionsSearchFilter) { collectionsV2(filter: $f, count: 3) { totalCount nodes { slug name } } }`, { f: { gameDomainName: [{ value: domain, op: 'EQUALS' }] } }],
      ['collections', `query($d: String) { collections(gameDomain: $d, count: 3) { nodes { slug name } } }`, { d: domain }],
    ]) {
      const r = await gql(query, vars);
      console.log(domain, name, r.status, short(r.json, 600));
    }
  }
}

async function nexusRevision(slug, domain) {
  const queries = [
    `query($s: String!, $d: String) { collectionRevision(slug: $s, domainName: $d, viewAdultContent: false) { revisionNumber collection { name slug summary tileImage { url } user { name } game { domainName } } modFiles { optional fileId file { fileId name version size uri modId mod { modId name pictureUrl } } } } }`,
    `query($s: String!, $d: String) { collection(slug: $s, domainName: $d, viewAdultContent: false) { name slug latestPublishedRevision { revisionNumber modFiles { optional fileId file { fileId name version modId mod { modId name } } } } } }`,
  ];
  for (const q of queries) {
    const r = await gql(q, { s: slug, d: domain });
    console.log('revision', slug, r.status, short(r.json, 2500));
  }
}

async function thunderstorePacks() {
  console.log('=== Thunderstore: сборки ===');
  for (const community of ['valheim', 'riskofrain2']) {
    const url = `https://thunderstore.io/api/cyberstorm/listing/${community}/?ordering=most-downloaded&included_categories=modpacks`;
    const r = await fetch(url, { headers: { 'User-Agent': 'ModLaunch/experiment' } });
    const body = await r.text();
    console.log(community, r.status, body.slice(0, 600));
  }
}

(async () => {
  await nexusCollections().catch((e) => console.log('ошибка', e));
  for (const [slug, domain] of (process.env.SLUGS ?? '').split(',').filter(Boolean).map((s) => s.split(':'))) await nexusRevision(slug, domain);
  await thunderstorePacks().catch((e) => console.log('ошибка', e));
})();
