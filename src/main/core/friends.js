'use strict';

const crypto = require('node:crypto');
const path = require('node:path');
const { JsonStore } = require('./store');

/**
 * Друзья и «кто во что играет» (2.1).
 *
 * Сервер — та же база Cloud Firestore, что у отзывов и аккаунтов: своего
 * кода на сервере нет, кто что может читать и писать, решают правила базы
 * (firebase/friends.rules — их один раз вставляют в консоли Firebase).
 *
 *   • Друзья есть только у аккаунта с почтой: безымянная запись живёт на
 *     одном компьютере, а список друзей должен переезжать вместе с человеком.
 *
 *   • У каждого есть код друга из восьми знаков (codes/{код} → uid). Код
 *     диктуют голосом или пишут в чат, поэтому в нём нет 0/O и 1/I.
 *
 *   • Дружба — один документ на пару (friendships/{меньший uid}_{больший}):
 *     запрос «ждёт», пока второй не примет. Удалить его может любой из двоих.
 *
 *   • «В сети» и «в игре» — поле state в profiles/{uid}. Раз в пять минут,
 *     пока ModHub открыт, сервер ставит там своё время (seen). Друг в сети,
 *     пока seen не старше шести минут по часам сервера: упавший ModHub или
 *     выдернутый интернет сами становятся «не в сети», а неверные часы
 *     на чьём-то компьютере ни на что не влияют.
 *
 *   • Бесплатный лимит Firebase — 50 000 чтений и 20 000 записей в сутки
 *     на всех сразу. Поэтому друзей спрашивают, только пока их видно, и не
 *     чаще раза в полминуты; список дружб — раз в пять минут, а отметка
 *     «в сети» — одна запись в пять минут.
 */

const FIRESTORE = 'https://firestore.googleapis.com/v1';
const TIMEOUT_MS = 15000;
const ONLINE_MS = 6 * 60 * 1000;
const BEAT_MS = 5 * 60 * 1000;
const MIN_REFRESH_MS = 30 * 1000;
// Список дружб меняется редко — его спрашиваем раз в пять минут (и сразу
// после своих действий), а «кто в сети» — при каждом обновлении.
const LINKS_MS = 5 * 60 * 1000;
const PAUSE_MS = 10 * 60 * 1000;
const MAX_LINKS = 200;
const MAX_NAME = 32;
const CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const CODE_RE = /^[A-HJ-NP-Z2-9]{8}$/;
const MODES = ['all', 'online', 'hidden'];

class FriendsError extends Error {
  constructor(code, message) {
    super(message || code);
    this.code = code;
  }
}

/* --- значения Firestore: здесь, в отличие от отзывов, есть и списки --- */

function toValue(value) {
  if (value === null || value === undefined) return { nullValue: null };
  if (Array.isArray(value)) return { arrayValue: { values: value.map(toValue) } };
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
  if ('arrayValue' in value) return (value.arrayValue?.values ?? []).map(fromValue);
  if ('mapValue' in value) return fromFields(value.mapValue?.fields);
  return null;
}

function toFields(object) {
  return Object.fromEntries(Object.entries(object).map(([key, value]) => [key, toValue(value)]));
}

function fromFields(fields) {
  return Object.fromEntries(Object.entries(fields ?? {}).map(([key, value]) => [key, fromValue(value)]));
}

function cleanName(value) {
  return String(value ?? '')
    .replace(/[\u0000-\u001f\u007f]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, MAX_NAME);
}

/** Документ дружбы: один на пару, имя — оба uid по порядку. */
function pairId(a, b) {
  return a < b ? `${a}_${b}` : `${b}_${a}`;
}

/** «abcd efgh», «ABCD-EFGH» → «ABCDEFGH». Не похоже на код — null. */
function normalizeCode(input) {
  const code = String(input ?? '')
    .toUpperCase()
    .replace(/[\s\-_.]/g, '');
  return CODE_RE.test(code) ? code : null;
}

function formatCode(code) {
  return code ? `${code.slice(0, 4)}-${code.slice(4)}` : null;
}

/** Восемь знаков из 32: 256 делится на 32 нацело, поэтому без перекоса. */
function randomCode(random = crypto.randomBytes) {
  return [...random(8)].map((byte) => CODE_ALPHABET[byte % CODE_ALPHABET.length]).join('');
}

function timeOf(value) {
  const at = Date.parse(value ?? '');
  return Number.isFinite(at) ? at : 0;
}

const STATE_RANK = { playing: 0, online: 1, offline: 2 };

