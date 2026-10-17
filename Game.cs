namespace DoomSharp;

[Flags]
public enum Buttons
{
    None = 0, Forward = 1, Back = 2, Left = 4, Right = 8, TurnLeft = 16, TurnRight = 32,
    Fire = 64, Use = 128, Run = 256, Restart = 512, Pistol = 1024, Shotgun = 2048, Map = 4096,
    Chaingun = 8192, RocketLauncher = 16384
}

public enum Weapon { Pistol, Shotgun, Chaingun, RocketLauncher }

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
    public bool Rocket, Exploded;
    public int ExplosionTicks;
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
    public bool HasShotgun { get; private set; }
    public bool HasChaingun { get; private set; }
    public bool HasRocketLauncher { get; private set; }
    public Weapon SelectedWeapon { get; private set; }
    public bool ShotgunSelected => SelectedWeapon == Weapon.Shotgun;
    public int ChaingunFrame { get; private set; }
    public int ChaingunFlashFrame { get; private set; }
    public int ChaingunFlashTicks { get; private set; }
    public int RocketFireTicks { get; private set; }
    public int Ammo => SelectedWeapon switch
    {
        Weapon.Shotgun => Shells, Weapon.RocketLauncher => Rockets, _ => Bullets
    };
    public int Keys { get; private set; }
    public int Kills { get; private set; }
    public int TotalEnemies => Actors.Count(a => a.Enemy);
    public int Secrets { get; private set; }
    public double Time { get; private set; }
    public double FireTime { get; private set; }
    public double DamageFlash { get; private set; }
    public double PickupFlash { get; private set; }
    public double WalkTime { get; private set; }
    public double Bob { get; private set; }
    public bool Automap { get; private set; }
    public bool Finished { get; private set; }
    public string CompletionPrompt { get; private set; } = "";
    public string Message { get; private set; } = "";
    public List<Actor> Actors { get; } = [];
    public List<Projectile> Projectiles { get; } = [];
    public Action<string>? PlaySound;
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
        Thing? start = Map.Things.Cast<Thing?>().FirstOrDefault(t => t!.Value.Type == 1);
        if (start == null) throw new InvalidDataException($"{Map.Name} has no player-one start.");
        Position = start.Value.Position; Angle = start.Value.Angle * Math.PI / 180;
        Feet = Map.FloorAt(Position, 16);
        Actors.Clear(); Projectiles.Clear(); foundSecrets.Clear();
        bossDeathActivated = false;
        UnsupportedThings = 0;
        foreach (Thing thing in Map.Things)
        {
            if (thing.Type is >= 1 and <= 4 or 11 or 14 || (thing.Flags & 16) != 0 || (thing.Flags & 2) == 0) continue;
            Actor? actor = CreateActor(thing);
            if (actor == null)
            {
                UnsupportedThings++;
                Console.Error.WriteLine($"Unsupported thing type {thing.Type} at {thing.Position}; skipped.");
            }
            else Actors.Add(actor);
        }
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
        2010 => new(t, "ROCK", false, false, 16, 1),
        2046 => new(t, "BROK", false, false, 16, 1),
        2007 => new(t, "CLIP", false, false, 16, 1),
        2048 => new(t, "AMMO", false, false, 16, 1),
        2008 => new(t, "SHEL", false, false, 16, 1),
        2049 => new(t, "SBOX", false, false, 16, 1),
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
        DamageFlash = Math.Max(0, DamageFlash - dt); PickupFlash = Math.Max(0, PickupFlash - dt);
        messageTime -= dt;
        if (messageTime <= 0) Message = "";
        Buttons pressed = input.Held & ~previous;
        previous = input.Held;
        if ((pressed & Buttons.Restart) != 0) { Load(Map.Name, true); return; }
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
            if (!Map.CanMove(destination.Position, destination.Position, 16, 56, sector.Floor) ||
                Actors.Any(a => !a.Removed && a.Solid && a.Health > 0 &&
                    (a.Position - destination.Position).Length < a.Radius + 16))
            { Say("TELEPORT DESTINATION BLOCKED"); return true; }
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
        if (ActivateStairs(line, crossing) || ActivateFloor(line, crossing)) return;
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

    bool ActivateStairs(Line line, bool crossing)
    {
        int special = line.Special;
        if (!(crossing ? special is 8 or 100 : special is 7 or 127)) return false;
        if (!crossing && Vec.Cross(Map.Vertices[line.B] - Map.Vertices[line.A],
            Position - Map.Vertices[line.A]) > 0) { PlaySound?.Invoke("NOWAY"); return true; }
        bool turbo = special is 100 or 127;
        double step = turbo ? 16 : 8, speed = turbo ? 140 : 8.75;
        bool targeted = false, activated = false;
        HashSet<int> visited = [];
        foreach (int seed in TaggedSectors(line))
        {
            targeted = true;
            if (Map.Sectors[seed].FloorDirection != 0 || visited.Contains(seed)) continue;
            string texture = Map.Sectors[seed].FloorTexture;
            double target = Map.Sectors[seed].Floor + step;
            int current = seed;
            while (true)
            {
                visited.Add(current);
                activated |= StartFloor(Map.Sectors[current], target, false, speed);
                int next = -1;
                // Stair chains follow front-to-back two-sided lines in map order.
                foreach (int index in Map.SectorLines[current])
                {
                    Line edge = Map.Lines[index];
                    if (edge.Left == 65535 || (edge.Flags & 4) == 0 ||
                        Map.Sides[edge.Right].Sector != current) continue;
                    int candidate = Map.Sides[edge.Left].Sector;
                    Sector sector = Map.Sectors[candidate];
                    if (visited.Contains(candidate) || sector.FloorDirection != 0 || sector.FloorTexture != texture) continue;
                    next = candidate;
                    break;
                }
                if (next < 0) break;
                current = next;
                target += step;
            }
        }
        if (activated) { line.Special = 0; PlaySound?.Invoke("PSTART"); }
        if (!targeted) { Say("STAIRS HAVE NO TAGGED SECTOR"); PlaySound?.Invoke("NOWAY"); }
        return true;
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
            weapon == Weapon.RocketLauncher && !HasRocketLauncher)
        {
            Say(weapon == Weapon.RocketLauncher ? "ROCKET LAUNCHER NOT OWNED" :
                $"{weapon.ToString().ToUpperInvariant()} NOT OWNED");
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

    void Hurt(Actor actor, int damage)
    {
        if (actor.Removed || actor.Health <= 0) return;
        actor.Health -= damage; actor.HurtTime = .2;
        PlaySound?.Invoke("POPAIN");
        if (actor.Health > 0) return;
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
            Blast(actor.Position, Map.Sectors[Map.FindSector(actor.Position)].Floor + 32, false);
        }
    }

    void Blast(Vec origin, double z, bool rocket)
    {
        foreach (Actor other in Actors.ToArray())
            if (!other.Removed && other.Health > 0 && (other.Enemy || other.Type == 2035))
            {
                double distance = Math.Max(0, (other.Position - origin).Length - (rocket ? other.Radius : 0));
                if (distance < 128 && Map.ClearSight(origin, other.Position, z))
                    Hurt(other, Math.Max(1, (int)(128 - distance)));
            }
        double playerDistance = Math.Max(0, (Position - origin).Length - (rocket ? 16 : 0));
        if (playerDistance < 128 && Map.ClearSight(origin, Position, rocket ? z : Eye))
            Damage(rocket ? Math.Max(1, (int)(128 - playerDistance)) : (int)(128 - playerDistance));
    }

    void UpdateActor(Actor actor, double dt)
    {
        actor.HurtTime = Math.Max(0, actor.HurtTime - dt); actor.Cooldown -= dt;
        if (actor.Removed) return;
        if (actor.Health <= 0) { actor.DeathTime += dt; return; }
        if (!actor.Enemy || Health <= 0 || Finished) return;
        Vec delta = Position - actor.Position;
        double distance = delta.Length;
        double floor = Map.Sectors[Map.FindSector(actor.Position)].Floor;
        if (distance > 1500 || !Map.ClearSight(actor.Position, Position, floor + 40)) return;
        if (actor.Cooldown <= 0 && (distance < 64 || actor.Type is not (3002 or 58) && distance < 800))
        {
            actor.Cooldown = 1.1 + random.NextDouble();
            if (distance < 64) Damage(random.Next(3, 9) * 3);
            else if (actor.Type is 3004 or 9 or 65 or 7)
            {
                PlaySound?.Invoke(actor.Type == 9 ? "SHOTGN" : "PISTOL");
                if (random.NextDouble() < Math.Clamp(1 - distance / 1200, .15, .9))
                    Damage(random.Next(1, 5) * (actor.Type == 9 ? 5 : 3));
            }
            else
            {
                Projectiles.Add(new() { Position = actor.Position, Z = floor + 32, Velocity = delta * (220 / distance) });
                PlaySound?.Invoke("FIRSHT");
            }
        }
        if (distance > actor.Radius + 20 && actor.HurtTime == 0)
        {
            Vec move = delta * (dt * (actor.Type is 3002 or 58 ? 90 : 42) / distance);
            Vec next = actor.Position + move;
            if (Map.CanMove(actor.Position, next, actor.Radius, 56, floor, true) &&
                !Actors.Any(a => a != actor && a.Solid && !a.Removed && a.Health > 0 &&
                    (a.Position - next).Length < actor.Radius + a.Radius))
                actor.Position = next;
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
            Vec next = p.Position + p.Velocity * dt;
            double impact = Map.ProjectileImpact(p.Position, next, p.Z, p.Rocket ? 11 : 0, p.Rocket ? 8 : 0);
            Actor? target = null;
            bool playerHit = false;
            if (p.Rocket)
            {
                foreach (Actor actor in Actors)
                {
                    if (actor.Removed || !actor.Solid || actor.Health <= 0) continue;
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
            }
            if (impact <= 1)
            {
                p.Position += (next - p.Position) * impact;
                if (p.Rocket)
                {
                    // Mark the impact before damage: barrel chains cannot apply it a second time.
                    p.Exploded = true; p.ExplosionTicks = 18;
                    if (target != null && (target.Enemy || target.Type == 2035))
                        Hurt(target, random.Next(1, 9) * 20);
                    Blast(p.Position, p.Z, true);
                    PlaySound?.Invoke("BAREXP");
                }
                else
                {
                    if (playerHit) { Damage(random.Next(1, 9) * 3); PlaySound?.Invoke("FIRXPL"); }
                    p.Time = 0;
                }
                continue;
            }
            p.Position = next;
        }
        Projectiles.RemoveAll(p => p.Rocket ? p.Exploded && p.ExplosionTicks <= 0 : p.Time <= 0);
    }

    void Damage(int amount)
    {
        int absorb = Math.Min(Armor, amount / 3); Armor -= absorb; Health = Math.Max(0, Health - amount + absorb);
        DamageFlash = .3; PlaySound?.Invoke(Health > 0 ? "PLPAIN" : "PLDETH");
        if (Health == 0) Say("YOU DIED - PRESS R TO RESTART");
    }

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
            case 2010: if (Rockets >= 50) taken = false; else Rockets++; break;
            case 2046: if (Rockets >= 50) taken = false; else Rockets = Math.Min(50, Rockets + 5); break;
            case 2007: if (Bullets >= 200) taken = false; else Bullets = Math.Min(200, Bullets + 10); break;
            case 2048: if (Bullets >= 200) taken = false; else Bullets = Math.Min(200, Bullets + 50); break;
            case 2008: if (Shells >= 50) taken = false; else Shells = Math.Min(50, Shells + 4); break;
            case 2049: if (Shells >= 50) taken = false; else Shells = Math.Min(50, Shells + 20); break;
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
    }

    void Say(string text) { Message = text; messageTime = 4; }

    void FinishLevel(bool secret)
    {
        Finished = true; secretExit = secret;
        string? next = NextMapName(Map.Name, secretExit);
        CompletionPrompt = next != null && Wad.Maps.Contains(next) ?
            "LEVEL COMPLETE - FIRE TO CONTINUE" : "END OF AVAILABLE MAPS - R TO REPLAY";
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

    void Load(string name, bool reset)
    {
        Map = new(Wad, name); Assets = new(Wad, name);
        if (reset || Health <= 0)
        {
            Health = 100; Armor = 0; Bullets = 50; Shells = 0; Rockets = 0;
            HasShotgun = false; HasChaingun = false; HasRocketLauncher = false;
            SelectedWeapon = Weapon.Pistol;
        }
        Keys = 0; Kills = 0; Secrets = 0; Time = 0; FireTime = 0; Finished = false; secretExit = false;
        CompletionPrompt = "";
        DamageFlash = 0; PickupFlash = 0; damageTick = 0; WalkTime = 0;
        chaingunTicks = 0; chaingunFollowup = false; chaingunRefiring = false; pendingWeapon = null;
        ChaingunFrame = 0; ChaingunFlashFrame = 0; ChaingunFlashTicks = 0;
        RocketFireTicks = 0;
        Spawn(); Say(name);
    }
}
