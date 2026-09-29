[CmdletBinding()]
param(
    [int]$Port = 5080,
    [switch]$ResetStoredSqlConnections
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$sqlSecretHelper = Join-Path $PSScriptRoot "staging-sql-secrets.ps1"
if (-not (Test-Path -LiteralPath $sqlSecretHelper)) {
    throw "SQL secret helper not found: $sqlSecretHelper"
}
. $sqlSecretHelper

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Fam.Pulverentnahme.Web\Fam.Pulverentnahme.Web.csproj"

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found. Install the .NET 8 SDK first."
}

$oxaionUser = (Read-Host "OXAION STAGING User").Trim()
if ([string]::IsNullOrWhiteSpace($oxaionUser)) {
    throw "Oxaion STAGING user must not be empty."
}

$securePassword = Read-Host "Oxaion STAGING password for $oxaionUser" -AsSecureString
$credential = New-Object System.Management.Automation.PSCredential($oxaionUser, $securePassword)
$plainPassword = $credential.GetNetworkCredential().Password

$sqlConnections = Resolve-FamStagingSqlConnections -ResetStoredSqlConnections:$ResetStoredSqlConnections
$syncosConnectionString = $sqlConnections.SyncosConnectionString
$oxaionSqlConnectionString = $sqlConnections.OxaionSqlConnectionString

try {
    $env:Oxaion__User = $oxaionUser
    $env:Oxaion__Password = $plainPassword
    $env:Syncos__ConnectionString = $syncosConnectionString
    $env:PersonnelAuthentication__ConnectionString = $syncosConnectionString
    $env:OxaionSql__ConnectionString = $oxaionSqlConnectionString

    Write-Host ""
    Write-Host "FAM Pulverentnahme STAGING" -ForegroundColor Cyan
    Write-Host "Oxaion HTTP: http://oxapp.cnc-domain.fuchshofer:11118 / Firma 103 / User $oxaionUser" -ForegroundColor DarkGray
    Write-Host "Syncos RFID + Passwortpruefung: konfiguriert" -ForegroundColor DarkGray
    Write-Host "SQL-Verbindungen: verschluesselt gespeichert fuer diesen Windows-Benutzer (falls nicht per Umgebungsvariable vorgegeben)" -ForegroundColor DarkGray
    Write-Host "Oxaion SQL Lagerbestandsansicht: konfiguriert (read-only)" -ForegroundColor DarkGray
    Write-Host "WebApp: http://localhost:$Port" -ForegroundColor Green
    Write-Host "Android im selben Netz: http://<IP-DIESES-PCS>:$Port" -ForegroundColor Green
    Write-Host "Web NFC benoetigt HTTPS; manueller Passwort-Login kann in STAGING ueber HTTP getestet werden." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Beenden mit STRG+C." -ForegroundColor DarkGray
    Write-Host ""

    & dotnet run --project $project --urls "http://0.0.0.0:$Port"
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
