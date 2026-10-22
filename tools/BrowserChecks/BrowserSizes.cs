using System.Text.Json;

internal static class BrowserSizes
{
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
        static long Size(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(Primary).Sum(path => new FileInfo(path).Length);
        JsonElement render = JsonDocument.Parse(File.ReadAllText(Path.Combine(measurements, "render-comparison.json"))).RootElement;
        var result = new
        {
            method = "Release published files, raw bytes excluding shared Freedoom WAD, licenses, source maps, debug files and precompressed sidecars; 120 consecutive software renders of paused initial E1M1 in headless Chromium, wall-clock performance.now; median of five samples per runtime.",
            blazorBytes = Size(framework) + new[] { "index.html", "host.js", "style.css" }.Sum(file => new FileInfo(Path.Combine(site, file)).Length),
            netwasmBytes = Size(netwasm),
            render,
        };
        File.WriteAllText(Path.Combine(site, "comparison.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
