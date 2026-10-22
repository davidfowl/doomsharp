import { open } from "./browser/DoomSharp.NetWasm.mjs";
import * as host from "./host.js";

const status = document.querySelector("#status");
const controls = ["restart", "wad", "save", "load", "benchmark"].map(id => document.getElementById(id));
const app = await open();
app.failure.then(error => { if (error) status.textContent = `NetWasm stopped: ${error.message}`; });

const bridge = {
  async invokeMethodAsync(method, ...args) {
    switch (method) {
      case "Frame": {
        const pixels = app.frame(...args);
        for (let sound = app.nextSound(); sound.length; sound = app.nextSound())
          host.playSound(sound, 22050);
        const music = app.nextMusic();
        if (music !== "\0") {
          if (music) host.playMusic(music, app.musicBytes(), 22050);
          else host.stopMusic();
        }
        return pixels;
      }
      case "Key": return app.key(...args);
      case "ResetInput": return app.resetInput();
      case "HostError": status.textContent = args[0]; return;
      case "Benchmark": return app.benchmark(...args);
      default: throw new Error(`Unknown host method: ${method}`);
    }
  },
};

host.initialize(bridge);

async function load(bytes) {
  controls.forEach(control => control.disabled = true);
  status.textContent = "Loading WAD...";
  try {
    host.stopMusic();
    const map = app.start(bytes, document.querySelector("#map").value);
    host.start();
    status.textContent = `Playing ${map}. Click the game to enable sound and mouse capture.`;
  } catch (error) {
    status.textContent = `Cannot load WAD: ${error.message}`;
    console.error(error);
  } finally {
    controls.forEach(control => control.disabled = false);
  }
}

async function freedoom() {
  try {
    const response = await fetch("../assets/freedoom/freedoom1.wad");
    if (!response.ok) throw new Error(`Freedoom download failed: HTTP ${response.status}`);
    await load(new Uint8Array(await response.arrayBuffer()));
  } catch (error) {
    status.textContent = `Cannot load WAD: ${error.message}`;
    console.error(error);
  }
}

document.querySelector("#restart").addEventListener("click", freedoom);
document.querySelector("#wad").addEventListener("change", async event => {
  const file = event.target.files[0];
  if (!file) return;
  if (file.size > 64 * 1024 * 1024) {
    status.textContent = "Cannot load WAD: file exceeds 64 MiB limit.";
    return;
  }
  try { await load(new Uint8Array(await file.arrayBuffer())); }
  catch (error) { status.textContent = `Cannot load WAD: ${error.message}`; console.error(error); }
});
document.querySelector("#mute").addEventListener("change", event => {
  app.mute(event.target.checked);
  if (event.target.checked) host.stopMusic();
});
for (const [id, key] of [["save", "F5"], ["load", "F9"]])
  document.getElementById(id).addEventListener("click", () => {
    app.key(key, true);
    app.key(key, false);
  });
document.querySelector("#benchmark").addEventListener("click", () => {
  const start = performance.now();
  const checksum = app.benchmark(120);
  const milliseconds = performance.now() - start;
  status.textContent = `120 software renders: ${milliseconds.toFixed(1)} ms (checksum ${checksum}). Reload to benchmark the initial scene.`;
});

await freedoom();
