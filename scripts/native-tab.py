"""Observe native XK_Tab on/off plus W/Ctrl controls in the real game."""
import argparse
from pathlib import Path
import hashlib
from native_input import NativeGame

parser = argparse.ArgumentParser()
parser.add_argument("executable", type=Path)
parser.add_argument("--source-commit", required=True)
parser.add_argument("--output-dir", type=Path, required=True)
parser.add_argument("--wad", type=Path, default=Path("tools/e2e-fixtures/generated/portal-equal.wad"))
args = parser.parse_args()
digits = [
    ".###.#...##..###.#.###..##...#.###.",
    "..#...##....#....#....#....#...###.",
    ".###.#...#....#...#...#...#...#####",
    "####.....#....#.###.....#....#####.",
    "...#...##..#.#.#..#.#####...#....#.",
    "######....#....####.....#....#####.",
    ".###.#....#....####.#...##...#.###.",
    "#####....#...#...#...#....#....#...",
    ".###.#...##...#.###.#...##...#.###.",
    ".###.#...##...#.####....#....#.###.",
]


def ammo(frame):
    value = ""
    # Observe the rendered HUD, not game state; default health100/armor0.
    for x in (320, 332):
        glyph = "".join(
            "#" if frame.pixel(int((x + col * 2 + .5) * frame.width / 640),
                               int((355 + row * 2 + .5) * frame.height / 400)) == bytes((255, 219, 138)) else "."
            for row in range(7) for col in range(5))
        if glyph not in digits:
            raise RuntimeError(f"Cannot decode actual ammo HUD glyph: {glyph}")
        value += str(digits.index(glyph))
    return int(value)


def background(frame):
    viewport = frame.rgb[:frame.width * (frame.height * 7 // 8) * 3]
    return sum(viewport[i:i + 3] == b"\x08\x09\x0b" for i in range(0, len(viewport), 3)) / (len(viewport) // 3)


with NativeGame(args.executable, args.wad, args.source_commit, args.output_dir) as game:
    game.metadata["runnerSha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    initial = game.capture("01-initial-3d")
    initial_ammo = ammo(initial)
    game.hold("Tab", .06)
    on = game.capture("02-tab-on")
    game.hold("Tab", .06)
    off = game.capture("03-tab-off")
    game.hold("w", .50)
    moved = game.capture("04-forward-control")
    game.hold("Control_L", .40)
    fired = game.capture("05-fire-control")
    final_ammo = ammo(fired)
    game.quit()
    fractions = [background(frame) for frame in (initial, on, off)]
    game.check("initial-3d-background", fractions[0] < .05, fraction=fractions[0], maximum=.05)
    game.check("native-XK-Tab-opens-automap", fractions[1] > .90, fraction=fractions[1], minimum=.90)
    game.check("native-XK-Tab-closes-automap", fractions[2] < .05, fraction=fractions[2], maximum=.05)
    game.check("W-positive-control", 0 <= game.metadata["finalPosition"][0] <= 30,
               initialX=-64, finalPosition=game.metadata["finalPosition"], expectedX="[0,30]")
    game.check("Ctrl-positive-control-ammo-HUD", initial_ammo == 50 and final_ammo == 48,
               initialAmmo=initial_ammo, finalAmmo=final_ammo)
    game.metadata["completed"] = True
    passed = all(check["passed"] for check in game.metadata["assertions"])
    game.metadata["outcome"] = "passed" if passed else "failed"
    if not passed:
        raise SystemExit(1)
