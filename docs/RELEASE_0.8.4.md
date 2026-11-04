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

## Current implementation status

- The settings window has dedicated tabs for General, Controller, Video & Graphics, Audio, Subtitles, and About.
- Controller, video, audio, and subtitle presentation preferences are stored in the existing per-user JSON settings file under `%LocalAppData%\\RevivalRevolution`. Existing settings files remain compatible because new fields have defaults.
- The graphics tab includes a read-only discovery action. It checks a bounded set of Unity/game configuration locations and relevant registry metadata; it does not write game files or change registry values.
- Resolution choices (1280×720, 1600×900, 1920×1080, 2560×1440, and 3840×2160) and fullscreen are passed to the Unity player on the next offline launch using documented command-line arguments. The default remains fullscreen to preserve the previous launch behavior.
- The controller tab has live read-only XInput detection and telemetry for connected XInput-compatible devices (buttons, triggers, and both sticks). Generic HID-only devices may not be detected. Dead-zone, sensitivity, curve, inversion, and vibration preferences are saved, but are not yet applied to game input.
- Graphics quality presets, V-Sync, FPS cap (60–240), FPS overlay, audio levels, and subtitle rendering preferences are saved only; their in-game application remains pending verification.
- The Mod List page/navigation is absent from the current launcher tree. The inspected startup and offline bootstrap paths do not launch a separate Metal Revolution Configuration app or TenProtect executable. Official protection/runtime dependencies are not removed or bypassed.

## Known implementation prerequisites

- The repository does not currently include authoritative character-intro dialogue/transcripts and timestamps for the target game patch. Do not fabricate translations or subtitle timings.
- Controller dead-zone/sensitivity changes must not be presented as functional until an input path actually consumes and applies them in-game.
- FPS options must not be presented as effective until a supported game config/API mechanism is identified and verified against the target patch.
- The launcher now performs version-agnostic installation inspection: it detects the executable/data folder and reads product/file version metadata when present. Unknown versions are reported, not rejected.
- The launcher should open with the currently installed game without requiring the future patch. Patch-specific subtitle data, content mappings, and verified graphics controls remain gated until the patch is available.

## Validation

CI build and smoke-test results must be recorded before any build is described as ready. This branch remains development-only; do not merge or publish without explicit authorization.
