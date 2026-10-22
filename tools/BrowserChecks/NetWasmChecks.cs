using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

internal static class NetWasmChecks
{
    public static async Task<int> Run(string baseUrl, string output, bool onlyNetWasm = false, string? fixtures = null)
    {
        if (Directory.Exists(output)) throw new IOException("Evidence directory already exists: " + output);
        Directory.CreateDirectory(output);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        List<object> results = [];
        int? baselineChecksum = null;
        foreach (string runtime in onlyNetWasm ? new[] { "netwasm/" } : new[] { "", "netwasm/" })
        {
            IPage page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1200, Height = 800 } });
            List<string> errors = [];
            page.PageError += (_, error) => errors.Add(error);
            page.RequestFailed += (_, request) =>
            {
                if (request.Failure != "net::ERR_ABORTED") errors.Add($"{request.Url}: {request.Failure}");
            };
            string url = baseUrl.TrimEnd('/') + "/" + runtime;
            await page.AddInitScriptAsync("""
                window.soundStarts = 0;
                const start = AudioBufferSourceNode.prototype.start;
                AudioBufferSourceNode.prototype.start = function (...args) {
                    window.soundStarts++;
                    return start.apply(this, args);
                };
                """);
            await page.GotoAsync(url);
            await page.WaitForFunctionAsync("() => document.querySelector('#status')?.textContent.startsWith('Playing E1M1.')", null,
                new() { Timeout = 120000 });
            await page.WaitForFunctionAsync("""
                () => {
                    const data = document.querySelector('#game').getContext('2d').getImageData(0, 0, 640, 400).data;
                    let nonblack = 0;
                    for (let i = 0; i < data.length; i += 4)
                        if (data[i] || data[i+1] || data[i+2]) nonblack++;
                    return nonblack > 200000;
                }
                """);
            var downloads = await page.EvaluateAsync<JsonElement>("""
                baseUrl => {
                    const entries = [...performance.getEntriesByType('navigation'), ...performance.getEntriesByType('resource')]
                        .filter(entry => entry.name.startsWith(baseUrl) &&
                            !entry.name.includes('/assets/freedoom/') &&
                            !entry.name.endsWith('/comparison.json'));
                    return {
                        rawBytes: entries.reduce((total, entry) => total + entry.decodedBodySize, 0),
                        resources: entries.map(entry => ({
                            name: new URL(entry.name).pathname, bytes: entry.decodedBodySize
                        }))
                    };
                }
                """, baseUrl.TrimEnd('/') + "/");
            if (downloads.GetProperty("rawBytes").GetInt64() <= 0)
                throw new InvalidOperationException($"{runtime}: browser did not report first-load response sizes.");
            const string pixelHash = """
                () => {
                    const data = document.querySelector('#game').getContext('2d').getImageData(0, 0, 640, 350).data;
                    let hash = 2166136261;
                    for (let i = 0; i < data.length; i += 128)
                        hash = Math.imul(hash ^ data[i], 16777619);
                    return hash >>> 0;
                }
                """;
            await page.EvaluateAsync("""
                () => {
                    const context = document.querySelector('#game').getContext('2d');
                    const original = context.putImageData;
                    window.frameFault = { draws: 0 };
                    context.putImageData = function (...args) {
                        if (window.frameFault.draws === 0) {
                            context.clearRect(0, 0, 640, 400);
                            window.frameFault.draws = -1;
                            throw new Error('Injected frame fault');
                        }
                        window.frameFault.draws++;
                        return original.apply(this, args);
                    };
                }
                """);
            await page.WaitForFunctionAsync("""
                () => document.querySelector('#status')?.textContent === 'Game stopped: Injected frame fault'
                """);
            uint blank = await page.EvaluateAsync<uint>(pixelHash);
            await page.GetByRole(AriaRole.Button, new() { Name = "Restart Freedoom", Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("""
                () => {
                    if (!document.querySelector('#status')?.textContent.startsWith('Playing E1M1.') ||
                        window.frameFault.draws < 0) return false;
                    const data = document.querySelector('#game').getContext('2d').getImageData(0, 0, 640, 400).data;
                    let nonblack = 0;
                    for (let i = 0; i < data.length; i += 4)
                        if (data[i] || data[i+1] || data[i+2]) nonblack++;
                    return nonblack > 200000;
                }
                """);
            uint recovered = await page.EvaluateAsync<uint>(pixelHash);
            if (blank == recovered || errors.Count > 0)
                throw new InvalidOperationException($"{runtime}: frame-fault retry did not restore rendering; browser errors: {string.Join("; ", errors)}");
            uint before = await page.EvaluateAsync<uint>(pixelHash);
            await page.Keyboard.DownAsync("ArrowRight");
            await page.WaitForTimeoutAsync(400);
            await page.Keyboard.UpAsync("ArrowRight");
            await page.WaitForTimeoutAsync(150);
            uint after = await page.EvaluateAsync<uint>(pixelHash);
            if (before == after) throw new InvalidOperationException($"{runtime}: keyboard turn did not change rendered scene.");
            if (runtime.Length > 0)
            {
                await page.Locator("#game").ClickAsync();
                int priorSound = await page.EvaluateAsync<int>("window.soundStarts");
                await page.Keyboard.DownAsync("Control");
                await page.WaitForTimeoutAsync(400);
                await page.Keyboard.UpAsync("Control");
                await page.WaitForFunctionAsync("before => window.soundStarts > before", priorSound);
            }
            await page.ReloadAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('#status')?.textContent.startsWith('Playing E1M1.')", null,
                new() { Timeout = 120000 });
            await page.EvaluateAsync("window.dispatchEvent(new Event('blur'))");
            List<double> timings = [];
            int? checksum = null;
            for (int sample = 0; sample < (onlyNetWasm ? 1 : 5); sample++)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Benchmark 120 renders" }).ClickAsync();
                string text = await page.Locator("#status").InnerTextAsync();
                Match match = Regex.Match(text, @"120 software renders: ([\d.]+) ms \(checksum (-?\d+)\)");
                if (!match.Success || errors.Count > 0)
                    throw new InvalidOperationException($"{runtime}: benchmark/status: {text}; browser errors: {string.Join("; ", errors)}");
                timings.Add(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                int observed = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (checksum is not null && checksum != observed)
                    throw new InvalidOperationException($"{runtime}: rendered checksum changed between samples.");
                checksum = observed;
            }
            timings.Sort();
            if (baselineChecksum is not null && baselineChecksum != checksum)
                throw new InvalidOperationException("The two runtimes rendered different benchmark checksums; speed comparison is invalid.");
            baselineChecksum = checksum;
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, runtime.Length == 0 ? "blazor.png" : "netwasm.png") });
            results.Add(new { runtime = runtime.Length == 0 ? "blazor-aot" : "netwasm", url,
                renderMilliseconds = timings[timings.Count / 2], samplesMilliseconds = timings,
                checksum,
                firstLoad = downloads,
                browser = await page.EvaluateAsync<string>("navigator.userAgent") });
            if (runtime.Length > 0 && fixtures is not null)
            {
                uint original = await page.EvaluateAsync<uint>(pixelHash);
                await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(fixtures, "portal-equal.wad"));
                await page.WaitForFunctionAsync("""
                    oldHash => {
                        const data = document.querySelector('#game').getContext('2d').getImageData(0, 0, 640, 350).data;
                        let hash = 2166136261;
                        for (let i = 0; i < data.length; i += 128) hash = Math.imul(hash ^ data[i], 16777619);
                        return (hash >>> 0) !== oldHash &&
                            document.querySelector('#status').textContent.startsWith('Playing ');
                    }
                    """, original);
                if (errors.Count > 0) throw new InvalidOperationException("NetWasm local WAD errors: " + string.Join("; ", errors));
            }
            await page.CloseAsync();
        }
        await File.WriteAllTextAsync(Path.Combine(output, "render-comparison.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
