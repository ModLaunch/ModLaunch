'use strict';

/* Страница ModLaunch Hub: поиск, сортировка, фильтры, окно мода, скачивание
   с проверкой SHA-256 и ссылки вида hub.html#mod=<id> / #author=<uid>. */

Object.assign(EN, {
  'h.chip': 'New in ModLaunch 5.5',
  'h.title1': 'ModLaunch',
  'h.sub': 'Community mods — no ads, no paid “fast download”, no middlemen. Browse and download here; in ModLaunch they install with one button.',
  'h.search': 'Name, author or tag',
  'h.kind.all': 'All', 'h.kind.package': 'Packages', 'h.kind.script': 'Scripts',
  'h.allGames': 'All games',
  'h.sort.trending': 'Trending', 'h.sort.new': 'New', 'h.sort.updated': 'Updated', 'h.sort.downloads': 'Popular', 'h.sort.likes': 'Loved',
  'h.stat.mods': 'mods', 'h.stat.authors': 'authors', 'h.stat.downloads': 'downloads', 'h.stat.likes': 'likes',
  'h.nothing': 'Nothing found', 'h.nothing.p': 'Clear the filters or search differently.',
  'h.empty': 'Hub has 0 mods yet — be the first author',
  'h.empty.p': 'Publish a mod archive or a ModScript mod from ModLaunch — it shows up here and in everyone’s app right away.',
  'h.empty.btn': 'How to publish',
  'h.soon': 'ModLaunch Hub is opening',
  'h.soon.p': 'The gallery is being switched on right now — mods published in the app will appear here very soon.',
  'h.error': 'Couldn’t load Hub. Check your connection and refresh the page.',
  'h.byAuthor': 'Mods by {author}', 'h.clear': 'Show all',
  'm.install': 'Install in ModLaunch', 'm.download': 'Download archive', 'm.downloadCode': 'Download .mls',
  'm.share': 'Copy link', 'm.copied': 'Link copied', 'm.vt': 'VirusTotal', 'm.author': 'All mods by the author',
  'm.about': 'Description', 'm.versions': 'Versions', 'm.comments': 'Comments', 'm.noComments': 'No comments yet — comment in the app.',
  'm.code': 'ModScript source', 'm.codeHint': 'A script mod: ModLaunch builds it on your PC. It contains no program code — only data and settings.',
  'm.installHint': 'Open ModLaunch → Creator Hub → Hub and find “{name}”, or open the game catalog and pick the ModLaunch Hub source.',
  'm.hash': 'SHA-256', 'm.updated': 'Updated {when}', 'm.size': '{size} MB', 'm.bad': 'The file failed the SHA-256 check — don’t install it. Try again.',
  'm.checking': 'Downloading and checking…', 'm.ok': 'Checked by SHA-256 ✓',
  'p.eyebrow': 'For authors', 'p.title': 'How to publish a mod',
  'p.1t': 'Download ModLaunch', 'p.1p': 'And sign in to a ModLaunch account in Settings → Accounts — only authors with an email can publish.',
  'p.2t': 'Creator Hub → “Publish a mod”', 'p.2p': 'Pick a mod archive (up to 8 MB) or build a mod in the ModScript workshop. Add a description, tags, images and a changelog.',
  'p.3t': 'Done', 'p.3p': 'The mod appears here and in everyone’s app right away. New version — one button in “My uploads”; download and like stats are there too.',
  'p.rules': '<b>Hub rules.</b> Publish only your own mods or mods you’re allowed to share. Every file is checked by SHA-256 on download; bad mods can be reported right in the app.',
});
Object.assign(ru, {
  'h.allGames': 'Все игры',
  'h.sort.trending': 'В тренде', 'h.sort.new': 'Новые', 'h.sort.updated': 'Обновлённые', 'h.sort.downloads': 'Популярные', 'h.sort.likes': 'Любимые',
  'h.stat.mods': 'модов', 'h.stat.authors': 'авторов', 'h.stat.downloads': 'загрузок', 'h.stat.likes': 'лайков',
  'h.nothing': 'Ничего не нашлось', 'h.nothing.p': 'Снимите фильтры или поищите по-другому.',
  'h.empty': 'В Hub пока 0 модов — станьте первым автором',
  'h.empty.p': 'Опубликуйте архив мода или мод на ModScript из ModLaunch — он сразу появится здесь и у всех в программе.',
  'h.empty.btn': 'Как опубликовать',
  'h.soon': 'ModLaunch Hub открывается',
  'h.soon.p': 'Галерею прямо сейчас включают — моды, опубликованные в программе, появятся здесь совсем скоро.',
  'h.error': 'Не удалось загрузить Hub. Проверьте интернет и обновите страницу.',
  'h.byAuthor': 'Моды автора {author}', 'h.clear': 'Показать все',
  'm.install': 'Установить в ModLaunch', 'm.download': 'Скачать архив', 'm.downloadCode': 'Скачать .mls',
  'm.share': 'Скопировать ссылку', 'm.copied': 'Ссылка скопирована', 'm.vt': 'VirusTotal', 'm.author': 'Все моды автора',
  'm.about': 'Описание', 'm.versions': 'Версии', 'm.comments': 'Комментарии', 'm.noComments': 'Комментариев пока нет — написать можно в программе.',
  'm.code': 'Исходник ModScript', 'm.codeHint': 'Мод-скрипт: ModLaunch соберёт его у вас на компьютере. Программного кода в нём нет — только данные и настройки.',
  'm.installHint': 'Откройте ModLaunch → Creator Hub → Hub и найдите «{name}» — или откройте каталог игры и выберите источник ModLaunch Hub.',
  'm.hash': 'SHA-256', 'm.updated': 'Обновлён {when}', 'm.size': '{size} МБ', 'm.bad': 'Файл не прошёл проверку SHA-256 — не ставьте его. Попробуйте ещё раз.',
  'm.checking': 'Скачиваем и проверяем…', 'm.ok': 'Проверено по SHA-256 ✓',
});

