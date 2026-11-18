# Moonlighter Co-op Mod — Design

Date: 2026-11-18
Status: draft, awaiting approval

## Intent

Two friends play all of Moonlighter together, online: town, shop, dungeons, bed/days. It should feel like one shared playthrough, not two parallel games. One player hosts; the other joins over the internet without router setup.

## Goals

- 2-player online co-op for the full game loop (town, shop, dungeons, sleeping).
- Players are always in the same place. When one player goes through a dungeon door, both go to the new room. Same for town and scene changes.
- Shared state: one inventory, one pool of money (gold), one shop, one bed. A purchase by either player spends the shared gold.
- Separate health per player. In dungeons the HUD shows both players' health bars.
- Death in a dungeon turns that player into a ghost. A ghost can be revived at a dungeon healing pool. Reviving consumes the pool: it no longer heals anyone afterwards.
- Joining works through Steam (lobby + invite) with no port forwarding. Direct IP connect is a fallback.

## Non-goals

- More than 2 players.
- Split-screen or local co-op on one PC.
- Anti-cheat. Players are friends.
- Matchmaking with strangers or public lobbies.
- Console versions. Steam PC build 1.15.8 only.
- Separate per-player inventories, wallets, or progression.

## Anti-goals (would count as failure even if it "works")

- Players drifting apart: different rooms, different enemies, different item counts, different gold.
- Corrupting the host's save file, or touching the joining player's own save.
- Movement and combat that feel laggy for the joining player (input delay on their own character).
- Requiring port forwarding to play.

## Constraints

