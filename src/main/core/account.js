'use strict';

const fs = require('node:fs');
const path = require('node:path');

/**
 * Аккаунт ModHub: почта и пароль.
 *
 * Вход — через Firebase Authentication того же проекта, что и отзывы:
 * своего сервера нет, пароли хранит и проверяет Google. ModHub пароль
 * никуда не сохраняет — он уходит прямо в защищённый вход Firebase, а на
 * диске остаётся только «пропуск» (refresh-токен), и тот зашифрован
 * средствами Windows (safeStorage → DPAPI), если они доступны.
 *
 * Два «места» для пропуска:
 *
 *   • anon — безымянная учётная запись этого компьютера. Её получает любой
 *     ModHub при первом отзыве, регистрироваться для этого не нужно.
 *
 *   • user — аккаунт с почтой. Пока он есть, отзывы пишутся от него:
 *     под его именем и с правом править их с любого компьютера.
 *
 * Регистрация на компьютере, где уже есть безымянная запись, не заводит
 * вторую: Firebase «привязывает» почту и пароль к ней же. Номер (uid) не
 * меняется — отзывы, оставленные до регистрации, становятся отзывами
 * аккаунта. Выход из аккаунта возвращает к безымянной записи компьютера.
 */

const IDENTITY = 'https://identitytoolkit.googleapis.com/v1';
const FIRESTORE = 'https://firestore.googleapis.com/v1';
const SECURE_TOKEN = 'https://securetoken.googleapis.com/v1';
const TIMEOUT_MS = 15000;
const MAX_NAME = 32;
const MIN_PASSWORD = 8;
const MAX_PASSWORD = 128;
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

/** Ошибка, у которой есть код для перевода на человеческий. */
class AccountError extends Error {
  constructor(code, message) {
    super(message || code);
    this.code = code;
  }
}

/**
 * Ответы Firebase → наши коды. С защитой от перебора почт (она включена
 * у новых проектов) Google не говорит, чего именно нет — почты или
 * пароля, поэтому и мы говорим одно: «неверная почта или пароль».
 */
const CODES = [
  [/EMAIL_EXISTS/, 'EMAIL_EXISTS'],
  [/INVALID_EMAIL|MISSING_EMAIL/, 'BAD_EMAIL'],
  [/WEAK_PASSWORD|MISSING_PASSWORD|PASSWORD_DOES_NOT_MEET/, 'WEAK_PASSWORD'],
  [/INVALID_LOGIN_CREDENTIALS|EMAIL_NOT_FOUND|INVALID_PASSWORD/, 'WRONG_LOGIN'],
  [/USER_DISABLED/, 'DISABLED'],
  [/TOO_MANY_ATTEMPTS/, 'TOO_MANY'],
  [/OPERATION_NOT_ALLOWED|ADMIN_ONLY_OPERATION|CONFIGURATION_NOT_FOUND/, 'NOT_ENABLED'],
  [/CREDENTIAL_TOO_OLD/, 'RELOGIN'],
  [/TOKEN_EXPIRED|USER_NOT_FOUND|INVALID_REFRESH_TOKEN|INVALID_ID_TOKEN|INVALID_GRANT_TYPE/, 'SESSION'],
  [/RESOURCE_EXHAUSTED|quota|UNAVAILABLE|billing/i, 'BUSY'],
  [/API.?key/i, 'BAD_KEY'],
];

function cleanName(value) {
  return String(value ?? '')
    .replace(/[\u0000-\u001f\u007f]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, MAX_NAME);
}

function cleanEmail(value) {
  return String(value ?? '').trim().toLowerCase();
}

class Account {
  /**
   * @param {object} options
   * @param {{projectId?: string, apiKey?: string}} options.config
   * @param {string} options.file — где хранить пропуск (account.json)
   * @param {typeof fetch} [options.fetch]
   * @param {{encrypt: (s: string) => string, decrypt: (s: string) => string} | null} [options.protect]
   * @param {{uid?: string, refreshToken?: string} | null} [options.legacyAuth] — безымянная запись из отзывов 1.9.2
   * @param {() => string} [options.lang] — язык писем: ru / en
   */
  constructor({ config, file, fetch: fetchImpl, protect = null, legacyAuth = null, lang = () => 'ru' } = {}) {
    this.config = {
      projectId: String(config?.projectId ?? '').trim(),
      apiKey: String(config?.apiKey ?? '').trim(),
    };
    this.file = file;
    this.fetch = fetchImpl ?? globalThis.fetch;
    this.protect = protect;
    this.lang = lang;
    this.now = () => Date.now();
    this.live = { anon: null, user: null }; // idToken и срок — только в памяти
    this.pending = null;
    this.data = this._read();

    // Отзывы из 1.9.2 писались от безымянной записи, что хранилась
    // в reviews.json. Забираем её сюда, чтобы они остались «вашими».
    if (!this.data.anon && legacyAuth?.uid && legacyAuth?.refreshToken) {
      this.data.anon = { uid: legacyAuth.uid, refreshToken: this._seal(legacyAuth.refreshToken) };
      this._write();
    }
  }

