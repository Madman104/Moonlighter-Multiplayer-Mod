# Stage 3 (enemies and combat) — research notes

Findings from the decompiled game code (`DungeonRoom.cs`, `Enemy.cs`, `EnemyStats.cs`). Nothing is built yet.

## How a room spawns enemies
- `DungeonRoom.SpawnEnemies()` (~line 789) picks the room's enemy pack (`currentSpawnPack`). That value comes from `RoomSaveInfo.choosenEnemyPack`, so after stage 2 both games pick the **same pack**.
- It walks `enemyPack.spawns` in order and calls `SpawnEnemy(spawn, spawn.index /* tile */, isChampion)`. Each spawned enemy is appended to `room.roomEnemies`.
  - **Same order on both PCs**, so an enemy can be identified as `(room.x, room.y, index in roomEnemies)`.
- **Random part:** the champion. The game rolls `Random.Range(0,1)` against `championProb`, then picks a random spawn index. The host should send its champion index per room, and the client should spawn with it. Patch `SpawnEnemies` on the client so it uses the host's value.
- Other room randomness: `currentSpawnPack` set via `Random.Range` (~line 276) in `DungeonRoom` setup. Already overridden by the saved pack.

## Enemy state
- `Enemy.DealDamageToEnemy(float attackStrength, EnemyDamageReceiver, EnemyDamageReceivedType)` (~line 328) is where a hit is applied. It's the hook for reporting the client's hits to the host.
- `EnemyStats.CurrentHealth` setter (~line 62) raises `OnHealthChanged` / `OnHealthDecreased` and runs death when it reaches 0. Hook it, or set it, to mirror health.
- `DungeonRoom.AreAllEnemiesDefeated()` / `KillAllEnemies()` control whether the room's doors open.
- `Enemy.DropItemsOnDeath()` creates loot. That's stage 4 (shared items), but the host should own drops.

## Proposed stage 3 approach (hybrid, as in the design)
1. The host runs enemy AI. The client turns off AI on its enemies (the `EnemyBehaviour` component) and moves them to the host's position/animation snapshots: per room, roughly 10–20 Hz, using `SnapshotInterpolator` and the sprite-sync code from `RemotePlayerPuppet`.
2. The client's own hits are applied locally for instant feedback and sent to the host as `EnemyHit{room x,y, enemy index, damage}`. The host applies them and broadcasts health and death.
3. Enemy attacks hurt whichever Will they touch, on each player's own game. Each player owns their own health (design: separate health bars).
4. Room clear and door opening follow the host: the host sends `RoomCleared(x,y)`.
5. Open question to check first: enemy projectiles (many classes under `*Behaviour`). They spawn from AI, so on the client they'll come from mirrored animation events or not at all. Probably drive them from host "attack started" events.
