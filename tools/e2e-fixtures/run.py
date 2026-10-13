"""Drive the actual game process through its CLI and native X11 input."""

import argparse
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument("executable", type=Path)
parser.add_argument("--native", action="store_true")
parser.add_argument("--only-native", action="append", help="Rerun one named native scenario, retaining other results for the same binary")
args = parser.parse_args()
root = Path(__file__).resolve().parent
executable = args.executable.resolve()
evidence = root / "e2e-evidence"
evidence.mkdir(exist_ok=True)
results = []
selected = set(args.only_native or [])
if args.only_native:
    previous = json.loads((evidence / "results.json").read_text())
    if previous["executableSha256"] != hashlib.sha256(executable.read_bytes()).hexdigest():
        raise RuntimeError("Cannot retain E2E results from a different executable")
    results = [row for row in previous["results"] if row["scenario"] not in args.only_native]
scenarios = [
    "portal-equal", "step-24", "step-25", "clearance-56", "clearance-55",
    "overlap-55", "closed-portal", "flag-blocked", "masked-passable",
    "masked-blocked", "sky-portal", "blue-door", "lift-use-repeat",
    "e2e-key-door-combat-exit", "e2e-wrong-key",
]


def game_command(name, frames, output):
    return [
        str(executable), "--wad", str(root / "generated" / (name + ".wad")),
        "--map", "E1M1", "--no-audio", "--frames", str(frames),
        "--output", str(output),
    ]


def complete_frame(path):
    if not path.is_file():
        return False
    data = path.read_bytes()
    header = b"P6\n640 400\n255\n"
    return data.startswith(header) and len(data) == len(header) + 640 * 400 * 3


def capture_process(name, command, expect_rejection=False):
    frame = evidence / (name + ".ppm")
    frame.unlink(missing_ok=True)
    try:
        process = subprocess.run(command, capture_output=True, text=True, timeout=10)
        log = process.stdout + process.stderr
        (evidence / (name + ".log")).write_text(log)
        if expect_rejection:
            passed = process.returncode > 0 and bool(log.strip()) and not any(
                term in log.lower() for term in ("stack overflow", "segmentation fault")
            )
            status = "explicit-process-rejection" if passed else (
                "accepted-malformed-input" if process.returncode == 0 else "abnormal-or-unexplained-failure"
            )
        else:
            passed = process.returncode == 0 and complete_frame(frame)
            status = "loaded-simulated-exported-frame" if passed else "process-or-frame-failure"
        results.append({"scenario": name, "returncode": process.returncode, "status": status})
        print(f"{name}: {status}")
    except subprocess.TimeoutExpired:
        results.append({"scenario": name, "status": "process-timeout"})
        print(f"{name}: process-timeout")


if not args.only_native:
    for scenario in scenarios:
        capture_process(scenario, game_command(scenario, 35, evidence / (scenario + ".ppm")) + ["--headless"])
    for scenario in sorted(p.stem for p in (root / "generated").glob("*.wad") if p.stem.startswith("bad-") or p.stem == "cyclic-node"):
        capture_process(scenario, game_command(scenario, 1, evidence / (scenario + ".ppm")) + ["--headless"], True)


