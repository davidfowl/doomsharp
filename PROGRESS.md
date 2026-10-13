# Progress

Snapshot: 2026-10-13. This separates merged work from implementation claims and
isolated platform probes; it is not a declaration of a working game.

## Merged

At base commit `20cfe046b127712ae52e36dfb07be8704c139f39`, `main` contains
README, LICENSE and `.gitignore` only. No engine, executable or build workflow is
merged. This documentation increment adds a visible status page and README link.

## Work in progress

[Engine PR #1](https://github.com/davidfowl/doomsharp/pull/1) is open, not merged.
Its author reports WAD loading, BSP rendering, collision, basic combat, enemies,
pickups, doors, exits, HUD, sound effects and native platform backends. These are
in-flight implementation claims, not capabilities available on `main`.

The PR author reports a Linux Native AOT publish and deterministic behavioral
checks. Windows/Linux/macOS CI checks were pending when this snapshot was taken.
Real-WAD corpus and native desktop integration verification remain in progress.
Full original-DOOM parity is not claimed: moving floors/lifts/stairs, teleporters,
all map specials, precise monster behavior, all weapons/powerups, music,
savegames and episode routing remain unfinished in the PR description.

## Built vs. runtime-tested

No merged engine build or game runtime has been verified on any platform.

| Platform / architecture | Build evidence | Runtime evidence |
| --- | --- | --- |
| Linux x64 | Isolated .NET 10 Native AOT support probe reported successful; engine publish reported by PR #1 author, CI pending | Support probe only: X11 framebuffer readback under Xvfb, synthetic key input, ALSA null PCM buffer submission |
| Windows x64 | Engine CI pending; no successful build verified here | Unverified |
| macOS arm64 | Engine CI pending; no successful build verified here | Unverified |
| Other OS / architectures | Unverified | Unverified |

The Linux probe is **not** actual-engine verification, a desktop playthrough,
physical keyboard input verification or audible game audio verification. Xvfb,
synthetic events and ALSA null output do not establish those outcomes.

## Next milestones

- Land a reviewed, buildable engine increment with explicit supported limits.
- Confirm platform builds with successful CI evidence.
- Verify actual-engine desktop input, rendering and audible audio on each target.
- Record real-WAD gameplay coverage and remaining compatibility gaps separately
  from deterministic checks and platform support probes.
