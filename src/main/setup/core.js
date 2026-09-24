'use strict';

/**
 * Установщик ModHub — часть, которая не зависит от Electron.
 *
 * С версии 1.5 установщиком работает сама программа: ModHub-Setup.exe
 * распаковывает ModHub во временную папку и запускает его в режиме
 * установки. Окно рисуем сами, в стиле программы, а копирование, ярлыки
 * и записи в реестре делаем здесь. Всё, что можно проверить без Windows,
 * вынесено в этот файл и покрыто тестами.
 */

const path = require('node:path');
const os = require('node:os');

// Внутри Electron обычный fs считает app.asar папкой и полез бы копировать
// архив по одному файлу. original-fs видит его тем, чем он является.
let fs;
try {
  fs = require('original-fs');
} catch {
  fs = require('node:fs');
}

const EXE_NAME = 'ModLaunch.exe';
/** Прежние имена программы: такие папки — тоже наши, их обновляем. */
const OLD_EXE_NAMES = ['ModHub.exe'];
/**
 * Идентификатор приложения для Windows (AppUserModelID): по нему панель задач
 * связывает окно с ярлыком и берёт картинку ярлыка. До 1.6 он был
 * «io.modhub.app», и окно цеплялось за старые ярлыки 1.0–1.4, а те
 * показывали прежний значок-кубик из кэша Windows. Новый идентификатор
 * есть только у ярлыков, которые пишет установщик 1.6.1 и новее.
 */
const APP_ID = 'ModHub.Launcher';
/** Иконка лежит отдельным файлом в resources: для нового пути у Windows нет старой картинки в кэше. */
const ICON_FILE = 'modhub.ico';
const MARKER = '.modhub-install.json';
const UNINSTALL_KEY = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ModHub';

function codeError(code, extra = {}) {
  return Object.assign(new Error(code), { code, ...extra });
}

function defaultInstallDir(env = process.env) {
  const local = env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
  return path.join(local, 'Programs', 'ModLaunch');
}

/** Установка 2.x через NSIS: %LOCALAPPDATA%\Programs\modhub (по имени пакета). */
function nsisInstallDir(env = process.env) {
  const local = env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
  return path.join(local, 'Programs', 'modhub');
}

/** Ставим всегда в свою папку: выбрали «D:\Games» — получится «D:\Games\ModLaunch». Прежняя «ModHub» тоже годится. */
function normalizeTarget(dir) {
  const full = path.resolve(String(dir ?? '').trim() || defaultInstallDir());
  return /^(modlaunch|modhub)$/i.test(path.basename(full)) ? full : path.join(full, 'ModLaunch');
}

function isInside(child, parent) {
  const rel = path.relative(path.resolve(parent), path.resolve(child));
  return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
}

function readMarker(dir) {
  try {
    return JSON.parse(fs.readFileSync(path.join(dir, MARKER), 'utf8'));
  } catch {
    return null;
  }
}

/**
 * Что сейчас лежит в папке установки.
 *   missing / empty — ставим начисто;
 *   ours            — там ModHub (наш или старый, из установщика 1.0–1.4), обновляем;
 *   foreign         — чужие файлы: их не трогаем и ставить туда отказываемся.
 */
function inspectTarget(dir) {
  let entries;
  try {
    entries = fs.readdirSync(dir);
  } catch {
    return { kind: 'missing' };
  }
  if (entries.length === 0) return { kind: 'empty' };
  if (entries.includes(MARKER)) return { kind: 'ours', version: readMarker(dir)?.version ?? null };
  if ([EXE_NAME, ...OLD_EXE_NAMES].some((name) => entries.includes(name)) && fs.existsSync(path.join(dir, 'resources', 'app.asar'))) {
    return { kind: 'ours', version: null, legacy: true };
  }
  return { kind: 'foreign' };
}

function listFiles(root) {
  const files = [];
  const walk = (rel) => {
    for (const entry of fs.readdirSync(path.join(root, rel), { withFileTypes: true })) {
      const child = path.join(rel, entry.name);
      if (entry.isDirectory()) walk(child);
      else if (entry.isFile()) files.push({ rel: child, size: fs.statSync(path.join(root, child)).size });
    }
  };
  walk('');
  return files;
}

async function copyTree(source, target, onProgress = () => {}) {
  const files = listFiles(source);
  const total = files.reduce((sum, file) => sum + file.size, 0);
  let done = 0;
  let reported = 0;

  for (const file of files) {
    const to = path.join(target, file.rel);
    await fs.promises.mkdir(path.dirname(to), { recursive: true });
    await fs.promises.copyFile(path.join(source, file.rel), to);
    done += file.size;
    const now = Date.now();
    if (now - reported > 50 || done === total) {
      reported = now;
      onProgress({ done, total, ratio: total ? done / total : 1 });
    }
  }
  return { files: files.length, bytes: total };
}

async function removeDir(dir) {
  await fs.promises.rm(dir, { recursive: true, force: true, maxRetries: 8, retryDelay: 250 });
}

