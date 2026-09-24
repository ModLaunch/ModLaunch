'use strict';

/**
 * Проверка установщика на настоящем Windows (3.0).
 *
 *   node build/build.js && node tools/installer-check.js
 *
 * Запускает собранный dist/ModLaunch-Setup-<версия>.exe с --update (так
 * его запускает автообновление: без вопросов, сразу ставит и открывает
 * программу) и проверяет, что ModLaunch встал: файлы в папке, ярлык в
 * «Пуске», строка в «Приложениях» Windows, программа запустилась.
 * По ходу снимает экран — снимки уходят в tools/selftest-out.
 */

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn, execFileSync } = require('node:child_process');

const ROOT = path.join(__dirname, '..');
const OUT = path.join(__dirname, 'selftest-out');
const version = require('../package.json').version;
const setup = path.join(ROOT, 'dist', `ModLaunch-Setup-${version}.exe`);
const local = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
const target = path.join(local, 'Programs', 'ModLaunch');
const startLink = path.join(process.env.APPDATA || '', 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'ModLaunch.lnk');

fs.mkdirSync(OUT, { recursive: true });
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const results = [];
function check(name, ok, detail = '') {
  results.push({ name, ok: Boolean(ok), detail });
  console.log(`${ok ? 'PASS' : 'FAIL'}  установщик: ${name}${detail ? `  —  ${detail}` : ''}`);
}

const SHOT_PS = path.join(os.tmpdir(), 'modlaunch-shot.ps1');
fs.writeFileSync(
  SHOT_PS,
  [
    'param([string]$File)',
    'Add-Type -AssemblyName System.Windows.Forms, System.Drawing',
    '$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds',
    '$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height',
    '$g = [System.Drawing.Graphics]::FromImage($bmp)',
    '$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)',
    '$bmp.Save($File, [System.Drawing.Imaging.ImageFormat]::Png)',
  ].join('\r\n')
);
function shot(name) {
  try {
    execFileSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', SHOT_PS, path.join(OUT, `${name}.png`)], { timeout: 20000 });
  } catch (error) {
    console.log('снимок не вышел:', error.message);
  }
}

function running(name) {
  try {
    return execFileSync('tasklist', ['/FI', `IMAGENAME eq ${name}`], { encoding: 'utf8' }).toLowerCase().includes(name.toLowerCase());
  } catch {
    return false;
  }
}

async function main() {
  check('установщик собран', fs.existsSync(setup), setup);
  if (!fs.existsSync(setup)) return;
  check('размер установщика', fs.statSync(setup).size > 50 * 1024 * 1024, `${Math.round(fs.statSync(setup).size / 1048576)} МБ`);

  spawn(setup, ['--update'], { detached: true, stdio: 'ignore' }).unref();
  await delay(2500);
  shot('20-setup-splash');

  const exe = path.join(target, 'ModLaunch.exe');
  const until = Date.now() + 240000;
  const windowShotAt = Date.now() + 3500;
  let shotTaken = false;
  while (Date.now() < until && !fs.existsSync(path.join(target, 'resources', 'app.asar'))) {
    // Окно установщика появляется после распаковки — снимаем его через несколько секунд.
    if (!shotTaken && Date.now() > windowShotAt) {
      shot('21-setup-window');
      shotTaken = true;
    }
    await delay(1000);
  }
  shot('22-setup-progress');
  check('файлы программы на месте', fs.existsSync(exe) && fs.existsSync(path.join(target, 'resources', 'app.asar')), target);
  check('отметка установки', fs.existsSync(path.join(target, '.modhub-install.json')));

  // После установки в режиме обновления установщик сам открывает программу.
  const started = Date.now() + 60000;
  while (Date.now() < started && !running('ModLaunch.exe')) await delay(1000);
  check('программа запустилась после установки', running('ModLaunch.exe'));
  await delay(9000);
  shot('23-app-after-install');

  check('ярлык в «Пуске»', fs.existsSync(startLink), startLink);
  let uninstall = '';
  try {
    uninstall = execFileSync('reg', ['query', 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ModHub', '/v', 'DisplayName'], { encoding: 'utf8' });
  } catch (error) {
    uninstall = String(error.stdout || error.message);
  }
  check('строка в «Приложениях» Windows', /ModLaunch/.test(uninstall), uninstall.trim().split('\n').pop()?.trim());

  try {
    execFileSync('taskkill', ['/IM', 'ModLaunch.exe', '/F'], { stdio: 'ignore' });
  } catch {
    /* уже закрыта */
  }
}

main()
  .catch((error) => check('проверка дошла до конца', false, error.stack || error.message))
  .finally(() => {
    const failed = results.filter((r) => !r.ok).length;
    fs.writeFileSync(path.join(OUT, 'installer.json'), JSON.stringify({ results, failed }, null, 2));
    console.log(`\nИтого: ${results.length - failed} PASS, ${failed} FAIL`);
    process.exit(failed ? 1 : 0);
  });
