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
| Original generated demo | Working (worker-reported gameplay) | Linux OS-injected pickup/door/combat/exit reported in engine PR #1; independent synthetic qualification is worker-local, not a public packet |
| Classic WAD loading and BSP textured geometry | Working (limited coverage) | CI renders the generated demo; engine PR #1 reports 68 Freedoom map-start renders, not campaign completion; no extended map formats |
| Basic pistol/shotgun loop | Working (limited coverage) | Isolated demo kill/ammo/pickup/restart checks pass; OS-input gameplay worker-reported; not exact damage/RNG or every weapon |
| Movement, collision and sector actions | Partial | Isolated wall/step/door/lift/teleport/exit checks pass for selected cases, not game E2E; original physics and all mover/trigger variants unfinished |
| Actors, sprites, pickups and barrels | Partial | Transparency, drops and basic combat verified; monster states/attacks, infighting, boss behavior and some items/animations incomplete |
| HUD, weapon overlays and automap | Partial | HUD/weapon pixels rendered and patch offsets tested; automap code exists but has no dedicated end-to-end input verification |
| Native window presentation | Working | Bounded actual-engine desktop smoke runs pass on three OSes; interactive input coverage differs by platform below |
| DMX decoding and sound-effects mixer | Partial | Isolated resampling check passes; Linux ALSA null PCM submission worker-reported; physical audible playback and Windows/macOS audio unverified |
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

The [confirmed merged-main CI run](https://github.com/davidfowl/doomsharp/actions/runs/38367324903)
passes on all three target operating systems and retains native binary and
framebuffer artifacts. It ran at docs commit
[`6f7a7ed807814de9bcbe0c08ad90ee12b0f0ca75`](https://github.com/davidfowl/doomsharp/commit/6f7a7ed807814de9bcbe0c08ad90ee12b0f0ca75).
Each job runs `--self-test` before and after Native AOT publishing, renders a
35-frame headless demo, and exercises 35-frame native desktop presentation with
`--no-audio` (Linux under Xvfb). The 37 self-test checks call engine methods
directly, bypassing native input/window/audio. **These isolated checks and
presentation smoke are not actual-game E2E.** CI does not inject gameplay input,
exercise audio or validate the Freedoom corpus.

| Platform / architecture | Build evidence | Runtime evidence |
| --- | --- | --- |
| Linux x64 | Native AOT and isolated behavioral checks pass | CI: bounded X11 presentation under Xvfb, no audio/input automation; separate OS-injected demo, mouse capture and ALSA null output worker-reported |
| Windows x64 | Native AOT and behavioral tests pass | Bounded Win32/GDI desktop presentation in CI; interactive input and audio unverified |
| macOS arm64 | Native AOT and behavioral tests pass | Bounded AppKit/CoreGraphics desktop presentation in CI; interactive input and audio unverified |
| Other OS / architectures | Unverified | Unverified |

The [engine PR #1 report](https://github.com/davidfowl/doomsharp/pull/1) describes
a separate Linux actual-engine integration run using OS keyboard events:
shotgun pickup, door, combat and exit; health 88, one kill and level completed.
The engine worker also reported 13 effects submitted to ALSA null. These are
worker-reported results, not outcomes of the linked CI presentation smoke.
Independent synthetic OS-input collision/shotgun/kill/exit/restart qualification
was worker-reported at engine head
[`72c525746d705c80aaf76e1a014b0e04f97c3d9f`](https://github.com/davidfowl/doomsharp/commit/72c525746d705c80aaf76e1a014b0e04f97c3d9f).
Its qualification document/archive are worker-local and were not independently
inspected for this docs update; no public evidence packet is linked here.
Separate [synthetic fixture tooling](tools/e2e-fixtures/README.md) is now merged
in [PR #5](https://github.com/davidfowl/doomsharp/pull/5), with reproduction
instructions and worker-reported outcomes; generated evidence is not committed.
Accessible raw qualification evidence remains an integration gap.
These synthetic runs do **not** establish physical keyboard/mouse, audible
sound or original-map full gameplay. Other architectures remain unverified.

Engine PR #1 reports all 36 Freedoom 0.13.0 Phase 1 and 32 Phase 2 maps loaded
and rendered at their player starts with zero missing textures, with the asset
license and archive checksum checked. This worker-reported corpus result is
not reproduced by CI; no downloaded assets are bundled. Map parser/renderer
coverage is **not** proof of original-map gameplay or campaign completion.

## Reproduce

Run from the repository with the .NET 10 SDK. The demo and tests generate their
own original assets; no WAD download is required. The existing `--self-test`
commands below reproduce isolated checks, not game E2E or the separate
worker-reported OS-input/corpus qualifications.

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