const TAGS = ['qol', 'gameplay', 'content', 'items', 'visuals', 'audio', 'ui', 'cosmetics', 'multiplayer', 'tweaks', 'library', 'modpack', 'cheats', 'fixes'];
const TAG_RU = { qol: 'удобство', gameplay: 'геймплей', content: 'контент', items: 'предметы', visuals: 'графика', audio: 'звук', ui: 'интерфейс', cosmetics: 'косметика', multiplayer: 'мультиплеер', tweaks: 'настройки', library: 'библиотека', modpack: 'сборка', cheats: 'читы', fixes: 'исправления' };
const tagName = (tag) => (lang === 'en' ? tag : TAG_RU[tag] || tag);

const state = { all: null, error: null, q: '', game: '', kind: 'all', tag: '', sort: 'trending', author: '' };

function renderControls() {
  const game = document.getElementById('game');
  game.innerHTML = `<option value="">${esc(t('h.allGames'))}</option>` + GAMES.map((g) => `<option value="${g.id}">${esc(g.name)}</option>`).join('');
  game.value = state.game;
  document.getElementById('sorts').innerHTML = ['trending', 'new', 'updated', 'downloads', 'likes']
    .map((s) => `<button class="chip ${state.sort === s ? 'is-on' : ''}" data-sort="${s}">${esc(t('h.sort.' + s))}</button>`).join('');
  document.getElementById('tags').innerHTML = TAGS.map((tag) => `<button class="chip ${state.tag === tag ? 'is-on' : ''}" data-tag="${tag}">#${esc(tagName(tag))}</button>`).join('');
}

