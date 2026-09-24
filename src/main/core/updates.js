'use strict';

/**
 * Проверка обновлений установленных модов — как в CurseForge
 * и Thunderstore Mod Manager (1.10).
 *
 * Версии модов пишут как попало: «1.2.3», «v1.2», «1.2.3-beta», «2024.05».
 * Сравниваем по числам слева направо, а приставки и хвосты отбрасываем.
 * Когда в версиях нет чисел вовсе — считаем обновлением любое различие.
 */

function parts(version) {
  const clean = String(version ?? '').trim().replace(/^v(?=\d)/i, '');
  const main = clean.split(/[-+ ]/)[0];
  return main.split('.').map((p) => (/^\d+$/.test(p) ? Number(p) : NaN));
}

/** 1 — a новее b, -1 — старее, 0 — одинаковые или несравнимые. */
function compareVersions(a, b) {
  const x = parts(a);
  const y = parts(b);
  if (x.some(Number.isNaN) || y.some(Number.isNaN)) {
    return String(a ?? '').trim() === String(b ?? '').trim() ? 0 : 1;
  }
  for (let i = 0; i < Math.max(x.length, y.length); i += 1) {
    const d = (x[i] ?? 0) - (y[i] ?? 0);
    if (d !== 0) return d > 0 ? 1 : -1;
  }
  return 0;
}

/** Есть ли обновление: в каталоге версия новее установленной. */
function isNewer(latest, installed) {
  if (!latest) return false;
  if (!installed) return false;
  return compareVersions(latest, installed) > 0;
}

module.exports = { compareVersions, isNewer };
