# Native lift carry and step-off reproduction

This is an executable reproduction of a **known failed step-off**, not a green
full-lift-cycle acceptance result. `lift.py` launches a published game, sends
ordinary W/E/S/Escape events through XTest, and reads the actual window with
XGetImage. It neither calls engine methods nor changes game state. The existing
original `lift-use-repeat.wad` is sufficient; no new fixture or assets are needed.

## Build and run

Use Linux, Python 3 (standard library only), Xvfb, libX11 and libXtst, .NET 10,
and the engine's normal Native AOT publishing prerequisites. The runner starts
its own isolated Xvfb display. Do not wrap it in a shared game/display process.
Generate the fixture from this tooling checkout, then publish the original
baseline in a separate worktree:

```sh
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release -- \
  tools/e2e-fixtures/generated
sha256sum tools/e2e-fixtures/generated/lift-use-repeat.wad

BASELINE=$(mktemp -d /tmp/doomsharp-lift-source.XXXXXX)
PUBLISH=$(mktemp -d /tmp/doomsharp-lift-native.XXXXXX)
git worktree add --detach "$BASELINE" 1ff2c1e43afa6d806f59388a3b1aed6cb03738b2
dotnet publish "$BASELINE/DoomSharp.csproj" -c Release -r linux-x64 -o "$PUBLISH"
sha256sum "$PUBLISH/DoomSharp"
python3 tools/e2e-fixtures/lift.py "$PUBLISH/DoomSharp" \
  --source-commit 1ff2c1e43afa6d806f59388a3b1aed6cb03738b2
```

**Expected baseline runner exit: 1**, with
`step-off-clears-portal-radius-after-prolonged-backward-input FAIL`.
The game itself quits normally with exit 0. Do not suppress the runner failure
or describe the successful publish/game exit as successful lift acceptance.
Each invocation prints a new ignored evidence directory. An optional
`--output-dir PATH` must name a directory that does not exist; old evidence is
never overwritten. It contains `result.json`, `game.log`, `final.ppm`, and
11 native-window phase captures. Startup/process/readback failures are errors,
not substitute passes; partial phase evidence is retained.

For a separately pinned current-source comparison, use a separate publish and
evidence directory; never replace the baseline record:

```sh
CURRENT=$(git rev-parse HEAD)
CURRENT_PUBLISH=$(mktemp -d /tmp/doomsharp-lift-current.XXXXXX)
dotnet publish DoomSharp.csproj -c Release -r linux-x64 -o "$CURRENT_PUBLISH"
python3 tools/e2e-fixtures/lift.py "$CURRENT_PUBLISH/DoomSharp" \
  --source-commit "$CURRENT"
```

`--source-commit` is the caller's full build revision, not something inferred
from the executable. The manifest separately hashes the supplied executable,
fixture, runner, game log, final export and every native phase frame. Rebuilding
with another compiler/SDK/path may produce another executable hash; retain that
as a new observation rather than claiming binary identity.

For manual reproduction, launch the published executable with
`--wad tools/e2e-fixtures/generated/lift-use-repeat.wad --map E1M1 --no-audio`.
Face east as spawned; W about 0.30s, E about 0.06s; at about 1.15s after E,
W about 0.35s; release all movement until about 5.50s after E; then S at least
2.50s. The open west room should be reachable without lowering the lift again.

## Contract and measured baseline

The player starts at (-64,0), facing east. The west room spans x[-128,0] with
floor0; the east platform spans x[0,128], starts at floor64, has ceiling128 and
tag7. Both span y[-64,64]. The x0 portal is front-usable, repeatable special62.
There is no separate upper landing: the destination is the restored platform
height64. Stepping off means retreating west into the lower room, then being
able to use the lift from its front side again.

Original observed baseline:

| Item | Exact version / SHA256 |
|---|---|
| Merged source | `1ff2c1e43afa6d806f59388a3b1aed6cb03738b2` |
| Published Linux-x64 Native AOT executable | `578dfdb71bb4422a1d704fb98984d9f35f917b0d9a23b107ac3a2473522b1ea0` |
| Existing generated lift WAD | `e86d622806c6bf68ad3ba1facfeecd312fe04d27ea1b0dfdd4fff288b6297525` |

