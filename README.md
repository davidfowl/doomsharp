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
| `linux-x64`, `linux-arm64` | X11, ALSA | `libX11.so.6`, `libasound.so.2`; Wayland requires XWayland. This change was exercised on Linux x64 only. |
| `win-x64`, `win-arm64` | Win32/GDI, waveOut | No third-party native window/audio library. Windows runtime behavior was not reverified in this Linux workspace. |
| `osx-x64`, `osx-arm64` | AppKit/CoreGraphics, AudioQueue | Run from a graphical login session. macOS runtime behavior was not reverified in this Linux workspace. |
| Browser WASM | Canvas 2D, WebAudio, keyboard/pointer events | Desktop Chromium, Firefox and WebKit smoke checks; not proof of Safari/macOS or mobile-device behavior. |

The desktop framebuffer is 640x400 in an initially 960x600 window. Linux requires
a 24/32-bit TrueColor display. The CI workflow publishes and exercises six desktop
OS/architecture targets, plus three browser engines. A configured job is **not**
evidence that this local revision ran on that target.

## Browser WebAssembly

```sh
dotnet workload install wasm-tools
dotnet run --project Browser/DoomSharp.Browser.csproj -c Release --urls http://localhost:5187
```

Open `http://localhost:5187`. Choose **Play Freedoom**, **Test level**, or a local
classic WAD (up to 64 MiB), then click the game. The optional Map field accepts a
map name such as `E1M2`; leave it blank for the first map. A local WAD is processed
on your device, not uploaded. Use only lawfully obtained assets.

The browser runs the same C# engine source files as the desktop executable.
It uses the .NET WebAssembly runtime via Blazor, not a server-side game process.
Release builds compile the engine ahead of time for WebAssembly performance;
the first publish is slower and needs the `wasm-tools` workload.
The renderer is software-based; frame rate depends on map complexity and browser.
Clicking enables audio and requests pointer lock. Keyboard controls still work
when pointer lock is unavailable. Esc, loss of focus or a hidden tab pauses the
game and clears held input; click to resume. **Mute** suppresses new sound effects.
Mobile/touch controls, music, gamepads and persistent saves are not implemented.

Publish a static site:

```sh
dotnet publish Browser/DoomSharp.Browser.csproj -c Release -o artifacts/browser
dotnet run --project tools/Serve -c Release -- artifacts/browser/wwwroot --urls http://localhost:5187
```

Deploy the **contents** of `artifacts/browser/wwwroot` to any static HTTP host that
serves `.wasm` as `application/wasm` and `.dat`/`.wad` as binary data. Do not open
`index.html` as `file://`. Pointer lock/audio require browser permission and a
user gesture; use HTTPS in deployment (localhost is suitable for development).
For hosting under a subpath, change the `<base href="/">` in
`Browser/wwwroot/index.html` to that path before publishing. Keep the bundled
Freedoom license and credits with the site.

## Controls

WASD moves/strafes; arrows turn (up/down also move). Click once to capture the
mouse, then move it to turn. Ctrl or left click fires; E/Space uses; Shift runs.
1 selects the pistol, 2 an acquired shotgun, 4 an acquired chaingun, and 5 an
acquired rocket launcher. Tab toggles the automap; R restarts. Esc quits desktop
or pauses the browser. Losing focus clears input and releases mouse capture.

Use/fire advances after an exit where a next map exists. Living players retain
health/inventory; dead players start fresh. Episode finales offer replay.
`--help` lists desktop options; `--frames N` also bounds desktop presentation.

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

Browser runtime checks use the C# Playwright API:

```sh
dotnet run --project tools/BrowserChecks -c Release -- install --with-deps chromium firefox webkit
# In another terminal, serve the published site using tools/Serve as above.
dotnet run --project tools/BrowserChecks -c Release -- http://localhost:5187 tools/e2e-fixtures/generated artifacts/browser-chromium chromium
dotnet run --project tools/BrowserChecks -c Release -- http://localhost:5187 tools/e2e-fixtures/generated artifacts/browser-firefox firefox
dotnet run --project tools/BrowserChecks -c Release -- http://localhost:5187 tools/e2e-fixtures/generated artifacts/browser-webkit webkit
```

The browser checks observe actual Canvas output, keyboard movement, pointer
capture/turning where supported, automap, pause, visible-HUD-confirmed demo
completion/restart, WebAudio scheduling, mute, bundled/local WAD
loading and visible load errors. Browser tooling brings Playwright's own driver/browser dependencies;
the application itself does not depend on Playwright.

On headless Linux, Firefox needs a working audio backend even to schedule
WebAudio. CI supplies a private PulseAudio null sink and exports its `PULSE_SERVER`;
local desktop sessions normally already have PulseAudio/PipeWire. A missing
backend is a failed audio check, not silently reported as verified sound.

Synthetic input, null PCM and headless browsers do not establish physical-device
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
