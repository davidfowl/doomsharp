"""Development-only public executable/X11 input and framebuffer orchestration."""
import ctypes as c
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path
import re
import select
import subprocess
import time


class Image(c.Structure):
    _fields_ = [
        ("width", c.c_int), ("height", c.c_int), ("xoffset", c.c_int),
        ("format", c.c_int), ("data", c.c_void_p), ("byte_order", c.c_int),
        ("bitmap_unit", c.c_int), ("bitmap_bit_order", c.c_int),
        ("bitmap_pad", c.c_int), ("depth", c.c_int), ("bytes_per_line", c.c_int),
        ("bits_per_pixel", c.c_int), ("red_mask", c.c_ulong),
        ("green_mask", c.c_ulong), ("blue_mask", c.c_ulong), ("obdata", c.c_void_p),
    ]


@dataclass
class Frame:
    width: int
    height: int
    rgb: bytes
    sha256: str

    def pixel(self, x, y):
        offset = (y * self.width + x) * 3
        return self.rgb[offset:offset + 3]


class NativeGame:
    def __init__(self, executable, wad, source, output):
        if not re.fullmatch(r"[0-9a-f]{40}", source):
            raise ValueError("Supply the full source revision used to publish the executable.")
        self.executable, self.wad = Path(executable).resolve(), Path(wad).resolve()
        self.output = Path(output).resolve()
        self.output.mkdir(parents=True, exist_ok=False)
        self.started = time.monotonic()
        self.display = self.process = self.xvfb = self.log = None
        self.metadata = {
            "sourceCommit": source, "sourceProvenance": "Caller-supplied build revision",
            "executableSha256": hashlib.sha256(self.executable.read_bytes()).hexdigest(),
            "fixtureSha256": hashlib.sha256(self.wad.read_bytes()).hexdigest(),
            "command": [str(self.executable), "--wad", str(self.wad), "--map", "E1M1",
                        "--no-audio", "--frames", "100000", "--output", str(self.output / "final.ppm")],
            "events": [], "frames": [], "assertions": [], "completed": False,
            "limits": "Actual executable with virtual X11 input/framebuffer; no engine APIs, "
                      "state injection, physical input, audio or original campaign/timing oracle.",
        }

    def __enter__(self):
        try:
            self.x, self.t = c.CDLL("libX11.so.6"), c.CDLL("libXtst.so.6")
            definitions = {
                "XOpenDisplay": ([c.c_char_p], c.c_void_p),
                "XDefaultRootWindow": ([c.c_void_p], c.c_ulong),
                "XQueryTree": ([c.c_void_p, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_ulong),
                               c.POINTER(c.POINTER(c.c_ulong)), c.POINTER(c.c_uint)], c.c_int),
                "XFetchName": ([c.c_void_p, c.c_ulong, c.POINTER(c.c_void_p)], c.c_int),
                "XFree": ([c.c_void_p], c.c_int),
                "XSetInputFocus": ([c.c_void_p, c.c_ulong, c.c_int, c.c_ulong], c.c_int),
                "XStringToKeysym": ([c.c_char_p], c.c_ulong),
                "XKeysymToKeycode": ([c.c_void_p, c.c_ulong], c.c_uint),
                "XKeycodeToKeysym": ([c.c_void_p, c.c_ubyte, c.c_int], c.c_ulong),
                "XFlush": ([c.c_void_p], c.c_int),
                "XCloseDisplay": ([c.c_void_p], c.c_int),
                "XGetGeometry": ([c.c_void_p, c.c_ulong, c.POINTER(c.c_ulong),
                                 c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_uint),
                                 c.POINTER(c.c_uint), c.POINTER(c.c_uint), c.POINTER(c.c_uint)], c.c_int),
                "XGetImage": ([c.c_void_p, c.c_ulong, c.c_int, c.c_int, c.c_uint, c.c_uint,
                              c.c_ulong, c.c_int], c.POINTER(Image)),
                "XDestroyImage": ([c.POINTER(Image)], c.c_int),
            }
            for name, (parameters, result) in definitions.items():
                getattr(self.x, name).argtypes, getattr(self.x, name).restype = parameters, result
            self.t.XTestFakeKeyEvent.argtypes = [c.c_void_p, c.c_uint, c.c_int, c.c_ulong]
            self.t.XTestFakeKeyEvent.restype = c.c_int
            self.xvfb = subprocess.Popen(
                ["Xvfb", "-displayfd", "1", "-screen", "0", "1024x768x24", "-nolisten", "tcp"],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            if not select.select([self.xvfb.stdout], [], [], 5)[0]:
                raise RuntimeError("Xvfb did not allocate a display.")
            number = self.xvfb.stdout.readline().strip()
            if not number.isdecimal():
                raise RuntimeError("Invalid Xvfb display.")
            environment = dict(os.environ, DISPLAY=":" + number)
            self.display = self.x.XOpenDisplay(environment["DISPLAY"].encode())
            if not self.display:
                raise RuntimeError("Cannot open isolated X11 display.")
            self.log = (self.output / "game.log").open("w")
            self.process = subprocess.Popen(self.metadata["command"], stdout=self.log, stderr=self.log, env=environment)
            deadline, self.window = time.monotonic() + 10, None
            while time.monotonic() < deadline and self.process.poll() is None:
                self.window = self.find_window()
                if self.window:
                    break
                time.sleep(.02)
            if not self.window:
                raise RuntimeError("Actual game window did not appear.")
            self.x.XSetInputFocus(self.display, self.window, 2, 0)
            self.x.XFlush(self.display)
            time.sleep(.15)
            return self
        except (OSError, RuntimeError, subprocess.TimeoutExpired):
            self.close()
            raise

    def find_window(self):
        root, parent, count = c.c_ulong(), c.c_ulong(), c.c_uint()
        children = c.POINTER(c.c_ulong)()
        if not self.x.XQueryTree(self.display, self.x.XDefaultRootWindow(self.display), c.byref(root),
                                c.byref(parent), c.byref(children), c.byref(count)):
            raise RuntimeError("XQueryTree failed.")
        try:
            for index in range(count.value):
                name = c.c_void_p()
                if self.x.XFetchName(self.display, children[index], c.byref(name)) and name.value:
                    title = c.string_at(name)
                    self.x.XFree(name)
                    if title.startswith(b"DoomSharp"):
                        return children[index]
        finally:
            if children:
                self.x.XFree(children)
        return None

    def key(self, name, down):
        symbol = self.x.XStringToKeysym(name.encode())
        code = self.x.XKeysymToKeycode(self.display, symbol)
        actual = self.x.XKeycodeToKeysym(self.display, code, 0)
        if not code or actual != symbol or not self.t.XTestFakeKeyEvent(self.display, code, int(down), 0):
            raise RuntimeError(f"Native key/server mapping failed: {name} ({symbol:#x}/{actual:#x}).")
        self.x.XFlush(self.display)
        self.metadata["events"].append({"seconds": time.monotonic() - self.started,
                                        "key": name, "keysym": symbol, "keycode": code, "down": down})

    def hold(self, name, seconds):
        self.key(name, True)
        time.sleep(seconds)
        self.key(name, False)
        time.sleep(.12)

    def capture(self, name):
        root, x, y = c.c_ulong(), c.c_int(), c.c_int()
        width, height, border, depth = c.c_uint(), c.c_uint(), c.c_uint(), c.c_uint()
        if not self.x.XGetGeometry(self.display, self.window, c.byref(root), c.byref(x), c.byref(y),
                                  c.byref(width), c.byref(height), c.byref(border), c.byref(depth)):
            raise RuntimeError("Cannot read game window geometry.")
        image = self.x.XGetImage(self.display, self.window, 0, 0, width.value, height.value, c.c_ulong(-1), 2)
        if not image:
            raise RuntimeError("Cannot capture actual native framebuffer.")
        try:
            info = image.contents
            if (info.bits_per_pixel, info.byte_order, info.red_mask, info.green_mask, info.blue_mask) != (32, 0, 0xff0000, 0xff00, 0xff):
                raise RuntimeError("Unsupported native framebuffer layout.")
            raw = c.string_at(info.data, info.bytes_per_line * info.height)
            packed = b"".join(raw[row * info.bytes_per_line:row * info.bytes_per_line + info.width * 4]
                              for row in range(info.height))
            rgb = bytearray(info.width * info.height * 3)
            rgb[0::3], rgb[1::3], rgb[2::3] = packed[2::4], packed[1::4], packed[0::4]
            data = f"P6\n{info.width} {info.height}\n255\n".encode() + rgb
            (self.output / (name + ".ppm")).write_bytes(data)
            digest = hashlib.sha256(data).hexdigest()
            self.metadata["frames"].append({"name": name, "seconds": time.monotonic() - self.started,
                                            "width": info.width, "height": info.height, "sha256": digest})
            return Frame(info.width, info.height, bytes(rgb), digest)
        finally:
            self.x.XDestroyImage(image)

    def check(self, name, passed, **observations):
        self.metadata["assertions"].append({"name": name, "passed": bool(passed), **observations})
        print(name, "PASS" if passed else "FAIL", observations, flush=True)

    def quit(self):
        self.hold("Escape", .05)
        code = self.process.wait(timeout=10)
        self.log.close()
        self.metadata["returncode"] = code
        text = (self.output / "game.log").read_text()
        print(text, end="")
        position = re.search(r"position=\((-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)\)", text)
        if code != 0 or not position:
            raise RuntimeError("Game did not quit normally with public position output.")
        self.metadata["finalPosition"] = [float(value) for value in position.groups()]
        data = (self.output / "final.ppm").read_bytes()
        header = b"P6\n640 400\n255\n"
        if not data.startswith(header) or len(data) != len(header) + 640 * 400 * 3:
            raise RuntimeError("Incomplete native-game final export.")
        self.metadata["finalFrameSha256"] = hashlib.sha256(data).hexdigest()

    def close(self):
        if self.process and self.process.poll() is None:
            self.process.terminate()
            self.process.wait(timeout=10)
        if self.log and not self.log.closed:
            self.log.close()
        if self.display:
            self.x.XCloseDisplay(self.display)
            self.display = None
        if self.xvfb:
            self.xvfb.terminate()
            self.xvfb.wait(timeout=5)
        (self.output / "result.json").write_text(json.dumps(self.metadata, indent=2) + "\n")

    def __exit__(self, kind, error, traceback):
        if error:
            self.metadata["error"] = str(error)
        self.close()
