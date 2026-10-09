@echo off
chcp 65001 >nul
cd /d "%~dp0"
set VER=9.0.0
title ModLaunch %VER% - установщик
echo.
echo  ==== ModLaunch %VER%: собираю установщик ====
echo.
where dotnet >nul 2>nul || (echo  Не найден .NET SDK. Установите .NET 8 SDK и запустите снова. & pause & exit /b 1)

echo  [1/3] Сборка программы (пара минут)...
pushd desktop\ModLaunch
dotnet publish -c Release -r win-x64 -o ..\..\out
if errorlevel 1 (popd & echo. & echo  Сборка не удалась - пришлите этот текст Claude. & pause & exit /b 1)
popd

echo  [2/3] Установщик ModLaunch-Setup-%VER%.exe...
copy /y "out\ModLaunch.exe" "ModLaunch-Setup-%VER%.exe" >nul
if errorlevel 1 (echo  Не удалось записать установщик - закройте его, если он открыт, и запустите снова. & pause & exit /b 1)

echo  [3/3] Файлы для сайта: site\download и site\update.json...
powershell -NoProfile -ExecutionPolicy Bypass -File build\make-update.ps1 -Version %VER%

echo.
echo  Готово! Установщик лежит здесь:
echo  %~dp0ModLaunch-Setup-%VER%.exe
echo  Открываю его...
start "" "%~dp0ModLaunch-Setup-%VER%.exe"
timeout /t 4 /nobreak >nul
