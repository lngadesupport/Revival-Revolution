# Private Match by room code (0.8.1 test branch)

The **PRIVATE MATCH** window is dedicated to room-code invitations and uses the fixed private API endpoint:

`http://26.226.57.19:8765`

Each player still creates or restores their own account. The endpoint is not editable in this test build.

## Update the host's local Ranked API

The computer hosting `26.226.57.19:8765` must run the server code from this feature branch, because the previous server build does not have the `/v1/rooms/start` and `/v1/rooms/connection-code` routes.

1. Stop the currently running server with Ctrl+C in its PowerShell window.
2. In the repository folder on the host PC, fetch and switch to `feature/0.8.1-radmin-room-start`.
3. Start it again with `scripts/start-radmin-ranked-host.ps1`.
4. Keep the same AppData SQLite database. The server migrates the new nullable connection-code column without deleting player accounts.

Do not start a second server while the old process still owns TCP 8765.

## Private room lifecycle

1. The **PRIVATE MATCH** page first shows the invitation screen.
2. The host creates a six-character code and shares it; the guest enters that code.
3. The API creates one private match and marks both room participants accepted automatically.
4. Both launchers replace the invitation screen with the VS lobby: profile photo and nickname on each side, with **VS** between them.
5. Only the authenticated host sees and can press **INICIAR PARTIDA**. The guest waits in the VS lobby.
6. Only the authenticated host can call `POST /v1/rooms/start`.
7. The host launches the game bootstrap and direct UDP peer runtime bound to its local Radmin IPv4 address, then publishes the MR1 code through `POST /v1/rooms/connection-code`.
8. The guest receives that code through `GET /v1/match`, launches the game bootstrap and joins the host over UDP port 8766.
9. Both clients observe the peer session status in `peer-status.json` and `peer-summary.txt`.

Players can upload a PNG/JPEG profile photo from the Private Match header. If no photo is set, the lobby displays nickname initials. The opponent photo is loaded through the authenticated avatar endpoint.

The endpoint is idempotent for the host. A guest cannot start another player's room. A private match has a two-hour setup expiry after the guest joins.

## Local firewall and game startup

Run `scripts/enable-radmin-peer-firewall.ps1` as Administrator on **both** PCs. It opens inbound UDP 8766 only from the Radmin 26.0.0.0/8 range. Keep both PCs connected to the same private Radmin network.

The Private Match bootstrap invokes `offline_launcher.py` to start/attach the game bridge. It launches `ufo.exe` with Unity's `-screen-fullscreen 1` startup argument so the interactive Metal Revolution Configuration selector is skipped and the game can start directly. The target PC must still be checked to confirm that this Unity build honors the argument.

## Important implementation boundary

The branch implements the invitation-first Private Match UI, profile-photo upload/display, the host-only VS start control, MR1 key-code exchange, game bootstrap on both clients, direct Radmin UDP peer startup, authenticated handshake, and coordinated local VS readiness. This has passed code/build checks only; no real two-PC game session has been run in this environment, so live gameplay, the Unity configuration bypass, and bridge compatibility are not yet certified.

The current peer runtime requires:
- a running `ufo.exe` with the netplay bridge attached and `netplay-pid.txt` available;
- the optional native synthetic preflight DLL is bypassed for this Radmin path; the authenticated direct UDP handshake is used as the transport preflight;
- both clients to exchange a valid MR1 Radmin endpoint/key code, complete the authenticated UDP handshake, and have the game bridge report VS readiness.

The Windows launcher publish includes the three non-empty bridge/overlay DLLs present in the repository, plus the Python bootstrap, injector, peer session, selection, and live-fighter modules. The repository's root `Assembly-CSharp.dll` is zero bytes and is intentionally excluded; the game uses its own managed assembly from its installed `ufo_Data/Managed` directory. The Radmin path skips the optional `MetalGekko.dll` synthetic preflight, but the live game bridge and two-PC UDP/game flow still require real validation.

## Validation

Run the backend checks with:

```powershell
python -m unittest discover -s tests -p "test_*.py"
```

The Ranked CI runs 61 backend/protocol tests, including a real localhost UDP HMAC handshake and coordinated VS-state test using a fake game bridge. The Windows workflow builds and verifies the self-contained launcher package, its Python runtime scripts, the three non-empty bridge/overlay DLLs, and the Radmin firewall helper. These automated checks do not replace a real two-PC Radmin/game/rollback test.
