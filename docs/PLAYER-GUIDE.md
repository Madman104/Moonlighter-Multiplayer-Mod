# Moonlighter Co-op — player guide

Two-player online co-op for Moonlighter 1.15.8 (Steam). One player hosts; the other joins
and plays in the host's world.

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

**Without Steam (IP):** the host uses pause → CO-OP → **HOST (IP)** and forwards TCP port 27460. The guest copies the host's address, then uses pause → CO-OP → **PASTE IP** and **JOIN**. **Join co-op** on the main menu joins the last address you used.

Everything works with a controller. Menus use up/down and confirm, and Steam invites work through the overlay.

## What's shared and what's yours
| Shared (the host's world) | Yours |
|---|---|
| Town, shop, upgrades, quests, calendar and time of day; townspeople walk the same way on both screens | Your bag, equipment and weapons |
| Gold: one purse, and either player's purchase spends it | Your health |
| Dungeon floors and rooms: you're always in the same room | Your loot: each player gets their own drops |
| Enemies: same place, same health (2× health in co-op) | |
| The shop: showcases, prices, chests, customers, cash register | |

- **Bed:** the host's bed ends the day for both players.
- **Shop:**
  - Either player can stock showcases, set prices and use the register.
  - Customers come into the host's game; the guest sees them and can sell to them.
- **Emblem portal:** the host's portal is the shared one. The guest's portal asks the host to go through theirs.

Your partner's name (their Steam name) shows above their Will. In dungeons their health bar is under your gold, top left.

## Dying, ghosts and revives
- **Dying while your partner is alive** turns you into a **ghost**. A ghost:
  - drops their bag (except safe slots) where they fell;
  - can't be hurt, attack, pick up items or drink potions.
- **To revive:** your partner uses a **healing pool**. You come back at half health, and that pool is used up for both of you. Walk back to your items and pick them up.
- **If both of you go down,** it's a normal death with the normal penalty.

## Saves
- The **host's** save is the shared world and is saved as usual.
- The **guest's** own save only keeps the bag and gear they end co-op with. Their gold, day and story stay as they were.
- If the host disconnects, the guest's bag is saved and they go back to the main menu.

## Known limits
- Each enemy is run by the game of the closer player, so it only attacks that player. Its projectiles only show on that player's screen.
- If both players change the same showcase or chest slot at the same moment, the last change wins.
- Town upgrades the host buys during a session appear for the guest next time they join.
- Wanderer DLC dungeons: you travel together, but enemies there aren't shared.

## Debug launch options (for testing on one PC)
`--coop-test-client` (second copy without Steam, separate saves), `--coop-auto-host`, `--coop-auto-join <address>`. Players don't need these.