  get configured() {
    return Boolean(this.config.projectId && this.config.apiKey);
  }

  /* --- диск ------------------------------------------------------------ */

  _read() {
    try {
      const data = JSON.parse(fs.readFileSync(this.file, 'utf8'));
      return { anon: data?.anon ?? null, user: data?.user ?? null };
    } catch {
      return { anon: null, user: null };
    }
  }

  _write() {
    try {
      fs.mkdirSync(path.dirname(this.file), { recursive: true });
      const tmp = this.file + '.tmp';
      fs.writeFileSync(tmp, JSON.stringify(this.data), 'utf8');
      fs.renameSync(tmp, this.file);
    } catch {
      /* не записалось — войти придётся ещё раз, и только */
    }
  }

  /** Пропуск на диск — зашифрованным, если Windows это умеет. */
  _seal(token) {
    if (!token) return null;
    try {
      if (this.protect) return { enc: this.protect.encrypt(token) };
    } catch {
      /* шифрование недоступно — храним как есть, как хранят браузеры без него */
    }
    return { raw: token };
  }

  _open(sealed) {
    if (!sealed) return null;
    if (typeof sealed === 'string') return sealed;
    try {
      if (sealed.enc && this.protect) return this.protect.decrypt(sealed.enc);
    } catch {
      return null;
    }
    return sealed.raw ?? null;
  }

  /* --- сеть ------------------------------------------------------------ */

