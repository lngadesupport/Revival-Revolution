# Radmin private-room start flow (0.8.1 test branch)

The ONLINE window uses the fixed private API endpoint:

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

1. The host creates a six-character room code.
2. The guest enters the code.
3. The API creates one private match and marks both room participants accepted automatically.
4. The host sees the **INICIAR PARTIDA** button. The guest sees a waiting message.
5. Only the authenticated host can call `POST /v1/rooms/start`.
6. The host launches the game bootstrap and direct UDP peer runtime bound to its local Radmin IPv4 address, then publishes the MR1 code through `POST /v1/rooms/connection-code`.
7. The guest receives that code through `GET /v1/match`, launches the game bootstrap and joins the host over UDP port 8766.
8. Both clients observe the peer session status in `peer-status.json` and `peer-summary.txt`.

The endpoint is idempotent for the host. A guest cannot start another player's room. A private match has a two-hour setup expiry after the guest joins.

## Local firewall and game startup

Run `scripts/enable-radmin-peer-firewall.ps1` as Administrator on **both** PCs. It opens inbound UDP 8766 only from the Radmin 26.0.0.0/8 range. Keep both PCs connected to the same private Radmin network.

The ONLINE bootstrap invokes `offline_launcher.py` to start/attach the game bridge. If the game's configuration window appears, press its Play button so the Mono runtime can load and the bridge can attach.

## Important implementation boundary

The branch now wires the full launch sequence in code: the host start transition, MR1 key-code exchange, game bootstrap on both clients, direct Radmin UDP peer startup, authenticated handshake, and coordinated local VS readiness. This has passed code/build checks only; no real two-PC game session has been run in this environment, so live gameplay and bridge compatibility are not yet certified.

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

The Windows GitHub Actions workflow also builds and publishes the launcher artifact for this feature branch. A passing build does not replace a real two-PC game/P2P test.
