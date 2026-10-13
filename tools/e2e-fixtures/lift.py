"""Reproduce lift carry/step-off through the native executable, not engine APIs."""

import argparse
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import re
import select
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument("executable", type=Path)
parser.add_argument("--source-commit", required=True,
                    help="Full source revision used to publish the supplied executable")
parser.add_argument("--output-dir", type=Path,
                    help="New evidence directory; existing directories are never overwritten")
args = parser.parse_args()
if not re.fullmatch(r"[0-9a-fA-F]{40}", args.source_commit):
    parser.error("--source-commit requires a full 40-character revision")
root = Path(__file__).resolve().parent
game = args.executable.resolve()
wad = root / "generated" / "lift-use-repeat.wad"
fixture_hash = hashlib.sha256(wad.read_bytes()).hexdigest()
if fixture_hash != "e86d622806c6bf68ad3ba1facfeecd312fe04d27ea1b0dfdd4fff288b6297525":
    parser.error("Unexpected lift fixture SHA256; regenerate the documented original fixture")
output = args.output_dir or root / "e2e-evidence" / ("lift-" + str(time.time_ns()))
output = output.resolve()
output.mkdir(parents=True, exist_ok=False)
events, phases = [], []
started = time.monotonic()
metadata = {
    "sourceCommit": args.source_commit.lower(),
    "sourceCommitProvenance": "Caller-supplied build revision, not inferred from executable",
    "runnerSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    "executableSha256": hashlib.sha256(game.read_bytes()).hexdigest(),
    "fixtureSha256": fixture_hash,
    "command": [str(game), "--wad", str(wad), "--map", "E1M1", "--no-audio",
                "--frames", "5000", "--output", str(output / "final.ppm")],
    "nativeEvents": events, "phases": phases, "assertions": [],
    "completed": False, "fullLiftCycleVerified": False,
    "limitations": "Fixture-specific native framebuffer geometry and wall-clock inputs; "
                    "not original timing parity, exact bottom-height or retrigger acceptance. "
                    "No Tab, engine APIs, forced state, audio, physical input or Windows/macOS.",
}
(output / "result.json").write_text(json.dumps(metadata, indent=2) + "\n")
print("Evidence:", output, flush=True)
x11, xtst = C.CDLL("libX11.so.6"), C.CDLL("libXtst.so.6")


class Image(C.Structure):
    _fields_ = [
        ("width", C.c_int), ("height", C.c_int), ("xoffset", C.c_int),
        ("format", C.c_int), ("data", C.c_void_p), ("byte_order", C.c_int),
        ("bitmap_unit", C.c_int), ("bitmap_bit_order", C.c_int),
        ("bitmap_pad", C.c_int), ("depth", C.c_int), ("bytes_per_line", C.c_int),
        ("bits_per_pixel", C.c_int), ("red_mask", C.c_ulong),
        ("green_mask", C.c_ulong), ("blue_mask", C.c_ulong), ("obdata", C.c_void_p),
    ]


definitions = {
    "XOpenDisplay": ([C.c_char_p], C.c_void_p),
    "XDefaultRootWindow": ([C.c_void_p], C.c_ulong),
    "XQueryTree": ([C.c_void_p, C.c_ulong, C.POINTER(C.c_ulong), C.POINTER(C.c_ulong), C.POINTER(C.POINTER(C.c_ulong)), C.POINTER(C.c_uint)], C.c_int),
    "XFetchName": ([C.c_void_p, C.c_ulong, C.POINTER(C.c_void_p)], C.c_int),
    "XFree": ([C.c_void_p], C.c_int),
    "XSetInputFocus": ([C.c_void_p, C.c_ulong, C.c_int, C.c_ulong], C.c_int),
    "XStringToKeysym": ([C.c_char_p], C.c_ulong),
    "XKeysymToKeycode": ([C.c_void_p, C.c_ulong], C.c_uint),
    "XFlush": ([C.c_void_p], C.c_int),
    "XCloseDisplay": ([C.c_void_p], C.c_int),
    "XGetGeometry": ([C.c_void_p, C.c_ulong, C.POINTER(C.c_ulong), C.POINTER(C.c_int), C.POINTER(C.c_int), C.POINTER(C.c_uint), C.POINTER(C.c_uint), C.POINTER(C.c_uint), C.POINTER(C.c_uint)], C.c_int),
    "XGetImage": ([C.c_void_p, C.c_ulong, C.c_int, C.c_int, C.c_uint, C.c_uint, C.c_ulong, C.c_int], C.POINTER(Image)),
    "XDestroyImage": ([C.POINTER(Image)], C.c_int),
}
for name, (argtypes, restype) in definitions.items():
    getattr(x11, name).argtypes, getattr(x11, name).restype = argtypes, restype
