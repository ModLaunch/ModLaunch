'use strict';

const fs = require('node:fs');
const path = require('node:path');

const { downloadFile } = require('./download');
const { findModRoots, extractModRoot, extractFolder, hasFolder, readEntryText, toZip } = require('./archive');
const thunderstore = require('./sources/thunderstore');
const modlinks = require('./sources/modlinks');
const { ReShade, looksLikePreset, presetEffects } = require('./reshade');
const { t } = require('../i18n');

/**
 * Конвейер установки — то, ради чего всё остальное и написано.
 *
 * Один вызов проходит весь путь, который человек сейчас делает руками
 * по инструкции: разрешить зависимости, скачать, найти внутри архива
 * настоящий корень мода, разложить по папкам, записать в реестр.
 *
 * Каждый шаг сообщает о себе наружу, потому что молчащий индикатор
 * прогресса — это тот же гайд, только теперь в голове у пользователя.
 */

/**
 * @param {object} ctx
 * @param {object} ctx.game адаптер игры
 * @param {object} ctx.state результат games.inspect
 * @param {import('./registry').ModRegistry} ctx.registry
 * @param {object} mod запись мода из каталога
 * @param {(stage: object) => void} [onProgress]
 * @param {{reinstall?: Set<string>}} [options] reinstall — моды, которые надо
 *   поставить заново, даже если они уже стоят (обновление до новой версии)
 */
async function installFromCatalog(ctx, mod, onProgress = () => {}, options = {}) {
  const { game, state, registry } = ctx;

  onProgress({ code: 'install.deps', mod: mod.name });
  const plan = await buildPlan(game, mod);
  // Загрузчик (BepInExPack) в зависимостях почти у каждого мода Thunderstore.
  // Он ставится своей кнопкой в корень игры — как мод в plugins он только мешает.
  plan.order = plan.order.filter((entry) => !isLoaderPackage(game, entry.id));

  if (plan.missing.length > 0) {
    // Не тихо пропускаем, а говорим прямо: иначе человек получит
    // неработающую игру и будет искать причину сам.
    onProgress({ code: 'install.warn', level: 'warn', missing: plan.missing });
  }

  const installed = [];
  const total = plan.order.length;

  for (const [index, entry] of plan.order.entries()) {
    const reinstall = options.reinstall?.has(entry.id) ?? false;
    if (!reinstall && registry.has(entry.id) && !registry.get(entry.id).missing) {
      onProgress({ code: 'install.exists', mod: entry.name, index: index + 1, total });
      continue;
    }

    onProgress({ code: 'install.download', mod: entry.name, index: index + 1, total });
    const archive = await downloadFile(entry.downloadUrl, {
      fileName: `${entry.id.replace(/[^\w.-]+/g, '_')}.zip`,
      sha256: entry.sha256 ?? undefined,
      onProgress: (p) =>
        onProgress({
          code: 'install.download',
          mod: entry.name,
          index: index + 1,
          total,
          ratio: p.ratio,
          // Байты — для скорости и «осталось» в окне загрузок.
          bytes: p.received,
          bytesTotal: p.total,
        }),
    });

    onProgress({ code: 'install.extract', mod: entry.name, index: index + 1, total });
    const record = await installAny(ctx, archive, {
      id: entry.id,
      name: entry.name,
      version: entry.version,
      author: entry.author,
      source: entry.source,
      url: entry.url,
      icon: entry.icon ?? null,
      requestedBy: entry.id === mod.id ? null : mod.id,
    });

    // Сборки Thunderstore (3.1) приносят настройки модов в config/ — им
    // место в BepInEx/config, иначе сборка работает «с настройками по умолчанию».
    if (entry.source === 'thunderstore' && game.loader?.kind === 'bepinex') applyPackConfig(state, archive);

    fs.rmSync(archive, { force: true });
    installed.push(record);
  }

  onProgress({ code: 'install.done', mod: mod.name, installed: installed.length });
  return { installed, missing: plan.missing };
}

/** config/ (или BepInEx/config/) из пакета — в BepInEx/config игры. */
function applyPackConfig(state, archive) {
  const target = path.join(state.path, 'BepInEx', 'config');
  for (const folder of ['config', 'BepInEx/config']) {
    try {
      if (hasFolder(archive, folder)) extractFolder(archive, folder, target);
    } catch {
      /* не смогли разложить настройки — моды всё равно стоят */
    }
  }
}

