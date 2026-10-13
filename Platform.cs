using System.Runtime.InteropServices;

namespace DoomSharp;

public interface IPlatform : IDisposable
{
    void Poll(Input input);
    void Present(int[] pixels);
}

static class Platform
{
    public static IPlatform Open(int width, int height) =>
        OperatingSystem.IsWindows() ? new WindowsPlatform(width, height) :
        OperatingSystem.IsLinux() ? new X11Platform(width, height) :
        OperatingSystem.IsMacOS() ? new MacPlatform(width, height) :
        throw new PlatformNotSupportedException("Desktop backends support Windows, Linux/X11 and macOS.");

    public static Buttons Key(uint key) => key switch
    {
        'w' or 'W' or 0xff52 => Buttons.Forward,
        's' or 'S' or 0xff54 => Buttons.Back,
        'a' or 'A' => Buttons.Left,
        'd' or 'D' => Buttons.Right,
        0xff51 => Buttons.TurnLeft,
        0xff53 => Buttons.TurnRight,
        ' ' or 'e' or 'E' => Buttons.Use,
        0xffe1 or 0xffe2 => Buttons.Run,
        0xffe3 or 0xffe4 => Buttons.Fire,
        'r' or 'R' => Buttons.Restart,
        '1' => Buttons.Pistol,
        '2' => Buttons.Shotgun,
        '\t' => Buttons.Map,
        _ => Buttons.None
    };
}

sealed unsafe class X11Platform : IPlatform
{
    const string Lib = "libX11.so.6";
    readonly nint display, gc, image, data;
    readonly nuint window, deleteAtom;
    readonly int width, height, screenWidth, screenHeight;
    bool captured;
    Input? current;
    public X11Platform(int width, int height)
    {
        this.width = width; this.height = height;
        screenWidth = width * 3 / 2; screenHeight = height * 3 / 2;
        display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("Cannot open X11 display. Set DISPLAY or use --headless. Wayland requires XWayland.");
        int screen = XDefaultScreen(display);
        window = XCreateSimpleWindow(display, XRootWindow(display, screen), 0, 0,
            (uint)screenWidth, (uint)screenHeight, 0, 0, 0);
        XStoreName(display, window, "DoomSharp - click to capture mouse, Esc to quit");
        XSelectInput(display, window, (1L << 0) | (1L << 1) | (1L << 2) | (1L << 3) | (1L << 6) | (1L << 15) | (1L << 21));
        deleteAtom = XInternAtom(display, "WM_DELETE_WINDOW", 0);
        nuint atom = deleteAtom;
        XSetWMProtocols(display, window, &atom, 1);
        byte* hints = stackalloc byte[80];
        new Span<byte>(hints, 80).Clear();
        *(long*)hints = (1L << 4) | (1L << 5);
        *(int*)(hints + 24) = *(int*)(hints + 32) = screenWidth;
        *(int*)(hints + 28) = *(int*)(hints + 36) = screenHeight;
        XSetWMNormalHints(display, window, hints);
        XkbSetDetectableAutoRepeat(display, 1, out _);
        XMapWindow(display, window);
        gc = XCreateGC(display, window, 0, 0);
        int depth = XDefaultDepth(display, screen);
        if (depth is not (24 or 32)) throw new PlatformNotSupportedException($"X11 TrueColor 24/32-bit display required; found {depth}.");
        data = Marshal.AllocHGlobal(screenWidth * screenHeight * 4);
        image = XCreateImage(display, XDefaultVisual(display, screen), (uint)depth, 2, 0, data,
            (uint)screenWidth, (uint)screenHeight, 32, screenWidth * 4);
        if (image == 0) throw new InvalidOperationException("XCreateImage failed.");
        XFlush(display);
    }

    public void Poll(Input input)
    {
        current = input;
        byte* e = stackalloc byte[192];
        while (XPending(display) > 0)
        {
            XNextEvent(display, e);
            int type = *(int*)e;
            if (type is 2 or 3)
            {
                uint key = (uint)XLookupKeysym(e, 0);
                if (key is 0xff09 or 0xfe20) key = '\t';
                if (key == 0xff1b && type == 2) input.Quit = true;
                input.Set(Platform.Key(key), type == 2);
            }
            else if (type is 4 or 5 && *(uint*)(e + 84) == 1)
            {
                if (!captured && type == 4)
                {
                    int status = XGrabPointer(display, window, 1, (uint)((1 << 2) | (1 << 3) | (1 << 6)), 1, 1, window, 0, 0);
                    if (status != 0) Console.Error.WriteLine($"X11 mouse capture failed ({status}); keyboard controls remain available.");
                    else { captured = true; CenterMouse(); }
                }
                else input.Set(Buttons.Fire, type == 4);
            }
            else if (type == 6 && captured)
            {
                int x = *(int*)(e + 64);
                if (x != screenWidth / 2) { input.MouseX += x - screenWidth / 2; CenterMouse(); }
            }
            else if (type == 10) { input.Held = Buttons.None; captured = false; XUngrabPointer(display, 0); }
            else if (type == 33 && *(nuint*)(e + 56) == deleteAtom) input.Quit = true;
        }
    }

