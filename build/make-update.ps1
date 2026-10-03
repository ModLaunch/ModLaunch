# ModLaunch: кладёт установщик и update.json в папку site (для своего хостинга).
# Запускается из MAKE-SETUP.bat: powershell -ExecutionPolicy Bypass -File build\make-update.ps1 -Version 8.4.1
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'out\ModLaunch.exe'
if (-not (Test-Path $exe)) { throw "Нет out\ModLaunch.exe — сначала сборка" }
$download = Join-Path $root 'site\download'
New-Item -ItemType Directory -Force -Path $download | Out-Null
$setupName = "ModLaunch-Setup-$Version.exe"
$setup = Join-Path $download $setupName
Copy-Item $exe $setup -Force
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()

# Текст «что нового» — только раздел этой версии из build\release-notes.md.
$notesFile = Join-Path $root 'build\release-notes.md'
$notes = ''
if (Test-Path $notesFile) {
  $all = [IO.File]::ReadAllText($notesFile, [Text.Encoding]::UTF8)
  $parts = $all -split "(?m)^# ModLaunch "
  foreach ($p in $parts) { if ($p.StartsWith($Version)) { $notes = ($p.Substring($Version.Length)).Trim(); break } }
}

$manifest = [ordered]@{
  version = $Version
  name    = "ModLaunch $Version"
  notes   = $notes
  page    = 'https://modlaunchapp.com/'
  url     = "https://modlaunchapp.com/download/$setupName"
  sha256  = $hash
  size    = (Get-Item $setup).Length
}
$json = $manifest | ConvertTo-Json -Depth 3
[IO.File]::WriteAllText((Join-Path $root 'site\update.json'), $json, (New-Object Text.UTF8Encoding $false))
Write-Host "site\update.json: $Version, sha256 $hash"
