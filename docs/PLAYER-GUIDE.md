# Moonlighter Co-op — player guide

Online co-op for Moonlighter 1.15.8 (Steam), for **up to 4 players**. One player hosts; the others join
and play in the host's world.

## Install (both players)
1. Install **BepInEx 5** (5.4.x, x64) into your Moonlighter folder (the folder with `Moonlighter.exe`), and start the game once.
2. Unzip `MoonlighterCoop-<version>.zip` into that same folder. You should end up with `BepInEx\plugins\MoonlighterCoop\Coop.Plugin.dll` and `Coop.Core.dll`.
3. Both players need **the same mod version** and **the same DLC**, or the host refuses the connection.

## Play together
**Host**
- Main menu: **Continue as co-op host**. Your save loads, and once you're in town Steam's invite window opens. Pick your friend.
- Or, in game: pause → **CO-OP** → **HOST (STEAM)**. **INVITE FRIEND** opens the invite window again.

**Friend (guest)**
- Accept the invite from Steam: the notification, the overlay (Shift+Tab, or the Guide button on a pad), or **Join co-op** on the main menu, which opens the friends list.
- Your game loads the **host's world**. If you were already playing, it goes to the main menu first; press any key there if asked.
- **First time in a host's world**, the main menu asks: **Play the tutorial** (the game's own intro, then you arrive in the host's town) or **Skip the tutorial** (you get a Training Sword if you have no weapon). You won't be asked again for that world.
- **No save yet?** That's fine: joining makes a new save for you, which keeps your bag and gear from co-op.

**Without Steam (IP):** the host uses pause → CO-OP → **HOST (IP)** and forwards TCP port 27460. The guest copies the host's address, then uses pause → CO-OP → **PASTE IP** and **JOIN**. **Join co-op** on the main menu joins the last address you used.

Everything works with a controller. Menus use up/down and confirm, and Steam invites work through the overlay.

## What's shared and what's yours
| Shared (the host's world) | Yours |
|---|---|
| Town, shop, upgrades, quests, calendar and time of day; townspeople walk the same way on both screens | Your bag, equipment and weapons |
| Gold: one purse, and either player's purchase spends it | Your health |
| Dungeon floors: same layout for everyone; going to the next floor or home takes everyone | Which room you're in: explore the floor on your own or together |
| | Your quest drops |
| Loot on the floor: one copy for everyone, whoever picks it up first gets it | |
| Dungeon chests, including boss reward chests (one player at a time), and pots/crates: break or empty them once for everyone | |
| Shrines and healing pools: used by one player = used for everyone | |
| Town upgrades and the banker: show up for everyone straight away | |
| Enemies: same place, same health (2× health in co-op), including summoned ones and enemy-wave rooms | |
| Enemy attacks and projectiles: everyone sees them, and they can hit anyone | |
| The shop: showcases, prices, chests, customers, cash register | |
| Sounds: you hear each other's footsteps, swings and hits, quieter with distance | |

- **Bed:** the host's bed ends the day for both players, and everyone gets the bed's bonus.
- **Shop:**
  - Either player can stock showcases, set prices and use the register. Stands update for everyone as you change them.
  - One player at a time in the shop's menus (stands, shop chests, sales box): others see "<name> is using the shop" until it's closed.
  - Customers come into the host's game; the guest sees them and can sell to them.
- **Emblem portal:** the host's portal is the shared one. The guest's portal asks the host to go through theirs.

Every other player's name (their Steam name) shows above their Will. Its colour shows their health: green when full, yellow at half, red when low.

**Rooms:** each player can go anywhere on the floor. What happened in a room you weren't in (enemies killed, pots broken, chests emptied, shrines used) is there when you walk in. Loot from an enemy is rolled by the game of the player who fought it.

## Dying and spectating
- **Dying while anyone else is alive:** you **spectate**. Your Will disappears and you're always in the same room as the living player you watch, room to room and floor to floor. Nothing is dropped.
- **Several players alive:** left click (or the left bumper) watches the previous one, right click (or the right bumper) the next.
- **Your run ends** when the player you watch goes home, or when nobody is left alive. Then it's a normal death, with the same results screen and item loss as in solo play.

## Saves
- The **host's** save is the shared world and is saved as usual.
- The **guest's** own save keeps the bag and gear they end co-op with, plus what they learned: items discovered, recipes and enchantments seen, decorations and familiars found, deepest floor reached. Their gold, day and story stay as they were.
- If the host disconnects, the guest's bag is saved and they go back to the main menu.

## Known limits
- Each enemy is run by the game of the nearest player, so it chases that player (walk closer than your partner and it turns on you). Its attacks can still hit anyone in the way. Hits on you are worked out on that player's screen, so with lag a hit can land a moment after it looked like a miss.
- A bigger shop building appears the next time you walk into town (new shops in town open straight away).
- If some shop customers are invisible for the guest, send the log: it says "Customer sprite not loaded here".
- Wanderer DLC dungeons are shared, apart from its in-between levels (the lift and the falling floor). It's untested: if DLC runs misbehave, set `[Co-op] ShareWandererDungeons = false` on every PC.

## Settings
In the pause menu under **CO-OP**: **NAMES** (on/off) and **PARTNER SOUNDS** (100% / 50% / off).

Everything else is in `BepInEx\config\com.coop.moonlighter.cfg` (made on first start; edit with the game closed):

| Setting | Default | What it does |
|---|---|---|
| `[UI] ShowPlayerNames` | true | Names above the other players' heads |
| `[Sound] PartnerSoundVolume` | 1 | Volume of the other players' sounds (0 = off) |
| `[Sound] PartnerSoundRange` | 3 | How far their sounds carry, in half-screens |
| `[Co-op] EnemyHandoffDistance` | 24 | Host only: how much closer you must be before an enemy turns on you (16 = one tile) |
| `[Updates] CheckForUpdates` | true | On start, look for a newer release on GitHub and say so |
| `[Co-op] ShareWandererDungeons` | true | Share the Wanderer DLC dungeon (must match on every PC) |
| `[Connection] Port` | 27460 | Port for IP play |

## Troubleshooting
The log is `BepInEx\LogOutput.log` in the Moonlighter folder. Send it (from both players) when something goes wrong.

| You see | What to do |
|---|---|
| "Co-op mod version mismatch" | Everyone needs the same version. Get the latest zip from the Releases page. |
| "Game version mismatch" | Update Moonlighter in Steam on both PCs. |
| Nothing co-op in the menus, no "Moonlighter Co-op ... loaded" in the log | BepInEx isn't installed right: `winhttp.dll` and the `BepInEx` folder must sit next to `Moonlighter.exe`, and the game must be started once after installing it. |
| Can't join by IP | The host must allow the port (27460 TCP) in their firewall and router, or use Steam invites instead. |
| Joining puts you on the main menu | Expected: the guest loads the host's world from there. Pick your save when asked. |
| Your partner is stuck in town when you go into the dungeon | Wait a few seconds; their game catches up once it has loaded your world. |
| "Shared floor item not known here" in the log | That item doesn't exist in this player's game (different DLC or NG+). |
| "Dropped a late room update" in the log | A room change arrived for a room this game never built. Harmless once; tell me if it repeats. |

## Debug launch options (for testing on one PC)
`--coop-test-client` (second copy without Steam, separate saves), `--coop-auto-host`, `--coop-auto-join <address>`. Players don't need these.
