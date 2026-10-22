namespace DoomSharp;

public sealed class Renderer
{
    public int Width { get; }
    public int Height { get; }
    public int[] Pixels { get; }
    readonly double[] depth;
    readonly int[] skyPixels;
    readonly HashSet<string> missingSprites = [];
    readonly int viewHeight;
    readonly double focal;
    Vec position, forward, right;
    double eye, horizon;
    bool fullBright;
    Game game = null!;
    public int VisibleWallPixels { get; private set; }
    public int VisiblePlanePixels { get; private set; }
    public int VisibleSpritePixels { get; private set; }
    public int VisibleWeaponPixels { get; private set; }
    int tics;

    readonly record struct Vertex(double X, double Y, double Z, double U, double V);
    readonly record struct Screen(double X, double Y, double InvZ, double U, double V);

    public Renderer(int width = 640, int height = 400)
    {
        Width = width; Height = height; viewHeight = height - height / 8;
        focal = width / 2.0; horizon = viewHeight / 2.0;
        Pixels = new int[width * height]; depth = new double[width * viewHeight]; skyPixels = new int[width * viewHeight];
    }

    public void Render(Game state)
    {
        game = state; position = state.Position; eye = state.Eye;
        tics = (int)Math.Round(state.Time * 35.0);
        forward = new(Math.Cos(state.Angle), Math.Sin(state.Angle)); right = new(forward.Y, -forward.X);
        fullBright = state.LightAmpTime > 0;
        Array.Fill(depth, double.PositiveInfinity);
        Array.Fill(Pixels, 0x15151b);
        VisibleWallPixels = VisiblePlanePixels = VisibleSpritePixels = VisibleWeaponPixels = 0;
        DrawSky();
        Map map = state.Map;
        foreach (int subIndex in map.BackToFront(position))
        {
            Subsector sub = map.Subsectors[subIndex];
            Seg first = map.Segs[sub.First];
            Line firstLine = map.Lines[first.Line];
            Sector sector = map.Sectors[map.Sides[first.Direction == 0 ? firstLine.Right : firstLine.Left].Sector];
            Vec[] polygon = map.SubsectorPolygons[subIndex];
            for (int i = 1; i < polygon.Length - 1; i++)
            {
                Vec a = polygon[0], b = polygon[i], c = polygon[i + 1];
                if (eye > sector.Floor) Plane(a, b, c, sector.Floor, state.Assets.Flat(AnimatedFlat(sector.FloorTexture)), sector.Light);
                if (eye < sector.Ceiling)
                    Plane(a, b, c, sector.Ceiling, sector.CeilingTexture == "F_SKY1" ?
                        state.Assets.Texture(state.Assets.SkyName) : state.Assets.Flat(AnimatedFlat(sector.CeilingTexture)),
                        sector.Light, sector.CeilingTexture == "F_SKY1");
            }
            for (int i = 0; i < sub.Count; i++) DrawSeg(map.Segs[sub.First + i]);
        }
        DrawActors();
        if (state.Automap) DrawMap();
        else DrawWeapon();
        DrawHud();
        if (state.InvulnerabilityTime > 0)
            for (int i = 0; i < Width * viewHeight; i++)
            {
                int color = Pixels[i];
                int gray = ((color >> 16 & 255) + (color >> 8 & 255) + (color & 255)) / 3;
                int inverted = 255 - gray;
                Pixels[i] = inverted << 16 | inverted << 8 | inverted;
            }
        if (state.DamageFlash > 0 || state.PickupFlash > 0)
        {
            int tint = state.DamageFlash > 0 ? 0x900000 : 0x806000;
            for (int i = 0; i < Width * viewHeight; i++) Pixels[i] = Blend(Pixels[i], tint);
        }
    }

    void DrawSky()
    {
        Picture sky = game.Assets.Texture(game.Assets.SkyName);
        for (int x = 0; x < Width; x++)
        {
            double angle = game.Angle - Math.Atan((x - Width / 2.0) / focal);
            int sx = (int)(angle / Math.Tau * sky.Width * 4);
            for (int y = 0; y < viewHeight; y++)
                Pixels[y * Width + x] = skyPixels[y * Width + x] =
                    Math.Max(0, sky.Sample(sx, Math.Clamp(y * sky.Height / viewHeight, 0, sky.Height - 1)));
        }
    }

