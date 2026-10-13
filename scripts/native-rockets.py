"""Original rocket fixtures; actual published native game, OS input and visible output only."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import time
from native_input import NativeGame

spec = importlib.util.spec_from_file_location(
    "weapon_fixture_tools", Path(__file__).with_name("native-chaingun.py"))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
fixture.GLYPHS.update({
    "K": "#...##..#.#.#..##...#.#..#..#.#...#",
    "L": "#....#....#....#....#....#....#####",
    "R": "####.#...##...#####.#.#..#..#.#...#",
})
sha, shorts, name, patch = fixture.sha, fixture.shorts, fixture.name, fixture.patch
CASES = ("pickup-select", "unowned", "capacity", "direct", "miss", "splash", "occlusion",
         "thin-wall", "open-boundary", "self-damage", "empty", "cadence",
         "carry", "restart", "death", "dead-next", "missing", "audio", "audio-missing",
         "monster-projectile", "barrel")
WORLD_COLORS = {
    80: (255, 0, 255), 186: (0, 255, 0), 214: (255, 255, 0),
    215: (255, 0, 0), 216: (0, 0, 255), 217: (0, 255, 255),
}


def room(label, things, special=0, exit_wall=False, barrier=False, wall=None):
    lumps = fixture.room(label, things, special, exit_wall, barrier)
    if wall:
        values = dict(lumps)
        vertex = len(values["VERTEXES"]) // 4
        side = len(values["SIDEDEFS"]) // 30
        line = len(values["LINEDEFS"]) // 14
        values["VERTEXES"] += shorts(*wall[0], *wall[1])
        values["SIDEDEFS"] += shorts(0, 0) + name("-") * 2 + name("QLWALL") + shorts(0)
        values["LINEDEFS"] += shorts(vertex, vertex + 1, 1, 0, 0, side, 65535)
        values["SEGS"] += shorts(vertex, vertex + 1, 0, line, 0, 0)
        values["SSECTORS"] = shorts(5, 0)
        width = 13
        cells = width * 4
        values["BLOCKMAP"] = shorts(-128, -256, width, 4, *([4 + cells] * cells),
                                     0, *range(line + 1), 65535)
        lumps = [(key, values[key]) for key, _ in lumps]
    return lumps


def generate(output):
    original = Path(__file__).resolve().parent.parent / "tools/e2e-fixtures/generated/portal-equal.wad"
    data = original.read_bytes()
    if sha(data) != fixture.BASE_SHA:
        raise RuntimeError("Require the unchanged original publisher asset fixture.")
    count, directory = struct.unpack_from("<ii", data, 4)
    assets = []
    for i in range(count):
        offset, size, raw = struct.unpack_from("<ii8s", data, directory + i * 16)
        label = raw.rstrip(b"\0").decode("ascii")
        if label == "E1M1":
            break
        blob = data[offset:offset + size]
        if label == "PLAYPAL":
            palette = bytearray(blob)
            for start in range(0, len(palette), 768):
                for index, color in WORLD_COLORS.items():
                    palette[start + index * 3:start + index * 3 + 3] = bytes(color)
            blob = bytes(palette)
        assets.append((label, blob))
    patches = [
        ("LAUNA0", patch(32, 32, 16, 32, 186)),
        ("ROCKA0", patch(16, 24, 8, 24, 175)),
        ("BROKA0", patch(24, 24, 12, 24, 160)),
        ("CLIPA0", patch(16, 16, 8, 16, 175)),
        ("SHOTA0", patch(32, 20, 16, 20, 180)),
        ("MGUNA0", patch(32, 20, 16, 20, 180)),
        ("PISGA0", patch(64, 44, -128, -131, 133)),
        ("PISGB0", patch(64, 44, -128, -131, 141)),
        ("SHTGA0", patch(64, 44, -128, -131, 111)),
        ("SHTGB0", patch(64, 44, -128, -131, 119)),
        ("CHGGA0", patch(64, 44, -128, -131, 151)),
        ("CHGGB0", patch(64, 44, -128, -131, 173)),
        ("CHGFA0", patch(16, 16, -152, -116, 237)),
        ("CHGFB0", patch(16, 16, -152, -116, 219)),
        ("MISGA0", patch(64, 44, -128, -131, 123)),
        ("MISGB0", patch(64, 44, -128, -131, 131)),
        *[(f"MISF{frame}0", patch(16, 16, -152, -116, color))
          for frame, color in zip("ABCD", (203, 211, 227, 243))],
        *[(f"MISLA{rotation}", patch(16, 8, 8, 4, 214)) for rotation in (1, 3, 5, 7)],
        ("MISLA2A8", patch(16, 8, 8, 4, 214)),
        ("MISLA4A6", patch(16, 8, 8, 4, 214)),
        ("MISLB0", patch(48, 48, 24, 24, 215)),
        ("MISLC0", patch(48, 48, 24, 24, 216)),
        ("MISLD0", patch(48, 48, 24, 24, 217)),
        ("BAL1A0", patch(16, 16, 8, 8, 217)),
        ("BAR1A0", patch(24, 32, 12, 32, 180)),
    ]
    patches += [(f"{prefix}{frame}0", patch(32, 56, 16, 56, 80))
                for prefix in ("POSS", "HEAD", "SARG", "TROO") for frame in "ABCDEFGHIJKLMNOPQRSTU"]
    index = next(i for i, (label, _) in enumerate(assets) if label == "S_END")
    assets[index:index] = patches
    tone = b"\x80" * 16 + bytes(168 if i % 50 < 25 else 88 for i in range(4000)) + b"\x80" * 16
    sound = shorts(3, 11025) + struct.pack("<I", len(tone)) + tone
    assets += [(f"DS{effect}", sound) for effect in
               ("RLAUNC", "BAREXP", "ITEMUP", "PISTOL", "POPAIN", "PODTH1", "PLPAIN",
                "PLDETH", "FIRSHT", "FIRXPL", "SHOTGN")]
    output.mkdir(parents=True, exist_ok=False)
    records = {}
    for case in CASES:
        things = [(0, 0, 1), (0, 0, 2003)]
        wall, barrier = None, False
        if case == "pickup-select":
            things = [(0, 0, 1), (48, 0, 2003), (96, 0, 2001), (160, 0, 2002),
                      (224, 0, 2010), (320, 0, 2046)]
        elif case == "unowned":
            things = [(0, 0, 1)]
        elif case == "capacity":
            things = [(0, 0, 1)] + [(0, 0, 2046)] * 10 + [(48, 0, 2003), (96, 0, 2003)]
        elif case == "direct":
            things += [(0, 0, 2046), (1024, 0, 3005)]
            barrier = True
        elif case in ("miss", "empty"):
            things += [(1024, 180, 3004)]
            barrier = True
            if case == "empty":
                things += [(128, 0, 2007), (256, 180, 2010)]
        elif case in ("splash", "occlusion"):
            things += [(1024, 0, 3004), (1024, -80, 3004),
                       (1024, 80 if case == "occlusion" else 200, 3004)]
            barrier = True
            if case == "occlusion":
                wall = ((1100, 40), (960, 40))
        elif case in ("thin-wall", "open-boundary", "self-damage"):
            if case != "self-damage":
                things += [(256, 0, 3004)]
            wall = ((85, 256), (85, -256)) if case != "open-boundary" else None
            barrier = case == "open-boundary"
        elif case == "cadence":
            things += [(0, 0, 2046)] * 2
        elif case in ("carry", "restart", "death", "dead-next"):
            things[1] = (48, 0, 2003)
        elif case == "monster-projectile":
            things = [(0, 0, 1), (700, 0, 3001)]
        elif case == "barrel":
            things += [(1024, 0, 2035), (1024, 80, 3004)]
            barrier = True
        excluded = ({"MISGB0", "MISFA0", "MISLB0", "MISLA1", "MISLA3", "MISLA5",
                     "MISLA7", "MISLA2A8", "MISLA4A6"} if case == "missing" else
                    {"DSRLAUNC", "DSBAREXP"} if case == "audio-missing" else set())
        lumps = [(n, b) for n, b in assets if n not in excluded]
        lumps += room("E1M1", things, 5 if case == "death" else 11 if case == "dead-next" else 0,
                      case == "carry", barrier, wall)
        if case == "open-boundary":
            index = next(i for i, (n, _) in enumerate(lumps) if n == "VERTEXES")
            n, blob = lumps[index]
            lumps[index] = (n, blob[:16] + shorts(85, 256, 85, -256))
        if case in ("carry", "dead-next"):
            lumps += room("E1M2", [(-64, 0, 1)])
        body, entries = bytearray(), bytearray()
        for label, blob in lumps:
            entries.extend(struct.pack("<ii", 12 + len(body), len(blob)) + name(label))
            body.extend(blob)
        result = b"PWAD" + struct.pack("<ii", len(lumps), 12 + len(body)) + body + entries
        (output / (case + ".wad")).write_bytes(result)
        records[case + ".wad"] = sha(result)
    (output / "fixtures.json").write_text(json.dumps({
        "originalAssetFixtureSha256": fixture.BASE_SHA, "generatorSha256": sha(Path(__file__).read_bytes()),
        "helperSha256": sha(Path(fixture.__file__).read_bytes()), "fixtures": records,
        "limits": "Original sparse-BSP synthetic fixtures/color-coded patches, not original campaign or artwork."
    }, indent=2) + "\n")


def colors(frame):
    region = b"".join(frame.rgb[(y * 960 + 380) * 3:(y * 960 + 585) * 3] for y in range(340, 525))
    return {str(color): region.count(bytes([color]) * 3) for color in
            (111, 119, 123, 131, 133, 141, 151, 173, 203, 211, 227, 243)}


def marker(frame, kind):
    points = []
    predicates = {
        "missile": lambda r, g, b: r > 40 and g > 40 and b < 15 and abs(r - g) < 5,
        "B": lambda r, g, b: r > 40 and g < 15 and b < 15,
        "C": lambda r, g, b: b > 40 and g < 15 and r < 15,
        "D": lambda r, g, b: g > 40 and b > 40 and r < 15 and abs(g - b) < 5,
        "target": lambda r, g, b: r > g + 30 and b > g + 30,
    }
    predicate = predicates[kind]
    left, right, top, bottom = (440, 520, 230, 310) if kind == "target" else (350, 610, 160, 340)
    for y in range(top, bottom):
        row = frame.rgb[(y * 960 + left) * 3:(y * 960 + right) * 3]
        for column, (r, g, b) in enumerate(zip(row[0::3], row[1::3], row[2::3])):
            if predicate(r, g, b):
                x = left + column
                points.append((x, y))
    return {"pixels": len(points),
            "centerX": sum(x for x, _ in points) / len(points) if points else None,
            "width": max(x for x, _ in points) - min(x for x, _ in points) + 1 if points else 0}


def run(args):
    manifest = json.loads((args.input_dir / "fixtures.json").read_text())
    wad = args.input_dir / (args.case + ".wad")
    if sha(wad.read_bytes()) != manifest["fixtures"][wad.name]:
        raise RuntimeError("Static input hash mismatch.")
    session = NativeGame(args.executable, wad, args.source_commit, args.output_dir)
    if args.case in ("audio", "audio-missing"):
        session.metadata["command"].remove("--no-audio")
        config = session.output / "asound.conf"
        config.write_text("pcm.!default { type null }\n")
        os.environ["ALSA_CONFIG_PATH"] = str(config)
        session.metadata["limits"] += " ALSA-null PCM, not audible playback."
    with session as game:
        game.metadata.update(runnerSha256=sha(Path(__file__).read_bytes()),
                             helperSha256=sha(Path(fixture.__file__).read_bytes()), scenario=args.case)

        def observe(label):
            frame = game.capture(label)
            values = fixture.hud(frame)
            game.metadata["frames"][-1].update(hud=values, weaponColors=colors(frame))
            return frame, values

        def check(label, passed, **values):
            game.check(label, passed, **values)
            if not passed:
                if game.process.poll() is None:
                    game.quit()
                raise RuntimeError("Native acceptance failed: " + label)

        def tap():
            game.hold("Control_L", .06)

        def aimed(label):
            frame, values = observe(label)
            target = marker(frame, "target")
            check("aligned-visible-live-target-" + label, target["pixels"] > 100 and
                  target["centerX"] is not None and abs(target["centerX"] - 480) <= 8,
                  marker=target, **values)
            return values

        time.sleep(.25)
        initial, start = observe("01-start")
        case = args.case
        walked = case in ("pickup-select", "capacity", "carry", "restart", "death", "dead-next")
        if walked:
            check("initial-pistol-pool", start["ammo"] == 50 and colors(initial)["133"] > 1000, **start)
            game.hold("w", .35)
            time.sleep(.18)
            picked, values = observe("02-pickup")
            # Damage flashes alter world/weapon pixels, not the numeric HUD.
            check("ordinary-launcher-pickup", values["ammo"] == (50 if case == "capacity" else 2) and
                  (colors(picked)["123"] > 1000 or case in ("death", "dead-next")), **values)
        elif case not in ("unowned", "monster-projectile"):
            check("spawn-launcher-pickup", start["ammo"] ==
                  (12 if case == "cadence" else 7 if case == "direct" else 2) and
                  colors(initial)["123"] > 1000, **start)
        if case == "pickup-select":
            game.hold("1", .06)
            tap()
            _, values = observe("03-pistol")
            check("pistol-retains-bullet-pool", values["ammo"] == 49, **values)
            game.hold("w", .30)
            time.sleep(.2)
            _, values = observe("04-shotgun")
            check("shotgun-pickup", values["ammo"] == 8, **values)
            game.hold("w", .42)
            time.sleep(.2)
            _, values = observe("05-chaingun")
            check("chaingun-shared-bullet-pool", values["ammo"] == 69, **values)
            game.hold("5", .06)
            frame, values = observe("06-key5")
            check("native-key5-real-launcher", values["ammo"] == 2 and colors(frame)["123"] > 1000, **values)
            game.hold("w", .43)
            time.sleep(.2)
            _, values = observe("07-single-rocket")
            check("single-rocket-adds-one", values["ammo"] == 3, **values)
            game.hold("w", .65)
            time.sleep(.2)
            _, values = observe("08-rocket-box")
            check("box-adds-five-rockets", values["ammo"] == 8, **values)
            for key, ammo, color in (("2", 8, "111"), ("4", 69, "151"), ("5", 8, "123")):
                game.hold(key, .06)
                frame, values = observe("09-key" + key)
                check("selection-pool-key" + key, values["ammo"] == ammo and colors(frame)[color] > 1000, **values)
        elif case == "unowned":
            game.hold("5", .06)
            frame, values = observe("02-denied")
            check("unowned-launcher-denied", fixture.message(frame, "ROCKET LAUNCHER NOT OWNED") and
                  values["ammo"] == 50 and colors(frame)["133"] > 1000, **values)
        elif case == "capacity":
            game.hold("w", .30)
            game.hold("s", .30)
            frame, values = observe("03-full-duplicate")
            check("full-duplicate-remains", values["ammo"] == 50 and fixture.green(frame) > 100, **values)
            tap()
            time.sleep(.6)
            game.hold("1", .06)
            game.hold("w", .30)
            game.hold("s", .30)
            frame, values = observe("04-consumed-duplicate")
            check("duplicate-clamps-without-selection", values["ammo"] == 50 and colors(frame)["133"] > 1000 and
                  fixture.green(frame) == 0, greenPixels=fixture.green(frame), **values)
            game.hold("5", .06)
            _, values = observe("05-capacity")
            check("separate-rocket-capacity", values["ammo"] == 50, **values)
        elif case == "direct":
            aimed("02-tough-target")
            tap()
            flight, values = observe("03-travel")
            check("moving-not-instant-hit", values["ammo"] == 6 and values["kills"] == 0 and
                  marker(flight, "missile")["pixels"] > 8, marker=marker(flight, "missile"), **values)
            time.sleep(1.5)
            _, values = observe("04-first-impact")
            check("tough-target-survives-one", values["kills"] == 0, **values)
            tap()
            time.sleep(1.5)
            _, values = observe("05-second-impact")
            if values["kills"] == 0:
                tap()
                time.sleep(1.5)
                _, values = observe("06-third-impact")
            check("direct-damage-beyond-three-max-blasts", values["kills"] == 1 and
                  values["ammo"] in (4, 5) and values["health"] == 100, **values)
        elif case in ("splash", "occlusion"):
            aimed("02-direct-target")
            tap()
            time.sleep(1.6)
            _, values = observe("03-blast")
            check("near-dies-far-or-occluded-survives", values["kills"] == 2 and values["health"] == 100, **values)
            game.hold("a", 1.33 if case == "splash" else .53)
            values = aimed("04-surviving-target")
            check("surviving-target-live-before-control", values["kills"] == 2, **values)
            game.hold("1", .06)
            game.hold("Control_L", 1.3)
            _, values = observe("05-positive-control")
            check("ordinary-hitscan-kills-survivor", values["kills"] == 3 and values["ammo"] < 50, **values)
        elif case in ("thin-wall", "open-boundary", "self-damage"):
            tap()
            time.sleep(.8)
            frame, values = observe("02-impact-cleanup")
            closed = case != "open-boundary"
            check("closed-wall-blocks-open-boundary-passes", values["kills"] == (0 if closed else 1) and
                  (values["health"] == 30 if closed else values["health"] > 0),
                  **values)
            check("exactly-once-cleanup", all(marker(frame, kind)["pixels"] == 0 for kind in ("missile", "B", "C", "D")))
            health = values["health"]
            time.sleep(.8)
            _, values = observe("03-stable")
            check("no-repeat-explosion-damage", values["health"] == health and values["ammo"] == 1, **values)
        elif case == "miss":
            check("off-axis-enemy-present", fixture.green(initial) == 0 and start["kills"] == 0, **start)
            tap()
            flight, values = observe("02-flight")
            check("real-missile-on-miss", marker(flight, "missile")["pixels"] > 8, marker=marker(flight, "missile"))
            time.sleep(2.5)
            frame, values = observe("03-miss-cleanup")
            check("off-axis-miss-preserves-enemy", values["kills"] == 0 and values["ammo"] == 1, **values)
            check("miss-wall-impact-cleaned", all(marker(frame, kind)["pixels"] == 0 for kind in ("missile", "B", "C", "D")))
            game.hold("a", 1.2)
            aimed("04-missed-live-target")
            game.hold("1", .06)
            game.hold("Control_L", 1.3)
            _, values = observe("05-miss-positive-control")
            check("missed-target-hittable", values["kills"] == 1, **values)
        elif case == "empty":
            for _ in range(2):
                tap()
                time.sleep(.6)
            time.sleep(2.7)
            game.hold("a", 1.2)
            values = aimed("02-empty-aligned")
            check("empty-pool-live-target", values["ammo"] == 0 and values["kills"] == 0, **values)
            game.hold("Control_L", 2)
            frame, values = observe("03-dry-fire")
            check("empty-no-damage-projectile-or-flash", values["ammo"] == values["kills"] == 0 and
                  fixture.message(frame, "OUT OF AMMO") and
                  all(colors(frame)[str(color)] == 0 for color in (203, 211, 227, 243)) and
                  marker(frame, "missile")["pixels"] == 0, **values)
            game.hold("d", 1.2)
            game.hold("w", .85)
            _, values = observe("04-bullet-clip")
            check("bullet-clip-does-not-refill-rockets", values["ammo"] == 0, **values)
            game.hold("1", .06)
            _, values = observe("05-separate-bullets")
            check("ordinary-clip-positive-control", values["ammo"] == 60, **values)
            game.hold("5", .06)
            game.hold("a", 1.2)
            game.hold("w", .85)
            time.sleep(.2)
            values = aimed("06-reloaded-aligned")
            check("ordinary-single-rocket-reloads", values["ammo"] == 1 and values["kills"] == 0, **values)
            tap()
            time.sleep(1.4)
            frame, values = observe("07-reloaded-hit")
            check("reloaded-missile-kills-same-dry-target", values["ammo"] == 0 and values["kills"] == 1 and
                  colors(frame)["123"] > 1000, **values)
        elif case in ("cadence", "missing"):
            game.key("Control_L", True)
            phases = {}
            travel_samples = []
            for index in range(12):
                time.sleep(.035)
                frame, values = observe(f"02-weapon-phase-{index:02}")
                rendered = colors(frame)
                missile = marker(frame, "missile")
                if missile["pixels"] > 8 and 20 <= missile["width"] <= 200:
                    travel_samples.append((game.metadata["frames"][-1]["seconds"], missile))
                for color in (203, 211, 227, 243):
                    if rendered[str(color)] > 100:
                        phases[str(color)] = True
                if len(phases) == (3 if case == "missing" else 4):
                    break
            game.key("Control_L", False)
            check("supplied-four-flash-phases" if case == "cadence" else "other-real-flashes-with-missing-first",
                  len(phases) == (3 if case == "missing" else 4), phases=phases)
            check("one-tap-one-missile", values["ammo"] == (11 if case == "cadence" else 1), **values)
            if case == "missing":
                check("missing-body-no-pistol-substitution", all(colors(frame)[str(c)] == 0 for c in (133, 141)))
            else:
                check("multiple-visible-flight-samples", len(travel_samples) >= 2, samples=travel_samples)
                first, second = travel_samples[0], travel_samples[-1]
                a, b = first[1], second[1]
                dt = second[0] - first[0]
                # Known original patch width16 and this game's documented perspective, not engine state.
                focal = 480
                check("visible-travel-size-decreases", a["pixels"] > 8 and b["pixels"] > 8 and a["width"] > b["width"],
                      first=a, second=b, seconds=dt)
                lower = (focal * 16 / (b["width"] + 2) - focal * 16 / max(1, a["width"] - 2) - 20) / dt
                upper = (focal * 16 / max(1, b["width"] - 2) - focal * 16 / (a["width"] + 2) + 20) / dt
                check("twenty-unit-tick-travel-speed", lower <= 700 <= upper and
                      450 < lower < upper < 1050, speedInterval=[lower, upper])
            seen = set()
            for index in range(48):
                time.sleep(.04)
                frame, values = observe(f"05-impact-phase-{index:02}")
                seen.update(kind for kind in ("B", "C", "D") if marker(frame, kind)["pixels"] > 8)
                if ("C" in seen and "D" in seen if case == "missing" else len(seen) == 3):
                    break
            check("real-impact-animation" if case == "cadence" else "remaining-explosion-patches",
                  ("C" in seen and "D" in seen if case == "missing" else len(seen) == 3), phases=sorted(seen))
            time.sleep(.4)
            cleaned, before = observe("06-cleaned")
            check("impact-despawns", all(marker(cleaned, kind)["pixels"] == 0 for kind in ("missile", "B", "C", "D")))
            if case == "cadence":
                game.key("Control_L", True)
                started = time.monotonic()
                time.sleep(2)
                game.key("Control_L", False)
                elapsed = time.monotonic() - started
                time.sleep(.2)
                _, after = observe("07-held-cadence")
                check("twenty-tick-held-cadence", before["ammo"] - after["ammo"] == 4 and 1.99 <= elapsed <= 2.1,
                      rockets=before["ammo"] - after["ammo"], seconds=elapsed)
                time.sleep(3)
                frame, stable = observe("08-released-cleaned")
                check("release-no-extra-shots-or-projectiles", stable["ammo"] == after["ammo"] and
                      all(marker(frame, kind)["pixels"] == 0 for kind in ("missile", "B", "C", "D")), **stable)
        elif case in ("carry", "restart", "death", "dead-next"):
            tap()
            time.sleep(.6)
            _, values = observe("03-spent")
            check("one-rocket-before-lifecycle", values["ammo"] == 1, **values)
            if case == "carry":
                game.hold("w", .95)
                game.hold("e", .06)
                game.capture("04-complete")
                game.hold("e", .06)
                frame, values = observe("05-carried")
                check("living-carry", values["ammo"] == 1 and colors(frame)["123"] > 1000 and
                      values["health"] == 100, **values)
            else:
                if case != "restart":
                    time.sleep(10.2 if case == "death" else 5.2)
                    _, values = observe("04-dead")
                    check("ordinary-floor-death", values["health"] == 0, **values)
                game.hold("e" if case == "dead-next" else "r", .06)
                frame, values = observe("05-fresh")
                check("default-inventory-restored", values["ammo"] == 50 and values["health"] == 100 and
                      colors(frame)["133"] > 1000, **values)
                game.hold("5", .06)
                frame, values = observe("06-ownership-reset")
                check("launcher-ownership-reset", fixture.message(frame, "ROCKET LAUNCHER NOT OWNED"), **values)
        elif case in ("audio", "audio-missing"):
            tap()
            time.sleep(3)
            _, values = observe("02-audio-shot")
            check("one-real-shot-for-audio", values["ammo"] == 1, **values)
        elif case == "monster-projectile":
            time.sleep(.6)
            frame, values = observe("02-monster-flight")
            check("existing-monster-missile-travels", marker(frame, "D")["pixels"] > 8 and
                  values["health"] == 100, marker=marker(frame, "D"), **values)
            time.sleep(3)
            _, values = observe("02-monster-impact")
            check("existing-monster-projectile-damages-player", 0 < values["health"] < 100 and
                  values["ammo"] == 50 and values["kills"] == 0, **values)
        elif case == "barrel":
            tap()
            time.sleep(2)
            _, values = observe("02-barrel-chain")
            check("rocket-barrel-chain-one-kill", values["kills"] == 1 and values["ammo"] == 1, **values)
            time.sleep(.8)
            _, values = observe("03-barrel-stable")
            check("barrel-chain-no-duplicate-kill", values["kills"] == 1, **values)
        game.quit()
        text = (game.output / "game.log").read_text()
        inventory = re.search(r"Inventory: weapon=(\w+), bullets=(\d+), shells=(\d+), chaingun=(True|False)\.", text)
        missiles = re.search(r"Missile inventory: rockets=(\d+), launcher=(True|False)\.", text)
        check("ordinary-public-inventory", inventory is not None and missiles is not None)
        if inventory and missiles:
            game.metadata["inventory"] = dict(weapon=inventory[1], bullets=int(inventory[2]),
                                              shells=int(inventory[3]), rockets=int(missiles[1]),
                                              launcher=missiles[2] == "True")
            check("inventory-ammo-bounds", 0 <= int(missiles[1]) <= 50 and 0 <= int(inventory[2]) <= 200 and
                  0 <= int(inventory[3]) <= 50, **game.metadata["inventory"])
            check("ownership-matches-lifecycle", (missiles[2] == "True") ==
                  (case not in ("unowned", "monster-projectile", "restart", "death", "dead-next")))
            visible = game.metadata["frames"][-1]["hud"]
            pool = int(missiles[1] if inventory[1] == "RocketLauncher" else
                       inventory[3] if inventory[1] == "Shotgun" else inventory[2])
            check("public-ammo-matches-visible", visible["ammo"] == pool, visible=visible, **game.metadata["inventory"])
        if case in ("carry", "dead-next"):
            check("actual-e1m2-spawn", "map=E1M2." in text and game.metadata["finalPosition"] == [-64, 0])
        if case == "missing":
            check("missing-assets-diagnosed-once", all(text.count(fragment) == 1 for fragment in
                  ("Missing weapon patch MISGB0;", "Missing weapon patch MISFA0;",
                   "Missing projectile sprite MISLA;", "Missing projectile patch MISLB0;")))
        if case in ("audio", "audio-missing"):
            audio = re.search(r"Audio: (\d+) sound events, (\d+) PCM samples submitted\.", text)
            check("native-effects-submission", audio is not None and int(audio[1]) ==
                  (3 if case == "audio" else 1) and int(audio[2]) > 22050,
                  audio=audio.group(0) if audio else None)
            check("missing-sounds-once", all(text.count("Missing sound DS" + effect + ";") ==
                  (1 if case == "audio-missing" else 0) for effect in ("RLAUNC", "BAREXP")))
        check("recognized-rocket-things", all("Unsupported thing type " + str(kind) not in text
                                            for kind in (2003, 2010, 2046)))
        game.metadata["completed"] = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    generator = commands.add_parser("generate")
    generator.add_argument("--output-dir", type=Path, required=True)
    for command in ("run", "qualify"):
        child = commands.add_parser(command)
        child.add_argument("executable", type=Path)
        child.add_argument("--source-commit", required=True)
        child.add_argument("--input-dir", type=Path, required=True)
        child.add_argument("--output-dir", type=Path, required=True)
        if command == "run":
            child.add_argument("--case", choices=CASES, required=True)
    args = parser.parse_args()
    if args.command == "generate":
        generate(args.output_dir)
    elif args.command == "run":
        run(args)
    else:
        args.output_dir.mkdir(parents=True, exist_ok=False)
        outcomes = []
        for case in CASES:
            result = subprocess.run([
                sys.executable, str(Path(__file__).resolve()), "run", str(args.executable.resolve()),
                "--source-commit", args.source_commit, "--input-dir", str(args.input_dir.resolve()),
                "--output-dir", str((args.output_dir / case).resolve()), "--case", case])
            outcomes.append({"case": case, "collectorExit": result.returncode})
            (args.output_dir / "matrix.json").write_text(json.dumps(outcomes, indent=2) + "\n")
            if result.returncode:
                raise SystemExit(result.returncode)


if __name__ == "__main__":
    main()
