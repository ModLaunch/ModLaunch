'use strict';

const fs = require('node:fs');
const path = require('node:path');
const AdmZip = require('adm-zip');

/**
 * Разбор и распаковка архивов с модами.
 *
 * Главная задача файла — не «распаковать», а понять, ЧТО именно распаковывать.
 * Авторы модов пакуют архивы как им удобно: иногда мод лежит в корне, иногда
 * в папке с названием мода, иногда в двух вложенных папках, иногда в одном
 * архиве лежит сразу несколько модов. Угадывать глубину бессмысленно —
 * надо искать признак мода (маркер) и отталкиваться от него.
 */

/** Служебный мусор, который не должен считаться содержимым мода. */
const JUNK = /(^|\/)(__MACOSX\/|\.DS_Store$|Thumbs\.db$|desktop\.ini$)/i;

function normalise(entryName) {
  return entryName.replace(/\\/g, '/');
}

/**
 * @typedef {object} ModRoot
 * @property {string} prefix  путь внутри архива до папки мода ('' — корень архива)
 * @property {string} marker  файл, по которому мод опознан
 * @property {string} name    предполагаемое имя папки мода
 */

/**
 * Находит внутри архива корни модов.
 *
 * @param {string} zipPath
 * @param {object} spec описание того, как опознаётся мод для этой игры
 * @param {string[]} [spec.markerFiles] точные имена файлов-маркеров (manifest.json)
 * @param {RegExp}   [spec.markerPattern] шаблон маркера (/\.dll$/i)
 * @param {number}   [spec.maxDepth] глубже этого уровня маркеры игнорируются
 * @returns {ModRoot[]}
 */
function findModRoots(zipPath, spec) {
  const zip = new AdmZip(zipPath);
  const entries = zip
    .getEntries()
    .filter((e) => !e.isDirectory)
    .map((e) => normalise(e.entryName))
    .filter((name) => !JUNK.test(name));

  const markerFiles = (spec.markerFiles ?? []).map((f) => f.toLowerCase());
  const maxDepth = spec.maxDepth ?? 6;
  const roots = [];

  for (const entry of entries) {
    const parts = entry.split('/');
    const base = parts[parts.length - 1];
    const depth = parts.length - 1;
    if (depth > maxDepth) continue;

    const isMarker =
      markerFiles.includes(base.toLowerCase()) ||
      (spec.markerPattern instanceof RegExp && spec.markerPattern.test(base));

    if (!isMarker) continue;

    const prefix = parts.slice(0, -1).join('/');
    if (roots.some((r) => r.prefix === prefix)) continue;
    roots.push({
      prefix,
      marker: entry,
      name: prefix === '' ? path.parse(zipPath).name : parts[parts.length - 2],
    });
  }

  // Маркер найден и внутри вложенной папки, и в её родителе — берём внешний,
  // иначе мод с подпапкой плагинов установится дважды.
  const filtered = roots.filter(
    (r) => !roots.some((other) => other !== r && r.prefix.startsWith(other.prefix + '/'))
  );

  if (filtered.length > 0) return filtered;

  // Маркера нет вообще. Разворачиваем одиночную обёртку — самый частый случай
  // «архив из одной папки, внутри которой всё» — и ставим как есть.
  const topLevel = new Set(entries.map((e) => e.split('/')[0]));
  const singleWrapper =
    topLevel.size === 1 && entries.every((e) => e.includes('/')) ? [...topLevel][0] : '';

  return [
    {
      prefix: singleWrapper,
      marker: null,
      name: singleWrapper || path.parse(zipPath).name,
    },
  ];
}

/**
 * Распаковывает одну папку мода из архива в целевую директорию.
 *
 * @param {string} zipPath
 * @param {ModRoot} root
 * @param {string} destDir куда положить содержимое (сама папка мода)
 * @returns {string[]} относительные пути распакованных файлов
 */
