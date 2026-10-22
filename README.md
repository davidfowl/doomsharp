# DoomSharp

A clean-room C# classic WAD engine, with native desktop and browser WebAssembly
hosts. Gameplay, WAD loading, software rendering, generated assets and development
tools are C#. The browser's small JavaScript adapter only bridges Canvas, browser
events, animation scheduling and WebAudio; there is no JavaScript game engine.

**Playable, but not a complete DOOM replacement.** `--demo` plays the bundled,
freely licensed Freedoom Phase 1. The original two-room encounter is available
with `--fixture-demo`. No original DOOM source or proprietary assets are included.
Historical engine qualification and compatibility notes are in [PROGRESS.md](PROGRESS.md);
the commands and evidence boundaries below supersede its old tooling instructions.

## Desktop

Install the **.NET 10 SDK**. Run from the repository root:

```sh
dotnet run -c Release -- --demo --no-audio
dotnet run -c Release -- --fixture-demo
dotnet run -c Release -- --wad /path/to/game.wad --map E1M1
```

Desktop and regression tools use the .NET BCL, without managed game-engine or
test-framework dependencies. Audio uses the default native device and reports
failures; pass `--no-audio` if no device is available.

Publish a standalone Native AOT executable **on its target OS**:

```sh
dotnet publish DoomSharp.csproj -c Release -r linux-x64 -o artifacts/linux-x64
./artifacts/linux-x64/DoomSharp --demo
```

Keep the published `assets/freedoom` directory beside the executable. Asset lookup
is executable-relative, not working-directory-relative. On Windows, use
`artifacts/win-x64/DoomSharp.exe`. Native AOT requires the Visual Studio C++ tools
on Windows, a C compiler and zlib development headers on Linux, and Xcode
command-line tools on macOS. Cross-OS Native AOT publishing is not supported.

| Target RIDs | Desktop facilities | Requirements / evidence |
|---|---|---|
| `linux-x64`, `linux-arm64` | X11, ALSA | `libX11.so.6`, `libasound.so.2`; Wayland requires XWayland. Both RIDs passed CI executable/engine and XTest/Xvfb native checks. |
| `win-x64`, `win-arm64` | Win32/GDI, waveOut | No third-party native window/audio library. Both RIDs passed CI executable/engine checks and bounded native window rendering. |
| `osx-x64`, `osx-arm64` | AppKit/CoreGraphics, AudioQueue | Run from a graphical login session. Both RIDs passed CI executable/engine checks and bounded native window rendering. |
| Browser WASM | Canvas 2D, WebAudio, keyboard/pointer events | Desktop Chromium, Firefox and WebKit smoke checks; not proof of Safari/macOS or mobile-device behavior. |

The desktop framebuffer is 640x400 in an initially 960x600 window. Linux requires
a 24/32-bit TrueColor display. The CI workflow publishes and exercises six desktop
OS/architecture targets, plus three browser engines. A configured job is **not**
evidence that this local revision ran on that target.

