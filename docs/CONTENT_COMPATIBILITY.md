# Game content and community-port compatibility

## Goals

The launcher must remain stable while the game receives new characters, outfits, stages,
mobile-to-PC ports, balance changes, and community content. The launcher must not assume
that the original PC roster is permanent.

This contract separates three concerns:

1. **Game installation** — the actual game executable, Unity data, managed assemblies and
   asset bundles. These remain game-owned files.
2. **Community/user content** — imported ports, mods, profiles and backups. These are
   user-owned and must never be silently replaced by an official launcher update.
3. **Compatibility metadata** — a small description of the game build, bridge protocol,
   roster and content pack. This is used to prevent incompatible peers from starting a match.

## Launcher ownership and safe paths

The official launcher updater must not manage files under:

- `Metal Revolution/` and `ufo_Data/` — original game installation.
- `Launcher/Mods/` — community mods and character ports.
- `Launcher/UserContent/` — imported assets and user content.
- `Launcher/Content/` — user-installed content packs.
- `Launcher/Profiles/` — user compatibility profiles.
- `Launcher/Backups/` — user-created recovery copies.
- `Launcher/Config/`, `Launcher/Logs/`, `Launcher/Runtime/`, `Launcher/Downloads/`,
  `Launcher/UpdateStaging/` and `Launcher/UpdateBackup/` — local launcher state.

These directories are deliberately outside the official updater's ownership. A future
installer/content manager must use explicit import, backup, conflict detection and rollback;
it must not expand the official update manifest into these locations.

## Selection catalog contract

The game bridge currently produces `selection-catalog.json` for each local session.
It must enumerate the characters, outfit IDs and stage IDs that the game can actually
select. The network layer no longer assumes a 128-entry roster: current bounded limits are
4,096 characters, 1,024 outfits per character, 4,096 stages and 65,536 total catalog entries.
The limits prevent unbounded input while leaving room for substantial roster growth.

Catalog IDs must be non-negative 31-bit integers and unique. Character names are display
metadata and do not define identity. Outfit IDs are preserved as IDs; they are not inferred
from character order.

## Optional compatibility metadata

Newer bridge builds may add a `compatibility` object to `selection-catalog.json`:

```json
{
  "compatibility": {
    "game_build": "pc-1.2.0",
    "content_revision": "community-mobile-port-7",
    "content_hash": "64-character SHA-256 of the normalized installed content set",
    "asset_pack_hash": "64-character SHA-256 of the asset pack or empty string",
    "bridge_protocol": 2
  }
}
```

Fields:

- `game_build`: game executable/data build identifier (max 96 characters).
- `content_revision`: human-readable content-pack revision (max 96 characters).
- `content_hash`: SHA-256 of a canonical inventory of gameplay-relevant installed content.
- `asset_pack_hash`: optional SHA-256 of the normalized asset pack.
- `bridge_protocol`: positive integer identifying the bridge/catalog contract.

When metadata is omitted, the network layer supplies a legacy default so existing bridge
builds continue to work. Once both peers report metadata, the compatibility profile is part
of the selection-catalog fingerprint. Equal character IDs are therefore not enough: peers
with different game builds, content revisions, content hashes, asset packs or bridge
protocols are rejected before the shared selection is committed.

The bridge should calculate hashes from a deterministic, sorted inventory of gameplay
content. Do not hash temporary files, logs, caches, machine-specific absolute paths or
session-specific data. Do not use file modification times as compatibility identity.

## Required behavior for a new character port

Before a port is considered multiplayer-compatible:

1. Preserve the original PC installation and test the port in a separate copy.
2. Enumerate every new character and outfit with stable IDs in the bridge catalog.
3. Include all gameplay-relevant assets and dependencies in the content fingerprint.
4. Confirm both players have the same content fingerprint before selection commit.
5. Test local character loading, costume loading, stage loading and a full match.
6. Test rollback/replay behavior and repeat the test after a game or content update.
7. Keep a backup and provide a clean uninstall/restore procedure.

A character appearing in the selection UI is not proof that its animations, hitboxes,
scripts, assets or rollback behavior are compatible.

## Current implementation boundary

The Python selection layer now supports larger rosters and fingerprints optional
compatibility metadata. The launcher updater protects user-managed content directories.
However, the shipped native bridge is a compiled DLL and currently does not expose a
verified content-hash producer in this repository snapshot. Until that bridge is updated,
the new metadata fields remain optional and legacy catalog behavior continues.

This work does **not** automatically install or convert mobile assets, patch the game,
guarantee arbitrary mods will load, or prove real two-PC gameplay. Those capabilities
require game-specific bridge and asset-loader support plus testing against the actual port.
