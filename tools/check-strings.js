// Проверка текстов: каждый ключ, который код берёт строкой (I18n.T("…"), I18n.Plural и т. п.), есть на русском и
// английском; пустых строк нет; плейсхолдеры {x} в RU и EN совпадают. Запуск: node tools/check-strings.js
const fs = require('fs');
const path = require('path');
const app = path.resolve(__dirname, '..', 'desktop', 'ModLaunch');
const assets = path.join(app, 'Assets');

const dict = { ru: {}, en: {} };
for (const f of fs.readdirSync(assets).filter((f) => /^strings.*\.json$/.test(f)).sort()) {
  let json;
  try { json = JSON.parse(fs.readFileSync(path.join(assets, f), 'utf8').replace(/^﻿/, '')); } catch (e) { console.error('BAD JSON', f, e.message); process.exitCode = 1; continue; }
  for (const lang of ['ru', 'en']) if (json[lang]) Object.assign(dict[lang], json[lang]);
}

const used = new Map();
function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'obj' || e.name === 'bin') continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p);
    else if (e.name.endsWith('.cs')) {
      const text = fs.readFileSync(p, 'utf8');
      // Только целые ключи: «I18n.T("prefix." + x)» — составной, его части проверяются там, где собираются.
      for (const m of text.matchAll(/I18n\.T\(\s*"([A-Za-z0-9_.\-]+)"\s*[,)]/g)) if (!m[1].endsWith('.') && !used.has(m[1])) used.set(m[1], path.relative(app, p));
    }
  }
}
walk(app);

const missing = { ru: [], en: [] };
const empty = [];
const placeholders = [];
const ph = (s) => (String(s).match(/\{[a-zA-Z]+\}/g) || []).sort().join(',');
for (const [key, file] of used) {
  for (const lang of ['ru', 'en']) if (!(key in dict[lang])) missing[lang].push(`${key} (${file})`);
  for (const lang of ['ru', 'en']) if (key in dict[lang] && String(dict[lang][key]).trim() === '') empty.push(`${lang}:${key}`);
  if (key in dict.ru && key in dict.en && ph(dict.ru[key]) !== ph(dict.en[key])) placeholders.push(`${key}: ru ${ph(dict.ru[key])} / en ${ph(dict.en[key])}`);
}
console.log(`keys used in code: ${used.size}; ru ${Object.keys(dict.ru).length}, en ${Object.keys(dict.en).length}`);
for (const lang of ['ru', 'en']) {
  console.log(`missing in ${lang}: ${missing[lang].length}`);
  missing[lang].slice(0, 40).forEach((k) => console.log('  ' + k));
}
console.log(`empty: ${empty.length}`); empty.slice(0, 20).forEach((k) => console.log('  ' + k));
console.log(`placeholder mismatch: ${placeholders.length}`); placeholders.slice(0, 40).forEach((k) => console.log('  ' + k));
if (missing.ru.length || missing.en.length || empty.length || placeholders.length) process.exitCode = 1;
