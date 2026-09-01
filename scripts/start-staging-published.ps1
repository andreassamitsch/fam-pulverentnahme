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

$oxaionUser = (Read-Host "Oxaion STAGING user").Trim()
if ([string]::IsNullOrWhiteSpace($oxaionUser)) {
    throw "Oxaion STAGING user must not be empty."
}

$securePassword = Read-Host "Oxaion STAGING password for $oxaionUser" -AsSecureString
$credential = New-Object System.Management.Automation.PSCredential($oxaionUser, $securePassword)
$plainPassword = $credential.GetNetworkCredential().Password

try {
    $env:Oxaion__User = $oxaionUser
    $env:Oxaion__Password = $plainPassword

    Write-Host ""
    Write-Host "FAM Pulverentnahme STAGING - self contained" -ForegroundColor Cyan
    Write-Host "Keine lokale .NET-Installation erforderlich." -ForegroundColor DarkGray
    Write-Host "Oxaion: http://oxapp.cnc-domain.fuchshofer:11118 / Firma 103 / User $oxaionUser" -ForegroundColor DarkGray
    Write-Host "WebApp: http://localhost:$Port" -ForegroundColor Green
    Write-Host "Android im selben Netz: http://<IP-DIESES-PCS>:$Port" -ForegroundColor Green
    Write-Host ""
    Write-Host "Beenden mit STRG+C." -ForegroundColor DarkGray
    Write-Host ""

    & $exe --urls "http://0.0.0.0:$Port"
}
finally {
    Remove-Item Env:Oxaion__User -ErrorAction SilentlyContinue
    Remove-Item Env:Oxaion__Password -ErrorAction SilentlyContinue
    $plainPassword = $null
    $oxaionUser = $null
}
