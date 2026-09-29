Set-StrictMode -Version 2.0

function Get-FamStagingSqlSecretPath {
    $base = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($base)) {
        throw "LOCALAPPDATA is not available; persisted SQL secrets cannot be used."
    }

    return Join-Path (Join-Path $base "FAM-Pulverentnahme") "staging-sql-secrets.clixml"
}

function Get-PlainTextFromSecureString {
    param(
        [Parameter(Mandatory = $true)]
        [Security.SecureString]$SecureString,
        [string]$UserName = "fam-staging-secret"
    )

    $credential = New-Object System.Management.Automation.PSCredential($UserName, $SecureString)
    return $credential.GetNetworkCredential().Password
}

function Read-FamStagingSqlSecretStore {
    $path = Get-FamStagingSqlSecretPath
    $store = [ordered]@{
        Syncos = ""
        OxaionSql = ""
    }

    if (-not (Test-Path -LiteralPath $path)) {
        return $store
    }

    try {
        $data = Import-Clixml -LiteralPath $path
        if ($null -ne $data.Syncos) { $store.Syncos = [string]$data.Syncos }
        if ($null -ne $data.OxaionSql) { $store.OxaionSql = [string]$data.OxaionSql }
        return $store
    }
    catch {
        throw "Gespeicherte STAGING-SQL-Zugangsdaten konnten nicht gelesen werden. Mit -ResetStoredSqlConnections zuruecksetzen. Datei: $path. $($_.Exception.Message)"
    }
}

function Convert-StoredCipherToPlainText {
    param(
        [string]$CipherText,
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($CipherText)) {
        return ""
    }

    try {
        $secure = ConvertTo-SecureString -String $CipherText
        return Get-PlainTextFromSecureString -SecureString $secure -UserName $Name
    }
    catch {
        throw "Gespeicherter SQL-Connection-String '$Name' kann unter diesem Windows-Benutzer auf diesem Rechner nicht entschluesselt werden. Mit -ResetStoredSqlConnections neu speichern."
    }
}

function Set-FamStagingSqlSecretStore {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SyncosCipher,
        [Parameter(Mandatory = $true)]
        [string]$OxaionSqlCipher
    )

    $path = Get-FamStagingSqlSecretPath
    $directory = Split-Path -Parent $path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null

    [pscustomobject]@{
        Version = 1
        Syncos = $SyncosCipher
        OxaionSql = $OxaionSqlCipher
    } | Export-Clixml -LiteralPath $path -Force

    return $path
}

function Remove-FamStagingSqlSecretStore {
    $path = Get-FamStagingSqlSecretPath
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
    return $path
}

function Resolve-FamStagingSqlConnections {
    param(
        [switch]$ResetStoredSqlConnections
    )

    if ($ResetStoredSqlConnections) {
        $removed = Remove-FamStagingSqlSecretStore
        Write-Host "Gespeicherte STAGING-SQL-Verbindungen wurden zurueckgesetzt: $removed" -ForegroundColor Yellow
    }

    $store = Read-FamStagingSqlSecretStore
    $storeChanged = $false

    $syncosConnectionString = $env:Syncos__ConnectionString
    if ([string]::IsNullOrWhiteSpace($syncosConnectionString)) {
        if (-not [string]::IsNullOrWhiteSpace($store.Syncos)) {
            $syncosConnectionString = Convert-StoredCipherToPlainText -CipherText $store.Syncos -Name "syncos"
        }
        else {
            Write-Host ""
            Write-Host "Syncos STAGING wird fuer NFC und den Passwort-Fallback benoetigt." -ForegroundColor Cyan
            Write-Host "Einmalige Eingabe: Der Wert wird danach per Windows-DPAPI fuer diesen Windows-Benutzer auf diesem Rechner verschluesselt gespeichert." -ForegroundColor DarkGray
            $secureSyncos = Read-Host "SYNCOS STAGING SQL connection string" -AsSecureString
            $syncosConnectionString = Get-PlainTextFromSecureString -SecureString $secureSyncos -UserName "syncos"
            $store.Syncos = ConvertFrom-SecureString -SecureString $secureSyncos
            $storeChanged = $true
        }
    }

    $oxaionSqlConnectionString = $env:OxaionSql__ConnectionString
    if ([string]::IsNullOrWhiteSpace($oxaionSqlConnectionString)) {
        if (-not [string]::IsNullOrWhiteSpace($store.OxaionSql)) {
            $oxaionSqlConnectionString = Convert-StoredCipherToPlainText -CipherText $store.OxaionSql -Name "oxaion-sql"
        }
        else {
            Write-Host ""
            Write-Host "Fuer die rein lesenden Oxaion-SQL-Abfragen wird die Verbindung zur richtigen Oxaion STAGING Datenbank benoetigt." -ForegroundColor Cyan
            Write-Host "Einmalige Eingabe: Der Wert wird danach per Windows-DPAPI fuer diesen Windows-Benutzer auf diesem Rechner verschluesselt gespeichert." -ForegroundColor DarkGray
            $secureOxaionSql = Read-Host "OXAION STAGING SQL connection string (read-only)" -AsSecureString
            $oxaionSqlConnectionString = Get-PlainTextFromSecureString -SecureString $secureOxaionSql -UserName "oxaion-sql"
            $store.OxaionSql = ConvertFrom-SecureString -SecureString $secureOxaionSql
            $storeChanged = $true
        }
    }

    if ([string]::IsNullOrWhiteSpace($syncosConnectionString)) {
        throw "Syncos SQL connection string must not be empty for personnel authentication."
    }
    if ([string]::IsNullOrWhiteSpace($oxaionSqlConnectionString)) {
        throw "Oxaion SQL connection string must not be empty for the read-only inventory and target-location lookups."
    }

    if ($storeChanged) {
        $path = Set-FamStagingSqlSecretStore -SyncosCipher $store.Syncos -OxaionSqlCipher $store.OxaionSql
        Write-Host "STAGING-SQL-Verbindungen verschluesselt gespeichert: $path" -ForegroundColor DarkGray
    }

    return [pscustomobject]@{
        SyncosConnectionString = $syncosConnectionString
        OxaionSqlConnectionString = $oxaionSqlConnectionString
        StoredSecretPath = Get-FamStagingSqlSecretPath
    }
}
