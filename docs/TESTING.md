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
