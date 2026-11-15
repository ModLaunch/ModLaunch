// Нагрузочный тест сервера ModLaunch (Firebase Auth + Firestore REST) — так, как ходит программа.
// Каждый «пользователь»: анонимный вход → настройки рынка → сводка снятого → чёрный список → каталог Hub (300) →
// витрина рынка → свежие отзывы → свой отзыв → сердцебиение «в сети» (для вошедших с почтой).
//
// По умолчанию — локальный эмулятор (tools/integration.sh поднимает его так же). Для настоящего теста перед релизом —
// ОТДЕЛЬНЫЙ проект Firebase (staging) с теми же правилами:  --project modlaunch-staging --key <web api key>.
// Боевой проект (modhub-reviews) скрипт трогать отказывается.
//
//   node tools/loadtest/firestore-load.mjs --users 200 --concurrency 50
//   node tools/loadtest/firestore-load.mjs --project modlaunch-staging --key AIza... --users 5000 --concurrency 500 --ramp 300
const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => (a.startsWith('--') ? [...acc, [a.slice(2), all[i + 1]?.startsWith('--') || all[i + 1] === undefined ? 'true' : all[i + 1]]] : acc), []));
const project = args.project ?? 'demo-modlaunch';
if (project === 'modhub-reviews') { console.error('Боевой проект нагружать нельзя. Создайте staging-проект.'); process.exit(2); }
const emulator = !args.key;
const key = args.key ?? 'emulator';
const users = Number(args.users ?? 200);
const concurrency = Number(args.concurrency ?? 50);
const rampSeconds = Number(args.ramp ?? 0);
const AUTH = emulator ? 'http://127.0.0.1:9099/identitytoolkit.googleapis.com/v1' : 'https://identitytoolkit.googleapis.com/v1';
const FS = emulator ? 'http://127.0.0.1:8080/v1' : 'https://firestore.googleapis.com/v1';
const DOCS = `projects/${project}/databases/(default)/documents`;

const stats = new Map();
function record(op, ms, ok, code) {
  const s = stats.get(op) ?? { ms: [], ok: 0, fail: 0, codes: {} };
  s.ms.push(ms); ok ? s.ok++ : s.fail++;
  if (!ok) s.codes[code] = (s.codes[code] ?? 0) + 1;
  stats.set(op, s);
}

async function call(op, url, { method = 'POST', body, token } = {}) {
  const t0 = performance.now();
  try {
    const r = await fetch(url, { method, headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) }, body: body ? JSON.stringify(body) : undefined });
    const text = await r.text();
    // 404 на документе — «нет», это не ошибка нагрузки.
    const ok = r.ok || r.status === 404;
    record(op, performance.now() - t0, ok, r.status);
    return ok && text ? JSON.parse(text) : null;
  } catch (e) {
    record(op, performance.now() - t0, false, e.code ?? 'net');
    return null;
  }
}

const query = (collection, extra = {}) => ({ structuredQuery: { from: [{ collectionId: collection }], ...extra } });