    Vertex World(Vec p, double z, double u, double v)
    {
        Vec d = p - position;
        return new(Vec.Dot(d, right), z - eye, Vec.Dot(d, forward), u, v);
    }

    void Plane(Vec a, Vec b, Vec c, double z, Picture texture, int light, bool sky = false)
    {
        DrawPolygon([World(a, z, a.X, -a.Y), World(b, z, b.X, -b.Y), World(c, z, c.X, -c.Y)], texture, light, sky ? 3 : 1);
    }

    void DrawSeg(Seg seg)
    {
        Map map = game.Map; Line line = map.Lines[seg.Line];
        Vec a = map.Vertices[seg.A], b = map.Vertices[seg.B];
        if (Vec.Cross(b - a, position - a) >= 0) return;
        Side side = map.Sides[seg.Direction == 0 ? line.Right : line.Left];
        Sector front = map.Sectors[side.Sector];
        int backSide = seg.Direction == 0 ? line.Left : line.Right;
        double u = side.XOffset + seg.Offset;
        double length = (b - a).Length;
        if (backSide == 65535)
        {
            Picture texture = game.Assets.Texture(AnimatedWall(side.Middle));
            double topAnchor = (line.Flags & 16) != 0 ? front.Floor + texture.Height : front.Ceiling;
            Wall(a, b, front.Floor, front.Ceiling, u, u + length, topAnchor + side.YOffset, texture, front.Light);
            return;
        }
        Sector back = map.Sectors[map.Sides[backSide].Sector];
        if (front.Ceiling > back.Ceiling && !(front.CeilingTexture == "F_SKY1" && back.CeilingTexture == "F_SKY1") && side.Upper != "-")
        {
            Picture texture = game.Assets.Texture(AnimatedWall(side.Upper));
            double topAnchor = (line.Flags & 8) != 0 ? front.Ceiling : back.Ceiling + texture.Height;
            Wall(a, b, back.Ceiling, front.Ceiling, u, u + length, topAnchor + side.YOffset, texture, front.Light);
        }
        if (front.Floor < back.Floor && side.Lower != "-")
        {
            Picture texture = game.Assets.Texture(AnimatedWall(side.Lower));
            double topAnchor = (line.Flags & 16) != 0 ? front.Ceiling : back.Floor;
            Wall(a, b, front.Floor, back.Floor, u, u + length, topAnchor + side.YOffset, texture, front.Light);
        }
        if (side.Middle != "-")
        {
            Picture texture = game.Assets.Texture(AnimatedWall(side.Middle));
            double bottom = Math.Max(front.Floor, back.Floor), top = Math.Min(front.Ceiling, back.Ceiling);
            double anchor = ((line.Flags & 16) != 0 ? bottom + texture.Height : top) + side.YOffset;
            bottom = Math.Max(bottom, anchor - texture.Height); top = Math.Min(top, anchor);
            if (top > bottom) Wall(a, b, bottom, top, u, u + length, anchor, texture, front.Light);
        }
    }

    string AnimatedFlat(string name) => AnimatedFlatName(name, tics, game.Wad.HasFlat);
    string AnimatedWall(string name) => AnimatedWallName(name, tics, game.Wad.HasTexture);

    /// <summary>
    /// The flat name the renderer would display for the given tic count, substituting the matching
    /// animation-cycle frame only when <paramref name="exists"/> confirms it is present in the WAD.
    /// </summary>
    public static string AnimatedFlatName(string name, int tics, Func<string, bool> exists) => TextureAnimation.Flat(name, tics, exists);

    /// <summary>
    /// The wall texture name the renderer would display for the given tic count, substituting the matching
    /// animation-cycle frame only when <paramref name="exists"/> confirms it is present in the WAD.
    /// </summary>
    public static string AnimatedWallName(string name, int tics, Func<string, bool> exists) => TextureAnimation.Wall(name, tics, exists);

    void Wall(Vec a, Vec b, double bottom, double top, double u0, double u1, double anchor, Picture texture, int light)
    {
        if (top <= bottom) return;
        DrawPolygon([World(a, bottom, u0, anchor - bottom), World(b, bottom, u1, anchor - bottom),
            World(b, top, u1, anchor - top), World(a, top, u0, anchor - top)], texture, light, 0);
    }

