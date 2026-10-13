"""Original tagged-door fixtures and actual native input/framebuffer scenarios."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys
import time
from native_input import NativeGame

LOCKS = {99: "blue", 133: "blue", 134: "red", 135: "red", 136: "yellow", 137: "yellow"}
KEYS = {"none": 0, "blue-card": 5, "blue-skull": 40, "red-card": 13,
        "red-skull": 38, "yellow-card": 6, "yellow-skull": 39}
BASE_SHA = "0e88a988fdde17f2f1b119cf253fc288d0ff19f6a147b9edcfde4abfbd1f9ba7"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def shorts(*values):
    return struct.pack("<" + "H" * len(values), *(v & 65535 for v in values))


def name(value):
    return value.encode("ascii").ljust(8, b"\0")


def fixture(assets, special, key, variant):
    vertices = [(x, y) for x in (-192, 0, 64, 128, 256) for y in (-128, 128)]
    sides, lines = [], []

    def side(sector, portal):
        sides.append(shorts(0, 0) + name("QLWALL" if portal else "-") * 2 +
                     name("-" if portal else "QLWALL") + shorts(sector))
        return len(sides) - 1

    def line(a, b, right, left=None, action=0, tag=0):
        lines.append(shorts(a, b, 1 if left is None else 4, action, tag,
                            side(right, left is not None),
                            65535 if left is None else side(left, True)))
        return len(lines) - 1

    west = line(0, 1, 0)
    tops = [line(2 * i + 1, 2 * i + 3, i) for i in range(4)]
    portals = [line(2 * i + 1, 2 * i, i - 1, i,
                    special if i == 1 else 0, (8 if variant == "wrong-tag" else 7) if i == 1 else 0)
               for i in range(1, 4)]
    east = line(9, 8, 3)
    bottoms = [line(2 * i + 2, 2 * i, i) for i in range(4)]
    # Ordinary W1 close, reached only on returning from the first traversal.
    vertices.extend([(-64, 128), (-64, -128)])
    line(10, 11, 0, 0, 3, 7)
    segs = []
    for i in range(4):
        for a, b, index, direction in [
            (2 * i, 2 * i + 1, west if i == 0 else portals[i - 1], 0 if i == 0 else 1),
            (2 * i + 1, 2 * i + 3, tops[i], 0),
            (2 * i + 3, 2 * i + 2, east if i == 3 else portals[i], 0),
            (2 * i + 2, 2 * i, bottoms[i], 0),
        ]:
            segs.append(shorts(a, b, 0, index, direction, 0))
    x = 32 if variant == "backside" else -32
    things = shorts(x, 0, 180 if variant == "backside" else 0, 1, 7)
    if KEYS[key]:
        things += shorts(-96 if variant == "retry" else x, 0, 0, KEYS[key], 7)
    sectors = b"".join(shorts(0, 0 if i == 2 else 128) +
                       name("QLFLOOR") + name("QLCEIL") + shorts(192, 0, 7 if i == 2 else 0)
                       for i in range(4))
    nodes = b"".join(shorts(split, 128, 0, -256, 128, -128, -192, split,
                            128, -128, split, (64, 128, 256)[i],
                            0x8000 if i == 0 else i - 1, 0x8000 | (i + 1))
                     for i, split in enumerate((0, 64, 128)))
    lumps = assets + [
        ("E1M1", b""), ("THINGS", things), ("LINEDEFS", b"".join(lines)),
        ("SIDEDEFS", b"".join(sides)), ("VERTEXES", b"".join(shorts(*v) for v in vertices)),
        ("SEGS", b"".join(segs)), ("SSECTORS", b"".join(shorts(4, i * 4) for i in range(4))),
        ("NODES", nodes), ("SECTORS", sectors), ("REJECT", b"\0\0"),
        ("BLOCKMAP", shorts(-192, -128, 4, 2, *([12] * 8), 0, *range(len(lines)), 65535)),
    ]
    body, directory = bytearray(), bytearray()
    for lump_name, data in lumps:
        directory.extend(struct.pack("<ii", 12 + len(body), len(data)) + name(lump_name))
        body.extend(data)
    return b"PWAD" + struct.pack("<ii", len(lumps), 12 + len(body)) + body + directory


def generate(output):
    base = Path(__file__).resolve().parent.parent / "tools/e2e-fixtures/generated/portal-equal.wad"
    data = base.read_bytes()
    if sha(data) != BASE_SHA:
        raise RuntimeError("Generate the unchanged original publisher fixtures first.")
    count, directory = struct.unpack_from("<ii", data, 4)
    assets = []
    for i in range(count):
        offset, size, raw_name = struct.unpack_from("<ii8s", data, directory + i * 16)
        label = raw_name.rstrip(b"\0").decode("ascii")
        if label == "E1M1":
            break
        assets.append((label, data[offset:offset + size]))
    patch = next(data for label, data in assets if label == "BKEYA0")
    index = next(i for i, (label, _) in enumerate(assets) if label == "S_END")
    assets[index:index] = [(label, patch) for label in ("RKEYA0", "YKEYA0", "BSKUA0", "RSKUA0", "YSKUA0")]
    output.mkdir(parents=True, exist_ok=False)
    records = {}
    cases = [(special, color + "-card", "normal") for special, color in LOCKS.items()]
    cases += [(special, color + "-skull", "normal") for special, color in LOCKS.items()
              if special in (99, 134, 136)]
    cases += [(99, "red-card", "normal"), (134, "yellow-card", "normal"), (136, "blue-card", "normal"),
              (99, "none", "normal"), (99, "blue-card", "wrong-tag"),
              (99, "blue-card", "backside"), (133, "blue-card", "retry")]
    for special, key, variant in cases:
        label = f"{special}-{key}-{variant}.wad"
        data = fixture(assets, special, key, variant)
        (output / label).write_bytes(data)
        records[label] = sha(data)
    (output / "fixtures.json").write_text(json.dumps({
        "originalAssetFixtureSha256": BASE_SHA, "generatorSha256": sha(Path(__file__).read_bytes()),
        "fixtures": records, "limits": "Original scenarios, not a natural real-map campaign."
    }, indent=2) + "\n")
    print("Generated original four-sector tagged-door inputs:", output)


def difference(a, b):
    if (a.width, a.height) != (b.width, b.height):
        raise RuntimeError("Native frame dimensions differ.")
    # Exclude top messages and bottom HUD, not changing-world gameplay pixels.
    start, end = a.width * (a.height // 10) * 3, a.width * (a.height * 7 // 8) * 3
    return sum(a.rgb[i:i + 3] != b.rgb[i:i + 3] for i in range(start, end, 3)) / ((end - start) // 3)


def reference(path, game, mode):
    data = json.loads(path.read_text())
    expected_gate = "tagged-door-traversal" if mode == "open" else "unactivated-door-blocks"
    expected_x = data["finalPosition"][0]
    if (data["sourceCommit"] != game.metadata["sourceCommit"] or
            data["executableSha256"] != game.metadata["executableSha256"] or
            data["fixtureSha256"] != game.metadata["fixtureSha256"] or
            data["runnerSha256"] != sha(Path(__file__).read_bytes()) or
            data["scenarioMode"] != mode or not data["completed"] or data["returncode"] != 0 or
            expected_gate not in {a["name"] for a in data["assertions"]} or
            not all(a["passed"] for a in data["assertions"]) or
            (expected_x < 192 if mode == "open" else not 32 <= expected_x <= 48)):
        raise RuntimeError("Require passing same-build/input native reference: " + mode)
    frame = data["frames"][-1]
    raw = (path.parent / (frame["name"] + ".ppm")).read_bytes()
    if sha(raw) != frame["sha256"]:
        raise RuntimeError("Reference native frame hash mismatch.")
    magic, dimensions, maximum, rgb = raw.split(b"\n", 3)
    width, height = map(int, dimensions.split())
    if magic != b"P6" or maximum != b"255" or len(rgb) != width * height * 3:
        raise RuntimeError("Incomplete reference native frame.")
    from native_input import Frame
    return Frame(width, height, rgb, frame["sha256"])


def run(args):
    label = f"{args.special}-{args.key}-{args.variant}.wad"
    wad = args.input_dir / label
    manifest = json.loads((args.input_dir / "fixtures.json").read_text())
    if sha(wad.read_bytes()) != manifest["fixtures"][label]:
        raise RuntimeError("Original scenario input hash mismatch.")
    with NativeGame(args.executable, wad, args.source_commit, args.output_dir) as game:
        game.metadata.update(runnerSha256=sha(Path(__file__).read_bytes()), scenarioMode=args.mode,
                             special=args.special, key=args.key, variant=args.variant)
        time.sleep(.5)
        initial = game.capture("01-closed")
        if args.mode == "unactivated":
            game.hold("w", 2.2)
            time.sleep(1)
            game.capture("02-blocked")
        elif args.mode == "fast":
            game.hold("e", .06)
            game.key("Shift_L", True)
            game.hold("w", .4)
            game.key("Shift_L", False)
            game.capture("02-fast-traversal")
        else:
            game.hold("e", .06)
            time.sleep(.5)
            opened = game.capture("02-after-use")
            delta = difference(initial, opened)
            if args.mode in ("denied", "recover"):
                game.check("denied-use-does-not-open", delta <= .001, changedPixelFraction=delta, maximum=.001)
            else:
                game.check("use-opens-visible-remote-door", delta > .1, changedPixelFraction=delta, minimum=.1)
            if args.mode == "recover":
                game.hold("s", .35)
                game.hold("w", .35)
                time.sleep(.5)
                game.hold("e", .06)
                time.sleep(.5)
            game.hold("s" if args.variant == "backside" else "w", 2.2)
            time.sleep(.3)
            crossed = game.capture("03-traversed")
            if args.mode == "repeat":
                if args.open_reference is None or args.closed_reference is None:
                    raise RuntimeError("Repeat requires both independent native references.")
                delta = difference(crossed, reference(args.open_reference, game, "open"))
                game.check("first-traversal-before-return", delta <= .001, changedPixelFraction=delta, maximum=.001)
                if game.metadata["assertions"][-1]["passed"]:
                    game.hold("s", 2.5)
                    time.sleep(1.5)
                    game.hold("w", 2)
                    closed = game.capture("04-naturally-reclosed-block")
                    delta = difference(closed, reference(args.closed_reference, game, "unactivated"))
                    game.check("natural-close-blocks-before-second-use", delta <= .001,
                               changedPixelFraction=delta, maximum=.001)
                    if game.metadata["assertions"][-1]["passed"]:
                        game.hold("s", .55)
                        game.hold("e", .06)
                        time.sleep(.5)
                        game.hold("w", 2.2)
                        game.capture("05-after-second-use")
        game.quit()
        x, y = game.metadata["finalPosition"]
        if args.mode == "repeat":
            should_open = args.special in (99, 134, 136)
            game.check("SR-reopens-or-consumed-S1-stays-closed",
                       (x >= 192 if should_open else 32 <= x <= 48) and abs(y) < 1,
                       observedX=x, expected=">=192" if should_open else "[32,48]")
        elif args.mode == "fast":
            game.check("fast-opening-allows-short-run", 56 <= x <= 90 and abs(y) < 1,
                       observedX=x, expected="[56,90]", forwardRunSeconds=.4)
        else:
            blocked = args.mode in ("unactivated", "denied")
            game.check("unactivated-door-blocks" if args.mode == "unactivated" else
                       "locked-door-blocks" if blocked else "tagged-door-traversal",
                       (32 <= x <= 48 if blocked else x >= 192) and abs(y) < 1,
                       observedX=x, expected="[32,48]" if blocked else ">=192")
        passed = all(a["passed"] for a in game.metadata["assertions"])
        game.metadata.update(completed=True, outcome="passed" if passed else "failed")
        if not passed:
            raise SystemExit(1)


def qualify(args):
    args.output_dir.mkdir(parents=True, exist_ok=False)
    cases = [(f"open-{special}", special, color + "-card", "normal", "open")
             for special, color in LOCKS.items()]
    cases += [(f"unactivated-{s}", s, "blue-card", "normal", "unactivated") for s in (99, 133)]
    cases += [(f"repeat-{s}", s, "blue-card", "normal", "repeat") for s in (99, 133)]
    cases += [("fast-99", 99, "blue-card", "normal", "fast")]
    cases += [(f"skull-{s}", s, LOCKS[s] + "-skull", "normal", "open") for s in (99, 134, 136)]
    cases += [("wrong-blue", 99, "red-card", "normal", "denied"),
              ("wrong-red", 134, "yellow-card", "normal", "denied"),
              ("wrong-yellow", 136, "blue-card", "normal", "denied"),
              ("no-key", 99, "none", "normal", "denied"),
              ("wrong-tag", 99, "blue-card", "wrong-tag", "denied"),
              ("backside", 99, "blue-card", "backside", "denied"),
              ("denial-recovery", 133, "blue-card", "retry", "recover")]
    packet = {"sourceCommit": args.source_commit, "executableSha256": sha(args.executable.read_bytes()),
              "runnerSha256": sha(Path(__file__).read_bytes()), "plannedScenarios": len(cases),
              "invocationsPerScenario": 1, "completed": False, "results": [],
              "limits": "Original fixtures/native virtual input, not campaign, physical input or audio."}
    for label, special, key, variant, mode in cases:
        command = [sys.executable, str(Path(__file__).resolve()), "run", str(args.executable.resolve()),
                   "--source-commit", args.source_commit, "--input-dir", str(args.input_dir.resolve()),
                   "--output-dir", str((args.output_dir / label).resolve()),
                   "--special", str(special), "--key", key, "--variant", variant, "--mode", mode]
        if mode == "repeat":
            command += ["--open-reference", str((args.output_dir / f"open-{special}/result.json").resolve()),
                        "--closed-reference", str((args.output_dir / f"unactivated-{special}/result.json").resolve())]
        with (args.output_dir / (label + ".log")).open("w") as log:
            code = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=45).returncode
        packet["results"].append({"scenario": label, "runnerExit": code, "command": command})
        (args.output_dir / "results.json").write_text(json.dumps(packet, indent=2) + "\n")
        print(label, "PASS" if code == 0 else "FAIL", flush=True)
        if code != 0:
            raise SystemExit(code)
    packet["completed"] = True
    (args.output_dir / "results.json").write_text(json.dumps(packet, indent=2) + "\n")


parser = argparse.ArgumentParser(description=__doc__)
commands = parser.add_subparsers(dest="command", required=True)
gen = commands.add_parser("generate")
gen.add_argument("--output-dir", type=Path, required=True)
play = commands.add_parser("run")
play.add_argument("executable", type=Path)
play.add_argument("--source-commit", required=True)
play.add_argument("--input-dir", type=Path, required=True)
play.add_argument("--output-dir", type=Path, required=True)
play.add_argument("--special", type=int, choices=LOCKS, required=True)
play.add_argument("--key", choices=KEYS, required=True)
play.add_argument("--variant", choices=("normal", "wrong-tag", "backside", "retry"), default="normal")
play.add_argument("--mode", choices=("open", "unactivated", "repeat", "fast", "denied", "recover"), required=True)
play.add_argument("--open-reference", type=Path)
play.add_argument("--closed-reference", type=Path)
batch = commands.add_parser("qualify")
batch.add_argument("executable", type=Path)
batch.add_argument("--source-commit", required=True)
batch.add_argument("--input-dir", type=Path, required=True)
batch.add_argument("--output-dir", type=Path, required=True)
args = parser.parse_args()
if args.command == "generate":
    generate(args.output_dir)
elif args.command == "run":
    run(args)
else:
    qualify(args)
