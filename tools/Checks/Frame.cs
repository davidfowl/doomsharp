using System.Security.Cryptography;
using System.Text;

namespace DoomSharp.Tools;

sealed record Frame(int Width, int Height, byte[] Rgb)
{
    public string Hash => Convert.ToHexStringLower(SHA256.HashData(Rgb));
    public int Pixel(int x, int y)
    {
        int i = (y * Width + x) * 3;
        return Rgb[i] << 16 | Rgb[i + 1] << 8 | Rgb[i + 2];
    }
    public void Save(string path)
    {
        using var stream = File.Create(path);
        stream.Write(Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n"));
        stream.Write(Rgb);
    }
    public static bool Complete(string path, int width, int height)
    {
        if (!File.Exists(path)) return false;
        byte[] bytes = File.ReadAllBytes(path), header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        return bytes.Length == header.Length + width * height * 3 && bytes.AsSpan(0, header.Length).SequenceEqual(header);
    }
    public double Background()
    {
        int count = 0, pixels = Width * (Height * 7 / 8);
        for (int i = 0; i < pixels; i++)
            if (Rgb[i * 3] == 8 && Rgb[i * 3 + 1] == 9 && Rgb[i * 3 + 2] == 11) count++;
        return count / (double)pixels;
    }
    public int SwitchPixels(bool on)
    {
        int count = 0;
        for (int i = 0; i < Width * (Height * 7 / 8); i++)
        {
            int r = Rgb[i * 3], g = Rgb[i * 3 + 1], b = Rgb[i * 3 + 2];
            if (on ? g > r * 2 && g > b * 2 : r > g * 2 && r > b * 2) count++;
        }
        return count;
    }
    public int Ammo()
    {
        string[] digits = [
            ".###.#...##..###.#.###..##...#.###.", "..#...##....#....#....#....#...###.",
            ".###.#...#....#...#...#...#...#####", "####.....#....#.###.....#....#####.",
            "...#...##..#.#.#..#.#####...#....#.", "######....#....####.....#....#####.",
            ".###.#....#....####.#...##...#.###.", "#####....#...#...#...#....#....#...",
            ".###.#...##...#.###.#...##...#.###.", ".###.#...##...#.####....#....#.###."];
        int value = 0;
        foreach (int x in new[] { 320, 332 })
        {
            var glyph = new StringBuilder();
            for (int row = 0; row < 7; row++) for (int col = 0; col < 5; col++)
                glyph.Append(Pixel((int)((x + col * 2 + .5) * Width / 640), (int)((355 + row * 2 + .5) * Height / 400)) == 0xffdb8a ? '#' : '.');
            int digit = Array.IndexOf(digits, glyph.ToString());
            if (digit < 0) throw new InvalidDataException("Cannot decode native ammo HUD: " + glyph);
            value = value * 10 + digit;
        }
        return value;
    }
    public sealed record Geometry(int? Ceiling, int? Wall);
    public Geometry Landmarks()
    {
        int? ceiling = null, wall = null;
        for (int row = 0; row < Height * 7 / 8; row++)
        {
            int color = Pixel((int)(Width * .55), row), gray = color & 255;
            if ((color >> 8 & 255) == gray && (color >> 16) == gray && gray is >= 60 and <= 78) ceiling = row;
            else break;
        }
        for (int row = 0; row < Height * 7 / 8; row++)
            for (int col = Width / 2 - 80; col <= Width / 2 + 80; col += 4)
            {
                int color = Pixel(col, row), gray = color & 255;
                if ((color >> 8 & 255) == gray && (color >> 16) == gray && gray is >= 80 and <= 100) { wall = row; break; }
            }
        return new(ceiling, wall);
    }
}
