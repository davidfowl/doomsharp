using System.Buffers.Binary;
using System.Text;

namespace DoomSharp;

public sealed class Wad
{
    public sealed record Lump(string Name, byte[] Data);
    public List<Lump> Lumps { get; } = [];
    public List<string> Maps => Lumps.Where(l => IsMap(l.Name)).Select(l => l.Name).ToList();

    public Wad(string path) : this(File.ReadAllBytes(path)) { }

    public Wad(byte[] bytes)
    {
        if (bytes.Length < 12 || (Encoding.ASCII.GetString(bytes, 0, 4) is not ("IWAD" or "PWAD")))
            throw new InvalidDataException("Not a classic IWAD or PWAD.");
        int count = I32(bytes, 4), directory = I32(bytes, 8);
        if (count < 0 || directory < 12 || directory > bytes.Length ||
            count > (bytes.Length - directory) / 16)
            throw new InvalidDataException("WAD directory is outside the file.");
        for (int i = 0; i < count; i++)
        {
            int entry = directory + i * 16;
            int offset = I32(bytes, entry), length = I32(bytes, entry + 4);
            if (offset < 0 || length < 0 || offset > bytes.Length - length)
                throw new InvalidDataException($"WAD lump {i} is outside the file.");
            Lumps.Add(new(Name(bytes, entry + 8), bytes.AsSpan(offset, length).ToArray()));
        }
    }

    public byte[]? Find(string name) => Lumps.LastOrDefault(l => l.Name == name.ToUpperInvariant())?.Data;
    public byte[] Require(string name) => Find(name) ?? throw new InvalidDataException($"Missing WAD lump: {name}");

    /// <summary>True when a raw 64x64 flat lump of the given name is present (used to probe animated flat frames).</summary>
    public bool HasFlat(string name) => Find(name) is { Length: 4096 };

    /// <summary>
    /// True when a composed wall texture of the given name is defined in TEXTURE1/TEXTURE2 (used to probe
    /// animated wall frames without fully decoding/compositing the texture).
    /// </summary>
    public bool HasTexture(string name)
    {
        foreach (string lump in new[] { "TEXTURE1", "TEXTURE2" })
        {
            byte[]? b = Find(lump);
            if (b == null || b.Length < 4) continue;
            int count = I32(b, 0);
            if (count < 0 || count > (b.Length - 4) / 4) continue;
            for (int i = 0; i < count; i++)
            {
                int p = I32(b, 4 + i * 4);
                if (p < 0 || p > b.Length - 22) continue;
                if (Name(b, p) == name) return true;
            }
        }
        return false;
    }
    public static bool IsMap(string name) => (name.Length == 4 && name[0] == 'E' && char.IsAsciiDigit(name[1]) &&
        name[2] == 'M' && char.IsAsciiDigit(name[3])) ||
        (name.Length == 5 && name.StartsWith("MAP", StringComparison.Ordinal) &&
         char.IsAsciiDigit(name[3]) && char.IsAsciiDigit(name[4]));
    public static string Name(byte[] b, int offset) =>
        Encoding.ASCII.GetString(b, offset, 8).TrimEnd('\0').ToUpperInvariant();
    public static short I16(byte[] b, int offset) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(offset, 2));
    public static ushort U16(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset, 2));
    public static int I32(byte[] b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
}

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double b) => new(a.X * b, a.Y * b);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public static double Cross(Vec a, Vec b) => a.X * b.Y - a.Y * b.X;
    public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y;
}

public sealed class Sector
{
    public double Floor, Ceiling;
    public readonly double OriginalCeiling;
    public readonly string FloorTexture, CeilingTexture;
    public readonly int Light, Special, Tag;
    public double DoorTime;
    public double DoorSpeed = 90;
    public int DoorDirection;
    public double FloorTarget, FloorOrigin, LiftWait, FloorSpeed;
    public int FloorDirection;
    public bool Lift;
    public double CrusherTop, CrusherBottom, CrusherSpeed;
    public int CrusherDirection;
    public bool CrusherPaused, CrusherSilent, CrusherSlowed;
    public Sector(byte[] b, int p)
    {
        Floor = Wad.I16(b, p); Ceiling = OriginalCeiling = Wad.I16(b, p + 2);
        FloorTexture = Wad.Name(b, p + 4); CeilingTexture = Wad.Name(b, p + 12);
        Light = Wad.I16(b, p + 20); Special = Wad.I16(b, p + 22); Tag = Wad.I16(b, p + 24);
    }
}

