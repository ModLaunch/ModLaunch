'use strict';

const crypto = require('node:crypto');
const path = require('node:path');
const { JsonStore } = require('./store');

/**
 * Отзывы и оценки модов — общие для всех, у кого стоит ModHub.
 *
 * Друг ставит оценку у себя — через пару минут её видят все остальные.
 * Для этого нужен общий сервер. Сервер здесь — база Cloud Firestore
 * в бесплатном тарифе Firebase: своего кода на сервере нет вовсе, а кто
 * что может писать, решают правила базы (reviews/firestore.rules).
 * Бесплатный тариф Firebase не засыпает от простоя и не требует карты.
 *
 * Как устроено:
 *
 *   • Вход анонимный. При первом отзыве программа получает от Firebase
 *     безымянную учётную запись и хранит её у себя. Ни почты, ни пароля.
 *     По ней правила базы и понимают, чей отзыв: изменить или удалить
 *     его можно только с того же компьютера.
 *
 *   • Один отзыв на мод от одной установки. Номер документа — это
 *     «владелец_мод», поэтому второй отзыв просто заменяет первый.
 *
 *   • Время записи ставит сервер, а не компьютер: подделать дату
 *     нельзя, и по ней программа забирает только новое с прошлого раза.
 *     Удаление — это пометка deleted, а не стирание: иначе чужое
 *     удаление никто бы не заметил.
 *
 *   • Кончился бесплатный лимит Firebase на сегодня — программа
 *     не ломается и ничего не просит оплатить: пишет «отзывы пока что
 *     временно недоступны», показывает сохранённое и полчаса не
 *     стучится на сервер. Лимит обновляется каждые сутки сам.
 *
 *   • Все отзывы лежат копией на диске (reviews.json). Средняя оценка
 *     считается здесь же, поэтому карточки получают цифры мгновенно,
 *     даже без интернета, — просто последние известные.
 */

const FIRESTORE = 'https://firestore.googleapis.com/v1';
const IDENTITY = 'https://identitytoolkit.googleapis.com/v1';
const SECURE_TOKEN = 'https://securetoken.googleapis.com/v1';

const PAGE = 300;
const TIMEOUT_MS = 15000;
const MAX_TEXT = 1000;
const MAX_NAME = 32;
const EPOCH = '1970-01-01T00:00:00Z';
// Полная сверка (с ней пропадают отзывы, стёртые хозяином в консоли) —
// раз в неделю: это самая «дорогая» операция для бесплатного лимита
// Firebase (50 000 чтений в сутки на всех), обычная синхронизация
// читает только новое.
const FULL_SYNC_MS = 7 * 24 * 60 * 60 * 1000;
// Лимит кончился или сервер лёг — полчаса не стучимся: показываем
// «временно недоступно» и то, что уже лежит на диске.
const PAUSE_MS = 30 * 60 * 1000;
// С этой версии оценку можно поставить только моду, который скачан через
// ModHub и с которым хотя бы раз запускали игру (core/playlog.js). Отзыв,
// записанный такой версией, помечен в поле app — по нему окно и ставит
// у отзыва метку «Играл».
const PLAYED_SINCE = '1.9.7';

/** Ошибка, которую можно показать человеку как есть. */
class ReviewsError extends Error {
  constructor(code, message, extra = {}) {
    super(message || code);
    this.code = code;
    Object.assign(this, extra);
  }
}

/* ------------------------------------------------------------------ *
 *  Значения Firestore: в REST каждое поле обёрнуто в свой тип.
 * ------------------------------------------------------------------ */

function toValue(value) {
  if (value === null || value === undefined) return { nullValue: null };
  if (typeof value === 'boolean') return { booleanValue: value };
  if (typeof value === 'number') return Number.isInteger(value) ? { integerValue: String(value) } : { doubleValue: value };
  return { stringValue: String(value) };
}

function fromValue(value) {
  if (!value || typeof value !== 'object') return null;
  if ('stringValue' in value) return value.stringValue;
  if ('integerValue' in value) return Number(value.integerValue);
  if ('doubleValue' in value) return Number(value.doubleValue);
  if ('booleanValue' in value) return Boolean(value.booleanValue);
  if ('timestampValue' in value) return value.timestampValue;
  if ('nullValue' in value) return null;
  return null;
}

