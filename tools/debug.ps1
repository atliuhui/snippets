#requires -version 5.0
<#
.SYNOPSIS
    Start the Snippets GUI for local debugging.

.DESCRIPTION
    Locates the repository root and runs the Avalonia desktop app project.
    Any extra arguments after '--' are forwarded to the app.

.PARAMETER Configuration
    Build configuration passed to dotnet run. Defaults to Debug.

.PARAMETER NoBuild
    Pass --no-build to dotnet run. Use this after a successful build.

.EXAMPLE
    .\tools\debug.ps1

.EXAMPLE
    .\tools\debug.ps1 -NoBuild -- --some-app-argument
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoBuild,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppArgs = @()
)

$ErrorActionPreference = 'Stop'

function Fail($Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$projectPath = Join-Path $repoRoot 'src\Snippets.App\Snippets.App.csproj'

if (-not (Test-Path $projectPath)) {
    Fail "Could not find Snippets.App project at '$projectPath'."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "The dotnet CLI is required but was not found on PATH."
}

$dotnetArgs = @(
    'run',
    '--project', $projectPath,
    '--configuration', $Configuration
)

if ($NoBuild) {
    $dotnetArgs += '--no-build'
}

if ($AppArgs.Count -gt 0) {
    $dotnetArgs += '--'
    $dotnetArgs += $AppArgs
}

Write-Host "Starting Snippets GUI..." -ForegroundColor Cyan
Write-Host "Project: $projectPath" -ForegroundColor Gray

& dotnet @dotnetArgs
exit $LASTEXITCODE
