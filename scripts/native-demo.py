"""Play the published game through its public CLI and X11 keyboard input."""
import argparse
import ctypes as c
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time

parser = argparse.ArgumentParser()
parser.add_argument("executable", type=Path)
parser.add_argument("--output-dir", type=Path, default=Path("artifacts/native-demo"))
args = parser.parse_args()
executable = args.executable.resolve()
output = args.output_dir.resolve()
output.mkdir(parents=True, exist_ok=True)
config = output / "asound.conf"
config.write_text("pcm.!default { type null }\n")
environment = dict(os.environ, ALSA_CONFIG_PATH=str(config))

x = c.CDLL("libX11.so.6")
t = c.CDLL("libXtst.so.6")
signatures = {
    "XOpenDisplay": ([c.c_char_p], c.c_void_p),
    "XDefaultRootWindow": ([c.c_void_p], c.c_ulong),
    "XQueryTree": ([c.c_void_p, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_ulong),
                    c.POINTER(c.POINTER(c.c_ulong)), c.POINTER(c.c_uint)], c.c_int),
    "XFetchName": ([c.c_void_p, c.c_ulong, c.POINTER(c.c_void_p)], c.c_int),
    "XFree": ([c.c_void_p], c.c_int),
    "XKeysymToKeycode": ([c.c_void_p, c.c_ulong], c.c_ubyte),
    "XSetInputFocus": ([c.c_void_p, c.c_ulong, c.c_int, c.c_ulong], c.c_int),
    "XFlush": ([c.c_void_p], c.c_int),
    "XCloseDisplay": ([c.c_void_p], c.c_int),
}
for name, (parameters, result) in signatures.items():
    function = getattr(x, name)
    function.argtypes, function.restype = parameters, result
t.XTestFakeKeyEvent.argtypes = [c.c_void_p, c.c_uint, c.c_int, c.c_ulong]
t.XTestFakeKeyEvent.restype = c.c_int
display = x.XOpenDisplay(None)
if not display:
    raise RuntimeError("An X11 display is required; run with xvfb-run -a.")
results = []
completed = False


def find_window():
    parent, root, count = c.c_ulong(), c.c_ulong(), c.c_uint()
    children = c.POINTER(c.c_ulong)()
    if not x.XQueryTree(display, x.XDefaultRootWindow(display), c.byref(root),
                        c.byref(parent), c.byref(children), c.byref(count)):
        raise RuntimeError("XQueryTree failed.")
    try:
        for i in range(count.value):
            name = c.c_void_p()
            if x.XFetchName(display, children[i], c.byref(name)) and name.value:
                title = c.string_at(name)
                x.XFree(name)
                if title.startswith(b"DoomSharp"):
                    return children[i]
    finally:
        if children:
            x.XFree(children)
    return None


def key(symbol, seconds=.1):
    code = x.XKeysymToKeycode(display, symbol)
    if not code or not t.XTestFakeKeyEvent(display, code, 1, 0):
        raise RuntimeError(f"Cannot inject native key {symbol}.")
    x.XFlush(display)
    time.sleep(seconds)
    if not t.XTestFakeKeyEvent(display, code, 0, 0):
        raise RuntimeError(f"Cannot release native key {symbol}.")
    x.XFlush(display)
    time.sleep(.1)


def play(name, restart):
    frame, log_path = output / (name + ".ppm"), output / (name + ".log")
    frame.unlink(missing_ok=True)
    with log_path.open("w") as log:
        process = subprocess.Popen([str(executable), "--demo", "--frames", "100000",
                                    "--output", str(frame)], stdout=log, stderr=log, env=environment)
        try:
            deadline, window = time.monotonic() + 15, None
            while not window and time.monotonic() < deadline and process.poll() is None:
                window = find_window()
                time.sleep(.05)
            if not window:
                raise RuntimeError(f"Game window did not appear; see {log_path}.")
            x.XSetInputFocus(display, window, 2, 0)
            x.XFlush(display)
            key(ord("w"), .7)
            key(ord("e"))
            time.sleep(1.6)
            key(ord("w"), .7)
            key(0xffe3, 2.0)
            key(ord("w"), 2.0)
            key(ord("e"))
            if restart:
                key(ord("r"))
            key(0xff1b)
            code = process.wait(timeout=15)
            if code != 0:
                raise RuntimeError(f"Game exited with {code}; see {log_path}.")
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=10)
    text = log_path.read_text()
    print(text, end="")
    summary = next((line for line in text.splitlines() if line.startswith("Rendered ")), "")
    health = re.search(r"health=(\d+)", summary)
    audio = re.search(r"Audio: (\d+) sound events, (\d+) PCM samples submitted", text)
    if not health or not audio or int(audio[1]) < 1 or int(audio[2]) < 1:
        raise RuntimeError(f"Missing game/audio outcome; see {log_path}.")
    if restart:
        accepted = "health=100, kills=0" in summary and "position=(128.00,128.00)" in summary and "shotgun=False, finished=False" in summary
    else:
        accepted = int(health[1]) > 0 and "kills=1" in summary and "shotgun=True, finished=True" in summary and bool(re.search(r"position=\(5\d\d\.", summary))
    pixels = frame.read_bytes()
    header = b"P6\n640 400\n255\n"
    frame_complete = pixels.startswith(header) and len(pixels) == len(header) + 640 * 400 * 3
    results.append({"scenario": name, "summary": summary, "accepted": accepted,
                    "completeFrame": frame_complete, "frameSha256": hashlib.sha256(pixels).hexdigest()})
    if not accepted or not frame_complete:
        raise RuntimeError(f"Game scenario {name} did not reach its expected outcome; see {log_path}.")


try:
    with tempfile.TemporaryDirectory(dir=output) as scratch:
        for name, arguments, diagnostic in [
            ("missing-wad", ["--wad", str(Path(scratch) / "missing.wad"), "--headless"], "DoomSharp:"),
            ("invalid-frame-count", ["--demo", "--headless", "--frames", "0"], "--frames must be positive"),
        ]:
            run = subprocess.run([str(executable), *arguments], capture_output=True, text=True, timeout=15)
            text = run.stdout + run.stderr
            (output / (name + ".log")).write_text(text)
            results.append({"scenario": name, "returncode": run.returncode, "diagnostic": text.strip()})
            if run.returncode != 1 or diagnostic not in text:
                raise RuntimeError(f"Expected public startup rejection for {name}, got {run.returncode}: {text}")
    play("completed-demo", False)
    play("live-restart", True)
    completed = True
    print("NATIVE EXECUTABLE SCENARIOS PASSED: two startup rejections, completed demo and live restart.")
finally:
    (output / "results.json").write_text(json.dumps({
        "executable": str(executable),
        "executableSha256": hashlib.sha256(executable.read_bytes()).hexdigest(),
        "completed": completed,
        "results": results,
        "limits": "Real game with synthetic OS input and ALSA null output; not campaign parity or physical input/audible playback.",
    }, indent=2) + "\n")
    x.XCloseDisplay(display)
