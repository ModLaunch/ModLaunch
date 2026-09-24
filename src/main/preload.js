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
    /** Какие моды обновились в каталоге и обновить один (1.10). */
    updates: (gameId) => invoke('mods:updates', gameId),
    update: (gameId, modId) => invoke('mods:update', { gameId, modId }),
    /** Картинки установленных модов: { id: адрес }. */
    media: (gameId) => invoke('mods:media', gameId),
  },

  /** Шейдеры: ReShade в папке игры (2.1). */
  /** Обновления самой программы из GitHub Releases (3.0). */
  update: {
    status: () => invoke('update:status'),
    check: () => invoke('update:check'),
    install: () => invoke('update:install'),
    onAvailable: (handler) => {
      const listener = (_event, payload) => handler(payload);
      ipcRenderer.on('app-update', listener);
      return () => ipcRenderer.removeListener('app-update', listener);
    },
    onProgress: (handler) => {
      const listener = (_event, payload) => handler(payload);
      ipcRenderer.on('app-update-progress', listener);
      return () => ipcRenderer.removeListener('app-update-progress', listener);
    },
  },
  /** Статистика для владельца: скачивания и оценки (3.0). */
  stats: {
    releases: () => invoke('stats:releases'),
    reviews: () => invoke('stats:reviews'),
  },
  /** Своя рамка окна: свернуть, развернуть, закрыть (3.0). */
  window: {
    control: (action) => invoke('window:control', action),
    state: () => invoke('window:state'),
    onState: (handler) => {
      const listener = (_event, payload) => handler(payload);
      ipcRenderer.on('window-state', listener);
      return () => ipcRenderer.removeListener('window-state', listener);
    },
  },
  /** Друзья: код друга, запросы, кто во что играет (2.1). */
  friends: {
    view: () => invoke('friends:view'),
    refresh: (force = false) => invoke('friends:refresh', { force }),
    code: () => invoke('friends:code'),
    add: (code) => invoke('friends:add', code),
    accept: (uid) => invoke('friends:accept', uid),
    remove: (uid) => invoke('friends:remove', uid),
  },
  /** Оверлей в игре: посмотреть, как он выглядит, без игры (2.1). */
  overlay: {
    preview: (gameId) => invoke('overlay:preview', gameId),
  },
  dxvk: {
    list: () => invoke('dxvk:list'),
    pick: () => invoke('dxvk:pick'),
    inspect: (exe) => invoke('dxvk:inspect', exe),
    install: (exe, api) => invoke('dxvk:install', { exe, api }),
    remove: (exe, forget = false) => invoke('dxvk:remove', { exe, forget }),
  },
  reshade: {
    status: (gameId) => invoke('reshade:status', gameId),
    install: (gameId) => invoke('reshade:install', gameId),
  },

  /** Профили модов: запомнить, что включено, и переключаться (1.10). */
  profiles: {
    list: (gameId) => invoke('profiles:list', gameId),
    save: (gameId, name) => invoke('profiles:save', { gameId, name }),
    apply: (gameId, name) => invoke('profiles:apply', { gameId, name }),
    rename: (gameId, from, to) => invoke('profiles:rename', { gameId, from, to }),
    remove: (gameId, name) => invoke('profiles:remove', { gameId, name }),
  },

  /** Сборка модов в файле: поделиться с другом и поставить чужую (1.10). */
  pack: {
    export: (gameId, name) => invoke('pack:export', { gameId, name }),
    import: () => invoke('pack:import'),
  },

  /** Резервные копии сохранений (1.10). */
  backups: {
    list: (gameId) => invoke('backups:list', gameId),
    create: (gameId) => invoke('backups:create', gameId),
    restore: (gameId, name) => invoke('backups:restore', { gameId, name }),
    remove: (gameId, name) => invoke('backups:remove', { gameId, name }),
    summary: () => invoke('backups:summary'),
  },

  /** Игровое время по играм (1.10). */
  playtime: {
    all: () => invoke('playtime:all'),
    reset: (gameId) => invoke('playtime:reset', gameId),
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
    /** Несколько модов по номерам: «Нужные моды» и наборы (2.0). */
    many: (gameId, ids) => invoke('catalog:many', { gameId, ids }),
    /** Что поставить до мода: его требования, которых ещё нет (2.1). */
    plan: (gameId, modId) => invoke('catalog:plan', { gameId, modId }),
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

  /** Игра, запущенная из ModHub, закрылась: { gameId, counted, ms, playtime }. */
  onGameExit: (handler) => {
    const listener = (_event, payload) => handler(payload);
    ipcRenderer.on('game-exit', listener);
    return () => ipcRenderer.removeListener('game-exit', listener);
  },

  /** Пришла ссылка nxm:// из браузера. */
  onNxmLink: (handler) => {
    const listener = (_event, payload) => handler(payload);
    ipcRenderer.on('nxm-link', listener);
    return () => ipcRenderer.removeListener('nxm-link', listener);
  },
});
