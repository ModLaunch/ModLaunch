// node build-strings.js <out.json> <src1.js> [src2.js ...]
// Merges { key: [ru, en, uk, es, de, fr, pt-BR, zh-CN] } sources into the app's per-language string file,
// keeping keys already in <out.json>, and checks that every language has the same {placeholders}.
const fs = require('fs');
const path = require('path');
const LANGS = ['ru', 'en', 'uk', 'es', 'de', 'fr', 'pt-BR', 'zh-CN'];
const [out, ...srcs] = process.argv.slice(2);
const result = fs.existsSync(out) ? JSON.parse(fs.readFileSync(out, 'utf8')) : {};
for (const l of LANGS) result[l] ??= {};
let added = 0;
const problems = [];
for (const src of srcs) {
  const table = require(path.resolve(src));
  for (const [key, values] of Object.entries(table)) {
    if (!Array.isArray(values) || values.length !== LANGS.length) { problems.push(`${key}: needs ${LANGS.length} values`); continue; }
    const want = (values[0].match(/\{[a-zA-Z]+\}/g) || []).sort().join(',');
    values.forEach((v, i) => {
      const got = (v.match(/\{[a-zA-Z]+\}/g) || []).sort().join(',');
      if (got !== want) problems.push(`${key} [${LANGS[i]}]: placeholders ${got} vs ${want}`);
      if (!v.trim()) problems.push(`${key} [${LANGS[i]}]: empty`);
      result[LANGS[i]][key] = v;
    });
    added++;
  }
}
if (problems.length) { console.error(problems.join('\n')); process.exit(1); }
fs.writeFileSync(out, JSON.stringify(result, null, 2) + '\n', 'utf8');
console.log(`${added} keys × ${LANGS.length} languages → ${out} (${Object.keys(result.ru).length} keys total)`);
