# Engine scope and compatibility

DoomSharp is playable but not a complete replacement for the original game.
These notes describe supported behavior and known gaps; see the [project overview](../README.md)
for the code and a quick start.

## Powerups

Picking up Berserk, Invulnerability, Partial Invisibility, a Radiation Suit, a
Computer Area Map or a Light Amplification Visor grants their classic timed (or,
for Berserk/Computer Area Map, persistent) effect, shown in the HUD with the
remaining duration. Invulnerability blocks all damage and inverts the view; a
Radiation Suit stops most floor-damage; Partial Invisibility makes monsters miss
more often; the Visor renders at full brightness; and the Computer Area Map
reveals the whole automap instead of only the sectors you have visited (the
automap is fog-of-war without it). Powerups reset on level changes and are not
stored in savegames; loading a save also resets explored automap sectors. See
[PROGRESS.md](../PROGRESS.md) for exact scope and limits.

## Engine scope and limits

### Tagged stairs

Classic linedef actions 7 (use once) and 8 (walk once) build 8-unit steps at
8.75 units/second. Actions 127 (use once) and 100 (walk once) build 16-unit steps
at 140 units/second. Use actions require the front side; walk actions accept
either crossing direction. Each tagged seed starts from its current floor.
Chains follow the first available front-to-back two-sided linedef in map order,
keeping the seed's floor texture and adding one step per sector, including
untagged neighbors. All eligible tagged seeds are processed.

Active floor movers are not overwritten. A busy seed can be retried; a busy
neighbor is skipped when choosing the next link. Successful activation consumes
the action; missing/zero tags leave it available and display a diagnostic.
Stairs reuse moving-floor support and non-crushing occupancy checks, resume
after an obstruction clears, and reset on restart. Use-switch feedback is covered
below; exact vanilla busy-neighbor quirks and complete campaign progression
remain unqualified.

### Tagged crushers

Walk-once actions 6 (fast), 25 (normal) and 141 (normal, silent), plus repeatable
action 77 (fast), start all eligible tagged ceilings. They cycle without waiting
between their starting ceiling and 8 units above the starting floor. Normal and
fast speeds are 35 and 70 units/second; obstructed normal crushers slow to
4.375 units/second for the rest of the downstroke and restore normal speed
when rising. Silent crushers suppress movement sounds but play `PSTOP` at
their endpoints; others use `STNMOV` every eight 35-Hz ticks.

Overlapping living players, enemies and barrels receive 10 damage every four
ticks through existing armor, death/drop and barrel-blast handling. A body
spanning two crushers takes only one crush hit per tick. Contact uses sector
footprints, not just the center. The simplified engine uses 56-unit living
bodies and 16-unit dropped pickups; crushed drops are removed, while placed
pickups and corpse artwork are retained. This is not exact original actor-height,
gibbing, camera or collision parity.

Actions 57 (walk once) and 74 (repeatable) pause tagged crushers in place,
including damage. A new start action (for example 77) resumes the existing
direction, speed and silent variant rather than replacing it. Running crushers
are not restarted by recrossing. Successful once-only actions are consumed;
missing/zero tags or busy sectors retain the action and provide a diagnostic.
Active doors/floors are preserved, and active or paused crushers reserve their
sectors against door, floor and stair takeover. Restart recreates the original
geometry and removes crusher state.

The action mapping is cross-checked against the
[Doom editor linedef definitions](https://github.com/UltimateDoomBuilder/UltimateDoomBuilder/blob/master/Assets/Common/Configurations/Includes/Doom_linedefs.cfg).
Other crusher/ceiling actions, exact original obstruction quirks and authentic
campaign completion remain unqualified.

### Switch textures and buttons

The classic switch-texture integration makes successful front-side
use toggles the 40 canonical vanilla `SW1`/`SW2` pairs on existing switch actions:
7/127, 11/51, 18/21/23/60/62/69/122/123, and
29/63/99/103/111/112/113/133/134/135/136/137. Pair-table order is considered
first, off before on, then upper/middle/lower; only the front sidedef changes.
This is not arbitrary name-prefix pairing or support for Boom `SWITCHES` lumps.

One-shot switches latch. Repeatable buttons (60/62/69/123/63/99/134/136)
restore their original texture after 35 subsequent gameplay ticks. Successful
early retriggers may toggle again but neither extend the original deadline nor
create another reset. Failed/busy actions leave textures and deadlines untouched.
Buttons are independent, pause with gameplay, and are discarded on restart or
map change. Switch clicks/reset use `SWTCHN`; exit switches use `SWTCHX`.

Missing partners leave the texture unchanged, log once per map and allow another
valid slot/pair to be selected; unsupported custom switch names are diagnosed
without installing checkerboards. Missing tags, keys, busy movers and back-side
use do not falsely consume S1 actions or change textures. Manual doors and
crossing actions do not toggle switch textures. This adds feedback to existing
dispatch, not new action families or full door/mover/gameplay parity.

### Compatibility

Implemented: classic IWAD/PWAD directory/geometry/BSP validation; textured
walls/floors/ceilings, sky, masked walls and sprites; collision, sliding and
24-unit steps; pistol, shotgun, chaingun, chainsaw, plasma rifle, BFG9000 and
swept rocket/explosion combat (see [PROGRESS.md](../PROGRESS.md) for the BFG's
documented simplification versus classic tracer-spray);
pickups, keys, armor, simplified enemies/barrels; common doors, tagged locks,
lifts/floors, teleports, damaging floors, secrets, exits and map progression;
E1M8 last-Baron/tag666 floors and E2M8/E3M8 last-boss episode exits; animated flat/wall textures (water, nukage,
slime, blood, lava and fire cycles on a fixed 8-tic interval); DMX effects,
MUS/Standard-MIDI music playback
(approximate software synthesis, see [PROGRESS.md](../PROGRESS.md)), HUD, automap,
an end-of-level intermission stats screen (Kills/Items/Secrets percentages and
time; see [PROGRESS.md](../PROGRESS.md) for the exact "item" definition),
an in-game pause/options menu (F1), and a single-slot JSON save/load (F5/F9;
see [PROGRESS.md](../PROGRESS.md) for scope and verification; in-flight projectiles
are not preserved).
Classic switch textures and buttons supply the bounded feedback above.

Still incomplete: fist/Berserk melee and exact powerup parity,
monster states/AI/physics/RNG,
other moving-sector/switch specials, E4 boss actions,
pre-level menus, multiplayer and original visual/timing/audio
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
[`assets/freedoom/COPYING.txt`](../assets/freedoom/COPYING.txt),
[`CREDITS.txt`](../assets/freedoom/CREDITS.txt), and
[`CREDITS-MUSIC.txt`](../assets/freedoom/CREDITS-MUSIC.txt).
Owning another WAD does not grant permission to redistribute it.

The engine is [MIT licensed](../LICENSE); the bundled Freedoom assets retain
their own license and credits.
