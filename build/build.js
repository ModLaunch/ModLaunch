'use strict';

/**
 * Сборка ModLaunch для выпуска.
 *
 *   1. Копия программы — в .build/app: package.json без инструментов
 *      сборки, папка src и только те зависимости, что нужны в работе.
 *   2. electron-builder упаковывает копию в установщик.
 *
 * С 3.0.3 код не запутывается (обфускация убрана): исходники и так открыты
 * на GitHub, а запутанный код антивирусы принимают за вредоносный —
 * Nexus помечал установщик «Some suspicious files».
 *
 *   node build/build.js            — копия + установщик
 *   node build/build.js --stage    — только копия (для проверки)
 */

const fs = require('node:fs');
const path = require('node:path');
const { execSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const STAGE = path.join(ROOT, '.build', 'app');

function copyDir(from, to) {
  fs.mkdirSync(to, { recursive: true });
  for (const entry of fs.readdirSync(from, { withFileTypes: true })) {
    const a = path.join(from, entry.name);
    const b = path.join(to, entry.name);
    if (entry.isDirectory()) copyDir(a, b);
    else fs.copyFileSync(a, b);
  }
}

function stage() {
  fs.rmSync(path.join(ROOT, '.build'), { recursive: true, force: true });
  copyDir(path.join(ROOT, 'src'), path.join(STAGE, 'src'));

  const pkg = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
  delete pkg.devDependencies;
  delete pkg.scripts;
  fs.writeFileSync(path.join(STAGE, 'package.json'), JSON.stringify(pkg, null, 2));
  execSync('npm install --omit=dev --no-audit --no-fund --no-package-lock', { cwd: STAGE, stdio: 'inherit' });
}

stage();
if (!process.argv.includes('--stage')) {
  execSync('npx electron-builder --win --x64 --publish never', { cwd: ROOT, stdio: 'inherit' });
}
