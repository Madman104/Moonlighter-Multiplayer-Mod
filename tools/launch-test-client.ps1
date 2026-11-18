# Starts a second copy of Moonlighter as the local co-op test client.
# The test client runs without Steam and keeps its saves in CoopTestClient\.
# On the first run it seeds that folder with a COPY of your Steam Cloud saves
# (read only: the originals are never changed).
param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Moonlighter",
    [string]$DataDir = "$env:USERPROFILE\AppData\LocalLow\11BitStudios\Moonlighter"
)
$ErrorActionPreference = "Stop"

$testDir = Join-Path $DataDir "CoopTestClient"
New-Item -ItemType Directory -Force $testDir | Out-Null

if (-not (Get-ChildItem $testDir -File -ErrorAction SilentlyContinue)) {
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam").SteamPath
    $remote = Get-ChildItem (Join-Path $steam "userdata\*\606150\remote") -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($remote) {
        Copy-Item (Join-Path $remote.FullName "*") $testDir -Recurse
        Write-Host "Seeded test saves from $($remote.FullName)"
    } else {
        Write-Host "No Steam Cloud saves found; the test client starts with no saves."
    }
}

Start-Process -FilePath (Join-Path $GameDir "Moonlighter.exe") -ArgumentList "--coop-test-client" -WorkingDirectory $GameDir
Write-Host "Test client started. Its saves: $testDir"
