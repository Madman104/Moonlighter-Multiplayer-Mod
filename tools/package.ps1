# Builds the mod and makes the release zip: dist\MoonlighterCoop-<version>.zip.
# Unzip it into the Moonlighter folder (the one with Moonlighter.exe); BepInEx 5 must be installed.
param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Moonlighter"
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
& $dotnet build "$root\src\Coop.Plugin\Coop.Plugin.csproj" -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

$version = (Select-String -Path "$root\src\Coop.Plugin\CoopPlugin.cs" -Pattern 'ModVersion = "([^"]+)"').Matches[0].Groups[1].Value
$bin = "$root\src\Coop.Plugin\bin\Release\net472"
$stage = Join-Path $root "dist\stage"
$plugin = Join-Path $stage "BepInEx\plugins\MoonlighterCoop"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $plugin | Out-Null
Copy-Item "$bin\Coop.Plugin.dll", "$bin\Coop.Core.dll" $plugin
Copy-Item "$root\docs\PLAYER-GUIDE.md" (Join-Path $plugin "README.md")

$zip = Join-Path $root "dist\MoonlighterCoop-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item -Recurse -Force $stage
Write-Host "Made $zip"
