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

[Executable-only assurance PR #7](https://github.com/davidfowl/doomsharp/pull/7)
removed the isolated harness and its invocation. Its
[three-OS native executable CI](https://github.com/davidfowl/doomsharp/actions/runs/38369523543)
passes; Linux includes OS-input demo/restart and startup-failure qualification.

[Lift PR #10](https://github.com/davidfowl/doomsharp/pull/10) and
[X11 Tab PR #11](https://github.com/davidfowl/doomsharp/pull/11) are merged;
their owner-local before/after evidence is described below, not confused with
historical failures or independent downloaded-CI qualification.

## Feature status

| Feature | Status | Verification and limits |
| --- | --- | --- |
| Original generated demo | Working (limited scope) | Reproducible native Linux OS-input pickup/door/combat/exit and live restart via the published orchestration below; not a campaign |
| Classic WAD loading and BSP textured geometry | Working (limited coverage) | CI renders the generated demo; engine PR #1 reports 68 Freedoom map-start renders, not campaign completion; no extended map formats |
| Basic pistol/shotgun loop | Partial | Native demo observes shotgun pickup/kill/live restart; no current exact damage/RNG or complete pistol/all-weapon acceptance |
| Chaingun pickup/fire/inventory | Partial | Type2002 and chaingunner drop, key4, shared bullets, four-tick shots/two-shot pairs and supplied body/flash patches; dedicated Linux executable scenarios below, not exact original timing/RNG/visual parity or broad platform gameplay |
| Movement, collision and sector actions | Partial | Native door/lift paths and selected boundaries; tagged locked fast-door99/133-137 action scope has dedicated executable scenarios below, not every mover/trigger |
| Map progression and damaging exits | Partial | Native Linux input observes E1M1 to E1M2 with inventory/health retained; local classic-map scenarios qualify sector-11 damage/exit, fresh dead-player next-map state and finale/missing-map stop; not original campaign completion |
| Actors, sprites, pickups and barrels | Partial | Native demo observes pickup and one enemy kill; exact states/attacks, infighting, boss behavior and many item/visual cases remain unfinished or unverified |
| HUD, weapon overlays and automap | Partial | Actual executable exports HUD/weapon frames; local Linux native Tab on/off plus W/Ctrl controls qualify automap input, not original visuals or other-platform interactive behavior |
| Native window presentation | Working | Bounded actual-engine desktop smoke runs pass on three OSes; interactive input coverage differs by platform below |
| DMX decoding and sound-effects mixer | Partial | Native Linux playthrough submits effects to ALSA null; exact resampling/audio comparison, audible playback and Windows/macOS audio unverified |
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
Sector-11 exit handling is implemented, but the boss actions needed to reach
some original finale floors remain absent. Damaging-floor timing is approximate.

## Built vs. runtime-tested

The [earlier merged-main CI](https://github.com/davidfowl/doomsharp/actions/runs/38367324903)
established three-OS AOT builds and bounded no-audio presentation, not gameplay.
The former isolated 37-check harness and startup shortcut are now removed.
**No unit tests or direct engine-method qualification remain.**

The [current workflow](.github/workflows/build.yml) publishes and runs the
native game for headless and bounded desktop scenarios. Linux also launches
the real game with [OS-input orchestration](scripts/native-demo.py): two genuine
startup rejections, completed demo and live restart. Local native qualification
observed health 88, one kill, shotgun acquired and exit completed; restart
returned to the start with health 100 and no shotgun. Complete frames, logs and
the executable hash are retained, including artifacts when CI fails.
This is executable E2E through virtual OS facilities, not physical-device
verification, a visual/timing oracle or original-map campaign acceptance.

Separate local native-game qualification retained the pre-fix sector-11 failure:
walking into the floor reduced health to zero but left `finished=False`.
Nine actual executable scenarios now cover 20-point damage, completion exactly
at health 10, living health/shotgun carryover, dead-player health/inventory reset
at a fresh E1M2 spawn, natural exit, E1M8 stop despite other available maps,
missing-next-map stop, replay and a harmless-floor control. Input is OS keyboard
events; original fixture WAD variants are prepared before startup, not running
game-state manipulation. Frames, diagnostics, input/executable hashes and
outcomes remain worker-local, not a public raw packet or CI sector corpus.
This closes one exit-path gap, **not** original E1M8/campaign qualification.

The native lift step-off failure published in
[PR #9](https://github.com/davidfowl/doomsharp/pull/9) was reproduced on integrated
source `6302152c0b1c11ecba583b6d4053f1bab373fbd8`: all five lowering/wait/carry/
stable-top/height gates passed, but prolonged backward input remained at X=-4;
game exit0 was correctly runner exit1, not lift success.
Player support now uses the radius-16 footprint across traversable portals;
it remains at the upper floor until the body clears, instead of dropping at the
center crossing and being mistaken for a new 64-unit upward step. Moving-floor
headroom/occupancy use the same touched sectors; grounded secrets/damage require
contact with the center sector's floor. Existing wall/24-unit-step/56-unit-body
collision rules and instantaneous falling model are retained.

The unchanged focused runner plus [full-cycle orchestration](scripts/native-lift-cycle.py)
qualify lowering, boarding, stationary wait, automatic carry, stable height64
(tolerance2), successful X<=-32 step-off, retrigger and second return/step-off.
The latter compares the first cleared native frame with the same build's focused
passing pose before issuing the second activation. This is a static original
fixture oracle, not an original-game visual oracle or complete lift compatibility.
Reproduction and output-directory rules are in [README](README.md).
Two deliberately planned fresh native processes each completed both cycles;
both returned to X=-111.14 with health100. Their cleared native frames matched
the focused accepted pose exactly. These are local executable results; current
CI remains the three-OS native demo/presentation scope, not a lift corpus.

Linux Tab failure was reproduced again after the lift merge on
`ab7fab8ead6dcfb3f171942e312e30d189a81296`. The actual X server mapped keycode23
to `XK_Tab`0xff09 for press/release, but all three captures remained 3D (automap
background fraction0). Native W moved X=-64 to13.14 and Ctrl changed rendered
ammo50 to48. Game exit0 was runner exit1, not automap success.
The X11 backend now normalizes Tab/ISO_Left_Tab to the shared ASCII Tab key
before both press and release dispatch; adjacent keys and Windows/macOS mappings
are unchanged. [Native orchestration](scripts/native-tab.py) observes ordinary
Tab on/off and W/Ctrl controls through the actual game, with the five unchanged
thresholds and commands in README. Two planned fresh processes qualify this
path; raw evidence is worker-local, not public downloadable media or CI coverage.
The integrated native lift, demo and boundary/progression regressions remain
separate checks rather than being inferred from Tab success.

Independent qualification was coordinator-reported against frozen main
`7c6e3ce687693f0478e67a3efa13a51b4c9c49e2`, using downloaded Linux CI
[artifact11803281901](https://github.com/davidfowl/doomsharp/actions/runs/38379934406/artifacts/11803281901)
from [run38379934406](https://github.com/davidfowl/doomsharp/actions/runs/38379934406).
Its ELF SHA256 is
`c612a195dccd3eeda337f37c8a88f5e41c1164cddd655111c9871ea81a659f53`,
distinct from owner-local binaries. One focused6 prerequisite and one full13
lift run passed (both cycles, step-off X=-111.14); one genuine Tab0xff09/keycode23
five-gate run passed (on99.8831%, off0%, W X8.86, ammo50->48). This is independently
reported virtual-Xvfb/XTest/framebuffer evidence; its raw worker-local packet was
not inspected here, and it does not establish physical input. The same frozen
qualifier's natural licensed real-map exit/next-map route remains separate and
is not inferred to have passed from silence or these synthetic observations.

Tagged key-locked fast-opening switch types99/133-137 were missing from the
activation dispatch. A fresh native baseline on the above main, with a matching
blue card and type133, remained at X45.14 against a closed remote door (required
X>=192); game exit0 was collector exit1. The implementation reuses key inventory,
tag selection, neighboring-ceiling target and door movers. It requires front-side
use and a matching-color card/skull, leaves SR actions repeatable, consumes S1
only after activation, does not restart an active mover, and retains existing
normal-door speeds. An unmatched tag has an explicit gameplay diagnostic.
[Native orchestration](scripts/native-locked-doors.py) and README publish the
22 distinct positive/negative scenarios, unchanged measurable thresholds, exact
input schedules and frame/process/hash retention. Generation prepares original
WADs before startup; it never calls engine methods or injects live state.
These are local Linux executable results, not existing CI's locked-door corpus,
natural-map progression proof, switch-texture parity or every door family.
Broad Windows/macOS gameplay, physical input and audible playback remain open.

Chaingun type2002 was absent on main `cd921a8ef15d911df53676ad44dce098a1a28645`.
A fresh main Native AOT process diagnosed the unsupported thing; ordinary W
input over the pickup left visible ammo50 and pistol artwork (game exit0,
collector exit1). A collector's earlier kill-HUD slash-delimiter error is
separately retained, not misreported as a product failure.
The bounded implementation shares pistol bullets (cap200), grants20 for a placed
weapon/10 for a chaingunner drop, supports key4 alongside existing1/2, completes
a two-shot pair on release and has four35Hz ticks between shots. New bursts have
an accurate initial pair; held refire adds horizontal spread. Actual user-WAD
`CHGG`/`CHGF` patches and `DSPISTOL` are used, with explicit missing-asset
diagnostics. Empty/unowned inputs give visible feedback without damage or
ammunition underflow. Alive map changes retain owned/selected weapon and ammo;
restart/dead-player changes restore the pistol/default inventory.

[Native weapon orchestration](scripts/native-chaingun.py) requires15 distinct
fresh full-game processes with original pre-start fixture WADs, native OS input,
visible HUD/weapon/flash frames and ordinary inventory output. README documents
the exact commands and positive/negative gates, including cap/duplicates,
pistol/shotgun preservation, first-pair versus held-spread combat, one/zero ammo,
release/cadence and all carry/reset paths. Original color-coded assets distinguish
weapon phases rather than standing in for original artwork verification.
Audio cases exercise effects and ALSA null PCM submission/missing-sound feedback,
not audible playback. Raw outcomes/frames/hashes remain worker-local; public tool
source is not a public raw packet. Existing three-OS CI's native smoke/demo scope
is unchanged, and the independent frozen natural-route qualifier is not repinned
or duplicated. Automatic empty-weapon/fist fallback, raising/lowering, original
autoaim/RNG/physics and full original visual/timing comparison remain unfinished.
This increment does not establish a natural-map exit/campaign or all-weapon
compatibility; those and physical/broad-OS acceptance remain open.

| Platform / architecture | Build evidence | Runtime evidence |
| --- | --- | --- |
| Linux x64 | Native AOT publish | Actual game: headless/X11 frames, OS-injected demo/live restart and ALSA null submission; physical input/audio unverified |
| Windows x64 | Native AOT publish | Bounded Win32/GDI desktop presentation; interactive input and audio unverified |
| macOS arm64 | Native AOT publish | Bounded AppKit/CoreGraphics desktop presentation; interactive input and audio unverified |
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
Those older raw packets remain worker-local; current demo orchestration and CI
artifact retention make its narrower qualification reproducible.
These synthetic runs do **not** establish physical keyboard/mouse, audible
sound or original-map full gameplay. Other architectures remain unverified.

Engine PR #1 reports all 36 Freedoom 0.13.0 Phase 1 and 32 Phase 2 maps loaded
and rendered at their player starts with zero missing textures, with the asset
license and archive checksum checked. This worker-reported corpus result is
not reproduced by CI; no downloaded assets are bundled. Map parser/renderer
coverage is **not** proof of original-map gameplay or campaign completion.

## Reproduce

Run from the repository with the .NET 10 SDK. The demo contains original assets;
no WAD download is required. Native qualification operates on the published
executable, not engine methods.

```sh
dotnet run -c Release -- --demo --no-audio
dotnet run -c Release -- --demo --headless --frames 35 --output demo.ppm
dotnet run -c Release -- --wad /path/to/game.wad --map E1M1 --no-audio
dotnet run -c Release -- --wad /path/to/freedoom1.wad --validate-maps
```

Publish on the matching host OS, not across OSes. Native compiler prerequisites
and controls are in [README](README.md).

| Host | Native AOT publish | Run the published executable |
| --- | --- | --- |
| Linux x64 | `dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64` | `./artifacts/linux-x64/DoomSharp --demo --no-audio` |
| Windows x64 (PowerShell) | `dotnet publish -c Release -r win-x64 -o artifacts/win-x64` | `.\artifacts\win-x64\DoomSharp.exe --demo --no-audio` |
| macOS ARM64 | `dotnet publish -c Release -r osx-arm64 -o artifacts/osx-arm64` | `./artifacts/osx-arm64/DoomSharp --demo --no-audio` |

Linux OS-input qualification (development-only Python stdlib, Xvfb and XTest):

```sh
xvfb-run -a python3 scripts/native-demo.py artifacts/linux-x64/DoomSharp
```

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