public sealed record Side(int XOffset, int YOffset, string Upper, string Lower, string Middle, int Sector);
public sealed class Line
{
    public readonly int A, B, Flags, Tag;
    public int Special;
    public readonly int Right, Left;
    public Line(byte[] b, int p)
    {
        A = Wad.U16(b, p); B = Wad.U16(b, p + 2); Flags = Wad.U16(b, p + 4);
        Special = Wad.U16(b, p + 6); Tag = Wad.U16(b, p + 8);
        Right = Wad.U16(b, p + 10); Left = Wad.U16(b, p + 12);
    }
}

public readonly record struct Seg(int A, int B, int Line, int Direction, int Offset);
public readonly record struct Subsector(int Count, int First);
public readonly record struct Node(Vec Origin, Vec Delta, ushort Right, ushort Left);
public readonly record struct Thing(Vec Position, int Angle, int Type, int Flags);

public sealed class Map
{
    public string Name { get; }
    public Vec[] Vertices { get; }
    public Side[] Sides { get; }
    public Line[] Lines { get; }
    public Sector[] Sectors { get; }
    public Seg[] Segs { get; }
    public Subsector[] Subsectors { get; }
    public Node[] Nodes { get; }
    public Thing[] Things { get; }
    public int[][] SectorLines { get; }
    public Vec[][] SubsectorPolygons { get; }

    public Map(Wad wad, string name)
    {
        Name = name.ToUpperInvariant();
        int marker = wad.Lumps.FindLastIndex(l => l.Name == Name && Wad.IsMap(l.Name));
        if (marker < 0) throw new InvalidDataException($"Map {Name} is not in this WAD.");
        byte[] Get(string lump)
        {
            for (int i = marker + 1; i < wad.Lumps.Count && !Wad.IsMap(wad.Lumps[i].Name); i++)
                if (wad.Lumps[i].Name == lump) return wad.Lumps[i].Data;
            throw new InvalidDataException($"{Name} is missing {lump} (only classic DOOM maps are supported).");
        }
        T[] Read<T>(string lump, int stride, Func<byte[], int, T> decode)
        {
            byte[] bytes = Get(lump);
            if (bytes.Length % stride != 0) throw new InvalidDataException($"Invalid {lump} record size.");
            return Enumerable.Range(0, bytes.Length / stride).Select(i => decode(bytes, i * stride)).ToArray();
        }
        Vertices = Read("VERTEXES", 4, (b, p) => new Vec(Wad.I16(b, p), Wad.I16(b, p + 2)));
        Sectors = Read("SECTORS", 26, (b, p) => new Sector(b, p));
        Sides = Read("SIDEDEFS", 30, (b, p) => new Side(Wad.I16(b, p), Wad.I16(b, p + 2),
            Wad.Name(b, p + 4), Wad.Name(b, p + 12), Wad.Name(b, p + 20), Wad.U16(b, p + 28)));
        Lines = Read("LINEDEFS", 14, (b, p) => new Line(b, p));
        Segs = Read("SEGS", 12, (b, p) => new Seg(Wad.U16(b, p), Wad.U16(b, p + 2),
            Wad.U16(b, p + 6), Wad.U16(b, p + 8), Wad.U16(b, p + 10)));
        Subsectors = Read("SSECTORS", 4, (b, p) => new Subsector(Wad.U16(b, p), Wad.U16(b, p + 2)));
        Nodes = Read("NODES", 28, (b, p) => new Node(new(Wad.I16(b, p), Wad.I16(b, p + 2)),
            new(Wad.I16(b, p + 4), Wad.I16(b, p + 6)), Wad.U16(b, p + 24), Wad.U16(b, p + 26)));
        Things = Read("THINGS", 10, (b, p) => new Thing(new(Wad.I16(b, p), Wad.I16(b, p + 2)),
            Wad.I16(b, p + 4), Wad.U16(b, p + 6), Wad.U16(b, p + 8)));
        Validate();
        ValidateBlockmap(Get("BLOCKMAP"));
        SectorLines = Enumerable.Range(0, Sectors.Length).Select(s =>
            Enumerable.Range(0, Lines.Length).Where(i => Sides[Lines[i].Right].Sector == s ||
            (Lines[i].Left != 65535 && Sides[Lines[i].Left].Sector == s)).ToArray()).ToArray();
        SubsectorPolygons = BuildPolygons();
    }

