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
| Linux | X11 (`libX11.so.6`), ALSA (`libasound.so.2`) | x64 AOT, desktop/input loop and ALSA null-device output verified; Wayland needs XWayland |
| Windows | Win32/GDI, waveOut (`winmm`) | x64 AOT, engine tests and bounded desktop presentation verified in CI; interactive input/audio unverified |
| macOS | AppKit/CoreGraphics/QuartzCore, AudioQueue | ARM64 AOT, engine tests and bounded desktop presentation verified in CI; interactive input/audio unverified |

The window is initially 960x600 with an internal 640x400 software framebuffer.
Linux requires a 24/32-bit TrueColor X11 display. Audio uses the default OS device;
if none exists, use `--no-audio`. Failure to open a requested device is reported,
not silently treated as success. Run macOS from a graphical login session.

## Controls

WASD moves and strafes; arrows turn (up/down also move). Click once to capture
the mouse, then use mouse movement to turn and the left button or Ctrl to fire.
E/Space uses a door or switch, Shift runs, 1 selects pistol, 2 selects an acquired
shotgun, Tab toggles the automap, R restarts, and Esc quits. Losing window focus
releases mouse capture and held keys. After an exit, fire/use advances to the next
map using classic episode/DOOM II normal and secret exit routes. Episode finales
stop at the completion screen; start another episode with `--map E2M1`, etc.

## Implemented

- IWAD/PWAD directory bounds checks, classic map records and BSP validation.
- BSP traversal and clipped convex subsector polygons, perspective-correct wall,
  floor and ceiling textures, depth-tested transparent sprites and masked walls,
  sidedef offsets/pegging, sector light/distance shading and sky backgrounds.
- Cylinder/wall and actor collision, sliding and 24-unit steps, sector headroom.
- Pistol/shotgun hitscan, ammo, health/armor, keys, pickups, barrels, enemy deaths
  and drops; simplified enemy chase, melee, hitscan and projectile attacks.
- Common manual/tagged doors, key checks, door obstruction reopening, use/walk
  exits, tagged lower/wait/raise lifts, lower-to-lowest and raise-to-next/24 floors,
  tagged walk teleports, damaging floors, secrets, death/restart and map progression.
- WAD DMX sound decoding/resampling and a 16-voice native PCM effects mixer.
- HUD, weapon overlays, automap, headless framebuffer export, original demo and
  package-free deterministic behavioral tests.

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
These gaps can block authentic WAD progression; **full original-DOOM parity is
unfinished**.

## Verification

```sh
dotnet run -c Release -- --self-test
dotnet run -c Release -- --demo --headless --frames 35 --output demo.ppm
dotnet run -c Release -- --wad /path/to/freedoom1.wad --validate-maps
./artifacts/linux-x64/DoomSharp --self-test
```

`--self-test` runs 37 checks using a WAD generated in memory: malformed data, BSP queries,
collision/occlusion, patch transparency, texture composition, DMX resampling,
textured rendering, classic weapon-patch offsets, a pickup/door/combat/step/exit/restart
sequence, lifts carrying the player, teleports, sky depth and secret exit routing. The demo
assets are procedurally generated source-owned content; `--demo-wad path.wad`
can write the fixture for inspection. `--validate-maps` loads and renders each
map at its player start, reporting unsupported things and missing textures.
`--headless` opens neither a window nor an audio device. `--frames N` also bounds
desktop runs. PPM exports contain only the assets from the WAD you supply.

Freedoom 0.13.0's 36 Phase 1 and 32 Phase 2 maps have been loaded and rendered
at their player starts, with zero missing textures. This is parser/renderer
compatibility evidence, **not** a campaign-completion claim. The official release
archive's SHA-256 was checked against its published checksum; those assets are
not committed or bundled. A separate Linux/Xvfb test injected OS keyboard events
to collect the shotgun, open the door, kill the enemy and exit the demo, and
exercised mouse capture plus the ALSA mixer against a null output device.
Physical audible playback has not been verified.

CI builds and publishes Native AOT separately on Linux, Windows and macOS and
runs the behavioral tests, headless demo and a bounded native desktop smoke run
on each. A smoke window is not evidence of interactive input or audible sound.
