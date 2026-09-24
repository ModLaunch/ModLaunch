'use strict';

/**
 * Мост для оверлея в игре (2.1). Окно оверлея работает в песочнице и видит
 * только эти несколько вызовов — не весь window.modhub главного окна.
 */

const { contextBridge, ipcRenderer } = require('electron');

const call = (channel, payload) => ipcRenderer.invoke(channel, payload);

contextBridge.exposeInMainWorld('overlay', {
  state: () => call('overlay:state'),
  hide: () => call('overlay:hide'),
  openApp: () => call('overlay:open-app'),
  backup: () => call('overlay:backup'),
  note: (text) => call('overlay:note', text),
  friends: () => call('overlay:friends'),
  onShow(fn) {
    const listener = () => fn();
    ipcRenderer.on('overlay:show', listener);
    return () => ipcRenderer.removeListener('overlay:show', listener);
  },
});
