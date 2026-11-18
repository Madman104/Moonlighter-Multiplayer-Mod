# Moonlighter Co-op — Stage 0+1 Implementation Plan (Connect + See Each Other in Town)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two copies of Moonlighter on one PC connect over loopback, pass a version/DLC handshake, and each shows the other player's Will walking around the same scene. Steam lobby/invite is built but only smoke-tested.

**Architecture:** Pure, Unity-free logic (wire protocol, framing, handshake rules, TCP transport, interpolation, sprite id table) lives in `Coop.Core` (netstandard2.0) and is unit-tested with xUnit on .NET 8. `Coop.Plugin` (net472 BepInEx 5 plugin) holds everything touching Unity or the game: Harmony patches, test-client mode, the co-op manager, the player sampler and the remote-player puppet. The remote player is a **visual-only puppet** (SpriteRenderers mirroring the other Will), never a second `HeroMerchant`: `HeroMerchant.Instance` uses `FindObjectOfType<HeroMerchant>()` and has 1613 call sites, so a second `HeroMerchant` would hijack them.

**Tech Stack:** C#, .NET SDK 8.0.428, BepInEx 5.4.23.6, HarmonyX (`0Harmony.dll` in `BepInEx\core`), Unity 2019.2.7 Mono, Steamworks.NET (bundled in game's `Assembly-CSharp-firstpass.dll`), xUnit.

**Spec:** `docs/2026-11-18-moonlighter-coop-design.md`

## Global Constraints

- Game dir: `D:\SteamLibrary\steamapps\common\Moonlighter`; managed DLLs: `Moonlighter_Data\Managed`. Build property `GameDir` defaults to this path.
- Moonlighter build 1.15.8, Steam PC only.
- Exactly 2 players. Host = the player whose save is the world.
- The client's own real saves are never read or written in co-op; the test client uses only `<persistentDataPath>\CoopTestClient\`.
- Never write to the game's own files except `BepInEx\plugins\MoonlighterCoop\` (deploy target).
- Connection refused on mod version mismatch or DLC ownership mismatch.
- No port forwarding needed on the Steam path. TCP direct-IP is the fallback and the local-test path. Default port `27460`.
- `BepInEx` log shows `Supports SRE: False`: no `System.Reflection.Emit` in mod code.
- Decompiled game code (scratchpad) is reference only; never copy it into the repo.

## Review Focus

1. **Second game instance won't start or kills the first** (Steam already running the game, Unity single-instance). Expected: test client starts with `--coop-test-client`, host keeps running. Pinned by Task 5 Step 6 manual check.
2. **Test client touches real saves or Steam Cloud.** Expected: zero writes outside `CoopTestClient\`. Pinned by Task 5 tests on `TestSavePaths` plus Step 6 check of Steam Cloud file timestamps.
3. **Partial / coalesced TCP reads** (one `Read` holds half a frame or three frames). Expected: every frame decoded exactly once. Pinned by Task 2 `FrameBuffer` tests.
4. **Peer disconnects mid-session or never answers Hello.** Expected: other side shows "Disconnected" / "Timed out" within 10 s, puppet removed, game keeps running. Pinned by Task 3 timeout test and Task 6 manual check.
5. **Players in different scenes** (one in shop, one in town). Expected: puppet hidden, not drawn at a stale position in the wrong scene. Pinned by Task 7 `PuppetVisibility` test.

---

## File Structure

```
MoonlighterCoop.sln
Directory.Build.props                      GameDir property, LangVersion 9
src/Coop.Core/Coop.Core.csproj             netstandard2.0
  Protocol/MessageType.cs                  enum MessageType : byte
  Protocol/Messages.cs                     Hello, Welcome, Reject, PlayerState, SpriteLayer, Ping
  Protocol/MessageCodec.cs                 encode/decode payloads
  Protocol/FrameBuffer.cs                  length-prefixed framing
  Session/HandshakeRules.cs                version/DLC validation
  Session/PeerSession.cs                   connection state machine + timeouts
  Net/ITransport.cs                        transport abstraction
  Net/TcpTransport.cs                      TCP host/client
  Sync/SnapshotInterpolator.cs             position interpolation
  Sync/SpriteTable.cs                      sprite name <-> ushort id
  Sync/PuppetVisibility.cs                 same-scene rule
  TestMode/TestSavePaths.cs                test-client save path redirect