function toFields(object) {
  return Object.fromEntries(Object.entries(object).map(([key, value]) => [key, toValue(value)]));
}

function fromFields(fields) {
  return Object.fromEntries(Object.entries(fields ?? {}).map(([key, value]) => [key, fromValue(value)]));
}

/** Версия программы из поля app: «1.9.7» → [1, 9, 7]. Не версия — null. */
function parseVersion(value) {
  const match = /^(\d+)\.(\d+)(?:\.(\d+))?/.exec(String(value ?? '').trim());
  return match ? [Number(match[1]), Number(match[2]), Number(match[3] ?? 0)] : null;
}

/** Записан ли отзыв версией не старше заданной. */
function appAtLeast(app, since = PLAYED_SINCE) {
  const have = parseVersion(app);
  const need = parseVersion(since);
  if (!have || !need) return false;
  for (let i = 0; i < 3; i += 1) {
    if (have[i] !== need[i]) return have[i] > need[i];
  }
  return true;
}

/** Ключ мода в документе: имена модов бывают со слешами и прочим, что в id нельзя. */
function modKey(game, mod) {
  return crypto.createHash('sha256').update(`${game}|${mod}`).digest('hex').slice(0, 32);
}

/** Ключ для средней оценки, одинаковый в основном процессе и в окне. */
function statsKey(game, mod) {
  return `${game}|${mod}`;
}

/**
 * Время Firestore как число микросекунд. Строки сравнивать нельзя:
 * сервер обрезает нули в дробной части, и «…02.1Z» оказалось бы позже «…02.123Z».
 */
function tsValue(value) {
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d+))?Z$/.exec(String(value ?? ''));
  if (!match) {
    const parsed = Date.parse(value);
    return Number.isNaN(parsed) ? 0 : parsed * 1000;
  }
  const micros = Number((match[2] ?? '').padEnd(6, '0').slice(0, 6));
  return Date.parse(`${match[1]}Z`) * 1000 + micros;
}

function cleanText(value, max) {
  return String(value ?? '')
    .replace(/\r\n?/g, '\n')
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim()
    .slice(0, max);
}

/* ------------------------------------------------------------------ *
 *  Клиент
 * ------------------------------------------------------------------ */

class ReviewsClient {
  /**
   * @param {object} options
   * @param {{projectId?: string, apiKey?: string}} options.config
   * @param {string} options.dataDir
   * @param {string} [options.version]
   * @param {typeof fetch} [options.fetch]
   */
  constructor({ config, dataDir, version = '', fetch: fetchImpl, account = null } = {}) {
    this.config = {
      projectId: String(config?.projectId ?? '').trim(),
      apiKey: String(config?.apiKey ?? '').trim(),
    };
    this.version = version;
    this.fetch = fetchImpl ?? globalThis.fetch;
    this.store = new JsonStore(path.join(dataDir, 'reviews.json'), { auth: null, lastSync: EPOCH, reviews: {} });
    this.syncing = null;
    this.lastSyncAt = 0;
    this.lastError = null;
    this.pausedUntil = 0;
    this.now = () => Date.now();
    // С 1.9.4 «кто пишет» решает аккаунт (core/account.js): безымянная
    // запись компьютера или аккаунт с почтой. Без него — как в 1.9.2.
    this.account = account;
  }

  /** Номер того, от чьего имени сейчас пишутся отзывы. */
  currentUid() {
    return this.account ? this.account.uid() : (this.store.data.auth?.uid ?? null);
  }

  /** Безымянная запись, которую 1.9.2 хранил в reviews.json, — для переезда в аккаунт. */
  legacyAuth() {
    const auth = this.store.data.auth;
    return auth?.uid && auth?.refreshToken ? { uid: auth.uid, refreshToken: auth.refreshToken } : null;
  }

  get configured() {
    return Boolean(this.config.projectId && this.config.apiKey);
  }

  get documents() {
    return `projects/${this.config.projectId}/databases/(default)/documents`;
  }

