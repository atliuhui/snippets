#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Publish Snippets artifacts into dist/.

.DESCRIPTION
  Runs `dotnet publish` for every publishable project under src/ and places the
  produced binaries directly into dist/<rid>/.

    For macOS RIDs, also creates dist/<rid>/Snippets.app so the app can be opened
    with Finder or the `open` command.

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

.PARAMETER NoRestore
    Pass --no-restore to dotnet publish. Use after restoring the solution in CI.

.PARAMETER Version
    Optional application version passed to MSBuild.

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
    [switch]   $SelfContained,
    [switch]   $NoRestore,
    [string]   $Version,
    [string]   $AssemblyVersion,
    [string]   $FileVersion,
    [string]   $InformationalVersion
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$srcRoot  = Join-Path $repoRoot 'src'
$distRoot = Join-Path $repoRoot 'dist'

function Initialize-DotNetCliHome {
    if ($env:DOTNET_CLI_HOME) { return }

    $dotnetHome = Join-Path ([System.IO.Path]::GetTempPath()) 'snippets-dotnet-cli-home'
    New-Item -ItemType Directory -Path $dotnetHome -Force | Out-Null
    $env:DOTNET_CLI_HOME = $dotnetHome
}

if (-not $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE) {
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
}

if (-not $env:DOTNET_CLI_TELEMETRY_OPTOUT) {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
}

Initialize-DotNetCliHome

function Resolve-HostRid {
    $info = & dotnet --info 2>$null
    foreach ($line in $info) {
        if ($line -match '^\s*RID:\s+(\S+)') { return $Matches[1] }
    }
    throw "Could not detect host RID from 'dotnet --info'."
}

function Escape-PlistValue {
    param([string] $Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function New-MacOSIconFile {
    param(
        [string[]] $IconSources,
        [string] $ResourcesDirectory
    )

    $sips = Get-Command sips -ErrorAction SilentlyContinue
    $iconutil = Get-Command iconutil -ErrorAction SilentlyContinue
    if (-not $sips -or -not $iconutil) {
        Write-Warning "macOS icon tools were not found; the app bundle will not include an icon."
        return $null
    }

    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "snippets-macos-icon-$([guid]::NewGuid())"
    $iconsetDir = Join-Path $workDir 'app.iconset'
    try {
        New-Item -ItemType Directory -Path $iconsetDir -Force | Out-Null

        $renderedIcon = Join-Path $workDir 'app.png'
        $iconSource = $IconSources | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iconSource) {
            return $null
        }

        & $sips.Source -s format png $iconSource --out $renderedIcon | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $renderedIcon)) {
            throw "Could not render $iconSource with sips."
        }

        $iconSizes = @(
            @{ Size = 16; Name = 'icon_16x16.png' },
            @{ Size = 32; Name = 'icon_16x16@2x.png' },
            @{ Size = 32; Name = 'icon_32x32.png' },
            @{ Size = 64; Name = 'icon_32x32@2x.png' },
            @{ Size = 128; Name = 'icon_128x128.png' },
            @{ Size = 256; Name = 'icon_128x128@2x.png' },
            @{ Size = 256; Name = 'icon_256x256.png' },
            @{ Size = 512; Name = 'icon_256x256@2x.png' },
            @{ Size = 512; Name = 'icon_512x512.png' },
            @{ Size = 1024; Name = 'icon_512x512@2x.png' }
        )

        foreach ($iconSize in $iconSizes) {
            $outputPath = Join-Path $iconsetDir $iconSize.Name
            & $sips.Source -s format png -z $iconSize.Size $iconSize.Size $renderedIcon --out $outputPath | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Could not resize app icon to $($iconSize.Size)x$($iconSize.Size)."
            }
        }

        New-Item -ItemType Directory -Path $ResourcesDirectory -Force | Out-Null
        $iconPath = Join-Path $ResourcesDirectory 'app.icns'
        & $iconutil.Source -c icns $iconsetDir -o $iconPath
        if ($LASTEXITCODE -ne 0) {
            throw "Could not create $iconPath."
        }

        return 'app.icns'
    }
    finally {
        if (Test-Path $workDir) {
            Remove-Item -Recurse -Force $workDir
        }
    }
}