    void CenterMouse() => XWarpPointer(display, 0, window, 0, 0, 0, 0, screenWidth / 2, screenHeight / 2);

    public void Present(int[] pixels)
    {
        int* buffer = (int*)data;
        for (int y = 0; y < screenHeight; y++)
            for (int x = 0; x < screenWidth; x++) buffer[y * screenWidth + x] = pixels[(y * height / screenHeight) * width + x * width / screenWidth];
        XPutImage(display, window, gc, image, 0, 0, 0, 0, (uint)screenWidth, (uint)screenHeight);
        XFlush(display);
    }

    public void Dispose()
    {
        if (captured) XUngrabPointer(display, 0);
        if (current != null) current.Held = Buttons.None;
        XDestroyImage(image); // XDestroyImage owns and frees its data buffer.
        XFreeGC(display, gc); XDestroyWindow(display, window); XCloseDisplay(display);
    }

    [DllImport(Lib)] static extern nint XOpenDisplay(nint name);
    [DllImport(Lib)] static extern int XDefaultScreen(nint d);
    [DllImport(Lib)] static extern nuint XRootWindow(nint d, int s);
    [DllImport(Lib)] static extern int XDefaultDepth(nint d, int s);
    [DllImport(Lib)] static extern nint XDefaultVisual(nint d, int s);
    [DllImport(Lib)] static extern nuint XCreateSimpleWindow(nint d, nuint parent, int x, int y, uint w, uint h, uint border, nuint borderPixel, nuint background);
    [DllImport(Lib)] static extern int XStoreName(nint d, nuint w, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(Lib)] static extern int XSelectInput(nint d, nuint w, long mask);
    [DllImport(Lib)] static extern nuint XInternAtom(nint d, [MarshalAs(UnmanagedType.LPStr)] string name, int only);
    [DllImport(Lib)] static extern int XSetWMProtocols(nint d, nuint w, nuint* protocols, int count);
    [DllImport(Lib)] static extern void XSetWMNormalHints(nint d, nuint w, void* hints);
    [DllImport(Lib)] static extern int XkbSetDetectableAutoRepeat(nint d, int detectable, out int supported);
    [DllImport(Lib)] static extern int XMapWindow(nint d, nuint w);
    [DllImport(Lib)] static extern nint XCreateGC(nint d, nuint w, nuint mask, nint values);
    [DllImport(Lib)] static extern nint XCreateImage(nint d, nint visual, uint depth, int format, int offset, nint data, uint width, uint height, int pad, int stride);
    [DllImport(Lib)] static extern int XPutImage(nint d, nuint w, nint gc, nint image, int sx, int sy, int dx, int dy, uint width, uint height);
    [DllImport(Lib)] static extern int XPending(nint d);
    [DllImport(Lib)] static extern int XNextEvent(nint d, void* e);
    [DllImport(Lib)] static extern nuint XLookupKeysym(void* e, int index);
    [DllImport(Lib)] static extern int XGrabPointer(nint d, nuint w, int owner, uint mask, int pointerMode, int keyboardMode, nuint confine, nuint cursor, nuint time);
    [DllImport(Lib)] static extern int XUngrabPointer(nint d, nuint time);
    [DllImport(Lib)] static extern int XWarpPointer(nint d, nuint src, nuint dest, int sx, int sy, uint sw, uint sh, int dx, int dy);
    [DllImport(Lib)] static extern int XFlush(nint d);
    [DllImport(Lib)] static extern int XDestroyImage(nint image);
    [DllImport(Lib)] static extern int XFreeGC(nint d, nint gc);
    [DllImport(Lib)] static extern int XDestroyWindow(nint d, nuint w);
    [DllImport(Lib)] static extern int XCloseDisplay(nint d);
}

