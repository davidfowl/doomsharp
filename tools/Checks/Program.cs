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
