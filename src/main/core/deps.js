'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { JsonStore } = require('./store');

/**
 * Зависимости установленных модов (2.1).
 *
 * До 2.1 проверка сравнивала номер записи в реестре с зависимостями из
 * manifest.json. У модов Stardew с Nexus номер записи — «nexus:stardewvalley:1915»,
 * а в манифесте зависимость записана как «Pathoschild.ContentPatcher»: они не
 * совпадали никогда, и Content Patcher числился «недостающим», даже когда стоял.
 * Моды, поставленные руками, не учитывались вовсе, а требования Nexus
 * (Nautilus у модов Subnautica) не проверялись совсем.
 *
 * Теперь мод считается на месте, если совпало хоть что-то:
 *   • номер в реестре, в том числе номер Nexus без приставки;
 *   • UniqueID из manifest.json (Stardew) — и у модов ModHub, и у тех,
 *     что лежат в папке модов сами по себе;
 *   • имя папки или имя мода (для требований Nexus, у которых есть только имя).
 *
 * Для недостающего сразу ищется, откуда его поставить:
 *   • требование Nexus — это и есть номер на Nexus;
 *   • UniqueID Stardew — номер на Nexus спрашиваем у сайта SMAPI (smapi.io),
 *     ответ храним неделю;
 *   • зависимости Thunderstore и ModLinks — их же номера в каталоге.
 */

const SMAPI_API = 'https://smapi.io/api/v3.0/mods';
const WEEK = 7 * 24 * 60 * 60 * 1000;

function norm(value) {
  return String(value ?? '')
    .toLowerCase()
    .replace(/\(.*?\)/g, '')
    .replace(/[^a-z0-9а-яё]+/gi, '');
}

/** UniqueID из manifest.json в папках модов (Stardew): и свои, и чужие. */
function manifestIds(modsDir) {
  const out = new Set();
  if (!modsDir || !fs.existsSync(modsDir)) return out;
  const visit = (dir, depth) => {
    let entries;
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      if (entry.isFile() && entry.name.toLowerCase() === 'manifest.json') {
        try {
          const text = fs.readFileSync(path.join(dir, entry.name), 'utf8').replace(/^\uFEFF/, '');
          const data = JSON.parse(text.replace(/,\s*([}\]])/g, '$1'));
          if (data?.UniqueID) out.add(String(data.UniqueID).toLowerCase());
        } catch {
          /* битый манифест — не наш вопрос */
        }
      } else if (entry.isDirectory() && depth < 3 && !entry.name.startsWith('.')) {
        visit(path.join(dir, entry.name), depth + 1);
      }
    }
  };
  visit(modsDir, 0);
  return out;
}

/** Всё, по чему можно узнать установленный мод. */
function presentKeys(records, { modsDir, loaderIds = [] } = {}) {
  const ids = new Set(loaderIds.map((id) => String(id).toLowerCase()));
  const names = new Set();
  for (const record of records) {
    if (record.missing) continue;
    ids.add(String(record.id).toLowerCase());
    const nexus = /^nexus:[^:]+:(\d+)$/.exec(record.id);
    if (nexus) ids.add(`nexus#${nexus[1]}`);
    if (record.uniqueId) ids.add(String(record.uniqueId).toLowerCase());
    for (const extra of record.uniqueIds ?? []) ids.add(String(extra).toLowerCase());
    names.add(norm(record.name));
    names.add(norm(record.folder));
  }
  for (const id of manifestIds(modsDir)) ids.add(id);
  // Папки в модах, которых нет в реестре (поставлены руками): их имена тоже считаются.
  try {
    if (modsDir && fs.existsSync(modsDir)) {
      for (const entry of fs.readdirSync(modsDir, { withFileTypes: true })) names.add(norm(entry.name.replace(/\.dll$/i, '')));
    }
  } catch {
    /* папки нет — и ладно */
  }
  names.delete('');
  return { ids, names };
}

/**
 * Чего не хватает включённым модам.
 * @returns {Array<{mod: string, modId: string, missing: string, missingId: string, kind: string, resolve: object|null}>}
 */