    Vec[][] BuildPolygons()
    {
        Vec[][] result = new Vec[Subsectors.Length][];
        double minX = Vertices.Min(v => v.X) - 8, maxX = Vertices.Max(v => v.X) + 8;
        double minY = Vertices.Min(v => v.Y) - 8, maxY = Vertices.Max(v => v.Y) + 8;
        Stack<(int Child, List<Vec> Polygon)> pending = new();
        pending.Push((Nodes.Length == 0 ? 32768 : Nodes.Length - 1,
            [new(minX, minY), new(minX, maxY), new(maxX, maxY), new(maxX, minY)]));
        HashSet<int> visited = [];
        while (pending.TryPop(out var item))
        {
            if (!visited.Add(item.Child)) throw new InvalidDataException("BSP children must form a tree.");
            if ((item.Child & 32768) != 0)
            {
                int index = item.Child & 32767;
                Subsector sub = Subsectors[index];
                List<Vec> poly = item.Polygon;
                for (int i = 0; i < sub.Count; i++)
                {
                    Seg seg = Segs[sub.First + i];
                    poly = Clip(poly, Vertices[seg.A], Vertices[seg.B] - Vertices[seg.A], true);
                }
                result[index] = poly.ToArray();
            }
            else
            {
                Node node = Nodes[item.Child];
                pending.Push((node.Right, Clip(item.Polygon, node.Origin, node.Delta, true)));
                pending.Push((node.Left, Clip(item.Polygon, node.Origin, node.Delta, false)));
            }
        }
        if (result.Any(p => p == null)) throw new InvalidDataException("Unreachable BSP subsector.");
        return result;
    }

    static List<Vec> Clip(List<Vec> polygon, Vec origin, Vec delta, bool right)
    {
        List<Vec> result = [];
        if (polygon.Count == 0) return result;
        Vec previous = polygon[^1];
        double previousDistance = Vec.Cross(delta, previous - origin) * (right ? 1 : -1);
        foreach (Vec point in polygon)
        {
            double distance = Vec.Cross(delta, point - origin) * (right ? 1 : -1);
            if ((distance <= 0) != (previousDistance <= 0))
                result.Add(previous + (point - previous) * (previousDistance / (previousDistance - distance)));
            if (distance <= 0) result.Add(point);
            previous = point; previousDistance = distance;
        }
        return result;
    }

    void Validate()
    {
        if (Sectors.Length == 0 || Subsectors.Length == 0) throw new InvalidDataException("Empty map geometry.");
        foreach (Side s in Sides)
            if (s.Sector >= Sectors.Length) throw new InvalidDataException("Invalid sidedef sector.");
        foreach (Line l in Lines)
            if (l.A >= Vertices.Length || l.B >= Vertices.Length || l.Right >= Sides.Length ||
                (l.Left != 65535 && l.Left >= Sides.Length))
                throw new InvalidDataException("Invalid linedef reference.");
        foreach (Seg s in Segs)
        {
            if (s.A >= Vertices.Length || s.B >= Vertices.Length || s.Line >= Lines.Length ||
                s.Direction > 1 || (s.Direction == 1 && Lines[s.Line].Left == 65535))
                throw new InvalidDataException("Invalid seg reference.");
            // A zero-length (or coincident-vertex) seg carries no direction, so the BSP polygon
            // builder's half-plane clip against it is a no-op: the enclosing subsector polygon is
            // left unclipped on that edge and can bleed past the map's actual geometry into the
            // bounding-box padding, producing a visibly stretched floor/ceiling plane.
            if (Vertices[s.A] == Vertices[s.B]) throw new InvalidDataException("Degenerate (zero-length) seg.");
        }
        foreach (Subsector s in Subsectors)
            if (s.Count == 0 || s.First + s.Count > Segs.Length) throw new InvalidDataException("Invalid subsector.");
        for (int i = 0; i < Nodes.Length; i++)
        {
            CheckChild(Nodes[i].Right, i); CheckChild(Nodes[i].Left, i);
        }
        void CheckChild(ushort child, int parent)
        {
            if ((child & 32768) != 0 ? (child & 32767) >= Subsectors.Length : child >= parent)
                throw new InvalidDataException("Invalid or cyclic BSP child.");
        }
    }

