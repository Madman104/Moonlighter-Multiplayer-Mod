# Co-op mod — test log

## Stage 1 (connect + see each other in town)

### Automated (2026-11-18)

| Check | Result |
|---|---|
| `dotnet test` (67 tests: protocol, framing, handshake, session, TCP, save paths, interpolation, sprite ids, visibility) | PASS |
| Plugin loads in game launched via Steam (`Moonlighter Co-op 0.1.0 loaded`) | PASS |
| Test client starts alone without Steam (`--coop-test-client`), platform DRM-free | PASS |
| Test client seeds `CoopTestClient\` from Steam Cloud saves; Steam `remote\` files unchanged | PASS |
| Two test copies on one PC connect over 127.0.0.1 (`--coop-auto-host` / `--coop-auto-join 127.0.0.1`), both reach Connected | PASS |
| Closing the joined copy: host shows `Closed: Disconnected` within 12 s and keeps running | PASS |
| Two connected copies at the main menu with player sync running: no errors | PASS |
| Steam launch: `Steam co-op ready`, no errors | PASS |

### Needs a person at the keyboard

Setup: run `tools\deploy.ps1` with the game closed.

1. **Two copies at once.** Start Moonlighter through Steam (host). Then run `tools\launch-test-client.ps1`. Both windows stay open.
2. **Connect.** Load a save in both windows. Host window: pause, **CO-OP**, **HOST (IP)**. Allow the Windows Firewall prompt if it appears. Test window: pause, **CO-OP**, **JOIN 127.0.0.1**. The status line in both menus shows `Connected`, and a notice appears at the top of the screen. The CO-OP page uses the game's own buttons, works with mouse, keyboard and controller, and Back returns to the pause page.
3. **See each other.** Resume and walk around town in the host window. In the test window, a blue-tinted Will labelled P1 follows you: it walks, faces the right way and animates smoothly. Then try it the other way around.
4. **Different places.** Walk one Will into a dungeon while the other stays in town: the puppet disappears (dungeons are shared from stage 2). Inside the shop the puppet stays at its real position.
5. **Disconnect.** Close the test window. Within 10 s the host shows `Closed: Disconnected`, the puppet disappears, and the game keeps running.
6. **Test-client saves.** Save in the test window and quit it. Only files in `%USERPROFILE%\AppData\LocalLow\11BitStudios\Moonlighter\CoopTestClient\` change.
7. **Steam lobby (one account).** Host window: pause, **CO-OP**, **HOST (STEAM)**. The Steam overlay opens an invite dialog listing your friends.
8. **Steam join (needs a second Steam account that owns Moonlighter).** The friend accepts the invite and both show `Connected`. Not yet possible to test.

Results:

| # | Date | Result | Notes |
|---|---|---|---|
| 1–7 | 2026-11-18 | PASS | reported by user: works on one PC (pause-menu CO-OP page, connect, see each other) |
| 8 | | not tested | needs second account |

## Stage 2 (dungeon rooms together)

### Automated (2026-11-18)

| Check | Result |
|---|---|
| `dotnet test` (100 tests incl. floor layout store, travel arbiter, dungeon location, 200 KB layout through framing) | PASS |
| Two connected test copies: all dungeon patches (GenerateDungeon, LoadDungeonScene, LoadTownSceneFromDungeon, ChangeRoomTo, SpecialChangeRoomTo) apply, no errors | PASS |

### Needs a person at the keyboard

Setup as stage 1 (host via Steam + `tools\launch-test-client.ps1`, connect via pause → CO-OP).

1. **Enter together.** Host walks into a dungeon gate. The test window follows into the same dungeon. Log (`BepInEx\LogOutput.log*`): host `Sent floor layout ...`, client `Building the host's ... floor`.
2. **Same layout.** Both minimaps show the same rooms. The other player's Will is visible in the same room.
3. **Rooms.** Host goes through a door: test window moves to the same room. Then the test window goes through a door: host follows.
4. **Holes / special moves.** Fall through a hole or use a secret room: the other follows.
5. **Next floor.** Take the next-floor door: both arrive on the same next floor.
6. **Back to town.** Use the pendant or the dungeon exit: both return to town.
7. **Client enters first.** Repeat 1 with the test window walking into the gate first.

