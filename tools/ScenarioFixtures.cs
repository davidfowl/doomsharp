using System.Text;
using System.Buffers.Binary;

namespace DoomSharp.Tools;

public static class ScenarioFixtures
{
    public sealed record Lump(string Name, byte[] Data);
    public static IEnumerable<(string Name, byte[] Data)> Generate(byte[] baseline)
    {
        var assets = Read(baseline).TakeWhile(l => l.Name != "E1M1").ToList();
        int spriteEnd = assets.FindIndex(l => l.Name == "S_END");
        List<Lump> patches = [];
        foreach (string prefix in new[] { "MGUN", "LAUN", "ROCK", "BROK", "CLIP", "AMMO", "SHOT",
            "BKEY", "RKEY", "YKEY", "BSKU", "RSKU", "YSKU", "BAR1", "ARM1" })
            patches.Add(new(prefix + "A0", Patch(32, 32, 16, 32, 186)));
        foreach (string prefix in new[] { "POSS", "CPOS", "TROO", "HEAD", "BOSS", "BOS2", "SARG" })
            foreach (char frame in "ABCDEFGHIJKLMNOPQRSTU")
                patches.Add(new($"{prefix}{frame}0", Patch(32, 56, 16, 56, 80)));
        foreach (string prefix in new[] { "PISG", "SHTG", "CHGG", "MISG" })
            foreach (char frame in "AB")
                patches.Add(new($"{prefix}{frame}0", Patch(64, 44, -128, -131, (byte)(frame == 'A' ? 151 : 173))));
        foreach (char frame in "ABCD")
        {
            patches.Add(new($"MISF{frame}0", Patch(16, 16, -152, -116, 237)));
            patches.Add(new($"MISL{frame}0", Patch(32, 32, 16, 16, 219)));
        }
        foreach (char frame in "AB") patches.Add(new($"CHGF{frame}0", Patch(16, 16, -152, -116, 237)));
        foreach (int rotation in Enumerable.Range(1, 8))
            patches.Add(new($"MISLA{rotation}", Patch(16, 8, 8, 4, 214)));
        assets.InsertRange(spriteEnd, patches);
        byte[] tone = Enumerable.Range(0, 4032).Select(i => (byte)(i < 16 || i >= 4016 ? 128 : i % 50 < 25 ? 168 : 88)).ToArray();
        foreach (string effect in new[] { "PISTOL", "RLAUNC", "BAREXP", "ITEMUP", "SHOTGN", "PLPAIN", "PLDETH", "PODTH1", "STNMOV", "PSTOP" })
            assets.Add(new("DS" + effect, Pack(w => { w.Write((ushort)3); w.Write((ushort)11025); w.Write(tone.Length); w.Write(tone); })));

        foreach (int weapon in new[] { 2002, 2003 })
        {
            string prefix = weapon == 2002 ? "chaingun" : "rocket";
            foreach (string mode in new[] { "pickup", "capacity", "combat", "empty", "carry", "death", "missing", "audio-missing", "unowned",
                "miss", "barrel", "splash", "occlusion", "thin-wall", "drop" })
            {
                List<(int X, int Y, int Type)> things = [(0, 0, 1)];
                if (mode != "unowned") things.Add((0, 0, weapon));
                if (mode == "capacity")
                    things.AddRange(Enumerable.Repeat((0, 0, weapon == 2002 ? 2048 : 2046), 10));
                if (mode == "combat") things.Add((600, 0, 3004));
                if (mode == "miss") things.Add((600, 180, 3004));
                if (mode == "drop") { things.RemoveAll(t => t.Type == weapon); things.Add((600, 0, 65)); }
                if (mode == "barrel") { things.Add((600, 0, 2035)); things.Add((600, 80, 3004)); }
                if (mode is "splash" or "occlusion")
                {
                    things.Add((600, 0, 3004)); things.Add((600, -80, 3004)); things.Add((600, 80, 3004));
                }
                if (mode == "thin-wall") things.Add((256, 0, 3004));
                var selected = assets.Where(l => mode != "missing" || !(l.Name.StartsWith(weapon == 2002 ? "CHG" : "MIS", StringComparison.Ordinal))).ToList();
                if (mode == "audio-missing")
                    selected.RemoveAll(l => l.Name == (weapon == 2002 ? "DSPISTOL" : "DSRLAUNC"));
                var room = Room("E1M1", things, mode == "death" ? 5 : 0, mode == "carry");
                if (mode is "occlusion" or "thin-wall")
                    AddWall(room, mode == "occlusion" ? (680, 40, 540, 40) : (85, 256, 85, -256));
                if (mode is "combat" or "miss" or "barrel" or "splash" or "occlusion" or "drop") AddBarrier(room);
                selected.AddRange(room);
                if (mode == "carry") selected.AddRange(Room("E1M2", [(-64, 0, 1)]));
                yield return ($"{prefix}-{mode}", Write(selected));
            }
        }
        int[] locks = [99, 133, 134, 135, 136, 137];
        foreach (int special in locks)
            foreach (int key in new[] { 0, 5, 40, 13, 38, 6, 39 })
                yield return ($"door-{special}-{key}", Write(assets.Concat(Door(special, key))));
        // Noise propagation: a three-sector corridor with a thin interior wall blocking the player's
        // direct line of sight to a monster in the far sector. The middle sector is either "open" (a
        // normal passable gap, modeling an already-open door) or "closed" (floor == ceiling, modeling a
        // shut door), which is exactly the condition Game.NoiseAlert's sector-flood checks when deciding
        // whether a sound can propagate past it.
        foreach (bool open in new[] { true, false })
        {
            List<Lump> corridor = Corridor("E1M1", [-192, 64, 320, 576], [0, 0, 0], [128, open ? 128 : 0, 128],
                [0, 0, 0], [(-96, 0, 1), (448, 0, 3004)]);
            AddCorridorWall(corridor, 1, (192, 256, 192, -256));
            yield return ($"noise-propagation-{(open ? "wakes-through-open-door" : "blocked-by-closed-door")}",
                Write(assets.Concat(corridor)));
        }
        foreach (string mode in new[] { "last-baron", "two-barons", "knight", "mixed", "wrong-map", "no-tag" })
            yield return ($"boss-{mode}", Write(assets.Concat(Boss(mode))));
        foreach (int special in new[] { 7, 8, 100, 127 })
            foreach (string mode in new[] { "chain", "texture-stop", "reversed", "no-tag", "zero-tag",
                "multiple", "busy-seed", "busy-next", "blocked", "raised-start", "back-side" })
                yield return ($"stairs-{special}-{mode}", Write(assets.Concat(Stairs(special, mode))));
        foreach (int special in new[] { 6, 25, 77, 141 })
            foreach (string mode in new[] { "cycle", "no-tag", "zero-tag", "multiple", "crush", "armor",
                "overlap", "actor", "barrel", "drop", "stop57", "stop74", "door-busy", "floor-busy",
                "crusher-door", "crusher-floor", "double-contact", "raised-floor", "paused-player", "trap", "paused-actor" })
                yield return ($"crusher-{special}-{mode}", Write(assets.Concat(Crusher(special, mode))));
        var switchAssets = assets.ToList();
        byte[] palette = (byte[])switchAssets.Single(l => l.Name == "PLAYPAL").Data.Clone();
        new byte[] { 220, 40, 32, 32, 220, 48 }.CopyTo(palette, 230 * 3);
        switchAssets[switchAssets.FindIndex(l => l.Name == "PLAYPAL")] = new("PLAYPAL", palette);
        switchAssets[switchAssets.FindIndex(l => l.Name == "PNAMES")] = new("PNAMES",
            Pack(w => { w.Write(4); foreach (string name in new[] { "QLSOLID", "QLMASK", "SWOFF", "SWON" }) Name(w, name); }));
        switchAssets.Add(new("SWOFF", Patch(64, 128, 0, 128, 230)));
        switchAssets.Add(new("SWON", Patch(64, 128, 0, 128, 231)));
        foreach (string effect in new[] { "SWTCHN", "SWTCHX", "DOROPN", "BDOPN", "NOWAY" })
            switchAssets.Add(new("DS" + effect, Pack(w => { w.Write((ushort)3); w.Write((ushort)11025); w.Write(tone.Length); w.Write(tone); })));
        List<(int Special, string Mode)> switches = [];
        foreach (int special in new[] { 7, 127, 11, 51, 18, 21, 23, 60, 62, 69, 122, 123,
            29, 63, 99, 103, 111, 112, 113, 133, 134, 135, 136, 137 })
            foreach (string mode in new[] { "middle", "no-tag", "back" }) switches.Add((special, mode));
        foreach (int special in new[] { 29, 99 })
            foreach (string mode in new[] { "upper", "lower", "reverse", "priority", "same-pair", "pair-state",
                "missing", "fallback", "custom", "busy", "tier-two", "tier-three" }) switches.Add((special, mode));
        foreach (int special in new[] { 7, 18, 23, 62 }) switches.Add((special, "busy"));
        foreach (int special in new[] { 1, 31, 6, 8 }) switches.Add((special, "middle"));
        foreach (int special in locks) switches.Add((special, "no-key"));
        switches.Add((63, "independent"));
        switches.Add((63, "carry"));
        foreach (var scenario in switches)
            yield return ($"switch-{scenario.Special}-{scenario.Mode}", Write(switchAssets
                .Append(new("TEXTURE2", SwitchTextures(scenario.Mode is "missing" or "fallback")))
                .Concat(Switch(scenario.Special, scenario.Mode))));
        foreach (string mode in new[] { "full", "partial", "empty" })
        {
            List<(int X, int Y, int Type)> things = [(0, 0, 1)];
            if (mode != "empty") things.Add((100, 0, 5));
            if (mode == "full") things.Add((150, 0, 2015));
            if (mode == "partial") things.Add((150, 200, 2015));
            int special = mode == "full" ? 9 : 0;
            yield return ($"intermission-{mode}", Write(assets.Concat(Room("E1M1", things, special, true))));
        }
        foreach (int type in new[] { 2022, 2023, 2024, 2025, 2026, 2045 })
            yield return ($"powerup-{type}", Write(assets.Concat(Room("E1M1", [(0, 0, 1), (0, 0, type)]))));
        yield return ("powerup-floor-control", Write(assets.Concat(Room("E1M1", [(0, 0, 1)], 5))));
        yield return ("powerup-floor-invulnerability", Write(assets.Concat(Room("E1M1", [(0, 0, 1), (0, 0, 2022)], 5))));
        yield return ("powerup-floor-radsuit", Write(assets.Concat(Room("E1M1", [(0, 0, 1), (0, 0, 2025)], 5))));
        List<Lump> retarget = Room("E1M1", [(0, 0, 1), (600, 0, 3001), (300, 0, 3002)]);
        yield return ("infight-retarget", Write(assets.Concat(retarget)));
        List<Lump> playerSafe = Room("E1M1", [(0, 0, 1), (120, 0, 3002)]);
        yield return ("infight-player-safe", Write(assets.Concat(playerSafe)));
        yield return ("infight-telefrag", Write(assets.Concat(Teleport())));
        List<Lump> chaseMemory = Corridor("E1M1", [-200, 6000], [0], [128], [0], [(0, 0, 3002), (300, 0, 1)]);
        yield return ("chase-memory", Write(assets.Concat(chaseMemory)));
    }