if args.native or args.only_native:
    x11 = C.CDLL("libX11.so.6")
    xtst = C.CDLL("libXtst.so.6")
    signatures = {
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
    }
    for name, (argtypes, restype) in signatures.items():
        function = getattr(x11, name)
        function.argtypes, function.restype = argtypes, restype
    xtst.XTestFakeKeyEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
    xtst.XTestFakeKeyEvent.restype = C.c_int

    xvfb = subprocess.Popen(
        ["Xvfb", "-displayfd", "1", "-screen", "0", "1024x768x24", "-nolisten", "tcp"],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
    )
    display = None
    try:
        display_number = xvfb.stdout.readline().strip()
        if not display_number.isdecimal():
            raise RuntimeError("Xvfb did not allocate an isolated display")
        env = dict(os.environ, DISPLAY=":" + display_number)
        display = x11.XOpenDisplay(env["DISPLAY"].encode())
        if not display:
            raise RuntimeError("Cannot open allocated Xvfb display")

        def find_window():
            window_root = x11.XDefaultRootWindow(display)
            root_return, parent = C.c_ulong(), C.c_ulong()
            children, count = C.POINTER(C.c_ulong)(), C.c_uint()
            if not x11.XQueryTree(display, window_root, C.byref(root_return), C.byref(parent), C.byref(children), C.byref(count)):
                return None
            found = None
            try:
                for index in range(count.value):
                    name_ptr = C.c_void_p()
                    if x11.XFetchName(display, children[index], C.byref(name_ptr)) and name_ptr.value:
                        title = C.string_at(name_ptr).decode(errors="replace")
                        x11.XFree(name_ptr)
                        if "doomsharp" in title.lower():
                            found = int(children[index])
                            break
            finally:
                if children:
                    x11.XFree(children)
            return found

        def key(name, down):
            code = x11.XKeysymToKeycode(display, x11.XStringToKeysym(name.encode()))
            if code == 0 or not xtst.XTestFakeKeyEvent(display, code, int(down), 0):
                raise RuntimeError("Cannot inject native key " + name)
            x11.XFlush(display)

        def drive(name, actions, label):
            if args.only_native and label not in args.only_native:
                return
            selected.discard(label)
            frame = evidence / (label + ".ppm")
            frame.unlink(missing_ok=True)
            log_file = evidence / (label + ".log")
            events = []
            with log_file.open("w") as log:
                process = subprocess.Popen(game_command(name, 1000, frame), stdout=log, stderr=log, env=env)
                try:
                    deadline = time.monotonic() + 5
                    window = None
                    while time.monotonic() < deadline and process.poll() is None:
                        window = find_window()
                        if window:
                            break
                        time.sleep(0.02)
                    if not window:
                        raise RuntimeError("Actual game native window did not appear")
                    x11.XSetInputFocus(display, window, 2, 0)
                    x11.XFlush(display)
                    time.sleep(0.12)
                    for keys, duration in actions:
                        if process.poll() is not None:
                            raise RuntimeError("Game exited before native input sequence completed")
                        events.append({"keys": keys, "holdSeconds": duration})
                        for name_key in keys:
                            key(name_key, True)
                        time.sleep(duration)
                        for name_key in keys:
                            key(name_key, False)
                        time.sleep(0.08)
                    key("Escape", True)
                    time.sleep(0.08)
                    key("Escape", False)
                    process.wait(timeout=5)
                finally:
                    if process.poll() is None:
                        process.terminate()
                        process.wait(timeout=5)
            log_text = log_file.read_text()
            summary = next((line for line in log_text.splitlines() if line.startswith("Rendered ")), "")
            position = re.search(r"position=\((-?[\d.]+),(-?[\d.]+)\)", summary)
            final_position = [float(n) for n in position.groups()] if position else None
            outcome = "OBSERVATION"
            if label.startswith("native-") and name in (
                "portal-equal", "step-24", "clearance-56", "masked-passable",
                "step-25", "clearance-55", "overlap-55", "closed-portal",
                "flag-blocked", "masked-blocked", "blue-door", "e2e-wrong-key",
            ):
                must_cross = name in ("portal-equal", "step-24", "clearance-56", "masked-passable")
                passed = bool(final_position) and (final_position[0] > 0 if must_cross else final_position[0] <= -16)
                outcome = "PASS" if passed else "FAIL"
            if label == "native-key-door-combat-exit":
                health = re.search(r"health=(\d+)", summary)
                outcome = "PASS" if health and int(health.group(1)) > 0 and "shotgun=True" in summary and "kills=1" in summary and "finished=True" in summary else "FAIL"
            if label == "native-map-advance":
                outcome = "PASS" if final_position == [-64.0, 0.0] and "finished=False" in summary and "shotgun=True" in summary and "kills=0" in summary and "health=100" not in summary else "FAIL"
            if label == "native-deathless-restart":
                outcome = "PASS" if "health=100" in summary and "kills=0" in summary and "shotgun=False" in summary and "finished=False" in summary and final_position == [-96.0, 0.0] else "FAIL"
            results.append({
                "scenario": label, "fixture": name, "returncode": process.returncode,
                "inputPath": "XTest native key events to actual X11 game window",
                "inputSequence": events, "summary": summary,
                "finalPosition": final_position, "acceptance": outcome,
                "status": "native-input-process-and-frame-observed" if process.returncode == 0 and complete_frame(frame) else "native-process-failure",
            })
            print(label + ": " + outcome + "; " + summary)

        for name in ("portal-equal", "step-24", "step-25", "clearance-56", "clearance-55", "overlap-55", "closed-portal", "flag-blocked", "masked-passable", "masked-blocked"):
            drive(name, [(["w"], 1.0)], "native-" + name)
        drive("blue-door", [(["w"], 0.3), (["e"], 0.1), (["w"], 1.0)], "native-door-without-key")
        drive("e2e-wrong-key", [(["w"], 0.8), (["e"], 0.1), (["w"], 1.0)], "native-door-wrong-key")
        drive("lift-use-repeat", [(["w"], 0.3), (["e"], 0.1), (["w"], 2.5)], "native-lift-board")
        encounter = [
            (["w"], 0.8), (["e"], 0.1), (["Control_L"], 2.0),
            (["w", "Control_L"], 1.0), (["w"], 0.7), (["e"], 0.1),
        ]
        drive("e2e-key-door-combat-exit", encounter, "native-key-door-combat-exit")
        drive("e2e-key-door-combat-exit", encounter + [([], 0.3), (["e"], 0.1)], "native-map-advance")
        drive("e2e-key-door-combat-exit", encounter + [(["r"], 0.1)], "native-deathless-restart")
    finally:
        if display:
            x11.XCloseDisplay(display)
        xvfb.terminate()
        xvfb.wait(timeout=5)

if selected:
    raise RuntimeError("Unknown native scenario selection: " + ", ".join(sorted(selected)))
for row in results:
    if row["status"] == "accepted-or-abnormal-failure" and row["returncode"] == 0:
        row["status"] = "accepted-malformed-input"
(evidence / "results.json").write_text(json.dumps({
    "format": "doomsharp-full-executable-e2e-v2",
    "executable": str(executable),
    "executableSha256": hashlib.sha256(executable.read_bytes()).hexdigest(),
    "results": results,
    "limitations": "No engine internals invoked. Native input is synthetic XTest/Xvfb, not physical keyboard, audible audio or original-game parity. Visual correctness and exact original timing still require comparison.",
}, indent=2) + "\n")
if any(row.get("acceptance") == "FAIL" or row["status"] in (
    "process-or-frame-failure", "process-timeout", "native-process-failure",
    "abnormal-or-unexplained-failure",
) for row in results):
    raise SystemExit(1)
