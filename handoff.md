# Moonlighter Co-op — handoff (2026-11-18)

Written at the end of an unattended session. Start here tomorrow.

## Where things are
- **Repo:** this folder (git). Branches are stacked, each one built on the previous:
  `main` → `feat/coop-stage1` → `feat/coop-stage2` → `feat/coop-stage3` → `feat/coop-stage4` → `feat/coop-stage5` → **`feat/coop-stage6` (checked out, newest)**.
  Nothing is merged into `main` and nothing is pushed anywhere.
- **Installed in the game:** the newest build (stage 6a) is deployed to `D:\SteamLibrary\steamapps\common\Moonlighter\BepInEx\plugins\MoonlighterCoop\`.
- **Build & deploy:** close the game, then run `tools\deploy.ps1`. Tests: `"C:\Program Files\dotnet\dotnet.exe" test -c Release` (119 passing).
- **Test on one PC:** start Moonlighter through Steam (host), then run `tools\launch-test-client.ps1` (second copy, no Steam, own saves). Connect with pause → **CO-OP** → HOST (IP) / JOIN 127.0.0.1.
- **What to test, stage by stage:** `docs/TESTING.md`. Stage 1 passed your test. Stages 2–6a are built and smoke-tested only: both copies start and connect with every patch applied and no errors. Nobody has played them yet.

## What each stage does now
| Stage | Status | What it does |
|---|---|---|
| 1 Connect & see each other | ✅ tested by you | Steam lobby / IP join from the pause menu; the partner's Will is drawn as a tinted copy |
| 2 Dungeon rooms together | built, reviewed, fixed | The host's generated floor is sent to the guest, who builds the identical floor; entering, room changes, next floor and returning home are mirrored; host wins clashes |
| 3a Enemies | built | Shared enemy **health/deaths** (each game runs its own enemy AI); same champions on both; 2x health; only the host drops loot |
| 4a Gold | built | One shared purse; the **guest's own save is never written** while connected |
| 5 Ghosts | built | Die while your partner is alive → ghost; partner uses a healing pool → you revive at half health, pool used up; both down → normal death; partner health bar top-left in dungeons |
| 6a Day/bed | built | The host's time of day is the truth; the host's bed ends the day for both; the guest's bed only shows a notice |

## Not done yet (next steps, in suggested order)
1. **Play-test stages 2–6a** using `docs/TESTING.md`. Expect bugs: these use game code paths that couldn't be driven automatically.
2. **Stage 4b: shared inventory + items.** The biggest missing piece. Loot currently drops only on the host, so the guest can't pick anything up. Needs: one bag synced from the host, pickups as requests to the host, chests opened once.
3. **Stage 4c: shop.** You chose "both players can set prices and use the counter". Not started (ShopManager research still needed).
4. **Stage 3b: enemies moved by the host.** Right now enemy positions differ between screens. See `docs/notes/stage3-enemies-recon.md`.
5. **Stage 2 limits** listed in `docs/TESTING.md`: the guest's minimap starts revealed, emblem-portal return isn't shared, Unknown/Wanderer DLC floors aren't shared.
6. **Steam joining with a real friend:** never tested (needs a second account).
7. **Merge branches into `main`** once you're happy.

## Decisions to check (made while you were away)
- **Enemies, stage 3a:** each game runs its own enemy AI; only health/deaths are shared. This was faster to build within the remaining usage. The catch: enemies aren't in the same place on both screens. Stage 3b would fix it.
- **Guest saves:** the guest's game never writes its save while connected (design anti-goal). Progress the guest makes in co-op is therefore not kept in their own save.
- **Guest bed:** only shows a notice; it doesn't ask the host to sleep.
- **Ghost rules:** a ghost can't be hurt, can't hurt enemies and can't use pools. Revive gives half health. If the partner disconnects while you're a ghost, you die normally.
- **Gold:** the host's total is pushed every 2 s; the guest's spending is sent as changes. A quick double-spend by both at the same moment is possible.

## Review status
- Stage 1: whole-branch review done, fixes applied.
- Stage 2: review done; all 4 critical and the main important findings fixed (commit `f62c810`).
- Stages 3a–6a: a review was started at the end of the session. See the section below for results and fixes.
