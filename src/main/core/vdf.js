'use strict';

/**
 * Парсер формата Valve KeyValues (.vdf / .acf).
 *
 * Формат простой и стабильный: "ключ" "значение" либо "ключ" { вложенный блок }.
 * Собственной реализацией мы избегаем лишней зависимости ради ста строк кода,
 * а заодно контролируем поведение на битых файлах — Steam иногда оставляет
 * недописанный appmanifest после прерванной установки.
 */

/**
 * @param {string} text содержимое .vdf / .acf файла
 * @returns {object} дерево вида { ключ: значение | вложенный объект }
 */
function parseVdf(text) {
  const root = {};
  const stack = [root];
  let i = 0;
  const len = text.length;

  /** Читает строку в кавычках с поддержкой экранирования. */
  function readQuoted() {
    i++; // открывающая кавычка
    let out = '';
    while (i < len) {
      const ch = text[i];
      if (ch === '\\') {
        const next = text[i + 1];
        if (next === 'n') out += '\n';
        else if (next === 't') out += '\t';
        else out += next;
        i += 2;
        continue;
      }
      if (ch === '"') {
        i++;
        return out;
      }
      out += ch;
      i++;
    }
    return out;
  }

  /** Читает токен без кавычек (встречается в некоторых файлах Steam). */
  function readBare() {
    let out = '';
    while (i < len && !/[\s"{}]/.test(text[i])) {
      out += text[i];
      i++;
    }
    return out;
  }

  function skipTrivia() {
    while (i < len) {
      const ch = text[i];
      if (ch === '/' && text[i + 1] === '/') {
        while (i < len && text[i] !== '\n') i++;
        continue;
      }
      if (/\s/.test(ch)) {
        i++;
        continue;
      }
      return;
    }
  }

  let pendingKey = null;

  while (i < len) {
    skipTrivia();
    if (i >= len) break;

    const ch = text[i];

    if (ch === '}') {
      i++;
      if (stack.length > 1) stack.pop();
      pendingKey = null;
      continue;
    }

    if (ch === '{') {
      i++;
      const parent = stack[stack.length - 1];
      const key = pendingKey ?? '';
      const child = {};
      parent[key] = child;
      stack.push(child);
      pendingKey = null;
      continue;
    }

    const token = ch === '"' ? readQuoted() : readBare();
    if (token === '' && ch !== '"') {
      i++; // защита от зацикливания на неожиданном символе
      continue;
    }

    if (pendingKey === null) {
      pendingKey = token;
    } else {
      stack[stack.length - 1][pendingKey] = token;
      pendingKey = null;
    }
  }

  return root;
}

/**
 * Достаёт значение по пути, не падая на отсутствующих уровнях.
 * Сравнение ключей регистронезависимое: Steam пишет то "LibraryFolders",
 * то "libraryfolders" в разных версиях клиента.
 *
 * @param {object} obj
 * @param {string[]} path
 */
function getPath(obj, path) {
  let cur = obj;
  for (const segment of path) {
    if (!cur || typeof cur !== 'object') return undefined;
    if (segment in cur) {
      cur = cur[segment];
      continue;
    }
    const key = Object.keys(cur).find((k) => k.toLowerCase() === segment.toLowerCase());
    if (key === undefined) return undefined;
    cur = cur[key];
  }
  return cur;
}

module.exports = { parseVdf, getPath };
