<#
.SYNOPSIS
    Installs or updates everything needed to develop Woad on a Windows machine.

.DESCRIPTION
    Uses winget (built into Windows 10/11) to install Git, the .NET 10 SDK and the
    Godot .NET editor. The Godot version is read from Woad.csproj so every machine
    uses the same editor as the project. Safe to re-run: installed packages are
    upgraded instead of reinstalled.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\setup-dev.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$csproj = Join-Path $PSScriptRoot '..\Woad.csproj'
$sdkMatch = Select-String -Path $csproj -Pattern 'Godot\.NET\.Sdk/(\d+\.\d+\.\d+)'
if (-not $sdkMatch) {
    throw "Could not find the Godot.NET.Sdk version in $csproj."
}
$sdkVersion = $sdkMatch.Matches[0].Groups[1].Value
# winget lists x.y.0 releases as x.y (e.g. Godot 4.7.0 is "4.7").
$godotVersion = $sdkVersion -replace '\.0$', ''

$packages = @(
    @{ Id = 'Git.Git'; Name = 'Git'; Version = $null },
    @{ Id = 'Microsoft.DotNet.SDK.10'; Name = '.NET 10 SDK'; Version = $null },
    @{ Id = 'GodotEngine.GodotEngine.Mono'; Name = "Godot $godotVersion (.NET)"; Version = $godotVersion }
)

foreach ($package in $packages) {
    Write-Host "==> $($package.Name)" -ForegroundColor Cyan
    $arguments = @(
        'install', '--id', $package.Id, '--exact',
        '--accept-source-agreements', '--accept-package-agreements'
    )
    if ($package.Version) {
        $arguments += @('--version', $package.Version)
    }
    # winget upgrades a package that is already installed and returns a non-zero
    # exit code when there is nothing to do, so that is not treated as a failure.
    & winget @arguments
}

Write-Host ''
Write-Host 'Done. Open a new terminal so PATH changes apply, then from the repo root run:' -ForegroundColor Green
Write-Host '  dotnet test Woad.sln'
Write-Host "  Godot_v$godotVersion-stable_mono_win64 --editor --path ."
