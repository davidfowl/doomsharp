using System.IO.Compression;
using System.Text.Json;
using Microsoft.Playwright;

internal static class BrowserSizes
{
    public static async Task<int> CheckSite(string baseUrl)
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        foreach (string route in new[] { "", "netwasm/" })
        {
            IPage page = await browser.NewPageAsync();
            await page.GotoAsync(baseUrl.TrimEnd('/') + "/" + route);
            await page.WaitForFunctionAsync("""
                () => {
                    const table = document.querySelector('#comparison table');
                    return table?.rows.length === 3 &&
                        table.rows[1].textContent.includes('.NET WASM') &&
                        table.rows[2].textContent.includes('NetWasm') &&
                        [...table.rows].slice(1).every(row =>
                            row.cells.length === 5 &&
                            row.cells[1].textContent.includes('MiB') &&
                            row.cells[2].textContent.includes('MiB') &&
                            row.cells[3].textContent.includes('MiB') &&
                            row.cells[4].textContent.includes('ms'));
                }
                """, null, new() { Timeout = 120000 });
            await page.CloseAsync();
        }
        return 0;
    }

    public static int Write(string site, string measurements)
    {
        string framework = Path.Combine(site, "_framework");
        string netwasm = Path.Combine(site, "netwasm");
        if (!Directory.Exists(framework) || !Directory.Exists(Path.Combine(netwasm, "browser")))
            throw new DirectoryNotFoundException("Both published browser runtimes are required.");
        static bool Primary(string path) => !path.EndsWith(".br", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".map", StringComparison.OrdinalIgnoreCase);
        static IEnumerable<string> Files(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(Primary);
        // Brotli quality 11 with a 24-bit window, as `brotli -q 11` writes, so both runtimes'
        // compressed footprints are measured the same way regardless of the host's encoding.
        static long Brotli(string path)
        {
            byte[] source = File.ReadAllBytes(path);
            byte[] destination = new byte[BrotliEncoder.GetMaxCompressedLength(source.Length)];
            if (!BrotliEncoder.TryCompress(source, destination, out int written, quality: 11, window: 24))
                throw new InvalidOperationException("Brotli compression failed: " + path);
            return written;
        }
        string[] blazorFiles = [.. Files(framework), .. new[] { "index.html", "host.js", "style.css" }.Select(file => Path.Combine(site, file))];
        string[] netwasmFiles = [.. Files(netwasm)];
        JsonElement render = JsonDocument.Parse(File.ReadAllText(Path.Combine(measurements, "render-comparison.json"))).RootElement;
        var result = new
        {
            measuredAtUtc = DateTimeOffset.UtcNow,
            host = new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                processors = Environment.ProcessorCount },
            method = "Release published file footprint and first-load same-origin navigation/resource Performance entries (decodedBodySize) in fresh headless Chromium contexts, raw bytes excluding shared Freedoom assets and comparison data, and the same published files compressed with Brotli quality 11 (24-bit window); 120 consecutive software renders of paused initial E1M1, wall-clock performance.now; median of five samples per runtime.",
            blazorBytes = blazorFiles.Sum(path => new FileInfo(path).Length),
            netwasmBytes = netwasmFiles.Sum(path => new FileInfo(path).Length),
            blazorBrotliBytes = blazorFiles.Sum(Brotli),
            netwasmBrotliBytes = netwasmFiles.Sum(Brotli),
            render,
        };
        File.WriteAllText(Path.Combine(site, "comparison.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
