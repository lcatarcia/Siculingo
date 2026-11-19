[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [switch]$SkipNpmCi
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\publish'
}

$frontendDir = Join-Path $root 'frontend\siculingo-web'
$apiProject = Join-Path $root 'src\Siculingo.Api\Siculingo.Api.csproj'
$approvedPilot = Join-Path $root 'content\pilot-greetings.v1.json'
$appPublishDir = Join-Path $OutputDirectory 'app'
$zipPath = Join-Path $OutputDirectory 'siculingo-release.zip'

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $appPublishDir | Out-Null

Write-Host '1/4 Building and linting React/Vite PWA bundle...'
Push-Location $frontendDir
try {
    if (-not $SkipNpmCi) {
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    npm run lint
    if ($LASTEXITCODE -ne 0) { throw 'Frontend lint failed.' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally {
    Pop-Location
}

Write-Host '2/4 Publishing Siculingo.Api (Release, framework-dependent .NET 10)...'
dotnet publish $apiProject --configuration Release --output $appPublishDir /p:UseAppHost=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host '3/4 Assembling same-origin wwwroot and approved pilot content...'
$wwwrootDir = Join-Path $appPublishDir 'wwwroot'
New-Item -ItemType Directory -Force -Path $wwwrootDir | Out-Null
Copy-Item -Path (Join-Path $frontendDir 'dist\*') -Destination $wwwrootDir -Recurse -Force

if (Test-Path -LiteralPath $approvedPilot) {
    $contentOutDir = Join-Path $appPublishDir 'content'
    New-Item -ItemType Directory -Force -Path $contentOutDir | Out-Null
    Copy-Item -LiteralPath $approvedPilot -Destination (Join-Path $contentOutDir 'pilot-greetings.v1.json') -Force
}

Write-Host '4/4 Creating Azure App Service ZIP deployment package...'
Compress-Archive -Path (Join-Path $appPublishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force

$zipSizeMb = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)
Write-Host "Release package ready: $zipPath ($zipSizeMb MB)"
