# DoomSharp

**Current status: [what works and platform verification](PROGRESS.md).**

A clean-room, dependency-free C# experiment in building a DOOM-compatible engine
with .NET Native AOT. This repository does not contain or derive from the original
DOOM source code and does not distribute proprietary game assets.

**This is a playable first increment, not a complete DOOM replacement.** It can
load classic binary WAD maps, render textured sector geometry and actors, and run
a single-player combat loop. The bundled `--demo` is an original generated
two-room encounter: collect a shotgun, open the door, defeat the enemy and use
the far wall to exit. No downloaded assets are needed for the demo.

## Build and run

Install the .NET 10 SDK. There are no NuGet package references or game engines;
the only managed dependency is the .NET platform/BCL. Native AOT publishing uses
the SDK's compiler packs and your OS's native build toolchain.

```sh
dotnet run -c Release -- --demo --no-audio
dotnet run -c Release -- --wad /path/to/your/game.wad --map E1M1
dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64
./artifacts/linux-x64/DoomSharp --demo
```

Use `win-x64`, `win-arm64`, `linux-arm64`, `osx-x64` or `osx-arm64` as appropriate.
Publish on the target OS; .NET Native AOT does not support cross-OS compilation.
Windows requires the Visual Studio C++ build tools; Linux requires a C compiler
and zlib development headers; macOS requires Xcode command-line tools.

| Platform | Native facilities | Status |
|---|---|---|
| Linux | X11 (`libX11.so.6`), ALSA (`libasound.so.2`) | x64 AOT and desktop presentation; reproducible OS-input demo/restart and ALSA null qualification below; Wayland needs XWayland |
| Windows | Win32/GDI, waveOut (`winmm`) | x64 AOT and bounded desktop presentation; interactive input/audio unverified |
| macOS | AppKit/CoreGraphics/QuartzCore, AudioQueue | ARM64 AOT and bounded desktop presentation; interactive input/audio unverified |

The window is initially 960x600 with an internal 640x400 software framebuffer.
Linux requires a 24/32-bit TrueColor X11 display. Audio uses the default OS device;
if none exists, use `--no-audio`. Failure to open a requested device is reported,
not silently treated as success. Run macOS from a graphical login session.

## Controls

WASD moves and strafes; arrows turn (up/down also move). Click once to capture
the mouse, then use mouse movement to turn and the left button or Ctrl to fire.
E/Space uses a door or switch, Shift runs, 1 selects pistol, 2 selects an acquired
shotgun, 4 selects an acquired chaingun, 5 selects an acquired rocket launcher,
Tab toggles the automap, R restarts, and Esc quits. Losing window focus
releases mouse capture and held keys. After an exit, fire/use advances to the next
map using classic episode/DOOM II normal and secret exit routes. Episode finales
stop at the completion screen; start another episode with `--map E2M1`, etc.
Sector-11 damaging exit floors finish at health 10 or lower. Living players keep
health/inventory on the next map; dead players start it with fresh health and
default inventory. A finale or missing next map shows the replay prompt instead
of offering a nonexistent continuation.

## Implemented

- IWAD/PWAD directory bounds checks, classic map records and BSP validation.
- BSP traversal and clipped convex subsector polygons, perspective-correct wall,
  floor and ceiling textures, depth-tested transparent sprites and masked walls,
  sidedef offsets/pegging, sector light/distance shading and sky backgrounds.
- Cylinder/wall and actor collision, sliding and 24-unit steps, sector headroom.
- Pistol/shotgun/chaingun hitscan, ammo, health/armor, keys, pickups, barrels, enemy deaths
  and drops; simplified enemy chase, melee, hitscan and projectile attacks.
- Rocket launcher pickups, separate rocket ammo, swept moving missiles and
  direct/radial impacts, including wall occlusion, self-damage and barrel chains.
- Common manual/tagged doors, key checks, door obstruction reopening, use/walk
  exits, tagged lower/wait/raise lifts, lower-to-lowest and raise-to-next/24 floors,
  tagged walk teleports, damaging floors, secrets, death/restart and map progression.