async function session(i) {
  const auth = await call('auth.signUp(anon)', `${AUTH}/accounts:signUp?key=${key}`, { body: { returnSecureToken: true } });
  const token = auth?.idToken;
  const uid = auth?.localId;
  if (!token) return;
  await call('get config/market', `${FS}/${DOCS}/config/market?key=${key}`, { method: 'GET', token });
  await call('get config/takedowns', `${FS}/${DOCS}/config/takedowns?key=${key}`, { method: 'GET', token });
  await call('get config/blocklist', `${FS}/${DOCS}/config/blocklist?key=${key}`, { method: 'GET', token });
  await call('query hub catalog (300)', `${FS}/${DOCS}:runQuery?key=${key}`, { body: query('creations', { orderBy: [{ field: { fieldPath: 'updated' }, direction: 'DESCENDING' }], limit: 300 }), token });
  await call('query storefront (300)', `${FS}/${DOCS}:runQuery?key=${key}`, {
    body: query('market', { where: { fieldFilter: { field: { fieldPath: 'status' }, op: 'EQUAL', value: { stringValue: 'active' } } }, orderBy: [{ field: { fieldPath: 'updated' }, direction: 'DESCENDING' }], limit: 300 }), token,
  });
  const since = new Date(Date.now() - 3600_000).toISOString();
  await call('query fresh reviews', `${FS}/${DOCS}:runQuery?key=${key}`, {
    body: query('reviews', { where: { fieldFilter: { field: { fieldPath: 'updated' }, op: 'GREATER_THAN_OR_EQUAL', value: { timestampValue: since } } }, orderBy: [{ field: { fieldPath: 'updated' }, direction: 'ASCENDING' }], limit: 500 }), token,
  });
  // Каждый пятый оставляет отзыв (как в программе: reviews/{uid}_{key}).
  if (i % 5 === 0) {
    const k = (i % 16).toString(16).padStart(32, '0');
    await call('write review', `${FS}/${DOCS}:commit?key=${key}`, {
      token, body: { writes: [{
        update: { name: `${DOCS}/reviews/${uid}_${k}`, fields: {
          game: { stringValue: 'subnautica' }, mod: { stringValue: 'load-test' }, modName: { stringValue: 'Load test' }, key: { stringValue: k },
          uid: { stringValue: uid }, name: { stringValue: 'Load' }, stars: { integerValue: '5' }, text: { stringValue: 'ok' }, deleted: { booleanValue: false }, app: { stringValue: 'loadtest' },
        } },
        updateTransforms: [{ fieldPath: 'created', setToServerValue: 'REQUEST_TIME' }, { fieldPath: 'updated', setToServerValue: 'REQUEST_TIME' }],
      }] },
    });
  }
}

const started = performance.now();
let next = 0;
async function worker(w) {
  while (true) {
    const i = next++;
    if (i >= users) return;
    // Плавный разгон (правило Firestore «500/50/5»): пользователи стартуют равномерно за --ramp секунд.
    if (rampSeconds > 0) {
      const due = started + (i / users) * rampSeconds * 1000;
      const wait = due - performance.now();
      if (wait > 0) await new Promise((r) => setTimeout(r, wait));
    }
    await session(i);
  }
}
await Promise.all(Array.from({ length: Math.min(concurrency, users) }, (_, w) => worker(w)));
const seconds = (performance.now() - started) / 1000;

const pct = (arr, p) => { const s = [...arr].sort((a, b) => a - b); return s[Math.min(s.length - 1, Math.floor((p / 100) * s.length))] ?? 0; };
let total = 0, failed = 0;
console.log(`\n${users} пользователей, параллельно ${concurrency}, ${seconds.toFixed(1)} с, проект ${project}${emulator ? ' (эмулятор)' : ''}\n`);
console.log('операция'.padEnd(28) + 'всего'.padStart(7) + 'ошибок'.padStart(8) + 'p50 мс'.padStart(9) + 'p95 мс'.padStart(9) + 'p99 мс'.padStart(9) + '  коды ошибок');
for (const [op, s] of stats) {
  total += s.ok + s.fail; failed += s.fail;
  console.log(op.padEnd(28) + String(s.ok + s.fail).padStart(7) + String(s.fail).padStart(8) + pct(s.ms, 50).toFixed(0).padStart(9) + pct(s.ms, 95).toFixed(0).padStart(9) + pct(s.ms, 99).toFixed(0).padStart(9) + '  ' + JSON.stringify(s.codes));
}
console.log(`\nзапросов: ${total}, ошибок: ${failed} (${((failed / Math.max(1, total)) * 100).toFixed(2)} %), ${(total / seconds).toFixed(0)} запросов/с`);
process.exit(failed / Math.max(1, total) > 0.01 ? 1 : 0);
