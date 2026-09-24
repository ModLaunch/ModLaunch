'use strict';

const fs = require('node:fs');
const path = require('node:path');

/**
 * Простое хранилище состояния на JSON-файлах.
 *
 * Сознательно без базы данных: всё состояние ModHub — это список установленных
 * модов и пути к играм. Файл, который пользователь может открыть и прочитать,
 * здесь ценнее, чем индексы, а запись через временный файл защищает от
 * повреждения при выключении питания на середине.
 */
class JsonStore {
  /**
   * @param {string} filePath
   * @param {object} defaults
   */
  constructor(filePath, defaults = {}) {
    this.filePath = filePath;
    this.defaults = defaults;
    this.data = this._read();
  }

  _read() {
    try {
      const raw = fs.readFileSync(this.filePath, 'utf8');
      return { ...structuredClone(this.defaults), ...JSON.parse(raw) };
    } catch {
      return structuredClone(this.defaults);
    }
  }

  save() {
    fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
    const tmp = this.filePath + '.tmp';
    fs.writeFileSync(tmp, JSON.stringify(this.data, null, 2), 'utf8');
    fs.renameSync(tmp, this.filePath);
  }

  get(key, fallback = undefined) {
    return key in this.data ? this.data[key] : fallback;
  }

  set(key, value) {
    this.data[key] = value;
    this.save();
    return value;
  }
}

module.exports = { JsonStore };
