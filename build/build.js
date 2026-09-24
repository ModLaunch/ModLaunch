'use strict';

/**
 * Сборка ModHub для выпуска (2.0).
 *
 *   1. Копия программы — в .build/app: package.json без инструментов
 *      сборки, папка src и только те зависимости, что нужны в работе.
 *   2. Весь свой JavaScript в копии обфусцируется (javascript-obfuscator).
 *      Сторонние библиотеки (vendor/*.min.js, node_modules) не трогаем.
 *   3. electron-builder упаковывает копию в установщик.
 *
 * Исходники в src остаются читаемыми — обфусцируется только то, что
 * уходит людям.
 *
 *   node build/build.js            — копия + установщик
 *   node build/build.js --stage    — только копия (для проверки)
 */

const fs = require('node:fs');
const path = require('node:path');
const { execSync } = require('node:child_process');
const JavaScriptObfuscator = require('javascript-obfuscator');

const ROOT = path.resolve(__dirname, '..');
const STAGE = path.join(ROOT, '.build', 'app');

/**
 * Настройки подобраны так, чтобы код стал нечитаемым, но программа не
 * потеряла в скорости и не сломалась: без «разворота» управления (он
 * замедляет в разы) и без самозащиты (она ломается от форматирования).
 * Имена верхнего уровня не переименовываются: окно зовёт функции друг
 * друга между файлами (i18n.js → app.js).
 */
const OPTIONS = {
  compact: true,
  target: 'node',
  identifierNamesGenerator: 'hexadecimal',
  renameGlobals: false,
  stringArray: true,
  stringArrayEncoding: ['base64'],
  stringArrayThreshold: 0.75,
  stringArrayRotate: true,
  stringArrayShuffle: true,
  splitStrings: false,
  controlFlowFlattening: false,
  deadCodeInjection: false,
  selfDefending: false,
  debugProtection: false,
  numbersToExpressions: false,
  transformObjectKeys: false,
  unicodeEscapeSequence: false,
  sourceMap: false,
};

function copyDir(from, to) {
  fs.mkdirSync(to, { recursive: true });
  for (const entry of fs.readdirSync(from, { withFileTypes: true })) {
    const a = path.join(from, entry.name);
    const b = path.join(to, entry.name);
    if (entry.isDirectory()) copyDir(a, b);
    else fs.copyFileSync(a, b);
  }
}

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

function stage() {
  fs.rmSync(path.join(ROOT, '.build'), { recursive: true, force: true });
  copyDir(path.join(ROOT, 'src'), path.join(STAGE, 'src'));

  const pkg = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
  delete pkg.devDependencies;
  delete pkg.scripts;
  fs.writeFileSync(path.join(STAGE, 'package.json'), JSON.stringify(pkg, null, 2));
  execSync('npm install --omit=dev --no-audit --no-fund --no-package-lock', { cwd: STAGE, stdio: 'inherit' });

  let count = 0;
  for (const file of walk(path.join(STAGE, 'src'))) {
    if (!file.endsWith('.js') || /[\\/]vendor[\\/]/.test(file) || file.endsWith('.min.js')) continue;
    const code = fs.readFileSync(file, 'utf8');
    const browser = /[\\/]src[\\/](renderer|setup)[\\/]/.test(file);
    const result = JavaScriptObfuscator.obfuscate(code, { ...OPTIONS, target: browser ? 'browser' : 'node' });
    fs.writeFileSync(file, result.getObfuscatedCode());
    count += 1;
  }
  console.log(`[build] обфусцировано файлов: ${count}`);
}

stage();
if (!process.argv.includes('--stage')) {
  execSync('npx electron-builder --win nsis --x64', { cwd: ROOT, stdio: 'inherit' });
}
