"""Original weapon fixtures; qualify only the published game through native OS input."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import time
from native_input import NativeGame

BASE_SHA = "0e88a988fdde17f2f1b119cf253fc288d0ff19f6a147b9edcfde4abfbd1f9ba7"
CASES = ("pickup-select", "cadence", "capacity", "combat", "accuracy-spread",
         "drop", "one-empty", "unowned", "carry", "restart", "death", "dead-next", "missing",
         "audio", "audio-missing")
GLYPHS = {
    "0": ".###.#...##..###.#.###..##...#.###.",
    "1": "..#...##....#....#....#....#...###.",
    "2": ".###.#...#....#...#...#...#...#####",
    "3": "####.....#....#.###.....#....#####.",
    "4": "...#...##..#.#.#..#.#####...#....#.",
    "5": "######....#....####.....#....#####.",
    "6": ".###.#....#....####.#...##...#.###.",
    "7": "#####....#...#...#...#....#....#...",
    "8": ".###.#...##...#.###.#...##...#.###.",
    "9": ".###.#...##...#.####....#....#.###.",
    "A": ".###.#...##...#######...##...##...#",
    "C": ".#####....#....#....#....#.....####",
    "D": "####.#...##...##...##...##...#####.",
    "E": "######....#....####.#....#....#####",
    "F": "######....#....####.#....#....#....",
    "G": ".#####....#....#.####...##...#.###.",
    "H": "#...##...##...#######...##...##...#",
    "I": ".###...#....#....#....#....#...###.",
    "M": "#...###.###.#.##...##...##...##...#",
    "N": "#...###..##.#.##..###...##...##...#",
    "O": ".###.#...##...##...##...##...#.###.",
    "T": "#####..#....#....#....#....#....#..",
    "U": "#...##...##...##...##...##...#.###.",
    "W": "#...##...##...##...##.#.###.###...#",
    "/": "....#....#...#...#...#...#....#....",
    " ": "." * 35,
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def shorts(*values):
    return struct.pack("<" + "H" * len(values), *(v & 65535 for v in values))


def name(value):
    return value.encode("ascii").ljust(8, b"\0")


def patch(width, height, left, top, color):
    columns = [bytes((0, height, 0)) + bytes([color]) * height + b"\0\xff"
               for _ in range(width)]
    return (shorts(width, height, left, top) +
            b"".join(struct.pack("<i", 8 + width * 4 + x * (height + 5)) for x in range(width)) +
            b"".join(columns))


def room(label, things, special=0, exit_wall=False, barrier=False):
    vertices = [(-128, -256), (-128, 256), (1536, 256), (1536, -256)]
    if exit_wall:
        vertices[2:] = [(256, 256), (256, -256)]
    sides, lines, segs = [], [], []
    for i in range(4):
        sides.append(shorts(0, 0) + name("-") * 2 + name("QLWALL") + shorts(0))
        lines.append(shorts(i, (i + 1) % 4, 1, 11 if exit_wall and i == 2 else 0, 0, i, 65535))
        segs.append(shorts(i, (i + 1) % 4, 0, i, 0, 0))
    if barrier:
        vertices.extend([(992, 256), (992, -256)])
        for _ in range(2):
            sides.append(shorts(0, 0) + name("-") * 3 + shorts(0))
        lines.append(shorts(4, 5, 6, 0, 0, 4, 5))
    width = (vertices[2][0] + 128) // 128
    cells = width * 4
    return [
        (label, b""), ("THINGS", b"".join(shorts(x, y, 0, kind, 7) for x, y, kind in things)),
        ("LINEDEFS", b"".join(lines)), ("SIDEDEFS", b"".join(sides)),
        ("VERTEXES", b"".join(shorts(*v) for v in vertices)), ("SEGS", b"".join(segs)),
        ("SSECTORS", shorts(4, 0)), ("NODES", b""),
        ("SECTORS", shorts(0, 128) + name("QLFLOOR") + name("QLCEIL") + shorts(255, special, 0)),
        ("REJECT", b"\0"),
        ("BLOCKMAP", shorts(-128, -256, width, 4, *([4 + cells] * cells),
                            0, *range(len(lines)), 65535)),
    ]


def generate(output):
    original = Path(__file__).resolve().parent.parent / "tools/e2e-fixtures/generated/portal-equal.wad"
    data = original.read_bytes()
    if sha(data) != BASE_SHA:
        raise RuntimeError("Generate the unchanged original publisher fixtures first.")
    count, directory = struct.unpack_from("<ii", data, 4)
    assets = []
    for i in range(count):
        offset, size, raw = struct.unpack_from("<ii8s", data, directory + i * 16)
        label = raw.rstrip(b"\0").decode("ascii")
        if label == "E1M1":
            break
        if label == "PLAYPAL":
            palette = bytearray(data[offset:offset + size])
            for start in range(0, len(palette), 768):
                palette[start + 186 * 3:start + 186 * 3 + 3] = b"\0\xff\0"
            assets.append((label, bytes(palette)))
        else:
            assets.append((label, data[offset:offset + size]))
    new_patches = [
        ("MGUNA0", patch(32, 56, 16, 56, 186)),
        ("CLIPA0", patch(16, 16, 8, 16, 175)),
        ("AMMOA0", patch(24, 24, 12, 24, 160)),
        ("SHOTA0", patch(32, 20, 16, 20, 180)),
        ("PISGA0", patch(64, 44, -128, -131, 133)),
        ("PISGB0", patch(64, 44, -128, -131, 141)),
        ("SHTGA0", patch(64, 44, -128, -131, 111)),
        ("SHTGB0", patch(64, 44, -128, -131, 119)),
        ("CHGGA0", patch(64, 44, -128, -131, 151)),
        ("CHGGB0", patch(64, 44, -128, -131, 173)),
        ("CHGFA0", patch(16, 16, -152, -116, 237)),
        ("CHGFB0", patch(16, 16, -152, -116, 219)),
    ]
    new_patches += [(f"{prefix}{frame}0", patch(32, 56, 16, 56, 80))
                    for prefix in ("POSS", "CPOS") for frame in "ABCDEFGHIJKLMNOPQRSTU"]
    index = next(i for i, (label, _) in enumerate(assets) if label == "S_END")
    assets[index:index] = new_patches
    tone = b"\x80" * 16 + bytes(168 if i % 50 < 25 else 88 for i in range(4000)) + b"\x80" * 16
    sound = shorts(3, 11025) + struct.pack("<I", len(tone)) + tone
    assets += [("DSPISTOL", sound), ("DSITEMUP", sound)]
    output.mkdir(parents=True, exist_ok=False)
    records = {}
    for case in CASES:
        things = [(0, 0, 1), (0, 0, 2002)]
        if case == "pickup-select":
            things = [(0, 0, 1), (48, 0, 2002), (96, 0, 2001),
                      (160, 0, 2007), (256, 0, 2048)]
        elif case == "capacity":
            things = [(0, 0, 1)] + [(0, 0, 2048)] * 3 + [(48, 0, 2002), (96, 0, 2002)]
        elif case in ("combat", "accuracy-spread", "one-empty"):
            things += [(1024, 50 if case == "accuracy-spread" else 180 if case == "one-empty" else 0, 3004)]
            if case == "one-empty":
                things.append((128, 0, 2007))
        elif case == "drop":
            things = [(0, 0, 1), (1024, 0, 65)]
        elif case == "unowned":
            things = [(0, 0, 1)]
        elif case in ("carry", "restart", "death", "dead-next"):
            things[1] = (48, 0, 2002)
        lumps = [(n, b) for n, b in assets
                 if (case != "missing" or n not in ("CHGGB0", "CHGFA0")) and
                 (case != "audio-missing" or n != "DSPISTOL")]
        if case == "one-empty":
            index = next(i for i, (n, _) in enumerate(lumps) if n == "PLAYPAL")
            palette = bytearray(lumps[index][1])
            for start in range(0, len(palette), 768):
                palette[start + 80 * 3:start + 80 * 3 + 3] = b"\xff\0\xff"
            lumps[index] = ("PLAYPAL", bytes(palette))
        lumps += room("E1M1", things, 5 if case == "death" else 11 if case == "dead-next" else 0,
                      case == "carry", case in ("combat", "accuracy-spread", "one-empty", "drop"))
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
        "originalAssetFixtureSha256": BASE_SHA, "generatorSha256": sha(Path(__file__).read_bytes()),
        "fixtures": records, "limits": "Original synthetic maps and color-coded patches, not vanilla artwork/campaign."
    }, indent=2) + "\n")


def glyph(frame, x, y, color):
    if (frame.width, frame.height) != (960, 600):
        raise RuntimeError("Require the normal 960x600 native game window.")
    return "".join("#" if frame.pixel(int((x + col * 2 + 1) * 1.5),
                                     int((y + row * 2 + 1) * 1.5)) == color else "."
                   for row in range(7) for col in range(5))


def number(frame, column, y=355, color=b"\xff\xdb\x8a", terminator=" "):
    result = ""
    for i in range(4):
        observed = glyph(frame, 8 + (column + i) * 12, y, color)
        if observed == GLYPHS[terminator]:
            break
        matches = [digit for digit in "0123456789" if GLYPHS[digit] == observed]
        if len(matches) != 1:
            raise RuntimeError(f"Unreadable visible numeric HUD at column {column + i}: {observed}")
        result += matches[0]
    if not result:
        raise RuntimeError("Missing numeric HUD.")
    return int(result), len(result)


def hud(frame):
    health, health_digits = number(frame, 7)
    armor_column = 7 + health_digits + 8
    armor, armor_digits = number(frame, armor_column)
    ammo, _ = number(frame, armor_column + armor_digits + 7)
    kills, _ = number(frame, 12, 373, b"\xc0\xc0\xc0", "/")
    return {"health": health, "armor": armor, "ammo": ammo, "kills": kills}


def message(frame, text):
    return all(glyph(frame, 8 + i * 12, 8, b"\xff\xee\x90") == GLYPHS[char]
               for i, char in enumerate(text))


def colors(frame):
    region = b"".join(frame.rgb[(y * 960 + 380) * 3:(y * 960 + 585) * 3] for y in range(340, 525))
    result = {str(color): region.count(bytes([color]) * 3) for color in (111, 119, 133, 141, 151, 173, 219, 237)}
    result["chaingunDamageTint"] = region.count(bytes((148, 112, 112)))
    return result


def green(frame):
    return sum(frame.rgb[i + 1] > frame.rgb[i] + 50 and frame.rgb[i + 1] > frame.rgb[i + 2] + 50
               for i in range(0, 960 * 525 * 3, 3))

def target_marker(frame):
    points = []
    for y in range(230, 310):
        for x in range(440, 520):
            red, green, blue = frame.pixel(x, y)
            if red > green + 30 and blue > green + 30:
                points.append(x)
    return {"pixels": len(points), "centerX": sum(points) / len(points) if points else None}


def run(args):
    manifest = json.loads((args.input_dir / "fixtures.json").read_text())
    wad = args.input_dir / (args.case + ".wad")
    if sha(wad.read_bytes()) != manifest["fixtures"][wad.name]:
        raise RuntimeError("Original scenario input hash mismatch.")
    session = NativeGame(args.executable, wad, args.source_commit, args.output_dir)
    if args.case in ("audio", "audio-missing"):
        session.metadata["command"].remove("--no-audio")
        config = session.output / "asound.conf"
        config.write_text("pcm.!default { type null }\n")
        os.environ["ALSA_CONFIG_PATH"] = str(config)
        session.metadata["limits"] += " This case exercises ALSA null submission, not audible playback."
    with session as game:
        game.metadata.update(runnerSha256=sha(Path(__file__).read_bytes()), scenario=args.case)

        def observe(label):
            frame = game.capture(label)
            values = hud(frame)
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
            time.sleep(.18)

        time.sleep(.25)
        initial, start = observe("01-start")
        case = args.case
        if case in ("pickup-select", "capacity", "carry", "restart", "death", "dead-next"):
            expected = 200 if case == "capacity" else 50
            check("initial-bullet-pool", start["ammo"] == expected and colors(initial)["133"] > 1000, **start)
            game.hold("w", .35)
            time.sleep(.18)
            picked, values = observe("02-pickup")
            check("map-chaingun-pickup", values["ammo"] == (200 if case == "capacity" else 70) and
                  (colors(picked)["151"] > 1000 or case in ("death", "dead-next") and
                   colors(picked)["chaingunDamageTint"] > 1000), **values)
        elif case not in ("drop", "unowned"):
            check("spawn-chaingun-pickup", start["ammo"] == 70 and colors(initial)["151"] > 1000, **start)
        if case == "pickup-select":
            game.hold("1", .06)
            tap()
            pistol, values = observe("03-pistol")
            check("shared-pistol-bullet", values["ammo"] == 69 and colors(pistol)["133"] > 1000, **values)
            game.hold("w", .30)
            time.sleep(.18)
            shotgun, values = observe("04-shotgun")
            check("shotgun-pickup-preserved", values["ammo"] == 8 and colors(shotgun)["111"] > 1000, **values)
            tap()
            shot, values = observe("05-shell-used")
            check("shotgun-shell-use", values["ammo"] == 7, **values)
            game.hold("4", .06)
            time.sleep(.3)
            selected, values = observe("06-selected-chaingun")
            check("native-key4-selects-owned", values["ammo"] == 69 and colors(selected)["151"] > 1000, **values)
            game.hold("w", .42)
            time.sleep(.18)
            _, values = observe("07-clip")
            check("clip-adds-ten-shared-bullets", values["ammo"] == 79, **values)
            game.hold("w", .65)
            time.sleep(.18)
            _, values = observe("08-box")
            check("box-adds-fifty-shared-bullets", values["ammo"] == 129, **values)
            game.hold("2", .06)
            final, values = observe("09-shotgun-return")
            check("shotgun-selection-retains-shells", values["ammo"] == 7 and colors(final)["111"] > 1000, **values)
        elif case in ("cadence", "missing"):
            game.key("Control_L", True)
            time.sleep(.04)
            first, first_hud = observe("02-first-shot")
            time.sleep(.10)
            second, second_hud = observe("03-second-shot")
            if case == "cadence":
                check("distinct-body-and-flash-phases", colors(first)["151"] > 1000 and
                      colors(first)["237"] > 100 and colors(second)["173"] > 1000 and
                      colors(second)["219"] > 100, first=colors(first), second=colors(second))
            time.sleep(.8)
            _, held = observe("04-held")
            delta = game.metadata["frames"][-1]["seconds"] - game.metadata["frames"][-2]["seconds"]
            check("four-tick-sustained-cadence", 6 <= second_hud["ammo"] - held["ammo"] <= 8 and
                  .79 <= delta <= 1.1, bullets=second_hud["ammo"] - held["ammo"], seconds=delta)
            game.hold("1", .06)
            game.key("Control_L", False)
            time.sleep(.35)
            released, stopped = observe("05-released-selected-pistol")
            check("queued-selection-completes-pair", 0 <= held["ammo"] - stopped["ammo"] <= 2 and
                  colors(released)["133"] > 1000, held=held, stopped=stopped)
            time.sleep(.5)
            _, stable = observe("06-stable")
            check("released-trigger-stops-fire", stable["ammo"] == stopped["ammo"], **stable)
            game.hold("4", .06)
            before = stable["ammo"]
            tap()
            tapped, values = observe("07-tapped-pair")
            check("tap-completes-two-shots", values["ammo"] == before - 2 and
                  colors(tapped)["151"] > 1000 and colors(tapped)["219"] == colors(tapped)["237"] == 0, **values)
            if case == "missing":
                check("missing-assets-not-pistol-substitution", colors(second)["133"] == colors(second)["141"] == 0,
                      observed=colors(second))
        elif case == "capacity":
            game.hold("w", .30)
            game.hold("s", .30)
            remaining, values = observe("03-full-duplicate-remains")
            check("owned-full-ammo-pickup-remains", values["ammo"] == 200 and green(remaining) > 100,
                  greenPixels=green(remaining), **values)
            game.hold("1", .06)
            tap()
            _, values = observe("04-spent-one")
            check("capacity-still-consumes-ammo", values["ammo"] == 199, **values)
            game.hold("w", .30)
            game.hold("s", .30)
            time.sleep(.18)
            consumed, values = observe("05-duplicate-consumed")
            check("duplicate-clamps-without-forced-selection", values["ammo"] == 200 and
                  green(consumed) == 0 and colors(consumed)["133"] > 1000, greenPixels=green(consumed), **values)
        elif case in ("combat", "accuracy-spread"):
            if case == "accuracy-spread":
                for _ in range(3):
                    tap()
                _, accurate = observe("02-accurate-pairs-miss-off-axis")
                check("fresh-pairs-have-no-horizontal-spread", accurate["ammo"] == 64 and accurate["kills"] == 0, **accurate)
            if case == "accuracy-spread":
                game.hold("Control_L", 6)
            else:
                for _ in range(3):
                    tap()
            time.sleep(.18)
            _, values = observe("03-combat")
            check("sustained-spread-hits-off-axis" if case == "accuracy-spread" else "hitscan-kills-target",
                  values["kills"] == 1 and 0 < values["ammo"] < 70 and values["health"] == 100, **values)
        elif case == "drop":
            game.hold("Control_L", 5)
            _, killed = observe("02-chaingunner-killed")
            check("pistol-kills-chaingunner", killed["kills"] == 1 and 0 < killed["ammo"] < 50, **killed)
            game.hold("w", 7.0)
            time.sleep(.18)
            dropped, values = observe("03-dropped-chaingun")
            check("chaingunner-drops-weapon-ten-bullets", values["ammo"] == killed["ammo"] + 10 and
                  colors(dropped)["151"] > 1000, **values)
        elif case == "one-empty":
            game.hold("1", .06)
            tap()
            _, values = observe("02-odd-bullet-pool")
            check("pistol-prepares-odd-shared-pool", values["ammo"] == 69, **values)
            game.hold("4", .06)
            game.hold("Control_L", 6)
            time.sleep(.18)
            _, values = observe("03-drained-pairs")
            check("drain-retains-odd-positive-ammo", 1 <= values["ammo"] <= 19 and values["ammo"] % 2 == 1, **values)
            pair = 0
            while values["ammo"] > 1 and pair < 9:
                before = values["ammo"]
                tap()
                _, values = observe(f"04-drain-{pair:02}")
                check(f"ordinary-pair-{pair:02}", values["ammo"] == before - 2, **values)
                pair += 1
            check("one-bullet-remains", values["ammo"] == 1, **values)
            game.key("Control_L", True)
            time.sleep(.04)
            flash, last = observe("05-last-successful-shot")
            check("one-bullet-one-successful-shot", last["ammo"] == 0 and colors(flash)["237"] > 100, **last)
            game.key("Control_L", False)
            time.sleep(.4)
            empty, values = observe("06-empty-followup")
            check("empty-followup-no-negative-ammo-or-flash", values["ammo"] == 0 and values["kills"] == 0 and
                  colors(empty)["219"] == colors(empty)["237"] == 0 and message(empty, "OUT OF AMMO"), **values)
            game.hold("a", 1.2)
            aligned, values = observe("07-aligned-live-target")
            marker = target_marker(aligned)
            game.metadata["frames"][-1]["targetMarker"] = marker
            check("empty-fire-has-centered-hittable-target", marker["pixels"] > 100 and
                  marker["centerX"] is not None and abs(marker["centerX"] - 480) <= 6.5 and
                  values["ammo"] == values["kills"] == 0, marker=marker, **values)
            game.hold("Control_L", 2)
            dry, values = observe("07-dry-held")
            check("held-empty-no-damage-or-success-effect", values["ammo"] == 0 and values["kills"] == 0 and
                  colors(dry)["219"] == colors(dry)["237"] == 0 and message(dry, "OUT OF AMMO"), **values)
            game.hold("d", 1.2)
            game.hold("w", .85)
            time.sleep(.18)
            _, values = observe("08-empty-pool-clip")
            check("ordinary-clip-restores-empty-pool", values["ammo"] == 10, **values)
            game.hold("a", 1.2)
            aimed, values = observe("09-reloaded-aligned-target")
            marker = target_marker(aimed)
            game.metadata["frames"][-1]["targetMarker"] = marker
            check("reload-retains-centered-target", marker["pixels"] > 100 and marker["centerX"] is not None and
                  abs(marker["centerX"] - 480) <= 8 and values["ammo"] == 10 and values["kills"] == 0,
                  marker=marker, **values)
            tap()
            tap()
            reloaded, values = observe("10-reloaded-kill")
            check("reloaded-pairs-kill-previous-dry-target", values["ammo"] == 6 and values["kills"] == 1 and
                  colors(reloaded)["151"] > 1000, **values)
        elif case == "unowned":
            game.hold("4", .06)
            denied, values = observe("02-unowned-selection")
            check("unowned-selection-rejected-visibly", values["ammo"] == 50 and colors(denied)["133"] > 1000 and
                  message(denied, "CHAINGUN NOT OWNED"), **values)
        elif case in ("audio", "audio-missing"):
            for _ in range(3):
                tap()
            _, values = observe("02-three-audited-pairs")
            check("audio-case-six-successful-shots", values["ammo"] == 64, **values)
        elif case in ("carry", "restart", "death", "dead-next"):
            tap()
            _, values = observe("03-used-before-transition")
            check("inventory-before-reset-or-carry", values["ammo"] == 68, **values)
            if case == "carry":
                game.hold("w", .95)
                game.hold("e", .06)
                game.capture("04-level-complete")
                game.hold("e", .06)
                carried, values = observe("05-carried")
                check("living-next-map-retains-weapon-ammo", values["ammo"] == 68 and
                      colors(carried)["151"] > 1000 and values["health"] == 100, **values)
            else:
                if case != "restart":
                    time.sleep(10.2 if case == "death" else 5.2)
                    _, dead = observe("04-dead")
                    check("ordinary-floor-kills-player", dead["health"] == 0, **dead)
                game.hold("e" if case == "dead-next" else "r", .06)
                fresh, values = observe("05-fresh-defaults")
                check("fresh-default-inventory", values["ammo"] == 50 and values["health"] == 100 and
                      colors(fresh)["133"] > 1000, **values)
                game.hold("4", .06)
                denied, values = observe("06-reset-ownership")
                check("reset-removes-chaingun-ownership", message(denied, "CHAINGUN NOT OWNED") and
                      colors(denied)["133"] > 1000, **values)
        game.quit()
        text = (game.output / "game.log").read_text()
        inventory = re.search(r"Inventory: weapon=(\w+), bullets=(\d+), shells=(\d+), chaingun=(True|False)\.", text)
        check("ordinary-public-inventory-summary", inventory is not None, inventory=inventory.group(0) if inventory else None)
        if inventory:
            game.metadata["inventory"] = {
                "weapon": inventory[1], "bullets": int(inventory[2]),
                "shells": int(inventory[3]), "chaingun": inventory[4] == "True"}
            check("inventory-ammo-bounds", 0 <= int(inventory[2]) <= 200 and 0 <= int(inventory[3]) <= 50)
            check("public-ownership-matches-lifecycle", (inventory[4] == "True") ==
                  (case not in ("unowned", "restart", "death", "dead-next")), **game.metadata["inventory"])
            last_visible = game.metadata["frames"][-1]["hud"]
            check("public-ammo-matches-visible-hud", last_visible["ammo"] ==
                  int(inventory[3] if inventory[1] == "Shotgun" else inventory[2]),
                  visible=last_visible, inventory=game.metadata["inventory"])
        if case in ("carry", "dead-next"):
            check("actual-next-map-spawn", "map=E1M2." in text and game.metadata["finalPosition"] == [-64, 0])
        if case == "missing":
            check("missing-patches-diagnosed-once", text.count("Missing weapon patch CHGGB0;") == 1 and
                  text.count("Missing weapon patch CHGFA0;") == 1)
        if case in ("audio", "audio-missing"):
            audio = re.search(r"Audio: (\d+) sound events, (\d+) PCM samples submitted\.", text)
            check("native-effects-submission", audio is not None and int(audio[1]) ==
                  (7 if case == "audio" else 1) and int(audio[2]) > 22050,
                  audio=audio.group(0) if audio else None)
            check("missing-sound-diagnostic", text.count("Missing sound DSPISTOL;") ==
                  (1 if case == "audio-missing" else 0))
        check("recognized-chaingun-thing", "Unsupported thing type 2002" not in text)
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
