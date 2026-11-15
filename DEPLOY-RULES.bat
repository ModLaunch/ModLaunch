@echo off
chcp 65001 >nul
rem Выложить правила базы (firebase/firestore.rules) на боевой проект modhub-reviews.
rem Сначала гоняет все тесты правил на локальном эмуляторе; если хоть один упал — ничего не выкладывает.
rem Нужно один раз: Node.js и вход в Firebase (откроется браузер: firebase login).
setlocal
cd /d "%~dp0"
where node >nul 2>nul || (echo Нужен Node.js: https://nodejs.org & pause & exit /b 1)
echo [1/3] Вход в Firebase (если уже входили — пропустится)...
call npx -y firebase-tools@15 login || (echo Не удалось войти & pause & exit /b 1)
echo [2/3] Тесты правил на эмуляторе...
"%ProgramFiles%\Git\bin\bash.exe" tools/integration.sh rules || (echo ТЕСТЫ НЕ ПРОШЛИ — правила НЕ выложены & pause & exit /b 1)
echo [3/3] Выкладываю правила и индексы...
cd firebase
call npx -y firebase-tools@15 deploy --only firestore:rules,firestore:indexes --project modhub-reviews || (echo Выложить не удалось & pause & exit /b 1)
echo Готово: правила выложены.
pause