- Game: Moonlighter 1.15.8, Steam, `D:\SteamLibrary\steamapps\common\Moonlighter`.
- Engine: Unity 2019.2.7, Mono runtime (not IL2CPP). Game code is in `Moonlighter_Data\Managed\Assembly-CSharp.dll`.
- Mod loader: BepInEx 5.4.23.6 already installed; Harmony (HarmonyX) available. `Supports SRE: False` in the BepInEx log, so patches must not rely on `System.Reflection.Emit` at runtime beyond what HarmonyX/MonoMod handle.
- The mod ships as a BepInEx plugin DLL in `BepInEx\plugins\`. Both players install the same mod version; the mod refuses a connection on version mismatch.
- Networking library: the game already bundles Steamworks.NET in `Assembly-CSharp-firstpass.dll` (`SteamNetworking`, `SteamMatchmaking`, `SteamFriends` present) and initializes Steam via `DigitalSunGames.Platforms.SteamManager`. Use it; don't ship a second Steam API.
- The game is built for exactly one player. Relevant classes found so far: `HeroMerchant`, `HeroMerchantController`, `HeroMerchantStats`, `HeroMerchantInventory`, `HeroMerchantActions` (InControl input), `GameManager`, `DungeonManager`, `DungeonGenerator`, `DungeonDoor`, `CameraManager`, `HUDManager`, `GUIManager`, `ShopManager`, `TownManager`, `VillageManager`, `Enemy`, `EnemyBehaviour`, `PoolManager`. Exact responsibilities need confirming by decompiling.
- Decompiling and building need the .NET SDK and ILSpy (`ilspycmd`). Neither is installed yet; the user approved installing both.
- Testing: the user has one PC and one Steam account. Every stage must be testable on that one machine (see "Local test mode").
- DLC: Between Dimensions is installed on the host. Both players must own the same DLC; the mod checks at connect and refuses on mismatch. DLC content is fully in scope.

### Local test mode
- Before stage 1, the mod gets a test mode that runs a second copy of the game on the same PC, connected over loopback IP (127.0.0.1), without Steam networking.
- The second copy must start despite Steam already running the first one (e.g. skip Steam init or single-instance checks when a mod flag/launch argument is set). The second copy uses a separate, throwaway save location so it never touches the real saves.
- Steam lobby/invite code is still built, but can only be fully verified once a second account is available. Until then, loopback IP is the verified path.

### Menus and pause
- Opening a menu (inventory, pause, shop, crafting) does not freeze the shared world. The other player keeps playing, and a player in a menu can still be hit in a dungeon. Game systems that pause on menu open must be patched to keep running in co-op.

## Approaches

1. **Hybrid authority (chosen by the user).** Each player simulates their own character locally, so their movement and attacks are instant. The host is authoritative for everything shared: dungeon generation, room transitions, enemies (AI, health, death, drops), loot, inventory, gold, shop customers and prices, day/night, saving. The client renders the host's world and reports its own player's actions (position, animation, attacks, hits it landed, damage it took, item use). Trade-off: the client is trusted about its own hits. Acceptable between friends.
2. **Full host authority.** Client sends only input; host simulates both players. Perfect consistency, but the client feels network delay on every action, and the whole client game must be turned into a puppet. Rejected: violates the "not laggy" anti-goal and costs more.
3. **Deterministic lockstep.** Only inputs are exchanged; both simulate everything. Rejected: Unity physics and the game's RNG aren't deterministic across machines, so games would desync.

## How it works (decided parts)

### Roles and connection
- Host loads their save and opens co-op from a mod menu. The mod creates a Steam lobby (friends-only) and the host invites the friend via the Steam overlay.
- Client accepts the invite. The mod connects over Steam P2P (relay, no port forwarding).
- Fallback: host can listen on a UDP port; client enters IP:port. Only this path needs port forwarding.
- The host's save is the shared world. The client's own save files are never read or written while in co-op.

### Player 2
- On both machines, a second `HeroMerchant` instance represents the other player ("remote Will"). It is a visual/collision puppet driven by network state. It needs a visual difference from the local Will (builder's call: tint or outline).
- Each machine runs its own local Will with normal controls.
- Camera: one camera per machine, following that machine's local player. Because both players are always in the same room, this is enough.

### Same-room rule
- Room/scene transitions are host-decided. When either player triggers a door/exit, the request goes to the host; the host performs the transition and tells the client, which moves both players into the new room at the entry point.
- Same for town to dungeon, dungeon to town, entering the shop, and going to bed.

### Shared state
- Inventory, gold, shop shelves and prices, chests, and quest/progression flags live on the host.
- Client actions that change shared state (pick up, buy, sell, move items, set prices, use potions from inventory) are sent to the host as requests. The host applies them and broadcasts the result. The client's UI shows the host's state.
- One purchase by either player deducts the shared gold once.

### Health, death, ghosts
- Each player has their own health and stats. Each player has their own equipped weapons, armor and ring, taken from the shared inventory. (Claude's call: two Wills sharing one equipped sword makes no sense in combat.)
- Dungeon HUD shows both health bars.
- At 0 HP in a dungeon, that player becomes a ghost: no attacking, no taking damage, can't pick up items, still follows the same-room rule, visually see-through.
- A living player using a dungeon healing pool while the other is a ghost revives the ghost. The pool is then spent and heals nobody afterward.
- If both players are dead, the run ends using the game's normal death handling (normal item loss rules apply).

### Day cycle and saving
- Only the host's game saves. Sleeping/day advance happens when the host triggers it via the bed (builder's call on whether both must agree).

### Story, tutorial, cutscenes (Claude's call)
- Co-op can be started only from a save that has finished the intro/tutorial.
- Cutscenes and dialogue are driven by the host. The remote Will is hidden during cutscenes and reappears next to the host afterward. The client sees the cutscene but cannot advance it.

### Rejoin (Claude's call)
- "Rejoin" means a dropped client can reconnect while the host is in town. The host gets a full state transfer at that quiet point. Reconnecting mid-dungeon is out of scope.

## Delivery stages

Each stage is playable and tested in the real game before the next one starts:

1. Connection (Steam lobby + invite, IP fallback), and seeing each other walk around town.
2. Moving between dungeon rooms together (same-room rule, host-generated dungeon).
3. Enemies and combat synced.
4. Shared inventory, gold, shop.
5. Ghost/revive and both health bars.
6. Bed, day cycle, saving, and polish (disconnects, rejoin).

## Left to the builder

- Wire format, tick rate, interpolation, and which messages are reliable vs unreliable.
- How the remote Will is spawned and made visually distinct.
- Exact Harmony patch points, found by decompiling.
- Mod menu / UI for host and join.
- Disconnect handling (suggested default: client is returned to the main menu; host continues solo).
- Project layout and build setup (C# class library targeting .NET Framework 4.x, referencing the game's Managed DLLs and BepInEx core).

## Decisions added 2026-11-18 (user, before leaving)
- **Enemy toughness:** in co-op, enemies have **2x max health**; damage unchanged.
- **Shop:** **both players** can set prices and use the counter; the host's game validates every sale (no double sales).
- **Bed:** when the **host** sleeps, the day ends for both; the friend gets a notice first.
- **Unattended work:** when blocked, pick the safest option, record it in `handoff.md` under "Decisions to check", keep going. Stop at 5% usage left and write `handoff.md`.
