using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DoomSharp.Tools;

sealed class NativeGame : IDisposable
{
    readonly string output, source, executable;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<object> events = [], frames = [];
    Process? xvfb, process;
    Task<string>? stdout, stderr;
    nint display;
    nuint window;
    string? log;
    string[] command = [];
    string? fixtureSha256;
    bool completed;
    NativeGame(string executable, string output, string source)
    {
        this.executable = executable; this.output = output; this.source = source;
        if (Directory.Exists(output)) throw new IOException("Evidence directory already exists: " + output);
        Directory.CreateDirectory(output);
    }
    public static async Task<NativeGame> Start(string executable, string[] arguments, string output, string source, bool audio = false)
    {
        NativeGame game = new(executable, output, source);
        try
        {
            game.xvfb = Process.Start(new ProcessStartInfo("Xvfb")
            {
                ArgumentList = { "-displayfd", "1", "-screen", "0", "1024x768x24", "-nolisten", "tcp" },
                RedirectStandardOutput = true, RedirectStandardError = true
            }) ?? throw new InvalidOperationException("Cannot start Xvfb.");
            string? number = await game.xvfb.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (number == null || !number.All(char.IsAsciiDigit)) throw new InvalidOperationException("Xvfb did not allocate an isolated display.");
            game.display = XOpenDisplay(":" + number);
            if (game.display == 0) throw new InvalidOperationException("Cannot connect to isolated X11 display.");
            ProcessStartInfo start = new(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["DISPLAY"] = ":" + number;
            if (audio)
            {
                string config = Path.Combine(output, "asound.conf");
                File.WriteAllText(config, "pcm.!default { type null }\n");
                start.Environment["ALSA_CONFIG_PATH"] = config;
            }
            else start.ArgumentList.Add("--no-audio");
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            foreach (string argument in new[] { "--frames", "100000", "--output", Path.Combine(output, "final.ppm") })
                start.ArgumentList.Add(argument);
            game.command = start.ArgumentList.ToArray();
            int wadOption = Array.IndexOf(arguments, "--wad");
            if (wadOption >= 0) game.fixtureSha256 = ProcessCheck.Hash(arguments[wadOption + 1]);
            game.process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start game.");
            game.stdout = game.process.StandardOutput.ReadToEndAsync();
            game.stderr = game.process.StandardError.ReadToEndAsync();
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed.TotalSeconds < 10 && !game.process.HasExited)
            {
                game.window = game.FindWindow();
                if (game.window != 0) break;
                await Task.Delay(20);
            }
            if (game.window == 0) throw new InvalidOperationException("Actual game window did not appear.");
            XSetInputFocus(game.display, game.window, 2, 0); XFlush(game.display);
            await Task.Delay(200);
            return game;
        }
        catch { game.Dispose(); throw; }
    }
    unsafe nuint FindWindow()
    {
        if (XQueryTree(display, XDefaultRootWindow(display), out _, out _, out nint children, out uint count) == 0)
            throw new InvalidOperationException("XQueryTree failed.");
        try
        {
            for (int i = 0; i < count; i++)
            {
                nuint child = ((nuint*)children)[i];
                if (XFetchName(display, child, out nint title) != 0 && title != 0)
                {
                    try { if (Marshal.PtrToStringUTF8(title)?.StartsWith("DoomSharp", StringComparison.Ordinal) == true) return child; }
                    finally { XFree(title); }
                }
            }
            return 0;
        }
        finally { if (children != 0) XFree(children); }
    }
    public void Key(string name, bool down)
    {
        if (process == null || process.HasExited) throw new InvalidOperationException("Game exited before native input completed.");
        nuint symbol = XStringToKeysym(name);
        byte code = XKeysymToKeycode(display, symbol);
        if (code == 0 || XKeycodeToKeysym(display, code, 0) != symbol || XTestFakeKeyEvent(display, code, down ? 1 : 0, 0) == 0)
            throw new InvalidOperationException("Native key/server mapping failed: " + name);
        XFlush(display);
        events.Add(new { seconds = clock.Elapsed.TotalSeconds, name, down, symbol = (ulong)symbol, code });
    }
    public void Hold(string name, double seconds)
    {
        Key(name, true);
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        Key(name, false);
        Thread.Sleep(120);
    }
    public static void Until(Stopwatch start, double seconds)
    {
        double remaining = seconds - start.Elapsed.TotalSeconds;
        if (remaining > 0) Thread.Sleep(TimeSpan.FromSeconds(remaining));
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Image
    {
        public int Width, Height, XOffset, Format;
        public nint Data;
        public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, Stride, Bits;
        public nuint Red, Green, Blue;
        public nint Extra;
    }
    public Frame Capture(string name)
    {
        if (XGetGeometry(display, window, out _, out _, out _, out uint width, out uint height, out _, out _) == 0)
            throw new InvalidOperationException("Cannot read actual window geometry.");
        nint image = XGetImage(display, window, 0, 0, width, height, nuint.MaxValue, 2);
        if (image == 0) throw new InvalidOperationException("Cannot read native framebuffer.");
        try
        {
            Image info = Marshal.PtrToStructure<Image>(image);
            if (info.Bits != 32 || info.ByteOrder != 0 || info.Red != 0xff0000 || info.Green != 0xff00 || info.Blue != 0xff)
                throw new PlatformNotSupportedException("Unsupported native framebuffer layout.");
            byte[] raw = new byte[info.Stride * info.Height], rgb = new byte[info.Width * info.Height * 3];
            Marshal.Copy(info.Data, raw, 0, raw.Length);
            for (int y = 0; y < info.Height; y++) for (int x = 0; x < info.Width; x++)
            {
                int from = y * info.Stride + x * 4, to = (y * info.Width + x) * 3;
                rgb[to] = raw[from + 2]; rgb[to + 1] = raw[from + 1]; rgb[to + 2] = raw[from];
            }
            Frame frame = new(info.Width, info.Height, rgb);
            string path = Path.Combine(output, name + ".ppm");
            frame.Save(path);
            frames.Add(new { name, seconds = clock.Elapsed.TotalSeconds, frame.Width, frame.Height, sha256 = ProcessCheck.Hash(path) });
            return frame;
        }
        finally { XDestroyImage(image); }
    }
    public async Task<string> Quit()
    {
        Key("Escape", true);
        await process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        log = await stdout! + await stderr!;
        File.WriteAllText(Path.Combine(output, "game.log"), log);
        if (process.ExitCode != 0 || !Frame.Complete(Path.Combine(output, "final.ppm"), 640, 400))
            throw new InvalidOperationException("Game did not exit normally with a complete final frame.");
        _ = ProcessCheck.Position(log);
        completed = true;
        return log;
    }
    public void Dispose()
    {
        if (process != null)
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            log ??= stdout!.GetAwaiter().GetResult() + stderr!.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "game.log"), log);
            process.Dispose();
        }
        if (display != 0) { XCloseDisplay(display); display = 0; }
        if (xvfb != null)
        {
            if (!xvfb.HasExited) { xvfb.Kill(entireProcessTree: true); xvfb.WaitForExit(); }
            File.WriteAllText(Path.Combine(output, "xvfb.log"), xvfb.StandardError.ReadToEnd());
            xvfb.Dispose();
        }
        File.WriteAllText(Path.Combine(output, "session.json"), JsonSerializer.Serialize(new
        {
            sourceCommit = source, sourceProvenance = "Caller-supplied build revision", executable,
            executableSha256 = ProcessCheck.Hash(executable), completed, events, frames,
            command, fixtureSha256, runnerSha256 = ProcessCheck.Hash(typeof(NativeGame).Assembly.Location),
            limits = "Synthetic native XTest input/framebuffer and optional ALSA null; no physical input or audible playback."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    const string X11 = "libX11.so.6";
    [DllImport(X11)] static extern nint XOpenDisplay([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(X11)] static extern int XCloseDisplay(nint display);
    [DllImport(X11)] static extern nuint XDefaultRootWindow(nint display);
    [DllImport(X11)] static extern int XQueryTree(nint d, nuint w, out nuint root, out nuint parent, out nint children, out uint count);
    [DllImport(X11)] static extern int XFetchName(nint d, nuint w, out nint name);
    [DllImport(X11)] static extern int XFree(nint pointer);
    [DllImport(X11)] static extern int XSetInputFocus(nint d, nuint w, int revert, nuint time);
    [DllImport(X11)] static extern nuint XStringToKeysym([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(X11)] static extern byte XKeysymToKeycode(nint d, nuint symbol);
    [DllImport(X11)] static extern nuint XKeycodeToKeysym(nint d, byte code, int index);
    [DllImport(X11)] static extern int XFlush(nint d);
    [DllImport("libXtst.so.6")] static extern int XTestFakeKeyEvent(nint d, uint code, int down, nuint delay);
    [DllImport(X11)] static extern int XGetGeometry(nint d, nuint w, out nuint root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport(X11)] static extern nint XGetImage(nint d, nuint w, int x, int y, uint width, uint height, nuint planes, int format);
    [DllImport(X11)] static extern int XDestroyImage(nint image);
}
