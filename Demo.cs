using System.Text;

namespace DoomSharp;

// This tiny WAD and all its pixels/samples are generated here, independently of DOOM assets.
public static class Demo
{
    public static byte[] Create()
    {
        List<(string Name, byte[] Bytes)> lumps = [];
        void Add(string name, Action<BinaryWriter> write)
        {
            using MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, Encoding.ASCII, true)) write(writer);
            lumps.Add((name, stream.ToArray()));
        }
        void Raw(string name, byte[] data) => lumps.Add((name, data));
        Add("PLAYPAL", w =>
        {
            for (int i = 0; i < 256; i++) { w.Write((byte)i); w.Write((byte)(i * 3 / 4)); w.Write((byte)(255 - i / 2)); }
        });
        Raw("WALLPT", Patch(64, 64, 0, 0, (x, y) => (byte)(y % 16 == 0 || (x + (y / 16 % 2) * 16) % 32 == 0 ? 30 : 120 + y % 4 * 4)));
        Raw("SKYPT", Patch(128, 128, 0, 0, (x, y) => (byte)(180 + y / 8 + (x / 16 % 2) * 3)));
        Add("PNAMES", w => { w.Write(2); Name(w, "WALLPT"); Name(w, "SKYPT"); });
        Add("TEXTURE1", w =>
        {
            w.Write(2); w.Write(12); w.Write(44);
            Texture(w, "WALL", 64, 64, 0); Texture(w, "SKY1", 128, 128, 1);
        });
        Raw("FLOOR", Enumerable.Range(0, 4096).Select(i => (byte)(((i % 64 / 16 + i / 64 / 16) & 1) == 0 ? 65 : 95)).ToArray());
        Raw("CEIL", Enumerable.Range(0, 4096).Select(i => (byte)(i % 64 % 8 == 0 || i / 64 % 8 == 0 ? 140 : 150)).ToArray());
        byte[] Monster(char frame) => Patch(32, 56, 16, 56, (x, y) =>
            Math.Pow((x - 16) / 14.0, 2) + Math.Pow((y - 28) / 27.0, 2) > 1 ? null :
            (byte)(y is > 10 and < 17 && x is > 7 and < 24 ? 230 : 60 + (frame - 'A') * 10));
        foreach (char frame in "ABCDGHIJKL") Raw($"POS S{frame}0".Replace(" ", ""), Monster(frame));
        Raw("SHOTA0", Patch(40, 16, 20, 16, (x, y) => y is >= 6 and <= 10 ? (byte)220 : null));
        Raw("CLIPA0", Patch(12, 16, 6, 16, (x, y) => (byte)(x == 0 || y == 0 ? 200 : 100)));
        Raw("BKEYA0", Patch(12, 16, 6, 16, (x, y) => (byte)240));
        Raw("BAR1A0", Patch(24, 40, 12, 40, (x, y) => (byte)(y % 10 == 0 ? 40 : 180)));
        Raw("PISGA0", Patch(60, 50, 30, 50, (x, y) => x >= 20 && x <= 40 ? (byte)(y < 30 ? 210 : 100) : null));
        Raw("PISGB0", Patch(60, 50, 30, 50, (x, y) => x >= 20 && x <= 40 ? (byte)(y < 20 ? 250 : 130) : null));
        Raw("SHTGA0", Patch(70, 65, 35, 65, (x, y) => x >= 28 && x <= 42 ? (byte)(y < 40 ? 210 : 80) : null));
        Raw("SHTGB0", Patch(70, 65, 35, 65, (x, y) => x >= 23 && x <= 47 ? (byte)(y < 20 ? 255 : 180) : null));
        Raw("BAL1A0", Patch(12, 12, 6, 12, (x, y) => Math.Pow(x - 6, 2) + Math.Pow(y - 6, 2) < 30 ? (byte)250 : null));
        foreach (string sound in new[] { "PISTOL", "SHOTGN", "DOROPN", "DORCLS", "ITEMUP", "POPAIN", "PODTH1", "PLPAIN", "PLDETH", "NOWAY", "SWTCHN", "BAREXP", "FIRSHT", "FIRXPL" })
            Add("DS" + sound, w =>
            {
                const int n = 2205;
                w.Write((ushort)3); w.Write((ushort)11025); w.Write(n + 32);
                for (int i = -16; i < n + 16; i++)
                    w.Write((byte)(i < 0 || i >= n ? 128 : 128 + Math.Sin(i * .16) * 70 * (1 - i / (double)n)));
            });
        Raw("E1M1", []);
        Add("THINGS", w =>
        {
            Thing(w, 128, 128, 0, 1); Thing(w, 175, 128, 0, 2001);
            Thing(w, 450, 128, 180, 3004); Thing(w, 450, 200, 0, 2035); Thing(w, 180, 80, 0, 5);
        });
        Add("LINEDEFS", w =>
        {
            Line(w, 0, 1, 0, 65535); Line(w, 1, 2, 1, 65535);
            Line(w, 2, 3, 2, 3, 1); Line(w, 3, 0, 4, 65535);
            Line(w, 2, 4, 5, 65535); Line(w, 4, 5, 6, 7, 1);
            Line(w, 5, 3, 8, 65535); Line(w, 4, 6, 9, 65535);
            Line(w, 6, 7, 10, 65535, 11); Line(w, 7, 5, 11, 65535);
        });
        Add("SIDEDEFS", w =>
        {
            Side(w, 0); Side(w, 0); Side(w, 0, true); Side(w, 1, true); Side(w, 0);
            Side(w, 1); Side(w, 1, true); Side(w, 2, true); Side(w, 1);
            Side(w, 2); Side(w, 2); Side(w, 2);
        });
        Add("VERTEXES", w =>
        {
            foreach ((short x, short y) in new (short, short)[] { (0, 0), (0, 256), (256, 256), (256, 0), (288, 256), (288, 0), (544, 256), (544, 0) })
            { w.Write(x); w.Write(y); }
        });
        Add("SEGS", w =>
        {
            Seg(w, 0, 1, 0, 0); Seg(w, 1, 2, 1, 0); Seg(w, 2, 3, 2, 0); Seg(w, 3, 0, 3, 0);
            Seg(w, 3, 2, 2, 1); Seg(w, 2, 4, 4, 0); Seg(w, 4, 5, 5, 0); Seg(w, 5, 3, 6, 0);
            Seg(w, 5, 4, 5, 1); Seg(w, 4, 6, 7, 0); Seg(w, 6, 7, 8, 0); Seg(w, 7, 5, 9, 0);
        });
        Add("SSECTORS", w => { for (ushort i = 0; i < 3; i++) { w.Write((ushort)4); w.Write((ushort)(i * 4)); } });
        Add("NODES", w => { Node(w, 288, 32770, 32769); Node(w, 256, 0, 32768); });
        Add("SECTORS", w => { Sector(w, 0, 128, 240); Sector(w, 0, 0, 192); Sector(w, 16, 160, 200); });
        Raw("REJECT", [0, 0]);
        Raw("BLOCKMAP", []);
        using MemoryStream file = new();
        using BinaryWriter output = new(file, Encoding.ASCII, true);
        output.Write(Encoding.ASCII.GetBytes("PWAD")); output.Write(lumps.Count); output.Write(0);
        List<int> offsets = [];
        foreach (var lump in lumps) { offsets.Add((int)file.Position); output.Write(lump.Bytes); }
        int directory = (int)file.Position;
        for (int i = 0; i < lumps.Count; i++) { output.Write(offsets[i]); output.Write(lumps[i].Bytes.Length); Name(output, lumps[i].Name); }
        file.Position = 8; output.Write(directory);
        return file.ToArray();
    }

    static void Name(BinaryWriter w, string name) => w.Write(Encoding.ASCII.GetBytes(name.PadRight(8, '\0')));
    static void Texture(BinaryWriter w, string name, ushort width, ushort height, ushort patch)
    {
        Name(w, name); w.Write(0); w.Write(width); w.Write(height); w.Write(0); w.Write((ushort)1);
        w.Write((short)0); w.Write((short)0); w.Write(patch); w.Write((short)0); w.Write((short)0);
    }
    static void Thing(BinaryWriter w, short x, short y, short angle, ushort type)
    { w.Write(x); w.Write(y); w.Write(angle); w.Write(type); w.Write((ushort)7); }
    static void Line(BinaryWriter w, ushort a, ushort b, ushort right, ushort left, ushort special = 0)
    {
        w.Write(a); w.Write(b); w.Write((ushort)(left == 65535 ? 1 : 4)); w.Write(special);
        w.Write((ushort)0); w.Write(right); w.Write(left);
    }
    static void Side(BinaryWriter w, ushort sector, bool portal = false)
    {
        w.Write((short)0); w.Write((short)0); Name(w, "WALL"); Name(w, "WALL"); Name(w, portal ? "-" : "WALL"); w.Write(sector);
    }
    static void Seg(BinaryWriter w, ushort a, ushort b, ushort line, ushort direction)
    { w.Write(a); w.Write(b); w.Write((ushort)0); w.Write(line); w.Write(direction); w.Write((ushort)0); }
    static void Node(BinaryWriter w, short x, ushort right, ushort left)
    {
        w.Write(x); w.Write((short)0); w.Write((short)0); w.Write((short)256);
        for (int i = 0; i < 8; i++) w.Write((short)0);
        w.Write(right); w.Write(left);
    }
    static void Sector(BinaryWriter w, short floor, short ceiling, short light)
    {
        w.Write(floor); w.Write(ceiling); Name(w, "FLOOR"); Name(w, "CEIL");
        w.Write(light); w.Write((short)0); w.Write((short)0);
    }
    static byte[] Patch(int width, int height, short left, short top, Func<int, int, byte?> pixel)
    {
        using MemoryStream stream = new();
        using BinaryWriter w = new(stream, Encoding.ASCII, true);
        w.Write((ushort)width); w.Write((ushort)height); w.Write(left); w.Write(top);
        for (int x = 0; x < width; x++) w.Write(0);
        for (int x = 0; x < width; x++)
        {
            long position = stream.Position; stream.Position = 8 + x * 4; w.Write((int)position); stream.Position = position;
            int y = 0;
            while (y < height)
            {
                while (y < height && pixel(x, y) == null) y++;
                int start = y;
                List<byte> post = [];
                while (y < height && pixel(x, y) is byte color) { post.Add(color); y++; }
                if (post.Count == 0) break;
                w.Write((byte)start); w.Write((byte)post.Count); w.Write((byte)0);
                w.Write(post.ToArray()); w.Write((byte)0);
            }
            w.Write((byte)255);
        }
        return stream.ToArray();
    }
}
