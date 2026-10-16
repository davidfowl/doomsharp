# C# fixture generation and regression tools

Run from the repository root with .NET 10:

```sh
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release -- tools/e2e-fixtures/generated
dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64
SOURCE=$(git rev-parse HEAD)
dotnet run --project tools/Checks -c Release -- all artifacts/linux-x64/DoomSharp tools/e2e-fixtures/generated artifacts/checks "$SOURCE"
```

Use `headless` or `engine` instead of `all` on Windows/macOS, and adjust the
executable path (`DoomSharp.exe` on Windows). `native` allocates its own Xvfb
display and needs Linux, X11/XTest and ALSA. Each evidence directory must be new.
Results are written even when assertions fail; the runner exits nonzero.

`Generator.cs` preserves the original 15 gameplay and 10 malformed fixtures.
The original portal fixture SHA-256 remains
`0e88a988fdde17f2f1b119cf253fc288d0ff19f6a147b9edcfde4abfbd1f9ba7`;
the lift fixture remains
`e86d622806c6bf68ad3ba1facfeecd312fe04d27ea1b0dfdd4fff288b6297525`.
`../ScenarioFixtures.cs` generates additional original synthetic weapon,
tagged-door and boss scenarios. Every generated WAD is listed in `SHA256SUMS`.
All assets are generated original data, not proprietary game artwork.

The production project excludes tools and browser sources explicitly. Tool
projects compile their own sources; no `.cs.txt` workaround or Python is needed.
Fixture generator outputs stay under `.build`, keeping the source tree clean.

`tools/Checks` has three evidence levels:

| Mode | What it observes | What it does not establish |
|---|---|---|
| `headless` | Actual executable exit, diagnostics and complete 640x400 P6 export for every WAD | Native input, interactive gameplay or pixel-perfect visual correctness |
| `engine` | Public C# input and engine state for movement, locks/keys, weapons, progression, death and boss actions | OS input/render/audio paths |
| `native` | Real X11 windows, synthetic XTest input, framebuffer readback, ordinary final CLI output and ALSA-null PCM submission | Physical devices, audible playback, other OSs or original-game/campaign parity |

The C# suite consolidates the former Python fixture/executable/native tools. It
does not re-establish every historical color-coded weapon/door/boss oracle or
compare new results to old runner hashes. Historical results in git history and
`PROGRESS.md` retain their original scope. Current checks use new assertions and
new evidence; do not relabel engine-state assertions as native observations.

Output includes `results.json`, per-process logs and exported frames. Native
sessions additionally record frame hashes, key events, full executable SHA-256
and caller-supplied source commit in `session.json`. Source provenance is not
cryptographically inferred from the binary. Pin the source revision when
publishing, and retain the produced executable with its evidence.

The malformed cases must fail with exit 1 and a `DoomSharp:` diagnostic.
In particular, an invalid BLOCKMAP offset is now rejected rather than accepted
by an unused lump path; empty BLOCKMAP data remains supported because collision
scans linedefs directly.

Browser checks are separate in `tools/BrowserChecks`. See the root README for
static publishing, the C# static server and Chromium/Firefox/WebKit commands.
