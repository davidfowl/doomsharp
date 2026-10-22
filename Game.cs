using System.Text.Json;

namespace DoomSharp;

[Flags]
public enum Buttons
{
    None = 0, Forward = 1, Back = 2, Left = 4, Right = 8, TurnLeft = 16, TurnRight = 32,
    Fire = 64, Use = 128, Run = 256, Restart = 512, Pistol = 1024, Shotgun = 2048, Map = 4096,
    Chaingun = 8192, RocketLauncher = 16384, Save = 32768, Load = 65536,
    Chainsaw = 1048576, PlasmaRifle = 262144, BFG9000 = 524288
}

public enum Weapon { Pistol, Shotgun, Chaingun, RocketLauncher, Chainsaw, PlasmaRifle, BFG9000 }

public sealed class Input
{
    public Buttons Held;
    public double MouseX;
    public bool Quit;
    public void Set(Buttons button, bool down)
    {
        if (down) Held |= button; else Held &= ~button;
    }
}

public sealed class Actor
{
    public Vec Position;
    public readonly int Type;
    public readonly string Sprite;
    public readonly bool Enemy, Solid;
    public readonly double Radius;
    public int Health;
    public double Cooldown, HurtTime, DeathTime;
    public bool Removed, Dropped;
    public char? FixedFrame;
    public bool Hanging;
    // Null means "fight the player" (the default); set by infighting when another monster's attack hurts this one.
    public Actor? Target;
    // "Chase memory": the target's position the last time it was actually seen, and a countdown (seconds)
    // during which the monster keeps pursuing that remembered spot after losing line-of-sight. Null/zero
    // means the monster has never seen its target (or has fully forgotten it) and sits idle.
    public Vec? LastKnownTargetPosition;
    public double ChaseMemory;
    // Set by Game.NoiseAlert when a propagated sound reaches this monster's sector while it has no direct
    // line of sight to the player; AlertPosition is the sound's origin to blindly head toward until the
    // monster either arrives there, regains direct sight of the player, or is hurt into infighting instead.
    public bool Awake;
    public Vec? AlertPosition;
    public Actor(Thing thing, string sprite, bool enemy, bool solid, double radius, int health)
    {
        Position = thing.Position; Type = thing.Type; Sprite = sprite; Enemy = enemy; Solid = solid;
        Radius = radius; Health = health;
    }
}

public sealed class Projectile
{
    public Vec Position, Velocity;
    public double Z, Time = 5;
    public bool Rocket, Plasma, Bfg, Exploded;
    public int ExplosionTicks;
    // The monster that fired this projectile; null for player-fired projectiles. Used to attribute
    // infighting damage and to exclude the shooter from its own projectile's hit test.
    public Actor? Owner;
}

public sealed class Game
{
    public Wad Wad { get; }
    public Map Map { get; private set; }
    public Assets Assets { get; private set; }
    public Vec Position { get; private set; }
    public double Angle { get; private set; }
    public double Feet { get; private set; }
    public double Eye => Feet + (Health > 0 ? 41 : 12) + Math.Sin(WalkTime * 10) * Bob;
    public int Health { get; private set; } = 100;
    public int Armor { get; private set; }
    public int Bullets { get; private set; } = 50;
    public int Shells { get; private set; }
    public int Rockets { get; private set; }
    public int Cells { get; private set; }
    public bool HasShotgun { get; private set; }
    public bool HasChaingun { get; private set; }
    public bool HasRocketLauncher { get; private set; }
    public bool HasChainsaw { get; private set; }
    public bool HasPlasmaRifle { get; private set; }
    public bool HasBFG9000 { get; private set; }
    public Weapon SelectedWeapon { get; private set; }
    public bool ShotgunSelected => SelectedWeapon == Weapon.Shotgun;
    public int ChaingunFrame { get; private set; }
    public int ChaingunFlashFrame { get; private set; }
    public int ChaingunFlashTicks { get; private set; }
    public int RocketFireTicks { get; private set; }
    public int PlasmaFlashTicks { get; private set; }
    public int BFGFlashTicks { get; private set; }
    /// <summary>Has no magazine; shows a dash in the HUD like the classic chainsaw/fist slots.</summary>
    public bool WeaponHasNoAmmo => SelectedWeapon == Weapon.Chainsaw;
    public int Ammo => SelectedWeapon switch
    {
        Weapon.Shotgun => Shells, Weapon.RocketLauncher => Rockets,
        Weapon.PlasmaRifle or Weapon.BFG9000 => Cells, Weapon.Chainsaw => 0, _ => Bullets
    };
    public int Keys { get; private set; }
    public int Kills { get; private set; }
    public int TotalEnemies => Actors.Count(a => a.Enemy);
    public int Secrets { get; private set; }
    public int TotalSecrets => Map.Sectors.Count(s => s.Special == 9);
    public int Items { get; private set; }
    public int TotalItems => Actors.Count(a => IsItemType(a.Type));
    public double Time { get; private set; }
    public double FireTime { get; private set; }
    public double DamageFlash { get; private set; }
    public double PickupFlash { get; private set; }
    public double WalkTime { get; private set; }
    public double Bob { get; private set; }
    public bool Automap { get; private set; }
    public bool Finished { get; private set; }
    public string CompletionPrompt { get; private set; } = "";
    /// <summary>Classic end-of-level stats lines (map name, kills/items/secrets percentages, time,
    /// continue prompt), populated by <see cref="FinishLevel"/> and rendered while <see cref="Finished"/>.</summary>
    public string[] IntermissionStats { get; private set; } = [];
    public string Message { get; private set; } = "";
    public List<Actor> Actors { get; } = [];
    public List<Projectile> Projectiles { get; } = [];
    public Action<string>? PlaySound;
    public Action<string?>? PlayMusic;
    /// <summary>Invoked with a serialized <see cref="SaveGameState"/> whenever the player requests a save.</summary>
    public Action<string>? SaveGame;
    /// <summary>Invoked to fetch a previously saved game's JSON on demand; returns null if none exists.</summary>
    public Func<string?>? LoadGame;
    readonly Random random = new(1993);
    readonly HashSet<int> foundSecrets = [];
    Buttons previous;
    double damageTick, messageTime;
    public int UnsupportedThings { get; private set; }
    bool secretExit;
    bool bossDeathActivated;
    int chaingunTicks;
    bool chaingunFollowup, chaingunRefiring, chaingunAccuratePair;
    Weapon? pendingWeapon;

    public Game(Wad wad, string map)
    {
        Wad = wad; Map = new(wad, map); Assets = new(wad, map);
        Spawn();
    }

    void Spawn()
    {
        (Vec position, double angle, double feet, List<Actor> actors, int unsupportedThings) = BuildSpawnState(Map);
        Position = position; Angle = angle; Feet = feet;
        Actors.Clear(); Actors.AddRange(actors);
        Projectiles.Clear(); foundSecrets.Clear();
        bossDeathActivated = false;
        UnsupportedThings = unsupportedThings;
    }

    /// <summary>
    /// Computes the player start position/angle/feet and the deterministic spawn-order actor list for
    /// <paramref name="map"/>, purely as a function of <paramref name="map"/> (no instance state is read or
    /// mutated). Shared by <see cref="Spawn"/> (which commits the result to the live game) and
    /// <see cref="TryLoad"/> (which uses it to validate a save's actor layout against its target map
    /// <em>before</em> committing any mutation, so a malformed save never partially mutates an in-progress
    /// game).
    /// </summary>
    static (Vec Position, double Angle, double Feet, List<Actor> Actors, int UnsupportedThings) BuildSpawnState(Map map)
    {
        Thing? start = map.Things.Cast<Thing?>().FirstOrDefault(t => t!.Value.Type == 1);
        if (start == null) throw new InvalidDataException($"{map.Name} has no player-one start.");
        Vec position = start.Value.Position; double angle = start.Value.Angle * Math.PI / 180;
        double feet = map.FloorAt(position, 16);
        List<Actor> actors = []; int unsupportedThings = 0;
        foreach (Thing thing in map.Things)
        {
            if (thing.Type is >= 1 and <= 4 or 11 or 14 || (thing.Flags & 16) != 0 || (thing.Flags & 2) == 0) continue;
            Actor? actor = CreateActor(thing);
            if (actor == null)
            {
                unsupportedThings++;
                Console.Error.WriteLine($"Unsupported thing type {thing.Type} at {thing.Position}; skipped.");
            }
            else actors.Add(actor);
        }
        return (position, angle, feet, actors, unsupportedThings);
    }

