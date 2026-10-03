# ModLaunch: выкладывает установщик на сайт modlaunchapp.com.
# Сайт берёт кнопку «Скачать» из последнего выпуска GitHub (ModLaunch/ModLaunch),
# поэтому выкладка = новый выпуск на GitHub с установщиком и портативным zip.
# Запускается из PUBLISH.bat. Журнал — build\publish.log.
param([string]$Version = '', [string]$Repo = 'ModLaunch/ModLaunch')
# Continue: в Windows PowerShell 5 вывод gh в stderr при Stop превращается в ошибку.
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$log = Join-Path $PSScriptRoot 'publish.log'
function Log($m) { $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $m; Write-Host $line; Add-Content -Path $log -Value $line -Encoding UTF8 }
Set-Content -Path $log -Value "" -Encoding UTF8

try {
  if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'desktop\ModLaunch\ModLaunch.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
  }
  Log "Версия: $Version, репозиторий: $Repo"

  # GitHub CLI: в PATH или в обычной папке установки.
  $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
  if (-not $gh) { foreach ($p in @("$env:ProgramFiles\GitHub CLI\gh.exe", "${env:ProgramFiles(x86)}\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe")) { if (Test-Path $p) { $gh = $p; break } } }
  if (-not $gh) { Log "NO_GH: GitHub CLI не установлен"; exit 2 }
  Log "gh: $gh"
  $auth = & $gh auth status 2>&1 | Out-String
  Log ("auth: " + ($auth -replace "`r?`n", ' | '))
  if ($LASTEXITCODE -ne 0) { Log "NO_AUTH: GitHub CLI не вошёл в аккаунт"; exit 3 }

  $setup = Join-Path $root "ModLaunch-Setup-$Version.exe"
  if (-not (Test-Path $setup)) { Log "NO_SETUP: нет $setup — сначала MAKE-SETUP.bat"; exit 4 }
  $zip = Join-Path $root "ModLaunch-$Version-win-x64.zip"
  $tmp = Join-Path $env:TEMP "modlaunch-zip-$Version"
  New-Item -ItemType Directory -Force -Path $tmp -ErrorAction Stop | Out-Null
  Copy-Item (Join-Path $root 'out\ModLaunch.exe') (Join-Path $tmp 'ModLaunch.exe') -Force -ErrorAction Stop
  Compress-Archive -Path (Join-Path $tmp 'ModLaunch.exe') -DestinationPath $zip -Force -ErrorAction Stop
  Log ("zip: {0:N1} МБ" -f ((Get-Item $zip).Length / 1MB))

  # Текст выпуска — раздел этой версии из build\release-notes.md.
  $all = [IO.File]::ReadAllText((Join-Path $root 'build\release-notes.md'), [Text.Encoding]::UTF8)
  $notes = ''
  foreach ($part in ($all -split "(?m)^# ModLaunch ")) { if ($part.StartsWith($Version)) { $notes = $part.Substring($Version.Length).Trim(); break } }
  $notesFile = Join-Path $env:TEMP "modlaunch-notes-$Version.md"
  [IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding $false))

  $exists = $false
  & $gh release view "v$Version" --repo $Repo *> $null
  if ($LASTEXITCODE -eq 0) { $exists = $true }
  if ($exists) {
    Log "Выпуск v$Version уже есть — заменяю файлы"
    & $gh release upload "v$Version" $setup $zip --repo $Repo --clobber 2>&1 | ForEach-Object { Log $_ }
    & $gh release edit "v$Version" --repo $Repo --notes-file $notesFile --latest 2>&1 | ForEach-Object { Log $_ }
  } else {
    Log "Создаю выпуск v$Version"
    & $gh release create "v$Version" $setup $zip --repo $Repo --title "ModLaunch $Version" --notes-file $notesFile --latest 2>&1 | ForEach-Object { Log $_ }
  }
  if ($LASTEXITCODE -ne 0) { Log "FAILED: gh вернул $LASTEXITCODE"; exit 5 }
  $assets = & $gh release view "v$Version" --repo $Repo --json url,assets --jq '.url + " | " + ([.assets[].name] | join(", "))' 2>&1
  Log "OK: $assets"
  exit 0
}
catch { Log ("ERROR: " + $_.Exception.Message); exit 1 }