function renderStats() {
  const all = state.all || [];
  const box = document.getElementById('stats');
  box.innerHTML = [
    [all.length, 'h.stat.mods'],
    [new Set(all.map((m) => m.uid)).size, 'h.stat.authors'],
    [all.reduce((s, m) => s + m.downloads, 0), 'h.stat.downloads'],
    [all.reduce((s, m) => s + m.likes, 0), 'h.stat.likes'],
  ].map(([n, k]) => `<span><b>${fmt(n)}</b>${esc(t(k))}</span>`).join('');
}

function renderList() {
  const box = document.getElementById('list');
  const authorBox = document.getElementById('author');
  if (!state.all) {
    box.innerHTML = `<div class="hub-grid">${'<div class="skeleton"></div>'.repeat(6)}</div>`;
    return;
  }
  if (state.error) {
    const soon = state.error === 'DENIED';
    box.innerHTML = `<div class="empty"><div class="empty__art">✦</div><div><h3>${esc(t(soon ? 'h.soon' : 'h.error'))}</h3>${soon ? `<p>${esc(t('h.soon.p'))}</p>` : ''}
      <a class="btn btn--primary" href="#publish">${esc(t('h.empty.btn'))}</a></div></div>`;
    return;
  }
  if (!state.all.length) {
    box.innerHTML = `<div class="empty"><div class="empty__art">✦</div><div><h3>${esc(t('h.empty'))}</h3><p>${esc(t('h.empty.p'))}</p>
      <a class="btn btn--primary" href="#publish">${esc(t('h.empty.btn'))}</a>
      <a class="btn btn--ghost js-setup" href="https://github.com/ModLaunch/ModLaunch/releases/latest">${esc(t('hero.download'))}</a></div></div>`;
    renderRelease();
    return;
  }
  const q = state.q.trim().toLowerCase();
  const list = Hub.sort(state.all.filter((m) =>
    (!state.game || m.game === state.game) &&
    (state.kind === 'all' || m.kind === state.kind) &&
    (!state.tag || m.tags.includes(state.tag)) &&
    (!state.author || m.uid === state.author) &&
    (!q || [m.name, m.summary, m.author, ...m.tags, gameById(m.game)?.name || ''].some((s) => s.toLowerCase().includes(q)))), state.sort);
  const author = state.author && state.all.find((m) => m.uid === state.author);
  authorBox.innerHTML = author
    ? `<div class="note" style="display:flex;justify-content:space-between;align-items:center;gap:12px"><b>${esc(t('h.byAuthor', { author: author.author }))}</b><button class="btn btn--sm" id="clearAuthor">${esc(t('h.clear'))}</button></div>`
    : '';
  document.getElementById('clearAuthor')?.addEventListener('click', () => { state.author = ''; history.replaceState(null, '', location.pathname); renderList(); });
  box.innerHTML = list.length
    ? `<div class="hub-grid">${list.map(modCard).join('')}</div>`
    : `<div class="note"><b>${esc(t('h.nothing'))}</b> ${esc(t('h.nothing.p'))}</div>`;
  box.querySelectorAll('[data-mod]').forEach((b) => b.addEventListener('click', () => openMod(b.dataset.mod)));
  reveal(box);
}

/* --- окно мода --- */