    static Actor? CreateActor(Thing t) => t.Type switch
    {
        3004 => new(t, "POSS", true, true, 20, 20),
        9 => new(t, "SPOS", true, true, 20, 30),
        3001 => new(t, "TROO", true, true, 20, 60),
        3002 or 58 => new(t, "SARG", true, true, 30, 150),
        3003 => new(t, "BOSS", true, true, 24, 1000),
        69 => new(t, "BOS2", true, true, 24, 500),
        3005 => new(t, "HEAD", true, true, 31, 400),
        3006 => new(t, "SKUL", true, true, 16, 100),
        16 => new(t, "CYBR", true, true, 40, 4000),
        7 => new(t, "SPID", true, true, 64, 3000),
        65 => new(t, "CPOS", true, true, 20, 70),
        66 => new(t, "SKEL", true, true, 20, 300),
        67 => new(t, "FATT", true, true, 48, 600),
        68 => new(t, "BSPI", true, true, 64, 500),
        71 => new(t, "PAIN", true, true, 31, 400),
        64 => new(t, "VILE", true, true, 20, 700),
        2001 => new(t, "SHOT", false, false, 16, 1),
        2002 => new(t, "MGUN", false, false, 16, 1),
        2003 => new(t, "LAUN", false, false, 16, 1),
        2004 => new(t, "PLAS", false, false, 16, 1),
        2005 => new(t, "CSAW", false, false, 16, 1),
        2006 => new(t, "BFUG", false, false, 16, 1),
        2010 => new(t, "ROCK", false, false, 16, 1),
        2046 => new(t, "BROK", false, false, 16, 1),
        2007 => new(t, "CLIP", false, false, 16, 1),
        2048 => new(t, "AMMO", false, false, 16, 1),
        2008 => new(t, "SHEL", false, false, 16, 1),
        2049 => new(t, "SBOX", false, false, 16, 1),
        2047 => new(t, "CELL", false, false, 16, 1),
        17 => new(t, "CELP", false, false, 16, 1),
        2011 => new(t, "STIM", false, false, 16, 1),
        2012 => new(t, "MEDI", false, false, 16, 1),
        2014 => new(t, "BON1", false, false, 16, 1),
        2015 => new(t, "BON2", false, false, 16, 1),
        2018 => new(t, "ARM1", false, false, 16, 1),
        2019 => new(t, "ARM2", false, false, 16, 1),
        2013 => new(t, "SOUL", false, false, 16, 1),
        2023 => new(t, "PSTR", false, false, 16, 1),
        5 or 40 => new(t, t.Type == 5 ? "BKEY" : "BSKU", false, false, 16, 1),
        6 or 39 => new(t, t.Type == 6 ? "YKEY" : "YSKU", false, false, 16, 1),
        13 or 38 => new(t, t.Type == 13 ? "RKEY" : "RSKU", false, false, 16, 1),
        2035 => new(t, "BAR1", false, true, 10, 20),
        2028 => new(t, "COLU", false, true, 16, 1),
        48 => new(t, "ELEC", false, true, 16, 1),
        30 => new(t, "COL1", false, true, 16, 1),
        31 => new(t, "COL2", false, true, 16, 1),
        32 => new(t, "COL3", false, true, 16, 1),
        33 => new(t, "COL4", false, true, 16, 1),
        34 => new(t, "CAND", false, false, 16, 1),
        35 => new(t, "CBRA", false, true, 16, 1),
        45 => new(t, "TGRN", false, true, 16, 1),
        46 => new(t, "TRED", false, true, 16, 1),
        44 => new(t, "TBLU", false, true, 16, 1),
        55 => new(t, "SMBT", false, true, 16, 1),
        56 => new(t, "SMGT", false, true, 16, 1),
        57 => new(t, "SMRT", false, true, 16, 1),
        36 => new(t, "COL5", false, true, 16, 1),
        37 => new(t, "COL6", false, true, 16, 1),
        41 => new(t, "CEYE", false, true, 16, 1),
        42 => new(t, "FSKU", false, true, 16, 1),
        43 => new(t, "TRE1", false, true, 16, 1),
        47 => new(t, "SMIT", false, true, 16, 1),
        54 => new(t, "TRE2", false, true, 32, 1),
        24 => new(t, "POL5", false, false, 16, 1),
        25 => new(t, "POL1", false, true, 16, 1),
        26 => new(t, "POL6", false, true, 16, 1),
        27 => new(t, "POL4", false, true, 16, 1),
        28 => new(t, "POL2", false, true, 16, 1),
        29 => new(t, "POL3", false, true, 16, 1),
        49 or 63 => new(t, "GOR1", false, t.Type == 49, 16, 1) { Hanging = true },
        50 or 59 => new(t, "GOR2", false, t.Type == 50, 16, 1) { Hanging = true },
        51 or 61 => new(t, "GOR3", false, t.Type == 51, 16, 1) { Hanging = true },
        52 or 60 => new(t, "GOR4", false, t.Type == 52, 16, 1) { Hanging = true },
        53 or 62 => new(t, "GOR5", false, t.Type == 53, 16, 1) { Hanging = true },
        10 => new(t, "PLAY", false, false, 16, 0) { FixedFrame = 'N' },
        15 => new(t, "PLAY", false, false, 16, 0) { FixedFrame = 'W' },
        18 => new(t, "POSS", false, false, 16, 0) { FixedFrame = 'L' },
        19 => new(t, "SPOS", false, false, 16, 0) { FixedFrame = 'L' },
        20 => new(t, "TROO", false, false, 16, 0) { FixedFrame = 'M' },
        21 => new(t, "SARG", false, false, 16, 0) { FixedFrame = 'N' },
        22 => new(t, "HEAD", false, false, 16, 0) { FixedFrame = 'L' },
        23 => new(t, "SKUL", false, false, 16, 0) { FixedFrame = 'K' },
        12 => new(t, "PLAY", false, false, 16, 0) { FixedFrame = 'W' },
        _ => null
    };

    public void Tick(Input input, double dt)
    {
        dt = Math.Clamp(dt, 0, .05);
        Time += dt; FireTime = Math.Max(0, FireTime - dt);
        if (chaingunTicks > 0) chaingunTicks--;
        if (ChaingunFlashTicks > 0) ChaingunFlashTicks--;
        if (RocketFireTicks > 0) RocketFireTicks--;
        if (PlasmaFlashTicks > 0) PlasmaFlashTicks--;
        if (BFGFlashTicks > 0) BFGFlashTicks--;
        DamageFlash = Math.Max(0, DamageFlash - dt); PickupFlash = Math.Max(0, PickupFlash - dt);
        messageTime -= dt;
        if (messageTime <= 0) Message = "";
        Buttons pressed = input.Held & ~previous;
        previous = input.Held;
        if ((pressed & Buttons.Restart) != 0) { Load(Map.Name, true); return; }
        if ((pressed & Buttons.Save) != 0 && SaveGame != null && Health > 0 && !Finished) { SaveGame(Save()); Say("GAME SAVED"); }
        if ((pressed & Buttons.Load) != 0 && LoadGame != null)
        {
            string? json = LoadGame();
            if (json == null) Say("LOAD FAILED: no saved game");
            else Say(TryLoad(json, out string? error) ? "GAME LOADED" : $"LOAD FAILED: {error}");
        }
        if (Finished)
        {
            if ((pressed & (Buttons.Use | Buttons.Fire)) != 0) NextMap();
            input.MouseX = 0;
            return;
        }
        if (Health > 0)
        {
            if ((pressed & Buttons.Map) != 0) Automap = !Automap;
            if ((pressed & Buttons.Pistol) != 0) SelectWeapon(Weapon.Pistol);
            if ((pressed & Buttons.Shotgun) != 0) SelectWeapon(Weapon.Shotgun);
            if ((pressed & Buttons.Chaingun) != 0) SelectWeapon(Weapon.Chaingun);
            if ((pressed & Buttons.RocketLauncher) != 0) SelectWeapon(Weapon.RocketLauncher);
            if ((pressed & Buttons.Chainsaw) != 0) SelectWeapon(Weapon.Chainsaw);
            if ((pressed & Buttons.PlasmaRifle) != 0) SelectWeapon(Weapon.PlasmaRifle);
            if ((pressed & Buttons.BFG9000) != 0) SelectWeapon(Weapon.BFG9000);
            Angle += (((input.Held & Buttons.TurnLeft) != 0 ? 1 : 0) -
                ((input.Held & Buttons.TurnRight) != 0 ? 1 : 0)) * dt * 2.4 - input.MouseX * .003;
            Angle = Math.IEEERemainder(Angle, Math.Tau);
            Vec forward = new(Math.Cos(Angle), Math.Sin(Angle)), right = new(forward.Y, -forward.X);
            double f = ((input.Held & Buttons.Forward) != 0 ? 1 : 0) - ((input.Held & Buttons.Back) != 0 ? 1 : 0);
            double s = ((input.Held & Buttons.Right) != 0 ? 1 : 0) - ((input.Held & Buttons.Left) != 0 ? 1 : 0);
            Vec move = forward * f + right * s;
            Bob = move.Length > 0 ? 1.4 : 0;
            if (move.Length > 0)
            {
                WalkTime += dt;
                move *= dt * ((input.Held & Buttons.Run) != 0 ? 260 : 150) / Math.Max(1, move.Length);
                Vec old = Position;
                MovePlayer(move);
                CrossLines(old, Position);
            }
            Sector sector = Map.Sectors[Map.FindSector(Position)];
            Feet = Map.FloorAt(Position, 16);
            bool onSectorFloor = Feet == sector.Floor;
            if (onSectorFloor && sector.Special == 9 && foundSecrets.Add(Map.FindSector(Position))) { Secrets++; Say("SECRET FOUND"); }
            damageTick += dt;
            if (damageTick >= 1)
            {
                damageTick = 0;
                if (onSectorFloor && sector.Special is 4 or 5 or 7 or 11 or 16)
                    Damage(sector.Special switch { 7 => 5, 11 => 20, _ => 10 });
            }
            if (onSectorFloor && sector.Special == 11 && Health <= 10)
            {
                FinishLevel(false);
                input.MouseX = 0;
                return;
            }
            if ((pressed & Buttons.Use) != 0) Use();
            if (Health > 0) UpdateWeapon((input.Held & Buttons.Fire) != 0);
            foreach (Actor actor in Actors)
                if (!actor.Removed && !actor.Solid && !actor.Enemy && (actor.Position - Position).Length < 28)
                    Pickup(actor);
        }
        input.MouseX = 0;
        UpdateDoors(dt);
        UpdateFloors(dt);
        foreach (Actor actor in Actors.ToArray()) UpdateActor(actor, dt);
        UpdateProjectiles(dt);
    }