sealed unsafe class WindowsPlatform : IPlatform
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate nint WindowProc(nint window, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential)]
    struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background, MenuName, ClassName, SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Msg { public nint Window; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPixels, YPixels;
        public uint Colors, Important;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    readonly WindowProc procedure;
    readonly nint window, dc, className;
    readonly int width, height;
    BitmapInfo bitmap;
    Input? input;
    bool captured, closed;
    const uint Style = 0x00cf0000;

    public WindowsPlatform(int width, int height)
    {
        this.width = width; this.height = height; procedure = WndProc;
        className = Marshal.StringToHGlobalUni("DoomSharpWindow");
        WindowClass c = new()
        {
            Size = (uint)sizeof(WindowClass), Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
            Instance = GetModuleHandleW(0), ClassName = className, Cursor = LoadCursorW(0, 32512)
        };
        if (RegisterClassExW(ref c) == 0) throw new InvalidOperationException($"RegisterClass failed: {Marshal.GetLastWin32Error()}");
        Rect rect = new() { Right = width * 3 / 2, Bottom = height * 3 / 2 };
        AdjustWindowRect(ref rect, Style, false);
        window = CreateWindowExW(0, className, "DoomSharp - click to capture mouse, Esc to quit", Style,
            int.MinValue, int.MinValue, rect.Right - rect.Left, rect.Bottom - rect.Top, 0, 0, c.Instance, 0);
        if (window == 0) throw new InvalidOperationException($"CreateWindow failed: {Marshal.GetLastWin32Error()}");
        dc = GetDC(window);
        bitmap = new() { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
        ShowWindow(window, 5);
    }

    nint WndProc(nint w, uint m, nuint wp, nint lp)
    {
        if (m == 0x10) { closed = true; return 0; }
        if (input != null)
        {
            if (m is 0x100 or 0x101 or 0x104 or 0x105)
            {
                bool down = m is 0x100 or 0x104;
                uint key = (uint)wp;
                if (key == 27 && down) input.Quit = true;
                Buttons button = key switch
                {
                    38 => Buttons.Forward, 40 => Buttons.Back, 37 => Buttons.TurnLeft, 39 => Buttons.TurnRight,
                    16 => Buttons.Run, 17 => Buttons.Fire, _ => Platform.Key(key)
                };
                input.Set(button, down); return 0;
            }
            if (m == 0x201)
            {
                if (!captured) { captured = true; SetCapture(w); ShowCursor(false); CenterMouse(); }
                else input.Set(Buttons.Fire, true);
            }
            if (m == 0x202) input.Set(Buttons.Fire, false);
            if (m == 0x200 && captured)
            {
                GetClientRect(w, out Rect r);
                int x = (short)((long)lp & 65535);
                if (x != r.Right / 2) { input.MouseX += x - r.Right / 2; CenterMouse(); }
            }
            if (m == 8) { input.Held = Buttons.None; ReleaseMouse(); }
        }
        return DefWindowProcW(w, m, wp, lp);
    }

    void CenterMouse()
    {
        GetClientRect(window, out Rect r);
        int* point = stackalloc int[2]; point[0] = r.Right / 2; point[1] = r.Bottom / 2;
        ClientToScreen(window, point); SetCursorPos(point[0], point[1]);
    }
    void ReleaseMouse() { if (captured) { ReleaseCapture(); ShowCursor(true); captured = false; } }
    public void Poll(Input input)
    {
        this.input = input;
        while (PeekMessageW(out Msg message, 0, 0, 0, 1))
        {
            TranslateMessage(ref message); DispatchMessageW(ref message);
        }
        input.Quit |= closed;
    }
    public void Present(int[] pixels)
    {
        GetClientRect(window, out Rect r);
        fixed (int* p = pixels)
            if (StretchDIBits(dc, 0, 0, r.Right, r.Bottom, 0, 0, width, height, p, ref bitmap, 0, 0x00cc0020) == -1)
                throw new InvalidOperationException("StretchDIBits failed.");
    }
    public void Dispose()
    {
        ReleaseMouse(); ReleaseDC(window, dc); DestroyWindow(window);
        UnregisterClassW(className, GetModuleHandleW(0)); Marshal.FreeHGlobal(className);
        GC.KeepAlive(procedure);
    }

    [DllImport("kernel32")] static extern nint GetModuleHandleW(nint name);
    [DllImport("user32", SetLastError = true)] static extern ushort RegisterClassExW(ref WindowClass c);
    [DllImport("user32", SetLastError = true)] static extern nint CreateWindowExW(uint ex, nint className, [MarshalAs(UnmanagedType.LPWStr)] string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32")] static extern nint LoadCursorW(nint instance, nint cursor);
    [DllImport("user32")] static extern bool AdjustWindowRect(ref Rect rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu);
    [DllImport("user32")] static extern bool ShowWindow(nint w, int command);
    [DllImport("user32")] static extern nint GetDC(nint w);
    [DllImport("user32")] static extern int ReleaseDC(nint w, nint dc);
    [DllImport("user32")] static extern bool DestroyWindow(nint w);
    [DllImport("user32")] static extern bool UnregisterClassW(nint name, nint instance);
    [DllImport("user32")] static extern nint DefWindowProcW(nint w, uint m, nuint wp, nint lp);
    [DllImport("user32")] static extern bool PeekMessageW(out Msg msg, nint w, uint min, uint max, uint remove);
    [DllImport("user32")] static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32")] static extern nint DispatchMessageW(ref Msg msg);
    [DllImport("user32")] static extern bool GetClientRect(nint w, out Rect rect);
    [DllImport("user32")] static extern bool ClientToScreen(nint w, int* point);
    [DllImport("user32")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] static extern nint SetCapture(nint w);
    [DllImport("user32")] static extern bool ReleaseCapture();
    [DllImport("user32")] static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
    [DllImport("gdi32")] static extern int StretchDIBits(nint dc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, void* bits, ref BitmapInfo info, uint usage, uint operation);
}