function New-MacOSAppBundle {
    param(
        [System.IO.FileInfo] $Project,
        [string] $PublishDirectory
    )

    $executableName = $Project.BaseName
    $displayName = $executableName -replace '\.App$', ''
    $bundleName = "$displayName.app"
    $bundleRoot = Join-Path $PublishDirectory $bundleName
    $contentsDir = Join-Path $bundleRoot 'Contents'
    $macOSDir = Join-Path $contentsDir 'MacOS'
    $resourcesDir = Join-Path $contentsDir 'Resources'
    $stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "snippets-macos-bundle-$([guid]::NewGuid())"
    $iconSources = @(
        (Join-Path $Project.DirectoryName 'Assets/app.ico'),
        (Join-Path $Project.DirectoryName 'Assets/Icons/app.svg')
    )

    try {
        New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
        Get-ChildItem -LiteralPath $PublishDirectory -Force | Where-Object {
            -not ($_.PSIsContainer -and $_.Extension -ieq '.app')
        } | Copy-Item -Destination $stagingDir -Recurse -Force

        if (Test-Path $bundleRoot) {
            Remove-Item -Recurse -Force $bundleRoot
        }

        New-Item -ItemType Directory -Path $macOSDir -Force | Out-Null
        Get-ChildItem -LiteralPath $stagingDir -Force |
            Copy-Item -Destination $macOSDir -Recurse -Force

        $bundleExecutable = Join-Path $macOSDir $executableName
        if (-not (Test-Path $bundleExecutable)) {
            throw "Could not find macOS bundle executable at $bundleExecutable."
        }

        & chmod +x $bundleExecutable
        if ($LASTEXITCODE -ne 0) {
            throw "Could not mark $bundleExecutable as executable."
        }

        $iconFile = New-MacOSIconFile -IconSources $iconSources -ResourcesDirectory $resourcesDir
        $bundleIdentifier = "local.snippets.$($displayName.ToLowerInvariant())"
        $infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>$(Escape-PlistValue $executableName)</string>
    <key>CFBundleIdentifier</key>
    <string>$(Escape-PlistValue $bundleIdentifier)</string>
    <key>CFBundleName</key>
    <string>$(Escape-PlistValue $displayName)</string>
    <key>CFBundleDisplayName</key>
    <string>$(Escape-PlistValue $displayName)</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleIconFile</key>
    <string>$(Escape-PlistValue $iconFile)</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@
        Set-Content -LiteralPath (Join-Path $contentsDir 'Info.plist') -Value $infoPlist -Encoding utf8

        & codesign --force --deep --sign - $bundleRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Could not sign macOS app bundle at $bundleRoot."
        }

        & codesign --verify --deep --strict $bundleRoot
        if ($LASTEXITCODE -ne 0) {
            throw "macOS app bundle signature verification failed for $bundleRoot."
        }

        Write-Host "==> Created macOS app bundle: $bundleRoot" -ForegroundColor Cyan
    }
    finally {
        if (Test-Path $stagingDir) {
            Remove-Item -Recurse -Force $stagingDir
        }
    }
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
if ($Version) {
    $publishProps += "-p:Version=$Version"
}
if ($AssemblyVersion) {
    $publishProps += "-p:AssemblyVersion=$AssemblyVersion"
}
if ($FileVersion) {
    $publishProps += "-p:FileVersion=$FileVersion"
}
if ($InformationalVersion) {
    $publishProps += "-p:InformationalVersion=$InformationalVersion"
    $publishProps += '-p:IncludeSourceRevisionInInformationalVersion=false'
}
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
        $publishArgs = @(
            'publish',
            $project.FullName,
            '-c', $Configuration,
            '-r', $rid,
            '-o', $ridDir,
            '--nologo',
            '-v', 'minimal'
        )
        if ($NoRestore) {
            $publishArgs += '--no-restore'
        }
        $publishArgs += $publishProps
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $($project.BaseName) on $rid."
        }
    }

    if (-not $NoSingleFile) {
        Get-ChildItem -Path $ridDir -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }

    if ($rid -like 'osx-*') {
        foreach ($project in $projects) {
            New-MacOSAppBundle -Project $project -PublishDirectory $ridDir
        }
    }
}

Write-Host ''
Write-Host "Done. Artifacts in: $distRoot" -ForegroundColor Green
