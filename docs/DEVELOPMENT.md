# Development and verification

These commands and measurement boundaries are for reproducing published builds and
checks, not a claim that historical results qualify the current revision.

## Published build comparison

Each browser page displays a **Published build comparison**. It measures raw
Release runtime footprint (framework/module, JS, HTML and CSS), uncompressed
first-load response bytes (`PerformanceResourceTiming.decodedBodySize` for
same-origin navigation and resources in a fresh headless Chromium context),
and the median of five `performance.now()` samples of 120 consecutive C#
software renders of the paused initial E1M1 scene. Each sample crosses the
browser-call boundary once; the two JS interop paths differ.

The identical shared Freedoom WAD/credits and comparison data are excluded from
first-load size; licenses, `.br`/`.gz` sidecars, maps and PDBs are excluded from
published runtime footprint. These are **raw bytes**, not compressed HTTP
transfer size. Render time measures renderer throughput, **not** full gameplay
frame rate, startup, WAD loading, audio or input latency. Results depend on the
CI runner and browser, not just the runtime.

To reproduce on Linux, publish both runtimes per the
[NetWasm host instructions](../NetWasm/README.md), install Chromium with
`dotnet run --project tools/BrowserChecks -c Release -- install chromium`,
serve the merged site with `tools/Serve` as described there, then run:

```sh
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release -- tools/e2e-fixtures/generated
dotnet run --project tools/BrowserChecks -c Release -- netwasm http://127.0.0.1:5187/doomsharp/ artifacts/netwasm-checks tools/e2e-fixtures/generated
dotnet run --project tools/BrowserChecks -c Release -- sizes artifacts/browser/wwwroot artifacts/netwasm-checks
dotnet run --project tools/BrowserChecks -c Release -- stats http://127.0.0.1:5187/doomsharp/
```

`artifacts/netwasm-checks/render-comparison.json` and
`artifacts/browser/wwwroot/comparison.json` retain exact values. Chromium CI
regenerates the comparison after both builds and browser checks pass, before
Pages deployment. The benchmark button on each page times 120 renders on your
own machine (reload first for the initial scene).

## Verification and development tools

All repository-owned tools are C#; there are no Python scripts or commands.
The .NET WebAssembly workload supplies its upstream native compiler toolchain.
Run these commands from the repository root. [PROGRESS.md](../PROGRESS.md) records historical
qualification and compatibility notes; the commands and evidence boundaries
here supersede its old tooling instructions.
For a local static server, follow [browser publishing](PLAYING.md#run-and-deploy-the-browser-host).

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
See [fixture tooling](../tools/e2e-fixtures/README.md) and
[lift qualification](../tools/e2e-fixtures/LIFT.md).

Browser runtime checks use the C# Playwright API:

```sh
dotnet run --project tools/BrowserChecks -c Release -- install --with-deps chromium firefox webkit
# In another terminal, serve the published site using tools/Serve.
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