The original focused run rendered601 frames and printed health100,
position=(-4.00,0.00), finished=False. After 0.50s backward input and another
2.00s backward input, the two retreat images were byte-identical. The failed
gate is **observed X=-4.00; expected X<=-32**. The threshold clears the x0
portal and radius16 comfortably, before the west wall at x=-128; no intended
obstacle prevents that retreat.

| Phase | Expected | Original native observation |
|---|---|---|
| Use/lower | Platform descends toward neighboring floor0 | Visible lowering exposes the far wall/floor; not an independently exact bottom-height measurement. |
| Board/wait | Enter east platform and wait without movement | Board/wait images are identical; subsequently carried upward, establishing boarding. |
| Automatic raise/carry | Carry the resting player, not only move the floor | Ceiling boundary enters/moves while the wall/floor boundary stays fixed relative to the camera. |
| Destination | Return to floor64 and stop | Top/stable images identical; fixture perspective estimates floor63.850340136, within tolerance2 of64. |
| Step off | Retreat beyond the portal radius into the west room | **FAIL:** prolonged S leaves X=-4.00; retreat images identical. |
| Retrigger/second return | Repeat from a successfully vacated platform | Earlier exploratory run observes another descent/boarding/return from the snagged seam; **not** successful step-off or full-repeat acceptance. |

The resting eye offset41, ceiling128, viewport horizon175 and 640x400 render
geometry allow a fixture-specific height estimate from ceiling/floor edges.
The runner eliminates horizontal camera distance from that perspective ratio.
It does not use final XY to infer vertical displacement. Its grayscale
landmark detector depends on this original fixture's textures/palette and
current rendering geometry; changed graphics require revisiting the oracle.
The carry gate also requires an intermediate rising ceiling edge and stationary
wall/floor edge. Missing landmarks fail; they are not silently skipped.

## Original event and phase timings

These are actual recorded monotonic seconds from runner startup, not exact game
tics. The published runner repeats the nominal holds/schedule; OS scheduling
can change tick counts, positions and frame hashes. Key releases settle120ms.

Focused failed-step-off run:

| Key | Down (s) | Up (s) |
|---|---:|---:|
| W approach | 0.221311535 | 0.521552520 |
| E activate | 0.661238326 | 0.721398690 |
| W board | 1.822459030 | 2.172683089 |
| S step off | 6.178337454 | 6.678550580 |
| S prolonged retreat | 6.813250329 | 8.813916458 |
| Escape | 8.948789401 | 8.998976945 |

All captures were960x600 native window pixels; boundaries are native Y pixels.
A missing ceiling boundary means it was outside the detector's visible range.

| Capture | Time (s) | Ceiling Y | Wall bottom Y |
|---|---:|---:|---:|
| 01-raised-approach | 0.659120292 | absent | 524 |
| 02-first-descent | 1.074597087 | absent | 524 |
| 03-first-bottom | 1.820936545 | absent | 398 |
| 04-first-board | 2.305706290 | absent | 482 |
| 05-first-bottom-wait | 3.371426788 | absent | 482 |
| 06-first-rise | 5.120787487 | absent | 482 |
| 07-first-rise-late | 5.370854982 | 85 | 482 |
| 08-first-top | 5.821876087 | 137 | 482 |
| 09-first-top-stable | 6.176376986 | 137 | 482 |
| 10-step-off | 6.811659752 | absent | 524 |
| 11-prolonged-step-off | 8.947201304 | absent | 524 |

An earlier17-frame exploratory repeat run had these native events:

| Key | Down (s) | Up (s) |
|---|---:|---:|
| W approach | 0.216184736 | 0.516531217 |
| E activate | 0.651083995 | 0.711220712 |
| W board | 1.812173565 | 2.162398622 |
| S retreat | 6.162591164 | 6.662896922 |
| E retrigger | 6.794373668 | 6.854503987 |
| W second board | 7.956002783 | 8.456315110 |
| Escape | 12.304154226 | 12.354339617 |

Its17 captures occurred at0.649298928,1.061602651,1.809995549,
2.291368941,3.361989851,5.110026591,5.361684341,5.810730812,
6.160409137,6.792552186,7.205242006,7.953033787,8.584417557,
9.502275497,11.261092262,11.954614948,12.302631464 seconds, respectively.
The first ascent ceiling edge moved71->143 while the wall bottom stayed472.
After the snagged retreat, the second descent and automatic return were visible;
second top/stable ceiling edges were76, with the floor edge clipped at524.
Final ordinary output reported863 frames, X68.86. Its recorded assertions were
empty: it is observation evidence, **not a pass**. The published runner instead
ends after the prolonged retreat and asserts the failure directly.