- Tagged key-locked fast-opening door switches99/133-137: matching-color card or
  skull, front-side use, permanent opening, and repeatable versus one-use actions.
- WAD DMX sound decoding/resampling and a 16-voice native PCM effects mixer.
- HUD, weapon overlays, automap, headless framebuffer export and original demo.

The chaingun uses the pistol's bullet pool (capacity200). A placed type2002
pickup grants20 bullets; a chaingunner's dropped weapon grants10. Newly acquired
chainguns are selected even at full ammo; owned full-ammo pickups remain until
ammo is needed. At the game's fixed35Hz, each shot is spaced four ticks apart.
A tap completes a two-shot pair, or one shot if only one bullet remains. The
first pair of a burst has no horizontal spread; held refire has triangular
horizontal spread and the shared5/10/15 damage. Selection waits for a started
pair to finish. The HUD identifies the weapon; supplied `CHGGA0`/`CHGGB0` body
and `CHGFA0`/`CHGFB0` flash patches provide visible firing phases using their
WAD offsets, and the existing `DSPISTOL` effect is used. Missing patches/sounds
are diagnosed, not replaced with pistol artwork. An unowned selection or empty
weapon gives explicit feedback; empty fire causes no damage/ammo underflow.
Automatic empty-weapon/fist fallback, weapon raising/lowering, original autoaim,
RNG and exact original-game timing/visual comparison remain unfinished.

The rocket launcher uses its own pool (capacity50): type2003 grants2 rockets,
type2010 grants1 and type2046 grants5. New ownership selects the weapon even at
full ammo; owned full-ammo pickups remain, and duplicates refill without forcing
selection. Living next-map transitions carry ownership/ammo; restart or dead-player
transitions reset them. Empty fire gives feedback without a missile or flash.
At35Hz, rockets travel20 map units per tick and successful shots are20 ticks
apart. Release stops refire; selection waits for the started firing cycle.
Swept radius11/height8 missiles choose the first geometry or solid-actor contact,
apply20-160 direct damage to shootable targets and a bounded128-unit blast with
distance/radius falloff, line-of-sight occlusion and player self-damage. Barrel
chains share the blast helper. Explosion damage runs once, with18 ticks of
supplied `MISLB0`/`MISLC0`/`MISLD0` animation before removal. Flight uses the real
eight-way `MISLA` patches, including combined/mirrored rotation lumps. Supplied
`MISGA0`/`MISGB0` and `MISFA0`-`MISFD0` weapon patches and `DSRLAUNC`/`DSBAREXP`
effects are used; missing assets are explicitly diagnosed, not invented.
Horizontal firing/no autoaim, immediate launch rather than exact original windup,
Euclidean blast falloff, BCL RNG, absent backpack/skill multipliers and incomplete
raise/lower/visual parity remain limitations.

## Compatibility limits

