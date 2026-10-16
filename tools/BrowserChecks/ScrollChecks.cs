using System.Text.Json;
using Microsoft.Playwright;

static class ScrollChecks
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is < 3 or > 4 || args[2] is not ("chromium" or "msedge")
            || (args.Length == 4 && args[3] != "--headed"))
        {
            Console.Error.WriteLine("Usage: DoomSharp.BrowserChecks scroll <base-url> <new-output-directory> <chromium|msedge> [--headed]");
            return 2;
        }
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Evidence directory already exists: " + output);
        Directory.CreateDirectory(output);
        List<object> observations = [];
        List<string> errors = [], diagnostics = [];
        JsonElement environment = default;
        string? version = null;
        int failed = 0;
        using IPlaywright playwright = await Playwright.CreateAsync();
        try
        {
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = args.Length != 4, Channel = args[2] == "msedge" ? "msedge" : null
            });
            version = browser.Version;
            await using IBrowserContext context = await browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = 1200, Height = 600 },
                RecordVideoDir = Path.Combine(output, "video"),
                RecordVideoSize = new() { Width = 1200, Height = 600 }
            });
            IPage page = await context.NewPageAsync();
            page.SetDefaultTimeout(60000);
            page.PageError += (_, error) => errors.Add(error);
            page.Console += (_, message) => diagnostics.Add($"{message.Type}: {message.Text}");
            await page.AddInitScriptAsync("""
                window.scrollProbe={presents:0,gaps:[],scrolls:0};
                const present=CanvasRenderingContext2D.prototype.putImageData;
                CanvasRenderingContext2D.prototype.putImageData=function(...args) {
                    scrollProbe.presents++;
                    return present.apply(this,args);
                };
                let previous;
                function sample(t) {
                    if(previous && scrollProbe.gaps.length<20000) scrollProbe.gaps.push(t-previous);
                    previous=t;
                    requestAnimationFrame(sample);
                }
                requestAnimationFrame(sample);
                window.addEventListener('scroll',()=>scrollProbe.scrolls++);
                window.canvasHash=()=>{
                    const pixels=document.querySelector('#game').getContext('2d').getImageData(0,0,640,400).data;
                    let hash=2166136261;
                    for(let i=0;i<pixels.length;i++) hash=Math.imul(hash^pixels[i],16777619);
                    return hash>>>0;
                };
                """);
            await page.GotoAsync(args[0]);
            await page.Locator("#game").WaitForAsync();
            ILocator oldSelector = page.GetByRole(AriaRole.Button, new() { Name = "Play Freedoom", Exact = true });
            if (await oldSelector.CountAsync() != 0) await oldSelector.ClickAsync();
            await page.WaitForFunctionAsync("document.querySelector('#status')?.textContent.startsWith('Playing ')");
            await page.GetByRole(AriaRole.Checkbox, new() { Name = "Mute" }).CheckAsync();
            await page.WaitForTimeoutAsync(500);
            environment = await page.EvaluateAsync<JsonElement>("""
                () => {
                    const gl=document.createElement('canvas').getContext('webgl');
                    const ext=gl?.getExtension('WEBGL_debug_renderer_info');
                    return {userAgent:navigator.userAgent,platform:navigator.platform,
                        devicePixelRatio,screen:{width:screen.width,height:screen.height},
                        renderer:ext?gl.getParameter(ext.UNMASKED_RENDERER_WEBGL):null};
                }
                """);
            ILocator canvas = page.Locator("#game");
            foreach (string scenario in new[] {
                "paused-scaled", "active-captured", "active-fallback-scaled",
                "active-fallback-composited", "paused-native", "paused-composited" })
            {
                if (scenario == "active-captured")
                {
                    await canvas.ClickAsync();
                    await page.Keyboard.DownAsync("ArrowRight");
                }
                if (scenario == "active-fallback-scaled")
                {
                    await page.Keyboard.UpAsync("ArrowRight");
                    await page.Keyboard.PressAsync("Escape");
                    await page.EvaluateAsync("""
                        () => {
                            HTMLCanvasElement.prototype.requestPointerLock=()=>
                                Promise.reject(new Error('scroll diagnostic: keyboard-only fallback'));
                        }
                        """);
                    await canvas.ClickAsync();
                    await page.Keyboard.DownAsync("ArrowRight");
                }
                if (scenario == "active-fallback-composited")
                    await canvas.EvaluateAsync("c=>c.style.transform='translateZ(0)'");
                if (scenario == "paused-native")
                {
                    await page.Keyboard.UpAsync("ArrowRight");
                    await page.Keyboard.PressAsync("Escape");
                    await canvas.EvaluateAsync("c=>{c.style.width='640px';c.style.transform='none'}");
                }
                if (scenario == "paused-composited")
                    await canvas.EvaluateAsync("c=>{c.style.width='';c.style.transform='translateZ(0)'}");
                await page.EvaluateAsync("window.scrollTo(0,0)");
                await page.Mouse.MoveAsync(scenario == "active-captured" ? 400 : 10, 20);
                await page.WaitForTimeoutAsync(300);
                JsonElement before = await page.EvaluateAsync<JsonElement>("""
                    () => {
                        scrollProbe.gaps=[];
                        scrollProbe.scrolls=0;
                        return {presents:scrollProbe.presents,hash:canvasHash(),scrollY,locked:!!document.pointerLockElement};
                    }
                    """);
                for (int i = 0; i < 80; i++)
                {
                    await page.Mouse.WheelAsync(0, i % 20 < 10 ? 12 : -12);
                    await page.WaitForTimeoutAsync(16);
                }
                JsonElement after = await page.EvaluateAsync<JsonElement>("""
                    () => {
                        const gaps=scrollProbe.gaps.slice().sort((a,b)=>a-b);
                        const rect=document.querySelector('#game').getBoundingClientRect();
                        return {presents:scrollProbe.presents,hash:canvasHash(),scrollY,scrolls:scrollProbe.scrolls,
                            locked:!!document.pointerLockElement,count:gaps.length,
                            median:gaps[Math.floor(gaps.length*.5)],p95:gaps[Math.floor(gaps.length*.95)],
                            max:gaps.at(-1),over50:gaps.filter(g=>g>50).length,
                            rect:{x:rect.x,y:rect.y,width:rect.width,height:rect.height}};
                    }
                    """);
                for (int i = 0; i < 24; i++)
                {
                    await page.Mouse.WheelAsync(0, i % 12 < 6 ? 37 : -37);
                    await page.WaitForTimeoutAsync(16);
                    if (i % 3 == 0)
                        await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"{scenario}-{i}.png") });
                }
                observations.Add(new { scenario, before, after });
                if (scenario.Contains("fallback", StringComparison.Ordinal)
                    && (after.GetProperty("scrolls").GetInt32() == 0
                        || after.GetProperty("presents").GetInt32() <= before.GetProperty("presents").GetInt32()))
                {
                    diagnostics.Add($"{scenario}: did not observe both document scrolling and animated canvas presentation.");
                    failed++;
                }
                Console.WriteLine($"{scenario}: {after}");
            }
            if (errors.Count != 0) failed++;
            await context.CloseAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or InvalidOperationException or TimeoutException)
        {
            diagnostics.Add(ex.ToString());
            Console.Error.WriteLine(ex);
            failed++;
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            {
                url = args[0], browser = args[2], version,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                environment = environment.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : environment,
                observations, errors, diagnostics, failed,
                limits = "Recorded browser compositor output, not physical display scanout. Hosted graphics may be software-rendered. Active fallback scenarios deliberately reject pointer lock to allow real document scrolling during animated rendering. Timing excludes screenshots but includes video recording. These observations alone do not prove the reported visual tear is fixed."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failed == 0 ? 0 : 1;
    }
}