xtst.XTestFakeKeyEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
xtst.XTestFakeKeyEvent.restype = C.c_int
xvfb = subprocess.Popen(["Xvfb", "-displayfd", "1", "-screen", "0", "1024x768x24", "-nolisten", "tcp"],
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
display, process = None, None
try:
    if not select.select([xvfb.stdout], [], [], 5)[0]:
        raise RuntimeError("Xvfb did not allocate a display within five seconds")
    number = xvfb.stdout.readline().strip()
    if not number.isdecimal():
        raise RuntimeError("Xvfb did not allocate an isolated display")
    env = dict(os.environ, DISPLAY=":" + number)
    display = x11.XOpenDisplay(env["DISPLAY"].encode())
    if not display:
        raise RuntimeError("Cannot open the isolated X11 display")
    log = (output / "game.log").open("w")
    process = subprocess.Popen(metadata["command"], stdout=log, stderr=log, env=env)

    def find_window():
        parent, owner = C.c_ulong(), C.c_ulong()
        children, count = C.POINTER(C.c_ulong)(), C.c_uint()
        if not x11.XQueryTree(display, x11.XDefaultRootWindow(display), C.byref(owner),
                            C.byref(parent), C.byref(children), C.byref(count)):
            return None
        found = None
        try:
            for index in range(count.value):
                title = C.c_void_p()
                if x11.XFetchName(display, children[index], C.byref(title)) and title.value:
                    name = C.string_at(title).decode(errors="replace")
                    x11.XFree(title)
                    if "doomsharp" in name.lower():
                        found = int(children[index])
                        break
        finally:
            if children:
                x11.XFree(children)
        return found

    deadline, window = time.monotonic() + 5, None
    while time.monotonic() < deadline and process.poll() is None:
        window = find_window()
        if window:
            break
        time.sleep(0.02)
    if not window:
        raise RuntimeError("Actual game window did not appear")
    x11.XSetInputFocus(display, window, 2, 0)
    x11.XFlush(display)
    time.sleep(0.15)

    def key(name, down):
        if down and process.poll() is not None:
            raise RuntimeError("Game exited before completing native input")
        code = x11.XKeysymToKeycode(display, x11.XStringToKeysym(name.encode()))
        if not code or not xtst.XTestFakeKeyEvent(display, code, int(down), 0):
            raise RuntimeError("Native key injection failed: " + name)
        x11.XFlush(display)
        events.append({"seconds": time.monotonic() - started, "key": name, "down": down})

    def hold(name, seconds):
        key(name, True)
        time.sleep(seconds)
        key(name, False)
        time.sleep(0.12)

    def capture(name):
        owner, x, y = C.c_ulong(), C.c_int(), C.c_int()
        width, height, border, depth = C.c_uint(), C.c_uint(), C.c_uint(), C.c_uint()
        if not x11.XGetGeometry(display, window, C.byref(owner), C.byref(x), C.byref(y),
                               C.byref(width), C.byref(height), C.byref(border), C.byref(depth)):
            raise RuntimeError("Cannot read actual window geometry")
        image = x11.XGetImage(display, window, 0, 0, width.value, height.value, C.c_ulong(-1), 2)
        if not image:
            raise RuntimeError("Native framebuffer readback failed")
        try:
            info = image.contents
            if (info.bits_per_pixel, info.byte_order, info.red_mask, info.green_mask, info.blue_mask) != (32, 0, 0xff0000, 0xff00, 0xff):
                raise RuntimeError("Unsupported native image pixel format")
            raw = C.string_at(info.data, info.bytes_per_line * info.height)
            packed = b"".join(raw[row * info.bytes_per_line:row * info.bytes_per_line + info.width * 4] for row in range(info.height))
            rgb = bytearray(info.width * info.height * 3)
            rgb[0::3], rgb[1::3], rgb[2::3] = packed[2::4], packed[1::4], packed[0::4]
            data = f"P6\n{info.width} {info.height}\n255\n".encode() + rgb
            path = output / (name + ".ppm")
            path.write_bytes(data)
            # The original fixture's uniform ceiling is gray100; nearby checker
            # walls are gray64/128 and the floor is gray32/64, before shading.
            column = int(info.width * 0.55)
            ceiling_end = None
            for row in range(int(info.height * 0.875)):
                offset = (row * info.width + column) * 3
                color = rgb[offset:offset + 3]
                if color[0] == color[1] == color[2] and 60 <= color[0] <= 78:
                    ceiling_end = row
                else:
                    break
            wall_end = None
            for row in range(int(info.height * 0.875)):
                for col in range(info.width // 2 - 80, info.width // 2 + 81, 4):
                    offset = (row * info.width + col) * 3
                    color = rgb[offset:offset + 3]
                    if color[0] == color[1] == color[2] and 80 <= color[0] <= 100:
                        wall_end = row
                        break
            phases.append({"name": name, "seconds": time.monotonic() - started,
                           "width": info.width, "height": info.height,
                           "ceilingBoundaryY": ceiling_end, "wallBottomY": wall_end,
                           "sha256": hashlib.sha256(data).hexdigest()})
            print(name, "ceiling-boundary", ceiling_end, "wall-bottom", wall_end, flush=True)
        finally:
            x11.XDestroyImage(image)

    def until(activation, seconds):
        delay = activation + seconds - time.monotonic()
        if delay > 0:
            time.sleep(delay)

    hold("w", 0.30)
    capture("01-raised-approach")
    first = time.monotonic()
    hold("e", 0.06)
    until(first, 0.40); capture("02-first-descent")
    until(first, 1.15); capture("03-first-bottom")
    hold("w", 0.35); capture("04-first-board")
    until(first, 2.70); capture("05-first-bottom-wait")
    until(first, 4.45); capture("06-first-rise")
    until(first, 4.70); capture("07-first-rise-late")
    until(first, 5.15); capture("08-first-top")
    until(first, 5.50); capture("09-first-top-stable")
    hold("s", 0.50); capture("10-step-off")
    hold("s", 2.0); capture("11-prolonged-step-off")
    hold("Escape", 0.05)
    process.wait(timeout=5)
    log.close()
    text = (output / "game.log").read_text()
    position = re.search(r"position=\((-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)\)", text)
    metadata["returncode"] = process.returncode
    metadata["finalPosition"] = [float(value) for value in position.groups()] if position else None
    metadata["gameLogSha256"] = hashlib.sha256((output / "game.log").read_bytes()).hexdigest()
    if process.returncode != 0 or not position:
        raise RuntimeError("Actual game did not produce successful ordinary output")
    final_frame = (output / "final.ppm").read_bytes()
    header = b"P6\n640 400\n255\n"
    if not final_frame.startswith(header) or len(final_frame) != len(header) + 640 * 400 * 3:
        raise RuntimeError("Actual game did not export a complete final P6 frame")
    metadata["finalFrameSha256"] = hashlib.sha256(final_frame).hexdigest()
    phase_lookup = {phase["name"]: phase for phase in phases}
    board = phase_lookup["04-first-board"]
    wait = phase_lookup["05-first-bottom-wait"]
    top = phase_lookup["08-first-top"]
    stable = phase_lookup["09-first-top-stable"]
    late = phase_lookup["07-first-rise-late"]
    checks = [
        {"name": "activation-lowers-visible-platform",
         "passed": phase_lookup["03-first-bottom"]["wallBottomY"] is not None
         and phase_lookup["01-raised-approach"]["wallBottomY"] is not None
         and phase_lookup["03-first-bottom"]["wallBottomY"]
         < phase_lookup["01-raised-approach"]["wallBottomY"]},
        {"name": "stationary-bottom-wait", "passed": board["sha256"] == wait["sha256"]},
        {"name": "automatic-rise-changes-ceiling-not-floor-relative-to-camera",
         "passed": top["ceilingBoundaryY"] is not None and board["ceilingBoundaryY"] is None
         and late["ceilingBoundaryY"] is not None
         and late["ceilingBoundaryY"] < top["ceilingBoundaryY"]
         and top["wallBottomY"] == late["wallBottomY"] == board["wallBottomY"]},
        {"name": "destination-stable-across-native-frames",
         "passed": top["sha256"] == stable["sha256"]},
    ]
    estimate = None
    if top["ceilingBoundaryY"] is not None and top["wallBottomY"] is not None:
        scale = top["height"] / 400
        horizon = 175 * scale
        ceiling_edge = top["ceilingBoundaryY"] + 1
        floor_edge = top["wallBottomY"] + 1
        # Eliminate camera distance from perspective using the resting eye
        # offset41 and ceiling128; this is not a displacement inferred from XY.
        if floor_edge > horizon:
            estimate = 128 - 41 * (floor_edge - ceiling_edge) / (floor_edge - horizon)
    checks.append({"name": "destination-height-from-native-ceiling-floor-geometry",
                   "observedHeight": estimate, "expectedHeight": 64,
                   "tolerance": 2, "passed": estimate is not None and abs(estimate - 64) <= 2})
    checks.append({"name": "step-off-clears-portal-radius-after-prolonged-backward-input",
                   "observedX": float(position.group(1)), "expectedX": "<=-32",
                   "passed": float(position.group(1)) <= -32})
    metadata["assertions"] = checks
    metadata["completed"] = True
    metadata["outcome"] = "passed-carry-and-step-off-only" if all(c["passed"] for c in checks) else "failed"
    (output / "result.json").write_text(json.dumps(metadata, indent=2) + "\n")
    for check in checks:
        print(check["name"], "PASS" if check["passed"] else "FAIL", flush=True)
    if any(not check["passed"] for check in checks):
        raise SystemExit(1)
except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
    metadata["error"] = str(error)
    raise
finally:
    (output / "result.json").write_text(json.dumps(metadata, indent=2) + "\n")
    if process and process.poll() is None:
        process.terminate()
        process.wait(timeout=5)
    if display:
        x11.XCloseDisplay(display)
    xvfb.terminate()
    xvfb.wait(timeout=5)