function isLoaderPackage(game, id) {
  const wanted = String(game.loader?.thunderstorePackage ?? '').toLowerCase();
  const name = String(id ?? '').toLowerCase();
  return Boolean(name) && (name === wanted || /-bepinexpack(_[\w]+)?$/.test(name));
}

/**
 * Имена папок-«контейнеров»: архив вида BepInEx/plugins/Мод.dll даёт корень
 * «plugins», и два таких мода легли бы в одну папку — второй стёр бы первый.
 * Такую папку называем по самому моду.
 */
const GENERIC_FOLDER = /^(plugins|bepinex|patchers|core|mods|files|release|releases|bin|dll|dlls|build|output|x64|net\d*|netstandard[\d.]*)$/i;

/**
 * Строит порядок установки: зависимости раньше того, что от них зависит.
 */
async function buildPlan(game, mod) {
  if (game.catalog.kind === 'thunderstore') {
    return thunderstore.resolveDependencies(game.catalog.community, mod.id);
  }
  if (game.catalog.kind === 'modlinks') {
    return modlinks.resolveDependencies(mod.id);
  }
  // Каталога нет (Nexus) — ставим ровно то, что попросили.
  return { order: [mod], missing: [] };
}

/**
 * Архив с модом или с шейдер-пресетом (2.1): пресет ReShade ставится в папку
 * игры вместе с самим ReShade и нужными эффектами, всё остальное — как раньше.
 * @param {object} ctx { game, state, registry, reshade }
 */
async function installAny(ctx, sourcePath, meta = {}, onProgress = () => {}) {
  let converted;
  try {
    converted = toZip(sourcePath);
  } catch (error) {
    if (error.code === 'ARCHIVE_FORMAT') throw new Error(t('err.archiveFormat', { file: path.basename(sourcePath) }));
    throw error;
  }
  try {
    if (ctx.game.reshade && looksLikePreset(converted.path)) {
      return await installPreset(ctx, converted.path, meta, onProgress);
    }
    return installZip(ctx, converted.path, meta);
  } finally {
    if (converted.temporary) fs.rmSync(converted.path, { force: true });
  }
}

/**
 * Шейдер-пресет: ReShade (если его нет) → пресет в папку игры → нужные ему
 * эффекты → пресет становится текущим. В реестре — запись kind: preset.
 */
async function installPreset(ctx, zipPath, meta, onProgress) {
  const { game, state, registry } = ctx;
  const reshade = ctx.reshade ?? new ReShade({ cacheDir: path.join(require('node:os').tmpdir(), 'modhub-reshade') });
  const dir = ReShade.dirOf(game, state.path);

  if (!reshade.detect(dir).dll) {
    onProgress({ code: 'reshade.install', mod: meta.name });
    await reshade.install(dir, game.reshade.api, onProgress);
  }
  const presets = reshade.extractPreset(dir, zipPath);
  if (!presets.length) throw new Error(t('err.presetEmpty'));

  const wanted = new Set();
  for (const file of presets) {
    for (const fx of presetEffects(fs.readFileSync(path.join(dir, file), 'utf8'))) wanted.add(fx);
  }
  const effects = await reshade.ensureEffects(dir, [...wanted], onProgress);
  reshade.activate(dir, path.join(dir, presets[0]));

  let primary = null;
  for (const file of presets) {
    const saved = registry.add({
      id: primary && meta.id ? `${meta.id}#${file}` : meta.id ?? `preset:${file}`,
      name: primary ? file.replace(/\.ini$/i, '') : meta.name ?? file.replace(/\.ini$/i, ''),
      version: meta.version ?? '',
      author: meta.author ?? '',
      source: meta.source ?? 'file',
      url: meta.url ?? null,
      icon: meta.icon ?? null,
      kind: 'preset',
      folder: file,
      fileCount: 1,
      dependencies: [],
      requires: [],
      missingEffects: effects.missing,
      requestedBy: primary && meta.id ? meta.id : null,
      enabled: true,
      missing: false,
    });
    if (!primary) primary = saved;
  }
  return primary;
}

/**
 * Ставит мод из локального архива — общий путь и для каталога,
 * и для файла, который человек перетащил в окно, и для nxm-ссылки.
 *
 * @returns {object} запись реестра
 */
