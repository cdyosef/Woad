<#
.SYNOPSIS
    Builds Woad, runs the rules-engine tests, then runs the game headless five times: clicking
    through the menus (--menutest), pushing real mouse clicks through a room (--uitest), through
    a door and down stairs (--floortest) and out of the exit to the saved results (--resultstest),
    and letting a bot play a generated dungeon (--autoplay).

.PARAMETER Godot
    Path to a Godot .NET console executable. Defaults to $env:GODOT, then the version Woad.csproj
    pins (on PATH or in winget's package folder), then any Godot .NET console build on PATH.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\smoke-test.ps1
#>
[CmdletBinding()]
param(
    [string]$Godot = $env:GODOT
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not $Godot) {
    $version = (Select-String -Path "$root\Woad.csproj" -Pattern 'Godot\.NET\.Sdk/(\d+\.\d+\.\d+)').Matches[0].Groups[1].Value -replace '\.0$', ''
    $pinned = "Godot_v$version-stable_mono_win64_console.exe"
    $wingetPackages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $Godot = @(
        (Get-Command $pinned -ErrorAction SilentlyContinue).Source
        if (Test-Path $wingetPackages) { (Get-ChildItem $wingetPackages -Recurse -Filter $pinned -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
        (Get-Command 'Godot_v*_mono_win64_console.exe', 'godot-mono' -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    ) | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Godot) {
    throw 'No Godot .NET console executable found. Pass -Godot or set $env:GODOT.'
}

Write-Host "Using $Godot"

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

if (Get-Command python -ErrorAction SilentlyContinue) {
    Invoke-Step 'Generated assets are up to date' {
        python -I "$root\tools\build-assets.py" | Out-Null
        git -C $root diff --exit-code --stat -- assets
    }
}

Invoke-Step 'Build' { dotnet build "$root\Woad.sln" -v q -nologo }
Invoke-Step 'Rules tests' { dotnet test "$root\Woad.sln" --no-build -nologo }
Invoke-Step 'Menu click test' { & $Godot --headless --path $root --quit-after 20000 -- --menutest }
Invoke-Step 'Run click test' { & $Godot --headless --path $root --quit-after 6000 -- --uitest }
Invoke-Step 'Floor click test' { & $Godot --headless --path $root --quit-after 6000 -- --floortest }
Invoke-Step 'Results click test' { & $Godot --headless --path $root --quit-after 200000 -- --resultstest }
Invoke-Step 'Autoplay' { & $Godot --headless --path $root --quit-after 400000 -- --autoplay --fast }

Write-Host 'All smoke tests passed.' -ForegroundColor Green
