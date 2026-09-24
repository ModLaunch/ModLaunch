'use strict';

/**
 * Оверлей в игре (2.1).
 *
 * Три карточки поверх игры: сеанс (сколько играете, какие моды включены,
 * копия сохранений одной кнопкой), друзья в сети и заметки к игре —
 * координаты базы, что собрать, планы. Заметки сохраняются сами.
 *
 * Окно живёт в песочнице и видит только window.overlay (overlay-preload.js).
 */

// I18N — общий с i18n.js: он объявлен там на верхнем уровне скрипта.
const api = window.overlay;
const t = (key, params) => I18N.t(key, params);

const $ = (id) => document.getElementById(id);

let data = null;
let ticker = null;
let noteTimer = null;
let noteDirty = false;

function esc(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function initials(name) {
  return String(name ?? '?')
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0])
    .join('')
    .toUpperCase();
}

function hue(text) {
  let h = 0;
  for (const ch of String(text)) h = (h * 31 + ch.codePointAt(0)) >>> 0;
  return h % 360;
}

function plural(n, key) {
  const form = I18N.plural(n, 'one', 'few', 'many');
  return t(`${key}.${form}`, { n });
}

/** 3 ч 25 мин / 12 мин / меньше минуты — как в самом ModHub. */
function duration(ms) {
  const minutes = Math.floor((Number(ms) || 0) / 60000);
  if (minutes < 1) return t('time.lessMinute');
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (!h) return t('time.m', { m });
  return m ? t('time.hm', { h, m }) : t('time.h', { h });
}

/** Часы сеанса: 47:12 или 1:07:12. */
function clockOf(ms) {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n) => String(n).padStart(2, '0');
  return h ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

