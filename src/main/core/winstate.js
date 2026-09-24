'use strict';

const fs = require('node:fs');
const path = require('node:path');

/**
 * Размер и место окна между запусками.
 *
 * Первый запуск: окно под экран — на Full HD и шире сразу видна правая
 * панель (ей нужно от 1360 точек), на маленьком экране — привычные 1220.
 * Дальше ModHub открывается там и таким, каким его закрыли, в том числе
 * развёрнутым на весь экран. Если монитор с прошлого раза отключили,
 * окно не теряется за краем, а встаёт по центру основного экрана.
 */

const MIN_W = 940;
const MIN_H = 620;
const BASE_W = 1220;
const BASE_H = 800;
const MAX_FIRST_W = 1680;
const MAX_FIRST_H = 1050;

/**
 * Размер для первого запуска.
 * @param {{x:number,y:number,width:number,height:number}} area — рабочая область экрана
 */
function firstBounds(area) {
  // 1400 — чтобы и на ноутбуке с масштабом 125 % (1536 точек) панель была видна.
  let width = area.width >= 1500 ? Math.min(MAX_FIRST_W, Math.max(1400, Math.round(area.width * 0.86))) : BASE_W;
  let height = area.height >= 900 ? Math.min(MAX_FIRST_H, Math.round(area.height * 0.88)) : BASE_H;
  width = Math.min(Math.max(width, MIN_W), area.width);
  height = Math.min(Math.max(height, MIN_H), area.height);
  return {
    width,
    height,
    x: Math.round(area.x + (area.width - width) / 2),
    y: Math.round(area.y + (area.height - height) / 2),
  };
}

/** Видна ли заметная часть окна (заголовок) хотя бы на одном экране. */
function isVisible(bounds, areas) {
  return areas.some((area) => {
    const left = Math.max(bounds.x, area.x);
    const right = Math.min(bounds.x + bounds.width, area.x + area.width);
    const top = Math.max(bounds.y, area.y);
    const bottom = Math.min(bounds.y + 48, area.y + area.height);
    return right - left >= 120 && bottom - top >= 24;
  });
}

function isBounds(value) {
  return (
    value &&
    ['x', 'y', 'width', 'height'].every((key) => Number.isFinite(value[key])) &&
    value.width >= 200 &&
    value.height >= 200
  );
}

/**
 * Где открыть окно.
 * @param {object|null} saved — то, что сохранилось при закрытии
 * @param {Array<{x,y,width,height}>} areas — рабочие области всех экранов, первая — основной
 */
function restoreBounds(saved, areas) {
  const primary = areas[0] ?? { x: 0, y: 0, width: BASE_W, height: BASE_H };
  if (!saved || !isBounds(saved.bounds)) return { bounds: firstBounds(primary), maximized: false };

  const width = Math.max(MIN_W, Math.round(saved.bounds.width));
  const height = Math.max(MIN_H, Math.round(saved.bounds.height));
  let bounds = { x: Math.round(saved.bounds.x), y: Math.round(saved.bounds.y), width, height };
  if (!isVisible(bounds, areas)) {
    const w = Math.min(width, primary.width);
    const h = Math.min(height, primary.height);
    bounds = { width: w, height: h, x: Math.round(primary.x + (primary.width - w) / 2), y: Math.round(primary.y + (primary.height - h) / 2) };
  }
  return { bounds, maximized: Boolean(saved.maximized) };
}

function readState(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return null;
  }
}

function writeState(file, state) {
  try {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    const tmp = file + '.tmp';
    fs.writeFileSync(tmp, JSON.stringify(state), 'utf8');
    fs.renameSync(tmp, file);
  } catch {
    /* размер окна — не повод падать */
  }
}

/**
 * Следит за окном и сохраняет его размер при закрытии.
 * @param {import('electron').BrowserWindow} win
 * @param {string} file
 */
function track(win, file) {
  const save = () => {
    if (win.isDestroyed() || win.isMinimized()) return;
    writeState(file, { bounds: win.getNormalBounds(), maximized: win.isMaximized() });
  };
  win.on('close', save);
}

module.exports = { firstBounds, restoreBounds, isVisible, readState, writeState, track, MIN_W, MIN_H };
