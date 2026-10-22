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

// Builds a minimal valid IWAD byte image (header + lump data + directory) from the given name/bytes pairs,
// for exercising lump parsing against synthetic/malformed data without needing an on-disk fixture file.
static byte[] BuildMinimalWad((string Name, byte[] Data)[] lumps)
{
    using MemoryStream stream = new();
    using BinaryWriter writer = new(stream);
    writer.Write("IWAD"u8);
    writer.Write(lumps.Length);
    List<int> offsets = [];
    writer.Write(0); // directory offset placeholder, patched below
    foreach ((string _, byte[] data) in lumps)
    {
        offsets.Add((int)stream.Position);
        writer.Write(data);
    }
    int directory = (int)stream.Position;
    foreach (((string name, byte[] data), int offset) in lumps.Zip(offsets))
    {
        writer.Write(offset);
        writer.Write(data.Length);
        byte[] nameBytes = new byte[8];
        Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
        writer.Write(nameBytes);
    }
    writer.Flush();
    byte[] bytes = stream.ToArray();
    bytes[8] = (byte)directory; bytes[9] = (byte)(directory >> 8); bytes[10] = (byte)(directory >> 16); bytes[11] = (byte)(directory >> 24);
    return bytes;
}

// Builds a DMX-format ("DS*") sound lump header (format 3, given rate/count) with filler sample data,
// sized exactly to satisfy the parser's `count <= data.Length - 8` bound check.
static byte[] BuildDmxSound(ushort rate, int sampleCount)
{
    byte[] data = new byte[8 + sampleCount];
    data[0] = 3; data[1] = 0;
    data[2] = (byte)rate; data[3] = (byte)(rate >> 8);
    data[4] = (byte)sampleCount; data[5] = (byte)(sampleCount >> 8); data[6] = (byte)(sampleCount >> 16); data[7] = (byte)(sampleCount >> 24);
    return data;
}

