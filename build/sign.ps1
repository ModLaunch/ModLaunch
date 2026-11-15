# Подпись ModLaunch.exe (он же установщик ModLaunch-Setup-x.y.z.exe — это один и тот же файл).
# Ничего не настроено — выходит с кодом 0 и предупреждением: сборка остаётся неподписанной.
#
# Способы (выбираются переменной окружения SIGN_MODE или файлом build\signing.json с теми же полями):
#   thumbprint — сертификат уже в хранилище Windows или на USB-токене (OV/EV от SSL.com, Certum и т. п.):
#                SIGN_THUMBPRINT=<отпечаток SHA-1 сертификата>
#   azure      — Azure Artifact Signing (бывш. Trusted Signing):
#                SIGN_AZURE_ENDPOINT=https://weu.codesigning.azure.net  SIGN_AZURE_ACCOUNT=<аккаунт>
#                SIGN_AZURE_PROFILE=<профиль сертификата>  SIGN_AZURE_DLIB=<путь к Azure.CodeSigning.Dlib.dll>
#                (вход в Azure — переменные AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET или az login)
#   esigner    — облачная подпись SSL.com eSigner (CodeSignTool):
#                SIGN_ESIGNER_TOOL=<путь к CodeSignTool.bat>  SIGN_ESIGNER_USER / SIGN_ESIGNER_PASSWORD /
#                SIGN_ESIGNER_CREDENTIAL_ID / SIGN_ESIGNER_TOTP
# Секреты НЕ хранятся в репозитории: только переменные окружения (на CI — секреты GitHub Actions).
param([Parameter(Mandatory = $true)][string]$File)
$ErrorActionPreference = 'Stop'

function Setting([string]$name) {
    $v = [Environment]::GetEnvironmentVariable($name)
    if ($v) { return $v }
    $json = Join-Path $PSScriptRoot 'signing.json'
    if (Test-Path $json) {
        $cfg = Get-Content $json -Raw | ConvertFrom-Json
        $p = $cfg.PSObject.Properties[$name]
        if ($p) { return [string]$p.Value }
    }
    return $null
}

function Find-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kits) {
        $tool = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw 'signtool.exe не найден: установите Windows SDK (компонент "Signing Tools for Desktop Apps").'
}

$mode = Setting 'SIGN_MODE'
if (-not $mode) {
    Write-Warning "Подпись не настроена (SIGN_MODE) — $File остаётся без подписи. См. docs\release\CODE-SIGNING.md"
    exit 0
}
if (-not (Test-Path $File)) { throw "Нет файла $File" }
$timestamp = 'http://timestamp.digicert.com'
if (Setting 'SIGN_TIMESTAMP') { $timestamp = Setting 'SIGN_TIMESTAMP' }

switch ($mode) {
    'thumbprint' {
        $thumb = Setting 'SIGN_THUMBPRINT'
        if (-not $thumb) { throw 'SIGN_THUMBPRINT не задан' }
        & (Find-SignTool) sign /sha1 $thumb /fd SHA256 /tr $timestamp /td SHA256 /d 'ModLaunch' /du 'https://modlaunchapp.com' $File
    }
    'azure' {
        $dlib = Setting 'SIGN_AZURE_DLIB'
        if (-not $dlib -or -not (Test-Path $dlib)) { throw 'SIGN_AZURE_DLIB: путь к Azure.CodeSigning.Dlib.dll (NuGet Microsoft.Trusted.Signing.Client)' }
        $meta = Join-Path $env:TEMP 'modlaunch-artifact-signing.json'
        @{ Endpoint = (Setting 'SIGN_AZURE_ENDPOINT'); CodeSigningAccountName = (Setting 'SIGN_AZURE_ACCOUNT'); CertificateProfileName = (Setting 'SIGN_AZURE_PROFILE') } |
            ConvertTo-Json | Set-Content $meta -Encoding utf8
        & (Find-SignTool) sign /v /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /dlib $dlib /dmdf $meta $File
    }
    'esigner' {
        $tool = Setting 'SIGN_ESIGNER_TOOL'
        if (-not $tool -or -not (Test-Path $tool)) { throw 'SIGN_ESIGNER_TOOL: путь к CodeSignTool.bat (SSL.com)' }
        & $tool sign -username="$(Setting 'SIGN_ESIGNER_USER')" -password="$(Setting 'SIGN_ESIGNER_PASSWORD')" `
            -credential_id="$(Setting 'SIGN_ESIGNER_CREDENTIAL_ID')" -totp_secret="$(Setting 'SIGN_ESIGNER_TOTP')" `
            -input_file_path="$File" -override="true"
    }
    default { throw "Неизвестный SIGN_MODE: $mode (thumbprint | azure | esigner)" }
}
if ($LASTEXITCODE -ne 0) { throw "Подпись не удалась (код $LASTEXITCODE)" }

$sig = Get-AuthenticodeSignature $File
if ($sig.Status -ne 'Valid') { throw "После подписи статус: $($sig.Status) — $($sig.StatusMessage)" }
Write-Host "Подписано: $($sig.SignerCertificate.Subject)"
