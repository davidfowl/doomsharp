"""Original E1M8 fixtures; qualify the full published game through native OS input."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import struct
import sys
import tempfile
import time
from native_input import NativeGame

spec = importlib.util.spec_from_file_location("rocket_fixtures", Path(__file__).with_name("native-rockets.py"))
rocket = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rocket)
fixture = rocket.fixture
fixture.GLYPHS["P"] = "####.#...##...#####.#....#....#...."
sha, shorts, name, patch = fixture.sha, fixture.shorts, fixture.name, fixture.patch
CASES = ("first-only", "last-baron", "lowest-first", "lowest-second", "knight-only",
         "mixed", "wrong-episode", "wrong-map-number", "no-tag", "carry", "restart",
         "transition", "dead-control", "dead-player")


def map_name(case):
    return {"wrong-episode": "E2M8", "wrong-map-number": "MAP08",
            "transition": "E1M7"}.get(case, "E1M8")


def corridor(label, case):
    dead = case.startswith("dead-")
    end = 2560 if dead else 1280
    bounds = (-256, -128, end, end + 64, end + 128, end + 192, end + 384)
    vertices = [(x, y) for x in bounds for y in (-256, 256)]
    sides, lines, segs, subsectors = [], [], [], []

    def side(sector, portal):
        sides.append(shorts(0, 0) + name("QLWALL" if portal else "-") * 2 +
                     name("-" if portal else "QLWALL") + shorts(sector))
        return len(sides) - 1

    def line(a, b, right, left=None, flags=None, special=0):
        lines.append(shorts(a, b, (1 if left is None else 4) if flags is None else flags,
                            special, 0, side(right, left is not None),
                            65535 if left is None else side(left, True)))
        return len(lines) - 1

    west = line(0, 1, 0)
    tops = [line(i * 2 + 1, i * 2 + 3, i) for i in range(6)]
    portals = [line(i * 2 + 1, i * 2, i - 1, i) for i in range(1, 6)]
    east = line(13, 12, 5)
    bottoms = [line(i * 2 + 2, i * 2, i) for i in range(6)]
    for i in range(6):
        first = len(segs)
        for a, b, index, direction in (
            (i * 2, i * 2 + 1, west if i == 0 else portals[i - 1], 0 if i == 0 else 1),
            (i * 2 + 1, i * 2 + 3, tops[i], 0),
            (i * 2 + 3, i * 2 + 2, east if i == 5 else portals[i], 0),
            (i * 2 + 2, i * 2, bottoms[i], 0),
        ):
            segs.append(shorts(a, b, 0, index, direction, 0))
        if i == 1:
            vertex = len(vertices)
            vertices += [(end - 180, 256), (end - 180, -256)]
            line(vertex, vertex + 1, 1, 1, 6)
            if not dead:
                vertex = len(vertices)
                vertices += [(128, 100), (end, 100)]
                index = line(vertex, vertex + 1, 1)
                segs.append(shorts(vertex, vertex + 1, 0, index, 0, 0))
            if case == "transition" and label == "E1M7":
                vertex = len(vertices)
                vertices += [(96, 256), (96, -256)]
                line(vertex, vertex + 1, 1, 1, special=11)
        subsectors.append(shorts(len(segs) - first, first))
    nodes = b"".join(shorts(split, 256, 0, -512, 256, -256, bounds[0], split,
                            256, -256, split, bounds[i + 2],
                            0x8000 if i == 0 else i - 1, 0x8000 | (i + 1))
                     for i, split in enumerate(bounds[1:-1]))
    tag = 667 if case == "no-tag" else 666
    floors = (0, 64 if case == "carry" else 0, 64, -32, 96, -64)
    damaging_exit = case == "last-baron"
    sectors = b"".join(shorts(floor, 256) + name("QLFLOOR") + name("QLCEIL") +
                       shorts(255, 5 if dead and i == 1 else 11 if damaging_exit and i == 5 else 0,
                              tag if i in (2, 4) or case == "carry" and i == 1 else 0)
                       for i, floor in enumerate(floors))
    pickup = 0 if dead else 48
    things = [(0, 0, 1), (pickup, 0, 2003)] + [(pickup, 0, 2046)] * 10
    if dead:
        things += [(2400, 0, 16 if case == "dead-control" else 3003)]
    else:
        things += [(1152, 0, 69 if case == "knight-only" else 3003),
                   (1152, 200, 69 if case in ("knight-only", "mixed") else 3003)]
        if case == "carry":
            things.append((1536, -150, 2028))
    width = (bounds[-1] - bounds[0]) // 128
    cells = width * 4
    return [
        (label, b""), ("THINGS", b"".join(shorts(x, y, 0, kind, 7) for x, y, kind in things)),
        ("LINEDEFS", b"".join(lines)), ("SIDEDEFS", b"".join(sides)),
        ("VERTEXES", b"".join(shorts(*v) for v in vertices)), ("SEGS", b"".join(segs)),
        ("SSECTORS", b"".join(subsectors)), ("NODES", nodes), ("SECTORS", sectors),
        ("REJECT", b"\0" * 5),
        ("BLOCKMAP", shorts(bounds[0], -256, width, 4, *([4 + cells] * cells),
                            0, *range(len(lines)), 65535)),
    ]


def generate(output):
    output.mkdir(parents=True, exist_ok=False)
    with tempfile.TemporaryDirectory(prefix="original-assets-", dir=output) as temporary:
        sampled = Path(temporary) / "weapons"
        rocket.generate(sampled)
        data = (sampled / "cadence.wad").read_bytes()
    count, directory = struct.unpack_from("<ii", data, 4)
    assets = []
    for i in range(count):
        offset, size, raw = struct.unpack_from("<ii8s", data, directory + i * 16)
        label = raw.rstrip(b"\0").decode("ascii")
        if label == "E1M1":
            break
        assets.append((label, data[offset:offset + size]))
    patches = [(f"{prefix}{frame}0", patch(64, 80, 32, 80, 80))
               for prefix in ("BOSS", "BOS2", "CYBR") for frame in "ABCDEFGHIJKLMNOPQRSTU"]
    patches.append(("COLUA0", patch(64, 192, 32, 192, 186)))
    index = next(i for i, (label, _) in enumerate(assets) if label == "S_END")
    assets[index:index] = patches
    records = {}
    for case in CASES:
        lumps = assets + corridor(map_name(case), case)
        if case == "transition":
            lumps += corridor("E1M8", case)
        body, entries = bytearray(), bytearray()
        for label, blob in lumps:
            entries.extend(struct.pack("<ii", 12 + len(body), len(blob)) + name(label))
            body.extend(blob)
        data = b"PWAD" + struct.pack("<ii", len(lumps), 12 + len(body)) + body + entries
        (output / (case + ".wad")).write_bytes(data)
        records[case + ".wad"] = sha(data)
    (output / "fixtures.json").write_text(json.dumps({
        "generatorSha256": sha(Path(__file__).read_bytes()),
        "rocketFixtureToolSha256": sha(Path(rocket.__file__).read_bytes()),
        "hudFixtureToolSha256": sha(Path(fixture.__file__).read_bytes()),
        "originalAssetFixtureSha256": fixture.BASE_SHA, "fixtures": records,
        "limits": "Original synthetic geometry/color-coded patches. No proprietary assets or real campaign proof."
    }, indent=2) + "\n")


def completion_pixels(frame):
    return sum(frame.pixel(x, y) == b"\xff\xff\xff"
               for y in range(255, 291) for x in range(40, 935))


def green_top(frame):
    points = [(x, y) for y in range(160, 400) for x in range(500, 570)
              if frame.pixel(x, y)[1] > frame.pixel(x, y)[0] + 30 and
              frame.pixel(x, y)[1] > frame.pixel(x, y)[2] + 30]
    return {"pixels": len(points), "top": min((y for _, y in points), default=None)}


def run(args):
    manifest = json.loads((args.input_dir / "fixtures.json").read_text())
    wad = args.input_dir / (args.case + ".wad")
    if sha(wad.read_bytes()) != manifest["fixtures"][wad.name]:
        raise RuntimeError("Static boss input hash mismatch.")
    session = NativeGame(args.executable, wad, args.source_commit, args.output_dir)
    command = session.metadata["command"]
    command[command.index("--map") + 1] = map_name(args.case)
    with session as game:
        game.metadata.update(scenario=args.case, runnerSha256=sha(Path(__file__).read_bytes()),
                             rocketToolSha256=sha(Path(rocket.__file__).read_bytes()),
                             hudToolSha256=sha(Path(fixture.__file__).read_bytes()))
        sequence = 0

        def observe(label):
            nonlocal sequence
            sequence += 1
            frame = game.capture(f"{sequence:03}-{label}")
            values = fixture.hud(frame)
            game.metadata["frames"][-1]["hud"] = values
            return frame, values

        def check(label, passed, **values):
            game.check(label, passed, **values)
            if not passed:
                game.quit()
                raise RuntimeError("Native boss acceptance failed: " + label)

        def pickup():
            game.hold("w", .35)
            time.sleep(.2)
            frame, values = observe("launcher")
            check("ordinary-launcher-capacity", values["ammo"] == 50 and
                  rocket.colors(frame)["123"] > 1000, **values)

        def kill(target, label, clear=True):
            frame, values = observe(label + "-aligned")
            marker = rocket.marker(frame, "target")
            check(label + "-visible-hittable-target", marker["pixels"] > 100 and
                  marker["centerX"] is not None and abs(marker["centerX"] - 480) <= 8,
                  marker=marker, **values)
            game.key("Control_L", True)
            deadline = time.monotonic() + 10
            while time.monotonic() < deadline:
                time.sleep(.15)
                _, values = observe(label + "-fire")
                if values["kills"] >= target:
                    break
            game.key("Control_L", False)
            check(label + "-ordinary-projectile-death", values["kills"] == target and
                  values["ammo"] > 0 and values["health"] == 100, **values)
            if clear:
                time.sleep(3)
            return observe(label + "-after")

        def no_finish(frame, values):
            pixels = completion_pixels(frame)
            check("death-is-not-immediate-exit", pixels == 0 and values["health"] == 100,
                  completionPixels=pixels, **values)

        def pair():
            kill(1, "lower")
            game.hold("a", 1.33)
            frame, values = kill(2, "upper")
            no_finish(frame, values)
            game.hold("d", 1.33)

        def go_blocked():
            game.hold("w", 10)
            frame, values = observe("raised-barrier")
            no_finish(frame, values)
            return values

        def go_open():
            game.hold("w", 10)
            return observe("past-both-barriers")

        case = args.case
        time.sleep(.25)
        frame, values = observe("start")
        if not case.startswith("dead-"):
            check("fresh-map-default-pistol", values["kills"] == 0 and values["health"] == 100 and
                  values["ammo"] == 50 and rocket.colors(frame)["133"] > 1000, **values)
            pickup()
        if case.startswith("dead-"):
            marker = rocket.marker(frame, "target")
            check("far-visible-live-target", marker["pixels"] > 100 and
                  marker["centerX"] is not None and abs(marker["centerX"] - 480) <= 8,
                  marker=marker, **values)
            check("spawn-launcher-capacity", values["ammo"] == 50, **values)
            deadline = time.monotonic() + 7
            while values["health"] > 50 and time.monotonic() < deadline:
                time.sleep(.15)
                _, values = observe("ordinary-floor-damage")
            check("ordinary-floor-health50-before-launches", values["health"] == 50, **values)
            time.sleep(.7)
            game.hold("Control_L", 4.1)
            frame, values = observe("player-death-before-boss")
            deadline = time.monotonic() + 1
            while values["health"] > 0 and time.monotonic() < deadline:
                time.sleep(.02)
                frame, values = observe("player-death-before-boss")
            check("dead-player-with-live-target-and-eight-in-flight-shots",
                  values["health"] == 0 and values["kills"] == 0 and values["ammo"] == 42, **values)
            time.sleep(4)
            frame, values = observe("dead-stable")
            expected = 1 if case == "dead-player" else 0
            check("post-death-projectile-outcome", values["health"] == 0 and values["kills"] == expected,
                  **values)
            if case == "dead-player":
                control = args.dead_control
                if control is None:
                    raise RuntimeError("Supply --dead-control from the accepted dead-control case.")
                receipt = json.loads((control / "result.json").read_text())
                check("accepted-same-binary-dead-control", receipt["completed"] and
                      receipt["executableSha256"] == game.metadata["executableSha256"] and
                      receipt["scenario"] == "dead-control")
                record = receipt["frames"][-1]
                data = (control / (record["name"] + ".ppm")).read_bytes()
                check("dead-control-native-frame-hash", sha(data) == record["sha256"])
                rgb = data[len(b"P6\n960 600\n255\n"):]
                changed = total = 0
                for y in range(80, 525):
                    for x in range(960):
                        if 440 <= x < 520:
                            continue
                        offset = (y * 960 + x) * 3
                        total += 1
                        changed += rgb[offset:offset + 3] != frame.rgb[offset:offset + 3]
                check("dead-player-does-not-lower-tagged-floors", changed / total <= .001,
                      changedPixels=changed, comparedPixels=total, fraction=changed / total)
        elif case == "first-only":
            kill(1, "lower")
            go_blocked()
        elif case == "mixed":
            frame, values = kill(1, "only-baron")
            no_finish(frame, values)
            go_open()
        elif case == "carry":
            game.hold("a", 1.33)
            kill(1, "upper")
            game.hold("d", 1.33)
            before, values = observe("carry-before")
            initial = green_top(before)
            check("fixed-neighbor-green-reference", initial["pixels"] > 100 and initial["top"] is not None,
                  marker=initial)
            kill(2, "last-lower", False)
            middle, values = observe("carry-moving")
            moving = green_top(middle)
            time.sleep(2.3)
            after, values = observe("carry-after")
            final = green_top(after)
            no_finish(after, values)
            delta = None if final["top"] is None else initial["top"] - final["top"]
            check("boss-floor-gradual-contact-carry", final["pixels"] > 100 and delta is not None and
                  18 <= delta <= 26 and moving["top"] is not None and
                  final["top"] < moving["top"] < initial["top"], before=initial, moving=moving, after=final,
                  topShift=delta)
        else:
            pair()
            if case in ("knight-only", "wrong-episode", "wrong-map-number", "no-tag"):
                go_blocked()
            elif case == "transition":
                game.hold("e", .06)
                frame, values = observe("e1m7-completed")
                check("ordinary-use-exit-before-transition", completion_pixels(frame) > 100, **values)
                game.hold("e", .06)
                time.sleep(.2)
                frame, values = observe("fresh-e1m8")
                check("ordinary-transition-fresh-bosses", values["kills"] == 0 and values["health"] == 100 and
                      all(fixture.glyph(frame, 8 + i * 12, 373, b"\xc0\xc0\xc0") == fixture.GLYPHS[c]
                          for i, c in enumerate("E1M8")), **values)
                pickup()
                pair()
                go_open()
            elif case == "restart":
                game.hold("r", .06)
                frame, values = observe("restart-defaults")
                check("restart-restores-player-bosses-inventory", values["kills"] == 0 and
                      values["ammo"] == 50 and rocket.colors(frame)["133"] > 1000, **values)
                pickup()
                pair()
                go_open()
            elif case == "lowest-first":
                game.hold("w", 8.8)
                game.hold("s", .7)
                observe("first-lowest-contact")
            else:
                frame, values = go_open()
                if case == "lowest-second":
                    game.hold("s", 1.2)
                    observe("second-lowest-contact")
                elif case == "last-baron":
                    time.sleep(5.5)
                    frame, values = observe("natural-damaging-finale")
                    check("ordinary-sector11-completion-after-access",
                          values["health"] == 0 and values["kills"] == 2 and completion_pixels(frame) > 100,
                          completionPixels=completion_pixels(frame), **values)
        game.quit()
        x, y = game.metadata["finalPosition"]
        if case in ("first-only", "knight-only", "wrong-episode", "wrong-map-number", "no-tag"):
            check("raised-barrier-native-contact", 1240 <= x <= 1265 and abs(y) <= 8, position=(x, y))
        elif case in ("last-baron", "mixed", "restart", "transition"):
            check("all-tagged-barriers-ordinary-access", x >= 1504 and abs(y) <= 8, position=(x, y))
        elif case == "lowest-first":
            check("first-barrier-lowest-not-zero", 1296 <= x <= 1310 and abs(y) <= 8, position=(x, y))
        elif case == "lowest-second":
            check("second-barrier-lowest-not-first-neighbor", 1424 <= x <= 1440 and abs(y) <= 8, position=(x, y))
        elif case == "carry":
            check("floor-carry-without-teleport", 40 <= x <= 60 and abs(y) <= 8, position=(x, y))
        else:
            check("dead-scene-stationary-native-pose", (x, y) == (0, 0), position=(x, y))
        log = (game.output / "game.log").read_text()
        check("no-missing-required-assets-or-unsupported-identities",
              "Missing sprite" not in log and "Unsupported thing" not in log)
        if case == "no-tag":
            check("single-explicit-missing-tag-diagnostic",
                  log.count("E1M8 boss action: no sectors tagged666.") == 1)
        game.metadata["completed"] = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="mode", required=True)
    generate_parser = sub.add_parser("generate")
    generate_parser.add_argument("--output-dir", type=Path, required=True)
    runner = sub.add_parser("run")
    runner.add_argument("--executable", type=Path, required=True)
    runner.add_argument("--input-dir", type=Path, required=True)
    runner.add_argument("--source-commit", required=True)
    runner.add_argument("--output-dir", type=Path, required=True)
    runner.add_argument("--case", choices=CASES, required=True)
    runner.add_argument("--dead-control", type=Path)
    args = parser.parse_args()
    if args.mode == "generate":
        generate(args.output_dir)
    else:
        run(args)


if __name__ == "__main__":
    main()
