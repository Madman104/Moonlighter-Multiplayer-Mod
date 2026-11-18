# Moonlighter Co-op

<!-- download -->
## Download & install (players)
1. Download the zip from **[Releases](https://github.com/Madman104/Moonlighter-Multiplayer-Mod/releases/latest)** (`MoonlighterCoop-1.5.0.zip` under Assets).
2. Install **BepInEx 5** (5.4.x, x64) into your Moonlighter folder, the one with `Moonlighter.exe`. To find it in Steam: right-click Moonlighter → Manage → Browse local files. Start the game once, then close it.
3. Unzip the mod zip into that same folder. You should end up with `BepInEx\plugins\MoonlighterCoop\Coop.Plugin.dll`.
4. Everyone playing needs the same mod version and the same DLC.

How to host, join and play: [docs/PLAYER-GUIDE.md](docs/PLAYER-GUIDE.md).
<!-- /download -->

A BepInEx 5 mod that adds two-player online co-op to Moonlighter 1.15.8. Players: see
[docs/PLAYER-GUIDE.md](docs/PLAYER-GUIDE.md).

## Layout
- `src/Coop.Core` (netstandard2.0, no game references): protocol, session/handshake, transports, and pure rules. Unit-tested.
- `src/Coop.Plugin` (net472): the BepInEx plugin. Harmony patches and Unity code, grouped by area:
  - `Menu/`: the pause menu and main menu entries.
  - `Sync/`: drawing the partner's Will and shop customers.
  - `Dungeon/`: floors, travel, enemy health and enemy positions.
  - `Shared/`: gold, bag drop and ghosts, day, saves, world, shop.
  - `Steam/`: lobby and P2P.
- `tests/Coop.Core.Tests`: xUnit tests for Core.
- `docs/`: the design spec, test checklists (`TESTING.md`) and research notes.

## Build
```
"C:\Program Files\dotnet\dotnet.exe" test -c Release
tools\deploy.ps1          # build + copy into the game (game must be closed)
tools\package.ps1         # build + dist\MoonlighterCoop-<version>.zip (attach it to a GitHub Release for players)
tools\launch-test-client.ps1   # second local copy for one-PC testing
```
`GameDir` defaults to `D:\SteamLibrary\steamapps\common\Moonlighter`; pass `-GameDir` (or `-p:GameDir=`) for another install.