/** Занятый файл, нет прав — человеку это надо объяснить по-разному. */
function explainFsError(error) {
  if (['EBUSY', 'EPERM', 'ENOTEMPTY'].includes(error?.code)) return codeError('SETUP_BUSY');
  if (error?.code === 'EACCES') return codeError('SETUP_ACCESS');
  if (error?.code === 'ENOSPC') return codeError('SETUP_SPACE');
  return error;
}

/**
 * Установка или обновление.
 *
 * closeRunning и integrate приходят снаружи: закрыть запущенный ModHub и
 * прописаться в Windows можно только на Windows, а логика вокруг —
 * проверки папки, порядок шагов, прогресс — одинаковая везде.
 */
async function install({ source, target, version, onStep = () => {}, onProgress, closeRunning, integrate }) {
  const dir = normalizeTarget(target);
  if (isInside(dir, source) || isInside(source, dir)) throw codeError('SETUP_TARGET_SELF');

  const state = inspectTarget(dir);
  if (state.kind === 'foreign') throw codeError('SETUP_TARGET_FOREIGN', { target: dir });

  try {
    onStep('close');
    await closeRunning?.(dir);

    // Старую версию убираем целиком: так от неё не останется файлов,
    // которых в новой уже нет. Настройки и список модов живут в AppData
    // и этим не задеваются.
    if (state.kind === 'ours') {
      onStep('clean');
      await removeDir(dir);
    }

    // Отметку кладём до копирования: если установку прервут на середине,
    // повторная попытка узнает папку как свою, а не откажется от «чужих» файлов.
    onStep('copy');
    await fs.promises.mkdir(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, MARKER), JSON.stringify({ version, installedAt: new Date().toISOString() }));
    const { bytes } = await copyTree(source, dir, onProgress);

    const exe = path.join(dir, EXE_NAME);
    onStep('integrate');
    await integrate?.({ target: dir, exe, version, sizeKB: Math.ceil(bytes / 1024) });

    onStep('done');
    return { target: dir, exe, updated: state.kind === 'ours', from: state.version ?? null };
  } catch (error) {
    throw explainFsError(error);
  }
}

/** Ведёт ли ярлык на ModHub.exe — всё равно, какой версии и из какой папки. */
function isModHubTarget(target) {
  return /(^|[\\/])(modhub|modlaunch)\.exe$/i.test(String(target || '').trim());
}

const sameLink = (a, b) => path.win32.normalize(String(a)).toLowerCase() === path.win32.normalize(String(b)).toLowerCase();

/**
 * Что делать со старыми ярлыками ModHub, найденными на компьютере.
 * found: [{ path, target, kind: 'menu' | 'desktop' | 'pinned' }].
 *   Пуск — лишние копии удаляем: свой свежий ярлык установщик пишет сам;
 *   рабочий стол и панель задач — переписываем на новую версию: человек
 *   сам положил значок туда, где ему удобно, и место сохраняется.
 * keep — наши собственные ярлыки, их не трогаем.
 */
function planShortcutCleanup(found, { keep = [] } = {}) {
  return found
    .filter((item) => isModHubTarget(item.target) && !keep.some((own) => sameLink(own, item.path)))
    .map((item) => ({ path: item.path, action: item.kind === 'menu' ? 'delete' : 'rewrite' }));
}

/* ------------------------------------------------------------------ *
 *  Сценарии PowerShell
 *
 *  Реестр и процессы трогаем через PowerShell, а не reg.exe: он отдаёт
 *  вывод в UTF-8, и путь «C:\Users\Максим\…» доходит до нас целым.
 *  Строки передаются в одинарных кавычках — внутри них PowerShell ничего
 *  не подставляет, и единственное, что надо экранировать, — сам апостроф.
 * ------------------------------------------------------------------ */

const psq = (value) => `'${String(value).replace(/'/g, "''")}'`;
const psDir = (dir) => psq(String(dir).replace(/[\\/]+$/, '') + '\\');

