"""Two natural lift cycles, with step-off evidence before the second activation."""
import argparse
import hashlib
import json
from pathlib import Path
import time
from native_input import NativeGame

parser = argparse.ArgumentParser()
parser.add_argument("executable", type=Path)
parser.add_argument("--source-commit", required=True)
parser.add_argument("--output-dir", type=Path, required=True)
parser.add_argument("--step-off-reference", type=Path, required=True,
                    help="Passing unchanged lift.py result.json for this exact executable/source")
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
wad = root / "tools/e2e-fixtures/generated/lift-use-repeat.wad"
reference = json.loads(args.step_off_reference.read_text())
expected_fixture = "e86d622806c6bf68ad3ba1facfeecd312fe04d27ea1b0dfdd4fff288b6297525"
if (reference["sourceCommit"] != args.source_commit or
        reference["executableSha256"] != hashlib.sha256(args.executable.read_bytes()).hexdigest() or
        reference["fixtureSha256"] != expected_fixture or reference["returncode"] != 0 or
        reference["finalPosition"][0] > -32 or not all(c["passed"] for c in reference["assertions"])):
    raise RuntimeError("Reference must qualify the unchanged step-off gate on this exact build.")
phase = next(p for p in reference["phases"] if p["name"] == "11-prolonged-step-off")
reference_bytes = (args.step_off_reference.parent / "11-prolonged-step-off.ppm").read_bytes()
if hashlib.sha256(reference_bytes).hexdigest() != phase["sha256"]:
    raise RuntimeError("Reference native frame hash mismatch.")
magic, dimensions, maximum, reference_rgb = reference_bytes.split(b"\n", 3)
width, height = map(int, dimensions.split())
if magic != b"P6" or maximum != b"255" or len(reference_rgb) != width * height * 3:
    raise RuntimeError("Incomplete reference native frame.")


def landmarks(frame):
    ceiling, wall = None, None
    for row in range(frame.height * 7 // 8):
        color = frame.pixel(int(frame.width * .55), row)
        if color[0] == color[1] == color[2] and 60 <= color[0] <= 78:
            ceiling = row
        else:
            break
    for row in range(frame.height * 7 // 8):
        for col in range(frame.width // 2 - 80, frame.width // 2 + 81, 4):
            color = frame.pixel(col, row)
            if color[0] == color[1] == color[2] and 80 <= color[0] <= 100:
                wall = row
                break
    return ceiling, wall


def until(start, seconds):
    time.sleep(max(0, start + seconds - time.monotonic()))


with NativeGame(args.executable, wad, args.source_commit, args.output_dir) as game:
    if game.metadata["fixtureSha256"] != expected_fixture:
        raise RuntimeError("Unexpected original lift input.")
    game.metadata["runnerSha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    game.metadata["stepOffReference"] = {
        "resultSha256": hashlib.sha256(args.step_off_reference.read_bytes()).hexdigest(),
        "nativeFrameSha256": phase["sha256"], "observedX": reference["finalPosition"][0],
        "unchangedExpectedX": "<=-32", "maximumFrameDifferenceFraction": .001,
    }
    game.metadata["fullLiftCycleVerified"] = False
    for cycle in (1, 2):
        game.hold("w", .30 if cycle == 1 else .60)
        approach = game.capture(f"{cycle}-01-raised-approach")
        start = time.monotonic()
        game.hold("e", .06)
        until(start, .40); game.capture(f"{cycle}-02-descent")
        until(start, 1.15); bottom = game.capture(f"{cycle}-03-bottom")
        game.hold("w", .35); board = game.capture(f"{cycle}-04-board")
        until(start, 2.70); wait = game.capture(f"{cycle}-05-bottom-wait")
        until(start, 4.45); game.capture(f"{cycle}-06-rise")
        until(start, 4.70); late = game.capture(f"{cycle}-07-rise-late")
        until(start, 5.15); top = game.capture(f"{cycle}-08-top")
        until(start, 5.50); stable = game.capture(f"{cycle}-09-top-stable")
        game.hold("s", .50); game.capture(f"{cycle}-10-step-off")
        game.hold("s", 2.0); cleared = game.capture(f"{cycle}-11-prolonged-step-off")
        approach_ceiling, approach_wall = landmarks(approach)
        bottom_ceiling, bottom_wall = landmarks(bottom)
        board_ceiling, board_wall = landmarks(board)
        late_ceiling, late_wall = landmarks(late)
        top_ceiling, top_wall = landmarks(top)
        game.check(f"{cycle}-activation-lowers-visible-platform",
                   bottom_wall is not None and approach_wall is not None and bottom_wall < approach_wall)
        game.check(f"{cycle}-stationary-bottom-wait", board.sha256 == wait.sha256)
        game.check(f"{cycle}-automatic-rise-changes-ceiling-not-floor-relative-to-camera",
                   top_ceiling is not None and board_ceiling is None and late_ceiling is not None
                   and late_ceiling < top_ceiling and top_wall == late_wall == board_wall)
        game.check(f"{cycle}-destination-stable-across-native-frames", top.sha256 == stable.sha256)
        estimate = None
        if top_ceiling is not None and top_wall is not None:
            horizon = 175 * top.height / 400
            if top_wall + 1 > horizon:
                estimate = 128 - 41 * (top_wall - top_ceiling) / (top_wall + 1 - horizon)
        game.check(f"{cycle}-destination-height-from-native-ceiling-floor-geometry",
                   estimate is not None and abs(estimate - 64) <= 2,
                   observedHeight=estimate, expectedHeight=64, tolerance=2)
        if (cleared.width, cleared.height) != (width, height):
            raise RuntimeError("Native reference/cycle frame dimensions differ.")
        changed = sum(cleared.rgb[i:i + 3] != reference_rgb[i:i + 3] for i in range(0, len(reference_rgb), 3))
        difference = changed / (width * height)
        game.check(f"{cycle}-step-off-matches-qualified-cleared-portal-native-pose",
                   difference <= .001, changedPixelFraction=difference, maximum=.001,
                   referencePublicX=reference["finalPosition"][0], unchangedExpectedX="<=-32")
        if not all(check["passed"] for check in game.metadata["assertions"]):
            game.quit()
            game.metadata["completed"] = True
            game.metadata["outcome"] = "failed-before-further-retrigger"
            raise SystemExit(1)
    game.quit()
    game.check("final-step-off-clears-portal-radius", game.metadata["finalPosition"][0] <= -32,
               observedX=game.metadata["finalPosition"][0], unchangedExpectedX="<=-32")
    passed = all(check["passed"] for check in game.metadata["assertions"])
    game.metadata["completed"] = True
    game.metadata["fullLiftCycleVerified"] = passed
    game.metadata["outcome"] = "passed-two-full-cycles" if passed else "failed"
    if not passed:
        raise SystemExit(1)
