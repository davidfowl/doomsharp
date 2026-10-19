let canvas, context, dotnet, audio, animation, mouseX = 0, active = false, stopped = false;
let activation = 0;
let awaitingMouse = true;
let audioReady = false;
const listeners = [];
const voices = new Set();

function listen(target, name, callback, options) {
    target.addEventListener(name, callback, options);
    listeners.push(() => target.removeEventListener(name, callback, options));
}

export function initialize(reference) {
    dotnet = reference;
    canvas = document.getElementById("game");
    context = canvas.getContext("2d", { alpha: false });
    if (!context) throw new Error("A Canvas 2D context is required.");
    stopped = false;
    listen(canvas, "click", async () => {
        const current = ++activation;
        canvas.focus();
        active = true;
        if (document.pointerLockElement !== canvas) awaitingMouse = true;
        try {
            audio ??= new AudioContext();
            audioReady = false;
            const resume = audio.resume().then(() => { audioReady = true; startMusicVoice(); });
            const lock = canvas.requestPointerLock();
            await Promise.all([resume, lock]);
        } catch (error) {
            if (current === activation)
                await dotnet.invokeMethodAsync("HostError",
                    `Mouse/audio activation failed: ${error.message}. Keyboard controls remain available.`);
            console.error(error);
        }
    });
    listen(canvas, "keydown", event => {
        event.preventDefault();
        if (event.code === "Escape") { pause(); return; }
        if (!event.repeat) dotnet.invokeMethodAsync("Key", event.code, true).catch(fail);
    });
    listen(canvas, "keyup", event => {
        event.preventDefault();
        dotnet.invokeMethodAsync("Key", event.code, false).catch(fail);
    });
    listen(canvas, "wheel", event => {
        if (active) event.preventDefault();
    }, { passive: false });
    listen(canvas, "mousedown", event => {
        if (event.button === 0 && active && document.pointerLockElement === canvas)
            dotnet.invokeMethodAsync("Key", "Mouse0", true).catch(fail);
    });
    listen(window, "mouseup", event => {
        if (event.button === 0) dotnet.invokeMethodAsync("Key", "Mouse0", false).catch(fail);
    });
    listen(document, "mousemove", event => {
        if (active && document.pointerLockElement === canvas && (event.movementX || event.movementY)) {
            // Pointer-lock entry can report the cursor warp as relative motion.
            if (awaitingMouse) { awaitingMouse = false; return; }
            mouseX += event.movementX;
        }
    });
    listen(document, "pointerlockchange", () => {
        if (document.pointerLockElement !== canvas) pause();
    });
    listen(window, "blur", pause);
    listen(canvas, "blur", pause);
    listen(document, "visibilitychange", () => { if (document.hidden) pause(); });
    animation = requestAnimationFrame(frame);
}

export function start() {
    activation++;
    mouseX = 0;
    awaitingMouse = true;
    active = !document.hidden;
    if (active) canvas.focus({ preventScroll: true });
}

function pause() {
    activation++;
    active = false;
    mouseX = 0;
    awaitingMouse = true;
    if (document.pointerLockElement === canvas) document.exitPointerLock();
    for (const voice of voices) voice.stop();
    voices.clear();
    if (musicVoice) { musicVoice.stop(); musicVoice.disconnect(); musicVoice = null; }
    dotnet.invokeMethodAsync("ResetInput").catch(fail);
}

function fail(error) {
    stopped = true;
    dotnet.invokeMethodAsync("HostError", `Game stopped: ${error.message}`).catch(console.error);
    console.error(error);
}

async function frame(time) {
    if (stopped) return;
    try {
        const delta = mouseX;
        mouseX = 0;
        const pixels = await dotnet.invokeMethodAsync("Frame", time, delta, active);
        context.putImageData(new ImageData(new Uint8ClampedArray(pixels), 640, 400), 0, 0);
        if (!stopped) animation = requestAnimationFrame(frame);
    } catch (error) { fail(error); }
}

export function playSound(bytes, sampleRate) {
    // Autoplay runs silently until a user gesture enables WebAudio.
    if (!active || !audio || !audioReady) return;
    if (audio.state !== "running") throw new Error("Click the game to enable browser audio.");
    const buffer = audio.createBuffer(1, bytes.length / 2, sampleRate);
    const samples = buffer.getChannelData(0);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    for (let i = 0; i < samples.length; i++) samples[i] = view.getInt16(i * 2, true) / 32768;
    const voice = audio.createBufferSource();
    voice.buffer = buffer;
    voice.connect(audio.destination);
    if (voices.size === 16) { const first = voices.values().next().value; first.stop(); voices.delete(first); }
    voices.add(voice);
    voice.onended = () => { voices.delete(voice); voice.disconnect(); };
    voice.start();
}

let musicBuffer = null;
let musicName = null;
let musicVoice = null;
let pendingMusicBytes = null;
let pendingMusicSampleRate = 0;

function decodePendingMusic() {
    // Autoplay must not touch WebAudio; decoding only happens once a user
    // gesture has created and resumed the AudioContext.
    if (!audio || !audioReady || !pendingMusicBytes) return;
    const bytes = pendingMusicBytes;
    const sampleRate = pendingMusicSampleRate;
    pendingMusicBytes = null;
    musicBuffer = audio.createBuffer(1, bytes.length / 2, sampleRate);
    const samples = musicBuffer.getChannelData(0);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    for (let i = 0; i < samples.length; i++) samples[i] = view.getInt16(i * 2, true) / 32768;
}

function startMusicVoice() {
    decodePendingMusic();
    if (!musicBuffer || !active || !audio || !audioReady) return;
    if (musicVoice) { musicVoice.stop(); musicVoice.disconnect(); }
    const voice = audio.createBufferSource();
    voice.buffer = musicBuffer;
    voice.loop = true;
    voice.connect(audio.destination);
    voice.start();
    musicVoice = voice;
}

export function playMusic(name, bytes, sampleRate) {
    if (name === musicName) { if (!musicVoice) startMusicVoice(); return; }
    musicName = name;
    if (musicVoice) { musicVoice.stop(); musicVoice.disconnect(); musicVoice = null; }
    musicBuffer = null;
    pendingMusicBytes = null;
    if (!bytes || bytes.length === 0) return;
    pendingMusicBytes = bytes;
    pendingMusicSampleRate = sampleRate;
    // Mirrors playSound: runs silently until a user gesture enables WebAudio.
    if (!audio || !audioReady) return;
    startMusicVoice();
}

export function stopMusic() {
    musicName = null;
    musicBuffer = null;
    pendingMusicBytes = null;
    if (musicVoice) { musicVoice.stop(); musicVoice.disconnect(); musicVoice = null; }
}

const saveKey = "doomsharp-save";

export function saveGame(json) {
    try { localStorage.setItem(saveKey, json); }
    catch (error) { console.error(error); }
}

export function loadGame() {
    try { return localStorage.getItem(saveKey); }
    catch (error) { console.error(error); return null; }
}

export async function dispose() {
    stopped = true;
    cancelAnimationFrame(animation);
    for (const remove of listeners.splice(0)) remove();
    for (const voice of voices) voice.stop();
    voices.clear();
    if (musicVoice) { musicVoice.stop(); musicVoice.disconnect(); musicVoice = null; }
    if (audio) await audio.close();
}
