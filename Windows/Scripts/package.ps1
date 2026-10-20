<#
.SYNOPSIS
    Builds the Windows exe for x64 and arm64 and zips each for a GitHub release:
    dist\BitReaper-Windows-x64.zip, dist\BitReaper-Windows-arm64.zip
#>
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
foreach ($arch in 'x64', 'arm64') {
    & (Join-Path $PSScriptRoot 'build.ps1') -Arch $arch
    $zip = Join-Path $root "dist\BitReaper-Windows-$arch.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $root "dist\windows\$arch\BitReaper.exe") -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "$hash  $(Split-Path $zip -Leaf)"
}
