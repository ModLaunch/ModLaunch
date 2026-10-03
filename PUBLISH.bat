@echo off
chcp 65001 >nul
cd /d "%~dp0"
title ModLaunch - выкладка на сайт
echo.
echo  ==== ModLaunch: выкладываю установщик на сайт ====
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File build\publish-release.ps1
if errorlevel 1 (echo. & echo  Не получилось - подробности в build\publish.log. & pause & exit /b 1)
echo.
echo  Готово! Кнопка «Скачать» на modlaunchapp.com ведёт на новую версию.
timeout /t 6 /nobreak >nul
