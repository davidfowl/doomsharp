using System.Runtime.InteropServices;

namespace DoomSharp;

public sealed class Audio : IDisposable
{
    public const int SampleRate = 22050;
    readonly Assets assets;
    readonly Dictionary<string, short[]?> sounds = new(StringComparer.Ordinal);
    readonly List<(short[] Samples, int Position)> voices = [];
    readonly object gate = new();
    readonly Thread worker;
    readonly IAudioDevice device;
    volatile bool stop;
    volatile Exception? failure;
    long submittedSamples;
    public long SubmittedSamples => Interlocked.Read(ref submittedSamples);
    public int PlayedSounds { get; private set; }

    public Audio(Assets assets)
    {
        this.assets = assets;
        device = OperatingSystem.IsLinux() ? new AlsaDevice() : OperatingSystem.IsWindows() ? new WaveDevice() :
            OperatingSystem.IsMacOS() ? new AudioQueueDevice() :
            throw new PlatformNotSupportedException("No audio backend for this OS.");
        worker = new Thread(Run) { IsBackground = true, Name = "DoomSharp audio" };
        worker.Start();
    }
    public void Play(string name)
    {
        if (failure != null) throw new InvalidOperationException("Audio playback failed; use --no-audio if no audio device is available.", failure);
        if (!sounds.TryGetValue(name, out short[]? sound)) { sound = assets.Sound(name); sounds[name] = sound; }
        if (sound == null) return;
        PlayedSounds++;
        lock (gate)
        {
            if (voices.Count == 16) voices.RemoveAt(0);
            voices.Add((sound, 0));
        }
    }
    void Run()
    {
        try
        {
            short[] output = new short[512];
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            while (!stop)
            {
                Array.Clear(output);
                lock (gate)
                {
                    for (int v = voices.Count - 1; v >= 0; v--)
                    {
                        (short[] samples, int position) = voices[v];
                        int count = Math.Min(output.Length, samples.Length - position);
                        for (int i = 0; i < count; i++) output[i] = (short)Math.Clamp(output[i] + samples[position + i], short.MinValue, short.MaxValue);
                        position += count;
                        if (position == samples.Length) voices.RemoveAt(v);
                        else voices[v] = (samples, position);
                    }
                }
                device.Write(output);
                long total = Interlocked.Add(ref submittedSamples, output.Length);
                double wait = total / (double)SampleRate - clock.Elapsed.TotalSeconds;
                if (wait > .001) Thread.Sleep((int)(wait * 1000));
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            Console.Error.WriteLine($"Audio stopped: {ex.Message}");
        }
    }
    public void Check()
    {
        if (failure != null) throw new InvalidOperationException("Audio output stopped.", failure);
    }
    public void Dispose() { stop = true; worker.Join(); device.Dispose(); }
}

interface IAudioDevice : IDisposable { void Write(short[] samples); }

sealed unsafe class AlsaDevice : IAudioDevice
{
    readonly nint pcm;
    public AlsaDevice()
    {
        Check(snd_pcm_open(out pcm, "default", 0, 0));
        int result = snd_pcm_set_params(pcm, 2, 3, 1, Audio.SampleRate, 1, 80000);
        if (result < 0) { snd_pcm_close(pcm); Check(result); }
    }
    public void Write(short[] samples)
    {
        fixed (short* data = samples)
        {
            int position = 0;
            while (position < samples.Length)
            {
                long result = snd_pcm_writei(pcm, data + position, (nuint)(samples.Length - position));
                if (result < 0) Check(snd_pcm_recover(pcm, (int)result, 1));
                else if (result == 0) throw new IOException("ALSA made no playback progress.");
                else position += (int)result;
            }
        }
    }
    static void Check(int result)
    {
        if (result < 0) throw new IOException($"ALSA: {Marshal.PtrToStringUTF8(snd_strerror(result))}. Use --no-audio to run without sound.");
    }
    public void Dispose() { snd_pcm_drop(pcm); snd_pcm_close(pcm); }
    [DllImport("libasound.so.2")] static extern int snd_pcm_open(out nint pcm, [MarshalAs(UnmanagedType.LPStr)] string name, int stream, int mode);
    [DllImport("libasound.so.2")] static extern int snd_pcm_set_params(nint pcm, int format, int access, uint channels, uint rate, int resample, uint latency);
    [DllImport("libasound.so.2")] static extern long snd_pcm_writei(nint pcm, short* data, nuint size);
    [DllImport("libasound.so.2")] static extern int snd_pcm_recover(nint pcm, int error, int silent);
    [DllImport("libasound.so.2")] static extern int snd_pcm_drop(nint pcm);
    [DllImport("libasound.so.2")] static extern int snd_pcm_close(nint pcm);
    [DllImport("libasound.so.2")] static extern nint snd_strerror(int error);
}

sealed unsafe class WaveDevice : IAudioDevice
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct Format { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort Align, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct Header
    {
        public nint Data;
        public uint Length, Recorded;
        public nuint User;
        public uint Flags, Loops;
        public nint Next;
        public nuint Reserved;
    }
    readonly nint device;
    public WaveDevice()
    {
        Format f = new() { Tag = 1, Channels = 1, Rate = Audio.SampleRate, BytesPerSecond = Audio.SampleRate * 2, Align = 2, Bits = 16 };
        Check(waveOutOpen(out device, uint.MaxValue, ref f, 0, 0, 0));
    }
    public void Write(short[] samples)
    {
        fixed (short* p = samples)
        {
            Header header = new() { Data = (nint)p, Length = (uint)samples.Length * 2 };
            Check(waveOutPrepareHeader(device, &header, (uint)sizeof(Header)));
            Check(waveOutWrite(device, &header, (uint)sizeof(Header)));
            while ((Volatile.Read(ref header.Flags) & 1) == 0) Thread.Sleep(1);
            Check(waveOutUnprepareHeader(device, &header, (uint)sizeof(Header)));
        }
    }
    static void Check(uint result) { if (result != 0) throw new IOException($"Windows waveOut error {result}."); }
    public void Dispose() { waveOutReset(device); Check(waveOutClose(device)); }
    [DllImport("winmm")] static extern uint waveOutOpen(out nint device, uint id, ref Format format, nint callback, nuint instance, uint flags);
    [DllImport("winmm")] static extern uint waveOutPrepareHeader(nint d, Header* h, uint size);
    [DllImport("winmm")] static extern uint waveOutWrite(nint d, Header* h, uint size);
    [DllImport("winmm")] static extern uint waveOutUnprepareHeader(nint d, Header* h, uint size);
    [DllImport("winmm")] static extern uint waveOutReset(nint d);
    [DllImport("winmm")] static extern uint waveOutClose(nint d);
}

sealed unsafe class AudioQueueDevice : IAudioDevice
{
    const string Lib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    [StructLayout(LayoutKind.Sequential)]
    struct Format
    {
        public double Rate;
        public uint Id, Flags, BytesPerPacket, FramesPerPacket, BytesPerFrame, Channels, Bits, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Buffer
    {
        public uint Capacity;
        public nint Data;
        public uint Size;
        public nint User;
        public uint PacketCapacity;
        public nint Packets;
        public uint PacketCount;
    }
    readonly nint queue;
    readonly Buffer* buffer;
    readonly nint completed;
    public AudioQueueDevice()
    {
        completed = Marshal.AllocHGlobal(4);
        *(int*)completed = 1;
        Format format = new()
        {
            Rate = Audio.SampleRate, Id = 0x6c70636d, Flags = 4 | 8,
            BytesPerPacket = 2, FramesPerPacket = 1, BytesPerFrame = 2, Channels = 1, Bits = 16
        };
        Check(AudioQueueNewOutput(ref format, &Callback, completed, 0, 0, 0, out queue));
        Buffer* allocated;
        Check(AudioQueueAllocateBuffer(queue, 1024, out allocated)); buffer = allocated;
    }
    [UnmanagedCallersOnly]
    static void Callback(nint user, nint queue, Buffer* buffer) => Volatile.Write(ref *(int*)user, 1);
    public void Write(short[] samples)
    {
        while (Volatile.Read(ref *(int*)completed) == 0) Thread.Sleep(1);
        fixed (short* data = samples) new ReadOnlySpan<byte>(data, samples.Length * 2).CopyTo(new Span<byte>((void*)buffer->Data, (int)buffer->Capacity));
        buffer->Size = (uint)samples.Length * 2;
        Volatile.Write(ref *(int*)completed, 0);
        Check(AudioQueueEnqueueBuffer(queue, buffer, 0, 0));
        Check(AudioQueueStart(queue, 0));
    }
    static void Check(int result) { if (result != 0) throw new IOException($"macOS AudioQueue error {result}."); }
    public void Dispose()
    {
        Check(AudioQueueStop(queue, true)); Check(AudioQueueDispose(queue, true)); Marshal.FreeHGlobal(completed);
    }
    [DllImport(Lib)] static extern int AudioQueueNewOutput(ref Format format, delegate* unmanaged<nint, nint, Buffer*, void> callback, nint user, nint loop, nint mode, uint flags, out nint queue);
    [DllImport(Lib)] static extern int AudioQueueAllocateBuffer(nint queue, uint size, out Buffer* buffer);
    [DllImport(Lib)] static extern int AudioQueueEnqueueBuffer(nint queue, Buffer* buffer, uint count, nint packets);
    [DllImport(Lib)] static extern int AudioQueueStart(nint queue, nint time);
    [DllImport(Lib)] static extern int AudioQueueStop(nint queue, [MarshalAs(UnmanagedType.I1)] bool immediate);
    [DllImport(Lib)] static extern int AudioQueueDispose(nint queue, [MarshalAs(UnmanagedType.I1)] bool immediate);
}
