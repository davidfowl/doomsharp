using System.Diagnostics;
using System.Globalization;

namespace DoomSharp;

static class Program
{
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or FormatException or OverflowException or
            PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"DoomSharp: {ex.Message}");
            return 1;
        }
    }
    static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            Console.WriteLine("""
                DoomSharp - clean-room classic WAD engine (incomplete DOOM compatibility)
                Usage: DoomSharp --wad /path/to/game.wad [--map E1M1] [--no-audio]
                       DoomSharp --demo [--no-audio]
                Options:
                  --demo                                  Play bundled Freedoom Phase 1 (E1M1)
                  --fixture-demo                          Play the original two-room test level
                  --headless --frames N --output frame.ppm  Render without native devices
                  --frames N                               Stop after N frames (desktop too)
                  --validate-maps                          Load and render every map headlessly
                  --demo-wad path.wad                      Write the original demo WAD
                Controls: WASD move, arrows turn, mouse aim, Ctrl/click fire, E/Space use,
                          Shift run, 1 pistol, 2 shotgun, 4 chaingun, 5 rocket launcher,
                          Tab automap, R restart, F5 save, F9 load, Esc quit.
                Click the window once to capture the mouse. No proprietary assets included.
                """);
            return 0;
        }
        string? Value(string option)
        {
            int i = Array.IndexOf(args, option);
            if (i < 0) return null;
            if (i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {option}.");
            return args[i + 1];
        }
        HashSet<string> valued = ["--wad", "--map", "--frames", "--output", "--demo-wad"];
        HashSet<string> flags = ["--demo", "--fixture-demo", "--headless", "--no-audio", "--validate-maps"];
        for (int i = 0; i < args.Length; i++)
        {
            if (valued.Contains(args[i])) { _ = Value(args[i]); i++; }
            else if (!flags.Contains(args[i])) throw new ArgumentException($"Unknown option: {args[i]}");
        }
        if (Value("--demo-wad") is string demoPath) { File.WriteAllBytes(demoPath, Demo.Create()); Console.WriteLine($"Wrote original demo WAD: {demoPath}"); return 0; }
        bool demo = args.Contains("--demo");
        bool fixtureDemo = args.Contains("--fixture-demo");
        string? path = Value("--wad");
        if ((demo ? 1 : 0) + (fixtureDemo ? 1 : 0) + (path != null ? 1 : 0) > 1)
            throw new ArgumentException("Use only one of --demo, --fixture-demo or --wad.");
        if (demo)
        {
            path = Path.Combine(AppContext.BaseDirectory, "assets", "freedoom", "freedoom1.wad");
            if (!File.Exists(path))
                throw new FileNotFoundException("Bundled Freedoom WAD is missing. Rebuild or republish with assets/freedoom/freedoom1.wad included.", path);
        }
        Wad wad = fixtureDemo ? new(Demo.Create()) : new(path ?? throw new ArgumentException("Provide --wad path, --demo or --fixture-demo."));
        string map = Value("--map") ?? (demo ? "E1M1" : wad.Maps.FirstOrDefault()) ?? throw new InvalidDataException("WAD contains no classic maps.");
        bool headless = args.Contains("--headless"), validate = args.Contains("--validate-maps");
        int frames = Value("--frames") is string count ? int.Parse(count, CultureInfo.InvariantCulture) : headless ? 1 : int.MaxValue;
        if (frames <= 0) throw new ArgumentException("--frames must be positive.");
        Renderer renderer = new();
        if (validate)
        {
            foreach (string name in wad.Maps)
            {
                Game level = new(wad, name);
                renderer.Render(level);
                Console.WriteLine($"{name}: {level.Map.Nodes.Length} BSP nodes, {level.Map.Sectors.Length} sectors, " +
                    $"{level.Actors.Count} actors, {renderer.VisibleWallPixels} wall pixels, {renderer.VisiblePlanePixels} plane pixels; " +
                    $"{level.UnsupportedThings} unsupported things, {level.Assets.MissingTextures} missing textures.");
            }
            if (wad.Maps.Count == 0) throw new InvalidDataException("WAD contains no maps.");
            return 0;
        }
        Game game = new(wad, map);
        Console.WriteLine($"{game.Map.Name}: {game.Map.Vertices.Length} vertices, {game.Map.Nodes.Length} BSP nodes, " +
            $"{game.Map.Sectors.Length} sectors, {game.TotalEnemies} enemies.");
        Input input = new();
        int rendered = 0;
        if (headless)
        {
            for (int i = 0; i < frames; i++) { game.Tick(input, 1.0 / 35); renderer.Render(game); rendered++; }
        }
        else
        {
            using IPlatform platform = Platform.Open(renderer.Width, renderer.Height);
            using Audio? audio = args.Contains("--no-audio") ? null : new(game.Assets);
            if (audio != null) { game.PlaySound = audio.Play; game.PlayMusic = audio.PlayMusic; game.StartMusic(); }
            string savePath = Path.Combine(AppContext.BaseDirectory, "doomsharp.sav");
            game.SaveGame = json => File.WriteAllText(savePath, json);
            game.LoadGame = () => File.Exists(savePath) ? File.ReadAllText(savePath) : null;
            Stopwatch clock = Stopwatch.StartNew();
            double previous = clock.Elapsed.TotalSeconds, accumulator = 0;
            for (int i = 0; i < frames && !input.Quit; i++)
            {
                platform.Poll(input);
                double now = clock.Elapsed.TotalSeconds;
                accumulator += Math.Min(.25, now - previous); previous = now;
                while (accumulator >= 1.0 / 35) { game.Tick(input, 1.0 / 35); accumulator -= 1.0 / 35; }
                audio?.Check();
                renderer.Render(game); platform.Present(renderer.Pixels);
                rendered++;
                Thread.Sleep(1);
            }
            if (audio != null) Console.WriteLine($"Audio: {audio.PlayedSounds} sound events, {audio.SubmittedSamples} PCM samples submitted, {audio.MusicChanges} music changes.");
        }
        if (Value("--output") is string output) renderer.SavePpm(output);
        Console.WriteLine($"Rendered {rendered} frames; wall={renderer.VisibleWallPixels}, " +
            $"plane={renderer.VisiblePlanePixels}, sprite={renderer.VisibleSpritePixels}, weapon={renderer.VisibleWeaponPixels}; health={game.Health}, kills={game.Kills}, " +
            $"position=({game.Position.X:F2},{game.Position.Y:F2}), shotgun={game.HasShotgun}, finished={game.Finished}, map={game.Map.Name}.");
        Console.WriteLine($"Inventory: weapon={game.SelectedWeapon}, bullets={game.Bullets}, shells={game.Shells}, " +
            $"chaingun={game.HasChaingun}.");
        Console.WriteLine($"Missile inventory: rockets={game.Rockets}, launcher={game.HasRocketLauncher}.");
        return 0;
    }
}
