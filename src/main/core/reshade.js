'use strict';

const fs = require('node:fs');
const path = require('node:path');
const AdmZip = require('adm-zip');
const { downloadFile, fetchText } = require('./download');

/**
 * Шейдеры: ReShade и его пресеты (2.1).
 *
 * Шейдер-пресет — это не мод игры, а настройки ReShade: файл .ini со списком
 * эффектов и их параметров. До 2.1 ModHub клал такие архивы в папку плагинов
 * BepInEx или в Mods — и они не делали ровным счётом ничего. Теперь:
 *
 *   1. ReShade. Нет — ставим сами: берём свежий установщик с reshade.me,
 *      достаём из него ReShade64.dll и кладём рядом с exe игры под именем,
 *      которое нужно этой игре (dxgi.dll для DirectX 10–12, opengl32.dll для
 *      OpenGL). Рядом — ReShade.ini: где искать эффекты, какой пресет открыть,
 *      и без обучающего окна при первом запуске.
 *
 *   2. Эффекты. В пресете перечислены техники вида «LumaSharpen@LumaSharpen.fx».
 *      По официальному списку пакетов ReShade (EffectPackages.ini) находим,
 *      в каких пакетах лежат нужные .fx, и ставим только их, как это делает
 *      установщик ReShade. Эффекты, которые принёс сам архив пресета, тоже
 *      раскладываем по местам.
 *
 *   3. Пресет — в папку игры, и он сразу становится текущим (PresetPath).
 *      В игре меню ReShade открывается клавишей Home.
 */

const SITE = 'https://reshade.me/';
const PACKAGES_URL = 'https://raw.githubusercontent.com/crosire/reshade-shaders/list/EffectPackages.ini';
const DLL_NAME = { dx11: 'dxgi.dll', dx12: 'dxgi.dll', dx10: 'dxgi.dll', dx9: 'd3d9.dll', opengl: 'opengl32.dll' };
const PROXY_DLLS = ['dxgi.dll', 'd3d11.dll', 'd3d9.dll', 'opengl32.dll', 'd3d12.dll'];
const DAY = 24 * 60 * 60 * 1000;

/* --- INI ---------------------------------------------------------------- */

function parseIni(text) {
  const out = {};
  let section = '';
  for (const raw of String(text ?? '').replace(/^\uFEFF/, '').split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith(';') || line.startsWith('#')) continue;
    const head = /^\[(.+)\]$/.exec(line);
    if (head) {
      section = head[1];
      out[section] = out[section] ?? {};
      continue;
    }
    const eq = line.indexOf('=');
    if (eq <= 0) continue;
    out[section] = out[section] ?? {};
    out[section][line.slice(0, eq).trim()] = line.slice(eq + 1).trim();
  }
  return out;
}

/** Поменять или добавить ключи, не трогая остальное (комментарии, порядок). */
function patchIni(text, section, values) {
  const clean = String(text ?? '').replace(/^\uFEFF/, '');
  const lines = clean.trim() ? clean.split(/\r?\n/) : [];
  let start = lines.findIndex((l) => l.trim() === `[${section}]`);
  if (start === -1) {
    if (lines.length && lines[lines.length - 1].trim() !== '') lines.push('');
    lines.push(`[${section}]`);
    start = lines.length - 1;
  }
  let end = lines.findIndex((l, i) => i > start && /^\[.+\]$/.test(l.trim()));
  if (end === -1) end = lines.length;
  for (const [key, value] of Object.entries(values)) {
    const at = lines.findIndex((l, i) => i > start && i < end && l.split('=')[0].trim() === key);
    if (at !== -1) lines[at] = `${key}=${value}`;
    else {
      // Новый ключ — сразу за последней непустой строкой раздела.
      let insert = end;
      while (insert - 1 > start && lines[insert - 1].trim() === '') insert -= 1;
      lines.splice(insert, 0, `${key}=${value}`);
      end += 1;
    }
  }
  return lines.join('\r\n').replace(/(\r\n)+$/, '') + '\r\n';
}

/** Эффекты, которые просит пресет: «Техника@Файл.fx» → Файл.fx. */
function presetEffects(text) {
  const ini = parseIni(text);
  const general = ini[''] ?? {};
  const list = [general.Techniques, general.TechniqueSorting, ini.GENERAL?.Techniques].filter(Boolean).join(',');
  const files = new Set();
  for (const item of list.split(',')) {
    const at = item.indexOf('@');
    if (at > 0) files.add(item.slice(at + 1).trim().toLowerCase());
  }
  // Секции пресета называются по файлам эффектов: [LumaSharpen.fx].
  for (const name of Object.keys(ini)) if (/\.fx$/i.test(name)) files.add(name.toLowerCase());
  return [...files];
}

