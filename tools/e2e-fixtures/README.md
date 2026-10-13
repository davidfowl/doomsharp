# Original full-executable E2E fixtures

Optional developer tooling, not game-runtime dependencies or a mandatory CI gate.
All geometry, pixels, palettes and sprites are independently generated. No
proprietary WAD/assets, original DOOM source, third-party engine, test framework,
unit tests, generator self-checks or `--self-test` invocation are included.
The existing `--demo-wad` supplies one encounter; these scenarios add boundary
conditions and malformed inputs rather than replace that demo.

From the repository root with .NET 10:

```sh
dotnet run --project tools/e2e-fixtures/FixtureGenerator.csproj -c Release -- \
  tools/e2e-fixtures/generated
```

This writes **15 gameplay PWADs**, **10 deliberately malformed PWADs**, and
`tools/e2e-fixtures/generated/SHA256SUMS`. The manifest contains SHA256 for each
exact generated file; none of those files is committed. Generation is not
gameplay acceptance. These files load directly through DoomSharp's `--wad`;
they are not complete original-game IWAD replacements or certified vanilla PWAD
overlays. `Generator.cs.txt` is compiled explicitly by the tool project so the
root SDK project's recursive `*.cs` glob does not pull tool code into the engine.
Build intermediates likewise stay in the hidden `.build/` directory, outside
that root compile glob; no engine project exclusions are changed.

Use an actual independently built/downloaded DoomSharp executable:

```sh
GAME=/absolute/path/to/DoomSharp
"$GAME" --wad tools/e2e-fixtures/generated/portal-equal.wad --map E1M1 \
  --headless --no-audio --frames 35 --output /tmp/portal-equal.ppm
python3 tools/e2e-fixtures/run.py "$GAME"
python3 tools/e2e-fixtures/run.py "$GAME" --native
```

The runner uses only Python's standard library. `--native` additionally needs
Linux X11/XTest libraries and installed `Xvfb`; it creates an isolated display,
starts the real game window and sends native keyboard events. None of these
automation facilities is a production-engine dependency. On Windows/macOS, use
the same generated WADs with the native executable and manual controls.

Logs, complete P6 frame exports and machine-readable outcomes go under
`tools/e2e-fixtures/e2e-evidence/`, ignored by Git. `results.json` binds observations
to the executable's SHA256 and records native inputs. It is not an original-game
visual oracle. New whole runs replace results; only explicitly selected native
reruns retain other observations for the same binary:

```sh
python3 tools/e2e-fixtures/run.py "$GAME" \
  --only-native native-key-door-combat-exit
```

Unknown selection, timeouts, process/frame failures and failed native acceptance
produce errors/nonzero status. Accepted malformed input is recorded explicitly,
not converted into a successful rejection. This tooling is not a required CI gate.
No audio devices are opened; physical keyboard/audible audio are not verified.
Native key holds are wall-clock schedules, not exact tic-indexed commands.

The separate [native lift reproduction](LIFT.md) publishes the complete input
schedule and `lift.py` for a known baseline step-off failure. It observes carry
through native-window frames and asserts retreat into the lower room. Baseline
exit1 is intentional evidence of failed product behavior, not a green lift-cycle
result; the original boarding-only scenario below remains observation-only.

## Scenarios and expected product observations

Fresh process per scenario. W moves, E uses, Ctrl fires, R restarts, Esc stops.
Every key hold is released, followed by an80ms gap.

| WAD / scenario | Executable input | Observable acceptance |
|---|---|---|
| All15 gameplay WADs | Headless35 frames | Successful load/simulation and640x400 PPM export; inspect images separately for visual correctness. |
| `portal-equal`, `step-24`, `clearance-56`, `masked-passable` | W1s | Cross x=0 into east sector. |
| `step-25`, `clearance-55`, `overlap-55`, `closed-portal`, `flag-blocked`, `masked-blocked` | W1s | Remain west, with radius stopping around x=-16. |
| `blue-door` | W0.3s,E0.1s,W1s | No-key use must not open blue lock. |
| `e2e-wrong-key` | W0.8s,E0.1s,W1s | Red card/shotgun collected, blue lock still blocks. |
| `lift-use-repeat` | W0.3s,E0.1s,W2.5s | Record boarding after use; observation only, not certified wait/return/repeat timing. |
| `e2e-key-door-combat-exit` | W0.8s,E0.1s,Ctrl2s,W+Ctrl1s,W0.7s,E0.1s | Blue key permits door; shotgun=True,kills=1,positive health,finished=True. |
| `native-map-advance` | Full encounter, wait0.3s,E0.1s | Next room's distinct spawn(-64,0),kills0,finished=False; retain shotgun/damaged health rather than restart at(-96,0). |
| `native-deathless-restart` | Full encounter,R0.1s | Return to(-96,0),health100,kills0,no shotgun,finished=False. Not death/restart evidence. |
|10 malformed variants | Each through `--wad --headless --frames1` | Observe explicit rejection versus acceptance, timeout or abnormal termination; preserve each log. |

The geometry is two rectangular sectors, a directed portal at x=0, split wall
segs with texture offsets0/64, negative coordinates, leaf-bit BSP children, shared
BLOCKMAP candidate lists and zero REJECT. Masked posts include opaque palette
indices0/255 and transparent gaps. Encounter WADs include a second map E1M2.

## Evidence limits and original-game follow-up

Publication verification exercised the actual Linux Native AOT executable at
[engine commit3f900864194b6dfeb1fd9c998973050314022464](https://github.com/davidfowl/doomsharp/commit/3f900864194b6dfeb1fd9c998973050314022464),
[CI run38365995731](https://github.com/davidfowl/doomsharp/actions/runs/38365995731),
binary SHA256
`615e5a43c67fa84361eb156b6f176b0bf2575da2306e7b6efe5722f7e8cbee27`.
It produced15 native outcome passes, one observation-only lift run and nine
malformed rejections. **`bad-blockmap-offset.wad` was accepted and rendered.**
That may be an unused accelerator, but is not proof of BLOCKMAP validation.
Publishing these fixtures does not claim that a newer executable passes them.

Original-WAD campaign/secret/boss progression, complete lift cycles,35Hz
cross-refresh behavior, sprite/portal/sky visual reference comparisons, actual
death/restart, physical input/audio and Windows/macOS execution remain separate
full-executable obligations. Use only lawfully obtained WADs; no such assets are
included here. In particular, E1M8 last-Baron tag666 lowering is not immediate
victory; sector11 exit behavior and episode-specific secret returns must work.

Binary format provenance: Matt Fell, *Unofficial DOOM Specs* v1.666,
[complete public text](https://github.com/chocolatiers/doom-faqs/blob/e69d74f8acd932f9133b26ef0950d7a15b292221/unofficial-doom-specs.txt),
sections2,4-1 through4-11,5-1,6,8-1/8-2/8-4/8-5/8-6.
No source specification text is redistributed with the generator.