static byte[] Pack4(int value)
{
    byte[] bytes = new byte[4];
    bytes[0] = (byte)value; bytes[1] = (byte)(value >> 8); bytes[2] = (byte)(value >> 16); bytes[3] = (byte)(value >> 24);
    return bytes;
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

        // --- CLI/startup: a directory passed where a file path is expected must surface the UnauthorizedAccessException
        // thrown by the file APIs as a clean "DoomSharp: ..." message (exit 1), not an unhandled-exception process abort.
        var wadIsDirectory = await ProcessCheck.Run(executable, ["--wad", output, "--headless"]);
        Check("reject-wad-path-is-directory", wadIsDirectory.Code == 1 && wadIsDirectory.Text.Contains("DoomSharp:", StringComparison.Ordinal), wadIsDirectory);
        var demoWadIsDirectory = await ProcessCheck.Run(executable, ["--demo-wad", output]);
        Check("reject-demo-wad-path-is-directory", demoWadIsDirectory.Code == 1 && demoWadIsDirectory.Text.Contains("DoomSharp:", StringComparison.Ordinal), demoWadIsDirectory);
    }
    if (mode is "engine" or "all")
    {
        static string[] Textures(Side side) => [side.Upper, side.Lower, side.Middle];
        static bool RepeatSwitch(int special) => special is 60 or 62 or 69 or 123 or 63 or 99 or 134 or 136;
        foreach (string file in Directory.GetFiles(fixtures, "switch-*.wad").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string[] parts = name.Split('-', 3);
            int special = int.Parse(parts[1], CultureInfo.InvariantCulture);
            string variant = parts[2];
            bool surface = special is not (1 or 31 or 6 or 8), exit = special is 11 or 51;
            bool allowed = surface && variant is not ("back" or "busy" or "no-key") && (variant != "no-tag" || exit);
            bool changed = allowed && variant is not ("missing" or "custom"), repeat = RepeatSwitch(special);
            Game game = Load(name);
            if (variant == "busy") Tick(game, Buttons.Forward, 15); else Tick(game);
            Line line = game.Map.Lines[7];
            string[] before = Textures(game.Map.Sides[line.Right]);
            Side back = game.Map.Sides[line.Left];
            List<string> sounds = [];
            game.PlaySound = sounds.Add;
            using StringWriter diagnostics = new();
            TextWriter stderr = Console.Error;
            try { Console.SetError(diagnostics); Tick(game, Buttons.Use); }
            finally { Console.SetError(stderr); }
            int slot = variant is "upper" or "same-pair" or "fallback" ? 0 :
                variant is "lower" or "priority" ? 1 : 2;
            string[] expected = (string[])before.Clone();
            if (changed) expected[slot] = (before[slot].StartsWith("SW1", StringComparison.Ordinal) ? "SW2" : "SW1") + before[slot][3..];
            Check(name + "-texture-selection", Textures(game.Map.Sides[line.Right]).SequenceEqual(expected),
                new { before, actual = Textures(game.Map.Sides[line.Right]), expected });
            Check(name + "-back-side-unchanged", game.Map.Sides[line.Left] == back);
            int expectedSpecial = surface ? allowed && !repeat ? 0 : special : special == 31 ? 0 : special;
            Check(name + "-consumption", line.Special == expectedSpecial, line.Special);
            Check(name + "-click", sounds.Count(s => s is "SWTCHN" or "SWTCHX") == (changed ? 1 : 0) &&
                (!changed || sounds.Contains(exit ? "SWTCHX" : "SWTCHN")), sounds);
            if (special is 1 or 31)
                Check(name + "-manual-door-still-opens", game.Map.Sectors[2].Ceiling > 0);
            if (variant == "back")
                Check(name + "-back-denies-action", !game.Finished && game.Map.Sectors.All(s => s.DoorDirection == 0 && s.FloorDirection == 0));
            if (variant is "missing" or "fallback" or "custom")
                Check(name + "-diagnostic", diagnostics.ToString().Contains(variant == "custom" ? "Unsupported switch texture" :
                    "Missing switch texture partner", StringComparison.Ordinal) && game.Assets.MissingTextures == 0, diagnostics.ToString());
            Tick(game, count: 34);
            Check(name + "-before-35-ticks", Textures(game.Map.Sides[line.Right]).SequenceEqual(expected));
            Tick(game);
            Check(name + "-reset-or-latch", Textures(game.Map.Sides[line.Right]).SequenceEqual(changed && repeat ? before : expected));
            Check(name + "-reset-sound", sounds.Count(s => s is "SWTCHN" or "SWTCHX") == (changed ? repeat ? 2 : 1 : 0), sounds);
            Tick(game, Buttons.Restart);
            Check(name + "-restart", game.Map.Lines[7].Special == special &&
                Textures(game.Map.Sides[game.Map.Lines[7].Right]).SequenceEqual(before) && !game.Finished);
            Tick(game, count: 40);
            Check(name + "-no-stale-reset", Textures(game.Map.Sides[game.Map.Lines[7].Right]).SequenceEqual(before));
        }
        Game retrigger = Load("switch-99-middle"); Tick(retrigger);
        List<string> clicks = [];
        retrigger.PlaySound = clicks.Add;
        Tick(retrigger, Buttons.Use); Tick(retrigger, count: 16); Tick(retrigger, Buttons.Use);
        Check("switch-successful-retrigger-toggles", retrigger.Map.Sides[retrigger.Map.Lines[7].Right].Middle == "SW1BRCOM" &&
            clicks.Count(s => s == "SWTCHN") == 2);
        Tick(retrigger, count: 17);
        Check("switch-retrigger-keeps-original-timer", clicks.Count(s => s == "SWTCHN") == 2);
        Tick(retrigger);
        Check("switch-retrigger-one-reset", clicks.Count(s => s == "SWTCHN") == 3);
        Tick(retrigger, count: 35);
        Check("switch-retrigger-no-duplicate-timer", clicks.Count(s => s == "SWTCHN") == 3);
        Game busyButton = Load("switch-63-middle"); Tick(busyButton);
        clicks.Clear(); busyButton.PlaySound = clicks.Add;
        Tick(busyButton, Buttons.Use); Tick(busyButton, count: 5); Tick(busyButton, Buttons.Use);
        Check("switch-busy-retrigger-does-not-toggle", busyButton.Map.Sides[busyButton.Map.Lines[7].Right].Middle == "SW2BRCOM" &&
            clicks.Count(s => s == "SWTCHN") == 1);
        Tick(busyButton, count: 29);
        Check("switch-busy-retrigger-does-not-extend", busyButton.Map.Sides[busyButton.Map.Lines[7].Right].Middle == "SW1BRCOM" &&
            clicks.Count(s => s == "SWTCHN") == 2);
        Game restartedButton = Load("switch-99-middle"); Tick(restartedButton); Tick(restartedButton, Buttons.Use);
        Tick(restartedButton, count: 5); Tick(restartedButton, Buttons.Restart); Tick(restartedButton); Tick(restartedButton, Buttons.Use);
        Tick(restartedButton, count: 34);
        Check("switch-pending-restart-keeps-new-deadline", restartedButton.Map.Sides[restartedButton.Map.Lines[7].Right].Middle == "SW2BRCOM");
        Tick(restartedButton);
        Check("switch-pending-restart-resets-new-button", restartedButton.Map.Sides[restartedButton.Map.Lines[7].Right].Middle == "SW1BRCOM");
        Game noMove = Load("switch-23-middle");
        foreach (Sector sector in noMove.Map.Sectors) sector.Floor = 24;
        Tick(noMove); Tick(noMove, Buttons.Use);
        Check("switch-no-floor-motion-does-not-toggle", noMove.Map.Lines[7].Special == 23 &&
            noMove.Map.Sides[noMove.Map.Lines[7].Right].Middle == "SW1BRCOM");
        foreach (string variant in new[] { "missing", "custom" })
        {
            Game warning = Load("switch-99-" + variant); Tick(warning);
            using StringWriter diagnostics = new();
            TextWriter stderr = Console.Error;
            try
            {
                Console.SetError(diagnostics);
                Tick(warning, Buttons.Use); Tick(warning, count: 16); Tick(warning, Buttons.Use);
                Check("switch-" + variant + "-warns-once", diagnostics.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1);
                Tick(warning, Buttons.Restart); Tick(warning); Tick(warning, Buttons.Use);
                Check("switch-" + variant + "-restart-clears-warning", diagnostics.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 2);
            }
            finally { Console.SetError(stderr); }
        }
        Game independent = Load("switch-63-independent"); Tick(independent); Tick(independent, Buttons.Use);
        Tick(independent, Buttons.Forward, 15); Tick(independent, Buttons.Use);
        Check("switch-independent-buttons", independent.Map.Sides[independent.Map.Lines[7].Right].Middle == "SW2BRCOM" &&
            independent.Map.Sides[independent.Map.Lines[8].Right].Middle == "SW2BRN1");
        Tick(independent, count: 19);
        Check("switch-independent-first-reset", independent.Map.Sides[independent.Map.Lines[7].Right].Middle == "SW1BRCOM" &&
            independent.Map.Sides[independent.Map.Lines[8].Right].Middle == "SW2BRN1");
        Tick(independent, count: 16);
        Check("switch-independent-second-reset", independent.Map.Sides[independent.Map.Lines[8].Right].Middle == "SW1BRN1");
        Game carryButton = Load("switch-63-carry"); Tick(carryButton); Tick(carryButton, Buttons.Use);
        Tick(carryButton, Buttons.Forward | Buttons.Run, 16); Tick(carryButton, Buttons.Use);
        Check("switch-exit-with-pending-button", carryButton.Finished);
        Tick(carryButton); Tick(carryButton, Buttons.Use); Tick(carryButton, count: 40);
        Check("switch-no-reset-in-new-map", carryButton.Map.Name == "E1M2" && carryButton.Map.Sides.Length == 4);
        foreach (int crossing in new[] { 6, 8 })
        {
            Game cross = Load($"switch-{crossing}-middle"); Tick(cross, Buttons.Forward, 8);
            Check("switch-crossing-does-not-toggle-" + crossing, cross.Map.Sides[cross.Map.Lines[7].Right].Middle == "SW1BRCOM" &&
                (crossing == 6 ? cross.Map.Sectors[3].Ceiling < 128 : cross.Map.Sectors[3].Floor > 0));
        }
        foreach (int special in new[] { 6, 25, 77, 141 })
        {
            bool fast = special is 6 or 77;
            int speed = fast ? 2 : 1;
            Game cycle = Load($"crusher-{special}-cycle");
            List<string> sounds = [];
            cycle.PlaySound = sounds.Add;
            Tick(cycle, Buttons.Use);
            Check($"crusher-{special}-not-use", cycle.Map.Sectors[4].Ceiling == 128 && cycle.Map.Lines[7].Special == special);
            Tick(cycle, Buttons.Forward, 8);
            Check($"crusher-{special}-start-speed", cycle.Map.Sectors[4].Ceiling == 128 - speed);
            Check($"crusher-{special}-consumption", cycle.Map.Lines[7].Special == (special == 77 ? 77 : 0));
            Tick(cycle, Buttons.Back, 8);
            Check($"crusher-{special}-recross-keeps-phase", cycle.Map.Sectors[4].Ceiling == 128 - 9 * speed);
            int bottoms = 0, tops = 0;
            for (int tick = 0; tick < 500; tick++)
            {
                Tick(cycle);
                if (cycle.Map.Sectors[4].Ceiling == 8) bottoms++;
                if (cycle.Map.Sectors[4].Ceiling == 128) tops++;
            }
            Check($"crusher-{special}-cycles", bottoms >= 2 && tops >= 2 && cycle.Health == 100, new { bottoms, tops, cycle.Health });
            Check($"crusher-{special}-sounds", special == 141 ? !sounds.Contains("STNMOV") && sounds.Count(s => s == "PSTOP") >= 4 :
                sounds.Contains("STNMOV"), sounds.Distinct().ToArray());
            Tick(cycle, Buttons.Restart);
            Check($"crusher-{special}-restart", cycle.Map.Sectors[4].Ceiling == 128 && cycle.Map.Lines[7].Special == special);
            foreach (string modeName in new[] { "no-tag", "zero-tag" })
            {
                Game absent = Load($"crusher-{special}-{modeName}");
                Tick(absent, Buttons.Forward, 8); Tick(absent, count: 100);
                Check($"crusher-{special}-{modeName}", absent.Map.Sectors[4].Ceiling == 128 &&
                    absent.Map.Lines[7].Special == special && absent.Message == "CRUSHER HAS NO TAGGED SECTOR", absent.Message);
            }
            Game multiple = Load($"crusher-{special}-multiple");
            Tick(multiple, Buttons.Forward, 8);
            Check($"crusher-{special}-all-tags", multiple.Map.Sectors[4].Ceiling == 128 - speed &&
                multiple.Map.Sectors[5].Ceiling == 96 - speed && multiple.Map.Sectors[3].Ceiling == 256);
            Game raised = Load($"crusher-{special}-raised-floor");
            Tick(raised, Buttons.Forward, 8);
            double lowest = raised.Map.Sectors[4].Ceiling;
            for (int tick = 0; tick < 250; tick++) { Tick(raised); lowest = Math.Min(lowest, raised.Map.Sectors[4].Ceiling); }
            Check($"crusher-{special}-relative-bottom", lowest == 40 && raised.Map.Sectors[4].Floor == 32, lowest);
            Game doubleContact = Load($"crusher-{special}-double-contact");
            Tick(doubleContact, Buttons.Forward, 21);
            for (int tick = 0; tick < 160 && doubleContact.Health == 100; tick++) Tick(doubleContact);
            Check($"crusher-{special}-double-contact-single-damage", doubleContact.Health == 90 &&
                doubleContact.Map.TouchingSectors(doubleContact.Position, 16).Distinct().Count() == 2, doubleContact.Health);
            foreach (string variant in new[] { "crush", "armor", "overlap" })
            {
                Game crush = Load($"crusher-{special}-{variant}");
                Tick(crush, Buttons.Forward, 8);
                if (variant == "overlap") Tick(crush, Buttons.Back, 3);
                int first = 0;
                while (crush.Health == 100 && first++ < 160) Tick(crush);
                int health = crush.Health;
                double ceiling = crush.Map.Sectors[1].Ceiling;
                Check($"crusher-{special}-{variant}-damage", health == (variant == "armor" ? 93 : 90) &&
                    (variant != "overlap" || crush.Map.FindSector(crush.Position) == 0), new { health, crush.Position, ceiling });
                Tick(crush, count: 3);
                Check($"crusher-{special}-{variant}-damage-spacing", crush.Health == health, crush.Health);
                Tick(crush);
                Check($"crusher-{special}-{variant}-damage-cadence", crush.Health == health - (variant == "armor" ? 7 : 10), crush.Health);
                Check($"crusher-{special}-{variant}-crush-speed", Math.Abs(ceiling - crush.Map.Sectors[1].Ceiling - (fast ? 8 : .5)) < .000001);
                Tick(crush, count: 100);
                if (fast && variant == "armor")
                    Check($"crusher-{special}-armor-survives-first-stroke", crush.Health > 0 && crush.Health < 93, crush.Health);
                Tick(crush, count: 200);
                Check($"crusher-{special}-{variant}-lethal", crush.Health == 0, crush.Health);
                int wait = 0;
                while (crush.Map.Sectors[1].Ceiling != 8 && wait++ < 1200) Tick(crush);
                Tick(crush);
                Check($"crusher-{special}-{variant}-rise-restores-speed", crush.Map.Sectors[1].Ceiling == 8 + speed);
            }
            Game clear = Load($"crusher-{special}-overlap");
            Tick(clear, Buttons.Forward, 8); Tick(clear, Buttons.Back, 8); Tick(clear, count: 200);
            Check($"crusher-{special}-outside-footprint-safe", clear.Health == 100, clear.Health);
            foreach (string variant in new[] { "actor", "barrel", "drop" })
            {
                Game victim = Load($"crusher-{special}-{variant}");
                Tick(victim, Buttons.Forward, 8); Tick(victim, count: variant == "drop" ? 600 : 350);
                Actor actor = victim.Actors.First(a => a.Solid);
                Check($"crusher-{special}-{variant}", actor.Health <= 0 && (variant == "barrel" ? actor.Removed : victim.Kills == 1),
                    new { actor.Health, actor.Removed, victim.Kills });
                if (variant == "drop")
                    Check($"crusher-{special}-crushed-drop", victim.Actors.Any(a => a.Dropped) &&
                        victim.Actors.Where(a => a.Dropped).All(a => a.Removed));
            }
            foreach (int stop in new[] { 57, 74 })
                foreach (bool ascending in new[] { false, true })
                {
                    Game paused = Load($"crusher-{special}-stop{stop}");
                    Tick(paused, Buttons.Forward, 8);
                    if (ascending) Tick(paused, count: fast ? 60 : 120);
                    Tick(paused, Buttons.Forward, 16);
                    double height = paused.Map.Sectors[4].Ceiling;
                    Tick(paused, count: 40);
                    Check($"crusher-{special}-stop{stop}-{ascending}", paused.Map.Sectors[4].Ceiling == height &&
                        paused.Map.Lines[8].Special == (stop == 57 ? 0 : 74), height);
                    Tick(paused, Buttons.Forward, 15);
                    Check($"crusher-{special}-resume{stop}-{ascending}",
                        ascending ? paused.Map.Sectors[4].Ceiling > height : paused.Map.Sectors[4].Ceiling < height,
                        new { before = height, after = paused.Map.Sectors[4].Ceiling });
                    double resumedHeight = paused.Map.Sectors[4].Ceiling;
                    Tick(paused);
                    Check($"crusher-{special}-resume{stop}-{ascending}-preserves-speed",
                        paused.Map.Sectors[4].Ceiling - resumedHeight == (ascending ? speed : -speed));
                }
            Game pausedPlayer = Load($"crusher-{special}-paused-player");
            Tick(pausedPlayer, Buttons.Forward, 24);
            double pausedHeight = pausedPlayer.Map.Sectors[2].Ceiling;
            Tick(pausedPlayer, count: 200);
            Check($"crusher-{special}-paused-player-safe", pausedPlayer.Health == 100 &&
                pausedPlayer.Map.Sectors[2].Ceiling == pausedHeight, pausedPlayer.Health);
            Tick(pausedPlayer, Buttons.Forward, 15); Tick(pausedPlayer, count: 150);
            Check($"crusher-{special}-resumed-player-crushed", pausedPlayer.Health < 100, pausedPlayer.Health);
            Game pausedActor = Load($"crusher-{special}-paused-actor");
            Tick(pausedActor, Buttons.Forward, 8);
            Actor caught = pausedActor.Actors.Single(a => a.Enemy);
            for (int tick = 0; tick < 160 && caught.Health == 150; tick++) Tick(pausedActor);
            Tick(pausedActor, Buttons.Forward, 16);
            int pausedHealth = caught.Health;
            Tick(pausedActor, count: 40);
            Check($"crusher-{special}-stop-halts-damage", pausedHealth is > 0 and < 150 && caught.Health == pausedHealth, caught.Health);
            Tick(pausedActor, Buttons.Forward, 15); Tick(pausedActor, count: 4);
            Check($"crusher-{special}-resume-restores-damage", caught.Health < pausedHealth, caught.Health);
            foreach (string variant in new[] { "door-busy", "floor-busy", "crusher-door", "crusher-floor" })
            {
                Game busy = Load($"crusher-{special}-{variant}");
                Tick(busy, Buttons.Forward, 24);
                Sector sector = busy.Map.Sectors[4];
                bool valid = variant switch
                {
                    "door-busy" => sector.DoorDirection != 0 && sector.Ceiling > 128 && busy.Map.Lines[8].Special == special,
                    "floor-busy" => sector.FloorDirection != 0 && sector.Ceiling == 128 && busy.Map.Lines[8].Special == special,
                    "crusher-door" => sector.DoorDirection == 0 && sector.Ceiling < 128,
                    _ => sector.FloorDirection == 0 && sector.Floor == 0 && busy.Map.Lines[8].Special == 58
                };
                Check($"crusher-{special}-{variant}-ownership", valid,
                    new { sector.Ceiling, sector.Floor, sector.DoorDirection, sector.FloorDirection });
            }
        }
        foreach (int special in new[] { 7, 8, 100, 127 })
        {
            bool walk = special is 8 or 100, turbo = special is 100 or 127;
            int step = turbo ? 16 : 8;
            void ActivateStairs(Game game)
            {
                if (walk) Tick(game, Buttons.Forward, 8);
                else Tick(game, Buttons.Use);
            }
            foreach (string variant in new[] { "chain", "texture-stop", "reversed", "no-tag", "zero-tag",
                "multiple", "raised-start" })
            {
                Game game = Load($"stairs-{special}-{variant}");
                ActivateStairs(game);
                bool targeted = variant is not ("no-tag" or "zero-tag");
                int start = variant == "raised-start" ? 16 : 0;
                double[] expected = !targeted ? [0, 0, 0] :
                    variant is "texture-stop" or "reversed" ? [step, 0, 0] :
                    variant == "multiple" ? [step, 2 * step, step] :
                    [start + step, start + 2 * step, start + 3 * step];
                Check($"stairs-{special}-{variant}-targets",
                    game.Map.Sectors.Skip(2).Take(3).Select(s => s.FloorTarget).SequenceEqual(expected),
                    game.Map.Sectors.Select(s => s.FloorTarget).ToArray());
                Check($"stairs-{special}-{variant}-consumption", game.Map.Lines[8].Special == (targeted ? 0 : special));
                if (!targeted)
                    Check($"stairs-{special}-{variant}-diagnostic", game.Message == "STAIRS HAVE NO TAGGED SECTOR", game.Message);
                Tick(game, count: 220);
                Check($"stairs-{special}-{variant}-settled",
                    game.Map.Sectors.Skip(2).Take(3).Select(s => s.Floor).SequenceEqual(expected) &&
                    game.Map.Sectors.All(s => s.FloorDirection == 0), game.Map.Sectors.Select(s => s.Floor).ToArray());
                if (variant == "chain")
                {
                    Tick(game, Buttons.Forward, 70);
                    Check($"stairs-{special}-traversal", game.Position.X > 224, game.Position);
                    Tick(game, Buttons.Restart);
                    Check($"stairs-{special}-restart", game.Map.Sectors.Take(5).All(s => s.Floor == 0) &&
                        game.Map.Sectors[5].Floor == (turbo ? 64 : 48) && game.Map.Lines[8].Special == special);
                }
            }
            Game cadence = Load($"stairs-{special}-chain");
            ActivateStairs(cadence);
            double before = cadence.Map.Sectors[2].Floor;
            Tick(cadence);
            double delta = cadence.Map.Sectors[2].Floor - before;
            Check($"stairs-{special}-speed", Math.Abs(delta - (turbo ? 4 : .25)) < .000001, delta);
            Game wrong = Load($"stairs-{special}-chain");
            if (walk) Tick(wrong, Buttons.Use);
            else Tick(wrong, Buttons.Forward, 8);
            Check($"stairs-{special}-wrong-trigger", wrong.Map.Sectors.All(s => s.FloorDirection == 0) && wrong.Map.Lines[8].Special == special);
            Game back = Load($"stairs-{special}-back-side");
            ActivateStairs(back);
            Check($"stairs-{special}-back-side", (back.Map.Sectors[2].FloorDirection != 0) == walk &&
                back.Map.Lines[8].Special == (walk ? 0 : special));
            foreach (bool seed in new[] { true, false })
            {
                Game busy = Load($"stairs-{special}-busy-{(seed ? "seed" : "next")}");
                Tick(busy, Buttons.Forward, 15);
                double target = busy.Map.Sectors[seed ? 2 : 3].FloorTarget;
                ActivateStairs(busy);
                Check($"stairs-{special}-busy-{(seed ? "seed" : "next")}",
                    target == 24 && busy.Map.Sectors[seed ? 2 : 3].FloorTarget == target &&
                    busy.Map.Sectors[4].FloorDirection == 0 && busy.Map.Lines[8].Special == (seed ? special : 0),
                    new { busy.Position, targets = busy.Map.Sectors.Select(s => s.FloorTarget).ToArray(), action = busy.Map.Lines[8].Special });
                if (seed)
                {
                    Tick(busy, count: 35);
                    if (walk)
                    {
                        Tick(busy, Buttons.Back, 8);
                        ActivateStairs(busy);
                    }
                    else { Tick(busy); ActivateStairs(busy); }
                    Check($"stairs-{special}-busy-seed-retry", busy.Map.Sectors[2].FloorTarget == 24 + step && busy.Map.Lines[8].Special == 0,
                        new { busy.Position, target = busy.Map.Sectors[2].FloorTarget });
                }
            }
            if (!walk)
            {
                Game blocked = Load($"stairs-{special}-blocked");
                Tick(blocked, Buttons.Forward, 4);
                Tick(blocked, Buttons.Use);
                Tick(blocked, count: 220);
                Check($"stairs-{special}-occupied-headroom", blocked.Health == 100 && blocked.Feet <= 4 &&
                    blocked.Map.Sectors[2].FloorDirection == 1, new { blocked.Feet, blocked.Health });
                Tick(blocked, Buttons.Back, 16);
                Tick(blocked, count: 220);
                Check($"stairs-{special}-unblocked-resumes", blocked.Map.Sectors[2].Floor == step && blocked.Map.Sectors[2].FloorDirection == 0);
            }
        }
        // --- Music: MUS/MIDI pitch-bend translation to semitones ---
        Check("music-mus-bend-center", Math.Abs(Instruments.MusPitchBendSemitones(64)) < 1e-9, Instruments.MusPitchBendSemitones(64));
        Check("music-mus-bend-min", Math.Abs(Instruments.MusPitchBendSemitones(0) - -2.0) < 1e-9, Instruments.MusPitchBendSemitones(0));
        double expectedMusMax = (127 - 64) / 64.0 * 2.0;
        Check("music-mus-bend-max", Math.Abs(Instruments.MusPitchBendSemitones(127) - expectedMusMax) < 1e-9, Instruments.MusPitchBendSemitones(127));
        Check("music-midi-bend-center", Math.Abs(Instruments.MidiPitchBendSemitones(0, 64)) < 1e-9, Instruments.MidiPitchBendSemitones(0, 64));
        Check("music-midi-bend-min", Math.Abs(Instruments.MidiPitchBendSemitones(0, 0) - -2.0) < 1e-9, Instruments.MidiPitchBendSemitones(0, 0));
        double expectedMidiMax = (16383 - 8192) / 8192.0 * 2.0;
        Check("music-midi-bend-max", Math.Abs(Instruments.MidiPitchBendSemitones(127, 127) - expectedMidiMax) < 1e-9, Instruments.MidiPitchBendSemitones(127, 127));

        // --- Music: GM-program-aware voicing produces measurably different synthesis parameters ---
        Instruments.Voice piano = Instruments.ForProgram(0), organ = Instruments.ForProgram(20), guitar = Instruments.ForProgram(28),
            brass = Instruments.ForProgram(60), synth = Instruments.ForProgram(100);
        Check("music-voice-classify", Instruments.Classify(0) == Instruments.Family.Percussive && Instruments.Classify(20) == Instruments.Family.Sustained &&
            Instruments.Classify(28) == Instruments.Family.Plucked && Instruments.Classify(60) == Instruments.Family.Bright &&
            Instruments.Classify(100) == Instruments.Family.Synth,
            new { piano = Instruments.Classify(0), organ = Instruments.Classify(20), guitar = Instruments.Classify(28),
                brass = Instruments.Classify(60), synth = Instruments.Classify(100) });
        Check("music-voice-distinct-harmonics", !piano.Harmonics.SequenceEqual(organ.Harmonics) && !piano.Harmonics.SequenceEqual(guitar.Harmonics) &&
            !organ.Harmonics.SequenceEqual(brass.Harmonics) && !guitar.Harmonics.SequenceEqual(synth.Harmonics));
        Check("music-voice-distinct-envelope", organ.DecaySeconds > guitar.DecaySeconds && double.IsPositiveInfinity(organ.DecaySeconds) &&
            guitar.PluckOvershoot > 0 && piano.PluckOvershoot == 0 && organ.SustainLevel > piano.SustainLevel,
            new { organDecayIsInfinite = double.IsPositiveInfinity(organ.DecaySeconds), guitarDecay = guitar.DecaySeconds,
                guitarOvershoot = guitar.PluckOvershoot, pianoOvershoot = piano.PluckOvershoot,
                organSustain = organ.SustainLevel, pianoSustain = piano.SustainLevel });

        // --- Music: a full MUS-to-internal-event conversion round trip for a sample tune with a bend ---
        byte[] mus =
        [
            (byte)'M', (byte)'U', (byte)'S', 0x1A,
            12, 0, 16, 0,           // scoreLength=12, scoreStart=16
            1, 0, 0, 0, 0, 0, 0, 0, // channel/instrument header fields (unused by the parser)
            0x40, 0x00, 0x18,       // controller change ch0: patch (controller 0) = 24 (guitar/plucked)
            0x10, 0xBC, 0x64,       // play note ch0: note=60, has-volume bit set, velocity=100
            0xA0, 0x60, 0x46,       // pitch-bend ch0: value=96 (centered=64, so +1 semitone), then delta=70 ticks
            0x00, 0x3C,             // release note ch0: note=60
            0x60,                   // score end
        ];
        (List<Music.NoteSpan> musNotes, double musDuration) = Music.ParseMusForTest(mus);
        double expectedBend = Instruments.MusPitchBendSemitones(96);
        double expectedFrequency = 440.0 * Math.Pow(2, (60 - 69) / 12.0) * Math.Pow(2, expectedBend / 12.0);
        Check("music-mus-roundtrip-count", musNotes.Count == 1, musNotes.Count);
        if (musNotes.Count == 1)
        {
            Music.NoteSpan note = musNotes[0];
            Check("music-mus-roundtrip-timing", Math.Abs(note.Start) < 1e-9 && Math.Abs(note.End - 70 / 140.0) < 1e-9, new { note.Start, note.End });
            Check("music-mus-roundtrip-instrument", note.Instrument == 24 && !note.Percussion, new { note.Instrument, note.Percussion });
            Check("music-mus-roundtrip-bend", Math.Abs(note.Bend - expectedBend) < 1e-9, new { note.Bend, expectedBend });
            Check("music-mus-roundtrip-frequency", Math.Abs(note.Frequency - expectedFrequency) < 1e-6, new { note.Frequency, expectedFrequency });
        }
        Check("music-mus-roundtrip-duration", Math.Abs(musDuration - 70 / 140.0) < 1e-9, musDuration);

        // --- WAD/Assets: a malformed DS* sound lump with a pathologically small sample rate must be rejected
        // cleanly instead of overflowing the 32-bit resampled-length computation into a negative array size
        // (observed pre-fix as an uncaught OverflowException instead of the usual InvalidDataException). ---
        {
            byte[] overflowSound = BuildDmxSound(rate: 1, sampleCount: 100_000);
            byte[] badRateWad = BuildMinimalWad([("PLAYPAL", new byte[768]), ("PNAMES", Pack4(0)), ("DSTEST", overflowSound)]);
            Assets assets = new(new Wad(badRateWad), "MAP01");
            InvalidDataException? caught = null;
            try { assets.Sound("TEST"); } catch (InvalidDataException ex) { caught = ex; }
            Check("wad-sound-reject-overflowing-rate", caught != null, caught?.Message);
        }

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
        foreach (string name in new[] { "cyber-exit", "cyber-two", "cyber-wrong-map", "cyber-wrong-monster",
            "spider-exit", "spider-wrong-map", "spider-wrong-monster" })
        {
            Game game = Load("boss-" + name); Tick(game);
            foreach (Actor boss in game.Actors.Where(a => a.Enemy)) boss.Health = 1;
            Tick(game, Buttons.Fire, 70);
            bool shouldExit = name is "cyber-exit" or "spider-exit";
            Check("boss-" + name + "-first-kill", game.Finished == shouldExit && game.Kills == 1,
                new { game.Map.Name, game.Kills, game.Finished, game.Health });
            if (name == "cyber-two")
            {
                Actor remaining = game.Actors.Single(a => a.Enemy && a.Health > 0);
                double angle = Math.Atan2(remaining.Position.Y - game.Position.Y, remaining.Position.X - game.Position.X);
                game.Tick(new Input { MouseX = (game.Angle - angle) / .003 }, 1.0 / 35);
                Tick(game, Buttons.Fire, 70);
                Check("boss-cyber-two-final-kill", game.Kills == 2 && game.Finished &&
                    game.CompletionPrompt == "END OF AVAILABLE MAPS - R TO REPLAY",
                    new { game.Kills, game.Finished, game.CompletionPrompt });
            }
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
        {
            Game game = Load("rocket-pickup"); Tick(game);
            void Press(Buttons b) { Tick(game, b); Tick(game); }
            Press(Buttons.Menu);
            Check("menu-open", game.MenuOpen && game.MenuScreen == MenuScreen.Main && game.MenuIndex == 0,
                new { game.MenuOpen, game.MenuScreen, game.MenuIndex });
            double timeAtOpen = game.Time;
            Tick(game, count: 10);
            Check("menu-pauses-simulation", game.Time == timeAtOpen, new { game.Time, timeAtOpen });
            Press(Buttons.Back);
            Check("menu-navigate-down", game.MenuIndex == 1, game.MenuIndex);
            Press(Buttons.Forward);
            Check("menu-navigate-up", game.MenuIndex == 0, game.MenuIndex);
            Press(Buttons.Back); Press(Buttons.Back); Press(Buttons.Use);
            Check("menu-enter-options", game.MenuScreen == MenuScreen.Options && game.MenuIndex == 0,
                new { game.MenuScreen, game.MenuIndex });
            double sensitivity = game.MouseSensitivity;
            Press(Buttons.TurnRight);
            Check("menu-sensitivity-increase", game.MouseSensitivity > sensitivity, new { sensitivity, game.MouseSensitivity });
            Press(Buttons.TurnLeft);
            Check("menu-sensitivity-decrease", Math.Abs(game.MouseSensitivity - sensitivity) < 1e-9, new { sensitivity, game.MouseSensitivity });
            Press(Buttons.Back); Press(Buttons.Use);
            Check("menu-messages-toggle", !game.MessagesOn, game.MessagesOn);
            Press(Buttons.Back); Press(Buttons.Use);
            Check("menu-options-back", game.MenuScreen == MenuScreen.Main && game.MenuIndex == 0,
                new { game.MenuScreen, game.MenuIndex });
            Press(Buttons.Back); Press(Buttons.Back); Press(Buttons.Back); Press(Buttons.Use);
            Check("menu-enter-quit-confirm", game.MenuScreen == MenuScreen.QuitConfirm && game.MenuIndex == 1,
                new { game.MenuScreen, game.MenuIndex });
            Press(Buttons.Use);
            Check("menu-quit-no-returns", game.MenuScreen == MenuScreen.Main && game.MenuIndex == 0 && !game.Finished,
                new { game.MenuScreen, game.MenuIndex });
            Press(Buttons.Menu);
            Check("menu-toggle-closes", !game.MenuOpen, game.MenuOpen);
            Press(Buttons.Menu);
            Press(Buttons.Back); Press(Buttons.Back); Press(Buttons.Back); Press(Buttons.Use);
            Press(Buttons.Forward);
            Check("menu-quit-confirm-select-yes", game.MenuIndex == 0, game.MenuIndex);
            Input quit = new() { Held = Buttons.Use };
            game.Tick(quit, 1.0 / 35);
            Check("menu-quit-yes-sets-input-quit", quit.Quit, quit.Quit);
        }
        {
            // Full intermission: a keycard and an armor bonus on the walked path (both collected), a
            // secret sector (found on spawn) and no monsters. Percentages should all read 100%.
            Game game = Load("intermission-full"); Tick(game);
            Tick(game, Buttons.Forward, 50); Tick(game, Buttons.Use);
            Check("intermission-full-finished", game.Finished && game.Items == 2 && game.TotalItems == 2 &&
                game.Secrets == 1 && game.TotalSecrets == 1 && game.Kills == 0 && game.TotalEnemies == 0,
                new { game.Items, game.TotalItems, game.Secrets, game.TotalSecrets, game.Kills, game.TotalEnemies });
            Check("intermission-full-stats-text", game.IntermissionStats.Length == 4 &&
                game.IntermissionStats[1] == "KILLS 100%  ITEMS 100%  SECRETS 100%" &&
                game.IntermissionStats[^1] == game.CompletionPrompt, game.IntermissionStats);
        }
        {
            // Partial intermission: one of two items is off the walked path (uncollected), no secret
            // sector. Verifies non-trivial rounding (50%) and the zero-total-secrets 100% convention.
            Game game = Load("intermission-partial"); Tick(game);
            Tick(game, Buttons.Forward, 50); Tick(game, Buttons.Use);
            Check("intermission-partial-finished", game.Finished && game.Items == 1 && game.TotalItems == 2 &&
                game.Secrets == 0 && game.TotalSecrets == 0,
                new { game.Items, game.TotalItems, game.Secrets, game.TotalSecrets });
            Check("intermission-partial-stats-text",
                game.IntermissionStats[1] == "KILLS 100%  ITEMS 50%  SECRETS 100%", game.IntermissionStats);
        }
        {
            // Empty intermission: no items, no secrets, no monsters at all; every zero-total percentage
            // must still read 100% (matching the original's "nothing required" convention), never a crash.
            Game game = Load("intermission-empty"); Tick(game);
            Tick(game, Buttons.Forward, 50); Tick(game, Buttons.Use);
            Check("intermission-empty-finished", game.Finished && game.Items == 0 && game.TotalItems == 0 &&
                game.Secrets == 0 && game.TotalSecrets == 0 && game.Kills == 0 && game.TotalEnemies == 0,
                new { game.Items, game.TotalItems, game.Secrets, game.TotalSecrets });
            Check("intermission-empty-stats-text",
                game.IntermissionStats[1] == "KILLS 100%  ITEMS 100%  SECRETS 100%", game.IntermissionStats);
        }
        foreach (int type in new[] { 2022, 2023, 2024, 2025, 2026, 2045 })
        {
            Game game = Load($"powerup-{type}"); Tick(game);
            bool active = type switch
            {
                2022 => game.InvulnerabilityTime > 0,
                2023 => game.HasBerserk,
                2024 => game.InvisibilityTime > 0,
                2025 => game.RadiationSuitTime > 0,
                2026 => game.HasComputerMap,
                2045 => game.LightAmpTime > 0,
                _ => false
            };
            Check($"powerup-{type}-pickup", active && game.Items == 1 && game.TotalItems == 1,
                new { active, game.Items, game.TotalItems });
            string save = game.Save();
            bool restored = game.TryLoad(save, out string? error);
            Check($"powerup-{type}-save-reset", restored && game.InvulnerabilityTime == 0 &&
                game.InvisibilityTime == 0 && game.RadiationSuitTime == 0 && game.LightAmpTime == 0 &&
                !game.HasComputerMap && !game.HasBerserk, new { restored, error });
        }
        foreach (string kind in new[] { "control", "invulnerability", "radsuit" })
        {
            Game game = Load("powerup-floor-" + kind);
            Tick(game, count: 40);
            Check($"powerup-floor-{kind}", kind == "control" ? game.Health < 100 : game.Health == 100,
                game.Health);
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
        {
            // Chase memory: a demon that starts in sight of the player keeps closing even after the
            // player outruns its sight range, instead of freezing the instant it can no longer see them,
            // then settles back down to idle once the memory window has run out with sight never regained.
            Game game = Load("chase-memory");
            Actor demon = game.Actors.Single(a => a.Type == 3002);
            Tick(game); // Confirm the demon starts out actively tracking the player while in sight.
            Check("chase-memory-sighted", demon.ChaseMemory > 0 && demon.LastKnownTargetPosition != null,
                new { demon.ChaseMemory, demon.LastKnownTargetPosition });
            // Run the player away (melee-only demon is slower) one tic at a time until the very first
            // moment sight is lost (ChaseMemory starts ticking down from its full value); separation must
            // have cleared the engine's sight range by then.
            int tics = 0;
            for (; tics < 2000 && demon.ChaseMemory >= 3; tics++) Tick(game, Buttons.Run | Buttons.Forward);
            double distanceAtLoss = (game.Position - demon.Position).Length;
            Vec afterLoss = demon.Position;
            Check("chase-memory-exceeds-sight-range", distanceAtLoss > 1500 && demon.ChaseMemory is > 0 and < 3,
                new { tics, distanceAtLoss, demon.ChaseMemory });
            // Keep the player running away (never idle) so it cannot coincidentally wander back within
            // sight range; while the memory window is still open the demon should keep closing on the
            // last-known spot rather than stand still, even though it genuinely cannot see the player.
            Tick(game, Buttons.Run | Buttons.Forward, 20);
            Check("chase-memory-continues-after-sight-lost", demon.Position != afterLoss && demon.ChaseMemory > 0,
                new { afterLoss, demon.Position, demon.ChaseMemory });
            Vec duringMemory = demon.Position;
            // Once the memory window has fully expired without ever re-spotting the player, the demon
            // settles back to idle: it stops advancing and no longer repeats stale movement forever.
            Tick(game, Buttons.Run | Buttons.Forward, 150);
            Check("chase-memory-expires", demon.ChaseMemory == 0, new { demon.ChaseMemory });
            Vec afterExpiry = demon.Position;
            Tick(game, Buttons.Run | Buttons.Forward, 20);
            Check("chase-memory-idle-after-expiry", demon.Position == afterExpiry && demon.ChaseMemory == 0,
                new { duringMemory, afterExpiry, demon.Position });
        }
        {
            // A monster beyond a thin wall (no direct line of sight) but reachable through an open
            // connecting sector (modeling an open door) wakes up and starts moving toward the player once
            // gunfire propagates through that connection, even though it still cannot see the player.
            Game game = Load("noise-propagation-wakes-through-open-door");
            Actor monster = game.Actors.Single(a => a.Type == 3004);
            Vec initial = monster.Position;
            Check("noise-propagation-dormant-before-shot", !monster.Awake, new { monster.Awake });
            Tick(game, Buttons.Fire);
            Tick(game, count: 20);
            Check("noise-propagation-wakes-through-open-door", monster.Awake && monster.Position != initial,
                new { monster.Awake, initial, monster.Position });
        }
        {
            // The same geometry, but the connecting sector's door is shut (floor == ceiling, no gap):
            // Game.NoiseAlert's flood cannot cross it, so the monster never hears the shot and stays put.
            Game game = Load("noise-propagation-blocked-by-closed-door");
            Actor monster = game.Actors.Single(a => a.Type == 3004);
            Vec initial = monster.Position;
            Tick(game, Buttons.Fire);
            Tick(game, count: 20);
            Check("noise-propagation-blocked-by-closed-door", !monster.Awake && monster.Position == initial,
                new { monster.Awake, initial, monster.Position });
        }
        {
            // Save-game compatibility: a monster that was blindly chasing a propagated noise (Awake plus
            // its remembered AlertPosition) must keep doing so after a save/load cycle, not silently
            // revert to dormant the instant the save is restored (the per-actor state TryLoad previously
            // reapplied only covered Removed/Health, so this alert state used to be dropped on load).
            Game before = Load("noise-propagation-wakes-through-open-door");
            Tick(before, Buttons.Fire);
            Tick(before, count: 20);
            Actor awakened = before.Actors.Single(a => a.Type == 3004);
            Check("savegame-compat-awake-before-save", awakened.Awake && awakened.AlertPosition != null,
                new { awakened.Awake, awakened.AlertPosition });
            string json = before.Save();
            Vec positionBeforeSave = awakened.Position;

            Game after = Load("noise-propagation-wakes-through-open-door");
            bool loaded = after.TryLoad(json, out string? error);
            Actor restored = after.Actors.Single(a => a.Type == 3004);
            Check("savegame-compat-awake-restored", loaded && restored.Awake && restored.AlertPosition == awakened.AlertPosition,
                new { loaded, error, restoredAwake = restored.Awake, restoredAlert = restored.AlertPosition, savedAlert = awakened.AlertPosition });

            // The restored monster must keep closing on the remembered alert position on its own (no
            // fresh noise, no line of sight yet) rather than sitting idle because its wake state never
            // actually came back.
            Tick(after, count: 10);
            Check("savegame-compat-awake-continues-after-load", restored.Position != positionBeforeSave,
                new { positionBeforeSave, restored.Position });

            // A version-1/2 save (no ActorAwake/ActorAlertX/ActorAlertY fields at all) must still load
            // cleanly, with every actor simply defaulting to "not awake" — the pre-existing behavior.
            string legacyJson = Regex.Replace(json, @",""ActorAwake"":\[[^\]]*\],""ActorAlertX"":\[[^\]]*\],""ActorAlertY"":\[[^\]]*\]", "");
            legacyJson = Regex.Replace(legacyJson, "\"Version\":3", "\"Version\":2");
            Game legacy = Load("noise-propagation-wakes-through-open-door");
            bool legacyLoaded = legacy.TryLoad(legacyJson, out string? legacyError);
            Actor legacyActor = legacy.Actors.Single(a => a.Type == 3004);
            Check("savegame-compat-legacy-version-defaults-dormant",
                legacyLoaded && !legacyActor.Awake && legacyActor.AlertPosition == null,
                new { legacyLoaded, legacyError, legacyActor.Awake, legacyActor.AlertPosition, legacyJson });
        }
        {
            // Save-game compatibility: a demon that lost direct sight of the player but is still inside
            // its chase-memory window (ChaseMemory > 0, LastKnownTargetPosition set — see Game.UpdateActor)
            // must keep pursuing that remembered spot after a save/load cycle, not silently revert to idle
            // the instant the save is restored (ChaseMemory/LastKnownTargetPosition previously were not
            // part of SaveGameState at all).
            Game before = Load("chase-memory");
            Actor demon = before.Actors.Single(a => a.Type == 3002);
            Tick(before); // Confirm the demon starts out actively tracking the player while in sight.
            // Run the player away until sight is just lost (ChaseMemory starts ticking down from its full
            // value), matching the "chase-memory-exceeds-sight-range" scenario above.
            for (int tics = 0; tics < 2000 && demon.ChaseMemory >= 3; tics++) Tick(before, Buttons.Run | Buttons.Forward);
            Check("savegame-compat-chase-before-save",
                demon.ChaseMemory is > 0 and < 3 && demon.LastKnownTargetPosition != null,
                new { demon.ChaseMemory, demon.LastKnownTargetPosition });
            string json = before.Save();
            Vec rememberedBeforeSave = demon.LastKnownTargetPosition!.Value;
            double chaseMemoryBeforeSave = demon.ChaseMemory;

            Game after = Load("chase-memory");
            bool loaded = after.TryLoad(json, out string? error);
            Actor restored = after.Actors.Single(a => a.Type == 3002);
            Check("savegame-compat-chase-restored",
                loaded && restored.ChaseMemory == chaseMemoryBeforeSave && restored.LastKnownTargetPosition == rememberedBeforeSave,
                new { loaded, error, restored.ChaseMemory, restored.LastKnownTargetPosition, chaseMemoryBeforeSave, rememberedBeforeSave });

            // The restored demon must keep closing on the remembered spot on its own (player stays put, no
            // fresh sight) rather than freezing because its chase memory never actually came back.
            Vec positionAfterLoad = restored.Position;
            Tick(after, count: 5);
            Check("savegame-compat-chase-continues-after-load", restored.Position != positionAfterLoad,
                new { positionAfterLoad, restored.Position });
        }
        {
            // Save-game compatibility: a monster mid-infight (Target points at another monster, see
            // Game.Hurt) must keep fighting that rival after a save/load cycle instead of silently
            // reverting to "fight the player" (Target previously was not part of SaveGameState at all, so
            // TryLoad always left every restored actor's Target at its freshly-spawned null default).
            Game before = Load("infight-retarget");
            Tick(before, count: 150);
            Actor imp = before.Actors.Single(a => a.Type == 3001);
            Actor demon = before.Actors.Single(a => a.Type == 3002);
            Check("savegame-compat-infight-before-save", demon.Target == imp, new { target = demon.Target == imp ? "imp" : "other" });
            string json = before.Save();

            Game after = Load("infight-retarget");
            bool loaded = after.TryLoad(json, out string? error);
            Actor restoredImp = after.Actors.Single(a => a.Type == 3001);
            Actor restoredDemon = after.Actors.Single(a => a.Type == 3002);
            Check("savegame-compat-infight-restored", loaded && restoredDemon.Target == restoredImp,
                new { loaded, error, target = restoredDemon.Target == restoredImp ? "imp" : "other" });

            // The restored demon must keep closing on the imp on its own (no fresh hit needed) rather than
            // idling or re-acquiring the player because its infighting target never actually came back; a
            // shrinking separation confirms it is still actively pursuing restoredImp, not Position (the
            // player).
            double distanceAfterLoad = (restoredDemon.Position - restoredImp.Position).Length;
            Tick(after, count: 30);
            double distanceAfterChase = (restoredDemon.Position - restoredImp.Position).Length;
            Check("savegame-compat-infight-continues-after-load",
                restoredDemon.Target == restoredImp && distanceAfterChase < distanceAfterLoad,
                new { restoredDemon.Target, distanceAfterLoad, distanceAfterChase });
        }
        {
            // A version-1/2/3 save (no ActorChaseMemory/ActorLastKnownTargetX/ActorLastKnownTargetY/
            // ActorTargetIndex fields at all) must still load cleanly, with every actor simply defaulting
            // to "no chase memory, no infighting target" — the pre-existing behavior.
            Game before = Load("infight-retarget");
            Tick(before, count: 150);
            string json = before.Save();
            string legacyJson = Regex.Replace(json,
                @",""ActorChaseMemory"":\[[^\]]*\],""ActorLastKnownTargetX"":\[[^\]]*\],""ActorLastKnownTargetY"":\[[^\]]*\],""ActorTargetIndex"":\[[^\]]*\]",
                "");
            legacyJson = Regex.Replace(legacyJson, "\"Version\":4", "\"Version\":3");
            Game legacy = Load("infight-retarget");
            bool legacyLoaded = legacy.TryLoad(legacyJson, out string? legacyError);
            Actor legacyDemon = legacy.Actors.Single(a => a.Type == 3002);
            Check("savegame-compat-legacy-version-defaults-no-chase-or-target",
                legacyLoaded && legacyDemon.ChaseMemory == 0 && legacyDemon.LastKnownTargetPosition == null && legacyDemon.Target == null,
                new { legacyLoaded, legacyError, legacyDemon.ChaseMemory, legacyDemon.LastKnownTargetPosition, legacyDemon.Target, legacyJson });
        }
        {
            // Regression: Game.Save() previously threw ArgumentException for any save containing a resting
            // actor (no noise-alert position, no chase memory — the overwhelmingly common case), because
            // ActorAlertX/ActorAlertY/ActorLastKnownTargetX/ActorLastKnownTargetY used a raw double.NaN
            // "no value" sentinel that System.Text.Json's default writer rejects. They now encode "no
            // value" as an ordinary nullable-array null element instead, so saving and loading a fully idle
            // game must not throw and must round-trip to the same idle state.
            Game before = Load("infight-retarget");
            Actor restingDemon = before.Actors.Single(a => a.Type == 3002);
            Check("savegame-compat-resting-before-save",
                !restingDemon.Awake && restingDemon.AlertPosition == null && restingDemon.ChaseMemory == 0 &&
                restingDemon.LastKnownTargetPosition == null && restingDemon.Target == null,
                new { restingDemon.Awake, restingDemon.AlertPosition, restingDemon.ChaseMemory, restingDemon.LastKnownTargetPosition, restingDemon.Target });

            string? json = null; Exception? thrown = null;
            try { json = before.Save(); } catch (Exception ex) { thrown = ex; }
            Check("savegame-compat-resting-save-does-not-throw", thrown == null && json != null, thrown?.ToString());

            Game after = Load("infight-retarget");
            string? error = null;
            bool loaded = json != null && after.TryLoad(json, out error);
            Actor restoredDemon = after.Actors.Single(a => a.Type == 3002);
            Check("savegame-compat-resting-restored",
                loaded && !restoredDemon.Awake && restoredDemon.AlertPosition == null && restoredDemon.ChaseMemory == 0 &&
                restoredDemon.LastKnownTargetPosition == null && restoredDemon.Target == null,
                new { loaded, error, restoredDemon.Awake, restoredDemon.AlertPosition, restoredDemon.ChaseMemory, restoredDemon.LastKnownTargetPosition, restoredDemon.Target });
        }
        {
            // Gameplay-critical fields (PositionX/Angle/Feet/Time) are not covered by the narrow nullable
            // "no position" sentinel used for per-actor alert/chase-memory coordinates, so a save with a
            // non-finite value in one of them (hand-edited or otherwise corrupted) must be rejected during
            // load rather than silently accepted.
            Game before = Load("infight-retarget");
            Tick(before, count: 10);
            string json = before.Save();
            foreach (string field in new[] { "PositionX", "Angle", "Feet", "Time" })
            {
                string corrupted = Regex.Replace(json, $@"""{field}"":-?[0-9.eE+-]+", $@"""{field}"":NaN");
                Game target = Load("infight-retarget");
                bool loaded = target.TryLoad(corrupted, out string? error);
                Check($"savegame-reject-non-finite-{field}", !loaded && error != null, new { loaded, error, corrupted });
            }
        }
        {
            // ActorTargetIndex values other than the documented -1 "no target" sentinel (fight the player)
            // or a valid in-range actor index are corrupt save data and must reject the whole load, not
            // silently clamp to "no target" as a prior version of this fix did for any negative value or
            // any out-of-range index.
            Game before = Load("infight-retarget");
            Tick(before, count: 150);
            string json = before.Save();
            int actorCount = before.Actors.Count;

            string allNoTargetJson = Regex.Replace(json, @"""ActorTargetIndex"":\[[^\]]*\]",
                $@"""ActorTargetIndex"":[{string.Join(',', Enumerable.Repeat("-1", actorCount))}]");
            Game allNoTargetGame = Load("infight-retarget");
            bool allNoTargetLoaded = allNoTargetGame.TryLoad(allNoTargetJson, out string? allNoTargetError);
            Check("savegame-target-index-minus-one-valid",
                allNoTargetLoaded && allNoTargetGame.Actors.All(a => a.Target == null),
                new { allNoTargetLoaded, allNoTargetError });

            string negativeIndices = string.Join(',', Enumerable.Range(0, actorCount).Select(i => i == 0 ? "-2" : "-1"));
            string invalidNegativeJson = Regex.Replace(json, @"""ActorTargetIndex"":\[[^\]]*\]", $@"""ActorTargetIndex"":[{negativeIndices}]");
            Game invalidNegativeGame = Load("infight-retarget");
            bool invalidNegativeLoaded = invalidNegativeGame.TryLoad(invalidNegativeJson, out string? invalidNegativeError);
            Check("savegame-reject-target-index-invalid-negative", !invalidNegativeLoaded && invalidNegativeError != null,
                new { invalidNegativeLoaded, invalidNegativeError });

            string outOfRangeIndices = string.Join(',', Enumerable.Range(0, actorCount).Select(i => i == 0 ? actorCount.ToString() : "-1"));
            string outOfRangeJson = Regex.Replace(json, @"""ActorTargetIndex"":\[[^\]]*\]", $@"""ActorTargetIndex"":[{outOfRangeIndices}]");
            Game outOfRangeGame = Load("infight-retarget");
            bool outOfRangeLoaded = outOfRangeGame.TryLoad(outOfRangeJson, out string? outOfRangeError);
            Check("savegame-reject-target-index-out-of-range", !outOfRangeLoaded && outOfRangeError != null,
                new { outOfRangeLoaded, outOfRangeError });
            // A valid in-range index (not -1) resolving to the correct actor is already covered by
            // savegame-compat-infight-restored above.
        }
        {
            // TryLoad's contract: a malformed save must leave an already-in-progress game completely
            // unchanged, not partially mutated (e.g. Map/Actors already rebuilt for the save's target map
            // before validation caught the problem). Start a game, advance it, then attempt to load a save
            // with a corrupt (out-of-range) ActorTargetIndex into that same in-progress game, and assert
            // every observable field is identical afterward.
            Game game = Load("infight-retarget");
            Tick(game, count: 50);
            string beforeMapName = game.Map.Name;
            Vec beforePosition = game.Position; double beforeAngle = game.Angle, beforeFeet = game.Feet;
            int beforeHealth = game.Health, beforeArmor = game.Armor, beforeBullets = game.Bullets,
                beforeShells = game.Shells, beforeRockets = game.Rockets, beforeKeys = game.Keys,
                beforeKills = game.Kills, beforeSecrets = game.Secrets, beforeItems = game.Items;
            double beforeTime = game.Time;
            var beforeActors = game.Actors.Select(a => (a.Position, a.Health, a.Removed, a.Awake, a.AlertPosition,
                a.ChaseMemory, a.LastKnownTargetPosition, Target: a.Target == null ? -1 : game.Actors.IndexOf(a.Target))).ToList();

            string validJson = game.Save();
            int n = game.Actors.Count;
            string indices = string.Join(',', Enumerable.Range(0, n).Select(i => i == 0 ? n.ToString() : "-1"));
            string corruptJson = Regex.Replace(validJson, @"""ActorTargetIndex"":\[[^\]]*\]", $@"""ActorTargetIndex"":[{indices}]");
            bool loaded = game.TryLoad(corruptJson, out string? error);

            var afterActors = game.Actors.Select(a => (a.Position, a.Health, a.Removed, a.Awake, a.AlertPosition,
                a.ChaseMemory, a.LastKnownTargetPosition, Target: a.Target == null ? -1 : game.Actors.IndexOf(a.Target))).ToList();
            Check("savegame-reject-preserves-in-progress-game",
                !loaded && error != null &&
                game.Map.Name == beforeMapName && game.Position == beforePosition && game.Angle == beforeAngle &&
                game.Feet == beforeFeet && game.Health == beforeHealth && game.Armor == beforeArmor &&
                game.Bullets == beforeBullets && game.Shells == beforeShells && game.Rockets == beforeRockets &&
                game.Keys == beforeKeys && game.Kills == beforeKills && game.Secrets == beforeSecrets &&
                game.Items == beforeItems && game.Time == beforeTime && afterActors.SequenceEqual(beforeActors),
                new { loaded, error, beforeMapName, afterMapName = game.Map.Name });
        }
        {
            // ChaseMemory (see ChaseMemoryDuration/UpdateActor) is a countdown timer that only ever runs
            // from its full duration down to its 0 "no chase memory" sentinel/default; a negative value
            // never arises from normal play and must be rejected as malformed-save, not silently clamped.
            // Mismatched per-actor last-known-target X/Y presence (one set without its pair) must likewise
            // be rejected rather than silently dropped, and a numeric-overflow literal such as 1e999 (which
            // deserializes to double.PositiveInfinity, not NaN) must be rejected by the same finite check.
            Game before = Load("chase-memory");
            Actor demon = before.Actors.Single(a => a.Type == 3002);
            Tick(before);
            for (int tics = 0; tics < 2000 && demon.ChaseMemory >= 3; tics++) Tick(before, Buttons.Run | Buttons.Forward);
            Check("savegame-chase-memory-fixture-has-remembered-position",
                demon.ChaseMemory is > 0 and < 3 && demon.LastKnownTargetPosition != null, new { demon.ChaseMemory });
            string json = before.Save();
            int demonIndex = before.Actors.IndexOf(demon);
            SaveGameState baseline = JsonSerializer.Deserialize(json, SaveJsonContext.Default.SaveGameState)!;

            double[] invalidChaseMemory = [.. baseline.ActorChaseMemory!];
            invalidChaseMemory[demonIndex] = -1;
            string invalidChaseMemoryJson = JsonSerializer.Serialize(
                baseline with { ActorChaseMemory = invalidChaseMemory }, SaveJsonContext.Default.SaveGameState);
            Game invalidChaseMemoryGame = Load("chase-memory");
            bool invalidChaseMemoryLoaded = invalidChaseMemoryGame.TryLoad(invalidChaseMemoryJson, out string? invalidChaseMemoryError);
            Check("savegame-reject-chase-memory-invalid", !invalidChaseMemoryLoaded && invalidChaseMemoryError != null,
                new { invalidChaseMemoryLoaded, invalidChaseMemoryError });

            // The legitimate 0 "no chase memory" sentinel/default for every actor (the overwhelmingly
            // common case, already covered for a single resting actor by savegame-compat-resting-restored)
            // must still load successfully — this validation is not a new, tighter gameplay cap.
            double[] zeroChaseMemory = [.. baseline.ActorChaseMemory!.Select(_ => 0.0)];
            double?[] noTargetX = [.. baseline.ActorLastKnownTargetX!.Select(_ => (double?)null)];
            double?[] noTargetY = [.. baseline.ActorLastKnownTargetY!.Select(_ => (double?)null)];
            string zeroChaseMemoryJson = JsonSerializer.Serialize(baseline with
            {
                ActorChaseMemory = zeroChaseMemory, ActorLastKnownTargetX = noTargetX, ActorLastKnownTargetY = noTargetY
            }, SaveJsonContext.Default.SaveGameState);
            Game zeroChaseMemoryGame = Load("chase-memory");
            bool zeroChaseMemoryLoaded = zeroChaseMemoryGame.TryLoad(zeroChaseMemoryJson, out string? zeroChaseMemoryError);
            Check("savegame-chase-memory-zero-sentinel-loads",
                zeroChaseMemoryLoaded && zeroChaseMemoryGame.Actors.All(a => a.ChaseMemory == 0 && a.LastKnownTargetPosition == null),
                new { zeroChaseMemoryLoaded, zeroChaseMemoryError });

            // The demon's baseline X is non-null (remembered position just lost); pairing its X with a
            // missing Y is an impossible half-a-position.
            double?[] xWithoutY = [.. baseline.ActorLastKnownTargetY!];
            xWithoutY[demonIndex] = null;
            string xWithoutYJson = JsonSerializer.Serialize(
                baseline with { ActorLastKnownTargetY = xWithoutY }, SaveJsonContext.Default.SaveGameState);
            Game xWithoutYGame = Load("chase-memory");
            bool xWithoutYLoaded = xWithoutYGame.TryLoad(xWithoutYJson, out string? xWithoutYError);
            Check("savegame-reject-last-known-target-x-without-y", !xWithoutYLoaded && xWithoutYError != null,
                new { xWithoutYLoaded, xWithoutYError });

            double?[] yWithoutX = [.. baseline.ActorLastKnownTargetX!];
            yWithoutX[demonIndex] = null;
            string yWithoutXJson = JsonSerializer.Serialize(
                baseline with { ActorLastKnownTargetX = yWithoutX }, SaveJsonContext.Default.SaveGameState);
            Game yWithoutXGame = Load("chase-memory");
            bool yWithoutXLoaded = yWithoutXGame.TryLoad(yWithoutXJson, out string? yWithoutXError);
            Check("savegame-reject-last-known-target-y-without-x", !yWithoutXLoaded && yWithoutXError != null,
                new { yWithoutXLoaded, yWithoutXError });

            // Hand-edit the raw text (rather than going through SaveGameState/JsonSerializer.Serialize,
            // which rejects writing a non-finite double outright) so the demon's X element becomes the
            // literal overflow token "1e999", exactly as System.Text.Json would parse it from a corrupted
            // save file: as double.PositiveInfinity, not NaN.
            string overflowJson = Regex.Replace(json, @"""ActorLastKnownTargetX"":\[([^\]]*)\]", m =>
            {
                string[] elements = m.Groups[1].Value.Split(',');
                elements[demonIndex] = "1e999";
                return $@"""ActorLastKnownTargetX"":[{string.Join(',', elements)}]";
            });
            Check("savegame-chase-memory-overflow-fixture-edited", overflowJson != json, new { overflowJson, json });
            Game overflowGame = Load("chase-memory");
            bool overflowLoaded = overflowGame.TryLoad(overflowJson, out string? overflowError);
            Check("savegame-reject-last-known-target-overflow", !overflowLoaded && overflowError != null,
                new { overflowLoaded, overflowError });
        }
        {
            // (b) ActorRemoved/ActorHealth/FoundSecretSectors are declared non-nullable in SaveGameState,
            // but that is only a compile-time annotation: a hand-edited save can still carry an explicit
            // JSON null for any of them. Loading must hit the ordinary malformed-save error path, not throw
            // an unhandled NullReferenceException.
            Game before = Load("infight-retarget");
            Tick(before, count: 10);
            string json = before.Save();
            foreach (string field in new[] { "ActorRemoved", "ActorHealth", "FoundSecretSectors" })
            {
                string corrupted = Regex.Replace(json, $@"""{field}"":\[[^\]]*\]", $@"""{field}"":null");
                Game target = Load("infight-retarget");
                bool threw = false; bool loaded = false; string? error = null;
                try { loaded = target.TryLoad(corrupted, out error); }
                catch (Exception ex) { threw = true; error = ex.ToString(); }
                Check($"savegame-reject-null-{field}", !threw && !loaded && error != null, new { threw, loaded, error });
            }
        }
        {
            // Extend the existing "malformed save leaves an in-progress game untouched" contract
            // (savegame-reject-preserves-in-progress-game above) to this new null-required-array failure
            // point: it must also leave a running game's map/actor state completely unchanged rather than
            // partially rebuilding Map/Actors before the null was discovered.
            Game game = Load("infight-retarget");
            Tick(game, count: 50);
            string beforeMapName = game.Map.Name;
            Vec beforePosition = game.Position; double beforeAngle = game.Angle, beforeFeet = game.Feet;
            int beforeHealth = game.Health, beforeArmor = game.Armor;
            var beforeActors = game.Actors.Select(a => (a.Position, a.Health, a.Removed)).ToList();

            string validJson = game.Save();
            string corruptJson = Regex.Replace(validJson, @"""ActorHealth"":\[[^\]]*\]", @"""ActorHealth"":null");
            bool loaded = game.TryLoad(corruptJson, out string? error);

            var afterActors = game.Actors.Select(a => (a.Position, a.Health, a.Removed)).ToList();
            Check("savegame-reject-null-array-preserves-in-progress-game",
                !loaded && error != null && game.Map.Name == beforeMapName && game.Position == beforePosition &&
                game.Angle == beforeAngle && game.Feet == beforeFeet && game.Health == beforeHealth &&
                game.Armor == beforeArmor && afterActors.SequenceEqual(beforeActors),
                new { loaded, error, beforeMapName, afterMapName = game.Map.Name });
        }
        {
            // Save-game compatibility: an actor's live Position (mutated as it walks/chases, see
            // Game.UpdateActor) must round-trip across a save/load cycle instead of silently snapping back
            // to its map-defined spawn spot (ActorPositionX/ActorPositionY previously were not part of
            // SaveGameState at all, so TryLoad always left every restored actor at the freshly-rebuilt
            // candidate list's spawn position).
            Game before = Load("chase-memory");
            Actor demon = before.Actors.Single(a => a.Type == 3002);
            Vec spawnPosition = demon.Position;
            Tick(before); // Confirm the demon starts out actively tracking the player while in sight.
            for (int tics = 0; tics < 2000 && demon.ChaseMemory >= 3; tics++) Tick(before, Buttons.Run | Buttons.Forward);
            Vec movedPosition = demon.Position;
            Check("savegame-position-fixture-has-moved", movedPosition != spawnPosition,
                new { spawnPosition, movedPosition });
            string json = before.Save();

            Game after = Load("chase-memory");
            bool loaded = after.TryLoad(json, out string? error);
            Actor restored = after.Actors.Single(a => a.Type == 3002);
            Check("savegame-compat-position-restored", loaded && restored.Position == movedPosition,
                new { loaded, error, restored.Position, movedPosition });

            // The restored demon must keep acting from its actual (moved) position, not teleport back to
            // spawn and then move from there: continuing play for a few more tics must not reproduce the
            // original spawn-relative trajectory (the position it would be at if it had snapped back to
            // spawnPosition and resumed chasing from there is a different point than continuing on from
            // movedPosition).
            Vec positionAfterLoad = restored.Position;
            Tick(after, Buttons.Run | Buttons.Forward, count: 5);
            Check("savegame-compat-position-continues-after-load", restored.Position != positionAfterLoad,
                new { positionAfterLoad, restored.Position });

            // A version-1/2/3/4 save (no ActorPositionX/ActorPositionY fields at all) must still load
            // cleanly, with every actor defaulting to its map's deterministic spawn position — the
            // pre-existing behavior.
            string legacyJson = Regex.Replace(json, @",""ActorPositionX"":\[[^\]]*\],""ActorPositionY"":\[[^\]]*\]", "");
            legacyJson = Regex.Replace(legacyJson, "\"Version\":5", "\"Version\":4");
            Check("savegame-position-legacy-fixture-edited", legacyJson != json, new { legacyJson, json });
            Game legacy = Load("chase-memory");
            bool legacyLoaded = legacy.TryLoad(legacyJson, out string? legacyError);
            Actor legacyDemon = legacy.Actors.Single(a => a.Type == 3002);
            Check("savegame-compat-legacy-version-defaults-spawn-position",
                legacyLoaded && legacyDemon.Position == spawnPosition,
                new { legacyLoaded, legacyError, legacyDemon.Position, spawnPosition });
        }
        {
            // ActorPositionX/ActorPositionY are only optional as a whole pair (present together, or both
            // absent together); one array present without the other is an impossible half-a-position and
            // must be rejected rather than silently dropped.
            Game before = Load("infight-retarget");
            Tick(before, count: 10);
            string json = before.Save();

            string xWithoutYJson = Regex.Replace(json, @",""ActorPositionY"":\[[^\]]*\]", "");
            Game xWithoutYGame = Load("infight-retarget");
            bool xWithoutYLoaded = xWithoutYGame.TryLoad(xWithoutYJson, out string? xWithoutYError);
            Check("savegame-reject-actor-position-x-without-y", !xWithoutYLoaded && xWithoutYError != null,
                new { xWithoutYLoaded, xWithoutYError });

            string yWithoutXJson = Regex.Replace(json, @",""ActorPositionX"":\[[^\]]*\]", "");
            Game yWithoutXGame = Load("infight-retarget");
            bool yWithoutXLoaded = yWithoutXGame.TryLoad(yWithoutXJson, out string? yWithoutXError);
            Check("savegame-reject-actor-position-y-without-x", !yWithoutXLoaded && yWithoutXError != null,
                new { yWithoutXLoaded, yWithoutXError });

            // A non-finite coordinate (NaN/Infinity) and an out-of-the-WAD-coordinate-range value must each
            // be rejected before any live game state mutates, not silently accepted or clamped.
            string nanJson = Regex.Replace(json, @"""ActorPositionX"":\[[^\]]*\]", m =>
            {
                string[] elements = Regex.Match(m.Value, @"\[([^\]]*)\]").Groups[1].Value.Split(',');
                elements[0] = "NaN";
                return $@"""ActorPositionX"":[{string.Join(',', elements)}]";
            });
            Game nanGame = Load("infight-retarget");
            bool nanLoaded = nanGame.TryLoad(nanJson, out string? nanError);
            Check("savegame-reject-non-finite-actor-position", !nanLoaded && nanError != null, new { nanLoaded, nanError });

            string overflowJson = Regex.Replace(json, @"""ActorPositionY"":\[[^\]]*\]", m =>
            {
                string[] elements = Regex.Match(m.Value, @"\[([^\]]*)\]").Groups[1].Value.Split(',');
                elements[0] = "1e999";
                return $@"""ActorPositionY"":[{string.Join(',', elements)}]";
            });
            Check("savegame-position-overflow-fixture-edited", overflowJson != json, new { overflowJson, json });
            Game overflowGame = Load("infight-retarget");
            bool overflowLoaded = overflowGame.TryLoad(overflowJson, out string? overflowError);
            Check("savegame-reject-actor-position-overflow", !overflowLoaded && overflowError != null,
                new { overflowLoaded, overflowError });

            string outOfRangeJson = Regex.Replace(json, @"""ActorPositionX"":\[[^\]]*\]", m =>
            {
                string[] elements = Regex.Match(m.Value, @"\[([^\]]*)\]").Groups[1].Value.Split(',');
                elements[0] = "99999";
                return $@"""ActorPositionX"":[{string.Join(',', elements)}]";
            });
            Game outOfRangeGame = Load("infight-retarget");
            bool outOfRangeLoaded = outOfRangeGame.TryLoad(outOfRangeJson, out string? outOfRangeError);
            Check("savegame-reject-actor-position-out-of-range", !outOfRangeLoaded && outOfRangeError != null,
                new { outOfRangeLoaded, outOfRangeError });
        }
        {
            // TryLoad's "malformed save leaves an in-progress game untouched" contract must also cover a
            // corrupt ActorPositionX/ActorPositionY pair: an out-of-range position value must not partially
            // rebuild Map/Actors before validation catches it.
            Game game = Load("infight-retarget");
            Tick(game, count: 50);
            string beforeMapName = game.Map.Name;
            var beforeActors = game.Actors.Select(a => (a.Position, a.Health, a.Removed)).ToList();

            string validJson = game.Save();
            string corruptJson = Regex.Replace(validJson, @"""ActorPositionX"":\[[^\]]*\]", m =>
            {
                string[] elements = Regex.Match(m.Value, @"\[([^\]]*)\]").Groups[1].Value.Split(',');
                elements[0] = "99999";
                return $@"""ActorPositionX"":[{string.Join(',', elements)}]";
            });
            bool loaded = game.TryLoad(corruptJson, out string? error);

            var afterActors = game.Actors.Select(a => (a.Position, a.Health, a.Removed)).ToList();
            Check("savegame-reject-actor-position-preserves-in-progress-game",
                !loaded && error != null && game.Map.Name == beforeMapName && afterActors.SequenceEqual(beforeActors),
                new { loaded, error, beforeMapName, afterMapName = game.Map.Name });
        }
    }
    if (mode is "native" or "all")
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native input checks require Linux/X11/XTest; use headless/engine on Windows and macOS.");
        foreach (var scenario in new[] { (29, "middle"), (99, "middle"), (99, "upper"), (99, "lower"),
            (7, "middle"), (23, "middle"), (99, "no-key"), (29, "no-tag"), (1, "middle") })
        {
            int special = scenario.Item1;
            string variant = scenario.Item2, name = $"switch-{special}-{variant}";
            bool changed = variant is not ("no-key" or "no-tag") && special != 1;
            using NativeGame game = await NativeGame.Start(executable, ["--wad", Path.Combine(fixtures, name + ".wad")],
                Path.Combine(output, "native-" + name), source, audio: special == 99 && variant == "middle");
            Frame before = game.Capture("before");
            game.Hold("e", .06); Thread.Sleep(100);
            Frame activated = game.Capture("activated");
            Thread.Sleep(1100);
            Frame settled = game.Capture("after-reset");
            string text = await game.Quit();
            Check("native-" + name + "-visible-toggle", before.SwitchPixels(false) > 500 && before.SwitchPixels(true) == 0 &&
                (changed ? activated.SwitchPixels(true) > 500 : activated.SwitchPixels(true) == 0),
                new { before = before.SwitchPixels(false), after = activated.SwitchPixels(true) });
            bool reset = changed && special == 99;
            Check("native-" + name + "-visible-reset-or-latch", reset || !changed ? settled.SwitchPixels(true) == 0 &&
                (special == 1 || settled.SwitchPixels(false) > 500) : settled.SwitchPixels(true) > 500);
            if (special == 99 && variant == "middle")
                Check("native-switch-PCM", Regex.IsMatch(text, @"Audio: [1-9]\d* sound events, [1-9]\d* PCM samples submitted"), text);
        }
        foreach (int special in new[] { 6, 25, 77, 141 })
            foreach (bool paused in new[] { false, true })
            {
                string name = $"crusher-{special}-{(paused ? "paused-player" : "trap")}";
                using NativeGame game = await NativeGame.Start(executable, ["--wad", Path.Combine(fixtures, name + ".wad")],
                    Path.Combine(output, "native-" + name), source, audio: special == 6);
                game.Capture("initial");
                game.Hold("w", .7);
                Thread.Sleep(250);
                Frame first = game.Capture("active-or-paused");
                Thread.Sleep(4000);
                Frame final = game.Capture("after-wait");
                string text = await game.Quit();
                Check("native-" + name, text.Contains(paused ? "health=100," : "health=0,", StringComparison.Ordinal) &&
                    (!paused || first.Hash == final.Hash), text);
                if (special == 6)
                    Check("native-" + name + "-PCM", Regex.IsMatch(text, @"Audio: [1-9]\d* sound events, [1-9]\d* PCM samples submitted"), text);
            }
        foreach (int special in new[] { 7, 8, 100, 127 })
            foreach (bool tagged in new[] { true, false })
            {
                string name = $"stairs-{special}-{(tagged ? "chain" : "no-tag")}";
                using NativeGame game = await NativeGame.Start(executable, ["--wad", Path.Combine(fixtures, name + ".wad")],
                    Path.Combine(output, "native-" + name), source);
                game.Capture("initial");
                game.Hold(special is 8 or 100 ? "w" : "e", special is 8 or 100 ? .25 : .06);
                Thread.Sleep(3100);
                game.Capture("built");
                game.Hold("w", 1.8);
                game.Capture("traversal");
                string text = await game.Quit();
                double x = ProcessCheck.Position(text).X;
                Check("native-" + name, tagged ? x > 224 : x is >= 160 and <= 176, new { x, text });
            }
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