    void MovePlayer(Vec delta)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(delta.Length / 8));
        delta *= 1.0 / steps;
        for (int i = 0; i < steps; i++)
        {
            Vec next = Position + delta;
            if (CanMovePlayer(next)) Position = next;
            else
            {
                next = Position + new Vec(delta.X, 0);
                if (CanMovePlayer(next)) Position = next;
                next = Position + new Vec(0, delta.Y);
                if (CanMovePlayer(next)) Position = next;
            }
            Feet = Map.FloorAt(Position, 16);
        }
    }

    bool CanMovePlayer(Vec next) => Map.CanMove(Position, next, 16, 56, Feet) &&
        !Actors.Any(a => !a.Removed && a.Solid && a.Health > 0 && (a.Position - next).Length < a.Radius + 16);

    void Use()
    {
        Vec end = Position + new Vec(Math.Cos(Angle), Math.Sin(Angle)) * 72;
        List<(Line Line, double Distance)> hits = [];
        foreach (Line line in Map.Lines)
            if (Map.Intersects(Position, end, Map.Vertices[line.A], Map.Vertices[line.B], out double t))
                hits.Add((line, t));
        foreach (var hit in hits.OrderBy(h => h.Distance))
        {
            if (hit.Line.Special != 0) { Activate(hit.Line, false); return; }
            if (hit.Line.Left == 65535) break;
            Sector a = Map.Sectors[Map.Sides[hit.Line.Right].Sector], b = Map.Sectors[Map.Sides[hit.Line.Left].Sector];
            if (Math.Min(a.Ceiling, b.Ceiling) <= Math.Max(a.Floor, b.Floor)) break;
        }
        PlaySound?.Invoke("NOWAY");
    }

    void CrossLines(Vec old, Vec next)
    {
        foreach (Line l in Map.Lines)
            if (l.Special != 0 && Map.Intersects(old, next, Map.Vertices[l.A], Map.Vertices[l.B], out double t) &&
                t > 0 && Vec.Cross(Map.Vertices[l.B] - Map.Vertices[l.A], old - Map.Vertices[l.A]) *
                Vec.Cross(Map.Vertices[l.B] - Map.Vertices[l.A], next - Map.Vertices[l.A]) < 0)
            {
                if (CrossingTeleport(l, old)) break;
                Activate(l, true);
            }
    }

    bool CrossingTeleport(Line line, Vec old)
    {
        if (line.Special is not (39 or 97) ||
            Vec.Cross(Map.Vertices[line.B] - Map.Vertices[line.A], old - Map.Vertices[line.A]) >= 0) return false;
        foreach (Thing destination in Map.Things.Where(t => t.Type == 14))
        {
            Sector sector = Map.Sectors[Map.FindSector(destination.Position)];
            if (line.Tag == 0 || sector.Tag != line.Tag) continue;
            if (!Map.CanMove(destination.Position, destination.Position, 16, 56, sector.Floor))
            { Say("TELEPORT DESTINATION BLOCKED"); return true; }
            // Telefrag: anything occupying the destination is instantly killed, and the teleport still succeeds.
            foreach (Actor blocker in Actors.Where(a => !a.Removed && a.Solid && a.Health > 0 &&
                (a.Position - destination.Position).Length < a.Radius + 16).ToArray())
                Hurt(blocker, blocker.Health);
            Position = destination.Position; Feet = Map.FloorAt(Position, 16); Angle = destination.Angle * Math.PI / 180;
            PickupFlash = .3; PlaySound?.Invoke("TELEPT");
            if (line.Special == 39) line.Special = 0;
            return true;
        }
        Say("TELEPORT HAS NO TAGGED DESTINATION");
        return true;
    }

    void Activate(Line line, bool crossing)
    {
        int special = line.Special;
        if ((!crossing && special is 11 or 51) || (crossing && special is 52 or 124))
        {
            FinishLevel(special is 51 or 124);
            PlaySound?.Invoke("SWTCHN"); return;
        }
        if (ActivateFloor(line, crossing)) return;
        bool lockedSwitch = !crossing && special is 99 or 133 or 134 or 135 or 136 or 137;
        bool door = crossing ? special is 2 or 3 or 4 or 16 or 86 or 90 :
            lockedSwitch || special is 1 or 26 or 27 or 28 or 31 or 32 or 33 or 34 or 29 or 63 or 103 or 111 or 112 or 113;
        if (!door)
        {
            if (!crossing && special != 0) Say($"UNSUPPORTED LINE SPECIAL {special}");
            return;
        }
        if (lockedSwitch && Vec.Cross(Map.Vertices[line.B] - Map.Vertices[line.A],
            Position - Map.Vertices[line.A]) > 0) { PlaySound?.Invoke("NOWAY"); return; }
        int key = special is 26 or 32 or 99 or 133 ? 1 :
            special is 27 or 34 or 136 or 137 ? 2 : special is 28 or 33 or 134 or 135 ? 4 : 0;
        if (key != 0 && (Keys & key) == 0) { Say("YOU NEED THE MATCHING KEY"); PlaySound?.Invoke("NOWAY"); return; }
        bool manual = special is 1 or 26 or 27 or 28 or 31 or 32 or 33 or 34;
        IEnumerable<int> targets = manual
            ? line.Left == 65535 ? [] : [Map.Sides[line.Left].Sector]
            : TaggedSectors(line);
        bool targeted = false, activated = false;
        foreach (int s in targets)
        {
            targeted = true;
            Sector sector = Map.Sectors[s];
            if (lockedSwitch && sector.DoorDirection != 0) continue;
            sector.DoorDirection = crossing && special is 3 or 16 ? -1 : 1;
            sector.DoorTime = lockedSwitch || special is 2 or 31 or 32 or 33 or 34 or 103 or 112 ? -1 : 4;
            sector.DoorSpeed = lockedSwitch ? 280 : 90;
            activated = true;
            PlaySound?.Invoke(lockedSwitch ? "BDOPN" : sector.DoorDirection > 0 ? "DOROPN" : "DORCLS");
        }
        if (lockedSwitch && !targeted) { Say("DOOR HAS NO TAGGED SECTOR"); PlaySound?.Invoke("NOWAY"); }
        if ((!lockedSwitch || activated) &&
            special is 2 or 3 or 4 or 16 or 29 or 31 or 32 or 33 or 34 or 103 or 111 or 112 or 113 or 133 or 135 or 137)
            line.Special = 0;
    }

    IEnumerable<int> TaggedSectors(Line line) => Enumerable.Range(0, Map.Sectors.Length)
        .Where(i => line.Tag != 0 && Map.Sectors[i].Tag == line.Tag);

    IEnumerable<int> Neighbors(int sector)
    {
        foreach (int index in Map.SectorLines[sector])
        {
            Line line = Map.Lines[index];
            if (line.Left == 65535) continue;
            int right = Map.Sides[line.Right].Sector, left = Map.Sides[line.Left].Sector;
            int other = right == sector ? left : right;
            if (other != sector) yield return other;
        }
    }

    bool ActivateFloor(Line line, bool crossing)
    {
        int special = line.Special;
        bool lift = crossing ? special is 10 or 88 or 120 or 121 : special is 21 or 62 or 122 or 123;
        bool lower = crossing ? special is 38 or 82 : special is 23 or 60;
        bool raiseNext = crossing ? special is 119 or 128 : special is 18 or 69;
        bool raise24 = crossing && special is 58 or 92;
        if (!lift && !lower && !raiseNext && !raise24) return false;
        bool activated = false;
        foreach (int i in TaggedSectors(line))
        {
            Sector sector = Map.Sectors[i];
            if (sector.FloorDirection != 0) continue;
            double[] heights = Neighbors(i).Select(n => Map.Sectors[n].Floor).ToArray();
            double target = raise24 ? sector.Floor + 24 : raiseNext
                ? heights.Where(h => h > sector.Floor).DefaultIfEmpty(sector.Floor).Min()
                : heights.Append(sector.Floor).Min();
            activated |= StartFloor(sector, target, lift,
                special is 120 or 121 or 122 or 123 ? 140 : lift ? 70 : 35);
        }
        if (activated)
        {
            PlaySound?.Invoke("PSTART");
            if (special is 10 or 21 or 120 or 122 or 38 or 23 or 119 or 18 or 58) line.Special = 0;
        }
        return true;
    }

    static bool StartFloor(Sector sector, double target, bool lift, double speed)
    {
        if (sector.FloorDirection != 0 || target == sector.Floor) return false;
        sector.FloorOrigin = sector.Floor; sector.FloorTarget = target;
        sector.FloorDirection = target > sector.Floor ? 1 : -1;
        sector.Lift = lift; sector.LiftWait = 3; sector.FloorSpeed = speed;
        return true;
    }

    void BossDeath(Actor actor)
    {
        if (bossDeathActivated || Map.Name != "E1M8" || actor.Type != 3003 || Health <= 0 ||
            Actors.Any(a => a.Type == 3003 && !a.Removed && a.Health > 0)) return;
        bossDeathActivated = true;
        bool targeted = false, activated = false;
        for (int i = 0; i < Map.Sectors.Length; i++)
        {
            Sector sector = Map.Sectors[i];
            if (sector.Tag != 666) continue;
            targeted = true;
            double target = Neighbors(i).Select(n => Map.Sectors[n].Floor).Append(sector.Floor).Min();
            activated |= StartFloor(sector, target, false, 35);
        }
        if (activated) PlaySound?.Invoke("PSTART");
        if (!targeted)
        {
            Say("BOSS ACTION HAS NO TAGGED SECTOR");
            Console.Error.WriteLine($"{Map.Name} boss action: no sectors tagged666.");
        }
    }

    void UpdateFloors(double dt)
    {
        for (int i = 0; i < Map.Sectors.Length; i++)
        {
            Sector sector = Map.Sectors[i];
            if (sector.FloorDirection == 0) continue;
            if (sector.FloorDirection == 2)
            {
                sector.LiftWait -= dt;
                if (sector.LiftWait <= 0) { sector.FloorTarget = sector.FloorOrigin; sector.FloorDirection = 1; }
                continue;
            }
            double next = sector.Floor + sector.FloorDirection * sector.FloorSpeed * dt;
            next = sector.FloorDirection > 0 ? Math.Min(next, sector.FloorTarget) : Math.Max(next, sector.FloorTarget);
            bool blocked = Health > 0 && Map.TouchingSectors(Position, 16).Contains(i) &&
                Map.CeilingAt(Position, 16) - next < 56 ||
                Actors.Any(a => a.Solid && !a.Removed && a.Health > 0 &&
                    Map.TouchingSectors(a.Position, a.Radius).Contains(i) &&
                    Map.CeilingAt(a.Position, a.Radius) - next < 56);
            if (next > sector.Floor && blocked) continue;
            sector.Floor = next;
            if (next != sector.FloorTarget) continue;
            sector.FloorDirection = sector.Lift && next != sector.FloorOrigin ? 2 : 0;
            PlaySound?.Invoke("PSTOP");
        }
        Feet = Map.FloorAt(Position, 16);
    }

    void UpdateDoors(double dt)
    {
        for (int i = 0; i < Map.Sectors.Length; i++)
        {
            Sector sector = Map.Sectors[i];
            if (sector.DoorDirection == 0) continue;
            double target = sector.Floor;
            if (sector.DoorDirection == 1)
            {
                target = double.MaxValue;
                foreach (int neighbor in Neighbors(i)) target = Math.Min(target, Map.Sectors[neighbor].Ceiling - 4);
                if (target == double.MaxValue) target = Math.Max(sector.OriginalCeiling, sector.Floor + 124);
                target = Math.Max(sector.Floor, target);
                sector.Ceiling = Math.Min(target, sector.Ceiling + sector.DoorSpeed * dt);
                if (sector.Ceiling >= target) sector.DoorDirection = sector.DoorTime < 0 ? 0 : 2;
            }
            else if (sector.DoorDirection == 2)
            {
                sector.DoorTime -= dt;
                if (sector.DoorTime <= 0) { sector.DoorDirection = -1; PlaySound?.Invoke("DORCLS"); }
            }
            else
            {
                bool occupied = Health > 0 && Map.FindSector(Position) == i ||
                    Actors.Any(a => a.Solid && !a.Removed && a.Health > 0 && Map.FindSector(a.Position) == i);
                if (occupied) { sector.DoorDirection = 1; sector.DoorTime = 4; }
                else
                {
                    sector.Ceiling = Math.Max(target, sector.Ceiling - sector.DoorSpeed * dt);
                    if (sector.Ceiling <= target) sector.DoorDirection = 0;
                }
            }
        }
    }

    void SelectWeapon(Weapon weapon)
    {
        if (weapon == Weapon.Shotgun && !HasShotgun || weapon == Weapon.Chaingun && !HasChaingun ||
            weapon == Weapon.RocketLauncher && !HasRocketLauncher || weapon == Weapon.Chainsaw && !HasChainsaw ||
            weapon == Weapon.PlasmaRifle && !HasPlasmaRifle || weapon == Weapon.BFG9000 && !HasBFG9000)
        {
            Say(weapon switch
            {
                Weapon.RocketLauncher => "ROCKET LAUNCHER NOT OWNED",
                Weapon.PlasmaRifle => "PLASMA RIFLE NOT OWNED",
                Weapon.BFG9000 => "BFG9000 NOT OWNED",
                _ => $"{weapon.ToString().ToUpperInvariant()} NOT OWNED"
            });
            return;
        }
        if (SelectedWeapon == Weapon.Chaingun && (chaingunTicks > 0 || chaingunFollowup) ||
            SelectedWeapon == Weapon.RocketLauncher && RocketFireTicks > 0)
        {
            pendingWeapon = weapon;
            return;
        }
        SetWeapon(weapon);
    }

    void SetWeapon(Weapon weapon)
    {
        if (SelectedWeapon == weapon) return;
        SelectedWeapon = weapon;
        chaingunRefiring = false;
        ChaingunFrame = 0;
        ChaingunFlashTicks = 0;
    }

    void UpdateWeapon(bool fire)
    {
        if (SelectedWeapon == Weapon.RocketLauncher && RocketFireTicks > 0) return;
        if (SelectedWeapon == Weapon.Chaingun)
        {
            if (chaingunTicks > 0) return;
            // A started pair finishes even when the trigger is released or selection changes.
            if (chaingunFollowup)
            {
                chaingunFollowup = false;
                if (FireChaingun(1)) chaingunTicks = 4;
                return;
            }
        }
        if (pendingWeapon is Weapon weapon)
        {
            pendingWeapon = null;
            SetWeapon(weapon);
        }
        if (SelectedWeapon != Weapon.Chaingun)
        {
            if (fire && FireTime == 0)
            {
                if (SelectedWeapon == Weapon.RocketLauncher) ShootRocket();
                else if (SelectedWeapon == Weapon.PlasmaRifle) ShootPlasma();
                else if (SelectedWeapon == Weapon.BFG9000) ShootBfg();
                else if (SelectedWeapon == Weapon.Chainsaw) Saw();
                else Shoot();
            }
            return;
        }
        if (!fire)
        {
            chaingunRefiring = false;
            ChaingunFrame = 0;
            return;
        }
        if (FireTime > 0) return;
        chaingunAccuratePair = !chaingunRefiring;
        if (FireChaingun(0))
        {
            chaingunTicks = 4;
            chaingunFollowup = true;
            chaingunRefiring = true;
        }
    }

    bool FireChaingun(int frame)
    {
        if (Bullets == 0)
        {
            Say("OUT OF AMMO"); FireTime = .3; chaingunRefiring = false; ChaingunFrame = 0;
            return false;
        }
        Bullets--;
        ChaingunFrame = frame;
        ChaingunFlashFrame = frame;
        ChaingunFlashTicks = 5;
        PlaySound?.Invoke("PISTOL");
        double spread = chaingunAccuratePair ? 0 : (random.NextDouble() - random.NextDouble()) * Math.PI / 32;
        Hitscan(Angle + spread);
        NoiseAlert(Position);
        return true;
    }

    void Shoot()
    {
        bool shotgun = ShotgunSelected && HasShotgun;
        if (shotgun ? Shells == 0 : Bullets == 0) { Say("OUT OF AMMO"); FireTime = .3; return; }
        if (shotgun) Shells--; else Bullets--;
        FireTime = shotgun ? .85 : .3;
        PlaySound?.Invoke(shotgun ? "SHOTGN" : "PISTOL");
        for (int pellet = 0; pellet < (shotgun ? 7 : 1); pellet++)
        {
            double angle = Angle + (random.NextDouble() - .5) * (shotgun ? .12 : .015);
            Hitscan(angle);
        }
        NoiseAlert(Position);
    }

    void ShootRocket()
    {
        if (Rockets == 0) { Say("OUT OF AMMO"); FireTime = .3; return; }
        Rockets--;
        RocketFireTicks = 20;
        Projectiles.Add(new()
        {
            Rocket = true, Position = Position, Z = Feet + 32,
            Velocity = new Vec(Math.Cos(Angle), Math.Sin(Angle)) * 700
        });
        PlaySound?.Invoke("RLAUNC");
        NoiseAlert(Position);
    }

    void ShootPlasma()
    {
        if (Cells == 0) { Say("OUT OF AMMO"); FireTime = .3; return; }
        Cells--;
        FireTime = .1;
        PlasmaFlashTicks = 4;
        Projectiles.Add(new()
        {
            Plasma = true, Position = Position, Z = Feet + 32,
            Velocity = new Vec(Math.Cos(Angle), Math.Sin(Angle)) * 1000
        });
        PlaySound?.Invoke("PLASMA");
    }

    void ShootBfg()
    {
        // The classic BFG9000 costs 40 cells per shot.
        if (Cells < 40) { Say("OUT OF AMMO"); FireTime = .3; return; }
        Cells -= 40;
        FireTime = .8;
        BFGFlashTicks = 10;
        Projectiles.Add(new()
        {
            Bfg = true, Position = Position, Z = Feet + 32,
            Velocity = new Vec(Math.Cos(Angle), Math.Sin(Angle)) * 400
        });
        PlaySound?.Invoke("BFG");
    }

    void Saw()
    {
        FireTime = .1;
        Vec direction = new(Math.Cos(Angle), Math.Sin(Angle));
        Actor? target = null; double nearest = 64;
        foreach (Actor actor in Actors)
        {
            if (actor.Removed || actor.Health <= 0 || (!actor.Enemy && actor.Type != 2035)) continue;
            Vec relative = actor.Position - Position;
            double distance = Vec.Dot(relative, direction), off = Math.Abs(Vec.Cross(relative, direction));
            if (distance > 0 && distance < nearest && off < actor.Radius + 20) { target = actor; nearest = distance; }
        }
        if (target != null) { Hurt(target, random.Next(1, 11) * 2); PlaySound?.Invoke("SAWFUL"); }
        else PlaySound?.Invoke("SAWIDL");
    }

    void Hitscan(double angle)
    {
        Vec direction = new(Math.Cos(angle), Math.Sin(angle));
        Actor? target = null; double nearest = 2048;
        foreach (Actor actor in Actors)
        {
            if (actor.Removed || actor.Health <= 0 || (!actor.Enemy && actor.Type != 2035)) continue;
            Vec relative = actor.Position - Position;
            double distance = Vec.Dot(relative, direction), off = Math.Abs(Vec.Cross(relative, direction));
            double floor = Map.Sectors[Map.FindSector(actor.Position)].Floor;
            if (distance > 0 && distance < nearest && off < actor.Radius &&
                Eye > floor && Eye < floor + 64 && Map.ClearSight(Position, actor.Position, Eye))
            { target = actor; nearest = distance; }
        }
        if (target != null) Hurt(target, random.Next(1, 4) * 5);
    }

    // instigator is the attacking actor (null for the player), used only to drive infighting retargeting:
    // a monster hurt by another monster switches to fight its attacker, while player damage never retargets.
    void Hurt(Actor actor, int damage, Actor? instigator = null)
    {
        if (actor.Removed || actor.Health <= 0) return;
        actor.Health -= damage; actor.HurtTime = .2;
        PlaySound?.Invoke("POPAIN");
        if (actor.Health > 0)
        {
            if (actor.Enemy && instigator != null && instigator != actor && instigator.Enemy && instigator.Health > 0)
                actor.Target = instigator;
            return;
        }
        if (actor.Enemy)
        {
            Kills++; PlaySound?.Invoke("PODTH1");
            BossDeath(actor);
            int drop = actor.Type == 9 ? 2001 : actor.Type == 65 ? 2002 : actor.Type == 3004 ? 2007 : 0;
            if (drop != 0)
            {
                Actor item = CreateActor(new(actor.Position, 0, drop, 2)) ??
                    throw new InvalidOperationException($"Unsupported enemy drop {drop}.");
                item.Dropped = true;
                Actors.Add(item);
            }
        }
        if (actor.Type == 2035)
        {
            actor.Removed = true; PlaySound?.Invoke("BAREXP");
            Blast(actor.Position, Map.Sectors[Map.FindSector(actor.Position)].Floor + 32, false, instigator);
        }
    }

    void Blast(Vec origin, double z, bool rocket, Actor? instigator = null, double radius = 128, double maxDamage = 128)
    {
        foreach (Actor other in Actors.ToArray())
            if (!other.Removed && other.Health > 0 && (other.Enemy || other.Type == 2035))
            {
                double distance = Math.Max(0, (other.Position - origin).Length - (rocket ? other.Radius : 0));
                if (distance < radius && Map.ClearSight(origin, other.Position, z))
                    Hurt(other, Math.Max(1, (int)(maxDamage - distance * (maxDamage / radius))), instigator);
            }
        double playerDistance = Math.Max(0, (Position - origin).Length - (rocket ? 16 : 0));
        if (playerDistance < radius && Map.ClearSight(origin, Position, rocket ? z : Eye))
            Damage(rocket ? Math.Max(1, (int)(maxDamage - playerDistance * (maxDamage / radius))) :
                (int)(maxDamage - playerDistance * (maxDamage / radius)));
    }

    // Classic DOOM monsters keep a few seconds of "memory" of where their target last stood after losing
    // line-of-sight, continuing to close on that spot instead of freezing in place; the window here is a
    // few seconds' worth of ticks, matching that behavior without depending on the original movecount table.
    const double ChaseMemoryDuration = 3.0;

    void UpdateActor(Actor actor, double dt)
    {
        actor.HurtTime = Math.Max(0, actor.HurtTime - dt); actor.Cooldown -= dt;
        if (actor.Removed) return;
        if (actor.Health <= 0) { actor.DeathTime += dt; return; }
        if (!actor.Enemy || Finished) return;
        // A monster normally fights the player; infighting (see Hurt) can instead point it at another monster.
        Actor? rival = actor.Target != null && actor.Target != actor && !actor.Target.Removed && actor.Target.Health > 0
            ? actor.Target : null;
        if (rival == null && Health <= 0) return;
        Vec targetPosition = rival?.Position ?? Position;
        double meleeRadius = rival?.Radius ?? 0, stopRadius = rival?.Radius ?? 20;
        double floor = Map.Sectors[Map.FindSector(actor.Position)].Floor;
        Vec delta = targetPosition - actor.Position;
        double distance = delta.Length;
        bool sighted = distance <= 1500 && Map.ClearSight(actor.Position, targetPosition, floor + 40);
        if (sighted)
        {
            // Target is actually visible: refresh the remembered spot and reset the pursuit window.
            actor.LastKnownTargetPosition = targetPosition;
            actor.ChaseMemory = ChaseMemoryDuration;
        }
        else
        {
            actor.ChaseMemory = Math.Max(0, actor.ChaseMemory - dt);
            if (actor.ChaseMemory > 0 && actor.LastKnownTargetPosition is { } remembered)
            {
                // Sight is lost but the memory window hasn't expired: keep closing on the last-known
                // position instead of attacking (can't see the target) or standing still.
                targetPosition = remembered;
                delta = targetPosition - actor.Position;
                distance = delta.Length;
            }
            else if (rival == null && actor.Awake && actor.AlertPosition is { } alert)
            {
                // Chase memory of the player has run out (or never existed): a monster that was woken by a
                // propagated noise (see NoiseAlert) still blindly heads for the sound's origin until it
                // arrives, regains sight of the player (handled above once close enough), or is blocked.
                Vec toAlert = alert - actor.Position;
                double alertDistance = toAlert.Length;
                if (alertDistance > actor.Radius + 20 && actor.HurtTime == 0)
                    MoveToward(actor, toAlert, alertDistance, floor, dt);
                else
                    actor.Awake = false; // reached the noise's source without finding the player; go back to sleep
                return;
            }
            else return;
        }
        if (sighted && actor.Cooldown <= 0 && (distance < 64 + meleeRadius || actor.Type is not (3002 or 58) && distance < 800))
        {
            actor.Cooldown = 1.1 + random.NextDouble();
            if (distance < 64 + meleeRadius)
            {
                int damage = random.Next(3, 9) * 3;
                if (rival != null) Hurt(rival, damage, actor); else Damage(damage);
            }
            else if (actor.Type is 3004 or 9 or 65 or 7)
            {
                PlaySound?.Invoke(actor.Type == 9 ? "SHOTGN" : "PISTOL");
                if (random.NextDouble() < Math.Clamp(1 - distance / 1200, .15, .9))
                {
                    int damage = random.Next(1, 5) * (actor.Type == 9 ? 5 : 3);
                    if (rival != null) Hurt(rival, damage, actor); else Damage(damage);
                }
            }
            else
            {
                Projectiles.Add(new() { Position = actor.Position, Z = floor + 32, Velocity = delta * (220 / distance), Owner = actor });
                PlaySound?.Invoke("FIRSHT");
            }
        }
        if (distance > actor.Radius + stopRadius && actor.HurtTime == 0) MoveToward(actor, delta, distance, floor, dt);
    }

    void MoveToward(Actor actor, Vec delta, double distance, double floor, double dt)
    {
        Vec move = delta * (dt * (actor.Type is 3002 or 58 ? 90 : 42) / distance);
        Vec next = actor.Position + move;
        if (Map.CanMove(actor.Position, next, actor.Radius, 56, floor, true) &&
            !Actors.Any(a => a != actor && a.Solid && !a.Removed && a.Health > 0 &&
                (a.Position - next).Length < actor.Radius + a.Radius))
            actor.Position = next;
    }

    // Floods the sector graph from a loud sound's origin (player gunfire or pain) across currently-open
    // connections, waking any monster whose sector the flood reaches so it can start blindly chasing the
    // player (see UpdateActor) even without direct line of sight — a clean-room analogue of classic DOOM's
    // P_NoiseAlert. A connection only propagates sound when it currently has an open gap between the two
    // sectors' floor/ceiling (so a closed door blocks it, matching how it also blocks sight/movement) and
    // is not a classic sound-blocking linedef (flag bit 0x40, ML_SOUNDBLOCK). The flood is additionally
    // capped at the same 1500-unit range monsters already use for direct sight, so distant noise fades out.
    void NoiseAlert(Vec origin)
    {
        const double range = 1500;
        const int soundBlock = 0x40;
        int start = Map.FindSector(origin);
        Dictionary<int, double> reached = new() { [start] = 0 };
        Queue<int> frontier = new();
        frontier.Enqueue(start);
        while (frontier.Count > 0)
        {
            int sector = frontier.Dequeue();
            double baseDistance = reached[sector];
            foreach (int index in Map.SectorLines[sector])
            {
                Line line = Map.Lines[index];
                if (line.Left == 65535 || (line.Flags & soundBlock) != 0) continue;
                int right = Map.Sides[line.Right].Sector, left = Map.Sides[line.Left].Sector;
                int other = right == sector ? left : right;
                if (other == sector) continue;
                Sector a = Map.Sectors[right], b = Map.Sectors[left];
                if (Math.Min(a.Ceiling, b.Ceiling) - Math.Max(a.Floor, b.Floor) <= 0) continue; // closed door
                double distance = baseDistance + (Map.Vertices[line.A] - Map.Vertices[line.B]).Length;
                if (distance > range || (reached.TryGetValue(other, out double existing) && existing <= distance))
                    continue;
                reached[other] = distance;
                frontier.Enqueue(other);
            }
        }
        foreach (Actor actor in Actors)
        {
            if (actor.Enemy && !actor.Removed && actor.Health > 0 && reached.ContainsKey(Map.FindSector(actor.Position)))
            { actor.Awake = true; actor.AlertPosition = origin; }
        }
    }

    void UpdateProjectiles(double dt)
    {
        foreach (Projectile p in Projectiles)
        {
            if (p.Exploded)
            {
                p.ExplosionTicks--;
                continue;
            }
            // Rockets, plasma balls and BFG balls are all player-fired and seek enemy actors;
            // anything else is an enemy fireball that only threatens the player.
            bool playerFired = p.Rocket || p.Plasma || p.Bfg;
            Vec next = p.Position + p.Velocity * dt;
            double impact = Map.ProjectileImpact(p.Position, next, p.Z, playerFired ? 11 : 0, playerFired ? 8 : 0);
            Actor? target = null;
            bool playerHit = false;
            if (playerFired)
            {
                foreach (Actor actor in Actors)
                {
                    if (actor == p.Owner || actor.Removed || !actor.Solid || actor.Health <= 0) continue;
                    double floor = Map.Sectors[Map.FindSector(actor.Position)].Floor;
                    if (p.Z + 4 <= floor || p.Z - 4 >= floor + 56) continue;
                    double hit = Map.CircleImpact(p.Position, next, actor.Position, actor.Radius + 11);
                    if (hit < impact) { impact = hit; target = actor; }
                }
            }
            else
            {
                p.Time -= dt;
                if (Health > 0 && Math.Abs(p.Z - (Feet + 28)) < 28)
                {
                    double hit = Map.CircleImpact(p.Position, next, Position, 20);
                    if (hit < impact) { impact = hit; playerHit = true; }
                }
                // Monster fireballs can also strike other monsters in their path, triggering infighting.
                foreach (Actor actor in Actors)
                {
                    if (actor == p.Owner || actor.Removed || !actor.Solid || actor.Health <= 0) continue;
                    double floor = Map.Sectors[Map.FindSector(actor.Position)].Floor;
                    if (p.Z + 4 <= floor || p.Z - 4 >= floor + 56) continue;
                    double hit = Map.CircleImpact(p.Position, next, actor.Position, actor.Radius);
                    if (hit < impact) { impact = hit; target = actor; playerHit = false; }
                }
            }
            if (impact <= 1)
            {
                p.Position += (next - p.Position) * impact;
                if (p.Rocket)
                {
                    // Mark the impact before damage: barrel chains cannot apply it a second time.
                    p.Exploded = true; p.ExplosionTicks = 18;
                    if (target != null && (target.Enemy || target.Type == 2035))
                        Hurt(target, random.Next(1, 9) * 20, p.Owner);
                    Blast(p.Position, p.Z, true, p.Owner);
                    PlaySound?.Invoke("BAREXP");
                }
                else if (p.Bfg)
                {
                    // A simplified but faithful BFG: a big direct hit plus a wide, strong splash.
                    // (The classic tracer "spray" that re-damages every visible monster is not modeled.)
                    p.Exploded = true; p.ExplosionTicks = 10;
                    if (target != null && (target.Enemy || target.Type == 2035))
                        Hurt(target, random.Next(1, 9) * 15 + 100, p.Owner);
                    Blast(p.Position, p.Z, true, p.Owner, radius: 256, maxDamage: 200);
                    PlaySound?.Invoke("RXPLOD");
                }
                else if (p.Plasma)
                {
                    // Direct-hit only: no splash damage, matching the classic plasma rifle.
                    p.Exploded = true; p.ExplosionTicks = 4;
                    if (target != null && (target.Enemy || target.Type == 2035))
                        Hurt(target, random.Next(1, 8) * 5, p.Owner);
                    PlaySound?.Invoke("FIRXPL");
                }
                else
                {
                    if (playerHit) { Damage(random.Next(1, 9) * 3); PlaySound?.Invoke("FIRXPL"); }
                    else if (target != null) { Hurt(target, random.Next(1, 9) * 3, p.Owner); PlaySound?.Invoke("FIRXPL"); }
                    p.Time = 0;
                }
                continue;
            }
            p.Position = next;
        }
        Projectiles.RemoveAll(p => p.Rocket || p.Plasma || p.Bfg ? p.Exploded && p.ExplosionTicks <= 0 : p.Time <= 0);
    }

    void Damage(int amount)
    {
        int absorb = Math.Min(Armor, amount / 3); Armor -= absorb; Health = Math.Max(0, Health - amount + absorb);
        DamageFlash = .3; PlaySound?.Invoke(Health > 0 ? "PLPAIN" : "PLDETH");
        NoiseAlert(Position); // player pain/death cries are also loud enough to wake nearby monsters
        if (Health == 0) Say("YOU DIED - PRESS R TO RESTART");
    }

    /// <summary>
    /// Thing types counted toward the classic "items" intermission stat: health/armor bonuses,
    /// megaspheres/soulspheres, berserk and keycards/skull keys. Ammo and weapon pickups are
    /// excluded, matching the original engine's <c>itemcount</c> semantics.
    /// </summary>
    static bool IsItemType(int type) => type is 2011 or 2012 or 2014 or 2015 or 2018 or 2019 or 2013 or 2023
        or 5 or 40 or 6 or 39 or 13 or 38;

    void Pickup(Actor actor)
    {
        bool taken = true;
        switch (actor.Type)
        {
            case 2001: HasShotgun = true; SelectWeapon(Weapon.Shotgun); Shells = Math.Min(50, Shells + 8); break;
            case 2002:
                if (HasChaingun && Bullets >= 200) { taken = false; break; }
                bool newChaingun = !HasChaingun;
                HasChaingun = true;
                Bullets = Math.Min(200, Bullets + (actor.Dropped ? 10 : 20));
                if (newChaingun) SelectWeapon(Weapon.Chaingun);
                break;
            case 2003:
                if (HasRocketLauncher && Rockets >= 50) { taken = false; break; }
                bool newLauncher = !HasRocketLauncher;
                HasRocketLauncher = true;
                Rockets = Math.Min(50, Rockets + 2);
                if (newLauncher) SelectWeapon(Weapon.RocketLauncher);
                break;
            case 2004:
                if (HasPlasmaRifle && Cells >= 300) { taken = false; break; }
                bool newPlasma = !HasPlasmaRifle;
                HasPlasmaRifle = true;
                Cells = Math.Min(300, Cells + 40);
                if (newPlasma) SelectWeapon(Weapon.PlasmaRifle);
                break;
            case 2006:
                if (HasBFG9000 && Cells >= 300) { taken = false; break; }
                bool newBfg = !HasBFG9000;
                HasBFG9000 = true;
                Cells = Math.Min(300, Cells + 40);
                if (newBfg) SelectWeapon(Weapon.BFG9000);
                break;
            case 2005:
                if (HasChainsaw) { taken = false; break; }
                HasChainsaw = true;
                SelectWeapon(Weapon.Chainsaw);
                break;
            case 2010: if (Rockets >= 50) taken = false; else Rockets++; break;
            case 2046: if (Rockets >= 50) taken = false; else Rockets = Math.Min(50, Rockets + 5); break;
            case 2007: if (Bullets >= 200) taken = false; else Bullets = Math.Min(200, Bullets + 10); break;
            case 2048: if (Bullets >= 200) taken = false; else Bullets = Math.Min(200, Bullets + 50); break;
            case 2008: if (Shells >= 50) taken = false; else Shells = Math.Min(50, Shells + 4); break;
            case 2049: if (Shells >= 50) taken = false; else Shells = Math.Min(50, Shells + 20); break;
            case 2047: if (Cells >= 300) taken = false; else Cells = Math.Min(300, Cells + 20); break;
            case 17: if (Cells >= 300) taken = false; else Cells = Math.Min(300, Cells + 100); break;
            case 2011: case 2012:
                if (Health >= 100) taken = false;
                else Health = Math.Min(100, Health + (actor.Type == 2011 ? 10 : 25));
                break;
            case 2014: Health = Math.Min(200, Health + 1); break;
            case 2015: Armor = Math.Min(200, Armor + 1); break;
            case 2018: if (Armor >= 100) taken = false; else Armor = 100; break;
            case 2019: if (Armor >= 200) taken = false; else Armor = 200; break;
            case 2013: Health = Math.Min(200, Health + 100); break;
            case 2023: Health = Math.Max(100, Health); break;
            case 5: case 40: Keys |= 1; break;
            case 6: case 39: Keys |= 2; break;
            case 13: case 38: Keys |= 4; break;
            default: taken = false; break;
        }
        if (!taken) return;
        actor.Removed = true; PickupFlash = .15; PlaySound?.Invoke("ITEMUP");
        if (IsItemType(actor.Type)) Items++;
    }

    void Say(string text) { Message = text; messageTime = 4; }

    /// <summary>Rounds a value/total ratio to a whole percentage, treating a zero total as 100% (no
    /// kills/items/secrets required means full credit, matching the original intermission screen).</summary>
    static int Percent(int value, int total) => total <= 0 ? 100 : (int)Math.Round(value * 100.0 / total);

    void FinishLevel(bool secret)
    {
        Finished = true; secretExit = secret;
        string? next = NextMapName(Map.Name, secretExit);
        CompletionPrompt = next != null && Wad.Maps.Contains(next) ?
            "FIRE TO CONTINUE" : "END OF AVAILABLE MAPS - R TO REPLAY";
        int minutes = (int)(Time / 60), seconds = (int)Time % 60;
        IntermissionStats =
        [
            Map.Name + " FINISHED",
            $"KILLS {Percent(Kills, TotalEnemies)}%  ITEMS {Percent(Items, TotalItems)}%  SECRETS {Percent(Secrets, TotalSecrets)}%",
            $"TIME {minutes:D2}:{seconds:D2}",
            CompletionPrompt,
        ];
        Say(CompletionPrompt);
    }

    void NextMap()
    {
        string? next = NextMapName(Map.Name, secretExit);
        if (next == null || !Wad.Maps.Contains(next)) { Say("END OF AVAILABLE MAPS - R TO REPLAY"); return; }
        Load(next, false);
    }

    public static string? NextMapName(string current, bool secret)
    {
        if (current.StartsWith('E'))
        {
            int episode = current[1] - '0', level = current[3] - '0';
            if (secret) return $"E{episode}M9";
            if (level == 8) return null;
            int next = level == 9 ? episode switch { 1 => 4, 2 => 6, 3 => 7, 4 => 3, _ => 1 } : level + 1;
            return $"E{episode}M{next}";
        }
        int number = int.Parse(current.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture);
        if (secret && number is 15 or 31) return number == 15 ? "MAP31" : "MAP32";
        if (number == 30) return null;
        return $"MAP{(number is 31 or 32 ? 16 : number + 1):D2}";
    }

    /// <summary>Starts (or restarts) music for the current map. Call once after wiring <see cref="PlayMusic"/>.</summary>
    public void StartMusic() => PlayMusic?.Invoke(MusicLumpName(Map.Name));

    /// <summary>
    /// Maps a map name to its classic "D_*" music lump name: episode/level-based for E#M# maps, and the
    /// well-known fixed per-level table for MAP## maps (matching the original engine's level-to-track order).
    /// </summary>
    public static string MusicLumpName(string map)
    {
        if (map.Length == 4 && map[0] == 'E') return $"D_E{map[1]}M{map[3]}";
        if (map.StartsWith("MAP", StringComparison.Ordinal) &&
            int.TryParse(map.AsSpan(3), out int level) && level is >= 1 and <= 32)
            return "D_" + Map32MusicNames[level - 1];
        return "D_" + map;
    }

    static readonly string[] Map32MusicNames =
    [
        "RUNNIN", "STALKS", "COUNTD", "BETWEE", "DOOM", "THE_DA", "SHAWN", "DDTBLU", "IN_CIT", "DEAD",
        "STLKS2", "THEDA2", "DOOM2", "DDTBL2", "RUNNI2", "DEAD2", "STLKS3", "ROMERO", "SHAWN2", "MESSAG",
        "COUNT2", "DDTBL3", "AMPIE", "THEDA3", "ADRIAN", "MESSG2", "ROMER2", "TENSE", "SHAWN3", "OPENIN",
        "EVIL", "ULTIMA"
    ];

    void Load(string name, bool reset)
    {
        Map = new(Wad, name); Assets = new(Wad, name);
        PlayMusic?.Invoke(MusicLumpName(name));
        if (reset || Health <= 0)
        {
            Health = 100; Armor = 0; Bullets = 50; Shells = 0; Rockets = 0; Cells = 0;
            HasShotgun = false; HasChaingun = false; HasRocketLauncher = false;
            HasChainsaw = false; HasPlasmaRifle = false; HasBFG9000 = false;
            SelectedWeapon = Weapon.Pistol;
        }
        Keys = 0; Kills = 0; Secrets = 0; Items = 0; Time = 0; FireTime = 0; Finished = false; secretExit = false;
        CompletionPrompt = ""; IntermissionStats = [];
        DamageFlash = 0; PickupFlash = 0; damageTick = 0; WalkTime = 0;
        chaingunTicks = 0; chaingunFollowup = false; chaingunRefiring = false; pendingWeapon = null;
        ChaingunFrame = 0; ChaingunFlashFrame = 0; ChaingunFlashTicks = 0;
        RocketFireTicks = 0; PlasmaFlashTicks = 0; BFGFlashTicks = 0;
        Spawn(); Say(name);
    }

    /// <summary>
    /// Serializes the current map, player stats/inventory and per-actor removed/health/alert/position state
    /// to JSON. In-flight projectiles are not preserved; <see cref="TryLoad"/> always resumes with none
    /// airborne.
    /// </summary>
    public string Save() => JsonSerializer.Serialize(new SaveGameState(
        6, Map.Name, Position.X, Position.Y, Angle, Feet,
        Health, Armor, Bullets, Shells, Rockets, HasShotgun, HasChaingun, HasRocketLauncher, SelectedWeapon,
        Keys, Kills, Secrets, Time, [.. foundSecrets],
        [.. Actors.Select(a => a.Removed)], [.. Actors.Select(a => a.Health)], Items,
        [.. Actors.Select(a => a.Awake)],
        [.. Actors.Select(a => a.AlertPosition?.X)],
        [.. Actors.Select(a => a.AlertPosition?.Y)],
        [.. Actors.Select(a => a.ChaseMemory)],
        [.. Actors.Select(a => a.LastKnownTargetPosition?.X)],
        [.. Actors.Select(a => a.LastKnownTargetPosition?.Y)],
        [.. Actors.Select(a => a.Target == null ? -1 : Actors.IndexOf(a.Target))],
        [.. Actors.Select(a => a.Position.X)],
        [.. Actors.Select(a => a.Position.Y)], Cells, HasChainsaw, HasPlasmaRifle, HasBFG9000),
        SaveJsonContext.Default.SaveGameState);

    /// <summary>
    /// Restores a snapshot produced by <see cref="Save"/>: validates the entire save (version, map,
    /// gameplay-field finiteness, actor-array shapes and target indices) against the save's target map
    /// <em>before touching any live game state</em>, then only on success reloads the map, rebuilds the
    /// deterministic actor list and reapplies each actor's saved removed/health/alert/chase-memory/
    /// infighting-target/position state by index plus the player's saved position/stats/inventory. Returns
    /// false (with <paramref name="error"/> set) for an unreadable, version-mismatched, unknown-map,
    /// non-finite, actor-count-mismatched, out-of-range-target-index or out-of-range-position save, leaving
    /// the current game state completely unchanged (no partial mutation of an in-progress game).
    /// </summary>
    public bool TryLoad(string json, out string? error)
    {
        SaveGameState? state;
        try { state = JsonSerializer.Deserialize(json, SaveJsonContext.Default.SaveGameState); }
        catch (JsonException ex) { error = $"malformed save ({ex.Message})"; return false; }
        if (state == null) { error = "empty save."; return false; }
        if (state.Version is not (1 or 2 or 3 or 4 or 5 or 6)) { error = $"unsupported save version {state.Version}."; return false; }
        if (!Wad.Maps.Contains(state.Map)) { error = $"save references unknown map {state.Map}."; return false; }
        if (!double.IsFinite(state.PositionX) || !double.IsFinite(state.PositionY) || !double.IsFinite(state.Angle) ||
            !double.IsFinite(state.Feet) || !double.IsFinite(state.Time))
        {
            error = "save contains a non-finite gameplay value."; return false;
        }
        // ActorRemoved/ActorHealth/FoundSecretSectors are declared non-nullable in SaveGameState, but that
        // is only a compile-time (nullable-reference) annotation: a hand-edited or otherwise corrupted save
        // can still deserialize an explicit JSON `null` into any of them, and dereferencing that null below
        // (.Length, foreach) would throw an unhandled NullReferenceException instead of the explicit
        // malformed-save error every other corrupt-save case already returns.
        if (state.ActorRemoved == null || state.ActorHealth == null || state.FoundSecretSectors == null)
        {
            error = "save is missing required actor/secret array data."; return false;
        }

        // Compute the target map's deterministic actor list in a local (not yet committed to the live
        // game) so every shape/index check below can run before any mutation, honoring the "fully succeed
        // or leave the existing game completely unchanged" contract.
        Map candidateMap = new(Wad, state.Map);
        (_, _, _, List<Actor> candidateActors, int candidateUnsupportedThings) = BuildSpawnState(candidateMap);
        // Candidate Assets are constructed here too, alongside the candidate map/actors, so a failure
        // partway through assembling the candidate state (map, assets, or actor layout) can never leave
        // the live Map/Assets pair inconsistent with each other or with the live Actors list.
        Assets candidateAssets = new(Wad, state.Map);
        if (state.ActorRemoved.Length != candidateActors.Count || state.ActorHealth.Length != candidateActors.Count ||
            state.ActorAwake != null && state.ActorAwake.Length != candidateActors.Count ||
            state.ActorAlertX != null && state.ActorAlertX.Length != candidateActors.Count ||
            state.ActorAlertY != null && state.ActorAlertY.Length != candidateActors.Count ||
            state.ActorChaseMemory != null && state.ActorChaseMemory.Length != candidateActors.Count ||
            state.ActorLastKnownTargetX != null && state.ActorLastKnownTargetX.Length != candidateActors.Count ||
            state.ActorLastKnownTargetY != null && state.ActorLastKnownTargetY.Length != candidateActors.Count ||
            state.ActorTargetIndex != null && state.ActorTargetIndex.Length != candidateActors.Count ||
            state.ActorPositionX != null && state.ActorPositionX.Length != candidateActors.Count ||
            state.ActorPositionY != null && state.ActorPositionY.Length != candidateActors.Count)
        {
            error = "save's actor layout no longer matches this map."; return false;
        }
        // -1 is the documented "no infighting target" sentinel (fight the player); any other negative
        // value or an index at/beyond the actor count is corrupt save data and must reject the whole load,
        // not silently coerce to the harmless "no target" default.
        if (state.ActorTargetIndex != null)
        {
            foreach (int targetIndex in state.ActorTargetIndex)
            {
                if (targetIndex != -1 && (targetIndex < 0 || targetIndex >= candidateActors.Count))
                {
                    error = $"save contains an out-of-range actor target index {targetIndex}."; return false;
                }
            }
        }
        // ChaseMemory is a countdown timer (see ChaseMemoryDuration/UpdateActor) that only ever runs from
        // its full duration down to its 0 "no chase memory" sentinel/default; a negative or non-finite
        // value can never arise from normal play and is corrupt save data, not a tighter gameplay cap.
        if (state.ActorChaseMemory != null)
        {
            foreach (double chaseMemory in state.ActorChaseMemory)
            {
                if (!double.IsFinite(chaseMemory) || chaseMemory < 0)
                {
                    error = $"save contains an invalid actor chase-memory value {chaseMemory}."; return false;
                }
            }
        }
        // ActorLastKnownTargetX/Y encode "no remembered position" as both elements null together; either
        // whole array present without the other, or one element present without its paired element, is an
        // impossible half-a-position and must be rejected rather than silently dropped (which previously
        // lost the remembered position instead of flagging the corruption).
        if (state.ActorLastKnownTargetX == null != (state.ActorLastKnownTargetY == null))
        {
            error = "save's actor last-known-target X/Y arrays are inconsistently present."; return false;
        }
        if (state.ActorLastKnownTargetX != null && state.ActorLastKnownTargetY != null)
        {
            for (int i = 0; i < state.ActorLastKnownTargetX.Length; i++)
            {
                double? lastX = state.ActorLastKnownTargetX[i], lastY = state.ActorLastKnownTargetY[i];
                if (lastX.HasValue != lastY.HasValue)
                {
                    error = $"save contains an actor last-known-target position with only one of X/Y set at index {i}.";
                    return false;
                }
                // double.IsFinite(double?) isn't a thing; check the underlying value once we know it's
                // present. A non-finite literal such as 1e999 deserializes to double.PositiveInfinity,
                // which NaN-only checks would miss.
                if (lastX.HasValue && (!double.IsFinite(lastX.Value) || !double.IsFinite(lastY!.Value)))
                {
                    error = $"save contains a non-finite actor last-known-target position at index {i}."; return false;
                }
            }
        }
        // ActorPositionX/ActorPositionY are the only trailing actor fields that are not optional *per
        // element* (every actor always has a position, live or spawned), only optional as a whole pair
        // (absent together on a version 1-4 save, falling back to each actor's deterministic spawn
        // position already computed into candidateActors above). Either array present without the other
        // is corrupt save data, as is any non-finite value or a value outside the WAD vertex/thing
        // coordinate format's signed 16-bit range (see Wad.I16), which bounds every legitimate map
        // coordinate; validating this before the live-mutation point below keeps the atomic-load contract
        // established by the chase-memory/target-index fix (a malformed save must never partially mutate
        // an in-progress game).
        if (state.ActorPositionX == null != (state.ActorPositionY == null))
        {
            error = "save's actor position X/Y arrays are inconsistently present."; return false;
        }
        if (state.ActorPositionX != null && state.ActorPositionY != null)
        {
            const double MapCoordinateLimit = 32768;
            for (int i = 0; i < state.ActorPositionX.Length; i++)
            {
                double x = state.ActorPositionX[i], y = state.ActorPositionY[i];
                if (!double.IsFinite(x) || !double.IsFinite(y))
                {
                    error = $"save contains a non-finite actor position at index {i}."; return false;
                }
                if (Math.Abs(x) > MapCoordinateLimit || Math.Abs(y) > MapCoordinateLimit)
                {
                    error = $"save contains an out-of-range actor position at index {i}."; return false;
                }
            }
        }

        // All validation above is complete; only now may live game state actually change.
        Map = candidateMap; Assets = candidateAssets;
        Actors.Clear(); Actors.AddRange(candidateActors);
        Projectiles.Clear(); foundSecrets.Clear(); bossDeathActivated = false;
        UnsupportedThings = candidateUnsupportedThings;
        for (int i = 0; i < Actors.Count; i++)
        {
            Actors[i].Removed = state.ActorRemoved[i];
            Actors[i].Health = state.ActorHealth[i];
            // Absent (version 1/2) means "not awake", matching how those saves always behaved before
            // this field existed; present means round-trip the monster's noise-alert pursuit exactly.
            Actors[i].Awake = state.ActorAwake?[i] ?? false;
            double? alertX = state.ActorAlertX?[i], alertY = state.ActorAlertY?[i];
            Actors[i].AlertPosition = Actors[i].Awake && alertX.HasValue && alertY.HasValue
                ? new Vec(alertX.Value, alertY.Value) : null;
            // Absent (version 1/2/3) means "no chase memory/infighting target", matching how those saves
            // always behaved before these fields existed; present (version 4) round-trips a monster's
            // remembered-position pursuit and its infighting target exactly.
            Actors[i].ChaseMemory = state.ActorChaseMemory?[i] ?? 0;
            double? lastX = state.ActorLastKnownTargetX?[i], lastY = state.ActorLastKnownTargetY?[i];
            Actors[i].LastKnownTargetPosition = Actors[i].ChaseMemory > 0 && lastX.HasValue && lastY.HasValue
                ? new Vec(lastX.Value, lastY.Value) : null;
            int targetIndex = state.ActorTargetIndex?[i] ?? -1;
            Actors[i].Target = targetIndex == -1 ? null : Actors[targetIndex];
            // Absent (version 1-4) means "keep this map's deterministic spawn position", which is exactly
            // what candidateActors (just copied into Actors above) already holds via BuildSpawnState;
            // present (version 5) round-trips each actor's actual live position, so a monster that had
            // moved before the save reloads where it actually was instead of snapping back to its spawn.
            if (state.ActorPositionX != null && state.ActorPositionY != null)
            {
                Actors[i].Position = new Vec(state.ActorPositionX[i], state.ActorPositionY[i]);
            }
        }
        foundSecrets.Clear();
        foreach (int sector in state.FoundSecretSectors) foundSecrets.Add(sector);
        Position = new(state.PositionX, state.PositionY); Angle = state.Angle; Feet = state.Feet;
        Health = state.Health; Armor = state.Armor; Bullets = state.Bullets; Shells = state.Shells; Rockets = state.Rockets;
        Cells = state.Cells;
        HasShotgun = state.HasShotgun; HasChaingun = state.HasChaingun; HasRocketLauncher = state.HasRocketLauncher;
        HasChainsaw = state.HasChainsaw; HasPlasmaRifle = state.HasPlasmaRifle; HasBFG9000 = state.HasBFG9000;
        SelectedWeapon = state.SelectedWeapon; Keys = state.Keys; Kills = state.Kills; Secrets = state.Secrets; Time = state.Time;
        Items = state.Items;
        Finished = false; CompletionPrompt = ""; IntermissionStats = []; secretExit = false; DamageFlash = 0; PickupFlash = 0;
        chaingunTicks = 0; chaingunFollowup = false; chaingunRefiring = false; pendingWeapon = null;
        ChaingunFrame = 0; ChaingunFlashFrame = 0; ChaingunFlashTicks = 0; RocketFireTicks = 0;
        PlasmaFlashTicks = 0; BFGFlashTicks = 0;
        PlayMusic?.Invoke(MusicLumpName(Map.Name));
        error = null; return true;
    }
}