function installArchive(ctx, sourcePath, meta = {}) {
  let converted;
  try {
    converted = toZip(sourcePath);
  } catch (error) {
    if (error.code === 'ARCHIVE_FORMAT') throw new Error(t('err.archiveFormat', { file: path.basename(sourcePath) }));
    throw error;
  }
  try {
    return installZip(ctx, converted.path, meta);
  } finally {
    if (converted.temporary) fs.rmSync(converted.path, { force: true });
  }
}

function installZip(ctx, archivePath, meta = {}) {
  const { game, state, registry } = ctx;

  const roots = findModRoots(archivePath, game.modMarker);
  if (roots.length === 0) throw new Error(t('err.archiveStructure'));

  fs.mkdirSync(state.modsDir, { recursive: true });

  let primary = null;
  const folders = [];

  for (const root of roots) {
    // Метаданные берём из самого мода: это надёжнее, чем имя файла архива.
    let fileMeta = {};
    if (root.marker && /manifest\.json$/i.test(root.marker)) {
      const text = readEntryText(archivePath, root.marker);
      if (text) fileMeta = game.readModMeta(text, root.name) ?? {};
    }

    const rawName = fileMeta.name || root.name || meta.name || 'mod';
    const generic = GENERIC_FOLDER.test(String(rawName).trim());
    let folderName = sanitiseFolder(generic ? meta.name || meta.id || rawName : rawName);
    // Две папки одного архива (plugins и patchers) не должны лечь в одну.
    if (folders.includes(folderName)) folderName = sanitiseFolder(`${folderName} (${rawName})`);
    const destination = path.join(state.modsDir, folderName);

    // Переустановка поверх: сносим старую папку, иначе останутся файлы
    // от прошлой версии и мод будет вести себя необъяснимо.
    if (fs.existsSync(destination)) {
      fs.rmSync(destination, { recursive: true, force: true });
    }

    const files = extractModRoot(archivePath, root, destination);
    folders.push(folderName);

    const record = {
      // Второй и следующие корни архива — части того же мода, со своим id,
      // иначе запись второй части затёрла бы запись первой.
      id: primary && meta.id ? `${meta.id}#${folderName}` : meta.id ?? fileMeta.id ?? folderName,
      name: primary ? fileMeta.name || folderName : meta.name ?? fileMeta.name ?? folderName,
      version: fileMeta.version || meta.version || '',
      author: fileMeta.author || meta.author || '',
      source: meta.source ?? 'file',
      url: meta.url ?? null,
      // Картинка мода — чтобы во вкладке «Загрузки» он был узнаваем, как в каталоге.
      icon: meta.icon ?? null,
      folder: folderName,
      fileCount: files.length,
      // Зависимости из манифеста; пакет контента (Stardew) зависит и от того,
      // для кого он сделан (ContentPackFor) — это тоже обязательный мод.
      dependencies: [...(fileMeta.dependencies ?? []).map((d) => d.id), fileMeta.contentPackFor].filter(Boolean),
      // Как мод знает себя сам: UniqueID из manifest.json (2.1). По нему
      // другие моды и ищут его в своих зависимостях.
      uniqueId: fileMeta.id && root.marker && /manifest\.json$/i.test(root.marker) ? String(fileMeta.id) : null,
      // Требования со страницы мода на Nexus (2.1): у модов Subnautica это Nautilus.
      requires: primary ? [] : (meta.requires ?? []),
      requestedBy: primary && meta.id ? meta.id : meta.requestedBy ?? null,
      enabled: true,
      missing: false,
    };

    const saved = registry.add(record);
    if (!primary) primary = saved;
  }

  return primary;
}

function sanitiseFolder(name) {
  return (
    String(name)
      .replace(/[<>:"/\\|?*\x00-\x1f]/g, '')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, 120) || 'mod'
  );
}

/**
 * Проверяет, все ли зависимости установленных модов на месте.
 * Это ответ на второй по частоте вопрос пользователя — «почему не работает».
 */
function checkDependencies(registry) {
  const installedIds = new Set(registry.list().map((m) => m.id.toLowerCase()));
  const problems = [];

  for (const mod of registry.list()) {
    if (!mod.enabled) continue;
    for (const dep of mod.dependencies ?? []) {
      if (!dep) continue;
      if (installedIds.has(String(dep).toLowerCase())) continue;
      problems.push({ mod: mod.name, modId: mod.id, missing: dep });
    }
  }

  return problems;
}

module.exports = { installFromCatalog, installArchive, installAny, installPreset, checkDependencies, buildPlan, isLoaderPackage, GENERIC_FOLDER };
