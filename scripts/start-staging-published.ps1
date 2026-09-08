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
    Write-Host "Fuer NFC und den manuellen Passwort-Fallback wird eine SQL-Verbindung zu Syncos STAGING benoetigt." -ForegroundColor Cyan
    Write-Host "Bitte den vollstaendigen SQL-Connection-String eingeben/einfuegen. Die Eingabe wird nicht angezeigt." -ForegroundColor DarkGray
    $secureSyncos = Read-Host "SYNCOS STAGING SQL connection string" -AsSecureString
    $syncosCredential = New-Object System.Management.Automation.PSCredential("syncos", $secureSyncos)
    $syncosConnectionString = $syncosCredential.GetNetworkCredential().Password
}
if ([string]::IsNullOrWhiteSpace($syncosConnectionString)) {
    throw "Syncos SQL connection string must not be empty for personnel authentication."
}

$oxaionSqlConnectionString = $env:OxaionSql__ConnectionString
if ([string]::IsNullOrWhiteSpace($oxaionSqlConnectionString)) {
    Write-Host ""
    Write-Host "Fuer die RP.* Lagerbestandsansicht und die rein lesende Ziel-Lagerort/Lagerplatz-Suche wird eine separate SQL-Verbindung zur richtigen Oxaion STAGING Datenbank benoetigt." -ForegroundColor Cyan
    Write-Host "Diese Verbindung wird nur fuer SELECT-Abfragen verwendet; Materialbuchungen laufen weiterhin ueber Oxaion HTTP/Fachlogik." -ForegroundColor DarkGray
    Write-Host "Bitte den vollstaendigen Oxaion SQL-Connection-String eingeben/einfuegen. Die Eingabe wird nicht angezeigt." -ForegroundColor DarkGray
    $secureOxaionSql = Read-Host "OXAION STAGING SQL connection string (read-only)" -AsSecureString
    $oxaionSqlCredential = New-Object System.Management.Automation.PSCredential("oxaion-sql", $secureOxaionSql)
    $oxaionSqlConnectionString = $oxaionSqlCredential.GetNetworkCredential().Password
}
if ([string]::IsNullOrWhiteSpace($oxaionSqlConnectionString)) {
    throw "Oxaion SQL connection string must not be empty for the read-only inventory and target-location lookups."
}

try {
    $env:Oxaion__User = $oxaionUser
    $env:Oxaion__Password = $plainPassword
    $env:Syncos__ConnectionString = $syncosConnectionString
    $env:PersonnelAuthentication__ConnectionString = $syncosConnectionString
    $env:OxaionSql__ConnectionString = $oxaionSqlConnectionString

    Write-Host ""
    Write-Host "FAM Pulverentnahme STAGING - self contained" -ForegroundColor Cyan
    Write-Host "Keine lokale .NET-Installation erforderlich." -ForegroundColor DarkGray
    Write-Host "Oxaion HTTP: http://oxapp.cnc-domain.fuchshofer:11118 / Firma 103 / User $oxaionUser" -ForegroundColor DarkGray
    Write-Host "Syncos RFID + Passwortpruefung: konfiguriert" -ForegroundColor DarkGray
    Write-Host "Oxaion SQL Bestand + Zielortsuche: konfiguriert (read-only)" -ForegroundColor DarkGray
    Write-Host "WebApp lokal: http://localhost:$Port" -ForegroundColor Green
    Write-Host "Android/PWA mit Web NFC: HTTPS ist erforderlich. Manueller Login kann in STAGING auch ueber HTTP getestet werden." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Beenden mit STRG+C." -ForegroundColor DarkGray
    Write-Host ""

    & $exe --urls "http://0.0.0.0:$Port"
}
finally {
    Remove-Item Env:Oxaion__User -ErrorAction SilentlyContinue
    Remove-Item Env:Oxaion__Password -ErrorAction SilentlyContinue
    Remove-Item Env:Syncos__ConnectionString -ErrorAction SilentlyContinue
    Remove-Item Env:PersonnelAuthentication__ConnectionString -ErrorAction SilentlyContinue
    Remove-Item Env:OxaionSql__ConnectionString -ErrorAction SilentlyContinue
    $plainPassword = $null
    $syncosConnectionString = $null
    $oxaionSqlConnectionString = $null
    $oxaionUser = $null
}