  status() {
    return {
      configured: this.configured,
      projectId: this.config.projectId || null,
      uid: this.currentUid(),
      lastSync: this.lastSyncAt ? new Date(this.lastSyncAt).toISOString() : null,
      count: this.list().length,
      error: this.lastError,
      unavailable: this.lastError === 'BUSY',
    };
  }

  requireConfig() {
    if (!this.configured) throw new ReviewsError('NOT_CONFIGURED');
  }

  /* --- сеть --- */

  async request(url, { method = 'GET', body, token, form = false } = {}) {
    if (this.pausedUntil > this.now()) throw new ReviewsError('BUSY', 'paused');
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
    let response;
    try {
      const headers = { Accept: 'application/json' };
      if (token) headers.Authorization = `Bearer ${token}`;
      let payload;
      if (body !== undefined) {
        headers['Content-Type'] = form ? 'application/x-www-form-urlencoded' : 'application/json';
        payload = form ? new URLSearchParams(body).toString() : JSON.stringify(body);
      }
      response = await this.fetch(url, { method, headers, body: payload, signal: controller.signal });
    } catch (error) {
      throw new ReviewsError('OFFLINE', error?.name === 'AbortError' ? 'timeout' : error?.message);
    } finally {
      clearTimeout(timer);
    }

    let data = null;
    try {
      data = await response.json();
    } catch {
      data = null;
    }
    if (response.ok) return data;

    // Google кладёт причину то в status, то в message — смотрим в оба.
    const error = Array.isArray(data) ? data[0]?.error : data?.error;
    const reason = [error?.status, error?.message].filter(Boolean).join(': ') || String(response.status);
    const status = response.status;
    // Кончился бесплатный лимит на сегодня, нужен платный тариф или сервер
    // перегружен — для человека это одно: «временно недоступно».
    if (status === 429 || status === 503 || /RESOURCE_EXHAUSTED|quota|billing|exceeded|UNAVAILABLE|TOO_MANY_ATTEMPTS/i.test(reason)) {
      this.pausedUntil = this.now() + PAUSE_MS;
      throw new ReviewsError('BUSY', reason, { status });
    }
    if (/OPERATION_NOT_ALLOWED|ADMIN_ONLY_OPERATION|CONFIGURATION_NOT_FOUND|has not been used|is disabled/i.test(reason)) {
      throw new ReviewsError('AUTH_DISABLED', reason, { status });
    }
    if (/API.?key/i.test(reason)) throw new ReviewsError('BAD_KEY', reason, { status });
    // «Базы нет» Google пишет так: «The database (default) does not exist for
    // project …». А «такого отзыва ещё нет» — «Document "projects/…/databases/
    // (default)/documents/…" not found»: в пути тоже есть слово databases,
    // поэтому ищем именно «does not exist», а не просто «database».
    if (status === 404 && /database\s+\S+\s+does not exist|does not exist for project/i.test(reason)) {
      throw new ReviewsError('NO_DATABASE', reason, { status });
    }
    if (status === 404) throw new ReviewsError('NOT_FOUND', reason, { status });
    if (status === 403 || /PERMISSION_DENIED/.test(reason)) throw new ReviewsError('DENIED', reason, { status });
    throw new ReviewsError('SERVER', reason, { status });
  }

  /* --- вход --- */