/** Сегодня — только время, иначе — число и время. */
function whenText(value) {
  const at = new Date(value);
  if (Number.isNaN(at.getTime())) return '';
  const lang = I18N.lang === 'en' ? 'en-GB' : 'ru-RU';
  const today = at.toDateString() === new Date().toDateString();
  return today
    ? at.toLocaleTimeString(lang, { hour: '2-digit', minute: '2-digit' })
    : at.toLocaleString(lang, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
}

function keyLabel(key) {
  return String(key ?? '').replace('CommandOrControl', 'Ctrl');
}

async function unwrap(promise) {
  const result = await promise;
  if (result?.ok) return result.data;
  throw new Error(result?.error || 'error');
}

/* --- карточки --- */

function renderSession() {
  const game = data.game;
  const node = $('ovSession');
  if (!game) {
    node.innerHTML = `<div class="ovs__body"><h2>${esc(t('ovl.noGame'))}</h2></div>`;
    return;
  }
  const art = (data.art ?? []).map((src) => `url('${esc(src)}')`).join(', ');
  const mods = data.mods ?? [];
  const last = data.backups?.last ? new Date(data.backups.last) : null;
  node.style.setProperty('--accent', game.accent || '#8b6cff');
  node.innerHTML = `
    <div class="ovs__art" style="background-image:${art}"></div>
    <div class="ovs__body">
      <span class="ov__label">${esc(data.startedAt ? t('ovl.playing') : t('ovl.preview'))}</span>
      <h2>${esc(game.name)}</h2>
      ${data.startedAt ? `<div class="ovs__time" id="ovSessionTime">0:00</div>` : ''}
      ${data.totalMs >= 60000 ? `<p class="ov__muted">${esc(t('ovl.total', { time: duration(data.totalMs) }))}</p>` : ''}
      <div class="ovs__mods">
        <b>${esc(mods.length ? plural(mods.length, 'ovl.mods') : t('ovl.noMods'))}</b>
        ${mods.length ? `<span>${esc(mods.slice(0, 12).join(' · '))}${mods.length > 12 ? ' …' : ''}</span>` : ''}
      </div>
      <div class="ovs__actions">
        ${
          data.backups?.supported
            ? `<button class="ov__btn ov__btn--main" id="ovBackup" type="button">${esc(t('ovl.backup'))}</button>`
            : ''
        }
        <button class="ov__btn" id="ovOpen" type="button">${esc(t('ovl.open'))}</button>
      </div>
      <p class="ov__muted ovs__note" id="ovBackupNote">${
        last ? esc(t('ovl.backup.last', { time: whenText(last) })) : ''
      }</p>
    </div>`;
  $('ovBackup')?.addEventListener('click', makeBackup);
  $('ovOpen')?.addEventListener('click', () => api.openApp());
}

function friendStatus(friend) {
  if (friend.state === 'playing') {
    const long = friend.since ? duration(Date.now() - Date.parse(friend.since)) : '';
    return t('friends.playing', { game: friend.gameName || friend.game }) + (long ? ` · ${long}` : '');
  }
  return friend.state === 'online' ? t('friends.online') : t('friends.offline');
}

function renderFriends() {
  const node = $('ovFriends');
  const view = data.friends;
  if (!view?.configured) {
    node.innerHTML = `<h3>${esc(t('friends.title'))}</h3><p class="ov__muted">${esc(t('err.friendsOff'))}</p>`;
    return;
  }
  if (!view.signedIn) {
    node.innerHTML = `<h3>${esc(t('friends.title'))}</h3><p class="ov__muted">${esc(t('ovl.friends.signin'))}</p>`;
    return;
  }
  const friends = view.friends ?? [];
  const online = friends.filter((f) => f.state !== 'offline');
  const rows = (online.length ? online : friends).slice(0, 8);
  node.innerHTML = `
    <h3>${esc(online.length ? t('friends.aside.online', { n: online.length }) : t('friends.title'))}</h3>
    ${
      rows.length
        ? `<div class="ovf">${rows
            .map(
              (f) => `
              <div class="ovf__row is-${esc(f.state)}">
                <span class="ovf__face" style="--h:${hue(f.name)}">${esc(initials(f.name))}<i></i></span>
                <span class="ovf__text"><b>${esc(f.name)}</b><span>${esc(friendStatus(f))}</span></span>
              </div>`
            )
            .join('')}</div>`
        : `<p class="ov__muted">${esc(t('ovl.friends.none'))}</p>`
    }
    ${view.incoming?.length ? `<p class="ov__muted">${esc(plural(view.incoming.length, 'friends.requestsN'))}</p>` : ''}`;
}

function renderNotes() {
  const node = $('ovNotes');
  node.innerHTML = `
    <h3>${esc(t('ovl.notes'))}${data.game ? ` · <span class="ov__muted">${esc(data.game.name)}</span>` : ''}</h3>
    <textarea id="ovNote" maxlength="4000" spellcheck="false" placeholder="${esc(t('ovl.notes.placeholder'))}">${esc(data.note ?? '')}</textarea>
    <p class="ov__muted ovn__state" id="ovNoteState"></p>`;
  const area = $('ovNote');
  area.addEventListener('input', () => {
    noteDirty = true;
    $('ovNoteState').textContent = '';
    clearTimeout(noteTimer);
    noteTimer = setTimeout(saveNote, 700);
  });
  if (!data.game) area.disabled = true;
}

async function saveNote() {
  clearTimeout(noteTimer);
  if (!noteDirty) return;
  const area = $('ovNote');
  if (!area) return;
  noteDirty = false;
  data.note = area.value;
  await unwrap(api.note(area.value)).catch(() => {});
  const state = $('ovNoteState');
  if (state) state.textContent = t('ovl.notes.saved');
}

async function makeBackup() {
  const button = $('ovBackup');
  const note = $('ovBackupNote');
  if (button) button.disabled = true;
  try {
    const made = await unwrap(api.backup());
    if (note) {
      note.textContent = t('ovl.backup.done', { time: whenText(made?.at ?? Date.now()) });
    }
  } catch (error) {
    if (note) note.textContent = error.message;
  } finally {
    if (button) button.disabled = false;
  }
}

/* --- часы --- */

function tick() {
  const now = new Date();
  $('ovClock').textContent = now.toLocaleTimeString(I18N.lang === 'en' ? 'en-GB' : 'ru-RU', { hour: '2-digit', minute: '2-digit' });
  if (data?.startedAt) {
    const clock = clockOf(now - Date.parse(data.startedAt));
    $('ovTimer').textContent = clock;
    const big = $('ovSessionTime');
    if (big) big.textContent = clock;
  }
}

/* --- открыть и закрыть --- */

async function open() {
  data = await unwrap(api.state()).catch(() => null);
  if (!data) return;
  I18N.set(data.lang);
  $('ovGame').textContent = data.game?.name ?? '';
  $('ovTimer').textContent = '';
  $('ovKey').textContent = t('ovl.hint', { key: keyLabel(data.key) });
  $('ovClose').title = t('ovl.close');
  renderSession();
  renderFriends();
  renderNotes();
  $('ov').hidden = false;
  tick();
  clearInterval(ticker);
  ticker = setInterval(tick, 1000);
  // Друзья — свежие, но не чаще, чем разрешает основной процесс.
  unwrap(api.friends())
    .then((friends) => {
      if (!data) return;
      data.friends = friends;
      renderFriends();
    })
    .catch(() => {});
}

async function close() {
  clearInterval(ticker);
  await saveNote();
  api.hide();
}

api.onShow(open);

document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape') close();
});

// Щелчок мимо карточек — закрыть, как у оверлея Steam.
document.addEventListener('mousedown', (event) => {
  if (event.target === $('ov') || event.target === $('ovGrid')) close();
});

$('ovClose').addEventListener('click', close);

// Окно прячут (Alt+Tab, щелчок в игру) — дописанная заметка не должна пропасть.
window.addEventListener('blur', () => {
  saveNote();
});
