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
tagged-door, boss, stair, crusher and switch scenarios. Every generated WAD is listed in `SHA256SUMS`.
All assets are generated original data, not proprietary game artwork.

The 44 `stairs-<special>-<variant>` fixtures cover actions 7/8/100/127:
three-step routes to an otherwise unreachable raised destination, flat-texture
and linedef-direction boundaries, missing/zero tags, independent tagged seeds,
active floor movers started by ordinary crossing input, occupied headroom,
raised starting floors and back-side activation. Engine checks assert exact
targets and 35-Hz speeds, consumption/retry, obstruction/resumption and restart.
Native checks use eight fresh processes: four tagged routes must reach X>224;
four missing-tag controls must stay at 160<=X<=176. They capture the initial,
built and traversed frames and retain the usual input/process/hash records.
These are synthetic progression checks, not authentic-map campaign acceptance.

The 84 `crusher-<special>-<variant>` fixtures cover actions 6/25/77/141 and
coupled stop actions 57/74. The 222 engine assertions cover exact per-tick speed,
relative endpoints and repeated cycles, silent/normal effects, use rejection,
once-only/repeatable activation, missing/zero/multiple tags, overlapping sector
footprints, four-tick damage and armor, survival/death, monster kills, barrel
explosions, crushed drops, slowed-downstroke/normal-upstroke recovery, stop/resume
in both directions, suspended damage, preserved original speed and existing
door/floor ownership. Actor movement barriers are ordinary monster-blocking
linedefs, not injected state.

Eight fresh native processes cross into active or stopped traps using XTest:
each active variant must report health0; each paused control must report
health100 with identical stationary frames across four seconds. Two type6
processes additionally require sound events and nonzero ALSA-null PCM. Each
retains initial/active-or-paused/final captures and input/session/hash records.
These are local Linux virtual-device observations, not audible sound,
browser/other-OS gameplay or original-engine/campaign parity.

The 112 `switch-<special>-<variant>` fixtures cover 24 existing use-switch actions
without changing earlier fixture assets. Engine checks cover success/denial,
front/back sides, tags/keys/busy/no-motion cases, one-shot versus repeatable
consumption, exact pair/state/upper-middle-lower priority, missing partner
fallback, unsupported custom names, 35-tick restoration, retriggers, independent
buttons and restart/map-change cleanup. Test-only solids distinguish off red
from on green; paired patches and click tones are generated original data.

Nine native processes observe the actual framebuffer before activation, after
Use and after the reset deadline. Positive upper/middle/lower surfaces require
more than 500 on-color pixels, excluding the HUD; controls must never turn green.
S1 actions remain latched, SR99 resets, and one SR99 process submits nonzero
null PCM. Manual-door control artwork can disappear as its door opens; its
engine check independently requires that the door actually moves.
The browser runner uses the same fixtures for visible S1/SR/key-denied controls
and verifies that pausing preserves an active button for more than one second.
Real-time host checks complement, not replace, exact engine tick assertions.

The 68 `floor-highest-<special>-<variant>` fixtures add actions 19/45/83/102
without changing any of the earlier 343 WADs. They distinguish highest from
lowest/next-lower/nonadjacent floors, signed heights, equal/higher destinations,
an isolated tagged sector, self-reference, missing/zero/multiple tags, earlier
floor/door/crusher owners, door takeover denial, partial success, trigger side/type, riding players
and blocked upward snaps. Exact engine checks include speed, endpoints, stable
targets, failure/retry, consumption, unchanged floor properties, reset deadlines
and restart. A few explicit neighbor-height changes test later activation and
snapshot behavior; mover ownership and obstructions start through ordinary input.

`floor-engine` and `floor-native` select only this increment's checks; `engine`,
`native` and `all` also include them. Use the same five runner arguments shown
above, with a fresh evidence directory. Eight native processes activate each
action and its missing-tag control: the lowered route must reach X>208, whereas
the original 40-unit obstruction must stop at 72<=X<=80. Use surfaces also check
visible success/denial and reset/latch behavior; SR45 submits ALSA-null PCM.
The browser runner adds visible SR45/S1-102/missing-tag controls and pauses SR45
across its reset deadline. These host checks are synthetic, not a Freedoom
campaign playthrough or bit-exact classic-engine comparison.

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
