<#
.SYNOPSIS
    Builds Woad from this folder and plays it: what the desktop shortcut runs. No internet needed:
    it builds with the packages already restored, imports any new art, then opens the game. If the
    build fails it says so and plays the last good build.

.PARAMETER Godot
    Path to a Godot .NET executable. Defaults to $env:GODOT, then the version Woad.csproj pins (on
    PATH or in winget's package folder).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\play.ps1
#>
[CmdletBinding()]
param(
    [string]$Godot = $env:GODOT
)

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

if (-not $Godot) {
    $version = (Select-String -Path "$root\Woad.csproj" -Pattern 'Godot\.NET\.Sdk/(\d+\.\d+\.\d+)').Matches[0].Groups[1].Value -replace '\.0$', ''
    $pinned = "Godot_v$version-stable_mono_win64.exe"
    $wingetPackages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $Godot = @(
        (Get-Command $pinned -ErrorAction SilentlyContinue).Source
        if (Test-Path $wingetPackages) { (Get-ChildItem $wingetPackages -Recurse -Filter $pinned -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
    ) | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Godot) {
    Write-Host 'No Godot .NET executable found. Set $env:GODOT to it.' -ForegroundColor Red
    Read-Host 'Press Enter to close'
    exit 1
}

# The console build sits beside the editor: it imports headless without opening a window.
$console = $Godot -replace '\.exe$', '_console.exe'

Write-Host 'Building Woad...' -ForegroundColor Cyan
# --no-restore keeps it offline; a fresh clone without restored packages falls back to a normal build.
dotnet build Woad.csproj --no-restore -v quiet -nologo
if ($LASTEXITCODE -ne 0) {
    dotnet build Woad.csproj -v quiet -nologo
}
if ($LASTEXITCODE -ne 0) {
    Write-Host 'The build failed: playing the last good build.' -ForegroundColor Yellow
    Start-Sleep -Seconds 3
}

Write-Host 'Importing new art...' -ForegroundColor Cyan
if (Test-Path $console) {
    & $console --headless --path . --import *> $null
}

Start-Process $Godot -ArgumentList '--path', "`"$root`""
