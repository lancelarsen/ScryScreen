[CmdletBinding()]
param(
    # Build configuration
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',

    # Target runtime identifier
    [ValidateSet('win-x64','win-x86','win-arm64')]
    [string]$Runtime = 'win-x64',

    # Where to place published output (defaults to <repo>/artifacts)
    [string]$OutputRoot = '',

    # Produce a single-file executable
    [bool]$SingleFile = $true,

    # Bundle the .NET runtime so recipients don't need it installed
    [bool]$SelfContained = $true,

    # Optional: make startup a bit faster (bigger output)
    [bool]$ReadyToRun = $true,

    # Also zip the publish folder for easy sharing
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot 'artifacts'
}

$project = Join-Path $PSScriptRoot 'ScryScreen.App\ScryScreen.App.csproj'
if (-not (Test-Path $project)) {
    throw "Could not find project at: $project"
}

$publishDir = Join-Path $OutputRoot (Join-Path 'publish' (Join-Path $Runtime $Configuration))
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Publishing ScryScreen..." -ForegroundColor Cyan
Write-Host "  Project:       $project"
Write-Host "  Configuration: $Configuration"
Write-Host "  Runtime:       $Runtime"
Write-Host "  Output:        $publishDir"
Write-Host "  SingleFile:    $SingleFile"
Write-Host "  SelfContained: $SelfContained"
Write-Host "  ReadyToRun:    $ReadyToRun"

$dotnetArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $publishDir,
    '--self-contained', $SelfContained.ToString().ToLowerInvariant(),
    "-p:PublishSingleFile=$($SingleFile.ToString().ToLowerInvariant())",
    "-p:PublishReadyToRun=$($ReadyToRun.ToString().ToLowerInvariant())"
)

& dotnet @dotnetArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Best guess at exe name (matches csproj name by default)
$exe = Join-Path $publishDir 'ScryScreen.App.exe'
if (Test-Path $exe) {
    Write-Host "Published EXE: $exe" -ForegroundColor Green
} else {
    Write-Host "Publish complete. (EXE not found at expected path: $exe)" -ForegroundColor Yellow
}

if ($Zip) {
    $zipPath = Join-Path $OutputRoot ("ScryScreen-$Runtime-$Configuration.zip")
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

    Write-Host "Creating zip: $zipPath" -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath)
    Write-Host "Zip created: $zipPath" -ForegroundColor Green
}
