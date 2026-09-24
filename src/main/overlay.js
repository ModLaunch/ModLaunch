'use strict';

const { BrowserWindow, globalShortcut, screen } = require('electron');

/**
 * Оверлей в игре (2.1): прозрачное окно поверх игры по сочетанию клавиш.
 *
 * Сочетание занято, только пока идёт игра, запущенная из ModHub, — в
 * остальное время оно свободно для других программ. Поверх игры окно
 * видно в оконном режиме и в режиме «без рамки» (так по умолчанию
 * запускаются Subnautica, Lethal Company, Stardew Valley и Hollow Knight);
 * поверх исключительного полноэкранного режима Windows не рисует ничьих окон.
 *
 * Окно одно и создаётся при первом показе. Ушёл фокус (Alt+Tab, щелчок
 * в игру) — оверлей прячется сам, как в Steam.
 */

const KEYS = ['CommandOrControl+Shift+M', 'Alt+`', 'Shift+F1'];
const DEFAULT_KEY = KEYS[0];

class Overlay {
  /**
   * @param {object} options
   * @param {string} options.page — overlay.html
   * @param {string} options.preload — overlay-preload.js
   * @param {string} [options.icon]
   */
  constructor({ page, preload, icon } = {}) {
    this.page = page;
    this.preload = preload;
    this.icon = icon;
    this.win = null;
    this.key = null;
  }

  static keyOf(value) {
    return KEYS.includes(value) ? value : DEFAULT_KEY;
  }

  /** Занять сочетание на время игры. Занято другой программой — false. */
  arm(accelerator) {
    this.disarm();
    const key = Overlay.keyOf(accelerator);
    try {
      if (globalShortcut.register(key, () => this.toggle())) {
        this.key = key;
        return true;
      }
    } catch (error) {
      console.error('[overlay]', error);
    }
    return false;
  }

  /** Отпустить сочетание и спрятать окно (игру закрыли, оверлей выключили). */
  disarm() {
    if (this.key) {
      try {
        globalShortcut.unregister(this.key);
      } catch {
        /* уже свободно */
      }
    }
    this.key = null;
    this.hide();
  }

  get armed() {
    return Boolean(this.key);
  }

  window() {
    if (this.win && !this.win.isDestroyed()) return this.win;
    const win = new BrowserWindow({
      show: false,
      frame: false,
      transparent: true,
      backgroundColor: '#00000000',
      resizable: false,
      movable: false,
      minimizable: false,
      maximizable: false,
      fullscreenable: false,
      skipTaskbar: true,
      hasShadow: false,
      alwaysOnTop: true,
      icon: this.icon,
      webPreferences: {
        preload: this.preload,
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: true,
        backgroundThrottling: false,
      },
    });
    // Уровень «заставки» — выше окон игр в режиме без рамки.
    win.setAlwaysOnTop(true, 'screen-saver');
    win.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
    win.on('blur', () => this.hide());
    win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    win.webContents.on('will-navigate', (event) => event.preventDefault());
    win.loadFile(this.page);
    this.win = win;
    return win;
  }

  get visible() {
    return Boolean(this.win && !this.win.isDestroyed() && this.win.isVisible());
  }

  toggle() {
    if (this.visible) this.hide();
    else this.show();
  }

  /** Показать на том экране, где сейчас мышь, — там же, где игра. */
  show() {
    const win = this.window();
    const display = screen.getDisplayNearestPoint(screen.getCursorScreenPoint());
    win.setBounds(display.bounds);
    const reveal = () => {
      if (win.isDestroyed()) return;
      win.webContents.send('overlay:show');
      win.show();
      win.focus();
    };
    if (win.webContents.isLoading()) win.webContents.once('did-finish-load', reveal);
    else reveal();
  }

  hide() {
    if (this.visible) this.win.hide();
  }

  destroy() {
    this.disarm();
    if (this.win && !this.win.isDestroyed()) this.win.destroy();
    this.win = null;
  }
}

module.exports = { Overlay, KEYS, DEFAULT_KEY };
