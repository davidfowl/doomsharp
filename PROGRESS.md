# Progress

Snapshot: 2026-10-13, after the first engine merge. This distinguishes the
verified playable increment from full DOOM compatibility and physical-device
verification. **Full original-DOOM parity remains unfinished.**

## Merged

[Engine PR #1](https://github.com/davidfowl/doomsharp/pull/1) was squash-merged at
[`3f900864194b6dfeb1fd9c998973050314022464`](https://github.com/davidfowl/doomsharp/commit/3f900864194b6dfeb1fd9c998973050314022464).
`main` now contains a buildable clean-room C#/.NET 10 engine and Native AOT CI.
It uses the .NET platform/BCL and OS APIs, not third-party managed libraries or
game engines. Original DOOM source and proprietary WADs are not included.

Implemented: classic WAD loading, BSP-based textured walls/floors/ceilings,
transparent sprites, collision/steps, pistol/shotgun combat, simplified enemies,
pickups/keys/barrels, common doors, tagged lifts/floor triggers, teleports,
normal/secret exit routing, HUD/automap, weapon overlays and native PCM effects.
The original generated `--demo` has a playable pickup/door/combat/exit loop.
See [README](README.md) for build/run commands, controls and precise limitations.

## Work in progress

Remaining compatibility work includes stairs/crushers, remaining mover variants,
all vanilla map specials and switch textures, boss scripts, exact monster states
and attacks/infighting, remaining weapons/powerups, telefragging, original physics,
music/MUS playback, saves, menus and multiplayer. These gaps can block complete
authentic WAD campaigns. Extended maps/mod formats are not implemented.

## Built vs. runtime-tested

The [merged-main CI run](https://github.com/davidfowl/doomsharp/actions/runs/38365995731)
passes on all three target operating systems and retains native binary and
framebuffer artifacts. Each job runs 37 behavioral checks, a Native AOT publish,
tests the native executable, renders a headless demo, and exercises bounded
native desktop presentation.

| Platform / architecture | Build evidence | Runtime evidence |
| --- | --- | --- |
| Linux x64 | Native AOT and behavioral tests pass | Actual engine's X11 presentation under Xvfb; OS-injected keyboard demo playthrough, mouse capture and ALSA null PCM submission |
| Windows x64 | Native AOT and behavioral tests pass | Bounded Win32/GDI desktop presentation in CI; interactive input and audio unverified |
| macOS arm64 | Native AOT and behavioral tests pass | Bounded AppKit/CoreGraphics desktop presentation in CI; interactive input and audio unverified |
| Other OS / architectures | Unverified | Unverified |

The Linux actual-engine integration run collected the shotgun, opened a door,
crossed a raised-floor portal, killed the enemy and used the exit through OS
keyboard events: health 88, one kill, shotgun acquired, level completed. The
mixer submitted 13 effects to an ALSA null device. This supersedes the earlier
isolated support probe, but does **not** establish physical keyboard/mouse or
audible sound behavior. Other architectures remain unverified.

All 36 Freedoom 0.13.0 Phase 1 and 32 Phase 2 maps load and render at their player
starts with zero missing textures. The official asset license and archive
checksum were checked; no downloaded assets are bundled. This is map
parser/renderer evidence, **not** proof that every campaign is playable to completion.

## Next milestones

- Complete vanilla map-special dispatch, mover variants, switches and boss actions.
- Implement remaining combat/weapon/powerup behavior and original gameplay semantics.
- Add music, saves and menus, then verify full campaigns with lawful user-supplied WADs.
- Verify interactive input and audible audio on physical Windows/Linux/macOS systems,
  and validate other CPU architectures separately.
