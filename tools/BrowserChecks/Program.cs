using System.Text.Json;
using Microsoft.Playwright;
using DoomSharp;

if (args.Length > 0 && args[0] == "install") return Microsoft.Playwright.Program.Main(args);

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
        window.canvasPresents = 0;
        const present = CanvasRenderingContext2D.prototype.putImageData;
        CanvasRenderingContext2D.prototype.putImageData = function (...args) {
            window.canvasPresents++;
            return present.apply(this, args);
        };
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
    TaskCompletionSource releaseFreedoom = new();
    const string freedoomRoute = "**/assets/freedoom/freedoom1.wad";
    await page.RouteAsync(freedoomRoute, async route =>
    {
        await releaseFreedoom.Task;
        await route.ContinueAsync();
    });
    await page.GotoAsync(args[0], new() { WaitUntil = WaitUntilState.DOMContentLoaded });
    await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading Freedoom..." }).WaitForAsync();
    Check("automatic-Freedoom-loading-is-visible", await page.Locator("input[type=file]").IsDisabledAsync());
    Check("no-public-game-selection-buttons",
        await page.GetByRole(AriaRole.Button, new() { Name = "Test level", Exact = true }).CountAsync() == 0
        && await page.GetByRole(AriaRole.Button, new() { Name = "Play Freedoom", Exact = true }).CountAsync() == 0);
    releaseFreedoom.SetResult();
    await page.Locator("input[type=file]:enabled").WaitForAsync();
    await page.UnrouteAsync(freedoomRoute);
    ILocator canvas = page.Locator("#game");
    async Task CheckPageScroll(string name, bool overCanvas)
    {
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.Mouse.MoveAsync(overCanvas ? 400 : 10, overCanvas ? 350 : 20);
        await page.Mouse.WheelAsync(0, 400);
        await page.WaitForTimeoutAsync(400);
        double scroll = await page.EvaluateAsync<double>("window.scrollY");
        Check(name, scroll > 100, scroll);
    }
    await CheckPageScroll("initial-wheel-over-canvas-scrolls-page", true);
    await CheckPageScroll("initial-wheel-outside-canvas-scrolls-page", false);
    async Task CheckIdlePresents(string name)
    {
        await page.WaitForTimeoutAsync(150);
        int before = await page.EvaluateAsync<int>("window.canvasPresents");
        await page.WaitForTimeoutAsync(400);
        int after = await page.EvaluateAsync<int>("window.canvasPresents");
        Check(name, after == before, new { before, after });
    }
    await CheckIdlePresents("initial-canvas-does-not-repaint-unchanged-frames");
    async Task<JsonElement> Pixels() => await canvas.EvaluateAsync<JsonElement>("""
        c => {
            const p = c.getContext('2d').getImageData(0, 0, 640, 400).data;
            let nonblack=0, background=0, white=0, hash=2166136261;
            for(let i=0;i<p.length;i+=4) {
                if(p[i]||p[i+1]||p[i+2]) nonblack++;
                if(i<640*350*4 && p[i]===8 && p[i+1]===9 && p[i+2]===11) background++;
                if(i>=640*175*4 && i<640*195*4 && p[i]===255 && p[i+1]===255 && p[i+2]===255) white++;
                hash=Math.imul(hash^p[i],16777619);
                hash=Math.imul(hash^p[i+1],16777619);
                hash=Math.imul(hash^p[i+2],16777619);
            }
            return {nonblack, background, white, hash:hash>>>0};
        }
        """);
    async Task Hold(string key, int ms)
    {
        await page.Keyboard.DownAsync(key);
        await page.WaitForTimeoutAsync(ms);
        await page.Keyboard.UpAsync(key);
        await page.WaitForTimeoutAsync(150);
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
    async Task UntilHud(int y, int color, string text)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(12);
        string observed = "";
        while (DateTime.UtcNow < deadline)
        {
            observed = await Hud(y, color);
            if (observed.Contains(text, StringComparison.Ordinal)) return;
            await page.WaitForTimeoutAsync(100);
        }
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "failed-hud.png") });
        diagnostics.Add(await page.EvaluateAsync<string>("JSON.stringify({focus:document.activeElement?.id,hasFocus:document.hasFocus(),locked:document.pointerLockElement?.id,input:window.inputTrace,mouse:window.mouseTrace})"));
        throw new InvalidOperationException($"Visible HUD did not reach '{text}': {observed}");
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
    JsonElement automaticFreedoom = await Pixels();
    Check("Freedoom-starts-without-selection-click", automaticFreedoom.GetProperty("nonblack").GetInt32() > 200000,
        automaticFreedoom);
    Check("automatic-startup-does-not-request-audio-or-pointer-lock",
        await page.EvaluateAsync<bool>("window.audioContexts.length===0 && document.pointerLockElement===null"));
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "automatic-freedoom.png") });
    async Task LoadFixture()
    {
        await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "fixture-demo.wad", MimeType = "application/octet-stream", Buffer = Demo.Create()
        });
        await WaitForLoad();
        await WaitForPixels();
    }
    await LoadFixture();
    JsonElement initial = await Pixels();
    Check("fixture-renders-in-WASM", initial.GetProperty("nonblack").GetInt32() > 200000, initial);
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
    JsonElement off = await Pixels();
    Check("Tab-closes-automap", off.GetProperty("background").GetInt32() < 12000, off);
    await Hold("Control", 400);
    Check("browser-WebAudio-effects", await page.EvaluateAsync<int>("window.soundStarts") > 0,
        await page.EvaluateAsync<JsonElement>("({states:window.audioContexts.map(a=>a.state),status:document.querySelector('#status').textContent})"));
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
    await CheckIdlePresents("paused-canvas-does-not-repaint-unchanged-frames");
    await page.SetViewportSizeAsync(1200, 1000);
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "fixture.png") });
    await page.ReloadAsync();
    await page.Locator("input[type=file]:enabled").WaitForAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "Mute" }).CheckAsync();
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
    await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(fixtures, "portal-equal.wad"));
    await WaitForLoad();
    await WaitForPixels();
    Check("local-WAD-renders", (await Pixels()).GetProperty("nonblack").GetInt32() > 200000);
    await LoadFixture();
    await canvas.ClickAsync();
    await page.Keyboard.DownAsync("w");
    await UntilHud(355, 0xffdb8a, "SHOTGUN");
    await page.WaitForTimeoutAsync(500);
    await page.Keyboard.UpAsync("w");
    await Hold("e", 100); await page.WaitForTimeoutAsync(2500);
    await page.Keyboard.DownAsync("w"); await page.Keyboard.DownAsync("Control");
    await UntilHud(373, 0xc0c0c0, "KILLS 1/1");
    await page.Keyboard.UpAsync("Control");
    await page.WaitForTimeoutAsync(2500);
    await page.Keyboard.UpAsync("w");
    for (int attempt = 0; attempt < 5; attempt++)
    {
        await Hold("e", 100);
        if ((await Hud(175, 0xffffff, 30, 50)).Contains("END OF AVAILABLE MAPS", StringComparison.Ordinal)) break;
        await Hold("w", 1000);
    }
    string completed = await Hud(175, 0xffffff, 30, 50);
    Check("browser-demo-completion", completed.Contains("END OF AVAILABLE MAPS", StringComparison.Ordinal), completed);
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "completed-demo.png") });
    await Hold("r", 100);
    Check("browser-live-restart", (await Hud(355, 0xffdb8a)).Contains("AMMO 50  PISTOL", StringComparison.Ordinal)
        && !(await Hud(175, 0xffffff, 30, 50)).Contains("END OF AVAILABLE MAPS", StringComparison.Ordinal));
    await page.Keyboard.PressAsync("Escape");
    await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(fixtures, "bad-directory-count.wad"));
    await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Cannot load WAD:" }).WaitForAsync();
    Check("invalid-WAD-reported", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).Contains("directory", StringComparison.Ordinal));
    await page.WaitForFunctionAsync("""
        () => {
            const p=document.querySelector('#game').getContext('2d').getImageData(0,0,640,400).data;
            for(let i=0;i<p.length;i+=4) if(p[i]||p[i+1]||p[i+2]) return false;
            return true;
        }
        """);
    Check("failed-load-clears-retained-frame", (await Pixels()).GetProperty("nonblack").GetInt32() == 0);
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Map", Exact = true }).FillAsync("E9M9");
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Map", Exact = true }).PressAsync("Tab");
    await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
    {
        Name = "fixture-demo.wad", MimeType = "application/octet-stream", Buffer = Demo.Create()
    });
    await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Cannot load WAD:" }).WaitForAsync();
    Check("missing-map-reported", (await page.GetByRole(AriaRole.Status).InnerTextAsync()).Contains("E9M9", StringComparison.Ordinal));
    await page.RouteAsync(freedoomRoute, route => route.FulfillAsync(new() { Status = 503, Body = "Unavailable" }));
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Cannot load WAD:" }).WaitForAsync();
    Check("automatic-Freedoom-download-error-is-visible",
        (await page.GetByRole(AriaRole.Status).InnerTextAsync()).Contains("503", StringComparison.Ordinal)
        && await page.GetByRole(AriaRole.Button, new() { Name = "Retry Freedoom", Exact = true }).IsVisibleAsync());
    Check("local-WAD-remains-available-after-startup-error", await page.Locator("input[type=file]").IsEnabledAsync());
    await LoadFixture();
    Check("local-WAD-recovers-from-startup-error",
        !await page.GetByRole(AriaRole.Button, new() { Name = "Retry Freedoom", Exact = true }).IsVisibleAsync());
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Cannot load WAD:" }).WaitForAsync();
    await page.UnrouteAsync(freedoomRoute);
    await page.GetByRole(AriaRole.Button, new() { Name = "Retry Freedoom", Exact = true }).ClickAsync();
    await WaitForLoad(); await WaitForPixels();
    Check("retry-Freedoom-recovers", !await page.GetByRole(AriaRole.Button,
        new() { Name = "Retry Freedoom", Exact = true }).IsVisibleAsync());
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
