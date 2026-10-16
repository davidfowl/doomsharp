# Lift qualification

The C# `native` mode in `tools/Checks` replaces the Python step-off and cycle
runners. It launches the actual published executable and observes two natural
lower/wait/raise/carry/step-off cycles, using XTest keyboard events and X11
framebuffer readback. It does not call engine APIs or inject game state.

```sh
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release -- tools/e2e-fixtures/generated
dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64
dotnet run --project tools/Checks -c Release -- native artifacts/linux-x64/DoomSharp tools/e2e-fixtures/generated artifacts/native "$(git rev-parse HEAD)"
```

Requires .NET 10, Linux, Xvfb, libX11, libXtst and ALSA. The runner creates an
isolated display for each session and records native key events, P6 captures,
frame hashes, process logs and the final ordinary CLI summary. Evidence output
must be a new directory. The source commit is supplied by the builder.

The unchanged original lift fixture SHA-256 is
`e86d622806c6bf68ad3ba1facfeecd312fe04d27ea1b0dfdd4fff288b6297525`.
Each cycle requires:

- The visible platform lowers after use.
- Boarding and waiting give identical stationary frames.
- During raising, ceiling geometry changes while the camera-relative floor
  boundary stays fixed, showing player carry.
- Perspective ceiling/floor landmarks imply destination height64 within2 units.
- Consecutive top frames are identical.

After each cycle the player backs off before retriggering. After the second, the
ordinary CLI position must be `X <= -32`, beyond the portal's player-radius
boundary. This suite does **not** reuse the historical Python same-build
step-off-reference file/hash gate; its two-cycle acceptance is a new observation,
not a claim that the historical reference comparison was rerun.

Timing is wall-clock/fixture-specific and screenshots are from a virtual X
server. Results do not establish original-game timing, exact bottom-height,
physical input, audio, Windows/macOS behavior or natural-map campaign completion.
Historical investigations and evidence remain in git history.
