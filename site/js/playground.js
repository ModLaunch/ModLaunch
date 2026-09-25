'use strict';

/* Песочница ModScript: редактор с подсветкой, проверка на лету, файлы мода
   и готовый zip — всё в браузере, без сервера. */

Object.assign(EN, {
  's.chip': 'Creator Hub · ModLaunch 5.5',
  's.sub': 'ModLaunch’s modding language. One command per line, plain words, errors shown instantly — and a real mod comes out. Try it right here.',
  's.share': 'Share', 's.mls': 'Download .mls', 's.zip': 'Download mod (zip)',
  's.zipHint': 'A ready package — drop it into the game or install it via ModLaunch.',
  's.ok': 'No errors', 's.errors': 'Errors: {n}', 's.line': 'Line {n}',
  's.shared': 'Link with your code copied', 's.noZip': 'Fix the errors first',
  's.refEyebrow': 'Reference', 's.refTitle': 'Every ModScript command',
  's.refSub': 'Text goes in quotes, blocks in braces, # is a comment. Variables: let price = 10, then $price. Math works too: $price * 2.',
  's.finalTitle': 'Even more in the app', 's.finalSub': 'An editor with hints, 29 examples, one-click install into the game and publishing to ModLaunch Hub.',
  's.format': 'Format: {format}', 's.game': 'Game: {game}', 's.unknownGame': 'unknown game “{game}”',
});
Object.assign(ru, {
  's.ok': 'Ошибок нет', 's.errors': 'Ошибок: {n}', 's.line': 'Строка {n}',
  's.shared': 'Ссылка с вашим кодом скопирована', 's.noZip': 'Сначала исправьте ошибки',
  's.format': 'Формат: {format}', 's.game': 'Игра: {game}', 's.unknownGame': 'неизвестная игра «{game}»',
});

const LOADER = Object.fromEntries(GAMES.map((g) => [g.id, g.loader]));
const src = document.getElementById('src');
const hl = document.getElementById('hl');
const gutter = document.getElementById('gutter');
const select = document.getElementById('example');
let build = null;
let files = {};
let fileTab = '';

function fillExamples() {
  const current = select.value;
  select.innerHTML = MS_EXAMPLES.map((e) => `<option value="${e.id}">${esc(e.title[lang] || e.title.ru)} · ${esc(gameById(e.game)?.name || e.game)}</option>`).join('');
  if (current) select.value = current;
}

function errText(d) {
  const table = MS_ERRORS[lang] || MS_ERRORS.ru;
  return (table[d.msg] || MS_ERRORS.ru[d.msg] || d.msg).replaceAll('{x}', d.x ?? '');
}

function render() {
  const code = src.value;
  hl.innerHTML = highlight(code) + '\n';
  build = ModScript.compile(code);
  const errLines = new Set(build.diags.map((d) => d.line));
  gutter.innerHTML = code.split('\n').map((_, i) => `<span class="${errLines.has(i + 1) ? 'err' : ''}">${i + 1}</span>`).join('');
  syncScroll();

  const ok = build.ok;
  document.getElementById('dot').classList.toggle('bad', !ok);
  const game = gameById(build.game);
  const loader = LOADER[build.game];
  const format = loader === 'SMAPI' ? 'Content Patcher' : 'Thunderstore (BepInEx)';
  document.getElementById('status').textContent = ok
    ? `${t('s.ok')} · ${build.name} ${build.version} · ${game ? game.name : t('s.unknownGame', { game: build.game })} · ${format}`
    : t('s.errors', { n: build.diags.length });
  const diags = document.getElementById('diags');
  diags.innerHTML = build.diags.slice(0, 12).map((d) => `<button data-line="${d.line}">${esc(t('s.line', { n: d.line }))}: ${esc(errText(d))}</button>`).join('');
  diags.hidden = ok;
  document.getElementById('logs').innerHTML = build.log.map((l) => `› ${esc(l)}`).join('<br>');

  files = ok ? ModScript.pack(build, loader) : {};
  const names = Object.keys(files);
  if (!names.includes(fileTab)) fileTab = names.find((n) => n.endsWith('content.json')) || names.find((n) => n.startsWith('config/')) || names[0] || '';
  document.getElementById('files').innerHTML = names.map((n) => `<button class="${n === fileTab ? 'is-on' : ''}" data-file="${esc(n)}">${esc(n.split('/').pop())}</button>`).join('');
  document.getElementById('file').textContent = files[fileTab] ?? '';
  document.getElementById('zip').disabled = !ok;
  try { localStorage.setItem('ms.draft', code); } catch { /* без черновика */ }
}

function syncScroll() {
  hl.scrollTop = src.scrollTop;
  hl.scrollLeft = src.scrollLeft;
  gutter.scrollTop = src.scrollTop;
}

function goLine(n) {
  const lines = src.value.split('\n');
  const at = lines.slice(0, n - 1).reduce((s, l) => s + l.length + 1, 0);
  src.focus();
  src.setSelectionRange(at, at + (lines[n - 1] || '').length);
  src.scrollTop = Math.max(0, (n - 4) * 23);
  syncScroll();
}

