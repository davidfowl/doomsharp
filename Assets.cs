namespace DoomSharp;

public sealed class Picture
{
    public int Width { get; }
    public int Height { get; }
    public int Left { get; }
    public int Top { get; }
    public int[] Pixels { get; }
    public Picture(int width, int height, int left = 0, int top = 0)
    {
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new InvalidDataException("Invalid picture dimensions.");
        Width = width; Height = height; Left = left; Top = top;
        Pixels = new int[width * height];
        Array.Fill(Pixels, -1);
    }
    public int Sample(int x, int y) => Pixels[Mod(y, Height) * Width + Mod(x, Width)];
    static int Mod(int a, int b) => ((a % b) + b) % b;
}

public sealed class Assets
{
    public const int SoundSampleRate = 22050;
    readonly Wad wad;
    readonly Dictionary<string, Picture> pictures = new(StringComparer.Ordinal);
    readonly Dictionary<string, Picture> textures = new(StringComparer.Ordinal);
    readonly Dictionary<string, Picture> flats = new(StringComparer.Ordinal);
    public int[] Palette { get; } = new int[256];
    public string SkyName { get; }
    public int MissingTextures { get; private set; }
    readonly HashSet<string> missing = new(StringComparer.Ordinal);

    public Assets(Wad wad, string map)
    {
        this.wad = wad;
        byte[] palette = wad.Require("PLAYPAL");
        if (palette.Length < 768) throw new InvalidDataException("PLAYPAL has no complete palette.");
        for (int i = 0; i < 256; i++) Palette[i] = palette[i * 3] << 16 | palette[i * 3 + 1] << 8 | palette[i * 3 + 2];
        SkyName = map.StartsWith('E') ? $"SKY{map[1]}" :
            int.TryParse(map.AsSpan(3), out int n) && n > 20 ? "SKY3" : n > 11 ? "SKY2" : "SKY1";
        byte[] names = wad.Require("PNAMES");
        if (names.Length < 4) throw new InvalidDataException("Truncated PNAMES.");
        int count = Wad.I32(names, 0);
        if (count < 0 || count > (names.Length - 4) / 8) throw new InvalidDataException("Invalid PNAMES.");
        string[] patches = Enumerable.Range(0, count).Select(i => Wad.Name(names, 4 + i * 8)).ToArray();
        foreach (string lump in new[] { "TEXTURE1", "TEXTURE2" })
        {
            byte[]? b = wad.Find(lump);
            if (b == null) continue;
            if (b.Length < 4) throw new InvalidDataException($"Truncated {lump}.");
            int num = Wad.I32(b, 0);
            if (num < 0 || num > (b.Length - 4) / 4) throw new InvalidDataException($"Invalid {lump}.");
            for (int i = 0; i < num; i++)
            {
                int p = Wad.I32(b, 4 + i * 4);
                if (p < 0 || p > b.Length - 22) throw new InvalidDataException("Invalid texture offset.");
                string name = Wad.Name(b, p);
                Picture texture = new(Wad.U16(b, p + 12), Wad.U16(b, p + 14));
                int patchCount = Wad.U16(b, p + 20);
                if (patchCount > (b.Length - p - 22) / 10) throw new InvalidDataException("Invalid texture patch count.");
                for (int j = 0; j < patchCount; j++)
                {
                    int q = p + 22 + j * 10, index = Wad.U16(b, q + 4);
                    if (index >= patches.Length) throw new InvalidDataException("Texture patch index outside PNAMES.");
                    Picture? patch = Patch(patches[index]);
                    if (patch == null) throw new InvalidDataException($"Texture {name} requires missing patch {patches[index]}.");
                    int ox = Wad.I16(b, q), oy = Wad.I16(b, q + 2);
                    for (int y = 0; y < patch.Height; y++)
                        for (int x = 0; x < patch.Width; x++)
                        {
                            int dx = ox + x, dy = oy + y, color = patch.Pixels[y * patch.Width + x];
                            if (dx >= 0 && dx < texture.Width && dy >= 0 && dy < texture.Height && color >= 0)
                                texture.Pixels[dy * texture.Width + dx] = color;
                        }
                }
                textures[name] = texture;
            }
        }
    }

