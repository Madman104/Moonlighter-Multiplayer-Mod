# Builds the mod and copies it into the game's BepInEx plugins folder.
param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Moonlighter"
)
$ErrorActionPreference = "Stop"

if (Get-Process -Name "Moonlighter" -ErrorAction SilentlyContinue) {
    Write-Error "Moonlighter is running. Close every game window before deploying."
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
& $dotnet build "$root\src\Coop.Plugin\Coop.Plugin.csproj" -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

$bin = "$root\src\Coop.Plugin\bin\Release\net472"
$target = Join-Path $GameDir "BepInEx\plugins\MoonlighterCoop"
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$bin\Coop.Plugin.dll", "$bin\Coop.Core.dll" $target -Force
Write-Host "Deployed to $target"