function markdown(text) {
  const out = [];
  let list = false;
  for (const raw of String(text).split('\n')) {
    const line = raw.trim();
    const item = /^[-*] (.+)/.exec(line);
    if (list && !item) { out.push('</ul>'); list = false; }
    if (!line) continue;
    if (/^#{1,6} /.test(line)) out.push(`<h4>${esc(line.replace(/^#+ /, ''))}</h4>`);
    else if (item) { if (!list) { out.push('<ul>'); list = true; } out.push(`<li>${esc(item[1])}</li>`); }
    else out.push(`<p>${esc(line)}</p>`);
  }
  if (list) out.push('</ul>');
  return out.join('');
}

async function openMod(id) {
  history.replaceState(null, '', `#mod=${encodeURIComponent(id)}`);
  const modal = document.getElementById('modal');
  let m = state.all?.find((x) => x.id === id);
  if (!m) { try { m = await Hub.get(id); } catch { return; } }
  const g = gameById(m.game);
  const cover = m.images[0] ? `url('${esc(m.images[0])}')` : g ? `linear-gradient(135deg,rgba(124,92,255,.55),rgba(10,11,16,.85)),url('${steamHeader(g.steam)}')` : 'var(--accent)';
  modal.innerHTML = `<div class="modal__card" role="dialog" aria-modal="true">
    <div class="modal__cover" style="background-image:${cover}"><button class="icon-btn modal__close" aria-label="×"><svg viewBox="0 0 24 24"><path d="M18 6 6 18M6 6l12 12"/></svg></button></div>
    <div class="modal__body">
      <div class="modal__head"><div>
        <h2>${esc(m.name)}</h2>
        <p style="margin:6px 0 0"><a href="#author=${encodeURIComponent(m.uid)}" class="hmod__by" id="toAuthor">${esc(t('by', { author: m.author }))}</a> · ${esc(g?.name || m.game)} · ${esc(m.version)} · ${esc(t('kind.' + m.kind))}</p>
      </div>
      <div style="display:flex;gap:8px;flex-wrap:wrap">
        ${m.kind === 'package' && m.chunks > 0 ? `<button class="btn btn--primary" id="dl">${esc(t('m.download'))}</button>` : `<button class="btn btn--primary" id="dlCode">${esc(t('m.downloadCode'))}</button>`}
        <button class="btn" id="share">${esc(t('m.share'))}</button>
      </div></div>
      <div class="facts"><span>↓ ${fmt(m.downloads)}</span><span>♥ ${fmt(m.likes)}</span><span>💬 ${fmt(m.comments)}</span><span>${esc(t('m.updated', { when: ago(m.updated) }))}</span>
        ${m.size ? `<span>${esc(t('m.size', { size: (m.size / 1048576).toFixed(1) }))}</span>` : ''}
        ${m.tags.map((x) => `<span>#${esc(tagName(x))}</span>`).join('')}</div>
      <div class="dl-bar" id="dlBar" hidden><i></i></div><span id="dlState" class="hash"></span>
      ${m.images.length > 1 ? `<div class="gallery">${m.images.map((u) => `<img src="${esc(u)}" alt="" loading="lazy" />`).join('')}</div>` : ''}
      <div><h3 style="margin-bottom:8px">${esc(t('m.about'))}</h3><div class="md">${markdown(m.description || m.summary)}</div></div>
      ${m.kind === 'script' && m.code ? `<div><h3 style="margin-bottom:6px">${esc(t('m.code'))}</h3><p class="hash" style="margin:0 0 10px">${esc(t('m.codeHint'))}</p>
        <div class="code"><pre>${highlight(m.code).split('\n').map((l) => `<span class="ln">${l || ' '}</span>`).join('')}</pre></div></div>` : ''}
      <p class="note" style="margin:0">${esc(t('m.installHint', { name: m.name }))}</p>
      ${m.sha256 ? `<div class="hash">${esc(t('m.hash'))}: ${esc(m.sha256)} · <a href="https://www.virustotal.com/gui/file/${esc(m.sha256)}" target="_blank" rel="noopener" style="text-decoration:underline">${esc(t('m.vt'))}</a></div>` : ''}
      <div><h3 style="margin-bottom:10px">${esc(t('m.versions'))}</h3><div class="versions" id="versions"><div class="skeleton" style="height:54px"></div></div></div>
      <div><h3 style="margin-bottom:10px">${esc(t('m.comments'))}</h3><div class="comments" id="comments"><div class="skeleton" style="height:54px"></div></div></div>
    </div></div>`;
  modal.hidden = false;
  document.body.style.overflow = 'hidden';
  const close = () => { modal.hidden = true; document.body.style.overflow = ''; history.replaceState(null, '', location.pathname); };
  modal.onclick = (e) => { if (e.target === modal) close(); };
  modal.querySelector('.modal__close').onclick = close;
  document.onkeydown = (e) => { if (e.key === 'Escape' && !modal.hidden) close(); };
  modal.querySelector('#toAuthor').onclick = (e) => { e.preventDefault(); close(); state.author = m.uid; history.replaceState(null, '', `#author=${encodeURIComponent(m.uid)}`); renderList(); };
  modal.querySelector('#share').onclick = async () => {
    const url = `${SITE}hub.html#mod=${encodeURIComponent(m.id)}`;
    try { await navigator.clipboard.writeText(url); toast(t('m.copied')); } catch { prompt('', url); }
  };
  modal.querySelector('#dlCode')?.addEventListener('click', () => {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([m.code], { type: 'text/plain' }));
    a.download = `${m.name.replace(/[^\wа-яё.-]+/gi, '_')}.mls`;
    a.click();
  });
  modal.querySelector('#dl')?.addEventListener('click', async (e) => {
    const btn = e.currentTarget;
    const bar = modal.querySelector('#dlBar');
    const status = modal.querySelector('#dlState');
    btn.disabled = true;
    bar.hidden = false;
    status.textContent = t('m.checking');
    try {
      await Hub.download(m, null, (r) => (bar.firstElementChild.style.width = `${Math.round(r * 100)}%`));
      status.textContent = t('m.ok');
    } catch (err) {
      status.textContent = err.code === 'BAD_HASH' ? t('m.bad') : t('h.error');
    }
    btn.disabled = false;
  });

  Hub.versions(m.id).then((vs) => {
    modal.querySelector('#versions').innerHTML = vs.length ? vs.map((v) => `<div><span><b>${esc(v.version)}</b><small>${esc(v.changelog || '')}</small></span><small>${esc(ago(v.created ? new Date(v.created) : null))}</small></div>`).join('') : '—';
  }).catch(() => (modal.querySelector('#versions').textContent = '—'));
  Hub.comments(m.id).then((cs) => {
    modal.querySelector('#comments').innerHTML = cs.length
      ? cs.map((c) => `<div class="comment"><b>${esc(c.author)}</b><time>${esc(ago(c.created ? new Date(c.created) : null))}</time><p>${esc(c.text)}</p></div>`).join('')
      : `<p class="hash">${esc(t('m.noComments'))}</p>`;
  }).catch(() => (modal.querySelector('#comments').textContent = '—'));
}

