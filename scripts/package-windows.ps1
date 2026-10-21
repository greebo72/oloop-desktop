# Builds the Windows package.
#
#   scripts/package-windows.ps1 [-Version 0.1.0] [-Runtime win-x64]
#
# The result is dist/oloop-<version>-<runtime>.zip: the self-contained
# publish output, portable. No installer - the user unpacks the zip and
# runs oloop.exe.

param(
    [string] $Version = "",
    [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $csproj = Get-Content OloopApp.csproj -Raw
    $match = [regex]::Match($csproj, "<Version>([^<]*)</Version>")
    if (-not $match.Success) {
        throw "no <Version> in OloopApp.csproj"
    }
    $Version = $match.Groups[1].Value
}

$out = Join-Path $root "dist"
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("oloop-win-" + [guid]::NewGuid().ToString("N"))
$zip = Join-Path $out "oloop-$Version-$Runtime.zip"

Write-Host "==> Publishing oloop $Version for $Runtime (self-contained)"
New-Item -ItemType Directory -Force -Path $work | Out-Null
dotnet publish OloopApp.csproj `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $work | Out-Host

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
}

Write-Host "==> Zipping"
New-Item -ItemType Directory -Force -Path $out | Out-Null
if (Test-Path $zip) {
    Remove-Item $zip
}
Compress-Archive -Path (Join-Path $work "*") -DestinationPath $zip -CompressionLevel Optimal

Remove-Item -Recurse -Force $work

Write-Host "==> Done"
Write-Host "    $zip"