Supply your own **lawfully obtained** classic DOOM-format WAD. Owning a WAD does
not grant permission to redistribute it. Alternatively obtain
[Freedoom](https://freedoom.github.io/) separately under its BSD license. No
proprietary WAD, original DOOM source, or third-party engine is included here.
The renderer uses our own triangle rasterizer rather than ported DOOM code.

Map loading/rendering does **not** prove a whole campaign is completable. The
engine does not yet implement stairs/crushers, every lift/moving-floor variant,
all switch/trigger specials, switch-texture changes, exact monster state tables,
monster infighting, boss scripts, all weapons/powerups, original physics/RNG,
animated textures/flats, music/MUS playback, savegames, menus or multiplayer.
Some original decorations and item types are omitted with explicit diagnostics.
DOOM II enemies use approximate behaviors; rotations/animation are preliminary.
Only the medium-skill single-player thing flags are selected. No PWAD overlay
stack, Hexen/UDMF maps, extended BSP nodes, Boom/MBF/DEHACKED behavior or mods.
Teleport destination blockers are rejected rather than telefragged. Moving
floors are non-crushing and enemy/player behavior is approximate. Missing textures
show an explicitly logged diagnostic pattern.
Player grounding follows the highest floor touched by the radius-16 body, not
only its center sector. This preserves support when stepping off a raised lift;
moving-floor headroom checks also include the body's touched sectors. Falling
still uses instantaneous grounding, not original gravity/timing.
Damaging floors retain an approximate one-second cadence, not exact original
tick timing. Sector-11 exits do not implement the boss actions needed to reach
some original finale floors.
These gaps can block authentic WAD progression; **full original-DOOM parity is
unfinished**.

## Verification

```sh
dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64
./artifacts/linux-x64/DoomSharp --demo --headless --frames 35 --output demo.ppm
./artifacts/linux-x64/DoomSharp --wad /path/to/freedoom1.wad --validate-maps
xvfb-run -a python3 scripts/native-demo.py artifacts/linux-x64/DoomSharp
```

There are **no unit tests or isolated engine-method harnesses**. The former
37-check harness and its startup shortcut have been removed. Production WAD
validation and error diagnostics remain intact.

The [native demo orchestration](scripts/native-demo.py) uses Python's standard
library and native X11/XTest, only as development automation. It launches the
published game, sends OS keyboard events, and checks its public exit status,
summary and complete exported frame. Scenarios cover missing-file/invalid-frame
startup rejections, a pickup/door/combat/exit playthrough and a live restart.
It never calls engine internals, alters running game state, or injects a forced
exit. It uses ALSA null output: **PCM submission is not audible playback**.
Linux needs Xvfb/XTest for this automation, not for ordinary gameplay.
Logs, frames, the executable hash and outcomes are written to
`artifacts/native-demo`; failed scenarios remain failures and CI retains artifacts.

The demo assets are procedurally generated original content; `--demo-wad path.wad`
can write that encounter for inspection. `--validate-maps` loads and renders each
map at its player start, reporting unsupported things and missing textures.
`--headless` opens neither a window nor an audio device. `--frames N` also bounds
desktop runs. PPM exports contain only the assets from the WAD you supply.

The [engine PR #1 report](https://github.com/davidfowl/doomsharp/pull/1) states that
Freedoom 0.13.0's 36 Phase 1 and 32 Phase 2 maps loaded/rendered at their player
starts with zero missing textures, and that the archive checksum and asset
license were checked. These are worker-reported parser/renderer results, not
CI corpus coverage or campaign completion; those assets are not bundled.
The same report describes a Linux/Xvfb OS-injected demo playthrough, mouse
capture and ALSA null-device PCM output. Separate independent synthetic
OS-input collision/shotgun/kill/exit/restart qualification was worker-reported
at engine head `72c525746d705c80aaf76e1a014b0e04f97c3d9f`; its evidence remains
worker-local, not a publicly accessible qualification packet. Neither report
establishes physical input, audible playback or original-map full gameplay.
Separate [synthetic fixture tooling](tools/e2e-fixtures/README.md) is now merged;
its instructions and worker-reported outcomes are not the raw qualification
packet or original-game compatibility proof.

The [current CI workflow](.github/workflows/build.yml) publishes Native AOT
separately on Linux, Windows and macOS, then runs the actual native executable
for headless/demo desktop scenarios. Linux additionally runs the OS-input
demo/restart qualification above; its logs and frames accompany the native
artifacts. Windows/macOS remain bounded no-audio presentation smoke, **not**
interactive gameplay qualification. No run proves complete original-DOOM
campaign behavior or physical input/audible sound.

Separate local native OS-input qualification reproduces the formerly missing
sector-11 exit and covers its 20-point damage, exact 10-health exit threshold,
living/dead-player next-map rules, finale/missing-map stop, replay and an ordinary
floor control. It uses original classic-format fixture maps derived in scratch
from the unchanged published fixture inputs. These are worker-local executable
results, not a CI sector-special corpus or proof of completing original E1M8.

Linux lift qualification uses the unchanged published
[focused runner](tools/e2e-fixtures/LIFT.md), followed by two cycles in one actual
game process. The cycle runner requires the focused runner's passing
`X<=-32` step-off result for the same source/executable, matches that cleared
native pose before retriggering, and retains the lowering/wait/carry/top-height
gates (height64, tolerance2). Existing output directories are never overwritten.

```sh
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release -- tools/e2e-fixtures/generated
SOURCE=$(git rev-parse HEAD)
python3 tools/e2e-fixtures/lift.py artifacts/linux-x64/DoomSharp --source-commit "$SOURCE" --output-dir artifacts/lift-focused
python3 scripts/native-lift-cycle.py artifacts/linux-x64/DoomSharp --source-commit "$SOURCE" --step-off-reference artifacts/lift-focused/result.json --output-dir artifacts/lift-cycle
```

Run after publishing that exact source. These are fixture-specific virtual-X11
observations, not exact original timing, every lift variant or campaign proof.

Linux automap input qualification uses genuine native `XK_Tab` (`0xff09`) press/
release events, observes ordinary 3D -> automap -> 3D frames, and checks W movement
and Ctrl firing as positive controls. X11 Tab/ISO_Left_Tab keysyms are normalized
at the backend boundary; shared Windows/macOS mappings are unchanged.

```sh
python3 scripts/native-tab.py artifacts/linux-x64/DoomSharp --source-commit "$SOURCE" --output-dir artifacts/tab-input
```

This runner starts its own isolated Xvfb, like the lift runners; do not wrap it
in `xvfb-run`. It requires the generated `portal-equal.wad` above, X11/XTest native
libraries and a new output directory. Its five gates require initial/final 3D
background fraction below0.05, automap background fraction above0.90, final W
position X in [0,30] from -64, and rendered ammo50 -> 48 after Ctrl. The frames,
native input mappings, public output and hashes are retained even on failure.
This is Linux virtual-input/framebuffer evidence, not physical keyboard or
Windows/macOS interactive qualification; current CI remains the documented demo
scope.

Tagged locked-door qualification generates separate original four-sector maps
from the published fixture's original assets; it does not change publisher inputs
or assertions. The switch is on an open portal, while a **remote tagged sector**
blocks the route, so using the adjacent sector instead cannot pass. Run one fresh
native process per distinct scenario; the collector stops at the first failure
and never retries or overwrites an evidence directory:

```sh
python3 scripts/native-locked-doors.py generate --output-dir artifacts/locked-door-inputs
python3 scripts/native-locked-doors.py qualify artifacts/linux-x64/DoomSharp --source-commit "$SOURCE" --input-dir artifacts/locked-door-inputs --output-dir artifacts/locked-door-evidence
```

The22 scenarios cover all six matching-card specials, all three matching skulls,
wrong-color/no-key/tag/backside denial, recovery after a denied one-use action,
front use exactly on the line, unactivated controls, a short fast-opening run, and SR99 versus consumed S1133
after a natural W1 close. Positive traversal requires X>=192; blocked routes
require X in[32,48]. The fast case presses use then holds W+Shift for0.4s and
requires X in[56,90]. Repeat qualification first matches independently accepted
open and closed native poses for the same source/executable/input; maximum
changed-pixel fraction0.001. Image comparisons exclude top messages and bottom
HUD, not changing-world pixels. These are Linux virtual-X11 original-fixture
observations, not natural real-map/campaign completion or an exact timing oracle.
Normal doors retain their existing90-units/s approximation; these new fast
actions use280 units/s and the existing neighboring-ceiling-minus4 destination.
Switch texture changes and other fast-door families remain incomplete.

Chaingun qualification uses [native weapon orchestration](scripts/native-chaingun.py)
with15 distinct fresh-process scenarios. It prepares original WADs and
color-coded patches before startup, observes visible numeric HUD and weapon/
flash pixels, sends native W/1/2/4/Ctrl/E/R events, and checks ordinary process
output. No engine methods, live state injection, units or cheat paths are used.

```sh
python3 scripts/native-chaingun.py generate --output-dir artifacts/chaingun-inputs
python3 scripts/native-chaingun.py qualify artifacts/linux-x64/DoomSharp --source-commit "$SOURCE" --input-dir artifacts/chaingun-inputs --output-dir artifacts/chaingun-evidence
```

Publish that exact clean source first and generate the unchanged publisher
assets as above. The runner allocates its own Xvfb; output directories must be
new. It stops at the first failed scenario without retrying. Native gates cover
placed/dropped pickups, key selection and pistol/shotgun preservation, shared
clip/box ammo, cap200 and retained full-ammo duplicate, two-shot tap/release,
four-tick sustained cadence, first-pair accuracy versus refire spread, combat,
one/zero-ammo rejection and ordinary clip reload, live/dead restart and living/dead next-map inventory.
The dry-fire damage negative aligns a visible marked enemy with the crosshair
using ordinary strafing, requires it to survive two seconds of empty fire, then
collects a clip and kills that target with two accurate pairs. Thus a miss is not
used as a substitute for an empty-ammo damage rejection.
Distinct body/flash colors are fixture oracles, **not** original artwork parity.
Two audio scenarios check successful effect counts/ALSA-null PCM submission
and one-time missing-sound diagnostics; missing-body/flash diagnostics are also
required. No audible playback, physical input, original timing/RNG oracle,
natural-map campaign or other-platform interactive weapon acceptance is claimed.
Inputs, frames, events, stdout/stderr, collector outcomes and source/executable/
fixture/runner hashes are retained locally, not bundled or implied public raw
downloads. Existing three-OS CI remains native demo/headless/desktop smoke,
not this weapon corpus.

### Native rocket-launcher qualification

[Original rocket orchestration](scripts/native-rockets.py) exercises the full
published executable, not engine methods. From a clean checkout, generate the
unchanged publisher assets first, then use new output directories:

```sh
dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release \
  -- tools/e2e-fixtures/generated
python3 scripts/native-rockets.py generate --output-dir artifacts/rocket-inputs
python3 scripts/native-rockets.py qualify artifacts/linux-x64/DoomSharp \
  --source-commit "$(git rev-parse HEAD)" --input-dir artifacts/rocket-inputs \
  --output-dir artifacts/rocket-native
```

The runner starts its own isolated Xvfb; do not wrap it in `xvfb-run`.
Its21 distinct fresh native cases cover pickup/key5 and all existing weapon
pools, missile/box ammo and cap50/duplicates, visible travel before damage,
direct damage beyond what three128-point blasts can kill, a miss with a
hittable-target positive control, near/far splash and a wall-shielded target
with an ordinary hitscan positive control. A closed zero-thickness wall at X85
must impact on the near side: player health100->30, no behind-wall kill; its
open-boundary twin must let the projectile through. Depletion aligns a visible
live target before two seconds of empty fire, checks no projectile/flash/kill,
demonstrates that a bullet clip does not refill rockets, then collects ordinary
rocket ammo and kills that same target.

The real colored WAD patches distinguish all four muzzle-flash and three
explosion phases. A known16-unit patch's changing native projected width gives
a700-unit/s flight interval with one35Hz-tick and two-native-pixel uncertainty;
four missiles over two seconds qualify the20-tick firing cadence. Release,
single impact/despawn, living carry, live/dead restart, dead-player next-map
defaults, barrel chains, existing monster projectiles and explicit missing
patch/sound diagnostics have separate gates. Two cases count launch/explosion
effects and PCM submitted to ALSA null, **not audible output**.
Short weapon phases are captured before HUD/pixel analysis, with tap release
timed independently of decoding; analysis must not turn a tap into held refire.

These original sparse-BSP synthetic fixtures are bounded weapon oracles, not
original campaign maps/artwork, exact original timing/RNG/visual parity or
physical/broad-OS gameplay proof. Failures stop the matrix and retain raw frames,
events, summaries, exit codes and source/executable/input/runner hashes; no
unchanged retry is performed. Local raw packets remain worker-local unless
explicitly published. The unchanged three-host CI covers AOT/demo/presentation,
not this dedicated rocket corpus or the independent frozen navigation route.