[CI for the integrated C# tooling/WASM increment](https://github.com/davidfowl/doomsharp/actions/runs/38846813017)
passed all six native targets and Chromium/Firefox/WebKit. Native jobs publish
and run the actual executable, not just compile it; Linux additionally exercises
synthetic OS input, framebuffer readback and null PCM. Windows/macOS desktop
smoke is bounded to 35 frames with audio disabled. Local qualification used
Linux x64; neither it nor hosted CI establishes physical-device input, audible
playback, Safari/mobile support or complete campaign compatibility.

[Stair/crusher milestone CI](https://github.com/davidfowl/doomsharp/actions/runs/39067803717)
also passed all nine native/browser jobs at implementation `42a186f`.
Downloaded Linux x64 results confirm 748 executable/engine/native assertions;
the browser Release-AOT application passes 38 Chromium, 38 Firefox and 37 WebKit
runtime assertions. WebKit mouse look was unavailable, and the browser checks
do not traverse stair/crusher scenarios. See [PROGRESS.md](PROGRESS.md) for exact
coverage, downloadable CI evidence and remaining compatibility limits.
This milestone is still branch-only: a normal main fast-forward was rejected
by repository rules. The qualified branch is preserved; no rule bypass was used.

## Browser WebAssembly

The site offers two browser runtimes: the original **.NET WASM (Blazor AOT)** at
`/doomsharp/` and **NetWasm** at `/doomsharp/netwasm/`. Both use the same C#
engine and the same Freedoom WAD, with separate browser modules. NetWasm targets
its narrower `netwasm0.1` API profile; only the desktop-only `Wad(string path)`
and `Renderer.SavePpm` filesystem methods are excluded from this target. It
uses the same Canvas/WebAudio browser adapter and supports the same local WAD,
keyboard/pointer controls, music/effects and local saves. NetWasm runs on the
main browser thread, and a synchronous export (notably WAD initialization) can
block interaction temporarily. The NetWasm toolchain is pre-1.0; the exact SDK
version is pinned in `NetWasm/global.json`.

The website's **Published build comparison** reports raw Release browser
runtime footprint bytes (framework/module, JS, HTML and CSS) and uncompressed
first-load browser response bytes (`PerformanceResourceTiming.decodedBodySize`
for same-origin navigation and resources in a fresh Chromium context). The
identical shared Freedoom WAD/credits and comparison data are excluded from
first-load size; licenses, `.br`/`.gz` sidecars, maps and PDBs are excluded from
published runtime footprint. These are raw bytes, **not** a claim about actual
HTTP transfer size under compression. It also shows the median of five wall-clock `performance.now()`
samples of 120 consecutive C# software renders of the paused initial E1M1 scene in
headless Chromium, measured through one browser-call boundary per sample. This compares renderer
throughput, **not** full gameplay frame rate, WAD loading, startup, audio or
input latency; the two JS interop paths differ. Results depend on the CI
runner/browser and should not be extrapolated to other hardware. To reproduce
on Linux, publish both runtimes per [NetWasm host instructions](NetWasm/README.md),
install Chromium with `dotnet run --project tools/BrowserChecks -c Release -- install chromium`,
serve the merged site with `tools/Serve`, then run:

```sh
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release -- tools/e2e-fixtures/generated
dotnet run --project tools/BrowserChecks -c Release -- netwasm http://127.0.0.1:5187/doomsharp/ artifacts/netwasm-checks tools/e2e-fixtures/generated
dotnet run --project tools/BrowserChecks -c Release -- sizes artifacts/browser/wwwroot artifacts/netwasm-checks
dotnet run --project tools/BrowserChecks -c Release -- stats http://127.0.0.1:5187/doomsharp/
```

`artifacts/netwasm-checks/render-comparison.json` and
`artifacts/browser/wwwroot/comparison.json` retain exact values. The published
comparison is regenerated by the Chromium CI job after both builds and browser
checks pass, before Pages deployment. The benchmark button on each page lets
you time 120 renders on your own machine (reload first for the initial scene).

```sh
dotnet workload install wasm-tools
dotnet run --project Browser/DoomSharp.Browser.csproj -c Release --urls http://localhost:5187
```

Open `http://localhost:5187`. Bundled Freedoom Phase 1 loads and runs automatically,
without sound or mouse capture until you click the game. **Restart Freedoom**
reloads the bundled WAD; you can also choose a local classic WAD (up to 64 MiB).
The browser does not offer the original test level. The optional Map field accepts a
map name such as `E1M2`; leave it blank for the first map. A local WAD is processed
on your device, not uploaded. Use only lawfully obtained assets.

The browser runs the same C# engine source files as the desktop executable.
It uses the .NET WebAssembly runtime via Blazor, not a server-side game process.
Release builds compile the engine ahead of time for WebAssembly performance;
the first publish is slower and needs the `wasm-tools` workload.
The renderer is software-based; frame rate depends on map complexity and browser.
Clicking enables audio and requests pointer lock. Keyboard controls still work
when pointer lock is unavailable. Esc, loss of focus or a hidden tab pauses the
game and clears held input; click to resume. **Mute** suppresses new sound effects
and stops music; unchecking it resumes the current track.
Wheel input over the active game does not scroll the page. Press Esc to pause
and scroll normally; scrolling outside the game remains available during play.
Mobile/touch controls, gamepads and persistent saves are not implemented. Music
plays via a clean-room MUS/Standard-MIDI decoder and an approximate software
synthesizer; see [PROGRESS.md](PROGRESS.md).

Publish a static site:

```sh
dotnet publish Browser/DoomSharp.Browser.csproj -c Release -o artifacts/browser
dotnet run --project tools/Serve -c Release -- artifacts/browser/wwwroot --urls http://localhost:5187
```

Deploy the **contents** of `artifacts/browser/wwwroot` to any static HTTP host that
serves `.wasm` as `application/wasm` and `.dat`/`.wad` as binary data. Do not open
`index.html` as `file://`. Pointer lock/audio require browser permission and a
user gesture; use HTTPS in deployment (localhost is suitable for development).
The relative `<base href="./">` supports both root hosting and subpaths such as
`/doomsharp/`; use the URL with its trailing slash. Keep the published MIT license
and bundled Freedoom license and credits with the site. Uncompressed runtime
files must remain available; `.br`/`.gz` sidecars are optional and require correct
content-encoding headers if served.

### GitHub Pages

Set repository **Settings > Pages > Source** to **GitHub Actions**. On a push to
`main`, `.github/workflows/build.yml` publishes the browser site and exercises it
under `/doomsharp/` in Chromium, Firefox and WebKit; Chromium also tests the
NetWasm route and records a same-browser rendering comparison. After all three browser jobs
succeed, it deploys only the published `wwwroot` contents to the `github-pages`
environment. Pull requests and other branches test without deploying.
The deployment job then runs Chromium against the public Pages URL, exercising
WAD loading, rendering, input and gameplay. Its `doomsharp-live-pages` artifact
retains the results and screenshots; a successful upload alone is not a
successful live gameplay check.
The site is available at <https://davidfowl.github.io/doomsharp/> after a successful
Pages deployment. This build does not require cross-origin-isolation headers.

To reproduce the subpath check locally, serve the published output with:

```sh
dotnet run --project tools/Serve -c Release -- artifacts/browser/wwwroot --urls http://localhost:5187 --path-base /doomsharp
```

Open `http://localhost:5187/doomsharp/`, or pass that URL to the browser checks
documented below.

## Controls

WASD moves/strafes; arrows turn (up/down also move). Click once to capture the
mouse, then move it to turn. Ctrl or left click fires; E/Space uses; Shift runs.
1 selects the pistol, 2 an acquired shotgun, 3 an acquired chainsaw, 4 an
acquired chaingun, 5 an acquired rocket launcher, 6 an acquired plasma rifle,
and 7 an acquired BFG9000. Tab toggles the automap; R restarts. F1 opens an
in-game menu (Resume, New Game, Options, Quit Game; arrows navigate, E/Space/
fire selects, left/right adjusts mouse sensitivity and toggles messages in
Options). Esc quits desktop or pauses the browser. Losing focus clears input
and releases mouse capture. F5 saves and F9 loads game state (desktop: a
`doomsharp.sav` file next to the executable; browser: `localStorage`, also
reachable via Save/Load buttons).

Use/fire advances after an exit where a next map exists. Living players retain
health/inventory; dead players start fresh. Episode finales offer replay.
`--help` lists desktop options; `--frames N` also bounds desktop presentation.

## Powerups

Picking up Berserk, Invulnerability, Partial Invisibility, a Radiation Suit, a
Computer Area Map or a Light Amplification Visor grants their classic timed (or,
for Berserk/Computer Area Map, persistent) effect, shown in the HUD with the
remaining duration. Invulnerability blocks all damage and inverts the view; a
Radiation Suit stops most floor-damage; Partial Invisibility makes monsters miss
more often; the Visor renders at full brightness; and the Computer Area Map
reveals the whole automap instead of only the sectors you have visited (the
automap is fog-of-war without it). Powerups reset on level changes and are not
stored in savegames; loading a save also resets explored automap sectors. See
[PROGRESS.md](PROGRESS.md) for exact scope and limits.

## Verification and development tools

All repository-owned tools are C#; there are no Python scripts or commands.
The .NET WebAssembly workload supplies its upstream native compiler toolchain.
From the root:

```sh
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release -- tools/e2e-fixtures/generated
dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64
SOURCE=$(git rev-parse HEAD)
dotnet run --project tools/Checks -c Release -- headless artifacts/linux-x64/DoomSharp tools/e2e-fixtures/generated artifacts/headless "$SOURCE"
dotnet run --project tools/Checks -c Release -- engine artifacts/linux-x64/DoomSharp tools/e2e-fixtures/generated artifacts/engine "$SOURCE"
dotnet run --project tools/Checks -c Release -- native artifacts/linux-x64/DoomSharp tools/e2e-fixtures/generated artifacts/native "$SOURCE"
./artifacts/linux-x64/DoomSharp --demo --validate-maps
```

The first two modes are cross-platform (use the appropriate executable path).
`native` needs Linux, Xvfb, libX11, libXtst and ALSA; it allocates isolated X
displays itself. On Ubuntu these are provided by `xvfb`, `libx11-6`, `libxtst6`
and `libasound2t64`. `all` combines the three modes. Evidence directories must
be **new**, so previous results are never silently overwritten.

- `headless` invokes the actual executable for every generated WAD, checks
  complete P6 frames, and requires malformed input to exit 1 with a diagnostic.
- `engine` feeds public C# input into the shared engine to check movement
  thresholds, all six tagged-lock actions and card/skull colors, weapon
  ownership/ammo/cadence/combat/carry/restart/death, Baron floor actions, and
  tagged stair triggers/chains/speeds/headroom/restart, and crusher
  activation/cycles/damage/stop/resume/mover ownership, plus switch texture
  selection, success/denial, button deadlines/retriggers and map lifecycle.
- `native` drives the actual Linux executable using XTest keyboard events and
  reads the X11 framebuffer: demo completion/restart, Tab/movement/firing HUD,
  two lift lower/wait/raise/carry/step-off cycles, and both weapons with PCM
  submission through an ALSA null device. Stair checks traverse raised routes
  for all four supported actions and require untagged controls to stay blocked.
  Crusher checks require death in active traps versus stable frames/health100
  after stop actions; two fast-crusher cases also require null PCM submission.
  Switch checks observe off/on colors, permanent latches, timed resets and
  denied/manual controls; one repeatable-button case also requires null PCM.

Results contain explicit pass/fail assertions, diagnostics, executable hashes,
caller-supplied source revision and native frame/input records. A source revision
is provenance supplied by the builder, not something inferred from an executable.
These checks replace the previous separate Python runners; they deliberately
distinguish engine checks from OS-input evidence. The new suite is **not a
bit-for-bit port of every historical weapon/door/boss framebuffer oracle**.
Previous evidence remains historical, not newly established by these checks.
See [fixture tooling](tools/e2e-fixtures/README.md) and
[lift qualification](tools/e2e-fixtures/LIFT.md).

Browser runtime checks use the C# Playwright API:

```sh
dotnet run --project tools/BrowserChecks -c Release -- install --with-deps chromium firefox webkit
# In another terminal, serve the published site using tools/Serve as above.
dotnet run --project tools/BrowserChecks -c Release -- http://localhost:5187 tools/e2e-fixtures/generated artifacts/browser-chromium chromium
dotnet run --project tools/BrowserChecks -c Release -- http://localhost:5187 tools/e2e-fixtures/generated artifacts/browser-firefox firefox
dotnet run --project tools/BrowserChecks -c Release -- http://localhost:5187 tools/e2e-fixtures/generated artifacts/browser-webkit webkit
```

The browser checks observe actual Canvas output, keyboard movement, pointer
capture/turning where supported, automap, pause/restart, WebAudio scheduling, mute,
automatic bundled Freedoom startup before any interaction, local WAD loading,
and visible load/download errors with retry. Browser tooling brings Playwright's own driver/browser dependencies;
the application itself does not depend on Playwright.
Original switch fixtures additionally exercise visible one-shot latching,
repeatable reset, key denial and preservation of a pending button while paused.

On headless Linux, Firefox needs a working audio backend even to schedule
WebAudio. CI supplies a private PulseAudio null sink and exports its `PULSE_SERVER`;
local desktop sessions normally already have PulseAudio/PipeWire. A missing
backend is a failed audio check, not silently reported as verified sound.

Synthetic input, null PCM and headless browsers do not establish physical-device
input, audible sound, original-game parity, mobile support or campaign completion.

## Engine scope and limits

### Tagged stairs

Classic linedef actions 7 (use once) and 8 (walk once) build 8-unit steps at
8.75 units/second. Actions 127 (use once) and 100 (walk once) build 16-unit steps
at 140 units/second. Use actions require the front side; walk actions accept
either crossing direction. Each tagged seed starts from its current floor.
Chains follow the first available front-to-back two-sided linedef in map order,
keeping the seed's floor texture and adding one step per sector, including
untagged neighbors. All eligible tagged seeds are processed.

Active floor movers are not overwritten. A busy seed can be retried; a busy
neighbor is skipped when choosing the next link. Successful activation consumes
the action; missing/zero tags leave it available and display a diagnostic.
Stairs reuse moving-floor support and non-crushing occupancy checks, resume
after an obstruction clears, and reset on restart. Use-switch feedback is covered
below; exact vanilla busy-neighbor quirks and complete campaign progression
remain unqualified.

### Tagged crushers

Walk-once actions 6 (fast), 25 (normal) and 141 (normal, silent), plus repeatable
action 77 (fast), start all eligible tagged ceilings. They cycle without waiting
between their starting ceiling and 8 units above the starting floor. Normal and
fast speeds are 35 and 70 units/second; obstructed normal crushers slow to
4.375 units/second for the rest of the downstroke and restore normal speed
when rising. Silent crushers suppress movement sounds but play `PSTOP` at
their endpoints; others use `STNMOV` every eight 35-Hz ticks.

Overlapping living players, enemies and barrels receive 10 damage every four
ticks through existing armor, death/drop and barrel-blast handling. A body
spanning two crushers takes only one crush hit per tick. Contact uses sector
footprints, not just the center. The simplified engine uses 56-unit living
bodies and 16-unit dropped pickups; crushed drops are removed, while placed
pickups and corpse artwork are retained. This is not exact original actor-height,
gibbing, camera or collision parity.

Actions 57 (walk once) and 74 (repeatable) pause tagged crushers in place,
including damage. A new start action (for example 77) resumes the existing
direction, speed and silent variant rather than replacing it. Running crushers
are not restarted by recrossing. Successful once-only actions are consumed;
missing/zero tags or busy sectors retain the action and provide a diagnostic.
Active doors/floors are preserved, and active or paused crushers reserve their
sectors against door, floor and stair takeover. Restart recreates the original
geometry and removes crusher state.

The action mapping is cross-checked against the
[Doom editor linedef definitions](https://github.com/UltimateDoomBuilder/UltimateDoomBuilder/blob/master/Assets/Common/Configurations/Includes/Doom_linedefs.cfg).
Other crusher/ceiling actions, exact original obstruction quirks and authentic
campaign completion remain unqualified.

### Switch textures and buttons

The classic switch-texture integration makes successful front-side
use toggles the 40 canonical vanilla `SW1`/`SW2` pairs on existing switch actions:
7/127, 11/51, 18/21/23/60/62/69/122/123, and
29/63/99/103/111/112/113/133/134/135/136/137. Pair-table order is considered
first, off before on, then upper/middle/lower; only the front sidedef changes.
This is not arbitrary name-prefix pairing or support for Boom `SWITCHES` lumps.

One-shot switches latch. Repeatable buttons (60/62/69/123/63/99/134/136)
restore their original texture after 35 subsequent gameplay ticks. Successful
early retriggers may toggle again but neither extend the original deadline nor
create another reset. Failed/busy actions leave textures and deadlines untouched.
Buttons are independent, pause with gameplay, and are discarded on restart or
map change. Switch clicks/reset use `SWTCHN`; exit switches use `SWTCHX`.

Missing partners leave the texture unchanged, log once per map and allow another
valid slot/pair to be selected; unsupported custom switch names are diagnosed
without installing checkerboards. Missing tags, keys, busy movers and back-side
use do not falsely consume S1 actions or change textures. Manual doors and
crossing actions do not toggle switch textures. This adds feedback to existing
dispatch, not new action families or full door/mover/gameplay parity.

### Compatibility

Implemented: classic IWAD/PWAD directory/geometry/BSP validation; textured
walls/floors/ceilings, sky, masked walls and sprites; collision, sliding and
24-unit steps; pistol, shotgun, chaingun, chainsaw, plasma rifle, BFG9000 and
swept rocket/explosion combat (see [PROGRESS.md](PROGRESS.md) for the BFG's
documented simplification versus classic tracer-spray);
pickups, keys, armor, simplified enemies/barrels; common doors, tagged locks,
lifts/floors, teleports, damaging floors, secrets, exits and map progression;
E1M8 last-Baron/tag666 floors; animated flat/wall textures (water, nukage,
slime, blood, lava and fire cycles on a fixed 8-tic interval); DMX effects,
MUS/Standard-MIDI music playback
(approximate software synthesis, see [PROGRESS.md](PROGRESS.md)), HUD, automap,
an end-of-level intermission stats screen (Kills/Items/Secrets percentages and
time; see [PROGRESS.md](PROGRESS.md) for the exact "item" definition),
an in-game pause/options menu (F1), and a single-slot JSON save/load (F5/F9;
see [PROGRESS.md](PROGRESS.md) for scope and verification; in-flight projectiles
are not preserved).
Classic switch textures and buttons supply the bounded feedback above.

Still incomplete: fist/Berserk melee and exact powerup parity,
monster states/AI/physics/RNG,
other moving-sector/switch specials, other boss actions,
pre-level menus, multiplayer and original visual/timing/audio
parity. No PWAD overlay stacking, Hexen/UDMF, extended nodes, Boom/MBF/DEHACKED or
mod compatibility. Only medium-skill single-player flags are selected. Missing
assets/types produce explicit diagnostics; missing textures use a diagnostic
pattern. These gaps can block campaign progression. Loading/rendering all maps
does not prove they are completable.

The bundled unmodified Freedoom 0.13.0 Phase 1 comes from the
[official release](https://github.com/freedoom/freedoom/releases/tag/v0.13.0).
Its source archive `freedoom-0.13.0.zip` has SHA-256
`3f9b264f3e3ce503b4fb7f6bdcb1f419d93c7b546f4df3e874dd878db9688f59`.
The BSD license and contributor/music credits are in
`assets/freedoom/COPYING.txt`, `CREDITS.txt`, and `CREDITS-MUSIC.txt`.
Owning another WAD does not grant permission to redistribute it.