function registerScript({ target, exe, version, sizeKB }) {
  const strings = {
    DisplayName: 'ModLaunch',
    DisplayVersion: version,
    Publisher: 'ModLaunch',
    DisplayIcon: `${exe},0`,
    InstallLocation: target,
    UninstallString: `"${exe}" --uninstall`,
  };
  const numbers = { NoModify: 1, NoRepair: 1, EstimatedSize: Math.max(0, Math.round(sizeKB) || 0) };

  return [
    `$u=${psq(UNINSTALL_KEY)}`,
    'if(-not(Test-Path $u)){New-Item -Path $u -Force|Out-Null}',
    ...Object.entries(strings).map(
      ([name, value]) => `New-ItemProperty -Path $u -Name ${psq(name)} -Value ${psq(value)} -PropertyType String -Force|Out-Null`,
    ),
    ...Object.entries(numbers).map(
      ([name, value]) => `New-ItemProperty -Path $u -Name ${psq(name)} -Value ${value} -PropertyType DWord -Force|Out-Null`,
    ),

    // nxm:// — канал, по которому Nexus Mods отдаёт файлы бесплатным аккаунтам.
    "$c='HKCU:\\Software\\Classes\\nxm'",
    'foreach($k in @($c,"$c\\DefaultIcon","$c\\shell\\open\\command")){if(-not(Test-Path $k)){New-Item -Path $k -Force|Out-Null}}',
    "Set-ItemProperty -Path $c -Name '(default)' -Value 'URL:Nexus Mods Protocol'",
    "New-ItemProperty -Path $c -Name 'URL Protocol' -Value '' -PropertyType String -Force|Out-Null",
    `Set-ItemProperty -Path "$c\\DefaultIcon" -Name '(default)' -Value ${psq(`"${exe}",0`)}`,
    `Set-ItemProperty -Path "$c\\shell\\open\\command" -Name '(default)' -Value ${psq(`"${exe}" "%1"`)}`,

    "$m='HKCU:\\Software\\ModHub'",
    'if(-not(Test-Path $m)){New-Item -Path $m -Force|Out-Null}',
    `New-ItemProperty -Path $m -Name 'InstallDir' -Value ${psq(target)} -PropertyType String -Force|Out-Null`,
    `New-ItemProperty -Path $m -Name 'Version' -Value ${psq(version)} -PropertyType String -Force|Out-Null`,

    // Версии 1.0–1.4 ставились через NSIS и оставили свою строку в
    // «Установленных приложениях». Если она про эту же папку — убираем,
    // иначе в списке будет два ModHub, и один из них — нерабочий.
    `$d=${psDir(target)}`,
    "foreach($r in 'HKCU:','HKLM:'){$b=\"$r\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\";if(Test-Path $b){Get-ChildItem $b -ErrorAction SilentlyContinue|ForEach-Object{$p=Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue;if($_.PSChildName -ne 'ModHub' -and (\"$($p.DisplayName)\" -like 'ModHub*' -or \"$($p.DisplayName)\" -like 'ModLaunch*') -and \"$($p.UninstallString)\".IndexOf($d,[StringComparison]::OrdinalIgnoreCase) -ge 0){Remove-Item $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue}}}}",
  ].join('\n');
}

function unregisterScript({ exe }) {
  return [
    `Remove-Item -Path ${psq(UNINSTALL_KEY)} -Recurse -Force -ErrorAction SilentlyContinue`,
    "$c='HKCU:\\Software\\Classes\\nxm'",
    "$cmd=(Get-ItemProperty -Path \"$c\\shell\\open\\command\" -ErrorAction SilentlyContinue).'(default)'",
    // Протокол снимаем, только если он наш: иначе сломаем менеджер модов,
    // который человек поставил после нас.
    `if($cmd -eq ${psq(`"${exe}" "%1"`)}){Remove-Item -Path $c -Recurse -Force -ErrorAction SilentlyContinue}`,
    "Remove-Item -Path 'HKCU:\\Software\\ModHub' -Recurse -Force -ErrorAction SilentlyContinue",
  ].join('\n');
}

/** Закрывает ModHub, запущенный из папки установки. Себя и свои процессы не трогает. */
function closeScript({ target, exceptPid = 0 }) {
  return [
    `$d=${psDir(target)}`,
    `$me=${Number(exceptPid) || 0}`,
    '$n=0',
    `Get-CimInstance Win32_Process -Filter "Name='${EXE_NAME}' OR ${OLD_EXE_NAMES.map((n) => `Name='${n}'`).join(' OR ')}" -ErrorAction SilentlyContinue|Where-Object{$_.ExecutablePath -and $_.ExecutablePath.StartsWith($d,[StringComparison]::OrdinalIgnoreCase) -and $_.ProcessId -ne $me -and $_.ParentProcessId -ne $me}|ForEach-Object{Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue;$n++}`,
    'if($n -gt 0){Start-Sleep -Milliseconds 900}',
    'Write-Output $n',
  ].join('\n');
}

/** Где уже стоит ModHub, если стоит. */
function probeScript() {
  return "$m=Get-ItemProperty -Path 'HKCU:\\Software\\ModHub' -ErrorAction SilentlyContinue\nif($m){[pscustomobject]@{dir=\"$($m.InstallDir)\";version=\"$($m.Version)\"}|ConvertTo-Json -Compress}";
}

/**
 * Команда, которая удалит папку после нашего выхода: запущенный exe
 * сам себя стереть не может. /s снимает внешние кавычки, поэтому строку
 * собираем целиком и передаём как есть (windowsVerbatimArguments).
 */
function removalCommand(dirs) {
  const parts = dirs.filter(Boolean).map((dir) => `rmdir /s /q "${String(dir).replace(/"/g, '')}"`);
  return `/d /s /c "ping 127.0.0.1 -n 4 >nul & ${parts.join(' & ')}"`;
}

module.exports = {
  EXE_NAME,
  OLD_EXE_NAMES,
  nsisInstallDir,
  APP_ID,
  ICON_FILE,
  MARKER,
  UNINSTALL_KEY,
  codeError,
  defaultInstallDir,
  normalizeTarget,
  isInside,
  inspectTarget,
  isModHubTarget,
  planShortcutCleanup,
  readMarker,
  listFiles,
  copyTree,
  removeDir,
  install,
  psq,
  registerScript,
  unregisterScript,
  closeScript,
  probeScript,
  removalCommand,
};
