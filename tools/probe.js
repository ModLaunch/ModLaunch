'use strict';

/**
 * Проверка ModHub на живых сайтах (2.0.1).
 *
 * Запускается на серверах GitHub (.github/workflows/check.yml): там есть
 * интернет, а у разработчика его может не быть. Проверяет то, что нельзя
 * проверить без сети:
 *
 *   1. Nexus Mods: какие фильтры понимает GraphQL (схема), что находит каждый
 *      раздел каталога, открываются ли моды из «Нужных» и наборов.
 *   2. Thunderstore: категории сообщества, фильтр разделов, «Нужные».
 *   3. ModLinks (Hollow Knight): разделы по тегам.
 *   4. Настоящая установка: BepInEx для Subnautica в пустую «папку игры»
 *      и мод Lethal Company с зависимостями из Thunderstore.
 *
 * Ничего не падает молча: каждая проверка пишет PASS или FAIL, в конце —
 * итог и код выхода.
 */

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const games = require('../src/main/games');
const { sectionOf } = require('../src/main/games/sections');
const nexus = require('../src/main/core/sources/nexus');
const thunderstore = require('../src/main/core/sources/thunderstore');
const modlinks = require('../src/main/core/sources/modlinks');

const results = [];
function report(ok, name, detail = '') {
  results.push({ ok, name });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? `  —  ${detail}` : ''}`);
}

async function nexusRaw(query, variables = {}) {
  const response = await fetch('https://api.nexusmods.com/v2/graphql', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ query, variables }),
  });
  return response.json();
}

async function schema() {
  console.log('\n=== Nexus GraphQL: схема фильтров ===');
  const json = await nexusRaw(`{
    filter: __type(name: "ModsFilter") { inputFields { name type { name kind ofType { name kind ofType { name } } } } }
    ops: __type(name: "FilterComparisonOperator") { enumValues { name } }
    query: __type(name: "Query") { fields { name args { name } } }
  }`);
  if (json.errors) console.log('errors:', JSON.stringify(json.errors).slice(0, 500));
  console.log('ModsFilter:', (json.data?.filter?.inputFields ?? []).map((f) => f.name).join(', '));
  console.log('Operators:', (json.data?.ops?.enumValues ?? []).map((v) => v.name).join(', '));
  console.log('Query:', (json.data?.query?.fields ?? []).map((f) => `${f.name}(${f.args.map((a) => a.name).join(',')})`).join(' '));
}

async function nexusCategories(domain, gameId) {
  // Имена категорий игры — по первым 400 самым скачиваемым модам.
  const counts = new Map();
  for (let offset = 0; offset < 400; offset += 100) {
    const json = await nexusRaw(
      `query($filter: ModsFilter, $count: Int, $offset: Int) { mods(filter: $filter, count: $count, offset: $offset, sort: [{ downloads: { direction: DESC } }]) { nodes { modCategory { name categoryId } } } }`,
      { filter: { gameDomainName: [{ value: domain, op: 'EQUALS' }] }, count: 100, offset }
    );
    if (json.errors) {
      console.log('categories error:', JSON.stringify(json.errors).slice(0, 300));
      break;
    }
    for (const node of json.data?.mods?.nodes ?? []) {
      const key = `${node.modCategory?.name} #${node.modCategory?.categoryId}`;
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
  }
  console.log(`Категории ${domain}:`, [...counts].sort((a, b) => b[1] - a[1]).map(([k, n]) => `${k}=${n}`).join(' | '));
}

async function checkNexusGame(game) {
  console.log(`\n=== ${game.name} (Nexus: ${game.catalog.nexusDomain}) ===`);
  await nexusCategories(game.catalog.nexusDomain, game.catalog.nexusGameId);
  const all = await nexus.browse(game.catalog.nexusDomain, { page: 1 });
  report(all.total > 0, `${game.id}: весь каталог`, `${all.total} модов`);
  for (const section of game.sections) {
    if (section.special || section.id === 'all') continue;
    const s = sectionOf(game, section.id);
    try {
      const r = await nexus.browse(game.catalog.nexusDomain, { page: 1, categories: s.nexus });
      const cats = [...new Set(r.mods.map((m) => m.categories[0]))].slice(0, 4).join(', ');
      // Раздел работает, если нашёл моды и их меньше, чем во всём каталоге,
      // а категории найденных — ровно те, что просили.
      const include = s.nexus.filter((c) => !c.startsWith('!'));
      const exclude = s.nexus.filter((c) => c.startsWith('!')).map((c) => c.slice(1));
      const inside = r.mods.every((m) => include.includes(m.categories[0]) && !exclude.includes(m.categories[0]));
      report(r.total > 0 && r.total < all.total && inside, `${game.id}: раздел ${section.id}`, `${r.total} модов; категории: ${cats}; пример: ${r.mods.slice(0, 3).map((m) => m.name).join(' / ')}`);
    } catch (error) {
      report(false, `${game.id}: раздел ${section.id}`, error.message);
    }
  }
  // Поиск внутри раздела: категория + имя вместе.
  const firstCat = sectionOf(game, 'buildings').nexus.length ? 'buildings' : 'tools';
  const withQuery = await nexus.browse(game.catalog.nexusDomain, { page: 1, categories: sectionOf(game, firstCat).nexus, query: 'base' });
  report(withQuery.total >= 0, `${game.id}: поиск «base» в разделе ${firstCat}`, `${withQuery.total}: ${withQuery.mods.slice(0, 3).map((m) => m.name).join(' / ')}`);
  await checkPicks(game, async (id) => (await nexus.getDetails(game.catalog.nexusDomain, game.catalog.nexusGameId, id))?.mod);
}

