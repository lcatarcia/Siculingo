[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,40}$')]
    [string]$InstanceName = 'SiculingoDev',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,80}$')]
    [string]$DatabaseName = 'Siculingo_Development',
    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'src\Siculingo.Api\Siculingo.Api.csproj'
$infrastructure = Join-Path $root 'src\Siculingo.Infrastructure\Siculingo.Infrastructure.csproj'
Get-Command SqlLocalDB -ErrorAction Stop | Out-Null
$instances = @(& SqlLocalDB info)
if ($LASTEXITCODE -ne 0) { throw 'Unable to list LocalDB instances.' }
if ($instances -notcontains $InstanceName) {
    & SqlLocalDB create $InstanceName -s
} else {
    & SqlLocalDB start $InstanceName
}
if ($LASTEXITCODE -ne 0) { throw 'Unable to start the dedicated LocalDB instance.' }

$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection.Add('Data Source', "(localdb)\$InstanceName")
$connection.Add('Initial Catalog', $DatabaseName)
$connection.Add('Integrated Security', $true)
$connection.Add('Encrypt', $true)
$connection.Add('TrustServerCertificate', $true)
$connection.Add('Connect Timeout', 5)
$connection.Add('ConnectRetryCount', 0)
$settings = @{ ConnectionStrings = @{ Database = $connection.ConnectionString } }
$settings | ConvertTo-Json | dotnet user-secrets set --project $api
if ($LASTEXITCODE -ne 0) { throw 'Unable to configure local user-secrets.' }

Push-Location $root
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousTestDatabase = $env:SICULINGO_TEST_DATABASE
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Unable to restore local .NET tools.' }
    dotnet restore Siculingo.sln
    if ($LASTEXITCODE -ne 0) { throw 'Unable to restore .NET packages.' }
    dotnet ef database update --project $infrastructure --startup-project $api
    if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' }
    $approvedPilot = Join-Path $root 'content\pilot-greetings.v1.json'
    if (Test-Path $approvedPilot) {
        dotnet run --project $api --launch-profile local -- --publish-pilot $approvedPilot
        if ($LASTEXITCODE -ne 0) { throw 'Approved pilot catalog publication failed.' }
    }
    if ($RunTests) {
        $env:SICULINGO_TEST_DATABASE = $connection.ConnectionString
        dotnet test Siculingo.sln --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
    }
} finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:SICULINGO_TEST_DATABASE = $previousTestDatabase
    Pop-Location
}
Write-Host "Local development database ready on dedicated instance $InstanceName."
