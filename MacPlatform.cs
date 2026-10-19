using System.Runtime.InteropServices;

namespace DoomSharp;

sealed unsafe class MacPlatform : IPlatform
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public double X, Y, Width, Height; }
    readonly nint pool, app, window, view, layer, colorSpace;
    readonly int width, height;
    bool captured;
    public MacPlatform(int width, int height)
    {
        this.width = width; this.height = height;
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
        pool = Send(Send(Class("NSAutoreleasePool"), "alloc"), "init");
        app = Send(Class("NSApplication"), "sharedApplication");
        SendInt(app, Sel("setActivationPolicy:"), 0);
        window = InitWindow(Send(Class("NSWindow"), "alloc"), Sel("initWithContentRect:styleMask:backing:defer:"),
            new Rect { Width = width * 1.5, Height = height * 1.5 }, 1 | 2 | 4, 2, false);
        if (window == 0) throw new InvalidOperationException("Cannot create macOS NSWindow.");
        SendArg(window, Sel("setTitle:"), String("DoomSharp - click for mouse, Esc to quit"));
        SendBool(window, Sel("setReleasedWhenClosed:"), false);
        view = Send(window, "contentView");
        SendBool(view, Sel("setWantsLayer:"), true);
        layer = Send(view, "layer");
        SendArg(window, Sel("makeKeyAndOrderFront:"), 0);
        SendBool(app, Sel("activateIgnoringOtherApps:"), true);
        Send(app, "finishLaunching");
        colorSpace = CGColorSpaceCreateDeviceRGB();
    }
    static nint Class(string name) => objc_getClass(name);
    static nint Sel(string name) => sel_registerName(name);
    static nint Send(nint o, string selector) => Send0(o, Sel(selector));
    static nint String(string text)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text + '\0');
        fixed (byte* p = utf8) return SendArg(Class("NSString"), Sel("stringWithUTF8String:"), (nint)p);
    }

    public void Poll(Input input)
    {
        nint eventPool = Send(Send(Class("NSAutoreleasePool"), "alloc"), "init");
        try
        {
            nint e;
            while ((e = NextEvent(app, Sel("nextEventMatchingMask:untilDate:inMode:dequeue:"),
                nuint.MaxValue, Send(Class("NSDate"), "distantPast"), String("kCFRunLoopDefaultMode"), true)) != 0)
            {
                nuint type = (nuint)Send(e, "type");
                if (type is 10 or 11)
                {
                    ushort key = (ushort)Send(e, "keyCode");
                    if (key == 53 && type == 10) input.Quit = true;
                    Buttons button = key switch
                    {
                        13 => Buttons.Forward, 1 => Buttons.Back, 0 => Buttons.Left, 2 => Buttons.Right,
                        123 => Buttons.TurnLeft, 124 => Buttons.TurnRight, 126 => Buttons.Forward, 125 => Buttons.Back,
                        49 or 14 => Buttons.Use, 15 => Buttons.Restart, 18 => Buttons.Pistol, 19 => Buttons.Shotgun,
                        20 => Buttons.Chainsaw, 21 => Buttons.Chaingun, 23 => Buttons.RocketLauncher,
                        22 => Buttons.PlasmaRifle, 26 => Buttons.BFG9000, 48 => Buttons.Map,
                        96 => Buttons.Save, 101 => Buttons.Load, _ => Buttons.None
                    };
                    input.Set(button, type == 10);
                }
                else if (type == 12)
                {
                    nuint modifiers = (nuint)Send(e, "modifierFlags");
                    input.Set(Buttons.Run, (modifiers & (1 << 17)) != 0);
                    input.Set(Buttons.Fire, (modifiers & (1 << 18)) != 0);
                }
                else if (type == 1)
                {
                    if (!captured) { captured = true; CGAssociateMouseAndMouseCursorPosition(false); CGDisplayHideCursor(0); }
                    else input.Set(Buttons.Fire, true);
                }
                else if (type == 2) input.Set(Buttons.Fire, false);
                else if (type is 5 or 6 && captured) input.MouseX += SendDouble(e, Sel("deltaX"));
                SendArg(app, Sel("sendEvent:"), e);
            }
            Send(app, "updateWindows");
            if (Send(window, "isVisible") == 0) input.Quit = true;
            if (Send(window, "isKeyWindow") == 0)
            {
                input.Held = Buttons.None;
                ReleaseMouse();
            }
        }
        finally { Send(eventPool, "drain"); }
    }

    public void Present(int[] pixels)
    {
        fixed (int* data = pixels)
        {
            nint copy = CFDataCreate(0, data, pixels.Length * 4);
            nint provider = CGDataProviderCreateWithCFData(copy);
            CFRelease(copy);
            // Managed 0x00RRGGBB pixels are little-endian B,G,R,unused on supported desktop CPUs.
            nint image = CGImageCreate((nuint)width, (nuint)height, 8, 32, (nuint)(width * 4), colorSpace,
                0x2000 | 6, provider, 0, false, 0);
            if (image == 0) { CGDataProviderRelease(provider); throw new InvalidOperationException("CGImageCreate failed."); }
            SendBool(Class("CATransaction"), Sel("setDisableActions:"), true);
            SendArg(layer, Sel("setContents:"), image);
            Send(Class("CATransaction"), "flush");
            CGImageRelease(image); CGDataProviderRelease(provider);
        }
    }
    void ReleaseMouse()
    {
        if (!captured) return;
        CGAssociateMouseAndMouseCursorPosition(true); CGDisplayShowCursor(0); captured = false;
    }
    public void Dispose()
    {
        ReleaseMouse(); SendArg(layer, Sel("setContents:"), 0); Send(window, "close");
        Send(window, "release"); CGColorSpaceRelease(colorSpace); Send(pool, "drain");
    }
    [DllImport(ObjC)] static extern nint objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(ObjC)] static extern nint sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send0(nint o, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint SendArg(nint o, nint selector, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendInt(nint o, nint selector, int arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendBool(nint o, nint selector, [MarshalAs(UnmanagedType.I1)] bool arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern double SendDouble(nint o, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint InitWindow(nint o, nint selector, Rect rect, nuint style, nuint backing, [MarshalAs(UnmanagedType.I1)] bool defer);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint NextEvent(nint o, nint selector, nuint mask, nint date, nint mode, [MarshalAs(UnmanagedType.I1)] bool dequeue);
    [DllImport(Graphics)] static extern nint CGColorSpaceCreateDeviceRGB();
    [DllImport(Graphics)] static extern void CGColorSpaceRelease(nint space);
    [DllImport(Foundation)] static extern nint CFDataCreate(nint allocator, void* data, nint length);
    [DllImport(Foundation)] static extern void CFRelease(nint value);
    [DllImport(Graphics)] static extern nint CGDataProviderCreateWithCFData(nint data);
    [DllImport(Graphics)] static extern void CGDataProviderRelease(nint provider);
    [DllImport(Graphics)] static extern nint CGImageCreate(nuint w, nuint h, nuint bits, nuint pixelBits, nuint stride, nint space, uint info, nint provider, nint decode, [MarshalAs(UnmanagedType.I1)] bool interpolate, int intent);
    [DllImport(Graphics)] static extern void CGImageRelease(nint image);
    [DllImport(Graphics)] static extern int CGAssociateMouseAndMouseCursorPosition([MarshalAs(UnmanagedType.I1)] bool associate);
    [DllImport(Graphics)] static extern int CGDisplayHideCursor(uint display);
    [DllImport(Graphics)] static extern int CGDisplayShowCursor(uint display);
}