class FriendsClient {
  /**
   * @param {object} options
   * @param {{projectId?: string, apiKey?: string}} options.config
   * @param {import('./account').Account} options.account
   * @param {string} [options.dataDir] — где держать копию списка (friends.json)
   * @param {string} [options.version]
   * @param {typeof fetch} [options.fetch]
   * @param {string} [options.endpoint] — адрес Firestore; другой только у эмулятора
   */
  constructor({ config, account, dataDir = null, version = '', fetch: fetchImpl, endpoint = FIRESTORE } = {}) {
    this.config = {
      projectId: String(config?.projectId ?? '').trim(),
      apiKey: String(config?.apiKey ?? '').trim(),
    };
    this.account = account;
    this.version = String(version).slice(0, 20);
    this.fetch = fetchImpl ?? globalThis.fetch;
    this.endpoint = endpoint;
    this.now = () => Date.now();
    const blank = { uid: null, code: null, view: null };
    this.store = dataDir ? new JsonStore(path.join(dataDir, 'friends.json'), blank) : { data: { ...blank }, save() {} };
    // Что показывать о себе и что показано сейчас.
    this.presence = { state: 'online', game: '', gameName: '', mode: 'all' };
    this.published = null;
    this.skew = 0; // часы сервера минус наши
    this.lastRefresh = 0;
    this.links = null;
    this.linksAt = 0;
    this.refreshing = null;
    this.lastError = null;
    this.pausedUntil = 0;
  }

  get configured() {
    return Boolean(this.config.projectId && this.config.apiKey);
  }

  get documents() {
    return `projects/${this.config.projectId}/databases/(default)/documents`;
  }

  docName(relative) {
    return `${this.documents}/${relative}`;
  }

  url(suffix = '') {
    return `${this.endpoint}/${this.documents}${suffix}?key=${encodeURIComponent(this.config.apiKey)}`;
  }

  /** Время по часам сервера (по заголовку Date последнего ответа). */
  serverNow() {
    return this.now() + this.skew;
  }

  /* --- сеть --- */

