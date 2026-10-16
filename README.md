# DoomSharp

A clean-room C# classic WAD engine with native desktop hosts. Gameplay, WAD
loading, software rendering, generated assets and development tools are C#.

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
| `linux-x64`, `linux-arm64` | X11, ALSA | `libX11.so.6`, `libasound.so.2`; Wayland requires XWayland. This change was exercised on Linux x64 only. |
| `win-x64`, `win-arm64` | Win32/GDI, waveOut | No third-party native window/audio library. Windows runtime behavior was not reverified in this Linux workspace. |
| `osx-x64`, `osx-arm64` | AppKit/CoreGraphics, AudioQueue | Run from a graphical login session. macOS runtime behavior was not reverified in this Linux workspace. |

The desktop framebuffer is 640x400 in an initially 960x600 window. Linux requires
a 24/32-bit TrueColor display. The CI workflow publishes and exercises six desktop
OS/architecture targets. A configured job is **not**
evidence that this local revision ran on that target.

## Controls

WASD moves/strafes; arrows turn (up/down also move). Click once to capture the
mouse, then move it to turn. Ctrl or left click fires; E/Space uses; Shift runs.
1 selects the pistol, 2 an acquired shotgun, 4 an acquired chaingun, and 5 an
acquired rocket launcher. Tab toggles the automap; R restarts. Esc quits desktop.
Losing focus clears input and releases mouse capture.

Use/fire advances after an exit where a next map exists. Living players retain
health/inventory; dead players start fresh. Episode finales offer replay.
`--help` lists desktop options; `--frames N` also bounds desktop presentation.

## Verification and development tools

All repository-owned tools are C#; there are no Python scripts or commands.
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
  ownership/ammo/cadence/combat/carry/restart/death, and Baron floor actions.
- `native` drives the actual Linux executable using XTest keyboard events and
  reads the X11 framebuffer: demo completion/restart, Tab/movement/firing HUD,
  two lift lower/wait/raise/carry/step-off cycles, and both weapons with PCM
  submission through an ALSA null device.

Results contain explicit pass/fail assertions, diagnostics, executable hashes,
caller-supplied source revision and native frame/input records. A source revision
is provenance supplied by the builder, not something inferred from an executable.
These checks replace the previous separate Python runners; they deliberately
distinguish engine checks from OS-input evidence. The new suite is **not a
bit-for-bit port of every historical weapon/door/boss framebuffer oracle**.
Previous evidence remains historical, not newly established by these checks.
See [fixture tooling](tools/e2e-fixtures/README.md) and
[lift qualification](tools/e2e-fixtures/LIFT.md).

Synthetic input and null PCM do not establish physical-device
input, audible sound, original-game parity, mobile support or campaign completion.

## Engine scope and limits

Implemented: classic IWAD/PWAD directory/geometry/BSP validation; textured
walls/floors/ceilings, sky, masked walls and sprites; collision, sliding and
24-unit steps; pistol, shotgun, chaingun and swept rocket/explosion combat;
pickups, keys, armor, simplified enemies/barrels; common doors, tagged locks,
lifts/floors, teleports, damaging floors, secrets, exits and map progression;
E1M8 last-Baron/tag666 floors; DMX effects, HUD and automap.

Still incomplete: other weapons/powerups, exact monster states/AI/physics/RNG,
stairs/crushers and every moving-sector/switch special, other boss actions,
animated textures, music/MUS, menus, saves, multiplayer and original visual/timing
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
