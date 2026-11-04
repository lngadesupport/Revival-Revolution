# Revival Revolution Launcher 0.8.3

## Changes

- Replace the Scheduled Tasks registration (`schtasks /Create`) for the local Radmin host agent with a per-user startup entry under `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`.
- Start the Radmin host agent in the background and wait for the local API health endpoint before marking the host ready.
- Keep Windows Firewall configuration separate; creating a new firewall rule may still require Windows elevation.
- Refresh launcher navigation: remove the legacy Mod List and Discord entries, add a Radmin network status entry, and update resource copy.
- Set launcher assembly and file version to `0.8.3.0`.

## Multiplayer constraints

Private Match remains restricted to the shared Radmin VPN network. Peer transport remains IPv4 Radmin / UDP 8766; no public internet fallback, STUN, or relay is introduced.

Ranked matchmaking remains beta. ELO and competitive results remain disabled until an independent trusted result authority is available.

## Validation

The Windows CI workflow builds the launcher, runs integrity smoke tests, publishes the self-contained x64 build, and verifies the published runtime files. CI does not prove a real two-PC Radmin match or closed-bridge result integrity.