  /**
   * Действующий токен анонимной учётной записи. Первый раз — регистрация,
   * дальше — обмен refresh-токена на новый (живёт час).
   */
  async token() {
    this.requireConfig();
    if (this.account) {
      try {
        return await this.account.token();
      } catch (error) {
        // Ошибки входа переводим в ошибки отзывов, которые окно уже умеет показывать.
        const map = { OFFLINE: 'OFFLINE', BUSY: 'BUSY', NOT_ENABLED: 'AUTH_DISABLED', BAD_KEY: 'BAD_KEY', SESSION: 'SESSION', TOO_MANY: 'BUSY' };
        throw new ReviewsError(map[error.code] ?? 'SERVER', error.message);
      }
    }
    const key = encodeURIComponent(this.config.apiKey);
    let auth = this.store.data.auth;

    if (auth?.idToken && auth.expiresAt && Date.now() < auth.expiresAt - 60_000) return auth;

    if (auth?.refreshToken) {
      try {
        const data = await this.request(`${SECURE_TOKEN}/token?key=${key}`, {
          method: 'POST',
          form: true,
          body: { grant_type: 'refresh_token', refresh_token: auth.refreshToken },
        });
        auth = {
          uid: data.user_id ?? auth.uid,
          idToken: data.id_token,
          refreshToken: data.refresh_token ?? auth.refreshToken,
          expiresAt: Date.now() + Number(data.expires_in ?? 3600) * 1000,
        };
        this.store.data.auth = auth;
        this.store.save();
        return auth;
      } catch (error) {
        // Учётную запись удалили на сервере — заведём новую. Остальное пробрасываем.
        if (error.code === 'OFFLINE') throw error;
      }
    }

    const data = await this.request(`${IDENTITY}/accounts:signUp?key=${key}`, {
      method: 'POST',
      body: { returnSecureToken: true },
    });
    auth = {
      uid: data.localId,
      idToken: data.idToken,
      refreshToken: data.refreshToken,
      expiresAt: Date.now() + Number(data.expiresIn ?? 3600) * 1000,
    };
    this.store.data.auth = auth;
    this.store.save();
    return auth;
  }

  /* --- чтение --- */

  /**
   * Забирает с сервера всё, что изменилось с прошлого раза.
   * Первый раз — все отзывы, дальше — только новые и исправленные.
   * @param {{force?: boolean, maxAgeMs?: number}} [options]
   */
  async sync({ force = false, maxAgeMs = 20_000 } = {}) {
    if (!this.configured) return this.status();
    if (!force && Date.now() - this.lastSyncAt < maxAgeMs) return this.status();
    if (this.syncing) return this.syncing;

    this.syncing = (async () => {
      try {
        // Раз в неделю — полная сверка: так пропадают и отзывы, которые хозяин
        // сервера стёр руками в консоли Firebase (обычная синхронизация видит
        // только новое и изменённое, а стёртого на сервере уже нет).
        const full = Date.now() - (this.store.data.lastFull ?? 0) > FULL_SYNC_MS;
        let since = full ? EPOCH : this.store.data.lastSync || EPOCH;
        const seen = new Set();
        let complete = false;
        for (let round = 0; round < 50; round += 1) {
          const rows = await this.request(`${FIRESTORE}/${this.documents}:runQuery?key=${encodeURIComponent(this.config.apiKey)}`, {
            method: 'POST',
            body: {
              structuredQuery: {
                from: [{ collectionId: 'reviews' }],
                where: {
                  fieldFilter: {
                    field: { fieldPath: 'updated' },
                    op: 'GREATER_THAN_OR_EQUAL',
                    value: { timestampValue: since },
                  },
                },
                orderBy: [{ field: { fieldPath: 'updated' }, direction: 'ASCENDING' }],
                limit: PAGE,
              },
            },
          });

          const docs = (Array.isArray(rows) ? rows : []).map((row) => row?.document).filter(Boolean);
          let progressed = 0;
          for (const doc of docs) {
            const id = String(doc.name).split('/').pop();
            if (!seen.has(id)) {
              seen.add(id);
              progressed += 1;
            }
            const review = { id, ...fromFields(doc.fields) };
            this.store.data.reviews[id] = review;
            if (review.updated && tsValue(review.updated) > tsValue(since)) since = review.updated;
          }
          // Страница неполная или ничего нового (одна и та же секунда на границе) — всё.
          if (docs.length < PAGE || progressed === 0) {
            complete = true;
            break;
          }
        }
        if (full && complete) {
          for (const id of Object.keys(this.store.data.reviews)) {
            if (!seen.has(id)) delete this.store.data.reviews[id];
          }
          this.store.data.lastFull = Date.now();
        }
        this.store.data.lastSync = since;
        this.store.save();
        this.lastSyncAt = Date.now();
        this.lastError = null;
        return this.status();
      } catch (error) {
        this.lastError = error.code ?? 'SERVER';
        throw error;
      } finally {
        this.syncing = null;
      }
    })();
    return this.syncing;
  }

