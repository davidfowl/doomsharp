# Progress

Last verified: **2026-10-13**, against merged `main` and its linked CI run. This distinguishes the
verified playable increment from full DOOM compatibility and physical-device
verification. **Full original-DOOM parity remains unfinished.**

## Merged

[Engine PR #1](https://github.com/davidfowl/doomsharp/pull/1) was squash-merged at
[`3f900864194b6dfeb1fd9c998973050314022464`](https://github.com/davidfowl/doomsharp/commit/3f900864194b6dfeb1fd9c998973050314022464).
`main` now contains a buildable clean-room C#/.NET 10 engine and Native AOT CI.
It uses the .NET platform/BCL and OS APIs, not third-party managed libraries or
game engines. Original DOOM source and proprietary WADs are not included.

The [status reconciliation PR #3](https://github.com/davidfowl/doomsharp/pull/3)
is also merged. This page describes capabilities available on `main`, not
unmerged branch claims. Working below means verified **within the stated scope**,
not complete original-DOOM behavior.

## Feature status

| Feature | Status | Verification and limits |
| --- | --- | --- |
| Original generated demo | Working | Actual Linux engine completed pickup, door, raised-floor crossing, combat and exit through OS-injected keyboard events |
| Classic WAD loading and BSP textured geometry | Working | All 68 Freedoom 0.13.0 maps load/render at player starts; walls, floors, ceilings and sky depth tested; no extended map formats |
| Basic pistol/shotgun loop | Working | Demo kill/ammo/pickup/restart checks pass; not a claim of exact damage/RNG or every weapon |
| Movement, collision and sector actions | Partial | Wall/step/door/lift/teleport/exit checks pass for selected cases; original physics and all mover/trigger variants unfinished |
| Actors, sprites, pickups and barrels | Partial | Transparency, drops and basic combat verified; monster states/attacks, infighting, boss behavior and some items/animations incomplete |
| HUD, weapon overlays and automap | Partial | HUD/weapon pixels rendered and patch offsets tested; automap code exists but has no dedicated end-to-end input verification |
| Native window presentation | Working | Bounded actual-engine desktop smoke runs pass on three OSes; interactive input coverage differs by platform below |
| DMX decoding and sound-effects mixer | Partial | Resampling checked and Linux mixed PCM submitted to ALSA null; physical audible playback and Windows/macOS audio unverified |
| Remaining weapons/powerups, stairs/crushers, switch textures and boss scripts | Not implemented | Required for original gameplay/campaign parity |
| Music/MUS, saves, menus, multiplayer and extended maps/mods | Not implemented | No end-to-end capability claimed |
| Complete authentic WAD campaigns | Unverified | Map rendering alone is not campaign completion; missing behaviors can block progression |

## In-flight work and known limits

No additional unmerged engine capability is claimed here. Future progress PRs
must update this page with their verification scope and evidence links, and keep
branch-only work separate from merged-main status.

Remaining compatibility work includes stairs/crushers, remaining mover variants,
all vanilla map specials and switch textures, boss scripts, exact monster states
and attacks/infighting, remaining weapons/powerups, telefragging, original physics,
music/MUS playback, saves, menus and multiplayer. These gaps can block complete
authentic WAD campaigns. Extended maps/mod formats are not implemented.

## Built vs. runtime-tested

The [merged-main CI run](https://github.com/davidfowl/doomsharp/actions/runs/38366537493)
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

## Reproduce

Run from the repository with the .NET 10 SDK. The demo and tests generate their
own original assets; no WAD download is required.

```sh
dotnet run -c Release -- --demo --no-audio
dotnet run -c Release -- --self-test
dotnet run -c Release -- --demo --headless --frames 35 --output demo.ppm
dotnet run -c Release -- --wad /path/to/game.wad --map E1M1 --no-audio
dotnet run -c Release -- --wad /path/to/freedoom1.wad --validate-maps
```

Publish on the matching host OS, not across OSes. Native compiler prerequisites
and controls are in [README](README.md).

| Host | Native AOT publish | Test and run the published executable |
| --- | --- | --- |
| Linux x64 | `dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64` | `./artifacts/linux-x64/DoomSharp --self-test` then `./artifacts/linux-x64/DoomSharp --demo --no-audio` |
| Windows x64 (PowerShell) | `dotnet publish -c Release -r win-x64 -o artifacts/win-x64` | `.\artifacts\win-x64\DoomSharp.exe --self-test` then `.\artifacts\win-x64\DoomSharp.exe --demo --no-audio` |
| macOS ARM64 | `dotnet publish -c Release -r osx-arm64 -o artifacts/osx-arm64` | `./artifacts/osx-arm64/DoomSharp --self-test` then `./artifacts/osx-arm64/DoomSharp --demo --no-audio` |

Linux desktop runs need a 24/32-bit X11 display (XWayland on Wayland); sound needs
ALSA. Windows uses built-in GDI/waveOut, and macOS uses AppKit/CoreGraphics/
AudioQueue from a graphical login session. Omit `--no-audio` to request sound
from the default device; device failures are reported, not silently ignored.

For authentic maps, supply a **lawfully obtained classic DOOM-format WAD** or
obtain [Freedoom 0.13.0](https://github.com/freedoom/freedoom/releases/tag/v0.13.0)
under its BSD license. Replace example paths and map names with your local files.
Owning a proprietary WAD does not permit redistribution; none is bundled.
`--validate-maps` checks loading/rendering, not campaign gameplay completion.

## Next milestones

- Complete vanilla map-special dispatch, mover variants, switches and boss actions.
- Implement remaining combat/weapon/powerup behavior and original gameplay semantics.
- Add music, saves and menus, then verify full campaigns with lawful user-supplied WADs.
- Verify interactive input and audible audio on physical Windows/Linux/macOS systems,
  and validate other CPU architectures separately.