Known limits of stage 2 (by design): enemies, chests and loot are not shared yet (stage 3/4); each game spawns its own enemies, so a room can be clear in one window and full in the other. Dying sends both players home. The test client's own save slot is still written on dungeon entry (stage 6).

| # | Date | Result | Notes |
|---|---|---|---|
| 1–7 | | | |

More stage 2 limits found in review (not fixed yet):
- The joining player's minimap starts fully revealed (the game's saved-floor builder reveals it).
- The joining player's save doesn't record deeper floors reached, the Golem special unlock, the Wanderer spawn chance or the desert shrine on hard-enemy floors (those come from the normal generator, which the joining side skips).
- Pendant/emblem: going home is shared, but returning through the emblem portal is not; the other player stays in town.
- Unknown (DLC) and Wanderer floors are not shared yet: each game builds its own.

## Stage 3a (shared enemy health)

Automated (2026-11-18): `dotnet test` 109 pass; two connected test copies start with the enemy patches (EnemyStats.Init, SpawnEnemies, CurrentHealth setter, DropItemsOnDeath) applied, no errors.

Needs a person:
1. **Same enemies.** Enter a room together: both windows show the same enemy types; a champion (glowing) appears in both or neither.
2. **Double health.** Enemies take about twice as many hits as in solo.
3. **Shared damage.** Hit an enemy in one window: its health bar drops in the other window too. Kill it in one window: it dies in the other.
4. **Room clears together.** When all enemies are dead, doors open in both windows.
5. **Loot.** Only the host window drops items (the test window drops none, by design until stage 4).

Known limit (by design for 3a): enemies move independently on each screen (each chases its own player).

| # | Date | Result | Notes |
|---|---|---|---|
| 1–5 | | | |

## Stage 4a (shared gold) + guest save protection

Automated (2026-11-18): `dotnet test` 111 pass; two connected test copies start with the Stat.Value and SerializeGameSlot patches applied, no errors.

Needs a person:
1. **Same purse.** After connecting, the test window's gold changes to the host's amount.
2. **Spend once.** Buy something in either window: both windows show the same lower total.
3. **Earn together.** Pick up gold in a dungeon in either window: both totals rise by the same amount.
4. **Guest save untouched.** Play as the test window, quit, and check that `CoopTestClient\gameslot` did not change while connected (log line: `your own save is not written while you are a guest`).

| # | Date | Result | Notes |
|---|---|---|---|
| 1–4 | | | |

## Stage 5 (ghosts, revive, partner health bar)

Automated (2026-11-18): `dotnet test` 118 pass; two connected test copies start with the Die / DealDamageToPlayer / DealDamageToEnemy / HealingArea / LoadTownSceneFromDungeon patches applied, no errors.

Needs a person:
1. **Partner health bar.** In a dungeon, top-left shows `P1`/`P2` with the partner's health; it drops when they take damage.
2. **Ghost.** Let the test window's Will die while the host is alive: it doesn't go home; it turns see-through, takes no damage and can't hurt enemies. The host's bar label says "ghost - find a healing pool".
3. **Revive.** Host walks into a healing pool: the pool goes dark (used up), the ghost comes back at half health, and nobody gets healed by that pool afterwards.
4. **Both down.** With one player a ghost, let the other die: both return to town as after a normal death.
5. **Partner leaves while you're a ghost:** you die normally.

| # | Date | Result | Notes |
|---|---|---|---|
| 1–5 | | | |

## Stage 6a (shared day/night, host's bed)

Automated (2026-11-18): `dotnet test` 119 pass; two connected test copies start with the TimeManagerV2 and Bed patches applied, no errors.

Needs a person:
1. **Same time of day.** After connecting, the test window's day/evening wheel matches the host's within 5 s.
2. **Host sleeps.** Host uses the bed: the test window switches too and shows "Your partner slept: a new day begins".
3. **Guest bed.** The test window's bed does nothing except show "Only the host's bed ends the day".

| # | Date | Result | Notes |
|---|---|---|---|
| 1–3 | | | |

## Stage 4b v1 (shared bag)

Automated (2026-11-18): `dotnet test` 121 pass; two connected test copies start with bag sync, no errors.

Needs a person:
1. **Host's bag wins on connect.** After connecting, the test window's bag shows the host's items.
2. **Pick up / drop / move.** Change the bag in either window: within about 0.25 s the other window's bag matches (equipment stays separate).
3. **Stacks and enchanted gear** keep their count and enchantment.

