# Playing and publishing DoomSharp

Start with the [quick start](../README.md#quick-start) to run the desktop or Blazor host.
For the second browser runtime, see the [NetWasm instructions](../NetWasm/README.md).

## Native desktop

Other desktop modes:

```sh
dotnet run -c Release -- --fixture-demo
dotnet run -c Release -- --wad /path/to/game.wad --map E1M1
```

`--fixture-demo` plays the original two-room encounter. Desktop and regression
tools use the .NET BCL, without managed game-engine or test-framework
dependencies. Audio uses the default native device and reports failures; pass
`--no-audio` if no device is available.

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
passed all nine native/browser jobs at implementation `42a186f`.
Downloaded Linux x64 results confirm 748 executable/engine/native assertions;
the browser Release-AOT application passed 38 Chromium, 38 Firefox and 37 WebKit
runtime assertions. WebKit mouse look was unavailable, and the browser checks
did not traverse stair/crusher scenarios. See [PROGRESS.md](../PROGRESS.md) for
exact historical coverage and remaining compatibility limits; these runs do
not qualify every later revision.

## Browser WebAssembly

The site offers two browser runtimes: **.NET WASM (Blazor AOT)** at
`/doomsharp/` and **NetWasm** at `/doomsharp/netwasm/`. Both use the same C#
engine and Freedoom WAD, with separate browser modules. NetWasm targets
its narrower `netwasm0.1` API profile; only the desktop-only `Wad(string path)`
and `Renderer.SavePpm` filesystem methods are excluded from this target. It
uses the same Canvas/WebAudio browser adapter and supports the same local WAD,
keyboard/pointer controls, music/effects and local saves. NetWasm runs on the
main browser thread, and a synchronous export (notably WAD initialization) can
block interaction temporarily. The NetWasm toolchain is pre-1.0; the exact SDK
version is pinned in `NetWasm/global.json`.

### Run and deploy the browser host

The [quick start](../README.md#quick-start) runs the .NET WASM host locally. Open
`http://localhost:5187`. Bundled Freedoom Phase 1 loads and runs automatically,
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
Mobile/touch controls and gamepads are not implemented. Music
plays via a clean-room MUS/Standard-MIDI decoder and an approximate software
synthesizer; see [PROGRESS.md](../PROGRESS.md).

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

Open `http://localhost:5187/doomsharp/`, or pass that URL to the
[browser checks](DEVELOPMENT.md#verification-and-development-tools).

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
