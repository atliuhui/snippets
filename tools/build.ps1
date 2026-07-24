<#
.SYNOPSIS
  Publish Snippets artifacts into dist/.

.DESCRIPTION
  Runs `dotnet publish` for every publishable project under src/ and places the
  produced binaries directly into dist/<rid>/.

  Each dist/<rid>/ folder is cleared once at the start of the RID's publish so
  stale files never leak into the produced artifact.

.PARAMETER Runtime
  One or more .NET RIDs to publish for. Defaults to the current host RID
  (win-x64 / osx-arm64 / linux-x64, etc.).

.PARAMETER Configuration
  MSBuild configuration. Defaults to Release.

.PARAMETER NoSingleFile
  Disable single-file publish. The default is framework-dependent single-file;
  the target machine must have the matching .NET runtime installed.

.PARAMETER SelfContained
  Bundle the .NET runtime into the output. Defaults to false. Combine with the
  default single-file mode to produce one large standalone executable.

.EXAMPLE
  ./tools/build.ps1

.EXAMPLE
  ./tools/build.ps1 -Runtime win-x64,osx-arm64,linux-x64

.EXAMPLE
  ./tools/build.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [string[]] $Runtime,
    [string]   $Configuration = 'Release',
    [switch]   $NoSingleFile,
    [switch]   $SelfContained
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$srcRoot  = Join-Path $repoRoot 'src'
$distRoot = Join-Path $repoRoot 'dist'

function Resolve-HostRid {
    $info = & dotnet --info 2>$null
    foreach ($line in $info) {
        if ($line -match '^\s*RID:\s+(\S+)') { return $Matches[1] }
    }
    throw "Could not detect host RID from 'dotnet --info'."
}

if (-not $Runtime -or $Runtime.Count -eq 0) {
    $Runtime = @(Resolve-HostRid)
}

$projects = Get-ChildItem -Path $srcRoot -Recurse -Filter '*.csproj' | Where-Object {
    $xml = [xml](Get-Content $_.FullName)
    $outputType = $xml.Project.PropertyGroup.OutputType | Where-Object { $_ } | Select-Object -First 1
    $outputType -in 'Exe','WinExe'
}

if (-not $projects) {
    throw "No publishable projects found under $srcRoot."
}

Write-Host "Repo:           $repoRoot"
Write-Host "Configuration:  $Configuration"
Write-Host "Runtimes:       $($Runtime -join ', ')"
Write-Host "Projects:       $($projects.BaseName -join ', ')"
Write-Host "Single file:    $(-not $NoSingleFile)"
Write-Host "Self-contained: $([bool]$SelfContained)"
Write-Host ''

$publishProps = @()
$publishProps += "-p:SelfContained=$([bool]$SelfContained)".ToLower()
$publishProps += '-p:UseAppHost=true'
if (-not $NoSingleFile) {
    $publishProps += '-p:PublishSingleFile=true'
    $publishProps += '-p:IncludeNativeLibrariesForSelfExtract=true'
    $publishProps += '-p:DebugType=embedded'
    if ($SelfContained) {
        $publishProps += '-p:EnableCompressionInSingleFile=true'
    }
}

foreach ($rid in $Runtime) {
    $ridDir = Join-Path $distRoot $rid
    if (Test-Path $ridDir) {
        for ($i = 0; $i -lt 10; $i++) {
            Remove-Item -Recurse -Force $ridDir -ErrorAction SilentlyContinue
            if (-not (Test-Path $ridDir)) { break }
            Start-Sleep -Seconds 1
        }
        if (Test-Path $ridDir) {
            throw "Could not clear $ridDir - another process is holding a file there."
        }
    }
    New-Item -ItemType Directory -Path $ridDir -Force | Out-Null

    foreach ($project in $projects) {
        Write-Host "==> Publishing $($project.BaseName) for $rid -> $ridDir" -ForegroundColor Cyan
        & dotnet publish $project.FullName `
            -c $Configuration `
            -r $rid `
            -o $ridDir `
            --nologo `
            -v minimal `
            @publishProps
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $($project.BaseName) on $rid."
        }
    }

    if (-not $NoSingleFile) {
        Get-ChildItem -Path $ridDir -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }
}

Write-Host ''
Write-Host "Done. Artifacts in: $distRoot" -ForegroundColor Green