    void ValidateBlockmap(byte[] bytes)
    {
        // Collision scans linedefs directly; empty blockmaps are intentionally supported.
        if (bytes.Length == 0) return;
        if (bytes.Length < 8 || bytes.Length % 2 != 0) throw new InvalidDataException("Invalid BLOCKMAP header.");
        int width = Wad.U16(bytes, 4), height = Wad.U16(bytes, 6), words = bytes.Length / 2;
        long cells = (long)width * height;
        if (width == 0 || height == 0 || cells > words - 4) throw new InvalidDataException("Invalid BLOCKMAP dimensions.");
        bool[] validated = new bool[words];
        for (int i = 0; i < cells; i++)
        {
            int offset = Wad.U16(bytes, 8 + i * 2);
            if (offset < 4 + cells || offset >= words) throw new InvalidDataException("BLOCKMAP list offset is outside its data.");
            if (Wad.U16(bytes, offset * 2) != 0) throw new InvalidDataException("Invalid BLOCKMAP list header.");
            int end = offset + 1;
            while (end < words && !validated[end])
            {
                int line = Wad.U16(bytes, end * 2);
                validated[end] = true;
                if (line == 65535) break;
                if (line >= Lines.Length) throw new InvalidDataException("Invalid BLOCKMAP linedef reference.");
                end++;
            }
            if (end == words) throw new InvalidDataException("Unterminated BLOCKMAP list.");
        }
    }

    public int FindSector(Vec position)
    {
        int child = Nodes.Length == 0 ? 32768 : Nodes.Length - 1;
        while ((child & 32768) == 0)
        {
            Node n = Nodes[child];
            child = Vec.Cross(n.Delta, position - n.Origin) <= 0 ? n.Right : n.Left;
        }
        Seg seg = Segs[Subsectors[child & 32767].First];
        Line line = Lines[seg.Line];
        return Sides[seg.Direction == 0 ? line.Right : line.Left].Sector;
    }

    public IEnumerable<int> BackToFront(Vec position)
    {
        Stack<int> stack = new();
        stack.Push(Nodes.Length == 0 ? 32768 : Nodes.Length - 1);
        while (stack.Count > 0)
        {
            int child = stack.Pop();
            if ((child & 32768) != 0) { yield return child & 32767; continue; }
            Node n = Nodes[child];
            bool right = Vec.Cross(n.Delta, position - n.Origin) <= 0;
            stack.Push(right ? n.Right : n.Left);
            stack.Push(right ? n.Left : n.Right);
        }
    }

    public static double DistanceToLine(Vec p, Vec a, Vec b)
    {
        Vec d = b - a;
        double t = Math.Clamp(Vec.Dot(p - a, d) / Math.Max(1e-9, Vec.Dot(d, d)), 0, 1);
        return (p - (a + d * t)).Length;
    }

    public bool CanMove(Vec from, Vec to, double radius, double height, double feet, bool monster = false)
    {
        foreach (Line l in Lines)
        {
            Vec a = Vertices[l.A], b = Vertices[l.B];
            if (DistanceToLine(to, a, b) >= radius) continue;
            if (l.Left == 65535 || (l.Flags & 1) != 0 || (monster && (l.Flags & 2) != 0)) return false;
            Sector r = Sectors[Sides[l.Right].Sector], left = Sectors[Sides[l.Left].Sector];
            double floor = Math.Max(r.Floor, left.Floor), ceiling = Math.Min(r.Ceiling, left.Ceiling);
            if (floor - feet > 24 || ceiling - Math.Max(feet, floor) < height) return false;
        }
        Sector destination = Sectors[FindSector(to)];
        return destination.Ceiling - destination.Floor >= height && destination.Floor - feet <= 24;
    }

