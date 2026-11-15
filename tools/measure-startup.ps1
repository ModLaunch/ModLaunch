# Замер запуска ModLaunch: время до появления окна (холодный и тёплый старт) и память через 15 с.
# Данные — во временной чистой папке (как у нового пользователя). Запущенный замером процесс закрывается сам.
# Пример: powershell -ExecutionPolicy Bypass -File tools\measure-startup.ps1 -Exe out\ModLaunch.exe -Runs 3
param([Parameter(Mandatory = $true)][string]$Exe, [int]$Runs = 3)
$ErrorActionPreference = 'Stop'
$data = Join-Path $env:TEMP ("modlaunch-measure-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $data | Out-Null
$results = @()
for ($i = 1; $i -le $Runs; $i++) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['MODLAUNCH_DATA'] = $data
    $psi.EnvironmentVariables['DOTNET_ROLL_FORWARD'] = 'Major'
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    while ($p.MainWindowHandle -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep -Milliseconds 20; $p.Refresh() }
    $window = $sw.Elapsed.TotalSeconds
    Start-Sleep -Seconds 15
    $p.Refresh()
    $results += [pscustomobject]@{ Run = $i; Kind = $(if ($i -eq 1) { 'cold (first run)' } else { 'warm' }); WindowSec = [math]::Round($window, 2);
        WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB); PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB) }
    try { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(5000)) { $p.Kill() } } catch { }
    Start-Sleep -Seconds 2
}
$results | Format-Table -AutoSize
Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
$steam = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(steam|steamwebhelper)$' }
if ($steam) { "Steam сейчас: {0} процессов, {1:N0} МБ рабочего набора" -f $steam.Count, (($steam | Measure-Object WorkingSet64 -Sum).Sum / 1MB) }
