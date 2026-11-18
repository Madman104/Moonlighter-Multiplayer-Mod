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
