using System.Runtime.InteropServices.JavaScript;
using DoomSharp;

public static partial class PageExports
{
    const int SampleRate = DoomSharp.Assets.SoundSampleRate;
    static readonly Renderer Renderer = new();
    static readonly Renderer BenchmarkRenderer = new();
    static readonly Input Input = new();
    static readonly byte[] Rgba = new byte[640 * 400 * 4];
    static readonly Queue<(Buttons Button, bool Down)> Events = new();
    static readonly Queue<byte[]> Sounds = new();
    static readonly Dictionary<string, byte[]?> SoundCache = new(StringComparer.Ordinal);
    static readonly Dictionary<string, byte[]?> MusicCache = new(StringComparer.Ordinal);
    static Game? game;
    static Game? benchmarkGame;
    static double previous = -1, accumulator;
    static string? musicName;
    static bool musicChanged, muted;

    [JSImport("save", "app.host")]
    static extern void Save(string json);

    [JSImport("load", "app.host")]
    static extern string? Load();

    [JSExport("start")]
    public static string Start(byte[] bytes, string map)
    {
        Wad wad = new(bytes);
        string selected = string.IsNullOrWhiteSpace(map)
            ? wad.Maps.FirstOrDefault() ?? throw new InvalidDataException("WAD contains no classic maps.")
            : map.Trim().ToUpperInvariant();
        Game next = new(wad, selected);
        Game benchmark = new(wad, selected);
        next.PlaySound = QueueSound;
        next.PlayMusic = name => { musicName = name; musicChanged = true; };
        next.SaveGame = Save;
        next.LoadGame = Load;
        game = next;
        benchmarkGame = benchmark;
        SoundCache.Clear();
        MusicCache.Clear();
        Sounds.Clear();
        musicName = null;
        musicChanged = true;
        ResetInput();
        game.StartMusic();
        return selected;
    }

    static void QueueSound(string name)
    {
        if (muted) return;
        if (!SoundCache.TryGetValue(name, out byte[]? bytes))
        {
            short[]? samples = game!.Assets.Sound(name);
            bytes = samples == null ? null : Pcm(samples);
            if (bytes == null) Console.Error.WriteLine($"Missing sound DS{name}; this effect cannot be played.");
            SoundCache[name] = bytes;
        }
        if (bytes != null) Sounds.Enqueue(bytes);
    }

    static byte[] Pcm(short[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        return bytes;
    }

    [JSExport("frame")]
    public static byte[] Frame(double milliseconds, double mouseX, bool active)
    {
        Game? frameGame = game;
        if (frameGame == null) return Rgba;
        if (active)
        {
            if (previous >= 0) accumulator += Math.Clamp((milliseconds - previous) / 1000, 0, .25);
            Input.MouseX += mouseX;
            while (accumulator >= 1.0 / 35)
            {
                HashSet<Buttons> changed = [];
                while (Events.TryPeek(out var next) && changed.Add(next.Button))
                {
                    Events.Dequeue();
                    Input.Set(next.Button, next.Down);
                }
                frameGame.Tick(Input, 1.0 / 35);
                accumulator -= 1.0 / 35;
            }
            previous = milliseconds;
        }
        else ResetInput();
        Renderer.Render(frameGame);
        for (int i = 0; i < Renderer.Pixels.Length; i++)
        {
            int color = Renderer.Pixels[i];
            Rgba[i * 4] = (byte)(color >> 16);
            Rgba[i * 4 + 1] = (byte)(color >> 8);
            Rgba[i * 4 + 2] = (byte)color;
            Rgba[i * 4 + 3] = 255;
        }
        return Rgba;
    }

    [JSExport("nextSound")]
    public static byte[] NextSound() => Sounds.Count == 0 ? [] : Sounds.Dequeue();

    [JSExport("nextMusic")]
    public static string NextMusic()
    {
        if (!musicChanged) return "\0";
        musicChanged = false;
        return muted ? "" : musicName ?? "";
    }

    [JSExport("musicBytes")]
    public static byte[] MusicBytes()
    {
        if (musicName == null || muted || game == null) return [];
        if (!MusicCache.TryGetValue(musicName, out byte[]? bytes))
        {
            short[]? samples = game.Assets.Music(musicName);
            bytes = samples == null ? null : Pcm(samples);
            if (bytes == null) Console.Error.WriteLine($"Missing or unsupported music {musicName}; no track will play.");
            MusicCache[musicName] = bytes;
        }
        return bytes ?? [];
    }

    [JSExport("mute")]
    public static void Mute(bool value)
    {
        muted = value;
        Sounds.Clear();
        musicChanged = true;
    }

    [JSExport("key")]
    public static void Key(string code, bool down)
    {
        Buttons button = BrowserInput.FromCode(code);
        if (button != Buttons.None) Events.Enqueue((button, down));
    }

    [JSExport("resetInput")]
    public static void ResetInput()
    {
        Input.Held = Buttons.None;
        Events.Clear();
        Input.MouseX = 0;
        previous = -1;
        accumulator = 0;
    }

    [JSExport("benchmark")]
    public static int Benchmark(int frames)
    {
        if (benchmarkGame == null || frames < 1 || frames > 1000) throw new ArgumentOutOfRangeException(nameof(frames));
        int checksum = 0;
        for (int i = 0; i < frames; i++)
        {
            BenchmarkRenderer.Render(benchmarkGame);
            checksum = unchecked(checksum * 31 + BenchmarkRenderer.Pixels[(i * 7919) % BenchmarkRenderer.Pixels.Length]);
        }
        return checksum;
    }
}