Known limits: if both players change the bag at the same moment, the last change wins. Loot on the floor is still only on the host's screen (stage 3a); the guest picks up nothing yet.

| # | Date | Result | Notes |
|---|---|---|---|
| 1–3 | | | |

## Own bags + ghost drop (mod 0.7.0)
Automated: 121 tests pass; two test copies connect on 0.7.0 with every patch applied and no errors.

Manual checklist (not played yet):
1. Both in a dungeon: kill an enemy. Each player sees and can pick up their own loot.
2. Each player's bag changes only on their own screen.
3. Die while your partner is alive: you turn see-through and your non-safe bag items scatter where you fell.
4. As a ghost, walk over the pile: nothing is picked up or pulled toward you.
5. Partner uses a healing pool: you revive, walk back to the pile and pick everything up.
6. Leave the room and come back before picking up: the pile is still there.
7. Guest save: as the guest, pick up loot in co-op, go home, leave co-op, then reload your save. Your new bag items and gear are there. Your gold and day are what they were before you joined.

## Main menu "Continue as co-op host"
| Check | Result |
|---|---|
| Entry appears under Continue in the game's font, other entries move down one row | PASS (screenshot) |
| Up/Down reaches it with keys | PASS (you tested it, after the navigation fix) |
| Selecting it loads the save, then hosting starts in town | PASS (test-client mode, IP hosting, you tested it) |
| Steam: lobby made and invite window opens | not tested |

## 1.0 play-test checklist (two PCs, Steam)
Nothing below has been played yet. Mark PASS/FAIL with notes.

| # | Check | Result |
|---|---|---|
| 1 | Host: main menu "Continue as co-op host": loads, invite window opens in town | |
| 2 | Guest accepts the invite from the main menu ("Join co-op" opens the friends list) and lands in the host's town with their own bag | |
| 3 | Guest accepts while already in their own game: goes to the main menu, then loads the host's world | |
| 4 | Gold matches on both screens; both buy at the same moment with little gold: total ends right | |
| 5 | Shop: guest puts an item on a showcase and sets a price; host sees it. Host takes it; guest sees it gone | |
| 6 | Shop: either player opens the shop; customers appear for both; guest sells at the register | |
| 7 | Storage chest: item moved by one player appears for the other | |
| 8 | Dungeon: same floor; guest's minimap starts unexplored | |
| 9 | Enemies are in the same place on both screens; one chasing the guest attacks the guest | |
| 10 | Kill enemies from both sides; room opens for both | |
| 11 | Ghost: bag drops; revive at a pool; pick the bag back up | |
| 12 | Host uses the pendant; back in town both use the host's portal (guest's portal asks the host) | |
| 13 | Host sleeps: new day for both; day number matches | |
| 14 | Host quits mid-dungeon: guest gets a notice, bag kept, back to the main menu; guest's save shows their own gold/day | |
| 15 | Guest quits: host keeps playing; enemies move normally | |
| 16 | Controller only (both players): every co-op menu entry reachable; invite accepted via the overlay | |
| 17 | Unknown (5th) dungeon: same floor and enemies | |
| 18 | (1.0.2) Host goes into the dungeon while the guest is still loading in: guest arrives in the host's room | |
| 19 | (1.0.2) Partner health bar sits under the gold, not over the bag icon; Steam names shown | |
| 20 | (1.0.2) Town: same townspeople in the same places on both screens; guest can talk to them | |

## Shared floor loot (mod 1.3.0)
1. Both in a dungeon room: kill an enemy. The same items land in the same places on both screens (the guest's copy appears about 1 s later, when the host's has landed).
2. One player picks an item up: it disappears from the other screen. A partial pickup (full bag) leaves the same count on both.
3. Break a pot / get boss loot: same items on both screens.
4. Become a ghost: your dropped bag is only on your screen.
5. Log: "Shared floor item not known here" means the item didn't exist in that game (DLC/NG+ mismatch).

## Shared chests and pots (mod 1.4.0)
1. Same room: chests are the same type in both windows (a mimic is a mimic in both, and shares health).
2. Clear the room, open a chest in one window: the other player gets "<name> is using this chest" if they try.
3. Take items, close: the other window's chest now has the same items left.
4. Break a pot in one window: it breaks in the other too, and any drop appears once (shared floor item).
5. Log warnings to look for: "Could not apply a shared chest", "Could not break a shared breakable", "Could not tag mimics".