async function checkPicks(game, lookup) {
  const ids = [...new Set([...(game.featured?.picks ?? []), ...(game.featured?.kits ?? []).flatMap((k) => k.mods)])];
  for (const id of ids) {
    try {
      const mod = await lookup(id);
      report(Boolean(mod), `${game.id}: нужный мод ${id}`, mod ? `${mod.name}${mod.icon ? ' [есть картинка]' : ' [без картинки]'}` : 'не найден');
    } catch (error) {
      report(false, `${game.id}: нужный мод ${id}`, error.message);
    }
  }
}

async function checkThunderstore(game) {
  console.log(`\n=== ${game.name} (Thunderstore: ${game.catalog.community}) ===`);
  try {
    const filters = await (await fetch(`https://thunderstore.io/api/cyberstorm/community/${game.catalog.community}/filters/`)).json();
    console.log('filters:', JSON.stringify(filters).slice(0, 1500));
  } catch (error) {
    console.log('filters error', error.message);
  }
  const all = await thunderstore.search(game.catalog.community, '', { page: 1 });
  report(all.total > 0, `${game.id}: весь каталог`, `${all.total}`);
  for (const section of game.sections) {
    if (section.special || section.id === 'all') continue;
    const s = sectionOf(game, section.id);
    try {
      const r = await thunderstore.search(game.catalog.community, '', { page: 1, categories: s.thunderstore });
      const cats = [...new Set(r.mods.flatMap((m) => m.categories))].slice(0, 6).join(', ');
      report(r.mods.length > 0 && r.total < all.total, `${game.id}: раздел ${section.id}`, `${r.total}; категории: ${cats}; пример: ${r.mods.slice(0, 3).map((m) => m.name).join(' / ')}`);
    } catch (error) {
      report(false, `${game.id}: раздел ${section.id}`, error.message);
    }
  }
  await checkPicks(game, (id) => thunderstore.getById(game.catalog.community, id));
}

async function checkModlinks(game) {
  console.log(`\n=== ${game.name} (ModLinks) ===`);
  const all = await modlinks.search('', {});
  report(all.length > 0, `${game.id}: весь каталог`, `${all.length}`);
  for (const section of game.sections) {
    if (section.special || section.id === 'all') continue;
    const s = sectionOf(game, section.id);
    const r = await modlinks.search('', { categories: s.modlinks });
    report(r.length > 0 && r.length < all.length, `${game.id}: раздел ${section.id}`, `${r.length}`);
  }
  await checkPicks(game, (id) => modlinks.getById(id));
}

