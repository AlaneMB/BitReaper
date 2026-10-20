<#
.SYNOPSIS
    Builds BitReaper for Windows as one self-contained exe: dist\windows\<arch>\BitReaper.exe
.EXAMPLE
    Windows\Scripts\build.ps1                 # x64
    Windows\Scripts\build.ps1 -Arch arm64
#>
param(
    [ValidateSet('x64', 'arm64')] [string]$Arch = 'x64',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $root 'Windows\BitReaper\BitReaper.csproj'
$output = Join-Path $root "dist\windows\$Arch"

dotnet publish $project -c $Configuration -r "win-$Arch" --self-contained true -o $output `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:SatelliteResourceLanguages=en
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Write-Output "Built $(Join-Path $output 'BitReaper.exe')"
