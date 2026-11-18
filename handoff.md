# Moonlighter Co-op — handoff (2026-11-18)

Written at the end of an unattended session. Start here tomorrow.

## Where things are
- **Repo:** this folder (git). Branches are stacked, each one built on the previous:
  `main` → `feat/coop-stage1` → `feat/coop-stage2` → `feat/coop-stage3` → `feat/coop-stage4` → `feat/coop-stage5` → `feat/coop-stage6` → `feat/coop-stage4b` (shared bag v1, now superseded) → **`feat/coop-own-bags` (checked out, newest)**.
  Nothing is merged into `main` and nothing is pushed anywhere.
- **Installed in the game:** the newest build (own bags + ghost item drop, mod 0.7.0) is deployed to `D:\SteamLibrary\steamapps\common\Moonlighter\BepInEx\plugins\MoonlighterCoop\`.
- **Build & deploy:** close the game, then run `tools\deploy.ps1`. Tests: `"C:\Program Files\dotnet\dotnet.exe" test -c Release` (121 passing).
- **Main menu:** "Continue as co-op host" (under Continue) loads your save and starts hosting once you're in town. Steam opens its invite window; test-client mode hosts on IP. You tested it: selection and loading-then-hosting work.
- **Test on one PC:** start Moonlighter through Steam (host), then run `tools\launch-test-client.ps1` (second copy, no Steam, own saves). Connect with pause → **CO-OP** → HOST (IP) / JOIN 127.0.0.1.
- **What to test, stage by stage:** `docs/TESTING.md`. Stage 1 passed your test. Stages 2–6a are built and smoke-tested only: both copies start and connect with every patch applied and no errors. Nobody has played them yet.

## What each stage does now
| Stage | Status | What it does |
|---|---|---|
| 1 Connect & see each other | ✅ tested by you | Steam lobby / IP join from the pause menu; the partner's Will is drawn as a tinted copy |
| 2 Dungeon rooms together | built, reviewed, fixed | The host's generated floor is sent to the guest, who builds the identical floor; entering, room changes, next floor and returning home are mirrored; host wins clashes |
| 3a Enemies | built | Shared enemy **health/deaths** (each game runs its own enemy AI); same champions on both; 2x health; each game drops its own loot |
| 4a Gold | built | One shared purse; the guest's own save keeps only their own bag/equipment from co-op (see below) |
| 5 Ghosts | built | Die while your partner is alive → ghost; partner uses a healing pool → you revive at half health, pool used up; both down → normal death; partner health bar top-left in dungeons |
| Own bags (2026-11-18, your change) | built, smoke-tested | Each player keeps their own bag (shared bag removed). Loot is per player. Becoming a ghost drops your bag (not safe slots) where you fell; a ghost can't pick items up; walk back to the pile once revived |
| 6a Day/bed | built | The host's time of day is the truth; the host's bed ends the day for both; the guest's bed only shows a notice |

## Not done yet (next steps, in suggested order)
1. **Play-test stages 2–6a** using `docs/TESTING.md`. Expect bugs: these use game code paths that couldn't be driven automatically.
2. **Guest save (your choice: keep bag + gear):** built. When the guest's game saves, it writes a copy of their save from when they joined, with only the current bag, equipment and weapon set put in. Gold, day, story and shop stay as before co-op. Smoke-tested only (the copy is made and reads back); a real save write hasn't been tested yet. If the copy fails, the guest's save isn't written (old behaviour).
3. **Stage 4c: shop.** You chose "both players can set prices and use the counter". Not started (ShopManager research still needed).
4. **Stage 3b: enemies moved by the host.** Right now enemy positions differ between screens. See `docs/notes/stage3-enemies-recon.md`.
5. **Stage 2 limits** listed in `docs/TESTING.md`: the guest's minimap starts revealed, emblem-portal return isn't shared, Unknown/Wanderer DLC floors aren't shared.
6. **Steam joining with a real friend:** never tested (needs a second account).
7. **Merge branches into `main`** once you're happy.

## Decisions to check (made while you were away)
- **Own bags (your 2026-11-18 change):**
  - Loot is instanced: each game drops its own loot, so both players get a copy of every drop. Chests open separately in each game.
  - The death drop leaves items in safe slots (the game's own "safe" flag) and keeps equipment and the potion.
  - Items dropped by a ghost stay in that room (the room's drop list) until the run ends. Going home or a real death leaves them behind, like a normal death.
  - The Angel Amulet doesn't stop the ghost drop.
- **Enemies, stage 3a:** each game runs its own enemy AI; only health/deaths are shared. This was faster to build within the remaining usage. The catch: enemies aren't in the same place on both screens. Stage 3b would fix it.
- **Guest saves:** only bag, equipment and weapon set are kept. Item discoveries, recipes seen and achievements from co-op are not kept in the guest's save.
- **Guest bed:** only shows a notice; it doesn't ask the host to sleep.
- **Ghost rules:** a ghost can't be hurt, can't hurt enemies and can't use pools. Revive gives half health. If the partner disconnects while you're a ghost, you die normally.
- **Gold:** the host's total is pushed every 2 s; the guest's spending is sent as changes. A quick double-spend by both at the same moment is possible.

## Review status
- **Stage 1:** whole-branch review done; fixes applied.
- **Stage 2:** review done; all 4 critical findings and the main important ones fixed (commit `f62c810`).
- **Stages 3a–6a:** review done; fixes in commit `52f9c86`, plus pool sync after it:
  - Enemies now have a stable spawn-order id. The killing blow is shared, and hits no longer land on the wrong enemy when the two games' enemy lists differ.
  - The other player's hits now count even during a local invincibility window.
  - Loading a save no longer sends its gold to the partner. Returning to the main menu leaves the session.
  - The guest's save stays blocked after a disconnect until a save is reloaded.
  - Ghost fixes:
    - A ghost now keeps 1 health.
    - Both players being ghosts, or the partner really dying, now ends in a normal death.
    - Going home as a ghost keeps the death penalty.
    - While a partner is loading, their health counts as unknown, not as dead.
  - A revive drains the pool on both games.
  - The mod version is now **0.6.0**, so older builds are refused.

### Review findings not fixed yet (minor)
- **Day/night:**
  - The guest's day counter can drift from the host's, because only morning/evening is synced.
  - A shop closing shows "Your partner rested" on the guest.
  - The guest gets no bed benefits.
  - The guest sees the notice after the day changes, not before.
- **Gold:** if both players spend at the same instant with too little gold, one purchase can end up free.
- **Ghosts:**
  - Ghosts can drink potions.
  - Ghosts are possible in Wanderer/ICT dungeons, where enemy health isn't shared.
- **Enemies:** max health can still differ between games (difficulty, NG+ and night levelling are per player). The host's 1 s correction hides it, but health-bar fractions can differ.
- **Enemy summons** (boss minions, resurrections) get ids in each game's own order, so they can still mismatch. Pack enemies and bosses are stable.

## Inventory notes
Shared bag v1 (`feat/coop-stage4b`) was removed at your request: bags are per player. The protocol keeps message type 20 (BagSnapshot) unused. Game drop API used for the ghost pile: `ItemStack.Drop(pos, radius, roomTransform)` + `DungeonRoom.AddDropItems`.
