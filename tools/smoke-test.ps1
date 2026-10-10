<#
.SYNOPSIS
    Builds Woad, runs the rules-engine tests, then runs the game headless twice: once pushing
    real mouse clicks through the UI (--uitest) and once letting a bot play the room (--autoplay).

.PARAMETER Godot
    Path to a Godot .NET console executable. Defaults to $env:GODOT, then any Godot .NET
    console build on PATH.

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
    $Godot = (Get-Command 'Godot_v*_mono_win64_console.exe', 'godot-mono' -ErrorAction SilentlyContinue | Select-Object -First 1).Source
}
if (-not $Godot) {
    throw 'No Godot .NET console executable found. Pass -Godot or set $env:GODOT.'
}

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Invoke-Step 'Build' { dotnet build "$root\Woad.sln" -v q -nologo }
Invoke-Step 'Rules tests' { dotnet test "$root\Woad.sln" --no-build -nologo }
Invoke-Step 'UI click test' { & $Godot --headless --path $root --quit-after 6000 -- --uitest }
Invoke-Step 'Autoplay' { & $Godot --headless --path $root --quit-after 40000 -- --autoplay --fast }

Write-Host 'All smoke tests passed.' -ForegroundColor Green