    public IEnumerable<int> TouchingSectors(Vec position, double radius)
    {
        yield return FindSector(position);
        foreach (Line line in Lines)
        {
            if (line.Left == 65535 || (line.Flags & 1) != 0 ||
                DistanceToLine(position, Vertices[line.A], Vertices[line.B]) >= radius) continue;
            yield return Sides[line.Right].Sector;
            yield return Sides[line.Left].Sector;
        }
    }

    public double FloorAt(Vec position, double radius) =>
        TouchingSectors(position, radius).Max(i => Sectors[i].Floor);

    public double CeilingAt(Vec position, double radius) =>
        TouchingSectors(position, radius).Min(i => Sectors[i].Ceiling);

    public bool ClearSight(Vec from, Vec to, double z)
    {
        foreach (Line l in Lines)
        {
            if (!Intersects(from, to, Vertices[l.A], Vertices[l.B], out double t) || t < .0001 || t > .9999) continue;
            if (l.Left == 65535) return false;
            Sector a = Sectors[Sides[l.Right].Sector], b = Sectors[Sides[l.Left].Sector];
            if (z <= Math.Max(a.Floor, b.Floor) || z >= Math.Min(a.Ceiling, b.Ceiling)) return false;
        }
        return true;
    }

    public static bool Intersects(Vec from, Vec to, Vec a, Vec b, out double t)
    {
        Vec d = to - from, e = b - a;
        double cross = Vec.Cross(d, e);
        t = 0;
        if (Math.Abs(cross) < 1e-9) return false;
        t = Vec.Cross(a - from, e) / cross;
        double u = Vec.Cross(a - from, d) / cross;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }

    public static double CircleImpact(Vec from, Vec to, Vec center, double radius)
    {
        Vec offset = from - center, travel = to - from;
        double c = Vec.Dot(offset, offset) - radius * radius;
        if (c <= 0) return 0;
        double a = Vec.Dot(travel, travel), b = Vec.Dot(offset, travel);
        double discriminant = b * b - a * c;
        if (a == 0 || b >= 0 || discriminant < 0) return double.PositiveInfinity;
        double t = (-b - Math.Sqrt(discriminant)) / a;
        return t is >= 0 and <= 1 ? t : double.PositiveInfinity;
    }

    public double ProjectileImpact(Vec from, Vec to, double z, double radius, double height)
    {
        double earliest = double.PositiveInfinity;
        foreach (Line line in Lines)
        {
            if (line.Left != 65535)
            {
                Sector front = Sectors[Sides[line.Right].Sector], back = Sectors[Sides[line.Left].Sector];
                if (z - height / 2 > Math.Max(front.Floor, back.Floor) &&
                    z + height / 2 < Math.Min(front.Ceiling, back.Ceiling)) continue;
            }
            Vec a = Vertices[line.A], b = Vertices[line.B], edge = b - a, travel = to - from;
            if (radius == 0)
            {
                if (Intersects(from, to, a, b, out double t)) earliest = Math.Min(earliest, t);
                continue;
            }
            if (DistanceToLine(from, a, b) <= radius) return 0;
            earliest = Math.Min(earliest, Math.Min(CircleImpact(from, to, a, radius), CircleImpact(from, to, b, radius)));
            double length = edge.Length;
            if (length == 0) continue;
            Vec tangent = edge * (1 / length), normal = new(-tangent.Y, tangent.X);
            double speed = Vec.Dot(travel, normal), start = Vec.Dot(from - a, normal);
            if (Math.Abs(speed) < 1e-9) continue;
            for (int side = -1; side <= 1; side += 2)
            {
                double t = (side * radius - start) / speed;
                if (t < 0 || t > 1) continue;
                double along = Vec.Dot(from + travel * t - a, tangent);
                if (along >= 0 && along <= length) earliest = Math.Min(earliest, t);
            }
        }
        return earliest;
    }
}