tests/Coop.Core.Tests/Coop.Core.Tests.csproj  net8.0 xunit
src/Coop.Plugin/Coop.Plugin.csproj         net472, refs BepInEx + game DLLs (Private=false)
  CoopPlugin.cs                            BaseUnityPlugin entry
  CoopConfig.cs                            BepInEx config bindings
  TestMode/TestClientMode.cs               detects --coop-test-client
  TestMode/TestSaveController.cs           IPersistenFilesController for test client
  Patches/PlatformPatches.cs               Platform.Init / SteamManager.Awake patches
  Runtime/CoopManager.cs                   owns transport+session, pumps per frame
  Runtime/CoopWindow.cs                    F8 IMGUI host/join window
  Runtime/GameInfo.cs                      mod/game version, owned DLC names, scene id
  Sync/LocalPlayerSampler.cs               captures local Will's sprites
  Sync/RemotePlayerPuppet.cs               draws other Will
  Steam/SteamP2PTransport.cs               ITransport over SteamNetworking
  Steam/SteamLobby.cs                      lobby create/invite/join
tools/deploy.ps1                           build + copy to BepInEx\plugins\MoonlighterCoop
tools/launch-test-client.ps1               seed test save + start 2nd instance
```

---

### Task 1: Repo, solution, empty plugin that loads

**Files:**
- Create: `MoonlighterCoop.sln`, `Directory.Build.props`, `.gitignore`, `src/Coop.Core/Coop.Core.csproj`, `tests/Coop.Core.Tests/Coop.Core.Tests.csproj`, `src/Coop.Plugin/Coop.Plugin.csproj`, `src/Coop.Plugin/CoopPlugin.cs`, `tools/deploy.ps1`

**Interfaces:**
- Produces: `CoopPlugin : BaseUnityPlugin` with `[BepInPlugin("com.coop.moonlighter", "Moonlighter Co-op", "0.1.0")]`; `public const string ModVersion = "0.1.0";` `internal static ManualLogSource Log`.

- [ ] **Step 1:** `git init` at the session folder root; `.gitignore` excludes `bin/`, `obj/`, `*.user`.
- [ ] **Step 2:** Create projects. `Coop.Plugin.csproj`: `TargetFramework net472`, package `Microsoft.NETFramework.ReferenceAssemblies`, `<Reference>` with `<Private>false</Private>` to `$(GameDir)\BepInEx\core\BepInEx.dll`, `0Harmony.dll`, and from `$(GameDir)\Moonlighter_Data\Managed`: `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.IMGUIModule.dll`, `UnityEngine.InputLegacyModule.dll`, `UnityEngine.TextRenderingModule.dll`. ProjectReference to Coop.Core. Tests reference Coop.Core + xunit.
- [ ] **Step 3:** `CoopPlugin.Awake()` logs `Moonlighter Co-op 0.1.0 loaded`.
- [ ] **Step 4:** `tools/deploy.ps1`: `dotnet build -c Release`, copy `Coop.Plugin.dll` + `Coop.Core.dll` to `$GameDir\BepInEx\plugins\MoonlighterCoop\`. Refuses to run if `Moonlighter.exe` process is running.
- [ ] **Step 5: Verify.** Run `dotnet test` (0 tests, exit 0) and `tools/deploy.ps1`; launch game via Steam; `BepInEx\LogOutput.log` contains `1 plugins to load` and `Moonlighter Co-op 0.1.0 loaded`.
- [ ] **Step 6: Commit** `chore: scaffold co-op mod solution`.

---

### Task 2: Wire protocol and framing

**Files:**
- Create: `src/Coop.Core/Protocol/{MessageType,Messages,MessageCodec,FrameBuffer}.cs`
- Test: `tests/Coop.Core.Tests/Protocol/{MessageCodecTests,FrameBufferTests}.cs`

**Interfaces:**
- Produces:
  - `enum MessageType : byte { Hello = 1, Welcome = 2, Reject = 3, PlayerState = 4, Ping = 5, Pong = 6, Bye = 7 }`
  - `sealed class Hello { string ModVersion; string GameVersion; string[] OwnedDlc; }`
  - `sealed class Welcome { byte PlayerIndex; }` (host = 0, client = 1)
  - `sealed class Reject { RejectReason Reason; string Detail; }`, `enum RejectReason : byte { ModVersion = 1, GameVersion = 2, Dlc = 3, Full = 4 }`
  - `sealed class SpriteLayer { byte Slot; ushort SpriteId; bool Visible; bool FlipX; short SortingOrder; float LocalX; float LocalY; }`
  - `sealed class PlayerState { uint Tick; int SceneId; float X; float Y; float Z; SpriteLayer[] Layers; }`
  - `sealed class Ping { uint Nonce; }` (Pong reuses `Ping` shape)
  - `static class MessageCodec { byte[] Encode(MessageType t, object msg); (MessageType type, object msg) Decode(byte[] payload); }` — payload = 1 type byte + body, `BinaryWriter` little-endian, strings length-prefixed UTF-8. Unknown type byte throws `InvalidDataException`.
  - `sealed class FrameBuffer { static byte[] Frame(byte[] payload); void Append(byte[] data, int offset, int count); bool TryReadFrame(out byte[] payload); }` — 4-byte little-endian length prefix; frames > 65536 bytes throw `InvalidDataException`.

- [ ] **Step 1: Write failing tests.** `MessageCodecTests`: round-trip each message type with non-default values (e.g. `Hello("0.1.0","1.15.8",["Between Dimensions"])`, `PlayerState` with 3 layers incl. `FlipX=true`, `SortingOrder=-5`); `Decode` of `new byte[]{99}` throws `InvalidDataException`. `FrameBufferTests`: one frame split across 3 `Append` calls yields exactly one payload; three frames in one `Append` yield three payloads in order; empty buffer `TryReadFrame` returns false; length prefix `70000` throws.
- [ ] **Step 2:** `dotnet test --filter Protocol` → FAIL (types missing).
- [ ] **Step 3:** Implement the four files to the signatures above.
- [ ] **Step 4:** `dotnet test --filter Protocol` → PASS.
- [ ] **Step 5: Commit** `feat(core): wire protocol and framing`.

---

### Task 3: Handshake rules and peer session

**Files:**
- Create: `src/Coop.Core/Session/{HandshakeRules,PeerSession}.cs`, `src/Coop.Core/Net/ITransport.cs`
- Test: `tests/Coop.Core.Tests/Session/{HandshakeRulesTests,PeerSessionTests}.cs`

**Interfaces:**
- Consumes: Task 2 messages and `MessageCodec`.
- Produces:
  - `interface ITransport : IDisposable { bool IsHost; void Send(byte[] payload, bool reliable); bool TryReceive(out byte[] payload); event Action Connected; event Action<string> Disconnected; void Poll(); }`
  - `static class HandshakeRules { Reject Validate(Hello host, Hello client, bool ignoreDlc); }` — returns null when compatible. Order: ModVersion exact match, GameVersion exact match, then DLC set equality (order-insensitive) unless `ignoreDlc`.
  - `enum SessionState { Idle, Handshaking, Connected, Closed }`
  - `sealed class PeerSession { PeerSession(ITransport t, Hello local, bool ignoreDlc, Func<double> clock); SessionState State; string CloseReason; event Action<PlayerState> PlayerStateReceived; void Tick(); void SendPlayerState(PlayerState s); }` — client sends `Hello` on `Connected`; host validates, answers `Welcome` or `Reject` then closes. `Tick()` polls transport, dispatches, sends `Ping` every 1 s, closes with `"Timed out"` after 10 s without any received message, closes with `Reject.Detail` on Reject. `PlayerState` is sent unreliable; everything else reliable.

- [ ] **Step 1: Failing tests.** `HandshakeRulesTests`: equal hellos → null; mod `0.1.0` vs `0.1.1` → `RejectReason.ModVersion`; DLC `["A"]` vs `[]` → `Dlc`; same with `ignoreDlc:true` → null; DLC `["A","B"]` vs `["B","A"]` → null. `PeerSessionTests` with an in-memory `FakeTransportPair` (test helper): client+host reach `Connected`; mismatched mod version → client `Closed`, `CloseReason` contains `"mod version"`; advancing fake clock 10.5 s with no traffic → `Closed`, `"Timed out"`; `SendPlayerState` on host raises `PlayerStateReceived` on client with equal `Tick`.
- [ ] **Step 2:** `dotnet test --filter Session` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test --filter Session` → PASS.
- [ ] **Step 5: Commit** `feat(core): handshake and peer session`.

