[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,40}$')]
    [string]$InstanceName = 'SiculingoDev'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$apiDll = Join-Path $root 'src\Siculingo.Api\bin\Debug\net10.0\Siculingo.Api.dll'
$approvedPilot = Join-Path $root 'content\pilot-greetings.v1.json'

if (-not (Test-Path -LiteralPath $apiDll)) {
    dotnet build (Join-Path $root 'Siculingo.sln') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed before backup/restore drill.' }
}

Get-Command SqlLocalDB -ErrorAction Stop | Out-Null
& SqlLocalDB start $InstanceName | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Unable to start LocalDB instance $InstanceName." }

$drillId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$sourceDb = "Siculingo_DrillSrc_$drillId"
$restoredDb = "Siculingo_DrillRst_$drillId"
$drillDir = Join-Path ([System.IO.Path]::GetTempPath()) "siculingo-drill-$drillId"
New-Item -ItemType Directory -Force -Path $drillDir | Out-Null
$backupFile = Join-Path $drillDir "$sourceDb.bak"

function Invoke-MasterSql([string]$sql) {
    & sqlcmd -S "(localdb)\$InstanceName" -d master -E -b -V 16 -W -h -1 -Q $sql
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed executing: $sql" }
}

try {
    $srcMdf = Join-Path $drillDir "$sourceDb.mdf"
    $srcLdf = Join-Path $drillDir "${sourceDb}_log.ldf"
    Write-Host "1/5 Provisioning isolated source database $sourceDb..."
    Invoke-MasterSql "CREATE DATABASE [$sourceDb] ON PRIMARY (NAME = N'${sourceDb}_data', FILENAME = N'$srcMdf') LOG ON (NAME = N'${sourceDb}_log', FILENAME = N'$srcLdf');" | Out-Null

    $appBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $appBuilder['Data Source'] = "(localdb)\$InstanceName"
    $appBuilder['Initial Catalog'] = $sourceDb
    $appBuilder['Integrated Security'] = $true
    $appBuilder['Encrypt'] = $true
    $appBuilder['TrustServerCertificate'] = $true

    $prevConn = $env:ConnectionStrings__Database
    $prevEnv = $env:ASPNETCORE_ENVIRONMENT
    try {
        $env:ConnectionStrings__Database = $appBuilder.ConnectionString
        $env:ASPNETCORE_ENVIRONMENT = 'Production'

        Write-Host '2/5 Running controlled production CLI (--migrate-only & --seed-approved-pilot)...'
        dotnet $apiDll --migrate-only
        if ($LASTEXITCODE -ne 0) { throw '--migrate-only failed.' }

        dotnet $apiDll --seed-approved-pilot="$approvedPilot"
        if ($LASTEXITCODE -ne 0) { throw '--seed-approved-pilot failed.' }
    } finally {
        $env:ConnectionStrings__Database = $prevConn
        $env:ASPNETCORE_ENVIRONMENT = $prevEnv
    }

    Write-Host "3/5 Executing FULL SQL Server BACKUP to $backupFile..."
    Invoke-MasterSql "BACKUP DATABASE [$sourceDb] TO DISK = N'$backupFile' WITH FORMAT, INIT, CHECKSUM;" | Out-Null

    $rstMdf = Join-Path $drillDir "$restoredDb.mdf"
    $rstLdf = Join-Path $drillDir "${restoredDb}_log.ldf"
    Write-Host "4/5 Restoring backup into clean target database $restoredDb..."
    Invoke-MasterSql @"
RESTORE DATABASE [$restoredDb]
FROM DISK = N'$backupFile'
WITH MOVE N'${sourceDb}_data' TO N'$rstMdf',
     MOVE N'${sourceDb}_log' TO N'$rstLdf',
     CHECKSUM, RECOVERY;
"@ | Out-Null

    Write-Host '5/5 Running DBCC CHECKDB and verifying restored catalog integrity...'
    Invoke-MasterSql "DBCC CHECKDB ([$restoredDb]) WITH NO_INFOMSGS;" | Out-Null
    $countsLine = (Invoke-MasterSql "SET NOCOUNT ON; SELECT CONCAT((SELECT COUNT(*) FROM [$restoredDb].[dbo].[Lessons] WHERE [IsPublished] = 1), '|', (SELECT COUNT(*) FROM [$restoredDb].[dbo].[LevelThresholds]), '|', (SELECT COUNT(*) FROM [$restoredDb].[dbo].[Achievements]));" | Where-Object { $_ -match '^\d+\|\d+\|\d+$' } | Select-Object -First 1)
    if ($countsLine -ne '5|8|6') {
        throw "Unexpected restored counts (expected 5|8|6): '$countsLine'"
    }

    Write-Host "Backup/Restore drill PASSED: PublishedLessons=5, Levels=8, Achievements=6, DBCC CHECKDB clean."
} finally {
    foreach ($dbName in @($sourceDb, $restoredDb)) {
        try {
            & sqlcmd -S "(localdb)\$InstanceName" -d master -E -b -Q "IF DB_ID(N'$dbName') IS NOT NULL BEGIN ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]; END" | Out-Null
        } catch {}
    }
    if (Test-Path -LiteralPath $drillDir) {
        Remove-Item -LiteralPath $drillDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
