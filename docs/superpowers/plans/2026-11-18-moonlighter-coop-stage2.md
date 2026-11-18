# Moonlighter Co-op — Stage 2 Implementation Plan (Dungeon Rooms Together)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Both players enter dungeons together, on an identical floor layout, always in the same room, change floors together and return to town together.

**Architecture:** The host generates each floor normally. On `DungeonGenerator.OnGeneratedDungeon` it builds `new DungeonSaveInfo(DungeonManager.Instance)` (the game's own mid-dungeon save), serializes it with the game's `FsJSONManager`, and sends it. On the client a Harmony prefix replaces `DungeonGenerator.GenerateDungeon` with "wait for the host's layout for this culture/floor, then `GenerateSavedDungeon(info)`". Travel is mirrored: Harmony prefixes on `GameManager.LoadDungeonScene`, `GameManager.LoadTownSceneFromDungeon`, `DungeonManager.ChangeRoomTo` and `DungeonManager.SpecialChangeRoomTo` send a travel message, and the peer replays the same call (guarded against echo). If both players trigger different travel within 0.5 s, the host's wins (`TravelArbiter`).

**Spec:** `docs/2026-11-18-moonlighter-coop-design.md` (Same-room rule; Delivery stage 2)

## Global Constraints
- Everything from the stage 1 plan's Global Constraints still applies.
- Rooms are identified by grid `(DungeonRoom.x, DungeonRoom.y)`; doors by index in `room.roomDoors` of the room the door belongs to.
- Enemies, loot, chests and HP are NOT synced in this stage (stage 3/4). Each game spawns its own enemies.
- The client's own save slot is still written by the game on dungeon entry (`SaveSlotOnGoToDungeon`); fixing that belongs to stage 6.

## Review Focus
1. **Client starts loading before the layout arrives**, or the layout arrives first. Expected: client waits (max 30 s) and uses the matching culture/floor layout; never an older floor's. Pinned by `FloorLayoutStore` tests.
2. **Both players go through different doors in the same frame.** Expected: both end in the host's choice. Pinned by `TravelArbiter` tests.
3. **Echo loops:** replaying a remote room change must not send it back. Pinned by the `RemoteApply` guard; checked in the two-copy smoke run log (one RoomChange per door use).
4. **Layout bigger than 64 KB.** Expected: delivered. Pinned by `FrameBuffer` max raised to 1 MiB + test.
5. **Session drops mid-dungeon** while the client waits for a layout. Expected: client falls back to generating its own floor, never hangs on the loading screen.

---

### Task 1: Protocol — game messages, 1 MiB frames
- Add `MessageType.FloorLayout = 9`, `DungeonTravel = 10`, `TownTravel = 11`, `RoomChange = 12`.
- Messages: `FloorLayout { byte Culture; byte Floor; string Json; }`, `DungeonTravel { byte Culture; byte Level; bool Replay; }`, `TownTravel { bool FromEmblem; }`, `RoomChange { short FromX; short FromY; short ToX; short ToY; sbyte DoorIndex; bool Special; bool TeleportToLastDoor; }`.
- `FrameBuffer.MaxFrameSize = 1 << 20`.
- `PeerSession.SendGame(MessageType type, object msg)` (reliable, Connected only) and `event Action<MessageType, object> GameMessageReceived` for types >= 9.
- Tests: round-trip each; 200 KB FloorLayout through codec and FrameBuffer; GameMessage dispatch only when Connected.

### Task 2: FloorLayoutStore, TravelArbiter, LocationId for dungeons
- `FloorLayoutStore { void Put(int culture, int floor, string json); bool TryTake(int culture, int floor, out string json); void Clear(); }` — keeps only the newest layout per (culture, floor); `TryTake` removes it.
- `TravelArbiter { TravelArbiter(bool isHost, double window = 0.5); void LocalTravel(double now); bool ShouldApplyRemote(double now); }` — client always applies remote; host ignores a remote travel if it travelled locally within `window`.
- `LocationId.From(bool hasWill, bool townLoaded, bool dungeonLoaded, int culture, int floor)` — town → 1; dungeon → `100 + culture * 10 + floor`; otherwise -1.
- Tests for each.

### Task 3: Plugin — layout send/receive and client generation
- `Dungeon/FloorSync.cs`: on `OnGeneratedDungeon` (host, connected): serialize `new DungeonSaveInfo(DungeonManager.Instance)` via `FsJSONManager.Serialize` → `fsData.ToString()`; send `FloorLayout`; log size.
- Harmony prefix on `DungeonGenerator.GenerateDungeon(culture, level, test)`: if client and connected and `!test` → `__result = ClientFloor(culture, level)`; `ClientFloor` waits for the layout (30 s, or until session closes), deserializes with `FsJSONManager.Deserialize<DungeonSaveInfo>`, `yield return GenerateSavedDungeon(info)`; on timeout falls back to the original generator (guard flag to skip our prefix).
- `GameInfo.CurrentSceneId` uses the new `LocationId.From` with `DungeonManager.Instance.culture/level`.

### Task 4: Plugin — travel mirroring
- `Dungeon/TravelSync.cs` with `static bool ApplyingRemote`.
- Prefixes: `GameManager.LoadDungeonScene` → send `DungeonTravel`; `GameManager.LoadTownSceneFromDungeon` → `TownTravel`; `DungeonManager.ChangeRoomTo(to, door)` → `RoomChange` (door's room x,y + index); `DungeonManager.SpecialChangeRoomTo(to, teleport)` → `RoomChange{Special=true}`. All skip sending when `ApplyingRemote`, and record `TravelArbiter.LocalTravel`.
- On receive (if `ShouldApplyRemote`): set `ApplyingRemote`, call the same game method with resolved objects, clear flag. Unresolvable room/door → log warning, ignore.

### Task 5: Smoke + review
- Two test copies (auto host/join) start, connect, no errors. Deploy. Update `docs/TESTING.md` with stage 2 manual checks. Final review subagent; fix Critical/Important.
