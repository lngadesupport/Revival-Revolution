# Revival Revolution Launcher 0.8.4 — development scope

This branch is the isolated 0.8.4 workstream. It is based on the 0.8.3 launcher refresh and does not change `main`, stable releases, or the Radmin/Ranked implementation.

## Scope

- Refresh the Offline landing experience while retaining the existing Metal Revolution background.
- Restore the launcher window icon using the existing project logo asset.
- Expand launcher settings for controller, video/graphics, audio, and subtitles.
- Add Brazilian Portuguese subtitles for character entrance dialogue only after the exact source dialogue and timing from the target game patch are available.
- Investigate genuine game-supported graphics configuration, including frame-rate limits, before exposing controls that claim to change in-game behavior.
- Remove the legacy Mod List navigation/page and any related launcher-only entry points.
- Keep multiplayer/Radmin/Ranked behavior unchanged in this version.

## Startup and protected components

The current offline bootstrap starts `ufo.exe` directly with fullscreen arguments; it does not invoke a separate Metal Revolution Configuration executable. The launcher must not disable, remove, patch, or bypass TenProtect or any official anti-cheat/security component. Any configuration flow changes must preserve the game's required protection and normal startup dependencies.

## Known implementation prerequisites

- The repository does not currently include authoritative character-intro dialogue/transcripts and timestamps for the target game patch. Do not fabricate translations or subtitle timings.
- Controller dead-zone/sensitivity changes must not be presented as functional until an input path actually consumes and applies them in-game.
- FPS options must not be presented as effective until a supported game config/API mechanism is identified and verified against the target patch.
- The launcher now performs version-agnostic installation inspection: it detects the executable/data folder and reads product/file version metadata when present. Unknown versions are reported, not rejected.
- The launcher should open with the currently installed game without requiring the future patch. Patch-specific subtitle data, content mappings, and verified graphics controls remain gated until the patch is available.

## Validation

CI build and smoke-test results must be recorded before any build is described as ready. This branch remains development-only; do not merge or publish without explicit authorization.