    public Picture? Patch(string name)
    {
        if (pictures.TryGetValue(name, out Picture? cached)) return cached;
        byte[]? b = wad.Find(name);
        if (b == null) return null;
        if (b.Length < 8) throw new InvalidDataException($"Truncated patch {name}.");
        Picture picture = new(Wad.U16(b, 0), Wad.U16(b, 2), Wad.I16(b, 4), Wad.I16(b, 6));
        if (picture.Width > (b.Length - 8) / 4) throw new InvalidDataException($"Truncated columns in {name}.");
        for (int x = 0; x < picture.Width; x++)
        {
            int p = Wad.I32(b, 8 + x * 4), previous = -1;
            if (p < 0 || p >= b.Length) throw new InvalidDataException($"Invalid patch column in {name}.");
            while (b[p] != 255)
            {
                if (p > b.Length - 4) throw new InvalidDataException($"Truncated patch post in {name}.");
                int top = b[p], length = b[p + 1];
                if (top <= previous) top += previous;
                previous = top;
                if (length > b.Length - p - 4) throw new InvalidDataException($"Invalid post length in {name}.");
                for (int y = 0; y < length && top + y < picture.Height; y++)
                    picture.Pixels[(top + y) * picture.Width + x] = Palette[b[p + 3 + y]];
                p += length + 4;
                if (p >= b.Length) throw new InvalidDataException($"Unterminated patch column in {name}.");
            }
        }
        pictures[name] = picture;
        return picture;
    }

    public Picture Texture(string name)
    {
        if (textures.TryGetValue(name, out Picture? texture)) return texture;
        Warn(name);
        return Checker();
    }

    public Picture Flat(string name)
    {
        if (flats.TryGetValue(name, out Picture? flat)) return flat;
        byte[]? b = wad.Find(name);
        if (b == null) { Warn(name); return Checker(); }
        if (b.Length != 4096) throw new InvalidDataException($"Flat {name} must contain 4096 palette indices.");
        flat = new(64, 64);
        for (int i = 0; i < b.Length; i++) flat.Pixels[i] = Palette[b[i]];
        flats[name] = flat;
        return flat;
    }

    void Warn(string name)
    {
        if (!missing.Add(name)) return;
        MissingTextures++;
        Console.Error.WriteLine($"Missing texture/flat {name}; displaying diagnostic checkerboard.");
    }

    static Picture Checker()
    {
        Picture p = new(16, 16);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++) p.Pixels[y * 16 + x] = ((x / 8 + y / 8) & 1) == 0 ? 0xff00ff : 0x111111;
        return p;
    }

    public short[]? Sound(string name)
    {
        byte[]? b = wad.Find("DS" + name);
        if (b == null) return null;
        if (b.Length < 8 || Wad.U16(b, 0) != 3) throw new InvalidDataException($"Unsupported sound DS{name}.");
        int rate = Wad.U16(b, 2), count = Wad.I32(b, 4);
        if (rate == 0 || count < 0 || count > b.Length - 8) throw new InvalidDataException($"Invalid sound DS{name}.");
        // DMX sound lumps conventionally have 16 guard samples at either end.
        int start = count > 32 ? 24 : 8, length = count > 32 ? count - 32 : count;
        short[] pcm = new short[(int)((long)length * SoundSampleRate / rate)];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)((b[start + (int)((long)i * rate / SoundSampleRate)] - 128) * 128);
        return pcm;
    }

    /// <summary>Decodes a "D_*" music lump (MUS or Standard MIDI File) into one loop's worth of PCM, or null if absent/unsupported.</summary>
    public short[]? Music(string lumpName) => DoomSharp.Music.Render(wad.Find(lumpName), SoundSampleRate);
}