  /** Все живые отзывы (без удалённых), новые сверху. */
  list(game = null, mod = null) {
    const all = Object.values(this.store.data.reviews).filter(
      (r) => r && !r.deleted && r.stars >= 1 && (!game || r.game === game) && (mod === null || r.mod === mod)
    );
    return all.sort((a, b) => tsValue(b.updated) - tsValue(a.updated));
  }

  /** Отзыв с этого компьютера на этот мод — чтобы показать его в форме. */
  mine(game, mod) {
    const uid = this.currentUid();
    if (!uid) return null;
    return this.list(game, mod).find((r) => r.uid === uid) ?? null;
  }

  /** Все живые отзывы, написанные от текущего имени, — для удаления аккаунта. */
  own() {
    const uid = this.currentUid();
    return uid ? this.list().filter((r) => r.uid === uid) : [];
  }

  /** Отзывы мода для окна: у каждого отметка «ваш». */
  forMod(game, mod) {
    const uid = this.currentUid();
    return this.list(game, mod).map((r) => ({
      id: r.id,
      name: r.name,
      stars: r.stars,
      text: r.text,
      created: r.created,
      updated: r.updated,
      mine: Boolean(uid && r.uid === uid),
      admin: r.role === 'admin',
      // Автор скачал мод через ModHub и играл с ним: так пишет только 1.9.7 и новее.
      played: appAtLeast(r.app),
    }));
  }

  /**
   * Администратор скрывает чужой отзыв (спам, ругань). На сервере это
   * пометка «удалён модератором»: у всех отзыв пропадает при следующей
   * синхронизации, а автор не может вернуть его обратно.
   */
  async moderate(reviewId) {
    this.requireConfig();
    const id = String(reviewId ?? '');
    if (!/^[A-Za-z0-9]{6,128}_[0-9a-f]{32}$/.test(id)) throw new ReviewsError('BAD_INPUT', 'review');
    const auth = await this.token();
    const write = {
      update: { name: `${this.documents}/reviews/${id}`, fields: toFields({ deleted: true, stars: 0, text: '', moderated: true }) },
      updateMask: { fieldPaths: ['deleted', 'stars', 'text', 'moderated'] },
      updateTransforms: [{ fieldPath: 'updated', setToServerValue: 'REQUEST_TIME' }],
      currentDocument: { exists: true },
    };
    await this.request(`${FIRESTORE}/${this.documents}:commit?key=${encodeURIComponent(this.config.apiKey)}`, {
      method: 'POST',
      token: auth.idToken,
      body: { writes: [write] },
    });
    const local = this.store.data.reviews[id];
    if (local) {
      this.store.data.reviews[id] = { ...local, deleted: true, stars: 0, text: '', moderated: true };
      this.store.save();
    }
    await this.sync({ force: true }).catch(() => {});
    return true;
  }

  /** Средняя оценка и число отзывов по каждому моду: { "игра|мод": { avg, count } }. */
  stats() {
    const sums = new Map();
    for (const r of this.list()) {
      const key = statsKey(r.game, r.mod);
      const entry = sums.get(key) ?? { sum: 0, count: 0 };
      entry.sum += r.stars;
      entry.count += 1;
      sums.set(key, entry);
    }
    return Object.fromEntries(
      [...sums.entries()].map(([key, { sum, count }]) => [key, { avg: Math.round((sum / count) * 100) / 100, count }])
    );
  }

  /* --- запись --- */

  async commit(docId, fields, { create }) {
    const auth = await this.token();
    const name = `${this.documents}/reviews/${docId}`;
    const write = {
      update: { name, fields: toFields(fields) },
      updateTransforms: [{ fieldPath: 'updated', setToServerValue: 'REQUEST_TIME' }],
      currentDocument: { exists: !create },
    };
    if (create) write.updateTransforms.push({ fieldPath: 'created', setToServerValue: 'REQUEST_TIME' });
    else {
      // role в маске без значения — сервер его стирает: отметка «Админ»
      // не остаётся на отзыве, если права сняли.
      const paths = Object.keys(fields);
      if (!paths.includes('role')) paths.push('role');
      write.updateMask = { fieldPaths: paths };
    }

    await this.request(`${FIRESTORE}/${this.documents}:commit?key=${encodeURIComponent(this.config.apiKey)}`, {
      method: 'POST',
      token: auth.idToken,
      body: { writes: [write] },
    });
  }

