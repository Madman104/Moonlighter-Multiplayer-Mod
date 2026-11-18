# Moonlighter Co-op — handoff (1.0.2, 2026-11-18)

## Where things are
- **Repo:** `C:\Users\jhn\OneDrive\Documents\GitHub\Moonlighter-Multiplayer-Mod`, pushed to GitHub `Madman104/Moonlighter-Multiplayer-Mod`, branch `main` (all work, including the old stage branches' history).
- **Version:** mod 1.0.2. Other versions are refused by the handshake.
- **Build/test:** `"C:\Program Files\dotnet\dotnet.exe" test -c Release` (137 tests pass).
  - `tools\deploy.ps1` copies the build into the game; the game must be closed.
  - `tools\package.ps1` makes `dist\MoonlighterCoop-1.0.2.zip` (the plugin plus the player guide).
- **Players:** [docs/PLAYER-GUIDE.md](docs/PLAYER-GUIDE.md) covers install, hosting, joining, the rules and the limits.
- **What to test:** [docs/TESTING.md](docs/TESTING.md). The 1.0 checklist is at the bottom.

## What 1.0 does
| Area | What it does | Tested? |
|---|---|---|
| Connect | Steam lobby/invite, or IP. Main menu "Continue as co-op host" and "Join co-op"; pause → CO-OP | Stage 1 and the host entry played by you; Steam not tested |
| Host's world | On connect the host (in town) sends its save slot. The guest loads it from the main menu with their own bag and gear | Transfer and receive seen in logs; loading not played |
| Dungeons | Same floor, same room, next floor and home together. Emblem portal shared (the host's). Unknown dungeon included. Guest minimap no longer starts revealed | Not played |
| Enemies | Shared health and deaths, 2× health, same champions. **Same positions**: the closer player's game runs each enemy | Not played |
| Gold | One purse. A simultaneous over-spend becomes debt, taken from the next income | Not played |
| Bags | Per player. Each game drops its own loot | Not played |
| Ghosts | Die while your partner is alive → ghost, bag dropped where you fell. A healing pool revives (pool used up for both). Both down → normal death | Not played |
| Day/bed | Host's time of day and calendar for both. Host's bed ends the day | Not played |
| Shop | Showcases, prices and chests shared both ways. Customers only in the host's game, drawn for the guest. Guest's register sells via the host's. Either player can open the shop | Not played |
| Saves | Host saves normally. The guest's own save only gets their bag, gear and weapon set | Copy/read-back seen in logs; real save not played |
| Disconnect | Host gone → guest's bag saved, guest to main menu (a ghost guest just stops being a ghost). Guest gone → host plays on; paused enemies resume | Not played |
| Controller | All co-op menus are the game's own buttons with explicit up/down links. Steam invites through the overlay. INVITE FRIEND reopens the invite window | Keyboard navigation of the main menu entry tested by you |

## 1.1.0 — up to 4 players (branch `feat/four-players`, not merged)
- **CoopLink** (Core, tested): the host keeps one session per guest (up to 3). Everything a guest sends is forwarded to the other guests, tagged with the sender's player number. Host 0, guests 1–3.
- **Steam:** the lobby holds 4. One shared packet reader hands packets out by sender. **IP:** the host accepts up to 3 connections.
- **Per player:**
  - names and puppets;
  - enemies are run by the nearest of all players in the same room;
  - ghost rules: ghost while anyone else is alive, a pool revives every ghost, real death when nobody is left;
  - world and catch-up go to the one guest who joined;
  - when the host's partner leaves, the host keeps hosting for the others.
- **Names over heads** are coloured by health (green → yellow → red, grey = ghost); the separate health bar is gone (that change is on `main` too).
- **Not tested at all, not even launched** (you said you'd test). Before merging into `main`, test with 2, then 3 players. Watch for: everyone in the same room, gold matching, enemy positions, a guest leaving mid-dungeon.

## 1.0.2 (after your first test with a friend)
- **Fixed: a joining player couldn't get into the dungeon.** If you went into the dungeon while your friend was still loading your world, their game missed the trip and stayed in town. Now their game asks for a catch-up once it arrives, and yours re-sends the trip and the current floor, starting in your room.
- **Partner health bar** is below your gold (it overlapped the bag icon) and shows numbers.
- **Steam names** show above the partner's Will and on the health bar.
- **Town NPCs run on the host:** both games pick the same townspeople, and the guest's copies follow the host's positions and sprites. They can still be talked to. Shop customers were already host-only.
- **Decision to check:** enemies stay "run by the closer player" (already in the same place on both screens). Making the host run every enemy would mean enemies never attack the guest.

## Next steps
1. **Play-test with your friend** using the 1.0 checklist in `docs/TESTING.md`. Expect bugs in the untested rows above.
2. Fix what breaks, then **merge** (see below) and tag `v1.0.0`.

## Branches
Everything is on `main` (pushed). Tag a release once a play-test passes, e.g. `git tag v1.0.2` then `git push origin v1.0.2`.

## Decisions to check
- **Guest joins the host's world** (new): needed for a shared shop and town. Joining sends the guest to the main menu if they're in a game. Town upgrades the host buys mid-session show for the guest next time they join.
- **Enemies run by the closer player:** an enemy only attacks the player whose game runs it, and its projectiles only show on that screen. Switching needs a 24-unit margin. Ghosts never run enemies.
- **Shop sync** is "last change wins" per slot every 0.5 s, and never under an open shop panel. If both players change the same slot at once, one change is lost.
- **Guest disconnect handling:** the guest is sent to the main menu. Their bag is saved; their dungeon progress isn't.
- **Gold debt:** the host keeps the shortfall and silently takes it from the next income.
- **Ghost drop:** non-safe bag slots only; equipment and potion are kept. The Angel Amulet doesn't prevent it.
- **Loot is per player:** each game drops its own, so both of you get a copy.
- **Wanderer DLC dungeons:** travel is shared there, enemies aren't, and there are no ghosts.

## Known limits / minor findings not fixed
- Enemy summons the follower doesn't have are drawn as copies and can't be hit on that screen.
- Max enemy health can differ (difficulty/NG+ per player); the host's 1 s health correction hides most of it.
- Shop customers' sprites must be loaded in the guest's game. If some customers are invisible for the guest, the log says "Customer sprite not loaded here".
- The guest's bed gives no bed benefits.
- The guest's save doesn't keep item discoveries, recipes or achievements from co-op.

## Review (1.0)
A fresh reviewer read everything since the shared bag (`9c39f1c..HEAD`).
- **Fixed (commit `a4dc767`), critical:**
  - The guest's save could get the host's world written into it.
  - A ghost's dropped bag was lost if the host disconnected.
- **Fixed, important:**
  - Shop updates could undo the other player's changes and destroy items. Now only changed slots are sent; the host wins a clash.
  - The guest could get stuck on the way to the main menu.
  - Paused enemies didn't get their own state back.
  - A missing host floor made the games drift apart; the guest now leaves the session instead.
- **Fixed, minor:** see the commit message.
- **Not a bug (checked):** "the guest's register sale never completes". The guest has no customers, so their register tray is always empty; sales run on the host's register.
- **Left, minor:**
  - A gold total sent just before the guest's purchase arrives can briefly show old gold. Debt covers it.
  - A dead enemy on the runner's side can move on the follower's screen for up to 1 s, until the death arrives.
  - Ownership doesn't check that the guest is in the same room (harmless; the host resumes the enemies).
  - `HostEmblemTrip` is set before the portal trip is confirmed.
- **New behaviour from the fixes:** a guest with no save of their own can't join. They need to start a game first.
