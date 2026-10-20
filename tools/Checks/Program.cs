using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoomSharp;
using DoomSharp.Tools;

if (args.Length != 5 || args[0] is not ("headless" or "native" or "engine" or "all"))
{
    Console.Error.WriteLine("Usage: DoomSharp.Checks <headless|native|engine|all> <executable> <fixtures> <new-output-directory> <source-commit>");
    return 2;
}
string mode = args[0], executable = Path.GetFullPath(args[1]), fixtures = Path.GetFullPath(args[2]),
    output = Path.GetFullPath(args[3]), source = args[4];
if (!Regex.IsMatch(source, "^[0-9a-fA-F]{40}$") || !File.Exists(executable) || !Directory.Exists(fixtures) || Directory.Exists(output))
{
    Console.Error.WriteLine("Require an existing executable/fixture directory, a full source commit, and a new evidence directory.");
    return 2;
}
Directory.CreateDirectory(output);
List<object> results = [];
int failed = 0;
void Check(string name, bool passed, object? observed = null)
{
    results.Add(new { name, passed, observed });
    Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}");
    if (!passed) failed++;
}
Game Load(string name) { Wad wad = new(Path.Combine(fixtures, name + ".wad")); return new(wad, wad.Maps[0]); }
static void Tick(Game game, Buttons buttons = Buttons.None, int count = 1)
{
    Input input = new() { Held = buttons };
    for (int i = 0; i < count; i++) game.Tick(input, 1.0 / 35);
}
try
{
    if (mode is "headless" or "all")
    {
        foreach (string path in Directory.GetFiles(fixtures, "*.wad").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            bool reject = name.StartsWith("bad-", StringComparison.Ordinal) || name == "cyclic-node";
            string frame = Path.Combine(output, name + ".ppm");
            var run = await ProcessCheck.Run(executable,
                ["--wad", path, "--headless", "--frames", "35", "--output", frame]);
            File.WriteAllText(Path.Combine(output, name + ".log"), run.Text);
            Check("executable-" + name, reject ? run.Code == 1 && run.Text.Contains("DoomSharp:", StringComparison.Ordinal) :
                run.Code == 0 && Frame.Complete(frame, 640, 400), new { run.Code, frame });
        }
        foreach (string value in new[] { "0", "-1", "abc", "99999999999999999" })
        {
            var run = await ProcessCheck.Run(executable, ["--fixture-demo", "--headless", "--frames", value]);
            Check("reject-frames-" + value, run.Code == 1 && run.Text.Contains("DoomSharp:", StringComparison.Ordinal), run);
        }
        var missing = await ProcessCheck.Run(executable, ["--wad", Path.Combine(output, "does-not-exist.wad"), "--headless"]);
        Check("reject-missing-wad", missing.Code == 1 && missing.Text.Contains("DoomSharp:", StringComparison.Ordinal), missing);
    }
    if (mode is "engine" or "all")
    {
        foreach (string name in new[] { "portal-equal", "step-24", "step-25", "clearance-56", "clearance-55",
            "overlap-55", "closed-portal", "flag-blocked", "masked-passable", "masked-blocked" })
        {
            Game game = Load(name);
            Tick(game, Buttons.Forward, 35);
            bool cross = name is "portal-equal" or "step-24" or "clearance-56" or "masked-passable";
            Check("movement-" + name, cross ? game.Position.X > 0 : game.Position.X <= -16, game.Position);
        }
        foreach (int special in new[] { 99, 133, 134, 135, 136, 137 })
            foreach (int key in new[] { 0, 5, 40, 13, 38, 6, 39 })
            {
                Game game = Load($"door-{special}-{key}");
                Tick(game);
                Tick(game, Buttons.Use);
                Tick(game, count: 35);
                int required = special is 99 or 133 ? 1 : special is 134 or 135 ? 4 : 2;
                bool allowed = (game.Keys & required) != 0;
                bool opened = game.Map.Sectors[2].Ceiling > 100;
                Check($"tagged-lock-{special}-{key}", opened == allowed, new { game.Keys, opened, allowed });
                if (allowed)
                {
                    Tick(game, Buttons.Forward, 70);
                    Check($"tagged-traversal-{special}-{key}", game.Position.X > 192, game.Position);
                    int action = game.Map.Lines.Single(l => l.Tag == 7).Special;
                    Check($"tagged-consumption-{special}-{key}", action == (special is 99 or 134 or 136 ? special : 0), action);
                }
            }
        foreach (string prefix in new[] { "chaingun", "rocket" })
        {
            bool rocket = prefix == "rocket";
            Game game = Load(prefix + "-pickup");
            Tick(game);
            Check(prefix + "-pickup", rocket ? game.HasRocketLauncher && game.Rockets == 2 && game.SelectedWeapon == Weapon.RocketLauncher :
                game.HasChaingun && game.Bullets == 70 && game.SelectedWeapon == Weapon.Chaingun);
            int initial = game.Ammo;
            Tick(game, Buttons.Fire);
            Tick(game, count: 20);
            Check(prefix + "-tap-cadence", game.Ammo == initial - (rocket ? 1 : 2), new { initial, game.Ammo });
            game = Load(prefix + "-capacity"); Tick(game);
            Check(prefix + "-capacity", game.Ammo == (rocket ? 50 : 200), game.Ammo);
            game = Load(prefix + "-empty"); Tick(game);
            Tick(game, Buttons.Fire, 1000);
            Check(prefix + "-empty-no-underflow", game.Ammo == 0 && game.Projectiles.All(p => !p.Rocket || p.Exploded), game.Ammo);
            game = Load(prefix + "-unowned");
            Tick(game, rocket ? Buttons.RocketLauncher : Buttons.Chaingun);
            Check(prefix + "-unowned", game.SelectedWeapon == Weapon.Pistol && game.Message.Contains("NOT OWNED", StringComparison.Ordinal), game.Message);
            game = Load(prefix + "-combat"); Tick(game); Tick(game, Buttons.Fire, 350);
            Check(prefix + "-combat", game.Kills == 1, new { game.Kills, game.Ammo });
            game = Load(prefix + "-pickup"); Tick(game); Tick(game, Buttons.Forward, 20); Tick(game, Buttons.Restart);
            Check(prefix + "-restart", game.Health == 100 && game.SelectedWeapon == Weapon.Pistol && game.Position == new Vec(0, 0),
                new { game.Health, game.Position, game.SelectedWeapon });
            game = Load(prefix + "-carry"); Tick(game); Tick(game, Buttons.Forward, 50); Tick(game, Buttons.Use);
            Check(prefix + "-exit", game.Finished);
            Tick(game); Tick(game, Buttons.Use);
            Check(prefix + "-map-carry", game.Map.Name == "E1M2" && (rocket ? game.HasRocketLauncher : game.HasChaingun),
                new { game.Map.Name, game.SelectedWeapon, game.Ammo });
            game = Load(prefix + "-death"); Tick(game, count: 800);
            Check(prefix + "-death", game.Health == 0, game.Health);
        }
        foreach (string name in new[] { "last-baron", "two-barons", "knight", "mixed", "wrong-map", "no-tag" })
        {
            Game game = Load("boss-" + name); Tick(game);
            Tick(game, Buttons.Fire, 160);
            if (name == "two-barons")
            {
                Check("boss-first-baron-keeps-floor", game.Kills == 1 && game.Map.Sectors[1].Floor == 64,
                    new { game.Kills, floor = game.Map.Sectors[1].Floor });
                Actor target = game.Actors.Single(a => a.Enemy && a.Health > 0);
                double angle = Math.Atan2(target.Position.Y - game.Position.Y, target.Position.X - game.Position.X);
                game.Tick(new Input { MouseX = (game.Angle - angle) / .003 }, 1.0 / 35);
                Tick(game, Buttons.Fire, 160);
            }
            Tick(game, count: 120);
            bool activates = name is "last-baron" or "two-barons" or "mixed";
            Check("boss-" + name, (game.Map.Sectors[1].Floor == -32) == activates && !game.Finished,
                new { game.Kills, floor = game.Map.Sectors[1].Floor, game.Health });
        }
        foreach (string name in new[] { "combat", "miss", "splash", "occlusion", "thin-wall", "barrel" })
        {
            Game game = Load("rocket-" + name); Tick(game); Tick(game, Buttons.Fire);
            Check("rocket-" + name + "-travels-before-impact", game.Kills == 0 && game.Projectiles.Any(p => p.Rocket && !p.Exploded));
            Tick(game, count: 100);
            int kills = name switch { "miss" or "thin-wall" => 0, "splash" => 3, "occlusion" => 2, _ => 1 };
            Check("rocket-" + name + "-impact", game.Kills == kills, new { expected = kills, game.Kills, game.Health });
            int health = game.Health;
            Tick(game, count: 10);
            Check("rocket-" + name + "-cleanup", game.Projectiles.All(p => !p.Rocket) && (name == "miss" || game.Health == health),
                new { game.Health, projectiles = game.Projectiles.Count });
        }
        // (a) the animation table maps known names to their full, correctly ordered cycle.
        Check("texture-animation-flat-cycle",
            TextureAnimation.Flat("NUKAGE2", 0, _ => true) == "NUKAGE1" &&
            TextureAnimation.Flat("FWATER1", TextureAnimation.TicsPerFrame, _ => true) == "FWATER2" &&
            TextureAnimation.Flat("LAVA1", 0, _ => true) == "LAVA1");
        Check("texture-animation-wall-cycle",
            TextureAnimation.Wall("BLODGR3", 0, _ => true) == "BLODGR1" &&
            TextureAnimation.Wall("FIREBLU1", TextureAnimation.TicsPerFrame, _ => true) == "FIREBLU2" &&
            TextureAnimation.Wall("STARTAN2", 0, _ => true) == "STARTAN2");
        // (b) the frame index advances deterministically given a tic count, independent of framerate.
        Check("texture-animation-frame-index-deterministic",
            TextureAnimation.FrameIndex(0, 4) == 0 && TextureAnimation.FrameIndex(7, 4) == 0 &&
            TextureAnimation.FrameIndex(8, 4) == 1 && TextureAnimation.FrameIndex(31, 4) == 3 &&
            TextureAnimation.FrameIndex(32, 4) == 0 && TextureAnimation.FrameIndex(8 * 4 * 97 + 16, 4) == 2);
        // Missing frames (not present in the loaded WAD) fall back to the original static name.
        Check("texture-animation-missing-frame-falls-back",
            TextureAnimation.Flat("NUKAGE1", TextureAnimation.TicsPerFrame, _ => false) == "NUKAGE1" &&
            TextureAnimation.Wall("WFALL1", TextureAnimation.TicsPerFrame, _ => false) == "WFALL1");
        // (c) the renderer's displayed texture/flat name matches the expected cycle position for a given tic count.
        for (int frame = 0; frame < 3; frame++)
        {
            int tics = frame * TextureAnimation.TicsPerFrame + TextureAnimation.TicsPerFrame / 2;
            string expectedFlat = new[] { "NUKAGE1", "NUKAGE2", "NUKAGE3" }[frame];
            string expectedWall = new[] { "BLODRIP1", "BLODRIP2", "BLODRIP3" }[frame];
            string actualFlat = Renderer.AnimatedFlatName("NUKAGE1", tics, _ => true);
            string actualWall = Renderer.AnimatedWallName("BLODRIP1", tics, _ => true);
            Check($"renderer-animated-flat-frame-{frame}", actualFlat == expectedFlat, new { tics, actualFlat });
            Check($"renderer-animated-wall-frame-{frame}", actualWall == expectedWall, new { tics, actualWall });
        }
        Check("renderer-animated-flat-unanimated-passthrough", Renderer.AnimatedFlatName("FLOOR4_8", 1234, _ => true) == "FLOOR4_8");
        Check("renderer-animated-wall-unanimated-passthrough", Renderer.AnimatedWallName("STARTAN2", 1234, _ => true) == "STARTAN2");
        {
            // A fireball fired by one monster that strikes another should make the victim retarget the
            // attacker, not the player who never touched it.
            Game game = Load("infight-retarget");
            Tick(game, count: 150);
            Actor imp = game.Actors.Single(a => a.Type == 3001);
            Actor demon = game.Actors.Single(a => a.Type == 3002);
            Check("infight-retarget", demon.Target == imp && demon.Health < 150 && game.Health == 100,
                new { demon.Health, target = demon.Target == imp ? "imp" : "other", playerHealth = game.Health });
        }
        {
            // Player gunfire must never make a monster retaliate against another monster: the hit monster's
            // target stays unset (it keeps fighting the player, the only one that ever attacked it).
            Game game = Load("infight-player-safe");
            Tick(game);
            int initial = game.Actors.Single(a => a.Type == 3002).Health;
            Tick(game, Buttons.Fire);
            Tick(game, count: 5);
            Actor demon = game.Actors.Single(a => a.Type == 3002);
            Check("infight-player-safe", demon.Health < initial && demon.Target == null, new { initial, demon.Health, demon.Target });
        }
        {
            // Walking into an occupied teleport destination telefrags whatever is standing there and still
            // completes the teleport, instead of blocking it.
            Game game = Load("infight-telefrag");
            Actor blocker = game.Actors.Single(a => a.Type == 3004);
            Tick(game, Buttons.Forward, 60);
            Check("telefrag", blocker.Health <= 0 && game.Kills == 1 && game.Position.X > 50,
                new { blocker.Health, game.Kills, game.Position });
        }
    }
    if (mode is "native" or "all")
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native input checks require Linux/X11/XTest; use headless/engine on Windows and macOS.");
        foreach (bool restart in new[] { false, true })
        {
            string name = restart ? "native-restart" : "native-demo";
            using NativeGame game = await NativeGame.Start(executable, ["--fixture-demo"], Path.Combine(output, name), source, audio: true);
            game.Hold("w", .7); game.Hold("e", .1); Thread.Sleep(1600);
            game.Hold("w", .7); game.Hold("Control_L", 2); game.Hold("w", 2); game.Hold("e", .1);
            if (restart) game.Hold("r", .1);
            Frame frame = game.Capture("final-window");
            string text = await game.Quit();
            bool accepted = restart ? text.Contains("health=100, kills=0", StringComparison.Ordinal) &&
                text.Contains("position=(128.00,128.00), shotgun=False, finished=False", StringComparison.Ordinal) :
                text.Contains("kills=1", StringComparison.Ordinal) && text.Contains("shotgun=True, finished=True", StringComparison.Ordinal);
            Match pcm = Regex.Match(text, @"Audio: (\d+) sound events, (\d+) PCM samples submitted");
            Check(name, accepted && pcm.Success && long.Parse(pcm.Groups[1].Value, CultureInfo.InvariantCulture) > 0 &&
                long.Parse(pcm.Groups[2].Value, CultureInfo.InvariantCulture) > 0 && frame.Width == 960, text);
        }
        using (NativeGame game = await NativeGame.Start(executable, ["--wad", Path.Combine(fixtures, "portal-equal.wad")],
            Path.Combine(output, "native-tab"), source))
        {
            Frame initial = game.Capture("initial");
            game.Hold("Tab", .06); Frame on = game.Capture("map-on");
            game.Hold("Tab", .06); Frame off = game.Capture("map-off");
            game.Hold("w", .5); game.Hold("Control_L", .4);
            Frame fired = game.Capture("fired");
            string text = await game.Quit();
            double x = ProcessCheck.Position(text).X;
            Check("native-tab-open-close", initial.Background() < .05 && on.Background() > .9 && off.Background() < .05);
            Check("native-movement", x is >= 0 and <= 30, x);
            Check("native-fire-HUD", initial.Ammo() == 50 && fired.Ammo() == 48, new { initial = initial.Ammo(), final = fired.Ammo() });
        }
        using (NativeGame game = await NativeGame.Start(executable, ["--wad", Path.Combine(fixtures, "lift-use-repeat.wad")],
            Path.Combine(output, "native-lift"), source))
        {
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                game.Hold("w", cycle == 1 ? .3 : .6);
                Frame approach = game.Capture($"{cycle}-approach");
                Stopwatch start = Stopwatch.StartNew();
                game.Hold("e", .06);
                NativeGame.Until(start, 1.15); Frame bottom = game.Capture($"{cycle}-bottom");
                game.Hold("w", .35); Frame board = game.Capture($"{cycle}-board");
                NativeGame.Until(start, 2.70); Frame wait = game.Capture($"{cycle}-wait");
                NativeGame.Until(start, 4.70); Frame late = game.Capture($"{cycle}-rise");
                NativeGame.Until(start, 5.15); Frame top = game.Capture($"{cycle}-top");
                NativeGame.Until(start, 5.50); Frame stable = game.Capture($"{cycle}-stable");
                var a = approach.Landmarks(); var b = bottom.Landmarks(); var r = board.Landmarks();
                var l = late.Landmarks(); var t = top.Landmarks();
                double? estimate = t.Ceiling == null || t.Wall == null ? null :
                    128 - 41 * (t.Wall.Value - t.Ceiling.Value) / (t.Wall.Value + 1 - 175 * top.Height / 400.0);
                Check($"native-lift-{cycle}-lower", a.Wall != null && b.Wall < a.Wall, new { a, b });
                Check($"native-lift-{cycle}-wait", board.Hash == wait.Hash);
                Check($"native-lift-{cycle}-carry-rise", r.Ceiling == null && l.Ceiling != null && t.Ceiling > l.Ceiling &&
                    t.Wall == l.Wall && t.Wall == r.Wall, new { r, l, t });
                Check($"native-lift-{cycle}-height64", estimate != null && Math.Abs(estimate.Value - 64) <= 2, estimate);
                Check($"native-lift-{cycle}-stable", top.Hash == stable.Hash);
                game.Hold("s", .5); game.Hold("s", 2); game.Capture($"{cycle}-step-off");
            }
            string text = await game.Quit();
            Check("native-lift-step-off", ProcessCheck.Position(text).X <= -32, text);
        }
        foreach (string prefix in new[] { "chaingun", "rocket" })
        {
            using NativeGame game = await NativeGame.Start(executable, ["--wad", Path.Combine(fixtures, prefix + "-pickup.wad")],
                Path.Combine(output, "native-" + prefix), source, audio: true);
            game.Capture("pickup"); game.Hold("Control_L", .06); Thread.Sleep(800); game.Capture("fired");
            string text = await game.Quit();
            Check("native-" + prefix + "-fire", text.Contains(prefix == "rocket" ? "rockets=1, launcher=True" : "bullets=68", StringComparison.Ordinal) &&
                Regex.IsMatch(text, @"Audio: [1-9]\d* sound events, [1-9]\d* PCM samples submitted"), text);
        }
        foreach (var door in new[] { (Special: 99, Key: 5, Allowed: true), (Special: 134, Key: 38, Allowed: true),
            (Special: 136, Key: 6, Allowed: true), (Special: 99, Key: 13, Allowed: false), (Special: 133, Key: 0, Allowed: false) })
        {
            string name = $"door-{door.Special}-{door.Key}";
            using NativeGame game = await NativeGame.Start(executable, ["--wad", Path.Combine(fixtures, name + ".wad")],
                Path.Combine(output, "native-" + name), source);
            game.Hold("e", .06); game.Hold("w", 2.2); game.Capture("traversal");
            string text = await game.Quit();
            double x = ProcessCheck.Position(text).X;
            Check("native-" + name, door.Allowed ? x > 192 : x is >= 32 and <= 48, x);
        }
    }
}
catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
    ArgumentException or TimeoutException or PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException)
{
    Check("runner-error", false, ex.ToString());
    Console.Error.WriteLine(ex);
}
finally
{
    File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
    {
        sourceCommit = source, sourceProvenance = "Caller-supplied build revision",
        executable, executableSha256 = ProcessCheck.Hash(executable), mode, results, failed,
        limits = "Engine checks simulate public C# input. Executable checks run the real CLI. Native checks use synthetic XTest/Xvfb input and framebuffer readback, ALSA null PCM, not physical input/audible playback or original-game parity."
    }, new JsonSerializerOptions { WriteIndented = true }));
}
return failed == 0 ? 0 : 1;

namespace DoomSharp.Tools
{
    static class ProcessCheck
    {
        public static string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        public sealed record Result(int Code, string Text);
        public static async Task<Result> Run(string executable, string[] args)
        {
            using var process = new Process { StartInfo = new(executable) { RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            return new(process.ExitCode, await stdout + await stderr);
        }
        public static Vec Position(string text)
        {
            Match match = Regex.Match(text, @"position=\((-?[\d.]+),(-?[\d.]+)\)");
            if (!match.Success) throw new InvalidDataException("Missing public position summary.");
            return new(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        }
    }
}
