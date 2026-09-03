[CmdletBinding()]
param(
    [int]$Port = 5080
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$exe = Join-Path $PSScriptRoot "Fam.Pulverentnahme.Web.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Published backend not found: $exe"
}

$oxaionUser = (Read-Host "OXAION STAGING User").Trim()
if ([string]::IsNullOrWhiteSpace($oxaionUser)) {
    throw "Oxaion STAGING user must not be empty."
}

$securePassword = Read-Host "Oxaion STAGING password for $oxaionUser" -AsSecureString
$credential = New-Object System.Management.Automation.PSCredential($oxaionUser, $securePassword)
$plainPassword = $credential.GetNetworkCredential().Password

$syncosConnectionString = $env:Syncos__ConnectionString
if ([string]::IsNullOrWhiteSpace($syncosConnectionString)) {
    Write-Host ""
    Write-Host "Fuer NFC-Personalzuordnung wird eine SQL-Verbindung zu Syncos STAGING benoetigt." -ForegroundColor Cyan
    Write-Host "Bitte den vollstaendigen SQL-Connection-String eingeben/einfuegen. Die Eingabe wird nicht angezeigt." -ForegroundColor DarkGray
    $secureSyncos = Read-Host "SYNCOS STAGING SQL connection string" -AsSecureString
    $syncosCredential = New-Object System.Management.Automation.PSCredential("syncos", $secureSyncos)
    $syncosConnectionString = $syncosCredential.GetNetworkCredential().Password
}
if ([string]::IsNullOrWhiteSpace($syncosConnectionString)) {
    throw "Syncos SQL connection string must not be empty for NFC personnel lookup."
}

try {
    $env:Oxaion__User = $oxaionUser
    $env:Oxaion__Password = $plainPassword
    $env:Syncos__ConnectionString = $syncosConnectionString

    Write-Host ""
    Write-Host "FAM Pulverentnahme STAGING - self contained" -ForegroundColor Cyan
    Write-Host "Keine lokale .NET-Installation erforderlich." -ForegroundColor DarkGray
    Write-Host "Oxaion: http://oxapp.cnc-domain.fuchshofer:11118 / Firma 103 / User $oxaionUser" -ForegroundColor DarkGray
    Write-Host "Syncos RFID-Lookup: konfiguriert" -ForegroundColor DarkGray
    Write-Host "WebApp lokal: http://localhost:$Port" -ForegroundColor Green
    Write-Host "Android/PWA mit Web NFC: HTTPS ist erforderlich." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Beenden mit STRG+C." -ForegroundColor DarkGray
    Write-Host ""

    & $exe --urls "http://0.0.0.0:$Port"
}
finally {
    Remove-Item Env:Oxaion__User -ErrorAction SilentlyContinue
    Remove-Item Env:Oxaion__Password -ErrorAction SilentlyContinue
    Remove-Item Env:Syncos__ConnectionString -ErrorAction SilentlyContinue
    $plainPassword = $null
    $syncosConnectionString = $null
    $oxaionUser = $null
}
