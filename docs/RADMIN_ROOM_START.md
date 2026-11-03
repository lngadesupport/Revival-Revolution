# Radmin private-room start flow (0.8.1 test branch)

The ONLINE window uses the fixed private API endpoint:

`http://26.226.57.19:8765`

Each player still creates or restores their own account. The endpoint is not editable in this test build.

## Private room lifecycle

1. The host creates a six-character room code.
2. The guest enters the code.
3. The API creates one private match and marks both room participants accepted automatically.
4. The host sees the **INICIAR PARTIDA** button. The guest sees a waiting message.
5. Only the authenticated host can call `POST /v1/rooms/start`.
6. Both clients observe `room_status=starting` while polling `GET /v1/match`.

The endpoint is idempotent for the host. A guest cannot start another player's room. A private match has a two-hour setup expiry after the guest joins.

## Important implementation boundary

This change implements the server-side room transition and launcher UI signal only. It does **not** claim that a game session starts or that the two players are connected.

The current peer runtime requires:
- a running `ufo.exe` with the netplay bridge attached and `netplay-pid.txt` available;
- `MetalGekko.dll` beside `peer_session.py` for the native synthetic peer preflight;
- both clients to exchange a valid MR1 Radmin endpoint/key code and complete the authenticated UDP handshake.

At the time of this change, `MetalGekko.dll` is not present in the feature branch repository tree (the root contains `MetalNetplayBridgeV8.dll`, `MetalOfflineBridgeImGuiV3.dll`, and `MetalImGuiOverlay.dll`). The repository's `Assembly-CSharp.dll` root file is zero bytes and must not be packaged as a valid assembly. Therefore this branch must not be described as a fully working online game build until the correct missing runtime dependency is supplied and the two-PC UDP/game-bridge flow is tested.

## Validation

Run the backend checks with:

```powershell
python -m unittest discover -s tests -p "test_*.py"
```

The Windows GitHub Actions workflow also builds and publishes the launcher artifact for this feature branch. A passing build does not replace a real two-PC game/P2P test.
