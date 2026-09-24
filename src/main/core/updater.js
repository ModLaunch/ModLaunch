'use strict';

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { compareVersions } = require('./updates');

/**
 * Автообновление ModLaunch (3.0).
 *
 * Новые версии лежат в GitHub Releases (адрес — src/main/update.config.json).
 * Программа спрашивает о последнем выпуске при запуске и раз в несколько
 * часов. Есть новее — в окне появляется предложение обновиться; по кнопке
 * скачивается установщик ModLaunch-Setup-<версия>.exe, его sha256 сверяется
 * с тем, что опубликовал GitHub, и установщик запускается с --update:
 * он закрывает программу, заменяет файлы и открывает её снова.
 *
 * Портативная копия (из zip) обновиться сама не может — ей показываем
 * ссылку на страницу выпуска.
 */

const API = 'https://api.github.com';
const TIMEOUT_MS = 15000;
const SETUP_RE = /^ModLaunch-Setup-[\d.]+\.exe$/i;

class Updater {
  /**
   * @param {object} options
   * @param {{owner?: string, repo?: string}} options.config
   * @param {string} options.version — текущая версия программы
   * @param {() => boolean} options.installed — стоит ли программа через установщик
   * @param {Function} options.download — core/download.downloadFile
   * @param {typeof fetch} [options.fetch]
   */
  constructor({ config, version, installed = () => false, download, fetch: fetchImpl } = {}) {
    this.owner = String(config?.owner ?? '').trim();
    this.repo = String(config?.repo ?? '').trim();
    this.version = version;
    this.installed = installed;
    this.download = download;
    this.fetch = fetchImpl ?? globalThis.fetch;
    this.latest = null;
    this.checkedAt = 0;
    this.file = null;
    this.busy = null;
  }

  get configured() {
    return Boolean(this.owner && this.repo);
  }

  async request(url) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
    try {
      const response = await this.fetch(url, {
        headers: { Accept: 'application/vnd.github+json', 'User-Agent': `ModLaunch/${this.version}` },
        signal: controller.signal,
      });
      if (!response.ok) throw Object.assign(new Error(`GitHub ${response.status}`), { status: response.status });
      return await response.json();
    } finally {
      clearTimeout(timer);
    }
  }

  /** Последний выпуск и есть ли он новее нашего. */
  async check() {
    if (!this.configured) return this.status();
    const data = await this.request(`${API}/repos/${encodeURIComponent(this.owner)}/${encodeURIComponent(this.repo)}/releases/latest`);
    const version = String(data?.tag_name ?? '').replace(/^v/i, '');
    const asset = (data?.assets ?? []).find((a) => SETUP_RE.test(a?.name ?? ''));
    const digest = /^sha256:([0-9a-f]{64})$/i.exec(String(asset?.digest ?? ''))?.[1] ?? null;
    this.latest = {
      version,
      name: data?.name ?? `ModLaunch ${version}`,
      notes: String(data?.body ?? '').slice(0, 6000),
      page: data?.html_url ?? null,
      publishedAt: data?.published_at ?? null,
      asset: asset ? { name: asset.name, url: asset.browser_download_url, size: asset.size, sha256: digest } : null,
    };
    this.checkedAt = Date.now();
    return this.status();
  }

  status() {
    const latest = this.latest;
    const available = Boolean(latest?.version && compareVersions(latest.version, this.version) > 0);
    return {
      configured: this.configured,
      current: this.version,
      available,
      // Сам обновиться может только установленный ModLaunch и только если есть установщик.
      canInstall: available && this.installed() && Boolean(latest?.asset) && process.platform === 'win32',
      latest,
      checkedAt: this.checkedAt ? new Date(this.checkedAt).toISOString() : null,
      downloaded: Boolean(this.file),
    };
  }

  /** Скачать установщик новой версии (с проверкой sha256, если GitHub её дал). */
  async fetchSetup(onProgress = () => {}) {
    if (this.busy) return this.busy;
    this.busy = (async () => {
      try {
        const status = this.status();
        if (!status.available || !this.latest?.asset) throw Object.assign(new Error('no update'), { code: 'UPDATE_NONE' });
        const { asset } = this.latest;
        const file = await this.download(asset.url, {
          fileName: asset.name,
          sha256: asset.sha256 ?? undefined,
          onProgress: (p) => onProgress({ ratio: p.ratio, received: p.received, total: p.total }),
        });
        const target = path.join(os.tmpdir(), asset.name);
        if (file !== target) {
          fs.copyFileSync(file, target);
          fs.rmSync(file, { force: true });
        }
        this.file = target;
        return target;
      } finally {
        this.busy = null;
      }
    })();
    return this.busy;
  }

  /**
   * Запустить установщик в режиме обновления. Программу после этого надо
   * закрыть: установщик заменит её файлы и откроет снова.
   */
  launchSetup() {
    if (!this.file || !fs.existsSync(this.file)) throw Object.assign(new Error('not downloaded'), { code: 'UPDATE_NONE' });
    const env = { ...process.env };
    for (const name of ['PORTABLE_EXECUTABLE_DIR', 'PORTABLE_EXECUTABLE_FILE', 'PORTABLE_EXECUTABLE_APP_FILENAME', 'ELECTRON_RUN_AS_NODE']) delete env[name];
    spawn(this.file, ['--update'], { detached: true, stdio: 'ignore', env, cwd: os.tmpdir() }).unref();
    return true;
  }
}

module.exports = { Updater, SETUP_RE };