function isPresetText(text) {
  return /^\s*(Techniques|TechniqueSorting)\s*=/m.test(String(text ?? ''));
}

/* --- архивы -------------------------------------------------------------- */

/**
 * zip, приклеенный к exe (так устроен установщик ReShade): смещения внутри
 * считаются от начала zip, а не файла — отрезаем всё, что до него.
 */
function openAppendedZip(buffer) {
  const eocd = buffer.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  if (eocd < 0) throw new Error('no zip inside');
  const cdSize = buffer.readUInt32LE(eocd + 12);
  const cdOffset = buffer.readUInt32LE(eocd + 16);
  const start = eocd - cdSize - cdOffset;
  if (start < 0) throw new Error('broken zip offsets');
  return new AdmZip(buffer.subarray(start));
}

/**
 * Архив из Nexus похож на пресет ReShade: в нём .ini с Techniques=, эффекты
 * .fx или папка reshade-shaders. Моды (dll, manifest.json) сюда не попадают.
 */
function looksLikePreset(zipPath) {
  let zip;
  try {
    zip = new AdmZip(zipPath);
  } catch {
    return false;
  }
  const entries = zip.getEntries().filter((e) => !e.isDirectory);
  if (entries.some((e) => /\.dll$/i.test(e.entryName) && !/(^|\/)(dxgi|d3d9|d3d11|d3d12|opengl32|reshade\d*)\.dll$/i.test(e.entryName))) return false;
  if (entries.some((e) => /(^|\/)manifest\.json$/i.test(e.entryName))) return false;
  if (entries.some((e) => /(^|\/)reshade-shaders\//i.test(e.entryName) || /\.fxh?$/i.test(e.entryName))) return true;
  return entries.some((e) => /\.ini$/i.test(e.entryName) && e.header.size < 512 * 1024 && isPresetText(e.getData().toString('utf8')));
}

/* --- ReShade в папке игры ------------------------------------------------ */

class ReShade {
  /** @param {{cacheDir: string, onProgress?: Function}} options */
  constructor({ cacheDir, fetchText: getText = fetchText, download = downloadFile } = {}) {
    this.cacheDir = cacheDir;
    this.fetchText = getText;
    this.download = download;
  }

  /** Папка, где лежит exe игры: туда ставится ReShade и пресеты. */
  static dirOf(game, gamePath) {
    try {
      return path.dirname(game.launch(gamePath).command);
    } catch {
      return gamePath;
    }
  }

  /** Стоит ли ReShade: его ini или прокси-dll с его подписью. */
  detect(dir) {
    const ini = path.join(dir, 'ReShade.ini');
    const dll = PROXY_DLLS.map((name) => path.join(dir, name)).find((file) => {
      try {
        if (!fs.existsSync(file)) return false;
        const fd = fs.openSync(file, 'r');
        const buffer = Buffer.alloc(Math.min(fs.statSync(file).size, 8 * 1024 * 1024));
        fs.readSync(fd, buffer, 0, buffer.length, 0);
        fs.closeSync(fd);
        return buffer.includes(Buffer.from('ReShade'));
      } catch {
        return false;
      }
    });
    const installed = Boolean(dll) || fs.existsSync(ini);
    let preset = null;
    try {
      const data = parseIni(fs.readFileSync(ini, 'utf8'));
      preset = data.GENERAL?.PresetPath ?? null;
    } catch {
      /* ini нет — пресета тоже */
    }
    return { installed, dll: dll ? path.basename(dll) : null, ini: fs.existsSync(ini), preset };
  }

  /** Ссылка на свежий установщик с reshade.me (без аддонов — он безопаснее для онлайн-игр). */
  async latestSetupUrl() {
    const html = await this.fetchText(SITE, { timeout: 20000 });
    const links = [...String(html).matchAll(/href="([^"]*ReShade_Setup_[\d.]+\.exe)"/g)].map((m) => m[1]);
    if (!links.length) throw Object.assign(new Error('no ReShade download link'), { code: 'RESHADE_SITE' });
    return new URL(links[0], SITE).href;
  }

  /** ReShade64.dll из установщика — с запасом в кэше, чтобы не качать каждый раз. */
  async dll(onProgress = () => {}) {
    fs.mkdirSync(this.cacheDir, { recursive: true });
    const url = await this.latestSetupUrl();
    const version = (/ReShade_Setup_([\d.]+)\.exe/.exec(url) ?? [])[1] ?? 'latest';
    const cached = path.join(this.cacheDir, `ReShade64-${version}.dll`);
    if (fs.existsSync(cached) && fs.statSync(cached).size > 100 * 1024) return { file: cached, version };
    onProgress({ code: 'reshade.download', detail: `ReShade ${version}` });
    const setup = await this.download(url, {
      fileName: `ReShade_Setup_${version}.exe`,
      onProgress: (p) => onProgress({ code: 'reshade.download', detail: `ReShade ${version}`, ratio: p.ratio }),
    });
    try {
      const zip = openAppendedZip(fs.readFileSync(setup));
      const entry = zip.getEntries().find((e) => /(^|\/)ReShade64\.dll$/i.test(e.entryName));
      if (!entry) throw Object.assign(new Error('ReShade64.dll not found in setup'), { code: 'RESHADE_SETUP' });
      fs.writeFileSync(cached, entry.getData());
    } finally {
      fs.rmSync(setup, { force: true });
    }
    return { file: cached, version };
  }

  /**
   * Поставить ReShade в папку игры.
   * @param {string} dir папка exe
   * @param {string} api dx11 | dx12 | dx9 | opengl
   */
  async install(dir, api, onProgress = () => {}) {
    const present = this.detect(dir);
    if (!present.dll) {
      const { file, version } = await this.dll(onProgress);
      onProgress({ code: 'reshade.install', detail: `ReShade ${version}` });
      fs.copyFileSync(file, path.join(dir, DLL_NAME[api] ?? 'dxgi.dll'));
    }
    this.writeIni(dir, {});
    fs.mkdirSync(path.join(dir, 'reshade-shaders', 'Shaders'), { recursive: true });
    fs.mkdirSync(path.join(dir, 'reshade-shaders', 'Textures'), { recursive: true });
    // Стандартные эффекты ставит и официальный установщик: без них ReShade пуст.
    await this.ensureEffects(dir, [], onProgress, { required: true });
    return this.detect(dir);
  }

  /** ReShade.ini: где эффекты, какой пресет, клавиша меню, без обучения. */
  writeIni(dir, { presetPath } = {}) {
    const file = path.join(dir, 'ReShade.ini');
    let text = '';
    try {
      text = fs.readFileSync(file, 'utf8');
    } catch {
      /* новый */
    }
    const ini = parseIni(text);
    const general = {
      EffectSearchPaths: ini.GENERAL?.EffectSearchPaths || '.\\reshade-shaders\\Shaders\\**',
      TextureSearchPaths: ini.GENERAL?.TextureSearchPaths || '.\\reshade-shaders\\Textures\\**',
    };
    if (presetPath !== undefined) general.PresetPath = presetPath;
    else if (!ini.GENERAL?.PresetPath) general.PresetPath = '.\\ReShadePreset.ini';
    text = patchIni(text, 'GENERAL', general);
    if (!ini.OVERLAY?.TutorialProgress) text = patchIni(text, 'OVERLAY', { TutorialProgress: '4' });
    if (!ini.INPUT?.KeyOverlay) text = patchIni(text, 'INPUT', { KeyOverlay: '36,0,0,0' });
    fs.writeFileSync(file, text, 'utf8');
  }

  /** Сделать пресет текущим (или вернуть пустой, когда его выключили). */
  activate(dir, presetFile) {
    const rel = presetFile ? `.\\${path.relative(dir, presetFile)}` : '.\\ReShadePreset.ini';
    this.writeIni(dir, { presetPath: rel });
  }

  /* --- пакеты эффектов ---------------------------------------------------- */

  async packages() {
    fs.mkdirSync(this.cacheDir, { recursive: true });
    const file = path.join(this.cacheDir, 'EffectPackages.ini');
    let text = null;
    try {
      if (Date.now() - fs.statSync(file).mtimeMs < DAY) text = fs.readFileSync(file, 'utf8');
    } catch {
      /* кэша нет */
    }
    if (!text) {
      try {
        text = await this.fetchText(PACKAGES_URL, { timeout: 20000 });
        fs.writeFileSync(file, text, 'utf8');
      } catch (error) {
        if (fs.existsSync(file)) text = fs.readFileSync(file, 'utf8');
        else throw error;
      }
    }
    return Object.entries(parseIni(text))
      .filter(([key]) => /^\d+$/.test(key))
      .map(([key, p]) => ({
        key,
        name: p.PackageName ?? key,
        required: p.Required === '1',
        enabled: p.Enabled === '1',
        url: p.DownloadUrl,
        installPath: p.InstallPath || '.\\reshade-shaders\\Shaders',
        texturePath: p.TextureInstallPath || '.\\reshade-shaders\\Textures',
        effects: String(p.EffectFiles ?? '').split(',').map((s) => s.trim().toLowerCase()).filter(Boolean),
        deny: String(p.DenyEffectFiles ?? '').split(',').map((s) => s.trim().toLowerCase()).filter(Boolean),
      }))
      .filter((p) => p.url);
  }

  /** Какие .fx уже есть в папке эффектов. */
  presentEffects(dir) {
    const found = new Set();
    const root = path.join(dir, 'reshade-shaders');
    const walk = (d) => {
      let entries;
      try {
        entries = fs.readdirSync(d, { withFileTypes: true });
      } catch {
        return;
      }
      for (const e of entries) {
        if (e.isDirectory()) walk(path.join(d, e.name));
        else if (/\.fx$/i.test(e.name)) found.add(e.name.toLowerCase());
      }
    };
    walk(root);
    return found;
  }

  /**
   * Поставить пакеты эффектов, в которых лежат нужные .fx (и обязательные).
   * @returns {Promise<{installed: string[], missing: string[]}>}
   */
  async ensureEffects(dir, wanted, onProgress = () => {}, { required = false } = {}) {
    const list = await this.packages();
    const have = this.presentEffects(dir);
    const need = wanted.filter((fx) => !have.has(fx));
    const chosen = list.filter((p) => (required && p.required && !p.effects.every((fx) => have.has(fx))) || p.effects.some((fx) => need.includes(fx)));
    const installed = [];
    for (const pack of chosen) {
      onProgress({ code: 'reshade.effects', detail: pack.name });
      const archive = await this.download(pack.url, { fileName: `reshade-${pack.key}.zip` });
      try {
        this.extractPackage(dir, archive, pack);
        installed.push(pack.name);
      } finally {
        fs.rmSync(archive, { force: true });
      }
    }
    const after = this.presentEffects(dir);
    return { installed, missing: wanted.filter((fx) => !after.has(fx)) };
  }

  /** Архив пакета: …/Shaders/** → InstallPath, …/Textures/** → TextureInstallPath. */
  extractPackage(dir, archive, pack) {
    const zip = new AdmZip(archive);
    const target = (rel) => path.join(dir, rel.replace(/^\.\\/, '').replace(/\\/g, path.sep));
    for (const entry of zip.getEntries()) {
      if (entry.isDirectory) continue;
      const name = entry.entryName.replace(/\\/g, '/');
      const shaders = /(^|\/)Shaders\/(.+)$/i.exec(name);
      const textures = /(^|\/)Textures\/(.+)$/i.exec(name);
      let out = null;
      if (shaders) {
        if (pack.deny.includes(path.basename(shaders[2]).toLowerCase())) continue;
        out = path.join(target(pack.installPath), shaders[2]);
      } else if (textures) {
        out = path.join(target(pack.texturePath), textures[2]);
      }
      if (!out) continue;
      const safeRoot = path.resolve(dir, 'reshade-shaders');
      if (!path.resolve(out).startsWith(safeRoot + path.sep)) continue;
      fs.mkdirSync(path.dirname(out), { recursive: true });
      fs.writeFileSync(out, entry.getData());
    }
  }

  /* --- пресет ------------------------------------------------------------- */

  /**
   * Разложить архив пресета: .ini с Techniques= — в папку игры, эффекты из
   * архива — в reshade-shaders. Возвращает имена файлов пресетов.
   */
  extractPreset(dir, archive) {
    const zip = new AdmZip(archive);
    const presets = [];
    const safeRoot = path.resolve(dir);
    const put = (rel, data) => {
      const out = path.resolve(dir, rel);
      if (!out.startsWith(safeRoot + path.sep)) return;
      fs.mkdirSync(path.dirname(out), { recursive: true });
      fs.writeFileSync(out, data);
    };
    for (const entry of zip.getEntries()) {
      if (entry.isDirectory) continue;
      const name = entry.entryName.replace(/\\/g, '/');
      const base = path.basename(name);
      if (/__MACOSX\//.test(name)) continue;
      // ReShade и его настройки из архива не берём: у игры уже стоит свой.
      if (/^(dxgi|d3d9|d3d11|d3d12|opengl32)\.dll$/i.test(base) || /^reshade(\d*)?\.(ini|dll|log)$/i.test(base)) continue;
      const inShaders = /(^|\/)reshade-shaders\/(.+)$/i.exec(name);
      if (inShaders) {
        put(path.join('reshade-shaders', inShaders[2]), entry.getData());
      } else if (/\.fxh?$/i.test(base)) {
        put(path.join('reshade-shaders', 'Shaders', base), entry.getData());
      } else if (/(^|\/)Textures\/(.+)$/i.test(name)) {
        put(path.join('reshade-shaders', 'Textures', /(^|\/)Textures\/(.+)$/i.exec(name)[2]), entry.getData());
      } else if (/\.ini$/i.test(base)) {
        const data = entry.getData();
        if (!isPresetText(data.toString('utf8'))) continue;
        put(base, data);
        presets.push(base);
      }
    }
    return presets;
  }
}

module.exports = { ReShade, parseIni, patchIni, presetEffects, isPresetText, looksLikePreset, openAppendedZip, DLL_NAME };
