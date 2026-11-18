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
| Dungeon floors and rooms: you're always in the same room | Your quest drops |
| Loot on the floor: one copy for everyone, whoever picks it up first gets it | |
| Dungeon chests (one player at a time) and pots/crates: break or empty them once for everyone | |
| Enemies: same place, same health (2× health in co-op) | |
| The shop: showcases, prices, chests, customers, cash register | |
| Sounds: you hear each other's footsteps, swings and hits, quieter with distance | |

- **Bed:** the host's bed ends the day for both players.
- **Shop:**
  - Either player can stock showcases, set prices and use the register.
  - Customers come into the host's game; the guest sees them and can sell to them.
- **Emblem portal:** the host's portal is the shared one. The guest's portal asks the host to go through theirs.

Every other player's name (their Steam name) shows above their Will. Its colour shows their health: green when full, yellow at half, red when low, grey for a ghost.

## Dying, ghosts and revives
- **Dying while anyone else is alive** turns you into a **ghost**. A ghost:
  - drops their bag (except safe slots) where they fell;
  - can't be hurt, attack, pick up items or drink potions.
- **To revive:** a living player uses a **healing pool**. Every ghost comes back at half health, and that pool is used up for everyone. Walk back to your items and pick them up.
- **If nobody is left alive,** it's a normal death with the normal penalty.

## Saves
- The **host's** save is the shared world and is saved as usual.
- The **guest's** own save only keeps the bag and gear they end co-op with. Their gold, day and story stay as they were.
- If the host disconnects, the guest's bag is saved and they go back to the main menu.

## Known limits
- Each enemy is run by the game of the nearest player, so it only chases and hurts that player (walk closer than your partner and it turns on you). Its projectiles only show on that player's screen.
- If both players change the same showcase or chest slot at the same moment, the last change wins.
- Town upgrades the host buys during a session appear for the guest next time they join.
- Wanderer DLC dungeons: you travel together, but enemies there aren't shared.

## Debug launch options (for testing on one PC)
`--coop-test-client` (second copy without Steam, separate saves), `--coop-auto-host`, `--coop-auto-join <address>`. Players don't need these.