/* --- zip без библиотек: файлы без сжатия (store) --- */
const CRC = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; table[n] = c >>> 0; }
  return (bytes) => { let c = 0xffffffff; for (const b of bytes) c = table[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
})();
function makeZip(entries) {
  const enc = new TextEncoder();
  const parts = [];
  const central = [];
  let offset = 0;
  for (const [name, text] of Object.entries(entries)) {
    const nameBytes = enc.encode(name);
    const data = enc.encode(text);
    const crc = CRC(data);
    const local = new DataView(new ArrayBuffer(30));
    local.setUint32(0, 0x04034b50, true); local.setUint16(4, 20, true); local.setUint16(6, 0x0800, true);
    local.setUint32(14, crc, true); local.setUint32(18, data.length, true); local.setUint32(22, data.length, true);
    local.setUint16(26, nameBytes.length, true);
    parts.push(new Uint8Array(local.buffer), nameBytes, data);
    const cen = new DataView(new ArrayBuffer(46));
    cen.setUint32(0, 0x02014b50, true); cen.setUint16(4, 20, true); cen.setUint16(6, 20, true); cen.setUint16(8, 0x0800, true);
    cen.setUint32(16, crc, true); cen.setUint32(20, data.length, true); cen.setUint32(24, data.length, true);
    cen.setUint16(28, nameBytes.length, true); cen.setUint32(42, offset, true);
    central.push(new Uint8Array(cen.buffer), nameBytes);
    offset += 30 + nameBytes.length + data.length;
  }
  const size = central.reduce((s, p) => s + p.length, 0);
  const end = new DataView(new ArrayBuffer(22));
  end.setUint32(0, 0x06054b50, true);
  end.setUint16(8, Object.keys(entries).length, true); end.setUint16(10, Object.keys(entries).length, true);
  end.setUint32(12, size, true); end.setUint32(16, offset, true);
  return new Blob([...parts, ...central, new Uint8Array(end.buffer)], { type: 'application/zip' });
}
function save(blob, name) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 4000);
}
const fileName = () => (build?.name || 'mod').replace(/[^\wа-яё.-]+/gi, '_');

/* --- справка --- */
function renderDocs() {
  document.getElementById('docs').innerHTML = MS_DOCS.map((g) => `<article class="reveal"><h3>${esc(g.title[lang] || g.title.ru)}</h3><dl>${
    g.items.map((i) => `<div><dt>${esc(i.syntax[lang] || i.syntax.ru)}</dt><dd>${esc(i.text[lang] || i.text.ru)}</dd></div>`).join('')}</dl></article>`).join('');
  reveal(document.getElementById('docs'));
}

/* --- события --- */
src.addEventListener('input', render);
src.addEventListener('scroll', syncScroll);
src.addEventListener('keydown', (e) => {
  if (e.key === 'Tab') {
    e.preventDefault();
    const s = src.selectionStart;
    src.setRangeText('  ', s, src.selectionEnd, 'end');
    render();
  }
});
select.addEventListener('change', () => {
  src.value = MS_EXAMPLES.find((x) => x.id === select.value)?.code || '';
  fileTab = '';
  render();
});
document.getElementById('diags').addEventListener('click', (e) => { const b = e.target.closest('[data-line]'); if (b) goLine(Number(b.dataset.line)); });
document.getElementById('files').addEventListener('click', (e) => {
  const b = e.target.closest('[data-file]');
  if (!b) return;
  fileTab = b.dataset.file;
  render();
});
document.getElementById('saveMls').addEventListener('click', () => save(new Blob([src.value], { type: 'text/plain' }), fileName() + '.mls'));
document.getElementById('zip').addEventListener('click', () => {
  if (!build?.ok) { toast(t('s.noZip')); return; }
  save(makeZip(files), `${fileName()}-${build.version}.zip`);
});
document.getElementById('share').addEventListener('click', async () => {
  const data = btoa(unescape(encodeURIComponent(src.value)));
  const url = `${SITE}modscript.html#code=${encodeURIComponent(data)}`;
  history.replaceState(null, '', `#code=${encodeURIComponent(data)}`);
  try { await navigator.clipboard.writeText(url); toast(t('s.shared')); } catch { prompt('', url); }
});
langHooks.push(() => { fillExamples(); renderDocs(); render(); });

/* --- старт: код из ссылки, черновик или первая примочка --- */
fillExamples();
const shared = /#code=([^&]+)/.exec(location.hash);
let initial = null;
if (shared) { try { initial = decodeURIComponent(escape(atob(decodeURIComponent(shared[1])))); } catch { initial = null; } }
if (initial === null) { try { initial = localStorage.getItem('ms.draft'); } catch { initial = null; } }
if (!initial) { select.value = 'sdv-seeds'; initial = MS_EXAMPLES.find((x) => x.id === 'sdv-seeds').code; }
src.value = initial;
renderDocs();
render();
