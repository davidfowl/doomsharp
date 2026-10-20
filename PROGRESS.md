# Progress

**Current tooling:** the repository now uses C# fixture/regression tools and a
shared-engine browser WebAssembly host. See [README.md](README.md) for current
commands and verification boundaries. Entries below are historical: Python
commands and old runner hashes refer to their recorded revisions, not current
tooling. Historical platform results do not qualify a newer local revision.

Last verified: **2026-10-13**, against merged `main` and its linked CI run. This distinguishes the
verified playable increment from full DOOM compatibility and physical-device
verification. **Full original-DOOM parity remains unfinished.**

## Merged

[Engine PR #1](https://github.com/davidfowl/doomsharp/pull/1) was squash-merged at
[`3f900864194b6dfeb1fd9c998973050314022464`](https://github.com/davidfowl/doomsharp/commit/3f900864194b6dfeb1fd9c998973050314022464).
`main` now contains a buildable clean-room C#/.NET 10 engine and Native AOT CI.
It uses the .NET platform/BCL and OS APIs, not third-party managed libraries or
game engines. Original DOOM source and proprietary WADs are not included.

The [status reconciliation PR #3](https://github.com/davidfowl/doomsharp/pull/3)
is also merged. This page describes capabilities available on `main`, not
unmerged branch claims. Working below means verified **within the stated scope**,
not complete original-DOOM behavior.

[Executable-only assurance PR #7](https://github.com/davidfowl/doomsharp/pull/7)
removed the isolated harness and its invocation. Its
[three-OS native executable CI](https://github.com/davidfowl/doomsharp/actions/runs/38369523543)
passes; Linux includes OS-input demo/restart and startup-failure qualification.

[Lift PR #10](https://github.com/davidfowl/doomsharp/pull/10) and
[X11 Tab PR #11](https://github.com/davidfowl/doomsharp/pull/11) are merged;
their owner-local before/after evidence is described below, not confused with
historical failures or independent downloaded-CI qualification.

## Feature status

`--demo` now selects bundled Freedoom 0.13.0 Phase 1, starting at E1M1.
The original generated encounter is retained as `--fixture-demo` and remains
the target of the scripted OS-input demo/restart scenarios. Historical generated
demo evidence below does not establish Freedoom campaign completion.
Build/publish output includes the Phase 1 WAD, BSD license and credits.

| Feature | Status | Verification and limits |
| --- | --- | --- |
| Original generated demo | Working (limited scope) | Reproducible native Linux OS-input pickup/door/combat/exit and live restart via the published orchestration below; not a campaign |
| Classic WAD loading and BSP textured geometry | Working (limited coverage) | CI renders the generated demo; engine PR #1 reports 68 Freedoom map-start renders, not campaign completion; no extended map formats |
| Basic pistol/shotgun loop | Partial | Native demo observes shotgun pickup/kill/live restart; no current exact damage/RNG or complete pistol/all-weapon acceptance |
| Chaingun pickup/fire/inventory | Partial | Type2002 and chaingunner drop, key4, shared bullets, four-tick shots/two-shot pairs and supplied body/flash patches; dedicated Linux executable scenarios below, not exact original timing/RNG/visual parity or broad platform gameplay |
| Rocket launcher/inventory/projectiles | Partial | Type2003/key5, separate2010/2046 ammo/cap50, real20-unit/tick swept missiles, direct/occluded radial damage and supplied weapon/flight/explosion patches; bounded native scenarios described below, not exact original autoaim/windup/RNG/visual or campaign parity |
| Movement, collision and sector actions | Partial | Native door/lift paths and selected boundaries; tagged locked fast-door99/133-137 action scope has dedicated executable scenarios below, not every mover/trigger |
| Map progression and damaging exits | Partial | Native Linux input observes E1M1 to E1M2 with inventory/health retained; local classic-map scenarios qualify sector-11 damage/exit, fresh dead-player next-map state and finale/missing-map stop; not original campaign completion |
| E1M8 Baron/tag666 floor action | Partial | Last living type3003 with a live player, all eligible tagged floors lower to lowest neighbors through shared movers;14 synthetic native cases below, not original E1M8/campaign or exact death-state timing |
| Actors, sprites, pickups and barrels | Partial | Native demo observes pickup and one enemy kill; exact states/attacks, infighting, boss behavior and many item/visual cases remain unfinished or unverified |
| HUD, weapon overlays and automap | Partial | Actual executable exports HUD/weapon frames; local Linux native Tab on/off plus W/Ctrl controls qualify automap input, not original visuals or other-platform interactive behavior |
| Native window presentation | Working | Bounded actual-engine desktop smoke runs pass on three OSes; interactive input coverage differs by platform below |
| DMX decoding and sound-effects mixer | Partial | Native Linux playthrough submits effects to ALSA null; exact resampling/audio comparison, audible playback and Windows/macOS audio unverified |
| Remaining weapons/powerups, stairs/crushers, switch textures and other boss actions | Not implemented | Required for original gameplay/campaign parity |
| Music/MUS and Standard MIDI File playback | Partial | Clean-room MUS and SMF parsers decode bundled Freedoom `D_*` lumps into looping PCM mixed by the native audio thread and WebAudio on the browser target; verified via native ALSA-null submission and Chromium/WebKit WebAudio-after-click checks below; models per-channel pitch-bend as a semitone frequency offset and GM-program-aware timbre families via an approximate additive-harmonic/noise synth, not a General MIDI soundfont, so exact timbre/original parity is not claimed |
| Save/load game state | Partial | F5 saves and F9 loads a JSON snapshot (map, position/angle, health/armor/ammo/weapons, keys, kills/secrets/time, found-secret sectors and per-actor removed/health) via a source-generated `System.Text.Json` context; desktop persists to a `doomsharp.sav` file next to the executable, the browser persists to `localStorage` via a Save/Load button pair; verified end-to-end natively with real X11 key input (F5 produced a real save, F9 restored player position after movement) and with an in-process harness covering successful load plus rejection of bad-version/unknown-map/malformed JSON; headless (108/108), engine (121/121) and native (22/22) checks remain green; in-flight projectiles are not preserved across a save/load cycle |
| Animated flat/wall textures | Partial | Fixed built-in clean-room cycle table (`TextureAnimation.cs`) for water/nukage/slime/blood/lava/fire flats and walls (e.g. NUKAGE1-3, FWATER1-4, BLODGR1-4, FIREBLU1-2); advances one frame per 8 tics (vanilla's non-switch rate), driven by the existing fixed 1/35s tick clock so animation speed is independent of framerate; both floor/ceiling flats and upper/lower/middle wall textures animate via the existing WAD lookup/renderer sampling paths; a frame only substitutes when it is actually present in the loaded WAD (checked via new `Wad.HasFlat`/`Wad.HasTexture` helpers), so unrelated WADs see no new missing-texture diagnostics; new engine checks `texture-animation-*` and `renderer-animated-*` cover table mapping, deterministic frame-index-from-tics, missing-frame fallback and renderer-facing resolution; headless (108/108) and engine (133/133) checks and `--demo --validate-maps` (0 missing textures across all maps) remain green |
| Menus, multiplayer and extended maps/mods | Not implemented | No end-to-end capability claimed |
| Complete authentic WAD campaigns | Unverified | Map rendering alone is not campaign completion; missing behaviors can block progression |

## In-flight work and known limits

No additional unmerged engine capability is claimed here. Future progress PRs
must update this page with their verification scope and evidence links, and keep
branch-only work separate from merged-main status.

Remaining compatibility work includes stairs/crushers, remaining mover variants,
all vanilla map specials and switch textures, other boss actions, exact monster states
and attacks/infighting, remaining weapons/powerups, telefragging, original physics,
menus and multiplayer. These gaps can block complete
authentic WAD campaigns. Extended maps/mod formats are not implemented.

Save/load (`Save.cs`) is now partially implemented: F5/F9 (desktop keyboard,
X11/Windows/macOS) and a Save/Load button pair (browser) capture or restore a
single JSON snapshot of in-progress state — current map, player position/angle,
health/armor/ammo/weapon ownership, keys, kill/secret/time counters, which
secret sectors were found, and each actor's removed/health flags by spawn
index — using a source-generated `JsonSerializerContext` so the AOT/trimmed
desktop and WASM builds stay warning-free. Desktop persists to a `doomsharp.sav`
file beside the executable; the browser persists to `localStorage`. Loading
replays `Spawn()` for the saved map and reapplies actor state by index, so it
depends on deterministic actor ordering for a given map; in-flight projectiles
are discarded rather than restored. Saving is blocked while dead or after the
level has finished, mirroring the existing "no-op while finished" pattern used
elsewhere in `Game.Tick()`.

Music/MUS and Standard MIDI File playback (`Music.cs`) is now partially
implemented: a clean-room event-stream parser for both id Software's native MUS
format and Standard MIDI Files (needed because the bundled Freedoom WAD ships
`D_*` music lumps as SMF, not MUS) feeds a small additive-harmonic/noise-burst
software synthesizer, producing looping PCM mixed into the existing native
audio thread (`Audio.cs`) and the browser's WebAudio graph (`host.js`/`App.razor`)
alongside the existing one-shot SFX voices. Per-channel MUS/MIDI pitch-bend
controller events are translated to a semitone offset applied to the affected
note's frequency (`Instruments.MusPitchBendSemitones`/`MidiPitchBendSemitones`),
and GM program numbers are grouped into broad timbre families with distinct
harmonic content and attack/decay/sustain/release envelopes (`Instruments.cs`).
This is still an approximation of General MIDI timbres, not a soundfont-accurate
renderer, and SMPTE-divisioned MIDI files fall back to a default PPQ. See
the "Built vs. runtime-tested" verification below for exact evidence.
Sector-11 exit handling and E1M8's last-Baron/tag666 floor action are implemented;
other original finale boss actions remain absent. Damaging-floor timing is approximate.

## Built vs. runtime-tested

The [earlier merged-main CI](https://github.com/davidfowl/doomsharp/actions/runs/38367324903)
established three-OS AOT builds and bounded no-audio presentation, not gameplay.
The former isolated 37-check harness and startup shortcut are now removed.
**No unit tests or direct engine-method qualification remain.**

The [current workflow](.github/workflows/build.yml) publishes and runs the
native game for headless and bounded desktop scenarios. Linux also launches
the real game with [OS-input orchestration](scripts/native-demo.py): two genuine
startup rejections, completed demo and live restart. Local native qualification
observed health 88, one kill, shotgun acquired and exit completed; restart
returned to the start with health 100 and no shotgun. Complete frames, logs and
the executable hash are retained, including artifacts when CI fails.
This is executable E2E through virtual OS facilities, not physical-device
verification, a visual/timing oracle or original-map campaign acceptance.

Separate local native-game qualification retained the pre-fix sector-11 failure:
walking into the floor reduced health to zero but left `finished=False`.
Nine actual executable scenarios now cover 20-point damage, completion exactly
at health 10, living health/shotgun carryover, dead-player health/inventory reset
at a fresh E1M2 spawn, natural exit, E1M8 stop despite other available maps,
missing-next-map stop, replay and a harmless-floor control. Input is OS keyboard
events; original fixture WAD variants are prepared before startup, not running
game-state manipulation. Frames, diagnostics, input/executable hashes and
outcomes remain worker-local, not a public raw packet or CI sector corpus.
This closes one exit-path gap, **not** original E1M8/campaign qualification.

The native lift step-off failure published in
[PR #9](https://github.com/davidfowl/doomsharp/pull/9) was reproduced on integrated
source `6302152c0b1c11ecba583b6d4053f1bab373fbd8`: all five lowering/wait/carry/
stable-top/height gates passed, but prolonged backward input remained at X=-4;
game exit0 was correctly runner exit1, not lift success.
Player support now uses the radius-16 footprint across traversable portals;
it remains at the upper floor until the body clears, instead of dropping at the
center crossing and being mistaken for a new 64-unit upward step. Moving-floor
headroom/occupancy use the same touched sectors; grounded secrets/damage require
contact with the center sector's floor. Existing wall/24-unit-step/56-unit-body
collision rules and instantaneous falling model are retained.

The unchanged focused runner plus [full-cycle orchestration](scripts/native-lift-cycle.py)
qualify lowering, boarding, stationary wait, automatic carry, stable height64
(tolerance2), successful X<=-32 step-off, retrigger and second return/step-off.
The latter compares the first cleared native frame with the same build's focused
passing pose before issuing the second activation. This is a static original
fixture oracle, not an original-game visual oracle or complete lift compatibility.
Reproduction and output-directory rules are in [README](README.md).
Two deliberately planned fresh native processes each completed both cycles;
both returned to X=-111.14 with health100. Their cleared native frames matched
the focused accepted pose exactly. These are local executable results; current
CI remains the three-OS native demo/presentation scope, not a lift corpus.

Linux Tab failure was reproduced again after the lift merge on
`ab7fab8ead6dcfb3f171942e312e30d189a81296`. The actual X server mapped keycode23
to `XK_Tab`0xff09 for press/release, but all three captures remained 3D (automap
background fraction0). Native W moved X=-64 to13.14 and Ctrl changed rendered
ammo50 to48. Game exit0 was runner exit1, not automap success.
The X11 backend now normalizes Tab/ISO_Left_Tab to the shared ASCII Tab key
before both press and release dispatch; adjacent keys and Windows/macOS mappings
are unchanged. [Native orchestration](scripts/native-tab.py) observes ordinary
Tab on/off and W/Ctrl controls through the actual game, with the five unchanged
thresholds and commands in README. Two planned fresh processes qualify this
path; raw evidence is worker-local, not public downloadable media or CI coverage.
The integrated native lift, demo and boundary/progression regressions remain
separate checks rather than being inferred from Tab success.

Independent qualification was coordinator-reported against frozen main
`7c6e3ce687693f0478e67a3efa13a51b4c9c49e2`, using downloaded Linux CI
[artifact11803281901](https://github.com/davidfowl/doomsharp/actions/runs/38379934406/artifacts/11803281901)
from [run38379934406](https://github.com/davidfowl/doomsharp/actions/runs/38379934406).
Its ELF SHA256 is
`c612a195dccd3eeda337f37c8a88f5e41c1164cddd655111c9871ea81a659f53`,
distinct from owner-local binaries. One focused6 prerequisite and one full13
lift run passed (both cycles, step-off X=-111.14); one genuine Tab0xff09/keycode23
five-gate run passed (on99.8831%, off0%, W X8.86, ammo50->48). This is independently
reported virtual-Xvfb/XTest/framebuffer evidence; its raw worker-local packet was
not inspected here, and it does not establish physical input. The same frozen
qualifier's natural licensed real-map exit/next-map route remains separate and
is not inferred to have passed from silence or these synthetic observations.

Tagged key-locked fast-opening switch types99/133-137 were missing from the
activation dispatch. A fresh native baseline on the above main, with a matching
blue card and type133, remained at X45.14 against a closed remote door (required
X>=192); game exit0 was collector exit1. The implementation reuses key inventory,
tag selection, neighboring-ceiling target and door movers. It requires front-side
use and a matching-color card/skull, leaves SR actions repeatable, consumes S1
only after activation, does not restart an active mover, and retains existing
normal-door speeds. An unmatched tag has an explicit gameplay diagnostic.
[Native orchestration](scripts/native-locked-doors.py) and README publish the
22 distinct positive/negative scenarios, unchanged measurable thresholds, exact
input schedules and frame/process/hash retention. Generation prepares original
WADs before startup; it never calls engine methods or injects live state.
These are local Linux executable results, not existing CI's locked-door corpus,
natural-map progression proof, switch-texture parity or every door family.
Broad Windows/macOS gameplay, physical input and audible playback remain open.

Chaingun type2002 was absent on main `cd921a8ef15d911df53676ad44dce098a1a28645`.
A fresh main Native AOT process diagnosed the unsupported thing; ordinary W
input over the pickup left visible ammo50 and pistol artwork (game exit0,
collector exit1). A collector's earlier kill-HUD slash-delimiter error is
separately retained, not misreported as a product failure.
The bounded implementation shares pistol bullets (cap200), grants20 for a placed
weapon/10 for a chaingunner drop, supports key4 alongside existing1/2, completes
a two-shot pair on release and has four35Hz ticks between shots. New bursts have
an accurate initial pair; held refire adds horizontal spread. Actual user-WAD
`CHGG`/`CHGF` patches and `DSPISTOL` are used, with explicit missing-asset
diagnostics. Empty/unowned inputs give visible feedback without damage or
ammunition underflow. Alive map changes retain owned/selected weapon and ammo;
restart/dead-player changes restore the pistol/default inventory.

[Native weapon orchestration](scripts/native-chaingun.py) requires15 distinct
fresh full-game processes with original pre-start fixture WADs, native OS input,
visible HUD/weapon/flash frames and ordinary inventory output. The candidate's
15 cases passed, including seven bullets observed over0.810s of held fire,
70->68 on a tap, retained cap200 pickup then199->200 consumption, a dropped
weapon's34->44 bullets, no-ammo/no-flash/no-kill rejection, and fresh/retained
inventory on the documented lifecycle paths. Final reviewed-source qualification
also requires public ownership/ammo to match the visible lifecycle observations
and ordinary clip reload after depletion. README documents
the exact commands and positive/negative gates, including cap/duplicates,
pistol/shotgun preservation, first-pair versus held-spread combat, one/zero ammo,
release/cadence and all carry/reset paths. Original color-coded assets distinguish
weapon phases rather than standing in for original artwork verification.
Audio cases exercise effects and ALSA null PCM submission/missing-sound feedback,
not audible playback. Raw outcomes/frames/hashes remain worker-local; public tool
source is not a public raw packet. Existing three-OS CI's native smoke/demo scope
is unchanged, and the independent frozen natural-route qualifier is not repinned
or duplicated. Automatic empty-weapon/fist fallback, raising/lowering, original
autoaim/RNG/physics and full original visual/timing comparison remain unfinished.
This increment does not establish a natural-map exit/campaign or all-weapon
compatibility; those and physical/broad-OS acceptance remain open.

The original dry-fire packet's target was off-axis: ammo/flash rejection passed,
but its zero kills alone did not prove a hittable enemy received no damage.
The same-weapon collector now requires a visible centered target after ordinary
strafing, two seconds of empty fire with no kill/flash, then ordinary clip pickup
and two accurate pairs killing that target. This strengthens qualification
without changing engine behavior or weakening existing gates.

The rocket increment adds actual moving player missiles, not renamed bullets.
At35Hz, travel is20 units/tick and shot spacing20 ticks; contact sweeps the
radius11/height8 body against closed geometry and solid actors, chooses the
first hit, and applies20-160 direct plus bounded128-unit occluded splash damage,
including self-damage and barrel chains. Impact damage is applied once, followed
by8/6/4-tick explosion frames and removal. Type2003/2010/2046 pickups provide
2/1/5 rockets up to50, with separate ammo and living carry/reset semantics.
Real `MISG`/`MISF`/rotated `MISLA`/`MISLB`-`MISLD` WAD patches and launch/blast
sounds are used; missing assets are explicitly diagnosed. Existing monster
projectiles retain their speed/damage/art and now also use swept player contacts.

[Native rocket orchestration](scripts/native-rockets.py) defines21 fresh
full-game cases using ordinary OS input and visible output. It includes a
400-health direct target (three maximum128-point blasts alone cannot kill it),
aligned dry-fire target then ordinary rocket reload, bullet-clip non-refill
control, near/far and wall-shielded splash with surviving-target hitscan controls,
closed thin-wall versus open-boundary movement, exactly-once self-damage/cleanup,
all supplied firing/explosion phases, projected travel speed and held cadence,
cap/duplicates, lifecycle, barrel and existing monster-projectile preservation.
README has the normal build/generation/run commands and precise gates.
These are local Linux virtual-X11 executable gates, not a natural-map campaign,
physical input, audible audio or Windows/macOS gameplay qualification. ALSA null
only establishes submitted PCM; CI's three-host AOT/demo/presentation scope is
unchanged. Horizontal/no-autoaim fire, immediate launch rather than exact
original windup, Euclidean blast distance, BCL RNG, absent backpack/skill
multipliers and incomplete weapon raise/lower/visual timing remain limitations.
The independent frozen navigation qualifier is not repinned or replayed.

Baseline native input over the launcher left pistol/ammo50 and diagnosed all
three unsupported rocket thing types. The first candidate's cleanup collector
sampled a final explosion frame too early; that failure was retained and its
bounded observation interval corrected. A second collector failure combined
adjacent same-colored enemies into one target centroid; it was retained and
corrected by selecting the crosshair-nearest separated pixel component, without
lowering the100-pixel/eight-pixel alignment gates. Continuation qualified all21
cases/220 gates on the reviewed engine. The final source additionally restores
the pre-existing barrel player-damage rounding and explicitly gates the firing
body patch. Its initial cadence collector held fire while decoding images,
missed a short flash and allowed a second shot; that failure is retained too.
The corrected collector releases an ordinary tap independently of analysis and
captures short phases before decoding. Already-passed unchanged cases were not
replayed after that collector-only fix.

Final native-qualified C# source
[`61cd076201bf94ec4329527cab35a4cffa1b70da`](https://github.com/davidfowl/doomsharp/commit/61cd076201bf94ec4329527cab35a4cffa1b70da)
passed21 cases/222 gates with212 native captures on one pinned Linux AOT
executable. The first11 use the earlier runner revision and the remaining10
the corrected capture protocol; exact runner/input/binary identities and all
failed attempts remain in the worker-local packet. Published follow-up changes
are runner/documentation only, not new C# gameplay.
The thin-wall case observes health100->30 and kills0; its open twin kills1.
Near and unoccluded targets die while far/wall-shielded targets survive, then
ordinary hitscan kills those survivors. The aligned empty target survives two
seconds without ammo/projectile/flash, then ordinary rocket ammo and firing
kill it. Projected flight gives a592.25-832.91-units/s interval including700;
held fire consumes4 rockets over2.0004s, followed by stable release/cleanup.
All four flash and three explosion patches and the firing body are observed.
Native audio observes3 effects versus1 with missing launch/blast sounds, with
ALSA-null PCM and one-time diagnostics, not audible playback.
The unchanged final-artifact demo/startup/live-restart and five publisher
key/door/combat/progression/restart regressions pass. Their headless corpus
still accepts the pre-existing malformed `bad-blockmap-offset`; it is not
reported as a successful rejection. Raw archives are local, not public downloads.
These results are not campaign or exact-original timing proof.

### E1M8 last-Baron floor action

The action is limited to exact E1M8 and Baron type3003, not Hell Knight69.
All relevant living Barons must be dead and the player alive. An event latch
runs once per spawned map, reset by restart/loading. Every eligible tag666
sector uses the shared normal35-units/s floor mover toward its lowest adjacent
floor; active movers are not overwritten. Existing contact/carry, textures and
damaging specials are retained. Missing tag666 produces one explicit diagnostic.
The kill does not exit/teleport/change maps. This uses the current simplified
fatal-damage event, **not** exact original delayed death-state timing/monster parity.
Gameplay/map-editing references establish the E1M8/Baron/tag contract; source
implementations were not copied. Search summaries incorrectly called the action
instantaneous and contradicted their own eighth-unit speed quotation; those
numeric summaries were rejected, not treated as an original timing oracle.

Native-qualified production source
[`022d31c29db7578731472640c2e0e6ee046606b8`](https://github.com/davidfowl/doomsharp/commit/022d31c29db7578731472640c2e0e6ee046606b8)
passed **14 cases/145 gates/771 native captures** on one pinned Linux AOT ELF.
Subsequent edits are collector/fixture/docs only; C# remains identical.
The first6 cases use the initial runner, next6 the corrected variable-map HUD
collector and last2 corrected far-target fixtures. Already-passed unchanged
cases were not replayed. The baseline killed both Barons but remained blocked at
X1260, health100, `finished=False`; the new final positive ordinarily reaches
X1555.71 and sector11 naturally finishes at health0/kills2. First death, Knights,
wrong episode/map/tag remain blocked. Mixed identity opens with kills1/2.
Native retreat contacts X1298.57/1427.14 distinguish both lowest targets.
Carry moves the fixed green reference top255->254->234 while the player remains
near spawn. Restart restores the raised native scene exactly (0/220880 changed
pixels) and a second ordinary kill cycle opens it again. Ordinary E1M7 exit/advance
loads fresh E1M8 bosses/floors and subsequent deaths open access.

The player-dead negative genuinely launches eight missiles before floor damage
kills the player: native HUD first shows health0/kills0/ammo42, then delayed
impacts produce kills1. Outside the actor strip, its closed-floor scene exactly
matches the same ELF's surviving4000-health Cyberdemon control (0/391600 changed
pixels); no health/state injection was used. All14 accepted game/collector exits
are0. The unchanged native demo/startup/live-restart regression also passes.

Retained failures distinguish product from collector: the baseline absence;
MAP08's five-character label shifted KILLS one column; and the far target was
visible but distance shading yielded RGB30,0,30 outside the strict marker
predicate. The latter two were corrected without lowering the >100-pixel/eight-
pixel alignment gates or changing production. Only the delayed-death fixtures
move the target from2400 to2096; at most three pre-death impacts cannot kill the
1000-health Baron. The wrong-episode fixture also supplies original SKY2 artwork
instead of its initially logged fallback. Failed processes/frames remain in
the worker-local packet, alongside sources, exact ELF/input/tool hashes and
all native events/results. Public reproduction is in [README](README.md).
These original synthetic maps are not original E1M8, real-map navigation or
campaign completion. No physical/audible or Windows/macOS boss-gameplay result
is claimed; existing CI remains three-host AOT/demo/presentation.

| Platform / architecture | Build evidence | Runtime evidence |
| --- | --- | --- |
| Linux x64 | Native AOT publish | Actual game: headless/X11 frames, OS-injected demo/live restart and ALSA null submission; physical input/audio unverified |
| Windows x64 | Native AOT publish | Bounded Win32/GDI desktop presentation; interactive input and audio unverified |
| macOS arm64 | Native AOT publish | Bounded AppKit/CoreGraphics desktop presentation; interactive input and audio unverified |
| Other OS / architectures | Unverified | Unverified |

The [engine PR #1 report](https://github.com/davidfowl/doomsharp/pull/1) describes
a separate Linux actual-engine integration run using OS keyboard events:
shotgun pickup, door, combat and exit; health 88, one kill and level completed.
The engine worker also reported 13 effects submitted to ALSA null. These are
worker-reported results, not outcomes of the linked CI presentation smoke.
Independent synthetic OS-input collision/shotgun/kill/exit/restart qualification
was worker-reported at engine head
[`72c525746d705c80aaf76e1a014b0e04f97c3d9f`](https://github.com/davidfowl/doomsharp/commit/72c525746d705c80aaf76e1a014b0e04f97c3d9f).
Its qualification document/archive are worker-local and were not independently
inspected for this docs update; no public evidence packet is linked here.
Separate [synthetic fixture tooling](tools/e2e-fixtures/README.md) is now merged
in [PR #5](https://github.com/davidfowl/doomsharp/pull/5), with reproduction
instructions and worker-reported outcomes; generated evidence is not committed.
Those older raw packets remain worker-local; current demo orchestration and CI
artifact retention make its narrower qualification reproducible.
These synthetic runs do **not** establish physical keyboard/mouse, audible
sound or original-map full gameplay. Other architectures remain unverified.

Engine PR #1 reports all 36 Freedoom 0.13.0 Phase 1 and 32 Phase 2 maps loaded
and rendered at their player starts with zero missing textures, with the asset
license and archive checksum checked. This worker-reported corpus result is
not reproduced by CI; no downloaded assets are bundled. Map parser/renderer
coverage is **not** proof of original-map gameplay or campaign completion.

Music/MUS and Standard MIDI File playback (`Music.cs`, wired through
`Assets.Music`, `Game.PlayMusic`/`StartMusic`/`MusicLumpName` and the native
`Audio.PlayMusic` loop-mixer plus the browser `host.js`/`App.razor` WebAudio
path) was worker-reported against this build. `dotnet build` succeeded for all
six project files with zero warnings/errors. The bundled Freedoom WAD's
`D_E1M1`/`D_E1M8`/`D_E1M9` lumps (Standard MIDI, not MUS) decoded to plausible
PCM (100-186s, near-100% nonzero samples). The `tools/Checks` suite passed
`headless` (all PASS) and `engine` (121 PASS) unchanged, and `native`
(22/22 PASS, Xvfb/ALSA null device) including the demo/restart/chaingun/rocket
scenarios; a live Linux run against the bundled WAD showed
`Audio: 0 sound events, 226304 PCM samples submitted, 1 music changes.` on
entering E1M1, confirming the music track decoded and was mixed into real
output. Browser Playwright checks passed 36/36 on Chromium and 35/35 on WebKit
(including `autoplay-remains-silent`, `autoplay-does-not-request-audio-or-pointer`
and `Freedoom-WebAudio-after-click`, confirming music still only touches WebAudio
after a user gesture, matching the existing SFX autoplay contract). Firefox's
Playwright run reproducibly hung waiting for `AudioContext` to reach `running`
after a click on this build **and** on an unmodified main checkout in this same
environment, so it is a pre-existing environment/headless-audio limitation, not
a regression from this change. The synthesizer is a small additive-harmonic/
noise-burst approximation (no General MIDI soundfont or OPL emulation). It now
models per-channel MUS/MIDI pitch-bend controller events as a semitone offset
and shapes GM-program timbre families with distinct envelopes (`Instruments.cs`),
but SMPTE-divisioned MIDI timing still falls back to a default PPQ, and
audible/perceptual quality versus the original soundtrack is unverified.

Save/load game state (`Save.cs`, new `Buttons.Save`/`Buttons.Load` flags and
`Game.SaveGame`/`LoadGame` hooks mirroring the `PlaySound`/`PlayMusic` delegate
pattern, wired to file I/O in `Program.cs`, `localStorage` in `host.js`/`App.razor`,
and F5/F9 key mappings in `Platform.cs`/`MacPlatform.cs`) was worker-reported
against this build. `dotnet build -c Release` succeeded with zero warnings/errors
for the root project and `tools/Checks`; `JsonSerializer` calls use a
source-generated `SaveJsonContext` so the AOT/trimmed desktop and WASM targets'
`TreatWarningsAsErrors` stayed satisfied. The `tools/Checks` suite passed
unchanged: `headless` 108/108 PASS, `engine` 121/121 PASS, `native` 22/22 PASS
(Xvfb/ALSA null device, including demo/restart/chaingun/rocket/door scenarios).
A standalone in-process harness exercising `Game.Save()`/`TryLoad()` directly
confirmed round-tripping of health, position and per-actor removed/health
arrays, plus correct rejection of a bad-version, unknown-map and malformed-JSON
save. On the real published Linux executable under Xvfb with genuine X11 key
events (not the harness), F5 was observed writing a real `doomsharp.sav` with
accurate live state at least twice, and a combined save/move/load cycle showed
the player's position reverting exactly to the saved `(-416, 256)` after F9,
confirming the Load path end-to-end as well; some repeated synthetic key taps in
this bare Xvfb sandbox (no window manager) were dropped even for the
pre-existing Tab binding, which is a known input-delivery limitation of this
headless environment, not a defect in the Save/Load code path. Browser
Playwright checks (Save/Load buttons added to `App.razor`, not yet covered by a
dedicated scripted scenario to avoid touching the shared fixtures PR #27 also
modifies) passed unchanged at 36/36 on Chromium and 35/35 on WebKit; Firefox
reproducibly hung at the same pre-existing `AudioContext`/WebAudio step on this
build **and** on an unmodified main checkout in this environment, confirming it
is unrelated pre-existing flakiness. In-flight projectiles are discarded rather
than restored across a save/load cycle, and the feature does not yet have a
scripted native/browser regression test in the shared `tools/Checks`/
`tools/BrowserChecks` harnesses.

## Reproduce

Run from the repository with the .NET 10 SDK. The demo contains original assets;
no WAD download is required. Native qualification operates on the published
executable, not engine methods.

```sh
dotnet run -c Release -- --demo --no-audio
dotnet run -c Release -- --demo --headless --frames 35 --output demo.ppm
dotnet run -c Release -- --wad /path/to/game.wad --map E1M1 --no-audio
dotnet run -c Release -- --wad /path/to/freedoom1.wad --validate-maps
```

Publish on the matching host OS, not across OSes. Native compiler prerequisites
and controls are in [README](README.md).

| Host | Native AOT publish | Run the published executable |
| --- | --- | --- |
| Linux x64 | `dotnet publish -c Release -r linux-x64 -o artifacts/linux-x64` | `./artifacts/linux-x64/DoomSharp --demo --no-audio` |
| Windows x64 (PowerShell) | `dotnet publish -c Release -r win-x64 -o artifacts/win-x64` | `.\artifacts\win-x64\DoomSharp.exe --demo --no-audio` |
| macOS ARM64 | `dotnet publish -c Release -r osx-arm64 -o artifacts/osx-arm64` | `./artifacts/osx-arm64/DoomSharp --demo --no-audio` |

Linux OS-input qualification (development-only Python stdlib, Xvfb and XTest):

```sh
xvfb-run -a python3 scripts/native-demo.py artifacts/linux-x64/DoomSharp
```

Linux desktop runs need a 24/32-bit X11 display (XWayland on Wayland); sound needs
ALSA. Windows uses built-in GDI/waveOut, and macOS uses AppKit/CoreGraphics/
AudioQueue from a graphical login session. Omit `--no-audio` to request sound
from the default device; device failures are reported, not silently ignored.

For authentic maps, supply a **lawfully obtained classic DOOM-format WAD** or
obtain [Freedoom 0.13.0](https://github.com/freedoom/freedoom/releases/tag/v0.13.0)
under its BSD license. Replace example paths and map names with your local files.
Owning a proprietary WAD does not permit redistribution; none is bundled.
`--validate-maps` checks loading/rendering, not campaign gameplay completion.

## Next milestones

- Complete vanilla map-special dispatch, mover variants, switches and boss actions.
- Implement remaining combat/weapon/powerup behavior and original gameplay semantics.
- Add saves and menus, then verify full campaigns with lawful user-supplied WADs.
- Improve music fidelity (General MIDI soundfont/OPL-quality synthesis) beyond the
  current approximate additive-harmonic renderer; verify audible playback on
  physical Windows/Linux/macOS systems.
- Verify interactive input and audible audio on physical Windows/Linux/macOS systems,
  and validate other CPU architectures separately.
