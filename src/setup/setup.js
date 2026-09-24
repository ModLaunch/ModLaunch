'use strict';

/*
 * Окно установки и удаления. Всю работу делает главный процесс
 * (src/main/setup), здесь только экраны и переходы между ними.
 */
(() => {
  const api = window.setup;
  const $ = (selector) => document.querySelector(selector);
  const screen = $('#screen');
  const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

  const state = { info: null, target: '', existing: null, desktop: true, launch: true, wipe: false, progress: 0, busy: false };
  let strings = {};

  const t = (key, vars = {}) => String(strings[key] ?? key).replace(/\{(\w+)\}/g, (_, name) => vars[name] ?? '');
  const esc = (value) =>
    String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

  const ICON_OK = '<svg viewBox="0 0 24 24"><path pathLength="1" d="M5 12.5l4.5 4.5L19 7.5"/></svg>';
  const ICON_ERR = '<svg viewBox="0 0 24 24"><path pathLength="1" d="M12 6v8.5M12 18.2v.3"/></svg>';

  /** Смена экрана: старый уходит влево, новый въезжает справа. */
  async function show(html, mood) {
    document.body.dataset.mood = mood;
    if (screen.innerHTML) {
      screen.classList.remove('screen--in');
      screen.classList.add('screen--out');
      await wait(150);
    }
    screen.classList.remove('screen--out');
    screen.innerHTML = html;
    void screen.offsetWidth;
    screen.classList.add('screen--in');
  }

  const check = (option, label) =>
    `<label class="check"><input type="checkbox" data-opt="${option}" ${state[option] ? 'checked' : ''}><span class="check__box"></span>${esc(label)}</label>`;

  /* ------------------------------ Установка ------------------------------ */

  function welcome() {
    const update = Boolean(state.existing);
    const button = t(update ? 'welcome.updateBtn' : 'welcome.install');
    const note = !update
      ? ''
      : `<div class="note">${esc(
          state.existing.version && state.existing.version !== state.info.version
            ? t('welcome.update', { from: state.existing.version, version: state.info.version })
            : t('welcome.updateLegacy', { version: state.info.version }),
        )}</div>`;

    return show(
      `<h1>${esc(t(update ? 'welcome.titleUpdate' : 'welcome.title'))}</h1>
      <p class="lead">${esc(t('welcome.lead'))}</p>
      ${note}
      <div class="label">${esc(t('welcome.folder'))}</div>
      <div class="path" id="path"><span class="path__text" title="${esc(state.target)}">${esc(state.target)}</span>
        <button class="link" type="button" data-act="choose">${esc(t('welcome.change'))}</button></div>
      ${check('desktop', t('welcome.desktop'))}
      ${check('launch', t('welcome.launch'))}
      <div class="spacer"></div>
      <div class="actions">
        <button class="btn btn--primary" type="button" data-act="install">${esc(button)}</button>
        <button class="btn btn--ghost" type="button" data-act="portable">${esc(t('welcome.portable'))}</button>
      </div>
      <p class="terms">${esc(t('welcome.terms', { button }))} <a data-act="license">${esc(t('welcome.termsLink'))}</a>.</p>`,
      'idle',
    );
  }

  /** Пока копируются файлы — что умеет программа, карточка за карточкой. */
  const FEATURES = [
    ['feat.1', '<path d="M12 3v12m0 0-4.5-4.5M12 15l4.5-4.5M4.5 19.5h15"/>'],
    ['feat.2', '<rect x="3.5" y="5" width="17" height="12" rx="2.5"/><rect x="12.5" y="8" width="5" height="6" rx="1"/>'],
    ['feat.3', '<path d="M12 3.5l2.4 5 5.3.7-3.9 3.7 1 5.3L12 15.7l-4.8 2.5 1-5.3-3.9-3.7 5.3-.7z"/>'],
    ['feat.4', '<circle cx="9" cy="8.5" r="3.2"/><path d="M3.5 19c.9-3 3-4.6 5.5-4.6s4.6 1.6 5.5 4.6"/><path d="M15.5 5.6a3 3 0 0 1 0 5.8M17.4 14.6c1.6.6 2.6 2.1 3.1 4.4"/>'],
    ['feat.5', '<path d="M12 3.5 5 6.5v5c0 4.2 3 7.6 7 9 4-1.4 7-4.8 7-9v-5z"/><path d="m9 12 2.2 2.2L15.5 10"/>'],
  ];
  let featureTimer = null;

  function featuresHtml() {
    return `<div class="feats" id="feats">${FEATURES.map(
      ([key, svg], i) =>
        `<div class="feat${i === 0 ? ' is-on' : ''}"><span class="feat__icon"><svg viewBox="0 0 24 24">${svg}</svg></span><span class="feat__text"><b>${esc(t(key + '.title'))}</b><span>${esc(t(key + '.text'))}</span></span></div>`,
    ).join('')}<div class="feats__dots">${FEATURES.map((_, i) => `<i class="${i === 0 ? 'is-on' : ''}"></i>`).join('')}</div></div>`;
  }

  function rotateFeatures() {
    clearInterval(featureTimer);
    let index = 0;
    featureTimer = setInterval(() => {
      const cards = document.querySelectorAll('.feat');
      const dots = document.querySelectorAll('.feats__dots i');
      if (!cards.length) return clearInterval(featureTimer);
      index = (index + 1) % cards.length;
      cards.forEach((card, i) => card.classList.toggle('is-on', i === index));
      dots.forEach((dot, i) => dot.classList.toggle('is-on', i === index));
    }, 2600);
  }

  async function progressScreen(titleKey, hintKey, vars = {}) {
    state.progress = 0;
    const withFeatures = state.info.mode !== 'uninstall';
    await show(
      `<h1>${esc(t(titleKey, vars))}</h1>
      <p class="step" id="step">&nbsp;</p>
      <div class="pct" id="pct">0<small>%</small></div>
      <div class="track"><div class="fill" id="fill"></div></div>
      <div class="meta" id="meta"></div>
      <div class="spacer"></div>
      ${withFeatures ? featuresHtml() : `<p class="hint">${esc(t(hintKey))}</p>`}`,
      'working',
    );
    if (withFeatures) rotateFeatures();
  }

  /** Праздничный залп конфетти поверх окна. */
  function confetti() {
    const layer = document.createElement('div');
    layer.className = 'confetti';
    const colors = ['#8b6cff', '#b9a6ff', '#ffd27a', '#6fe3d5', '#ff8fb5'];
    for (let i = 0; i < 70; i += 1) {
      const piece = document.createElement('i');
      piece.style.setProperty('--x', `${Math.random() * 100}%`);
      piece.style.setProperty('--dx', `${(Math.random() - 0.5) * 240}px`);
      piece.style.setProperty('--r', `${Math.random() * 720 - 360}deg`);
      piece.style.setProperty('--d', `${1.4 + Math.random() * 1.4}s`);
      piece.style.setProperty('--delay', `${Math.random() * 0.35}s`);
      piece.style.background = colors[i % colors.length];
      layer.append(piece);
    }
    document.body.append(layer);
    setTimeout(() => layer.remove(), 3400);
  }

  // Шаги неравные по времени: основное — копирование, остальное — секунды.
  function onProgress(payload) {
    const fill = $('#fill');
    if (!fill) return;
    let ratio = state.progress;
    if (payload.step === 'close') ratio = Math.max(ratio, 0.03);
    if (payload.step === 'clean') ratio = Math.max(ratio, 0.06);
    if (payload.step === 'copy' && typeof payload.ratio === 'number') ratio = 0.08 + payload.ratio * 0.84;
    if (payload.step === 'integrate') ratio = Math.max(ratio, 0.94);
    if (payload.step === 'done') ratio = 1;
    state.progress = ratio;

    fill.style.width = `${(ratio * 100).toFixed(1)}%`;
    $('#pct').innerHTML = `${Math.round(ratio * 100)}<small>%</small>`;
    if (state.info.mode !== 'uninstall' && payload.step) $('#step').textContent = t(`step.${payload.step}`);
    if (payload.total) {
      const mb = (bytes) => Math.round(bytes / 1048576);
      $('#meta').textContent = t('progress.bytes', { done: mb(payload.done), total: mb(payload.total) });
    }
  }

  async function install() {
    if (state.busy) return;
    state.busy = true;
    if (state.info.mode === 'update') await progressScreen('update.title', 'progress.hint', { version: state.info.version });
    else await progressScreen(state.existing ? 'progress.update' : 'progress.install', 'progress.hint');
    const result = await api.install({ target: state.target, desktop: state.desktop });
    clearInterval(featureTimer);
    state.busy = false;
    if (!result?.ok) return failure(result);
    await wait(450);
    await done();
  }

  async function done() {
    const title = state.existing ? t('done.titleUpdate', { version: state.info.version }) : t('done.title');
    confetti();
    // Обновление из самой программы: сразу открыть её снова.
    if (state.info.mode === 'update') {
      await show(
        `<div class="badge">${ICON_OK}</div>
        <h1>${esc(title)}</h1>
        <p class="lead">${esc(t('update.lead'))}</p>`,
        'done',
      );
      setTimeout(() => api.launch(), 900);
      return;
    }
    await show(
      `<div class="badge">${ICON_OK}</div>
      <h1>${esc(title)}</h1>
      <p class="lead">${esc(t(state.desktop ? 'done.textDesktop' : 'done.textStart'))}</p>
      <div class="spacer"></div>
      <div class="actions">
        <button class="btn btn--primary" type="button" data-act="launch">${esc(t('done.launch'))}</button>
        <button class="btn btn--ghost" type="button" data-act="close">${esc(t('done.close'))}</button>
      </div>`,
      'done',
    );
    if (state.launch) setTimeout(launch, 1500);
  }

  async function launch() {
    const button = $('[data-act="launch"]');
    if (!button || button.disabled) return;
    button.disabled = true;
    button.textContent = t('done.launching');
    await api.launch();
  }

  function failure(result = {}) {
    const uninstall = state.info.mode === 'uninstall';
    const key = `error.${result.code}`;
    const message = strings[key] ? t(key, { message: result.message }) : t('error.generic', { message: result.message ?? '' });
    return show(
      `<div class="badge badge--error">${ICON_ERR}</div>
      <h1>${esc(t(uninstall ? 'uninstall.error' : 'error.title'))}</h1>
      <p class="lead">${esc(message)}</p>
      <div class="spacer"></div>
      <div class="actions">
        <button class="btn btn--primary" type="button" data-act="${uninstall ? 'uninstall' : 'install'}">${esc(t('error.retry'))}</button>
        <button class="btn btn--ghost" type="button" data-act="${uninstall ? 'close' : 'back'}">${esc(t(uninstall ? 'done.close' : 'error.back'))}</button>
      </div>`,
      'error',
    );
  }

  /* ------------------------------ Удаление ------------------------------ */

  function uninstallConfirm() {
    return show(
      `<h1>${esc(t('uninstall.title'))}</h1>
      <p class="lead">${esc(t('uninstall.lead'))}</p>
      ${check('wipe', t('uninstall.wipe'))}
      <div class="spacer"></div>
      <div class="actions">
        <button class="btn btn--danger" type="button" data-act="uninstall">${esc(t('uninstall.go'))}</button>
        <button class="btn btn--ghost" type="button" data-act="close">${esc(t('uninstall.cancel'))}</button>
      </div>`,
      'idle',
    );
  }

  async function uninstall() {
    if (state.busy) return;
    state.busy = true;
    await progressScreen('uninstall.progress', 'uninstall.hint');
    const result = await api.uninstall({ wipe: state.wipe });
    state.busy = false;
    if (!result?.ok) return failure(result);
    await wait(400);
    await show(
      `<div class="badge">${ICON_OK}</div>
      <h1>${esc(t('uninstall.done'))}</h1>
      <p class="lead">${esc(t('uninstall.bye'))}</p>
      <div class="spacer"></div>
      <div class="actions"><button class="btn btn--primary" type="button" data-act="close">${esc(t('done.close'))}</button></div>`,
      'done',
    );
  }

  /* ------------------------------ События ------------------------------ */

  async function choose() {
    const picked = await api.choose(state.target);
    if (!picked) return;
    state.target = picked.target;
    state.existing = picked.existing;
    await welcome();
    $('#path')?.classList.add('path--flash');
  }

  async function openLicense() {
    $('#sheetTitle').textContent = t('license.title');
    $('#sheetBtn').textContent = t('license.close');
    $('#sheetText').textContent = await api.license();
    $('#sheet').hidden = false;
    $('#sheetText').scrollTop = 0;
  }

  const ACTIONS = {
    min: () => api.window('min'),
    close: () => api.window('close'),
    choose,
    install,
    back: welcome,
    launch,
    license: openLicense,
    'sheet-close': () => ($('#sheet').hidden = true),
    uninstall,
    portable: async () => {
      if (state.busy) return;
      document.body.dataset.mood = 'working';
      await api.portable();
    },
  };

  document.addEventListener('click', (event) => {
    const target = event.target.closest('[data-act]');
    if (target) ACTIONS[target.dataset.act]?.();
  });
  document.addEventListener('change', (event) => {
    const option = event.target.dataset?.opt;
    if (option) state[option] = event.target.checked;
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !$('#sheet').hidden) $('#sheet').hidden = true;
  });

  (async function start() {
    const info = await api.info();
    state.info = info;
    strings = info.strings ?? {};
    state.target = info.target;
    state.existing = info.existing;

    document.documentElement.lang = info.lang;
    document.title = t(info.mode === 'uninstall' ? 'window.titleUninstall' : info.mode === 'update' ? 'welcome.titleUpdate' : 'window.title');
    $('#title').textContent = document.title;
    $('#ver').textContent = `v${info.version}`;
    $('#btnMin').title = t('window.min');
    $('#btnClose').title = t('window.close');

    api.onProgress(onProgress);
    if (info.mode === 'uninstall') await uninstallConfirm();
    else if (info.mode === 'update') {
      // Ярлык на рабочем столе не добавляем и не убираем: какой был, такой и останется.
      state.desktop = false;
      state.launch = true;
      await install();
    } else await welcome();
  })();
})();