  /** Есть ли уже документ на сервере — от этого зависит «создать» или «изменить». */
  async exists(docId) {
    try {
      await this.request(`${FIRESTORE}/${this.documents}/reviews/${docId}?key=${encodeURIComponent(this.config.apiKey)}`);
      return true;
    } catch (error) {
      if (error.code === 'NOT_FOUND') return false;
      throw error;
    }
  }

  /**
   * Публикует или заменяет свой отзыв о моде.
   *
   * played — играл ли человек с модом (решает основной процесс по plays.json).
   * Правка старого отзыва без игры не трогает поле app: метка «Играл»
   * не появляется у отзыва, если автор с модом так и не играл.
   *
   * @param {{game: string, mod: string, modName?: string, stars: number, text?: string, name: string, played?: boolean}} input
   */
  async submit(input) {
    this.requireConfig();
    const game = cleanText(input?.game, 40);
    const mod = cleanText(input?.mod, 200);
    const stars = Math.round(Number(input?.stars));
    const name = cleanText(input?.name, MAX_NAME).replace(/\s+/g, ' ');
    const text = cleanText(input?.text, MAX_TEXT);
    if (!game || !mod) throw new ReviewsError('BAD_INPUT', 'mod');
    if (!(stars >= 1 && stars <= 5)) throw new ReviewsError('BAD_INPUT', 'stars');
    if (!name) throw new ReviewsError('BAD_INPUT', 'name');

    const auth = await this.token();
    const key = modKey(game, mod);
    const docId = `${auth.uid}_${key}`;
    const fields = {
      game,
      mod,
      modName: cleanText(input?.modName || mod, 200),
      key,
      uid: auth.uid,
      name,
      stars,
      text,
      deleted: false,
    };
    if (input?.played !== false) fields.app = String(this.version).slice(0, 20);
    // Отзыв администратора — с отметкой «Админ» (правила базы проверят,
    // что это правда администратор).
    if (this.account?.profile().admin) fields.role = 'admin';

    const create = !(await this.exists(docId));
    await this.commit(docId, fields, { create });
    await this.sync({ force: true }).catch(() => {});

    // Сеть могла не отдать запись сразу — кладём свою копию, чтобы человек видел её.
    const now = new Date().toISOString();
    const saved = this.store.data.reviews[docId];
    if (!saved || saved.deleted || saved.stars !== stars || saved.text !== text) {
      this.store.data.reviews[docId] = { ...(saved ?? {}), id: docId, ...fields, created: saved?.created ?? now, updated: now };
      this.store.save();
    }
    return this.forMod(game, mod).find((r) => r.id === docId) ?? null;
  }

  /** Убирает свой отзыв (на сервере он остаётся пометкой «удалён»). */
  async remove(input) {
    this.requireConfig();
    const game = cleanText(input?.game, 40);
    const mod = cleanText(input?.mod, 200);
    const auth = await this.token();
    const key = modKey(game, mod);
    const docId = `${auth.uid}_${key}`;
    const current = this.store.data.reviews[docId];

    await this.commit(
      docId,
      {
        game,
        mod,
        modName: cleanText(current?.modName || mod, 200),
        key,
        uid: auth.uid,
        name: cleanText(current?.name || '—', MAX_NAME),
        stars: 0,
        text: '',
        deleted: true,
        app: String(this.version).slice(0, 20),
      },
      { create: false }
    );
    if (this.store.data.reviews[docId]) {
      this.store.data.reviews[docId] = { ...this.store.data.reviews[docId], deleted: true, stars: 0, text: '' };
      this.store.save();
    }
    await this.sync({ force: true }).catch(() => {});
    return true;
  }
}

module.exports = {
  ReviewsClient,
  ReviewsError,
  modKey,
  statsKey,
  toFields,
  fromFields,
  cleanText,
  tsValue,
  appAtLeast,
  PLAYED_SINCE,
};