  async request(url, { method = 'GET', body, token } = {}) {
    if (this.pausedUntil > this.now()) throw new FriendsError('BUSY', 'paused');
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
    let response;
    try {
      const headers = { Accept: 'application/json' };
      if (token) headers.Authorization = `Bearer ${token}`;
      if (body !== undefined) headers['Content-Type'] = 'application/json';
      response = await this.fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: controller.signal });
    } catch (error) {
      throw new FriendsError('OFFLINE', error?.name === 'AbortError' ? 'timeout' : error?.message);
    } finally {
      clearTimeout(timer);
    }

    const date = Date.parse(response.headers?.get?.('date') ?? '');
    if (Number.isFinite(date)) this.skew = date - this.now();

    let data = null;
    try {
      data = await response.json();
    } catch {
      data = null;
    }
    if (response.ok) return data;

    const error = Array.isArray(data) ? data[0]?.error : data?.error;
    const reason = [error?.status, error?.message].filter(Boolean).join(': ') || String(response.status);
    const status = response.status;
    if (status === 429 || status === 503 || /RESOURCE_EXHAUSTED|quota|billing|UNAVAILABLE/i.test(reason)) {
      this.pausedUntil = this.now() + PAUSE_MS;
      throw new FriendsError('BUSY', reason);
    }
    if (/ALREADY_EXISTS/.test(reason) || status === 409) throw new FriendsError('EXISTS', reason);
    if (/FAILED_PRECONDITION/.test(reason)) throw new FriendsError('PRECONDITION', reason);
    if (status === 404) throw new FriendsError('NOT_FOUND', reason);
    if (status === 403 || /PERMISSION_DENIED/.test(reason)) throw new FriendsError('DENIED', reason);
    if (/API.?key/i.test(reason)) throw new FriendsError('BAD_KEY', reason);
    throw new FriendsError('SERVER', reason);
  }

  async auth() {
    if (!this.configured) throw new FriendsError('NOT_CONFIGURED');
    if (!this.account?.signedIn) throw new FriendsError('SIGN_IN');
    let token;
    try {
      token = await this.account.token();
    } catch (error) {
      const map = { OFFLINE: 'OFFLINE', BUSY: 'BUSY', TOO_MANY: 'BUSY', SESSION: 'SIGN_IN', NOT_CONFIGURED: 'NOT_CONFIGURED', BAD_KEY: 'BAD_KEY' };
      throw new FriendsError(map[error.code] ?? 'SERVER', error.message);
    }
    const name = cleanName(this.account.profile()?.name) || 'ModHub';
    // Другой аккаунт на этом компьютере — чужой список показывать нельзя.
    if (this.store.data.uid !== token.uid) {
      this.store.data = { uid: token.uid, code: null, view: null };
      this.store.save();
      this.published = null;
      this.links = null;
    }
    return { uid: token.uid, idToken: token.idToken, name };
  }

  async commit(writes, me) {
    return this.request(this.url(':commit'), { method: 'POST', token: me.idToken, body: { writes } });
  }

  /** Документ целиком или null, если его нет. */
  async getDoc(relative, me) {
    try {
      const doc = await this.request(this.url(`/${relative}`), { token: me.idToken });
      return fromFields(doc?.fields);
    } catch (error) {
      if (error.code === 'NOT_FOUND') return null;
      throw error;
    }
  }

  /** Несколько документов одним запросом: { относительный путь: поля | null }. */
  async getMany(relatives, me) {
    const out = {};
    for (let i = 0; i < relatives.length; i += 100) {
      const part = relatives.slice(i, i + 100);
      const rows = await this.request(this.url(':batchGet'), {
        method: 'POST',
        token: me.idToken,
        body: { documents: part.map((r) => this.docName(r)) },
      });
      for (const row of Array.isArray(rows) ? rows : []) {
        const name = row.found?.name ?? row.missing;
        if (!name) continue;
        const relative = String(name).slice(this.documents.length + 1);
        out[relative] = row.found ? fromFields(row.found.fields) : null;
      }
    }
    return out;
  }

  /* --- код друга --- */

  /** Свой код: с диска, с сервера (другой компьютер того же аккаунта) или новый. */
  async myCode() {
    const me = await this.auth();
    if (this.store.data.code) return formatCode(this.store.data.code);

    const profile = await this.getDoc(`profiles/${me.uid}`, me);
    if (profile?.code && CODE_RE.test(profile.code)) {
      const owner = await this.getDoc(`codes/${profile.code}`, me).catch(() => null);
      if (owner?.uid === me.uid) return this._keepCode(profile.code);
    }

    for (let attempt = 0; attempt < 5; attempt += 1) {
      const code = randomCode();
      try {
        await this.commit(
          [
            {
              update: { name: this.docName(`codes/${code}`), fields: toFields({ uid: me.uid, name: me.name }) },
              updateTransforms: [{ fieldPath: 'created', setToServerValue: 'REQUEST_TIME' }],
              currentDocument: { exists: false },
            },
          ],
          me
        );
        const shown = this._keepCode(code);
        // Код — ещё и в профиль: так его найдёт этот же аккаунт на другом компьютере.
        this.published = null;
        await this.beat().catch(() => {});
        return shown;
      } catch (error) {
        // Такой код уже чей-то (шанс — один на триллион) — берём другой.
        if (error.code !== 'EXISTS' && error.code !== 'PRECONDITION') throw error;
      }
    }
    throw new FriendsError('SERVER', 'code');
  }

  _keepCode(code) {
    this.store.data.code = code;
    this.store.save();
    return formatCode(code);
  }

  /* --- «в сети», «в игре» --- */

  setMode(mode) {
    this.presence.mode = MODES.includes(mode) ? mode : 'all';
  }

  /** Что сейчас делает человек: state — online | playing | offline. */
  setActivity({ state = 'online', game = '', gameName = '' } = {}) {
    this.presence.state = ['online', 'playing', 'offline'].includes(state) ? state : 'online';
    this.presence.game = state === 'playing' ? String(game).slice(0, 40) : '';
    this.presence.gameName = state === 'playing' ? String(gameName).slice(0, 60) : '';
  }

  /** Что увидят друзья — с учётом «показывать только "в сети"» и невидимки. */
  shownPresence() {
    const { mode, state, game, gameName } = this.presence;
    if (mode === 'hidden' || state === 'offline') return { state: 'offline', game: '', gameName: '' };
    if (mode === 'online' || state !== 'playing') return { state: 'online', game: '', gameName: '' };
    return { state: 'playing', game, gameName };
  }

  /**
   * Отметка в профиле. Раз в пять минут — «я ещё здесь»; при смене
   * состояния — сразу, с новым «с какого времени». Невидимка пишет
   * «не в сети» один раз и дальше молчит.
   */
  async beat({ force = false } = {}) {
    if (!this.configured || !this.account?.signedIn) return false;
    const me = await this.auth();
    const shown = this.shownPresence();
    const key = `${shown.state}|${shown.game}`;
    const changed = this.published?.key !== key || this.published?.uid !== me.uid;
    if (!changed && !force && shown.state === 'offline') return false;
    if (!changed && !force && this.now() - this.published.at < BEAT_MS - 15_000) return false;

    const fields = {
      name: me.name,
      code: this.store.data.code ?? '',
      state: shown.state,
      game: shown.game,
      gameName: shown.gameName,
      app: this.version,
    };
    const transforms = [{ fieldPath: 'seen', setToServerValue: 'REQUEST_TIME' }];
    if (changed) transforms.push({ fieldPath: 'since', setToServerValue: 'REQUEST_TIME' });
    const write = {
      update: { name: this.docName(`profiles/${me.uid}`), fields: toFields(fields) },
      updateMask: { fieldPaths: Object.keys(fields) },
      updateTransforms: transforms,
    };
    // Первая отметка в этом запуске: документа может ещё не быть,
    // а «с какого времени» у нового документа должно быть обязательно.
    await this.commit([write], me);
    this.published = { key, uid: me.uid, at: this.now() };
    return true;
  }

  /** ModHub закрывается или человек выходит из аккаунта: «не в сети» сразу. */
  async goOffline() {
    if (!this.published || this.published.key.startsWith('offline|')) return false;
    const before = this.presence.state;
    this.presence.state = 'offline';
    try {
      return await this.beat({ force: true });
    } finally {
      this.presence.state = before === 'offline' ? 'online' : before;
      this.published = null;
    }
  }

  /* --- друзья и запросы --- */

  view() {
    const data = this.store.data;
    const signedIn = Boolean(this.account?.signedIn);
    const mine = signedIn && data.uid && data.uid === this.account.uid();
    return {
      configured: this.configured,
      signedIn,
      code: mine ? formatCode(data.code) : null,
      mode: this.presence.mode,
      friends: mine ? data.view?.friends ?? [] : [],
      incoming: mine ? data.view?.incoming ?? [] : [],
      outgoing: mine ? data.view?.outgoing ?? [] : [],
      at: mine ? data.view?.at ?? null : null,
      error: this.lastError,
    };
  }

  /** Список дружб и профили друзей. Чаще раза в полминуты — из памяти. */
  async refresh({ force = false } = {}) {
    if (!force && this.store.data.view && this.now() - this.lastRefresh < MIN_REFRESH_MS) return this.view();
    if (this.refreshing) return this.refreshing;
    this.refreshing = (async () => {
      try {
        const me = await this.auth();
        if (force || !this.links || this.now() - this.linksAt > LINKS_MS) {
          const rows = await this.request(this.url(':runQuery'), {
            method: 'POST',
            token: me.idToken,
            body: {
              structuredQuery: {
                from: [{ collectionId: 'friendships' }],
                where: { fieldFilter: { field: { fieldPath: 'users' }, op: 'ARRAY_CONTAINS', value: { stringValue: me.uid } } },
                limit: MAX_LINKS,
              },
            },
          });
          this.links = (Array.isArray(rows) ? rows : [])
            .map((row) => row?.document)
            .filter(Boolean)
            .map((doc) => ({ id: String(doc.name).split('/').pop(), ...fromFields(doc.fields) }))
            .filter((link) => Array.isArray(link.users) && link.users.includes(me.uid));
          this.linksAt = this.now();
        }
        const links = this.links;

        const other = (link) => link.users.find((uid) => uid !== me.uid) ?? '';
        const accepted = links.filter((link) => link.status === 'accepted' && other(link));
        const profiles = accepted.length ? await this.getMany(accepted.map((link) => `profiles/${other(link)}`), me) : {};
        const serverNow = this.serverNow();
        const local = (value) => (value ? new Date(timeOf(value) - this.skew).toISOString() : null);

        const friends = accepted
          .map((link) => {
            const uid = other(link);
            const profile = profiles[`profiles/${uid}`];
            const seen = timeOf(profile?.seen);
            const online = Boolean(profile && profile.state !== 'offline' && seen && serverNow - seen < ONLINE_MS);
            const playing = online && profile.state === 'playing' && Boolean(profile.game);
            return {
              uid,
              name: cleanName(profile?.name) || cleanName(link.from === uid ? link.fromName : link.toName) || '—',
              state: playing ? 'playing' : online ? 'online' : 'offline',
              game: playing ? String(profile.game) : '',
              gameName: playing ? String(profile.gameName || profile.game) : '',
              since: online ? local(profile.since) : null,
              seen: seen ? local(profile.seen) : null,
            };
          })
          .sort((a, b) => STATE_RANK[a.state] - STATE_RANK[b.state] || a.name.localeCompare(b.name));
        const incoming = links
          .filter((link) => link.status === 'pending' && link.to === me.uid)
          .map((link) => ({ uid: link.from, name: cleanName(link.fromName) || '—', at: local(link.created) }));
        const outgoing = links
          .filter((link) => link.status === 'pending' && link.from === me.uid)
          .map((link) => ({ uid: link.to, name: cleanName(link.toName) || '—', at: local(link.created) }));

        this.store.data.view = { friends, incoming, outgoing, at: new Date(this.now()).toISOString() };
        this.store.save();
        this.lastRefresh = this.now();
        this.lastError = null;
        return this.view();
      } catch (error) {
        this.lastError = error.code ?? 'SERVER';
        throw error;
      } finally {
        this.refreshing = null;
      }
    })();
    return this.refreshing;
  }

  /**
   * Добавить по коду. Если этот человек сам уже звал в друзья — запрос
   * просто принимается. Ответ: sent | accepted | already | pending.
   */
  async add(input) {
    const code = normalizeCode(input);
    if (!code) throw new FriendsError('BAD_CODE');
    const me = await this.auth();
    const target = await this.getDoc(`codes/${code}`, me);
    if (!target?.uid) throw new FriendsError('NO_SUCH_CODE');
    if (target.uid === me.uid) throw new FriendsError('SELF');
    const name = cleanName(target.name) || '—';
    const id = pairId(me.uid, target.uid);
    const users = me.uid < target.uid ? [me.uid, target.uid] : [target.uid, me.uid];

    try {
      await this.commit(
        [
          {
            update: {
              name: this.docName(`friendships/${id}`),
              fields: toFields({ users, from: me.uid, to: target.uid, fromName: me.name, toName: name, status: 'pending' }),
            },
            updateTransforms: [
              { fieldPath: 'created', setToServerValue: 'REQUEST_TIME' },
              { fieldPath: 'updated', setToServerValue: 'REQUEST_TIME' },
            ],
            currentDocument: { exists: false },
          },
        ],
        me
      );
      await this.refresh({ force: true }).catch(() => {});
      return { status: 'sent', name };
    } catch (error) {
      // Документ пары уже есть: запрос от нас, от него или вы уже друзья.
      if (!['EXISTS', 'PRECONDITION', 'DENIED'].includes(error.code)) throw error;
      let link = null;
      try {
        link = await this.getDoc(`friendships/${id}`, me);
      } catch (readError) {
        if (readError.code !== 'DENIED') throw readError;
      }
      if (!link) throw error;
      if (link.status === 'accepted') return { status: 'already', name };
      if (link.to === me.uid) {
        await this.accept(target.uid);
        return { status: 'accepted', name };
      }
      return { status: 'pending', name };
    }
  }

  async accept(uid) {
    const me = await this.auth();
    await this.commit(
      [
        {
          update: { name: this.docName(`friendships/${pairId(me.uid, String(uid))}`), fields: toFields({ status: 'accepted', toName: me.name }) },
          updateMask: { fieldPaths: ['status', 'toName'] },
          updateTransforms: [{ fieldPath: 'updated', setToServerValue: 'REQUEST_TIME' }],
          currentDocument: { exists: true },
        },
      ],
      me
    );
    return this.refresh({ force: true }).catch(() => this.view());
  }

  /** Отклонить запрос, отменить свой или убрать из друзей — это одно удаление. */
  async remove(uid) {
    const me = await this.auth();
    await this.commit([{ delete: this.docName(`friendships/${pairId(me.uid, String(uid))}`) }], me);
    return this.refresh({ force: true }).catch(() => this.view());
  }

  /** Перед удалением аккаунта: убрать профиль, код и все дружбы. */
  async forget() {
    const me = await this.auth();
    const view = await this.refresh({ force: true }).catch(() => this.view());
    const writes = [...view.friends, ...view.incoming, ...view.outgoing].map((f) => ({
      delete: this.docName(`friendships/${pairId(me.uid, f.uid)}`),
    }));
    writes.push({ delete: this.docName(`profiles/${me.uid}`) });
    if (this.store.data.code) writes.push({ delete: this.docName(`codes/${this.store.data.code}`) });
    for (let i = 0; i < writes.length; i += 400) await this.commit(writes.slice(i, i + 400), me);
    this.store.data = { uid: null, code: null, view: null };
    this.store.save();
    this.published = null;
    this.links = null;
    return true;
  }
}

module.exports = {
  FriendsClient,
  FriendsError,
  normalizeCode,
  formatCode,
  randomCode,
  pairId,
  toFields,
  fromFields,
  BEAT_MS,
  ONLINE_MS,
  CODE_RE,
};