## Evidence references and implementation boundary

Historical raw captures/logs remain local, not bundled or publicly downloadable.
New runs produce their own accessible-on-your-machine evidence. Original local
relative references and hashes:

| Evidence under `e2e-evidence/` | SHA256 |
|---|---|
| `lift-step-off-reproduction/result.json` | `d535e9e97b5b80a987412f9213c0703bf29c8cdf2f51e20c8b745939596b25c4` |
| `lift-step-off-reproduction/game.log` | `c381e231547ac1f85bb41fa4be73e6087751b10c2966447981415eb5e7638d62` |
| `lift-step-off-reproduction/final.ppm` | `270b16833fc53c71a959422d7b846b0a4d89dcb3a0da80e9a6fa49f767f3b80b` |
| Each focused retreat PPM,10/11 | `b929567478711920c0e7b4596ce17bcc5664d209d00fc6d947473584516000b3` |
| `lift-cycle/result.json` (exploratory) | `5dae6afd4bc15f33a69ec31a4ab9bf9b60b6a96cab98b45c6b95358090006ae5` |
| `lift-cycle/game.log` (exploratory) | `46a884164ff790610daf37b6979e5b9bc467c87a916ba412f4a8fa9c4966b394` |

The historical collector SHA256 was
`2c9d339396539c9777c0c2b34e464168ce40007793acd6e7a44cf4bfb72598af`.
This published version makes the focused scenario the only path, adds explicit
source provenance, a lowering gate and complete-export checks, fails missing
height evidence, and uses new evidence directories. Original evidence is
preserved rather than rewritten as if generated by the updated runner.

Publication validation replayed this updated runner, SHA256
`21eaf1720e97e46e5dd2a598ad1d89e431f436cbbd2234ecc46e0c0cfbad106c`,
against both the original executable above and separately pinned merged source
`5f6572cf158258f88e38d348e174e30c923b9b62`. The latter was freshly published as
Linux Native AOT, executable SHA256
`b9cbe8dcc04212fb736757810829905d2eb236b3f57f628615ba0c521044b45c`.
Both runs passed the five lowering/wait/carry/top evidence gates and failed the
unchanged step-off gate at X=-4.00, with game exit0 and runner exit1. Their
new manifests are `lift-1791881717795915390/result.json` (SHA256
`60bb070f2db3a994cc5f5198d2e7896575aac1a341479e9ad3a72615d6f48188`)
and `lift-1791881759797209092/result.json` (SHA256
`45923243c4922aa0640c25bf8dd55555b12a13236a55b59430e260ccb039b21f`),
respectively. All22 new native phase captures had complete P6 payloads and
matching recorded hashes. Root Native AOT publishing succeeded after fixture
generation, preserving the tool's compile isolation; the current native
executable also loaded/rendered the lift fixture for35 headless frames.

Read-only candidate interaction:
[Game.cs227-228](https://github.com/davidfowl/doomsharp/blob/1ff2c1e43afa6d806f59388a3b1aed6cb03738b2/Game.cs#L227-L228)
sets Feet from the center sector;
[Wad.cs256-268](https://github.com/davidfowl/doomsharp/blob/1ff2c1e43afa6d806f59388a3b1aed6cb03738b2/Wad.cs#L256-L268)
still checks the raised floor of a portal within radius16. Crossing the center
into floor0 while still touching floor64 makes further retreat look like a
64-unit step up against the24-unit limit. This is an explanation supported by
source inspection and executable evidence, not an isolated-method test or fix.

Publishing the reproduction does not fix this transition or prove playable
original DOOM. Even a future exit0 proves only this carry/step-off scenario;
`fullLiftCycleVerified` remains false until successful repeat-cycle evidence
exists. Audio, physical input, Tab, Windows/macOS, original timing parity,
campaign progression and real-map compatibility are outside this reproduction.
The missing PISGA0 warning is expected in this minimal fixture, not the step-off
defect. No proprietary assets, engine source copies, game-state injection,
unit framework, engine-method calls or forced transitions are used.