    void DrawPolygon(Vertex[] vertices, Picture texture, int light, int kind)
    {
        // Near-plane clipping is performed before perspective division.
        List<Vertex> clipped = [];
        Vertex previous = vertices[^1];
        foreach (Vertex current in vertices)
        {
            if ((previous.Z >= 1) != (current.Z >= 1))
            {
                double t = (1 - previous.Z) / (current.Z - previous.Z);
                clipped.Add(new(previous.X + (current.X - previous.X) * t,
                    previous.Y + (current.Y - previous.Y) * t, 1,
                    previous.U + (current.U - previous.U) * t, previous.V + (current.V - previous.V) * t));
            }
            if (current.Z >= 1) clipped.Add(current);
            previous = current;
        }
        if (clipped.Count < 3) return;
        Screen Project(Vertex v) => new(Width / 2.0 + v.X * focal / v.Z, horizon - v.Y * focal / v.Z,
            1 / v.Z, v.U / v.Z, v.V / v.Z);
        Screen first = Project(clipped[0]);
        for (int i = 1; i < clipped.Count - 1; i++) Triangle(first, Project(clipped[i]), Project(clipped[i + 1]), texture, light, kind);
    }

    void Triangle(Screen a, Screen b, Screen c, Picture texture, int light, int kind)
    {
        double area = Edge(a, b, c.X, c.Y);
        if (Math.Abs(area) < 1e-6) return;
        int minX = (int)Math.Clamp(Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, Width - 1);
        int maxX = (int)Math.Clamp(Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, Width - 1);
        int minY = (int)Math.Clamp(Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, viewHeight - 1);
        int maxY = (int)Math.Clamp(Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, viewHeight - 1);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double w0 = Edge(b, c, x + .5, y + .5) / area;
                double w1 = Edge(c, a, x + .5, y + .5) / area;
                double w2 = 1 - w0 - w1;
                if (w0 < -1e-8 || w1 < -1e-8 || w2 < -1e-8) continue;
                double inverse = w0 * a.InvZ + w1 * b.InvZ + w2 * c.InvZ;
                double distance = 1 / inverse;
                int index = y * Width + x;
                if (distance >= depth[index]) continue;
                if (kind == 3)
                {
                    depth[index] = distance; Pixels[index] = skyPixels[index]; VisiblePlanePixels++;
                    continue;
                }
                int u = (int)Math.Floor((w0 * a.U + w1 * b.U + w2 * c.U) * distance);
                int v = (int)Math.Floor((w0 * a.V + w1 * b.V + w2 * c.V) * distance);
                if (kind == 2 && (u < 0 || v < 0 || u >= texture.Width || v >= texture.Height)) continue;
                int color = texture.Sample(u, v);
                if (color < 0) continue;
                depth[index] = distance;
                Pixels[index] = Shade(color, light, distance);
                if (kind == 0) VisibleWallPixels++; else if (kind == 1) VisiblePlanePixels++; else VisibleSpritePixels++;
            }
    }

    static double Edge(Screen a, Screen b, double x, double y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
    int Shade(int color, int light, double distance)
    {
        double factor = fullBright ? 1 : Math.Clamp(light / 255.0 - distance / 2400, .12, 1);
        return (int)((color >> 16 & 255) * factor) << 16 |
            (int)((color >> 8 & 255) * factor) << 8 | (int)((color & 255) * factor);
    }

    void DrawActors()
    {
        foreach (Actor actor in game.Actors)
        {
            if (actor.Removed) continue;
            char frame = actor.FixedFrame ?? (actor.Health <= 0 && actor.Enemy ? (char)('H' + Math.Min(4, (int)(actor.DeathTime * 8))) :
                actor.HurtTime > 0 && actor.Enemy ? 'G' : (char)('A' + (actor.Enemy ? (int)(game.Time * 5) % 4 : 0)));
            Picture? picture = Sprite(actor.Sprite, frame, actor);
            if (picture == null) continue;
            double floor = game.Map.Sectors[game.Map.FindSector(actor.Position)].Floor;
            if (actor.Hanging) floor = game.Map.Sectors[game.Map.FindSector(actor.Position)].Ceiling - picture.Top;
            Billboard(actor.Position, floor, picture, game.Map.Sectors[game.Map.FindSector(actor.Position)].Light);
        }
        foreach (Projectile projectile in game.Projectiles)
        {
            Picture? picture = ProjectileSprite(projectile);
            bool playerFired = projectile.Rocket || projectile.Plasma || projectile.Bfg;
            if (picture != null)
                Billboard(projectile.Position, playerFired ? projectile.Z + picture.Height / 2 -
                    (picture.Top > 0 ? picture.Top : picture.Height) : projectile.Z - picture.Height / 2, picture, 255);
        }
    }

    Picture? ProjectileSprite(Projectile projectile)
    {
        if (projectile.Plasma)
        {
            string name = projectile.Exploded ? (projectile.ExplosionTicks > 2 ? "PLSEA0" : "PLSEB0") : "PLSSA0";
            Picture? sprite = game.Assets.Patch(name);
            if (sprite == null && missingSprites.Add(name))
                Console.Error.WriteLine($"Missing projectile patch {name}; plasma ball cannot be drawn.");
            return sprite;
        }
        if (projectile.Bfg)
        {
            string name = projectile.Exploded ?
                (projectile.ExplosionTicks > 6 ? "BFE1A0" : projectile.ExplosionTicks > 3 ? "BFE1B0" : "BFE1C0") :
                "BFS1A0";
            Picture? sprite = game.Assets.Patch(name);
            if (sprite == null && missingSprites.Add(name))
                Console.Error.WriteLine($"Missing projectile patch {name}; BFG ball cannot be drawn.");
            return sprite;
        }
        if (!projectile.Rocket) return game.Assets.Patch("BAL1A0");
        if (projectile.Exploded)
        {
            string name = projectile.ExplosionTicks > 10 ? "MISLB0" :
                projectile.ExplosionTicks > 4 ? "MISLC0" : "MISLD0";
            Picture? explosion = game.Assets.Patch(name);
            if (explosion == null && missingSprites.Add(name))
                Console.Error.WriteLine($"Missing projectile patch {name}; rocket explosion cannot be drawn.");
            return explosion;
        }
        double heading = Math.Atan2(projectile.Velocity.Y, projectile.Velocity.X);
        int rotation = 1 + ((int)Math.Round((Math.Atan2(position.Y - projectile.Position.Y,
            position.X - projectile.Position.X) - heading) / Math.Tau * 8) + 8) % 8;
        Picture? missile = RotatedSprite("MISL", 'A', rotation);
        if (missile == null && missingSprites.Add("MISLA"))
            Console.Error.WriteLine("Missing projectile sprite MISLA; flying rockets cannot be drawn.");
        return missile;
    }

    Picture? Sprite(string prefix, char frame, Actor actor)
    {
        // Actors face their chase target; choose an appropriate classic eight-way patch.
        double direction = Math.Atan2(game.Position.Y - actor.Position.Y, game.Position.X - actor.Position.X);
        int rotation = 1 + ((int)Math.Round((Math.Atan2(position.Y - actor.Position.Y, position.X - actor.Position.X) -
            direction) / Math.Tau * 8) + 8) % 8;
        Picture? patch = RotatedSprite(prefix, frame, rotation);
        if (patch != null) return patch;
        Picture? fallback = game.Assets.Patch($"{prefix}A0") ?? game.Assets.Patch($"{prefix}A1");
        if (fallback == null && missingSprites.Add(prefix))
            Console.Error.WriteLine($"Missing sprite family {prefix}; its actors cannot be drawn.");
        return fallback;
    }

    Picture? RotatedSprite(string prefix, char frame, int rotation)
    {
        Picture? patch = game.Assets.Patch($"{prefix}{frame}0") ?? game.Assets.Patch($"{prefix}{frame}{rotation}");
        if (patch != null) return patch;
        foreach (Wad.Lump lump in game.Wad.Lumps)
            if (lump.Name.Length == 8 && lump.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                (lump.Name[4] == frame && lump.Name[5] == '0' + rotation ||
                 lump.Name[6] == frame && lump.Name[7] == '0' + rotation))
            {
                patch = game.Assets.Patch(lump.Name);
                if (patch == null) continue;
                if (lump.Name[6] == frame && lump.Name[7] == '0' + rotation)
                {
                    Picture flipped = new(patch.Width, patch.Height, patch.Width - patch.Left, patch.Top);
                    for (int y = 0; y < patch.Height; y++)
                        for (int x = 0; x < patch.Width; x++) flipped.Pixels[y * patch.Width + x] = patch.Pixels[y * patch.Width + patch.Width - 1 - x];
                    return flipped;
                }
                return patch;
            }
        return null;
    }

    void Billboard(Vec center, double floor, Picture p, int light)
    {
        Vec a = center - right * p.Left, b = a + right * p.Width;
        double top = floor + p.Top;
        if (p.Top <= 0) top = floor + p.Height;
        DrawPolygon([World(a, top - p.Height, 0, p.Height), World(b, top - p.Height, p.Width, p.Height),
            World(b, top, p.Width, 0), World(a, top, 0, 0)], p, light, 2);
    }

    void DrawWeapon()
    {
        if (game.Health <= 0) return;
        string name = game.SelectedWeapon switch
        {
            Weapon.RocketLauncher => game.RocketFireTicks > 12 ? "MISGB0" : "MISGA0",
            Weapon.Chaingun => game.ChaingunFrame == 0 ? "CHGGA0" : "CHGGB0",
            Weapon.Shotgun => game.FireTime > .65 ? "SHTGB0" : "SHTGA0",
            Weapon.PlasmaRifle => game.FireTime > .05 ? "PLSGB0" : "PLSGA0",
            Weapon.BFG9000 => game.FireTime > .4 ? "BFGGC0" : game.FireTime > 0 ? "BFGGB0" : "BFGGA0",
            Weapon.Chainsaw => game.FireTime > 0 ? "SAWGB0" : "SAWGA0",
            _ => game.FireTime > .15 ? "PISGB0" : "PISGA0"
        };
        DrawWeaponPatch(name);
        if (game.SelectedWeapon == Weapon.Chaingun && game.ChaingunFlashTicks > 0)
            DrawWeaponPatch(game.ChaingunFlashFrame == 0 ? "CHGFA0" : "CHGFB0");
        if (game.SelectedWeapon == Weapon.RocketLauncher && game.RocketFireTicks > 4)
            DrawWeaponPatch($"MISF{(char)('A' + (20 - game.RocketFireTicks) / 4)}0");
        if (game.SelectedWeapon == Weapon.PlasmaRifle && game.PlasmaFlashTicks > 0)
            DrawWeaponPatch(game.PlasmaFlashTicks > 2 ? "PLSFA0" : "PLSFB0");
        if (game.SelectedWeapon == Weapon.BFG9000 && game.BFGFlashTicks > 0)
            DrawWeaponPatch(game.BFGFlashTicks > 5 ? "BFGFA0" : "BFGFB0");
    }

    void DrawWeaponPatch(string name)
    {
        Picture? p = game.Assets.Patch(name);
        if (p == null)
        {
            if (missingSprites.Add(name)) Console.Error.WriteLine($"Missing weapon patch {name}; weapon overlay cannot be drawn.");
            return;
        }
        double scale = Width / 320.0;
        int x = (int)(-p.Left * scale + Math.Sin(game.WalkTime * 5) * game.Bob * 3);
        int y = (int)(-p.Top * scale);
        Blit(p, x, y, scale);
    }

    void Blit(Picture p, int left, int top, double scale)
    {
        for (int y = Math.Max(0, top); y < Math.Min(viewHeight, top + p.Height * scale); y++)
            for (int x = Math.Max(0, left); x < Math.Min(Width, left + p.Width * scale); x++)
            {
                int color = p.Pixels[(int)((y - top) / scale) * p.Width + (int)((x - left) / scale)];
                if (color >= 0) { Pixels[y * Width + x] = color; VisibleWeaponPixels++; }
            }
    }

    void DrawMap()
    {
        for (int i = 0; i < Width * viewHeight; i++) Pixels[i] = 0x08090b;
        Vec ScreenPoint(Vec p) => new(Width / 2.0 + (p.X - position.X) * .3, viewHeight / 2.0 - (p.Y - position.Y) * .3);
        bool revealAll = game.HasComputerMap;
        foreach (Line line in game.Map.Lines)
        {
            if (!revealAll)
            {
                int front = game.Map.Sides[line.Right].Sector;
                int back = line.Left == 65535 ? -1 : game.Map.Sides[line.Left].Sector;
                if (!game.VisitedSectors.Contains(front) && !game.VisitedSectors.Contains(back)) continue;
            }
            Vec a = ScreenPoint(game.Map.Vertices[line.A]), b = ScreenPoint(game.Map.Vertices[line.B]);
            DrawLine(a, b, line.Left == 65535 ? 0xd06040 : line.Special != 0 ? 0xffff40 : 0x505050);
        }
        Vec center = ScreenPoint(position);
        DrawLine(center - forward * 3, center + new Vec(forward.X, -forward.Y) * 10, 0xffffff);
        foreach (Actor actor in game.Actors.Where(a => a.Enemy && a.Health > 0))
        {
            Vec p = ScreenPoint(actor.Position); DrawLine(p - new Vec(2, 0), p + new Vec(2, 0), 0xff4040);
        }
    }

    void DrawLine(Vec a, Vec b, int color)
    {
        int steps = (int)Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        if (steps > 100000) return;
        for (int i = 0; i <= steps; i++)
        {
            Vec p = a + (b - a) * (i / (double)Math.Max(1, steps));
            int x = (int)p.X, y = (int)p.Y;
            if (x >= 0 && x < Width && y >= 0 && y < viewHeight) Pixels[y * Width + x] = color;
        }
    }

    void DrawHud()
    {
        for (int y = viewHeight; y < Height; y++)
            for (int x = 0; x < Width; x++) Pixels[y * Width + x] = y == viewHeight ? 0x907050 : 0x28252a;
        int scale = Math.Max(1, Width / 320);
        Text($"HEALTH {game.Health}  ARMOR {game.Armor}  AMMO {game.Ammo}  " +
            game.SelectedWeapon.ToString().ToUpperInvariant(),
            8, viewHeight + 5, scale, 0xffdb8a);
        Text($"{game.Map.Name}  KILLS {game.Kills}/{game.TotalEnemies}  KEYS {game.Keys}  SECRETS {game.Secrets}",
            8, viewHeight + 5 + 9 * scale, scale, 0xc0c0c0);
        string powerups = string.Join("  ", new[]
        {
            game.HasBerserk ? "BERSERK" : null,
            game.InvulnerabilityTime > 0 ? $"INVULN {(int)game.InvulnerabilityTime}" : null,
            game.InvisibilityTime > 0 ? $"INVIS {(int)game.InvisibilityTime}" : null,
            game.RadiationSuitTime > 0 ? $"RADSUIT {(int)game.RadiationSuitTime}" : null,
            game.LightAmpTime > 0 ? $"VISOR {(int)game.LightAmpTime}" : null,
            game.HasComputerMap ? "COMPMAP" : null,
        }.Where(s => s != null));
        if (powerups.Length > 0) Text(powerups, 8, viewHeight + 5 + 18 * scale, scale, 0x90e0ff);
        if (game.Message.Length > 0) Text(game.Message, 8, 8, scale, 0xffee90);
        if (game.Finished)
            for (int i = 0; i < game.IntermissionStats.Length; i++)
                Text(game.IntermissionStats[i], 30, viewHeight / 2 - 9 * scale + i * 9 * scale, scale, 0xffffff);
        if (!game.Automap && game.Health > 0)
        {
            int x = Width / 2, y = viewHeight / 2;
            Pixels[y * Width + x - 2] = Pixels[y * Width + x + 2] = 0xcccccc;
            Pixels[(y - 2) * Width + x] = Pixels[(y + 2) * Width + x] = 0xcccccc;
        }
    }

    static int Blend(int a, int b) => ((a & 0xfefefe) >> 1) + ((a & 0xfcfcfc) >> 2) + ((b & 0xfcfcfc) >> 2);
    public void Text(string text, int x, int y, int scale, int color)
    {
        foreach (char c in text)
        {
            string glyph = Font.Glyph(c);
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                    if (glyph[row * 5 + col] == '#')
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int px = x + col * scale + dx, py = y + row * scale + dy;
                                if (px >= 0 && px < Width && py >= 0 && py < Height) Pixels[py * Width + px] = color;
                            }
            x += 6 * scale;
        }
    }

    public void SavePpm(string path)
    {
        using FileStream file = File.Create(path);
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n");
        file.Write(header);
        foreach (int pixel in Pixels) { file.WriteByte((byte)(pixel >> 16)); file.WriteByte((byte)(pixel >> 8)); file.WriteByte((byte)pixel); }
    }
}
