<#
.SYNOPSIS
    Builds the Windows version of Woad and uploads it to itch.io.

.DESCRIPTION
    Exports the "Windows Desktop" preset (export_presets.cfg) into export/windows, then pushes that
    folder to itch.io with butler, as the "windows" channel of the game's page. Each push is labeled
    with the git commit it was built from, and itch's app updates players to it automatically.

    Needs, once: Godot's .NET export templates for this Godot version (Editor > Manage Export
    Templates, or unpacked into %APPDATA%\Godot\export_templates), and butler (on the PATH, or in
    %LOCALAPPDATA%\butler), logged in to your itch.io account (run "butler login" yourself; it opens
    your browser).

.PARAMETER Target
    The itch.io page to upload to, as "user/game" (e.g. "chris/woad").

.PARAMETER SkipUpload
    Only build; don't upload. Handy for checking the build runs first.

.EXAMPLE
    .\tools\publish-itch.ps1 -Target chris/woad
#>
param(
    [string]$Target,
    [switch]$SkipUpload
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$godot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
$output = Join-Path $root 'export\windows'

if (-not $SkipUpload -and -not $Target) {
    throw 'Say where to upload: -Target yourname/yourgame (or -SkipUpload to only build).'
}

# A clean folder, so nothing left from an older build gets uploaded.
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force $output | Out-Null

Write-Host 'Building the C# code...'
dotnet build (Join-Path $root 'Woad.csproj') -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }

Write-Host 'Exporting the Windows build...'
& $godot --headless --path $root --export-release 'Windows Desktop' (Join-Path $output 'Woad.exe') | Out-Host
if (-not (Test-Path (Join-Path $output 'Woad.exe'))) { throw 'The export failed: no Woad.exe was made.' }
Copy-Item (Join-Path $root 'assets\fonts\PixelifySans-OFL.txt') $output

if ($SkipUpload) {
    Write-Host "Built: $output"
    return
}

$version = (git -C $root rev-parse --short HEAD).Trim()
$butler = if (Get-Command butler -ErrorAction SilentlyContinue) { 'butler' } else { Join-Path $env:LOCALAPPDATA 'butler\butler.exe' }
Write-Host "Uploading to $Target (version $version)..."
& $butler push $output "${Target}:windows" --userversion $version
if ($LASTEXITCODE -ne 0) { throw 'The upload failed.' }
Write-Host 'Done. It may take a minute for itch.io to process the new build.'