    static byte[] SwitchTextures(bool missing) => Pack(w =>
    {
        string[] names = ["SW1BRCOM", "SW2BRCOM", "SW1BRN1", "SW2BRN1", "SW1EXIT", "SW2EXIT",
            "SW1WOOD", "SW2WOOD", "SW1SKULL", "SW2SKULL", "SW1TEST", "SW2TEST"];
        if (missing) names = names.Where(n => n != "SW2BRCOM").ToArray();
        w.Write(names.Length);
        for (int i = 0; i < names.Length; i++) w.Write(4 + names.Length * 4 + i * 32);
        foreach (string name in names)
        {
            Name(w, name); w.Write(0); w.Write(Shorts(64, 128)); w.Write(0); w.Write((short)1);
            w.Write(Shorts(0, 0, name.StartsWith("SW1", StringComparison.Ordinal) ? 2 : 3, 1, 0));
        }
    });

    static List<Lump> Switch(int special, string mode)
    {
        bool door = special is 1 or 31 or 29 or 63 or 99 or 103 or 111 or 112 or 113 or 133 or 134 or 135 or 136 or 137;
        int floor = special is 21 or 62 or 122 or 123 ? 64 : special is 23 or 60 ? 24 : 0;
        int[] floors = [0, 0, mode == "lower" ? 32 : 0, floor, special is 18 or 69 ? 24 : 0];
        int[] ceilings = [128, 128, mode == "upper" ? 64 : 128, door ? 0 : 128, 128];
        if (special is 1 or 31) { ceilings[2] = 0; ceilings[3] = 128; }
        int[] tags = [0, 0, 0, mode == "no-tag" ? 8 : 7, mode == "independent" ? 9 : 0];
        if (mode == "independent") ceilings[4] = 0;
        int x = mode == "busy" ? -96 : mode == "upper" ? -64 : mode == "back" ? 32 : -32;
        List<(int X, int Y, int Type)> things = [(x, 0, 1)];
        int key = special is 99 or 133 ? 5 : special is 134 or 135 ? 13 : special is 136 or 137 ? 6 : 0;
        if (key != 0 && mode != "no-key") things.Add((x, 0, key));
        var map = Corridor("E1M1", [-192, -64, 0, 64, 128, 320], floors, ceilings, tags, things);
        byte[] lines = map.Single(l => l.Name == "LINEDEFS").Data;
        void Action(int line, int action, int tag)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(line * 14 + 6), (ushort)action);
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(line * 14 + 8), (ushort)tag);
        }
        Action(7, special, 7);
        if (mode == "busy") Action(6, door ? 2 : 58, 7);
        if (mode == "independent") Action(8, 63, 9);
        if (mode == "carry") Action(8, 11, 0);
        byte[] sides = map.Single(l => l.Name == "SIDEDEFS").Data;
        int right = BinaryPrimitives.ReadUInt16LittleEndian(lines.AsSpan(7 * 14 + 10));
        void Texture(int side, int slot, string texture) =>
            Encoding.ASCII.GetBytes(texture.PadRight(8, '\0')).CopyTo(sides, side * 30 + 4 + slot * 8);
        string name = mode == "reverse" ? "SW2BRCOM" : mode == "custom" ? "SW1TEST" :
            mode == "tier-two" ? "SW1WOOD" : mode == "tier-three" ? "SW1SKULL" : "SW1BRCOM";
        int slot = mode == "upper" || special is 1 or 31 ? 0 : mode == "lower" ? 1 : 2;
        Texture(right, slot, name);
        if (mode is "priority" or "same-pair" or "pair-state" or "fallback")
        {
            Texture(right, 0, mode is "priority" or "fallback" ? "SW1EXIT" : mode == "pair-state" ? "SW2BRCOM" : "SW1BRCOM");
            Texture(right, 2, mode == "priority" ? "SW1BRN1" : "SW1BRCOM");
            Texture(right, 1, mode == "priority" ? "SW1BRCOM" : mode == "fallback" ? "SW1EXIT" : "-");
            if (mode == "same-pair") Texture(right, 1, "SW1BRCOM");
        }
        if (mode == "independent")
        {
            int second = BinaryPrimitives.ReadUInt16LittleEndian(lines.AsSpan(8 * 14 + 10));
            Texture(second, 2, "SW1BRN1");
        }
        if (mode == "back")
            BinaryPrimitives.WriteUInt16LittleEndian(map.Single(l => l.Name == "THINGS").Data.AsSpan(4), 180);
        if (mode == "carry") map.AddRange(Room("E1M2", [(-64, 0, 1)]));
        return map;
    }

    static List<Lump> Teleport()
    {
        // Two sectors joined by a line tagged/specialed as a teleporter (39); the player starts in the first
        // and walks into the second, triggering a teleport to a destination marker (type 14) in the second
        // sector where a monster already stands, so the only way through is to telefrag it.
        List<Lump> map = Corridor("E1M1", [-128, 0, 128], [0, 0], [128, 128], [0, 7],
            [(-64, 0, 1), (64, 0, 14), (64, 0, 3004)]);
        byte[] lines = map.Single(l => l.Name == "LINEDEFS").Data;
        const int portal = 3;
        BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(portal * 14 + 6), 39);
        BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(portal * 14 + 8), 7);
        return map;
    }

    static void AddBarrier(List<Lump> map)
    {
        int vertex = map.Single(l => l.Name == "VERTEXES").Data.Length / 4;
        int side = map.Single(l => l.Name == "SIDEDEFS").Data.Length / 30;
        int line = map.Single(l => l.Name == "LINEDEFS").Data.Length / 14;
        void Append(string name, byte[] bytes)
        {
            int i = map.FindIndex(l => l.Name == name);
            map[i] = new(name, map[i].Data.Concat(bytes).ToArray());
        }
        Append("VERTEXES", Shorts(568, 256, 568, -256));
        Append("SIDEDEFS", Pack(w => { Side(w, 0, true); Side(w, 0, true); }));
        Append("LINEDEFS", Shorts(vertex, vertex + 1, 6, 0, 0, side, side + 1));
        int blockmap = map.FindIndex(l => l.Name == "BLOCKMAP");
        byte[] block = map[blockmap].Data;
        map[blockmap] = new("BLOCKMAP", block.AsSpan(0, block.Length - 2).ToArray().Concat(Shorts(line, 65535)).ToArray());
    }

    // Like AddWall, but for multi-sector Corridor() maps: computes indices dynamically (Corridor already
    // has several vertices/sides/lines) instead of assuming the single-sector Room() layout, and assigns
    // the new one-sided solid line to the given sector so it blocks sight/movement through that sector
    // without altering sector connectivity (SectorLines) used by Game.NoiseAlert's flood-fill.
    static void AddCorridorWall(List<Lump> map, int sector, (int X1, int Y1, int X2, int Y2) wall)
    {
        int vertex = map.Single(l => l.Name == "VERTEXES").Data.Length / 4;
        int side = map.Single(l => l.Name == "SIDEDEFS").Data.Length / 30;
        int line = map.Single(l => l.Name == "LINEDEFS").Data.Length / 14;
        void Append(string name, byte[] bytes)
        {
            int i = map.FindIndex(l => l.Name == name);
            map[i] = new(name, map[i].Data.Concat(bytes).ToArray());
        }
        Append("VERTEXES", Shorts(wall.X1, wall.Y1, wall.X2, wall.Y2));
        Append("SIDEDEFS", Pack(w => Side(w, sector, false)));
        Append("LINEDEFS", Shorts(vertex, vertex + 1, 1, 0, 0, side, 65535));
        int blockmap = map.FindIndex(l => l.Name == "BLOCKMAP");
        byte[] block = map[blockmap].Data;
        map[blockmap] = new("BLOCKMAP", block.AsSpan(0, block.Length - 2).ToArray().Concat(Shorts(line, 65535)).ToArray());
    }

    static void AddWall(List<Lump> map, (int X1, int Y1, int X2, int Y2) wall)
    {
        void Append(string name, byte[] bytes)
        {
            int i = map.FindIndex(l => l.Name == name);
            map[i] = new(name, map[i].Data.Concat(bytes).ToArray());
        }
        Append("VERTEXES", Shorts(wall.X1, wall.Y1, wall.X2, wall.Y2));
        Append("SIDEDEFS", Pack(w => Side(w, 0, false)));
        Append("LINEDEFS", Shorts(4, 5, 1, 0, 0, 4, 65535));
        Append("SEGS", Shorts(4, 5, 0, 4, 0, 0));
        int subsector = map.FindIndex(l => l.Name == "SSECTORS");
        map[subsector] = new("SSECTORS", Shorts(5, 0));
        int blockmap = map.FindIndex(l => l.Name == "BLOCKMAP");
        byte[] block = map[blockmap].Data;
        map[blockmap] = new("BLOCKMAP", block.AsSpan(0, block.Length - 2).ToArray().Concat(Shorts(4, 65535)).ToArray());
    }

    public static List<Lump> Room(string label, IEnumerable<(int X, int Y, int Type)> things, int special = 0, bool exit = false)
    {
        int end = exit ? 256 : 1536;
        int width = (end + 128) / 128, cells = width * 4;
        return
        [
            new(label, []),
            new("THINGS", Shorts(things.SelectMany(t => new[] { t.X, t.Y, 0, t.Type, 7 }).ToArray())),
            new("LINEDEFS", Shorts(Enumerable.Range(0, 4).SelectMany(i => new[] { i, (i + 1) % 4, 1, exit && i == 2 ? 11 : 0, 0, i, 65535 }).ToArray())),
            new("SIDEDEFS", Pack(w => { for (int i = 0; i < 4; i++) Side(w, 0, false); })),
            new("VERTEXES", Shorts(-128, -256, -128, 256, end, 256, end, -256)),
            new("SEGS", Shorts(Enumerable.Range(0, 4).SelectMany(i => new[] { i, (i + 1) % 4, 0, i, 0, 0 }).ToArray())),
            new("SSECTORS", Shorts(4, 0)), new("NODES", []),
            new("SECTORS", Pack(w => Sector(w, 0, 128, special, 0))),
            new("REJECT", [0]),
            new("BLOCKMAP", Shorts(new[] { -128, -256, width, 4 }.Concat(Enumerable.Repeat(4 + cells, cells)).Concat(new[] { 0, 0, 1, 2, 3, 65535 }).ToArray()))
        ];
    }

    static List<Lump> Door(int special, int key)
    {
        // Four sectors: the switch is on the first portal; its tagged target is remote.
        var map = Corridor("E1M1", [-192, 0, 64, 128, 256],
            [0, 0, 0, 0], [128, 128, 0, 128], [0, 0, 7, 0],
            key == 0 ? [(-32, 0, 1)] : [(-32, 0, 1), (-32, 0, key)]);
        byte[] lines = map.Single(l => l.Name == "LINEDEFS").Data;
        int portal = 1 + 4;
        BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(portal * 14 + 6), (ushort)special);
        BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(portal * 14 + 8), 7);
        return map;
    }

    static List<Lump> Boss(string mode)
    {
        List<(int X, int Y, int Type)> things = [(0, 0, 1), (0, 0, 2003)];
        things.AddRange(Enumerable.Repeat((0, 0, 2046), 10));
        things.Add((600, 0, mode == "knight" ? 69 : 3003));
        if (mode is "two-barons" or "mixed") things.Add((600, 200, mode == "mixed" ? 69 : 3003));
        return Corridor(mode == "wrong-map" ? "E1M1" : "E1M8", [-128, 768, 832, 960],
            [0, 64, -32], [256, 256, 256], [0, mode == "no-tag" ? 667 : 666, 0], things);
    }

    static List<Lump> Stairs(int special, string mode)
    {
        int end = special is 100 or 127 ? 64 : 48;
        int[] floors = mode == "raised-start" ? [0, 0, 16, 0, 0, end] : [0, 0, 0, 0, 0, end];
        int[] ceilings = mode == "blocked" ? [256, 256, 60, 256, 256, 256] : [256, 256, 256, 256, 256, 256];
        int tag = mode == "zero-tag" ? 0 : 7;
        int[] tags = [0, 0, mode == "no-tag" ? 8 : tag, 0, mode == "multiple" ? tag : 0, 0];
        if (mode is "busy-seed" or "busy-next") tags[mode == "busy-seed" ? 2 : 3] = 9;
        var map = Corridor("E1M1", [-192, -64, 0, 64, 128, 192, 320], floors, ceilings, tags,
            [(mode is "busy-seed" or "busy-next" ? -96 : mode == "back-side" ? 32 : -32, 0, 1)]);
        byte[] sectors = map.Single(l => l.Name == "SECTORS").Data;
        NameAt(5, "QLCEIL");
        if (mode == "texture-stop") NameAt(3, "QLCEIL");
        if (mode == "multiple") { NameAt(4, "QLCEIL"); NameAt(5, "QLFLOOR"); }
        void NameAt(int sector, string name) =>
            Encoding.ASCII.GetBytes(name.PadRight(8, '\0')).CopyTo(sectors, sector * 26 + 4);
        byte[] lines = map.Single(l => l.Name == "LINEDEFS").Data;
        BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(8 * 14 + 6), (ushort)special);
        BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(8 * 14 + 8), (ushort)tag);
        if (mode is "busy-seed" or "busy-next")
        {
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(7 * 14 + 6), 58);
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(7 * 14 + 8), 9);
            // Keep the stair seed tagged too when the earlier crossing starts its mover.
            if (mode == "busy-seed")
                BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(8 * 14 + 8), 9);
        }
        if (mode == "reversed")
        {
            ushort right = BinaryPrimitives.ReadUInt16LittleEndian(lines.AsSpan(9 * 14 + 10));
            ushort left = BinaryPrimitives.ReadUInt16LittleEndian(lines.AsSpan(9 * 14 + 12));
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(9 * 14 + 10), left);
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(9 * 14 + 12), right);
            byte[] segs = map.Single(l => l.Name == "SEGS").Data;
            for (int p = 0; p < segs.Length; p += 12)
                if (BinaryPrimitives.ReadUInt16LittleEndian(segs.AsSpan(p + 6)) == 9)
                    segs[p + 8] ^= 1;
        }
        if (mode == "back-side")
            BinaryPrimitives.WriteUInt16LittleEndian(map.Single(l => l.Name == "THINGS").Data.AsSpan(4), 180);
        return map;
    }

    static List<Lump> Crusher(int special, string mode)
    {
        int target = mode is "crush" or "armor" or "overlap" or "double-contact" ? 1 :
            mode is "paused-player" or "trap" ? 2 : 4;
        int[] floors = [0, 0, 0, 0, mode == "floor-busy" ? 64 : mode == "raised-floor" ? 32 : 0, 0];
        int[] ceilings = [256, 256, 256, 256, 256, 256];
        ceilings[target] = 128;
        if (mode == "raised-floor") ceilings[target] = 160;
        int[] tags = [0, 0, 0, 0, 0, 0];
        tags[target] = mode == "no-tag" ? 8 : mode == "zero-tag" ? 0 : 7;
        if (mode == "multiple") { tags[5] = 7; ceilings[5] = 96; }
        if (mode == "double-contact") { tags[2] = 7; ceilings[2] = 128; }
        List<(int X, int Y, int Type)> things = [(-32, 0, 1)];
        if (mode == "armor") things.Add((-32, 0, 2018));
        if (mode is "actor" or "barrel" or "drop" or "paused-actor")
            things.Add((320, 0, mode is "actor" or "paused-actor" ? 3002 : mode == "barrel" ? 2035 : 3004));
        var map = Corridor("E1M1", [-192, 0, 64, 128, 256, 384, 512], floors, ceilings, tags, things);
        byte[] lines = map.Single(l => l.Name == "LINEDEFS").Data;
        void Action(int line, int action)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(line * 14 + 6), (ushort)action);
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(line * 14 + 8), (ushort)(mode == "zero-tag" ? 0 : 7));
        }
        Action(7, special);
        if (mode is "stop57" or "stop74")
        {
            Action(8, mode == "stop57" ? 57 : 74);
            Action(9, 77);
        }
        if (mode is "paused-player" or "paused-actor") { Action(8, 74); Action(9, 77); }
        if (mode is "door-busy" or "floor-busy")
        {
            Action(7, mode == "door-busy" ? 2 : 88);
            Action(8, special);
        }
        if (mode is "crusher-door" or "crusher-floor") Action(8, mode == "crusher-door" ? 2 : 58);
        if (mode is "actor" or "drop" or "paused-actor")
            BinaryPrimitives.WriteUInt16LittleEndian(lines.AsSpan(10 * 14 + 4), 6);
        return map;
    }

    static List<Lump> Corridor(string label, int[] bounds, int[] floors, int[] ceilings, int[] tags,
        IEnumerable<(int X, int Y, int Type)> things)
    {
        int sectors = floors.Length;
        List<Lump> result = [];
        List<byte[]> sides = [], lines = [], segs = [];
        int Line(int a, int b, int right, int? left = null)
        {
            int first = sides.Count;
            sides.Add(Pack(w => Side(w, right, left != null)));
            if (left != null) sides.Add(Pack(w => Side(w, left.Value, true)));
            lines.Add(Shorts(a, b, left == null ? 1 : 4, 0, 0, first, left == null ? 65535 : first + 1));
            return lines.Count - 1;
        }
        int west = Line(0, 1, 0);
        int[] tops = Enumerable.Range(0, sectors).Select(i => Line(i * 2 + 1, i * 2 + 3, i)).ToArray();
        int[] portals = Enumerable.Range(1, sectors - 1).Select(i => Line(i * 2 + 1, i * 2, i - 1, i)).ToArray();
        int east = Line(sectors * 2 + 1, sectors * 2, sectors - 1);
        int[] bottoms = Enumerable.Range(0, sectors).Select(i => Line(i * 2 + 2, i * 2, i)).ToArray();
        for (int i = 0; i < sectors; i++)
        {
            segs.Add(Shorts(i * 2, i * 2 + 1, 0, i == 0 ? west : portals[i - 1], i == 0 ? 0 : 1, 0));
            segs.Add(Shorts(i * 2 + 1, i * 2 + 3, 0, tops[i], 0, 0));
            segs.Add(Shorts(i * 2 + 3, i * 2 + 2, 0, i == sectors - 1 ? east : portals[i], 0, 0));
            segs.Add(Shorts(i * 2 + 2, i * 2, 0, bottoms[i], 0, 0));
        }
        int width = (bounds[^1] - bounds[0] + 127) / 128, cells = width * 4;
        result.AddRange([
            new(label, []), new("THINGS", Shorts(things.SelectMany(t => new[] { t.X, t.Y, 0, t.Type, 7 }).ToArray())),
            new("LINEDEFS", lines.SelectMany(b => b).ToArray()), new("SIDEDEFS", sides.SelectMany(b => b).ToArray()),
            new("VERTEXES", Shorts(bounds.SelectMany(x => new[] { x, -256, x, 256 }).ToArray())),
            new("SEGS", segs.SelectMany(b => b).ToArray()),
            new("SSECTORS", Shorts(Enumerable.Range(0, sectors).SelectMany(i => new[] { 4, i * 4 }).ToArray())),
            new("NODES", Shorts(Enumerable.Range(0, sectors - 1).SelectMany(i => new[] {
                bounds[i + 1], 256, 0, -512, 256, -256, bounds[0], bounds[i + 1],
                256, -256, bounds[i + 1], bounds[i + 2], i == 0 ? 0x8000 : i - 1, 0x8000 | (i + 1) }).ToArray())),
            new("SECTORS", Pack(w => { for (int i = 0; i < sectors; i++) Sector(w, floors[i], ceilings[i], 0, tags[i]); })),
            new("REJECT", new byte[(sectors * sectors + 7) / 8]),
            new("BLOCKMAP", Shorts(new[] { bounds[0], -256, width, 4 }.Concat(Enumerable.Repeat(4 + cells, cells))
                .Concat(new[] { 0 }).Concat(Enumerable.Range(0, lines.Count)).Append(65535).ToArray()))
        ]);
        return result;
    }

    static void Side(BinaryWriter w, int sector, bool portal)
    {
        w.Write(Shorts(0, 0)); Name(w, portal ? "QLWALL" : "-"); Name(w, portal ? "QLWALL" : "-");
        Name(w, portal ? "-" : "QLWALL"); w.Write((ushort)sector);
    }
    static void Sector(BinaryWriter w, int floor, int ceiling, int special, int tag)
    {
        w.Write(Shorts(floor, ceiling)); Name(w, "QLFLOOR"); Name(w, "QLCEIL");
        w.Write(Shorts(255, special, tag));
    }
    static byte[] Patch(int width, int height, int left, int top, byte color) => Pack(w =>
    {
        w.Write(Shorts(width, height, left, top));
        for (int x = 0; x < width; x++) w.Write(8 + width * 4 + x * (height + 5));
        for (int x = 0; x < width; x++)
        {
            w.Write((byte)0); w.Write((byte)height); w.Write((byte)0);
            w.Write(Enumerable.Repeat(color, height).ToArray()); w.Write((byte)0); w.Write((byte)255);
        }
    });
    public static byte[] Write(IEnumerable<Lump> source) => Pack(w =>
    {
        Lump[] lumps = source.ToArray();
        w.Write(Encoding.ASCII.GetBytes("PWAD")); w.Write(lumps.Length); w.Write(0);
        List<int> positions = [];
        foreach (Lump lump in lumps) { positions.Add(checked((int)w.BaseStream.Position)); w.Write(lump.Data); }
        int directory = checked((int)w.BaseStream.Position);
        for (int i = 0; i < lumps.Length; i++)
        { w.Write(positions[i]); w.Write(lumps[i].Data.Length); Name(w, lumps[i].Name); }
        w.BaseStream.Position = 8; w.Write(directory);
    });
    static List<Lump> Read(byte[] bytes)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        int directory = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        List<Lump> lumps = [];
        for (int i = 0; i < count; i++)
        {
            int entry = directory + i * 16;
            int start = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry));
            int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 4));
            lumps.Add(new(Encoding.ASCII.GetString(bytes, entry + 8, 8).TrimEnd('\0'), bytes.AsSpan(start, length).ToArray()));
        }
        return lumps;
    }
    static byte[] Shorts(params int[] values) => Pack(w => { foreach (int value in values) w.Write(unchecked((ushort)value)); });
    static void Name(BinaryWriter w, string name)
    {
        if (name.Length > 8) throw new ArgumentException("Lump name exceeds eight characters.");
        w.Write(Encoding.ASCII.GetBytes(name.PadRight(8, '\0')));
    }
    static byte[] Pack(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer); writer.Flush(); return stream.ToArray();
    }
}