  async request(url, body, { form = false } = {}) {
    if (!this.configured) throw new AccountError('NOT_CONFIGURED');
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
    let response;
    try {
      response = await this.fetch(url, {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': form ? 'application/x-www-form-urlencoded' : 'application/json',
          // Язык писем «подтвердите почту» и «сброс пароля».
          'X-Firebase-Locale': this.lang() === 'en' ? 'en' : 'ru',
        },
        body: form ? new URLSearchParams(body).toString() : JSON.stringify(body),
        signal: controller.signal,
      });
    } catch (error) {
      throw new AccountError('OFFLINE', error?.name === 'AbortError' ? 'timeout' : error?.message);
    } finally {
      clearTimeout(timer);
    }
    let data = null;
    try {
      data = await response.json();
    } catch {
      data = null;
    }
    if (response.ok) return data ?? {};
    const reason = [data?.error?.message, data?.error?.status].filter(Boolean).join(': ') || String(response.status);
    const found = CODES.find(([pattern]) => pattern.test(reason));
    throw new AccountError(found ? found[1] : 'SERVER', reason);
  }

  identity(pathname) {
    return `${IDENTITY}/${pathname}?key=${encodeURIComponent(this.config.apiKey)}`;
  }

  /* --- кто сейчас ------------------------------------------------------ */

  /** Что видно в окне. Пропусков тут нет и быть не должно. */
  profile() {
    const user = this.data.user;
    return {
      configured: this.configured,
      signedIn: Boolean(user),
      uid: user?.uid ?? this.data.anon?.uid ?? null,
      email: user?.email ?? null,
      name: user?.name ?? null,
      verified: Boolean(user?.verified),
      since: user?.since ?? null,
      // Администратор: почта в списке admins и подтверждена (так же решают
      // правила базы). Почта в списке, но не подтверждена — «ждёт».
      admin: Boolean(user?.admin && user?.verified),
      adminPending: Boolean(user?.admin && !user?.verified),
    };
  }

  /** Номер того, от чьего имени сейчас пишутся отзывы (без запросов в сеть). */
  uid() {
    return this.data.user?.uid ?? this.data.anon?.uid ?? null;
  }

  get signedIn() {
    return Boolean(this.data.user);
  }

  /**
   * Действующий токен для записи в базу. Нет ни аккаунта, ни безымянной
   * записи — заводим безымянную (так было и в 1.9.2).
   * @returns {Promise<{uid: string, idToken: string}>}
   */
  async token() {
    const slot = this.data.user ? 'user' : 'anon';
    const live = this.live[slot];
    if (live?.idToken && live.expiresAt - 60_000 > this.now() && live.uid === this.data[slot]?.uid) {
      return { uid: live.uid, idToken: live.idToken };
    }
    if (this.pending) return this.pending;
    this.pending = (async () => {
      try {
        const stored = this.data[slot];
        const refresh = this._open(stored?.refreshToken);
        if (stored && refresh) {
          try {
            return await this._refresh(slot, refresh);
          } catch (error) {
            if (error.code !== 'SESSION') throw error;
            // Пропуск больше не действует (аккаунт удалили или сменили пароль).
            this.data[slot] = null;
            this.live[slot] = null;
            this._write();
            if (slot === 'user') throw error;
          }
        }
        if (slot === 'user') throw new AccountError('SESSION');
        const data = await this.request(this.identity('accounts:signUp'), { returnSecureToken: true });
        return this._remember('anon', data.localId, data.idToken, data.refreshToken, data.expiresIn);
      } finally {
        this.pending = null;
      }
    })();
    return this.pending;
  }

  async _refresh(slot, refreshToken) {
    const data = await this.request(
      `${SECURE_TOKEN}/token?key=${encodeURIComponent(this.config.apiKey)}`,
      { grant_type: 'refresh_token', refresh_token: refreshToken },
      { form: true }
    );
    return this._remember(slot, data.user_id, data.id_token, data.refresh_token ?? refreshToken, data.expires_in);
  }

  _remember(slot, uid, idToken, refreshToken, expiresIn, extra = {}) {
    const expiresAt = this.now() + Number(expiresIn ?? 3600) * 1000;
    this.live[slot] = { uid, idToken, expiresAt };
    const before = this.data[slot] ?? {};
    this.data[slot] = { ...before, ...extra, uid, refreshToken: this._seal(refreshToken) };
    this._write();
    return { uid, idToken };
  }

  /* --- регистрация, вход, выход --------------------------------------- */

  _check({ name, email, password }, { needName }) {
    const cleanMail = cleanEmail(email);
    if (!EMAIL_RE.test(cleanMail) || cleanMail.length > 254) throw new AccountError('BAD_EMAIL');
    if (typeof password !== 'string' || password.length < MIN_PASSWORD || password.length > MAX_PASSWORD) {
      throw new AccountError('WEAK_PASSWORD');
    }
    const cleanNick = cleanName(name);
    if (needName && !cleanNick) throw new AccountError('NO_NAME');
    return { name: cleanNick, email: cleanMail, password };
  }

  /**
   * Создать аккаунт. Если на компьютере уже есть безымянная запись —
   * почта и пароль привязываются к ней, и отзывы остаются за человеком.
   */
  async signUp(input) {
    if (this.data.user) throw new AccountError('ALREADY');
    const { name, email, password } = this._check(input ?? {}, { needName: true });

    let created = null;
    const anonRefresh = this._open(this.data.anon?.refreshToken);
    if (anonRefresh) {
      try {
        const anon = await this.token();
        created = await this.request(this.identity('accounts:update'), {
          idToken: anon.idToken,
          email,
          password,
          returnSecureToken: true,
        });
        created.localId = created.localId ?? anon.uid;
      } catch (error) {
        // Безымянная запись испорчена — просто заведём новый аккаунт.
        if (error.code !== 'SESSION') throw error;
      }
    }
    if (!created) {
      created = await this.request(this.identity('accounts:signUp'), { email, password, returnSecureToken: true });
    }

    const since = new Date(this.now()).toISOString();
    this._remember('user', created.localId, created.idToken, created.refreshToken, created.expiresIn, {
      email,
      name,
      verified: false,
      since,
    });
    // Безымянная запись стала аккаунтом — отдельной больше нет.
    if (this.data.anon?.uid === created.localId) {
      this.data.anon = null;
      this.live.anon = null;
      this._write();
    }

    await this._setName(name).catch(() => {});
    await this.sendVerification().catch(() => {});
    return this.profile();
  }

  /** Войти в аккаунт, созданный раньше (на этом или другом компьютере). */
  async signIn(input) {
    const email = cleanEmail(input?.email);
    const password = String(input?.password ?? '');
    if (!EMAIL_RE.test(email)) throw new AccountError('BAD_EMAIL');
    if (!password) throw new AccountError('WRONG_LOGIN');
    const data = await this.request(this.identity('accounts:signInWithPassword'), { email, password, returnSecureToken: true });
    this._remember('user', data.localId, data.idToken, data.refreshToken, data.expiresIn, {
      email: data.email ?? email,
      name: cleanName(data.displayName) || cleanName(email.split('@')[0]),
      verified: false,
      since: this.data.user?.since ?? null,
    });
    await this.refresh().catch(() => {});
    return this.profile();
  }

  /** Выйти: отзывы снова пишутся от безымянной записи компьютера. */
  signOut() {
    this.data.user = null;
    this.live.user = null;
    this._write();
    return this.profile();
  }

  /** Подтянуть с сервера имя, почту, «почта подтверждена» и права администратора. */
  async refresh() {
    if (!this.data.user) return this.profile();
    const { idToken } = await this.token();
    const data = await this.request(this.identity('accounts:lookup'), { idToken });
    const info = data?.users?.[0];
    if (info) {
      const created = Number(info.createdAt);
      // Почту только что подтвердили — старый токен об этом не знает, а правила
      // базы смотрят именно в токен. Берём свежий.
      if (info.emailVerified && !this.data.user.verified) this.live.user = null;
      this.data.user = {
        ...this.data.user,
        email: info.email ?? this.data.user.email,
        name: cleanName(info.displayName) || this.data.user.name,
        verified: Boolean(info.emailVerified),
        since: Number.isFinite(created) && created > 0 ? new Date(created).toISOString() : this.data.user.since,
      };
      this._write();
    }
    await this.checkAdmin().catch(() => {});
    return this.profile();
  }

  /**
   * Есть ли почта аккаунта в списке администраторов (коллекция admins).
   * Правила базы разрешают спросить только про себя. Нет сети — остаётся
   * прежний ответ.
   */
  async checkAdmin() {
    const user = this.data.user;
    if (!user?.email) return false;
    const { idToken } = await this.token();
    const url = `${FIRESTORE}/projects/${encodeURIComponent(this.config.projectId)}/databases/(default)/documents/admins/${encodeURIComponent(user.email)}?key=${encodeURIComponent(this.config.apiKey)}`;
    let response;
    try {
      response = await this.fetch(url, { method: 'GET', headers: { Accept: 'application/json', Authorization: `Bearer ${idToken}` } });
    } catch (error) {
      throw new AccountError('OFFLINE', error?.message);
    }
    if (response.status === 200) this.data.user.admin = true;
    else if (response.status === 404 || response.status === 403) this.data.user.admin = false;
    else return Boolean(this.data.user.admin);
    this._write();
    return Boolean(this.data.user.admin);
  }

  async _setName(name) {
    const { idToken } = await this.token();
    await this.request(this.identity('accounts:update'), { idToken, displayName: name });
    this.data.user = { ...this.data.user, name };
    this._write();
  }

  /** Сменить имя, под которым видны отзывы. */
  async rename(value) {
    if (!this.data.user) throw new AccountError('SESSION');
    const name = cleanName(value);
    if (!name) throw new AccountError('NO_NAME');
    await this._setName(name);
    return this.profile();
  }

  /** Письмо «подтвердите почту» (ещё раз). */
  async sendVerification() {
    if (!this.data.user) throw new AccountError('SESSION');
    const { idToken } = await this.token();
    await this.request(this.identity('accounts:sendOobCode'), { requestType: 'VERIFY_EMAIL', idToken });
    return true;
  }

  /**
   * Письмо со ссылкой на новый пароль. Есть ли такая почта, Google
   * не говорит (защита от перебора) — и мы отвечаем одинаково.
   */
  async resetPassword(value) {
    const email = cleanEmail(value ?? this.data.user?.email);
    if (!EMAIL_RE.test(email)) throw new AccountError('BAD_EMAIL');
    await this.request(this.identity('accounts:sendOobCode'), { requestType: 'PASSWORD_RESET', email });
    return true;
  }

  /**
   * Удалить аккаунт насовсем. Пароль спрашиваем ещё раз: так требует
   * Google для опасных действий, и так случайный клик ничего не сотрёт.
   */
  async deleteAccount(password) {
    const user = this.data.user;
    if (!user) throw new AccountError('SESSION');
    const fresh = await this.request(this.identity('accounts:signInWithPassword'), {
      email: user.email,
      password: String(password ?? ''),
      returnSecureToken: true,
    });
    if (fresh.localId !== user.uid) throw new AccountError('WRONG_LOGIN');
    await this.request(this.identity('accounts:delete'), { idToken: fresh.idToken });
    return this.signOut();
  }
}

module.exports = { Account, AccountError, cleanName, MIN_PASSWORD, MAX_NAME };