## Boss chests, shrines, pools, races, late join (mod 1.5.0)
1. Kill a boss: the reward chests are the same types with the same contents in both windows.
2. Press a shrine in one window: the doors open in the other window too.
3. Drink from a healing pool in one window: it darkens in the other; the other player gets only what's left.
4. Both players walk onto the same item at the same moment: only one keeps it; the other sees "Your partner grabbed that first" and it leaves their bag.
5. Both open the same chest at the same moment: the later one sees "Your partner was already using that chest" on closing, and ends up with the first one's content.
6. Late join: host goes into the dungeon, kills things, leaves loot, opens a chest, uses a pool; then the guest joins. The guest sees the same loot, chest content and pool level.

## Settings, updates, max health, guest progress (mod 1.6.0)
1. Pause > CO-OP: NAMES toggles the names; PARTNER SOUNDS cycles 100% / 50% / OFF and the partner's footsteps follow.
2. With an older mod, start the game: a toast says the newer version is out; the log says "Co-op mod X is out". Up to date: the log says "Co-op mod is up to date".
3. Different difficulty / NG+ between players: an enemy's health bar is the same on both screens after a second.
4. As the guest, discover a new item in co-op, leave co-op, load your own save: the item is discovered there too.

## Shared attacks, summons, waves, town, Wanderer (mod 1.7.0)
1. Stand near an enemy your partner is closer to: its projectiles show on your screen and hit you (watch the health bar); dodging through them works.
2. A slime that splits / a seeder that sprouts: the new enemies appear in both windows, share health and can be hit by both.
3. Enemy-wave shrine: the wave enemies appear in both windows.
4. Host buys a town upgrade: the guest's log says "town upgrades updated from another player"; the shop/bed level works for the guest.
5. Wanderer DLC dungeon: same floor, same enemies, shared loot.
6. Log lines: "Partner hitbox layer N, touches M enemy layers" (M should be 2-4), "Enemy hit guard on A AttackHit and B contact methods".

## Waves, boss minions, panning, bed (mod 1.8.0)
1. Enemy-wave shrine: when one player starts it, the other window plays the same wave (room closes, portals open) with the same enemies; beating it opens the room and the reward chest in both windows, with the same content.
2. Golem King: its rocks/minions appear in both windows and can be hit by both.
3. Partner sounds come from their side of the screen (left/right).
4. Host sleeps: the guest gets the bed bonus (shield/absorption) too.
5. Guest presses LEAVE CO-OP: the toast says "Left co-op", and their bag, gear and discoveries are in their own save.

## Weapons, blinking, own rooms, spectating, smoothness (mod 1.9.0)
1. Attack with each weapon type: the partner's screen shows the weapon swing, in front of/behind Will as on your screen.
2. Walk around for a few minutes: the partner's Will never blinks out. Log: any "Partner sprite not loaded here: ..." lines (send them).
3. Go into different rooms of the same floor: no one is pulled along. Each fights their own room; walking into a room the other cleared shows it cleared, opened chests opened, broken pots broken.
4. Both in one room again: enemies are shared as before.
5. Taking the stairs or going home: everyone goes.
6. Die while the partner lives: you vanish and watch them; nothing drops. When they go home you get the normal death results (bag lost like solo).
7. Dungeon framerate is steady (no hitch every couple of seconds).

## Spectate switching (mod 1.9.1)
1. 2 players: the dead player is always in the living player's room (changes room with them).
2. 3+ players, one dead: left/right click (or bumpers) switches who you watch; a toast names them; the view moves to their room.

## Shop lock, missing art, bubbles (mod 1.10.0)
1. Shop: one player opens a stand menu; the other gets "<name> is using the shop" when trying any stand, shop chest or the sales box.
2. Put an item on a stand and close: the other player sees it on the stand within a moment; only one player can take it out (no copies).
3. Both press open at the same instant: one menu closes with "<name> opened the shop first".
4. Partner uses a weapon you don't own (e.g. Broom Spear): after a moment its swing shows. Log on the owner's side: "Sending sprite pictures to other players".
5. Guest in the shop: customers' expression bubbles show above stands and furniture.
