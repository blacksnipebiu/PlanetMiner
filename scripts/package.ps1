<#
.SYNOPSIS
    Builds the Thunderstore package for PlanetMiner.

.DESCRIPTION
    Lays out the archive like this (the DLL sits at the root):

        manifest.json          (version_number replaced with -Version)
        icon.png
        README.md
        PlanetMiner.dll

    Add -GameLayout to get BepInEx/plugins/PlanetMiner/PlanetMiner.dll instead, which is the
    layout Thunderstore expects.

    Run it through package.cmd when the PowerShell execution policy blocks scripts:

        scripts\package.cmd
        scripts\package.cmd -Dll PlanetMiner\bin\Release\PlanetMiner.dll -Version 3.1.9

    Without -Dll it builds the plugin (Release) first, so the zip always matches the sources.

.PARAMETER Dll
    Use this DLL instead of building. Skips the build.

.PARAMETER Version
    Version written into manifest.json. Defaults to the [BepInPlugin] version in PlanetMiner.cs.

.PARAMETER Out
    Destination zip. Defaults to artifacts/PlanetMiner-<version>.zip.

.PARAMETER Meta
    Directory holding manifest.json, icon.png and README.md. Defaults to Meta.

.PARAMETER SkipBuild
    Do not build; package the existing bin/Release (or bin/Debug) output.

.PARAMETER GameLayout
    Put the DLL under BepInEx/plugins/PlanetMiner/ instead of the archive root. Thunderstore
    wants this layout; the default flat archive is meant for unpacking by hand.
#>
[CmdletBinding()]
param(
    [string]$Dll,
    [string]$Version,
    [string]$Out,
    [string]$Meta,
    [switch]$SkipBuild,
    [switch]$GameLayout
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "PlanetMiner/PlanetMiner.csproj"
$releaseDll = Join-Path $root "PlanetMiner/bin/Release/PlanetMiner.dll"
$debugDll = Join-Path $root "PlanetMiner/bin/Debug/PlanetMiner.dll"

function Invoke-ReleaseBuild {
    Write-Host "building $project (Release)"

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        & $dotnet.Source build $project -c Release --nologo -v minimal -p:DeployAfterBuild=false
        if ($LASTEXITCODE -ne 0) { throw "build failed (exit code $LASTEXITCODE)" }
        return
    }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = $null
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
    if (-not $msbuild) { throw "neither dotnet nor MSBuild found; build in Visual Studio and rerun with -SkipBuild" }

    & $msbuild $project /nologo /v:minimal /p:Configuration=Release /p:DeployAfterBuild=false
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit code $LASTEXITCODE)" }
}

if (-not $Dll) {
    if (-not $SkipBuild) {
        Invoke-ReleaseBuild
    }

    if (Test-Path $releaseDll) {
        $Dll = $releaseDll
    }
    elseif (Test-Path $debugDll) {
        Write-Warning "Release build not found, packaging the Debug build: $debugDll"
        $Dll = $debugDll
    }
    else {
        throw "no PlanetMiner.dll found under PlanetMiner/bin; build the project first or pass -Dll"
    }
}

if (-not $Meta) { $Meta = Join-Path $root "Meta" }
if (-not $Version) {
    $source = Get-Content (Join-Path $root "PlanetMiner/PlanetMiner.cs") -Raw
    $match = [regex]::Match($source, 'BepInPlugin\(\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*"([^"]+)"\s*\)')
    if (-not $match.Success) { throw "BepInPlugin version not found in PlanetMiner.cs" }
    $Version = $match.Groups[1].Value
}
$Version = $Version.TrimStart("v")
if (-not $Out) { $Out = Join-Path $root "artifacts/PlanetMiner-$Version.zip" }

if (-not (Test-Path $Dll)) { throw "DLL not found: $Dll" }
foreach ($name in @("manifest.json", "icon.png", "README.md")) {
    $path = Join-Path $Meta $name
    if (-not (Test-Path $path)) { throw "Meta file not found: $path" }
}

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("PlanetMiner-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item (Join-Path $Meta "manifest.json") $stage
    Copy-Item (Join-Path $Meta "icon.png") $stage
    Copy-Item (Join-Path $Meta "README.md") $stage

    if ($GameLayout) {
        $pluginDir = Join-Path $stage "BepInEx/plugins/PlanetMiner"
        New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
        Copy-Item $Dll (Join-Path $pluginDir "PlanetMiner.dll")
    }
    else {
        Copy-Item $Dll (Join-Path $stage "PlanetMiner.dll")
    }

    # Only the version is patched, so the rest of the manifest keeps its formatting and text.
    $manifestPath = Join-Path $stage "manifest.json"
    $manifest = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
    $pattern = '"version_number"\s*:\s*"[^"]*"'
    if ($manifest -notmatch $pattern) { throw "manifest.json has no version_number field" }
    $manifest = [regex]::Replace($manifest, $pattern, '"version_number": "' + $Version + '"')
    [System.IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

    $outFull = [System.IO.Path]::GetFullPath($Out)
    $outDir = Split-Path -Parent $outFull
    if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    if (Test-Path $outFull) { Remove-Item $outFull -Force }

    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $outFull
    Write-Host "packaged $outFull ($Version)"
    Get-ChildItem -Recurse $stage -File | ForEach-Object { Write-Host ("  " + $_.FullName.Substring($stage.Length + 1)) }
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