function extractModRoot(zipPath, root, destDir) {
  const zip = new AdmZip(zipPath);
  const prefix = root.prefix === '' ? '' : root.prefix + '/';
  const written = [];

  fs.mkdirSync(destDir, { recursive: true });

  for (const entry of zip.getEntries()) {
    if (entry.isDirectory) continue;
    const name = normalise(entry.entryName);
    if (JUNK.test(name)) continue;
    if (prefix && !name.startsWith(prefix)) continue;

    const relative = prefix ? name.slice(prefix.length) : name;
    if (relative === '') continue;

    // Защита от архивов с путями вида ../../windows/system32
    const target = path.resolve(destDir, relative);
    if (!target.startsWith(path.resolve(destDir) + path.sep) && target !== path.resolve(destDir)) {
      continue;
    }

    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, entry.getData());
    written.push(relative);
  }

  return written;
}

/**
 * Распаковывает архив целиком — для загрузчиков вроде BepInEx,
 * которые кладутся в корень игры как есть.
 *
 * @param {string} zipPath
 * @param {string} destDir
 * @param {string} [stripPrefix] папка внутри архива, которую надо развернуть
 */
function extractAll(zipPath, destDir, stripPrefix = '') {
  const zip = new AdmZip(zipPath);
  const prefix = stripPrefix ? normalise(stripPrefix).replace(/\/?$/, '/') : '';
  const written = [];

  for (const entry of zip.getEntries()) {
    if (entry.isDirectory) continue;
    const name = normalise(entry.entryName);
    if (JUNK.test(name)) continue;
    if (prefix && !name.startsWith(prefix)) continue;

    const relative = prefix ? name.slice(prefix.length) : name;
    if (relative === '') continue;

    const target = path.resolve(destDir, relative);
    if (!target.startsWith(path.resolve(destDir) + path.sep)) continue;

    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, entry.getData());
    written.push(relative);
  }

  return written;
}

/** Читает один текстовый файл из архива, не распаковывая остальное. */
function readEntryText(zipPath, entryName) {
  const zip = new AdmZip(zipPath);
  const wanted = normalise(entryName).toLowerCase();
  const entry = zip.getEntries().find((e) => normalise(e.entryName).toLowerCase() === wanted);
  if (!entry) return null;
  return entry.getData().toString('utf8').replace(/^﻿/, '');
}

/**
 * Приводит архив к zip.
 *
 * Внутри ModHub всё работает с zip, но на Nexus встречаются .7z и .rar.
 * В Windows 10 и 11 есть свой tar (libarchive), который читает оба формата,
 * — им и распаковываем во временную папку, а оттуда собираем zip.
 * Ничего стороннего в программу для этого не добавляется.
 */
function toZip(filePath) {
  if (/\.zip$/i.test(filePath)) return { path: filePath, temporary: false };
  if (!/\.(7z|rar)$/i.test(filePath)) return { path: filePath, temporary: false };

  const { execFileSync } = require('node:child_process');
  const os = require('node:os');
  const tar =
    process.platform === 'win32'
      ? path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'tar.exe')
      : 'bsdtar';
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'modhub-unpack-'));
  try {
    execFileSync(tar, ['-xf', filePath, '-C', work], { windowsHide: true, stdio: 'ignore', timeout: 120000 });
  } catch (error) {
    fs.rmSync(work, { recursive: true, force: true });
    const failure = new Error(`unpack: ${path.basename(filePath)}`);
    failure.code = 'ARCHIVE_FORMAT';
    failure.cause = error;
    throw failure;
  }
  const zip = new AdmZip();
  zip.addLocalFolder(work);
  const out = path.join(os.tmpdir(), `modhub-${Date.now()}-${path.basename(filePath).replace(/\.[^.]+$/, '')}.zip`);
  zip.writeZip(out);
  fs.rmSync(work, { recursive: true, force: true });
  return { path: out, temporary: true };
}

module.exports = { findModRoots, extractModRoot, extractAll, readEntryText, toZip };