---

### Task 4: TCP transport

**Files:**
- Create: `src/Coop.Core/Net/TcpTransport.cs`
- Test: `tests/Coop.Core.Tests/Net/TcpTransportTests.cs`

**Interfaces:**
- Consumes: `ITransport`, `FrameBuffer`.
- Produces: `sealed class TcpTransport : ITransport { static TcpTransport Host(int port); static TcpTransport Connect(string host, int port); }` — non-blocking sockets, `NoDelay = true`, accepts one client (a second connection is closed immediately), all I/O happens in `Poll()` (no threads, so Unity main thread owns everything). `reliable` flag ignored (TCP).

- [ ] **Step 1: Failing tests.** Host on port 0-style free port (helper finds free port), connect to `127.0.0.1`; poll both until `Connected` fires (≤ 2 s); client sends 100 payloads of varying size 1..5000 bytes, host receives all 100 in order; host `Dispose()` → client `Disconnected` fires within 2 s; a second client connecting is rejected while the first stays connected.
- [ ] **Step 2:** `dotnet test --filter Net` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test --filter Net` → PASS.
- [ ] **Step 5: Commit** `feat(core): tcp transport`.

---

### Task 5: Local test-client mode (second instance, isolated saves)

**Files:**
- Create: `src/Coop.Core/TestMode/TestSavePaths.cs`, `src/Coop.Plugin/TestMode/{TestClientMode,TestSaveController}.cs`, `src/Coop.Plugin/Patches/PlatformPatches.cs`, `tools/launch-test-client.ps1`
- Modify: `src/Coop.Plugin/CoopPlugin.cs` (apply Harmony patches in `Awake`)
- Test: `tests/Coop.Core.Tests/TestMode/TestSavePathsTests.cs`

**Interfaces:**
- Produces:
  - `static class TestSavePaths { const string FolderName = "CoopTestClient"; string Resolve(string persistentDataPath, string file); }` — returns `persistentDataPath\CoopTestClient\file`; throws `ArgumentException` if `file` is rooted or contains `..` segments.
  - `static class TestClientMode { bool Active; }` — true when `Environment.GetCommandLineArgs()` contains `--coop-test-client`.
  - `sealed class TestSaveController : DigitalSunGames.Platforms.IPersistenFilesController` — all sync and async members implemented against `TestSavePaths.Resolve`; async variants run synchronously and invoke the callback (the game's `DRMFreeCloudSaveController` leaves async members empty, so it can't be reused). Creates the folder on first write.
  - `PlatformPatches`: Harmony prefix on `DigitalSunGames.Platforms.Platform.Init` — when `TestClientMode.Active`, set Platform's private fields (`m_Controller = new DRMFreeManager()`, `m_CloudSave = new TestSaveController()`, `m_Achievements` = a no-op `IAchievementController`, `m_FileSystem = new StandaloneFileSystemController()`, `m_StoreController` = existing `SteamStoreController` only if it makes no Steam calls on construction, else a no-op `IStoreController`, `m_DLC = new DRMFreeDLCController()`), set `CurrentPlatform = SupportedPlatforms.DRM_FREE`, `m_Initialized = true`, skip original. Prefix on `SteamManager.Awake` → skip original when active (Initialized stays false). Use `AccessTools.Field` for private fields.
  - DLC in test mode: `PeerSession` is created with `ignoreDlc: true` when `TestClientMode.Active` (DRM-free DLC controller reports DLC as not owned).

- [ ] **Step 1: Failing tests** for `TestSavePaths`: `Resolve(@"C:\p", "slot1.sav")` == `@"C:\p\CoopTestClient\slot1.sav"`; `"..\\x"` throws; `@"C:\x"` throws.
- [ ] **Step 2:** `dotnet test --filter TestMode` → FAIL. **Step 3:** implement `TestSavePaths`. **Step 4:** → PASS.
- [ ] **Step 5:** Implement plugin-side files. `IAchievementController` / `IStoreController` members: read their decompiled interfaces and implement no-ops.
- [ ] **Step 6:** `tools/launch-test-client.ps1`: resolves `persistentDataPath` (`%USERPROFILE%\AppData\LocalLow\<company>\Moonlighter` — confirm from first run's `Player.log`), copies the host's Steam Cloud save files from `<Steam>\userdata\<id>\606150\remote\` into `...\CoopTestClient\` **only if that folder is empty** (read-only copy of real saves), then starts `Moonlighter.exe --coop-test-client` directly (not via Steam).
- [ ] **Step 7: Manual verify.** Deploy. Start game via Steam (host). Run `tools/launch-test-client.ps1`. Both windows run; client loads the copied save and reaches town. Save in the client, quit it, and check: files changed under `CoopTestClient\` only; Steam `remote\` files' LastWriteTime unchanged. If Unity refuses a second instance, record the exact error and add a `-force-single-instance`-style workaround before continuing.
- [ ] **Step 8: Commit** `feat(plugin): local test-client mode with isolated saves`.

---

### Task 6: Co-op manager, config, F8 window (loopback connect)

**Files:**
- Create: `src/Coop.Plugin/{CoopConfig.cs, Runtime/CoopManager.cs, Runtime/CoopWindow.cs, Runtime/GameInfo.cs}`
- Modify: `src/Coop.Plugin/CoopPlugin.cs`

**Interfaces:**
- Consumes: `PeerSession`, `TcpTransport`, `Hello`, `TestClientMode`.
- Produces:
  - `CoopConfig`: `ConfigEntry<int> Port` (default `27460`), `ConfigEntry<string> LastHostAddress` (default `127.0.0.1`), `ConfigEntry<KeyCode> WindowKey` (default `F8`).
  - `static class GameInfo { string GameVersion => Application.version; string[] OwnedDlcNames(); int CurrentSceneId(); }` — DLC names from `Platform.DLC.GetLoadedDLCs(h => h.isOwned)` → `info` name field; scene id = build index of the active gameplay scene (town/dungeon/shop are loaded additively — pick the highest-index loaded scene that isn't the persistent/main-menu one; confirm in decompiled `GameManager` scene ids).
  - `sealed class CoopManager : MonoBehaviour { static CoopManager Instance; PeerSession Session; void HostTcp(int port); void JoinTcp(string address, int port); void HostSteam(); void Leave(); }` — created by `CoopPlugin` on a `DontDestroyOnLoad` GameObject; `Update()` calls `Session.Tick()`. On close, logs reason and raises `event Action<string> SessionClosed`.
  - `CoopWindow`: IMGUI window toggled by `WindowKey`. Shows state (`Idle / Connecting / Connected as Host|Client / Closed: <reason>`), buttons `Host (IP)`, `Join (IP)` with address field, `Host (Steam)` (wired in Task 8), `Leave`.

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Manual verify.** Host instance: F8 → Host (IP). Test client: F8 → Join `127.0.0.1`. Both show `Connected`. Quit test client: host shows `Closed: Disconnected` within 10 s and keeps running. Temporarily set client `ModVersion` to `0.0.0` via a debug build constant: client shows `Closed: ... mod version`.
- [ ] **Step 3: Commit** `feat(plugin): co-op manager and connect window`.

---

### Task 7: See the other player (sampler, sprite table, puppet)

**Files:**
- Create: `src/Coop.Core/Sync/{SpriteTable,SnapshotInterpolator,PuppetVisibility}.cs`, `src/Coop.Plugin/Sync/{LocalPlayerSampler,RemotePlayerPuppet}.cs`
- Modify: `src/Coop.Plugin/Runtime/CoopManager.cs`
- Test: `tests/Coop.Core.Tests/Sync/{SpriteTableTests,SnapshotInterpolatorTests,PuppetVisibilityTests}.cs`

**Interfaces:**
- Consumes: `PlayerState`, `SpriteLayer`, `PeerSession.SendPlayerState`, `PlayerStateReceived`.
- Produces:
  - `sealed class SpriteTable { SpriteTable(IEnumerable<string> names); ushort GetId(string name); string GetName(ushort id); }` — names de-duplicated, sorted ordinal, id = index + 1; unknown name → 0; id 0 → null. Same game build on both sides gives identical tables.
  - `sealed class SnapshotInterpolator { SnapshotInterpolator(double delaySeconds = 0.1); void Add(double time, float x, float y, float z); bool TrySample(double now, out float x, out float y, out float z); }` — buffers up to 32 snapshots; samples at `now - delay`, linear between neighbours; clamps to newest when starved (no extrapolation); false when empty.
  - `static class PuppetVisibility { bool ShouldShow(int localSceneId, int remoteSceneId, SessionState state); }` — true only when `Connected` and scene ids equal.
  - `LocalPlayerSampler`: at 20 Hz, reads all `SpriteRenderer`s under `HeroMerchant.Instance` (slot = index in a stable order from `GetComponentsInChildren<SpriteRenderer>(true)` captured once per scene load), builds `PlayerState` with sprite ids from a `SpriteTable` built once from `Resources.FindObjectsOfTypeAll<Sprite>()` names.
  - `RemotePlayerPuppet : MonoBehaviour`: one child `SpriteRenderer` per slot, copies sprite/flip/visible/sortingOrder/local offset from the latest state, position from `SnapshotInterpolator`, tint `new Color(0.75f, 0.85f, 1f)`, label `P1`/`P2` above head. Has no `HeroMerchant` component and no colliders in this stage.

- [ ] **Step 1: Failing tests.** `SpriteTable`: `["b","a","a"]` → `GetId("a")==1`, `GetId("b")==2`, `GetId("zz")==0`, `GetName(0)==null`. `SnapshotInterpolator` (delay 0.1): add (0.0, x=0) and (0.1, x=10); `TrySample(0.15)` → x≈5; `TrySample(5.0)` → x=10 (clamped); empty → false. `PuppetVisibility`: same scene + Connected → true; different scene → false; same scene + Closed → false.
- [ ] **Step 2:** `dotnet test --filter Sync` → FAIL. **Step 3:** implement Core files. **Step 4:** → PASS.
- [ ] **Step 5:** Implement sampler + puppet; wire into `CoopManager` (sample/send while Connected; puppet created on Connected, destroyed on close; visibility via `PuppetVisibility`).
- [ ] **Step 6: Manual verify.** Both instances in town, connected. Walk the host Will: tinted puppet in client window walks, faces the right way, animates (walk cycle visible, not frozen), smooth at 20 Hz. Repeat in the other direction. Client enters the shop while host stays in town: puppet disappears in both windows; returns when both are in town. Disconnect: puppet removed.
- [ ] **Step 7: Commit** `feat: remote player puppet in same scene`.

---

### Task 8: Steam lobby + P2P transport (build + smoke test)

**Files:**
- Create: `src/Coop.Plugin/Steam/{SteamP2PTransport,SteamLobby}.cs`
- Modify: `src/Coop.Plugin/Runtime/{CoopManager,CoopWindow}.cs`

**Interfaces:**
- Consumes: `ITransport`, `FrameBuffer` not needed (Steam packets are message-bounded).
- Produces:
  - `sealed class SteamP2PTransport : ITransport { SteamP2PTransport(CSteamID remote, bool isHost); }` — `SteamNetworking.SendP2PPacket` with `EP2PSend.k_EP2PSendReliable` / `k_EP2PSendUnreliableNoDelay`, channel `0`; `Poll()` drains `IsP2PPacketAvailable`/`ReadP2PPacket`; host accepts `P2PSessionRequest_t` only from the lobby member; `P2PSessionConnectFail_t` → `Disconnected`. Read the bundled Steamworks.NET signatures from the decompiled firstpass DLL before writing.
  - `sealed class SteamLobby { void CreateFriendsOnly(); void OpenInviteOverlay(); event Action<CSteamID> PeerJoined; }` — `SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2)`, `SteamFriends.ActivateGameOverlayInviteDialog(lobbyId)`, handles `GameLobbyJoinRequested_t` (join from invite while running) and `+connect_lobby <id>` launch arg (join from invite when game was closed); lobby data key `coop_mod_version` = `ModVersion`.
  - Disabled (button greyed, tooltip "Steam not available in test client") when `!SteamManager.Instance.Initialized`.

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Smoke verify (one account).** Host via Steam: F8 → Host (Steam): log shows `Lobby created <id>`; Steam overlay invite dialog opens listing friends. Full join cannot be verified until a second account is available; leave a checklist line in `docs/TESTING.md` for that.
- [ ] **Step 3: Commit** `feat(plugin): steam lobby and p2p transport`.

---

### Task 9: Stage 1 acceptance

- [ ] **Step 1:** `dotnet test` → all pass.
- [ ] **Step 2:** Fresh deploy; run the Task 5–7 manual checks end to end in one sitting; record results in `docs/TESTING.md` (date, pass/fail per check, anything odd).
- [ ] **Step 3: Commit** `docs: stage 1 test results`.