/** Коллекции Nexus (3.1): список и точный состав без ключа. */
async function checkCollections() {
  console.log('\n=== Коллекции Nexus ===');
  for (const domain of ['subnautica', 'stardewvalley']) {
    const list = await nexus.browseCollections(domain, { page: 1 });
    report(list.collections.length > 0 && list.total > 0, `${domain}: коллекции`, `${list.total}; ${list.collections.slice(0, 3).map((c) => `${c.name} (${c.modCount})`).join(' / ')}`);
  }
  const c = await nexus.getCollection('subnautica', 'https://www.nexusmods.com/games/subnautica/collections/pnq4xb');
  report(c.mods.length > 10 && c.mods.every((m) => m.id && Number.isInteger(m.fileId)) && c.mods.some((m) => m.id === '24'), 'subnautica: состав коллекции с номерами файлов', `${c.name}: ${c.mods.length} модов, напр. ${c.mods.slice(0, 3).map((m) => `${m.name}#${m.fileId}`).join(', ')}`);
  try {
    await nexus.getCollection('subnautica', 'htknoa');
    report(false, 'коллекция другой игры отклоняется');
  } catch (error) {
    report(/NEXUS_COLLECTION/.test(error.code ?? ''), 'коллекция другой игры отклоняется', error.code);
  }
}

async function checkInstall() {
  console.log('\n=== Настоящая установка ===');
  const bepinex = require('../src/main/core/loaders/bepinex');
  const install = require('../src/main/core/install');
  const { ModRegistry } = require('../src/main/core/registry');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'modhub-probe-'));

  const DATA = { 'lethal-company': 'Lethal Company_Data', subnautica: 'Subnautica_Data', 'subnautica-below-zero': 'SubnauticaZero_Data', valheim: 'valheim_Data', 'risk-of-rain-2': 'Risk of Rain 2_Data' };
  for (const id of Object.keys(DATA)) {
    const game = games.byId(id);
    const dir = path.join(root, id);
    fs.mkdirSync(path.join(dir, DATA[id]), { recursive: true });
    try {
      await bepinex.install({ ...game, path: dir }, () => {});
      const state = bepinex.detect(dir);
      report(fs.existsSync(path.join(dir, 'BepInEx', 'core')) && fs.existsSync(path.join(dir, 'winhttp.dll')), `${id}: BepInEx ставится`, JSON.stringify(state));
    } catch (error) {
      report(false, `${id}: BepInEx ставится`, error.message);
    }
  }

  // Мод с зависимостями — весь путь установки, как у кнопки «Установить».
  const game = games.byId('lethal-company');
  const dir = path.join(root, 'lethal-company');
  const state = { path: dir, modsDir: game.modsDir(dir), found: true };
  const registry = new ModRegistry(path.join(root, 'data'), game.id, { modsDir: state.modsDir, storageDir: path.join(dir, 'ModHub') });
  try {
    const names = [];
    for (const id of ['notnotnotswipez-MoreCompany', 'tinyhoot-ShipLoot', 'x753-More_Suits']) {
      const mod = await thunderstore.getById('lethal-company', id);
      const result = await install.installFromCatalog({ game, state, registry }, mod, () => {});
      names.push(...result.installed.map((r) => r.name));
    }
    const files = fs.readdirSync(state.modsDir);
    const bad = files.filter((f) => /^(plugins|bepinex|bepinexpack)$/i.test(f));
    const records = registry.list().map((r) => `${r.id} → ${r.folder}`);
    report(files.length >= 3 && !bad.length, 'lethal-company: три мода ставятся рядом и не затирают друг друга', `поставлено: ${names.join(', ')}; в plugins: ${files.join(', ')}; реестр: ${records.join('; ')}`);
    const dlls = [];
    const walk = (d) => fs.readdirSync(d, { withFileTypes: true }).forEach((e) => (e.isDirectory() ? walk(path.join(d, e.name)) : /\.dll$/i.test(e.name) && dlls.push(path.relative(state.modsDir, path.join(d, e.name)))));
    walk(state.modsDir);
    report(dlls.length >= 3, 'lethal-company: dll модов на месте', dlls.join(', '));
  } catch (error) {
    report(false, 'lethal-company: MoreCompany ставится', error.message);
  }
}

async function checkShaders() {
  console.log('\n=== Шейдеры: ReShade по-настоящему ===');
  const AdmZip = require('adm-zip');
  const { ReShade, looksLikePreset } = require('../src/main/core/reshade');
  const install = require('../src/main/core/install');
  const { ModRegistry } = require('../src/main/core/registry');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'modhub-shaders-'));
  const game = games.byId('subnautica');
  const dir = path.join(root, 'Subnautica');
  fs.mkdirSync(path.join(dir, 'Subnautica_Data'), { recursive: true });
  fs.writeFileSync(path.join(dir, 'Subnautica.exe'), '');
  const tool = new ReShade({ cacheDir: path.join(root, 'cache') });
  const zip = new AdmZip();
  zip.addFile('Cinematic/Cinematic.ini', Buffer.from('Techniques=LumaSharpen@LumaSharpen.fx,Vibrance@Vibrance.fx,Deband@Deband.fx\r\n\r\n[LumaSharpen.fx]\r\nsharp_strength=0.65\r\n'));
  zip.addFile('Cinematic/readme.txt', Buffer.from('preset'));
  const archive = path.join(root, 'Cinematic.zip');
  zip.writeZip(archive);
  report(looksLikePreset(archive), 'шейдеры: архив пресета узнаётся');
  const state = { path: dir, modsDir: game.modsDir(dir) };
  const registry = new ModRegistry(path.join(root, 'data'), game.id, { modsDir: state.modsDir, storageDir: path.join(dir, 'ModHub'), presetDir: dir });
  try {
    const record = await install.installAny({ game, state, registry, reshade: tool }, archive, { id: 'nexus:subnautica:1', name: 'Cinematic' }, (p) => console.log('   ', p.code, p.detail ?? ''));
    const detect = tool.detect(dir);
    const dll = path.join(dir, 'dxgi.dll');
    const size = fs.existsSync(dll) ? fs.statSync(dll).size : 0;
    report(detect.installed && detect.dll === 'dxgi.dll' && size > 1024 * 1024, 'шейдеры: ReShade скачан с reshade.me и встал как dxgi.dll', `${Math.round(size / 1024)} КБ, ${JSON.stringify(detect)}`);
    const fx = ['LumaSharpen.fx', 'Vibrance.fx', 'Deband.fx'].map((f) => [f, tool.presentEffects(dir).has(f.toLowerCase())]);
    report(fx.every(([, ok]) => ok), 'шейдеры: эффекты пресета поставлены', JSON.stringify(fx));
    report(record.kind === 'preset' && fs.existsSync(path.join(dir, 'Cinematic.ini')) && /PresetPath=\.\\Cinematic\.ini/.test(fs.readFileSync(path.join(dir, 'ReShade.ini'), 'utf8')), 'шейдеры: пресет в папке игры и выбран в ReShade.ini');
  } catch (error) {
    report(false, 'шейдеры: установка', error.stack);
  }
}

