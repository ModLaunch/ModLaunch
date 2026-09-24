'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('setup', {
  info: () => ipcRenderer.invoke('setup:info'),
  choose: (current) => ipcRenderer.invoke('setup:choose', current),
  install: (options) => ipcRenderer.invoke('setup:install', options),
  launch: () => ipcRenderer.invoke('setup:launch'),
  portable: () => ipcRenderer.invoke('setup:portable'),
  uninstall: (options) => ipcRenderer.invoke('setup:uninstall', options),
  license: () => ipcRenderer.invoke('setup:license'),
  window: (action) => ipcRenderer.invoke('setup:window', action),
  onProgress(callback) {
    const listener = (_event, payload) => callback(payload);
    ipcRenderer.on('setup:progress', listener);
    return () => ipcRenderer.off('setup:progress', listener);
  },
});