function findMissing(records, { modsDir, loaderIds = [], hide = [] } = {}) {
  const present = presentKeys(records, { modsDir, loaderIds });
  const hidden = new Set(hide.map(String));
  const out = [];
  const seen = new Set();
  for (const record of records) {
    if (record.missing || record.enabled === false) continue;
    // Зависимости из манифеста мода: UniqueID (Stardew), «Автор-Пакет» (Thunderstore), имя (ModLinks).
    for (const dep of record.dependencies ?? []) {
      const id = String(dep ?? '').trim();
      if (!id) continue;
      const key = id.toLowerCase();
      if (present.ids.has(key) || present.names.has(norm(id))) continue;
      if (/-bepinexpack(_\w+)?$/i.test(id) || /^smapi$/i.test(id) || key === 'pathoschild.smapi') continue;
      if (seen.has(`${record.id}|${key}`)) continue;
      seen.add(`${record.id}|${key}`);
      out.push({ mod: record.name, modId: record.id, missing: id, missingId: id, kind: 'manifest', resolve: null });
    }
    // Требования со страницы Nexus: номер и имя.
    for (const req of record.requires ?? []) {
      const id = String(req?.id ?? '');
      if (!id || hidden.has(id)) continue;
      if (present.ids.has(`nexus#${id}`) || present.names.has(norm(req.name))) continue;
      if (seen.has(`${record.id}|nexus#${id}`)) continue;
      seen.add(`${record.id}|nexus#${id}`);
      out.push({ mod: record.name, modId: record.id, missing: req.name || `#${id}`, missingId: id, kind: 'nexus', resolve: { id } });
    }
  }
  return out;
}

/**
 * UniqueID модов Stardew → номер на Nexus, по сайту SMAPI.
 * Ответ сайта держим неделю: и лишний раз не спрашиваем, и без сети помним.
 */
class SmapiIndex {
  constructor({ file, fetch: fetchImpl = globalThis.fetch, now = () => Date.now(), version = '2.1' } = {}) {
    this.store = new JsonStore(file, { items: {} });
    this.fetch = fetchImpl;
    this.now = now;
    this.version = version;
  }

  cached(id) {
    const item = this.store.data.items[String(id).toLowerCase()];
    return item && this.now() - item.at < WEEK ? item : null;
  }

  /** @returns {Promise<Record<string, {nexusId: string|null, name: string|null}>>} */
  async lookup(uniqueIds) {
    const wanted = [...new Set(uniqueIds.map((id) => String(id)))].filter(Boolean);
    const out = {};
    const ask = [];
    for (const id of wanted) {
      const hit = this.cached(id);
      if (hit) out[id] = hit;
      else ask.push(id);
    }
    if (ask.length) {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), 15000);
      try {
        const response = await this.fetch(SMAPI_API, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'User-Agent': `ModLaunch/${this.version}` },
          body: JSON.stringify({
            mods: ask.slice(0, 50).map((id) => ({ id, updateKeys: [] })),
            apiVersion: '4.0.0',
            gameVersion: '1.6.15',
            platform: 'Windows',
            includeExtendedMetadata: true,
          }),
          signal: controller.signal,
        });
        const list = response.ok ? await response.json() : [];
        for (const item of Array.isArray(list) ? list : []) {
          const id = String(item?.id ?? '');
          if (!id) continue;
          const meta = item.metadata ?? {};
          const nexusId = meta.nexusID ?? meta.nexusId ?? null;
          const entry = { nexusId: nexusId ? String(nexusId) : null, name: meta.name ?? null, at: this.now() };
          this.store.data.items[id.toLowerCase()] = entry;
          out[id] = entry;
        }
        this.store.save();
      } catch {
        /* сайт SMAPI недоступен — поставить недостающее можно будет руками */
      } finally {
        clearTimeout(timer);
      }
    }
    return out;
  }
}

module.exports = { findMissing, presentKeys, manifestIds, SmapiIndex, norm };