async function checkDeps() {
  console.log('\n=== Зависимости ===');
  const deps = require('../src/main/core/deps');
  const details = await nexus.getDetails('subnautica', 1155, '2800');
  const reqs = (details?.requirements ?? []).map((r) => `${r.id}:${r.name}`);
  report(reqs.some((r) => r.startsWith('1262:')), 'зависимости: у Decorations Mod в требованиях Nautilus', reqs.join(', '));
  const index = new deps.SmapiIndex({ file: path.join(os.tmpdir(), 'smapi-index-probe.json') });
  const found = await index.lookup(['Pathoschild.ContentPatcher', 'spacechase0.SpaceCore']);
  report(found['Pathoschild.ContentPatcher']?.nexusId === '1915', 'зависимости: SMAPI знает номер Content Patcher на Nexus', JSON.stringify(found));
}

/** Поиск из шапки (весь каталог, без раздела) — у каждого сайта свой. */
async function checkSearch() {
  console.log('\n=== Поиск ===');
  const cases = [
    ['subnautica', 'nexus', 'seamoth', /seamoth/i],
    ['subnautica-below-zero', 'nexus', 'seatruck', /seatruck/i],
    ['stardew-valley', 'nexus', 'stardew expanded', /expanded/i],
    ['lethal-company', 'thunderstore', 'more company', /more\s*company/i],
    ['hollow-knight', 'modlinks', 'bench', /bench/i],
  ];
  for (const [id, kind, query, expect] of cases) {
    const game = games.byId(id);
    try {
      let mods = [];
      if (kind === 'nexus') mods = (await nexus.browse(game.catalog.nexusDomain, { page: 1, query, hide: game.catalog.hide })).mods;
      else if (kind === 'thunderstore') mods = (await thunderstore.search(game.catalog.community, query, { page: 1 })).mods;
      else mods = await modlinks.search(query, {});
      const names = mods.slice(0, 3).map((m) => m.name);
      report(mods.length > 0 && mods.slice(0, 5).some((m) => expect.test(m.name)), `поиск «${query}» — ${id}`, `${mods.length}: ${names.join(' / ')}`);
    } catch (error) {
      report(false, `поиск «${query}» — ${id}`, error.message);
    }
  }
}

(async () => {
  await checkSearch().catch((e) => report(false, 'поиск упал', e.stack));
  await checkShaders().catch((e) => report(false, 'шейдеры упали', e.stack));
  await checkDeps().catch((e) => report(false, 'зависимости упали', e.stack));
  await schema().catch((e) => console.log('schema error', e.message));
  for (const game of games.all()) {
    try {
      if (game.catalog.kind === 'nexus') await checkNexusGame(game);
      else if (game.catalog.kind === 'thunderstore') await checkThunderstore(game);
      else if (game.catalog.kind === 'modlinks') await checkModlinks(game);
    } catch (error) {
      report(false, `${game.id}: проверка упала`, error.stack);
    }
  }
  await checkCollections().catch((e) => report(false, 'коллекции упали', e.stack));
  await checkInstall().catch((e) => report(false, 'установка упала', e.stack));

  const failed = results.filter((r) => !r.ok);
  console.log(`\nИтого: ${results.length - failed.length} PASS, ${failed.length} FAIL`);
  for (const f of failed) console.log(`  FAIL ${f.name}`);
  process.exitCode = failed.length ? 1 : 0;
})();
