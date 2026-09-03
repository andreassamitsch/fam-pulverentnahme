[CmdletBinding()]
param(
    [int]$Port = 5080
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

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

$syncosConnectionString = $env:Syncos__ConnectionString
if ([string]::IsNullOrWhiteSpace($syncosConnectionString)) {
    Write-Host ""
    Write-Host "Syncos STAGING wird fuer NFC und den Passwort-Fallback benoetigt." -ForegroundColor Cyan
    $secureSyncos = Read-Host "SYNCOS STAGING SQL connection string" -AsSecureString
    $syncosCredential = New-Object System.Management.Automation.PSCredential("syncos", $secureSyncos)
    $syncosConnectionString = $syncosCredential.GetNetworkCredential().Password
}
if ([string]::IsNullOrWhiteSpace($syncosConnectionString)) {
    throw "Syncos SQL connection string must not be empty for personnel authentication."
}

try {
    $env:Oxaion__User = $oxaionUser
    $env:Oxaion__Password = $plainPassword
    $env:Syncos__ConnectionString = $syncosConnectionString
    $env:PersonnelAuthentication__ConnectionString = $syncosConnectionString

    Write-Host ""
    Write-Host "FAM Pulverentnahme STAGING" -ForegroundColor Cyan
    Write-Host "Oxaion: http://oxapp.cnc-domain.fuchshofer:11118 / Firma 103 / User $oxaionUser" -ForegroundColor DarkGray
    Write-Host "Syncos RFID + Passwortpruefung: konfiguriert" -ForegroundColor DarkGray
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
    $plainPassword = $null
    $syncosConnectionString = $null
    $oxaionUser = $null
}
