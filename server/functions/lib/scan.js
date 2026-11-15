// Проверка выложенных файлов через VirusTotal (по отпечатку SHA-256; неизвестный файл отправляется на анализ).
// Опасное — сразу снимается (takedowns + сводка + чёрный список) и попадает в очередь модерации.
import { FieldValue, Timestamp } from 'firebase-admin/firestore';

const VT = 'https://www.virustotal.com/api/v3';

/** Итог анализа VirusTotal: malicious ≥ 3 движков — опасно, 1–2 — на ручную проверку. */
export function verdictOf(stats) {
  const bad = (stats?.malicious ?? 0);
  const suspicious = (stats?.suspicious ?? 0);
  if (bad >= 3) return 'malicious';
  if (bad + suspicious >= 1) return 'suspicious';
  return 'clean';
}

export async function lookup(apiKey, sha256, fetchImpl = fetch) {
  const r = await fetchImpl(`${VT}/files/${sha256}`, { headers: { 'x-apikey': apiKey } });
  if (r.status === 404) return null;
  if (!r.ok) throw new Error(`VirusTotal ${r.status}`);
  const json = await r.json();
  return json?.data?.attributes?.last_analysis_stats ?? null;
}

export async function upload(apiKey, bytes, fileName, fetchImpl = fetch) {
  const form = new FormData();
  form.append('file', new Blob([bytes]), fileName || 'file.zip');
  const r = await fetchImpl(`${VT}/files`, { method: 'POST', headers: { 'x-apikey': apiKey }, body: form });
  if (!r.ok) throw new Error(`VirusTotal upload ${r.status}`);
  return (await r.json())?.data?.id ?? null;
}

/** Собрать файл лота рынка из кусков base64 (market/{id}/files/{n}). */
export async function listingBytes(db, id, chunks) {
  const parts = [];
  for (let n = 0; n < chunks; n++) parts.push((await db.doc(`market/${id}/files/${n}`).get()).data()?.data ?? '');
  return Buffer.from(parts.join(''), 'base64');
}

/** Собрать файл мода Hub из кусков (blobs/{id}__{version}__{n}). */
export async function creationBytes(db, id, version, chunks) {
  const parts = [];
  for (let n = 0; n < chunks; n++) parts.push((await db.doc(`blobs/${id}__${version}__${n}`).get()).data()?.data ?? '');
  return Buffer.from(parts.join(''), 'base64');
}

/**
 * Проверить файл лота или мода Hub. kind: listing | creation. Результат пишется в scans/{sha256}; опасное — снимается.
 * Возвращает вердикт: clean | suspicious | malicious | queued (отправлен на анализ, проверим позже).
 */
export async function scanItem(db, apiKey, { kind, id, sha256, title, owner, bytes }, fetchImpl = fetch, now = new Date()) {
  if (!/^[0-9a-f]{64}$/.test(sha256 ?? '')) return 'skip';
  const done = await db.doc(`scans/${sha256}`).get();
  let stats = done.exists && done.data().verdict !== 'queued' ? done.data().stats : await lookup(apiKey, sha256, fetchImpl);
  if (!stats) {
    if (bytes?.length) await upload(apiKey, bytes, `${id}.zip`, fetchImpl);
    await db.doc(`scans/${sha256}`).set({ verdict: 'queued', kind, id, title, owner, at: Timestamp.fromDate(now) }, { merge: true });
    return 'queued';
  }
  const verdict = verdictOf(stats);
  await db.doc(`scans/${sha256}`).set({ verdict, stats, kind, id, title, owner, at: Timestamp.fromDate(now) }, { merge: true });
  if (verdict === 'malicious') {
    const batch = db.batch();
    batch.set(db.doc(`takedowns/${kind}_${id}`), {
      kind, target: id, reason: 'malware', note: `VirusTotal: ${stats.malicious} detections`, sha256, title: String(title ?? '').slice(0, 120),
      owner: String(owner ?? '').slice(0, 128), by: 'server', at: Timestamp.fromDate(now),
    });
    batch.set(db.doc('config/takedowns'), { ids: FieldValue.arrayUnion(`${kind}_${id}`), updated: Timestamp.fromDate(now) }, { merge: true });
    batch.set(db.doc('config/blocklist'), { hashes: FieldValue.arrayUnion(sha256), updated: Timestamp.fromDate(now), by: 'server' }, { merge: true });
    await batch.commit();
  }
  if (verdict !== 'clean') {
    await db.doc(`reports/${kind}_${id}__server`).set({
      kind, target: id, owner: String(owner ?? ''), title: String(title ?? '').slice(0, 120), reason: 'malware',
      text: `VirusTotal: malicious ${stats.malicious ?? 0}, suspicious ${stats.suspicious ?? 0}${verdict === 'malicious' ? ' — снято автоматически' : ' — проверьте вручную'}`,
      uid: 'server', status: 'open', created: Timestamp.fromDate(now),
    });
  }
  return verdict;
}
