using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Playwright;
using DoomSharp;

const string FreedoomSha256 = "7323bcc168c5a45ff10749b339960e98314740a734c30d4b9f3337001f9e703d";

if (args.Length > 0 && args[0] == "install") return Microsoft.Playwright.Program.Main(args);
if (args.Length == 3 && args[0] == "netwasm") return await NetWasmChecks.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "netwasm-only") return await NetWasmChecks.Run(args[1], args[2], onlyNetWasm: true);
if (args.Length == 4 && args[0] == "netwasm") return await NetWasmChecks.Run(args[1], args[2], fixtures: args[3]);
if (args.Length == 4 && args[0] == "netwasm-only") return await NetWasmChecks.Run(args[1], args[2], onlyNetWasm: true, fixtures: args[3]);
if (args.Length == 3 && args[0] == "sizes") return BrowserSizes.Write(args[1], args[2]);

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Usage: DoomSharp.BrowserChecks <base-url> <fixtures> <new-output-directory> [chromium|firefox|webkit]");
    return 2;
}
string output = Path.GetFullPath(args[2]), fixtures = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Evidence directory already exists: " + output);
Directory.CreateDirectory(output);
using IPlaywright playwright = await Playwright.CreateAsync();
IBrowserType type = (args.Length == 4 ? args[3] : "chromium") switch
{
    "chromium" => playwright.Chromium, "firefox" => playwright.Firefox, "webkit" => playwright.Webkit,
    _ => throw new ArgumentException("Unsupported browser.")
};
List<object> results = [];
List<string> errors = [];
List<string> diagnostics = [];
List<string> outsideBase = [];
int failed = 0;
void Check(string name, bool passed, object? observation = null)
{
    results.Add(new { name, passed, observation });
    Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}");
    if (!passed) failed++;
}
try
{
    await using IBrowser browser = await type.LaunchAsync(new() { Headless = true });
    IPage page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 600 } });
    page.SetDefaultTimeout(60000);
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) => diagnostics.Add($"{message.Type}: {message.Text}");
    page.RequestFailed += (_, request) => diagnostics.Add($"Request failed: {request.Url}: {request.Failure}");
    Uri baseUri = new(args[0].TrimEnd('/') + "/");
    page.Request += (_, request) =>
    {
        Uri uri = new(request.Url);
        if (uri.GetLeftPart(UriPartial.Authority) == baseUri.GetLeftPart(UriPartial.Authority)
            && !baseUri.IsBaseOf(uri)) outsideBase.Add(request.Url);
    };
    await page.AddInitScriptAsync("""
        window.soundStarts = 0;
        window.audioContexts = [];
        window.inputTrace = [];
        for (const name of ['keydown','keyup','blur','focus','pointerlockchange'])
            window.addEventListener(name, event => {
                window.inputTrace.push({name,code:event.code,focus:document.activeElement?.id,
                    locked:document.pointerLockElement?.id,time:performance.now()});
                if(window.inputTrace.length>60) window.inputTrace.shift();
            }, true);
        window.mouseTrace = [];
        document.addEventListener('mousemove', event => {
            if(document.pointerLockElement && (event.movementX || event.movementY)) {
                window.mouseTrace.push({x:event.movementX,y:event.movementY,time:performance.now()});
                if(window.mouseTrace.length>20) window.mouseTrace.shift();
            }
        });
        const Audio = window.AudioContext;
        window.AudioContext = new Proxy(Audio, {construct(target,args) {
            const context = new target(...args);
            window.audioContexts.push(context);
            return context;
        }});
        const original = AudioBufferSourceNode.prototype.start;
        AudioBufferSourceNode.prototype.start = function (...args) {
            window.soundStarts++;
            return original.apply(this, args);
        };
        """);
    IResponse bundledWad = await page.RunAndWaitForResponseAsync(
        () => page.GotoAsync(args[0]),
        response => response.Url == new Uri(baseUri, "assets/freedoom/freedoom1.wad").AbsoluteUri);
    await page.GetByRole(AriaRole.Button, new() { Name = "Restart Freedoom", Exact = true }).WaitForAsync();
    ILocator canvas = page.Locator("#game");
    // A fixed sleep here raced with the browser's main thread staying busy servicing the
    // WASM autoplay render loop (this runs before the game is paused), which could leave
    // the scroll unprocessed by the time the assertion ran under Firefox on loaded CI
    // runners. Poll for the scroll to actually land instead, bounded by Playwright's
    // default WaitForFunction timeout, falling back to the last observed value on timeout
    // so the assertion still reports a meaningful diagnostic.
    async Task CheckPageScroll(string name, bool overCanvas)
    {
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.Mouse.MoveAsync(overCanvas ? 400 : 10, overCanvas ? 350 : 20);
        await page.Mouse.WheelAsync(0, 400);
        try
        {
            await page.WaitForFunctionAsync("() => window.scrollY > 100");
        }
        catch (TimeoutException) { }
        double scroll = await page.EvaluateAsync<double>("window.scrollY");
        Check(name, scroll > 100, scroll);
    }
    async Task<JsonElement> Pixels() => await canvas.EvaluateAsync<JsonElement>("""
        c => {
            const p = c.getContext('2d').getImageData(0, 0, 640, 400).data;
            let nonblack=0, background=0, white=0, switchOff=0, switchOn=0, hash=2166136261;
            for(let i=0;i<p.length;i+=4) {
                if(p[i]||p[i+1]||p[i+2]) nonblack++;
                if(i<640*350*4 && p[i]===8 && p[i+1]===9 && p[i+2]===11) background++;
                if(i>=640*175*4 && i<640*195*4 && p[i]===255 && p[i+1]===255 && p[i+2]===255) white++;
                if(i<640*350*4 && p[i]>p[i+1]*2 && p[i]>p[i+2]*2) switchOff++;
                if(i<640*350*4 && p[i+1]>p[i]*2 && p[i+1]>p[i+2]*2) switchOn++;
                hash=Math.imul(hash^p[i],16777619);
                hash=Math.imul(hash^p[i+1],16777619);
                hash=Math.imul(hash^p[i+2],16777619);
            }
            return {nonblack, background, white, switchOff, switchOn, hash:hash>>>0};
        }
        """);
    async Task Hold(string key, int ms)
    {
        await page.Keyboard.DownAsync(key);
        await page.WaitForTimeoutAsync(ms);
        await page.Keyboard.UpAsync(key);
        await page.WaitForTimeoutAsync(150);
    }
    // Browsers vary in input-to-render latency (notably Firefox under headless WASM), so poll for the
    // expected switch-texture pixel state instead of relying on a single fixed-delay snapshot. When
    // pauseOnSuccess is set, the Escape keydown is dispatched directly from the same in-page poll loop
    // (not via a follow-up Playwright round trip), so a repeatable button's real-time 35-tick reset
    // cannot race ahead of the pause while slower browsers finish their own condition check and the
    // separate IPC call needed to send a synthetic key press.
    async Task<JsonElement> WaitForSwitchState(bool on, int timeoutMs = 6000, bool pauseOnSuccess = false, string? tag = null)
    {
        bool satisfied = await canvas.EvaluateAsync<bool>($$"""
            c => new Promise(resolve => {
                const deadline = Date.now() + {{timeoutMs}};
                const check = () => {
                    const p = c.getContext('2d').getImageData(0, 0, 640, 400).data;
                    let switchOn = 0;
                    for (let i = 0; i < 640 * 350 * 4; i += 4)
                        if (p[i + 1] > p[i] * 2 && p[i + 1] > p[i + 2] * 2) switchOn++;
                    if ({{(on ? "switchOn > 500" : "switchOn === 0")}}) {
                        {{(pauseOnSuccess ? "c.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true }));" : "")}}
                        resolve(true);
                    } else if (Date.now() > deadline) resolve(false);
                    else requestAnimationFrame(check);
                };
                check();
            })
            """);
        if (!satisfied) diagnostics.Add($"WaitForSwitchState(tag={tag}, on={on}, pauseOnSuccess={pauseOnSuccess}) timed out after {timeoutMs}ms.");
        return await Pixels();
    }
    async Task<string> Hud(int y, int color, int left = 8, int columns = 52)
    {
        string[] glyphs = await canvas.EvaluateAsync<string[]>("""
            (c, {y,color,left,columns}) => {
                const p=c.getContext('2d').getImageData(0,y,640,14).data;
                return Array.from({length:columns}, (_,column) => {
                    let glyph='';
                    for(let row=0;row<7;row++) for(let x=0;x<5;x++) {
                        const i=((row*2)*640+left+column*12+x*2)*4;
                        glyph+=((p[i]<<16)|(p[i+1]<<8)|p[i+2])===color?'#':'.';
                    }
                    return glyph;
                });
            }
            """, new { y, color, left, columns });
        return string.Concat(glyphs.Select(g => Enumerable.Range(32, 59).Select(i => (char)i)
            .FirstOrDefault(c => Font.Glyph(c) == g, '?')));
    }
    async Task WaitForPixels(bool automap = false)
    {
        await page.WaitForFunctionAsync("""
            automap => {
                const p = document.querySelector('#game').getContext('2d').getImageData(0,0,640,400).data;
                let count = 0;
                for(let i=0;i<(automap?640*350*4:p.length);i+=4)
                    if(automap ? p[i]===8&&p[i+1]===9&&p[i+2]===11 : p[i]||p[i+1]||p[i+2]) count++;
                return count > 200000;
            }
            """, automap);
    }
    // The fixed Hold() delay after releasing Tab is not guaranteed to outlast the automap
    // teardown render, which caused rare flakiness in "Tab-closes-automap". Explicitly poll
    // for the automap-background color to actually disappear (bounded by Playwright's
    // default WaitForFunction timeout) before taking the pixel snapshot used by the assertion.
    async Task WaitForAutomapClosed()
    {
        await page.WaitForFunctionAsync("""
            () => {
                const p = document.querySelector('#game').getContext('2d').getImageData(0,0,640,350).data;
                let count = 0;
                for(let i=0;i<p.length;i+=4)
                    if(p[i]===8&&p[i+1]===9&&p[i+2]===11) count++;
                return count < 12000;
            }
            """);
    }
    async Task WaitForLoad()
    {
        try
        {
            await page.WaitForFunctionAsync("""
                () => {
                    const text=document.querySelector('#status').textContent;
                    return text.startsWith('Playing ') || text.startsWith('Cannot load WAD:') || text.startsWith('Game stopped:');
                }
                """);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("Load did not become ready: " + await page.GetByRole(AriaRole.Status).InnerTextAsync(), ex);
        }
        string text = await page.GetByRole(AriaRole.Status).InnerTextAsync();
        diagnostics.Add("Load status: " + text);
        if (!text.StartsWith("Playing ", StringComparison.Ordinal)) throw new InvalidOperationException(text);
    }
    await WaitForLoad();
    await WaitForPixels();
    Check("Freedoom-autoloads-E1M1", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).StartsWith("Playing E1M1.", StringComparison.Ordinal));
    // The WAD exceeds Chromium's inspector response-body cache.
    IAPIResponse wadAsset = await page.APIRequest.GetAsync(bundledWad.Url);
    Check("bundled-Freedoom-asset", bundledWad.Ok && wadAsset.Ok &&
        Convert.ToHexStringLower(SHA256.HashData(await wadAsset.BodyAsync())) == FreedoomSha256);
    await wadAsset.DisposeAsync();
    Check("no-test-level-button", await page.GetByRole(AriaRole.Button, new() { Name = "Test level", Exact = true }).CountAsync() == 0);
    Check("autoplay-does-not-request-audio-or-pointer", await page.EvaluateAsync<bool>(
        "window.audioContexts.length === 0 && window.soundStarts === 0 && document.pointerLockElement === null"));
    Check("autoplay-focuses-keyboard-controls", await canvas.EvaluateAsync<bool>("c => document.activeElement === c"));
    JsonElement autoplay = await Pixels();
    Check("Freedoom-renders-without-interaction", autoplay.GetProperty("nonblack").GetInt32() > 200000, autoplay);
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "freedoom-autoplay.png") });
    await Hold("ArrowRight", 400);
    Check("Freedoom-runs-before-first-click", (await Pixels()).GetProperty("hash").GetUInt32() != autoplay.GetProperty("hash").GetUInt32());
    await Hold("Control", 250);
    string autoplayHud = await Hud(355, 0xffdb8a);
    Check("Freedoom-fires-before-first-click", autoplayHud.Contains("PISTOL", StringComparison.Ordinal)
        && !autoplayHud.Contains("AMMO 50 ", StringComparison.Ordinal), autoplayHud);
    Check("autoplay-remains-silent", await page.EvaluateAsync<bool>("window.audioContexts.length === 0 && window.soundStarts === 0"));
    await CheckPageScroll("active-wheel-outside-canvas-scrolls-page", false);
    await canvas.ClickAsync();
    await page.WaitForFunctionAsync("window.audioContexts.some(context => context.state === 'running')");
    await Hold("Control", 400);
    Check("Freedoom-WebAudio-after-click", await page.EvaluateAsync<int>("window.soundStarts") > 0,
        await page.EvaluateAsync<JsonElement>("({states:window.audioContexts.map(a=>a.state),status:document.querySelector('#status').textContent})"));
    await page.Keyboard.PressAsync("Escape");
    await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(fixtures, "portal-equal.wad"));
    await WaitForLoad();
    await WaitForPixels();
    JsonElement initial = await Pixels();
    Check("local-WAD-renders-in-WASM", initial.GetProperty("nonblack").GetInt32() > 200000, initial);
    await canvas.ClickAsync();
    await page.WaitForTimeoutAsync(300);
    Check("pointer-capture-preserves-view", (await Pixels()).GetProperty("hash").GetUInt32() == initial.GetProperty("hash").GetUInt32());
    double activeScroll = await page.EvaluateAsync<double>("window.scrollY");
    await page.Mouse.WheelAsync(0, 400);
    await page.WaitForTimeoutAsync(400);
    double afterWheel = await page.EvaluateAsync<double>("window.scrollY");
    Check("active-wheel-keeps-canvas-in-view", Math.Abs(afterWheel - activeScroll) < 1,
        new { before = activeScroll, after = afterWheel });
    Check("wheel-preserves-framebuffer", (await Pixels()).GetProperty("hash").GetUInt32() == initial.GetProperty("hash").GetUInt32());
    await Hold("Tab", 100);
    await WaitForPixels(automap: true);
    JsonElement map = await Pixels();
    Check("Tab-opens-automap", map.GetProperty("background").GetInt32() > 200000, map);
    await Hold("Tab", 100);
    await WaitForAutomapClosed();
    JsonElement off = await Pixels();
    Check("Tab-closes-automap", off.GetProperty("background").GetInt32() < 12000, off);
    await Hold("w", 500);
    JsonElement moved = await Pixels();
    Check("keyboard-moves-world", moved.GetProperty("hash").GetUInt32() != off.GetProperty("hash").GetUInt32());
    if (await canvas.EvaluateAsync<bool>("c => document.pointerLockElement === c"))
    {
        // Headless absolute mouse moves can lose their relative deltas under pointer lock.
        await canvas.DispatchEventAsync("mousemove", new { bubbles = true, movementX = 20, movementY = 0 });
        await canvas.DispatchEventAsync("mousemove", new { bubbles = true, movementX = 40, movementY = 0 });
        await page.WaitForTimeoutAsync(300);
        Check("pointer-look-turns-world", (await Pixels()).GetProperty("hash").GetUInt32() != moved.GetProperty("hash").GetUInt32());
    }
    else diagnostics.Add("Pointer lock unavailable in this browser session; mouse-look behavior is unverified.");
    await page.Keyboard.PressAsync("Escape");
    await page.WaitForTimeoutAsync(300);
    JsonElement paused = await Pixels();
    await Hold("w", 400);
    JsonElement still = await Pixels();
    Check("Escape-pauses", paused.GetProperty("hash").GetUInt32() == still.GetProperty("hash").GetUInt32());
    Check("pause-releases-pointer", await page.EvaluateAsync<bool>("document.pointerLockElement === null"));
    await CheckPageScroll("paused-wheel-over-canvas-scrolls-page", true);
    await CheckPageScroll("paused-wheel-outside-canvas-scrolls-page", false);
    await page.SetViewportSizeAsync(1200, 1000);
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "fixture.png") });
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "Mute" }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Restart Freedoom", Exact = true }).ClickAsync();
    await WaitForLoad();
    await WaitForPixels();
    JsonElement freedoom = await Pixels();
    Check("bundled-Freedoom-renders", freedoom.GetProperty("nonblack").GetInt32() > 200000, freedoom);
    await canvas.ClickAsync();
    int soundsBefore = await page.EvaluateAsync<int>("window.soundStarts");
    await Hold("Control", 400);
    Check("mute-suppresses-effects", await page.EvaluateAsync<int>("window.soundStarts") == soundsBefore);
    await page.Keyboard.PressAsync("Escape");
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "freedoom.png") });
    await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(fixtures, "step-24.wad"));
    await WaitForLoad();
    await WaitForPixels();
    Check("local-WAD-renders", (await Pixels()).GetProperty("nonblack").GetInt32() > 200000);
    await canvas.ClickAsync();
    await Hold("Control", 400);
    Check("local-WAD-firing-consumes-ammo", !(await Hud(355, 0xffdb8a)).Contains("AMMO 50  PISTOL", StringComparison.Ordinal));
    await Hold("r", 100);
    Check("browser-live-restart", (await Hud(355, 0xffdb8a)).Contains("AMMO 50  PISTOL", StringComparison.Ordinal));
    await page.Keyboard.PressAsync("Escape");
    foreach (var scenario in new[] { (29, "middle"), (99, "middle"), (99, "no-key") })
    {
        int special = scenario.Item1;
        string variant = scenario.Item2, name = $"switch-{special}-{variant}";
        bool changed = variant != "no-key", repeat = special == 99 && changed;
        bool needsKey = special is 99 or 133 or 134 or 135 or 136 or 137 && changed;
        // Headless Firefox has an observed, CI-reproducible canvas-paint desync for this scenario's WAD:
        // the C# engine itself has been directly verified (via temporary debug counters during root-cause
        // investigation) to toggle the switch correctly and deterministically on every attempt -- yet the
        // canvas's own getImageData readback can keep returning stale (pre-toggle) pixels for the full wait
        // window regardless of polling strategy (requestAnimationFrame, setTimeout, or a forced reflow
        // before each read all fail identically), a genuine browser rendering limitation, not an engine or
        // test-polling bug. A full page reload (which tears down and recreates the canvas and its 2D
        // context) before a retried attempt is a safe mitigation here (unlike retrying just the "e"
        // keypress, which really would re-trigger an already toggled repeatable switch): it gives Firefox's
        // renderer a genuinely clean slate to paint correctly, while the pass/fail thresholds below are
        // unchanged from a single try.
        int attempts = needsKey ? 2 : 1;
        JsonElement before = default, activated = default, pausedState = default, settled = default;
        string hud = "", message = "", lastInputTrace = "";
        bool toggleOk = false, pauseOk = true, settledOk = false;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            if (attempt > 1)
            {
                // Swapping the WAD file via the existing file input keeps the same <canvas> element and its
                // already-created 2D context alive, which was observed (during root-cause investigation) to
                // sometimes carry the Firefox getImageData staleness forward into the retried attempt too. A
                // full page reload tears down and recreates the canvas and its rendering context from
                // scratch, which is what actually gives Firefox's renderer a clean slate to paint correctly.
                await page.ReloadAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Restart Freedoom", Exact = true }).WaitForAsync();
            }
            await page.WaitForFunctionAsync("document.pointerLockElement === null");
            await page.WaitForTimeoutAsync(200);
            await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(fixtures, name + ".wad"));
            await WaitForLoad();
            await WaitForPixels();
            before = await Pixels();
            // A programmatic FocusAsync() does not count as a user gesture to Firefox, so headless Firefox's
            // background/inactive-tab timer throttling can keep requestAnimationFrame (and therefore every
            // engine tick) from running at all for several seconds after a WAD swap -- unlike every other
            // scenario in this file, which always re-clicks the canvas (a real synthetic click event) before
            // waiting on engine state. Click here too so the key-pickup and switch-toggle waits below are not
            // racing a throttled, possibly-stalled render loop.
            await canvas.ClickAsync();
            await page.WaitForTimeoutAsync(100);
            if (needsKey)
            {
                // The fixture spawns the player standing on the key it needs, so pickup (processed once per
                // real-time game tick) normally finishes within a frame or two. But the engine only ticks while
                // the page considers itself "active", and under headless Firefox that can take an extra moment
                // to settle after a WAD swap. Pressing "e" before the first active tick has run would reach a
                // genuinely keyless player and get rejected by the locked-switch check, not because the engine
                // dropped the keystroke but because the key truly had not been picked up yet. Wait for the HUD's
                // key count to confirm pickup really happened before testing the switch itself.
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while (!(await Hud(373, 0xc0c0c0, 8, 40)).Contains("KEYS 1", StringComparison.Ordinal))
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        diagnostics.Add($"{name}: key pickup not observed in HUD within 5s before pressing e (attempt {attempt}/{attempts}).");
                        break;
                    }
                    await page.WaitForTimeoutAsync(100);
                }
            }
            await Hold("e", 150);
            // For the repeatable button scenario, dispatch the pausing Escape atomically from inside the
            // same in-page poll that detects the toggle (see WaitForSwitchState), so there is no gap for the
            // button's real-time 35-tick reset to slip past the pause. The timeout here is generous: headless
            // Firefox can throttle requestAnimationFrame (and therefore both the engine's own tick/render loop
            // and this poll's in-page detection loop, which also relies on rAF) for several seconds at a time.
            // Retrying the keypress itself is not a safe option here: button 99 fully reopens (DoorDirection
            // resets to 0) well inside that same window, so a second "e" tap sent while the first one's effect
            // merely hasn't been painted yet would really re-trigger the repeatable switch and toggle it back
            // off, trading a flaky pass for a flaky failure instead of fixing either.
            activated = await WaitForSwitchState(changed, 15000, pauseOnSuccess: repeat, tag: $"{name}-toggle (attempt {attempt}/{attempts})");
            hud = await Hud(355, 0xffdb8a);
            message = await Hud(8, 0xffee90, columns: 32);
            lastInputTrace = (await page.EvaluateAsync<JsonElement>("window.inputTrace")).ToString();
            toggleOk = before.GetProperty("switchOff").GetInt32() > 500 && before.GetProperty("switchOn").GetInt32() == 0 &&
                (changed ? activated.GetProperty("switchOn").GetInt32() > 500 : activated.GetProperty("switchOn").GetInt32() == 0);
            if (!toggleOk && attempt < attempts)
            {
                diagnostics.Add($"{name}: toggle not observed on attempt {attempt}/{attempts}; reloading the WAD and retrying the whole scenario.");
                await page.Keyboard.PressAsync("Escape");
                continue;
            }
            if (repeat)
            {
                pausedState = await WaitForSwitchState(true, 15000, tag: $"{name}-paused (attempt {attempt}/{attempts})");
                pauseOk = pausedState.GetProperty("switchOn").GetInt32() > 500;
                if (toggleOk && !pauseOk && attempt < attempts)
                {
                    diagnostics.Add($"{name}: pause-preserves-button not observed on attempt {attempt}/{attempts}; reloading the WAD and retrying the whole scenario.");
                    await canvas.ClickAsync();
                    await page.Keyboard.PressAsync("Escape");
                    continue;
                }
                await canvas.ClickAsync();
            }
            settled = await WaitForSwitchState(repeat || !changed ? false : true, 2000, tag: $"{name}-settled (attempt {attempt}/{attempts})");
            settledOk = repeat || !changed ?
                settled.GetProperty("switchOn").GetInt32() == 0 && settled.GetProperty("switchOff").GetInt32() > 500 :
                settled.GetProperty("switchOn").GetInt32() > 500;
            break;
        }
        Check(name + "-visible-toggle", toggleOk,
            new { before, activated, hud, message, input = lastInputTrace });
        if (repeat) Check(name + "-pause-preserves-button", pauseOk, pausedState);
        Check(name + "-visible-reset-or-latch", settledOk, settled);
        await page.Keyboard.PressAsync("Escape");
    }
    await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(fixtures, "bad-directory-count.wad"));
    await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Cannot load WAD:" }).WaitForAsync();
    Check("invalid-WAD-reported", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).Contains("directory", StringComparison.Ordinal));
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Map", Exact = true }).FillAsync("E9M9");
    await page.GetByRole(AriaRole.Button, new() { Name = "Restart Freedoom", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Cannot load WAD:" }).WaitForAsync();
    Check("missing-map-reported", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).Contains("E9M9", StringComparison.Ordinal));
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Map", Exact = true }).FillAsync("E1M2");
    await page.GetByRole(AriaRole.Button, new() { Name = "Restart Freedoom", Exact = true }).ClickAsync();
    await WaitForLoad();
    await WaitForPixels();
    Check("explicit-Freedoom-map-loads", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).StartsWith("Playing E1M2.", StringComparison.Ordinal));
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Map", Exact = true }).FillAsync("");
    await canvas.EvaluateAsync("""
        c => {
            window.originalPointerLock = c.requestPointerLock;
            c.requestPointerLock = () => new Promise((resolve, reject) => {
                window.rejectPointerLock = () => reject(new Error('Delayed pointer-lock denial'));
            });
        }
        """);
        await canvas.ClickAsync();
    await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(fixtures, "portal-equal.wad"));
    await WaitForLoad();
    await canvas.EvaluateAsync("""
        c => {
            c.requestPointerLock = window.originalPointerLock;
            window.rejectPointerLock();
        }
        """);
    await page.WaitForTimeoutAsync(200);
    Check("stale-pointer-denial-does-not-overwrite-new-game", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).StartsWith("Playing E1M1.", StringComparison.Ordinal));
    await page.RouteAsync("**/assets/freedoom/freedoom1.wad",
        route => route.FulfillAsync(new() { Status = 503, Body = "WAD unavailable" }));
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Cannot load WAD:" }).WaitForAsync();
    Check("autoload-download-failure-reported", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).Contains("503", StringComparison.Ordinal));
    Check("failed-autoload-allows-retry", await page.GetByRole(AriaRole.Button, new() { Name = "Restart Freedoom", Exact = true }).IsEnabledAsync());
    await page.UnrouteAsync("**/assets/freedoom/freedoom1.wad");
    await page.GetByRole(AriaRole.Button, new() { Name = "Restart Freedoom", Exact = true }).ClickAsync();
    await WaitForLoad();
    await WaitForPixels();
    Check("Freedoom-recovers-after-download-failure", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).StartsWith("Playing E1M1.", StringComparison.Ordinal));
    Check("no-unhandled-browser-errors", errors.Count == 0, errors);
    Check("resources-stay-under-base-path", outsideBase.Count == 0, outsideBase);
    Check("no-false-fatal-error-banner", !await page.Locator("#blazor-error-ui").IsVisibleAsync());
}
catch (Exception ex) when (ex is PlaywrightException or IOException or InvalidOperationException or TimeoutException)
{
    Check("browser-runner-error", false, ex.ToString());
    Console.Error.WriteLine(ex.Message);
}
finally
{
    File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
    {
        browser = type.Name, url = args[0], results, failed, diagnostics,
        limits = "Headless browser, synthetic keyboard/pointer and WebAudio scheduling; no physical input or audible playback. Browser tests do not establish Windows/macOS desktop behavior."
    }, new JsonSerializerOptions { WriteIndented = true }));
}
return failed == 0 ? 0 : 1;
