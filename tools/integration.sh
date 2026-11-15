#!/usr/bin/env bash
# Интеграционные проверки ModLaunch на локальном эмуляторе Firebase (вход, деньги, правила базы).
# Боевой проект не трогается: эмулятор работает с проектом demo-modlaunch и правилами из firebase/.
# Нужны: Node.js 20+, Java 11+ (JAVA_HOME или java в PATH), собранный ModLaunch.exe.
# Запуск:  tools/integration.sh [ModLaunch.exe]  |  tools/integration.sh rules  |  tools/integration.sh server  |  tools/integration.sh e2e
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
mode="app"; case "${1:-}" in rules|server|e2e) mode="$1"; shift;; esac
exe="${1:-$root/desktop/ModLaunch/bin/Release/net8.0/win-x64/ModLaunch.exe}"
if [ -z "${JAVA_HOME:-}" ] && ! command -v java >/dev/null; then
  for j in "/c/Program Files/JetBrains/"*/jbr "/c/Program Files/Eclipse Adoptium/"* "/c/Program Files/Java/"*; do
    [ -x "$j/bin/java.exe" ] && export JAVA_HOME="$j" && break
  done
fi
[ -n "${JAVA_HOME:-}" ] && export PATH="$JAVA_HOME/bin:$PATH"
# Java не открывает внутренний канал, если во временной папке не-ASCII буквы (имя пользователя «Максим»):
# даём ей короткий путь 8.3.
if command -v cmd >/dev/null; then
  short="$(cmd //c 'for %I in ("%TEMP%") do @echo %~sI' | tr -d '\r')"
  mkdir -p "$short/ml-java" && export JAVA_TOOL_OPTIONS="-Djdk.net.unixdomain.tmpdir=$short/ml-java -Djava.io.tmpdir=$short/ml-java"
fi
cd "$root/firebase"
fb="${FIREBASE:-npx -y firebase-tools@15}"
if [ "$mode" = "rules" ]; then
  (cd test && npm ci --no-audit --no-fund >/dev/null)
  exec $fb emulators:exec --project demo-modlaunch --only firestore "cd test && npm test"
fi
# Сервер (Cloud Functions): логика магазина на эмуляторе базы и сквозной прогон самих функций.
if [ "$mode" = "server" ]; then
  (cd ../server/functions && npm ci --no-audit --no-fund >/dev/null)
  exec $fb emulators:exec --project demo-modlaunch --only firestore "cd ../server/functions && npm test"
fi
if [ "$mode" = "e2e" ]; then
  (cd ../server/functions && npm ci --no-audit --no-fund >/dev/null)
  [ -f ../server/functions/.secret.local ] || cp ../server/functions/test/e2e.secret.local ../server/functions/.secret.local
  export FUNCTIONS_DISCOVERY_TIMEOUT=120
  exec $fb emulators:exec --project demo-modlaunch --only auth,firestore,functions "node ../server/functions/test/e2e.mjs"
fi
export DOTNET_ROLL_FORWARD=Major MODLAUNCH_FIREBASE_EMULATOR=127.0.0.1 MODLAUNCH_FIREBASE_PROJECT=demo-modlaunch
exec $fb emulators:exec --project demo-modlaunch --only firestore,auth "\"$exe\" --integration"
