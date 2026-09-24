'use strict';

const { contextBridge, ipcRenderer, webFrame } = require('electron');

/**
 * Мост между окном и основным процессом.
 *
 * Интерфейс не получает ни доступа к файловой системе, ни require:
 * наружу торчит только этот список именованных операций. Ровно поэтому
 * contextIsolation оставлен включённым, а nodeIntegration — выключенным.
 */

const invoke = (channel, payload) => ipcRenderer.invoke(channel, payload);

contextBridge.exposeInMainWorld('modhub', {
  app: {
    info: () => invoke('app:info'),
    openExternal: (url) => invoke('app:openExternal', url),
    openFolder: (path) => invoke('app:openFolder', path),
    pickFolder: () => invoke('app:pickFolder'),
    relaunchAsAdmin: () => invoke('app:relaunchAsAdmin'),
    pickArchive: () => invoke('app:pickArchive'),
    /** Запустить установщик загрузчика вручную, в обычном окне. */
    runManual: (target) => invoke('app:runManual', target),
    /** Очистить кэш картинок и сохранённые ленты. */
    clearCache: () => invoke('app:clearCache'),
    /**
     * Масштаб интерфейса из настроек. webFrame меняет масштаб честно —
     * вместе с размером окна в CSS-пикселях, поэтому вёрстка не ломается.
     */
    setZoom: (factor) => {
      const value = Number(factor);
      if (Number.isFinite(value) && value >= 0.75 && value <= 1.5) webFrame.setZoomFactor(value);
      return true;
    },
  },

  settings: {
    read: () => invoke('settings:read'),
    write: (patch) => invoke('settings:write', patch),
    checkNexusKey: (key) => invoke('settings:checkNexusKey', key),
  },

  games: {
    /** Мгновенный список: известные пути отдаются без обращения к дискам. */
    list: () => invoke('games:list'),
    supported: () => invoke('games:supported'),
    /** Поиск игры. deep — обойти диски вглубь, forget — забыть найденное раньше. */
    inspect: (gameId, options = {}) => invoke('games:inspect', { gameId, ...options }),
    rescan: (gameId, options = {}) =>
      invoke('games:inspect', { gameId, forget: true, ...options }),
    setPath: (gameId, path) => invoke('games:setPath', { gameId, path }),
    resetPath: (gameId) => invoke('games:setPath', { gameId, path: null }),
    installLoader: (gameId) => invoke('games:installLoader', gameId),
    launch: (gameId) => invoke('games:launch', gameId),
  },

  mods: {
    list: (gameId) => invoke('mods:list', gameId),
    setEnabled: (gameId, modId, enabled) => invoke('mods:setEnabled', { gameId, modId, enabled }),
    remove: (gameId, modId) => invoke('mods:remove', { gameId, modId }),
    installFromCatalog: (gameId, modId) => invoke('mods:installFromCatalog', { gameId, modId }),
    browserCancel: (gameId, modId) => invoke('mods:browserCancel', { gameId, modId }),
    browserFile: (gameId, modId, filePath) => invoke('mods:browserFile', { gameId, modId, filePath }),
    installFromFile: (gameId, filePath) => invoke('mods:installFromFile', { gameId, filePath }),
    problems: (gameId) => invoke('mods:problems', gameId),
    /** Картинки установленных модов: { id: адрес }. */
    media: (gameId) => invoke('mods:media', gameId),
  },

  /** Настоящие картинки игр из Steam: { [gameId]: { hero, logo, cover, header, shots } }. */
  media: {
    games: () => invoke('media:games'),
  },

  /** Реклама в шапке: { items, rotate, advertiseUrl }. */
  ads: {
    list: () => invoke('ads:list'),
  },

  catalog: {
    search: (gameId, query, options) => invoke('catalog:search', { gameId, query, options }),
    categories: (gameId) => invoke('catalog:categories', gameId),
    get: (gameId, modId) => invoke('catalog:get', { gameId, modId }),
    details: (gameId, modId) => invoke('catalog:details', { gameId, modId }),
    /** Досмотреть картинки модов, которых нет во встроенном указателе. */
    media: (gameId, ids) => invoke('catalog:media', { gameId, ids }),
  },

  /** Отзывы и оценки — общие для всех, у кого стоит ModHub. */
  account: {
    get: () => invoke('account:get'),
    refresh: () => invoke('account:refresh'),
    signUp: (payload) => invoke('account:signUp', payload),
    signIn: (payload) => invoke('account:signIn', payload),
    signOut: () => invoke('account:signOut'),
    rename: (name) => invoke('account:rename', { name }),
    verify: () => invoke('account:verify'),
    reset: (email) => invoke('account:reset', { email }),
    remove: (password) => invoke('account:delete', { password }),
  },
  reviews: {
    status: () => invoke('reviews:status'),
    sync: (options = {}) => invoke('reviews:sync', options),
    stats: () => invoke('reviews:stats'),
    list: (gameId, modId) => invoke('reviews:list', { gameId, modId }),
    /** Можно ли оценить мод: скачан ли он и играли ли с ним (1.9.7). */
    gate: (gameId, modId) => invoke('reviews:gate', { gameId, modId }),
    submit: (payload) => invoke('reviews:submit', payload),
    remove: (gameId, modId) => invoke('reviews:remove', { gameId, modId }),
    moderate: (gameId, modId, reviewId) => invoke('reviews:moderate', { gameId, modId, reviewId }),
  },

  nexus: {
    describe: (link) => invoke('nxm:describe', link),
    install: (gameId, link) => invoke('nxm:install', { gameId, link }),
  },

  diagnostics: {
    issues: (gameId) => invoke('diagnostics:issues', gameId),
    bisect: (gameId, payload) => invoke('diagnostics:bisect', { gameId, ...payload }),
  },

  /** Поток событий прогресса: установка, скачивание, установка загрузчика. */
  onProgress: (handler) => {
    const listener = (_event, payload) => handler(payload);
    ipcRenderer.on('progress', listener);
    return () => ipcRenderer.removeListener('progress', listener);
  },

  /** Права администратора запросили, но подтверждения так и не было. */
  onElevationCancelled: (handler) => {
    const listener = () => handler();
    ipcRenderer.on('elevation-cancelled', listener);
    return () => ipcRenderer.removeListener('elevation-cancelled', listener);
  },

  /** Пришла ссылка nxm:// из браузера. */
  onNxmLink: (handler) => {
    const listener = (_event, payload) => handler(payload);
    ipcRenderer.on('nxm-link', listener);
    return () => ipcRenderer.removeListener('nxm-link', listener);
  },
});