/* --- события --- */

document.getElementById('q').addEventListener('input', (e) => { state.q = e.target.value; renderList(); });
document.getElementById('game').addEventListener('change', (e) => { state.game = e.target.value; renderList(); });
document.getElementById('kind').addEventListener('change', (e) => { state.kind = e.target.value; renderList(); });
document.getElementById('sorts').addEventListener('click', (e) => { const s = e.target.closest('[data-sort]'); if (s) { state.sort = s.dataset.sort; renderControls(); renderList(); } });
document.getElementById('tags').addEventListener('click', (e) => { const s = e.target.closest('[data-tag]'); if (s) { state.tag = state.tag === s.dataset.tag ? '' : s.dataset.tag; renderControls(); renderList(); } });
langHooks.push(() => { renderControls(); renderStats(); renderList(); });

function route() {
  const mod = /#mod=([^&]+)/.exec(location.hash);
  const author = /#author=([^&]+)/.exec(location.hash);
  if (author) { state.author = decodeURIComponent(author[1]); renderList(); }
  if (mod) openMod(decodeURIComponent(mod[1]));
}
addEventListener('hashchange', route);

(async () => {
  renderControls();
  renderList();
  try { state.all = await Hub.all(); }
  catch (e) { state.all = []; state.error = e.code || 'ERROR'; }
  renderStats();
  renderList();
  route();
})();
